#include "../native/shell/shared.h"
#include <cstdio>
#include <stdexcept>
tray::SnapshotResult nativeFixture;
uint64_t fixtureSnapshotReads{};
namespace tray {
SnapshotResult RegistrationTestSnapshot(const SymbolAdapter&, uint32_t, uint64_t, bool = true) {
    ++fixtureSnapshotReads;
    return nativeFixture;
}
}
BOOL WINAPI RegistrationTestNotifyIcon(DWORD, PNOTIFYICONDATAW data) {
    if (data->uFlags & NIF_STATE)
        nativeFixture.records[0].placement = data->dwState & NIS_HIDDEN
                                                ? tray::Placement::Hidden : tray::Placement::Overflow;
    if (data->uFlags & NIF_TIP) nativeFixture.records[0].tip = data->szTip;
    return TRUE;
}
// Execute real bridge handlers; substitute only the external native inventory/notify boundary.
#define ReadSnapshot RegistrationTestSnapshot
#define Shell_NotifyIconW RegistrationTestNotifyIcon
#include "../native/shell/bridge_entry.cpp"
#undef Shell_NotifyIconW
#undef ReadSnapshot
#define CHECK(x) do { if (!(x)) throw std::runtime_error(#x); } while (0)
struct TrackedBitmap : IUnknown {
    unsigned released{};
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID, void**) override { return E_NOINTERFACE; }
    ULONG STDMETHODCALLTYPE AddRef() override { return 1; }
    ULONG STDMETHODCALLTYPE Release() override { ++released; return 0; }
} oldBitmap;
std::unique_ptr<BridgeState> testState;
std::string ResetRegistrationFixture(bool changedAddress) {
    entries.clear(); oldBitmap.released = 0;
    testState = std::make_unique<BridgeState>(); state = testState.get(); state->epoch = 1;
    moduleBase = 0; adapter = {}; nextRegistration = 1;
    auto entry = std::make_unique<Entry>(); entry->registration = 1;
    entry->record.identity = {1, 11, 100, 1000, 11, false, {}};
    entry->record.recordAddress = 0x20000; entry->record.placement = Placement::Overflow;
    entry->original = &oldBitmap; entry->customPainted = true;
    entry->originalPreview.width = entry->originalPreview.height = 1;
    entry->originalPreview.pixels = {0xff1955e9};
    auto record = entry->record;
    if (changedAddress) record.recordAddress = 0x21000;
    nativeFixture = {}; nativeFixture.complete = true; nativeFixture.records = {record};
    fixtureSnapshotReads = 0;
    auto key = IdentityKey(record.identity); entries.emplace(key, std::move(entry));
    return key;
}
void ReplacementFoundDuringCommandRenewsRegistration() {
    auto key = ResetRegistrationFixture(true);
    state->command.operation = BridgeOperation::Visibility;
    state->command.identity = nativeFixture.records[0].identity;
    state->command.visibility = 1; ExecuteCommand();
    CHECK(!state->lastError); CHECK(state->count == 1);
    CHECK(state->icons[0].registration == 2); CHECK(state->icons[0].placement == 2);
    CHECK(entries.at(key)->originalPreview.pixels.empty());
    CHECK(!entries.at(key)->original); CHECK(!entries.at(key)->customPainted);
    CHECK(oldBitmap.released == 1); entries.clear(); CHECK(oldBitmap.released == 1);
}
void ReplacementCannotRetainAnUnreadableOldBitmap() {
    auto key = ResetRegistrationFixture(true); Publish(false);
    CHECK(state->icons[0].registration == 2);
    CHECK(entries.at(key)->originalPreview.pixels.empty());
    CHECK(!entries.at(key)->original); CHECK(oldBitmap.released == 1);
    entries.clear(); CHECK(oldBitmap.released == 1);
}
void UnchangedRegistrationPreservesPreviewAndSerial() {
    auto key = ResetRegistrationFixture(false); Publish(false);
    CHECK(state->icons[0].registration == 1);
    CHECK(entries.at(key)->originalPreview.pixels[0] == 0xff1955e9);
    CHECK(oldBitmap.released == 0); entries.clear(); CHECK(oldBitmap.released == 1);
}
void SourceVisibilityIsIndependentOfAssistantVisibility() {
    auto key = ResetRegistrationFixture(false);
    auto& entry = *entries.at(key); entry.originalHidden = true;
    nativeFixture.records[0].placement = Placement::Hidden; Publish(false);
    CHECK(state->icons[0].sourceHidden); CHECK(!state->icons[0].managed);
    entry.originalHidden = false; entry.visibility = 1; Publish(false);
    CHECK(!state->icons[0].sourceHidden); CHECK(state->icons[0].managed);
    CHECK(state->icons[0].placement == 2);
}
void CompletePolicyPublishesOnceAndMatchesLegacyState() {
    auto image = CopyIcon(LoadIconW(nullptr, IDI_APPLICATION)); CHECK(image);
    auto run = [&](bool combined) {
        auto key = ResetRegistrationFixture(false);
        entries.at(key)->customPainted = false;
        auto send = [&](BridgeOperation operation) {
            state->command = {}; state->command.operation = operation;
            state->command.identity = nativeFixture.records[0].identity;
            state->command.replacementIcon = reinterpret_cast<uint64_t>(image);
            state->command.visibility = 1; state->command.index = 0;
            wcscpy_s(state->command.tip, L"policy preview");
            state->command.clickEvent = PolicyReplaceImage | PolicyTip | PolicyVisibility | PolicyBehavior;
            ExecuteCommand(); CHECK(!state->lastError);
        };
        if (combined) send(BridgeOperation::Policy);
        else for (auto operation : {BridgeOperation::Apply, BridgeOperation::Behavior,
                                    BridgeOperation::Tip, BridgeOperation::Visibility}) send(operation);
        const auto& entry = *entries.at(key);
        CHECK(entry.custom); CHECK(entry.tipChanged && entry.customTip == L"policy preview");
        CHECK(entry.visibility == 1 && !entry.swapButtons);
        CHECK(state->count == 1 && state->icons[0].placement == static_cast<int>(Placement::Hidden));
        CHECK(state->icons[0].custom && state->icons[0].managed);
        auto reads = fixtureSnapshotReads;
        CHECK(reads == (combined ? 2 : 8));
        CHECK(state->commandsTotal == (combined ? 1 : 4));
        CHECK(state->publications == (combined ? 1 : 4));
        entries.clear();
        return reads;
    };
    auto before = run(false), after = run(true);
    DestroyIcon(image);
    std::printf("METRIC complete policy full-directory scans %llu -> %llu; commands/publications 4 -> 1\n",
                static_cast<unsigned long long>(before), static_cast<unsigned long long>(after));
}
void CombinedPolicyRejectsChangedIdentityAndInvalidFlags() {
    auto key = ResetRegistrationFixture(false);
    state->command.operation = BridgeOperation::Policy;
    state->command.identity = nativeFixture.records[0].identity;
    state->command.identity.ownerCreatedAt++;
    state->command.clickEvent = PolicyVisibility; state->command.visibility = 1;
    ExecuteCommand(); CHECK(state->lastError == ERROR_NOT_FOUND);
    CHECK(entries.at(key)->visibility == -1);
    fixtureSnapshotReads = 0;
    state->command.identity = nativeFixture.records[0].identity;
    state->command.clickEvent = PolicyReplaceImage | PolicyClearImage;
    ExecuteCommand(); CHECK(state->lastError == ERROR_INVALID_PARAMETER);
    CHECK(fixtureSnapshotReads == 0 && entries.at(key)->visibility == -1);
    entries.clear();
}
void CombinedPolicyRenewsRegistrationBeforeMutation() {
    auto key = ResetRegistrationFixture(true);
    state->command.operation = BridgeOperation::Policy;
    state->command.identity = nativeFixture.records[0].identity;
    state->command.clickEvent = PolicyVisibility | PolicyBehavior;
    state->command.index = 0; state->command.visibility = 1;
    ExecuteCommand(); CHECK(!state->lastError);
    CHECK(state->icons[0].registration == 2 && state->icons[0].placement == 2);
    CHECK(entries.at(key)->originalPreview.pixels.empty() && !entries.at(key)->original);
    CHECK(oldBitmap.released == 1); entries.clear();
}
void FreshOriginalInputPolicyDoesNotDiscoverTheXamlTree() {
    auto key = ResetRegistrationFixture(false);
    CHECK(!HasInputMethodLogos());
    state->command.operation = BridgeOperation::InputMethodLogo;
    state->command.index = 0;
    wcscpy_s(state->command.inputProfileKey, L"isolated-test-profile");
    // No XAML root exists in this process: the old discovery path fails here.
    ExecuteCommand(); CHECK(state->lastError == ERROR_SUCCESS);
    CHECK(state->commandsTotal == 1 && state->snapshotReads == 0);
    entries.clear();
}
int wmain() {
    int failed{};
    for (const auto& test : {std::pair{"record replacement found by command renews registration", ReplacementFoundDuringCommandRenewsRegistration},
                            std::pair{"complete policy applies once with the legacy final state", CompletePolicyPublishesOnceAndMatchesLegacyState},
                            std::pair{"combined policy rejects stale identities and malformed flags", CombinedPolicyRejectsChangedIdentityAndInvalidFlags},
                            std::pair{"combined policy renews replaced native registrations", CombinedPolicyRenewsRegistrationBeforeMutation},
                            std::pair{"fresh original input policy skips unnecessary XAML discovery", FreshOriginalInputPolicyDoesNotDiscoverTheXamlTree},
                            std::pair{"source-hidden metadata differs from assistant hide", SourceVisibilityIsIndependentOfAssistantVisibility},
                            std::pair{"unreadable replacement cannot keep previous bitmap", ReplacementCannotRetainAnUnreadableOldBitmap},
                            std::pair{"unchanged registration keeps preview and serial", UnchangedRegistrationPreservesPreviewAndSerial}}) {
        try { test.second(); std::printf("PASS %s\n", test.first); }
        catch (const std::exception& error) { ++failed; std::fprintf(stderr, "FAIL %s: %s\n", test.first, error.what()); }
        entries.clear(); state = nullptr;
    }
    return failed ? 1 : 0;
}
