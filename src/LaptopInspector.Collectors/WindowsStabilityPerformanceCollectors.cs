using LaptopInspector.Core.Abstractions;
using LaptopInspector.Core.Models;
using LaptopInspector.Core.Services;

namespace LaptopInspector.Collectors;

public sealed class EventLogStabilityCollector : WindowsPowerShellCollectorBase
{
    public override string Id => "stability.events";

    public override string Category => "Stability";

    public override TimeSpan Timeout => TimeSpan.FromSeconds(75);

    protected override int JsonDepth => 9;

    protected override string BuildScript(CollectionContext context)
    {
        return "$days = " + context.Options.Days.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; " + """
$since = (Get-Date).AddDays(-[Math]::Abs($days))
$criticalAndError = Get-WinEvent -FilterHashtable @{LogName=@('System','Application'); Level=@(1,2); StartTime=$since} -ErrorAction SilentlyContinue
$groups = $criticalAndError | Group-Object ProviderName,Id | Sort-Object Count -Descending | Select-Object -First 80 @{Name='ProviderAndId';Expression={$_.Name}},Count,@{Name='FirstSeen';Expression={($_.Group | Sort-Object TimeCreated | Select-Object -First 1).TimeCreated}},@{Name='LastSeen';Expression={($_.Group | Sort-Object TimeCreated -Descending | Select-Object -First 1).TimeCreated}},@{Name='SampleMessage';Expression={($_.Group | Select-Object -First 1).Message}}
$bugChecks = Get-WinEvent -FilterHashtable @{LogName='System'; Id=1001; ProviderName='Microsoft-Windows-WER-SystemErrorReporting'; StartTime=$since} -ErrorAction SilentlyContinue | Select-Object TimeCreated,Id,ProviderName,Message
$kernelPower = Get-WinEvent -FilterHashtable @{LogName='System'; Id=41; ProviderName='Microsoft-Windows-Kernel-Power'; StartTime=$since} -ErrorAction SilentlyContinue | Select-Object TimeCreated,Id,Message
$unexpectedShutdown = Get-WinEvent -FilterHashtable @{LogName='System'; Id=6008; StartTime=$since} -ErrorAction SilentlyContinue | Select-Object TimeCreated,Id,ProviderName,Message
$whea = Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Microsoft-Windows-WHEA-Logger'; StartTime=$since} -ErrorAction SilentlyContinue | Select-Object TimeCreated,Id,LevelDisplayName,Message
$disk = Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName=@('disk','Ntfs','stornvme','volmgr'); StartTime=$since} -ErrorAction SilentlyContinue | Where-Object { $_.Id -in @(7,11,51,55,98,153,161) } | Select-Object TimeCreated,Id,ProviderName,LevelDisplayName,Message
$display = Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Display'; Id=4101; StartTime=$since} -ErrorAction SilentlyContinue | Select-Object TimeCreated,Id,Message
$appCrashes = Get-WinEvent -FilterHashtable @{LogName='Application'; Id=@(1000,1002); StartTime=$since} -ErrorAction SilentlyContinue | Group-Object ProviderName,Id | Sort-Object Count -Descending | Select-Object -First 40 Name,Count,@{Name='Sample';Expression={($_.Group | Select-Object -First 1).Message}}
$reliability = Get-CimInstance Win32_ReliabilityStabilityMetrics -ErrorAction SilentlyContinue | Sort-Object TimeGenerated -Descending | Select-Object -First 30 TimeGenerated,SystemStabilityIndex
$minidumps = Get-ChildItem -Path (Join-Path $env:SystemRoot 'Minidump') -Filter '*.dmp' -ErrorAction SilentlyContinue | Select-Object Name,FullName,Length,CreationTime,LastWriteTime
[ordered]@{
  source = 'Get-WinEvent + Reliability Monitor CIM'
  days = $days
  since = $since
  criticalAndErrorGroups = $groups
  bugChecks = $bugChecks
  kernelPower41 = $kernelPower
  unexpectedShutdown6008 = $unexpectedShutdown
  wheaLogger = $whea
  diskAndFileSystemErrors = $disk
  displayTdr4101 = $display
  applicationCrashGroups = $appCrashes
  reliabilityMetrics = $reliability
  minidumps = $minidumps
}
""";
    }

    protected override IReadOnlyList<Finding> Analyze(JsonNodeAdapter data, CollectionContext context)
    {
        return Array.Empty<Finding>();
    }
}

public sealed class PerformanceSnapshotCollector : WindowsPowerShellCollectorBase
{
    public override string Id => "performance.snapshot";

    public override string Category => "Performance";

    public override CollectorPriority Priority => CollectorPriority.Low;

    public override TimeSpan Timeout => TimeSpan.FromSeconds(35);

    protected override string BuildScript(CollectionContext context)
    {
        return """
$counters = if (Get-Command Get-Counter -ErrorAction SilentlyContinue) {
  Get-Counter -Counter '\Processor(_Total)\% Processor Time','\Memory\Available MBytes','\PhysicalDisk(_Total)\% Disk Time','\System\Processor Queue Length' -SampleInterval 1 -MaxSamples 3 -ErrorAction SilentlyContinue
} else { $null }
$counterSamples = if ($null -eq $counters) { @() } else { $counters.CounterSamples | Select-Object Path,CookedValue,Timestamp }
$cpu = Get-CimInstance Win32_PerfFormattedData_PerfOS_Processor -ErrorAction SilentlyContinue | Select-Object Name,PercentProcessorTime,PercentUserTime,PercentPrivilegedTime,InterruptsPersec,DPCRate
$memory = Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory -ErrorAction SilentlyContinue | Select-Object AvailableMBytes,CommittedBytes,CommitLimit,PagesPersec,CacheBytes,PoolNonpagedBytes,PoolPagedBytes
$disk = Get-CimInstance Win32_PerfFormattedData_PerfDisk_PhysicalDisk -ErrorAction SilentlyContinue | Select-Object Name,PercentDiskTime,DiskReadsPersec,DiskWritesPersec,AvgDisksecPerRead,AvgDisksecPerWrite,CurrentDiskQueueLength
[ordered]@{
  source = 'PDH counters through PowerShell + Win32_PerfFormattedData_*'
  benchmarkEnabled = $false
  note = 'Сборщик выполняет только read-only snapshot. Записывающие тесты запускаются отдельными коллекторами при --benchmark и --deep-disk.'
  counterSamples = $counterSamples
  processor = $cpu
  memory = $memory
  disk = $disk
}
""";
    }
}

public sealed class CollectionCoverageCollector : ICollector
{
    public string Id => "scan.coverage";

    public string Category => "Collection";

    public CollectorPriority Priority => CollectorPriority.Background;

    public bool RequiresAdmin => false;

    public TimeSpan Timeout => TimeSpan.FromSeconds(3);

    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    public Task<CollectorResult> CollectAsync(CollectionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        System.Text.Json.Nodes.JsonArray sections = new();
        Add(sections, "4.1", "Идентификация системы", "system.identity,native.firmware", "Реальные SMBIOS-данные доступны через native.firmware на Windows.");
        Add(sections, "4.2", "BIOS / UEFI / прошивка", "native.firmware", "UEFI db/dbx детальная расшифровка будет расширена после добавления privilege-aware UEFI variable reader.");
        Add(sections, "4.3", "Процессор", "hardware.processor", "CPUID decode реализован в licore; прямой native export будет подключён при расширении firmware/CPU collector.");
        Add(sections, "4.4", "Оперативная память", "hardware.memory", "SPD требует SMBus/опциональный драйвер и пока явно не собирается.");
        Add(sections, "4.5", "Материнская плата, чипсет, шины", "hardware.bus,native.deviceTree", "PCIe link width/speed зависят от доступности DEVPKEY драйверов.");
        Add(sections, "4.6", "Накопители", "native.storage", "SMART/NVMe Health read-only; BitLocker в security.overview.");
        Add(sections, "4.7", "Графика и дисплеи", "native.display", "EDID через registry/SetupAPI будет добавлен в C# stage refinement.");
        Add(sections, "4.8", "Аккумулятор и питание", "native.battery,power.thermal", "powercfg /energy не запускается без явного флага.");
        Add(sections, "4.9", "Температуры, вентиляторы, сенсоры", "power.thermal,native.sensors", "Вентиляторы обычно требуют vendor WMI или драйвер.");
        Add(sections, "4.10", "Сеть", "network.overview,native.wlan", "Wi-Fi ключи никогда не собираются.");
        Add(sections, "4.11", "Аудио, камера, ввод", "peripherals.audioCameraInput", "Media Foundation capabilities будут добавлены позднее.");
        Add(sections, "4.12", "USB и периферия", "native.usb", "Hub IOCTL topology будет расширен позднее.");
        Add(sections, "4.13", "Диспетчер устройств", "native.deviceTree", "Скрытые/отключённые устройства собираются только при будущей опции history/deep.");
        Add(sections, "4.14", "Операционная система", "os.windows", "SFC/DISM CBS deep parsing отложен.");
        Add(sections, "4.15", "Программное обеспечение", "software.inventory", "Пароли, cookies и история браузеров не собираются.");
        Add(sections, "4.16", "Безопасность", "security.overview,native.tpm", "Security score рассчитывает Health Rules Engine по правилам из rules.json.");
        Add(sections, "4.17", "Журналы событий и стабильность", "stability.events", "Глубина задаётся --days.");
        Add(sections, "4.18", "Производительность", "performance.snapshot", "Полные бенчмарки выполняются отдельными коллекторами при --benchmark.");
        Add(sections, "4.19", "Виртуализация и окружение разработки", "virtualization.environment", "Информативный read-only сбор.");

        System.Text.Json.Nodes.JsonObject data = new()
        {
            ["source"] = "static coverage map",
            ["sections"] = sections
        };
        return Task.FromResult(CollectorResult.Success(Id, TimeSpan.Zero, data));
    }

    private static void Add(System.Text.Json.Nodes.JsonArray array, string number, string title, string collectors, string note)
    {
        array.Add(new System.Text.Json.Nodes.JsonObject
        {
            ["section"] = number,
            ["title"] = title,
            ["collectors"] = collectors,
            ["note"] = note
        });
    }
}
