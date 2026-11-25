# Security policy

## Supported usage

LaptopInspector is intended for defensive diagnostics, asset inventory, support and authorized troubleshooting of Windows laptops.

## Reporting vulnerabilities

Please report vulnerabilities privately to the repository maintainers. Include:

- affected version/commit;
- reproduction steps;
- expected vs actual behavior;
- logs or sample reports with secrets removed;
- whether administrator privileges are required.

## Security design

- read-only collectors by default;
- no credential collection;
- no Wi‑Fi key collection;
- no BitLocker recovery key collection;
- no browser history/cookie/password collection;
- anonymization mode for identifiers;
- native ABI catches exceptions and returns error JSON;
- optional disk benchmark requires explicit environment confirmation.

## Out of scope

- requests to add offensive capabilities;
- stealth, evasion or persistence;
- credential extraction;
- destructive stress tests or DoS;
- automatic driver installation/loading.
