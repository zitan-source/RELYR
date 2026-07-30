using Microsoft.Win32;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using WpfColor=System.Windows.Media.Color;

namespace RELYR;

internal static class ThemeService
{
    static AppThemeMode currentMode=AppThemeMode.System;
    static bool usesDark=true;

    internal static AppThemeMode CurrentMode=>currentMode;
    internal static bool UsesDark=>usesDark;
    internal static event Action? ThemeChanged;

    internal static void Apply(AppThemeMode mode)
    {
        currentMode=mode;
        usesDark=mode switch
        {
            AppThemeMode.Dark=>true,
            AppThemeMode.Light=>false,
            _=>SystemUsesDarkMode()
        };
        var colors=usesDark?DarkPalette:LightPalette;
        if(System.Windows.Application.Current is { } app)
            foreach(var (key,value) in colors)app.Resources[key]=new SolidColorBrush(Parse(value));
        ThemeChanged?.Invoke();
    }

    internal static void RefreshSystemTheme()
    {
        if(currentMode==AppThemeMode.System)Apply(AppThemeMode.System);
    }

    internal static bool SystemUsesDarkMode()
    {
        try
        {
            object? value=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1);
            return Convert.ToInt32(value,CultureInfo.InvariantCulture)==0;
        }
        catch{return false;}
    }

    internal static SolidColorBrush Brush(string key)
    {
        if(System.Windows.Application.Current?.Resources[key] is SolidColorBrush brush)return brush;
        return new SolidColorBrush(System.Windows.Media.Colors.Transparent);
    }

    internal static WpfColor Color(string key)=>Brush(key).Color;
    static WpfColor Parse(string value)=>(WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(value);

    static readonly IReadOnlyDictionary<string,string> DarkPalette=new Dictionary<string,string>
    {
        ["AppBackground"]="#151719",["HeaderBackground"]="#1C1E21",["FooterBackground"]="#17191C",
        ["PaneBackground"]="#1C1E21",["SurfaceBackground"]="#202225",["CardBackground"]="#25282C",
        ["ControlBackground"]="#2B2E33",["ControlHoverBackground"]="#363A40",["ControlPressedBackground"]="#253D57",
        ["InputBackground"]="#202225",["BorderBrush"]="#484B50",["SubtleBorderBrush"]="#35383D",
        ["PrimaryText"]="#F2F2F3",["SecondaryText"]="#A7ABB1",["MutedText"]="#7F848C",
        ["AccentBrush"]="#0A84FF",["AccentStrongBrush"]="#0A84FF",["AccentButtonText"]="#FFFFFF",["AccentSoftBrush"]="#193B5E",
        ["WarningBrush"]="#FFD60A",["DangerBrush"]="#FF453A",["DangerBackground"]="#512623",
        ["DangerHoverBackground"]="#69302C",["DangerForeground"]="#FFFFFF",["DangerHoverForeground"]="#FFFFFF",
        ["KeyBackground"]="#292C30",["ReservedKeyBackground"]="#35383C",
        ["LayerActiveBackground"]="#153A5F",["EditingKeyBackground"]="#0A84FF",["EditingKeyBorderBrush"]="#64B5FF",
        ["ActionKeyIconBrush"]="#F09A3E",["ActionDisabledIconBrush"]="#AAB4C2",["ActionProfileIconBrush"]="#68A7FF",["ActionShortcutIconBrush"]="#52D5BE",
        ["ActionTextIconBrush"]="#E4B936",["ActionLaunchIconBrush"]="#AA78DA",["ActionMacroIconBrush"]="#E15A65"
    };

    static readonly IReadOnlyDictionary<string,string> LightPalette=new Dictionary<string,string>
    {
        ["AppBackground"]="#F5F5F7",["HeaderBackground"]="#FFFFFF",["FooterBackground"]="#F0F0F2",
        ["PaneBackground"]="#F2F2F4",["SurfaceBackground"]="#FAFAFB",["CardBackground"]="#FFFFFF",
        ["ControlBackground"]="#FFFFFF",["ControlHoverBackground"]="#E9E9EC",["ControlPressedBackground"]="#DCEBFA",
        ["InputBackground"]="#FFFFFF",["BorderBrush"]="#C7C7CC",["SubtleBorderBrush"]="#DEDEE2",
        ["PrimaryText"]="#1D1D1F",["SecondaryText"]="#626268",["MutedText"]="#86868B",
        ["AccentBrush"]="#007AFF",["AccentStrongBrush"]="#007AFF",["AccentButtonText"]="#FFFFFF",["AccentSoftBrush"]="#E1F0FF",
        ["WarningBrush"]="#9A6700",["DangerBrush"]="#D70015",["DangerBackground"]="#D70015",
        ["DangerHoverBackground"]="#FFE5E7",["DangerForeground"]="#FFFFFF",["DangerHoverForeground"]="#A50011",
        ["KeyBackground"]="#FFFFFF",["ReservedKeyBackground"]="#E7E7EA",
        ["LayerActiveBackground"]="#DDEEFF",["EditingKeyBackground"]="#007AFF",["EditingKeyBorderBrush"]="#005FC7",
        ["ActionKeyIconBrush"]="#B85B00",["ActionDisabledIconBrush"]="#596575",["ActionProfileIconBrush"]="#075EAD",["ActionShortcutIconBrush"]="#087B69",
        ["ActionTextIconBrush"]="#8A6500",["ActionLaunchIconBrush"]="#7040A3",["ActionMacroIconBrush"]="#B42332"
    };
}
