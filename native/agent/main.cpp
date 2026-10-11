#include "settings.h"
#include "../shell/bridge_client.h"
#include "../core/notify_patch.h"
#include "../core/protection.h"
#include "../platform/user_security.h"
#include <shlobj.h>
#include <shobjidl.h>
#include <fstream>
#include <memory>
#include <unordered_map>
#include <algorithm>
#include <chrono>
#include <deque>
#include <mutex>
using namespace tray;
namespace {
constexpr UINT OwnCallback=WM_APP+101,OpenMessage=WM_APP+102;
HWND window{};HANDLE stopEvent{},pipeThread{},workerThread{},workEvent{},endingEvent{},singleton{};DWORD session{};SessionMachine lifecycle;BridgeClient bridge;SymbolAdapter adapter;
std::filesystem::path root,dataRoot,settingsPath;Json settings;bool recovered{},ownAdded{},safeMode{};std::atomic<bool> stopping{};HICON ownImage{};
std::unordered_map<std::string,IconRule> rules;std::unordered_map<std::string,std::string> applied;std::unordered_map<std::string,Json> outcomes;
std::vector<BridgeIcon> rows;IconCatalog catalog;LONG seenSequence{-1};uint64_t configRevision{};
struct Request{Json input,output;HANDLE event{CreateEventW(nullptr,TRUE,FALSE,nullptr)};~Request(){CloseHandle(event);}};
std::mutex queueMutex,menuMutex;std::deque<std::shared_ptr<Request>> requests;
std::vector<NativeIconRecord> menuRows;Json menuSettings=Json::object();
void Enqueue(const std::shared_ptr<Request>& request){{std::lock_guard lock(queueMutex);requests.push_back(request);}SetEvent(workEvent);}
void PostJob(const std::string& method,Json data=Json::object()){auto request=std::make_shared<Request>();request->input={{"schema",1},{"method",method},{"data",std::move(data)}};Enqueue(request);}
struct HiddenWindow{HWND hwnd{};DWORD pid{};uint64_t created{};WINDOWPLACEMENT placement{sizeof(WINDOWPLACEMENT),0,0,{},{},{}};std::wstring title;};
std::vector<HiddenWindow> hidden;
bool locked{};std::atomic<bool> menuLocked{};uint64_t nextUnlock{};uint32_t failedUnlocks{};
PasswordDigest Digest(){auto& j=settings.at("protection").at("digest");PasswordDigest d;d.salt=j.at("salt").get<std::array<uint8_t,16>>();d.hash=j.at("hash").get<std::array<uint8_t,32>>();d.iterations=j.at("iterations");return d;}
uint64_t Generation(DWORD pid){HANDLE p=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid);if(!p)return 0;FILETIME c{},e{},k{},u{};GetProcessTimes(p,&c,&e,&k,&u);CloseHandle(p);return (uint64_t(c.dwHighDateTime)<<32)|c.dwLowDateTime;}
uint64_t Now(){return static_cast<uint64_t>(std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::system_clock::now().time_since_epoch()).count());}
NativeIconRecord Record(const BridgeIcon& row){NativeIconRecord r;r.identity=row.identity;r.executable=row.executable;r.windowClass=row.windowClass;r.tip=row.tip;r.notifyVersion=row.version;r.callbackMessage=row.callback;r.placement=static_cast<Placement>(row.placement);r.nativeIndex=row.index;r.sourceRevision=row.revision;r.currentFrame.fingerprint=row.sourceFingerprint;return r;}
Json Result(bool ok,const std::string& detail={},DWORD error=0){return {{"success",ok},{"detail",detail},{"errorCode",error},{"revision",configRevision}};}
bool Persist(){settings["revision"]=++configRevision;if(!SaveSettings(settingsPath,settings)){--configRevision;return false;}return true;}
void LoadRules(){rules.clear();for(auto& j:settings["rules"]){auto rule=ParseRule(j);rules[PersistentKey(rule.selector)]=std::move(rule);}applied.clear();}
void OpenUI(){
    auto name=Wide(settings.value("uiActivation",std::string{}));if(name.empty())return;
    IApplicationActivationManager* manager{};if(SUCCEEDED(CoCreateInstance(CLSID_ApplicationActivationManager,nullptr,CLSCTX_LOCAL_SERVER,IID_IApplicationActivationManager,reinterpret_cast<void**>(&manager)))){DWORD pid{};manager->ActivateApplication(name.c_str(),nullptr,AO_NONE,&pid);manager->Release();}
}
bool SetOwnIcon(){
    auto policy=settings["ownIcon"];if(ownImage){DestroyIcon(ownImage);ownImage=nullptr;}bool visible=policy.value("visible",true);
    NOTIFYICONDATAW d{};d.cbSize=sizeof(d);d.hWnd=window;d.uID=1;
    if(!visible){if(ownAdded)Shell_NotifyIconW(NIM_DELETE,&d);ownAdded=false;return true;}
    auto asset=Wide(policy.value("asset",std::string{}));ownImage=asset.empty()?LoadAsset(root/L"Assets"/L"AppIcon.ico"):LoadAsset(asset);
    if(!ownImage&&asset.empty())ownImage=CopyIcon(LoadIconW(nullptr,IDI_APPLICATION));if(!ownImage)return false;
    d.uFlags=NIF_ICON|NIF_MESSAGE|NIF_TIP;d.uCallbackMessage=OwnCallback;d.hIcon=ownImage;auto tip=Wide(policy.value("tip",std::string("托盘图标助手")));wcsncpy_s(d.szTip,std::size(d.szTip),tip.c_str(),_TRUNCATE);
    bool ok=Shell_NotifyIconW(ownAdded?NIM_MODIFY:NIM_ADD,&d)!=FALSE;if(ok){ownAdded=true;d.uVersion=NOTIFYICON_VERSION_4;Shell_NotifyIconW(NIM_SETVERSION,&d);}return ok;
}
bool SetStartup(bool enabled){HKEY key{};if(RegCreateKeyExW(HKEY_CURRENT_USER,L"Software\\Microsoft\\Windows\\CurrentVersion\\Run",0,nullptr,0,KEY_SET_VALUE,nullptr,&key,nullptr)!=ERROR_SUCCESS)return false;
    std::wstring command=L"\""+(root/L"TrayAssistant.Agent.exe").wstring()+L"\" --background";LONG code=enabled?RegSetValueExW(key,L"TrayIconAssistant",0,REG_SZ,reinterpret_cast<const BYTE*>(command.c_str()),static_cast<DWORD>((command.size()+1)*2)):RegDeleteValueW(key,L"TrayIconAssistant");RegCloseKey(key);return code==ERROR_SUCCESS||(!enabled&&code==ERROR_FILE_NOT_FOUND);}
void RestoreWindows(){for(auto& item:hidden){DWORD pid{};GetWindowThreadProcessId(item.hwnd,&pid);if(IsWindow(item.hwnd)&&pid==item.pid&&Generation(pid)==item.created){SetWindowPlacement(item.hwnd,&item.placement);ShowWindowAsync(item.hwnd,item.placement.showCmd);}}hidden.clear();}
bool MinimizeWindow(HWND target){if(!IsWindow(target)||target==window||!IsWindowVisible(target)||GetAncestor(target,GA_ROOT)!=target)return false;DWORD pid{};GetWindowThreadProcessId(target,&pid);if(pid==GetCurrentProcessId()||pid==ExplorerPid())return false;HiddenWindow item;item.hwnd=target;item.pid=pid;item.created=Generation(pid);if(!item.created)return false;wchar_t title[512]{};GetWindowTextW(target,title,512);item.title=title;GetWindowPlacement(target,&item.placement);hidden.push_back(item);ShowWindowAsync(target,SW_HIDE);return true;}
void ApplyRule(const BridgeIcon& row,const IconRule* rule){
    if(!ShouldRecover(lifecycle)||safeMode)return;
    auto key=IdentityKey(row.identity);auto r=Record(row);int visibility=rule&&rule->enabled?rule->visibility:-1;
    if(rule&&IsInactive(*rule,catalog.items[key].activity,Now()))visibility=1;
    std::string stamp=std::to_string(configRevision)+":"+std::to_string(visibility);if(applied[key]==stamp)return;
    bool success=true;DWORD error{};auto send=[&](BridgeCommand cmd){cmd.identity=row.identity;bool ok=bridge.Command(cmd);if(!ok){success=false;error=bridge.shared?bridge.shared->lastError:ERROR_NOT_READY;}return ok;};
    if(rule&&rule->enabled&&!rule->asset.empty()){auto image=LoadAsset(rule->asset,rule->assetIndex);if(image){BridgeCommand cmd;cmd.operation=BridgeOperation::Apply;cmd.replacementIcon=reinterpret_cast<uint64_t>(image);cmd.suppliedBaseline=true;cmd.baselineFingerprint=rule->baselineFingerprint;cmd.baselineWidth=rule->baselineWidth;cmd.baselineHeight=rule->baselineHeight;send(cmd);DestroyIcon(image);}else{success=false;error=ERROR_FILE_NOT_FOUND;}}
    else if(row.managed){BridgeCommand cmd;cmd.operation=BridgeOperation::Restore;send(cmd);}
    if(visibility!=-1){BridgeCommand cmd;cmd.operation=BridgeOperation::Visibility;cmd.visibility=visibility;send(cmd);}
    if(rule&&rule->enabled){BridgeCommand cmd;cmd.operation=BridgeOperation::Tip;wcsncpy_s(cmd.tip,std::size(cmd.tip),rule->tip.c_str(),_TRUNCATE);send(cmd);if(rule->location!=-1&&rule->order!=-1){cmd={};cmd.operation=BridgeOperation::Move;cmd.location=rule->location;cmd.index=rule->order;send(cmd);}}
    outcomes[key]=Result(success,success?"已生效":"规则已保存，应用失败",error);if(success)applied[key]=stamp;
}
void Refresh(){
    if(!ShouldRecover(lifecycle)||stopping)return;
    if(!bridge.Alive()){
        SetOperationalState(lifecycle,SessionState::WaitingForShell);if(!ExplorerPid())return;bridge.Reset();
        if(!bridge.Attach(adapter,root/L"TrayAssistant.ShellBridge.dll"))return;seenSequence=-1;applied.clear();SetOperationalState(lifecycle,SessionState::Ready);
    }
    LONG sequence=bridge.shared->snapshotSequence;if((sequence&1)||sequence==seenSequence){for(auto& row:rows){auto found=rules.find(PersistentKey(SelectorFor(Record(row))));if(found!=rules.end()&&(found->second.inactiveSeconds||found->second.unusedDays))ApplyRule(row,&found->second);}return;}
    auto current=bridge.Snapshot();if(sequence!=bridge.shared->snapshotSequence)return;seenSequence=sequence;rows=std::move(current);
    SnapshotResult snapshot;snapshot.complete=true;for(auto& row:rows)snapshot.records.push_back(Record(row));auto delta=MergeSnapshot(catalog,snapshot,Now());if(!delta.accepted)return;
    for(auto it=applied.begin();it!=applied.end();)if(!catalog.items.contains(it->first))it=applied.erase(it);else ++it;
    for(auto& row:rows){if(row.identity.ownerPid==GetCurrentProcessId())continue;auto found=rules.find(PersistentKey(SelectorFor(Record(row))));ApplyRule(row,found==rules.end()?nullptr:&found->second);}
    std::vector<NativeIconRecord> menu;for(auto& row:rows){if(row.identity.ownerPid==GetCurrentProcessId())continue;auto record=Record(row);auto found=rules.find(PersistentKey(SelectorFor(record)));if(found!=rules.end()&&!found->second.menuVisible)continue;menu.push_back(std::move(record));}
    {std::lock_guard lock(menuMutex);menuRows=std::move(menu);menuSettings=settings["menu"];}
}
std::string Bytes64(const uint32_t* pixels,size_t count){const auto* bytes=reinterpret_cast<const uint8_t*>(pixels);static const char chars[]="ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";std::string out;size_t size=count*4;out.reserve((size+2)/3*4);for(size_t i=0;i<size;i+=3){uint32_t n=uint32_t(bytes[i])<<16;if(i+1<size)n|=uint32_t(bytes[i+1])<<8;if(i+2<size)n|=bytes[i+2];out+=chars[(n>>18)&63];out+=chars[(n>>12)&63];out+=i+1<size?chars[(n>>6)&63]:'=';out+=i+2<size?chars[n&63]:'=';}return out;}
Json CatalogJson(){Json icons=Json::array();for(auto& row:rows){auto record=Record(row);auto key=IdentityKey(row.identity);auto selector=SelectorFor(record);auto found=rules.find(PersistentKey(selector));IconRule empty;empty.selector=selector;auto rule=found==rules.end()?empty:found->second;auto j=RuleJson(rule);j["key"]=key;j["pid"]=row.identity.ownerPid;j["tipCurrent"]=Utf8(row.tip);j["placement"]=row.placement;j["index"]=row.index;j["dynamic"]=row.dynamic;j["custom"]=row.custom;j["live"]=row.live;j["width"]=row.width;j["height"]=row.height;j["pixels"]=Bytes64(row.pixels,row.width*row.height);j["sourceFingerprint"]=row.sourceFingerprint;j["originalWidth"]=row.originalWidth;j["originalHeight"]=row.originalHeight;j["originalPixels"]=Bytes64(row.originalPixels,row.originalWidth*row.originalHeight);j["outcome"]=outcomes.contains(key)?outcomes[key]:Result(true,"原生状态");icons.push_back(std::move(j));}return {{"icons",icons},{"revision",catalog.revision}};}
Json Handle(const Json& req){
    if(req.value("schema",0)!=1)return Result(false,"协议版本不匹配",ERROR_REVISION_MISMATCH);auto method=req.value("method",std::string{});auto d=req.value("data",Json::object());Json result=Result(true);
    bool protectSettings=locked&&settings.value("protection",Json::object()).value("settings",false);
    bool protectInteractions=locked&&settings.value("protection",Json::object()).value("interactions",false);
    if(protectSettings&&(method=="settings.read"||method=="settings.apply"||method=="settings.import"||method=="rule.apply"||method=="rule.remove"||method=="ownIcon.apply"||method=="agent.stop"))return Result(false,"设置已锁定，请先解锁",ERROR_ACCESS_DENIED);
    if(protectInteractions&&(method=="icon.invoke"||method=="window.minimize"||method=="window.restore"||method=="internal.minimize"))return Result(false,"交互已锁定，请先解锁",ERROR_ACCESS_DENIED);
    if(method=="protection.set"){
        auto old=settings.value("protection",Json::object());if(old.contains("digest")&&!VerifyPassword(d.value("currentPassword",std::string{}),Digest()))return Result(false,"当前密码不正确",ERROR_ACCESS_DENIED);
        PasswordDigest digest;if(!CreatePasswordDigest(d.value("newPassword",std::string{}),digest))return Result(false,"密码不能为空或过长",ERROR_INVALID_DATA);
        auto saved=settings;settings["protection"]={{"digest",{{"salt",digest.salt},{"hash",digest.hash},{"iterations",digest.iterations}}},{"settings",d.value("settings",true)},{"menus",d.value("menus",true)},{"interactions",d.value("interactions",false)},{"lockAtStartup",d.value("lockAtStartup",false)}};
        if(!Persist()){settings=saved;return Result(false,"保护配置保存失败",ERROR_WRITE_FAULT);}result["saved"]=true;
    }
    else if(method=="protection.lock"){if(!settings.value("protection",Json::object()).contains("digest"))return Result(false,"请先设置保护密码",ERROR_INVALID_DATA);locked=true;menuLocked=settings["protection"].value("menus",true);result["data"]={{"locked",true}};}
    else if(method=="protection.unlock"){
        if(GetTickCount64()<nextUnlock)return Result(false,"请稍后再试",ERROR_RETRY);
        if(!settings.value("protection",Json::object()).contains("digest")||!VerifyPassword(d.value("password",std::string{}),Digest())){nextUnlock=GetTickCount64()+std::min<uint32_t>(30000,++failedUnlocks*1000);return Result(false,"密码不正确",ERROR_ACCESS_DENIED);}locked=false;menuLocked=false;failedUnlocks=0;nextUnlock=0;result["data"]={{"locked",false}};
    }
    else if(method=="internal.openUi")OpenUI();
    else if(method=="internal.shellCreated"){ownAdded=false;SetOwnIcon();seenSequence=-1;}
    else if(method=="internal.minimize")MinimizeWindow(reinterpret_cast<HWND>(d.at("hwnd").get<uint64_t>()));
    else if(method=="status.read"){std::vector<uint32_t> times;if(bridge.shared)times.assign(bridge.shared->callbackMicros,bridge.shared->callbackMicros+std::min<uint64_t>(512,bridge.shared->events));std::sort(times.begin(),times.end());result["data"]={{"ready",lifecycle.state==SessionState::Ready},{"state",static_cast<int>(lifecycle.state.load())},{"connected",bridge.Alive()},{"version","0.1.3"},{"recovered",recovered},{"pid",GetCurrentProcessId()},{"callbackP95Micros",times.empty()?0:times[(times.size()-1)*95/100]},{"events",bridge.shared?bridge.shared->events:0}};}
    else if(method=="catalog.read"){Refresh();result["data"]=CatalogJson();}
    else if(method=="settings.read")result["data"]=settings;
    else if(method=="settings.import"){
        auto imported=d.at("settings");if(!ValidateSettings(imported))return Result(false,"配置内容无效",ERROR_INVALID_DATA);auto old=settings;settings=imported;
        if(!Persist()){settings=old;return Result(false,"配置保存失败",ERROR_WRITE_FAULT);}bridge.Reset();LoadRules();SetOwnIcon();SetStartup(settings.value("startup",false));safeMode=settings.value("safeMode",false);seenSequence=-1;Refresh();result["saved"]=true;
    }
    else if(method=="rule.apply"||method=="rule.remove"){
        auto rule=ParseRule(d.at("rule"));auto key=PersistentKey(rule.selector);auto saved=settings;auto& list=settings["rules"];for(auto it=list.begin();it!=list.end();)if(PersistentKey(ParseRule(*it).selector)==key)it=list.erase(it);else ++it;
        if(method=="rule.apply")list.push_back(RuleJson(rule));if(!Persist()){settings=saved;return Result(false,"配置保存失败",ERROR_WRITE_FAULT);}LoadRules();seenSequence=-1;Refresh();result["saved"]=true;for(auto& row:rows)if(Matches(rule.selector,Record(row))){result=outcomes[IdentityKey(row.identity)];result["saved"]=true;break;}
    }
    else if(method=="ownIcon.apply"){auto saved=settings;settings["ownIcon"]=d;if(!Persist()){settings=saved;return Result(false,"配置保存失败",ERROR_WRITE_FAULT);}bool ok=SetOwnIcon();result=Result(ok,ok?"已生效":"已保存，图标资源无法加载",ok?0:ERROR_INVALID_DATA);result["saved"]=true;}
    else if(method=="settings.apply"){
        auto saved=settings;for(auto& [key,value]:d.items())if(key!="schema"&&key!="rules"&&key!="ownIcon"&&key!="revision")settings[key]=value;if(!Persist()){settings=saved;return Result(false,"配置保存失败",ERROR_WRITE_FAULT);}bool ok=SetStartup(settings.value("startup",false));safeMode=settings.value("safeMode",false);if(safeMode)bridge.Reset();applied.clear();seenSequence=-1;result=Result(ok,ok?"设置已保存":"已保存，自启动设置失败",ok?0:GetLastError());result["saved"]=true;
    }
    else if(method=="icon.invoke"){
        auto key=d.at("key").get<std::string>();auto row=std::find_if(rows.begin(),rows.end(),[&](const auto& r){return IdentityKey(r.identity)==key;});if(row==rows.end())return Result(false,"图标已退出",ERROR_NOT_FOUND);
        POINT p{};GetCursorPos(&p);BridgeCommand cmd;cmd.identity=row->identity;cmd.operation=d.value("action",std::string("menu"))=="menu"?BridgeOperation::Menu:BridgeOperation::Click;cmd.clickEvent=d.value("action",std::string{})=="double"?WM_LBUTTONDBLCLK:WM_LBUTTONUP;cmd.x=static_cast<float>(p.x);cmd.y=static_cast<float>(p.y);RecordAccess(catalog.items[key].activity,Now());applied.erase(key);result=Result(bridge.Command(cmd),"原应用回调",bridge.shared?bridge.shared->lastError:ERROR_NOT_READY);
    }
    else if(method=="window.list"){Json list=Json::array();EnumWindows([](HWND hwnd,LPARAM ptr)->BOOL{if(!IsWindowVisible(hwnd)||GetWindow(hwnd,GW_OWNER)||hwnd==window)return TRUE;wchar_t title[512]{};if(!GetWindowTextW(hwnd,title,512))return TRUE;DWORD pid{};GetWindowThreadProcessId(hwnd,&pid);static_cast<Json*>(reinterpret_cast<void*>(ptr))->push_back({{"hwnd",reinterpret_cast<uint64_t>(hwnd)},{"pid",pid},{"title",Utf8(title)},{"created",Generation(pid)}});return TRUE;},reinterpret_cast<LPARAM>(&list));result["data"]={{"windows",list},{"hidden",Json::array()}};for(auto& h:hidden)result["data"]["hidden"].push_back({{"hwnd",reinterpret_cast<uint64_t>(h.hwnd)},{"title",Utf8(h.title)}});}
    else if(method=="window.minimize"){auto hwnd=reinterpret_cast<HWND>(d.at("hwnd").get<uint64_t>());DWORD pid{};GetWindowThreadProcessId(hwnd,&pid);bool ok=Generation(pid)==d.at("created").get<uint64_t>()&&MinimizeWindow(hwnd);result=Result(ok,ok?"已收起窗口":"窗口身份已变化或无法收起",ok?0:ERROR_INVALID_WINDOW_HANDLE);}
    else if(method=="window.restore"){RestoreWindows();result=Result(true,"已恢复窗口");}
    else if(method=="agent.stop"){lifecycle.state=SessionState::UserStopping;stopping=true;SetTimer(window,2,100,nullptr);}
    else return Result(false,"未知命令",ERROR_INVALID_FUNCTION);
    result["requestId"]=req.value("requestId",std::string{});return result;
}
DWORD WINAPI Serve(void*){
    auto sd=CurrentUserSecurity();if(!sd)return 1;SECURITY_ATTRIBUTES sa{sizeof(sa),sd,FALSE};auto name=L"\\\\.\\pipe\\TrayAssistant.v1."+std::to_wstring(session);
    while(WaitForSingleObject(stopEvent,0)==WAIT_TIMEOUT){HANDLE pipe=CreateNamedPipeW(name.c_str(),PIPE_ACCESS_DUPLEX,PIPE_TYPE_BYTE|PIPE_READMODE_BYTE|PIPE_WAIT|PIPE_REJECT_REMOTE_CLIENTS,1,65536,65536,0,&sa);if(pipe==INVALID_HANDLE_VALUE)break;
        if(ConnectNamedPipe(pipe,nullptr)||GetLastError()==ERROR_PIPE_CONNECTED){try{std::string input;char chunk[4096];DWORD read{};while(input.find('\n')==std::string::npos&&input.size()<4*1024*1024&&ReadFile(pipe,chunk,sizeof(chunk),&read,nullptr)&&read)input.append(chunk,read);
            if(input.find('\n')!=std::string::npos){auto request=std::make_shared<Request>();request->input=Json::parse(input.substr(0,input.find('\n')));Enqueue(request);
                HANDLE waits[]={request->event,stopEvent};if(WaitForMultipleObjects(2,waits,FALSE,10000)==WAIT_OBJECT_0){auto output=request->output.dump()+"\n";DWORD written{};WriteFile(pipe,output.data(),static_cast<DWORD>(output.size()),&written,nullptr);}}
        }catch(...){const char error[]="{\"success\":false,\"detail\":\"无效请求\"}\n";DWORD written{};WriteFile(pipe,error,sizeof(error)-1,&written,nullptr);}}
        FlushFileBuffers(pipe);DisconnectNamedPipe(pipe);CloseHandle(pipe);
    }LocalFree(sd);return 0;
}
DWORD WINAPI Worker(void*){
    CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);auto start=SessionState::Starting;lifecycle.state.compare_exchange_strong(start,SessionState::WaitingForShell);if(ShouldRecover(lifecycle))SetOwnIcon();
    while(!stopping&&WaitForSingleObject(stopEvent,0)==WAIT_TIMEOUT){
        std::deque<std::shared_ptr<Request>> jobs;{std::lock_guard lock(queueMutex);jobs.swap(requests);}
        for(auto& request:jobs){SetPropW(window,L"TrayAssistant.Busy",reinterpret_cast<HANDLE>(1));try{request->output=Handle(request->input);}catch(const std::exception& e){request->output=Result(false,e.what(),ERROR_INVALID_DATA);}RemovePropW(window,L"TrayAssistant.Busy");request->output["requestId"]=request->input.value("requestId",std::string{});SetEvent(request->event); }
        Refresh();HANDLE waits[]={stopEvent,workEvent};WaitForMultipleObjects(2,waits,FALSE,500);
    }
    if(lifecycle.state==SessionState::SessionEnding){if(bridge.shared)InterlockedExchange(&bridge.shared->sessionEnding,1);}else RestoreWindows();
    bridge.Reset();NOTIFYICONDATAW d{};d.cbSize=sizeof(d);d.hWnd=window;d.uID=1;if(ownAdded)Shell_NotifyIconW(NIM_DELETE,&d);ownAdded=false;if(ownImage){DestroyIcon(ownImage);ownImage=nullptr;}
    CoUninitialize();return 0;
}
void TrayMenu(){
    if(menuLocked){PostJob("internal.openUi");return;}
    std::vector<NativeIconRecord> icons;Json options;{std::lock_guard lock(menuMutex);icons=menuRows;options=menuSettings;}
    HMENU menu=CreatePopupMenu();AppendMenuW(menu,MF_STRING,1,L"打开托盘图标助手");AppendMenuW(menu,MF_STRING,2,L"恢复所有收起的窗口");AppendMenuW(menu,MF_SEPARATOR,0,nullptr);
    for(size_t i=0;i<icons.size();++i){auto label=icons[i].tip;if(label.empty())label=std::filesystem::path(icons[i].executable).stem().wstring();if(options.value("programNames",true))label=std::filesystem::path(icons[i].executable).stem().wstring()+L" · "+label;AppendMenuW(menu,MF_STRING,100+i,label.substr(0,100).c_str());}
    AppendMenuW(menu,MF_SEPARATOR,0,nullptr);AppendMenuW(menu,MF_STRING,3,L"退出并恢复托盘");POINT p{};GetCursorPos(&p);SetForegroundWindow(window);auto selected=TrackPopupMenu(menu,TPM_RETURNCMD|TPM_RIGHTBUTTON,p.x,p.y,0,window,nullptr);DestroyMenu(menu);PostMessageW(window,WM_NULL,0,0);
    if(selected==1)PostJob("internal.openUi");else if(selected==2)PostJob("window.restore");else if(selected==3)PostMessageW(window,WM_CLOSE,0,0);else if(selected>=100&&static_cast<size_t>(selected-100)<icons.size())PostJob("icon.invoke",{{"key",IdentityKey(icons[selected-100].identity)},{"action","menu"}});
}
LRESULT CALLBACK WindowProc(HWND hwnd,UINT msg,WPARAM wp,LPARAM lp){
    static UINT taskbarCreated=RegisterWindowMessageW(L"TaskbarCreated");if(msg==taskbarCreated){PostJob("internal.shellCreated");return 0;}
    if(msg==WM_QUERYENDSESSION){HandleSessionQuery(lifecycle);return TRUE;}
    if(msg==WM_ENDSESSION){HandleSessionEnd(lifecycle,wp!=0);if(wp){stopping=true;SetEvent(endingEvent);PostMessageW(hwnd,WM_CLOSE,0,0);}return 0;}
    if(msg==WM_TIMER){if(wp==2){KillTimer(hwnd,2);PostMessageW(hwnd,WM_CLOSE,0,0);}return 0;}
    if(msg==OpenMessage){PostJob("internal.openUi");return 0;}
    if(msg==WM_HOTKEY){if(wp==1)PostJob("internal.openUi");else if(wp==2)PostJob("internal.minimize",{{"hwnd",reinterpret_cast<uint64_t>(GetForegroundWindow())}});else if(wp==3)TrayMenu();return 0;}
    if(msg==OwnCallback){auto event=LOWORD(lp);if(event==WM_CONTEXTMENU)TrayMenu();else if(event==NIN_SELECT||event==NIN_KEYSELECT||event==WM_LBUTTONDBLCLK)PostJob("internal.openUi");return 0;}
    if(msg==WM_CLOSE){stopping=true;if(lifecycle.state!=SessionState::SessionEnding)lifecycle.state=SessionState::UserStopping;SetEvent(stopEvent);if(pipeThread)CancelSynchronousIo(pipeThread);UnregisterHotKey(hwnd,1);UnregisterHotKey(hwnd,2);UnregisterHotKey(hwnd,3);PostQuitMessage(0);return 0;}
    if(msg==WM_DESTROY){PostQuitMessage(0);return 0;}return DefWindowProcW(hwnd,msg,wp,lp);
}
}
int WINAPI wWinMain(HINSTANCE instance,HINSTANCE,PWSTR,int){
    CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);ProcessIdToSessionId(GetCurrentProcessId(),&session);int argc{};auto args=CommandLineToArgvW(GetCommandLineW(),&argc);bool background=false;
    wchar_t executable[32768]{};GetModuleFileNameW(nullptr,executable,32768);root=std::filesystem::path(executable).parent_path();wchar_t local[32768]{};GetEnvironmentVariableW(L"LOCALAPPDATA",local,32768);dataRoot=std::filesystem::path(local)/L"TrayIconAssistant";
    for(int i=1;i<argc;++i){if(std::wstring(args[i])==L"--background")background=true;else if(std::wstring(args[i])==L"--data"&&i+1<argc)dataRoot=args[++i];}LocalFree(args);
    auto sd=CurrentUserSecurity();if(!sd)return 1;SECURITY_ATTRIBUTES security{sizeof(security),sd,FALSE};singleton=CreateMutexW(&security,FALSE,(L"Local\\TrayAssistant.Agent."+std::to_wstring(session)).c_str());auto mutexError=GetLastError();if(sd)LocalFree(sd);if(!singleton)return 1;if(mutexError==ERROR_ALREADY_EXISTS){if(!background)PostMessageW(FindWindowW(L"TrayAssistant.Agent",nullptr),OpenMessage,0,0);CloseHandle(singleton);return 0;}
    try{settingsPath=dataRoot/L"settings.json";settings=LoadSettings(settingsPath,recovered);configRevision=settings.value("revision",uint64_t{});safeMode=settings.value("safeMode",false);LoadRules();adapter=LoadAdapter(root/L"supported-adapters.json");
    auto endingSd=CurrentUserSecurity();SECURITY_ATTRIBUTES endingSecurity{sizeof(endingSecurity),endingSd,FALSE};endingEvent=CreateEventW(&endingSecurity,TRUE,FALSE,EndingEventName(GetCurrentProcessId(),Generation(GetCurrentProcessId())).c_str());if(endingSd)LocalFree(endingSd);if(!endingEvent)throw std::runtime_error("Ending signal unavailable");stopEvent=CreateEventW(nullptr,TRUE,FALSE,nullptr);workEvent=CreateEventW(nullptr,FALSE,FALSE,nullptr);WNDCLASSW wc{};wc.hInstance=instance;wc.lpfnWndProc=WindowProc;wc.lpszClassName=L"TrayAssistant.Agent";RegisterClassW(&wc);window=CreateWindowW(wc.lpszClassName,L"托盘图标助手后台",WS_OVERLAPPED,0,0,0,0,nullptr,nullptr,instance,nullptr);if(!window)throw std::runtime_error("Session window unavailable");
    RegisterHotKey(window,1,MOD_CONTROL|MOD_ALT|MOD_NOREPEAT,'T');RegisterHotKey(window,2,MOD_WIN|MOD_ALT|MOD_NOREPEAT,'M');RegisterHotKey(window,3,MOD_WIN|MOD_ALT|MOD_NOREPEAT,'T');workerThread=CreateThread(nullptr,0,Worker,nullptr,0,nullptr);pipeThread=CreateThread(nullptr,0,Serve,nullptr,0,nullptr);if(!workerThread||!pipeThread)throw std::runtime_error("Worker unavailable");if(!background)PostJob("internal.openUi");MSG message{};while(GetMessageW(&message,nullptr,0,0)>0){TranslateMessage(&message);DispatchMessageW(&message);}if(pipeThread){CancelSynchronousIo(pipeThread);WaitForSingleObject(pipeThread,100);CloseHandle(pipeThread);}if(workerThread){if(WaitForSingleObject(workerThread,900)!=WAIT_OBJECT_0)ExitProcess(0);CloseHandle(workerThread);}DestroyWindow(window);CloseHandle(workEvent);CloseHandle(endingEvent);CloseHandle(stopEvent);CloseHandle(singleton);CoUninitialize();return 0;
    }catch(...){if(singleton)CloseHandle(singleton);CoUninitialize();return 1;}
}
