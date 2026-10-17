param([Parameter(Mandatory)][string]$LayoutPath)
$ErrorActionPreference='Stop'
$project=Split-Path $PSScriptRoot -Parent
. (Join-Path $project 'packaging/Shortcut.ps1')
$fixture=Join-Path $project ('artifacts/shortcut-identity-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture -Force | Out-Null
$linkPath=Join-Path $fixture 'ReTray.lnk'
$brand=Join-Path $fixture 'Branding'
$family='CRYS74L.ReTray_fh3g15a7mygna'
function Require([bool]$Condition,[string]$Message) { if(!$Condition) { throw $Message } }
foreach($iteration in 1..2) {
    Write-ReTrayDesktopShortcut -InstallLocation $LayoutPath -PackageFamilyName $family -ShortcutPath $linkPath -BrandDirectory $brand
    $shell=New-Object -ComObject WScript.Shell
    $link=$shell.CreateShortcut($linkPath)
    Require ($link.TargetPath -eq (Join-Path $LayoutPath 'ReTray.exe')) 'Shortcut must target the package activation launcher'
    Require ($link.Arguments -eq ('"'+$family+'!App" --show')) 'Shortcut must preserve package identity and request a visible window'
    Require ($link.WorkingDirectory -eq $LayoutPath) 'Open file location must resolve inside the application payload'
    Require ($link.IconLocation -eq ((Join-Path $brand 'DesktopIcon.ico')+',0')) 'Desktop icon must retain its separate white background artwork'
    Require ([ReTray.Packaging.ShortcutIdentity]::Read($linkPath) -eq 'CRYS74L.ReTray.Desktop') 'Desktop artwork must be isolated from the colored taskbar group'
}
$other=Join-Path $fixture 'Unrelated.lnk'
$foreign=$shell.CreateShortcut($other)
$foreign.TargetPath=Join-Path $env:WINDIR 'System32/notepad.exe'
$foreign.Save()
$before=(Get-FileHash -LiteralPath $other -Algorithm SHA256).Hash
$rejected=$false
try { Write-ReTrayDesktopShortcut -InstallLocation $LayoutPath -PackageFamilyName $family -ShortcutPath $other -BrandDirectory $brand }
catch { $rejected=$_.Exception.Message -like '*unrelated*' }
Require $rejected 'An unrelated shortcut must be rejected'
Require ((Get-FileHash -LiteralPath $other -Algorithm SHA256).Hash -eq $before) 'An unrelated shortcut was changed'
Write-Host ('PASS package launcher target, activation arguments, artwork, explicit group identity, repeat update and unrelated shortcut preservation: '+$fixture)
