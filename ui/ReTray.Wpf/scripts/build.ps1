param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

if (Test-Path -LiteralPath $localDotnet) {
    $env:DOTNET_ROOT = Split-Path -Parent $localDotnet
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
}

if (-not $NoRestore) {
    $projectFiles = @(
        (Join-Path $projectRoot 'src\LanDeviceMonitor.App\LanDeviceMonitor.App.csproj'),
        (Join-Path $projectRoot 'tests\LanDeviceMonitor.Tests\LanDeviceMonitor.Tests.csproj')
    )

    foreach ($projectFile in $projectFiles) {
        dotnet restore $projectFile --ignore-failed-sources --disable-parallel -p:NuGetAudit=false
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed for $projectFile with exit code $LASTEXITCODE"
        }
    }
}

dotnet build (Join-Path $projectRoot 'LanDeviceMonitor.sln') --configuration $Configuration --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}
