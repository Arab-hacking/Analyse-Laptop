using LaptopInspector.Core.Models;
using LaptopInspector.Core.Services;

namespace LaptopInspector.Collectors;

public sealed class NetworkCollector : WindowsPowerShellCollectorBase
{
    public override string Id => "network.overview";

    public override string Category => "Network";

    public override TimeSpan Timeout => TimeSpan.FromSeconds(35);

    protected override int JsonDepth => 9;

    protected override string BuildScript(CollectionContext context)
    {
        return """
$adapters = if (Get-Command Get-NetAdapter -ErrorAction SilentlyContinue) { Get-NetAdapter -ErrorAction SilentlyContinue | Select-Object Name,InterfaceDescription,InterfaceGuid,MacAddress,Status,LinkSpeed,MediaType,PhysicalMediaType,MtuSize,DriverInformation,DriverFileName,DriverVersion,DriverDate,InterfaceOperationalStatus,ConnectorPresent } else { @() }
$ip = if (Get-Command Get-NetIPConfiguration -ErrorAction SilentlyContinue) { Get-NetIPConfiguration -Detailed -ErrorAction SilentlyContinue | Select-Object InterfaceAlias,InterfaceDescription,NetProfile,IPv4Address,IPv6Address,IPv4DefaultGateway,IPv6DefaultGateway,DNSServer,DhcpServer,NetAdapter } else { @() }
$dns = if (Get-Command Get-DnsClientServerAddress -ErrorAction SilentlyContinue) { Get-DnsClientServerAddress -ErrorAction SilentlyContinue | Select-Object InterfaceAlias,AddressFamily,ServerAddresses } else { @() }
$routes = if (Get-Command Get-NetRoute -ErrorAction SilentlyContinue) { Get-NetRoute -ErrorAction SilentlyContinue | Select-Object -First 300 DestinationPrefix,NextHop,InterfaceAlias,RouteMetric,Protocol,AddressFamily } else { @() }
$tcp = if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) { Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue | Select-Object LocalAddress,LocalPort,OwningProcess,State,AppliedSetting } else { @() }
$udp = if (Get-Command Get-NetUDPEndpoint -ErrorAction SilentlyContinue) { Get-NetUDPEndpoint -ErrorAction SilentlyContinue | Select-Object LocalAddress,LocalPort,OwningProcess } else { @() }
$firewallProfiles = if (Get-Command Get-NetFirewallProfile -ErrorAction SilentlyContinue) { Get-NetFirewallProfile -ErrorAction SilentlyContinue | Select-Object Name,Enabled,DefaultInboundAction,DefaultOutboundAction,NotifyOnListen,AllowInboundRules,AllowLocalFirewallRules } else { @() }
$firewallAllowRules = if (Get-Command Get-NetFirewallRule -ErrorAction SilentlyContinue) { Get-NetFirewallRule -Enabled True -Action Allow -Direction Inbound -ErrorAction SilentlyContinue | Select-Object -First 200 DisplayName,Profile,Program,Service,Protocol,LocalPort,RemoteAddress } else { @() }
$proxy = [ordered]@{
  winHttp = if (Get-Command netsh.exe -ErrorAction SilentlyContinue) { (& netsh winhttp show proxy 2>$null) -join "`n" } else { $null }
  winInet = Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings' -ErrorAction SilentlyContinue | Select-Object ProxyEnable,ProxyServer,AutoConfigURL
}
$hostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'
$hosts = if (Test-Path $hostsPath) { Get-Content $hostsPath -ErrorAction SilentlyContinue | Where-Object { $_ -and $_.Trim() -and -not $_.Trim().StartsWith('#') } | Select-Object -First 100 } else { @() }
$shares = Get-CimInstance Win32_Share -ErrorAction SilentlyContinue | Select-Object Name,Path,Description,Type
[ordered]@{
  source = 'NetTCPIP/NetSecurity cmdlets + registry + CIM'
  adapters = $adapters
  ipConfiguration = $ip
  dnsServers = $dns
  routes = $routes
  listeningTcp = $tcp
  udpEndpoints = $udp
  firewallProfiles = $firewallProfiles
  inboundAllowRulesSample = $firewallAllowRules
  proxy = $proxy
  hostsNonCommentEntries = $hosts
  smbShares = $shares
  wifiDetails = 'Подробности Wi-Fi собираются native.wlan без чтения ключей.'
}
""";
    }
}

public sealed class SecurityCollector : WindowsPowerShellCollectorBase
{
    public override string Id => "security.overview";

    public override string Category => "Security";

    public override CollectorPriority Priority => CollectorPriority.High;

    public override TimeSpan Timeout => TimeSpan.FromSeconds(40);

    protected override int JsonDepth => 9;

    protected override string BuildScript(CollectionContext context)
    {
        return """
$securityCenterAv = Get-CimInstance -Namespace root\SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction SilentlyContinue | Select-Object displayName,instanceGuid,pathToSignedProductExe,pathToSignedReportingExe,productState,timestamp
$securityCenterFirewall = Get-CimInstance -Namespace root\SecurityCenter2 -ClassName FirewallProduct -ErrorAction SilentlyContinue | Select-Object displayName,instanceGuid,pathToSignedProductExe,productState,timestamp
$defender = if (Get-Command Get-MpComputerStatus -ErrorAction SilentlyContinue) { Get-MpComputerStatus -ErrorAction SilentlyContinue | Select-Object AMServiceEnabled,AntivirusEnabled,AntispywareEnabled,BehaviorMonitorEnabled,IoavProtectionEnabled,NISEnabled,OnAccessProtectionEnabled,RealTimeProtectionEnabled,IsTamperProtected,AntivirusSignatureVersion,AntivirusSignatureLastUpdated,AntispywareSignatureLastUpdated,QuickScanEndTime,FullScanEndTime,DefenderSignaturesOutOfDate } else { $null }
$defenderPreferences = if (Get-Command Get-MpPreference -ErrorAction SilentlyContinue) { Get-MpPreference -ErrorAction SilentlyContinue | Select-Object DisableRealtimeMonitoring,DisableBehaviorMonitoring,DisableIOAVProtection,PUAProtection,ControlledFolderAccessProtectedFolders,ExclusionPath,ExclusionProcess,ExclusionExtension } else { $null }
$tpm = if (Get-Command Get-Tpm -ErrorAction SilentlyContinue) { Get-Tpm -ErrorAction SilentlyContinue } else { $null }
$bitlocker = if (Get-Command Get-BitLockerVolume -ErrorAction SilentlyContinue) { Get-BitLockerVolume -ErrorAction SilentlyContinue | Select-Object MountPoint,VolumeStatus,ProtectionStatus,EncryptionPercentage,EncryptionMethod,LockStatus,AutoUnlockEnabled,@{Name='KeyProtectorTypes';Expression={ $_.KeyProtector | ForEach-Object KeyProtectorType }} } else { @() }
$deviceGuard = Get-CimInstance -ClassName Win32_DeviceGuard -Namespace root\Microsoft\Windows\DeviceGuard -ErrorAction SilentlyContinue
$uac = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System' -ErrorAction SilentlyContinue | Select-Object EnableLUA,ConsentPromptBehaviorAdmin,PromptOnSecureDesktop,EnableInstallerDetection,EnableVirtualization
$rdp = Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server' -ErrorAction SilentlyContinue | Select-Object fDenyTSConnections
$nla = Get-ItemProperty -Path 'HKLM:\SYSTEM\CurrentControlSet\Control\Terminal Server\WinStations\RDP-Tcp' -ErrorAction SilentlyContinue | Select-Object UserAuthentication,SecurityLayer
$winRm = Get-Service WinRM -ErrorAction SilentlyContinue | Select-Object Name,Status,StartType
$smb1 = if (Get-Command Get-WindowsOptionalFeature -ErrorAction SilentlyContinue) { Get-WindowsOptionalFeature -Online -FeatureName SMB1Protocol -ErrorAction SilentlyContinue | Select-Object FeatureName,State } else { $null }
$passwordPolicy = if (Get-Command net.exe -ErrorAction SilentlyContinue) { (& net accounts 2>$null) -join "`n" } else { $null }
$executionPolicy = if (Get-Command Get-ExecutionPolicy -ErrorAction SilentlyContinue) { Get-ExecutionPolicy -List -ErrorAction SilentlyContinue } else { @() }
[ordered]@{
  source = 'SecurityCenter2 + Defender + TPM + BitLocker + registry'
  antivirusProducts = $securityCenterAv
  firewallProducts = $securityCenterFirewall
  defender = $defender
  defenderPreferencesSummary = if ($null -eq $defenderPreferences) { $null } else { [ordered]@{
    disableRealtimeMonitoring = $defenderPreferences.DisableRealtimeMonitoring
    puaProtection = $defenderPreferences.PUAProtection
    controlledFolderAccessProtectedFolderCount = @($defenderPreferences.ControlledFolderAccessProtectedFolders).Count
    exclusionPathCount = @($defenderPreferences.ExclusionPath).Count
    exclusionProcessCount = @($defenderPreferences.ExclusionProcess).Count
    exclusionExtensionCount = @($defenderPreferences.ExclusionExtension).Count
  } }
  tpm = $tpm
  bitLockerVolumes = $bitlocker
  deviceGuard = $deviceGuard
  uac = $uac
  rdp = [ordered]@{ terminalServer = $rdp; nla = $nla }
  winRm = $winRm
  smb1 = $smb1
  passwordPolicyRaw = $passwordPolicy
  powerShellExecutionPolicy = $executionPolicy
  secretPolicy = 'Recovery keys, passwords, cookies and browser history are not collected.'
}
""";
    }
}

public sealed class PowerThermalCollector : WindowsPowerShellCollectorBase
{
    public override string Id => "power.thermal";

    public override string Category => "Power";

    public override TimeSpan Timeout => TimeSpan.FromSeconds(40);

    protected override string BuildScript(CollectionContext context)
    {
        return """
$powercfgA = if (Get-Command powercfg.exe -ErrorAction SilentlyContinue) { (& powercfg.exe /a 2>$null) -join "`n" } else { $null }
$activeScheme = if (Get-Command powercfg.exe -ErrorAction SilentlyContinue) { (& powercfg.exe /getactivescheme 2>$null) -join "`n" } else { $null }
$thermalZones = Get-CimInstance -Namespace root\wmi -ClassName MSAcpi_ThermalZoneTemperature -ErrorAction SilentlyContinue | Select-Object InstanceName,CurrentTemperature,CriticalTripPoint,PassiveTripPoint
$thermalCounters = Get-CimInstance Win32_PerfFormattedData_Counters_ThermalZoneInformation -ErrorAction SilentlyContinue | Select-Object Name,HighPrecisionTemperature,Temperature
$processorPowerEvents = Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Microsoft-Windows-Kernel-Processor-Power'; Id=37; StartTime=(Get-Date).AddDays(-30)} -ErrorAction SilentlyContinue | Select-Object -First 50 TimeCreated,Id,ProviderName,Message
[ordered]@{
  source = 'powercfg + ACPI thermal CIM + Kernel-Processor-Power events'
  powercfgAvailableSleepStatesRaw = $powercfgA
  activeSchemeRaw = $activeScheme
  thermalZones = $thermalZones
  thermalCounters = $thermalCounters
  processorPowerThrottlingEvents = $processorPowerEvents
  batteryDetails = 'Подробности батареи собираются native.battery.'
}
""";
    }
}

public sealed class AudioCameraInputCollector : WindowsPowerShellCollectorBase
{
    public override string Id => "peripherals.audioCameraInput";

    public override string Category => "Peripherals";

    public override TimeSpan Timeout => TimeSpan.FromSeconds(35);

    protected override string BuildScript(CollectionContext context)
    {
        return """
$entities = Get-CimInstance Win32_PnPEntity -ErrorAction SilentlyContinue
$audio = $entities | Where-Object { $_.PNPClass -in @('MEDIA','AudioEndpoint') -or $_.Name -match 'Audio|Realtek|Cirrus|Microphone|Speaker' } | Select-Object Name,PNPClass,PNPDeviceID,Manufacturer,Service,Status,ConfigManagerErrorCode
$cameras = $entities | Where-Object { $_.PNPClass -in @('Camera','Image') -or $_.Name -match 'Camera|Webcam|IR Camera|Hello' } | Select-Object Name,PNPClass,PNPDeviceID,Manufacturer,Service,Status,ConfigManagerErrorCode
$biometric = $entities | Where-Object { $_.PNPClass -eq 'Biometric' -or $_.Name -match 'Fingerprint|Biometric|Windows Hello' } | Select-Object Name,PNPClass,PNPDeviceID,Manufacturer,Service,Status,ConfigManagerErrorCode
$input = $entities | Where-Object { $_.PNPClass -in @('Keyboard','Mouse','HIDClass') -or $_.Name -match 'Touchpad|Digitizer|Pen|Touch Screen|Keyboard|Mouse' } | Select-Object Name,PNPClass,PNPDeviceID,Manufacturer,Service,Status,ConfigManagerErrorCode
$layouts = Get-WinUserLanguageList -ErrorAction SilentlyContinue | Select-Object LanguageTag,InputMethodTips
$touch = Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\PrecisionTouchPad' -ErrorAction SilentlyContinue
[ordered]@{
  source = 'CIM Win32_PnPEntity + language/PrecisionTouchPad registry'
  audioDevices = $audio
  cameras = $cameras
  biometricDevices = $biometric
  inputDevices = $input
  keyboardLayouts = $layouts
  precisionTouchPadRegistry = $touch
}
""";
    }
}
