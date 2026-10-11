#pragma once
#include <cstdint>
namespace tray {
struct MousePoint {
    float x{}, y{};
};
using NativeAction = void(__fastcall*)(void*, bool, const MousePoint*);
using BehaviorFlags = uint32_t (*)(void*);
using AccessRecorder = void (*)(void*);
bool InstallBehaviorHooks(uint64_t base, BehaviorFlags, AccessRecorder);
void RemoveBehaviorHooks();
void FinishBehaviorHooks();
uint32_t ActiveBehaviorCallbacks();
void InvokeNativeAction(void* record, int action, const MousePoint*, bool nonMouse = true,
                        bool applyPolicy = false);
} // namespace tray
