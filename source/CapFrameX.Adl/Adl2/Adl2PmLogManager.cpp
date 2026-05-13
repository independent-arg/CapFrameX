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
    // First context — the one we hand to ADLX via InitializeWithCallerAdl
    // so that IADLMapping can translate ADLX indices to ADL adapter indices.
    void*                                   g_adlCtx = nullptr;
    // Second context — used exclusively for PMLog calls. AMD's driver
    // appears to bind PMLog to whichever context first "claimed" the
    // adapter, and ADLX's StartPerformanceMetricsTracking on g_adlCtx
    // makes a subsequent PMLog_Start on the same context return ADL_ERR
    // on RDNA 4 (confirmed on RX 9070 XT). Using a separate context for
    // PMLog sidesteps that conflict; the adapter index from the IADLMapping
    // bridge is universal across ADL2 contexts.
    void*                                   g_adlCtxPmLog = nullptr;
    bool                                    g_initFailed = false;
    // Diagnostics for the pre-emptive Start-before-ADLX experiment. If the
    // race hypothesis is right, preemptiveStartedCount > 0 indicates we
    // claimed PMLog before ADLX could.
    int                                     g_preemptiveAdapterCount = -1;
    int                                     g_preemptiveStartedCount = 0;

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
        // Last raw ADL return codes from the start sequence — surfaced via
        // Adl2PmLogData diagnostics for triage on systems where PMLog is
        // advertised but refuses to start.
        int supportResultCode = 0;
        int startResultCode = 0;
        int supportSensorCount = 0;
        int successfulSampleRate = 0;
        bool supportCached = false;
        unsigned short supportedSensors[ADL_PMLOG_MAX_SUPPORTED_SENSORS] = {};
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
            if (kv.second.started && p_ADL2_Adapter_PMLog_Stop && g_adlCtxPmLog)
            {
                p_ADL2_Adapter_PMLog_Stop(g_adlCtxPmLog, kv.first, nullptr);
            }
        }
        g_tracking.clear();

        if (g_adlCtxPmLog && p_ADL2_Main_Control_Destroy)
        {
            p_ADL2_Main_Control_Destroy(g_adlCtxPmLog);
        }
        g_adlCtxPmLog = nullptr;

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

        // Create the first ADL2 master context — this one is handed to ADLX
        // via InitializeWithCallerAdl so the IADLMapping bridge can resolve
        // ADLX→ADL adapter indices. The second arg enables enumeration of
        // unattached/inactive adapters too.
        if (p_ADL2_Main_Control_Create(AdlMallocCallback, 1, &g_adlCtx) != ADL_OK || !g_adlCtx)
        {
            Cleanup();
            g_initFailed = true;
            return false;
        }

        // Create a SECOND context exclusively for PMLog. ADLX's
        // StartPerformanceMetricsTracking marks g_adlCtx as the owner of the
        // adapter's telemetry session, which makes a subsequent
        // ADL2_Adapter_PMLog_Start on the same context fail with ADL_ERR on
        // RDNA 4 (RX 9070 XT). The second context behaves like HWiNFO's
        // single-tenant context.
        // iEnumConnectedAdapters = 0 here (only connected/active adapters)
        // instead of 1: HWiNFO's call uses the register-stored value that
        // the RE didn't fully recover, and 0 is the more conservative AMD
        // sample-code default — some PMLog driver paths key off whether the
        // adapter is in the "connected" set, which differs between 0 and 1.
        if (p_ADL2_Main_Control_Create(AdlMallocCallback, 0, &g_adlCtxPmLog) != ADL_OK || !g_adlCtxPmLog)
            g_adlCtxPmLog = nullptr;

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

    // All PMLog calls go through the dedicated context, falling back to the
    // ADLX-shared one when the secondary create failed.
    static void* PmLogCtx()
    {
        return g_adlCtxPmLog ? g_adlCtxPmLog : g_adlCtx;
    }

    bool StartTracking(int adlAdapterIndex)
    {
        std::lock_guard<std::mutex> lock(g_mutex);

        void* ctx = PmLogCtx();
        if (!ctx || adlAdapterIndex < 0)
            return false;
        if (!p_ADL2_Adapter_PMLog_Support_Get || !p_ADL2_Adapter_PMLog_Start)
            return false;

        auto& state = g_tracking[adlAdapterIndex];
        if (state.started)
            return true;

        // Only call PMLog_Support_Get once per adapter — HWiNFO does the
        // same. A second call after the first appears to put the driver
        // into a state where PMLog_Start returns ADL_ERR. The first call
        // happened either from GetPmLogSupport (constructor activation) or
        // from a previous StartTracking attempt; either way the result is
        // already cached in `state`.
        if (!state.supportCached)
        {
            ADLPMLogSupportInfo support = {};
            state.supportResultCode = p_ADL2_Adapter_PMLog_Support_Get(ctx, adlAdapterIndex, &support);
            if (state.supportResultCode != ADL_OK)
                return false;
            for (int i = 0; i < ADL_PMLOG_MAX_SUPPORTED_SENSORS; ++i)
                state.supportedSensors[i] = support.usSensors[i];
            state.supportCached = true;
        }

        // HWiNFO calls New_QueryPMLogData_Get exactly once BEFORE PMLog_Start
        // (VA 0x1403D4791 right before 0x1403D48B2). It looks superficially
        // like a probe but on RDNA 4 it appears to be a required init step —
        // PMLog_Start refuses without it. The return code and data are
        // discarded; only the side effect on driver state matters.
        // (HWiNFO does NOT call PMLog_Stop before Start; doing so on a fresh
        // session apparently puts some Adrenalin builds into the same
        // ADL_ERR state we are trying to escape.)
        if (p_ADL2_New_QueryPMLogData_Get)
        {
            ADLPMLogData probe = {};
            probe.ulVersion = 1;
            p_ADL2_New_QueryPMLogData_Get(ctx, adlAdapterIndex, &probe);
        }

        // Build the start payload — full driver-advertised sensor list,
        // verbatim, with explicit 0-terminator at the end. Matches HWiNFO's
        // single-shot invocation byte-for-byte.
        ADLPMLogStartInput input = {};
        int copyCount = 0;
        for (; copyCount < ADL_PMLOG_MAX_SUPPORTED_SENSORS - 1; ++copyCount)
        {
            unsigned short s = state.supportedSensors[copyCount];
            input.usSensors[copyCount] = s;
            if (s == ADL_SENSOR_MAXTYPES)
                break;
        }
        // Explicit terminator (HWiNFO appends one even when the support list
        // already ended on its own sentinel).
        if (copyCount < ADL_PMLOG_MAX_SUPPORTED_SENSORS)
            input.usSensors[copyCount] = ADL_SENSOR_MAXTYPES;
        state.supportSensorCount = copyCount;
        if (copyCount == 0)
            return false;
        input.ulSampleRate = 1000;

        ADLPMLogStartOutput output = {};
        state.startResultCode = p_ADL2_Adapter_PMLog_Start(ctx, adlAdapterIndex, &input, &output, nullptr);
        state.successfulSampleRate = 1000;
        if (state.startResultCode != ADL_OK)
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
        void* ctx = PmLogCtx();
        if (ctx && p_ADL2_Adapter_PMLog_Stop)
        {
            p_ADL2_Adapter_PMLog_Stop(ctx, adlAdapterIndex, nullptr);
        }
        it->second.started = false;
    }

    int PreemptiveStartAll()
    {
        g_preemptiveAdapterCount = -1;
        g_preemptiveStartedCount = 0;

        if (!g_adlCtx || !p_ADL2_Adapter_NumberOfAdapters_Get)
            return 0;

        int numAdapters = 0;
        if (p_ADL2_Adapter_NumberOfAdapters_Get(g_adlCtx, &numAdapters) != ADL_OK || numAdapters <= 0)
        {
            g_preemptiveAdapterCount = numAdapters;
            return 0;
        }

        g_preemptiveAdapterCount = numAdapters;
        for (int i = 0; i < numAdapters; ++i)
        {
            if (StartTracking(i))
                ++g_preemptiveStartedCount;
        }
        return g_preemptiveStartedCount;
    }

    bool GetPmLogSupport(int adlAdapterIndex, Adl2PmLogSupport* out)
    {
        if (!out) return false;
        memset(out, 0, sizeof(*out));

        void* ctx = PmLogCtx();
        if (!ctx || adlAdapterIndex < 0)
            return false;
        if (!p_ADL2_Adapter_PMLog_Support_Get)
            return false;

        std::lock_guard<std::mutex> lock(g_mutex);
        auto& state = g_tracking[adlAdapterIndex];

        // Cache the support list in the tracking state so StartTracking
        // can reuse it without invoking the driver again. Calling
        // PMLog_Support_Get a second time appears to put the driver into
        // a state where PMLog_Start returns ADL_ERR on RDNA 4.
        if (!state.supportCached)
        {
            ADLPMLogSupportInfo info = {};
            state.supportResultCode = p_ADL2_Adapter_PMLog_Support_Get(ctx, adlAdapterIndex, &info);
            if (state.supportResultCode != ADL_OK)
                return false;
            for (int i = 0; i < ADL_PMLOG_MAX_SUPPORTED_SENSORS; ++i)
                state.supportedSensors[i] = info.usSensors[i];
            state.supportCached = true;
        }

        int n = 0;
        for (int i = 0; i < ADL_PMLOG_MAX_SUPPORTED_SENSORS && n < ADL2_PMLOG_MAX_VALUES; ++i)
        {
            unsigned short s = state.supportedSensors[i];
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
        out->startStatus = -1;

        if (!PmLogCtx() || adlAdapterIndex < 0)
            return false;

        // Lazily start tracking on first sample. After PMLog_Start
        // succeeds the driver maps a shared buffer at
        // state.out.ptr_LoggingAddress that we read below.
        bool started = StartTracking(adlAdapterIndex);
        out->startStatus = started ? 1 : 0;
        {
            std::lock_guard<std::mutex> lock(g_mutex);
            out->contextMode = (g_adlCtxPmLog != nullptr) ? 1 : 0;
            out->preemptiveAdapterCount = g_preemptiveAdapterCount;
            out->preemptiveStartedCount = g_preemptiveStartedCount;
            auto it = g_tracking.find(adlAdapterIndex);
            if (it != g_tracking.end())
            {
                out->supportResultCode = it->second.supportResultCode;
                out->startResultCode = it->second.startResultCode;
                out->supportSensorCount = it->second.supportSensorCount;
                out->successfulSampleRate = it->second.successfulSampleRate;
                out->loggingAddress = reinterpret_cast<unsigned long long>(it->second.out.ptr_LoggingAddress);
                for (int i = 0; i < ADL2_PMLOG_RAW_DUMP_COUNT; ++i)
                    out->supportedSensorIds[i] = static_cast<int>(it->second.supportedSensors[i]);
            }
        }
        if (!started)
            return false;

        // HWiNFO uses the shared-memory transport, not the polling API.
        // After ADL2_Adapter_PMLog_Start succeeds the driver continuously
        // writes a live ADLPMLogData snapshot to output.ptr_LoggingAddress;
        // ADL2_New_QueryPMLogData_Get only returns header/support flags
        // (sensor IDs in [0] with 0/1 in [1]). Read directly from the
        // mapped buffer instead — reverse-engineered from
        // HWiNFO64.unpacked.exe VA 0x1403D48B2 (PMLog_Start call) and
        // 0x140ABDFF8 (the resolved entry point slot).
        const ADLPMLogData* mapped = nullptr;
        {
            std::lock_guard<std::mutex> lock(g_mutex);
            auto it = g_tracking.find(adlAdapterIndex);
            if (it != g_tracking.end() && it->second.started)
                mapped = static_cast<const ADLPMLogData*>(it->second.out.ptr_LoggingAddress);
        }
        if (mapped == nullptr)
            return false;

        // Snapshot the relevant fields into a local so the loop below sees
        // a stable view even though the driver keeps writing to the buffer.
        ADLPMLogData data;
        memcpy(&data, mapped, sizeof(data));

        out->supported = true;
        out->sampleRate = static_cast<int>(data.ulActiveSampleRate);
        out->driverSensorCount = static_cast<int>(data.ulSensors);
        out->lastUpdated = static_cast<long long>(data.ulLastUpdated);

        // Raw dump of the first N rows so the report can still verify the
        // shared-memory layout independently of our interpretation.
        for (int i = 0; i < ADL2_PMLOG_RAW_DUMP_COUNT; ++i)
        {
            out->rawValues[i][0] = data.ulValues[i][0];
            out->rawValues[i][1] = data.ulValues[i][1];
        }

        int n = 0;
        for (int i = 0; i < ADL_PMLOG_MAX_SUPPORTED_SENSORS && n < ADL2_PMLOG_MAX_VALUES; ++i)
        {
            unsigned int sensorIdx = data.ulValues[i][0];
            if (sensorIdx == 0xFFFFFFFFu || sensorIdx == ADL_SENSOR_MAXTYPES)
                continue;
            if (sensorIdx >= static_cast<unsigned int>(ADL_PMLOG_MAX_SENSORS_REAL))
                continue;
            out->entries[n].sensorIndex = static_cast<int>(sensorIdx);
            out->entries[n].value = static_cast<int>(data.ulValues[i][1]);
            ++n;
        }
        out->entryCount = n;
        return true;
    }
}
