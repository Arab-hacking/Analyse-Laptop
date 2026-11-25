#include "li_native.h"
#include "native_common.h"

#include <cstdint>
#include <exception>
#include <string>
#include <vector>

#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#include <dxgi1_6.h>
#endif

namespace {
#ifdef _WIN32
std::string dxgi_format_name(DXGI_FORMAT format)
{
    switch (format) {
    case DXGI_FORMAT_R8G8B8A8_UNORM: return "R8G8B8A8_UNORM";
    case DXGI_FORMAT_B8G8R8A8_UNORM: return "B8G8R8A8_UNORM";
    case DXGI_FORMAT_R10G10B10A2_UNORM: return "R10G10B10A2_UNORM";
    case DXGI_FORMAT_R16G16B16A16_FLOAT: return "R16G16B16A16_FLOAT";
    default: return "Other";
    }
}

std::string rotation_name(DXGI_MODE_ROTATION rotation)
{
    switch (rotation) {
    case DXGI_MODE_ROTATION_UNSPECIFIED: return "Unspecified";
    case DXGI_MODE_ROTATION_IDENTITY: return "Identity";
    case DXGI_MODE_ROTATION_ROTATE90: return "Rotate90";
    case DXGI_MODE_ROTATION_ROTATE180: return "Rotate180";
    case DXGI_MODE_ROTATION_ROTATE270: return "Rotate270";
    default: return "Unknown";
    }
}

std::string scaling_name(DISPLAYCONFIG_SCALING scaling)
{
    switch (scaling) {
    case DISPLAYCONFIG_SCALING_IDENTITY: return "Identity";
    case DISPLAYCONFIG_SCALING_CENTERED: return "Centered";
    case DISPLAYCONFIG_SCALING_STRETCHED: return "Stretched";
    case DISPLAYCONFIG_SCALING_ASPECTRATIOCENTEREDMAX: return "AspectRatioCenteredMax";
    case DISPLAYCONFIG_SCALING_CUSTOM: return "Custom";
    case DISPLAYCONFIG_SCALING_PREFERRED: return "Preferred";
    default: return "Unknown";
    }
}

void append_adapter_outputs(li_native::JsonWriter& writer, IDXGIAdapter1* adapter)
{
    writer.raw(",\"outputs\":[");
    bool first = true;
    for (UINT output_index = 0u;; ++output_index) {
        IDXGIOutput* output = nullptr;
        HRESULT hr = adapter->EnumOutputs(output_index, &output);
        if (hr == DXGI_ERROR_NOT_FOUND) {
            break;
        }

        if (FAILED(hr) || output == nullptr) {
            continue;
        }

        DXGI_OUTPUT_DESC desc{};
        output->GetDesc(&desc);
        writer.comma(first);
        writer.begin_object();
        writer.key("deviceName");
        writer.string(li_native::wide_to_utf8(desc.DeviceName));
        writer.raw(",\"attachedToDesktop\":");
        writer.boolean(desc.AttachedToDesktop != FALSE);
        writer.raw(",\"desktopCoordinates\":{");
        writer.raw("\"left\":");
        writer.signed_number(desc.DesktopCoordinates.left);
        writer.raw(",\"top\":");
        writer.signed_number(desc.DesktopCoordinates.top);
        writer.raw(",\"right\":");
        writer.signed_number(desc.DesktopCoordinates.right);
        writer.raw(",\"bottom\":");
        writer.signed_number(desc.DesktopCoordinates.bottom);
        writer.raw("},\"rotation\":");
        writer.string(rotation_name(desc.Rotation));
        writer.raw(",\"monitorHandle\":");
        writer.unsigned_number(reinterpret_cast<std::uintptr_t>(desc.Monitor));
        writer.end_object();
        output->Release();
    }

    writer.end_array();
}

void append_dxgi(li_native::JsonWriter& writer)
{
    writer.raw("\"dxgiAdapters\":[");
    IDXGIFactory1* factory = nullptr;
    HRESULT hr = CreateDXGIFactory1(__uuidof(IDXGIFactory1), reinterpret_cast<void**>(&factory));
    if (FAILED(hr) || factory == nullptr) {
        writer.raw("],\"dxgiError\":");
        writer.string(li_native::hresult_message(hr));
        return;
    }

    bool first = true;
    for (UINT index = 0u;; ++index) {
        IDXGIAdapter1* adapter = nullptr;
        hr = factory->EnumAdapters1(index, &adapter);
        if (hr == DXGI_ERROR_NOT_FOUND) {
            break;
        }

        if (FAILED(hr) || adapter == nullptr) {
            continue;
        }

        DXGI_ADAPTER_DESC1 desc{};
        adapter->GetDesc1(&desc);
        writer.comma(first);
        writer.begin_object();
        writer.key("description");
        writer.string(li_native::wide_to_utf8(desc.Description));
        writer.raw(",\"vendorId\":");
        writer.unsigned_number(desc.VendorId);
        writer.raw(",\"deviceId\":");
        writer.unsigned_number(desc.DeviceId);
        writer.raw(",\"subSysId\":");
        writer.unsigned_number(desc.SubSysId);
        writer.raw(",\"revision\":");
        writer.unsigned_number(desc.Revision);
        writer.raw(",\"dedicatedVideoMemoryBytes\":");
        writer.unsigned_number(desc.DedicatedVideoMemory);
        writer.raw(",\"dedicatedSystemMemoryBytes\":");
        writer.unsigned_number(desc.DedicatedSystemMemory);
        writer.raw(",\"sharedSystemMemoryBytes\":");
        writer.unsigned_number(desc.SharedSystemMemory);
        writer.raw(",\"flags\":");
        writer.unsigned_number(desc.Flags);
        append_adapter_outputs(writer, adapter);
        writer.end_object();
        adapter->Release();
    }

    factory->Release();
    writer.end_array();
}

void append_display_config(li_native::JsonWriter& writer)
{
    writer.raw(",\"displayConfig\":");
    UINT32 path_count = 0u;
    UINT32 mode_count = 0u;
    LONG status = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, &path_count, &mode_count);
    if (status != ERROR_SUCCESS) {
        writer.begin_object();
        writer.key("status");
        writer.string("unavailable");
        writer.raw(",\"errorCode\":");
        writer.signed_number(status);
        writer.end_object();
        return;
    }

    std::vector<DISPLAYCONFIG_PATH_INFO> paths(path_count);
    std::vector<DISPLAYCONFIG_MODE_INFO> modes(mode_count);
    status = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, &path_count, paths.data(), &mode_count, modes.data(), nullptr);
    if (status != ERROR_SUCCESS) {
        writer.begin_object();
        writer.key("status");
        writer.string("queryFailed");
        writer.raw(",\"errorCode\":");
        writer.signed_number(status);
        writer.end_object();
        return;
    }

    writer.begin_object();
    writer.key("pathCount");
    writer.unsigned_number(path_count);
    writer.raw(",\"modeCount\":");
    writer.unsigned_number(mode_count);
    writer.raw(",\"paths\":[");
    bool first = true;
    for (UINT32 index = 0u; index < path_count; ++index) {
        const auto& path = paths[index];
        writer.comma(first);
        writer.begin_object();
        writer.key("sourceAdapterId");
        writer.unsigned_number(static_cast<unsigned long long>(path.sourceInfo.adapterId.HighPart) << 32u | static_cast<unsigned long long>(path.sourceInfo.adapterId.LowPart));
        writer.raw(",\"sourceId\":");
        writer.unsigned_number(path.sourceInfo.id);
        writer.raw(",\"targetId\":");
        writer.unsigned_number(path.targetInfo.id);
        writer.raw(",\"targetAvailable\":");
        writer.boolean(path.targetInfo.targetAvailable != FALSE);
        writer.raw(",\"outputTechnology\":");
        writer.unsigned_number(path.targetInfo.outputTechnology);
        writer.raw(",\"rotation\":");
        writer.unsigned_number(path.targetInfo.rotation);
        writer.raw(",\"scaling\":");
        writer.string(scaling_name(path.targetInfo.scaling));
        writer.raw(",\"refreshRate\":");
        if (path.targetInfo.refreshRate.Denominator != 0u) {
            writer.double_number(static_cast<double>(path.targetInfo.refreshRate.Numerator) / static_cast<double>(path.targetInfo.refreshRate.Denominator));
        }
        else {
            writer.null_value();
        }

        writer.end_object();
    }

    writer.raw("]}");
}
#endif
}  // namespace

extern "C" LI_EXPORT int32_t li_collect_display_json(char** output) noexcept
{
    if (output == nullptr) {
        li_native::set_last_error("Аргумент output равен null.");
        return -1;
    }

    *output = nullptr;
#ifndef _WIN32
    return li_native::return_json_string(output, li_native::unsupported_json("display", "DXGI и DisplayConfig доступны только на Windows."));
#else
    try {
        li_native::JsonWriter writer;
        writer.raw("{\"collector\":\"display\",\"status\":\"ok\",\"source\":\"DXGI+DisplayConfig\",");
        append_dxgi(writer);
        append_display_config(writer);
        writer.raw("}");
        return li_native::return_json_string(output, writer.str());
    }
    catch (const std::exception& exception) {
        return li_native::return_error_json(output, "display", exception.what());
    }
    catch (...) {
        return li_native::return_error_json(output, "display", "Необработанное исключение при сборе дисплеев.");
    }
#endif
}
