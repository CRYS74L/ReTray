#include "../native/shell/shared.h"
#include <cstdio>
#include <stdexcept>
tray::SnapshotResult nativeFixture;
uint64_t fixtureSnapshotReads{};
uint64_t fixtureTargetChecks{};
std::vector<uint32_t> invalidFixtureUids;
tray::BridgeState* cancellationTestState{};
bool cancelOnMutation{}, prematureBatchAck{};
HICON releaseOnMutation{};
bool reenterOnMutation{};
void ReenterCommandForMailboxTest();
namespace tray {
SnapshotResult RegistrationTestSnapshot(const SymbolAdapter&, uint32_t, uint64_t, bool = true) {
    ++fixtureSnapshotReads;
    return nativeFixture;
}
uint32_t RegistrationTestValidateNativeRecord(const SymbolAdapter&, uint64_t, const NativeIconRecord& record) {
    ++fixtureTargetChecks;
    if (std::find(invalidFixtureUids.begin(), invalidFixtureUids.end(), record.identity.uid) != invalidFixtureUids.end())
        return ERROR_NOT_FOUND;
    return std::any_of(nativeFixture.records.begin(), nativeFixture.records.end(), [&](const auto& current) {
        return SameIcon(current.identity, record.identity) && current.recordAddress == record.recordAddress;
    }) ? ERROR_SUCCESS : ERROR_NOT_FOUND;
}
}
BOOL WINAPI RegistrationTestNotifyIcon(DWORD, PNOTIFYICONDATAW data) {
    auto found = std::find_if(nativeFixture.records.begin(), nativeFixture.records.end(), [&](const auto& record) {
        return data->hWnd == reinterpret_cast<HWND>(record.identity.ownerHwnd) && data->uID == record.identity.uid;
    });
    if (found == nativeFixture.records.end()) return FALSE;
    if (data->uFlags & NIF_STATE)
        found->placement = data->dwState & NIS_HIDDEN
                                                ? tray::Placement::Hidden : tray::Placement::Overflow;
    if (data->uFlags & NIF_STATE) {
        if (reenterOnMutation) { reenterOnMutation = false; ReenterCommandForMailboxTest(); }
        if (cancellationTestState && cancellationTestState->requestSequence &&
            cancellationTestState->replySequence == cancellationTestState->requestSequence) prematureBatchAck = true;
        if (cancelOnMutation && cancellationTestState) {
            InterlockedExchange(&cancellationTestState->cancelledSequence, cancellationTestState->requestSequence);
            cancelOnMutation = false;
        }
        if (releaseOnMutation) { DestroyIcon(releaseOnMutation); releaseOnMutation = nullptr; }
    }
    if (data->uFlags & NIF_TIP) found->tip = data->szTip;
    return TRUE;
}
// Execute real bridge handlers; substitute only the external native inventory/notify boundary.
#define ReadSnapshot RegistrationTestSnapshot
#define ValidateNativeRecord RegistrationTestValidateNativeRecord
#define Shell_NotifyIconW RegistrationTestNotifyIcon
#include "../native/shell/bridge_entry.cpp"
#undef Shell_NotifyIconW
#undef ReadSnapshot
#undef ValidateNativeRecord
void ReenterCommandForMailboxTest() {
    state->requestSequence = 2; state->replySequence = 2; state->acceptedSequence = 2;
    state->batchReplyCount = 99; state->batchErrors[0] = 444; state->lastError = 555;
    ExecuteCommand(); // Native COM/Win32 calls may pump another command message.
}
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
    cancellationTestState = state; cancelOnMutation = prematureBatchAck = reenterOnMutation = false;
    if (releaseOnMutation) { DestroyIcon(releaseOnMutation); releaseOnMutation = nullptr; }
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
    fixtureSnapshotReads = fixtureTargetChecks = 0; invalidFixtureUids.clear();
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
void PrepareBatchFixture(unsigned count = 32) {
    ResetRegistrationFixture(false); entries.clear();
    const auto prototype = nativeFixture.records[0]; nativeFixture.records.clear();
    for (unsigned index = 0; index < count; ++index) {
        auto record = prototype; record.identity.uid += index; record.recordAddress += index * 0x1000;
        nativeFixture.records.push_back(record);
        auto entry = std::make_unique<Entry>(); entry->record = record; entry->registration = index + 1;
        entries.emplace(IdentityKey(record.identity), std::move(entry));
    }
    state->batchCount = static_cast<uint32_t>(nativeFixture.records.size());
    for (size_t index = 0; index < nativeFixture.records.size(); ++index) {
        auto& command = state->batch[index]; command = {}; command.operation = BridgeOperation::Policy;
        command.identity = nativeFixture.records[index].identity; command.clickEvent = PolicyVisibility | PolicyBehavior;
        command.index = 0; command.visibility = 1;
    }
    state->command = {}; state->command.operation = BridgeOperation::PolicyBatch;
}
void StartupRoundUsesOneIdentitySnapshotAndPublication() {
    PrepareBatchFixture();
    state->command.operation = BridgeOperation::PolicyBatch; ExecuteCommand();
    CHECK(!state->lastError && state->batchReplyCount == 32);
    for (size_t index = 0; index < 32; ++index) CHECK(!state->batchErrors[index]);
    std::printf("METRIC startup round icons=32 directory-scans=%llu publications=%llu commands=%llu\n",
                static_cast<unsigned long long>(fixtureSnapshotReads),
                static_cast<unsigned long long>(state->publications),
                static_cast<unsigned long long>(state->commandsTotal));
    CHECK(fixtureSnapshotReads == 2);
    CHECK(state->publications == 1);
    CHECK(state->commandsTotal == 1);
    CHECK(fixtureTargetChecks == 32);
}
void BatchKeepsItemFailuresIndependentAndRevalidatesEveryTarget() {
    PrepareBatchFixture(8);
    invalidFixtureUids.push_back(nativeFixture.records[1].identity.uid);
    state->batch[2].clickEvent |= PolicyReplaceImage; // Invalid/null HICON must fail safely.
    state->batch[3].index = -1; // Invalid policy flags/parameters.
    state->batch[4] = state->batch[0]; // Duplicate identity is rejected.
    ++state->batch[5].identity.ownerCreatedAt; // Recycled process generation.
    ExecuteCommand();
    CHECK(!state->lastError && state->batchReplyCount == 8);
    CHECK(!state->batchErrors[0] && state->batchErrors[1] == ERROR_NOT_FOUND);
    CHECK(state->batchErrors[2] == ERROR_INVALID_HANDLE && state->batchErrors[3] == ERROR_INVALID_PARAMETER);
    CHECK(state->batchErrors[4] == ERROR_INVALID_PARAMETER && state->batchErrors[5] == ERROR_NOT_FOUND);
    CHECK(!state->batchErrors[6] && !state->batchErrors[7]);
    CHECK(nativeFixture.records[0].placement == Placement::Hidden);
    CHECK(nativeFixture.records[1].placement == Placement::Overflow);
    CHECK(nativeFixture.records[7].placement == Placement::Hidden);
    CHECK(fixtureSnapshotReads == 2 && state->publications == 1);
}
void IncompleteOrOversizedBatchCannotMutateAnyIcon() {
    PrepareBatchFixture(3); nativeFixture.complete = false;
    ExecuteCommand(); CHECK(!state->lastError && state->batchReplyCount == 3);
    for (unsigned index = 0; index < 3; ++index) {
        CHECK(state->batchErrors[index] == ERROR_RETRY);
        CHECK(nativeFixture.records[index].placement == Placement::Overflow);
    }
    CHECK(fixtureSnapshotReads == 1 && fixtureTargetChecks == 0 && state->publications == 0);
    fixtureSnapshotReads = 0; state->batchCount = static_cast<uint32_t>(MaxBridgeIcons + 1);
    ExecuteCommand(); CHECK(state->lastError == ERROR_INVALID_PARAMETER && !state->batchReplyCount);
    CHECK(!fixtureSnapshotReads);
}
void BatchRenewsRegistrationsAndPreservesDynamicOriginalFrames() {
    auto key = ResetRegistrationFixture(true);
    state->batchCount = 1; state->batch[0] = {};
    state->batch[0].operation = BridgeOperation::Policy;
    state->batch[0].identity = nativeFixture.records[0].identity;
    state->batch[0].clickEvent = PolicyVisibility; state->batch[0].visibility = 1;
    state->command.operation = BridgeOperation::PolicyBatch; ExecuteCommand();
    CHECK(!state->batchErrors[0] && entries.at(key)->registration == 2);
    CHECK(!entries.at(key)->original && oldBitmap.released == 1);
    PrepareBatchFixture(2);
    auto& entry = *entries.at(IdentityKey(nativeFixture.records[0].identity));
    entry.source.origin = FrameOrigin::SourceLive; entry.source.readState = FrameReadState::Ready;
    entry.source.width = entry.source.height = 1; entry.source.pixels = {0xff123456};
    entry.source.fingerprint = 88; entry.originalPreview = entry.source;
    auto image = CopyIcon(LoadIconW(nullptr, IDI_APPLICATION)); CHECK(image);
    state->batch[0].clickEvent |= PolicyReplaceImage;
    state->batch[0].replacementIcon = reinterpret_cast<uint64_t>(image);
    state->batch[0].suppliedBaseline = true; state->batch[0].baselineFingerprint = 77;
    state->batch[0].baselineWidth = state->batch[0].baselineHeight = 1;
    ExecuteCommand(); DestroyIcon(image);
    CHECK(!state->batchErrors[0] && state->icons[0].dynamic);
    CHECK(entry.source.pixels[0] == 0xff123456 && entry.originalPreview.pixels[0] == 0xff123456);
    CHECK(!entry.customPainted && entry.custom);
}
void BatchCancellationCannotAcknowledgeBeforeCompletionOrUseLateRequests() {
    PrepareBatchFixture(3); state->requestSequence = 1; state->cancelledSequence = 1;
    ExecuteCommand(); CHECK(state->lastError == ERROR_OPERATION_ABORTED);
    CHECK(!fixtureSnapshotReads && state->replySequence == 1);
    state->requestSequence = 2; state->cancelledSequence = 0; cancelOnMutation = true;
    ExecuteCommand(); CHECK(!state->lastError && state->batchReplyCount == 3);
    CHECK(!state->batchErrors[0]);
    CHECK(state->batchErrors[1] == ERROR_OPERATION_ABORTED && state->batchErrors[2] == ERROR_OPERATION_ABORTED);
    CHECK(nativeFixture.records[0].placement == Placement::Hidden);
    CHECK(nativeFixture.records[1].placement == Placement::Overflow);
    CHECK(!prematureBatchAck && state->replySequence == 2);
}
void BatchOwnsIconCopiesBeforeTheCallerCanReleaseThem() {
    PrepareBatchFixture(2); state->requestSequence = 1;
    releaseOnMutation = CopyIcon(LoadIconW(nullptr, IDI_APPLICATION)); CHECK(releaseOnMutation);
    state->batch[1].clickEvent |= PolicyReplaceImage;
    state->batch[1].replacementIcon = reinterpret_cast<uint64_t>(releaseOnMutation);
    ExecuteCommand(); CHECK(!state->batchErrors[0] && !state->batchErrors[1]);
    CHECK(!releaseOnMutation && state->icons[1].custom);
    CHECK(!prematureBatchAck && state->replySequence == 1);
}
void ReentrantCommandsCannotExecuteOrOverwriteAnotherSequence() {
    PrepareBatchFixture(3); state->requestSequence = 1; reenterOnMutation = true;
    ExecuteCommand();
    CHECK(state->requestSequence == 2 && state->replySequence == 2 && state->acceptedSequence == 2);
    CHECK(state->batchReplyCount == 99 && state->batchErrors[0] == 444 && state->lastError == 555);
    CHECK(state->commandsTotal == 1); // Nested message did not execute.
    CHECK(nativeFixture.records[1].placement == Placement::Overflow);
}
int wmain() {
    int failed{};
    for (const auto& test : {std::pair{"record replacement found by command renews registration", ReplacementFoundDuringCommandRenewsRegistration},
                            std::pair{"startup policy round shares one identity read and publication", StartupRoundUsesOneIdentitySnapshotAndPublication},
                            std::pair{"batch isolates failures and validates each live target", BatchKeepsItemFailuresIndependentAndRevalidatesEveryTarget},
                            std::pair{"incomplete and oversized batches cannot mutate icons", IncompleteOrOversizedBatchCannotMutateAnyIcon},
                            std::pair{"batch renews registrations without replacing dynamic originals", BatchRenewsRegistrationsAndPreservesDynamicOriginalFrames},
                            std::pair{"canceled batches never publish an early or foreign acknowledgment", BatchCancellationCannotAcknowledgeBeforeCompletionOrUseLateRequests},
                            std::pair{"batch owns icon copies before caller handles are released", BatchOwnsIconCopiesBeforeTheCallerCanReleaseThem},
                            std::pair{"reentrant commands cannot execute or overwrite another sequence", ReentrantCommandsCannotExecuteOrOverwriteAnotherSequence},
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
