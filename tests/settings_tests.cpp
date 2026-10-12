#include "../native/agent/settings.h"
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
        printf("PASS defaults, atomic settings, corruption backup recovery, invalid schema "
               "rejection, lossless rule identity roundtrip, legacy policy neutralization\n");
        return 0;
    } catch (const std::exception& e) {
        fprintf(stderr, "FAIL settings: %s\n", e.what());
        return 1;
    }
}
