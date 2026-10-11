#include "../native/shell/bridge_client.h"
#include <iostream>
int wmain(int argc,wchar_t** argv) {
    if(argc<3)return 2;
    try {
        auto adapter=tray::LoadAdapter(argv[1]);tray::BridgeClient client;
        if(!client.Attach(adapter,argv[2])){std::cerr<<"FAIL native bridge did not become ready, error="<<(client.shared?client.shared->lastError:GetLastError())<<"\n";return 1;}
        auto rows=client.Snapshot();size_t readable{},matches{};
        for(const auto& row:rows){if(row.live)++readable;if(row.width&&row.height)++matches;}
        std::cout<<"native in-process current frames records="<<rows.size()<<" live="<<readable<<" drawable="<<matches<<"\n";
        if(rows.empty()||readable!=rows.size())return 1;
        client.Detach();if(client.shared->status!=2){std::cerr<<"FAIL bridge detach\n";return 1;}
        std::cout<<"PASS current native frames + acknowledged callback detachment\n";return 0;
    }catch(const std::exception& e){std::cerr<<e.what()<<"\n";return 1;}
}
