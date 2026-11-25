# LaptopInspector

`LaptopInspector` — консольное read-only приложение для тотального анализа ноутбука под Windows 10/11. Репозиторий содержит **Этапы 1–8**: каркас решения, переносимое C17-ядро `licore`, C++20 native layer, C# сборщики для разделов 4.1–4.19, Health Rules Engine на 70+ правил, расширенные отчёты, опциональные benchmark/vendor modules, CI, документацию и sample reports.

## Диаграмма модулей

```text
LaptopInspector.Cli
  ├─ LaptopInspector.Core        — модели, ICollector, планировщик, приватность, Health Rules Engine
  ├─ LaptopInspector.Collectors  — начальные сборщики среды и конфигурации
  ├─ LaptopInspector.Reporting   — offline HTML dashboard, JSON Schema, TXT, Markdown, CSV ZIP и diff
  └─ LaptopInspector.Interop     — безопасная обёртка P/Invoke
        └─ LaptopInspector.Native.dll/.so (C++20)
              ├─ SetupAPI / CfgMgr32 device tree
              ├─ Storage IOCTL / SMART / NVMe Health
              ├─ Battery IOCTL
              ├─ USB SetupAPI
              ├─ DXGI / DisplayConfig
              ├─ WLAN API
              ├─ Firmware tables / UEFI variables
              ├─ TPM TBS probe / Sensor device probe
              └─ licore (C17)
                    ├─ SMBIOS/DMI parser
                    ├─ EDID/CTA-861/DisplayID parser
                    ├─ ACPI table header parser
                    ├─ CPUID decoder
                    ├─ ATA S.M.A.R.T. decoder
                    ├─ NVMe Health Log decoder
                    └─ CRC32/SHA-256
```

## Быстрый старт

```powershell
./build.ps1
./src/LaptopInspector.Cli/bin/Release/net8.0/LaptopInspector.exe scan --output ./.reports --format html,json,txt --anonymize
```

Публикация single-file:

```powershell
./build.ps1 -Configuration Release -Runtime win-x64 -Publish
```

Генерация примерного отчёта на синтетических данных:

```powershell
python tools/generate_sample_report.py
```

Примеры сохраняются в `sample_reports/`.

Нативные C-тесты через CMake:

```powershell
cmake -S src/native -B artifacts/native -DLI_BUILD_NATIVE_TESTS=ON
cmake --build artifacts/native --config Release
ctest --test-dir artifacts/native --output-on-failure
```

## Команды CLI

```text
LaptopInspector.exe [scan|quick|diff|list-collectors|selftest] [options]

scan                 Полный сбор каркасных сборщиков
quick                Быстрый сбор с глобальным таймаутом 30 секунд
diff <a> <b>         Сравнение двух JSON-отчётов
list-collectors      Список зарегистрированных сборщиков
selftest             Самопроверка каркаса

Опции scan/quick:
  -o, --output <dir>         Каталог отчётов
  -f, --format <list>        html,json,txt,md,csv
  --include <ids>            Только указанные сборщики или маски
  --exclude <ids>            Исключить сборщики или маски
  --days <N>                 Глубина анализа журналов
  --benchmark                Включить будущие бенчмарки
  --energy                   Включить будущий powercfg /energy
  --anonymize                Маскировать идентификаторы
  --history                  Включить будущую историю USB/Wi-Fi
  --deep-disk                Включить будущий анализ крупнейших папок
  --online                   Разрешить будущие сетевые проверки
  --raw                      Включать raw-дампы
  --lang <ru|en>             Язык отчёта
  --timeout <sec>            Глобальный таймаут
  --parallel <N>             Степень параллелизма
  --no-elevate               Не предлагать повышение прав
  -v, --verbose              Подробный лог
  --open                     Открыть HTML после завершения
```

## Что реализовано в Этапе 1

- структура решения `.sln`, проекты `.csproj`, единая сборка `build.ps1`;
- интерфейс `ICollector`, модели результатов, находок, отчёта и параметров запуска;
- планировщик с зависимостями, таймаутами, параллельным выполнением уровней графа и изоляцией ошибок;
- CLI-команды `scan`, `quick`, `diff`, `list-collectors`, `selftest`;
- Serilog в консоль и файл `LaptopInspector.log` рядом с отчётом;
- базовые генераторы HTML, JSON, TXT, Markdown и CSV ZIP;
- режим `--anonymize` для идентификаторов, редактирование секретов в командных строках;
- минимальные CMake-проекты `licore` и `LaptopInspector.Native` с плоским C ABI;
- JSON Schema начальной модели отчёта;
- xUnit-тесты инфраструктуры.

## Что реализовано в Этапе 2

- C17 API `licore/li_core.h` с `li_json_output`, статусами ошибок и функциями освобождения памяти;
- безопасный JSON-builder без глобального состояния;
- SMBIOS parser: структуры DMI, строки, raw hex, декодирование основных типов 0/1/2/3/4/16/17/32 и имена типов 0–46/OEM;
- EDID parser: base block EDID 1.4, manufacturer EISA ID, descriptor blocks, standard timings, chromaticity, CTA-861 data blocks, DisplayID generic blocks;
- ACPI parser: последовательность таблиц с header/OEM/checksum и деталями FADT, MADT, BGRT, FPDT, TPM2, BERT, WSMT, MSDM с маскированием OEM-ключа до последних 5 символов;
- CPUID decoder: vendor/brand/family/model/stepping, leaf 1/7/extended flags, cache leaf 4, topology 0x0B/0x1F, hybrid core type 0x1A, raw leaves;
- ATA S.M.A.R.T. decoder: 30 атрибутов, имена популярных ID, normalized/worst/raw, температура, часы, LBA read/write estimate;
- NVMe Health Log decoder: Critical Warning по битам, температура, spare, percentage used, 128-bit counters, TB estimate, temperature sensors;
- CRC32 и SHA-256;
- фикстуры `tests/fixtures/*.bin` и C-тесты `tests/native_tests/native_tests.c`.

## Что реализовано в Этапе 3

- ABI `LaptopInspector.Native` поднят до версии 2;
- добавлен общий C++ JSON-writer, UTF-16 → UTF-8, Win32/HRESULT error formatting и единое выделение буферов DLL;
- экспортированы read-only C ABI функции:
  - `li_collect_device_tree_json` — SetupAPI/CfgMgr32: PnP-устройства, class GUID, hardware/compatible IDs, драйверные ключи, location paths, problem codes;
  - `li_collect_storage_json` — `\\.\\PhysicalDriveN`, `IOCTL_STORAGE_QUERY_PROPERTY`, геометрия дисков, seek penalty, write cache, ATA SMART и NVMe Health через `licore`;
  - `li_collect_battery_json` — `GUID_DEVINTERFACE_BATTERY`, `IOCTL_BATTERY_QUERY_*`, ёмкость, износ, циклы, напряжение, rate/status;
  - `li_collect_usb_json` — USB устройства и device interfaces через SetupAPI;
  - `li_collect_display_json` — DXGI adapters/outputs, VRAM/shared memory, DisplayConfig active paths;
  - `li_collect_wlan_json` — WLAN interfaces, current connection, available networks без сбора ключей;
  - `li_collect_firmware_json` — SMBIOS через `GetSystemFirmwareTable('RSMB')`, ACPI table enumeration/parsing, SecureBoot UEFI variable;
  - `li_collect_tpm_json` — TBS dynamic context probe;
  - `li_collect_sensor_json` — Sensor class device enumeration;
- C# `LaptopInspector.Interop` умеет вызывать все новые нативные функции через `NativeBridge.TryCollect`;
- на не-Windows платформах все функции возвращают валидный JSON `unsupported`, поэтому сборка и тесты не ломаются.

## Что реализовано в Этапе 4

- добавлен безопасный `CommandRunner` с таймаутами и kill process tree;
- добавлен `WindowsPowerShellCollectorBase`, который запускает read-only PowerShell/CIM/registry scripts и возвращает JSON;
- добавлен `JsonPrivacyScrubber`: при `--anonymize` детерминированно маскируются serial/UUID/MAC/BSSID/SSID/user/instance IDs/device paths/product keys;
- native exports Этапа 3 подключены как C# collectors:
  - `native.firmware`, `native.deviceTree`, `native.storage`, `native.battery`, `native.usb`, `native.display`, `native.wlan`, `native.tpm`, `native.sensors`;
- добавлены Windows collectors:
  - `system.identity` — идентификация системы, BIOS/baseboard/enclosure, Entra join probe;
  - `hardware.processor` — CPU CIM, частоты, counters, registry microcode field;
  - `hardware.memory` — OS memory, physical modules, arrays, pagefiles;
  - `hardware.bus` — PCI/USB4/chipset candidates and slots;
  - `os.windows` — OS build, hotfixes, optional features, pending reboot, .NET runtimes;
  - `software.inventory` — uninstall registry, AppX, autoruns, services, tasks, processes with secret redaction;
  - `security.overview` — SecurityCenter2, Defender, TPM, BitLocker, DeviceGuard, UAC, RDP/NLA, WinRM, SMB1, password policy;
  - `network.overview` — adapters, IP/DNS/routes, listening ports, firewall, proxy, hosts, SMB shares;
  - `power.thermal` — powercfg `/a`, active scheme, ACPI thermal zones, throttling events;
  - `peripherals.audioCameraInput` — audio/camera/biometric/input devices and keyboard layouts;
  - `stability.events` — System/Application critical/error grouping, BSOD, Kernel-Power, WHEA, disk, TDR, app crash, reliability metrics, minidumps;
  - `performance.snapshot` — read-only PDH/CIM performance snapshot;
  - `virtualization.environment` — Hyper-V/WSL/Docker/VM hints;
  - `scan.coverage` — карта покрытия разделов 4.1–4.19 с явными ограничениями.

## Что реализовано в Этапе 5

- добавлен `HealthRulesEngine` в `LaptopInspector.Core.Rules`;
- добавлена модель `HealthRuleDefinition` / `HealthRuleCondition` / `HealthEvaluation`;
- добавлен расширяемый каталог `src/LaptopInspector.Core/rules.json`, копируемый рядом с CLI;
- добавлен встроенный fallback `HealthRuleCatalog.DefaultRulesJson`, если внешний файл правил отсутствует или повреждён;
- реализованы операторы правил: `exists`, `isNullOrEmpty`, `equals`, `notEquals`, `greaterThan`, `anyGreaterThan`, `anyLessThan`, `containsText`, `anyContainsText`, `anyEquals`, `anyIn`, `countEquals`, `countGreaterOrEqual`, `countLessThan`, `countTextGreaterOrEqual`, `anyEqualsWithSiblingGreaterThan`, `statusIn`;
- каталог содержит 71 правило по батарее, накопителям, температурам, памяти, устройствам, сети, безопасности, ОС, ПО, стабильности, производительности, виртуализации, прошивке, benchmark и покрытию;
- scoring формирует `Hardware Health Score`, `Security Score`, `Stability Score`, `Overall Score`, penalties по bucket и вердикт;
- `ReportFactory` интегрирует findings правил в HTML/JSON/TXT/MD/CSV отчёты;
- добавлены xUnit-тесты `HealthRulesEngineTests`;
- добавлена документация `docs/health-rules.md`.

## Что реализовано в Этапе 6

- HTML-отчёт расширен до offline dashboard: SVG score gauges, ключевые компоненты, severity-фильтры, поиск, сортируемая таблица сборщиков, раскрываемые data sections, raw JSON, светлая/тёмная тема и print CSS;
- TXT-отчёт теперь содержит метаданные, score summary, penalties, severity summary, полный список findings, журнал сборщиков и инвентарь data sections;
- Markdown-отчёт содержит GitHub-friendly таблицы оценок, penalties, findings, collectors и data inventory;
- CSV ZIP расширен файлами `software_programs.csv`, `software_services.csv`, `software_processes.csv`, `devices.csv`, `usb_devices.csv`, `storage_physical_drives.csv`, `battery.csv`, `events_*.csv`, `network_adapters.csv`;
- `diff.html` теперь сравнивает оценки, батарею, NVMe wear/write counters, программы, устройства/драйверы, события стабильности, настройки безопасности, статусы сборщиков и rule findings;
- `schema/report.schema.json` обновлён до подробной JSON Schema draft 2020-12;
- добавлен `ReportDataExplorer` для безопасного чтения и flatten JSON-данных;
- добавлена документация `docs/reporting.md`.

## Что реализовано в Этапе 7

- добавлен `benchmark.synthetic`, включаемый только флагом `--benchmark`;
- CPU benchmark: single-thread и multi-thread integer/hash workload со score;
- memory benchmark: copy bandwidth и pointer-chasing latency;
- disk benchmark пишет временный файл 128 МиБ только при явном подтверждении переменной окружения `LAPTOPINSPECTOR_CONFIRM_DISK_BENCHMARK=1`; без неё раздел помечается как skipped;
- добавлен `power.energy`, включаемый только `--energy`, запускает `powercfg /energy /xml` и сохраняет XML рядом с отчётом;
- добавлен `storage.deepDisk`, включаемый только `--deep-disk`, считает top-20 каталогов системного диска с тайм-бюджетом и обработкой отказов доступа;
- добавлен `optional.driverProbe`: read-only probe наличия PawnIO/WinRing0/OpenLibSys/InpOut/RwDrv-подобных драйверов без открытия устройств и без установки драйверов;
- ABI `LaptopInspector.Native` поднят до версии 3;
- добавлен `li_collect_gpu_vendor_json`: динамический probe NVML (`nvml.dll`) с температурой/утилизацией/частотами/power limit, а также наличие ADLX/ADL/IGCL DLL;
- добавлен C# collector `native.gpuVendor`;
- health rules расширены до 71 правила: benchmark low score/bandwidth, GPU temperature, risky kernel driver probe.

## Что реализовано в Этапе 8

- добавлены расширенные документы: `docs/user-guide.md`, `docs/build-and-release.md`, `docs/testing.md`, `docs/privacy-and-security.md`, `docs/benchmarks.md`, `docs/reporting.md`, `docs/health-rules.md`;
- добавлены `SECURITY.md`, `LICENSE.md`, `THIRD_PARTY_NOTICES.md`;
- обновлён GitHub Actions CI: Windows build/test/publish/selftest/list-collectors, Linux native C/C++ smoke, sample report generation и JSON validation;
- добавлен генератор синтетического примера `tools/generate_sample_report.py`;
- добавлены sample reports в `sample_reports/` в форматах HTML/JSON/TXT/Markdown/CSV ZIP;
- добавлены xUnit-тесты CLI parser и `ReportDataExplorer`;
- README содержит команды сборки, публикации, запуска и генерации примеров.

## Ограничения текущей версии

- PowerShell/CIM collectors intentionally return `Skipped` on non-Windows platforms.
- Расширенные IOCTL USB hub topology и D3D12 feature-level details будут углублены на следующих этапах.
- PDF/DOCX остаются опциональными будущими форматами.
