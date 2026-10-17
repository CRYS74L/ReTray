# Retain the newest two project versions plus any registered/running layout.
# Only versioned publish/dist products are eligible, never sources, settings,
# signing keys, design studies or arbitrary ignored files.
[CmdletBinding(SupportsShouldProcess)]
param([string]$ProjectRoot=(Split-Path $PSScriptRoot -Parent), [int]$KeepVersions=2)
$ErrorActionPreference='Stop'
if($KeepVersions -lt 2){throw 'Keep at least the current and previous versions'}
$root=[IO.Path]::GetFullPath($ProjectRoot)
if(!(Test-Path -LiteralPath (Join-Path $root 'VERSION')) -or !(Test-Path -LiteralPath (Join-Path $root 'package.ps1'))){throw 'Not a ReTray build root'}
$publish=Join-Path $root 'publish'
$dist=Join-Path $root 'dist'
$candidates=@()
foreach($parent in @($publish,$dist)) {
    if(!(Test-Path -LiteralPath $parent)){continue}
    foreach($directory in Get-ChildItem -LiteralPath $parent -Directory) {
        if($directory.Name -notmatch '^(\d+\.\d+\.\d+)(?:-origin[A-Za-z0-9.-]*)?$'){continue}
        if($directory.Attributes -band [IO.FileAttributes]::ReparsePoint){continue}
        $version=[Version]$Matches[1]
        $complete=if($parent -eq $publish) {
            Test-Path -LiteralPath (Join-Path $directory.FullName 'layout/ReTray.exe')
        } else {Test-Path -LiteralPath (Join-Path $directory.FullName 'SHA256.txt')}
        $candidates+= [pscustomobject]@{Directory=$directory;Version=$version;Parent=$parent;Complete=$complete}
    }
}
$keep=@($candidates | Where-Object Complete | ForEach-Object Version | Sort-Object -Descending -Unique | Select-Object -First $KeepVersions)
$latest=@($candidates | Where-Object Complete | Group-Object { $_.Parent+'|'+$_.Version } | ForEach-Object {
    $_.Group | Sort-Object {$_.Directory.LastWriteTimeUtc} -Descending | Select-Object -First 1
} | ForEach-Object {$_.Directory.FullName})
$protected=@(Get-Process -Name ReTray,ReTray.App,ReTray.Agent -ErrorAction SilentlyContinue | ForEach-Object {$_.Path} | Where-Object {$_})
$package=Get-AppxPackage -Name CRYS74L.ReTray -ErrorAction Stop
if($package){$protected+= $package.InstallLocation}
$removed=0L
foreach($candidate in $candidates) {
    if($candidate.Version -in $keep -and $candidate.Directory.FullName -in $latest){continue}
    $target=[IO.Path]::GetFullPath($candidate.Directory.FullName)
    $prefix=$target+[IO.Path]::DirectorySeparatorChar
    if(!$target.StartsWith($candidate.Parent+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Build cleanup escaped its output directory'}
    if(@($protected | Where-Object {$_ -eq $target -or $_.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)}).Count){continue}
    # Refuse nested junctions as well, so recursion cannot leave this build.
    if(@(Get-ChildItem -LiteralPath $target -Recurse -Force | Where-Object {$_.Attributes -band [IO.FileAttributes]::ReparsePoint}).Count){continue}
    if($PSCmdlet.ShouldProcess($target,'Remove obsolete generated build')) {
        $bytes=(Get-ChildItem -LiteralPath $target -Recurse -File -Force | Measure-Object Length -Sum).Sum
        try {Remove-Item -LiteralPath $target -Recurse; $removed+=$bytes}
        catch {Write-Warning ('Could not fully remove generated build '+$target+': '+$_.Exception.Message)}
    }
}
Write-Host ('Generated build cleanup reclaimed '+[math]::Round($removed/1GB,2)+' GiB; kept versions '+($keep -join ', '))
