using System.IO;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Button = System.Windows.Controls.Button;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;
using WpfApplication = System.Windows.Application;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfCursors = System.Windows.Input.Cursors;
using WpfImage = System.Windows.Controls.Image;
using WpfSize = System.Windows.Size;

namespace RELYR;

internal sealed partial class DeckPanelOverlayWindow
{
    System.Windows.Controls.ContextMenu CreateDeckButtonContextMenu(int slot)
    {
        var menu = new System.Windows.Controls.ContextMenu { MinWidth = 242 };
        var timerOne = CreateDeckContextMenuItem("\uE823", "1分", "");
        timerOne.Click += (_, _) => StartDeckTimer(TimeSpan.FromMinutes(1));
        var timerThree = CreateDeckContextMenuItem("\uE823", "3分", "");
        timerThree.Click += (_, _) => StartDeckTimer(TimeSpan.FromMinutes(3));
        var timerTen = CreateDeckContextMenuItem("\uE823", "10分", "");
        timerTen.Click += (_, _) => StartDeckTimer(TimeSpan.FromMinutes(10));
        var timerThirty = CreateDeckContextMenuItem("\uE823", "30分", "");
        timerThirty.Click += (_, _) => StartDeckTimer(TimeSpan.FromMinutes(30));
        var timerCustom = CreateDeckContextMenuItem("\uE787", "任意...", "");
        timerCustom.Click += (_, _) => PromptAndStartDeckTimer();
        var timerCancel = CreateDeckContextMenuItem("\uE711", "タイマーを停止", "", true);
        timerCancel.Click += (_, _) => DeckTimerService.Shared.Cancel();
        var timerSeparator = new Separator();
        FrameworkElement[] timerControls = [timerOne, timerThree, timerTen, timerThirty, timerCustom, timerCancel, timerSeparator];
        foreach (FrameworkElement control in timerControls)
        {
            control.Visibility = Visibility.Collapsed;
            menu.Items.Add(control);
        }
        var rename = CreateDeckContextMenuItem("\uE70F", "名前の変更...", "");
        rename.Click += (_, _) => RenameDeckButton(slot);
        var copy = CreateDeckContextMenuItem("\uE8C8", "コピー", "");
        copy.Click += (_, _) => CopyDeckFile(slot);
        var paste = CreateDeckContextMenuItem("\uE77F", "貼り付け", "");
        paste.Click += (_, _) => PasteDeckFile(slot);
        var reveal = CreateDeckContextMenuItem("\uE838", "ファイルの場所を開く", "");
        reveal.Click += (_, _) => RevealDeckFile(slot);
        var color = CreateDeckContextMenuItem("\uE790", "色を変更...", "");
        color.Click += (_, _) => ChooseDeckButtonColor(slot);
        var icon = CreateDeckContextMenuItem("\uE8B9", "アイコン変更...", "");
        icon.Click += (_, _) => ChooseDeckButtonIcon(slot);
        var resetColor = CreateDeckContextMenuItem("\uE777", "色を標準に戻す", "");
        resetColor.Click += (_, _) => ResetDeckButtonColor(slot);
        var delete = CreateDeckContextMenuItem("\uE74D", "削除", "Del", true);
        delete.Click += (_, _) => DeleteDeckButton(slot);
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
            var mapping = DeckPanelLayout.FindMapping(layout, slot);
            bool timerMonitor = string.Equals(mapping?.DeckMonitor, "timer", StringComparison.OrdinalIgnoreCase);
            bool timerRunning = DeckTimerService.Shared.Snapshot().IsRunning;
            foreach (FrameworkElement control in timerControls)
                control.Visibility = timerMonitor ? Visibility.Visible : Visibility.Collapsed;
            timerCancel.Visibility = timerMonitor && timerRunning ? Visibility.Visible : Visibility.Collapsed;
            copy.IsEnabled = DeckPanelLayout.IsAvailableFile(mapping);
            paste.IsEnabled = ClipboardFile() != null;
            reveal.IsEnabled = DeckPanelLayout.IsAvailableFile(mapping);
            resetColor.IsEnabled = DeckPanelLayout.TryGetButtonColor(mapping, out _);
            delete.IsEnabled = mapping != null;
        };
        TrackContextMenu(menu);
        return menu;
    }

    static void StartDeckTimer(TimeSpan duration)
        => DeckTimerService.Shared.Start(duration);

    void PromptAndStartDeckTimer()
    {
        DeckTimerSnapshot snapshot = DeckTimerService.Shared.Snapshot();
        double initialMinutes = snapshot.Duration.TotalMinutes is >= 1 and <= 1440
            ? snapshot.Duration.TotalMinutes
            : 5;
        var dialog = new Window
        {
            Title = "タイマー",
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 400,
            Height = 228,
            ResizeMode = ResizeMode.NoResize,
            Background = ThemeService.Brush("SurfaceBackground"),
            Foreground = ThemeService.Brush("PrimaryText"),
            ShowInTaskbar = false,
            Topmost = true
        };
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock
        {
            Text = "時間（分）",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        var box = new System.Windows.Controls.TextBox
        {
            Text = initialMinutes.ToString("0.#", CultureInfo.CurrentCulture),
            FontSize = 15,
            Height = 40,
            Padding = new Thickness(10, 7, 10, 7),
            MaxLength = 6,
            Background = ThemeService.Brush("InputBackground"),
            Foreground = ThemeService.Brush("PrimaryText"),
            BorderBrush = ThemeService.Brush("BorderBrush")
        };
        panel.Children.Add(box);
        var validation = new TextBlock
        {
            Text = "1〜1440分で指定",
            FontSize = 11,
            Margin = new Thickness(1, 5, 0, 0),
            Foreground = ThemeService.Brush("SecondaryText")
        };
        panel.Children.Add(validation);
        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var cancel = new Button
        {
            Content = "キャンセル",
            Width = 98,
            Height = 40,
            Margin = new Thickness(6, 0, 0, 0),
            Style = (Style)WpfApplication.Current.FindResource("AppButtonStyle")
        };
        var start = new Button
        {
            Content = "開始",
            Width = 98,
            Height = 40,
            Margin = new Thickness(6, 0, 0, 0),
            Style = (Style)WpfApplication.Current.FindResource("AccentAppButtonStyle"),
            IsDefault = true
        };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        start.Click += (_, _) =>
        {
            if (!TryParseTimerMinutes(box.Text, out double minutes))
            {
                validation.Text = "1〜1440分の数値を入力してください";
                validation.Foreground = ThemeService.Brush("DangerBrush");
                box.Focus();
                box.SelectAll();
                return;
            }
            dialog.Tag = TimeSpan.FromMinutes(minutes);
            dialog.DialogResult = true;
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(start);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        if (dialog.ShowDialog() == true && dialog.Tag is TimeSpan requested)
            StartDeckTimer(requested);
    }

    internal static bool TryParseTimerMinutes(string? text, out double minutes)
    {
        bool parsed = double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out minutes)
            || double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out minutes);
        return parsed && double.IsFinite(minutes) && minutes is >= 1 and <= 1440;
    }
    static System.Windows.Controls.MenuItem CreateDeckContextMenuItem(string icon, string label, string shortcut, bool danger = false)
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
        return new System.Windows.Controls.MenuItem { Header = header, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 1, 0, 1), Foreground = danger ? ThemeService.Brush("DangerBrush") : ThemeService.Brush("PrimaryText") };
    }
    Mapping GetOrCreateDeckMapping(int slot)
    {
        var mapping = DeckPanelLayout.FindMapping(layout, slot);
        if (mapping != null)
            return mapping;
        mapping = new Mapping { Input = DeckPanelLayout.InputName(slot), Layer = DeckPanelLayout.Layer };
        layout.Mappings.Add(mapping);
        return mapping;
    }
    static bool HasDeckButtonContent(Mapping mapping)
        => MainWindow.MappingHasConfiguredAction(mapping) || DeckMonitorCatalog.IsMonitor(mapping.DeckMonitor) || !string.IsNullOrWhiteSpace(mapping.Description) || !string.IsNullOrWhiteSpace(mapping.DeckColor) || DeckPanelLayout.HasRegisteredFile(mapping) || DeckIconCatalog.HasIcon(mapping);
    void RenameDeckButton(int slot)
    {
        var existing = DeckPanelLayout.FindMapping(layout, slot);
        string? name = PromptDeckButtonName(existing?.Description ?? "");
        if (name == null || (existing == null && name.Length == 0))
            return;
        var mapping = existing ?? GetOrCreateDeckMapping(slot);
        mapping.Description = name;
        if (!HasDeckButtonContent(mapping))
            layout.Mappings.Remove(mapping);
        CommitDeckSlotChange(slot);
    }
    string? PromptDeckButtonName(string initial)
    {
        var dialog = new Window { Title = "Deckボタン名", Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Width = 420, Height = 196, ResizeMode = ResizeMode.NoResize, Background = ThemeService.Brush("SurfaceBackground"), Foreground = ThemeService.Brush("PrimaryText"), ShowInTaskbar = false };
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = "ボタンの下に表示する名前", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var box = new System.Windows.Controls.TextBox { Text = initial, FontSize = 15, Height = 40, Padding = new Thickness(10, 7, 10, 7), Background = ThemeService.Brush("InputBackground"), Foreground = ThemeService.Brush("PrimaryText"), BorderBrush = ThemeService.Brush("BorderBrush") };
        panel.Children.Add(box);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "キャンセル", Width = 98, Height = 40, Margin = new Thickness(6, 0, 0, 0), Style = (Style)WpfApplication.Current.FindResource("AppButtonStyle") };
        var ok = new Button { Content = "変更", Width = 98, Height = 40, Margin = new Thickness(6, 0, 0, 0), Style = (Style)WpfApplication.Current.FindResource("AccentAppButtonStyle"), IsDefault = true };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        ok.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }
    static string? ClipboardFile()
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
    void CopyDeckFile(int slot)
    {
        var mapping = DeckPanelLayout.FindMapping(layout, slot);
        if (!DeckPanelLayout.IsAvailableFile(mapping))
            return;
        try
        {
            var data = new System.Windows.DataObject();
            data.SetData(System.Windows.DataFormats.FileDrop, new[] { mapping!.DeckFilePath });
            System.Windows.Clipboard.SetDataObject(data, true);
        }
        catch (COMException) { }
    }
    void PasteDeckFile(int slot)
    {
        string? file = ClipboardFile();
        if (file == null)
            return;
        var target = GetOrCreateDeckMapping(slot);
        target.DeckFilePath = Path.GetFullPath(file);
        target.DeckMonitor = string.Empty;
        CommitDeckSlotChange(slot);
    }
    void RevealDeckFile(int slot)
    {
        var mapping = DeckPanelLayout.FindMapping(layout, slot);
        if (!DeckPanelLayout.IsAvailableFile(mapping))
            return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{mapping!.DeckFilePath}\"") { UseShellExecute = true });
        }
        catch { }
    }
    void ChooseDeckButtonColor(int slot)
    {
        var existing = DeckPanelLayout.FindMapping(layout, slot);
        var initial = DeckPanelLayout.TryGetButtonColor(existing, out var current) ? current : ThemeService.Color("AccentBrush");
        var picker = new ThemeColorPickerWindow(initial) { Owner = this, Topmost = true };
        if (picker.ShowDialog() != true)
            return;
        var selectedColor = picker.SelectedColor;
        GetOrCreateDeckMapping(slot).DeckColor = $"#{selectedColor.R:X2}{selectedColor.G:X2}{selectedColor.B:X2}";
        RefreshDeckColorSlot(slot);
    }
    void ResetDeckButtonColor(int slot)
    {
        var mapping = DeckPanelLayout.FindMapping(layout, slot);
        if (mapping == null)
            return;
        mapping.DeckColor = "";
        if (!HasDeckButtonContent(mapping))
            layout.Mappings.Remove(mapping);
        RefreshDeckColorSlot(slot);
    }
    void ChooseDeckButtonIcon(int slot)
    {
        var mapping = DeckPanelLayout.FindMapping(layout, slot);
        var picker = new DeckIconPickerWindow(mapping?.DeckIcon ?? "", mapping?.DeckIconPath ?? "") { Owner = this, Topmost = true };
        if (picker.ShowDialog() != true)
            return;
        mapping ??= GetOrCreateDeckMapping(slot);
        mapping.DeckIcon = picker.SelectedPresetId;
        mapping.DeckIconPath = picker.SelectedCustomPath;
        mapping.DeckIconAutoAssigned = false;
        if (!HasDeckButtonContent(mapping))
            layout.Mappings.Remove(mapping);
        CommitDeckSlotChange(slot);
    }
    void RefreshDeckColorSlot(int slot)
        => CommitDeckSlotChange(slot);

    void CommitDeckSlotChange(int slot)
    {
        RefreshDeckSlots(slot, slot);
        OverlayService.NotifyDeckLayoutChanged(false, layout.Id, slot, slot);
    }
    void DeleteDeckButton(int slot)
    {
        string input = DeckPanelLayout.InputName(slot);
        if (layout.Mappings.RemoveAll(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase)) > 0)
            CommitDeckSlotChange(slot);
    }

#if !PRODUCTION_PUBLISH
    internal void DeleteDeckButtonForTest(int slot) => DeleteDeckButton(slot);
    internal void AssignDeckFileForTest(int slot, string path) => AssignDeckFile(slot, path);
#endif
    void ConfigureHoverPreview(Button button, Mapping? mapping)
    {
        if (mapping == null)
            return;
        if (DeckPanelLayout.HasRegisteredFile(mapping) && !DeckPanelLayout.IsAvailableFile(mapping))
        {
            button.ToolTip = DeckPanelLayout.CreateMissingFileToolTip();
            ToolTipService.SetInitialShowDelay(button, 220);
            ToolTipService.SetShowDuration(button, 20000);
            return;
        }
        if (DeckPanelLayout.IsVideoFile(mapping.DeckFilePath) && File.Exists(mapping.DeckFilePath))
        {
            string path = mapping.DeckFilePath;
            button.MouseEnter += (_, _) => ShowVideoPreview(button, path, sourceHoverEnabled: true);
            return;
        }
        if (DeckPanelLayout.IsImageFile(mapping.DeckFilePath) && File.Exists(mapping.DeckFilePath))
        {
            string path = mapping.DeckFilePath;
            var tooltip = new System.Windows.Controls.ToolTip
            {
                Background = WpfBrushes.Transparent,
                BorderBrush = WpfBrushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Placement = PlacementMode.Custom,
                Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = .5, Color = Colors.Black }
            };
            ConfigureOutsideDeckPreview(tooltip, button);
            button.ToolTip = tooltip;
            button.ToolTipOpening += (_, _) =>
            {
                try
                {
                    var image = DeckPanelLayout.LoadImageThumbnail(path, 360);
                    tooltip.Content = image == null ? new TextBlock { Text = "画像を読み込めません", Foreground = ThemeService.Brush("PrimaryText") } : CreateHoverCard(new WpfImage { Source = image, Width = 240, Height = 180, Stretch = Stretch.Uniform });
                }
                catch { tooltip.Content = new TextBlock { Text = "画像を読み込めません", Foreground = ThemeService.Brush("PrimaryText") }; }
            };
            button.ToolTipClosing += (_, _) => tooltip.Content = null;
            return;
        }
        object? content = CreateHoverContent(mapping, button);
        if (content != null)
        {
            if (content is string text)
            {
                var label = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 340,
                    Foreground = ThemeService.Brush("PrimaryText")
                };
                var tooltip = new System.Windows.Controls.ToolTip
                {
                    Content = label,
                    Padding = new Thickness(10, 8, 10, 8),
                    BorderThickness = new Thickness(1),
                    Placement = PlacementMode.Mouse,
                    Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = .5, Color = Colors.Black }
                };
                tooltip.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "CardBackground");
                tooltip.SetResourceReference(System.Windows.Controls.Control.BorderBrushProperty, "AccentBrush");
                ConfigureOutsideDeckPreview(tooltip, button);
                button.ToolTip = tooltip;
            }
            else
            {
                var tooltip = new System.Windows.Controls.ToolTip
                {
                    Content = content,
                    Background = WpfBrushes.Transparent,
                    BorderBrush = WpfBrushes.Transparent,
                    BorderThickness = new Thickness(0),
                    Padding = new Thickness(0),
                    Placement = PlacementMode.Custom
                };
                ConfigureOutsideDeckPreview(tooltip, button);
                button.ToolTip = tooltip;
            }
            ToolTipService.SetInitialShowDelay(button, 220);
            ToolTipService.SetShowDuration(button, 20000);
        }
        if (DeckPanelLayout.IsAudioFile(mapping.DeckFilePath))
        {
            string audioPath = mapping.DeckFilePath;
            button.MouseEnter += (_, _) => ScheduleHoverAudio(button, audioPath);
            button.MouseLeave += (_, _) => StopHoverAudioFor(button);
            button.Unloaded += (_, _) => StopHoverAudioFor(button);
        }
    }
    void ConfigureOutsideDeckPreview(System.Windows.Controls.ToolTip tooltip, Button source)
    {
        tooltip.PlacementTarget = source;
        tooltip.Placement = PlacementMode.Custom;
        tooltip.CustomPopupPlacementCallback = (popupSize, targetSize, offset) => OutsideDeckPlacements(source, popupSize, targetSize);
    }
    CustomPopupPlacement[] OutsideDeckPlacements(FrameworkElement source, System.Windows.Size popupSize, System.Windows.Size targetSize)
    {
        double gap = targetSize.Width;
        double y = (targetSize.Height - popupSize.Height) / 2;
        var right = new CustomPopupPlacement(new Point(targetSize.Width + gap, y), PopupPrimaryAxis.Vertical);
        var left = new CustomPopupPlacement(new Point(-popupSize.Width - gap, y), PopupPrimaryAxis.Vertical);
        Point sourceInDeck = source.TranslatePoint(new Point(0, 0), this);
        double deckWidth = ActualWidth > 0 ? ActualWidth : Width;
        return sourceInDeck.X + targetSize.Width / 2 > deckWidth / 2 ? [left, right] : [right, left];
    }
    object? CreateHoverContent(Mapping mapping, FrameworkElement source)
    {
        if (DeckPanelLayout.IsImageFile(mapping.DeckFilePath))
        {
            var image = DeckPanelLayout.LoadFileThumbnail(mapping.DeckFilePath, 360);
            if (image != null)
            {
                var preview = new WpfImage { Source = image, Width = 240, Height = 180, Stretch = Stretch.Uniform };
                return CreateHoverCard(preview);
            }
        }
        if (DeckPanelLayout.HasRegisteredFile(mapping))
            return DeckPanelLayout.FileDisplayName(mapping);
        return MainWindow.AssignmentToolTipText(mapping);
    }
    void ClearVideoPreviews()
    {
        videoPreview?.Dispose();
        videoPreview = null;
    }
    void ShowVideoPreview(Button source, string path, bool sourceHoverEnabled)
    {
        try
        {
            if (videoPreview?.IsFor(source) != true || videoPreview.SourceHoverEnabled != sourceHoverEnabled)
            {
                videoPreview?.Dispose();
                videoPreview = new DeckVideoPreviewPopup(source, path, this, sourceHoverEnabled);
            }
            videoPreview.Show();
        }
        catch
        {
            try { videoPreview?.Dispose(); } catch { }
            videoPreview = null;
        }
    }
    FrameworkElement CreateHoverCard(FrameworkElement content)
    {
        var inner = new Border { Padding = new Thickness(4), CornerRadius = new CornerRadius(8), Child = content, SnapsToDevicePixels = true, ClipToBounds = true };
        inner.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        inner.SizeChanged += (_, _) => inner.Clip = new RectangleGeometry(new Rect(inner.RenderSize), 8, 8);
        var border = new Border { Padding = new Thickness(1), CornerRadius = new CornerRadius(10), Child = inner, SnapsToDevicePixels = true, Opacity = .98, Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 7, Opacity = .62, Color = Colors.Black } };
        border.SetResourceReference(Border.BackgroundProperty, "CardBackground");
        border.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
        border.BorderThickness = new Thickness(1);
        return border;
    }
    void ScheduleHoverAudio(Button source, string path)
    {
        CancelPendingHoverAudio();
        StopHoverAudio();
        if (!hoverPreviewsEnabled || !DeckPanelLayout.IsAudioFile(path) || !File.Exists(path))
            return;
        pendingHoverAudioSource = source;
        pendingHoverAudioPath = path;
        hoverAudioStartTimer.Start();
    }
    void HoverAudioStartTimerTick(object? sender, EventArgs e)
    {
        Button? source = pendingHoverAudioSource;
        string path = pendingHoverAudioPath;
        CancelPendingHoverAudio();
        if (source?.IsMouseOver == true)
            PlayFileAudio(path, source, requireHoverEnabled: true);
    }
    void CancelPendingHoverAudio()
    {
        hoverAudioStartTimer.Stop();
        pendingHoverAudioSource = null;
        pendingHoverAudioPath = "";
    }
    void StopHoverAudioFor(Button source)
    {
        if (ReferenceEquals(pendingHoverAudioSource, source))
            CancelPendingHoverAudio();
        if (ReferenceEquals(hoverAudioSource, source))
            StopHoverAudio();
    }
    void PlayFileAudio(string path, Button source, bool requireHoverEnabled)
    {
        if ((requireHoverEnabled && !hoverPreviewsEnabled) || !DeckPanelLayout.IsAudioFile(path) || !File.Exists(path))
            return;
        try
        {
            StopHoverAudio();
            var player = new MediaPlayer();
            hoverAudioPlayer = player;
            hoverAudioSource = source;
            player.MediaEnded += (_, _) => { if (ReferenceEquals(hoverAudioPlayer, player)) StopHoverAudio(); };
            player.MediaFailed += (_, _) => { if (ReferenceEquals(hoverAudioPlayer, player)) StopHoverAudio(); };
            player.Open(new Uri(path, UriKind.Absolute));
            player.Volume = .8;
            player.Play();
        }
        catch { StopHoverAudio(); }
    }
    void StopHoverAudio()
    {
        var player = hoverAudioPlayer;
        hoverAudioPlayer = null;
        hoverAudioSource = null;
        if (player == null)
            return;
        try
        {
            player.Stop();
            player.Close();
        }
        catch { }
    }
    void DeckButtonDragStarted(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button { Tag: int })
            return;
        fileDragButton = (Button)sender;
        fileDragStart = e.GetPosition(fileDragButton);
    }
    void DeckButtonDragMoved(object sender, MouseEventArgs e)
    {
        if (sender is not Button button || !ReferenceEquals(fileDragButton, button) || e.LeftButton != MouseButtonState.Pressed || button.Tag is not int slot)
            return;
        Point current = e.GetPosition(button);
        if (Math.Abs(current.X - fileDragStart.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(current.Y - fileDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        var mapping = DeckPanelLayout.FindMapping(layout, slot);
        if (!deckReorderDragging)
        {
            CancelPendingHoverAudio();
            StopHoverAudio();
            deckReorderDragging = true;
            deckReorderSourceSlot = slot;
            button.Opacity = .72;
            button.CaptureMouse();
            StartReorderDragPreview(button, mapping);
        }
        UpdateDeckReorderDrag();
        if (DeckPanelLayout.IsAvailableFile(mapping) && IsCursorOutsideDeckWindow())
            StartExternalFileDrag(button, mapping!);
        e.Handled = true;
    }
    bool IsCursorOutsideDeckWindow()
    {
        var cursor = CurrentCursorPosition();
        Point point = PointFromScreen(new Point(cursor.X, cursor.Y));
        return point.X < 0 || point.Y < 0 || point.X > ActualWidth || point.Y > ActualHeight;
    }
    System.Drawing.Point CurrentCursorPosition()
        => CursorPositionProviderForTest?.Invoke() ?? System.Windows.Forms.Cursor.Position;
    void StartExternalFileDrag(Button button, Mapping mapping)
    {
        CancelDeckReorder();
        fileDragButton = null;
        try
        {
            var data = new System.Windows.DataObject();
            data.SetData(System.Windows.DataFormats.FileDrop, new[] { mapping.DeckFilePath });
            data.SetData(DeckPanelLayout.FileSourceDragFormat, true);
            if (DeckIconCatalog.HasIcon(mapping))
                StartDragPreview(mapping);
            try
            {
                internalDeckDragActive = true;
                // Exposing Move lets Explorer relocate the registered source file.
                // Deck is a launcher/reference surface, so external drops are
                // copy-only and never change or delete the registered source.
                System.Windows.DragDrop.DoDragDrop(button, data, DeckPanelLayout.ExternalFileDragEffects);
            }
            finally { internalDeckDragActive = false; StopDragPreview(); }
        }
        catch (COMException) { }
    }
    void DeckButtonDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not Button)
            return;
        if (e.Data.GetDataPresent(DeckPanelLayout.SlotDragFormat))
        {
            // Normal Deck mode deliberately has no internal drop operation.
            e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
            return;
        }
        if (!DeckPanelLayout.IsInternalFileDrag(e.Data) && DeckPanelLayout.GetDroppedFile(e.Data) != null)
        {
            e.Effects = System.Windows.DragDropEffects.Copy;
            e.Handled = true;
        }
    }
    void DeckButtonDropped(object sender, System.Windows.DragEventArgs e)
    {
        if (sender is not Button { Tag: int target } || target < 1)
            return;
        if (e.Data.GetDataPresent(DeckPanelLayout.SlotDragFormat))
        {
            e.Handled = true;
            return;
        }
        if (!DeckPanelLayout.IsInternalFileDrag(e.Data) && DeckPanelLayout.GetDroppedFile(e.Data) is string file)
        {
            AssignDeckFile(target, file);
            e.Handled = true;
        }
    }

    void AssignDeckFile(int slot, string file)
    {
        var mapping = DeckPanelLayout.FindMapping(layout, slot);
        if (mapping == null)
        {
            mapping = new Mapping { Input = DeckPanelLayout.InputName(slot), Layer = DeckPanelLayout.Layer };
            layout.Mappings.Add(mapping);
        }
        mapping.DeckFilePath = Path.GetFullPath(file);
        mapping.DeckMonitor = string.Empty;
        CommitDeckSlotChange(slot);
    }
    void StartDragPreview(Mapping mapping)
    {
        if (!DeckPanelLayout.HasRegisteredFile(mapping))
            return;
        try
        {
            FrameworkElement? configuredIcon = DeckIconCatalog.CreateVisual(mapping, 42, false);
            var image = configuredIcon == null && DeckPanelLayout.HasRegisteredFile(mapping) ? DeckPanelLayout.LoadFileThumbnail(mapping.DeckFilePath, 128) : null;
            FrameworkElement preview = configuredIcon ?? (image != null
                ? new WpfImage { Source = image, Stretch = Stretch.Uniform }
                : DeckPanelLayout.CreateFileIcon(mapping.DeckFilePath, 42));
            dragPreview = CreateCompactDragPreview(preview);
            dragPreview.Show();
            UpdateDragPreview();
        }
        catch { dragPreview = null; }
    }
    void UpdateDragPreview()
    {
        if (dragPreview == null)
            return;
        var screen = CurrentCursorPosition();
        dragPreview.MoveToPhysical(screen.X, screen.Y);
    }
    void DeckDragGiveFeedback(object sender, System.Windows.GiveFeedbackEventArgs e)
    {
        UpdateDragPreview();
        e.UseDefaultCursors = false;
        e.Handled = true;
    }
    void StopDragPreview()
    {
        if (dragPreview == null)
            return;
        try
        {
            dragPreview.Close();
        }
        catch { }
        dragPreview = null;
        Mouse.OverrideCursor = null;
    }
    void DeckButtonDragEnded(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button && ReferenceEquals(fileDragButton, button) && deckReorderDragging)
        {
            try
            {
                Point point = button.PointToScreen(e.GetPosition(button));
                int targetSlot = DeckSlotAt(new NativeDropPoint { X = (int)Math.Round(point.X), Y = (int)Math.Round(point.Y) });
                if (targetSlot > 0 && targetSlot != deckReorderSourceSlot)
                {
                    DeckPanelLayout.SwapSlots(layout, deckReorderSourceSlot, targetSlot);
                    RefreshDeckSlots(deckReorderSourceSlot, targetSlot);
                    OverlayService.NotifyDeckLayoutChanged(false, layout.Id, deckReorderSourceSlot, targetSlot);
                }
            }
            finally { CancelDeckReorder(); }
        }
        fileDragButton = null;
    }
    void CancelDeckReorder()
    {
        fileDragButton?.Opacity = 1;
        if (Mouse.Captured is Button captured && ReferenceEquals(captured, fileDragButton))
            captured.ReleaseMouseCapture();
        deckReorderDragging = false;
        deckReorderSourceSlot = 0;
        ClearDeckReorderTarget();
        StopDragPreview();
    }
    void UpdateDeckReorderDrag()
    {
        var cursor = CurrentCursorPosition();
        int targetSlot = DeckSlotAt(new NativeDropPoint { X = cursor.X, Y = cursor.Y });
        SetDeckReorderTarget(targetSlot);
        UpdateDragPreview();
    }
    void SetDeckReorderTarget(int slot)
    {
        Button? target = slot > 0 && slot != deckReorderSourceSlot ? deckButtons.FirstOrDefault(x => x.Tag is int candidate && candidate == slot) : null;
        if (ReferenceEquals(target, deckReorderTargetButton))
            return;
        ClearDeckReorderTarget();
        if (target == null)
            return;
        deckReorderTargetOriginalBorderBrush = target.BorderBrush;
        deckReorderTargetOriginalBorderThickness = target.BorderThickness;
        target.BorderBrush = new SolidColorBrush(DeckAccent);
        target.BorderThickness = new Thickness(3);
        target.ApplyTemplate();
        if (target.Template.FindName("DropTargetBadge", target) is UIElement badge)
            badge.Opacity = 1;
        deckReorderTargetButton = target;
    }
    void ClearDeckReorderTarget()
    {
        if (deckReorderTargetButton == null)
            return;
        deckReorderTargetButton.ApplyTemplate();
        if (deckReorderTargetButton.Template.FindName("DropTargetBadge", deckReorderTargetButton) is UIElement badge)
            badge.Opacity = 0;
        deckReorderTargetButton.BorderBrush = deckReorderTargetOriginalBorderBrush ?? WpfBrushes.Transparent;
        deckReorderTargetButton.BorderThickness = deckReorderTargetOriginalBorderThickness;
        deckReorderTargetButton = null;
        deckReorderTargetOriginalBorderBrush = null;
        deckReorderTargetOriginalBorderThickness = new Thickness(0);
    }
    void StartReorderDragPreview(Button source, Mapping? mapping)
    {
        try
        {
            FrameworkElement content = DeckPanelLayout.CreateButtonContent(DeckPanelLayout.InputName((int)source.Tag), mapping);
            if (content is TextBlock text)
                text.Foreground = source.Foreground;
            var face = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(3),
                Background = source.Background,
                BorderBrush = source.BorderBrush,
                BorderThickness = source.BorderThickness,
                Child = content
            };
            dragPreview = CreateCompactDragPreview(face);
            dragPreview.Show();
        }
        catch { dragPreview = null; }
    }
    internal static DeckDragPreviewWindow CreateCompactDragPreview(FrameworkElement preview)
        => new(preview, compact: true);
    internal void SetDeckReorderTargetForTest(int slot) => SetDeckReorderTarget(slot);
    internal void ClearDeckReorderTargetForTest() => ClearDeckReorderTarget();
}
