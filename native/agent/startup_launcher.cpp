#include <windows.h>
#include <shellapi.h>
#include <shobjidl.h>
#include <appmodel.h>
#include <filesystem>
#include <string>
#include <vector>

// Activate this exact installed layout when available. An extracted ZIP in a
// different folder runs its own payload instead of launching an unrelated copy.
static std::wstring InstalledApplication(const std::filesystem::path& root) {
    constexpr auto family = L"CRYS74L.ReTray_fh3g15a7mygna";
    UINT32 count{}, length{};
    if (GetPackagesByPackageFamily(family, &count, nullptr, &length, nullptr) != ERROR_INSUFFICIENT_BUFFER) return {};
    std::vector<PWSTR> names(count);
    std::vector<wchar_t> buffer(length);
    if (GetPackagesByPackageFamily(family, &count, names.data(), &length, buffer.data()) != ERROR_SUCCESS) return {};
    for (UINT32 i = 0; i < count; ++i) {
        UINT32 pathLength{};
        if (GetPackagePathByFullName(names[i], &pathLength, nullptr) != ERROR_INSUFFICIENT_BUFFER) continue;
        std::vector<wchar_t> path(pathLength);
        if (GetPackagePathByFullName(names[i], &pathLength, path.data()) != ERROR_SUCCESS) continue;
        std::error_code error;
        if (std::filesystem::equivalent(root, std::filesystem::path(path.data()), error) && !error)
            return std::wstring(family) + L"!App";
    }
    return {};
}

static int RunExtracted(const std::filesystem::path& root, bool background) {
    const auto executable = root / L"runtime" / L"ReTray.exe";
    std::wstring command = L"\"" + executable.wstring() + L"\"" + (background ? L" --background" : L"");
    STARTUPINFOW startup{}; startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    if (!CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE, 0, nullptr,
                        executable.parent_path().c_str(), &startup, &process)) {
        MessageBoxW(nullptr, L"无法启动 ReTray，请完整解压并保留 runtime 文件夹。", L"ReTray", MB_OK | MB_ICONERROR);
        return 1;
    }
    CloseHandle(process.hThread); CloseHandle(process.hProcess);
    return 0;
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    int count{};
    auto args = CommandLineToArgvW(GetCommandLineW(), &count);
    if (!args || count < 1 || count > 3) { if (args) LocalFree(args); return 1; }
    wchar_t module[32768]{};
    if (!GetModuleFileNameW(nullptr, module, std::size(module))) { LocalFree(args); return 1; }
    const auto root = std::filesystem::path(module).parent_path();
    const bool implicit = count == 1 || (count == 2 &&
        (std::wstring(args[1]) == L"--background" || std::wstring(args[1]) == L"--show"));
    std::wstring application = implicit ? InstalledApplication(root) : args[1];
    const bool show = implicit ? count == 1 || std::wstring(args[1]) == L"--show" :
        count == 3 && std::wstring(args[2]) == L"--show";
    const bool valid = implicit || count == 2 || show;
    LocalFree(args);
    if (!valid) return 1;
    if (implicit && application.empty()) return RunExtracted(root, !show);
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
