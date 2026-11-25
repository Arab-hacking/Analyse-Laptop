# Health Rules Engine

Этап 5 добавляет расширяемый движок правил здоровья. Правила находятся в `src/LaptopInspector.Core/rules.json` и копируются рядом с CLI как `rules.json`. Если внешний файл отсутствует или повреждён, используется встроенный каталог `HealthRuleCatalog.DefaultRulesJson`.

## Модель правила

```json
{
  "id": "STO-001",
  "enabled": true,
  "severity": "Critical",
  "scoreBucket": "hardware",
  "category": "Storage",
  "title": "NVMe Critical Warning не равен нулю",
  "details": "NVMe сообщает Critical Warning: {value}.",
  "recommendation": "Срочно сделайте резервную копию.",
  "condition": {
    "source": "data",
    "path": ["native.storage", "physicalDrives", "*", "nvmeHealth", "criticalWarning"],
    "operator": "anyGreaterThan",
    "number": 0
  }
}
```

`path` — массив сегментов JSON; `*` разворачивает массивы и объекты. Ключи с точками, например `native.storage`, являются обычными именами свойств и указываются одним сегментом.

## Операторы

- `exists`
- `isNullOrEmpty`
- `equals`
- `notEquals`
- `greaterThan`
- `anyGreaterThan`
- `anyLessThan`
- `containsText`
- `anyContainsText`
- `anyEquals`
- `anyIn`
- `countEquals`
- `countGreaterOrEqual`
- `countLessThan`
- `countTextGreaterOrEqual`
- `anyEqualsWithSiblingGreaterThan`
- `statusIn` для `source = collector`

## Скоринг

По умолчанию penalties по severity:

| Severity | Penalty |
|---|---:|
| Info | 0 |
| Low | 2 |
| Medium | 7 |
| High | 15 |
| Critical | 30 |

Bucket score:

```text
bucketScore = clamp(100 - penalties, 0, 100)
```

Итог:

```text
overall = hardware*0.45 + security*0.35 + stability*0.20
```

Веса задаются в `scoreWeights` в `rules.json`.

## Текущее покрытие

Каталог содержит 71 правило по категориям:

- Collection;
- Battery;
- Storage;
- Thermal;
- Memory;
- Devices;
- Display;
- Network;
- Security;
- OperatingSystem;
- Software;
- Stability;
- Performance;
- Benchmarks;
- Optional kernel driver probes;
- Virtualization;
- Firmware;
- Coverage.

Правила не собирают данные сами: они оценивают JSON, сформированный сборщиками Этапов 3–4. Если сборщик пропущен или источник недоступен, соответствующие правила либо не срабатывают, либо добавляют informational finding через правила `COL-*`/`COV-*`.
