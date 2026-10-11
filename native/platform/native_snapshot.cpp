#include "native_snapshot.h"
#include <tlhelp32.h>
#include <objbase.h>
#include <fstream>
#include <algorithm>
#include <cstring>

namespace tray {
std::string Utf8(const std::wstring& value) {
    int n=WideCharToMultiByte(CP_UTF8,0,value.data(),static_cast<int>(value.size()),nullptr,0,nullptr,nullptr);
    std::string result(n,0); if(n) WideCharToMultiByte(CP_UTF8,0,value.data(),static_cast<int>(value.size()),result.data(),n,nullptr,nullptr); return result;
}
std::wstring Wide(const std::string& value) {
    int n=MultiByteToWideChar(CP_UTF8,0,value.data(),static_cast<int>(value.size()),nullptr,0);
    std::wstring result(n,0); if(n) MultiByteToWideChar(CP_UTF8,0,value.data(),static_cast<int>(value.size()),result.data(),n); return result;
}
SymbolAdapter LoadAdapter(const std::filesystem::path& file) {
    std::ifstream stream(file); nlohmann::json document; stream>>document; auto j=document.at("adapters").at(0); SymbolAdapter a;
    auto pdbText=L"{"+Wide(j.at("pdb").get<std::string>())+L"}";
    if(FAILED(CLSIDFromString(pdbText.c_str(),&a.image.pdb))) throw std::runtime_error("Invalid adapter PDB identity");
    a.image.age=j.at("age"); a.image.timestamp=j.at("timestamp"); a.image.imageSize=j.at("imageSize");
    #define FIELD(f) a.f=j.at(#f).get<uint32_t>()
    FIELD(root); FIELD(model); FIELD(manager); FIELD(promoted); FIELD(overflow); FIELD(itemVtable); FIELD(vectorAt); FIELD(vectorSize);
    FIELD(vectorBegin); FIELD(vectorEnd); FIELD(window); FIELD(uid); FIELD(guid); FIELD(version); FIELD(callback); FIELD(tip); FIELD(imageObject);
    FIELD(cachedIcon); FIELD(setIcon); FIELD(getIcon);
    #undef FIELD
    // Internal callers have a build-specific ABI. A matching PE alone must not
    // allow a damaged manifest to substitute a different function or layout.
    const uint32_t expected[]={0x2430e8,0x260,0x240,0x18,0x20,0x1c79a0,0x77270,0x7d790,0x58,0x60,0x50,0x58,0x5c,0x94,0x98,0xa0,0xc8,0x130,0x3fd98,0x287b0};
    const uint32_t actual[]={a.root,a.model,a.manager,a.promoted,a.overflow,a.itemVtable,a.vectorAt,a.vectorSize,a.vectorBegin,a.vectorEnd,a.window,a.uid,a.guid,a.version,a.callback,a.tip,a.imageObject,a.cachedIcon,a.setIcon,a.getIcon};
    if(!std::equal(std::begin(expected),std::end(expected),std::begin(actual)))throw std::runtime_error("Adapter layout is not an implemented ABI");
    for(const auto& check:j.at("checks")) {
        std::string text=check.at("bytes"); if(text.size()!=32) throw std::runtime_error("Invalid adapter code check");
        std::array<uint8_t,16> bytes{}; for(size_t i=0;i<16;++i) bytes[i]=static_cast<uint8_t>(std::stoul(text.substr(i*2,2),nullptr,16));
        uint32_t rva=check.at("rva"); if(rva>=a.image.imageSize || 16>a.image.imageSize-rva) throw std::runtime_error("Code check out of image");
        a.checks.emplace_back(rva,bytes);
    }
    if(a.checks.size()<3 || a.root+8>a.image.imageSize) throw std::runtime_error("Incomplete adapter");
    return a;
}
uint32_t ExplorerPid() { DWORD id{}; GetWindowThreadProcessId(FindWindowW(L"Shell_TrayWnd",nullptr),&id); return id; }
class Reader {
    HANDLE process_{};
public:
    explicit Reader(uint32_t pid):process_(OpenProcess(PROCESS_VM_READ|PROCESS_QUERY_INFORMATION,FALSE,pid)){}
    ~Reader(){if(process_)CloseHandle(process_);}
    bool bytes(uint64_t address,void* output,size_t length) const { SIZE_T got{}; return process_ && ValidReadRange(address,length) && ReadProcessMemory(process_,reinterpret_cast<void*>(address),output,length,&got) && got==length; }
    template<class T> bool read(uint64_t address,T& result) const {return bytes(address,&result,sizeof(result));}
    bool vector(uint64_t address,std::vector<uint64_t>& items) const {
        std::array<uint64_t,2> first{},last{}; if(!read(address,first) || !ValidVectorBounds(first[0],first[1])) return false;
        items.resize(static_cast<size_t>((first[1]-first[0])/8));
        if(!items.empty() && !bytes(first[0],items.data(),items.size()*8)) return false;
        return read(address,last) && first==last;
    }
    bool image(uint64_t base,const SymbolAdapter& a) const {
        IMAGE_DOS_HEADER dos{}; IMAGE_NT_HEADERS64 nt{};
        if(!read(base,dos)||dos.e_magic!=IMAGE_DOS_SIGNATURE||dos.e_lfanew<64||dos.e_lfanew>4096||!read(base+dos.e_lfanew,nt))return false;
        if(nt.Signature!=IMAGE_NT_SIGNATURE||nt.FileHeader.Machine!=IMAGE_FILE_MACHINE_AMD64||nt.OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR64_MAGIC)return false;
        if(nt.FileHeader.TimeDateStamp!=a.image.timestamp||nt.OptionalHeader.SizeOfImage!=a.image.imageSize)return false;
        const auto debug=nt.OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_DEBUG]; if(debug.Size>4096||debug.VirtualAddress+debug.Size>a.image.imageSize)return false;
        bool matched=false;
        for(size_t offset=0;offset+sizeof(IMAGE_DEBUG_DIRECTORY)<=debug.Size;offset+=sizeof(IMAGE_DEBUG_DIRECTORY)) {
            IMAGE_DEBUG_DIRECTORY d{}; if(!read(base+debug.VirtualAddress+offset,d))return false;
            struct CodeView {uint32_t signature;GUID guid;uint32_t age;} cv{};
            if(d.Type==IMAGE_DEBUG_TYPE_CODEVIEW && d.SizeOfData>=sizeof(cv) && d.AddressOfRawData+sizeof(cv)<=a.image.imageSize && read(base+d.AddressOfRawData,cv))
                if(cv.signature==0x53445352 && IsEqualGUID(cv.guid,a.image.pdb) && cv.age==a.image.age)matched=true;
        }
        if(!matched)return false;
        for(const auto& [rva,expected]:a.checks) {std::array<uint8_t,16> actual{}; if(!read(base+rva,actual)||actual!=expected)return false;}
        return true;
    }
    bool collection(uint64_t object,uint64_t base,const SymbolAdapter& a,std::vector<uint64_t>& items) const {
        for(int adjustment=-32;adjustment<=64;adjustment+=8) {
            uint64_t candidate=object+adjustment,vt{},at{},size{};
            if(!read(candidate,vt)||!read(vt+48,at)||!read(vt+56,size))continue;
            if(at==base+a.vectorAt && size==base+a.vectorSize)return vector(candidate+a.vectorBegin,items);
        }
        return false;
    }
    std::wstring tip(uint64_t value) const {
        uint64_t buffer{}; if(!value||!read(value+16,buffer))return {};
        std::wstring text; for(size_t i=0;i<127;++i) {wchar_t c{};if(!read(buffer+i*2,c)||!c)break;text+=c;} return text;
    }
};
static uint64_t ModuleBase(uint32_t pid) {
    HANDLE snapshot=CreateToolhelp32Snapshot(TH32CS_SNAPMODULE,pid); if(snapshot==INVALID_HANDLE_VALUE)return 0;
    MODULEENTRY32W module{};module.dwSize=sizeof(module);uint64_t base{};
    if(Module32FirstW(snapshot,&module))do{if(!_wcsicmp(module.szModule,L"Taskbar.dll")){base=reinterpret_cast<uint64_t>(module.modBaseAddr);break;}}while(Module32NextW(snapshot,&module));
    CloseHandle(snapshot);return base;
}
static uint64_t OwnerInfo(uint32_t pid,std::wstring& executable) {
    HANDLE p=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid); if(!p)return 0;
    std::vector<wchar_t> path(32768);DWORD length=static_cast<DWORD>(path.size()); if(QueryFullProcessImageNameW(p,0,path.data(),&length))executable.assign(path.data(),length);
    FILETIME created{},exited{},kernel{},user{};uint64_t generation{};
    if(GetProcessTimes(p,&created,&exited,&kernel,&user))generation=(static_cast<uint64_t>(created.dwHighDateTime)<<32)|created.dwLowDateTime;
    CloseHandle(p);return generation;
}
SnapshotResult ReadSnapshot(const SymbolAdapter& a,uint32_t pid,uint64_t epoch,bool includeCaches) {
    SnapshotResult result;result.shellEpoch=epoch;result.outcome.detail="Native tray snapshot unavailable or changed during read";
    Reader memory(pid);uint64_t base=ModuleBase(pid),root{},model{},manager{},promoted{},overflow{};
    if(!base || !memory.image(base,a)){result.outcome.detail="Installed Taskbar image does not match verified adapter";return result;}
    if(!memory.read(base+a.root,root)||!memory.read(root+a.model,model)||!memory.read(model+a.manager,manager)||!memory.read(manager+a.promoted,promoted)||!memory.read(manager+a.overflow,overflow))return result;
    std::vector<uint64_t> records,pinned,folded;
    if(!memory.vector(manager,records)||!memory.collection(promoted,base,a,pinned)||!memory.collection(overflow,base,a,folded))return result;
    const size_t recordSize=std::max({a.cachedIcon+8,a.imageObject+8,a.guid+16,a.callback+4}); if(recordSize>1024)return result;
    for(uint64_t address:records) {
        std::vector<uint8_t> first(recordSize),last(recordSize); if(!memory.bytes(address,first.data(),first.size()))return result;
        auto field=[&]<class T>(uint32_t offset,T& target){if(offset+sizeof(T)>first.size())throw std::runtime_error("Invalid record layout");memcpy(&target,first.data()+offset,sizeof(T));};
        uint64_t vt{},tip{};NativeIconRecord item;field(0,vt);if(vt!=base+a.itemVtable)return result;
        field(a.window,item.identity.ownerHwnd);field(a.uid,item.identity.uid);field(a.guid,item.identity.guid);
        field(a.callback,item.callbackMessage);field(a.version,item.notifyVersion);field(a.tip,tip);field(a.imageObject,item.imageObject);field(a.cachedIcon,item.cachedIcon);
        item.identity.hasGuid=!IsEqualGUID(item.identity.guid,GUID{});item.identity.shellEpoch=epoch;
        DWORD owner{};auto window=reinterpret_cast<HWND>(item.identity.ownerHwnd);DWORD thread=GetWindowThreadProcessId(window,&owner);if(!thread||!owner)continue;
        item.identity.ownerPid=owner;item.identity.ownerCreatedAt=OwnerInfo(owner,item.executable);item.recordAddress=address;item.tip=memory.tip(tip);
        wchar_t cls[256]{};GetClassNameW(window,cls,256);item.windowClass=cls;
        auto contains=[&](const std::vector<uint64_t>& list){return std::any_of(list.begin(),list.end(),[&](uint64_t abi){return abi>=address && abi-address<=64;});};
        item.placement=contains(pinned)?Placement::Promoted:contains(folded)?Placement::Overflow:Placement::Hidden;
        const auto& collection=item.placement==Placement::Promoted?pinned:folded;
        auto position=std::find_if(collection.begin(),collection.end(),[&](uint64_t abi){return abi>=address&&abi-address<=64;});
        item.nativeIndex=static_cast<uint32_t>(position-collection.begin());
        item.sourceState=item.placement==Placement::Hidden?NIS_HIDDEN:0;
        if(includeCaches){auto cached=RasterizeIcon(reinterpret_cast<HICON>(item.cachedIcon),FrameOrigin::CachedOriginal);if(cached.outcome.success)item.cachedOriginal=std::move(cached.frame);}
        DWORD after{};if(!memory.bytes(address,last.data(),last.size())||first!=last||GetWindowThreadProcessId(window,&after)!=thread||after!=owner)return result;
        result.records.push_back(std::move(item));
    }
    std::vector<uint64_t> endRecords,endPinned,endFolded;uint64_t endRoot{},endModel{},endManager{},endPromoted{},endOverflow{};
    if(!memory.read(base+a.root,endRoot)||endRoot!=root||!memory.read(root+a.model,endModel)||endModel!=model||!memory.read(model+a.manager,endManager)||endManager!=manager||!memory.read(manager+a.promoted,endPromoted)||endPromoted!=promoted||!memory.read(manager+a.overflow,endOverflow)||endOverflow!=overflow||!memory.vector(manager,endRecords)||endRecords!=records||!memory.collection(promoted,base,a,endPinned)||endPinned!=pinned||!memory.collection(overflow,base,a,endFolded)||endFolded!=folded)return result;
    result.complete=true;result.outcome.success=true;result.outcome.detail.clear();return result;
}
nlohmann::json SnapshotJson(const SnapshotResult& snapshot,bool includePixels) {
    nlohmann::json result={{"success",snapshot.outcome.success},{"complete",snapshot.complete},{"detail",snapshot.outcome.detail},{"epoch",snapshot.shellEpoch},{"icons",nlohmann::json::array()}};
    for(const auto& icon:snapshot.records) {
        const auto& id=icon.identity;wchar_t guid[40]{};StringFromGUID2(id.guid,guid,40);
        nlohmann::json j={{"key",IdentityKey(id)},{"pid",id.ownerPid},{"created",id.ownerCreatedAt},{"hwnd",id.ownerHwnd},{"uid",id.uid},{"hasGuid",id.hasGuid},{"guid",Utf8(guid)},{"callback",icon.callbackMessage},{"version",icon.notifyVersion},{"placement",static_cast<int>(icon.placement)},{"executable",Utf8(icon.executable)},{"windowClass",Utf8(icon.windowClass)},{"tip",Utf8(icon.tip)},{"record",icon.recordAddress},{"imageObject",icon.imageObject},{"cacheWidth",icon.cachedOriginal.width},{"cacheHeight",icon.cachedOriginal.height},{"cacheFingerprint",icon.cachedOriginal.fingerprint},{"currentFrameAvailable",!icon.currentFrame.pixels.empty()}};
        if(includePixels)j["cachedPixels"]=icon.cachedOriginal.pixels;result["icons"].push_back(std::move(j));
    }
    return result;
}
}
