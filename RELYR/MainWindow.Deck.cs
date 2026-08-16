using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBox = System.Windows.Controls.TextBox;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfMessageBox = RELYR.AppDialog;

namespace RELYR;

/// <summary>
/// Deck layout editing, Deck button file actions, and overlay synchronization.
/// This partial keeps the main window orchestration separate from Deck-specific UI behavior.
/// </summary>
public partial class MainWindow
{
    MediaPlayer? deckEditorAudioPlayer;
    System.Windows.Controls.Primitives.Popup? deckEditorThumbnailPopup;

    // Deck button metadata and context actions
    void RenameDeckButton(string input)
    {
        var mappings = MappingCollectionForInput(input);
        var mapping = mappings.LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
        string? name = PromptText("Deckボタン名", "ボタンの下に表示する名前", mapping?.Description ?? "");
        if (name == null)
            return;
        SetDeckButtonName(input, name);
    }
    void SetDeckButtonName(string input, string name)
    {
        var mappings = MappingCollectionForInput(input);
        var mapping = mappings.LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
        if (mapping == null)
        {
            mapping = new Mapping { Input = input, Layer = DeckPanelLayout.Layer };
            mappings.Add(mapping);
        }
        mapping.Description = name;
        if (selected?.Input.Equals(input, StringComparison.OrdinalIgnoreCase) == true)
        {
            selected.Description = name;
            if (DeckNameBox != null && !string.Equals(DeckNameBox.Text, name, StringComparison.Ordinal))
            {
                loading = true;
                DeckNameBox.Text = name;
                loading = false;
            }
        }
        if (!HasDeckButtonContent(mapping))
            mappings.Remove(mapping);
        MarkDirty();
        RefreshSelectedInputVisual(input);
    }
    void DeckNameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (loading || selected == null || !DeckPanelLayout.IsInputName(selected.Input))
            return;
        selected.Description = DeckNameBox.Text.Trim();
        if (HasDeckButtonContent(selected))
        {
            var mappings = MappingCollectionForInput(selected.Input);
            if (!mappings.Contains(selected))
                mappings.Add(selected);
        }
        else
            MappingCollectionForInput(selected.Input).Remove(selected);
        // Keep typing independent of the potentially 324-button live overlay.
        // Update only the selected editor button so its label remains live.
        MarkDirty(refreshDeckPanel: false);
        RefreshSelectedInputVisual(selected.Input);
    }
    static bool HasDeckButtonContent(Mapping? mapping) => MappingHasConfiguredAction(mapping) || !string.IsNullOrWhiteSpace(mapping?.Description) || !string.IsNullOrWhiteSpace(mapping?.DeckColor) || DeckPanelLayout.HasRegisteredFile(mapping) || DeckIconCatalog.HasIcon(mapping);
    void SetDeckButtonFile(string input, string path)
    {
        if (!DeckPanelLayout.IsInputName(input))
            return;
        string normalized = "";
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                normalized = Path.GetFullPath(path.Trim());
            }
            catch { ShowInlineNotice("ファイルの場所を読み取れません"); return; }
        }
        var mappings = MappingCollectionForInput(input);
        var mapping = mappings.LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
        if (mapping == null && normalized.Length > 0)
        {
            mapping = new Mapping { Input = input, Layer = DeckPanelLayout.Layer };
            mappings.Add(mapping);
        }
        if (mapping == null)
            return;
        mapping.DeckFilePath = normalized;
        if (selected?.Input.Equals(input, StringComparison.OrdinalIgnoreCase) == true)
            selected.DeckFilePath = normalized;
        if (!HasDeckButtonContent(mapping))
            mappings.Remove(mapping);
        UpdateDeckFileDropTarget();
        MarkDirty();
        RefreshSelectedInputVisual(input);
    }
    void DeckFileSelect_Click(object sender, RoutedEventArgs e)
    {
        if (selected == null || !DeckPanelLayout.IsInputName(selected.Input))
            return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Deckに登録するファイルを選択", CheckFileExists = true, Multiselect = false, Filter = "すべてのファイル|*.*" };
        if (dialog.ShowDialog(this) == true)
            SetDeckButtonFile(selected.Input, dialog.FileName);
    }
    void DeckFileDropTarget_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        bool valid = !DeckPanelLayout.IsInternalFileDrag(e.Data) && DeckPanelLayout.GetDroppedFile(e.Data) != null;
        e.Effects = valid ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        DeckFileDropTarget.Background = valid ? ThemeService.Brush("AccentSoftBrush") : ThemeService.Brush("ControlBackground");
        e.Handled = true;
    }
    void DeckFileDropTarget_DragLeave(object sender, System.Windows.DragEventArgs e) => DeckFileDropTarget.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
    void DeckFileDropTarget_Drop(object sender, System.Windows.DragEventArgs e)
    {
        DeckFileDropTarget.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
        if (selected == null || !DeckPanelLayout.IsInputName(selected.Input))
            return;
        if (DeckPanelLayout.IsInternalFileDrag(e.Data))
        {
            e.Handled = true;
            return;
        }
        string? file = DeckPanelLayout.GetDroppedFile(e.Data);
        if (file == null)
        {
            ShowInlineNotice("登録できるファイルがありません");
            return;
        }
        SetDeckButtonFile(selected.Input, file);
        e.Handled = true;
    }
    void UpdateDeckFileDropTarget()
    {
        if (DeckFileDropTarget == null)
            return;
        string path = selected?.DeckFilePath ?? "";
        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        FrameworkElement icon;
        var thumbnail = DeckPanelLayout.LoadFileThumbnail(path, 120);
        if (thumbnail != null)
            icon = new System.Windows.Controls.Image { Source = thumbnail, Width = 58, Height = 48, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 10, 0) };
        else
            icon = new TextBlock { Text = DeckPanelLayout.IsAudioFile(path) ? "▶" : DeckPanelLayout.IsTextFile(path) ? "\uE8A5" : "\uE8B7", FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons"), FontSize = DeckPanelLayout.IsAudioFile(path) ? 22 : 19, Width = 58, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        if (thumbnail == null)
        {
            icon = DeckPanelLayout.CreateFileIcon(path, DeckPanelLayout.IsAudioFile(path) ? 22 : 19);
            icon.Width = 58;
            icon.Margin = new Thickness(0, 0, 10, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
        }
        content.Children.Add(icon);
        string name = DeckPanelLayout.FileDisplayName(selected);
        var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        labels.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(name) ? "ファイルを選択" : name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        labels.Children.Add(new TextBlock { Text = string.IsNullOrWhiteSpace(path) ? "クリックまたはドロップして登録" : File.Exists(path) ? "ドラッグして利用" : "ファイルが見つかりません", FontSize = 11, Foreground = ThemeService.Brush(File.Exists(path) || string.IsNullOrWhiteSpace(path) ? "SecondaryText" : "DangerBrush"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0) });
        Grid.SetColumn(labels, 1);
        content.Children.Add(labels);
        DeckFileDropTarget.Content = content;
    }
    void DeckHoverPreviewChanged(object sender, RoutedEventArgs e)
    {
        if (updatingDeckEditor || config == null)
            return;
        config.DeckHoverPreviewsEnabled = DeckHoverPreviewBox.IsChecked == true;
        MarkDirty(refreshDeckPanel: false);
    }
    internal void SetDeckButtonNameForTest(string input, string name) => SetDeckButtonName(input, name);
    internal ContextMenu CreateDeckInputContextMenu(string input)
    {
        var mappings = MappingCollectionForInput(input);
        var menu = new ContextMenu { MinWidth = 242 };
        var rename = CreateDeckContextMenuItem("\uE70F", "名前の変更...", "");
        rename.Click += (_, _) => RenameDeckButton(input);
        var copy = CreateDeckContextMenuItem("\uE8C8", "コピー", "");
        copy.Click += (_, _) => { var mapping = mappings.LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase)); if (mapping != null) CopyDeckFileToClipboard(mapping); };
        var paste = CreateDeckContextMenuItem("\uE77F", "貼り付け", "");
        paste.Click += (_, _) => { string? file = ClipboardDeckFile(); if (file != null) SetDeckButtonFile(input, file); };
        var reveal = CreateDeckContextMenuItem("\uE838", "ファイルの場所を開く", "");
        reveal.Click += (_, _) => RevealDeckFile(input);
        var color = CreateDeckContextMenuItem("\uE790", "色を変更...", "");
        color.Click += (_, _) => ChooseDeckButtonColor(input);
        var icon = CreateDeckContextMenuItem("\uE8B9", "アイコン変更...", "");
        icon.Click += (_, _) => ChooseDeckButtonIcon(input);
        var resetColor = CreateDeckContextMenuItem("\uE777", "色を標準に戻す", "");
        resetColor.Click += (_, _) => SetDeckButtonColor(input, "");
        var delete = CreateDeckContextMenuItem("\uE74D", "削除", "Del", true);
        delete.Click += (_, _) => { if (mappings.RemoveAll(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase)) > 0) { ClearSelectedInput(); MarkDirty(); RefreshSelectedInputVisual(input); } };
        menu.Items.Add(rename);
        menu.Items.Add(new Separator());
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(reveal);
        menu.Items.Add(new Separator());
        menu.Items.Add(color);
        menu.Items.Add(icon);
        menu.Items.Add(resetColor);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        menu.Opened += (_, _) =>
        {
            var existing = mappings.LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
            copy.IsEnabled = DeckPanelLayout.IsAvailableFile(existing);
            paste.IsEnabled = ClipboardDeckFile() != null;
            reveal.IsEnabled = DeckPanelLayout.IsAvailableFile(existing);
            resetColor.IsEnabled = DeckPanelLayout.TryGetButtonColor(existing, out _);
            delete.IsEnabled = existing != null;
        };
        return menu;
    }
    static MenuItem CreateDeckContextMenuItem(string icon, string label, string shortcut, bool danger = false)
    {
        var header = new Grid { Width = 208, Height = 30 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var color = danger ? ThemeService.Brush("DangerBrush") : ThemeService.Brush("AccentBrush");
        header.Children.Add(new TextBlock { Text = icon, FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 15, Foreground = color, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Center });
        var text = new TextBlock { Text = label, FontSize = 13.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Foreground = danger ? ThemeService.Brush("DangerBrush") : ThemeService.Brush("PrimaryText") };
        Grid.SetColumn(text, 2);
        header.Children.Add(text);
        if (shortcut.Length > 0)
        {
            var key = new TextBlock { Text = shortcut, FontSize = 10.5, Foreground = ThemeService.Brush("SecondaryText"), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(key, 3);
            header.Children.Add(key);
        }
        return new MenuItem { Header = header, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 1, 0, 1), Foreground = danger ? ThemeService.Brush("DangerBrush") : ThemeService.Brush("PrimaryText") };
    }
    static string? ClipboardDeckFile()
    {
        try
        {
            if (!System.Windows.Clipboard.ContainsFileDropList())
                return null;
            foreach (object? value in System.Windows.Clipboard.GetFileDropList())
            if (value is string file && File.Exists(file))
                return file;
        }
        catch (COMException) { }
        return null;
    }
    void RevealDeckFile(string input)
    {
        var mapping = MappingCollectionForInput(input).LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
        if (!DeckPanelLayout.IsAvailableFile(mapping))
            return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{mapping!.DeckFilePath}\"") { UseShellExecute = true });
        }
        catch { }
    }
    void CopyDeckFileToClipboard(Mapping mapping)
    {
        if (!DeckPanelLayout.IsAvailableFile(mapping))
        {
            ShowInlineNotice("登録ファイルが見つかりません");
            return;
        }
        try
        {
            var data = new System.Windows.DataObject();
            data.SetData(System.Windows.DataFormats.FileDrop, new[] { mapping.DeckFilePath });
            System.Windows.Clipboard.SetDataObject(data, true);
            ShowInlineNotice("ファイルをコピーしました");
        }
        catch (COMException) { ShowInlineNotice("ファイルをクリップボードへコピーできませんでした"); }
    }
    void ChooseDeckButtonColor(string input)
    {
        var existing = MappingCollectionForInput(input).LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
        var initial = DeckPanelLayout.TryGetButtonColor(existing, out var current) ? current : ThemeService.Color("AccentBrush");
        var picker = new ThemeColorPickerWindow(initial) { Owner = this };
        if (picker.ShowDialog() != true)
            return;
        var selectedColor = picker.SelectedColor;
        SetDeckButtonColor(input, $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}");
    }
    void ChooseDeckButtonIcon(string input)
    {
        var mappings = MappingCollectionForInput(input);
        var mapping = mappings.LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
        var picker = new DeckIconPickerWindow(mapping?.DeckIcon ?? "", mapping?.DeckIconPath ?? "") { Owner = this };
        if (picker.ShowDialog() != true)
            return;
        mapping ??= new Mapping { Input = input, Layer = DeckPanelLayout.Layer };
        if (!mappings.Contains(mapping))
            mappings.Add(mapping);
        mapping.DeckIcon = picker.SelectedPresetId;
        mapping.DeckIconPath = picker.SelectedCustomPath;
        if (selected?.Input.Equals(input, StringComparison.OrdinalIgnoreCase) == true)
        {
            selected.DeckIcon = mapping.DeckIcon;
            selected.DeckIconPath = mapping.DeckIconPath;
        }
        if (!HasDeckButtonContent(mapping))
            mappings.Remove(mapping);
        MarkDirty();
        RefreshSelectedInputVisual(input);
    }
    void SetDeckButtonColor(string input, string color)
    {
        var mappings = MappingCollectionForInput(input);
        var mapping = mappings.LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
        if (mapping == null)
        {
            mapping = new Mapping { Input = input, Layer = DeckPanelLayout.Layer };
            mappings.Add(mapping);
        }
        mapping.DeckColor = color;
        if (selected?.Input.Equals(input, StringComparison.OrdinalIgnoreCase) == true)
            selected.DeckColor = color;
        if (!HasDeckButtonContent(mapping))
            mappings.Remove(mapping);
        MarkDirty();
        RefreshSelectedInputVisual(input);
        UpdateDeckColorPicker();
    }
    void DeckColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (selected?.Input is not string input || !DeckPanelLayout.IsInputName(input) || sender is not System.Windows.Controls.Button { Tag: string color })
            return;
        SetDeckButtonColor(input, color);
    }
    void DeckColorReset_Click(object sender, RoutedEventArgs e)
    {
        if (selected?.Input is string input && DeckPanelLayout.IsInputName(input))
            SetDeckButtonColor(input, "");
    }
    void DeckColorCustom_Click(object sender, RoutedEventArgs e)
    {
        if (selected?.Input is string input && DeckPanelLayout.IsInputName(input))
            ChooseDeckButtonColor(input);
    }
    void UpdateDeckColorPicker()
    {
        if (DeckColorStatus == null || DeckColorSwatches == null)
            return;
        string color = selected?.DeckColor ?? "";
        DeckColorStatus.Text = string.IsNullOrWhiteSpace(color) ? "標準の配色を使用中" : $"選択中: {color}";
        foreach (var swatch in DeckColorSwatches.Children.OfType<System.Windows.Controls.Button>())
        {
            bool active = string.Equals(swatch.Tag?.ToString(), color, StringComparison.OrdinalIgnoreCase);
            swatch.BorderBrush = active ? ThemeService.Brush("PrimaryText") : ThemeService.Brush("SubtleBorderBrush");
            swatch.BorderThickness = new Thickness(active ? 2 : 1);
        }
    }

    // Deck layout editor and management workspace
    void BuildDeckManagementPanel()
    {
        CloseDeckEditorMediaPreview();
        DeckManagementGrid.Children.Clear();
        deckManagementButtons.Clear();
        deckManagementNameLabels.Clear();
        var layout = selectedDeckLayout ?? DeckPanelLayout.DefaultLayout(config);
        if (layout == null)
            return;
        DeckManagementGrid.Rows = layout.Rows;
        DeckManagementGrid.Columns = layout.Columns;
        DeckManagementGrid.Width = layout.Columns * DeckPanelLayout.CellWidth;
        DeckManagementGrid.Height = layout.Rows * DeckPanelLayout.CellHeight;
        for (int slot = 1; slot <= DeckPanelLayout.VisibleSlotCount(layout); slot++)
        {
            int capturedSlot = slot;
            var button = new System.Windows.Controls.Button
            {
                Tag = DeckPanelLayout.InputName(slot),
                Margin = new Thickness(2),
                MinWidth = 0,
                MinHeight = 0,
                Padding = new Thickness(3),
                FontSize = 11,
                AllowDrop = true,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
                Style = (Style)FindResource("DeckButtonStyle")
            };
            button.Click += (_, _) => DeckManagementButtonClicked(button, DeckPanelLayout.InputName(capturedSlot));
            button.MouseEnter += DeckManagementButton_MouseEnter;
            button.MouseDoubleClick += DeckManagementButton_DoubleClick;
            button.PreviewMouseRightButtonDown += InputButton_RightClick;
            button.PreviewMouseLeftButtonDown += DeckButtonReorderStarted;
            button.PreviewMouseMove += DeckButtonReorderMoved;
            button.PreviewMouseLeftButtonUp += DeckButtonReorderEnded;
            button.PreviewDragEnter += DeckButtonDragOver;
            button.PreviewDragOver += DeckButtonDragOver;
            button.PreviewDragLeave += DeckButtonDragLeave;
            button.PreviewDrop += DeckButtonDropped;
            button.Width = DeckPanelLayout.KeyWidth;
            button.Height = DeckPanelLayout.KeyHeight;
            button.Margin = new Thickness(DeckPanelLayout.ButtonGap / 2, 0, DeckPanelLayout.ButtonGap / 2, 0);
            button.Padding = new Thickness(3);
            var nameLabel = DeckPanelLayout.CreateNameLabel(null);
            var cell = new StackPanel { Width = DeckPanelLayout.CellWidth, Height = DeckPanelLayout.CellHeight };
            cell.Children.Add(button);
            cell.Children.Add(nameLabel);
            DeckManagementGrid.Children.Add(cell);
            deckManagementButtons.Add(button);
            deckManagementNameLabels[button] = nameLabel;
        }
        ColorDeckManagementButtons();
    }
    void DeckManagementButton_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string input } button)
            return;
        var mapping = DeckPanelLayout.FindMapping(selectedDeckLayout ?? DeckPanelLayout.DefaultLayout(config), DeckPanelLayout.SlotNumber(input));
        bool available = DeckPanelLayout.IsAvailableFile(mapping);
        bool previous = button.Resources["DeckFileAvailable"] is bool value && value;
        if (DeckPanelLayout.HasRegisteredFile(mapping) && available != previous)
            UpdateDeckManagementButtonVisual(button);
    }
    void DeckManagementButtonClicked(System.Windows.Controls.Button source, string input)
    {
        if (MultiSelectToggle.IsChecked == true)
        {
            int slot = DeckPanelLayout.SlotNumber(input);
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0 && deckMultiSelectionAnchor > 0)
            {
                foreach (string selectedInput in DeckSelectionRange(deckMultiSelectionAnchor, slot))
                    multiSelectedInputs.Add(selectedInput);
            }
            else
            {
                if (!multiSelectedInputs.Add(input))
                    multiSelectedInputs.Remove(input);
                deckMultiSelectionAnchor = slot;
            }
            UpdateMultiSelectControls();
            ColorDeckManagementButtons();
            return;
        }
        SelectInput(input);
        CloseDeckEditorMediaPreview();
        var layout = selectedDeckLayout ?? DeckPanelLayout.DefaultLayout(config);
        var mapping = DeckPanelLayout.FindMapping(layout, DeckPanelLayout.SlotNumber(input));
        if (!DeckPanelLayout.IsAvailableFile(mapping))
            return;
        if (DeckPanelLayout.IsAudioFile(mapping!.DeckFilePath))
            PlayDeckEditorAudio(mapping.DeckFilePath);
        else if (DeckPanelLayout.IsImageFile(mapping.DeckFilePath) || DeckPanelLayout.IsVideoFile(mapping.DeckFilePath))
            ShowDeckEditorThumbnail(source, mapping.DeckFilePath);
    }
    internal static IEnumerable<string> DeckSelectionRange(int anchorSlot, int targetSlot)
    {
        int first = Math.Min(anchorSlot, targetSlot);
        int last = Math.Max(anchorSlot, targetSlot);
        for (int slot = first; slot <= last; slot++)
            yield return DeckPanelLayout.InputName(slot);
    }
    void DeckManagementButton_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string input })
            return;
        e.Handled = true;
        SelectInput(input, false);
        CloseDeckEditorMediaPreview();
        OpenActionPicker(false);
    }
    void PlayDeckEditorAudio(string path)
    {
        try
        {
            var player = new MediaPlayer();
            deckEditorAudioPlayer = player;
            player.MediaEnded += (_, _) => { if (ReferenceEquals(deckEditorAudioPlayer, player)) StopDeckEditorAudio(); };
            player.MediaFailed += (_, _) => { if (ReferenceEquals(deckEditorAudioPlayer, player)) StopDeckEditorAudio(); };
            player.Open(new Uri(path, UriKind.Absolute));
            player.Volume = .8;
            PreviewMouseMove += DeckEditorAudioMouseMoved;
            player.Play();
        }
        catch { StopDeckEditorAudio(); }
    }
    void DeckEditorAudioMouseMoved(object sender, System.Windows.Input.MouseEventArgs e) => StopDeckEditorAudio();
    void StopDeckEditorAudio()
    {
        PreviewMouseMove -= DeckEditorAudioMouseMoved;
        var player = deckEditorAudioPlayer;
        deckEditorAudioPlayer = null;
        if (player == null)
            return;
        try
        {
            player.Stop();
            player.Close();
        }
        catch { }
    }
    void ShowDeckEditorThumbnail(System.Windows.Controls.Button source, string path)
    {
        var thumbnail = DeckPanelLayout.LoadFileThumbnail(path, 420);
        if (thumbnail == null)
            return;
        var image = new System.Windows.Controls.Image
        {
            Source = thumbnail,
            MaxWidth = 360,
            MaxHeight = 260,
            Stretch = Stretch.Uniform,
            IsHitTestVisible = false
        };
        var card = new Border
        {
            Child = image,
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Background = ThemeService.Brush("CardBackground"),
            BorderBrush = ThemeService.Brush("AccentBrush")
        };
        var popup = new System.Windows.Controls.Primitives.Popup
        {
            PlacementTarget = source,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
            VerticalOffset = 6,
            AllowsTransparency = true,
            StaysOpen = false,
            Child = card
        };
        deckEditorThumbnailPopup = popup;
        popup.Closed += (_, _) =>
        {
            if (ReferenceEquals(deckEditorThumbnailPopup, popup))
                deckEditorThumbnailPopup = null;
        };
        popup.IsOpen = true;
    }
    void CloseDeckEditorMediaPreview()
    {
        StopDeckEditorAudio();
        var popup = deckEditorThumbnailPopup;
        deckEditorThumbnailPopup = null;
        if (popup != null)
            popup.IsOpen = false;
    }
    void DeckButtonReorderStarted(object sender, MouseButtonEventArgs e)
    {
        if (MultiSelectToggle.IsChecked == true)
            return;
        if (sender is not System.Windows.Controls.Button { Tag: string input } || !DeckPanelLayout.IsInputName(input))
            return;
        deckReorderSource = (System.Windows.Controls.Button)sender;
        deckReorderStart = e.GetPosition(deckReorderSource);
    }
    void DeckButtonReorderMoved(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || !ReferenceEquals(button, deckReorderSource) || e.LeftButton != MouseButtonState.Pressed || button.Tag is not string input)
            return;
        System.Windows.Point point = e.GetPosition(button);
        if (Math.Abs(point.X - deckReorderStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - deckReorderStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        deckReorderSource = null;
        var data = new System.Windows.DataObject();
        data.SetData(DeckPanelLayout.SlotDragFormat, input);
        var mapping = DeckPanelLayout.FindMapping(selectedDeckLayout ?? DeckPanelLayout.DefaultLayout(config), DeckPanelLayout.SlotNumber(input));
        if (DeckPanelLayout.IsAvailableFile(mapping))
            data.SetData(System.Windows.DataFormats.FileDrop, new[] { mapping!.DeckFilePath });
        try
        {
            RunDeckEditorDrag(button, mapping, data);
        }
        finally { ClearDeckReorderTarget(); }
        e.Handled = true;
    }
    static void RunDeckEditorDrag(System.Windows.Controls.Button button, Mapping? mapping, System.Windows.DataObject data)
    {
        DeckDragPreviewWindow? preview = null;
        System.Windows.GiveFeedbackEventHandler? feedback = null;
        try
        {
            var icon = DeckIconCatalog.CreateVisual(mapping, 34, false);
            if (icon != null)
            {
                preview = new DeckDragPreviewWindow(icon);
                feedback = (_, e) =>
                {
                    var cursor = System.Windows.Forms.Cursor.Position;
                    preview.MoveToPhysical(cursor.X, cursor.Y);
                    e.UseDefaultCursors = false;
                    e.Handled = true;
                };
                button.GiveFeedback += feedback;
                preview.Show();
                var cursor = System.Windows.Forms.Cursor.Position;
                preview.MoveToPhysical(cursor.X, cursor.Y);
            }
            // Copy-only also protects a registered source file when this drag
            // leaves the Deck editor and is dropped into Explorer/Desktop.
            DragDrop.DoDragDrop(button, data, DeckPanelLayout.ExternalFileDragEffects);
        }
        finally
        {
            if (feedback != null) button.GiveFeedback -= feedback;
            preview?.Close();
        }
    }
    void DeckButtonReorderEnded(object sender, MouseButtonEventArgs e)
    {
        deckReorderSource = null;
        ClearDeckReorderTarget();
    }
    void DeckButtonDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string input } || !DeckPanelLayout.IsInputName(input))
            return;
        bool slot = e.Data.GetDataPresent(DeckPanelLayout.SlotDragFormat);
        bool file = !DeckPanelLayout.IsInternalFileDrag(e.Data) && DeckPanelLayout.GetDroppedFile(e.Data) != null;
        bool validSlot = slot && e.Data.GetData(DeckPanelLayout.SlotDragFormat) is string source && !source.Equals(input, StringComparison.OrdinalIgnoreCase);
        SetDeckReorderTarget(validSlot ? (System.Windows.Controls.Button)sender : null);
        e.Effects = validSlot || file ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }
    void DeckButtonDragLeave(object sender, System.Windows.DragEventArgs e)
    {
        if (ReferenceEquals(sender, deckReorderTarget))
            ClearDeckReorderTarget();
    }
    void SetDeckReorderTarget(System.Windows.Controls.Button? target)
    {
        if (ReferenceEquals(target, deckReorderTarget))
            return;
        ClearDeckReorderTarget();
        if (target == null)
            return;
        target.BorderBrush = ThemeService.Brush("AccentBrush");
        target.BorderThickness = new Thickness(2);
        deckReorderTarget = target;
    }
    void ClearDeckReorderTarget()
    {
        if (deckReorderTarget == null)
            return;
        var target = deckReorderTarget;
        deckReorderTarget = null;
        UpdateDeckManagementButtonVisual(target);
    }
    void DeckButtonDropped(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string target } || !DeckPanelLayout.IsInputName(target))
            return;
        ClearDeckReorderTarget();
        if (e.Data.GetDataPresent(DeckPanelLayout.SlotDragFormat) && e.Data.GetData(DeckPanelLayout.SlotDragFormat) is string source && DeckPanelLayout.IsInputName(source))
        {
            var layout = selectedDeckLayout ?? DeckPanelLayout.DefaultLayout(config);
            if (layout != null && !source.Equals(target, StringComparison.OrdinalIgnoreCase))
            {
                DeckPanelLayout.SwapSlots(layout, DeckPanelLayout.SlotNumber(source), DeckPanelLayout.SlotNumber(target));
                BuildDeckManagementPanel();
                SelectInput(target, false);
                MarkDirty();
            }
            e.Handled = true;
            return;
        }
        if (DeckPanelLayout.IsInternalFileDrag(e.Data))
        {
            e.Handled = true;
            return;
        }
        if (DeckPanelLayout.GetDroppedFile(e.Data) is string file)
        {
            SetDeckButtonFile(target, file);
            SelectInput(target, false);
        }
        e.Handled = true;
    }
    void OpenDeckPanelManager_Click(object sender, RoutedEventArgs e)
    {
        deckManagementMode = true;
        currentLayer = DeckPanelLayout.Layer;
        KeyboardWorkspace.Visibility = Visibility.Collapsed;
        DeckWorkspace.Visibility = Visibility.Visible;
        DetectInputButton.Visibility = Visibility.Collapsed;
        LongPressExpander.Visibility = Visibility.Collapsed;
        LongPressOnlyButton.Visibility = Visibility.Collapsed;
        KindBox.ItemsSource = DeckActionOptions();
        KindBox.SelectedValuePath = nameof(ActionOption.SelectionKind);
        ShowDeckLayoutList();
        UpdateDeckScopeUi();
        ClearSelectedInput();
        UpdateLayerButtons();
        ColorButtons();
    }
    void ShowKeyboardWorkspace()
    {
        if (!deckManagementMode)
            return;
        CloseDeckEditorMediaPreview();
        deckManagementMode = false;
        selectedDeckLayout = null;
        KeyboardWorkspace.Visibility = Visibility.Visible;
        DeckWorkspace.Visibility = Visibility.Collapsed;
        DeckLayoutListWorkspace.Visibility = Visibility.Visible;
        DeckEditorWorkspace.Visibility = Visibility.Collapsed;
        ToolbarSaveButton.Visibility = Visibility.Visible;
        DetectInputButton.Visibility = Visibility.Visible;
        LongPressExpander.Visibility = Visibility.Visible;
        LongPressOnlyButton.Visibility = Visibility.Visible;
        KindBox.ItemsSource = ActionOptions(allowGesture: true);
        KindBox.SelectedValuePath = nameof(ActionOption.SelectionKind);
        DeckNameEditorPanel.Visibility = Visibility.Collapsed;
        UpdateDeckScopeUi();
    }
    void UpdateDeckScopeUi()
    {
        if (ProfileBox == null)
            return;
        bool enabled = deckManagementMode && selectedDeckLayout?.ProfileSwitchEnabled == true;
        ProfileBox.IsEnabled = !deckManagementMode || enabled;
        ProfileBox.Opacity = ProfileBox.IsEnabled ? 1 : .45;
    }
    void ShowDeckLayoutList()
    {
        CloseDeckEditorMediaPreview();
        selectedDeckLayout = null;
        DeckLayoutListWorkspace.Visibility = Visibility.Visible;
        DeckEditorWorkspace.Visibility = Visibility.Collapsed;
        ToolbarSaveButton.Visibility = Visibility.Visible;
        WorkspaceSubtitle.Text = $"{DeckPanelLayout.LayoutsForActiveProfile(config).Count()}個のレイアウト";
        RefreshDeckLayoutCards();
        UpdateDeckScopeUi();
    }
    void RefreshDeckLayoutCards()
    {
        if (DeckLayoutCardsPanel == null)
            return;
        DeckLayoutCardsPanel.Children.Clear();
        foreach (var layout in DeckPanelLayout.LayoutsForActiveProfile(config))
            DeckLayoutCardsPanel.Children.Add(CreateDeckLayoutCard(layout));
        var add = new System.Windows.Controls.Button
        {
            Width = 236,
            Height = 190,
            Margin = new Thickness(0, 0, 14, 14),
            Padding = new Thickness(16),
            Content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = "＋", FontSize = 28, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Foreground = ThemeService.Brush("SecondaryText") }, new TextBlock { Text = "新規レイアウト", FontSize = 14, Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Center } } },
            Background = WpfBrushes.Transparent,
            BorderBrush = ThemeService.Brush("BorderBrush"),
            BorderThickness = new Thickness(1)
        };
        add.Click += NewDeckLayout_Click;
        DeckLayoutCardsPanel.Children.Add(add);
    }
    System.Windows.Controls.Button CreateDeckLayoutCard(DeckLayoutDefinition layout)
    {
        var previewSize = DeckPreviewSize(layout.Columns, layout.Rows);
        var preview = new System.Windows.Controls.Primitives.UniformGrid { Rows = layout.Rows, Columns = layout.Columns, Width = previewSize.Width, Height = previewSize.Height, HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        for (int index = 0; index < DeckPanelLayout.VisibleSlotCount(layout); index++)
        {
            var cell = new Border { Margin = new Thickness(1), CornerRadius = new CornerRadius(2) };
            cell.SetResourceReference(Border.BackgroundProperty, "DeckPreviewCellBackground");
            preview.Children.Add(cell);
        }
        bool isDefault = CurrentProfile.DefaultDeckLayoutId.Equals(layout.Id, StringComparison.OrdinalIgnoreCase);
        var content = new StackPanel();
        content.Children.Add(new Grid { Height = 88, Margin = new Thickness(0, 0, 0, 12), Children = { preview } });
        content.Children.Add(new TextBlock { Tag = "DeckLayoutName", Text = layout.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        content.Children.Add(new TextBlock { Text = $"{layout.Columns}×{layout.Rows}・{DeckPanelLayout.VisibleSlotCount(layout)}ボタン" + (isDefault ? "  ・  既定" : ""), FontSize = 11, Margin = new Thickness(0, 5, 0, 0), Foreground = ThemeService.Brush(isDefault ? "AccentTextBrush" : "SecondaryText") });
        var card = new System.Windows.Controls.Button { Tag = layout, Content = content, Width = 236, Height = 190, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(16), HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, BorderThickness = new Thickness(1) };
        card.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "CardBackground");
        card.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, isDefault ? "AccentBrush" : "BorderBrush");
        card.Click += (_, _) => EditDeckLayout(layout);
        var menu = new ContextMenu();
        var makeDefault = new MenuItem { Header = "既定のDeckにする", IsEnabled = !isDefault };
        makeDefault.Click += (_, _) => SetDefaultDeckLayout(layout);
        var duplicate = new MenuItem { Header = "複製" };
        duplicate.Click += (_, _) => DuplicateDeckLayout(layout);
        var delete = new MenuItem { Header = "削除" };
        delete.Click += (_, _) => DeleteDeckLayout(layout);
        menu.Items.Add(makeDefault);
        menu.Items.Add(new Separator());
        menu.Items.Add(duplicate);
        menu.Items.Add(delete);
        card.ContextMenu = menu;
        return card;
    }
    internal static (double Width, double Height) DeckPreviewSize(int columns, int rows)
    {
        const double previewMaxWidth = 190, previewMaxHeight = 88;
        double cellSize = Math.Min(previewMaxWidth / Math.Max(1, columns), previewMaxHeight / Math.Max(1, rows));
        return (Math.Max(1, columns) * cellSize, Math.Max(1, rows) * cellSize);
    }
    void NewDeckLayout_Click(object sender, RoutedEventArgs e)
    {
        if (PromptNewDeckLayout() is not { } choice)
            return;
        var layout = new DeckLayoutDefinition { Name = choice.Name, Columns = choice.Columns, Rows = choice.Rows };
        config.DeckLayouts.Add(layout);
        SetDefaultDeckLayout(layout, false);
        MarkDirty();
        EditDeckLayout(layout);
    }
    (string Name, int Columns, int Rows)? PromptNewDeckLayout()
    {
        var dialog = new Window { Title = "新規Deckレイアウト", Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Width = 460, Height = 348, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = ThemeService.Brush("SurfaceBackground"), Foreground = ThemeService.Brush("PrimaryText") };
        var root = new Grid { Margin = new Thickness(24, 20, 24, 20) };
        foreach (var height in new[] { GridLength.Auto, GridLength.Auto, GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto })
            root.RowDefinitions.Add(new RowDefinition { Height = height });
        root.Children.Add(new TextBlock { Text = "レイアウト名", FontSize = 12, Foreground = ThemeService.Brush("SecondaryText") });
        var name = new TextBox { Style = (Style)FindResource(typeof(TextBox)), Text = "新しいDeck", Height = 40, Margin = new Thickness(0, 6, 0, 18), VerticalContentAlignment = VerticalAlignment.Center };
        Grid.SetRow(name, 1);
        root.Children.Add(name);

        var sizeRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sizeRow.Children.Add(new TextBlock { Text = "サイズ", VerticalAlignment = VerticalAlignment.Center, Foreground = ThemeService.Brush("SecondaryText") });
        var sizes = new System.Windows.Controls.ComboBox { Name = "NewDeckSizeBox", Style = (Style)FindResource("ToolbarComboBoxStyle"), Width = 220, Height = 40, Margin = new Thickness(0), SelectedIndex = 1 };
        sizes.Items.Add(new ComboBoxItem { Content = "コンパクト  3×3", Tag = "3x3" });
        sizes.Items.Add(new ComboBoxItem { Content = "標準  9×5", Tag = "9x5" });
        sizes.Items.Add(new ComboBoxItem { Content = "ワイド  8×2", Tag = "8x2" });
        sizes.Items.Add(new ComboBoxItem { Content = "カスタム", Tag = "custom" });
        Grid.SetColumn(sizes, 1);
        sizeRow.Children.Add(sizes);
        Grid.SetRow(sizeRow, 2);
        root.Children.Add(sizeRow);

        var customRow = new Grid { Name = "NewDeckCustomSizeRow", Visibility = Visibility.Collapsed, Margin = new Thickness(70, 0, 0, 0) };
        customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        customRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var columnsBox = new TextBox { Style = (Style)FindResource(typeof(TextBox)), Text = "9", Width = 64, Height = 40, MaxLength = 2, FontSize = 14, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        var times = new TextBlock { Text = "×", Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = ThemeService.Brush("SecondaryText") };
        Grid.SetColumn(times, 1);
        var rowsBox = new TextBox { Style = (Style)FindResource(typeof(TextBox)), Text = "5", Width = 64, Height = 40, MaxLength = 2, FontSize = 14, Padding = new Thickness(8, 0, 8, 0), Margin = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        Grid.SetColumn(rowsBox, 2);
        var limit = new TextBlock { Text = "1～18", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = ThemeService.Brush("MutedText"), FontSize = 11 };
        Grid.SetColumn(limit, 3);
        customRow.Children.Add(columnsBox);
        customRow.Children.Add(times);
        customRow.Children.Add(rowsBox);
        customRow.Children.Add(limit);
        Grid.SetRow(customRow, 3);
        root.Children.Add(customRow);

        var validation = new TextBlock { Foreground = ThemeService.Brush("WarningBrush"), FontSize = 11, VerticalAlignment = VerticalAlignment.Bottom };
        Grid.SetRow(validation, 4);
        root.Children.Add(validation);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new System.Windows.Controls.Button { Content = "キャンセル", IsCancel = true, Height = 40, MinWidth = 92, Margin = new Thickness(0, 0, 8, 0) };
        var create = new System.Windows.Controls.Button { Content = "作成", IsDefault = true, Height = 40, MinWidth = 84, Background = ThemeService.Brush("AccentStrongBrush"), Foreground = ThemeService.Brush("AccentButtonText"), BorderBrush = ThemeService.Brush("AccentBrush") };
        if (System.Windows.Application.Current?.Resources["AppButtonStyle"] is Style buttonStyle)
        {
            cancel.Style = buttonStyle;
            create.Style = buttonStyle;
        }
        void UpdateCustomVisibility(object? _, SelectionChangedEventArgs __)
        {
            customRow.Visibility = (sizes.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "custom" ? Visibility.Visible : Visibility.Collapsed;
            validation.Text = "";
        }
        sizes.SelectionChanged += UpdateCustomVisibility;
        cancel.Click += (_, _) => dialog.DialogResult = false;
        create.Click += (_, _) =>
        {
            string tag = (sizes.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "9x5";
            if (string.IsNullOrWhiteSpace(name.Text))
            {
                validation.Text = "レイアウト名を入力してください。";
                name.Focus();
                return;
            }
            if (!TryResolveDeckLayoutSize(tag, columnsBox.Text, rowsBox.Text, out _, out _))
            {
                validation.Text = "列数と行数は1～18で入力してください。";
                columnsBox.Focus();
                return;
            }
            dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(create);
        Grid.SetRow(buttons, 5);
        root.Children.Add(buttons);
        dialog.Content = root;
        FollowWindowsTitleBarTheme(dialog);
        if (NewDeckDialogLoadedForTest != null)
            dialog.Loaded += (_, _) => NewDeckDialogLoadedForTest(dialog);
        if (dialog.ShowDialog() != true)
            return null;
        string selectedTag = (sizes.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "9x5";
        return TryResolveDeckLayoutSize(selectedTag, columnsBox.Text, rowsBox.Text, out int columns, out int rows) ? (name.Text.Trim(), columns, rows) : null;
    }

    internal static bool TryResolveDeckLayoutSize(string preset, string columnsText, string rowsText, out int columns, out int rows)
    {
        (columns, rows) = preset switch
        {
            "3x3" => (3, 3),
            "9x5" => (9, 5),
            "8x2" => (8, 2),
            _ => (0, 0)
        };
        if (!preset.Equals("custom", StringComparison.OrdinalIgnoreCase))
            return columns > 0 && rows > 0;
        return TryParseDeckDimension(columnsText, out columns) && TryParseDeckDimension(rowsText, out rows) && columns is >= 1 and <= DeckPanelLayout.MaximumColumns && rows is >= 1 and <= DeckPanelLayout.MaximumRows;
    }

    internal static bool TryParseDeckDimension(string text, out int value)
    {
        string trimmed = text.Trim();
        if (trimmed.Length is 0 or > 2)
        {
            value = 0;
            return false;
        }
        Span<char> normalized = stackalloc char[trimmed.Length];
        int length = 0;
        foreach (char character in trimmed)
            normalized[length++] = character is >= '０' and <= '９' ? (char)('0' + character - '０') : character;
        return int.TryParse(normalized[..length], out value);
    }
    void EditDeckLayout(DeckLayoutDefinition layout)
    {
        selectedDeckLayout = layout;
        DeckLayoutListWorkspace.Visibility = Visibility.Collapsed;
        DeckEditorWorkspace.Visibility = Visibility.Visible;
        ToolbarSaveButton.Visibility = Visibility.Collapsed;
        updatingDeckEditor = true;
        DeckLayoutNameBox.Text = layout.Name;
        DeckProfileSwitchBox.IsChecked = layout.ProfileSwitchEnabled;
        DeckColumnsBox.Text = layout.Columns.ToString();
        DeckRowsBox.Text = layout.Rows.ToString();
        DeckOpacitySlider.Value = config.InputPanelOpacityPercent;
        DeckOpacityValueText.Text = config.InputPanelOpacityPercent + "%";
        DeckHoverPreviewBox.IsChecked = config.DeckHoverPreviewsEnabled;
        DeckSizePresetBox.Style = (Style)FindResource("ToolbarComboBoxStyle");
        DeckSizePresetBox.Height = 40;
        UpdateDeckPanelColorEditor();
        string preset = layout.Columns == 3 && layout.Rows == 3 ? "3x3" : layout.Columns == 9 && layout.Rows == 5 ? "9x5" : layout.Columns == 8 && layout.Rows == 2 ? "8x2" : "custom";
        DeckSizePresetBox.SelectedItem = DeckSizePresetBox.Items.Cast<ComboBoxItem>().First(x => Equals(x.Tag, preset));
        DeckCustomSizePanel.Visibility = preset == "custom" ? Visibility.Visible : Visibility.Collapsed;
        updatingDeckEditor = false;
        UpdateDeckScopeUi();
        BuildDeckManagementPanel();
        ClearSelectedInput();
        WorkspaceSubtitle.Text = $"{layout.Columns}×{layout.Rows}・{DeckPanelLayout.VisibleSlotCount(layout)}ボタン";
        ColorButtons();
    }
    void DeckBack_Click(object sender, RoutedEventArgs e)
    {
        ClearSelectedInput();
        ShowDeckLayoutList();
        UpdateLayerButtons();
    }
    void DeckSave_Click(object sender, RoutedEventArgs e)
    {
        SaveAndApply("Deckレイアウトを保存し、反映しました");
        RefreshDeckLayoutCards();
    }
    void DeckLayoutNameChanged(object sender, TextChangedEventArgs e)
    {
        if (updatingDeckEditor || selectedDeckLayout == null)
            return;
        string name = DeckLayoutNameBox.Text.Trim();
        if (name.Length == 0)
            return;
        selectedDeckLayout.Name = name;
        MarkDirty(refreshDeckPanel: false);
    }
    void DeckProfileSwitchChanged(object sender, RoutedEventArgs e)
    {
        if (updatingDeckEditor || selectedDeckLayout == null)
            return;
        if (DeckProfileSwitchBox.IsChecked == true)
            EnableDeckProfileSwitch(selectedDeckLayout);
        else if (!DisableDeckProfileSwitch(selectedDeckLayout))
        {
            updatingDeckEditor = true;
            DeckProfileSwitchBox.IsChecked = true;
            updatingDeckEditor = false;
        }
        UpdateDeckScopeUi();
    }
    void EnableDeckProfileSwitch(DeckLayoutDefinition layout)
    {
        var active = DeckPanelLayout.ActiveProfile(config) ?? config.Profiles[0];
        string groupId = Guid.NewGuid().ToString("N");
        layout.ProfileSwitchEnabled = true;
        layout.ProfileGroupId = groupId;
        layout.ProfileId = active.Id;
        bool wasDefault = config.DefaultDeckLayoutId.Equals(layout.Id, StringComparison.OrdinalIgnoreCase);
        foreach (var profile in config.Profiles)
        {
            var variant = ReferenceEquals(profile, active) ? layout : CreateDeckProfileVariant(layout, profile, blank: true);
            if (!ReferenceEquals(variant, layout))
                config.DeckLayouts.Add(variant);
            if (wasDefault || profile.DefaultDeckLayoutId.Equals(layout.Id, StringComparison.OrdinalIgnoreCase))
                profile.DefaultDeckLayoutId = variant.Id;
        }
        MarkDirty();
        ShowInlineNotice("各プロファイルに同じレイアウトの空のDeckを作成しました");
    }
    bool DisableDeckProfileSwitch(DeckLayoutDefinition layout)
    {
        var group = config.DeckLayouts.Where(candidate => candidate.ProfileSwitchEnabled
            && candidate.ProfileGroupId.Equals(layout.ProfileGroupId, StringComparison.OrdinalIgnoreCase)).ToList();
        var standard = group.FirstOrDefault(candidate => candidate.ProfileId.Equals(config.Profiles[0].Id, StringComparison.OrdinalIgnoreCase)) ?? layout;
        if (group.Count > 1 && WpfMessageBox.Show("プロファイル切替を無効にすると、標準プロファイルのDeckが全プロファイル共通のDeckとして設定されます。\n\n続行しますか？", "Deckのプロファイル切替", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return false;
        foreach (var variant in group.Where(candidate => !ReferenceEquals(candidate, standard)).ToList())
        {
            RemoveDeckLayoutReferences(config, variant, standard);
            config.DeckLayouts.Remove(variant);
        }
        standard.ProfileSwitchEnabled = false;
        standard.ProfileGroupId = "";
        standard.ProfileId = "";
        config.DefaultDeckLayoutId = standard.Id;
        config.SharedDefaultDeckLayoutId = standard.Id;
        foreach (var profile in config.Profiles)
            profile.DefaultDeckLayoutId = standard.Id;
        selectedDeckLayout = standard;
        if (!ReferenceEquals(layout, standard))
            EditDeckLayout(standard);
        MarkDirty();
        ShowInlineNotice("標準プロファイルのDeckを全プロファイル共通に設定しました");
        return true;
    }
    static DeckLayoutDefinition CreateDeckProfileVariant(DeckLayoutDefinition source, Profile profile, bool blank) => new()
    {
        Name = source.Name,
        Columns = source.Columns,
        Rows = source.Rows,
        PanelColor = source.PanelColor,
        PanelPinned = source.PanelPinned,
        PanelWidth = source.PanelWidth,
        PanelHeight = source.PanelHeight,
        ProfileSwitchEnabled = true,
        ProfileGroupId = source.ProfileGroupId,
        ProfileId = profile.Id,
        Mappings = blank ? [] : [.. source.Mappings.Select(CloneMapping)]
    };
    void SyncDeckProfileVariants()
    {
        var profileIds = config.Profiles.Select(profile => profile.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var orphan in config.DeckLayouts.Where(layout => layout.ProfileSwitchEnabled && !profileIds.Contains(layout.ProfileId)).ToList())
        {
            var fallback = config.DeckLayouts.FirstOrDefault(layout => !ReferenceEquals(layout, orphan)
                && layout.ProfileSwitchEnabled
                && layout.ProfileGroupId.Equals(orphan.ProfileGroupId, StringComparison.OrdinalIgnoreCase)
                && profileIds.Contains(layout.ProfileId))
                ?? config.DeckLayouts.FirstOrDefault(layout => !ReferenceEquals(layout, orphan) && !layout.ProfileSwitchEnabled);
            if (fallback != null)
                RemoveDeckLayoutReferences(config, orphan, fallback);
            config.DeckLayouts.Remove(orphan);
        }
        foreach (var group in config.DeckLayouts.Where(layout => layout.ProfileSwitchEnabled).GroupBy(layout => layout.ProfileGroupId, StringComparer.OrdinalIgnoreCase).ToList())
        {
            var template = group.First();
            foreach (var profile in config.Profiles.Where(profile => !group.Any(layout => layout.ProfileId.Equals(profile.Id, StringComparison.OrdinalIgnoreCase))))
                config.DeckLayouts.Add(CreateDeckProfileVariant(template, profile, blank: true));
        }
    }
    void DeckSizePresetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingDeckEditor || selectedDeckLayout == null || DeckSizePresetBox.SelectedItem is not ComboBoxItem item)
            return;
        string tag = item.Tag?.ToString() ?? "custom";
        DeckCustomSizePanel.Visibility = tag == "custom" ? Visibility.Visible : Visibility.Collapsed;
        if (tag == "custom")
            return;
        var size = tag switch
        {
            "3x3" => (3, 3),
            "8x2" => (8, 2),
            _ => (9, 5)
        };
        ApplyDeckSize(size.Item1, size.Item2);
    }
    void DeckCustomSizeKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        CommitCustomDeckSize();
        e.Handled = true;
    }
    void DeckCustomSizeLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Moving between the two fields is still one edit.  Do not rebuild the
        // grid until the user leaves the size controls entirely.
        if (ReferenceEquals(e.NewFocus, DeckColumnsBox) || ReferenceEquals(e.NewFocus, DeckRowsBox))
            return;
        CommitCustomDeckSize();
    }
    void CommitCustomDeckSize()
    {
        if (updatingDeckEditor || selectedDeckLayout == null || DeckCustomSizePanel.Visibility != Visibility.Visible)
            return;
        if (!TryResolveDeckLayoutSize("custom", DeckColumnsBox.Text, DeckRowsBox.Text, out int columns, out int rows))
        {
            updatingDeckEditor = true;
            DeckColumnsBox.Text = selectedDeckLayout.Columns.ToString();
            DeckRowsBox.Text = selectedDeckLayout.Rows.ToString();
            updatingDeckEditor = false;
            ShowInlineNotice("列数と行数は1～18で入力してください");
            return;
        }
        if (selectedDeckLayout.Columns == columns && selectedDeckLayout.Rows == rows)
            return;
        ApplyDeckSize(columns, rows, false);
    }
    void ApplyDeckSize(int columns, int rows, bool updateBoxes = true)
    {
        if (selectedDeckLayout == null)
            return;
        selectedDeckLayout.Columns = columns;
        selectedDeckLayout.Rows = rows;
        if (updateBoxes)
        {
            updatingDeckEditor = true;
            DeckColumnsBox.Text = columns.ToString();
            DeckRowsBox.Text = rows.ToString();
            updatingDeckEditor = false;
        }
        BuildDeckManagementPanel();
        ClearSelectedInput();
        WorkspaceSubtitle.Text = $"{columns}×{rows}・{columns * rows}ボタン";
        MarkDirty();
    }
    void DeckOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DeckOpacityValueText == null)
            return;
        int value = (int)Math.Round(e.NewValue);
        DeckOpacityValueText.Text = value + "%";
        if (updatingDeckEditor || config == null)
            return;
        config.InputPanelOpacityPercent = value;
        MarkDirty();
    }
    void DeckPanelColorChoose_Click(object sender, RoutedEventArgs e)
    {
        if (selectedDeckLayout == null)
            return;
        var initial = DeckPanelLayout.TryParseButtonColor(selectedDeckLayout.PanelColor, out var current) ? current : ThemeService.Color("CardBackground");
        var picker = new ThemeColorPickerWindow(initial) { Owner = this };
        if (picker.ShowDialog() != true)
            return;
        var selectedColor = picker.SelectedColor;
        selectedDeckLayout.PanelColor = $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}";
        UpdateDeckPanelColorEditor();
        MarkDirty();
    }
    void DeckPanelColorReset_Click(object sender, RoutedEventArgs e)
    {
        if (selectedDeckLayout == null)
            return;
        selectedDeckLayout.PanelColor = "";
        UpdateDeckPanelColorEditor();
        MarkDirty();
    }
    void UpdateDeckPanelColorEditor()
    {
        if (DeckPanelColorPreview == null)
            return;
        if (selectedDeckLayout != null && DeckPanelLayout.TryParseButtonColor(selectedDeckLayout.PanelColor, out var color))
            DeckPanelColorPreview.Background = new SolidColorBrush(color);
        else
        {
            DeckPanelColorPreview.ClearValue(Border.BackgroundProperty);
            DeckPanelColorPreview.SetResourceReference(Border.BackgroundProperty, "AppBackground");
        }
    }
    void SetDefaultDeckLayout(DeckLayoutDefinition layout, bool refresh = true)
    {
        CurrentProfile.DefaultDeckLayoutId = layout.Id;
        if (!layout.ProfileSwitchEnabled)
            config.DefaultDeckLayoutId = layout.Id;
        MarkDirty();
        if (refresh)
            RefreshDeckLayoutCards();
    }
    void DuplicateDeckLayout(DeckLayoutDefinition source)
    {
        string name = source.Name + " のコピー";
        int suffix = 2;
        while (config.DeckLayouts.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            name = source.Name + $" のコピー {suffix++}";
        var copy = new DeckLayoutDefinition { Name = name, Columns = source.Columns, Rows = source.Rows, PanelColor = source.PanelColor, PanelWidth = source.PanelWidth, PanelHeight = source.PanelHeight, Mappings = [.. source.Mappings.Select(CloneMapping)] };
        config.DeckLayouts.Add(copy);
        MarkDirty();
        RefreshDeckLayoutCards();
    }
    void DeleteDeckLayout(DeckLayoutDefinition layout)
    {
        if (config.DeckLayouts.Count <= 1)
        {
            ShowInlineNotice("最後のDeckレイアウトは削除できません");
            return;
        }
        var references = CountDeckLayoutReferences(config, layout);
        var fallback = config.DeckLayouts.First(candidate => !ReferenceEquals(candidate, layout));
        if (references.Total > 0)
        {
            var lines = new List<string> { $"「{layout.Name}」は現在、次の場所から参照されています。", "" };
            if (references.DefaultSettings > 0)
                lines.Add($"・既定Deck設定：{references.DefaultSettings}件（「{fallback.Name}」へ変更）");
            if (references.Mappings > 0)
                lines.Add($"・キー／Deckの割り当て：{references.Mappings}件");
            if (references.MacroSteps > 0)
                lines.Add($"・マクロ内のアクション：{references.MacroSteps}件");
            if (references.GestureActions > 0)
                lines.Add($"・ジェスチャーのアクション：{references.GestureActions}件");
            lines.Add("");
            lines.Add("これらの参照を解除して、このDeckレイアウトを削除しますか？");
            if (WpfMessageBox.Show(this, string.Join(Environment.NewLine, lines), "参照を解除してDeckレイアウトを削除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            RemoveDeckLayoutReferences(config, layout, fallback);
        }
        else if (WpfMessageBox.Show(this, $"「{layout.Name}」を削除しますか？\n割り当てたボタンも削除されます。", "Deckレイアウトの削除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        config.DeckLayouts.Remove(layout);
        if (ReferenceEquals(selectedDeckLayout, layout))
            selectedDeckLayout = fallback;
        MarkDirty();
        RefreshDeckLayoutCards();
        ShowInlineNotice($"「{layout.Name}」と参照していた割り当てを削除しました");
    }

    internal sealed record DeckLayoutReferenceSummary(int DefaultSettings, int Mappings, int MacroSteps, int GestureActions)
    {
        internal int Total => DefaultSettings + Mappings + MacroSteps + GestureActions;
    }

    internal static DeckLayoutReferenceSummary CountDeckLayoutReferences(AppConfig source, DeckLayoutDefinition layout)
    {
        int defaults = (source.DefaultDeckLayoutId.Equals(layout.Id, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            + (source.SharedDefaultDeckLayoutId.Equals(layout.Id, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            + source.Profiles.Count(profile => profile.DefaultDeckLayoutId.Equals(layout.Id, StringComparison.OrdinalIgnoreCase));
        string action = DeckPanelLayout.ActionValue(layout.Id);
        IEnumerable<Mapping> mappings = source.Profiles.SelectMany(profile => profile.Mappings).Concat(source.SharedDeckMappings).Concat(source.DeckLayouts.SelectMany(deck => deck.Mappings));
        int mappingCount = mappings.Sum(mapping => (mapping.Value.Equals(action, StringComparison.OrdinalIgnoreCase) ? 1 : 0) + (mapping.LongPressValue.Equals(action, StringComparison.OrdinalIgnoreCase) ? 1 : 0));
        int macroCount = source.Macros.SelectMany(macro => macro.Steps).Count(step => step.RecordedActionValue.Equals(action, StringComparison.OrdinalIgnoreCase));
        int gestureCount = source.Gestures.Sum(gesture => new[] { gesture.UpValue, gesture.DownValue, gesture.LeftValue, gesture.RightValue, gesture.CenterValue }.Count(value => value.Equals(action, StringComparison.OrdinalIgnoreCase)));
        return new DeckLayoutReferenceSummary(defaults, mappingCount, macroCount, gestureCount);
    }

    internal static DeckLayoutReferenceSummary RemoveDeckLayoutReferences(AppConfig source, DeckLayoutDefinition layout, DeckLayoutDefinition fallback)
    {
        var removed = CountDeckLayoutReferences(source, layout);
        if (source.DefaultDeckLayoutId.Equals(layout.Id, StringComparison.OrdinalIgnoreCase))
            source.DefaultDeckLayoutId = fallback.Id;
        if (source.SharedDefaultDeckLayoutId.Equals(layout.Id, StringComparison.OrdinalIgnoreCase))
            source.SharedDefaultDeckLayoutId = fallback.Id;
        foreach (var profile in source.Profiles.Where(profile => profile.DefaultDeckLayoutId.Equals(layout.Id, StringComparison.OrdinalIgnoreCase)))
            profile.DefaultDeckLayoutId = fallback.Id;

        string action = DeckPanelLayout.ActionValue(layout.Id);
        foreach (var mappings in source.Profiles.Select(profile => profile.Mappings).Append(source.SharedDeckMappings).Concat(source.DeckLayouts.Select(deck => deck.Mappings)))
        {
            foreach (var mapping in mappings.ToArray())
            {
                if (mapping.Value.Equals(action, StringComparison.OrdinalIgnoreCase))
                {
                    mapping.Kind = ActionKind.None;
                    mapping.Value = "";
                }
                if (mapping.LongPressValue.Equals(action, StringComparison.OrdinalIgnoreCase))
                {
                    mapping.LongPressKind = ActionKind.None;
                    mapping.LongPressValue = "";
                }
                if (!MappingHasConfiguredAction(mapping) && !(DeckPanelLayout.IsInputName(mapping.Input) && HasDeckButtonContent(mapping)))
                    mappings.Remove(mapping);
            }
        }
        foreach (var macro in source.Macros)
            macro.Steps.RemoveAll(step => step.RecordedActionValue.Equals(action, StringComparison.OrdinalIgnoreCase));
        foreach (var gesture in source.Gestures)
        {
            if (gesture.UpValue.Equals(action, StringComparison.OrdinalIgnoreCase)) { gesture.UpKind = ActionKind.None; gesture.UpValue = ""; }
            if (gesture.DownValue.Equals(action, StringComparison.OrdinalIgnoreCase)) { gesture.DownKind = ActionKind.None; gesture.DownValue = ""; }
            if (gesture.LeftValue.Equals(action, StringComparison.OrdinalIgnoreCase)) { gesture.LeftKind = ActionKind.None; gesture.LeftValue = ""; }
            if (gesture.RightValue.Equals(action, StringComparison.OrdinalIgnoreCase)) { gesture.RightKind = ActionKind.None; gesture.RightValue = ""; }
            if (gesture.CenterValue.Equals(action, StringComparison.OrdinalIgnoreCase)) { gesture.CenterKind = ActionKind.None; gesture.CenterValue = ""; }
        }
        return removed;
    }
    void PersistDeckPanelPosition(double left, double top)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top))
            return;
        config.DeckPanelLeft = left;
        config.DeckPanelTop = top;
        appliedConfig.DeckPanelLeft = left;
        appliedConfig.DeckPanelTop = top;
        try
        {
            var persisted = store.Load();
            persisted.DeckPanelLeft = left;
            persisted.DeckPanelTop = top;
            store.Save(persisted);
        }
        catch { }
    }
    void PersistDeckPanelSize(string layoutId, double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
            return;
        static void Apply(IEnumerable<DeckLayoutDefinition> layouts, string id, double w, double h)
        {
            var target = layouts.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                target.PanelWidth = w;
                target.PanelHeight = h;
            }
        }
        Apply(config.DeckLayouts, layoutId, width, height);
        Apply(appliedConfig.DeckLayouts, layoutId, width, height);
        try
        {
            var persisted = store.Load();
            Apply(persisted.DeckLayouts, layoutId, width, height);
            store.Save(persisted);
        }
        catch { }
    }
    void PersistDeckPanelPinned(string layoutId, bool pinned)
    {
        static void Apply(IEnumerable<DeckLayoutDefinition> layouts, string id, bool value)
        {
            var target = layouts.FirstOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (target != null)
                target.PanelPinned = value;
        }

        Apply(config.DeckLayouts, layoutId, pinned);
        Apply(appliedConfig.DeckLayouts, layoutId, pinned);
        try
        {
            var persisted = store.Load();
            Apply(persisted.DeckLayouts, layoutId, pinned);
            store.Save(persisted);
        }
        catch { }
    }
    void PersistInputPanelPosition(bool extended, double left, double top)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top))
            return;
        if (extended)
        {
            config.ExtendedKeypadPanelLeft = left;
            config.ExtendedKeypadPanelTop = top;
            appliedConfig.ExtendedKeypadPanelLeft = left;
            appliedConfig.ExtendedKeypadPanelTop = top;
        }
        else
        {
            config.NumpadPanelLeft = left;
            config.NumpadPanelTop = top;
            appliedConfig.NumpadPanelLeft = left;
            appliedConfig.NumpadPanelTop = top;
        }
        try
        {
            var persisted = store.Load();
            if (extended)
            {
                persisted.ExtendedKeypadPanelLeft = left;
                persisted.ExtendedKeypadPanelTop = top;
            }
            else
            {
                persisted.NumpadPanelLeft = left;
                persisted.NumpadPanelTop = top;
            }
            store.Save(persisted);
        }
        catch { }
    }
    void KeypadInput_Click(object sender, RoutedEventArgs e)
    {
        if (selected == null)
        {
            ShowInlineNotice("先に割り当てるキーを選択してください");
            return;
        }
        bool longPress = ReferenceEquals(sender, LongKindBox);
        TextBox target = longPress ? LongValueBox : ValueBox;
        var kindBox = longPress ? LongKindBox : KindBox;
        var picker = new MacroInputPickerWindow(config.KeyboardLayout) { Owner = this };
        picker.ConfigureShortcutEditing(target.Text);
        bool changed = false;
        picker.ShortcutChanged += value =>
        {
            changed = true;
            kindBox.SelectedValue = ActionKind.Shortcut;
            target.Text = value;
            if (longPress)
                LongPressExpander.IsExpanded = true;
        };
#if !PRODUCTION_PUBLISH
        if (KeypadInputRequestedForTest != null)
            KeypadInputRequestedForTest(picker);
        else
#endif
        picker.ShowDialog();
        if (changed)
            CompleteDestinationInput();
    }

    // Deck overlay synchronization
    void HandleOverlayDeckLayoutChanged()
    {
        // The overlay changes the same Deck layout model.  Persist that change
        // and rebuild the currently visible management grid so it cannot show
        // stale slot contents after an overlay reorder or file drop.
        MarkDirty(refreshDeckPanel: false);
        if (!deckManagementMode)
            return;
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            if (!deckManagementMode)
                return;
            if (DeckEditorWorkspace.Visibility == Visibility.Visible)
            {
                ClearSelectedInput();
                BuildDeckManagementPanel();
            }
            else if (DeckLayoutListWorkspace.Visibility == Visibility.Visible)
                RefreshDeckLayoutCards();
        }));
    }
    void HandleOverlayDeckSlotsChanged(string layoutId, int firstSlot, int secondSlot)
    {
        MarkDirty(refreshDeckPanel: false);
        if (!deckManagementMode || DeckEditorWorkspace.Visibility != Visibility.Visible || selectedDeckLayout?.Id != layoutId)
            return;
        AnimateDeckManagementSwap(firstSlot, secondSlot);
    }
    void AnimateDeckManagementSwap(int firstSlot, int secondSlot)
    {
        string firstInput = DeckPanelLayout.InputName(firstSlot), secondInput = DeckPanelLayout.InputName(secondSlot);
        var first = deckManagementButtons.FirstOrDefault(x => x.Tag is string tag && tag.Equals(firstInput, StringComparison.OrdinalIgnoreCase));
        var second = deckManagementButtons.FirstOrDefault(x => x.Tag is string tag && tag.Equals(secondInput, StringComparison.OrdinalIgnoreCase));
        if (first == null || second == null || firstSlot == secondSlot)
        {
            foreach (var button in new[] { first, second }.Where(x => x != null))
                UpdateDeckManagementButtonVisual(button!);
            return;
        }
        System.Windows.Point firstOrigin = first.TranslatePoint(new System.Windows.Point(), DeckManagementGrid);
        System.Windows.Point secondOrigin = second.TranslatePoint(new System.Windows.Point(), DeckManagementGrid);
        var firstTranslation = new TranslateTransform();
        var secondTranslation = new TranslateTransform();
        first.RenderTransform = firstTranslation;
        second.RenderTransform = secondTranslation;
        System.Windows.Controls.Panel.SetZIndex(first, 2);
        System.Windows.Controls.Panel.SetZIndex(second, 1);
        first.Opacity = .86;
        second.Opacity = .86;
        var duration = TimeSpan.FromMilliseconds(145);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var firstX = new DoubleAnimation(0, secondOrigin.X - firstOrigin.X, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
        var firstY = new DoubleAnimation(0, secondOrigin.Y - firstOrigin.Y, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
        var secondX = new DoubleAnimation(0, firstOrigin.X - secondOrigin.X, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
        var secondY = new DoubleAnimation(0, firstOrigin.Y - secondOrigin.Y, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
        var opacity = new DoubleAnimation(.86, 1, duration) { EasingFunction = easing, FillBehavior = FillBehavior.Stop };
        firstX.Completed += (_, _) =>
        {
            first.RenderTransform = Transform.Identity;
            second.RenderTransform = Transform.Identity;
            first.Opacity = 1;
            second.Opacity = 1;
            System.Windows.Controls.Panel.SetZIndex(first, 0);
            System.Windows.Controls.Panel.SetZIndex(second, 0);
            UpdateDeckManagementButtonVisual(first);
            UpdateDeckManagementButtonVisual(second);
        };
        firstTranslation.BeginAnimation(TranslateTransform.XProperty, firstX);
        firstTranslation.BeginAnimation(TranslateTransform.YProperty, firstY);
        secondTranslation.BeginAnimation(TranslateTransform.XProperty, secondX);
        secondTranslation.BeginAnimation(TranslateTransform.YProperty, secondY);
        first.BeginAnimation(UIElement.OpacityProperty, opacity);
        second.BeginAnimation(UIElement.OpacityProperty, opacity);
    }
}
