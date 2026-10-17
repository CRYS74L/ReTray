param([switch]$SkipNative,[string]$BuildSuffix='')
$ErrorActionPreference='Stop'
$productRoot=$PSScriptRoot
$version=(Get-Content -LiteralPath (Join-Path $productRoot 'VERSION') -Raw).Trim()
if($version -notmatch '^\d+\.\d+\.\d+$'){throw 'VERSION must contain a three-part numeric version'}
if($BuildSuffix -notmatch '^[A-Za-z0-9.-]*$'){throw 'BuildSuffix must contain only letters, digits, dots or hyphens'}
$common=Join-Path (Split-Path (Split-Path $productRoot -Parent) -Parent) '【Common】'
$dotnet=Join-Path $common 'DotNet\sdk-10\dotnet.exe'
if(!(Test-Path -LiteralPath $dotnet)){$dotnet=(Get-Command dotnet -ErrorAction Stop).Source}
$env:PATH=(Split-Path $dotnet -Parent)+';'+$env:PATH
if(!$SkipNative){foreach($target in @('Runtime','Bridge')){& (Join-Path $productRoot 'build.ps1') -Target $target;if($LASTEXITCODE -ne 0){throw "Native $target failed"}}}
$publishRoot=[System.IO.Path]::GetFullPath((Join-Path $productRoot 'publish'))
$layout=[System.IO.Path]::GetFullPath((Join-Path $publishRoot ($version+'-origin'+$BuildSuffix+'\layout')))
if(!$layout.StartsWith($publishRoot+[System.IO.Path]::DirectorySeparatorChar,[System.StringComparison]::OrdinalIgnoreCase)){throw 'Publish layout must remain inside this product publish directory'}
if(Test-Path -LiteralPath $layout){
    # Include old executable names so an upgrade never overwrites a running old layout.
    $usingLayout=@(Get-Process -Name 'ReTray','ReTray.App','ReTray.Agent' -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -and $_.Path.StartsWith($layout+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)
    })
    $registered=Get-AppxPackage -Name 'CRYS74L.ReTray' -ErrorAction Stop
    if($usingLayout.Count -or ($registered -and $registered.InstallLocation -eq $layout)){
        throw 'This layout is running or registered. Choose a new BuildSuffix instead of overwriting it.'
    }
    Remove-Item -LiteralPath $layout -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $layout | Out-Null
$runtime=Join-Path $layout 'runtime'
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
$distribution=Join-Path $productRoot ('dist\'+$version)
New-Item -ItemType Directory -Force -Path $distribution | Out-Null
& (Join-Path $productRoot 'packaging/build-assets.ps1')
$originRoot=Join-Path $productRoot 'ui\ReTray.Wpf'
$originProject=Join-Path $originRoot 'src\LanDeviceMonitor.App\LanDeviceMonitor.App.csproj'
if(!(Test-Path -LiteralPath $originProject)){throw 'The complete LAN source project is missing'}
Push-Location -LiteralPath $originRoot
try{
    & $dotnet publish $originProject -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -p:PublishTrimmed=false -p:PublishReadyToRun=false -p:DebugType=None -p:DebugSymbols=false -o $runtime -v:minimal
    if($LASTEXITCODE -ne 0){throw 'UI publish failed'}
}finally{Pop-Location}
foreach($required in @('ReTray.exe','ReTray.Runtime.dll','ReTray.ShellBridge.dll')) {
    if(!(Test-Path -LiteralPath (Join-Path $runtime $required) -PathType Leaf)){throw "Unified application payload is missing $required"}
}
# Use the .NET SDK's apphost writer to bind the actual executable directly to
# runtime/ReTray.dll. There is no wrapper process or second ReTray.exe.
$resolvedHost=(& $dotnet msbuild $originProject -p:RuntimeIdentifier=win-x64 -p:Platform=x64 -t:ResolveFrameworkReferences -getItem:ResolvedAppHostPack -v:quiet | Out-String | ConvertFrom-Json)
if($LASTEXITCODE -ne 0){throw 'Apphost runtime resolution failed'}
$template=$resolvedHost.Items.ResolvedAppHostPack[0].Path
if(!$template){throw 'Restored .NET apphost template missing'}
$sdkVersion=(& $dotnet --version).Trim()
Add-Type -Path (Join-Path (Split-Path $dotnet -Parent) ('sdk/'+$sdkVersion+'/Microsoft.NET.HostModel.dll'))
[Microsoft.NET.HostModel.AppHost.HostWriter]::CreateAppHost($template,(Join-Path $layout 'ReTray.exe'),'runtime/ReTray.dll',$true,(Join-Path $runtime 'ReTray.dll'),$false,$false,$null)
Remove-Item -LiteralPath (Join-Path $runtime 'ReTray.exe')
$shellAssets=Join-Path $layout 'Assets'
New-Item -ItemType Directory -Path $shellAssets -Force | Out-Null
foreach($image in Get-ChildItem -LiteralPath (Join-Path $productRoot 'ui/ReTray.App/Assets') -File) {
    if($image.Name -match '^(StoreLogo|Square44x44Logo|Square150x150Logo|Wide310x150Logo|SplashScreen)[.-]' -or $image.Name -eq 'DesktopIcon.ico') {
        $name=$image.Name.Replace('StoreLogo','ReTrayStore').Replace('Square44x44Logo','ReTrayAppList').Replace('Square150x150Logo','ReTrayTile').Replace('Wide310x150Logo','ReTrayWide').Replace('SplashScreen','ReTraySplash')
        Copy-Item -LiteralPath $image.FullName -Destination (Join-Path $shellAssets $name)
    }
}
foreach($obsolete in @('ReTray.App.exe','ReTray.Agent.exe','ReTray.Backend.exe')) {
    if(Test-Path -LiteralPath (Join-Path $layout $obsolete)){throw "Obsolete separate-process payload found: $obsolete"}
}
$manifest=Join-Path $layout 'AppxManifest.xml'
$doc=[xml](Get-Content -LiteralPath (Join-Path $productRoot 'ui\ReTray.App\Package.appxmanifest') -Raw)
$doc.Package.Identity.Version=$version+'.0'
$mainApplication=$doc.Package.Applications.Application | Where-Object Id -eq 'App'
$mainApplication.Executable='ReTray.exe'
$mainApplication.Extensions.Extension.Executable='ReTray.exe'
$mainApplication.EntryPoint='Windows.FullTrustApplication'
$doc.Package.Resources.Resource.Language='zh-CN'
# Index the SVG-derived scale/targetsize PNGs explicitly, including every scale
# in the main package. Do not inherit a stale SDK-generated resource index.
$doc.Package.Properties.Logo='Assets\ReTrayStore.png'
$mainApplication.VisualElements.Square150x150Logo='Assets\ReTrayTile.png'
$mainApplication.VisualElements.Square44x44Logo='Assets\ReTrayAppList.png'
$mainApplication.VisualElements.DefaultTile.Wide310x150Logo='Assets\ReTrayWide.png'
$mainApplication.VisualElements.SplashScreen.Image='Assets\ReTraySplash.png'
foreach($legal in @('LICENSE','NOTICE.md','README.md')) {
    Copy-Item -LiteralPath (Join-Path $productRoot $legal) -Destination $layout -Force
}
$doc.Save($manifest)
$productEntries=@(Get-ChildItem -LiteralPath $layout -Recurse -File -Filter 'ReTray*.exe')
if($productEntries.Count -ne 1 -or $productEntries[0].FullName -ne (Join-Path $layout 'ReTray.exe')) {
    throw 'The distribution must contain exactly one ReTray executable at its root'
}
$duplicateShellImages=@(Get-ChildItem -LiteralPath (Join-Path $runtime 'Assets') -File | Where-Object {$_.Name -match '^(StoreLogo|Square44x44Logo|Square150x150Logo|Wide310x150Logo|SplashScreen|ReTrayAppList|ReTrayTile)[.-]'})
if($duplicateShellImages.Count){throw 'Shell icon resources were duplicated in runtime/Assets'}
$signing=Join-Path $productRoot 'artifacts\signing'
New-Item -ItemType Directory -Force -Path $signing | Out-Null
$certificate=Join-Path $signing 'development.pfx'
if(!(Test-Path -LiteralPath $certificate)){
    & winapp cert generate --publisher 'CN=CRYS74L' --output $certificate --export-cer --quiet
    if($LASTEXITCODE -ne 0){throw 'Development certificate creation failed'}
}
$package=Join-Path $distribution ('ReTray-'+$version+'-x64.msix')
& winapp tool makepri new /pr $layout /cf (Join-Path $productRoot 'packaging/priconfig.xml') /mn $manifest /of (Join-Path $layout 'resources.pri') /o | Out-Null
if($LASTEXITCODE -ne 0){throw 'ReTray icon resource index generation failed'}
& winapp package $layout --manifest $manifest --output $package --cert $certificate --skip-pri --quiet
if($LASTEXITCODE -ne 0){throw 'MSIX packaging failed'}
Copy-Item -LiteralPath (Join-Path $signing 'development.cer') -Destination (Join-Path $distribution 'development-certificate.cer') -Force
Copy-Item -LiteralPath (Join-Path $productRoot 'packaging\Install.ps1') -Destination $distribution -Force
Copy-Item -LiteralPath (Join-Path $productRoot 'packaging\Shortcut.ps1') -Destination $distribution -Force
Copy-Item -LiteralPath (Join-Path $productRoot 'packaging\安装ReTray.cmd') -Destination $distribution -Force
Copy-Item -LiteralPath (Join-Path $productRoot 'packaging\README.txt') -Destination $distribution -Force
Copy-Item -LiteralPath (Join-Path $productRoot 'LICENSE') -Destination $distribution -Force
Copy-Item -LiteralPath (Join-Path $productRoot 'NOTICE.md') -Destination $distribution -Force
$portable=Join-Path $distribution ('ReTray-'+$version+'-win-x64.zip')
Compress-Archive -Path (Join-Path $layout '*') -DestinationPath $portable -Force
$hashes=@($package,$portable) | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash+'  '+(Split-Path $_ -Leaf) }
Set-Content -LiteralPath (Join-Path $distribution 'SHA256.txt') -Value $hashes -Encoding utf8
Write-Host ('Build complete: '+$package)
# Keep a rollback build without accumulating every previous package forever.
& (Join-Path $productRoot 'packaging/Clear-GeneratedBuilds.ps1') -ProjectRoot $productRoot
