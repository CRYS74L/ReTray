#pragma once
#include <windows.h>
#include <shellapi.h>
#include <filesystem>
#include <optional>
#include <string>
#include <vector>

namespace tray {
inline std::optional<std::filesystem::path> LegacyRunExecutable(const std::wstring& command) {
    int count{};
    auto args = CommandLineToArgvW(command.c_str(), &count);
    if (!args) return {};
    std::optional<std::filesystem::path> result;
    if (count == 2 && !_wcsicmp(args[1], L"--background")) {
        auto path = std::filesystem::path(args[0]);
        if (path.is_absolute() && !_wcsicmp(path.filename().c_str(), L"TrayAssistant.Agent.exe"))
            result = path;
    }
    LocalFree(args);
    return result;
}
inline bool LegacyProductIdentity(const std::filesystem::path& app) {
    DWORD ignored{}, length = GetFileVersionInfoSizeW(app.c_str(), &ignored);
    if (!length || length > 1024 * 1024) return false;
    std::vector<BYTE> data(length);
    if (!GetFileVersionInfoW(app.c_str(), 0, length, data.data())) return false;
    struct Translation { WORD language, codePage; };
    Translation* translations{}; UINT bytes{};
    if (!VerQueryValueW(data.data(), L"\\VarFileInfo\\Translation",
                        reinterpret_cast<void**>(&translations), &bytes)) return false;
    for (UINT i = 0; i < bytes / sizeof(Translation); ++i) {
        wchar_t query[96]{};
        swprintf_s(query, std::size(query), L"\\StringFileInfo\\%04x%04x\\ProductName",
                   translations[i].language, translations[i].codePage);
        wchar_t* product{}; UINT size{};
        if (VerQueryValueW(data.data(), query, reinterpret_cast<void**>(&product), &size) &&
            size && product && !wcscmp(product, L"\u6258\u76d8\u56fe\u6807\u52a9\u624b"))
            return true;
    }
    return false;
}
inline DWORD RetireLegacyRunStartup() {
    HKEY key{};
    auto opened = RegOpenKeyExW(HKEY_CURRENT_USER,
        L"Software\\Microsoft\\Windows\\CurrentVersion\\Run", 0,
        KEY_QUERY_VALUE | KEY_SET_VALUE, &key);
    if (opened == ERROR_FILE_NOT_FOUND) return ERROR_SUCCESS;
    if (opened != ERROR_SUCCESS) return opened;
    wchar_t command[32768]{}; DWORD bytes = sizeof(command), type{};
    auto status = RegQueryValueExW(key, L"TrayIconAssistant", nullptr, &type,
                                   reinterpret_cast<BYTE*>(command), &bytes);
    if (status == ERROR_SUCCESS && type == REG_SZ && bytes >= sizeof(wchar_t) &&
        bytes <= sizeof(command) && command[bytes / sizeof(wchar_t) - 1] == 0) {
        auto executable = LegacyRunExecutable(command);
        // The old UI executable has the old product's version resource even in
        // unpackaged layouts. A matching filename alone is not ownership proof.
        if (executable && LegacyProductIdentity(executable->parent_path() / L"TrayAssistant.App.exe"))
            status = RegDeleteValueW(key, L"TrayIconAssistant");
        else status = ERROR_SUCCESS;
    } else if (status == ERROR_FILE_NOT_FOUND || status == ERROR_MORE_DATA || status == ERROR_SUCCESS)
        status = ERROR_SUCCESS; // Preserve foreign values and unsupported formats.
    RegCloseKey(key);
    return status == ERROR_FILE_NOT_FOUND ? ERROR_SUCCESS : status;
}
}
