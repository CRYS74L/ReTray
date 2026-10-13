#pragma once
#include <windows.h>
#include <algorithm>
#include <cstdint>
#include <string>
#include <vector>

namespace tray {
// Read-only fallback for owners whose process DACL denies even limited query access.
// Resolve the native image path to a DOS path so it matches normal process queries.
inline uint64_t SystemOwnerInfo(uint32_t pid, std::wstring& executable) {
    executable.clear();
    using Query = LONG (NTAPI*)(ULONG, void*, ULONG, ULONG*);
    auto query = reinterpret_cast<Query>(GetProcAddress(GetModuleHandleW(L"ntdll.dll"),
                                                       "NtQuerySystemInformation"));
    if (!query || !pid) return 0;
    struct UnicodeString { USHORT length, maximumLength; wchar_t* buffer; };
    // Bracket the path query with creation times: a recycled PID must not combine
    // the previous process's executable with the replacement's generation.
    struct ProcessPrefix {
        ULONG next, threads;
        int64_t privateWorkingSet;
        ULONG hardFaults, highWatermark;
        uint64_t cycles, created, userTime, kernelTime;
        UnicodeString image;
        LONG priority;
        HANDLE pid;
    };
    static_assert(offsetof(ProcessPrefix, created) == 32);
    auto generation = [&]() -> uint64_t {
        std::vector<uint8_t> bytes(65536);
        ULONG needed{}; LONG status{};
        for (int attempt = 0; attempt < 6; ++attempt) {
            status = query(5, bytes.data(), static_cast<ULONG>(bytes.size()), &needed);
            if (status >= 0) break;
            if (status != static_cast<LONG>(0xc0000004) || needed > 32 * 1024 * 1024) return 0;
            auto size = std::max(size_t(needed) + 65536, bytes.size() * 2);
            if (size > 32 * 1024 * 1024) return 0;
            bytes.resize(size);
        }
        if (status < 0 || needed > bytes.size()) return 0;
        size_t offset{}, length = needed ? needed : bytes.size();
        while (offset + sizeof(ProcessPrefix) <= length) {
            auto& process = *reinterpret_cast<const ProcessPrefix*>(bytes.data() + offset);
            if (reinterpret_cast<uintptr_t>(process.pid) == pid) return process.created;
            if (!process.next || process.next < sizeof(ProcessPrefix) || process.next > length - offset) break;
            offset += process.next;
        }
        return 0;
    };
    auto before = generation();
    if (!before) return 0;
    std::wstring resolvedPath;
    struct ProcessIdInfo { HANDLE pid; UnicodeString image; };
    std::vector<wchar_t> path(32767);
    ProcessIdInfo info{reinterpret_cast<HANDLE>(uintptr_t(pid)),
                       {0, static_cast<USHORT>(path.size() * sizeof(wchar_t)), path.data()}};
    ULONG needed{};
    if (query(88, &info, sizeof(info), &needed) >= 0 && info.image.buffer == path.data() &&
        info.image.length <= info.image.maximumLength && !(info.image.length % 2)) {
        std::wstring native(path.data(), info.image.length / 2);
        wchar_t drives[512]{};
        auto count = GetLogicalDriveStringsW(std::size(drives), drives);
        if (count && count < std::size(drives)) {
            for (auto drive = drives; *drive; drive += wcslen(drive) + 1) {
                wchar_t name[]{drive[0], L':', 0}, device[32768]{};
                if (!QueryDosDeviceW(name, device, std::size(device))) continue;
                auto size = wcslen(device);
                if (native.size() > size && native[size] == L'\\' &&
                    !_wcsnicmp(native.c_str(), device, size)) {
                    resolvedPath = name + native.substr(size); break;
                }
            }
        }
    }
    if (resolvedPath.empty() || generation() != before) return 0;
    executable = std::move(resolvedPath);
    return before;
}
inline uint64_t ReadOwnerInfo(uint32_t pid, std::wstring& executable) {
    executable.clear();
    HANDLE process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pid);
    if (!process) return SystemOwnerInfo(pid, executable);
    wchar_t path[32768]{}; DWORD length = std::size(path);
    if (QueryFullProcessImageNameW(process, 0, path, &length)) executable.assign(path, length);
    FILETIME c{}, e{}, k{}, u{};
    uint64_t created{};
    if (GetProcessTimes(process, &c, &e, &k, &u))
        created = (uint64_t(c.dwHighDateTime) << 32) | c.dwLowDateTime;
    DWORD exitCode{};
    if (!GetExitCodeProcess(process, &exitCode) || exitCode != STILL_ACTIVE) {
        CloseHandle(process); executable.clear(); return 0;
    }
    if (executable.empty() || !created) {
        std::wstring resolvedPath;
        auto fallback = SystemOwnerInfo(pid, resolvedPath);
        if (fallback && (!created || fallback == created)) {
            executable = std::move(resolvedPath); created = fallback;
        }
    }
    CloseHandle(process);
    if (executable.empty() || !created) { executable.clear(); return 0; }
    return created;
}
}
