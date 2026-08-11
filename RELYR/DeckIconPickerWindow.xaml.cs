using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RELYR;

public partial class DeckIconPickerWindow : Window
{
    string selectedPresetId = "";
    string selectedCustomPath = "";
    internal string SelectedPresetId => selectedPresetId;
    internal string SelectedCustomPath => selectedCustomPath;
    internal int PresetCountForTest => PresetPanel.Children.Count;

    internal DeckIconPickerWindow(string presetId = "", string customPath = "")
    {
        InitializeComponent();
        selectedPresetId = presetId;
        selectedCustomPath = customPath;
        BuildPresets();
        UpdateSelectionPreview();
        SourceInitialized += (_, _) => MainWindow.ApplyWindowsTitleBarTheme(this);
    }

    void BuildPresets()
    {
        foreach (var preset in DeckIconCatalog.Presets)
        {
            var button = new System.Windows.Controls.Button
            {
                Tag = preset,
                Width = 58,
                Height = 58,
                MinWidth = 58,
                Margin = new Thickness(4),
                Padding = new Thickness(0),
                ToolTip = preset.Name,
                Style = (Style)FindResource("AppButtonStyle"),
                Content = new TextBlock { Text = preset.Glyph, FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"), FontSize = 22, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            };
            button.Click += Preset_Click;
            PresetPanel.Children.Add(button);
        }
    }

    void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: DeckIconPreset preset }) return;
        selectedPresetId = preset.Id;
        selectedCustomPath = "";
        UpdateSelectionPreview();
    }

    void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Deckアイコン画像を選択", Filter = "画像ファイル|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico;*.tif;*.tiff|すべてのファイル|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true || !DeckIconCatalog.IsSupportedCustomIcon(dialog.FileName)) return;
        selectedPresetId = "";
        selectedCustomPath = Path.GetFullPath(dialog.FileName);
        UpdateSelectionPreview();
    }

    void Clear_Click(object sender, RoutedEventArgs e)
    {
        selectedPresetId = "";
        selectedCustomPath = "";
        UpdateSelectionPreview();
    }

    void UpdateSelectionPreview()
    {
        var mapping = new Mapping { DeckIcon = selectedPresetId, DeckIconPath = selectedCustomPath };
        SelectedPreview.Child = DeckIconCatalog.CreateVisual(mapping, 26, false);
        SelectedNameText.Text = selectedCustomPath.Length > 0 ? Path.GetFileName(selectedCustomPath) : DeckIconCatalog.Presets.FirstOrDefault(x => x.Id == selectedPresetId)?.Name ?? "アイコンなし";
        foreach (System.Windows.Controls.Button button in PresetPanel.Children)
        {
            bool selected = button.Tag is DeckIconPreset preset && preset.Id == selectedPresetId && selectedCustomPath.Length == 0;
            button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, selected ? "AccentSoftBrush" : "ControlBackground");
            button.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, selected ? "AccentBrush" : "BorderBrush");
        }
    }

    void Apply_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    internal void SelectPresetForTest(string id)
    {
        selectedPresetId = id;
        selectedCustomPath = "";
        UpdateSelectionPreview();
    }
}
