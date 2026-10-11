#include "settings.h"
#include <fstream>
#include <objbase.h>
#include <gdiplus.h>
namespace tray {
Json RuleJson(const IconRule& r){wchar_t guid[40]{};StringFromGUID2(r.selector.guid,guid,40);return {{"selector",{{"executable",Utf8(r.selector.executable)},{"windowClass",Utf8(r.selector.windowClass)},{"uid",r.selector.uid},{"hasGuid",r.selector.hasGuid},{"guid",Utf8(guid)}}},{"visibility",r.visibility},{"location",r.location},{"order",r.order},{"asset",Utf8(r.asset)},{"tip",Utf8(r.tip)},{"enabled",r.enabled},{"menuVisible",r.menuVisible},{"alwaysShow",r.alwaysShow},{"inactiveSeconds",r.inactiveSeconds},{"unusedDays",r.unusedDays},{"hotkey",r.hotkey},{"swapButtons",r.swapButtons},{"baselineFingerprint",r.baselineFingerprint},{"baselineWidth",r.baselineWidth},{"baselineHeight",r.baselineHeight},{"assetIndex",r.assetIndex}};}
IconRule ParseRule(const Json& j){
    IconRule r;auto s=j.at("selector");r.selector.executable=Wide(s.at("executable").get<std::string>());r.selector.windowClass=Wide(s.value("windowClass",std::string{}));r.selector.uid=s.value("uid",0u);r.selector.hasGuid=s.value("hasGuid",false);
    if(FAILED(CLSIDFromString(Wide(s.value("guid",std::string("{00000000-0000-0000-0000-000000000000}"))).c_str(),&r.selector.guid)))throw std::runtime_error("Invalid rule GUID");
    r.visibility=j.value("visibility",-1);r.location=j.value("location",-1);r.order=j.value("order",-1);r.asset=Wide(j.value("asset",std::string{}));r.tip=Wide(j.value("tip",std::string{}));r.enabled=j.value("enabled",true);r.menuVisible=j.value("menuVisible",true);r.alwaysShow=j.value("alwaysShow",false);r.inactiveSeconds=j.value("inactiveSeconds",0u);r.unusedDays=j.value("unusedDays",0u);r.hotkey=j.value("hotkey",0u);r.swapButtons=j.value("swapButtons",false);r.baselineFingerprint=j.value("baselineFingerprint",uint64_t{});r.baselineWidth=j.value("baselineWidth",0u);r.baselineHeight=j.value("baselineHeight",0u);r.assetIndex=j.value("assetIndex",0u);
    if(r.selector.executable.empty()||r.selector.executable.size()>32767||r.tip.size()>127||r.visibility<-1||r.visibility>1||r.location<-1||r.location>1||r.order<-1||r.order>=256||r.inactiveSeconds>86400||r.unusedDays>3650)throw std::runtime_error("Rule values out of bounds");return r;
}
Json DefaultSettings(){return {{"schema",1},{"revision",0},{"theme","system"},{"startup",false},{"ownIcon",{{"visible",true},{"asset",""},{"tip","托盘图标助手"}}},{"rules",Json::array()},{"menu",{{"programNames",true},{"hoverDelayMs",500},{"swapButtons",false}}},{"taskbar",{{"opacity",255},{"hideStart",false}}},{"safeMode",false},{"uiTopmost",false},{"uiOpacity",255}};}
bool ValidateSettings(const Json& j){try{if(!j.is_object()||j.at("schema")!=1||!j.at("rules").is_array()||j.at("rules").size()>512)return false;auto theme=j.value("theme",std::string("system"));if(theme!="system"&&theme!="light"&&theme!="dark")return false;for(auto& r:j.at("rules"))ParseRule(r);if(!j.at("ownIcon").at("visible").is_boolean())return false;return true;}catch(...){return false;}}
static Json ReadFile(const std::filesystem::path& path){if(!std::filesystem::exists(path)||std::filesystem::file_size(path)>4*1024*1024)throw std::runtime_error("Settings missing/oversized");std::ifstream f(path);Json j;f>>j;if(!ValidateSettings(j))throw std::runtime_error("Invalid settings");return j;}
bool SaveSettings(const std::filesystem::path& path,const Json& settings){
    if(!ValidateSettings(settings))return false;try{std::filesystem::create_directories(path.parent_path());auto tmp=path;tmp+=L"."+std::to_wstring(GetCurrentProcessId())+L".tmp";auto text=settings.dump(2);
    HANDLE file=CreateFileW(tmp.c_str(),GENERIC_WRITE,0,nullptr,CREATE_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);if(file==INVALID_HANDLE_VALUE)return false;DWORD written{};bool ok=WriteFile(file,text.data(),static_cast<DWORD>(text.size()),&written,nullptr)&&written==text.size()&&FlushFileBuffers(file);CloseHandle(file);if(!ok){DeleteFileW(tmp.c_str());return false;}
    auto backup=path;backup+=L".bak";try{ReadFile(path);CopyFileW(path.c_str(),backup.c_str(),FALSE);}catch(...){}
    if(!MoveFileExW(tmp.c_str(),path.c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH)){DeleteFileW(tmp.c_str());return false;}return true;
    }catch(...){return false;}
}
Json LoadSettings(const std::filesystem::path& path,bool& recovered){recovered=false;try{return ReadFile(path);}catch(...){auto backup=path;backup+=L".bak";try{auto j=ReadFile(backup);recovered=true;return j;}catch(...){recovered=std::filesystem::exists(path);return DefaultSettings();}}}
HICON LoadAsset(const std::filesystem::path& path,int index){
    if(path.empty())return nullptr;try{if(!std::filesystem::is_regular_file(path)||std::filesystem::file_size(path)>32*1024*1024)return nullptr;auto ext=path.extension().wstring();for(auto& c:ext)c=static_cast<wchar_t>(towlower(c));
    if(ext==L".ico")return static_cast<HICON>(LoadImageW(nullptr,path.c_str(),IMAGE_ICON,32,32,LR_LOADFROMFILE));
    if(ext==L".exe"||ext==L".dll"){HICON large{},small{};ExtractIconExW(path.c_str(),index,&large,&small,1);if(large)DestroyIcon(large);return small;}
    if(ext!=L".png"&&ext!=L".bmp"&&ext!=L".jpg"&&ext!=L".jpeg")return nullptr;
    ULONG_PTR token{};Gdiplus::GdiplusStartupInput input;if(Gdiplus::GdiplusStartup(&token,&input,nullptr)!=Gdiplus::Ok)return nullptr;HICON result{};
    {Gdiplus::Bitmap source(path.c_str());if(source.GetLastStatus()==Gdiplus::Ok&&source.GetWidth()<=2048&&source.GetHeight()<=2048){Gdiplus::Bitmap sized(32,32,PixelFormat32bppARGB);Gdiplus::Graphics g(&sized);g.SetInterpolationMode(Gdiplus::InterpolationModeHighQualityBicubic);g.Clear(Gdiplus::Color(0));g.DrawImage(&source,0,0,32,32);sized.GetHICON(&result);}}
    Gdiplus::GdiplusShutdown(token);return result;}catch(...){return nullptr;}
}
}
