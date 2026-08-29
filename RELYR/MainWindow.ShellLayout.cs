using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using ContextMenu = System.Windows.Controls.ContextMenu;
using ListBox = System.Windows.Controls.ListBox;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBox = System.Windows.Controls.TextBox;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfMessageBox = RELYR.AppDialog;

namespace RELYR;

public partial class MainWindow
{
    void ProfileChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading)
            return;
        if (ProfileBox.SelectedItem is ComboBoxItem { Tag: string tag } && tag == NewProfileMenuTag)
        {
            loading = true;
            ProfileBox.SelectedItem = config.ActiveProfile;
            loading = false;
            Dispatcher.BeginInvoke(new Action(() => NewProfile_Click(ProfileBox, new RoutedEventArgs())));
            return;
        }
        if (ProfileBox.SelectedItem is not string name)
            return;
        suppressAutomaticProfileSwitchUntil = DateTime.UtcNow.AddSeconds(2);
        SwitchProfile(name, false);
    }
    void ProfileDropDownOpened(object sender, EventArgs e) => profileDropDownOpen = true;
    void ProfileDropDownClosed(object sender, EventArgs e)
    {
        profileDropDownOpen = false;
        suppressAutomaticProfileSwitchUntil = DateTime.UtcNow.AddSeconds(2);
    }
    void KeyboardLayoutChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || config == null)
            return;
        config.KeyboardLayout = KeyboardLayoutBox.SelectedIndex == 1 ? "US" : "JIS";
        appliedConfig.KeyboardLayout = config.KeyboardLayout;
        engine.UseUsLayout = config.KeyboardLayout == "US";
        BuildKeyboard();
        ColorButtons();
        var persisted = store.Load();
        persisted.KeyboardLayout = config.KeyboardLayout;
        store.Save(persisted);
        SynchronizeEditorHistoryCheckpoint();
        ShowInlineNotice(config.KeyboardLayout + "配列へ切り替えました");
    }

    void LightThemeToggle_Click(object sender, RoutedEventArgs e) => ApplyToolbarTheme(AppThemeMode.Light);
    void DarkThemeToggle_Click(object sender, RoutedEventArgs e) => ApplyToolbarTheme(AppThemeMode.Dark);

    void ApplyToolbarTheme(AppThemeMode mode)
    {
        if (config == null)
            return;
        config.ThemeMode = mode;
        if (appliedConfig != null)
            appliedConfig.ThemeMode = mode;
        var persisted = store.Load();
        persisted.ThemeMode = mode;
        store.Save(persisted);
        SynchronizeEditorHistoryCheckpoint();
        ThemeService.Apply(mode);
        UpdateThemeToolbarControls();
    }

    void UpdateThemeToolbarControls()
    {
        if (LightThemeToggle == null || DarkThemeToggle == null)
            return;
        LightThemeToggle.IsChecked = ThemeService.CurrentMode == AppThemeMode.Light;
        DarkThemeToggle.IsChecked = ThemeService.CurrentMode == AppThemeMode.Dark;
    }
    void MainContent_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (WorkspaceGrid == null || AssignmentPane == null || LowerInputRow == null)
            return;
        double gap = e.NewSize.Width < 1000 ? 12 : 16;
        double shellWidth = ActualWidth > 0 ? ActualWidth : e.NewSize.Width + LayerNavigationPane.Width;
        // The navigation contains short fixed labels, so it can be materially
        // narrower than the inspector without wrapping. Return that space to
        // the keyboard and Deck workspaces at every breakpoint.
        double navigationPaneWidth = shellWidth < 1000 ? 208 : shellWidth < 1500 ? 216 : 224;
        double inspectorPaneWidth = shellWidth < 1000 ? 232 : shellWidth < 1500 ? 252 : 272;
        LayerNavigationPane.Width = navigationPaneWidth;
        LayerNavigationColumn.Width = new GridLength(0);
        HeaderBrandColumn.Width = new GridLength(0);
        AssignmentPaneColumn.Width = new GridLength(inspectorPaneWidth);
        double centerWidth = Math.Max(360, e.NewSize.Width - inspectorPaneWidth - gap * 2);
        double mouseWidth = Math.Clamp(centerWidth * .23, 96, 240);
        MouseColumn.Width = new GridLength(mouseWidth);
        // Keep the mouse on the same visual key scale as the main keyboard.
        // Its portrait layout may make the lower row taller than the keypad.
        double lowerHeight = Math.Clamp(e.NewSize.Height * .36, 220, 340);
        // Fit the main and lower controls as one vertical composition.  The
        // A-key remains the scale reference for every lower and mouse key,
        // even on a short display, so the mouse can never be clipped below.
        double availableHeight = Math.Max(360, e.NewSize.Height - gap * 2);
        double fixedVerticalSpace = KeyboardSurfaceCard.Padding.Top + KeyboardSurfaceCard.Padding.Bottom
            + LowerInputGrid.Margin.Top + MouseHost.Margin.Top + MouseHost.Margin.Bottom;
        double widthScale = Math.Max(.35, (centerWidth - KeyboardSurfaceCard.Padding.Left - KeyboardSurfaceCard.Padding.Right) / Math.Max(1, KeyboardPanel.Width));
        double heightScale = Math.Max(.35, (availableHeight - fixedVerticalSpace) / (KeyboardPanel.Height + MousePanel.Height));
        double maxCommonScale = Math.Clamp(Math.Min(widthScale, heightScale), .35, MaximumKeyboardWorkspaceScale);
        KeyboardViewbox.Width = KeyboardPanel.Width * maxCommonScale;
        KeyboardViewbox.Height = KeyboardPanel.Height * maxCommonScale;
        KeyboardViewbox.MaxWidth = KeyboardViewbox.Width;
        KeyboardViewbox.MaxHeight = KeyboardViewbox.Height;
        double maximumMouseWidth = shellWidth <= 1500 ? 170 : MousePanel.Width * MaximumKeyboardWorkspaceScale;
        double maximumMouseScale = maximumMouseWidth / Math.Max(1, MousePanel.Width);
        double mouseScale = Math.Clamp((mouseWidth - 16) / Math.Max(1, MousePanel.Width), .35, maximumMouseScale);
        double secondaryHeight = Math.Min(lowerHeight - 12, Math.Max(80, (centerWidth - mouseWidth - 14) / 654 * 312));
        SecondaryKeyboardViewbox.Height = secondaryHeight;
        MouseHost.Width = MousePanel.Width * mouseScale;
        MouseHost.Height = MousePanel.Height * mouseScale;
        double mouseTotalHeight = MouseHost.Height + MouseHost.Margin.Top + MouseHost.Margin.Bottom;
        LowerInputRow.Height = new GridLength(Math.Max(secondaryHeight, mouseTotalHeight) + LowerInputGrid.Margin.Top);
        WorkspaceGrid.Margin = new Thickness(gap);
        AssignmentPane.Padding = new Thickness(gap);
        UpdateLayerButtonWidths();
        ScheduleLowerKeyboardScaleSync();
        ScheduleToolbarKeyboardAlignment();
    }

    void TopToolbarPane_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ToolbarPanel == null || ProfileBox == null || KeyboardLayoutBox == null)
            return;
        bool compact = e.NewSize.Width < 1040;
        bool narrow = e.NewSize.Width < 920;
        ProfileBox.Width = narrow ? 132 : compact ? 145 : 170;
        KeyboardLayoutBox.Width = narrow ? 70 : 84;
        ToolbarPanel.Margin = new Thickness(compact ? 6 : 14, 9.5, 0, -9.5);
        double compactCommandWidth = compact ? 34 : 44;
        foreach (var control in new System.Windows.Controls.Control[] { EditorUndoButton, EditorRedoButton, MultiSelectToggle, MultiCopyButton, MultiPasteButton, MultiDeleteButton })
        {
            control.Width = compactCommandWidth;
            control.MinWidth = compactCommandWidth;
            control.Margin = new Thickness(compact ? 1 : 3);
            control.Padding = new Thickness(0);
        }
        double themeCommandWidth = compact ? 34 : 40;
        foreach (var control in new System.Windows.Controls.Control[] { LightThemeToggle, DarkThemeToggle })
        {
            control.Width = themeCommandWidth;
            control.MinWidth = themeCommandWidth;
            control.Margin = new Thickness(0);
            control.Padding = new Thickness(0);
        }
        ProfileToolbarIcon.Margin = new Thickness(0, 0, compact ? 5 : 8, 0);
        KeyboardLayoutToolbarIcon.Margin = new Thickness(compact ? 10 : 18, 0, compact ? 5 : 8, 0);
        MultiSelectActionsPanel.Margin = new Thickness(compact ? 10 : 18, 0, 0, 0);
        ToolbarSaveButton.Width = compact ? 78 : 96;
        ToolbarSaveButton.MinWidth = ToolbarSaveButton.Width;
        ToolbarSaveButton.Margin = new Thickness(compact ? 8 : 18, 3, compact ? 6 : 12, 3);
        ScheduleToolbarKeyboardAlignment();
    }
    void KeyboardViewbox_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ScheduleLowerKeyboardScaleSync();
        ScheduleToolbarKeyboardAlignment();
    }
    void ScheduleToolbarKeyboardAlignment()
    {
        int generation = ++toolbarKeyboardAlignmentGeneration;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            if (generation == toolbarKeyboardAlignmentGeneration)
                AlignToolbarProfileWithEscapeKey();
        }));
    }
    void AlignToolbarProfileWithEscapeKey()
    {
        if (!editorUiInitialized || KeyboardWorkspace.Visibility != Visibility.Visible || !KeyboardViewbox.IsVisible)
            return;
        var escapeKey = InputButtons(KeyboardPanel).FirstOrDefault(button => string.Equals(button.Tag?.ToString(), "Esc", StringComparison.OrdinalIgnoreCase));
        if (escapeKey == null || escapeKey.ActualWidth <= 0 || ProfileToolbarIcon.ActualWidth <= 0)
            return;
        try
        {
            double escapeLeft = escapeKey.TranslatePoint(new System.Windows.Point(), TopToolbarPane).X;
            double profileIconLeft = ProfileToolbarIcon.TranslatePoint(new System.Windows.Point(), TopToolbarPane).X;
            double alignedLeftMargin = Math.Max(0, ToolbarContextPanel.Margin.Left + escapeLeft - profileIconLeft);
            if (Math.Abs(alignedLeftMargin - ToolbarContextPanel.Margin.Left) <= 0.25)
                return;
            ToolbarContextPanel.Margin = new Thickness(alignedLeftMargin, 0, 0, 0);
        }
        catch (InvalidOperationException)
        {
            // A resize can briefly detach the scaled keyboard visual. The next
            // layout pass schedules alignment again without disturbing input.
        }
    }
    void ScheduleLowerKeyboardScaleSync()
    {
        int generation = ++lowerKeyboardScaleSyncGeneration;
        int attempts = 0;
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (generation != lowerKeyboardScaleSyncGeneration || ++attempts > 6 || MatchLowerKeyboardScale())
                LayoutUpdated -= handler;
        };
        LayoutUpdated += handler;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
        {
            if (generation == lowerKeyboardScaleSyncGeneration)
                MatchLowerKeyboardScale();
        }));
    }
    bool MatchLowerKeyboardScale()
    {
        var referenceKey = KeyboardPanel?.Children.OfType<System.Windows.Controls.Button>().FirstOrDefault(button => Equals(button.Tag, "A"));
        var lowerKey = SecondaryKeyboardPanel?.Children.OfType<System.Windows.Controls.Button>().FirstOrDefault(button => Equals(button.Tag, "Insert"));
        if (referenceKey == null || lowerKey == null || referenceKey.ActualHeight <= 0 || lowerKey.ActualHeight <= 0 || SecondaryKeyboardViewbox.ActualHeight <= 0)
            return false;
        double mainHeight = referenceKey.TransformToAncestor(this).TransformBounds(new Rect(0, 0, referenceKey.ActualWidth, referenceKey.ActualHeight)).Height;
        double lowerHeight = lowerKey.TransformToAncestor(this).TransformBounds(new Rect(0, 0, lowerKey.ActualWidth, lowerKey.ActualHeight)).Height;
        if (mainHeight <= 0 || lowerHeight <= 0)
            return false;
        double difference = Math.Abs(mainHeight - lowerHeight);
        double matchedHeight = SecondaryKeyboardViewbox.ActualHeight;
        if (difference >= .05)
        {
            double correction = mainHeight / lowerHeight;
            double matchedWidth = SecondaryKeyboardViewbox.ActualWidth * correction;
            matchedHeight = SecondaryKeyboardViewbox.ActualHeight * correction;
            SecondaryKeyboardViewbox.Width = matchedWidth;
            SecondaryKeyboardViewbox.Height = matchedHeight;
        }
        double maximumMouseWidth = ActualWidth <= 1500 ? 170 : MousePanel.Width * MaximumKeyboardWorkspaceScale;
        double maximumMouseScale = maximumMouseWidth / Math.Max(1, MousePanel.Width);
        double mouseScale = Math.Min(mainHeight / SecondaryKeyHeight, Math.Min(maximumMouseScale, Math.Max(.35, (MouseColumn.ActualWidth - 16) / Math.Max(1, MousePanel.Width))));
        MouseHost.Width = MousePanel.Width * mouseScale;
        MouseHost.Height = MousePanel.Height * mouseScale;
        double mouseTotalHeight = MouseHost.Height + MouseHost.Margin.Top + MouseHost.Margin.Bottom;
        LowerInputRow.Height = new GridLength(Math.Max(matchedHeight, mouseTotalHeight) + LowerInputGrid.Margin.Top);
        return difference < .05;
    }
    void LayerButtonsPanel_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateLayerButtonWidths();
    void UpdateLayerButtonWidths()
    {
        if (LayerButtonsPanel == null)
            return;
        bool compact = MainContentGrid.ActualHeight > 0 && MainContentGrid.ActualHeight < 620;
        bool narrow = LayerNavigationPane.Width < 215;
        foreach (var category in new[] { KeyboardLayerCategory, MouseLayerCategory, WindowsLayerCategory })
            category.Margin = compact ? new Thickness(8, 4, 8, 2) : new Thickness(8, 6, 8, 3);
        foreach (var divider in new[] { KeyboardLayerDivider, MouseLayerDivider })
            divider.Margin = compact ? new Thickness(8, 4, 8, 2) : new Thickness(8, 6, 8, 2);
        foreach (var button in LayerButtonsPanel.Children.OfType<System.Windows.Controls.Button>())
        {
            button.Width = double.NaN;
            button.Height = compact ? 48 : 52;
            button.MinHeight = 48;
            button.Padding = new Thickness(8, 4, 8, 4);
            button.FontSize = 15;
            button.Margin = new Thickness(0, 1, 0, 1);
            button.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
            button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch;
            StackPanel? content = button.Content switch
            {
                StackPanel direct => direct,
                Grid grid => grid.Children.OfType<StackPanel>().FirstOrDefault(),
                _ => null
            };
            if (content != null)
            {
                content.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
                foreach (var title in content.Children.OfType<TextBlock>().Take(1))
                {
                    title.TextWrapping = TextWrapping.NoWrap;
                    title.FontSize = 15;
                }
                foreach (var description in content.Children.OfType<TextBlock>().Skip(1))
                {
                    description.Visibility = Visibility.Visible;
                    description.FontSize = narrow ? 9.5 : 10.5;
                    description.TextWrapping = TextWrapping.NoWrap;
                }
            }
        }
    }
}
