#include "../native/shell/bridge_client.h"
#include <cstdio>
using namespace tray;
int wmain(int argc,wchar_t** argv){if(argc!=3)return 2;auto adapter=LoadAdapter(argv[1]);BridgeClient client;if(!client.Attach(adapter,argv[2]))return 3;auto icons=client.Snapshot().size();BridgeCommand command;command.operation=BridgeOperation::SystemRead;if(!client.Command(command)||client.shared->systemCount<4){fprintf(stderr,"FAIL existing XAML controls unavailable\n");return 1;}
    bool start=false,network=false;for(uint32_t i=0;i<client.shared->systemCount;++i){auto control=client.shared->systemControls[i];printf("CONTROL id=%u instances=%u visible=%u\n",control.id,control.instances,control.visible);if(control.id==7)start=true;if(control.id==0)network=true;}
    if(!start||!network)return 4;command.operation=BridgeOperation::SystemSet;command.location=0;command.visibility=1;if(!client.Command(command))return 5;command.operation=BridgeOperation::SystemRead;if(!client.Command(command))return 6;
    for(uint32_t i=0;i<client.shared->systemCount;++i)if(client.shared->systemControls[i].id==0&&client.shared->systemControls[i].visible)return 7;
    command.operation=BridgeOperation::SystemSet;command.location=0;command.visibility=-1;if(!client.Command(command))return 8;command.operation=BridgeOperation::Refresh;if(!client.Command(command))return 9;if(client.Snapshot().size()!=icons)return 10;client.Detach();puts("PASS existing XAML controls, native network visibility restore, no external notification registrations");return 0;}
