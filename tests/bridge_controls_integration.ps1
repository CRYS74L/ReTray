param([switch]$SkipBuild,[switch]$Orphan)
$ErrorActionPreference='Stop'
$productRoot=Split-Path -Parent $PSScriptRoot
if(!$SkipBuild){foreach($target in @('Source','BridgeControls','Bridge')){& (Join-Path $productRoot 'build.ps1') -Target $target;if($LASTEXITCODE -ne 0){throw "Build $target failed"}}}
$source=Start-Process -FilePath (Join-Path $productRoot 'build\TrayAssistant.TestSource.exe') -WindowStyle Hidden -PassThru
$map=$null;$view=$null;$hwnd=0
try {
 $deadline=[DateTime]::UtcNow.AddSeconds(3)
 while([DateTime]::UtcNow -lt $deadline){try{$map=[System.IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting(('Local\TrayAssistant.TestSource.'+$source.Id));break}catch{Start-Sleep -Milliseconds 50}}
 if(!$map){throw 'Source status did not initialize'}
 $view=$map.CreateViewAccessor(0,0,[System.IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
 $hwnd=$view.ReadUInt64(8)
 Start-Sleep -Milliseconds 150
 $extra=@();if($Orphan){$extra+='--orphan'}
 & (Join-Path $productRoot 'build\BridgeControls.exe') (Join-Path $productRoot 'native\platform\supported-adapters.json') (Join-Path $productRoot 'build\TrayAssistant.ShellBridge.dll') $source.Id @extra
 if($LASTEXITCODE -ne 0){throw 'Controlled bridge acceptance failed'}
 if($Orphan){Start-Sleep -Milliseconds 300;$shell=Get-Process -Name explorer | Select-Object -First 1;if(@($shell.Modules|Where-Object ModuleName -eq 'TrayAssistant.ShellBridge.dll').Count){throw 'Orphan bridge still loaded'};& (Join-Path $productRoot 'build\TrayAssistant.Probe.exe') (Join-Path $productRoot 'native\platform\supported-adapters.json') (Join-Path $productRoot 'artifacts\orphan-snapshot.json');$snap=Get-Content -Raw -LiteralPath (Join-Path $productRoot 'artifacts\orphan-snapshot.json')|ConvertFrom-Json;$item=$snap.icons|Where-Object {$_.pid -eq $source.Id -and !$_.hasGuid};if($item.placement -eq 2 -or $item.tip -eq 'Custom test tooltip'){throw 'Orphan policy restoration failed'};Write-Host 'PASS owner-death restoration and native DLL unload'}
} finally {
 if($hwnd){
  $stopDefinition=@'
using System;using System.Runtime.InteropServices;
public static class SourceStopMessage {[DllImport("user32.dll")]public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);}
'@
  if(!('SourceStopMessage' -as [type])){Add-Type -TypeDefinition $stopDefinition}
  [void][SourceStopMessage]::PostMessage([IntPtr]::new([long]$hwnd),0x800c,[IntPtr]::Zero,[IntPtr]::Zero)
 }
 if(!$source.WaitForExit(2000)){Stop-Process -Id $source.Id -Force}
 if($view){$view.Dispose()};if($map){$map.Dispose()}
}
