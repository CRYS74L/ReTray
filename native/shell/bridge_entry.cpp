#include "../core/notify_patch.h"
#include "../platform/software_bitmap.h"
#include "../platform/process_identity.h"
#include "shared.h"
#include "system_controls.h"
#include "input_method_logo.h"
#include "behavior_hooks.h"
#include <algorithm>
#include <commctrl.h>
#include <memory>
#include <objbase.h>
#include <unordered_map>
#include <unordered_set>

using namespace tray;
namespace {
HMODULE heldModule{};
HANDLE mapping{}, agentProcess{}, stopWatch{}, endingEvent{};
BridgeState* state{};
HWND taskbar{};
SymbolAdapter adapter;
volatile LONG callbacks{};
struct CallbackGuard {
    CallbackGuard() {
        InterlockedIncrement(&callbacks);
    }
    ~CallbackGuard() {
        InterlockedDecrement(&callbacks);
    }
};
uint64_t moduleBase{};
bool ownOperation{};
bool commandExecuting{}, detachRequested{};
UINT commandMessage{};
UINT reconcileMessage{};
bool reconcileQueued{};
uint64_t nextRegistration{};
bool interactionsLocked{};
bool opacityChanged{}, wasLayered{}, hadAlpha{};
SnapshotResult ReadBridgeSnapshot() {
    auto started = PerformanceMicros();
    auto result = ReadSnapshot(adapter, GetCurrentProcessId(), state->epoch, false);
    ++state->snapshotReads;
    state->snapshotMicrosTotal += PerformanceMicros() - started;
    return result;
}
BYTE savedAlpha{};
COLORREF savedColor{};
DWORD savedLayerFlags{};
void RestoreOpacity();
bool ApplyOpacity(int alpha) {
    if (alpha < 0 || alpha > 255)
        return false;
    if (alpha == 255) {
        RestoreOpacity();
        return true;
    }
    auto style = GetWindowLongPtrW(taskbar, GWL_EXSTYLE);
    if (!opacityChanged) {
        wasLayered = (style & WS_EX_LAYERED) != 0;
        hadAlpha = GetLayeredWindowAttributes(taskbar, &savedColor, &savedAlpha,
                                              &savedLayerFlags) != FALSE;
    }
    SetLastError(0);
    auto previous = SetWindowLongPtrW(taskbar, GWL_EXSTYLE, style | WS_EX_LAYERED);
    if (!previous && GetLastError())
        return false;
    if (!SetLayeredWindowAttributes(taskbar, 0, static_cast<BYTE>(alpha), LWA_ALPHA)) {
        SetWindowLongPtrW(taskbar, GWL_EXSTYLE, style);
        return false;
    }
    opacityChanged = true;
    return true;
}
void RestoreOpacity() {
    if (!opacityChanged)
        return;
    if (wasLayered && hadAlpha)
        SetLayeredWindowAttributes(taskbar, savedColor, savedAlpha, savedLayerFlags);
    else if (!wasLayered)
        SetWindowLongPtrW(taskbar, GWL_EXSTYLE,
                          GetWindowLongPtrW(taskbar, GWL_EXSTYLE) &
                              ~static_cast<LONG_PTR>(WS_EX_LAYERED));
    opacityChanged = false;
}
struct Entry {
    uint64_t registration{};
    NativeIconRecord record;
    IUnknown* original{};
    Frame source;
    // The UI original preview keeps the last visible source frame within this registration only.
    Frame originalPreview;
    FrameBaseline baseline;
    HICON replacement{};
    bool custom{}, customPainted{}, moved{}, tipChanged{}, swapButtons{};
    std::wstring originalTip, customTip;
    int visibility{-1};
    Placement originalPlacement{Placement::Overflow};
    bool originalHidden{};
    uint64_t revision{}, lastAccessAt{};
    ~Entry() {
        if (original)
            original->Release();
        if (replacement)
            DestroyIcon(replacement);
    }
};
std::unordered_map<std::string, std::unique_ptr<Entry>> entries;
uint32_t BehaviorFor(void* record) {
    if (interactionsLocked)
        return 2;
    for (auto& [key, entry] : entries)
        if (entry->record.recordAddress == reinterpret_cast<uint64_t>(record))
            return !ownOperation && entry->swapButtons ? 1 : 0;
    return 0;
}
void Accessed(void* record) {
    FILETIME time{};
    GetSystemTimeAsFileTime(&time);
    uint64_t now =
        (((uint64_t(time.dwHighDateTime) << 32) | time.dwLowDateTime) - 116444736000000000ull) /
        10000;
    for (auto& [key, entry] : entries)
        if (entry->record.recordAddress == reinterpret_cast<uint64_t>(record)) {
            entry->lastAccessAt = now;
            InterlockedIncrement(&state->snapshotSequence);
            for (uint32_t n = 0; n < state->count; ++n)
                if (SameIcon(state->icons[n].identity, entry->record.identity)) {
                    state->icons[n].lastAccessAt = now;
                    break;
                }
            InterlockedIncrement(&state->snapshotSequence);
            break;
        }
}
template <class T> bool LocalRead(uint64_t address, T& out) {
    SIZE_T n{};
    return ValidReadRange(address, sizeof(T)) &&
           ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<void*>(address), &out, sizeof(T),
                             &n) &&
           n == sizeof(T);
}
uint64_t GetModel() {
    uint64_t root{}, model{};
    if (!LocalRead(moduleBase + adapter.root, root) || !LocalRead(root + adapter.model, model))
        return 0;
    return model;
}
bool Method(uint64_t object, size_t slot, uint32_t rva, void*& function) {
    uint64_t table{}, target{};
    if (!LocalRead(object, table) || !LocalRead(table + slot * 8, target) ||
        target != moduleBase + rva)
        return false;
    function = reinterpret_cast<void*>(target);
    return true;
}
IUnknown* CurrentBitmap(const NativeIconRecord& record, bool* known = nullptr,
                        uint32_t* error = nullptr) {
    if (known)
        *known = false;
    void* method{};
    if (error)
        *error = 0;
    if (!Method(record.recordAddress + 0x10, 8, adapter.getIcon, method)) {
        if (error)
            *error = static_cast<uint32_t>(E_NOINTERFACE);
        return nullptr;
    }
    void* value{};
    using Fn = HRESULT(__stdcall*)(void*, void**);
    auto hr = reinterpret_cast<Fn>(method)(reinterpret_cast<void*>(record.recordAddress + 0x10),
                                          &value);
    if (FAILED(hr)) {
        if (error)
            *error = static_cast<uint32_t>(hr);
        return nullptr;
    }
    if (known)
        *known = true;
    return static_cast<IUnknown*>(value);
}
void RaiseChanged(uint64_t record) {
    using Fn = void(__cdecl*)(void*, const std::nullptr_t&, const std::nullptr_t&);
    const std::nullptr_t nothing = nullptr;
    reinterpret_cast<Fn>(moduleBase + 0x2919c)(reinterpret_cast<void*>(record + 0xf0), nothing,
                                               nothing);
}
void RestoreBitmap(Entry& entry) {
    auto slot = reinterpret_cast<void**>(entry.record.recordAddress + adapter.imageObject);
    if (entry.original)
        entry.original->AddRef();
    auto previous = static_cast<IUnknown*>(InterlockedExchangePointer(slot, entry.original));
    if (previous)
        previous->Release();
    RaiseChanged(entry.record.recordAddress);
}
Frame SourceFrame(IUnknown* object) {
    return RasterizeSoftwareBitmap(object).frame;
}
void SaveOriginal(Entry& entry) {
    bool known{};
    uint32_t error{};
    auto object = CurrentBitmap(entry.record, &known, &error);
    if (!known) {
        entry.source = {};
        entry.source.readError = error;
        entry.source.revision = ++entry.revision;
        return;
    }
    if (entry.original)
        entry.original->Release();
    entry.original = object;
    entry.source = SourceFrame(object);
    entry.source.revision = ++entry.revision;
    if (entry.source.origin == FrameOrigin::SourceLive && !entry.source.transparent &&
        entry.source.width && entry.source.height &&
        entry.source.pixels.size() >=
            static_cast<size_t>(entry.source.PixelWidth()) * entry.source.PixelHeight())
        entry.originalPreview = entry.source;
}
void BeginRegistration(Entry& entry, const NativeIconRecord& record) {
    entry.registration = ++nextRegistration;
    if (entry.original)
        entry.original->Release();
    entry.original = nullptr;
    entry.source = {};
    entry.originalPreview = {};
    entry.customPainted = false;
    entry.moved = false;
    entry.lastAccessAt = 0;
    entry.record = record;
    entry.originalHidden = record.placement == Placement::Hidden;
    entry.originalPlacement = entry.originalHidden ? Placement::Overflow : record.placement;
    entry.originalTip = record.tip;
}
void DrawPolicy(Entry& entry) {
    if (entry.source.origin != FrameOrigin::SourceLive)
        return;
    auto choice = ChooseFrame(entry.source, entry.baseline, entry.custom && entry.replacement);
    if (choice == FrameDecision::CustomIdle) {
        using SetIcon = void(__fastcall*)(void*, HICON);
        reinterpret_cast<SetIcon>(moduleBase + adapter.setIcon)(
            reinterpret_cast<void*>(entry.record.recordAddress), entry.replacement);
        entry.customPainted = true;
    } else if (entry.custom && entry.customPainted) {
        RestoreBitmap(entry);
        entry.customPainted = false;
    }
}
void WriteFrame(BridgeIcon& out, const Frame& frame) {
    out.live = frame.origin == FrameOrigin::SourceLive;
    out.transparent = frame.transparent;
    out.fingerprint = frame.fingerprint;
    out.revision = frame.revision;
    const auto pixelWidth = frame.PixelWidth(), pixelHeight = frame.PixelHeight();
    if (!pixelWidth || !pixelHeight ||
        frame.pixels.size() < static_cast<size_t>(pixelWidth) * pixelHeight) {
        out.width = out.height = 0;
        return;
    }
    const uint32_t w = std::min<uint32_t>(64, pixelWidth),
                   h = std::min<uint32_t>(64, pixelHeight);
    out.width = w;
    out.height = h;
    for (uint32_t y = 0; y < h; ++y)
        for (uint32_t x = 0; x < w; ++x)
            out.pixels[y * w + x] =
                frame.pixels[static_cast<size_t>(y) * pixelHeight / h * pixelWidth +
                             static_cast<size_t>(x) * pixelWidth / w];
}
bool SetTip(const NativeIconRecord& record, const std::wstring& text) {
    if (record.tip == text)
        return true;
    NOTIFYICONDATAW data{};
    data.cbSize = sizeof(data);
    data.hWnd = reinterpret_cast<HWND>(record.identity.ownerHwnd);
    data.uID = record.identity.uid;
    data.uFlags = NIF_TIP;
    if (record.identity.hasGuid) {
        data.uFlags |= NIF_GUID;
        data.guidItem = record.identity.guid;
    }
    wcsncpy_s(data.szTip, std::size(data.szTip), text.c_str(), _TRUNCATE);
    return Shell_NotifyIconW(NIM_MODIFY, &data) != FALSE;
}
bool VisibleVector(uint64_t object, std::vector<uint64_t>& values);
bool MoveIcon(const NativeIconRecord& record, int location, uint32_t index) {
    void* method{};
    uint64_t model = GetModel();
    uint64_t manager{}, collection{};
    std::vector<uint64_t> visible;
    if (!model || location < 0 || location > 1 || !LocalRead(model + adapter.manager, manager) ||
        !LocalRead(manager + (location == 0 ? adapter.promoted : adapter.overflow), collection) ||
        !VisibleVector(collection, visible) || !Method(model + 0x20, 6, 0x1309f0, method))
        return false;
    const bool alreadyHere = static_cast<int>(record.placement) == location;
    const auto last =
        static_cast<uint32_t>(visible.size() - (alreadyHere && !visible.empty() ? 1 : 0));
    index = std::min(index, last);
    if (alreadyHere && record.nativeIndex == index)
        return true;
    using Fn = HRESULT(__stdcall*)(void*, void*, int, UINT);
    return SUCCEEDED(reinterpret_cast<Fn>(method)(
        reinterpret_cast<void*>(model + 0x20), reinterpret_cast<void*>(record.recordAddress + 0x10),
        location, index));
}
bool MoveRegion(const NativeIconRecord& record, int location) {
    // Visibility restoration can change placement before this call; use the actual current region.
    auto live = ReadSnapshot(adapter, GetCurrentProcessId(), state->epoch, false);
    auto current = std::find_if(live.records.begin(), live.records.end(), [&](const auto& item) {
        return SameIcon(item.identity, record.identity);
    });
    return live.complete && current != live.records.end() &&
           (static_cast<int>(current->placement) == location ||
            MoveIcon(*current, location, UINT32_MAX));
}
void FillRow(BridgeIcon& row, Entry& entry) {
    auto& record = entry.record;
    row = {};
    row.identity = record.identity;
    row.registration = entry.registration;
    row.record = record.recordAddress;
    row.callback = record.callbackMessage;
    row.version = record.notifyVersion;
    row.placement = static_cast<uint32_t>(record.placement);
    row.index = record.nativeIndex;
    row.custom = entry.custom;
    row.sourceHidden = entry.originalHidden;
    wcsncpy_s(row.executable, std::size(row.executable), record.executable.c_str(), _TRUNCATE);
    wcsncpy_s(row.tip, std::size(row.tip),
              (entry.tipChanged ? entry.customTip : entry.originalTip).c_str(), _TRUNCATE);
    wcsncpy_s(row.windowClass, std::size(row.windowClass), record.windowClass.c_str(), _TRUNCATE);
    if (entry.custom &&
        ChooseFrame(entry.source, entry.baseline, true) == FrameDecision::CustomIdle) {
        bool known{};
        auto current = CurrentBitmap(record, &known);
        auto frame = known ? SourceFrame(current) : Frame{};
        if (current)
            current->Release();
        WriteFrame(row, frame);
    } else
        WriteFrame(row, entry.source);
    row.revision = entry.revision;
    row.lastAccessAt = entry.lastAccessAt;
    row.sourceFingerprint = entry.source.fingerprint;
    row.sourceWidth = entry.source.width;
    row.sourceHeight = entry.source.height;
    row.sourceReadState = static_cast<uint32_t>(entry.source.readState);
    row.sourceReadError = entry.source.readError;
    row.nativeWidth = entry.source.nativeWidth;
    row.nativeHeight = entry.source.nativeHeight;
    row.nativePixelFormat = entry.source.nativePixelFormat;
    row.nativeAlphaMode = entry.source.nativeAlphaMode;
    wcsncpy_s(row.sourceRuntimeType, std::size(row.sourceRuntimeType),
              entry.source.runtimeType.c_str(), _TRUNCATE);
    row.decision = static_cast<uint32_t>(ChooseFrame(entry.source, entry.baseline, entry.custom));
    wcsncpy_s(row.originalTip, std::size(row.originalTip), entry.originalTip.c_str(), _TRUNCATE);
    const auto& preview = entry.originalPreview;
    row.originalWidth = std::min<uint32_t>(64, preview.PixelWidth());
    row.originalHeight = std::min<uint32_t>(64, preview.PixelHeight());
    for (uint32_t y = 0; y < row.originalHeight; ++y)
        for (uint32_t x = 0; x < row.originalWidth; ++x)
            row.originalPixels[y * row.originalWidth + x] =
                preview.pixels[static_cast<size_t>(y) * preview.PixelHeight() / row.originalHeight *
                                   preview.PixelWidth() +
                               static_cast<size_t>(x) * preview.PixelWidth() / row.originalWidth];
    row.dynamic =
        ChooseFrame(entry.source, entry.baseline, entry.custom) == FrameDecision::NativeDynamic;
    row.managed = entry.custom || entry.visibility != -1 || entry.tipChanged || entry.moved ||
                  entry.swapButtons;
}
void Publish(bool refreshOriginal, const WireEvent* event = nullptr) {
    TickInputMethodLogos();
    if (!state)
        return;
    struct Measurement {
        BridgeState* target; uint64_t started{PerformanceMicros()};
        ~Measurement() { ++target->publications; target->publishMicrosTotal += PerformanceMicros() - started; }
    } measurement{state};
    auto snapshot = ReadBridgeSnapshot();
    if (!snapshot.complete || snapshot.records.size() > MaxBridgeIcons) {
        state->lastError = ERROR_RETRY;
        return;
    }
    std::vector<std::string> active;
    InterlockedIncrement(&state->snapshotSequence);
    state->count = 0;
    for (auto& record : snapshot.records) {
        std::string key = IdentityKey(record.identity);
        active.push_back(key);
        auto found = entries.find(key);
        if (found == entries.end()) {
            auto item = std::make_unique<Entry>();
            BeginRegistration(*item, record);
            SaveOriginal(*item);
            found = entries.emplace(key, std::move(item)).first;
        }
        auto& entry = *found->second;
        const bool changedObject = entry.record.recordAddress != record.recordAddress;
        bool sourceUpdate = event && ((event->identity.hasGuid && record.identity.hasGuid &&
                                       IsEqualGUID(event->identity.guid, record.identity.guid)) ||
                                      (!event->identity.hasGuid && !record.identity.hasGuid &&
                                       event->identity.ownerHwnd == record.identity.ownerHwnd &&
                                       event->identity.uid == record.identity.uid));
        if (changedObject || (sourceUpdate && event->patch.operation == NIM_ADD)) {
            BeginRegistration(entry, record);
        } else
            entry.record = record;
        if (changedObject ||
            (sourceUpdate &&
             (event->patch.operation == NIM_ADD || (event->patch.suppliedFlags & NIF_ICON)))) {
            SaveOriginal(entry);
            DrawPolicy(entry);
        } else if ((refreshOriginal && !entry.custom) ||
                   ((entry.source.origin != FrameOrigin::SourceLive ||
                     entry.source.readState == FrameReadState::Absent) && !entry.customPainted)) {
            SaveOriginal(entry);
            DrawPolicy(entry);
        }
        if (sourceUpdate && (event->patch.suppliedFlags & NIF_TIP))
            entry.originalTip = event->patch.tip;
        if (state->count >= MaxBridgeIcons)
            break;
        FillRow(state->icons[state->count++], entry);
    }
    for (auto it = entries.begin(); it != entries.end();) {
        if (std::find(active.begin(), active.end(), it->first) == active.end())
            it = entries.erase(it);
        else
            ++it;
    }
    state->lastSnapshotTick = GetTickCount64();
    InterlockedIncrement(&state->snapshotSequence);
}
LRESULT CALLBACK Subclass(HWND, UINT, WPARAM, LPARAM, UINT_PTR, DWORD_PTR);
bool SetVisibility(const NativeIconRecord& record, bool hidden, bool force = false) {
    if (!force && (record.placement == Placement::Hidden) == hidden)
        return true;
    NOTIFYICONDATAW data{};
    data.cbSize = sizeof(data);
    data.hWnd = reinterpret_cast<HWND>(record.identity.ownerHwnd);
    data.uID = record.identity.uid;
    data.uFlags = NIF_STATE;
    data.dwStateMask = NIS_HIDDEN;
    data.dwState = hidden ? NIS_HIDDEN : 0;
    if (record.identity.hasGuid) {
        data.uFlags |= NIF_GUID;
        data.guidItem = record.identity.guid;
    }
    return Shell_NotifyIconW(NIM_MODIFY, &data) != FALSE;
}
bool WireMatches(const IconIdentity& id, const WireEvent& event) {
    return event.identity.hasGuid ? id.hasGuid && IsEqualGUID(id.guid, event.identity.guid)
                                  : !id.hasGuid && id.ownerHwnd == event.identity.ownerHwnd &&
                                        id.uid == event.identity.uid;
}
bool LocalVector(uint64_t address, std::vector<uint64_t>& values) {
    uint64_t begin{}, end{};
    if (!LocalRead(address, begin) || !LocalRead(address + 8, end) ||
        !ValidVectorBounds(begin, end))
        return false;
    values.resize((end - begin) / 8);
    SIZE_T read{};
    return values.empty() || (ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<void*>(begin),
                                                values.data(), values.size() * 8, &read) &&
                              read == values.size() * 8);
}
bool VisibleVector(uint64_t object, std::vector<uint64_t>& values) {
    for (int adjust = -32; adjust <= 64; adjust += 8) {
        uint64_t candidate = object + adjust, table{}, at{}, size{};
        if (LocalRead(candidate, table) && LocalRead(table + 48, at) &&
            LocalRead(table + 56, size) && at == moduleBase + adapter.vectorAt &&
            size == moduleBase + adapter.vectorSize)
            return LocalVector(candidate + adapter.vectorBegin, values);
    }
    return false;
}
bool ReadAdded(const WireEvent& event, NativeIconRecord& record) {
    auto model = GetModel();
    uint64_t manager{}, promoted{}, overflow{};
    if (!model || !LocalRead(model + adapter.manager, manager) ||
        !LocalRead(manager + adapter.promoted, promoted) ||
        !LocalRead(manager + adapter.overflow, overflow))
        return false;
    std::vector<uint64_t> list, pinned, folded;
    if (!LocalVector(manager, list) || !VisibleVector(promoted, pinned) ||
        !VisibleVector(overflow, folded))
        return false;
    for (auto it = list.rbegin(); it != list.rend(); ++it) {
        uint64_t address = *it, table{};
        IconIdentity id;
        id.shellEpoch = state->epoch;
        if (!LocalRead(address, table) || table != moduleBase + adapter.itemVtable)
            return false;
        if (!LocalRead(address + adapter.window, id.ownerHwnd) ||
            !LocalRead(address + adapter.uid, id.uid) ||
            !LocalRead(address + adapter.guid, id.guid))
            return false;
        id.hasGuid = !IsEqualGUID(id.guid, GUID{});
        if (!WireMatches(id, event))
            continue;
        DWORD pid{};
        if (!GetWindowThreadProcessId(reinterpret_cast<HWND>(id.ownerHwnd), &pid) || !pid)
            return false;
        id.ownerPid = pid;
        id.ownerCreatedAt = ReadOwnerInfo(pid, record.executable);
        if (!id.ownerCreatedAt) return false;
        record.identity = id;
        record.recordAddress = address;
        if (!LocalRead(address + adapter.callback, record.callbackMessage) ||
            !LocalRead(address + adapter.version, record.notifyVersion) ||
            !LocalRead(address + adapter.imageObject, record.imageObject))
            return false;
        wchar_t cls[256]{};
        GetClassNameW(reinterpret_cast<HWND>(id.ownerHwnd), cls, 256);
        record.windowClass = cls;
        uint64_t hstring{}, buffer{};
        if (LocalRead(address + adapter.tip, hstring) && hstring &&
            LocalRead(hstring + 16, buffer)) {
            for (int n = 0; n < 127; ++n) {
                wchar_t ch{};
                if (!LocalRead(buffer + n * 2, ch) || !ch)
                    break;
                record.tip += ch;
            }
        }
        auto position = [&](const auto& values) {
            return std::find_if(values.begin(), values.end(), [&](uint64_t abi) {
                return abi >= address && abi - address <= 64;
            });
        };
        auto p = position(pinned), f = position(folded);
        record.placement = p != pinned.end()   ? Placement::Promoted
                           : f != folded.end() ? Placement::Overflow
                                               : Placement::Hidden;
        record.nativeIndex = static_cast<uint32_t>(
            record.placement == Placement::Promoted ? p - pinned.begin() : f - folded.begin());
        record.sourceState = record.placement == Placement::Hidden ? NIS_HIDDEN : 0;
        return true;
    }
    return false;
}
bool MembershipDelta(const WireEvent& event) {
    if (event.patch.operation != NIM_ADD && event.patch.operation != NIM_DELETE)
        return true;
    auto found = std::find_if(entries.begin(), entries.end(), [&](const auto& item) {
        return WireMatches(item.second->record.identity, event);
    });
    if (event.patch.operation == NIM_DELETE) {
        if (found == entries.end())
            return true;
        auto identity = found->second->record.identity;
        InterlockedIncrement(&state->snapshotSequence);
        for (uint32_t n = 0; n < state->count; ++n)
            if (SameIcon(state->icons[n].identity, identity)) {
                if (n + 1 < state->count)
                    memmove(&state->icons[n], &state->icons[n + 1],
                            (state->count - n - 1) * sizeof(BridgeIcon));
                --state->count;
                break;
            }
        entries.erase(found);
        InterlockedIncrement(&state->snapshotSequence);
        return true;
    }
    NativeIconRecord record;
    if (!ReadAdded(event, record))
        return false;
    auto key = IdentityKey(record.identity);
    auto current = entries.find(key);
    if (current == entries.end()) {
        if (state->count >= MaxBridgeIcons)
            return false;
        auto entry = std::make_unique<Entry>();
        BeginRegistration(*entry, record);
        SaveOriginal(*entry);
        current = entries.emplace(key, std::move(entry)).first;
    } else {
        // A successful NIM_ADD starts a new registration, even when Explorer reuses the address.
        BeginRegistration(*current->second, record);
        SaveOriginal(*current->second);
        DrawPolicy(*current->second);
        if (event.patch.suppliedFlags & NIF_TIP)
            current->second->originalTip = record.tip;
    }
    auto& entry = *current->second;
    ownOperation = true;
    if (entry.visibility != -1) {
        SetVisibility(record, entry.visibility == 1);
        entry.record.placement =
            entry.visibility == 1 ? Placement::Hidden : entry.originalPlacement;
    }
    if (entry.tipChanged)
        SetTip(record, entry.customTip);
    ownOperation = false;
    InterlockedIncrement(&state->snapshotSequence);
    uint32_t n{};
    for (; n < state->count; ++n)
        if (SameIcon(state->icons[n].identity, record.identity))
            break;
    if (n == state->count)
        ++state->count;
    FillRow(state->icons[n], entry);
    state->lastSnapshotTick = GetTickCount64();
    InterlockedIncrement(&state->snapshotSequence);
    return true;
}
void QueueReconcile() {
    if (!reconcileQueued) {
        reconcileQueued = true;
        PostMessageW(taskbar, reconcileMessage, 0, 0);
    }
}
void Detach() {
    if (!state)
        return;
    if (endingEvent && WaitForSingleObject(endingEvent, 0) == WAIT_OBJECT_0)
        InterlockedExchange(&state->sessionEnding, 1);
    ownOperation = true;
    RemoveBehaviorHooks();
    auto live = ReadSnapshot(adapter, GetCurrentProcessId(), state->epoch, false);
    std::vector<Entry*> restoreRegions;
    if (!state->sessionEnding && live.complete)
        for (auto& [key, entry] : entries) {
            auto current =
                std::find_if(live.records.begin(), live.records.end(), [&](const auto& r) {
                    return SameIcon(r.identity, entry->record.identity);
                });
            if (current != live.records.end()) {
                entry->record = *current;
                if (entry->customPainted)
                    RestoreBitmap(*entry);
                if (entry->tipChanged)
                    SetTip(*current, entry->originalTip);
                if (entry->visibility != -1 || entry->tipChanged || entry->moved)
                    SetVisibility(*current, entry->originalHidden);
                if (entry->moved && !entry->originalHidden)
                    restoreRegions.push_back(entry.get());
            }
        }
    for (auto entry : restoreRegions)
        MoveRegion(entry->record, static_cast<int>(entry->originalPlacement));
    if (!state->sessionEnding && live.complete)
        for (auto& [key, entry] : entries)
            if (entry->visibility != -1 || entry->tipChanged || entry->moved)
                SetVisibility(entry->record, entry->originalHidden, true);
    RestoreSystemControls(!state->sessionEnding);
    if (!state->sessionEnding)
        RestoreOpacity();
    entries.clear();
    RemoveWindowSubclass(taskbar, Subclass, BridgeMagic);
    state->status = 2;
    SetEvent(stopWatch);
    ownOperation = false;
}
bool RequestCancelled(LONG sequence) {
    return sequence && (state->cancelledSequence == sequence || state->requestSequence != sequence);
}
void ExecutePolicyBatch(LONG sequence);
void ExecuteCommand(const BridgeCommand* batchItem = nullptr, const SnapshotResult* batchSnapshot = nullptr) {
    if (!state)
        return;
    if (!batchItem && commandExecuting) return;
    struct ExecutionGuard {
        bool outer;
        explicit ExecutionGuard(bool value) : outer(value) { if (outer) commandExecuting = true; }
        ~ExecutionGuard() {
            if (!outer) return;
            commandExecuting = false;
            if (detachRequested) {
                detachRequested = false;
                PostMessageW(taskbar, commandMessage, 1, 0);
            }
        }
    } executionGuard{batchItem == nullptr};
    const auto sequence = state->requestSequence;
    MemoryBarrier();
    auto cmd = batchItem ? *batchItem : state->command;
    const auto acknowledge = [&] {
        if (!batchItem && state->requestSequence == sequence) {
            InterlockedExchange(&state->acceptedSequence, sequence);
            InterlockedExchange(&state->replySequence, sequence);
        }
    };
    struct Measurement {
        BridgeState* target; bool enabled; uint64_t started{PerformanceMicros()};
        ~Measurement() {
            if (!enabled) return;
            target->lastCommandMicros = PerformanceMicros() - started;
            ++target->commandsTotal; target->commandMicrosTotal += target->lastCommandMicros;
        }
    } measurement{state, batchItem == nullptr};
    DWORD error{};
    try {
        if (!batchItem && RequestCancelled(sequence)) {
            state->lastError = ERROR_OPERATION_ABORTED; acknowledge(); return;
        }
        if (!batchItem && cmd.operation == BridgeOperation::PolicyBatch) {
            ExecutePolicyBatch(sequence); acknowledge(); return;
        }
        if (cmd.operation == BridgeOperation::Detach) {
            Detach();
            acknowledge();
            return;
        }
        if (cmd.operation == BridgeOperation::Refresh) {
            Publish(cmd.index != 0);
            acknowledge();
            return;
        }
        if (cmd.operation == BridgeOperation::SystemRead ||
            cmd.operation == BridgeOperation::SystemSet || cmd.operation == BridgeOperation::SystemPolicy) {
            bool ok = ReadSystemControls(moduleBase, adapter, *state);
            if (ok && cmd.operation == BridgeOperation::SystemSet)
                ok = SetSystemControl(cmd.location, cmd.visibility);
            if (ok && cmd.operation == BridgeOperation::SystemPolicy)
                ok = SetSystemPolicy(cmd.location, cmd.visibility);
            state->lastError = ok ? 0 : ERROR_NOT_SUPPORTED;
            acknowledge();
            return;
        }
        if(cmd.operation==BridgeOperation::InputMethodLogo) {
            // An original-mode startup policy has nothing to restore in a new
            // bridge. Avoid two complete XAML control-tree discoveries for
            // each saved original-mode profile; actual owned images and all
            // transaction/rollback phases continue through the normal path.
            if (cmd.index == 0 && !cmd.replacementIcon && !HasInputMethodLogos()) {
                state->lastError = ERROR_SUCCESS;
                acknowledge();
                return;
            }
            const bool cachedTransaction=cmd.index==2 || cmd.index==3;
            bool ok=(cachedTransaction || ReadSystemControls(moduleBase,adapter,*state)) &&
                UpdateInputMethodLogo(Utf8(cmd.inputProfileKey),reinterpret_cast<HICON>(cmd.replacementIcon),cmd.index);
            if(ok && !cachedTransaction)ReadSystemControls(moduleBase,adapter,*state);
            state->lastError=ok?0:ERROR_NOT_SUPPORTED;
            acknowledge();return;
        }
        if (cmd.operation == BridgeOperation::Taskbar) {
            bool ok = true;
            if (cmd.index != -1)
                ok = ApplyOpacity(cmd.index);
            if (cmd.visibility != -1)
                ok = ReadSystemControls(moduleBase, adapter, *state) &&
                     SetSystemControl(7, cmd.visibility) && ok;
            state->lastError = ok ? 0 : ERROR_NOT_SUPPORTED;
            acknowledge();
            return;
        }
        if (cmd.operation == BridgeOperation::Behavior && cmd.identity.ownerPid == 0) {
            interactionsLocked = cmd.index == 1;
            state->lastError = 0;
            acknowledge();
            return;
        }
        if (cmd.operation == BridgeOperation::Policy &&
            ((cmd.clickEvent & ~(PolicyReplaceImage | PolicyClearImage | PolicyTip | PolicyVisibility | PolicyBehavior)) ||
             ((cmd.clickEvent & PolicyReplaceImage) && (cmd.clickEvent & PolicyClearImage)) ||
             ((cmd.clickEvent & PolicyVisibility) && (cmd.visibility < -1 || cmd.visibility > 1)) ||
             ((cmd.clickEvent & PolicyBehavior) && (cmd.index < 0 || cmd.index > 1)))) {
            state->lastError = ERROR_INVALID_PARAMETER;
            acknowledge();
            return;
        }
        SnapshotResult localSnapshot;
        if (!batchSnapshot) localSnapshot = ReadBridgeSnapshot();
        const auto& snapshot = batchSnapshot ? *batchSnapshot : localSnapshot;
        auto match =
            std::find_if(snapshot.records.begin(), snapshot.records.end(),
                         [&](const auto& r) { return SameIcon(r.identity, cmd.identity); });
        if (!snapshot.complete)
            error = ERROR_RETRY;
        else if (match == snapshot.records.end()) {
            if (!batchItem) Publish(false);
            error = ERROR_NOT_FOUND;
        } else {
            if (batchItem) {
                auto valid = ValidateNativeRecord(adapter, moduleBase, *match);
                if (valid != ERROR_SUCCESS) { state->lastError = valid; return; }
            }
            auto it = entries.find(IdentityKey(match->identity));
            if (it == entries.end())
                error = ERROR_NOT_FOUND;
            else {
                auto& entry = *it->second;
                if (entry.record.recordAddress != match->recordAddress) {
                    BeginRegistration(entry, *match);
                    SaveOriginal(entry);
                    DrawPolicy(entry);
                } else
                    entry.record = *match;
                ownOperation = true;
                if (cmd.operation == BridgeOperation::Policy) {
                    if (cmd.clickEvent & PolicyReplaceImage) {
                        auto replacement = CopyIcon(reinterpret_cast<HICON>(cmd.replacementIcon));
                        if (!replacement) error = ERROR_INVALID_HANDLE;
                        else {
                            if (entry.replacement) DestroyIcon(entry.replacement);
                            entry.replacement = replacement; entry.custom = true;
                            entry.baseline = cmd.suppliedBaseline
                                ? FrameBaseline{cmd.baselineWidth > 0 && cmd.baselineHeight > 0,
                                                cmd.baselineFingerprint, cmd.baselineWidth, cmd.baselineHeight}
                                : FrameBaseline{entry.source.origin == FrameOrigin::SourceLive && !entry.source.transparent,
                                                entry.source.fingerprint, entry.source.width, entry.source.height};
                            DrawPolicy(entry);
                        }
                    } else if (cmd.clickEvent & PolicyClearImage) {
                        if (entry.customPainted) RestoreBitmap(entry);
                        entry.custom = false; entry.customPainted = false;
                        if (entry.replacement) { DestroyIcon(entry.replacement); entry.replacement = nullptr; }
                    }
                    if (cmd.clickEvent & PolicyBehavior) entry.swapButtons = cmd.index == 1;
                    if (cmd.clickEvent & PolicyTip) {
                        entry.customTip = cmd.tip;
                        const bool changed = entry.tipChanged || !entry.customTip.empty();
                        entry.tipChanged = !entry.customTip.empty();
                        if (changed && !SetTip(*match, entry.tipChanged ? entry.customTip : entry.originalTip))
                            error = ERROR_INVALID_DATA;
                    }
                    if (cmd.clickEvent & PolicyVisibility) {
                        entry.visibility = cmd.visibility;
                        if (!SetVisibility(*match, cmd.visibility == -1 ? entry.originalHidden : cmd.visibility == 1))
                            error = ERROR_INVALID_DATA;
                    }
                } else if (cmd.operation == BridgeOperation::Apply) {
                    if (entry.replacement)
                        DestroyIcon(entry.replacement);
                    entry.replacement = CopyIcon(reinterpret_cast<HICON>(cmd.replacementIcon));
                    entry.custom = entry.replacement != nullptr;
                    entry.baseline =
                        cmd.suppliedBaseline
                            ? FrameBaseline{cmd.baselineWidth > 0 && cmd.baselineHeight > 0,
                                            cmd.baselineFingerprint, cmd.baselineWidth,
                                            cmd.baselineHeight}
                            : FrameBaseline{entry.source.origin == FrameOrigin::SourceLive &&
                                                !entry.source.transparent,
                                            entry.source.fingerprint, entry.source.width,
                                            entry.source.height};
                    DrawPolicy(entry);
                    if (!entry.custom)
                        error = ERROR_INVALID_HANDLE;
                } else if (cmd.operation == BridgeOperation::ClearImage) {
                    if (entry.customPainted)
                        RestoreBitmap(entry);
                    entry.custom = false;
                    entry.customPainted = false;
                    if (entry.replacement) {
                        DestroyIcon(entry.replacement);
                        entry.replacement = nullptr;
                    }
                } else if (cmd.operation == BridgeOperation::Behavior) {
                    entry.swapButtons = cmd.index == 1;
                } else if (cmd.operation == BridgeOperation::Restore) {
                    const bool restoreVisibility =
                        entry.visibility != -1 || entry.tipChanged || entry.moved;
                    entry.swapButtons = false;
                    if (entry.customPainted)
                        RestoreBitmap(entry);
                    entry.custom = false;
                    entry.customPainted = false;
                    if (entry.replacement) {
                        DestroyIcon(entry.replacement);
                        entry.replacement = nullptr;
                    }
                    if (restoreVisibility)
                        SetVisibility(*match, entry.originalHidden);
                    entry.visibility = -1;
                    if (entry.tipChanged) {
                        SetTip(*match, entry.originalTip);
                        entry.tipChanged = false;
                    }
                    if (entry.moved) {
                        if (!entry.originalHidden &&
                            !MoveRegion(*match, static_cast<int>(entry.originalPlacement)))
                            error = ERROR_INVALID_DATA;
                        else
                            entry.moved = false;
                    }
                    if (restoreVisibility && !SetVisibility(*match, entry.originalHidden, true))
                        error = ERROR_INVALID_DATA;
                } else if (cmd.operation == BridgeOperation::Visibility) {
                    entry.visibility = cmd.visibility;
                    if (!SetVisibility(*match, cmd.visibility == -1 ? entry.originalHidden
                                                                    : cmd.visibility == 1))
                        error = ERROR_INVALID_DATA;
                } else if (cmd.operation == BridgeOperation::Move) {
                    if (cmd.location == -1) {
                        if (entry.moved && !entry.originalHidden &&
                            !MoveRegion(*match, static_cast<int>(entry.originalPlacement)))
                            error = ERROR_INVALID_DATA;
                        else
                            entry.moved = false;
                    } else if (cmd.location < 0 || cmd.location > 1 || cmd.index < -1 ||
                               cmd.index >= 256 ||
                               !(cmd.index == -1 ? MoveRegion(*match, cmd.location)
                                                : MoveIcon(*match, cmd.location, cmd.index)))
                        error = ERROR_INVALID_DATA;
                    else if (cmd.index != -1 || static_cast<int>(match->placement) != cmd.location)
                        entry.moved = true;
                } else if (cmd.operation == BridgeOperation::Tip) {
                    entry.customTip = cmd.tip;
                    entry.tipChanged = !entry.customTip.empty();
                    if (!SetTip(*match, entry.tipChanged ? entry.customTip : entry.originalTip))
                        error = ERROR_INVALID_DATA;
                } else if (cmd.operation == BridgeOperation::Menu) {
                    void* method{};
                    if (!Method(match->recordAddress + 0x10, 14, 0x14aa40, method))
                        error = ERROR_NOT_SUPPORTED;
                    else {
                        struct Point {
                            float x, y;
                        };
                        using Fn = HRESULT(__stdcall*)(void*, bool, Point);
                        auto hr = reinterpret_cast<Fn>(method)(
                            reinterpret_cast<void*>(match->recordAddress + 0x10), false,
                            {cmd.x, cmd.y});
                        if (FAILED(hr))
                            error = hr;
                    }
                } else if (cmd.operation == BridgeOperation::Invoke) {
                    MousePoint point{cmd.x, cmd.y};
                    ownOperation = false;
                    InvokeNativeAction(reinterpret_cast<void*>(match->recordAddress),
                                       cmd.clickEvent, &point, cmd.visibility != 0, true);
                    ownOperation = true;
                } else if (cmd.operation == BridgeOperation::Click) {
                    if (interactionsLocked) {
                        error = ERROR_ACCESS_DENIED;
                        ownOperation = false;
                        state->lastError = error;
                        acknowledge();
                        return;
                    }
                    MousePoint point{cmd.x, cmd.y};
                    if (cmd.clickEvent == WM_RBUTTONDBLCLK) {
                        using Fn = void(__fastcall*)(void*, int, const MousePoint*);
                        auto callback = reinterpret_cast<Fn>(moduleBase + 0x73d90);
                        callback(reinterpret_cast<void*>(match->recordAddress), WM_RBUTTONDBLCLK,
                                 &point);
                        callback(reinterpret_cast<void*>(match->recordAddress), WM_RBUTTONUP,
                                 &point);
                    } else
                        InvokeNativeAction(reinterpret_cast<void*>(match->recordAddress),
                                           cmd.clickEvent == WM_LBUTTONDBLCLK ? 2 : 0, &point,
                                           true);
                } else
                    error = ERROR_INVALID_FUNCTION;
                ownOperation = false;
                if (!batchItem) Publish(false);
            }
        }
    } catch (...) {
        error = ERROR_INVALID_FUNCTION;
        ownOperation = false;
    }
    if (state->requestSequence == sequence) state->lastError = error;
    acknowledge();
}
void ExecutePolicyBatch(LONG sequence) {
    const auto count = state->batchCount;
    state->batchReplyCount = 0;
    if (!count || count > MaxBridgeIcons) { state->lastError = ERROR_INVALID_PARAMETER; return; }
    // Copy the bounded request before any native call can pump messages.
    std::vector<BridgeCommand> commands(state->batch, state->batch + count);
    // Own local copies before a slow native operation or caller timeout can
    // release the supplied HICONs. CopyIcon also rejects invalid GDI handles.
    using OwnedIcon = std::unique_ptr<std::remove_pointer_t<HICON>, decltype(&DestroyIcon)>;
    std::vector<OwnedIcon> images;
    for (auto& command : commands) {
        if (command.operation != BridgeOperation::Policy || !(command.clickEvent & PolicyReplaceImage)) continue;
        if (RequestCancelled(sequence)) { command.replacementIcon = 0; continue; }
        images.emplace_back(CopyIcon(reinterpret_cast<HICON>(command.replacementIcon)), DestroyIcon);
        command.replacementIcon = reinterpret_cast<uint64_t>(images.back().get());
    }
    if (state->requestSequence != sequence) return;
    // The sender may now release its private handle copies. No subsequent
    // receiver code reads an HICON from the shared request again.
    InterlockedExchange(&state->acceptedSequence, sequence);
    std::vector<DWORD> errors(count, ERROR_RETRY);
    const auto snapshot = ReadBridgeSnapshot();
    if (snapshot.complete) {
        std::unordered_set<std::string> seen;
        for (size_t index = 0; index < commands.size(); ++index) {
            if (RequestCancelled(sequence)) { errors[index] = ERROR_OPERATION_ABORTED; continue; }
            const auto& command = commands[index];
            if ((command.operation != BridgeOperation::Policy && command.operation != BridgeOperation::Restore) ||
                !seen.insert(IdentityKey(command.identity)).second) {
                errors[index] = ERROR_INVALID_PARAMETER; continue;
            }
            // ExecuteCommand preserves per-field partial success, original
            // image/baseline bookkeeping and registration renewal semantics.
            ExecuteCommand(&command, &snapshot);
            errors[index] = state->lastError;
        }
        if (state->requestSequence != sequence) return;
        Publish(false);
    }
    if (state->requestSequence != sequence) return;
    std::copy(errors.begin(), errors.end(), state->batchErrors);
    state->batchReplyCount = count;
    state->lastError = ERROR_SUCCESS; // Delivery succeeded; each item has its own result.
}
LRESULT CALLBACK Subclass(HWND hwnd, UINT message, WPARAM wp, LPARAM lp, UINT_PTR, DWORD_PTR) {
    CallbackGuard guard;
    if (message == commandMessage) {
        if (commandExecuting) {
            if (wp == 1) detachRequested = true;
            return 1;
        }
        if (wp == 1)
            Detach();
        else if (state && state->requestSequence != state->replySequence)
            ExecuteCommand();
        return 1;
    }
    if (message == reconcileMessage) {
        reconcileQueued = false;
        Publish(false);
        return 1;
    }
    if (message == WM_NCDESTROY) {
        Detach();
        return DefSubclassProc(hwnd, message, wp, lp);
    }
    if (message != WM_COPYDATA || ownOperation || !state)
        return DefSubclassProc(hwnd, message, wp, lp);
    WireEvent event;
    auto data = reinterpret_cast<const COPYDATASTRUCT*>(lp);
    if (data && data->dwData == 1 && data->lpData && data->cbData <= 65536)
        event = DecodeWire({static_cast<const uint8_t*>(data->lpData), data->cbData});
    auto result = DefSubclassProc(hwnd, message, wp, lp);
    if (event.supported && result && !ownOperation) {
        LARGE_INTEGER begin{}, end{}, frequency{};
        QueryPerformanceCounter(&begin);
        if (event.patch.operation == NIM_MODIFY || event.patch.operation == NIM_SETVERSION) {
            auto found = std::find_if(entries.begin(), entries.end(), [&](const auto& item) {
                const auto& id = item.second->record.identity;
                return event.identity.hasGuid
                           ? id.hasGuid && IsEqualGUID(id.guid, event.identity.guid)
                           : !id.hasGuid && id.ownerHwnd == event.identity.ownerHwnd &&
                                 id.uid == event.identity.uid;
            });
            if (found != entries.end()) {
                auto& entry = *found->second;
                ApplyPatch(entry.record, event.patch);
                if (event.patch.operation == NIM_MODIFY && (event.patch.suppliedFlags & NIF_ICON)) {
                    entry.customPainted = false;
                    SaveOriginal(entry);
                    DrawPolicy(entry);
                }
                if (event.patch.operation == NIM_MODIFY && (event.patch.suppliedFlags & NIF_TIP)) {
                    entry.originalTip = event.patch.tip;
                    if (entry.tipChanged) {
                        ownOperation = true;
                        SetTip(entry.record, entry.customTip);
                        ownOperation = false;
                    }
                }
                if (event.patch.operation == NIM_MODIFY &&
                    (event.patch.suppliedFlags & NIF_STATE)) {
                    if (event.patch.stateMask & NIS_HIDDEN)
                        entry.originalHidden = (event.patch.state & NIS_HIDDEN) != 0;
                    entry.record.placement = (entry.record.sourceState & NIS_HIDDEN)
                                                 ? Placement::Hidden
                                                 : entry.originalPlacement;
                    if (entry.visibility != -1) {
                        ownOperation = true;
                        SetVisibility(entry.record, entry.visibility == 1);
                        ownOperation = false;
                        entry.record.placement =
                            entry.visibility == 1 ? Placement::Hidden : entry.originalPlacement;
                    }
                }
                InterlockedIncrement(&state->snapshotSequence);
                for (uint32_t i = 0; i < state->count; ++i)
                    if (SameIcon(state->icons[i].identity, entry.record.identity)) {
                        FillRow(state->icons[i], entry);
                        break;
                    }
                InterlockedIncrement(&state->snapshotSequence);
            } else
                Publish(false, &event);
        } else if (!MembershipDelta(event))
            QueueReconcile();
        QueryPerformanceCounter(&end);
        QueryPerformanceFrequency(&frequency);
        state->callbackMicros[state->events++ % 512] =
            static_cast<uint32_t>((end.QuadPart - begin.QuadPart) * 1000000 / frequency.QuadPart);
    }
    return result;
}
DWORD WINAPI WatchAgent(void*) {
    HANDLE waits[] = {agentProcess, stopWatch};
    auto result = WaitForMultipleObjects(2, waits, FALSE, INFINITE);
    if (result == WAIT_OBJECT_0)
        PostMessageW(taskbar, commandMessage, 1, 0);
    WaitForSingleObject(stopWatch, INFINITE);
    while (InterlockedCompareExchange(&callbacks, 0, 0) || ActiveBehaviorCallbacks())
        Sleep(1);
    FinishBehaviorHooks();
    Sleep(20);
    if (state) {
        UnmapViewOfFile(state);
        state = nullptr;
    }
    if (mapping) {
        CloseHandle(mapping);
        mapping = nullptr;
    }
    if (endingEvent) {
        CloseHandle(endingEvent);
        endingEvent = nullptr;
    }
    if (agentProcess) {
        CloseHandle(agentProcess);
        agentProcess = nullptr;
    }
    if (stopWatch) {
        CloseHandle(stopWatch);
        stopWatch = nullptr;
    }
    auto hold = heldModule;
    heldModule = nullptr;
    FreeLibraryAndExitThread(hold, 0);
}
void Install(HWND hwnd) {
    if (state)
        return;
    DWORD session{};
    ProcessIdToSessionId(GetCurrentProcessId(), &session);
    mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS, FALSE, MappingName(session).c_str());
    if (!mapping)
        return;
    state = static_cast<BridgeState*>(
        MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(BridgeState)));
    if (!state || state->magic != BridgeMagic || state->schema != BridgeSchema ||
        state->byteSize != sizeof(BridgeState)) {
        if (state)
            UnmapViewOfFile(state);
        CloseHandle(mapping);
        state = nullptr;
        mapping = nullptr;
        return;
    }
    agentProcess =
        OpenProcess(SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION, FALSE, state->agentPid);
    if (!agentProcess) {
        state->lastError = GetLastError();
        return;
    }
    FILETIME created{}, exited{}, kernel{}, user{};
    DWORD agentSession{};
    if (!GetProcessTimes(agentProcess, &created, &exited, &kernel, &user) ||
        ((static_cast<uint64_t>(created.dwHighDateTime) << 32) | created.dwLowDateTime) !=
            state->agentCreatedAt ||
        !ProcessIdToSessionId(state->agentPid, &agentSession) || agentSession != session) {
        state->lastError = ERROR_ACCESS_DENIED;
        CloseHandle(agentProcess);
        agentProcess = nullptr;
        return;
    }
    endingEvent = OpenEventW(SYNCHRONIZE, FALSE,
                             EndingEventName(state->agentPid, state->agentCreatedAt).c_str());
    adapter = Expand(state->adapter);
    moduleBase = reinterpret_cast<uint64_t>(GetModuleHandleW(L"Taskbar.dll"));
    taskbar = hwnd;
    commandMessage = RegisterWindowMessageW(CommandMessage);
    reconcileMessage = RegisterWindowMessageW(L"ReTray.Bridge.Reconcile.1");
    auto snapshot = ReadSnapshot(adapter, GetCurrentProcessId(), state->epoch, false);
    if (!snapshot.complete) {
        state->lastError = ERROR_BAD_EXE_FORMAT;
        return;
    }
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS, reinterpret_cast<LPCWSTR>(&Install),
                       &heldModule);
    stopWatch = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!stopWatch || !SetWindowSubclass(hwnd, Subclass, BridgeMagic, 0)) {
        state->lastError = GetLastError();
        if (stopWatch) {
            CloseHandle(stopWatch);
            stopWatch = nullptr;
        }
        FreeLibrary(heldModule);
        heldModule = nullptr;
        return;
    }
    if (!InstallBehaviorHooks(moduleBase, BehaviorFor, Accessed)) {
        RemoveWindowSubclass(hwnd, Subclass, BridgeMagic);
        CloseHandle(stopWatch);
        stopWatch = nullptr;
        FreeLibrary(heldModule);
        heldModule = nullptr;
        state->lastError = ERROR_NOT_SUPPORTED;
        return;
    }
    auto thread = CreateThread(nullptr, 0, WatchAgent, nullptr, 0, nullptr);
    if (thread)
        CloseHandle(thread);
    else {
        RemoveBehaviorHooks();
        if (!ActiveBehaviorCallbacks())
            FinishBehaviorHooks();
        RemoveWindowSubclass(hwnd, Subclass, BridgeMagic);
        CloseHandle(stopWatch);
        stopWatch = nullptr;
        FreeLibrary(heldModule);
        heldModule = nullptr;
        state->lastError = GetLastError();
        return;
    }
    Publish(true);
    state->status = 1;
}
void CleanupFailedInstall() {
    if (!state || state->status == 1)
        return;
    if (endingEvent) {
        CloseHandle(endingEvent);
        endingEvent = nullptr;
    }
    if (agentProcess) {
        CloseHandle(agentProcess);
        agentProcess = nullptr;
    }
    if (stopWatch) {
        CloseHandle(stopWatch);
        stopWatch = nullptr;
    }
    if (heldModule) {
        FreeLibrary(heldModule);
        heldModule = nullptr;
    }
    UnmapViewOfFile(state);
    state = nullptr;
    if (mapping) {
        CloseHandle(mapping);
        mapping = nullptr;
    }
}
} // namespace
extern "C" __declspec(dllexport) LRESULT CALLBACK TrayBootstrap(int code, WPARAM wp, LPARAM lp) {
    if (code == HC_ACTION && lp) {
        auto message = reinterpret_cast<CWPSTRUCT*>(lp);
        if (message->message == RegisterWindowMessageW(InstallMessage)) {
            try {
                Install(message->hwnd);
            } catch (...) {
                if (state)
                    state->lastError = ERROR_INVALID_FUNCTION;
            }
            CleanupFailedInstall();
        }
    }
    return CallNextHookEx(nullptr, code, wp, lp);
}
BOOL WINAPI DllMain(HINSTANCE h, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH)
        DisableThreadLibraryCalls(h);
    return TRUE;
}
