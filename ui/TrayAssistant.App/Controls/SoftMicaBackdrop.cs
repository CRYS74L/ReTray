using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace TrayAssistant_App.Controls;

public sealed class SoftMicaBackdrop : SystemBackdrop
{
    private MicaController? controller;
    private bool? darkAppearance;
    private FrameworkElement? themeRoot;
    private SystemBackdropConfiguration? configuration;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnTargetConnected(target, root);
        configuration = GetDefaultSystemBackdropConfiguration(target, root);
        themeRoot = root.Content as FrameworkElement;
        if (themeRoot != null) themeRoot.ActualThemeChanged += RootThemeChanged;
        controller = new MicaController { Kind = MicaKind.BaseAlt };
        ApplyAppearance(configuration, root);
        controller.SetSystemBackdropConfiguration(configuration);
        controller.AddSystemBackdropTarget(target);
    }

    private void RootThemeChanged(FrameworkElement sender, object args)
    {
        if (configuration != null && sender.XamlRoot != null) ApplyAppearance(configuration, sender.XamlRoot);
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        if (configuration != null) ApplyAppearance(configuration, root);
    }

    private void ApplyAppearance(SystemBackdropConfiguration configuration, XamlRoot root)
    {
        if (controller == null) return;
        var dark = root.Content is FrameworkElement element
            ? element.ActualTheme == ElementTheme.Dark
            : configuration.Theme == SystemBackdropTheme.Dark;
        if (darkAppearance == dark) return;
        darkAppearance = dark;
        var color = dark ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 243, 243, 243);
        controller.TintColor = color;
        controller.FallbackColor = color;
        // Keep BaseAlt's wallpaper material, with a more neutral tint than its vivid default.
        controller.TintOpacity = dark ? 0.65f : 0.68f;
        controller.LuminosityOpacity = 1.0f;
    }

    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        if (themeRoot != null) themeRoot.ActualThemeChanged -= RootThemeChanged;
        themeRoot = null;
        configuration = null;
        controller?.RemoveSystemBackdropTarget(target);
        controller?.Dispose();
        controller = null;
        darkAppearance = null;
        base.OnTargetDisconnected(target);
    }
}
