# Third-party notices

LaptopInspector uses the following third-party/open-source components through NuGet or platform SDKs.

## NuGet packages

| Package | Purpose | License |
|---|---|---|
| Microsoft.Extensions.DependencyInjection | Dependency injection | MIT |
| Microsoft.Extensions.DependencyInjection.Abstractions | DI abstractions | MIT |
| Microsoft.Extensions.Logging | Logging abstractions/integration | MIT |
| Microsoft.Extensions.Logging.Abstractions | Logging abstractions | MIT |
| Serilog | Structured logging | Apache-2.0 |
| Serilog.Extensions.Logging | Microsoft logging bridge | Apache-2.0 |
| Serilog.Sinks.Console | Console logging sink | Apache-2.0 |
| Serilog.Sinks.File | File logging sink | Apache-2.0 |
| System.Security.Principal.Windows | Windows principal/admin detection | MIT |
| xunit | Tests | Apache-2.0 |
| xunit.runner.visualstudio | Test runner | Apache-2.0 |
| Microsoft.NET.Test.Sdk | Test SDK | MIT |
| coverlet.collector | Coverage collection | MIT |

## Platform SDKs

Windows API headers/libraries are used from the Windows SDK:

- SetupAPI;
- CfgMgr32;
- DXGI;
- WLAN API;
- Battery class IOCTLs;
- Storage IOCTLs;
- TBS API dynamic loading;
- firmware APIs.

These are provided by Microsoft as part of the Windows SDK and are not redistributed as source in this repository.

## Runtime optional DLL probes

LaptopInspector may dynamically check for vendor DLL presence at runtime:

- `nvml.dll`;
- `amdadlx64.dll`;
- `atiadlxx.dll`;
- `atiadlxy.dll`;
- `igcl.dll`;
- `IntelGraphicsControlLib.dll`.

The project does not redistribute these DLLs.
