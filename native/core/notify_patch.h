#pragma once
#include "types.h"
#include <atomic>
#include <mutex>
#include <span>
namespace tray {
struct IconPatch {
    uint32_t operation{}, suppliedFlags{}, state{}, stateMask{}, callbackMessage{}, notifyVersion{};
    Frame frame;
    std::wstring tip;
};
struct WireEvent {
    bool supported{};
    IconIdentity identity;
    IconPatch patch;
    uint32_t iconHandle{};
    std::wstring module;
};
WireEvent DecodeWire(std::span<const uint8_t> bytes);
OperationResult ApplyPatch(NativeIconRecord& icon, const IconPatch& patch);
struct FrameBaseline {
    bool reliable{};
    uint64_t fingerprint{};
    uint32_t width{}, height{};
};
enum class FrameDecision { Native, CustomIdle, NativeOff, NativeDynamic, AwaitingBaseline };
FrameDecision ChooseFrame(const Frame&, const FrameBaseline&, bool enabled);
enum class SessionState {
    Starting,
    WaitingForShell,
    Ready,
    Reconnecting,
    UserStopping,
    SessionQuery,
    SessionEnding
};
struct SessionMachine {
    std::atomic<SessionState> state{SessionState::Starting}, beforeQuery{SessionState::Starting};
    std::atomic<bool> queryPending{};
    std::mutex transition;
};
bool HandleSessionQuery(SessionMachine&);
void HandleSessionEnd(SessionMachine&, bool ending);
bool ShouldRecover(const SessionMachine&);
void SetOperationalState(SessionMachine&, SessionState);
} // namespace tray
