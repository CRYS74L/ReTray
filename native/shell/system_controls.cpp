#include "system_controls.h"
#include <objbase.h>
#undef GetCurrentTime
#include <algorithm>
#include <unordered_set>
#include <winrt/windows.foundation.h>
#include <winrt/windows.ui.xaml.data.h>
#include <winrt/windows.ui.xaml.h>
#include <winrt/windows.ui.xaml.media.h>
namespace tray {
namespace {
using namespace winrt::Windows::UI::Xaml;
struct Control {
    uint32_t id{};
    FrameworkElement element{nullptr};
    winrt::Windows::Foundation::IInspectable original{nullptr};
    Data::Binding binding{nullptr};
    bool changed{};
};
std::vector<Control> controls;
std::unordered_set<uint32_t> hiddenIds;
void Hide(Control& c) {
    c.changed = true;
    if (c.element.Visibility() != Visibility::Collapsed)
        c.element.Visibility(Visibility::Collapsed);
}
template <class T> bool Read(uint64_t p, T& v) {
    SIZE_T n{};
    return ValidReadRange(p, sizeof(T)) &&
           ReadProcessMemory(GetCurrentProcess(), reinterpret_cast<void*>(p), &v, sizeof(T), &n) &&
           n == sizeof(T);
}
winrt::Windows::Foundation::IInspectable
Property(const winrt::Windows::Foundation::IInspectable& source, const GUID& iid, size_t slot) {
    void* receiver{};
    if (!source ||
        FAILED(reinterpret_cast<IUnknown*>(winrt::get_abi(source))->QueryInterface(iid, &receiver)))
        return nullptr;
    void* output{};
    auto table = *static_cast<void***>(receiver);
    using Fn = HRESULT(__stdcall*)(void*, void**);
    auto result = reinterpret_cast<Fn>(table[slot])(receiver, &output);
    static_cast<IUnknown*>(receiver)->Release();
    if (FAILED(result) || !output)
        return nullptr;
    return {output, winrt::take_ownership_from_abi};
}
std::wstring DataClass(const FrameworkElement& element) {
    static const GUID view{
        0xcbdd88d7, 0x22a3, 0x58d8, {0x8b, 0xf3, 0x1c, 0xd6, 0x14, 0x09, 0xed, 0x9a}},
        model{0x99c155bb, 0x604b, 0x553e, {0x9a, 0xd0, 0xbb, 0x3f, 0xb7, 0xd6, 0x78, 0x0f}},
        configuration{0xe1ebd49a, 0x19bb, 0x5174, {0xaf, 0x4e, 0xff, 0x26, 0x62, 0x85, 0x5f, 0xf3}};
    auto vm = Property(element, view, 10);
    auto config = Property(vm, model, 6);
    auto data = Property(config, configuration, 6);
    return data ? std::wstring(winrt::get_class_name(data)) : L"";
}
int Classify(const std::wstring& name, const std::wstring& type, const std::wstring& data) {
    if (name == L"LaunchListButton")
        return 7;
    if (name == L"ControlCenterButton")
        return 8;
    if (type.find(L"DateTimeIconContent") != std::wstring::npos)
        return 4;
    if (data.find(L"NetworkSystemTrayIconDataModel") != std::wstring::npos)
        return 0;
    if (data.find(L"VolumeSystemTrayIconDataModel") != std::wstring::npos)
        return 1;
    if (data.find(L"PowerSystemTrayIconDataModel") != std::wstring::npos)
        return 2;
    if (data.find(L"LanguageSystemTrayIconDataModel") != std::wstring::npos ||
        data.find(L"ImeSystemTrayIconDataModel") != std::wstring::npos)
        return 3;
    if (data.find(L"Privacy") != std::wstring::npos)
        return 6;
    if (type.find(L"NotificationIconContent") != std::wstring::npos ||
        data.find(L"NotificationBadgeSystemTrayIconDataModel") != std::wstring::npos)
        return 5;
    return -1;
}
void Restore(Control& c) {
    if (!c.changed)
        return;
    auto dp = UIElement::VisibilityProperty();
    if (c.binding)
        Data::BindingOperations::SetBinding(c.element, dp, c.binding);
    else if (c.original == DependencyProperty::UnsetValue())
        c.element.ClearValue(dp);
    else
        c.element.SetValue(dp, c.original);
    c.changed = false;
}
FrameworkElement Root(uint64_t base, const SymbolAdapter& adapter) {
    uint64_t root{}, table{}, getter{};
    if (!Read(base + adapter.root, root) || !Read(root + 8, table) || table != base + 0x1d5b58 ||
        !Read(table + 118 * 8, getter) || getter != base + 0x7e750)
        return nullptr;
    uint64_t pair[2]{};
    using Get = void*(__fastcall*)(void*, void*);
    reinterpret_cast<Get>(getter)(reinterpret_cast<void*>(root + 8), pair);
    struct Owner {
        uint64_t base;
        uint64_t* pair;
        ~Owner() {
            using Dtor = void(__fastcall*)(void*);
            reinterpret_cast<Dtor>(base + 0x388a0)(pair);
        }
    } owner{base, pair};
    uint64_t frame{};
    if (!pair[0] || !Read(pair[0] + 0x10, frame) || !frame)
        return nullptr;
    winrt::Windows::Foundation::IInspectable inspect{nullptr};
    winrt::copy_from_abi(inspect, reinterpret_cast<void*>(frame));
    auto element = inspect.try_as<UIElement>();
    if (!element)
        return nullptr;
    auto xaml = element.XamlRoot();
    return xaml ? xaml.Content().try_as<FrameworkElement>() : nullptr;
}
} // namespace
bool ReadSystemControls(uint64_t base, const SymbolAdapter& adapter, BridgeState& state) {
    try {
        auto root = Root(base, adapter);
        if (!root)
            return false;
        std::vector<DependencyObject> pending{root};
        size_t visited{};
        std::vector<Control> discovered;
        while (!pending.empty() && ++visited <= 8192) {
            auto node = pending.back();
            pending.pop_back();
            auto element = node.try_as<FrameworkElement>();
            if (element) {
                auto type = std::wstring(winrt::get_class_name(element));
                auto name = std::wstring(element.Name());
                auto data = type.find(L"IconView") != std::wstring::npos ? DataClass(element) : L"";
                auto id = Classify(name, type, data);
                if (id >= 0) {
                    auto existing =
                        std::find_if(controls.begin(), controls.end(),
                                     [&](const auto& c) { return c.element == element; });
                    if (existing != controls.end())
                        discovered.push_back(*existing);
                    else {
                        Control c;
                        c.id = static_cast<uint32_t>(id);
                        c.element = element;
                        c.original = element.ReadLocalValue(UIElement::VisibilityProperty());
                        auto binding =
                            element.GetBindingExpression(UIElement::VisibilityProperty());
                        if (binding)
                            c.binding = binding.ParentBinding();
                        discovered.push_back(c);
                    }
                }
            }
            int count = Media::VisualTreeHelper::GetChildrenCount(node);
            for (int i = 0; i < count; ++i)
                pending.push_back(Media::VisualTreeHelper::GetChild(node, i));
        }
        for (auto& old : controls)
            if (old.changed &&
                std::none_of(discovered.begin(), discovered.end(),
                             [&](const auto& c) { return c.element == old.element; }))
                Restore(old);
        controls = std::move(discovered);
        state.systemCount = 0;
        for (auto& c : controls) {
            if (hiddenIds.contains(c.id) && !c.changed)
                try {
                    Hide(c);
                } catch (...) {
                    // Discovery of another target must remain usable. This new object
                    // has been attempted once; only an explicit Set retries its setter.
                }
            auto row = std::find_if(state.systemControls, state.systemControls + state.systemCount,
                                    [&](const auto& r) { return r.id == c.id; });
            if (row == state.systemControls + state.systemCount) {
                if (state.systemCount >= 32)
                    break;
                row = &state.systemControls[state.systemCount++];
                *row = {};
                row->id = c.id;
                wcsncpy_s(row->name, std::size(row->name), c.element.Name().c_str(), _TRUNCATE);
                wcsncpy_s(row->type, std::size(row->type), winrt::get_class_name(c.element).c_str(),
                          _TRUNCATE);
            }
            ++row->instances;
            row->visible = row->visible || c.element.Visibility() == Visibility::Visible;
        }
        return state.systemCount > 0;
    } catch (...) {
        return false;
    }
}
bool SetSystemControl(uint32_t id, int visibility) {
    bool found = false;
    try {
        for (auto& c : controls)
            if (c.id == id) {
                found = true;
                if (visibility != 1)
                    Restore(c);
                else
                    Hide(c);
            }
        if (visibility != 1)
            hiddenIds.erase(id);
        else if (found)
            hiddenIds.insert(id);
        return found || visibility != 1;
    } catch (...) {
        return false;
    }
}
void RestoreSystemControls(bool restore) {
    if (restore)
        for (auto& c : controls)
            try {
                Restore(c);
            } catch (...) {
            }
    controls.clear();
    hiddenIds.clear();
}
} // namespace tray
