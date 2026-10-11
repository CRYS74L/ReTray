#pragma once
#include <array>
#include <string>
#include <cstdint>
namespace tray {
struct PasswordDigest{std::array<uint8_t,16> salt{};std::array<uint8_t,32> hash{};uint64_t iterations{600000};};
bool CreatePasswordDigest(const std::string&,PasswordDigest&);
bool VerifyPassword(const std::string&,const PasswordDigest&);
}
