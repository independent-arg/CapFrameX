// Adl2PmLogManager.cpp — see Adl2PmLogManager.h for the design rationale.

#include "Adl2PmLogManager.h"

#include <Windows.h>
#include <unordered_map>
#include <mutex>
#include <cstdlib>
#include <cstring>

namespace
{
    constexpr const wchar_t* kAdl64Dll = L"atiadlxx.dll";
    constexpr const wchar_t* kAdl32Dll = L"atiadlxy.dll";

    std::mutex                              g_mutex;
    HMODULE                                 g_module = nullptr;
    void*                                   g_adlCtx = nullptr;
    bool                                    g_initFailed = false;

    ADL2_MAIN_CONTROL_CREATE                p_ADL2_Main_Control_Create = nullptr;
    ADL2_MAIN_CONTROL_DESTROY               p_ADL2_Main_Control_Destroy = nullptr;
    ADL2_ADAPTER_NUMBEROFADAPTERS_GET       p_ADL2_Adapter_NumberOfAdapters_Get = nullptr;
    ADL2_ADAPTER_ADAPTERINFO_GET            p_ADL2_Adapter_AdapterInfo_Get = nullptr;
    ADL2_ADAPTER_ACTIVE_GET                 p_ADL2_Adapter_Active_Get = nullptr;
    ADL2_ADAPTER_PMLOG_SUPPORT_GET          p_ADL2_Adapter_PMLog_Support_Get = nullptr;
    ADL2_ADAPTER_PMLOG_START                p_ADL2_Adapter_PMLog_Start = nullptr;
    ADL2_ADAPTER_PMLOG_STOP                 p_ADL2_Adapter_PMLog_Stop = nullptr;
    ADL2_NEW_QUERYPMLOGDATA_GET             p_ADL2_New_QueryPMLogData_Get = nullptr;

    // ADL2 requires the caller to allocate and free memory for AdapterInfo
    // arrays. malloc/free is fine; we just hand the same allocator into
    // ADL2_Main_Control_Create.
    void* __stdcall AdlMallocCallback(int iSize)
    {
        return malloc(static_cast<size_t>(iSize));
    }

    struct TrackingState
    {
        bool started = false;
        ADLPMLogStartOutput out = {};
    };

    // ADL2 PMLog state must be Start/Stop'd per adapter. We keep one
    // tracking record per adapter index.
    std::unordered_map<int, TrackingState> g_tracking;

    template <typename Fn>
    bool ResolveFn(Fn& target, const char* name)
    {
        target = reinterpret_cast<Fn>(GetProcAddress(g_module, name));
        return target != nullptr;
    }

    void Cleanup()
    {
        for (auto& kv : g_tracking)
        {
            if (kv.second.started && p_ADL2_Adapter_PMLog_Stop && g_adlCtx)
            {
                p_ADL2_Adapter_PMLog_Stop(g_adlCtx, kv.first, nullptr);
            }
        }
        g_tracking.clear();

        if (g_adlCtx && p_ADL2_Main_Control_Destroy)
        {
            p_ADL2_Main_Control_Destroy(g_adlCtx);
        }
        g_adlCtx = nullptr;

        if (g_module)
        {
            FreeLibrary(g_module);
        }
        g_module = nullptr;

        p_ADL2_Main_Control_Create = nullptr;
        p_ADL2_Main_Control_Destroy = nullptr;
        p_ADL2_Adapter_NumberOfAdapters_Get = nullptr;
        p_ADL2_Adapter_AdapterInfo_Get = nullptr;
        p_ADL2_Adapter_Active_Get = nullptr;
        p_ADL2_Adapter_PMLog_Support_Get = nullptr;
        p_ADL2_Adapter_PMLog_Start = nullptr;
        p_ADL2_Adapter_PMLog_Stop = nullptr;
        p_ADL2_New_QueryPMLogData_Get = nullptr;
    }
}

namespace Adl2PmLog
{
    bool Initialize()
    {
        std::lock_guard<std::mutex> lock(g_mutex);

        if (g_adlCtx != nullptr)
            return true;
        if (g_initFailed)
            return false;

        g_module = LoadLibraryW(kAdl64Dll);
        if (!g_module)
            g_module = LoadLibraryW(kAdl32Dll);
        if (!g_module)
        {
            g_initFailed = true;
            return false;
        }

        if (!ResolveFn(p_ADL2_Main_Control_Create,            "ADL2_Main_Control_Create")            ||
            !ResolveFn(p_ADL2_Main_Control_Destroy,           "ADL2_Main_Control_Destroy")           ||
            !ResolveFn(p_ADL2_Adapter_NumberOfAdapters_Get,   "ADL2_Adapter_NumberOfAdapters_Get")   ||
            !ResolveFn(p_ADL2_Adapter_AdapterInfo_Get,        "ADL2_Adapter_AdapterInfo_Get"))
        {
            Cleanup();
            g_initFailed = true;
            return false;
        }

        // Optional entry points — PMLog is unavailable on old drivers but
        // base ADL2 still works; treat resolution failures here as soft.
        ResolveFn(p_ADL2_Adapter_Active_Get,            "ADL2_Adapter_Active_Get");
        ResolveFn(p_ADL2_Adapter_PMLog_Support_Get,     "ADL2_Adapter_PMLog_Support_Get");
        ResolveFn(p_ADL2_Adapter_PMLog_Start,           "ADL2_Adapter_PMLog_Start");
        ResolveFn(p_ADL2_Adapter_PMLog_Stop,            "ADL2_Adapter_PMLog_Stop");
        ResolveFn(p_ADL2_New_QueryPMLogData_Get,        "ADL2_New_QueryPMLogData_Get");

        // Create the ADL2 master context. The second arg enables enumeration
        // of unattached/inactive adapters too, which we need so the BDF
        // lookup covers every physical GPU.
        if (p_ADL2_Main_Control_Create(AdlMallocCallback, 1, &g_adlCtx) != ADL_OK || !g_adlCtx)
        {
            Cleanup();
            g_initFailed = true;
            return false;
        }

        return true;
    }

    void Close()
    {
        std::lock_guard<std::mutex> lock(g_mutex);
        Cleanup();
    }

    void* GetContext()
    {
        return g_adlCtx;
    }

    bool StartTracking(int adlAdapterIndex)
    {
        std::lock_guard<std::mutex> lock(g_mutex);

        if (!g_adlCtx || adlAdapterIndex < 0)
            return false;
        if (!p_ADL2_Adapter_PMLog_Support_Get || !p_ADL2_Adapter_PMLog_Start)
            return false;

        auto& state = g_tracking[adlAdapterIndex];
        if (state.started)
            return true;

        ADLPMLogSupportInfo support = {};
        if (p_ADL2_Adapter_PMLog_Support_Get(g_adlCtx, adlAdapterIndex, &support) != ADL_OK)
            return false;

        // Build the start payload: forward every supported sensor at 1000 ms
        // cadence — matches the ADLX update interval AmdGpu already uses.
        ADLPMLogStartInput input = {};
        int copyCount = 0;
        for (; copyCount < ADL_PMLOG_MAX_SUPPORTED_SENSORS; ++copyCount)
        {
            input.usSensors[copyCount] = support.usSensors[copyCount];
            if (support.usSensors[copyCount] == ADL_SENSOR_MAXTYPES)
                break;
        }
        if (copyCount == 0)
            return false;
        input.ulSampleRate = 1000;

        ADLPMLogStartOutput output = {};
        if (p_ADL2_Adapter_PMLog_Start(g_adlCtx, adlAdapterIndex, &input, &output, nullptr) != ADL_OK)
            return false;

        state.started = true;
        state.out = output;
        return true;
    }

    void StopTracking(int adlAdapterIndex)
    {
        std::lock_guard<std::mutex> lock(g_mutex);

        auto it = g_tracking.find(adlAdapterIndex);
        if (it == g_tracking.end() || !it->second.started)
            return;
        if (g_adlCtx && p_ADL2_Adapter_PMLog_Stop)
        {
            p_ADL2_Adapter_PMLog_Stop(g_adlCtx, adlAdapterIndex, nullptr);
        }
        it->second.started = false;
    }

    bool GetPmLogSupport(int adlAdapterIndex, Adl2PmLogSupport* out)
    {
        if (!out) return false;
        memset(out, 0, sizeof(*out));

        if (!g_adlCtx || adlAdapterIndex < 0)
            return false;
        if (!p_ADL2_Adapter_PMLog_Support_Get)
            return false;

        ADLPMLogSupportInfo info = {};
        if (p_ADL2_Adapter_PMLog_Support_Get(g_adlCtx, adlAdapterIndex, &info) != ADL_OK)
            return false;

        int n = 0;
        for (int i = 0; i < ADL_PMLOG_MAX_SUPPORTED_SENSORS && n < ADL2_PMLOG_MAX_VALUES; ++i)
        {
            unsigned short s = info.usSensors[i];
            if (s == ADL_SENSOR_MAXTYPES)
                break;
            out->supportedSensors[n++] = static_cast<int>(s);
        }
        out->sensorCount = n;
        out->supported = (n > 0);
        return out->supported;
    }

    bool GetPmLogData(int adlAdapterIndex, Adl2PmLogData* out)
    {
        if (!out) return false;
        memset(out, 0, sizeof(*out));

        if (!g_adlCtx || adlAdapterIndex < 0)
            return false;
        if (!p_ADL2_New_QueryPMLogData_Get)
            return false;

        // Lazily start tracking on first sample — keeps the wrapper API
        // simple (callers only need GetPmLogData / GetPmLogSupport).
        // StartTracking takes the mutex itself and returns true if tracking
        // is already running for this adapter, so this call is cheap on
        // subsequent samples.
        StartTracking(adlAdapterIndex);

        ADLPMLogData data = {};
        if (p_ADL2_New_QueryPMLogData_Get(g_adlCtx, adlAdapterIndex, &data) != ADL_OK)
            return false;

        out->supported = true;
        out->sampleRate = static_cast<int>(data.ulActiveSampleRate);

        int n = 0;
        for (int i = 0; i < ADL_PMLOG_MAX_SUPPORTED_SENSORS && n < ADL2_PMLOG_MAX_VALUES; ++i)
        {
            unsigned int sensorIdx = data.ulValues[i][0];
            // Driver marks empty slots either with 0xFFFFFFFF or with the
            // sentinel ADL_SENSOR_MAXTYPES (=0). Both end the list.
            if (sensorIdx == 0xFFFFFFFFu || sensorIdx == ADL_SENSOR_MAXTYPES)
                break;
            out->entries[n].sensorIndex = static_cast<int>(sensorIdx);
            out->entries[n].value = static_cast<int>(data.ulValues[i][1]);
            ++n;
        }
        out->entryCount = n;
        return true;
    }
}
