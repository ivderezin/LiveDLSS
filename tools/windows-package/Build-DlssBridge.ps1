# Usage: powershell -ExecutionPolicy Bypass -File .\Build-DlssBridge.ps1 -StreamlineSdk C:\sdk\streamline
param(
  [Parameter(Mandatory = $true)][string]$StreamlineSdk
)
$ErrorActionPreference = "Stop"

if (-not (Test-Path (Join-Path $StreamlineSdk "include\sl.h"))) {
  throw "$StreamlineSdk\include\sl.h not found. Pass the root of the extracted Streamline SDK."
}
if (-not (Get-Command cmake -ErrorAction SilentlyContinue)) {
  throw "CMake not found in PATH. Install it: winget install Kitware.CMake"
}

$src = Join-Path $PSScriptRoot "dlss-bridge"
$build = Join-Path $env:TEMP "livedlss-dlss-build"
$dest = Join-Path $PSScriptRoot "LiveDLSSServer"

cmake -S $src -B $build -A x64 "-DSTREAMLINE_SDK_DIR=$StreamlineSdk" "-DLIVEDLSS_RUNTIME_DIR=$dest"
cmake --build $build --config Release --target install

Write-Host "`nDone: LiveDLSS.Dlss.dll and Streamline DLLs copied to $dest" -ForegroundColor Green
Write-Host "Run Run-LiveDLSS.bat; the inspector should show 'Upscaler: NVIDIA DLSS (Streamline)'."
