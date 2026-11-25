# Benchmarks and optional modules

Этап 7 добавляет опциональные read-only и explicitly-confirmed модули. По умолчанию они не создают нагрузку и не пишут временные benchmark-файлы.

## `--benchmark`

Флаг `--benchmark` включает `benchmark.synthetic`:

- CPU single-thread synthetic hash workload;
- CPU multi-thread synthetic hash workload;
- memory copy bandwidth;
- memory pointer-chasing latency.

Результаты ориентировочные: на них влияют питание, температура, фоновые задачи, троттлинг и режим Windows Power Mode.

### Disk benchmark

Дисковый benchmark пишет временный файл 128 МиБ в каталог отчёта. Чтобы случайно не выполнять запись, одного `--benchmark` недостаточно. Требуется явное подтверждение переменной окружения:

```powershell
$env:LAPTOPINSPECTOR_CONFIRM_DISK_BENCHMARK = '1'
LaptopInspector.exe scan --benchmark --output .reports
```

Если переменная не установлена, раздел `benchmark.synthetic.disk` содержит `status = skipped` и причину. Временный файл создаётся с `DeleteOnClose`; после завершения дополнительно выполняется попытка удаления.

## `--energy`

Флаг `--energy` включает `power.energy`:

```powershell
powercfg /energy /duration 60 /output powercfg_energy.xml /xml
```

XML сохраняется в каталог отчёта. В JSON добавляются путь, exit code, stderr/stdout, длина XML и простые счётчики Error/Warning/Informational.

## `--deep-disk`

Флаг `--deep-disk` включает `storage.deepDisk`:

- анализирует системный диск Windows;
- считает top-20 каталогов по размеру;
- пропускает недоступные файлы/каталоги;
- имеет тайм-бюджет и может вернуть `partial = true`.

## Vendor GPU probes

`native.gpuVendor` использует динамическую загрузку DLL и не требует SDK на этапе сборки:

- `nvml.dll` — при наличии читает count/name/temperature/utilization/clocks/power limit;
- `amdadlx64.dll`, `atiadlxx.dll`, `atiadlxy.dll` — probe наличия AMD ADLX/ADL DLL;
- `igcl.dll`, `IntelGraphicsControlLib.dll` — probe наличия Intel IGCL DLL.

Если DLL отсутствует, возвращается JSON с `present = false` или `available = false`; ошибок уровня сбора это не вызывает.

## Optional ring-0 driver probe

`optional.driverProbe` не устанавливает, не открывает и не использует kernel drivers. Он только читает CIM:

- `Win32_SystemDriver` для PawnIO/WinRing0/InpOut/RwDrv/OpenLibSys;
- `Win32_PnPEntity` по похожим именам.

Health rule `DRV-SEC-001` помечает WinRing-like драйверы как security risk.
