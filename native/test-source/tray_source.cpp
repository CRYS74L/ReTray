#include "../core/types.h"
#include <array>
using namespace tray;
constexpr UINT Callback=WM_APP+9,ChangeColor=WM_APP+10,MakeOff=WM_APP+11,StopSource=WM_APP+12;
struct SourceStatus {uint32_t magic,pid;uint64_t hwnd;volatile LONG callbacks,menus;uint64_t lastW,lastL;};
SourceStatus* status{};HANDLE map{};HICON current{};HWND window{};
const GUID GuidIcon={0x31415926,0x1011,0x2022,{0x93,0x24,0x45,0x66,0x87,0xa8,0xc9,0xea}};
HICON CreateColor(uint32_t color) {
    BITMAPV5HEADER header{};header.bV5Size=sizeof(header);header.bV5Width=32;header.bV5Height=-32;header.bV5Planes=1;header.bV5BitCount=32;header.bV5Compression=BI_BITFIELDS;
    header.bV5RedMask=0xff0000;header.bV5GreenMask=0xff00;header.bV5BlueMask=0xff;header.bV5AlphaMask=0xff000000;
    void* pixels{};HDC dc=GetDC(nullptr);auto bitmap=CreateDIBSection(dc,reinterpret_cast<BITMAPINFO*>(&header),DIB_RGB_COLORS,&pixels,nullptr,0);ReleaseDC(nullptr,dc);
    std::fill_n(static_cast<uint32_t*>(pixels),1024,color);std::array<uint8_t,128> mask{};if(!(color>>24))mask.fill(255);
    auto mono=CreateBitmap(32,32,1,1,mask.data());ICONINFO info{};info.fIcon=TRUE;info.hbmColor=bitmap;info.hbmMask=mono;
    auto icon=CreateIconIndirect(&info);DeleteObject(bitmap);DeleteObject(mono);return icon;
}
NOTIFYICONDATAW Data(bool guid,DWORD flags) {
    NOTIFYICONDATAW data{};data.cbSize=sizeof(data);data.hWnd=window;data.uID=guid?42:11;data.uFlags=flags;data.hIcon=current;data.uCallbackMessage=Callback;
    if(guid){data.uFlags|=NIF_GUID;data.guidItem=GuidIcon;wcscpy_s(data.szTip,L"TrayAssistant Test GUID");}else wcscpy_s(data.szTip,L"TrayAssistant Test Legacy");return data;
}
void Update(uint32_t color){auto next=CreateColor(color);auto old=current;current=next;for(bool guid:{false,true}){auto data=Data(guid,NIF_ICON);Shell_NotifyIconW(NIM_MODIFY,&data);}if(old)DestroyIcon(old);}
LRESULT CALLBACK Procedure(HWND hwnd,UINT message,WPARAM w,LPARAM l) {
    if(message==ChangeColor){Update(static_cast<uint32_t>(w));return 0;}
    if(message==MakeOff){Update(0);return 0;}
    if(message==StopSource){DestroyWindow(hwnd);return 0;}
    if(message==Callback){InterlockedIncrement(&status->callbacks);status->lastW=w;status->lastL=l;UINT event=LOWORD(l);if(event==WM_RBUTTONUP||event==WM_CONTEXTMENU){InterlockedIncrement(&status->menus);HMENU menu=CreatePopupMenu();AppendMenuW(menu,MF_STRING,1,L"Test original menu");POINT point{};GetCursorPos(&point);SetForegroundWindow(hwnd);TrackPopupMenu(menu,TPM_RETURNCMD|TPM_NONOTIFY,point.x,point.y,0,hwnd,nullptr);DestroyMenu(menu);}return 0;}
    if(message==WM_QUERYENDSESSION)return TRUE;
    if(message==WM_ENDSESSION&&w){DestroyWindow(hwnd);return 0;}
    if(message==WM_DESTROY){for(bool guid:{false,true}){auto data=Data(guid,0);Shell_NotifyIconW(NIM_DELETE,&data);}PostQuitMessage(0);return 0;}
    return DefWindowProcW(hwnd,message,w,l);
}
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,LPWSTR,int) {
    std::wstring name=L"Local\\TrayAssistant.TestSource."+std::to_wstring(GetCurrentProcessId());map=CreateFileMappingW(INVALID_HANDLE_VALUE,nullptr,PAGE_READWRITE,0,sizeof(SourceStatus),name.c_str());status=static_cast<SourceStatus*>(MapViewOfFile(map,FILE_MAP_ALL_ACCESS,0,0,sizeof(SourceStatus)));if(!status)return 1;*status={};status->magic=0x54535431;status->pid=GetCurrentProcessId();
    WNDCLASSW cls{};cls.lpfnWndProc=Procedure;cls.hInstance=instance;cls.lpszClassName=L"TrayAssistant.TestSource";RegisterClassW(&cls);window=CreateWindowExW(0,cls.lpszClassName,L"TrayAssistant controlled source",0,0,0,0,0,nullptr,nullptr,instance,nullptr);status->hwnd=reinterpret_cast<uint64_t>(window);current=CreateColor(0xff248954);
    for(bool guid:{false,true}){auto data=Data(guid,NIF_ICON|NIF_TIP|NIF_MESSAGE);if(!Shell_NotifyIconW(NIM_ADD,&data))return 2;if(guid){data.uVersion=NOTIFYICON_VERSION_4;Shell_NotifyIconW(NIM_SETVERSION,&data);}}
    MSG message{};while(GetMessageW(&message,nullptr,0,0)>0){TranslateMessage(&message);DispatchMessageW(&message);}if(current)DestroyIcon(current);UnmapViewOfFile(status);CloseHandle(map);return 0;
}
