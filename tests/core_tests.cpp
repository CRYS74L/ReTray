#include "../native/core/notify_patch.h"

#include "../native/core/rule_engine.h"
#include "../native/core/types.h"
#include "../native/platform/bitmap_bytes.h"
#include "../native/platform/software_bitmap.h"
#include <cstdio>
#include <cstdlib>
#include <functional>
#include <stdexcept>
#include <winrt/windows.foundation.h>
#include <winrt/windows.graphics.imaging.h>

using namespace tray;
static void FillBitmap(const winrt::Windows::Graphics::Imaging::SoftwareBitmap& bitmap,
                       uint32_t color) {
    using namespace winrt::Windows::Graphics::Imaging;
    auto locked = bitmap.LockBuffer(BitmapBufferAccessMode::Write);
    auto reference = locked.CreateReference();
    auto access = reference.as<BitmapBytes>();
    uint8_t* bytes{};
    uint32_t capacity{};
    winrt::check_hresult(access->GetBuffer(&bytes, &capacity));
    auto plane = locked.GetPlaneDescription(0);
    for (int y = 0; y < bitmap.PixelHeight(); ++y)
        for (int x = 0; x < bitmap.PixelWidth(); ++x)
            reinterpret_cast<uint32_t*>(bytes + plane.StartIndex + y * plane.Stride)[x] = color;
}
#define REQUIRE(x)                                                                                 \
    do {                                                                                           \
        if (!(x))                                                                                  \
            throw std::runtime_error(#x);                                                          \
    } while (0)
static HICON ColoredIcon(bool visible) {
    BITMAPV5HEADER hdr{};
    hdr.bV5Size = sizeof(hdr);
    hdr.bV5Width = 16;
    hdr.bV5Height = -16;
    hdr.bV5Planes = 1;
    hdr.bV5BitCount = 32;
    hdr.bV5Compression = BI_BITFIELDS;
    hdr.bV5RedMask = 0x00ff0000;
    hdr.bV5GreenMask = 0x0000ff00;
    hdr.bV5BlueMask = 0x000000ff;
    hdr.bV5AlphaMask = 0xff000000;
    void* bits{};
    HDC dc = GetDC(nullptr);
    auto color = CreateDIBSection(dc, reinterpret_cast<BITMAPINFO*>(&hdr), DIB_RGB_COLORS, &bits,
                                  nullptr, 0);
    ReleaseDC(nullptr, dc);
    auto values = static_cast<uint32_t*>(bits);
    for (int i = 0; i < 256; ++i)
        values[i] = visible ? 0xff258954 : 0;
    unsigned char maskBytes[32];
    memset(maskBytes, visible ? 0 : 0xff, sizeof(maskBytes));
    auto mask = CreateBitmap(16, 16, 1, 1, maskBytes);
    ICONINFO ii{};
    ii.fIcon = TRUE;
    ii.hbmColor = color;
    ii.hbmMask = mask;
    auto icon = CreateIconIndirect(&ii);
    DeleteObject(color);
    DeleteObject(mask);
    return icon;
}
int main(int argc, char** argv) {
    winrt::init_apartment();
    std::string filter = argc > 1 ? argv[1] : "";
    int passed = 0, failed = 0;
    auto test = [&](const char* name, std::function<void()> body) {
        if (!filter.empty() && std::string(name).find(filter) == std::string::npos)
            return;
        try {
            body();
            ++passed;
            printf("PASS %s\n", name);
        } catch (const std::exception& e) {
            ++failed;
            printf("FAIL %s: %s\n", name, e.what());
        } catch (const winrt::hresult_error& e) {
            ++failed;
            printf("ERROR %s: HRESULT=%08X\n", name, static_cast<unsigned>(e.code().value));
        }
    };
    test("NativeSnapshot.ValidUserRange", [] { REQUIRE(ValidReadRange(0x10000, 32)); });
    test("NativeSnapshot.RejectNullAndOverflow", [] {
        REQUIRE(!ValidReadRange(0, 8));
        REQUIRE(!ValidReadRange(0x7fffffffffff, 8));
        REQUIRE(!ValidReadRange(0x10000, 65537));
    });
    test("NativeSnapshot.AcceptBoundedVector",
         [] { REQUIRE(ValidVectorBounds(0x10000, 0x10018, 4)); });
    test("NativeSnapshot.RejectMalformedVector", [] {
        REQUIRE(!ValidVectorBounds(0x10000, 0xffff, 4));
        REQUIRE(!ValidVectorBounds(0x10000, 0x10011, 4));
        REQUIRE(!ValidVectorBounds(0x10000, 0x10028, 4));
    });
    test("NativeSnapshot.PdbIdentityMustMatch", [] {
        ImageIdentity a{};
        a.pdb.Data1 = 123;
        a.age = 1;
        a.imageSize = 8192;
        ImageIdentity b = a;
        REQUIRE(SameImage(a, b));
        b.age = 2;
        REQUIRE(!SameImage(a, b));
        b = a;
        b.pdb.Data1 = 124;
        REQUIRE(!SameImage(a, b));
        b = a;
        b.imageSize = 4096;
        REQUIRE(!SameImage(a, b));
    });
    test("Identity.GuidAndLegacyCannotMerge", [] {
        IconIdentity a{};
        a.ownerHwnd = 0x101;
        a.uid = 3;
        a.shellEpoch = 1;
        a.ownerPid = 42;
        a.ownerCreatedAt = 99;
        auto b = a;
        b.hasGuid = true;
        b.guid.Data1 = 55;
        REQUIRE(!SameIcon(a, b));
        REQUIRE(IdentityKey(a) != IdentityKey(b));
    });
    test("Identity.OwnerGenerationCannotReuse", [] {
        IconIdentity a{};
        a.ownerHwnd = 0x101;
        a.uid = 3;
        a.shellEpoch = 1;
        a.ownerPid = 42;
        a.ownerCreatedAt = 99;
        auto b = a;
        REQUIRE(SameIcon(a, b));
        b.ownerCreatedAt = 100;
        REQUIRE(!SameIcon(a, b));
        b = a;
        b.shellEpoch = 2;
        REQUIRE(!SameIcon(a, b));
    });
    test("Frame.CachedIconIsNotLive", [] {
        auto icon = ColoredIcon(true);
        auto f = RasterizeIcon(icon, FrameOrigin::CachedOriginal);
        DestroyIcon(icon);
        REQUIRE(f.outcome.success);
        REQUIRE(!f.isLive);
        REQUIRE(f.frame.width == 16);
        REQUIRE(!f.frame.transparent);
    });
    test("Frame.SourcePixelContentIsLive", [] {
        auto a = ColoredIcon(true), b = ColoredIcon(true);
        auto fa = RasterizeIcon(a, FrameOrigin::SourceLive),
             fb = RasterizeIcon(b, FrameOrigin::SourceLive);
        DestroyIcon(a);
        DestroyIcon(b);
        REQUIRE(fa.outcome.success);
        REQUIRE(fa.isLive);
        REQUIRE(fa.frame.fingerprint == fb.frame.fingerprint);
        REQUIRE(fa.frame.pixels.size() == 256);
    });
    test("Frame.TransparentOffFrameStaysOff", [] {
        auto icon = ColoredIcon(false);
        auto f = RasterizeIcon(icon, FrameOrigin::SourceLive);
        DestroyIcon(icon);
        REQUIRE(f.outcome.success);
        REQUIRE(f.frame.transparent);
    });
    test("Frame.InvalidWinrtPointerIsRejected", [] {
        auto f =
            RasterizeIcon(reinterpret_cast<HICON>(0x123456789000ULL), FrameOrigin::CachedOriginal);
        REQUIRE(!f.outcome.success);
        REQUIRE(!f.isLive);
    });
    test("CurrentFrame.SoftwareBitmapPixelsAreCurrent", [] {
        using namespace winrt::Windows::Graphics::Imaging;
        SoftwareBitmap bitmap(BitmapPixelFormat::Bgra8, 16, 16, BitmapAlphaMode::Premultiplied);
        FillBitmap(bitmap, 0xff258954);
        auto a = RasterizeSoftwareBitmap(winrt::get_abi(bitmap));
        REQUIRE(a.outcome.success);
        REQUIRE(a.isLive);
        REQUIRE(a.frame.pixels[0] == 0xff258954);
        FillBitmap(bitmap, 0xff1955e9);
        auto b = RasterizeSoftwareBitmap(winrt::get_abi(bitmap));
        REQUIRE(b.outcome.success);
        REQUIRE(b.frame.pixels[0] == 0xff1955e9);
        REQUIRE(a.frame.fingerprint != b.frame.fingerprint);
    });
    test("CurrentFrame.SoftwareBitmapOffFramePreserved", [] {
        using namespace winrt::Windows::Graphics::Imaging;
        SoftwareBitmap bitmap(BitmapPixelFormat::Bgra8, 16, 16, BitmapAlphaMode::Premultiplied);
        FillBitmap(bitmap, 0);
        auto frame = RasterizeSoftwareBitmap(winrt::get_abi(bitmap));
        REQUIRE(frame.outcome.success);
        REQUIRE(frame.frame.transparent);
    });
    test("Protocol.ModifyIconPreservesOwnerCallbackVersion", [] {
        NativeIconRecord r{};
        r.callbackMessage = 0x8001;
        r.notifyVersion = 4;
        r.tip = L"original";
        r.identity.hasGuid = true;
        r.identity.guid.Data1 = 42;
        IconPatch p{};
        p.operation = NIM_MODIFY;
        p.suppliedFlags = NIF_ICON;
        p.frame.fingerprint = 99;
        p.notifyVersion = 5000;
        REQUIRE(ApplyPatch(r, p).success);
        REQUIRE(r.callbackMessage == 0x8001);
        REQUIRE(r.notifyVersion == 4);
        REQUIRE(r.tip == L"original");
        REQUIRE(r.identity.guid.Data1 == 42);
        REQUIRE(r.currentFrame.fingerprint == 99);
    });
    test("Protocol.StateMaskOnlyTouchesSelectedBits", [] {
        NativeIconRecord r{};
        r.sourceState = 0x10;
        IconPatch p{};
        p.operation = NIM_MODIFY;
        p.suppliedFlags = NIF_STATE;
        p.state = NIS_HIDDEN;
        p.stateMask = NIS_HIDDEN;
        REQUIRE(ApplyPatch(r, p).success);
        REQUIRE(r.sourceState == 0x11);
        p.state = 0;
        REQUIRE(ApplyPatch(r, p).success);
        REQUIRE(r.sourceState == 0x10);
    });
    test("Protocol.SetVersionOnlyOnVersionOperation", [] {
        NativeIconRecord r{};
        IconPatch p{};
        p.operation = NIM_SETVERSION;
        p.notifyVersion = 4;
        REQUIRE(ApplyPatch(r, p).success);
        REQUIRE(r.notifyVersion == 4);
        p.operation = NIM_MODIFY;
        p.suppliedFlags = NIF_TIP;
        p.notifyVersion = 10000;
        p.tip = L"updated";
        REQUIRE(ApplyPatch(r, p).success);
        REQUIRE(r.notifyVersion == 4);
        REQUIRE(r.tip == L"updated");
    });
    test("Protocol.Fixed32BitWirePreservesGuidAndModule", [] {
        std::vector<uint8_t> b(1484);
        auto d = [&](size_t n, uint32_t v) { memcpy(b.data() + n, &v, 4); };
        d(0, 0x34753423);
        d(4, NIM_MODIFY);
        d(8, 956);
        d(12, 0x12345);
        d(16, 42);
        d(20, NIF_GUID | NIF_ICON);
        d(28, 0x9876);
        d(0x3b0, 55);
        const wchar_t tip[] = L"test";
        memcpy(b.data() + 0x20, tip, sizeof(tip));
        const wchar_t path[] = L"C:\\Fake\\Example.exe";
        memcpy(b.data() + 0x3c4, path, sizeof(path));
        auto e = DecodeWire(b);
        REQUIRE(e.supported);
        REQUIRE(e.identity.ownerHwnd == 0x12345);
        REQUIRE(e.identity.uid == 42);
        REQUIRE(e.identity.hasGuid);
        REQUIRE(e.identity.guid.Data1 == 55);
        REQUIRE(e.iconHandle == 0x9876);
        REQUIRE(e.module == L"C:\\Fake\\Example.exe");
    });
    test("Protocol.TruncatedWireCannotBeDecoded", [] {
        std::vector<uint8_t> b(20);
        uint32_t magic = 0x34753423;
        memcpy(b.data(), &magic, 4);
        REQUIRE(!DecodeWire(b).supported);
    });
    test("Dynamic.StaticIdleCanBeCustom", [] {
        Frame f{};
        f.fingerprint = 123;
        f.width = 16;
        f.height = 16;
        FrameBaseline b{true, 123, 16, 16};
        REQUIRE(ChooseFrame(f, b, true) == FrameDecision::CustomIdle);
    });
    test("Dynamic.OffAndUnknownAnimationPreserved", [] {
        Frame f{};
        f.fingerprint = 456;
        f.width = 16;
        f.height = 16;
        FrameBaseline b{true, 123, 16, 16};
        REQUIRE(ChooseFrame(f, b, true) == FrameDecision::NativeDynamic);
        f.transparent = true;
        REQUIRE(ChooseFrame(f, b, true) == FrameDecision::NativeOff);
    });
    test("Dynamic.UncertainBaselineDoesNotFlatten", [] {
        Frame f{};
        FrameBaseline b{};
        REQUIRE(ChooseFrame(f, b, true) == FrameDecision::AwaitingBaseline);
    });
    test("Session.QueryAlwaysAcceptsAndStopsRecovery", [] {
        SessionMachine m{};
        m.state = SessionState::Ready;
        REQUIRE(HandleSessionQuery(m));
        REQUIRE(m.state == SessionState::SessionQuery);
        REQUIRE(!ShouldRecover(m));
    });
    test("Session.CancelledShutdownRecoversPriorState", [] {
        SessionMachine m{};
        m.state = SessionState::Ready;
        HandleSessionQuery(m);
        HandleSessionEnd(m, false);
        REQUIRE(m.state == SessionState::Ready);
        REQUIRE(ShouldRecover(m));
    });
    test("Session.UserStopAndEndNeverRecover", [] {
        SessionMachine m{};
        m.state = SessionState::UserStopping;
        REQUIRE(!ShouldRecover(m));
        HandleSessionQuery(m);
        HandleSessionEnd(m, false);
        REQUIRE(m.state == SessionState::UserStopping);
        m.state = SessionState::Ready;
        HandleSessionEnd(m, true);
        REQUIRE(m.state == SessionState::SessionEnding);
        REQUIRE(!ShouldRecover(m));
    });
    test("Catalog.PartialSnapshotCannotEraseLiveState", [] {
        IconCatalog c;
        SnapshotResult s;
        s.complete = true;
        s.records.resize(1);
        s.records[0].identity.ownerPid = 42;
        REQUIRE(MergeSnapshot(c, s, 100).added == 1);
        s.complete = false;
        s.records.clear();
        REQUIRE(!MergeSnapshot(c, s, 200).accepted);
        REQUIRE(c.items.size() == 1);
    });
    test("Catalog.DeleteRemovesRuntimeOnly", [] {
        IconCatalog c;
        SnapshotResult s;
        s.complete = true;
        s.records.resize(1);
        s.records[0].executable = L"C:\\Example.exe";
        auto selector = SelectorFor(s.records[0]);
        auto key = PersistentKey(selector);
        MergeSnapshot(c, s, 100);
        s.records.clear();
        REQUIRE(MergeSnapshot(c, s, 200).removed == 1);
        REQUIRE(c.items.empty());
        REQUIRE(PersistentKey(selector) == key);
    });
    test("Catalog.ThousandUpdatesKeepSelectionAndIdentity", [] {
        IconCatalog c;
        SnapshotResult s;
        s.complete = true;
        s.records.resize(2);
        auto& a = s.records[0];
        a.identity.ownerPid = 42;
        a.identity.ownerCreatedAt = 1;
        a.identity.ownerHwnd = 100;
        a.identity.uid = 7;
        auto& b = s.records[1];
        b = a;
        b.identity.hasGuid = true;
        b.identity.guid.Data1 = 55;
        MergeSnapshot(c, s, 1);
        c.selected = IdentityKey(b.identity);
        for (int n = 0; n < 1000; ++n) {
            a.currentFrame.fingerprint = n;
            REQUIRE(MergeSnapshot(c, s, n + 2).accepted);
            REQUIRE(c.items.size() == 2);
            REQUIRE(c.selected == IdentityKey(b.identity));
        }
        REQUIRE(c.items.at(IdentityKey(a.identity)).activity.lastFrameChange == 1001);
    });
    test("Rule.DistinguishesGuidFromLegacy", [] {
        NativeIconRecord r;
        r.executable = L"C:\\Example.exe";
        r.identity.uid = 7;
        auto legacy = SelectorFor(r);
        r.identity.hasGuid = true;
        r.identity.guid.Data1 = 55;
        auto guid = SelectorFor(r);
        REQUIRE(PersistentKey(legacy) != PersistentKey(guid));
        REQUIRE(!Matches(legacy, r));
        REQUIRE(Matches(guid, r));
    });
    test("Rule.PackageUpgradePreservesSavedGuidPolicy", [] {
        NativeIconRecord r;
        r.executable = L"C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.1007.2314.0_x64__2p2nqsd0c76g0\\app\\ChatGPT.exe";
        r.identity.hasGuid = true; r.identity.guid.Data1 = 123;
        IconRule saved; saved.selector = SelectorFor(r);
        saved.asset = L"C:\\Assets\\replacement.ico"; saved.visibility = 1; saved.tip = L"Saved hover";
        auto savedKey = PersistentKey(saved.selector);
        std::unordered_map<std::string, IconRule> rules{{savedKey, saved}};
        r.executable = L"C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.1007.9446.0_x64__2p2nqsd0c76g0\\app\\ChatGPT.exe";
        REQUIRE(Matches(saved.selector, r));
        for (auto result : {ResolveRule(rules, r), ResolveRule(rules, r, {r})}) {
            REQUIRE(result.rule && !result.ambiguous);
            REQUIRE(result.rule->asset == saved.asset && result.rule->tip == saved.tip);
            REQUIRE(result.rule->visibility == saved.visibility);
            REQUIRE(PersistentKey(result.rule->selector) == savedKey);
        }
        // Moving a Store installation or changing architecture keeps its app identity.
        r.executable = L"D:/WindowsApps/OpenAI.Codex_27.1.2.3_arm64__2p2nqsd0c76g0/app/CHATGPT.EXE";
        REQUIRE(Matches(saved.selector, r));
        r.identity.guid.Data1 = 124;
        REQUIRE(!Matches(saved.selector, r));
    });
    test("Rule.PackageUpgradeDoesNotMergeOtherApplicationsOrRoles", [] {
        NativeIconRecord r;
        r.executable = L"C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.1007.2314.0_x64__2p2nqsd0c76g0\\app\\ChatGPT.exe";
        r.identity.hasGuid = true; r.identity.guid.Data1 = 123;
        auto saved = SelectorFor(r);
        r.executable = L"C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.1007.2314.0_x64__2p2nqsd0c76g0" +
            std::wstring(1, L'\0') + L"garbage\\app\\ChatGPT.exe";
        REQUIRE(!Matches(saved, r));
        for (auto path : {
            L"C:\\Program Files\\WindowsApps\\Other.App_26.1007.9446.0_x64__2p2nqsd0c76g0\\app\\ChatGPT.exe",
            L"C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.1007.9446.0_x64__8wekyb3d8bbwe\\app\\ChatGPT.exe",
            L"C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.1007.9446.0_x64_extra_2p2nqsd0c76g0\\app\\ChatGPT.exe",
            L"C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.1007.9446.0_x64__2p2nqsd0c76g0\\helper\\ChatGPT.exe",
            L"C:\\Fake\\WindowsApps\\OpenAI.Codex_26.1007.9446.0_x64__2p2nqsd0c76g0\\app\\ChatGPT.exe",
            L"C:\\Program Files\\WindowsApps\\OpenAI.Codex_invalid_x64__2p2nqsd0c76g0\\app\\ChatGPT.exe",
            L"C:\\Program Files\\WindowsApps\\OpenAI.Codex_26.1007.9446.0_x64__2p2nqsd0c76g0\\..\\app\\ChatGPT.exe",
            L"C:\\Other\\ChatGPT.exe"}) {
            r.executable = path; REQUIRE(!Matches(saved, r));
        }
        // An ordinary version-looking directory is not a package identity.
        r.executable = L"C:\\Apps\\Example_1.0.0.0_x64__8wekyb3d8bbwe\\App.exe";
        saved = SelectorFor(r);
        r.executable = L"C:\\Apps\\Example_2.0.0.0_x64__8wekyb3d8bbwe\\App.exe";
        REQUIRE(!Matches(saved, r));
    });
    test("Rule.PackageUpgradeRetainsLegacyRoleSafetyAndConflicts", [] {
        NativeIconRecord r;
        r.executable = L"C:\\Program Files\\WindowsApps\\Example.App_1.0.0.0_x64__8wekyb3d8bbwe\\App.exe";
        r.identity = {1, 10, 100, 7, 7, false, {}};
        r.windowClass = L"TrayHost"; r.callbackMessage = WM_APP + 9;
        r.tip = L"Primary role"; r.cachedOriginal.fingerprint = 111;
        IconRule saved; saved.selector = SelectorFor(r, {r}); saved.asset = L"C:\\Assets\\one.ico";
        std::unordered_map<std::string, IconRule> rules{{PersistentKey(saved.selector), saved}};
        r.executable = L"C:\\Program Files\\WindowsApps\\Example.App_2.0.0.0_x64__8wekyb3d8bbwe\\App.exe";
        r.identity = {2, 20, 200, 8, 8, false, {}};
        auto resolved = ResolveRule(rules, r, {r});
        REQUIRE(resolved.rule && resolved.reassociated && !resolved.ambiguous && !resolved.provisional);
        auto secondary = r; secondary.identity.uid = 9; secondary.identity.ownerHwnd++;
        REQUIRE(ResolveRule(rules, r, {r, secondary}).ambiguous);
        // Independent saved policies from different versions must not silently overwrite.
        r.identity.hasGuid = true; r.identity.guid.Data1 = 123;
        saved.selector = SelectorFor(r); rules.clear(); rules[PersistentKey(saved.selector)] = saved;
        auto other = saved;
        other.selector.executable = L"C:\\Program Files\\WindowsApps\\Example.App_3.0.0.0_x64__8wekyb3d8bbwe\\App.exe";
        other.asset = L"C:\\Assets\\two.ico"; rules[PersistentKey(other.selector)] = other;
        REQUIRE(ResolveRule(rules, r, {r}).ambiguous);
    });
    test("Rule.PackagePathDerivedGuidSurvivesUpgradeAndMove", [] {
        NativeIconRecord r;
        r.executable = L"C:\\Program Files\\WindowsApps\\Example.App_1.0.0.0_x64__8wekyb3d8bbwe\\App.exe";
        r.identity.hasGuid = true;
        CLSIDFromString(L"{2F31B3F1-DEAC-9960-2888-D840F013C50C}", &r.identity.guid);
        IconRule saved; saved.selector = SelectorFor(r); saved.visibility = 1;
        saved.asset = L"C:\\Assets\\saved.ico";
        auto key = PersistentKey(saved.selector);
        std::unordered_map<std::string, IconRule> rules{{key, saved}};
        for (const auto& [path, guid] : {
            std::pair{L"C:\\Program Files\\WindowsApps\\Example.App_2.0.0.0_x64__8wekyb3d8bbwe\\App.exe",
                      L"{CCC6E893-8F93-4866-8E30-2EF39FE1ACAD}"},
            std::pair{L"D:\\WindowsApps\\Example.App_3.0.0.0_arm64__8wekyb3d8bbwe\\App.exe",
                      L"{997B8602-91D2-DB65-FDCD-F8D454531E73}"}}) {
            r.executable = path; CLSIDFromString(guid, &r.identity.guid);
            REQUIRE(Matches(saved.selector, r));
            REQUIRE(ResolveRule(rules, r).rule);
            auto result = ResolveRule(rules, r, {r});
            REQUIRE(result.rule && result.reassociated && !result.provisional && !result.ambiguous);
            REQUIRE(result.rule->visibility == 1 && result.rule->asset == saved.asset);
            REQUIRE(PersistentKey(result.rule->selector) == key);
        }
        // Newly saved original spelling works too; legacy normalized keys stay stable.
        auto original = saved;
        original.selector.executable = L"C:\\Program Files\\WindowsApps\\Example.App_1.0.0.0_x64__8wekyb3d8bbwe\\App.exe";
        REQUIRE(Matches(original.selector, r));
        REQUIRE(PersistentKey(original.selector) == key);
    });
    test("Rule.PackagePathGuidDuplicatesRequireIdenticalPolicies", [] {
        NativeIconRecord r;
        r.executable = L"C:\\Program Files\\WindowsApps\\Example.App_1.0.0.0_x64__8wekyb3d8bbwe\\App.exe";
        r.identity.hasGuid = true;
        CLSIDFromString(L"{2F31B3F1-DEAC-9960-2888-D840F013C50C}", &r.identity.guid);
        IconRule first; first.selector = SelectorFor(r); first.visibility = 1;
        r.executable = L"C:\\Program Files\\WindowsApps\\Example.App_2.0.0.0_x64__8wekyb3d8bbwe\\App.exe";
        CLSIDFromString(L"{CCC6E893-8F93-4866-8E30-2EF39FE1ACAD}", &r.identity.guid);
        auto second = first; second.selector = SelectorFor(r);
        std::unordered_map<std::string, IconRule> rules{
            {PersistentKey(first.selector), first}, {PersistentKey(second.selector), second}};
        auto selected = ResolveRule(rules, r, {r});
        REQUIRE(selected.rule && !selected.ambiguous && !ResolveRule(rules, r).ambiguous);
        auto key = PersistentKey(selected.rule->selector);
        // Selection does not depend on unordered_map iteration order.
        rules.rehash(71);
        REQUIRE(PersistentKey(ResolveRule(rules, r, {r}).rule->selector) == key);
        for (int field = 0; field < 4; ++field) {
            auto changed = second;
            if (field == 0) changed.visibility = 0;
            if (field == 1) changed.asset = L"C:\\Assets\\other.ico";
            if (field == 2) changed.tip = L"Different tip";
            if (field == 3) changed.hotkey = 123;
            rules[PersistentKey(second.selector)] = changed;
            REQUIRE(ResolveRule(rules, r, {r}).ambiguous);
            REQUIRE(ResolveRule(rules, r).ambiguous);
        }
        second.visibility = 1; second.enabled = false;
        rules[PersistentKey(second.selector)] = second;
        REQUIRE(ResolveRule(rules, r, {r}).ambiguous);
    });
    test("Rule.PackagePathGuidNeverBroadensUnrelatedGuidRoles", [] {
        NativeIconRecord r;
        r.executable = L"C:\\Program Files\\WindowsApps\\Example.App_1.0.0.0_x64__8wekyb3d8bbwe\\App.exe";
        r.identity.hasGuid = true;
        CLSIDFromString(L"{2F31B3F1-DEAC-9960-2888-D840F013C50C}", &r.identity.guid);
        auto saved = SelectorFor(r);
        r.executable = L"C:\\Program Files\\WindowsApps\\Example.App_2.0.0.0_x64__8wekyb3d8bbwe\\App.exe";
        // Named secondary H.NotifyIcon GUID, arbitrary GUID, and mismatched casing hash.
        for (auto guid : {L"{8725F072-41B7-A078-C398-9C9241E95139}",
                          L"{00000001-0000-0000-0000-000000000000}",
                          L"{03C1522B-E4FE-FB41-0878-84645AE4718D}"}) {
            CLSIDFromString(guid, &r.identity.guid);
            REQUIRE(!Matches(saved, r));
            IconRule rule; rule.selector = saved;
            REQUIRE(!ResolveRule({{PersistentKey(saved), rule}}, r, {r}).rule);
        }
        CLSIDFromString(L"{CCC6E893-8F93-4866-8E30-2EF39FE1ACAD}", &r.identity.guid);
        saved.guid.Data1++;
        REQUIRE(!Matches(saved, r));
        // Ordinary versioned folders never acquire package matching.
        saved.executable = L"C:\\Apps\\Example.App_1.0.0.0_x64__8wekyb3d8bbwe\\App.exe";
        REQUIRE(!Matches(saved, r));
    });
    test("Rule.MatchesRestartWithoutTooltipOrHandles", [] {
        NativeIconRecord r;
        r.executable = L"C:\\Example.exe";
        r.windowClass = L"App";
        r.identity.uid = 7;
        auto s = SelectorFor(r);
        r.tip = L"new message";
        r.identity.ownerCreatedAt = 999;
        r.identity.ownerHwnd = 456;
        REQUIRE(Matches(s, r));
        r.executable = L"c:\\EXAMPLE.EXE";
        r.windowClass = L"App_NewSession";
        REQUIRE(Matches(s, r));
        r.identity.uid = 8;
        REQUIRE(!Matches(s, r));
    });
    test("Rule.SelectorPreservesNativeUidAndCapturesGenericRole", [] {
        NativeIconRecord r;
        r.executable = L"C:\\Synthetic\\Example.exe";
        r.windowClass = L"CommonTrayHost";
        r.identity.uid = 42888048;
        auto saved = SelectorFor(r);
        r.identity.uid = 18967408;
        r.identity.ownerPid = 7654; r.identity.ownerHwnd = 8765;
        r.identity.ownerCreatedAt = 9876;
        REQUIRE(!Matches(saved, r));
        REQUIRE(PersistentKey(saved) != PersistentKey(SelectorFor(r)));
        REQUIRE(saved.matchUid && saved.legacy.ready && saved.legacy.windowClass == L"CommonTrayHost");
        r.executable = L"C:\\Other.exe";
        REQUIRE(!Matches(saved, r));
        r.executable = L"C:\\Synthetic\\Example.exe";
        r.identity.hasGuid = true; r.identity.guid.Data1 = 55;
        REQUIRE(!Matches(saved, r));
    });
    test("Rule.GenericChangedUidAcrossUnrelatedApplications", [] {
        for (auto executable : {L"C:\\AlphaAgent.exe", L"C:\\NotifyHost.exe", L"D:\\WidgetService.exe"}) {
            NativeIconRecord record;
            record.executable = executable; record.windowClass = L"GenericNotificationWindow";
            record.callbackMessage = WM_APP + 9; record.tip = L"Primary role";
            record.cachedOriginal.fingerprint = 111;
            record.identity = {1, 10, 100, 7, 7, false, {}};
            IconRule rule; rule.selector = SelectorFor(record); rule.visibility = 1;
            auto savedKey = PersistentKey(rule.selector);
            std::unordered_map<std::string, IconRule> rules{{savedKey, rule}};
            record.identity = {2, 20, 200, 8, 123456, false, {}};
            auto result = ResolveRule(rules, record, {record});
            REQUIRE(result.rule && !result.ambiguous && result.rule->visibility == 1);
            REQUIRE(PersistentKey(result.rule->selector) == savedKey);
        }
    });
    test("Rule.GenericRoleSeparatesMultipleLegacyIcons", [] {
        NativeIconRecord primary;
        primary.executable = L"C:\\SyntheticHost.exe"; primary.windowClass = L"SharedIconHost";
        primary.callbackMessage = WM_APP + 9; primary.tip = L"Primary role";
        primary.cachedOriginal.fingerprint = 111;
        primary.identity = {1, 10, 100, 7, 7, false, {}};
        IconRule rule; rule.selector = SelectorFor(primary); rule.visibility = 1;
        std::unordered_map<std::string, IconRule> rules{{PersistentKey(rule.selector), rule}};
        primary.identity.uid = 700; primary.identity.ownerCreatedAt = 20;
        auto secondary = primary; secondary.identity.uid = 800;
        secondary.tip = L"Secondary role";
        std::vector<NativeIconRecord> current{primary, secondary};
        REQUIRE(ResolveRule(rules, primary, current).rule);
        REQUIRE(!ResolveRule(rules, secondary, current).rule);
        secondary.tip = primary.tip; current = {primary, secondary};
        auto ambiguous = ResolveRule(rules, primary, current);
        REQUIRE(!ambiguous.rule && ambiguous.ambiguous);
    });
    test("Rule.GenericDifferentUidInstancesKeepOneLogicalPolicy", [] {
        NativeIconRecord first;
        first.executable = L"C:\\InstanceHost.exe"; first.windowClass = L"CommonHost";
        first.callbackMessage = WM_APP + 9; first.tip = L"Primary";
        first.cachedOriginal.fingerprint = 111;
        first.identity = {1, 10, 100, 7, 7, false, {}};
        IconRule rule; rule.selector = SelectorFor(first);
        auto key = PersistentKey(rule.selector);
        std::unordered_map<std::string, IconRule> rules{{key, rule}};
        auto second = first; second.identity = {1, 20, 200, 8, 99, false, {}};
        std::vector<NativeIconRecord> current{first, second};
        REQUIRE(ResolveRule(rules, first, current).rule);
        auto recreated = ResolveRule(rules, second, current);
        REQUIRE(recreated.rule && PersistentKey(recreated.rule->selector) == key);
        REQUIRE(rules.at(key).selector.uid == 7);
    });
    test("Rule.GenericDelayedExactUidCannotTransferToSecondary", [] {
        NativeIconRecord primary;
        primary.executable = L"C:\\LateRegistration.exe"; primary.windowClass = L"CommonHost";
        primary.callbackMessage = WM_APP + 9; primary.tip = L"Primary";
        primary.cachedOriginal.fingerprint = 111;
        primary.identity = {1, 10, 100, 7, 7, false, {}};
        IconRule rule; rule.selector = SelectorFor(primary);
        std::unordered_map<std::string, IconRule> rules{{PersistentKey(rule.selector), rule}};
        auto secondary = primary; secondary.identity.uid = 99;
        secondary.tip = L"Secondary";
        REQUIRE(!ResolveRule(rules, secondary, {secondary}).rule);
        std::vector<NativeIconRecord> current{primary, secondary};
        REQUIRE(ResolveRule(rules, primary, current).rule);
        REQUIRE(!ResolveRule(rules, secondary, current).rule);
    });
    test("Rule.GenericStrongRegistrationReservesRoleDuringDynamicChanges", [] {
        NativeIconRecord primary;
        primary.executable = L"C:\\DynamicRole.exe"; primary.windowClass = L"CommonHost";
        primary.callbackMessage = WM_APP + 9; primary.tip = L"Primary";
        primary.cachedOriginal.fingerprint = 111;
        primary.identity = {1, 10, 100, 7, 7, false, {}}; primary.registrationSerial = 1;
        IconRule rule; rule.selector = SelectorFor(primary);
        rule.selector.legacy.dynamicTip = rule.selector.legacy.dynamicImage = true;
        auto key = PersistentKey(rule.selector);
        std::unordered_map<std::string, IconRule> rules{{key, rule}};
        primary.identity.uid = 20; primary.tip = L"Unsampled frame";
        primary.cachedOriginal.fingerprint = 999;
        auto secondary = primary; secondary.identity.uid = 30; secondary.registrationSerial = 2;
        secondary.tip = L"Primary"; secondary.cachedOriginal.fingerprint = 111;
        std::unordered_map<std::string, RuleBinding> bindings{{IdentityKey(primary.identity), {key, false, 1}}};
        std::vector<NativeIconRecord> current{primary, secondary};
        REQUIRE(ResolveRule(rules, primary, current, &bindings.begin()->second, &bindings).rule);
        REQUIRE(!ResolveRule(rules, secondary, current, nullptr, &bindings).rule);
    });
    test("Rule.GenericOldMetadataFreeBootstrapIsReversible", [] {
        NativeIconRecord record;
        record.executable = L"C:\\OldPreferences.exe";
        record.identity = {1, 10, 100, 7, 99, false, {}};
        IconRule rule; rule.selector.executable = record.executable; rule.selector.uid = 7;
        auto key = PersistentKey(rule.selector);
        std::unordered_map<std::string, IconRule> rules{{key, rule}};
        auto provisional = ResolveRule(rules, record, {record});
        REQUIRE(provisional.rule && provisional.provisional);
        auto late = record; late.identity.uid = 7;
        REQUIRE(!ResolveRule(rules, record, {record, late}).rule);
        REQUIRE(ResolveRule(rules, late, {record, late}).rule);
        late.sourceState = NIS_HIDDEN;
        REQUIRE(!ResolveRule(rules, record, {record, late}).rule);
    });
    test("Rule.GenericHigherSpecificityOverridesLowerAmbiguity", [] {
        NativeIconRecord primary;
        primary.executable = L"C:\\Priority.exe"; primary.windowClass = L"MainHost";
        primary.callbackMessage = WM_APP + 9; primary.tip = L"Primary";
        primary.identity = {1, 10, 100, 7, 99, false, {}};
        auto secondary = primary; secondary.identity.uid = 100; secondary.tip = L"Secondary";
        IconRule general; general.selector.executable = primary.executable; general.selector.uid = 7;
        general.selector.legacy = DescribeLegacyRole(primary, {primary});
        general.selector.legacy.tips.clear(); general.selector.legacy.dynamicTip = true;
        IconRule specific = general; specific.selector.windowClass = primary.windowClass;
        specific.selector.legacy.tips = {L"Primary"}; specific.selector.legacy.dynamicTip = false;
        std::unordered_map<std::string, IconRule> rules{{PersistentKey(general.selector), general},
                                                     {PersistentKey(specific.selector), specific}};
        auto resolved = ResolveRule(rules, primary, {primary, secondary});
        REQUIRE(resolved.rule && !resolved.ambiguous && !resolved.rule->selector.windowClass.empty());
        primary.identity.uid = secondary.identity.uid = 7;
        secondary.identity.ownerHwnd += 1; secondary.windowClass = L"OtherHost";
        general.selector.legacy = {}; rules[PersistentKey(general.selector)] = general;
        resolved = ResolveRule(rules, primary, {primary, secondary});
        REQUIRE(resolved.rule && !resolved.ambiguous && !resolved.rule->selector.windowClass.empty());
    });
    test("Activity.FrameAndAccessTimersAreSeparate", [] {
        IconRule r;
        r.inactiveSeconds = 10;
        ActivityState a{1000, 1000, 1};
        REQUIRE(IsInactive(r, a, 11000));
        RecordAccess(a, 11000);
        REQUIRE(a.lastFrameChange == 1000);
        REQUIRE(!IsInactive(r, a, 11000));
        REQUIRE(IsInactive(r, a, 21000));
        r.alwaysShow = true;
        REQUIRE(!IsInactive(r, a, 999999));
    });
    test("Session.QueryDuringStartupRetainsCompletedConnection", [] {
        SessionMachine m;
        m.state = SessionState::WaitingForShell;
        HandleSessionQuery(m);
        SetOperationalState(m, SessionState::Ready);
        REQUIRE(m.state == SessionState::SessionQuery);
        HandleSessionEnd(m, false);
        REQUIRE(m.state == SessionState::Ready);
        SetOperationalState(m, SessionState::Reconnecting);
        REQUIRE(m.state == SessionState::Reconnecting);
        m.state = SessionState::UserStopping;
        SetOperationalState(m, SessionState::Ready);
        REQUIRE(m.state == SessionState::UserStopping);
    });
    test("Rule.OptionalTipInstanceAndConflicts", [] {
        NativeIconRecord record;
        record.executable = L"C:\\Synthetic\\App.exe";
        record.tip = L"Account A warning";
        record.identity.ownerPid = 42;
        record.identity.ownerCreatedAt = 99;
        IconRule general;
        general.selector = SelectorFor(record);
        IconRule specific = general;
        specific.selector.tipMode = 2;
        specific.selector.tipPattern = L"Account A";
        std::unordered_map<std::string, IconRule> rules{
            {PersistentKey(general.selector), general},
            {PersistentKey(specific.selector), specific}};
        auto result = ResolveRule(rules, record);
        REQUIRE(result.rule && result.rule->selector.tipMode == 2 && !result.ambiguous);
        specific.selector.ownerPid = 42;
        specific.selector.ownerCreatedAt = 100;
        REQUIRE(!Matches(specific.selector, record));
        specific = general;
        specific.selector.tipMode = 3;
        specific.selector.tipPattern = L"Account";
        general.selector.tipMode = 3;
        general.selector.tipPattern = L"warning";
        rules.clear();
        rules.emplace(PersistentKey(specific.selector), specific);
        rules.emplace(PersistentKey(general.selector), general);
        REQUIRE(ResolveRule(rules, record).ambiguous);
    });
    printf("RESULT passed=%d failed=%d\n", passed, failed);
    return failed ? 1 : 0;
}
