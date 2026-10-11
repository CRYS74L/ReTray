param([ValidateSet('CoreTests','Probe','Agent','Bridge','BridgeTest','BridgeControls','Source','All')][string]$Target='CoreTests',[string]$Filter='')
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
    & $clang @commonArgs (Join-Path $productRoot 'tests\core_tests.cpp') (Join-Path $productRoot 'native\core\types.cpp') (Join-Path $productRoot 'native\core\notify_patch.cpp') (Join-Path $productRoot 'native\core\rule_engine.cpp') (Join-Path $productRoot 'native\platform\software_bitmap.cpp') '-o' $exe '-luser32' '-lgdi32' '-lshell32' '-lole32' '-loleaut32' '-lruntimeobject'
    if ($LASTEXITCODE -ne 0) { throw 'Core test build failed' }
    & $exe $Filter
    exit $LASTEXITCODE
}
if ($Target -eq 'Probe') {
    & $clang @commonArgs '-municode' (Join-Path $productRoot 'native\platform\probe_main.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') (Join-Path $productRoot 'native\core\types.cpp') '-o' (Join-Path $outputRoot 'TrayAssistant.Probe.exe') '-luser32' '-lgdi32' '-lshell32' '-lole32'
    if ($LASTEXITCODE -ne 0) { throw 'Native probe build failed' }
    exit 0
}
if ($Target -eq 'BridgeTest') {
    & $clang @commonArgs '-municode' (Join-Path $productRoot 'tests\bridge_smoke.cpp') (Join-Path $productRoot 'native\shell\bridge_client.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') (Join-Path $productRoot 'native\core\types.cpp') '-o' (Join-Path $outputRoot 'BridgeSmoke.exe') '-luser32' '-lgdi32' '-lshell32' '-lole32' '-ladvapi32'
    if($LASTEXITCODE -ne 0){throw 'Bridge smoke build failed'}
    exit 0
}
if ($Target -eq 'Source') {
    & $clang @commonArgs '-municode' '-mwindows' (Join-Path $productRoot 'native\test-source\tray_source.cpp') '-o' (Join-Path $outputRoot 'TrayAssistant.TestSource.exe') '-luser32' '-lgdi32' '-lshell32'
    if($LASTEXITCODE -ne 0){throw 'Controlled source build failed'}
    exit 0
}
if ($Target -eq 'BridgeControls') {
    & $clang @commonArgs '-municode' (Join-Path $productRoot 'tests\bridge_control_tests.cpp') (Join-Path $productRoot 'native\shell\bridge_client.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') (Join-Path $productRoot 'native\core\types.cpp') '-o' (Join-Path $outputRoot 'BridgeControls.exe') '-luser32' '-lgdi32' '-lshell32' '-lole32' '-ladvapi32'
    if($LASTEXITCODE -ne 0){throw 'Bridge controls build failed'}
    exit 0
}
if ($Target -eq 'Bridge') {
    & $clang @commonArgs '-shared' (Join-Path $productRoot 'native\shell\bridge_entry.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') (Join-Path $productRoot 'native\platform\software_bitmap.cpp') (Join-Path $productRoot 'native\core\types.cpp') (Join-Path $productRoot 'native\core\notify_patch.cpp') '-o' (Join-Path $outputRoot 'TrayAssistant.ShellBridge.dll') '-luser32' '-lgdi32' '-lshell32' '-lcomctl32' '-lole32' '-loleaut32' '-lruntimeobject'
    if($LASTEXITCODE -ne 0){throw 'Bridge build failed'}
    exit 0
}
throw "Target $Target has not been implemented"
