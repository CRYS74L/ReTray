#include "../core/notify_patch.h"

#include "../platform/user_security.h"
#include "../shell/bridge_client.h"
#include "settings.h"
#include "logon_startup.h"
#include "tray_menu_typography.h"

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
const UINT ExitInterfaceMessage = RegisterWindowMessageW(L"TrayAssistant.ExitInterface.v1");
const UINT ShowTrayMenuMessage = RegisterWindowMessageW(L"TrayAssistant.ShowMenu.v1");
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
std::unordered_map<std::string, Frame> presentationFrames;
std::unordered_map<std::string, uint64_t> registrations;
bool HasVisiblePixels(const uint32_t* pixels, uint32_t width, uint32_t height) {
    return width && height && std::any_of(pixels, pixels + width * height,
                                         [](uint32_t pixel) { return (pixel >> 24) != 0; });
}
void RememberPresentationFrame(const BridgeIcon& row) {
    auto key = IdentityKey(row.identity);
    auto& preview = presentationFrames[key];
    if (HasVisiblePixels(row.pixels, row.width, row.height)) {
        preview.width = row.width; preview.height = row.height;
        preview.pixels.assign(row.pixels, row.pixels + row.width * row.height);
    } else if (preview.pixels.empty() &&
               HasVisiblePixels(row.originalPixels, row.originalWidth, row.originalHeight)) {
        preview.width = row.originalWidth; preview.height = row.originalHeight;
        preview.pixels.assign(row.originalPixels,
                              row.originalPixels + row.originalWidth * row.originalHeight);
    }
}
IconCatalog catalog;
LONG seenSequence{-1};
uint64_t lastCatalogRefresh{};
uint64_t configRevision{};
uint64_t environmentEpoch{}, lastEnvironmentCheck{};
std::string environmentStamp;
std::unordered_map<int, bool> environmentPolicies;
std::array<bool, 10> environmentAvailable{};
Json environmentOutcome = {{"success", true}};
Json startupOutcome = {{"success", true}};
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
uint64_t lastActivitySave{};

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
    try {
        auto candidate = NormalizeSettings(settings);
        candidate["revision"] = configRevision + 1;
        if (!SaveSettings(settingsPath, candidate)) return false;
        settings = std::move(candidate);
        ++configRevision;
        return true;
    } catch (...) { return false; }
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
    std::lock_guard lock(hotkeyMutex);
    if (requestedHotkeys.empty() && registeredHotkeys.empty())
        return;
    requestedHotkeys = Json::array();
    PostMessageW(window, UpdateHotkeys, 0, 0);
}
void RegisterRequestedHotkeys() {
    std::lock_guard lock(hotkeyMutex);
    for (const auto& old : registeredHotkeys)
        UnregisterHotKey(window, old.at("id"));
    requestedHotkeys = Json::array();
    registeredHotkeys = Json::array();
    hotkeyErrors = Json::array();
}
void OpenUI() {
    auto name = Wide(settings.value("uiActivation", std::string{}));
    IApplicationActivationManager* manager{};
    if (!name.empty() && SUCCEEDED(CoCreateInstance(CLSID_ApplicationActivationManager, nullptr, CLSCTX_LOCAL_SERVER,
                                   IID_IApplicationActivationManager,
                                   reinterpret_cast<void**>(&manager)))) {
        DWORD pid{};
        auto result = manager->ActivateApplication(name.c_str(), nullptr, AO_NONE, &pid);
        manager->Release();
        if (SUCCEEDED(result))
            return;
    }
    // The UI is also usable without package registration. Derive its executable
    // from this installation rather than accepting a command from configuration.
    std::wstring module(32768, L'\0');
    auto length = GetModuleFileNameW(nullptr, module.data(), static_cast<DWORD>(module.size()));
    if (!length || length >= module.size())
        return;
    module.resize(length);
    auto executable = std::filesystem::path(module).parent_path() / L"TrayAssistant.App.exe";
    std::wstring command = L"\"" + executable.wstring() + L"\"";
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    if (CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE, 0, nullptr,
                       executable.parent_path().c_str(), &startup, &process)) {
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
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
bool SetStartup(bool enabled, bool previousEnabled) {
    startupOutcome.erase("rollbackFailed");
    std::filesystem::path startupExecutable = root / L"TrayAssistant.Agent.exe";
    std::wstring startupArguments = L"--background";
    UINT32 packageNameLength{};
    bool packaged = GetCurrentPackageFullName(&packageNameLength, nullptr) == ERROR_INSUFFICIENT_BUFFER;
    HKEY key{};
    bool retireRun{};
    if (!packaged) {
        auto opened = RegOpenKeyExW(HKEY_CURRENT_USER,
            L"Software\\Microsoft\\Windows\\CurrentVersion\\Run", 0,
            KEY_QUERY_VALUE | KEY_SET_VALUE, &key);
        if (opened != ERROR_SUCCESS && opened != ERROR_FILE_NOT_FOUND) {
            SetLastError(opened); return false;
        }
        if (key) {
            auto command = L"\"" + startupExecutable.wstring() + L"\" --background";
            wchar_t previous[32768]{}; DWORD bytes = sizeof(previous), type{};
            auto code = RegQueryValueExW(key, L"TrayIconAssistant", nullptr, &type,
                                       reinterpret_cast<BYTE*>(previous), &bytes);
            if (code != ERROR_SUCCESS && code != ERROR_FILE_NOT_FOUND && code != ERROR_MORE_DATA) {
                RegCloseKey(key); SetLastError(code); return false;
            }
            retireRun = code == ERROR_SUCCESS && type == REG_SZ && bytes <= sizeof(previous) &&
                        previous[std::size(previous) - 1] == 0 && command == previous;
        }
    }
    if (packaged && enabled) {
        UINT32 familyLength{};
        GetCurrentPackageFamilyName(&familyLength, nullptr);
        std::vector<wchar_t> family(familyLength);
        auto code = GetCurrentPackageFamilyName(&familyLength, family.data());
        if (code != ERROR_SUCCESS) { SetLastError(code); return false; }
        wchar_t local[32768]{};
        GetEnvironmentVariableW(L"LOCALAPPDATA", local, std::size(local));
        startupExecutable = std::filesystem::path(local) / L"Packages" / family.data() /
                            L"LocalState" / L"Startup" / L"TrayAssistant.Startup-v1.exe";
        try { std::filesystem::create_directories(startupExecutable.parent_path()); }
        catch (...) { SetLastError(ERROR_CANNOT_MAKE); return false; }
        // The small v1 activator is version-independent and never reads personal settings.
        if (!CopyFileW((root / L"TrayAssistant.Startup.exe").c_str(), startupExecutable.c_str(), TRUE) &&
            GetLastError() != ERROR_FILE_EXISTS) return false;
        startupArguments = L"\"" + std::wstring(family.data()) + L"!Background\"";
    }
    auto scheduled = SetLogonStartupResult(startupExecutable, enabled, StartupTaskName(), startupArguments);
    if (scheduled != ERROR_SUCCESS) {
        if (key) RegCloseKey(key);
        SetLastError(scheduled); return false;
    }
    if (packaged) {
        // Keep the existing extension as an upgrade fallback until our task is created.
        // StartupTask is scoped to the App identity that declares it. Background must
        // not query it; our immediate task is already valid under either identity.
        UINT32 applicationLength{};
        GetCurrentApplicationUserModelId(&applicationLength, nullptr);
        std::vector<wchar_t> application(applicationLength);
        if (applicationLength &&
            GetCurrentApplicationUserModelId(&applicationLength, application.data()) == ERROR_SUCCESS &&
            std::wstring_view(application.data()).ends_with(L"!App")) {
            try {
                auto operation = winrt::Windows::ApplicationModel::StartupTask::GetAsync(L"TrayAssistantStartup");
                auto deadline = GetTickCount64() + 3000;
                while (operation.Status() == winrt::Windows::Foundation::AsyncStatus::Started &&
                       !stopping && GetTickCount64() < deadline)
                    operation.wait_for(std::chrono::milliseconds(25));
                if (operation.Status() == winrt::Windows::Foundation::AsyncStatus::Started)
                    operation.Cancel();
                else operation.get().Disable();
            } catch (...) {
                // An unavailable legacy extension cannot undo the successfully applied task.
                // The single-instance guard also prevents the fallback starting a second agent.
            }
        }
        return true;
    }
    // Only an exact legacy command is retired. Failure restores the task's prior preference.
    if (retireRun) {
        auto code = RegDeleteValueW(key, L"TrayIconAssistant");
        if (code != ERROR_SUCCESS && code != ERROR_FILE_NOT_FOUND) {
            auto restored = SetLogonStartupResult(startupExecutable, previousEnabled,
                                                  StartupTaskName(), startupArguments);
            RegCloseKey(key);
            if (restored != ERROR_SUCCESS) startupOutcome["rollbackFailed"] = true;
            SetLastError(restored == ERROR_SUCCESS ? code : restored);
            return false;
        }
    }
    if (key) RegCloseKey(key);
    return true;
}
int EffectiveVisibility(const BridgeIcon& row, const IconRule* rule) {
    (void)row;
    return rule && rule->enabled ? rule->visibility : -1;
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
            if (send(cmd)) {
                auto preview = RasterizeIcon(image, FrameOrigin::Custom);
                if (preview.outcome.success && !preview.frame.transparent)
                    presentationFrames[key] = std::move(preview.frame);
            }
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
        cmd.index = 0;
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
bool NativeSystemControlPresent(int id) {
    if (!bridge.shared)
        return false;
    for (uint32_t index = 0; index < std::min<uint32_t>(32, bridge.shared->systemCount); ++index) {
        const auto& row = bridge.shared->systemControls[index];
        if (row.id == static_cast<uint32_t>(id) && row.instances)
            return true;
    }
    return false;
}
bool RefreshSystemControls() {
    if (!bridge.Alive() || !ShouldRecover(lifecycle) || stopping)
        return false;
    if (environmentEpoch != bridge.shared->epoch) {
        environmentPolicies.clear();
        environmentAvailable.fill(false);
        environmentStamp.clear();
        environmentEpoch = bridge.shared->epoch;
    }
    lastEnvironmentCheck = GetTickCount64();
    BridgeCommand command;
    command.operation = BridgeOperation::SystemRead;
    if (!bridge.Command(command))
        return false;
    for (const auto& policy : settings.value("systemControls", Json::array()))
        environmentAvailable[policy.at("id").get<int>()] = NativeSystemControlPresent(policy.at("id").get<int>());
    return true;
}
bool ApplyEnvironment(bool force = false) {
    if (!bridge.Alive() || !ShouldRecover(lifecycle))
        return false;
    auto stamp = settings.value("systemControls", Json::array()).dump();
    bool shellChanged = environmentEpoch != bridge.shared->epoch;
    if (!force && !shellChanged && environmentStamp == stamp) {
        bool hasHiddenPolicy = std::any_of(environmentPolicies.begin(), environmentPolicies.end(),
                                           [](const auto& policy) { return policy.second; });
        if (hasHiddenPolicy && GetTickCount64() - lastEnvironmentCheck >= 3000 &&
            !RefreshSystemControls())
            environmentOutcome = Result(false, "当前系统图标对象无法刷新",
                                         bridge.shared->lastError ? bridge.shared->lastError : ERROR_TIMEOUT);
        return environmentOutcome.value("success", true);
    }
    auto previous = shellChanged ? std::unordered_map<int, bool>{} : environmentPolicies;
    if (shellChanged)
        environmentAvailable.fill(false);
    environmentEpoch = bridge.shared->epoch;
    environmentStamp = std::move(stamp);
    lastEnvironmentCheck = GetTickCount64();
    environmentPolicies.clear();
    for (const auto& policy : settings.value("systemControls", Json::array()))
        environmentPolicies[policy.at("id").get<int>()] = policy.value("hidden", false);
    bool ok = true;
    DWORD error{};
    auto apply = [&](int id, bool hidden) {
        BridgeCommand command;
        command.operation = BridgeOperation::SystemPolicy;
        command.location = id;
        command.visibility = hidden ? 1 : 0;
        if (!bridge.Command(command)) {
            ok = false;
            error = bridge.shared->lastError ? bridge.shared->lastError : ERROR_TIMEOUT;
        }
        environmentAvailable[id] = NativeSystemControlPresent(id);
    };
    for (const auto& [id, hidden] : previous)
        if (!environmentPolicies.contains(id))
            apply(id, false);
    for (const auto& [id, hidden] : environmentPolicies)
        if (force || !previous.contains(id) || previous.at(id) != hidden)
            apply(id, hidden);
    environmentOutcome =
        Result(ok, ok ? "系统图标设置已生效" : "已保存的系统图标策略当前无法应用", error);
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
bool ReconcileNativeCatalog(bool force = false) {
    const auto now = GetTickCount64();
    if (!force && lastCatalogRefresh && now - lastCatalogRefresh < 2000)
        return true;
    // Exit/window destruction can remove a native registration without NIM_DELETE.
    // Bound the read frequency independently of source icon updates and failed reads.
    lastCatalogRefresh = now;
    BridgeCommand command;
    command.operation = BridgeOperation::Refresh;
    command.index = 0; // Membership only: retain source and blink presentation frames.
    return bridge.Command(command);
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
        lastCatalogRefresh = 0;
        registrations.clear();
        presentationFrames.clear();
        outcomes.clear();
        applied.clear();
        failedStamp.clear();
        retryAfter.clear();
        SetOperationalState(lifecycle, SessionState::Ready);
    }
    ApplyEnvironment();
    if (!ReconcileNativeCatalog())
        return;
    if (activityDirty && GetTickCount64() - lastActivitySave >= 30000) {
        if (SaveDataJson(dataRoot / L"activity.json", activityHistory))
            activityDirty = false;
        lastActivitySave = GetTickCount64();
    }
    LONG sequence = bridge.shared->snapshotSequence;
    if ((sequence & 1) || sequence == seenSequence)
        return;
    bool complete{};
    auto current = bridge.Snapshot(&complete);
    if (!complete || sequence != bridge.shared->snapshotSequence)
        return;
    SnapshotResult snapshot;
    snapshot.complete = true;
    for (const auto& row : current)
        snapshot.records.push_back(Record(row));
    auto delta = MergeSnapshot(catalog, snapshot, Now());
    if (!delta.accepted)
        return;
    seenSequence = sequence;
    rows = std::move(current);
    std::unordered_set<std::string> present;
    for (const auto& row : rows) {
        auto key = IdentityKey(row.identity);
        present.insert(key);
        auto [known, inserted] = registrations.try_emplace(key, row.registration);
        if (!inserted && known->second != row.registration) {
            // DELETE/ADD can complete between polls with the same HWND/UID or GUID.
            // The new registration must inherit saved policy and its own source image.
            known->second = row.registration;
            applied.erase(key);
            failedStamp.erase(key);
            retryAfter.erase(key);
            outcomes.erase(key);
            presentationFrames.erase(key);
        }
        RememberPresentationFrame(row);
    }
    for (auto it = registrations.begin(); it != registrations.end();)
        if (!present.contains(it->first)) it = registrations.erase(it); else ++it;
    for (auto it = presentationFrames.begin(); it != presentationFrames.end();)
        if (!present.contains(it->first)) it = presentationFrames.erase(it); else ++it;
    for (const auto& row : rows)
        RememberOwnIcon(row);
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
    for (auto it = outcomes.begin(); it != outcomes.end();)
        if (!catalog.items.contains(it->first))
            it = outcomes.erase(it);
        else
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
        // Applications may register hidden image banks alongside their real icon.
        // Preserve them internally; list icons the source exposes or the user manages.
        if (row.sourceHidden && !row.managed)
            continue;
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
        j["canPersist"] = owned || !record.executable.empty();
        j["stableKey"] = StableIconKey(row);
        if (owned) {
            j["asset"] = settings["ownIcon"].value("asset", std::string{});
            j["tip"] = settings["ownIcon"].value("tip", std::string("托盘图标助手"));
        }
        j["key"] = key;
        j["registration"] = row.registration;
        j["sourceHidden"] = row.sourceHidden;
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
        RememberPresentationFrame(row);
        const auto& preview = presentationFrames.at(key);
        j["displayWidth"] = preview.width;
        j["displayHeight"] = preview.height;
        j["displayPixels"] = Bytes64(preview.pixels.data(), preview.pixels.size());
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
            !PostMessageW(ui ? ui : window, ui ? ExitInterfaceMessage : WM_CLOSE, 0, 0)) {
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
    if (!ReconcileNativeCatalog(true))
        return Result(false, "原生图标目录读取失败，请重试",
                      bridge.shared->lastError ? bridge.shared->lastError : ERROR_TIMEOUT);
    bool complete{};
    auto current = bridge.Snapshot(&complete);
    if (!complete)
        return Result(false, "图标状态更新中，请重试", ERROR_RETRY);
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
    if (!target->executable[0])
        return Result(false, "暂时无法识别图标所属程序，配置未改变", ERROR_NOT_READY);
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
Json SetSystemVisibility(const Json& data) {
    if (!data.contains("id") || !data.at("id").is_number_integer() ||
        !data.contains("visible") || !data.at("visible").is_boolean()) {
        auto result = Result(false, "系统图标开关参数无效", ERROR_INVALID_DATA);
        result["saved"] = false;
        return result;
    }
    const auto& requested = data.at("id");
    if (requested.is_number_unsigned() ? requested.get<uint64_t>() >= 10
                                      : requested.get<int64_t>() < 0 || requested.get<int64_t>() >= 10) {
        auto result = Result(false, "系统图标编号无效", ERROR_INVALID_DATA);
        result["saved"] = false;
        return result;
    }
    int id = requested.get<int>();
    bool visible = data.at("visible");
    auto reply = [&](bool success, const std::string& detail, DWORD error = 0,
                     bool saved = false) {
        auto result = Result(success, detail, error);
        result["saved"] = saved;
        result["data"] = {{"id", id}, {"visible", visible}};
        return result;
    };
    if (!ShouldRecover(lifecycle) || stopping || !bridge.Alive())
        return reply(false, "原生系统图标尚未就绪", ERROR_NOT_READY);
    auto nativeError = [&]() -> DWORD {
        return bridge.shared && bridge.shared->lastError ? bridge.shared->lastError : ERROR_TIMEOUT;
    };
    BridgeCommand command;
    command.operation = BridgeOperation::SystemRead;
    if (!bridge.Command(command))
        return reply(false, "无法读取当前系统图标，配置未改变", nativeError());
    bool exists = NativeSystemControlPresent(id);
    if (environmentEpoch == bridge.shared->epoch)
        environmentAvailable[id] = exists;
    if (!exists && !visible && id != 6)
        return reply(false, "所选系统图标当前不存在，无法隐藏，配置未改变", ERROR_NOT_FOUND);
    auto previous = settings;
    bool priorHidden = false;
    for (const auto& policy : previous.value("systemControls", Json::array()))
        if (policy.at("id") == id)
            priorHidden = policy.value("hidden", false);
    command.operation = id == 6 ? BridgeOperation::SystemPolicy : BridgeOperation::SystemSet;
    command.location = id;
    command.visibility = visible ? 0 : 1;
    auto restorePolicy = [&]() {
        command.visibility = priorHidden ? 1 : 0;
        return bridge.Command(command);
    };
    // Restoring an absent target also forgets its native session intent, so it cannot
    // hide a future object after the persisted preference has been cleared.
    if (!bridge.Command(command)) {
        auto error = nativeError();
        bool restored = error == ERROR_NOT_FOUND || restorePolicy();
        auto result = reply(false, restored ? "所选系统图标更新失败，配置未改变"
                                            : "所选系统图标更新失败，原策略恢复失败，配置未改变",
                            error);
        result["restored"] = restored;
        return result;
    }
    auto& policies = settings["systemControls"];
    for (auto entry = policies.begin(); entry != policies.end();)
        if (entry->at("id") == id)
            entry = policies.erase(entry);
        else
            ++entry;
    if (!visible)
        policies.push_back({{"id", id}, {"hidden", true}});
    if (!Persist()) {
        settings = previous;
        bool restored = !exists || restorePolicy();
        auto result = reply(false, restored ? "配置保存失败，已恢复原系统图标策略"
                                            : "配置保存失败，原系统图标策略恢复失败",
                            ERROR_WRITE_FAULT);
        result["restored"] = restored;
        return result;
    }
    // This target has already been applied. Keep the background policy diff in sync so
    // the successful transaction cannot replay another target's unchanged failure.
    bool synchronized = environmentEpoch == bridge.shared->epoch &&
                        environmentStamp == previous.value("systemControls", Json::array()).dump();
    if (environmentEpoch != bridge.shared->epoch) {
        environmentPolicies.clear();
        environmentAvailable.fill(false);
        environmentEpoch = bridge.shared->epoch;
    }
    environmentAvailable[id] = exists;
    if (visible)
        environmentPolicies.erase(id);
    else
        environmentPolicies[id] = true;
    environmentStamp = synchronized ? settings["systemControls"].dump() : std::string{};
    return reply(true, exists ? (visible ? "已恢复系统图标" : "已隐藏系统图标")
                              : "已清除此系统图标的隐藏偏好，当前未创建系统图标",
                 0, true);
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
    } else if (method == "assistant.menu") {
        bool ok = PostMessageW(window, ShowTrayMenuMessage, 0, 0) != FALSE;
        return Result(ok, "助手菜单", ok ? ERROR_SUCCESS : GetLastError());
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
            {"startup", startupOutcome},
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
        if (!RefreshSystemControls())
            return Result(false, "系统控件读取不可用",
                          bridge.shared ? (bridge.shared->lastError ? bridge.shared->lastError : ERROR_TIMEOUT)
                                        : ERROR_NOT_READY);
        result["data"] = Json::array();
        std::array<bool, 10> savedHidden{}, present{};
        for (const auto& policy : settings.value("systemControls", Json::array()))
            savedHidden[policy.at("id").get<int>()] = policy.value("hidden", false);
        for (uint32_t i = 0; i < bridge.shared->systemCount; ++i) {
            auto row = bridge.shared->systemControls[i];
            bool saved = row.id < savedHidden.size() && savedHidden[row.id];
            if (row.id < present.size())
                present[row.id] = true;
            result["data"].push_back(
                {{"id", row.id}, {"visible", row.visible}, {"instances", row.instances},
                 {"savedHidden", saved}, {"glyph", Utf8(row.glyph)},
                 {"fontFamily", Utf8(row.fontFamily)},
                 {"prePaintHides", row.prePaintHides}, {"visibilityHides", row.visibilityHides}});
        }
        for (size_t id = 0; id < savedHidden.size(); ++id)
            if ((savedHidden[id] || id == 6) && !present[id])
                result["data"].push_back({{"id", id}, {"instances", 0}, {"visible", false},
                                           {"savedHidden", savedHidden[id]}});
    } else if (method == "system.set") {
        result = SetSystemVisibility(d);
    } else if (method == "system.apply") {
        result = Result(false, "请使用单个系统图标开关", ERROR_NOT_SUPPORTED);
        result["saved"] = false;
    } else if (method == "taskbar.apply") {
        result = Result(false, "任务栏高级功能已取消", ERROR_NOT_SUPPORTED);
        result["saved"] = false;
    } else if (method == "settings.import") {
        result = Result(false, "配置导入功能已取消", ERROR_NOT_SUPPORTED);
        result["saved"] = false;
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
                key != "newIconLocation" && key != "systemControls")
                settings[key] = value;
        bool changedStartup = d.contains("startup") && settings["startup"].is_boolean();
        if (!ValidateSettings(settings)) {
            settings = saved;
            return Result(false, "设置参数无效，配置未改变", ERROR_INVALID_DATA);
        }
        if (changedStartup && !SetStartup(settings["startup"].get<bool>(), saved.value("startup", false))) {
            auto error = GetLastError();
            settings = saved;
            return Result(false, startupOutcome.value("rollbackFailed", false)
                                    ? "自启动设置失败，原有自启动状态恢复失败"
                                    : "自启动设置失败，配置未改变", error);
        }
        if (!Persist()) {
            bool restored = !changedStartup ||
                SetStartup(saved.value("startup", false), settings.value("startup", false));
            settings = saved;
            return Result(false, restored ? "配置保存失败" : "配置保存失败，自启动状态恢复失败",
                          ERROR_WRITE_FAULT);
        }
        ScheduleHotkeys();
        result = Result(true, "设置已保存");
        if (changedStartup) startupOutcome = Result(true, "登录计划任务已更新");
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
    // Keep native menu routing while using the bundled Noto font at the cursor's DPI.
    auto previousDpi = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    HMENU menu = CreatePopupMenu();
    if (!menu) {
        if (previousDpi) SetThreadDpiAwarenessContext(previousDpi);
        return;
    }
    POINT point{};
    GetCursorPos(&point);
    tray_menu_typography::Prepare(root, point);
    AppendMenuW(menu, MF_OWNERDRAW, 1, tray_menu_typography::Data(1));
    AppendMenuW(menu, MF_OWNERDRAW, 2, tray_menu_typography::Data(2));
    AppendMenuW(menu, MF_OWNERDRAW, 3, tray_menu_typography::Data(3));
    SetForegroundWindow(window);
    UINT selected{};
    {
        tray_menu_typography::RoundedMenuScope roundedMenu;
        selected = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_LEFTALIGN | TPM_BOTTOMALIGN,
                                 point.x, point.y, 0, window, nullptr);
    }
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
        if (!IsAssistantUi(ui) || !PostMessageW(ui, ExitInterfaceMessage, 0, 0))
            PostMessageW(window, WM_CLOSE, 0, 0);
    }
}
LRESULT CALLBACK WindowProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    if (msg == ShowTrayMenuMessage) { TrayMenu(); return 0; }
    if (msg == WM_MEASUREITEM && tray_menu_typography::Measure(lp)) return TRUE;
    if (msg == WM_DRAWITEM && tray_menu_typography::Draw(lp)) return TRUE;
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
    if (msg == WM_HOTKEY)
        return 0;
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
    UINT32 startupPackageLength{};
    // The legacy manifest entry invokes Agent without arguments. Deliberate UI
    // launches and the new activator explicitly pass --background or --open.
    bool legacyAutomaticLaunch = argc == 1 &&
        GetCurrentPackageFullName(&startupPackageLength, nullptr) == ERROR_INSUFFICIENT_BUFFER;
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
        if (legacyAutomaticLaunch && !settings.value("startup", false)) {
            CloseHandle(singleton);
            CoUninitialize();
            return 0;
        }
        // Refresh the action after a package update and migrate the old enabled mechanism.
        // Disabled users are not silently opted into startup.
        if (settings.value("startup", false)) {
            auto ok = SetStartup(true, true);
            startupOutcome = Result(ok, ok ? "登录计划任务已更新" : "登录计划任务更新失败",
                                    ok ? 0 : GetLastError());
        }

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
