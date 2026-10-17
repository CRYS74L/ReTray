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
    if (pendingSequence_ && shared) {
        InterlockedExchange(&shared->cancelledSequence, pendingSequence_);
        RetireAcceptedIcons();
        if (!ReceiverFinishedWithIcons()) {
            // The receiver may be between cancellation check and CopyIcon.
            // Retain this mapping and the sender-owned handle copies; a later
            // reset/attach poll reclaims them after the explicit handoff.
            resetPending_ = true;
            if (!detachPosted_ && window_)
                detachPosted_ = PostMessageW(window_, RegisterWindowMessageW(CommandMessage), 1, 0) != FALSE;
            return;
        }
    }
    Detach();
    heldIcons_.clear(); pendingSequence_ = 0;
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
    if (explorerProcess_) { CloseHandle(explorerProcess_); explorerProcess_ = nullptr; }
    resetPending_ = detachPosted_ = false;
}
bool BridgeClient::Alive() const {
    return !resetPending_ && shared && shared->status == 1 && IsWindow(window_) &&
           FindWindowW(L"Shell_TrayWnd", nullptr) == window_;
}
bool BridgeClient::Attach(const SymbolAdapter& adapter, const std::filesystem::path& path) {
    if (Alive())
        return true;
    Reset();
    if (resetPending_) { lastError_ = ERROR_BUSY; return false; }
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
    explorerProcess_ = OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!explorerProcess_) return false;
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
bool BridgeClient::ReceiverFinishedWithIcons() const {
    return heldIcons_.empty() || (explorerProcess_ && WaitForSingleObject(explorerProcess_, 0) == WAIT_OBJECT_0) ||
        (shared && pendingSequence_ && (shared->replySequence == pendingSequence_ ||
          (pendingBatch_ && shared->acceptedSequence == pendingSequence_)));
}
void BridgeClient::RetireAcceptedIcons() {
    if (ReceiverFinishedWithIcons()) heldIcons_.clear();
}
bool BridgeClient::MailboxAvailable() {
    RetireAcceptedIcons();
    if (pendingSequence_) {
        if (!shared || shared->replySequence != pendingSequence_) { lastError_ = ERROR_BUSY; return false; }
        pendingSequence_ = 0;
    }
    if (resetPending_ || !shared || shared->status != 1) { lastError_ = ERROR_NOT_READY; return false; }
    return true;
}
bool BridgeClient::Command(BridgeCommand command, DWORD timeout) {
    if (!MailboxAvailable()) return false;
    if (command.replacementIcon) {
        auto copy = CopyIcon(reinterpret_cast<HICON>(command.replacementIcon));
        if (!copy) { lastError_ = ERROR_INVALID_HANDLE; return false; }
        heldIcons_.emplace_back(copy, DestroyIcon);
        command.replacementIcon = reinterpret_cast<uint64_t>(copy);
    }
    pendingBatch_ = false;
    return Send(command, timeout);
}
bool BridgeClient::Send(BridgeCommand command, DWORD timeout) {
    shared->command = command;
    shared->lastError = 0;
    auto sequence = InterlockedIncrement(&shared->requestSequence);
    pendingSequence_ = sequence;
    if (!PostMessageW(window_, RegisterWindowMessageW(CommandMessage), 0, 0)) {
        lastError_ = GetLastError(); heldIcons_.clear(); pendingSequence_ = 0; return false;
    }
    auto deadline = GetTickCount64() + timeout;
    while (GetTickCount64() < deadline && shared->replySequence != sequence)
        Sleep(5);
    if (shared->replySequence != sequence) {
        InterlockedExchange(&shared->cancelledSequence, sequence);
        lastError_ = ERROR_TIMEOUT; RetireAcceptedIcons(); return false;
    }
    lastError_ = shared->lastError; heldIcons_.clear(); pendingSequence_ = 0;
    return lastError_ == 0;
}
bool BridgeClient::CommandBatch(const std::vector<BridgeCommand>& commands, std::vector<DWORD>& errors,
                                DWORD timeout) {
    errors.clear();
    if (commands.empty()) return true;
    if (!MailboxAvailable()) return false;
    if (!Alive() || commands.size() > MaxBridgeIcons) { lastError_ = ERROR_INVALID_PARAMETER; return false; }
    shared->batchCount = static_cast<uint32_t>(commands.size());
    shared->batchReplyCount = 0;
    std::copy(commands.begin(), commands.end(), shared->batch);
    for (size_t index = 0; index < commands.size(); ++index) {
        auto& command = shared->batch[index];
        if (command.operation != BridgeOperation::Policy || !(command.clickEvent & PolicyReplaceImage)) continue;
        auto copy = CopyIcon(reinterpret_cast<HICON>(command.replacementIcon));
        heldIcons_.emplace_back(copy, DestroyIcon);
        command.replacementIcon = reinterpret_cast<uint64_t>(copy);
    }
    std::fill_n(shared->batchErrors, commands.size(), ERROR_NOT_READY);
    BridgeCommand envelope; envelope.operation = BridgeOperation::PolicyBatch;
    pendingBatch_ = true;
    if (!Send(envelope, timeout)) return false;
    MemoryBarrier();
    if (shared->batchReplyCount != commands.size()) { lastError_ = ERROR_INVALID_DATA; return false; }
    errors.assign(shared->batchErrors, shared->batchErrors + commands.size());
    return true;
}
std::vector<BridgeIcon> BridgeClient::Snapshot(bool* complete) const {
    if (complete)
        *complete = false;
    if (!shared)
        return {};
    for (int attempt = 0; attempt < 4; ++attempt) {
        auto before = shared->snapshotSequence;
        if (before & 1) {
            Sleep(1);
            continue;
        }
        uint32_t count = shared->count;
        if (count > MaxBridgeIcons)
            return {};
        std::vector<BridgeIcon> rows(shared->icons, shared->icons + count);
        MemoryBarrier();
        if (before == shared->snapshotSequence) {
            if (complete)
                *complete = true;
            return rows;
        }
    }
    return {};
}
void BridgeClient::Detach() {
    if (pendingSequence_ && shared && shared->replySequence != pendingSequence_) {
        InterlockedExchange(&shared->cancelledSequence, pendingSequence_);
        if (!detachPosted_ && window_)
            detachPosted_ = PostMessageW(window_, RegisterWindowMessageW(CommandMessage), 1, 0) != FALSE;
    }
    if (Alive()) {
        BridgeCommand command;
        command.operation = BridgeOperation::Detach;
        if (!Command(command, 500) && !detachPosted_ && window_)
            detachPosted_ = PostMessageW(window_, RegisterWindowMessageW(CommandMessage), 1, 0) != FALSE;
    }
    if (hook_) {
        UnhookWindowsHookEx(hook_);
        hook_ = nullptr;
    }
}
} // namespace tray
