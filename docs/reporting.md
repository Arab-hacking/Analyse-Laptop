# Reporting Engine

Этап 6 расширяет генераторы отчётов LaptopInspector. Все основные форматы работают офлайн и не используют внешние ресурсы.

## HTML

HTML — основной формат отчёта. Один самодостаточный файл содержит:

- sticky-навигацию;
- Dashboard с SVG score gauges;
- карточки ключевых компонентов: CPU, RAM, Storage, Battery, GPU/Display, OS, Network, Security;
- severity-фильтры для findings;
- полнотекстовый client-side поиск;
- сортируемую таблицу сборщиков;
- секции данных по каждому collector ID;
- raw JSON в `<details>`;
- светлую/тёмную тему;
- print-friendly CSS.

## JSON

JSON остаётся канонической машинной моделью. `schema/report.schema.json` обновлён до draft 2020-12 модели с описанием:

- `metadata`;
- `summary`;
- `collectors`;
- `findings`;
- `data.health.rules`.

`JsonReportWriter` добавляет `reportHashSha256`, рассчитанный по JSON без самого hash-поля.

## TXT

TXT-отчёт рассчитан на пересылку в чат/форум. Он содержит:

- метаданные;
- оценки и penalties;
- severity summary;
- полный список findings;
- журнал сборщиков;
- инвентарь top-level `data` sections;
- формулу оценки и SHA-256.

## Markdown

Markdown подходит для GitHub/wiki. Он содержит те же ключевые блоки, что TXT, но в таблицах Markdown.

## CSV ZIP

CSV-архив содержит:

- `collectors.csv`;
- `findings.csv`;
- `scores.csv`;
- `data_sections.csv`;
- `software_programs.csv`;
- `software_services.csv`;
- `software_processes.csv`;
- `devices.csv`;
- `usb_devices.csv`;
- `storage_physical_drives.csv`;
- `battery.csv`;
- `events_bugchecks.csv`;
- `events_whea.csv`;
- `events_disk.csv`;
- `network_adapters.csv`.

CSV rows формируются через flatten scalar-полей JSON-объектов. Глубоко вложенные массивы сохраняются в JSON-отчёте.

## Diff

`LaptopInspector diff old.json new.json --out diff.html` сравнивает:

- metadata;
- hardware/security/stability/overall scores;
- battery wear;
- NVMe Percentage Used;
- NVMe Data Units Written;
- NVMe media errors;
- установленные программы;
- устройства/драйверы по instance ID;
- группы событий стабильности;
- ключевые настройки безопасности;
- статусы сборщиков;
- rule findings по `ruleId`.
