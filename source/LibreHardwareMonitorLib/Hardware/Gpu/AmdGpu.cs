using LibreHardwareMonitor.Interop;
using Microsoft.Win32;
using System;
using System.Globalization;
using System.Text;

namespace LibreHardwareMonitor.Hardware.Gpu;

internal sealed class AmdGpu : GenericGpu
{
    private readonly uint _adapterIndex;
    private readonly Adl.AdlxDeviceInfo _deviceInfo;
    private readonly string _d3dDeviceId;

    // Temperature sensors
    private readonly Sensor _temperatureCore;
    private readonly Sensor _temperatureHotSpot;
    private readonly Sensor _temperatureMemory;
    private readonly Sensor _temperatureIntake;

    // Clock sensors
    private readonly Sensor _coreClock;
    private readonly Sensor _memoryClock;
    private readonly Sensor _npuClock;

    // Voltage sensors
    private readonly Sensor _coreVoltage;

    // Load sensors
    private readonly Sensor _coreLoad;
    private readonly Sensor _npuLoad;

    // Fan sensor
    private readonly Sensor _fan;
    private readonly Sensor _fanDuty;

    // Power sensors
    private readonly Sensor _powerTotal;
    private readonly Sensor _powerTotalBoardPower;

    // Memory sensors (VRAM)
    private readonly Sensor _memoryUsed;
    private readonly Sensor _sharedMemory;

    // --- ADL2 PMLog second source -----------------------------------------
    // These sensors fill gaps that ADLX does not cover. ADLX is authoritative
    // for every metric it provides — anything redundant (GFXCLK, MEMCLK,
    // HOTSPOT, FAN_RPM/PCT, GFX_VOLT, ACTIVITY_GFX, ASIC/BOARD_POWER) is
    // intentionally absent here and continues to come from Adl.
    private readonly Sensor _temperatureVrCore;
    private readonly Sensor _temperatureVrMemory;
    private readonly Sensor _temperatureVrSoC;
    private readonly Sensor _temperatureLiquid;
    private readonly Sensor _temperaturePlx;

    private readonly Sensor _socClock;
    private readonly Sensor _fabricClock;
    private readonly Sensor _displayEngineClock;
    private readonly Sensor _vcnDecoderClock;
    private readonly Sensor _vcnEncoderClock;

    private readonly Sensor _socVoltage;
    private readonly Sensor _memoryVoltage;

    private readonly Sensor _coreCurrent;
    private readonly Sensor _socCurrent;

    private readonly Sensor _corePowerRail;
    private readonly Sensor _socPower;

    private readonly Sensor _memoryControllerLoad;

    private readonly Sensor _pcieSpeed;
    private readonly Sensor _pcieLanes;
    private readonly Sensor _throttlerStatus;

    // Cached PMLog support — checked once in the constructor so Update()
    // can skip the native call when PMLog isn't usable for this GPU.
    private readonly bool _pmLogAvailable;

    public AmdGpu(uint adapterIndex, Adl.AdlxDeviceInfo deviceInfo, ISettings settings)
        : base(deviceInfo.GpuName?.Trim() ?? "AMD GPU",
               new Identifier("gpu-amd", adapterIndex.ToString(CultureInfo.InvariantCulture)),
               settings)
    {
        _adapterIndex = adapterIndex;
        _deviceInfo = deviceInfo;

        // Determine if discrete GPU based on GpuType
        IsDiscreteGpu = deviceInfo.GpuType == (uint)Adl.GpuType.Discrete;

        int index = (int)adapterIndex;

        // Temperature sensors
        _temperatureCore = new Sensor("GPU Core", 0, SensorType.Temperature, this, settings)
        { IsPresentationDefault = true, PresentationSortKey = $"{index}_2_0" };
        _temperatureMemory = new Sensor("GPU Memory", 1, SensorType.Temperature, this, settings)
        { PresentationSortKey = $"{index}_2_1" };
        _temperatureHotSpot = new Sensor("GPU Hot Spot", 2, SensorType.Temperature, this, settings)
        { PresentationSortKey = $"{index}_2_2" };
        _temperatureIntake = new Sensor("GPU Intake", 3, SensorType.Temperature, this, settings)
        { PresentationSortKey = $"{index}_2_3" };

        // Clock sensors
        _coreClock = new Sensor("GPU Core", 0, SensorType.Clock, this, settings)
        { IsPresentationDefault = true, PresentationSortKey = $"{index}_0_0" };
        _memoryClock = new Sensor("GPU Memory", 1, SensorType.Clock, this, settings)
        { IsPresentationDefault = true, PresentationSortKey = $"{index}_0_1" };
        _npuClock = new Sensor("NPU", 2, SensorType.Clock, this, settings)
        { PresentationSortKey = $"{index}_0_2" };

        // Fan sensor
        _fan = new Sensor("GPU Fan", 0, SensorType.Fan, this, settings)
        { PresentationSortKey = $"{index}_5_0" };
        _fanDuty = new Sensor("GPU Fan", 0, SensorType.Control, this, settings)
        { PresentationSortKey = $"{index}_5_1" };

        // Voltage sensor
        _coreVoltage = new Sensor("GPU Core", 0, SensorType.Voltage, this, settings)
        { PresentationSortKey = $"{index}_4_0" };

        // Load sensors
        _coreLoad = new Sensor("GPU Core", 0, SensorType.Load, this, settings)
        { IsPresentationDefault = true, PresentationSortKey = $"{index}_1_0" };
        _npuLoad = new Sensor("NPU", 1, SensorType.Load, this, settings)
        { PresentationSortKey = $"{index}_1_1" };

        // Power sensors
        _powerTotal = new Sensor("GPU Power", 0, SensorType.Power, this, settings)
        { IsPresentationDefault = true, PresentationSortKey = $"{index}_3_0" };
        _powerTotalBoardPower = new Sensor("GPU TBP", 1, SensorType.Power, this, settings)
        { IsPresentationDefault = true, PresentationSortKey = $"{index}_3_1" };

        // Memory sensors
        _memoryUsed = new Sensor("GPU Memory Dedicated", 0, SensorType.Data, this, settings)
        { IsPresentationDefault = true, PresentationSortKey = $"{index}_6_0" };
        _sharedMemory = new Sensor("GPU Memory Shared", 3, SensorType.Data, this, settings)
        { PresentationSortKey = $"{index}_6_1" };

        // --- PMLog-only sensors. Sort keys put them after the ADLX ones.
        _temperatureVrCore   = new Sensor("GPU VRM Core",   4, SensorType.Temperature, this, settings) { PresentationSortKey = $"{index}_2_4" };
        _temperatureVrMemory = new Sensor("GPU VRM Memory", 5, SensorType.Temperature, this, settings) { PresentationSortKey = $"{index}_2_5" };
        _temperatureVrSoC    = new Sensor("GPU VRM SoC",    6, SensorType.Temperature, this, settings) { PresentationSortKey = $"{index}_2_6" };
        _temperatureLiquid   = new Sensor("GPU Liquid",     7, SensorType.Temperature, this, settings) { PresentationSortKey = $"{index}_2_7" };
        _temperaturePlx      = new Sensor("GPU PLX",        8, SensorType.Temperature, this, settings) { PresentationSortKey = $"{index}_2_8" };

        _socClock           = new Sensor("GPU SoC",            3, SensorType.Clock, this, settings) { PresentationSortKey = $"{index}_0_3" };
        _fabricClock        = new Sensor("GPU Fabric (FCLK)",  4, SensorType.Clock, this, settings) { PresentationSortKey = $"{index}_0_4" };
        _displayEngineClock = new Sensor("GPU Display Engine", 5, SensorType.Clock, this, settings) { PresentationSortKey = $"{index}_0_5" };
        _vcnDecoderClock    = new Sensor("GPU VCN Decoder",    6, SensorType.Clock, this, settings) { PresentationSortKey = $"{index}_0_6" };
        _vcnEncoderClock    = new Sensor("GPU VCN Encoder",    7, SensorType.Clock, this, settings) { PresentationSortKey = $"{index}_0_7" };

        _socVoltage    = new Sensor("GPU SoC",    1, SensorType.Voltage, this, settings) { PresentationSortKey = $"{index}_4_1" };
        _memoryVoltage = new Sensor("GPU Memory", 2, SensorType.Voltage, this, settings) { PresentationSortKey = $"{index}_4_2" };

        _coreCurrent = new Sensor("GPU Core", 0, SensorType.Current, this, settings) { PresentationSortKey = $"{index}_7_0" };
        _socCurrent  = new Sensor("GPU SoC",  1, SensorType.Current, this, settings) { PresentationSortKey = $"{index}_7_1" };

        _corePowerRail = new Sensor("GPU Core Rail (VDDCR_GFX)", 2, SensorType.Power, this, settings) { PresentationSortKey = $"{index}_3_2" };
        _socPower      = new Sensor("GPU SoC",                   3, SensorType.Power, this, settings) { PresentationSortKey = $"{index}_3_3" };

        _memoryControllerLoad = new Sensor("GPU Memory Controller", 2, SensorType.Load, this, settings) { PresentationSortKey = $"{index}_1_2" };

        _pcieSpeed       = new Sensor("GPU PCIe Gen",   0, SensorType.Factor, this, settings) { PresentationSortKey = $"{index}_8_0" };
        _pcieLanes       = new Sensor("GPU PCIe Lanes", 1, SensorType.Factor, this, settings) { PresentationSortKey = $"{index}_8_1" };
        _throttlerStatus = new Sensor("GPU Throttler",  2, SensorType.Factor, this, settings) { PresentationSortKey = $"{index}_8_2" };

        // Activate ADLX sensors based on support flags BEFORE calling Update().
        // Then layer PMLog sensors on top — only the ones the driver advertises.
        ActivateSensorsFromSupportFlags();
        _pmLogAvailable = ActivateSensorsFromPmLogSupport();
    }

    /// <summary>
    /// Activates sensors based on what metrics the GPU supports, without needing actual telemetry data.
    /// This is called in the constructor to ensure sensors are visible even before telemetry history is available.
    /// </summary>
    private void ActivateSensorsFromSupportFlags()
    {
        Adl.AdlxTelemetrySupport support = new();
        if (!Adl.GetTelemetrySupport(_adapterIndex, ref support))
            return;

        // Temperature sensors
        if (support.GpuTemperatureSupported)
            ActivateSensor(_temperatureCore);
        if (support.GpuHotspotTemperatureSupported)
            ActivateSensor(_temperatureHotSpot);
        if (support.GpuMemoryTemperatureSupported)
            ActivateSensor(_temperatureMemory);
        if (support.GpuIntakeTemperatureSupported)
            ActivateSensor(_temperatureIntake);

        // Clock sensors
        if (support.GpuClockSpeedSupported)
            ActivateSensor(_coreClock);
        if (support.GpuVRAMClockSpeedSupported)
            ActivateSensor(_memoryClock);
        if (support.NpuFrequencySupported)
            ActivateSensor(_npuClock);

        // Fan sensor
        if (support.GpuFanSpeedSupported)
            ActivateSensor(_fan);
        if (support.GpuFanDutySupported)
            ActivateSensor(_fanDuty);

        // Voltage sensor
        if (support.GpuVoltageSupported)
            ActivateSensor(_coreVoltage);

        // Load sensors
        if (support.GpuUsageSupported)
            ActivateSensor(_coreLoad);
        if (support.NpuActivityLevelSupported)
            ActivateSensor(_npuLoad);

        // Power sensors
        if (support.GpuPowerSupported)
            ActivateSensor(_powerTotal);
        if (support.GpuTotalBoardPowerSupported)
            ActivateSensor(_powerTotalBoardPower);

        // Memory sensors
        if (support.GpuVramSupported)
            ActivateSensor(_memoryUsed);
        if (support.GpuSharedMemorySupported)
            ActivateSensor(_sharedMemory);
    }

    /// <summary>
    /// Activates the PMLog (ADL2) sensors the driver advertises for this GPU.
    /// Only sensors whose ADL_PMLOG_* index is listed in <c>support.SupportedSensors</c>
    /// get activated — keeping the sensor list aligned with what the driver
    /// will actually fill in <see cref="Update"/>.
    ///
    /// Requires <c>PMLog_Start</c> to actually succeed, not just
    /// <c>PMLog_Support_Get</c>: on RDNA 4 (RX 9070 XT, Adrenalin as of
    /// 2026-05) the driver advertises ~20 sensors via Support_Get but then
    /// rejects every Start call with ADL_ERR, leaving sensors empty.
    /// HWiNFO succeeds on the same GPU through a path we have not been
    /// able to identify even after four reverse-engineering passes; rather
    /// than show dead sensors, we hide the entire PMLog block when Start
    /// fails.
    /// </summary>
    private bool ActivateSensorsFromPmLogSupport()
    {
        Adl.Adl2PmLogSupport support = new();
        if (!Adl.GetAdl2PmLogSupport(_adapterIndex, ref support) || !support.Supported)
            return false;

        // Trigger the lazy PMLog_Start in the native wrapper and require it
        // to succeed. We only check StartStatus here — the actual data may
        // not be populated yet on first call, that's fine and Update() will
        // pick it up.
        Adl.Adl2PmLogData probe = new();
        Adl.GetAdl2PmLogData(_adapterIndex, ref probe);
        if (probe.StartStatus != 1)
            return false;

        for (int i = 0; i < support.SensorCount; i++)
        {
            switch ((Adl.Adl2PmLogSensor)support.SupportedSensors[i])
            {
                case Adl.Adl2PmLogSensor.TemperatureVrVddc:    ActivateSensor(_temperatureVrCore);   break;
                case Adl.Adl2PmLogSensor.TemperatureVrmVdd:    ActivateSensor(_temperatureVrMemory); break;
                case Adl.Adl2PmLogSensor.TemperatureVrSoC:     ActivateSensor(_temperatureVrSoC);    break;
                case Adl.Adl2PmLogSensor.TemperatureLiquid:    ActivateSensor(_temperatureLiquid);   break;
                case Adl.Adl2PmLogSensor.TemperaturePlx:       ActivateSensor(_temperaturePlx);      break;

                case Adl.Adl2PmLogSensor.ClkSoCClock:          ActivateSensor(_socClock);            break;
                case Adl.Adl2PmLogSensor.ClkFabricClock:       ActivateSensor(_fabricClock);         break;
                case Adl.Adl2PmLogSensor.ClkDcefClock:         ActivateSensor(_displayEngineClock);  break;
                case Adl.Adl2PmLogSensor.ClkVcn0Clock1:        ActivateSensor(_vcnDecoderClock);     break;
                case Adl.Adl2PmLogSensor.ClkVcn0Clock2:        ActivateSensor(_vcnEncoderClock);     break;

                case Adl.Adl2PmLogSensor.SsnSoCVoltage:        ActivateSensor(_socVoltage);          break;
                case Adl.Adl2PmLogSensor.MemVoltage:           ActivateSensor(_memoryVoltage);       break;

                case Adl.Adl2PmLogSensor.SsnGfxCurrent:        ActivateSensor(_coreCurrent);         break;
                case Adl.Adl2PmLogSensor.SsnSoCCurrent:        ActivateSensor(_socCurrent);          break;

                case Adl.Adl2PmLogSensor.SsnGfxPower:          ActivateSensor(_corePowerRail);       break;
                case Adl.Adl2PmLogSensor.SsnSoCPower:          ActivateSensor(_socPower);            break;

                case Adl.Adl2PmLogSensor.InfoActivityMem:      ActivateSensor(_memoryControllerLoad);break;

                case Adl.Adl2PmLogSensor.PcieBusSpeed:         ActivateSensor(_pcieSpeed);           break;
                case Adl.Adl2PmLogSensor.PcieBusLanes:         ActivateSensor(_pcieLanes);           break;
                case Adl.Adl2PmLogSensor.ThrottlerStatus:      ActivateSensor(_throttlerStatus);     break;
            }
        }
        return true;
    }

    /// <summary>
    /// Writes the current PMLog snapshot into the cached PMLog sensors. ADLX
    /// telemetry has already filled the redundant sensors before this runs,
    /// so we only touch the ones PMLog exclusively owns. Raw PMLog values are
    /// in mV/mA/mW; clocks and temperatures arrive in their target units.
    /// </summary>
    private void UpdatePmLogSensors()
    {
        if (!_pmLogAvailable)
            return;

        Adl.Adl2PmLogData data = new();
        if (!Adl.GetAdl2PmLogData(_adapterIndex, ref data) || !data.Supported)
            return;

        for (int i = 0; i < data.EntryCount; i++)
        {
            Adl.Adl2PmLogEntry e = data.Entries[i];
            switch ((Adl.Adl2PmLogSensor)e.SensorIndex)
            {
                case Adl.Adl2PmLogSensor.TemperatureVrVddc:    _temperatureVrCore.Value   = (float)e.Value; break;
                case Adl.Adl2PmLogSensor.TemperatureVrmVdd:    _temperatureVrMemory.Value = (float)e.Value; break;
                case Adl.Adl2PmLogSensor.TemperatureVrSoC:     _temperatureVrSoC.Value    = (float)e.Value; break;
                case Adl.Adl2PmLogSensor.TemperatureLiquid:    _temperatureLiquid.Value   = (float)e.Value; break;
                case Adl.Adl2PmLogSensor.TemperaturePlx:       _temperaturePlx.Value      = (float)e.Value; break;

                case Adl.Adl2PmLogSensor.ClkSoCClock:          _socClock.Value            = (float)e.Value; break;
                case Adl.Adl2PmLogSensor.ClkFabricClock:       _fabricClock.Value         = (float)e.Value; break;
                case Adl.Adl2PmLogSensor.ClkDcefClock:         _displayEngineClock.Value  = (float)e.Value; break;
                case Adl.Adl2PmLogSensor.ClkVcn0Clock1:        _vcnDecoderClock.Value     = (float)e.Value; break;
                case Adl.Adl2PmLogSensor.ClkVcn0Clock2:        _vcnEncoderClock.Value     = (float)e.Value; break;

                // PMLog reports voltages/currents/powers in milli-units.
                case Adl.Adl2PmLogSensor.SsnSoCVoltage:        _socVoltage.Value    = e.Value / 1000f; break;
                case Adl.Adl2PmLogSensor.MemVoltage:           _memoryVoltage.Value = e.Value / 1000f; break;
                case Adl.Adl2PmLogSensor.SsnGfxCurrent:        _coreCurrent.Value   = e.Value / 1000f; break;
                case Adl.Adl2PmLogSensor.SsnSoCCurrent:        _socCurrent.Value    = e.Value / 1000f; break;
                case Adl.Adl2PmLogSensor.SsnGfxPower:          _corePowerRail.Value = e.Value / 1000f; break;
                case Adl.Adl2PmLogSensor.SsnSoCPower:          _socPower.Value      = e.Value / 1000f; break;

                case Adl.Adl2PmLogSensor.InfoActivityMem:      _memoryControllerLoad.Value = (float)Math.Min(e.Value, 100); break;

                case Adl.Adl2PmLogSensor.PcieBusSpeed:         _pcieSpeed.Value       = (float)e.Value; break;
                case Adl.Adl2PmLogSensor.PcieBusLanes:         _pcieLanes.Value       = (float)e.Value; break;
                case Adl.Adl2PmLogSensor.ThrottlerStatus:      _throttlerStatus.Value = (float)e.Value; break;
            }
        }
    }

    public override string DeviceId => _deviceInfo.DriverPath ?? string.Empty;

    public override HardwareType HardwareType => HardwareType.GpuAmd;

    public override void Update()
    {
        UpdateProcessMemorySensors();

        // Get ADLX telemetry data
        Adl.AdlxTelemetryData telemetry = new();
        if (Adl.GetTelemetry(_adapterIndex, 1000, ref telemetry))
        {
            // Temperature sensors
            _temperatureCore.Value = telemetry.GpuTemperatureSupported ? (float)telemetry.GpuTemperatureValue : null;
            _temperatureHotSpot.Value = telemetry.GpuHotspotTemperatureSupported ? (float)telemetry.GpuHotspotTemperatureValue : null;
            _temperatureMemory.Value = telemetry.GpuMemoryTemperatureSupported ? (float)telemetry.GpuMemoryTemperatureValue : null;
            _temperatureIntake.Value = telemetry.GpuIntakeTemperatureSupported ? (float)telemetry.GpuIntakeTemperatureValue : null;

            // Clock sensors
            _coreClock.Value = telemetry.GpuClockSpeedSupported ? (float)telemetry.GpuClockSpeedValue : null;
            _memoryClock.Value = telemetry.GpuVRAMClockSpeedSupported ? (float)telemetry.GpuVRAMClockSpeedValue : null;
            _npuClock.Value = telemetry.NpuFrequencySupported ? (float)telemetry.NpuFrequencyValue : null;

            // Fan sensor
            _fan.Value = telemetry.GpuFanSpeedSupported ? (float)telemetry.GpuFanSpeedValue : null;
            _fanDuty.Value = telemetry.GpuFanDutySupported ? (float)telemetry.GpuFanDutyValue : null;

            // Voltage sensor (ADLX returns voltage in mV, convert to V)
            _coreVoltage.Value = telemetry.GpuVoltageSupported ? (float)(telemetry.GpuVoltageValue / 1000.0) : null;

            // Load sensors
            _coreLoad.Value = telemetry.GpuUsageSupported ? (float)Math.Min(telemetry.GpuUsageValue, 100) : null;
            _npuLoad.Value = telemetry.NpuActivityLevelSupported ? (float)Math.Min(telemetry.NpuActivityLevelValue, 100) : null;

            // Power sensors
            _powerTotal.Value = telemetry.GpuPowerSupported ? (float)telemetry.GpuPowerValue : null;
            _powerTotalBoardPower.Value = telemetry.GpuTotalBoardPowerSupported ? (float)telemetry.GpuTotalBoardPowerValue : null;

            // VRAM usage from ADLX (in MB, convert to GB)
            _memoryUsed.Value = telemetry.GpuVramSupported ? (float)(telemetry.GpuVramValue / 1024.0) : null;

            // Shared memory from ADLX (in MB, convert to GB)
            _sharedMemory.Value = telemetry.GpuSharedMemorySupported ? (float)(telemetry.GpuSharedMemoryValue / 1024.0) : null;
        }

        // PMLog second source — only sensors that ADLX does not already cover.
        UpdatePmLogSensors();
    }

    public override void Close()
    {
        base.Close();
    }

    public override string GetDriverVersion()
    {
        // First, try to get Radeon Software version from AMD's standard registry location
        try
        {
            using RegistryKey amdKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\AMD\CN");
            if (amdKey != null)
            {
                var sv = amdKey.GetValue("RadeonSoftwareVersion");
                if (sv != null && !string.IsNullOrEmpty(sv.ToString()))
                {
                    return $"Adrenalin {sv}";
                }
            }
        }
        catch
        {
            // Ignore registry access errors
        }

        // Fall back to getting driver version from device class registry key
        if (!string.IsNullOrEmpty(_deviceInfo.DriverPath))
        {
            try
            {
                // DriverPath is typically "{GUID}\NNNN" - prepend the Control\Class path
                string driverPath = _deviceInfo.DriverPath;

                // Remove any "\Registry\Machine\" prefix if present
                const string registryPrefix = @"\Registry\Machine\";
                if (driverPath.StartsWith(registryPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    driverPath = driverPath.Substring(registryPrefix.Length);
                }

                // If it starts with a GUID brace, prepend the Control\Class path
                if (driverPath.StartsWith("{", StringComparison.Ordinal))
                {
                    driverPath = @"SYSTEM\CurrentControlSet\Control\Class\" + driverPath;
                }

                using RegistryKey deviceKey = Registry.LocalMachine.OpenSubKey(driverPath);
                if (deviceKey != null)
                {
                    // Try RadeonSoftwareVersion first (in case it's stored here)
                    var radeonVersion = deviceKey.GetValue("RadeonSoftwareVersion");
                    if (radeonVersion != null && !string.IsNullOrEmpty(radeonVersion.ToString()))
                    {
                        return $"Adrenalin {radeonVersion}";
                    }

                    // Fall back to Windows driver version
                    var driverVersion = deviceKey.GetValue("DriverVersion");
                    if (driverVersion != null && !string.IsNullOrEmpty(driverVersion.ToString()))
                    {
                        return driverVersion.ToString();
                    }
                }
            }
            catch
            {
                // Ignore registry access errors
            }
        }

        return null;
    }

    public override string GetReport()
    {
        var r = new StringBuilder();

        r.AppendLine("AMD GPU (ADLX)");
        r.AppendLine();

        r.Append("AdapterIndex: ");
        r.AppendLine(_adapterIndex.ToString(CultureInfo.InvariantCulture));
        r.Append("GpuName: ");
        r.AppendLine(_deviceInfo.GpuName);
        r.Append("GpuType: ");
        r.AppendLine(((Adl.GpuType)_deviceInfo.GpuType).ToString());
        r.Append("VendorId: ");
        r.AppendLine(_deviceInfo.VendorId);
        r.Append("DriverPath: ");
        r.AppendLine(_deviceInfo.DriverPath);
        r.Append("UniqueId: ");
        r.AppendLine(_deviceInfo.Id.ToString(CultureInfo.InvariantCulture));
        r.AppendLine();

        r.AppendLine("ADLX Telemetry Support");
        r.AppendLine();

        Adl.AdlxTelemetryData telemetry = new();
        if (Adl.GetTelemetry(_adapterIndex, 1000, ref telemetry))
        {
            r.AppendFormat(" GPU Usage: Supported={0}, Value={1}%{2}", telemetry.GpuUsageSupported, telemetry.GpuUsageValue, Environment.NewLine);
            r.AppendFormat(" GPU Clock Speed: Supported={0}, Value={1} MHz{2}", telemetry.GpuClockSpeedSupported, telemetry.GpuClockSpeedValue, Environment.NewLine);
            r.AppendFormat(" GPU VRAM Clock Speed: Supported={0}, Value={1} MHz{2}", telemetry.GpuVRAMClockSpeedSupported, telemetry.GpuVRAMClockSpeedValue, Environment.NewLine);
            r.AppendFormat(" GPU Temperature: Supported={0}, Value={1}°C{2}", telemetry.GpuTemperatureSupported, telemetry.GpuTemperatureValue, Environment.NewLine);
            r.AppendFormat(" GPU Hotspot Temperature: Supported={0}, Value={1}°C{2}", telemetry.GpuHotspotTemperatureSupported, telemetry.GpuHotspotTemperatureValue, Environment.NewLine);
            r.AppendFormat(" GPU Intake Temperature: Supported={0}, Value={1}°C{2}", telemetry.GpuIntakeTemperatureSupported, telemetry.GpuIntakeTemperatureValue, Environment.NewLine);
            r.AppendFormat(" GPU Memory Temperature: Supported={0}, Value={1}°C{2}", telemetry.GpuMemoryTemperatureSupported, telemetry.GpuMemoryTemperatureValue, Environment.NewLine);
            r.AppendFormat(" GPU Power: Supported={0}, Value={1} W{2}", telemetry.GpuPowerSupported, telemetry.GpuPowerValue, Environment.NewLine);
            r.AppendFormat(" GPU Total Board Power: Supported={0}, Value={1} W{2}", telemetry.GpuTotalBoardPowerSupported, telemetry.GpuTotalBoardPowerValue, Environment.NewLine);
            r.AppendFormat(" GPU Fan Speed: Supported={0}, Value={1} RPM{2}", telemetry.GpuFanSpeedSupported, telemetry.GpuFanSpeedValue, Environment.NewLine);
            r.AppendFormat(" GPU VRAM: Supported={0}, Value={1} MB{2}", telemetry.GpuVramSupported, telemetry.GpuVramValue, Environment.NewLine);
            r.AppendFormat(" GPU Voltage: Supported={0}, Value={1} mV{2}", telemetry.GpuVoltageSupported, telemetry.GpuVoltageValue, Environment.NewLine);
            r.AppendFormat(" NPU Frequency: Supported={0}, Value={1} MHz{2}", telemetry.NpuFrequencySupported, telemetry.NpuFrequencyValue, Environment.NewLine);
            r.AppendFormat(" NPU Activity Level: Supported={0}, Value={1}%{2}", telemetry.NpuActivityLevelSupported, telemetry.NpuActivityLevelValue, Environment.NewLine);
            r.AppendFormat(" GPU Shared Memory: Supported={0}, Value={1} MB{2}", telemetry.GpuSharedMemorySupported, telemetry.GpuSharedMemoryValue, Environment.NewLine);
            r.AppendFormat(" GPU Fan Duty: Supported={0}, Value={1}%{2}", telemetry.GpuFanDutySupported, telemetry.GpuFanDutyValue, Environment.NewLine);
        }
        else
        {
            r.AppendLine(" Failed to get telemetry data");
        }

        r.AppendLine();
        r.AppendLine("ADL2 PMLog (second source)");
        r.AppendLine();
        r.AppendFormat(" Adl2AdapterIndex: {0}{1}", _deviceInfo.Adl2AdapterIndex, Environment.NewLine);
        r.AppendFormat(" sizeof(Adl2PmLogData) managed: {0}{1}",
            System.Runtime.InteropServices.Marshal.SizeOf<Adl.Adl2PmLogData>(), Environment.NewLine);

        // Always dump the PMLog diagnostic block — even when sensors are
        // not activated, we want to see WHY (StartResultCode etc.) so the
        // report is useful for triage.
        r.AppendFormat(" _pmLogAvailable: {0}{1}", _pmLogAvailable, Environment.NewLine);
        {
            Adl.Adl2PmLogData pm = new();
            bool ok = Adl.GetAdl2PmLogData(_adapterIndex, ref pm);
            r.AppendFormat(" GetAdl2PmLogData: {0}{1}", ok, Environment.NewLine);
            r.AppendFormat(" Supported: {0}{1}", pm.Supported, Environment.NewLine);
            r.AppendFormat(" StartStatus: {0} (1=ok, 0=fail, -1=not attempted){1}", pm.StartStatus, Environment.NewLine);
            r.AppendFormat(" SupportResultCode: {0}{1}", pm.SupportResultCode, Environment.NewLine);
            r.AppendFormat(" StartResultCode: {0} (0=OK, -1=ERR, -3=INVALID_PARAM, -8=NOT_SUPPORTED){1}", pm.StartResultCode, Environment.NewLine);
            r.AppendFormat(" SupportSensorCount (forwarded to Start): {0}{1}", pm.SupportSensorCount, Environment.NewLine);
            r.AppendFormat(" SuccessfulSampleRate: {0} ms{1}", pm.SuccessfulSampleRate, Environment.NewLine);
            r.AppendFormat(" ContextMode: {0} (0=ADLX-shared, 1=dedicated){1}", pm.ContextMode, Environment.NewLine);
            r.AppendFormat(" PreemptiveAdapterCount: {0}{1}", pm.PreemptiveAdapterCount, Environment.NewLine);
            r.AppendFormat(" PreemptiveStartedCount: {0}{1}", pm.PreemptiveStartedCount, Environment.NewLine);
            r.AppendFormat(" LoggingAddress: 0x{0:X16}{1}", pm.LoggingAddress, Environment.NewLine);
            if (pm.SupportedSensorIds != null)
            {
                int shown = Math.Min(pm.SupportSensorCount, pm.SupportedSensorIds.Length);
                r.Append(" SupportedSensorIds:");
                for (int i = 0; i < shown; i++)
                {
                    r.AppendFormat(" {0}", pm.SupportedSensorIds[i]);
                }
                r.AppendLine();
            }
            r.AppendFormat(" DriverSensorCount: {0}{1}", pm.DriverSensorCount, Environment.NewLine);
            r.AppendFormat(" LastUpdated: {0}{1}", pm.LastUpdated, Environment.NewLine);
            r.AppendFormat(" SampleRate: {0} ms{1}", pm.SampleRate, Environment.NewLine);
            r.AppendFormat(" EntryCount: {0}{1}", pm.EntryCount, Environment.NewLine);
            if (pm.Entries != null)
            {
                for (int i = 0; i < pm.EntryCount; i++)
                {
                    r.AppendFormat("   [{0}] sensor={1} value={2}{3}", i, pm.Entries[i].SensorIndex, pm.Entries[i].Value, Environment.NewLine);
                }
            }
            // Raw driver buffer — read this section to see exactly what
            // atiadlxx.dll wrote, regardless of how our code interpreted it.
            if (pm.RawValues != null && pm.RawValues.Length >= 2 * Adl.ADL2_PMLOG_RAW_DUMP_COUNT)
            {
                r.AppendFormat(" RawValues (first {0} rows of ulValues[][2]):{1}", Adl.ADL2_PMLOG_RAW_DUMP_COUNT, Environment.NewLine);
                for (int i = 0; i < Adl.ADL2_PMLOG_RAW_DUMP_COUNT; i++)
                {
                    uint c0 = pm.RawValues[2 * i];
                    uint c1 = pm.RawValues[2 * i + 1];
                    if (c0 == 0u && c1 == 0u)
                        continue;
                    r.AppendFormat("   raw[{0,3}] = [{1}, {2}]  (0x{1:X8}, 0x{2:X8}){3}", i, c0, c1, Environment.NewLine);
                }
            }
        }

        r.AppendLine();

        if (_d3dDeviceId != null)
        {
            r.AppendLine("D3D");
            r.AppendLine();
            r.AppendLine(" Id: " + _d3dDeviceId);
        }

        return r.ToString();
    }
}
