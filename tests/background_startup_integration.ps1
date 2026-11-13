param([Parameter(Mandatory)][string]$Executable, [switch]$MeasureOnly)
$ErrorActionPreference = 'Stop'
if (Get-Process -Name ReTray -ErrorAction SilentlyContinue) { throw 'Exit ReTray before running this integration check.' }
$Executable = (Resolve-Path -LiteralPath $Executable).Path
$startupTask = @(Get-ScheduledTask | Where-Object {$_.TaskName -like 'ReTray.Logon.*'})
if ($startupTask.Count -eq 1 -and $startupTask[0].Actions[0].Execute -ine $Executable) {
    throw 'Use the registered deployment path so the application cannot redirect its startup task to a test copy.'
}
function Invoke-ReTrayDiagnosticRequest([string]$Method) {
    $sessionId = (Get-Process -Id $PID).SessionId
    $pipe = [IO.Pipes.NamedPipeClientStream]::new('.',"ReTray.v1.$sessionId",[IO.Pipes.PipeDirection]::InOut)
    $writer = $null
    $reader = $null
    try {
        $pipe.Connect(1000)
        $encoding = [Text.UTF8Encoding]::new($false)
        $writer = [IO.StreamWriter]::new($pipe,$encoding,4096,$true)
        $reader = [IO.StreamReader]::new($pipe,$encoding,$false,4096,$true)
        $writer.AutoFlush = $true
        $writer.WriteLine((@{schema=1;requestId='background-startup-test';method=$Method;data=@{}}|ConvertTo-Json -Compress))
        $response = $reader.ReadLineAsync()
        if (!$response.Wait(10000)) { throw 'Runtime request timed out.' }
        $reply = $response.Result | ConvertFrom-Json -Depth 50
        if (!$reply.success) { throw $reply.detail }
        return $reply
    } finally {
        if ($writer) { $writer.Dispose() }
        if ($reader) { $reader.Dispose() }
        $pipe.Dispose()
    }
}
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class BackgroundStartupWindows {
 public sealed class Row {
  public long Handle;
  public string Title="";
  public bool Visible;
 }
 delegate bool Callback(IntPtr hwnd,IntPtr data);
 [DllImport("user32.dll")] static extern bool EnumWindows(Callback callback,IntPtr data);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr hwnd,StringBuilder text,int count);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hwnd);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr word,IntPtr value);
 public static Row[] Read(uint pid) {
  var rows=new List<Row>();
  EnumWindows((hwnd,data)=>{
   uint owner;
   GetWindowThreadProcessId(hwnd,out owner);
   if(owner==pid) {
    var text=new StringBuilder(256);
    GetWindowText(hwnd,text,256);
    rows.Add(new Row {
     Handle=hwnd.ToInt64(),
     Title=text.ToString(),
     Visible=IsWindowVisible(hwnd)
    });
   }
   return true;
  },IntPtr.Zero);
  return rows.ToArray();
 }
}
'@
function Wait-Until([scriptblock]$Condition, [string]$Failure) {
    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do { if (& $Condition) { return }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $deadline)
    throw $Failure
}
$process = $null
try {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $Executable -ArgumentList '--background' -WindowStyle Hidden -PassThru
    Wait-Until {
        if ($process.HasExited) { throw 'ReTray exited during background startup.' }
        try { $script:status = Invoke-ReTrayDiagnosticRequest 'status.read'; return $status.data.ready -and $status.data.connected -and $status.data.pid -eq $process.Id }
        catch [System.Management.Automation.MethodInvocationException] { if ($_.Exception.InnerException -is [TimeoutException]) { return $false }; throw }
    } 'Runtime failed to connect to the Shell.'
    $readyMs = $watch.ElapsedMilliseconds
    Start-Sleep -Seconds 1
    $process.Refresh()
    $windows = @([BackgroundStartupWindows]::Read($process.Id))
    $main = @($windows | Where-Object {$_.Title -eq 'ReTray'})
    $result = [ordered]@{
        version=$status.data.version; readyMs=$readyMs;
        cpuMs=[Math]::Round($process.TotalProcessorTime.TotalMilliseconds);
        privateMB=[Math]::Round($process.PrivateMemorySize64/1MB,2);
        mainWindowCount=$main.Count
    }
    if (!$MeasureOnly) {
        if ($main.Count -ne 1 -or $windows.Title -notcontains 'ReTray.InterfaceHost') {
            # The native runtime's message window also has the ReTray title.
            throw 'Background startup did not retain just the runtime window and lightweight interface host.'
        }
        if (@($windows | Where-Object {$_.Visible}).Count) { throw 'Background startup displayed a window.' }
        [void](Invoke-ReTrayDiagnosticRequest 'assistant.menu')
        Start-Sleep -Milliseconds 700
        if (@([BackgroundStartupWindows]::Read($process.Id) | Where-Object {$_.Title -eq 'ReTray'}).Count -ne 1) {
            throw 'Opening the tray menu constructed the full main window.'
        }
        $result.menuWithoutMainWindow = $true
        $hostWindow = $windows | Where-Object {$_.Title -eq 'ReTray.InterfaceHost'} | Select-Object -First 1
        $exitMessage = [BackgroundStartupWindows]::RegisterWindowMessage('ReTray.ExitInterface.v1')
        if (![BackgroundStartupWindows]::PostMessage([IntPtr]::new($hostWindow.Handle),$exitMessage,[IntPtr]::Zero,[IntPtr]::Zero)) { throw 'Windowless exit message failed.' }
        if (!$process.WaitForExit(10000)) { throw 'Exit before opening the main window did not finish.' }
        if ($process.ExitCode -ne 0) { throw 'Windowless exit failed.' }
        $result.windowlessExit = $true
        $process.Dispose()
        $process = Start-Process -FilePath $Executable -ArgumentList '--background' -WindowStyle Hidden -PassThru
        Wait-Until {
            try { $script:status = Invoke-ReTrayDiagnosticRequest 'status.read'; return $status.data.ready -and $status.data.connected }
            catch [System.Management.Automation.MethodInvocationException] { if ($_.Exception.InnerException -is [TimeoutException]) { return $false }; throw }
        } 'Second startup failed.'
        [void](Invoke-ReTrayDiagnosticRequest 'internal.openUi')
        Wait-Until { @([BackgroundStartupWindows]::Read($process.Id) | Where-Object {$_.Title -eq 'ReTray' -and $_.Visible}).Count -eq 1 } 'First activation did not display the main window.'
        $mainWindow = [BackgroundStartupWindows]::Read($process.Id) | Where-Object {$_.Title -eq 'ReTray' -and $_.Visible} | Select-Object -First 1
        [void][BackgroundStartupWindows]::PostMessage([IntPtr]::new($mainWindow.Handle),0x10,[IntPtr]::Zero,[IntPtr]::Zero)
        Wait-Until { @([BackgroundStartupWindows]::Read($process.Id) | Where-Object {$_.Handle -eq $mainWindow.Handle -and $_.Visible}).Count -eq 0 } 'Close did not hide the main window.'
        if ($process.HasExited) { throw 'Closing the interface exited the runtime.' }
        [void](Invoke-ReTrayDiagnosticRequest 'internal.openUi')
        Wait-Until { @([BackgroundStartupWindows]::Read($process.Id) | Where-Object {$_.Handle -eq $mainWindow.Handle -and $_.Visible}).Count -eq 1 } 'Activation did not reuse the main window.'
        $result.firstOpenAndReopen = $true
        $catalog = Invoke-ReTrayDiagnosticRequest 'catalog.read'
        $result.catalogCount = @($catalog.data.icons).Count
    }
    $result | ConvertTo-Json
} finally {
    if ($process) {
        if (!$process.HasExited) {
            [void](Invoke-ReTrayDiagnosticRequest 'agent.stop')
            if (!$process.WaitForExit(10000)) { throw 'Runtime cleanup did not finish.' }
        }
        $process.Dispose()
    }
}
