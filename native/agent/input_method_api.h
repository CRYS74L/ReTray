// Included by main.cpp after its settings transaction and pixel serialization helpers.
std::string inputMethodStamp;
uint64_t inputMethodEpoch{};
std::unordered_set<std::string> appliedInputMethods;
bool inputMethodApplySuccess=true;
uint64_t inputMethodRetryAt{};
Json inputMethodRollback;
Json InputMethodPolicy(const std::string& key) {
    for(const auto& p:settings.value("inputMethodIcons",Json::array()))if(p.at("key")==key)return p;
    return {{"key",key},{"asset",""},{"index",0},{"useReplacement",false}};
}
bool SendInputMethodPolicy(const Json& policy,int phase=0) {
    HICON icon{};
    auto asset=policy.at("asset").get<std::string>();
    if(phase!=2 && phase!=3 && policy.at("useReplacement").get<bool>() && !asset.empty()) {
        icon=LoadAsset(Wide(asset),policy.at("index"),48);if(!icon)return false;
    }
    BridgeCommand command;command.operation=BridgeOperation::InputMethodLogo;
    wcsncpy_s(command.inputProfileKey,Wide(policy.at("key").get<std::string>()).c_str(),_TRUNCATE);
    command.replacementIcon=reinterpret_cast<uint64_t>(icon);
    command.index=phase;
    bool ok=bridge.Command(command);if(icon)DestroyIcon(icon);return ok;
}
bool ApplyInputMethodIcons() {
    if(!bridge.Alive() || !ShouldRecover(lifecycle) || stopping)return false;
    if(!inputMethodRollback.is_null()) {
        if(GetTickCount64()<inputMethodRetryAt)return false;
        if(!SendInputMethodPolicy(inputMethodRollback,3)){inputMethodRetryAt=GetTickCount64()+3000;return false;}
        inputMethodRollback=nullptr;inputMethodStamp.clear();
    }
    auto stamp=settings.value("inputMethodIcons",Json::array()).dump();auto epoch=bridge.shared->epoch;
    if(stamp==inputMethodStamp && epoch==inputMethodEpoch && (inputMethodApplySuccess || GetTickCount64()<inputMethodRetryAt))return inputMethodApplySuccess;
    auto prior=epoch==inputMethodEpoch?appliedInputMethods:std::unordered_set<std::string>{};
    inputMethodStamp=stamp;inputMethodEpoch=epoch;appliedInputMethods.clear();inputMethodApplySuccess=true;
    for(const auto& policy:settings.value("inputMethodIcons",Json::array())) {
        auto key=policy.at("key").get<std::string>();prior.erase(key);appliedInputMethods.insert(key);
        if(!SendInputMethodPolicy(policy))inputMethodApplySuccess=false;
    }
    for(const auto& key:prior)if(!SendInputMethodPolicy({{"key",key},{"asset",""},{"index",0},{"useReplacement",false}})) {
        // A removed policy still owns its native replacement until restore succeeds.
        // Retain that key across every retry instead of forgetting it after one failure.
        appliedInputMethods.insert(key); inputMethodApplySuccess=false;
    }
    inputMethodRetryAt=inputMethodApplySuccess?0:GetTickCount64()+3000;
    return inputMethodApplySuccess;
}
Json InputMethodImage(const std::wstring& file,int index) {
    if(file.empty())return nullptr;
    auto icon=LoadAsset(file,index,48);if(!icon)return nullptr;
    auto frame=RasterizeIcon(icon,FrameOrigin::SourceLive);DestroyIcon(icon);
    if(!frame.outcome.success)return nullptr;
    return {{"width",frame.frame.PixelWidth()},{"height",frame.frame.PixelHeight()},
            {"pixels",Bytes64(frame.frame.pixels.data(),frame.frame.pixels.size())}};
}
Json ReadInputMethods() {
    auto reply=Result(true,"输入法标志已读取");reply["data"]=Json::array();
    std::string displayedProfile;
    if(RefreshSystemControls())for(uint32_t i=0;i<std::min<uint32_t>(32,bridge.shared->systemCount);++i)
        if(bridge.shared->systemControls[i].id==3)displayedProfile=Utf8(bridge.shared->systemControls[i].inputProfileKey);
    for(const auto& profile:EnumerateInputProfiles()) {
        auto policy=InputMethodPolicy(profile.key);
        reply["data"].push_back({{"key",profile.key},{"name",Utf8(profile.name)},
            {"active",profile.key==displayedProfile},{"original",InputMethodImage(profile.iconFile,profile.iconIndex)},
            {"policy",policy},{"replacement",InputMethodImage(Wide(policy.at("asset")),policy.at("index"))}});
    }
    return reply;
}
Json SetInputMethodIcon(const Json& policy) {
    if(!ValidInputMethodPolicy(policy))return Result(false,"输入法图标参数无效",ERROR_INVALID_DATA);
    auto key=policy.at("key").get<std::string>();auto profiles=EnumerateInputProfiles();
    if(std::none_of(profiles.begin(),profiles.end(),[&](const auto& row){return row.key==key;}))
        return Result(false,"输入法已移除或禁用",ERROR_NOT_FOUND);
    if(!bridge.Alive() || !ShouldRecover(lifecycle) || stopping)return Result(false,"任务栏尚未就绪",ERROR_NOT_READY);
    if(!inputMethodRollback.is_null()) {
        if(!SendInputMethodPolicy(inputMethodRollback,3))return Result(false,"正在恢复上一项输入法图标设置，请稍后重试",ERROR_RETRY);
        inputMethodRollback=nullptr;inputMethodStamp.clear();
    }
    auto old=InputMethodPolicy(key);
    if(old==policy)return Result(true,"输入法图标设置未改变");
    // Validate before changing the live display; original mode can retain a missing file.
    if(policy.at("useReplacement").get<bool>() && !policy.at("asset").get_ref<const std::string&>().empty() &&
        InputMethodImage(Wide(policy.at("asset")),policy.at("index")).is_null())
        return Result(false,"无法读取替代图标",ERROR_RESOURCE_NAME_NOT_FOUND);
    if(!SendInputMethodPolicy(policy,1)) {
        auto error=bridge.shared->lastError?bridge.shared->lastError:ERROR_NOT_SUPPORTED;
        if(!SendInputMethodPolicy(policy,3)){inputMethodRollback=policy;inputMethodRetryAt=GetTickCount64()+3000;}
        return Result(false,"输入法标志替换失败，配置未改变",error);
    }
    auto previous=settings;
    auto& policies=settings["inputMethodIcons"];if(!policies.is_array())policies=Json::array();
    auto item=std::find_if(policies.begin(),policies.end(),[&](const auto& p){return p.at("key")==key;});
    if(item==policies.end())policies.push_back(policy);else *item=policy;
    if(!Persist()) {
        settings=std::move(previous);
        bool restored=SendInputMethodPolicy(policy,3);
        if(!restored) {inputMethodRollback=policy;inputMethodStamp.clear();inputMethodApplySuccess=false;inputMethodRetryAt=0;}
        return Result(false,restored?"配置保存失败，原图标已恢复":"配置保存失败，图标恢复待重试",ERROR_WRITE_FAULT);
    }
    inputMethodStamp.clear();
    if(!SendInputMethodPolicy(policy,2)) {
        auto reply=Result(false,"已保存，任务栏确认待重试",ERROR_RETRY);reply["saved"]=true;return reply;
    }
    auto reply=Result(true,"输入法标志设置已保存");reply["saved"]=true;reply["data"]=policy;return reply;
}
