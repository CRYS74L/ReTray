param([switch]$SkipNative)
$ErrorActionPreference='Stop'
$productRoot=$PSScriptRoot
$version=(Get-Content -LiteralPath (Join-Path $productRoot 'VERSION') -Raw).Trim()
$common=Join-Path (Split-Path (Split-Path $productRoot -Parent) -Parent) '【Common】'
$dotnet=Join-Path $common 'DotNet\sdk-10\dotnet.exe'
if(!(Test-Path -LiteralPath $dotnet)){$dotnet=(Get-Command dotnet -ErrorAction Stop).Source}
$env:PATH=(Split-Path $dotnet -Parent)+';'+$env:PATH
if(!$SkipNative){foreach($target in @('Agent','Bridge')){& (Join-Path $productRoot 'build.ps1') -Target $target;if($LASTEXITCODE -ne 0){throw "Native $target failed"}}}
$layout=Join-Path $productRoot ('publish\'+$version+'\layout')
$distribution=Join-Path $productRoot ('dist\'+$version)
New-Item -ItemType Directory -Force -Path $distribution | Out-Null
& $dotnet publish (Join-Path $productRoot 'ui\TrayAssistant.App\TrayAssistant.App.csproj') -c Release -r win-x64 --self-contained true -p:Platform=x64 -p:WindowsAppSDKSelfContained=true -p:PublishTrimmed=false -p:PublishReadyToRun=false -p:AppxPackageSigningEnabled=false -p:GenerateAppxPackageOnBuild=false -o $layout -v:minimal
if($LASTEXITCODE -ne 0){throw 'UI publish failed'}
$manifest=Join-Path $layout 'AppxManifest.xml'
if(!(Test-Path -LiteralPath $manifest)){
    $generated=Join-Path $productRoot 'ui\TrayAssistant.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\AppxManifest.xml'
    Copy-Item -LiteralPath $generated -Destination $manifest
}
$doc=[xml](Get-Content -LiteralPath $manifest -Raw)
$doc.Package.Identity.Version=$version+'.0';$doc.Save($manifest)
$signing=Join-Path $productRoot 'artifacts\signing'
New-Item -ItemType Directory -Force -Path $signing | Out-Null
$certificate=Join-Path $signing 'development.pfx'
if(!(Test-Path -LiteralPath $certificate)){
    & winapp cert generate --publisher 'CN=CRYS74L' --output $certificate --export-cer --quiet
    if($LASTEXITCODE -ne 0){throw 'Development certificate creation failed'}
}
$package=Join-Path $distribution ('TrayIconAssistant-'+$version+'-x64.msix')
& winapp package $layout --manifest $manifest --output $package --cert $certificate --skip-pri --quiet
if($LASTEXITCODE -ne 0){throw 'MSIX packaging failed'}
Copy-Item -LiteralPath (Join-Path $signing 'development.cer') -Destination (Join-Path $distribution 'development-certificate.cer') -Force
Copy-Item -LiteralPath (Join-Path $productRoot 'packaging\Install.ps1') -Destination $distribution -Force
Copy-Item -LiteralPath (Join-Path $productRoot 'packaging\安装助手.cmd') -Destination $distribution -Force
Copy-Item -LiteralPath (Join-Path $productRoot 'packaging\README.txt') -Destination $distribution -Force
$hash=Get-FileHash -LiteralPath $package -Algorithm SHA256
Set-Content -LiteralPath (Join-Path $distribution 'SHA256.txt') -Value ($hash.Hash+'  '+(Split-Path $package -Leaf)) -Encoding utf8
Write-Host ('Build complete: '+$package)
