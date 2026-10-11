#Requires -RunAsAdministrator
$ErrorActionPreference='Stop'
$packages=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'TrayIconAssistant-*-x64.msix')
if($packages.Count -ne 1){throw 'Expected exactly one assistant package'}
$certificate=Join-Path $PSScriptRoot 'development-certificate.cer'
$cert=[Security.Cryptography.X509Certificates.X509Certificate2]::new($certificate)
if($cert.Subject -ne 'CN=CRYS74L'){throw 'Unexpected package publisher'}
Write-Host ('This development build uses a self-signed certificate: '+$cert.Thumbprint)
Import-Certificate -FilePath $certificate -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
Add-AppxPackage -Path $packages[0].FullName
Write-Host 'Installed for the current user. Open Tray Icon Assistant from Start.'
