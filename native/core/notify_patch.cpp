#include "notify_patch.h"
#include <cstring>
namespace tray {
WireEvent DecodeWire(std::span<const uint8_t> bytes) {
    WireEvent event;
    if (bytes.size() < 0x5cc || bytes.size() > 65536)
        return event;
    auto d = [&](size_t offset) {
        uint32_t value{};
        memcpy(&value, bytes.data() + offset, 4);
        return value;
    };
    if (d(0) != 0x34753423 || d(8) < 0x3bc || d(4) > NIM_SETVERSION)
        return event;
    event.patch.operation = d(4);
    event.identity.ownerHwnd = d(0xc);
    event.identity.uid = d(0x10);
    event.patch.suppliedFlags = d(0x14);
    event.patch.callbackMessage = d(0x18);
    event.iconHandle = d(0x1c);
    event.patch.state = d(0x120);
    event.patch.stateMask = d(0x124);
    if (event.patch.operation == NIM_SETVERSION)
        event.patch.notifyVersion = d(0x328);
    memcpy(&event.identity.guid, bytes.data() + 0x3b0, 16);
    event.identity.hasGuid =
        (event.patch.suppliedFlags & NIF_GUID) && !IsEqualGUID(event.identity.guid, GUID{});
    auto text = [&](size_t offset, size_t limit) {
        std::wstring value;
        for (size_t i = 0; i < limit && offset + i * 2 + 2 <= bytes.size(); ++i) {
            wchar_t c;
            memcpy(&c, bytes.data() + offset + i * 2, 2);
            if (!c)
                break;
            value += c;
        }
        return value;
    };
    event.patch.tip = text(0x20, 128);
    event.module = text(0x3c4, 260);
    event.supported = true;
    return event;
}
OperationResult ApplyPatch(NativeIconRecord& record, const IconPatch& patch) {
    if (patch.operation == NIM_SETVERSION) {
        record.notifyVersion = patch.notifyVersion;
        return {true, 0, "", record.sourceRevision};
    }
    if (patch.operation != NIM_ADD && patch.operation != NIM_MODIFY)
        return {false, ERROR_INVALID_PARAMETER, "Not a record update"};
    if (patch.operation == NIM_ADD) {
        record.callbackMessage = 0;
        record.notifyVersion = 0;
        record.sourceState = 0;
        record.currentFrame = {};
        record.tip.clear();
    }
    if (patch.suppliedFlags & NIF_ICON)
        record.currentFrame = patch.frame;
    if (patch.suppliedFlags & NIF_MESSAGE)
        record.callbackMessage = patch.callbackMessage;
    if (patch.suppliedFlags & NIF_TIP)
        record.tip = patch.tip;
    if (patch.suppliedFlags & NIF_STATE)
        record.sourceState =
            (record.sourceState & ~patch.stateMask) | (patch.state & patch.stateMask);
    ++record.sourceRevision;
    return {true, 0, "", record.sourceRevision};
}
FrameDecision ChooseFrame(const Frame& source, const FrameBaseline& baseline, bool enabled) {
    if (!enabled)
        return FrameDecision::Native;
    if (source.transparent)
        return FrameDecision::NativeOff;
    if (!baseline.reliable)
        return FrameDecision::AwaitingBaseline;
    if (source.fingerprint == baseline.fingerprint && source.width == baseline.width &&
        source.height == baseline.height)
        return FrameDecision::CustomIdle;
    return FrameDecision::NativeDynamic;
}
bool HandleSessionQuery(SessionMachine& machine) {
    std::lock_guard lock(machine.transition);
    if (!machine.queryPending) {
        machine.beforeQuery = machine.state.load();
        machine.queryPending = true;
    }
    machine.state = SessionState::SessionQuery;
    return true;
}
void HandleSessionEnd(SessionMachine& machine, bool ending) {
    std::lock_guard lock(machine.transition);
    if (ending)
        machine.state = SessionState::SessionEnding;
    else if (machine.queryPending)
        machine.state = machine.beforeQuery.load();
    machine.queryPending = false;
}
bool ShouldRecover(const SessionMachine& machine) {
    return !machine.queryPending &&
           (machine.state == SessionState::Starting ||
            machine.state == SessionState::WaitingForShell ||
            machine.state == SessionState::Ready || machine.state == SessionState::Reconnecting);
}
void SetOperationalState(SessionMachine& machine, SessionState next) {
    std::lock_guard lock(machine.transition);
    if (machine.state == SessionState::UserStopping || machine.state == SessionState::SessionEnding)
        return;
    if (machine.queryPending)
        machine.beforeQuery = next;
    else
        machine.state = next;
}
} // namespace tray
