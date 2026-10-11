#include "shared.h"
#include "../platform/software_bitmap.h"
#include "../core/notify_patch.h"
#include <commctrl.h>
#include <objbase.h>
#include <algorithm>
#include <memory>
#include <unordered_map>

using namespace tray;
namespace {
HMODULE heldModule{};HANDLE mapping{},agentProcess{},stopWatch{};BridgeState* state{};HWND taskbar{};SymbolAdapter adapter;
volatile LONG callbacks{};
struct CallbackGuard{CallbackGuard(){InterlockedIncrement(&callbacks);}~CallbackGuard(){InterlockedDecrement(&callbacks);}};
uint64_t moduleBase{};bool ownOperation{};UINT commandMessage{};
struct Entry {
    NativeIconRecord record;IUnknown* original{};Frame source;FrameBaseline baseline;
    HICON replacement{};bool custom{};int visibility{-1};Placement originalPlacement{Placement::Overflow};uint32_t originalIndex{};
    ~Entry(){if(original)original->Release();if(replacement)DestroyIcon(replacement);}
};
std::unordered_map<std::string,std::unique_ptr<Entry>> entries;
template<class T> bool LocalRead(uint64_t address,T& out) {SIZE_T n{};return ValidReadRange(address,sizeof(T))&&ReadProcessMemory(GetCurrentProcess(),reinterpret_cast<void*>(address),&out,sizeof(T),&n)&&n==sizeof(T);}
uint64_t GetModel() {uint64_t root{},model{};if(!LocalRead(moduleBase+adapter.root,root)||!LocalRead(root+adapter.model,model))return 0;return model;}
bool Method(uint64_t object,size_t slot,uint32_t rva,void*& function) {uint64_t table{},target{};if(!LocalRead(object,table)||!LocalRead(table+slot*8,target)||target!=moduleBase+rva)return false;function=reinterpret_cast<void*>(target);return true;}
IUnknown* CurrentBitmap(const NativeIconRecord& record) {
    void* method{};if(!Method(record.recordAddress+0x10,8,adapter.getIcon,method))return nullptr;
    void* value{};using Fn=HRESULT(__stdcall*)(void*,void**);
    if(FAILED(reinterpret_cast<Fn>(method)(reinterpret_cast<void*>(record.recordAddress+0x10),&value)))return nullptr;
    return static_cast<IUnknown*>(value);
}
void RaiseChanged(uint64_t record) {using Fn=void(__cdecl*)(void*,const std::nullptr_t&,const std::nullptr_t&);const std::nullptr_t nothing=nullptr;reinterpret_cast<Fn>(moduleBase+0x2919c)(reinterpret_cast<void*>(record+0xf0),nothing,nothing);}
void RestoreBitmap(Entry& entry) {
    auto slot=reinterpret_cast<void**>(entry.record.recordAddress+adapter.imageObject);
    if(entry.original)entry.original->AddRef();
    auto previous=static_cast<IUnknown*>(InterlockedExchangePointer(slot,entry.original));
    if(previous)previous->Release();RaiseChanged(entry.record.recordAddress);
}
Frame SourceFrame(IUnknown* object) {
    if(object){auto frame=RasterizeSoftwareBitmap(object);if(frame.outcome.success)return std::move(frame.frame);return {};}
    Frame blank;blank.origin=FrameOrigin::SourceLive;blank.transparent=true;return blank;
}
void SaveOriginal(Entry& entry) {
    auto object=CurrentBitmap(entry.record);if(entry.original)entry.original->Release();entry.original=object;
    entry.source=SourceFrame(object);++entry.source.revision;
}
void DrawPolicy(Entry& entry) {
    auto choice=ChooseFrame(entry.source,entry.baseline,entry.custom&&entry.replacement);
    if(choice==FrameDecision::CustomIdle){using SetIcon=void(__fastcall*)(void*,HICON);reinterpret_cast<SetIcon>(moduleBase+adapter.setIcon)(reinterpret_cast<void*>(entry.record.recordAddress),entry.replacement);}
    else if(entry.custom)RestoreBitmap(entry);
}
void WriteFrame(BridgeIcon& out,const Frame& frame) {
    out.live=frame.origin==FrameOrigin::SourceLive;out.transparent=frame.transparent;out.fingerprint=frame.fingerprint;out.revision=frame.revision;
    if(!frame.width||!frame.height){out.width=out.height=0;return;}
    const uint32_t w=std::min<uint32_t>(64,frame.width),h=std::min<uint32_t>(64,frame.height);out.width=w;out.height=h;
    for(uint32_t y=0;y<h;++y)for(uint32_t x=0;x<w;++x)out.pixels[y*w+x]=frame.pixels[static_cast<size_t>(y)*frame.height/h*frame.width+static_cast<size_t>(x)*frame.width/w];
}
void Publish(bool refreshOriginal,const WireEvent* event=nullptr) {
    if(!state)return;auto snapshot=ReadSnapshot(adapter,GetCurrentProcessId(),state->epoch);if(!snapshot.complete){state->lastError=ERROR_RETRY;return;}
    std::vector<std::string> active;
    InterlockedIncrement(&state->snapshotSequence);state->count=0;
    for(auto& record:snapshot.records) {
        std::string key=IdentityKey(record.identity);active.push_back(key);auto found=entries.find(key);
        if(found==entries.end()) {auto item=std::make_unique<Entry>();item->record=record;item->originalPlacement=record.placement;SaveOriginal(*item);found=entries.emplace(key,std::move(item)).first;}
        auto& entry=*found->second;entry.record=record;
        bool sourceUpdate=event && ((event->identity.hasGuid && record.identity.hasGuid && IsEqualGUID(event->identity.guid,record.identity.guid)) || (!event->identity.hasGuid&&!record.identity.hasGuid&&event->identity.ownerHwnd==record.identity.ownerHwnd&&event->identity.uid==record.identity.uid));
        if(sourceUpdate && (event->patch.operation==NIM_ADD||(event->patch.suppliedFlags&NIF_ICON))) {SaveOriginal(entry);DrawPolicy(entry);}
        else if(refreshOriginal&&!entry.custom)SaveOriginal(entry);
        if(state->count>=MaxBridgeIcons)break;auto& row=state->icons[state->count++];row={};row.identity=record.identity;row.record=record.recordAddress;
        row.callback=record.callbackMessage;row.version=record.notifyVersion;row.placement=static_cast<uint32_t>(record.placement);row.custom=entry.custom;
        wcsncpy_s(row.executable,std::size(row.executable),record.executable.c_str(),_TRUNCATE);wcsncpy_s(row.tip,std::size(row.tip),record.tip.c_str(),_TRUNCATE);wcsncpy_s(row.windowClass,std::size(row.windowClass),record.windowClass.c_str(),_TRUNCATE);
        auto current=CurrentBitmap(record);auto frame=SourceFrame(current);if(current)current->Release();WriteFrame(row,frame);
        row.dynamic=ChooseFrame(entry.source,entry.baseline,entry.custom)==FrameDecision::NativeDynamic;
    }
    for(auto it=entries.begin();it!=entries.end();) {if(std::find(active.begin(),active.end(),it->first)==active.end())it=entries.erase(it);else ++it;}
    state->lastSnapshotTick=GetTickCount64();InterlockedIncrement(&state->snapshotSequence);
}
LRESULT CALLBACK Subclass(HWND,UINT,WPARAM,LPARAM,UINT_PTR,DWORD_PTR);
bool SetVisibility(const NativeIconRecord& record,bool hidden) {
    NOTIFYICONDATAW data{};data.cbSize=sizeof(data);data.hWnd=reinterpret_cast<HWND>(record.identity.ownerHwnd);data.uID=record.identity.uid;data.uFlags=NIF_STATE;data.dwStateMask=NIS_HIDDEN;data.dwState=hidden?NIS_HIDDEN:0;
    if(record.identity.hasGuid){data.uFlags|=NIF_GUID;data.guidItem=record.identity.guid;}
    return Shell_NotifyIconW(NIM_MODIFY,&data)!=FALSE;
}
void Detach() {
    if(!state)return;ownOperation=true;
    auto live=ReadSnapshot(adapter,GetCurrentProcessId(),state->epoch);
    for(auto& [key,entry]:entries){auto current=std::find_if(live.records.begin(),live.records.end(),[&](const auto& r){return SameIcon(r.identity,entry->record.identity);});if(current!=live.records.end()){entry->record=*current;if(entry->custom)RestoreBitmap(*entry);if(entry->visibility!=-1)SetVisibility(*current,entry->originalPlacement==Placement::Hidden);}}
    entries.clear();RemoveWindowSubclass(taskbar,Subclass,BridgeMagic);state->status=2;SetEvent(stopWatch);ownOperation=false;
}
void ExecuteCommand() {
    if(!state)return;auto cmd=state->command;auto start=GetTickCount64();DWORD error{};
    try {
        if(cmd.operation==BridgeOperation::Detach){Detach();InterlockedExchange(&state->replySequence,state->requestSequence);return;}
        if(cmd.operation==BridgeOperation::Refresh){Publish(true);InterlockedExchange(&state->replySequence,state->requestSequence);return;}
        auto snapshot=ReadSnapshot(adapter,GetCurrentProcessId(),state->epoch);
        auto match=std::find_if(snapshot.records.begin(),snapshot.records.end(),[&](const auto& r){return SameIcon(r.identity,cmd.identity);});
        if(!snapshot.complete||match==snapshot.records.end())error=ERROR_NOT_FOUND;
        else {
            auto it=entries.find(IdentityKey(match->identity));if(it==entries.end())error=ERROR_NOT_FOUND;
            else {auto& entry=*it->second;entry.record=*match;ownOperation=true;
                if(cmd.operation==BridgeOperation::Apply){if(entry.replacement)DestroyIcon(entry.replacement);entry.replacement=CopyIcon(reinterpret_cast<HICON>(cmd.replacementIcon));entry.custom=entry.replacement!=nullptr;entry.baseline={entry.source.origin==FrameOrigin::SourceLive&&!entry.source.transparent,entry.source.fingerprint,entry.source.width,entry.source.height};DrawPolicy(entry);if(!entry.custom)error=ERROR_INVALID_HANDLE;}
                else if(cmd.operation==BridgeOperation::Restore){RestoreBitmap(entry);entry.custom=false;if(entry.replacement){DestroyIcon(entry.replacement);entry.replacement=nullptr;}}
                else if(cmd.operation==BridgeOperation::Visibility){entry.visibility=cmd.visibility;if(!SetVisibility(*match,cmd.visibility==1))error=ERROR_INVALID_DATA;}
                else if(cmd.operation==BridgeOperation::Move){void* method{};uint64_t model=GetModel();if(!model||!Method(model+0x20,6,0x1309f0,method))error=ERROR_NOT_SUPPORTED;else{using Fn=HRESULT(__stdcall*)(void*,void*,int,UINT);auto hr=reinterpret_cast<Fn>(method)(reinterpret_cast<void*>(model+0x20),reinterpret_cast<void*>(match->recordAddress+0x10),cmd.location,cmd.index);if(FAILED(hr))error=hr;}}
                else if(cmd.operation==BridgeOperation::Menu){void* method{};if(!Method(match->recordAddress+0x10,14,0x14aa40,method))error=ERROR_NOT_SUPPORTED;else{struct Point{float x,y;};using Fn=HRESULT(__stdcall*)(void*,bool,Point);auto hr=reinterpret_cast<Fn>(method)(reinterpret_cast<void*>(match->recordAddress+0x10),false,{cmd.x,cmd.y});if(FAILED(hr))error=hr;}}
                else if(cmd.operation==BridgeOperation::Click){struct Point{float x,y;} point{cmd.x,cmd.y};using Fn=void(__fastcall*)(void*,int,const Point*);reinterpret_cast<Fn>(moduleBase+0x73d90)(reinterpret_cast<void*>(match->recordAddress),cmd.clickEvent,&point);}
                ownOperation=false;Publish(false);
            }
        }
    }catch(...){error=ERROR_INVALID_FUNCTION;ownOperation=false;}
    state->lastError=error;state->lastCommandMicros=(GetTickCount64()-start)*1000;InterlockedExchange(&state->replySequence,state->requestSequence);
}
LRESULT CALLBACK Subclass(HWND hwnd,UINT message,WPARAM wp,LPARAM lp,UINT_PTR,DWORD_PTR) {
    CallbackGuard guard;
    if(message==commandMessage){if(wp==1)Detach();else ExecuteCommand();return 1;}
    if(message==WM_NCDESTROY){Detach();return DefSubclassProc(hwnd,message,wp,lp);}
    if(message!=WM_COPYDATA||ownOperation||!state)return DefSubclassProc(hwnd,message,wp,lp);
    WireEvent event;auto data=reinterpret_cast<const COPYDATASTRUCT*>(lp);
    if(data && data->dwData==1 && data->lpData && data->cbData<=65536)event=DecodeWire({static_cast<const uint8_t*>(data->lpData),data->cbData});
    auto result=DefSubclassProc(hwnd,message,wp,lp);
    if(event.supported && result && !ownOperation){++state->events;Publish(false,&event);}
    return result;
}
DWORD WINAPI WatchAgent(void*) {
    HANDLE waits[]={agentProcess,stopWatch};auto result=WaitForMultipleObjects(2,waits,FALSE,INFINITE);
    if(result==WAIT_OBJECT_0)PostMessageW(taskbar,commandMessage,1,0);
    WaitForSingleObject(stopWatch,INFINITE);
    while(InterlockedCompareExchange(&callbacks,0,0))Sleep(1);
    Sleep(20);
    if(state){UnmapViewOfFile(state);state=nullptr;}if(mapping){CloseHandle(mapping);mapping=nullptr;}
    if(agentProcess){CloseHandle(agentProcess);agentProcess=nullptr;}if(stopWatch){CloseHandle(stopWatch);stopWatch=nullptr;}
    auto hold=heldModule;heldModule=nullptr;FreeLibraryAndExitThread(hold,0);
}
void Install(HWND hwnd) {
    if(state)return;DWORD session{};ProcessIdToSessionId(GetCurrentProcessId(),&session);mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,MappingName(session).c_str());if(!mapping)return;
    state=static_cast<BridgeState*>(MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(BridgeState)));
    if(!state||state->magic!=BridgeMagic||state->schema!=1){if(state)UnmapViewOfFile(state);CloseHandle(mapping);state=nullptr;mapping=nullptr;return;}
    agentProcess=OpenProcess(SYNCHRONIZE|PROCESS_QUERY_LIMITED_INFORMATION,FALSE,state->agentPid);if(!agentProcess){state->lastError=GetLastError();return;}
    adapter=Expand(state->adapter);moduleBase=reinterpret_cast<uint64_t>(GetModuleHandleW(L"Taskbar.dll"));taskbar=hwnd;commandMessage=RegisterWindowMessageW(CommandMessage);
    auto snapshot=ReadSnapshot(adapter,GetCurrentProcessId(),state->epoch);if(!snapshot.complete){state->lastError=ERROR_BAD_EXE_FORMAT;return;}
    GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,reinterpret_cast<LPCWSTR>(&Install),&heldModule);
    if(!SetWindowSubclass(hwnd,Subclass,BridgeMagic,0)){state->lastError=GetLastError();return;}
    stopWatch=CreateEventW(nullptr,TRUE,FALSE,nullptr);auto thread=CreateThread(nullptr,0,WatchAgent,nullptr,0,nullptr);if(thread)CloseHandle(thread);
    Publish(true);state->status=1;
}
}
extern "C" __declspec(dllexport) LRESULT CALLBACK TrayBootstrap(int code,WPARAM wp,LPARAM lp) {
    if(code==HC_ACTION&&lp){auto message=reinterpret_cast<CWPSTRUCT*>(lp);if(message->message==RegisterWindowMessageW(InstallMessage))try{Install(message->hwnd);}catch(...){if(state)state->lastError=ERROR_INVALID_FUNCTION;}}
    return CallNextHookEx(nullptr,code,wp,lp);
}
BOOL WINAPI DllMain(HINSTANCE h,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH)DisableThreadLibraryCalls(h);return TRUE;}
