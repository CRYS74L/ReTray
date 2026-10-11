#include "../native/shell/bridge_client.h"
#include <cstdio>
#include <stdexcept>
using namespace tray;
#define CHECK(x)                                                                                   \
    do {                                                                                           \
        if (!(x))                                                                                  \
            throw std::runtime_error(#x);                                                          \
    } while (0)
int wmain(int argc, wchar_t** argv) {
    if (argc != 4)
        return 2;
    auto pid = std::stoul(argv[3]);
    try {
        BridgeClient bridge;
        auto adapter = LoadAdapter(argv[1]);
        CHECK(bridge.Attach(adapter, argv[2]));
        auto rows = bridge.Snapshot();
        auto row = std::find_if(rows.begin(), rows.end(), [&](const auto& r) {
            return r.identity.ownerPid == pid && !r.identity.hasGuid;
        });
        CHECK(row != rows.end());
        auto id = row->identity;
        auto hwnd = reinterpret_cast<HWND>(id.ownerHwnd);
        BridgeCommand custom;
        custom.operation = BridgeOperation::Apply;
        custom.identity = id;
        custom.replacementIcon = reinterpret_cast<uint64_t>(LoadIconW(nullptr, IDI_INFORMATION));
        CHECK(bridge.Command(custom));
        rows = bridge.Snapshot();
        auto customHash = std::find_if(rows.begin(), rows.end(), [&](const auto& r) {
                              return SameIcon(r.identity, id);
                          })->fingerprint;
        auto stats = [&](const char* phase) {
            std::vector<uint32_t> t(bridge.shared->callbackMicros,
                                    bridge.shared->callbackMicros +
                                        std::min<uint64_t>(512, bridge.shared->events));
            std::sort(t.begin(), t.end());
            uint32_t p95 = t.empty() ? 0 : t[(t.size() - 1) * 95 / 100];
            printf("PERF %s events=%llu p95=%u us\n", phase,
                   static_cast<unsigned long long>(bridge.shared->events), p95);
            return p95;
        };
        auto send = [&](UINT message, WPARAM count) {
            DWORD_PTR result{};
            CHECK(SendMessageTimeoutW(hwnd, message, count, 0, SMTO_ABORTIFHUNG, 60000, &result));
        };
        send(WM_APP + 20, 200);
        rows = bridge.Snapshot();
        CHECK(std::count_if(rows.begin(), rows.end(),
                            [&](const auto& r) { return r.identity.ownerPid == pid; }) == 202);
        CHECK(std::find_if(rows.begin(), rows.end(), [&](const auto& r) {
                  return SameIcon(r.identity, id);
              })->fingerprint == customHash);
        auto addP95 = stats("200-add");
        send(WM_APP + 22, 100);
        rows = bridge.Snapshot();
        CHECK(std::count_if(rows.begin(), rows.end(),
                            [&](const auto& r) { return r.identity.ownerPid == pid; }) == 202);
        stats("100-add-delete");
        send(WM_APP + 23, 1000);
        rows = bridge.Snapshot();
        auto primary = std::find_if(rows.begin(), rows.end(),
                                    [&](const auto& r) { return SameIcon(r.identity, id); });
        CHECK(primary != rows.end());
        CHECK(primary->fingerprint == customHash);
        CHECK(primary->version == 0);
        CHECK(primary->revision >= 1001);
        auto modifyP95 = stats("1000-flash-updates");
        send(WM_APP + 21, 0);
        rows = bridge.Snapshot();
        CHECK(std::count_if(rows.begin(), rows.end(),
                            [&](const auto& r) { return r.identity.ownerPid == pid; }) == 2);
        bridge.Detach();
        CHECK(bridge.shared->status == 2);
        printf("PASS identities, custom stability, dynamic cycle, additions/deletions and bounded "
               "cleanup\n");
        if (addP95 > 2000 || modifyP95 > 2000) {
            puts("FAIL p95 callback overhead exceeds 2ms gate");
            return 1;
        }
        return 0;
    } catch (const std::exception& e) {
        fprintf(stderr, "FAIL stress %s\n", e.what());
        return 1;
    }
}
