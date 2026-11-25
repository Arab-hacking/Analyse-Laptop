#include "licore/li_core.h"

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifndef LI_FIXTURE_DIR
#define LI_FIXTURE_DIR "../fixtures"
#endif

typedef int (*parse_function)(const uint8_t*, uintptr_t, li_json_output*);

static int failures = 0;

static void fail(const char* message)
{
    ++failures;
    fprintf(stderr, "FAIL: %s\n", message);
}

static uint8_t* read_fixture(const char* name, uintptr_t* length)
{
    char path[1024];
    snprintf(path, sizeof(path), "%s/%s", LI_FIXTURE_DIR, name);
    FILE* file = fopen(path, "rb");
    if (file == NULL) {
        fprintf(stderr, "Не удалось открыть фикстуру: %s\n", path);
        return NULL;
    }

    if (fseek(file, 0, SEEK_END) != 0) {
        fclose(file);
        return NULL;
    }

    long size = ftell(file);
    if (size < 0) {
        fclose(file);
        return NULL;
    }

    if (fseek(file, 0, SEEK_SET) != 0) {
        fclose(file);
        return NULL;
    }

    uint8_t* buffer = (uint8_t*)malloc((size_t)size == 0u ? 1u : (size_t)size);
    if (buffer == NULL) {
        fclose(file);
        return NULL;
    }

    size_t read_count = fread(buffer, 1u, (size_t)size, file);
    fclose(file);
    if (read_count != (size_t)size) {
        free(buffer);
        return NULL;
    }

    *length = (uintptr_t)size;
    return buffer;
}

static void expect_contains(const char* json, const char* needle, const char* label)
{
    if (json == NULL || strstr(json, needle) == NULL) {
        fprintf(stderr, "JSON does not contain '%s' for %s\n", needle, label);
        fail(label);
    }
}

static void test_fixture_parser(const char* fixture, parse_function parser, const char* expected, const char* label)
{
    uintptr_t length = 0u;
    uint8_t* data = read_fixture(fixture, &length);
    if (data == NULL) {
        fail(fixture);
        return;
    }

    li_json_output output;
    output.data = NULL;
    output.length = 0u;
    int32_t status = parser(data, length, &output);
    if (status != LI_STATUS_OK) {
        fprintf(stderr, "%s returned status %d\n", label, (int)status);
        fail(label);
    }

    if (output.data == NULL || output.length == 0u) {
        fail(label);
    }
    else {
        expect_contains(output.data, expected, label);
    }

    li_json_output_free(&output);
    free(data);
}

static void test_sha256(void)
{
    const uint8_t data[] = { 'a', 'b', 'c' };
    char hex[65];
    int32_t status = li_core_sha256_hex(data, sizeof(data), hex);
    if (status != LI_STATUS_OK) {
        fail("sha256 status");
        return;
    }

    if (strcmp(hex, "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad") != 0) {
        fail("sha256 value");
    }
}

static void test_crc32(void)
{
    const uint8_t data[] = { '1','2','3','4','5','6','7','8','9' };
    uint32_t crc = li_core_crc32(data, sizeof(data));
    if (crc != 0xCBF43926u) {
        fail("crc32 value");
    }
}

static void test_cpuid(void)
{
    li_json_output output;
    output.data = NULL;
    output.length = 0u;
    int32_t status = li_cpuid_to_json(&output);
    if (status != LI_STATUS_OK && status != LI_STATUS_UNSUPPORTED) {
        fail("cpuid status");
    }

    if (output.data == NULL || output.length == 0u) {
        fail("cpuid output");
    }
    else {
        expect_contains(output.data, "\"parser\":\"cpuid\"", "cpuid parser");
    }

    li_json_output_free(&output);
}

int main(void)
{
    test_sha256();
    test_crc32();
    test_fixture_parser("smbios_minimal.bin", li_smbios_to_json, "FixtureLaptop", "smbios");
    test_fixture_parser("edid_with_cta.bin", li_edid_to_json, "CTA-861", "edid");
    test_fixture_parser("acpi_tables.bin", li_acpi_tables_to_json, "BGRT", "acpi");
    test_fixture_parser("ata_smart.bin", li_smart_ata_to_json, "Power-On Hours", "ata smart");
    test_fixture_parser("nvme_health.bin", li_nvme_health_to_json, "percentageUsed", "nvme health");
    test_cpuid();

    if (failures != 0) {
        fprintf(stderr, "Native tests failed: %d\n", failures);
        return 1;
    }

    printf("Native tests passed.\n");
    return 0;
}
