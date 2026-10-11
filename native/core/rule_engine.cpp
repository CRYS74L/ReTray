#include "rule_engine.h"
#include <algorithm>
#include <cwctype>
#include <objbase.h>
#include <unordered_set>
namespace tray {
static std::wstring Normalize(std::wstring text){for(auto& c:text){if(c==L'/')c=L'\\';c=static_cast<wchar_t>(towlower(c));}return text;}
RuleSelector SelectorFor(const NativeIconRecord& r){RuleSelector s;s.executable=Normalize(r.executable);s.uid=r.identity.uid;s.hasGuid=r.identity.hasGuid;s.guid=r.identity.guid;return s;}
std::string PersistentKey(const RuleSelector& s){
    wchar_t guid[40]{};StringFromGUID2(s.guid,guid,40);
    auto value=Normalize(s.executable)+L"|"+s.windowClass+(s.hasGuid?L"|guid:"+std::wstring(guid):L"|uid:"+std::to_wstring(s.uid))+L"|"+std::to_wstring(s.tipMode)+L":"+s.tipPattern+L"|"+std::to_wstring(s.ownerPid)+L":"+std::to_wstring(s.ownerCreatedAt);
    int size=WideCharToMultiByte(CP_UTF8,0,value.data(),static_cast<int>(value.size()),nullptr,0,nullptr,nullptr);
    std::string key(size,0);WideCharToMultiByte(CP_UTF8,0,value.data(),static_cast<int>(value.size()),key.data(),size,nullptr,nullptr);return key;
}
bool Matches(const RuleSelector& s,const NativeIconRecord& r){
    if(s.executable.empty()||Normalize(s.executable)!=Normalize(r.executable)||(!s.windowClass.empty()&&s.windowClass!=r.windowClass)||s.hasGuid!=r.identity.hasGuid||!(s.hasGuid?IsEqualGUID(s.guid,r.identity.guid):s.uid==r.identity.uid))return false;
    if(s.ownerPid&&(s.ownerPid!=r.identity.ownerPid||s.ownerCreatedAt!=r.identity.ownerCreatedAt))return false;
    if(s.tipMode==1)return r.tip==s.tipPattern;if(s.tipMode==2)return r.tip.starts_with(s.tipPattern);if(s.tipMode==3)return r.tip.find(s.tipPattern)!=std::wstring::npos;return s.tipMode==0;
}
CatalogDelta MergeSnapshot(IconCatalog& catalog,const SnapshotResult& snapshot,uint64_t now){
    CatalogDelta delta;if(!snapshot.complete||snapshot.records.size()>256)return delta;
    std::unordered_set<std::string> active;for(const auto& record:snapshot.records){auto key=IdentityKey(record.identity);if(!active.insert(key).second)return {};}
    for(const auto& record:snapshot.records){auto key=IdentityKey(record.identity);auto found=catalog.items.find(key);
        if(found==catalog.items.end()){catalog.items.emplace(key,CatalogItem{record,{now,now,record.currentFrame.fingerprint}});++delta.added;}
        else{auto& item=found->second;if(item.activity.fingerprint!=record.currentFrame.fingerprint){item.activity.lastFrameChange=now;item.activity.fingerprint=record.currentFrame.fingerprint;}item.record=record;++delta.updated;}
    }
    for(auto it=catalog.items.begin();it!=catalog.items.end();){if(!active.contains(it->first)){if(catalog.selected==it->first)catalog.selected.clear();it=catalog.items.erase(it);++delta.removed;}else ++it;}
    ++catalog.revision;delta.accepted=true;return delta;
}
bool IsInactive(const IconRule& rule,const ActivityState& activity,uint64_t now){
    if(!rule.enabled||rule.alwaysShow)return false;
    auto idle=std::max(activity.lastFrameChange,activity.lastAccess);
    bool inactive=rule.inactiveSeconds&&now>=idle&&now-idle>=static_cast<uint64_t>(rule.inactiveSeconds)*1000;
    bool unused=rule.unusedDays&&now>=activity.lastAccess&&now-activity.lastAccess>=static_cast<uint64_t>(rule.unusedDays)*86400000;
    return inactive||unused;
}
void RecordAccess(ActivityState& activity,uint64_t now){activity.lastAccess=now;}
}
