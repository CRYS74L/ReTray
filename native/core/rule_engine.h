#pragma once
#include "types.h"
#include <unordered_map>
namespace tray {
struct RuleSelector {
    std::wstring executable, windowClass;
    uint32_t uid{};
    bool hasGuid{};
    GUID guid{};
    std::wstring tipPattern;
    uint32_t tipMode{}, ownerPid{};
    uint64_t ownerCreatedAt{};
};
struct IconRule {
    RuleSelector selector;
    int visibility{-1}, location{-1}, order{-1};
    std::wstring asset, tip;
    bool enabled{true}, menuVisible{true}, alwaysShow{};
    uint32_t inactiveSeconds{}, unusedDays{}, hotkey{}, hotkeyAction{1};
    int newState{-1};
    bool swapButtons{};
    uint64_t baselineFingerprint{};
    uint32_t baselineWidth{}, baselineHeight{}, assetIndex{};
};
struct ActivityState {
    uint64_t lastFrameChange{}, lastAccess{}, fingerprint{};
};
struct CatalogItem {
    NativeIconRecord record;
    ActivityState activity;
};
struct IconCatalog {
    std::unordered_map<std::string, CatalogItem> items;
    std::string selected;
    uint64_t revision{};
};
struct CatalogDelta {
    bool accepted{};
    size_t added{}, updated{}, removed{};
};
RuleSelector SelectorFor(const NativeIconRecord&);
std::string PersistentKey(const RuleSelector&);
bool Matches(const RuleSelector&, const NativeIconRecord&);
CatalogDelta MergeSnapshot(IconCatalog&, const SnapshotResult&, uint64_t now);
bool IsInactive(const IconRule&, const ActivityState&, uint64_t now);
void RecordAccess(ActivityState&, uint64_t now);
struct RuleResolution {
    const IconRule* rule{};
    bool ambiguous{};
};
RuleResolution ResolveRule(const std::unordered_map<std::string, IconRule>&,
                           const NativeIconRecord&);
} // namespace tray
