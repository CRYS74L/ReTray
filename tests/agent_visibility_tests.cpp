#include "../native/shell/bridge_client.h"
#include "../native/agent/settings.h"
#include "../native/agent/logon_startup.h"
#include <algorithm>
#include <cstdio>
#include <memory>
#include <stdexcept>
#include <unordered_set>
BOOL WINAPI AgentTestNotifyIcon(DWORD operation, PNOTIFYICONDATAW data);
BOOL WINAPI AgentTestTrackPopupMenu(HMENU, UINT, int, int, int, HWND, const RECT*);
BOOL WINAPI AgentTestCreateProcessW(LPCWSTR, LPWSTR, LPSECURITY_ATTRIBUTES, LPSECURITY_ATTRIBUTES,
                                  BOOL, DWORD, LPVOID, LPCWSTR, LPSTARTUPINFOW,
                                  LPPROCESS_INFORMATION);
int WINAPI AgentTestMessageBoxW(HWND, LPCWSTR, LPCWSTR, UINT);
DWORD AgentTestSetLogonStartupResult(const std::filesystem::path&, bool, const std::wstring&,
                                    const std::wstring&);
LSTATUS WINAPI AgentTestRegOpenKeyExW(HKEY, LPCWSTR, DWORD, REGSAM, PHKEY);
LSTATUS WINAPI AgentTestRegQueryValueExW(HKEY, LPCWSTR, LPDWORD, LPDWORD, LPBYTE, LPDWORD);
LSTATUS WINAPI AgentTestRegDeleteValueW(HKEY, LPCWSTR);
LSTATUS WINAPI AgentTestRegCloseKey(HKEY);

// Exercise real handlers, native menus, waits, and persistence. Replace only the external
// Explorer bridge and desktop popup/process launch boundaries, keeping the tests isolated.
namespace tray {
struct AgentTestBridgeClient {
    std::unique_ptr<BridgeState> state = std::make_unique<BridgeState>();
    BridgeState* shared{state.get()};
    std::vector<BridgeCommand> commands;
    DWORD nextError{};
    DWORD failPid{};
    DWORD refreshError{};
    bool snapshotComplete{true};
    std::unordered_set<std::string> absentNativeIcons;
    bool Alive() const { return shared && shared->status == 1; }
    bool Attach(const SymbolAdapter&, const std::filesystem::path&) { return Alive(); }
    void Reset() {}
    bool Command(BridgeCommand command, DWORD = 2000) {
        commands.push_back(command);
        InterlockedIncrement(&shared->requestSequence);
        if (command.operation == BridgeOperation::Refresh) {
            shared->lastError = std::exchange(refreshError, 0);
            if (shared->lastError) return false;
            uint32_t kept{};
            for (uint32_t i = 0; i < shared->count; ++i)
                if (!absentNativeIcons.contains(IdentityKey(shared->icons[i].identity)))
                    shared->icons[kept++] = shared->icons[i];
            shared->count = kept;
            shared->snapshotSequence += 2;
            shared->replySequence = shared->requestSequence;
            return true;
        }
        shared->lastError = (!failPid || failPid == command.identity.ownerPid)
                                ? std::exchange(nextError, 0) : 0;
        if (shared->lastError) return false;
        for (uint32_t i = 0; i < shared->count; ++i) {
            auto& row = shared->icons[i];
            if (!SameIcon(row.identity, command.identity)) continue;
            if (command.operation == BridgeOperation::Visibility) {
                row.placement = command.visibility == 1 ? 2 : 1;
                row.managed = command.visibility != -1 || row.custom;
            } else if (command.operation == BridgeOperation::Move && command.location != -1) {
                row.placement = command.location;
            } else if (command.operation == BridgeOperation::Apply) {
                row.custom = row.managed = true;
            } else if (command.operation == BridgeOperation::ClearImage) {
                row.custom = false;
            }
        }
        shared->snapshotSequence += 2;
        shared->replySequence = shared->requestSequence;
        return true;
    }
    std::vector<BridgeIcon> Snapshot(bool* complete = nullptr) const {
        if (complete) *complete = snapshotComplete;
        if (!snapshotComplete) return {};
        return {shared->icons, shared->icons + shared->count};
    }
};
}
#define BridgeClient AgentTestBridgeClient
#define Shell_NotifyIconW AgentTestNotifyIcon
#define TrackPopupMenu AgentTestTrackPopupMenu
#define CreateProcessW AgentTestCreateProcessW
#define MessageBoxW AgentTestMessageBoxW
#define SetLogonStartupResult AgentTestSetLogonStartupResult
#define RegOpenKeyExW AgentTestRegOpenKeyExW
#define RegQueryValueExW AgentTestRegQueryValueExW
#define RegDeleteValueW AgentTestRegDeleteValueW
#define RegCloseKey AgentTestRegCloseKey
#include "../native/agent/main.cpp"
#undef RegCloseKey
#undef RegDeleteValueW
#undef RegQueryValueExW
#undef RegOpenKeyExW
#undef SetLogonStartupResult
#undef MessageBoxW
#undef CreateProcessW
#undef TrackPopupMenu
#undef Shell_NotifyIconW
#undef BridgeClient

bool startupTaskEnabled{};
unsigned startupTaskWrites{};
LONG startupRunOpenError{ERROR_FILE_NOT_FOUND}, startupRunDeleteError{};
std::wstring startupRunCommand;
DWORD AgentTestSetLogonStartupResult(const std::filesystem::path&, bool enabled,
                                    const std::wstring&, const std::wstring&) {
    startupTaskEnabled = enabled; ++startupTaskWrites; return ERROR_SUCCESS;
}
LSTATUS WINAPI AgentTestRegOpenKeyExW(HKEY, LPCWSTR, DWORD, REGSAM, PHKEY key) {
    if (!startupRunOpenError) *key = reinterpret_cast<HKEY>(uintptr_t(1));
    return startupRunOpenError;
}
LSTATUS WINAPI AgentTestRegQueryValueExW(HKEY, LPCWSTR, LPDWORD, LPDWORD type, LPBYTE data,
                                      LPDWORD bytes) {
    auto size = static_cast<DWORD>((startupRunCommand.size() + 1) * sizeof(wchar_t));
    if (size > *bytes) { *bytes = size; return ERROR_MORE_DATA; }
    *type = REG_SZ; *bytes = size; memcpy(data, startupRunCommand.c_str(), size);
    return ERROR_SUCCESS;
}
LSTATUS WINAPI AgentTestRegDeleteValueW(HKEY, LPCWSTR) { return startupRunDeleteError; }
LSTATUS WINAPI AgentTestRegCloseKey(HKEY) { return ERROR_SUCCESS; }

#define CHECK(x) do { if (!(x)) throw std::runtime_error(#x); } while (0)
BridgeIcon removedOwnIcon;
UINT menuSelection{}, menuFlags{};
DPI_AWARENESS_CONTEXT menuDpi{};
std::vector<std::wstring> menuLabels;
unsigned restartLaunches{}, restartErrors{};
std::vector<std::wstring> restartArguments;
bool restartWaitHandlesValid{};
DWORD restartCreationFlags{};
BOOL restartInherits{};
BOOL WINAPI AgentTestTrackPopupMenu(HMENU menu, UINT flags, int, int, int, HWND, const RECT*) {
    menuFlags = flags; menuDpi = GetThreadDpiAwarenessContext(); menuLabels.clear();
    for (int index = 0; index < GetMenuItemCount(menu); ++index) {
        wchar_t label[128]{}; GetMenuStringW(menu, index, label, std::size(label), MF_BYPOSITION);
        if (!label[0]) {
            MENUITEMINFOW item{}; item.cbSize = sizeof(item); item.fMask = MIIM_DATA;
            GetMenuItemInfoW(menu, index, TRUE, &item);
            auto name = reinterpret_cast<const MSAAMENUINFO*>(item.dwItemData);
            if (name && name->dwMSAASignature == MSAA_MENU_SIG)
                wcsncpy_s(label, std::size(label), name->pszWText, _TRUNCATE);
        }
        menuLabels.emplace_back(label);
    }
    return menuSelection;
}
BOOL WINAPI AgentTestCreateProcessW(LPCWSTR, LPWSTR command, LPSECURITY_ATTRIBUTES, LPSECURITY_ATTRIBUTES,
                                  BOOL inherits, DWORD flags, LPVOID, LPCWSTR, LPSTARTUPINFOW,
                                  LPPROCESS_INFORMATION) {
    ++restartLaunches; restartInherits = inherits; restartCreationFlags = flags;
    restartArguments.clear(); restartWaitHandlesValid = false;
    int count{}; auto arguments = CommandLineToArgvW(command, &count);
    if (arguments) {
        for (int index = 0; index < count; ++index) restartArguments.emplace_back(arguments[index]);
        if (count == 8) {
            auto agent = reinterpret_cast<HANDLE>(wcstoull(arguments[5], nullptr, 10));
            auto ui = reinterpret_cast<HANDLE>(wcstoull(arguments[6], nullptr, 10));
            auto cancel = reinterpret_cast<HANDLE>(wcstoull(arguments[7], nullptr, 10));
            DWORD agentFlags{}, cancelFlags{};
            restartWaitHandlesValid = !ui && GetHandleInformation(agent, &agentFlags) &&
                (agentFlags & HANDLE_FLAG_INHERIT) && GetHandleInformation(cancel, &cancelFlags) &&
                (cancelFlags & HANDLE_FLAG_INHERIT) && WaitForSingleObject(agent, 0) == WAIT_TIMEOUT &&
                WaitForSingleObject(cancel, 0) == WAIT_TIMEOUT;
        }
        LocalFree(arguments);
    }
    SetLastError(ERROR_BAD_EXE_FORMAT); return FALSE;
}
int WINAPI AgentTestMessageBoxW(HWND, LPCWSTR, LPCWSTR, UINT) { ++restartErrors; return IDOK; }
BOOL WINAPI AgentTestNotifyIcon(DWORD operation, PNOTIFYICONDATAW data) {
    if (operation == NIM_DELETE) {
        for (uint32_t i = 0; i < bridge.shared->count; ++i)
            if (bridge.shared->icons[i].identity.ownerPid == GetCurrentProcessId() &&
                bridge.shared->icons[i].identity.ownerHwnd == reinterpret_cast<uintptr_t>(data->hWnd) &&
                bridge.shared->icons[i].identity.uid == data->uID) {
                removedOwnIcon = bridge.shared->icons[i];
                bridge.shared->icons[i] = bridge.shared->icons[--bridge.shared->count];
                bridge.shared->snapshotSequence += 2;
                break;
            }
    } else if (operation == NIM_ADD) {
        bridge.shared->icons[bridge.shared->count++] = removedOwnIcon;
        bridge.shared->snapshotSequence += 2;
    }
    return TRUE;
}
std::filesystem::path fixtureRoot;
BridgeIcon Target() { return bridge.shared->icons[0]; }
std::string TargetKey() { return IdentityKey(Target().identity); }
std::vector<BridgeCommand> IconCommands() {
    std::vector<BridgeCommand> result;
    for (const auto& command : bridge.commands)
        if (command.identity.ownerPid) result.push_back(command);
    return result;
}
Json Call(const std::string& method, Json data) {
    return Handle({{"schema", 1}, {"requestId", "unit"}, {"method", method}, {"data", data}});
}
void ResetFixture() {
    bridge.state = std::make_unique<BridgeState>(); bridge.shared = bridge.state.get();
    bridge.shared->status = 1; bridge.shared->epoch = 1; bridge.shared->count = 2;
    bridge.commands.clear(); bridge.nextError = bridge.failPid = bridge.refreshError = 0;
    bridge.absentNativeIcons.clear(); bridge.snapshotComplete = true;
    for (uint32_t i = 0; i < 2; ++i) {
        auto& row = bridge.shared->icons[i];
        row.identity.shellEpoch = 1; row.identity.ownerHwnd = 100 + i;
        row.identity.ownerPid = 1000 + i; row.identity.ownerCreatedAt = 10 + i;
        row.identity.uid = 11; row.placement = 1; row.live = true;
        row.registration = i + 1;
        wcscpy_s(row.executable, i ? L"C:\\Fixture\\Peer.exe" : L"C:\\Fixture\\Target.exe");
    }
    lifecycle.state = SessionState::Ready; stopping = false; window = nullptr;
    ownOpenQueued = false; ownIdentityKey.clear(); ownIdentityEpoch = 0;
    if (ownImage) { DestroyIcon(ownImage); ownImage = nullptr; }
    settings = DefaultSettings(); configRevision = 0; settingsPath = fixtureRoot / L"settings.json";
    dataRoot = fixtureRoot; rows.clear(); presentationFrames.clear(); catalog = {}; applied.clear(); failedStamp.clear();
    retryAfter.clear(); outcomes.clear(); seenSequence = -1;
    lastCatalogRefresh = 0;
    registrations.clear();
    roleBindings.clear(); roleObservations.clear(); ruleContext.clear();
    identityRetryAt = 0; identityOutcome = Json::object();
    environmentStamp.clear(); environmentEpoch = lastEnvironmentCheck = 0;
    activityHistory = Json::object(); activityDirty = false; ownAdded = false;
    startupTaskEnabled = false; startupTaskWrites = 0;
    startupRunOpenError = ERROR_FILE_NOT_FOUND; startupRunDeleteError = 0; startupRunCommand.clear();
    wchar_t systemPath[MAX_PATH]{}; GetSystemDirectoryW(systemPath, MAX_PATH);
    IconRule target, peer;
    target.selector = SelectorFor(Record(bridge.shared->icons[0])); target.visibility = 0;
    target.asset = (std::filesystem::path(systemPath) / L"shell32.dll").wstring();
    target.tip = L"Preserved custom tip"; target.baselineFingerprint = 55;
    target.baselineWidth = target.baselineHeight = 32; target.hotkey = (MOD_ALT << 16) | 'A';
    peer.selector = SelectorFor(Record(bridge.shared->icons[1])); peer.visibility = 0;
    settings["rules"] = {RuleJson(target), RuleJson(peer)};
    CHECK(SaveSettings(settingsPath, settings));
    LoadRules(); Refresh(); bridge.commands.clear();
}
void ImmediateVisibility() {
    ResetFixture(); auto original = settings["rules"][0];
    auto response = Call("icon.visibility", {{"key", TargetKey()}, {"visibility", 1}});
    CHECK(response.value("success", false)); CHECK(response.value("saved", false));
    CHECK(Target().placement == 2); CHECK(Target().custom);
    CHECK(response["data"]["visible"] == false); CHECK(response["data"]["placement"] == 2);
    CHECK(response["data"]["visibility"] == 1);
    auto commands = IconCommands(); CHECK(commands.size() == 1);
    CHECK(commands[0].operation == BridgeOperation::Visibility);
    CHECK(commands[0].identity.ownerPid == 1000);
    original["visibility"] = 1; CHECK(settings["rules"][0] == original);
    bool recovered{}; CHECK(LoadSettings(settingsPath, recovered)["rules"][0] == original);
    Refresh(); CHECK(IconCommands().size() == 1);
    for (int value : {0,1,0}) {
        bridge.commands.clear(); response = Call("icon.visibility", {{"key", TargetKey()}, {"visibility", value}});
        CHECK(response.value("success", false)); Refresh(); CHECK(IconCommands().size() == 1);
        CHECK(Target().placement == (value == 1 ? 2u : 1u)); CHECK(Target().custom);
    }
}
void UnrelatedSavesKeepRulesApplied() {
    ResetFixture(); CHECK(Call("settings.apply", {{"uiOpacity", 244}}).value("success", false));
    Refresh(); CHECK(IconCommands().empty());
    bridge.shared->systemCount = 1;
    bridge.shared->systemControls[0] = {};
    bridge.shared->systemControls[0].id = 0;
    bridge.shared->systemControls[0].instances = 1;
    bridge.shared->systemControls[0].visible = true;
    CHECK(Call("system.set", {{"id", 0}, {"visible", false}}).value("success", false));
    Refresh(); CHECK(IconCommands().empty());
    auto own = settings["ownIcon"]; own["visible"] = false;
    CHECK(Call("ownIcon.apply", own).value("success", false)); Refresh(); CHECK(IconCommands().empty());
}
void ExplicitRuleReapplyRetriesOnlySelectedIcon() {
    ResetFixture(); auto target = settings["rules"][0];
    bridge.nextError = ERROR_FILE_NOT_FOUND;
    auto first = Call("rule.apply", {{"rule", target}});
    CHECK(!first.value("success", true));
    auto second = Call("rule.apply", {{"rule", target}});
    CHECK(second.value("success", false)); CHECK(Target().custom);
    auto commands = IconCommands(); CHECK(commands.size() == 8);
    for (const auto& command : commands) CHECK(command.identity.ownerPid == 1000);
}
void SavedRepliesHaveCurrentRevision() {
    ResetFixture(); auto result = Call("icons.reorder", {{"keys", Json::array()}});
    CHECK(result.value("success", false)); CHECK(result["revision"] == configRevision);
}
void LegacyPlacementIgnored() {
    ResetFixture(); auto target = settings["rules"][0]; target["location"] = 0;
    CHECK(Call("rule.apply", {{"rule", target}}).value("success", false));
    CHECK(Target().placement == 1);
    for (const auto& command : IconCommands()) CHECK(command.operation != BridgeOperation::Move);
    CHECK(Call("settings.apply", {{"newIconLocation", 0}}).value("success", false));
    auto& added = bridge.shared->icons[bridge.shared->count++]; added = bridge.shared->icons[1];
    added.identity.ownerPid = 2222; added.identity.ownerHwnd = 3333;
    wcscpy_s(added.executable, L"C:\\Fixture\\New.exe"); added.placement = 1;
    bridge.shared->snapshotSequence += 2; Refresh(); CHECK(added.placement == 1);
    for (const auto& command : IconCommands()) CHECK(command.operation != BridgeOperation::Move);
    target["location"] = "legacy-unused"; CHECK(ParseRule(target).location == -1);
    auto legacy = settings; legacy["newIconLocation"] = "legacy-unused";
    CHECK(ValidateSettings(legacy)); CHECK(SaveSettings(settingsPath, legacy));
    bool recovered{}; CHECK(!LoadSettings(settingsPath, recovered).contains("newIconLocation"));
}
void InvalidVisibilityCannotPersist() {
    ResetFixture(); auto before = settings;
    for (auto value : {Json(2), Json("hidden"), Json(uint64_t(1) << 32), Json(UINT64_MAX), Json(0.0)}) {
        bool rejected{};
        try { rejected = !Call("icon.visibility", {{"key", TargetKey()}, {"visibility", value}}).value("success", false); }
        catch (...) { rejected = true; }
        CHECK(rejected); CHECK(settings == before); CHECK(IconCommands().empty());
    }
    CHECK(!Call("icon.visibility", {{"key", "stale"}, {"visibility", 1}}).value("success", false));
    CHECK(settings == before); CHECK(IconCommands().empty());
}
void NativeExitRaceRollsBackRule() {
    ResetFixture(); auto before = settings; bridge.nextError = ERROR_NOT_FOUND;
    auto result = Call("icon.visibility", {{"key", TargetKey()}, {"visibility", 1}});
    CHECK(!result.value("success", true)); CHECK(!result.value("saved", true));
    CHECK(settings["rules"] == before["rules"]); CHECK(Target().custom); CHECK(Target().placement == 1);
    bool recovered{}; CHECK(LoadSettings(settingsPath, recovered)["rules"] == before["rules"]);
    CHECK(LoadSettings(settingsPath.wstring() + L".bak", recovered)["rules"] == before["rules"]);
    bridge.commands.clear(); Refresh(); CHECK(IconCommands().empty());
}
void SaveFailureCannotChangeNativeIcon() {
    ResetFixture(); auto before = settings;
    settingsPath = fixtureRoot; // A directory cannot replace the settings file.
    auto result = Call("icon.visibility", {{"key", TargetKey()}, {"visibility", 1}});
    CHECK(!result.value("success", true)); CHECK(settings == before);
    CHECK(Target().placement == 1); CHECK(Target().custom); CHECK(IconCommands().empty());
}
void FullRuleValidationStillRuns() {
    ResetFixture(); settings["startup"] = "invalid"; auto before = settings;
    auto result = Call("icon.visibility", {{"key", TargetKey()}, {"visibility", 1}});
    CHECK(!result.value("success", true)); CHECK(settings == before); CHECK(IconCommands().empty());
}
void SharedSelectorFanoutRollsBackOnExit() {
    ResetFixture(); auto& sibling = bridge.shared->icons[bridge.shared->count++]; sibling = Target();
    sibling.identity.ownerHwnd = 4444; sibling.identity.ownerPid = 2222;
    bridge.shared->snapshotSequence += 2; Refresh(); bridge.commands.clear();
    auto before = settings; bridge.failPid = 2222; bridge.nextError = ERROR_NOT_FOUND;
    auto result = Call("icon.visibility", {{"key", TargetKey()}, {"visibility", 1}});
    CHECK(!result.value("success", true)); CHECK(!result.value("saved", true));
    CHECK(Target().placement == 1); CHECK(sibling.placement == 1);
    CHECK(Target().custom); CHECK(sibling.custom); CHECK(settings["rules"] == before["rules"]);
    auto commands = IconCommands(); CHECK(commands.size() == 3);
    for (const auto& command : commands) CHECK(command.operation == BridgeOperation::Visibility);
}
void OwnVisibilityDoesNotCreateRule() {
    ResetFixture(); auto& own = bridge.shared->icons[bridge.shared->count++]; own = bridge.shared->icons[0];
    window = reinterpret_cast<HWND>(9999);
    own.identity.ownerPid = GetCurrentProcessId(); own.identity.ownerHwnd = 9999; own.identity.uid = 1;
    own.identity.ownerCreatedAt = Generation(GetCurrentProcessId());
    rows.push_back(own); auto before = settings["rules"];
    auto response = Call("icon.visibility", {{"key", IdentityKey(own.identity)}, {"visibility", 1}});
    CHECK(response.value("success", false)); CHECK(settings["ownIcon"]["visible"] == false);
    CHECK(settings["rules"] == before); CHECK(IconCommands().empty());
}
void OwnHiddenIconCanImmediatelyShowAgain() {
    ResetFixture(); auto& own = bridge.shared->icons[bridge.shared->count++]; own = Target();
    window = reinterpret_cast<HWND>(9999);
    own.identity.ownerPid = GetCurrentProcessId(); own.identity.ownerHwnd = 9999; own.identity.uid = 1;
    own.identity.ownerCreatedAt = Generation(GetCurrentProcessId());
    ownAdded = true; ownImage = CopyIcon(LoadIconW(nullptr, IDI_APPLICATION));
    auto ownKey = IdentityKey(own.identity); auto before = settings["rules"];
    CHECK(Call("icon.visibility", {{"key", ownKey}, {"visibility", 1}}).value("success", false));
    CHECK(bridge.shared->count == 2);
    auto shown = Call("icon.visibility", {{"key", ownKey}, {"visibility", 0}});
    CHECK(shown.value("success", false)); CHECK(shown["data"]["visible"] == true);
    CHECK(bridge.shared->count == 3); CHECK(settings["rules"] == before); CHECK(ownImage);
    bridge.shared->epoch = 2;
    CHECK(!Call("icon.visibility", {{"key", ownKey}, {"visibility", 1}}).value("success", true));
    CHECK(settings["ownIcon"]["visible"] == true);
    DestroyIcon(ownImage); ownImage = nullptr; ownAdded = false;
}
void OwnSingleClickQueuesOneOpen() {
    ResetFixture(); requests.clear();
    WindowProc(nullptr, OwnCallback, 0, MAKELONG(WM_LBUTTONDOWN, 1));
    CHECK(requests.empty());
    WindowProc(nullptr, OwnCallback, 0, MAKELONG(WM_LBUTTONUP, 1));
    CHECK(requests.size() == 1); CHECK(requests.front()->input["method"] == "internal.openUi");
    WindowProc(nullptr, OwnCallback, 0, MAKELONG(NIN_SELECT, 1));
    WindowProc(nullptr, OwnCallback, 0, MAKELONG(WM_LBUTTONDBLCLK, 1));
    WindowProc(nullptr, OwnCallback, 0, MAKELONG(WM_LBUTTONUP, 1));
    CHECK(requests.size() == 1);
    auto pending = requests.front(); requests.clear();
    CHECK(Handle(pending->input).value("success", false));
    WindowProc(nullptr, OwnCallback, 0, MAKELONG(NIN_KEYSELECT, 1));
    CHECK(requests.size() == 1);
    pending = requests.front(); requests.clear(); CHECK(Handle(pending->input).value("success", false));
}
void OwnMenuUsesNativeDpiAndUpperRightAnchor() {
    ResetFixture(); menuSelection = 0;
    auto previous = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_UNAWARE);
    TrayMenu();
    auto restored = GetThreadDpiAwarenessContext();
    SetThreadDpiAwarenessContext(previous);
    CHECK(menuLabels == std::vector<std::wstring>({L"打开助手", L"重新启动", L"退出"}));
    CHECK((menuFlags & TPM_BOTTOMALIGN) != 0);
    CHECK((menuFlags & (TPM_RIGHTALIGN | TPM_CENTERALIGN)) == 0);
    CHECK(AreDpiAwarenessContextsEqual(menuDpi, DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2));
    CHECK(AreDpiAwarenessContextsEqual(restored, DPI_AWARENESS_CONTEXT_UNAWARE));
}
void OwnRestartLaunchFailureKeepsCurrentAgentAndSettings() {
    ResetFixture(); root = fixtureRoot; auto before = settings;
    dataRoot = std::filesystem::path(fixtureRoot.wstring() + L"\\用户设置 含 空格\\");
    restartLaunches = restartErrors = 0; menuSelection = 2;
    TrayMenu();
    CHECK(restartLaunches == 1); CHECK(restartErrors == 1);
    CHECK(restartArguments.size() == 8);
    CHECK(restartArguments[0] == (fixtureRoot / L"TrayAssistant.Agent.exe").wstring());
    CHECK(restartArguments[1] == L"--open"); CHECK(restartArguments[2] == L"--data");
    CHECK(restartArguments[3] == dataRoot.wstring()); CHECK(restartArguments[4] == L"--restart-wait");
    CHECK(restartInherits); CHECK(restartWaitHandlesValid);
    CHECK((restartCreationFlags & (EXTENDED_STARTUPINFO_PRESENT | CREATE_NO_WINDOW)) ==
          (EXTENDED_STARTUPINFO_PRESENT | CREATE_NO_WINDOW));
    CHECK(!stopping); CHECK(settings == before);
    bool recovered{}; CHECK(LoadSettings(settingsPath, recovered) == before);
    CHECK(IconCommands().empty());
}
void RestartWaitRequiresBothExitsAndHonorsCancellation() {
    HANDLE signals[] = {CreateEventW(nullptr, TRUE, FALSE, nullptr),
                        CreateEventW(nullptr, TRUE, FALSE, nullptr),
                        CreateEventW(nullptr, TRUE, FALSE, nullptr)};
    CHECK(signals[0] && signals[1] && signals[2]);
    CHECK(!WaitForRestartExit(signals[0], signals[1], signals[2], 1));
    SetEvent(signals[0]); CHECK(!WaitForRestartExit(signals[0], signals[1], signals[2], 1));
    ResetEvent(signals[0]); SetEvent(signals[1]);
    CHECK(!WaitForRestartExit(signals[0], signals[1], signals[2], 1));
    SetEvent(signals[0]); CHECK(WaitForRestartExit(signals[0], signals[1], signals[2], 1));
    SetEvent(signals[2]); CHECK(!WaitForRestartExit(signals[0], signals[1], signals[2], 1));
    for (auto handle : signals) CloseHandle(handle);
}
void BlinkPresentationKeepsVisibleFrameForAnyApplication() {
    ResetFixture();
    for (int index = 0; index < 2; ++index) {
        auto row = bridge.shared->icons[index];
        row.width = row.height = 2;
        std::fill_n(row.pixels, 4, index ? 0xFF112233u : 0xFF445566u);
        RememberPresentationFrame(row);
        auto key = IdentityKey(row.identity);
        auto expected = presentationFrames.at(key).pixels;
        row.width = row.height = 0;
        RememberPresentationFrame(row);
        CHECK(presentationFrames.at(key).pixels == expected);
        row.width = row.height = 2;
        std::fill_n(row.pixels, 4, 0u);
        RememberPresentationFrame(row);
        CHECK(presentationFrames.at(key).pixels == expected);
        std::fill_n(row.pixels, 4, 0xFF778899u);
        RememberPresentationFrame(row);
        CHECK(presentationFrames.at(key).pixels[0] == 0xFF778899u);
    }
}
void BlinkPresentationUsesOriginalWhenFirstSeenBlank() {
    ResetFixture();
    presentationFrames.clear();
    auto row = bridge.shared->icons[0];
    row.width = row.height = 0;
    row.originalWidth = row.originalHeight = 2;
    std::fill_n(row.originalPixels, 4, 0xFFABCDEFu);
    RememberPresentationFrame(row);
    auto& preview = presentationFrames.at(IdentityKey(row.identity));
    CHECK(preview.width == 2 && preview.height == 2);
    CHECK(preview.pixels[0] == 0xFFABCDEFu);
    rows = {row};
    auto result = CatalogJson();
    CHECK(result["icons"][0]["width"] == 0);
    CHECK(result["icons"][0]["pixels"] == "");
    CHECK(result["icons"][0]["displayWidth"] == 2);
    CHECK(!result["icons"][0]["displayPixels"].get<std::string>().empty());
}
void BlinkPresentationHasCustomImageBeforeFirstVisibleSnapshot() {
    ResetFixture();
    auto row = bridge.shared->icons[0];
    auto key = IdentityKey(row.identity);
    auto expected = presentationFrames.at(key).pixels;
    CHECK(!expected.empty());
    row.width = row.height = 0;
    row.decision = static_cast<uint32_t>(FrameDecision::NativeOff);
    RememberPresentationFrame(row);
    CHECK(presentationFrames.at(key).pixels == expected);
}
void RemovedImportCannotChangeSettings() {
    ResetFixture(); auto before = settings;
    auto response = Call("settings.import", {{"settings", DefaultSettings()}});
    CHECK(!response.value("success", true));
    CHECK(response["errorCode"] == ERROR_NOT_SUPPORTED);
    CHECK(!response.value("saved", true));
    CHECK(settings == before);
}
void SilentExitIsRemovedWithoutAnotherNotification() {
    ResetFixture(); auto key = TargetKey(); auto before = settings;
    CHECK(catalog.items.contains(key)); CHECK(presentationFrames.contains(key));
    CHECK(outcomes.contains(key));
    bridge.absentNativeIcons.insert(key); auto unchanged = bridge.shared->snapshotSequence;
    Sleep(2100); CHECK(bridge.shared->snapshotSequence == unchanged); Refresh();
    CHECK(rows.size() == 1); CHECK(catalog.items.size() == 1);
    CHECK(!catalog.items.contains(key)); CHECK(!presentationFrames.contains(key));
    CHECK(!applied.contains(key)); CHECK(!outcomes.contains(key));
    CHECK(settings == before);
}
void ReopenDoesNotAccumulateAndKeepsLiveInstances() {
    ResetFixture(); auto original = Target(); auto before = settings;
    for (uint32_t generation = 0; generation < 4; ++generation) {
        bridge.absentNativeIcons.insert(TargetKey());
        auto& replacement = bridge.shared->icons[bridge.shared->count++]; replacement = original;
        replacement.identity.ownerPid = 3000 + generation;
        replacement.identity.ownerHwnd = 6000 + generation;
        replacement.identity.ownerCreatedAt = 30 + generation;
        bridge.shared->snapshotSequence += 2;
        Sleep(2100); Refresh();
        CHECK(rows.size() == 2); CHECK(catalog.items.size() == 2);
        // Keep slot zero as the current registration for the next cycle.
        std::swap(bridge.shared->icons[0], bridge.shared->icons[1]);
        bridge.shared->snapshotSequence += 2;
    }
    auto& simultaneous = bridge.shared->icons[bridge.shared->count++]; simultaneous = original;
    simultaneous.identity.ownerPid = 4444; simultaneous.identity.ownerHwnd = 8888;
    simultaneous.identity.ownerCreatedAt = 44;
    bridge.shared->snapshotSequence += 2; Refresh();
    CHECK(rows.size() == 3); CHECK(catalog.items.size() == 3);
    CHECK(CatalogJson()["icons"].size() == 3); CHECK(settings == before);
}
void StaleVisibilityIsRejectedBeforeSaving() {
    ResetFixture(); auto key = TargetKey(); auto before = settings;
    bridge.absentNativeIcons.insert(key);
    auto result = Call("icon.visibility", {{"key", key}, {"visibility", 1}});
    CHECK(!result.value("success", true)); CHECK(result["errorCode"] == ERROR_NOT_FOUND);
    CHECK(result["detail"] == "图标已退出"); CHECK(settings == before);
    CHECK(IconCommands().empty());
    bool recovered{}; CHECK(LoadSettings(settingsPath, recovered) == before);
}
void StaleSiblingCannotBlockLiveVisibility() {
    ResetFixture(); auto key = TargetKey();
    auto& sibling = bridge.shared->icons[bridge.shared->count++]; sibling = Target();
    sibling.identity.ownerHwnd = 4444; sibling.identity.ownerPid = 2222;
    sibling.identity.ownerCreatedAt = 22;
    bridge.absentNativeIcons.insert(IdentityKey(sibling.identity));
    bridge.failPid = 2222; bridge.nextError = ERROR_NOT_FOUND;
    auto result = Call("icon.visibility", {{"key", key}, {"visibility", 1}});
    CHECK(result.value("success", false)); CHECK(Target().placement == 2);
    CHECK(IconCommands().size() == 1); CHECK(bridge.shared->count == 2);
}
void FailedNativeReadKeepsCatalogAndCannotSaveVisibility() {
    ResetFixture(); auto before = settings; auto count = rows.size();
    auto key = TargetKey(); bridge.refreshError = ERROR_RETRY;
    Sleep(2100); Refresh(); CHECK(rows.size() == count); CHECK(catalog.items.size() == count);
    bridge.refreshError = ERROR_RETRY;
    auto result = Call("icon.visibility", {{"key", key}, {"visibility", 1}});
    CHECK(!result.value("success", true)); CHECK(settings == before);
    CHECK(IconCommands().empty()); CHECK(rows.size() == count);
}
void IncompleteSharedReadCannotBecomeAnEmptyCatalog() {
    ResetFixture(); auto before = settings; auto count = rows.size(); auto key = TargetKey();
    auto registration = registrations.at(key); ++bridge.shared->icons[0].registration;
    bridge.snapshotComplete = false; seenSequence = -1; Refresh();
    CHECK(rows.size() == count); CHECK(catalog.items.size() == count);
    CHECK(registrations.at(key) == registration);
    auto result = Call("icon.visibility", {{"key", key}, {"visibility", 1}});
    CHECK(!result.value("success", true)); CHECK(result["errorCode"] == ERROR_RETRY);
    CHECK(settings == before); CHECK(IconCommands().empty());
}
void DuplicateSnapshotCannotReplaceAcceptedCatalog() {
    ResetFixture(); auto peerKey = IdentityKey(bridge.shared->icons[1].identity);
    auto key = TargetKey(); auto registration = registrations.at(key);
    ++bridge.shared->icons[0].registration;
    bridge.shared->icons[1] = Target(); bridge.shared->snapshotSequence += 2; Refresh();
    CHECK(rows.size() == 2); CHECK(IdentityKey(rows[1].identity) == peerKey);
    CHECK(catalog.items.contains(peerKey));
    CHECK(registrations.at(key) == registration);
}
void SameIdentityRecreationRestoresSavedPolicy() {
    ResetFixture(); auto key = TargetKey();
    CHECK(Call("icon.visibility", {{"key", key}, {"visibility", 1}}).value("success", false));
    Refresh(); bridge.commands.clear();
    // DELETE + ADD occurred between polls, so only the fresh unmanaged registration is seen.
    auto& renewed = bridge.shared->icons[0];
    ++renewed.registration;
    renewed.custom = renewed.managed = false; renewed.placement = 1;
    bridge.shared->snapshotSequence += 2; Refresh();
    CHECK(Target().custom); CHECK(Target().placement == 2);
    CHECK(IconCommands().size() == 4);
}
void SameIdentityRecreationCannotReuseOldBlinkPreview() {
    ResetFixture(); rules.clear(); applied.clear();
    auto& row = bridge.shared->icons[0]; row.managed = row.custom = false;
    row.width = row.height = 2; row.pixels[0] = 0xff1955e9;
    bridge.shared->snapshotSequence += 2; Refresh();
    auto key = TargetKey(); CHECK(presentationFrames.at(key).pixels[0] == 0xff1955e9);
    ++row.registration; row.width = row.height = 0;
    row.originalWidth = row.originalHeight = 0;
    bridge.shared->snapshotSequence += 2; Refresh();
    CHECK(presentationFrames.at(key).pixels.empty());
    CHECK(CatalogJson()["icons"][0]["displayPixels"] == "");
}
void ImmediateVisibilityAfterGuidRecreationRestoresOtherPolicy() {
    ResetFixture(); auto& row = bridge.shared->icons[0];
    row.identity.hasGuid = true; row.identity.guid.Data1 = 0x12345678;
    auto rule = ParseRule(settings["rules"][0]); rule.selector = SelectorFor(Record(row));
    settings["rules"][0] = RuleJson(rule); LoadRules();
    bridge.shared->snapshotSequence += 2; Refresh(); auto key = TargetKey();
    CHECK(Target().custom); ++row.registration;
    row.managed = row.custom = false; row.placement = 1;
    bridge.shared->snapshotSequence += 2;
    CHECK(Call("icon.visibility", {{"key", key}, {"visibility", 1}}).value("success", false));
    Refresh(); CHECK(Target().custom); CHECK(Target().placement == 2);
    CHECK(registrations.at(key) == Target().registration);
}
void NativeHiddenImageSlotsDoNotBecomeCatalogRows() {
    ResetFixture();
    for (uint32_t slot = 0; slot < 12; ++slot) {
        auto& bank = bridge.shared->icons[bridge.shared->count++]; bank = Target();
        bank.identity.uid = UINT32_MAX - slot;
        bank.sourceHidden = true; bank.placement = 2;
        bank.managed = bank.custom = false;
    }
    bridge.shared->snapshotSequence += 2; Refresh();
    CHECK(bridge.shared->count == 14); // Preserve the application's native image bank.
    CHECK(CatalogJson()["icons"].size() == 2);
}
void AssistantHiddenActiveIconRemainsControllable() {
    ResetFixture(); auto key = TargetKey();
    CHECK(Call("icon.visibility", {{"key", key}, {"visibility", 1}}).value("success", false));
    Refresh(); CHECK(Target().placement == 2); CHECK(!Target().sourceHidden);
    auto icons = CatalogJson()["icons"]; CHECK(icons.size() == 2);
    CHECK(icons[0]["key"] == key);
    CHECK(Call("icon.visibility", {{"key", key}, {"visibility", 0}}).value("success", false));
}
void SourceHiddenSlotBecomesListedWhenApplicationShowsIt() {
    ResetFixture(); auto& bank = bridge.shared->icons[bridge.shared->count++]; bank = Target();
    bank.identity.uid = UINT32_MAX; bank.sourceHidden = true;
    bank.managed = bank.custom = false; bank.placement = 2;
    bridge.shared->snapshotSequence += 2; Refresh();
    CHECK(CatalogJson()["icons"].size() == 2);
    bank.sourceHidden = false; bank.placement = 1;
    bridge.shared->snapshotSequence += 2; Refresh();
    CHECK(CatalogJson()["icons"].size() == 3);
}
void GenericUidChangeRestoresHiddenPolicy() {
    ResetFixture();
    auto peer = settings["rules"][1];
    auto& row = bridge.shared->icons[0];
    wcscpy_s(row.executable, L"C:\\Fixture\\ChangingUid.exe");
    wcscpy_s(row.windowClass, L"SharedTrayHost");
    wcscpy_s(row.originalTip, L"Primary role"); row.callback = WM_APP + 8;
    row.identity.uid = 42036080; row.custom = row.managed = false; row.placement = 1;
    IconRule old; old.selector = SelectorFor(Record(row), RuleRecords(bridge.Snapshot()));
    old.visibility = 1;
    auto initialKey = PersistentKey(old.selector);
    settings["rules"] = {RuleJson(old), peer}; settings["iconOrder"] = {initialKey, "@assistant"};
    CHECK(SaveSettings(settingsPath, settings));
    bool recovered{}; settings = LoadSettings(settingsPath, recovered); CHECK(!recovered);
    CHECK(settings["rules"].size() == 2); LoadRules();
    bridge.shared->snapshotSequence += 2; Refresh(); CHECK(row.placement == 2);
    auto key = IdentityKey(row.identity);
    CHECK(Call("icon.visibility", {{"key", key}, {"visibility", 0}}).value("success", false));
    CHECK(row.placement == 1); CHECK(settings["rules"].size() == 2);
    CHECK(Call("icon.visibility", {{"key", key}, {"visibility", 1}}).value("success", false));
    row.identity.uid = 23456789; row.identity.ownerPid += 50;
    row.identity.ownerCreatedAt += 100; row.identity.ownerHwnd += 50;
    row.registration += 10; row.placement = 1; row.managed = false;
    bridge.shared->snapshotSequence += 2; Refresh(); CHECK(row.placement == 2);
    auto catalog = CatalogJson()["icons"];
    auto current = std::find_if(catalog.begin(), catalog.end(), [&](const auto& icon) { return icon["key"] == IdentityKey(row.identity); });
    CHECK(current != catalog.end()); CHECK((*current)["identityReassociated"] == true);
    CHECK((*current)["selector"]["uid"] == 42036080); CHECK((*current)["nativeUid"] == 23456789);
    CHECK(settings["iconOrder"][0] == initialKey); CHECK(settings["rules"].size() == 2);
    CHECK(bridge.shared->icons[1].placement == 1);
    CHECK(LoadSettings(settingsPath, recovered)["rules"] == settings["rules"]);
}
void IdentityLearningDoesNotReplayVisualPolicy() {
    ResetFixture(); auto key = TargetKey(); auto before = configRevision;
    auto& row = bridge.shared->icons[0];
    wcscpy_s(row.originalTip, L"Native dynamic tooltip");
    row.originalWidth = row.originalHeight = 1; row.originalPixels[0] = 0xff778899;
    bridge.shared->snapshotSequence += 2; Refresh();
    CHECK(configRevision > before); CHECK(IconCommands().empty());
    auto captured = ParseRule(settings["rules"][0]); CHECK(captured.selector.legacy.dynamicTip);
    CHECK(!captured.selector.legacy.images.empty());
    CHECK(applied.contains(key));
    CHECK(Call("icon.visibility", {{"key", key}, {"visibility", 1}}).value("success", false));
    CHECK(IconCommands().size() == 1); CHECK(Target().placement == 2);
}
void OwnOriginalPreviewDoesNotFollowReplacementPixels() {
    ResetFixture(); auto& own = bridge.shared->icons[bridge.shared->count++]; own = Target();
    own.identity.ownerPid = GetCurrentProcessId(); own.identity.uid = 1;
    own.originalWidth = own.originalHeight = 1; own.originalPixels[0] = 0xff123456;
    bridge.shared->snapshotSequence += 2; Refresh();
    auto first = CatalogJson()["icons"];
    auto findOwn = [](const Json& list) {
        for (const auto& item : list) if (item.value("owned", false)) return item;
        throw std::runtime_error("Owned catalog row missing");
    };
    auto original = findOwn(first); CHECK(original["originalWidth"] > 0);
    own.originalPixels[0] = 0xffabcdef;
    bridge.shared->snapshotSequence += 2; Refresh();
    auto replacement = findOwn(CatalogJson()["icons"]);
    CHECK(original["originalPixels"] == replacement["originalPixels"]);
    CHECK(original["originalWidth"] == replacement["originalWidth"]);
}
void ProvisionalAssociationCannotTrainIdentityAndDelayedUidRestores() {
    ResetFixture(); auto& row = bridge.shared->icons[0];
    row.custom = row.managed = false; row.placement = 1; row.identity.uid = 99;
    IconRule old; old.selector.executable = row.executable; old.selector.uid = 7; old.visibility = 1;
    settings["rules"] = {RuleJson(old), settings["rules"][1]};
    CHECK(SaveSettings(settingsPath, settings)); auto initial = settings["rules"][0];
    LoadRules(); bridge.shared->snapshotSequence += 2; Refresh();
    CHECK(row.placement == 2); CHECK(settings["rules"][0] == initial);
    auto& delayed = bridge.shared->icons[bridge.shared->count++]; delayed = row;
    delayed.identity.uid = 7; delayed.identity.ownerHwnd += 50; delayed.registration += 50;
    delayed.managed = false; delayed.placement = 1;
    bridge.shared->snapshotSequence += 2; Refresh();
    CHECK(row.placement == 1); CHECK(delayed.placement == 2);
    CHECK(ParseRule(settings["rules"][0]).selector.uid == 7);
    CHECK(ParseRule(settings["rules"][0]).selector.legacy.observedUid == 7);
}
void ConfirmedRowKeepsItsOwnRuleWhenOldUidArrives() {
    for (bool editor : {false, true}) {
        ResetFixture(); auto& row = bridge.shared->icons[0];
        row.custom = row.managed = false; row.placement = 1; row.identity.uid = 99;
        IconRule old; old.selector.executable = row.executable; old.selector.uid = 7; old.visibility = 0;
        settings["rules"] = {RuleJson(old), settings["rules"][1]};
        CHECK(SaveSettings(settingsPath, settings)); LoadRules();
        bridge.shared->snapshotSequence += 2; Refresh();
        CHECK(roleBindings.at(TargetKey()).provisional);
        Json reply;
        if (editor) {
            auto edited = settings["rules"][0]; edited["visibility"] = 1; edited["tip"] = "Explicit edited tip";
            reply = Call("rule.apply", {{"key", TargetKey()}, {"rule", edited}, {"previousSelector", edited["selector"]}});
        } else reply = Call("icon.visibility", {{"key", TargetKey()}, {"visibility", 1}});
        CHECK(reply.value("success", false)); Refresh(); CHECK(row.placement == 2);
        CHECK(settings["rules"].size() == 3);
        auto found = ResolveCurrent(rules, Record(row), RuleRecords(bridge.Snapshot()));
        CHECK(found.rule && !found.provisional && found.rule->selector.uid == 99);
        wcscpy_s(row.originalTip, L"New dynamic tooltip");
        row.originalWidth = row.originalHeight = 1; row.originalPixels[0] = 0xff991122;
        auto& delayed = bridge.shared->icons[bridge.shared->count++]; delayed = row;
        delayed.identity.uid = 7; delayed.registration += 50; delayed.managed = false;
        delayed.placement = 1;
        bridge.shared->snapshotSequence += 2; Refresh();
        CHECK(row.placement == 2); CHECK(delayed.placement == 1);
        found = ResolveCurrent(rules, Record(row), RuleRecords(bridge.Snapshot()));
        CHECK(found.rule && !found.provisional && found.rule->selector.uid == 99);
        std::unordered_set<std::string> listKeys;
        for (const auto& icon : CatalogJson()["icons"]) CHECK(listKeys.insert(icon["stableKey"].get<std::string>()).second);
    }
}
void ExplicitBatchReapplyRetriesReassociatedRegistration() {
    ResetFixture(); auto& row = bridge.shared->icons[0];
    wcscpy_s(row.windowClass, L"SharedTrayHost"); wcscpy_s(row.originalTip, L"Primary");
    row.callback = WM_APP + 8;
    auto saved = ParseRule(settings["rules"][0]);
    saved.selector = SelectorFor(Record(row), RuleRecords(bridge.Snapshot()));
    settings["rules"][0] = RuleJson(saved); CHECK(SaveSettings(settingsPath, settings)); LoadRules();
    row.identity.uid = 99; row.registration += 100; row.custom = row.managed = false;
    bridge.nextError = ERROR_FILE_NOT_FOUND;
    bridge.shared->snapshotSequence += 2; Refresh();
    CHECK(failedStamp.contains(TargetKey())); bridge.commands.clear();
    auto reply = Call("rules.apply", {{"rules", Json::array({RuleJson(saved)})}});
    CHECK(reply.value("success", false)); CHECK(!failedStamp.contains(TargetKey()));
    CHECK(!IconCommands().empty());
    for (const auto& command : IconCommands()) CHECK(command.identity.ownerPid == row.identity.ownerPid);
}
void UnresolvedIdentityCannotPoisonConfiguration() {
    ResetFixture(); auto before = settings;
    bridge.shared->icons[0].executable[0] = 0;
    bridge.shared->snapshotSequence += 2;
    Json reply;
    try { reply = Call("icon.visibility", {{"key", TargetKey()}, {"visibility", 1}}); }
    catch (...) { reply = {{"success", false}}; }
    CHECK(!reply.value("success", true)); CHECK(settings == before);
    auto peerKey = IdentityKey(bridge.shared->icons[1].identity);
    CHECK(Call("icon.visibility", {{"key", peerKey}, {"visibility", 1}}).value("success", false));
}
void StartupRunPreflightFailureCannotChangeTask() {
    for (bool previous : {false, true}) {
        ResetFixture(); settings["startup"] = previous; auto before = settings;
        startupTaskEnabled = previous; startupRunOpenError = ERROR_ACCESS_DENIED;
        auto reply = Call("settings.apply", {{"startup", !previous}});
        CHECK(!reply.value("success", true)); CHECK(settings == before);
        CHECK(startupTaskEnabled == previous); CHECK(startupTaskWrites == 0);
    }
}
void NormalizeFailureDoesNotCommitSettingsOrRevision() {
    ResetFixture(); auto valid = settings; auto revision = configRevision;
    bool recovered{}; auto disk = LoadSettings(settingsPath, recovered);
    settings["rules"][0]["selector"]["executable"] = "";
    auto invalid = settings;
    CHECK(!Persist()); CHECK(settings == invalid); CHECK(configRevision == revision);
    CHECK(LoadSettings(settingsPath, recovered) == disk);
    settings = valid;
    CHECK(Call("icon.visibility", {{"key", TargetKey()}, {"visibility", 1}}).value("success", false));
}
void StartupRunDeleteFailureRestoresTask() {
    for (bool previous : {false, true}) {
        ResetFixture(); settings["startup"] = previous; auto before = settings;
        startupTaskEnabled = previous; startupRunOpenError = ERROR_SUCCESS;
        startupRunDeleteError = ERROR_WRITE_FAULT;
        startupRunCommand = L"\"" + (root / L"TrayAssistant.Agent.exe").wstring() + L"\" --background";
        auto reply = Call("settings.apply", {{"startup", !previous}});
        CHECK(!reply.value("success", true)); CHECK(settings == before);
        CHECK(startupTaskEnabled == previous);
    }
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    fixtureRoot = std::filesystem::path(argv[1]); std::filesystem::create_directories(fixtureRoot);
    int failed{};
    for (const auto& test : {std::pair{"unresolved owner cannot poison later configuration saves", UnresolvedIdentityCannotPoisonConfiguration},
                            std::pair{"normalization failure does not commit configuration or revision", NormalizeFailureDoesNotCommitSettingsOrRevision},
                            std::pair{"Run registry preflight failure leaves task unchanged", StartupRunPreflightFailureCannotChangeTask},
                            std::pair{"Run retirement failure restores startup task", StartupRunDeleteFailureRestoresTask},
                            std::pair{"generic recreated UID inherits saved hiding without touching peer", GenericUidChangeRestoresHiddenPolicy},
                            std::pair{"identity metadata learning does not replay visual policy", IdentityLearningDoesNotReplayVisualPolicy},
                            std::pair{"own original preview is independent of replacement pixels", OwnOriginalPreviewDoesNotFollowReplacementPixels},
                            std::pair{"provisional identity cannot train and delayed UID restores secondary", ProvisionalAssociationCannotTrainIdentityAndDelayedUidRestores},
                            std::pair{"explicit row confirmation survives dynamic image and delayed old UID", ConfirmedRowKeepsItsOwnRuleWhenOldUidArrives},
                            std::pair{"explicit batch reapply retries changed UID", ExplicitBatchReapplyRetriesReassociatedRegistration},
                            std::pair{"generic blink preview retains visible image", BlinkPresentationKeepsVisibleFrameForAnyApplication},
                            std::pair{"hidden native image bank does not create public rows", NativeHiddenImageSlotsDoNotBecomeCatalogRows},
                            std::pair{"assistant-hidden active icon stays controllable", AssistantHiddenActiveIconRemainsControllable},
                            std::pair{"source hidden registration appears on activation", SourceHiddenSlotBecomesListedWhenApplicationShowsIt},
                            std::pair{"silent exit pruned without notifications", SilentExitIsRemovedWithoutAnotherNotification},
                            std::pair{"reopen pruned while simultaneous instances remain", ReopenDoesNotAccumulateAndKeepsLiveInstances},
                            std::pair{"stale visibility rejected before saving", StaleVisibilityIsRejectedBeforeSaving},
                            std::pair{"stale sibling does not block live visibility", StaleSiblingCannotBlockLiveVisibility},
                            std::pair{"native read failure retains catalog and settings", FailedNativeReadKeepsCatalogAndCannotSaveVisibility},
                            std::pair{"incomplete shared read cannot empty catalog", IncompleteSharedReadCannotBecomeAnEmptyCatalog},
                            std::pair{"duplicate snapshot keeps accepted catalog", DuplicateSnapshotCannotReplaceAcceptedCatalog},
                            std::pair{"same identity recreation restores saved policy", SameIdentityRecreationRestoresSavedPolicy},
                            std::pair{"same identity recreation drops old blink preview", SameIdentityRecreationCannotReuseOldBlinkPreview},
                            std::pair{"immediate visibility after GUID recreation restores image", ImmediateVisibilityAfterGuidRecreationRestoresOtherPolicy},
                            std::pair{"blank first snapshot uses original without changing live pixels", BlinkPresentationUsesOriginalWhenFirstSeenBlank},
                            std::pair{"custom blink image available before first visible snapshot", BlinkPresentationHasCustomImageBeforeFirstVisibleSnapshot},
                            std::pair{"removed import cannot change settings", RemovedImportCannotChangeSettings},
                            std::pair{"immediate visibility and no replay", ImmediateVisibility},
                            std::pair{"unrelated saves do not replay", UnrelatedSavesKeepRulesApplied},
                            std::pair{"explicit rule reapply retries selected icon", ExplicitRuleReapplyRetriesOnlySelectedIcon},
                            std::pair{"saved replies carry current revision", SavedRepliesHaveCurrentRevision},
                            std::pair{"legacy placement ignored", LegacyPlacementIgnored},
                            std::pair{"invalid visibility rejected before persist", InvalidVisibilityCannotPersist},
                            std::pair{"exit race rollback", NativeExitRaceRollsBackRule},
                            std::pair{"save failure keeps native icon", SaveFailureCannotChangeNativeIcon},
                            std::pair{"full rule validation retained", FullRuleValidationStillRuns},
                            std::pair{"shared selector exit rollback", SharedSelectorFanoutRollsBackOnExit},
                            std::pair{"own visibility keeps normal rules", OwnVisibilityDoesNotCreateRule},
                            std::pair{"own hide immediately shows again", OwnHiddenIconCanImmediatelyShowAgain},
                            std::pair{"own single click queues one UI activation", OwnSingleClickQueuesOneOpen},
                            std::pair{"own native menu DPI and upper right anchor", OwnMenuUsesNativeDpiAndUpperRightAnchor},
                            std::pair{"restart launch failure preserves current state", OwnRestartLaunchFailureKeepsCurrentAgentAndSettings},
                            std::pair{"restart requires both exits and honors cancellation", RestartWaitRequiresBothExitsAndHonorsCancellation}}) {
        try { test.second(); std::printf("PASS %s\n", test.first); }
        catch (const std::exception& e) { ++failed; std::fprintf(stderr, "FAIL %s: %s\n", test.first, e.what()); }
    }
    return failed ? 1 : 0;
}
