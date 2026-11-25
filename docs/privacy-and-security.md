# Приватность и безопасность

LaptopInspector проектируется как read-only диагностический инструмент.

## Что не собирается никогда

- пароли;
- Wi‑Fi ключи;
- BitLocker recovery keys;
- cookies;
- история браузеров;
- содержимое пользовательских файлов;
- токены и Authorization headers;
- приватные ключи и сертификаты пользователя.

## Read-only политика

Допустимая запись:

- отчёты в каталог `--output`;
- лог `LaptopInspector.log` рядом с отчётом;
- sample/generated artifacts при разработке;
- временный disk benchmark file только при `--benchmark` + `LAPTOPINSPECTOR_CONFIRM_DISK_BENCHMARK=1`.

Недопустимо:

- изменение реестра;
- изменение настроек ОС;
- установка/загрузка драйверов;
- изменение firmware/UEFI variables;
- изменение сетевой конфигурации;
- запуск destructive/DoS тестов.

## Анонимизация

Флаг:

```powershell
--anonymize
```

Маскирует serial/UUID/MAC/BSSID/SSID/user/instance IDs/device paths/product keys детерминированным hash внутри отчёта. Одинаковые исходные значения получают одинаковую маску в пределах одного запуска, что сохраняет полезность diff без раскрытия реальных идентификаторов.

## Redaction командных строк

Командные строки процессов проходят regex-redaction для типичных секретов:

- `password=`;
- `token=`;
- `secret=`;
- `api-key=`;
- `--key`;
- `Authorization:`;
- `Bearer ...`.

## Опциональные kernel driver probes

`optional.driverProbe` только проверяет наличие известных сервисов/устройств через CIM. Он не открывает device handle, не выполняет IOCTL и не устанавливает драйверы.

## Network policy

По умолчанию сетевые запросы не выполняются. Флаг `--online` зарезервирован для будущей проверки версий BIOS/драйверов и должен использоваться только явно.
