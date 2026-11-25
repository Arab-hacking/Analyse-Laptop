#include "li_native.h"

#include "licore/li_core.h"
#include "native_common.h"

#include <exception>
#include <string>
#include <vector>

#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#endif

namespace {
#ifdef _WIN32
constexpr DWORD fourcc(char a, char b, char c, char d)
{
    return (static_cast<DWORD>(a) << 24u) |
        (static_cast<DWORD>(b) << 16u) |
        (static_cast<DWORD>(c) << 8u) |
        static_cast<DWORD>(d);
}

std::string signature_from_dword(DWORD value)
{
    std::string direct(4, '\0');
    direct[0] = static_cast<char>((value >> 24u) & 0xFFu);
    direct[1] = static_cast<char>((value >> 16u) & 0xFFu);
    direct[2] = static_cast<char>((value >> 8u) & 0xFFu);
    direct[3] = static_cast<char>(value & 0xFFu);
    for (char& character : direct) {
        if (character < 0x20 || character > 0x7E) {
            character = '?';
        }
    }

    return direct;
}

void append_smbios(li_native::JsonWriter& writer)
{
    writer.raw("\"smbios\":");
    DWORD provider = fourcc('R', 'S', 'M', 'B');
    UINT size = GetSystemFirmwareTable(provider, 0u, nullptr, 0u);
    if (size == 0u) {
        writer.begin_object();
        writer.key("status");
        writer.string("unavailable");
        writer.raw(",\"error\":");
        li_native::append_win32_error(writer, GetLastError());
        writer.end_object();
        return;
    }

    std::vector<uint8_t> buffer(size);
    UINT read = GetSystemFirmwareTable(provider, 0u, buffer.data(), size);
    if (read == 0u) {
        writer.begin_object();
        writer.key("status");
        writer.string("readFailed");
        writer.raw(",\"error\":");
        li_native::append_win32_error(writer, GetLastError());
        writer.end_object();
        return;
    }

    li_json_output parsed{};
    int32_t status = li_smbios_to_json(buffer.data(), read, &parsed);
    if (parsed.data != nullptr) {
        writer.raw(parsed.data);
        li_json_output_free(&parsed);
    }
    else {
        writer.begin_object();
        writer.key("status");
        writer.string("parseFailed");
        writer.raw(",\"parseStatus\":");
        writer.signed_number(status);
        writer.raw(",\"rawHex\":");
        writer.hex_bytes(buffer.data(), read);
        writer.end_object();
    }
}

void append_secure_boot(li_native::JsonWriter& writer)
{
    writer.raw(",\"secureBoot\":");
    BYTE value = 0u;
    DWORD attributes = 0u;
    DWORD read = GetFirmwareEnvironmentVariableExW(
        L"SecureBoot",
        L"{8BE4DF61-93CA-11D2-AA0D-00E098032B8C}",
        &value,
        sizeof(value),
        &attributes);
    if (read == 0u) {
        writer.begin_object();
        writer.key("available");
        writer.boolean(false);
        writer.raw(",\"error\":");
        li_native::append_win32_error(writer, GetLastError());
        writer.end_object();
        return;
    }

    writer.begin_object();
    writer.key("available");
    writer.boolean(true);
    writer.raw(",\"enabled\":");
    writer.boolean(value != 0u);
    writer.raw(",\"rawValue\":");
    writer.unsigned_number(value);
    writer.raw(",\"attributes\":");
    writer.unsigned_number(attributes);
    writer.end_object();
}

void append_acpi(li_native::JsonWriter& writer)
{
    writer.raw(",\"acpi\":");
    DWORD provider = fourcc('A', 'C', 'P', 'I');
    UINT size = GetSystemFirmwareTable(provider, 0u, nullptr, 0u);
    if (size == 0u) {
        writer.begin_object();
        writer.key("status");
        writer.string("unavailable");
        writer.raw(",\"error\":");
        li_native::append_win32_error(writer, GetLastError());
        writer.end_object();
        return;
    }

    std::vector<DWORD> ids(size / sizeof(DWORD));
    UINT read = GetSystemFirmwareTable(provider, 0u, ids.data(), size);
    if (read == 0u) {
        writer.begin_object();
        writer.key("status");
        writer.string("enumerationFailed");
        writer.raw(",\"error\":");
        li_native::append_win32_error(writer, GetLastError());
        writer.end_object();
        return;
    }

    writer.begin_object();
    writer.key("status");
    writer.string("ok");
    writer.raw(",\"tableIds\":[");
    bool first = true;
    for (DWORD id : ids) {
        writer.comma(first);
        writer.begin_object();
        writer.key("idHex");
        writer.string(li_native::bytes_to_hex(&id, sizeof(id)));
        writer.raw(",\"signatureGuess\":");
        writer.string(signature_from_dword(id));
        writer.raw(",\"parsed\":");
        UINT table_size = GetSystemFirmwareTable(provider, id, nullptr, 0u);
        if (table_size == 0u) {
            writer.null_value();
        }
        else {
            std::vector<uint8_t> table(table_size);
            UINT table_read = GetSystemFirmwareTable(provider, id, table.data(), table_size);
            if (table_read == 0u) {
                writer.null_value();
            }
            else {
                li_json_output parsed{};
                int32_t status = li_acpi_tables_to_json(table.data(), table_read, &parsed);
                if (parsed.data != nullptr) {
                    writer.raw(parsed.data);
                    li_json_output_free(&parsed);
                }
                else {
                    writer.begin_object();
                    writer.key("status");
                    writer.string("parseFailed");
                    writer.raw(",\"parseStatus\":");
                    writer.signed_number(status);
                    writer.end_object();
                }
            }
        }

        writer.end_object();
    }

    writer.raw("],\"tableCount\":");
    writer.unsigned_number(ids.size());
    writer.end_object();
}
#endif
}  // namespace

extern "C" LI_EXPORT int32_t li_collect_firmware_json(char** output) noexcept
{
    if (output == nullptr) {
        li_native::set_last_error("Аргумент output равен null.");
        return -1;
    }

    *output = nullptr;
#ifndef _WIN32
    return li_native::return_json_string(output, li_native::unsupported_json("firmware", "GetSystemFirmwareTable доступен только на Windows."));
#else
    try {
        li_native::JsonWriter writer;
        writer.raw("{\"collector\":\"firmware\",\"status\":\"ok\",\"source\":\"GetSystemFirmwareTable+UEFI variables\",");
        append_smbios(writer);
        append_acpi(writer);
        append_secure_boot(writer);
        writer.raw("}");
        return li_native::return_json_string(output, writer.str());
    }
    catch (const std::exception& exception) {
        return li_native::return_error_json(output, "firmware", exception.what());
    }
    catch (...) {
        return li_native::return_error_json(output, "firmware", "Необработанное исключение при сборе прошивки.");
    }
#endif
}
