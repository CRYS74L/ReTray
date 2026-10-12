#include "../native/shell/shared.h"
#include <cstdio>
#include <stdexcept>
tray::SnapshotResult nativeFixture;
namespace tray {
SnapshotResult RegistrationTestSnapshot(const SymbolAdapter&, uint32_t, uint64_t, bool = true) {
    return nativeFixture;
}
}
BOOL WINAPI RegistrationTestNotifyIcon(DWORD, PNOTIFYICONDATAW data) {
    nativeFixture.records[0].placement = data->dwState & NIS_HIDDEN
                                            ? tray::Placement::Hidden : tray::Placement::Overflow;
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
int wmain() {
    int failed{};
    for (const auto& test : {std::pair{"record replacement found by command renews registration", ReplacementFoundDuringCommandRenewsRegistration},
                            std::pair{"unreadable replacement cannot keep previous bitmap", ReplacementCannotRetainAnUnreadableOldBitmap},
                            std::pair{"unchanged registration keeps preview and serial", UnchangedRegistrationPreservesPreviewAndSerial}}) {
        try { test.second(); std::printf("PASS %s\n", test.first); }
        catch (const std::exception& error) { ++failed; std::fprintf(stderr, "FAIL %s: %s\n", test.first, error.what()); }
        entries.clear(); state = nullptr;
    }
    return failed ? 1 : 0;
}
