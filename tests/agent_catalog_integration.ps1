param([switch]$SkipBuild,[string]$ExpectedVersion='',[int]$ReopenCount=5)
$ErrorActionPreference='Stop'
$productRoot=Split-Path $PSScriptRoot -Parent
if(!$SkipBuild){
    & (Join-Path $productRoot 'build.ps1') -Target Source
    if($LASTEXITCODE -ne 0){throw 'Source build failed'}
}
if($ReopenCount -lt 1 -or $ReopenCount -gt 20){throw 'ReopenCount must be between 1 and 20'}
$sourcePath=Join-Path $productRoot 'build\ReTray.TestSource.exe'
$sourceSelector=$sourcePath.ToLowerInvariant()
$sources=[Collections.Generic.List[Diagnostics.Process]]::new()
$results=[Collections.Generic.List[object]]::new()
$policyRule=$null
$previousRule=$null
if(!('CatalogLifecycleNative' -as [type])){
    Add-Type -TypeDefinition @'
using System;using System.Runtime.InteropServices;
public static class CatalogLifecycleNative {
 [DllImport("user32.dll")]public static extern IntPtr SendMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
 [DllImport("user32.dll")]public static extern bool PostMessage(IntPtr h,uint m,IntPtr w,IntPtr l);
}
'@
}
function Invoke-CatalogAgent([string]$Method,$Data=@{}){
    $session=(Get-Process -Id $PID).SessionId
    $pipe=[IO.Pipes.NamedPipeClientStream]::new('.',"ReTray.v1.$session",[IO.Pipes.PipeDirection]::InOut)
    $writer=$null;$reader=$null
    try{
        $pipe.Connect(3000);$encoding=[Text.UTF8Encoding]::new($false)
        $writer=[IO.StreamWriter]::new($pipe,$encoding,4096,$true);$writer.AutoFlush=$true
        $reader=[IO.StreamReader]::new($pipe,$encoding,$false,4096,$true)
        $writer.WriteLine((@{schema=1;requestId='catalog-lifecycle';method=$Method;data=$Data}|ConvertTo-Json -Depth 40 -Compress))
        $response=$reader.ReadLineAsync();if(!$response.Wait(6000)){throw 'Agent response timed out'}
        return $response.Result|ConvertFrom-Json -Depth 40
    }finally{
        if($writer){$writer.Dispose()};if($reader){$reader.Dispose()};$pipe.Dispose()
    }
}
function Read-SourceIcons{
    $reply=Invoke-CatalogAgent 'catalog.read'
    if(!$reply.success){throw $reply.detail}
    @($reply.data.icons|Where-Object {$_.selector.executable -eq $sourceSelector})
}
function Wait-SourceCount([int]$Count){
    $watch=[Diagnostics.Stopwatch]::StartNew()
    do{
        $icons=@(Read-SourceIcons)
        if($icons.Count -eq $Count){return $watch.ElapsedMilliseconds}
        Start-Sleep -Milliseconds 100
    }while($watch.ElapsedMilliseconds -lt 6000)
    throw "Expected $Count current source registrations, observed $($icons.Count)"
}
function Start-LifecycleSource{
    $process=Start-Process -FilePath $sourcePath -ArgumentList '--unique-guid' -WindowStyle Hidden -PassThru
    $sources.Add($process)
    $deadline=[DateTime]::UtcNow.AddSeconds(4);$map=$null;$view=$null
    try{
        do{
            try{$map=[IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting("Local\ReTray.TestSource.$($process.Id)");break}
            catch{if($process.HasExited){throw 'Controlled source exited during startup'};Start-Sleep -Milliseconds 50}
        }while([DateTime]::UtcNow -lt $deadline)
        if(!$map){throw 'Missing source status mapping'}
        $view=$map.CreateViewAccessor(0,0,[IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
        do{$handle=$view.ReadUInt64(8);if($handle){break};Start-Sleep -Milliseconds 30}while([DateTime]::UtcNow -lt $deadline)
        if(!$handle){throw 'Missing source HWND'}
        return [pscustomobject]@{Process=$process;Hwnd=[IntPtr]::new([long]$handle)}
    }finally{if($view){$view.Dispose()};if($map){$map.Dispose()}}
}
try{
    $status=Invoke-CatalogAgent 'status.read'
    if(!$status.success -or !$status.data.ready){throw 'Run the assistant before testing its current catalog'}
    if($ExpectedVersion -and $status.data.version -ne $ExpectedVersion){throw 'Running agent version mismatch'}
    if(@(Read-SourceIcons).Count){throw 'Existing controlled source registrations must be preserved; stop that test first'}
    $settingsBefore=(Invoke-CatalogAgent 'settings.read').data
    for($cycle=1;$cycle -le $ReopenCount;$cycle++){
        $source=Start-LifecycleSource;[void](Wait-SourceCount 2)
        $oldKeys=@(Read-SourceIcons|ForEach-Object key)
        # Intentional abnormal exit, restricted to the process started by this test.
        Stop-Process -Id $source.Process.Id -Force;$source.Process.WaitForExit()
        $elapsed=Wait-SourceCount 0
        $results.Add([pscustomobject]@{test='abrupt-exit-and-reopen';cycle=$cycle;removedAfterMs=$elapsed})
        $stale=Invoke-CatalogAgent 'icon.visibility' @{key=$oldKeys[0];visibility=1}
        if($stale.success -or $stale.errorCode -ne 1168){throw 'Exited key was accepted by visibility transaction'}
    }
    $first=Start-LifecycleSource;[void](Wait-SourceCount 2)
    $second=Start-LifecycleSource;[void](Wait-SourceCount 4)
    $results.Add([pscustomobject]@{test='two-live-instances-kept';count=4})
    [void][CatalogLifecycleNative]::PostMessage($second.Hwnd,0x800c,[IntPtr]::Zero,[IntPtr]::Zero)
    if(!$second.Process.WaitForExit(2500)){throw 'Normal source exit failed'}
    [void](Wait-SourceCount 2)
    $target=@(Read-SourceIcons|Where-Object {!$_.selector.hasGuid})[0]
    $policyRule=$target
    $selectorJson=$target.selector|ConvertTo-Json -Depth 12 -Compress
    $previousRule=@($settingsBefore.rules|Where-Object {($_.selector|ConvertTo-Json -Depth 12 -Compress) -eq $selectorJson})|Select-Object -First 1
    foreach($visibility in @(1,0)){
        $reply=Invoke-CatalogAgent 'icon.visibility' @{key=$target.key;visibility=$visibility}
        if(!$reply.success -or !$reply.saved){throw "Live visibility failed: $($reply.detail)"}
        $current=@(Read-SourceIcons|Where-Object {$_.key -eq $target.key})[0]
        if(($current.placement -eq 2) -ne ($visibility -eq 1)){throw 'Live visibility did not change native state'}
    }
    $results.Add([pscustomobject]@{test='live-hide-and-show';status='PASS'})
    $policyRule.asset=Join-Path $env:SystemRoot 'System32\shell32.dll'
    $policyRule.tip='Registration recreation test'
    $policyRule.baselineFingerprint=$target.sourceFingerprint
    $policyRule.baselineWidth=$target.sourceWidth
    $policyRule.baselineHeight=$target.sourceHeight
    $applied=Invoke-CatalogAgent 'rule.apply' @{rule=$policyRule}
    if(!$applied.success -or !$applied.saved){throw 'Custom registration fixture failed to apply'}
    $hidden=Invoke-CatalogAgent 'icon.visibility' @{key=$target.key;visibility=1}
    if(!$hidden.success){throw 'Custom registration fixture failed to hide'}
    $registration=@(Read-SourceIcons|Where-Object {$_.key -eq $target.key})[0].registration
    if([CatalogLifecycleNative]::SendMessage($first.Hwnd,0x801e,[IntPtr]::Zero,[IntPtr]::Zero) -ne [IntPtr]1){throw 'Same-owner registration recreation failed'}
    $watch=[Diagnostics.Stopwatch]::StartNew()
    do{
        $current=@(Read-SourceIcons|Where-Object {$_.key -eq $target.key})|Select-Object -First 1
        if($current -and $current.registration -gt $registration -and $current.custom -and $current.placement -eq 2 -and $current.tipCurrent -eq $policyRule.tip){break}
        Start-Sleep -Milliseconds 100
    }while($watch.ElapsedMilliseconds -lt 6000)
    if(!$current -or !$current.custom -or $current.placement -ne 2 -or $current.tipCurrent -ne $policyRule.tip -or $current.registration -le $registration){throw 'Same-identity recreation lost saved policy'}
    $results.Add([pscustomobject]@{test='same-identity-delete-add-restores-policy';restoredAfterMs=$watch.ElapsedMilliseconds})
    $renewed=[CatalogLifecycleNative]::SendMessage($first.Hwnd,0x801c,[IntPtr]::Zero,[IntPtr]::Zero)
    if($renewed -ne [IntPtr]1){throw 'Silent window destruction failed'}
    $elapsed=Wait-SourceCount 0
    if($first.Process.HasExited){throw 'Window-only test must keep the process alive'}
    $results.Add([pscustomobject]@{test='owner-window-exit-with-process-alive';removedAfterMs=$elapsed})
    $map=[IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting("Local\ReTray.TestSource.$($first.Process.Id)")
    $view=$map.CreateViewAccessor(0,0,[IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
    try{$newWindow=[IntPtr]::new([long]$view.ReadUInt64(8))}finally{$view.Dispose();$map.Dispose()}
    if([CatalogLifecycleNative]::SendMessage($newWindow,0x801d,[IntPtr]::Zero,[IntPtr]::Zero) -ne [IntPtr]1){throw 'Re-register in same process failed'}
    [void](Wait-SourceCount 1)
    $results.Add([pscustomobject]@{test='same-process-new-owner-registration';status='PASS'})
}finally{
    if($policyRule){
        $restore=if($previousRule){Invoke-CatalogAgent 'rule.apply' @{rule=$previousRule}}else{Invoke-CatalogAgent 'rule.remove' @{rule=$policyRule}}
        if(!$restore.success){Write-Error "Test rule restoration failed: $($restore.detail)"}
    }
    foreach($process in $sources){if(!$process.HasExited){Stop-Process -Id $process.Id -Force;$process.WaitForExit()};$process.Dispose()}
}
[void](Wait-SourceCount 0)
$settingsAfter=(Invoke-CatalogAgent 'settings.read').data
$settingsBefore.revision=$settingsAfter.revision
if(($settingsBefore|ConvertTo-Json -Depth 40 -Compress) -ne ($settingsAfter|ConvertTo-Json -Depth 40 -Compress)){throw 'Lifecycle test changed pre-existing settings'}
$evidence=Join-Path $productRoot 'artifacts\catalog-lifecycle'
New-Item -ItemType Directory -Path $evidence -Force|Out-Null
$results|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $evidence 'results.json') -Encoding utf8
$results|Format-Table -AutoSize
Write-Host 'PASS real native lifecycle, GUID/legacy identities, live multi-instance visibility and unchanged pre-existing settings'
exit 0
