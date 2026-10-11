#pragma once
#include "shared.h"
namespace tray {
class BridgeClient {
    HANDLE mapping_{};HHOOK hook_{};HMODULE dll_{};HWND window_{};
public:
    BridgeState* shared{};
    ~BridgeClient();
    bool Attach(const SymbolAdapter&,const std::filesystem::path& dll);
    bool Command(BridgeCommand command,DWORD timeout=2000);
    std::vector<BridgeIcon> Snapshot() const;
    void Detach();
};
}
