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
 $busyPipe=[IO.Pipes.NamedPipeClientStream]::new('.',('TrayAssistant.v1.'+(Get-Process -Id $PID).SessionId),[IO.Pipes.PipeDirection]::InOut)
 try{
  $busyPipe.Connect(2000);$busyWriter=[IO.StreamWriter]::new($busyPipe,[Text.UTF8Encoding]::new($false),4096,$true);$busyWriter.AutoFlush=$true;$busyReader=[IO.StreamReader]::new($busyPipe,[Text.UTF8Encoding]::new($false),$false,4096,$true)
  $busyWriter.WriteLine('{"schema":1,"requestId":"busy","method":"protection.set","data":{"newPassword":"integration-test-only","currentPassword":"integration-test-only","settings":true,"menus":true,"interactions":false}}')
  $busyRead=$busyReader.ReadLineAsync();$busyLimit=[DateTime]::UtcNow.AddSeconds(2);do{$working=[AgentTestNative]::GetProp($window,'TrayAssistant.Busy') -ne [IntPtr]::Zero;if(!$working){Start-Sleep -Milliseconds 1}}while(!$working -and !$busyRead.IsCompleted -and [DateTime]::UtcNow -lt $busyLimit)
  if(!$working){throw 'Did not observe active worker for busy-session test'}
  $busyWatch=[Diagnostics.Stopwatch]::StartNew();$busyReply=[AgentTestNative]::SendMessage($window,0x11,[UIntPtr]::Zero,[IntPtr]::Zero);$busyWatch.Stop();[void][AgentTestNative]::SendMessage($window,0x16,[UIntPtr]::Zero,[IntPtr]::Zero)
  if($busyReply -ne [IntPtr]1 -or $busyWatch.ElapsedMilliseconds -gt 50){throw 'Busy rule worker delayed session query'}
  if(!$busyRead.Wait(5000) -or !(($busyRead.Result|ConvertFrom-Json).success)){throw 'Password digest setup failed'}
 }finally{if($busyWriter){$busyWriter.Dispose()};if($busyReader){$busyReader.Dispose()};$busyPipe.Dispose()}
 if([IO.File]::ReadAllText((Join-Path $dataDir 'settings.json')).Contains('integration-test-only')){throw 'Plain secret persisted'}
 if(!(Invoke-Agent 'protection.lock').success){throw 'Lock failed'}
 if((Invoke-Agent 'settings.read').success){throw 'Settings protection bypassed'}
 if(!(Invoke-Agent 'protection.unlock' @{password='integration-test-only'}).success){throw 'Unlock failed'}
 $stopwatch=[Diagnostics.Stopwatch]::StartNew();$stop=Invoke-Agent 'agent.stop';$agent.WaitForExit(1000)|Out-Null;$stopwatch.Stop();if(!$agent.HasExited -or $stopwatch.ElapsedMilliseconds -gt 1000){throw 'Agent exit missed bound'}
 "PASS real catalog, own icon, protection digest/lock/unlock, QUERY/cancel $($watch.ElapsedMilliseconds)ms, busy worker QUERY $($busyWatch.ElapsedMilliseconds)ms, stop $($stopwatch.ElapsedMilliseconds)ms; real shutdown remains untested"
}finally{if(!$agent.HasExited){try{Invoke-Agent 'agent.stop'|Out-Null;$agent.WaitForExit(1000)|Out-Null}catch{}}}
