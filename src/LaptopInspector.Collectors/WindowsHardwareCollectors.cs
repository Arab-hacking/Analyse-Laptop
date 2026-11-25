using LaptopInspector.Core.Models;
using LaptopInspector.Core.Services;

namespace LaptopInspector.Collectors;

public sealed class SystemIdentificationCollector : WindowsPowerShellCollectorBase
{
    public override string Id => "system.identity";

    public override string Category => "System";

    public override CollectorPriority Priority => CollectorPriority.High;

    public override TimeSpan Timeout => TimeSpan.FromSeconds(25);

    protected override string BuildScript(CollectionContext context)
    {
        return """
$computerSystem = Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue | Select-Object Manufacturer,Model,SystemFamily,SystemSKUNumber,Domain,PartOfDomain,Workgroup,TotalPhysicalMemory,UserName
$product = Get-CimInstance Win32_ComputerSystemProduct -ErrorAction SilentlyContinue | Select-Object Vendor,Name,Version,IdentifyingNumber,UUID,SKUNumber
$bios = Get-CimInstance Win32_BIOS -ErrorAction SilentlyContinue | Select-Object Manufacturer,SMBIOSBIOSVersion,Version,ReleaseDate,SerialNumber,SMBIOSMajorVersion,SMBIOSMinorVersion,EmbeddedControllerMajorVersion,EmbeddedControllerMinorVersion
$baseBoard = Get-CimInstance Win32_BaseBoard -ErrorAction SilentlyContinue | Select-Object Manufacturer,Product,Version,SerialNumber,Tag
$enclosure = Get-CimInstance Win32_SystemEnclosure -ErrorAction SilentlyContinue | Select-Object Manufacturer,SerialNumber,SMBIOSAssetTag,ChassisTypes,LockPresent,SecurityStatus
$dsregRaw = $null
$dsregCommand = Get-Command dsregcmd.exe -ErrorAction SilentlyContinue
if ($null -ne $dsregCommand) { $dsregRaw = (& dsregcmd.exe /status 2>$null) -join "`n" }
[ordered]@{
  source = 'CIM Win32_* + dsregcmd'
  computerSystem = $computerSystem
  product = $product
  bios = $bios
  baseBoard = $baseBoard
  enclosure = $enclosure
  entraJoin = [ordered]@{
    azureAdJoined = if ($null -eq $dsregRaw) { $null } else { [bool]($dsregRaw -match 'AzureAdJoined\s*:\s*YES') }
    domainJoined = if ($null -eq $dsregRaw) { $null } else { [bool]($dsregRaw -match 'DomainJoined\s*:\s*YES') }
    workplaceJoined = if ($null -eq $dsregRaw) { $null } else { [bool]($dsregRaw -match 'WorkplaceJoined\s*:\s*YES') }
    rawAvailable = $null -ne $dsregRaw
  }
}
""";
    }
}

public sealed class ProcessorCollector : WindowsPowerShellCollectorBase
{
    public override string Id => "hardware.processor";

    public override string Category => "Processor";

    public override CollectorPriority Priority => CollectorPriority.High;

    public override TimeSpan Timeout => TimeSpan.FromSeconds(25);

    protected override string BuildScript(CollectionContext context)
    {
        return """
$cpu = Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue | Select-Object DeviceID,Name,Manufacturer,Description,Architecture,Family,ProcessorType,Revision,Stepping,NumberOfCores,NumberOfEnabledCore,NumberOfLogicalProcessors,MaxClockSpeed,CurrentClockSpeed,L2CacheSize,L3CacheSize,VirtualizationFirmwareEnabled,VMMonitorModeExtensions,SecondLevelAddressTranslationExtensions
$perf = Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -ErrorAction SilentlyContinue | Select-Object Name,PercentProcessorTime,PercentUserTime,PercentPrivilegedTime,InterruptsPersec,DPCRate
$centralProcessor = Get-ItemProperty -Path 'HKLM:\HARDWARE\DESCRIPTION\System\CentralProcessor\0' -ErrorAction SilentlyContinue
$speculation = [ordered]@{ available = $false; source = 'PowerShell module SpeculationControl is optional' }
[ordered]@{
  source = 'CIM Win32_Processor + performance counters + registry'
  processors = $cpu
  performance = $perf
  registry = if ($null -eq $centralProcessor) { $null } else { [ordered]@{
    identifier = $centralProcessor.Identifier
    processorNameString = $centralProcessor.ProcessorNameString
    vendorIdentifier = $centralProcessor.VendorIdentifier
    mhz = $centralProcessor.'~MHz'
    updateRevision = if ($null -eq $centralProcessor.'Update Revision') { $null } else { [Convert]::ToBase64String($centralProcessor.'Update Revision') }
  } }
  speculationControl = $speculation
}
""";
    }
}

public sealed class MemoryCollector : WindowsPowerShellCollectorBase
{
    public override string Id => "hardware.memory";

    public override string Category => "Memory";

    public override CollectorPriority Priority => CollectorPriority.High;

    public override TimeSpan Timeout => TimeSpan.FromSeconds(25);

    protected override string BuildScript(CollectionContext context)
    {
        return """
$os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue | Select-Object TotalVisibleMemorySize,FreePhysicalMemory,TotalVirtualMemorySize,FreeVirtualMemory,SizeStoredInPagingFiles,FreeSpaceInPagingFiles
$modules = Get-CimInstance Win32_PhysicalMemory -ErrorAction SilentlyContinue | Select-Object BankLabel,DeviceLocator,Capacity,ConfiguredClockSpeed,ConfiguredVoltage,FormFactor,Manufacturer,PartNumber,SerialNumber,SMBIOSMemoryType,Speed,TotalWidth,DataWidth,InterleavePosition,Tag
$arrays = Get-CimInstance Win32_PhysicalMemoryArray -ErrorAction SilentlyContinue | Select-Object MaxCapacity,MaxCapacityEx,MemoryDevices,MemoryErrorCorrection
$pageFiles = Get-CimInstance Win32_PageFileUsage -ErrorAction SilentlyContinue | Select-Object Name,AllocatedBaseSize,CurrentUsage,PeakUsage,TempPageFile
$compression = Get-CimInstance -Namespace root\cimv2 -ClassName Win32_PerfFormattedData_PerfOS_Memory -ErrorAction SilentlyContinue | Select-Object AvailableMBytes,CacheBytes,CommittedBytes,CommitLimit,PoolNonpagedBytes,PoolPagedBytes,PagesPersec
[ordered]@{
  source = 'CIM memory classes'
  operatingSystemMemory = $os
  physicalMemoryModules = $modules
  physicalMemoryArrays = $arrays
  pageFiles = $pageFiles
  performance = $compression
  upgradeHeuristic = [ordered]@{
    moduleCount = @($modules).Count
    arrayCount = @($arrays).Count
    freeSlotsKnown = if ($null -eq $arrays) { $null } else { [Math]::Max(0, ($arrays | Measure-Object -Property MemoryDevices -Sum).Sum - @($modules).Count) }
  }
}
""";
    }
}

public sealed class MotherboardBusCollector : WindowsPowerShellCollectorBase
{
    public override string Id => "hardware.bus";

    public override string Category => "Bus";

    public override TimeSpan Timeout => TimeSpan.FromSeconds(35);

    protected override string BuildScript(CollectionContext context)
    {
        return """
$pci = Get-CimInstance Win32_PnPEntity -ErrorAction SilentlyContinue | Where-Object { $_.PNPDeviceID -like 'PCI\*' } | Select-Object Name,PNPDeviceID,Manufacturer,Service,ClassGuid,ConfigManagerErrorCode,Status
$usb4 = Get-CimInstance Win32_PnPEntity -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'Thunderbolt|USB4|PCI Express Root|eSPI|LPC' } | Select-Object Name,PNPDeviceID,Manufacturer,Service,ClassGuid,ConfigManagerErrorCode,Status
$slots = Get-CimInstance Win32_SystemSlot -ErrorAction SilentlyContinue | Select-Object SlotDesignation,Tag,ConnectorPinout,ConnectorType,CurrentUsage,Status,SupportsHotPlug,Shared,PMESignal,MaxDataWidth
[ordered]@{
  source = 'CIM Win32_PnPEntity + Win32_SystemSlot'
  pciDevices = $pci
  thunderboltUsb4ChipsetCandidates = $usb4
  systemSlots = $slots
  note = 'Текущая и максимальная ширина/скорость PCIe линка читаются нативным сборщиком deviceTree, если DEVPKEY доступны драйверу.'
}
""";
    }
}
