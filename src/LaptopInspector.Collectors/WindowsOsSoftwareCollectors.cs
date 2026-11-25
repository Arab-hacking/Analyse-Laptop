using LaptopInspector.Core.Models;
using LaptopInspector.Core.Services;

namespace LaptopInspector.Collectors;

public sealed class OperatingSystemCollector : WindowsPowerShellCollectorBase
{
    public override string Id => "os.windows";

    public override string Category => "OperatingSystem";

    public override CollectorPriority Priority => CollectorPriority.High;

    public override TimeSpan Timeout => TimeSpan.FromSeconds(35);

    protected override string BuildScript(CollectionContext context)
    {
        return """
$os = Get-CimInstance Win32_OperatingSystem -ErrorAction SilentlyContinue | Select-Object Caption,Version,BuildNumber,OSArchitecture,InstallDate,LastBootUpTime,Locale,CountryCode,CodeSet,SerialNumber,WindowsDirectory,SystemDirectory,RegisteredUser,Organization
$computerInfo = Get-ComputerInfo -ErrorAction SilentlyContinue | Select-Object WindowsProductName,WindowsVersion,WindowsBuildLabEx,WindowsCurrentVersion,WindowsEditionId,OsHardwareAbstractionLayer,CsProcessors,CsSystemType,BiosFirmwareType,SecureBootState,HyperVisorPresent,DeviceGuardSecurityServicesConfigured,DeviceGuardSecurityServicesRunning
$hotFixes = Get-HotFix -ErrorAction SilentlyContinue | Sort-Object InstalledOn -Descending | Select-Object -First 80 HotFixID,Description,InstalledBy,InstalledOn
$pendingKeys = @(
  'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending',
  'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired',
  'HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager'
)
$pending = foreach ($key in $pendingKeys) {
  if (Test-Path $key) {
    if ($key -like '*Session Manager') {
      $value = (Get-ItemProperty -Path $key -Name PendingFileRenameOperations -ErrorAction SilentlyContinue).PendingFileRenameOperations
      if ($null -ne $value) { [ordered]@{ key = $key; present = $true; valueCount = @($value).Count } }
    } else { [ordered]@{ key = $key; present = $true } }
  }
}
$features = if (Get-Command Get-WindowsOptionalFeature -ErrorAction SilentlyContinue) { Get-WindowsOptionalFeature -Online -ErrorAction SilentlyContinue | Where-Object { $_.State -eq 'Enabled' } | Select-Object -First 120 FeatureName,State } else { @() }
$dotnet = Get-ChildItem 'HKLM:\SOFTWARE\dotnet\Setup\InstalledVersions' -ErrorAction SilentlyContinue | ForEach-Object { Get-ChildItem $_.PsPath -ErrorAction SilentlyContinue | ForEach-Object { Get-ItemProperty $_.PsPath -ErrorAction SilentlyContinue } } | Select-Object Version,InstallLocation,Architecture
$timeZone = Get-TimeZone -ErrorAction SilentlyContinue | Select-Object Id,DisplayName,StandardName,DaylightName,BaseUtcOffset,SupportsDaylightSavingTime
[ordered]@{
  source = 'CIM + registry + Windows optional feature APIs'
  operatingSystem = $os
  computerInfo = $computerInfo
  hotFixes = $hotFixes
  pendingReboot = [ordered]@{ pending = [bool](@($pending).Count -gt 0); evidence = $pending }
  enabledOptionalFeatures = $features
  dotnetRuntimes = $dotnet
  timeZone = $timeZone
  environment = [ordered]@{
    machinePathEntries = (($env:Path -split ';') | Where-Object { $_ })
    temp = $env:TEMP
    processorArchitecture = $env:PROCESSOR_ARCHITECTURE
  }
}
""";
    }
}

public sealed class SoftwareInventoryCollector : WindowsPowerShellCollectorBase
{
    public override string Id => "software.inventory";

    public override string Category => "Software";

    public override TimeSpan Timeout => TimeSpan.FromSeconds(60);

    protected override int JsonDepth => 9;

    protected override string BuildScript(CollectionContext context)
    {
        return """
function Get-UninstallEntries($Path, $Scope) {
  Get-ChildItem $Path -ErrorAction SilentlyContinue | ForEach-Object {
    $p = Get-ItemProperty $_.PsPath -ErrorAction SilentlyContinue
    if ($null -ne $p.DisplayName) {
      [ordered]@{
        scope = $Scope
        name = $p.DisplayName
        version = $p.DisplayVersion
        publisher = $p.Publisher
        installDate = $p.InstallDate
        estimatedSizeKb = $p.EstimatedSize
        installLocation = $p.InstallLocation
        uninstallStringPresent = [bool]$p.UninstallString
        registryKey = $_.Name
      }
    }
  }
}
$programs = @()
$programs += Get-UninstallEntries 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' 'HKLM64'
$programs += Get-UninstallEntries 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall' 'HKLM32'
$programs += Get-UninstallEntries 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall' 'HKCU'
$appx = if (Get-Command Get-AppxPackage -ErrorAction SilentlyContinue) { Get-AppxPackage -ErrorAction SilentlyContinue | Select-Object -First 200 Name,PackageFullName,Publisher,Version,InstallLocation,IsFramework,SignatureKind } else { @() }
$runKeys = @(
  'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run',
  'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run',
  'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run',
  'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce',
  'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce'
)
$autoruns = foreach ($key in $runKeys) {
  $item = Get-ItemProperty -Path $key -ErrorAction SilentlyContinue
  if ($null -ne $item) {
    $item.PSObject.Properties | Where-Object { $_.Name -notmatch '^PS' } | ForEach-Object { [ordered]@{ key = $key; name = $_.Name; command = ([string]$_.Value -replace '(?i)((password|passwd|pwd|token|secret|api[-_]?key|--key|/key|authorization)\s*[:=]\s*)\S+','$1<redacted>') } }
  }
}
$services = Get-CimInstance Win32_Service -ErrorAction SilentlyContinue | Select-Object Name,DisplayName,State,StartMode,StartName,PathName,ServiceType,ProcessId,ExitCode
$tasks = if (Get-Command Get-ScheduledTask -ErrorAction SilentlyContinue) { Get-ScheduledTask -ErrorAction SilentlyContinue | Select-Object -First 500 TaskPath,TaskName,State,Author,Description } else { @() }
$processes = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Select-Object ProcessId,ParentProcessId,Name,ExecutablePath,@{Name='CommandLine';Expression={ ([string]$_.CommandLine -replace '(?i)((password|passwd|pwd|token|secret|api[-_]?key|--key|/key|authorization)\s*[:=]\s*)\S+','$1<redacted>') }},CreationDate,WorkingSetSize,KernelModeTime,UserModeTime,ThreadCount,HandleCount
[ordered]@{
  source = 'Registry uninstall keys + AppX + CIM services/processes + ScheduledTasks'
  installedPrograms = $programs | Sort-Object name
  appxPackages = $appx
  autoruns = $autoruns
  services = $services
  scheduledTasks = $tasks
  processes = $processes
  browserCollectionPolicy = 'Только установленные пакеты и процессы; история, cookies и пароли не собираются.'
}
""";
    }
}

public sealed class VirtualizationCollector : WindowsPowerShellCollectorBase
{
    public override string Id => "virtualization.environment";

    public override string Category => "Virtualization";

    public override TimeSpan Timeout => TimeSpan.FromSeconds(25);

    protected override string BuildScript(CollectionContext context)
    {
        return """
$computerSystem = Get-CimInstance Win32_ComputerSystem -ErrorAction SilentlyContinue | Select-Object Manufacturer,Model,HypervisorPresent
$processor = Get-CimInstance Win32_Processor -ErrorAction SilentlyContinue | Select-Object Name,VirtualizationFirmwareEnabled,SecondLevelAddressTranslationExtensions,VMMonitorModeExtensions
$hyperv = Get-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All -ErrorAction SilentlyContinue | Select-Object FeatureName,State
$wsl = if (Get-Command wsl.exe -ErrorAction SilentlyContinue) { (& wsl.exe --list --verbose 2>$null) -join "`n" } else { $null }
$docker = Get-Command docker.exe -ErrorAction SilentlyContinue | Select-Object Source,Version
$vmHints = Get-CimInstance Win32_PnPEntity -ErrorAction SilentlyContinue | Where-Object { $_.Name -match 'VMware|VirtualBox|Hyper-V|VirtIO|QEMU' } | Select-Object Name,PNPDeviceID,Manufacturer,Status
[ordered]@{
  source = 'CIM + optional features + command probes'
  computerSystem = $computerSystem
  processor = $processor
  hyperVFeature = $hyperv
  wslListRaw = $wsl
  dockerCommand = $docker
  virtualizationDeviceHints = $vmHints
}
""";
    }
}
