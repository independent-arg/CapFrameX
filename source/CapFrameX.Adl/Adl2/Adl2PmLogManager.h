// Adl2PmLogManager.h
//
// Second telemetry source that complements ADLX with the data AMD exposes
// through the legacy ADL2 PMLog interface (atiadlxx.dll). ADLX remains
// authoritative for every sensor it covers; this module only fills the
// gaps (SoC clock, fabric clock, multimedia clocks, VR temperatures,
// per-rail power, throttle status, PCIe link state).

#pragma once

#include "adl2_pmlog_min.h"

#define ADL2_PMLOG_MAX_VALUES  256

// Default natural packing. C# side mirrors with [StructLayout(Sequential)]
// and explicit field offsets are unnecessary as long as the field types
// match exactly (bool ⇄ MarshalAs(I1), int ⇄ int).
typedef struct Adl2PmLogEntry
{
    int sensorIndex;   // ADL_PMLOG_* enum value (0 if entry unused)
    int value;         // raw sensor reading; unit depends on sensorIndex
} Adl2PmLogEntry;

typedef struct Adl2PmLogSupport
{
    bool supported;                                  // whether PMLog is supported at all
    int  sensorCount;                                // entries in supportedSensors
    int  supportedSensors[ADL2_PMLOG_MAX_VALUES];    // ADL_PMLOG_* indices the GPU reports
} Adl2PmLogSupport;

#define ADL2_PMLOG_RAW_DUMP_COUNT 48

typedef struct Adl2PmLogData
{
    bool supported;                                  // mirrors Adl2PmLogSupport.supported
    int  sampleRate;                                 // active sample rate in ms
    int  entryCount;                                 // valid pairs in entries[]
    // --- Diagnostics. Populated even on partial/failed reads so the C#
    // side can include them in GetReport(). startStatus < 0 means
    // StartTracking was never attempted; 0 means it failed; 1 means OK.
    int  startStatus;
    int  driverSensorCount;                          // raw ulSensors from the driver
    long long lastUpdated;                           // raw ulLastUpdated (microseconds)
    // Raw ADL_OK/ADL_ERR return codes from the last attempt so we can tell
    // why PMLog_Start refused (0 = ADL_OK, -8 = not supported, -3 = bad param, …).
    int  supportResultCode;                          // ADL2_Adapter_PMLog_Support_Get
    int  startResultCode;                            // ADL2_Adapter_PMLog_Start (last successful or last attempted)
    int  supportSensorCount;                         // how many sensors we forwarded to PMLog_Start
    int  successfulSampleRate;                       // sample rate that finally worked (or last tried)
    int  contextMode;                                // 0=primary (ADLX-shared), 1=dedicated PMLog ctx
    int  preemptiveAdapterCount;                     // adapter count enumerated for pre-emptive Start (-1 = not tried)
    int  preemptiveStartedCount;                     // adapters for which the pre-emptive Start succeeded
    unsigned long long loggingAddress;               // ptr_LoggingAddress from PMLog_Start
    // First N supported sensor IDs as returned by PMLog_Support_Get. Filled
    // even when PMLog_Start fails so the report can show what the driver
    // advertised. Trailing slots are zero.
    int  supportedSensorIds[ADL2_PMLOG_RAW_DUMP_COUNT];
    // First N raw rows of ADLPMLogData.ulValues[][2] — kept so the report
    // can show exactly what the driver returned without depending on our
    // interpretation. Useful when values look like flags (0/1) instead of
    // real measurements.
    unsigned int rawValues[ADL2_PMLOG_RAW_DUMP_COUNT][2];
    Adl2PmLogEntry entries[ADL2_PMLOG_MAX_VALUES];
} Adl2PmLogData;

namespace Adl2PmLog
{
    // Loads atiadlxx.dll, resolves the entry points and creates the ADL2
    // master context. Safe to call multiple times; later calls return the
    // result of the first call.
    bool Initialize();

    // Releases the ADL2 master context and frees the loaded module.
    void Close();

    // Returns the bound ADL2 context. nullptr until Initialize succeeded.
    void* GetContext();

    // Starts PMLog tracking for the given ADL adapter index (idempotent).
    // No-op when PMLog support flags are empty.
    bool StartTracking(int adlAdapterIndex);

    // Stops PMLog tracking for the given ADL adapter index. Called during
    // shutdown; safe even when StartTracking was never called.
    void StopTracking(int adlAdapterIndex);

    // Pre-emptively starts PMLog on every adapter ADL2 sees. Called from
    // AdlManager BEFORE ADLX initialization so that PMLog claims the
    // adapter's telemetry session first. Hypothesis: on RDNA 4 the AMD
    // driver gives exclusive ownership to whichever telemetry session
    // (ADLX vs ADL2 PMLog) starts first. Returns the number of adapters
    // for which Start succeeded.
    int PreemptiveStartAll();

    // Fills *out with the list of PMLog sensor indices the GPU advertises.
    bool GetPmLogSupport(int adlAdapterIndex, Adl2PmLogSupport* out);

    // Fills *out with the most recent PMLog snapshot. The caller iterates
    // entries[0..entryCount-1] and matches sensorIndex against the
    // ADL_PMLOG_* constants defined in adl2_pmlog_min.h.
    bool GetPmLogData(int adlAdapterIndex, Adl2PmLogData* out);
}
