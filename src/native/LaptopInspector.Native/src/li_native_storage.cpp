#include "li_native.h"

#include "licore/li_core.h"
#include "native_common.h"

#include <cstring>
#include <exception>
#include <string>
#include <vector>

#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#include <winioctl.h>
#ifndef SMART_CMD
#define SMART_CMD 0xB0
#endif
#ifndef READ_ATTRIBUTES
#define READ_ATTRIBUTES 0xD0
#endif
#ifndef READ_ATTRIBUTE_BUFFER_SIZE
#define READ_ATTRIBUTE_BUFFER_SIZE 512
#endif
#endif

namespace {
#ifdef _WIN32
std::string storage_bus_type_name(STORAGE_BUS_TYPE type)
{
    switch (type) {
    case BusTypeUnknown: return "Unknown";
    case BusTypeScsi: return "SCSI";
    case BusTypeAtapi: return "ATAPI";
    case BusTypeAta: return "ATA";
    case BusType1394: return "IEEE1394";
    case BusTypeSsa: return "SSA";
    case BusTypeFibre: return "Fibre Channel";
    case BusTypeUsb: return "USB";
    case BusTypeRAID: return "RAID";
    case BusTypeiScsi: return "iSCSI";
    case BusTypeSas: return "SAS";
    case BusTypeSata: return "SATA";
    case BusTypeSd: return "SD";
    case BusTypeMmc: return "MMC";
    case BusTypeVirtual: return "Virtual";
    case BusTypeFileBackedVirtual: return "FileBackedVirtual";
    case BusTypeSpaces: return "Storage Spaces";
    case BusTypeNvme: return "NVMe";
    case BusTypeSCM: return "SCM";
    case BusTypeUfs: return "UFS";
    default: return "Other";
    }
}

std::string descriptor_string(const std::vector<BYTE>& buffer, DWORD offset)
{
    if (offset == 0u || offset >= buffer.size()) {
        return {};
    }

    const char* value = reinterpret_cast<const char*>(buffer.data() + offset);
    std::size_t max_length = buffer.size() - offset;
    std::size_t length = strnlen_s(value, max_length);
    return std::string(value, value + length);
}

bool device_io_control(HANDLE handle, DWORD code, void* in_buffer, DWORD in_size, void* out_buffer, DWORD out_size, DWORD* returned)
{
    DWORD local_returned = 0u;
    BOOL ok = DeviceIoControl(handle, code, in_buffer, in_size, out_buffer, out_size, returned == nullptr ? &local_returned : returned, nullptr);
    return ok != FALSE;
}

void append_storage_descriptor(li_native::JsonWriter& writer, HANDLE handle)
{
    STORAGE_PROPERTY_QUERY query{};
    query.PropertyId = StorageDeviceProperty;
    query.QueryType = PropertyStandardQuery;
    STORAGE_DESCRIPTOR_HEADER header{};
    DWORD returned = 0u;
    writer.raw(",\"deviceDescriptor\":");
    if (!device_io_control(handle, IOCTL_STORAGE_QUERY_PROPERTY, &query, sizeof(query), &header, sizeof(header), &returned) || header.Size == 0u) {
        writer.null_value();
        return;
    }

    std::vector<BYTE> buffer(header.Size);
    if (!device_io_control(handle, IOCTL_STORAGE_QUERY_PROPERTY, &query, sizeof(query), buffer.data(), static_cast<DWORD>(buffer.size()), &returned) || buffer.size() < sizeof(STORAGE_DEVICE_DESCRIPTOR)) {
        writer.null_value();
        return;
    }

    const auto* descriptor = reinterpret_cast<const STORAGE_DEVICE_DESCRIPTOR*>(buffer.data());
    writer.begin_object();
    writer.key("deviceType");
    writer.unsigned_number(descriptor->DeviceType);
    writer.raw(",\"removableMedia\":");
    writer.boolean(descriptor->RemovableMedia != FALSE);
    writer.raw(",\"commandQueueing\":");
    writer.boolean(descriptor->CommandQueueing != FALSE);
    writer.raw(",\"vendorId\":");
    writer.string(descriptor_string(buffer, descriptor->VendorIdOffset));
    writer.raw(",\"productId\":");
    writer.string(descriptor_string(buffer, descriptor->ProductIdOffset));
    writer.raw(",\"productRevision\":");
    writer.string(descriptor_string(buffer, descriptor->ProductRevisionOffset));
    writer.raw(",\"serialNumber\":");
    writer.string(descriptor_string(buffer, descriptor->SerialNumberOffset));
    writer.raw(",\"busType\":");
    writer.string(storage_bus_type_name(descriptor->BusType));
    writer.raw(",\"busTypeRaw\":");
    writer.unsigned_number(descriptor->BusType);
    writer.end_object();
}

void append_geometry(li_native::JsonWriter& writer, HANDLE handle)
{
    DISK_GEOMETRY_EX geometry{};
    DWORD returned = 0u;
    writer.raw(",\"geometry\":");
    if (!device_io_control(handle, IOCTL_DISK_GET_DRIVE_GEOMETRY_EX, nullptr, 0u, &geometry, sizeof(geometry), &returned)) {
        writer.null_value();
        return;
    }

    writer.begin_object();
    writer.key("diskSizeBytes");
    writer.unsigned_number(static_cast<unsigned long long>(geometry.DiskSize.QuadPart));
    writer.raw(",\"bytesPerSector\":");
    writer.unsigned_number(geometry.Geometry.BytesPerSector);
    writer.raw(",\"sectorsPerTrack\":");
    writer.unsigned_number(geometry.Geometry.SectorsPerTrack);
    writer.raw(",\"tracksPerCylinder\":");
    writer.unsigned_number(geometry.Geometry.TracksPerCylinder);
    writer.raw(",\"cylinders\":");
    writer.unsigned_number(static_cast<unsigned long long>(geometry.Geometry.Cylinders.QuadPart));
    writer.raw(",\"mediaType\":");
    writer.unsigned_number(geometry.Geometry.MediaType);
    writer.end_object();
}

void append_seek_penalty(li_native::JsonWriter& writer, HANDLE handle)
{
    STORAGE_PROPERTY_QUERY query{};
    query.PropertyId = StorageDeviceSeekPenaltyProperty;
    query.QueryType = PropertyStandardQuery;
    DEVICE_SEEK_PENALTY_DESCRIPTOR descriptor{};
    DWORD returned = 0u;
    writer.raw(",\"seekPenalty\":");
    if (!device_io_control(handle, IOCTL_STORAGE_QUERY_PROPERTY, &query, sizeof(query), &descriptor, sizeof(descriptor), &returned)) {
        writer.null_value();
        return;
    }

    writer.begin_object();
    writer.key("incursSeekPenalty");
    writer.boolean(descriptor.IncursSeekPenalty != FALSE);
    writer.end_object();
}

void append_write_cache(li_native::JsonWriter& writer, HANDLE handle)
{
    STORAGE_PROPERTY_QUERY query{};
    query.PropertyId = StorageDeviceWriteCacheProperty;
    query.QueryType = PropertyStandardQuery;
    STORAGE_WRITE_CACHE_PROPERTY descriptor{};
    DWORD returned = 0u;
    writer.raw(",\"writeCache\":");
    if (!device_io_control(handle, IOCTL_STORAGE_QUERY_PROPERTY, &query, sizeof(query), &descriptor, sizeof(descriptor), &returned)) {
        writer.null_value();
        return;
    }

    writer.begin_object();
    writer.key("writeCacheEnabled");
    writer.boolean(descriptor.WriteCacheEnabled == WriteCacheEnable);
    writer.raw(",\"writeCacheType\":");
    writer.unsigned_number(descriptor.WriteCacheType);
    writer.raw(",\"writeCacheChangeable\":");
    writer.unsigned_number(descriptor.WriteCacheChangeable);
    writer.raw(",\"flushCacheSupported\":");
    writer.boolean(descriptor.FlushCacheSupported != FALSE);
    writer.raw(",\"userDefinedPowerProtection\":");
    writer.boolean(descriptor.UserDefinedPowerProtection != FALSE);
    writer.end_object();
}

void append_smart_ata(li_native::JsonWriter& writer, HANDLE handle)
{
    writer.raw(",\"ataSmart\":");
    SENDCMDINPARAMS input{};
    input.irDriveRegs.bCommandReg = SMART_CMD;
    input.irDriveRegs.bFeaturesReg = READ_ATTRIBUTES;
    input.cBufferSize = READ_ATTRIBUTE_BUFFER_SIZE;
    constexpr DWORD output_size = sizeof(SENDCMDOUTPARAMS) + READ_ATTRIBUTE_BUFFER_SIZE;
    std::vector<BYTE> output(output_size);
    DWORD returned = 0u;
    if (!device_io_control(handle, SMART_RCV_DRIVE_DATA, &input, sizeof(SENDCMDINPARAMS) - 1u, output.data(), static_cast<DWORD>(output.size()), &returned)) {
        writer.begin_object();
        writer.key("status");
        writer.string("unavailable");
        writer.raw(",\"error\":");
        li_native::append_win32_error(writer, GetLastError());
        writer.end_object();
        return;
    }

    auto* command_output = reinterpret_cast<SENDCMDOUTPARAMS*>(output.data());
    li_json_output parsed{};
    int32_t parse_status = li_smart_ata_to_json(command_output->bBuffer, READ_ATTRIBUTE_BUFFER_SIZE, &parsed);
    if (parsed.data != nullptr) {
        writer.raw(parsed.data);
        li_json_output_free(&parsed);
    }
    else {
        writer.begin_object();
        writer.key("status");
        writer.string("parseFailed");
        writer.raw(",\"parseStatus\":");
        writer.signed_number(parse_status);
        writer.end_object();
    }
}

void append_nvme_health(li_native::JsonWriter& writer, HANDLE handle)
{
    writer.raw(",\"nvmeHealth\":");
    constexpr DWORD health_log_size = 512u;
    std::vector<BYTE> buffer(sizeof(STORAGE_PROPERTY_QUERY) + sizeof(STORAGE_PROTOCOL_SPECIFIC_DATA) + health_log_size, 0u);
    auto* query = reinterpret_cast<STORAGE_PROPERTY_QUERY*>(buffer.data());
    query->PropertyId = StorageDeviceProtocolSpecificProperty;
    query->QueryType = PropertyStandardQuery;
    auto* protocol = reinterpret_cast<STORAGE_PROTOCOL_SPECIFIC_DATA*>(query->AdditionalParameters);
    protocol->ProtocolType = ProtocolTypeNvme;
    protocol->DataType = NVMeDataTypeLogPage;
    protocol->ProtocolDataRequestValue = 0x02u;
    protocol->ProtocolDataRequestSubValue = 0u;
    protocol->ProtocolDataOffset = sizeof(STORAGE_PROTOCOL_SPECIFIC_DATA);
    protocol->ProtocolDataLength = health_log_size;
    DWORD returned = 0u;
    if (!device_io_control(handle, IOCTL_STORAGE_QUERY_PROPERTY, query, static_cast<DWORD>(buffer.size()), buffer.data(), static_cast<DWORD>(buffer.size()), &returned)) {
        writer.begin_object();
        writer.key("status");
        writer.string("unavailable");
        writer.raw(",\"error\":");
        li_native::append_win32_error(writer, GetLastError());
        writer.end_object();
        return;
    }

    const auto* output_protocol = reinterpret_cast<const STORAGE_PROTOCOL_SPECIFIC_DATA*>(query->AdditionalParameters);
    if (output_protocol->ProtocolDataOffset + output_protocol->ProtocolDataLength > buffer.size() || output_protocol->ProtocolDataLength < health_log_size) {
        writer.raw("{\"status\":\"invalidProtocolResponse\"}");
        return;
    }

    const uint8_t* health_log = buffer.data() + output_protocol->ProtocolDataOffset;
    li_json_output parsed{};
    int32_t parse_status = li_nvme_health_to_json(health_log, health_log_size, &parsed);
    if (parsed.data != nullptr) {
        writer.raw(parsed.data);
        li_json_output_free(&parsed);
    }
    else {
        writer.begin_object();
        writer.key("status");
        writer.string("parseFailed");
        writer.raw(",\"parseStatus\":");
        writer.signed_number(parse_status);
        writer.end_object();
    }
}
#endif
}  // namespace

extern "C" LI_EXPORT int32_t li_collect_storage_json(char** output) noexcept
{
    if (output == nullptr) {
        li_native::set_last_error("Аргумент output равен null.");
        return -1;
    }

    *output = nullptr;
#ifndef _WIN32
    return li_native::return_json_string(output, li_native::unsupported_json("storage", "IOCTL накопителей доступен только на Windows."));
#else
    try {
        li_native::JsonWriter writer;
        writer.raw("{\"collector\":\"storage\",\"status\":\"ok\",\"source\":\"CreateFile+DeviceIoControl\",\"physicalDrives\":[");
        bool first = true;
        unsigned long count = 0u;
        for (DWORD index = 0u; index < 64u; ++index) {
            std::wstring path = L"\\\\.\\PhysicalDrive" + std::to_wstring(index);
            HANDLE handle = CreateFileW(path.c_str(), FILE_READ_ATTRIBUTES | GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, nullptr, OPEN_EXISTING, 0u, nullptr);
            if (handle == INVALID_HANDLE_VALUE) {
                continue;
            }

            struct HandleGuard final {
                HANDLE value;
                ~HandleGuard() { CloseHandle(value); }
            } guard{ handle };

            writer.comma(first);
            writer.begin_object();
            writer.key("index");
            writer.unsigned_number(index);
            writer.raw(",\"path\":");
            writer.string(li_native::wide_to_utf8(path));
            append_storage_descriptor(writer, handle);
            append_geometry(writer, handle);
            append_seek_penalty(writer, handle);
            append_write_cache(writer, handle);
            append_smart_ata(writer, handle);
            append_nvme_health(writer, handle);
            writer.end_object();
            ++count;
        }

        writer.raw("],\"driveCount\":");
        writer.unsigned_number(count);
        writer.raw("}");
        return li_native::return_json_string(output, writer.str());
    }
    catch (const std::exception& exception) {
        return li_native::return_error_json(output, "storage", exception.what());
    }
    catch (...) {
        return li_native::return_error_json(output, "storage", "Необработанное исключение при сборе накопителей.");
    }
#endif
}
