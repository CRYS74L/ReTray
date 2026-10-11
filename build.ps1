param([ValidateSet('CoreTests','Probe','Agent','AgentVisibilityTests','Bridge','BridgeTest','BridgeControls','Source','SettingsTests','SystemControls','Stress','All')][string]$Target='CoreTests',[string]$Filter='')
$ErrorActionPreference='Stop'
$productRoot=$PSScriptRoot
$projectVersion=(Get-Content -LiteralPath (Join-Path $productRoot 'VERSION') -Raw).Trim()
$workspaceBase=Split-Path -Parent (Split-Path -Parent $productRoot)
$clang=Join-Path $workspaceBase '【Common】\LLVM-MinGW\llvm-mingw-20260922-ucrt-x86_64\bin\clang++.exe'
if (!(Test-Path -LiteralPath $clang)) { $clang=(Get-Command clang++.exe -ErrorAction Stop).Source }
$outputRoot=Join-Path $productRoot 'build'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
Set-Content -LiteralPath (Join-Path $outputRoot 'product_version.h') -Value ('#define TRAY_ASSISTANT_VERSION "'+$projectVersion+'"') -Encoding utf8
$commonArgs=@('-std=c++20','-O2','-static','-DUNICODE','-D_UNICODE','-DWIN32_LEAN_AND_MEAN','-DNOMINMAX','-D_WIN32_WINNT=0x0A00','-DNTDDI_VERSION=0x0A00000C','-Wall','-Wextra','-I',(Join-Path $productRoot 'native'))
$cppWinrt=Join-Path $workspaceBase '【Common】\WindowsSDK\10.0.28000.2705\microsoft.windows.sdk.cpp\c\Include\10.0.28000.0\cppwinrt'
$commonArgs+=@('-fms-extensions','-Wno-nonportable-include-path','-I',$cppWinrt)
$commonArgs+=@('-include',(Join-Path $outputRoot 'product_version.h'))
if ($Target -eq 'CoreTests') {
    $exe=Join-Path $outputRoot 'CoreTests.exe'
    & $clang @commonArgs (Join-Path $productRoot 'tests\core_tests.cpp') (Join-Path $productRoot 'native\core\types.cpp') (Join-Path $productRoot 'native\core\notify_patch.cpp') (Join-Path $productRoot 'native\core\rule_engine.cpp') (Join-Path $productRoot 'native\platform\software_bitmap.cpp') '-o' $exe '-luser32' '-lgdi32' '-lshell32' '-lole32' '-loleaut32' '-lruntimeobject' '-lbcrypt'
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
    $clangC=Join-Path (Split-Path $clang -Parent) 'clang.exe'
    $hookObjects=@()
    foreach($sourceName in @('buffer','hook','trampoline','hde\hde64')) {
        $source=Join-Path $productRoot ('third_party\minhook\src\'+$sourceName+'.c')
        $object=Join-Path $outputRoot ('minhook-'+($sourceName -replace '\\','-')+'.o')
        & $clangC '-O2' '-D_WIN32_WINNT=0x0A00' '-c' $source '-o' $object
        if($LASTEXITCODE -ne 0){throw 'MinHook build failed'}
        $hookObjects+=$object
    }
    & $clang @commonArgs '-shared' (Join-Path $productRoot 'native\shell\bridge_entry.cpp') (Join-Path $productRoot 'native\shell\system_controls.cpp') (Join-Path $productRoot 'native\shell\behavior_hooks.cpp') @hookObjects (Join-Path $productRoot 'native\platform\native_snapshot.cpp') (Join-Path $productRoot 'native\platform\software_bitmap.cpp') (Join-Path $productRoot 'native\core\types.cpp') (Join-Path $productRoot 'native\core\notify_patch.cpp') '-o' (Join-Path $outputRoot 'TrayAssistant.ShellBridge.dll') '-luser32' '-lgdi32' '-lshell32' '-lcomctl32' '-lole32' '-loleaut32' '-lruntimeobject' '-lbcrypt'
    if($LASTEXITCODE -ne 0){throw 'Bridge build failed'}
    exit 0
}
if ($Target -eq 'SettingsTests') {
    & $clang @commonArgs '-municode' (Join-Path $productRoot 'tests\settings_tests.cpp') (Join-Path $productRoot 'native\agent\settings.cpp') (Join-Path $productRoot 'native\core\rule_engine.cpp') (Join-Path $productRoot 'native\core\types.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') '-o' (Join-Path $outputRoot 'SettingsTests.exe') '-luser32' '-lgdi32' '-lshell32' '-lole32' '-lgdiplus' '-lbcrypt'
    if($LASTEXITCODE -ne 0){throw 'Settings tests build failed'}
    & (Join-Path $outputRoot 'SettingsTests.exe') (Join-Path $productRoot 'artifacts\settings-fixture')
    exit $LASTEXITCODE
}
if ($Target -eq 'AgentVisibilityTests') {
    $exe=Join-Path $outputRoot 'AgentVisibilityTests.exe'
    & $clang @commonArgs '-municode' (Join-Path $productRoot 'tests\agent_visibility_tests.cpp') (Join-Path $productRoot 'native\agent\settings.cpp') (Join-Path $productRoot 'native\core\rule_engine.cpp') (Join-Path $productRoot 'native\core\notify_patch.cpp') (Join-Path $productRoot 'native\core\types.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') '-o' $exe '-luser32' '-lgdi32' '-lshell32' '-lole32' '-loleaut32' '-lruntimeobject' '-lbcrypt' '-ladvapi32' '-lgdiplus' '-luuid'
    if($LASTEXITCODE -ne 0){throw 'Agent visibility tests build failed'}
    & $exe (Join-Path $productRoot 'artifacts\agent-visibility-fixture')
    exit $LASTEXITCODE
}
if ($Target -eq 'Agent') {
    & $clang @commonArgs '-municode' '-mwindows' (Join-Path $productRoot 'native\agent\main.cpp') (Join-Path $productRoot 'native\agent\settings.cpp') (Join-Path $productRoot 'native\core\rule_engine.cpp') (Join-Path $productRoot 'native\core\notify_patch.cpp') (Join-Path $productRoot 'native\core\types.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') (Join-Path $productRoot 'native\shell\bridge_client.cpp') '-o' (Join-Path $outputRoot 'TrayAssistant.Agent.exe') '-luser32' '-lgdi32' '-lshell32' '-lole32' '-loleaut32' '-lruntimeobject' '-lbcrypt' '-ladvapi32' '-lbcrypt' '-lgdiplus' '-lbcrypt' '-luuid'
    if($LASTEXITCODE -ne 0){throw 'Agent build failed'}
    Copy-Item -LiteralPath (Join-Path $productRoot 'native\platform\supported-adapters.json') -Destination $outputRoot -Force
    exit 0
}
if ($Target -eq 'SystemControls') {
    & $clang @commonArgs '-municode' (Join-Path $productRoot 'tests\system_controls.cpp') (Join-Path $productRoot 'native\shell\bridge_client.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') (Join-Path $productRoot 'native\core\types.cpp') '-o' (Join-Path $outputRoot 'SystemControls.exe') '-luser32' '-lgdi32' '-lshell32' '-lole32' '-ladvapi32'
    if($LASTEXITCODE -ne 0){throw 'System controls tests build failed'}
    & (Join-Path $outputRoot 'SystemControls.exe') (Join-Path $productRoot 'native\platform\supported-adapters.json') (Join-Path $outputRoot 'TrayAssistant.ShellBridge.dll')
    exit $LASTEXITCODE
}
if ($Target -eq 'Stress') {
    & $clang @commonArgs '-municode' (Join-Path $productRoot 'tests\bridge_stress.cpp') (Join-Path $productRoot 'native\shell\bridge_client.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') (Join-Path $productRoot 'native\core\types.cpp') '-o' (Join-Path $outputRoot 'BridgeStress.exe') '-luser32' '-lgdi32' '-lshell32' '-lole32' '-ladvapi32'
    if($LASTEXITCODE -ne 0){throw 'Stress tests build failed'}
    exit 0
}
throw "Target $Target has not been implemented"





