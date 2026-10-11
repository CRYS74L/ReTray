#pragma once
#include <sddl.h>
#include <string>
#include <vector>
#include <windows.h>
namespace tray {
inline PSECURITY_DESCRIPTOR CurrentUserSecurity() {
    HANDLE token{};
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token))
        return nullptr;
    DWORD size{};
    GetTokenInformation(token, TokenUser, nullptr, 0, &size);
    std::vector<uint8_t> bytes(size);
    if (!GetTokenInformation(token, TokenUser, bytes.data(), size, &size)) {
        CloseHandle(token);
        return nullptr;
    }
    CloseHandle(token);
    LPWSTR sid{};
    if (!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(bytes.data())->User.Sid, &sid))
        return nullptr;
    auto sddl = L"D:P(A;;GA;;;" + std::wstring(sid) + L")";
    LocalFree(sid);
    PSECURITY_DESCRIPTOR result{};
    ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(), SDDL_REVISION_1, &result,
                                                         nullptr);
    return result;
}
} // namespace tray
