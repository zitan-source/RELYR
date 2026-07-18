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
        ["AppBackground"]="#11141B",["HeaderBackground"]="#101720",["FooterBackground"]="#0B1118",
        ["PaneBackground"]="#111923",["SurfaceBackground"]="#171D27",["CardBackground"]="#1D2330",
        ["ControlBackground"]="#293142",["ControlHoverBackground"]="#354158",["ControlPressedBackground"]="#176B5D",
        ["InputBackground"]="#171D27",["BorderBrush"]="#465168",["SubtleBorderBrush"]="#33465A",
        ["PrimaryText"]="#E8ECF4",["SecondaryText"]="#AAB4C8",["MutedText"]="#8F9CB2",
        ["AccentBrush"]="#72E0C1",["AccentStrongBrush"]="#1F8F7B",["AccentButtonText"]="#FFFFFF",["AccentSoftBrush"]="#244B4A",
        ["WarningBrush"]="#F6C66A",["DangerBrush"]="#E16A78",["DangerBackground"]="#7D2430",
        ["DangerHoverBackground"]="#9B3040",["KeyBackground"]="#1B2733",["ReservedKeyBackground"]="#343B44",
        ["LayerActiveBackground"]="#146B5C"
    };

    static readonly IReadOnlyDictionary<string,string> LightPalette=new Dictionary<string,string>
    {
        ["AppBackground"]="#F3F6FA",["HeaderBackground"]="#FFFFFF",["FooterBackground"]="#E8EEF5",
        ["PaneBackground"]="#EDF2F7",["SurfaceBackground"]="#F8FAFC",["CardBackground"]="#FFFFFF",
        ["ControlBackground"]="#FFFFFF",["ControlHoverBackground"]="#E7F3F0",["ControlPressedBackground"]="#CFE9E3",
        ["InputBackground"]="#FFFFFF",["BorderBrush"]="#B8C5D4",["SubtleBorderBrush"]="#D3DCE7",
        ["PrimaryText"]="#172231",["SecondaryText"]="#526174",["MutedText"]="#6D7C90",
        ["AccentBrush"]="#087B69",["AccentStrongBrush"]="#0B806D",["AccentButtonText"]="#FFFFFF",["AccentSoftBrush"]="#DDF2ED",
        ["WarningBrush"]="#9A5B00",["DangerBrush"]="#B42332",["DangerBackground"]="#FCE8EA",
        ["DangerHoverBackground"]="#F6D3D8",["KeyBackground"]="#FFFFFF",["ReservedKeyBackground"]="#E5E9EE",
        ["LayerActiveBackground"]="#D4EEE8"
    };
}
