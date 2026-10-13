#include "input_method_logo.h"
#include "../platform/input_profiles.h"
#undef GetCurrentTime
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.UI.Xaml.Controls.h>
#include <winrt/Windows.UI.Xaml.Data.h>
#include <winrt/Windows.UI.Xaml.Media.h>
#include <winrt/Windows.UI.Xaml.Media.Imaging.h>
#include <winrt/Windows.Storage.Streams.h>
#include <unordered_map>
#include <algorithm>
namespace tray {
namespace {
using namespace winrt::Windows::UI::Xaml;
using namespace winrt::Windows::UI::Xaml::Controls;
using namespace winrt::Windows::UI::Xaml::Media;
struct BufferBytes : IUnknown {
    virtual HRESULT __stdcall Buffer(BYTE**)=0;
};
struct Target {
    FrameworkElement source{nullptr}; Grid parent{nullptr}; Image overlay{nullptr};
    winrt::Windows::Foundation::IInspectable model{nullptr};
    winrt::Windows::Foundation::IInspectable opacity{nullptr}; Data::Binding binding{nullptr};
    int64_t sourceToken{},opacityToken{};bool watching{};
};
std::vector<InputMethodLogoHost> roots;
std::vector<Target> targets;
std::unordered_map<std::string,ImageSource> images;
std::string activeKey;
std::wstring activeId;
std::string checkpointKey;
ImageSource checkpoint{nullptr};
bool hasCheckpoint{};
bool updating{};DWORD logoError{};
bool SupportedHost() {
    auto base=reinterpret_cast<uint64_t>(GetModuleHandleW(L"Taskbar.View.dll"));if(!base)return false;
    auto read=[](uint64_t at,auto& value){SIZE_T bytes{};return ValidReadRange(at,sizeof(value)) &&
        ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(at),&value,sizeof(value),&bytes) && bytes==sizeof(value);};
    IMAGE_DOS_HEADER dos{};IMAGE_NT_HEADERS64 nt{};
    if(!read(base,dos) || dos.e_magic!=IMAGE_DOS_SIGNATURE || dos.e_lfanew<64 || dos.e_lfanew>4096 ||
       !read(base+dos.e_lfanew,nt) || nt.Signature!=IMAGE_NT_SIGNATURE)return false;
    const auto debug=nt.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG];
    if(!debug.VirtualAddress || debug.Size>4096 || uint64_t(debug.VirtualAddress)+debug.Size>nt.OptionalHeader.SizeOfImage)return false;
    const GUID expected{0xc2df1c2b,0x2369,0x46b7,{0xa8,0xab,0x9e,0x6b,0x7b,0x1a,0xb9,0x8b}};
    for(uint32_t offset=0;offset+sizeof(IMAGE_DEBUG_DIRECTORY)<=debug.Size;offset+=sizeof(IMAGE_DEBUG_DIRECTORY)) {
        IMAGE_DEBUG_DIRECTORY entry{};struct CodeView{uint32_t signature;GUID guid;uint32_t age;} cv{};
        if(read(base+debug.VirtualAddress+offset,entry) && entry.Type==IMAGE_DEBUG_TYPE_CODEVIEW && entry.SizeOfData>=sizeof(cv) &&
           uint64_t(entry.AddressOfRawData)+sizeof(cv)<=nt.OptionalHeader.SizeOfImage && read(base+entry.AddressOfRawData,cv) &&
           cv.signature==0x53445352 && cv.guid==expected && cv.age==1)return true;
    }
    return false;
}
template<class T> bool Read(uint64_t address,T& value) {
    SIZE_T n{};return ValidReadRange(address,sizeof(value)) &&
        ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(address),&value,sizeof(value),&n) && n==sizeof(value);
}
bool TrustedFunction(void* address) {
    MEMORY_BASIC_INFORMATION info{};HMODULE module{};wchar_t path[32768]{},windows[32768]{};
    if(!VirtualQuery(address,&info,sizeof(info)) || info.State!=MEM_COMMIT || info.Type!=MEM_IMAGE ||
       !(info.Protect&(PAGE_EXECUTE|PAGE_EXECUTE_READ|PAGE_EXECUTE_READWRITE|PAGE_EXECUTE_WRITECOPY)) ||
       !GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                         reinterpret_cast<wchar_t*>(address),&module))return false;
    auto length=GetWindowsDirectoryW(windows,std::size(windows));
    return length && length<std::size(windows) && GetModuleFileNameW(module,path,std::size(path)) &&
           !_wcsnicmp(path,windows,length) && path[length]==L'\\';
}
std::wstring ShellProfileId(const winrt::Windows::Foundation::IInspectable& data) {
    if(!data || !SupportedHost())return {};
    const GUID languageId{0x6b255fee,0xee7e,0x5e70,{0xa9,0x4d,0x14,0x99,0xd5,0x8b,0xcf,0xdc}};
    winrt::com_ptr<IUnknown> marker;
    if(FAILED(reinterpret_cast<IUnknown*>(winrt::get_abi(data))->QueryInterface(languageId,marker.put_void())))return {};
    auto address=reinterpret_cast<uint64_t>(marker.get());uint64_t table{},slot0{},slot4{};
    auto base=reinterpret_cast<uint64_t>(GetModuleHandleW(L"Taskbar.View.dll"));
    if(!Read(address,table) || table!=base+0x9064f8 || !Read(table,slot0) || slot0!=base+0x31200 ||
       !Read(table+4*8,slot4) || slot4!=base+0x311e30 || address<0x18)return {};
    // The matched producer thunks adjust the marker interface by -0x18.
    // Follow the exact Shell getter: its selected entry precedes the profile manager.
    uint64_t selected{};
    if(Read(address-0x18+0x128,selected) && selected) {
        uint64_t text[4]{};
        if(!Read(selected+0x10,text) || text[2]>128 || text[3]<text[2] || text[3]>4096)return {};
        if(!text[2])return {};
        auto buffer=text[3]<8?selected+0x10:text[0];
        if(!ValidReadRange(buffer,text[2]*2))return {};
        std::wstring id(text[2],L'\0');SIZE_T n{};
        if(!ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(buffer),id.data(),id.size()*2,&n) || n!=id.size()*2)return {};
        // A selected plain keyboard layout must remain a layout. Never fall back
        // to the manager's previous TIP and put its logo on a different entry.
        return id;
    }
    uint64_t manager{},managerTable{},query{};
    if(!Read(address-0x18+0x78,manager) || !manager || !Read(manager,managerTable) ||
       !Read(managerTable,query) || !TrustedFunction(reinterpret_cast<void*>(query)))return {};
    const GUID managerId{0xb43d96bf,0xcc74,0x52ec,{0x9e,0xe6,0xf9,0x69,0x86,0x96,0x8c,0xf1}};
    winrt::com_ptr<IUnknown> profileManager;
    if(FAILED(reinterpret_cast<IUnknown*>(manager)->QueryInterface(managerId,profileManager.put_void())))return {};
    auto tablePointer=*reinterpret_cast<void***>(profileManager.get());
    if(!TrustedFunction(tablePointer[13]))return {};
    using GetProfile=HRESULT(__stdcall*)(void*,void**);
    winrt::Windows::Foundation::IInspectable profile{nullptr};
    if(FAILED(reinterpret_cast<GetProfile>(tablePointer[13])(profileManager.get(),winrt::put_abi(profile))) || !profile)return {};
    if(winrt::get_class_name(profile)!=L"WindowsUdk.UI.Input.Text.TextInputProfile")return {};
    auto profileTable=*reinterpret_cast<void***>(winrt::get_abi(profile));
    if(!TrustedFunction(profileTable[6]))return {};
    using GetId=HRESULT(__stdcall*)(void*,void**);
    winrt::hstring id;
    if(FAILED(reinterpret_cast<GetId>(profileTable[6])(winrt::get_abi(profile),winrt::put_abi(id))))return {};
    return std::wstring(id);
}
std::string ShellProfileKey(const winrt::Windows::Foundation::IInspectable& model,std::wstring* raw=nullptr) {
    auto id=ShellProfileId(model);if(raw)*raw=id;
    InputProfileId profile;
    return ParseInputTipId(Utf8(id),profile)?InputProfileKey(profile):std::string{};
}
void Restore(Target& t, bool visible=true) {
    if(t.watching) {
        t.source.UnregisterPropertyChangedCallback(UIElement::OpacityProperty(),t.opacityToken);
        if(t.source.try_as<Image>())t.source.UnregisterPropertyChangedCallback(Image::SourceProperty(),t.sourceToken);
        t.watching=false;
    }
    if(t.overlay) {
        uint32_t at{};if(t.parent.Children().IndexOf(t.overlay,at))t.parent.Children().RemoveAt(at);
        t.overlay=nullptr;
        if(visible) {
            if(t.binding)Data::BindingOperations::SetBinding(t.source,UIElement::OpacityProperty(),t.binding);
            else if(t.opacity==DependencyProperty::UnsetValue())t.source.ClearValue(UIElement::OpacityProperty());
            else t.source.SetValue(UIElement::OpacityProperty(),t.opacity);
        }
    }
}
void Apply(Target& t, const ImageSource& image) {
    if(!image) {Restore(t);return;}
    if(!t.overlay) {
        t.opacity=t.source.ReadLocalValue(UIElement::OpacityProperty());
        t.binding=nullptr;
        if(auto b=t.source.GetBindingExpression(UIElement::OpacityProperty()))t.binding=b.ParentBinding();
        t.overlay=Image();t.overlay.Name(L"TrayAssistantInputMethodLogo");t.overlay.IsHitTestVisible(false);
        t.overlay.Stretch(Stretch::Uniform);t.overlay.Margin(t.source.Margin());
        t.overlay.HorizontalAlignment(t.source.HorizontalAlignment());t.overlay.VerticalAlignment(t.source.VerticalAlignment());
        Grid::SetRow(t.overlay,Grid::GetRow(t.source));Grid::SetColumn(t.overlay,Grid::GetColumn(t.source));
        Grid::SetRowSpan(t.overlay,Grid::GetRowSpan(t.source));Grid::SetColumnSpan(t.overlay,Grid::GetColumnSpan(t.source));
        t.parent.Children().Append(t.overlay);
        t.opacityToken=t.source.RegisterPropertyChangedCallback(UIElement::OpacityProperty(),[](const DependencyObject& sender,const DependencyProperty&){
            if(updating)return;try{
                auto source=sender.as<FrameworkElement>();
                for(auto& target:targets)if(target.source==source) {
                    target.opacity=source.ReadLocalValue(UIElement::OpacityProperty());target.binding=nullptr;
                    if(auto binding=source.GetBindingExpression(UIElement::OpacityProperty()))target.binding=binding.ParentBinding();
                }
                TickInputMethodLogos();
            }catch(...){}
        });
        if(t.source.try_as<Image>())t.sourceToken=t.source.RegisterPropertyChangedCallback(Image::SourceProperty(),[](const auto&,const auto&){
            if(updating)return;try{TickInputMethodLogos();}catch(...){}
        });
        t.watching=true;
    }
    if(t.overlay.Source()!=image)t.overlay.Source(image);
    auto width=t.source.ActualWidth(),height=t.source.ActualHeight();
    if(width>0 && t.overlay.Width()!=width)t.overlay.Width(width);
    if(height>0 && t.overlay.Height()!=height)t.overlay.Height(height);
    if(t.source.Opacity()!=0)t.source.Opacity(0);
}
std::vector<Target> Locate() {
    std::vector<Target> found;
    for(const auto& host:roots) {
        std::vector<DependencyObject> nodes{host.element};
        while(!nodes.empty()) {
            auto node=nodes.back();nodes.pop_back();
            if(auto leaf=node.try_as<FrameworkElement>();leaf && leaf.Name()!=L"TrayAssistantInputMethodLogo" &&
                (leaf.try_as<Image>() || leaf.try_as<TextBlock>() || leaf.try_as<FontIcon>())) {
                if(auto parent=VisualTreeHelper::GetParent(leaf).try_as<Grid>()) {
                    auto old=std::find_if(targets.begin(),targets.end(),[&](const auto& t){return t.source==leaf;});
                    if(old!=targets.end())found.push_back(*old);else{Target t;t.source=leaf;t.parent=parent;found.push_back(std::move(t));}
                    found.back().model=host.model;
                    continue;
                }
            }
            auto count=VisualTreeHelper::GetChildrenCount(node);
            for(int i=0;i<count;++i) {
                auto child=VisualTreeHelper::GetChild(node,i);
                if(auto element=child.try_as<FrameworkElement>();element && element.Name()==L"TrayAssistantInputMethodLogo")continue;
                nodes.push_back(child);
            }
        }
    }
    return found;
}
}
void DiscoverInputMethodLogos(const std::vector<InputMethodLogoHost>& elements) {
    roots=elements;TickInputMethodLogos();
}
void TickInputMethodLogos() {
    if(updating || (images.empty() && targets.empty()))return;
    updating=true;struct End{~End(){updating=false;}}end;
    try {
        activeKey=roots.empty()?std::string{}:ShellProfileKey(roots.front().model,&activeId);
        if(images.empty()) {
            for(auto& target:targets)Restore(target);
            targets.clear();logoError=0;return;
        }
        auto next=Locate();
        for(auto& t:targets)if(std::none_of(next.begin(),next.end(),[&](const auto& n){return n.source==t.source;}))Restore(t);
        targets=std::move(next);
        logoError=0;
        for(auto& t:targets) {
            auto key=ShellProfileKey(t.model);auto policy=images.find(key);
            Apply(t,policy==images.end()?ImageSource{nullptr}:policy->second);
        }
        if(!images.empty() && activeId.empty())logoError=ERROR_NOT_READY;
        if(images.contains(activeKey) && targets.empty())logoError=ERROR_NOT_FOUND;
    }catch(const winrt::hresult_error& e){logoError=static_cast<DWORD>(e.code());}
    catch(...){logoError=ERROR_INVALID_DATA;}
}
bool HasInputMethodLogos(){return !images.empty();}
bool SetInputMethodLogo(const std::string& key, HICON icon) {
    InputProfileId id;if(!ParseInputProfileKey(key,id) || !SupportedHost())return false;
    ImageSource source{nullptr};
    if(icon) {
        auto copy=CopyIcon(icon);if(!copy)return false;
        auto frame=RasterizeIcon(copy,FrameOrigin::Custom);DestroyIcon(copy);
        if(!frame.outcome.success)return false;
        auto width=frame.frame.PixelWidth(),height=frame.frame.PixelHeight();
        Imaging::WriteableBitmap bitmap(width,height);
        const GUID bufferId{0x905a0fef,0xbc53,0x11df,{0x8c,0x49,0x00,0x1e,0x4f,0xc6,0x86,0xda}};
        winrt::com_ptr<BufferBytes> bytes;
        winrt::check_hresult(reinterpret_cast<IUnknown*>(winrt::get_abi(bitmap.PixelBuffer()))->QueryInterface(bufferId,bytes.put_void()));
        BYTE* data{};winrt::check_hresult(bytes->Buffer(&data));memcpy(data,frame.frame.pixels.data(),frame.frame.pixels.size()*4);
        bitmap.Invalidate();source=bitmap;
    }
    if(source)images.insert_or_assign(key,source);else images.erase(key);
    TickInputMethodLogos();
    return !source || logoError==0;
}
bool UpdateInputMethodLogo(const std::string& key,HICON icon,int phase) {
    if(phase==2) {if(hasCheckpoint && checkpointKey==key){checkpoint=nullptr;checkpointKey.clear();hasCheckpoint=false;}return true;}
    if(phase==3) {
        if(hasCheckpoint && checkpointKey==key) {
            if(checkpoint)images.insert_or_assign(key,checkpoint);else images.erase(key);
            checkpoint=nullptr;checkpointKey.clear();hasCheckpoint=false;TickInputMethodLogos();
        }
        return true;
    }
    if(phase==1) {
        auto old=images.find(key);checkpoint=old==images.end()?ImageSource{nullptr}:old->second;
        checkpointKey=key;hasCheckpoint=true;
        try{if(SetInputMethodLogo(key,icon))return true;}catch(...){UpdateInputMethodLogo(key,nullptr,3);throw;}
        UpdateInputMethodLogo(key,nullptr,3);return false;
    }
    return SetInputMethodLogo(key,icon);
}
void ReadInputMethodLogoStatus(SystemControl& row) {
    activeKey=roots.empty()?std::string{}:ShellProfileKey(roots.front().model,&activeId);
    row.logoTargets=static_cast<uint32_t>(targets.size());row.logoApplied=0;row.logoError=logoError;
    for(const auto& t:targets)if(t.overlay)++row.logoApplied;
    wcsncpy_s(row.inputProfileKey,Wide(activeKey).c_str(),_TRUNCATE);
    wcsncpy_s(row.inputProfileId,activeId.c_str(),_TRUNCATE);
}
void RestoreInputMethodLogos(bool restore) {
    updating=true;
    for(auto& t:targets)try{Restore(t,restore);}catch(...){}
    targets.clear();roots.clear();images.clear();activeKey.clear();activeId.clear();
    checkpoint=nullptr;checkpointKey.clear();hasCheckpoint=false;logoError=0;updating=false;
}
}
