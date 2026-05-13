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

typedef struct Adl2PmLogData
{
    bool supported;                                  // mirrors Adl2PmLogSupport.supported
    int  sampleRate;                                 // active sample rate in ms
    int  entryCount;                                 // valid pairs in entries[]
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

    // Fills *out with the list of PMLog sensor indices the GPU advertises.
    bool GetPmLogSupport(int adlAdapterIndex, Adl2PmLogSupport* out);

    // Fills *out with the most recent PMLog snapshot. The caller iterates
    // entries[0..entryCount-1] and matches sensorIndex against the
    // ADL_PMLOG_* constants defined in adl2_pmlog_min.h.
    bool GetPmLogData(int adlAdapterIndex, Adl2PmLogData* out);
}
