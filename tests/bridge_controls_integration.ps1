param([switch]$SkipBuild)
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
 & (Join-Path $productRoot 'build\BridgeControls.exe') (Join-Path $productRoot 'native\platform\supported-adapters.json') (Join-Path $productRoot 'build\TrayAssistant.ShellBridge.dll') $source.Id
 if($LASTEXITCODE -ne 0){throw 'Controlled bridge acceptance failed'}
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
