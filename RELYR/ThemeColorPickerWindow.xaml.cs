using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;

namespace RELYR;

public partial class ThemeColorPickerWindow : Window
{
    static readonly string[] PresetColors =
    [
        "#1DA78C", "#2E8B78", "#0A84FF", "#5E5CE6", "#BF5AF2", "#D64F73",
        "#E56B3F", "#D99A24", "#6D8F3E", "#4F708C", "#6B6675", "#2B2E33"
    ];

    bool updating;
    double hue;
    double saturation;
    double brightness;

    internal WpfColor SelectedColor { get; private set; }
    internal int PresetCountForTest => PresetPanel.Children.Count;
    internal string HexTextForTest => HexBox.Text;
    internal bool UsesThemeSurfaceForTest => Equals(Background, ThemeService.Brush("AppBackground"));

    internal ThemeColorPickerWindow(WpfColor initialColor)
    {
        InitializeComponent();
        MainWindow.FollowWindowsTitleBarTheme(this);
        BuildPresets();
        SetColor(initialColor);
    }

    void BuildPresets()
    {
        foreach (string value in PresetColors)
        {
            var color = (WpfColor)System.Windows.Media.ColorConverter.ConvertFromString(value);
            var button = new Button
            {
                Width = 34,
                Height = 34,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(color),
                BorderBrush = ThemeService.Brush("BorderBrush"),
                BorderThickness = new Thickness(1),
                Cursor = WpfCursors.Hand,
                ToolTip = value,
                Tag = color
            };
            button.Click += Preset_Click;
            PresetPanel.Children.Add(button);
        }
    }

    void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: WpfColor color })
            SetColor(color);
    }

    void SetColor(WpfColor color)
    {
        SelectedColor = color;
        RgbToHsv(color, out hue, out saturation, out brightness);
        UpdateControls();
    }

    void UpdateControls()
    {
        updating = true;
        HueSlider.Value = hue;
        SaturationSlider.Value = saturation * 100;
        BrightnessSlider.Value = brightness * 100;
        HueValue.Text = $"{Math.Round(hue):0}°";
        SaturationValue.Text = $"{Math.Round(saturation * 100):0}%";
        BrightnessValue.Text = $"{Math.Round(brightness * 100):0}%";
        string hex = $"#{SelectedColor.R:X2}{SelectedColor.G:X2}{SelectedColor.B:X2}";
        if (!string.Equals(HexBox.Text, hex, StringComparison.OrdinalIgnoreCase))
            HexBox.Text = hex;
        ColorPreview.Background = new SolidColorBrush(SelectedColor);
        SaturationTrack.Background = new LinearGradientBrush(HsvToRgb(hue, 0, brightness), HsvToRgb(hue, 1, brightness), 0);
        BrightnessTrack.Background = new LinearGradientBrush(Colors.Black, HsvToRgb(hue, saturation, 1), 0);
        HexError.Visibility = Visibility.Collapsed;
        updating = false;
    }

    void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updating || HueSlider == null)
            return;
        hue = HueSlider.Value;
        saturation = SaturationSlider.Value / 100d;
        brightness = BrightnessSlider.Value / 100d;
        SelectedColor = HsvToRgb(hue, saturation, brightness);
        UpdateControls();
    }

    void HexBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (updating)
            return;
        if (TryParseHex(HexBox.Text, out var color))
        {
            HexError.Visibility = Visibility.Collapsed;
            SetColor(color);
        }
        else
            HexError.Visibility = Visibility.Visible;
    }

    void HexBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !TryParseHex(HexBox.Text, out var color))
            return;
        SetColor(color);
        DialogResult = true;
        e.Handled = true;
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (!TryParseHex(HexBox.Text, out var color))
        {
            HexError.Visibility = Visibility.Visible;
            HexBox.Focus();
            return;
        }
        SelectedColor = color;
        DialogResult = true;
    }

    internal static bool TryParseHex(string? value, out WpfColor color)
    {
        color = default;
        string text = value?.Trim() ?? "";
        if (text.StartsWith('#'))
            text = text[1..];
        if (text.Length != 6 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            return false;
        color = WpfColor.FromRgb((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        return true;
    }

    static void RgbToHsv(WpfColor color, out double h, out double s, out double v)
    {
        double r = color.R / 255d, g = color.G / 255d, b = color.B / 255d;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        h = delta == 0 ? 0 : max == r ? 60 * (((g - b) / delta) % 6) : max == g ? 60 * (((b - r) / delta) + 2) : 60 * (((r - g) / delta) + 4);
        if (h < 0) h += 360;
        s = max == 0 ? 0 : delta / max;
        v = max;
    }

    static WpfColor HsvToRgb(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };
        return WpfColor.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}
