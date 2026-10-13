#include "../native/agent/settings.h"
#include "../native/platform/process_identity.h"
#include <cstdio>
#include <fstream>
#include <stdexcept>
using namespace tray;
#define CHECK(x)                                                                                   \
    do {                                                                                           \
        if (!(x))                                                                                  \
            throw std::runtime_error(#x);                                                          \
    } while (0)
int wmain(int argc, wchar_t** argv) {
    if (argc != 2)
        return 2;
    auto directory = std::filesystem::path(argv[1]);
    std::filesystem::create_directories(directory);
    auto path = directory / L"settings.json";
    try {
        std::wstring systemPath;
        auto created = SystemOwnerInfo(GetCurrentProcessId(), systemPath);
        wchar_t ownPath[32768]{}; GetModuleFileNameW(nullptr, ownPath, std::size(ownPath));
        CHECK(!_wcsicmp(systemPath.c_str(), ownPath));
        FILETIME c{}, e{}, k{}, u{};
        CHECK(GetProcessTimes(GetCurrentProcess(), &c, &e, &k, &u));
        CHECK(created == ((uint64_t(c.dwHighDateTime) << 32) | c.dwLowDateTime));
        systemPath.clear(); CHECK(!SystemOwnerInfo(UINT32_MAX, systemPath));
        CHECK(systemPath.empty());
        auto settings = DefaultSettings();
        CHECK(ValidateSettings(settings));
        settings["ownIcon"]["visible"] = false;
        CHECK(SaveSettings(path, settings));
        bool recovered{};
        auto read = LoadSettings(path, recovered);
        CHECK(read["ownIcon"]["visible"] == false);
        CHECK(!recovered);
        settings["theme"] = "dark";
        CHECK(SaveSettings(path, settings));
        {
            std::ofstream bad(path);
            bad << "{invalid";
        }
        read = LoadSettings(path, recovered);
        CHECK(recovered);
        CHECK(read["ownIcon"]["visible"] == false);
        auto invalid = settings;
        invalid["schema"] = 999;
        CHECK(!SaveSettings(path, invalid));
        CHECK(LoadSettings(path, recovered)["schema"] == 1);
        invalid = settings;
        invalid["startup"] = "invalid";
        CHECK(!SaveSettings(path, invalid));
        invalid = settings; invalid["autoSort"] = "bad";
        for (auto value : {Json("invalid"), Json(-1), Json(UINT64_MAX)}) {
            auto badRevision = settings; badRevision["revision"] = value;
            CHECK(!SaveSettings(path, badRevision));
        }
        for (auto field : {"language", "uiActivation"}) {
            auto badText = settings; badText[field] = 123;
            CHECK(!SaveSettings(path, badText));
        }
        CHECK(!SaveSettings(path, invalid));
        invalid = settings;
        invalid["password"] = "synthetic-test-value";
        CHECK(!SaveSettings(path, invalid));
        NativeIconRecord r;
        r.executable = L"C:\\Synthetic\\Example.exe";
        r.identity.hasGuid = true;
        r.identity.guid.Data1 = 55;
        IconRule rule;
        rule.selector = SelectorFor(r);
        rule.asset = L"example.ico";
        rule.visibility = 1;
        auto parsed = ParseRule(RuleJson(rule));
        CHECK(PersistentKey(parsed.selector) == PersistentKey(rule.selector));
        CHECK(parsed.visibility == 1);
        CHECK(parsed.asset == rule.asset);
        auto legacy = DefaultSettings();
        legacy["rules"] = {RuleJson(rule)};
        legacy["rules"][0].update({{"swapButtons", true}, {"alwaysShow", true},
                                    {"inactiveSeconds", 10}, {"unusedDays", 2},
                                    {"hotkey", (MOD_ALT << 16) | 'A'}, {"assetIndex", 7}});
        legacy["hotkeys"] = {{"open", (MOD_ALT << 16) | 'A'}, {"menu", (MOD_ALT << 16) | 'M'}};
        legacy["taskbar"] = {{"opacity", 88}, {"hideStart", true}};
        legacy["uiOpacity"] = 77; legacy["uiTopmost"] = true; legacy["autoSort"] = true;
        legacy["iconOrder"] = {"custom", "@assistant"};
        CHECK(SaveDataJson(path, legacy));
        read = LoadSettings(path, recovered); CHECK(!recovered);
        CHECK(read["hotkeys"]["open"] == 0 && read["hotkeys"]["menu"] == 0);
        CHECK(read["taskbar"]["opacity"] == 255 && read["taskbar"]["hideStart"] == false);
        CHECK(read["uiOpacity"] == 255 && read["uiTopmost"] == false && read["autoSort"] == false);
        CHECK(read["iconOrder"] == Json::array({"custom", "@assistant"}));
        parsed = ParseRule(read["rules"][0]);
        CHECK(parsed.visibility == 1 && parsed.asset == L"example.ico" && parsed.assetIndex == 7);
        CHECK(!parsed.swapButtons && !parsed.alwaysShow && parsed.hotkey == 0);
        CHECK(parsed.inactiveSeconds == 0 && parsed.unusedDays == 0);
        auto migrated = DefaultSettings();
        NativeIconRecord mac;
        mac.executable = L"C:\\Synthetic\\ChangingUid.exe";
        IconRule oldMac; oldMac.selector = SelectorFor(mac);
        oldMac.selector.uid = 42888048; oldMac.visibility = 0;
        auto first = RuleJson(oldMac);
        auto oldOrderKey = PersistentKey(oldMac.selector);
        oldMac.selector.uid = 18967408; oldMac.visibility = 1;
        migrated["rules"] = {first, RuleJson(rule), RuleJson(oldMac)};
        migrated["iconOrder"] = {"@assistant", oldOrderKey, "peer", PersistentKey(oldMac.selector)};
        CHECK(SaveDataJson(path, migrated));
        read = LoadSettings(path, recovered); CHECK(!recovered);
        // The loader cannot infer which obsolete UID was a particular application role.
        // Preserve every preference and order entry; association uses live evidence.
        CHECK(read["rules"].size() == 3);
        CHECK(read["rules"] == migrated["rules"]);
        CHECK(read["iconOrder"] == migrated["iconOrder"]);
        mac.windowClass = L"CommonTrayHost"; mac.identity.uid = 42036080;
        CHECK(SaveSettings(path, read));
        CHECK(LoadSettings(path, recovered)["rules"] == read["rules"]);
        for (bool explicitFirst : {false, true}) {
            IconRule broad, explicitRule;
            broad.selector.executable = mac.executable;
            broad.selector.uid = 42; broad.visibility = 0;
            explicitRule = broad; explicitRule.selector.windowClass = mac.windowClass;
            explicitRule.visibility = 1;
            auto overlap = DefaultSettings();
            RuleSelector oldUi; oldUi.executable = mac.executable; oldUi.uid = 42;
            overlap["iconOrder"] = {"before", PersistentKey(oldUi), "after"};
            overlap["rules"] = explicitFirst ? Json::array({RuleJson(explicitRule), RuleJson(broad)})
                                               : Json::array({RuleJson(broad), RuleJson(explicitRule)});
            CHECK(SaveDataJson(path, overlap));
            auto migrated = LoadSettings(path, recovered); CHECK(!recovered);
            std::unordered_map<std::string, IconRule> rules;
            for (const auto& entry : migrated["rules"]) {
                auto saved = ParseRule(entry); rules[PersistentKey(saved.selector)] = saved;
            }
            mac.identity.uid = 42;
            auto resolved = ResolveRule(rules, mac);
            CHECK(resolved.rule && !resolved.ambiguous && resolved.rule->visibility == 1);
            CHECK(migrated["iconOrder"][1] == PersistentKey(SelectorFor(mac)));
            overlap["rules"] = {RuleJson(explicitRule)};
            CHECK(SaveDataJson(path, overlap));
            migrated = LoadSettings(path, recovered); CHECK(!recovered);
            CHECK(migrated["iconOrder"][1] == PersistentKey(SelectorFor(mac)));
        }
        mac.callbackMessage = WM_APP + 8; mac.tip = L"Original tooltip";
        mac.cachedOriginal.fingerprint = 123; mac.identity.uid = 42;
        IconRule captured; captured.selector = SelectorFor(mac, {mac});
        captured.selector.legacy.listKey = "saved-list-position";
        captured.selector.legacy.dynamicImage = true;
        auto encoded = RuleJson(captured);
        CHECK(RuleJson(ParseRule(encoded)) == encoded);
        auto remembered = encoded;
        remembered["asset"] = "";
        remembered["replacementAsset"] = "C:\\Synthetic\\Remembered.dll";
        remembered["replacementAssetIndex"] = 4;
        auto inactive = ParseRule(remembered);
        CHECK(inactive.asset.empty() && inactive.replacementAsset == L"C:\\Synthetic\\Remembered.dll");
        CHECK(inactive.replacementAssetIndex == 4 && RuleJson(inactive) == remembered);
        auto oldActive = RuleJson(rule); oldActive.erase("replacementAsset"); oldActive.erase("replacementAssetIndex");
        auto compatible = ParseRule(oldActive);
        CHECK(compatible.replacementAsset == compatible.asset);
        auto wideBaseline = encoded;
        wideBaseline["baselineWidth"] = 4096; wideBaseline["baselineHeight"] = 1024;
        CHECK(ParseRule(wideBaseline).baselineWidth == 4096);
        auto oversizedBaseline = wideBaseline; oversizedBaseline["baselineHeight"] = 4096;
        bool oversizedRejected{}; try { ParseRule(oversizedBaseline); } catch (...) { oversizedRejected = true; }
        CHECK(oversizedRejected);
        auto malformed = encoded; malformed["selector"]["legacyRole"]["images"] = {-1};
        bool rejected{}; try { ParseRule(malformed); } catch (...) { rejected = true; }
        CHECK(rejected);
        printf("PASS defaults, atomic settings, corruption backup recovery, invalid schema "
               "rejection, lossless rule identity roundtrip, legacy policy neutralization\n");
        return 0;
    } catch (const std::exception& e) {
        fprintf(stderr, "FAIL settings: %s\n", e.what());
        return 1;
    }
}
