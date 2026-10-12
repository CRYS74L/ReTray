#include "../native/shell/bridge_client.h"
#include "../native/agent/settings.h"
#include <algorithm>
#include <cstdio>
#include <memory>
#include <stdexcept>
BOOL WINAPI AgentTestNotifyIcon(DWORD operation, PNOTIFYICONDATAW data);
BOOL WINAPI AgentTestTrackPopupMenu(HMENU, UINT, int, int, int, HWND, const RECT*);
BOOL WINAPI AgentTestCreateProcessW(LPCWSTR, LPWSTR, LPSECURITY_ATTRIBUTES, LPSECURITY_ATTRIBUTES,
                                  BOOL, DWORD, LPVOID, LPCWSTR, LPSTARTUPINFOW,
                                  LPPROCESS_INFORMATION);
int WINAPI AgentTestMessageBoxW(HWND, LPCWSTR, LPCWSTR, UINT);

// Exercise real handlers, native menus, waits, and persistence. Replace only the external
// Explorer bridge and desktop popup/process launch boundaries, keeping the tests isolated.
namespace tray {
struct AgentTestBridgeClient {
    std::unique_ptr<BridgeState> state = std::make_unique<BridgeState>();
    BridgeState* shared{state.get()};
    std::vector<BridgeCommand> commands;
    DWORD nextError{};
    DWORD failPid{};
    bool Alive() const { return shared && shared->status == 1; }
    bool Attach(const SymbolAdapter&, const std::filesystem::path&) { return Alive(); }
    void Reset() {}
    bool Command(BridgeCommand command, DWORD = 2000) {
        commands.push_back(command);
        InterlockedIncrement(&shared->requestSequence);
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
    std::vector<BridgeIcon> Snapshot() const {
        return {shared->icons, shared->icons + shared->count};
    }
};
}
#define BridgeClient AgentTestBridgeClient
#define Shell_NotifyIconW AgentTestNotifyIcon
#define TrackPopupMenu AgentTestTrackPopupMenu
#define CreateProcessW AgentTestCreateProcessW
#define MessageBoxW AgentTestMessageBoxW
#include "../native/agent/main.cpp"
#undef MessageBoxW
#undef CreateProcessW
#undef TrackPopupMenu
#undef Shell_NotifyIconW
#undef BridgeClient

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
    bridge.commands.clear(); bridge.nextError = bridge.failPid = 0;
    for (uint32_t i = 0; i < 2; ++i) {
        auto& row = bridge.shared->icons[i];
        row.identity.shellEpoch = 1; row.identity.ownerHwnd = 100 + i;
        row.identity.ownerPid = 1000 + i; row.identity.ownerCreatedAt = 10 + i;
        row.identity.uid = 11; row.placement = 1; row.live = true;
        wcscpy_s(row.executable, i ? L"C:\\Fixture\\Peer.exe" : L"C:\\Fixture\\Target.exe");
    }
    lifecycle.state = SessionState::Ready; stopping = false; window = nullptr;
    ownOpenQueued = false; ownIdentityKey.clear(); ownIdentityEpoch = 0;
    if (ownImage) { DestroyIcon(ownImage); ownImage = nullptr; }
    settings = DefaultSettings(); configRevision = 0; settingsPath = fixtureRoot / L"settings.json";
    dataRoot = fixtureRoot; rows.clear(); catalog = {}; applied.clear(); failedStamp.clear();
    retryAfter.clear(); outcomes.clear(); seenSequence = -1;
    environmentStamp.clear(); environmentEpoch = lastEnvironmentCheck = 0;
    activityHistory = Json::object(); activityDirty = false; ownAdded = false;
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
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    fixtureRoot = std::filesystem::path(argv[1]); std::filesystem::create_directories(fixtureRoot);
    int failed{};
    for (const auto& test : {std::pair{"immediate visibility and no replay", ImmediateVisibility},
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
