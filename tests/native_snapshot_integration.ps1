$ErrorActionPreference='Stop'
$productRoot=Split-Path -Parent $PSScriptRoot
& (Join-Path $productRoot 'build.ps1') -Target Probe
if($LASTEXITCODE -ne 0){throw 'Probe build failed'}
$work=Join-Path $productRoot 'artifacts\snapshot-tests'
New-Item -ItemType Directory -Force -Path $work | Out-Null
$exe=Join-Path $productRoot 'build\TrayAssistant.Probe.exe'
$adapterPath=Join-Path $productRoot 'native\platform\supported-adapters.json'
$out=Join-Path $work 'snapshot.json'
& $exe $adapterPath $out
if($LASTEXITCODE -ne 0){throw 'Real native snapshot failed'}
$snapshot=Get-Content -LiteralPath $out -Raw | ConvertFrom-Json
if(!$snapshot.success -or !$snapshot.complete){throw 'Snapshot incorrectly reported incomplete native read'}
$keys=@($snapshot.icons | ForEach-Object key)
if(@($keys | Select-Object -Unique).Count -ne $keys.Count){throw 'Distinct native identities were merged'}
$bad=Get-Content -LiteralPath $adapterPath -Raw | ConvertFrom-Json
$bad.adapters[0].age++
$badPath=Join-Path $work 'bad-age.json'
[System.IO.File]::WriteAllText($badPath,($bad|ConvertTo-Json -Depth 9),[System.Text.UTF8Encoding]::new($false))
& $exe $badPath
if($LASTEXITCODE -eq 0){throw 'Wrong PDB adapter was accepted'}
$bad=Get-Content -LiteralPath $adapterPath -Raw | ConvertFrom-Json
$bad.adapters[0].checks[0].bytes='00000000000000000000000000000000'
$badPath=Join-Path $work 'bad-code.json'
[System.IO.File]::WriteAllText($badPath,($bad|ConvertTo-Json -Depth 9),[System.Text.UTF8Encoding]::new($false))
& $exe $badPath
if($LASTEXITCODE -eq 0){throw 'Wrong code signature was accepted'}
Write-Host ('PASS real native snapshot + identity uniqueness + mismatched PDB/code rejection; live records: {0}' -f $snapshot.icons.Count)
