# Usage (as administrator): powershell -ExecutionPolicy Bypass -File .\Allow-Firewall.ps1
#Requires -RunAsAdministrator
$exe = Join-Path $PSScriptRoot "LiveDLSSServer\LiveDLSSServer.exe"

Remove-NetFirewallRule -DisplayName "LiveDLSS*" -ErrorAction SilentlyContinue
New-NetFirewallRule -DisplayName "LiveDLSS TCP 47800" -Direction Inbound -Protocol TCP -LocalPort 47800 `
  -Program $exe -Action Allow -Profile Private | Out-Null
New-NetFirewallRule -DisplayName "LiveDLSS mDNS 5353" -Direction Inbound -Protocol UDP -LocalPort 5353 `
  -Program $exe -Action Allow -Profile Private | Out-Null

Write-Host "Done. Make sure the Wi-Fi/Ethernet network is set to Private (Settings → Network)." -ForegroundColor Green
Get-NetConnectionProfile | Format-Table Name, InterfaceAlias, NetworkCategory
