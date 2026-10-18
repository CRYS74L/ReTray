param([string]$KeepPublish='')
$ErrorActionPreference='Stop'
$productRoot=Split-Path $PSScriptRoot -Parent
$publishRoot=[System.IO.Path]::GetFullPath((Join-Path $productRoot 'publish'))
$distRoot=[System.IO.Path]::GetFullPath((Join-Path $productRoot 'dist'))
$version=(Get-Content -LiteralPath (Join-Path $productRoot 'VERSION') -Raw).Trim()
$numericVersion=$version -replace '[-+].*$',''
$activePaths=@(Get-Process -Name 'ReTray','ReTray.App','ReTray.Agent' -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Path)
# A loose MSIX registration points at its build directory even while the app is
# stopped. Never remove that directory just because no process is using it.
try {
    $registeredPackages=@(Get-AppxPackage -Name 'CRYS74L.ReTray' -ErrorAction Stop)
    foreach($registered in $registeredPackages) {
        if([string]::IsNullOrWhiteSpace($registered.InstallLocation)) {
            Write-Warning 'Build copy cleanup skipped: the registered package location is unavailable.'
            return
        }
        $activePaths+=([System.IO.Path]::GetFullPath($registered.InstallLocation)+[System.IO.Path]::DirectorySeparatorChar+'AppxManifest.xml')
    }
} catch {
    Write-Warning 'Build copy cleanup skipped: package registration could not be checked.'
    return
}
$copies=@(Get-ChildItem -LiteralPath $publishRoot -Directory | Where-Object {$_.Name -match '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$'})
$previous=$copies | Where-Object {[version]($_.Name.Split('-')[0]) -lt [version]$numericVersion} |
    Sort-Object @{Expression={[version]($_.Name.Split('-')[0])};Descending=$true}, @{Expression={$_.LastWriteTime};Descending=$true} |
    Select-Object -First 1
$previousVersion=if($previous){$previous.Name.Split('-')[0]}else{''}
function Remove-GeneratedCopy([string]$target,[string]$root) {
    $absolute=[System.IO.Path]::GetFullPath($target)
    if(!$absolute.StartsWith($root+[System.IO.Path]::DirectorySeparatorChar,[System.StringComparison]::OrdinalIgnoreCase)){throw 'Build copy is outside its generated output directory'}
    if(@($activePaths | Where-Object {$_ -and $_.StartsWith($absolute+[System.IO.Path]::DirectorySeparatorChar,[System.StringComparison]::OrdinalIgnoreCase)}).Count){return}
    Remove-Item -LiteralPath $absolute -Recurse -ErrorAction SilentlyContinue
    if(!(Test-Path -LiteralPath $absolute)){return}
    # Loose MSIX registration leaves empty hidden metadata directories. Delete
    # only empty directories; a nonempty or locked directory is retained.
    foreach($folder in @(Get-ChildItem -LiteralPath $absolute -Directory -Recurse -Force | Sort-Object {$_.FullName.Length} -Descending)) {
        if(![System.IO.Directory]::EnumerateFileSystemEntries($folder.FullName).GetEnumerator().MoveNext()) {
            [System.IO.Directory]::Delete($folder.FullName,$false)
        }
    }
    if(![System.IO.Directory]::EnumerateFileSystemEntries($absolute).GetEnumerator().MoveNext()) {
        [System.IO.Directory]::Delete($absolute,$false)
    }
}
foreach($copy in $copies) {
    if(($KeepPublish -and $copy.FullName -eq [System.IO.Path]::GetFullPath($KeepPublish)) -or ($previous -and $copy.FullName -eq $previous.FullName)){continue}
    Remove-GeneratedCopy $copy.FullName $publishRoot
}
foreach($copy in @(Get-ChildItem -LiteralPath $distRoot -Directory | Where-Object {$_.Name -match '^\d+\.\d+\.\d+$'})) {
    if($copy.Name -in @($numericVersion,$previousVersion)){continue}
    Remove-GeneratedCopy $copy.FullName $distRoot
}
