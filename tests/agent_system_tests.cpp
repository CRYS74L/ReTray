#include "../native/shell/bridge_client.h"
#include "../native/agent/settings.h"
#include "../native/platform/input_profiles.h"
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
    std::map<int, bool> pendingPolicies;
    std::vector<BridgeCommand> commands;
    DWORD readError{}, setError{};
    int failingId{-1};
    bool failAfterChange{};
    bool failLogo{};
    std::string displayedInputProfile;
    bool Alive() const { return shared && shared->status == 1; }
    DWORD LastError() const { return shared ? static_cast<DWORD>(shared->lastError) : ERROR_NOT_READY; }
    bool CommandBatch(const std::vector<BridgeCommand>& batch, std::vector<DWORD>& errors) {
        errors.clear();
        for (const auto& command : batch) {
            auto success = Command(command);
            errors.push_back(success ? ERROR_SUCCESS : static_cast<DWORD>(shared->lastError));
        }
        return true;
    }
    void AddNativeControl(int id) { hidden[id] = pendingPolicies.contains(id) && pendingPolicies.at(id); }
    bool Attach(const SymbolAdapter&, const std::filesystem::path&) { return Alive(); }
    void Reset() { for (auto& [id, value] : hidden) value = false; }
    void PublishSystems() {
        shared->systemCount = 0;
        for (const auto& [id, value] : hidden) {
            auto& row = shared->systemControls[shared->systemCount++];
            row = {}; row.id = id; row.visible = !value; row.instances = 1;
            if(id==3)wcsncpy_s(row.inputProfileKey,Wide(displayedInputProfile).c_str(),_TRUNCATE);
        }
    }
    bool Command(BridgeCommand command, DWORD = 2000) {
        commands.push_back(command);
        shared->lastError = 0;
        if(command.operation==BridgeOperation::InputMethodLogo) {
            if(failLogo)shared->lastError=ERROR_NOT_READY;
        } else if (command.operation == BridgeOperation::SystemRead) {
            PublishSystems();
            shared->lastError = std::exchange(readError, 0);
        } else if (command.operation == BridgeOperation::SystemSet || command.operation == BridgeOperation::SystemPolicy) {
            if (command.operation == BridgeOperation::SystemPolicy) pendingPolicies[command.location] = command.visibility == 1;
            PublishSystems();
            auto found = hidden.find(command.location);
            if (found == hidden.end() && command.visibility == 1 && command.operation == BridgeOperation::SystemSet) shared->lastError = ERROR_NOT_FOUND;
            else if (found == hidden.end()) {}
            else if (failingId == command.location && setError) {
                if (failAfterChange) found->second = command.visibility == 1;
                shared->lastError = std::exchange(setError, 0);
            } else found->second = command.visibility == 1;
        }
        return shared->lastError == 0;
    }
    std::vector<BridgeIcon> Snapshot(bool* complete = nullptr) const {
        if (complete) *complete = true;
        return {shared->icons, shared->icons + shared->count};
    }
};
} // namespace tray
std::vector<tray::InputProfile> SystemTestInputProfiles() {
    tray::InputProfile row;
    tray::ParseInputProfileKey("tsf:0804:{86598FB9-66A2-463E-B9C2-AEB906D477AD}:{607FDF85-FCC8-4DBD-A365-41296F980C9C}",row.id);
    row.key=tray::InputProfileKey(row.id);row.name=L"Fixture input method";row.active=false;
    return {row};
}
#define EnumerateInputProfiles SystemTestInputProfiles
#define BridgeClient SystemTestBridgeClient
#define Shell_NotifyIconW SystemTestNotifyIcon
#define RegisterHotKey SystemTestRegisterHotKey
#define UnregisterHotKey SystemTestUnregisterHotKey
#define RegCreateKeyExW SystemTestRegCreateKeyExW
#include "../native/agent/main.cpp"
#undef EnumerateInputProfiles
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
    bridge.pendingPolicies.clear();
    bridge.readError = bridge.setError = 0; bridge.failingId = -1; bridge.failAfterChange = false;
    lifecycle.state = SessionState::Ready; stopping = false; window = nullptr;
    settings = DefaultSettings(); settings["ownIcon"]["visible"] = false;
    configRevision = 0; settingsPath = fixtureRoot / L"settings.json"; root = dataRoot = fixtureRoot;
    rows.clear(); catalog = {}; rules.clear(); applied.clear(); failedStamp.clear(); retryAfter.clear();
    outcomes.clear(); seenSequence = -1; environmentStamp.clear();
    environmentEpoch = lastEnvironmentCheck = environmentRetryAt = 0; environmentOutcome = Result(true);
    requestedHotkeys = registeredHotkeys = hotkeyErrors = Json::array(); hotkeyRegistrations = 0;
    activityHistory = Json::object(); activityDirty = false; ownAdded = false;
    importOwnPending = false; importOwnRetryAt = 0;
    registrations.clear(); lastCatalogRefresh = 0;
    inputMethodStamp.clear();inputMethodEpoch=inputMethodRetryAt=0;appliedInputMethods.clear();inputMethodApplySuccess=true;inputMethodRollback=nullptr;
    bridge.failLogo=false;
    bridge.displayedInputProfile.clear();
    CHECK(SaveSettings(settingsPath, settings));
}
std::vector<BridgeCommand> SystemChanges() {
    std::vector<BridgeCommand> result;
    for (const auto& command : bridge.commands)
        if (command.operation == BridgeOperation::SystemSet || command.operation == BridgeOperation::SystemPolicy) result.push_back(command);
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
    for (auto id : {Json(-1), Json(10), Json(0.0), Json("0"), Json(UINT64_MAX)}) {
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
    ResetFixture(); settings["systemControls"] = {{{"id", 0}, {"hidden", true}}};
    bridge.failingId = 0; bridge.setError = ERROR_NOT_SUPPORTED;
    CHECK(!ApplyEnvironment()); auto attempted = SystemChanges().size();
    lastEnvironmentCheck = 0; CHECK(!ApplyEnvironment());
    CHECK(SystemChanges().size() == attempted);
}
void SystemReadAppliesPolicyWhenMissingTargetAppears() {
    ResetFixture(); settings["systemControls"] = {{{"id", 6}, {"hidden", true}}};
    CHECK(ApplyEnvironment()); bridge.commands.clear();
    bridge.AddNativeControl(6); // The XAML event applies the preloaded policy before any RPC.
    CHECK(bridge.hidden.at(6)); CHECK(bridge.commands.empty());
    auto response = Call("system.read", Json::object());
    CHECK(response.value("success", false)); CHECK(bridge.hidden.at(6));
    CHECK(SystemChanges().empty());
    CHECK(response["data"][2]["id"] == 6 && response["data"][2]["visible"] == false);
    Call("system.read", Json::object()); lastEnvironmentCheck = 0; ApplyEnvironment();
    CHECK(SystemChanges().empty()); CHECK(configRevision == 0);
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
    CHECK(ApplyEnvironment()); bridge.commands.clear();
    bridge.AddNativeControl(5);
    auto response = Call("system.set", {{"id", 0}, {"visible", false}});
    CHECK(response.value("success", false));
    response = Call("system.read", Json::object());
    CHECK(response.value("success", false)); CHECK(response["data"].size() == 4);
    CHECK(response["data"][0]["visible"] == false); CHECK(response["data"][2]["visible"] == false);
    auto attempted = SystemChanges().size(); Call("system.read", Json::object());
    CHECK(SystemChanges().size() == attempted);
}
void SystemRowsExposeAbsentSavedPreferenceWithoutApplyingAnything() {
    ResetFixture(); settings["systemControls"] = {{{"id", 0}, {"hidden", true}},
                                                   {{"id", 5}, {"hidden", true}}};
    CHECK(ApplyEnvironment()); bridge.commands.clear();
    auto response = Call("system.read", Json::object());
    CHECK(response.value("success", false)); CHECK(response["data"].size() == 4);
    CHECK(response["data"][0]["id"] == 0 && response["data"][0]["savedHidden"] == true);
    CHECK(response["data"][1]["id"] == 1 && response["data"][1]["savedHidden"] == false);
    CHECK(response["data"][2] == Json({{"id", 5}, {"instances", 0}, {"visible", false},
                                      {"savedHidden", true}}));
    CHECK(SystemChanges().empty()); CHECK(configRevision == 0);
}
void SuccessfulSystemTransactionDoesNotReplayFailedPeer() {
    ResetFixture(); settings["systemControls"] = {{{"id", 5}, {"hidden", true}}};
    CHECK(ApplyEnvironment()); bridge.commands.clear();
    auto response = Call("system.set", {{"id", 0}, {"visible", false}});
    CHECK(response.value("success", false)); ApplyEnvironment();
    auto changes = SystemChanges(); CHECK(changes.size() == 1); CHECK(changes[0].location == 0);
}
void PrivacyPreferenceDoesNotRequireActiveIndicator() {
    ResetFixture();
    auto response=Call("system.set",{{"id",6},{"visible",false}});
    CHECK(response.value("success",false));CHECK(response.value("saved",false));
    CHECK(!bridge.hidden.contains(6));CHECK(bridge.pendingPolicies.at(6));
    auto read=Call("system.read",Json::object());
    CHECK(read["data"].back()["id"]==6);CHECK(read["data"].back()["savedHidden"]==true);
    response=Call("system.set",{{"id",6},{"visible",true}});
    CHECK(response.value("success",false));CHECK(!bridge.pendingPolicies.at(6));
    read=Call("system.read",Json::object());
    CHECK(read["data"].back()["id"]==6);CHECK(read["data"].back()["savedHidden"]==false);
    CHECK(read["data"].back()["instances"]==0);
    bridge.AddNativeControl(6);CHECK(!bridge.hidden.at(6));
}
void InputMethodAndLanguageAreIndependent() {
    ResetFixture();bridge.hidden.emplace(3,false);bridge.hidden.emplace(9,false);
    auto response=Call("system.set",{{"id",3},{"visible",false}});
    CHECK(response.value("success",false));CHECK(bridge.hidden.at(3));CHECK(!bridge.hidden.at(9));
    response=Call("system.set",{{"id",9},{"visible",false}});
    CHECK(response.value("success",false));CHECK(bridge.hidden.at(3));CHECK(bridge.hidden.at(9));
    response=Call("system.set",{{"id",3},{"visible",true}});
    CHECK(response.value("success",false));CHECK(!bridge.hidden.at(3));CHECK(bridge.hidden.at(9));
    bool recovered{};auto persisted=LoadSettings(settingsPath,recovered);
    CHECK(persisted["systemControls"].size()==1);CHECK(persisted["systemControls"][0]["id"]==9);
    CHECK(persisted["systemControls"][0]["hidden"]==true);CHECK(!recovered);
    auto legacy=DefaultSettings();legacy.erase("splitInputIndicators");legacy["systemControls"]={{{"id",3},{"hidden",true}}};
    auto migrated=NormalizeSettings(legacy);CHECK(migrated["systemControls"].size()==2);
    CHECK(migrated["systemControls"][1]["id"]==9);CHECK(migrated["systemControls"][1]["hidden"]==true);
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
void LoadedLegacyPoliciesKeepCustomAppearanceAndManualVisibility() {
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
    {
        std::ofstream file(settingsPath, std::ios::binary | std::ios::trunc);
        file << imported.dump(2);
    }
    bool recovered{};
    settings = LoadSettings(settingsPath, recovered);
    CHECK(!recovered);
    auto parsed = ParseRule(settings["rules"][0]);
    CHECK(parsed.visibility == 1); CHECK(parsed.asset == L"custom.dll"); CHECK(parsed.assetIndex == 7);
    CHECK(parsed.tip == L"custom tip"); CHECK(!parsed.swapButtons && !parsed.alwaysShow);
    CHECK(parsed.inactiveSeconds == 0 && parsed.unusedDays == 0 && parsed.hotkey == 0);
    CHECK(parsed.menuVisible); CHECK(settings["iconOrder"] == Json::array({"custom", "@assistant"}));
    CHECK(LoadSettings(settingsPath, recovered) == settings);
}
void InputMethodSaveFailureRollsBackCachedImageWithoutOldFile() {
    ResetFixture();auto profile=SystemTestInputProfiles().front();
    Json prior={{"key",profile.key},{"asset",Utf8(fixtureRoot/L"missing-old.png")},{"index",0},{"useReplacement",true}};
    settings["inputMethodIcons"]={prior};CHECK(SaveSettings(settingsPath,settings));auto before=settings;
    settingsPath=fixtureRoot;
    auto wanted=prior;wanted["useReplacement"]=false;
    auto reply=Call("inputMethods.set",wanted);CHECK(!reply.at("success").get<bool>());CHECK(settings==before);
    std::vector<int> phases;for(const auto& command:bridge.commands)if(command.operation==BridgeOperation::InputMethodLogo)phases.push_back(command.index);
    CHECK(phases==std::vector<int>({1,3}));
}
void InputMethodTransientFailureRetriesAfterBackoff() {
    ResetFixture();auto profile=SystemTestInputProfiles().front();
    settings["inputMethodIcons"]={{{"key",profile.key},{"asset",""},{"index",0},{"useReplacement",false}}};
    bridge.failLogo=true;CHECK(!ApplyInputMethodIcons());auto count=bridge.commands.size();
    bridge.failLogo=false;CHECK(!ApplyInputMethodIcons());CHECK(bridge.commands.size()==count);
    Sleep(3100);CHECK(ApplyInputMethodIcons());CHECK(bridge.commands.size()>count);
}
void InactiveInputMethodPolicyPersistsWithoutSwitchingOrChangingStateControl() {
    ResetFixture();auto profile=SystemTestInputProfiles().front();
    auto reply=Call("inputMethods.set",{{"key",profile.key},{"asset",""},{"index",0},{"useReplacement",true}});
    CHECK(reply.at("success").get<bool>());bool recovered{};auto read=LoadSettings(settingsPath,recovered);
    CHECK(!recovered && read["inputMethodIcons"][0]["key"]==profile.key && read["inputMethodIcons"][0]["useReplacement"]==true);
    for(const auto& command:bridge.commands)CHECK(command.operation==BridgeOperation::InputMethodLogo);
    auto invalid=Call("inputMethods.set",{{"key","language:0804"},{"asset",""},{"index",0},{"useReplacement",true}});
    CHECK(!invalid.at("success").get<bool>());
}
void InputMethodActiveFlagUsesShellInsteadOfCallerThread() {
    ResetFixture();auto profile=SystemTestInputProfiles().front();CHECK(!profile.active);
    bridge.hidden[3]=false;bridge.displayedInputProfile=profile.key;
    auto reply=Call("inputMethods.read",Json::object());CHECK(reply.at("success").get<bool>());
    CHECK(reply["data"][0]["active"]==true);
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) return 2;
    fixtureRoot = std::filesystem::path(argv[1]); std::filesystem::create_directories(fixtureRoot);
    int failed{};
    for (const auto& test : {std::pair{"input method rollback uses cached image",InputMethodSaveFailureRollsBackCachedImageWithoutOldFile},
                            std::pair{"input method retry is delayed but recovers",InputMethodTransientFailureRetriesAfterBackoff},
                            std::pair{"inactive input method saves without touching state control",InactiveInputMethodPolicyPersistsWithoutSwitchingOrChangingStateControl},
                            std::pair{"active input method comes from Shell data model",InputMethodActiveFlagUsesShellInsteadOfCallerThread},
                            std::pair{"single system target excludes missing peer", SingleSystemToggleIgnoresUnavailableSavedPeer},
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
                            std::pair{"privacy preference works without active indicator", PrivacyPreferenceDoesNotRequireActiveIndicator},
                            std::pair{"input method and language remain independent", InputMethodAndLanguageAreIndependent},
                            std::pair{"canceled advanced native behavior", CanceledAdvancedSettingsCannotApplyNativeBehavior},
                            std::pair{"legacy settings keep appearance and manual visibility", LoadedLegacyPoliciesKeepCustomAppearanceAndManualVisibility}}) {
        try { test.second(); std::printf("PASS %s\n", test.first); }
        catch (const std::exception& error) { ++failed; std::fprintf(stderr, "FAIL %s: %s\n", test.first, error.what()); }
    }
    return failed ? 1 : 0;
}
