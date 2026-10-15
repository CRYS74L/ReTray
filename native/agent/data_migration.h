#pragma once
#include "settings.h"
#include <fstream>

namespace tray {
// Copy once into a sibling staging directory and publish by rename. The former
// directory is retained as a recoverable backup; an existing ReTray profile wins.
inline void MigrateLegacyData(const std::filesystem::path& local) {
    auto legacy = local / L"TrayIconAssistant";
    const auto logicalLegacy = legacy;
    const auto target = local / L"ReTray";
    auto hasProfile = [](const auto& directory) {
        return std::filesystem::exists(directory / L"settings.json") ||
               std::filesystem::exists(directory / L"settings.json.bak");
    };
    if (hasProfile(target)) return;
    auto newest = [](const auto& directory) {
        auto time = std::filesystem::file_time_type::min();
        for (auto file : {L"settings.json", L"settings.json.bak"}) {
            std::error_code error;
            auto candidate = std::filesystem::last_write_time(directory / file, error);
            if (!error) time = std::max(time, candidate);
        }
        return time;
    };
    // Former MSIX builds redirected LocalAppData writes into LocalCache. Prefer
    // the most recently saved real profile over a stale loose-build profile.
    const auto packages = local / L"Packages";
    if (std::filesystem::is_directory(packages)) {
        for (const auto& entry : std::filesystem::directory_iterator(packages)) {
            if (!entry.is_directory() || entry.is_symlink() ||
                !entry.path().filename().wstring().starts_with(L"CRYS74L.TrayIconAssistant_")) continue;
            auto candidate = entry.path() / L"LocalCache" / L"Local" / L"TrayIconAssistant";
            if (hasProfile(candidate) && (!hasProfile(legacy) || newest(candidate) > newest(legacy)))
                legacy = candidate;
        }
    }
    if (!hasProfile(legacy)) return;
    auto staging = local / (L"ReTray.migration-" + std::to_wstring(GetCurrentProcessId()));
    if (std::filesystem::exists(staging))
        throw std::runtime_error("A previous migration staging directory needs inspection");
    std::filesystem::create_directories(staging);
    try {
        for (const auto& entry : std::filesystem::directory_iterator(legacy)) {
            if (entry.is_symlink()) throw std::runtime_error("Linked profile entries cannot be migrated");
            std::filesystem::copy(entry.path(), staging / entry.path().filename(),
                                  std::filesystem::copy_options::recursive);
        }
        for (const auto file : {L"settings.json", L"settings.json.bak"}) {
            auto path = staging / file;
            if (!std::filesystem::exists(path)) continue;
            Json value;
            try { std::ifstream input(path); input >> value; }
            catch (const Json::exception&) {
                // Preserve a damaged primary/backup byte-for-byte. The ordinary
                // settings loader can still recover from the other copy.
                continue;
            }
            auto rebase = [&](auto&& self, Json& item) -> void {
                if (item.is_object()) {
                    for (auto& [key, child] : item.items()) {
                        if ((key == "asset" || key == "replacementAsset") && child.is_string()) {
                            auto original = std::filesystem::path(Wide(child.get<std::string>()));
                            for (const auto& origin : {legacy, logicalLegacy}) {
                                auto relative = original.lexically_relative(origin);
                                if (!relative.empty() && !relative.is_absolute() && *relative.begin() != L"..") {
                                    child = Utf8((target / relative).wstring()); break;
                                }
                            }
                        } else self(self, child);
                    }
                } else if (item.is_array()) for (auto& child : item) self(self, child);
            };
            rebase(rebase, value);
            if (value.is_object() && value.contains("uiActivation"))
                value["uiActivation"] = ""; // New UI registers its own package identity.
            if (value.contains("ownIcon") && value["ownIcon"].is_object() &&
                value["ownIcon"].contains("tip") && value["ownIcon"]["tip"].is_string() &&
                value["ownIcon"]["tip"].get<std::string>() ==
                    "\u6258\u76d8\u56fe\u6807\u52a9\u624b")
                value["ownIcon"]["tip"] = "ReTray";
            // Retain unknown fields. NormalizeSettings handles the default label
            // when the profile is loaded through the normal validated path.
            std::ofstream output(path, std::ios::trunc | std::ios::binary);
            output << value.dump(2); output.close();
            if (!output) throw std::runtime_error("Cannot save migrated profile");
        }
        std::error_code error;
        std::filesystem::rename(staging, target, error);
        // Another first launch can finish the exact same migration concurrently.
        if (error && !hasProfile(target))
            throw std::filesystem::filesystem_error("Cannot publish migrated profile", staging, target, error);
        if (std::filesystem::exists(staging)) std::filesystem::remove_all(staging);
    } catch (...) {
        // Keep staging for diagnosis; never delete the original user profile.
        throw;
    }
}
}
