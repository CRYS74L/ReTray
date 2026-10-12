#pragma once
#include <windows.h>
#include <oleacc.h>
#include <filesystem>
#include <string>

namespace tray_menu_typography {
struct Typeface {
    HFONT font{};
    std::wstring path;
    bool privateLoaded{};
    UINT dpi{96};
    ~Typeface() {
        if (font) DeleteObject(font);
        if (privateLoaded) RemoveFontResourceExW(path.c_str(), FR_PRIVATE, nullptr);
    }
};
inline Typeface typeface;
inline const wchar_t* Label(UINT id) {
    switch (id) {
        case 1: return L"打开助手";
        case 2: return L"重新启动";
        case 3: return L"退出";
        default: return L"";
    }
}
inline MSAAMENUINFO menuNames[] = {
    {MSAA_MENU_SIG, 4, const_cast<wchar_t*>(L"打开助手")},
    {MSAA_MENU_SIG, 4, const_cast<wchar_t*>(L"重新启动")},
    {MSAA_MENU_SIG, 2, const_cast<wchar_t*>(L"退出")}};
inline const wchar_t* Data(UINT id) { return reinterpret_cast<const wchar_t*>(&menuNames[id - 1]); }
inline int Scale(int value) { return MulDiv(value, typeface.dpi, 96); }
inline void Prepare(const std::filesystem::path& root, POINT point) {
    if (!typeface.privateLoaded) {
        typeface.path = (root / L"Assets" / L"Fonts" / L"Static" / L"NotoSansSC-Regular.ttf").wstring();
        typeface.privateLoaded = AddFontResourceExW(typeface.path.c_str(), FR_PRIVATE, nullptr) != 0;
    }
    auto monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
    UINT dpiX = 96, dpiY = 96;
    auto shcore = LoadLibraryW(L"shcore.dll");
    if (shcore) {
        using GetDpi = HRESULT(WINAPI*)(HMONITOR, int, UINT*, UINT*);
        auto getDpi = reinterpret_cast<GetDpi>(GetProcAddress(shcore, "GetDpiForMonitor"));
        if (getDpi) getDpi(monitor, 0, &dpiX, &dpiY);
        FreeLibrary(shcore);
    }
    if (typeface.font && typeface.dpi == dpiX) return;
    if (typeface.font) DeleteObject(typeface.font);
    typeface.dpi = dpiX;
    typeface.font = CreateFontW(-Scale(12), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_NATURAL_QUALITY,
        DEFAULT_PITCH | FF_SWISS, L"Noto Sans SC");
}
inline bool Measure(LPARAM argument) {
    auto item = reinterpret_cast<MEASUREITEMSTRUCT*>(argument);
    if (!item || item->CtlType != ODT_MENU || item->itemID < 1 || item->itemID > 3) return false;
    auto dc = GetDC(nullptr);
    auto previous = SelectObject(dc, typeface.font);
    SIZE size{};
    auto label = Label(item->itemID);
    GetTextExtentPoint32W(dc, label, static_cast<int>(wcslen(label)), &size);
    item->itemWidth = size.cx + Scale(24);
    item->itemHeight = static_cast<UINT>(Scale(27));
    SelectObject(dc, previous); ReleaseDC(nullptr, dc);
    return true;
}
inline bool Draw(LPARAM argument) {
    auto item = reinterpret_cast<DRAWITEMSTRUCT*>(argument);
    if (!item || item->CtlType != ODT_MENU || item->itemID < 1 || item->itemID > 3) return false;
    auto background = GetSysColor(COLOR_MENU);
    auto foreground = GetSysColor(COLOR_MENUTEXT);
    if (item->itemState & ODS_SELECTED) {
        auto mix = [](BYTE a, BYTE b) { return static_cast<BYTE>((a * 92 + b * 8) / 100); };
        background = RGB(mix(GetRValue(background), GetRValue(foreground)),
                         mix(GetGValue(background), GetGValue(foreground)),
                         mix(GetBValue(background), GetBValue(foreground)));
    }
    auto brush = CreateSolidBrush(background);
    FillRect(item->hDC, &item->rcItem, brush); DeleteObject(brush);
    auto previous = SelectObject(item->hDC, typeface.font);
    auto oldMode = SetBkMode(item->hDC, TRANSPARENT);
    auto oldColor = SetTextColor(item->hDC, foreground);
    auto bounds = item->rcItem; bounds.left += Scale(12); bounds.right -= Scale(12);
    DrawTextW(item->hDC, Label(item->itemID), -1, &bounds, DT_SINGLELINE | DT_VCENTER | DT_LEFT | DT_NOPREFIX);
    SetTextColor(item->hDC, oldColor); SetBkMode(item->hDC, oldMode);
    SelectObject(item->hDC, previous);
    return true;
}
inline HWND roundedWindow{};
inline SIZE roundedSize{};
inline LRESULT CALLBACK RoundMenuHook(int code, WPARAM argument, LPARAM value) {
    if (code == HC_ACTION) {
        auto event = reinterpret_cast<CWPSTRUCT*>(value);
        if (event->message == WM_WINDOWPOSCHANGED) {
            wchar_t name[32]{};
            GetClassNameW(event->hwnd, name, 32);
            if (wcscmp(name, L"#32768") == 0) {
                RECT bounds{}; GetWindowRect(event->hwnd, &bounds);
                SIZE size{bounds.right - bounds.left, bounds.bottom - bounds.top};
                if (size.cx > 0 && size.cy > 0 &&
                    (roundedWindow != event->hwnd || roundedSize.cx != size.cx || roundedSize.cy != size.cy)) {
                    roundedWindow = event->hwnd; roundedSize = size;
                    auto region = CreateRoundRectRgn(0, 0, size.cx + 1, size.cy + 1, Scale(16), Scale(16));
                    if (region && !SetWindowRgn(event->hwnd, region, TRUE)) DeleteObject(region);
                }
            }
        }
    }
    return CallNextHookEx(nullptr, code, argument, value);
}
struct RoundedMenuScope {
    HHOOK hook{};
    RoundedMenuScope() {
        roundedWindow = nullptr; roundedSize = {};
        hook = SetWindowsHookExW(WH_CALLWNDPROC, RoundMenuHook, nullptr, GetCurrentThreadId());
    }
    ~RoundedMenuScope() { if (hook) UnhookWindowsHookEx(hook); roundedWindow = nullptr; }
};
}
