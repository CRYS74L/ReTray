#pragma once
#include "../core/types.h"
#include "../../third_party/nlohmann/json.hpp"
#include <filesystem>
namespace tray {
struct SymbolAdapter {
    ImageIdentity image;
    uint32_t root{},model{},manager{},promoted{},overflow{},itemVtable{},vectorAt{},vectorSize{},vectorBegin{},vectorEnd{};
    uint32_t window{},uid{},guid{},version{},callback{},tip{},imageObject{},cachedIcon{},setIcon{},getIcon{};
    std::vector<std::pair<uint32_t,std::array<uint8_t,16>>> checks;
};
SymbolAdapter LoadAdapter(const std::filesystem::path& file);
uint32_t ExplorerPid();
SnapshotResult ReadSnapshot(const SymbolAdapter&,uint32_t explorerPid,uint64_t epoch);
nlohmann::json SnapshotJson(const SnapshotResult&,bool includePixels=false);
std::string Utf8(const std::wstring&);
std::wstring Wide(const std::string&);
}
