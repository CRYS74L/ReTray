param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
$product=Split-Path $PSScriptRoot -Parent
if(!$SkipBuild){ & (Join-Path $product 'build.ps1') -Target Agent; if($LASTEXITCODE -ne 0){throw 'Agent build failed'} }
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class AgentTestNative {
[DllImport("user32.dll",EntryPoint="FindWindowW",CharSet=CharSet.Unicode)]public static extern IntPtr FindWindow(string c,IntPtr t);
[DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr h,uint m,UIntPtr w,IntPtr l);
}
'@
function Invoke-Agent($method,$data=@{}) {
 $session=(Get-Process -Id $PID).SessionId
 $pipe=[IO.Pipes.NamedPipeClientStream]::new('.',"TrayAssistant.v1.$session",[IO.Pipes.PipeDirection]::InOut)
 try{$pipe.Connect(3000);$utf8=[Text.UTF8Encoding]::new($false);$writer=[IO.StreamWriter]::new($pipe,$utf8,4096,$true);$writer.AutoFlush=$true;$reader=[IO.StreamReader]::new($pipe,$utf8,$false,4096,$true)
 $writer.WriteLine((@{schema=1;requestId='test';method=$method;data=$data}|ConvertTo-Json -Depth 12 -Compress))
 $task=$reader.ReadLineAsync();if(!$task.Wait(5000)){throw 'IPC response timeout'};return $task.Result|ConvertFrom-Json -Depth 30
 }finally{if($writer){$writer.Dispose()};if($reader){$reader.Dispose()};$pipe.Dispose()}
}
$dataDir=Join-Path $product 'artifacts\agent-integration'
$agent=Start-Process -FilePath (Join-Path $product 'build\TrayAssistant.Agent.exe') -ArgumentList @('--background','--data',('"'+$dataDir+'"')) -WindowStyle Hidden -PassThru
try{
 $limit=[DateTime]::UtcNow.AddSeconds(8);do{try{$status=Invoke-Agent 'status.read';break}catch{Start-Sleep -Milliseconds 200}}while([DateTime]::UtcNow -lt $limit)
 if(!$status.success -or !$status.data.ready){throw 'Agent did not attach existing tray inventory'}
 $catalog=Invoke-Agent 'catalog.read';if(!$catalog.success -or $catalog.data.icons.Count -lt 1){throw 'Empty real catalog'}
 $own=Invoke-Agent 'ownIcon.apply' @{visible=$false;asset='';tip='test'};if(!$own.success -or !$own.saved){throw 'Own icon policy not saved/applied'}
 $window=[AgentTestNative]::FindWindow('TrayAssistant.Agent',[IntPtr]::Zero);if($window -eq [IntPtr]::Zero){throw 'Missing session window'}
 $watch=[Diagnostics.Stopwatch]::StartNew();$reply=[AgentTestNative]::SendMessage($window,0x11,[UIntPtr]::Zero,[IntPtr]::Zero);$watch.Stop();if($reply -ne [IntPtr]1 -or $watch.ElapsedMilliseconds -gt 50){throw 'Session query refused/slow'}
 [void][AgentTestNative]::SendMessage($window,0x16,[UIntPtr]::Zero,[IntPtr]::Zero)
 if(!(Invoke-Agent 'status.read').data.ready){throw 'Cancelled session did not return ready'}
 $stopwatch=[Diagnostics.Stopwatch]::StartNew();$stop=Invoke-Agent 'agent.stop';$agent.WaitForExit(1000)|Out-Null;$stopwatch.Stop();if(!$agent.HasExited -or $stopwatch.ElapsedMilliseconds -gt 1000){throw 'Agent exit missed bound'}
 "PASS real catalog, own icon saved/applied, synthetic QUERY/cancel lifecycle ($($watch.ElapsedMilliseconds)ms), bounded normal stop ($($stopwatch.ElapsedMilliseconds)ms); real shutdown remains untested"
}finally{if(!$agent.HasExited){try{Invoke-Agent 'agent.stop'|Out-Null;$agent.WaitForExit(1000)|Out-Null}catch{}}}
