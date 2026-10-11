#include <windows.h>
#include <initializer_list>
#include "behavior_hooks.h"
#include "../../third_party/minhook/include/MinHook.h"
namespace tray {
namespace {
NativeAction originalClick{}, originalContext{}, originalDouble{};
BehaviorFlags flags{};
AccessRecorder access{};
uint64_t base{};
bool installed{};
volatile LONG callbacks{};
struct Guard {
    Guard() {
        InterlockedIncrement(&callbacks);
    }
    ~Guard() {
        InterlockedDecrement(&callbacks);
    }
};
void __fastcall Click(void* record, bool nonMouse, const MousePoint* point) {
    Guard guard;
    auto policy = flags ? flags(record) : 0;
    if (policy & 2)
        return;
    if (access)
        access(record);
    ((policy & 1) && !nonMouse ? originalContext : originalClick)(record, nonMouse, point);
}
void __fastcall Context(void* record, bool nonMouse, const MousePoint* point) {
    Guard guard;
    auto policy = flags ? flags(record) : 0;
    if (policy & 2)
        return;
    if (access)
        access(record);
    ((policy & 1) && !nonMouse ? originalClick : originalContext)(record, nonMouse, point);
}
void __fastcall Double(void* record, bool nonMouse, const MousePoint* point) {
    Guard guard;
    auto policy = flags ? flags(record) : 0;
    if (policy & 2)
        return;
    if (access)
        access(record);
    originalDouble(record, nonMouse, point);
}
} // namespace
bool InstallBehaviorHooks(uint64_t module, BehaviorFlags read, AccessRecorder recorder) {
    if (installed) {
        flags = read;
        access = recorder;
        return true;
    }
    base = module;
    flags = read;
    access = recorder;
    auto status = MH_Initialize();
    if (status != MH_OK)
        return false;
    struct Hook {
        uint32_t rva;
        void* handler;
        void** original;
    };
    Hook hooks[] = {
        {0x14a5f8, reinterpret_cast<void*>(&Click), reinterpret_cast<void**>(&originalClick)},
        {0x14aa90, reinterpret_cast<void*>(&Context), reinterpret_cast<void**>(&originalContext)},
        {0x14af30, reinterpret_cast<void*>(&Double), reinterpret_cast<void**>(&originalDouble)}};
    for (auto& hook : hooks)
        if (MH_CreateHook(reinterpret_cast<void*>(base + hook.rva), hook.handler, hook.original) !=
            MH_OK) {
            MH_Uninitialize();
            return false;
        }
    for (auto& hook : hooks)
        if (MH_EnableHook(reinterpret_cast<void*>(base + hook.rva)) != MH_OK) {
            RemoveBehaviorHooks();
            MH_Uninitialize();
            return false;
        }
    installed = true;
    return true;
}
void RemoveBehaviorHooks() {
    if (base)
        for (uint32_t rva : {0x14a5f8u, 0x14aa90u, 0x14af30u})
            MH_DisableHook(reinterpret_cast<void*>(base + rva));
}
void FinishBehaviorHooks() {
    if (installed) {
        MH_Uninitialize();
        installed = false;
    }
    originalClick = originalContext = originalDouble = nullptr;
    flags = nullptr;
    access = nullptr;
}
uint32_t ActiveBehaviorCallbacks() {
    return InterlockedCompareExchange(&callbacks, 0, 0);
}
void InvokeNativeAction(void* record, int action, const MousePoint* point, bool nonMouse,
                        bool applyPolicy) {
    if (applyPolicy) {
        (action == 1 ? Context : action == 2 ? Double : Click)(record, nonMouse, point);
        return;
    }
    NativeAction function = action == 1   ? originalContext
                            : action == 2 ? originalDouble
                                          : originalClick;
    if (function)
        function(record, nonMouse, point);
    else if (base) {
        auto rva = action == 1 ? 0x14aa90 : action == 2 ? 0x14af30 : 0x14a5f8;
        reinterpret_cast<NativeAction>(base + rva)(record, nonMouse, point);
    }
}
} // namespace tray
