param([switch]$SkipNative,[string]$BuildSuffix='-portable')
$ErrorActionPreference='Stop'
$productRoot=$PSScriptRoot
$version=(Get-Content -LiteralPath (Join-Path $productRoot 'VERSION') -Raw).Trim()
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'VERSION must contain a three-part numeric version'}
if($BuildSuffix -notmatch '^[A-Za-z0-9.-]*$'){throw 'Invalid build suffix'}
$common=Join-Path (Split-Path (Split-Path $productRoot -Parent) -Parent) '【Common】'
$dotnet=Join-Path $common 'DotNet\sdk-10\dotnet.exe'
if(!(Test-Path -LiteralPath $dotnet)){$dotnet=(Get-Command dotnet -ErrorAction Stop).Source}
if(!$SkipNative){foreach($target in @('Runtime','Bridge')){& (Join-Path $productRoot 'build.ps1') -Target $target;if($LASTEXITCODE -ne 0){throw "Native $target build failed"}}}
$publishRoot=[IO.Path]::GetFullPath((Join-Path $productRoot 'publish'))
$layout=[IO.Path]::GetFullPath((Join-Path $publishRoot ($version+'-origin'+$BuildSuffix+'\layout')))
if(!$layout.StartsWith($publishRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid publish target'}
# Never overwrite a running/previously populated portable directory or its Data.
if(Test-Path -LiteralPath $layout){throw 'Choose a fresh BuildSuffix; existing portable data is preserved'}
New-Item -ItemType Directory -Path $layout -Force | Out-Null
& (Join-Path $productRoot 'packaging/build-assets.ps1')
$originRoot=Join-Path $productRoot 'ui/ReTray.Wpf'
$project=Join-Path $originRoot 'src/LanDeviceMonitor.App/LanDeviceMonitor.App.csproj'
Push-Location -LiteralPath $originRoot
try{
    & $dotnet publish $project -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:ReTrayPortable=true -p:DebugType=None -p:DebugSymbols=false -p:PublishReadyToRun=false -o $layout -v:minimal
    if($LASTEXITCODE -ne 0){throw 'Portable publish failed'}
}finally{Pop-Location}
$entries=@(Get-ChildItem -LiteralPath $layout -Force)
if($entries.Count -ne 1 -or $entries[0].Name -ne 'ReTray.exe' -or $entries[0].PSIsContainer){throw 'Portable distribution must contain only ReTray.exe'}
$distribution=Join-Path $productRoot ('dist/'+$version)
New-Item -ItemType Directory -Path $distribution -Force | Out-Null
$exe=Join-Path $distribution 'ReTray.exe'
Copy-Item -LiteralPath (Join-Path $layout 'ReTray.exe') -Destination $exe
$zip=Join-Path $distribution ('ReTray-'+$version+'-portable-win-x64.zip')
Compress-Archive -LiteralPath $exe -DestinationPath $zip -Force
@($exe,$zip) | ForEach-Object {(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash+'  '+[IO.Path]::GetFileName($_)} | Set-Content -LiteralPath (Join-Path $distribution 'SHA256.txt') -Encoding utf8
Write-Host ('Portable build complete: '+$exe)
& (Join-Path $productRoot 'packaging/Clear-GeneratedBuilds.ps1') -ProjectRoot $productRoot