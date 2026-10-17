#include "../native/platform/native_snapshot.h"
#include <windows.h>
#include <tlhelp32.h>
#include <cstdio>
#include <stdexcept>
uint64_t moduleEnumerations{};
HMODULE WINAPI LookupTestGetModuleHandleW(LPCWSTR name) {
    // Map the taskbar name to a module already in this isolated test process.
    // No taskbar DLL is loaded and no Explorer state is changed.
    return GetModuleHandleW(!_wcsicmp(name, L"Taskbar.dll") ? L"kernel32.dll" : name);
}
HANDLE WINAPI LookupTestCreateToolhelp32Snapshot(DWORD flags, DWORD pid) {
    ++moduleEnumerations;
    return CreateToolhelp32Snapshot(flags, pid);
}
#define GetModuleHandleW LookupTestGetModuleHandleW
#define CreateToolhelp32Snapshot LookupTestCreateToolhelp32Snapshot
#include "../native/platform/native_snapshot.cpp"
#undef GetModuleHandleW
#undef CreateToolhelp32Snapshot
#define CHECK(x) do { if (!(x)) throw std::runtime_error(#x); } while (0)
uint64_t LegacyModuleLookup(DWORD pid, const wchar_t* name) {
    auto snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE, pid);
    if (snapshot == INVALID_HANDLE_VALUE) return 0;
    MODULEENTRY32W module{}; module.dwSize = sizeof(module);
    uint64_t base{};
    if (Module32FirstW(snapshot, &module)) do {
        if (!_wcsicmp(module.szModule, name)) { base = reinterpret_cast<uint64_t>(module.modBaseAddr); break; }
    } while (Module32NextW(snapshot, &module));
    CloseHandle(snapshot); return base;
}
uint64_t Micros() {
    LARGE_INTEGER value{}, frequency{}; QueryPerformanceCounter(&value); QueryPerformanceFrequency(&frequency);
    return static_cast<uint64_t>(value.QuadPart / frequency.QuadPart) * 1000000 +
        static_cast<uint64_t>(value.QuadPart % frequency.QuadPart) * 1000000 / frequency.QuadPart;
}
void TargetedRecordValidationRejectsDeletedAndRecycledOwners() {
    WNDCLASSW cls{}; cls.hInstance = GetModuleHandleW(nullptr); cls.lpfnWndProc = DefWindowProcW;
    cls.lpszClassName = L"ReTray.IsolatedRecordValidation";
    CHECK(RegisterClassW(&cls));
    auto window = CreateWindowW(cls.lpszClassName, L"", WS_POPUP, 0, 0, 1, 1, nullptr, nullptr, cls.hInstance, nullptr);
    CHECK(window);
    try {
        std::array<uint8_t, 64> bytes{};
        uint64_t address = reinterpret_cast<uint64_t>(bytes.data());
        std::array<uint64_t, 1> records{address};
        std::array<uint64_t, 2> collection{reinterpret_cast<uint64_t>(records.data()), reinterpret_cast<uint64_t>(records.data() + 1)};
        uint64_t manager = reinterpret_cast<uint64_t>(collection.data());
        uint64_t model = reinterpret_cast<uint64_t>(&manager), root = reinterpret_cast<uint64_t>(&model);
        const auto base = reinterpret_cast<uint64_t>(&root);
        tray::SymbolAdapter adapter{}; adapter.window = 8; adapter.uid = 16; adapter.guid = 24; adapter.itemVtable = 0x2000;
        const uint64_t table = base + adapter.itemVtable, hwnd = reinterpret_cast<uint64_t>(window);
        const uint32_t uid = 17;
        memcpy(bytes.data(), &table, sizeof(table)); memcpy(bytes.data() + adapter.window, &hwnd, sizeof(hwnd));
        memcpy(bytes.data() + adapter.uid, &uid, sizeof(uid));
        FILETIME created{}, exited{}, kernel{}, user{};
        CHECK(GetProcessTimes(GetCurrentProcess(), &created, &exited, &kernel, &user));
        tray::NativeIconRecord record; record.recordAddress = address;
        record.identity.ownerHwnd = hwnd; record.identity.ownerPid = GetCurrentProcessId(); record.identity.uid = uid;
        record.identity.ownerCreatedAt = (uint64_t(created.dwHighDateTime) << 32) | created.dwLowDateTime;
        CHECK(tray::ValidateNativeRecord(adapter, base, record) == ERROR_SUCCESS);
        ++record.identity.ownerCreatedAt;
        CHECK(tray::ValidateNativeRecord(adapter, base, record) == ERROR_NOT_FOUND);
        --record.identity.ownerCreatedAt;
        bytes[adapter.guid] = 1;
        CHECK(tray::ValidateNativeRecord(adapter, base, record) == ERROR_NOT_FOUND);
        bytes[adapter.guid] = 0;
        collection[1] = collection[0];
        CHECK(tray::ValidateNativeRecord(adapter, base, record) == ERROR_NOT_FOUND);
        collection[1] += sizeof(uint64_t);
        DestroyWindow(window); window = nullptr;
        CHECK(tray::ValidateNativeRecord(adapter, base, record) == ERROR_NOT_FOUND);
    } catch (...) { if (window) DestroyWindow(window); UnregisterClassW(cls.lpszClassName, cls.hInstance); throw; }
    UnregisterClassW(cls.lpszClassName, cls.hInstance);
    std::printf("PASS targeted validation rejects stale generations, GUIDs, manager membership and dead windows\n");
}
int wmain(int argc, wchar_t** argv) {
    try {
        TargetedRecordValidationRejectsDeletedAndRecycledOwners();
        auto pid = GetCurrentProcessId();
        auto expected = reinterpret_cast<uint64_t>(GetModuleHandleW(L"kernel32.dll"));
        auto started = Micros();
        for (int i = 0; i < 100; ++i) CHECK(LegacyModuleLookup(pid, L"kernel32.dll") == expected);
        auto oldMicros = Micros() - started;
        started = Micros();
        for (int i = 0; i < 100; ++i) CHECK(tray::ModuleBase(pid) == expected);
        auto newMicros = Micros() - started;
        CHECK(moduleEnumerations == 0);
        std::printf("PASS local snapshot lookup retains the module identity without module enumeration\n");
        std::printf("METRIC 100 local module lookups legacy=%llu us optimized=%llu us\n",
                    static_cast<unsigned long long>(oldMicros), static_cast<unsigned long long>(newMicros));
        auto explorer = tray::ExplorerPid();
        if (explorer) {
            auto base = LegacyModuleLookup(explorer, L"Taskbar.dll"); CHECK(base);
            started = Micros();
            for (int i = 0; i < 20; ++i) CHECK(LegacyModuleLookup(explorer, L"Taskbar.dll") == base);
            std::printf("METRIC 20 read-only Explorer module enumerations=%llu us\n",
                        static_cast<unsigned long long>(Micros() - started));
            if (argc > 1) {
                auto adapter = tray::LoadAdapter(argv[1]);
                started = Micros();
                size_t icons{};
                for (int i = 0; i < 8; ++i) {
                    auto snapshot = tray::ReadSnapshot(adapter, explorer, 1, false);
                    CHECK(snapshot.complete); icons = snapshot.records.size();
                }
                std::printf("METRIC 8 read-only full Explorer snapshots icons=%zu total=%llu us\n", icons,
                            static_cast<unsigned long long>(Micros() - started));
            }
        }
        return 0;
    } catch (const std::exception& error) {
        std::fprintf(stderr, "FAIL %s\n", error.what()); return 1;
    }
}
