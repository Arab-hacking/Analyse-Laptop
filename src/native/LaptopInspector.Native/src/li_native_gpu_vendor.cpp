#include "li_native.h"
#include "native_common.h"

#include <exception>
#include <string>

#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#endif

namespace {
#ifdef _WIN32
struct ModuleGuard final {
    HMODULE value;
    ~ModuleGuard() { if (value != nullptr) { FreeLibrary(value); } }
};

using NvmlDevice = void*;
using NvmlReturn = int;

struct NvmlUtilization final {
    unsigned int gpu;
    unsigned int memory;
};

using NvmlInit = NvmlReturn(__cdecl*)();
using NvmlShutdown = NvmlReturn(__cdecl*)();
using NvmlDeviceGetCount = NvmlReturn(__cdecl*)(unsigned int*);
using NvmlDeviceGetHandleByIndex = NvmlReturn(__cdecl*)(unsigned int, NvmlDevice*);
using NvmlDeviceGetName = NvmlReturn(__cdecl*)(NvmlDevice, char*, unsigned int);
using NvmlDeviceGetTemperature = NvmlReturn(__cdecl*)(NvmlDevice, unsigned int, unsigned int*);
using NvmlDeviceGetUtilizationRates = NvmlReturn(__cdecl*)(NvmlDevice, NvmlUtilization*);
using NvmlDeviceGetClockInfo = NvmlReturn(__cdecl*)(NvmlDevice, unsigned int, unsigned int*);
using NvmlDeviceGetPowerManagementLimit = NvmlReturn(__cdecl*)(NvmlDevice, unsigned int*);

void append_dll_probe(li_native::JsonWriter& writer, const wchar_t* dll_name)
{
    HMODULE module = LoadLibraryW(dll_name);
    writer.begin_object();
    writer.key("dll");
    writer.string(li_native::wide_to_utf8(dll_name));
    writer.raw(",\"present\":");
    writer.boolean(module != nullptr);
    if (module == nullptr) {
        writer.raw(",\"error\":");
        li_native::append_win32_error(writer, GetLastError());
    }
    else {
        FreeLibrary(module);
    }

    writer.end_object();
}

void append_nvml(li_native::JsonWriter& writer)
{
    writer.raw("\"nvml\":");
    HMODULE module = LoadLibraryW(L"nvml.dll");
    if (module == nullptr) {
        writer.begin_object();
        writer.key("available");
        writer.boolean(false);
        writer.raw(",\"error\":");
        li_native::append_win32_error(writer, GetLastError());
        writer.end_object();
        return;
    }

    ModuleGuard module_guard{ module };
    auto init = reinterpret_cast<NvmlInit>(GetProcAddress(module, "nvmlInit_v2"));
    auto shutdown = reinterpret_cast<NvmlShutdown>(GetProcAddress(module, "nvmlShutdown"));
    auto get_count = reinterpret_cast<NvmlDeviceGetCount>(GetProcAddress(module, "nvmlDeviceGetCount_v2"));
    auto get_handle = reinterpret_cast<NvmlDeviceGetHandleByIndex>(GetProcAddress(module, "nvmlDeviceGetHandleByIndex_v2"));
    auto get_name = reinterpret_cast<NvmlDeviceGetName>(GetProcAddress(module, "nvmlDeviceGetName"));
    auto get_temperature = reinterpret_cast<NvmlDeviceGetTemperature>(GetProcAddress(module, "nvmlDeviceGetTemperature"));
    auto get_utilization = reinterpret_cast<NvmlDeviceGetUtilizationRates>(GetProcAddress(module, "nvmlDeviceGetUtilizationRates"));
    auto get_clock = reinterpret_cast<NvmlDeviceGetClockInfo>(GetProcAddress(module, "nvmlDeviceGetClockInfo"));
    auto get_power_limit = reinterpret_cast<NvmlDeviceGetPowerManagementLimit>(GetProcAddress(module, "nvmlDeviceGetPowerManagementLimit"));

    writer.begin_object();
    writer.key("available");
    writer.boolean(true);
    writer.raw(",\"entryPointsAvailable\":");
    bool entry_points = init != nullptr && shutdown != nullptr && get_count != nullptr && get_handle != nullptr;
    writer.boolean(entry_points);
    if (!entry_points) {
        writer.raw(",\"devices\":[]}");
        return;
    }

    NvmlReturn init_result = init();
    writer.raw(",\"initResult\":");
    writer.signed_number(init_result);
    writer.raw(",\"devices\":[");
    if (init_result == 0) {
        unsigned int count = 0u;
        if (get_count(&count) == 0) {
            bool first = true;
            for (unsigned int index = 0u; index < count; ++index) {
                NvmlDevice device = nullptr;
                if (get_handle(index, &device) != 0 || device == nullptr) {
                    continue;
                }

                writer.comma(first);
                writer.begin_object();
                writer.key("index");
                writer.unsigned_number(index);
                if (get_name != nullptr) {
                    char name[128]{};
                    if (get_name(device, name, sizeof(name)) == 0) {
                        writer.raw(",\"name\":");
                        writer.string(name);
                    }
                }

                if (get_temperature != nullptr) {
                    unsigned int temperature = 0u;
                    if (get_temperature(device, 0u, &temperature) == 0) {
                        writer.raw(",\"temperatureCelsius\":");
                        writer.unsigned_number(temperature);
                    }
                }

                if (get_utilization != nullptr) {
                    NvmlUtilization utilization{};
                    if (get_utilization(device, &utilization) == 0) {
                        writer.raw(",\"utilization\":{\"gpuPercent\":");
                        writer.unsigned_number(utilization.gpu);
                        writer.raw(",\"memoryPercent\":");
                        writer.unsigned_number(utilization.memory);
                        writer.raw("}");
                    }
                }

                if (get_clock != nullptr) {
                    unsigned int graphics_clock = 0u;
                    unsigned int memory_clock = 0u;
                    if (get_clock(device, 0u, &graphics_clock) == 0) {
                        writer.raw(",\"graphicsClockMHz\":");
                        writer.unsigned_number(graphics_clock);
                    }

                    if (get_clock(device, 2u, &memory_clock) == 0) {
                        writer.raw(",\"memoryClockMHz\":");
                        writer.unsigned_number(memory_clock);
                    }
                }

                if (get_power_limit != nullptr) {
                    unsigned int milliwatts = 0u;
                    if (get_power_limit(device, &milliwatts) == 0) {
                        writer.raw(",\"powerLimitMilliwatts\":");
                        writer.unsigned_number(milliwatts);
                    }
                }

                writer.end_object();
            }
        }

        shutdown();
    }

    writer.raw("]}");
}

void append_vendor_dlls(li_native::JsonWriter& writer)
{
    writer.raw(",\"vendorDllProbes\":[");
    const wchar_t* dlls[] = {
        L"amdadlx64.dll",
        L"atiadlxx.dll",
        L"atiadlxy.dll",
        L"igcl.dll",
        L"IntelGraphicsControlLib.dll"
    };
    bool first = true;
    for (const wchar_t* dll : dlls) {
        writer.comma(first);
        append_dll_probe(writer, dll);
    }

    writer.raw("]");
}
#endif
}  // namespace

extern "C" LI_EXPORT int32_t li_collect_gpu_vendor_json(char** output) noexcept
{
    if (output == nullptr) {
        li_native::set_last_error("Аргумент output равен null.");
        return -1;
    }

    *output = nullptr;
#ifndef _WIN32
    return li_native::return_json_string(output, li_native::unsupported_json("gpuVendor", "NVML/ADLX/IGCL probes доступны только на Windows."));
#else
    try {
        li_native::JsonWriter writer;
        writer.raw("{\"collector\":\"gpuVendor\",\"status\":\"ok\",\"source\":\"dynamic vendor DLL probes\",");
        append_nvml(writer);
        append_vendor_dlls(writer);
        writer.raw("}");
        return li_native::return_json_string(output, writer.str());
    }
    catch (const std::exception& exception) {
        return li_native::return_error_json(output, "gpuVendor", exception.what());
    }
    catch (...) {
        return li_native::return_error_json(output, "gpuVendor", "Необработанное исключение при сборе vendor GPU probes.");
    }
#endif
}
