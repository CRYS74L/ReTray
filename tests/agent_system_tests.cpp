#include "../native/shell/bridge_client.h"
#include "../native/agent/settings.h"
#include <cstdio>
#include <map>
#include <memory>
#include <stdexcept>

BOOL WINAPI SystemTestNotifyIcon(DWORD, PNOTIFYICONDATAW);
BOOL WINAPI SystemTestRegisterHotKey(HWND, int, UINT, UINT);
BOOL WINAPI SystemTestUnregisterHotKey(HWND, int);
LSTATUS WINAPI SystemTestRegCreateKeyExW(HKEY, LPCWSTR, DWORD, LPWSTR, DWORD, REGSAM,
                                       const LPSECURITY_ATTRIBUTES, PHKEY, LPDWORD);

// The actual RPC and file transactions run in this process. Explorer, the own icon,
// hotkey registration and the user's Run key are the only replaced external boundaries.
namespace tray {
struct SystemTestBridgeClient {
    std::unique_ptr<BridgeState> state = std::make_unique<BridgeState>();
    BridgeState* shared{state.get()};
    std::map<int, bool> hidden{{0, false}, {1, false}};
    std::vector<BridgeCommand> commands;
    DWORD readError{}, setError{};
    int failingId{-1};
    bool failAfterChange{};
    bool Alive() const { return shared && shared->status == 1; }
    bool Attach(const SymbolAdapter&, const std::filesystem::path&) { return Alive(); }
    void Reset() { for (auto& [id, value] : hidden) value = false; }
    void PublishSystems() {
        shared->systemCount = 0;
        for (const auto& [id, value] : hidden) {
            auto& row = shared->systemControls[shared->systemCount++];
            row = {}; row.id = id; row.visible = !value; row.instances = 1;
        }
    }
    bool Command(BridgeCommand command, DWORD = 2000) {
        commands.push_back(command);
        shared->lastError = 0;
        if (command.operation == BridgeOperation::SystemRead) {
            PublishSystems();
            shared->lastError = std::exchange(readError, 0);
        } else if (command.operation == BridgeOperation::SystemSet) {
            PublishSystems();
            auto found = hidden.find(command.location);
            if (found == hidden.end() && command.visibility == 1) shared->lastError = ERROR_NOT_FOUND;
            else if (found == hidden.end()) {}
            else if (failingId == command.location && setError) {
                if (failAfterChange) found->second = command.visibility == 1;
                shared->lastError = std::exchange(setError, 0);
            } else found->second = command.visibility == 1;
        }
        return shared->lastError == 0;
    }
    std::vector<BridgeIcon> Snapshot() const {
        return {shared->icons, shared->icons + shared->count};
    }
};
} // namespace tray
#define BridgeClient SystemTestBridgeClient
#define Shell_NotifyIconW SystemTestNotifyIcon
#define RegisterHotKey SystemTestRegisterHotKey
#define UnregisterHotKey SystemTestUnregisterHotKey
#define RegCreateKeyExW SystemTestRegCreateKeyExW
#include "../native/agent/main.cpp"
#undef RegCreateKeyExW
#undef UnregisterHotKey
#undef RegisterHotKey
#undef Shell_NotifyIconW
#undef BridgeClient

#define CHECK(x) do { if (!(x)) throw std::runtime_error(#x); } while (0)
unsigned hotkeyRegistrations{};
BOOL WINAPI SystemTestNotifyIcon(DWORD, PNOTIFYICONDATAW) { return TRUE; }
BOOL WINAPI SystemTestRegisterHotKey(HWND, int, UINT, UINT) { ++hotkeyRegistrations; return TRUE; }
BOOL WINAPI SystemTestUnregisterHotKey(HWND, int) { return TRUE; }
LSTATUS WINAPI SystemTestRegCreateKeyExW(HKEY, LPCWSTR, DWORD, LPWSTR, DWORD, REGSAM,
                                       const LPSECURITY_ATTRIBUTES, PHKEY, LPDWORD) {
    return ERROR_ACCESS_DENIED;
}
std::filesystem::path fixtureRoot;
Json Call(const std::string& method, Json data) {
    return Handle({{"schema", 1}, {"method", method}, {"data", std::move(data)}});
}
void ResetFixture() {
    bridge.state = std::make_unique<BridgeState>(); bridge.shared = bridge.state.get();
    bridge.shared->status = 1; bridge.shared->epoch = 1;
    bridge.hidden = {{0, false}, {1, false}}; bridge.commands.clear();
    bridge.readError = bridge.setError = 0; bridge.failingId = -1; bridge.failAfterChange = false;
    lifecycle.state = SessionState::Ready; stopping = false; window = nullptr;
    settings = DefaultSettings(); settings["ownIcon"]["visible"] = false;
    configRevision = 0; settingsPath = fixtureRoot / L"settings.json"; root = dataRoot = fixtureRoot;
    rows.clear(); catalog = {}; rules.clear(); applied.clear(); failedStamp.clear(); retryAfter.clear();
    outcomes.clear(); seenSequence = -1; environmentStamp.clear();
    environmentEpoch = lastEnvironmentCheck = 0; environmentOutcome = Result(true);
    requestedHotkeys = registeredHotkeys = hotkeyErrors = Json::array(); hotkeyRegistrations = 0;
    activityHistory = Json::object(); activityDirty = false; ownAdded = false;
    CHECK(SaveSettings(settingsPath, settings));
}
std::vector<BridgeCommand> SystemChanges() {
    std::vector<BridgeCommand> result;
    for (const auto& command : bridge.commands)
        if (command.operation == BridgeOperation::SystemSet) result.push_back(command);
    return result;
}
void SingleSystemToggleIgnoresUnavailableSavedPeer() {
    ResetFixture(); settings["systemControls"] = {{{"id", 5}, {"hidden", true}}};
    CHECK(SaveSettings(settingsPath, settings));
    auto response = Call("system.set", {{"id", 0}, {"visible", false}});
    CHECK(response.value("success", false)); CHECK(response.value("saved", false));
    CHECK(response["data"]["id"] == 0); CHECK(response["data"]["visible"] == false);
    CHECK(bridge.hidden.at(0)); CHECK(!bridge.hidden.at(1));
    auto changes = SystemChanges(); CHECK(changes.size() == 1); CHECK(changes[0].location == 0);
    CHECK(settings["systemControls"] == Json::array({{{"id", 5}, {"hidden", true}},
                                                     {{"id", 0}, {"hidden", true}}}));
    bool recovered{}; CHECK(LoadSettings(settingsPath, recovered)["systemControls"] == settings["systemControls"]);
    response = Call("system.set", {{"id", 0}, {"visible", true}});
    CHECK(response.value("success", false)); CHECK(!bridge.hidden.at(0));
    CHECK(settings["systemControls"] == Json::array({{{"id", 5}, {"hidden", true}}}));
}
void MissingHideCannotSaveButMissingShowClearsPreference() {
    ResetFixture(); auto before = settings;
    auto response = Call("system.set", {{"id", 5}, {"visible", false}});
    CHECK(!response.value("success", true)); CHECK(!response.value("saved", true));
    CHECK(response["errorCode"] == ERROR_NOT_FOUND); CHECK(settings == before);
    CHECK(SystemChanges().empty());
    settings["systemControls"] = {{{"id", 5}, {"hidden", true}}, {{"id", 1}, {"hidden", true}}};
    CHECK(SaveSettings(settingsPath, settings));
    response = Call("system.set", {{"id", 5}, {"visible", true}});
    CHECK(response.value("success", false)); CHECK(response.value("saved", false));
    CHECK(response["data"]["visible"] == true);
    auto changes = SystemChanges(); CHECK(changes.size() == 1);
    CHECK(changes[0].location == 5 && changes[0].visibility == 0);
    CHECK(!bridge.hidden.contains(5));
    CHECK(settings["systemControls"] == Json::array({{{"id", 1}, {"hidden", true}}}));
}
void SystemSaveFailureRestoresPreviousTargetPolicy() {
    for (bool priorHidden : {false, true}) {
        ResetFixture();
        if (priorHidden) settings["systemControls"] = {{{"id", 0}, {"hidden", true}}};
        bridge.hidden[0] = priorHidden; CHECK(SaveSettings(settingsPath, settings));
        auto before = settings; settingsPath = fixtureRoot; // A directory cannot replace a file.
        auto response = Call("system.set", {{"id", 0}, {"visible", priorHidden}});
        CHECK(!response.value("success", true)); CHECK(!response.value("saved", true));
        CHECK(response["errorCode"] == ERROR_WRITE_FAULT); CHECK(settings == before);
        CHECK(bridge.hidden.at(0) == priorHidden); CHECK(!bridge.hidden.at(1));
        auto changes = SystemChanges(); CHECK(changes.size() == 2);
        CHECK(changes[0].location == 0 && changes[1].location == 0);
        CHECK(changes[1].visibility == (priorHidden ? 1 : 0));
        bool recovered{}; CHECK(LoadSettings(fixtureRoot / L"settings.json", recovered) == before);
    }
}
void FailedSystemNativeActionRestoresPolicyWithoutSaving() {
    ResetFixture(); auto before = settings;
    bridge.failingId = 0; bridge.setError = ERROR_NOT_SUPPORTED; bridge.failAfterChange = true;
    auto response = Call("system.set", {{"id", 0}, {"visible", false}});
    CHECK(!response.value("success", true)); CHECK(!response.value("saved", true));
    CHECK(response["errorCode"] == ERROR_NOT_SUPPORTED); CHECK(settings == before);
    CHECK(!bridge.hidden.at(0)); CHECK(configRevision == 0);
    bool recovered{}; CHECK(LoadSettings(settingsPath, recovered) == before);
}
void InvalidSystemRequestCannotActOrPersist() {
    ResetFixture(); auto before = settings;
    for (auto id : {Json(-1), Json(9), Json(0.0), Json("0"), Json(UINT64_MAX)}) {
        auto response = Call("system.set", {{"id", id}, {"visible", false}});
        CHECK(!response.value("success", true)); CHECK(settings == before);
    }
    for (auto visible : {Json(0), Json("false"), Json(nullptr)}) {
        auto response = Call("system.set", {{"id", 0}, {"visible", visible}});
        CHECK(!response.value("success", true)); CHECK(settings == before);
    }
    CHECK(bridge.commands.empty());
}
void ReadFailureCannotPersistNewPolicy() {
    ResetFixture(); auto before = settings; bridge.readError = ERROR_TIMEOUT;
    auto response = Call("system.set", {{"id", 0}, {"visible", false}});
    CHECK(!response.value("success", true)); CHECK(!response.value("saved", true));
    CHECK(settings == before); CHECK(SystemChanges().empty());
}
void UnchangedSystemFailuresAreNotRetriedInBackground() {
    ResetFixture(); settings["systemControls"] = {{{"id", 5}, {"hidden", true}}};
    CHECK(!ApplyEnvironment()); auto attempted = SystemChanges().size();
    lastEnvironmentCheck = 0; CHECK(!ApplyEnvironment());
    CHECK(SystemChanges().size() == attempted);
}
void SystemReadAppliesPolicyWhenMissingTargetAppears() {
    ResetFixture(); settings["systemControls"] = {{{"id", 5}, {"hidden", true}}};
    CHECK(!ApplyEnvironment()); bridge.commands.clear();
    bridge.hidden.emplace(5, false); // A previously absent native target appears.
    auto response = Call("system.read", Json::object());
    CHECK(response.value("success", false)); CHECK(bridge.hidden.at(5));
    auto changes = SystemChanges(); CHECK(changes.size() == 1); CHECK(changes[0].location == 5);
    CHECK(response["data"][2]["id"] == 5 && response["data"][2]["visible"] == false);
    Call("system.read", Json::object()); lastEnvironmentCheck = 0; ApplyEnvironment();
    CHECK(SystemChanges().size() == 1); CHECK(configRevision == 0);
}
void ExistingFailedTargetIsNotRetriedByObjectRefresh() {
    ResetFixture(); settings["systemControls"] = {{{"id", 0}, {"hidden", true}}};
    bridge.failingId = 0; bridge.setError = ERROR_NOT_SUPPORTED;
    CHECK(!ApplyEnvironment()); auto attempted = SystemChanges().size();
    Call("system.read", Json::object()); lastEnvironmentCheck = 0; ApplyEnvironment();
    CHECK(SystemChanges().size() == attempted); CHECK(!bridge.hidden.at(0));
}
void MaintenanceFailureDoesNotBlockCurrentSystemRows() {
    ResetFixture(); settings["systemControls"] = {{{"id", 5}, {"hidden", true}}};
    CHECK(!ApplyEnvironment()); bridge.commands.clear();
    bridge.hidden.emplace(5, false); bridge.failingId = 5; bridge.setError = ERROR_NOT_SUPPORTED;
    auto response = Call("system.set", {{"id", 0}, {"visible", false}});
    CHECK(response.value("success", false));
    response = Call("system.read", Json::object());
    CHECK(response.value("success", false)); CHECK(response["data"].size() == 3);
    CHECK(response["data"][0]["visible"] == false); CHECK(response["data"][2]["visible"] == true);
    auto attempted = SystemChanges().size(); Call("system.read", Json::object());
    CHECK(SystemChanges().size() == attempted);
}
void SystemRowsExposeAbsentSavedPreferenceWithoutApplyingAnything() {
    ResetFixture(); settings["systemControls"] = {{{"id", 0}, {"hidden", true}},
                                                   {{"id", 5}, {"hidden", true}}};
    CHECK(!ApplyEnvironment()); bridge.commands.clear();
    auto response = Call("system.read", Json::object());
    CHECK(response.value("success", false)); CHECK(response["data"].size() == 3);
    CHECK(response["data"][0]["id"] == 0 && response["data"][0]["savedHidden"] == true);
    CHECK(response["data"][1]["id"] == 1 && response["data"][1]["savedHidden"] == false);
    CHECK(response["data"][2] == Json({{"id", 5}, {"instances", 0}, {"visible", false},
                                      {"savedHidden", true}}));
    CHECK(SystemChanges().empty()); CHECK(configRevision == 0);
}
void SuccessfulSystemTransactionDoesNotReplayFailedPeer() {
    ResetFixture(); settings["systemControls"] = {{{"id", 5}, {"hidden", true}}};
    CHECK(!ApplyEnvironment()); bridge.commands.clear();
    auto response = Call("system.set", {{"id", 0}, {"visible", false}});
    CHECK(response.value("success", false)); ApplyEnvironment();
    auto changes = SystemChanges(); CHECK(changes.size() == 1); CHECK(changes[0].location == 0);
}
void CanceledAdvancedSettingsCannotApplyNativeBehavior() {
    ResetFixture();
    auto response = Call("settings.apply", {{"hotkeys", {{"open", (MOD_ALT << 16) | 'Q'}}},
                        {"autoSort", true}, {"uiTopmost", true}, {"uiOpacity", 77},
                        {"reviewIntervalSeconds", 2},
                        {"taskbar", {{"opacity", 88}, {"hideStart", true}}}});
    CHECK(response.value("success", false));
    CHECK(settings["hotkeys"]["open"] == 0); CHECK(settings["hotkeys"]["menu"] == 0);
    CHECK(settings["autoSort"] == false); CHECK(settings["uiTopmost"] == false);
    CHECK(settings["uiOpacity"] == 255); CHECK(settings["reviewIntervalSeconds"] == 0);
    CHECK(settings["taskbar"]["opacity"] == 255); CHECK(settings["taskbar"]["hideStart"] == false);
    ApplyEnvironment();
    for (const auto& command : bridge.commands) CHECK(command.operation != BridgeOperation::Taskbar);
    requestedHotkeys = {{{"id", 1}, {"code", (MOD_ALT << 16) | 'Q'}, {"action", "open"}}};
    RegisterRequestedHotkeys(); CHECK(hotkeyRegistrations == 0); CHECK(registeredHotkeys.empty());
    CHECK(!Call("taskbar.apply", {{"opacity", 88}, {"hideStart", true}}).value("success", true));
    CHECK(Call("settings.apply", {{"systemControls", Json::array({{{"id", 0}, {"hidden", true}}})}})
              .value("success", false));
    CHECK(settings["systemControls"].empty()); CHECK(!bridge.hidden.at(0));
    auto before = settings;
    response = Call("system.apply", {{"controls", Json::array({{{"id", 0}, {"hidden", true}}})}});
    CHECK(!response.value("success", true)); CHECK(!response.value("saved", true));
    CHECK(settings == before); CHECK(!bridge.hidden.at(0));
}
void ImportedLegacyPoliciesKeepCustomAppearanceAndManualVisibility() {
    ResetFixture(); auto imported = settings;
    NativeIconRecord record; record.executable = L"C:\\Fixture\\Custom.exe"; record.identity.uid = 12;
    IconRule rule; rule.selector = SelectorFor(record); rule.visibility = 1;
    rule.asset = L"custom.dll"; rule.assetIndex = 7; rule.tip = L"custom tip";
    auto legacy = RuleJson(rule);
    legacy.update({{"swapButtons", true}, {"alwaysShow", true}, {"inactiveSeconds", 10},
                   {"unusedDays", 2}, {"hotkey", (MOD_ALT << 16) | 'Q'}, {"hotkeyAction", 3},
                   {"menuVisible", false}, {"newState", 1}});
    imported["rules"] = {legacy}; imported["iconOrder"] = {"custom", "@assistant"};
    imported["hotkeys"] = {{"open", (MOD_ALT << 16) | 'Q'}}; imported["uiTopmost"] = true;
    imported["taskbar"] = {{"opacity", 77}, {"hideStart", true}};
    auto response = Call("settings.import", {{"settings", imported}});
    CHECK(response.value("success", false)); CHECK(response.value("saved", false));
    auto parsed = ParseRule(settings["rules"][0]);
    CHECK(parsed.visibility == 1); CHECK(parsed.asset == L"custom.dll"); CHECK(parsed.assetIndex == 7);
    CHECK(parsed.tip == L"custom tip"); CHECK(!parsed.swapButtons && !parsed.alwaysShow);
    CHECK(parsed.inactiveSeconds == 0 && parsed.unusedDays == 0 && parsed.hotkey == 0);
    CHECK(parsed.menuVisible); CHECK(settings["iconOrder"] == Json::array({"custom", "@assistant"}));
    bool recovered{}; CHECK(LoadSettings(settingsPath, recovered) == settings);
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    fixtureRoot = std::filesystem::path(argv[1]); std::filesystem::create_directories(fixtureRoot);
    int failed{};
    for (const auto& test : {std::pair{"single system target excludes missing peer", SingleSystemToggleIgnoresUnavailableSavedPeer},
                            std::pair{"missing hide and missing show", MissingHideCannotSaveButMissingShowClearsPreference},
                            std::pair{"save failure restores target policy", SystemSaveFailureRestoresPreviousTargetPolicy},
                            std::pair{"native failure does not persist", FailedSystemNativeActionRestoresPolicyWithoutSaving},
                            std::pair{"invalid system parameters", InvalidSystemRequestCannotActOrPersist},
                            std::pair{"system read failure", ReadFailureCannotPersistNewPolicy},
                            std::pair{"background failures do not retry unchanged policy", UnchangedSystemFailuresAreNotRetriedInBackground},
                            std::pair{"system read applies newly present target", SystemReadAppliesPolicyWhenMissingTargetAppears},
                            std::pair{"object refresh does not retry existing failure", ExistingFailedTargetIsNotRetriedByObjectRefresh},
                            std::pair{"maintenance failure keeps current system rows readable", MaintenanceFailureDoesNotBlockCurrentSystemRows},
                            std::pair{"system rows expose absent saved preference", SystemRowsExposeAbsentSavedPreferenceWithoutApplyingAnything},
                            std::pair{"system success does not replay failed peer", SuccessfulSystemTransactionDoesNotReplayFailedPeer},
                            std::pair{"canceled advanced native behavior", CanceledAdvancedSettingsCannotApplyNativeBehavior},
                            std::pair{"import legacy keeps appearance and manual visibility", ImportedLegacyPoliciesKeepCustomAppearanceAndManualVisibility}}) {
        try { test.second(); std::printf("PASS %s\n", test.first); }
        catch (const std::exception& error) { ++failed; std::fprintf(stderr, "FAIL %s: %s\n", test.first, error.what()); }
    }
    return failed ? 1 : 0;
}
