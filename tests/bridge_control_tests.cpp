#include "../native/shell/bridge_client.h"
#include "../native/core/notify_patch.h"
#include <cstdio>
#include <stdexcept>
using namespace tray;
constexpr UINT ChangeColor = WM_APP + 10, MakeOff = WM_APP + 11;
#define CHECK(value)                                                                               \
    do {                                                                                           \
        if (!(value))                                                                              \
            throw std::runtime_error(#value);                                                      \
    } while (0)
int wmain(int argc, wchar_t** argv) {
    if (argc < 4)
        return 2;
    DWORD sourcePid = std::stoul(argv[3]);
    try {
        auto adapter = LoadAdapter(argv[1]);
        BridgeClient client;
        CHECK(client.Attach(adapter, argv[2]));
        auto rows = client.Snapshot();
        auto find = [&](const std::vector<BridgeIcon>& list, bool guid) {
            auto found = std::find_if(list.begin(), list.end(), [&](const auto& row) {
                return row.identity.ownerPid == sourcePid && row.identity.hasGuid == guid;
            });
            CHECK(found != list.end());
            return *found;
        };
        auto original = find(rows, false), guid = find(rows, true);
        CHECK(original.version == 0);
        CHECK(guid.version == 4);
        CHECK(original.live && guid.live);
        auto refresh = [&]() {
            BridgeCommand c;
            c.operation = BridgeOperation::Refresh;
            CHECK(client.Command(c));
        };
        PostMessageW(reinterpret_cast<HWND>(original.identity.ownerHwnd), ChangeColor, 0xff1955e9,
                     0);
        Sleep(150);
        refresh();
        auto changed = find(client.Snapshot(), false);
        CHECK(changed.fingerprint != original.fingerprint);
        CHECK(changed.revision > original.revision);
        CHECK(changed.callback == original.callback && changed.version == original.version);
        CHECK(changed.originalWidth > 0 && changed.originalHeight > 0);
        CHECK(std::any_of(changed.originalPixels,
                          changed.originalPixels + changed.originalWidth * changed.originalHeight,
                          [](uint32_t pixel) { return (pixel >> 24) != 0; }));
        BridgeCommand hide;
        hide.operation = BridgeOperation::Visibility;
        hide.identity = original.identity;
        hide.visibility = 1;
        CHECK(client.Command(hide));
        CHECK(find(client.Snapshot(), false).placement == static_cast<uint32_t>(Placement::Hidden));
        hide.visibility = 0;
        CHECK(client.Command(hide));
        CHECK(find(client.Snapshot(), false).placement != static_cast<uint32_t>(Placement::Hidden));
        // A later source request must survive the assistant's forced visibility and restoration.
        SendMessageW(reinterpret_cast<HWND>(original.identity.ownerHwnd), WM_APP + 27, 1, 0);
        CHECK(find(client.Snapshot(), false).placement != static_cast<uint32_t>(Placement::Hidden));
        BridgeCommand hiddenTip;
        hiddenTip.operation = BridgeOperation::Tip;
        hiddenTip.identity = original.identity;
        wcscpy_s(hiddenTip.tip, L"Source hidden while customized");
        CHECK(client.Command(hiddenTip));
        BridgeCommand hiddenMove;
        hiddenMove.operation = BridgeOperation::Move;
        hiddenMove.identity = original.identity;
        hiddenMove.location = 1;
        hiddenMove.index = 255;
        CHECK(client.Command(hiddenMove));
        hide.visibility = 1;
        CHECK(client.Command(hide));
        CHECK(find(client.Snapshot(), false).placement == static_cast<uint32_t>(Placement::Hidden));
        BridgeCommand restoreState;
        restoreState.operation = BridgeOperation::Restore;
        restoreState.identity = original.identity;
        CHECK(client.Command(restoreState));
        CHECK(find(client.Snapshot(), false).placement == static_cast<uint32_t>(Placement::Hidden));
        SendMessageW(reinterpret_cast<HWND>(original.identity.ownerHwnd), WM_APP + 27, 0, 0);
        refresh();
        auto icon = LoadIconW(nullptr, IDI_INFORMATION);
        BridgeCommand custom;
        custom.operation = BridgeOperation::Apply;
        custom.identity = original.identity;
        custom.replacementIcon = reinterpret_cast<uint64_t>(icon);
        CHECK(client.Command(custom));
        auto replaced = find(client.Snapshot(), false);
        CHECK(replaced.fingerprint != changed.fingerprint);
        CHECK(replaced.callback == original.callback);
        CHECK(replaced.version == 0);
        SendMessageW(reinterpret_cast<HWND>(original.identity.ownerHwnd), WM_APP + 26, 0, 0);
        refresh();
        CHECK(find(client.Snapshot(), false).version == NOTIFYICON_VERSION_4);
        CHECK(find(client.Snapshot(), false).sourceFingerprint == changed.sourceFingerprint);
        SendMessageW(reinterpret_cast<HWND>(original.identity.ownerHwnd), WM_APP + 24, 0, 0);
        refresh();
        CHECK(find(client.Snapshot(), false).sourceFingerprint == changed.sourceFingerprint);
        PostMessageW(reinterpret_cast<HWND>(original.identity.ownerHwnd), MakeOff, 0, 0);
        Sleep(150);
        refresh();
        auto off = find(client.Snapshot(), false);
        // A transparent native frame still drives flashing; only the UI original preview is held.
        CHECK(off.live && off.transparent && off.custom);
        CHECK(off.decision == static_cast<uint32_t>(FrameDecision::NativeOff));
        CHECK(off.sourceFingerprint == off.fingerprint);
        CHECK(off.sourceFingerprint != changed.sourceFingerprint);
        CHECK(off.sourceWidth == off.width && off.sourceHeight == off.height);
        CHECK(std::all_of(off.pixels, off.pixels + off.width * off.height,
                          [](uint32_t pixel) { return (pixel >> 24) == 0; }));
        CHECK(off.originalWidth == changed.originalWidth &&
              off.originalHeight == changed.originalHeight);
        CHECK(std::equal(changed.originalPixels,
                         changed.originalPixels + changed.originalWidth * changed.originalHeight,
                         off.originalPixels));
        PostMessageW(reinterpret_cast<HWND>(original.identity.ownerHwnd), ChangeColor, 0xff1955e9,
                     0);
        Sleep(150);
        refresh();
        CHECK(find(client.Snapshot(), false).fingerprint == replaced.fingerprint);
        BridgeCommand restore;
        restore.operation = BridgeOperation::Restore;
        restore.identity = original.identity;
        CHECK(client.Command(restore));
        CHECK(find(client.Snapshot(), false).fingerprint == changed.fingerprint);
        BridgeCommand move;
        move.operation = BridgeOperation::Move;
        move.identity = guid.identity;
        move.location = 1;
        move.index = guid.index == 0 ? 1 : 0;
        CHECK(client.Command(move));
        CHECK(find(client.Snapshot(), true).index == static_cast<uint32_t>(move.index));
        CHECK(find(client.Snapshot(), true).placement ==
              static_cast<uint32_t>(Placement::Overflow));
        auto guidSource = find(client.Snapshot(), true).sourceFingerprint;
        custom.identity = guid.identity;
        CHECK(client.Command(custom));
        auto customGuid = find(client.Snapshot(), true).fingerprint;
        auto beforeRegionRestore = find(client.Snapshot(), true);
        move.location = -1;
        CHECK(client.Command(move));
        auto restoredRegion = find(client.Snapshot(), true);
        CHECK(restoredRegion.placement == guid.placement);
        if (beforeRegionRestore.placement == guid.placement)
            CHECK(restoredRegion.index == beforeRegionRestore.index);
        CHECK(restoredRegion.fingerprint == customGuid);
        move.location = 1;
        move.index = 255;
        CHECK(client.Command(move));
        auto bounded = find(client.Snapshot(), true);
        CHECK(bounded.index < 255);
        CHECK(bounded.fingerprint == customGuid);
        // Repeating a rule must leave every icon in the same native position.
        auto settled = client.Snapshot();
        for (int i = 0; i < 8; ++i) {
            CHECK(client.Command(move));
            auto current = client.Snapshot();
            CHECK(current.size() == settled.size());
            for (const auto& item : settled) {
                auto found =
                    std::find_if(current.begin(), current.end(), [&](const auto& candidate) {
                        return SameIcon(item.identity, candidate.identity);
                    });
                CHECK(found != current.end());
                CHECK(found->placement == item.placement && found->index == item.index);
            }
        }
        BridgeCommand clear;
        clear.operation = BridgeOperation::ClearImage;
        clear.identity = guid.identity;
        CHECK(client.Command(clear));
        CHECK(find(client.Snapshot(), true).fingerprint == guidSource);
        CHECK(find(client.Snapshot(), true).index == bounded.index);
        move.index = guid.index == 0 ? 1 : 0;
        CHECK(client.Command(move));
        // Applying a region-only rule to an icon already there must preserve every native index.
        auto beforeRegionRule = client.Snapshot();
        move.index = -1;
        CHECK(client.Command(move));
        auto afterRegionRule = client.Snapshot();
        CHECK(afterRegionRule.size() == beforeRegionRule.size());
        for (const auto& item : beforeRegionRule) {
            auto found = std::find_if(afterRegionRule.begin(), afterRegionRule.end(),
                                      [&](const auto& candidate) {
                                          return SameIcon(item.identity, candidate.identity);
                                      });
            CHECK(found != afterRegionRule.end());
            CHECK(found->placement == item.placement && found->index == item.index);
        }
        hide.identity = guid.identity;
        hide.visibility = 1;
        CHECK(client.Command(hide));
        CHECK(find(client.Snapshot(), true).placement == static_cast<uint32_t>(Placement::Hidden));
        hide.visibility = 0;
        CHECK(client.Command(hide));
        BridgeCommand menu;
        menu.operation = BridgeOperation::Menu;
        menu.identity = original.identity;
        menu.x = 400;
        menu.y = 400;
        CHECK(client.Command(menu));
        Sleep(150);
        PostMessageW(reinterpret_cast<HWND>(original.identity.ownerHwnd), WM_CANCELMODE, 0, 0);
        auto statusName = L"Local\\TrayAssistant.TestSource." + std::to_wstring(sourcePid);
        auto statusMap = OpenFileMappingW(FILE_MAP_READ, FALSE, statusName.c_str());
        CHECK(statusMap != nullptr);
        auto bytes = static_cast<uint8_t*>(MapViewOfFile(statusMap, FILE_MAP_READ, 0, 0, 40));
        CHECK(bytes != nullptr);
        LONG menuCount{};
        memcpy(&menuCount, bytes + 20, 4);
        CHECK(menuCount > 0);
        auto countMenus = [&]() {
            LONG value{};
            memcpy(&value, bytes + 20, 4);
            return value;
        };
        BridgeCommand behavior;
        behavior.operation = BridgeOperation::Behavior;
        behavior.identity = original.identity;
        behavior.index = 1;
        CHECK(client.Command(behavior));
        BridgeCommand invoke;
        invoke.operation = BridgeOperation::Invoke;
        invoke.identity = original.identity;
        invoke.clickEvent = 0;
        invoke.visibility = 0;
        invoke.x = 400;
        invoke.y = 400;
        auto beforeSwap = countMenus();
        CHECK(client.Command(invoke));
        Sleep(100);
        CHECK(countMenus() > beforeSwap);
        PostMessageW(reinterpret_cast<HWND>(original.identity.ownerHwnd), WM_CANCELMODE, 0, 0);
        invoke.visibility = 1;
        auto beforeKeyboard = countMenus();
        CHECK(client.Command(invoke));
        Sleep(100);
        CHECK(countMenus() == beforeKeyboard);
        behavior.identity = {};
        behavior.index = 1;
        CHECK(client.Command(behavior));
        invoke.clickEvent = 1;
        CHECK(client.Command(invoke));
        Sleep(100);
        CHECK(countMenus() == beforeKeyboard);
        behavior.index = 0;
        CHECK(client.Command(behavior));
        behavior.identity = original.identity;
        behavior.index = 0;
        CHECK(client.Command(behavior));
        UnmapViewOfFile(bytes);
        CloseHandle(statusMap);
        BridgeCommand tip;
        tip.operation = BridgeOperation::Tip;
        tip.identity = original.identity;
        wcscpy_s(tip.tip, L"Custom test tooltip");
        CHECK(client.Command(tip));
        CHECK(std::wstring(find(client.Snapshot(), false).tip) == L"Custom test tooltip");
        hide.identity = original.identity;
        hide.visibility = 1;
        CHECK(client.Command(hide));
        if (argc > 4 && std::wstring(argv[4]) == L"--orphan") {
            printf("ORPHAN test leaving hidden icon for bridge owner-death restoration\n");
            fflush(stdout);
            ExitProcess(0);
        }
        auto unchangedNativeOrder = client.Snapshot();
        unchangedNativeOrder.erase(
            std::remove_if(unchangedNativeOrder.begin(), unchangedNativeOrder.end(),
                           [&](const auto& item) {
                               return item.placement != guid.placement ||
                                      SameIcon(item.identity, original.identity);
                           }),
            unchangedNativeOrder.end());
        std::sort(unchangedNativeOrder.begin(), unchangedNativeOrder.end(),
                  [](const auto& a, const auto& b) { return a.index < b.index; });
        client.Detach();
        CHECK(client.shared->status == 2);
        auto after = ReadSnapshot(adapter, ExplorerPid(), original.identity.shellEpoch);
        auto final = std::find_if(after.records.begin(), after.records.end(), [&](const auto& r) {
            return r.identity.ownerPid == sourcePid && !r.identity.hasGuid;
        });
        CHECK(final != after.records.end());
        CHECK(final->placement == static_cast<Placement>(original.placement));
        auto finalGuid =
            std::find_if(after.records.begin(), after.records.end(), [&](const auto& r) {
                return r.identity.ownerPid == sourcePid && r.identity.hasGuid;
            });
        CHECK(finalGuid != after.records.end());
        CHECK(finalGuid->placement == static_cast<Placement>(guid.placement));
        std::vector<NativeIconRecord> peersAfterDetach;
        for (const auto& item : after.records)
            if (std::any_of(unchangedNativeOrder.begin(), unchangedNativeOrder.end(),
                            [&](const auto& before) {
                                return SameIcon(item.identity, before.identity);
                            }))
                peersAfterDetach.push_back(item);
        CHECK(peersAfterDetach.size() == unchangedNativeOrder.size());
        std::sort(peersAfterDetach.begin(), peersAfterDetach.end(),
                  [](const auto& a, const auto& b) { return a.nativeIndex < b.nativeIndex; });
        for (size_t i = 0; i < unchangedNativeOrder.size(); ++i)
            CHECK(SameIcon(peersAfterDetach[i].identity, unchangedNativeOrder[i].identity));
        CHECK(final->tip == std::wstring(original.tip));
        printf("PASS source-before-attach + legacy/v4 + current updates + GUID/legacy hide/show + "
               "custom + stable original preview during off/flash + lossless frame restore + "
               "native move + region-only restoration + actual original menu + "
               "detach visibility restoration without native reordering\n");
        return 0;
    } catch (const std::exception& error) {
        fprintf(stderr, "FAIL bridge control: %s\n", error.what());
        return 1;
    }
}
