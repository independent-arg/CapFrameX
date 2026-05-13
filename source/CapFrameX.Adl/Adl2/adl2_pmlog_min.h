// Minimal subset of the AMD ADL (Display Library) SDK headers, restricted to
// the PMLog telemetry path that supplements ADLX.
//
// Only the types, constants and entry points needed by Adl2PmLogManager are
// declared here. This avoids dragging the full ADL SDK into the repository
// while staying ABI-compatible with atiadlxx.dll.
//
// The full SDK is published by AMD under the GPUOpen ADL package.
// https://gpuopen.com/amd-gpu-services-display-library-adl-sdk/

#pragma once

#include <cstdint>

#define ADL_MAX_PATH                  256
#define ADL_OK                        0
#define ADL_OK_WARNING                1
#define ADL_ERR                       -1

#define ADL_PMLOG_MAX_SUPPORTED_SENSORS  256

// Subset of ADL_PMLOG_SENSORS we expose as non-redundant CapFrameX sensors.
// Values match the public ADL SDK adl_defines.h (PMLOG indices are stable
// across SDK revisions; new entries are appended, never reordered).
enum ADLSensorType
{
    ADL_SENSOR_MAXTYPES                 = 0,
    ADL_PMLOG_CLK_GFXCLK                = 1,
    ADL_PMLOG_CLK_MEMCLK                = 2,
    ADL_PMLOG_CLK_SOCCLK                = 3,
    ADL_PMLOG_CLK_UVDCLK1               = 4,
    ADL_PMLOG_CLK_UVDCLK2               = 5,
    ADL_PMLOG_CLK_VCECLK                = 6,
    ADL_PMLOG_CLK_VCN0CLK1              = 7,
    ADL_PMLOG_CLK_VCN0CLK2              = 8,
    ADL_PMLOG_TEMPERATURE_EDGE          = 9,
    ADL_PMLOG_TEMPERATURE_MEM           = 10,
    ADL_PMLOG_TEMPERATURE_VRVDDC        = 11,
    ADL_PMLOG_TEMPERATURE_VRMVDD        = 12,
    ADL_PMLOG_TEMPERATURE_LIQUID        = 13,
    ADL_PMLOG_TEMPERATURE_PLX           = 14,
    ADL_PMLOG_FAN_RPM                   = 15,
    ADL_PMLOG_FAN_PERCENTAGE            = 16,
    ADL_PMLOG_SSN_GFX_VOLT              = 17,
    ADL_PMLOG_SSN_GFX_CURR              = 18,
    ADL_PMLOG_SSN_GFX_POWER             = 19,
    ADL_PMLOG_SSN_SOC_VOLT              = 20,
    ADL_PMLOG_SSN_SOC_CURR              = 21,
    ADL_PMLOG_SSN_SOC_POWER             = 22,
    ADL_PMLOG_INFO_ACTIVITY_GFX         = 23,
    ADL_PMLOG_INFO_ACTIVITY_MEM         = 24,
    ADL_PMLOG_GFX_VOLTAGE               = 25,
    ADL_PMLOG_MEM_VOLTAGE               = 26,
    ADL_PMLOG_ASIC_POWER                = 27,
    ADL_PMLOG_TEMPERATURE_VRMVDDC       = 28,
    ADL_PMLOG_TEMPERATURE_VRMVDDCI      = 29,
    ADL_PMLOG_TEMPERATURE_VRSOC         = 30,
    ADL_PMLOG_TEMPERATURE_VRMVDDGFX     = 31,
    ADL_PMLOG_TEMPERATURE_HOTSPOT       = 32,
    ADL_PMLOG_THROTTLER_STATUS          = 33,
    ADL_PMLOG_PCIE_BUS_SPEED            = 34,
    ADL_PMLOG_PCIE_BUS_LANES            = 35,
    ADL_PMLOG_TEMPERATURE_LIQUID0       = 36,
    ADL_PMLOG_TEMPERATURE_LIQUID1       = 37,
    ADL_PMLOG_CLK_FCLK                  = 38,
    ADL_PMLOG_CLK_DCEFCLK               = 39,
    ADL_PMLOG_FAN_PWM                   = 40,
    ADL_PMLOG_BOARD_POWER               = 41,
    ADL_PMLOG_MAX_SENSORS_REAL          = 42
};

// AdapterInfo subset — only the fields we touch.
typedef struct AdapterInfo
{
    int  iSize;
    int  iAdapterIndex;
    char strUDID[ADL_MAX_PATH];
    int  iBusNumber;
    int  iDeviceNumber;
    int  iFunctionNumber;
    int  iVendorID;
    char strAdapterName[ADL_MAX_PATH];
    char strDisplayName[ADL_MAX_PATH];
    int  iPresent;
    int  iExist;
    char strDriverPath[ADL_MAX_PATH];
    char strDriverPathExt[ADL_MAX_PATH];
    char strPNPString[ADL_MAX_PATH];
    int  iOSDisplayIndex;
} AdapterInfo, *LPAdapterInfo;

// PMLog support struct (returned by ADL2_Adapter_PMLog_Support_Get).
// The first array is a list of supported sensor enum values, terminated
// by ADL_SENSOR_MAXTYPES (0). The driver fills as many slots as needed.
typedef struct ADLPMLogSupportInfo
{
    unsigned short usSensors[ADL_PMLOG_MAX_SUPPORTED_SENSORS];
    int            ulReserved[16];
} ADLPMLogSupportInfo;

// PMLog config passed to ADL2_Adapter_PMLog_Start.
// We request all supported sensors at 1000 ms sample rate, in user-space
// memory mode (= 1), with no event-driven callback.
typedef struct ADLPMLogStartInput
{
    unsigned short usSensors[ADL_PMLOG_MAX_SUPPORTED_SENSORS];
    unsigned long  ulSampleRate;
} ADLPMLogStartInput;

typedef struct ADLPMLogStartOutput
{
    union
    {
        void*          ptr_LoggingAddress;
        unsigned long long ptr_LoggingAddress64;
    };
} ADLPMLogStartOutput;

// PMLogData buffer returned by ADL2_New_QueryPMLogData_Get.
// Per public AMD docs:
//   ulValues[i][0] = sensor enum (ADL_PMLOG_*), or 0xFFFFFFFF if entry empty
//   ulValues[i][1] = current value for that sensor
typedef struct ADLPMLogData
{
    unsigned int ulVersion;
    unsigned int ulActiveSampleRate;
    unsigned long long ulLastUpdated;
    unsigned int ulValues[ADL_PMLOG_MAX_SUPPORTED_SENSORS][2];
    unsigned int ulSensors;
    unsigned int ulReserved[256];
} ADLPMLogData;

// Memory allocation callback type required by ADL2_Main_Control_Create.
typedef void* (*ADL_MAIN_MALLOC_CALLBACK)(int);

// ADL2 entry-point function pointer types (all stdcall on Win32, but on
// Win64 the calling convention is unified — they're declared __stdcall
// in the SDK for source compatibility).
typedef int (__stdcall *ADL2_MAIN_CONTROL_CREATE)              (ADL_MAIN_MALLOC_CALLBACK, int /*iEnumConnectedAdapters*/, void** /*context*/);
typedef int (__stdcall *ADL2_MAIN_CONTROL_DESTROY)             (void* /*context*/);
typedef int (__stdcall *ADL2_ADAPTER_NUMBEROFADAPTERS_GET)     (void* /*context*/, int* /*numAdapters*/);
typedef int (__stdcall *ADL2_ADAPTER_ADAPTERINFO_GET)          (void* /*context*/, LPAdapterInfo, int /*size*/);
typedef int (__stdcall *ADL2_ADAPTER_ACTIVE_GET)               (void* /*context*/, int /*adapter*/, int* /*status*/);
typedef int (__stdcall *ADL2_ADAPTER_PMLOG_SUPPORT_GET)        (void* /*context*/, int /*adapter*/, ADLPMLogSupportInfo* /*out*/);
typedef int (__stdcall *ADL2_ADAPTER_PMLOG_START)              (void* /*context*/, int /*adapter*/, ADLPMLogStartInput* /*in*/, ADLPMLogStartOutput* /*out*/, void* /*hDevice*/);
typedef int (__stdcall *ADL2_ADAPTER_PMLOG_STOP)               (void* /*context*/, int /*adapter*/, void* /*hDevice*/);
typedef int (__stdcall *ADL2_NEW_QUERYPMLOGDATA_GET)           (void* /*context*/, int /*adapter*/, ADLPMLogData* /*out*/);
