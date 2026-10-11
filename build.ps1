param([ValidateSet('CoreTests','Probe','Agent','Bridge','Source','All')][string]$Target='CoreTests',[string]$Filter='')
$ErrorActionPreference='Stop'
$productRoot=$PSScriptRoot
$workspaceBase=Split-Path -Parent (Split-Path -Parent $productRoot)
$clang=Join-Path $workspaceBase '【Common】\LLVM-MinGW\llvm-mingw-20260922-ucrt-x86_64\bin\clang++.exe'
if (!(Test-Path -LiteralPath $clang)) { $clang=(Get-Command clang++.exe -ErrorAction Stop).Source }
$outputRoot=Join-Path $productRoot 'build'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$commonArgs=@('-std=c++20','-O2','-static','-DUNICODE','-D_UNICODE','-DWIN32_LEAN_AND_MEAN','-DNOMINMAX','-Wall','-Wextra','-I',(Join-Path $productRoot 'native'))
$cppWinrt=Join-Path $workspaceBase '【Common】\WindowsSDK\10.0.28000.2705\microsoft.windows.sdk.cpp\c\Include\10.0.28000.0\cppwinrt'
$commonArgs+=@('-fms-extensions','-Wno-nonportable-include-path','-I',$cppWinrt)
if ($Target -eq 'CoreTests') {
    $exe=Join-Path $outputRoot 'CoreTests.exe'
    & $clang @commonArgs (Join-Path $productRoot 'tests\core_tests.cpp') (Join-Path $productRoot 'native\core\types.cpp') (Join-Path $productRoot 'native\platform\software_bitmap.cpp') '-o' $exe '-luser32' '-lgdi32' '-lshell32' '-lole32' '-loleaut32' '-lruntimeobject'
    if ($LASTEXITCODE -ne 0) { throw 'Core test build failed' }
    & $exe $Filter
    exit $LASTEXITCODE
}
if ($Target -eq 'Probe') {
    & $clang @commonArgs '-municode' (Join-Path $productRoot 'native\platform\probe_main.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') (Join-Path $productRoot 'native\core\types.cpp') '-o' (Join-Path $outputRoot 'TrayAssistant.Probe.exe') '-luser32' '-lgdi32' '-lshell32' '-lole32'
    if ($LASTEXITCODE -ne 0) { throw 'Native probe build failed' }
    exit 0
}
throw "Target $Target has not been implemented"
