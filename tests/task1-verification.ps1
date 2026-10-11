$ErrorActionPreference='Stop'
$productRoot=Split-Path -Parent $PSScriptRoot
& (Join-Path $productRoot 'build.ps1') -Target CoreTests
if($LASTEXITCODE -ne 0){throw 'Core tests failed'}
& (Join-Path $PSScriptRoot 'native_snapshot_integration.ps1')
& (Join-Path $productRoot 'build.ps1') -Target BridgeTest
if($LASTEXITCODE -ne 0){throw 'Bridge client failed to build'}
& (Join-Path $productRoot 'build.ps1') -Target Bridge
if($LASTEXITCODE -ne 0){throw 'Bridge failed to build'}
$before=@((Get-Process -Name explorer).Id)
& (Join-Path $productRoot 'build\BridgeSmoke.exe') (Join-Path $productRoot 'native\platform\supported-adapters.json') (Join-Path $productRoot 'build\TrayAssistant.ShellBridge.dll')
if($LASTEXITCODE -ne 0){throw 'Current-frame bridge test failed'}
Start-Sleep -Milliseconds 150
$shell=Get-Process -Name explorer | Select-Object -First 1
if($shell.Id -notin $before){throw 'Explorer restarted during bridge test'}
if(@($shell.Modules|Where-Object ModuleName -eq 'TrayAssistant.ShellBridge.dll').Count){throw 'Bridge DLL did not unload after callback detachment'}
Write-Host 'PASS task1 native inventory, identity/raster/protocol/session contracts, actual current frames, safe DLL unload and unchanged Explorer PID'
