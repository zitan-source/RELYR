using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MenuItem = System.Windows.Controls.MenuItem;

namespace RELYR;

internal static class UiIntegrationTest
{
    internal static int Run(TextWriter output)
    {
        var report = new VerificationReport(output);
        Action<bool, string> Check = report.Check;
        static void Pump(MainWindow w) => w.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
        string? previousConfigDirectory = Environment.GetEnvironmentVariable("RELYR_CONFIG_DIR");
        string testConfigDirectory = VerificationPaths.CreateRunDirectory("ui-test");
        Environment.SetEnvironmentVariable("RELYR_CONFIG_DIR", testConfigDirectory);
        MainWindow? window = null;
        try
        {
            new ConfigService().Save(new AppConfig { FirstRunCompleted = true, CapsLockLayerWarningAccepted = true, CheckForUpdates = false, Gestures = [new GestureDefinition { Name = "ウィンドウ操作", UpKind = ActionKind.Shortcut, UpValue = "Win+Up", CenterKind = ActionKind.Key, CenterValue = "Enter" }] });
            window = new MainWindow(true) { Width = 800, Height = 620 };
            System.Windows.Application.Current.MainWindow = window;
            window.Show();
            window.UpdateLayout();
            Check(window.IsInputEngineReadyForTest, "input engine is ready when the main window and tray initialization complete");
            int hiddenProfileOverlays = 0;
            for (int i = 0; i < 3; i++)
            {
                var transientOverlay = new ProfileSwitchOverlay("標準", TimeSpan.FromMilliseconds(150));
                transientOverlay.Show();
                PumpFor(TimeSpan.FromMilliseconds(350));
                if (!transientOverlay.IsVisible)
                    hiddenProfileOverlays++;
            }
            Check(hiddenProfileOverlays == 3, "profile-switch notification hides after its display duration on three consecutive displays");
            window.ShowProfileOverlayForTest("標準");
            var firstProfileOverlay = window.ProfileOverlayForTest;
            window.ShowProfileOverlayForTest("標準");
            Check(firstProfileOverlay != null && ReferenceEquals(firstProfileOverlay, window.ProfileOverlayForTest), "repeated notification requests for the same profile reuse the current state instead of restarting the display timer");
            PumpFor(TimeSpan.FromMilliseconds(1250));
            Check(!window.IsProfileOverlayVisibleForTest, "the real main-window profile notification disappears after one second");
            var standardProfile = window.CurrentProfileForTest;
            standardProfile.Mappings.Add(new Mapping { Input = "MouseForward+MouseLeft", Kind = ActionKind.Key, Value = "A" });
            var switchedProfile = new Profile { Name = "レイヤー切替テスト", Mappings = [new Mapping { Input = "MouseForward+MouseLeft", Kind = ActionKind.Key, Value = "B" }] };
            window.ProfilesForTest.Add(switchedProfile);
            window.SaveAndApplyForTest();
            window.BeginLayerMappingScopeForTest("MouseForward");
            window.SwitchProfileForTest(switchedProfile.Name);
            Check(window.RuntimeMappingForTest("MouseForward+MouseLeft") is { Value: "A" }, "a held layer keeps the complete profile mapping snapshot across a runtime profile switch");
            window.EndLayerMappingScopeForTest("MouseForward");
            Check(window.RuntimeMappingForTest("MouseForward+MouseLeft") is { Value: "B" }, "releasing the layer immediately activates the newly selected profile mappings");
            window.SwitchProfileForTest(standardProfile.Name);
            standardProfile.Mappings.RemoveAll(x => x.Input == "MouseForward+MouseLeft");
            window.ProfilesForTest.Remove(switchedProfile);
            window.SaveAndApplyForTest();
            var managedStandard = new Profile { Name = "標準" };
            var managedAutomatic = new Profile { Name = "自動切替テスト", AutoSwitchEnabled = true, AutoSwitchApplications = ["relyr-profile-test.exe"] };
            window.ApplyProfileManagerResultForTest([managedStandard, managedAutomatic], managedStandard.Name, true);
            var persistedProfiles = new ConfigService().Load();
            Check(!persistedProfiles.AutoSave && persistedProfiles.AutoSwitchProfilesByCursor && persistedProfiles.Profiles.Any(x => x.Name == managedAutomatic.Name && x.AutoSwitchEnabled && x.AutoSwitchApplications.Contains("relyr-profile-test.exe")) && window.AppliedProfileNameForTest == managedStandard.Name, "Profile Manager Apply immediately saves and activates profile routing even when assignment auto-save is off");
            bool automaticProfileApplied = window.ApplyAutomaticProfileForTest(["relyr-profile-test"]);
            Check(automaticProfileApplied && window.AppliedProfileNameForTest == managedAutomatic.Name && window.ProfileOverlayForTest is { IsVisible: true } appliedOverlay && appliedOverlay.ProfileNameText.Text == managedAutomatic.Name, "an applied target application switches the live profile and shows the enabled profile overlay end to end");
            PumpFor(TimeSpan.FromMilliseconds(1250));
            bool automaticProfileReturned = window.ApplyAutomaticProfileForTest([]);
            Check(automaticProfileReturned && window.AppliedProfileNameForTest == managedStandard.Name && window.ProfileOverlayForTest is { IsVisible: true } returnOverlay && returnOverlay.ProfileNameText.Text == managedStandard.Name, "leaving the target application returns to the standard profile and shows its overlay");
            PumpFor(TimeSpan.FromMilliseconds(1250));
            window.ApplyProfileManagerResultForTest([new Profile { Name = "標準" }], "標準", true);
            window.Width = 800;
            window.Height = 620;
            window.UpdateLayout();
            Pump(window);
            double compactMouseWidth = RenderedWidth(window.MousePanel, window);
            window.Width = 1500;
            window.Height = 900;
            window.UpdateLayout();
            Pump(window);
            double expandedMouseWidth = RenderedWidth(window.MousePanel, window);
            output.WriteLine($"INFO main mouse rendered width compact={compactMouseWidth:F1} expanded={expandedMouseWidth:F1}");
            Check(expandedMouseWidth > compactMouseWidth * 1.2 && expandedMouseWidth <= 170, "main mouse diagram scales down for compact windows without growing beyond its designed size");
            window.Width = 800;
            window.Height = 620;
            window.UpdateLayout();
            Pump(window);
            var compactProfileManager = new ProfileManagerWindow([new Profile { Name = "標準" }, new Profile { Name = "編集用", AutoSwitchEnabled = true, AutoSwitchApplications = ["notepad.exe"] }], "編集用", true) { Owner = window, ShowInTaskbar = false, Width = 820, Height = 560 };
            compactProfileManager.Show();
            compactProfileManager.UpdateLayout();
            Check(compactProfileManager.ProfileListColumn.ActualWidth <= 170 && compactProfileManager.ApplicationManagementColumn.ActualWidth > compactProfileManager.ProfileListColumn.ActualWidth * 3 && compactProfileManager.RunningApplicationList.ActualWidth > 250 && compactProfileManager.RunningApplicationList.ActualHeight > 260, $"profile management keeps a compact profile pane and gives the application editor most of the available width and height (list={compactProfileManager.RunningApplicationList.ActualWidth:F0}x{compactProfileManager.RunningApplicationList.ActualHeight:F0}, profile={compactProfileManager.ProfileListColumn.ActualWidth:F0}, editor={compactProfileManager.ApplicationManagementColumn.ActualWidth:F0})");
            Check(ScrollViewer.GetHorizontalScrollBarVisibility(compactProfileManager.RunningApplicationList) == ScrollBarVisibility.Disabled && ScrollViewer.GetVerticalScrollBarVisibility(compactProfileManager.RunningApplicationList) == ScrollBarVisibility.Hidden, "running application list remains scrollable by wheel without displaying scrollbars");
            Check(Descendants<Border>(compactProfileManager.AssignedApplicationList).Any(border => border.CornerRadius.TopLeft == 8) && Descendants<Border>(compactProfileManager.RunningApplicationList).Any(border => border.CornerRadius.TopLeft == 8), "profile application lists use the shared eight-pixel control radius instead of square system borders");
            Check(compactProfileManager.CursorProfileSwitchBox.IsChecked == true && compactProfileManager.CursorProfileSwitchBox.ToolTip?.ToString()?.Contains("RELYR自身") == true, "profile manager exposes cursor-based automatic switching and explains that RELYR windows are ignored");
            var profileCommandButtons = new[] { compactProfileManager.AddProfileButton, compactProfileManager.RenameProfileButton, compactProfileManager.DeleteProfileButton };
            Check(profileCommandButtons.All(button => button.Content is Viewbox && Descendants<System.Windows.Shapes.Path>(button).Any(path => Equals(path.Stroke, button.Foreground))) && profileCommandButtons.All(button => !string.IsNullOrWhiteSpace(button.ToolTip?.ToString())), "profile add, rename, and delete commands use theme-aware vector icons with explanatory tooltips instead of cramped text");
            CaptureForReview(compactProfileManager, "profile-manager-compact.png");
            compactProfileManager.Close();
            Check(window.VersionText.Text == "v" + MainWindow.DisplayVersion && window.Title.Contains(window.VersionText.Text), "running version is always visible");
            using (var darkTrayMenu = TrayMenuTheme.Create(true))
            using (var lightTrayMenu = TrayMenuTheme.Create(false))
            {
                darkTrayMenu.Items.Add("表示");
                lightTrayMenu.Items.Add("表示");
                TrayMenuTheme.Apply(darkTrayMenu, true);
                TrayMenuTheme.Apply(lightTrayMenu, false);
                darkTrayMenu.PerformLayout();
                Check(darkTrayMenu.BackColor.GetBrightness() < .25 && darkTrayMenu.ForeColor.GetBrightness() > .7 && lightTrayMenu.BackColor.GetBrightness() > .9 && lightTrayMenu.ForeColor.GetBrightness() < .3 && darkTrayMenu.MinimumSize.Width >= 230 && darkTrayMenu.Region is { } roundedRegion && !roundedRegion.IsVisible(0, 0), "tray menu uses a readable, spacious palette with a genuinely rounded outer edge");
            }
            Check(window.TrayMenuItemTextsForTest().Contains("再起動") && !window.TrayMenuItemTextsForTest().Contains("セーフモード"), "tray menu replaces the unclear safe mode item with RELYR restart");
            Check(window.InputDisplayText.Text == "キーを選択してください" && window.KindBox.Items.Count == 8 && window.LongKindBox.Items.Count == 8 && window.KindBox.Items.Cast<object>().Select(x => x.GetType().GetProperty("Label")?.GetValue(x)?.ToString()).SequenceEqual(["別のキー", "プロファイル", "ショートカット", "文字列", "アプリ・パス", "マクロ", "ジェスチャー", "キーパッドから入力"]) && window.LongKindBox.Items.Cast<object>().Single(x => Equals(x.GetType().GetProperty("Kind")?.GetValue(x), ActionKind.Gesture)).GetType().GetProperty("IsEnabled")?.GetValue(window.LongKindBox.Items.Cast<object>().Single(x => Equals(x.GetType().GetProperty("Kind")?.GetValue(x), ActionKind.Gesture))) is false && window.DestinationClearButton.Visibility == Visibility.Collapsed && window.DestinationConfirmButton.Visibility == Visibility.Collapsed && window.LongDestinationClearButton.Visibility == Visibility.Collapsed && window.LongDestinationConfirmButton.Visibility == Visibility.Collapsed, "short and long editors replace disable with keypad input and hide edit actions until direct editing starts");
            window.ValueBox.ApplyTemplate();
            window.MultiCopyButton.ApplyTemplate();
            Check(Descendants<Border>(window.ValueBox).Any(border => border.CornerRadius.TopLeft == 8) && Descendants<Border>(window.MultiCopyButton).Any(border => border.CornerRadius.TopLeft == 8), "text inputs and standard buttons share the same eight-pixel control radius");
            var navigationIcons = new[] { window.MacroManagerButton, window.ProfileManagerButton, window.GestureManagerButton, window.DeckPanelManagerButton, window.AppSettingsButton }.Select(button => Descendants<Canvas>(button).Single(icon => Equals(icon.Tag, "SidebarIcon"))).ToArray();
            var taskbarLayerIcon = Descendants<Border>(window.TaskbarLayerButton).First(border => border.Style == window.FindResource("LayerIconFrame"));
            double taskbarLayerIconLeft = taskbarLayerIcon.TranslatePoint(new System.Windows.Point(), window).X;
            var navigationIconColumns = navigationIcons.Select(icon => (Grid)icon.Parent).ToArray();
            Check(navigationIconColumns.All(column => Math.Abs(column.TranslatePoint(new System.Windows.Point(), window).X - taskbarLayerIconLeft) < .1) && navigationIcons.All(icon => Math.Abs(icon.ActualWidth - 32) < .1 && icon.Children.OfType<System.Windows.Shapes.Shape>().Any(shape => shape.RenderedGeometry.Bounds.Width <= 18 && shape.RenderedGeometry.Bounds.Height <= 18)) && Descendants<TextBlock>(window.NormalLayerButton).First(text => text.Text == "デフォルト").FontSize == 15 && Descendants<TextBlock>(window.SpaceLayerButton).First(text => text.Text == "Space").FontSize == 15, "sidebar command icons use a smaller vector mark centered in the same 32-pixel column as layer icons");
            var shortPicker = new ActionPickerWindow(null, "JIS", [new GestureDefinition { Name = "ウィンドウ操作" }], true);
            var longPicker = new ActionPickerWindow(null, "JIS", [new GestureDefinition { Name = "ウィンドウ操作" }], false);
            var orderedMajors = ActionPickerWindow.OrderMajorCategories(shortPicker.ActionsForTest.Select(x => x.MajorCategory));
            Check(!shortPicker.ActionsForTest.Any(x => x.Kind is ActionKind.Gesture or ActionKind.Disabled) && !longPicker.ActionsForTest.Any(x => x.Kind is ActionKind.Gesture or ActionKind.Disabled) && orderedMajors.Last() == "その他", "shortcut picker excludes gestures and disable while keeping Other as the last major category");
            var gesturePickerMenu = window.CreateGesturePickerMenu(window.KindBox);
            Check(gesturePickerMenu.Items.OfType<MenuItem>().Select(x => x.Header?.ToString()).SequenceEqual(["ウィンドウ操作"]), "gesture button opens a dedicated list containing only registered gestures");
            shortPicker.Close();
            longPicker.Close();
            var gestureManager = new GestureManagerWindow([new GestureDefinition { Name = "ウィンドウ操作", UpKind = ActionKind.Shortcut, UpValue = "Win+Up" }], [new Profile { Name = "標準", Mappings = [new Mapping { Input = "G", Kind = ActionKind.Gesture, Value = "ウィンドウ操作" }] }], [new MacroDefinition { Name = "マクロ1" }], "JIS") { Owner = window, ShowInTaskbar = false };
            gestureManager.Show();
            gestureManager.UpdateLayout();
            var gestureSlots = Descendants<System.Windows.Controls.Button>(gestureManager).Where(x => x.Tag is "Up" or "Down" or "Left" or "Right" or "Center").ToArray();
            var gestureLabels = Descendants<TextBlock>(gestureManager).Select(x => x.Text).ToArray();
            var gestureChoiceMenu = gestureManager.CreateActionTypeMenu(gestureSlots.First(x => x.Content?.ToString() == "選択…"), "Up");
            Check(gestureManager.TitleBarUsesDarkMode == MainWindow.IsWindowsAppDarkMode() && gestureSlots.Count(x => x.Content?.ToString() == "選択…") == 5 && gestureManager.ResultGestures[0].Name == "ウィンドウ操作" && gestureLabels.Contains("短押し") && !gestureLabels.Contains("センター") && gestureChoiceMenu.Items.OfType<MenuItem>().Select(x => x.Header?.ToString()).SequenceEqual(["別のキー", "プロファイル", "ショートカット", "文字列", "アプリ・パス", "マクロ"]), "gesture manager follows the Windows title-bar theme and exposes six action types for every direction and short press");
            var gestureCommandButtons = new[] { gestureManager.AddGestureButton, gestureManager.RenameGestureButton, gestureManager.DeleteGestureButton };
            Check(gestureCommandButtons.All(button => button.Content is Viewbox && Descendants<System.Windows.Shapes.Path>(button).Any(path => Equals(path.Stroke, button.Foreground))) && !gestureLabels.Any(text => text.Contains("ジェスチャーの入れ子は安全のため", StringComparison.Ordinal)), "gesture add, rename, and delete commands use theme-aware vector icons and the unwanted nesting notice is removed");
            CaptureForReview(gestureManager, "gesture-manager.png");
            gestureManager.Close();
            var emptyStateCenter = window.InspectorEmptyState.TranslatePoint(new System.Windows.Point(window.InspectorEmptyState.ActualWidth / 2, window.InspectorEmptyState.ActualHeight / 2), window.AssignmentPane).Y;
            Check(window.AssignmentPane.BorderThickness == new Thickness(0) && window.AssignmentPane.CornerRadius == new CornerRadius(0) && window.AssignmentPane.Effect == null && ReferenceEquals(window.AssignmentPane.Background, ThemeService.Brush("AppBackground")) && window.InspectorEmptyState.VerticalAlignment == System.Windows.VerticalAlignment.Center && Math.Abs(emptyStateCenter - window.AssignmentPane.ActualHeight / 2) < 24, "the inspector merges into the workspace background without an outer card or shadow while keeping its empty state centered");
            var keyColor = MainWindow.AssignmentColorFor(new Mapping { Kind = ActionKind.Key });
            var disabledColor = MainWindow.AssignmentColorFor(new Mapping { Kind = ActionKind.Disabled });
            var shortcutColor = MainWindow.AssignmentColorFor(new Mapping { Kind = ActionKind.Shortcut });
            var textColor = MainWindow.AssignmentColorFor(new Mapping { Kind = ActionKind.Text });
            var launchColor = MainWindow.AssignmentColorFor(new Mapping { Kind = ActionKind.Launch });
            var macroColor = MainWindow.AssignmentColorFor(new Mapping { Kind = ActionKind.Macro });
            Check(keyColor.R > keyColor.G && keyColor.G > keyColor.B && Math.Abs(disabledColor.R - disabledColor.G) < 15 && shortcutColor.G > shortcutColor.R * 2 && textColor.R > textColor.B * 2 && macroColor.R > macroColor.G * 2 && launchColor.B > launchColor.G && new[] { keyColor, disabledColor, shortcutColor, textColor, launchColor, macroColor }.Distinct().Count() == 6, "assigned keys use distinct orange, gray, green, yellow, purple, and red action colors");
            var updateForTest = new UpdateInfo(new Version(9, 9, 9), "9.9.9", new Uri("https://github.com/zitan-source/RELYR/releases/download/v9.9.9/RELYR-Update-9.9.9.exe"), new Uri("https://github.com/zitan-source/RELYR/releases/download/v9.9.9/RELYR-Update-9.9.9.exe.sha256"), null, "RELYR-Update-9.9.9.exe");
            window.ShowUpdateAvailableForTest(updateForTest);
            Check(window.UpdateBanner.Visibility == Visibility.Visible && window.UpdateBannerText.Text.Contains("v9.9.9") && window.UpdateAvailableButton.Content?.ToString() == "今すぐ更新", "available update appears as a prominent banner with an update action");
            window.DismissAvailableUpdateForTest();
            Check(window.UpdateBanner.Visibility == Visibility.Collapsed, "dismissing an update hides its banner");
            window.ShowUpdateAvailableForTest(updateForTest);
            Check(window.UpdateBanner.Visibility == Visibility.Collapsed, "a dismissed version stays hidden");
            var newerUpdate = updateForTest with
            {
                Version = new Version(9, 9, 10),
                VersionText = "9.9.10"
            };
            window.ShowUpdateAvailableForTest(newerUpdate);
            Check(window.UpdateBanner.Visibility == Visibility.Visible && window.UpdateBannerText.Text.Contains("v9.9.10"), "a newer version is shown after an older notification was dismissed");
            window.UpdateBanner.Visibility = Visibility.Collapsed;
            var closeSettings = new SettingsWindow(new AppConfig { GestureThresholdPixels = 14, LockCursorDuringGesture = false, ClockBackgroundMode = ClockBackgroundMode.Solid, ClockDisplayMode = ClockDisplayMode.FullDateAndTime, ClockBackgroundImage = @"C:\Images\clock.png", ClockSolidColor = "#123456", ClockShowOnAllMonitors = false, InputPanelOpacityPercent = 67 });
            closeSettings.Show();
            closeSettings.UpdateLayout();
            Check(closeSettings.ActiveWindowTargetBox.Content?.ToString() == "アクティブなウィンドウ" && closeSettings.CursorWindowTargetBox.Content?.ToString() == "マウスカーソル下のウィンドウ" && closeSettings.ActiveWindowTargetBox.IsChecked == true && closeSettings.CursorWindowTargetBox.IsChecked == false, "settings provides one clear target choice for close, maximize, snap, and other window actions");
            closeSettings.CursorWindowTargetBox.IsChecked = true;
            Check(closeSettings.SelectedWindowActionTarget == WindowActionTarget.WindowUnderCursor, "window-under-cursor target can be selected without changing the action itself");
            Check(closeSettings.GestureThreshold == 14 && closeSettings.GestureThresholdBox.Text == "14" && closeSettings.LockGestureCursorBox.IsChecked == false && !closeSettings.LockCursorDuringGesture, "gesture sensitivity and cursor-lock behavior are both visible and editable in the layer settings");
            closeSettings.LockGestureCursorBox.IsChecked = true;
            Check(closeSettings.LockCursorDuringGesture, "gesture cursor locking can be enabled without changing the sensitivity");
            var settingsCategories = closeSettings.CategoryList.Items.Cast<ListBoxItem>().ToArray();
            Check(settingsCategories[^2].Tag?.ToString() == "Update" && settingsCategories.Last().Tag?.ToString() == "Support" && settingsCategories.Any(x => x.Tag?.ToString() == "Overlay") && Descendants<System.Windows.Controls.CheckBox>(closeSettings.AppearancePanel).Contains(closeSettings.ProfileOverlayBox) && Descendants<Separator>(closeSettings.AppearancePanel).Any() && !Descendants<TextBlock>(closeSettings).Any(x => x.Text.Contains("仮想デスクトップ番号のすぐ上", StringComparison.Ordinal)), "appearance uses a divider between color mode and profile switching while keeping overlay and support options discoverable");
            closeSettings.SelectCategory("Appearance");
            closeSettings.UpdateLayout();
            CaptureForReview(closeSettings, "appearance-settings.png");
            closeSettings.SelectCategory("Layers");
            closeSettings.UpdateLayout();
            CaptureForReview(closeSettings, "layer-settings.png");
            Check(closeSettings.SelectedClockBackgroundMode == ClockBackgroundMode.Solid && closeSettings.SelectedClockDisplayMode == ClockDisplayMode.FullDateAndTime && closeSettings.ClockBackgroundImage == @"C:\Images\clock.png" && closeSettings.ClockSolidColor == "#123456" && !closeSettings.ClockShowOnAllMonitors && closeSettings.InputPanelOpacityPercent == 67, "overlay settings restore keypad opacity, solid color, clock image, date format, and monitor scope");
            closeSettings.SelectCategory("Overlay");
            closeSettings.UpdateLayout();
            Check(closeSettings.OverlayPanel.Visibility == Visibility.Visible && closeSettings.GeneralPanel.Visibility == Visibility.Collapsed && closeSettings.ClockDisplayModeBox.ItemContainerStyle != null && closeSettings.InputPanelOpacityValueText.Text == "67%" && closeSettings.ClockSolidPicker.IsEnabled && closeSettings.ClockSolidColorSample.Background is System.Windows.Media.SolidColorBrush sampleBrush && sampleBrush.Color == System.Windows.Media.Color.FromRgb(0x12, 0x34, 0x56) && closeSettings.ClockAllMonitorsBox.TranslatePoint(new System.Windows.Point(), closeSettings).Y > closeSettings.ClockDisplayModeBox.TranslatePoint(new System.Windows.Point(0, closeSettings.ClockDisplayModeBox.ActualHeight), closeSettings).Y, "overlay settings stack clock format above monitor scope with readable themed controls and a live solid-color sample");
            CaptureForReview(closeSettings, "clock-settings.png");
            closeSettings.Close();
            Check(window.TitleBarUsesDarkMode == MainWindow.IsWindowsAppDarkMode(), "title bar follows the Windows app theme");
            Check(ThemeService.Color("AccentBrush") == System.Windows.Media.Color.FromRgb(0x1D, 0xA7, 0x8C), "the application accent uses the restored RELYR green in every color mode");
            var neutralBackground = ThemeService.Color("AppBackground");
            Check(new[] { neutralBackground.R, neutralBackground.G, neutralBackground.B }.Max() - new[] { neutralBackground.R, neutralBackground.G, neutralBackground.B }.Min() <= 3, "the application background remains neutral instead of tinting the entire workspace green");
            var themedDialog = new AppDialog(window, "確認メッセージ", "RELYRの確認", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            themedDialog.Show();
            themedDialog.UpdateLayout();
            var dialogButtons = themedDialog.ButtonPanel.Children.OfType<System.Windows.Controls.Button>().ToArray();
            Check(themedDialog.Background == ThemeService.Brush("SurfaceBackground") && themedDialog.MessageText.Foreground == ThemeService.Brush("SecondaryText") && dialogButtons.Select(x => x.Content?.ToString()).SequenceEqual(["キャンセル", "いいえ", "はい"]) && dialogButtons.Select(x => x.ActualHeight).Distinct().Count() == 1, "all confirmations use the themed RELYR dialog with aligned app-style buttons");
            themedDialog.Close();
            var themedSettings = new SettingsWindow(new AppConfig()) { Owner = window, ShowInTaskbar = false };
            themedSettings.Show();
            themedSettings.UpdateLayout();
            Check(themedSettings.TitleBarUsesDarkMode == MainWindow.IsWindowsAppDarkMode(), "settings title bar follows the Windows app theme");
            themedSettings.Close();
            var numpadOverlay = new InputPanelOverlayWindow(false, 63);
            var extendedOverlay = new InputPanelOverlayWindow(true);
            numpadOverlay.Show();
            extendedOverlay.Show();
            numpadOverlay.UpdateLayout();
            extendedOverlay.UpdateLayout();
            var numpadInputs = numpadOverlay.InputButtons.Select(x => x.Tag?.ToString()).Where(x => x != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var extendedInputs = extendedOverlay.InputButtons.Select(x => x.Tag?.ToString()).Where(x => x != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
            string[] requiredNumpad = ["Back", "NumLock", "Divide", "Multiply", "Subtract", "Add", "Decimal", "NumPadEnter", "NumPad0", "NumPad1", "NumPad2", "NumPad3", "NumPad4", "NumPad5", "NumPad6", "NumPad7", "NumPad8", "NumPad9"];
            string[] requiredExtended = ["Insert", "Home", "PageUp", "Delete", "End", "PageDown", "PrintScreen", "ScrollLock", "Pause", "Left", "Up", "Right", "Down"];
            Check(requiredNumpad.All(numpadInputs.Contains) && requiredNumpad.All(extendedInputs.Contains) && requiredExtended.All(extendedInputs.Contains), "overlay keypads include a complete numpad with Backspace and the combined navigation/cursor layout");
            Check(numpadOverlay.InputButtons.Concat(extendedOverlay.InputButtons).All(x => x.Style == System.Windows.Application.Current.Resources["AppButtonStyle"]), "overlay keypads inherit the established RELYR button design");
            var standardOverlayKeys = extendedOverlay.InputButtons.Where(x => x.Tag?.ToString() is "Insert" or "Home" or "PageUp" or "Delete" or "End" or "PageDown" or "PrintScreen" or "ScrollLock" or "Pause" or "Left" or "Up" or "Right" or "Down" or "NumLock" or "Divide" or "Multiply" or "Subtract" or "NumPad7" or "NumPad8" or "NumPad9" or "NumPad4" or "NumPad5" or "NumPad6" or "NumPad1" or "NumPad2" or "NumPad3" or "Decimal").ToArray();
            Check(standardOverlayKeys.All(x => Math.Abs(x.ActualWidth - 54) < .1 && Math.Abs(x.ActualHeight - 52) < .1), "navigation, cursor, and normal numpad keys match the main JIS keyboard's 54 by 52 size");
            var scaledDrag = InputPanelOverlayWindow.PhysicalDragDeltaToDip(new Vector(150, 75), new DpiScale(1.5, 1.5));
            Check(Math.Abs(scaledDrag.X - 100) < .1 && Math.Abs(scaledDrag.Y - 50) < .1, "overlay dragging converts physical pointer movement to DPI-independent window movement without cursor drift");
            Check(Math.Abs(numpadOverlay.PanelOpacity - .63) < .001 && Math.Abs(extendedOverlay.PanelOpacity - .96) < .001, "numpad and extended keypad apply the configured opacity while keeping the established default");
            Check(Math.Abs(numpadOverlay.CloseButton.ActualWidth - numpadOverlay.CloseButton.ActualHeight) < .1 && numpadOverlay.CloseButton.Content is System.Windows.Shapes.Path, "overlay close control is a centered vector X inside an exact square button");
            CaptureForReview(numpadOverlay, "overlay-numpad.png");
            CaptureForReview(extendedOverlay, "overlay-extended.png");
            numpadOverlay.CloseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(!numpadOverlay.IsVisible, "overlay close button closes the panel on the first click");
            extendedOverlay.Close();
            OverlayService.Configure(() => new AppConfig { ClockBackgroundMode = ClockBackgroundMode.Solid, ClockSolidColor = "#123456", ClockShowOnAllMonitors = false }, () => true);
            OverlayService.TryShow(OverlayService.ClockAction);
            Pump(window);
            OverlayService.TryDismissFullScreenKeyboard(true);
            Pump(window);
            bool clockStayedVisibleDuringSourceRepeat = OverlayService.FullScreenVisible;
            OverlayService.TryDismissFullScreenKeyboard(false);
            Pump(window);
            bool clockStayedVisibleAfterSourceRelease = OverlayService.FullScreenVisible;
            OverlayService.TryDismissFullScreenKeyboard(true);
            Pump(window);
            Check(clockStayedVisibleDuringSourceRepeat && clockStayedVisibleAfterSourceRelease && !OverlayService.FullScreenVisible, "clock ignores source-key repeat, survives its release, and closes on the next fresh key press");
            var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0];
            var cursorOverlay = new ScreenOverlayWindow(primaryScreen, true, new AppConfig { ClockBackgroundMode = ClockBackgroundMode.Solid, ClockSolidColor = "#123456" });
            var clockTime = Descendants<TextBlock>(cursorOverlay).OrderByDescending(x => x.FontSize).First();
            Check(cursorOverlay.Cursor == System.Windows.Input.Cursors.None && cursorOverlay.ForceCursor && clockTime.FontFamily.Source == "Segoe UI Variable Display" && clockTime.FontStretch == FontStretches.Condensed, "clock hides the pointer and uses the narrow Segoe UI Variable display face");
            cursorOverlay.Close();
            var backgroundOnlyOverlay = new ScreenOverlayWindow(primaryScreen, false, new AppConfig { ClockBackgroundMode = ClockBackgroundMode.Solid, ClockSolidColor = "#123456" }, true);
            Check(!backgroundOnlyOverlay.IsClock && backgroundOnlyOverlay.Content is Grid backgroundGrid && backgroundGrid.Background is SolidColorBrush backgroundBrush && backgroundBrush.Color == System.Windows.Media.Color.FromRgb(0x12, 0x34, 0x56), "a monitor without clock text still receives the configured clock background");
            backgroundOnlyOverlay.Close();
            OverlayService.Configure(null);
            var firstRun = new SetupWindow { Owner = window, ShowInTaskbar = false };
            firstRun.Show();
            firstRun.UpdateLayout();
            Check(!Descendants<TextBlock>(firstRun).Any(x => x.Text.Contains("自動起動", StringComparison.Ordinal) || x.Text.Contains("サインイン時", StringComparison.Ordinal)), "first-run tutorial does not duplicate the installer startup choice");
            Check(firstRun.TitleBarUsesDarkMode == MainWindow.IsWindowsAppDarkMode() && firstRun.UsesDarkPalette == MainWindow.IsWindowsAppDarkMode(), "tutorial content and title bar follow the Windows app theme");
            Check(firstRun.TutorialAppIcon.Source != null, "tutorial header uses the packaged RELYR application icon");
            Check(Descendants<TextBlock>(firstRun.PageOne).Any(x => x.Text.Contains("Spaceを長押しで連打していた方へ", StringComparison.Ordinal)) && Descendants<TextBlock>(firstRun.PageOne).Any(x => x.Text.Contains("2回目を長押し", StringComparison.Ordinal)), "tutorial explains how former Space hold-repeat users can repeat Space");
            Check(firstRun.CurrentPage == 0 && firstRun.PageOne.Visibility == Visibility.Visible && firstRun.PageTwo.Visibility == Visibility.Collapsed && firstRun.PageThree.Visibility == Visibility.Collapsed, "tutorial opens on the layer introduction page");
            firstRun.ShowPageForTest(1);
            Check(firstRun.PageTwo.Visibility == Visibility.Visible && firstRun.BackButton.Visibility == Visibility.Visible && firstRun.NextButton.Content?.ToString() == "次へ", "tutorial moves to the three-step assignment page");
            firstRun.ShowPageForTest(2);
            Check(firstRun.PageThree.Visibility == Visibility.Visible && firstRun.NextButton.Content?.ToString() == "RELYRを使い始める" && Descendants<TextBlock>(firstRun.PageThree).Any(x => x.Text.Contains("CapsLock", StringComparison.Ordinal) && x.Text.Contains("Windowsの再起動が必要", StringComparison.Ordinal)), "tutorial finishes with the CapsLock restart requirement on the safety and recovery page");
            firstRun.Close();
            var manualTutorial = new SetupWindow(true);
            Check(manualTutorial.DoNotShowAgainBox.Visibility == Visibility.Collapsed && manualTutorial.SkipButton.Visibility == Visibility.Collapsed, "tutorial opened from settings does not change first-run preferences");
            manualTutorial.Close();
            var toolbarControls = new System.Windows.Controls.Control[] { window.ProfileBox, window.NewProfileButton, window.KeyboardLayoutBox, window.MultiSelectToggle, window.MultiCopyButton, window.MultiPasteButton, window.MultiDeleteButton, window.ToolbarSaveButton };
            Check(toolbarControls.All(x => Math.Abs(x.ActualHeight - 40) < .1), "toolbar controls use one consistent 40-pixel height");
            Check(window.TopToolbarPane.BorderThickness == new Thickness(0) && window.BottomStatusPane.BorderThickness == new Thickness(0) && window.LayerNavigationPane.BorderThickness == new Thickness(0) && ReferenceEquals(window.TopToolbarPane.Background, ThemeService.Brush("AppBackground")) && ReferenceEquals(window.BottomStatusPane.Background, ThemeService.Brush("AppBackground")) && ReferenceEquals(window.LayerNavigationPane.Background, ThemeService.Brush("AppBackground")) && !Descendants<Separator>(window.SidebarStatusPanel).Any(), "all outer panes share one flat background without full-height toolbar, sidebar, or status borders");
            double leftDividerRight = window.LeftPaneSoftDivider.TranslatePoint(new System.Windows.Point(window.LeftPaneSoftDivider.ActualWidth, 0), window.LayerNavigationPane).X;
            double rightDividerLeft = window.RightPaneSoftDivider.TranslatePoint(new System.Windows.Point(), window.MainContentGrid).X;
            Check(Math.Abs(window.LeftPaneSoftDivider.ActualWidth - 1) < .1 && Math.Abs(leftDividerRight - window.LayerNavigationPane.ActualWidth) < 1
                && window.LeftPaneSoftDivider.ActualHeight < window.LayerNavigationPane.ActualHeight - 48 && window.LeftPaneSoftDivider.Opacity < .7 && !window.LeftPaneSoftDivider.IsHitTestVisible
                && Math.Abs(window.RightPaneSoftDivider.ActualWidth - 1) < .1 && Math.Abs(rightDividerLeft - (window.MainContentGrid.ActualWidth - window.AssignmentPaneColumn.ActualWidth)) < 1
                && window.RightPaneSoftDivider.ActualHeight < window.MainContentGrid.ActualHeight - 48 && window.RightPaneSoftDivider.Opacity < .7 && !window.RightPaneSoftDivider.IsHitTestVisible,
                "left and right pane boundaries use restrained partial-height one-pixel dividers");
            var lowerActionButtons = new[] { window.MacroManagerButton, window.ProfileManagerButton, window.GestureManagerButton, window.DeckPanelManagerButton, window.AppSettingsButton };
            var sidebarIconCells = lowerActionButtons.Select(button => Descendants<Canvas>(button).Single(icon => Equals(icon.Tag, "SidebarIcon"))).ToArray();
            var lowerActionLabels = lowerActionButtons.Select(x => Descendants<TextBlock>(x).Last()).ToArray();
            double taskbarIconLeft = taskbarLayerIcon.TranslatePoint(new System.Windows.Point(), window).X;
            double taskbarLabelLeft = Descendants<TextBlock>(window.TaskbarLayerButton).First(x => x.Text == "タスクバー").TranslatePoint(new System.Windows.Point(), window).X;
            Check(lowerActionButtons.All(x => x.HorizontalContentAlignment == System.Windows.HorizontalAlignment.Stretch && x.Content is Grid grid && grid.ColumnDefinitions.Count == 2 && Math.Abs(grid.ColumnDefinitions[0].Width.Value - 32) < .1) && lowerActionLabels.All(x => x.TextAlignment == TextAlignment.Left && x.HorizontalAlignment == System.Windows.HorizontalAlignment.Stretch) && lowerActionLabels.Select(x => x.TranslatePoint(new System.Windows.Point(), window).X).All(x => Math.Abs(x - taskbarLabelLeft) < .1) && sidebarIconCells.Select(x => x.TranslatePoint(new System.Windows.Point(), window).X + x.ActualWidth / 2).All(x => Math.Abs(x - (taskbarIconLeft + taskbarLayerIcon.ActualWidth / 2)) < .1) && lowerActionLabels.Select(x => x.Text).SequenceEqual(["マクロ", "プロファイル", "ジェスチャー", "Deckパネル", "設定"]), "sidebar command icons and labels share the exact center and text planes of the layer rows");
            var sidebarDividers = new[] { window.KeyboardLayerDivider, window.MouseLayerDivider, window.ManagementDivider };
            Check(sidebarDividers.Select(x => x.TranslatePoint(new System.Windows.Point(), window).X).Max() - sidebarDividers.Select(x => x.TranslatePoint(new System.Windows.Point(), window).X).Min() < .1 && sidebarDividers.Select(x => x.ActualWidth).Max() - sidebarDividers.Select(x => x.ActualWidth).Min() < .1, "left-pane section dividers share identical left and right edges");
            window.DeckPanelManagerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.DeckWorkspace.Visibility == Visibility.Visible && window.KeyboardWorkspace.Visibility == Visibility.Collapsed && window.DeckLayoutListWorkspace.Visibility == Visibility.Visible && window.DeckEditorWorkspace.Visibility == Visibility.Collapsed && window.DeckLayoutCardsPanel.Children.Count == window.ConfigForTest.DeckLayouts.Count + 1, "Deck management opens a layout-card list with a permanent New card");
            var extraDeck = window.AddDeckLayoutForTest("配信用", 8, 2);
            Pump(window);
            Check(window.DeckLayoutCardsPanel.Children.OfType<System.Windows.Controls.Button>().Any(x => ReferenceEquals(x.Tag, extraDeck)) && window.ConfigForTest.DeckLayouts.Count >= 2, "multiple named Deck layouts appear in the list");
            var standardDeck = window.ConfigForTest.DeckLayouts.First();
            window.EditDeckLayoutForTest(standardDeck);
            Pump(window);
            var deckButtons = window.DeckManagementButtonsForTest.ToArray();
            foreach (var deckButton in deckButtons)
                deckButton.ApplyTemplate();
            Check(deckButtons.All(button => !Descendants<Border>(button).Any(border => Math.Abs(border.Height - 1) < .1)), "Deck buttons omit the decorative top highlight line");
            Check(window.DeckEditorWorkspace.Visibility == Visibility.Visible && window.DeckLayoutListWorkspace.Visibility == Visibility.Collapsed && deckButtons.Length == standardDeck.Columns * standardDeck.Rows && deckButtons.All(x => Math.Abs(x.Width - 54) < .1 && Math.Abs(x.Height - 52) < .1), "opening a layout shows its A-key-sized grid in the editor");
            Check(window.DeckKeypadInputButton.Visibility == Visibility.Visible && window.DetectInputButton.Visibility == Visibility.Collapsed && window.LongPressExpander.Visibility == Visibility.Collapsed && window.KindBox.Items.Cast<object>().All(x => !x.ToString()!.Contains("Gesture", StringComparison.Ordinal)) && window.KindBox.Items.Cast<object>().Select(x => x.GetType().GetProperty("Label")?.GetValue(x)?.ToString()).Contains("キーパッドから入力") && !window.KindBox.Items.Cast<object>().Select(x => x.GetType().GetProperty("Label")?.GetValue(x)?.ToString()).Contains("無効化"), "Deck editing keeps the inspector, replaces disable with keypad input, and excludes gestures and long press");
            bool deckDoubleClickOpenedActionPicker = false;
            window.ActionPickerRequestedForTest = (longPress, category) =>
            {
                deckDoubleClickOpenedActionPicker = !longPress;
                return null;
            };
            deckButtons[0].RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Controls.Control.MouseDoubleClickEvent });
            Pump(window);
            window.ActionPickerRequestedForTest = null;
            Check(deckDoubleClickOpenedActionPicker && window.InputName.Text == "Deck+01" && !window.ValueBox.IsKeyboardFocusWithin, "double-clicking a Deck slot selects only that slot and opens the same action picker as Shortcut");
            deckButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.ApplyCatalogActionForTest(new CatalogAction("テスト", "コピー", "", ActionKind.Shortcut, "Ctrl+C"));
            Pump(window);
            Check(!window.ValueBox.IsKeyboardFocusWithin && !window.IsEditingSelectedInputForTest && window.DestinationConfirmButton.Visibility == Visibility.Collapsed, "using a selected Deck action immediately completes editing and hides the contextual confirmation button");
            window.SetDeckButtonNameForTest("Deck+01", "コピー");
            standardDeck.Mappings.Add(new Mapping { Input = "Deck+45", Layer = "Deck", Kind = ActionKind.Key, Value = "Z", Description = "保持" });
            window.ApplyDeckSizeForTest(3, 3);
            window.ApplyDeckSizeForTest(9, 5);
            Pump(window);
            var deckTexts = Descendants<TextBlock>(window.DeckManagementGrid).Select(x => x.Text).ToArray();
            Check(standardDeck.Mappings.Any(x => x.Input == "Deck+01" && x.Value == "Ctrl+C" && x.Description == "コピー") && standardDeck.Mappings.Any(x => x.Input == "Deck+45" && x.Value == "Z" && x.Description == "保持"), "shrinking and expanding preserves hidden assignments and editable button names");
            window.DeckOpacitySlider.Value = 67;
            Pump(window);
            var deckCenter = window.DeckGridViewbox.TranslatePoint(new System.Windows.Point(window.DeckGridViewbox.ActualWidth / 2, 0), window.DeckEditorWorkspace).X;
            Check(window.DeckOpacityValueText.Text == "67%" && window.ConfigForTest.InputPanelOpacityPercent == 67 && Math.Abs(deckCenter - window.DeckEditorWorkspace.ActualWidth / 2) < 2, "Deck opacity is editable in place and the grid is centered in the upper editor area");
            Check(window.DeckWindowActionTargetForTest == WindowActionTarget.ActiveWindow, "Deck actions always target the previously active window instead of the overlay under the cursor");
            var colorPicker = new ThemeColorPickerWindow(System.Windows.Media.Color.FromRgb(0x12, 0x34, 0x56)) { Owner = window };
            colorPicker.Show();
            colorPicker.UpdateLayout();
            AppThemeMode pickerTheme = ThemeService.CurrentMode;
            ThemeService.Apply(AppThemeMode.Light);
            Pump(window);
            bool pickerFollowedLightTheme = colorPicker.Background is SolidColorBrush lightPickerBackground && lightPickerBackground.Color.R > 0xE0;
            ThemeService.Apply(pickerTheme);
            Pump(window);
            Check(colorPicker.UsesThemeSurfaceForTest && pickerFollowedLightTheme && colorPicker.PresetCountForTest == 12 && colorPicker.HexTextForTest == "#123456" && ThemeColorPickerWindow.TryParseHex("#ABCDEF", out var parsedPickerColor) && parsedPickerColor == System.Windows.Media.Color.FromRgb(0xAB, 0xCD, 0xEF), "the color picker follows dark and light RELYR themes and exposes concise presets with exact HEX input");
            CaptureForReview(colorPicker, "theme-color-picker.png");
            colorPicker.Close();
            string deckPreviewImage = Path.Combine(testConfigDirectory, "deck-preview.png");
            var previewBitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, new byte[16], 8);
            var previewEncoder = new PngBitmapEncoder();
            previewEncoder.Frames.Add(BitmapFrame.Create(previewBitmap));
            using (var previewStream = File.Create(deckPreviewImage))
                previewEncoder.Save(previewStream);
            string deckPreviewAudio = Path.Combine(testConfigDirectory, "deck-preview.wav");
            using (var audioStream = File.Create(deckPreviewAudio))
            using (var audioWriter = new BinaryWriter(audioStream))
            {
                audioWriter.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                audioWriter.Write(38);
                audioWriter.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                audioWriter.Write(16);
                audioWriter.Write((short)1);
                audioWriter.Write((short)1);
                audioWriter.Write(8000);
                audioWriter.Write(16000);
                audioWriter.Write((short)2);
                audioWriter.Write((short)16);
                audioWriter.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                audioWriter.Write(2);
                audioWriter.Write((short)0);
            }
            string deckPreviewVideo = Path.Combine(testConfigDirectory, "deck-preview.mp4");
            File.WriteAllBytes(deckPreviewVideo, [0]);
            string missingDeckFile = Path.Combine(testConfigDirectory, "moved-or-deleted.png");
            standardDeck.Mappings.Add(new Mapping { Input = "Deck+02", Layer = "Deck", DeckFilePath = deckPreviewImage });
            standardDeck.Mappings.Add(new Mapping { Input = "Deck+03", Layer = "Deck", DeckFilePath = deckPreviewAudio });
            standardDeck.Mappings.Add(new Mapping { Input = "Deck+04", Layer = "Deck", DeckIcon = "home" });
            standardDeck.Mappings.Add(new Mapping { Input = "Deck+05", Layer = "Deck", DeckFilePath = missingDeckFile });
            window.EditDeckLayoutForTest(standardDeck);
            Pump(window);
            var editorIconMenu = window.CreateDeckInputContextMenu("Deck+04");
            bool editorHasIconCommand = editorIconMenu.Items.OfType<MenuItem>().Select(item => item.Header).OfType<Grid>().SelectMany(grid => grid.Children.OfType<TextBlock>()).Any(text => text.Text == "アイコン変更...");
            Check(window.DeckManagementButtonsForTest[3].Content is TextBlock { Text: "\uE80F" } && editorHasIconCommand, $"Deck editor renders a selected preset and exposes icon change from right-click (content={window.DeckManagementButtonsForTest[3].Content?.GetType().Name}:{(window.DeckManagementButtonsForTest[3].Content as TextBlock)?.Text}, menu={editorHasIconCommand})");
            Check(window.DeckManagementButtonsForTest[4].Content is Grid missingEditorIcon && Descendants<System.Windows.Shapes.Path>(missingEditorIcon).Any(path => Equals(path.Stroke, ThemeService.Brush("DangerBrush"))) && window.DeckManagementButtonsForTest[4].ToolTip is System.Windows.Controls.ToolTip { Content: TextBlock { Text: "参照先のファイルが削除されたか、移動された可能性があります。" } }, "a missing Deck file automatically becomes a broken-link icon with a concise explanation in the editor");
            window.DeckManagementButtonsForTest[1].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(window.IsDeckEditorThumbnailOpenForTest, "clicking an image file in the Deck editor opens its thumbnail preview");
            window.DeckManagementButtonsForTest[2].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(window.IsDeckEditorAudioPlayingForTest && !window.IsDeckEditorThumbnailOpenForTest, "clicking an audio file in the Deck editor starts only its audio preview");
            window.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseMoveEvent });
            Check(!window.IsDeckEditorAudioPlayingForTest, "the first pointer movement stops Deck editor audio immediately");
            CaptureForReview(window, "deck-manager.png");
            Mapping? deckExecuted = null;
            (double Left, double Top)? savedDeckPosition = null;
            (double Width, double Height)? savedDeckSize = null;
            var overlayLayout = new DeckLayoutDefinition { Name = "標準Deck", Columns = 9, Rows = 5, Mappings = [new Mapping { Input = "Deck+01", Layer = "Deck", Kind = ActionKind.Shortcut, Value = "Ctrl+C", Description = "コピー" }] };
            var deckOverlayConfig = new AppConfig { InputPanelOpacityPercent = 67, DeckAutoHideAfterAction = false, DeckAutoHideOnPointerLeave = false, DeckPanelLeft = 120, DeckPanelTop = 140, DeckLayouts = [overlayLayout], Profiles = [new Profile { Name = "標準", DefaultDeckLayoutId = overlayLayout.Id }], SharedDefaultDeckLayoutId = overlayLayout.Id };
            overlayLayout.Mappings.Add(new Mapping { Input = "Deck+02", Layer = "Deck", DeckFilePath = deckPreviewImage });
            overlayLayout.Mappings.Add(new Mapping { Input = "Deck+03", Layer = "Deck", DeckFilePath = deckPreviewVideo });
            overlayLayout.Mappings.Add(new Mapping { Input = "Deck+04", Layer = "Deck", DeckFilePath = deckPreviewImage, DeckIcon = "search" });
            overlayLayout.Mappings.Add(new Mapping { Input = "Deck+05", Layer = "Deck", DeckFilePath = missingDeckFile });
            var backdropProbe = CreateBackdropProbeWindow();
            backdropProbe.Show();
            backdropProbe.UpdateLayout();
            var deckConstructionTime = System.Diagnostics.Stopwatch.StartNew();
            var deckOverlay = new DeckPanelOverlayWindow(deckOverlayConfig, map => deckExecuted = map, 67, (left, top) => savedDeckPosition = (left, top), overlayLayout, (width, height) => savedDeckSize = (width, height));
            deckConstructionTime.Stop();
            bool deckReadyBeforeShow = deckOverlay.DeckButtons.Count == 45;
            var deckShowTime = System.Diagnostics.Stopwatch.StartNew();
            deckOverlay.Show();
            deckShowTime.Stop();
            deckOverlay.UpdateLayout();
            Pump(window);
            PumpFor(TimeSpan.FromMilliseconds(150));
            deckOverlay.DeckButtons[2].RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            Pump(window);
            int videoPreviewsBeforeHide = deckOverlay.VideoPreviewCountForTest;
            deckOverlay.HideForReuse();
            var deckReopenTime = System.Diagnostics.Stopwatch.StartNew();
            deckOverlay.Show();
            deckReopenTime.Stop();
            Pump(window);
            Check(deckReadyBeforeShow && deckConstructionTime.Elapsed < TimeSpan.FromMilliseconds(250) && deckReopenTime.Elapsed < TimeSpan.FromMilliseconds(100) && deckOverlay.DeckButtons[1].Content is System.Windows.Controls.Image, $"Deck overlay presents complete controls on its first frame and reopens from cache without another initialization beat (construct={deckConstructionTime.ElapsedMilliseconds} ms, firstShow={deckShowTime.ElapsedMilliseconds} ms, reopen={deckReopenTime.ElapsedMilliseconds} ms)");
            int videoPreviewsAfterHide = deckOverlay.VideoPreviewCountForTest;
            deckOverlay.DeckButtons[2].RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            Pump(window);
            Check(videoPreviewsBeforeHide == 1 && videoPreviewsAfterHide == 0 && deckOverlay.VideoPreviewCountForTest == 1, "hiding the cached Deck releases its video player and the preview remains wired after reopening");
            Check(deckOverlay.DeckButtons[1].ToolTip is System.Windows.Controls.ToolTip { Placement: System.Windows.Controls.Primitives.PlacementMode.Custom, CustomPopupPlacementCallback: not null }, "Deck thumbnail preview is placed outside the Deck instead of covering adjacent keys");
            var deckOverlayBackground = (deckOverlay.Content as Border)?.Background as SolidColorBrush;
            Check(deckOverlayBackground != null && deckOverlayBackground.Color.R == ThemeService.Color("AppBackground").R && deckOverlayBackground.Color.G == ThemeService.Color("AppBackground").G && deckOverlayBackground.Color.B == ThemeService.Color("AppBackground").B, "Deck overlay default surface uses the same background tone as the main app");
            var overlayDeckView = Descendants<Viewbox>(deckOverlay).Single(view => view.Child is System.Windows.Controls.Primitives.UniformGrid);
            var overlayRoot = (Grid)overlayDeckView.Parent;
            Check(deckOverlay.HeaderBackgroundForTest == System.Windows.Media.Brushes.Transparent && deckOverlay.HeaderGripVisibleForTest && DeckPanelOverlayWindow.CanDragPanelFromForTest((Border)deckOverlay.Content) && !DeckPanelOverlayWindow.CanDragPanelFromForTest(deckOverlay.DeckButtons[0]) && deckOverlay.PanelPaddingForTest.Left == 12 && deckOverlay.PanelPaddingForTest.Top == 12 && deckOverlay.PanelPaddingForTest.Right == 12 && deckOverlay.PanelPaddingForTest.Bottom == 12 && overlayDeckView.Margin == new Thickness(0) && overlayDeckView.StretchDirection == StretchDirection.Both && overlayDeckView.HorizontalAlignment == System.Windows.HorizontalAlignment.Center && overlayDeckView.VerticalAlignment == VerticalAlignment.Center && Math.Abs(overlayDeckView.ActualWidth - overlayRoot.ActualWidth) < 1 && Math.Abs(overlayDeckView.ActualHeight - overlayRoot.RowDefinitions[2].ActualHeight) < 1, "large Decks show the grip, every non-button panel surface can drag, and the aspect-locked grid leaves no extra blank band");
            var cornerHits = new[] { new System.Windows.Point(1, 1), new System.Windows.Point(deckOverlay.ActualWidth - 1, 1), new System.Windows.Point(1, deckOverlay.ActualHeight - 1), new System.Windows.Point(deckOverlay.ActualWidth - 1, deckOverlay.ActualHeight - 1) }.Select(deckOverlay.ResizeHitTestForTest).ToArray();
            Check(deckOverlay.ResizeMode == ResizeMode.CanResize && cornerHits.All(hit => hit != 0) && cornerHits.Distinct().Count() == 4 && deckOverlay.ResizeHitTestForTest(new System.Windows.Point(deckOverlay.ActualWidth / 2, deckOverlay.ActualHeight / 2)) == 0, "all four Deck overlay corners expose distinct resize hit zones without consuming the center");
            Check(deckOverlay.DeckButtons.Count == 45 && deckOverlay.DeckButtons.All(x => x.IsEnabled && x.Background is SolidColorBrush && !Descendants<Border>(x).Any(border => border.Background is LinearGradientBrush)) && Math.Abs(deckOverlay.VisualOpacityForTest - .67) < .001 && !deckOverlay.ShowActivated && deckOverlay.UsesNoActivateStyle && Descendants<TextBlock>(deckOverlay).Any(x => x.Text == "コピー") && Math.Abs(deckOverlay.Left - 120) < .1 && Math.Abs(deckOverlay.Top - 140) < .1, "Deck overlay keeps its established translucent non-activating behavior with flat solid button faces");
            var overlayIconMenu = deckOverlay.DeckButtons[3].ContextMenu;
            bool overlayHasIconCommand = overlayIconMenu != null && overlayIconMenu.Items.OfType<MenuItem>().Select(item => item.Header).OfType<Grid>().SelectMany(grid => grid.Children.OfType<TextBlock>()).Any(text => text.Text == "アイコン変更...");
            Check(deckOverlay.DeckButtons[3].Content is TextBlock { Text: "\uE721" } && overlayHasIconCommand && DeckIconCatalog.CreateVisual(overlayLayout.Mappings.Single(x => x.Input == "Deck+04"), 34, false) != null, "Deck overlay uses the configured icon, offers the same right-click picker, and can build that icon for external drag feedback");
            Check(deckOverlay.DeckButtons[4].Content is Grid missingOverlayIcon && Descendants<System.Windows.Shapes.Path>(missingOverlayIcon).Any(path => Equals(path.Stroke, ThemeService.Brush("DangerBrush"))) && deckOverlay.DeckButtons[4].ToolTip is System.Windows.Controls.ToolTip { Content: TextBlock { Text: "参照先のファイルが削除されたか、移動された可能性があります。" } }, "a missing Deck file uses the same broken-link icon and compact warning in the overlay");
            deckOverlay.Refresh(40, true);
            PumpFor(TimeSpan.FromMilliseconds(90));
            Check(Math.Abs(deckOverlay.VisualOpacityForTest - .4) < .001, "Deck refresh immediately applies 40 percent panel opacity");
            deckOverlay.Refresh(100, true);
            PumpFor(TimeSpan.FromMilliseconds(90));
            Check(Math.Abs(deckOverlay.VisualOpacityForTest - 1) < .001, "Deck refresh immediately applies 100 percent panel opacity");
            deckOverlay.Refresh(67, true);
            deckOverlay.MoveAndPersistForTest(180, 210);
            Check(savedDeckPosition is { Left: 180, Top: 210 }, "moving the Deck overlay persists its last display position when dragging ends");
            deckOverlay.ResizeAndPersistForTest(deckOverlay.Width + 40, deckOverlay.Height + 30);
            Pump(window);
            Check(savedDeckSize is { } resizedDeck && Math.Abs(resizedDeck.Width - deckOverlay.ActualWidth) < .1 && Math.Abs(resizedDeck.Height - deckOverlay.ActualHeight) < .1 && Math.Abs(overlayDeckView.ActualWidth - overlayRoot.ActualWidth) < 1 && Math.Abs(overlayDeckView.ActualHeight - overlayRoot.RowDefinitions[2].ActualHeight) < 1, "resizing the Deck overlay preserves its Deck aspect without blank bands and persists its new size");
            deckOverlay.ResetSizeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(deckOverlay.ResetSizeButton.ToolTip?.ToString() == "元の大きさに戻す" && deckOverlay.ResetSizeButton.Content is System.Windows.Shapes.Path && Math.Abs(deckOverlay.ActualWidth - deckOverlay.DefaultWidthForTest) < 1 && Math.Abs(deckOverlay.ActualHeight - deckOverlay.DefaultHeightForTest) < 1, $"the header reset icon restores the Deck overlay's fitted original size (actual={deckOverlay.ActualWidth:F2}x{deckOverlay.ActualHeight:F2}, default={deckOverlay.DefaultWidthForTest:F2}x{deckOverlay.DefaultHeightForTest:F2})");
            var cursorBeforeDeckHover = System.Windows.Forms.Cursor.Position;
            try
            {
                var hoverCenter = deckOverlay.DeckButtons[0].PointToScreen(new System.Windows.Point(deckOverlay.DeckButtons[0].ActualWidth / 2, deckOverlay.DeckButtons[0].ActualHeight / 2));
                System.Windows.Forms.Cursor.Position = new System.Drawing.Point((int)hoverCenter.X, (int)hoverCenter.Y);
                PumpFor(TimeSpan.FromMilliseconds(180));
                CaptureForReview(deckOverlay, "deck-overlay.png");
            }
            finally { System.Windows.Forms.Cursor.Position = cursorBeforeDeckHover; }
            window.OpenSettingsForTest();
            Pump(window);
            var openSettings = window.SettingsWindowForTest;
            deckExecuted = null;
            deckOverlay.DeckButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(openSettings is { IsVisible: true } && deckOverlay.IsEnabled && deckExecuted?.Value == "Ctrl+C", "Deck overlay remains enabled and executes actions while the modeless settings window is open");
            openSettings?.Close();
            Pump(window);
            deckOverlay.DeckButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(deckExecuted?.Value == "Ctrl+C", "Deck overlay sends the selected action through the normal executor");
            Check(Math.Abs(deckOverlay.CloseButton.ActualWidth - 28) < .1 && Math.Abs(deckOverlay.CloseButton.ActualHeight - 30) < .1 && deckOverlay.CloseButton.InputHitTest(new System.Windows.Point(2, 2)) != null, "Deck overlay close control keeps a compact visual while its full transparent surface remains clickable");
            deckOverlay.CloseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(!deckOverlay.IsVisible, "Deck overlay closes from its top-right X button");
            var narrowDeckLayout = new DeckLayoutDefinition { Name = "縦長Deck", Columns = 1, Rows = 18 };
            var narrowDeckOverlay = new DeckPanelOverlayWindow(new AppConfig { DeckLayouts = [narrowDeckLayout] }, null, selectedLayout: narrowDeckLayout);
            narrowDeckOverlay.Show();
            narrowDeckOverlay.UpdateLayout();
            Pump(window);
            var narrowResetTopLeft = narrowDeckOverlay.ResetSizeButton.TranslatePoint(new System.Windows.Point(), narrowDeckOverlay);
            var narrowCloseBottomRight = narrowDeckOverlay.CloseButton.TranslatePoint(new System.Windows.Point(narrowDeckOverlay.CloseButton.ActualWidth, narrowDeckOverlay.CloseButton.ActualHeight), narrowDeckOverlay);
            Check(!narrowDeckOverlay.HeaderTitleVisibleForTest && !narrowDeckOverlay.HeaderGripVisibleForTest && narrowDeckOverlay.HeaderToolTipForTest == narrowDeckLayout.Name && DeckPanelOverlayWindow.CanDragPanelFromForTest((Border)narrowDeckOverlay.Content) && !DeckPanelOverlayWindow.CanDragPanelFromForTest(narrowDeckOverlay.DeckButtons[0]) && !narrowDeckOverlay.ResetSizeButton.IsVisible && narrowDeckOverlay.MoreButton.IsVisible && narrowDeckOverlay.CloseButton.IsVisible && narrowDeckOverlay.MoreButton.ActualWidth <= 24.1 && narrowDeckOverlay.CloseButton.ActualWidth <= 24.1 && narrowResetTopLeft.X >= 0 && narrowCloseBottomRight.X <= narrowDeckOverlay.ActualWidth - 6 && narrowCloseBottomRight.Y <= narrowDeckOverlay.ActualHeight + .1 && narrowDeckOverlay.HeaderContextMenuForTest?.Items.Count == 2, "a 1-by-18 Deck replaces separate pin/reset controls with an overflow menu, keeps close fully inset, and remains draggable from every non-key surface");
            CaptureForReview(narrowDeckOverlay, "deck-overlay-1x18.png");
            narrowDeckOverlay.Close();
            var autoHideLayout = new DeckLayoutDefinition { Name = "Auto hide", Columns = 3, Rows = 3, Mappings = [new Mapping { Input = "Deck+01", Layer = "Deck", Kind = ActionKind.Shortcut, Value = "Ctrl+C" }] };
            bool? savedPinned = null;
            Mapping? autoHideExecuted = null;
            var autoHideConfig = new AppConfig { DeckLayouts = [autoHideLayout], DeckAutoHideAfterAction = true, DeckAutoHideOnPointerLeave = true };
            var autoHideOverlay = new DeckPanelOverlayWindow(autoHideConfig, mapping => autoHideExecuted = mapping, selectedLayout: autoHideLayout, pinnedChanged: (_, pinned) => savedPinned = pinned);
            var autoHideCursor = System.Windows.Forms.Cursor.Position;
            try
            {
                var work = SystemParameters.WorkArea;
                autoHideOverlay.Left = work.Right - autoHideOverlay.Width - 8;
                autoHideOverlay.Top = work.Bottom - autoHideOverlay.Height - 8;
                System.Windows.Forms.Cursor.Position = new System.Drawing.Point((int)work.Left + 4, (int)work.Top + 4);
                autoHideOverlay.PrepareForShow();
                autoHideOverlay.Show();
                PumpFor(TimeSpan.FromMilliseconds(650));
                Check(autoHideOverlay.IsVisible, "an unpinned Deck shown away from the pointer remains visible until the pointer has entered it once");
                autoHideOverlay.DeckButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                PumpFor(TimeSpan.FromMilliseconds(350));
                Check(!autoHideOverlay.IsVisible && autoHideExecuted?.Value == "Ctrl+C", "an unpinned Deck hides after executing a button without losing the action");
                autoHideOverlay.PrepareForShow();
                autoHideOverlay.Show();
                autoHideOverlay.PinButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                autoHideOverlay.DeckButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                PumpFor(TimeSpan.FromMilliseconds(350));
                Check(autoHideOverlay.IsVisible && autoHideOverlay.IsPinnedForTest && savedPinned == true, "pinning keeps the Deck visible after actions and persists the choice for that layout");
                autoHideOverlay.PinButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                autoHideOverlay.ArmPointerAutoHideForTest();
                autoHideOverlay.SetDragActiveForTest(true);
                autoHideOverlay.RequestPointerAutoHideForTest();
                PumpFor(TimeSpan.FromMilliseconds(650));
                bool stayedDuringDrag = autoHideOverlay.IsVisible;
                autoHideOverlay.SetDragActiveForTest(false);
                PumpFor(TimeSpan.FromMilliseconds(650));
                Check(stayedDuringDrag && !autoHideOverlay.IsVisible && savedPinned == false, "pointer-leave auto-hide pauses throughout drag and resumes only after drag completion");
            }
            finally
            {
                System.Windows.Forms.Cursor.Position = autoHideCursor;
                autoHideOverlay.Close();
            }
            var maximumDeckLayout = new DeckLayoutDefinition
            {
                Name = "18×18",
                Columns = 18,
                Rows = 18,
                Mappings = Enumerable.Range(1, 324).Select(slot => new Mapping { Input = $"Deck+{slot:00}", Layer = "Deck", DeckFilePath = deckPreviewVideo }).ToList()
            };
            var maximumDeckOverlay = new DeckPanelOverlayWindow(
                new AppConfig { DeckLayouts = [maximumDeckLayout], DeckHoverPreviewsEnabled = true },
                null,
                positionChanged: (_, _) => throw new IOException("simulated position persistence failure"),
                selectedLayout: maximumDeckLayout,
                sizeChanged: (_, _) => throw new IOException("simulated size persistence failure"));
            Check(maximumDeckOverlay.Width <= SystemParameters.WorkArea.Width - 24 + .1 && maximumDeckOverlay.Height <= SystemParameters.WorkArea.Height - 24 + .1 && maximumDeckOverlay.MinWidth < maximumDeckOverlay.MaxWidth && maximumDeckOverlay.MinHeight < maximumDeckOverlay.MaxHeight, "an 18-by-18 Deck initially fits the work area and remains freely resizable");
            Check(maximumDeckOverlay.VideoPreviewCountForTest == 0, "an all-video 18-by-18 Deck allocates no media player before hover");
            maximumDeckOverlay.Show();
            double maximumDeckRestoreWidth = maximumDeckOverlay.ActualWidth, maximumDeckRestoreHeight = maximumDeckOverlay.ActualHeight;
            maximumDeckOverlay.ToggleSafeMaximizeForTest();
            Pump(window);
            Check(maximumDeckOverlay.IsSafelyMaximizedForTest && maximumDeckOverlay.WindowState == WindowState.Normal && maximumDeckOverlay.ActualWidth <= SystemParameters.WorkArea.Width - 24 + 1 && maximumDeckOverlay.ActualHeight <= SystemParameters.WorkArea.Height - 24 + 1, "Deck maximize stays in a bounded aspect-safe normal window instead of entering WPF transparent-window maximized state");
            maximumDeckOverlay.ToggleSafeMaximizeForTest();
            Pump(window);
            Check(!maximumDeckOverlay.IsSafelyMaximizedForTest && Math.Abs(maximumDeckOverlay.ActualWidth - maximumDeckRestoreWidth) < 1 && Math.Abs(maximumDeckOverlay.ActualHeight - maximumDeckRestoreHeight) < 1, "safe Deck maximize restores the previous overlay size");
            maximumDeckOverlay.DeckButtons[0].RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            Pump(window);
            maximumDeckOverlay.DeckButtons[1].RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            Pump(window);
            for (int hoverIndex = 2; hoverIndex < 26; hoverIndex++)
                maximumDeckOverlay.DeckButtons[hoverIndex].RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            for (int cycle = 0; cycle < 12; cycle++)
            {
                maximumDeckOverlay.HideForReuse();
                maximumDeckOverlay.Show();
                maximumDeckOverlay.ToggleSafeMaximizeForTest();
                maximumDeckOverlay.ToggleSafeMaximizeForTest();
            }
            Pump(window);
            Check(maximumDeckOverlay.VideoPreviewCountForTest == 0, "each cached Deck hide releases the active hover video player");
            maximumDeckOverlay.DeckButtons[25].RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            Pump(window);
            Check(maximumDeckOverlay.VideoPreviewCountForTest == 1 && DeckPanelLayout.CachedLargeThumbnailCountForTest == 0, "an all-video 18-by-18 Deck reuses one hover player, keeps large previews transient, and survives rapid hover/open/maximize cycles");
            maximumDeckOverlay.Close();
            var maximumAnimatedDeck = new DeckLayoutDefinition
            {
                Name = "18×18 アニメ",
                Columns = 18,
                Rows = 18,
                Mappings = Enumerable.Range(1, 324).Select(slot => new Mapping { Input = $"Deck+{slot:00}", Layer = "Deck", DeckIcon = DeckIconCatalog.AnimatedId(DeckIconCatalog.Presets[(slot - 1) % DeckIconCatalog.Presets.Count].Id) }).ToList()
            };
            var animatedDeckConstruction = System.Diagnostics.Stopwatch.StartNew();
            var maximumAnimatedOverlay = new DeckPanelOverlayWindow(new AppConfig { DeckLayouts = [maximumAnimatedDeck] }, null, selectedLayout: maximumAnimatedDeck);
            animatedDeckConstruction.Stop();
            maximumAnimatedOverlay.Show();
            PumpFor(TimeSpan.FromMilliseconds(350));
            int runningAnimatedIcons = maximumAnimatedOverlay.DeckButtons.Count(button => button.Content is TextBlock text && (text.HasAnimatedProperties || text.RenderTransform is TransformGroup transforms && transforms.Children.Any(transform => transform.HasAnimatedProperties)));
            Check(animatedDeckConstruction.Elapsed < TimeSpan.FromSeconds(1) && runningAnimatedIcons == 324, $"an 18-by-18 Deck keeps all 324 lightweight animated presets running without media decoders or a slow construction path (construct={animatedDeckConstruction.ElapsedMilliseconds} ms, running={runningAnimatedIcons})");
            maximumAnimatedOverlay.Close();
            backdropProbe.Close();
            window.ShowDeckLayoutListForTest();
            string defaultDeckBeforeSwitch = DeckPanelLayout.DefaultLayout(window.ConfigForTest)!.Id;
            string originalProfileName = window.CurrentProfileForTest.Name;
            var anotherProfile = window.ProfilesForTest.FirstOrDefault(x => !x.Name.Equals(originalProfileName, StringComparison.OrdinalIgnoreCase));
            if (anotherProfile != null)
                window.SwitchProfileForTest(anotherProfile.Name);
            Pump(window);
            Check(DeckPanelLayout.DefaultLayout(window.ConfigForTest)?.Id == defaultDeckBeforeSwitch && !window.ConfigForTest.UseSharedDeckPanel && !Descendants<System.Windows.Controls.CheckBox>(window).Any(x => x.Content?.ToString()?.Contains("すべてのプロファイルで共通のDeck", StringComparison.Ordinal) == true) && window.ProfileBox.IsEnabled, "Deck remains one global selection when profiles switch and no common-Deck checkbox is shown");
            if (anotherProfile != null)
                window.SwitchProfileForTest(originalProfileName);
            Check(MainWindow.TryResolveDeckLayoutSize("custom", "18", "18", out int dialogColumns, out int dialogRows) && dialogColumns == 18 && dialogRows == 18 && !MainWindow.TryResolveDeckLayoutSize("custom", "19", "5", out _, out _) && window.DeckSizePresetBox.Style == window.FindResource("ToolbarComboBoxStyle") && Math.Abs(window.DeckSizePresetBox.Height - 40) < .1, "Deck creation supports themed preset and custom 1x1 through 18x18 sizes");
            Check(deckOverlay.CloseButton.BorderThickness == new Thickness(0) && ReferenceEquals(deckOverlay.CloseButton.Background, System.Windows.Media.Brushes.Transparent), "Deck overlay close control renders only the X without a surrounding outline or surface");
            Check(!Descendants<TextBlock>(window).Any(x => x.Text is "一般権限" or "管理者モード"), "the obsolete process privilege label is absent from the main footer");
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.KeyboardWorkspace.Visibility == Visibility.Visible && window.DeckWorkspace.Visibility == Visibility.Collapsed && !window.KeyboardWorkspace.HasAnimatedProperties, "choosing a keyboard layer returns immediately from Deck management without animating the entire keyboard surface");
            window.DeckPanelManagerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.EditDeckLayoutForTest(standardDeck);
            window.ApplyDeckSizeForTest(18, 18);
            Pump(window);
            window.DeckManagementButtonsForTest[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            string originalLargeDeckValue = window.ValueBox.Text;
            window.ValueBox.Clear();
            window.ResetDeckVisualUpdateCountForTest();
            const string largeDeckTyping = "Ctrl+Shift+K";
            foreach (char character in largeDeckTyping)
                window.ValueBox.AppendText(character.ToString());
            Check(window.DeckVisualUpdateCountForTest == largeDeckTyping.Length, $"18x18 Deck typing refreshes only the selected button instead of all 324 buttons (updates={window.DeckVisualUpdateCountForTest})");
            window.ValueBox.Text = originalLargeDeckValue;
            window.ApplyDeckSizeForTest(9, 5);
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.DeckBackButton.Content is TextBlock && window.DeckBackButton.ToolTip?.ToString() == "Deck一覧へ戻る" && Math.Abs(window.DeckBackButton.ActualHeight - window.DeckLayoutNameBox.ActualHeight) < .1 && Math.Abs(window.DeckSaveButton.ActualHeight - window.DeckLayoutNameBox.ActualHeight) < .1 && Math.Abs(window.DeckLayoutNameBox.ActualHeight - 40) < .1, "Deck editor uses a compact icon-only back control and aligns back, name, and save controls to the same height");
            Check(window.ProfileBox.TranslatePoint(new System.Windows.Point(), window).X < window.KeyboardLayoutBox.TranslatePoint(new System.Windows.Point(), window).X, "profile context precedes the less-frequently changed keyboard layout");
            double profileLabelLeft = window.ProfileToolbarLabel.TranslatePoint(new System.Windows.Point(), window).X;
            double mainKeyboardLeft = window.KeyboardViewbox.TranslatePoint(new System.Windows.Point(), window).X;
            Check(Math.Abs(profileLabelLeft - mainKeyboardLeft) <= 2.1, $"the profile label aligns with the main keyboard's left edge (profile={profileLabelLeft:F1}, keyboard={mainKeyboardLeft:F1})");
            Check(ReferenceEquals(window.LayerNavigationPane.Parent, window.ShellDock) && window.ShellDock.Children.IndexOf(window.LayerNavigationPane) == 0 && Math.Abs(window.HeaderBrandColumn.Width.Value) < .1 && window.ToolbarSaveButton.TranslatePoint(new System.Windows.Point(window.ToolbarSaveButton.ActualWidth, 0), window.ToolbarPanel).X <= window.ToolbarPanel.ActualWidth + 1, "the sidebar spans from the client top while the toolbar begins to its right and stays on one line");
            Check(window.ToolbarPanel.Parent is Grid && !Descendants<ScrollViewer>(window.ToolbarPanel).Any(), "compact toolbar needs neither wrapping nor a horizontal slider");
            Check(window.NewProfileButton.Content?.ToString() == "＋" && Math.Abs(window.NewProfileButton.Width - 40) < .1 && Math.Abs(window.NewProfileButton.ActualWidth - window.NewProfileButton.ActualHeight) < .1, "new profile uses an exact square plus-only button");
            Check(!ReferenceEquals(window.EngineToggle.Parent, window.LeftBottomActions) && ReferenceEquals(window.MacroManagerButton.Parent, window.LeftBottomActions) && ReferenceEquals(window.ProfileManagerButton.Parent, window.LeftBottomActions) && ReferenceEquals(window.GestureManagerButton.Parent, window.LeftBottomActions) && ReferenceEquals(window.DeckPanelManagerButton.Parent, window.LeftBottomActions) && ReferenceEquals(window.AppSettingsButton.Parent, window.LeftBottomActions) && !window.ToolbarPanel.Children.Contains(window.EngineToggle) && !window.ToolbarPanel.Children.Contains(window.AutoSaveToggle), "macro and management buttons are fixed at lower left while engine and auto-save move to the status bar");
            var visibleText = Descendants<TextBlock>(window).Where(x => x.IsVisible).Select(x => x.Text).ToList();
            double brandLeft = window.ProductNameText.TranslatePoint(new System.Windows.Point(), window).X;
            Check(window.ProductNameText.Text == "RELYR" && window.ProductNameText.IsVisible && window.ProductNameText.HorizontalAlignment == System.Windows.HorizontalAlignment.Left && window.ProductNameText.TextAlignment == TextAlignment.Left && Math.Abs(window.ProductNameText.FontSize - 25) < .1 && Math.Abs(brandLeft - 22) < .1 && !visibleText.Any(x => x is "INPUT CUSTOMIZER" or "中央のキーまたはマウスを選び、右側で動作を設定します。" or "キーを選択して割り当て" or "緊急停止" or "Ctrl + Alt + Shift + F12") && !visibleText.Any(x => x.StartsWith("レイヤー・場所を選択：")), $"RELYR uses a clear sidebar brand and omits redundant assignment instructions ({brandLeft:F1}px)");
            Check(window.KeyboardViewbox.ActualWidth > 0 && window.KeyboardViewbox.ActualHeight > 0
                && window.KeyboardSurfaceCard.Background is SolidColorBrush keyboardSurface && keyboardSurface.Color == Colors.Transparent
                && window.KeyboardSurfaceCard.BorderThickness.Left == 0 && window.KeyboardSurfaceCard.CornerRadius.TopLeft == 0 && window.KeyboardSurfaceCard.Effect == null
                && window.MouseFrame.Background is SolidColorBrush mouseSurface && mouseSurface.Color == Colors.Transparent
                && window.MouseFrame.BorderThickness.Left == 0 && window.MouseFrame.CornerRadius.TopLeft == 0 && window.MouseFrame.Effect == null
                && window.SecondaryKeyboardPanel.Children.OfType<Border>().Where(x => x.Tag is string).All(x => x.Background is SolidColorBrush brush && brush.Color == Colors.Transparent && x.BorderThickness.Left == 0 && x.CornerRadius.TopLeft == 0 && x.Effect == null),
                "main keyboard, secondary groups and mouse merge into one flat workspace without outer cards");
            var materialKey = window.KeyboardPanel.Children.OfType<System.Windows.Controls.Button>().First(x => Equals(x.Tag, "A"));
            materialKey.ApplyTemplate();
            var materialKeyBorder = (Border)materialKey.Template.FindName("KeyBorder", materialKey)!;
            bool hasCheapKeyDepth = Descendants<Border>(materialKey).Any(x => x.Background is SolidColorBrush brush && brush.Color == ThemeService.Color("KeyDepthBrush"));
            Check(materialKeyBorder.Effect == null && hasCheapKeyDepth && materialKeyBorder.CornerRadius.TopLeft == 8 && !Descendants<Border>(materialKey).Any(x => x.Background is LinearGradientBrush) && window.MouseBody.Background is LinearGradientBrush && window.MouseBody.Effect is System.Windows.Media.Effects.DropShadowEffect, "buttons use flat solid faces and shared eight-pixel corners while the non-button mouse body keeps its directional shading");
            window.KeyboardLayoutBox.SelectedIndex = 1;
            window.UpdateLayout();
            var usEnter = window.KeyboardPanel.Children.OfType<System.Windows.Controls.Button>().First(x => Equals(x.Tag, "Enter"));
            Check(usEnter.Height < 70, "US layout switch shows rectangular Enter key");
            var usKeys = window.KeyboardPanel.Children.OfType<System.Windows.Controls.Button>().ToList();
            var usRowRightEdges = new[] { 44d, 100d, 156d, 212d, 268d }.Select(y => usKeys.Where(x => Math.Abs(Canvas.GetTop(x) - y) < .1).Max(x => Canvas.GetLeft(x) + x.Width)).ToArray();
            var usTopRow = usKeys.Where(x => Math.Abs(Canvas.GetTop(x)) < .1).OrderBy(Canvas.GetLeft).ToList();
            Check(usRowRightEdges.Max() - usRowRightEdges.Min() < .1 && Math.Abs(usRowRightEdges[0] - 900) < .1 && Math.Abs(Canvas.GetLeft(usTopRow[^1]) + usTopRow[^1].Width - 900) < .1, "US keyboard and Esc-to-Delete row share one clean right edge");
            double usKeyboardRight = window.KeyboardSurfaceCard.TranslatePoint(new System.Windows.Point(window.KeyboardSurfaceCard.ActualWidth, 0), window).X, usMouseRight = window.MouseFrame.TranslatePoint(new System.Windows.Point(window.MouseFrame.ActualWidth, 0), window).X;
            Check(Math.Abs(window.KeyboardPanel.Width - 900) < .1 && Math.Abs(usKeyboardRight - usMouseRight) < 1, "mouse group right edge aligns with the US keyboard workspace edge");
            Check(usTopRow.Count == 14 && Equals(usTopRow[^1].Tag, "Delete") && AdjacentGaps(usTopRow).All(x => Math.Abs(x - 4) < .1), "Delete follows F12 with uniform spacing");
            Check(usKeys.Any(x => Equals(x.Tag, "\\") && Equals(x.Content, "＼")), "US backslash key is unambiguous on Japanese Windows");
            window.KeyboardLayoutBox.SelectedIndex = 0;
            window.UpdateLayout();
            var jisKeys = window.KeyboardPanel.Children.OfType<System.Windows.Controls.Button>().ToList();
            var jisEnter = jisKeys.First(x => Equals(x.Tag, "Enter"));
            Check(Math.Abs(jisEnter.Height - 108) < .1, "JIS layout switch shows a correctly proportioned JIS Enter key");
            jisEnter.ApplyTemplate();
            var jisEnterShape = jisEnter.Template.FindName("EnterShape", jisEnter) as System.Windows.Shapes.Path;
            Check(jisEnter.Style == window.FindResource("JisEnterButton") && jisEnter.Clip != null && jisEnterShape is { StrokeThickness: 1, StrokeLineJoin: PenLineJoin.Round } && !window.KeyboardPanel.Children.OfType<System.Windows.Shapes.Path>().Any(), $"JIS Enter uses one rounded L-shaped key surface without a second jagged outline (shape={jisEnterShape != null}, clip={jisEnter.Clip != null}, extraOutline={window.KeyboardPanel.Children.OfType<System.Windows.Shapes.Path>().Any()})");
            Check(jisEnterShape != null && !jisEnterShape.Data.FillContains(new System.Windows.Point(1, 51)) && jisEnterShape.Data.FillContains(new System.Windows.Point(9, 51)) && !jisEnterShape.Data.FillContains(new System.Windows.Point(1, 53)) && !jisEnterShape.Data.FillContains(new System.Windows.Point(19, 53)) && jisEnterShape.Data.FillContains(new System.Windows.Point(21, 53)), "JIS Enter rounds both the highlighted outer step and the inner transition with eight-pixel corners");
            var jisEnterDepth = Descendants<System.Windows.Shapes.Path>(jisEnter).FirstOrDefault(x => x.RenderTransform is TranslateTransform);
            var jisEnterClip = jisEnter.Clip;
            Check(jisEnterShape != null && Math.Abs(jisEnterShape.Data.Bounds.Width - 160) < .1 && Math.Abs(jisEnterShape.Data.Bounds.Height - 106.86) < .01
                && jisEnterDepth?.RenderTransform is TranslateTransform { Y: 1 } && Math.Abs(jisEnterDepth.Data.Bounds.Height - 106.86) < .01
                && jisEnterClip != null && jisEnterClip.Bounds.Width == 160 && Math.Abs(jisEnterClip.Bounds.Height - 106.86) < .01,
                "JIS Enter matches ordinary keys with an eight-pixel corner and exactly one pixel of lower depth");
            Check(jisKeys.Any(x => Equals(x.Tag, "_") && Equals(x.Content, "＼  _")), "JIS Ro key uses backslash symbol without lone hiragana");
            var jisTopRow = jisKeys.Where(x => Math.Abs(Canvas.GetTop(x)) < .1).OrderBy(Canvas.GetLeft).ToList();
            Check(jisTopRow.Count == 14 && Equals(jisTopRow[^1].Tag, "Delete") && Math.Abs(Canvas.GetLeft(jisTopRow[^1]) + jisTopRow[^1].Width - 942) < .1 && AdjacentGaps(jisTopRow).All(x => Math.Abs(x - 4) < .1), "JIS Esc-to-Delete row aligns with the 942-pixel main key block");
            double jisKeyboardRight = window.KeyboardSurfaceCard.TranslatePoint(new System.Windows.Point(window.KeyboardSurfaceCard.ActualWidth, 0), window).X, jisMouseRight = window.MouseFrame.TranslatePoint(new System.Windows.Point(window.MouseFrame.ActualWidth, 0), window).X;
            Check(Math.Abs(window.KeyboardPanel.Width - 942) < .1 && Math.Abs(jisKeyboardRight - jisMouseRight) < 1, "mouse group right edge aligns with the JIS keyboard workspace edge");
            var mainRows = new[] { 44d, 100d, 156d, 212d, 268d };
            Check(mainRows.Zip(mainRows.Skip(1), (a, b) => b - a - 52).All(x => Math.Abs(x - 4) < .1), "main keyboard rows use a uniform four-pixel vertical gap");
            var numberRow = jisKeys.Where(x => Math.Abs(Canvas.GetTop(x) - 44) < .1).OrderBy(Canvas.GetLeft).ToList();
            var qRow = jisKeys.Where(x => Math.Abs(Canvas.GetTop(x) - 100) < .1 && !Equals(x.Tag, "Enter")).OrderBy(Canvas.GetLeft).ToList();
            Check(AdjacentGaps(numberRow).All(x => Math.Abs(x - 4) < .1) && qRow.Count == 13 && AdjacentGaps(qRow).All(x => x >= 4 - .1), "main keyboard keys preserve non-overlapping horizontal gutters");
            var backspace = numberRow[^1];
            var upperBracket = jisKeys.First(x => Equals(x.Tag, "["));
            var rightBracket = jisKeys.First(x => Equals(x.Tag, "]"));
            var rightShift = jisKeys.First(x => Equals(x.Tag, "RightShift"));
            var rightCtrl = jisKeys.First(x => Equals(x.Tag, "RightCtrl"));
            rightShift.ApplyTemplate();
            var rightShiftFace = (Border)rightShift.Template.FindName("KeyBorder", rightShift)!;
            double enterToShiftFaceGap = Canvas.GetTop(rightShift) - (Canvas.GetTop(jisEnter) + jisEnterShape!.Data.Bounds.Height);
            double shiftToCtrlFaceGap = Canvas.GetTop(rightCtrl) - (Canvas.GetTop(rightShift) + rightShiftFace.ActualHeight);
            Check(Math.Abs(enterToShiftFaceGap - shiftToCtrlFaceGap) < .1, $"JIS Enter-to-Shift visible gap exactly matches the Shift-to-Ctrl visible gap (enter={enterToShiftFaceGap:F2}, regular={shiftToCtrlFaceGap:F2}, shiftFace={rightShiftFace.ActualHeight:F2})");
            Check(Math.Abs(Canvas.GetTop(jisEnter) - (Canvas.GetTop(backspace) + backspace.Height) - 4) < .1 && Math.Abs(Canvas.GetLeft(jisEnter) - (Canvas.GetLeft(upperBracket) + upperBracket.Width) - 4) < .1 && Math.Abs(Canvas.GetLeft(jisEnter) + 24 - (Canvas.GetLeft(rightBracket) + rightBracket.Width) - 6) < .1 && Math.Abs(Canvas.GetTop(rightShift) - (Canvas.GetTop(jisEnter) + jisEnter.Height) - 4) < .1, "JIS Enter preserves standard outer gutters and widens the stroked inner gutter to the same visible width");
            var functionKeys = jisKeys.Where(x => x.Tag?.ToString() is "Esc" or "F1" or "F2" or "F3" or "F4" or "F5" or "F6" or "F7" or "F8" or "F9" or "F10" or "F11" or "F12" or "F14" or "F15" or "F16" or "F17" or "F18" or "F19" or "F20" or "F21" or "F22" or "F23" or "F24").ToList();
            Check(functionKeys.Count == 24 && functionKeys.All(x => Math.Abs(x.ActualHeight - 36) < .1), "top and extended function keys share one compact rendered height");
            var f1 = functionKeys.First(x => Equals(x.Tag, "F1"));
            var extendedFunctions = functionKeys.Where(x => x.Tag?.ToString() is "F14" or "F15" or "F16" or "F17" or "F18" or "F19" or "F20" or "F21" or "F22" or "F23" or "F24").ToList();
            var topToMainGap = 44 - (Canvas.GetTop(f1) + f1.ActualHeight);
            var mainToExtendedGap = Canvas.GetTop(extendedFunctions[0]) - (268 + 52);
            Check(!Descendants<TextBlock>(window.KeyboardPanel).Any(x => x.Text == "拡張ファンクションキー") && extendedFunctions.All(x => Math.Abs(x.Width - f1.Width) < .1 && Math.Abs(x.ActualHeight - f1.ActualHeight) < .1 && Math.Abs(Canvas.GetTop(x) - 328) < .1) && Math.Abs(topToMainGap - 8) < .1 && Math.Abs(mainToExtendedGap - 8) < .1, "upper and lower function rows use the same visible eight-pixel separation from the main keyboard");
            Check(window.NormalLayerButton.ActualHeight is >= 48 and <= 54 && window.SpaceLayerButton.ActualHeight is >= 48 and <= 54, "layer cards retain readable two-line content at a compact height");
            window.Width = 1850;
            window.Height = 1000;
            window.UpdateLayout();
            var layerButtons = window.LayerButtonsPanel.Children.OfType<System.Windows.Controls.Button>().ToList();
            Check(ReferenceEquals(window.LayerButtonsPanel.Parent, window.LayerNavigationHost) && layerButtons.Select(x => Math.Round(x.TranslatePoint(new System.Windows.Point(), window.LayerButtonsPanel).Y)).Distinct().Count() == 7, "layer buttons stay vertically arranged in the left pane");
            Check(window.LayerButtonsPanel.Children.IndexOf(window.KeyboardLayerCategory) < window.LayerButtonsPanel.Children.IndexOf(window.NormalLayerButton) && window.LayerButtonsPanel.Children.IndexOf(window.MouseLayerCategory) < window.LayerButtonsPanel.Children.IndexOf(window.RightMouseLayerButton) && window.LayerButtonsPanel.Children.IndexOf(window.WindowsLayerCategory) < window.LayerButtonsPanel.Children.IndexOf(window.TaskbarLayerButton), "layer buttons are grouped into keyboard, mouse and Windows categories");
            Check(layerButtons.All(x => x.HorizontalContentAlignment == System.Windows.HorizontalAlignment.Stretch && x.Content is Grid && Descendants<Border>(x).Any(border => border.Style == window.FindResource("LayerIconFrame")) && Descendants<TextBlock>(x).Skip(1).Any(description => description.IsVisible)) && Descendants<TextBlock>(window.NormalLayerButton).Any(x => x.Text == "デフォルト") && Descendants<TextBlock>(window.SpaceLayerButton).Any(x => x.Text == "Space") && Descendants<TextBlock>(window.CapsLockLayerButton).Any(x => x.Text == "CapsLock") && Descendants<TextBlock>(window.RightMouseLayerButton).Any(x => x.Text == "右クリック") && Descendants<TextBlock>(window.ForwardMouseLayerButton).Any(x => x.Text == "進む") && Descendants<TextBlock>(window.BackMouseLayerButton).Any(x => x.Text == "戻る") && Descendants<TextBlock>(window.TaskbarLayerButton).Any(x => x.Text == "タスクバー") && window.KeyboardLayerCategory.Text == "KEY LAYER" && window.MouseLayerCategory.Text == "MOUSE LAYER" && window.WindowsLayerCategory.Text == "SYSTEM" && Descendants<System.Windows.Shapes.Ellipse>(window.NormalLayerButton).Any(x => Equals(x.Tag, "LayerActiveIndicator") && x.Visibility == Visibility.Visible), "layer cards stretch their content grid so every active status dot uses the same fixed right column");
            var layerDots = layerButtons.Select(button => Descendants<System.Windows.Shapes.Ellipse>(button).Single(dot => Equals(dot.Tag, "LayerActiveIndicator"))).ToArray();
            var layerDotCenters = new List<double>();
            foreach (var dot in layerDots)
            {
                dot.Visibility = Visibility.Visible;
                window.UpdateLayout();
                layerDotCenters.Add(dot.TranslatePoint(new System.Windows.Point(dot.ActualWidth / 2, 0), window).X);
                dot.Visibility = Visibility.Collapsed;
            }
            window.RefreshLayerButtonsForTest();
            output.WriteLine("INFO layer dot centers=" + string.Join(", ", layerButtons.Zip(layerDotCenters, (button, center) => $"{button.Tag}:{center:F2}/w{button.ActualWidth:F2}/cw{((FrameworkElement)button.Content).ActualWidth:F2}")));
            Check(layerDotCenters.Max() - layerDotCenters.Min() < .1, $"every layer active dot shares one horizontal position (spread={layerDotCenters.Max() - layerDotCenters.Min():F2})");
            Check(layerButtons.All(x => x.ActualWidth >= window.LayerNavigationHost.ActualWidth - 8), "layer buttons fill the usable width of the left pane");
            Check(window.LayerNavigationPane.ActualWidth is >= 200 and <= 244 && Math.Abs(window.LayerNavigationColumn.ActualWidth) < .1 && window.AssignmentPaneColumn.ActualWidth is >= 288 and <= 340, "top-spanning navigation and inspector stay within their responsive width bands");
            Check(f1.TranslatePoint(new System.Windows.Point(), window.KeyboardViewbox).Y <= 1, "main keyboard is top-aligned without excessive blank space");
            var maximizedMainKey = window.KeyboardPanel.Children.OfType<System.Windows.Controls.Button>().First(x => Equals(x.Tag, "A"));
            var maximizedNumpadKey = window.SecondaryKeyboardPanel.Children.OfType<System.Windows.Controls.Button>().First(x => Equals(x.Tag, "NumPad7"));
            var mainLeftKey = window.KeyboardPanel.Children.OfType<System.Windows.Controls.Button>().First(x => Equals(x.Tag, "Esc"));
            var navigationLeftKey = window.SecondaryKeyboardPanel.Children.OfType<System.Windows.Controls.Button>().First(x => Equals(x.Tag, "Insert"));
            var navigationHeading = window.SecondaryKeyboardPanel.Children.OfType<TextBlock>().First(x => x.Text == "ナビゲーション");
            double mainLeft = mainLeftKey.TranslatePoint(new System.Windows.Point(), window.WorkspaceGrid).X;
            double navigationLeft = navigationLeftKey.TranslatePoint(new System.Windows.Point(), window.WorkspaceGrid).X;
            double navigationHeadingLeft = navigationHeading.TranslatePoint(new System.Windows.Point(), window.WorkspaceGrid).X;
            Check(Math.Abs(mainLeft - navigationLeft) < 1 && Math.Abs(mainLeft - navigationHeadingLeft) < 1 && Math.Abs(window.SecondaryKeyboardViewbox.Margin.Left - window.KeyboardSurfaceCard.Padding.Left) < .1,
                $"main keyboard and navigation share one exact left edge ({mainLeft:F1}/{navigationLeft:F1}/{navigationHeadingLeft:F1})");
            Check(RenderedWidth(maximizedMainKey, window) <= RenderedWidth(maximizedNumpadKey, window) * 1.35, "main and lower keyboard controls stay visually balanced when maximized");
            var renderedMouseWidth = RenderedWidth(window.MousePanel, window);
            Check(Math.Abs(window.MouseFrame.ActualHeight - window.SecondaryKeyboardViewbox.ActualHeight) < 1 && renderedMouseWidth <= window.MouseColumn.ActualWidth - 16, $"mouse controls share the lower keyboard height and never overpower its column ({window.MouseFrame.ActualHeight:F1}/{window.SecondaryKeyboardViewbox.ActualHeight:F1}, {renderedMouseWidth:F1}/{window.MouseColumn.ActualWidth:F1})");
            Check(Math.Abs(window.LowerInputGrid.ActualWidth - window.KeyboardSurfaceCard.ActualWidth) < 1 && window.MouseHost.TranslatePoint(new System.Windows.Point(window.MouseHost.ActualWidth, 0), window.WorkspaceGrid).X <= window.KeyboardSurfaceCard.TranslatePoint(new System.Windows.Point(window.KeyboardSurfaceCard.ActualWidth, 0), window.WorkspaceGrid).X + 1, "mouse stays inside the keyboard workspace right edge");
            window.Width = 1160;
            window.Height = 1250;
            window.UpdateLayout();
            Pump(window);
            var portraitNumpadFrame = window.SecondaryKeyboardPanel.Children.OfType<Border>().First(x => Equals(x.Tag, "テンキー"));
            var portraitNumpadBounds = portraitNumpadFrame.TransformToAncestor(window).TransformBounds(new Rect(0, 0, portraitNumpadFrame.ActualWidth, portraitNumpadFrame.ActualHeight));
            double portraitLowerTop = window.LowerInputGrid.TranslatePoint(new System.Windows.Point(), window).Y, portraitKeyboardBottom = window.KeyboardSurfaceCard.TranslatePoint(new System.Windows.Point(0, window.KeyboardSurfaceCard.ActualHeight), window).Y, portraitMouseTop = window.MouseFrame.TranslatePoint(new System.Windows.Point(), window).Y;
            Check(Math.Abs(portraitLowerTop - portraitKeyboardBottom - 16) < 1 && Math.Abs(portraitMouseTop - portraitNumpadBounds.Top) < 1 && Math.Abs(window.MouseFrame.ActualHeight - portraitNumpadBounds.Height) < 1 && window.MouseFrame.TranslatePoint(new System.Windows.Point(0, window.MouseFrame.ActualHeight), window.LowerInputGrid).Y <= window.LowerInputGrid.ActualHeight + 1, "portrait layout uses whitespace to separate the main and lower controls while keeping navigation and mouse aligned");
            CaptureForReview(window, "portrait-main.png");
            window.Width = 800;
            window.Height = 620;
            window.UpdateLayout();
            Check(!Descendants<TextBlock>(window.LayerNavigationHost).Any(x => x.Text is "レイヤー選択" or "操作モードを切り替えます"), "redundant layer-selection heading and hint are removed from the compact left pane");
            var lastLayerPosition = window.TaskbarLayerButton.TranslatePoint(new System.Windows.Point(), window.MainContentGrid);
            var lastLayerBottom = lastLayerPosition.Y + window.TaskbarLayerButton.ActualHeight;
            var leftActionsTop = window.LeftBottomActions.TranslatePoint(new System.Windows.Point(), window.MainContentGrid).Y;
            double leftActionsBottom = leftActionsTop + window.LeftBottomActions.ActualHeight;
            double statusTop = window.SidebarStatusPanel.TranslatePoint(new System.Windows.Point(), window.MainContentGrid).Y;
            Check(layerButtons.All(x => x.ActualWidth > 0 && x.ActualWidth <= window.LayerNavigationPane.ActualWidth && x.ActualHeight >= 48) && ReferenceEquals(window.LeftBottomActions.Parent, window.LayerNavigationGrid) && Grid.GetRow(window.LeftBottomActions) == 2 && leftActionsBottom <= statusTop + 1 && window.LayerNavigationScrollViewer.ScrollableHeight > 0, $"left navigation keeps commands fixed at the bottom above status while layers scroll independently ({lastLayerBottom:F0}, {leftActionsTop:F0}, {statusTop:F0})");
            var clippedLayerLabels = layerButtons.SelectMany(button => Descendants<TextBlock>(button).Where(text => text.IsVisible).Select(text => (button, text))).Where(pair => pair.text.TranslatePoint(new System.Windows.Point(pair.text.ActualWidth, 0), pair.button).X > pair.button.ActualWidth + 1).Select(pair => pair.text.Text).ToArray();
            Check(clippedLayerLabels.Length == 0, "layer labels stay fully inside their buttons at the minimum window width" + (clippedLayerLabels.Length == 0 ? "" : " (clipped: " + string.Join(",", clippedLayerLabels) + ")"));
            var renderedMouseBounds = window.MousePanel.TransformToAncestor(window.MouseHost).TransformBounds(new Rect(0, 0, window.MousePanel.ActualWidth, window.MousePanel.ActualHeight));
            Check(renderedMouseBounds.Left >= -1 && renderedMouseBounds.Top >= -1 && renderedMouseBounds.Right <= window.MouseHost.ActualWidth + 1 && renderedMouseBounds.Bottom <= window.MouseHost.ActualHeight + 1, "complete mouse diagram remains visible at the minimum window size");
            var secondaryKeys = window.SecondaryKeyboardPanel.Children.OfType<System.Windows.Controls.Button>().ToList();
            Check(secondaryKeys.Any(x => Equals(x.Tag, "Insert")) && secondaryKeys.Any(x => Equals(x.Tag, "NumPad0")) && !window.KeyboardPanel.Children.OfType<System.Windows.Controls.Button>().Any(x => Equals(x.Tag, "Insert")), "navigation and numpad keys are placed below the main keyboard");
            var lowerGroupTitles = Descendants<TextBlock>(window.LowerInputGrid).Where(x => x.Text is "ナビゲーション" or "テンキー" or "カーソルキー" or "マウス").Select(x => x.Text).ToHashSet();
            Check(window.LowerInputGrid.Children.Contains(window.SecondaryKeyboardViewbox) && window.LowerInputGrid.Children.Contains(window.MouseHost) && window.SecondaryKeyboardPanel.Children.OfType<Border>().Count() == 3 && window.LowerInputGrid.Children.OfType<Border>().Count() == 1 && new[] { "ナビゲーション", "テンキー", "カーソルキー", "マウス" }.All(lowerGroupTitles.Contains), "lower input groups retain four headings while their layout regions remain invisible");
            var baseA = jisKeys.First(x => Equals(x.Tag, "A"));
            var spanningNumpadTags = new[] { "Add", "NumPadEnter", "NumPad0" };
            Check(secondaryKeys.Where(x => !spanningNumpadTags.Contains(x.Tag?.ToString())).All(x => Math.Abs(x.Width - baseA.Width) < .1 && Math.Abs(x.Height - baseA.Height) < .1), "every ordinary navigation, numpad and cursor button exactly matches the A-key size");
            var renderedSecondaryKey = secondaryKeys.First(x => !spanningNumpadTags.Contains(x.Tag?.ToString()));
            Check(secondaryKeys.Where(x => !spanningNumpadTags.Contains(x.Tag?.ToString())).All(x => Math.Abs(RenderedWidth(x, window) - RenderedWidth(baseA, window)) < .1 && Math.Abs(RenderedHeight(x, window) - RenderedHeight(baseA, window)) < .1), $"every ordinary lower keyboard button renders at exactly the same on-screen size as the A key (A={RenderedWidth(baseA, window):F2}x{RenderedHeight(baseA, window):F2}, lower={RenderedWidth(renderedSecondaryKey, window):F2}x{RenderedHeight(renderedSecondaryKey, window):F2}, view={window.SecondaryKeyboardViewbox.ActualWidth:F2}x{window.SecondaryKeyboardViewbox.ActualHeight:F2})");
            var numpadZero = secondaryKeys.First(x => Equals(x.Tag, "NumPad0"));
            var numpadEnter = secondaryKeys.First(x => Equals(x.Tag, "NumPadEnter"));
            var numpadAdd = secondaryKeys.First(x => Equals(x.Tag, "Add"));
            Check(Math.Abs(numpadZero.Width - (baseA.Width * 2 + 4)) < .1 && Math.Abs(numpadZero.Height - baseA.Height) < .1 && new[] { numpadAdd, numpadEnter }.All(x => Math.Abs(x.Width - baseA.Width) < .1 && Math.Abs(x.Height - (baseA.Height * 2 + 4)) < .1), "numpad 0, plus and Enter use standard gap-preserving spans");
            Check(NoOverlaps(secondaryKeys), "standard numpad spans fill the grid without overlapping any lower key");
            var lowerFrames = window.SecondaryKeyboardPanel.Children.OfType<Border>().OrderBy(Canvas.GetLeft).ToList();
            Check(lowerFrames.Zip(lowerFrames.Skip(1), (a, b) => Canvas.GetLeft(b) - (Canvas.GetLeft(a) + a.Width)).All(x => Math.Abs(x - 12) < .1), "navigation, numpad and cursor frames use one uniform group gap");
            Check(lowerFrames.Select(x => x.Height).Distinct().Count() == 1 && lowerFrames.All(x => x.CornerRadius.TopLeft == 0 && x.BorderThickness.Left == 0 && x.Effect == null && x.Background is SolidColorBrush brush && brush.Color == Colors.Transparent), "navigation, numpad and cursor use equal-height transparent layout regions without cards or shadows");
            foreach (var frame in lowerFrames)
            {
                var contained = secondaryKeys.Where(x => Canvas.GetLeft(x) >= Canvas.GetLeft(frame) && Canvas.GetLeft(x) + x.Width <= Canvas.GetLeft(frame) + frame.Width && Canvas.GetTop(x) >= Canvas.GetTop(frame) && Canvas.GetTop(x) + x.Height <= Canvas.GetTop(frame) + frame.Height).ToList();
                Check(contained.Count > 0 && Math.Abs(contained.Min(Canvas.GetLeft) - Canvas.GetLeft(frame)) < .1 && Math.Abs(Canvas.GetLeft(frame) + frame.Width - contained.Max(x => Canvas.GetLeft(x) + x.Width) - 20) < .1, $"{frame.Tag} group starts on the shared left edge and keeps its inter-group breathing space on the trailing side");
            }
            var up = secondaryKeys.First(x => Equals(x.Tag, "Up"));
            var left = secondaryKeys.First(x => Equals(x.Tag, "Left"));
            var down = secondaryKeys.First(x => Equals(x.Tag, "Down"));
            var right = secondaryKeys.First(x => Equals(x.Tag, "Right"));
            var add = secondaryKeys.First(x => Equals(x.Tag, "Add"));
            var arrowKeys = new[] { up, left, down, right };
            Check(arrowKeys.All(x => Math.Abs(x.Width - jisKeys.First(k => Equals(k.Tag, "A")).Width) < .1) && Canvas.GetLeft(up) == Canvas.GetLeft(down) && Canvas.GetTop(up) < Canvas.GetTop(down) && Canvas.GetTop(left) == Canvas.GetTop(down) && Canvas.GetTop(right) == Canvas.GetTop(down) && NoOverlaps(arrowKeys) && !Bounds(left).IntersectsWith(Bounds(add)), "arrow keys match the A-key size and form a non-overlapping inverted-T cluster");
            Check(Math.Abs(Canvas.GetLeft(down) - (Canvas.GetLeft(left) + left.Width) - 4) < .1 && Math.Abs(Canvas.GetLeft(right) - (Canvas.GetLeft(down) + down.Width) - 4) < .1 && Math.Abs(Canvas.GetTop(down) - (Canvas.GetTop(up) + up.Height) - 4) < .1, "arrow keys use the same four-pixel gaps as the keyboard");
            Check(Math.Abs(Canvas.GetTop(up) - secondaryKeys.Min(Canvas.GetTop)) < .1, "arrow-key cluster is top-aligned with the lower keyboard controls");
            up.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.KindBox.SelectedValue = ActionKind.Shortcut;
            window.ValueBox.Text = "Enter";
            Pump(window);
            Check(up.Background is SolidColorBrush editingArrowBrush && editingArrowBrush.Color == MainWindow.AssignmentColorFor(new Mapping { Kind = ActionKind.Shortcut, Value = "Enter" }) && MainWindow.GetIsSelectionPulseActive(up) && MainWindow.HasSelectionPulseAnimationForTest(up), "a selected key has a visible running pulse after an action is entered");
            window.CompleteDestinationInputForTest();
            Pump(window);
            Check(up.Background is SolidColorBrush assignedArrowBrush && assignedArrowBrush.Color.G > assignedArrowBrush.Color.R * 2 && !window.IsEditingSelectedInputForTest && !MainWindow.GetIsSelectionPulseActive(up) && !MainWindow.HasSelectionPulseAnimationForTest(up), "input completion retains the assigned-action color and stops the selection pulse");
            Check(window.CurrentProfileForTest.Mappings.Any(x => x.Input == "Up" && x.Value == "Enter") && window.AppliedMappingForTest("Up") == null && window.LastInput.Text.Contains("未保存"), "input completion keeps edits pending when auto-save is off");
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(ReferenceEquals(window.MouseHost.Child, window.MousePanel), "mouse diagram is placed to the right of the lower keyboard block");
            Check(Descendants<TextBlock>(window.CapsLockLayerButton).Any(x => x.Text.Contains("F13リマップ")), "CapsLock layer clearly shows the reliable F13 requirement");
            window.SetCapsLockRemapForTest(true);
            window.CapsLockLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(window.CapsLockLayerButton.Background is SolidColorBrush capsActive && capsActive.Color == ThemeService.Color("LayerActiveBackground"), "CapsLock layer opens when the F13 remap is active");
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var engineToggle = window.EngineToggle;
            Check(engineToggle.IsVisible && window.EngineStatus.Text.Contains("稼働中") && window.EngineStatus.Foreground is SolidColorBrush engineBrush && engineBrush.Color == ThemeService.Color("AccentTextBrush"), "engine state is a readable clickable item at the sidebar foot");
            var engineTextCenter = window.EngineStatus.TranslatePoint(new System.Windows.Point(0, window.EngineStatus.ActualHeight / 2), engineToggle).Y;
            Check(Math.Abs(engineTextCenter - engineToggle.ActualHeight / 2) < 1, "engine status text is vertically centered");
            Check(window.AutoSaveToggle.IsVisible && window.AutoSaveStatus.Text.Contains("自動保存 オフ"), "auto-save state is visible at the sidebar foot");
            window.AutoSaveToggle.IsChecked = true;
            Check(window.AutoSaveStatus.Text.Contains("自動保存 オン") && new ConfigService().Load().AutoSave && window.AppliedMappingForTest("Up") is { Value: "Enter" }, "turning auto-save on saves and applies the pending edit");
            window.AutoSaveToggle.IsChecked = false;
            Check(!new ConfigService().Load().AutoSave, "turning auto-save off is persisted immediately");
            window.AutoSaveToggle.IsChecked = true;
            Check(ReferenceEquals(window.SidebarStatusPanel.Parent, ((Grid)window.LayerNavigationPane.Child)) && !Descendants<TextBlock>(window.SidebarStatusPanel).Any(x => x.Text.StartsWith("プロファイル:", StringComparison.Ordinal)), "sidebar status row omits the redundant active-profile label");
            Check(!Descendants<TextBlock>(window).Any(x => x.Text.StartsWith("レイヤーボタンを押しながら")), "redundant layer explanation banner is removed");
            var mouseButtons = Descendants<System.Windows.Controls.Button>(window.MousePanel).Where(x => x.Tag is string).ToList();
            var mouseBodyGradient = window.MouseBody.Background as LinearGradientBrush;
            Check(mouseButtons.All(button => button.BorderThickness == new Thickness(1)) && mouseBodyGradient != null && mouseBodyGradient.GradientStops.Count >= 2 && mouseBodyGradient.GradientStops[0].Color != ThemeService.Color("ControlBackground"), "mouse buttons use visible borders against a lighter body surface");
            var tiltButtons = mouseButtons.Where(x => Equals(x.Tag, "TiltLeft") || Equals(x.Tag, "TiltRight")).ToList();
            var tiltText = Descendants<TextBlock>(window.MousePanel).FirstOrDefault(x => x.Text == "TILT");
            Check(tiltButtons.Count == 2 && tiltText != null && Math.Abs(Canvas.GetLeft(tiltText)) < .1 && Math.Abs(tiltText.Width - window.MousePanel.Width) < .1 && tiltText.TextAlignment == TextAlignment.Center, "mouse TILT label is precisely centered");
            var mouseBack = mouseButtons.First(x => Equals(x.Tag, "MouseBack"));
            var mouseForward = mouseButtons.First(x => Equals(x.Tag, "MouseForward"));
            Check(mouseBack.Content?.ToString() == "戻る" && mouseForward.Content?.ToString() == "進む" && Canvas.GetTop(mouseForward) < Canvas.GetTop(mouseBack), "mouse side buttons use the conventional front/upper Forward and rear/lower Back positions without changing their input identities");
            Check(window.ForwardMouseLayerButton.TranslatePoint(new System.Windows.Point(), window).Y < window.BackMouseLayerButton.TranslatePoint(new System.Windows.Point(), window).Y, "left navigation lists Forward above Back to match the mouse diagram");
            Check(!Descendants<TextBlock>(window.MousePanel).Any(x => x.Text == "MOUSE"), "redundant MOUSE label is removed");
            Check(mouseButtons.All(x => Canvas.GetLeft(x) >= 0 && Canvas.GetTop(x) >= 0 && Canvas.GetLeft(x) + x.Width <= window.MousePanel.Width && Canvas.GetTop(x) + x.Height <= window.MousePanel.Height), "mouse controls stay inside diagram");
            var leftClick = mouseButtons.First(x => Equals(x.Tag, "MouseLeft"));
            var rightClick = mouseButtons.First(x => Equals(x.Tag, "MouseRight"));
            var wheelControls = mouseButtons.Where(x => Equals(x.Tag, "WheelUp") || Equals(x.Tag, "WheelDown") || Equals(x.Tag, "MouseMiddle"));
            static Rect Bounds(System.Windows.Controls.Button b) => new(Canvas.GetLeft(b), Canvas.GetTop(b), b.Width, b.Height);
            Check(leftClick.Width <= 56 && rightClick.Width <= 56 && wheelControls.All(x => !Bounds(x).IntersectsWith(Bounds(leftClick)) && !Bounds(x).IntersectsWith(Bounds(rightClick))), "mouse click areas are compact and do not overlap wheel controls");
            Check(mouseButtons.Sum(x => x.Content?.ToString()?.Length ?? 0) <= 24, "mouse diagram uses concise labels");
            Check(window.Icon != null && window.Icon.Width > 0 && window.Icon.Height > 0, "main window explicitly uses the normal RELYR application icon instead of inheriting a macro-shortcut icon");
            CaptureForReview(window, "mouse-layout-main.png");
            Check(window.AssignmentPane.Visibility == Visibility.Visible && Grid.GetColumn(window.AssignmentPane) == 2 && Math.Abs(window.AssignmentPaneTransform.X) < .1, "assignment pane is always visible in its own column");
            var keys = window.KeyboardPanel.Children.OfType<System.Windows.Controls.Button>().ToList();
            foreach (var keyButton in keys)
                keyButton.ApplyTemplate();
            Check(keys.All(button => !Descendants<Border>(button).Any(border => Math.Abs(border.Height - 1) < .1)), "main keyboard buttons omit the decorative top highlight line");
            var space = keys.First(x => Equals(x.Tag, "Space"));
            Check(space.Opacity < .6, "Space key is visibly reserved on normal layer");
            space.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.UpdateLayout();
            Check(window.AssignmentPane.Visibility == Visibility.Visible && window.LastInput.Text.Contains("変更できません"), "reserved Space click shows inline warning without hiding the assignment pane");
            var capsSource = keys.First(x => Equals(x.Tag, "CapsLock"));
            Check(capsSource.Opacity < .6, "CapsLock is visibly reserved while choosing the source key");
            capsSource.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.InputDisplayText.Text == "キーを選択してください", "reserved CapsLock cannot be selected as a source key");
            var qForCaps = keys.First(x => Equals(x.Tag, "Q"));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals("Q", StringComparison.OrdinalIgnoreCase));
            qForCaps.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(capsSource.Opacity < .6, "CapsLock remains protected until an action field is explicitly selected");
            window.KindBox.SelectedValue = ActionKind.Key;
            Pump(window);
            window.ValueBox.Focus();
            System.Windows.Input.Keyboard.Focus(window.ValueBox);
            Check(window.DestinationClearButton.Visibility == Visibility.Visible && window.DestinationClearButton.Content?.ToString() == "削除" && window.DestinationConfirmButton.Visibility == Visibility.Visible && window.DestinationConfirmButton.Content?.ToString() == "確定" && window.DestinationClearButton.TranslatePoint(new System.Windows.Point(), window).X < window.DestinationConfirmButton.TranslatePoint(new System.Windows.Point(), window).X, "direct execution editing shows delete immediately to the left of confirmation");
            Check(window.EnterPhysicalExecutionKeyForTest(System.Windows.Input.Key.CapsLock, System.Windows.Input.ModifierKeys.None), "physical keyboard input is accepted while execution content is being edited");
            Pump(window);
            Check(window.ValueBox.Text == "CapsLock" && Equals(window.KindBox.SelectedValue, ActionKind.Key), "CapsLock can be entered from the physical keyboard");
            window.DestinationClearButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.ValueBox.Text == "" && window.ValueBox.IsKeyboardFocusWithin && window.DestinationClearButton.Visibility == Visibility.Visible && window.DestinationConfirmButton.Visibility == Visibility.Visible, "delete clears only the current execution content and keeps it ready for re-entry");
            window.KindBox.SelectedIndex = -1;
            window.ValueBox.Focus();
            System.Windows.Input.Keyboard.Focus(window.ValueBox);
            Check(window.EnterPhysicalExecutionKeyForTest(System.Windows.Input.Key.Return, System.Windows.Input.ModifierKeys.None), "physical Enter is captured even when an empty execution field has no action kind yet");
            Pump(window);
            Check(window.ValueBox.Text == "Enter" && Equals(window.KindBox.SelectedValue, ActionKind.Key), "physical Enter is stored as the Enter key action");
            window.WorkspaceGrid.RaiseEvent(BlankClick());
            Pump(window);
            Check(window.DestinationConfirmButton.Visibility == Visibility.Collapsed, "clicking outside commits direct input and hides confirmation");
            rightClick.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(!window.ValueBox.IsKeyboardFocusWithin && MainWindow.GetIsSelectionPulseActive(rightClick) && MainWindow.HasSelectionPulseAnimationForTest(rightClick), "selecting a mouse control starts a visible running pulse without moving the caret");
            var key = keys.First(x => !Equals(x.Tag, "Space"));
            key.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.UpdateLayout();
            Pump(window);
            Check(!window.ValueBox.IsKeyboardFocusWithin && MainWindow.GetIsSelectionPulseActive(key) && MainWindow.HasSelectionPulseAnimationForTest(key), "selecting an unassigned keyboard key starts a visible running pulse without moving the caret");
            static System.Windows.Input.MouseButtonEventArgs BlankClick() => new(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent
            };
            window.WorkspaceGrid.RaiseEvent(BlankClick());
            Pump(window);
            Check(!window.HasDestinationInputTargetForTest && !window.ValueBox.IsKeyboardFocusWithin && window.InputName.Text == "" && !window.AssignmentEditor.IsEnabled, "the first non-interactive workspace click clears a selected key that is not being edited");
            window.WorkspaceGrid.RaiseEvent(BlankClick());
            Pump(window);
            Check(window.InputName.Text == "" && window.InputDisplayText.Text == "キーを選択してください" && !window.AssignmentEditor.IsEnabled, "the next workspace click keeps the editor in its initial state");
            key.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(!window.ValueBox.IsKeyboardFocusWithin && MainWindow.GetIsSelectionPulseActive(key), "selecting another keyboard key restores the visible selection pulse without focusing execution input");
            var emptySource = keys.First(x => Equals(x.Tag, "M"));
            var nextSource = keys.First(x => Equals(x.Tag, "N"));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals("M", StringComparison.OrdinalIgnoreCase));
            emptySource.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.KindBox.SelectedValue = ActionKind.Shortcut;
            window.ValueBox.Clear();
            Pump(window);
            window.CompleteDestinationInputForTest();
            nextSource.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(!window.CurrentProfileForTest.Mappings.Any(x => x.Input.Equals("M", StringComparison.OrdinalIgnoreCase)) && emptySource.Background is SolidColorBrush emptySourceBrush && emptySourceBrush.Color == ThemeService.Color("KeyBackground"), "an unfinished key with empty execution content returns to the unassigned color when another key is selected");
            window.SpaceLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(!window.ValueBox.IsKeyboardFocusWithin && !window.LongValueBox.IsKeyboardFocusWithin && !window.HasDestinationInputTargetForTest && window.InputName.Text == "" && window.InputDisplayText.Text == "キーを選択してください" && !window.AssignmentEditor.IsEnabled, "switching layers clears the selected key and returns the editor to its initial state");
            key.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.InputName.Text == "Space+" + key.Tag && !window.ValueBox.IsKeyboardFocusWithin && MainWindow.GetIsSelectionPulseActive(key), "the next on-screen key selects its mapping and restores the visible selection pulse without focusing execution input");
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            var multiA = keys.First(x => Equals(x.Tag, "A"));
            var multiB = keys.First(x => Equals(x.Tag, "B"));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input is "A" or "B" or "Space+A" or "Space+B");
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "A", Layer = "通常", Kind = ActionKind.Text, Value = "multi-A" });
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "B", Layer = "通常", Kind = ActionKind.Shortcut, Value = "Ctrl+B" });
            window.ColorButtonsForTest();
            window.MultiSelectToggle.IsChecked = true;
            multiA.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            multiB.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            foreach (var mouseButton in mouseButtons)
                mouseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.MultiSelectToggle.Content?.ToString() == "選択" && window.MultiSelectToggle.Template != null && window.MultiCopyButton.IsEnabled && !window.MultiPasteButton.IsEnabled && window.MultiDeleteButton.IsEnabled && multiA.BorderBrush is SolidColorBrush multiBorder && multiBorder.Color == ThemeService.Color("AccentBrush") && MainWindow.GetIsMultiSelected(multiA) && MainWindow.GetIsSelectionPulseActive(multiA) && mouseButtons.All(x => MainWindow.GetIsMultiSelected(x) && MainWindow.GetIsSelectionPulseActive(x)), "multi-select visibly marks keyboard keys and every mouse control and enables bulk deletion");
            foreach (var mouseButton in mouseButtons)
                mouseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.MultiCopyButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.SpaceLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.MultiPasteButton.IsEnabled, "multi-select keeps the copied key group while switching layers");
            window.MultiPasteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.CurrentProfileForTest.Mappings.LastOrDefault(x => x.Input == "Space+A") is { Kind: ActionKind.Text, Value: "multi-A" } && window.CurrentProfileForTest.Mappings.LastOrDefault(x => x.Input == "Space+B") is { Kind: ActionKind.Shortcut, Value: "Ctrl+B" } && window.MultiSelectToggle.IsChecked == false && !MainWindow.GetIsMultiSelected(multiA) && !window.AssignmentEditor.IsEnabled && window.InputName.Text.Length == 0, "multi-select paste applies each copied assignment and immediately completes editing");
            window.MultiSelectToggle.IsChecked = true;
            multiA.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            multiB.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.MultiDeleteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(!window.CurrentProfileForTest.Mappings.Any(x => x.Input is "Space+A" or "Space+B") && window.CurrentProfileForTest.Mappings.Any(x => x.Input == "A") && window.CurrentProfileForTest.Mappings.Any(x => x.Input == "B") && window.MultiSelectToggle.IsChecked == false && !window.MultiDeleteButton.IsEnabled && !MainWindow.GetIsMultiSelected(multiA) && !MainWindow.GetIsSelectionPulseActive(multiA), "toolbar trash deletes all selected assignments in the current layer and exits multi-select");
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            ((MenuItem)window.CreateInputContextMenu("A").Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            ((MenuItem)window.CreateInputContextMenu("B").Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump(window);
            Check(window.CurrentProfileForTest.Mappings.LastOrDefault(x => x.Input == "B") is { Kind: ActionKind.Text, Value: "multi-A" } && window.InputName.Text.Length == 0 && !window.AssignmentEditor.IsEnabled && window.ProfileBox.IsEnabled, "single-key paste immediately completes editing and leaves profile switching available");
            var allLayerSource = window.CurrentProfileForTest.Mappings.Last(x => x.Input == "B");
            allLayerSource.LongPressKind = ActionKind.Shortcut;
            allLayerSource.LongPressValue = "Ctrl+Shift+B";
            allLayerSource.LongPressMs = 640;
            allLayerSource.Application = "notepad.exe";
            var mainKeyboardMenu = window.CreateInputContextMenu("B");
            var assignAllLayers = mainKeyboardMenu.Items.OfType<MenuItem>().SingleOrDefault(item => item.Header?.ToString() == "全レイヤーに割り当てる");
            assignAllLayers?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump(window);
            var allLayerCopies = MainWindow.AllAssignmentLayerNames.Select(layer => window.CurrentProfileForTest.Mappings.LastOrDefault(mapping => mapping.Input.Equals(layer + "+B", StringComparison.OrdinalIgnoreCase))).ToArray();
            Check(assignAllLayers?.IsEnabled == true && allLayerCopies.All(mapping => mapping is { Kind: ActionKind.Text, Value: "multi-A", LongPressKind: ActionKind.Shortcut, LongPressValue: "Ctrl+Shift+B", LongPressMs: 640, Application: "notepad.exe" }) && allLayerCopies.Select(mapping => mapping!.Layer).SequenceEqual(MainWindow.AllAssignmentLayerNames) && window.CurrentProfileForTest.Mappings.LastOrDefault(mapping => mapping.Input == "B") == allLayerSource && !window.CreateInputContextMenu("Insert", false).Items.OfType<MenuItem>().Any(item => item.Header?.ToString() == "全レイヤーに割り当てる"), "main keyboard context menu copies the complete assignment through every non-default layer without changing the default or exposing the command on lower keys");
            multiA.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.MultiDeleteButton.IsEnabled && window.MultiDeleteButton.Foreground is SolidColorBrush deleteForeground && deleteForeground.Color == ThemeService.Color("PrimaryText"), "toolbar trash becomes active for one normally selected assigned key and uses a white icon");
            window.MultiDeleteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(!window.CurrentProfileForTest.Mappings.Any(x => x.Input == "A") && !window.MultiDeleteButton.IsEnabled, "toolbar trash deletes the assignment of one normally selected key and clears its active state");
            Check(window.ToolbarSaveButton.TranslatePoint(new System.Windows.Point(), window).X > window.MultiDeleteButton.TranslatePoint(new System.Windows.Point(), window).X, "save remains fixed at the right end of the toolbar after multi-select controls");
            var directKey = keys.First(x => Equals(x.Tag, "Q"));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals("Q", StringComparison.OrdinalIgnoreCase));
            directKey.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.KindBox.SelectedIndex < 0 && window.LongKindBox.SelectedIndex < 0 && !window.LongPressExpander.IsEnabled && !window.LongPressOnlyButton.IsEnabled, "an unassigned normal-layer alphabet key starts with inactive choices and protects text input by disabling long press");
            window.SpaceLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            directKey.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.LongPressExpander.IsEnabled && window.LongPressOnlyButton.IsEnabled, "the same alphabet key keeps long press available in the Space layer");
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            directKey.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.ValueBox.Text = "A";
            Pump(window);
            window.CompleteDestinationInputForTest();
            Pump(window);
            var configuredDirect = window.CurrentProfileForTest.Mappings.LastOrDefault(x => x.Input == "Q");
            var appliedDirect = window.AppliedMappingForTest("Q");
            Check(configuredDirect is { Kind: ActionKind.Key, Value: "A" } && appliedDirect is { Kind: ActionKind.Key, Value: "A" }, "typing directly into an unassigned execution field intentionally creates a single-key replacement");
            window.WorkspaceGrid.RaiseEvent(BlankClick());
            Pump(window);
            window.AutoSaveToggle.IsChecked = false;
            var nineKey = keys.First(x => Equals(x.Tag, "9"));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals("9", StringComparison.OrdinalIgnoreCase));
            nineKey.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.ValueBox.Text = "8";
            Pump(window);
            window.CompleteDestinationInputForTest();
            Pump(window);
            Check(window.CurrentProfileForTest.Mappings.LastOrDefault(x => x.Input == "9") is { Kind: ActionKind.Key, Value: "8" } && window.AppliedMappingForTest("9") == null && nineKey.Background is SolidColorBrush pendingNineBrush && pendingNineBrush.Color == MainWindow.AssignmentColorFor(new Mapping { Kind = ActionKind.Key, Value = "8" }), "with auto-save off, completing 9 to 8 keeps the visible draft but does not change the live engine");
            window.SaveAndApplyForTest();
            Pump(window);
            Check(window.AppliedMappingForTest("9") is { Kind: ActionKind.Key, Value: "8" }, "Save and Apply commits the pending 9 to 8 mapping to the live engine");
            window.AutoSaveToggle.IsChecked = true;
            window.WorkspaceGrid.RaiseEvent(BlankClick());
            Pump(window);
            var escapeKey = keys.First(x => Equals(x.Tag, "Esc"));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals("Esc", StringComparison.OrdinalIgnoreCase));
            escapeKey.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.LongPressExpander.IsExpanded = true;
            window.LongKindBox.SelectedValue = ActionKind.Shortcut;
            window.LongValueBox.Text = "LWin+L";
            Pump(window);
            var shortChoiceHeights = window.KindBox.Items.Cast<object>().Select(x => (window.KindBox.ItemContainerGenerator.ContainerFromItem(x) as ListBoxItem)?.ActualHeight ?? 0).ToArray();
            var longChoiceHeights = window.LongKindBox.Items.Cast<object>().Select(x => (window.LongKindBox.ItemContainerGenerator.ContainerFromItem(x) as ListBoxItem)?.ActualHeight ?? 0).ToArray();
            Check(shortChoiceHeights.All(x => Math.Abs(x - 44) < .5) && longChoiceHeights.All(x => Math.Abs(x - 44) < .5), "short-press and long-press choices use the same readable height without clipping wrapped labels");
            window.CompleteDestinationInputForTest();
            Pump(window);
            var configuredEscape = window.CurrentProfileForTest.Mappings.LastOrDefault(x => x.Input == "Esc");
            var appliedEscape = window.AppliedMappingForTest("Esc");
            Check(configuredEscape is { Kind: ActionKind.None, LongPressKind: ActionKind.Shortcut, LongPressValue: "LWin+L" } && appliedEscape is { Kind: ActionKind.None, LongPressKind: ActionKind.Shortcut, LongPressValue: "LWin+L" } && escapeKey.Background is SolidColorBrush escapeBrush && escapeBrush.Color.G > escapeBrush.Color.R * 2, "an Esc long-only shortcut is normalized, applied immediately, and shown in shortcut green");
            window.WorkspaceGrid.RaiseEvent(BlankClick());
            Pump(window);
            var gestureKey = keys.First(x => Equals(x.Tag, "F5"));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals("F5", StringComparison.OrdinalIgnoreCase));
            gestureKey.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.ApplyCatalogActionForTest(new CatalogAction("ジェスチャー", "ウィンドウ操作", "", ActionKind.Gesture, "ウィンドウ操作"), false);
            Pump(window);
            var editingGesture = window.CurrentProfileForTest.Mappings.LastOrDefault(x => x.Input == "F5");
            Check(editingGesture is { Kind: ActionKind.Gesture, Value: "ウィンドウ操作", LongPressKind: ActionKind.None, LongPressValue: "" } && Equals(window.KindBox.SelectedValue, ActionKind.Gesture) && window.ValueBox.Text == "ジェスチャー：ウィンドウ操作" && window.ValueBox.IsReadOnly && window.LongPressExpander is { IsEnabled: false, IsExpanded: false } && window.LongPressExpander.Opacity < 1, "a gesture is assigned from its dedicated short-press choice, keeps its generated name read-only, and disables the incompatible long-press editor");
            CaptureForReview(window, "gesture-short-main.png");
            window.CompleteDestinationInputForTest();
            Pump(window);
            Check(window.AppliedMappingForTest("F5") is { Kind: ActionKind.Gesture, Value: "ウィンドウ操作" } && gestureKey.Background is SolidColorBrush gestureBrush && gestureBrush.Color == MainWindow.AssignmentColorFor(new Mapping { Kind = ActionKind.Gesture, Value = "ウィンドウ操作" }), "completing a short gesture stores its reference and colors the assigned key consistently");
            gestureKey.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.ApplyCatalogActionForTest(new CatalogAction("編集", "コピー", "", ActionKind.Shortcut, "Ctrl+C"), false);
            Pump(window);
            Check(window.CurrentProfileForTest.Mappings.Last(x => x.Input == "F5") is { Kind: ActionKind.Shortcut, Value: "Ctrl+C" } && !window.ValueBox.IsReadOnly && !window.ValueBox.IsKeyboardFocusWithin && !window.IsEditingSelectedInputForTest && window.DestinationConfirmButton.Visibility == Visibility.Collapsed && window.LongPressExpander.IsEnabled && window.LongPressExpander.Opacity == 1, "choosing a normal short-press action replaces the gesture, completes editing, hides confirmation, and restores the long-press editor");
            window.KeypadInputRequestedForTest = picker => picker.SetShortcutValue("Ctrl+K", true);
            window.OpenKeypadInputForTest();
            window.KeypadInputRequestedForTest = null;
            Pump(window);
            Check(window.CurrentProfileForTest.Mappings.Last(x => x.Input == "F5") is { Kind: ActionKind.Shortcut, Value: "Ctrl+K" } && !window.ValueBox.IsKeyboardFocusWithin && window.DestinationConfirmButton.Visibility == Visibility.Collapsed, "keypad input works from the main keyboard editor and completes immediately after the keypad closes");
            window.WorkspaceGrid.RaiseEvent(BlankClick());
            Pump(window);
            foreach (string trigger in new[] { "MouseRight", "MouseBack", "MouseForward" })
                window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = trigger, Kind = ActionKind.Gesture, Value = "ウィンドウ操作" });
            window.RefreshLayerButtonsForTest();
            Pump(window);
            Check(!window.RightMouseLayerButton.IsEnabled && !window.BackMouseLayerButton.IsEnabled && !window.ForwardMouseLayerButton.IsEnabled && new[] { window.RightMouseLayerButton, window.BackMouseLayerButton, window.ForwardMouseLayerButton }.All(x => x.ToolTip?.ToString()?.Contains("ジェスチャー") == true) && MainWindow.IsMouseLayerBlockedByDirectGesture([window.CurrentProfileForTest], window.CurrentProfileForTest.Name, "MouseRight"), "direct gestures on right, back, and forward buttons visibly disable their conflicting layer buttons without deleting layer assignments");
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input is "MouseRight" or "MouseBack" or "MouseForward");
            window.RefreshLayerButtonsForTest();
            Pump(window);
            Check(window.RightMouseLayerButton.IsEnabled && window.BackMouseLayerButton.IsEnabled && window.ForwardMouseLayerButton.IsEnabled, "removing each direct mouse gesture immediately restores its corresponding layer button");
            if (!window.ProfilesForTest.Any(x => x.Name == "プロファイル4"))
                window.ProfilesForTest.Add(new Profile { Name = "プロファイル4" });
            window.SaveAndApplyForTest();
            var oKey = keys.First(x => Equals(x.Tag, "O"));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals("O", StringComparison.OrdinalIgnoreCase));
            oKey.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.LongPressExpander.IsExpanded = true;
            window.ApplyProfileActionForTest("プロファイル4", true);
            Check(Equals(window.LongKindBox.SelectedValue, ActionKind.Profile) && window.LongValueBox.Text == "プロファイル：プロファイル4", "profile assignment selects the profile action button and shows a readable profile name");
            window.LongPressBox.Text = "650";
            Pump(window);
            var editingProfileSwitch = window.CurrentProfileForTest.Mappings.LastOrDefault(x => x.Input == "O");
            window.CompleteDestinationInputForTest();
            Pump(window);
            var appliedProfileSwitch = window.AppliedMappingForTest("O");
            Check(editingProfileSwitch is { LongPressKind: ActionKind.Profile, LongPressValue: "プロファイル4", LongPressMs: 650 } && appliedProfileSwitch is { Kind: ActionKind.None, LongPressKind: ActionKind.Profile, LongPressValue: "プロファイル4", LongPressMs: 650 }, "a long-press profile action remains assigned when its timing is edited and after input completion");
            Check(appliedProfileSwitch != null && window.ExecuteMappingForTest(appliedProfileSwitch, "O:Long"), "the saved long-press profile action is accepted by the runtime executor");
            Pump(window);
            Check(window.AppliedProfileNameForTest == "プロファイル4" && window.IsProfileOverlayVisibleForTest, "executing a profile action switches the runtime profile and briefly shows its name");
            window.SwitchProfileForTest("標準");
            Pump(window);
            window.WorkspaceGrid.RaiseEvent(BlankClick());
            Pump(window);
            key.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.KindBox.Focus();
            System.Windows.Input.Keyboard.Focus(window.KindBox);
            var kindPress = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent };
            window.KindBox.RaiseEvent(kindPress);
            Pump(window);
            Check(window.InputName.Text.Length > 0 && window.AssignmentEditor.IsEnabled, "interactive controls do not accidentally complete or clear the selected input");
            window.KindBox.SelectedValue = ActionKind.Shortcut;
            window.ValueBox.Text = "Ctrl+C";
            Pump(window);
            window.CompleteDestinationInputForTest();
            key.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            key.ApplyTemplate();
            var selectedKeyTint = (Border)key.Template.FindName("SelectionTint", key)!;
            Check(!window.ValueBox.IsKeyboardFocusWithin && key.Background is SolidColorBrush editingAssignedBrush && editingAssignedBrush.Color == MainWindow.AssignmentColorFor(new Mapping { Kind = ActionKind.Shortcut, Value = "Ctrl+C" }) && MainWindow.GetIsSelectionPulseActive(key) && MainWindow.GetIsCurrentSelected(key) && selectedKeyTint.Opacity > .7, $"selecting an assigned key preserves its action color beneath a visible green selection surface and starts the pulse (selected={MainWindow.GetIsCurrentSelected(key)}, tint={selectedKeyTint.Opacity:F2}, pulse={MainWindow.GetIsSelectionPulseActive(key)})");
            window.ValueBox.Focus();
            System.Windows.Input.Keyboard.Focus(window.ValueBox);
            var visualKeyPress = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent };
            key.RaiseEvent(visualKeyPress);
            Pump(window);
            Check(!visualKeyPress.Handled && window.ValueBox.Text == "Ctrl+C", "the on-screen keyboard selects assignment targets only and never writes into execution content");
            window.AssignmentDeleteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(!window.ValueBox.IsKeyboardFocusWithin && !window.IsEditingSelectedInputForTest && window.InputName.Text == "" && window.AssignmentDeleteButton.Foreground is SolidColorBrush deleteText && deleteText.Color == ThemeService.Color("DangerBrush") && key.Background is SolidColorBrush deletedKeyBrush && deletedKeyBrush.Color == ThemeService.Color("KeyBackground") && !MainWindow.GetIsSelectionPulseActive(key) && !MainWindow.HasSelectionPulseAnimationForTest(key), "deleting an assignment clears its color and selection pulse");
            key.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.LongPressExpander.IsExpanded = false;
            window.LongPressExpander.IsExpanded = true;
            window.UpdateLayout();
            Pump(window);
            Check(window.LongValueBox.IsKeyboardFocusWithin && window.LongValueBox.CaretIndex == window.LongValueBox.Text.Length && window.LongDestinationClearButton.Visibility == Visibility.Visible && window.LongDestinationConfirmButton.Visibility == Visibility.Visible, "expanding long-press actions puts the caret in content and shows matching delete and confirm actions");
            window.AssignmentScrollViewer.ScrollToTop();
            window.UpdateLayout();
            var assignmentWheel = new System.Windows.Input.MouseWheelEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = System.Windows.UIElement.PreviewMouseWheelEvent };
            window.AssignmentScrollViewer.RaiseEvent(assignmentWheel);
            window.UpdateLayout();
            Check(window.AssignmentScrollViewer.ScrollableHeight > 0 && window.AssignmentScrollViewer.VerticalOffset > 0 && assignmentWheel.Handled, $"assignment pane handles the wheel directly even above nested action controls ({window.AssignmentScrollViewer.ScrollableHeight:F0}, {window.AssignmentScrollViewer.VerticalOffset:F0}, {assignmentWheel.Handled})");
            var workspaceRight = window.WorkspaceGrid.TranslatePoint(new System.Windows.Point(window.WorkspaceGrid.ActualWidth, 0), window.MainContentGrid).X;
            var assignmentLeft = window.AssignmentPane.TranslatePoint(new System.Windows.Point(), window.MainContentGrid).X;
            Check(window.AssignmentPane.Visibility == Visibility.Visible && assignmentLeft >= workspaceRight - 1, "assignment pane never overlays the keyboard workspace");
            window.DismissAssignmentPaneIfOutside(window.WorkspaceGrid);
            Check(window.AssignmentPane.Visibility == Visibility.Visible && !window.AssignmentPaneTransform.HasAnimatedProperties && Math.Abs(window.AssignmentPaneTransform.X) < .1, "assignment pane remains fixed when the user clicks elsewhere");
            window.KindBox.SelectedValue = ActionKind.Text;
            window.ValueBox.Text = "delete-me";
            var assignedMenu = window.CreateInputContextMenu(key.Tag!.ToString()!);
            var contextDelete = assignedMenu.Items.OfType<System.Windows.Controls.MenuItem>().FirstOrDefault(x => x.Header?.ToString() == "この割り当てを削除");
            contextDelete?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            var clearedMenu = window.CreateInputContextMenu(key.Tag!.ToString()!);
            var clearedDelete = clearedMenu.Items.OfType<System.Windows.Controls.MenuItem>().FirstOrDefault(x => x.Header?.ToString() == "この割り当てを削除");
            Check(contextDelete?.IsEnabled == true && clearedDelete?.IsEnabled == false && window.ValueBox.Text == "", "right-click menu deletes the selected key assignment and refreshes the editor");
            string beforeDetection = window.InputName.Text;
            window.BeginInputDetectionForTest();
            window.FeedDetectedInputForTest("MouseRight Layer Down");
            Check(window.InputName.Text == beforeDetection && window.LastInput.Text.Contains("押したまま"), "input detection waits after a layer button is pressed");
            window.FeedDetectedInputForTest("S Down");
            Pump(window);
            Check(window.InputName.Text == "MouseRight+S" && window.InputDisplayText.Text == "右クリック + S" && window.ValueBox.IsKeyboardFocusWithin, "input detection recognizes a held layer plus the next key and focuses its empty action");
            window.KindBox.SelectedValue = ActionKind.Shortcut;
            window.ValueBox.Text = "Ctrl+C";
            window.CompleteDestinationInputForTest();
            window.BeginInputDetectionForTest();
            window.FeedDetectedInputForTest("MouseRight Layer Down");
            window.FeedDetectedInputForTest("S Down");
            Pump(window);
            Check(!window.ValueBox.IsKeyboardFocusWithin, "input detection leaves the caret out when the detected action already has execution content");
            Check(Descendants<System.Windows.Controls.Button>(window.AssignmentPane).Contains(window.DetectInputButton) && !Descendants<TextBlock>(window.AssignmentPane).Any(x => x.Text is "割り当て" or "現在のレイヤー"), "input detection replaces redundant assignment and current-layer headings at the top right");
            static string ItemLabel(object item) => (string?)item.GetType().GetProperty("Label")?.GetValue(item) ?? "";
            var shortActionLabels = window.KindBox.Items.Cast<object>().Select(ItemLabel).ToArray();
            var longActionLabels = window.LongKindBox.Items.Cast<object>().Select(ItemLabel).ToArray();
            var balancedActionLabels = new[] { "別のキー", "プロファイル", "ショートカット", "文字列", "アプリ・パス", "マクロ", "ジェスチャー", "キーパッドから入力" };
            Check(window.KindBox.Visibility == Visibility.Visible && shortActionLabels.SequenceEqual(balancedActionLabels) && longActionLabels.SequenceEqual(balancedActionLabels), "short and long editors keep a balanced two-column layout with keypad input replacing disable");
            var keypadChoiceLabels = new[] { window.KindBox, window.LongKindBox }.Select(list => list.Items.Cast<object>().Single(x => x.GetType().GetProperty("IsKeypad")?.GetValue(x) is true)).Select((choice, index) => (ListBoxItem)new[] { window.KindBox, window.LongKindBox }[index].ItemContainerGenerator.ContainerFromItem(choice)!).Select(container => Descendants<TextBlock>(container).First(x => x.Text.Contains("キーパッドから", StringComparison.Ordinal))).ToArray();
            Check(keypadChoiceLabels.All(label => label.Text == "キーパッドから入力" && label.TextWrapping == TextWrapping.NoWrap && label.TextTrimming == TextTrimming.None && label.ActualWidth >= label.DesiredSize.Width - .1), "keypad input stays on one fully visible line in both action editors");
            var obsoleteHelperLabels = new[] { "アクション", "プロファイル", "マクロ", "アプリ" };
            Check(!Descendants<System.Windows.Controls.Button>(window.AssignmentPane).Any(x => obsoleteHelperLabels.Contains(x.Content?.ToString())), "redundant helper action rows are removed from both editors");
            var iconResourceKeys = new[] { "ActionKeyIconBrush", "ActionProfileIconBrush", "ActionShortcutIconBrush", "ActionTextIconBrush", "ActionLaunchIconBrush", "ActionMacroIconBrush" };
            var shortChoiceItems = Enumerable.Range(0, 6).Select(i => (ListBoxItem)window.KindBox.ItemContainerGenerator.ContainerFromIndex(i)!).ToArray();
            var longChoiceItems = Enumerable.Range(0, 6).Select(i => (ListBoxItem)window.LongKindBox.ItemContainerGenerator.ContainerFromIndex(i)!).ToArray();
            static TextBlock ChoiceIcon(ListBoxItem item) => Descendants<TextBlock>(item).First(x => x.Style == item.FindResource("ActionChoiceIcon"));
            static TextBlock ChoiceLabel(ListBoxItem item) => Descendants<TextBlock>(item).First(x => x.Style != item.FindResource("ActionChoiceIcon"));
            Check(shortChoiceItems.Concat(longChoiceItems).All(x => x.Background is SolidColorBrush background && background.Color == ThemeService.Color("KeyBackground") && ChoiceLabel(x).Foreground is SolidColorBrush label && label.Color == ThemeService.Color("PrimaryText")) && shortChoiceItems.Select(ChoiceIcon).Select(x => ((SolidColorBrush)x.Foreground).Color).SequenceEqual(iconResourceKeys.Select(ThemeService.Color)) && longChoiceItems.Select(ChoiceIcon).Select(x => ((SolidColorBrush)x.Foreground).Color).SequenceEqual(iconResourceKeys.Select(ThemeService.Color)), "short and long action buttons keep the standard button surface and identify actions only by aligned icon color");
            Check(window.KindBox.Background == System.Windows.Media.Brushes.Transparent && window.LongKindBox.Background == System.Windows.Media.Brushes.Transparent && Enumerable.Range(0, window.KindBox.Items.Count).Select(i => (ListBoxItem)window.KindBox.ItemContainerGenerator.ContainerFromIndex(i)!).All(x => Math.Abs(x.Opacity - 1) < .01), "disabled action choices avoid the extra gray list background and double opacity");
            var assignmentTexts = Descendants<TextBlock>(window.AssignmentPane).Select(x => x.Text).ToArray();
            Check(assignmentTexts.Contains("実行する操作") && assignmentTexts.Contains("長押しの操作") && !assignmentTexts.Any(x => x.Contains("最初に中央の") || x is "短押しの動作" or "長押しの動作" || x.StartsWith("空欄の場合") || x.StartsWith("短押しとは別")), "inspector uses concise action headings without redundant guidance");
            Check(ScrollViewer.GetVerticalScrollBarVisibility(window.LayerNavigationScrollViewer) == ScrollBarVisibility.Hidden && ScrollViewer.GetVerticalScrollBarVisibility(window.AssignmentScrollViewer) == ScrollBarVisibility.Hidden, "small-window side panes stay wheel-scrollable without visible vertical sliders");
            Check(!Descendants<System.Windows.Controls.Expander>(window.AssignmentPane).Any(x => x.Header?.ToString() is "マウスドラッグの詳細" or "条件・詳細設定"), "obsolete drag-detail and condition-detail sections are removed from the assignment pane");
            Check(!Descendants<System.Windows.Controls.CheckBox>(window.AssignmentPane).Any(x => x.Content?.ToString() == "この割り当てを有効にする") && ReferenceEquals(window.InputDisplayText.Foreground, ThemeService.Brush("PrimaryText")), "the redundant per-assignment enable switch is absent and right-pane text uses the primary theme color");
            using (var icon = MainWindow.CreateDesktopNumberIcon(8))
            using (var bitmap = icon.ToBitmap())
            {
                var ink = new List<System.Drawing.Point>();
                for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var c = bitmap.GetPixel(x, y);
                    if (c.A > 30)
                        ink.Add(new System.Drawing.Point(x, y));
                }
                int height = ink.Count == 0 ? 0 : ink.Max(p => p.Y) - ink.Min(p => p.Y) + 1;
                Check(height >= 24, "tray desktop number fills icon area");
                Check(bitmap.GetPixel(0, 0).A < 30 && bitmap.GetPixel(31, 31).A < 30, "tray desktop number has transparent background");
            }
            using (var appIcon = MainWindow.CreateDefaultTrayIcon())
                Check(appIcon.Handle != IntPtr.Zero, "tray uses the RELYR application icon when desktop numbers are disabled");
            var fakeUpdate = new UpdateInfo(new Version(99, 0, 0), "99.0.0", new Uri("https://github.com/zitan-source/RELYR/releases/download/v99.0.0/RELYR-Update-99.0.0.exe"), new Uri("https://github.com/zitan-source/RELYR/releases/download/v99.0.0/RELYR-Update-99.0.0.exe.sha256"), null, "RELYR-Update-99.0.0.exe");
            var settings = new SettingsWindow(new AppConfig(), fakeUpdate);
            settings.UpdateLayout();
            Check(settings.DesktopNumberTrayBox != null, "tray desktop number option exists");
            Check(settings.CheckForUpdatesBox.IsChecked == true && settings.CheckForUpdates, "update checking is enabled by default and can be changed in settings");
            settings.CheckForUpdatesBox.IsChecked = false;
            Check(!settings.CheckForUpdates, "update checking can be disabled");
            Check(settings.TutorialButton.Content?.ToString() == "チュートリアルを見る", "app settings can reopen the tutorial");
            Check(!settings.StartWithWindowsChanged, "opening settings does not request a privileged startup rewrite");
            settings.StartupBox.IsChecked = !settings.StartWithWindows;
            Check(settings.StartWithWindowsChanged, "only changing the startup checkbox requests its privileged update");
            settings.StartupBox.IsChecked = !settings.StartWithWindows;
            Check(!settings.StartWithWindowsChanged, "restoring the startup checkbox removes privileged update request");
            Check(!Descendants<System.Windows.Controls.ScrollViewer>(settings).Any(), "settings use category pages without scrolling");
            settings.CategoryList.SelectedIndex = 6;
            settings.UpdateLayout();
            Check(settings.UpdatePanel.Visibility == Visibility.Visible && settings.GeneralPanel.Visibility == Visibility.Collapsed && settings.CheckForUpdatesButton.Content?.ToString() == "アップデートを確認" && settings.InstallUpdateButton.Content?.ToString() == "今すぐアップデート" && settings.InstallUpdateButton.Visibility == Visibility.Visible && settings.UpdateStatusText.Text.Contains("v99.0.0") && settings.UpdateStatusText.Foreground is SolidColorBrush availableBrush && availableBrush.Color == ThemeService.Color("WarningBrush") && !settings.UpdateStatusText.Text.EndsWith('。'), "available update uses a clear orange status without unnecessary terminal punctuation");
            settings.ApplyUpdateResult(new UpdateCheckResult(MainWindow.RunningVersion, MainWindow.DisplayVersion, null, DateTimeOffset.Now), true);
            Check(settings.UpdateStatusText.Text == $"最新バージョンです（v{MainWindow.DisplayVersion}）" && settings.UpdateStatusText.Foreground is SolidColorBrush currentBrush && currentBrush.Color == ThemeService.Color("AccentBrush"), "current version uses a concise green status");
            settings.SelectCategory("Support");
            settings.UpdateLayout();
            Check(settings.SupportPanel.Visibility == Visibility.Visible && settings.UpdatePanel.Visibility == Visibility.Collapsed && settings.OpenSupportPageButton.Content?.ToString() == "支援ページを開く" && Uri.TryCreate(SettingsWindow.SupportPageUrl, UriKind.Absolute, out var supportUri) && supportUri.Scheme == Uri.UriSchemeHttps && supportUri.Host == "ko-fi.com", "support settings appear directly after updates and use the trusted HTTPS Ko-fi page");
            settings.CategoryList.SelectedIndex = 2;
            settings.UpdateLayout();
            Check(settings.LayersPanel.Visibility == Visibility.Visible && settings.GeneralPanel.Visibility == Visibility.Collapsed, "settings sidebar switches category pages");
            Check(Descendants<TextBlock>(settings.LayersPanel).Contains(settings.CapsRemapStatus) && !Descendants<TextBlock>(settings.GeneralPanel).Contains(settings.CapsRemapStatus), "CapsLock controls are located in the layer settings category");
            settings.CategoryList.SelectedIndex = 1;
            settings.UpdateLayout();
            Check(Descendants<System.Windows.Controls.CheckBox>(settings.AppearancePanel).Contains(settings.ProfileOverlayBox) && !Descendants<System.Windows.Controls.CheckBox>(settings.LayersPanel).Contains(settings.ProfileOverlayBox) && !Descendants<TextBlock>(settings.AppearancePanel).Any(x => x.Text.Contains("仮想デスクトップ番号のすぐ上")), "profile overlay option is contained in the Appearance profile-switch card without obsolete placement text");
            settings.CategoryList.SelectedIndex = 4;
            settings.UpdateLayout();
            Check(settings.ArchivePanel.Visibility == Visibility.Visible, "archive settings fit on their own category page");
            Check(settings.ArchiveWatchFolderBox.Text == Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) && settings.ArchiveDestinationFolderBox.Text == "" && Descendants<System.Windows.Controls.Button>(settings.ArchivePanel).Count(x => x.Content?.ToString() == "参照…") == 2, "archive settings provide separate browsable watch and destination folders");
            settings.CategoryList.SelectedIndex = 5;
            settings.UpdateLayout();
            Check(settings.ResetAllButton.Background is SolidColorBrush resetBrush && resetBrush.Color.R > resetBrush.Color.G && resetBrush.Color.R > resetBrush.Color.B, "data settings show a clearly red all-reset action");
            settings.Close();
            var themeSettings = new SettingsWindow(new AppConfig { ThemeMode = AppThemeMode.System });
            themeSettings.UpdateLayout();
            Check(themeSettings.SystemThemeBox != null && themeSettings.LightThemeBox != null && themeSettings.DarkThemeBox != null, "appearance settings offer system, light, and dark modes");
            themeSettings.LightThemeBox!.IsChecked = true;
            window.UpdateLayout();
            Check(!ThemeService.UsesDark && ThemeService.Color("AppBackground").R > 200 && ThemeService.Color("PrimaryText").R < 80, "light mode uses a genuinely light background with dark readable text");
            Check(shortChoiceItems.All(x => x.Background is SolidColorBrush background && background.Color == ThemeService.Color("KeyBackground") && ChoiceLabel(x).Foreground is SolidColorBrush label && label.Color == ThemeService.Color("PrimaryText")) && shortChoiceItems.Select(ChoiceIcon).Select(x => ((SolidColorBrush)x.Foreground).Color).SequenceEqual(iconResourceKeys.Select(ThemeService.Color)), "action buttons keep standard light-theme surfaces with darker readable category icons");
            themeSettings.DarkThemeBox!.IsChecked = true;
            window.UpdateLayout();
            Check(ThemeService.UsesDark && ThemeService.Color("AppBackground").R < 40 && ThemeService.Color("PrimaryText").R > 180, "dark mode retains the established dark palette");
            Check(shortChoiceItems.All(x => x.Background is SolidColorBrush background && background.Color == ThemeService.Color("KeyBackground") && ChoiceLabel(x).Foreground is SolidColorBrush label && label.Color == ThemeService.Color("PrimaryText")) && shortChoiceItems.Select(ChoiceIcon).Select(x => ((SolidColorBrush)x.Foreground).Color).SequenceEqual(iconResourceKeys.Select(ThemeService.Color)), "action buttons keep standard dark-theme surfaces with brighter category icons");
            themeSettings.Close();
            var checkedAt = new DateTimeOffset(2026, 7, 18, 12, 34, 0, TimeSpan.FromHours(9));
            var currentResult = new UpdateCheckResult(MainWindow.RunningVersion, MainWindow.DisplayVersion, null, checkedAt);
            var currentUpdateSettings = new SettingsWindow(new AppConfig { LastUpdateCheckUtcTicks = checkedAt.UtcTicks }, currentResult);
            currentUpdateSettings.UpdateLayout();
            Check(currentUpdateSettings.CurrentVersionText.Text == "v" + MainWindow.DisplayVersion && currentUpdateSettings.LatestVersionText.Text == "v" + MainWindow.DisplayVersion && currentUpdateSettings.LastCheckedText.Text.Contains("2026/07/18") && currentUpdateSettings.InstallUpdateButton.Visibility == Visibility.Collapsed, "update page shows current/latest versions and last check time without an install button when current");
            Check(currentUpdateSettings.UpdateProgressBar.Visibility == Visibility.Collapsed && currentUpdateSettings.UpdateProgressText.Visibility == Visibility.Collapsed, "download progress stays unobtrusive until an update is downloaded");
            currentUpdateSettings.Close();
            var settingsWithAutoSave = new SettingsWindow(new AppConfig { AutoSave = true, SpaceHoldRepeatEnabled = true, SpaceHoldRepeatDelayMs = 450 }) { Owner = window, ShowInTaskbar = false };
            settingsWithAutoSave.Show();
            settingsWithAutoSave.UpdateLayout();
            Check(settingsWithAutoSave.GeneralPanel.ActualHeight <= ((FrameworkElement)settingsWithAutoSave.GeneralPanel.Parent).ActualHeight + .5, "general settings surface fits above the footer without clipping");
            settingsWithAutoSave.CategoryList.SelectedIndex = 6;
            settingsWithAutoSave.UpdateLayout();
            Check(settingsWithAutoSave.UpdatePanel.ActualHeight <= ((FrameworkElement)settingsWithAutoSave.UpdatePanel.Parent).ActualHeight + .5, "update settings surface fits above the footer without clipping");
            settingsWithAutoSave.CategoryList.SelectedIndex = 7;
            settingsWithAutoSave.UpdateLayout();
            Check(settingsWithAutoSave.SupportPanel.ActualHeight <= ((FrameworkElement)settingsWithAutoSave.SupportPanel.Parent).ActualHeight + .5, "support settings fit without scrolling or clipping");
            settingsWithAutoSave.CategoryList.SelectedIndex = 2;
            settingsWithAutoSave.UpdateLayout();
            Check(settingsWithAutoSave.LayersPanel.ActualHeight <= ((FrameworkElement)settingsWithAutoSave.LayersPanel.Parent).ActualHeight + .5, "layer settings fit without scrolling or clipping");
            settingsWithAutoSave.CategoryList.SelectedIndex = 0;
            settingsWithAutoSave.UpdateLayout();
            Check(settingsWithAutoSave.AutoSaveBox.IsChecked == true, "auto-save option exists");
            Check(settingsWithAutoSave.SpaceRepeatBox.IsChecked == true && settingsWithAutoSave.SpaceRepeatDelayBox.Text == "450", "Space hold repeat controls are clear");
            Check(settingsWithAutoSave.EnableCapsRemapButton != null && settingsWithAutoSave.DisableCapsRemapButton != null && settingsWithAutoSave.CapsRemapStatus.Text.Length > 0, "CapsLock F13 setup and restore controls are available");
            Check(Descendants<System.Windows.Controls.Button>(settingsWithAutoSave).Any(x => x.Content?.ToString() == "インポート") && Descendants<System.Windows.Controls.Button>(settingsWithAutoSave).Any(x => x.Content?.ToString() == "エクスポート"), "import and export are in app settings");
            settingsWithAutoSave.Close();
            Check(!Descendants<System.Windows.Controls.Button>(window).Any(x => x.Content?.ToString() is "インポート" or "エクスポート"), "import and export are removed from main toolbar");
            Check(!window.ToolbarPanel.Children.OfType<System.Windows.Controls.Button>().Any(x => x.Content?.ToString() is "名前変更" or "自動切替" or "割り当てコピー" or "削除") && window.ProfileManagerButton.Content is Grid, "the main toolbar keeps only immediate profile context while profile management stays in the sidebar");
            var profileManager = new ProfileManagerWindow([new Profile { Name = "標準" }, new Profile { Name = "編集用", AutoSwitchEnabled = true, AutoSwitchApplications = ["notepad.exe"] }], "編集用") { Owner = window, ShowInTaskbar = false };
            profileManager.Show();
            profileManager.UpdateLayout();
            Check(profileManager.TitleBarUsesDarkMode == MainWindow.IsWindowsAppDarkMode() && profileManager.ProfileList.Items.Count == 2 && Descendants<System.Windows.Controls.Button>(profileManager).Any(x => x.Content?.ToString() == "割り当てをコピー") && Descendants<System.Windows.Controls.Button>(profileManager).Any(x => x.Content?.ToString() == "アプリを選ぶ…"), "dedicated profile manager provides profile editing, assignment clipboard and clear auto-switch app selection");
            profileManager.Close();
            var actionPicker = new ActionPickerWindow(window.ProfilesForTest, "JIS", deckLayouts: window.ConfigForTest.DeckLayouts) { Owner = window, ShowInTaskbar = false };
            actionPicker.Show();
            actionPicker.UpdateLayout();
            Check(actionPicker.TitleBarUsesDarkMode == MainWindow.IsWindowsAppDarkMode(), "action picker title bar follows the Windows app theme");
            Check(actionPicker.MajorCategoryList.Items.Count >= 9 && actionPicker.CategoryList.Items.Count > 0 && Equals(actionPicker.CategoryList.Items[0], ActionPickerWindow.AllCategories) && Equals(actionPicker.MajorCategoryList.Items[^1], "その他") && actionPicker.MajorCategoryList.Items.Contains("Deckパネル"), "action picker separates major categories, keeps Other last, and exposes Deck layouts as a dedicated major category");
            actionPicker.MajorCategoryList.SelectedItem = "Deckパネル";
            Pump(window);
            Check(actionPicker.CategoryList.Items.Count == 1 && Equals(actionPicker.CategoryList.Items[0], ActionPickerWindow.AllCategories) && actionPicker.ActionList.Items.Cast<CatalogAction>().Select(x => x.Name).SequenceEqual(window.ConfigForTest.DeckLayouts.Select(x => x.Name)), "Deck has no middle categories and lists each saved layout by name");
            actionPicker.MajorCategoryList.SelectedItem = "メディア";
            Pump(window);
            Check(Equals(actionPicker.CategoryList.SelectedItem, ActionPickerWindow.AllCategories) && actionPicker.ActionList.Items.Cast<CatalogAction>().Any() && actionPicker.ActionList.Items.Cast<CatalogAction>().All(x => x.MajorCategory == "メディア"), "changing a major category refreshes its All action list even when the middle-category label stays the same");
            Check(!actionPicker.ActionsForTest.Any(x => x.Kind is ActionKind.Disabled or ActionKind.Profile or ActionKind.Gesture) && actionPicker.CustomShortcutBox != null && actionPicker.UseShortcutButton != null, "the shortcut picker contains only actions plus direct keypad entry while dedicated buttons handle profile, gesture, and disable");
            var clockCatalogAction = actionPicker.ActionsForTest.First(x => x.Value == OverlayService.ClockAction);
            actionPicker.MajorCategoryList.SelectedItem = clockCatalogAction.MajorCategory;
            Pump(window);
            actionPicker.CategoryList.SelectedItem = ActionPickerWindow.AllCategories;
            Pump(window);
            actionPicker.ActionList.SelectedItem = actionPicker.ActionList.Items.Cast<CatalogAction>().First(x => x.Value == OverlayService.ClockAction);
            Pump(window);
            bool clockSettingsRequested = false;
            actionPicker.OpenClockSettingsForTest = _ => clockSettingsRequested = true;
            Check(actionPicker.ClockSettingsButton.Visibility == Visibility.Visible, "selecting the clock action reveals a direct clock-settings button");
            actionPicker.ClockSettingsButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(clockSettingsRequested, "the clock-settings button opens the overlay settings route without changing the selected action");
            Check(ActionPickerWindow.AddShortcutPart("Ctrl", "LeftCtrl") == "Ctrl" && ActionPickerWindow.AddShortcutPart("Ctrl+Alt", "RightCtrl") == "Ctrl+Alt" && ActionPickerWindow.AddShortcutPart("Ctrl+Alt", "Delete") == "Ctrl+Alt+Delete", "keypad shortcut input normalizes modifiers and does not duplicate Ctrl");
            actionPicker.SearchBox.Text = "IME";
            Pump(window);
            Check(actionPicker.ActionList.Items.Cast<CatalogAction>().Select(x => x.Value).Order().SequenceEqual(new[] { "ImeOff", "ImeOn", "ImeToggle" }.Order()), "action picker search finds all IME actions");
            actionPicker.Close();
            Check(MainWindow.ShortcutTextForKey(System.Windows.Input.Key.LeftCtrl, System.Windows.Input.ModifierKeys.None) == "Ctrl" && MainWindow.ShortcutTextForKey(System.Windows.Input.Key.C, System.Windows.Input.ModifierKeys.Control) == "Ctrl+C" && MainWindow.ShortcutTextForKey(System.Windows.Input.Key.C, System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift | System.Windows.Input.ModifierKeys.Alt) == "Ctrl+Shift+Alt+C", "physical keyboard entry records normalized two-key and three-modifier shortcuts without duplicates");
            window.SpaceLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            var spaceMouseLeft = window.VisualInputButtonsForTest.First(x => Equals(x.Tag, "MouseLeft"));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals("Space+MouseLeft", StringComparison.OrdinalIgnoreCase));
            spaceMouseLeft.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            var shiftClickAction = ActionCatalog.Items.Single(x => x.Name == "Shift+左クリック");
            window.ApplyCatalogActionForTest(shiftClickAction);
            Pump(window);
            var editingShiftClick = window.CurrentProfileForTest.Mappings.LastOrDefault(x => x.Input == "Space+MouseLeft");
            Check((ActionKind?)window.KindBox.SelectedValue == ActionKind.Shortcut && editingShiftClick is { Kind: ActionKind.Mouse, Value: "ShiftDrag" }, "choosing Shift+left click visibly selects Shortcut while preserving its drag-capable mouse action");
            window.CompleteDestinationInputForTest();
            Pump(window);
            var appliedShiftClick = window.AppliedMappingForTest("Space+MouseLeft");
            Check(appliedShiftClick is { Kind: ActionKind.Mouse, Value: "ShiftDrag" }, "Space-layer left click applies the Shift-click action through the native mouse-drag execution path");
            spaceMouseLeft.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.KindBox.SelectedValue = ActionKind.Shortcut;
            window.ValueBox.Text = "Shift+MouseLeft";
            Pump(window);
            var manualShiftClick = window.CurrentProfileForTest.Mappings.LastOrDefault(x => x.Input == "Space+MouseLeft");
            Check(manualShiftClick is { Kind: ActionKind.Mouse, Value: "ShiftDrag" }, "manually entered Shift+MouseLeft is normalized to the same reliable mouse action");
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            var sampleApps = new[] { new InstalledApplicationInfo("RELYR テスト", "C:\\Apps\\RELYR.exe", "インストール済みアプリ"), new InstalledApplicationInfo("メモ帳", "C:\\Windows\\notepad.exe", "スタート メニュー") };
            var applicationPicker = new ApplicationPickerWindow(sampleApps) { Owner = window, ShowInTaskbar = false };
            applicationPicker.Show();
            applicationPicker.UpdateLayout();
            Check(applicationPicker.TitleBarUsesDarkMode == MainWindow.IsWindowsAppDarkMode() && applicationPicker.ApplicationList.Items.Count == 2, "application picker follows the Windows theme and shows installed applications");
            applicationPicker.SearchBox.Text = "RELYR";
            Pump(window);
            Check(applicationPicker.ApplicationList.Items.Count == 1 && applicationPicker.ResultCount.Text == "1件", "application picker searches installed applications by name");
            applicationPicker.Close();
            var discoveredApps = ApplicationPickerWindow.DiscoverApplications();
            Check(discoveredApps.Count > 0 && discoveredApps.All(x => File.Exists(x.LaunchPath)), "installed application discovery returns launchable Start menu or registry entries");
            bool recordingState = false, captureMoves = false, mappedActions = false;
            var macroConfig = new AppConfig();
            var macro = new MacroWindow(macroConfig, (recording, capture, mapped) => { recordingState = recording; captureMoves = capture; mappedActions = mapped; }) { Owner = window, ShowInTaskbar = false };
            macro.Show();
            macro.UpdateLayout();
            Check(macro.TitleBarUsesDarkMode == MainWindow.IsWindowsAppDarkMode(), "macro title bar follows the Windows app theme");
            Check(Math.Abs(macro.MacroSearchBox.ActualHeight - 40) < .1 && Math.Abs(macro.NameBox.ActualHeight - 40) < .1 && Math.Abs(macro.MacroSearchBox.TranslatePoint(new System.Windows.Point(), macro).Y - macro.NameBox.TranslatePoint(new System.Windows.Point(), macro).Y) < .1, "macro search and name fields use the same height and align on one horizontal line");
            System.Windows.FrameworkElement[] manualFormControls = [macro.ManualTextBox, macro.AddTextActionButton, macro.WaitBox, macro.AddWaitButton];
            double[] manualLeftEdges = manualFormControls.Select(x => x.TranslatePoint(new System.Windows.Point(), macro).X).ToArray();
            double[] manualRightEdges = manualFormControls.Select(x => x.TranslatePoint(new System.Windows.Point(), macro).X + x.ActualWidth).ToArray();
            Check(manualLeftEdges.Max() - manualLeftEdges.Min() < .1 && manualRightEdges.Max() - manualRightEdges.Min() < .1 && Math.Abs(macro.ManualTextBox.ActualWidth - macro.WaitBox.ActualWidth) < .1, "manual macro text, wait fields, and action buttons share exact left and right edges");
            Check(macroConfig.Macros.Count == 0 && macro.EmptyHint.IsVisible && !macro.EditorPanel.IsEnabled, "macro window starts empty and waits for New");
            Check(macro.UseButton.Visibility == Visibility.Collapsed, "main macro manager hides the ambiguous assign button");
            Check(macro.MacroList.ActualWidth > 140 && macro.StepList.ActualWidth > 300 && macro.EditorTabs.ActualWidth > 240, "macro manager uses a readable three-pane layout");
            var macroListActions = new[] { macro.NewMacroButton, macro.DuplicateMacroButton, macro.EditMacroButton, macro.DeleteMacroButton };
            Check(macroListActions.All(button => button.Content is TextBlock text && text.FontFamily.Source == "Segoe MDL2 Assets" && Math.Abs(button.ActualHeight - 40) < .1) && macroListActions.Max(button => button.ActualWidth) - macroListActions.Min(button => button.ActualWidth) < .1 && macroListActions.All(button => button.ToolTip != null), "macro list actions use four equal icon-only controls with descriptive tooltips");
            Check(new[] { macro.ManualModeButton, macro.RecordModeButton, macro.StepEditModeButton }.Select(x => x.Content?.ToString()).SequenceEqual(["手動追加", "自動記録", "手順編集"]) && macro.EditorTabs.Template != null && macro.DropIndicator.Visibility == Visibility.Collapsed, "macro editing modes use ordinary app-styled buttons and keep the drag insertion guide hidden until needed");
            macro.RecordModeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(macro.EditorTabs.SelectedIndex == 1, "macro mode buttons switch the editor without old tab headers");
            macro.ManualModeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            macro.NewMacroButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(macroConfig.Macros.Count == 1 && !macro.NameBox.IsReadOnly && macro.NameBox.IsKeyboardFocusWithin && macro.EditMacroButton.IsEnabled && macro.ConfirmNameButton.IsVisible, "New creates a macro and immediately enters name editing");
            macro.NameBox.Text = "確定テスト";
            macro.ConfirmNameButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(macro.NameBox.IsReadOnly && !macro.ConfirmNameButton.IsVisible && macroConfig.Macros[0].Name == "確定テスト", "macro name has an explicit confirmation button");
            macro.AddManualKeyForTest(System.Windows.Input.Key.A);
            Check(macroConfig.Macros[0].Steps.Select(x => x.Event).SequenceEqual(["A Down", "A Up"]), "manual macro mode appends each pressed key as a safe down/up pair");
            Check(macro.StepList.Items.Cast<MacroWindow.StepView>().Any(x => x.Title.Contains("A") && x.Detail.Contains("Down")), "macro steps are displayed as human-readable operations");
            Check(Descendants<TextBlock>(macro).Any(x => x.Text.Contains("Ctrl + Shift + F12")), "macro stop shortcut is explained");
            Check(macro.RecordKeyboardBox.IsChecked == true && macro.RecordKeyboardBox.Content.ToString()!.Contains("キーボード操作"), "keyboard recording option exists and defaults on");
            Check(macro.RecordMappedActionsBox.IsChecked == true && macro.RecordPhysicalInputBox.IsChecked == false, "macro recording clearly defaults to assigned actions and offers physical-key mode");
            macro.RecordKeyboardBox.IsChecked = false;
            Check(!macroConfig.RecordKeyboardInputInMacros && !MacroWindow.ShouldRecordEvent("A Down", false) && MacroWindow.ShouldRecordEvent("MouseLeft Down", false), "keyboard recording can be disabled without excluding mouse input");
            Check(macro.RecordMouseMovesBox.IsChecked == false && macro.RecordMouseMovesBox.Content.ToString()!.Contains("移動軌跡"), "mouse trajectory recording option exists and defaults off");
            Check(macro.RelativeMouseMovementBox.Content.ToString()!.Contains("開始位置") && !macro.RelativeMouseMovementBox.Content.ToString()!.Contains("おすすめ") && macro.FixedMousePositionBox.Content.ToString()!.Contains("同じ位置"), "mouse trajectory offers neutral relative and fixed-position choices");
            int manualStepCount = macroConfig.Macros[0].Steps.Count;
            macro.RecordButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(recordingState && mappedActions && !captureMoves && !macro.RecordKeyboardBox.IsEnabled && macroConfig.Macros[0].Steps.Count == manualStepCount, "macro recording appends assigned actions without replacing manual steps");
            macro.Capture("Space Down");
            macro.Capture("Up Down");
            macro.CaptureMappedAction(new Mapping { Input = "Space+Up", Layer = "Space", Kind = ActionKind.Shortcut, Value = "Win+Left" }, "Space+Up");
            Check(macroConfig.Macros[0].Steps.Last().RecordedActionKind == ActionKind.Shortcut && macroConfig.Macros[0].Steps.Last().RecordedActionValue == "Win+Left" && !macroConfig.Macros[0].Steps.Skip(manualStepCount).Any(x => x.Event is "Space Down" or "Up Down"), "assigned-action mode records the resulting action without duplicate physical layer keys");
            macro.Capture("LeftCtrl Down");
            macro.Capture("LeftShift Down");
            macro.Capture("F12 Down");
            Check(!recordingState && macro.RecordKeyboardBox.IsEnabled, "macro stop shortcut still works when keyboard recording is off");
            macro.RecordKeyboardBox.IsChecked = true;
            macro.RecordMouseMovesBox.IsChecked = true;
            macro.RecordButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(recordingState && captureMoves && mappedActions, "macro recording includes assigned actions and mouse trajectory when enabled");
            Thread.Sleep(320);
            macro.Capture("MouseMove:5000,5000");
            macro.Capture("MouseMove:5012,4996");
            macro.RecordButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(macroConfig.Macros[0].Steps.Any(x => x.Event == "MouseMoveRelative:12,-4"), "relative mouse recording stores movement from the recording start instead of a fixed screen position");
            Check(macro.SaveButton.IsVisible && macro.ShortcutButton.IsVisible && macro.SaveButton.Content?.ToString() == "保存", "macro window clearly separates saving from desktop shortcut creation");
            macro.SuppressUnsavedPromptForTest = true;
            macro.Close();
            Check(macroConfig.Macros.Count == 0, "closing an unsaved new macro discards the draft");
            var savedConfig = new AppConfig { Macros = [new MacroDefinition { Name = "保存テスト", Steps = [new() { Event = "A Down" }, new() { Event = "A Up" }] }] };
            bool savedEvent = false;
            var savedMacro = new MacroWindow(savedConfig, (_, _, _) => { }) { Owner = window, ShowInTaskbar = false };
            savedMacro.Saved += () => savedEvent = true;
            savedMacro.Show();
            savedMacro.UpdateLayout();
            savedMacro.SaveButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(savedMacro.IsVisible && savedMacro.SaveRequested && savedEvent && savedMacro.FooterStatus.Text.Contains("開いたまま"), "saving a macro applies it without closing the macro window");
            savedMacro.Close();
            var assignConfig = new AppConfig { Macros = [new MacroDefinition { Name = "割り当てテスト", Steps = [new() { Event = "A Down" }, new() { Event = "A Up" }] }] };
            var assignMacro = new MacroWindow(assignConfig, (_, _, _) => { }, true, "Space+K（短押し）") { Owner = window, ShowInTaskbar = false };
            assignMacro.Show();
            assignMacro.UpdateLayout();
            Check(assignMacro.UseButton.Visibility == Visibility.Visible && assignMacro.UseButton.IsEnabled && assignMacro.AssignmentTargetText.Text.Contains("Space+K"), "assign button appears only with a clear assignment target");
            assignMacro.Close();
            var oldCursor = System.Windows.Forms.Cursor.Position;
            bool oldTopmost = window.Topmost;
            try
            {
                window.Topmost = true;
                window.Activate();
                Pump(window);
                var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                var center = window.PointToScreen(new System.Windows.Point(window.ActualWidth / 2, window.ActualHeight / 2));
                System.Windows.Forms.Cursor.Position = new System.Drawing.Point((int)center.X, (int)center.Y);
                if (WindowMonitorService.WindowUnderCursorForTest() != handle)
                {
                    output.WriteLine("SKIP toggle maximize under cursor: test session cursor is not over the test window");
                }
                else
                {
                    bool initialMaximized = WindowMonitorService.IsMaximizedForTest(handle);
                    WindowMonitorService.ToggleMaximizeUnderCursor();
                    bool toggledMaximized = WindowMonitorService.IsMaximizedForTest(handle);
                    WindowMonitorService.ToggleMaximizeUnderCursor();
                    Check(toggledMaximized != initialMaximized && WindowMonitorService.IsMaximizedForTest(handle) == initialMaximized, "toggle maximize under cursor works in both directions");
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("カーソルの位置")) { output.WriteLine("SKIP toggle maximize under cursor: test session does not expose cursor position"); }
            finally { window.Topmost = oldTopmost; System.Windows.Forms.Cursor.Position = oldCursor; }
            Window? cursorTargetWindow = null;
            Action? queuedCursorAction = null;
            try
            {
                cursorTargetWindow = new Window { Title = "RELYR cursor target test", Width = 260, Height = 140, Left = 80, Top = 80, ShowInTaskbar = false };
                cursorTargetWindow.Show();
                cursorTargetWindow.UpdateLayout();
                var cursorTargetHandle = new System.Windows.Interop.WindowInteropHelper(cursorTargetWindow).Handle;
                var cursorTargetCenter = cursorTargetWindow.PointToScreen(new System.Windows.Point(cursorTargetWindow.ActualWidth / 2, cursorTargetWindow.ActualHeight / 2));
                System.Windows.Forms.Cursor.Position = new System.Drawing.Point((int)cursorTargetCenter.X, (int)cursorTargetCenter.Y);
                Pump(window);
                if (WindowMonitorService.WindowUnderCursorForTest() != cursorTargetHandle)
                {
                    output.WriteLine("SKIP queued close under cursor: test session cursor is not over the target window");
                }
                else
                {
                    InputEngine.DesktopActionOutputForTest = action => queuedCursorAction = action;
                    InputEngine.SendShortcut("LeftAlt+F4", false, WindowActionTarget.WindowUnderCursor);
                    window.Activate();
                    var mainCenter = window.PointToScreen(new System.Windows.Point(window.ActualWidth / 2, window.ActualHeight / 2));
                    System.Windows.Forms.Cursor.Position = new System.Drawing.Point((int)mainCenter.X, (int)mainCenter.Y);
                    queuedCursorAction?.Invoke();
                    Pump(window);
                    Check(queuedCursorAction != null && !cursorTargetWindow.IsVisible && window.IsVisible, "LeftAlt+F4 closes the window captured under the cursor instead of the active window");
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("カーソルの位置")) { output.WriteLine("SKIP queued close under cursor: test session does not expose cursor position"); }
            finally { InputEngine.DesktopActionOutputForTest = null; cursorTargetWindow?.Close(); System.Windows.Forms.Cursor.Position = oldCursor; }
            var multiStepConfig = new AppConfig { Macros = [new MacroDefinition { Name = "複数選択", Steps = [new() { Event = "A Down" }, new() { Event = "B Down" }, new() { Event = "C Down" }] }] };
            var multiStepMacro = new MacroWindow(multiStepConfig, (_, _, _) => { }) { Owner = window, ShowInTaskbar = false };
            multiStepMacro.Show();
            multiStepMacro.UpdateLayout();
            Pump(window);
            Check(multiStepMacro.StepList.SelectionMode == System.Windows.Controls.SelectionMode.Extended, "macro steps use standard Shift-range and Ctrl-discontinuous selection");
            multiStepMacro.StepList.SelectedItems.Add(multiStepMacro.StepList.Items[0]);
            multiStepMacro.StepList.SelectedItems.Add(multiStepMacro.StepList.Items[2]);
            Descendants<System.Windows.Controls.Button>(multiStepMacro).First(x => x.Content?.ToString() == "選択した手順を削除").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(multiStepConfig.Macros[0].Steps.Select(x => x.Event).SequenceEqual(["B Down"]), "non-adjacent selected macro steps are deleted together");
            multiStepMacro.SuppressUnsavedPromptForTest = true;
            multiStepMacro.Close();
            var visualInputs = window.VisualInputButtonsForTest;
            var layerCases = new[] { ("通常", window.NormalLayerButton), ("Space", window.SpaceLayerButton), ("CapsLock", window.CapsLockLayerButton), ("MouseRight", window.RightMouseLayerButton), ("MouseBack", window.BackMouseLayerButton), ("MouseForward", window.ForwardMouseLayerButton), ("Taskbar", window.TaskbarLayerButton) };
            var visualKeys = visualInputs.Select(x => (string)x.Tag).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var replacementColor = MainWindow.AssignmentColorFor(new Mapping { Kind = ActionKind.Key });
            foreach (var (layer, layerButton) in layerCases)
            {
                foreach (string visualKey in visualKeys)
                {
                    if (visualKey == "CapsLock" || (visualKey == "Space" && layer is "通常" or "Space"))
                        continue;
                    string input = layer == "通常" ? visualKey : layer + "+" + visualKey;
                    window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
                    window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = input, Layer = layer, Kind = ActionKind.Key, Value = "A" });
                }
                layerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump(window);
                var missed = visualInputs.Where(x => (string)x.Tag != "CapsLock" && !((string)x.Tag == "Space" && layer is "通常" or "Space") && !HasBackgroundColor(x, replacementColor)).Select(x => (string)x.Tag).Distinct().ToArray();
                Check(visualInputs.Count > 100 && missed.Length == 0, $"every assignable visual key is orange on the {layer} layer" + (missed.Length == 0 ? "" : " (missing: " + string.Join(",", missed) + ")"));
            }
            string? pickedInput = null;
            var inputPicker = new MacroInputPickerWindow("JIS") { Owner = window, ShowInTaskbar = false };
            inputPicker.InputChosen += input => pickedInput = input;
            inputPicker.Show();
            inputPicker.UpdateLayout();
            Pump(window);
            var pickerInputs = inputPicker.InputButtonsForTest.Select(x => x.Tag?.ToString()).Where(x => x != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var requiredPickerInputs = new[] { "A", "Enter", "F13", "F24", "NumPadEnter", "MouseLeft", "MouseRight", "MouseMiddle", "MouseBack", "MouseForward", "MouseX", "WheelUp", "WheelDown", "TiltLeft", "TiltRight" };
            var missingPickerInputs = requiredPickerInputs.Where(x => !pickerInputs.Contains(x)).ToArray();
            var invalidPickerInputs = pickerInputs.Where(input => !InputEngine.IsValidRecordedEvent(input + " Down") || !InputEngine.IsValidRecordedEvent(input + " Up")).ToArray();
            bool pickerComplete = inputPicker.TitleBarUsesDarkMode == MainWindow.IsWindowsAppDarkMode() && pickerInputs.Count > 100 && missingPickerInputs.Length == 0 && invalidPickerInputs.Length == 0;
            Check(pickerComplete, "macro keypad follows the Windows theme and exposes the full keyboard and every supported mouse input" + (pickerComplete ? "" : $" (count={pickerInputs.Count}, missing={string.Join(",", missingPickerInputs)}, invalid={string.Join(",", invalidPickerInputs)}, titleDark={inputPicker.TitleBarUsesDarkMode}, windowsDark={MainWindow.IsWindowsAppDarkMode()})"));
            var pickerA = inputPicker.InputButtonsForTest.First(x => x.Tag?.ToString() == "A");
            var pickerEnter = inputPicker.InputButtonsForTest.First(x => x.Tag?.ToString() == "Enter");
            pickerEnter.ApplyTemplate();
            var pickerEnterShape = pickerEnter.Template.FindName("EnterShape", pickerEnter) as System.Windows.Shapes.Path;
            Check(pickerEnter.Style == inputPicker.FindResource("JisEnterButton") && pickerEnter.Clip != null && pickerEnterShape is { StrokeThickness: 1, StrokeLineJoin: PenLineJoin.Round } && inputPicker.InputSurfaceBorder.Background == ThemeService.Brush("AppBackground") && !inputPicker.InputCanvas.Children.OfType<System.Windows.Shapes.Path>().Any(), "macro keypad uses the quiet app background and the same single rounded JIS Enter surface as the main screen");
            var pickerBackspace = inputPicker.InputButtonsForTest.First(x => x.Tag?.ToString() == "Back");
            var pickerUpperBracket = inputPicker.InputButtonsForTest.First(x => x.Tag?.ToString() == "[");
            var pickerRightBracket = inputPicker.InputButtonsForTest.First(x => x.Tag?.ToString() == "]");
            var pickerRightShift = inputPicker.InputButtonsForTest.First(x => x.Tag?.ToString() == "RightShift");
            Check(pickerEnterShape != null && Math.Abs(pickerEnterShape.Data.Bounds.Width - 160) < .1 && Math.Abs(pickerEnterShape.Data.Bounds.Height - 106.86) < .01
                && !pickerEnterShape.Data.FillContains(new System.Windows.Point(1, 51)) && pickerEnterShape.Data.FillContains(new System.Windows.Point(9, 51)) && !pickerEnterShape.Data.FillContains(new System.Windows.Point(1, 53)) && !pickerEnterShape.Data.FillContains(new System.Windows.Point(19, 53)) && pickerEnterShape.Data.FillContains(new System.Windows.Point(21, 53))
                && Math.Abs(Canvas.GetTop(pickerEnter) - (Canvas.GetTop(pickerBackspace) + pickerBackspace.Height) - 4) < .1
                && Math.Abs(Canvas.GetLeft(pickerEnter) - (Canvas.GetLeft(pickerUpperBracket) + pickerUpperBracket.Width) - 4) < .1
                && Math.Abs(Canvas.GetLeft(pickerEnter) + 24 - (Canvas.GetLeft(pickerRightBracket) + pickerRightBracket.Width) - 6) < .1
                && Math.Abs(Canvas.GetTop(pickerRightShift) - (Canvas.GetTop(pickerEnter) + pickerEnter.Height) - 4) < .1,
                "macro keypad mirrors the main JIS Enter geometry with uniform four-pixel gaps on every adjoining edge");
            var pickerNavigation = inputPicker.InputButtonsForTest.Where(x => x.Tag?.ToString() is "Insert" or "Home" or "PageUp" or "Left" or "Up" or "Right" or "Down").ToArray();
            var pickerFrames = inputPicker.InputCanvas.Children.OfType<Border>().Where(x => x.Tag is string).Select(x => x.Tag!.ToString()).ToHashSet();
            var pickerBack = inputPicker.InputButtonsForTest.First(x => x.Tag?.ToString() == "MouseBack");
            var pickerForward = inputPicker.InputButtonsForTest.First(x => x.Tag?.ToString() == "MouseForward");
            Check(Canvas.GetTop(pickerForward) < Canvas.GetTop(pickerBack) && pickerForward.Content?.ToString() == "進む" && pickerBack.Content?.ToString() == "戻る", "macro keypad uses the same conventional Forward-above-Back mouse layout as the main screen");
            inputPicker.Width = 780;
            inputPicker.Height = 540;
            inputPicker.UpdateLayout();
            Pump(window);
            bool pickerContained = inputPicker.InputButtonsForTest.All(x => Canvas.GetLeft(x) >= 0 && Canvas.GetTop(x) >= 0 && Canvas.GetLeft(x) + x.Width <= inputPicker.InputCanvas.Width + .1 && Canvas.GetTop(x) + x.Height <= inputPicker.InputCanvas.Height + .1);
            Check(!Descendants<ScrollViewer>(inputPicker).Any() && inputPicker.InputViewbox.Stretch == Stretch.Uniform && pickerNavigation.All(x => Math.Abs(x.Height - pickerA.Height) < .1) && new[] { "ナビゲーション", "テンキー", "カーソルキー", "マウス" }.All(pickerFrames.Contains) && inputPicker.InputViewbox.ActualWidth > 0 && inputPicker.InputViewbox.ActualHeight > 0, "macro keypad scales as one complete surface without scrollbars");
            CaptureForReview(inputPicker, "mouse-layout-keypad.png");
            inputPicker.InputButtonsForTest.First(x => x.Tag?.ToString() == "MouseRight").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(pickedInput == "MouseRight" && inputPicker.IsVisible && inputPicker.StatusText.Text.Contains("右クリック"), "macro keypad adds an input immediately and remains open for consecutive choices");
            inputPicker.Close();
            var keypadMacroConfig = new AppConfig { Macros = [new MacroDefinition { Name = "keypad input" }] };
            var keypadMacro = new MacroWindow(keypadMacroConfig, (_, _, _) => { }) { Owner = window, ShowInTaskbar = false };
            keypadMacro.Show();
            keypadMacro.UpdateLayout();
            keypadMacro.AddInputFromKeypadForTest("MouseRight");
            keypadMacro.AddInputFromKeypadForTest("WheelUp");
            Check(keypadMacroConfig.Macros[0].Steps.Select(x => x.Event).SequenceEqual(["MouseRight Down", "MouseRight Up", "WheelUp Down", "WheelUp Up"]) && keypadMacroConfig.Macros[0].Steps.All(x => InputEngine.IsValidRecordedEvent(x.Event)), "keypad selections append safe down/up pairs for clicks and wheel operations");
            keypadMacro.SuppressUnsavedPromptForTest = true;
            keypadMacro.Close();
            var contextStepConfig = new AppConfig { Macros = [new MacroDefinition { Name = "context menu", Steps = [new() { Event = "A Down" }, new() { Event = "B Down" }, new() { Event = "C Down" }] }] };
            var contextStepMacro = new MacroWindow(contextStepConfig, (_, _, _) => { }) { Owner = window, ShowInTaskbar = false };
            contextStepMacro.Show();
            contextStepMacro.UpdateLayout();
            Pump(window);
            Check(new[] { contextStepMacro.CopyStepsMenuItem.Header?.ToString(), contextStepMacro.PasteStepsMenuItem.Header?.ToString(), contextStepMacro.DeleteStepsMenuItem.Header?.ToString() }.SequenceEqual(["コピー", "貼り付け", "削除"]), "macro steps provide a concise right-click copy, paste and delete menu");
            contextStepMacro.StepList.SelectedItems.Add(contextStepMacro.StepList.Items[0]);
            contextStepMacro.StepList.SelectedItems.Add(contextStepMacro.StepList.Items[2]);
            contextStepMacro.CopyStepsMenuItem.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            contextStepMacro.StepList.SelectedItems.Clear();
            contextStepMacro.StepList.SelectedItem = contextStepMacro.StepList.Items[2];
            contextStepMacro.PasteStepsMenuItem.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Check(contextStepConfig.Macros[0].Steps.Select(x => x.Event).SequenceEqual(["A Down", "B Down", "C Down", "A Down", "C Down"]) && contextStepMacro.StepList.SelectedItems.Count == 2, "right-click paste preserves copied step order and selects the inserted copies");
            contextStepMacro.DeleteStepsMenuItem.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.MenuItem.ClickEvent));
            Check(contextStepConfig.Macros[0].Steps.Select(x => x.Event).SequenceEqual(["A Down", "B Down", "C Down"]), "right-click delete removes all selected macro steps in one operation");
            contextStepMacro.SuppressUnsavedPromptForTest = true;
            contextStepMacro.Close();
            var coordinateConfig = new AppConfig { Macros = [new MacroDefinition { Name = "coordinate capture" }] };
            var coordinateMacro = new MacroWindow(coordinateConfig, (_, _, _) => { }) { Owner = window, ShowInTaskbar = false };
            coordinateMacro.Show();
            coordinateMacro.UpdateLayout();
            coordinateMacro.CoordinateCaptureButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(coordinateMacro.CoordinateCaptureActiveForTest && coordinateMacro.CoordinateCaptureButton.Content?.ToString()?.Contains("Esc") == true, "coordinate button clearly enters one-click capture mode");
            using (var coordinateEngine = new InputEngine())
            {
                var coordinateDown = coordinateEngine.DirectMouseForTest(0x201, 0, 321, 654);
                var coordinateUp = coordinateEngine.DirectMouseForTest(0x202, 0, 321, 654);
                Pump(window);
                Check(coordinateDown == (IntPtr)1 && coordinateUp == (IntPtr)1 && coordinateConfig.Macros[0].Steps.Select(x => x.Event).SequenceEqual(["MouseMove:321,654", "MouseLeft Down", "MouseLeft Up"]) && !coordinateMacro.CoordinateCaptureActiveForTest && !InputEngine.CoordinateCapturePendingForTest && coordinateMacro.CoordinateCaptureButton.Content?.ToString() == "座標を記録", "one captured coordinate appends a compact move-and-click macro and automatically leaves capture mode");
                coordinateMacro.CoordinateCaptureButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                var escapeSource = System.Windows.PresentationSource.FromVisual(coordinateMacro);
                if (escapeSource != null)
                {
                    var escape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, escapeSource, Environment.TickCount, System.Windows.Input.Key.Escape) { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
                    coordinateMacro.RaiseEvent(escape);
                    Check(escape.Handled && !coordinateMacro.CoordinateCaptureActiveForTest && !InputEngine.CoordinateCapturePendingForTest && coordinateConfig.Macros[0].Steps.Count == 3, "Escape cancels coordinate capture without adding steps");
                }
            }
            coordinateMacro.SuppressUnsavedPromptForTest = true;
            coordinateMacro.Close();
            ThemeService.Apply(AppThemeMode.Light);
            Check(ThemeService.Color("AppBackground") == System.Windows.Media.Color.FromRgb(0xF3, 0xF3, 0xF3), "light mode uses the neutral Windows background tone");
            window.ProfileBox.IsDropDownOpen = true;
            Pump(window);
            var selectedProfileItem = window.ProfileBox.ItemContainerGenerator.ContainerFromItem(window.ProfileBox.SelectedItem) as ComboBoxItem;
            Check(selectedProfileItem != null && selectedProfileItem.Template != null && selectedProfileItem.Foreground is SolidColorBrush profileItemForeground && profileItemForeground.Color == ThemeService.Color("PrimaryText"), "light profile choices retain a custom theme-aware row and dark readable foreground");
            window.ProfileBox.IsDropDownOpen = false;
            Pump(window);
            var lightProfileManager = new ProfileManagerWindow([new Profile { Name = "標準" }, new Profile { Name = "編集用" }], "標準") { Owner = window, ShowInTaskbar = false };
            lightProfileManager.Show();
            lightProfileManager.UpdateLayout();
            var lightProfilePrimaryButtons = new[] { lightProfileManager.AddProfileButton, lightProfileManager.RenameProfileButton };
            Check(lightProfilePrimaryButtons.All(button => Descendants<System.Windows.Shapes.Path>(button).All(path => Equals(path.Stroke, button.Foreground))) && lightProfilePrimaryButtons.All(button => button.Foreground is SolidColorBrush brush && DeckPanelLayout.ContrastRatio(brush.Color, ThemeService.Color("ControlBackground")) >= 4.5) && Descendants<System.Windows.Shapes.Path>(lightProfileManager.DeleteProfileButton).All(path => Equals(path.Stroke, lightProfileManager.DeleteProfileButton.Foreground)), "profile action icons inherit readable button colors in the light theme, including the white trash icon on red");
            CaptureForReview(lightProfileManager, "profile-manager-light.png");
            lightProfileManager.Close();
            var lightDeckIconPicker = new DeckIconPickerWindow("home") { Owner = window, ShowInTaskbar = false };
            lightDeckIconPicker.Show();
            lightDeckIconPicker.UpdateLayout();
            var animatedPresetVisual = DeckIconCatalog.CreateVisual(new Mapping { DeckIcon = DeckIconCatalog.AnimatedId("refresh") }, 22, false);
            bool animatedPresetIsRunning = animatedPresetVisual is TextBlock { RenderTransform: TransformGroup animatedTransforms } && animatedTransforms.Children.Any(transform => transform.HasAnimatedProperties);
            var animatedScissors = DeckIconCatalog.CreateVisual(new Mapping { DeckIcon = DeckIconCatalog.AnimatedId("cut") }, 22, false);
            bool scissorsSnipIsRunning = animatedScissors is TextBlock { RenderTransform: TransformGroup scissorsTransforms }
                && scissorsTransforms.Children.OfType<ScaleTransform>().Any(transform => transform.HasAnimatedProperties)
                && scissorsTransforms.Children.OfType<RotateTransform>().Any(transform => transform.HasAnimatedProperties);
            Check(lightDeckIconPicker.PresetCountForTest == 100 && lightDeckIconPicker.AnimatedPresetCountForTest == 100 && lightDeckIconPicker.BrowseButton.IsVisible && lightDeckIconPicker.SelectedPresetId == "home" && animatedPresetIsRunning && scissorsSnipIsRunning && Descendants<System.Windows.Controls.Button>(lightDeckIconPicker.PresetPanel).All(button => button.Foreground is SolidColorBrush brush && DeckPanelLayout.ContrastRatio(brush.Color, ThemeService.Color("ControlBackground")) >= 4.5), $"Deck icon picker separates 100 theme-readable still presets from 100 continuously animated presets, including the scissors-specific two-part snip motion, and retains custom image browsing (still={lightDeckIconPicker.PresetCountForTest}, animated={lightDeckIconPicker.AnimatedPresetCountForTest}, running={animatedPresetIsRunning}, scissors={scissorsSnipIsRunning})");
            string customGifPath = Path.Combine(testConfigDirectory, "custom-deck-icon.gif");
            var gifEncoder = new GifBitmapEncoder();
            foreach (byte red in new byte[] { 0, 255 })
            {
                var pixels = new byte[16];
                for (int pixel = 0; pixel < 4; pixel++)
                {
                    pixels[pixel * 4 + 2] = red;
                    pixels[pixel * 4 + 3] = 255;
                }
                gifEncoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, pixels, 8)));
            }
            using (var gifStream = File.Create(customGifPath))
                gifEncoder.Save(gifStream);
            var customGifIcon = DeckIconCatalog.CreateVisual(new Mapping { DeckIconPath = customGifPath }, 28, false) as AnimatedGifIcon;
            var gifHost = new Window { Width = 80, Height = 80, Content = customGifIcon, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            gifHost.Show();
            PumpFor(TimeSpan.FromMilliseconds(600));
            long gifAdvancesBefore = customGifIcon?.FrameAdvanceCountForTest ?? -1;
            PumpFor(TimeSpan.FromMilliseconds(240));
            Check(customGifIcon is { FrameCountForTest: >= 2, Source: not null } && customGifIcon.FrameAdvanceCountForTest > gifAdvancesBefore, $"a user-selected GIF is bounded, decoded off the UI path, and continuously advances through its loop (frames={customGifIcon?.FrameCountForTest ?? 0}, advances={customGifIcon?.FrameAdvanceCountForTest ?? 0})");
            gifHost.Close();
            CaptureForReview(lightDeckIconPicker, "deck-icon-picker-light.png");
            lightDeckIconPicker.Close();
            var lightProfileOverlay = new ProfileSwitchOverlay("ライト確認", TimeSpan.FromSeconds(2));
            Check(lightProfileOverlay.Opacity == 0 && lightProfileOverlay.ThemeAppliedBeforeRevealForTest && lightProfileOverlay.SurfaceColorForTest == ThemeService.Color("CardBackground"), "the light profile notification is fully themed while still hidden before its first frame");
            lightProfileOverlay.Show();
            PumpFor(TimeSpan.FromMilliseconds(40));
            Check(lightProfileOverlay.Opacity == 1 && lightProfileOverlay.SurfaceColorForTest == ThemeService.Color("CardBackground"), "the profile notification reveals directly in the light theme without a dark first frame");
            CaptureForReview(lightProfileOverlay, "profile-overlay-light.png");
            lightProfileOverlay.Close();
            window.CurrentProfileForTest.Mappings.Clear();
            window.DeckPanelManagerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.EditDeckLayoutForTest(standardDeck);
            Pump(window);
            window.DeckSizePresetBox.SelectedItem = window.DeckSizePresetBox.Items.Cast<ComboBoxItem>().First(item => Equals(item.Tag, "custom"));
            Pump(window);
            window.DeckColumnsBox.Text = "１２";
            window.DeckRowsBox.Text = "１８";
            window.Activate();
            window.Focus();
            window.DeckColumnsBox.Focus();
            System.Windows.Input.Keyboard.Focus(window.DeckColumnsBox);
            Pump(window);
            Check(window.DeckColumnsBox.Width >= 64 && window.DeckColumnsBox.Height >= 40 && window.DeckRowsBox.Width >= 64 && window.DeckRowsBox.Height >= 40 && window.DeckColumnsBox.Text == "１２" && MainWindow.TryResolveDeckLayoutSize("custom", window.DeckColumnsBox.Text, window.DeckRowsBox.Text, out int lightColumns, out int lightRows) && lightColumns == 12 && lightRows == 18, "Deck dimension fields are large enough to read and accept full-width digits");
            Check(window.DeckColumnsBox.IsKeyboardFocusWithin && !window.ShouldInterceptPhysicalInputForTest, $"physical keyboard input passes through RELYR while a Deck dimension field owns focus (visible={window.DeckColumnsBox.IsVisible}, enabled={window.DeckColumnsBox.IsEnabled}, focus={window.DeckColumnsBox.IsKeyboardFocusWithin}, focused={System.Windows.Input.Keyboard.FocusedElement?.GetType().Name ?? "none"}, intercept={window.ShouldInterceptPhysicalInputForTest})");
            var lightAudioButton = window.DeckManagementButtonsForTest[2];
            Check(Descendants<System.Windows.Shapes.Path>(lightAudioButton).Any(path => Equals(path.Fill, lightAudioButton.Foreground)), "Deck audio and play icons inherit readable button text color in the light theme");
            CaptureForReview(window, "deck-editor-light.png");
            window.DeckPanelManagerButton.Focus();
            System.Windows.Input.Keyboard.Focus(window.DeckPanelManagerButton);
            Pump(window);
            Check(window.ShouldInterceptPhysicalInputForTest, "global RELYR input interception resumes when a text field no longer owns keyboard focus");
            window.ShowDeckLayoutListForTest();
            Pump(window);
            var lightDeckPreviewCells = Descendants<Border>(window.DeckLayoutCardsPanel).Where(border => ReferenceEquals(border.Background, ThemeService.Brush("DeckPreviewCellBackground"))).ToArray();
            var lightDeckCards = window.DeckLayoutCardsPanel.Children.OfType<System.Windows.Controls.Button>().Where(button => Descendants<TextBlock>(button).Any(text => Equals(text.Tag, "DeckLayoutName"))).ToArray();
            var lightDeckCellColor = ThemeService.Color("DeckPreviewCellBackground");
            var lightDeckCardColor = ThemeService.Color("CardBackground");
            int lightDeckContrast = Math.Abs(lightDeckCellColor.R - lightDeckCardColor.R) + Math.Abs(lightDeckCellColor.G - lightDeckCardColor.G) + Math.Abs(lightDeckCellColor.B - lightDeckCardColor.B);
            var lightDeckNameTops = lightDeckCards.Select(card => Descendants<TextBlock>(card).Single(text => Equals(text.Tag, "DeckLayoutName")).TranslatePoint(new System.Windows.Point(), card).Y).ToArray();
            Check(lightDeckPreviewCells.Length > 0 && lightDeckContrast >= 60, $"light-theme Deck thumbnails remain visible against white cards (cells={lightDeckPreviewCells.Length}, contrast={lightDeckContrast})");
            Check(lightDeckNameTops.Length > 0 && lightDeckNameTops.Max() - lightDeckNameTops.Min() < .1, $"every Deck card places its name on one horizontal line (spread={lightDeckNameTops.Max() - lightDeckNameTops.Min():F2})");
            ThemeService.Apply(AppThemeMode.Dark);
            Pump(window);
            var darkDeckCardsAfterSwitch = window.DeckLayoutCardsPanel.Children.OfType<System.Windows.Controls.Button>().Where(button => Descendants<TextBlock>(button).Any(text => Equals(text.Tag, "DeckLayoutName"))).ToArray();
            Check(darkDeckCardsAfterSwitch.Length == lightDeckCards.Length && darkDeckCardsAfterSwitch.All(card => card.Background is SolidColorBrush brush && brush.Color == ThemeService.Color("CardBackground")) && darkDeckCardsAfterSwitch.All(card => Descendants<TextBlock>(card).Single(text => Equals(text.Tag, "DeckLayoutName")).Foreground is SolidColorBrush brush && brush.Color == ThemeService.Color("PrimaryText")), "Deck cards fully recolor when switching directly from light to dark theme");
            ThemeService.Apply(AppThemeMode.Light);
            Pump(window);
            CaptureForReview(window, "redesign-light-deck.png");
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.Width = 1280;
            window.Height = 900;
            window.LayerNavigationScrollViewer.ScrollToTop();
            window.UpdateLayout();
            Pump(window);
            var lightSelectedKey = window.VisualInputButtonsForTest.First(button => button.Tag?.ToString() == "E");
            lightSelectedKey.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(lightSelectedKey.Foreground is SolidColorBrush selectedKeyText && selectedKeyText.Color == ThemeService.Color("PrimaryText") && DeckPanelLayout.ContrastRatio(selectedKeyText.Color, ThemeService.Color("AccentSoftBrush")) >= 4.5, "an unassigned selected key keeps readable text in the light theme");
            var lightGestureManager = new GestureManagerWindow([new GestureDefinition { Name = "ライト確認" }], [.. window.ProfilesForTest], [.. window.ConfigForTest.Macros], "JIS") { Owner = window, ShowInTaskbar = false };
            lightGestureManager.Show();
            lightGestureManager.UpdateLayout();
            var lightGestureIconButtons = Descendants<System.Windows.Controls.Button>(lightGestureManager).Where(button => button.ToolTip?.ToString()?.StartsWith("ジェスチャー", StringComparison.Ordinal) == true).ToArray();
            Check(lightGestureIconButtons.Length == 3 && lightGestureIconButtons.All(button => Descendants<System.Windows.Shapes.Path>(button).All(path => Equals(path.Stroke, button.Foreground))), "gesture add, rename, and delete icons inherit a visible theme-aware button foreground");
            CaptureForReview(lightGestureManager, "gesture-manager-light.png");
            lightGestureManager.Close();
            CaptureForReview(window, "redesign-light-main.png");
            ThemeService.Apply(AppThemeMode.Dark);
            window.CurrentProfileForTest.Mappings.Clear();
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.Width = 1280;
            window.Height = 900;
            window.LayerNavigationScrollViewer.ScrollToTop();
            window.UpdateLayout();
            Pump(window);
            CaptureForReview(window, "redesign-dark-main.png");
            window.DeckPanelManagerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            CaptureForReview(window, "redesign-dark-deck.png");
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            bool newDeckDialogInspected = false;
            window.NewDeckDialogLoadedForTest = dialog => { dialog.UpdateLayout(); var sizeBox = Descendants<System.Windows.Controls.ComboBox>(dialog).First(x => x.Name == "NewDeckSizeBox"); sizeBox.SelectedIndex = 3; dialog.UpdateLayout(); var customRow = Descendants<Grid>(dialog).First(x => x.Name == "NewDeckCustomSizeRow"); Check(Math.Abs(sizeBox.ActualHeight - 40) < .1 && sizeBox.ActualWidth <= 220.1 && sizeBox.Items.Count == 4 && customRow.Visibility == Visibility.Visible && dialog.Background == ThemeService.Brush("SurfaceBackground"), "new Deck dialog stays compact and themed while exposing custom columns and rows"); CaptureForReview(dialog, "new-deck-dialog-dark.png"); newDeckDialogInspected = true; dialog.DialogResult = false; };
            window.ShowNewDeckDialogForTest();
            window.NewDeckDialogLoadedForTest = null;
            Check(newDeckDialogInspected, "new Deck dialog is rendered and inspected in dark mode");
            window.PrepareForSystemShutdown();
            Check(window.IsInputHookDisposedForTest, "system shutdown immediately disposes keyboard and mouse hooks");
        }
        catch (Exception ex) { report.RecordException("UI exception", "FAIL UI exception: ", ex); }
        finally { if (window != null) { window.PrepareForSystemShutdown(); window.Close(); } Environment.SetEnvironmentVariable("RELYR_CONFIG_DIR", previousConfigDirectory); try { if (Directory.Exists(testConfigDirectory)) Directory.Delete(testConfigDirectory, true); } catch { } }
        return report.Complete("UI INTEGRATION TEST PASSED", "UI INTEGRATION TEST FAILED: ");
    }
    static bool IsDark(System.Windows.Media.Brush brush) => brush is SolidColorBrush b && b.Color.R < 80 && b.Color.G < 80 && b.Color.B < 80;
    static void PumpFor(TimeSpan duration)
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        var timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
    static void CaptureForReview(Window window, string fileName)
    {
        string? directory = Environment.GetEnvironmentVariable("RELYR_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return;
        window.UpdateLayout();
        int width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, fileName));
        encoder.Save(stream);
    }
    static Window CreateBackdropProbeWindow()
    {
        var grid = new Grid { Background = new LinearGradientBrush(System.Windows.Media.Color.FromRgb(24, 45, 66), System.Windows.Media.Color.FromRgb(72, 31, 42), new System.Windows.Point(0, 0), new System.Windows.Point(1, 1)) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new Border { Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(24, 117, 126)), Margin = new Thickness(22), CornerRadius = new CornerRadius(18), Opacity = .92 });
        var right = new Border { Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(154, 71, 54)), Margin = new Thickness(22), CornerRadius = new CornerRadius(18), Opacity = .92 };
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        grid.Children.Add(new TextBlock { Text = "BACKDROP\nAcrylic verification", FontSize = 48, FontWeight = FontWeights.SemiBold, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(46), VerticalAlignment = VerticalAlignment.Top });
        return new Window { Left = 32, Top = 64, Width = 920, Height = 660, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = System.Windows.Media.Brushes.Transparent, Content = grid };
    }
    static bool CaptureDesktopForReview(Window window, string fileName)
    {
        string? directory = Environment.GetEnvironmentVariable("RELYR_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return true;
        try
        {
            IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
                return false;
            const int padding = 22;
            int width = Math.Max(1, rect.Right - rect.Left + padding * 2);
            int height = Math.Max(1, rect.Bottom - rect.Top + padding * 2);
            using var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(rect.Left - padding, rect.Top - padding, 0, 0, new System.Drawing.Size(width, height), System.Drawing.CopyPixelOperation.SourceCopy);
            Directory.CreateDirectory(directory);
            bitmap.Save(Path.Combine(directory, fileName), System.Drawing.Imaging.ImageFormat.Png);
            return true;
        }
        catch
        {
            return false;
        }
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct NativeRectangle { internal int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] static extern bool GetWindowRect(IntPtr hwnd, out NativeRectangle rectangle);
    static bool HasBackgroundColor(System.Windows.Controls.Button button, System.Windows.Media.Color expected) => button.Background is SolidColorBrush b && b.Color == expected;
    static IEnumerable<double> AdjacentGaps(IReadOnlyList<System.Windows.Controls.Button> keys)
    {
        for (int i = 1; i < keys.Count; i++)
            yield return Canvas.GetLeft(keys[i]) - (Canvas.GetLeft(keys[i - 1]) + keys[i - 1].Width);
    }
    static bool NoOverlaps(IReadOnlyList<System.Windows.Controls.Button> keys)
    {
        for (int i = 0; i < keys.Count; i++)
        for (int j = i + 1; j < keys.Count; j++)
        if (new Rect(Canvas.GetLeft(keys[i]), Canvas.GetTop(keys[i]), keys[i].Width, keys[i].Height).IntersectsWith(new Rect(Canvas.GetLeft(keys[j]), Canvas.GetTop(keys[j]), keys[j].Width, keys[j].Height)))
            return false;
        return true;
    }
    static double RenderedWidth(FrameworkElement element, Visual ancestor) => element.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)).Width;
    static double RenderedHeight(FrameworkElement element, Visual ancestor) => element.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, element.ActualWidth, element.ActualHeight)).Height;
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item)
                yield return item;
            foreach (var nested in Descendants<T>(child))
                yield return nested;
        }
        if (count == 0 && root is ContentControl { Content: DependencyObject content })
        {
            if (content is T item)
                yield return item;
            foreach (var nested in Descendants<T>(content))
                yield return nested;
        }
    }
}
