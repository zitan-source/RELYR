using System.Collections.Concurrent;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MenuItem = System.Windows.Controls.MenuItem;

namespace RELYR;

internal static class UiIntegrationTest
{
    internal static int RunMouseLayout(TextWriter output)
    {
        var report = new VerificationReport(output);
        Action<bool, string> Check = report.Check;
        string? previousConfigDirectory = Environment.GetEnvironmentVariable("RELYR_CONFIG_DIR");
        string testConfigDirectory = VerificationPaths.CreateRunDirectory("mouse-ui-test");
        AppThemeMode previousTheme = ThemeService.CurrentMode;
        MainWindow? window = null;
        MacroInputPickerWindow? picker = null;
        try
        {
            Environment.SetEnvironmentVariable("RELYR_CONFIG_DIR", testConfigDirectory);
            new ConfigService().Save(new AppConfig { FirstRunCompleted = true, CapsLockLayerWarningAccepted = true, CheckForUpdates = false, ThemeMode = AppThemeMode.Dark });
            ThemeService.Apply(AppThemeMode.Dark);
            window = new MainWindow(true, suppressTray: true, startInputHooks: false) { Width = 1500, Height = 900 };
            System.Windows.Application.Current.MainWindow = window;
            window.Show();
            window.UpdateLayout();
            for (int i = 0; i < 3; i++)
            {
                window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
                window.UpdateLayout();
            }

            var aKey = window.KeyboardPanel.Children.OfType<System.Windows.Controls.Button>().First(x => Equals(x.Tag, "A"));
            var mouseButtons = Descendants<System.Windows.Controls.Button>(window.MousePanel).Where(x => x.Tag is string).ToList();
            var leftClick = mouseButtons.First(x => Equals(x.Tag, "MouseLeft"));
            var rightClick = mouseButtons.First(x => Equals(x.Tag, "MouseRight"));
            Check(!leftClick.IsEnabled && leftClick.Opacity < .6 && !window.RuntimeInterceptsInputForTest("MouseLeft"), "normal-layer left click is visibly disabled and cannot intercept Windows click or drag input even if an old mapping remains");
            Check(DeckPanelLayout.ExternalFileDragEffects == System.Windows.DragDropEffects.Copy, "Deck file drags expose copy-only semantics so Explorer cannot move or delete the registered source file");
            var ordinary = mouseButtons.Where(x => !Equals(x.Tag, "MouseLeft") && !Equals(x.Tag, "MouseRight")).ToList();
            var wheelDown = mouseButtons.First(x => Equals(x.Tag, "WheelDown"));
            var tiltLeft = mouseButtons.First(x => Equals(x.Tag, "TiltLeft"));
            var tiltRight = mouseButtons.First(x => Equals(x.Tag, "TiltRight"));
            var forward = mouseButtons.First(x => Equals(x.Tag, "MouseForward"));
            var back = mouseButtons.First(x => Equals(x.Tag, "MouseBack"));
            var x1 = mouseButtons.First(x => Equals(x.Tag, "MouseX"));
            static Rect Bounds(System.Windows.Controls.Button button) => new(Canvas.GetLeft(button), Canvas.GetTop(button), button.Width, button.Height);

            Check(mouseButtons.Count == 10, "mouse diagram exposes exactly the ten existing mouse actions");
            Check(ordinary.All(x => Math.Abs(x.Width - aKey.Width) < .1 && Math.Abs(x.Height - aKey.Height) < .1), "every ordinary mouse control exactly matches the active-layout A-key size");
            double renderedAHeight = aKey.TransformToAncestor(window).TransformBounds(new Rect(0, 0, aKey.ActualWidth, aKey.ActualHeight)).Height;
            double renderedMouseKeyHeight = ordinary[0].TransformToAncestor(window).TransformBounds(new Rect(0, 0, ordinary[0].ActualWidth, ordinary[0].ActualHeight)).Height;
            Check(Math.Abs(renderedMouseKeyHeight - renderedAHeight) < .1, "ordinary mouse controls render at the same on-screen height as the A key");
            Check(new[] { leftClick, rightClick }.All(x => Math.Abs(x.Width - aKey.Width) < .1 && Math.Abs(x.Height - (aKey.Height * 3 + 8)) < .1 && Math.Abs(Canvas.GetTop(x) + x.Height - (Canvas.GetTop(wheelDown) + wheelDown.Height)) < .1), "left and right click extend exactly to the wheel-down edge");
            Check(Canvas.GetTop(tiltLeft) > Canvas.GetTop(wheelDown) + wheelDown.Height && Math.Abs((Canvas.GetLeft(tiltLeft) + tiltLeft.Width + 2) - window.MousePanel.Width / 2) < .1 && Math.Abs((Canvas.GetLeft(tiltRight) - 2) - window.MousePanel.Width / 2) < .1, "tilt controls sit immediately below the wheel and share the horizontal center");
            Check(Math.Abs(Canvas.GetLeft(forward) - Canvas.GetLeft(back)) < .1 && Canvas.GetTop(forward) < Canvas.GetTop(back) && Canvas.GetLeft(x1) > Canvas.GetLeft(back) && Math.Abs(Canvas.GetTop(x1) - Canvas.GetTop(back)) < .1, "Forward and Back stay on the left while X1 occupies the lower right");
            Check(mouseButtons.SelectMany((button, index) => mouseButtons.Skip(index + 1).Select(other => !Bounds(button).IntersectsWith(Bounds(other)))).All(x => x), "mouse controls never overlap and preserve the keyboard gap system");
            double mouseHostBottom = window.MouseHost.TranslatePoint(new System.Windows.Point(0, window.MouseHost.ActualHeight), window.LowerInputGrid).Y + window.MouseHost.Margin.Bottom;
            Check(mouseHostBottom <= window.LowerInputGrid.ActualHeight + .1, $"the complete portrait mouse remains visible inside the lower workspace row ({mouseHostBottom:F1}/{window.LowerInputGrid.ActualHeight:F1})");
            Check(window.MousePanel.Height > window.MousePanel.Width * 1.9 && window.MouseBody.CornerRadius.TopLeft == 14 && window.MouseBody.Effect == null && window.MouseBody.Background is SolidColorBrush body && body.Color.A == 0, "mouse diagram uses a tall flat rounded-rectangle body without the old oval gradient or shadow");
            Check(mouseButtons.All(x => Descendants<Border>(x).All(border => border.Background is not LinearGradientBrush)), "mouse controls use the same flat solid faces as keyboard keys");
            CaptureForReview(window, "mouse-layout-dark.png");

            ThemeService.Apply(AppThemeMode.Light);
            window.UpdateLayout();
            Check(mouseButtons.All(x => x.Foreground is SolidColorBrush foreground && foreground.Color == ThemeService.Color("PrimaryText")) && window.MouseBody.BorderBrush is SolidColorBrush border && border.Color == ThemeService.Color("SubtleBorderBrush"), "light theme keeps every mouse label and the outer boundary visible");
            CaptureForReview(window, "mouse-layout-light.png");

            window.Width = 880;
            window.Height = 640;
            window.UpdateLayout();
            for (int i = 0; i < 3; i++)
            {
                window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
                window.UpdateLayout();
            }
            double mouseBottomAtMinimum = window.MouseHost.TranslatePoint(new System.Windows.Point(0, window.MouseHost.ActualHeight), window.MainContentGrid).Y;
            Check(mouseBottomAtMinimum <= window.MainContentGrid.ActualHeight + .1, $"mouse bottom remains visible at the minimum window size ({mouseBottomAtMinimum:F1}/{window.MainContentGrid.ActualHeight:F1})");
            CaptureForReview(window, "mouse-layout-minimum.png");

            ThemeService.Apply(AppThemeMode.Dark);
            picker = new MacroInputPickerWindow("JIS") { Owner = window, ShowInTaskbar = false };
            picker.Show();
            picker.UpdateLayout();
            var pickerA = picker.InputButtonsForTest.First(x => Equals(x.Tag, "A"));
            var pickerMouseButtons = picker.InputButtonsForTest.Where(x => x.Tag?.ToString() is "MouseLeft" or "MouseRight" or "MouseMiddle" or "MouseBack" or "MouseForward" or "MouseX" or "WheelUp" or "WheelDown" or "TiltLeft" or "TiltRight").ToList();
            var pickerOrdinary = pickerMouseButtons.Where(x => x.Tag?.ToString() is not "MouseLeft" and not "MouseRight").ToList();
            var pickerClicks = pickerMouseButtons.Where(x => x.Tag?.ToString() is "MouseLeft" or "MouseRight").ToList();
            Check(pickerMouseButtons.Count == 10 && pickerOrdinary.All(x => Math.Abs(x.Width - pickerA.Width) < .1 && Math.Abs(x.Height - pickerA.Height) < .1) && pickerClicks.All(x => Math.Abs(x.Width - pickerA.Width) < .1 && Math.Abs(x.Height - (pickerA.Height * 3 + 8)) < .1), "keypad-input mouse mirrors the same A-key and wheel-aligned click dimensions");
            CaptureForReview(picker, "mouse-layout-keypad-dark.png");
            ThemeService.Apply(AppThemeMode.Light);
            picker.UpdateLayout();
            Check(pickerMouseButtons.All(x => x.Foreground is SolidColorBrush foreground && foreground.Color == ThemeService.Color("PrimaryText")), "keypad-input mouse remains readable in the light theme");
            CaptureForReview(picker, "mouse-layout-keypad-light.png");
        }
        catch (Exception ex) { report.RecordException("Mouse UI exception", "FAIL Mouse UI exception: ", ex); }
        finally
        {
            ThemeService.Apply(previousTheme);
            picker?.Close();
            if (window != null) { window.PrepareForSystemShutdown(); window.Close(); }
            Environment.SetEnvironmentVariable("RELYR_CONFIG_DIR", previousConfigDirectory);
            try { if (Directory.Exists(testConfigDirectory)) Directory.Delete(testConfigDirectory, true); } catch { }
        }
        return report.Complete("MOUSE UI TEST PASSED", "MOUSE UI TEST FAILED: ");
    }

    internal static int Run(TextWriter output)
    {
        var report = new VerificationReport(output);
        Action<bool, string> Check = report.Check;
        static void Pump(MainWindow w) => w.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
        string? previousConfigDirectory = Environment.GetEnvironmentVariable("RELYR_CONFIG_DIR");
        string testConfigDirectory = VerificationPaths.CreateRunDirectory("ui-test");
        Environment.SetEnvironmentVariable("RELYR_CONFIG_DIR", testConfigDirectory);
        MainWindow? window = null;
        MainWindow? deferredStartupWindow = null;
        try
        {
            new ConfigService().Save(new AppConfig { FirstRunCompleted = true, CapsLockLayerWarningAccepted = true, CheckForUpdates = false, Gestures = [new GestureDefinition { Name = "ウィンドウ操作", UpKind = ActionKind.Shortcut, UpValue = "Win+Up", CenterKind = ActionKind.Key, CenterValue = "Enter" }] });
            var deferredStartupConfig = new AppConfig { FirstRunCompleted = true, CapsLockLayerWarningAccepted = true, CheckForUpdates = false };
            deferredStartupConfig.Profiles[0].Mappings.Add(new Mapping { Input = "Space+J", Layer = "Space", Kind = ActionKind.Key, Value = "Left" });
            var deferredStartupKeys = new ConcurrentQueue<(ushort Key, bool Up)>();
            deferredStartupWindow = new MainWindow(true, suppressTray: true, startupConfig: deferredStartupConfig, runtimeRole: RuntimeRole.UiHost, startInputHooks: false, deferEditorUiUntilShown: true);
            try
            {
                InputEngine.KeyOutputForTest = (key, up) => { deferredStartupKeys.Enqueue((key, up)); return true; };
                bool runtimeReadyBeforeEditor = deferredStartupWindow.IsInputEngineReadyForTest
                    && !deferredStartupWindow.IsEditorUiInitializedForTest
                    && deferredStartupWindow.KeyboardPanel.Children.Count == 0
                    && deferredStartupWindow.DeckManagementButtonsForTest.Count == 0
                    && deferredStartupWindow.RuntimeInterceptsInputForTest("Space+J");
                IntPtr[] deferredInputResults =
                [
                    deferredStartupWindow.DirectPhysicalKeyForTest(0x20, false),
                    deferredStartupWindow.DirectPhysicalKeyForTest(0x4A, false),
                    deferredStartupWindow.DirectPhysicalKeyForTest(0x4A, true),
                    deferredStartupWindow.DirectPhysicalKeyForTest(0x20, true)
                ];
                bool deferredActionCompleted = SpinWait.SpinUntil(() => deferredStartupKeys.Count >= 2, 1000);
                Check(runtimeReadyBeforeEditor && deferredInputResults.All(result => result == (IntPtr)1) && deferredActionCompleted
                    && deferredStartupKeys.Count(action => action == (0x25, false)) == 1
                    && deferredStartupKeys.Count(action => action == (0x25, true)) == 1
                    && !deferredStartupWindow.HasCapturedInputStateForTest,
                    "tray startup executes a real Space-layer mapping before constructing any keyboard or Deck editor controls");

                deferredStartupWindow.InitializeEditorUiForTest();
                Check(deferredStartupWindow.IsEditorUiInitializedForTest
                    && deferredStartupWindow.KeyboardPanel.Children.Count > 0
                    && deferredStartupWindow.DeckManagementButtonsForTest.Count > 0
                    && deferredStartupWindow.IsInputEngineReadyForTest
                    && deferredStartupWindow.RuntimeInterceptsInputForTest("Space+J"),
                    "first editor initialization builds its controls once without changing the ready runtime mapping");
            }
            finally
            {
                InputEngine.KeyOutputForTest = null;
            }
            // Exercise the main editor through the UI-host runtime role as well:
            // RELYR's own foreground window must never disable normal mappings.
            window = new MainWindow(true, suppressTray: true, runtimeRole: RuntimeRole.UiHost, startInputHooks: false) { Width = 800, Height = 620 };
            System.Windows.Application.Current.MainWindow = window;
            window.Show();
            window.UpdateLayout();
            deferredStartupWindow.PrepareForSystemShutdown();
            deferredStartupWindow.Close();
            deferredStartupWindow = null;
            Check(window.IsInputEngineReadyForTest, "input engine is ready when the main window and tray initialization complete");
            var mainRuntimeProfile = window.CurrentProfileForTest;
            string[] mainRuntimeInputs = ["F1", "Space+WheelDown", "Space+MouseForward", "CapsLock+U", "MouseRight+WheelDown", "MouseBack+WheelDown", "MouseForward+WheelDown"];
            mainRuntimeProfile.Mappings.RemoveAll(mapping => mainRuntimeInputs.Contains(mapping.Input, StringComparer.OrdinalIgnoreCase));
            mainRuntimeProfile.Mappings.Add(new Mapping { Input = "F1", Layer = "通常", Kind = ActionKind.None, LongPressKind = ActionKind.Shortcut, LongPressValue = OverlayService.DeckPanelAction, LongPressMs = 50 });
            mainRuntimeProfile.Mappings.Add(new Mapping { Input = "Space+WheelDown", Layer = "Space", Kind = ActionKind.Shortcut, Value = "Ctrl+PageDown" });
            mainRuntimeProfile.Mappings.Add(new Mapping { Input = "Space+MouseForward", Layer = "Space", Kind = ActionKind.Shortcut, Value = "LeftAlt+F4" });
            mainRuntimeProfile.Mappings.Add(new Mapping { Input = "CapsLock+U", Layer = "CapsLock", Kind = ActionKind.Key, Value = "Enter" });
            mainRuntimeProfile.Mappings.Add(new Mapping { Input = "MouseRight+WheelDown", Layer = "MouseRight", Kind = ActionKind.Key, Value = "Home" });
            mainRuntimeProfile.Mappings.Add(new Mapping { Input = "MouseBack+WheelDown", Layer = "MouseBack", Kind = ActionKind.Key, Value = "Left" });
            mainRuntimeProfile.Mappings.Add(new Mapping { Input = "MouseForward+WheelDown", Layer = "MouseForward", Kind = ActionKind.Key, Value = "Down" });
            window.ConfigForTest.WindowActionTarget = WindowActionTarget.ActiveWindow;
            window.SaveAndApplyForTest();
            window.SetCapsLockRemapForTest(true);
            var mainRuntimeDeckActions = new ConcurrentQueue<string>();
            var mainRuntimeKeyActions = new ConcurrentQueue<(ushort Key, bool Up)>();
            OverlayService.ActionRequestedForTest = mainRuntimeDeckActions.Enqueue;
            InputEngine.KeyOutputForTest = (key, up) => { mainRuntimeKeyActions.Enqueue((key, up)); return true; };
            var mainRuntimeResults = new List<IntPtr>
            {
                window.DirectPhysicalKeyForTest(0x70, false)
            };
            bool mainRuntimeDeckWorked = SpinWait.SpinUntil(() => mainRuntimeDeckActions.Count == 1, 500);
            mainRuntimeResults.Add(window.DirectPhysicalKeyForTest(0x70, true));
            mainRuntimeResults.Add(window.DirectPhysicalKeyForTest(0x20, false));
            mainRuntimeResults.Add(window.DirectPhysicalMouseForTest(0x20A, unchecked((int)0xFF880000), 0, 0));
            mainRuntimeResults.Add(window.DirectPhysicalKeyForTest(0x20, true));
            mainRuntimeResults.Add(window.DirectPhysicalKeyForTest(0x20, false));
            mainRuntimeResults.Add(window.DirectPhysicalMouseForTest(0x20B, 2 << 16, 0, 0));
            mainRuntimeResults.Add(window.DirectPhysicalMouseForTest(0x20C, 2 << 16, 0, 0));
            mainRuntimeResults.Add(window.DirectPhysicalKeyForTest(0x20, true));
            mainRuntimeResults.Add(window.DirectPhysicalKeyForTest(0x7C, false));
            mainRuntimeResults.Add(window.DirectPhysicalKeyForTest(0x55, false));
            mainRuntimeResults.Add(window.DirectPhysicalKeyForTest(0x55, true));
            mainRuntimeResults.Add(window.DirectPhysicalKeyForTest(0x7C, true));
            mainRuntimeResults.Add(window.DirectPhysicalMouseForTest(0x20B, 1 << 16, 0, 0));
            mainRuntimeResults.Add(window.DirectPhysicalMouseForTest(0x20A, unchecked((int)0xFF880000), 0, 0));
            mainRuntimeResults.Add(window.DirectPhysicalMouseForTest(0x20C, 1 << 16, 0, 0));
            mainRuntimeResults.Add(window.DirectPhysicalMouseForTest(0x20B, 2 << 16, 0, 0));
            mainRuntimeResults.Add(window.DirectPhysicalMouseForTest(0x20A, unchecked((int)0xFF880000), 0, 0));
            mainRuntimeResults.Add(window.DirectPhysicalMouseForTest(0x20C, 2 << 16, 0, 0));
            bool mainRuntimeOutputsWorked = SpinWait.SpinUntil(() => mainRuntimeKeyActions.Count >= 15, 1000);
            Check(window.ShouldInterceptPhysicalInputForTest && window.ShouldInterceptPhysicalMouseForTest
                && mainRuntimeResults.All(result => result == (IntPtr)1)
                && mainRuntimeDeckWorked && mainRuntimeOutputsWorked && !window.HasCapturedInputStateForTest
                && mainRuntimeKeyActions.Count(action => action == (0x0D, false)) == 1 && mainRuntimeKeyActions.Count(action => action == (0x0D, true)) == 1
                && mainRuntimeKeyActions.Count(action => action == (0x25, false)) == 1 && mainRuntimeKeyActions.Count(action => action == (0x25, true)) == 1
                && mainRuntimeKeyActions.Count(action => action == (0x28, false)) == 1 && mainRuntimeKeyActions.Count(action => action == (0x28, true)) == 1,
                "RELYR main screen runs normal F1, Space wheel/forward, CapsLock, Back, and Forward layer actions through the UI-host runtime without a foreground exception");
            const int rapidRuntimeIterations = 250;
            int rapidOutputsBefore = mainRuntimeKeyActions.Count;
            bool rapidRuntimeStayedClean = true;
            for (int i = 0; i < rapidRuntimeIterations; i++)
            {
                IntPtr[] rapidResults =
                [
                    window.DirectPhysicalKeyForTest(0x20, false),
                    window.DirectPhysicalMouseForTest(0x20A, unchecked((int)0xFF880000), 0, 0),
                    window.DirectPhysicalKeyForTest(0x20, true),
                    window.DirectPhysicalKeyForTest(0x20, false),
                    window.DirectPhysicalMouseForTest(0x20B, 2 << 16, 0, 0),
                    window.DirectPhysicalMouseForTest(0x20C, 2 << 16, 0, 0),
                    window.DirectPhysicalKeyForTest(0x20, true),
                    window.DirectPhysicalKeyForTest(0x7C, false),
                    window.DirectPhysicalKeyForTest(0x55, false),
                    window.DirectPhysicalKeyForTest(0x55, true),
                    window.DirectPhysicalKeyForTest(0x7C, true),
                    window.DirectPhysicalMouseForTest(0x204),
                    window.DirectPhysicalMouseForTest(0x20A, unchecked((int)0xFF880000), 0, 0),
                    window.DirectPhysicalMouseForTest(0x205),
                    window.DirectPhysicalMouseForTest(0x20B, 1 << 16, 0, 0),
                    window.DirectPhysicalMouseForTest(0x20A, unchecked((int)0xFF880000), 0, 0),
                    window.DirectPhysicalMouseForTest(0x20C, 1 << 16, 0, 0),
                    window.DirectPhysicalMouseForTest(0x20B, 2 << 16, 0, 0),
                    window.DirectPhysicalMouseForTest(0x20A, unchecked((int)0xFF880000), 0, 0),
                    window.DirectPhysicalMouseForTest(0x20C, 2 << 16, 0, 0)
                ];
                rapidRuntimeStayedClean &= rapidResults.All(result => result == (IntPtr)1)
                    && !window.HasCapturedInputStateForTest;
            }
            bool rapidRuntimeOutputsCompleted = SpinWait.SpinUntil(
                () => mainRuntimeKeyActions.Count >= rapidOutputsBefore + (rapidRuntimeIterations * 16), 5000);
            Check(rapidRuntimeStayedClean && rapidRuntimeOutputsCompleted && !window.HasCapturedInputStateForTest,
                "250 rapid Space, CapsLock, Right, Back, and Forward layer action cycles complete without input-state residue or a stopped runtime");
            OverlayService.ActionRequestedForTest = null;
            InputEngine.KeyOutputForTest = null;
            window.SetCapsLockRemapForTest(false);
            int hiddenProfileOverlays = 0;
            for (int i = 0; i < 3; i++)
            {
                var transientOverlay = new ProfileSwitchOverlay("標準", TimeSpan.FromMilliseconds(150));
                transientOverlay.Show();
                PumpFor(TimeSpan.FromMilliseconds(750));
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
            window.ApplyProfileManagerResultForTest([managedStandard, managedAutomatic], managedStandard.Name);
            var persistedProfiles = new ConfigService().Load();
            Check(!persistedProfiles.AutoSave && !persistedProfiles.AutoSwitchProfilesByCursor && persistedProfiles.Profiles.Any(x => x.Name == managedAutomatic.Name && x.AutoSwitchEnabled && x.AutoSwitchApplications.Contains("relyr-profile-test.exe")) && window.AppliedProfileNameForTest == managedStandard.Name, "Profile Manager Apply immediately saves foreground-based profile routing even when assignment auto-save is off");
            bool automaticProfileApplied = window.ApplyAutomaticProfileForTest(["relyr-profile-test"]);
            Check(automaticProfileApplied && window.AppliedProfileNameForTest == managedAutomatic.Name
                && window.CurrentProfileForTest.Name == managedStandard.Name && Equals(window.ProfileBox.SelectedItem, managedStandard.Name)
                && window.ProfileOverlayForTest is { IsVisible: true } appliedOverlay && appliedOverlay.ProfileNameText.Text == managedAutomatic.Name,
                "automatic routing switches runtime behavior while the editor profile dropdown remains on the user's selection");
            bool automaticProfileReturned = window.ApplyAutomaticProfileForTest([]);
            window.ResetAutomaticProfileCandidateForTest();
            bool transientDefaultWasAccepted = window.ObserveAutomaticProfileCandidateForTest("標準", managedAutomatic.Name);
            bool currentChromeWasAccepted = window.ObserveAutomaticProfileCandidateForTest(managedAutomatic.Name, managedAutomatic.Name);
            bool secondTransientDefaultWasAccepted = window.ObserveAutomaticProfileCandidateForTest("標準", managedAutomatic.Name);
            bool stableDefaultWasAccepted = window.ObserveAutomaticProfileCandidateForTest("標準", managedAutomatic.Name);
            Check(!transientDefaultWasAccepted && currentChromeWasAccepted && !secondTransientDefaultWasAccepted && stableDefaultWasAccepted
                && MainWindow.AutomaticProfileRequiredSamples(window.ProfilesForTest, managedAutomatic.Name) == 1
                && MainWindow.AutomaticProfileRequiredSamples(window.ProfilesForTest, managedStandard.Name) == 2,
                "foreground profile switching enters a matched app and its Deck immediately while requiring confirmation only before returning to the standard profile");
            Check(automaticProfileReturned && window.AppliedProfileNameForTest == managedStandard.Name
                && window.CurrentProfileForTest.Name == managedStandard.Name && Equals(window.ProfileBox.SelectedItem, managedStandard.Name)
                && window.ProfileOverlayForTest is { IsVisible: true } returnOverlay && returnOverlay.ProfileNameText.Text == managedStandard.Name,
                "foreground routing returns the runtime profile without moving the editor dropdown");
            PumpFor(TimeSpan.FromMilliseconds(1250));
            window.ApplyProfileManagerResultForTest([new Profile { Name = "標準" }], "標準");
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
            var compactProfileManager = new ProfileManagerWindow([new Profile { Name = "標準" }, new Profile { Name = "編集用", AutoSwitchEnabled = true, AutoSwitchApplications = ["notepad.exe"] }], "編集用") { Owner = window, ShowInTaskbar = false, Width = 820, Height = 560 };
            compactProfileManager.Show();
            compactProfileManager.UpdateLayout();
            Check(compactProfileManager.ProfileListColumn.ActualWidth <= 170 && compactProfileManager.ApplicationManagementColumn.ActualWidth > compactProfileManager.ProfileListColumn.ActualWidth * 3 && compactProfileManager.RunningApplicationList.ActualWidth > 250 && compactProfileManager.RunningApplicationList.ActualHeight > 260, $"profile management keeps a compact profile pane and gives the application editor most of the available width and height (list={compactProfileManager.RunningApplicationList.ActualWidth:F0}x{compactProfileManager.RunningApplicationList.ActualHeight:F0}, profile={compactProfileManager.ProfileListColumn.ActualWidth:F0}, editor={compactProfileManager.ApplicationManagementColumn.ActualWidth:F0})");
            Check(ScrollViewer.GetHorizontalScrollBarVisibility(compactProfileManager.RunningApplicationList) == ScrollBarVisibility.Disabled && ScrollViewer.GetVerticalScrollBarVisibility(compactProfileManager.RunningApplicationList) == ScrollBarVisibility.Hidden, "running application list remains scrollable by wheel without displaying scrollbars");
            Check(Descendants<Border>(compactProfileManager.AssignedApplicationList).Any(border => border.CornerRadius.TopLeft == 8) && Descendants<Border>(compactProfileManager.RunningApplicationList).Any(border => border.CornerRadius.TopLeft == 8), "profile application lists use the shared eight-pixel control radius instead of square system borders");
            Check(Descendants<TextBlock>(compactProfileManager).Any(text => text.Text.Contains("アクティブな対象アプリ", StringComparison.Ordinal)) && compactProfileManager.AutoSwitchBox.ToolTip?.ToString()?.Contains("アクティブ", StringComparison.Ordinal) == true, "profile manager clearly explains foreground-based automatic switching");
            compactProfileManager.AutoSwitchBox.ApplyTemplate();
            var assignedApplicationItems = compactProfileManager.AssignedApplicationList.Items.Cast<ApplicationDisplayItem>().ToArray();
            var runningApplicationItems = compactProfileManager.RunningApplicationList.Items.Cast<ApplicationDisplayItem>().ToArray();
            Check(compactProfileManager.AutoSwitchBox.Template.FindName("SwitchTrack", compactProfileManager.AutoSwitchBox) is Border
                && assignedApplicationItems.Length == 1 && assignedApplicationItems.All(item => item.Icon != null)
                && runningApplicationItems.All(item => item.Icon != null)
                && Descendants<System.Windows.Controls.Image>(compactProfileManager.AssignedApplicationList).Any(image => image.Source != null),
                "profile automatic switching uses the shared theme switch and every assigned/running application row exposes an application icon");
            var profileCommandButtons = new[] { compactProfileManager.AddProfileButton, compactProfileManager.RenameProfileButton, compactProfileManager.DeleteProfileButton };
            Check(profileCommandButtons.All(button => button.Content is Viewbox && Descendants<System.Windows.Shapes.Path>(button).Any(path => Equals(path.Stroke, button.Foreground))) && profileCommandButtons.All(button => !string.IsNullOrWhiteSpace(button.ToolTip?.ToString())), "profile add, rename, and delete commands use theme-aware vector icons with explanatory tooltips instead of cramped text");
            CaptureForReview(compactProfileManager, "profile-manager-compact.png");
            SystemCommands.CloseWindow(compactProfileManager);
            Pump(window);
            Check(!compactProfileManager.IsVisible, "the profile manager always accepts the standard title-bar close command");
            Check(window.VersionText.Text == "v" + MainWindow.DisplayVersion && window.Title.Contains(window.VersionText.Text), "running version is always visible");
            Check(!MainWindow.NativeTrayRegistrationAllowed(false)
                && StableNotifyIcon.Identifier == new Guid("b0c52fd8-c5b7-48c0-83b2-9bfdcab49a68"),
                "development and integration-test processes cannot register a native tray icon and the product identity remains permanent");
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
            Check(window.InputDisplayText.Text == "キーを選択してください" && window.KindBox.Items.Count == 9 && window.LongKindBox.Items.Count == 9 && window.KindBox.Items.Cast<object>().Select(x => x.GetType().GetProperty("Label")?.GetValue(x)?.ToString()).SequenceEqual(["別のキー", "プロファイル", "ショートカット", "文字列", "アプリ・パス", "マクロ", "ジェスチャー", "Deckパネル", "キーパッドから入力"]) && window.LongKindBox.Items.Cast<object>().Single(x => Equals(x.GetType().GetProperty("Kind")?.GetValue(x), ActionKind.Gesture)).GetType().GetProperty("IsEnabled")?.GetValue(window.LongKindBox.Items.Cast<object>().Single(x => Equals(x.GetType().GetProperty("Kind")?.GetValue(x), ActionKind.Gesture))) is false && window.DestinationClearButton.Visibility == Visibility.Collapsed && window.DestinationConfirmButton.Visibility == Visibility.Collapsed && window.LongDestinationClearButton.Visibility == Visibility.Collapsed && window.LongDestinationConfirmButton.Visibility == Visibility.Collapsed, "short and long editors expose Deck panels before keypad input and hide edit actions until direct editing starts");
            var deckPickerMenu = window.CreateDeckPanelPickerMenu(window.KindBox, false);
            var longDeckPickerMenu = window.CreateDeckPanelPickerMenu(window.LongKindBox, true);
            var savedDeckNames = window.ConfigForTest.DeckLayouts.Where(x => !string.IsNullOrWhiteSpace(x.Name)).Select(x => x.Name).ToArray();
            Check(deckPickerMenu.Items.OfType<MenuItem>().Select(x => x.Header?.ToString()).SequenceEqual(savedDeckNames)
                && longDeckPickerMenu.Items.OfType<MenuItem>().Select(x => x.Header?.ToString()).SequenceEqual(savedDeckNames)
                && deckPickerMenu.Items.OfType<MenuItem>().All(x => x.ToolTip?.ToString()?.Contains('×') == true)
                && window.KindBox.Items.Cast<object>().Single(x => x.GetType().GetProperty("Label")?.GetValue(x)?.ToString() == "Deckパネル").GetType().GetProperty("IsDeckPanel")?.GetValue(window.KindBox.Items.Cast<object>().Single(x => x.GetType().GetProperty("Label")?.GetValue(x)?.ToString() == "Deckパネル")) is true,
                "the dedicated Deck panel choice opens a compact list containing only saved Deck layouts for short and long actions");
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
            gestureManager.GestureTitle.ApplyTemplate();
            var gestureTitleEditButton = (System.Windows.Controls.Button?)gestureManager.GestureTitle.Template.FindName("GestureTitleEditButton", gestureManager.GestureTitle);
            gestureTitleEditButton?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(gestureTitleEditButton != null && !gestureManager.GestureTitle.IsReadOnly && gestureManager.GestureTitle.IsKeyboardFocusWithin
                && Descendants<System.Windows.Shapes.Path>(gestureManager).Count(path => Equals(path.Stroke, ThemeService.Brush("AccentTextBrush"))) >= 4,
                "gesture direction rows use visible directional line icons and the title pencil starts inline editing");
            gestureManager.GestureList.Focus();
            Pump(window);
            Check(gestureManager.GestureTitle.IsReadOnly, "gesture title returns to its quiet display state after inline editing is committed");
            var gestureCommandButtons = new[] { gestureManager.AddGestureButton, gestureManager.RenameGestureButton, gestureManager.DeleteGestureButton };
            Check(gestureCommandButtons.All(button => button.Content is Viewbox && Descendants<System.Windows.Shapes.Path>(button).Any(path => Equals(path.Stroke, button.Foreground))) && !gestureLabels.Any(text => text.Contains("ジェスチャーの入れ子は安全のため", StringComparison.Ordinal)), "gesture add, rename, and delete commands use theme-aware vector icons and the unwanted nesting notice is removed");
            CaptureForReview(gestureManager, "gesture-manager.png");
            gestureManager.Close();
            var emptyTitleCenter = window.InspectorEmptyTitleText.TranslatePoint(new System.Windows.Point(window.InspectorEmptyTitleText.ActualWidth / 2, window.InspectorEmptyTitleText.ActualHeight / 2), window.AssignmentPane);
            double emptyButtonTop = window.DetectInputButton.TranslatePoint(new System.Windows.Point(), window.AssignmentPane).Y;
            Check(window.AssignmentPane.BorderThickness == new Thickness(0) && window.AssignmentPane.CornerRadius == new CornerRadius(0) && window.AssignmentPane.Effect == null && ReferenceEquals(window.AssignmentPane.Background, ThemeService.Brush("AppBackground")) && window.InspectorEmptyState.VerticalAlignment == System.Windows.VerticalAlignment.Stretch && window.InspectorEmptyState.RenderTransform is TranslateTransform emptyStateShift && Math.Abs(emptyStateShift.Y + 96) <= .1 && Math.Abs(emptyTitleCenter.Y - (window.AssignmentPane.ActualHeight / 2 - 96)) <= 1.1 && emptyButtonTop >= 0, "the inspector lifts the complete empty-state composition another two-centimeter-equivalent step without clipping its input button");
            Check(!Descendants<TextBlock>(window.AssignmentPane).Any(text => text.Text == "インスペクター")
                && window.DetectInputButton.Content is TextBlock { Text: "⌘" }
                && Descendants<TextBlock>(window.AssignmentPane).Count(text => text.Text == "⌘") == 1
                && Math.Abs(window.DetectInputButton.ActualWidth - 64) < .1
                && Math.Abs(window.DetectInputButton.ActualHeight - 64) < .1
                && window.DetectInputButton.HorizontalAlignment == System.Windows.HorizontalAlignment.Center
                && ReferenceEquals(window.DetectInputButton.Parent, window.InspectorEmptyState)
                && Math.Abs(window.DetectInputButton.Margin.Top) < .1
                && Math.Abs(window.DetectInputButton.Margin.Bottom - 24) < .1
                && Math.Abs(window.DetectInputButton.TranslatePoint(new System.Windows.Point(window.DetectInputButton.ActualWidth / 2, 0), window.AssignmentPane).X - window.AssignmentPane.ActualWidth / 2) < 1
                && window.DetectInputButton.TranslatePoint(new System.Windows.Point(0, window.DetectInputButton.ActualHeight), window.AssignmentPane).Y < emptyTitleCenter.Y
                && window.DetectInputButton.ToolTip?.ToString() == "入力を検出",
                "the inspector heading is removed while the circular input-detection button stays centered above the centered empty-state title");
            string[] mainInspectorHintIcons = [window.InspectorHintOneIcon.Data.ToString(), window.InspectorHintTwoIcon.Data.ToString(), window.InspectorHintThreeIcon.Data.ToString()];
            Check(window.InspectorHintsPanel.HorizontalAlignment == System.Windows.HorizontalAlignment.Center
                && Math.Abs(window.InspectorHintsPanel.Margin.Top - 48) < .1
                && window.InspectorHintOneTitle.Text == "キーをクリック"
                && window.InspectorHintTwoTitle.Text == "入力を検出"
                && window.InspectorHintThreeTitle.Text == "右クリック"
                && new[] { window.InspectorHintOneIcon.Data, window.InspectorHintTwoIcon.Data, window.InspectorHintThreeIcon.Data }.All(geometry => geometry != null)
                && mainInspectorHintIcons.Distinct().Count() == 3
                && new[] { window.InspectorHintOneTitle, window.InspectorHintOneDescription, window.InspectorHintTwoTitle, window.InspectorHintTwoDescription, window.InspectorHintThreeTitle, window.InspectorHintThreeDescription }.All(text => text.TextAlignment == TextAlignment.Left && text.HorizontalAlignment == System.Windows.HorizontalAlignment.Left),
                "the key and mouse layer inspector uses distinct pointer, keyboard, and right-mouse vector icons while left-aligning every label inside its centered content block");
            double hintsCenter = window.InspectorHintsPanel.TranslatePoint(new System.Windows.Point(window.InspectorHintsPanel.ActualWidth / 2, 0), window.AssignmentPane).X;
            double[] hintRowLefts = new Grid[] { window.InspectorHintOneRow, window.InspectorHintTwoRow, window.InspectorHintThreeRow }.Select(row => row.TranslatePoint(new System.Windows.Point(), window.AssignmentPane).X).ToArray();
            Check(Math.Abs(hintsCenter - window.AssignmentPane.ActualWidth / 2) <= 1.1
                && hintRowLefts.Max() - hintRowLefts.Min() <= 1.1
                && new[] { window.InspectorHintOneRow, window.InspectorHintTwoRow, window.InspectorHintThreeRow }.All(row => Math.Abs(row.ActualWidth - window.InspectorHintsPanel.ActualWidth) <= 1.1),
                "each hint row shares one centered horizontal layout while its icon and text remain consistently left aligned");
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
            var closeSettings = new SettingsWindow(new AppConfig { GestureThresholdPixels = 14, LockCursorDuringGesture = false, ClockBackgroundMode = ClockBackgroundMode.Solid, ClockDisplayMode = ClockDisplayMode.FullDateAndTime, ClockBackgroundImage = @"C:\Images\clock.png", ClockSolidColor = "#123456", ClockShowOnAllMonitors = false, InputPanelOpacityPercent = 67, DeckAfterActionBehavior = DeckAutoDismissBehavior.Hide, DeckPointerLeaveBehavior = DeckAutoDismissBehavior.CollapseToEdge });
            closeSettings.Show();
            closeSettings.UpdateLayout();
            Check(closeSettings.ActiveWindowTargetBox.Content?.ToString() == "アクティブなウィンドウ" && closeSettings.CursorWindowTargetBox.Content?.ToString() == "マウスカーソル下のウィンドウ" && closeSettings.ActiveWindowTargetBox.IsChecked == true && closeSettings.CursorWindowTargetBox.IsChecked == false, "settings provides one clear target choice for close, maximize, snap, and other window actions");
            closeSettings.CursorWindowTargetBox.IsChecked = true;
            Check(closeSettings.SelectedWindowActionTarget == WindowActionTarget.WindowUnderCursor, "window-under-cursor target can be selected without changing the action itself");
            Check(closeSettings.GestureThreshold == 14 && closeSettings.GestureThresholdBox.Text == "14" && closeSettings.LockGestureCursorBox.IsChecked == false && !closeSettings.LockCursorDuringGesture, "gesture sensitivity and cursor-lock behavior are both visible and editable in the layer settings");
            closeSettings.LockGestureCursorBox.IsChecked = true;
            Check(closeSettings.LockCursorDuringGesture, "gesture cursor locking can be enabled without changing the sensitivity");
            var settingsCategories = closeSettings.CategoryList.Items.Cast<ListBoxItem>().ToArray();
            int updateCategoryIndex = Array.FindIndex(settingsCategories, item => item.Tag?.ToString() == "Update");
            Check(updateCategoryIndex >= 0 && settingsCategories[updateCategoryIndex + 1].Tag?.ToString() == "Disabled" && settingsCategories.Last().Tag?.ToString() == "Support" && settingsCategories.Any(x => x.Tag?.ToString() == "Overlay") && Descendants<System.Windows.Controls.CheckBox>(closeSettings.AppearancePanel).Contains(closeSettings.ProfileOverlayBox) && Descendants<Separator>(closeSettings.AppearancePanel).Any() && !Descendants<TextBlock>(closeSettings).Any(x => x.Text.Contains("仮想デスクトップ番号のすぐ上", StringComparison.Ordinal)), "appearance uses a divider between color mode and profile switching while keeping overlay, disabled-app, and support options discoverable");
            closeSettings.SelectCategory("Appearance");
            closeSettings.UpdateLayout();
            CaptureForReview(closeSettings, "appearance-settings.png");
            closeSettings.SelectCategory("Layers");
            closeSettings.UpdateLayout();
            CaptureForReview(closeSettings, "layer-settings.png");
            Check(closeSettings.SelectedClockBackgroundMode == ClockBackgroundMode.Solid && closeSettings.SelectedClockDisplayMode == ClockDisplayMode.FullDateAndTime && closeSettings.ClockBackgroundImage == @"C:\Images\clock.png" && closeSettings.ClockSolidColor == "#123456" && !closeSettings.ClockShowOnAllMonitors && closeSettings.InputPanelOpacityPercent == 67, "overlay settings restore keypad opacity, solid color, clock image, date format, and monitor scope");
            Check(closeSettings.DeckAfterActionBehavior == DeckAutoDismissBehavior.Hide && closeSettings.DeckPointerLeaveBehavior == DeckAutoDismissBehavior.CollapseToEdge, "saving general settings preserves Deck display behaviors now owned by the Deck workspace");
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
            Check(Math.Abs(numpadOverlay.PanelOpacity - .63) < .001 && Math.Abs(extendedOverlay.PanelOpacity - .96) < .001 && !numpadOverlay.AllowsTransparency && !extendedOverlay.AllowsTransparency, "numpad and extended keypad use bounded opaque native windows and apply the configured panel opacity");
            numpadOverlay.CapturePanelMouseForTest();
            if (numpadOverlay.OwnsMouseCaptureForTest)
                Check(true, "the keypad drag surface can own capture while it is being moved");
            else
                output.WriteLine("SKIP keypad drag capture acquisition: the current test session denied global mouse capture");
            Check(Math.Abs(numpadOverlay.CloseButton.ActualWidth - numpadOverlay.CloseButton.ActualHeight) < .1 && numpadOverlay.CloseButton.Content is System.Windows.Shapes.Path, "overlay close control is a centered vector X inside an exact square button");
            CaptureForReview(numpadOverlay, "overlay-numpad.png");
            CaptureForReview(extendedOverlay, "overlay-extended.png");
            numpadOverlay.CloseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(!numpadOverlay.IsVisible && !numpadOverlay.OwnsMouseCaptureForTest, "overlay close releases its drag capture and closes the panel on the first click");
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
            OverlayService.Configure(() => new AppConfig { ClockBackgroundMode = ClockBackgroundMode.Solid, ClockSolidColor = "#123456", ClockShowOnAllMonitors = false }, () => false);
            OverlayService.TryShow(OverlayService.ClockAction);
            Pump(window);
            int externallyClosedOverlayCount = OverlayService.ScreenOverlayCountForTest;
            OverlayService.CloseScreenOverlaysExternallyForTest();
            Pump(window);
            Check(externallyClosedOverlayCount > 0 && !OverlayService.FullScreenVisible && OverlayService.ScreenOverlayCountForTest == 0, "external fullscreen window closure clears the global input-consumption transaction");
            var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens[0];
            var cursorOverlay = new ScreenOverlayWindow(primaryScreen, true, new AppConfig { ClockBackgroundMode = ClockBackgroundMode.Solid, ClockSolidColor = "#123456" });
            var clockTime = Descendants<TextBlock>(cursorOverlay).OrderByDescending(x => x.FontSize).First();
            Check(cursorOverlay.Cursor == System.Windows.Input.Cursors.None && cursorOverlay.ForceCursor && clockTime.FontFamily.Source == "Segoe UI Variable Display" && clockTime.FontStretch == FontStretches.Condensed, "clock hides the pointer and uses the narrow Segoe UI Variable display face");
            cursorOverlay.Close();
            Check(cursorOverlay.CursorRestoredForTest && cursorOverlay.Cursor == System.Windows.Input.Cursors.Arrow, "closing a fullscreen overlay explicitly restores a visible system cursor");
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
            manualTutorial.DoNotShowAgainBox.ApplyTemplate();
            window.DeckProfileSwitchBox.ApplyTemplate();
            Check(manualTutorial.DoNotShowAgainBox.Template.FindName("SwitchTrack", manualTutorial.DoNotShowAgainBox) is Border
                && window.DeckProfileSwitchBox.Template.FindName("SwitchTrack", window.DeckProfileSwitchBox) is Border,
                "first-run and Deck options use the same theme-aware switch instead of a system checkbox");
            manualTutorial.Close();
            var toolbarControls = new System.Windows.Controls.Control[] { window.ProfileBox, window.NewProfileButton, window.KeyboardLayoutBox, window.MultiSelectToggle, window.MultiCopyButton, window.MultiPasteButton, window.MultiDeleteButton, window.ToolbarSaveButton, window.LightThemeToggle, window.DarkThemeToggle };
            double toolbarControlTop = window.ProfileBox.TranslatePoint(new System.Windows.Point(), window).Y;
            double sidebarLogoTop = window.ProductNameText.TranslatePoint(new System.Windows.Point(), window).Y;
            Check(toolbarControls.All(x => Math.Abs(x.ActualHeight - 44) < .1)
                && window.ToolbarSaveButton.ActualWidth >= 77
                && Math.Abs(window.ToolbarPanel.Margin.Top - 9.5) < .1
                && Math.Abs(window.ToolbarPanel.Margin.Bottom + 9.5) < .1
                && Math.Abs(toolbarControlTop - sidebarLogoTop) <= 1.1,
                $"toolbar uses the larger reference dimensions and aligns its top plane to the untouched sidebar logo ({toolbarControlTop:F1}/{sidebarLogoTop:F1})");
            double layoutRight = window.KeyboardLayoutBox.TranslatePoint(new System.Windows.Point(window.KeyboardLayoutBox.ActualWidth, 0), window.ToolbarPanel).X;
            double selectionLeft = window.MultiSelectActionsPanel.TranslatePoint(new System.Windows.Point(), window.ToolbarPanel).X;
            double selectionRight = window.MultiSelectActionsPanel.TranslatePoint(new System.Windows.Point(window.MultiSelectActionsPanel.ActualWidth, 0), window.ToolbarPanel).X;
            double saveLeft = window.ToolbarSaveButton.TranslatePoint(new System.Windows.Point(), window.ToolbarPanel).X;
            Check(ReferenceEquals(window.MultiSelectActionsPanel.Parent, window.ToolbarContextPanel)
                && selectionLeft >= layoutRight - 1.1 && selectionLeft - layoutRight <= 20.1 && selectionRight <= saveLeft + 1.1,
                $"selection, copy, paste, and delete sit immediately after the keyboard layout without overlapping save ({layoutRight:F1} <= {selectionLeft:F1}..{selectionRight:F1} <= {saveLeft:F1})");
            var assignmentDragSourceButton = window.VisualInputButtonsForTest.First(button => Equals(button.Tag, "H"));
            var assignmentDragTargetButton = window.VisualInputButtonsForTest.First(button => Equals(button.Tag, "J"));
            Check(window.VisualInputButtonsForTest.All(button => button.AllowDrop), "every visible keyboard and mouse button accepts the shared assignment drag route");
            window.SetInputHoverForTest(assignmentDragSourceButton, true);
            PumpFor(TimeSpan.FromMilliseconds(150));
            Check(assignmentDragSourceButton.RenderTransform is ScaleTransform hoverScale && hoverScale.HasAnimatedProperties && hoverScale.ScaleX >= 1.049 && hoverScale.ScaleY >= 1.049,
                "main keyboard hover reaches a visible five-percent transform-only pop without changing layout measurements");
            window.SetInputHoverForTest(assignmentDragSourceButton, false);
            window.SetAssignmentDropTargetForTest(assignmentDragTargetButton, true);
            assignmentDragTargetButton.ApplyTemplate();
            var mainDropTint = (UIElement)assignmentDragTargetButton.Template.FindName("DropTargetTint", assignmentDragTargetButton)!;
            var mainDropBadge = (UIElement)assignmentDragTargetButton.Template.FindName("DropTargetBadge", assignmentDragTargetButton)!;
            Check(MainWindow.GetIsAssignmentDropTarget(assignmentDragTargetButton)
                && assignmentDragTargetButton.BorderThickness.Left >= 3
                && mainDropTint.Opacity == 0 && mainDropBadge.Opacity == 1,
                "a main-key drag target keeps its original face while restoring a clear center marker and high-contrast outline");
            CaptureForReview(window, "main-drag-target.png");
            window.SetAssignmentDropTargetForTest(assignmentDragTargetButton, false);
            window.CurrentProfileForTest.Mappings.RemoveAll(mapping => mapping.Input is "H" or "J");
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "H", Layer = "通常", Kind = ActionKind.Text, Value = "drag-source", Application = "editor.exe" });
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "H", Layer = "通常", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+H", Application = "browser.exe" });
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "J", Layer = "通常", Kind = ActionKind.Shortcut, Value = "Ctrl+J" });
            Check(window.TransferCurrentLayerAssignmentsForTest("H", "J") == AssignmentTransferResult.Swapped
                && window.CurrentProfileForTest.Mappings.Count(mapping => mapping.Input == "J") == 2
                && window.CurrentProfileForTest.Mappings.Count(mapping => mapping.Input == "H") == 1
                && window.CurrentProfileForTest.Mappings.Single(mapping => mapping.Input == "H").Value == "Ctrl+J",
                "the production main-layer drag transfer swaps occupied keys and retains all application-specific source actions");
            window.CurrentProfileForTest.Mappings.RemoveAll(mapping => mapping.Input is "H" or "J");
            window.ColorButtonsForTest();
            var compactDragPreview = new DeckDragPreviewWindow(new Border(), compact: true);
            Check(Math.Abs(compactDragPreview.PreviewWidthForTest - 20) < .1 && Math.Abs(compactDragPreview.PreviewHeightForTest - 20) < .1,
                "editor action drags use a compact 20-pixel preview that does not cover the destination key");
            compactDragPreview.Close();
            window.LightThemeToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(window);
            bool lightToolbarApplied = ThemeService.CurrentMode == AppThemeMode.Light && window.ConfigForTest.ThemeMode == AppThemeMode.Light && window.LightThemeToggle.IsChecked == true && window.DarkThemeToggle.IsChecked == false;
            window.DarkThemeToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(window);
            Check(lightToolbarApplied && ThemeService.CurrentMode == AppThemeMode.Dark && window.ConfigForTest.ThemeMode == AppThemeMode.Dark && window.LightThemeToggle.IsChecked == false && window.DarkThemeToggle.IsChecked == true, "the visible light and dark toolbar buttons apply and persist the selected theme immediately");
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
            var liveDeckConfig = window.DeckOverlayConfigForTest;
            Check(ReferenceEquals(liveDeckConfig.DeckLayouts, window.ConfigForTest.DeckLayouts)
                && ReferenceEquals(liveDeckConfig.DeckLayouts.First(), standardDeck),
                "the Deck editor and live overlay share one layout model while runtime profile selection remains a snapshot");
            window.EditDeckLayoutForTest(standardDeck);
            Pump(window);
            var deckButtons = window.DeckManagementButtonsForTest.ToArray();
            foreach (var deckButton in deckButtons)
                deckButton.ApplyTemplate();
            Check(deckButtons.All(button => !Descendants<Border>(button).Any(border => Math.Abs(border.Height - 1) < .1)), "Deck buttons omit the decorative top highlight line");
            var deckCells = deckButtons.Select(button => (StackPanel)button.Parent).ToArray();
            Check(window.DeckEditorWorkspace.Visibility == Visibility.Visible && window.DeckLayoutListWorkspace.Visibility == Visibility.Collapsed && deckButtons.Length == standardDeck.Columns * standardDeck.Rows
                && deckButtons.All(button => Math.Abs(button.Width - DeckPanelLayout.KeyWidth) < .1 && Math.Abs(button.Height - DeckPanelLayout.KeyHeight) < .1)
                && deckCells.All(cell => Math.Abs(cell.Width - DeckPanelLayout.KeyWidth - DeckPanelLayout.ButtonGap) < .1 && Math.Abs(cell.Height - DeckPanelLayout.KeyHeight - DeckPanelLayout.ButtonGap) < .1)
                && deckCells.All(cell => Descendants<TextBlock>(cell).Any(label => Math.Abs(label.Height - DeckPanelLayout.NameLabelHeight) < .1)),
                "Deck editor keeps the 54x52 button and visible name below it while matching horizontal and vertical button gaps");
            var unassignedDeckButton = deckButtons.First(button => standardDeck.Mappings.All(mapping => !mapping.Input.Equals((string)button.Tag, StringComparison.OrdinalIgnoreCase)));
            string unassignedDeckInput = (string)unassignedDeckButton.Tag;
            unassignedDeckButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            bool unassignedDeckSelectedWithoutImplicitEdit = window.InputName.Text == unassignedDeckInput
                && !window.ValueBox.IsKeyboardFocusWithin
                && !window.IsEditingSelectedInputForTest;
            window.ClickDeckPreviewBackgroundForTest();
            Pump(window);
            Check(unassignedDeckSelectedWithoutImplicitEdit && window.InputName.Text.Length == 0 && window.InspectorEmptyState.Visibility == Visibility.Visible,
                "an unassigned Deck slot does not force the value editor and one preview-background click clears its selection");
            window.SetInputHoverForTest(deckButtons[0], true);
            PumpFor(TimeSpan.FromMilliseconds(150));
            Check(deckButtons[0].RenderTransform is ScaleTransform deckHoverScale && deckHoverScale.HasAnimatedProperties && deckHoverScale.ScaleX >= 1.049 && deckHoverScale.ScaleY >= 1.049,
                "Deck buttons use the same visible five-percent transform-only hover enlargement as the main keyboard");
            window.SetInputHoverForTest(deckButtons[0], false);
            window.SetAssignmentDropTargetForTest(deckButtons[1], true);
            deckButtons[1].ApplyTemplate();
            var deckDropTint = (UIElement)deckButtons[1].Template.FindName("DropTargetTint", deckButtons[1])!;
            var deckDropBadge = (UIElement)deckButtons[1].Template.FindName("DropTargetBadge", deckButtons[1])!;
            Check(MainWindow.GetIsAssignmentDropTarget(deckButtons[1]) && deckButtons[1].BorderThickness.Left >= 3
                && deckDropTint.Opacity == 0 && deckDropBadge.Opacity == 1,
                "Deck editor drag targets keep their original face while restoring the same clear drop marker");
            CaptureForReview(window, "deck-drag-target.png");
            window.SetAssignmentDropTargetForTest(deckButtons[1], false);
            Check(window.DeckKeypadInputButton.Visibility == Visibility.Visible && window.DetectInputButton.Visibility == Visibility.Collapsed && window.LongPressExpander.Visibility == Visibility.Collapsed && window.KindBox.Items.Cast<object>().All(x => !x.ToString()!.Contains("Gesture", StringComparison.Ordinal)) && window.KindBox.Items.Cast<object>().Select(x => x.GetType().GetProperty("Label")?.GetValue(x)?.ToString()).Contains("Deckパネル") && window.KindBox.Items.Cast<object>().Select(x => x.GetType().GetProperty("Label")?.GetValue(x)?.ToString()).Contains("キーパッドから入力") && !window.KindBox.Items.Cast<object>().Select(x => x.GetType().GetProperty("Label")?.GetValue(x)?.ToString()).Contains("無効化"), "Deck editing keeps the inspector, exposes saved Deck panels before keypad input, and excludes gestures and long press");
            bool deckDoubleClickOpenedActionPicker = false;
            window.ActionPickerRequestedForTest = (longPress, category) =>
            {
                deckDoubleClickOpenedActionPicker = !longPress;
                return null;
            };
            deckButtons[0].RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Controls.Control.MouseDoubleClickEvent });
            Pump(window);
            window.ActionPickerRequestedForTest = null;
            deckButtons[0].ApplyTemplate();
            var deckSingleBadge = (UIElement)deckButtons[0].Template.FindName("MultiSelectBadge", deckButtons[0])!;
            Check(deckDoubleClickOpenedActionPicker && window.InputName.Text == "Deck+01" && !window.ValueBox.IsKeyboardFocusWithin
                && deckSingleBadge.Opacity == 0 && deckButtons[0].Opacity == 1 && Math.Abs(deckButtons[1].Opacity - MainWindow.SelectionDimOpacity) < .01
                && deckButtons[0].BorderBrush is SolidColorBrush deckSingleBorder && deckSingleBorder.Color == ThemeService.Color("AccentBrush")
                && deckButtons[0].BorderThickness == new Thickness(2),
                "double-clicking a Deck slot keeps only that slot bright with the shared selection outline, dims its peers without badges, and opens the same action picker as Shortcut");
            deckButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var deckSwatches = window.DeckColorSwatches.Children.OfType<System.Windows.Controls.Button>().ToArray();
            var deckSwatchRows = deckSwatches.Select(swatch => Math.Round(swatch.TranslatePoint(new System.Windows.Point(), window.DeckColorSwatches).Y)).Distinct().Count();
            double lastDeckSwatchRight = deckSwatches[^1].TranslatePoint(new System.Windows.Point(deckSwatches[^1].ActualWidth, 0), window.DeckColorSwatches).X;
            Check(deckSwatches.Length == 6 && deckSwatchRows == 2 && lastDeckSwatchRight <= window.DeckColorSwatches.ActualWidth + .1,
                $"the narrower inspector reflows six Deck colors into two complete rows without clipping (right={lastDeckSwatchRight:F1}/{window.DeckColorSwatches.ActualWidth:F1})");
            window.ApplyCatalogActionForTest(new CatalogAction("テスト", "コピー", "", ActionKind.Shortcut, "Ctrl+C"));
            Pump(window);
            var autoIconMapping = DeckPanelLayout.FindMapping(standardDeck, 1);
            Check(!window.ValueBox.IsKeyboardFocusWithin && !window.IsEditingSelectedInputForTest && window.DestinationConfirmButton.Visibility == Visibility.Collapsed
                && autoIconMapping is { DeckIcon: "copy", DeckIconAutoAssigned: true }
                && DeckIconCatalog.CreateVisual(autoIconMapping, 22) != null
                && DeckIconCatalog.CreateVisual(new Mapping { DeckIcon = DeckIconCatalog.AnimatedId(autoIconMapping.DeckIcon) }, 22) != null,
                "using a selected Deck action completes editing and assigns its paired static/animated visual");
            window.SetDeckButtonNameForTest("Deck+01", "コピー");
            standardDeck.Mappings.Add(new Mapping { Input = "Deck+45", Layer = "Deck", Kind = ActionKind.Key, Value = "Z", Description = "保持" });
            double stableSaveButtonLeft = window.DeckSaveButton.TranslatePoint(new System.Windows.Point(), window.DeckEditorWorkspace).X;
            window.ApplyDeckSizeForTest(18, 1);
            Pump(window);
            bool wideDeckUsesBoundedViewport = window.DeckManagementGrid.Columns == 18 && window.DeckManagementGrid.Rows == 1
                && Math.Abs(window.DeckManagementGrid.Width - 18 * DeckPanelLayout.CellWidth) < .1
                && Math.Abs(window.DeckManagementGrid.Height - DeckPanelLayout.CellHeight) < .1
                && window.DeckGridScaleTransform.ScaleX is >= .25 and <= 1;
            window.ApplyDeckSizeForTest(1, 18);
            Pump(window);
            bool tallDeckUsesBoundedViewport = window.DeckManagementGrid.Columns == 1 && window.DeckManagementGrid.Rows == 18
                && Math.Abs(window.DeckManagementGrid.Width - DeckPanelLayout.CellWidth) < .1
                && Math.Abs(window.DeckManagementGrid.Height - 18 * DeckPanelLayout.CellHeight) < .1
                && window.DeckGridScaleTransform.ScaleY is >= .25 and <= 1;
            double saveButtonAfterExtremeLayouts = window.DeckSaveButton.TranslatePoint(new System.Windows.Point(), window.DeckEditorWorkspace).X;
            Check(wideDeckUsesBoundedViewport && tallDeckUsesBoundedViewport && Math.Abs(stableSaveButtonLeft - saveButtonAfterExtremeLayouts) < .1,
                "1-row-by-18-column and 18-row-by-1-column Decks stay bounded inside the preview without moving the editor header");
            window.ApplyDeckSizeForTest(3, 3);
            window.ApplyDeckSizeForTest(9, 5);
            Pump(window);
            var deckTexts = Descendants<TextBlock>(window.DeckManagementGrid).Select(x => x.Text).ToArray();
            Check(standardDeck.PanelWidth == null && standardDeck.PanelHeight == null
                && standardDeck.Mappings.Any(x => x.Input == "Deck+01" && x.Value == "Ctrl+C" && x.Description == "コピー")
                && standardDeck.Mappings.Any(x => x.Input == "Deck+45" && x.Value == "Z" && x.Description == "保持"),
                "changing a Deck grid resets only its obsolete zoom while preserving hidden assignments and editable button names");
            window.DeckOpacitySlider.Value = 67;
            Pump(window);
            var deckCenter = window.DeckGridViewbox.TranslatePoint(new System.Windows.Point(window.DeckGridViewbox.ActualWidth / 2, 0), window.DeckGridScrollViewer).X;
            bool centeredOrScrollablePreview = window.DeckGridScrollViewer.ScrollableWidth > .1 || Math.Abs(deckCenter - window.DeckGridScrollViewer.ViewportWidth / 2) < 2;
            Check(window.DeckOpacityValueText.Text == "67%" && window.ConfigForTest.InputPanelOpacityPercent == 67 && centeredOrScrollablePreview,
                $"Deck opacity is editable in place and the dedicated preview is centered when it fits or scrollable when readability requires it (center={deckCenter:F1}, viewport={window.DeckGridScrollViewer.ViewportWidth:F1}, scroll={window.DeckGridScrollViewer.ScrollableWidth:F1})");
            Check(window.DeckWindowActionTargetForTest == WindowActionTarget.ActiveWindow, "Deck actions always target the previously active window instead of the overlay under the cursor");
            Check(window.TaskbarWindowActionTargetForTest == WindowActionTarget.ActiveWindow
                  && MainWindow.IsTaskbarMappedInput("Taskbar+MouseMiddle")
                  && MainWindow.IsTaskbarMappedInput("Taskbar+MouseLeft:Long")
                  && !MainWindow.IsTaskbarMappedInput("MouseMiddle"),
                "taskbar mappings use the existing active window instead of resolving the Explorer taskbar as a cursor target");
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
            standardDeck.Mappings.Add(new Mapping { Input = "Deck+06", Layer = "Deck", DeckIcon = DeckIconCatalog.AnimatedId("refresh") });
            window.EditDeckLayoutForTest(standardDeck);
            Pump(window);
            Check((window.DeckAfterActionBehaviorBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == window.ConfigForTest.DeckAfterActionBehavior.ToString()
                && (window.DeckPointerLeaveBehaviorBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == window.ConfigForTest.DeckPointerLeaveBehavior.ToString()
                && window.DeckAfterActionBehaviorBox.Items.Cast<ComboBoxItem>().Select(item => item.Content?.ToString()).SequenceEqual(["そのまま表示", "画面端に折りたたむ", "Deckを非表示"])
                && window.DeckPointerLeaveBehaviorBox.Items.Cast<ComboBoxItem>().Select(item => item.Content?.ToString()).SequenceEqual(["そのまま表示", "画面端に折りたたむ", "Deckを非表示"])
                && Descendants<TextBlock>(window.DeckAutoHideSettingsGroup).Any(text => text.Text == "固定中は自動動作しません。")
                && !Descendants<TextBlock>(window.DeckSettingsPanel).Any(text => text.Text.Contains("各プロファイルに個別", StringComparison.Ordinal) || text.Text.Contains("クリック領域を完全", StringComparison.Ordinal))
                && window.DeckProfileSwitchBox.Content?.ToString() == "プロファイル別に切り替える"
                && window.DeckHoverPreviewBox.Content?.ToString() == "ファイルをホバー再生"
                && System.Windows.Automation.AutomationProperties.GetName(window.DeckOverlayToggleButton) == "Deckを表示",
                "Deck display settings name the trigger and result, distinguish collapse from hide, and reserve preview wording for the actual Deck");
            OverlayService.ResetDeckRefreshRequestCountForTest();
            window.DeckAfterActionBehaviorBox.SelectedItem = window.DeckAfterActionBehaviorBox.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, "Hide"));
            window.DeckPointerLeaveBehaviorBox.SelectedItem = window.DeckPointerLeaveBehaviorBox.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, "StayVisible"));
            Pump(window);
            Check(window.ConfigForTest.DeckAfterActionBehavior == DeckAutoDismissBehavior.Hide
                && window.ConfigForTest.DeckPointerLeaveBehavior == DeckAutoDismissBehavior.StayVisible
                && OverlayService.DeckRefreshRequestCountForTest == 2,
                "Deck display behaviors update the shared live overlay configuration immediately");
            window.DeckAfterActionBehaviorBox.SelectedItem = window.DeckAfterActionBehaviorBox.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, "CollapseToEdge"));
            window.DeckPointerLeaveBehaviorBox.SelectedItem = window.DeckPointerLeaveBehaviorBox.Items.Cast<ComboBoxItem>().Single(item => Equals(item.Tag, "CollapseToEdge"));
            Pump(window);
            var editorIconMenu = window.CreateDeckInputContextMenu("Deck+04");
            bool editorHasIconCommand = editorIconMenu.Items.OfType<MenuItem>().Select(item => item.Header).OfType<Grid>().SelectMany(grid => grid.Children.OfType<TextBlock>()).Any(text => text.Text == "アイコン変更...");
            Check(window.DeckManagementButtonsForTest[3].Content is TextBlock { Text: "\uE80F" } && editorHasIconCommand, $"Deck editor renders a selected preset and exposes icon change from right-click (content={window.DeckManagementButtonsForTest[3].Content?.GetType().Name}:{(window.DeckManagementButtonsForTest[3].Content as TextBlock)?.Text}, menu={editorHasIconCommand})");
            Check(window.DeckManagementButtonsForTest[4].Content is Grid missingEditorIcon && Descendants<System.Windows.Shapes.Path>(missingEditorIcon).Any(path => Equals(path.Stroke, ThemeService.Brush("DangerBrush"))) && window.DeckManagementButtonsForTest[4].ToolTip is System.Windows.Controls.ToolTip { Content: TextBlock { Text: "参照先のファイルが削除されたか、移動された可能性があります。" } }, "a missing Deck file automatically becomes a broken-link icon with a concise explanation in the editor");
            var assignmentMenu = window.CreateDeckInputContextMenu("Deck+01");
            string[] assignmentMenuLabels = assignmentMenu.Items.OfType<MenuItem>()
                .Select(item => item.Header).OfType<Grid>()
                .SelectMany(grid => grid.Children.OfType<TextBlock>())
                .Select(text => text.Text).ToArray();
            window.CopyDeckAssignmentForTest("Deck+01");
            window.PasteDeckAssignmentForTest("Deck+07");
            Check(window.HasCopiedDeckAssignmentForTest
                && assignmentMenuLabels.Contains("この割り当てをコピー")
                && assignmentMenuLabels.Contains("コピーした割り当てを貼り付け")
                && standardDeck.Mappings.LastOrDefault(mapping => mapping.Input == "Deck+07") is { Kind: ActionKind.Shortcut, Value: "Ctrl+C" },
                "Deck right-click exposes assignment copy/paste separately from file copy and pastes a complete assignment into another slot");
            var animatedResetMapping = standardDeck.Mappings.Single(mapping => mapping.Input == "Deck+06");
            bool animatedIconWasVisible = window.DeckManagementButtonsForTest[5].Content is TextBlock { Tag: string iconTag } && iconTag == DeckIconCatalog.VisualTag;
            animatedResetMapping.DeckIcon = "";
            window.ColorButtonsForTest();
            bool clearedIconIsDefault = window.DeckManagementButtonsForTest[5].Content is TextBlock clearedIcon
                && !Equals(clearedIcon.Tag, DeckIconCatalog.VisualTag)
                && !clearedIcon.FontFamily.Source.Contains("Fluent Icons", StringComparison.OrdinalIgnoreCase)
                && !clearedIcon.HasAnimatedProperties
                && !clearedIcon.RenderTransform.HasAnimatedProperties;
            Check(animatedIconWasVisible && clearedIconIsDefault, "removing an animated Deck icon rebuilds the ordinary default button instead of reusing the symbol font or animation");
            window.DeckManagementButtonsForTest[1].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(window.IsDeckEditorThumbnailOpenForTest, "clicking an image file in the Deck editor opens its thumbnail preview");
            window.DeckManagementButtonsForTest[2].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(window.IsDeckEditorAudioPlayingForTest && !window.IsDeckEditorThumbnailOpenForTest, "clicking an audio file in the Deck editor starts only its audio preview");
            window.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.PreviewMouseMoveEvent });
            Check(!window.IsDeckEditorAudioPlayingForTest, "the first pointer movement stops Deck editor audio immediately");
            CaptureForReview(window, "deck-manager.png");
            double compactDeckEditorWidth = window.Width, compactDeckEditorHeight = window.Height;
            double wideDeckEditorWidth = Math.Min(1500, SystemParameters.WorkArea.Width - 32);
            if (wideDeckEditorWidth >= 1200)
            {
                window.Width = wideDeckEditorWidth;
                window.Height = Math.Min(900, SystemParameters.WorkArea.Height - 32);
                Pump(window);
                window.ApplyDeckSizeForTest(18, 1);
                Pump(window);
                var wideLastCell = (FrameworkElement)window.DeckManagementGrid.Children[^1];
                double wideLastCellRight = wideLastCell.TranslatePoint(new System.Windows.Point(wideLastCell.ActualWidth, 0), window.DeckGridScrollViewer).X;
                bool completeWideRowVisible = window.DeckGridScrollViewer.ScrollableWidth < .1
                    && wideLastCellRight <= window.DeckGridScrollViewer.ViewportWidth + .5;
                CaptureForReview(window, "deck-editor-wide-18x1.png");
                window.ApplyDeckSizeForTest(1, 18);
                Pump(window);
                var tallLastCell = (FrameworkElement)window.DeckManagementGrid.Children[^1];
                double tallLastCellBottom = tallLastCell.TranslatePoint(new System.Windows.Point(0, tallLastCell.ActualHeight), window.DeckGridScrollViewer).Y;
                bool completeTallColumnVisible = window.DeckGridScrollViewer.ScrollableHeight < .1
                    && tallLastCellBottom <= window.DeckGridScrollViewer.ViewportHeight + .5;
                CaptureForReview(window, "deck-editor-wide-1x18.png");
                window.ApplyDeckSizeForTest(9, 5);
                Pump(window);
                Check(window.DeckSettingsPanel.Visibility == Visibility.Collapsed
                    && Grid.GetColumn(window.DeckPreviewPane) == 0
                    && window.DeckPreviewPane.ActualWidth > 0
                    && window.DeckGridScrollViewer.ViewportWidth > 0
                    && completeWideRowVisible
                    && completeTallColumnVisible
                    && !Descendants<TextBlock>(window.DeckPreviewPane).Any(text => text.Text is "Deckプレビュー" or "非表示" or "表示中"),
                    $"the default Deck editor dedicates the body to the real preview and shows complete 18x1/1x18 layouts (wideRight={wideLastCellRight:F1}/{window.DeckGridScrollViewer.ViewportWidth:F1}, tallBottom={tallLastCellBottom:F1}/{window.DeckGridScrollViewer.ViewportHeight:F1})");
                CaptureForReview(window, "deck-editor-wide.png");
                window.DeckListViewToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Pump(window);
                var deckListRows = window.DeckListPanel.Children.OfType<Grid>().ToArray();
                Check(window.DeckListScrollViewer.Visibility == Visibility.Visible
                    && window.DeckGridScrollViewer.Visibility == Visibility.Collapsed
                    && deckListRows.Length == DeckPanelLayout.VisibleSlotCount(standardDeck)
                    && deckListRows.All(row => row.ColumnDefinitions.Count == 2)
                    && Descendants<TextBlock>(window.DeckListPanel).Any(text => text.Text == "未設定")
                    && Descendants<TextBlock>(window.DeckListPanel).Any(text => text.Text.Contains("Ctrl", StringComparison.OrdinalIgnoreCase) || text.Text == "コピー"),
                    "Deck list view shows every slot in a scrollable two-column button/Action list, including unassigned slots");
                CaptureForReview(window, "deck-editor-list.png");
                window.DeckGridViewToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                window.DeckCustomizeToggleButton.IsChecked = true;
                window.DeckCustomizeToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Pump(window);
                double wideSettingsLeft = window.DeckSettingsPanel.TranslatePoint(new System.Windows.Point(), window.DeckEditorWorkspace).X;
                double widePreviewRight = window.DeckPreviewPane.TranslatePoint(new System.Windows.Point(window.DeckPreviewPane.ActualWidth, 0), window.DeckEditorWorkspace).X;
                Check(window.DeckSettingsPanel.Visibility == Visibility.Visible
                    && Grid.GetColumn(window.DeckSettingsPanel) == 2
                    && Grid.GetColumn(window.DeckPreviewPane) == 0
                    && wideSettingsLeft > widePreviewRight
                    && window.DeckLayoutCustomizeTab.IsChecked == true
                    && window.DeckCoreSettingsCard.Visibility == Visibility.Visible
                    && window.DeckLayoutSettingsCard.Visibility == Visibility.Visible
                    && window.DeckAppearanceSettingsCard.Visibility == Visibility.Collapsed
                    && window.DeckAutoHideSettingsCard.Visibility == Visibility.Collapsed,
                    "customization opens as one tabbed drawer between the preview and the frame-free inspector");
                CaptureForReview(window, "deck-editor-customize.png");
                window.DeckCustomizeCloseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump(window);
                window.Width = compactDeckEditorWidth;
                window.Height = compactDeckEditorHeight;
                Pump(window);
                window.DeckCustomizeToggleButton.IsChecked = true;
                window.DeckCustomizeToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Pump(window);
                double compactOverlayRight = window.DeckOverlayToggleButton.TranslatePoint(new System.Windows.Point(window.DeckOverlayToggleButton.ActualWidth, 0), window.DeckEditorWorkspace).X;
                double compactViewLeft = window.DeckGridViewToggle.TranslatePoint(new System.Windows.Point(), window.DeckEditorWorkspace).X;
                double compactTitleRight = window.DeckLayoutNameBox.TranslatePoint(new System.Windows.Point(window.DeckLayoutNameBox.ActualWidth, 0), window.DeckEditorWorkspace).X;
                Check(Grid.GetRow(window.DeckSettingsPanel) == 0
                    && Grid.GetRow(window.DeckPreviewPane) == 2
                    && window.DeckSettingsPanel.ActualWidth <= window.DeckEditorWorkspace.ActualWidth + .1
                    && compactTitleRight <= compactViewLeft + .5
                    && compactOverlayRight <= window.DeckEditorWorkspace.ActualWidth + .5
                    && window.DeckCustomizeLabel.Visibility == Visibility.Collapsed
                    && window.DeckPreviewPane.ActualHeight > 0,
                    $"compact Deck editing stacks the drawer and icon-compacts the command bar without clipping or overlap (titleRight={compactTitleRight:F1}, viewLeft={compactViewLeft:F1}, overlayRight={compactOverlayRight:F1}/{window.DeckEditorWorkspace.ActualWidth:F1})");
                CaptureForReview(window, "deck-editor-compact-customize.png");
                window.DeckCustomizeCloseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump(window);
            }
            else
                Check(true, "wide Deck editor capture is not applicable on this work area");
            Mapping? deckExecuted = null;
            (double Left, double Top)? savedDeckPosition = null;
            (double Width, double Height)? savedDeckSize = null;
            string? savedDeckSizeLayoutId = null;
            var overlayLayout = new DeckLayoutDefinition { Name = "標準Deck", Columns = 9, Rows = 5, Mappings = [new Mapping { Input = "Deck+01", Layer = "Deck", Kind = ActionKind.Shortcut, Value = "Ctrl+C", Description = "コピー" }] };
            var deckOverlayConfig = new AppConfig { InputPanelOpacityPercent = 67, DeckAfterActionBehavior = DeckAutoDismissBehavior.StayVisible, DeckPointerLeaveBehavior = DeckAutoDismissBehavior.StayVisible, DeckPanelLeft = 120, DeckPanelTop = 140, DeckLayouts = [overlayLayout], Profiles = [new Profile { Name = "標準", DefaultDeckLayoutId = overlayLayout.Id }], SharedDefaultDeckLayoutId = overlayLayout.Id };
            overlayLayout.Mappings.Add(new Mapping { Input = "Deck+02", Layer = "Deck", DeckFilePath = deckPreviewImage });
            overlayLayout.Mappings.Add(new Mapping { Input = "Deck+03", Layer = "Deck", DeckFilePath = deckPreviewVideo });
            overlayLayout.Mappings.Add(new Mapping { Input = "Deck+04", Layer = "Deck", DeckFilePath = deckPreviewImage, DeckIcon = "search" });
            overlayLayout.Mappings.Add(new Mapping { Input = "Deck+05", Layer = "Deck", DeckFilePath = missingDeckFile });
            var backdropProbe = CreateBackdropProbeWindow();
            backdropProbe.Show();
            backdropProbe.UpdateLayout();
            var deckConstructionTime = System.Diagnostics.Stopwatch.StartNew();
            var deckOverlay = new DeckPanelOverlayWindow(deckOverlayConfig, map => deckExecuted = map, 67, (left, top) => savedDeckPosition = (left, top), overlayLayout, (layoutId, width, height) => { savedDeckSizeLayoutId = layoutId; savedDeckSize = (width, height); });
            deckConstructionTime.Stop();
            bool deckReadyBeforeShow = deckOverlay.DeckButtons.Count == 45;
            var deckShowTime = System.Diagnostics.Stopwatch.StartNew();
            deckOverlay.Show();
            deckShowTime.Stop();
            deckOverlay.UpdateLayout();
            Pump(window);
            PumpFor(TimeSpan.FromMilliseconds(150));
            var overlayCells = deckOverlay.DeckButtons.Select(button => (StackPanel)button.Parent).ToArray();
            Check(deckOverlay.DeckButtons.All(button => Math.Abs(button.Width - DeckPanelLayout.KeyWidth) < .1 && Math.Abs(button.Height - DeckPanelLayout.KeyHeight) < .1)
                && overlayCells.All(cell => Math.Abs(cell.Width - DeckPanelLayout.KeyWidth - DeckPanelLayout.ButtonGap) < .1 && Math.Abs(cell.Height - DeckPanelLayout.KeyHeight - DeckPanelLayout.ButtonGap) < .1)
                && overlayCells.All(cell => Descendants<TextBlock>(cell).Any(label => Math.Abs(label.Height - DeckPanelLayout.NameLabelHeight) < .1)),
                "floating Deck keeps each name below its 54x52 button and matches the visible row and column gaps");
            deckOverlay.DeckButtons[2].RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            Pump(window);
            int videoPreviewsBeforeHide = deckOverlay.VideoPreviewCountForTest;
            deckOverlay.CapturePanelMouseForTest();
            if (deckOverlay.OwnsMouseCaptureForTest)
                Check(true, "the Deck drag surface can own capture while it is being moved");
            else
                output.WriteLine("SKIP Deck drag capture acquisition: the current test session denied global mouse capture");
            deckOverlay.HideForReuse();
            Check(!deckOverlay.OwnsMouseCaptureForTest, "hiding a cached Deck releases every Deck-owned mouse capture");
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
            Check(deckOverlay.HeaderBackgroundForTest == System.Windows.Media.Brushes.Transparent && deckOverlay.HeaderGripVisibleForTest && DeckPanelOverlayWindow.CanDragPanelFromForTest((Border)deckOverlay.Content) && !DeckPanelOverlayWindow.CanDragPanelFromForTest(deckOverlay.DeckButtons[0]) && deckOverlay.PanelPaddingForTest.Left == 12 && deckOverlay.PanelPaddingForTest.Top == 12 && deckOverlay.PanelPaddingForTest.Right == 12 && deckOverlay.PanelPaddingForTest.Bottom == 12 && overlayDeckView.Margin == new Thickness(0) && overlayDeckView.StretchDirection == StretchDirection.Both && overlayDeckView.HorizontalAlignment == System.Windows.HorizontalAlignment.Center && overlayDeckView.VerticalAlignment == VerticalAlignment.Center && Math.Abs(overlayDeckView.ActualWidth - overlayRoot.ActualWidth) < 1 && Math.Abs(overlayDeckView.ActualHeight - overlayRoot.RowDefinitions[2].ActualHeight) < 1, $"large Decks show the grip, every non-button panel surface can drag, and the aspect-locked grid leaves no extra blank band (grip={deckOverlay.HeaderGripVisibleForTest}, view={overlayDeckView.ActualWidth:F2}x{overlayDeckView.ActualHeight:F2}, root={overlayRoot.ActualWidth:F2}x{overlayRoot.RowDefinitions[2].ActualHeight:F2})");
            var cornerHits = new[] { new System.Windows.Point(1, 1), new System.Windows.Point(deckOverlay.ActualWidth - 1, 1), new System.Windows.Point(1, deckOverlay.ActualHeight - 1), new System.Windows.Point(deckOverlay.ActualWidth - 1, deckOverlay.ActualHeight - 1) }.Select(deckOverlay.ResizeHitTestForTest).ToArray();
            Check(deckOverlay.ResizeMode == ResizeMode.CanResize && cornerHits.All(hit => hit != 0) && cornerHits.Distinct().Count() == 4 && deckOverlay.ResizeHitTestForTest(new System.Windows.Point(deckOverlay.ActualWidth / 2, deckOverlay.ActualHeight / 2)) == 0, "all four Deck overlay corners expose distinct resize hit zones without consuming the center");
            Check(deckOverlay.DeckButtons.Count == 45 && deckOverlay.DeckButtons.All(x => x.IsEnabled && x.Background is SolidColorBrush && !Descendants<Border>(x).Any(border => border.Background is LinearGradientBrush)) && Math.Abs(deckOverlay.VisualOpacityForTest - .67) < .001 && !deckOverlay.AllowsTransparency && deckOverlay.Background is SolidColorBrush { Color.A: 255 } && !deckOverlay.ShowActivated && deckOverlay.UsesNoActivateStyle && Descendants<TextBlock>(deckOverlay).Any(x => x.Text == "コピー") && Math.Abs(deckOverlay.Left - 120) < .1 && Math.Abs(deckOverlay.Top - 140) < .1, "Deck overlay uses an opaque rounded native window, never an invisible transparent hit surface, while retaining flat button faces and no-activate behavior");
            var overlayHoverButton = deckOverlay.DeckButtons[0];
            overlayHoverButton.ApplyTemplate();
            var overlayHoverRoot = (FrameworkElement)overlayHoverButton.Template.FindName("HoverRoot", overlayHoverButton)!;
            overlayHoverButton.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseEnterEvent });
            PumpFor(TimeSpan.FromMilliseconds(150));
            Check(overlayHoverRoot.RenderTransform is ScaleTransform overlayHoverScale && overlayHoverScale.HasAnimatedProperties && overlayHoverScale.ScaleX >= 1.049 && overlayHoverScale.ScaleY >= 1.049
                && overlayHoverButton.Template.FindName("GlassHighlight", overlayHoverButton) == null
                && overlayHoverButton.Template.FindName("HoverUnderline", overlayHoverButton) == null,
                "Deck overlay hover uses the same visible five-percent transform-only pop without adding a color wash or underline");
            overlayHoverButton.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseLeaveEvent });
            var overlayDropTarget = deckOverlay.DeckButtons[1];
            var overlayTargetBackground = overlayDropTarget.Background;
            var overlayTargetBorder = overlayDropTarget.BorderBrush;
            var overlayTargetThickness = overlayDropTarget.BorderThickness;
            deckOverlay.SetDeckReorderTargetForTest((int)overlayDropTarget.Tag);
            overlayDropTarget.ApplyTemplate();
            var overlayDropBadge = (UIElement)overlayDropTarget.Template.FindName("DropTargetBadge", overlayDropTarget)!;
            Check(ReferenceEquals(overlayDropTarget.Background, overlayTargetBackground) && overlayDropTarget.BorderThickness == new Thickness(3) && overlayDropTarget.Effect == null && overlayDropBadge.Opacity == 1,
                "Deck overlay reorder targets preserve the complete button face and restore the clear drop marker");
            deckOverlay.ClearDeckReorderTargetForTest();
            Check(ReferenceEquals(overlayDropTarget.BorderBrush, overlayTargetBorder) && overlayDropTarget.BorderThickness == overlayTargetThickness && overlayDropBadge.Opacity == 0,
                "clearing a Deck overlay reorder target restores its exact previous border without changing click behavior");
            var compactOverlayPreview = DeckPanelOverlayWindow.CreateCompactDragPreview(new Border());
            Check(Math.Abs(compactOverlayPreview.PreviewWidthForTest - 20) < .1 && Math.Abs(compactOverlayPreview.PreviewHeightForTest - 20) < .1,
                "Deck overlay reorder and file drags use the same compact 20 by 20 preview as the editor");
            compactOverlayPreview.Close();
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
            double liveResizeStartWidth = deckOverlay.ActualWidth;
            double liveResizeStartHeight = deckOverlay.ActualHeight;
            deckOverlay.BeginInteractiveSizingForTest(liveResizeStartWidth, liveResizeStartHeight);
            var resizeNoiseFrame = deckOverlay.ConstrainInteractiveSizeForTest(liveResizeStartWidth + .8, liveResizeStartHeight + 1.2, 8);
            bool ignoredResizeNoise = deckOverlay.CornerResizeWidthDrivenForTest == null
                && Math.Abs(resizeNoiseFrame.Width - liveResizeStartWidth) < 1
                && Math.Abs(resizeNoiseFrame.Height - liveResizeStartHeight) < 1;
            var liveResizeFrames = new[]
            {
                deckOverlay.ConstrainInteractiveSizeForTest(liveResizeStartWidth + 20, liveResizeStartHeight + 2, 8),
                deckOverlay.ConstrainInteractiveSizeForTest(liveResizeStartWidth + 40, liveResizeStartHeight + 160, 8),
                deckOverlay.ConstrainInteractiveSizeForTest(liveResizeStartWidth + 60, liveResizeStartHeight + 1, 8)
            };
            bool liveResizeStayedSmooth = ignoredResizeNoise
                && deckOverlay.CornerResizeWidthDrivenForTest == true
                && !deckOverlay.AppliesRoundedRegionDuringResizeForTest
                && liveResizeFrames.Zip(liveResizeFrames.Skip(1), (before, after) => after.Width > before.Width && after.Height > before.Height).All(value => value);
            deckOverlay.EndInteractiveSizingForTest();
            Check(liveResizeStayedSmooth && deckOverlay.AppliesRoundedRegionDuringResizeForTest,
                "continuous corner resizing ignores rounding noise, locks one driving axis, changes both dimensions monotonically, and defers native rounded-region redraw until release");
            deckOverlay.BeginInteractiveSizingForTest(liveResizeStartWidth, liveResizeStartHeight);
            _ = deckOverlay.ConstrainInteractiveSizeForTest(liveResizeStartWidth + 1, liveResizeStartHeight + 2, 8);
            var verticalResizeFrames = new[]
            {
                deckOverlay.ConstrainInteractiveSizeForTest(liveResizeStartWidth + 2, liveResizeStartHeight + 30, 8),
                deckOverlay.ConstrainInteractiveSizeForTest(liveResizeStartWidth + 140, liveResizeStartHeight + 50, 8),
                deckOverlay.ConstrainInteractiveSizeForTest(liveResizeStartWidth + 3, liveResizeStartHeight + 70, 8)
            };
            bool verticalResizeStayedSmooth = deckOverlay.CornerResizeWidthDrivenForTest == false
                && verticalResizeFrames.Zip(verticalResizeFrames.Skip(1), (before, after) => after.Width > before.Width && after.Height > before.Height).All(value => value);
            deckOverlay.EndInteractiveSizingForTest();
            Check(verticalResizeStayedSmooth,
                "a vertically led corner resize keeps the height driver after later horizontal pointer noise");
            deckOverlay.ResizeAndPersistForTest(deckOverlay.Width + 40, deckOverlay.Height + 30);
            Pump(window);
            Check(savedDeckSizeLayoutId == overlayLayout.Id && savedDeckSize is { } resizedDeck && Math.Abs(resizedDeck.Width - deckOverlay.ActualWidth) < .1 && Math.Abs(resizedDeck.Height - deckOverlay.ActualHeight) < .1 && Math.Abs(overlayDeckView.ActualWidth - overlayRoot.ActualWidth) < 1 && Math.Abs(overlayDeckView.ActualHeight - overlayRoot.RowDefinitions[2].ActualHeight) < 1, $"resizing the Deck overlay preserves its Deck aspect without blank bands and persists its new size under that layout only (saved={savedDeckSize?.Width:F2}x{savedDeckSize?.Height:F2}, actual={deckOverlay.ActualWidth:F2}x{deckOverlay.ActualHeight:F2}, view={overlayDeckView.ActualWidth:F2}x{overlayDeckView.ActualHeight:F2}, root={overlayRoot.ActualWidth:F2}x{overlayRoot.RowDefinitions[2].ActualHeight:F2})");
            deckOverlay.ResetSizeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(deckOverlay.ResetSizeButton.ToolTip?.ToString() == "初期サイズに戻す" && deckOverlay.ResetSizeButton.Content is System.Windows.Shapes.Path && Math.Abs(deckOverlay.ActualWidth - deckOverlay.DefaultWidthForTest) < 1 && Math.Abs(deckOverlay.ActualHeight - deckOverlay.DefaultHeightForTest) < 1, $"the header reset icon restores the Deck overlay's fitted initial size (actual={deckOverlay.ActualWidth:F2}x{deckOverlay.ActualHeight:F2}, default={deckOverlay.DefaultWidthForTest:F2}x{deckOverlay.DefaultHeightForTest:F2})");
            CaptureForReview(deckOverlay, "deck-overlay.png");
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
            var independentlySizedDeckA = new DeckLayoutDefinition { Name = "Deck A", Columns = 3, Rows = 3, PanelWidth = 320, PanelHeight = 320 };
            var independentlySizedDeckB = new DeckLayoutDefinition { Name = "Deck B", Columns = 3, Rows = 3, PanelWidth = 520, PanelHeight = 520 };
            var independentlySizedConfig = new AppConfig { DeckPanelWidth = 410, DeckPanelHeight = 410, DeckLayouts = [independentlySizedDeckA, independentlySizedDeckB] };
            var independentlySizedOverlayA = new DeckPanelOverlayWindow(independentlySizedConfig, null, selectedLayout: independentlySizedDeckA);
            var independentlySizedOverlayB = new DeckPanelOverlayWindow(independentlySizedConfig, null, selectedLayout: independentlySizedDeckB);
            Check(independentlySizedOverlayB.Width - independentlySizedOverlayA.Width > 100, "different Deck layouts restore their own saved sizes instead of inheriting the most recently resized Deck");
            independentlySizedOverlayA.Close();
            independentlySizedOverlayB.Close();
            var profileShapeLayout = new DeckLayoutDefinition { Name = "Profile shape", Columns = 8, Rows = 2 };
            var profileShapeOverlay = new DeckPanelOverlayWindow(new AppConfig { DeckPanelWidth = 900, DeckPanelHeight = 120, DeckLayouts = [profileShapeLayout] }, null, selectedLayout: profileShapeLayout);
            Check(Math.Abs(profileShapeOverlay.Width - profileShapeOverlay.DefaultWidthForTest) < .1
                && Math.Abs(profileShapeOverlay.Height - profileShapeOverlay.DefaultHeightForTest) < .1,
                "a profile-linked Deck with a different grid uses its own fitted zoom instead of inheriting a legacy global Deck size");
            profileShapeOverlay.Close();
            var narrowDeckLayout = new DeckLayoutDefinition { Name = "縦長Deck", Columns = 1, Rows = 18 };
            var narrowDeckOverlay = new DeckPanelOverlayWindow(new AppConfig { DeckLayouts = [narrowDeckLayout] }, null, selectedLayout: narrowDeckLayout);
            narrowDeckOverlay.Show();
            narrowDeckOverlay.UpdateLayout();
            Pump(window);
            var narrowResetTopLeft = narrowDeckOverlay.ResetSizeButton.TranslatePoint(new System.Windows.Point(), narrowDeckOverlay);
            var narrowCloseBottomRight = narrowDeckOverlay.CloseButton.TranslatePoint(new System.Windows.Point(narrowDeckOverlay.CloseButton.ActualWidth, narrowDeckOverlay.CloseButton.ActualHeight), narrowDeckOverlay);
            var narrowHeaderMenu = narrowDeckOverlay.HeaderContextMenuForTest;
            var narrowStoreItem = narrowHeaderMenu?.Items.OfType<MenuItem>().FirstOrDefault();
            Check(!narrowDeckOverlay.HeaderTitleVisibleForTest && !narrowDeckOverlay.HeaderGripVisibleForTest && narrowDeckOverlay.HeaderToolTipForTest == narrowDeckLayout.Name && DeckPanelOverlayWindow.CanDragPanelFromForTest((Border)narrowDeckOverlay.Content) && !DeckPanelOverlayWindow.CanDragPanelFromForTest(narrowDeckOverlay.DeckButtons[0]) && !narrowDeckOverlay.ResetSizeButton.IsVisible && !narrowDeckOverlay.MoreButton.IsVisible && narrowDeckOverlay.FullScreenButton.IsVisible && narrowDeckOverlay.CloseButton.IsVisible && narrowDeckOverlay.FullScreenButton.ActualWidth <= 24.1 && narrowDeckOverlay.CloseButton.ActualWidth <= 24.1 && narrowResetTopLeft.X >= 0 && narrowCloseBottomRight.X <= narrowDeckOverlay.ActualWidth - 6 && narrowCloseBottomRight.Y <= narrowDeckOverlay.ActualHeight + .1 && narrowHeaderMenu?.Items.Count == 2 && ReferenceEquals(narrowHeaderMenu, narrowDeckOverlay.PanelContextMenuForTest) && narrowStoreItem is { Header: "画面端に折りたたむ", IsCheckable: true }, "a 1-by-18 Deck prioritizes maximize and hide, remains draggable from every non-key surface, and exposes the explicitly named collapse command from blank panel space");
            CaptureForReview(narrowDeckOverlay, "deck-overlay-1x18.png");
            narrowDeckOverlay.Close();
            var changingDeckLayout = new DeckLayoutDefinition { Name = "サイズ変更Deck", Columns = 9, Rows = 5 };
            var changingDeckOverlay = new DeckPanelOverlayWindow(new AppConfig { DeckLayouts = [changingDeckLayout] }, null, selectedLayout: changingDeckLayout);
            changingDeckOverlay.Show();
            changingDeckOverlay.UpdateLayout();
            Pump(window);
            changingDeckLayout.Columns = 1;
            changingDeckLayout.Rows = 9;
            changingDeckOverlay.Refresh(96, true);
            changingDeckOverlay.UpdateLayout();
            Pump(window);
            var changingDeckView = Descendants<Viewbox>(changingDeckOverlay).Single(view => view.Child is System.Windows.Controls.Primitives.UniformGrid);
            var changingDeckRoot = (Grid)changingDeckView.Parent;
            Check(changingDeckOverlay.ActualWidth < 120
                  && Math.Abs(changingDeckView.ActualWidth - changingDeckRoot.ActualWidth) < 1
                  && changingDeckOverlay.DeckButtons.Count == 9,
                $"changing an existing Deck from 9-by-5 to 1-by-9 clears the old minimum width instead of persisting a blank right band (window={changingDeckOverlay.ActualWidth:F1}, grid={changingDeckView.ActualWidth:F1}, host={changingDeckRoot.ActualWidth:F1})");
            CaptureForReview(changingDeckOverlay, "deck-overlay-resized-1x9.png");
            changingDeckOverlay.Close();
            var autoHideLayout = new DeckLayoutDefinition { Name = "Auto hide", Columns = 3, Rows = 3, Mappings = [new Mapping { Input = "Deck+01", Layer = "Deck", Kind = ActionKind.Shortcut, Value = "Ctrl+C" }] };
            bool? savedPinned = null;
            (double Left, double Top)? savedCollapsedPosition = null;
            Mapping? autoHideExecuted = null;
            var autoHideConfig = new AppConfig { DeckLayouts = [autoHideLayout], DeckAfterActionBehavior = DeckAutoDismissBehavior.CollapseToEdge, DeckPointerLeaveBehavior = DeckAutoDismissBehavior.CollapseToEdge };
            var autoHideOverlay = new DeckPanelOverlayWindow(autoHideConfig, mapping => autoHideExecuted = mapping, selectedLayout: autoHideLayout, pinnedChanged: (_, pinned) => savedPinned = pinned, collapsedPositionChanged: (left, top) =>
            {
                savedCollapsedPosition = (left, top);
                autoHideConfig.DeckPanelCollapsedLeft = left;
                autoHideConfig.DeckPanelCollapsedTop = top;
            });
            var simulatedAutoHideCursor = new System.Drawing.Point();
            autoHideOverlay.CursorPositionProviderForTest = () => simulatedAutoHideCursor;
            try
            {
                var work = SystemParameters.WorkArea;
                autoHideOverlay.Left = work.Right - autoHideOverlay.Width - 8;
                autoHideOverlay.Top = work.Bottom - autoHideOverlay.Height - 8;
                simulatedAutoHideCursor = new System.Drawing.Point((int)work.Left + 4, (int)work.Top + 4);
                autoHideOverlay.PrepareForShow();
                autoHideOverlay.Show();
                PumpFor(TimeSpan.FromMilliseconds(650));
                Check(autoHideOverlay.IsVisible, "an unpinned Deck shown away from the pointer remains visible until the pointer has entered it once");
                var storageMenu = autoHideOverlay.PanelContextMenuForTest!;
                var storageItem = storageMenu.Items.OfType<MenuItem>().First();
                storageMenu.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.ContextMenu.OpenedEvent));
                bool storageCheckedForUnpinnedDeck = storageItem.IsChecked;
                storageItem.IsChecked = false;
                storageItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                bool uncheckedStoragePinsDeck = savedPinned == true && autoHideOverlay.IsPinnedForTest;
                storageItem.IsChecked = true;
                storageItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Pump(window);
                bool checkedStorageCollapsesDeck = savedPinned == false && !autoHideOverlay.IsPinnedForTest && autoHideOverlay.IsCollapsedToEdge;
                storageMenu.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.ContextMenu.ClosedEvent));
                autoHideOverlay.ExpandFromEdge();
                Pump(window);
                Check(storageCheckedForUnpinnedDeck && uncheckedStoragePinsDeck && checkedStorageCollapsesDeck,
                    "the blank-area storage menu mirrors pin state, pins when unchecked, and immediately stores when checked");
                double expandedAutoHideWidth = autoHideOverlay.ActualWidth;
                double expandedAutoHideHeight = autoHideOverlay.ActualHeight;
                var expandedAutoHidePosition = new System.Windows.Point(autoHideOverlay.Left, autoHideOverlay.Top);
                autoHideOverlay.DeckButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                PumpFor(TimeSpan.FromMilliseconds(500));
                Check(autoHideOverlay.IsVisible && autoHideOverlay.IsCollapsedToEdge && autoHideExecuted?.Value == "Ctrl+C", "an unpinned Deck collapses to a small edge tab after executing a button without losing the action");
                double collapsedAutoHideWidth = autoHideOverlay.ActualWidth;
                double collapsedAutoHideHeight = autoHideOverlay.ActualHeight;
                var moveHandleRight = autoHideOverlay.CollapsedMoveHandle.TranslatePoint(new System.Windows.Point(autoHideOverlay.CollapsedMoveHandle.ActualWidth, 0), autoHideOverlay).X;
                Check(autoHideOverlay.WindowState == WindowState.Normal
                    && collapsedAutoHideWidth <= 220.1
                    && collapsedAutoHideHeight < 70
                    && !autoHideOverlay.PinButton.IsVisible
                    && !autoHideOverlay.ResetSizeButton.IsVisible
                    && !autoHideOverlay.MoreButton.IsVisible
                    && !autoHideOverlay.CloseButton.IsVisible
                    && autoHideOverlay.CollapsedMoveHandle.IsVisible
                    && moveHandleRight >= autoHideOverlay.ActualWidth - 15,
                    "a collapsed Deck owns only a small normal-window hit surface and replaces unusable header controls with one right-edge move handle");
                var collapsedBodyPoint = autoHideOverlay.PointToScreen(new System.Windows.Point(12, autoHideOverlay.ActualHeight / 2));
                simulatedAutoHideCursor = new System.Drawing.Point((int)Math.Round(collapsedBodyPoint.X), (int)Math.Round(collapsedBodyPoint.Y));
                autoHideOverlay.ArmEdgeExpansionForTest();
                autoHideOverlay.ContinueFromCollapsedMoveHandleForTest();
                Pump(window);
                Check(!autoHideOverlay.IsCollapsedToEdge, $"entering a collapsed Deck through its move handle expands as soon as the pointer continues into the Deck body (armed={autoHideOverlay.EdgeExpansionArmedForTest}, outside={autoHideOverlay.CursorOutsideForTest}, panelOver={autoHideOverlay.IsMouseOver}, handleOver={autoHideOverlay.CollapsedMoveHandle.IsMouseOver})");
                autoHideOverlay.CollapseToEdge();
                Pump(window);
                autoHideOverlay.MoveCollapsedTabForTest(work.Left + 120, work.Top + 90);
                Pump(window);
                bool collapsedTabMoved = Math.Abs(autoHideOverlay.Left - (work.Left + 120)) < 1 && Math.Abs(autoHideOverlay.Top - (work.Top + 90)) < 1
                    && savedCollapsedPosition is { } savedCollapsed
                    && Math.Abs(savedCollapsed.Left - autoHideOverlay.Left) < 1
                    && Math.Abs(savedCollapsed.Top - autoHideOverlay.Top) < 1;
                var virtualDesktop = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
                // WorkArea excludes the taskbar while VirtualScreen does not.  A small
                // difference (40 px on this machine) is therefore not another monitor.
                const double monitorBoundaryMargin = 64;
                bool hasAnotherMonitorArea = virtualDesktop.Left < work.Left - monitorBoundaryMargin
                    || virtualDesktop.Top < work.Top - monitorBoundaryMargin
                    || virtualDesktop.Right > work.Right + monitorBoundaryMargin
                    || virtualDesktop.Bottom > work.Bottom + monitorBoundaryMargin;
                bool crossedMonitorBoundary = true;
                var crossRequest = new System.Windows.Point(autoHideOverlay.Left, autoHideOverlay.Top);
                var crossActual = crossRequest;
                if (hasAnotherMonitorArea)
                {
                    double secondLeft = virtualDesktop.Left < work.Left - monitorBoundaryMargin ? virtualDesktop.Left + 32 : virtualDesktop.Right > work.Right + monitorBoundaryMargin ? work.Right + 32 : work.Left + 120;
                    double secondTop = virtualDesktop.Top < work.Top - monitorBoundaryMargin ? virtualDesktop.Top + 32 : virtualDesktop.Bottom > work.Bottom + monitorBoundaryMargin ? work.Bottom + 32 : work.Top + 90;
                    crossRequest = new System.Windows.Point(secondLeft, secondTop);
                    autoHideOverlay.MoveCollapsedTabForTest(secondLeft, secondTop);
                    Pump(window);
                    crossActual = new System.Windows.Point(autoHideOverlay.Left, autoHideOverlay.Top);
                    // Windows can normalize a top-level window by a few device-independent pixels
                    // when it crosses monitors with different DPI.  The contract is that the tab
                    // reaches the other monitor, not that WPF preserves the requested raw pixels.
                    crossedMonitorBoundary = !work.Contains(new System.Windows.Point(
                        autoHideOverlay.Left + autoHideOverlay.ActualWidth / 2,
                        autoHideOverlay.Top + autoHideOverlay.ActualHeight / 2));
                    autoHideOverlay.MoveCollapsedTabForTest(work.Left + 120, work.Top + 90);
                    Pump(window);
                }
                autoHideOverlay.ExpandFromEdge();
                Pump(window);
                Check(collapsedTabMoved && crossedMonitorBoundary
                    && Math.Abs(autoHideOverlay.Left - expandedAutoHidePosition.X) < 1
                    && Math.Abs(autoHideOverlay.Top - expandedAutoHidePosition.Y) < 1,
                    $"moving the collapsed Deck tab crosses monitor boundaries without changing the position restored by the next expansion (moved={collapsedTabMoved}, crossed={crossedMonitorBoundary}, another={hasAnotherMonitorArea}, work={work}, virtual={virtualDesktop}, request={crossRequest}, actual={crossActual}, restored=({autoHideOverlay.Left:F1},{autoHideOverlay.Top:F1}), expected=({expandedAutoHidePosition.X:F1},{expandedAutoHidePosition.Y:F1}))");
                autoHideOverlay.CollapseToEdge();
                Pump(window);
                Check(Math.Abs(autoHideOverlay.Left - (work.Left + 120)) < 1 && Math.Abs(autoHideOverlay.Top - (work.Top + 90)) < 1,
                    "a moved collapsed Deck returns to its saved collapsed position instead of recalculating the nearest edge");
                System.Windows.Point collapsedTabCenter = autoHideOverlay.PointToScreen(new System.Windows.Point(
                    autoHideOverlay.ActualWidth / 2,
                    autoHideOverlay.ActualHeight / 2));
                autoHideOverlay.ExpandFromEdge();
                Pump(window);
                autoHideOverlay.Left = work.Left + Math.Max(80, (work.Width - autoHideOverlay.ActualWidth) / 2);
                autoHideOverlay.Top = work.Bottom - autoHideOverlay.ActualHeight - 40;
                autoHideOverlay.UpdateLayout();
                System.Windows.Point expandedDeckCenter = autoHideOverlay.PointToScreen(new System.Windows.Point(
                    autoHideOverlay.ActualWidth / 2,
                    autoHideOverlay.ActualHeight / 2));
                simulatedAutoHideCursor = new System.Drawing.Point((int)Math.Round(collapsedTabCenter.X), (int)Math.Round(collapsedTabCenter.Y));
                autoHideOverlay.CollapseToEdge();
                PumpFor(TimeSpan.FromMilliseconds(120));
                bool ignoredSyntheticEnter = autoHideOverlay.IsCollapsedToEdge && !autoHideOverlay.CursorOutsideForTest;
                simulatedAutoHideCursor = new System.Drawing.Point((int)work.Left + 4, (int)work.Top + 4);
                autoHideOverlay.HandlePointerLeftForTest();
                Pump(window);
                bool armedAfterRealLeave = autoHideOverlay.EdgeExpansionArmedForTest;
                simulatedAutoHideCursor = new System.Drawing.Point((int)Math.Round(collapsedTabCenter.X), (int)Math.Round(collapsedTabCenter.Y));
                autoHideOverlay.HandlePointerEnteredForTest();
                Pump(window);
                Check(ignoredSyntheticEnter && armedAfterRealLeave && !autoHideOverlay.IsCollapsedToEdge,
                    $"moving a collapsed Deck beneath a stationary pointer cannot oscillate; expansion requires a genuine leave and re-entry (ignored={ignoredSyntheticEnter}, armed={armedAfterRealLeave}, expanded={!autoHideOverlay.IsCollapsedToEdge})");
                PumpFor(TimeSpan.FromMilliseconds(650));
                bool stayedOpenDuringPointerTravel = !autoHideOverlay.IsCollapsedToEdge;
                simulatedAutoHideCursor = new System.Drawing.Point((int)Math.Round(expandedDeckCenter.X), (int)Math.Round(expandedDeckCenter.Y));
                autoHideOverlay.HandlePointerEnteredForTest();
                Pump(window);
                bool armedAfterEnteringExpandedDeck = autoHideOverlay.PointerAutoHideArmedForTest;
                simulatedAutoHideCursor = new System.Drawing.Point((int)work.Left + 4, (int)work.Top + 4);
                autoHideOverlay.HandlePointerLeftForTest();
                PumpFor(TimeSpan.FromMilliseconds(650));
                Check(stayedOpenDuringPointerTravel && armedAfterEnteringExpandedDeck && autoHideOverlay.IsCollapsedToEdge,
                    "hover-expanding from a moved edge tab stays open while the pointer travels to the restored Deck, then auto-hides only after the pointer has entered and left the expanded Deck");
                autoHideOverlay.ExpandFromEdge();
                Pump(window);
                autoHideOverlay.CollapseToEdge();
                autoHideOverlay.ExpandFromEdge();
                Pump(window);
                Check(Math.Abs(autoHideOverlay.ActualWidth - expandedAutoHideWidth) < 1 && Math.Abs(autoHideOverlay.ActualHeight - expandedAutoHideHeight) < 1,
                    "expanding the edge tab restores the Deck's exact per-layout zoom");
                autoHideOverlay.PinButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                autoHideOverlay.DeckButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                PumpFor(TimeSpan.FromMilliseconds(350));
                Check(autoHideOverlay.IsVisible && autoHideOverlay.IsPinnedForTest && savedPinned == true, "pinning keeps the Deck visible after actions and persists the choice for that layout");
                autoHideOverlay.PinButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                simulatedAutoHideCursor = new System.Drawing.Point((int)work.Left + 4, (int)work.Top + 4);
                PumpFor(TimeSpan.FromMilliseconds(80));
                autoHideOverlay.ArmPointerAutoHideForTest();
                autoHideOverlay.SetDragActiveForTest(true);
                autoHideOverlay.RequestPointerAutoHideForTest();
                PumpFor(TimeSpan.FromMilliseconds(650));
                bool stayedDuringDrag = autoHideOverlay.IsVisible;
                autoHideOverlay.SetDragActiveForTest(false);
                PumpFor(TimeSpan.FromMilliseconds(650));
                Check(stayedDuringDrag && autoHideOverlay.IsVisible && autoHideOverlay.IsCollapsedToEdge && savedPinned == false, "pointer-leave edge storage pauses throughout drag and resumes only after drag completion");
                autoHideOverlay.ExpandFromEdge();
                autoHideOverlay.RefreshAppearance(96, true, DeckAutoDismissBehavior.CollapseToEdge, DeckAutoDismissBehavior.Hide);
                simulatedAutoHideCursor = new System.Drawing.Point((int)work.Left + 4, (int)work.Top + 4);
                autoHideOverlay.ArmPointerAutoHideForTest();
                autoHideOverlay.RequestPointerAutoHideForTest();
                PumpFor(TimeSpan.FromMilliseconds(650));
                Check(!autoHideOverlay.IsVisible && !autoHideOverlay.OwnsMouseCaptureForTest,
                    "pointer-leave hide removes the Deck and its mouse capture instead of leaving an edge tab or hit-test surface");
                autoHideOverlay.PrepareForShow();
                autoHideOverlay.Show();
                autoHideOverlay.RefreshAppearance(96, true, DeckAutoDismissBehavior.Hide, DeckAutoDismissBehavior.StayVisible);
                autoHideOverlay.DeckButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                PumpFor(TimeSpan.FromMilliseconds(350));
                Check(autoHideExecuted != null && !autoHideOverlay.IsVisible && !autoHideOverlay.IsCollapsedToEdge,
                    "after-action hide runs the Deck action first and then fully hides the Deck without collapsing it");
            }
            finally
            {
                autoHideOverlay.CursorPositionProviderForTest = null;
                autoHideOverlay.Close();
            }
            var restoredCollapsedOverlay = new DeckPanelOverlayWindow(autoHideConfig, null, selectedLayout: autoHideLayout);
            try
            {
                restoredCollapsedOverlay.Show();
                Pump(window);
                restoredCollapsedOverlay.CollapseToEdge();
                Pump(window);
                Check(Math.Abs(restoredCollapsedOverlay.Left - autoHideConfig.DeckPanelCollapsedLeft!.Value) < 1
                    && Math.Abs(restoredCollapsedOverlay.Top - autoHideConfig.DeckPanelCollapsedTop!.Value) < 1,
                    "a newly constructed Deck restores the dragged collapsed position after an application restart");
            }
            finally { restoredCollapsedOverlay.Close(); }
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
                sizeChanged: (_, _, _) => throw new IOException("simulated size persistence failure"));
            Check(maximumDeckOverlay.Width <= SystemParameters.WorkArea.Width - 24 + .1 && maximumDeckOverlay.Height <= SystemParameters.WorkArea.Height - 24 + .1 && maximumDeckOverlay.MinWidth < maximumDeckOverlay.MaxWidth && maximumDeckOverlay.MinHeight < maximumDeckOverlay.MaxHeight, "an 18-by-18 Deck initially fits the work area and remains freely resizable");
            Check(maximumDeckOverlay.VideoPreviewCountForTest == 0, "an all-video 18-by-18 Deck allocates no media player before hover");
            maximumDeckOverlay.Show();
            double maximumDeckRestoreWidth = maximumDeckOverlay.ActualWidth, maximumDeckRestoreHeight = maximumDeckOverlay.ActualHeight;
            var maximumDeckWorkArea = maximumDeckOverlay.CurrentMonitorWorkAreaForTest;
            maximumDeckOverlay.ToggleSafeMaximizeForTest();
            Pump(window);
            Check(maximumDeckOverlay.IsSafelyMaximizedForTest
                && maximumDeckOverlay.WindowState == WindowState.Normal
                && Math.Abs(maximumDeckOverlay.ActualWidth - maximumDeckWorkArea.Width) < 2
                && Math.Abs(maximumDeckOverlay.ActualHeight - maximumDeckWorkArea.Height) < 2
                && maximumDeckOverlay.FullScreenButton.IsVisible
                && maximumDeckOverlay.FullScreenButton.ToolTip?.ToString() == "元の位置とサイズに戻す",
                "Deck full screen fills its current monitor work area in a safe normal window and exposes a clear restore control");
            maximumDeckOverlay.ToggleSafeMaximizeForTest();
            Pump(window);
            Check(!maximumDeckOverlay.IsSafelyMaximizedForTest && Math.Abs(maximumDeckOverlay.ActualWidth - maximumDeckRestoreWidth) < 1 && Math.Abs(maximumDeckOverlay.ActualHeight - maximumDeckRestoreHeight) < 1 && maximumDeckOverlay.FullScreenButton.ToolTip?.ToString() == "最大化" && maximumDeckOverlay.ResetSizeButton.ToolTip?.ToString() == "初期サイズに戻す" && maximumDeckOverlay.CloseButton.ToolTip?.ToString() == "Deckを非表示", "Deck maximize restores the previous overlay geometry while reset and hide keep distinct labels");
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
            // The last loop iteration intentionally shows the Deck again. Hide
            // once more before inspecting media ownership; a real pointer over
            // a button may legitimately start a new preview after Show().
            maximumDeckOverlay.HideForReuse();
            Pump(window);
            Check(maximumDeckOverlay.VideoPreviewCountForTest == 0, "each cached Deck hide releases the active hover video player");
            maximumDeckOverlay.Show();
            maximumDeckOverlay.DeckButtons[25].RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            Pump(window);
            Check(maximumDeckOverlay.VideoPreviewCountForTest == 1 && DeckPanelLayout.CachedLargeThumbnailCountForTest == 0, "an all-video 18-by-18 Deck reuses one hover player, keeps large previews transient, and survives rapid hover/open/maximize cycles");
            maximumDeckOverlay.Close();
            var touchDeckLayout = new DeckLayoutDefinition
            {
                Name = "タッチ操作",
                Columns = 3,
                Rows = 3,
                Mappings = Enumerable.Range(1, 9).Select(slot => new Mapping { Input = $"Deck+{slot:00}", Layer = "Deck", Kind = ActionKind.Key, Value = slot.ToString(), Description = slot.ToString() }).ToList()
            };
            var touchDeckOverlay = new DeckPanelOverlayWindow(new AppConfig { DeckLayouts = [touchDeckLayout] }, null, selectedLayout: touchDeckLayout);
            touchDeckOverlay.Show();
            touchDeckOverlay.UpdateLayout();
            Pump(window);
            double touchButtonWidth = RenderedWidth(touchDeckOverlay.DeckButtons[0], touchDeckOverlay);
            touchDeckOverlay.FullScreenButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            double fullScreenTouchButtonWidth = RenderedWidth(touchDeckOverlay.DeckButtons[0], touchDeckOverlay);
            Check(touchDeckOverlay.IsSafelyMaximizedForTest && fullScreenTouchButtonWidth > touchButtonWidth * 2,
                $"a touch-oriented Deck full-screen button grows the actionable button surface substantially ({touchButtonWidth:F1} -> {fullScreenTouchButtonWidth:F1})");
            CaptureForReview(touchDeckOverlay, "deck-overlay-fullscreen-touch.png");
            touchDeckOverlay.FullScreenButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            touchDeckOverlay.Close();
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
            string originalProfileName = window.CurrentProfileForTest.Name;
            var anotherProfile = window.ProfilesForTest.FirstOrDefault(x => !x.Name.Equals(originalProfileName, StringComparison.OrdinalIgnoreCase));
            if (anotherProfile == null)
            {
                anotherProfile = new Profile { Name = "Deck clipboard profile" };
                window.ApplyProfileManagerResultForTest([.. window.ProfilesForTest, anotherProfile], originalProfileName);
                window.ShowDeckLayoutListForTest();
            }
            Check(!window.ProfileBox.IsEnabled && window.ProfileBox.Opacity < .6, "the Deck list keeps the unrelated profile selector visibly disabled");
            OverlayService.Configure(() => window.ConfigForTest, positionChanged: (layoutId, left, top) =>
            {
                var target = window.ConfigForTest.DeckLayouts.FirstOrDefault(layout => layout.Id.Equals(layoutId, StringComparison.OrdinalIgnoreCase));
                if (target != null)
                {
                    target.PanelLeft = left;
                    target.PanelTop = top;
                }
            }, presentationStateChanged: window.HandleDeckOverlayPresentationChanged);
            string selectedDeckAction = DeckPanelLayout.ActionValue(standardDeck.Id);
            OverlayService.TryShow(selectedDeckAction);
            Pump(window);
            window.EditDeckLayoutForTest(standardDeck);
            Pump(window);
            Check(window.InspectorEmptyTitleText.Text == "Deckのキーを選択"
                && window.InspectorHintOneTitle.Text == "Deckボタンをクリック"
                && window.InspectorHintTwoTitle.Text == "ドラッグ＆ドロップ"
                && window.InspectorHintTwoDescription.Text == "Actionを移動・入れ替え"
                && window.InspectorHintOneIcon.Data.ToString() != mainInspectorHintIcons[0]
                && window.InspectorHintTwoIcon.Data.ToString() != mainInspectorHintIcons[1]
                && window.InspectorHintThreeIcon.Data.ToString() == mainInspectorHintIcons[2]
                && window.InspectorHintThreeTitle.Text == "右クリック",
                "the Deck editor replaces the pointer and keyboard icons with Deck-grid and move icons while retaining the shared right-click mouse icon");
            Check(OverlayService.IsDeckPanelVisible(selectedDeckAction)
                && System.Windows.Automation.AutomationProperties.GetName(window.DeckOverlayToggleButton) == "Deckを非表示"
                && window.DeckSaveStatusText.Visibility == Visibility.Collapsed
                && OverlayService.DeckPanelPresentationState(selectedDeckAction) == OverlayService.DeckPresentationState.Visible,
                $"the actual Deck preview action changes into an explicit hide action while keeping detailed state out of the visible toolbar (visible={OverlayService.IsDeckPanelVisible(selectedDeckAction)}, button={window.DeckOverlayToggleButton.Content})");
            window.DeckOverlayToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(!OverlayService.IsDeckPanelVisible(selectedDeckAction)
                && System.Windows.Automation.AutomationProperties.GetName(window.DeckOverlayToggleButton) == "Deckを表示"
                && OverlayService.DeckPanelPresentationState(selectedDeckAction) == OverlayService.DeckPresentationState.Hidden,
                "hiding the actual Deck removes the overlay and returns the preview action to an explicit show state without a redundant status label");
            window.DeckOverlayToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.ShowDeckLayoutListForTest();
            var globalDeckPanelBeforeProfileSwitch = OverlayService.DeckPanelInstanceForTest;
            if (anotherProfile != null)
            {
                window.SwitchProfileForTest(anotherProfile.Name);
                Pump(window);
                window.SwitchProfileForTest(originalProfileName);
                Pump(window);
            }
            Check(globalDeckPanelBeforeProfileSwitch != null && ReferenceEquals(globalDeckPanelBeforeProfileSwitch, OverlayService.DeckPanelInstanceForTest), $"a profile switch keeps a profile-independent Deck overlay intact without blinking or rebuilding it (before={globalDeckPanelBeforeProfileSwitch?.LayoutId ?? "null"}, after={OverlayService.DeckPanelInstanceForTest?.LayoutId ?? "null"}, linked={standardDeck.ProfileSwitchEnabled})");
            OverlayService.TryShow(DeckPanelLayout.ActionValue(extraDeck.Id));
            Pump(window);
            var coexistingPanels = OverlayService.DeckPanelInstancesForTest;
            var standardPanel = coexistingPanels.FirstOrDefault(panel => panel.LayoutId.Equals(standardDeck.Id, StringComparison.OrdinalIgnoreCase));
            var extraPanel = coexistingPanels.FirstOrDefault(panel => panel.LayoutId.Equals(extraDeck.Id, StringComparison.OrdinalIgnoreCase));
            standardPanel?.MoveAndPersistForTest(140, 180);
            extraPanel?.MoveAndPersistForTest(520, 260);
            Check(coexistingPanels.Count == 2 && coexistingPanels.All(panel => panel.IsVisible)
                && standardPanel != null && extraPanel != null
                && standardDeck.PanelLeft == 140 && standardDeck.PanelTop == 180
                && extraDeck.PanelLeft == 520 && extraDeck.PanelTop == 260,
                "different Deck overlays coexist and persist independent positions suitable for separate monitors");
            OverlayService.TryShow(DeckPanelLayout.ActionValue(standardDeck.Id));
            Pump(window);
            Check(standardPanel?.IsVisible == false && extraPanel?.IsVisible == true, "toggling one coexisting Deck never hides another Deck");
            OverlayService.TryShow(DeckPanelLayout.ActionValue(standardDeck.Id));
            Pump(window);
            foreach (var panel in OverlayService.DeckPanelInstancesForTest.ToArray())
                panel.Close();
            string runtimeProfileBeforeDeckEdit = window.AppliedProfileNameForTest;
            string[] runtimeMappingsBeforeDeckEdit = window.AppliedMappingsForTest.Select(mapping => $"{mapping.Input}\u001f{mapping.Kind}\u001f{mapping.Value}").ToArray();
            if (anotherProfile != null)
            {
                window.SwitchProfileForTest(anotherProfile.Name);
                runtimeProfileBeforeDeckEdit = window.AppliedProfileNameForTest;
                runtimeMappingsBeforeDeckEdit = window.AppliedMappingsForTest.Select(mapping => $"{mapping.Input}\u001f{mapping.Kind}\u001f{mapping.Value}").ToArray();
                window.ConfigForTest.ActiveProfile = originalProfileName;
            }
            window.EditDeckLayoutForTest(standardDeck);
            Pump(window);
            double deckNameBottom = window.DeckLayoutNameBox.TranslatePoint(new System.Windows.Point(0, window.DeckLayoutNameBox.ActualHeight), window.DeckEditorWorkspace).Y;
            Check(window.DeckProfileSwitchBox.IsChecked == false && !window.ProfileBox.IsEnabled && window.DeckLayoutNameBox.ActualWidth >= 109.5
                && window.DeckLayoutNameBox.BorderThickness == new Thickness(0)
                && window.DeckSettingsPanel.Visibility == Visibility.Collapsed
                && window.DeckPreviewPane.ActualWidth > 0 && window.DeckPreviewPane.ActualHeight > 0,
                $"a global Deck keeps profile selection disabled while the plain editable title and full preview remain readable (profileChecked={window.DeckProfileSwitchBox.IsChecked}, profileEnabled={window.ProfileBox.IsEnabled}, name={window.DeckLayoutNameBox.ActualWidth:F1}, border={window.DeckLayoutNameBox.BorderThickness}, settings={window.DeckSettingsPanel.Visibility}, preview={window.DeckPreviewPane.ActualWidth:F1}x{window.DeckPreviewPane.ActualHeight:F1})");
            window.DeckLayoutNameBox.ApplyTemplate();
            var deckTitleEditButton = (System.Windows.Controls.Button?)window.DeckLayoutNameBox.Template.FindName("EditGlyphButton", window.DeckLayoutNameBox);
            deckTitleEditButton?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            var deckTitleFocusScope = System.Windows.Input.FocusManager.GetFocusScope(window.DeckLayoutNameBox);
            Check(deckTitleEditButton != null
                && ReferenceEquals(System.Windows.Input.FocusManager.GetFocusedElement(deckTitleFocusScope), window.DeckLayoutNameBox)
                && window.DeckLayoutNameBox.SelectionLength == window.DeckLayoutNameBox.Text.Length,
                "the Deck title pencil is a real control that focuses and selects the complete Deck name");
            window.DeckBackButton.Focus();
            window.DeckCustomizeToggleButton.IsChecked = true;
            window.DeckCustomizeToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(window);
            double deckProfileTop = window.DeckProfileSwitchBox.TranslatePoint(new System.Windows.Point(), window.DeckEditorWorkspace).Y;
            double settingsLeft = window.DeckSettingsPanel.TranslatePoint(new System.Windows.Point(), window.DeckEditorWorkspace).X;
            double deckPreviewPaneLeft = window.DeckPreviewPane.TranslatePoint(new System.Windows.Point(), window.DeckEditorWorkspace).X;
            double settingsTop = window.DeckSettingsPanel.TranslatePoint(new System.Windows.Point(), window.DeckEditorWorkspace).Y;
            double deckPreviewPaneTop = window.DeckPreviewPane.TranslatePoint(new System.Windows.Point(), window.DeckEditorWorkspace).Y;
            bool panesDoNotOverlap = deckPreviewPaneLeft >= settingsLeft + window.DeckSettingsPanel.ActualWidth
                || settingsLeft >= deckPreviewPaneLeft + window.DeckPreviewPane.ActualWidth
                || deckPreviewPaneTop >= settingsTop + window.DeckSettingsPanel.ActualHeight
                || settingsTop >= deckPreviewPaneTop + window.DeckPreviewPane.ActualHeight;
            Check(deckProfileTop > deckNameBottom && panesDoNotOverlap && window.DeckSettingsPanel.Visibility == Visibility.Visible,
                $"the customization drawer opens below the title without covering the Deck preview (settings={settingsLeft:F1},{settingsTop:F1} {window.DeckSettingsPanel.ActualWidth:F1}x{window.DeckSettingsPanel.ActualHeight:F1}, preview={deckPreviewPaneLeft:F1},{deckPreviewPaneTop:F1} {window.DeckPreviewPane.ActualWidth:F1}x{window.DeckPreviewPane.ActualHeight:F1})");
            window.DeckProfileSwitchBox.IsChecked = true;
            Pump(window);
            window.SaveAndApplyForTest();
            Check(window.AppliedProfileNameForTest == runtimeProfileBeforeDeckEdit && window.AppliedMappingsForTest.Select(mapping => $"{mapping.Input}\u001f{mapping.Kind}\u001f{mapping.Value}").SequenceEqual(runtimeMappingsBeforeDeckEdit), "enabling Deck profile switching preserves the live runtime profile and every layer mapping while the editor is showing another profile");
            string linkedGroup = standardDeck.ProfileGroupId;
            var linkedVariants = window.ConfigForTest.DeckLayouts.Where(layout => layout.ProfileSwitchEnabled && layout.ProfileGroupId.Equals(linkedGroup, StringComparison.OrdinalIgnoreCase)).ToList();
            Check(window.ProfileBox.IsEnabled && window.ProfileBox.Opacity == 1 && linkedVariants.Count == window.ProfilesForTest.Count && linkedVariants.All(layout => layout.Columns == standardDeck.Columns && layout.Rows == standardDeck.Rows) && linkedVariants.Where(layout => !ReferenceEquals(layout, standardDeck)).All(layout => layout.Mappings.Count == 0), "enabling one Deck creates an independent same-shaped blank Deck for every other profile and restores profile selection");
            if (anotherProfile != null)
            {
                window.MultiSelectToggle.IsChecked = false;
                window.MultiSelectToggle.IsChecked = true;
                window.DeckManagementButtonsForTest[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                window.DeckManagementButtonsForTest[1].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                window.MultiCopyButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                window.SwitchProfileForTest(anotherProfile.Name);
                Pump(window);
                bool selectedBlankVariant = window.SelectedDeckLayoutForTest is { ProfileSwitchEnabled: true, Mappings.Count: 0 } selectedVariant
                    && selectedVariant.ProfileGroupId == linkedGroup
                    && selectedVariant.ProfileId == anotherProfile.Id
                    && DeckPanelLayout.DefaultLayout(window.ConfigForTest)?.Id == selectedVariant.Id;
                bool selectionSurvived = window.MultiSelectToggle.IsChecked == true
                    && window.MultiSelectedInputsForTest.Order().SequenceEqual(new[] { "Deck+01", "Deck+02" })
                    && window.MultiPasteButton.IsEnabled;
                window.MultiPasteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump(window);
                bool pastedAcrossProfile = window.SelectedDeckLayoutForTest!.Mappings.Any(mapping => mapping.Input == "Deck+01" && mapping.Value == "Ctrl+C")
                    && window.SelectedDeckLayoutForTest.Mappings.Any(mapping => mapping.Input == "Deck+02" && mapping.DeckFilePath == deckPreviewImage);
                Check(selectedBlankVariant && selectionSurvived && pastedAcrossProfile, "Deck multi-selection and its copied assignments survive a profile switch and paste into that profile's linked Deck");
                window.SwitchProfileForTest(originalProfileName);
            }
            else
                Check(true, "Deck multi-selection profile-switch regression is not applicable with a single configured profile");
            var bulkDeck = window.SelectedDeckLayoutForTest!;
            var bulkDeckOriginalMappings = bulkDeck.Mappings.Where(mapping => DeckPanelLayout.SlotNumber(mapping.Input) is >= 1 and <= 4).ToList();
            bulkDeck.Mappings.RemoveAll(mapping => DeckPanelLayout.SlotNumber(mapping.Input) is >= 1 and <= 4);
            bulkDeck.Mappings.Add(new Mapping { Input = DeckPanelLayout.InputName(1), Layer = DeckPanelLayout.Layer, Kind = ActionKind.Text, Value = "Deck一括1" });
            bulkDeck.Mappings.Add(new Mapping { Input = DeckPanelLayout.InputName(2), Layer = DeckPanelLayout.Layer, Kind = ActionKind.Shortcut, Value = "Ctrl+Shift+2" });
            window.ColorButtonsForTest();
            window.MultiSelectToggle.IsChecked = true;
            window.DeckManagementButtonsForTest[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.DeckManagementButtonsForTest[1].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.DeckManagementButtonsForTest[0].ApplyTemplate();
            var deckMultiTint = (UIElement)window.DeckManagementButtonsForTest[0].Template.FindName("SelectionTint", window.DeckManagementButtonsForTest[0])!;
            var deckMultiBadge = (UIElement)window.DeckManagementButtonsForTest[0].Template.FindName("MultiSelectBadge", window.DeckManagementButtonsForTest[0])!;
            Check(window.DeckManagementButtonsForTest[0].Opacity == 1 && window.DeckManagementButtonsForTest[1].Opacity == 1
                && Math.Abs(window.DeckManagementButtonsForTest[2].Opacity - MainWindow.SelectionDimOpacity) < .01
                && window.DeckManagementButtonsForTest[0].BorderBrush is SolidColorBrush deckMultiBorder && deckMultiBorder.Color == ThemeService.Color("AccentBrush")
                && window.DeckManagementButtonsForTest[0].BorderThickness == new Thickness(2) && deckMultiTint.Opacity == 0 && deckMultiBadge.Opacity == 0
                && !MainWindow.HasSelectionPulseAnimationForTest(window.DeckManagementButtonsForTest[0]),
                "Deck multi-selection keeps selected buttons bright with the shared selection outline and dims every unselected button without badges");
            CaptureForReview(window, "deck-multi-selection.png");
            window.MultiCopyButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.DeckManagementButtonsForTest[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.DeckManagementButtonsForTest[1].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.DeckManagementButtonsForTest[2].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.DeckManagementButtonsForTest[3].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.MultiPasteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(bulkDeck.Mappings.LastOrDefault(mapping => mapping.Input == DeckPanelLayout.InputName(3)) is { Kind: ActionKind.Text, Value: "Deck一括1" }
                && bulkDeck.Mappings.LastOrDefault(mapping => mapping.Input == DeckPanelLayout.InputName(4)) is { Kind: ActionKind.Shortcut, Value: "Ctrl+Shift+2" }
                && window.MultiSelectToggle.IsChecked == false,
                "Deck top toolbar copies and pastes multiple selected button assignments in slot order");
            window.MultiSelectToggle.IsChecked = true;
            window.DeckManagementButtonsForTest[2].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.DeckManagementButtonsForTest[3].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.MultiDeleteButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(!bulkDeck.Mappings.Any(mapping => mapping.Input is "Deck+03" or "Deck+04")
                && MainWindow.DeckSelectionRange(5, 2).SequenceEqual(["Deck+02", "Deck+03", "Deck+04", "Deck+05"]),
                "Deck top toolbar deletes a multi-selection and Shift-range selection covers every slot between anchor and target");
            bulkDeck.Mappings.RemoveAll(mapping => DeckPanelLayout.SlotNumber(mapping.Input) is >= 1 and <= 4);
            bulkDeck.Mappings.AddRange(bulkDeckOriginalMappings);
            window.ColorButtonsForTest();
            Check(MainWindow.TryResolveDeckLayoutSize("custom", "18", "18", out int dialogColumns, out int dialogRows) && dialogColumns == 18 && dialogRows == 18 && !MainWindow.TryResolveDeckLayoutSize("custom", "19", "5", out _, out _) && window.DeckSizePresetBox.Style == window.FindResource("ToolbarComboBoxStyle") && Math.Abs(window.DeckSizePresetBox.Height - 36) < .1, "Deck creation supports themed preset and custom 1x1 through 18x18 sizes");
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
            OverlayService.ResetDeckRefreshRequestCountForTest();
            const string largeDeckTyping = "Ctrl+Shift+K";
            foreach (char character in largeDeckTyping)
                window.ValueBox.AppendText(character.ToString());
            Check(window.DeckVisualUpdateCountForTest == largeDeckTyping.Length && OverlayService.DeckRefreshRequestCountForTest == 0, $"18x18 Deck typing updates only the selected button and never rebuilds the 324-button overlay (buttonUpdates={window.DeckVisualUpdateCountForTest}, overlayRefreshes={OverlayService.DeckRefreshRequestCountForTest})");
            window.SaveAndApplyForTest();
            Pump(window);
            Check(OverlayService.DeckRefreshRequestCountForTest == 1, "confirming a Deck shortcut refreshes the visible overlay exactly once without restarting RELYR");
            window.ValueBox.Text = originalLargeDeckValue;
            window.ApplyDeckSizeForTest(9, 5);
            window.DeckCustomizeToggleButton.IsChecked = false;
            window.DeckCustomizeToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(window);
            var deckEditorBody = (Grid)window.DeckSettingsPanel.Parent;
            Check(window.DeckBackButton.Content is TextBlock
                && window.DeckBackButton.ToolTip?.ToString() == "Deck一覧へ戻る"
                && Math.Abs(window.DeckBackButton.ActualHeight - window.DeckLayoutNameBox.ActualHeight) < .1
                && Math.Abs(window.DeckLayoutNameBox.ActualHeight - 44) < .1
                && window.DeckLayoutNameBox.BorderThickness == new Thickness(0)
                && Math.Abs(window.DeckAfterActionBehaviorBox.Height - 36) < .1
                && Math.Abs(window.DeckPointerLeaveBehaviorBox.Height - 36) < .1
                && Math.Abs(window.DeckOverlayToggleButton.ActualHeight - 36) < .1
                && window.DeckSaveButton.Visibility == Visibility.Collapsed
                && window.ToolbarSaveButton.Visibility == Visibility.Visible
                && window.DeckSettingsPanel.Visibility == Visibility.Collapsed
                && window.DeckPreviewSurface.BorderThickness == new Thickness(0)
                && window.DeckPreviewSurface.CornerRadius == new CornerRadius(0)
                && window.DeckPreviewSurface.Background is SolidColorBrush previewSurface && previewSurface.Color == Colors.Transparent
                && deckEditorBody.ColumnDefinitions.Count == 3
                && deckEditorBody.RowDefinitions.Count == 3
                && deckEditorBody.ColumnDefinitions[0].Width.IsStar
                && Grid.GetColumn(window.DeckPreviewPane) == 0,
                $"Deck editor keeps one stable command bar, a plain title, the global save action, and a full-width default preview (back={window.DeckBackButton.ActualHeight:F1}, name={window.DeckLayoutNameBox.ActualHeight:F1}/{window.DeckLayoutNameBox.BorderThickness}, behavior={window.DeckAfterActionBehaviorBox.ActualHeight:F1}/{window.DeckPointerLeaveBehaviorBox.ActualHeight:F1}, overlay={window.DeckOverlayToggleButton.ActualHeight:F1}, localSave={window.DeckSaveButton.Visibility}, globalSave={window.ToolbarSaveButton.Visibility}, settings={window.DeckSettingsPanel.Visibility}, columns={deckEditorBody.ColumnDefinitions.Count}:{deckEditorBody.ColumnDefinitions[0].Width}, previewColumn={Grid.GetColumn(window.DeckPreviewPane)})");
            window.DeckCustomizeToggleButton.IsChecked = true;
            window.DeckCustomizeToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(window);
            bool drawerAtSide = window.DeckSettingsPanel.Visibility == Visibility.Visible
                && Grid.GetColumn(window.DeckSettingsPanel) == 2
                && deckEditorBody.ColumnDefinitions[2].Width.IsAbsolute
                && deckEditorBody.ColumnDefinitions[2].Width.Value is >= 282 and <= 320
                && Grid.GetColumn(window.DeckPreviewPane) == 0;
            bool drawerStacked = window.DeckSettingsPanel.Visibility == Visibility.Visible
                && Grid.GetRow(window.DeckSettingsPanel) == 0
                && Grid.GetRow(window.DeckPreviewPane) == 2
                && deckEditorBody.RowDefinitions[2].Height.IsStar;
            Check(drawerAtSide || drawerStacked, "Deck customization uses a bounded side drawer or a stacked drawer at compact widths without overlap");
            var deckSettingSections = new[] { window.DeckCoreSettingsCard, window.DeckLayoutSettingsCard, window.DeckAppearanceSettingsCard, window.DeckAutoHideSettingsCard };
            Check(deckSettingSections.All(section => section.BorderThickness == new Thickness(0))
                && deckSettingSections.All(section => section.Padding == new Thickness(0))
                && window.DeckSettingsPanel.BorderThickness == new Thickness(1)
                && Descendants<System.Windows.Controls.Primitives.ToggleButton>(window.DeckSettingsPanel).Select(tab => tab.Content?.ToString()).Count(text => text is "レイアウト" or "見た目" or "動作") == 3,
                "Deck settings use one intentional drawer with three progressive-disclosure tabs instead of repetitive section frames");
            Check(window.DeckLayoutSettingsCard.Visibility == Visibility.Visible
                && window.DeckCustomSizePanel.Visibility == Visibility.Visible
                && window.DeckColumnsSlider.Maximum == 18
                && window.DeckRowsSlider.Maximum == 18
                && window.DeckPanelPaddingSlider.Maximum == 24
                && window.DeckPanelCornerRadiusSlider.Maximum == 24
                && window.DeckCustomizationResetButton.Visibility == Visibility.Visible,
                "the layout tab exposes compact size, rows, columns, spacing, corner radius, and reset controls from the reference design");
            window.DeckAppearanceCustomizeTab.IsChecked = true;
            window.DeckAppearanceCustomizeTab.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(window);
            Check(window.DeckAppearanceSettingsCard.Visibility == Visibility.Visible
                && window.DeckLayoutSettingsCard.Visibility == Visibility.Collapsed
                && window.DeckPanelColorControls.Visibility == Visibility.Visible
                && window.DeckHoverAnimationBox.Content?.ToString() == "ホバー時アニメーション"
                && window.DeckHoverPreviewBox.Content?.ToString() == "ファイルをホバー再生",
                "the appearance tab exposes background, opacity, hover animation, and file-preview controls without an extra settings frame");
            CaptureForReview(window, "deck-editor-customize-appearance.png");
            window.DeckBehaviorCustomizeTab.IsChecked = true;
            window.DeckBehaviorCustomizeTab.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(window);
            Check(window.DeckAutoHideSettingsCard.Visibility == Visibility.Visible
                && window.DeckAppearanceSettingsCard.Visibility == Visibility.Collapsed
                && Descendants<TextBlock>(window.DeckAfterActionBehaviorGroup).Any(text => text.Text == "実行後")
                && Descendants<TextBlock>(window.DeckPointerLeaveBehaviorGroup).Any(text => text.Text == "マウスが離れた後"),
                "the behavior tab keeps post-action and pointer-leave collapse or hide choices separate and explicit");
            CaptureForReview(window, "deck-editor-customize-behavior.png");
            window.DeckCustomizeCloseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.ProfileBox.TranslatePoint(new System.Windows.Point(), window).X < window.KeyboardLayoutBox.TranslatePoint(new System.Windows.Point(), window).X, "profile context precedes the less-frequently changed keyboard layout");
            double keyboardCenter = window.KeyboardSurfaceCard.TranslatePoint(new System.Windows.Point(window.KeyboardSurfaceCard.ActualWidth / 2, 0), window.WorkspaceGrid).X;
            double workspaceCenter = window.WorkspaceGrid.ActualWidth / 2;
            Check(Math.Abs(keyboardCenter - workspaceCenter) <= 1.1, $"the main keyboard is centered in the middle workspace (keyboard={keyboardCenter:F1}, workspace={workspaceCenter:F1})");
            Check(ReferenceEquals(window.LayerNavigationPane.Parent, window.ShellDock) && window.ShellDock.Children.IndexOf(window.LayerNavigationPane) == 0 && Math.Abs(window.HeaderBrandColumn.Width.Value) < .1 && window.ToolbarSaveButton.TranslatePoint(new System.Windows.Point(window.ToolbarSaveButton.ActualWidth, 0), window.ToolbarPanel).X <= window.ToolbarPanel.ActualWidth + 1, "the sidebar spans from the client top while the toolbar begins to its right and stays on one line");
            Check(window.ToolbarPanel.Parent is Grid && !Descendants<ScrollViewer>(window.ToolbarPanel).Any(), "compact toolbar needs neither wrapping nor a horizontal slider");
            Check(window.NewProfileButton.Content?.ToString() == "＋" && Math.Abs(window.NewProfileButton.Width - 44) < .1 && Math.Abs(window.NewProfileButton.ActualWidth - window.NewProfileButton.ActualHeight) < .1, "new profile uses the larger exact square plus-only button");
            Check(!ReferenceEquals(window.EngineToggle.Parent, window.LeftBottomActions) && ReferenceEquals(window.MacroManagerButton.Parent, window.LeftBottomActions) && ReferenceEquals(window.ProfileManagerButton.Parent, window.LeftBottomActions) && ReferenceEquals(window.GestureManagerButton.Parent, window.LeftBottomActions) && ReferenceEquals(window.DeckPanelManagerButton.Parent, window.LeftBottomActions) && ReferenceEquals(window.AppSettingsButton.Parent, window.LeftBottomActions) && !window.ToolbarPanel.Children.Contains(window.EngineToggle) && !window.ToolbarPanel.Children.Contains(window.AutoSaveToggle), "macro and management buttons are fixed at lower left while engine and auto-save move to the status bar");
            var visibleText = Descendants<TextBlock>(window).Where(x => x.IsVisible).Select(x => x.Text).ToList();
            double brandLeft = window.ProductNameText.TranslatePoint(new System.Windows.Point(), window).X;
            Check(window.ProductNameText.Text == "RELYR" && window.ProductNameText.IsVisible && window.ProductNameText.HorizontalAlignment == System.Windows.HorizontalAlignment.Left && window.ProductNameText.TextAlignment == TextAlignment.Left && Math.Abs(window.ProductNameText.FontSize - 25) < .1 && Math.Abs(brandLeft - 22) < .1 && !visibleText.Any(x => x is "INPUT CUSTOMIZER" or "中央のキーまたはマウスを選び、右側で動作を設定します。" or "キーを選択して割り当て" or "緊急停止" or "Ctrl + Alt + Shift + F12") && !visibleText.Any(x => x.StartsWith("レイヤー・場所を選択：")), $"RELYR uses a clear sidebar brand and omits redundant assignment instructions ({brandLeft:F1}px)");
            Check(window.KeyboardViewbox.ActualWidth > 0 && window.KeyboardViewbox.ActualHeight > 0
                && window.KeyboardViewbox.ActualWidth <= window.WorkspaceGrid.ActualWidth - window.KeyboardSurfaceCard.Padding.Left - window.KeyboardSurfaceCard.Padding.Right + 1
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
            Check(materialKeyBorder.Effect == null && hasCheapKeyDepth && materialKeyBorder.CornerRadius.TopLeft == 8 && !Descendants<Border>(materialKey).Any(x => x.Background is LinearGradientBrush) && window.MouseBody.Background is SolidColorBrush mouseBodyBrush && mouseBodyBrush.Color.A == 0 && window.MouseBody.Effect == null, "buttons use flat solid faces and shared eight-pixel corners while the mouse body remains flat and transparent");
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
            Check(window.NormalLayerButton.ActualHeight is >= 48 and <= 54 && window.SpaceLayerButton.ActualHeight is >= 48 and <= 54, "layer cards retain compact readable titles");
            window.Width = 1850;
            window.Height = 1000;
            window.UpdateLayout();
            Pump(window);
            Check(Math.Abs(window.ToolbarSaveButton.ActualWidth - 96) < .1
                && new System.Windows.Controls.Control[] { window.MultiSelectToggle, window.MultiCopyButton, window.MultiPasteButton, window.MultiDeleteButton, window.LightThemeToggle, window.DarkThemeToggle }.All(control => Math.Abs(control.ActualWidth - 44) < .1)
                && Math.Abs(window.NewProfileButton.ActualWidth - 44) < .1,
                "wide layouts restore the full reference toolbar button and save dimensions");
            var layerButtons = window.LayerButtonsPanel.Children.OfType<System.Windows.Controls.Button>().ToList();
            Check(ReferenceEquals(window.LayerButtonsPanel.Parent, window.LayerNavigationHost) && layerButtons.Select(x => Math.Round(x.TranslatePoint(new System.Windows.Point(), window.LayerButtonsPanel).Y)).Distinct().Count() == 7, "layer buttons stay vertically arranged in the left pane");
            Check(window.LayerButtonsPanel.Children.IndexOf(window.KeyboardLayerCategory) < window.LayerButtonsPanel.Children.IndexOf(window.NormalLayerButton) && window.LayerButtonsPanel.Children.IndexOf(window.MouseLayerCategory) < window.LayerButtonsPanel.Children.IndexOf(window.RightMouseLayerButton) && window.LayerButtonsPanel.Children.IndexOf(window.WindowsLayerCategory) < window.LayerButtonsPanel.Children.IndexOf(window.TaskbarLayerButton), "layer buttons are grouped into keyboard, mouse and Windows categories");
            Check(layerButtons.All(x => x.HorizontalContentAlignment == System.Windows.HorizontalAlignment.Stretch && x.Content is Grid && Descendants<Border>(x).Any(border => border.Style == window.FindResource("LayerIconFrame")) && Descendants<TextBlock>(x).Count() == 1 && Descendants<TextBlock>(x).Single().VerticalAlignment == VerticalAlignment.Center) && Descendants<TextBlock>(window.NormalLayerButton).Any(x => x.Text == "デフォルト") && Descendants<TextBlock>(window.SpaceLayerButton).Any(x => x.Text == "Space") && Descendants<TextBlock>(window.CapsLockLayerButton).Any(x => x.Text == "CapsLock") && Descendants<TextBlock>(window.RightMouseLayerButton).Any(x => x.Text == "右クリック") && Descendants<TextBlock>(window.ForwardMouseLayerButton).Any(x => x.Text == "進む") && Descendants<TextBlock>(window.BackMouseLayerButton).Any(x => x.Text == "戻る") && Descendants<TextBlock>(window.TaskbarLayerButton).Any(x => x.Text == "タスクバー") && window.KeyboardLayerCategory.Text == "KEY LAYER" && window.MouseLayerCategory.Text == "MOUSE LAYER" && window.WindowsLayerCategory.Text == "SYSTEM" && Descendants<System.Windows.Shapes.Ellipse>(window.NormalLayerButton).Any(x => Equals(x.Tag, "LayerActiveIndicator") && x.Visibility == Visibility.Visible), "layer cards show one vertically centered title while every active status dot uses the same fixed right column");
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
            double wideCenterWorkspaceWidth = window.ActualWidth - window.LayerNavigationPane.ActualWidth - window.AssignmentPaneColumn.ActualWidth;
            Check(Math.Abs(window.LayerNavigationPane.ActualWidth - 224) < .1
                && Math.Abs(window.AssignmentPaneColumn.ActualWidth - 272) < .1
                && window.LayerNavigationPane.ActualWidth < window.AssignmentPaneColumn.ActualWidth
                && Math.Abs(window.LayerNavigationColumn.ActualWidth) < .1
                && wideCenterWorkspaceWidth > window.ActualWidth - 512,
                $"wide navigation uses only its label width while the inspector retains its content width ({window.LayerNavigationPane.ActualWidth:F1}/{window.AssignmentPaneColumn.ActualWidth:F1}, center={wideCenterWorkspaceWidth:F1})");
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
            double wideKeyboardLeft = window.KeyboardSurfaceCard.TranslatePoint(new System.Windows.Point(), window.WorkspaceGrid).X;
            double wideKeyboardRight = window.KeyboardSurfaceCard.TranslatePoint(new System.Windows.Point(window.KeyboardSurfaceCard.ActualWidth, 0), window.WorkspaceGrid).X;
            Check(Math.Abs((wideKeyboardLeft + wideKeyboardRight) / 2 - window.WorkspaceGrid.ActualWidth / 2) < 1
                && wideKeyboardLeft >= -1 && wideKeyboardRight <= window.WorkspaceGrid.ActualWidth + 1
                && RenderedWidth(maximizedMainKey, window) > 52,
                $"wide layouts enlarge and center the complete keyboard without clipping ({wideKeyboardLeft:F1}..{wideKeyboardRight:F1} of {window.WorkspaceGrid.ActualWidth:F1}, key={RenderedWidth(maximizedMainKey, window):F1})");
            Check(RenderedWidth(maximizedMainKey, window) <= RenderedWidth(maximizedNumpadKey, window) * 1.35, "main and lower keyboard controls stay visually balanced when maximized");
            var renderedMouseWidth = RenderedWidth(window.MousePanel, window);
            var renderedMainAHeight = RenderedHeight(maximizedMainKey, window);
            var ordinaryMouseKey = Descendants<System.Windows.Controls.Button>(window.MousePanel).First(x => Equals(x.Tag, "MouseMiddle"));
            Check(Math.Abs(RenderedHeight(ordinaryMouseKey, window) - renderedMainAHeight) < 1 && renderedMouseWidth <= window.MouseColumn.ActualWidth - 16, $"mouse controls match the main A-key height and never overpower their column ({RenderedHeight(ordinaryMouseKey, window):F1}/{renderedMainAHeight:F1}, {renderedMouseWidth:F1}/{window.MouseColumn.ActualWidth:F1})");
            Check(Math.Abs(window.LowerInputGrid.ActualWidth - window.KeyboardSurfaceCard.ActualWidth) < 1 && window.MouseHost.TranslatePoint(new System.Windows.Point(window.MouseHost.ActualWidth, 0), window.WorkspaceGrid).X <= window.KeyboardSurfaceCard.TranslatePoint(new System.Windows.Point(window.KeyboardSurfaceCard.ActualWidth, 0), window.WorkspaceGrid).X + 1, "mouse stays inside the keyboard workspace right edge");
            window.Width = 1160;
            window.Height = 1250;
            window.UpdateLayout();
            Pump(window);
            double portraitCenterWorkspaceWidth = window.ActualWidth - window.LayerNavigationPane.ActualWidth - window.AssignmentPaneColumn.ActualWidth;
            Check(Math.Abs(window.LayerNavigationPane.ActualWidth - 216) < .1
                && Math.Abs(window.AssignmentPaneColumn.ActualWidth - 252) < .1
                && portraitCenterWorkspaceWidth > window.ActualWidth - 480,
                $"standard navigation stays narrower than the inspector and widens the center ({window.LayerNavigationPane.ActualWidth:F1}/{window.AssignmentPaneColumn.ActualWidth:F1}, center={portraitCenterWorkspaceWidth:F1})");
            var portraitNumpadFrame = window.SecondaryKeyboardPanel.Children.OfType<Border>().First(x => Equals(x.Tag, "テンキー"));
            var portraitNumpadBounds = portraitNumpadFrame.TransformToAncestor(window).TransformBounds(new Rect(0, 0, portraitNumpadFrame.ActualWidth, portraitNumpadFrame.ActualHeight));
            double portraitLowerTop = window.LowerInputGrid.TranslatePoint(new System.Windows.Point(), window).Y, portraitKeyboardBottom = window.KeyboardSurfaceCard.TranslatePoint(new System.Windows.Point(0, window.KeyboardSurfaceCard.ActualHeight), window).Y, portraitMouseTop = window.MouseFrame.TranslatePoint(new System.Windows.Point(), window).Y;
            Check(Math.Abs(portraitLowerTop - portraitKeyboardBottom - 16) < 1 && portraitMouseTop >= portraitNumpadBounds.Top && window.MouseFrame.TranslatePoint(new System.Windows.Point(0, window.MouseFrame.ActualHeight), window.LowerInputGrid).Y <= window.LowerInputGrid.ActualHeight + 1, "portrait layout uses whitespace to separate the main and lower controls while keeping the complete mouse visible");
            CaptureForReview(window, "portrait-main.png");
            window.Width = 800;
            window.Height = 620;
            window.UpdateLayout();
            Pump(window);
            double compactCenterWorkspaceWidth = window.ActualWidth - window.LayerNavigationPane.ActualWidth - window.AssignmentPaneColumn.ActualWidth;
            Check(Math.Abs(window.LayerNavigationPane.ActualWidth - 208) < .1
                && Math.Abs(window.AssignmentPaneColumn.ActualWidth - 232) < .1
                && compactCenterWorkspaceWidth > window.ActualWidth - 452,
                $"minimum navigation stays narrower than the inspector and preserves more central workspace ({window.LayerNavigationPane.ActualWidth:F1}/{window.AssignmentPaneColumn.ActualWidth:F1}, center={compactCenterWorkspaceWidth:F1})");
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
            Check(secondaryKeys.Where(x => !spanningNumpadTags.Contains(x.Tag?.ToString())).All(x => Math.Abs(RenderedBaseWidth(x, window) - RenderedBaseWidth(baseA, window)) < .1 && Math.Abs(RenderedBaseHeight(x, window) - RenderedBaseHeight(baseA, window)) < .1), $"every ordinary lower keyboard button has exactly the same on-screen base size as the A key, independently of the intentional hover pop (A={RenderedBaseWidth(baseA, window):F2}x{RenderedBaseHeight(baseA, window):F2}, lower={RenderedBaseWidth(renderedSecondaryKey, window):F2}x{RenderedBaseHeight(renderedSecondaryKey, window):F2}, view={window.SecondaryKeyboardViewbox.ActualWidth:F2}x{window.SecondaryKeyboardViewbox.ActualHeight:F2})");
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
            Check(up.Background is SolidColorBrush editingArrowBrush && editingArrowBrush.Color == MainWindow.AssignmentColorFor(new Mapping { Kind = ActionKind.Shortcut, Value = "Enter" })
                && MainWindow.GetIsSelectionPulseActive(up) && !MainWindow.HasSelectionPulseAnimationForTest(up) && up.Opacity == 1
                && Math.Abs(left.Opacity - MainWindow.SelectionDimOpacity) < .01
                && up.BorderBrush is SolidColorBrush selectedArrowBorder && selectedArrowBorder.Color == ThemeService.Color("AccentBrush") && up.BorderThickness == new Thickness(2),
                "a selected key retains its full action face and the shared selection outline while surrounding keys dim");
            window.CompleteDestinationInputForTest();
            Pump(window);
            Check(up.Background is SolidColorBrush assignedArrowBrush && assignedArrowBrush.Color.G > assignedArrowBrush.Color.R * 2 && !window.IsEditingSelectedInputForTest && !MainWindow.GetIsSelectionPulseActive(up) && !MainWindow.HasSelectionPulseAnimationForTest(up), "input completion retains the assigned-action color and stops the selection pulse");
            Check(window.CurrentProfileForTest.Mappings.Any(x => x.Input == "Up" && x.Value == "Enter") && window.AppliedMappingForTest("Up") == null && window.LastInput.Text.Contains("未保存"), "input completion keeps edits pending when auto-save is off");
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(ReferenceEquals(window.MouseHost.Child, window.MousePanel), "mouse diagram is placed to the right of the lower keyboard block");
            Check(layerButtons.All(button => Descendants<TextBlock>(button).Count() == 1), "layer cards contain no secondary instruction text");
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
            Check(mouseButtons.All(button => button.BorderThickness == new Thickness(1) && !Descendants<Border>(button).Any(border => border.Background is LinearGradientBrush)) && window.MouseBody.Background is SolidColorBrush flatMouseBody && flatMouseBody.Color.A == 0, "mouse buttons use visible flat borders inside a transparent body surface");
            var tiltButtons = mouseButtons.Where(x => Equals(x.Tag, "TiltLeft") || Equals(x.Tag, "TiltRight")).ToList();
            var tiltText = Descendants<TextBlock>(window.MousePanel).FirstOrDefault(x => x.Text == "チルト");
            Check(tiltButtons.Count == 2 && tiltText != null && Math.Abs(Canvas.GetLeft(tiltText) + tiltText.Width / 2 - window.MousePanel.Width / 2) < .1 && tiltText.TextAlignment == TextAlignment.Center, "mouse tilt label is precisely centered");
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
            var mainA = window.KeyboardPanel.Children.OfType<System.Windows.Controls.Button>().First(x => Equals(x.Tag, "A"));
            Check(Math.Abs(leftClick.Width - mainA.Width) < .1 && Math.Abs(rightClick.Width - mainA.Width) < .1 && Math.Abs(leftClick.Height - (mainA.Height * 3 + 8)) < .1 && Math.Abs(rightClick.Height - (mainA.Height * 3 + 8)) < .1 && wheelControls.All(x => !Bounds(x).IntersectsWith(Bounds(leftClick)) && !Bounds(x).IntersectsWith(Bounds(rightClick))), "left and right click extend through all three wheel controls and do not overlap them");
            Check(mouseButtons.Sum(x => x.Content?.ToString()?.Length ?? 0) <= 24, "mouse diagram uses concise labels");
            Check(window.Icon != null && window.Icon.Width > 0 && window.Icon.Height > 0, "main window explicitly uses the normal RELYR application icon instead of inheriting a macro-shortcut icon");
            CaptureForReview(window, "mouse-layout-main.png");
            Check(window.AssignmentPane.Visibility == Visibility.Visible && Grid.GetColumn(window.AssignmentPane) == 2 && Math.Abs(window.AssignmentPaneTransform.X) < .1, "assignment pane is always visible in its own column");
            var keys = window.KeyboardPanel.Children.OfType<System.Windows.Controls.Button>().ToList();
            foreach (var keyButton in keys)
                keyButton.ApplyTemplate();
            Check(keys.All(button => !Descendants<Border>(button).Any(border => Math.Abs(border.Height - 1) < .1)), "main keyboard buttons omit the decorative top highlight line");
            var space = keys.First(x => Equals(x.Tag, "Space"));
            Check(Math.Abs(space.Opacity - .48) < .01 && space.Background is SolidColorBrush idleSpaceBrush && idleSpaceBrush.Color == ThemeService.Color("ReservedKeyBackground"), "Space key keeps the existing reserved face on the normal layer when nothing is selected");
            space.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.UpdateLayout();
            Check(window.AssignmentPane.Visibility == Visibility.Visible && window.LastInput.Text.Contains("変更できません"), "reserved Space click shows inline warning without hiding the assignment pane");
            var capsSource = keys.First(x => Equals(x.Tag, "CapsLock"));
            Check(Math.Abs(capsSource.Opacity - .48) < .01 && capsSource.Background is SolidColorBrush idleCapsBrush && idleCapsBrush.Color == ThemeService.Color("ReservedKeyBackground"), "CapsLock keeps the existing reserved face while choosing the source key");
            capsSource.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.InputDisplayText.Text == "キーを選択してください", "reserved CapsLock cannot be selected as a source key");
            var qForCaps = keys.First(x => Equals(x.Tag, "Q"));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals("Q", StringComparison.OrdinalIgnoreCase));
            qForCaps.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(Math.Abs(capsSource.Opacity - MainWindow.SelectionDimOpacity) < .01
                && Math.Abs(space.Opacity - MainWindow.SelectionDimOpacity) < .01
                && Math.Abs(leftClick.Opacity - MainWindow.SelectionDimOpacity) < .01
                && new[] { capsSource, space, leftClick }.All(button => button.Background is SolidColorBrush reservedSelectionBrush && reservedSelectionBrush.Color == ThemeService.Color("KeyBackground"))
                && qForCaps.BorderBrush is SolidColorBrush qSelectionBorder && qSelectionBorder.Color == ThemeService.Color("AccentBrush") && qForCaps.BorderThickness == new Thickness(2),
                "a single selection uses the shared outline while reserved CapsLock, Space, and normal-layer left click match every other dimmed key face");
            CaptureForReview(window, "main-reserved-selection-dim.png");
            window.KindBox.SelectedValue = ActionKind.Key;
            Pump(window);
            window.ValueBox.Text = "Left";
            window.ValueBox.Focus();
            System.Windows.Input.Keyboard.Focus(window.ValueBox);
            Check(window.DestinationClearButton.Visibility == Visibility.Visible && window.DestinationClearButton.Content?.ToString() == "削除" && window.DestinationConfirmButton.Visibility == Visibility.Visible && window.DestinationConfirmButton.Content?.ToString() == "確定" && window.DestinationClearButton.TranslatePoint(new System.Windows.Point(), window).X < window.DestinationConfirmButton.TranslatePoint(new System.Windows.Point(), window).X, "direct execution editing shows delete immediately to the left of confirmation");
            window.ValueBox.CaretIndex = 2;
            string executionTextBeforePreview = window.ValueBox.Text;
            int executionCaretBeforePreview = window.ValueBox.CaretIndex;
            var previewSource = PresentationSource.FromVisual(window) ?? throw new InvalidOperationException("main-window presentation source is unavailable");
            var previewLeft = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, previewSource, Environment.TickCount, System.Windows.Input.Key.Left)
            {
                RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent
            };
            window.ValueBox.RaiseEvent(previewLeft);
            Check(!previewLeft.Handled && window.ValueBox.Text == executionTextBeforePreview && window.ValueBox.CaretIndex == executionCaretBeforePreview, "the Action content field is a normal TextBox and does not intercept PreviewKeyDown navigation before runtime mappings can reach it");
            window.DestinationClearButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.ValueBox.Text == "" && window.ValueBox.IsKeyboardFocusWithin && window.DestinationClearButton.Visibility == Visibility.Visible && window.DestinationConfirmButton.Visibility == Visibility.Visible, "delete clears only the current execution content and keeps it ready for re-entry");
            window.KindBox.SelectedIndex = -1;
            window.ValueBox.Text = "Enter";
            window.ValueBox.Focus();
            System.Windows.Input.Keyboard.Focus(window.ValueBox);
            Pump(window);
            Check(window.ValueBox.Text == "Enter" && Equals(window.KindBox.SelectedValue, ActionKind.Key), "directly entered key names remain available without a physical-key PreviewKeyDown capture mode");
            window.WorkspaceGrid.RaiseEvent(BlankClick());
            Pump(window);
            Check(window.DestinationConfirmButton.Visibility == Visibility.Collapsed, "clicking outside commits direct input and hides confirmation");
            rightClick.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(!window.ValueBox.IsKeyboardFocusWithin && MainWindow.GetIsSelectionPulseActive(rightClick) && !MainWindow.HasSelectionPulseAnimationForTest(rightClick)
                && rightClick.Opacity == 1 && rightClick.BorderBrush is SolidColorBrush rightSelectionBorder && rightSelectionBorder.Color == ThemeService.Color("AccentBrush") && rightClick.BorderThickness == new Thickness(2),
                "selecting a mouse control keeps it bright with the shared selection outline without moving the caret");
            var key = keys.First(x => !Equals(x.Tag, "Space"));
            key.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.UpdateLayout();
            Pump(window);
            Check(!window.ValueBox.IsKeyboardFocusWithin && MainWindow.GetIsSelectionPulseActive(key) && !MainWindow.HasSelectionPulseAnimationForTest(key)
                && key.Opacity == 1 && Math.Abs(rightClick.Opacity - MainWindow.SelectionDimOpacity) < .01
                && key.BorderBrush is SolidColorBrush keySelectionBorder && keySelectionBorder.Color == ThemeService.Color("AccentBrush") && key.BorderThickness == new Thickness(2),
                "selecting an unassigned keyboard key keeps it bright with the shared selection outline and dims the previous control");
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
            Check(!window.ValueBox.IsKeyboardFocusWithin && MainWindow.GetIsSelectionPulseActive(key) && key.Opacity == 1, "selecting another keyboard key restores full brightness without focusing execution input");
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
            Check(window.InputName.Text == "Space+" + key.Tag && !window.ValueBox.IsKeyboardFocusWithin && MainWindow.GetIsSelectionPulseActive(key) && key.Opacity == 1, "the next on-screen key selects its mapping and restores full brightness without focusing execution input");
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            var multiA = keys.First(x => Equals(x.Tag, "A"));
            var multiB = keys.First(x => Equals(x.Tag, "B"));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input is "A" or "B" or "Space+A" or "Space+B");
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "A", Layer = "通常", Kind = ActionKind.Text, Value = "multi-A" });
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "B", Layer = "通常", Kind = ActionKind.Shortcut, Value = "Ctrl+B" });
            window.ColorButtonsForTest();
            var previousSingleSelection = keys.First(x => Equals(x.Tag, "P"));
            previousSingleSelection.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            previousSingleSelection.ApplyTemplate();
            var previousSingleBadge = (UIElement)previousSingleSelection.Template.FindName("MultiSelectBadge", previousSingleSelection)!;
            Check(MainWindow.GetIsCurrentSelected(previousSingleSelection) && previousSingleBadge.Opacity == 0 && previousSingleSelection.Opacity == 1
                && Math.Abs(multiA.Opacity - MainWindow.SelectionDimOpacity) < .01
                && previousSingleSelection.BorderBrush is SolidColorBrush singleBorder && singleBorder.Color == ThemeService.Color("AccentBrush") && previousSingleSelection.BorderThickness == new Thickness(2),
                "a singly selected key stays bright with the shared selection outline while every peer dims, without a badge");
            window.MultiSelectToggle.IsChecked = true;
            Pump(window);
            Check(!MainWindow.GetIsCurrentSelected(previousSingleSelection) && previousSingleSelection.BorderThickness == new Thickness(1)
                && Math.Abs(previousSingleSelection.Opacity - MainWindow.SelectionDimOpacity) < .01 && window.InputName.Text.Length == 0,
                "entering multi-select clears the previous single selection and initially dims every selectable key");
            multiA.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            multiB.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            foreach (var mouseButton in mouseButtons)
                mouseButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            multiA.ApplyTemplate();
            var multiTint = (UIElement)multiA.Template.FindName("SelectionTint", multiA)!;
            var multiBadge = (UIElement)multiA.Template.FindName("MultiSelectBadge", multiA)!;
            Check((window.MultiSelectToggle.Content?.ToString() == "選択" || window.MultiSelectToggle.Content is TextBlock) && window.MultiSelectToggle.Template != null
                && window.MultiCopyButton.IsEnabled && !window.MultiPasteButton.IsEnabled && window.MultiDeleteButton.IsEnabled
                && multiA.BorderBrush is SolidColorBrush multiBorder && multiBorder.Color == ThemeService.Color("AccentBrush") && multiA.BorderThickness == new Thickness(2)
                && multiTint.Opacity == 0 && multiBadge.Opacity == 0 && multiA.Opacity == 1 && multiB.Opacity == 1
                && Math.Abs(previousSingleSelection.Opacity - MainWindow.SelectionDimOpacity) < .01
                && MainWindow.GetIsMultiSelected(multiA) && MainWindow.GetIsSelectionPulseActive(multiA) && !MainWindow.HasSelectionPulseAnimationForTest(multiA)
                && mouseButtons.Where(x => !Equals(x.Tag, "MouseLeft")).All(x => MainWindow.GetIsMultiSelected(x) && x.Opacity == 1 && MainWindow.GetIsSelectionPulseActive(x) && !MainWindow.HasSelectionPulseAnimationForTest(x))
                && !MainWindow.GetIsMultiSelected(leftClick),
                "multi-select keeps every explicitly selected main key bright with the shared selection outline while unselected keys stay dim, with no badges");
            CaptureForReview(window, "main-multi-selection.png");
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
            double toolbarSaveX = window.ToolbarSaveButton.TranslatePoint(new System.Windows.Point(), window).X;
            double toolbarDeleteX = window.MultiDeleteButton.TranslatePoint(new System.Windows.Point(), window).X;
            Check(toolbarSaveX > toolbarDeleteX, $"save remains fixed at the right end of the toolbar after multi-select controls (save={toolbarSaveX:F1}, delete={toolbarDeleteX:F1}, saveVisibility={window.ToolbarSaveButton.Visibility})");
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
            var assignableDeck = DeckPanelLayout.DefaultLayout(window.ConfigForTest) ?? throw new InvalidOperationException("default Deck layout was not created");
            string assignableDeckAction = DeckPanelLayout.ActionValue(assignableDeck.Id);
            window.ApplyCatalogActionForTest(new CatalogAction("Deckパネル", assignableDeck.Name, "", ActionKind.Shortcut, assignableDeckAction), false);
            Pump(window);
            Check(window.CurrentProfileForTest.Mappings.Last(x => x.Input == "F5") is { Kind: ActionKind.Shortcut, Value: var assignedDeckValue } && assignedDeckValue == assignableDeckAction
                && window.ValueBox.Text == "Deckパネル：" + assignableDeck.Name
                && window.KindBox.SelectedItem?.GetType().GetProperty("IsDeckPanel")?.GetValue(window.KindBox.SelectedItem) is true,
                "selecting a saved Deck assigns its stable layout action and keeps the dedicated Deck panel row selected");
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
            Check(window.AppliedProfileNameForTest == "プロファイル4" && window.IsProfileOverlayVisibleForTest
                && window.CurrentProfileForTest.Name == "プロファイル4" && Equals(window.ProfileBox.SelectedItem, "プロファイル4"),
                "an explicit profile action switches the editor and runtime together without a hidden mapping profile");
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
            var selectedKeyBadge = (Border)key.Template.FindName("MultiSelectBadge", key)!;
            Check(!window.ValueBox.IsKeyboardFocusWithin && key.Background is SolidColorBrush editingAssignedBrush && editingAssignedBrush.Color == MainWindow.AssignmentColorFor(new Mapping { Kind = ActionKind.Shortcut, Value = "Ctrl+C" })
                && MainWindow.GetIsSelectionPulseActive(key) && MainWindow.GetIsCurrentSelected(key) && selectedKeyTint.Opacity == 0 && selectedKeyBadge.Opacity == 0
                && !MainWindow.HasSelectionPulseAnimationForTest(key) && key.Opacity == 1
                && key.BorderBrush is SolidColorBrush assignedSelectionBorder && assignedSelectionBorder.Color == ThemeService.Color("AccentBrush") && key.BorderThickness == new Thickness(2),
                $"selecting an assigned key preserves its full action color with the shared selection outline and without a tint or badge (selected={MainWindow.GetIsCurrentSelected(key)}, tint={selectedKeyTint.Opacity:F2}, badge={selectedKeyBadge.Opacity:F2})");
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
            var balancedActionLabels = new[] { "別のキー", "プロファイル", "ショートカット", "文字列", "アプリ・パス", "マクロ", "ジェスチャー", "Deckパネル", "キーパッドから入力" };
            var shortActionItems = Enumerable.Range(0, window.KindBox.Items.Count).Select(i => (ListBoxItem)window.KindBox.ItemContainerGenerator.ContainerFromIndex(i)!).ToArray();
            var longActionItems = Enumerable.Range(0, window.LongKindBox.Items.Count).Select(i => (ListBoxItem)window.LongKindBox.ItemContainerGenerator.ContainerFromIndex(i)!).ToArray();
            bool actionChoicesUseOneColumn = shortActionItems.Select(item => Math.Round(item.TranslatePoint(new System.Windows.Point(), window.KindBox).X)).Distinct().Count() == 1
                && longActionItems.Select(item => Math.Round(item.TranslatePoint(new System.Windows.Point(), window.LongKindBox).X)).Distinct().Count() == 1;
            Check(window.KindBox.Visibility == Visibility.Visible && shortActionLabels.SequenceEqual(balancedActionLabels) && longActionLabels.SequenceEqual(balancedActionLabels) && actionChoicesUseOneColumn, "short and long editors reflow into one complete no-wrap column with keypad input replacing disable");
            var allActionChoiceItems = shortActionItems.Concat(longActionItems).ToArray();
            var actionChoiceArrows = allActionChoiceItems.Select(item => Descendants<TextBlock>(item).Single(text => text.Text == "›")).ToArray();
            var actionChoiceLabels = allActionChoiceItems.Select(item => Descendants<TextBlock>(item).Single(text => balancedActionLabels.Contains(text.Text))).ToArray();
            Check(actionChoiceArrows.All(arrow => ReferenceEquals(arrow.Foreground, ThemeService.Brush("AccentTextBrush")) && arrow.HorizontalAlignment == System.Windows.HorizontalAlignment.Right)
                && actionChoiceLabels.All(label => label.HorizontalAlignment == System.Windows.HorizontalAlignment.Stretch && label.TextAlignment == TextAlignment.Left),
                "every action choice keeps its label left-aligned and uses one right-edge accent chevron");
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
            var settingsSwitches = Descendants<System.Windows.Controls.CheckBox>(settings).ToArray();
            foreach (var appSwitch in settingsSwitches)
                appSwitch.ApplyTemplate();
            Check(settingsSwitches.Length == 10 && settingsSwitches.All(appSwitch => appSwitch.Template.FindName("SwitchTrack", appSwitch) is Border),
                "all ten settings checkboxes render through the shared RELYR switch template");
            Check(Descendants<System.Windows.Controls.ScrollViewer>(settings).All(scroll => ReferenceEquals(scroll, settings.LayersScrollPanel)), "only the longer layer category uses a bounded scroll surface");
            settings.CategoryList.SelectedIndex = 6;
            settings.UpdateLayout();
            Check(settings.UpdatePanel.Visibility == Visibility.Visible && settings.GeneralPanel.Visibility == Visibility.Collapsed && settings.CheckForUpdatesButton.Content?.ToString() == "アップデートを確認" && settings.InstallUpdateButton.Content?.ToString() == "今すぐアップデート" && settings.InstallUpdateButton.Visibility == Visibility.Visible && settings.UpdateStatusText.Text.Contains("v99.0.0") && settings.UpdateStatusText.Foreground is SolidColorBrush availableBrush && availableBrush.Color == ThemeService.Color("WarningBrush") && !settings.UpdateStatusText.Text.EndsWith('。'), "available update uses a clear orange status without unnecessary terminal punctuation");
            settings.ApplyUpdateResult(new UpdateCheckResult(MainWindow.RunningVersion, MainWindow.DisplayVersion, null, DateTimeOffset.Now), true);
            Check(settings.UpdateStatusText.Text == $"最新バージョンです（v{MainWindow.DisplayVersion}）" && settings.UpdateStatusText.Foreground is SolidColorBrush currentBrush && currentBrush.Color == ThemeService.Color("AccentBrush"), "current version uses a concise green status");
            var updateNotes = new UpdateNotesWindow("9.9.9", "- Deckを見やすく改善\n- 全画面表示を追加");
            updateNotes.Show();
            PumpFor(TimeSpan.FromMilliseconds(80));
            Check(updateNotes.VersionText.Text.Contains("v9.9.9", StringComparison.Ordinal) && updateNotes.NotesText.Text.Contains("全画面表示", StringComparison.Ordinal), "the post-update window shows the installed version and GitHub release body without rewriting it");
            CaptureForReview(updateNotes, "update-notes.png");
            updateNotes.Close();
            settings.SelectCategory("Support");
            settings.UpdateLayout();
            Check(settings.SupportPanel.Visibility == Visibility.Visible && settings.UpdatePanel.Visibility == Visibility.Collapsed && settings.OpenSupportPageButton.Content?.ToString() == "支援ページを開く" && Uri.TryCreate(SettingsWindow.SupportPageUrl, UriKind.Absolute, out var supportUri) && supportUri.Scheme == Uri.UriSchemeHttps && supportUri.Host == "ko-fi.com", "support settings use the trusted HTTPS Ko-fi page");
            var settingsCategoryTags = settings.CategoryList.Items.Cast<ListBoxItem>().Select(item => item.Tag?.ToString()).ToArray();
            Check(Array.IndexOf(settingsCategoryTags, "Disabled") == Array.IndexOf(settingsCategoryTags, "Update") + 1, "the dedicated disabled-app category appears immediately below updates");
            settings.SelectCategory("Disabled");
            settings.UpdateLayout();
            Check(settings.DisabledPanel.Visibility == Visibility.Visible
                && settings.LayersScrollPanel.Visibility == Visibility.Collapsed
                && Descendants<System.Windows.Controls.ListBox>(settings.DisabledPanel).Contains(settings.InputDisabledApplicationList)
                && !Descendants<System.Windows.Controls.ListBox>(settings.LayersPanel).Contains(settings.InputDisabledApplicationList),
                "disabled applications live on the independent disabled page instead of the layer page");
            settings.Show();
            PumpFor(TimeSpan.FromMilliseconds(80));
            CaptureForReview(settings, "settings-disabled.png");
            settings.Hide();
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
            settingsWithAutoSave.SelectCategory("Disabled");
            settingsWithAutoSave.UpdateLayout();
            Check(settingsWithAutoSave.DisabledPanel.ActualHeight <= ((FrameworkElement)settingsWithAutoSave.DisabledPanel.Parent).ActualHeight + .5, "disabled-app settings fit above the footer without clipping");
            settingsWithAutoSave.SelectCategory("Support");
            settingsWithAutoSave.UpdateLayout();
            Check(settingsWithAutoSave.SupportPanel.ActualHeight <= ((FrameworkElement)settingsWithAutoSave.SupportPanel.Parent).ActualHeight + .5, "support settings fit without scrolling or clipping");
            settingsWithAutoSave.CategoryList.SelectedIndex = 2;
            settingsWithAutoSave.UpdateLayout();
            Check(settingsWithAutoSave.LayersScrollPanel.ActualHeight <= ((FrameworkElement)settingsWithAutoSave.LayersScrollPanel.Parent).ActualHeight + .5, "layer settings remain fully reachable through a bounded scroll surface without covering the footer");
            settingsWithAutoSave.SelectCategory("Disabled");
            settingsWithAutoSave.UpdateLayout();
            settingsWithAutoSave.AddInputDisabledApplicationForTest("RobloxPlayerBeta.exe");
            Check(settingsWithAutoSave.InputDisabledApplicationList.Items.Cast<ApplicationDisplayItem>().Select(x => x.Value).SequenceEqual(["RobloxPlayerBeta.exe"])
                && Descendants<System.Windows.Controls.Button>(settingsWithAutoSave.DisabledPanel).Any(x => x.Content?.ToString() == "起動中から追加…")
                && Descendants<System.Windows.Controls.TextBlock>(settingsWithAutoSave.DisabledPanel).Any(x => x.Text.Contains("入力をそのままアプリへ渡します", StringComparison.Ordinal)),
                "the dedicated disabled page clearly manages applications where all RELYR keyboard and mouse processing is disabled");
            window.SetInputDisabledApplicationsForTest(["RobloxPlayerBeta.exe"], "RobloxPlayerBeta");
            var excludedKeyDown = window.DirectPhysicalKeyForTest(0x20, false);
            var excludedKeyUp = window.DirectPhysicalKeyForTest(0x20, true);
            var excludedMouseDown = window.DirectPhysicalMouseForTest(0x201);
            var excludedMouseUp = window.DirectPhysicalMouseForTest(0x202);
            Check(!window.ShouldInterceptPhysicalInputForTest && !window.ShouldInterceptPhysicalMouseForTest
                && new[] { excludedKeyDown, excludedKeyUp, excludedMouseDown, excludedMouseUp }.All(result => result != (IntPtr)1)
                && !window.HasCapturedInputStateForTest,
                "an active registered application receives untouched keyboard and mouse input with no RELYR capture");
            window.SetInputDisabledApplicationsForTest([], "RELYR");
            Check(!window.InputProcessingSuppressedForTest,
                "leaving an input-disabled application restores normal RELYR processing immediately");
            settingsWithAutoSave.CategoryList.SelectedIndex = 0;
            settingsWithAutoSave.UpdateLayout();
            Check(settingsWithAutoSave.AutoSaveBox.IsChecked == true, "auto-save option exists");
            Check(settingsWithAutoSave.SpaceRepeatBox.IsChecked == true && settingsWithAutoSave.SpaceRepeatDelayBox.Text == "450", "Space hold repeat controls are clear");
            Check(settingsWithAutoSave.EnableCapsRemapButton != null && settingsWithAutoSave.DisableCapsRemapButton != null && settingsWithAutoSave.CapsRemapStatus.Text.Length > 0, "CapsLock F13 setup and restore controls are available");
            Check(Descendants<System.Windows.Controls.Button>(settingsWithAutoSave).Any(x => x.Content?.ToString() == "インポート") && Descendants<System.Windows.Controls.Button>(settingsWithAutoSave).Any(x => x.Content?.ToString() == "エクスポート"), "import and export are in app settings");
            settingsWithAutoSave.Close();
            Pump(window);
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
            var modifierKeys = new ConcurrentQueue<(ushort Key, bool Up)>();
            var modifierMouse = new ConcurrentQueue<uint>();
            InputEngine.KeyOutputForTest = (key, up) => { modifierKeys.Enqueue((key, up)); return true; };
            InputEngine.MouseFlagOutputForTest = (flag, _) => modifierMouse.Enqueue(flag);
            using (var physicalMouse = new InputEngine { Enabled = false })
            {
                bool allWorkerDragsCompleted = true;
                foreach (var (value, modifierKey) in new[] { ("ShiftDrag", (ushort)0x10), ("CtrlDrag", (ushort)0x11) })
                {
                    while (modifierKeys.TryDequeue(out _)) { }
                    while (modifierMouse.TryDequeue(out _)) { }
                    physicalMouse.DirectMouseForTest(0x201);
                    bool queued = window.QueueModifierDragForTest(value, true);
                    bool started = SpinWait.SpinUntil(() => modifierKeys.Any(item => item == (modifierKey, false)) && modifierMouse.Contains(2u), 500);
                    physicalMouse.DirectMouseForTest(0x202);
                    bool ended = SpinWait.SpinUntil(() => modifierKeys.Any(item => item == (modifierKey, true)) && modifierMouse.Contains(4u), 500);
                    allWorkerDragsCompleted &= queued && started && ended;
                }
                Check(allWorkerDragsCompleted, "the main-window modifier path starts and releases both Shift-drag and Ctrl-drag in order");
            }
            bool exactMainWindowPathPassed = true;
            foreach (var (value, modifierKey) in new[] { ("ShiftDrag", (ushort)0x10), ("CtrlDrag", (ushort)0x11) })
            {
                window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals("Space+MouseLeft", StringComparison.OrdinalIgnoreCase));
                window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "Space+MouseLeft", Layer = "Space", Kind = ActionKind.Mouse, Value = value });
                window.SaveAndApplyForTest();
                while (modifierKeys.TryDequeue(out _)) { }
                while (modifierMouse.TryDequeue(out _)) { }
                var spaceDown = window.DirectPhysicalKeyForTest(0x20, false);
                var mouseDown = window.DirectPhysicalMouseForTest(0x201, 100, 100);
                bool startedBeforeFirstMove = modifierKeys.Any(item => item == (modifierKey, false)) && modifierMouse.Contains(2u);
                var firstMove = window.DirectPhysicalMouseForTest(0x200, 170, 145);
                bool started = SpinWait.SpinUntil(() => modifierKeys.Any(item => item == (modifierKey, false))
                    && modifierMouse.Contains(2u)
                    && window.NativeMouseDragReadyForTest("Space+MouseLeft"), 500);
                var secondMove = window.DirectPhysicalMouseForTest(0x200, 190, 160);
                var mouseUp = window.DirectPhysicalMouseForTest(0x202, 170, 145);
                var spaceUp = window.DirectPhysicalKeyForTest(0x20, true);
                bool completed = SpinWait.SpinUntil(() => modifierKeys.Any(item => item == (modifierKey, false)) && modifierKeys.Any(item => item == (modifierKey, true)) && modifierMouse.Contains(2u) && modifierMouse.Contains(4u), 500);
                exactMainWindowPathPassed &= spaceDown == (IntPtr)1 && mouseDown == (IntPtr)1 && (startedBeforeFirstMove || firstMove == (IntPtr)1) && started && secondMove != (IntPtr)1 && mouseUp == (IntPtr)1 && spaceUp == (IntPtr)1 && completed;
            }
            Check(exactMainWindowPathPassed, "the installed-app path blocks any early movement until Ctrl/Shift plus synthetic LeftDown is ready, then releases after physical mouse-up");
            InputEngine.KeyOutputForTest = null;
            InputEngine.MouseFlagOutputForTest = null;
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
            Check(Descendants<System.Windows.Controls.Image>(applicationPicker.ApplicationList).Any(image => image.Source != null), "application picker shows an application icon beside every visible choice");
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
            foreach (var macroSwitch in Descendants<System.Windows.Controls.CheckBox>(macro))
                macroSwitch.ApplyTemplate();
            Check(Descendants<System.Windows.Controls.CheckBox>(macro).All(appSwitch => appSwitch.Template.FindName("SwitchTrack", appSwitch) is Border),
                "every macro recording checkbox uses the shared switch template");
            Check(macro.TitleBarUsesDarkMode == MainWindow.IsWindowsAppDarkMode(), "macro title bar follows the Windows app theme");
            Check(Math.Abs(macro.MacroSearchBox.ActualHeight - 40) < .1 && Math.Abs(macro.NameBox.ActualHeight - 44) < .1 && macro.NameBox.BorderThickness == new Thickness(0), "macro name is a plain title instead of another framed form field");
            System.Windows.FrameworkElement[] manualFormControls = [macro.ManualTextBox, macro.AddTextActionButton, macro.WaitBox, macro.AddWaitButton];
            double[] manualLeftEdges = manualFormControls.Select(x => x.TranslatePoint(new System.Windows.Point(), macro).X).ToArray();
            double[] manualRightEdges = manualFormControls.Select(x => x.TranslatePoint(new System.Windows.Point(), macro).X + x.ActualWidth).ToArray();
            Check(manualLeftEdges.Max() - manualLeftEdges.Min() < .1 && manualRightEdges.Max() - manualRightEdges.Min() < .1 && Math.Abs(macro.ManualTextBox.ActualWidth - macro.WaitBox.ActualWidth) < .1, "manual macro text, wait fields, and action buttons share exact left and right edges");
            Check(macroConfig.Macros.Count == 0 && macro.EmptyHint.IsVisible && !macro.EditorPanel.IsEnabled, "macro window starts empty and waits for New");
            Check(macro.UseButton.Visibility == Visibility.Collapsed, "main macro manager hides the ambiguous assign button");
            Check(macro.MacroList.ActualWidth > 140 && macro.StepList.ActualWidth > 300 && macro.EditorTabs.ActualWidth > 240, "macro manager uses a readable three-pane layout");
            var macroListActions = new[] { macro.NewMacroButton, macro.DuplicateMacroButton, macro.EditMacroButton, macro.DeleteMacroButton };
            Check(macroListActions.All(button => button.Content is TextBlock text && text.FontFamily.Source == "Segoe MDL2 Assets" && Math.Abs(button.ActualHeight - 40) < .1) && macroListActions.Max(button => button.ActualWidth) - macroListActions.Min(button => button.ActualWidth) < .1 && macroListActions.All(button => button.ToolTip != null), "macro list actions use four equal icon-only controls with descriptive tooltips");
            Check(new[] { macro.ManualModeButton, macro.RecordModeButton, macro.StepEditModeButton }.SelectMany(Descendants<TextBlock>).Select(x => x.Text).Where(x => x is "手動追加" or "自動記録" or "手順編集").SequenceEqual(["手動追加", "自動記録", "手順編集"]) && macro.EditorTabs.Template != null && macro.DropIndicator.Visibility == Visibility.Collapsed, "macro editing modes use icon-labelled app buttons and keep the drag insertion guide hidden until needed");
            macro.RecordModeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(macro.EditorTabs.SelectedIndex == 1, "macro mode buttons switch the editor without old tab headers");
            macro.ManualModeButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            macro.NewMacroButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(macroConfig.Macros.Count == 1 && !macro.NameBox.IsReadOnly && macro.NameBox.IsKeyboardFocusWithin && macro.EditMacroButton.IsEnabled && macro.ConfirmNameButton.IsVisible, "New creates a macro and immediately enters name editing");
            macro.NameBox.Text = "確定テスト";
            macro.ConfirmNameButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(macro.NameBox.IsReadOnly && !macro.ConfirmNameButton.IsVisible && macroConfig.Macros[0].Name == "確定テスト", "macro name has an explicit confirmation button");
            macro.NameBox.ApplyTemplate();
            var macroNameEditButton = (System.Windows.Controls.Button?)macro.NameBox.Template.FindName("EditMacroNameButton", macro.NameBox);
            macroNameEditButton?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(macroNameEditButton != null && !macro.NameBox.IsReadOnly && macro.NameBox.IsKeyboardFocusWithin, "the visible macro-title pencil starts inline name editing");
            macro.ConfirmNameButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            macro.AddManualKeyForTest(System.Windows.Input.Key.A);
            Check(macroConfig.Macros[0].Steps.Select(x => x.Event).SequenceEqual(["A Down", "A Up"]), "manual macro mode appends each pressed key as a safe down/up pair");
            Check(macro.StepList.Items.Cast<MacroWindow.StepView>().Any(x => x.Title.Contains("A") && x.Detail.Contains("Down")), "macro steps are displayed as human-readable operations");
            Pump(window);
            var macroStepHandles = Descendants<Border>(macro.StepList).Where(border => Equals(border.Tag, "MacroStepDragHandle")).ToArray();
            var macroStepNumbers = Descendants<Border>(macro.StepList).Where(border => Equals(border.Tag, "MacroStepNumber")).ToArray();
            Check(macroStepHandles.Length == macroConfig.Macros[0].Steps.Count && macroStepHandles.All(handle => handle.Cursor == System.Windows.Input.Cursors.SizeAll && !string.IsNullOrWhiteSpace(handle.ToolTip?.ToString()))
                && macroStepNumbers.Length == macroConfig.Macros[0].Steps.Count && macroStepNumbers.All(number => Math.Abs(number.ActualWidth - 34) < .1),
                "every macro step has a readable fixed number badge and an explicit three-dot drag handle");
            Check(macro.OpenSafeStepDragPreviewForTest(), "the visual-only macro drag preview is also click-through at the native popup-window level");
            CaptureForReview(macro, "macro-manager.png");
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
            output.WriteLine("SKIP physical window-under-cursor checks in the default UI suite: they require an isolated desktop and must never move the user's pointer.");
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
                    if (visualKey == "CapsLock" || (visualKey == "Space" && layer is "通常" or "Space") || (visualKey == "MouseLeft" && layer == "通常"))
                        continue;
                    string input = layer == "通常" ? visualKey : layer + "+" + visualKey;
                    window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
                    window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = input, Layer = layer, Kind = ActionKind.Key, Value = "A" });
                }
                layerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump(window);
                var missed = visualInputs.Where(x => (string)x.Tag != "CapsLock" && !((string)x.Tag == "Space" && layer is "通常" or "Space") && !((string)x.Tag == "MouseLeft" && layer == "通常") && !HasBackgroundColor(x, replacementColor)).Select(x => (string)x.Tag).Distinct().ToArray();
                Check(visualInputs.Count > 100 && missed.Length == 0, $"every assignable visual key is orange on the {layer} layer" + (missed.Length == 0 ? "" : " (missing: " + string.Join(",", missed) + ")"));
            }
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals("F1", StringComparison.OrdinalIgnoreCase));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals("F24", StringComparison.OrdinalIgnoreCase));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals("Space+J", StringComparison.OrdinalIgnoreCase));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals("CapsLock+K", StringComparison.OrdinalIgnoreCase));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals("MouseRight+L", StringComparison.OrdinalIgnoreCase));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals("MouseBack+M", StringComparison.OrdinalIgnoreCase));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals("MouseForward+N", StringComparison.OrdinalIgnoreCase));
            var unassignedNormalMouseInputs = new[] { "MouseLeft", "MouseRight", "MouseMiddle", "MouseBack", "MouseForward" };
            window.CurrentProfileForTest.Mappings.RemoveAll(mapping => unassignedNormalMouseInputs.Contains(mapping.Input, StringComparer.OrdinalIgnoreCase)
                || unassignedNormalMouseInputs.Any(input => mapping.Input.Equals("Taskbar+" + input, StringComparison.OrdinalIgnoreCase)));
            window.CurrentProfileForTest.Mappings.Add(new Mapping
            {
                Input = "F1",
                Layer = "通常",
                Kind = ActionKind.None,
                LongPressKind = ActionKind.Shortcut,
                LongPressValue = OverlayService.DeckPanelAction,
                LongPressMs = 50
            });
            window.CurrentProfileForTest.Mappings.Add(new Mapping
            {
                Input = "Space+J",
                Layer = "Space",
                Kind = ActionKind.Key,
                Value = "Left"
            });
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "CapsLock+K", Layer = "CapsLock", Kind = ActionKind.Key, Value = "Right" });
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "MouseRight+L", Layer = "MouseRight", Kind = ActionKind.Key, Value = "Up" });
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "MouseBack+M", Layer = "MouseBack", Kind = ActionKind.Key, Value = "Down" });
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "MouseForward+N", Layer = "MouseForward", Kind = ActionKind.Key, Value = "Enter" });
            window.SaveAndApplyForTest();
            var staleMouseLeftLayerMapping = new Mapping { Input = "MouseLeft+J", Layer = "MouseLeft", Kind = ActionKind.Key, Value = "A" };
            window.AddAppliedMappingForTest(staleMouseLeftLayerMapping);
            bool staleMouseLeftLayerMappingIgnored = ReferenceEquals(window.RuntimeMappingForTest("MouseLeft+J"), staleMouseLeftLayerMapping)
                && !window.RuntimeInterceptsInputForTest("MouseLeft+J");
            string runtimeProfileBeforeLayerSelection = window.AppliedProfileNameForTest;
            window.SetCapsLockRemapForTest(true);
            var requestedDeckActions = new ConcurrentQueue<string>();
            var focusedEditorKeyActions = new ConcurrentQueue<(ushort Key, bool Up)>();
            var ordinaryMouseClickBatches = new ConcurrentQueue<(uint Flag, uint Data)[]>();
            OverlayService.ActionRequestedForTest = requestedDeckActions.Enqueue;
            InputEngine.KeyOutputForTest = (key, up) => { focusedEditorKeyActions.Enqueue((key, up)); return true; };
            InputEngine.MouseFlagOutputForTest = (_, _) => { };
            InputEngine.MouseClickBatchOutputForTest = batch => ordinaryMouseClickBatches.Enqueue(batch);
            bool everyRuntimeActionWorkedOnEveryLayerEditor = true;
            var editorLayerButtons = new[] { window.NormalLayerButton, window.SpaceLayerButton, window.CapsLockLayerButton, window.RightMouseLayerButton, window.BackMouseLayerButton, window.ForwardMouseLayerButton };
            foreach (var layerButton in editorLayerButtons)
            {
                layerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump(window);
                var layerF2 = visualInputs.First(button => Equals(button.Tag, "F2"));
                layerF2.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump(window);
                window.ValueBox.Focus();
                System.Windows.Input.Keyboard.Focus(window.ValueBox);
                Pump(window);
                int before = requestedDeckActions.Count;
                var f1Down = window.DirectPhysicalKeyForTest(0x70, false);
                bool requested = SpinWait.SpinUntil(() => requestedDeckActions.Count > before, 500);
                var f1Up = window.DirectPhysicalKeyForTest(0x70, true);
                var unassignedF24Down = window.DirectPhysicalKeyForTest(0x87, false);
                var unassignedF24Up = window.DirectPhysicalKeyForTest(0x87, true);
                int keyActionsBefore = focusedEditorKeyActions.Count;
                var spaceDown = window.DirectPhysicalKeyForTest(0x20, false);
                var jDown = window.DirectPhysicalKeyForTest(0x4A, false);
                var jUp = window.DirectPhysicalKeyForTest(0x4A, true);
                var spaceUp = window.DirectPhysicalKeyForTest(0x20, true);
                bool spaceStateClean = !window.HasCapturedInputStateForTest;

                var capsDown = window.DirectPhysicalKeyForTest(0x7C, false);
                var kDown = window.DirectPhysicalKeyForTest(0x4B, false);
                var kUp = window.DirectPhysicalKeyForTest(0x4B, true);
                var capsUp = window.DirectPhysicalKeyForTest(0x7C, true);
                bool capsStateClean = !window.HasCapturedInputStateForTest;

                var rightDown = window.DirectPhysicalMouseForTest(0x204);
                var lDown = window.DirectPhysicalKeyForTest(0x4C, false);
                var lUp = window.DirectPhysicalKeyForTest(0x4C, true);
                var rightUp = window.DirectPhysicalMouseForTest(0x205);
                bool rightStateClean = !window.HasCapturedInputStateForTest;

                var backDown = window.DirectPhysicalMouseForTest(0x20B, 1 << 16, 0, 0);
                var mDown = window.DirectPhysicalKeyForTest(0x4D, false);
                var mUp = window.DirectPhysicalKeyForTest(0x4D, true);
                var backUp = window.DirectPhysicalMouseForTest(0x20C, 1 << 16, 0, 0);
                bool backStateClean = !window.HasCapturedInputStateForTest;

                var forwardDown = window.DirectPhysicalMouseForTest(0x20B, 2 << 16, 0, 0);
                var nDown = window.DirectPhysicalKeyForTest(0x4E, false);
                var nUp = window.DirectPhysicalKeyForTest(0x4E, true);
                var forwardUp = window.DirectPhysicalMouseForTest(0x20C, 2 << 16, 0, 0);
                bool forwardStateClean = !window.HasCapturedInputStateForTest;
                bool allKeyActionsProduced = SpinWait.SpinUntil(() => focusedEditorKeyActions.Count >= keyActionsBefore + 10, 500);
                int clickBatchesBeforePlainClicks = ordinaryMouseClickBatches.Count;
                bool mappedChordsDidNotReplaySourceClicks = clickBatchesBeforePlainClicks == (Array.IndexOf(editorLayerButtons, layerButton) * 3);

                var unassignedLeftDown = window.DirectPhysicalMouseForTest(0x201);
                var unassignedLeftUp = window.DirectPhysicalMouseForTest(0x202);
                bool unassignedLeftStateClean = !window.HasCapturedInputStateForTest;

                var plainRightDown = window.DirectPhysicalMouseForTest(0x204);
                var plainRightUp = window.DirectPhysicalMouseForTest(0x205);
                bool plainRightStateClean = !window.HasCapturedInputStateForTest;
                bool plainRightReplayed = SpinWait.SpinUntil(() => ordinaryMouseClickBatches.Count >= clickBatchesBeforePlainClicks + 1, 500);
                var plainBackDown = window.DirectPhysicalMouseForTest(0x20B, 1 << 16, 0, 0);
                var plainBackUp = window.DirectPhysicalMouseForTest(0x20C, 1 << 16, 0, 0);
                bool plainBackStateClean = !window.HasCapturedInputStateForTest;
                bool plainBackReplayed = SpinWait.SpinUntil(() => ordinaryMouseClickBatches.Count >= clickBatchesBeforePlainClicks + 2, 500);
                var plainForwardDown = window.DirectPhysicalMouseForTest(0x20B, 2 << 16, 0, 0);
                var plainForwardUp = window.DirectPhysicalMouseForTest(0x20C, 2 << 16, 0, 0);
                bool plainForwardStateClean = !window.HasCapturedInputStateForTest;
                bool plainForwardReplayed = SpinWait.SpinUntil(() => ordinaryMouseClickBatches.Count >= clickBatchesBeforePlainClicks + 3, 500);
                var plainClickBatches = ordinaryMouseClickBatches.ToArray().Skip(clickBatchesBeforePlainClicks).Take(3).ToArray();
                bool ordinaryClicksPreserved = unassignedLeftDown != (IntPtr)1 && unassignedLeftUp != (IntPtr)1
                    && new[] { plainRightDown, plainRightUp, plainBackDown, plainBackUp, plainForwardDown, plainForwardUp }.All(result => result == (IntPtr)1)
                    && plainRightReplayed && plainBackReplayed && plainForwardReplayed
                    && plainClickBatches.Length == 3
                    && plainClickBatches[0].SequenceEqual([(8u, 0u), (16u, 0u)])
                    && plainClickBatches[1].SequenceEqual([(0x80u, 1u), (0x100u, 1u)])
                    && plainClickBatches[2].SequenceEqual([(0x80u, 2u), (0x100u, 2u)]);
                everyRuntimeActionWorkedOnEveryLayerEditor &= window.ValueBox.IsKeyboardFocusWithin
                    && window.AppliedProfileNameForTest == runtimeProfileBeforeLayerSelection
                    && staleMouseLeftLayerMappingIgnored
                    && f1Down == (IntPtr)1 && f1Up == (IntPtr)1 && requested
                    && unassignedF24Down != (IntPtr)1 && unassignedF24Up != (IntPtr)1
                    && new[] { spaceDown, jDown, jUp, spaceUp, capsDown, kDown, kUp, capsUp, rightDown, lDown, lUp, rightUp, backDown, mDown, mUp, backUp, forwardDown, nDown, nUp, forwardUp }.All(result => result == (IntPtr)1)
                    && allKeyActionsProduced && mappedChordsDidNotReplaySourceClicks && ordinaryClicksPreserved
                    && spaceStateClean && capsStateClean && rightStateClean && backStateClean && forwardStateClean
                    && unassignedLeftStateClean && plainRightStateClean && plainBackStateClean && plainForwardStateClean;
            }
            OverlayService.ActionRequestedForTest = null;
            InputEngine.KeyOutputForTest = null;
            InputEngine.MouseFlagOutputForTest = null;
            InputEngine.MouseClickBatchOutputForTest = null;
            window.RemoveAppliedMappingForTest(staleMouseLeftLayerMapping);
            window.SetCapsLockRemapForTest(false);
            Check(everyRuntimeActionWorkedOnEveryLayerEditor
                && requestedDeckActions.All(action => action == OverlayService.DeckPanelAction)
                && requestedDeckActions.Count == editorLayerButtons.Length
                && new ushort[] { 0x25, 0x27, 0x26, 0x28, 0x0D }.All(key =>
                    focusedEditorKeyActions.Count(action => action == (key, false)) == editorLayerButtons.Length
                    && focusedEditorKeyActions.Count(action => action == (key, true)) == editorLayerButtons.Length)
                && ordinaryMouseClickBatches.Count == editorLayerButtons.Length * 3,
                "every layer editor keeps all runtime actions active, rejects stale MouseLeft layer mappings, passes physical left clicks through without capture, and preserves ordinary right, back, and forward clicks");
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
            Check(coordinateMacro.CoordinateCaptureActiveForTest && coordinateMacro.CoordinateCaptureLabel.Text.Contains("Esc", StringComparison.Ordinal), "coordinate button clearly enters one-click capture mode");
            using (var coordinateEngine = new InputEngine())
            {
                var coordinateDown = coordinateEngine.DirectMouseForTest(0x201, 0, 321, 654);
                var coordinateUp = coordinateEngine.DirectMouseForTest(0x202, 0, 321, 654);
                Pump(window);
                Check(coordinateDown == (IntPtr)1 && coordinateUp == (IntPtr)1 && coordinateConfig.Macros[0].Steps.Select(x => x.Event).SequenceEqual(["MouseMove:321,654", "MouseLeft Down", "MouseLeft Up"]) && !coordinateMacro.CoordinateCaptureActiveForTest && !InputEngine.CoordinateCapturePendingForTest && coordinateMacro.CoordinateCaptureLabel.Text == "座標を記録", "one captured coordinate appends a compact move-and-click macro and automatically leaves capture mode");
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
            var animatedNumber = DeckIconCatalog.CreateVisual(new Mapping { DeckIcon = DeckIconCatalog.AnimatedId("number-20") }, 22, false);
            bool animatedNumberIsRunning = animatedNumber is TextBlock { Text: "20", RenderTransform: TransformGroup numberTransforms }
                && numberTransforms.Children.Any(transform => transform.HasAnimatedProperties);
            bool hasAllNumberSamples = Enumerable.Range(1, 20).All(number => DeckIconCatalog.Presets.Any(preset => preset.Id == "number-" + number && preset.Glyph == number.ToString()));
            int expectedDeckPresetCount = DeckIconCatalog.Presets.Count;
            Check(lightDeckIconPicker.PresetCountForTest == expectedDeckPresetCount && lightDeckIconPicker.AnimatedPresetCountForTest == expectedDeckPresetCount && lightDeckIconPicker.BrowseButton.IsVisible && lightDeckIconPicker.SelectedPresetId == "home" && animatedPresetIsRunning && scissorsSnipIsRunning && animatedNumberIsRunning && hasAllNumberSamples && Descendants<System.Windows.Controls.Button>(lightDeckIconPicker.PresetPanel).All(button => button.Foreground is SolidColorBrush brush && DeckPanelLayout.ContrastRatio(brush.Color, ThemeService.Color("ControlBackground")) >= 4.5), $"Deck icon picker exposes every readable still and animated preset as a paired set, including numeric samples 1 through 20, and retains custom image browsing (catalog={expectedDeckPresetCount}, still={lightDeckIconPicker.PresetCountForTest}, animated={lightDeckIconPicker.AnimatedPresetCountForTest}, number20={animatedNumberIsRunning})");
            var presetButtons = lightDeckIconPicker.PresetPanel.Children.Cast<System.Windows.Controls.Button>().Take(3).ToArray();
            bool presetsFillAvailableWidth = lightDeckIconPicker.PresetPanel.ActualWidth >= lightDeckIconPicker.StaticPresetScroll.ViewportWidth - 1
                && presetButtons.Length == 3
                && Math.Abs(presetButtons[0].TranslatePoint(new System.Windows.Point(), lightDeckIconPicker).Y - presetButtons[2].TranslatePoint(new System.Windows.Point(), lightDeckIconPicker).Y) < 1;
            var footerButtons = new[] { lightDeckIconPicker.BrowseButton, lightDeckIconPicker.ClearButton, lightDeckIconPicker.CancelButton, lightDeckIconPicker.ApplyButton };
            bool compactFooter = footerButtons.Select(button => button.TranslatePoint(new System.Windows.Point(), lightDeckIconPicker).Y).DistinctBy(y => Math.Round(y)).Count() == 1
                && footerButtons.All(button => Math.Abs(button.ActualHeight - 44) < 1)
                && Math.Abs(lightDeckIconPicker.BrowseButton.ActualWidth - lightDeckIconPicker.ClearButton.ActualWidth) < 1;
            Check(presetsFillAvailableWidth && compactFooter, "Deck icon picker uses the full list width without a preview gap and keeps Browse, None, Cancel, and Apply aligned in one equal-height row");
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
            PumpFor(TimeSpan.FromMilliseconds(500));
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
            window.DeckCustomizeToggleButton.IsChecked = true;
            window.DeckCustomizeToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(window);
            window.DeckSizePresetBox.SelectedItem = window.DeckSizePresetBox.Items.Cast<ComboBoxItem>().First(item => Equals(item.Tag, "custom"));
            Pump(window);
            window.DeckColumnsBox.Text = "１２";
            window.DeckRowsBox.Text = "１８";
            Pump(window);
            Check(window.DeckColumnsBox.Width >= 54 && window.DeckColumnsBox.Height >= 36 && window.DeckRowsBox.Width >= 54 && window.DeckRowsBox.Height >= 36 && window.DeckColumnsBox.Text == "１２" && MainWindow.TryResolveDeckLayoutSize("custom", window.DeckColumnsBox.Text, window.DeckRowsBox.Text, out int lightColumns, out int lightRows) && lightColumns == 12 && lightRows == 18, "compact Deck dimension fields remain large enough to read and accept full-width digits");
            Check(window.ShouldInterceptPhysicalInputForTest && window.ShouldInterceptPhysicalMouseForTest,
                $"the Deck editor leaves normal keyboard and mouse mappings active while unassigned keys pass through (keyboardIntercept={window.ShouldInterceptPhysicalInputForTest}, mouseIntercept={window.ShouldInterceptPhysicalMouseForTest})");
            var focusedEditorLayerEvents = new List<string>();
            using (var focusedEditorLayerEngine = new InputEngine())
            {
                focusedEditorLayerEngine.ShouldInterceptInput = () => focusedEditorLayerEngine.HasCapturedPhysicalInput;
                focusedEditorLayerEngine.ShouldInterceptMouseInput = () => true;
                focusedEditorLayerEngine.HasMapping = input => input is "MouseRight+*" or "MouseRight+Space";
                focusedEditorLayerEngine.InputReceived = input => { focusedEditorLayerEvents.Add(input); return input.Equals("MouseRight+Space", StringComparison.OrdinalIgnoreCase); };
                var focusedRightDown = focusedEditorLayerEngine.DirectMouseForTest(0x204);
                var focusedSpaceDown = focusedEditorLayerEngine.DirectKeyForTest(0x20, false);
                var focusedSpaceUp = focusedEditorLayerEngine.DirectKeyForTest(0x20, true);
                var focusedRightUp = focusedEditorLayerEngine.DirectMouseForTest(0x205);
                Check(focusedRightDown == (IntPtr)1 && focusedSpaceDown == (IntPtr)1 && focusedSpaceUp == (IntPtr)1 && focusedRightUp == (IntPtr)1 && focusedEditorLayerEvents.SequenceEqual(["MouseRight+Space"]) && !focusedEditorLayerEngine.HasCapturedStateForTest(), "MouseRight+Space executes its Deck-capable layer chord once even while an active RELYR text editor receives standalone keys");
            }
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
            window.FailOpenAfterTaskbarClickReplayFailureForTest();
            Pump(window);
            var failOpenLeftDown = window.DirectPhysicalMouseForTest(0x201);
            var failOpenLeftUp = window.DirectPhysicalMouseForTest(0x202);
            Check(window.TaskbarClickReplayFailedForTest && !window.InputEngineEnabledForTest
                && failOpenLeftDown != (IntPtr)1 && failOpenLeftUp != (IntPtr)1
                && !window.HasCapturedInputStateForTest,
                "a taskbar replay failure disables RELYR interception and passes later physical clicks through without captured state");
            window.PrepareForSystemShutdown();
            Check(window.IsInputHookDisposedForTest, "system shutdown immediately disposes keyboard and mouse hooks");
        }
        catch (Exception ex) { report.RecordException("UI exception", "FAIL UI exception: ", ex); }
        finally
        {
            if (deferredStartupWindow != null) { deferredStartupWindow.PrepareForSystemShutdown(); deferredStartupWindow.Close(); }
            if (window != null) { window.PrepareForSystemShutdown(); window.Close(); }
            Environment.SetEnvironmentVariable("RELYR_CONFIG_DIR", previousConfigDirectory);
            try { if (Directory.Exists(testConfigDirectory)) Directory.Delete(testConfigDirectory, true); } catch { }
        }
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
    static double RenderedBaseWidth(FrameworkElement element, Visual ancestor)
        => RenderedWidth(element, ancestor) / RenderScale(element, horizontal: true);
    static double RenderedBaseHeight(FrameworkElement element, Visual ancestor)
        => RenderedHeight(element, ancestor) / RenderScale(element, horizontal: false);
    static double RenderScale(FrameworkElement element, bool horizontal)
    {
        if (element.RenderTransform is not ScaleTransform scale)
            return 1;
        double value = horizontal ? scale.ScaleX : scale.ScaleY;
        return double.IsFinite(value) && Math.Abs(value) > .001 ? Math.Abs(value) : 1;
    }
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
