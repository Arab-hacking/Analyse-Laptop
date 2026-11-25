# Руководство пользователя LaptopInspector

LaptopInspector — portable read-only диагностический инструмент для Windows 10/11 ноутбуков. Программа собирает сведения о железе, прошивке, ОС, драйверах, ПО, сети, безопасности, журналах событий, здоровье компонентов и формирует отчёты.

## Быстрый запуск

```powershell
LaptopInspector.exe scan --output .reports --format html,json,txt --anonymize
```

Рекомендуемый запуск для полного отчёта:

```powershell
LaptopInspector.exe scan `
  --output .reports `
  --format html,json,txt,md,csv `
  --days 30 `
  --anonymize `
  --verbose
```

## Команды

| Команда | Назначение |
|---|---|
| `scan` | Полный сбор данных. Используется по умолчанию. |
| `quick` | Быстрый сбор ключевых модулей с глобальным таймаутом 30 секунд. |
| `diff <old.json> <new.json>` | Сравнение двух JSON-отчётов. |
| `list-collectors` | Список зарегистрированных сборщиков. |
| `selftest` | Проверка DI/регистрации сборщиков и базовой инфраструктуры. |

## Форматы отчётов

```powershell
--format html,json,txt,md,csv
```

- `html` — основной offline dashboard;
- `json` — полная машинная модель;
- `txt` — текст для пересылки в чат/форум;
- `md` — Markdown для GitHub/wiki;
- `csv` — ZIP с CSV-файлами по табличным разделам.

## Приватность

Используйте:

```powershell
--anonymize
```

Будут маскированы serial/UUID/MAC/BSSID/SSID/user/instance IDs/device paths/product keys. Wi‑Fi пароли, BitLocker recovery keys, cookies, история браузера и содержимое пользовательских файлов не собираются никогда.

## Опциональные модули

| Опция | Что включает |
|---|---|
| `--benchmark` | CPU/memory synthetic benchmarks. Disk benchmark только при отдельном env-confirmation. |
| `--energy` | `powercfg /energy /xml`, занимает около 60 секунд. |
| `--deep-disk` | Top-20 каталогов системного диска по размеру с тайм-бюджетом. |
| `--history` | Зарезервировано для будущей истории USB/Wi‑Fi профилей без секретов. |
| `--online` | Зарезервировано для будущих сетевых проверок версий BIOS/драйверов. По умолчанию сеть не используется. |

## Disk benchmark confirmation

Дисковый benchmark пишет временный файл. Чтобы запустить его явно:

```powershell
$env:LAPTOPINSPECTOR_CONFIRM_DISK_BENCHMARK = '1'
LaptopInspector.exe scan --benchmark --output .reports
```

Без этой переменной disk benchmark будет помечен как skipped.

## Exit codes

| Код | Значение |
|---:|---|
| 0 | Успех. |
| 1 | Успех с частичными ошибками сборщиков. |
| 2 | Найдены Critical-проблемы. |
| 3 | Фатальная ошибка. |
| 64 | Ошибка аргументов CLI. |

## Сравнение отчётов

```powershell
LaptopInspector.exe diff old.json new.json --out diff.html
```

Diff показывает изменения оценок, батареи, SSD wear/write counters, программ, устройств, стабильности, настроек безопасности, статусов сборщиков и rule findings.
