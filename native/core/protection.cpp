#include "protection.h"
#include <windows.h>
#include <bcrypt.h>
#include <vector>
namespace tray {
static bool Derive(const std::string& password,const PasswordDigest& digest,std::array<uint8_t,32>& output){
    if(password.empty()||password.size()>1024||digest.iterations<100000||digest.iterations>2000000)return false;
    BCRYPT_ALG_HANDLE algorithm{};if(BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,BCRYPT_ALG_HANDLE_HMAC_FLAG)<0)return false;
    std::vector<uint8_t> secret(password.begin(),password.end());auto result=BCryptDeriveKeyPBKDF2(algorithm,secret.data(),static_cast<ULONG>(secret.size()),const_cast<PUCHAR>(digest.salt.data()),static_cast<ULONG>(digest.salt.size()),digest.iterations,output.data(),static_cast<ULONG>(output.size()),0);
    SecureZeroMemory(secret.data(),secret.size());BCryptCloseAlgorithmProvider(algorithm,0);return result>=0;
}
bool CreatePasswordDigest(const std::string& password,PasswordDigest& digest){
    if(password.empty()||password.size()>1024)return false;digest.iterations=600000;
    if(BCryptGenRandom(nullptr,digest.salt.data(),static_cast<ULONG>(digest.salt.size()),BCRYPT_USE_SYSTEM_PREFERRED_RNG)<0)return false;return Derive(password,digest,digest.hash);
}
bool VerifyPassword(const std::string& password,const PasswordDigest& digest){std::array<uint8_t,32> candidate{};if(!Derive(password,digest,candidate))return false;uint8_t different{};for(size_t i=0;i<candidate.size();++i)different|=candidate[i]^digest.hash[i];SecureZeroMemory(candidate.data(),candidate.size());return different==0;}
}
