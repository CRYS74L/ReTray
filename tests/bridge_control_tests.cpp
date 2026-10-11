#include "../native/shell/bridge_client.h"
#include <cstdio>
#include <stdexcept>
using namespace tray;
constexpr UINT ChangeColor=WM_APP+10,MakeOff=WM_APP+11;
#define CHECK(value) do{if(!(value))throw std::runtime_error(#value);}while(0)
int wmain(int argc,wchar_t** argv) {
    if(argc<4)return 2;DWORD sourcePid=std::stoul(argv[3]);
    try{
        auto adapter=LoadAdapter(argv[1]);BridgeClient client;CHECK(client.Attach(adapter,argv[2]));auto rows=client.Snapshot();
        auto find=[&](const std::vector<BridgeIcon>& list,bool guid){auto found=std::find_if(list.begin(),list.end(),[&](const auto& row){return row.identity.ownerPid==sourcePid&&row.identity.hasGuid==guid;});CHECK(found!=list.end());return *found;};
        auto original=find(rows,false),guid=find(rows,true);CHECK(original.version==0);CHECK(guid.version==4);CHECK(original.live&&guid.live);
        auto refresh=[&](){BridgeCommand c;c.operation=BridgeOperation::Refresh;CHECK(client.Command(c));};
        PostMessageW(reinterpret_cast<HWND>(original.identity.ownerHwnd),ChangeColor,0xff1955e9,0);Sleep(150);refresh();auto changed=find(client.Snapshot(),false);CHECK(changed.fingerprint!=original.fingerprint);CHECK(changed.callback==original.callback&&changed.version==original.version);
        BridgeCommand hide;hide.operation=BridgeOperation::Visibility;hide.identity=original.identity;hide.visibility=1;CHECK(client.Command(hide));CHECK(find(client.Snapshot(),false).placement==static_cast<uint32_t>(Placement::Hidden));
        hide.visibility=0;CHECK(client.Command(hide));CHECK(find(client.Snapshot(),false).placement!=static_cast<uint32_t>(Placement::Hidden));
        auto icon=LoadIconW(nullptr,IDI_INFORMATION);BridgeCommand custom;custom.operation=BridgeOperation::Apply;custom.identity=original.identity;custom.replacementIcon=reinterpret_cast<uint64_t>(icon);CHECK(client.Command(custom));auto replaced=find(client.Snapshot(),false);CHECK(replaced.fingerprint!=changed.fingerprint);CHECK(replaced.callback==original.callback);CHECK(replaced.version==0);
        PostMessageW(reinterpret_cast<HWND>(original.identity.ownerHwnd),MakeOff,0,0);Sleep(150);refresh();CHECK(find(client.Snapshot(),false).transparent);
        PostMessageW(reinterpret_cast<HWND>(original.identity.ownerHwnd),ChangeColor,0xff1955e9,0);Sleep(150);refresh();CHECK(find(client.Snapshot(),false).fingerprint==replaced.fingerprint);
        BridgeCommand restore;restore.operation=BridgeOperation::Restore;restore.identity=original.identity;CHECK(client.Command(restore));CHECK(find(client.Snapshot(),false).fingerprint==changed.fingerprint);
        BridgeCommand move;move.operation=BridgeOperation::Move;move.identity=guid.identity;move.location=1;move.index=0;CHECK(client.Command(move));CHECK(find(client.Snapshot(),true).placement==static_cast<uint32_t>(Placement::Overflow));
        hide.identity=guid.identity;hide.visibility=1;CHECK(client.Command(hide));CHECK(find(client.Snapshot(),true).placement==static_cast<uint32_t>(Placement::Hidden));hide.visibility=0;CHECK(client.Command(hide));
        BridgeCommand menu;menu.operation=BridgeOperation::Menu;menu.identity=original.identity;menu.x=400;menu.y=400;CHECK(client.Command(menu));Sleep(150);PostMessageW(reinterpret_cast<HWND>(original.identity.ownerHwnd),WM_CANCELMODE,0,0);
        auto statusName=L"Local\\TrayAssistant.TestSource."+std::to_wstring(sourcePid);auto statusMap=OpenFileMappingW(FILE_MAP_READ,FALSE,statusName.c_str());CHECK(statusMap!=nullptr);auto bytes=static_cast<uint8_t*>(MapViewOfFile(statusMap,FILE_MAP_READ,0,0,40));CHECK(bytes!=nullptr);LONG menuCount{};memcpy(&menuCount,bytes+20,4);CHECK(menuCount>0);UnmapViewOfFile(bytes);CloseHandle(statusMap);
        hide.identity=original.identity;hide.visibility=1;CHECK(client.Command(hide));
        client.Detach();CHECK(client.shared->status==2);
        auto after=ReadSnapshot(adapter,ExplorerPid(),original.identity.shellEpoch);auto final=std::find_if(after.records.begin(),after.records.end(),[&](const auto& r){return r.identity.ownerPid==sourcePid&&!r.identity.hasGuid;});CHECK(final!=after.records.end());CHECK(final->placement==static_cast<Placement>(original.placement));
        printf("PASS source-before-attach + legacy/v4 + current updates + GUID/legacy hide/show + custom + off/flash cycle + lossless restore + native move + actual original menu + detach visibility restoration\n");return 0;
    }catch(const std::exception& error){fprintf(stderr,"FAIL bridge control: %s\n",error.what());return 1;}
}
