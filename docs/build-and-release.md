# Сборка и выпуск

## Требования

- Windows 10/11 x64 для полного runtime-тестирования;
- .NET SDK 8.0 или новее;
- CMake 3.22+;
- MSVC v143+ / Visual Studio Build Tools 2022;
- PowerShell 7+ рекомендуется для `build.ps1`.

## Полная сборка

```powershell
./build.ps1
```

Скрипт выполняет:

1. `dotnet restore`;
2. `dotnet build`;
3. `dotnet test`;
4. CMake configure/build native layer;
5. `ctest` для native tests;
6. копирование `LaptopInspector.Native.dll` рядом с CLI.

## Publish single-file

```powershell
./build.ps1 -Configuration Release -Runtime win-x64 -Publish
```

Результат:

```text
artifacts/publish/win-x64
```

## Native-only build

```powershell
cmake -S src/native -B artifacts/native -DLI_BUILD_NATIVE_TESTS=ON
cmake --build artifacts/native --config Release
ctest --test-dir artifacts/native --output-on-failure -C Release
```

## ARM64

```powershell
./build.ps1 -Runtime win-arm64 -Publish
```

Native ARM64 requires an ARM64-capable MSVC toolchain. Low-level Windows APIs are guarded by normal Win32 headers; feature availability still depends on OS and driver support.

## Подпись бинарей

Репозиторий не содержит сертификат. Рекомендуемый процесс выпуска:

1. собрать Release artifacts;
2. просканировать антивирусом/Defender;
3. подписать `LaptopInspector.exe` и `LaptopInspector.Native.dll` корпоративным Authenticode сертификатом;
4. проверить подпись:

```powershell
Get-AuthenticodeSignature .\LaptopInspector.exe
Get-AuthenticodeSignature .\LaptopInspector.Native.dll
```

## Release checklist

- [ ] `./build.ps1 -Configuration Release` проходит без ошибок;
- [ ] `./build.ps1 -Configuration Release -Runtime win-x64 -Publish` формирует artifacts;
- [ ] `LaptopInspector.exe selftest` возвращает 0;
- [ ] `LaptopInspector.exe quick --output .reports --format html,json,txt --anonymize` формирует отчёты;
- [ ] JSON проходит `schema/report.schema.json`;
- [ ] HTML открывается offline в Edge/Chrome/Firefox;
- [ ] `LaptopInspector.exe diff old.json new.json --out diff.html` работает на двух sample/real отчётах;
- [ ] binaries подписаны, если это релиз для внешнего распространения.
