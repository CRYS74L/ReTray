using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace TrayAssistant_App.Controls;

public sealed class SoftMicaBackdrop : SystemBackdrop
{
    private MicaController? controller;
    private bool? darkAppearance;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnTargetConnected(target, root);
        var configuration = GetDefaultSystemBackdropConfiguration(target, root);
        controller = new MicaController { Kind = MicaKind.BaseAlt };
        ApplyAppearance(configuration, root);
        controller.SetSystemBackdropConfiguration(configuration);
        controller.AddSystemBackdropTarget(target);
    }

    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot root)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, root);
        ApplyAppearance(GetDefaultSystemBackdropConfiguration(target, root), root);
    }

    private void ApplyAppearance(SystemBackdropConfiguration configuration, XamlRoot root)
    {
        if (controller == null) return;
        var dark = configuration.Theme == SystemBackdropTheme.Dark ||
            (configuration.Theme == SystemBackdropTheme.Default && root.Content is FrameworkElement { ActualTheme: ElementTheme.Dark });
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
        controller?.RemoveSystemBackdropTarget(target);
        controller?.Dispose();
        controller = null;
        darkAppearance = null;
        base.OnTargetDisconnected(target);
    }
}
