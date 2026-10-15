param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
$product=Split-Path $PSScriptRoot -Parent
if(!$SkipBuild){ & (Join-Path $product 'build.ps1') -Target Agent; if($LASTEXITCODE -ne 0){throw 'Agent build failed'} }
Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class AgentTestNative {
[DllImport("user32.dll",EntryPoint="FindWindowW",CharSet=CharSet.Unicode)]public static extern IntPtr FindWindow(string c,IntPtr t);
[DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr h,uint m,UIntPtr w,IntPtr l);
[DllImport("user32.dll",EntryPoint="GetPropW",CharSet=CharSet.Unicode)]public static extern IntPtr GetProp(IntPtr h,string n);
}
'@
function Invoke-Agent($method,$data=@{}) {
 $session=(Get-Process -Id $PID).SessionId
 $pipe=[IO.Pipes.NamedPipeClientStream]::new('.',"ReTray.v1.$session",[IO.Pipes.PipeDirection]::InOut)
 try{$pipe.Connect(3000);$utf8=[Text.UTF8Encoding]::new($false);$writer=[IO.StreamWriter]::new($pipe,$utf8,4096,$true);$writer.AutoFlush=$true;$reader=[IO.StreamReader]::new($pipe,$utf8,$false,4096,$true)
 $writer.WriteLine((@{schema=1;requestId='test';method=$method;data=$data}|ConvertTo-Json -Depth 12 -Compress))
 $task=$reader.ReadLineAsync();if(!$task.Wait(5000)){throw 'IPC response timeout'};return $task.Result|ConvertFrom-Json -Depth 30
 }finally{if($writer){$writer.Dispose()};if($reader){$reader.Dispose()};$pipe.Dispose()}
}
$dataDir=Join-Path $product ('artifacts\agent-integration-'+[guid]::NewGuid().ToString('N'))
if(Get-Process ReTray.Agent -ErrorAction SilentlyContinue){throw 'Stop the running assistant before starting isolated agent acceptance'}
$agent=Start-Process -FilePath (Join-Path $product 'build\ReTray.Agent.exe') -ArgumentList @('--background','--data',('"'+$dataDir+'"')) -WindowStyle Hidden -PassThru
try{
 $limit=[DateTime]::UtcNow.AddSeconds(8);do{try{$status=Invoke-Agent 'status.read';if($status.success -and $status.data.ready){break};Start-Sleep -Milliseconds 100}catch{Start-Sleep -Milliseconds 200}}while([DateTime]::UtcNow -lt $limit)
 if(!$status.success -or !$status.data.ready){throw 'Agent did not attach existing tray inventory'}
 $catalog=Invoke-Agent 'catalog.read';if(!$catalog.success -or $catalog.data.icons.Count -lt 1){throw 'Empty real catalog'}
 $own=Invoke-Agent 'ownIcon.apply' @{visible=$false;asset='';tip='test'};if(!$own.success -or !$own.saved){throw 'Own icon policy not saved/applied'}
 $window=[AgentTestNative]::FindWindow('ReTray.Agent',[IntPtr]::Zero);if($window -eq [IntPtr]::Zero){throw 'Missing session window'}
 $watch=[Diagnostics.Stopwatch]::StartNew();$reply=[AgentTestNative]::SendMessage($window,0x11,[UIntPtr]::Zero,[IntPtr]::Zero);$watch.Stop();if($reply -ne [IntPtr]1 -or $watch.ElapsedMilliseconds -gt 50){throw 'Session query refused/slow'}
 [void][AgentTestNative]::SendMessage($window,0x16,[UIntPtr]::Zero,[IntPtr]::Zero)
 if(!(Invoke-Agent 'status.read').data.ready){throw 'Cancelled session did not return ready'}
 $before=Invoke-Agent 'settings.read'
 foreach($request in @(@{method='settings.apply';data=@{autoSort='bad'}},@{method='ownIcon.apply';data=@{visible=$false;asset='';tip='test';showInList='bad'}})) {
   $invalid=Invoke-Agent $request.method $request.data
  if($invalid.success -or $invalid.saved){throw 'Invalid settings accepted'}
 }
 $after=Invoke-Agent 'settings.read'
 if(($before.data|ConvertTo-Json -Depth 30 -Compress) -ne ($after.data|ConvertTo-Json -Depth 30 -Compress)){throw 'Rejected settings changed persisted configuration'}
 Start-Sleep -Milliseconds 1200
 if(!(Invoke-Agent 'status.read').data.ready){throw 'Agent failed after rejected configuration'}
 $stopwatch=[Diagnostics.Stopwatch]::StartNew();$stop=Invoke-Agent 'agent.stop';$agent.WaitForExit(1000)|Out-Null;$stopwatch.Stop();if(!$agent.HasExited -or $stopwatch.ElapsedMilliseconds -gt 1000){throw 'Agent exit missed bound'}
 "PASS real catalog, own icon, invalid config rejection and unchanged settings, QUERY/cancel $($watch.ElapsedMilliseconds)ms, stop $($stopwatch.ElapsedMilliseconds)ms; real shutdown remains untested"
}finally{if(!$agent.HasExited){try{Invoke-Agent 'agent.stop'|Out-Null;$agent.WaitForExit(1000)|Out-Null}catch{}}}



exit 0
