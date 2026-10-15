#pragma once
#include "../platform/native_snapshot.h"
namespace tray {
constexpr uint32_t BridgeMagic = 0x54494131;
constexpr uint32_t BridgeSchema = 8;
constexpr size_t MaxBridgeIcons = 256, MaxFramePixels = 4096;
constexpr wchar_t InstallMessage[] = L"ReTray.Bridge.Install.8";
constexpr wchar_t CommandMessage[] = L"ReTray.Bridge.Command.8";
enum class BridgeOperation : uint32_t {
    Refresh = 1,
    Apply = 2,
    Restore = 3,
    Visibility = 4,
    Move = 5,
    Menu = 6,
    Click = 7,
    Detach = 8,
    Tip = 9,
    SystemRead = 10,
    SystemSet = 11,
    Taskbar = 12,
    Behavior = 13,
    Invoke = 14,
    ClearImage = 15,
    SystemPolicy = 16,
    InputMethodLogo = 17
};
struct SystemControl {
    uint32_t id{}, instances{};
    bool visible{};
    wchar_t name[128]{}, type[128]{};
    wchar_t glyph[32]{}, fontFamily[96]{};
    uint64_t prePaintHides{}, visibilityHides{};
    uint32_t logoTargets{}, logoApplied{}, logoError{};
    wchar_t inputProfileKey[96]{};
    wchar_t inputProfileId[160]{};
};
struct CompactAdapter {
    ImageIdentity image;
    uint32_t fields[24]{};
    uint32_t checkCount{};
    uint32_t checkRvas[16]{};
    uint8_t checkBytes[16][16]{};
};
struct BridgeIcon {
    IconIdentity identity;
    uint64_t registration{};
    uint64_t record{}, fingerprint{}, revision{}, sourceFingerprint{}, lastAccessAt{};
    uint32_t callback{}, version{}, width{}, height{}, placement{}, index{};
    uint32_t pixels[MaxFramePixels]{};
    uint32_t originalPixels[MaxFramePixels]{};
    uint32_t originalWidth{}, originalHeight{}, sourceWidth{}, sourceHeight{}, decision{};
    uint32_t sourceReadState{}, sourceReadError{}, nativeWidth{}, nativeHeight{}, nativePixelFormat{},
        nativeAlphaMode{};
    wchar_t sourceRuntimeType[128]{};
    bool live{}, transparent{}, custom{}, dynamic{}, managed{}, sourceHidden{};
    wchar_t executable[512]{}, tip[128]{}, originalTip[128]{}, windowClass[128]{};
};
struct BridgeCommand {
    BridgeOperation operation{};
    IconIdentity identity;
    uint64_t replacementIcon{};
    int32_t visibility{-1}, location{-1}, index{-1};
    uint32_t clickEvent{};
    float x{}, y{};
    wchar_t tip[128]{};
    uint64_t baselineFingerprint{};
    uint32_t baselineWidth{}, baselineHeight{};
    bool suppliedBaseline{};
    wchar_t inputProfileKey[96]{};
};
struct BridgeState {
    uint32_t magic{}, schema{}, agentPid{}, sessionId{}, byteSize{};
    uint64_t agentCreatedAt{}, epoch{};
    volatile LONG status{}, lastError{}, snapshotSequence{}, requestSequence{}, replySequence{},
        sessionEnding{};
    uint32_t count{};
    uint64_t events{}, lastSnapshotTick{}, lastCommandMicros{};
    uint32_t callbackMicros[512]{};
    uint32_t systemCount{};
    SystemControl systemControls[32]{};
    CompactAdapter adapter;
    BridgeCommand command;
    BridgeIcon icons[MaxBridgeIcons];
};
inline std::wstring MappingName(uint32_t session) {
    return L"Local\\ReTray.Bridge.8." + std::to_wstring(session);
}
inline std::wstring EndingEventName(uint32_t pid, uint64_t created) {
    return L"Local\\ReTray.Ending." + std::to_wstring(pid) + L"." + std::to_wstring(created);
}
inline CompactAdapter Compact(const SymbolAdapter& a) {
    CompactAdapter c;
    c.image = a.image;
    uint32_t fields[] = {a.root,       a.model,       a.manager,    a.promoted,    a.overflow,
                         a.itemVtable, a.vectorAt,    a.vectorSize, a.vectorBegin, a.vectorEnd,
                         a.window,     a.uid,         a.guid,       a.version,     a.callback,
                         a.tip,        a.imageObject, a.cachedIcon, a.setIcon,     a.getIcon};
    memcpy(c.fields, fields, sizeof(fields));
    c.checkCount = static_cast<uint32_t>(std::min<size_t>(16, a.checks.size()));
    for (uint32_t i = 0; i < c.checkCount; ++i) {
        c.checkRvas[i] = a.checks[i].first;
        memcpy(c.checkBytes[i], a.checks[i].second.data(), 16);
    }
    return c;
}
inline SymbolAdapter Expand(const CompactAdapter& c) {
    SymbolAdapter a;
    a.image = c.image;
    uint32_t* fields[] = {&a.root,        &a.model,      &a.manager,  &a.promoted,
                          &a.overflow,    &a.itemVtable, &a.vectorAt, &a.vectorSize,
                          &a.vectorBegin, &a.vectorEnd,  &a.window,   &a.uid,
                          &a.guid,        &a.version,    &a.callback, &a.tip,
                          &a.imageObject, &a.cachedIcon, &a.setIcon,  &a.getIcon};
    for (size_t i = 0; i < std::size(fields); ++i)
        *fields[i] = c.fields[i];
    for (uint32_t i = 0; i < std::min<uint32_t>(16, c.checkCount); ++i) {
        std::array<uint8_t, 16> b{};
        memcpy(b.data(), c.checkBytes[i], 16);
        a.checks.emplace_back(c.checkRvas[i], b);
    }
    return a;
}
} // namespace tray
