#pragma once
#include "../core/rule_engine.h"
#include "../platform/native_snapshot.h"
namespace tray {
using Json = nlohmann::json;
Json RuleJson(const IconRule&);
IconRule ParseRule(const Json&);
Json DefaultSettings();
Json NormalizeSettings(Json);
bool ValidateSettings(const Json&);
bool SaveSettings(const std::filesystem::path&, const Json&);
bool SaveDataJson(const std::filesystem::path&, const Json&);
Json LoadSettings(const std::filesystem::path&, bool& recovered);
HICON LoadAsset(const std::filesystem::path&, int index = 0, int size = 32);
} // namespace tray
