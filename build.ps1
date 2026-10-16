param([ValidateSet('InputMethodLiveTests','CoreTests','BitmapTests','ProcessIdentityTests','SnapshotLookupTests','Probe','Runtime','Agent','AgentVisibilityTests','AgentSystemTests','Bridge','BridgeRegistrationTests','BridgeTest','BridgeControls','Source','SettingsTests','StartupTests','SystemControls','Stress','All')][string]$Target='CoreTests',[string]$Filter='')
$ErrorActionPreference='Stop'
$productRoot=$PSScriptRoot
$projectVersion=(Get-Content -LiteralPath (Join-Path $productRoot 'VERSION') -Raw).Trim()
$workspaceBase=Split-Path -Parent (Split-Path -Parent $productRoot)
$clang=Join-Path $workspaceBase '【Common】\LLVM-MinGW\llvm-mingw-20260922-ucrt-x86_64\bin\clang++.exe'
if (!(Test-Path -LiteralPath $clang)) { $clang=(Get-Command clang++.exe -ErrorAction Stop).Source }
$outputRoot=Join-Path $productRoot 'build'
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
Set-Content -LiteralPath (Join-Path $outputRoot 'product_version.h') -Value ('#define RETRAY_VERSION "'+$projectVersion+'"') -Encoding utf8
$commonArgs=@('-std=c++20','-O2','-static','-DUNICODE','-D_UNICODE','-DWIN32_LEAN_AND_MEAN','-DNOMINMAX','-D_WIN32_WINNT=0x0A00','-DNTDDI_VERSION=0x0A00000C','-Wall','-Wextra','-I',(Join-Path $productRoot 'native'))
$cppWinrt=Join-Path $workspaceBase '【Common】\WindowsSDK\10.0.28000.2705\microsoft.windows.sdk.cpp\c\Include\10.0.28000.0\cppwinrt'
$commonArgs+=@('-fms-extensions','-Wno-nonportable-include-path','-I',$cppWinrt)
$commonArgs+=@('-include',(Join-Path $outputRoot 'product_version.h'))
if ($Target -in @('Agent','Runtime','Probe')) {
    & (Join-Path $productRoot 'packaging/build-assets.ps1')
    $brandHeader=@(
        ('#define RETRAY_FILE_VERSION '+($projectVersion.Replace('.',','))+',0'),
        ('#define RETRAY_VERSION_TEXT "'+$projectVersion+'\0"'),
        ('#define RETRAY_ICON_PATH "'+((Join-Path $productRoot 'ui/ReTray.App/Assets/AppIcon.ico').Replace('\','/'))+'"')
    )
    Set-Content -LiteralPath (Join-Path $outputRoot 'native-brand.h') -Value $brandHeader -Encoding utf8
    $resource=Join-Path $outputRoot 'native-brand.res'
    $windres=Join-Path (Split-Path $clang -Parent) 'x86_64-w64-mingw32-windres.exe'
    & $windres '-I' $outputRoot '-i' (Join-Path $productRoot 'packaging/native-brand.rc') '-o' $resource '-O' 'coff'
    if($LASTEXITCODE -ne 0){throw 'ReTray native icon/version resource failed'}
    $commonArgs+=$resource
}
if ($Target -eq 'InputMethodLiveTests') {
    & $clang @commonArgs '-municode' (Join-Path $productRoot 'tests\input_method_live_tests.cpp') (Join-Path $productRoot 'native\platform\input_profiles.cpp') (Join-Path $productRoot 'native\shell\bridge_client.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') (Join-Path $productRoot 'native\core\types.cpp') '-o' (Join-Path $outputRoot 'InputMethodLiveTests.exe') '-luser32' '-lgdi32' '-lshell32' '-lole32' '-loleaut32' '-ladvapi32' '-luuid'
    if($LASTEXITCODE -ne 0){throw 'Input method live tests build failed'}
    exit 0
}
if ($Target -eq 'CoreTests') {
    $exe=Join-Path $outputRoot 'CoreTests.exe'
    & $clang @commonArgs (Join-Path $productRoot 'tests\core_tests.cpp') (Join-Path $productRoot 'native\core\types.cpp') (Join-Path $productRoot 'native\core\notify_patch.cpp') (Join-Path $productRoot 'native\core\rule_engine.cpp') (Join-Path $productRoot 'native\platform\software_bitmap.cpp') '-o' $exe '-luser32' '-lgdi32' '-lshell32' '-lole32' '-loleaut32' '-lruntimeobject' '-lbcrypt'
    if ($LASTEXITCODE -ne 0) { throw 'Core test build failed' }
    & $exe $Filter
    exit $LASTEXITCODE
}
if ($Target -eq 'ProcessIdentityTests') {
    $exe=Join-Path $outputRoot 'ProcessIdentityTests.exe'
    & $clang @commonArgs '-municode' (Join-Path $productRoot 'tests\process_identity_tests.cpp') '-o' $exe '-ladvapi32' '-lshell32'
    if($LASTEXITCODE -ne 0){throw 'Process identity tests build failed'}
    & $exe
    exit $LASTEXITCODE
}
if ($Target -eq 'SnapshotLookupTests') {
    $exe=Join-Path $outputRoot 'SnapshotLookupTests.exe'
    & $clang @commonArgs '-municode' (Join-Path $productRoot 'tests\native_snapshot_lookup_tests.cpp') (Join-Path $productRoot 'native\core\types.cpp') '-o' $exe '-luser32' '-lgdi32' '-lshell32' '-lole32'
    if($LASTEXITCODE -ne 0){throw 'Snapshot lookup test build failed'}
    & $exe
    exit $LASTEXITCODE
}
if ($Target -eq 'Probe') {
    & $clang @commonArgs '-municode' (Join-Path $productRoot 'native\platform\probe_main.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') (Join-Path $productRoot 'native\core\types.cpp') '-o' (Join-Path $outputRoot 'ReTray.Probe.exe') '-luser32' '-lgdi32' '-lshell32' '-lole32'
    if ($LASTEXITCODE -ne 0) { throw 'Native probe build failed' }
    exit 0
}
if ($Target -eq 'BridgeTest') {
    & $clang @commonArgs '-municode' (Join-Path $productRoot 'tests\bridge_smoke.cpp') (Join-Path $productRoot 'native\shell\bridge_client.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') (Join-Path $productRoot 'native\core\types.cpp') '-o' (Join-Path $outputRoot 'BridgeSmoke.exe') '-luser32' '-lgdi32' '-lshell32' '-lole32' '-ladvapi32'
    if($LASTEXITCODE -ne 0){throw 'Bridge smoke build failed'}
    exit 0
}
if ($Target -eq 'Source') {
    & $clang @commonArgs '-municode' '-mwindows' (Join-Path $productRoot 'native\test-source\tray_source.cpp') '-o' (Join-Path $outputRoot 'ReTray.TestSource.exe') '-luser32' '-lgdi32' '-lshell32'
    if($LASTEXITCODE -ne 0){throw 'Controlled source build failed'}
    exit 0
}
if ($Target -eq 'BridgeControls') {
    & $clang @commonArgs '-municode' (Join-Path $productRoot 'tests\bridge_control_tests.cpp') (Join-Path $productRoot 'native\shell\bridge_client.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') (Join-Path $productRoot 'native\core\types.cpp') '-o' (Join-Path $outputRoot 'BridgeControls.exe') '-luser32' '-lgdi32' '-lshell32' '-lole32' '-ladvapi32'
    if($LASTEXITCODE -ne 0){throw 'Bridge controls build failed'}
    exit 0
}
if ($Target -in @('Bridge','BridgeRegistrationTests','BitmapTests')) {
    $clangC=Join-Path (Split-Path $clang -Parent) 'clang.exe'
    $hookObjects=@()
    foreach($sourceName in @('buffer','hook','trampoline','hde\hde64')) {
        $source=Join-Path $productRoot ('third_party\minhook\src\'+$sourceName+'.c')
        $object=Join-Path $outputRoot ('minhook-'+($sourceName -replace '\\','-')+'.o')
        & $clangC '-O2' '-D_WIN32_WINNT=0x0A00' '-c' $source '-o' $object
        if($LASTEXITCODE -ne 0){throw 'MinHook build failed'}
        $hookObjects+=$object
    }
    $bridgeMode=if($Target -eq 'Bridge'){'-shared'}else{'-municode'}
    $bridgeSource=if($Target -eq 'Bridge'){'native\shell\bridge_entry.cpp'}elseif($Target -eq 'BitmapTests'){'tests\bitmap_tests.cpp'}else{'tests\bridge_registration_tests.cpp'}
    $bridgeOutput=Join-Path $outputRoot $(if($Target -eq 'Bridge'){'ReTray.ShellBridge.dll'}else{$Target+'.exe'})
    & $clang @commonArgs $bridgeMode (Join-Path $productRoot $bridgeSource) (Join-Path $productRoot 'native\shell\system_controls.cpp') (Join-Path $productRoot 'native\shell\input_method_logo.cpp') (Join-Path $productRoot 'native\platform\input_profiles.cpp') (Join-Path $productRoot 'native\shell\behavior_hooks.cpp') @hookObjects (Join-Path $productRoot 'native\platform\native_snapshot.cpp') (Join-Path $productRoot 'native\platform\software_bitmap.cpp') (Join-Path $productRoot 'native\core\types.cpp') (Join-Path $productRoot 'native\core\notify_patch.cpp') '-o' $bridgeOutput '-luser32' '-lgdi32' '-lshell32' '-lcomctl32' '-lole32' '-loleaut32' '-lruntimeobject' '-lbcrypt' '-luuid'
    if($LASTEXITCODE -ne 0){throw 'Bridge build failed'}
    if($Target -in @('BridgeRegistrationTests','BitmapTests')){& $bridgeOutput;exit $LASTEXITCODE}
    exit 0
}
if ($Target -eq 'SettingsTests') {
    & $clang @commonArgs '-municode' (Join-Path $productRoot 'tests\settings_tests.cpp') (Join-Path $productRoot 'native\agent\settings.cpp') (Join-Path $productRoot 'native\core\rule_engine.cpp') (Join-Path $productRoot 'native\core\types.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') '-o' (Join-Path $outputRoot 'SettingsTests.exe') '-luser32' '-lgdi32' '-lshell32' '-lole32' '-lgdiplus' '-lbcrypt'
    if($LASTEXITCODE -ne 0){throw 'Settings tests build failed'}
    & (Join-Path $outputRoot 'SettingsTests.exe') (Join-Path $productRoot 'artifacts\settings-fixture')
    exit $LASTEXITCODE
}
if ($Target -in @('AgentVisibilityTests','AgentSystemTests')) {
    $testSource=if($Target -eq 'AgentSystemTests'){'tests\agent_system_tests.cpp'}else{'tests\agent_visibility_tests.cpp'}
    $fixture=if($Target -eq 'AgentSystemTests'){'artifacts\agent-system-fixture'}else{'artifacts\agent-visibility-fixture'}
    $exe=Join-Path $outputRoot ($Target+'.exe')
    & $clang @commonArgs '-municode' (Join-Path $productRoot $testSource) (Join-Path $productRoot 'native\platform\input_profiles.cpp') (Join-Path $productRoot 'native\agent\settings.cpp') (Join-Path $productRoot 'native\core\rule_engine.cpp') (Join-Path $productRoot 'native\core\notify_patch.cpp') (Join-Path $productRoot 'native\core\types.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') '-o' $exe '-luser32' '-lgdi32' '-lshell32' '-lole32' '-loleaut32' '-lruntimeobject' '-lbcrypt' '-ladvapi32' '-lgdiplus' '-luuid' '-lversion'
    if($LASTEXITCODE -ne 0){throw "$Target build failed"}
    & $exe (Join-Path $productRoot $fixture)
    exit $LASTEXITCODE
}
if ($Target -eq 'StartupTests') {
    $exe=Join-Path $outputRoot 'StartupTests.exe'
    & $clang @commonArgs '-municode' (Join-Path $productRoot 'tests\startup_tests.cpp') '-o' $exe '-lole32' '-loleaut32' '-ladvapi32' '-luuid' '-lruntimeobject' '-lshell32' '-lversion'
    if($LASTEXITCODE -ne 0){throw 'Startup tests build failed'}
    & $exe
    exit $LASTEXITCODE
}
if ($Target -in @('Agent','Runtime')) {
    & $clang @commonArgs '-municode' '-mwindows' (Join-Path $productRoot 'native\agent\startup_launcher.cpp') '-o' (Join-Path $outputRoot 'ReTray.Startup.exe') '-lole32' '-lshell32' '-luuid'
    if($LASTEXITCODE -ne 0){throw 'Startup launcher build failed'}
    # Agent remains an isolated executable fixture for native acceptance tests.
    # Production builds host Runtime inside the one ReTray.exe process.
    $nativeMode=@('-municode','-mwindows')
    $nativeOutput=Join-Path $outputRoot 'ReTray.Agent.exe'
    $nativeArgs=$commonArgs
    if($Target -eq 'Runtime') {
        $runtimeResource=Join-Path $outputRoot 'runtime-brand.res'
        & $windres '-DRETRAY_NATIVE_DLL' '-I' $outputRoot '-i' (Join-Path $productRoot 'packaging/native-brand.rc') '-o' $runtimeResource '-O' 'coff'
        if($LASTEXITCODE -ne 0){throw 'ReTray runtime version resource failed'}
        $nativeArgs=@($commonArgs | Where-Object {$_ -ne $resource})+$runtimeResource
        $nativeMode=@('-shared','-DRETRAY_EMBEDDED_RUNTIME')
        $nativeOutput=Join-Path $outputRoot 'ReTray.Runtime.dll'
    }
    & $clang @nativeArgs @nativeMode (Join-Path $productRoot 'native\agent\main.cpp') (Join-Path $productRoot 'native\platform\input_profiles.cpp') (Join-Path $productRoot 'native\agent\settings.cpp') (Join-Path $productRoot 'native\core\rule_engine.cpp') (Join-Path $productRoot 'native\core\notify_patch.cpp') (Join-Path $productRoot 'native\core\types.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') (Join-Path $productRoot 'native\shell\bridge_client.cpp') '-o' $nativeOutput '-luser32' '-lgdi32' '-lshell32' '-lole32' '-loleaut32' '-lruntimeobject' '-lbcrypt' '-ladvapi32' '-lgdiplus' '-luuid' '-lversion'
    if($LASTEXITCODE -ne 0){throw "$Target build failed"}
    Copy-Item -LiteralPath (Join-Path $productRoot 'native\platform\supported-adapters.json') -Destination $outputRoot -Force
    exit 0
}
if ($Target -eq 'SystemControls') {
    & $clang @commonArgs '-municode' (Join-Path $productRoot 'tests\system_controls.cpp') (Join-Path $productRoot 'native\shell\bridge_client.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') (Join-Path $productRoot 'native\core\types.cpp') '-o' (Join-Path $outputRoot 'SystemControls.exe') '-luser32' '-lgdi32' '-lshell32' '-lole32' '-ladvapi32'
    if($LASTEXITCODE -ne 0){throw 'System controls tests build failed'}
    & (Join-Path $outputRoot 'SystemControls.exe') (Join-Path $productRoot 'native\platform\supported-adapters.json') (Join-Path $outputRoot 'ReTray.ShellBridge.dll')
    exit $LASTEXITCODE
}
if ($Target -eq 'Stress') {
    & $clang @commonArgs '-municode' (Join-Path $productRoot 'tests\bridge_stress.cpp') (Join-Path $productRoot 'native\shell\bridge_client.cpp') (Join-Path $productRoot 'native\platform\native_snapshot.cpp') (Join-Path $productRoot 'native\core\types.cpp') '-o' (Join-Path $outputRoot 'BridgeStress.exe') '-luser32' '-lgdi32' '-lshell32' '-lole32' '-ladvapi32'
    if($LASTEXITCODE -ne 0){throw 'Stress tests build failed'}
    exit 0
}
throw "Target $Target has not been implemented"
