# Build the in-process D3D12 / NGX NR bridge. Supply the NVIDIA model DLL separately.
param(
  [Parameter(Mandatory = $true)][string]$NgxSdk,
  [string]$ModelDll
)
$ErrorActionPreference = "Stop"
if (-not (Test-Path (Join-Path $NgxSdk "include\nvsdk_ngx_params.h"))) {
  throw "NgxSdk must point to the NVIDIA DLSS SDK root containing include\nvsdk_ngx_params.h."
}
$packagedSource = Join-Path $PSScriptRoot "neural-rendering-bridge"
if (Test-Path $packagedSource) {
  $source = $packagedSource
  $destination = Join-Path $PSScriptRoot "LiveDLSSServer"
} else {
  $source = Join-Path $PSScriptRoot "..\..\apps\windows\native\LiveDLSS.NeuralRendering"
  $destination = Join-Path $PSScriptRoot "..\..\apps\windows\runtimes\neural-rendering"
}
$build = Join-Path $env:TEMP "livedlss-neural-rendering-build"
cmake -S $source -B $build -A x64 "-DNGX_SDK_DIR=$NgxSdk" "-DLIVEDLSS_RUNTIME_DIR=$destination"
if ($LASTEXITCODE -ne 0) { throw "NR bridge configure failed ($LASTEXITCODE)." }
cmake --build $build --config Release --target install
if ($LASTEXITCODE -ne 0) { throw "NR bridge build failed ($LASTEXITCODE)." }
if ($ModelDll) {
  Copy-Item -LiteralPath $ModelDll -Destination (Join-Path $destination "nvngx_dlssnr.dll") -Force
}
Write-Host "NR bridge installed in $destination. Put nvngx_dlssnr.dll beside it and the server exe."
