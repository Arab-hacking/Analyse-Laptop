#!/usr/bin/env pwsh
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$Runtime = 'win-x64',

    [switch]$SkipTests,

    [switch]$SkipNative,

    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$solution = Join-Path $root 'LaptopInspector.sln'
$artifacts = Join-Path $root 'artifacts'
$publishDir = Join-Path $artifacts "publish/$Runtime"
$nativeLibraryPath = $null

# MSVC developer shell (ilammy/msvc-dev-cmd) и похожие окружения экспортируют Platform=x64.
# MSBuild превращает переменные окружения в глобальные свойства и ищет конфигурацию решения
# "Debug|x64", которой в LaptopInspector.sln нет (error MSB4126). Убираем унаследованное значение.
if (-not [string]::IsNullOrEmpty($env:Platform)) {
    Write-Host "Ignoring inherited Platform=$env:Platform (solution uses 'Any CPU')." -ForegroundColor DarkYellow
    Remove-Item -Path Env:Platform
}

# Явный массив аргументов вместо ValueFromRemainingArguments: в advanced-функциях
# параметры вида -S, --build или /p:X=Y не гарантированно попадают в остаток.
function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FilePath,

        [string[]]$Arguments = @()
    )

    Write-Verbose ("Running: {0} {1}" -f $FilePath, ($Arguments -join ' '))
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command '$FilePath $($Arguments -join ' ')' failed with exit code $LASTEXITCODE."
    }
}

function Find-NativeLibrary {
    param([Parameter(Mandatory = $true)][string]$BuildDirectory)

    $names = @('LaptopInspector.Native.dll', 'LaptopInspector.Native.so', 'LaptopInspector.Native.dylib')
    return Get-ChildItem -Path $BuildDirectory -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $names -contains $_.Name } |
        Select-Object -First 1
}

Write-Host '== LaptopInspector build =='
Write-Host "Root: $root"
Write-Host "Configuration: $Configuration"

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null

Invoke-Checked -FilePath 'dotnet' -Arguments @('restore', $solution)
Invoke-Checked -FilePath 'dotnet' -Arguments @('build', $solution, '--configuration', $Configuration, '--no-restore')

if (-not $SkipTests) {
    Invoke-Checked -FilePath 'dotnet' -Arguments @(
        'test', $solution,
        '--configuration', $Configuration,
        '--no-build',
        '--logger', 'trx;LogFileName=LaptopInspector.Tests.trx'
    )
}

if (-not $SkipNative) {
    $cmake = Get-Command cmake -ErrorAction SilentlyContinue
    if ($null -eq $cmake) {
        Write-Warning 'CMake was not found. Native projects were skipped.'
    }
    else {
        $nativeSource = Join-Path $root 'src/native'
        $nativeBuild = Join-Path $artifacts 'native'
        $nativeTests = if ($SkipTests) { 'OFF' } else { 'ON' }
        Invoke-Checked -FilePath 'cmake' -Arguments @(
            '-S', $nativeSource,
            '-B', $nativeBuild,
            "-DCMAKE_BUILD_TYPE=$Configuration",
            "-DLI_BUILD_NATIVE_TESTS=$nativeTests"
        )
        Invoke-Checked -FilePath 'cmake' -Arguments @('--build', $nativeBuild, '--config', $Configuration)
        if (-not $SkipTests) {
            Invoke-Checked -FilePath 'ctest' -Arguments @('--test-dir', $nativeBuild, '--output-on-failure', '-C', $Configuration)
        }

        $nativeLibrary = Find-NativeLibrary -BuildDirectory $nativeBuild
        if ($null -ne $nativeLibrary) {
            $nativeLibraryPath = $nativeLibrary.FullName
            $cliBin = Join-Path $root "src/LaptopInspector.Cli/bin/$Configuration/net8.0"
            New-Item -ItemType Directory -Force -Path $cliBin | Out-Null
            Copy-Item -Path $nativeLibraryPath -Destination $cliBin -Force
            Write-Host "Native library copied to: $cliBin"
        }
        else {
            Write-Warning 'Native library was not found after CMake build.'
        }
    }
}

if ($Publish) {
    $cliProject = Join-Path $root 'src/LaptopInspector.Cli/LaptopInspector.Cli.csproj'
    Invoke-Checked -FilePath 'dotnet' -Arguments @(
        'publish', $cliProject,
        '--configuration', $Configuration,
        '--runtime', $Runtime,
        '--self-contained', 'true',
        '--output', $publishDir,
        '/p:PublishSingleFile=true',
        '/p:IncludeNativeLibrariesForSelfExtract=true'
    )
    if ($null -ne $nativeLibraryPath) {
        Copy-Item -Path $nativeLibraryPath -Destination $publishDir -Force
    }

    Write-Host "Publish ready: $publishDir"
}

Write-Host 'Build completed successfully.'
