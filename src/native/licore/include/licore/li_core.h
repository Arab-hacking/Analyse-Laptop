#pragma once

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

#define LI_CORE_ABI_VERSION 2u

typedef enum li_status {
    LI_STATUS_OK = 0,
    LI_STATUS_INVALID_ARGUMENT = -1,
    LI_STATUS_OUT_OF_MEMORY = -2,
    LI_STATUS_TRUNCATED = -3,
    LI_STATUS_UNSUPPORTED = -4,
    LI_STATUS_PARSE_ERROR = -5
} li_status;

typedef struct li_json_output {
    char* data;
    uintptr_t length;
} li_json_output;

uint32_t li_core_get_abi_version(void);
uint32_t li_core_crc32(const uint8_t* data, uintptr_t length);
int32_t li_core_sha256_hex(const uint8_t* data, uintptr_t length, char output_hex[65]);
void li_core_free(void* pointer);
void li_json_output_free(li_json_output* output);

int32_t li_smbios_to_json(const uint8_t* data, uintptr_t length, li_json_output* output);
int32_t li_edid_to_json(const uint8_t* data, uintptr_t length, li_json_output* output);
int32_t li_acpi_tables_to_json(const uint8_t* data, uintptr_t length, li_json_output* output);
int32_t li_cpuid_to_json(li_json_output* output);
int32_t li_smart_ata_to_json(const uint8_t* data, uintptr_t length, li_json_output* output);
int32_t li_nvme_health_to_json(const uint8_t* data, uintptr_t length, li_json_output* output);

#ifdef __cplusplus
}
#endif
