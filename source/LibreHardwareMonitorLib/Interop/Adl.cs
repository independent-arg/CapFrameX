// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) CapFrameX and Contributors.
// All Rights Reserved.

using System;
using System.IO;
using System.Runtime.InteropServices;

// ReSharper disable InconsistentNaming

namespace LibreHardwareMonitor.Interop;

/// <summary>
/// AMD Display Library interop wrapper. Bridges the modern ADLX COM API
/// (primary source) with the legacy ADL2 PMLog telemetry path (second
/// source for sensors ADLX does not expose).
/// </summary>
internal static class Adl
{
    public const int ATI_VENDOR_ID = 0x1002;

    private const string DllName = "CapFrameX.Adl.dll";

    private const int MAX_DRIVER_PATH_LEN = 200;
    private const int MAX_GPU_NAME_LEN = 100;
    private const int MAX_VENDOR_ID_LEN = 20;

    private static bool _dllLoaded;
    private static bool _dllLoadAttempted;

    /// <summary>
    /// GPU type enumeration matching ADLX_GPU_TYPE.
    /// </summary>
    public enum GpuType
    {
        Undefined = 0,
        Integrated = 1,
        Discrete = 2
    }

    /// <summary>
    /// Telemetry support flags structure matching AdlxTelemetrySupport in ADLXManager.h.
    /// Used to query what metrics are supported before actual data is available.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AdlxTelemetrySupport
    {
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuUsageSupported;
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuClockSpeedSupported;
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuVRAMClockSpeedSupported;
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuTemperatureSupported;
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuHotspotTemperatureSupported;
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuPowerSupported;
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuFanSpeedSupported;
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuVramSupported;
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuVoltageSupported;
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuTotalBoardPowerSupported;
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuIntakeTemperatureSupported;
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuMemoryTemperatureSupported;
        [MarshalAs(UnmanagedType.I1)]
        public bool NpuFrequencySupported;
        [MarshalAs(UnmanagedType.I1)]
        public bool NpuActivityLevelSupported;
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuSharedMemorySupported;
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuFanDutySupported;
    }

    /// <summary>
    /// Telemetry data structure matching AdlxTelemetryData in ADLXManager.h.
    /// Must match the native struct layout exactly.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct AdlxTelemetryData
    {
        // GPU Usage
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuUsageSupported;
        public double GpuUsageValue;

        // GPU Core Frequency
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuClockSpeedSupported;
        public double GpuClockSpeedValue;

        // GPU VRAM Frequency
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuVRAMClockSpeedSupported;
        public double GpuVRAMClockSpeedValue;

        // GPU Core Temperature
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuTemperatureSupported;
        public double GpuTemperatureValue;

        // GPU Hotspot Temperature
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuHotspotTemperatureSupported;
        public double GpuHotspotTemperatureValue;

        // GPU Power
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuPowerSupported;
        public double GpuPowerValue;

        // Fan Speed
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuFanSpeedSupported;
        public double GpuFanSpeedValue;

        // VRAM Usage
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuVramSupported;
        public double GpuVramValue;

        // GPU Voltage
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuVoltageSupported;
        public double GpuVoltageValue;

        // GPU TBP (Total Board Power)
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuTotalBoardPowerSupported;
        public double GpuTotalBoardPowerValue;

        // GPU Intake Temperature
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuIntakeTemperatureSupported;
        public double GpuIntakeTemperatureValue;

        // GPU Memory Temperature (IADLXGPUMetrics1)
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuMemoryTemperatureSupported;
        public double GpuMemoryTemperatureValue;

        // NPU Frequency (IADLXGPUMetrics1)
        [MarshalAs(UnmanagedType.I1)]
        public bool NpuFrequencySupported;
        public double NpuFrequencyValue;

        // NPU Activity Level (IADLXGPUMetrics1)
        [MarshalAs(UnmanagedType.I1)]
        public bool NpuActivityLevelSupported;
        public double NpuActivityLevelValue;

        // GPU Shared Memory (IADLXGPUMetrics2)
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuSharedMemorySupported;
        public double GpuSharedMemoryValue;

        // GPU Fan Duty (IADLXGPUMetrics3)
        [MarshalAs(UnmanagedType.I1)]
        public bool GpuFanDutySupported;
        public double GpuFanDutyValue;
    }

    /// <summary>
    /// Device info structure matching AdlxDeviceInfo in ADLXManager.h.
    /// Must match the native struct layout exactly.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct AdlxDeviceInfo
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_GPU_NAME_LEN)]
        public string GpuName;

        // Undefined = 0, Integrated = 1, Discrete = 2
        public uint GpuType;

        public int Id;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_VENDOR_ID_LEN)]
        public string VendorId;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_DRIVER_PATH_LEN)]
        public string DriverPath;

        /// <summary>
        /// ADL2 adapter index for this physical GPU. -1 when no ADL2 mapping
        /// is available. Used to address ADL2 PMLog calls.
        /// </summary>
        public int Adl2AdapterIndex;
    }

    public const int ADL2_PMLOG_MAX_VALUES = 256;
    public const int ADL2_PMLOG_RAW_DUMP_COUNT = 48;

    /// <summary>
    /// One PMLog sensor reading. SensorIndex is one of the
    /// <see cref="Adl2PmLogSensor"/> values; Value is the raw integer reading
    /// (unit depends on the index — see CapFrameX AMD GPU documentation).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Adl2PmLogEntry
    {
        public int SensorIndex;
        public int Value;
    }

    /// <summary>
    /// Mirror of Adl2PmLogSupport in Adl2PmLogManager.h. Lists the PMLog
    /// sensor indices the driver advertises for the GPU.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Adl2PmLogSupport
    {
        [MarshalAs(UnmanagedType.I1)]
        public bool Supported;
        public int SensorCount;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ADL2_PMLOG_MAX_VALUES)]
        public int[] SupportedSensors;
    }

    /// <summary>
    /// Mirror of Adl2PmLogData in Adl2PmLogManager.h. EntryCount tells how
    /// many entries in <see cref="Entries"/> are valid.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Adl2PmLogData
    {
        [MarshalAs(UnmanagedType.I1)]
        public bool Supported;
        public int SampleRate;
        public int EntryCount;

        /// <summary>
        /// Result of the wrapper's internal PMLog_Start call:
        /// 1 = succeeded, 0 = failed, -1 = not attempted.
        /// </summary>
        public int StartStatus;

        /// <summary>Driver's raw <c>ulSensors</c> count.</summary>
        public int DriverSensorCount;

        /// <summary>Driver's raw <c>ulLastUpdated</c> timestamp (microseconds).</summary>
        public long LastUpdated;

        /// <summary>Raw ADL return code from <c>ADL2_Adapter_PMLog_Support_Get</c>.</summary>
        public int SupportResultCode;

        /// <summary>Raw ADL return code from <c>ADL2_Adapter_PMLog_Start</c>.</summary>
        public int StartResultCode;

        /// <summary>How many sensor IDs we forwarded to <c>PMLog_Start</c>.</summary>
        public int SupportSensorCount;

        /// <summary>Sample rate (ms) we used in the last <c>PMLog_Start</c> attempt.</summary>
        public int SuccessfulSampleRate;

        /// <summary>
        /// 0 = the ADLX-shared ADL2 context was used (fallback when the
        /// dedicated PMLog context couldn't be created),
        /// 1 = a dedicated ADL2 context was used (the dual-context workaround
        /// for the ADLX vs ADL2 telemetry conflict).
        /// </summary>
        public int ContextMode;

        /// <summary>
        /// Number of adapters the pre-emptive (before-ADLX) Start sweep saw.
        /// -1 = not attempted.
        /// </summary>
        public int PreemptiveAdapterCount;

        /// <summary>Adapters whose pre-emptive Start succeeded.</summary>
        public int PreemptiveStartedCount;

        /// <summary><c>ptr_LoggingAddress</c> returned by <c>PMLog_Start</c> (shared buffer address).</summary>
        public ulong LoggingAddress;

        /// <summary>
        /// First N supported sensor IDs from <c>PMLog_Support_Get</c>, even when
        /// <c>PMLog_Start</c> later refused. Trailing slots are zero.
        /// </summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ADL2_PMLOG_RAW_DUMP_COUNT)]
        public int[] SupportedSensorIds;

        /// <summary>
        /// Raw dump of the first <see cref="ADL2_PMLOG_RAW_DUMP_COUNT"/> rows of
        /// <c>ADLPMLogData.ulValues[][2]</c>, flattened row-major: for row <c>i</c>
        /// the [0] column is at index <c>2*i</c>, the [1] column at <c>2*i+1</c>.
        /// Bypasses our interpretation of the format so the report can show
        /// the truth on the wire.
        /// </summary>
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2 * ADL2_PMLOG_RAW_DUMP_COUNT)]
        public uint[] RawValues;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = ADL2_PMLOG_MAX_VALUES)]
        public Adl2PmLogEntry[] Entries;
    }

    /// <summary>
    /// Subset of ADL_PMLOG_* sensor indices that CapFrameX consumes from the
    /// PMLog second source. Values match the public AMD ADL SDK and are
    /// stable across SDK revisions. Sensors that ADLX already exposes (e.g.
    /// GFXCLK, MEMCLK, HOTSPOT, ASIC_POWER) are intentionally omitted —
    /// ADLX is the authoritative source for them.
    /// </summary>
    public enum Adl2PmLogSensor
    {
        ClkSoCClock          = 3,
        TemperatureVrVddc    = 11,
        TemperatureVrmVdd    = 12,
        TemperatureLiquid    = 13,
        TemperaturePlx       = 14,
        SsnGfxCurrent        = 18,
        SsnGfxPower          = 19,
        SsnSoCVoltage        = 20,
        SsnSoCCurrent        = 21,
        SsnSoCPower          = 22,
        InfoActivityMem      = 24,
        MemVoltage           = 26,
        TemperatureVrSoC     = 30,
        ThrottlerStatus      = 33,
        PcieBusSpeed         = 34,
        PcieBusLanes         = 35,
        ClkVcn0Clock1        = 7,
        ClkVcn0Clock2        = 8,
        ClkFabricClock       = 38,
        ClkDcefClock         = 39,
    }

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "IntializeAdlx")]
    private static extern bool IntializeAdlx_Native();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "CloseAdlx")]
    private static extern void CloseAdlx_Native();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetAtiAdpaterCount")]
    private static extern uint GetAtiAdapterCount_Native();

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetAdlxTelemetry")]
    private static extern bool GetAdlxTelemetry_Native(uint index, uint historyLength, ref AdlxTelemetryData telemetryData);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetAdlxTelemetrySupport")]
    private static extern bool GetAdlxTelemetrySupport_Native(uint index, ref AdlxTelemetrySupport telemetrySupport);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetAdlxDeviceInfo")]
    private static extern bool GetAdlxDeviceInfo_Native(uint index, ref AdlxDeviceInfo deviceInfo);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetAdl2PmLogSupport")]
    private static extern bool GetAdl2PmLogSupport_Native(uint index, ref Adl2PmLogSupport pmLogSupport);

    [DllImport(DllName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "GetAdl2PmLogData")]
    private static extern bool GetAdl2PmLogData_Native(uint index, ref Adl2PmLogData pmLogData);

    /// <summary>
    /// Checks if the ADLX DLL is available and can be loaded.
    /// </summary>
    public static bool IsAvailable()
    {
        if (_dllLoadAttempted)
            return _dllLoaded;

        _dllLoadAttempted = true;

        try
        {
            // Try to find the DLL in the application directory
            string assemblyPath = AppDomain.CurrentDomain.BaseDirectory;
            string dllPath = Path.Combine(assemblyPath, DllName);

            if (!File.Exists(dllPath))
            {
                // Try x64 subdirectory
                dllPath = Path.Combine(assemblyPath, "x64", DllName);
            }

            if (File.Exists(dllPath))
            {
                _dllLoaded = true;
            }
        }
        catch
        {
            _dllLoaded = false;
        }

        return _dllLoaded;
    }

    /// <summary>
    /// Initializes the ADLX library.
    /// </summary>
    /// <returns>True if initialization succeeded, false otherwise.</returns>
    public static bool Initialize()
    {
        if (!IsAvailable())
            return false;

        try
        {
            return IntializeAdlx_Native();
        }
        catch (DllNotFoundException)
        {
            _dllLoaded = false;
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Closes the ADLX library and releases resources.
    /// </summary>
    public static void Close()
    {
        if (!_dllLoaded)
            return;

        try
        {
            CloseAdlx_Native();
        }
        catch
        {
            // Ignore exceptions during cleanup
        }
    }

    /// <summary>
    /// Gets the number of AMD adapters detected by ADLX.
    /// </summary>
    /// <returns>Number of AMD GPU adapters.</returns>
    public static uint GetAdapterCount()
    {
        if (!_dllLoaded)
            return 0;

        try
        {
            return GetAtiAdapterCount_Native();
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Gets telemetry data for the specified GPU adapter.
    /// </summary>
    /// <param name="index">Adapter index (0-based).</param>
    /// <param name="historyLength">History length in milliseconds.</param>
    /// <param name="telemetryData">Output telemetry data structure.</param>
    /// <returns>True if telemetry was retrieved successfully, false otherwise.</returns>
    public static bool GetTelemetry(uint index, uint historyLength, ref AdlxTelemetryData telemetryData)
    {
        if (!_dllLoaded)
            return false;

        try
        {
            return GetAdlxTelemetry_Native(index, historyLength, ref telemetryData);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the supported telemetry metrics for the specified GPU adapter.
    /// This method queries support flags directly without needing actual telemetry data,
    /// useful for activating sensors before telemetry history is available.
    /// </summary>
    /// <param name="index">Adapter index (0-based).</param>
    /// <param name="telemetrySupport">Output support flags structure.</param>
    /// <returns>True if support flags were retrieved successfully, false otherwise.</returns>
    public static bool GetTelemetrySupport(uint index, ref AdlxTelemetrySupport telemetrySupport)
    {
        if (!_dllLoaded)
            return false;

        try
        {
            return GetAdlxTelemetrySupport_Native(index, ref telemetrySupport);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets device information for the specified GPU adapter.
    /// </summary>
    /// <param name="index">Adapter index (0-based).</param>
    /// <param name="deviceInfo">Output device info structure.</param>
    /// <returns>True if device info was retrieved successfully, false otherwise.</returns>
    public static bool GetDeviceInfo(uint index, ref AdlxDeviceInfo deviceInfo)
    {
        if (!_dllLoaded)
            return false;

        try
        {
            return GetAdlxDeviceInfo_Native(index, ref deviceInfo);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Queries the PMLog sensor indices the driver advertises for the given
    /// ADLX GPU. PMLog is the ADL2 telemetry path that complements ADLX with
    /// rail-level data (SoC clock/voltage, VR temperatures, throttler reason).
    /// Returns false on systems where ADL2 is unavailable, where no mapping
    /// to an ADL adapter could be established, or where the driver reports
    /// no supported sensors.
    /// </summary>
    public static bool GetAdl2PmLogSupport(uint index, ref Adl2PmLogSupport support)
    {
        if (!_dllLoaded)
            return false;

        // Allocate the inner array if the caller didn't (Marshal will fill it).
        if (support.SupportedSensors == null)
            support.SupportedSensors = new int[ADL2_PMLOG_MAX_VALUES];

        try
        {
            return GetAdl2PmLogSupport_Native(index, ref support);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Fetches the most recent PMLog snapshot for the given ADLX GPU.
    /// First call per GPU implicitly starts PMLog tracking; subsequent calls
    /// just sample. Caller iterates <see cref="Adl2PmLogData.Entries"/> from
    /// 0 to <see cref="Adl2PmLogData.EntryCount"/> and picks the sensors of
    /// interest by <see cref="Adl2PmLogSensor"/> index.
    /// </summary>
    public static bool GetAdl2PmLogData(uint index, ref Adl2PmLogData data)
    {
        if (!_dllLoaded)
            return false;

        if (data.Entries == null)
            data.Entries = new Adl2PmLogEntry[ADL2_PMLOG_MAX_VALUES];
        if (data.RawValues == null)
            data.RawValues = new uint[2 * ADL2_PMLOG_RAW_DUMP_COUNT];
        if (data.SupportedSensorIds == null)
            data.SupportedSensorIds = new int[ADL2_PMLOG_RAW_DUMP_COUNT];

        try
        {
            return GetAdl2PmLogData_Native(index, ref data);
        }
        catch
        {
            return false;
        }
    }
}
