@echo off
set "TRAY_INSTALL_SCRIPT=%~dp0Install.ps1"
powershell.exe -NoProfile -Command "Start-Process -FilePath powershell.exe -Verb RunAs -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File ""' + $env:TRAY_INSTALL_SCRIPT + '""')"

