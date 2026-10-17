#include <windows.h>
#include <shellapi.h>
#include <shobjidl.h>
#include <string>

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    int count{};
    auto args = CommandLineToArgvW(GetCommandLineW(), &count);
    if (!args || (count != 2 && count != 3)) { if (args) LocalFree(args); return 1; }
    std::wstring application(args[1]);
    const bool show = count == 3 && std::wstring(args[2]) == L"--show";
    const bool valid = count == 2 || show;
    LocalFree(args);
    if (!valid) return 1;
    if (!application.starts_with(L"CRYS74L.ReTray_") ||
        !application.ends_with(L"!App")) return 1;
    if (FAILED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED))) return 1;
    IApplicationActivationManager* manager{};
    auto result = CoCreateInstance(CLSID_ApplicationActivationManager, nullptr,
        CLSCTX_INPROC_SERVER, IID_IApplicationActivationManager,
        reinterpret_cast<void**>(&manager));
    if (SUCCEEDED(result)) {
        DWORD pid{};
        // Desktop/taskbar launches must keep the same package activation and
        // storage identity as Start. Only the logon task starts in background.
        result = manager->ActivateApplication(application.c_str(), show ? L"" : L"--background", AO_NONE, &pid);
        manager->Release();
    }
    CoUninitialize();
    return SUCCEEDED(result) ? 0 : 1;
}
