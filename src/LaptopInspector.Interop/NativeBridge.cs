using System.Runtime.InteropServices;

namespace LaptopInspector.Interop;

public sealed record NativeProbeResult(
    bool Available,
    uint AbiVersion,
    string? RuntimeJson,
    string? Error);

public enum NativeCollectionKind
{
    Runtime,
    DeviceTree,
    Storage,
    Battery,
    Usb,
    Display,
    Wlan,
    Firmware,
    Tpm,
    Sensors,
    GpuVendor
}

public static class NativeBridge
{
    private delegate int NativeJsonCollector(out IntPtr jsonPointer);

    public static NativeProbeResult Probe()
    {
        try
        {
            uint abiVersion = NativeMethods.GetAbiVersion();
            string? runtimeJson = TryCollectRuntimeJson(out string? collectError);
            return new NativeProbeResult(
                true,
                abiVersion,
                runtimeJson,
                collectError);
        }
        catch (DllNotFoundException exception)
        {
            return Unavailable(exception.Message);
        }
        catch (EntryPointNotFoundException exception)
        {
            return Unavailable(exception.Message);
        }
        catch (BadImageFormatException exception)
        {
            return Unavailable(exception.Message);
        }
    }

    public static string? TryCollect(NativeCollectionKind kind, out string? error)
    {
        NativeJsonCollector collector = kind switch
        {
            NativeCollectionKind.Runtime => NativeMethods.CollectRuntimeJson,
            NativeCollectionKind.DeviceTree => NativeMethods.CollectDeviceTreeJson,
            NativeCollectionKind.Storage => NativeMethods.CollectStorageJson,
            NativeCollectionKind.Battery => NativeMethods.CollectBatteryJson,
            NativeCollectionKind.Usb => NativeMethods.CollectUsbJson,
            NativeCollectionKind.Display => NativeMethods.CollectDisplayJson,
            NativeCollectionKind.Wlan => NativeMethods.CollectWlanJson,
            NativeCollectionKind.Firmware => NativeMethods.CollectFirmwareJson,
            NativeCollectionKind.Tpm => NativeMethods.CollectTpmJson,
            NativeCollectionKind.Sensors => NativeMethods.CollectSensorJson,
            NativeCollectionKind.GpuVendor => NativeMethods.CollectGpuVendorJson,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Неизвестный нативный сборщик.")
        };

        return TryCollectJson(collector, out error);
    }

    public static string GetLastError()
    {
        try
        {
            IntPtr pointer = NativeMethods.GetLastErrorPointer();
            return Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
        }
        catch (DllNotFoundException exception)
        {
            return exception.Message;
        }
        catch (EntryPointNotFoundException exception)
        {
            return exception.Message;
        }
    }

    private static NativeProbeResult Unavailable(string error)
    {
        return new NativeProbeResult(false, 0, null, error);
    }

    private static string? TryCollectRuntimeJson(out string? error)
    {
        return TryCollectJson(NativeMethods.CollectRuntimeJson, out error);
    }

    private static string? TryCollectJson(NativeJsonCollector collector, out string? error)
    {
        IntPtr pointer = IntPtr.Zero;
        try
        {
            int status = collector(out pointer);
            if (status != 0)
            {
                error = GetLastError();
                return null;
            }

            if (pointer == IntPtr.Zero)
            {
                error = "Нативная функция вернула пустой указатель.";
                return null;
            }

            error = null;
            return Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
        }
        catch (DllNotFoundException exception)
        {
            error = exception.Message;
            return null;
        }
        catch (EntryPointNotFoundException exception)
        {
            error = exception.Message;
            return null;
        }
        catch (BadImageFormatException exception)
        {
            error = exception.Message;
            return null;
        }
        finally
        {
            if (pointer != IntPtr.Zero)
            {
                NativeMethods.Free(pointer);
            }
        }
    }
}
