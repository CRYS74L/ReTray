#include "settings.h"
#include <fstream>
#include <set>
#include <objbase.h>
#include <gdiplus.h>
#include <shlobj.h>
namespace tray {
Json RuleJson(const IconRule& r) {
    wchar_t guid[40]{};
    StringFromGUID2(r.selector.guid, guid, 40);
    return {{"selector",
             {{"executable", Utf8(r.selector.executable)},
              {"windowClass", Utf8(r.selector.windowClass)},
              {"uid", r.selector.uid},
              {"hasGuid", r.selector.hasGuid},
              {"guid", Utf8(guid)},
              {"tipPattern", Utf8(r.selector.tipPattern)},
              {"tipMode", r.selector.tipMode},
              {"ownerPid", r.selector.ownerPid},
              {"ownerCreatedAt", r.selector.ownerCreatedAt}}},
            {"visibility", r.visibility},
            {"location", -1},
            {"order", -1},
            {"asset", Utf8(r.asset)},
            {"tip", Utf8(r.tip)},
            {"enabled", r.enabled},
            {"menuVisible", true},
            {"alwaysShow", false},
            {"inactiveSeconds", 0},
            {"unusedDays", 0},
            {"hotkey", 0},
            {"hotkeyAction", 0},
            {"newState", -1},
            {"swapButtons", false},
            {"baselineFingerprint", r.baselineFingerprint},
            {"baselineWidth", r.baselineWidth},
            {"baselineHeight", r.baselineHeight},
            {"assetIndex", r.assetIndex}};
}
IconRule ParseRule(const Json& j) {
    IconRule r;
    auto s = j.at("selector");
    r.selector.executable = Wide(s.at("executable").get<std::string>());
    r.selector.windowClass = Wide(s.value("windowClass", std::string{}));
    r.selector.uid = s.value("uid", 0u);
    r.selector.hasGuid = s.value("hasGuid", false);
    r.selector.tipPattern = Wide(s.value("tipPattern", std::string{}));
    r.selector.tipMode = s.value("tipMode", 0u);
    r.selector.ownerPid = s.value("ownerPid", 0u);
    r.selector.ownerCreatedAt = s.value("ownerCreatedAt", uint64_t{});
    if (FAILED(CLSIDFromString(
            Wide(s.value("guid", std::string("{00000000-0000-0000-0000-000000000000}"))).c_str(),
            &r.selector.guid)))
        throw std::runtime_error("Invalid rule GUID");
    r.visibility = j.value("visibility", -1);
    r.asset = Wide(j.value("asset", std::string{}));
    r.tip = Wide(j.value("tip", std::string{}));
    r.enabled = j.value("enabled", true);
    // The removed advanced controls remain readable in schema 1, but cannot activate
    // behavior through an older settings file or an imported rule.
    r.hotkeyAction = 0;
    r.baselineFingerprint = j.value("baselineFingerprint", uint64_t{});
    r.baselineWidth = j.value("baselineWidth", 0u);
    r.baselineHeight = j.value("baselineHeight", 0u);
    r.assetIndex = j.value("assetIndex", 0u);
    if (r.selector.executable.empty() || r.selector.executable.size() > 32767 ||
        r.tip.size() > 127 || r.visibility < -1 || r.visibility > 1 ||
        r.selector.tipMode > 3 || r.selector.tipPattern.size() > 127 ||
        r.selector.windowClass.size() > 255 ||
        (bool(r.selector.ownerPid) != bool(r.selector.ownerCreatedAt)) || r.assetIndex > 10000 ||
        r.baselineWidth > 2048 || r.baselineHeight > 2048)
        throw std::runtime_error("Rule values out of bounds");
    return r;
}
Json DefaultSettings() {
    return {{"schema", 1},
            {"revision", 0},
            {"theme", "system"},
            {"startup", false},
            {"ownIcon",
             {{"visible", true}, {"asset", ""}, {"tip", "托盘图标助手"}, {"showInList", true}}},
            {"rules", Json::array()},

            {"systemControls", Json::array()},
            {"splitInputIndicators", true},
            {"menu",
             {{"programNames", true},
              {"hoverDelayMs", 0},
              {"reverse", false},
              {"newFirst", true},
              {"filterUnusedDays", 0},
              {"action", "menu"}}},
            {"hotkeys",
             {
                 {"open", 0},
                 {"menu", 0},
             }},
            {"autoSort", false},
            {"reviewIntervalSeconds", 0},
            {"taskbar", {{"opacity", 255}, {"hideStart", false}}},
            {"iconOrder", Json::array()},
            {"uiTopmost", false},
            {"uiOpacity", 255}};
}
static bool ContainsSecret(const Json& value) {
    if (value.is_object())
        for (const auto& [key, child] : value.items()) {
            if (key == "password" || key == "newPassword" || key == "currentPassword" ||
                ContainsSecret(child))
                return true;
        }
    else if (value.is_array())
        for (const auto& child : value)
            if (ContainsSecret(child))
                return true;
    return false;
}
bool ValidateSettings(const Json& j) {
    try {
        if (!j.is_object() || ContainsSecret(j) || j.at("schema") != 1 ||
            !j.at("rules").is_array() || j.at("rules").size() > 512)
            return false;
        auto theme = j.value("theme", std::string("system"));
        if (theme != "system" && theme != "light" && theme != "dark")
            return false;
        for (auto& r : j.at("rules"))
            ParseRule(r);
        if (j.contains("hotkeys")) {
            if (!j["hotkeys"].is_object())
                return false;
            for (const auto& code : j["hotkeys"]) {
                auto value = code.get<uint32_t>();
                if (value && (!(value >> 16) || (value >> 16) > 15 || (value & 0xffff) > 255))
                    return false;
            }
        }
        if (j.value("reviewIntervalSeconds", 0u) > 86400)
            return false;
        if (j.contains("menu")) {
            const auto& menu = j["menu"];
            if (!menu.is_object() || menu.value("hoverDelayMs", 0u) > 10000 ||
                menu.value("filterUnusedDays", 0u) > 3650)
                return false;
            for (auto field : {"reverse", "newFirst", "programNames"})
                if (!menu.value(field, Json(false)).is_boolean())
                    return false;
            auto action = menu.value("action", std::string("menu"));
            if (action != "menu" && action != "click" && action != "double" &&
                action != "rightDouble")
                return false;
        }
        if (j.contains("systemControls")) {
            const auto& controls = j["systemControls"];
            if (!controls.is_array() || controls.size() > 10)
                return false;
            std::array<bool, 10> seen{};
            for (const auto& p : controls) {
                int id = p.at("id");
                if (id < 0 || id >= 10 || seen[id] || !p.at("hidden").is_boolean())
                    return false;
                seen[id] = true;
            }
        }
        for (const char* key : {"startup", "uiTopmost", "autoSort"})
            if (j.contains(key) && !j.at(key).is_boolean())
                return false;
        if (j.contains("iconOrder")) {
            const auto& order = j.at("iconOrder");
            if (!order.is_array() || order.size() > 256)
                return false;
            std::set<std::string> seen;
            for (const auto& key : order)
                if (!key.is_string() || key.get<std::string>().size() > 32768 ||
                    !seen.insert(key.get<std::string>()).second)
                    return false;
        }
        const auto& own = j.at("ownIcon");
        if (!own.at("visible").is_boolean() || !own.value("showInList", Json(true)).is_boolean() ||
            !own.value("asset", Json("")).is_string() ||
            own.value("tip", std::string{}).size() > 512)
            return false;
        int alpha = j.value("uiOpacity", 255);
        if (alpha < 0 || alpha > 255)
            return false;
        if (j.contains("taskbar")) {
            int opacity = j["taskbar"].value("opacity", 255);
            if (opacity < 0 || opacity > 255 ||
                !j["taskbar"].value("hideStart", Json(false)).is_boolean())
                return false;
        }
        return true;
    } catch (...) {
        return false;
    }
}
bool SaveDataJson(const std::filesystem::path& path, const Json& value) {
    try {
        std::filesystem::create_directories(path.parent_path());
    } catch (...) {
        return false;
    }
    auto tmp = path;
    tmp += L".tmp";
    auto text = value.dump();
    HANDLE file = CreateFileW(tmp.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
                              FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE)
        return false;
    DWORD written{};
    bool ok = WriteFile(file, text.data(), static_cast<DWORD>(text.size()), &written, nullptr) &&
              written == text.size() && FlushFileBuffers(file);
    CloseHandle(file);
    if (ok)
        ok = MoveFileExW(tmp.c_str(), path.c_str(),
                         MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH) != FALSE;
    if (!ok)
        DeleteFileW(tmp.c_str());
    return ok;
}
static Json ReadFile(const std::filesystem::path& path) {
    if (!std::filesystem::exists(path) || std::filesystem::file_size(path) > 4 * 1024 * 1024)
        throw std::runtime_error("Settings missing/oversized");
    std::ifstream f(path);
    Json j;
    f >> j;
    if (!ValidateSettings(j))
        throw std::runtime_error("Invalid settings");
    return j;
}
bool SaveSettings(const std::filesystem::path& path, const Json& settings) {
    if (!ValidateSettings(settings))
        return false;
    try {
        std::filesystem::create_directories(path.parent_path());
        auto tmp = path;
        tmp += L"." + std::to_wstring(GetCurrentProcessId()) + L".tmp";
        auto text = settings.dump(2);
        HANDLE file = CreateFileW(tmp.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS,
                                  FILE_ATTRIBUTE_NORMAL, nullptr);
        if (file == INVALID_HANDLE_VALUE)
            return false;
        DWORD written{};
        bool ok =
            WriteFile(file, text.data(), static_cast<DWORD>(text.size()), &written, nullptr) &&
            written == text.size() && FlushFileBuffers(file);
        CloseHandle(file);
        if (!ok) {
            DeleteFileW(tmp.c_str());
            return false;
        }
        auto backup = path;
        backup += L".bak";
        try {
            ReadFile(path);
            CopyFileW(path.c_str(), backup.c_str(), FALSE);
        } catch (...) {
        }
        if (!MoveFileExW(tmp.c_str(), path.c_str(),
                         MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) {
            DeleteFileW(tmp.c_str());
            return false;
        }
        return true;
    } catch (...) {
        return false;
    }
}
Json NormalizeSettings(Json value) {
    value.erase("protection");
    value.erase("windowRules");
    value.erase("newIconLocation");
    auto result = DefaultSettings();
    result.update(value);
    if (!value.value("splitInputIndicators", false)) {
        auto& controls=result["systemControls"];
        auto combined=std::find_if(controls.begin(),controls.end(),[](const auto& item){return item.at("id")==3;});
        bool separate=std::any_of(controls.begin(),controls.end(),[](const auto& item){return item.at("id")==9;});
        if(combined!=controls.end()&&!separate) controls.push_back({{"id",9},{"hidden",combined->at("hidden")}});
    }
    result["splitInputIndicators"]=true;
    for (const char* key : {"ownIcon"}) {
        auto merged = DefaultSettings().at(key);
        if (value.contains(key))
            merged.update(value.at(key));
        result[key] = merged;
    }
    result["hotkeys"] = {{"open", 0}, {"menu", 0}};
    result["taskbar"] = {{"opacity", 255}, {"hideStart", false}};
    result["autoSort"] = result["uiTopmost"] = false;
    result["uiOpacity"] = 255;
    result["reviewIntervalSeconds"] = 0;
    for (auto& rule : result["rules"])
        if (rule.is_object())
            rule.update({{"location", -1}, {"order", -1}, {"menuVisible", true},
                         {"alwaysShow", false}, {"inactiveSeconds", 0}, {"unusedDays", 0},
                         {"hotkey", 0}, {"hotkeyAction", 0}, {"newState", -1},
                         {"swapButtons", false}});
    return result;
}
Json LoadSettings(const std::filesystem::path& path, bool& recovered) {
    recovered = false;
    try {
        return NormalizeSettings(ReadFile(path));
    } catch (...) {
        auto backup = path;
        backup += L".bak";
        try {
            auto j = ReadFile(backup);
            recovered = true;
            return NormalizeSettings(j);
        } catch (...) {
            recovered = std::filesystem::exists(path);
            return DefaultSettings();
        }
    }
}
HICON LoadAsset(const std::filesystem::path& path, int index, int size) {
    size = std::clamp(size, 16, 128);
    if (path.empty())
        return nullptr;
    try {
        if (!std::filesystem::is_regular_file(path) ||
            std::filesystem::file_size(path) > 32 * 1024 * 1024)
            return nullptr;
        auto ext = path.extension().wstring();
        for (auto& c : ext)
            c = static_cast<wchar_t>(towlower(c));
        if (ext == L".ico")
            return static_cast<HICON>(
                LoadImageW(nullptr, path.c_str(), IMAGE_ICON, size, size, LR_LOADFROMFILE));
        if (ext == L".exe" || ext == L".dll") {
            HICON large{}, small{};
            SHDefExtractIconW(path.c_str(), index, 0, &large, &small, MAKELONG(size, size));
            if (small)
                DestroyIcon(small);
            return large;
        }
        if (ext != L".png" && ext != L".bmp" && ext != L".jpg" && ext != L".jpeg")
            return nullptr;
        ULONG_PTR token{};
        Gdiplus::GdiplusStartupInput input;
        if (Gdiplus::GdiplusStartup(&token, &input, nullptr) != Gdiplus::Ok)
            return nullptr;
        HICON result{};
        {
            Gdiplus::Bitmap source(path.c_str());
            if (source.GetLastStatus() == Gdiplus::Ok && source.GetWidth() <= 2048 &&
                source.GetHeight() <= 2048) {
                Gdiplus::Bitmap sized(size, size, PixelFormat32bppARGB);
                Gdiplus::Graphics g(&sized);
                g.SetInterpolationMode(Gdiplus::InterpolationModeHighQualityBicubic);
                g.Clear(Gdiplus::Color(0));
                double ratio =
                    std::min(double(size) / source.GetWidth(), double(size) / source.GetHeight());
                int width = std::max(1, int(source.GetWidth() * ratio)),
                    height = std::max(1, int(source.GetHeight() * ratio));
                g.DrawImage(&source, (size - width) / 2, (size - height) / 2, width, height);
                sized.GetHICON(&result);
            }
        }
        Gdiplus::GdiplusShutdown(token);
        return result;
    } catch (...) {
        return nullptr;
    }
}
} // namespace tray
