#include "../core/notify_patch.h"

#include "../platform/user_security.h"
#include "../shell/bridge_client.h"
#include "settings.h"

#include <algorithm>
#include <chrono>
#include <deque>
#include <fstream>
#include <memory>
#include <mutex>
#include <shlobj.h>
#include <shobjidl.h>
#include <unordered_map>
#include <unordered_set>
#include <tuple>
#include <appmodel.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.ApplicationModel.h>
using namespace tray;
namespace {
constexpr UINT OwnCallback = WM_APP + 101, OpenMessage = WM_APP + 102, UpdateHotkeys = WM_APP + 103;
HWND window{};
std::atomic<HWND> uiWindow{};

HANDLE stopEvent{}, pipeThread{}, workerThread{}, workEvent{}, endingEvent{}, singleton{},
    restartHandoff{};
DWORD session{};
SessionMachine lifecycle;
BridgeClient bridge;
SymbolAdapter adapter;
std::filesystem::path root, dataRoot, settingsPath;
Json settings;
bool recovered{}, ownAdded{};
std::atomic<bool> stopping{};
std::atomic<bool> ownOpenQueued{};
HICON ownImage{};
std::string ownIdentityKey;
uint64_t ownIdentityEpoch{};
std::unordered_map<std::string, IconRule> rules;
std::unordered_map<std::string, std::string> applied;
std::unordered_map<std::string, std::string> instanceOrderKeys;
std::unordered_map<std::string, std::string> failedStamp;
std::unordered_map<std::string, uint64_t> retryAfter;
std::unordered_map<std::string, Json> outcomes;
std::vector<BridgeIcon> rows;
IconCatalog catalog;
LONG seenSequence{-1};
uint64_t configRevision{};
uint64_t environmentEpoch{}, lastEnvironmentCheck{};
std::string environmentStamp;
Json environmentOutcome = {{"success", true}};
struct Request {
    Json input, output;
    HANDLE event{CreateEventW(nullptr, TRUE, FALSE, nullptr)};
    ~Request() {
        CloseHandle(event);
    }
};
std::mutex queueMutex;
std::deque<std::shared_ptr<Request>> requests;

std::mutex hotkeyMutex;
Json requestedHotkeys = Json::array(), registeredHotkeys = Json::array(),
     hotkeyErrors = Json::array();
Json activityHistory = Json::object();
bool activityDirty{};
uint64_t lastActivitySave{}, lastReview{};

void ScheduleHotkeys();
void Enqueue(const std::shared_ptr<Request>& request) {
    {
        std::lock_guard lock(queueMutex);
        requests.push_back(request);
    }
    SetEvent(workEvent);
}
void PostJob(const std::string& method, Json data = Json::object()) {
    auto request = std::make_shared<Request>();
    request->input = {{"schema", 1}, {"method", method}, {"data", std::move(data)}};
    Enqueue(request);
}

uint64_t Generation(DWORD pid) {
    HANDLE p = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!p)
        return 0;
    FILETIME c{}, e{}, k{}, u{};
    GetProcessTimes(p, &c, &e, &k, &u);
    CloseHandle(p);
    return (uint64_t(c.dwHighDateTime) << 32) | c.dwLowDateTime;
}
bool IsOwnIcon(const BridgeIcon& row) {
    return bridge.shared && row.identity.shellEpoch == bridge.shared->epoch &&
           row.identity.ownerPid == GetCurrentProcessId() &&
           row.identity.ownerHwnd == reinterpret_cast<uintptr_t>(window) &&
           row.identity.uid == 1 && !row.identity.hasGuid &&
           row.identity.ownerCreatedAt == Generation(GetCurrentProcessId());
}
void RememberOwnIcon(const BridgeIcon& row) {
    if (IsOwnIcon(row)) {
        ownIdentityKey = IdentityKey(row.identity);
        ownIdentityEpoch = bridge.shared->epoch;
    }
}
uint64_t Now() {
    return static_cast<uint64_t>(std::chrono::duration_cast<std::chrono::milliseconds>(
                                     std::chrono::system_clock::now().time_since_epoch())
                                     .count());
}
NativeIconRecord Record(const BridgeIcon& row) {
    NativeIconRecord r;
    r.identity = row.identity;
    r.executable = row.executable;
    r.windowClass = row.windowClass;
    r.tip = row.originalTip;
    r.notifyVersion = row.version;
    r.callbackMessage = row.callback;
    r.placement = static_cast<Placement>(row.placement);
    r.nativeIndex = row.index;
    r.sourceRevision = row.revision;
    r.currentFrame.fingerprint = row.sourceFingerprint;
    return r;
}
Json Result(bool ok, const std::string& detail = {}, DWORD error = 0) {
    return {
        {"success", ok}, {"detail", detail}, {"errorCode", error}, {"revision", configRevision}};
}
bool Persist() {
    settings["revision"] = ++configRevision;
    if (!SaveSettings(settingsPath, settings)) {
        --configRevision;
        return false;
    }
    return true;
}
void LoadRules() {
    rules.clear();
    for (auto& j : settings["rules"]) {
        auto rule = ParseRule(j);
        rules[PersistentKey(rule.selector)] = std::move(rule);
    }
}
void InvalidateRule(const RuleSelector& selector, const std::string& previousKey = {}) {
    for (const auto& row : rows) {
        auto record = Record(row);
        auto prior = ResolveRule(rules, record);
        if (!Matches(selector, record) &&
            (previousKey.empty() || !prior.rule ||
             PersistentKey(prior.rule->selector) != previousKey))
            continue;
        auto key = IdentityKey(row.identity);
        applied.erase(key);
        failedStamp.erase(key);
        retryAfter.erase(key);
    }
}
void ScheduleHotkeys() {
    if (!window || stopping)
        return;
    Json requested = Json::array();
    auto defaults = DefaultSettings()["hotkeys"];
    auto keys = settings.value("hotkeys", defaults);
    int id = 1;
    for (const char* name : {"open", "menu"})
        requested.push_back({{"id", id++},
                             {"code", keys.value(name, defaults.at(name).get<uint32_t>())},
                             {"action", name}});
    for (const auto& row : rows) {
        auto found = ResolveRule(rules, Record(row));
        if (!found.rule || !found.rule->enabled || !found.rule->hotkey)
            continue;
        static const char* actions[] = {"click", "menu", "double", "rightDouble"};
        requested.push_back({{"id", id++},
                             {"code", found.rule->hotkey},
                             {"action", "icon"},
                             {"iconAction", actions[found.rule->hotkeyAction]},
                             {"key", IdentityKey(row.identity)}});
    }
    std::lock_guard lock(hotkeyMutex);
    if (requested == requestedHotkeys)
        return;
    requestedHotkeys = std::move(requested);
    PostMessageW(window, UpdateHotkeys, 0, 0);
}
void RegisterRequestedHotkeys() {
    Json requested;
    {
        std::lock_guard lock(hotkeyMutex);
        requested = requestedHotkeys;
    }
    for (const auto& old : registeredHotkeys)
        UnregisterHotKey(window, old.at("id"));
    registeredHotkeys = Json::array();
    Json errors = Json::array();
    for (const auto& item : requested) {
        uint32_t code = item.at("code");
        if (!code)
            continue;
        if (!RegisterHotKey(window, item.at("id"), (code >> 16) | MOD_NOREPEAT, code & 0xffff))
            errors.push_back(
                {{"action", item.at("action")}, {"code", code}, {"error", GetLastError()}});
        else
            registeredHotkeys.push_back(item);
    }
    {
        std::lock_guard lock(hotkeyMutex);
        hotkeyErrors = errors;
    }
}
void OpenUI() {
    auto name = Wide(settings.value("uiActivation", std::string{}));
    if (name.empty())
        return;
    IApplicationActivationManager* manager{};
    if (SUCCEEDED(CoCreateInstance(CLSID_ApplicationActivationManager, nullptr, CLSCTX_LOCAL_SERVER,
                                   IID_IApplicationActivationManager,
                                   reinterpret_cast<void**>(&manager)))) {
        DWORD pid{};
        manager->ActivateApplication(name.c_str(), nullptr, AO_NONE, &pid);
        manager->Release();
    }
}
bool SetOwnIcon(bool reloadImage = true) {
    auto policy = settings["ownIcon"];
    if (reloadImage && ownImage) {
        DestroyIcon(ownImage);
        ownImage = nullptr;
    }
    bool visible = policy.value("visible", true);
    NOTIFYICONDATAW d{};
    d.cbSize = sizeof(d);
    d.hWnd = window;
    d.uID = 1;
    if (!visible) {
        if (ownAdded && !Shell_NotifyIconW(NIM_DELETE, &d))
            return false;
        ownAdded = false;
        return true;
    }
    auto asset = Wide(policy.value("asset", std::string{}));
    if (!ownImage)
        ownImage = asset.empty() ? LoadAsset(root / L"Assets" / L"AppIcon.ico") : LoadAsset(asset);
    if (!ownImage && asset.empty())
        ownImage = CopyIcon(LoadIconW(nullptr, IDI_APPLICATION));
    if (!ownImage)
        return false;
    d.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP;
    d.uCallbackMessage = OwnCallback;
    d.hIcon = ownImage;
    auto tip = Wide(policy.value("tip", std::string("托盘图标助手")));
    wcsncpy_s(d.szTip, std::size(d.szTip), tip.c_str(), _TRUNCATE);
    bool ok = Shell_NotifyIconW(ownAdded ? NIM_MODIFY : NIM_ADD, &d) != FALSE;
    if (ok) {
        ownAdded = true;
        d.uVersion = NOTIFYICON_VERSION_4;
        Shell_NotifyIconW(NIM_SETVERSION, &d);
    }
    return ok;
}
bool SetStartup(bool enabled) {
    UINT32 packageNameLength{};
    if (GetCurrentPackageFullName(&packageNameLength, nullptr) == ERROR_INSUFFICIENT_BUFFER) {
        try {
            const auto deadline = std::chrono::steady_clock::now() + std::chrono::seconds(3);
            auto wait = [&](auto operation) {
                while (operation.Status() == winrt::Windows::Foundation::AsyncStatus::Started) {
                    if (stopping || std::chrono::steady_clock::now() >= deadline) {
                        operation.Cancel();
                        throw winrt::hresult_error(
                            HRESULT_FROM_WIN32(stopping ? ERROR_CANCELLED : ERROR_TIMEOUT));
                    }
                    operation.wait_for(std::chrono::milliseconds(25));
                }
                return operation.get();
            };
            auto task = wait(
                winrt::Windows::ApplicationModel::StartupTask::GetAsync(L"TrayAssistantStartup"));
            if (!enabled) {
                task.Disable();
                return true;
            }
            auto state = wait(task.RequestEnableAsync());
            if (state == winrt::Windows::ApplicationModel::StartupTaskState::Enabled)
                return true;
            SetLastError(ERROR_ACCESS_DISABLED_BY_POLICY);
        } catch (const winrt::hresult_error& error) {
            SetLastError(static_cast<DWORD>(error.code().value));
        }
        return false;
    }
    HKEY key{};
    if (RegCreateKeyExW(HKEY_CURRENT_USER, L"Software\\Microsoft\\Windows\\CurrentVersion\\Run", 0,
                        nullptr, 0, KEY_SET_VALUE, nullptr, &key, nullptr) != ERROR_SUCCESS)
        return false;
    std::wstring command =
        L"\"" + (root / L"TrayAssistant.Agent.exe").wstring() + L"\" --background";
    LONG code = enabled ? RegSetValueExW(key, L"TrayIconAssistant", 0, REG_SZ,
                                         reinterpret_cast<const BYTE*>(command.c_str()),
                                         static_cast<DWORD>((command.size() + 1) * 2))
                        : RegDeleteValueW(key, L"TrayIconAssistant");
    RegCloseKey(key);
    return code == ERROR_SUCCESS || (!enabled && code == ERROR_FILE_NOT_FOUND);
}
int EffectiveVisibility(const BridgeIcon& row, const IconRule* rule) {
    int visibility = rule && rule->enabled ? rule->visibility : -1;
    if (rule && IsInactive(*rule, catalog.items[IdentityKey(row.identity)].activity, Now()))
        visibility = 1;
    return visibility;
}
std::string RuleStamp(const IconRule* rule, int visibility) {
    return (rule ? RuleJson(*rule).dump() : std::string("native")) + ":" +
           std::to_string(visibility);
}
void ApplyRule(const BridgeIcon& row, const IconRule* rule) {
    if (!ShouldRecover(lifecycle))
        return;
    auto key = IdentityKey(row.identity);
    int visibility = EffectiveVisibility(row, rule);
    auto stamp = RuleStamp(rule, visibility);
    if (applied[key] == stamp)
        return;
    if (failedStamp[key] == stamp) {
        auto retry = retryAfter.find(key);
        if (retry == retryAfter.end() || GetTickCount64() < retry->second)
            return;
    }
    bool success = true;
    DWORD error{};
    auto send = [&](BridgeCommand cmd) {
        cmd.identity = row.identity;
        bool ok = bridge.Command(cmd);
        if (!ok) {
            success = false;
            error = bridge.shared ? bridge.shared->lastError : ERROR_NOT_READY;
        }
        return ok;
    };
    if (rule && rule->enabled && !rule->asset.empty()) {
        int size = row.sourceWidth
                       ? row.sourceWidth
                       : GetSystemMetricsForDpi(
                             SM_CXSMICON, GetDpiForWindow(FindWindowW(L"Shell_TrayWnd", nullptr)));
        auto image = LoadAsset(rule->asset, rule->assetIndex, size);
        if (image) {
            BridgeCommand cmd;
            cmd.operation = BridgeOperation::Apply;
            cmd.replacementIcon = reinterpret_cast<uint64_t>(image);
            cmd.suppliedBaseline = true;
            cmd.baselineFingerprint = rule->baselineFingerprint;
            cmd.baselineWidth = rule->baselineWidth;
            cmd.baselineHeight = rule->baselineHeight;
            send(cmd);
            DestroyIcon(image);
        } else {
            success = false;
            error = ERROR_FILE_NOT_FOUND;
        }
    } else if (row.custom && rule && rule->enabled) {
        BridgeCommand cmd;
        cmd.operation = BridgeOperation::ClearImage;
        send(cmd);
    } else if (row.managed && !(rule && rule->enabled)) {
        BridgeCommand cmd;
        cmd.operation = BridgeOperation::Restore;
        send(cmd);
    }

    if (rule && rule->enabled) {
        BridgeCommand cmd;
        cmd.operation = BridgeOperation::Behavior;
        cmd.index = rule->swapButtons ? 1 : 0;
        send(cmd);
        cmd = {};
        cmd.operation = BridgeOperation::Tip;
        wcsncpy_s(cmd.tip, std::size(cmd.tip), rule->tip.c_str(), _TRUNCATE);
        send(cmd);
    }
    if (visibility != -1 || row.managed) {
        BridgeCommand cmd;
        cmd.operation = BridgeOperation::Visibility;
        cmd.visibility = visibility;
        send(cmd);
    }
    outcomes[key] = Result(success, success ? "已生效" : "规则已保存，应用失败", error);
    if (success) {
        applied[key] = stamp;
        failedStamp.erase(key);
        retryAfter.erase(key);
    } else {
        applied.erase(key);
        failedStamp[key] = stamp;
        if (error == ERROR_RETRY || error == ERROR_NOT_READY || error == ERROR_TIMEOUT ||
            error == ERROR_BUSY)
            retryAfter[key] = GetTickCount64() + 3000;
        else
            retryAfter.erase(key);
    }
}
bool ApplyEnvironment(bool force = false) {
    if (!bridge.Alive() || !ShouldRecover(lifecycle))
        return false;
    auto now = GetTickCount64();
    auto stamp = Json::array({settings.value("taskbar", Json::object()),
                             settings.value("systemControls", Json::array())}).dump();
    if (!force && environmentEpoch == bridge.shared->epoch &&
        environmentStamp == stamp && now - lastEnvironmentCheck < 3000)
        return environmentOutcome.value("success", true);
    bool changed =
        environmentEpoch != bridge.shared->epoch || environmentStamp != stamp;
    environmentEpoch = bridge.shared->epoch;
    environmentStamp = std::move(stamp);
    lastEnvironmentCheck = now;
    bool ok = true;
    DWORD error{};
    auto taskbar = settings.value("taskbar", Json::object());
    if (taskbar.value("opacity", 255) != 255 || taskbar.value("hideStart", false) || force ||
        changed) {
        BridgeCommand command;
        command.operation = BridgeOperation::Taskbar;
        command.index = taskbar.value("opacity", 255);
        command.visibility = taskbar.value("hideStart", false) ? 1 : 0;
        ok = bridge.Command(command);
        if (!ok)
            error = bridge.shared->lastError;
    }
    if (changed || force) {
        static std::vector<int> prior;
        std::vector<int> current;
        for (const auto& policy : settings.value("systemControls", Json::array()))
            current.push_back(policy.at("id"));
        for (int id : prior)
            if (std::find(current.begin(), current.end(), id) == current.end()) {
                BridgeCommand command;
                command.operation = BridgeOperation::SystemSet;
                command.location = id;
                command.visibility = 0;
                bridge.Command(command);
            }
        prior = current;
    }
    if (settings.contains("systemControls"))
        for (const auto& policy : settings["systemControls"]) {
            BridgeCommand command;
            command.operation = BridgeOperation::SystemSet;
            command.location = policy.at("id");
            command.visibility = policy.value("hidden", false) ? 1 : 0;
            if (!bridge.Command(command)) {
                ok = false;
                error = bridge.shared->lastError;
            }
        }
    environmentOutcome =
        Result(ok, ok ? "任务栏设置已生效" : "设置已保存，等待原有系统控件或适配失败", error);
    return ok;
}
std::string StableIconKey(const BridgeIcon& row) {
    if (row.identity.ownerPid == GetCurrentProcessId())
        return "@assistant";
    auto base = PersistentKey(SelectorFor(Record(row)));
    auto owner = base + "|live:" + std::to_string(row.identity.ownerCreatedAt) + ":" +
                 std::to_string(row.identity.ownerHwnd);
    auto found = instanceOrderKeys.find(owner);
    if (found != instanceOrderKeys.end())
        return found->second;
    std::unordered_set<std::string> activeOwners;
    for (const auto& current : rows) {
        auto currentBase = PersistentKey(SelectorFor(Record(current)));
        activeOwners.insert(currentBase +
                            "|live:" + std::to_string(current.identity.ownerCreatedAt) + ":" +
                            std::to_string(current.identity.ownerHwnd));
    }
    for (auto it = instanceOrderKeys.begin(); it != instanceOrderKeys.end();)
        if (!activeOwners.contains(it->first))
            it = instanceOrderKeys.erase(it);
        else
            ++it;
    std::unordered_set<std::string> occupied;
    for (const auto& [token, assigned] : instanceOrderKeys)
        occupied.insert(assigned);
    size_t ordinal{};
    while (occupied.contains(ordinal ? base + "|instance:" + std::to_string(ordinal) : base))
        ++ordinal;
    auto key = ordinal ? base + "|instance:" + std::to_string(ordinal) : base;
    instanceOrderKeys.emplace(owner, key);
    return key;
}
void Refresh() {
    if (!ShouldRecover(lifecycle) || stopping)
        return;
    if (!bridge.Alive()) {
        SetOperationalState(lifecycle, SessionState::WaitingForShell);
        if (!ExplorerPid())
            return;
        bridge.Reset();
        if (!bridge.Attach(adapter, root / L"TrayAssistant.ShellBridge.dll"))
            return;
        seenSequence = -1;
        applied.clear();
        failedStamp.clear();
        retryAfter.clear();
        SetOperationalState(lifecycle, SessionState::Ready);
    }
    ApplyEnvironment();
    auto interval = settings.value("reviewIntervalSeconds", 0u);
    if (interval && GetTickCount64() - lastReview >= uint64_t(interval) * 1000) {
        lastReview = GetTickCount64();
        BridgeCommand command;
        command.operation = BridgeOperation::Refresh;
        bridge.Command(command);
    }
    if (activityDirty && GetTickCount64() - lastActivitySave >= 30000) {
        if (SaveDataJson(dataRoot / L"activity.json", activityHistory))
            activityDirty = false;
        lastActivitySave = GetTickCount64();
    }
    LONG sequence = bridge.shared->snapshotSequence;
    if ((sequence & 1) || sequence == seenSequence) {
        for (auto& row : rows) {
            auto found = ResolveRule(rules, Record(row));
            if (found.rule && (found.rule->inactiveSeconds || found.rule->unusedDays))
                ApplyRule(row, found.rule);
        }
        return;
    }
    auto current = bridge.Snapshot();
    if (sequence != bridge.shared->snapshotSequence)
        return;
    seenSequence = sequence;
    rows = std::move(current);
    for (const auto& row : rows)
        RememberOwnIcon(row);
    SnapshotResult snapshot;
    snapshot.complete = true;
    for (auto& row : rows)
        snapshot.records.push_back(Record(row));
    auto delta = MergeSnapshot(catalog, snapshot, Now());
    if (!delta.accepted)
        return;
    for (const auto& row : rows) {
        auto record = Record(row);
        auto stable = PersistentKey(SelectorFor(record));
        auto& activity = catalog.items[IdentityKey(row.identity)].activity;
        if (!activityHistory.contains(stable)) {
            activityHistory[stable] = {{"firstSeen", Now()}, {"lastAccess", Now()}};
            activityDirty = true;
        }
        auto& saved = activityHistory[stable];
        auto accessed = std::max(saved.value("lastAccess", uint64_t{}), row.lastAccessAt);
        activity.lastAccess = accessed;
        if (accessed != saved.value("lastAccess", uint64_t{})) {
            saved["lastAccess"] = accessed;
            activityDirty = true;
        }
    }
    for (auto it = applied.begin(); it != applied.end();)
        if (!catalog.items.contains(it->first))
            it = applied.erase(it);
        else
            ++it;
    for (auto it = failedStamp.begin(); it != failedStamp.end();)
        if (!catalog.items.contains(it->first)) {
            retryAfter.erase(it->first);
            it = failedStamp.erase(it);
        } else
            ++it;
    for (auto& row : rows) {
        if (row.identity.ownerPid == GetCurrentProcessId())
            continue;
        auto found = ResolveRule(rules, Record(row));
        if (found.ambiguous) {
            outcomes[IdentityKey(row.identity)] = Result(false, "多个匹配规则冲突", ERROR_DUP_NAME);
            continue;
        }
        ApplyRule(row, found.rule);
    }
    ScheduleHotkeys();
}
std::string Bytes64(const uint32_t* pixels, size_t count) {
    const auto* bytes = reinterpret_cast<const uint8_t*>(pixels);
    static const char chars[] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    std::string out;
    size_t size = count * 4;
    out.reserve((size + 2) / 3 * 4);
    for (size_t i = 0; i < size; i += 3) {
        uint32_t n = uint32_t(bytes[i]) << 16;
        if (i + 1 < size)
            n |= uint32_t(bytes[i + 1]) << 8;
        if (i + 2 < size)
            n |= bytes[i + 2];
        out += chars[(n >> 18) & 63];
        out += chars[(n >> 12) & 63];
        out += i + 1 < size ? chars[(n >> 6) & 63] : '=';
        out += i + 2 < size ? chars[n & 63] : '=';
    }
    return out;
}
Json CatalogJson() {
    Json icons = Json::array();
    for (auto& row : rows) {
        if (row.identity.ownerPid == GetCurrentProcessId() &&
            !settings["ownIcon"].value("showInList", true))
            continue;
        auto record = Record(row);
        auto key = IdentityKey(row.identity);
        auto selector = SelectorFor(record);
        auto found = ResolveRule(rules, record);
        IconRule empty;
        empty.selector = selector;
        auto rule = found.rule ? *found.rule : empty;
        auto j = RuleJson(rule);
        bool owned = row.identity.ownerPid == GetCurrentProcessId();
        j["owned"] = owned;
        j["stableKey"] = StableIconKey(row);
        if (owned) {
            j["asset"] = settings["ownIcon"].value("asset", std::string{});
            j["tip"] = settings["ownIcon"].value("tip", std::string("托盘图标助手"));
        }
        j["key"] = key;
        j["pid"] = row.identity.ownerPid;
        j["tipCurrent"] = Utf8(row.tip);
        j["placement"] = row.placement;
        j["index"] = row.index;
        j["dynamic"] = row.dynamic;
        j["custom"] = row.custom;
        j["live"] = row.live;
        j["width"] = row.width;
        j["height"] = row.height;
        j["pixels"] = Bytes64(row.pixels, row.width * row.height);
        j["sourceFingerprint"] = row.sourceFingerprint;
        j["sourceWidth"] = row.sourceWidth;
        j["sourceHeight"] = row.sourceHeight;
        j["decision"] = row.decision;
        j["created"] = row.identity.ownerCreatedAt;
        j["originalWidth"] = row.originalWidth;
        j["originalHeight"] = row.originalHeight;
        j["originalPixels"] = Bytes64(row.originalPixels, row.originalWidth * row.originalHeight);
        j["outcome"] = outcomes.contains(key) ? outcomes[key] : Result(true, "原生状态");
        auto stable = PersistentKey(SelectorFor(record));
        auto history = activityHistory.value(stable, Json::object());
        j["firstSeen"] = history.value("firstSeen", Now());
        j["lastAccess"] = history.value("lastAccess", Now());
        j["new"] = rule.newState == 1 ||
                   (rule.newState == -1 && Now() - history.value("firstSeen", Now()) < 86400000);
        icons.push_back(std::move(j));
    }
    return {{"icons", icons}, {"revision", catalog.revision}};
}
HANDLE AssistantUiProcess(HWND hwnd, DWORD access = PROCESS_QUERY_LIMITED_INFORMATION,
                          BOOL inherit = FALSE) {
    if (!IsWindow(hwnd)) return nullptr;
    DWORD pid{};
    GetWindowThreadProcessId(hwnd, &pid);
    HANDLE process = OpenProcess(access, inherit, pid);
    if (!process) return nullptr;
    wchar_t path[32768]{};
    DWORD length = std::size(path);
    bool read = QueryFullProcessImageNameW(process, 0, path, &length) != FALSE;
    DWORD currentPid{};
    GetWindowThreadProcessId(hwnd, &currentPid);
    if (!read || currentPid != pid ||
        _wcsicmp(path, (root / L"TrayAssistant.App.exe").c_str()) != 0) {
        CloseHandle(process);
        return nullptr;
    }
    return process;
}
bool IsAssistantUi(HWND hwnd) {
    auto process = AssistantUiProcess(hwnd);
    if (!process) return false;
    CloseHandle(process);
    return true;
}
HWND AssistantUiWindow() {
    auto ui = uiWindow.load();
    if (IsAssistantUi(ui)) return ui;
    // A right click can arrive during UI startup, before ui.attach is sent.
    ui = nullptr;
    EnumWindows([](HWND candidate, LPARAM result) -> BOOL {
        if (!IsWindowVisible(candidate) || GetWindow(candidate, GW_OWNER) ||
            !IsAssistantUi(candidate)) return TRUE;
        *reinterpret_cast<HWND*>(result) = candidate;
        return FALSE;
    }, reinterpret_cast<LPARAM>(&ui));
    return ui;
}
std::wstring QuoteCommandArgument(const std::wstring& value) {
    std::wstring quoted = L"\"";
    size_t backslashes{};
    for (auto character : value) {
        if (character == L'\\') { ++backslashes; continue; }
        quoted.append(backslashes * (character == L'\"' ? 2 : 1), L'\\');
        backslashes = 0;
        if (character == L'\"') quoted.push_back(L'\\');
        quoted.push_back(character);
    }
    quoted.append(backslashes * 2, L'\\');
    quoted.push_back(L'\"');
    return quoted;
}
bool WaitForRestartExit(HANDLE oldAgent, HANDLE oldUi, HANDLE cancel, DWORD timeout = 30000) {
    if (!oldAgent || !cancel) return false;
    auto deadline = GetTickCount64() + timeout;
    for (auto target : {oldUi, oldAgent}) {
        if (!target) continue;
        auto now = GetTickCount64();
        HANDLE waits[] = {cancel, target};
        auto remaining = now < deadline ? static_cast<DWORD>(deadline - now) : 0;
        if (WaitForMultipleObjects(std::size(waits), waits, FALSE, remaining) != WAIT_OBJECT_0 + 1)
            return false;
    }
    return WaitForSingleObject(cancel, 0) == WAIT_TIMEOUT;
}
DWORD RestartAssistant() {
    if (restartHandoff) {
        if (WaitForSingleObject(restartHandoff, 0) == WAIT_TIMEOUT) return ERROR_SUCCESS;
        CloseHandle(restartHandoff);
        restartHandoff = nullptr;
    }
    auto ui = AssistantUiWindow();
    HANDLE oldUi = ui ? AssistantUiProcess(ui, SYNCHRONIZE | PROCESS_QUERY_LIMITED_INFORMATION,
                                          TRUE) : nullptr;
    if (ui && !oldUi) return ERROR_INVALID_WINDOW_HANDLE;
    SECURITY_ATTRIBUTES inherit{sizeof(inherit), nullptr, TRUE};
    HANDLE handles[] = {OpenProcess(SYNCHRONIZE, TRUE, GetCurrentProcessId()), oldUi, nullptr};
    auto closeHandles = [&] { for (auto handle : handles) if (handle) CloseHandle(handle); };
    if (!handles[0]) {
        auto error = GetLastError(); closeHandles(); return error;
    }
    handles[2] = CreateEventW(&inherit, TRUE, FALSE, nullptr);
    if (!handles[2]) {
        auto error = GetLastError(); closeHandles(); return error;
    }
    SIZE_T bytes{};
    InitializeProcThreadAttributeList(nullptr, 1, 0, &bytes);
    std::vector<unsigned char> storage(bytes);
    auto attributes = reinterpret_cast<PPROC_THREAD_ATTRIBUTE_LIST>(storage.data());
    if (!InitializeProcThreadAttributeList(attributes, 1, 0, &bytes)) {
        auto error = GetLastError(); closeHandles(); return error;
    }
    std::vector<HANDLE> inherited{handles[0], handles[2]};
    if (oldUi) inherited.push_back(oldUi);
    if (!UpdateProcThreadAttribute(attributes, 0, PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
                                  inherited.data(), inherited.size() * sizeof(HANDLE), nullptr,
                                  nullptr)) {
        auto error = GetLastError(); DeleteProcThreadAttributeList(attributes);
        closeHandles(); return error;
    }
    auto executable = root / L"TrayAssistant.Agent.exe";
    auto command = QuoteCommandArgument(executable.wstring()) + L" --open --data " +
                   QuoteCommandArgument(dataRoot.wstring()) + L" --restart-wait";
    for (auto handle : handles)
        command += L" " + std::to_wstring(reinterpret_cast<uintptr_t>(handle));
    STARTUPINFOEXW startup{};
    startup.StartupInfo.cb = sizeof(startup);
    startup.lpAttributeList = attributes;
    PROCESS_INFORMATION process{};
    bool created = CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, TRUE,
                                  EXTENDED_STARTUPINFO_PRESENT | CREATE_NO_WINDOW, nullptr,
                                  root.c_str(), &startup.StartupInfo, &process) != FALSE;
    auto error = created ? ERROR_SUCCESS : GetLastError();
    DeleteProcThreadAttributeList(attributes);
    if (created) {
        CloseHandle(process.hThread);
        restartHandoff = process.hProcess;
        // QuitAsync flushes pending UI edits before stopping the Agent. The replacement
        // waits on the exact processes, so it cannot race the old single-instance locks.
        if ((ui && !IsAssistantUi(ui)) ||
            !PostMessageW(ui ? ui : window, WM_CLOSE, 0, 0)) {
            error = ERROR_INVALID_WINDOW_HANDLE;
            SetEvent(handles[2]);
        }
    }
    closeHandles();
    return error;
}
bool RollbackSettings(const Json& previous) {
    auto pending = settings;
    settings = previous;
    settings["revision"] = configRevision;
    // Persist already backed up the previous configuration; rollback must not replace that
    // backup with the rejected policy.
    if (ValidateSettings(settings) && SaveDataJson(settingsPath, settings))
        return true;
    settings = std::move(pending);
    return false;
}
Json SetIconVisibility(const Json& data) {
    if (!data.contains("key") || !data.at("key").is_string() ||
        data.at("key").get_ref<const std::string&>().empty() ||
        data.at("key").get_ref<const std::string&>().size() > 32768 ||
        !data.contains("visibility") || !data.at("visibility").is_number_integer())
        return Result(false, "图标可见性参数无效", ERROR_INVALID_DATA);
    auto key = data.at("key").get<std::string>();
    const auto& requested = data.at("visibility");
    if (requested.is_number_unsigned() ? requested.get<uint64_t>() > 1
                                      : requested.get<int64_t>() < -1 || requested.get<int64_t>() > 1)
        return Result(false, "图标可见性参数无效", ERROR_INVALID_DATA);
    int visibility = requested.get<int>();
    if (!ShouldRecover(lifecycle) || stopping || !bridge.Alive())
        return Result(false, "原生托盘尚未就绪", ERROR_NOT_READY);
    auto current = bridge.Snapshot();
    auto target = std::find_if(current.begin(), current.end(),
                               [&](const auto& row) { return IdentityKey(row.identity) == key; });
    if (target != current.end() && target->identity.shellEpoch != bridge.shared->epoch)
        target = current.end();
    if (target != current.end())
        RememberOwnIcon(*target);
    bool owned = key == ownIdentityKey && ownIdentityEpoch == bridge.shared->epoch;
    if (target == current.end() && !owned)
        return Result(false, "图标已退出", ERROR_NOT_FOUND);
    auto previous = settings;
    if (owned) {
        settings["ownIcon"]["visible"] = visibility != 1;
        if (!Persist()) {
            settings = previous;
            return Result(false, "配置保存失败", ERROR_WRITE_FAULT);
        }
        if (!SetOwnIcon(false)) {
            auto error = GetLastError();
            bool rolledBack = RollbackSettings(previous);
            auto result = Result(false, rolledBack ? "助手图标更新失败，配置未改变"
                                                  : "助手图标更新失败，配置回滚失败",
                                 rolledBack ? error : ERROR_WRITE_FAULT);
            result["saved"] = !rolledBack;
            return result;
        }
        auto result = Result(true, visibility == 1 ? "已隐藏" : "已显示");
        result["saved"] = true;
        result["data"] = {{"key", key}, {"visibility", visibility}, {"visible", visibility != 1}};
        if (visibility == 1)
            result["data"]["placement"] = static_cast<int>(Placement::Hidden);
        else
            for (const auto& row : bridge.Snapshot())
                if (IdentityKey(row.identity) == key)
                    result["data"]["placement"] = row.placement;
        return result;
    }
    auto found = ResolveRule(rules, Record(*target));
    if (found.ambiguous)
        return Result(false, "多个匹配规则冲突", ERROR_DUP_NAME);
    IconRule rule = found.rule ? *found.rule : IconRule{};
    if (!found.rule)
        rule.selector = SelectorFor(Record(*target));
    rule.visibility = visibility;
    rule.inactiveSeconds = rule.unusedDays = 0;
    rule.enabled = true;
    auto stable = PersistentKey(rule.selector);
    auto updatedRules = rules;
    updatedRules[stable] = rule;
    struct Affected {
        BridgeIcon row;
        int priorVisibility{};
        bool fullyApplied{};
    };
    std::vector<Affected> affected;
    for (const auto& row : current) {
        auto next = ResolveRule(updatedRules, Record(row));
        if (!next.rule || PersistentKey(next.rule->selector) != stable)
            continue;
        auto prior = ResolveRule(rules, Record(row));
        auto oldVisibility = EffectiveVisibility(row, prior.rule);
        auto appliedTo = applied.find(IdentityKey(row.identity));
        bool wasApplied = appliedTo != applied.end() &&
                          appliedTo->second == RuleStamp(prior.rule, oldVisibility);
        bool unchangedPolicy = !prior.rule || prior.rule->enabled;
        affected.push_back({row, oldVisibility,
                            unchangedPolicy && (wasApplied || (!prior.rule && !row.managed))});
    }
    auto& list = settings["rules"];
    auto existing = std::find_if(list.begin(), list.end(), [&](const auto& entry) {
        return PersistentKey(ParseRule(entry).selector) == stable;
    });
    if (existing == list.end())
        list.push_back(RuleJson(rule));
    else
        *existing = RuleJson(rule);
    if (!Persist()) {
        settings = previous;
        return Result(false, "配置保存失败", ERROR_WRITE_FAULT);
    }
    size_t completed{};
    for (const auto& item : affected) {
        BridgeCommand command;
        command.operation = BridgeOperation::Visibility;
        command.identity = item.row.identity;
        command.visibility = visibility;
        if (!bridge.Command(command)) {
            auto error = bridge.shared ? bridge.shared->lastError : ERROR_NOT_READY;
            if (error != ERROR_NOT_FOUND) {
                command.visibility = item.priorVisibility;
                if (!bridge.Command(command))
                    applied.erase(IdentityKey(item.row.identity));
            }
            for (size_t i = 0; i < completed; ++i) {
                command.identity = affected[i].row.identity;
                command.visibility = affected[i].priorVisibility;
                if (!bridge.Command(command))
                    applied.erase(IdentityKey(command.identity));
            }
            bool rolledBack = RollbackSettings(previous);
            auto result = Result(false, rolledBack ? "图标可见性更新失败，配置未改变"
                                                  : "图标可见性更新失败，配置回滚失败",
                                 rolledBack ? error : ERROR_WRITE_FAULT);
            result["saved"] = !rolledBack;
            if (!rolledBack) {
                rules = std::move(updatedRules);
                seenSequence = -1;
            }
            return result;
        }
        ++completed;
    }
    rules = std::move(updatedRules);
    auto result = Result(true, visibility == 1 ? "已隐藏" : "已显示");
    result["saved"] = true;
    auto stamp = RuleStamp(&rule, visibility);
    for (const auto& item : affected) {
        auto affectedKey = IdentityKey(item.row.identity);
        if (item.fullyApplied) {
            applied[affectedKey] = stamp;
            failedStamp.erase(affectedKey);
            retryAfter.erase(affectedKey);
        } else
            applied.erase(affectedKey);
        outcomes[affectedKey] = result;
    }
    result["data"] = {{"key", key}, {"visibility", visibility},
                      {"visible", false}, {"placement", static_cast<int>(Placement::Hidden)}};
    for (const auto& row : bridge.Snapshot())
        if (IdentityKey(row.identity) == key) {
            result["data"]["placement"] = row.placement;
            result["data"]["visible"] = row.placement != static_cast<uint32_t>(Placement::Hidden);
            break;
        }
    return result;
}
Json Handle(const Json& req) {
    if (!req.is_object())
        return Result(false, "请求必须是对象", ERROR_INVALID_DATA);
    if (req.value("schema", 0) != 1)
        return Result(false, "协议版本不匹配", ERROR_REVISION_MISMATCH);
    auto method = req.value("method", std::string{});
    auto d = req.value("data", Json::object());
    if (!d.is_object())
        return Result(false, "命令参数必须是对象", ERROR_INVALID_DATA);
    Json result = Result(true);
    if (method == "ui.attach") {
        auto hwnd = reinterpret_cast<HWND>(d.at("hwnd").get<uintptr_t>());
        if (!IsAssistantUi(hwnd)) return Result(false, "界面窗口身份不匹配", ERROR_INVALID_WINDOW_HANDLE);
        uiWindow = hwnd;
    } else if (method == "icons.reorder") {
        auto keys = d.at("keys");
        if (!keys.is_array() || keys.size() > MaxBridgeIcons)
            return Result(false, "图标顺序无效", ERROR_INVALID_DATA);
        std::unordered_set<std::string> unique;
        for (const auto& key : keys)
            if (!key.is_string() || key.get<std::string>().size() > 32768 ||
                !unique.insert(key.get<std::string>()).second)
                return Result(false, "图标顺序无效", ERROR_INVALID_DATA);
        auto saved = settings;
        settings["iconOrder"] = keys;
        settings["autoSort"] = false;
        if (!Persist()) {
            settings = saved;
            return Result(false, "顺序保存失败", ERROR_WRITE_FAULT);
        }
        result["saved"] = true;
    } else if (method == "internal.openUi") {
        OpenUI();
        ownOpenQueued = false;
    } else if (method == "internal.shellCreated") {
        ownAdded = false;
        SetOwnIcon();
        seenSequence = -1;
    } else if (method == "status.read") {
        Json shortcuts;
        {
            std::lock_guard lock(hotkeyMutex);
            shortcuts = hotkeyErrors;
        }
        std::vector<uint32_t> times;
        if (bridge.shared)
            times.assign(bridge.shared->callbackMicros,
                         bridge.shared->callbackMicros +
                             std::min<uint64_t>(512, bridge.shared->events));
        std::sort(times.begin(), times.end());
        result["data"] = {
            {"ready", lifecycle.state == SessionState::Ready},
            {"state", static_cast<int>(lifecycle.state.load())},
            {"connected", bridge.Alive()},
            {"version", TRAY_ASSISTANT_VERSION},

            {"environment", environmentOutcome},
            {"hotkeyErrors", shortcuts},
            {"recovered", recovered},
            {"pid", GetCurrentProcessId()},
            {"callbackP95Micros", times.empty() ? 0 : times[(times.size() - 1) * 95 / 100]},
            {"events", bridge.shared ? bridge.shared->events : 0}};
    } else if (method == "icon.preview") {
        auto path = Wide(d.at("path").get<std::string>());
        int index = d.value("index", 0);
        if (index < 0 || index > 10000)
            return Result(false, "图标序号无效", ERROR_INVALID_DATA);
        auto icon = LoadAsset(path, index, 48);
        if (!icon)
            return Result(false, "无法读取此资源或图标序号", ERROR_RESOURCE_NAME_NOT_FOUND);
        auto frame = RasterizeIcon(icon, FrameOrigin::SourceLive);
        DestroyIcon(icon);
        if (!frame.outcome.success)
            return Result(false, "无法解码图标", ERROR_INVALID_DATA);
        int count = 1;
        auto ext = std::filesystem::path(path).extension().wstring();
        if (_wcsicmp(ext.c_str(), L".exe") == 0 || _wcsicmp(ext.c_str(), L".dll") == 0)
            count = ExtractIconExW(path.c_str(), -1, nullptr, nullptr, 0);
        result["data"] = {{"width", frame.frame.width},
                          {"height", frame.frame.height},
                          {"pixels", Bytes64(frame.frame.pixels.data(), frame.frame.pixels.size())},
                          {"count", count}};
    } else if (method == "catalog.read") {
        Refresh();
        result["data"] = CatalogJson();
    } else if (method == "settings.read")
        result["data"] = settings;
    else if (method == "system.read") {
        BridgeCommand command;
        command.operation = BridgeOperation::SystemRead;
        if (!bridge.Command(command))
            return Result(false, "系统控件读取不可用",
                          bridge.shared ? bridge.shared->lastError : ERROR_NOT_READY);
        result["data"] = Json::array();
        for (uint32_t i = 0; i < bridge.shared->systemCount; ++i) {
            auto row = bridge.shared->systemControls[i];
            result["data"].push_back(
                {{"id", row.id}, {"visible", row.visible}, {"instances", row.instances}});
        }
    } else if (method == "system.apply") {
        auto saved = settings;
        settings["systemControls"] = d.at("controls");
        if (!Persist()) {
            settings = saved;
            return Result(false, "配置保存失败", ERROR_WRITE_FAULT);
        }
        ApplyEnvironment(true);
        result = environmentOutcome;
        result["saved"] = true;
    } else if (method == "taskbar.apply") {
        auto saved = settings;
        settings["taskbar"] = d;
        if (!Persist()) {
            settings = saved;
            return Result(false, "任务栏设置无效或保存失败", ERROR_INVALID_DATA);
        }
        ApplyEnvironment(true);
        result = environmentOutcome;
        result["saved"] = true;
    } else if (method == "settings.import") {
        auto imported = d.at("settings");
        if (!ValidateSettings(imported))
            return Result(false, "配置内容无效", ERROR_INVALID_DATA);
        auto old = settings;
        settings = imported;
        if (!Persist()) {
            settings = old;
            return Result(false, "配置保存失败", ERROR_WRITE_FAULT);
        }
        bridge.Reset();
        settings.erase("safeMode");
        if (settings.contains("hotkeys"))
            settings["hotkeys"].erase("restoreIcons");
        for (auto& entry : settings["rules"])
            entry["enabled"] = true;
        LoadRules();
        SetOwnIcon();
        SetStartup(settings.value("startup", false));
        seenSequence = -1;
        Refresh();
        result["saved"] = true;
    } else if (method == "rules.apply") {
        auto list = d.at("rules");
        if (!list.is_array() || list.size() > 256)
            return Result(false, "批量规则无效", ERROR_INVALID_DATA);
        for (const auto& entry : list)
            ParseRule(entry);
        auto saved = settings;
        for (const auto& entry : list) {
            auto rule = ParseRule(entry);
            auto key = PersistentKey(rule.selector);
            auto& existing = settings["rules"];
            for (auto it = existing.begin(); it != existing.end();) {
                if (PersistentKey(ParseRule(*it).selector) == key)
                    it = existing.erase(it);
                else
                    ++it;
            }
            existing.push_back(RuleJson(rule));
        }
        if (!Persist()) {
            settings = saved;
            return Result(false, "批量规则无法保存", ERROR_WRITE_FAULT);
        }
        settings.erase("safeMode");
        if (settings.contains("hotkeys"))
            settings["hotkeys"].erase("restoreIcons");
        for (auto& entry : settings["rules"])
            entry["enabled"] = true;
        for (const auto& entry : list)
            InvalidateRule(ParseRule(entry).selector);
        LoadRules();
        seenSequence = -1;
        Refresh();
        result["saved"] = true;
        result["data"] = outcomes;
        result["detail"] = "批量规则已保存并应用";
    } else if (method == "rule.apply" || method == "rule.remove") {
        auto rule = ParseRule(d.at("rule"));
        auto key = PersistentKey(rule.selector);
        auto saved = settings;
        auto& list = settings["rules"];
        std::string priorKey;
        if (d.contains("previousSelector")) {
            auto prior = d.at("rule");
            prior["selector"] = d.at("previousSelector");
            priorKey = PersistentKey(ParseRule(prior).selector);
        }
        for (auto it = list.begin(); it != list.end();)
            if (PersistentKey(ParseRule(*it).selector) == key ||
                (!priorKey.empty() && PersistentKey(ParseRule(*it).selector) == priorKey))
                it = list.erase(it);
            else
                ++it;
        if (method == "rule.apply")
            list.push_back(RuleJson(rule));
        if (!Persist()) {
            settings = saved;
            return Result(false, "配置保存失败", ERROR_WRITE_FAULT);
        }
        settings.erase("safeMode");
        if (settings.contains("hotkeys"))
            settings["hotkeys"].erase("restoreIcons");
        for (auto& entry : settings["rules"])
            entry["enabled"] = true;
        InvalidateRule(rule.selector, priorKey);
        LoadRules();
        seenSequence = -1;
        Refresh();
        result["saved"] = true;
        for (auto& row : rows)
            if (Matches(rule.selector, Record(row))) {
                result = outcomes[IdentityKey(row.identity)];
                result["saved"] = true;
                break;
            }
    } else if (method == "icon.visibility")
        result = SetIconVisibility(d);
    else if (method == "ownIcon.apply") {
        auto saved = settings;
        settings["ownIcon"] = d;
        if (!Persist()) {
            settings = saved;
            return Result(false, "配置保存失败", ERROR_WRITE_FAULT);
        }
        bool ok = SetOwnIcon();
        result =
            Result(ok, ok ? "已生效" : "已保存，图标资源无法加载", ok ? 0 : ERROR_INVALID_DATA);
        result["saved"] = true;
    } else if (method == "settings.apply") {
        auto saved = settings;
        for (auto& [key, value] : d.items())
            if (key != "schema" && key != "rules" && key != "ownIcon" && key != "revision" &&
                key != "newIconLocation")
                settings[key] = value;
        if (!Persist()) {
            settings = saved;
            return Result(false, "配置保存失败", ERROR_WRITE_FAULT);
        }
        bool ok = !d.contains("startup") || SetStartup(settings.value("startup", false));
        ScheduleHotkeys();
        result = Result(ok, ok ? "设置已保存" : "已保存，自启动设置失败", ok ? 0 : GetLastError());
        result["saved"] = true;
    } else if (method == "icon.invoke") {
        auto key = d.at("key").get<std::string>();
        auto row = std::find_if(rows.begin(), rows.end(),
                                [&](const auto& r) { return IdentityKey(r.identity) == key; });
        if (row == rows.end())
            return Result(false, "图标已退出", ERROR_NOT_FOUND);
        POINT p{};
        GetCursorPos(&p);
        BridgeCommand cmd;
        cmd.identity = row->identity;
        cmd.operation = d.value("action", std::string("menu")) == "menu" ? BridgeOperation::Menu
                                                                         : BridgeOperation::Click;
        auto action = d.value("action", std::string{});
        cmd.clickEvent = action == "double"        ? WM_LBUTTONDBLCLK
                         : action == "rightDouble" ? WM_RBUTTONDBLCLK
                                                   : WM_LBUTTONUP;
        cmd.x = static_cast<float>(p.x);
        cmd.y = static_cast<float>(p.y);
        if (d.value("nativePointer", false) && action != "rightDouble") {
            cmd.operation = BridgeOperation::Invoke;
            cmd.clickEvent = action == "menu" ? 1 : action == "double" ? 2 : 0;
            cmd.visibility = d.value("keyboard", false) ? 1 : 0;
            cmd.x = d.value("x", cmd.x);
            cmd.y = d.value("y", cmd.y);
        }
        RecordAccess(catalog.items[key].activity, Now());
        auto stable = PersistentKey(SelectorFor(Record(*row)));
        activityHistory[stable]["lastAccess"] = Now();
        activityDirty = true;
        applied.erase(key);
        result = Result(bridge.Command(cmd), "原应用回调",
                        bridge.shared ? bridge.shared->lastError : ERROR_NOT_READY);
    } else if (method == "agent.stop") {
        lifecycle.state = SessionState::UserStopping;
        stopping = true;
        SetTimer(window, 2, 100, nullptr);
    } else
        return Result(false, "未知命令", ERROR_INVALID_FUNCTION);
    result["revision"] = configRevision;
    result["requestId"] = req.value("requestId", std::string{});
    return result;
}
DWORD WINAPI Serve(void*) {
    auto sd = CurrentUserSecurity();
    if (!sd)
        return 1;
    SECURITY_ATTRIBUTES sa{sizeof(sa), sd, FALSE};
    auto name = L"\\\\.\\pipe\\TrayAssistant.v1." + std::to_wstring(session);
    while (WaitForSingleObject(stopEvent, 0) == WAIT_TIMEOUT) {
        HANDLE pipe = CreateNamedPipeW(name.c_str(), PIPE_ACCESS_DUPLEX,
                                       PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT |
                                           PIPE_REJECT_REMOTE_CLIENTS,
                                       1, 65536, 65536, 0, &sa);
        if (pipe == INVALID_HANDLE_VALUE)
            break;
        if (ConnectNamedPipe(pipe, nullptr) || GetLastError() == ERROR_PIPE_CONNECTED) {
            try {
                std::string input;
                char chunk[4096];
                DWORD read{};
                while (input.find('\n') == std::string::npos && input.size() < 4 * 1024 * 1024 &&
                       ReadFile(pipe, chunk, sizeof(chunk), &read, nullptr) && read)
                    input.append(chunk, read);
                if (input.find('\n') != std::string::npos) {
                    auto request = std::make_shared<Request>();
                    request->input = Json::parse(input.substr(0, input.find('\n')));
                    Enqueue(request);
                    HANDLE waits[] = {request->event, stopEvent};
                    if (WaitForMultipleObjects(2, waits, FALSE, 10000) == WAIT_OBJECT_0) {
                        auto output = request->output.dump() + "\n";
                        DWORD written{};
                        WriteFile(pipe, output.data(), static_cast<DWORD>(output.size()), &written,
                                  nullptr);
                    }
                }
            } catch (...) {
                const char error[] = "{\"success\":false,\"detail\":\"无效请求\"}\n";
                DWORD written{};
                WriteFile(pipe, error, sizeof(error) - 1, &written, nullptr);
            }
        }
        FlushFileBuffers(pipe);
        DisconnectNamedPipe(pipe);
        CloseHandle(pipe);
    }
    LocalFree(sd);
    return 0;
}
DWORD WINAPI Worker(void*) {
    CoInitializeEx(nullptr, COINIT_MULTITHREADED);
    auto start = SessionState::Starting;
    lifecycle.state.compare_exchange_strong(start, SessionState::WaitingForShell);
    if (ShouldRecover(lifecycle))
        SetOwnIcon();
    while (!stopping && WaitForSingleObject(stopEvent, 0) == WAIT_TIMEOUT) {
        std::deque<std::shared_ptr<Request>> jobs;
        {
            std::lock_guard lock(queueMutex);
            jobs.swap(requests);
        }
        for (auto& request : jobs) {
            SetPropW(window, L"TrayAssistant.Busy", reinterpret_cast<HANDLE>(1));
            try {
                request->output = Handle(request->input);
            } catch (const std::exception& e) {
                request->output = Result(false, e.what(), ERROR_INVALID_DATA);
            }
            if (request->input.is_object() && request->input.contains("method") &&
                request->input["method"] == "internal.openUi")
                ownOpenQueued = false;
            RemovePropW(window, L"TrayAssistant.Busy");
            request->output["requestId"] = request->input.is_object() &&
                                                   request->input.contains("requestId") &&
                                                   request->input["requestId"].is_string()
                                               ? request->input["requestId"]
                                               : Json("");
            SetEvent(request->event);
        }
        try {
            Refresh();
        } catch (...) {
            applied.clear();
            seenSequence = -1;
        }
        HANDLE waits[] = {stopEvent, workEvent};
        WaitForMultipleObjects(2, waits, FALSE, 500);
    }
    if (lifecycle.state == SessionState::SessionEnding) {
        if (bridge.shared)
            InterlockedExchange(&bridge.shared->sessionEnding, 1);
    }
    if (lifecycle.state != SessionState::SessionEnding && activityDirty)
        SaveDataJson(dataRoot / L"activity.json", activityHistory);
    bridge.Reset();
    NOTIFYICONDATAW d{};
    d.cbSize = sizeof(d);
    d.hWnd = window;
    d.uID = 1;
    if (ownAdded)
        Shell_NotifyIconW(NIM_DELETE, &d);
    ownAdded = false;
    if (ownImage) {
        DestroyIcon(ownImage);
        ownImage = nullptr;
    }
    CoUninitialize();
    return 0;
}
void TrayMenu() {
    // Win32 PMv2 menus use the system's menu font at the monitor's actual DPI.
    auto previousDpi = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    HMENU menu = CreatePopupMenu();
    if (!menu) {
        if (previousDpi) SetThreadDpiAwarenessContext(previousDpi);
        return;
    }
    AppendMenuW(menu, MF_STRING, 1, L"打开助手");
    AppendMenuW(menu, MF_STRING, 2, L"重新启动");
    AppendMenuW(menu, MF_STRING, 3, L"退出");
    POINT point{};
    GetCursorPos(&point);
    SetForegroundWindow(window);
    auto selected =
        TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_LEFTALIGN | TPM_BOTTOMALIGN,
                       point.x, point.y, 0, window, nullptr);
    DestroyMenu(menu);
    PostMessageW(window, WM_NULL, 0, 0);
    if (previousDpi) SetThreadDpiAwarenessContext(previousDpi);
    if (selected == 1)
        PostJob("internal.openUi");
    else if (selected == 2) {
        if (RestartAssistant() != ERROR_SUCCESS)
            MessageBoxW(window, L"无法启动重启进程。当前助手与配置仍保留，请稍后重试。",
                        L"托盘图标助手", MB_OK | MB_ICONERROR);
    } else if (selected == 3) {
        auto ui = uiWindow.load();
        if (!IsAssistantUi(ui) || !PostMessageW(ui, WM_CLOSE, 0, 0))
            PostMessageW(window, WM_CLOSE, 0, 0);
    }
}
LRESULT CALLBACK WindowProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    static UINT taskbarCreated = RegisterWindowMessageW(L"TaskbarCreated");
    if (msg == taskbarCreated) {
        PostJob("internal.shellCreated");
        return 0;
    }
    if (msg == WM_QUERYENDSESSION) {
        HandleSessionQuery(lifecycle);
        return TRUE;
    }
    if (msg == WM_ENDSESSION) {
        HandleSessionEnd(lifecycle, wp != 0);
        if (wp) {
            stopping = true;
            SetEvent(endingEvent);
            PostMessageW(hwnd, WM_CLOSE, 0, 0);
        }
        return 0;
    }
    if (msg == WM_TIMER) {
        if (wp == 2) {
            KillTimer(hwnd, 2);
            PostMessageW(hwnd, WM_CLOSE, 0, 0);
        }
        return 0;
    }
    if (msg == UpdateHotkeys) {
        RegisterRequestedHotkeys();
        return 0;
    }
    if (msg == OpenMessage) {
        PostJob("internal.openUi");
        return 0;
    }
    if (msg == WM_HOTKEY) {
        for (const auto& item : registeredHotkeys)
            if (item.at("id").get<int>() == int(wp)) {
                auto action = item.at("action").get<std::string>();
                if (action == "open")
                    PostJob("internal.openUi");
                else if (action == "menu")
                    TrayMenu();
                else if (action == "icon")
                    PostJob("icon.invoke",
                            {{"key", item.at("key")}, {"action", item.at("iconAction")}});
                break;
            }
        return 0;
    }
    if (msg == OwnCallback) {
        auto event = LOWORD(lp);
        if (event == WM_CONTEXTMENU)
            TrayMenu();
        else if (event == NIN_SELECT || event == NIN_KEYSELECT || event == WM_LBUTTONUP ||
                 event == WM_LBUTTONDBLCLK) {
            if (!ownOpenQueued.exchange(true))
                PostJob("internal.openUi");
        }
        return 0;
    }
    if (msg == WM_CLOSE) {
        stopping = true;
        if (lifecycle.state != SessionState::SessionEnding)
            lifecycle.state = SessionState::UserStopping;
        SetEvent(stopEvent);
        if (pipeThread)
            CancelSynchronousIo(pipeThread);
        for (const auto& item : registeredHotkeys)
            UnregisterHotKey(hwnd, item.at("id"));
        PostQuitMessage(0);
        return 0;
    }
    if (msg == WM_DESTROY) {
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(hwnd, msg, wp, lp);
}
} // namespace
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR, int) {
    // The Agent is a separate executable; the App.exe DPI manifest does not cover it.
    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    ProcessIdToSessionId(GetCurrentProcessId(), &session);
    int argc{};
    auto args = CommandLineToArgvW(GetCommandLineW(), &argc);
    bool background = true;
    bool restartWaiting{};
    HANDLE restartHandles[3]{};

    wchar_t executable[32768]{};
    GetModuleFileNameW(nullptr, executable, 32768);
    root = std::filesystem::path(executable).parent_path();
    wchar_t local[32768]{};
    GetEnvironmentVariableW(L"LOCALAPPDATA", local, 32768);
    dataRoot = std::filesystem::path(local) / L"TrayIconAssistant";
    for (int i = 1; i < argc; ++i) {
        if (std::wstring(args[i]) == L"--background")
            background = true;
        else if (std::wstring(args[i]) == L"--open")
            background = false;
        else if (std::wstring(args[i]) == L"--data" && i + 1 < argc)
            dataRoot = args[++i];
        else if (std::wstring(args[i]) == L"--restart-wait") {
            if (i + 3 >= argc) { LocalFree(args); return 1; }
            restartWaiting = true;
            for (auto& handle : restartHandles) {
                auto value = args[++i];
                wchar_t* end{};
                auto integer = wcstoull(value, &end, 10);
                if (!*value || *end || *value == L'-') { LocalFree(args); return 1; }
                handle = reinterpret_cast<HANDLE>(static_cast<uintptr_t>(integer));
            }
        }
    }
    LocalFree(args);
    if (restartWaiting) {
        bool inherited = restartHandles[0] && restartHandles[2];
        for (auto handle : restartHandles) {
            if (!handle) continue;
            DWORD flags{};
            inherited &= GetHandleInformation(handle, &flags) && (flags & HANDLE_FLAG_INHERIT);
        }
        bool finished = inherited && WaitForRestartExit(restartHandles[0], restartHandles[1],
                                                       restartHandles[2]);
        for (auto handle : restartHandles) if (handle) CloseHandle(handle);
        if (!finished) return 1;
        background = false;
    }
    CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    auto sd = CurrentUserSecurity();
    if (!sd)
        return 1;
    SECURITY_ATTRIBUTES security{sizeof(security), sd, FALSE};
    singleton = CreateMutexW(&security, FALSE,
                             (L"Local\\TrayAssistant.Agent." + std::to_wstring(session)).c_str());
    auto mutexError = GetLastError();
    if (sd)
        LocalFree(sd);
    if (!singleton)
        return 1;
    if (mutexError == ERROR_ALREADY_EXISTS) {
        if (!background)
            PostMessageW(FindWindowW(L"TrayAssistant.Agent", nullptr), OpenMessage, 0, 0);
        CloseHandle(singleton);
        return 0;
    }
    try {
        settingsPath = dataRoot / L"settings.json";
        bool firstUse = !std::filesystem::exists(settingsPath);
        settings = LoadSettings(settingsPath, recovered);
        if (firstUse) {
            wchar_t common[32768]{};
            GetEnvironmentVariableW(L"PROGRAMDATA", common, std::size(common));
            auto defaults = std::filesystem::path(common) / L"TrayIconAssistant" / L"Defaults" /
                            L"settings.json";
            if (std::filesystem::is_regular_file(defaults)) {
                bool ignored{};
                auto inherited = LoadSettings(defaults, ignored);
                if (!ignored && ValidateSettings(inherited))
                    settings = std::move(inherited);
            }
        }
        configRevision = settings.value("revision", uint64_t{});

        try {
            auto activityPath = dataRoot / L"activity.json";
            if (std::filesystem::exists(activityPath) &&
                std::filesystem::file_size(activityPath) < 4 * 1024 * 1024) {
                std::ifstream file(activityPath);
                file >> activityHistory;
                if (!activityHistory.is_object())
                    activityHistory = Json::object();
            }
        } catch (...) {
            activityHistory = Json::object();
        }
        settings.erase("safeMode");
        if (settings.contains("hotkeys"))
            settings["hotkeys"].erase("restoreIcons");
        for (auto& entry : settings["rules"])
            entry["enabled"] = true;
        LoadRules();
        adapter = LoadAdapter(root / L"supported-adapters.json");
        auto endingSd = CurrentUserSecurity();
        if (!endingSd)
            throw std::runtime_error("Ending signal security unavailable");
        SECURITY_ATTRIBUTES endingSecurity{sizeof(endingSecurity), endingSd, FALSE};
        endingEvent = CreateEventW(
            &endingSecurity, TRUE, FALSE,
            EndingEventName(GetCurrentProcessId(), Generation(GetCurrentProcessId())).c_str());
        if (endingSd)
            LocalFree(endingSd);
        if (!endingEvent)
            throw std::runtime_error("Ending signal unavailable");
        stopEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
        workEvent = CreateEventW(nullptr, FALSE, FALSE, nullptr);
        WNDCLASSW wc{};
        wc.hInstance = instance;
        wc.lpfnWndProc = WindowProc;
        wc.lpszClassName = L"TrayAssistant.Agent";
        RegisterClassW(&wc);
        window = CreateWindowW(wc.lpszClassName, L"托盘图标助手后台", WS_OVERLAPPED, 0, 0, 0, 0,
                               nullptr, nullptr, instance, nullptr);
        if (!window)
            throw std::runtime_error("Session window unavailable");
        ScheduleHotkeys();
        workerThread = CreateThread(nullptr, 0, Worker, nullptr, 0, nullptr);
        pipeThread = CreateThread(nullptr, 0, Serve, nullptr, 0, nullptr);
        if (!workerThread || !pipeThread)
            throw std::runtime_error("Worker unavailable");
        if (!background)
            PostJob("internal.openUi");
        MSG message{};
        while (GetMessageW(&message, nullptr, 0, 0) > 0) {
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
        if (pipeThread) {
            CancelSynchronousIo(pipeThread);
            WaitForSingleObject(pipeThread, 100);
            CloseHandle(pipeThread);
        }
        if (workerThread) {
            if (WaitForSingleObject(workerThread, 900) != WAIT_OBJECT_0)
                ExitProcess(0);
            CloseHandle(workerThread);
        }
        DestroyWindow(window);
        CloseHandle(workEvent);
        CloseHandle(endingEvent);
        CloseHandle(stopEvent);
        CloseHandle(singleton);
        if (restartHandoff) CloseHandle(restartHandoff);

        CoUninitialize();
        return 0;
    } catch (...) {
        if (singleton)
            CloseHandle(singleton);
        if (restartHandoff) CloseHandle(restartHandoff);
        CoUninitialize();
        return 1;
    }
}
