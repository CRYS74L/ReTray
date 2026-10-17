#include "../native/shell/bridge_client.h"
#include <cstdio>
#include <stdexcept>
HWND mailboxWindow{};
HWND WINAPI MailboxTestFindWindowW(LPCWSTR name, LPCWSTR title) {
    if (name && !_wcsicmp(name, L"Shell_TrayWnd")) return mailboxWindow;
    return FindWindowW(name, title);
}
#define FindWindowW MailboxTestFindWindowW
#include "../native/shell/bridge_client.cpp"
#undef FindWindowW
#define CHECK(x) do { if (!(x)) throw std::runtime_error(#x); } while (0)
namespace tray {
struct BridgeClientFixtureAccess {
    static void Initialize(BridgeClient& client, BridgeState* shared) {
        client.shared = shared; client.window_ = mailboxWindow;
        client.explorerProcess_ = OpenProcess(SYNCHRONIZE, FALSE, GetCurrentProcessId());
    }
};
}
int main() {
    WNDCLASSW cls{}; cls.hInstance = GetModuleHandleW(nullptr); cls.lpfnWndProc = DefWindowProcW;
    cls.lpszClassName = L"ReTray.IsolatedMailboxTest";
    if (!RegisterClassW(&cls)) return 1;
    mailboxWindow = CreateWindowW(cls.lpszClassName, L"", 0, 0, 0, 0, 0, HWND_MESSAGE,
                                  nullptr, cls.hInstance, nullptr);
    int failed{};
    try {
        auto state = std::make_unique<tray::BridgeState>(); state->status = 1;
        tray::BridgeClient client; tray::BridgeClientFixtureAccess::Initialize(client, state.get());
        tray::BridgeCommand policy; policy.operation = tray::BridgeOperation::Policy;
        policy.clickEvent = tray::PolicyReplaceImage; policy.identity.uid = 17;
        auto image = CopyIcon(LoadIconW(nullptr, IDI_APPLICATION)); CHECK(image);
        policy.replacementIcon = reinterpret_cast<uint64_t>(image);
        std::vector<DWORD> errors;
        CHECK(!client.CommandBatch({policy}, errors, 10));
        const auto sequence = state->requestSequence;
        tray::BridgeCommand next; next.operation = tray::BridgeOperation::Refresh;
        CHECK(!client.Command(next, 10));
        CHECK(state->requestSequence == sequence && state->command.operation == tray::BridgeOperation::PolicyBatch);
        DestroyIcon(image);
        ICONINFO info{};
        CHECK(GetIconInfo(reinterpret_cast<HICON>(state->batch[0].replacementIcon), &info));
        if (info.hbmColor) DeleteObject(info.hbmColor); if (info.hbmMask) DeleteObject(info.hbmMask);
        state->replySequence = sequence;
        std::puts("PASS timed-out mailbox cannot be reused and retains receiver icon handles");
        // A receiver accepting its own copy permits handle retirement, but
        // never permits another request to overwrite the still-running slot.
        policy.replacementIcon = reinterpret_cast<uint64_t>(CopyIcon(LoadIconW(nullptr, IDI_APPLICATION)));
        CHECK(!client.CommandBatch({policy}, errors, 10));
        DestroyIcon(reinterpret_cast<HICON>(policy.replacementIcon));
        auto offered = reinterpret_cast<HICON>(state->batch[0].replacementIcon);
        auto received = CopyIcon(offered); CHECK(received);
        const auto accepted = state->requestSequence;
        state->acceptedSequence = accepted;
        CHECK(!client.Command(next, 10) && client.LastError() == ERROR_BUSY);
        CHECK(state->requestSequence == accepted && state->command.operation == tray::BridgeOperation::PolicyBatch);
        CHECK(GetIconInfo(received, &info));
        if (info.hbmColor) DeleteObject(info.hbmColor); if (info.hbmMask) DeleteObject(info.hbmMask);
        CHECK(!GetIconInfo(offered, &info));
        DestroyIcon(received); state->replySequence = accepted;
        std::puts("PASS accepted icon ownership does not release the result-slot lease");
        policy.replacementIcon = reinterpret_cast<uint64_t>(CopyIcon(LoadIconW(nullptr, IDI_APPLICATION)));
        CHECK(!client.CommandBatch({policy}, errors, 10));
        DestroyIcon(reinterpret_cast<HICON>(policy.replacementIcon));
        offered = reinterpret_cast<HICON>(state->batch[0].replacementIcon);
        auto started = GetTickCount64(); client.Reset();
        CHECK(GetTickCount64() - started < 100 && client.shared == state.get());
        CHECK(!client.Alive());
        CHECK(GetIconInfo(offered, &info));
        if (info.hbmColor) DeleteObject(info.hbmColor); if (info.hbmMask) DeleteObject(info.hbmMask);
        state->acceptedSequence = state->requestSequence; state->replySequence = state->requestSequence;
        client.Reset(); CHECK(!client.shared);
        CHECK(!GetIconInfo(offered, &info));
        std::puts("PASS reset defers unaccepted handles without blocking, then reclaims after receiver confirmation");
    } catch (const std::exception& error) { ++failed; std::fprintf(stderr, "FAIL %s\n", error.what()); }
    DestroyWindow(mailboxWindow); UnregisterClassW(cls.lpszClassName, cls.hInstance);
    return failed ? 1 : 0;
}
