# Тестирование

## Managed tests

```powershell
dotnet test LaptopInspector.sln -c Release --collect:"XPlat Code Coverage"
```

Покрываются:

- планировщик сборщиков;
- приватность и redaction;
- Health Rules Engine;
- ReportDataExplorer.

## Native tests

```powershell
cmake -S src/native -B artifacts/native -DLI_BUILD_NATIVE_TESTS=ON
cmake --build artifacts/native --config Release
ctest --test-dir artifacts/native --output-on-failure -C Release
```

Native tests используют фикстуры:

```text
tests/fixtures/smbios_minimal.bin
tests/fixtures/edid_with_cta.bin
tests/fixtures/acpi_tables.bin
tests/fixtures/ata_smart.bin
tests/fixtures/nvme_health.bin
```

Проверяются:

- CRC32;
- SHA-256;
- SMBIOS parser;
- EDID/CTA parser;
- ACPI parser;
- ATA SMART parser;
- NVMe Health parser;
- CPUID JSON output / unsupported status.

## Sample reports

Синтетические sample reports генерируются командой:

```powershell
python tools/generate_sample_report.py
```

Выход:

```text
sample_reports/LaptopInspector_SampleLaptop_20261125_120000.html
sample_reports/LaptopInspector_SampleLaptop_20261125_120000.json
sample_reports/LaptopInspector_SampleLaptop_20261125_120000.txt
sample_reports/LaptopInspector_SampleLaptop_20261125_120000.md
sample_reports/LaptopInspector_SampleLaptop_20261125_120000_csv.zip
```

## Manual Windows smoke test

```powershell
LaptopInspector.exe selftest
LaptopInspector.exe list-collectors
LaptopInspector.exe quick --output .reports --format html,json,txt --anonymize
LaptopInspector.exe scan --output .reports --format html,json,txt,md,csv --days 7 --anonymize
```

## Optional tests

```powershell
LaptopInspector.exe scan --energy --output .reports
LaptopInspector.exe scan --deep-disk --output .reports
LaptopInspector.exe scan --benchmark --output .reports
$env:LAPTOPINSPECTOR_CONFIRM_DISK_BENCHMARK = '1'
LaptopInspector.exe scan --benchmark --output .reports
```

## JSON Schema validation

Any draft 2020-12 validator can be used with:

```text
schema/report.schema.json
```

CI can validate sample reports and real integration outputs against this schema.
