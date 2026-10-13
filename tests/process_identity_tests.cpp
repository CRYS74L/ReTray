#include "../native/platform/process_identity.h"
#include <aclapi.h>
#include <sddl.h>
#include <shellapi.h>
#include <cstdio>
#include <stdexcept>
#define CHECK(x) do { if (!(x)) throw std::runtime_error(#x); } while (0)
using namespace tray;
int wmain(int argc, wchar_t** argv) {
    if (argc == 4 && std::wstring(argv[1]) == L"--restricted") {
        auto ready = OpenEventW(EVENT_MODIFY_STATE, FALSE, argv[2]);
        auto stop = OpenEventW(SYNCHRONIZE, FALSE, argv[3]);
        if (!ready || !stop) return 2;
        HANDLE token{}; DWORD bytes{};
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token)) return 2;
        GetTokenInformation(token, TokenUser, nullptr, 0, &bytes);
        std::vector<uint8_t> data(bytes);
        if (!GetTokenInformation(token, TokenUser, data.data(), bytes, &bytes)) return 2;
        CloseHandle(token);
        wchar_t* sid{};
        if (!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(data.data())->User.Sid, &sid)) return 2;
        std::wstring acl = L"D:P(A;;0x00160001;;;" + std::wstring(sid) + L")";
        LocalFree(sid);
        PSECURITY_DESCRIPTOR descriptor{};
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(acl.c_str(), SDDL_REVISION_1,
                &descriptor, nullptr)) return 2;
        BOOL present{}, defaulted{}; PACL dacl{};
        if (!GetSecurityDescriptorDacl(descriptor, &present, &dacl, &defaulted)) return 2;
        auto result = SetSecurityInfo(GetCurrentProcess(), SE_KERNEL_OBJECT,
            DACL_SECURITY_INFORMATION | PROTECTED_DACL_SECURITY_INFORMATION, nullptr, nullptr,
            dacl, nullptr);
        LocalFree(descriptor);
        if (result != ERROR_SUCCESS) return 2;
        SetEvent(ready); auto waited = WaitForSingleObject(stop, 15000);
        CloseHandle(ready); CloseHandle(stop);
        return waited == WAIT_OBJECT_0 ? 0 : 2;
    }
    int failed{};
    try {
        std::wstring stale = L"C:\\StaleOwner.exe";
        CHECK(ReadOwnerInfo(UINT32_MAX, stale) == 0); CHECK(stale.empty());
        std::puts("PASS unavailable owner clears stale identity");
    } catch (const std::exception& error) { ++failed; std::fprintf(stderr, "FAIL %s\n", error.what()); }
    auto suffix = std::to_wstring(GetCurrentProcessId()) + L"." + std::to_wstring(GetTickCount64());
    auto readyName = L"Local\\TrayAssistant.OwnerTest.Ready." + suffix;
    auto stopName = L"Local\\TrayAssistant.OwnerTest.Stop." + suffix;
    auto ready = CreateEventW(nullptr, TRUE, FALSE, readyName.c_str());
    auto stop = CreateEventW(nullptr, TRUE, FALSE, stopName.c_str());
    PROCESS_INFORMATION child{};
    try {
        wchar_t path[32768]{}; GetModuleFileNameW(nullptr, path, std::size(path));
        auto command = L"\"" + std::wstring(path) + L"\" --restricted \"" + readyName + L"\" \"" + stopName + L"\"";
        STARTUPINFOW startup{}; startup.cb = sizeof(startup);
        CHECK(CreateProcessW(path, command.data(), nullptr, nullptr, FALSE, CREATE_NO_WINDOW,
                             nullptr, nullptr, &startup, &child));
        HANDLE waits[]{ready, child.hProcess};
        CHECK(WaitForMultipleObjects(2, waits, FALSE, 5000) == WAIT_OBJECT_0);
        SetLastError(0); auto denied = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, child.dwProcessId);
        if (denied) CloseHandle(denied);
        CHECK(!denied && GetLastError() == ERROR_ACCESS_DENIED);
        FILETIME c{}, e{}, k{}, u{}; CHECK(GetProcessTimes(child.hProcess, &c, &e, &k, &u));
        std::wstring executable;
        auto generation = ReadOwnerInfo(child.dwProcessId, executable);
        CHECK(generation == ((uint64_t(c.dwHighDateTime) << 32) | c.dwLowDateTime));
        CHECK(!_wcsicmp(executable.c_str(), path));
        std::puts("PASS denied-query owner uses generic system identity with correct path and generation");
        SetEvent(stop); CHECK(WaitForSingleObject(child.hProcess, 5000) == WAIT_OBJECT_0);
        executable = L"C:\\StaleOwner.exe";
        CHECK(!ReadOwnerInfo(child.dwProcessId, executable)); CHECK(executable.empty());
        std::puts("PASS exited restricted owner cannot retain old identity");
    } catch (const std::exception& error) { ++failed; std::fprintf(stderr, "FAIL %s\n", error.what()); }
    SetEvent(stop);
    if (child.hProcess) { WaitForSingleObject(child.hProcess, 16000); CloseHandle(child.hProcess); }
    if (child.hThread) CloseHandle(child.hThread);
    CloseHandle(stop); CloseHandle(ready);
    return failed ? 1 : 0;
}
