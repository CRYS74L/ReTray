#Requires -RunAsAdministrator
$ErrorActionPreference='Stop'
$packages=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'ReTray-*-x64.msix')
if($packages.Count -ne 1){throw 'Expected exactly one ReTray package'}
$certificate=Join-Path $PSScriptRoot 'development-certificate.cer'
$cert=[Security.Cryptography.X509Certificates.X509Certificate2]::new($certificate)
if($cert.Subject -ne 'CN=CRYS74L'){throw 'Unexpected package publisher'}
Write-Host ('This development build uses a self-signed certificate: '+$cert.Thumbprint)
Import-Certificate -FilePath $certificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
Add-AppxPackage -Path $packages[0].FullName
$installed=Get-AppxPackage -Name 'CRYS74L.ReTray' -ErrorAction Stop
if(!$installed){throw 'ReTray package registration was not found after installation'}
. (Join-Path $PSScriptRoot 'Shortcut.ps1')
Write-ReTrayDesktopShortcut -InstallLocation $installed.InstallLocation -PackageFamilyName $installed.PackageFamilyName
Write-Host 'Installed for the current user. Open ReTray from Start.'
