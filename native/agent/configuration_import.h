// Included after input_method_api.h. Import commits persisted configuration before
// issuing visual commands; live identities and cached native source images survive.
Json ImportFailure(const std::string& detail, DWORD error = ERROR_INVALID_DATA) {
    auto reply = Result(false, detail, error);
    reply["saved"] = false;
    return reply;
}
bool ImportIndexValid(const Json& object, const char* field) {
    if (!object.contains(field)) return true;
    const auto& value = object.at(field);
    return value.is_number_integer() &&
        (value.is_number_unsigned() ? value.get<uint64_t>() <= 10000
                                   : value.get<int64_t>() >= 0 && value.get<int64_t>() <= 10000);
}
bool ImportAssetReferenceValid(const Json& object, const char* field) {
    if (!object.contains(field)) return true;
    const auto& asset = object.at(field);
    return asset.is_string() && asset.get_ref<const std::string&>().size() <= 32767 &&
           asset.get_ref<const std::string&>().find('\0') == std::string::npos;
}
bool PrepareConfigurationImport(const Json& data, Json& candidate) {
    try {
        if (!data.contains("settings") || !data.at("settings").is_object()) return false;
        const auto& incoming = data.at("settings");
        // Validate the unmodified document: normalization must not hide malformed
        // fields or recursively embedded secret values in retired settings.
        if (incoming.dump().size() > 2 * 1024 * 1024 || !ValidateSettings(incoming)) return false;
        for (const auto& rule : incoming.at("rules"))
            if (!ImportIndexValid(rule, "assetIndex") || !ImportIndexValid(rule, "replacementAssetIndex") ||
                !ImportAssetReferenceValid(rule, "asset") || !ImportAssetReferenceValid(rule, "replacementAsset")) return false;
        const auto& own = incoming.at("ownIcon");
        if (!ImportIndexValid(own, "assetIndex") || !ImportIndexValid(own, "replacementAssetIndex") ||
            !ImportAssetReferenceValid(own, "asset") || !ImportAssetReferenceValid(own, "replacementAsset")) return false;
        candidate = NormalizeSettings(incoming);
        candidate["revision"] = configRevision;
        if (settings.contains("uiActivation")) candidate["uiActivation"] = settings.at("uiActivation");
        else candidate.erase("uiActivation");
        // SaveSettings writes indented JSON while ReadFile rejects files above
        // 4 MiB. Deep padding can be small on the wire but enormous on disk.
        // Reserve room for Persist's next revision before any backup or mutation.
        return candidate.dump().size() <= 2 * 1024 * 1024 &&
               candidate.dump(2).size() <= 4 * 1024 * 1024 - 4096 && ValidateSettings(candidate);
    } catch (...) { return false; }
}
bool ImportAssetsAvailable(const Json& candidate) {
    std::unordered_set<std::string> loaded;
    auto loadPath = [&](const std::string& asset, int index) {
        if (asset.empty()) return true;
        if (asset.size() > 32767 || asset.find('\0') != std::string::npos) return false;
        auto token = asset + '\0' + std::to_string(index);
        if (loaded.contains(token)) return true;
        auto image = LoadAsset(Wide(asset), index);
        if (!image) return false;
        DestroyIcon(image);
        loaded.insert(std::move(token));
        return true;
    };
    auto load = [&](const Json& object, const char* field, const char* indexField) {
        return !object.contains(field) || loadPath(object.at(field).get<std::string>(), object.value(indexField, 0));
    };
    try {
        for (const auto& rule : candidate.at("rules"))
            if (!load(rule, "asset", "assetIndex") || !load(rule, "replacementAsset", "replacementAssetIndex")) return false;
        // Own icon currently loads index zero; retained references are checked even
        // while using the original icon or while the assistant icon is hidden.
        const auto& own = candidate.at("ownIcon");
        for (const char* field : {"asset", "replacementAsset"}) {
            if (!own.contains(field)) continue;
            if (!loadPath(own.at(field).get<std::string>(), 0)) return false;
        }
        for (const auto& policy : candidate.at("inputMethodIcons"))
            if (!load(policy, "asset", "index")) return false;
        return true;
    } catch (...) { return false; }
}
Json ValidateConfigurationImport(const Json& data) {
    Json candidate;
    if (!PrepareConfigurationImport(data, candidate))
        return ImportFailure("导入配置无效");
    auto reply = Result(true, "配置校验通过");
    reply["saved"] = false;
    reply["data"] = std::move(candidate);
    return reply;
}
Json ImportConfiguration(const Json& data) {
    if (!data.contains("expectedRevision") || !data.at("expectedRevision").is_number_integer())
        return ImportFailure("导入版本参数无效");
    const auto& revision = data.at("expectedRevision");
    if ((!revision.is_number_unsigned() && revision.get<int64_t>() < 0) ||
        revision.get<uint64_t>() != configRevision)
        return ImportFailure("配置已改变，请重新导入", ERROR_REVISION_MISMATCH);
    if (configRevision >= INT64_MAX)
        return ImportFailure("配置版本无法递增", ERROR_ARITHMETIC_OVERFLOW);
    Json candidate;
    if (!PrepareConfigurationImport(data, candidate)) return ImportFailure("导入配置无效");
    if (!ImportAssetsAvailable(candidate))
        return ImportFailure("导入图标资源无法读取", ERROR_RESOURCE_NAME_NOT_FOUND);
    const auto previous = settings;
    std::filesystem::path backup;
    try {
        auto name = L"settings-before-import-" + std::to_wstring(Now()) + L"-" +
                    std::to_wstring(configRevision) + L"-" + std::to_wstring(GetCurrentProcessId());
        backup = dataRoot / L"Backups" / (name + L".json");
        for (unsigned suffix = 1; std::filesystem::exists(backup); ++suffix)
            backup = dataRoot / L"Backups" / (name + L"-" + std::to_wstring(suffix) + L".json");
        if (!SaveDataJson(backup, previous))
            return ImportFailure("导入前备份失败，配置未改变", ERROR_WRITE_FAULT);
    } catch (...) { return ImportFailure("导入前备份失败，配置未改变", ERROR_WRITE_FAULT); }
    bool startupChanged = previous.value("startup", false) != candidate.value("startup", false);
    if (startupChanged && !SetStartup(candidate.at("startup"), previous.value("startup", false))) {
        auto error = GetLastError();
        bool rollbackFailed = startupOutcome.value("rollbackFailed", false);
        auto reply = ImportFailure(rollbackFailed ? "自启动设置失败，原有自启动状态恢复失败"
                                                 : "自启动设置失败，配置未改变", error);
        reply["rollbackFailed"] = rollbackFailed;
        return reply;
    }
    settings = std::move(candidate);
    if (!Persist()) {
        bool restored = !startupChanged || SetStartup(previous.value("startup", false), settings.value("startup", false));
        settings = previous;
        auto reply = ImportFailure(restored ? "配置保存失败，配置未改变" : "配置保存失败，自启动状态恢复失败", ERROR_WRITE_FAULT);
        reply["rollbackFailed"] = !restored;
        return reply;
    }
    if (startupChanged) startupOutcome = Result(true, "登录计划任务已更新");
    LoadRules();
    // Preserve applied stamps so unchanged icons do not replay, but allow rejected
    // policies from an earlier session attempt to be tried with this import.
    failedStamp.clear(); retryAfter.clear();
    seenSequence = -1;
    // Acknowledgement is about durable storage. Explorer commands may each take
    // seconds, so enqueue their existing worker application instead of blocking
    // the saved reply behind a bulk runtime update.
    importOwnPending = importOwnPending || previous.at("ownIcon") != settings.at("ownIcon");
    importOwnRetryAt = 0;
    environmentRetryAt = std::max<ULONGLONG>(1, GetTickCount64());
    auto reply = Result(true, "配置已导入");
    reply["saved"] = true;
    reply["backupPath"] = Utf8(backup.wstring());
    reply["data"] = settings;
    reply["runtime"] = {{"state", "pending"}};
    return reply;
}
