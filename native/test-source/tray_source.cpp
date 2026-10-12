#include "../core/types.h"
#include <algorithm>
#include <array>
#include <unordered_set>
using namespace tray;
constexpr UINT Callback = WM_APP + 9, ChangeColor = WM_APP + 10, MakeOff = WM_APP + 11,
               StopSource = WM_APP + 12;
struct SourceStatus {
    uint32_t magic, pid;
    uint64_t hwnd;
    volatile LONG callbacks, menus;
    uint64_t lastW, lastL;
};
SourceStatus* status{};
HANDLE map{};
HICON current{};
HWND window{};
bool silentOwnerExit{};
std::unordered_set<uint32_t> extras;
GUID GuidIcon = {
    0x31415926, 0x1011, 0x2022, {0x93, 0x24, 0x45, 0x66, 0x87, 0xa8, 0xc9, 0xea}};
HICON CreateColor(uint32_t color) {
    BITMAPV5HEADER header{};
    header.bV5Size = sizeof(header);
    header.bV5Width = 32;
    header.bV5Height = -32;
    header.bV5Planes = 1;
    header.bV5BitCount = 32;
    header.bV5Compression = BI_BITFIELDS;
    header.bV5RedMask = 0xff0000;
    header.bV5GreenMask = 0xff00;
    header.bV5BlueMask = 0xff;
    header.bV5AlphaMask = 0xff000000;
    void* pixels{};
    HDC dc = GetDC(nullptr);
    auto bitmap = CreateDIBSection(dc, reinterpret_cast<BITMAPINFO*>(&header), DIB_RGB_COLORS,
                                   &pixels, nullptr, 0);
    ReleaseDC(nullptr, dc);
    std::fill_n(static_cast<uint32_t*>(pixels), 1024, color);
    std::array<uint8_t, 128> mask{};
    if (!(color >> 24))
        mask.fill(255);
    auto mono = CreateBitmap(32, 32, 1, 1, mask.data());
    ICONINFO info{};
    info.fIcon = TRUE;
    info.hbmColor = bitmap;
    info.hbmMask = mono;
    auto icon = CreateIconIndirect(&info);
    DeleteObject(bitmap);
    DeleteObject(mono);
    return icon;
}
NOTIFYICONDATAW Data(bool guid, DWORD flags) {
    NOTIFYICONDATAW data{};
    data.cbSize = sizeof(data);
    data.hWnd = window;
    data.uID = guid ? 42 : 11;
    data.uFlags = flags;
    data.hIcon = current;
    data.uCallbackMessage = Callback;
    if (guid) {
        data.uFlags |= NIF_GUID;
        data.guidItem = GuidIcon;
        wcscpy_s(data.szTip, L"TrayAssistant Test GUID");
    } else
        wcscpy_s(data.szTip, L"TrayAssistant Test Legacy");
    return data;
}
void Update(uint32_t color) {
    auto next = CreateColor(color);
    auto old = current;
    current = next;
    for (bool guid : {false, true}) {
        auto data = Data(guid, NIF_ICON);
        Shell_NotifyIconW(NIM_MODIFY, &data);
    }
    if (old)
        DestroyIcon(old);
}
LRESULT CALLBACK Procedure(HWND hwnd, UINT message, WPARAM w, LPARAM l) {
    if (message == WM_APP + 30) {
        for (bool guid : {false, true}) {
            auto data = Data(guid, 0);
            if (!Shell_NotifyIconW(NIM_DELETE, &data)) return 0;
            data = Data(guid, NIF_ICON | NIF_TIP | NIF_MESSAGE);
            if (!Shell_NotifyIconW(NIM_ADD, &data)) return 0;
            if (guid) {
                data.uVersion = NOTIFYICON_VERSION_4;
                Shell_NotifyIconW(NIM_SETVERSION, &data);
            }
        }
        return 1;
    }
    if (message == WM_APP + 28) {
        // Model apps that destroy their tray owner without sending NIM_DELETE,
        // while their process continues running.
        silentOwnerExit = true;
        DestroyWindow(hwnd);
        silentOwnerExit = false;
        window = CreateWindowExW(0, L"TrayAssistant.TestSource", L"TrayAssistant controlled source",
                                 0, 0, 0, 0, 0, nullptr, nullptr,
                                 GetModuleHandleW(nullptr), nullptr);
        status->hwnd = reinterpret_cast<uint64_t>(window);
        return window != nullptr;
    }
    if (message == WM_APP + 29) {
        auto data = Data(false, NIF_ICON | NIF_TIP | NIF_MESSAGE);
        return Shell_NotifyIconW(NIM_ADD, &data);
    }
    if (message == WM_APP + 26) {
        auto data = Data(false, NIF_ICON | NIF_TIP | NIF_MESSAGE | NIF_STATE);
        data.uVersion = NOTIFYICON_VERSION_4;
        return Shell_NotifyIconW(NIM_SETVERSION, &data);
    }
    if (message == WM_APP + 27) {
        auto data = Data(false, NIF_STATE); data.dwStateMask = NIS_HIDDEN;
        data.dwState = w ? NIS_HIDDEN : 0;
        return Shell_NotifyIconW(NIM_MODIFY, &data);
    }
    static UINT created = RegisterWindowMessageW(L"TaskbarCreated");
    if (message == created) {
        for (bool guid : {false, true}) {
            auto data = Data(guid, NIF_ICON | NIF_TIP | NIF_MESSAGE);
            Shell_NotifyIconW(NIM_ADD, &data);
            if (guid) {
                data.uVersion = NOTIFYICON_VERSION_4;
                Shell_NotifyIconW(NIM_SETVERSION, &data);
            }
        }
        return 0;
    }
    if (message == WM_APP + 25) {
        SetWindowLongPtrW(hwnd, GWL_STYLE, WS_OVERLAPPEDWINDOW);
        SetWindowPos(hwnd, nullptr, 180, 180, 480, 280,
                     SWP_NOACTIVATE | SWP_NOZORDER | SWP_FRAMECHANGED);
        ShowWindow(hwnd, SW_SHOWNOACTIVATE);
        return 1;
    }
    if (message == WM_APP + 20) {
        for (uint32_t i = 0; i < std::min<uint32_t>(200, w); ++i) {
            auto data = Data(false, NIF_ICON | NIF_TIP | NIF_MESSAGE);
            data.uID = 100 + i;
            wcscpy_s(data.szTip, L"TrayAssistant load test");
            if (Shell_NotifyIconW(NIM_ADD, &data))
                extras.insert(data.uID);
        }
        return 1;
    }
    if (message == WM_APP + 21) {
        for (auto id : extras) {
            auto data = Data(false, 0);
            data.uID = id;
            Shell_NotifyIconW(NIM_DELETE, &data);
        }
        extras.clear();
        return 1;
    }
    if (message == WM_APP + 22) {
        for (uint32_t i = 0; i < std::min<uint32_t>(100, w); ++i) {
            auto data = Data(false, NIF_ICON | NIF_TIP | NIF_MESSAGE);
            data.uID = 400;
            Shell_NotifyIconW(NIM_ADD, &data);
            Shell_NotifyIconW(NIM_DELETE, &data);
        }
        return 1;
    }
    if (message == WM_APP + 23) {
        for (uint32_t i = 0; i < std::min<uint32_t>(1000, w); ++i)
            Update(i % 2 ? 0xff248954 : 0);
        return 1;
    }
    if (message == WM_APP + 24) {
        auto data = Data(false, 0);
        return Shell_NotifyIconW(NIM_SETFOCUS, &data);
    }
    if (message == ChangeColor) {
        Update(static_cast<uint32_t>(w));
        return 0;
    }
    if (message == MakeOff) {
        Update(0);
        return 0;
    }
    if (message == StopSource) {
        DestroyWindow(hwnd);
        return 0;
    }
    if (message == Callback) {
        InterlockedIncrement(&status->callbacks);
        status->lastW = w;
        status->lastL = l;
        UINT event = LOWORD(l);
        if (event == WM_RBUTTONUP || event == WM_CONTEXTMENU) {
            InterlockedIncrement(&status->menus);
            HMENU menu = CreatePopupMenu();
            AppendMenuW(menu, MF_STRING, 1, L"Test original menu");
            POINT point{};
            GetCursorPos(&point);
            SetForegroundWindow(hwnd);
            TrackPopupMenu(menu, TPM_RETURNCMD | TPM_NONOTIFY, point.x, point.y, 0, hwnd, nullptr);
            DestroyMenu(menu);
        }
        return 0;
    }
    if (message == WM_QUERYENDSESSION)
        return TRUE;
    if (message == WM_ENDSESSION && w) {
        DestroyWindow(hwnd);
        return 0;
    }
    if (message == WM_DESTROY) {
        if (silentOwnerExit) {
            extras.clear();
            return 0;
        }
        for (auto id : extras) {
            auto data = Data(false, 0);
            data.uID = id;
            Shell_NotifyIconW(NIM_DELETE, &data);
        }
        extras.clear();
        for (bool guid : {false, true}) {
            auto data = Data(guid, 0);
            Shell_NotifyIconW(NIM_DELETE, &data);
        }
        PostQuitMessage(0);
        return 0;
    }
    return DefWindowProcW(hwnd, message, w, l);
}
int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, LPWSTR options, int) {
    if (wcsstr(options, L"--unique-guid"))
        GuidIcon.Data1 ^= GetCurrentProcessId();
    std::wstring name =
        L"Local\\TrayAssistant.TestSource." + std::to_wstring(GetCurrentProcessId());
    map = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(SourceStatus),
                             name.c_str());
    status = static_cast<SourceStatus*>(
        MapViewOfFile(map, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(SourceStatus)));
    if (!status)
        return 1;
    *status = {};
    status->magic = 0x54535431;
    status->pid = GetCurrentProcessId();
    WNDCLASSW cls{};
    cls.lpfnWndProc = Procedure;
    cls.hInstance = instance;
    cls.lpszClassName = L"TrayAssistant.TestSource";
    RegisterClassW(&cls);
    window = CreateWindowExW(0, cls.lpszClassName, L"TrayAssistant controlled source", 0, 0, 0, 0,
                             0, nullptr, nullptr, instance, nullptr);
    status->hwnd = reinterpret_cast<uint64_t>(window);
    current = CreateColor(0xff248954);
    for (bool guid : {false, true}) {
        auto data = Data(guid, NIF_ICON | NIF_TIP | NIF_MESSAGE);
        if (!Shell_NotifyIconW(NIM_ADD, &data))
            return 2;
        if (guid) {
            data.uVersion = NOTIFYICON_VERSION_4;
            Shell_NotifyIconW(NIM_SETVERSION, &data);
        }
    }
    MSG message{};
    while (GetMessageW(&message, nullptr, 0, 0) > 0) {
        TranslateMessage(&message);
        DispatchMessageW(&message);
    }
    if (current)
        DestroyIcon(current);
    UnmapViewOfFile(status);
    CloseHandle(map);
    return 0;
}
