#pragma once
#include <windows.h>
#include <filesystem>
#include <string>

namespace tray {
inline int OwnIconPixelSize() {
    auto taskbar = FindWindowW(L"Shell_TrayWnd", nullptr);
    UINT dpi = taskbar ? GetDpiForWindow(taskbar) : GetDpiForSystem();
    if (!dpi) dpi = 96;
    return GetSystemMetricsForDpi(SM_CXSMICON, dpi);
}
inline bool WindowsUsesDarkMode() {
    DWORD light = 1, bytes = sizeof(light);
    auto status = RegGetValueW(HKEY_CURRENT_USER,
        L"Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize",
        L"SystemUsesLightTheme", RRF_RT_REG_DWORD, nullptr, &light, &bytes);
    return status == ERROR_SUCCESS && light == 0;
}
inline std::filesystem::path OwnIconAsset(const std::filesystem::path& root,
                                         const std::wstring& custom, bool dark) {
    if (!custom.empty()) return custom;
    return root / L"Assets" / (dark ? L"TrayWhite.ico" : L"TrayBlack.ico");
}
}
