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
    internal int AnimatedPresetCountForTest => AnimatedPresetPanel.Children.Count;

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
            PresetPanel.Children.Add(CreatePresetButton(preset, false));
            AnimatedPresetPanel.Children.Add(CreatePresetButton(preset, true));
        }
        ShowPresetCategory(DeckIconCatalog.IsAnimatedPreset(selectedPresetId));
    }

    System.Windows.Controls.Button CreatePresetButton(DeckIconPreset preset, bool animated)
    {
        string id = animated ? DeckIconCatalog.AnimatedId(preset.Id) : preset.Id;
        var button = new System.Windows.Controls.Button
        {
            Tag = id,
            Width = 58,
            Height = 58,
            MinWidth = 58,
            Margin = new Thickness(4),
            Padding = new Thickness(0),
            ToolTip = animated ? preset.Name + "（GIFアニメ）" : preset.Name,
            Style = (Style)FindResource("AppButtonStyle"),
            Content = DeckIconCatalog.CreateVisual(new Mapping { DeckIcon = id }, 22, false)
        };
        button.Click += Preset_Click;
        return button;
    }

    void Preset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string id }) return;
        selectedPresetId = id;
        selectedCustomPath = "";
        UpdateSelectionPreview();
    }

    void StaticPresetTab_Click(object sender, RoutedEventArgs e) => ShowPresetCategory(false);
    void AnimatedPresetTab_Click(object sender, RoutedEventArgs e) => ShowPresetCategory(true);

    void ShowPresetCategory(bool animated)
    {
        StaticPresetScroll.Visibility = animated ? Visibility.Collapsed : Visibility.Visible;
        AnimatedPresetScroll.Visibility = animated ? Visibility.Visible : Visibility.Collapsed;
        StaticPresetTabButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, animated ? "ControlBackground" : "AccentSoftBrush");
        StaticPresetTabButton.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, animated ? "BorderBrush" : "AccentBrush");
        AnimatedPresetTabButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, animated ? "AccentSoftBrush" : "ControlBackground");
        AnimatedPresetTabButton.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, animated ? "AccentBrush" : "BorderBrush");
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
        string plainPresetId = DeckIconCatalog.IsAnimatedPreset(selectedPresetId) ? selectedPresetId[DeckIconCatalog.AnimatedPrefix.Length..] : selectedPresetId;
        string presetName = DeckIconCatalog.Presets.FirstOrDefault(x => x.Id == plainPresetId)?.Name ?? "アイコンなし";
        SelectedNameText.Text = selectedCustomPath.Length > 0 ? Path.GetFileName(selectedCustomPath) : DeckIconCatalog.IsAnimatedPreset(selectedPresetId) ? presetName + "（GIFアニメ）" : presetName;
        foreach (System.Windows.Controls.Button button in PresetPanel.Children.Cast<System.Windows.Controls.Button>().Concat(AnimatedPresetPanel.Children.Cast<System.Windows.Controls.Button>()))
        {
            bool selected = button.Tag is string id && id == selectedPresetId && selectedCustomPath.Length == 0;
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
