#pragma once
#include "shared.h"
namespace tray {
bool ReadSystemControls(uint64_t moduleBase, const SymbolAdapter&, BridgeState&);
bool SetSystemControl(uint32_t id, int visibility);
void RestoreSystemControls(bool restore = true);
} // namespace tray
