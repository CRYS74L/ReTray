#pragma once
#include "shared.h"
#include <memory>
namespace tray {
class BridgeClient {
    friend struct BridgeClientFixtureAccess;
    HANDLE mapping_{};
    HHOOK hook_{};
    HMODULE dll_{};
    HWND window_{};
    HANDLE explorerProcess_{};
    LONG pendingSequence_{};
    bool pendingBatch_{}, resetPending_{}, detachPosted_{};
    DWORD lastError_{};
    using HeldIcon = std::unique_ptr<std::remove_pointer_t<HICON>, decltype(&DestroyIcon)>;
    std::vector<HeldIcon> heldIcons_;
    bool MailboxAvailable();
    bool ReceiverFinishedWithIcons() const;
    void RetireAcceptedIcons();
    bool Send(BridgeCommand, DWORD timeout);

  public:
    BridgeState* shared{};
    ~BridgeClient();
    bool Attach(const SymbolAdapter&, const std::filesystem::path& dll);
    bool Command(BridgeCommand command, DWORD timeout = 2000);
    bool CommandBatch(const std::vector<BridgeCommand>& commands, std::vector<DWORD>& errors,
                      DWORD timeout = 2000);
    std::vector<BridgeIcon> Snapshot(bool* complete = nullptr) const;
    void Detach();
    void Reset();
    bool Alive() const;
    DWORD LastError() const { return lastError_; }
};
} // namespace tray
