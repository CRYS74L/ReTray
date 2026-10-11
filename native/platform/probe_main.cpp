#include "native_snapshot.h"
#include <fstream>
#include <iostream>
int wmain(int argc, wchar_t** argv) {
    try {
        if (argc < 2) {
            std::cerr << "Usage: TrayAssistant.Probe.exe adapter.json [output.json]\n";
            return 2;
        }
        auto adapter = tray::LoadAdapter(argv[1]);
        auto snapshot = tray::ReadSnapshot(adapter, tray::ExplorerPid(), 1);
        if (argc > 2) {
            std::ofstream stream{std::filesystem::path(argv[2])};
            stream << tray::SnapshotJson(snapshot, true).dump(2);
        }
        std::cout << "native snapshot success=" << snapshot.outcome.success
                  << " complete=" << snapshot.complete << " count=" << snapshot.records.size()
                  << "\n";
        if (!snapshot.outcome.success)
            std::cerr << snapshot.outcome.detail << "\n";
        return snapshot.outcome.success ? 0 : 1;
    } catch (const std::exception& error) {
        std::cerr << error.what() << "\n";
        return 1;
    }
}
