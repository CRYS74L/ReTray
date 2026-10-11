#include "../native/agent/settings.h"
#include <fstream>
#include <cstdio>
#include <stdexcept>
using namespace tray;
#define CHECK(x) do{if(!(x))throw std::runtime_error(#x);}while(0)
int wmain(int argc,wchar_t** argv){
    if(argc!=2)return 2;auto directory=std::filesystem::path(argv[1]);std::filesystem::create_directories(directory);auto path=directory/L"settings.json";
    try{auto settings=DefaultSettings();CHECK(ValidateSettings(settings));settings["ownIcon"]["visible"]=false;CHECK(SaveSettings(path,settings));bool recovered{};auto read=LoadSettings(path,recovered);CHECK(read["ownIcon"]["visible"]==false);CHECK(!recovered);
    settings["theme"]="dark";CHECK(SaveSettings(path,settings));{std::ofstream bad(path);bad<<"{invalid";}read=LoadSettings(path,recovered);CHECK(recovered);CHECK(read["ownIcon"]["visible"]==false);
    auto invalid=settings;invalid["schema"]=999;CHECK(!SaveSettings(path,invalid));CHECK(LoadSettings(path,recovered)["schema"]==1);
    invalid=settings;invalid["startup"]="invalid";CHECK(!SaveSettings(path,invalid));invalid=settings;invalid["password"]="synthetic-test-value";CHECK(!SaveSettings(path,invalid));
    NativeIconRecord r;r.executable=L"C:\\Synthetic\\Example.exe";r.identity.hasGuid=true;r.identity.guid.Data1=55;IconRule rule;rule.selector=SelectorFor(r);rule.asset=L"example.ico";rule.visibility=1;auto parsed=ParseRule(RuleJson(rule));CHECK(PersistentKey(parsed.selector)==PersistentKey(rule.selector));CHECK(parsed.visibility==1);CHECK(parsed.asset==rule.asset);
    printf("PASS defaults, atomic settings, corruption backup recovery, invalid schema rejection, lossless rule identity roundtrip\n");return 0;
    }catch(const std::exception& e){fprintf(stderr,"FAIL settings: %s\n",e.what());return 1;}
}
