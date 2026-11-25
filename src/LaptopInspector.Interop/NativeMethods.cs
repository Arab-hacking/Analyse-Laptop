using System.Runtime.InteropServices;

namespace LaptopInspector.Interop;

internal static partial class NativeMethods
{
    private const string LibraryName = "LaptopInspector.Native";

    [LibraryImport(LibraryName, EntryPoint = "li_get_abi_version")]
    internal static partial uint GetAbiVersion();

    [LibraryImport(LibraryName, EntryPoint = "li_get_last_error")]
    internal static partial IntPtr GetLastErrorPointer();

    [LibraryImport(LibraryName, EntryPoint = "li_free")]
    internal static partial void Free(IntPtr pointer);

    [LibraryImport(LibraryName, EntryPoint = "li_collect_runtime_json")]
    internal static partial int CollectRuntimeJson(out IntPtr jsonPointer);

    [LibraryImport(LibraryName, EntryPoint = "li_collect_device_tree_json")]
    internal static partial int CollectDeviceTreeJson(out IntPtr jsonPointer);

    [LibraryImport(LibraryName, EntryPoint = "li_collect_storage_json")]
    internal static partial int CollectStorageJson(out IntPtr jsonPointer);

    [LibraryImport(LibraryName, EntryPoint = "li_collect_battery_json")]
    internal static partial int CollectBatteryJson(out IntPtr jsonPointer);

    [LibraryImport(LibraryName, EntryPoint = "li_collect_usb_json")]
    internal static partial int CollectUsbJson(out IntPtr jsonPointer);

    [LibraryImport(LibraryName, EntryPoint = "li_collect_display_json")]
    internal static partial int CollectDisplayJson(out IntPtr jsonPointer);

    [LibraryImport(LibraryName, EntryPoint = "li_collect_wlan_json")]
    internal static partial int CollectWlanJson(out IntPtr jsonPointer);

    [LibraryImport(LibraryName, EntryPoint = "li_collect_firmware_json")]
    internal static partial int CollectFirmwareJson(out IntPtr jsonPointer);

    [LibraryImport(LibraryName, EntryPoint = "li_collect_tpm_json")]
    internal static partial int CollectTpmJson(out IntPtr jsonPointer);

    [LibraryImport(LibraryName, EntryPoint = "li_collect_sensor_json")]
    internal static partial int CollectSensorJson(out IntPtr jsonPointer);

    [LibraryImport(LibraryName, EntryPoint = "li_collect_gpu_vendor_json")]
    internal static partial int CollectGpuVendorJson(out IntPtr jsonPointer);
}
