using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Microsoft.Win32;
using WpfColor = System.Windows.Media.Color;

namespace RELYR;

internal static class ThemeService
{
    static AppThemeMode currentMode = AppThemeMode.System;
    static bool usesDark = true;

    internal static AppThemeMode CurrentMode => currentMode;
    internal static bool UsesDark => usesDark;
    internal static event Action? ThemeChanged;

    internal static void Apply(AppThemeMode mode)
    {
        currentMode = mode;
        usesDark = mode switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => SystemUsesDarkMode()
        };
        var colors = usesDark ? DarkPalette : LightPalette;
        if (System.Windows.Application.Current is { } app)
        {
            foreach (var (key, value) in colors)
            {
                var brush = new SolidColorBrush(Parse(value));
                brush.Freeze();
                app.Resources[key] = brush;
            }
            ApplyMaterialResources(app, usesDark);
        }
        ThemeChanged?.Invoke();
    }

    internal static void RefreshSystemTheme()
    {
        if (currentMode == AppThemeMode.System)
            Apply(AppThemeMode.System);
    }

    internal static bool SystemUsesDarkMode()
    {
        try
        {
            object? value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
            return Convert.ToInt32(value, CultureInfo.InvariantCulture) == 0;
        }
        catch { return false; }
    }

    internal static SolidColorBrush Brush(string key)
    {
        if (System.Windows.Application.Current?.Resources[key] is SolidColorBrush brush)
            return brush;
        return new SolidColorBrush(System.Windows.Media.Colors.Transparent);
    }

    internal static WpfColor Color(string key) => Brush(key).Color;
    static WpfColor Parse(string value) => (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(value);

    static void ApplyMaterialResources(System.Windows.Application app, bool dark)
    {
        app.Resources["MouseBodyBrush"] = Gradient(dark
            ? new[] { ("#454A50", 0d), ("#30343A", 1d) }
            : new[] { ("#FFFFFF", 0d), ("#EAEBED", 1d) });
        app.Resources["CardShadowEffect"] = Shadow(dark ? "#08090A" : "#6B6E73", 20, 5, dark ? .42 : .24);
        app.Resources["GroupCardShadowEffect"] = Shadow(dark ? "#08090A" : "#70747A", 12, 3, dark ? .28 : .18);
        app.Resources["MouseBodyShadowEffect"] = Shadow(dark ? "#08090A" : "#676B71", 18, 7, dark ? .62 : .34);
    }

    static LinearGradientBrush Gradient(IEnumerable<(string Color, double Offset)> stops)
    {
        var brush = new LinearGradientBrush { StartPoint = new System.Windows.Point(0, 0), EndPoint = new System.Windows.Point(0, 1) };
        foreach (var (color, offset) in stops)
            brush.GradientStops.Add(new GradientStop(Parse(color), offset));
        brush.Freeze();
        return brush;
    }

    static DropShadowEffect Shadow(string color, double blurRadius, double depth, double opacity) => new()
    {
        Color = Parse(color),
        BlurRadius = blurRadius,
        ShadowDepth = depth,
        Direction = 270,
        Opacity = opacity,
        RenderingBias = RenderingBias.Performance
    };

    static readonly IReadOnlyDictionary<string, string> DarkPalette = new Dictionary<string, string>
    {
        ["AppBackground"] = "#161719",
        ["HeaderBackground"] = "#1A1B1D",
        ["FooterBackground"] = "#191A1C",
        ["PaneBackground"] = "#191A1C",
        ["SurfaceBackground"] = "#1D1F21",
        ["CardBackground"] = "#212326",
        ["ControlBackground"] = "#292C30",
        ["DeckPreviewCellBackground"] = "#292C30",
        ["ControlHoverBackground"] = "#34383D",
        ["ControlPressedBackground"] = "#183F38",
        ["InputBackground"] = "#1C1E20",
        ["BorderBrush"] = "#41454B",
        ["SubtleBorderBrush"] = "#2D3034",
        ["PrimaryText"] = "#F4F4F5",
        ["SecondaryText"] = "#A9ADB3",
        ["MutedText"] = "#7D828A",
        ["AccentBrush"] = "#1DA78C",
        ["AccentTextBrush"] = "#1DA78C",
        ["AccentStrongBrush"] = "#168D76",
        ["AccentButtonText"] = "#FFFFFF",
        ["AccentSoftBrush"] = "#183F38",
        ["WarningBrush"] = "#FFD60A",
        ["DangerBrush"] = "#FF453A",
        ["DangerBackground"] = "#512623",
        ["DangerHoverBackground"] = "#69302C",
        ["DangerForeground"] = "#FFFFFF",
        ["DangerHoverForeground"] = "#FFFFFF",
        ["KeyBackground"] = "#272A2E",
        ["ReservedKeyBackground"] = "#30343A",
        ["KeyDepthBrush"] = "#111214",
        ["LayerActiveBackground"] = "#183F38",
        ["EditingKeyBackground"] = "#1DA78C",
        ["EditingKeyBorderBrush"] = "#6BD7C0",
        ["ActionKeyIconBrush"] = "#F09A3E",
        ["ActionDisabledIconBrush"] = "#AAB4C2",
        ["ActionProfileIconBrush"] = "#68A7FF",
        ["ActionShortcutIconBrush"] = "#52D5BE",
        ["ActionTextIconBrush"] = "#E4B936",
        ["ActionLaunchIconBrush"] = "#AA78DA",
        ["ActionMacroIconBrush"] = "#E15A65"
    };

    static readonly IReadOnlyDictionary<string, string> LightPalette = new Dictionary<string, string>
    {
        ["AppBackground"] = "#F3F3F3",
        ["HeaderBackground"] = "#FAFAFB",
        ["FooterBackground"] = "#F7F7F8",
        ["PaneBackground"] = "#F7F7F8",
        ["SurfaceBackground"] = "#F8F8F9",
        ["CardBackground"] = "#FFFFFF",
        ["ControlBackground"] = "#FBFBFC",
        ["DeckPreviewCellBackground"] = "#E1E4E8",
        ["ControlHoverBackground"] = "#F0F1F3",
        ["ControlPressedBackground"] = "#DDF4EF",
        ["InputBackground"] = "#FFFFFF",
        ["BorderBrush"] = "#D6D8DC",
        ["SubtleBorderBrush"] = "#E5E6E8",
        ["PrimaryText"] = "#202124",
        ["SecondaryText"] = "#676B73",
        ["MutedText"] = "#6C7078",
        ["AccentBrush"] = "#1DA78C",
        ["AccentTextBrush"] = "#087B69",
        ["AccentStrongBrush"] = "#087B69",
        ["AccentButtonText"] = "#FFFFFF",
        ["AccentSoftBrush"] = "#DDF4EF",
        ["WarningBrush"] = "#9A6700",
        ["DangerBrush"] = "#D70015",
        ["DangerBackground"] = "#D70015",
        ["DangerHoverBackground"] = "#FFE5E7",
        ["DangerForeground"] = "#FFFFFF",
        ["DangerHoverForeground"] = "#A50011",
        ["KeyBackground"] = "#FBFBFC",
        ["ReservedKeyBackground"] = "#EEF0F2",
        ["KeyDepthBrush"] = "#D4D6DA",
        ["LayerActiveBackground"] = "#DDF4EF",
        ["EditingKeyBackground"] = "#1DA78C",
        ["EditingKeyBorderBrush"] = "#147562",
        ["ActionKeyIconBrush"] = "#B85B00",
        ["ActionDisabledIconBrush"] = "#596575",
        ["ActionProfileIconBrush"] = "#075EAD",
        ["ActionShortcutIconBrush"] = "#087B69",
        ["ActionTextIconBrush"] = "#8A6500",
        ["ActionLaunchIconBrush"] = "#7040A3",
        ["ActionMacroIconBrush"] = "#B42332"
    };
}
