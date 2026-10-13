#pragma once
#include "native_snapshot.h"
#include <objbase.h>
#include <cstdio>

namespace tray {
struct InputProfileId { uint16_t language{}; GUID clsid{}, profile{}; };
inline std::string InputProfileKey(const InputProfileId& id) {
    wchar_t clsid[40]{}, profile[40]{};
    StringFromGUID2(id.clsid, clsid, 40); StringFromGUID2(id.profile, profile, 40);
    char language[5]{}; snprintf(language, sizeof(language), "%04X", id.language);
    return std::string("tsf:") + language + ":" + Utf8(clsid) + ":" + Utf8(profile);
}
inline bool ParseInputProfileKey(const std::string& key, InputProfileId& id) {
    if (key.size() != 86 || key.substr(0,4) != "tsf:" || key[8] != ':' || key[47] != ':') return false;
    unsigned language{};
    for (size_t i=4;i<8;++i) {
        char c=key[i]; unsigned digit = c>='0'&&c<='9'?c-'0':c>='A'&&c<='F'?c-'A'+10:16;
        if (digit>15) return false; language=language*16+digit;
    }
    auto clsid=Wide(key.substr(9,38)), profile=Wide(key.substr(48,38));
    if (FAILED(CLSIDFromString(clsid.c_str(), &id.clsid)) || FAILED(CLSIDFromString(profile.c_str(), &id.profile)) ||
        id.clsid==GUID{} || id.profile==GUID{} || !language) return false;
    id.language=static_cast<uint16_t>(language);
    return InputProfileKey(id)==key;
}
inline bool ValidInputMethodPolicy(const nlohmann::json& p) {
    try {
        InputProfileId id;
        if (!p.is_object() || !p.at("key").is_string() || !ParseInputProfileKey(p.at("key"),id) ||
            !p.at("asset").is_string() || p.at("asset").get_ref<const std::string&>().size()>32767 ||
            p.at("asset").get_ref<const std::string&>().find('\0')!=std::string::npos ||
            !p.at("useReplacement").is_boolean() || !p.at("index").is_number_integer()) return false;
        const auto& index=p.at("index");
        return index.is_number_unsigned()?index.get<uint64_t>()<=10000:index.get<int64_t>()>=0 && index.get<int64_t>()<=10000;
    } catch (...) { return false; }
}
inline bool ParseInputTipId(const std::string& text,InputProfileId& id) {
    if(text.size()!=81 || text[4]!=':')return false;
    auto key=std::string("tsf:")+text.substr(0,43)+":"+text.substr(43);
    for(auto& c:key)if(c>='a' && c<='f')c=static_cast<char>(c-'a'+'A');
    // Keep the scheme lowercase; GUID and language hex have canonical uppercase spelling.
    key.replace(0,4,"tsf:");
    return ParseInputProfileKey(key,id);
}
struct InputProfile {
    InputProfileId id;
    std::string key;
    std::wstring name, iconFile;
    int iconIndex{};
    bool active{};
};
std::vector<InputProfile> EnumerateInputProfiles();
// Thread-local TSF information; taskbar policy selection uses its own data model.
std::string ActiveInputProfileKey();
}
