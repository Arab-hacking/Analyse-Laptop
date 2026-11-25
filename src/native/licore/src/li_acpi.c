#include "li_internal.h"

#include <string.h>

typedef struct acpi_header_view {
    char signature[5];
    uint32_t length;
    uint8_t revision;
    uint8_t checksum;
    char oem_id[7];
    char oem_table_id[9];
    uint32_t oem_revision;
    char creator_id[5];
    uint32_t creator_revision;
} acpi_header_view;

static void read_acpi_header(const uint8_t* table, acpi_header_view* header)
{
    li_fixed_ascii(header->signature, sizeof(header->signature), table, 4u);
    header->length = li_read_le32(table + 4u);
    header->revision = table[8u];
    header->checksum = table[9u];
    li_fixed_ascii(header->oem_id, sizeof(header->oem_id), table + 10u, 6u);
    li_fixed_ascii(header->oem_table_id, sizeof(header->oem_table_id), table + 16u, 8u);
    header->oem_revision = li_read_le32(table + 24u);
    li_fixed_ascii(header->creator_id, sizeof(header->creator_id), table + 28u, 4u);
    header->creator_revision = li_read_le32(table + 32u);
}

static int signature_equals(const acpi_header_view* header, const char* signature)
{
    return strncmp(header->signature, signature, 4u) == 0;
}

static void append_acpi_header(li_json_builder* builder, const acpi_header_view* header, const uint8_t* table)
{
    li_json_append(builder, "\"signature\":");
    li_json_append_escaped(builder, header->signature);
    li_json_append_format(builder, ",\"length\":%lu,\"revision\":%u,\"checksum\":%u", (unsigned long)header->length, (unsigned int)header->revision, (unsigned int)header->checksum);
    li_json_append(builder, ",\"checksumValid\":");
    li_json_append_bool(builder, li_checksum8_is_valid(table, header->length));
    li_json_append(builder, ",\"oemId\":");
    li_json_append_escaped(builder, header->oem_id);
    li_json_append(builder, ",\"oemTableId\":");
    li_json_append_escaped(builder, header->oem_table_id);
    li_json_append_format(builder, ",\"oemRevision\":%lu", (unsigned long)header->oem_revision);
    li_json_append(builder, ",\"creatorId\":");
    li_json_append_escaped(builder, header->creator_id);
    li_json_append_format(builder, ",\"creatorRevision\":%lu", (unsigned long)header->creator_revision);
}

static const char* madt_entry_name(uint8_t type)
{
    switch (type) {
    case 0u: return "Processor Local APIC";
    case 1u: return "I/O APIC";
    case 2u: return "Interrupt Source Override";
    case 3u: return "NMI Source";
    case 4u: return "Local APIC NMI";
    case 5u: return "Local APIC Address Override";
    case 6u: return "I/O SAPIC";
    case 7u: return "Local SAPIC";
    case 8u: return "Platform Interrupt Sources";
    case 9u: return "Processor Local x2APIC";
    case 10u: return "Local x2APIC NMI";
    case 11u: return "GIC CPU Interface";
    case 12u: return "GIC Distributor";
    case 13u: return "GIC MSI Frame";
    case 14u: return "GIC Redistributor";
    case 15u: return "GIC ITS";
    case 16u: return "Multiprocessor Wakeup";
    default: return "Unknown MADT Entry";
    }
}

static void append_fadt_details(li_json_builder* builder, const uint8_t* table, uint32_t length)
{
    li_json_append(builder, ",\"fadt\":{");
    if (length >= 44u) {
        li_json_append_format(builder, "\"firmwareControl32\":%lu,\"dsdt32\":%lu", (unsigned long)li_read_le32(table + 36u), (unsigned long)li_read_le32(table + 40u));
    }
    else {
        li_json_append(builder, "\"firmwareControl32\":null,\"dsdt32\":null");
    }

    if (length >= 48u) {
        li_json_append_format(builder, ",\"preferredPowerManagementProfile\":%u,\"sciInterrupt\":%u", (unsigned int)table[45u], (unsigned int)li_read_le16(table + 46u));
    }

    if (length >= 116u) {
        li_json_append_format(builder, ",\"flags\":%lu", (unsigned long)li_read_le32(table + 112u));
    }

    if (length >= 148u) {
        li_json_append_format(builder, ",\"xFirmwareControl\":%llu,\"xDsdt\":%llu", (unsigned long long)li_read_le64(table + 132u), (unsigned long long)li_read_le64(table + 140u));
    }

    li_json_append_char(builder, '}');
}

static void append_madt_details(li_json_builder* builder, const uint8_t* table, uint32_t length)
{
    li_json_append(builder, ",\"madt\":{");
    if (length < 44u) {
        li_json_append(builder, "\"truncated\":true}");
        return;
    }

    li_json_append_format(builder, "\"localApicAddress\":%lu,\"flags\":%lu", (unsigned long)li_read_le32(table + 36u), (unsigned long)li_read_le32(table + 40u));
    li_json_append(builder, ",\"entries\":[");
    uintptr_t offset = 44u;
    int first = 1;
    while (offset + 2u <= (uintptr_t)length) {
        uint8_t type = table[offset];
        uint8_t entry_length = table[offset + 1u];
        if (entry_length < 2u || offset + entry_length > (uintptr_t)length) {
            break;
        }

        if (!first) {
            li_json_append_char(builder, ',');
        }

        first = 0;
        li_json_append_format(builder, "{\"type\":%u,\"name\":", (unsigned int)type);
        li_json_append_escaped(builder, madt_entry_name(type));
        li_json_append_format(builder, ",\"length\":%u,\"rawHex\":", (unsigned int)entry_length);
        li_json_append_hex(builder, table + offset, entry_length);
        li_json_append_char(builder, '}');
        offset += entry_length;
    }

    li_json_append(builder, "]}");
}

static void append_tpm2_details(li_json_builder* builder, const uint8_t* table, uint32_t length)
{
    li_json_append(builder, ",\"tpm2\":{");
    if (length >= 52u) {
        li_json_append_format(builder, "\"platformClass\":%u", (unsigned int)li_read_le16(table + 36u));
        li_json_append_format(builder, ",\"controlArea\":%llu", (unsigned long long)li_read_le64(table + 40u));
        li_json_append_format(builder, ",\"startMethod\":%lu", (unsigned long)li_read_le32(table + 48u));
    }
    else {
        li_json_append(builder, "\"truncated\":true");
    }

    li_json_append_char(builder, '}');
}

static void append_bgrt_details(li_json_builder* builder, const uint8_t* table, uint32_t length)
{
    li_json_append(builder, ",\"bgrt\":{");
    if (length >= 56u) {
        li_json_append_format(builder, "\"version\":%u,\"status\":%u,\"imageType\":%u", (unsigned int)li_read_le16(table + 36u), (unsigned int)table[38u], (unsigned int)table[39u]);
        li_json_append_format(builder, ",\"imageAddress\":%llu", (unsigned long long)li_read_le64(table + 40u));
        li_json_append_format(builder, ",\"imageOffsetX\":%lu,\"imageOffsetY\":%lu", (unsigned long)li_read_le32(table + 48u), (unsigned long)li_read_le32(table + 52u));
    }
    else {
        li_json_append(builder, "\"truncated\":true");
    }

    li_json_append_char(builder, '}');
}

static void append_fpdt_details(li_json_builder* builder, const uint8_t* table, uint32_t length)
{
    li_json_append(builder, ",\"fpdt\":{\"records\":[");
    uintptr_t offset = 36u;
    int first = 1;
    while (offset + 4u <= (uintptr_t)length) {
        uint16_t type = li_read_le16(table + offset);
        uint8_t record_length = table[offset + 2u];
        uint8_t revision = table[offset + 3u];
        if (record_length < 4u || offset + record_length > (uintptr_t)length) {
            break;
        }

        if (!first) {
            li_json_append_char(builder, ',');
        }

        first = 0;
        li_json_append_format(builder, "{\"type\":%u,\"length\":%u,\"revision\":%u,\"rawHex\":", (unsigned int)type, (unsigned int)record_length, (unsigned int)revision);
        li_json_append_hex(builder, table + offset, record_length);
        li_json_append_char(builder, '}');
        offset += record_length;
    }

    li_json_append(builder, "]}");
}

static void append_bert_details(li_json_builder* builder, const uint8_t* table, uint32_t length)
{
    li_json_append(builder, ",\"bert\":{");
    if (length >= 48u) {
        li_json_append_format(builder, "\"bootErrorRegionLength\":%lu,\"bootErrorRegionAddress\":%llu", (unsigned long)li_read_le32(table + 36u), (unsigned long long)li_read_le64(table + 40u));
    }
    else {
        li_json_append(builder, "\"truncated\":true");
    }

    li_json_append_char(builder, '}');
}

static void append_wsmt_details(li_json_builder* builder, const uint8_t* table, uint32_t length)
{
    li_json_append(builder, ",\"wsmt\":{");
    if (length >= 40u) {
        uint32_t flags = li_read_le32(table + 36u);
        li_json_append_format(builder, "\"protectionFlags\":%lu", (unsigned long)flags);
        li_json_append_format(builder, ",\"fixedCommunicationBuffers\":%s", (flags & 0x01u) != 0u ? "true" : "false");
        li_json_append_format(builder, ",\"commBufferNestedPointerProtection\":%s", (flags & 0x02u) != 0u ? "true" : "false");
        li_json_append_format(builder, ",\"systemResourceProtection\":%s", (flags & 0x04u) != 0u ? "true" : "false");
    }
    else {
        li_json_append(builder, "\"truncated\":true");
    }

    li_json_append_char(builder, '}');
}

static void append_msdm_details(li_json_builder* builder, const uint8_t* table, uint32_t length)
{
    li_json_append(builder, ",\"msdm\":{");
    if (length >= 56u) {
        uint32_t data_type = li_read_le32(table + 36u);
        uint32_t data_reserved = li_read_le32(table + 40u);
        uint32_t data_length = li_read_le32(table + 44u);
        li_json_append_format(builder, "\"dataType\":%lu,\"dataReserved\":%lu,\"dataLength\":%lu", (unsigned long)data_type, (unsigned long)data_reserved, (unsigned long)data_length);
        if (data_length > 0u && 48u + data_length <= length) {
            char key_tail[6];
            uintptr_t tail_offset = 48u + data_length >= 5u ? 48u + data_length - 5u : 48u;
            li_fixed_ascii(key_tail, sizeof(key_tail), table + tail_offset, 5u);
            li_json_append(builder, ",\"productKeyLast5\":");
            li_json_append_escaped(builder, key_tail);
        }
    }
    else {
        li_json_append(builder, "\"truncated\":true");
    }

    li_json_append_char(builder, '}');
}

static void append_signature_details(li_json_builder* builder, const acpi_header_view* header, const uint8_t* table)
{
    if (signature_equals(header, "FACP")) {
        append_fadt_details(builder, table, header->length);
    }
    else if (signature_equals(header, "APIC")) {
        append_madt_details(builder, table, header->length);
    }
    else if (signature_equals(header, "TPM2")) {
        append_tpm2_details(builder, table, header->length);
    }
    else if (signature_equals(header, "BGRT")) {
        append_bgrt_details(builder, table, header->length);
    }
    else if (signature_equals(header, "FPDT")) {
        append_fpdt_details(builder, table, header->length);
    }
    else if (signature_equals(header, "BERT")) {
        append_bert_details(builder, table, header->length);
    }
    else if (signature_equals(header, "WSMT")) {
        append_wsmt_details(builder, table, header->length);
    }
    else if (signature_equals(header, "MSDM")) {
        append_msdm_details(builder, table, header->length);
    }
}

int32_t li_acpi_tables_to_json(const uint8_t* data, uintptr_t length, li_json_output* output)
{
    if ((data == NULL && length != 0u) || output == NULL) {
        return LI_STATUS_INVALID_ARGUMENT;
    }

    output->data = NULL;
    output->length = 0u;
    li_json_builder builder;
    li_json_init(&builder);
    li_json_append_format(&builder, "{\"parser\":\"acpi\",\"status\":\"ok\",\"inputLength\":%llu,\"tables\":[", (unsigned long long)length);

    uintptr_t offset = 0u;
    unsigned int table_count = 0u;
    int partial = 0;
    while (offset + 36u <= length) {
        acpi_header_view header;
        const uint8_t* table = data + offset;
        read_acpi_header(table, &header);
        if (header.length < 36u || offset + (uintptr_t)header.length > length) {
            partial = 1;
            break;
        }

        if (table_count != 0u) {
            li_json_append_char(&builder, ',');
        }

        li_json_append_char(&builder, '{');
        append_acpi_header(&builder, &header, table);
        append_signature_details(&builder, &header, table);
        li_json_append(&builder, ",\"rawHeaderHex\":");
        li_json_append_hex(&builder, table, 36u);
        li_json_append_char(&builder, '}');
        ++table_count;
        offset += header.length;
    }

    if (offset != length) {
        partial = 1;
    }

    li_json_append(&builder, "]");
    li_json_append_format(&builder, ",\"tableCount\":%u,\"trailingBytes\":%llu", table_count, (unsigned long long)(length > offset ? length - offset : 0u));
    li_json_append(&builder, ",\"partial\":");
    li_json_append_bool(&builder, partial);
    li_json_append_char(&builder, '}');

    int32_t status = li_json_detach(&builder, output);
    if (status != LI_STATUS_OK) {
        return status;
    }

    return partial ? LI_STATUS_TRUNCATED : LI_STATUS_OK;
}
