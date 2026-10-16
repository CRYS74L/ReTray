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
$brand=Join-Path $env:LOCALAPPDATA 'ReTray/Branding'
New-Item -ItemType Directory -Path $brand -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $installed.InstallLocation 'Assets/DesktopIcon.ico') -Destination (Join-Path $brand 'DesktopIcon.ico') -Force
$shell=New-Object -ComObject WScript.Shell
$shortcutPath=Join-Path ([Environment]::GetFolderPath('Desktop')) 'ReTray.lnk'
if(Test-Path -LiteralPath $shortcutPath) {
    $existing=$shell.CreateShortcut($shortcutPath)
    if($existing.Arguments -notmatch 'CRYS74L\.ReTray' -and
       [IO.Path]::GetFileName($existing.TargetPath) -notin @('ReTray.exe','ReTray.App.exe')) {
        throw 'An unrelated ReTray desktop shortcut already exists; it was preserved.'
    }
}
$shortcut=$shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath=Join-Path $env:WINDIR 'explorer.exe'
$shortcut.Arguments='shell:AppsFolder\'+$installed.PackageFamilyName+'!App'
$shortcut.IconLocation=(Join-Path $brand 'DesktopIcon.ico')+',0'
$shortcut.Description='ReTray — Redefine Your Tray'
$shortcut.Save()
Write-Host 'Installed for the current user. Open ReTray from Start.'
