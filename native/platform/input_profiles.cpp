#include "input_profiles.h"
#include <msctf.h>
#include <oleauto.h>
#include <stdexcept>
#include <set>

namespace tray {
namespace {
template<class T> struct ComOwner {
    ComOwner()=default;ComOwner(const ComOwner&)=delete;
    ComOwner(ComOwner&& other) noexcept:value(other.value){other.value=nullptr;}
    T* value{}; ~ComOwner(){if(value)value->Release();}
    T* operator->() const {return value;}
};
ComOwner<ITfInputProcessorProfileMgr> Profiles() {
    ComOwner<ITfInputProcessorProfileMgr> result;
    auto hr=CoCreateInstance(CLSID_TF_InputProcessorProfiles,nullptr,CLSCTX_INPROC_SERVER,
                            IID_ITfInputProcessorProfileMgr,reinterpret_cast<void**>(&result.value));
    if(FAILED(hr)) throw std::runtime_error("无法读取输入法配置");
    return result;
}
bool ReadIcon(HKEY root, REGSAM view, const std::wstring& path, std::wstring& file, int& index) {
    HKEY key{}; if(RegOpenKeyExW(root,path.c_str(),0,KEY_READ|view,&key)!=ERROR_SUCCESS)return false;
    wchar_t buffer[32768]{};DWORD bytes=sizeof(buffer),type{};
    auto error=RegQueryValueExW(key,L"IconFile",nullptr,&type,reinterpret_cast<BYTE*>(buffer),&bytes);
    DWORD number{},size=sizeof(number);
    auto indexResult=RegQueryValueExW(key,L"IconIndex",nullptr,&type,reinterpret_cast<BYTE*>(&number),&size);
    RegCloseKey(key);
    if(error!=ERROR_SUCCESS || !buffer[0] || bytes>=sizeof(buffer))return false;
    wchar_t expanded[32768]{};
    auto count=ExpandEnvironmentStringsW(buffer,expanded,std::size(expanded));
    if(!count || count>std::size(expanded))return false;
    file=expanded;index=indexResult==ERROR_SUCCESS?static_cast<int>(number):0;
    return true;
}
}
std::vector<InputProfile> EnumerateInputProfiles() {
    auto manager=Profiles(); ComOwner<IEnumTfInputProcessorProfiles> enumerator;
    if(FAILED(manager->EnumProfiles(0,&enumerator.value)))throw std::runtime_error("无法枚举输入法配置");
    ComOwner<ITfInputProcessorProfiles> descriptions;
    manager->QueryInterface(IID_ITfInputProcessorProfiles,reinterpret_cast<void**>(&descriptions.value));
    std::vector<InputProfile> result; std::set<std::string> seen;
    TF_INPUTPROCESSORPROFILE p{};ULONG fetched{};
    while(enumerator->Next(1,&p,&fetched)==S_OK && fetched) {
        if(p.dwProfileType!=TF_PROFILETYPE_INPUTPROCESSOR || !(p.dwFlags&2) || p.catid!=GUID_TFCAT_TIP_KEYBOARD)continue;
        InputProfile row;row.id={p.langid,p.clsid,p.guidProfile};row.key=InputProfileKey(row.id);row.active=(p.dwFlags&1)!=0;
        if(!seen.insert(row.key).second)continue;
        BSTR name{};
        if(descriptions.value && SUCCEEDED(descriptions->GetLanguageProfileDescription(p.clsid,p.langid,p.guidProfile,&name)) && name) {
            row.name.assign(name,SysStringLen(name));SysFreeString(name);
        }
        if(row.name.empty())row.name=L"输入法";
        wchar_t clsid[40]{},profile[40]{},language[16]{};
        StringFromGUID2(p.clsid,clsid,40);StringFromGUID2(p.guidProfile,profile,40);
        swprintf(language,std::size(language),L"0x%08x",p.langid);
        auto path=std::wstring(L"Software\\Microsoft\\CTF\\TIP\\")+clsid+L"\\LanguageProfile\\"+language+L"\\"+profile;
        for(auto root:{HKEY_CURRENT_USER,HKEY_LOCAL_MACHINE}) {
            if(ReadIcon(root,KEY_WOW64_64KEY,path,row.iconFile,row.iconIndex) ||
               ReadIcon(root,KEY_WOW64_32KEY,path,row.iconFile,row.iconIndex))break;
        }
        result.push_back(std::move(row));
    }
    return result;
}
std::string ActiveInputProfileKey() {
    try {
        auto manager=Profiles();TF_INPUTPROCESSORPROFILE p{};
        if(FAILED(manager->GetActiveProfile(GUID_TFCAT_TIP_KEYBOARD,&p)) || p.dwProfileType!=TF_PROFILETYPE_INPUTPROCESSOR)return {};
        return InputProfileKey({p.langid,p.clsid,p.guidProfile});
    } catch (...) {return {};}
}
}
