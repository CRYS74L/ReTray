#include "../native/shell/bridge_client.h"
#include "../native/platform/input_profiles.h"
#include <cstdio>
#include <stdexcept>
using namespace tray;
#define CHECK(x) do{if(!(x))throw std::runtime_error(#x);}while(0)
HICON ColorIcon(uint32_t color) {
    BITMAPINFO info{};info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);info.bmiHeader.biWidth=32;
    info.bmiHeader.biHeight=-32;info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;
    void* pixels{};auto bitmap=CreateDIBSection(nullptr,&info,DIB_RGB_COLORS,&pixels,nullptr,0);
    if(!bitmap)return nullptr;
    std::fill_n(static_cast<uint32_t*>(pixels),1024,color);
    uint8_t zeros[128]{};auto mask=CreateBitmap(32,32,1,1,zeros);
    ICONINFO iconInfo{TRUE,0,0,mask,bitmap};auto icon=CreateIconIndirect(&iconInfo);
    DeleteObject(mask);DeleteObject(bitmap);return icon;
}
bool WaitPixel(int x,int y,COLORREF color) {
    for(int attempt=0;attempt<60;++attempt) {
        auto dc=GetDC(nullptr);auto pixel=GetPixel(dc,x,y);ReleaseDC(nullptr,dc);
        if(pixel==color)return true;Sleep(25);
    }
    return false;
}
int wmain(int argc,wchar_t** argv) {
    if(argc!=5)return 2;CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
    int result=1;BridgeClient client;
    try {
        auto adapter=LoadAdapter(argv[1]);CHECK(client.Attach(adapter,argv[2]));
        int x=_wtoi(argv[3]),y=_wtoi(argv[4]);
        auto read=[&](){BridgeCommand cmd;cmd.operation=BridgeOperation::SystemRead;CHECK(client.Command(cmd));
            SystemControl logo{};for(uint32_t i=0;i<client.shared->systemCount;++i){auto row=client.shared->systemControls[i];
                if(row.id==3)logo=row;if(row.id==9)CHECK(row.logoApplied==0);}
            return logo;};
        auto logo=read();auto key=Utf8(logo.inputProfileKey);InputProfileId id;CHECK(ParseInputProfileKey(key,id));
        std::printf("Shell profile: %s\n",key.c_str());
        auto send=[&](const std::string& profile,HICON icon,int phase){BridgeCommand cmd;cmd.operation=BridgeOperation::InputMethodLogo;
            cmd.index=phase;cmd.replacementIcon=reinterpret_cast<uint64_t>(icon);wcsncpy_s(cmd.inputProfileKey,Wide(profile).c_str(),_TRUNCATE);
            CHECK(client.Command(cmd));};
        auto blue=ColorIcon(0xff0000ff);CHECK(blue);send(key,blue,0);DestroyIcon(blue);
        CHECK(WaitPixel(x,y,RGB(0,0,255)));CHECK(read().logoApplied==1);
        std::puts("PASS real taskbar logo replacement leaves state control untouched");
        auto red=ColorIcon(0xffff0000);CHECK(red);send(key,red,1);DestroyIcon(red);
        CHECK(WaitPixel(x,y,RGB(255,0,0)));send(key,nullptr,3);
        CHECK(WaitPixel(x,y,RGB(0,0,255)));CHECK(read().logoApplied==1);
        std::puts("PASS real ImageSource checkpoint rolls back after original HICON destruction");
        for(const auto& profile:EnumerateInputProfiles())if(profile.key!=key) {
            auto other=ColorIcon(0xffff0000);CHECK(other);send(profile.key,other,0);DestroyIcon(other);
            CHECK(WaitPixel(x,y,RGB(0,0,255)));CHECK(Utf8(read().inputProfileKey)==key);send(profile.key,nullptr,0);
            std::puts("PASS inactive input method policy does not replace the active logo");break;
        }
        send(key,nullptr,0);CHECK(read().logoApplied==0);
        std::puts("PASS original mode removes the overlay while keeping native source");result=0;
    }catch(const std::exception& e){std::fprintf(stderr,"FAIL live input method: %s\n",e.what());}
    client.Detach();CoUninitialize();return result;
}
