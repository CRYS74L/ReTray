$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$projectPath = Join-Path $projectRoot 'src\LanDeviceMonitor.App\LanDeviceMonitor.App.csproj'
$projectXml = [xml](Get-Content -Raw -Encoding UTF8 -LiteralPath $projectPath)
$applicationName = [string]$projectXml.Project.PropertyGroup.AssemblyName
$distDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $projectRoot 'dist'))
$outputDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $distDirectory $applicationName))

$expectedPrefix = $distDirectory.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
if (-not $outputDirectory.StartsWith(
        $expectedPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean publish output outside dist: $outputDirectory"
}

if (Test-Path -LiteralPath $outputDirectory) {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

if (Test-Path -LiteralPath $localDotnet) {
    $env:DOTNET_ROOT = Split-Path -Parent $localDotnet
    $env:PATH = "$env:DOTNET_ROOT;$env:PATH"
}

dotnet restore $projectPath `
    --runtime win-x64 `
    --ignore-failed-sources `
    --disable-parallel `
    -p:NuGetAudit=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet restore failed with exit code $LASTEXITCODE"
}

dotnet publish $projectPath `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --no-restore `
    --output $outputDirectory `
    -p:PublishSingleFile=false `
    -p:UseSharedCompilation=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Output "Published to: $outputDirectory"
