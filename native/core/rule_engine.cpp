#include "rule_engine.h"
#include <algorithm>
#include <cwctype>
#include <objbase.h>
#include <unordered_set>
namespace tray {
static std::wstring Normalize(std::wstring text) {
    for (auto& c : text) {
        if (c == L'/')
            c = L'\\';
        c = static_cast<wchar_t>(towlower(c));
    }
    return text;
}
RuleSelector SelectorFor(const NativeIconRecord& r) {
    RuleSelector s;
    s.executable = Normalize(r.executable);
    s.uid = r.identity.uid;
    s.hasGuid = r.identity.hasGuid;
    s.guid = r.identity.guid;
    if (!s.hasGuid) s.legacy = DescribeLegacyRole(r, {});
    return s;
}
static bool SameOwner(const NativeIconRecord& a, const NativeIconRecord& b) {
    return a.identity.ownerPid == b.identity.ownerPid &&
           a.identity.ownerCreatedAt == b.identity.ownerCreatedAt;
}
static bool PublicRole(const NativeIconRecord& r) {
    return !(r.sourceState & NIS_HIDDEN) || r.managed;
}
static size_t LegacyCount(const NativeIconRecord& r, const std::vector<NativeIconRecord>& context) {
    return std::count_if(context.begin(), context.end(), [&](const auto& peer) {
        return SameOwner(peer, r) && !peer.identity.hasGuid && PublicRole(peer) &&
               Normalize(peer.executable) == Normalize(r.executable);
    });
}
LegacyRole DescribeLegacyRole(const NativeIconRecord& r,
                              const std::vector<NativeIconRecord>& context) {
    LegacyRole role;
    role.ready = !r.identity.hasGuid && !r.executable.empty();
    role.hasObservedUid = role.ready; role.observedUid = r.identity.uid;
    role.windowClass = r.windowClass; role.callback = r.callbackMessage;
    role.single = !context.empty() && LegacyCount(r, context) == 1;
    role.cardinalityKnown = !context.empty();
    if (!r.tip.empty()) role.tips.push_back(r.tip);
    auto image = r.cachedOriginal.fingerprint ? r.cachedOriginal.fingerprint
                                            : r.currentFrame.fingerprint;
    if (image && (r.cachedOriginal.fingerprint || !r.currentFrame.transparent)) role.images.push_back(image);
    return role;
}
RuleSelector SelectorFor(const NativeIconRecord& r, const std::vector<NativeIconRecord>& context) {
    auto selector = SelectorFor(r);
    if (!selector.hasGuid) selector.legacy = DescribeLegacyRole(r, context);
    return selector;
}
std::string PersistentKey(const RuleSelector& s) {
    wchar_t guid[40]{};
    StringFromGUID2(s.guid, guid, 40);
    auto value = Normalize(s.executable) + L"|" + s.windowClass +
                 (s.hasGuid ? L"|guid:" + std::wstring(guid)
                            : s.matchUid ? L"|uid:" + std::to_wstring(s.uid) : L"|class") +
                 L"|" + std::to_wstring(s.tipMode) + L":" + s.tipPattern + L"|" +
                 std::to_wstring(s.ownerPid) + L":" + std::to_wstring(s.ownerCreatedAt) +
                 (s.windowClassIsIdentity ? L"|identity-class" : L"");
    int size = WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()),
                                   nullptr, 0, nullptr, nullptr);
    std::string key(size, 0);
    WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), key.data(), size,
                        nullptr, nullptr);
    return key;
}
bool Matches(const RuleSelector& s, const NativeIconRecord& r) {
    if (s.executable.empty() || Normalize(s.executable) != Normalize(r.executable) ||
        (!s.windowClass.empty() && s.windowClass != r.windowClass) ||
        s.hasGuid != r.identity.hasGuid ||
        !(s.hasGuid ? IsEqualGUID(s.guid, r.identity.guid)
                    : s.matchUid ? s.uid == r.identity.uid : !s.windowClass.empty()))
        return false;
    if (s.ownerPid &&
        (s.ownerPid != r.identity.ownerPid || s.ownerCreatedAt != r.identity.ownerCreatedAt))
        return false;
    if (s.tipMode == 1)
        return r.tip == s.tipPattern;
    if (s.tipMode == 2)
        return r.tip.starts_with(s.tipPattern);
    if (s.tipMode == 3)
        return r.tip.find(s.tipPattern) != std::wstring::npos;
    return s.tipMode == 0;
}
CatalogDelta MergeSnapshot(IconCatalog& catalog, const SnapshotResult& snapshot, uint64_t now) {
    CatalogDelta delta;
    if (!snapshot.complete || snapshot.records.size() > 256)
        return delta;
    std::unordered_set<std::string> active;
    for (const auto& record : snapshot.records) {
        auto key = IdentityKey(record.identity);
        if (!active.insert(key).second)
            return {};
    }
    for (const auto& record : snapshot.records) {
        auto key = IdentityKey(record.identity);
        auto found = catalog.items.find(key);
        if (found == catalog.items.end()) {
            catalog.items.emplace(key,
                                  CatalogItem{record, {now, now, record.currentFrame.fingerprint}});
            ++delta.added;
        } else {
            auto& item = found->second;
            if (item.activity.fingerprint != record.currentFrame.fingerprint) {
                item.activity.lastFrameChange = now;
                item.activity.fingerprint = record.currentFrame.fingerprint;
            }
            item.record = record;
            ++delta.updated;
        }
    }
    for (auto it = catalog.items.begin(); it != catalog.items.end();) {
        if (!active.contains(it->first)) {
            if (catalog.selected == it->first)
                catalog.selected.clear();
            it = catalog.items.erase(it);
            ++delta.removed;
        } else
            ++it;
    }
    ++catalog.revision;
    delta.accepted = true;
    return delta;
}
bool IsInactive(const IconRule& rule, const ActivityState& activity, uint64_t now) {
    if (!rule.enabled || rule.alwaysShow)
        return false;
    auto idle = std::max(activity.lastFrameChange, activity.lastAccess);
    bool inactive = rule.inactiveSeconds && now >= idle &&
                    now - idle >= static_cast<uint64_t>(rule.inactiveSeconds) * 1000;
    bool unused = rule.unusedDays && now >= activity.lastAccess &&
                  now - activity.lastAccess >= static_cast<uint64_t>(rule.unusedDays) * 86400000;
    return inactive || unused;
}
void RecordAccess(ActivityState& activity, uint64_t now) {
    activity.lastAccess = now;
}
RuleResolution ResolveRule(const std::unordered_map<std::string, IconRule>& rules,
                           const NativeIconRecord& record) {
    RuleResolution result;
    int best = -1;
    for (const auto& [key, rule] : rules) {
        if (!Matches(rule.selector, record))
            continue;
        const auto& s = rule.selector;
        int rank = (s.ownerPid ? 32 : 0) +
                   (!s.windowClass.empty() && !s.windowClassIsIdentity ? 16 : 0) +
                   (s.tipMode == 1   ? 8
                    : s.tipMode == 2 ? 4
                    : s.tipMode == 3 ? 2
                                     : 0);
        if (rank > best) {
            best = rank;
            result = {&rule, false};
        } else if (rank == best)
            result.ambiguous = true;
    }
    if (result.ambiguous)
        result.rule = nullptr;
    return result;
}
static int Specificity(const RuleSelector& s) {
    return (s.ownerPid ? 32 : 0) +
           (!s.windowClass.empty() && !s.windowClassIsIdentity ? 16 : 0) +
           (s.tipMode == 1 ? 8 : s.tipMode == 2 ? 4 : s.tipMode == 3 ? 2 : 0);
}
static bool SemanticScope(const RuleSelector& s, const NativeIconRecord& r) {
    if (s.executable.empty() || Normalize(s.executable) != Normalize(r.executable) ||
        s.hasGuid != r.identity.hasGuid ||
        (!s.windowClass.empty() && !s.windowClassIsIdentity && s.windowClass != r.windowClass))
        return false;
    if (s.ownerPid && (s.ownerPid != r.identity.ownerPid ||
                       s.ownerCreatedAt != r.identity.ownerCreatedAt)) return false;
    return s.tipMode == 0 || (s.tipMode == 1 && r.tip == s.tipPattern) ||
           (s.tipMode == 2 && r.tip.starts_with(s.tipPattern)) ||
           (s.tipMode == 3 && r.tip.find(s.tipPattern) != std::wstring::npos);
}
static bool ExactIdentity(const RuleSelector& s, const NativeIconRecord& r) {
    if (!SemanticScope(s, r)) return false;
    if (s.hasGuid) return IsEqualGUID(s.guid, r.identity.guid);
    return s.matchUid ? s.uid == r.identity.uid
                      : s.legacy.hasObservedUid && s.legacy.observedUid == r.identity.uid;
}
static bool PriorUidPresent(const RuleSelector& s, const NativeIconRecord& record,
                            const std::vector<NativeIconRecord>& context) {
    if (!s.matchUid && !s.legacy.hasObservedUid) return false;
    auto uid = s.matchUid ? s.uid : s.legacy.observedUid;
    // Hidden registrations also reserve their native UID; they may be an image bank.
    return std::any_of(context.begin(), context.end(), [&](const auto& peer) {
        return SameOwner(peer, record) && !peer.identity.hasGuid && peer.identity.uid == uid &&
               Normalize(peer.executable) == Normalize(s.executable);
    });
}
static int RoleScore(const RuleSelector& s, const NativeIconRecord& r,
                     const std::vector<NativeIconRecord>& context, bool* provisional = nullptr) {
    if (provisional) *provisional = false;
    if (!SemanticScope(s, r) || s.hasGuid || !PublicRole(r)) return 0;
    const auto& role = s.legacy;
    if (!role.ready) {
        // A declared UID-less class selector is old role evidence, never a wildcard exact ID.
        if (provisional) *provisional = s.windowClassIsIdentity;
        return !s.matchUid && !s.windowClass.empty() && s.windowClass == r.windowClass ? 1 : 0;
    }
    bool tip = std::find(role.tips.begin(), role.tips.end(), r.tip) != role.tips.end();
    auto fingerprint = r.cachedOriginal.fingerprint ? r.cachedOriginal.fingerprint
                                                    : r.currentFrame.fingerprint;
    bool image = fingerprint && std::find(role.images.begin(), role.images.end(), fingerprint) !=
                                    role.images.end();
    bool cls = !role.windowClass.empty() && role.windowClass == r.windowClass;
    bool callback = role.callback && role.callback == r.callbackMessage;
    if (!role.tips.empty() && !role.dynamicTip && !tip) return 0;
    if (!role.images.empty() && !role.dynamicImage && !image && !tip) return 0;
    bool singleton = role.single && LegacyCount(r, context) == 1;
    if (!cls && !(tip && (image || callback)) && !(singleton && image && callback)) return 0;
    if (role.callback && !callback && !(tip || image)) return 0;
    if (!tip && !image) {
        if (!singleton || !cls || !callback) return 0;
        if (provisional) *provisional = true;
    }
    return (tip ? 8 : 0) + (image ? 4 : 0) + (cls ? 2 : 0) + (callback ? 1 : 0);
}
static bool ReservedExact(const std::unordered_map<std::string, IconRule>& rules,
                           const NativeIconRecord& r) {
    return std::any_of(rules.begin(), rules.end(), [&](const auto& item) {
        return ExactIdentity(item.second.selector, r);
    });
}
RuleResolution ResolveRule(const std::unordered_map<std::string, IconRule>& rules,
                           const NativeIconRecord& record,
                           const std::vector<NativeIconRecord>& context, const RuleBinding* binding,
                           const std::unordered_map<std::string, RuleBinding>* bindings) {
    RuleResolution exact;
    int exactRank = -1, exactAmbiguousRank = -1;
    for (const auto& [key, rule] : rules) {
        if (!ExactIdentity(rule.selector, record)) continue;
        size_t count{};
        for (const auto& peer : context)
            if (SameOwner(peer, record) && ExactIdentity(rule.selector, peer)) ++count;
        if (count > 1 && !rule.selector.hasGuid) {
            int bestScore{}; size_t winners{}; bool selected{};
            for (const auto& peer : context) {
                if (!SameOwner(peer, record) || !ExactIdentity(rule.selector, peer)) continue;
                auto score = RoleScore(rule.selector, peer, context);
                if (score > bestScore) { bestScore = score; winners = 0; selected = false; }
                if (score == bestScore) { ++winners; selected |= SameIcon(peer.identity, record.identity); }
            }
            if (!bestScore || winners != 1) {
                exactAmbiguousRank = std::max(exactAmbiguousRank, Specificity(rule.selector)); continue;
            }
            if (!selected || RoleScore(rule.selector, record, context) != bestScore) continue;
        }
        int rank = Specificity(rule.selector);
        if (rank > exactRank) { exactRank = rank; exact = {&rule, false, false, false}; }
        else if (rank == exactRank) exactAmbiguousRank = std::max(exactAmbiguousRank, rank);
    }
    if (exactAmbiguousRank >= 0 && exactAmbiguousRank >= exactRank) return {nullptr, true};
    if (exact.rule) return exact;
    if (!PublicRole(record)) return {};
    RuleResolution result;
    int bestRank = -1, ambiguousRank = -1;
    for (const auto& [key, rule] : rules) {
        const auto& s = rule.selector;
        if (s.hasGuid || !SemanticScope(s, record) || PriorUidPresent(s, record, context)) continue;
        bool cached = binding && binding->key == key;
        // A proven role already belongs to one native registration per process.
        // A later secondary icon using an old frame must not claim it again.
        if (!cached && bindings && std::any_of(context.begin(), context.end(), [&](const auto& peer) {
            auto occupied = bindings->find(IdentityKey(peer.identity));
            return SameOwner(peer, record) && occupied != bindings->end() &&
                occupied->second.key == key && !occupied->second.provisional &&
                occupied->second.registration == peer.registrationSerial && SemanticScope(s, peer);
        })) continue;
        bool provisional{};
        auto score = RoleScore(s, record, context, &provisional);
        if (!score && !s.legacy.ready && s.matchUid && LegacyCount(record, context) == 1) {
            auto eligible = std::count_if(rules.begin(), rules.end(), [&](const auto& item) {
                return !item.second.selector.hasGuid && SemanticScope(item.second.selector, record);
            });
            // Old settings may predate observed role evidence. This reversible binding
            // remains provisional and never trains identity; a delayed exact UID wins.
            if (eligible == 1) { score = 1; provisional = true; }
        }
        bool accepted = cached && !binding->provisional;
        if (!accepted && score) {
            int highest{}; size_t winners{};
            for (const auto& peer : context) {
                if (!SameOwner(peer, record) || ReservedExact(rules, peer)) continue;
                auto peerScore = SameIcon(peer.identity, record.identity) ? score : RoleScore(s, peer, context);
                if (peerScore > highest) { highest = peerScore; winners = 0; }
                if (peerScore && peerScore == highest) ++winners;
            }
            accepted = score == highest && winners == 1;
            if (!accepted && score == highest && winners > 1)
                ambiguousRank = std::max(ambiguousRank, Specificity(s));
        }
        if (!accepted) continue;
        int rank = Specificity(s);
        if (rank > bestRank) { bestRank = rank; result = {&rule, false, true, cached ? binding->provisional : provisional}; }
        else if (rank == bestRank) ambiguousRank = std::max(ambiguousRank, rank);
    }
    if (ambiguousRank >= 0 && ambiguousRank >= bestRank) return {nullptr, true};
    return result;
}
} // namespace tray
