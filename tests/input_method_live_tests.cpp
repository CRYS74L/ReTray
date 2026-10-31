#include "../native/shell/bridge_client.h"
#include "../native/platform/input_profiles.h"
#include <msctf.h>
#include <atomic>
#include <fstream>
#include <thread>
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
void PumpFor(DWORD milliseconds) {
    auto until=GetTickCount64()+milliseconds;
    do {
        MSG message{};
        while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)) {
            TranslateMessage(&message);DispatchMessageW(&message);
        }
        MsgWaitForMultipleObjectsEx(0,nullptr,5,QS_ALLINPUT,MWMO_INPUTAVAILABLE);
    }while(GetTickCount64()<until);
}
struct InputSession {
    ITfThreadMgr* thread{};
    ITfInputProcessorProfileMgr* profiles{};
    ITfInputProcessorProfiles* languages{};
    TF_INPUTPROCESSORPROFILE original{};
    HWND window{},previous{GetForegroundWindow()};
    bool activated{},saved{};
    ~InputSession() {
        if(saved) {
            languages->ChangeCurrentLanguage(original.langid);
            profiles->ActivateProfile(original.dwProfileType,original.langid,original.clsid,
                                      original.guidProfile,original.hkl,0);
            PumpFor(150);
        }
        if(window)DestroyWindow(window);
        if(activated)thread->Deactivate();
        if(languages)languages->Release();
        if(profiles)profiles->Release();
        if(thread)thread->Release();
        if(previous)SetForegroundWindow(previous);
    }
    void Open() {
        CHECK(SUCCEEDED(CoCreateInstance(CLSID_TF_ThreadMgr,nullptr,CLSCTX_INPROC_SERVER,
                                        IID_ITfThreadMgr,reinterpret_cast<void**>(&thread))));
        TfClientId client{};CHECK(SUCCEEDED(thread->Activate(&client)));activated=true;
        CHECK(SUCCEEDED(CoCreateInstance(CLSID_TF_InputProcessorProfiles,nullptr,CLSCTX_INPROC_SERVER,
                                        IID_ITfInputProcessorProfileMgr,reinterpret_cast<void**>(&profiles))));
        CHECK(SUCCEEDED(profiles->QueryInterface(IID_ITfInputProcessorProfiles,reinterpret_cast<void**>(&languages))));
        CHECK(SUCCEEDED(profiles->GetActiveProfile(GUID_TFCAT_TIP_KEYBOARD,&original)));saved=true;
        WNDCLASSW cls{};cls.lpfnWndProc=DefWindowProcW;cls.hInstance=GetModuleHandleW(nullptr);
        cls.lpszClassName=L"ReTray.InputMethodAcceptance";CHECK(RegisterClassW(&cls));
        window=CreateWindowExW(0,cls.lpszClassName,L"ReTray input method switching acceptance",WS_OVERLAPPEDWINDOW|WS_VISIBLE,
                               80,80,520,180,nullptr,nullptr,GetModuleHandleW(nullptr),nullptr);
        auto editor=CreateWindowExW(0,L"EDIT",L"",WS_CHILD|WS_VISIBLE|ES_MULTILINE,0,0,500,130,window,nullptr,cls.hInstance,nullptr);
        CHECK(editor);
        CHECK(window);SetForegroundWindow(window);
        if(GetForegroundWindow()!=window) {
            std::puts("Waiting for the acceptance window to be activated...");std::fflush(stdout);
            auto until=GetTickCount64()+90000;
            while(GetForegroundWindow()!=window && GetTickCount64()<until)PumpFor(50);
        }
        CHECK(GetForegroundWindow()==window);SetFocus(editor);PumpFor(300);
    }
    void Switch(const InputProfileId& id) {
        if(GetForegroundWindow()!=window) {
            SetForegroundWindow(window);
            auto until=GetTickCount64()+3000;
            while(GetForegroundWindow()!=window && GetTickCount64()<until)PumpFor(20);
        }
        CHECK(GetForegroundWindow()==window);
        CHECK(SUCCEEDED(languages->ChangeCurrentLanguage(id.language)));
        auto hr=profiles->ActivateProfile(TF_PROFILETYPE_INPUTPROCESSOR,id.language,id.clsid,id.profile,nullptr,0);
        if(hr!=S_OK)std::fprintf(stderr,"ActivateProfile returned 0x%08lx\n",static_cast<unsigned long>(hr));
        CHECK(hr==S_OK);
    }
};
struct PixelCapture {
    static constexpr int side=48,width=96,logoX=72;
    struct Sample { uint64_t micros{};HWND foreground{};uint32_t pixels[width*side]{}; };
    std::vector<Sample> samples;
    std::atomic<bool> stop{},failed{},ready{};
    std::thread worker;
    static uint32_t Color(const Sample& sample) {
        auto color=sample.pixels[side/2*width+logoX]&0xffffff;
        for(int y=side/2-4;y<=side/2+4;++y)
            for(int x=logoX-4;x<=logoX+4;++x)
                if((sample.pixels[y*width+x]&0xffffff)!=color)return UINT32_MAX;
        return color;
    }
    static uint64_t StateGlyph(const Sample& sample) {
        uint64_t hash=14695981039346656037ull;
        for(int y=12;y<36;++y)for(int x=16;x<36;++x) {
            auto p=sample.pixels[y*width+x];
            bool ink=(p&255)>180 && ((p>>8)&255)>180 && ((p>>16)&255)>180;
            hash=(hash^static_cast<uint64_t>(ink))*1099511628211ull;
        }
        return hash;
    }
    ~PixelCapture(){Finish();}
    void Start(int x,int y) {
        worker=std::thread([this,x,y] {
            auto screen=GetDC(nullptr),dc=CreateCompatibleDC(screen);
            BITMAPINFO info{};info.bmiHeader.biSize=sizeof(BITMAPINFOHEADER);
            info.bmiHeader.biWidth=width;info.bmiHeader.biHeight=-side;
            info.bmiHeader.biPlanes=1;info.bmiHeader.biBitCount=32;
            void* pixels{};auto bitmap=CreateDIBSection(screen,&info,DIB_RGB_COLORS,&pixels,nullptr,0);
            if(!screen || !dc || !bitmap)failed=true;
            auto old=bitmap?SelectObject(dc,bitmap):nullptr;
            while(!stop && !failed) {
                if(!BitBlt(dc,0,0,width,side,screen,x-logoX,y-side/2,SRCCOPY)){failed=true;break;}
                GdiFlush();Sample frame;frame.micros=PerformanceMicros();frame.foreground=GetForegroundWindow();
                memcpy(frame.pixels,pixels,sizeof(frame.pixels));samples.push_back(frame);ready=true;Sleep(8);
            }
            if(old)SelectObject(dc,old);
            if(bitmap)DeleteObject(bitmap);
            if(dc)DeleteDC(dc);
            if(screen)ReleaseDC(nullptr,screen);
        });
        while(!ready && !failed)Sleep(1);
        CHECK(!failed);
    }
    void Finish(){stop=true;if(worker.joinable())worker.join();}
};
void SwitchAcceptance(BridgeClient& client,int x,int y,const std::filesystem::path& output) {
    InputSession session;session.Open();
    auto profiles=EnumerateInputProfiles();CHECK(profiles.size()>=2);
    // Use the current foreground thread's exact TSF identity, not its language alone.
    auto current=ActiveInputProfileKey();
    auto first=std::find_if(profiles.begin(),profiles.end(),[&](const auto& p){return p.key==current;});
    CHECK(first!=profiles.end());
    auto second=std::find_if(profiles.begin(),profiles.end(),[&](const auto& p){return p.key!=first->key;});
    CHECK(second!=profiles.end());
    std::printf("Switch profiles: %s / %s\n",Utf8(first->name).c_str(),Utf8(second->name).c_str());std::fflush(stdout);
    struct RestorePolicies {
        BridgeClient& client;std::vector<std::string> keys;
        ~RestorePolicies(){for(const auto& key:keys){BridgeCommand cmd;cmd.operation=BridgeOperation::InputMethodLogo;
            cmd.index=0;wcsncpy_s(cmd.inputProfileKey,Wide(key).c_str(),_TRUNCATE);client.Command(cmd);}}
    } restore{client,{first->key,second->key}};
    auto set=[&](const std::string& key,uint32_t color){
        auto icon=ColorIcon(color);CHECK(icon);BridgeCommand cmd;cmd.operation=BridgeOperation::InputMethodLogo;cmd.index=0;
        cmd.replacementIcon=reinterpret_cast<uint64_t>(icon);wcsncpy_s(cmd.inputProfileKey,Wide(key).c_str(),_TRUNCATE);
        bool ok=client.Command(cmd);DestroyIcon(icon);CHECK(ok);
    };
    set(first->key,0xff0000ff);set(second->key,0xffff0000);
    CHECK(WaitPixel(x,y,RGB(0,0,255)));
    PixelCapture capture;capture.Start(x,y);
    std::vector<std::pair<uint64_t,uint32_t>> switches;
    std::vector<uint64_t> started;
    for(int i=0;i<10;++i) {
        const auto& next=i%2==0?*second:*first;
        started.push_back(PerformanceMicros());
        session.Switch(next.id);CHECK(ActiveInputProfileKey()==next.key);
        switches.emplace_back(PerformanceMicros(),i%2==0?0x00ff0000:0x000000ff);
        PumpFor(600);
    }
    capture.Finish();CHECK(!capture.failed);
    std::filesystem::create_directories(output);
    std::ofstream frames(output/L"frames.bgra",std::ios::binary),times(output/L"frames.csv"),activations(output/L"switches.csv");
    times<<"frame,micros,rgb,foreground,state_glyph\n";activations<<"activated_micros,expected,lag_after_native_glyph_micros\n";
    size_t nativeFrames{},focusLost{};
    for(size_t i=0;i<capture.samples.size();++i) {
        const auto& sample=capture.samples[i];auto color=PixelCapture::Color(sample);
        frames.write(reinterpret_cast<const char*>(sample.pixels),sizeof(sample.pixels));
        times<<i<<','<<sample.micros<<','<<color<<','<<reinterpret_cast<uintptr_t>(sample.foreground)<<','<<PixelCapture::StateGlyph(sample)<<'\n';
        if(sample.foreground!=session.window)++focusLost;
        if(color!=0x000000ff && color!=0x00ff0000)++nativeFrames;
    }
    frames.close();times.close();CHECK(frames);CHECK(times);
    auto firstGlyph=PixelCapture::StateGlyph(capture.samples.front());
    auto settled=std::find_if(capture.samples.rbegin(),capture.samples.rend(),[&](const auto& sample){return sample.micros<started[1];});
    CHECK(settled!=capture.samples.rend());auto secondGlyph=PixelCapture::StateGlyph(*settled);CHECK(firstGlyph!=secondGlyph);
    uint64_t maximum{};size_t missed{};
    for(size_t i=0;i<switches.size();++i) {
        const auto& [at,expected]=switches[i];auto end=i+1<started.size()?started[i+1]:UINT64_MAX;
        auto frame=std::find_if(capture.samples.begin(),capture.samples.end(),[&](const auto& sample){
            return sample.micros>=started[i] && sample.micros<end && PixelCapture::Color(sample)==expected;
        });
        auto native=std::find_if(capture.samples.begin(),capture.samples.end(),[&](const auto& sample){
            return sample.micros>=started[i] && sample.micros<end && PixelCapture::StateGlyph(sample)==(i%2==0?secondGlyph:firstGlyph);
        });
        if(frame==capture.samples.end() || native==capture.samples.end()){activations<<at<<','<<expected<<",-1\n";++missed;continue;}
        // Compare the two real taskbar visuals. TSF activation can finish before
        // Windows updates either native indicator, especially on a cold switch.
        auto elapsed=frame->micros>native->micros?frame->micros-native->micros:0;maximum=std::max(maximum,elapsed);
        activations<<at<<','<<expected<<','<<elapsed<<'\n';
        if(elapsed>150000)++missed;
    }
    activations.close();CHECK(activations);
    std::printf("Switch capture: %zu frames, %zu native/blank frames, %zu foreground changes, %zu late/missed switches, maximum %.2f ms\n",
                capture.samples.size(),nativeFrames,focusLost,missed,maximum/1000.0);
    CHECK(focusLost==0);CHECK(nativeFrames==0);CHECK(missed==0);
    // No read/refresh command is sent during capture: polling must not hide a missed notification.
    BridgeCommand read;read.operation=BridgeOperation::SystemRead;CHECK(client.Command(read));
    for(uint32_t i=0;i<client.shared->systemCount;++i) {
        const auto& row=client.shared->systemControls[i];
        if(row.id==9)CHECK(row.logoApplied==0);
        if(row.id==3){CHECK(Utf8(row.inputProfileKey)==first->key);CHECK(row.logoError==0);CHECK(row.logoApplied==1);}
    }
    std::puts("PASS ten foreground input method switches keep custom pixels in every captured frame without refresh polling");
    BridgeCommand original;original.operation=BridgeOperation::InputMethodLogo;original.index=0;
    wcsncpy_s(original.inputProfileKey,Wide(second->key).c_str(),_TRUNCATE);CHECK(client.Command(original));
    for(int i=0;i<2;++i) {
        session.Switch(second->id);PumpFor(100);
        auto dc=GetDC(nullptr);auto pixel=GetPixel(dc,x,y);ReleaseDC(nullptr,dc);
        CHECK(pixel!=RGB(0,0,255) && pixel!=RGB(255,0,0));
        session.Switch(first->id);PumpFor(100);CHECK(WaitPixel(x,y,RGB(0,0,255)));
    }
    wcsncpy_s(original.inputProfileKey,Wide(first->key).c_str(),_TRUNCATE);CHECK(client.Command(original));
    PumpFor(100);CHECK(client.Command(read));
    for(uint32_t i=0;i<client.shared->systemCount;++i)if(client.shared->systemControls[i].id==3)CHECK(client.shared->systemControls[i].logoApplied==0);
    std::puts("PASS original-mode profile stays native, configured profile resumes, and clearing all policies releases overlays");
}
int wmain(int argc,wchar_t** argv) {
    if(argc!=5 && !(argc==7 && std::wstring(argv[5])==L"--switch"))return 2;
    CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);
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
        std::puts("PASS original mode removes the overlay while keeping native source");
        if(argc==7)SwitchAcceptance(client,x,y,argv[6]);
        result=0;
    }catch(const std::exception& e){std::fprintf(stderr,"FAIL live input method: %s\n",e.what());}
    client.Detach();CoUninitialize();return result;
}
