#pragma once
#include "types.h"
#include <unordered_map>
namespace tray {
struct LegacyRole {
    bool ready{}, single{}, cardinalityKnown{}, dynamicTip{}, dynamicImage{}, hasObservedUid{};
    uint32_t callback{}, observedUid{};
    std::wstring windowClass;
    std::vector<std::wstring> tips;
    std::vector<uint64_t> images;
    std::string listKey;
};
struct RuleSelector {
    std::wstring executable, windowClass;
    uint32_t uid{};
    bool hasGuid{};
    bool matchUid{true};
    bool windowClassIsIdentity{};
    GUID guid{};
    std::wstring tipPattern;
    uint32_t tipMode{}, ownerPid{};
    uint64_t ownerCreatedAt{};
    LegacyRole legacy;
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
RuleSelector SelectorFor(const NativeIconRecord&, const std::vector<NativeIconRecord>&);
LegacyRole DescribeLegacyRole(const NativeIconRecord&, const std::vector<NativeIconRecord>&);
std::string PersistentKey(const RuleSelector&);
bool Matches(const RuleSelector&, const NativeIconRecord&);
CatalogDelta MergeSnapshot(IconCatalog&, const SnapshotResult&, uint64_t now);
bool IsInactive(const IconRule&, const ActivityState&, uint64_t now);
void RecordAccess(ActivityState&, uint64_t now);
struct RuleResolution {
    const IconRule* rule{};
    bool ambiguous{};
    bool reassociated{}, provisional{};
};
struct RuleBinding { std::string key; bool provisional{}; uint64_t registration{}; };
RuleResolution ResolveRule(const std::unordered_map<std::string, IconRule>&,
                           const NativeIconRecord&);
RuleResolution ResolveRule(const std::unordered_map<std::string, IconRule>&,
                           const NativeIconRecord&, const std::vector<NativeIconRecord>&,
                           const RuleBinding* binding = nullptr,
                           const std::unordered_map<std::string, RuleBinding>* bindings = nullptr);
} // namespace tray
