#include "system_controls.h"
#include "input_method_logo.h"
#include <objbase.h>
#undef GetCurrentTime
#include <algorithm>
#include <unordered_set>
#include <unordered_map>
#include <winrt/windows.foundation.h>
#include <winrt/windows.ui.xaml.data.h>
#include <winrt/windows.ui.xaml.controls.h>
#include <winrt/windows.ui.xaml.automation.peers.h>
#include <winrt/windows.ui.xaml.h>
#include <winrt/windows.ui.xaml.media.h>
namespace tray {
namespace {
using namespace winrt::Windows::UI::Xaml;
struct Control {
    uint32_t id{};
    FrameworkElement element{nullptr};
    winrt::Windows::Foundation::IInspectable model{nullptr};
    winrt::Windows::Foundation::IInspectable original{nullptr};
    Data::Binding binding{nullptr};
    bool changed{};
    int64_t visibilityToken{};
    double originalOpacity{1};
    bool originalHitTest{true};
};
std::vector<Control> controls;
std::unordered_set<uint32_t> hiddenIds;
FrameworkElement watchedRoot{nullptr};
winrt::event_token layoutToken{};
bool discovering{};
struct IconGlyph { std::wstring text, family; };
std::unordered_map<uint32_t, IconGlyph> glyphs;
uint64_t prePaintHides[10]{}, visibilityHides[10]{};
void Hide(Control& c) {
    if (c.id == 7) {
        if (!c.changed) { c.originalOpacity=c.element.Opacity();c.originalHitTest=c.element.IsHitTestVisible(); }
        c.changed=true;
        c.element.Opacity(0);c.element.IsHitTestVisible(false);
        return;
    }
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
winrt::Windows::Foundation::IInspectable DataModel(const FrameworkElement& element) {
    static const GUID view{
        0xcbdd88d7, 0x22a3, 0x58d8, {0x8b, 0xf3, 0x1c, 0xd6, 0x14, 0x09, 0xed, 0x9a}},
        model{0x99c155bb, 0x604b, 0x553e, {0x9a, 0xd0, 0xbb, 0x3f, 0xb7, 0xd6, 0x78, 0x0f}},
        configuration{0xe1ebd49a, 0x19bb, 0x5174, {0xaf, 0x4e, 0xff, 0x26, 0x62, 0x85, 0x5f, 0xf3}};
    auto vm = Property(element, view, 10);
    auto config = Property(vm, model, 6);
    auto data = Property(config, configuration, 6);
    return data;
}
int Classify(const std::wstring& name, const std::wstring& type, const std::wstring& data) {
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
    if (data.find(L"LanguageSystemTrayIconDataModel") != std::wstring::npos)
        return 3;
    if (data.find(L"ImeSystemTrayIconDataModel") != std::wstring::npos)
        return 9;
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
    if (c.id==7) {
        c.element.Opacity(c.originalOpacity);c.element.IsHitTestVisible(c.originalHitTest);c.changed=false;
        return;
    }
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
void ReadGlyph(const FrameworkElement& element, uint32_t id) {
    std::vector<DependencyObject> pending{element};
    IconGlyph glyph;
    while (!pending.empty()) {
        auto node = pending.back(); pending.pop_back();
        if (auto icon = node.try_as<Controls::FontIcon>(); icon && !icon.Glyph().empty()) {
            glyph.text += std::wstring(icon.Glyph());
            glyph.family = std::wstring(icon.FontFamily().Source());
        }
        if (auto text = node.try_as<Controls::TextBlock>(); text && !text.Text().empty()) {
            auto family = std::wstring(text.FontFamily().Source());
            if (family.find(L"Icons") != std::wstring::npos ||
                family.find(L"Symbol") != std::wstring::npos || id == 3) {
                glyph.text += std::wstring(text.Text()); glyph.family = std::move(family);
            }
        }
        auto count = Media::VisualTreeHelper::GetChildrenCount(node);
        for (int i = count - 1; i >= 0; --i)
            pending.push_back(Media::VisualTreeHelper::GetChild(node, i));
    }
    if (!glyph.text.empty()) glyphs[id] = std::move(glyph);
}
void Discover(const FrameworkElement& root, bool beforePaint = false) {
    if (discovering) return;
    discovering = true;
    struct End { ~End() { discovering = false; } } end;
    std::vector<DependencyObject> pending{root};
    std::vector<Control> discovered;
    while (!pending.empty()) {
        auto node = pending.back(); pending.pop_back();
        if (auto element = node.try_as<FrameworkElement>()) {
            auto type = std::wstring(winrt::get_class_name(element));
            auto model = type.find(L"IconView") != std::wstring::npos ? DataModel(element) : winrt::Windows::Foundation::IInspectable{nullptr};
            auto data = model ? std::wstring(winrt::get_class_name(model)) : L"";
            auto id = Classify(std::wstring(element.Name()), type, data);
            if (beforePaint) {
                auto known = std::find_if(controls.begin(), controls.end(), [&](const auto& c) { return c.element == element; });
                if (known != controls.end()) id = known->id;
            }
            // Start and Task View share the same template name LaunchListButton.
            // Their automation identities distinguish the actual controls.
            if (!beforePaint && element.Name() == L"LaunchListButton") {
                auto peer = Automation::Peers::FrameworkElementAutomationPeer::CreatePeerForElement(element);
                if (peer && peer.GetAutomationId() == L"StartButton") id = 7;
            }
            if (id >= 0) {
                auto old = std::find_if(controls.begin(), controls.end(),
                    [&](const auto& c) { return c.element == element; });
                Control c;
                if (old != controls.end()) c = *old;
                else {
                    c.id = static_cast<uint32_t>(id); c.element = element;
                    c.original = element.ReadLocalValue(UIElement::VisibilityProperty());
                    if (auto binding = element.GetBindingExpression(UIElement::VisibilityProperty()))
                        c.binding = binding.ParentBinding();
                    auto property=id==7?UIElement::OpacityProperty():UIElement::VisibilityProperty();
                    c.visibilityToken = element.RegisterPropertyChangedCallback(
                        property, [id](const DependencyObject& sender, const DependencyProperty&) {
                            if (!hiddenIds.contains(id)) return;
                            auto target = sender.as<UIElement>();
                            if(id==7) { if(target.Opacity()!=0) target.Opacity(0); return; }
                            if (target.Visibility() == Visibility::Visible) {
                                target.Visibility(Visibility::Collapsed);
                                ++visibilityHides[id];
                            }
                        });
                }
                c.model=model;
                if (!beforePaint) ReadGlyph(element, id);
                if (hiddenIds.contains(id)) {
                    if (beforePaint && id != 7 && element.Visibility() == Visibility::Visible) ++prePaintHides[id];
                    Hide(c);
                }
                discovered.push_back(std::move(c));
            }
        }
        auto count = Media::VisualTreeHelper::GetChildrenCount(node);
        for (int i = 0; i < count; ++i) pending.push_back(Media::VisualTreeHelper::GetChild(node, i));
    }
    for (auto& old : controls)
        if (std::none_of(discovered.begin(), discovered.end(), [&](const auto& c) { return c.element == old.element; })) {
            old.element.UnregisterPropertyChangedCallback(old.id==7?UIElement::OpacityProperty():UIElement::VisibilityProperty(), old.visibilityToken);
            Restore(old);
        }
    controls = std::move(discovered);
    std::vector<InputMethodLogoHost> logos;
    for(const auto& c:controls)if(c.id==3)logos.push_back({c.element,c.model});
    DiscoverInputMethodLogos(logos);
}
void Watch(const FrameworkElement& root) {
    if (watchedRoot == root) return;
    if (watchedRoot) watchedRoot.LayoutUpdated(layoutToken);
    watchedRoot = root;
    layoutToken = root.LayoutUpdated([](const auto&, const auto&) {
        // LayoutUpdated runs on the taskbar's XAML thread before its frame is
        // presented. A late-created privacy control is collapsed in that frame.
        if (discovering) return;
        if (hiddenIds.empty() && !HasInputMethodLogos()) return;
        try { Discover(watchedRoot, true); } catch (...) { /* Keep Explorer's layout event alive. */ }
    });
}
} // namespace
bool ReadSystemControls(uint64_t base, const SymbolAdapter& adapter, BridgeState& state) {
    try {
        auto root = Root(base, adapter);
        if (!root)
            return false;
        Watch(root);
        Discover(root);
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
            if(c.id==3)ReadInputMethodLogoStatus(*row);
            row->visible = row->visible || (c.id==7 ? c.element.Opacity()>0 && c.element.IsHitTestVisible()
                                                  : c.element.Visibility() == Visibility::Visible);
            if (auto glyph = glyphs.find(c.id); glyph != glyphs.end()) {
                wcsncpy_s(row->glyph, glyph->second.text.c_str(), _TRUNCATE);
                wcsncpy_s(row->fontFamily, glyph->second.family.c_str(), _TRUNCATE);
            }
            row->prePaintHides = prePaintHides[c.id];
            row->visibilityHides = visibilityHides[c.id];

        }
        for (const auto& [id, glyph] : glyphs)
            if (std::none_of(state.systemControls, state.systemControls + state.systemCount,
                [&](const auto& row) { return row.id == id; })) {
                auto& row = state.systemControls[state.systemCount++]; row = {}; row.id = id;
                wcsncpy_s(row.glyph, glyph.text.c_str(), _TRUNCATE);
                wcsncpy_s(row.fontFamily, glyph.family.c_str(), _TRUNCATE);
                row.prePaintHides = prePaintHides[id]; row.visibilityHides = visibilityHides[id];
            }
        return state.systemCount > 0;
    } catch (...) {
        return false;
    }
}
bool SetSystemControl(uint32_t id, int visibility) {
    bool found = false;
    try {
        if (visibility != 1) hiddenIds.erase(id);
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
bool SetSystemPolicy(uint32_t id, int visibility) {
    if (visibility == 1) hiddenIds.insert(id);
    else hiddenIds.erase(id);
    for (auto& c : controls) if (c.id == id) {
        if (visibility == 1) Hide(c); else Restore(c);
    }
    return true;
}
void RestoreSystemControls(bool restore) {
    RestoreInputMethodLogos(restore);
    if (watchedRoot) { watchedRoot.LayoutUpdated(layoutToken); watchedRoot = nullptr; }
    hiddenIds.clear();
    for (auto& c : controls)
        c.element.UnregisterPropertyChangedCallback(c.id==7?UIElement::OpacityProperty():UIElement::VisibilityProperty(), c.visibilityToken);
    if (restore)
        for (auto& c : controls)
            try {
                Restore(c);
            } catch (...) {
            }
    controls.clear();
    glyphs.clear();
}
} // namespace tray
