#include "bridge_client.h"
#include <sddl.h>
#include <thread>
namespace tray {
static PSECURITY_DESCRIPTOR UserSecurity() {
    HANDLE token{};
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_QUERY, &token))
        return nullptr;
    DWORD needed{};
    GetTokenInformation(token, TokenUser, nullptr, 0, &needed);
    std::vector<uint8_t> info(needed);
    if (!GetTokenInformation(token, TokenUser, info.data(), needed, &needed)) {
        CloseHandle(token);
        return nullptr;
    }
    CloseHandle(token);
    LPWSTR sid{};
    if (!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(info.data())->User.Sid, &sid))
        return nullptr;
    std::wstring sddl = L"D:P(A;;GA;;;" + std::wstring(sid) + L")";
    LocalFree(sid);
    PSECURITY_DESCRIPTOR sd{};
    ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(), SDDL_REVISION_1, &sd,
                                                         nullptr);
    return sd;
}
BridgeClient::~BridgeClient() {
    Reset();
}
void BridgeClient::Reset() {
    Detach();
    if (shared) {
        UnmapViewOfFile(shared);
        shared = nullptr;
    }
    if (mapping_) {
        CloseHandle(mapping_);
        mapping_ = nullptr;
    }
    if (dll_) {
        FreeLibrary(dll_);
        dll_ = nullptr;
    }
    window_ = nullptr;
}
bool BridgeClient::Alive() const {
    return shared && shared->status == 1 && IsWindow(window_) &&
           FindWindowW(L"Shell_TrayWnd", nullptr) == window_;
}
bool BridgeClient::Attach(const SymbolAdapter& adapter, const std::filesystem::path& path) {
    if (Alive())
        return true;
    Reset();
    DWORD session{};
    ProcessIdToSessionId(GetCurrentProcessId(), &session);
    auto sd = UserSecurity();
    SECURITY_ATTRIBUTES security{sizeof(security), sd, FALSE};
    if (!sd)
        return false;
    mapping_ = CreateFileMappingW(INVALID_HANDLE_VALUE, &security, PAGE_READWRITE, 0,
                                  sizeof(BridgeState), MappingName(session).c_str());
    auto createError = GetLastError();
    if (sd)
        LocalFree(sd);
    if (!mapping_ || createError == ERROR_ALREADY_EXISTS) {
        if (mapping_) {
            CloseHandle(mapping_);
            mapping_ = nullptr;
        }
        return false;
    }
    shared = static_cast<BridgeState*>(
        MapViewOfFile(mapping_, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(BridgeState)));
    if (!shared)
        return false;
    memset(shared, 0, sizeof(BridgeState));
    shared->magic = BridgeMagic;
    shared->schema = BridgeSchema;
    shared->byteSize = sizeof(BridgeState);
    shared->agentPid = GetCurrentProcessId();
    shared->sessionId = session;
    shared->epoch = GetTickCount64();
    shared->adapter = Compact(adapter);
    FILETIME start{}, exit{}, kernel{}, user{};
    GetProcessTimes(GetCurrentProcess(), &start, &exit, &kernel, &user);
    shared->agentCreatedAt =
        (static_cast<uint64_t>(start.dwHighDateTime) << 32) | start.dwLowDateTime;
    window_ = FindWindowW(L"Shell_TrayWnd", nullptr);
    DWORD pid{};
    auto thread = GetWindowThreadProcessId(window_, &pid);
    if (!thread)
        return false;
    dll_ = LoadLibraryW(path.c_str());
    if (!dll_)
        return false;
    auto entry = reinterpret_cast<HOOKPROC>(GetProcAddress(dll_, "TrayBootstrap"));
    if (!entry)
        return false;
    hook_ = SetWindowsHookExW(WH_CALLWNDPROC, entry, dll_, thread);
    if (!hook_)
        return false;
    DWORD_PTR reply{};
    SendMessageTimeoutW(window_, RegisterWindowMessageW(InstallMessage), 0, 0, SMTO_ABORTIFHUNG,
                        2000, &reply);
    auto deadline = GetTickCount64() + 2000;
    while (GetTickCount64() < deadline && !shared->status && !shared->lastError)
        Sleep(10);
    bool ready = shared->status == 1;
    if (hook_) {
        UnhookWindowsHookEx(hook_);
        hook_ = nullptr;
    }
    return ready;
}
bool BridgeClient::Command(BridgeCommand command, DWORD timeout) {
    if (!shared || shared->status != 1)
        return false;
    shared->command = command;
    shared->lastError = 0;
    auto sequence = InterlockedIncrement(&shared->requestSequence);
    PostMessageW(window_, RegisterWindowMessageW(CommandMessage), 0, 0);
    auto deadline = GetTickCount64() + timeout;
    while (GetTickCount64() < deadline && shared->replySequence != sequence)
        Sleep(5);
    return shared->replySequence == sequence && shared->lastError == 0;
}
std::vector<BridgeIcon> BridgeClient::Snapshot() const {
    if (!shared)
        return {};
    for (int attempt = 0; attempt < 4; ++attempt) {
        auto before = shared->snapshotSequence;
        if (before & 1) {
            Sleep(1);
            continue;
        }
        uint32_t count = std::min<uint32_t>(MaxBridgeIcons, shared->count);
        std::vector<BridgeIcon> rows(shared->icons, shared->icons + count);
        MemoryBarrier();
        if (before == shared->snapshotSequence)
            return rows;
    }
    return {};
}
void BridgeClient::Detach() {
    if (Alive()) {
        BridgeCommand command;
        command.operation = BridgeOperation::Detach;
        Command(command, 500);
    }
    if (hook_) {
        UnhookWindowsHookEx(hook_);
        hook_ = nullptr;
    }
}
} // namespace tray
