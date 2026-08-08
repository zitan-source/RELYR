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
        MarkDirty();
        RefreshSelectedInputVisual(selected.Input);
    }
    static bool HasDeckButtonContent(Mapping? mapping) => MappingHasConfiguredAction(mapping) || !string.IsNullOrWhiteSpace(mapping?.Description) || !string.IsNullOrWhiteSpace(mapping?.DeckColor) || DeckPanelLayout.HasRegisteredFile(mapping);
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
        MarkDirty();
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
        DeckManagementGrid.Width = layout.Columns * (DeckPanelLayout.KeyWidth + DeckPanelLayout.Gap);
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
            button.Margin = new Thickness(2, 0, 2, 0);
            var nameLabel = DeckPanelLayout.CreateNameLabel(null);
            var cell = new StackPanel { Width = DeckPanelLayout.KeyWidth + DeckPanelLayout.Gap, Height = DeckPanelLayout.CellHeight };
            cell.Children.Add(button);
            cell.Children.Add(nameLabel);
            DeckManagementGrid.Children.Add(cell);
            deckManagementButtons.Add(button);
            deckManagementNameLabels[button] = nameLabel;
        }
        ColorDeckManagementButtons();
    }
    void DeckManagementButtonClicked(System.Windows.Controls.Button source, string input)
    {
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
            DragDrop.DoDragDrop(button, data, System.Windows.DragDropEffects.Move | System.Windows.DragDropEffects.Copy);
        }
        finally { ClearDeckReorderTarget(); }
        e.Handled = true;
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
        e.Effects = validSlot ? System.Windows.DragDropEffects.Move : file ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
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
        KindBox.SelectedValuePath = nameof(ActionOption.Kind);
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
        KindBox.SelectedValuePath = nameof(ActionOption.Kind);
        DeckNameEditorPanel.Visibility = Visibility.Collapsed;
        UpdateDeckScopeUi();
    }
    void UpdateDeckScopeUi()
    {
        if (ProfileBox == null)
            return;
        ProfileBox.IsEnabled = true;
        ProfileBox.Opacity = 1;
    }
    void ShowDeckLayoutList()
    {
        CloseDeckEditorMediaPreview();
        selectedDeckLayout = null;
        DeckLayoutListWorkspace.Visibility = Visibility.Visible;
        DeckEditorWorkspace.Visibility = Visibility.Collapsed;
        ToolbarSaveButton.Visibility = Visibility.Visible;
        WorkspaceSubtitle.Text = $"{config.DeckLayouts.Count}個のレイアウト";
        RefreshDeckLayoutCards();
        UpdateDeckScopeUi();
    }
    void RefreshDeckLayoutCards()
    {
        if (DeckLayoutCardsPanel == null)
            return;
        DeckLayoutCardsPanel.Children.Clear();
        foreach (var layout in config.DeckLayouts)
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
        var preview = new System.Windows.Controls.Primitives.UniformGrid { Rows = layout.Rows, Columns = layout.Columns, Width = previewSize.Width, Height = previewSize.Height, Margin = new Thickness(0, 0, 0, 12) };
        for (int index = 0; index < DeckPanelLayout.VisibleSlotCount(layout); index++)
        {
            var cell = new Border { Margin = new Thickness(1), CornerRadius = new CornerRadius(2) };
            cell.SetResourceReference(Border.BackgroundProperty, "ControlBackground");
            preview.Children.Add(cell);
        }
        bool isDefault = config.DefaultDeckLayoutId.Equals(layout.Id, StringComparison.OrdinalIgnoreCase);
        var content = new StackPanel();
        content.Children.Add(preview);
        content.Children.Add(new TextBlock { Text = layout.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        content.Children.Add(new TextBlock { Text = $"{layout.Columns}×{layout.Rows}・{DeckPanelLayout.VisibleSlotCount(layout)}ボタン" + (isDefault ? "  ・  既定" : ""), FontSize = 11, Margin = new Thickness(0, 5, 0, 0), Foreground = ThemeService.Brush(isDefault ? "AccentBrush" : "SecondaryText") });
        var card = new System.Windows.Controls.Button { Tag = layout, Content = content, Width = 236, Height = 190, Margin = new Thickness(0, 0, 14, 14), Padding = new Thickness(16), HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, Background = ThemeService.Brush("CardBackground"), BorderBrush = ThemeService.Brush(isDefault ? "AccentBrush" : "BorderBrush"), BorderThickness = new Thickness(1) };
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
        var name = new TextBox { Style = (Style)FindResource(typeof(TextBox)), Text = "新しいDeck", Height = 38, Margin = new Thickness(0, 6, 0, 18), VerticalContentAlignment = VerticalAlignment.Center };
        Grid.SetRow(name, 1);
        root.Children.Add(name);

        var sizeRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
        sizeRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sizeRow.Children.Add(new TextBlock { Text = "サイズ", VerticalAlignment = VerticalAlignment.Center, Foreground = ThemeService.Brush("SecondaryText") });
        var sizes = new System.Windows.Controls.ComboBox { Name = "NewDeckSizeBox", Style = (Style)FindResource("ToolbarComboBoxStyle"), Width = 220, Height = 36, Margin = new Thickness(0), SelectedIndex = 1 };
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
        var columnsBox = new TextBox { Style = (Style)FindResource(typeof(TextBox)), Text = "9", Width = 48, Height = 36, MaxLength = 2, Margin = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        var times = new TextBlock { Text = "×", Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Foreground = ThemeService.Brush("SecondaryText") };
        Grid.SetColumn(times, 1);
        var rowsBox = new TextBox { Style = (Style)FindResource(typeof(TextBox)), Text = "5", Width = 48, Height = 36, MaxLength = 2, Margin = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
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
        var cancel = new System.Windows.Controls.Button { Content = "キャンセル", IsCancel = true, Height = 38, MinWidth = 92, Margin = new Thickness(0, 0, 8, 0) };
        var create = new System.Windows.Controls.Button { Content = "作成", IsDefault = true, Height = 38, MinWidth = 84, Background = ThemeService.Brush("AccentStrongBrush"), Foreground = ThemeService.Brush("AccentButtonText"), BorderBrush = ThemeService.Brush("AccentBrush") };
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
        return int.TryParse(columnsText, out columns) && int.TryParse(rowsText, out rows) && columns is >= 1 and <= DeckPanelLayout.MaximumColumns && rows is >= 1 and <= DeckPanelLayout.MaximumRows;
    }
    void EditDeckLayout(DeckLayoutDefinition layout)
    {
        selectedDeckLayout = layout;
        DeckLayoutListWorkspace.Visibility = Visibility.Collapsed;
        DeckEditorWorkspace.Visibility = Visibility.Visible;
        ToolbarSaveButton.Visibility = Visibility.Collapsed;
        updatingDeckEditor = true;
        DeckLayoutNameBox.Text = layout.Name;
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
        MarkDirty();
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
            DeckPanelColorPreview.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        }
    }
    void SetDefaultDeckLayout(DeckLayoutDefinition layout, bool refresh = true)
    {
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
        var copy = new DeckLayoutDefinition { Name = name, Columns = source.Columns, Rows = source.Rows, PanelColor = source.PanelColor, Mappings = [.. source.Mappings.Select(CloneMapping)] };
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
        if (IsDeckLayoutReferenced(layout))
        {
            WpfMessageBox.Show(this, "このレイアウトは既定設定またはアクションから参照されています。先に参照先を別のレイアウトへ変更してください。", "Deckレイアウトを削除できません", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (WpfMessageBox.Show(this, $"「{layout.Name}」を削除しますか？\n割り当てたボタンも削除されます。", "Deckレイアウトの削除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        config.DeckLayouts.Remove(layout);
        MarkDirty();
        RefreshDeckLayoutCards();
    }
    bool IsDeckLayoutReferenced(DeckLayoutDefinition layout)
    {
        if (config.DefaultDeckLayoutId.Equals(layout.Id, StringComparison.OrdinalIgnoreCase))
            return true;
        string action = DeckPanelLayout.ActionValue(layout.Id);
        IEnumerable<Mapping> mappings = config.Profiles.SelectMany(x => x.Mappings).Concat(config.SharedDeckMappings).Concat(config.DeckLayouts.SelectMany(x => x.Mappings));
        if (mappings.Any(x => x.Value.Equals(action, StringComparison.OrdinalIgnoreCase) || x.LongPressValue.Equals(action, StringComparison.OrdinalIgnoreCase)))
            return true;
        if (config.Macros.SelectMany(x => x.Steps).Any(x => x.RecordedActionValue.Equals(action, StringComparison.OrdinalIgnoreCase)))
            return true;
        return config.Gestures.Any(x => new[] { x.UpValue, x.DownValue, x.LeftValue, x.RightValue, x.CenterValue }.Any(v => v.Equals(action, StringComparison.OrdinalIgnoreCase)));
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
    void DeckKeypadInput_Click(object sender, RoutedEventArgs e)
    {
        if (!deckManagementMode || selected == null)
        {
            ShowInlineNotice("先にDeckパネルのボタンを選択してください");
            return;
        }
        var picker = new MacroInputPickerWindow(config.KeyboardLayout) { Owner = this };
        picker.ConfigureShortcutEditing(ValueBox.Text);
        picker.ShortcutChanged += value =>
        {
            KindBox.SelectedValue = ActionKind.Shortcut;
            ValueBox.Text = value;
        };
        picker.ShowDialog();
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
