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
            new ConfigService().Save(new AppConfig { FirstRunCompleted = true, CapsLockLayerWarningAccepted = true, CheckForUpdates = false, ThemeMode = AppThemeMode.Dark, AutoSave = false });
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
            var staleBareSpace = new Mapping { Input = "Space", Layer = "通常", Kind = ActionKind.Shortcut, Value = "DeckPanel:stale" };
            window.AddAppliedMappingForTest(staleBareSpace);
            Check(!leftClick.IsEnabled && leftClick.Opacity < .6 && !window.RuntimeInterceptsInputForTest("MouseLeft")
                && !window.RuntimeInterceptsInputForTest("Space"),
                "normal left click and stale bare-Space actions cannot intercept native Windows input even if an old mapping remains");
            window.RemoveAppliedMappingForTest(staleBareSpace);
            Check(DeckPanelLayout.ExternalFileDragEffects == System.Windows.DragDropEffects.Copy, "Deck file drags expose copy-only semantics so Explorer cannot move or delete the registered source file");
            var ordinary = mouseButtons.Where(x => !Equals(x.Tag, "MouseLeft") && !Equals(x.Tag, "MouseRight")).ToList();
            var wheelDown = mouseButtons.First(x => Equals(x.Tag, "WheelDown"));
            var tiltLeft = mouseButtons.First(x => Equals(x.Tag, "TiltLeft"));
            var tiltRight = mouseButtons.First(x => Equals(x.Tag, "TiltRight"));
            var forward = mouseButtons.First(x => Equals(x.Tag, "MouseForward"));
            var back = mouseButtons.First(x => Equals(x.Tag, "MouseBack"));
            var x1 = mouseButtons.First(x => Equals(x.Tag, "MouseX"));
            static Rect Bounds(System.Windows.Controls.Button button) => new(Canvas.GetLeft(button), Canvas.GetTop(button), button.Width, button.Height);

            Check(mouseButtons.Count == 10 && !x1.IsEnabled && x1.Opacity < .6
                && x1.ToolTip?.ToString() == "追加ボタンは入力として使用できません",
                "mouse diagram keeps the X1 position visibly unavailable with a concise reason");
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
            var pickerMouseButtons = picker.InputButtonsForTest.Where(x => x.Tag?.ToString() is "MouseLeft" or "MouseRight" or "MouseMiddle" or "MouseBack" or "MouseForward" or "WheelUp" or "WheelDown" or "TiltLeft" or "TiltRight").ToList();
            var pickerOrdinary = pickerMouseButtons.Where(x => x.Tag?.ToString() is not "MouseLeft" and not "MouseRight").ToList();
            var pickerClicks = pickerMouseButtons.Where(x => x.Tag?.ToString() is "MouseLeft" or "MouseRight").ToList();
            Check(pickerMouseButtons.Count == 9 && !picker.InputButtonsForTest.Any(x => Equals(x.Tag, "MouseX"))
                && pickerOrdinary.All(x => Math.Abs(x.Width - pickerA.Width) < .1 && Math.Abs(x.Height - pickerA.Height) < .1)
                && pickerClicks.All(x => Math.Abs(x.Width - pickerA.Width) < .1 && Math.Abs(x.Height - (pickerA.Height * 3 + 8)) < .1),
                "keypad-input mouse exposes only the nine mouse inputs it can actually record");
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

    internal static async Task<int> CaptureContextMenuReviewAsync(TextWriter output)
    {
        var report = new VerificationReport(output);
        Action<bool, string> Check = report.Check;
        string? previousConfigDirectory = Environment.GetEnvironmentVariable("RELYR_CONFIG_DIR");
        string testConfigDirectory = VerificationPaths.CreateRunDirectory("context-menu-review");
        AppThemeMode previousTheme = ThemeService.CurrentMode;
        MainWindow? window = null;
        try
        {
            Environment.SetEnvironmentVariable("RELYR_CONFIG_DIR", testConfigDirectory);
            var config = new AppConfig { FirstRunCompleted = true, CapsLockLayerWarningAccepted = true, CheckForUpdates = false, AutoSave = false, ThemeMode = AppThemeMode.Dark };
            config.Profiles[0].Mappings.Add(new Mapping { Input = "B", Layer = "通常", Kind = ActionKind.Text, Value = "visual-review" });
            new ConfigService().Save(config);
            ThemeService.Apply(AppThemeMode.Dark);
            window = new MainWindow(true, suppressTray: true, startupConfig: config, runtimeRole: RuntimeRole.Standard, startInputHooks: false)
            {
                Width = 1500,
                Height = 900,
                Topmost = true,
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            System.Windows.Application.Current.MainWindow = window;
            window.Show();
            window.Activate();
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
            await Task.Delay(320);
            var target = window.VisualInputButtonsForTest.First(button => Equals(button.Tag, "B"));

            var darkMenu = window.CreateInputContextMenu("B");
            target.ContextMenu = darkMenu;
            darkMenu.PlacementTarget = target;
            darkMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            darkMenu.IsOpen = true;
            await Task.Delay(360);
            Check(darkMenu.IsOpen && darkMenu.ActualWidth >= 180 && darkMenu.ActualHeight > 0,
                $"dark context menu is visibly open ({darkMenu.ActualWidth:F1}x{darkMenu.ActualHeight:F1})");
            Check(CaptureContextMenuForReview(darkMenu, "context-menu-dark.png"), "dark context menu screenshot saved");
            Check(CaptureDesktopForReview(window, "context-menu-dark-full.png"), "dark RELYR window with context menu screenshot saved");
            Check(CaptureElementsForReview(window, [window.KeyboardLayoutToolbarIcon, window.KeyboardLayoutBox], "keyboard-layout-alignment-dark.png"), "keyboard-layout alignment screenshot saved");
            darkMenu.IsOpen = false;
            target.ContextMenu = null;

            ThemeService.Apply(AppThemeMode.Light);
            await window.Dispatcher.InvokeAsync(window.UpdateLayout, System.Windows.Threading.DispatcherPriority.Render);
            await Task.Delay(260);
            var lightMenu = window.CreateInputContextMenu("B");
            target.ContextMenu = lightMenu;
            lightMenu.PlacementTarget = target;
            lightMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            lightMenu.IsOpen = true;
            await Task.Delay(360);
            Check(lightMenu.IsOpen && lightMenu.ActualWidth >= 180 && lightMenu.ActualHeight > 0,
                $"light context menu is visibly open ({lightMenu.ActualWidth:F1}x{lightMenu.ActualHeight:F1})");
            Check(CaptureContextMenuForReview(lightMenu, "context-menu-light.png"), "light context menu screenshot saved");
            Check(CaptureDesktopForReview(window, "context-menu-light-full.png"), "light RELYR window with context menu screenshot saved");
            lightMenu.IsOpen = false;
            target.ContextMenu = null;
        }
        catch (Exception error)
        {
            report.RecordException("Context menu review exception", "FAIL context menu review exception: ", error);
        }
        finally
        {
            ThemeService.Apply(previousTheme);
            if (window != null)
            {
                window.Topmost = false;
                window.PrepareForSystemShutdown();
                window.Close();
            }
            Environment.SetEnvironmentVariable("RELYR_CONFIG_DIR", previousConfigDirectory);
            try { if (Directory.Exists(testConfigDirectory)) Directory.Delete(testConfigDirectory, true); } catch { }
        }
        return report.Complete("CONTEXT MENU REVIEW PASSED", "CONTEXT MENU REVIEW FAILED: ");
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
            new ConfigService().Save(new AppConfig { FirstRunCompleted = true, CapsLockLayerWarningAccepted = true, CheckForUpdates = false, AutoSave = false, Gestures = [new GestureDefinition { Name = "ウィンドウ操作", UpKind = ActionKind.Shortcut, UpValue = "Win+Up", CenterKind = ActionKind.Key, CenterValue = "Enter" }] });
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
            Check(compactProfileManager.AssignedApplicationList.BorderThickness == new Thickness(0)
                && compactProfileManager.RunningApplicationList.BorderThickness == new Thickness(0)
                && Math.Abs(compactProfileManager.ProfilePaneDivider.ActualWidth - 1) < .1
                && Math.Abs(compactProfileManager.ApplicationTransferDivider.ActualWidth - 1) < .1,
                "profile application lists remain frameless while restrained one-pixel dividers preserve the transfer relationship");
            compactProfileManager.SetInstalledApplicationsLoadingStateForTest(showingInstalled: true, loadingInstalled: true);
            Check(compactProfileManager.InstalledApplicationsLoadingPanel.Visibility == Visibility.Visible
                && compactProfileManager.RunningApplicationList.Opacity < .3
                && !compactProfileManager.RunningApplicationList.IsHitTestVisible
                && Descendants<TextBlock>(compactProfileManager.InstalledApplicationsLoadingPanel).Any(text => text.Text.Contains("初回のみ", StringComparison.Ordinal)),
                "installed-app discovery immediately presents a simple centered loading state instead of appearing frozen");
            compactProfileManager.SetInstalledApplicationsLoadingStateForTest(showingInstalled: false, loadingInstalled: false);
            Check(Descendants<TextBlock>(compactProfileManager).Any(text => text.Text.Contains("対象アプリが前面に来る", StringComparison.Ordinal)) && compactProfileManager.AutoSwitchBox.ToolTip?.ToString()?.Contains("対象アプリ", StringComparison.Ordinal) == true, "profile manager clearly explains foreground-based automatic switching in the quiet footer");
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
            Check(compactProfileManager.ProfileCommandBar.Columns == 5
                && new[] { compactProfileManager.AddProfileButton, compactProfileManager.RenameProfileButton, compactProfileManager.DeleteProfileButton, compactProfileManager.CopyAssignmentsButton, compactProfileManager.PasteAssignmentsButton }.All(button => ReferenceEquals(button.Parent, compactProfileManager.ProfileCommandBar) && button.ActualWidth <= 30.1),
                "profile add, rename, delete, copy, and paste commands form one compact aligned toolbar");
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
            var gestureManager = new GestureManagerWindow([
                new GestureDefinition { Name = "ウィンドウ操作", GestureThresholdPixels = 9, LockCursorDuringGesture = false, UpKind = ActionKind.Shortcut, UpValue = "Win+Up" },
                new GestureDefinition { Name = "別の操作", GestureThresholdPixels = 31, LockCursorDuringGesture = true }
            ], [new Profile { Name = "標準", Mappings = [new Mapping { Input = "G", Kind = ActionKind.Gesture, Value = "ウィンドウ操作" }] }], [new MacroDefinition { Name = "マクロ1" }], "JIS") { Owner = window, ShowInTaskbar = false };
            gestureManager.Show();
            gestureManager.UpdateLayout();
            var gestureSlots = Descendants<System.Windows.Controls.Button>(gestureManager).Where(x => x.Tag is "Up" or "Down" or "Left" or "Right" or "Center").ToArray();
            var gestureLabels = Descendants<TextBlock>(gestureManager).Select(x => x.Text).ToArray();
            var gestureSelectButtons = gestureSlots.Where(x => x.ToolTip?.ToString()?.EndsWith("動作を選択", StringComparison.Ordinal) == true).ToArray();
            var gestureChoiceMenu = gestureManager.CreateActionTypeMenu(gestureSelectButtons.First(), "Up");
            Check(gestureManager.TitleBarUsesDarkMode == MainWindow.IsWindowsAppDarkMode() && gestureSelectButtons.Length == 5 && gestureManager.ResultGestures[0].Name == "ウィンドウ操作" && gestureLabels.Contains("短押し") && !gestureLabels.Contains("センター") && gestureChoiceMenu.Items.OfType<MenuItem>().Select(x => x.Header?.ToString()).SequenceEqual(["別のキー", "プロファイル", "ショートカット", "文字列", "アプリ・パス", "マクロ"]), "gesture manager follows the Windows title-bar theme and exposes six action types through compact icon commands for every direction and short press");
            var gestureActionRows = gestureSelectButtons.Select(button => (Grid)button.Parent).Distinct().ToArray();
            Check(gestureActionRows.Length == 5
                && gestureActionRows.All(row => row.ColumnDefinitions[^2].Width.Value >= 50 && row.ColumnDefinitions[^1].Width.Value >= 50)
                && gestureSlots.All(button => button.ActualWidth + button.Margin.Left + button.Margin.Right <= ((Grid)button.Parent).ColumnDefinitions[Grid.GetColumn(button)].ActualWidth + .1),
                "gesture row action columns reserve the complete icon-button width so the right edge is never clipped");
            gestureManager.GestureTitle.ApplyTemplate();
            var gestureTitleEditButton = (System.Windows.Controls.Button?)gestureManager.GestureTitle.Template.FindName("GestureTitleEditButton", gestureManager.GestureTitle);
            var gestureTitleDisplayText = (TextBlock?)gestureManager.GestureTitle.Template.FindName("GestureTitleDisplayText", gestureManager.GestureTitle);
            double titleTextRight = gestureTitleDisplayText?.TranslatePoint(new System.Windows.Point(gestureTitleDisplayText.ActualWidth, 0), gestureManager).X ?? double.NaN;
            double titlePencilLeft = gestureTitleEditButton?.TranslatePoint(new System.Windows.Point(), gestureManager).X ?? double.NaN;
            Check(gestureTitleDisplayText != null && gestureTitleEditButton != null && titlePencilLeft - titleTextRight is >= 6 and <= 12,
                $"gesture title pencil stays immediately beside the title instead of drifting to the pane edge (gap={titlePencilLeft - titleTextRight:F1})");
            Check(gestureManager.LockGestureCursorBox.Content?.ToString() == "カーソルを固定" && gestureManager.LockGestureCursorBox.IsChecked == false && !gestureManager.ResultGestures[0].LockCursorDuringGesture,
                "gesture editor shows the selected gesture's cursor behavior in the upper-right switch");
            gestureManager.LockGestureCursorBox.ApplyTemplate();
            var cursorLabel = Descendants<ContentPresenter>(gestureManager.LockGestureCursorBox).FirstOrDefault();
            var cursorTrack = Descendants<Border>(gestureManager.LockGestureCursorBox).FirstOrDefault(border => Math.Abs(border.ActualWidth - 42) < .1 && Math.Abs(border.ActualHeight - 24) < .1);
            double cursorLabelRight = cursorLabel?.TranslatePoint(new System.Windows.Point(cursorLabel.ActualWidth, 0), gestureManager).X ?? double.NaN;
            double cursorTrackLeft = cursorTrack?.TranslatePoint(new System.Windows.Point(), gestureManager).X ?? double.NaN;
            Check(cursorLabel != null && cursorTrack != null && cursorTrackLeft - cursorLabelRight is >= 8 and <= 16 && gestureManager.LockGestureCursorBox.ActualWidth < 170,
                $"cursor-lock label and switch stay compact instead of stretching across the header (gap={cursorTrackLeft - cursorLabelRight:F1}, width={gestureManager.LockGestureCursorBox.ActualWidth:F1})");
            Check(gestureManager.GestureThresholdBox.Text == "9", "gesture editor shows the selected gesture's movement threshold in the upper-right controls");
            gestureManager.GestureThresholdBox.Text = "17";
            gestureManager.GestureList.SelectedIndex = 1;
            gestureManager.UpdateLayout();
            Check(gestureManager.GestureThresholdBox.Text == "31" && gestureManager.LockGestureCursorBox.IsChecked == true
                && gestureManager.ResultGestures[0] is { GestureThresholdPixels: 17, LockCursorDuringGesture: false }
                && gestureManager.ResultGestures[1] is { GestureThresholdPixels: 31, LockCursorDuringGesture: true },
                "movement threshold and cursor behavior remain independent for each selected gesture");
            gestureManager.GestureThresholdBox.Text = "44";
            gestureManager.LockGestureCursorBox.IsChecked = false;
            gestureManager.GestureList.SelectedIndex = 0;
            gestureManager.UpdateLayout();
            Check(gestureManager.GestureThresholdBox.Text == "17" && gestureManager.LockGestureCursorBox.IsChecked == false
                && gestureManager.ResultGestures[1] is { GestureThresholdPixels: 44, LockCursorDuringGesture: false },
                "editing a second gesture never overwrites the first gesture's sensitivity or cursor choice");
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
            double emptyButtonTop = window.ActionPaletteButton.TranslatePoint(new System.Windows.Point(), window.AssignmentPane).Y;
            Check(window.AssignmentPane.BorderThickness == new Thickness(0) && window.AssignmentPane.CornerRadius == new CornerRadius(0) && window.AssignmentPane.Effect == null && ReferenceEquals(window.AssignmentPane.Background, ThemeService.Brush("AppBackground")) && window.InspectorEmptyState.VerticalAlignment == System.Windows.VerticalAlignment.Stretch && window.InspectorEmptyState.RenderTransform is TranslateTransform emptyStateShift && Math.Abs(emptyStateShift.Y + 96) <= .1 && Math.Abs(emptyTitleCenter.Y - (window.AssignmentPane.ActualHeight / 2 - 96)) <= 1.1 && emptyButtonTop >= 0, "the inspector lifts the complete empty-state composition another two-centimeter-equivalent step without clipping its input button");
            Check(!Descendants<TextBlock>(window.AssignmentPane).Any(text => text.Text == "インスペクター")
                && window.ActionPaletteButton.Content is TextBlock { Text: "\uE8FD" }
                && Math.Abs(window.ActionPaletteButton.ActualWidth - 68) < .1
                && Math.Abs(window.ActionPaletteButton.ActualHeight - 70) < .1
                && window.ActionPaletteButton.HorizontalAlignment == System.Windows.HorizontalAlignment.Center
                && ReferenceEquals(window.ActionPaletteButton.Parent, window.InspectorEmptyState)
                && Math.Abs(window.ActionPaletteButton.Margin.Top) < .1
                && Math.Abs(window.ActionPaletteButton.Margin.Bottom - 24) < .1
                && Math.Abs(window.ActionPaletteButton.TranslatePoint(new System.Windows.Point(window.ActionPaletteButton.ActualWidth / 2, 0), window.AssignmentPane).X - window.AssignmentPane.ActualWidth / 2) < 1
                && window.ActionPaletteButton.TranslatePoint(new System.Windows.Point(0, window.ActionPaletteButton.ActualHeight), window.AssignmentPane).Y < emptyTitleCenter.Y
                && window.ActionPaletteButton.ToolTip?.ToString() == "Action一覧を開く",
                "the inspector keeps a simple circular icon-only Action launcher with a shallow raised surface");
            string[] mainInspectorHintIcons = [window.InspectorHintOneIcon.Data.ToString(), window.InspectorHintTwoIcon.Data.ToString(), window.InspectorHintThreeIcon.Data.ToString()];
            Check(window.InspectorHintsPanel.HorizontalAlignment == System.Windows.HorizontalAlignment.Center
                && Math.Abs(window.InspectorHintsPanel.Margin.Top - 48) < .1
                && window.InspectorHintOneTitle.Text == "Actionを開く"
                && window.InspectorHintTwoTitle.Text == "ドラッグ"
                && window.InspectorHintThreeTitle.Text == "キーをクリック"
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
            if (!window.ConfigForTest.Macros.Any(macro => macro.Name == "お気に入りテスト"))
                window.ConfigForTest.Macros.Add(new MacroDefinition { Name = "お気に入りテスト", Steps = [new MacroStep { Event = "A Down" }, new MacroStep { Event = "A Up" }] });
            window.ConfigForTest.ActionPaletteFavorites.Clear();
            window.ConfigForTest.ActionPaletteRecentActions.Clear();
            window.SetActionPaletteApplicationsForTest(new InstalledApplicationInfo("Sample App", Environment.ProcessPath ?? "RELYR.exe", "テスト"));
            window.OpenActionPaletteForTest();
            Pump(window);
            Check(window.IsActionPaletteOpenForTest
                && window.ActionPalettePane.Visibility == Visibility.Visible
                && window.AssignmentScrollViewer.Visibility == Visibility.Collapsed
                && window.InspectorEmptyState.Visibility == Visibility.Collapsed
                && window.ActionPaletteActionsForTest.Count > 100
                && window.ActionPaletteActionsForTest.Any(action => action.Name == "コピー" && action.Value == "Ctrl+C")
                && window.ActionPaletteActionsForTest.Any(action => action.Category == "Deckパネル")
                && window.ActionPaletteCategoryBox.Items.Cast<object>().Select(item => item.ToString()).Take(4).SequenceEqual(new[] { "お気に入り", "最近使ったもの", "すべて", "使用中" })
                && window.ActionPaletteCategoryBox.SelectedItem?.ToString() == "最近使ったもの"
                && window.ActionPaletteCategoryBox.Items.Cast<object>().Count(item => item.ToString() == "使用中") == 1
                && window.ActionPaletteCategoryBox.Items.Cast<object>().Any(item => item.ToString() == "インストールアプリ")
                && window.ActionPaletteCategoryBox.Items.Cast<object>().Any(item => item.ToString() == "パス・文字列")
                && window.ActionPaletteCategoryBox.Items.Cast<object>().Any(item => item.ToString() == "キー")
                && window.ActionPaletteCategoryBox.Items.Cast<object>().Any(item => item.ToString() == "ショートカット")
                && window.ActionPaletteActionsForTest.Any(action => action is { Category: "パス・文字列", Kind: ActionKind.Text, ValueRequest: CatalogActionValueRequest.Text })
                && window.ActionPaletteActionsForTest.Any(action => action is { Category: "パス・文字列", Kind: ActionKind.Launch, ValueRequest: CatalogActionValueRequest.Launch })
                && window.ActionPaletteActionsForTest.Any(action => action.Category == "インストールアプリ" && action.Kind == ActionKind.Launch)
                && window.ActionPaletteActionsForTest.Count(action => action.Category == "キー" && action.Kind == ActionKind.Key) >= 90
                && window.ActionPaletteList.ActualWidth <= window.ActionPalettePane.ActualWidth + .1
                && window.ActionPaletteList.Items.Count == 0
                && window.ActionPaletteEmptyText.Visibility == Visibility.Visible
                && window.ActionPaletteEmptyText.Text.Contains("割り当てる", StringComparison.Ordinal)
                && window.ActionPaletteClearRecentButton.Visibility == Visibility.Visible
                && !window.ActionPaletteClearRecentButton.IsEnabled,
                "the fixed right pane opens a searchable concrete-action library without adding width or a second column");
            window.ActionPaletteCloseButton.ApplyTemplate();
            Check(window.ActionPaletteCloseButton.BorderThickness == new Thickness(0)
                && window.ActionPaletteCloseButton.Background is SolidColorBrush flatCloseBackground
                && flatCloseBackground.Color.A == 0
                && !Descendants<Border>(window.ActionPaletteCloseButton).Any(),
                "the Action header close control is a flat glyph with no enclosing border or filled button surface");
            Check(window.ActionPaletteSearchBox.ActualHeight >= window.ActionPaletteSearchBox.MinHeight
                && Math.Abs(window.ActionPaletteSearchBox.ActualHeight - 40) <= .1
                && Math.Abs(window.ActionPaletteSearchBox.ActualWidth - window.ActionPaletteCategoryBox.ActualWidth) <= 1.1
                && window.ActionPaletteResultCount.Visibility == Visibility.Collapsed
                && window.ActionPaletteCategoryBox.MaxDropDownHeight >= 500,
                "the Action search and compact category dropdown share one full-width edge");
            var categoryOptions = window.ActionPaletteCategoryBox.Items.Cast<MainWindow.ActionPaletteCategoryOption>().ToArray();
            Check(categoryOptions[0] is { Name: "お気に入り", Section: "ステータス", Tone: "favorite", StartsSection: true, ShowDivider: false }
                && categoryOptions.First(option => option.Name == "パス・文字列") is { Section: "作成", StartsSection: true, ShowDivider: true }
                && categoryOptions.First(option => option.Name == "キー") is { Section: "キー入力", StartsSection: true, ShowDivider: true }
                && categoryOptions.First(option => option.Name == "Windows") is { Section: "機能", StartsSection: true, ShowDivider: true }
                && categoryOptions.First(option => option.Name == "入力・編集") is { Section: "ショートカット", StartsSection: true, ShowDivider: true }
                && categoryOptions.Where(option => option.Name is "キー" or "マウス" or "ショートカット").All(option => option.Section == "キー入力")
                && categoryOptions.Where(option => option.Name is "Windows" or "Windowsアプリ" or "インストールアプリ" or "プロファイル" or "マクロ" or "ジェスチャー" or "Deckパネル" or "オーバーレイ" or "モニター").All(option => option.Section == "機能")
                && categoryOptions.Count(option => option.StartsSection) == 5
                && categoryOptions.All(option => !string.IsNullOrWhiteSpace(option.Glyph) && !string.IsNullOrWhiteSpace(option.Tone))
                && categoryOptions.First(option => option.Name == "キー").Tone == "key"
                && categoryOptions.First(option => option.Name == "プロファイル").Tone == "profile"
                && categoryOptions.First(option => option.Name == "マクロ").Tone == "macro"
                && categoryOptions.First(option => option.Name == "インストールアプリ").Tone == "launch",
                $"the shared Action category list uses the ordered status, input, feature, and shortcut sections in both main and Deck editors ({string.Join(", ", categoryOptions.Select(option => $"{option.Section}:{option.Name}"))})");
            window.ActionPaletteCategoryBox.ApplyTemplate();
            AppThemeMode categoryCaptureTheme = ThemeService.CurrentMode;
            ThemeService.Apply(AppThemeMode.Dark);
            Pump(window);
            window.ActionPaletteCategoryBox.IsDropDownOpen = true;
            window.UpdateLayout();
            var favoriteCategoryContainer = (ComboBoxItem)window.ActionPaletteCategoryBox.ItemContainerGenerator.ContainerFromItem(categoryOptions[0])!;
            var recentCategoryContainer = (ComboBoxItem)window.ActionPaletteCategoryBox.ItemContainerGenerator.ContainerFromItem(categoryOptions[1])!;
            favoriteCategoryContainer.ApplyTemplate();
            recentCategoryContainer.ApplyTemplate();
            var favoriteCategoryCheck = (TextBlock)favoriteCategoryContainer.Template.FindName("CategorySelectedCheck", favoriteCategoryContainer);
            var selectedCategoryCheck = (TextBlock)recentCategoryContainer.Template.FindName("CategorySelectedCheck", recentCategoryContainer);
            double categoryBottomInPane = window.ActionPaletteCategoryBox.TranslatePoint(
                new System.Windows.Point(0, window.ActionPaletteCategoryBox.ActualHeight), window.ActionPalettePane).Y;
            double expectedCategoryDropHeight = Math.Max(160, window.ActionPalettePane.ActualHeight - categoryBottomInPane - 12);
            Check(window.ActionPaletteCategoryBox is System.Windows.Controls.ComboBox
                && Descendants<System.Windows.Controls.Primitives.Popup>(window.ActionPaletteCategoryBox).Any()
                && favoriteCategoryCheck.Opacity == 0 && selectedCategoryCheck.Opacity == 1
                && Math.Abs(window.ActionPaletteCategoryBox.MaxDropDownHeight - expectedCategoryDropHeight) <= 1.1
                && window.ActionPaletteSelectedCategoryGlyph.Foreground is SolidColorBrush recentCategoryBrush
                && recentCategoryBrush.Color == ThemeService.Color("ActionProfileIconBrush"),
                "the Action categories use the full available pane height with a clear selected check mark and low-resolution-safe clamping");
            Check(CaptureElementForReview(window.ActionPaletteCategoryBox, "action-category-dropdown.png"), "the Action category dropdown screenshot is saved");
            window.ActionPaletteCategoryBox.IsDropDownOpen = false;
            ThemeService.Apply(categoryCaptureTheme);
            Pump(window);
            window.SelectActionPalettePopupItemForTest("お気に入り");
            Pump(window);
            Check(window.ActionPaletteSelectedCategoryGlyph.Foreground is SolidColorBrush favoriteCategoryBrush
                && favoriteCategoryBrush.Color == ThemeService.Color("ActionTextIconBrush"),
                "the closed Action category selector keeps the selected category's own icon color instead of forcing every status icon to green");
            window.SelectActionPalettePopupItemForTest("すべて");
            Pump(window);
            var favoriteCopyAction = window.ActionPaletteActionsForTest.First(action => action.Name == "コピー" && action.Value == "Ctrl+C");
            object favoriteCopyItem = window.ActionPaletteList.Items.Cast<object>().First(item =>
                item.GetType().GetProperty("Action")?.GetValue(item) is CatalogAction action
                && action.Kind == favoriteCopyAction.Kind
                && action.Value == favoriteCopyAction.Value);
            window.ActionPaletteList.ScrollIntoView(favoriteCopyItem);
            window.UpdateLayout();
            Pump(window);
            var favoriteCopyContainer = (ListBoxItem)window.ActionPaletteList.ItemContainerGenerator.ContainerFromItem(favoriteCopyItem)!;
            var favoriteCopyStar = Descendants<System.Windows.Controls.Button>(favoriteCopyContainer).Single(button => button.Name == "ActionFavoriteButton");
            window.ActionPaletteList.ApplyTemplate();
            var actionPaletteScrollViewer = (ScrollViewer)window.ActionPaletteList.Template.FindName("PART_ScrollViewer", window.ActionPaletteList);
            actionPaletteScrollViewer.ApplyTemplate();
            var actionPaletteScrollBar = (System.Windows.Controls.Primitives.ScrollBar)actionPaletteScrollViewer.Template.FindName("PART_VerticalScrollBar", actionPaletteScrollViewer);
            double favoriteStarRight = favoriteCopyStar.TranslatePoint(new System.Windows.Point(favoriteCopyStar.ActualWidth, 0), favoriteCopyContainer).X;
            double actionCardLeft = favoriteCopyContainer.TranslatePoint(new System.Windows.Point(), window.ActionPalettePane).X;
            double actionCardRight = favoriteCopyContainer.TranslatePoint(new System.Windows.Point(favoriteCopyContainer.ActualWidth, 0), window.ActionPalettePane).X;
            double actionScrollBarLeft = actionPaletteScrollBar.TranslatePoint(new System.Windows.Point(), window.ActionPalettePane).X;
            double actionScrollBarRight = actionPaletteScrollBar.TranslatePoint(new System.Windows.Point(actionPaletteScrollBar.ActualWidth, 0), window.ActionPalettePane).X;
            Check(ReferenceEquals(window.ActionPaletteList.Style, window.FindResource("ActionPaletteListStyle"))
                && actionPaletteScrollBar.ActualWidth <= 3.1
                && favoriteCopyStar.Content?.ToString() == "☆"
                && favoriteCopyContainer.ActualWidth - favoriteStarRight is >= 9 and <= 14
                && actionScrollBarLeft > actionCardRight
                && !Descendants<TextBlock>(favoriteCopyContainer).Any(text => text.Text == "⋮⋮"),
                $"every Action row ends with its favorite star while the dedicated thin scrollbar stays in a separate gutter (row={favoriteCopyContainer.ActualWidth:F1}, categories={window.ActionPaletteCategoryBox.ActualWidth:F1}, list={window.ActionPaletteList.ActualWidth:F1}, viewer={actionPaletteScrollViewer.ActualWidth:F1}, card={actionCardLeft:F1}..{actionCardRight:F1}, scroll={actionScrollBarLeft:F1}..{actionScrollBarRight:F1}, star gap={favoriteCopyContainer.ActualWidth - favoriteStarRight:F1})");
            favoriteCopyStar.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            var favoriteCandidates = new[]
            {
                window.ActionPaletteActionsForTest.Single(action => action.ValueRequest == CatalogActionValueRequest.Text),
                window.ActionPaletteActionsForTest.Single(action => action.ValueRequest == CatalogActionValueRequest.Launch),
                window.ActionPaletteActionsForTest.First(action => action is { Name: "Sample App", Kind: ActionKind.Launch }),
                window.ActionPaletteActionsForTest.First(action => action is { Kind: ActionKind.Macro, Value: "お気に入りテスト" }),
                window.ActionPaletteActionsForTest.First(action => action.Kind == ActionKind.Gesture),
                window.ActionPaletteActionsForTest.First(action => action.Kind == ActionKind.Shortcut && OverlayService.IsOverlayAction(action.Value))
            };
            foreach (var candidate in favoriteCandidates)
                window.ToggleActionPaletteFavoriteForTest(candidate);
            window.SelectActionPalettePopupItemForTest("お気に入り");
            Pump(window);
            var favoriteActions = window.VisibleActionPaletteActionsForTest.ToArray();
            Check(favoriteActions.Any(action => action.Value == "Ctrl+C")
                && favoriteActions.Any(action => action is { Kind: ActionKind.Text, ValueRequest: CatalogActionValueRequest.Text })
                && favoriteActions.Any(action => action is { Kind: ActionKind.Launch, ValueRequest: CatalogActionValueRequest.Launch })
                && favoriteActions.Any(action => action is { Name: "Sample App", Kind: ActionKind.Launch })
                && favoriteActions.Any(action => action.Kind == ActionKind.Macro)
                && favoriteActions.Any(action => action.Kind == ActionKind.Gesture)
                && favoriteActions.Any(action => action.Kind == ActionKind.Shortcut && OverlayService.IsOverlayAction(action.Value))
                && window.ConfigForTest.ActionPaletteFavorites.Count >= 7,
                "favorites contain only user-starred shortcuts, text, paths, installed apps, macros, gestures, and overlays, all as the original draggable Actions");
            CaptureForReview(window, "action-palette-favorites.png");
            var actionPaletteTemplate = (DataTemplate)window.Resources["ActionPaletteItemTemplate"];
            var actionPaletteTemplateRoot = (FrameworkElement)actionPaletteTemplate.LoadContent();
            var actionPaletteGlyph = (TextBlock)actionPaletteTemplateRoot.FindName("ActionPaletteItemGlyph");
            var actionPaletteTemplateGrid = (Grid)((Grid)actionPaletteTemplateRoot).Children[0];
            Check(actionPaletteGlyph.FontFamily.Source.StartsWith("Segoe UI Variable", StringComparison.Ordinal)
                && window.ActionPaletteSearchBox.TextAlignment == TextAlignment.Left
                && window.ActionPaletteSearchBox.FlowDirection == System.Windows.FlowDirection.LeftToRight
                && actionPaletteTemplateGrid.ColumnDefinitions.Count == 3
                && !Descendants<TextBlock>(actionPaletteTemplateRoot).Any(text => text.Text is "選択・検索" or "⋮⋮"),
                "Action rows render both literal letters and icon-font glyphs without tofu, omit the obsolete six-dot handle, and keep search input at the left edge");
            Check(MainWindow.ActionPaletteItemDetail(new CatalogAction("編集・クリップボード", "コピー", "", ActionKind.Shortcut, "Ctrl+C"), "入力・編集") == "Ctrl + C"
                && MainWindow.ActionPaletteItemDetail(new CatalogAction("画面キャプチャ", "画面全体をスクリーンショット", "", ActionKind.Key, "PrintScreen"), "Windows") == "PrintScreen"
                && MainWindow.ActionPaletteItemDetail(new CatalogAction("アプリ", "Sample App", "", ActionKind.Launch, "sample.exe"), "アプリ") == "アプリ"
                && MainWindow.ActionPaletteItemDetail(new CatalogAction("パス・文字列", "文字列を入力…", "", ActionKind.Text, "", CatalogActionValueRequest.Text), "パス・文字列") == "ドロップ後に指定"
                && MainWindow.ActionPaletteItemDetail(new CatalogAction("マクロ", "Sample Macro", "", ActionKind.Macro, "sample"), "マクロ") == "マクロ",
                "Action rows show the actual key or shortcut below keyboard actions while non-key actions keep a concise type label");
            window.ActionPaletteSearchBox.Text = "音量";
            window.ActionPaletteSearchBox.Focus();
            window.ActionPaletteSearchBox.CaretIndex = window.ActionPaletteSearchBox.Text.Length;
            Pump(window);
            var actionSearchContentHost = (ScrollViewer)window.ActionPaletteSearchBox.Template.FindName("PART_ContentHost", window.ActionPaletteSearchBox);
            double actionSearchHostX = actionSearchContentHost.TranslatePoint(new System.Windows.Point(), window.ActionPaletteSearchBox).X;
            CaptureForReview(window, "action-search-position.png");
            Check(actionSearchContentHost.HorizontalContentAlignment == System.Windows.HorizontalAlignment.Left
                && Math.Abs(actionSearchContentHost.Margin.Left - 30) < .1
                && actionSearchHostX >= 29 && actionSearchHostX <= 34
                && window.ActionPaletteSearchBox.TextAlignment == TextAlignment.Left,
                $"the Action search input host starts immediately after the search icon and keeps typed text left aligned (host={actionSearchHostX:F1}px)");
            window.ActionPaletteSearchBox.Clear();
            System.Windows.Input.Keyboard.ClearFocus();
            window.Focus();
            Pump(window);
            window.SelectActionPalettePopupItemForTest("キー");
            Pump(window);
            string[] orderedPaletteKeys = window.VisibleActionPaletteActionsForTest.Select(action => action.Value).ToArray();
            string[] expectedLetters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(letter => letter.ToString()).ToArray();
            string[] expectedDigits = Enumerable.Range(0, 10).Select(number => number.ToString()).ToArray();
            string[] expectedFunctionKeys = Enumerable.Range(1, 24).Select(number => $"F{number}").ToArray();
            int upKeyIndex = Array.IndexOf(orderedPaletteKeys, "Up");
            int leftKeyIndex = Array.IndexOf(orderedPaletteKeys, "Left");
            int downKeyIndex = Array.IndexOf(orderedPaletteKeys, "Down");
            int rightKeyIndex = Array.IndexOf(orderedPaletteKeys, "Right");
            Check(orderedPaletteKeys.Take(expectedLetters.Length).SequenceEqual(expectedLetters)
                && orderedPaletteKeys.Skip(expectedLetters.Length).Take(expectedDigits.Length).SequenceEqual(expectedDigits)
                && orderedPaletteKeys.Skip(expectedLetters.Length + expectedDigits.Length).Take(expectedFunctionKeys.Length).SequenceEqual(expectedFunctionKeys)
                && upKeyIndex >= 0 && upKeyIndex < leftKeyIndex && leftKeyIndex < downKeyIndex && downKeyIndex < rightKeyIndex,
                "the Key category begins with A-Z, then 0-9 and F1-F24 before the remaining special keys");
            window.ActionPaletteSearchBox.Text = "excel";
            Pump(window);
            bool clearButtonAppeared = window.ActionPaletteSearchClearButton.Visibility == Visibility.Visible;
            window.ActionPaletteSearchClearButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            var emptySearchClick = new System.Windows.Input.MouseButtonEventArgs(
                System.Windows.Input.Mouse.PrimaryDevice,
                Environment.TickCount,
                System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent
            };
            window.ActionPaletteSearchBox.RaiseEvent(emptySearchClick);
            Check(clearButtonAppeared
                && window.ActionPaletteSearchBox.Text.Length == 0
                && window.ActionPaletteSearchBox.CaretIndex == 0
                && window.ActionPaletteSearchHint.Visibility == Visibility.Visible
                && window.ActionPaletteSearchClearButton.Visibility == Visibility.Collapsed
                && emptySearchClick.Handled,
                "an empty Action search always starts at the left edge and a stable right-side clear button resets the query");
            window.SelectActionPalettePopupItemForTest("ショートカット");
            Pump(window);
            Check(window.ActionPaletteCustomShortcutButton.Visibility == Visibility.Visible,
                "the Shortcut category and its first-shortcut creation entry remain reachable before any custom shortcut exists");
            Check(Math.Abs(window.ActionPaletteCustomShortcutButton.ActualHeight - 36) < .1
                && Math.Abs(window.ActionPaletteCustomShortcutButton.ActualWidth - window.ActionPaletteList.ActualWidth) <= 1.1,
                "the custom shortcut creator keeps the compact pre-0.1.367 row size");
            window.AddActionPaletteShortcutForTest("Ctrl+Alt+K");
            Pump(window);
            Check(window.ActionPaletteCategoryBox.SelectedItem?.ToString() == "ショートカット"
                && window.ActionPaletteCustomShortcutButton.Visibility == Visibility.Visible
                && window.ActionPaletteActionsForTest.Any(action => action.Category == "任意のショートカット" && action.Kind == ActionKind.Shortcut && action.Value == "Ctrl+Alt+K"),
                "the shared keyboard and Deck Action pane can create a custom shortcut and expose the completed row for drag assignment");
            string[] mainActionCategories = window.ActionPaletteCategoryBox.Items.Cast<object>().Select(item => item.ToString()!).ToArray();
            foreach (string category in mainActionCategories)
            {
                window.SelectActionPalettePopupItemForTest(category);
                Pump(window);
            }
            Check(window.IsActionPaletteOpenForTest
                && window.ActionPaletteCategoryBox.SelectedItem?.ToString() == mainActionCategories[^1],
                "choosing every category from the permanent category list keeps the shared Action library open");
            window.SelectActionPalettePopupItemForTest(mainActionCategories[0]);
            Check(Math.Abs(MainWindow.ActionPaletteDragPreviewWidth(240) - 196.8) < .1
                && Math.Abs(MainWindow.ActionPaletteDragPreviewHeight - 42) < .1,
                "action drags use a compact whole-row preview instead of an ambiguous icon-only ghost");
            var previewWorkArea = new Rect(0, 0, 960, 540);
            var previewTarget = new Rect(390, 210, 96, 92);
            var previewCursor = new System.Windows.Point(438, 279);
            var previewPlacement = DeckDragPreviewWindow.CalculateAvoidingPlacement(
                previewCursor, new System.Windows.Size(220, 42), previewWorkArea, previewTarget);
            Rect[] edgeTargets =
            [
                new(0, 210, 96, 92),
                new(864, 210, 96, 92),
                new(390, 0, 96, 92),
                new(390, 448, 96, 92),
                new(0, 0, 96, 92),
                new(864, 448, 96, 92)
            ];
            bool everyEdgeAvoidsTarget = edgeTargets.All(target =>
            {
                var cursor = new System.Windows.Point(target.Left + target.Width / 2, target.Top + target.Height / 2);
                var placement = DeckDragPreviewWindow.CalculateAvoidingPlacement(
                    cursor, new System.Windows.Size(220, 42), previewWorkArea, target);
                return !placement.IntersectsWith(target) && previewWorkArea.Contains(placement);
            });
            Check(!previewPlacement.IntersectsWith(previewTarget)
                && previewWorkArea.Contains(previewPlacement)
                && everyEdgeAvoidsTarget,
                "the Action drag card stays inside the monitor and moves to another side instead of covering its drop key at the center, every edge, or a corner");
            Check(DeckPanelOverlayWindow.SupportsMonitorWheelAdjustment(DeckMonitorInteraction.Volume)
                && DeckPanelOverlayWindow.SupportsMonitorWheelAdjustment(DeckMonitorInteraction.Brightness)
                && !DeckPanelOverlayWindow.SupportsMonitorWheelAdjustment(DeckMonitorInteraction.TaskManager)
                && !DeckPanelOverlayWindow.SupportsMonitorWheelAdjustment(DeckMonitorInteraction.AutoExtractToggle),
                "volume and brightness Deck monitors accept wheel adjustment without making passive monitors consume scrolling");
            bool originalArchiveState = ArchiveAutomationState.Enabled;
            ArchiveAutomationState.Set(true);
            Check(ArchiveAutomationState.Reading() is { Text: "ON", Detail: "監視中", Level: 1 }
                && DeckMonitorCatalog.TryGet("auto-extract", out var autoExtractMonitor)
                && autoExtractMonitor.Interaction == DeckMonitorInteraction.AutoExtractToggle,
                "the auto-extraction Deck monitor exposes an immediately readable and clickable ON state");
            ArchiveAutomationState.Set(false);
            Check(ArchiveAutomationState.Reading() is { Text: "OFF", Detail: "停止中", Level: 0 },
                "the auto-extraction Deck monitor immediately reflects the OFF state");
            ArchiveAutomationState.Set(originalArchiveState);
            UiMotionService.Apply(true);
            Check(window.ExerciseFrozenActionLaunchMotionForTest(),
                "Action hover clones production-frozen template transforms before animation instead of terminating the resident process");
            var frozenMotionHost = new Border
            {
                RenderTransform = new TransformGroup
                {
                    Children = new TransformCollection
                    {
                        new ScaleTransform(1, 1),
                        new TranslateTransform()
                    }
                }
            };
            frozenMotionHost.RenderTransform.Freeze();
            var mutableMotion = UiMotionService.MutableMotionTransform(frozenMotionHost);
            Check(!frozenMotionHost.RenderTransform.IsFrozen
                && !mutableMotion.Scale.IsFrozen
                && !mutableMotion.Translate.IsFrozen,
                "shared content motion clones a frozen transform group before animating it");
            var frozenMotionProbe = new ScaleTransform(1, 1);
            frozenMotionProbe.Freeze();
            UiMotionService.RunSafely("frozen-transform-regression", () =>
                frozenMotionProbe.BeginAnimation(ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(1.05, TimeSpan.FromMilliseconds(10))));
            Check(!UiMotionService.Enabled,
                "an unexpected animation failure is contained and disables motion for the current process instead of escaping to WPF shutdown");
            UiMotionService.Apply(true);
            var dispatcherFrozenMotionProbe = new ScaleTransform(1, 1);
            dispatcherFrozenMotionProbe.Freeze();
            Exception? dispatcherAnimationFailure = null;
            try
            {
                dispatcherFrozenMotionProbe.BeginAnimation(
                    ScaleTransform.ScaleXProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(1.05, TimeSpan.FromMilliseconds(10)));
            }
            catch (Exception ex)
            {
                dispatcherAnimationFailure = ex;
            }
            Check(dispatcherAnimationFailure != null
                && UiMotionService.TryHandleDispatcherException(dispatcherAnimationFailure)
                && !UiMotionService.Enabled,
                "the dispatcher fail-safe recognizes an animation-only failure and keeps it out of the resident-process shutdown path");
            UiMotionService.Apply(true);
            window.ClickActionPaletteBlankForTest();
            Check(!window.IsActionPaletteOpenForTest,
                "one blank workspace click closes the action library without requiring a second click");
            window.OpenActionPaletteForTest();
            Pump(window);
            window.ClickVisualInputForTest("F22");
            Pump(window);
            Check(!window.IsActionPaletteOpenForTest
                && window.InputName.Text == "F22"
                && window.AssignmentScrollViewer.Visibility == Visibility.Visible
                && window.AssignmentEditor.Visibility == Visibility.Visible
                && window.HasAssignmentEditorRevealForTest,
                "one main-keyboard click replaces the open Action library with that key's assignment editor and gives the inspector a visible content-only arrival");
            window.ClickActionPaletteBlankForTest();
            Pump(window);
            window.CloseActionPaletteForTest();
            window.OpenActionPaletteForTest();
            Pump(window);
            window.ActionPaletteSearchBox.Text = "クリップボード履歴";
            Pump(window);
            Check(window.ActionPaletteSearchHint.Visibility == Visibility.Collapsed
                && window.ActionPaletteList.Items.Count >= 1
                && window.ActionPaletteList.Items.Cast<object>().All(item => item.GetType().GetProperty("Name")?.GetValue(item)?.ToString()?.Contains("クリップボード", StringComparison.Ordinal) == true),
                "the narrow action library filters concrete rows by search without wrapped descriptions");
            window.ActionPaletteSearchBox.Clear();
            var previousF24 = window.CurrentProfileForTest.Mappings.Where(mapping => mapping.Input == "F24").Select(mapping => mapping.Copy()).ToArray();
            var copyAction = window.ActionPaletteActionsForTest.First(action => action.Name == "コピー" && action.Value == "Ctrl+C");
            bool paletteApplied = window.ApplyPaletteActionForTest(copyAction, "F24", "F24");
            var f24Button = window.VisualInputButtonsForTest.First(button => button.IsVisible && string.Equals(button.Tag?.ToString(), "F24", StringComparison.OrdinalIgnoreCase));
            bool paletteDropMotionStarted = window.HasPaletteDropMotionForTest(f24Button);
            bool paletteDropWaveCentered = window.HasCenteredPaletteDropWaveForTest(f24Button);
            PumpFor(TimeSpan.FromMilliseconds(35));
            CaptureForReview(window, "action-drop-center-wave.png");
            Check(paletteApplied
                && window.IsActionPaletteOpenForTest
                && window.CurrentProfileForTest.Mappings.LastOrDefault(mapping => mapping.Input == "F24") is { Kind: ActionKind.Shortcut, Value: "Ctrl+C" }
                && window.ActionPaletteUndoBar.Visibility == Visibility.Visible
                && window.ActionPaletteUndoDurationForTest == TimeSpan.FromSeconds(5)
                && paletteDropMotionStarted
                && paletteDropWaveCentered
                && MainWindow.ActionDropWaveDurationMs == 500,
                "a palette drop replaces the target, expands a clearly readable half-second clipped accent wave from its exact center, and offers undo for five seconds");
            window.SelectActionPalettePopupItemForTest("最近使ったもの");
            Pump(window);
            Check(window.VisibleActionPaletteActionsForTest.FirstOrDefault() is { Kind: ActionKind.Shortcut, Value: "Ctrl+C" }
                && window.ConfigForTest.ActionPaletteRecentActions.FirstOrDefault() == "Shortcut:Ctrl+C"
                && window.ActionPaletteClearRecentButton.Visibility == Visibility.Visible
                && window.ActionPaletteClearRecentButton.IsEnabled,
                "a successful drag assignment places that concrete Action first in the shared recent list without changing its drag behavior");
            window.ActionPaletteClearRecentButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.ConfigForTest.ActionPaletteRecentActions.Count == 0
                && window.ActionPaletteList.Items.Count == 0
                && window.ActionPaletteClearRecentButton.Visibility == Visibility.Visible
                && !window.ActionPaletteClearRecentButton.IsEnabled,
                "the quiet recent-list footer clears only the shared recent history and stays available in both keyboard and Deck Action panes");
            window.ApplyUiAnimationsForTest(false);
            Check(window.InputTransformStableForTest(f24Button)
                && window.PaletteDropWaveSettledForTest(f24Button),
                "turning RELYR animations off immediately settles the center wave without changing the key surface");
            window.ApplyUiAnimationsForTest(true);
            AppThemeMode paletteTheme = ThemeService.CurrentMode;
            ThemeService.Apply(AppThemeMode.Light);
            Pump(window);
            bool lightPaletteReadable = window.ActionPaletteSearchBox.Foreground is SolidColorBrush lightSearchForeground
                && lightSearchForeground.Color == ThemeService.Color("PrimaryText")
                && window.ActionPaletteUndoText.Foreground is SolidColorBrush lightUndoForeground
                && lightUndoForeground.Color == ThemeService.Color("PrimaryText")
                && window.ActionPaletteUndoText.Opacity == 1;
            ThemeService.Apply(AppThemeMode.Dark);
            Pump(window);
            bool darkPaletteReadable = window.ActionPaletteSearchBox.Foreground is SolidColorBrush darkSearchForeground
                && darkSearchForeground.Color == ThemeService.Color("PrimaryText")
                && window.ActionPaletteUndoText.Foreground is SolidColorBrush darkUndoForeground
                && darkUndoForeground.Color == ThemeService.Color("PrimaryText")
                && window.ActionPaletteUndoText.Opacity == 1;
            ThemeService.Apply(paletteTheme);
            Check(lightPaletteReadable && darkPaletteReadable,
                "action motion leaves labels on the primary readable foreground in both light and dark themes");
            window.UndoPaletteActionForTest();
            Pump(window);
            var restoredF24 = window.CurrentProfileForTest.Mappings.Where(mapping => mapping.Input == "F24").ToArray();
            Check(restoredF24.Length == previousF24.Length
                && restoredF24.Zip(previousF24).All(pair => pair.First.Kind == pair.Second.Kind && pair.First.Value == pair.Second.Value && pair.First.LongPressKind == pair.Second.LongPressKind && pair.First.LongPressValue == pair.Second.LongPressValue)
                && window.ActionPaletteUndoBar.Visibility == Visibility.Collapsed,
                "palette undo restores the complete previous action instead of merely clearing the replacement");
            var dualAssignmentPreviousF24 = window.CurrentProfileForTest.Mappings.Where(mapping => mapping.Input == "F24").Select(mapping => mapping.Copy()).ToArray();
            window.CurrentProfileForTest.Mappings.RemoveAll(mapping => mapping.Input == "F24");
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "F24", Layer = "通常", Kind = ActionKind.Shortcut, Value = "Ctrl+C" });
            window.ColorButtonsForTest();
            Pump(window);
            f24Button = window.VisualInputButtonsForTest.First(button => button.IsVisible
                && PresentationSource.FromVisual(button) != null
                && string.Equals(button.Tag?.ToString(), "F24", StringComparison.OrdinalIgnoreCase));
            var enterAction = window.ActionPaletteActionsForTest.First(action => action.Kind == ActionKind.Key && action.Value == "Enter");
            double dualKeyWidth = f24Button.ActualWidth;
            double dualKeyHeight = f24Button.ActualHeight;
            window.ShowActionPaletteDragPreviewForTest();
            window.SetPaletteAssignmentDropTargetForTest(f24Button, enterAction, longPress: false);
            f24Button.ApplyTemplate();
            var slotOverlay = (UIElement)f24Button.Template.FindName("AssignmentSlotOverlay", f24Button)!;
            var shortDropZone = (Border)f24Button.Template.FindName("ShortPressDropZone", f24Button)!;
            var longDropZone = (Border)f24Button.Template.FindName("LongPressDropZone", f24Button)!;
            var shortDropGlow = (Border)f24Button.Template.FindName("ShortPressDropGlow", f24Button)!;
            var longDropGlow = (Border)f24Button.Template.FindName("LongPressDropGlow", f24Button)!;
            var shortDropLabel = (TextBlock)f24Button.Template.FindName("ShortPressDropLabel", f24Button)!;
            var longDropLabel = (TextBlock)f24Button.Template.FindName("LongPressDropLabel", f24Button)!;
            var activeDropScale = UiMotionService.MutableScale(f24Button);
            Check(slotOverlay.Opacity == 1
                && shortDropZone.Background is SolidColorBrush { Color.A: 0 }
                && longDropZone.Background is SolidColorBrush { Color.A: 0 }
                && slotOverlay.ClipToBounds && shortDropZone.ClipToBounds && longDropZone.ClipToBounds
                && shortDropLabel.Text == "TAP" && longDropLabel.Text == "HOLD"
                && Math.Abs(shortDropLabel.FontSize - 9) < .001
                && Math.Abs(longDropLabel.FontSize - shortDropLabel.FontSize) < .001
                && shortDropGlow.Opacity > .7 && longDropGlow.Opacity == 0
                && Math.Abs(activeDropScale.ScaleX - MainWindow.AssignmentDropTargetScale) < .001
                && Math.Abs(activeDropScale.ScaleY - MainWindow.AssignmentDropTargetScale) < .001
                && (activeDropScale.ScaleX - 1) * dualKeyWidth / 2 < dualKeyWidth * .1,
                "an Action hovering even a compact key reveals label-only TAP/HOLD halves with a soft selected glow and enlarges the hit target without hiding a neighboring key");
            window.SetPaletteAssignmentDropTargetForTest(f24Button, enterAction, longPress: true);
            var (splitPreviewBounds, splitTargetBounds) = window.PositionActionPaletteDragPreviewForTest(f24Button);
            CaptureForReview(window, "action-drop-split-long.png");
            f24Button.ApplyTemplate();
            slotOverlay = (UIElement)f24Button.Template.FindName("AssignmentSlotOverlay", f24Button)!;
            longDropZone = (Border)f24Button.Template.FindName("LongPressDropZone", f24Button)!;
            shortDropGlow = (Border)f24Button.Template.FindName("ShortPressDropGlow", f24Button)!;
            longDropGlow = (Border)f24Button.Template.FindName("LongPressDropGlow", f24Button)!;
            Check(shortDropGlow.Opacity == 0 && longDropGlow.Opacity > .7
                && slotOverlay.Visibility == Visibility.Visible
                && Math.Abs(f24Button.ActualWidth - dualKeyWidth) < .001
                && Math.Abs(f24Button.ActualHeight - dualKeyHeight) < .001
                && !splitPreviewBounds.IsEmpty && !splitTargetBounds.IsEmpty
                && !splitPreviewBounds.IntersectsWith(splitTargetBounds),
                $"moving through the same key highlights its lower long-press half without changing hit-test geometry, while the Action card stays completely outside the key (preview={splitPreviewBounds}, target={splitTargetBounds}, overlay={slotOverlay.Visibility}, size={f24Button.ActualWidth:F1}x{f24Button.ActualHeight:F1})");
            window.DismissActionPaletteDragPreviewForTest();
            window.ClearAssignmentDropTargetForTest();
            var jisEnterDropTarget = window.VisualInputButtonsForTest.First(button => button.IsVisible
                && Equals(button.Tag, "Enter")
                && Equals(button.Style, window.FindResource("JisEnterButton")));
            window.SetPaletteAssignmentDropTargetForTest(jisEnterDropTarget, enterAction, longPress: true);
            jisEnterDropTarget.ApplyTemplate();
            var enterDropOutline = (System.Windows.Shapes.Path)jisEnterDropTarget.Template.FindName("AssignmentSlotOutline", jisEnterDropTarget)!;
            var enterTapLabel = (TextBlock)jisEnterDropTarget.Template.FindName("ShortPressDropLabel", jisEnterDropTarget)!;
            var enterHoldLabel = (TextBlock)jisEnterDropTarget.Template.FindName("LongPressDropLabel", jisEnterDropTarget)!;
            var enterTapGlow = (Border)jisEnterDropTarget.Template.FindName("ShortPressDropGlow", jisEnterDropTarget)!;
            var enterHoldGlow = (Border)jisEnterDropTarget.Template.FindName("LongPressDropGlow", jisEnterDropTarget)!;
            CaptureForReview(window, "action-drop-jis-enter.png");
            Check(enterDropOutline.Stroke is SolidColorBrush enterOutlineBrush && enterOutlineBrush.Color == ThemeService.Color("AccentBrush")
                && Math.Abs(enterDropOutline.StrokeThickness - 3) < .001
                && enterDropOutline.StrokeLineJoin == PenLineJoin.Round
                && Math.Abs(enterDropOutline.Data.Bounds.Width - 160) < .1
                && Math.Abs(enterDropOutline.Data.Bounds.Height - 106.86) < .01
                && Math.Abs(enterTapLabel.FontSize - shortDropLabel.FontSize) < .001
                && Math.Abs(enterHoldLabel.FontSize - longDropLabel.FontSize) < .001
                && Math.Abs(enterTapGlow.Width - shortDropGlow.Width) < .001
                && Math.Abs(enterTapGlow.Height - shortDropGlow.Height) < .001
                && Math.Abs(enterHoldGlow.Width - longDropGlow.Width) < .001
                && Math.Abs(enterHoldGlow.Height - longDropGlow.Height) < .001,
                "the JIS Enter preview redraws its complete rounded outline while TAP/HOLD type and glow sizes remain identical to every key");
            window.ClearAssignmentDropTargetForTest();
            window.SetPaletteAssignmentDropTargetForTest(window.MouseLeftVisual, enterAction, longPress: false);
            Check(System.Windows.Controls.Panel.GetZIndex(window.MouseHost) == 50 && window.MouseLeftVisual.Opacity == 1,
                "an enlarged mouse drop target raises its complete opaque host above the mouse section title");
            CaptureForReview(window, "action-drop-mouse-zorder.png");
            window.ClearAssignmentDropTargetForTest();
            Check(System.Windows.Controls.Panel.GetZIndex(window.MouseHost) == 1,
                "clearing a mouse drop target restores the mouse host's normal stacking order");
            window.TaskbarLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.SetPaletteAssignmentDropTargetForTest(window.MouseLeftVisual, enterAction, longPress: false);
            window.MouseLeftVisual.ApplyTemplate();
            var reservedTaskbarTapMark = (UIElement)window.MouseLeftVisual.Template.FindName("ShortPressDropUnavailableMark", window.MouseLeftVisual)!;
            var reservedTaskbarHoldMark = (UIElement)window.MouseLeftVisual.Template.FindName("LongPressDropUnavailableMark", window.MouseLeftVisual)!;
            bool taskbarTapApplied = window.ApplyPaletteActionForTest(enterAction, "Taskbar+MouseLeft", "MouseLeft", longPress: false);
            bool taskbarHoldApplied = window.ApplyPaletteActionForTest(enterAction, "Taskbar+MouseLeft", "MouseLeft", longPress: true);
            var taskbarLeftMapping = window.CurrentProfileForTest.Mappings.LastOrDefault(mapping => mapping.Input == "Taskbar+MouseLeft");
            var staleAppliedTaskbarLeftHold = new Mapping { Input = "Taskbar+MouseLeft", Layer = "Taskbar", Kind = ActionKind.None, LongPressKind = ActionKind.Key, LongPressValue = "F9" };
            window.AddAppliedMappingForTest(staleAppliedTaskbarLeftHold);
            bool staleTaskbarLeftRuntimeIntercepted = window.RuntimeInterceptsInputForTest("Taskbar+MouseLeft");
            window.RemoveAppliedMappingForTest(staleAppliedTaskbarLeftHold);
            Check(!MainWindow.GetIsShortPressAssignmentDropAvailable(window.MouseLeftVisual)
                && !MainWindow.GetIsLongPressAssignmentDropAvailable(window.MouseLeftVisual)
                && reservedTaskbarTapMark.Opacity == 1 && reservedTaskbarHoldMark.Opacity == 1
                && !taskbarTapApplied && !taskbarHoldApplied
                && taskbarLeftMapping == null
                && !staleTaskbarLeftRuntimeIntercepted
                && InputAssignmentPolicy.UnavailableInputReason("Taskbar+MouseLeft") == "タスクバーの左クリック／ドラッグはWindows専用です",
                "Taskbar+MouseLeft blocks both TAP and HOLD so Windows keeps native click and pinned-app drag/reorder behavior");
            window.ClearAssignmentDropTargetForTest();
            window.CurrentProfileForTest.Mappings.RemoveAll(mapping => mapping.Input == "Taskbar+MouseLeft");
            window.SetPaletteAssignmentDropTargetForTest(window.MouseRightVisual, enterAction, longPress: false);
            window.MouseRightVisual.ApplyTemplate();
            var reservedTaskbarRightTapMark = (UIElement)window.MouseRightVisual.Template.FindName("ShortPressDropUnavailableMark", window.MouseRightVisual)!;
            bool taskbarRightTapApplied = window.ApplyPaletteActionForTest(enterAction, "Taskbar+MouseRight", "MouseRight", longPress: false);
            bool taskbarRightHoldApplied = window.ApplyPaletteActionForTest(enterAction, "Taskbar+MouseRight", "MouseRight", longPress: true);
            var taskbarRightMapping = window.CurrentProfileForTest.Mappings.LastOrDefault(mapping => mapping.Input == "Taskbar+MouseRight");
            Check(!MainWindow.GetIsShortPressAssignmentDropAvailable(window.MouseRightVisual)
                && MainWindow.GetIsLongPressAssignmentDropAvailable(window.MouseRightVisual)
                && reservedTaskbarRightTapMark.Opacity == 1
                && !taskbarRightTapApplied && taskbarRightHoldApplied
                && taskbarRightMapping is { Kind: ActionKind.None, Value: "", LongPressKind: ActionKind.Key, LongPressValue: "Enter" },
                "Taskbar+MouseRight keeps the Windows app-icon menu TAP visibly reserved while the same split target still accepts a HOLD Action");
            window.ClearAssignmentDropTargetForTest();
            window.CurrentProfileForTest.Mappings.RemoveAll(mapping => mapping.Input == "Taskbar+MouseRight");
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            bool longPaletteApplied = window.ApplyPaletteActionForTest(enterAction, "F24", "F24", longPress: true);
            Pump(window);
            var dualF24 = window.CurrentProfileForTest.Mappings.Last(mapping => mapping.Input == "F24");
            f24Button = window.VisualInputButtonsForTest.First(button => button.IsVisible && string.Equals(button.Tag?.ToString(), "F24", StringComparison.OrdinalIgnoreCase));
            f24Button.ApplyTemplate();
            var longBadge = (Border)f24Button.Template.FindName("LongPressBadge", f24Button)!;
            var longBadgeText = (TextBlock)f24Button.Template.FindName("LongPressBadgeText", f24Button)!;
            CaptureForReview(window, "dual-assignment-key.png");
            f24Button.ApplyTemplate();
            longBadge = (Border)f24Button.Template.FindName("LongPressBadge", f24Button)!;
            longBadgeText = (TextBlock)f24Button.Template.FindName("LongPressBadgeText", f24Button)!;
            var assignmentToolTip = (System.Windows.Controls.ToolTip)f24Button.ToolTip!;
            var assignmentToolTipText = Descendants<TextBlock>((DependencyObject)assignmentToolTip.Content).Select(text => text.Text).ToArray();
            assignmentToolTip.IsOpen = true;
            Pump(window);
            CaptureForReview(window, "assignment-hover-card.png");
            assignmentToolTip.IsOpen = false;
            Check(longPaletteApplied
                && dualF24 is { Kind: ActionKind.Shortcut, Value: "Ctrl+C", LongPressKind: ActionKind.Key, LongPressValue: "Enter" }
                && f24Button.Background is SolidColorBrush dualShortBrush && dualShortBrush.Color == MainWindow.AssignmentColorFor(new Mapping { Kind = ActionKind.Shortcut, Value = "Ctrl+C" })
                && MainWindow.GetHasDualPressAssignment(f24Button)
                && longBadge.Visibility == Visibility.Visible && longBadge.Background is SolidColorBrush dualLongBrush && dualLongBrush.Color == MainWindow.AssignmentColorFor(new Mapping { Kind = ActionKind.Key, Value = "Enter" })
                && longBadgeText.Text == "HOLD"
                && f24Button.Template.FindName("LongPressBand", f24Button) == null
                && f24Button.Template.FindName("LongPressStateIcon", f24Button) == null
                && assignmentToolTip.Style == window.FindResource("AssignmentHoverToolTipStyle")
                && assignmentToolTip.PlacementTarget == f24Button
                && assignmentToolTipText.Contains("TAP") && assignmentToolTipText.Contains("HOLD") && assignmentToolTipText.Contains("コピー") && assignmentToolTipText.Contains("Enter")
                && !assignmentToolTipText.Any(text => text.Contains("アクション：", StringComparison.Ordinal) || text.Contains("実行内容：", StringComparison.Ordinal)),
                $"dropping on the lower half preserves the short action and shows only a colored HOLD badge plus a rounded two-row Action card (applied={longPaletteApplied}, dual={MainWindow.GetHasDualPressAssignment(f24Button)}, long={MainWindow.GetHasLongPressAssignment(f24Button)}, badge={longBadge.Visibility}/{longBadge.Background}, face={f24Button.Background})");
            window.ClickVisualInputForTest("F24");
            Pump(window);
            Check(window.AssignmentTapFavoriteButton.Visibility == Visibility.Visible
                && window.AssignmentHoldFavoriteButton.Visibility == Visibility.Visible
                && window.AssignmentTapCard.Cursor == System.Windows.Input.Cursors.Hand
                && window.AssignmentHoldCard.Cursor == System.Windows.Input.Cursors.Hand,
                "configured TAP and HOLD summaries expose independent draggable cards and favorite stars");
            var starPress = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                Source = window.AssignmentTapFavoriteButton
            };
            window.AssignmentTapFavoriteButton.RaiseEvent(starPress);
            Check(!window.IsAssignmentActionDragArmedForTest,
                "pressing a summary favorite star never arms the Action drag gesture");
            var previousF23BeforeMove = window.CurrentProfileForTest.Mappings.Where(mapping => mapping.Input == "F23").Select(mapping => mapping.Copy()).ToArray();
            bool summaryActionMoved = window.MoveAssignedActionForTest("F24", sourceLongPress: false, targetInput: "F23", targetKey: "F23", targetLongPress: true);
            var movedSourceF24 = window.CurrentProfileForTest.Mappings.Last(mapping => mapping.Input == "F24");
            var movedTargetF23 = window.CurrentProfileForTest.Mappings.Last(mapping => mapping.Input == "F23");
            Check(summaryActionMoved
                && !MainWindow.HasConfiguredShortAction(movedSourceF24)
                && movedSourceF24 is { LongPressKind: ActionKind.Key, LongPressValue: "Enter" }
                && movedTargetF23 is { LongPressKind: ActionKind.Shortcut, LongPressValue: "Ctrl+C" },
                "dragging a summary TAP Action to another key's HOLD moves only that slot after the valid drop succeeds");
            window.UndoPaletteActionForTest();
            var restoredMoveF24 = window.CurrentProfileForTest.Mappings.Last(mapping => mapping.Input == "F24");
            var restoredMoveF23 = window.CurrentProfileForTest.Mappings.Where(mapping => mapping.Input == "F23").ToArray();
            Check(restoredMoveF24 is { Kind: ActionKind.Shortcut, Value: "Ctrl+C", LongPressKind: ActionKind.Key, LongPressValue: "Enter" }
                && restoredMoveF23.Length == previousF23BeforeMove.Length
                && restoredMoveF23.Zip(previousF23BeforeMove).All(pair => pair.First.Kind == pair.Second.Kind && pair.First.Value == pair.Second.Value && pair.First.LongPressKind == pair.Second.LongPressKind && pair.First.LongPressValue == pair.Second.LongPressValue),
                "undo restores both the source and overwritten destination after moving a summary Action");
            var normalAlphabetButton = window.VisualInputButtonsForTest.First(button => Equals(button.Tag, "A"));
            window.SetPaletteAssignmentDropTargetForTest(normalAlphabetButton, enterAction, longPress: true);
            normalAlphabetButton.ApplyTemplate();
            var unavailableLongMark = (UIElement)normalAlphabetButton.Template.FindName("LongPressDropUnavailableMark", normalAlphabetButton)!;
            var unavailableLongZone = (Border)normalAlphabetButton.Template.FindName("LongPressDropZone", normalAlphabetButton)!;
            var unavailableLongLabel = (TextBlock)normalAlphabetButton.Template.FindName("LongPressDropLabel", normalAlphabetButton)!;
            Check(unavailableLongMark.Opacity == 1
                && unavailableLongZone.Background is SolidColorBrush { Color.A: 0 }
                && unavailableLongLabel.Opacity < .3,
                "a key that cannot execute long press keeps its lower half disabled instead of accepting a fake assignment");
            window.ClearAssignmentDropTargetForTest();
            window.UndoPaletteActionForTest();
            window.CurrentProfileForTest.Mappings.RemoveAll(mapping => mapping.Input == "F24");
            window.CurrentProfileForTest.Mappings.AddRange(dualAssignmentPreviousF24.Select(mapping => mapping.Copy()));
            window.ColorButtonsForTest();
            Pump(window);
            var textTemplateAction = window.ActionPaletteActionsForTest.Single(action => action.ValueRequest == CatalogActionValueRequest.Text);
            var launchTemplateAction = window.ActionPaletteActionsForTest.Single(action => action.ValueRequest == CatalogActionValueRequest.Launch);
            var previousF23 = window.CurrentProfileForTest.Mappings.Where(mapping => mapping.Input == "F23").Select(mapping => mapping.Copy()).ToArray();
            window.ShowActionPaletteDragPreviewForTest();
            bool parameterResolverSawDragPreview = true;
            window.SetActionPaletteValueResolverForTest(_ =>
            {
                parameterResolverSawDragPreview = window.IsActionPaletteDragPreviewVisibleForTest;
                return null;
            });
            bool cancelledTemplateDrop = window.ApplyPaletteActionForTest(textTemplateAction, "F23", "F23");
            var cancelledF23 = window.CurrentProfileForTest.Mappings.Where(mapping => mapping.Input == "F23").ToArray();
            Check(!cancelledTemplateDrop
                && !window.CanUndoPaletteActionForTest
                && !parameterResolverSawDragPreview
                && !window.IsActionPaletteDragPreviewVisibleForTest
                && cancelledF23.Length == previousF23.Length
                && cancelledF23.Zip(previousF23).All(pair => pair.First.Kind == pair.Second.Kind && pair.First.Value == pair.Second.Value),
                "pointer release removes the drag preview before the parameter dialog, and cancelling makes no mapping, save, or undo-state change");
            const string multilineText = "こんにちは\r\nRELYR";
            window.SetActionPaletteValueResolverForTest(action => action.ValueRequest == CatalogActionValueRequest.Text ? multilineText : null);
            bool textTemplateApplied = window.ApplyPaletteActionForTest(textTemplateAction, "F23", "F23");
            Check(textTemplateApplied
                && window.CurrentProfileForTest.Mappings.LastOrDefault(mapping => mapping.Input == "F23") is { Kind: ActionKind.Text, Value: multilineText },
                "dropping the Text creation Action preserves its confirmed multiline Unicode value and uses the normal assignment transaction");
            window.UndoPaletteActionForTest();
            const string launchPath = @"C:\テスト フォルダー\sample file.txt";
            window.SetActionPaletteValueResolverForTest(action => action.ValueRequest == CatalogActionValueRequest.Launch ? $"  {launchPath}  " : null);
            bool launchTemplateApplied = window.ApplyPaletteActionForTest(launchTemplateAction, "F23", "F23");
            Check(launchTemplateApplied
                && window.CurrentProfileForTest.Mappings.LastOrDefault(mapping => mapping.Input == "F23") is { Kind: ActionKind.Launch, Value: launchPath },
                "dropping the Path creation Action accepts a Japanese path with spaces and commits one trimmed concrete launch value");
            window.UndoPaletteActionForTest();
            window.SetActionPaletteValueResolverForTest(null);
            CaptureForReview(window, "action-palette.png");
            window.CloseActionPaletteForTest();
            Pump(window);
            var malformedPaletteMapping = new Mapping { Input = "F23", Kind = ActionKind.Disabled, Value = null! };
            var malformedPaletteMacro = new MacroDefinition { Name = "破損値テスト", Steps = null! };
            var malformedPaletteDeck = new DeckLayoutDefinition { Id = null!, Name = "破損Deck", Mappings = null! };
            window.CurrentProfileForTest.Mappings.Add(malformedPaletteMapping);
            window.ConfigForTest.Macros.Add(malformedPaletteMacro);
            window.ConfigForTest.DeckLayouts.Add(malformedPaletteDeck);
            window.OpenActionPaletteForTest();
            Pump(window);
            Check(window.IsActionPaletteOpenForTest
                && window.ActionPalettePane.Visibility == Visibility.Visible
                && window.ActionPaletteActionsForTest.Any(action => action.Name == "破損値テスト"),
                "the Action button stays alive and opens the library when imported/runtime data contains null action values, macro steps, or Deck identifiers");
            window.CloseActionPaletteForTest();
            window.CurrentProfileForTest.Mappings.Remove(malformedPaletteMapping);
            window.ConfigForTest.Macros.Remove(malformedPaletteMacro);
            window.ConfigForTest.DeckLayouts.Remove(malformedPaletteDeck);
            Pump(window);
            for (int iteration = 0; iteration < 25; iteration++)
            {
                window.OpenActionPaletteForTest();
                Pump(window);
                window.CloseActionPaletteAnimatedForTest();
                Pump(window);
            }
            window.OpenActionPaletteForTest();
            Pump(window);
            Check(window.IsActionPaletteOpenForTest && window.ActionPalettePane.Visibility == Visibility.Visible,
                "repeated animated Action opens and closes cannot let an older completion callback close the current library or terminate the UI process");
            window.CloseActionPaletteForTest();
            Pump(window);
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
            var closeSettings = new SettingsWindow(new AppConfig { GestureThresholdPixels = 14, ClockBackgroundMode = ClockBackgroundMode.Solid, ClockDisplayMode = ClockDisplayMode.FullDateAndTime, ClockBackgroundImage = @"C:\Images\clock.png", ClockSolidColor = "#123456", ClockShowOnAllMonitors = false, InputPanelOpacityPercent = 67, DeckAfterActionBehavior = DeckAutoDismissBehavior.Hide, DeckPointerLeaveBehavior = DeckAutoDismissBehavior.CollapseToEdge });
            closeSettings.Show();
            closeSettings.UpdateLayout();
            Check(closeSettings.ActiveWindowTargetBox.Content?.ToString() == "アクティブなウィンドウ" && closeSettings.CursorWindowTargetBox.Content?.ToString() == "マウスカーソル下のウィンドウ" && closeSettings.ActiveWindowTargetBox.IsChecked == true && closeSettings.CursorWindowTargetBox.IsChecked == false, "settings provides one clear target choice for close, maximize, snap, and other window actions");
            closeSettings.CursorWindowTargetBox.IsChecked = true;
            Check(closeSettings.SelectedWindowActionTarget == WindowActionTarget.WindowUnderCursor, "window-under-cursor target can be selected without changing the action itself");
            Check(closeSettings.FindName("GestureThresholdBox") == null && closeSettings.FindName("LockGestureCursorBox") == null
                && !Descendants<TextBlock>(closeSettings).Any(text => text.Text is "ジェスチャー感度" or "方向を確定する移動量" or "ジェスチャー中にカーソルを固定する"),
                "layer settings no longer duplicate sensitivity or cursor behavior now owned by each gesture");
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
            CaptureForReview(firstRun, "tutorial-page-1.png");
            Check(!Descendants<TextBlock>(firstRun).Any(x => x.Text.Contains("自動起動", StringComparison.Ordinal) || x.Text.Contains("サインイン時", StringComparison.Ordinal)), "first-run tutorial does not duplicate the installer startup choice");
            Check(firstRun.TitleBarUsesDarkMode == MainWindow.IsWindowsAppDarkMode() && firstRun.UsesDarkPalette == MainWindow.IsWindowsAppDarkMode(), "tutorial content and title bar follow the Windows app theme");
            Check(firstRun.TutorialAppIcon.Source != null, "tutorial header uses the packaged RELYR application icon");
            Check(Descendants<System.Windows.Controls.Image>(firstRun).Count(x => x.Source != null) >= 12, "tutorial loads the supplied screenshots and packaged RELYR icon");
            Check(Descendants<TextBlock>(firstRun.PageOne).Any(x => x.Text == "レイヤーを選ぶ")
                && Descendants<TextBlock>(firstRun.PageOne).Any(x => x.Text == "Actionを開く")
                && Descendants<TextBlock>(firstRun.PageOne).Any(x => x.Text == "キーへドラッグ"),
                "tutorial starts with the three short assignment steps");
            Check(firstRun.CurrentPage == 0 && firstRun.PageCounterText.Text == "1 / 5"
                && firstRun.PageOne.Visibility == Visibility.Visible
                && new[] { firstRun.PageTwo, firstRun.PageThree, firstRun.PageFour, firstRun.PageFive }.All(page => page.Visibility == Visibility.Collapsed),
                "tutorial opens on page one of the five-page visual guide");
            firstRun.ShowPageForTest(1);
            firstRun.UpdateLayout();
            CaptureForReview(firstRun, "tutorial-page-2.png");
            Check(firstRun.PageTwo.Visibility == Visibility.Visible && firstRun.BackButton.Visibility == Visibility.Visible
                && firstRun.NextButton.Content?.ToString() == "次へ"
                && Descendants<TextBlock>(firstRun.PageTwo).Any(x => x.Text == "パス・文字列"),
                "tutorial explains assignment details without a text-heavy page");
            firstRun.ShowPageForTest(2);
            firstRun.UpdateLayout();
            CaptureForReview(firstRun, "tutorial-page-3.png");
            Check(firstRun.PageThree.Visibility == Visibility.Visible
                && Descendants<TextBlock>(firstRun.PageThree).Any(x => x.Text == "Spaceを2回続けて押し、2回目を押したままにする")
                && Descendants<TextBlock>(firstRun.PageThree).Any(x => x.Text == "変更後はWindowsを再起動"),
                "tutorial states the exact Space repeat gesture and CapsLock restart requirement");
            firstRun.ShowPageForTest(3);
            firstRun.UpdateLayout();
            CaptureForReview(firstRun, "tutorial-page-4.png");
            Check(firstRun.PageFour.Visibility == Visibility.Visible
                && Descendants<TextBlock>(firstRun.PageFour).Any(x => x.Text == "初期値は自動保存オン。")
                && Descendants<TextBlock>(firstRun.PageFour).Any(x => x.Text.Contains("設定 → 一般 → 保存", StringComparison.Ordinal)),
                "tutorial explains the default auto-save state and both ways to turn it off");
            firstRun.ShowPageForTest(4);
            firstRun.UpdateLayout();
            CaptureForReview(firstRun, "tutorial-page-5.png");
            Check(firstRun.PageFive.Visibility == Visibility.Visible && firstRun.PageCounterText.Text == "5 / 5"
                && firstRun.NextButton.Content?.ToString() == "RELYRを使い始める"
                && new[] { "アプリ別プロファイル", "ジェスチャー", "Deck", "マクロ" }
                    .All(label => Descendants<TextBlock>(firstRun.PageFive).Any(x => x.Text == label)),
                "tutorial finishes with a compact visual index of advanced features");
            firstRun.Close();
            var manualTutorial = new SetupWindow(true);
            Check(manualTutorial.DoNotShowAgainBox.Visibility == Visibility.Collapsed && manualTutorial.SkipButton.Visibility == Visibility.Collapsed, "tutorial opened from settings does not change first-run preferences");
            manualTutorial.DoNotShowAgainBox.ApplyTemplate();
            window.DeckProfileSwitchBox.ApplyTemplate();
            Check(manualTutorial.DoNotShowAgainBox.Template.FindName("SwitchTrack", manualTutorial.DoNotShowAgainBox) is Border
                && window.DeckProfileSwitchBox.Template.FindName("SwitchTrack", window.DeckProfileSwitchBox) is Border,
                "first-run and Deck options use the same theme-aware switch instead of a system checkbox");
            manualTutorial.Close();
            var toolbarControls = new System.Windows.Controls.Control[] { window.ProfileBox, window.KeyboardLayoutBox, window.EditorUndoButton, window.EditorRedoButton, window.MultiSelectToggle, window.MultiCopyButton, window.MultiPasteButton, window.MultiDeleteButton, window.ToolbarSaveButton };
            double toolbarControlTop = window.ProfileBox.TranslatePoint(new System.Windows.Point(), window).Y;
            double sidebarLogoTop = window.ProductNameText.TranslatePoint(new System.Windows.Point(), window).Y;
            Check(toolbarControls.All(x => Math.Abs(x.ActualHeight - 44) < .1)
                && Math.Abs(window.ThemeSegmentPanel.ActualHeight - 44) < .1
                && new[] { window.LightThemeToggle, window.DarkThemeToggle }.All(x => Math.Abs(x.ActualHeight - 44) < .1)
                && window.ToolbarSaveButton.ActualWidth >= 77
                && window.ToolbarSaveButton.BorderThickness == new Thickness(0)
                && window.ThemeSegmentPanel.Background is SolidColorBrush themePanelBrush && themePanelBrush.Color.A == 0
                && new System.Windows.Controls.Control[] { window.ProfileBox, window.KeyboardLayoutBox, window.EditorUndoButton, window.EditorRedoButton, window.MultiSelectToggle, window.MultiCopyButton, window.MultiPasteButton, window.MultiDeleteButton }.All(control => control.BorderThickness == new Thickness(0))
                && Math.Abs(window.ToolbarPanel.Margin.Top - 9.5) < .1
                && Math.Abs(window.ToolbarPanel.Margin.Bottom + 9.5) < .1
                && window.KeyboardLayoutToolbarIcon.RenderTransform is TranslateTransform keyboardIconShift && Math.Abs(keyboardIconShift.Y - 5) < .1
                && Math.Abs(toolbarControlTop - sidebarLogoTop) <= 1.1,
                $"toolbar is flat, the keyboard glyph is optically centered on its layout label, and the 44 px controls align to the untouched sidebar logo ({toolbarControlTop:F1}/{sidebarLogoTop:F1})");
            double layoutRight = window.KeyboardLayoutBox.TranslatePoint(new System.Windows.Point(window.KeyboardLayoutBox.ActualWidth, 0), window.ToolbarPanel).X;
            double selectionLeft = window.MultiSelectActionsPanel.TranslatePoint(new System.Windows.Point(), window.ToolbarPanel).X;
            double selectionRight = window.MultiSelectActionsPanel.TranslatePoint(new System.Windows.Point(window.MultiSelectActionsPanel.ActualWidth, 0), window.ToolbarPanel).X;
            double saveLeft = window.ToolbarSaveButton.TranslatePoint(new System.Windows.Point(), window.ToolbarPanel).X;
            Check(ReferenceEquals(window.MultiSelectActionsPanel.Parent, window.ToolbarContextPanel)
                && selectionLeft >= layoutRight - 1.1 && selectionLeft - layoutRight <= 20.1 && selectionRight <= saveLeft + 1.1,
                $"selection, copy, paste, and delete sit immediately after the keyboard layout without overlapping save ({layoutRight:F1} <= {selectionLeft:F1}..{selectionRight:F1} <= {saveLeft:F1})");
            var assignmentDragSourceButton = window.VisualInputButtonsForTest.First(button => Equals(button.Tag, "H"));
            var assignmentDragTargetButton = window.VisualInputButtonsForTest.First(button => Equals(button.Tag, "J"));
            var mouseHoverButton = window.VisualInputButtonsForTest.First(button => Equals(button.Tag, "MouseRight"));
            Check(window.VisualInputButtonsForTest.All(button => button.AllowDrop), "every visible keyboard and mouse button accepts the shared assignment drag route");
            var rapidHoverButtons = new[] { assignmentDragSourceButton, assignmentDragTargetButton, mouseHoverButton };
            var rapidHoverSizes = rapidHoverButtons.Select(button => (button.ActualWidth, button.ActualHeight)).ToArray();
            for (int cycle = 0; cycle < 100; cycle++)
            {
                foreach (var button in rapidHoverButtons)
                {
                    button.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseEnterEvent });
                    button.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseLeaveEvent });
                }
            }
            PumpFor(TimeSpan.FromMilliseconds(60));
            Check(rapidHoverButtons.Select((button, index) => window.InputTransformStableForTest(button)
                    && System.Windows.Controls.Panel.GetZIndex(button) == 0
                    && Math.Abs(button.ActualWidth - rapidHoverSizes[index].ActualWidth) < .001
                    && Math.Abs(button.ActualHeight - rapidHoverSizes[index].ActualHeight) < .001).All(stable => stable),
                "rapid pointer travel across keyboard and mouse controls never scales, overlaps, or moves their hit-test geometry");
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
            window.CurrentProfileForTest.Mappings.RemoveAll(mapping => mapping.Input is "F10" or "F11");
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "F10", Layer = "通常", Kind = ActionKind.Text, Value = "drag-source", Application = "editor.exe" });
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "F10", Layer = "通常", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+H", Application = "browser.exe" });
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "F11", Layer = "通常", Kind = ActionKind.Shortcut, Value = "Ctrl+J" });
            Check(window.TransferCurrentLayerAssignmentsForTest("F10", "F11") == AssignmentTransferResult.Swapped
                && window.CurrentProfileForTest.Mappings.Count(mapping => mapping.Input == "F11") == 2
                && window.CurrentProfileForTest.Mappings.Count(mapping => mapping.Input == "F10") == 1
                && window.CurrentProfileForTest.Mappings.Single(mapping => mapping.Input == "F10").Value == "Ctrl+J",
                "the production main-layer drag transfer swaps occupied keys and retains all application-specific source actions");
            window.CurrentProfileForTest.Mappings.RemoveAll(mapping => mapping.Input is "F10" or "F11");
            window.ColorButtonsForTest();
            var compactDragPreview = new DeckDragPreviewWindow(new Border(), compact: true);
            Check(Math.Abs(compactDragPreview.PreviewWidthForTest - 20) < .1 && Math.Abs(compactDragPreview.PreviewHeightForTest - 20) < .1,
                "editor action drags use a compact 20-pixel preview that does not cover the destination key");
            compactDragPreview.Close();
            window.LightThemeToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(window);
            bool lightToolbarApplied = ThemeService.CurrentMode == AppThemeMode.Light && window.ConfigForTest.ThemeMode == AppThemeMode.Light && window.LightThemeToggle.IsChecked == true && window.DarkThemeToggle.IsChecked == false;
            bool lightTrayMenuApplied = window.TrayMenuBackColorForTest.GetBrightness() > .9;
            window.DarkThemeToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Pump(window);
            window.LightThemeToggle.ApplyTemplate();
            window.DarkThemeToggle.ApplyTemplate();
            var lightThemeSurface = (Border)window.LightThemeToggle.Template.FindName("SegmentSurface", window.LightThemeToggle)!;
            var darkThemeSurface = (Border)window.DarkThemeToggle.Template.FindName("SegmentSurface", window.DarkThemeToggle)!;
            var darkThemeLine = (Border)window.DarkThemeToggle.Template.FindName("SegmentLine", window.DarkThemeToggle)!;
            Check(lightToolbarApplied && lightTrayMenuApplied
                && ThemeService.CurrentMode == AppThemeMode.Dark && window.ConfigForTest.ThemeMode == AppThemeMode.Dark && window.LightThemeToggle.IsChecked == false && window.DarkThemeToggle.IsChecked == true
                && window.TrayMenuBackColorForTest.GetBrightness() < .25
                && window.ThemeSegmentPanel.ClipToBounds
                && lightThemeSurface.CornerRadius == new CornerRadius(0) && darkThemeSurface.CornerRadius == new CornerRadius(0)
                && darkThemeSurface.Background is SolidColorBrush selectedThemeSurface && selectedThemeSurface.Color.A == 0
                && darkThemeLine.Opacity == 1,
                "the connected light/dark control applies the theme immediately and marks selection with only a restrained underline");
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
            bool sidebarButtonStructureAligned = lowerActionButtons.All(x => x.HorizontalContentAlignment == System.Windows.HorizontalAlignment.Stretch && x.Content is Grid grid && grid.ColumnDefinitions.Count == 2 && Math.Abs(grid.ColumnDefinitions[0].Width.Value - 32) < .1);
            bool sidebarLabelAlignment = lowerActionLabels.All(x => x.TextAlignment == TextAlignment.Left && x.HorizontalAlignment == System.Windows.HorizontalAlignment.Stretch);
            double[] sidebarLabelLefts = lowerActionLabels.Select(x => x.TranslatePoint(new System.Windows.Point(), window).X).ToArray();
            double taskbarIconCenter = taskbarIconLeft + taskbarLayerIcon.ActualWidth / 2;
            double[] sidebarIconCenters = sidebarIconCells.Select(x => x.TranslatePoint(new System.Windows.Point(), window).X + x.ActualWidth / 2).ToArray();
            bool sidebarLabelsMatch = lowerActionLabels.Select(x => x.Text).SequenceEqual(["マクロ", "プロファイル", "ジェスチャー", "Deckパネル", "設定"]);
            Check(sidebarButtonStructureAligned && sidebarLabelAlignment
                && sidebarLabelLefts.All(x => Math.Abs(x - taskbarLabelLeft) < .1)
                && sidebarIconCenters.All(x => Math.Abs(x - taskbarIconCenter) < .1)
                && sidebarLabelsMatch,
                $"sidebar command icons and labels share the exact center and text planes of the layer rows (structure={sidebarButtonStructureAligned}; alignment={sidebarLabelAlignment}; labels={sidebarLabelsMatch}; taskbarLabel={taskbarLabelLeft:F2}; actionLabels={string.Join('/', sidebarLabelLefts.Select(value => value.ToString("F2")))}; taskbarIcon={taskbarIconCenter:F2}; actionIcons={string.Join('/', sidebarIconCenters.Select(value => value.ToString("F2")))})");
            var sidebarDividers = new[] { window.KeyboardLayerDivider, window.MouseLayerDivider, window.ManagementDivider };
            Check(sidebarDividers.Select(x => x.TranslatePoint(new System.Windows.Point(), window).X).Max() - sidebarDividers.Select(x => x.TranslatePoint(new System.Windows.Point(), window).X).Min() < .1 && sidebarDividers.Select(x => x.ActualWidth).Max() - sidebarDividers.Select(x => x.ActualWidth).Min() < .1, "left-pane section dividers share identical left and right edges");
            window.DeckPanelManagerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            var flatDeckGalleryButtons = window.DeckLayoutCardsPanel.Children.OfType<System.Windows.Controls.Button>().ToArray();
            Check(window.DeckWorkspace.Visibility == Visibility.Visible && window.KeyboardWorkspace.Visibility == Visibility.Collapsed && window.DeckLayoutListWorkspace.Visibility == Visibility.Visible && window.DeckEditorWorkspace.Visibility == Visibility.Collapsed && window.DeckLayoutCardsPanel.Children.Count == window.ConfigForTest.DeckLayouts.Count + 1
                && flatDeckGalleryButtons.All(button => button.BorderThickness == new Thickness(0) && Math.Abs(button.ActualHeight - 164) < .1)
                && window.DeckPanelManagerButton.BorderThickness == new Thickness(0)
                && window.DeckNavigationActiveIndicator.Visibility == Visibility.Visible,
                "Deck management opens a flat layout gallery and marks its sidebar destination with the shared active dot");
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
            window.OpenActionPaletteForTest();
            Pump(window);
            unassignedDeckButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(!window.IsActionPaletteOpenForTest
                && window.InputName.Text == unassignedDeckInput
                && window.AssignmentScrollViewer.Visibility == Visibility.Visible
                && window.AssignmentEditor.Visibility == Visibility.Visible,
                "one Deck-slot click replaces the open Action library with that slot's assignment editor");
            window.ClickDeckPreviewBackgroundForTest();
            Pump(window);
            var deckHoverSizes = deckButtons.Take(3).Select(button => (button.ActualWidth, button.ActualHeight)).ToArray();
            for (int cycle = 0; cycle < 100; cycle++)
            {
                foreach (var button in deckButtons.Take(3))
                {
                    button.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseEnterEvent });
                    button.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseLeaveEvent });
                }
            }
            PumpFor(TimeSpan.FromMilliseconds(60));
            Check(deckButtons.Take(3).Select((button, index) => window.InputTransformStableForTest(button)
                    && System.Windows.Controls.Panel.GetZIndex(button) == 0
                    && Math.Abs(button.ActualWidth - deckHoverSizes[index].ActualWidth) < .001
                    && Math.Abs(button.ActualHeight - deckHoverSizes[index].ActualHeight) < .001).All(stable => stable),
                "rapid pointer travel across Deck editor controls keeps every hit-test surface fixed and free of animation clocks");
            window.SetAssignmentDropTargetForTest(deckButtons[1], true);
            deckButtons[1].ApplyTemplate();
            var deckDropTint = (UIElement)deckButtons[1].Template.FindName("DropTargetTint", deckButtons[1])!;
            var deckDropBadge = (UIElement)deckButtons[1].Template.FindName("DropTargetBadge", deckButtons[1])!;
            Check(MainWindow.GetIsAssignmentDropTarget(deckButtons[1]) && deckButtons[1].BorderThickness.Left >= 3
                && deckDropTint.Opacity == 0 && deckDropBadge.Opacity == 1,
                "Deck editor drag targets keep their original face while restoring the same clear drop marker");
            CaptureForReview(window, "deck-drag-target.png");
            window.SetAssignmentDropTargetForTest(deckButtons[1], false);
            var deckInputsForPalette = new[] { "Deck+01", "Deck+02" };
            var deckMappingsBeforePalette = deckInputsForPalette.ToDictionary(input => input, input => standardDeck.Mappings.Where(mapping => mapping.Input == input).Select(mapping => mapping.Copy()).ToArray());
            window.MultiSelectToggle.IsChecked = true;
            deckButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            deckButtons[1].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.OpenActionPaletteForTest();
            const string deckTemplateText = "Deck 1行目\r\nDeck 2行目";
            window.SetActionPaletteValueResolverForTest(action => action.ValueRequest == CatalogActionValueRequest.Text ? deckTemplateText : null);
            OverlayService.ResetDeckRefreshRequestCountForTest();
            bool deckTemplateApplied = window.ApplyPaletteActionForTest(textTemplateAction, "Deck+01", "Deck+01");
            Check(deckTemplateApplied
                && deckInputsForPalette.All(input => standardDeck.Mappings.LastOrDefault(mapping => mapping.Input == input) is { Kind: ActionKind.Text, Value: deckTemplateText })
                && OverlayService.DeckSlotRefreshRequestCountForTest == 1
                && OverlayService.DeckRefreshRequestCountForTest == 0,
                "one confirmed Text creation drop applies the same concrete value to every selected Deck slot through differential live synchronization");
            window.UndoPaletteActionForTest();
            Pump(window);
            window.SetActionPaletteValueResolverForTest(null);
            bool deckPaletteApplied = window.ApplyPaletteActionForTest(copyAction, "Deck+01", "Deck+01");
            bool deckPaletteWaveCentered = deckButtons.Take(2).All(window.HasCenteredPaletteDropWaveForTest);
            PumpFor(TimeSpan.FromMilliseconds(35));
            CaptureForReview(window, "action-drop-deck-center-wave.png");
            Check(deckPaletteApplied
                && deckInputsForPalette.All(input => standardDeck.Mappings.LastOrDefault(mapping => mapping.Input == input) is { Kind: ActionKind.Shortcut, Value: "Ctrl+C", DeckIcon: "copy", DeckIconAutoAssigned: true })
                && window.IsActionPaletteOpenForTest
                && deckPaletteWaveCentered
                && MainWindow.ActionDropWaveDurationMs == 500,
                "dropping one concrete action on selected Deck targets applies it and its icon, then expands the same readable half-second clipped accent wave from every slot center");
            window.UndoPaletteActionForTest();
            Pump(window);
            Check(deckInputsForPalette.All(input =>
            {
                var restored = standardDeck.Mappings.Where(mapping => mapping.Input == input).ToArray();
                var expected = deckMappingsBeforePalette[input];
                return restored.Length == expected.Length && restored.Zip(expected).All(pair => pair.First.Kind == pair.Second.Kind && pair.First.Value == pair.Second.Value && pair.First.DeckIcon == pair.Second.DeckIcon && pair.First.Description == pair.Second.Description);
            }), "one undo restores every Deck slot in a multiple-target palette assignment, including its previous visual fields");
            Check(window.ActionPaletteActionsForTest.Any(action => action.Category == DeckMonitorCatalog.Category),
                "the Deck editor alone exposes the monitor library in the existing Action pane");
            window.SelectActionPalettePopupItemForTest(DeckMonitorCatalog.Category);
            Pump(window);
            Check(window.IsActionPaletteOpenForTest && window.ActionPaletteCategoryBox.SelectedItem?.ToString() == DeckMonitorCatalog.Category,
                "choosing the Deck-only monitor category filters the shared Action library without dismissing it");
            CaptureForReview(window, "deck-monitor-library.png");
            var deckBeforeMonitor = standardDeck.Mappings.Where(mapping => mapping.Input == "Deck+01").Select(mapping => mapping.Copy()).ToArray();
            OverlayService.ResetDeckRefreshRequestCountForTest();
            bool monitorApplied = window.ApplyPaletteMonitorForTest("battery", "Deck+01");
            Pump(window);
            Check(monitorApplied
                && standardDeck.Mappings.LastOrDefault(mapping => mapping.Input == "Deck+01") is { DeckMonitor: "battery", Kind: ActionKind.None }
                && window.DeckManagementButtonsForTest.First(button => Equals(button.Tag, "Deck+01")).Content is DeckMonitorView
                && OverlayService.DeckSlotRefreshRequestCountForTest == 1
                && OverlayService.DeckRefreshRequestCountForTest == 0,
                "a Deck-only monitor drop replaces one existing slot in the editor and every cached overlay without rebuilding the complete Deck");
            var stableMonitorView = window.DeckManagementButtonsForTest.First(button => Equals(button.Tag, "Deck+01")).Content;
            window.ColorButtonsForTest();
            Check(ReferenceEquals(stableMonitorView, window.DeckManagementButtonsForTest.First(button => Equals(button.Tag, "Deck+01")).Content),
                "routine visual refreshes reuse a live monitor view instead of allocating another graph tree");
            CaptureForReview(window, "deck-monitor.png");
            window.UndoPaletteActionForTest();
            Pump(window);
            Check(standardDeck.Mappings.Where(mapping => mapping.Input == "Deck+01").Select(mapping => mapping.DeckMonitor).SequenceEqual(deckBeforeMonitor.Select(mapping => mapping.DeckMonitor)),
                "monitor assignment uses the same complete five-second undo transaction as Action assignment");
            window.CloseActionPaletteForTest();
            window.MultiSelectToggle.IsChecked = false;
            deckButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.LegacyAssignmentEditor.Visibility == Visibility.Collapsed
                && window.AssignmentSummaryPanel.Visibility == Visibility.Visible
                && window.AssignmentTapSlotText.Text == "ACTION"
                && window.AssignmentHoldCard.Visibility == Visibility.Collapsed
                && window.AssignmentReplaceHintText.Text.Contains("Deckボタンへドラッグ", StringComparison.Ordinal),
                "Deck selection uses the same unified assignment summary with one ACTION destination and no retired direct editor");
            window.OpenActionPaletteForTest();
            Pump(window);
            Check(window.ActionPaletteContextText.Text.Contains("Deck + 01", StringComparison.Ordinal)
                && window.ActionPaletteCategoryBox.Items.Cast<object>().Any(item => item.ToString() == DeckMonitorCatalog.Category)
                && window.ActionPaletteCategoryBox.SelectedItem?.ToString() == "最近使ったもの"
                && window.ActionPaletteClearRecentButton.Visibility == Visibility.Visible,
                "Deck opens the same recent-first Action library with its selected button as the drag destination and retains Deck-only monitors");
            window.CloseActionPaletteForTest();
            Pump(window);
            deckButtons[0].RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left) { RoutedEvent = System.Windows.Controls.Control.MouseDoubleClickEvent });
            Pump(window);
            deckButtons[0].ApplyTemplate();
            var deckSingleBadge = (UIElement)deckButtons[0].Template.FindName("MultiSelectBadge", deckButtons[0])!;
            Check(window.IsActionPaletteOpenForTest && window.InputName.Text == "Deck+01" && !window.ValueBox.IsKeyboardFocusWithin
                && window.ActionPaletteContextText.Text.Contains("Deck + 01", StringComparison.Ordinal)
                && deckSingleBadge.Opacity == 0 && deckButtons[0].Opacity == 1 && Math.Abs(deckButtons[1].Opacity - MainWindow.SelectionDimOpacity) < .01
                && deckButtons[0].BorderBrush is SolidColorBrush deckSingleBorder && deckSingleBorder.Color == ThemeService.Color("AccentBrush")
                && deckButtons[0].BorderThickness == new Thickness(2),
                "double-clicking a Deck slot keeps only that slot bright and opens the same draggable Action library used by the keyboard");
            window.CloseActionPaletteForTest();
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
            string applicationIconPath = Environment.ProcessPath ?? Path.Combine(Environment.SystemDirectory, "notepad.exe");
            deckButtons[17].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.ApplyApplicationSelectionForTest(false, applicationIconPath);
            Pump(window);
            var applicationIconMapping = DeckPanelLayout.FindMapping(standardDeck, 18);
            var existingApplicationWithoutFace = new Mapping { Kind = ActionKind.Launch, Value = applicationIconPath };
            var applicationWithManualFace = new Mapping { Kind = ActionKind.Launch, Value = applicationIconPath, DeckIcon = "home", DeckIconAutoAssigned = false };
            Check(applicationIconMapping is { Kind: ActionKind.Launch, DeckIconAutoAssigned: true }
                && DeckIconCatalog.CreateVisual(applicationIconMapping, 22) is System.Windows.Controls.Image { Source: not null }
                && DeckIconCatalog.CreateVisual(existingApplicationWithoutFace, 22) is System.Windows.Controls.Image { Source: not null }
                && DeckIconCatalog.CreateVisual(applicationWithManualFace, 22) is TextBlock,
                "new and existing Deck application assignments use the executable's real icon while preserving a manually selected face");
            if (applicationIconMapping != null)
                standardDeck.Mappings.Remove(applicationIconMapping);
            deckButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
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
                  && MainWindow.IsTaskbarMappedInput("Taskbar+MouseRight:Long")
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
            bool initialDeckHoverAnimation = standardDeck.HoverAnimationEnabled;
            window.DeckHoverAnimationBox.IsChecked = !initialDeckHoverAnimation;
            Pump(window);
            bool deckHoverSettingChanged = standardDeck.HoverAnimationEnabled == !initialDeckHoverAnimation
                && OverlayService.DeckLayoutPreviewRequestCountForTest >= 1
                && OverlayService.DeckRefreshRequestCountForTest == 0;
            window.DeckHoverAnimationBox.IsChecked = initialDeckHoverAnimation;
            Pump(window);
            Check(deckHoverSettingChanged && standardDeck.HoverAnimationEnabled == initialDeckHoverAnimation,
                "Deck hover animation updates the live layout through the lightweight appearance path instead of being a save-only setting");
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
                window.DeckMediumSizeToggle.IsChecked = true;
                window.DeckMediumSizeToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Pump(window);
                int mediumSlots = standardDeck.Columns * standardDeck.Rows;
                window.DeckLargeSizeToggle.IsChecked = true;
                window.DeckLargeSizeToggle.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Pump(window);
                int largeSlots = standardDeck.Columns * standardDeck.Rows;
                var settingsControlClick = new System.Windows.Input.MouseButtonEventArgs(
                    System.Windows.Input.Mouse.PrimaryDevice,
                    Environment.TickCount,
                    System.Windows.Input.MouseButton.Left)
                {
                    RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent
                };
                window.DeckColumnsSlider.RaiseEvent(settingsControlClick);
                Pump(window);
                bool settingsControlKeptDrawerOpen = window.DeckSettingsPanel.Visibility == Visibility.Visible;
                window.ClickDeckPreviewBackgroundForTest();
                Pump(window);
                Check(mediumSlots == 24 && largeSlots == 45 && largeSlots > mediumSlots
                    && settingsControlKeptDrawerOpen
                    && window.DeckSettingsPanel.Visibility == Visibility.Collapsed,
                    "Deck size presets increase consistently from 3x3 through 6x4 to 9x5, controls keep the drawer open, and one blank preview click closes it");
                window.DeckCustomizeToggleButton.IsChecked = true;
                window.DeckCustomizeToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Pump(window);
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
            deckOverlay.CapturePanelMouseForTest();
            deckOverlay.RequestHideForReuse();
            PumpFor(TimeSpan.FromMilliseconds(70));
            Check(deckOverlay.IsVisible
                && deckOverlay.IsPresentationHiding
                && !deckOverlay.OwnsMouseCaptureForTest
                && !deckOverlay.PresentationContentHitTestVisibleForTest
                && deckOverlay.PresentationMotionActiveForTest
                && deckOverlay.DepartureUsesScaleFadeOnlyForTest
                && deckOverlay.PresentationScaleForTest is <= 1 and > .96
                && Math.Abs(deckOverlay.PresentationOffsetForTest) < .001,
                $"Deck departure is a short centered scale-and-fade that releases capture and disables interaction without positional movement (visible={deckOverlay.IsVisible}, hiding={deckOverlay.IsPresentationHiding}, capture={deckOverlay.OwnsMouseCaptureForTest}, hit={deckOverlay.PresentationContentHitTestVisibleForTest}, motion={deckOverlay.PresentationMotionActiveForTest}, scaleOnly={deckOverlay.DepartureUsesScaleFadeOnlyForTest}, scale={deckOverlay.PresentationScaleForTest:F3}, offset={deckOverlay.PresentationOffsetForTest:F3})");
            deckOverlay.PrepareForShow();
            PumpFor(TimeSpan.FromMilliseconds(260));
            Check(deckOverlay.IsVisible
                && !deckOverlay.IsPresentationHiding
                && deckOverlay.PresentationContentHitTestVisibleForTest
                && !deckOverlay.PresentationMotionActiveForTest,
                "showing Deck during its fade cancels the stale hide completion and retargets smoothly from the live opacity");
            deckOverlay.RequestHideForReuse();
            PumpFor(TimeSpan.FromMilliseconds(270));
            Check(!deckOverlay.IsVisible
                && !deckOverlay.IsPresentationHiding
                && !deckOverlay.OwnsMouseCaptureForTest
                && deckOverlay.PresentationContentHitTestVisibleForTest
                && !deckOverlay.PresentationMotionActiveForTest,
                "Deck fade completes within its watchdog and leaves no cached visible, captured, or transparent hit surface");
            deckOverlay.PrepareForShow();
            deckOverlay.Show();
            PumpFor(TimeSpan.FromMilliseconds(320));
            Check(deckOverlay.IsVisible
                && !deckOverlay.PresentationMotionActiveForTest
                && Math.Abs(deckOverlay.PresentationOffsetForTest) < .001,
                "Deck arrival settles to a stable full-opacity content surface without a lingering animation clock");
            UiMotionService.Apply(false);
            var motionDisabledLayout = new DeckLayoutDefinition { Name = "Motion disabled", Columns = 1, Rows = 1 };
            var motionDisabledDeck = new DeckPanelOverlayWindow(
                new AppConfig { DeckLayouts = [motionDisabledLayout] },
                null,
                selectedLayout: motionDisabledLayout);
            motionDisabledDeck.PrepareForShow();
            motionDisabledDeck.Show();
            Pump(window);
            bool motionDisabledArrivalWasImmediate = motionDisabledDeck.IsVisible
                && !motionDisabledDeck.PresentationMotionActiveForTest
                && Math.Abs(motionDisabledDeck.Opacity - 1) < .001
                && Math.Abs(motionDisabledDeck.PresentationOffsetForTest) < .001;
            motionDisabledDeck.RequestHideForReuse();
            Check(motionDisabledArrivalWasImmediate && !motionDisabledDeck.IsVisible,
                "Deck arrival and departure are both immediate when RELYR animations are off");
            motionDisabledDeck.Close();
            UiMotionService.Apply(true);
            var clickPreviewLayout = new DeckLayoutDefinition
            {
                Name = "Click preview",
                Columns = 2,
                Rows = 1,
                Mappings =
                [
                    new Mapping { Input = "Deck+01", Layer = DeckPanelLayout.Layer, DeckFilePath = deckPreviewAudio },
                    new Mapping { Input = "Deck+02", Layer = DeckPanelLayout.Layer, DeckFilePath = deckPreviewVideo }
                ]
            };
            var clickPreviewOverlay = new DeckPanelOverlayWindow(
                new AppConfig { DeckLayouts = [clickPreviewLayout], DeckHoverPreviewsEnabled = false },
                null,
                selectedLayout: clickPreviewLayout);
            clickPreviewOverlay.Show();
            clickPreviewOverlay.UpdateLayout();
            Pump(window);
            clickPreviewOverlay.DeckButtons[1].RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = System.Windows.Input.Mouse.MouseEnterEvent });
            Pump(window);
            bool hoverOffStayedClosed = clickPreviewOverlay.VideoPreviewCountForTest == 0;
            clickPreviewOverlay.DeckButtons[1].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            bool videoOpenedFromClick = clickPreviewOverlay.VideoPreviewCountForTest == 1
                && clickPreviewOverlay.VideoPreviewUsesSourceHoverForTest == false;
            clickPreviewOverlay.DeckButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            bool audioOpenedFromClick = clickPreviewOverlay.AudioPreviewActiveForTest;
            Check(hoverOffStayedClosed && videoOpenedFromClick && audioOpenedFromClick,
                "with file hover disabled, hover stays silent while one Deck click opens the existing thumbnail video preview or starts audio playback");
            clickPreviewOverlay.Close();
            Pump(window);
            Check(deckOverlay.DeckButtons[1].ToolTip is System.Windows.Controls.ToolTip { Placement: System.Windows.Controls.Primitives.PlacementMode.Custom, CustomPopupPlacementCallback: not null }, "Deck thumbnail preview is placed outside the Deck instead of covering adjacent keys");
            var deckOverlayBackground = (deckOverlay.Content as Border)?.Background as SolidColorBrush;
            Check(deckOverlayBackground != null && deckOverlayBackground.Color.R == ThemeService.Color("AppBackground").R && deckOverlayBackground.Color.G == ThemeService.Color("AppBackground").G && deckOverlayBackground.Color.B == ThemeService.Color("AppBackground").B, "Deck overlay default surface uses the same background tone as the main app");
            var overlayDeckView = Descendants<Viewbox>(deckOverlay).Single(view => view.Child is System.Windows.Controls.Primitives.UniformGrid);
            var overlayRoot = (Grid)overlayDeckView.Parent;
            Check(deckOverlay.HeaderBackgroundForTest == System.Windows.Media.Brushes.Transparent && deckOverlay.HeaderGripVisibleForTest && DeckPanelOverlayWindow.CanDragPanelFromForTest((Border)deckOverlay.Content) && !DeckPanelOverlayWindow.CanDragPanelFromForTest(deckOverlay.DeckButtons[0]) && deckOverlay.PanelPaddingForTest.Left == 12 && deckOverlay.PanelPaddingForTest.Top == 12 && deckOverlay.PanelPaddingForTest.Right == 12 && deckOverlay.PanelPaddingForTest.Bottom == 12 && overlayDeckView.Margin == new Thickness(0) && overlayDeckView.StretchDirection == StretchDirection.Both && overlayDeckView.HorizontalAlignment == System.Windows.HorizontalAlignment.Center && overlayDeckView.VerticalAlignment == VerticalAlignment.Center && Math.Abs(overlayDeckView.ActualWidth - overlayRoot.ActualWidth) < 1 && Math.Abs(overlayDeckView.ActualHeight - overlayRoot.RowDefinitions[2].ActualHeight) < 1, $"large Decks show the grip, every non-button panel surface can drag, and the aspect-locked grid leaves no extra blank band (grip={deckOverlay.HeaderGripVisibleForTest}, view={overlayDeckView.ActualWidth:F2}x{overlayDeckView.ActualHeight:F2}, root={overlayRoot.ActualWidth:F2}x{overlayRoot.RowDefinitions[2].ActualHeight:F2})");
            Check(!DeckPanelOverlayWindow.CanDragPanelFromForTest(new Slider())
                && !DeckPanelOverlayWindow.CanDragPanelFromForTest(new System.Windows.Controls.Primitives.Thumb()),
                "Deck overlay monitor sliders and thumbs keep mouse capture instead of starting a panel drag");
            var cornerHits = new[] { new System.Windows.Point(1, 1), new System.Windows.Point(deckOverlay.ActualWidth - 1, 1), new System.Windows.Point(1, deckOverlay.ActualHeight - 1), new System.Windows.Point(deckOverlay.ActualWidth - 1, deckOverlay.ActualHeight - 1) }.Select(deckOverlay.ResizeHitTestForTest).ToArray();
            Check(deckOverlay.ResizeMode == ResizeMode.CanResize && cornerHits.All(hit => hit != 0) && cornerHits.Distinct().Count() == 4 && deckOverlay.ResizeHitTestForTest(new System.Windows.Point(deckOverlay.ActualWidth / 2, deckOverlay.ActualHeight / 2)) == 0, "all four Deck overlay corners expose distinct resize hit zones without consuming the center");
            Check(deckOverlay.DeckButtons.Count == 45 && deckOverlay.DeckButtons.All(x => x.IsEnabled && Math.Abs(x.Opacity - 1) < .001 && x.Background is SolidColorBrush && !Descendants<Border>(x).Any(border => border.Background is LinearGradientBrush)) && Math.Abs(deckOverlay.VisualOpacityForTest - .67) < .001 && !deckOverlay.AllowsTransparency && deckOverlay.Background is SolidColorBrush { Color.A: > 0 } && !deckOverlay.ShowActivated && deckOverlay.UsesNoActivateStyle && Descendants<TextBlock>(deckOverlay).Any(x => x.Text == "コピー") && Math.Abs(deckOverlay.Left - 120) < .1 && Math.Abs(deckOverlay.Top - 140) < .1, "Deck retains the shared pre-0.1.367 panel opacity inside a non-layered no-activate native window");
            var overlayHoverButton = deckOverlay.DeckButtons[0];
            overlayHoverButton.ApplyTemplate();
            var overlayHoverRoot = (FrameworkElement)overlayHoverButton.Template.FindName("HoverRoot", overlayHoverButton)!;
            var overlayHoverScale = UiMotionService.MutableScale(overlayHoverRoot);
            double overlayHoverWidth = overlayHoverButton.ActualWidth;
            double overlayHoverHeight = overlayHoverButton.ActualHeight;
            for (int cycle = 0; cycle < 100; cycle++)
            {
                overlayHoverButton.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseEnterEvent });
                overlayHoverButton.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseLeaveEvent });
            }
            PumpFor(TimeSpan.FromMilliseconds(260));
            Check(!overlayHoverRoot.HasAnimatedProperties
                && !overlayHoverScale.HasAnimatedProperties
                && Math.Abs(overlayHoverScale.ScaleX - 1) < .001
                && Math.Abs(overlayHoverScale.ScaleY - 1) < .001
                && Math.Abs(overlayHoverButton.ActualWidth - overlayHoverWidth) < .001
                && Math.Abs(overlayHoverButton.ActualHeight - overlayHoverHeight) < .001
                && overlayHoverButton.Template.FindName("GlassHighlight", overlayHoverButton) == null
                && overlayHoverButton.Template.FindName("HoverUnderline", overlayHoverButton) == null,
                "rapid pointer travel across the Deck overlay leaves the interruptible scale settled without resizing or moving its hit surface");
            PumpFor(TimeSpan.FromMilliseconds(260));
            bool hoverScaleBaselineAnimated = overlayHoverScale.HasAnimatedProperties;
            double hoverScaleBaselineValue = overlayHoverScale.ScaleX;
            bool hoverScaleBaseline = !hoverScaleBaselineAnimated && Math.Abs(hoverScaleBaselineValue - 1) < .001;
            overlayHoverButton.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseEnterEvent });
            bool hoverScaleDeferred = !overlayHoverScale.HasAnimatedProperties && Math.Abs(overlayHoverScale.ScaleX - 1) < .001;
            PumpFor(TimeSpan.FromMilliseconds(120));
            bool hoverScaleStillWaiting = !overlayHoverScale.HasAnimatedProperties && Math.Abs(overlayHoverScale.ScaleX - 1) < .001;
            PumpFor(TimeSpan.FromMilliseconds(320));
            bool hoverScaleStarted = overlayHoverScale.HasAnimatedProperties || overlayHoverScale.ScaleX > 1.0001;
            double hoverScaleLiveValue = overlayHoverScale.ScaleX;
            bool hoverScaleIsDeliberate = hoverScaleLiveValue > 1.0001 && hoverScaleLiveValue <= 1.0701;
            PumpFor(TimeSpan.FromMilliseconds(520));
            double hoverScaleSettledInValue = overlayHoverScale.ScaleX;
            CaptureForReview(deckOverlay, "deck-hover-scale.png");
            bool hoverScaleRuns = hoverScaleBaseline && hoverScaleDeferred && hoverScaleStarted && hoverScaleIsDeliberate
                && !overlayHoverScale.HasAnimatedProperties
                && Math.Abs(hoverScaleSettledInValue - 1.07) < .001
                && Math.Abs(overlayHoverScale.ScaleY - hoverScaleSettledInValue) < .001
                && Math.Abs(overlayHoverButton.Opacity - 1) < .001
                && Math.Abs(overlayHoverButton.ActualWidth - overlayHoverWidth) < .001
                && Math.Abs(overlayHoverButton.ActualHeight - overlayHoverHeight) < .001;
            overlayHoverButton.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseLeaveEvent });
            bool hoverScaleOutStarted = overlayHoverScale.HasAnimatedProperties;
            PumpFor(TimeSpan.FromMilliseconds(190));
            bool hoverScaleSettled = hoverScaleOutStarted && !overlayHoverScale.HasAnimatedProperties && Math.Abs(overlayHoverScale.ScaleX - 1) < .001 && Math.Abs(overlayHoverScale.ScaleY - 1) < .001;
            overlayLayout.HoverAnimationEnabled = false;
            deckOverlay.RefreshLayoutPreview(67, true);
            overlayHoverButton.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseEnterEvent });
            bool layoutHoverOffIsImmediate = !overlayHoverScale.HasAnimatedProperties && Math.Abs(overlayHoverScale.ScaleX - 1) < .001;
            overlayLayout.HoverAnimationEnabled = true;
            UiMotionService.Apply(false);
            deckOverlay.RefreshLayoutPreview(67, true);
            overlayHoverButton.RaiseEvent(new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = UIElement.MouseEnterEvent });
            bool globalHoverOffIsImmediate = !overlayHoverScale.HasAnimatedProperties && Math.Abs(overlayHoverScale.ScaleX - 1) < .001;
            UiMotionService.Apply(true);
            deckOverlay.RefreshLayoutPreview(67, true);
            Check(hoverScaleRuns && hoverScaleSettled && layoutHoverOffIsImmediate && globalHoverOffIsImmediate,
                $"Deck hover ignores brief pointer crossings, eases in slowly without overshoot, returns promptly, and enlarges only when both animation settings are enabled without resizing its hit surface (baseline={hoverScaleBaseline}/{hoverScaleBaselineAnimated}/{hoverScaleBaselineValue:F3}, deferred={hoverScaleDeferred}/{hoverScaleStillWaiting}, started={hoverScaleStarted}/{hoverScaleLiveValue:F3}, deliberate={hoverScaleIsDeliberate}, target={hoverScaleSettledInValue:F3}, running={hoverScaleRuns}, out={hoverScaleOutStarted}, settled={hoverScaleSettled}, layoutOff={layoutHoverOffIsImmediate}, globalOff={globalHoverOffIsImmediate})");
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
            PumpFor(TimeSpan.FromMilliseconds(270));
            Check(!deckOverlay.IsVisible, "Deck overlay closes from its top-right X button");
            var differentialDeckLayout = new DeckLayoutDefinition
            {
                Name = "Differential Deck",
                Columns = 3,
                Rows = 2,
                Mappings =
                [
                    new Mapping { Input = "Deck+01", Layer = DeckPanelLayout.Layer, Kind = ActionKind.Shortcut, Value = "Ctrl+C" },
                    new Mapping { Input = "Deck+02", Layer = DeckPanelLayout.Layer, Kind = ActionKind.Text, Value = "keep" }
                ]
            };
            var differentialDeckOverlay = new DeckPanelOverlayWindow(
                new AppConfig { DeckLayouts = [differentialDeckLayout] },
                null,
                selectedLayout: differentialDeckLayout);
            differentialDeckOverlay.Show();
            differentialDeckOverlay.UpdateLayout();
            Pump(window);
            var cellsBeforeDelete = differentialDeckOverlay.DeckButtons.ToArray();
            OverlayService.Configure(
                () => window.ConfigForTest,
                slotsChanged: window.HandleOverlayDeckSlotsChangedForTest);
            OverlayService.ResetDeckRefreshRequestCountForTest();
            differentialDeckOverlay.DeleteDeckButtonForTest(1);
            Pump(window);
            var cellsAfterDelete = differentialDeckOverlay.DeckButtons.ToArray();
            bool deleteTouchedOnlyTarget = !ReferenceEquals(cellsBeforeDelete[0], cellsAfterDelete[0])
                && cellsBeforeDelete.Skip(1).SequenceEqual(cellsAfterDelete.Skip(1));
            differentialDeckOverlay.AssignDeckFileForTest(1, deckPreviewImage);
            Pump(window);
            var cellsAfterFileDrop = differentialDeckOverlay.DeckButtons.ToArray();
            bool fileDropTouchedOnlyTarget = !ReferenceEquals(cellsAfterDelete[0], cellsAfterFileDrop[0])
                && cellsAfterDelete.Skip(1).SequenceEqual(cellsAfterFileDrop.Skip(1));
            window.SaveAndApplyForTest();
            Pump(window);
            Check(deleteTouchedOnlyTarget
                && fileDropTouchedOnlyTarget
                && differentialDeckLayout.Mappings.Single(mapping => mapping.Input == "Deck+01").DeckFilePath == Path.GetFullPath(deckPreviewImage)
                && OverlayService.DeckRefreshRequestCountForTest == 0,
                $"Deck overlay delete and file drop replace only the changed cell, and the following save does not rebuild the whole Deck (delete={deleteTouchedOnlyTarget}, drop={fileDropTouchedOnlyTarget}, refreshes={OverlayService.DeckRefreshRequestCountForTest})");
            differentialDeckOverlay.Close();
            OverlayService.Configure(null);
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
            autoHideOverlay.PointerButtonsPressedProviderForTest = () => false;
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
                PumpFor(TimeSpan.FromMilliseconds(760));
                Check(!autoHideOverlay.IsVisible && !autoHideOverlay.OwnsMouseCaptureForTest,
                    $"pointer-leave hide removes the Deck and its mouse capture instead of leaving an edge tab or hit-test surface ({autoHideOverlay.AutoHideStateForTest})");
                autoHideOverlay.PrepareForShow();
                autoHideOverlay.Show();
                autoHideOverlay.RefreshAppearance(96, true, DeckAutoDismissBehavior.Hide, DeckAutoDismissBehavior.StayVisible);
                autoHideOverlay.DeckButtons[0].RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                PumpFor(TimeSpan.FromMilliseconds(430));
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
            Check(window.InspectorEmptyTitleText.Text == "Actionを選択"
                && window.InspectorHintOneTitle.Text == "Actionを開く"
                && window.InspectorHintTwoTitle.Text == "ドラッグ"
                && window.InspectorHintTwoDescription.Text == "Deckへ割り当て"
                && window.InspectorHintThreeTitle.Text == "Deckボタンをクリック"
                && window.InspectorHintThreeDescription.Text == "詳細を編集",
                "the Deck editor keeps the same action-library workflow while changing the target-specific hint copy");
            Check(OverlayService.IsDeckPanelVisible(selectedDeckAction)
                && System.Windows.Automation.AutomationProperties.GetName(window.DeckOverlayToggleButton) == "Deckを非表示"
                && window.DeckSaveStatusText.Visibility == Visibility.Collapsed
                && OverlayService.DeckPanelPresentationState(selectedDeckAction) == OverlayService.DeckPresentationState.Visible,
                $"the actual Deck preview action changes into an explicit hide action while keeping detailed state out of the visible toolbar (visible={OverlayService.IsDeckPanelVisible(selectedDeckAction)}, button={window.DeckOverlayToggleButton.Content})");
            window.DeckOverlayToggleButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            PumpFor(TimeSpan.FromMilliseconds(270));
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
            PumpFor(TimeSpan.FromMilliseconds(270));
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
            var stableDeckCells = window.DeckGridButtonsForTest.Take(306).ToArray();
            OverlayService.ResetDeckRefreshRequestCountForTest();
            window.ApplyDeckSliderSizeForTest(17, 18);
            Pump(window);
            bool sliderResizeReusedCells = stableDeckCells.SequenceEqual(window.DeckGridButtonsForTest.Take(306));
            window.ApplyDeckSliderSizeForTest(18, 18);
            Pump(window);
            Check(sliderResizeReusedCells
                && window.DeckGridButtonsForTest.Count == 324
                && OverlayService.DeckRefreshRequestCountForTest == 0
                && OverlayService.DeckLayoutPreviewRequestCountForTest >= 2,
                "Deck dimension Sliders reuse existing cells and route live preview frames through the lightweight overlay path");
            window.FlushDeckCustomizationRefreshForTest();
            Check(OverlayService.DeckRefreshRequestCountForTest == 0,
                "releasing a Deck customization Slider does not trigger a redundant full overlay rebuild");
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
                && Math.Abs(window.DeckOverlayToggleButton.ActualHeight - 44) < .1
                && window.DeckBackButton.BorderThickness == new Thickness(0)
                && window.DeckGridViewToggle.BorderThickness == new Thickness(0)
                && window.DeckListViewToggle.BorderThickness == new Thickness(0)
                && window.DeckCustomizeToggleButton.BorderThickness == new Thickness(0)
                && window.DeckOverlayToggleButton.BorderThickness == new Thickness(0)
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
            window.ApplyDeckSizeForTest(18, 18);
            Pump(window);
            var denseLastCell = (FrameworkElement)window.DeckManagementGrid.Children[^1];
            var denseLastCorner = denseLastCell.TranslatePoint(
                new System.Windows.Point(denseLastCell.ActualWidth, denseLastCell.ActualHeight),
                window.DeckGridScrollViewer);
            Check(window.DeckGridScrollViewer.ScrollableWidth < .1
                && window.DeckGridScrollViewer.ScrollableHeight < .1
                && denseLastCorner.X <= window.DeckGridScrollViewer.ViewportWidth + .5
                && denseLastCorner.Y <= window.DeckGridScrollViewer.ViewportHeight + .5,
                $"an 18x18 Deck remains completely visible while the customization drawer is open (corner={denseLastCorner.X:F1},{denseLastCorner.Y:F1}; viewport={window.DeckGridScrollViewer.ViewportWidth:F1},{window.DeckGridScrollViewer.ViewportHeight:F1})");
            window.ApplyDeckSizeForTest(9, 5);
            Pump(window);
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
            var toolbarEscapeKey = window.KeyboardPanel.Children.OfType<System.Windows.Controls.Button>().First(x => Equals(x.Tag, "Esc"));
            double profileToolbarLeft = window.ProfileToolbarIcon.TranslatePoint(new System.Windows.Point(), window).X;
            double escapeKeyLeft = toolbarEscapeKey.TranslatePoint(new System.Windows.Point(), window).X;
            Check(Math.Abs(profileToolbarLeft - escapeKeyLeft) <= 1.1, $"the profile mark and Escape key share one exact left edge ({profileToolbarLeft:F1}/{escapeKeyLeft:F1})");
            double keyboardCenter = window.KeyboardSurfaceCard.TranslatePoint(new System.Windows.Point(window.KeyboardSurfaceCard.ActualWidth / 2, 0), window.WorkspaceGrid).X;
            double workspaceCenter = window.WorkspaceGrid.ActualWidth / 2;
            Check(Math.Abs(keyboardCenter - workspaceCenter) <= 1.1, $"the main keyboard is centered in the middle workspace (keyboard={keyboardCenter:F1}, workspace={workspaceCenter:F1})");
            Check(ReferenceEquals(window.LayerNavigationPane.Parent, window.ShellDock) && window.ShellDock.Children.IndexOf(window.LayerNavigationPane) == 0 && Math.Abs(window.HeaderBrandColumn.Width.Value) < .1 && window.ToolbarSaveButton.TranslatePoint(new System.Windows.Point(window.ToolbarSaveButton.ActualWidth, 0), window.ToolbarPanel).X <= window.ToolbarPanel.ActualWidth + 1, "the sidebar spans from the client top while the toolbar begins to its right and stays on one line");
            Check(window.ToolbarPanel.Parent is Grid && !Descendants<ScrollViewer>(window.ToolbarPanel).Any(), "compact toolbar needs neither wrapping nor a horizontal slider");
            Check(window.ProfileBox.BorderThickness == new Thickness(0) && window.KeyboardLayoutBox.BorderThickness == new Thickness(0)
                && Descendants<System.Windows.Shapes.Path>(window.ProfileToolbarIcon).Count() == 2
                && Descendants<System.Windows.Shapes.Ellipse>(window.ProfileToolbarIcon).Count() == 2,
                "toolbar profile and layout selectors use quiet frameless surfaces while the profile mark combines RELYR layers with a user glyph");
            var newProfileMenuItem = window.ProfileBox.Items.OfType<ComboBoxItem>().SingleOrDefault(item => Equals(item.Tag, "NewProfile"));
            Check(newProfileMenuItem?.Content is Border { Child: TextBlock { Text: "＋  新しいプロファイル" } }
                && !Descendants<System.Windows.Controls.Button>(window.ToolbarContextPanel).Any(button => button.ToolTip?.ToString()?.Contains("プロファイルを追加", StringComparison.Ordinal) == true),
                "new profile creation lives inside the profile menu instead of consuming a permanent toolbar button");
            Check(newProfileMenuItem?.Content is Border newProfileBorder
                && newProfileBorder.MinHeight >= 40
                && newProfileBorder.Margin.Bottom >= 4
                && newProfileBorder.Padding.Bottom >= 6,
                "the new-profile command keeps enough measured height and bottom breathing room to remain fully visible");
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
                && new System.Windows.Controls.Control[] { window.MultiSelectToggle, window.MultiCopyButton, window.MultiPasteButton, window.MultiDeleteButton }.All(control => Math.Abs(control.ActualWidth - 44) < .1)
                && new System.Windows.Controls.Control[] { window.LightThemeToggle, window.DarkThemeToggle }.All(control => Math.Abs(control.ActualWidth - 40) < .1),
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
            toolbarEscapeKey = window.KeyboardPanel.Children.OfType<System.Windows.Controls.Button>().First(x => Equals(x.Tag, "Esc"));
            profileToolbarLeft = window.ProfileToolbarIcon.TranslatePoint(new System.Windows.Point(), window).X;
            escapeKeyLeft = toolbarEscapeKey.TranslatePoint(new System.Windows.Point(), window).X;
            Check(Math.Abs(profileToolbarLeft - escapeKeyLeft) <= 1.1, $"compact layouts preserve the shared profile and Escape left edge ({profileToolbarLeft:F1}/{escapeKeyLeft:F1})");
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
            Check(window.CurrentProfileForTest.Mappings.Any(x => x.Input == "Up" && x.Value == "Enter") && window.AppliedMappingForTest("Up") == null && window.LastInput.Text.Contains("未保存")
                && window.UnsavedChangesIndicator.Visibility == Visibility.Visible,
                "input completion keeps edits pending and shows the persistent bottom-center unsaved indicator when auto-save is off");
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(ReferenceEquals(window.MouseHost.Child, window.MousePanel), "mouse diagram is placed to the right of the lower keyboard block");
            Check(layerButtons.All(button => Descendants<TextBlock>(button).Count() == 1), "layer cards contain no secondary instruction text");
            window.SetCapsLockRemapForTest(true);
            window.CapsLockLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(window.CapsLockLayerButton.Background is SolidColorBrush capsActive
                && capsActive.Color == ThemeService.Color("LayerActiveBackground")
                && !window.HasLayerEditorTransitionForTest
                && Math.Abs(window.KeyboardWorkspace.Opacity - 1) < .001,
                "CapsLock layer opens immediately when the F13 remap is active without moving or fading the editor workspace");
            window.ApplyUiAnimationsForTest(false);
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Check(!window.HasLayerEditorTransitionForTest
                && Math.Abs(window.KeyboardWorkspace.Opacity - 1) < .001,
                "turning animations off makes the same layer change immediate and removes every workspace motion clock");
            window.ApplyUiAnimationsForTest(true);
            var engineToggle = window.EngineToggle;
            Check(engineToggle.IsVisible && window.EngineStatus.Text.Contains("稼働中") && window.EngineStatus.Foreground is SolidColorBrush engineBrush && engineBrush.Color == ThemeService.Color("AccentTextBrush"), "engine state is a readable clickable item at the sidebar foot");
            var engineTextCenter = window.EngineStatus.TranslatePoint(new System.Windows.Point(0, window.EngineStatus.ActualHeight / 2), engineToggle).Y;
            Check(Math.Abs(engineTextCenter - engineToggle.ActualHeight / 2) < 1, "engine status text is vertically centered");
            Check(window.AutoSaveToggle.IsVisible && window.AutoSaveStatus.Text.Contains("自動保存 オフ"), "auto-save state is visible at the sidebar foot");
            window.AutoSaveToggle.IsChecked = true;
            Check(window.AutoSaveStatus.Text.Contains("自動保存 オン") && new ConfigService().Load().AutoSave && window.AppliedMappingForTest("Up") is { Value: "Enter" }
                && window.UnsavedChangesIndicator.Visibility == Visibility.Collapsed,
                "turning auto-save on saves and applies the pending edit, then clears the unsaved indicator");
            window.AutoSaveToggle.IsChecked = false;
            Check(!new ConfigService().Load().AutoSave && window.UnsavedChangesIndicator.Visibility == Visibility.Collapsed,
                "turning auto-save off is persisted immediately without claiming that a clean document is unsaved");
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
            var leftKeyAction = new CatalogAction("キー", "Left", "左矢印キーを送信します", ActionKind.Key, "Left");
            bool leftKeyApplied = window.ApplyPaletteActionForTest(leftKeyAction, "Q", "Q");
            Pump(window);
            qForCaps.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(leftKeyApplied
                && window.LegacyAssignmentEditor.Visibility == Visibility.Collapsed
                && window.AssignmentSummaryPanel.Visibility == Visibility.Visible
                && window.AssignmentTapSlotText.Text == "TAP"
                && window.AssignmentTapNameText.Text == "Left"
                && window.AssignmentReplaceHintText.Text.Contains("TAP / HOLD", StringComparison.Ordinal),
                "a selected key uses the unified read-only TAP/HOLD summary while changes remain drag assignments");
            window.OpenActionPaletteForTest();
            Pump(window);
            Check(window.ActionPaletteContextText.Text.Contains("Q", StringComparison.Ordinal)
                && window.ActionPaletteContextText.Text.Contains("TAP / HOLD", StringComparison.Ordinal),
                "the same right pane opens the Action library with the selected key as its drag destination");
            window.CloseActionPaletteForTest();
            Pump(window);
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
                && mouseButtons.Where(x => x.IsEnabled && !Equals(x.Tag, "MouseLeft")).All(x => MainWindow.GetIsMultiSelected(x) && x.Opacity == 1 && MainWindow.GetIsSelectionPulseActive(x) && !MainWindow.HasSelectionPulseAnimationForTest(x))
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
            Check(window.EditorUndoButton.IsEnabled && !window.EditorRedoButton.IsEnabled, "a completed assignment change enables the toolbar undo command");
            var editorUndoIcon = Descendants<System.Windows.Shapes.Path>(window.EditorUndoButton).Single();
            var editorRedoIcon = Descendants<System.Windows.Shapes.Path>(window.EditorRedoButton).Single();
            Check(window.EditorUndoButton.Content is Viewbox && window.EditorRedoButton.Content is Viewbox
                && editorUndoIcon.StrokeStartLineCap == PenLineCap.Round && editorUndoIcon.StrokeEndLineCap == PenLineCap.Round
                && editorRedoIcon.StrokeStartLineCap == PenLineCap.Round && editorRedoIcon.StrokeEndLineCap == PenLineCap.Round
                && Math.Abs(editorUndoIcon.StrokeThickness - 1.8) < .001 && Math.Abs(editorRedoIcon.StrokeThickness - 1.8) < .001,
                "toolbar undo and redo use matching simple rounded horizontal-arrow paths instead of the bent font glyphs");
            window.EditorUndoButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.CurrentProfileForTest.Mappings.LastOrDefault(x => x.Input == "B") is { Kind: ActionKind.Shortcut, Value: "Ctrl+B" }
                && window.EditorRedoButton.IsEnabled, "toolbar undo restores the complete assignment state and enables redo");
            window.EditorRedoButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.CurrentProfileForTest.Mappings.LastOrDefault(x => x.Input == "B") is { Kind: ActionKind.Text, Value: "multi-A" }
                && window.EditorUndoButton.IsEnabled, "toolbar redo reapplies the assignment state");
            var capsPaste = (MenuItem)window.CreateInputContextMenu("CapsLock").Items[1];
            var x1Paste = (MenuItem)window.CreateInputContextMenu("MouseX").Items[1];
            Check(!capsPaste.IsEnabled && capsPaste.ToolTip?.ToString() == "CapsLockは割り当て元にはできません"
                && !x1Paste.IsEnabled && x1Paste.ToolTip?.ToString() == "追加ボタンは入力として使用できません",
                "right-click paste is disabled for CapsLock and X1 with concise reasons");
            window.CurrentProfileForTest.Mappings.RemoveAll(mapping => mapping.Input is "Taskbar+MouseLeft" or "Taskbar+MouseRight");
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "Taskbar+MouseLeft", Layer = "Taskbar", Kind = ActionKind.None, LongPressKind = ActionKind.Key, LongPressValue = "F9" });
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "Taskbar+MouseRight", Layer = "Taskbar", Kind = ActionKind.None, LongPressKind = ActionKind.Key, LongPressValue = "F10" });
            bool staleTaskbarAssignmentsSanitized = InputAssignmentPolicy.SanitizeMappings(window.CurrentProfileForTest.Mappings);
            window.TaskbarLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            var taskbarLeftPaste = (MenuItem)window.CreateInputContextMenu("MouseLeft").Items[1];
            var taskbarRightPaste = (MenuItem)window.CreateInputContextMenu("MouseRight").Items[1];
            taskbarLeftPaste.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            taskbarRightPaste.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            window.MouseRightVisual.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(staleTaskbarAssignmentsSanitized
                && !taskbarLeftPaste.IsEnabled
                && taskbarLeftPaste.ToolTip?.ToString() == "タスクバーの左クリック／ドラッグはWindows専用です"
                && !taskbarRightPaste.IsEnabled
                && taskbarRightPaste.ToolTip?.ToString() == "タスクバーの右クリックはWindows操作専用です"
                && !window.CurrentProfileForTest.Mappings.Any(mapping => mapping.Input == "Taskbar+MouseLeft")
                && window.CurrentProfileForTest.Mappings.Single(mapping => mapping.Input == "Taskbar+MouseRight") is { Kind: ActionKind.None, LongPressKind: ActionKind.Key, LongPressValue: "F10" }
                && window.AssignmentTapNameText.Text == "Windowsの右クリック"
                && window.AssignmentTapDetailText.Text == "TAPは変更できません",
                "normalization and paste fully reserve taskbar left click/drag while taskbar right keeps its native TAP without erasing an existing HOLD");
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            var allLayerSource = new Mapping { Input = "F7", Layer = "通常", Kind = ActionKind.Text, Value = "multi-A", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+Shift+B", LongPressMs = 640, Application = "notepad.exe" };
            window.CurrentProfileForTest.Mappings.RemoveAll(mapping => mapping.Input == "F7");
            window.CurrentProfileForTest.Mappings.Add(allLayerSource);
            var mainKeyboardMenu = window.CreateInputContextMenu("F7");
            var assignAllLayers = mainKeyboardMenu.Items.OfType<MenuItem>().SingleOrDefault(item => item.Header?.ToString() == "全レイヤーに割り当てる");
            var assignAllProfiles = mainKeyboardMenu.Items.OfType<MenuItem>().SingleOrDefault(item => item.Header?.ToString() == "全プロファイルに割り当て");
            mainKeyboardMenu.ApplyTemplate();
            assignAllLayers?.ApplyTemplate();
            var contextMenuSurface = (Border?)mainKeyboardMenu.Template.FindName("ContextMenuSurface", mainKeyboardMenu);
            var contextMenuItemSurface = assignAllLayers == null ? null : (Border?)assignAllLayers.Template.FindName("MenuItemBorder", assignAllLayers);
            var contextMenuSeparatorStyle = System.Windows.Application.Current.TryFindResource(MenuItem.SeparatorStyleKey) as Style;
            var contextMenuSeparator = contextMenuSeparatorStyle == null ? null : new Separator { Style = contextMenuSeparatorStyle };
            contextMenuSeparator?.ApplyTemplate();
            var contextMenuSeparatorLine = contextMenuSeparator == null ? null : (Border?)contextMenuSeparator.Template.FindName("ContextMenuSeparatorLine", contextMenuSeparator);
            Check(contextMenuSurface != null && contextMenuItemSurface != null
                && mainKeyboardMenu.MinWidth >= 180 && mainKeyboardMenu.BorderThickness == new Thickness(0)
                && contextMenuSurface.CornerRadius == new CornerRadius(10)
                && ReferenceEquals(contextMenuSurface.Background, ThemeService.Brush("CardBackground"))
                && contextMenuItemSurface.CornerRadius == new CornerRadius(6)
                && contextMenuSeparatorLine != null
                && ReferenceEquals(contextMenuSeparatorStyle?.BasedOn, System.Windows.Application.Current.Resources["ContextMenuSeparatorStyle"])
                && ReferenceEquals(contextMenuSeparatorLine.Background, ThemeService.Brush("SubtleBorderBrush"))
                && contextMenuSeparatorLine.Opacity <= 0.42
                && contextMenuSeparatorLine.Margin.Left >= 13,
                "the WPF menu separator resource key and all right-click menus share the same compact theme-aware surface, row treatment, and quiet separators");
            assignAllLayers?.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump(window);
            var allLayerCopies = MainWindow.AllAssignmentLayerNames.Select(layer => window.CurrentProfileForTest.Mappings.LastOrDefault(mapping => mapping.Input.Equals(layer + "+F7", StringComparison.OrdinalIgnoreCase))).ToArray();
            var secondaryKeyboardMenu = window.CreateInputContextMenu("Insert");
            Check(assignAllLayers?.IsEnabled == true && assignAllProfiles != null
                && allLayerCopies.All(mapping => mapping is { Kind: ActionKind.Text, Value: "multi-A", LongPressKind: ActionKind.Shortcut, LongPressValue: "Ctrl+Shift+B", LongPressMs: 640, Application: "notepad.exe" })
                && allLayerCopies.Select(mapping => mapping!.Layer).SequenceEqual(MainWindow.AllAssignmentLayerNames)
                && window.CurrentProfileForTest.Mappings.LastOrDefault(mapping => mapping.Input == "F7") == allLayerSource
                && secondaryKeyboardMenu.Items.OfType<MenuItem>().Any(item => item.Header?.ToString() == "全レイヤーに割り当てる")
                && secondaryKeyboardMenu.Items.OfType<MenuItem>().Any(item => item.Header?.ToString() == "全プロファイルに割り当て"),
                "key context menus copy the complete assignment through all layers and expose all-layer/all-profile assignment on both main and secondary keys");
            var singleScopeMenuHeaders = window.CreateInputContextMenu("F7").Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray();
            Check(singleScopeMenuHeaders.Contains("全レイヤーから削除") && singleScopeMenuHeaders.Contains("全プロファイルから削除"),
                "single-key context menus expose removal from every layer and every profile");
            window.MultiSelectToggle.IsChecked = true;
            multiA.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            multiB.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            var multiScopeMenu = window.CreateMultiSelectionContextMenu();
            var multiAssignAllLayers = multiScopeMenu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "全レイヤーに割り当てる");
            var multiAssignAllProfiles = multiScopeMenu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "全プロファイルに割り当て");
            var multiDeleteAllLayers = multiScopeMenu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "全レイヤーから削除");
            var multiDeleteAllProfiles = multiScopeMenu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "全プロファイルから削除");
            multiAssignAllLayers.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump(window);
            bool multiLayerAssignmentsApplied = MainWindow.AllAssignmentLayerNames.All(layer =>
                window.CurrentProfileForTest.Mappings.Any(mapping => mapping.Input == layer + "+A" && mapping.Kind == ActionKind.Text && mapping.Value == "multi-A")
                && window.CurrentProfileForTest.Mappings.Any(mapping => mapping.Input == layer + "+B" && mapping.Kind == ActionKind.Text && mapping.Value == "multi-A"));
            window.EditorUndoButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            bool multiLayerAssignmentUndoneOnce = MainWindow.AllAssignmentLayerNames.All(layer =>
                !window.CurrentProfileForTest.Mappings.Any(mapping => mapping.Input is var input && (input == layer + "+A" || input == layer + "+B")))
                && window.CurrentProfileForTest.Mappings.Any(mapping => mapping.Input == "A")
                && window.CurrentProfileForTest.Mappings.Any(mapping => mapping.Input == "B");
            Check(multiAssignAllLayers.IsEnabled && multiAssignAllProfiles.IsEnabled
                && multiDeleteAllLayers.IsEnabled && multiDeleteAllProfiles.IsEnabled
                && multiLayerAssignmentsApplied && multiLayerAssignmentUndoneOnce,
                "multi-selection exposes all four scope commands, applies every selected key across layers, and restores the whole batch with one undo");
            window.EditorRedoButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.MultiSelectToggle.IsChecked = true;
            multiA.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            multiB.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            var multiDeleteLayerMenu = window.CreateMultiSelectionContextMenu();
            multiDeleteLayerMenu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "全レイヤーから削除").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump(window);
            bool multiLayersDeleted = !window.CurrentProfileForTest.Mappings.Any(mapping => mapping.Input is "A" or "B"
                || MainWindow.AllAssignmentLayerNames.Any(layer => mapping.Input == layer + "+A" || mapping.Input == layer + "+B"));
            window.EditorUndoButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(multiLayersDeleted && window.CurrentProfileForTest.Mappings.Any(mapping => mapping.Input == "A")
                && window.CurrentProfileForTest.Mappings.Any(mapping => mapping.Input == "B"),
                "multi-selection removes every selected key from all layers and restores the complete deletion with one undo");
            window.MultiSelectToggle.IsChecked = true;
            multiA.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            multiB.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.CreateMultiSelectionContextMenu().Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "全プロファイルに割り当て").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump(window);
            bool multiProfilesAssigned = window.ProfilesForTest.Where(profile => !ReferenceEquals(profile, window.CurrentProfileForTest)).All(profile =>
                profile.Mappings.Any(mapping => mapping.Input == "A") && profile.Mappings.Any(mapping => mapping.Input == "B"));
            window.MultiSelectToggle.IsChecked = true;
            multiA.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            multiB.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            window.CreateMultiSelectionContextMenu().Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "全プロファイルから削除").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Pump(window);
            bool multiProfilesDeleted = window.ProfilesForTest.All(profile => !profile.Mappings.Any(mapping => mapping.Input is "A" or "B"));
            window.EditorUndoButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(multiProfilesAssigned && multiProfilesDeleted
                && window.ProfilesForTest.All(profile => profile.Mappings.Any(mapping => mapping.Input == "A") && profile.Mappings.Any(mapping => mapping.Input == "B")),
                "multi-selection applies and removes every selected input across profiles, with one undo restoring the full cross-profile batch");
            multiA.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.MultiDeleteButton.IsEnabled
                && window.MultiDeleteButton.Foreground is SolidColorBrush deleteForeground && deleteForeground.Color == ThemeService.Color("SecondaryText")
                && window.MultiDeleteButton.Background is SolidColorBrush deleteBackground && deleteBackground.Color.A == 0
                && window.MultiDeleteButton.BorderThickness == new Thickness(0),
                "toolbar trash becomes active for one normally selected assigned key while staying flat and neutral until hover");
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
            Check(window.AssignmentTapNameText.Text == "元の入力"
                && window.AssignmentHoldNameText.Text == "設定不可"
                && window.AssignmentHoldDetailText.Text == "通常の英字では長押し不可"
                && window.AssignmentHoldCard.Opacity < 1,
                "an unassigned normal-layer alphabet key explains in the unified summary why HOLD cannot be assigned");
            window.SpaceLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            directKey.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.AssignmentHoldNameText.Text == "未設定"
                && window.AssignmentHoldDetailText.Text.Contains("HOLD", StringComparison.Ordinal)
                && Math.Abs(window.AssignmentHoldCard.Opacity - 1) < .01,
                "the same alphabet key exposes a valid HOLD destination in the Space layer");
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
            bool holdDropApplied = window.ApplyPaletteActionForTest(
                new CatalogAction("ショートカット", "ロック", "", ActionKind.Shortcut, "LWin+L"),
                "Esc",
                "Esc",
                longPress: true);
            Pump(window);
            Check(holdDropApplied
                && window.AssignmentHoldNameText.Text == "ショートカット"
                && window.AssignmentHoldDetailText.Text == "Win + L"
                && window.AssignmentHoldTimingPanel.Visibility == Visibility.Visible
                && window.LegacyAssignmentEditor.Visibility == Visibility.Collapsed,
                $"a HOLD assignment appears in the compact summary with its timing control and no retired direct editor (applied={holdDropApplied}, name={window.AssignmentHoldNameText.Text}, detail={window.AssignmentHoldDetailText.Text}, timing={window.AssignmentHoldTimingPanel.Visibility}, legacy={window.LegacyAssignmentEditor.Visibility})");
            var configuredEscape = window.CurrentProfileForTest.Mappings.LastOrDefault(x => x.Input == "Esc");
            var appliedEscape = window.AppliedMappingForTest("Esc");
            escapeKey.ApplyTemplate();
            var escapeHoldBadge = (Border)escapeKey.Template.FindName("LongPressBadge", escapeKey)!;
            Check(configuredEscape is { Kind: ActionKind.None, LongPressKind: ActionKind.Shortcut, LongPressValue: "LWin+L" }
                && appliedEscape is { Kind: ActionKind.None, LongPressKind: ActionKind.Shortcut, LongPressValue: "LWin+L" }
                && ReferenceEquals(escapeKey.Background, ThemeService.Brush("KeyBackground"))
                && MainWindow.GetHasLongPressAssignment(escapeKey) && !MainWindow.GetHasDualPressAssignment(escapeKey)
                && escapeHoldBadge.Visibility == Visibility.Visible
                && escapeHoldBadge.Background is SolidColorBrush escapeHoldBrush
                && escapeHoldBrush.Color == MainWindow.AssignmentColorFor(new Mapping { Kind = ActionKind.Shortcut, Value = "LWin+L" }),
                "an Esc long-only shortcut is normalized and applied immediately while only its HOLD pill receives the shortcut color");
            window.WorkspaceGrid.RaiseEvent(BlankClick());
            Pump(window);
            Check(CaptureElementForReview(escapeKey, "long-only-hold-key.png"), "the long-only key screenshot is saved");
            var gestureKey = keys.First(x => Equals(x.Tag, "F5"));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals("F5", StringComparison.OrdinalIgnoreCase));
            gestureKey.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.ApplyCatalogActionForTest(new CatalogAction("ジェスチャー", "ウィンドウ操作", "", ActionKind.Gesture, "ウィンドウ操作"), false);
            Pump(window);
            var editingGesture = window.CurrentProfileForTest.Mappings.LastOrDefault(x => x.Input == "F5");
            Check(editingGesture is { Kind: ActionKind.Gesture, Value: "ウィンドウ操作", LongPressKind: ActionKind.None, LongPressValue: "" }
                && window.AssignmentTapNameText.Text == "ジェスチャー：ウィンドウ操作"
                && window.AssignmentHoldNameText.Text == "設定不可"
                && window.AssignmentHoldDetailText.Text == "ジェスチャーとの併用不可",
                "a gesture appears in the unified TAP summary and explains why HOLD is unavailable");
            CaptureForReview(window, "gesture-short-main.png");
            window.CompleteDestinationInputForTest();
            Pump(window);
            Check(window.AppliedMappingForTest("F5") is { Kind: ActionKind.Gesture, Value: "ウィンドウ操作" } && gestureKey.Background is SolidColorBrush gestureBrush && gestureBrush.Color == MainWindow.AssignmentColorFor(new Mapping { Kind = ActionKind.Gesture, Value = "ウィンドウ操作" }), "completing a short gesture stores its reference and colors the assigned key consistently");
            gestureKey.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.ApplyCatalogActionForTest(new CatalogAction("編集", "コピー", "", ActionKind.Shortcut, "Ctrl+C"), false);
            Pump(window);
            Check(window.CurrentProfileForTest.Mappings.Last(x => x.Input == "F5") is { Kind: ActionKind.Shortcut, Value: "Ctrl+C" }
                && window.AssignmentTapNameText.Text == "コピー"
                && window.AssignmentHoldNameText.Text == "未設定"
                && !window.IsEditingSelectedInputForTest,
                "drag-overwriting a gesture with a normal TAP Action refreshes the summary and restores the HOLD destination");
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
            const string profileInput = "F6";
            var profileKey = keys.First(x => Equals(x.Tag, profileInput));
            window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals(profileInput, StringComparison.OrdinalIgnoreCase));
            profileKey.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.LongPressExpander.IsExpanded = true;
            window.ApplyProfileActionForTest("プロファイル4", true);
            Check(Equals(window.LongKindBox.SelectedValue, ActionKind.Profile) && window.LongValueBox.Text == "プロファイル：プロファイル4", "profile assignment selects the profile action button and shows a readable profile name");
            window.LongPressBox.Text = "650";
            Pump(window);
            var editingProfileSwitch = window.CurrentProfileForTest.Mappings.LastOrDefault(x => x.Input == profileInput);
            window.CompleteDestinationInputForTest();
            Pump(window);
            var appliedProfileSwitch = window.AppliedMappingForTest(profileInput);
            Check(editingProfileSwitch is { LongPressKind: ActionKind.Profile, LongPressValue: "プロファイル4", LongPressMs: 650 } && appliedProfileSwitch is { Kind: ActionKind.None, LongPressKind: ActionKind.Profile, LongPressValue: "プロファイル4", LongPressMs: 650 }, "a long-press profile action remains assigned when its timing is edited and after input completion");
            Check(appliedProfileSwitch != null && window.ExecuteMappingForTest(appliedProfileSwitch, profileInput + ":Long"), "the saved long-press profile action is accepted by the runtime executor");
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
            string selectedInputForHold = window.InputName.Text;
            string selectedKeyForHold = key.Tag!.ToString()!;
            bool summaryHoldApplied = window.ApplyPaletteActionForTest(
                new CatalogAction("キー", "Enter", "Enterキーを送信します", ActionKind.Key, "Enter"),
                selectedInputForHold,
                selectedKeyForHold,
                longPress: true);
            Pump(window);
            window.LongPressDurationSlider.Value = 650;
            Pump(window);
            Check(summaryHoldApplied
                && window.AssignmentHoldNameText.Text == "Enter"
                && window.AssignmentHoldTimingPanel.Visibility == Visibility.Visible
                && window.AssignmentHoldDurationText.Text == "0.65秒"
                && window.CurrentProfileForTest.Mappings.Last(mapping => mapping.Input == selectedInputForHold).LongPressMs == 650,
                "the unified HOLD summary exposes readable seconds and persists its selected timing");
            Check(window.LegacyAssignmentEditor.Visibility == Visibility.Collapsed
                && window.AssignmentSummaryPanel.Visibility == Visibility.Visible,
                "the fixed inspector keeps the unified summary and never restores the retired direct form");
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
            Check(window.InputName.Text == "MouseRight+S"
                && window.InputDisplayText.Text == "右クリック + S"
                && !window.ValueBox.IsKeyboardFocusWithin
                && window.AssignmentTapNameText.Text == "元の入力",
                "input detection recognizes a held layer plus the next key and opens its unified assignment summary");
            window.KindBox.SelectedValue = ActionKind.Shortcut;
            window.ValueBox.Text = "Ctrl+C";
            window.CompleteDestinationInputForTest();
            window.BeginInputDetectionForTest();
            window.FeedDetectedInputForTest("MouseRight Layer Down");
            window.FeedDetectedInputForTest("S Down");
            Pump(window);
            Check(!window.ValueBox.IsKeyboardFocusWithin, "input detection leaves the caret out when the detected action already has execution content");
            Check(Descendants<System.Windows.Controls.Button>(window.AssignmentPane).Contains(window.ActionPaletteButton)
                && window.LegacyAssignmentEditor.Visibility == Visibility.Collapsed
                && window.AssignmentSummaryPanel.Visibility == Visibility.Visible,
                "input detection keeps the shared Action launcher and never restores the retired direct form");
            var assignmentTexts = Descendants<TextBlock>(window.AssignmentSummaryPanel).Select(x => x.Text).ToArray();
            Check(assignmentTexts.Contains("TAP") && assignmentTexts.Contains("HOLD")
                && assignmentTexts.Any(text => text.Contains("ドラッグ", StringComparison.Ordinal))
                && !assignmentTexts.Any(text => text is "実行する操作" or "長押しの操作"),
                "the inspector uses concise TAP/HOLD summaries and one drag-overwrite instruction");
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
            Check(settingsSwitches.Length == 12 && settingsSwitches.All(appSwitch => appSwitch.Template.FindName("SwitchTrack", appSwitch) is Border),
                "all twelve remaining settings checkboxes render through the shared RELYR switch template");
            Check(Descendants<System.Windows.Controls.ScrollViewer>(settings).All(scroll => ReferenceEquals(scroll, settings.LayersScrollPanel)), "only the longer layer category uses a bounded scroll surface");
            settings.CategoryList.SelectedIndex = 6;
            settings.UpdateLayout();
            Check(settings.UpdatePanel.Visibility == Visibility.Visible && settings.GeneralPanel.Visibility == Visibility.Collapsed && settings.CheckForUpdatesButton.Content?.ToString() == "アップデートを確認" && settings.InstallUpdateButton.Content?.ToString() == "今すぐアップデート" && settings.InstallUpdateButton.Visibility == Visibility.Visible && settings.UpdateStatusText.Text.Contains("v99.0.0") && settings.UpdateStatusText.Foreground is SolidColorBrush availableBrush && availableBrush.Color == ThemeService.Color("WarningBrush") && !settings.UpdateStatusText.Text.EndsWith('。'), "available update uses a clear orange status without unnecessary terminal punctuation");
            settings.ApplyUpdateResult(new UpdateCheckResult(MainWindow.RunningVersion, MainWindow.DisplayVersion, null, DateTimeOffset.Now), true);
            Check(settings.UpdateStatusText.Text == $"最新バージョンです（v{MainWindow.DisplayVersion}）" && settings.UpdateStatusText.Foreground is SolidColorBrush currentBrush && currentBrush.Color == ThemeService.Color("AccentBrush"), "current version uses a concise green status");
            var updateNotes = new UpdateNotesWindow("9.9.9", "- Deckを見やすく改善\n- 全画面表示を追加");
            updateNotes.Show();
            PumpFor(TimeSpan.FromMilliseconds(80));
            Check(updateNotes.VersionText.Text.Contains("v9.9.9", StringComparison.Ordinal)
                  && updateNotes.NotesText.Text.Contains("全画面表示", StringComparison.Ordinal),
                "the post-update window shows the installed version and GitHub release body without rewriting it");
            Check(updateNotes.Background == ThemeService.Brush("AppBackground")
                  && updateNotes.HeadingText.Foreground == ThemeService.Brush("PrimaryText")
                  && updateNotes.NotesText.Foreground == ThemeService.Brush("PrimaryText")
                  && updateNotes.NotesSurface.Background == ThemeService.Brush("SurfaceBackground")
                  && updateNotes.NotesSurface.BorderBrush == ThemeService.Brush("SubtleBorderBrush")
                  && updateNotes.ConfirmButton.Foreground == ThemeService.Brush("AccentButtonText"),
                "the post-update window uses readable dark-theme text and surfaces instead of WPF's black default foreground");
            ThemeService.Apply(AppThemeMode.Light);
            PumpFor(TimeSpan.FromMilliseconds(40));
            Check(updateNotes.Background == ThemeService.Brush("AppBackground")
                  && updateNotes.HeadingText.Foreground == ThemeService.Brush("PrimaryText")
                  && updateNotes.NotesText.Foreground == ThemeService.Brush("PrimaryText")
                  && updateNotes.NotesSurface.Background == ThemeService.Brush("SurfaceBackground")
                  && updateNotes.ConfirmButton.Foreground == ThemeService.Brush("AccentButtonText"),
                "the visible post-update window follows a live switch to the readable light palette");
            ThemeService.Apply(AppThemeMode.Dark);
            PumpFor(TimeSpan.FromMilliseconds(40));
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
            Check(settings.ArchiveWatchFolderBox.Text == Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                && settings.ArchiveDestinationFolderBox.Text == ""
                && settings.ArchiveOverlayBox.IsChecked == true
                && !settings.ArchiveOverlayBox.IsEnabled
                && Descendants<System.Windows.Controls.Button>(settings.ArchivePanel).Count(x => x.Content?.ToString() == "参照…") == 2,
                "archive settings provide separate folders and a default-on overlay switch that follows the auto-extraction master switch");
            settings.ExtractBox.IsChecked = true;
            Check(settings.ArchiveOverlayBox.IsEnabled && settings.ShowArchiveExtractionOverlay,
                "enabling auto extraction also enables its compact progress-overlay preference");
            var archiveOverlay = new ArchiveProgressOverlay();
            archiveOverlay.ShowActivity(new ArchiveActivity(ArchiveActivityState.Extracting, "sample.zip"));
            PumpFor(TimeSpan.FromMilliseconds(40));
            Check(archiveOverlay.UsesCompactClickThroughSurfaceForTest && archiveOverlay.UsesNativeClickThroughStylesForTest,
                "the archive progress surface is compact, non-activating, and native click-through so it cannot block the desktop or taskbar");
            archiveOverlay.CloseForProcessExit();
            settings.CategoryList.SelectedIndex = 5;
            settings.UpdateLayout();
            Check(settings.ResetAllButton.Background is SolidColorBrush resetBrush && resetBrush.Color == ThemeService.Color("ControlBackground")
                && settings.ResetAllButton.Foreground is SolidColorBrush resetForeground && resetForeground.Color == ThemeService.Color("DangerBrush"),
                "data settings keep the destructive reset command neutral until hover while retaining a restrained danger cue");
            settings.Close();
            var themeSettings = new SettingsWindow(new AppConfig { ThemeMode = AppThemeMode.System });
            themeSettings.UpdateLayout();
            Check(themeSettings.SystemThemeBox != null && themeSettings.LightThemeBox != null && themeSettings.DarkThemeBox != null
                && themeSettings.UiAnimationsBox.IsChecked == true && themeSettings.UiAnimationsEnabled,
                "appearance settings offer system, light, and dark modes with RELYR animations enabled by default");
            themeSettings.UiAnimationsBox.IsChecked = false;
            Check(!themeSettings.UiAnimationsEnabled, "the RELYR animation switch can disable motion independently of Windows settings");
            themeSettings.LightThemeBox!.IsChecked = true;
            window.UpdateLayout();
            Check(!ThemeService.UsesDark && ThemeService.Color("AppBackground").R > 200 && ThemeService.Color("PrimaryText").R < 80, "light mode uses a genuinely light background with dark readable text");
            System.Windows.Media.Color CategoryGlyphColor(string category)
            {
                window.SelectActionPalettePopupItemForTest(category);
                var option = categoryOptions.First(candidate => candidate.Name == category);
                window.ActionPaletteCategoryBox.IsDropDownOpen = true;
                window.UpdateLayout();
                Pump(window);
                var container = (ComboBoxItem)window.ActionPaletteCategoryBox.ItemContainerGenerator.ContainerFromItem(option)!;
                var glyph = Descendants<TextBlock>(container).First(text => text.Text == option.Glyph);
                var color = ((SolidColorBrush)glyph.Foreground).Color;
                window.ActionPaletteCategoryBox.IsDropDownOpen = false;
                return color;
            }
            bool lightKeyCategoryColor = CategoryGlyphColor("キー") == ThemeService.Color("ActionKeyIconBrush");
            bool lightMacroCategoryColor = CategoryGlyphColor("マクロ") == ThemeService.Color("ActionMacroIconBrush");
            bool lightLaunchCategoryColor = CategoryGlyphColor("インストールアプリ") == ThemeService.Color("ActionLaunchIconBrush");
            Check(lightKeyCategoryColor && lightMacroCategoryColor && lightLaunchCategoryColor, "the unified Action categories use their distinct readable key, macro, and app colors in the light theme");
            themeSettings.DarkThemeBox!.IsChecked = true;
            window.UpdateLayout();
            Check(ThemeService.UsesDark && ThemeService.Color("AppBackground").R < 40 && ThemeService.Color("PrimaryText").R > 180, "dark mode retains the established dark palette");
            bool darkKeyCategoryColor = CategoryGlyphColor("キー") == ThemeService.Color("ActionKeyIconBrush");
            bool darkMacroCategoryColor = CategoryGlyphColor("マクロ") == ThemeService.Color("ActionMacroIconBrush");
            bool darkLaunchCategoryColor = CategoryGlyphColor("インストールアプリ") == ThemeService.Color("ActionLaunchIconBrush");
            Check(darkKeyCategoryColor && darkMacroCategoryColor && darkLaunchCategoryColor, "the unified Action categories keep their distinct readable key, macro, and app colors in the dark theme");
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
            settingsWithAutoSave.SelectCategory("Privacy");
            settingsWithAutoSave.UpdateLayout();
            Check(settingsWithAutoSave.PrivacyPanel.ActualHeight <= ((FrameworkElement)settingsWithAutoSave.PrivacyPanel.Parent).ActualHeight + .5
                && settingsWithAutoSave.DetailedDiagnosticsBox.IsChecked == false
                && Descendants<System.Windows.Controls.TextBlock>(settingsWithAutoSave.PrivacyPanel).Any(x => x.Text.Contains("外部へ送信されることはありません", StringComparison.Ordinal))
                && Descendants<System.Windows.Controls.Button>(settingsWithAutoSave.PrivacyPanel).Any(x => x.Content?.ToString() == "診断ログを削除"),
                "privacy settings keep detailed diagnostics opt-in and disclose local-only bounded log storage with deletion control");
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
            Check(profileManager.TitleBarUsesDarkMode == MainWindow.IsWindowsAppDarkMode() && profileManager.ProfileList.Items.Count == 2
                && profileManager.CopyAssignmentsButton.Content is TextBlock && profileManager.CopyAssignmentsButton.ToolTip?.ToString() == "割り当てをコピー"
                && profileManager.RunningApplicationsTab.Content?.ToString() == "起動中"
                && profileManager.InstalledApplicationsTab.Content?.ToString() == "インストール済み"
                && profileManager.RunningApplicationsTab.IsChecked == true,
                "dedicated profile manager provides assignment clipboard and a simple in-place source switch for running and installed applications");
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
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "Space+MouseLeft", Layer = "Space", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+K" });
            spaceMouseLeft.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            var shiftClickAction = ActionCatalog.Items.Single(x => x.Name == "Shift+左クリック");
            window.ApplyCatalogActionForTest(shiftClickAction);
            Pump(window);
            var editingShiftClick = window.CurrentProfileForTest.Mappings.LastOrDefault(x => x.Input == "Space+MouseLeft");
            Check((ActionKind?)window.KindBox.SelectedValue == ActionKind.Shortcut
                && editingShiftClick is { Kind: ActionKind.Mouse, Value: "ShiftDrag", LongPressKind: ActionKind.None, LongPressValue: "" }
                && window.LongPressExpander is { IsEnabled: false, IsExpanded: false }
                && !window.LongPressOnlyButton.IsEnabled
                && window.LongPressExpander.Header?.ToString() == "＋ 長押し（短押しの修飾クリックとは併用できません）",
                "choosing Shift+left click clears an unreachable long action and disables its editor with a concise reason");
            window.ApplyCatalogActionForTest(new CatalogAction("編集", "コピー", "", ActionKind.Shortcut, "Ctrl+C"), true);
            Pump(window);
            Check(editingShiftClick is { LongPressKind: ActionKind.None, LongPressValue: "" }, "a disabled modifier-click long editor cannot add an action through the assignment path");
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
            var wheelUpInput = window.VisualInputButtonsForTest.First(x => Equals(x.Tag, "WheelUp"));
            window.CurrentProfileForTest.Mappings.RemoveAll(mapping => mapping.Input.Equals("WheelUp", StringComparison.OrdinalIgnoreCase));
            window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = "WheelUp", Layer = "通常", Kind = ActionKind.Shortcut, Value = "Ctrl+C" });
            wheelUpInput.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            var wheelMapping = window.CurrentProfileForTest.Mappings.Single(mapping => mapping.Input == "WheelUp");
            window.ApplyCatalogActionForTest(new CatalogAction("その他", "テスト", "", ActionKind.Gesture, "Gesture"));
            window.ApplyCatalogActionForTest(new CatalogAction("編集", "コピー", "", ActionKind.Shortcut, "Ctrl+C"), longPress: true);
            Pump(window);
            Check(!window.ShortGestureOptionEnabledForTest
                && window.LongPressExpander is { IsEnabled: false, IsExpanded: false }
                && window.LongPressExpander.Header?.ToString() == "＋ 長押し（ホイール／チルトでは設定できません）"
                && wheelMapping is { Kind: ActionKind.Shortcut, Value: "Ctrl+C", LongPressKind: ActionKind.None, LongPressValue: "" },
                "wheel input visibly disables gesture and long press, and hidden assignment paths cannot add either action");

            window.CurrentProfileForTest.Mappings.RemoveAll(mapping => mapping.Input.Equals("MouseRight", StringComparison.OrdinalIgnoreCase)
                || mapping.Input.StartsWith("MouseRight+", StringComparison.OrdinalIgnoreCase));
            var directRight = new Mapping
            {
                Input = "MouseRight",
                Layer = "通常",
                Kind = ActionKind.Mouse,
                Value = "MouseRight",
                LongPressKind = ActionKind.Shortcut,
                LongPressValue = "Ctrl+K"
            };
            window.CurrentProfileForTest.Mappings.Add(directRight);
            window.RightMouseLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            var rightLayerSource = window.VisualInputButtonsForTest.First(x => Equals(x.Tag, "MouseRight"));
            var x1LayerSource = window.VisualInputButtonsForTest.First(x => Equals(x.Tag, "MouseX"));
            Check(!rightLayerSource.IsEnabled && rightLayerSource.ToolTip?.ToString() == "レイヤーと同じボタンには設定できません"
                && !x1LayerSource.IsEnabled && x1LayerSource.ToolTip?.ToString() == "追加ボタンは入力として使用できません",
                "a mouse layer disables its own source button and X1 with concise reasons");
            Check(window.ApplyPaletteActionForTest(new CatalogAction("編集", "コピー", "", ActionKind.Shortcut, "Ctrl+C"), "MouseRight+K", "K")
                && directRight is { LongPressKind: ActionKind.None, LongPressValue: "" },
                "adding a right-click-layer action automatically removes the now-unreachable default right-click long action");
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            window.VisualInputButtonsForTest.First(x => Equals(x.Tag, "MouseRight")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(window.LongPressExpander is { IsEnabled: false, IsExpanded: false }
                && window.LongPressExpander.Header?.ToString() == "＋ 長押し（レイヤー使用中は設定できません）",
                "default right-click shows that long press is unavailable while its layer is in use");
            window.CompleteDestinationInputForTest();
            Pump(window);
            var sampleApps = new[] { new InstalledApplicationInfo("RELYR テスト", "C:\\Apps\\RELYR.exe", "インストール済みアプリ"), new InstalledApplicationInfo("メモ帳", "C:\\Windows\\notepad.exe", "スタート メニュー") };
            var applicationPicker = new ApplicationPickerWindow(sampleApps) { Owner = window, ShowInTaskbar = false };
            applicationPicker.Show();
            applicationPicker.UpdateLayout();
            Check(applicationPicker.TitleBarUsesDarkMode == MainWindow.IsWindowsAppDarkMode() && applicationPicker.ApplicationList.Items.Count == 2, "application picker follows the Windows theme and shows installed applications");
            Check(Descendants<System.Windows.Controls.Image>(applicationPicker.ApplicationList).Any(image => image.Source != null), "application picker shows an application icon beside every visible choice");
            applicationPicker.ManualTargetBox.Text = "  https://example.com/RELYR path  ";
            Pump(window);
            Check(applicationPicker.SelectButton.IsEnabled
                && applicationPicker.BrowseFolderButton.Visibility == Visibility.Visible
                && ApplicationPickerWindow.NormalizeManualTarget(applicationPicker.ManualTargetBox.Text) == "https://example.com/RELYR path",
                "application picker accepts direct file, folder, and URL targets while trimming only path-edge whitespace");
            applicationPicker.ManualTargetBox.Clear();
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
            Check(new[] { macro.MacroListPane, macro.MacroStepsPane, macro.MacroToolsPane }.All(pane => pane.BorderThickness == new Thickness(0) && pane.Background is SolidColorBrush brush && brush.Color.A == 0)
                && macro.MacroList.BorderThickness == new Thickness(0) && macro.StepList.BorderThickness == new Thickness(0),
                "macro manager panes and working lists are separated by spacing instead of nested permanent frames");
            var macroListActions = new[] { macro.NewMacroButton, macro.DuplicateMacroButton, macro.EditMacroButton, macro.DeleteMacroButton };
            Check(macroListActions.All(button => button.Content is TextBlock text && text.FontFamily.Source == "Segoe MDL2 Assets" && Math.Abs(button.ActualHeight - 40) < .1 && button.ActualWidth >= 40 && button.BorderThickness == new Thickness(0)) && macroListActions.Max(button => button.ActualWidth) - macroListActions.Min(button => button.ActualWidth) < .1 && macroListActions.All(button => button.ToolTip != null), "macro list actions use four equal flat icon-only controls with complete hit targets and descriptive tooltips");
            Check(new[] { macro.ManualModeButton, macro.RecordModeButton, macro.StepEditModeButton }.SelectMany(Descendants<TextBlock>).Select(x => x.Text).Where(x => x is "追加" or "記録" or "編集").SequenceEqual(["追加", "記録", "編集"]) && macro.EditorTabs.Template != null && macro.DropIndicator.Visibility == Visibility.Collapsed, "macro editing modes use compact icon-labelled controls and keep the drag insertion guide hidden until needed");
            var macroCommandIcons = Descendants<TextBlock>(macro).Where(text => Equals(text.Tag, "MacroCommandIcon")).ToArray();
            Check(macroCommandIcons.Length >= 7 && macroCommandIcons.All(icon => Math.Abs(icon.ActualWidth - 28) < .1 && Math.Abs(icon.ActualHeight - 22) < .1 && icon.TextAlignment == TextAlignment.Center),
                "manual macro command icons, including the text T, use one centered alignment plane");
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
                && macroStepNumbers.Length == macroConfig.Macros[0].Steps.Count && macroStepNumbers.All(number => Math.Abs(number.ActualWidth - 34) < .1 && number.BorderThickness == new Thickness(0)),
                "every macro step has a readable flat fixed number and an explicit three-dot drag handle");
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
                    string input = layer == "通常" ? visualKey : layer + "+" + visualKey;
                    if (visualKey == "CapsLock" || (visualKey == "Space" && layer is "通常" or "Space") || (visualKey == "MouseLeft" && layer is "通常" or "Taskbar")
                        || (visualKey == "MouseRight" && layer == "Taskbar")
                        || InputAssignmentPolicy.IsUnreachableInput(input))
                        continue;
                    window.CurrentProfileForTest.Mappings.RemoveAll(x => x.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
                    window.CurrentProfileForTest.Mappings.Add(new Mapping { Input = input, Layer = layer, Kind = ActionKind.Key, Value = "A" });
                }
                layerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump(window);
                var missed = visualInputs.Where(x =>
                {
                    string key = (string)x.Tag;
                    string input = layer == "通常" ? key : layer + "+" + key;
                    return key != "CapsLock" && !(key == "Space" && layer is "通常" or "Space")
                        && !(key == "MouseLeft" && layer is "通常" or "Taskbar")
                        && !(key == "MouseRight" && layer == "Taskbar") && !InputAssignmentPolicy.IsUnreachableInput(input)
                        && !HasBackgroundColor(x, replacementColor);
                }).Select(x => (string)x.Tag).Distinct().ToArray();
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
                window.CloseActionPaletteForTest();
                layerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump(window);
                var layerF2 = visualInputs.First(button => Equals(button.Tag, "F2"));
                layerF2.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Pump(window);
                window.OpenActionPaletteForTest();
                Pump(window);
                window.ActionPaletteSearchBox.Focus();
                System.Windows.Input.Keyboard.Focus(window.ActionPaletteSearchBox);
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
                everyRuntimeActionWorkedOnEveryLayerEditor &= window.ActionPaletteSearchBox.IsKeyboardFocusWithin
                    && window.AppliedProfileNameForTest == runtimeProfileBeforeLayerSelection
                    && staleMouseLeftLayerMappingIgnored
                    && f1Down == (IntPtr)1 && f1Up == (IntPtr)1 && requested
                    && unassignedF24Down != (IntPtr)1 && unassignedF24Up != (IntPtr)1
                    && new[] { spaceDown, jDown, jUp, spaceUp, capsDown, kDown, kUp, capsUp, rightDown, lDown, lUp, rightUp, backDown, mDown, mUp, backUp, forwardDown, nDown, nUp, forwardUp }.All(result => result == (IntPtr)1)
                    && allKeyActionsProduced && mappedChordsDidNotReplaySourceClicks && ordinaryClicksPreserved
                    && spaceStateClean && capsStateClean && rightStateClean && backStateClean && forwardStateClean
                    && unassignedLeftStateClean && plainRightStateClean && plainBackStateClean && plainForwardStateClean;
            }
            window.CloseActionPaletteForTest();
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
            var requiredPickerInputs = new[] { "A", "Enter", "F13", "F24", "NumPadEnter", "MouseLeft", "MouseRight", "MouseMiddle", "MouseBack", "MouseForward", "WheelUp", "WheelDown", "TiltLeft", "TiltRight" };
            var missingPickerInputs = requiredPickerInputs.Where(x => !pickerInputs.Contains(x)).ToArray();
            var invalidPickerInputs = pickerInputs.Where(input => !InputEngine.IsValidRecordedEvent(input + " Down") || !InputEngine.IsValidRecordedEvent(input + " Up")).ToArray();
            bool pickerComplete = inputPicker.TitleBarUsesDarkMode == MainWindow.IsWindowsAppDarkMode() && pickerInputs.Count > 100
                && missingPickerInputs.Length == 0 && invalidPickerInputs.Length == 0 && !pickerInputs.Contains("MouseX");
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
            Check(ThemeService.Color("GestureRowHoverBackground") != ThemeService.Color("AppBackground")
                && DeckPanelLayout.ContrastRatio(ThemeService.Color("GestureRowHoverBackground"), ThemeService.Color("AppBackground")) > 1.1,
                "light gesture rows use a visibly distinct but restrained hover surface");
            window.ProfileBox.IsDropDownOpen = true;
            Pump(window);
            var selectedProfileItem = window.ProfileBox.ItemContainerGenerator.ContainerFromItem(window.ProfileBox.SelectedItem) as ComboBoxItem;
            var selectedProfileCheck = selectedProfileItem?.Template.FindName("SelectedCheck", selectedProfileItem) as TextBlock;
            Check(selectedProfileItem != null && selectedProfileItem.Template != null && selectedProfileItem.Foreground is SolidColorBrush profileItemForeground && profileItemForeground.Color == ThemeService.Color("PrimaryText") && selectedProfileCheck?.Opacity == 1, "light profile choices retain a readable theme-aware row with a restrained selected check");
            window.ProfileBox.IsDropDownOpen = false;
            Pump(window);
            var lightProfileManager = new ProfileManagerWindow([new Profile { Name = "標準" }, new Profile { Name = "編集用" }], "標準") { Owner = window, ShowInTaskbar = false };
            lightProfileManager.Show();
            lightProfileManager.UpdateLayout();
            var lightProfilePrimaryButtons = new[] { lightProfileManager.AddProfileButton, lightProfileManager.RenameProfileButton, lightProfileManager.DeleteProfileButton };
            Check(lightProfilePrimaryButtons.All(button => Descendants<System.Windows.Shapes.Path>(button).All(path => Equals(path.Stroke, button.Foreground)))
                && !lightProfileManager.DeleteProfileButton.IsEnabled
                && ReferenceEquals(lightProfileManager.DeleteProfileButton.Background, ThemeService.Brush("ControlBackground"))
                && ReferenceEquals(lightProfileManager.DeleteProfileButton.Foreground, ThemeService.Brush("MutedText")),
                "profile action icons inherit theme-aware colors and an unavailable delete command stays neutral instead of permanently red");
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
            var lightDeckSurfaceColor = ThemeService.Color("AppBackground");
            int lightDeckContrast = Math.Abs(lightDeckCellColor.R - lightDeckSurfaceColor.R) + Math.Abs(lightDeckCellColor.G - lightDeckSurfaceColor.G) + Math.Abs(lightDeckCellColor.B - lightDeckSurfaceColor.B);
            var lightDeckNameTops = lightDeckCards.Select(card => Descendants<TextBlock>(card).Single(text => Equals(text.Tag, "DeckLayoutName")).TranslatePoint(new System.Windows.Point(), card).Y).ToArray();
            Check(lightDeckPreviewCells.Length > 0 && lightDeckContrast >= 36 && lightDeckCards.All(card => card.BorderThickness == new Thickness(0)), $"light-theme Deck thumbnails remain visible in the borderless gallery (cells={lightDeckPreviewCells.Length}, contrast={lightDeckContrast})");
            Check(lightDeckNameTops.Length > 0 && lightDeckNameTops.Max() - lightDeckNameTops.Min() < .1, $"every Deck card places its name on one horizontal line (spread={lightDeckNameTops.Max() - lightDeckNameTops.Min():F2})");
            ThemeService.Apply(AppThemeMode.Dark);
            Pump(window);
            var darkDeckCardsAfterSwitch = window.DeckLayoutCardsPanel.Children.OfType<System.Windows.Controls.Button>().Where(button => Descendants<TextBlock>(button).Any(text => Equals(text.Tag, "DeckLayoutName"))).ToArray();
            Check(darkDeckCardsAfterSwitch.Length == lightDeckCards.Length
                && darkDeckCardsAfterSwitch.All(card => card.BorderThickness == new Thickness(0))
                && darkDeckCardsAfterSwitch.Where(card => card.Tag is DeckLayoutDefinition layout && !window.CurrentProfileForTest.DefaultDeckLayoutId.Equals(layout.Id, StringComparison.OrdinalIgnoreCase)).All(card => card.Background is SolidColorBrush brush && brush.Color.A == 0)
                && darkDeckCardsAfterSwitch.All(card => Descendants<TextBlock>(card).Single(text => Equals(text.Tag, "DeckLayoutName")).Foreground is SolidColorBrush brush && brush.Color == ThemeService.Color("PrimaryText")),
                "Deck gallery remains flat and fully recolors when switching directly from light to dark theme");
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
            window.EditDeckLayoutForTest(standardDeck);
            window.OpenActionPaletteForTest();
            Pump(window);
            bool deckPaletteIncludedMonitors = window.ActionPaletteCategoryBox.Items.Cast<object>().Any(item => item.ToString() == DeckMonitorCatalog.Category);
            window.NormalLayerButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Pump(window);
            Check(deckPaletteIncludedMonitors
                && !window.IsActionPaletteOpenForTest
                && window.ActionPalettePane.Visibility == Visibility.Collapsed
                && window.KeyboardWorkspace.Visibility == Visibility.Visible,
                "switching from Deck editing to a keyboard layer closes the Action pane so Deck-only monitor choices cannot remain visible");
            window.OpenActionPaletteForTest();
            Pump(window);
            Check(!window.ActionPaletteCategoryBox.Items.Cast<object>().Any(item => item.ToString() == DeckMonitorCatalog.Category),
                "reopening the Action pane on a keyboard layer rebuilds its categories without the Deck-only monitor library");
            window.CloseActionPaletteForTest();
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
    static bool CaptureElementForReview(FrameworkElement element, string fileName)
    {
        string? directory = Environment.GetEnvironmentVariable("RELYR_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return true;
        element.UpdateLayout();
        int width = Math.Max(1, (int)Math.Ceiling(element.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(element.ActualHeight));
        if (width <= 1 || height <= 1)
            return false;
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(directory);
        using var stream = File.Create(Path.Combine(directory, fileName));
        encoder.Save(stream);
        return true;
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
    static bool CaptureContextMenuForReview(System.Windows.Controls.ContextMenu menu, string fileName)
    {
        IntPtr hwnd = (System.Windows.PresentationSource.FromVisual(menu) as System.Windows.Interop.HwndSource)?.Handle ?? IntPtr.Zero;
        if (hwnd != IntPtr.Zero && CaptureNativeWindowForReview(hwnd, fileName, 18))
            return true;
        string? directory = Environment.GetEnvironmentVariable("RELYR_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return true;
        try
        {
            menu.UpdateLayout();
            System.Windows.Point topLeft = menu.PointToScreen(new System.Windows.Point());
            return CaptureScreenRegionForReview(
                (int)Math.Floor(topLeft.X) - 18,
                (int)Math.Floor(topLeft.Y) - 18,
                (int)Math.Ceiling(menu.ActualWidth) + 36,
                (int)Math.Ceiling(menu.ActualHeight) + 36,
                fileName);
        }
        catch
        {
            return false;
        }
    }
    static bool CaptureElementsForReview(Window window, IReadOnlyCollection<FrameworkElement> elements, string fileName)
    {
        string? directory = Environment.GetEnvironmentVariable("RELYR_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return true;
        try
        {
            var visible = elements.Where(element => element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0).ToArray();
            if (visible.Length == 0)
                return false;
            var topLeft = visible.Select(element => element.PointToScreen(new System.Windows.Point())).ToArray();
            var bottomRight = visible.Select(element => element.PointToScreen(new System.Windows.Point(element.ActualWidth, element.ActualHeight))).ToArray();
            const int padding = 16;
            int left = (int)Math.Floor(topLeft.Min(point => point.X)) - padding;
            int top = (int)Math.Floor(topLeft.Min(point => point.Y)) - padding;
            int right = (int)Math.Ceiling(bottomRight.Max(point => point.X)) + padding;
            int bottom = (int)Math.Ceiling(bottomRight.Max(point => point.Y)) + padding;
            return CaptureScreenRegionForReview(left, top, right - left, bottom - top, fileName);
        }
        catch
        {
            return false;
        }
    }
    static bool CaptureNativeWindowForReview(IntPtr hwnd, string fileName, int padding)
    {
        string? directory = Environment.GetEnvironmentVariable("RELYR_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return true;
        try
        {
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var rect))
                return false;
            return CaptureScreenRegionForReview(
                rect.Left - padding,
                rect.Top - padding,
                rect.Right - rect.Left + padding * 2,
                rect.Bottom - rect.Top + padding * 2,
                fileName);
        }
        catch
        {
            return false;
        }
    }
    static bool CaptureScreenRegionForReview(int left, int top, int width, int height, string fileName)
    {
        string? directory = Environment.GetEnvironmentVariable("RELYR_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(directory))
            return true;
        try
        {
            using var bitmap = new System.Drawing.Bitmap(Math.Max(1, width), Math.Max(1, height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(left, top, 0, 0, bitmap.Size, System.Drawing.CopyPixelOperation.SourceCopy);
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
