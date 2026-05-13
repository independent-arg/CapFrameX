#pragma once
#include "SDK/Include/ADLXDefines.h"
#include "Adl2/Adl2PmLogManager.h"

#define MAX_DRIVER_PATH_LEN  200
#define MAX_GPU_NAME_LEN  100
#define MAX_VENDOR_ID_LEN  20

// Struct for querying supported metrics only (no values needed)
// Used for activating sensors before telemetry data is available
typedef struct AdlxTelemetrySupport
{
	bool gpuUsageSupported = false;
	bool gpuClockSpeedSupported = false;
	bool gpuVRAMClockSpeedSupported = false;
	bool gpuTemperatureSupported = false;
	bool gpuHotspotTemperatureSupported = false;
	bool gpuPowerSupported = false;
	bool gpuFanSpeedSupported = false;
	bool gpuVramSupported = false;
	bool gpuVoltageSupported = false;
	bool gpuTotalBoardPowerSupported = false;
	bool gpuIntakeTemperatureSupported = false;
	bool gpuMemoryTemperatureSupported = false;
	bool npuFrequencySupported = false;
	bool npuActivityLevelSupported = false;
	bool gpuSharedMemorySupported = false;
	bool gpuFanDutySupported = false;
};

typedef struct AdlxTelemetryData
{
	// GPU Usage
	bool gpuUsageSupported = false;
	double gpuUsageValue;

	// GPU Core Frequency
	bool gpuClockSpeedSupported = false;
	double gpuClockSpeedValue;

	// GPU VRAM Frequency
	bool gpuVRAMClockSpeedSupported = false;
	double gpuVRAMClockSpeedValue;

	// GPU Core Temperature
	bool gpuTemperatureSupported = false;
	double gpuTemperatureValue;

	// GPU Hotspot Temperature
	bool gpuHotspotTemperatureSupported = false;
	double gpuHotspotTemperatureValue;

	// GPU Power
	bool gpuPowerSupported = false;
	double gpuPowerValue;

	// Fan Speed
	bool gpuFanSpeedSupported = false;
	double gpuFanSpeedValue;

	// VRAM Usage
	bool gpuVramSupported = false;
	double gpuVramValue;

	// GPU Voltage
	bool gpuVoltageSupported = false;
	double gpuVoltageValue;

	// GPU TBP
	bool gpuTotalBoardPowerSupported = false;
	double gpuTotalBoardPowerValue;

	// GPU Intake Temperature
	bool gpuIntakeTemperatureSupported = false;
	double gpuIntakeTemperatureValue;

	// GPU Memory Temperature (IADLXGPUMetrics1)
	bool gpuMemoryTemperatureSupported = false;
	double gpuMemoryTemperatureValue;

	// NPU Frequency (IADLXGPUMetrics1)
	bool npuFrequencySupported = false;
	double npuFrequencyValue;

	// NPU Activity Level (IADLXGPUMetrics1)
	bool npuActivityLevelSupported = false;
	double npuActivityLevelValue;

	// GPU Shared Memory (IADLXGPUMetrics2)
	bool gpuSharedMemorySupported = false;
	double gpuSharedMemoryValue;

	// GPU Fan Duty (IADLXGPUMetrics3)
	bool gpuFanDutySupported = false;
	double gpuFanDutyValue;
};

typedef struct AdlxDeviceInfo
{
	char GpuName[MAX_GPU_NAME_LEN];
	// Undefinied = 0, Integrated = 1, Discrete = 2
	uint32_t GpuType;
	int32_t Id;
	char VendorId[MAX_VENDOR_ID_LEN];
	char DriverPath[MAX_DRIVER_PATH_LEN];
	// Matching ADL2 adapter index for this GPU, -1 if no mapping. Filled by
	// the wrapper using ADLX's IADLMapping when ADL2 is also initialised.
	int32_t Adl2AdapterIndex;
};

#define ADLX_API __declspec(dllimport)

extern "C" ADLX_API bool IntializeAdlx();

extern "C" ADLX_API void CloseAdlx();

extern "C" ADLX_API adlx_uint GetAtiAdpaterCount();

extern "C" ADLX_API bool GetAdlxTelemetry(const adlx_uint index, const adlx_uint historyLength, AdlxTelemetryData * adlxTelemetryData);

extern "C" ADLX_API bool GetAdlxTelemetrySupport(const adlx_uint index, AdlxTelemetrySupport * adlxTelemetrySupport);

extern "C" ADLX_API bool GetAdlxDeviceInfo(const adlx_uint index, AdlxDeviceInfo * adlxDeviceInfo);

// Returns the ADL2 PMLog support list for the GPU at the given ADLX index,
// translated through IADLMapping. Returns false when ADL2 is not loaded,
// when no mapping exists, or when PMLog is unsupported by the driver.
extern "C" ADLX_API bool GetAdl2PmLogSupport(const adlx_uint index, Adl2PmLogSupport * pmLogSupport);

// Returns the most recent PMLog snapshot for the GPU at the given ADLX index.
// On first call per adapter the wrapper transparently starts PMLog tracking;
// subsequent calls just sample the running log. Returns false on error.
extern "C" ADLX_API bool GetAdl2PmLogData(const adlx_uint index, Adl2PmLogData * pmLogData);