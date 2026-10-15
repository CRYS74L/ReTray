#pragma once
#include <windows.h>
#include <algorithm>
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
inline std::string language="zh";
inline void SetLanguage(const std::string& value){language=value;}
inline const wchar_t* Label(UINT id) {
    if(language=="en")switch(id){case 1:return L"Open";case 2:return L"Restart";case 3:return L"Exit";default:return L"";}
    if(language=="zh-Hant")switch(id){case 1:return L"開啟";case 2:return L"重新啟動";case 3:return L"退出";default:return L"";}
    if(language=="ja")switch(id){case 1:return L"開く";case 2:return L"再起動";case 3:return L"終了";default:return L"";}
    switch (id) {
        case 1: return L"打开";
        case 2: return L"重新启动";
        case 3: return L"退出";
        default: return L"";
    }
}
inline MSAAMENUINFO menuNames[] = {
    {MSAA_MENU_SIG, 2, const_cast<wchar_t*>(L"打开")},
    {MSAA_MENU_SIG, 4, const_cast<wchar_t*>(L"重新启动")},
    {MSAA_MENU_SIG, 2, const_cast<wchar_t*>(L"退出")}};
inline const wchar_t* Data(UINT id) {
    auto& name=menuNames[id-1];name.pszWText=const_cast<wchar_t*>(Label(id));name.cchWText=static_cast<DWORD>(wcslen(name.pszWText));
    return reinterpret_cast<const wchar_t*>(&name);
}
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
    typeface.font = CreateFontW(-Scale(13), 0, 0, 0, FW_NORMAL, FALSE, FALSE, FALSE,
        DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY,
        DEFAULT_PITCH | FF_SWISS, typeface.privateLoaded ? L"Noto Sans SC" : L"Segoe UI");
}
inline bool Measure(LPARAM argument) {
    auto item = reinterpret_cast<MEASUREITEMSTRUCT*>(argument);
    if (!item || item->CtlType != ODT_MENU || item->itemID < 1 || item->itemID > 3) return false;
    auto dc = GetDC(nullptr);
    auto previous = SelectObject(dc, typeface.font);
    SIZE size{};
    auto label = Label(item->itemID);
    GetTextExtentPoint32W(dc, label, static_cast<int>(wcslen(label)), &size);
    item->itemWidth = static_cast<UINT>(std::max(size.cx + Scale(28), static_cast<LONG>(Scale(104))));
    item->itemHeight = static_cast<UINT>(Scale(30));
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
    auto bounds = item->rcItem; bounds.left += Scale(14); bounds.right -= Scale(14);
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
                    // DWM owns the menu's full frame and shadow. A custom window
                    // region clips that frame (and prevents system rounding).
                    auto dwm = LoadLibraryW(L"dwmapi.dll");
                    if (dwm) {
                        using SetAttribute = HRESULT(WINAPI*)(HWND, DWORD, LPCVOID, DWORD);
                        auto set = reinterpret_cast<SetAttribute>(GetProcAddress(dwm, "DwmSetWindowAttribute"));
                        DWORD preference = 3; // DWMWCP_ROUNDSMALL, standard auxiliary menus.
                        if (set) set(event->hwnd, 33, &preference, sizeof(preference));
                        FreeLibrary(dwm);
                    }
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
