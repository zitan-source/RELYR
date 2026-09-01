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
    bool updatingDeckManagementViewport;
    DeckEditorLayoutMode? deckEditorLayoutMode;
    bool deckCustomizeOpen;
    bool deckCompactCommandBar;
    DeckCustomizeTab selectedDeckCustomizeTab = DeckCustomizeTab.Layout;
    DeckEditorViewMode selectedDeckEditorViewMode = DeckEditorViewMode.Grid;
    readonly List<System.Windows.Controls.Button> deckGridButtons = [];
    readonly List<System.Windows.Controls.Button> deckListButtons = [];
    readonly Dictionary<System.Windows.Controls.Button, (TextBlock Type, TextBlock Value)> deckListActionLabels = [];
    readonly Dictionary<Border, System.Windows.Controls.Button> deckListActionTargets = [];
    const string DeckActionDragFormat = "RELYR.DeckAction.v1";
    const string DeckSlotGroupDragFormat = "RELYR.DeckSlotGroup.v1";
    Border? deckListActionDragSource;
    Border? deckListActionDropTarget;
    System.Windows.Point deckListActionDragStart;
    System.Windows.Controls.Button? deckClickModifierSource;
    ModifierKeys deckClickModifiersAtMouseDown;
    readonly System.Windows.Threading.DispatcherTimer deckCustomizationRefreshTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(280)
    };
    bool deckCustomizationRefreshTimerHooked;
    bool deckCustomizationRefreshPending;
    bool deckManagementListRefreshPending;
    bool deckCustomizationSliderDragging;
    bool deckOverlayVisualSynchronized;
    enum DeckEditorLayoutMode { Closed, DrawerSide, DrawerStacked }
    enum DeckCustomizeTab { Layout, Appearance, Behavior }
    enum DeckEditorViewMode { Grid, List }

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
    static bool HasDeckButtonContent(Mapping? mapping) => MappingHasConfiguredAction(mapping) || DeckMonitorCatalog.IsMonitor(mapping?.DeckMonitor) || !string.IsNullOrWhiteSpace(mapping?.Description) || !string.IsNullOrWhiteSpace(mapping?.DeckColor) || DeckPanelLayout.HasRegisteredFile(mapping) || DeckIconCatalog.HasIcon(mapping);
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
        if (normalized.Length > 0)
            DeckPanelLayout.ApplyRegisteredFile(mapping, normalized);
        else
            mapping.DeckFilePath = string.Empty;
        if (selected?.Input.Equals(input, StringComparison.OrdinalIgnoreCase) == true)
        {
            if (normalized.Length > 0)
                DeckPanelLayout.ApplyRegisteredFile(selected, normalized);
            else
                selected.DeckFilePath = string.Empty;
        }
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
    void DeckAutoDismissBehaviorChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updatingDeckEditor || config == null)
            return;
        config.DeckAfterActionBehavior = SelectedDeckAutoDismissBehavior(DeckAfterActionBehaviorBox);
        config.DeckPointerLeaveBehavior = SelectedDeckAutoDismissBehavior(DeckPointerLeaveBehaviorBox);
        MarkDirty();
    }

    static DeckAutoDismissBehavior SelectedDeckAutoDismissBehavior(System.Windows.Controls.ComboBox box)
        => Enum.TryParse((box.SelectedItem as ComboBoxItem)?.Tag?.ToString(), true, out DeckAutoDismissBehavior behavior)
            ? behavior
            : DeckAutoDismissBehavior.StayVisible;

    static void SelectDeckAutoDismissBehavior(System.Windows.Controls.ComboBox box, DeckAutoDismissBehavior behavior)
    {
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().First(item =>
            string.Equals(item.Tag?.ToString(), behavior.ToString(), StringComparison.OrdinalIgnoreCase));
    }
    internal void SetDeckButtonNameForTest(string input, string name) => SetDeckButtonName(input, name);
    internal ContextMenu CreateDeckInputContextMenu(string input)
    {
        var mappings = MappingCollectionForInput(input);
        var menu = new ContextMenu { MinWidth = 242 };
        var copyAssignment = CreateDeckContextMenuItem("\uE8C8", "この割り当てをコピー", "");
        copyAssignment.Click += (_, _) => CopyDeckAssignment(input);
        var pasteAssignment = CreateDeckContextMenuItem("\uE77F", "コピーした割り当てを貼り付け", "");
        pasteAssignment.Click += (_, _) => PasteDeckAssignment(input);
        var rename = CreateDeckContextMenuItem("\uE70F", "名前の変更...", "");
        rename.Click += (_, _) => RenameDeckButton(input);
        var copyFile = CreateDeckContextMenuItem("\uE8C8", "登録ファイルをコピー", "");
        copyFile.Click += (_, _) => { var mapping = mappings.LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase)); if (mapping != null) CopyDeckFileToClipboard(mapping); };
        var pasteFile = CreateDeckContextMenuItem("\uE77F", "クリップボードのファイルを登録", "");
        pasteFile.Click += (_, _) => { string? file = ClipboardDeckFile(); if (file != null) SetDeckButtonFile(input, file); };
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
        menu.Items.Add(copyAssignment);
        menu.Items.Add(pasteAssignment);
        menu.Items.Add(new Separator());
        menu.Items.Add(rename);
        menu.Items.Add(new Separator());
        menu.Items.Add(copyFile);
        menu.Items.Add(pasteFile);
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
            copyAssignment.IsEnabled = existing != null && HasDeckButtonContent(existing);
            pasteAssignment.IsEnabled = copiedDeckMapping != null;
            copyFile.IsEnabled = DeckPanelLayout.IsAvailableFile(existing);
            pasteFile.IsEnabled = ClipboardDeckFile() != null;
            reveal.IsEnabled = DeckPanelLayout.IsAvailableFile(existing);
            resetColor.IsEnabled = DeckPanelLayout.TryGetButtonColor(existing, out _);
            delete.IsEnabled = existing != null;
        };
        return menu;
    }
    void CopyDeckAssignment(string input)
    {
        var mapping = MappingCollectionForInput(input).LastOrDefault(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
        if (mapping == null || !HasDeckButtonContent(mapping))
            return;
        copiedDeckMapping = CloneMapping(mapping);
        ShowInlineNotice(DisplayInputName(input) + " の割り当てをコピーしました");
    }
    void PasteDeckAssignment(string input)
    {
        if (copiedDeckMapping == null || !DeckPanelLayout.IsInputName(input))
            return;
        var mappings = MappingCollectionForInput(input);
        var copy = CloneMapping(copiedDeckMapping);
        copy.Input = input;
        copy.Layer = DeckPanelLayout.Layer;
        mappings.RemoveAll(mapping => mapping.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
        mappings.Add(copy);
        ClearSelectedInput();
        MarkDirty();
        RefreshSelectedInputVisual(input);
        ShowInlineNotice(DisplayInputName(input) + " へ割り当てを貼り付けました");
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
        mapping.DeckIconAutoAssigned = false;
        if (selected?.Input.Equals(input, StringComparison.OrdinalIgnoreCase) == true)
        {
            selected.DeckIcon = mapping.DeckIcon;
            selected.DeckIconPath = mapping.DeckIconPath;
            selected.DeckIconAutoAssigned = false;
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
        if (!editorUiInitialized)
            return;
        CloseDeckEditorMediaPreview();
        DeckManagementGrid.Children.Clear();
        deckManagementButtons.Clear();
        deckGridButtons.Clear();
        deckManagementNameLabels.Clear();
        var layout = selectedDeckLayout ?? DeckPanelLayout.DefaultLayout(config);
        if (layout == null)
            return;
        DeckManagementGrid.Rows = layout.Rows;
        DeckManagementGrid.Columns = layout.Columns;
        DeckManagementGrid.Width = layout.Columns * DeckPanelLayout.CellWidth;
        DeckManagementGrid.Height = layout.Rows * DeckPanelLayout.CellHeight;
        for (int slot = 1; slot <= DeckPanelLayout.VisibleSlotCount(layout); slot++)
            AddDeckManagementGridCell(slot);
        ColorDeckManagementButtons();
        ApplyDeckEditorViewMode();
        UpdateDeckManagementViewport();
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(UpdateDeckManagementViewport));
    }

    void AddDeckManagementGridCell(int slot)
    {
        int capturedSlot = slot;
        var button = new System.Windows.Controls.Button
        {
            Tag = DeckPanelLayout.InputName(slot),
            MinWidth = 0,
            MinHeight = 0,
            FontSize = 11,
            AllowDrop = true,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalContentAlignment = System.Windows.VerticalAlignment.Center,
            Style = (Style)FindResource("DeckButtonStyle"),
            Width = DeckPanelLayout.KeyWidth,
            Height = DeckPanelLayout.KeyHeight,
            Margin = new Thickness(DeckPanelLayout.ButtonGap / 2, 0, DeckPanelLayout.ButtonGap / 2, 0),
            Padding = new Thickness(3)
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
        var nameLabel = DeckPanelLayout.CreateNameLabel(null);
        var cell = new StackPanel { Width = DeckPanelLayout.CellWidth, Height = DeckPanelLayout.CellHeight };
        cell.Children.Add(button);
        cell.Children.Add(nameLabel);
        DeckManagementGrid.Children.Add(cell);
        deckGridButtons.Add(button);
        deckManagementButtons.Add(button);
        deckManagementNameLabels[button] = nameLabel;
    }

    void ResizeDeckManagementPanel(int columns, int rows, bool deferListRefresh)
    {
        DeckManagementGrid.Rows = rows;
        DeckManagementGrid.Columns = columns;
        DeckManagementGrid.Width = columns * DeckPanelLayout.CellWidth;
        DeckManagementGrid.Height = rows * DeckPanelLayout.CellHeight;
        int desiredCount = columns * rows;
        while (deckGridButtons.Count > desiredCount)
        {
            var button = deckGridButtons[^1];
            if (button.Parent is UIElement cell)
                DeckManagementGrid.Children.Remove(cell);
            deckGridButtons.RemoveAt(deckGridButtons.Count - 1);
            deckManagementButtons.Remove(button);
            deckManagementNameLabels.Remove(button);
        }
        while (deckGridButtons.Count < desiredCount)
        {
            AddDeckManagementGridCell(deckGridButtons.Count + 1);
            UpdateDeckManagementButtonVisual(deckGridButtons[^1]);
        }
        if (selectedDeckEditorViewMode == DeckEditorViewMode.List)
        {
            if (deferListRefresh)
                deckManagementListRefreshPending = true;
            else
                BuildDeckManagementList();
        }
        UpdateDeckManagementViewport();
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(UpdateDeckManagementViewport));
    }

    void BuildDeckManagementList()
    {
        ClearDeckManagementList();
        var layout = selectedDeckLayout ?? DeckPanelLayout.DefaultLayout(config);
        if (layout == null)
            return;
        for (int slot = 1; slot <= DeckPanelLayout.VisibleSlotCount(layout); slot++)
        {
            int capturedSlot = slot;
            string input = DeckPanelLayout.InputName(slot);
            var button = new System.Windows.Controls.Button
            {
                Tag = input,
                Width = DeckPanelLayout.KeyWidth,
                Height = DeckPanelLayout.KeyHeight,
                MinWidth = 0,
                MinHeight = 0,
                Margin = new Thickness(0),
                Padding = new Thickness(3),
                FontSize = 11,
                AllowDrop = true,
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

            var actionType = new TextBlock
            {
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            actionType.SetResourceReference(TextBlock.ForegroundProperty, "PrimaryText");
            var actionValue = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 0),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            actionValue.SetResourceReference(TextBlock.ForegroundProperty, "MutedText");
            var actionText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            actionText.Children.Add(actionType);
            actionText.Children.Add(actionValue);
            var actionPanel = new Border
            {
                Tag = input,
                AllowDrop = true,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(8, 5, 8, 5),
                CornerRadius = new CornerRadius(8),
                Background = WpfBrushes.Transparent,
                BorderBrush = WpfBrushes.Transparent,
                BorderThickness = new Thickness(1),
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = actionText
            };
            actionPanel.PreviewMouseLeftButtonDown += DeckListActionDragStarted;
            actionPanel.PreviewMouseMove += DeckListActionDragMoved;
            actionPanel.PreviewMouseLeftButtonUp += DeckListActionDragEnded;
            actionPanel.PreviewDragEnter += DeckListActionDragOver;
            actionPanel.PreviewDragOver += DeckListActionDragOver;
            actionPanel.PreviewDragLeave += DeckListActionDragLeave;
            actionPanel.PreviewDrop += DeckListActionDropped;
            var row = new Grid { Height = 68, Margin = new Thickness(0, 0, 0, 6) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(button);
            Grid.SetColumn(actionPanel, 1);
            row.Children.Add(actionPanel);
            DeckListPanel.Children.Add(row);
            deckListButtons.Add(button);
            deckManagementButtons.Add(button);
            deckListActionLabels[button] = (actionType, actionValue);
            deckListActionTargets[actionPanel] = button;
        }
        ColorDeckManagementButtons();
    }

    void ClearDeckManagementList()
    {
        foreach (var oldButton in deckListButtons)
        {
            deckManagementButtons.Remove(oldButton);
            deckManagementNameLabels.Remove(oldButton);
            deckListActionLabels.Remove(oldButton);
        }
        deckListButtons.Clear();
        deckListActionTargets.Clear();
        deckListActionDragSource = null;
        ClearDeckListActionDropTarget();
        DeckListPanel.Children.Clear();
    }

    readonly record struct DeckActionState(
        ActionKind Kind,
        string Value,
        ActionKind LongPressKind,
        string LongPressValue,
        int LongPressMs,
        string DragValue,
        string DragEndValue,
        string Application,
        string DeckMonitor)
    {
        internal static DeckActionState From(Mapping? mapping) => mapping == null
            ? new(ActionKind.None, "", ActionKind.None, "", 500, "", "", "", "")
            : new(mapping.Kind, mapping.Value, mapping.LongPressKind, mapping.LongPressValue,
                mapping.LongPressMs, mapping.DragValue, mapping.DragEndValue, mapping.Application, mapping.DeckMonitor);

        internal bool HasContent => Kind != ActionKind.None || LongPressKind != ActionKind.None
            || !string.IsNullOrWhiteSpace(Value) || !string.IsNullOrWhiteSpace(LongPressValue)
            || !string.IsNullOrWhiteSpace(DragValue) || !string.IsNullOrWhiteSpace(DragEndValue)
            || !string.IsNullOrWhiteSpace(Application) || DeckMonitorCatalog.IsMonitor(DeckMonitor);
    }

    internal static bool SwapDeckActionsPreservingButtonAppearance(DeckLayoutDefinition layout, string firstInput, string secondInput)
    {
        if (!DeckPanelLayout.IsInputName(firstInput) || !DeckPanelLayout.IsInputName(secondInput)
            || firstInput.Equals(secondInput, StringComparison.OrdinalIgnoreCase))
            return false;
        Mapping? first = layout.Mappings.LastOrDefault(mapping => mapping.Input.Equals(firstInput, StringComparison.OrdinalIgnoreCase));
        Mapping? second = layout.Mappings.LastOrDefault(mapping => mapping.Input.Equals(secondInput, StringComparison.OrdinalIgnoreCase));
        DeckActionState firstAction = DeckActionState.From(first);
        DeckActionState secondAction = DeckActionState.From(second);
        if (!firstAction.HasContent && !secondAction.HasContent)
            return false;
        ApplyDeckActionState(layout.Mappings, firstInput, secondAction);
        ApplyDeckActionState(layout.Mappings, secondInput, firstAction);
        return true;
    }

    static void ApplyDeckActionState(List<Mapping> mappings, string input, DeckActionState action)
    {
        Mapping? mapping = mappings.LastOrDefault(candidate => candidate.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
        if (mapping == null && action.HasContent)
        {
            mapping = new Mapping { Input = input, Layer = DeckPanelLayout.Layer };
            mappings.Add(mapping);
        }
        if (mapping == null)
            return;
        mapping.Kind = action.Kind;
        mapping.Value = action.Value;
        mapping.LongPressKind = action.LongPressKind;
        mapping.LongPressValue = action.LongPressValue;
        mapping.LongPressMs = action.LongPressMs;
        mapping.DragValue = action.DragValue;
        mapping.DragEndValue = action.DragEndValue;
        mapping.Application = action.Application;
        mapping.DeckMonitor = action.DeckMonitor;
        NormalizeLongOnlyMapping(mapping);
        if (!HasDeckButtonContent(mapping))
            mappings.Remove(mapping);
    }

    void DeckListActionDragStarted(object sender, MouseButtonEventArgs e)
    {
        if (MultiSelectToggle.IsChecked == true || sender is not Border { Tag: string input } target)
            return;
        var layout = selectedDeckLayout ?? DeckPanelLayout.DefaultLayout(config);
        var mapping = DeckPanelLayout.FindMapping(layout, DeckPanelLayout.SlotNumber(input));
        if (!MappingHasConfiguredAction(mapping) && !DeckMonitorCatalog.IsMonitor(mapping?.DeckMonitor))
            return;
        deckListActionDragSource = target;
        deckListActionDragStart = e.GetPosition(target);
    }

    void DeckListActionDragMoved(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (sender is not Border { Tag: string input } source || !ReferenceEquals(source, deckListActionDragSource)
            || e.LeftButton != MouseButtonState.Pressed)
            return;
        System.Windows.Point current = e.GetPosition(source);
        if (Math.Abs(current.X - deckListActionDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - deckListActionDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        deckListActionDragSource = null;
        var data = new System.Windows.DataObject();
        data.SetData(DeckActionDragFormat, input);
        try { System.Windows.DragDrop.DoDragDrop(source, data, System.Windows.DragDropEffects.Move); }
        finally { ClearDeckListActionDropTarget(); }
        e.Handled = true;
    }

    void DeckListActionDragEnded(object sender, MouseButtonEventArgs e)
    {
        deckListActionDragSource = null;
        ClearDeckListActionDropTarget();
    }

    void DeckListActionDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not Border { Tag: string target } actionTarget || !DeckPanelLayout.IsInputName(target))
            return;
        bool valid;
        if (e.Data.GetDataPresent(DeckActionDragFormat) && e.Data.GetData(DeckActionDragFormat) is string source)
        {
            valid = DeckPanelLayout.IsInputName(source) && !source.Equals(target, StringComparison.OrdinalIgnoreCase);
            e.Effects = valid ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
        }
        else if (TryGetPaletteMonitor(e.Data, out _))
        {
            valid = selectedDeckLayout != null;
            e.Effects = valid ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        }
        else if (TryGetPaletteAction(e.Data, out CatalogAction action))
        {
            valid = CanAssignPaletteAction(target, action);
            e.Effects = valid ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        }
        else
        {
            valid = false;
            e.Effects = System.Windows.DragDropEffects.None;
        }
        SetDeckListActionDropTarget(valid ? actionTarget : null);
        e.Handled = true;
    }

    void DeckListActionDragLeave(object sender, System.Windows.DragEventArgs e)
    {
        if (ReferenceEquals(sender, deckListActionDropTarget))
            ClearDeckListActionDropTarget();
    }

    void SetDeckListActionDropTarget(Border? target)
    {
        if (ReferenceEquals(target, deckListActionDropTarget))
            return;
        ClearDeckListActionDropTarget();
        if (target == null)
            return;
        deckListActionDropTarget = target;
        target.Background = ThemeService.Brush("AccentSoftBrush");
        target.BorderBrush = ThemeService.Brush("AccentBrush");
    }

    void ClearDeckListActionDropTarget()
    {
        if (deckListActionDropTarget == null)
            return;
        deckListActionDropTarget.Background = WpfBrushes.Transparent;
        deckListActionDropTarget.BorderBrush = WpfBrushes.Transparent;
        deckListActionDropTarget = null;
    }

    void DeckListActionDropped(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not Border { Tag: string target } || !DeckPanelLayout.IsInputName(target))
            return;
        ClearDeckListActionDropTarget();
        if (e.Data.GetDataPresent(DeckActionDragFormat) && e.Data.GetData(DeckActionDragFormat) is string source)
        {
            var layout = selectedDeckLayout;
            bool swapped = layout != null && SwapDeckActionsPreservingButtonAppearance(layout, source, target);
            if (swapped)
            {
                RefreshSelectedInputVisual(source);
                RefreshSelectedInputVisual(target);
                MarkDirty(refreshDeckPanel: false);
                OverlayService.RefreshDeckPanelSlots(layout!.Id,
                    [DeckPanelLayout.SlotNumber(source), DeckPanelLayout.SlotNumber(target)]);
                deckOverlayVisualSynchronized = true;
                SelectInput(target, false);
                ShowInlineNotice($"{DisplayInputName(source)} と {DisplayInputName(target)} のActionだけを入れ替えました");
            }
            e.Effects = swapped ? System.Windows.DragDropEffects.Move : System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (TryGetPaletteMonitor(e.Data, out DeckMonitorDefinition monitor))
        {
            bool applied = ApplyPaletteMonitorDrop(monitor, target);
            e.Effects = applied ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (TryGetPaletteAction(e.Data, out CatalogAction action))
        {
            bool applied = CanAssignPaletteAction(target, action) && ApplyPaletteActionDrop(action, target, target);
            e.Effects = applied ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
            e.Handled = true;
        }
    }

    static (string Type, string Value) DeckListActionSummary(Mapping? mapping)
    {
        if (DeckMonitorCatalog.TryGet(mapping?.DeckMonitor, out var monitor))
            return (DeckMonitorCatalog.Category, monitor.Name);
        if (!MappingInterceptsInput(mapping))
            return ("未設定", "Actionは割り当てられていません");
        string shortText = HasConfiguredShortAction(mapping)
            ? FriendlyActionValue(mapping!.Kind, mapping.Value)
            : "";
        string longText = HasConfiguredLongPress(mapping)
            ? $"長押し: {FriendlyActionValue(mapping!.LongPressKind, mapping.LongPressValue)}"
            : "";
        string value = string.IsNullOrWhiteSpace(shortText) ? longText
            : string.IsNullOrWhiteSpace(longText) ? shortText
            : $"{shortText}  /  {longText}";
        return (ActionKindDisplayName(AssignmentDisplayKind(mapping!)), value);
    }

    void DeckGridScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateDeckManagementViewport();

    void DeckEditorBody_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateDeckEditorBodyLayout(e.NewSize.Width);

    void UpdateDeckEditorBodyLayout(double availableWidth)
    {
        if (availableWidth <= 0 || DeckEditorBody == null)
            return;
        DeckEditorLayoutMode mode;
        if (!deckCustomizeOpen)
            mode = DeckEditorLayoutMode.Closed;
        else
        {
            bool sideDrawer = deckEditorLayoutMode switch
            {
                DeckEditorLayoutMode.DrawerSide => availableWidth >= 700,
                DeckEditorLayoutMode.DrawerStacked => availableWidth >= 760,
                _ => availableWidth >= 740
            };
            mode = sideDrawer ? DeckEditorLayoutMode.DrawerSide : DeckEditorLayoutMode.DrawerStacked;
        }
        deckEditorLayoutMode = mode;
        deckCompactCommandBar = availableWidth < 560;
        DeckCustomizeLabel.Visibility = deckCompactCommandBar ? Visibility.Collapsed : Visibility.Visible;
        DeckCustomizeGlyph.Margin = deckCompactCommandBar ? new Thickness(0) : new Thickness(0, 0, 7, 0);
        DeckCustomizeToggleButton.Width = deckCompactCommandBar ? 40 : double.NaN;
        DeckCustomizeToggleButton.Padding = deckCompactCommandBar ? new Thickness(0) : new Thickness(12, 0, 12, 0);
        DeckOverlayToggleButton.Width = deckCompactCommandBar ? 40 : double.NaN;
        DeckOverlayToggleButton.Padding = deckCompactCommandBar ? new Thickness(0) : new Thickness(12, 0, 12, 0);
        DeckLayoutNameBox.MinWidth = deckCompactCommandBar ? 88 : 110;
        UpdateDeckOverlayPresentationUi();
        ConfigureDeckSettingsSections();
        DeckCustomizeToggleButton.IsChecked = deckCustomizeOpen;
        DeckSettingsPanel.Visibility = deckCustomizeOpen ? Visibility.Visible : Visibility.Collapsed;
        DeckEditorPreviewColumn.Width = new GridLength(1, GridUnitType.Star);
        DeckEditorBodyPrimaryRow.Height = new GridLength(1, GridUnitType.Star);
        DeckEditorPaneDivider.Visibility = Visibility.Collapsed;
        DeckSettingsPanel.MaxHeight = double.PositiveInfinity;
        DeckSettingsPanel.MaxWidth = double.PositiveInfinity;
        DeckSettingsPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        Grid.SetRow(DeckPreviewPane, 0);
        Grid.SetColumn(DeckPreviewPane, 0);

        if (mode == DeckEditorLayoutMode.Closed)
        {
            DeckEditorGapColumn.Width = new GridLength(0);
            DeckEditorSettingsColumn.Width = new GridLength(0);
            DeckEditorBodyGapRow.Height = new GridLength(0);
            DeckEditorBodySecondaryRow.Height = new GridLength(0);
        }
        else if (mode == DeckEditorLayoutMode.DrawerSide)
        {
            double drawerWidth = Math.Clamp(availableWidth * .29, 282, 320);
            DeckEditorGapColumn.Width = new GridLength(12);
            DeckEditorSettingsColumn.Width = new GridLength(drawerWidth);
            DeckEditorBodyGapRow.Height = new GridLength(0);
            DeckEditorBodySecondaryRow.Height = new GridLength(0);
            Grid.SetRow(DeckSettingsPanel, 0);
            Grid.SetColumn(DeckSettingsPanel, 2);
        }
        else
        {
            DeckEditorGapColumn.Width = new GridLength(0);
            DeckEditorSettingsColumn.Width = new GridLength(0);
            DeckEditorBodyPrimaryRow.Height = GridLength.Auto;
            DeckEditorBodyGapRow.Height = new GridLength(12);
            DeckEditorBodySecondaryRow.Height = new GridLength(1, GridUnitType.Star);
            DeckSettingsPanel.MaxHeight = 310;
            DeckSettingsPanel.MaxWidth = 620;
            DeckSettingsPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            Grid.SetRow(DeckSettingsPanel, 0);
            Grid.SetColumn(DeckSettingsPanel, 0);
            Grid.SetRow(DeckPreviewPane, 2);
            Grid.SetColumn(DeckPreviewPane, 0);
        }
        _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(UpdateDeckManagementViewport));
    }

    void ConfigureDeckSettingsSections()
    {
        Grid.SetRow(DeckLayoutSettingsCard, 0);
        Grid.SetRow(DeckCoreSettingsCard, 1);
        Grid.SetRow(DeckAppearanceSettingsCard, 2);
        Grid.SetRow(DeckAutoHideSettingsCard, 3);
        Grid.SetColumn(DeckCoreSettingsCard, 0);
        Grid.SetColumn(DeckLayoutSettingsCard, 0);
        Grid.SetColumn(DeckAppearanceSettingsCard, 0);
        Grid.SetColumn(DeckAutoHideSettingsCard, 0);
        DeckLayoutSettingsCard.Margin = new Thickness(0, 0, 0, 18);
        DeckCoreSettingsCard.Margin = new Thickness(0);
        DeckAppearanceSettingsCard.Margin = new Thickness(0);
        DeckAutoHideSettingsCard.Margin = new Thickness(0);
        DeckSettingsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        DeckBehaviorGapColumn.Width = new GridLength(0);
        DeckBehaviorSecondColumn.Width = new GridLength(0);
        Grid.SetRow(DeckPointerLeaveBehaviorGroup, 1);
        Grid.SetColumn(DeckPointerLeaveBehaviorGroup, 0);
        DeckPointerLeaveBehaviorGroup.Margin = new Thickness(0, 10, 0, 0);
        Grid.SetRow(DeckPinnedBehaviorHint, 2);
        Grid.SetColumn(DeckPinnedBehaviorHint, 0);
        Grid.SetColumnSpan(DeckPinnedBehaviorHint, 1);
        DeckPinnedBehaviorHint.Visibility = Visibility.Visible;
        DeckCustomSizePanel.Margin = new Thickness(0, 14, 0, 0);
        DeckHoverPreviewBox.Margin = new Thickness(0, 12, 0, 0);
        ApplyDeckCustomizeTab();
    }

    void DeckCustomizeToggle_Click(object sender, RoutedEventArgs e)
    {
        deckCustomizeOpen = DeckCustomizeToggleButton.IsChecked == true;
        deckEditorLayoutMode = null;
        UpdateDeckEditorBodyLayout(DeckEditorBody.ActualWidth);
    }

    void DeckCustomizeClose_Click(object sender, RoutedEventArgs e)
        => CloseDeckCustomization();

    void CloseDeckCustomization()
    {
        FlushDeckCustomizationRefresh();
        deckCustomizeOpen = false;
        deckEditorLayoutMode = null;
        UpdateDeckEditorBodyLayout(DeckEditorBody.ActualWidth);
    }

    void DeckCustomizeTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Primitives.ToggleButton { Tag: string tag })
            return;
        selectedDeckCustomizeTab = tag switch
        {
            "Appearance" => DeckCustomizeTab.Appearance,
            "Behavior" => DeckCustomizeTab.Behavior,
            _ => DeckCustomizeTab.Layout
        };
        ApplyDeckCustomizeTab();
    }

    void ApplyDeckCustomizeTab()
    {
        if (DeckLayoutCustomizeTab == null)
            return;
        DeckLayoutCustomizeTab.IsChecked = selectedDeckCustomizeTab == DeckCustomizeTab.Layout;
        DeckAppearanceCustomizeTab.IsChecked = selectedDeckCustomizeTab == DeckCustomizeTab.Appearance;
        DeckBehaviorCustomizeTab.IsChecked = selectedDeckCustomizeTab == DeckCustomizeTab.Behavior;
        DeckCoreSettingsCard.Visibility = selectedDeckCustomizeTab == DeckCustomizeTab.Layout ? Visibility.Visible : Visibility.Collapsed;
        DeckLayoutSettingsCard.Visibility = selectedDeckCustomizeTab == DeckCustomizeTab.Layout ? Visibility.Visible : Visibility.Collapsed;
        DeckAppearanceSettingsCard.Visibility = selectedDeckCustomizeTab == DeckCustomizeTab.Appearance ? Visibility.Visible : Visibility.Collapsed;
        DeckAutoHideSettingsCard.Visibility = selectedDeckCustomizeTab == DeckCustomizeTab.Behavior ? Visibility.Visible : Visibility.Collapsed;
        DeckSettingsScrollViewer.ScrollToTop();
    }

    void DeckViewMode_Click(object sender, RoutedEventArgs e)
    {
        selectedDeckEditorViewMode = sender == DeckListViewToggle ? DeckEditorViewMode.List : DeckEditorViewMode.Grid;
        ApplyDeckEditorViewMode();
    }

    void ApplyDeckEditorViewMode()
    {
        if (DeckGridViewToggle == null)
            return;
        bool list = selectedDeckEditorViewMode == DeckEditorViewMode.List;
        DeckGridViewToggle.IsChecked = !list;
        DeckListViewToggle.IsChecked = list;
        DeckGridScrollViewer.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
        DeckListScrollViewer.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
        ActionPaletteCloseButton.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
        if (list)
        {
            BuildDeckManagementList();
            if (!actionPaletteOpen)
                OpenActionPalette_Click(DeckListViewToggle, new RoutedEventArgs());
        }
        else
        {
            if (actionPaletteOpen)
                CloseActionPalette(animated: false);
            ClearDeckManagementList();
            UpdateDeckManagementViewport();
        }
    }

    bool DeckListActionLibraryPinned => deckManagementMode
        && selectedDeckEditorViewMode == DeckEditorViewMode.List
        && DeckEditorWorkspace?.Visibility == Visibility.Visible;

    void UpdateDeckManagementViewport()
    {
        if (updatingDeckManagementViewport || DeckGridScrollViewer == null || DeckGridScaleTransform == null || DeckManagementGrid == null)
            return;
        double viewportWidth = DeckGridScrollViewer.ViewportWidth;
        double viewportHeight = DeckGridScrollViewer.ViewportHeight;
        if (!double.IsFinite(viewportWidth) || !double.IsFinite(viewportHeight) || viewportWidth <= 32 || viewportHeight <= 32)
            return;
        double requiredScale = Math.Min(1, Math.Min(
            (viewportWidth - 40) / Math.Max(1, DeckManagementGrid.Width),
            (viewportHeight - 40) / Math.Max(1, DeckManagementGrid.Height)));
        // The editor preview is an overview of the complete configured Deck.
        // Keep every outer row and column visible even for 18x18; list view is
        // available when the user needs a larger per-button editing surface.
        // Very short stacked layouts can leave little more than 100 px for the
        // overview. Do not impose a readability floor that clips the final row;
        // list view remains the readable editing alternative.
        const double minimumScale = .02;
        double scale = Math.Clamp(requiredScale, minimumScale, 1);
        if (Math.Abs(DeckGridScaleTransform.ScaleX - scale) < .001)
            return;
        updatingDeckManagementViewport = true;
        try
        {
            DeckGridScaleTransform.ScaleX = scale;
            DeckGridScaleTransform.ScaleY = scale;
        }
        finally { updatingDeckManagementViewport = false; }
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
        => DeckManagementButtonClicked(source, input, ConsumeDeckClickModifiers(source, Keyboard.Modifiers));

    void CaptureDeckClickModifiers(System.Windows.Controls.Button source, ModifierKeys modifiers)
    {
        deckClickModifierSource = source;
        deckClickModifiersAtMouseDown = modifiers & (ModifierKeys.Control | ModifierKeys.Shift);
    }

    ModifierKeys ConsumeDeckClickModifiers(System.Windows.Controls.Button source, ModifierKeys current)
    {
        // WPF raises Button.Click after the synthetic mouse-up has been queued.
        // A generated Shift/Ctrl click may therefore have released its modifier
        // before Click samples Keyboard.Modifiers. Mouse-down is the authoritative
        // state for Windows-style selection, for both physical and mapped clicks.
        bool matches = ReferenceEquals(deckClickModifierSource, source);
        ModifierKeys captured = matches ? deckClickModifiersAtMouseDown : ModifierKeys.None;
        deckClickModifierSource = null;
        deckClickModifiersAtMouseDown = ModifierKeys.None;
        return current | captured;
    }

    void DeckManagementButtonClicked(System.Windows.Controls.Button source, string input, ModifierKeys modifiers)
    {
        bool extendedSelection = (modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0;
        if (extendedSelection)
        {
            string? previousSingleSelection = MultiSelectToggle.IsChecked == true
                || selected == null
                || !DeckPanelLayout.IsInputName(selected.Input)
                    ? null
                    : selected.Input;
            if (MultiSelectToggle.IsChecked != true)
            {
                MultiSelectToggle.IsChecked = true;
                modifierActivatedMultiSelect = true;
                if (!string.IsNullOrWhiteSpace(previousSingleSelection))
                {
                    multiSelectedInputs.Add(previousSingleSelection);
                    multiSelectionAnchorInput = previousSingleSelection;
                }
            }
            ApplyWindowsMultiSelection(input, modifiers, DeckMultiSelectionOrder());
            UpdateMultiSelectControls();
            ColorDeckManagementButtons();
            return;
        }
        if (MultiSelectToggle.IsChecked == true)
        {
            if (modifierActivatedMultiSelect)
                MultiSelectToggle.IsChecked = false;
            else
            {
                if (!multiSelectedInputs.Add(input))
                    multiSelectedInputs.Remove(input);
                multiSelectionAnchorInput = input;
                UpdateMultiSelectControls();
                ColorDeckManagementButtons();
                return;
            }
        }
        // Deck selection mirrors the main keyboard: selecting a slot reveals
        // its editor without forcing the execution field into edit mode. This
        // lets one background click clear the selection instead of spending a
        // first click only completing an implicit edit session.
        if (actionPaletteOpen && !DeckListActionLibraryPinned)
            CloseActionPalette(animated: false);
        multiSelectionAnchorInput = input;
        SelectInput(input, false);
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

    string[] DeckMultiSelectionOrder()
        => deckManagementButtons
            .Select(button => button.Tag as string)
            .Where(input => !string.IsNullOrWhiteSpace(input))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(DeckPanelLayout.SlotNumber)
            .ToArray();
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
        OpenActionPalette_Click(SelectedActionPaletteButton, new RoutedEventArgs());
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
            IsHitTestVisible = false,
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
            IsHitTestVisible = false,
            Child = card
        };
        popup.Opened += (_, _) => NonInteractivePopupSafety.Apply(popup);
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
        if (sender is not System.Windows.Controls.Button { Tag: string input } button || !DeckPanelLayout.IsInputName(input))
            return;
        CaptureDeckClickModifiers(button, Keyboard.Modifiers);
        if (MultiSelectToggle.IsChecked == true && !multiSelectedInputs.Contains(input))
            return;
        deckReorderSource = button;
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
        string[] group = MultiSelectToggle.IsChecked == true && multiSelectedInputs.Contains(input)
            ? multiSelectedInputs
                .Where(DeckPanelLayout.IsInputName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(DeckPanelLayout.SlotNumber)
                .ToArray()
            : [];
        if (group.Length > 1)
            data.SetData(DeckSlotGroupDragFormat, group);
        else
            data.SetData(DeckPanelLayout.SlotDragFormat, input);
        var mapping = DeckPanelLayout.FindMapping(selectedDeckLayout ?? DeckPanelLayout.DefaultLayout(config), DeckPanelLayout.SlotNumber(input));
        string[] registeredFiles = (group.Length > 1 ? group : [input])
            .Select(candidate => DeckPanelLayout.FindMapping(selectedDeckLayout ?? DeckPanelLayout.DefaultLayout(config), DeckPanelLayout.SlotNumber(candidate)))
            .Where(DeckPanelLayout.IsAvailableFile)
            .Select(candidate => candidate!.DeckFilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (registeredFiles.Length > 0)
            data.SetData(System.Windows.DataFormats.FileDrop, registeredFiles);
        try
        {
            deckClickModifierSource = null;
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
            if (button.Tag is string input)
            {
                FrameworkElement content = DeckPanelLayout.CreateButtonContent(input, mapping);
                if (content is TextBlock text)
                    text.Foreground = button.Foreground;
                var face = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(2),
                    Background = button.Background,
                    BorderBrush = button.BorderBrush,
                    BorderThickness = new Thickness(1),
                    Child = new Viewbox { Stretch = Stretch.Uniform, Child = content }
                };
                preview = new DeckDragPreviewWindow(face, compact: true);
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
        if (TryGetPaletteMonitor(e.Data, out _))
        {
            bool validMonitor = selectedDeckLayout != null;
            SetDeckReorderTarget(validMonitor ? (System.Windows.Controls.Button)sender : null);
            e.Effects = validMonitor ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (TryGetPaletteAction(e.Data, out CatalogAction paletteAction))
        {
            bool paletteValid = CanAssignPaletteAction(input, paletteAction);
            SetDeckReorderTarget(paletteValid ? (System.Windows.Controls.Button)sender : null);
            e.Effects = paletteValid ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (TryGetDeckSlotGroup(e.Data, out string[] group))
        {
            var layout = selectedDeckLayout ?? DeckPanelLayout.DefaultLayout(config);
            bool validGroup = layout != null && CanMoveDeckSlotsAsBlock(group, input, DeckPanelLayout.VisibleSlotCount(layout));
            SetDeckReorderTarget(validGroup ? (System.Windows.Controls.Button)sender : null);
            e.Effects = validGroup ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }
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
        SetAssignmentDropTargetVisual(target, true);
        deckReorderTarget = target;
    }
    void ClearDeckReorderTarget()
    {
        if (deckReorderTarget == null)
            return;
        var target = deckReorderTarget;
        deckReorderTarget = null;
        SetAssignmentDropTargetVisual(target, false);
        UpdateDeckManagementButtonVisual(target);
    }
    void DeckButtonDropped(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string target } || !DeckPanelLayout.IsInputName(target))
            return;
        ClearDeckReorderTarget();
        if (TryGetPaletteMonitor(e.Data, out DeckMonitorDefinition monitor))
        {
            bool applied = ApplyPaletteMonitorDrop(monitor, target);
            e.Effects = applied ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (TryGetPaletteAction(e.Data, out CatalogAction paletteAction))
        {
            bool applied = CanAssignPaletteAction(target, paletteAction)
                && ApplyPaletteActionDrop(paletteAction, target, target);
            e.Effects = applied ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (TryGetDeckSlotGroup(e.Data, out string[] group))
        {
            var layout = selectedDeckLayout ?? DeckPanelLayout.DefaultLayout(config);
            string[] movedInputs = [];
            bool moved = layout != null && MoveDeckSlotsAsBlock(layout, group, target, DeckPanelLayout.VisibleSlotCount(layout), out movedInputs);
            if (moved)
            {
                multiSelectedInputs.Clear();
                foreach (string movedInput in movedInputs)
                    multiSelectedInputs.Add(movedInput);
                multiSelectionAnchorInput = movedInputs.FirstOrDefault();
                modifierActivatedMultiSelect = false;
                BuildDeckManagementPanel();
                UpdateMultiSelectControls();
                MarkDirty();
            }
            e.Effects = moved ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }
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

    static bool TryGetDeckSlotGroup(System.Windows.IDataObject data, out string[] inputs)
    {
        inputs = [];
        try
        {
            if (!data.GetDataPresent(DeckSlotGroupDragFormat) || data.GetData(DeckSlotGroupDragFormat) is not string[] value)
                return false;
            inputs = value;
            return inputs.Length > 1;
        }
        catch { return false; }
    }

    internal static bool CanMoveDeckSlotsAsBlock(IReadOnlyCollection<string> sourceInputs, string targetInput, int visibleSlotCount)
    {
        int[] sourceSlots = sourceInputs
            .Where(DeckPanelLayout.IsInputName)
            .Select(DeckPanelLayout.SlotNumber)
            .Where(slot => slot >= 1 && slot <= visibleSlotCount)
            .Distinct()
            .Order()
            .ToArray();
        int targetSlot = DeckPanelLayout.SlotNumber(targetInput);
        if (sourceSlots.Length < 2 || targetSlot < 1 || targetSlot + sourceSlots.Length - 1 > visibleSlotCount)
            return false;
        return !sourceSlots.SequenceEqual(Enumerable.Range(targetSlot, sourceSlots.Length));
    }

    internal static bool MoveDeckSlotsAsBlock(
        DeckLayoutDefinition layout,
        IReadOnlyCollection<string> sourceInputs,
        string targetInput,
        int visibleSlotCount,
        out string[] movedInputs)
    {
        movedInputs = [];
        if (!CanMoveDeckSlotsAsBlock(sourceInputs, targetInput, visibleSlotCount))
            return false;

        int[] sourceSlots = sourceInputs
            .Where(DeckPanelLayout.IsInputName)
            .Select(DeckPanelLayout.SlotNumber)
            .Where(slot => slot >= 1 && slot <= visibleSlotCount)
            .Distinct()
            .Order()
            .ToArray();
        int targetSlot = DeckPanelLayout.SlotNumber(targetInput);
        int[] targetSlots = Enumerable.Range(targetSlot, sourceSlots.Length).ToArray();
        var sourceSet = sourceSlots.ToHashSet();
        var targetSet = targetSlots.ToHashSet();
        int[] sourceOnly = sourceSlots.Where(slot => !targetSet.Contains(slot)).ToArray();
        int[] targetOnly = targetSlots.Where(slot => !sourceSet.Contains(slot)).ToArray();

        var sourceMappings = sourceSlots.ToDictionary(
            slot => slot,
            slot => layout.Mappings.Where(mapping => mapping.Input.Equals(DeckPanelLayout.InputName(slot), StringComparison.OrdinalIgnoreCase)).ToArray());
        var displacedMappings = targetOnly.ToDictionary(
            slot => slot,
            slot => layout.Mappings.Where(mapping => mapping.Input.Equals(DeckPanelLayout.InputName(slot), StringComparison.OrdinalIgnoreCase)).ToArray());

        for (int index = 0; index < sourceSlots.Length; index++)
            MoveDeckMappingGroup(sourceMappings[sourceSlots[index]], targetSlots[index]);
        for (int index = 0; index < targetOnly.Length; index++)
            MoveDeckMappingGroup(displacedMappings[targetOnly[index]], sourceOnly[index]);

        movedInputs = targetSlots.Select(DeckPanelLayout.InputName).ToArray();
        return true;
    }

    static void MoveDeckMappingGroup(IEnumerable<Mapping> mappings, int targetSlot)
    {
        string targetInput = DeckPanelLayout.InputName(targetSlot);
        foreach (Mapping mapping in mappings)
        {
            mapping.Input = targetInput;
            mapping.Layer = DeckPanelLayout.Layer;
        }
    }
    void OpenDeckPanelManager_Click(object sender, RoutedEventArgs e)
    {
        deckManagementMode = true;
        currentLayer = DeckPanelLayout.Layer;
        KeyboardWorkspace.Visibility = Visibility.Collapsed;
        DeckWorkspace.Visibility = Visibility.Visible;
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
        selectedDeckEditorViewMode = DeckEditorViewMode.Grid;
        ActionPaletteCloseButton.Visibility = Visibility.Visible;
        KeyboardWorkspace.Visibility = Visibility.Visible;
        DeckWorkspace.Visibility = Visibility.Collapsed;
        DeckLayoutListWorkspace.Visibility = Visibility.Visible;
        DeckEditorWorkspace.Visibility = Visibility.Collapsed;
        if (actionPaletteOpen)
            CloseActionPalette(animated: false);
        ToolbarSaveButton.Visibility = Visibility.Visible;
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
        selectedDeckEditorViewMode = DeckEditorViewMode.Grid;
        ActionPaletteCloseButton.Visibility = Visibility.Visible;
        DeckLayoutListWorkspace.Visibility = Visibility.Visible;
        DeckEditorWorkspace.Visibility = Visibility.Collapsed;
        if (actionPaletteOpen)
            CloseActionPalette(animated: false);
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
            Height = 164,
            Margin = new Thickness(0, 0, 18, 18),
            Padding = new Thickness(14),
            Tag = "NewDeckLayout",
            ToolTip = "新しいDeckレイアウトを作成",
            Content = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { new TextBlock { Text = "＋", FontSize = 26, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Foreground = ThemeService.Brush("SecondaryText") }, new TextBlock { Text = "新規レイアウト", FontSize = 14, FontWeight = FontWeights.Medium, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Center } } },
            Background = WpfBrushes.Transparent,
            BorderBrush = WpfBrushes.Transparent,
            BorderThickness = new Thickness(0)
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
        content.Children.Add(new Grid { Height = 72, Margin = new Thickness(0, 0, 0, 8), Children = { preview } });
        content.Children.Add(new TextBlock { Tag = "DeckLayoutName", Text = layout.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        content.Children.Add(new TextBlock { Text = $"{layout.Columns}×{layout.Rows}・{DeckPanelLayout.VisibleSlotCount(layout)}ボタン" + (isDefault ? "  ・  既定" : ""), FontSize = 11, Margin = new Thickness(0, 4, 0, 0), Foreground = ThemeService.Brush(isDefault ? "AccentTextBrush" : "SecondaryText") });
        var card = new System.Windows.Controls.Button { Tag = layout, Content = content, Width = 236, Height = 164, Margin = new Thickness(0, 0, 18, 18), Padding = new Thickness(14), HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, BorderBrush = WpfBrushes.Transparent, BorderThickness = new Thickness(0), ToolTip = $"{layout.Name}を編集" };
        if (isDefault)
            card.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AccentSoftBrush");
        else
            card.Background = WpfBrushes.Transparent;
        card.Click += (_, _) => EditDeckLayout(layout);
        var menu = new ContextMenu();
        var toggleOverlay = new MenuItem { Header = "オーバーレイを表示／非表示" };
        toggleOverlay.Click += (_, _) => OverlayService.TryShow(DeckPanelLayout.ActionValue(layout.Id));
        var makeDefault = new MenuItem { Header = "既定のDeckにする", IsEnabled = !isDefault };
        makeDefault.Click += (_, _) => SetDefaultDeckLayout(layout);
        var duplicate = new MenuItem { Header = "複製" };
        duplicate.Click += (_, _) => DuplicateDeckLayout(layout);
        var delete = new MenuItem { Header = "削除" };
        delete.Click += (_, _) => DeleteDeckLayout(layout);
        menu.Items.Add(toggleOverlay);
        menu.Items.Add(new Separator());
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
        sizes.Items.Add(new ComboBoxItem { Content = "小  3×3（9ボタン）", Tag = "3x3" });
        sizes.Items.Add(new ComboBoxItem { Content = "中  6×4（24ボタン）", Tag = "6x4" });
        sizes.Items.Add(new ComboBoxItem { Content = "大  9×5（45ボタン）", Tag = "9x5" });
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
            "6x4" => (6, 4),
            "9x5" => (9, 5),
            // Saved Decks persist dimensions, not preset tags. Keep the former
            // wide tag readable while new UI presets use a consistent scale.
            _ when preset.Equals("8x2", StringComparison.OrdinalIgnoreCase) => (8, 2),
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
        ToolbarSaveButton.Visibility = Visibility.Visible;
        deckCustomizeOpen = false;
        selectedDeckCustomizeTab = DeckCustomizeTab.Layout;
        selectedDeckEditorViewMode = DeckEditorViewMode.Grid;
        deckEditorLayoutMode = null;
        updatingDeckEditor = true;
        DeckLayoutNameBox.Text = layout.Name;
        DeckProfileSwitchBox.IsChecked = layout.ProfileSwitchEnabled;
        DeckColumnsBox.Text = layout.Columns.ToString();
        DeckRowsBox.Text = layout.Rows.ToString();
        DeckColumnsSlider.Value = layout.Columns;
        DeckRowsSlider.Value = layout.Rows;
        DeckPanelPaddingSlider.Value = layout.PanelPadding;
        DeckPanelCornerRadiusSlider.Value = layout.PanelCornerRadius;
        DeckPanelPaddingValueText.Text = $"{Math.Round(layout.PanelPadding):0} px";
        DeckPanelCornerRadiusValueText.Text = $"{Math.Round(layout.PanelCornerRadius):0} px";
        DeckOpacitySlider.Value = config.DeckChromeOpacityPercent;
        DeckOpacityValueText.Text = config.DeckChromeOpacityPercent + "%";
        DeckHoverAnimationBox.IsChecked = layout.HoverAnimationEnabled;
        DeckHoverPreviewBox.IsChecked = config.DeckHoverPreviewsEnabled;
        SelectDeckAutoDismissBehavior(DeckAfterActionBehaviorBox, config.DeckAfterActionBehavior);
        SelectDeckAutoDismissBehavior(DeckPointerLeaveBehaviorBox, config.DeckPointerLeaveBehavior);
        DeckSizePresetBox.Style = (Style)FindResource("ToolbarComboBoxStyle");
        DeckSizePresetBox.Height = 36;
        UpdateDeckPanelColorEditor();
        string preset = DeckPresetForSize(layout.Columns, layout.Rows);
        DeckSizePresetBox.SelectedItem = DeckSizePresetBox.Items.Cast<ComboBoxItem>().First(x => Equals(x.Tag, preset));
        DeckCustomSizePanel.Visibility = Visibility.Visible;
        UpdateDeckSizeSegmentSelection(preset);
        updatingDeckEditor = false;
        UpdateDeckScopeUi();
        BuildDeckManagementPanel();
        ClearSelectedInput();
        UpdateDeckEditorBodyLayout(DeckEditorBody.ActualWidth);
        WorkspaceSubtitle.Text = $"列数 {layout.Columns}・行数 {layout.Rows}・{DeckPanelLayout.VisibleSlotCount(layout)}ボタン";
        ColorButtons();
        UpdateDeckOverlayPresentationUi();
        UpdateDeckSaveStatus(saved: true);
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
    void DeckOverlayToggle_Click(object sender, RoutedEventArgs e)
    {
        if (selectedDeckLayout != null)
            OverlayService.TryShow(DeckPanelLayout.ActionValue(selectedDeckLayout.Id));
    }

    internal void HandleDeckOverlayPresentationChanged()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(UpdateDeckOverlayPresentationUi);
            return;
        }
        UpdateDeckOverlayPresentationUi();
    }

    void UpdateDeckOverlayPresentationUi()
    {
        if (DeckOverlayToggleButton == null || selectedDeckLayout == null)
            return;
        var state = OverlayService.DeckPanelPresentationState(DeckPanelLayout.ActionValue(selectedDeckLayout.Id));
        bool visible = state != OverlayService.DeckPresentationState.Hidden;
        DeckOverlayToggleButton.Content = deckCompactCommandBar
            ? new TextBlock { Text = "\uE890", FontFamily = new System.Windows.Media.FontFamily("Segoe Fluent Icons"), FontSize = 14 }
            : visible ? "Deckを非表示" : "Deckを表示";
        System.Windows.Automation.AutomationProperties.SetName(DeckOverlayToggleButton, visible ? "Deckを非表示" : "Deckを表示");
        DeckOverlayToggleButton.ToolTip = state switch
        {
            OverlayService.DeckPresentationState.Collapsed => "画面端に折りたたまれています。クリックすると完全に非表示にします",
            OverlayService.DeckPresentationState.Maximized => "最大化されています。クリックすると完全に非表示にします",
            OverlayService.DeckPresentationState.Visible => "表示中のDeckを完全に非表示にします",
            _ => "このDeckを実際のオーバーレイとして表示します"
        };
    }

    void UpdateDeckSaveStatus(bool saved)
    {
        if (DeckSaveStatusText == null)
            return;
        DeckSaveStatusText.Text = saved ? "保存済み" : config.AutoSave ? "自動保存待ち" : "未保存";
        DeckSaveStatusText.Foreground = ThemeService.Brush(saved ? "MutedText" : "WarningBrush");
        DeckSaveStatusText.Visibility = saved || deckCompactCommandBar ? Visibility.Collapsed : Visibility.Visible;
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
    void DeckTitleEditButton_Click(object sender, RoutedEventArgs e)
    {
        FocusManager.SetFocusedElement(FocusManager.GetFocusScope(DeckLayoutNameBox), DeckLayoutNameBox);
        DeckLayoutNameBox.Focus();
        DeckLayoutNameBox.SelectAll();
        e.Handled = true;
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
        PanelPadding = source.PanelPadding,
        PanelCornerRadius = source.PanelCornerRadius,
        HoverAnimationEnabled = source.HoverAnimationEnabled,
        PanelPinned = source.PanelPinned,
        PanelWidth = source.PanelWidth,
        PanelHeight = source.PanelHeight,
        PanelLeft = source.PanelLeft,
        PanelTop = source.PanelTop,
        PanelCollapsedLeft = source.PanelCollapsedLeft,
        PanelCollapsedTop = source.PanelCollapsedTop,
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
        DeckCustomSizePanel.Visibility = Visibility.Visible;
        UpdateDeckSizeSegmentSelection(tag);
        if (tag == "custom")
            return;
        var size = tag switch
        {
            "3x3" => (3, 3),
            "6x4" => (6, 4),
            _ => (9, 5)
        };
        ApplyDeckSize(size.Item1, size.Item2);
    }
    void DeckSizeSegment_Click(object sender, RoutedEventArgs e)
    {
        if (updatingDeckEditor || selectedDeckLayout == null || sender is not System.Windows.Controls.Primitives.ToggleButton { Tag: string tag })
            return;
        var size = tag switch
        {
            "3x3" => (3, 3),
            "6x4" => (6, 4),
            _ => (9, 5)
        };
        ApplyDeckSize(size.Item1, size.Item2);
    }
    void UpdateDeckSizeSegmentSelection(string preset)
    {
        DeckSmallSizeToggle.IsChecked = preset == "3x3";
        DeckMediumSizeToggle.IsChecked = preset == "6x4";
        DeckLargeSizeToggle.IsChecked = preset == "9x5";
    }
    internal static string DeckPresetForSize(int columns, int rows)
        => (columns, rows) switch
        {
            (3, 3) => "3x3",
            (6, 4) => "6x4",
            (9, 5) => "9x5",
            _ => "custom"
        };
    void DeckDimensionSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (updatingDeckEditor || selectedDeckLayout == null || DeckColumnsSlider == null || DeckRowsSlider == null)
            return;
        // Do not write back to either Slider while its Thumb owns the mouse.
        // Rebuilding and resynchronizing the complete editor here used to make
        // the Thumb lag behind the pointer and occasionally jump to an older
        // value. Resize only the changed tail of the preview and defer the live
        // overlay rebuild until the drag pauses or ends.
        ApplyDeckSize(
            (int)Math.Round(DeckColumnsSlider.Value),
            (int)Math.Round(DeckRowsSlider.Value),
            synchronizeSliders: false,
            deferDeckRefresh: true);
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
    void ApplyDeckSize(int columns, int rows, bool updateBoxes = true, bool synchronizeSliders = true, bool deferDeckRefresh = false)
    {
        if (selectedDeckLayout == null)
            return;
        columns = Math.Clamp(columns, 1, DeckPanelLayout.MaximumColumns);
        rows = Math.Clamp(rows, 1, DeckPanelLayout.MaximumRows);
        int previousColumns = selectedDeckLayout.Columns;
        int previousRows = selectedDeckLayout.Rows;
        bool dimensionsChanged = previousColumns != columns || previousRows != rows;
        selectedDeckLayout.Columns = columns;
        selectedDeckLayout.Rows = rows;
        selectedDeckLayout.PanelWidth = null;
        selectedDeckLayout.PanelHeight = null;
        if (updateBoxes)
        {
            updatingDeckEditor = true;
            DeckColumnsBox.Text = columns.ToString();
            DeckRowsBox.Text = rows.ToString();
            if (synchronizeSliders)
            {
                DeckColumnsSlider.Value = columns;
                DeckRowsSlider.Value = rows;
            }
            string preset = DeckPresetForSize(columns, rows);
            DeckSizePresetBox.SelectedItem = DeckSizePresetBox.Items.Cast<ComboBoxItem>().First(x => Equals(x.Tag, preset));
            UpdateDeckSizeSegmentSelection(preset);
            updatingDeckEditor = false;
        }
        if (dimensionsChanged)
        {
            int visibleCount = columns * rows;
            if (selected?.Input is string selectedInput
                && DeckPanelLayout.IsInputName(selectedInput)
                && DeckPanelLayout.SlotNumber(selectedInput) > visibleCount)
                ClearSelectedInput();
            multiSelectedInputs.RemoveWhere(input => DeckPanelLayout.IsInputName(input) && DeckPanelLayout.SlotNumber(input) > visibleCount);
            ResizeDeckManagementPanel(columns, rows, deferDeckRefresh);
        }
        WorkspaceSubtitle.Text = $"列数 {columns}・行数 {rows}・{columns * rows}ボタン";
        MarkDirty(refreshDeckPanel: false);
        if (deferDeckRefresh)
            QueueDeckCustomizationRefresh();
        else
            OverlayService.RefreshDeckPanel();
    }
    void DeckPanelPaddingChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DeckPanelPaddingValueText == null)
            return;
        double value = Math.Round(e.NewValue);
        DeckPanelPaddingValueText.Text = $"{value:0} px";
        if (updatingDeckEditor || selectedDeckLayout == null)
            return;
        selectedDeckLayout.PanelPadding = value;
        selectedDeckLayout.PanelWidth = null;
        selectedDeckLayout.PanelHeight = null;
        MarkDirty(refreshDeckPanel: false);
        QueueDeckCustomizationRefresh();
    }
    void DeckPanelCornerRadiusChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DeckPanelCornerRadiusValueText == null)
            return;
        double value = Math.Round(e.NewValue);
        DeckPanelCornerRadiusValueText.Text = $"{value:0} px";
        if (updatingDeckEditor || selectedDeckLayout == null)
            return;
        selectedDeckLayout.PanelCornerRadius = value;
        MarkDirty(refreshDeckPanel: false);
        QueueDeckCustomizationRefresh();
    }

    void QueueDeckCustomizationRefresh()
    {
        deckCustomizationRefreshPending = true;
        // Render the latest layout once per WPF frame. The overlay path updates
        // dimensions/appearance and only the added or removed tail of cells.
        OverlayService.RefreshDeckPanelLayoutPreview();
        deckOverlayVisualSynchronized = true;
        if (deckCustomizationSliderDragging)
            autoSaveTimer.Stop();
        if (!deckCustomizationRefreshTimerHooked)
        {
            deckCustomizationRefreshTimer.Tick += (_, _) => FlushDeckCustomizationRefresh();
            deckCustomizationRefreshTimerHooked = true;
        }
        deckCustomizationRefreshTimer.Stop();
        deckCustomizationRefreshTimer.Start();
    }

    void FlushDeckCustomizationRefresh()
    {
        deckCustomizationRefreshTimer.Stop();
        if (deckManagementListRefreshPending)
        {
            deckManagementListRefreshPending = false;
            if (selectedDeckEditorViewMode == DeckEditorViewMode.List)
                BuildDeckManagementList();
        }
        if (!deckCustomizationRefreshPending)
            return;
        deckCustomizationRefreshPending = false;
    }

    void DeckCustomizationSliderReleased(object sender, MouseButtonEventArgs e)
        => CompleteDeckCustomizationSliderInteraction();

    void DeckCustomizationSliderPressed(object sender, MouseButtonEventArgs e)
    {
        BeginEditorHistoryTransaction();
        deckCustomizationSliderDragging = true;
        autoSaveTimer.Stop();
    }

    void DeckCustomizationSliderLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
        => CompleteDeckCustomizationSliderInteraction();

    void CompleteDeckCustomizationSliderInteraction()
    {
        if (!deckCustomizationSliderDragging)
            return;
        deckCustomizationSliderDragging = false;
        CompleteEditorHistoryTransaction();
        FlushDeckCustomizationRefresh();
        if (config.AutoSave)
        {
            autoSaveTimer.Stop();
            autoSaveTimer.Start();
        }
    }
    void DeckHoverAnimationChanged(object sender, RoutedEventArgs e)
    {
        if (updatingDeckEditor || selectedDeckLayout == null)
            return;
        selectedDeckLayout.HoverAnimationEnabled = DeckHoverAnimationBox.IsChecked == true;
        MarkDirty(refreshDeckPanel: false);
        OverlayService.RefreshDeckPanelLayoutPreview();
        deckOverlayVisualSynchronized = true;
    }
    void DeckCustomizationReset_Click(object sender, RoutedEventArgs e)
    {
        if (selectedDeckLayout == null)
            return;
        updatingDeckEditor = true;
        selectedDeckLayout.PanelColor = "";
        selectedDeckLayout.PanelPadding = 12;
        selectedDeckLayout.PanelCornerRadius = 14;
        selectedDeckLayout.HoverAnimationEnabled = true;
        selectedDeckLayout.PanelWidth = null;
        selectedDeckLayout.PanelHeight = null;
        config.DeckChromeOpacityPercent = 100;
        DeckPanelPaddingSlider.Value = 12;
        DeckPanelCornerRadiusSlider.Value = 14;
        DeckPanelPaddingValueText.Text = "12 px";
        DeckPanelCornerRadiusValueText.Text = "14 px";
        DeckOpacitySlider.Value = 100;
        DeckOpacityValueText.Text = "100%";
        DeckHoverAnimationBox.IsChecked = true;
        updatingDeckEditor = false;
        UpdateDeckPanelColorEditor();
        ApplyDeckSize(9, 5);
    }
    void DeckOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (DeckOpacityValueText == null)
            return;
        int value = (int)Math.Round(e.NewValue);
        DeckOpacityValueText.Text = value + "%";
        if (updatingDeckEditor || config == null)
            return;
        if (config.DeckChromeOpacityPercent == value)
            return;
        config.DeckChromeOpacityPercent = value;
        // Opacity is a presentation-only change. A full Deck refresh rebuilds
        // every button and makes the Thumb visibly stall while it is dragged.
        // Reuse the frame-coalesced lightweight appearance path instead.
        MarkDirty(refreshDeckPanel: false);
        QueueDeckCustomizationRefresh();
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
        var copy = new DeckLayoutDefinition { Name = name, Columns = source.Columns, Rows = source.Rows, PanelColor = source.PanelColor, PanelPadding = source.PanelPadding, PanelCornerRadius = source.PanelCornerRadius, HoverAnimationEnabled = source.HoverAnimationEnabled, PanelWidth = source.PanelWidth, PanelHeight = source.PanelHeight, Mappings = [.. source.Mappings.Select(CloneMapping)] };
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
    void PersistDeckPanelPosition(string layoutId, double left, double top)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top))
            return;
        static void Apply(IEnumerable<DeckLayoutDefinition> layouts, string id, double x, double y)
        {
            var target = layouts.FirstOrDefault(layout => layout.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                target.PanelLeft = x;
                target.PanelTop = y;
            }
        }
        Apply(config.DeckLayouts, layoutId, left, top);
        Apply(appliedConfig.DeckLayouts, layoutId, left, top);
        try
        {
            var persisted = store.Load();
            Apply(persisted.DeckLayouts, layoutId, left, top);
            store.Save(persisted);
        }
        catch { }
    }
    void PersistDeckPanelCollapsedPosition(string layoutId, double left, double top)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top))
            return;
        static void Apply(IEnumerable<DeckLayoutDefinition> layouts, string id, double x, double y)
        {
            var target = layouts.FirstOrDefault(layout => layout.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                target.PanelCollapsedLeft = x;
                target.PanelCollapsedTop = y;
            }
        }
        Apply(config.DeckLayouts, layoutId, left, top);
        Apply(appliedConfig.DeckLayouts, layoutId, left, top);
        try
        {
            var persisted = store.Load();
            Apply(persisted.DeckLayouts, layoutId, left, top);
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
        if (longPress && (selected == null || !IsLongPressSupportedFor(selected, MappingCollectionForInput(selected.Input))))
            return;
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
        // The overlay already updated exactly these live cells. Mark that
        // visual state as synchronized so auto-save cannot follow the
        // differential edit with a redundant full Deck rebuild.
        deckOverlayVisualSynchronized = true;
        if (!deckManagementMode || DeckEditorWorkspace.Visibility != Visibility.Visible || selectedDeckLayout?.Id != layoutId)
            return;
        AnimateDeckManagementSwap(firstSlot, secondSlot);
    }
    void AnimateDeckManagementSwap(int firstSlot, int secondSlot)
        => UiMotionService.RunSafely("deck-swap", () => AnimateDeckManagementSwapCore(firstSlot, secondSlot));

    void AnimateDeckManagementSwapCore(int firstSlot, int secondSlot)
    {
        string firstInput = DeckPanelLayout.InputName(firstSlot), secondInput = DeckPanelLayout.InputName(secondSlot);
        var first = deckManagementButtons.FirstOrDefault(x => x.Tag is string tag && tag.Equals(firstInput, StringComparison.OrdinalIgnoreCase));
        var second = deckManagementButtons.FirstOrDefault(x => x.Tag is string tag && tag.Equals(secondInput, StringComparison.OrdinalIgnoreCase));
        if (first == null || second == null || firstSlot == secondSlot || !UiMotionService.Enabled)
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
