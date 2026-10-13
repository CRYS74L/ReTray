#include <windows.h>
#include <shellapi.h>
#include <shobjidl.h>
#include <string>

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    int count{};
    auto args = CommandLineToArgvW(GetCommandLineW(), &count);
    if (!args || count != 2) { if (args) LocalFree(args); return 1; }
    std::wstring application(args[1]); LocalFree(args);
    if (!application.starts_with(L"CRYS74L.TrayIconAssistant_") ||
        !application.ends_with(L"!Background")) return 1;
    if (FAILED(CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED))) return 1;
    IApplicationActivationManager* manager{};
    auto result = CoCreateInstance(CLSID_ApplicationActivationManager, nullptr,
        CLSCTX_INPROC_SERVER, IID_IApplicationActivationManager,
        reinterpret_cast<void**>(&manager));
    if (SUCCEEDED(result)) {
        DWORD pid{};
        result = manager->ActivateApplication(application.c_str(), L"--background", AO_NONE, &pid);
        manager->Release();
    }
    CoUninitialize();
    return SUCCEEDED(result) ? 0 : 1;
}
