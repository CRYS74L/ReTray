using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;

namespace LanDeviceMonitor.Services;

public static class ThemeService
{
    internal static event Action? PaletteChanged;
    public const string SystemMode = "System";
    public const string LightMode = "Light";
    public const string DarkMode = "Dark";
    public static string CurrentMode { get; private set; } = SystemMode;

    private static readonly IReadOnlyDictionary<string, string> LightPalette =
        new Dictionary<string, string>
        {
            ["SegmentInactiveTextColor"] = "#71717A",
            ["SegmentTrackColor"] = "#08000000",
            ["SegmentSelectionColor"] = "#EEFFFFFF",
            ["SwitchOnColor"] = "#628EF7",
            ["SwitchOffColor"] = "#B3D1D1D6",
            ["AccentColor"] = "#4D7EF2",
            ["AccentHoverColor"] = "#416FDF",
            ["AccentPressedColor"] = "#3560CB",
            ["AccentLightColor"] = "#DEFFFFFF",
            ["ShellPanelColor"] = "#70FFFFFF",
            ["PlotSurfaceColor"] = "#80FAFAFA",
            ["MicaPanelColor"] = "#0CFFFFFF",
            ["MicaCardColor"] = "#70FFFFFF",
            ["MicaStrongColor"] = "#88FFFFFF",
            ["FloatingSurfaceColor"] = "#E8FFFFFF",
            ["TrayMoreHoverColor"] = "#90FFFFFF",
            ["MeshGatewayColor"] = "#88FFFFFF",
            ["SurfaceColor"] = "#70FFFFFF",
            ["SurfaceStrongColor"] = "#88FFFFFF",
            ["WindowColor"] = "#08FFFFFF",
            ["SidebarColor"] = "#08FFFFFF",
            ["DeviceIconColor"] = "#66C4D7FF",
            ["OfflineDeviceIconColor"] = "#30939CAA",
            ["DangerTextColor"] = "#C4362E",
            ["DangerSurfaceColor"] = "#90FFF0F0",
            ["RestartSurfaceColor"] = "#66FFD0CC",
            ["DangerConfirmColor"] = "#C24246",
            ["DangerConfirmPressedColor"] = "#AA3B3F",
            ["TextColor"] = "#182034",
            ["MutedTextColor"] = "#667087",
            ["CaptionBackIdleColor"] = "#737373",
            ["CaptionBackHoverColor"] = "#000000",
            ["BorderColor"] = "#9AD4D4D4",
            ["FooterButtonBorderColor"] = "#88939CAA",
            ["FooterIconHoverColor"] = "#4F5D76",
            ["FooterHoverColor"] = "#E0FFFFFF",
            ["FooterPressedColor"] = "#14000000",
            ["DevicePressedColor"] = "#0A000000",
            ["SectionTitleColor"] = "#454C5C",
            ["StudioInputSurfaceColor"] = "#FFFFFF",
            ["StudioSurfaceColor"] = "#FFFFFF",
            ["StudioHoverColor"] = "#F0F0F2",
            ["StudioTextColor"] = "#242426",
            ["StudioSelectedTextColor"] = "#4D7EF2",
            ["StudioHoverBorderColor"] = "#C2C2C6",
            ["StudioKeyboardColor"] = "#EEEEF0",
            ["InputBorderColor"] = "#E5E5E8",
            ["ControlColor"] = "#FFFFFF",
            ["ControlHoverColor"] = "#F8F8FA",
            ["ComboBoxColor"] = "#FFFFFF",
            ["ComboBoxHoverColor"] = "#DCFFFFFF",
            ["ComboBoxPopupColor"] = "#FFFFFF",
            ["QuietControlColor"] = "#B0F5F5F5",
            ["CaptionHoverColor"] = "#2B000000",
            ["CaptionPressedColor"] = "#3D000000",
            ["ToolTipColor"] = "#F2FFFFFF",
            ["WarningSurfaceColor"] = "#F0FFF4E5",
            ["ConfirmSurfaceColor"] = "#8CFFFFFF",
            ["ProgressInfoColor"] = "#DCEBF1FF",
            ["ProgressSuccessColor"] = "#DCDDF5E8",
            ["ProgressErrorColor"] = "#E6FEE2E2",
            ["ListItemColor"] = "#98FFFFFF",
            ["ListItemHoverColor"] = "#C0FFFFFF",
            ["DeviceListItemColor"] = "#99FFFFFF",
            ["DeviceListItemSelectedColor"] = "#C0FFFFFF",
            ["DeviceListItemHoverColor"] = "#BFFFFFFF",
            ["DeviceListItemSelectedHoverColor"] = "#00FFFFFF",
            ["OnlineCardColor"] = "#84D9F8E6",
            ["OfflineCardColor"] = "#84FFEAEA",
            ["OnlineCardBorderColor"] = "#704CC88A",
            ["OfflineCardBorderColor"] = "#40E36C6C",
            ["StatusOnlineColor"] = "#A8DDF5E8",
            ["StatusOfflineColor"] = "#A8FCE8E8",
            ["StatusOnlineTextColor"] = "#14794A",
            ["StatusOfflineTextColor"] = "#B64141",
            ["TimelineOnlineDotColor"] = "#6CE09B",
            ["TimelineOfflineDotColor"] = "#FF9183",
            ["TimelineItemColor"] = "#40FFFFFF",
            ["TimelineItemBorderColor"] = "#70D6D6D6",
            ["TimelineLineColor"] = "#A8A8A8",
            ["TimelineDurationColor"] = "#B0FFFFFF",
            ["ScrollThumbColor"] = "#4D666666",
            ["ScrollThumbHoverColor"] = "#8F666666",
            ["TrafficUploadColor"] = "#FF9876",
            ["TrafficDownloadColor"] = "#0B80FF",
            ["TrafficUploadSurfaceColor"] = "#FFF5F0",
            ["TrafficDownloadSurfaceColor"] = "#F1F7FF",
            ["TrafficUploadIconColor"] = "#FFE8DD",
            ["TrafficDownloadIconColor"] = "#DCEBFF",
            ["TrafficChartColor"] = "#ECECEC",
            ["TrafficChartGridColor"] = "#C8C8C8",
            ["TrafficChartLabelColor"] = "#7C8498"
        };

    private static readonly IReadOnlyDictionary<string, string> DarkPalette =
        new Dictionary<string, string>
        {
            ["SegmentInactiveTextColor"] = "#B0B0B8",
            ["SegmentTrackColor"] = "#B8232323",
            ["SegmentSelectionColor"] = "#CC3E3E3E",
            ["SwitchOnColor"] = "#7EA2FF",
            ["SwitchOffColor"] = "#99545454",
            ["AccentColor"] = "#7EA2FF",
            ["AccentHoverColor"] = "#91B1FF",
            ["AccentPressedColor"] = "#6D91F4",
            ["AccentLightColor"] = "#604A4A4A",
            ["ShellPanelColor"] = "#88181818",
            ["PlotSurfaceColor"] = "#C01C1C1C",
            ["MicaPanelColor"] = "#09202020",
            ["MicaCardColor"] = "#A4242424",
            ["MicaStrongColor"] = "#B8323232",
            ["FloatingSurfaceColor"] = "#EE323232",
            ["TrayMoreHoverColor"] = "#28FFFFFF",
            ["MeshGatewayColor"] = "#A4242424",
            ["SurfaceColor"] = "#A4242424",
            ["SurfaceStrongColor"] = "#B8323232",
            ["WindowColor"] = "#09181818",
            ["SidebarColor"] = "#09181818",
            ["DeviceIconColor"] = "#403A5C86",
            ["OfflineDeviceIconColor"] = "#20939CAA",
            ["DangerTextColor"] = "#FF8F8A",
            ["DangerSurfaceColor"] = "#5C55282D",
            ["RestartSurfaceColor"] = "#40754E55",
            ["DangerConfirmColor"] = "#C24246",
            ["DangerConfirmPressedColor"] = "#AA3B3F",
            ["TextColor"] = "#F2F5FB",
            ["MutedTextColor"] = "#AAB4C8",
            ["CaptionBackIdleColor"] = "#A3A3A3",
            ["CaptionBackHoverColor"] = "#FFFFFF",
            ["BorderColor"] = "#30575757",
            ["FooterButtonBorderColor"] = "#996F6F6F",
            ["FooterIconHoverColor"] = "#FAFBFD",
            ["FooterHoverColor"] = "#24FFFFFF",
            ["FooterPressedColor"] = "#24000000",
            ["DevicePressedColor"] = "#14000000",
            ["SectionTitleColor"] = "#D1D7E2",
            ["StudioInputSurfaceColor"] = "#181818",
            ["StudioSurfaceColor"] = "#2B2B2B",
            ["StudioHoverColor"] = "#383838",
            ["StudioTextColor"] = "#F5F5F7",
            ["StudioSelectedTextColor"] = "#7EA2FF",
            ["StudioHoverBorderColor"] = "#606060",
            ["StudioKeyboardColor"] = "#353535",
            ["InputBorderColor"] = "#3A3A3A",
            ["ControlColor"] = "#2B2B2B",
            ["ControlHoverColor"] = "#383838",
            ["ComboBoxColor"] = "#2B2B2B",
            ["ComboBoxHoverColor"] = "#96505050",
            ["ComboBoxPopupColor"] = "#2B2B2B",
            ["QuietControlColor"] = "#A0323232",
            ["CaptionHoverColor"] = "#52000000",
            ["CaptionPressedColor"] = "#68000000",
            ["ToolTipColor"] = "#E0363636",
            ["WarningSurfaceColor"] = "#806B532D",
            ["ConfirmSurfaceColor"] = "#B0363636",
            ["ProgressInfoColor"] = "#705A6170",
            ["ProgressSuccessColor"] = "#7042574C",
            ["ProgressErrorColor"] = "#705C4248",
            ["ListItemColor"] = "#702A2A2A",
            ["ListItemHoverColor"] = "#90343434",
            ["DeviceListItemColor"] = "#90303030",
            ["DeviceListItemSelectedColor"] = "#C03B3B3B",
            ["DeviceListItemHoverColor"] = "#90343434",
            ["DeviceListItemSelectedHoverColor"] = "#00FFFFFF",
            ["OnlineCardColor"] = "#6319583D",
            ["OfflineCardColor"] = "#635B1B2E",
            ["OnlineCardBorderColor"] = "#9067DDA6",
            ["OfflineCardBorderColor"] = "#8CE36E82",
            ["StatusOnlineColor"] = "#7E2A5A45",
            ["StatusOfflineColor"] = "#7E5A303A",
            ["StatusOnlineTextColor"] = "#9DE3BE",
            ["StatusOfflineTextColor"] = "#FFB6B6",
            ["TimelineOnlineDotColor"] = "#6CE09B",
            ["TimelineOfflineDotColor"] = "#FF9183",
            ["TimelineItemColor"] = "#B02E2E2E",
            ["TimelineItemBorderColor"] = "#305A5A5A",
            ["TimelineLineColor"] = "#898989",
            ["TimelineDurationColor"] = "#B03D3D3D",
            ["ScrollThumbColor"] = "#40666666",
            ["ScrollThumbHoverColor"] = "#807A7A7A",
            ["TrafficUploadColor"] = "#FFB092",
            ["TrafficDownloadColor"] = "#70AFFF",
            ["TrafficUploadSurfaceColor"] = "#483229",
            ["TrafficDownloadSurfaceColor"] = "#263C5B",
            ["TrafficUploadIconColor"] = "#6E4032",
            ["TrafficDownloadIconColor"] = "#304E76",
            ["TrafficChartColor"] = "#353535",
            ["TrafficChartGridColor"] = "#40666666",
            ["TrafficChartLabelColor"] = "#A6A6A6"
        };

    public static string NormalizeMode(string? mode) =>
        mode is LightMode or DarkMode ? mode : SystemMode;

    public static bool ResolveIsDark(string? mode)
    {
        var normalized = NormalizeMode(mode);
        if (normalized == DarkMode)
        {
            return true;
        }

        if (normalized == LightMode)
        {
            return false;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool Apply(string? mode)
    {
        CurrentMode = NormalizeMode(mode);
        var isDark = ResolveIsDark(CurrentMode);
        var palette = isDark ? DarkPalette : LightPalette;
        var resources = System.Windows.Application.Current.Resources;

        foreach (var (colorKey, colorValue) in palette)
        {
            var color = (System.Windows.Media.Color)
                System.Windows.Media.ColorConverter.ConvertFromString(colorValue);
            resources[colorKey] = color;
            var brushKey = colorKey.Replace("Color", "Brush", StringComparison.Ordinal);
            if (resources[brushKey] is SolidColorBrush brush && !brush.IsFrozen)
            {
                brush.Color = color;
            }
            else
            {
                resources[brushKey] = new SolidColorBrush(color);
            }
        }

        PaletteChanged?.Invoke();
        return isDark;
    }
}
