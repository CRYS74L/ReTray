$ErrorActionPreference='Stop'
$node=(Get-Command node -ErrorAction Stop).Source
if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'node_modules/sharp/package.json'))) {
    throw 'Missing SVG build dependency. Run: npm ci --prefix packaging'
}
& $node (Join-Path $PSScriptRoot 'build-assets.cjs')
if ($LASTEXITCODE -ne 0) { throw 'ReTray SVG icon build failed' }
