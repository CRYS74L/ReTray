#pragma once
#include "shared.h"
#undef GetCurrentTime
#include <winrt/Windows.UI.Xaml.h>
namespace tray {
struct InputMethodLogoHost {
    winrt::Windows::UI::Xaml::FrameworkElement element{nullptr};
    winrt::Windows::Foundation::IInspectable model{nullptr};
};
void DiscoverInputMethodLogos(const std::vector<InputMethodLogoHost>&);
bool UpdateInputMethodLogo(const std::string& key, HICON image, int phase=0);
void TickInputMethodLogos();
bool HasInputMethodLogos();
void ReadInputMethodLogoStatus(SystemControl&);
void RestoreInputMethodLogos(bool restore=true);
}
