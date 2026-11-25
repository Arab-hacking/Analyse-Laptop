#include "li_native.h"
#include "native_common.h"

#include <exception>
#include <iterator>
#include <string>

#ifdef _WIN32
#define NOMINMAX
#include <windows.h>
#include <wlanapi.h>
#endif

namespace {
#ifdef _WIN32
struct WlanMemoryGuard final {
    void* value;
    ~WlanMemoryGuard() { if (value != nullptr) { WlanFreeMemory(value); } }
};

std::string dot11_ssid_to_string(const DOT11_SSID& ssid)
{
    return std::string(reinterpret_cast<const char*>(ssid.ucSSID), reinterpret_cast<const char*>(ssid.ucSSID + ssid.uSSIDLength));
}

std::string interface_state_name(WLAN_INTERFACE_STATE state)
{
    switch (state) {
    case wlan_interface_state_not_ready: return "notReady";
    case wlan_interface_state_connected: return "connected";
    case wlan_interface_state_ad_hoc_network_formed: return "adHocNetworkFormed";
    case wlan_interface_state_disconnecting: return "disconnecting";
    case wlan_interface_state_disconnected: return "disconnected";
    case wlan_interface_state_associating: return "associating";
    case wlan_interface_state_discovering: return "discovering";
    case wlan_interface_state_authenticating: return "authenticating";
    default: return "unknown";
    }
}

std::string auth_algorithm_name(DOT11_AUTH_ALGORITHM value)
{
    switch (value) {
    case DOT11_AUTH_ALGO_80211_OPEN: return "802.11 Open";
    case DOT11_AUTH_ALGO_80211_SHARED_KEY: return "802.11 Shared Key";
    case DOT11_AUTH_ALGO_WPA: return "WPA";
    case DOT11_AUTH_ALGO_WPA_PSK: return "WPA-PSK";
    case DOT11_AUTH_ALGO_WPA_NONE: return "WPA-None";
    case DOT11_AUTH_ALGO_RSNA: return "WPA2/RSNA";
    case DOT11_AUTH_ALGO_RSNA_PSK: return "WPA2/RSNA-PSK";
    default: return "Unknown";
    }
}

std::string cipher_algorithm_name(DOT11_CIPHER_ALGORITHM value)
{
    switch (value) {
    case DOT11_CIPHER_ALGO_NONE: return "None";
    case DOT11_CIPHER_ALGO_WEP40: return "WEP40";
    case DOT11_CIPHER_ALGO_TKIP: return "TKIP";
    case DOT11_CIPHER_ALGO_CCMP: return "CCMP";
    case DOT11_CIPHER_ALGO_WEP104: return "WEP104";
    case DOT11_CIPHER_ALGO_BIP: return "BIP";
    default: return "Unknown";
    }
}

void append_current_connection(li_native::JsonWriter& writer, HANDLE client, const GUID& interface_guid)
{
    writer.raw(",\"currentConnection\":");
    DWORD data_size = 0u;
    WLAN_OPCODE_VALUE_TYPE opcode_type{};
    void* data = nullptr;
    DWORD result = WlanQueryInterface(client, &interface_guid, wlan_intf_opcode_current_connection, nullptr, &data_size, &data, &opcode_type);
    if (result != ERROR_SUCCESS || data == nullptr) {
        writer.null_value();
        return;
    }

    WlanMemoryGuard guard{ data };
    auto* attributes = static_cast<WLAN_CONNECTION_ATTRIBUTES*>(data);
    writer.begin_object();
    writer.key("isState");
    writer.string(interface_state_name(attributes->isState));
    writer.raw(",\"profileName\":");
    writer.string(li_native::wide_to_utf8(attributes->strProfileName));
    writer.raw(",\"ssid\":");
    writer.string(dot11_ssid_to_string(attributes->wlanAssociationAttributes.dot11Ssid));
    writer.raw(",\"bssid\":");
    writer.hex_bytes(attributes->wlanAssociationAttributes.dot11Bssid, sizeof(attributes->wlanAssociationAttributes.dot11Bssid));
    writer.raw(",\"phyType\":");
    writer.unsigned_number(attributes->wlanAssociationAttributes.dot11PhyType);
    writer.raw(",\"signalQuality\":");
    writer.unsigned_number(attributes->wlanAssociationAttributes.wlanSignalQuality);
    writer.raw(",\"rxRateKbps\":");
    writer.unsigned_number(attributes->wlanAssociationAttributes.ulRxRate);
    writer.raw(",\"txRateKbps\":");
    writer.unsigned_number(attributes->wlanAssociationAttributes.ulTxRate);
    writer.raw(",\"authAlgorithm\":");
    writer.string(auth_algorithm_name(attributes->wlanSecurityAttributes.dot11AuthAlgorithm));
    writer.raw(",\"cipherAlgorithm\":");
    writer.string(cipher_algorithm_name(attributes->wlanSecurityAttributes.dot11CipherAlgorithm));
    writer.raw(",\"securityEnabled\":");
    writer.boolean(attributes->wlanSecurityAttributes.bSecurityEnabled != FALSE);
    writer.raw(",\"oneXEnabled\":");
    writer.boolean(attributes->wlanSecurityAttributes.bOneXEnabled != FALSE);
    writer.end_object();
}

void append_available_networks(li_native::JsonWriter& writer, HANDLE client, const GUID& interface_guid)
{
    writer.raw(",\"availableNetworks\":[");
    WLAN_AVAILABLE_NETWORK_LIST* list = nullptr;
    DWORD result = WlanGetAvailableNetworkList(client, &interface_guid, 0u, nullptr, &list);
    if (result != ERROR_SUCCESS || list == nullptr) {
        writer.raw("]");
        return;
    }

    WlanMemoryGuard guard{ list };
    bool first = true;
    for (DWORD index = 0u; index < list->dwNumberOfItems; ++index) {
        const WLAN_AVAILABLE_NETWORK& network = list->Network[index];
        writer.comma(first);
        writer.begin_object();
        writer.key("profileName");
        writer.string(li_native::wide_to_utf8(network.strProfileName));
        writer.raw(",\"ssid\":");
        writer.string(dot11_ssid_to_string(network.dot11Ssid));
        writer.raw(",\"bssType\":");
        writer.unsigned_number(network.dot11BssType);
        writer.raw(",\"numberOfBssids\":");
        writer.unsigned_number(network.uNumberOfBssids);
        writer.raw(",\"connectable\":");
        writer.boolean(network.bNetworkConnectable != FALSE);
        writer.raw(",\"signalQuality\":");
        writer.unsigned_number(network.wlanSignalQuality);
        writer.raw(",\"securityEnabled\":");
        writer.boolean(network.bSecurityEnabled != FALSE);
        writer.raw(",\"authAlgorithm\":");
        writer.string(auth_algorithm_name(network.dot11DefaultAuthAlgorithm));
        writer.raw(",\"cipherAlgorithm\":");
        writer.string(cipher_algorithm_name(network.dot11DefaultCipherAlgorithm));
        writer.end_object();
    }

    writer.end_array();
}
#endif
}  // namespace

extern "C" LI_EXPORT int32_t li_collect_wlan_json(char** output) noexcept
{
    if (output == nullptr) {
        li_native::set_last_error("Аргумент output равен null.");
        return -1;
    }

    *output = nullptr;
#ifndef _WIN32
    return li_native::return_json_string(output, li_native::unsupported_json("wlan", "WLAN API доступен только на Windows."));
#else
    try {
        HANDLE client = nullptr;
        DWORD negotiated_version = 0u;
        DWORD result = WlanOpenHandle(2u, nullptr, &negotiated_version, &client);
        if (result != ERROR_SUCCESS) {
            return li_native::return_error_json(output, "wlan", li_native::win32_error_message(result));
        }

        struct WlanHandleGuard final {
            HANDLE value;
            ~WlanHandleGuard() { WlanCloseHandle(value, nullptr); }
        } client_guard{ client };

        WLAN_INTERFACE_INFO_LIST* interfaces = nullptr;
        result = WlanEnumInterfaces(client, nullptr, &interfaces);
        if (result != ERROR_SUCCESS || interfaces == nullptr) {
            return li_native::return_error_json(output, "wlan", li_native::win32_error_message(result));
        }

        WlanMemoryGuard interfaces_guard{ interfaces };
        li_native::JsonWriter writer;
        writer.raw("{\"collector\":\"wlan\",\"status\":\"ok\",\"source\":\"WlanAPI\",\"negotiatedVersion\":");
        writer.unsigned_number(negotiated_version);
        writer.raw(",\"interfaces\":[");
        bool first = true;
        for (DWORD index = 0u; index < interfaces->dwNumberOfItems; ++index) {
            const WLAN_INTERFACE_INFO& item = interfaces->InterfaceInfo[index];
            writer.comma(first);
            writer.begin_object();
            writer.key("description");
            writer.string(li_native::wide_to_utf8(item.strInterfaceDescription));
            writer.raw(",\"state\":");
            writer.string(interface_state_name(item.isState));
            writer.raw(",\"guid\":");
            wchar_t guid_buffer[64]{};
            StringFromGUID2(item.InterfaceGuid, guid_buffer, static_cast<int>(std::size(guid_buffer)));
            writer.string(li_native::wide_to_utf8(guid_buffer));
            append_current_connection(writer, client, item.InterfaceGuid);
            append_available_networks(writer, client, item.InterfaceGuid);
            writer.end_object();
        }

        writer.raw("]}");
        return li_native::return_json_string(output, writer.str());
    }
    catch (const std::exception& exception) {
        return li_native::return_error_json(output, "wlan", exception.what());
    }
    catch (...) {
        return li_native::return_error_json(output, "wlan", "Необработанное исключение при сборе Wi-Fi.");
    }
#endif
}
