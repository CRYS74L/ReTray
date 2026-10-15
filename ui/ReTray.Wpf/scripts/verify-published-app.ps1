param(
    [int]$TimeoutSeconds = 15
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $projectRoot 'src\LanDeviceMonitor.App\LanDeviceMonitor.App.csproj'
$projectXml = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $projectPath)
$applicationName = [string]$projectXml.Project.PropertyGroup.AssemblyName
$executablePath = Join-Path (
    Join-Path (
        Join-Path $projectRoot 'artifacts') $applicationName) "$applicationName.exe"
$executablePath = [System.IO.Path]::GetFullPath($executablePath)

if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Published executable does not exist: $executablePath"
}

$existingProcesses = Get-Process -Name $applicationName -ErrorAction SilentlyContinue |
    Where-Object {
        $_.Path -and
        $_.Path.Equals(
            $executablePath,
            [System.StringComparison]::OrdinalIgnoreCase)
    }
if ($existingProcesses) {
    throw "Close the published application before verification: $executablePath"
}

$primaryProcess = $null
try {
    $primaryProcess = Start-Process -FilePath $executablePath -PassThru
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 200
        $primaryProcess.Refresh()
        if ($primaryProcess.HasExited) {
            throw "Published executable exited during startup with code $($primaryProcess.ExitCode)."
        }
    } while (
        $primaryProcess.MainWindowHandle -eq 0 -and
        (Get-Date) -lt $deadline)

    if ($primaryProcess.MainWindowHandle -eq 0) {
        throw "Published executable did not create a main window within $TimeoutSeconds seconds."
    }

    $primaryWindowHandle = $primaryProcess.MainWindowHandle
    $secondaryProcess = Start-Process -FilePath $executablePath -PassThru
    if (-not $secondaryProcess.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $secondaryProcess.Id -Force -ErrorAction SilentlyContinue
        throw "Second launch did not complete single-instance handoff within $TimeoutSeconds seconds."
    }

    Start-Sleep -Milliseconds 500
    $primaryProcess.Refresh()
    if ($secondaryProcess.ExitCode -ne 0) {
        throw "Second launch exited with code $($secondaryProcess.ExitCode)."
    }

    if ($primaryProcess.HasExited -or $primaryProcess.MainWindowHandle -eq 0) {
        throw 'The primary application stopped or lost its main window after the second launch.'
    }

    [pscustomobject]@{
        Executable = $executablePath
        PrimaryProcessId = $primaryProcess.Id
        PrimaryWindowHandle = $primaryWindowHandle
        SecondaryExitCode = $secondaryProcess.ExitCode
        SingleInstanceHandoff = $true
    } | Format-List
}
finally {
    if ($primaryProcess -and -not $primaryProcess.HasExited) {
        Stop-Process -Id $primaryProcess.Id -Force -ErrorAction SilentlyContinue
        for ($attempt = 0; $attempt -lt 30; $attempt++) {
            if (-not (Get-Process -Id $primaryProcess.Id -ErrorAction SilentlyContinue)) {
                break
            }

            Start-Sleep -Milliseconds 100
        }
    }
}
