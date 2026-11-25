#include "li_internal.h"

#include <stdio.h>
#include <string.h>

typedef struct smbios_table_view {
    const uint8_t* data;
    uintptr_t length;
    uint8_t major;
    uint8_t minor;
    uint8_t dmi_revision;
    int has_raw_header;
} smbios_table_view;

static const char* smbios_type_name(uint8_t type)
{
    static const char* names[] = {
        "BIOS Information",
        "System Information",
        "Baseboard Information",
        "System Enclosure",
        "Processor Information",
        "Memory Controller Information",
        "Memory Module Information",
        "Cache Information",
        "Port Connector Information",
        "System Slots",
        "On Board Devices Information",
        "OEM Strings",
        "System Configuration Options",
        "BIOS Language Information",
        "Group Associations",
        "System Event Log",
        "Physical Memory Array",
        "Memory Device",
        "32-bit Memory Error Information",
        "Memory Array Mapped Address",
        "Memory Device Mapped Address",
        "Built-in Pointing Device",
        "Portable Battery",
        "System Reset",
        "Hardware Security",
        "System Power Controls",
        "Voltage Probe",
        "Cooling Device",
        "Temperature Probe",
        "Electrical Current Probe",
        "Out-of-band Remote Access",
        "Boot Integrity Services",
        "System Boot Information",
        "64-bit Memory Error Information",
        "Management Device",
        "Management Device Component",
        "Management Device Threshold Data",
        "Memory Channel",
        "IPMI Device Information",
        "System Power Supply",
        "Additional Information",
        "Onboard Devices Extended Information",
        "Management Controller Host Interface",
        "TPM Device",
        "Processor Additional Information",
        "Firmware Inventory Information",
        "String Property"
    };

    if (type < LI_ARRAY_LEN(names)) {
        return names[type];
    }

    if (type >= 128u) {
        return "OEM-specific Structure";
    }

    return "Unknown SMBIOS Structure";
}

static const char* smbios_chassis_type_name(uint8_t type)
{
    switch (type & 0x7Fu) {
    case 0x01u: return "Other";
    case 0x02u: return "Unknown";
    case 0x03u: return "Desktop";
    case 0x04u: return "Low Profile Desktop";
    case 0x05u: return "Pizza Box";
    case 0x06u: return "Mini Tower";
    case 0x07u: return "Tower";
    case 0x08u: return "Portable";
    case 0x09u: return "Laptop";
    case 0x0Au: return "Notebook";
    case 0x0Bu: return "Hand Held";
    case 0x0Cu: return "Docking Station";
    case 0x0Du: return "All In One";
    case 0x0Eu: return "Sub Notebook";
    case 0x1Eu: return "Tablet";
    case 0x1Fu: return "Convertible";
    case 0x20u: return "Detachable";
    default: return "Unspecified";
    }
}

static const char* smbios_memory_type_name(uint8_t type)
{
    switch (type) {
    case 0x01u: return "Other";
    case 0x02u: return "Unknown";
    case 0x03u: return "DRAM";
    case 0x04u: return "EDRAM";
    case 0x05u: return "VRAM";
    case 0x06u: return "SRAM";
    case 0x07u: return "RAM";
    case 0x09u: return "ROM";
    case 0x0Fu: return "SDRAM";
    case 0x12u: return "DDR";
    case 0x13u: return "DDR2";
    case 0x18u: return "DDR3";
    case 0x1Au: return "DDR4";
    case 0x1Bu: return "LPDDR";
    case 0x1Cu: return "LPDDR2";
    case 0x1Du: return "LPDDR3";
    case 0x1Eu: return "LPDDR4";
    case 0x22u: return "DDR5";
    case 0x23u: return "LPDDR5";
    default: return "Unspecified";
    }
}

static const char* smbios_form_factor_name(uint8_t type)
{
    switch (type) {
    case 0x01u: return "Other";
    case 0x02u: return "Unknown";
    case 0x03u: return "SIMM";
    case 0x04u: return "SIP";
    case 0x05u: return "Chip";
    case 0x06u: return "DIP";
    case 0x08u: return "DIMM";
    case 0x09u: return "TSOP";
    case 0x0Au: return "PGA";
    case 0x0Bu: return "RIMM";
    case 0x0Cu: return "SODIMM";
    case 0x0Fu: return "FB-DIMM";
    case 0x10u: return "Die";
    default: return "Unspecified";
    }
}

static smbios_table_view smbios_get_table_view(const uint8_t* data, uintptr_t length)
{
    smbios_table_view view;
    view.data = data;
    view.length = length;
    view.major = 0u;
    view.minor = 0u;
    view.dmi_revision = 0u;
    view.has_raw_header = 0;

    if (data != NULL && length >= 8u) {
        uint32_t raw_length = li_read_le32(data + 4u);
        if (data[1] >= 2u && data[1] <= 4u && raw_length > 0u && (uintptr_t)raw_length <= length - 8u) {
            view.data = data + 8u;
            view.length = (uintptr_t)raw_length;
            view.major = data[1];
            view.minor = data[2];
            view.dmi_revision = data[3];
            view.has_raw_header = 1;
        }
    }

    return view;
}

static uintptr_t smbios_structure_end(const uint8_t* table, uintptr_t table_length, uintptr_t structure_offset, uint8_t formatted_length, int* complete)
{
    uintptr_t cursor = structure_offset + (uintptr_t)formatted_length;
    *complete = 0;
    if (cursor >= table_length) {
        return table_length;
    }

    while (cursor + 1u < table_length) {
        if (table[cursor] == 0u && table[cursor + 1u] == 0u) {
            *complete = 1;
            return cursor + 2u;
        }

        ++cursor;
    }

    return table_length;
}

static void smbios_copy_string(const uint8_t* strings, uintptr_t strings_length, uint8_t index, char* destination, uintptr_t destination_size)
{
    if (destination == NULL || destination_size == 0u) {
        return;
    }

    destination[0] = '\0';
    if (strings == NULL || strings_length == 0u || index == 0u) {
        return;
    }

    uint8_t current = 1u;
    uintptr_t offset = 0u;
    while (offset < strings_length && strings[offset] != 0u) {
        uintptr_t start = offset;
        while (offset < strings_length && strings[offset] != 0u) {
            ++offset;
        }

        if (current == index) {
            li_fixed_ascii(destination, destination_size, strings + start, offset - start);
            return;
        }

        ++current;
        ++offset;
    }
}

static void append_string_field(li_json_builder* builder, const char* key, const uint8_t* structure, uint8_t structure_length, uint8_t field_offset, const uint8_t* strings, uintptr_t strings_length)
{
    char value[256];
    value[0] = '\0';
    if (field_offset < structure_length) {
        smbios_copy_string(strings, strings_length, structure[field_offset], value, sizeof(value));
    }

    li_json_append_format(builder, ",\"%s\":", key);
    li_json_append_escaped(builder, value);
}

static void append_u8_field(li_json_builder* builder, const char* key, const uint8_t* structure, uint8_t structure_length, uint8_t field_offset)
{
    li_json_append_format(builder, ",\"%s\":", key);
    if (field_offset < structure_length) {
        li_json_append_format(builder, "%u", (unsigned int)structure[field_offset]);
    }
    else {
        li_json_append(builder, "null");
    }
}

static void append_u16_field(li_json_builder* builder, const char* key, const uint8_t* structure, uint8_t structure_length, uint8_t field_offset)
{
    li_json_append_format(builder, ",\"%s\":", key);
    if ((uintptr_t)field_offset + 1u < structure_length) {
        li_json_append_format(builder, "%u", (unsigned int)li_read_le16(structure + field_offset));
    }
    else {
        li_json_append(builder, "null");
    }
}

static void append_u32_field(li_json_builder* builder, const char* key, const uint8_t* structure, uint8_t structure_length, uint8_t field_offset)
{
    li_json_append_format(builder, ",\"%s\":", key);
    if ((uintptr_t)field_offset + 3u < structure_length) {
        li_json_append_format(builder, "%lu", (unsigned long)li_read_le32(structure + field_offset));
    }
    else {
        li_json_append(builder, "null");
    }
}

static void append_uuid_field(li_json_builder* builder, const uint8_t* structure, uint8_t structure_length)
{
    li_json_append(builder, ",\"uuid\":");
    if (structure_length < 24u) {
        li_json_append(builder, "null");
        return;
    }

    const uint8_t* uuid = structure + 8u;
    int all_zero = 1;
    int all_ff = 1;
    for (uintptr_t index = 0u; index < 16u; ++index) {
        if (uuid[index] != 0u) {
            all_zero = 0;
        }

        if (uuid[index] != 0xFFu) {
            all_ff = 0;
        }
    }

    if (all_zero || all_ff) {
        li_json_append(builder, "null");
        return;
    }

    li_json_append_format(builder,
        "\"%02x%02x%02x%02x-%02x%02x-%02x%02x-%02x%02x-%02x%02x%02x%02x%02x%02x\"",
        uuid[3], uuid[2], uuid[1], uuid[0], uuid[5], uuid[4], uuid[7], uuid[6],
        uuid[8], uuid[9], uuid[10], uuid[11], uuid[12], uuid[13], uuid[14], uuid[15]);
}

static void append_strings_array(li_json_builder* builder, const uint8_t* strings, uintptr_t strings_length)
{
    li_json_append(builder, ",\"strings\":[");
    uintptr_t offset = 0u;
    int first = 1;
    while (offset < strings_length && strings[offset] != 0u) {
        uintptr_t start = offset;
        while (offset < strings_length && strings[offset] != 0u) {
            ++offset;
        }

        if (!first) {
            li_json_append_char(builder, ',');
        }

        first = 0;
        li_json_append_escaped_len(builder, (const char*)(strings + start), offset - start);
        ++offset;
    }

    li_json_append_char(builder, ']');
}

static void append_bios_characteristics(li_json_builder* builder, const uint8_t* structure, uint8_t structure_length)
{
    static const char* names[] = {
        "Reserved", "Reserved", "Unknown", "BIOS Characteristics Not Supported", "ISA", "MCA", "EISA", "PCI",
        "PC Card", "PNP", "APM", "Upgradeable BIOS", "Shadowing", "VL-VESA", "ESCD", "Boot From CD",
        "Selectable Boot", "BIOS ROM Socketed", "Boot From PC Card", "EDD", "Japanese Floppy NEC 9800",
        "Japanese Floppy Toshiba", "5.25 360KB Floppy", "5.25 1.2MB Floppy", "3.5 720KB Floppy",
        "3.5 2.88MB Floppy", "Print Screen", "Keyboard 8042", "Serial", "Printer", "CGA/Mono Video",
        "NEC PC-98"
    };

    li_json_append(builder, ",\"characteristics\":[");
    if (structure_length >= 18u) {
        uint64_t bits = li_read_le64(structure + 10u);
        int first = 1;
        for (uintptr_t bit = 0u; bit < LI_ARRAY_LEN(names); ++bit) {
            if ((bits & ((uint64_t)1u << bit)) != 0u) {
                if (!first) {
                    li_json_append_char(builder, ',');
                }

                first = 0;
                li_json_append_escaped(builder, names[bit]);
            }
        }
    }

    li_json_append_char(builder, ']');
}

static void append_memory_size(li_json_builder* builder, const uint8_t* structure, uint8_t structure_length)
{
    li_json_append(builder, ",\"sizeBytes\":");
    if (structure_length < 14u) {
        li_json_append(builder, "null");
        return;
    }

    uint16_t size = li_read_le16(structure + 12u);
    if (size == 0u) {
        li_json_append(builder, "0");
    }
    else if (size == 0xFFFFu) {
        li_json_append(builder, "null");
    }
    else if (size == 0x7FFFu && structure_length >= 32u) {
        uint32_t extended_mb = li_read_le32(structure + 28u);
        li_json_append_format(builder, "%llu", (unsigned long long)extended_mb * 1024ull * 1024ull);
    }
    else if ((size & 0x8000u) != 0u) {
        uint64_t kb = (uint64_t)(size & 0x7FFFu);
        li_json_append_format(builder, "%llu", (unsigned long long)kb * 1024ull);
    }
    else {
        uint64_t mb = (uint64_t)size;
        li_json_append_format(builder, "%llu", (unsigned long long)mb * 1024ull * 1024ull);
    }
}

static void append_processor_id(li_json_builder* builder, const uint8_t* structure, uint8_t structure_length)
{
    li_json_append(builder, ",\"processorIdHex\":");
    if (structure_length >= 16u) {
        li_json_append_hex(builder, structure + 8u, 8u);
    }
    else {
        li_json_append(builder, "null");
    }
}

static void append_type_specific_data(li_json_builder* builder, const uint8_t* structure, uint8_t structure_length, const uint8_t* strings, uintptr_t strings_length)
{
    uint8_t type = structure[0];
    switch (type) {
    case 0u:
        append_string_field(builder, "vendor", structure, structure_length, 4u, strings, strings_length);
        append_string_field(builder, "version", structure, structure_length, 5u, strings, strings_length);
        append_string_field(builder, "releaseDate", structure, structure_length, 8u, strings, strings_length);
        append_u8_field(builder, "romSizeRaw", structure, structure_length, 9u);
        append_bios_characteristics(builder, structure, structure_length);
        append_u8_field(builder, "systemBiosMajorRelease", structure, structure_length, 20u);
        append_u8_field(builder, "systemBiosMinorRelease", structure, structure_length, 21u);
        append_u8_field(builder, "embeddedControllerMajorRelease", structure, structure_length, 22u);
        append_u8_field(builder, "embeddedControllerMinorRelease", structure, structure_length, 23u);
        break;
    case 1u:
        append_string_field(builder, "manufacturer", structure, structure_length, 4u, strings, strings_length);
        append_string_field(builder, "productName", structure, structure_length, 5u, strings, strings_length);
        append_string_field(builder, "version", structure, structure_length, 6u, strings, strings_length);
        append_string_field(builder, "serialNumber", structure, structure_length, 7u, strings, strings_length);
        append_uuid_field(builder, structure, structure_length);
        append_u8_field(builder, "wakeUpType", structure, structure_length, 24u);
        append_string_field(builder, "skuNumber", structure, structure_length, 25u, strings, strings_length);
        append_string_field(builder, "family", structure, structure_length, 26u, strings, strings_length);
        break;
    case 2u:
        append_string_field(builder, "manufacturer", structure, structure_length, 4u, strings, strings_length);
        append_string_field(builder, "product", structure, structure_length, 5u, strings, strings_length);
        append_string_field(builder, "version", structure, structure_length, 6u, strings, strings_length);
        append_string_field(builder, "serialNumber", structure, structure_length, 7u, strings, strings_length);
        append_string_field(builder, "assetTag", structure, structure_length, 8u, strings, strings_length);
        append_u8_field(builder, "featureFlags", structure, structure_length, 9u);
        append_string_field(builder, "locationInChassis", structure, structure_length, 10u, strings, strings_length);
        append_u16_field(builder, "chassisHandle", structure, structure_length, 11u);
        append_u8_field(builder, "boardType", structure, structure_length, 13u);
        break;
    case 3u:
        append_string_field(builder, "manufacturer", structure, structure_length, 4u, strings, strings_length);
        append_u8_field(builder, "chassisTypeRaw", structure, structure_length, 5u);
        li_json_append(builder, ",\"chassisTypeName\":");
        li_json_append_escaped(builder, structure_length > 5u ? smbios_chassis_type_name(structure[5u]) : "Unknown");
        li_json_append(builder, ",\"chassisLockPresent\":");
        li_json_append_bool(builder, structure_length > 5u && (structure[5u] & 0x80u) != 0u);
        append_string_field(builder, "version", structure, structure_length, 6u, strings, strings_length);
        append_string_field(builder, "serialNumber", structure, structure_length, 7u, strings, strings_length);
        append_string_field(builder, "assetTag", structure, structure_length, 8u, strings, strings_length);
        append_u8_field(builder, "bootupState", structure, structure_length, 9u);
        append_u8_field(builder, "powerSupplyState", structure, structure_length, 10u);
        append_u8_field(builder, "thermalState", structure, structure_length, 11u);
        append_u8_field(builder, "securityStatus", structure, structure_length, 12u);
        break;
    case 4u:
        append_string_field(builder, "socketDesignation", structure, structure_length, 4u, strings, strings_length);
        append_u8_field(builder, "processorType", structure, structure_length, 5u);
        append_u8_field(builder, "processorFamily", structure, structure_length, 6u);
        append_string_field(builder, "manufacturer", structure, structure_length, 7u, strings, strings_length);
        append_processor_id(builder, structure, structure_length);
        append_string_field(builder, "version", structure, structure_length, 16u, strings, strings_length);
        append_u16_field(builder, "externalClockMHz", structure, structure_length, 18u);
        append_u16_field(builder, "maxSpeedMHz", structure, structure_length, 20u);
        append_u16_field(builder, "currentSpeedMHz", structure, structure_length, 22u);
        append_u8_field(builder, "status", structure, structure_length, 24u);
        append_u8_field(builder, "upgrade", structure, structure_length, 25u);
        append_u8_field(builder, "coreCount", structure, structure_length, 35u);
        append_u8_field(builder, "coreEnabled", structure, structure_length, 36u);
        append_u8_field(builder, "threadCount", structure, structure_length, 37u);
        break;
    case 16u:
        append_u8_field(builder, "location", structure, structure_length, 4u);
        append_u8_field(builder, "use", structure, structure_length, 5u);
        append_u8_field(builder, "errorCorrection", structure, structure_length, 6u);
        append_u32_field(builder, "maximumCapacityKilobytes", structure, structure_length, 7u);
        append_u16_field(builder, "numberOfMemoryDevices", structure, structure_length, 13u);
        if (structure_length >= 23u) {
            li_json_append(builder, ",\"extendedMaximumCapacityBytes\":");
            li_json_append_format(builder, "%llu", (unsigned long long)li_read_le64(structure + 15u));
        }
        break;
    case 17u:
        append_u16_field(builder, "totalWidthBits", structure, structure_length, 8u);
        append_u16_field(builder, "dataWidthBits", structure, structure_length, 10u);
        append_memory_size(builder, structure, structure_length);
        append_u8_field(builder, "formFactor", structure, structure_length, 14u);
        li_json_append(builder, ",\"formFactorName\":");
        li_json_append_escaped(builder, structure_length > 14u ? smbios_form_factor_name(structure[14u]) : "Unknown");
        append_string_field(builder, "deviceLocator", structure, structure_length, 16u, strings, strings_length);
        append_string_field(builder, "bankLocator", structure, structure_length, 17u, strings, strings_length);
        append_u8_field(builder, "memoryType", structure, structure_length, 18u);
        li_json_append(builder, ",\"memoryTypeName\":");
        li_json_append_escaped(builder, structure_length > 18u ? smbios_memory_type_name(structure[18u]) : "Unknown");
        append_u16_field(builder, "typeDetail", structure, structure_length, 19u);
        append_u16_field(builder, "speedMTs", structure, structure_length, 21u);
        append_string_field(builder, "manufacturer", structure, structure_length, 23u, strings, strings_length);
        append_string_field(builder, "serialNumber", structure, structure_length, 24u, strings, strings_length);
        append_string_field(builder, "assetTag", structure, structure_length, 25u, strings, strings_length);
        append_string_field(builder, "partNumber", structure, structure_length, 26u, strings, strings_length);
        append_u8_field(builder, "attributes", structure, structure_length, 27u);
        append_u16_field(builder, "configuredMemorySpeedMTs", structure, structure_length, 32u);
        append_u16_field(builder, "minimumVoltageMillivolts", structure, structure_length, 34u);
        append_u16_field(builder, "maximumVoltageMillivolts", structure, structure_length, 36u);
        append_u16_field(builder, "configuredVoltageMillivolts", structure, structure_length, 38u);
        break;
    case 32u:
        append_u8_field(builder, "bootStatus", structure, structure_length, 10u);
        break;
    default:
        break;
    }
}

int32_t li_smbios_to_json(const uint8_t* data, uintptr_t length, li_json_output* output)
{
    if ((data == NULL && length != 0u) || output == NULL) {
        return LI_STATUS_INVALID_ARGUMENT;
    }

    output->data = NULL;
    output->length = 0u;
    smbios_table_view view = smbios_get_table_view(data, length);
    li_json_builder builder;
    li_json_init(&builder);
    li_json_append(&builder, "{\"parser\":\"smbios\",\"status\":\"ok\"");
    li_json_append_format(&builder, ",\"inputLength\":%llu", (unsigned long long)length);
    li_json_append(&builder, ",\"rawHeader\":{");
    li_json_append_format(&builder, "\"present\":%s", view.has_raw_header ? "true" : "false");
    li_json_append_format(&builder, ",\"major\":%u,\"minor\":%u,\"dmiRevision\":%u", (unsigned int)view.major, (unsigned int)view.minor, (unsigned int)view.dmi_revision);
    li_json_append(&builder, "}");
    li_json_append(&builder, ",\"structures\":[");

    uintptr_t offset = 0u;
    unsigned int structure_count = 0u;
    int partial = 0;
    while (offset + 4u <= view.length) {
        const uint8_t* structure = view.data + offset;
        uint8_t type = structure[0];
        uint8_t formatted_length = structure[1];
        uint16_t handle = li_read_le16(structure + 2u);
        if (formatted_length < 4u || offset + (uintptr_t)formatted_length > view.length) {
            partial = 1;
            break;
        }

        int complete = 0;
        uintptr_t end = smbios_structure_end(view.data, view.length, offset, formatted_length, &complete);
        if (!complete) {
            partial = 1;
        }

        uintptr_t strings_start = offset + (uintptr_t)formatted_length;
        uintptr_t strings_length = end > strings_start + 1u ? end - strings_start - 2u : 0u;
        if (structure_count != 0u) {
            li_json_append_char(&builder, ',');
        }

        li_json_append(&builder, "{");
        li_json_append_format(&builder, "\"type\":%u,\"typeName\":", (unsigned int)type);
        li_json_append_escaped(&builder, smbios_type_name(type));
        li_json_append_format(&builder, ",\"handle\":%u,\"formattedLength\":%u", (unsigned int)handle, (unsigned int)formatted_length);
        li_json_append(&builder, ",\"complete\":");
        li_json_append_bool(&builder, complete);
        append_type_specific_data(&builder, structure, formatted_length, view.data + strings_start, strings_length);
        append_strings_array(&builder, view.data + strings_start, strings_length);
        li_json_append(&builder, ",\"rawHex\":");
        li_json_append_hex(&builder, structure, end - offset);
        li_json_append_char(&builder, '}');

        ++structure_count;
        offset = end;
        if (type == 127u) {
            break;
        }
    }

    if (offset < view.length && offset + 4u > view.length) {
        partial = 1;
    }

    li_json_append(&builder, "]");
    li_json_append_format(&builder, ",\"structureCount\":%u", structure_count);
    li_json_append_format(&builder, ",\"trailingBytes\":%llu", (unsigned long long)(view.length > offset ? view.length - offset : 0u));
    li_json_append(&builder, ",\"partial\":");
    li_json_append_bool(&builder, partial);
    li_json_append_char(&builder, '}');

    int32_t status = li_json_detach(&builder, output);
    if (status != LI_STATUS_OK) {
        return status;
    }

    return partial ? LI_STATUS_TRUNCATED : LI_STATUS_OK;
}
