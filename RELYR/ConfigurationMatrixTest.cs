using System.IO;

namespace RELYR;

/// <summary>
/// Exercises user-configurable behavior without installing hooks or emitting
/// input to Windows. Every engine event is delivered directly to a fresh
/// instance and every mapped output is captured by <see cref="FakeOutput"/>.
/// </summary>
internal static class ConfigurationMatrixTest
{
    internal static int Run(TextWriter output)
    {
        var report = new VerificationReport(output);
        int cases = 0;
        string directory = VerificationPaths.CreateRunDirectory("configuration-matrix");
        try
        {
            TestCatalogAndExecution(report, ref cases);
            TestInputAssignmentPolicy(report, ref cases);
            TestValidConfigurationCrossProduct(report, ref cases);
            TestNormalizationAndRejection(report, directory, ref cases);
            TestDeckDimensions(report, ref cases);
            TestProfileRouting(report, ref cases);
            TestMappingApplicationRecovery(report, ref cases);
            TestMacroGraphs(report, ref cases);
            TestModifierClickOutputOrder(report, ref cases);
            TestLayerStateTransitions(report, ref cases);
            TestKeyboardFailOpenMatrix(report, ref cases);
            TestUnassignedMouseLayerClicks(report, ref cases);
            TestRightWheelMovementDoesNotCreateContextClick(report, ref cases);
            TestRawReleaseRecovery(report, ref cases);
            TestExtendedMouseButtonIdentity(report, ref cases);
        }
        catch (Exception ex)
        {
            report.RecordException("configuration matrix exception", "FAIL exception: ", ex);
        }
        finally
        {
            try { Directory.Delete(directory, true); } catch { }
        }
        output.WriteLine($"CASES={cases}");
        return report.Complete("CONFIGURATION MATRIX PASSED", "CONFIGURATION MATRIX FAILED: ");
    }

    static void TestKeyboardFailOpenMatrix(VerificationReport report, ref int cases)
    {
        const int nextHookResult = 73;
        using var engine = new InputEngine
        {
            Enabled = true,
            TreatF13AsCapsLock = false,
            SpaceHoldRepeatEnabled = false,
            NextHookForTest = (_, _, _) => new IntPtr(nextHookResult),
            HasMapping = _ => false,
            InputReceived = _ => false,
            HasLongPress = _ => false,
            IsGesturePress = _ => false,
            IsGestureLongPress = _ => false,
            IsNativeMouseDrag = _ => false,
            HasLegacyMouseDrag = _ => false
        };

        // Reproduce the reported failure state: the transaction flag says a
        // fullscreen overlay exists, but there is no visible native surface.
        // Delete and every Ctrl+V transition must reach Windows immediately.
        OverlayService.ArmStaleFullScreenTransactionForTest();
        bool staleStatePassedThrough = engine.DirectKeyForTest(0x2E, false) == new IntPtr(nextHookResult)
            && engine.DirectKeyForTest(0x2E, true) == new IntPtr(nextHookResult)
            && engine.DirectKeyForTest(0x11, false) == new IntPtr(nextHookResult)
            && engine.DirectKeyForTest(0x56, false) == new IntPtr(nextHookResult)
            && engine.DirectKeyForTest(0x56, true) == new IntPtr(nextHookResult)
            && engine.DirectKeyForTest(0x11, true) == new IntPtr(nextHookResult)
            && !OverlayService.FullScreenVisible;
        cases += 7;

        // This is deliberately a complete virtual-key matrix rather than a
        // Delete special case. With no mapping or layer configured, every
        // keyboard Down/Up pair must be passed to the next Windows hook.
        bool allUnassignedKeysPassThrough = true;
        for (int virtualKey = 0x08; virtualKey <= 0xFE; virtualKey++)
        {
            allUnassignedKeysPassThrough &= engine.DirectKeyForTest((ushort)virtualKey, false) == new IntPtr(nextHookResult);
            allUnassignedKeysPassThrough &= engine.DirectKeyForTest((ushort)virtualKey, true) == new IntPtr(nextHookResult);
            cases += 2;
        }

        report.Check(staleStatePassedThrough && allUnassignedKeysPassThrough && !engine.HasCapturedStateForTest(),
            "a stale fullscreen transaction fails open and every unassigned virtual key preserves its Windows Down/Up path");
    }

    static void TestCatalogAndExecution(VerificationReport report, ref int cases)
    {
        var fake = new FakeOutput();
        var executor = new MappingExecutor(fake);
        bool allExecuted = true;
        foreach (var action in ActionCatalog.Items)
        {
            fake.Calls.Clear();
            var map = new Mapping { Input = "F6", Layer = "通常", Kind = action.Kind, Value = action.Value };
            bool handled = executor.Execute(map, "F6", out string executed);
            allExecuted &= handled && !executed.StartsWith("エラー:", StringComparison.Ordinal);
            cases++;
        }
        report.Check(allExecuted, $"all {ActionCatalog.Items.Count} catalog actions dispatch through a captured output backend");

        bool lifecyclePassed = true;
        foreach (string drag in new[] { "ShiftDrag", "CtrlDrag", "AltDrag" })
        {
            fake.Calls.Clear();
            var map = new Mapping { Input = "Space+MouseLeft", Layer = "Space", Kind = ActionKind.Mouse, Value = drag };
            foreach (string suffix in new[] { ":PressStart", ":PressEnd", ":DragStart", ":DragEnd" })
            {
                lifecyclePassed &= executor.Execute(map, map.Input + suffix, out _);
                cases++;
            }
            lifecyclePassed &= fake.Calls.SequenceEqual([
                $"mouse:{drag}:Start", $"mouse:{drag}:End", $"mouse:{drag}:Start", $"mouse:{drag}:End"]);
        }
        report.Check(lifecyclePassed, "Shift/Ctrl/Alt modified click and drag preserve paired Start/End dispatch");

        bool longPassed = true;
        foreach (var (kind, value, prefix) in ExecutableActions())
        {
            fake.Calls.Clear();
            var map = new Mapping { Input = "Space+F6", Layer = "Space", Kind = ActionKind.Shortcut, Value = "Ctrl+C", LongPressKind = kind, LongPressValue = value };
            bool handled = executor.Execute(map, map.Input + ":Long", out _);
            longPassed &= kind == ActionKind.Disabled
                ? handled && fake.Calls.Count == 0
                : handled && fake.Calls.Count > 0 && fake.Calls[0] == "neutralize:" + map.Input
                    && fake.Calls[^1].StartsWith(prefix, StringComparison.Ordinal);
            cases++;
        }
        report.Check(longPassed, "every executable long-press action neutralizes its source and dispatches to the correct backend");
    }

    static void TestValidConfigurationCrossProduct(VerificationReport report, ref int cases)
    {
        string[] inputs = ["F6", "Space+J", "CapsLock+U", "MouseRight+MouseMiddle", "MouseBack+J", "MouseForward+MouseLeft", "Taskbar+MouseMiddle"];
        string[] applications = ["", "notepad.exe", "NOTEPAD"];
        int[] durations = [50, 500, 10000];
        bool valid = true;
        var actions = ExecutableActions().Prepend((ActionKind.None, "", "")).ToArray();
        foreach (string input in inputs)
        foreach (string application in applications)
        foreach (int duration in durations)
        foreach (var (kind, value, _) in actions)
        foreach (var (longKind, longValue, _) in actions)
        {
            var config = CompleteConfig();
            config.Profiles[0].Mappings.Add(new Mapping
            {
                Input = input,
                Layer = LayerFor(input),
                Kind = kind,
                Value = value,
                LongPressKind = longKind,
                LongPressValue = longValue,
                LongPressMs = duration
            });
            valid &= ConfigValidator.Validate(config).Count == 0;
            cases++;
        }
        report.Check(valid, "layer, application condition, duration boundary, and action-kind cross product validates");

        bool gestureValid = true;
        foreach (string input in inputs)
        foreach (string application in applications)
        foreach (int duration in durations)
        {
            var config = CompleteConfig();
            config.Profiles[0].Mappings.Add(new Mapping
            {
                Input = input,
                Layer = LayerFor(input),
                Application = application,
                Kind = ActionKind.Gesture,
                Value = "Gesture",
                LongPressMs = duration
            });
            gestureValid &= ConfigValidator.Validate(config).Count == 0;
            cases++;
        }
        report.Check(gestureValid, "direct gestures validate independently on every layer and application condition");
    }

    static void TestInputAssignmentPolicy(VerificationReport report, ref int cases)
    {
        bool rejectedIntrinsicConflicts = true;
        foreach (Mapping invalid in new[]
        {
            new Mapping { Input = "WheelUp", Layer = "通常", Kind = ActionKind.Shortcut, Value = "Ctrl+C", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+K" },
            new Mapping { Input = "Space+TiltLeft", Layer = "Space", Kind = ActionKind.Gesture, Value = "Gesture" },
            new Mapping { Input = "MouseX", Layer = "通常", Kind = ActionKind.Shortcut, Value = "Ctrl+C" },
            new Mapping { Input = "CapsLock", Layer = "通常", Kind = ActionKind.Shortcut, Value = "Ctrl+C" },
            new Mapping { Input = "MouseRight+MouseRight", Layer = "MouseRight", Kind = ActionKind.Shortcut, Value = "Ctrl+C" },
            new Mapping { Input = "Taskbar+MouseLeft", Layer = "Taskbar", Kind = ActionKind.Shortcut, Value = "Ctrl+C" },
            new Mapping { Input = "Taskbar+MouseRight", Layer = "Taskbar", Kind = ActionKind.Shortcut, Value = "Ctrl+C" },
            new Mapping { Input = "Q", Layer = "通常", Kind = ActionKind.Shortcut, Value = "Ctrl+C", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+K" },
            new Mapping { Input = "Space+MouseRight", Layer = "Space", Kind = ActionKind.Mouse, Value = "CtrlDrag", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+K" }
        })
        {
            var config = CompleteConfig();
            config.Profiles[0].Mappings.Add(invalid);
            rejectedIntrinsicConflicts &= ConfigValidator.Validate(config).Count > 0;
            cases++;
        }

        var layerConflict = CompleteConfig();
        layerConflict.Profiles[0].Mappings =
        [
            new() { Input = "MouseRight", Layer = "通常", Kind = ActionKind.Mouse, Value = "MouseRight", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+K" },
            new() { Input = "MouseRight+K", Layer = "MouseRight", Kind = ActionKind.Shortcut, Value = "Ctrl+C" }
        ];
        bool rejectedLayerConflict = ConfigValidator.Validate(layerConflict).Any(error => error.Contains("MouseRight", StringComparison.Ordinal));
        cases++;
        report.Check(rejectedIntrinsicConflicts && rejectedLayerConflict,
            "validation rejects every intrinsically unreachable long press, impulse gesture, fake input, self-layer input, and active mouse-layer conflict");

        bool protectedTaskbarHoldValid = true;
        foreach (string input in new[] { "Taskbar+MouseLeft", "Taskbar+MouseRight" })
        {
            var config = CompleteConfig();
            config.Profiles[0].Mappings.Add(new Mapping
            {
                Input = input,
                Layer = "Taskbar",
                Kind = ActionKind.None,
                LongPressKind = ActionKind.Shortcut,
                LongPressValue = "Ctrl+Shift+Escape"
            });
            protectedTaskbarHoldValid &= ConfigValidator.Validate(config).Count == 0;
            cases++;
        }
        report.Check(protectedTaskbarHoldValid,
            "taskbar left and right buttons accept HOLD-only assignments while their Windows TAP remains reserved");
    }

    static void TestNormalizationAndRejection(VerificationReport report, string directory, ref int cases)
    {
        var service = new ConfigService(directory);
        var config = CompleteConfig();
        config.ActiveProfile = null!;
        config.KeyboardLayout = "broken";
        config.EmergencyShortcut = null!;
        config.SpaceHoldRepeatDelayMs = int.MinValue;
        config.DoubleClickMs = int.MaxValue;
        config.MouseDragPixels = -1;
        config.GestureThresholdPixels = int.MaxValue;
        config.InputPanelOpacityPercent = -50;
        config.WindowActionTarget = (WindowActionTarget)99;
        config.ThemeMode = (AppThemeMode)99;
        config.ClockBackgroundMode = (ClockBackgroundMode)99;
        config.ClockDisplayMode = (ClockDisplayMode)99;
        config.ActionPaletteFavorites = ["", " Shortcut:Ctrl+C ", "shortcut:ctrl+c", "Text:こんにちは"];
        config.ActionPaletteRecentActions = ["", " Shortcut:Ctrl+C ", "shortcut:ctrl+c", .. Enumerable.Range(0, 20).Select(index => $"Key:F{index + 1}")];
        config.Profiles[0].AutoSwitchApplications = ["", " ", " Notepad.exe ", "notepad.exe"];
        config.Profiles[0].Mappings =
        [
            new() { Input = "MouseLeft", Layer = "通常", Kind = ActionKind.Disabled },
            new() { Input = "Space+J", Layer = "CapsLock", Kind = (ActionKind)99, Value = "bad", LongPressKind = (ActionKind)98, LongPressValue = "bad" }
        ];
        config.DeckLayouts[0].Rows = 99;
        config.DeckLayouts[0].Columns = -1;
        config.DeckLayouts[0].PanelPadding = 999;
        config.DeckLayouts[0].PanelCornerRadius = -20;
        config.DeckLayouts[0].Mappings =
        [
            new() { Input = "Deck+01", Layer = "wrong", Kind = ActionKind.Gesture, Value = "Gesture", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+C" }
        ];
        config.Macros = [new() { Id = "", Name = "M", Steps = [new() { Event = null!, RecordedActionKind = (ActionKind)99, RecordedActionValue = null! }] }];
        service.Save(config);
        var repaired = service.Load();
        bool repairedSafely = repaired.Version == ConfigService.CurrentVersion
            && repaired.ActiveProfile == repaired.Profiles[0].Name
            && repaired.KeyboardLayout == "JIS"
            && repaired.SpaceHoldRepeatDelayMs == 100
            && repaired.DoubleClickMs == 2000
            && repaired.MouseDragPixels == 1
            && repaired.GestureThresholdPixels == 100
            && repaired.InputPanelOpacityPercent == 40
            && repaired.WindowActionTarget == WindowActionTarget.ActiveWindow
            && repaired.ThemeMode == AppThemeMode.System
            && repaired.ClockBackgroundMode == ClockBackgroundMode.FrostedScreen
            && repaired.ClockDisplayMode == ClockDisplayMode.DateAndTime
            && repaired.ActionPaletteFavorites.SequenceEqual(["Shortcut:Ctrl+C", "Text:こんにちは"])
            && repaired.ActionPaletteRecentActions.Count == 16
            && repaired.ActionPaletteRecentActions[0] == "Shortcut:Ctrl+C"
            && repaired.ActionPaletteRecentActions[1] == "Key:F1"
            && repaired.ActionPaletteRecentActions[^1] == "Key:F15"
            && repaired.Profiles[0].AutoSwitchApplications.SequenceEqual(["Notepad.exe"])
            && repaired.Profiles[0].Mappings is [{ Input: "Space+J", Layer: "Space", Kind: ActionKind.None, LongPressKind: ActionKind.None }]
            && repaired.DeckLayouts[0].Rows == 18 && repaired.DeckLayouts[0].Columns == 1
            && repaired.DeckLayouts[0].PanelPadding == 24 && repaired.DeckLayouts[0].PanelCornerRadius == 0
            && repaired.DeckLayouts[0].Mappings is [{ Layer: "Deck", Kind: ActionKind.None, LongPressKind: ActionKind.None }]
            && !string.IsNullOrWhiteSpace(repaired.Macros[0].Id)
            && repaired.Macros[0].Steps.Count == 0
            && ConfigValidator.Validate(repaired).Count == 0;
        cases++;
        report.Check(repairedSafely, "malformed imported settings are bounded without disabling the engine or reserving normal left click");

        var legacyKinds = CompleteConfig();
        legacyKinds.Version = 27;
        legacyKinds.Profiles[0].Mappings =
        [
            new() { Input = "Taskbar+Up", Layer = "Taskbar", Kind = ActionKind.Shortcut, Value = @"C:\Program Files\Orchis\orchis.exe" }
        ];
        legacyKinds.DeckLayouts[0].Mappings =
        [
            new() { Input = "Deck+03", Layer = "Deck", Kind = ActionKind.Key, Value = "さ" }
        ];
        legacyKinds.Gestures =
        [
            new() { Name = "Legacy text", UpKind = ActionKind.Shortcut, UpValue = "こんにちは" }
        ];
        legacyKinds.Macros =
        [
            new() { Id = "legacy", Name = "Legacy", Steps = [new() { RecordedActionKind = ActionKind.Key, RecordedActionValue = @"C:\Tools\sample.exe" }] }
        ];
        service.Save(legacyKinds);
        var migratedKinds = service.Load();
        bool migratedLegacyKinds = migratedKinds.Version == ConfigService.CurrentVersion
            && migratedKinds.Profiles[0].Mappings is [{ Kind: ActionKind.Launch, Value: @"C:\Program Files\Orchis\orchis.exe" }]
            && migratedKinds.DeckLayouts[0].Mappings is [{ Kind: ActionKind.Text, Value: "さ" }]
            && migratedKinds.Gestures is [{ UpKind: ActionKind.Text, UpValue: "こんにちは" }]
            && migratedKinds.Macros is [{ Steps: [{ RecordedActionKind: ActionKind.Launch, RecordedActionValue: @"C:\Tools\sample.exe" }] }]
            && ConfigValidator.Validate(migratedKinds).Count == 0;
        cases++;
        report.Check(migratedLegacyKinds, "v27 text and application paths misclassified as keys are repaired before strict validation");

        var impossibleAssignments = CompleteConfig();
        impossibleAssignments.Profiles[0].Mappings =
        [
            new() { Input = "WheelUp", Layer = "通常", Kind = ActionKind.Shortcut, Value = "Ctrl+C", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+K" },
            new() { Input = "Space+TiltRight", Layer = "Space", Kind = ActionKind.Gesture, Value = "Gesture", LongPressKind = ActionKind.Profile, LongPressValue = "Default" },
            new() { Input = "MouseX", Layer = "通常", Kind = ActionKind.Shortcut, Value = "Ctrl+C" },
            new() { Input = "CapsLock", Layer = "通常", Kind = ActionKind.Shortcut, Value = "Ctrl+C" },
            new() { Input = "Space+Space", Layer = "Space", Kind = ActionKind.Shortcut, Value = "Ctrl+C" },
            new() { Input = "Taskbar+MouseLeft", Layer = "Taskbar", Kind = ActionKind.Shortcut, Value = "Ctrl+L" },
            new() { Input = "Taskbar+MouseRight", Layer = "Taskbar", Kind = ActionKind.Shortcut, Value = "Ctrl+R", LongPressKind = ActionKind.Key, LongPressValue = "F10" },
            new() { Input = "MouseRight", Layer = "通常", Kind = ActionKind.Mouse, Value = "MouseRight", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+K" },
            new() { Input = "MouseRight+K", Layer = "MouseRight", Kind = ActionKind.Shortcut, Value = "Ctrl+C" },
            new() { Input = "O", Layer = "通常", Kind = ActionKind.Key, Value = "O", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+K" },
            new() { Input = "F7", Layer = "通常", Kind = ActionKind.Mouse, Value = "MouseX" }
        ];
        impossibleAssignments.Gestures =
        [
            new() { Name = "Legacy X1", UpKind = ActionKind.Mouse, UpValue = "MouseX" }
        ];
        impossibleAssignments.Macros =
        [
            new() { Id = "legacy-x1", Name = "Legacy X1", Steps = [new() { Event = "MouseX Down" }, new() { RecordedActionKind = ActionKind.Mouse, RecordedActionValue = "MouseX" }] }
        ];
        service.Save(impossibleAssignments);
        var repairedAssignments = service.Load();
        var repairedMappings = repairedAssignments.Profiles[0].Mappings;
        bool repairedImpossibleAssignments = !repairedMappings.Any(mapping => InputAssignmentPolicy.IsUnreachableInput(mapping.Input))
            && repairedMappings.Single(mapping => mapping.Input == "WheelUp") is { Kind: ActionKind.Shortcut, Value: "Ctrl+C", LongPressKind: ActionKind.None, LongPressValue: "" }
            && repairedMappings.Single(mapping => mapping.Input == "Space+TiltRight") is { Kind: ActionKind.None, Value: "", LongPressKind: ActionKind.None, LongPressValue: "" }
            && repairedMappings.Single(mapping => mapping.Input == "MouseRight") is { LongPressKind: ActionKind.None, LongPressValue: "" }
            && repairedMappings.Single(mapping => mapping.Input == "O") is { LongPressKind: ActionKind.None, LongPressValue: "" }
            && repairedMappings.Single(mapping => mapping.Input == "F7") is { Kind: ActionKind.Mouse, Value: "MouseForward" }
            && !repairedMappings.Any(mapping => mapping.Input == "Taskbar+MouseLeft")
            && repairedMappings.Single(mapping => mapping.Input == "Taskbar+MouseRight") is { Kind: ActionKind.None, Value: "", LongPressKind: ActionKind.Key, LongPressValue: "F10" }
            && repairedAssignments.Gestures.Single().UpKind == ActionKind.Mouse && repairedAssignments.Gestures.Single().UpValue == "MouseForward"
            && repairedAssignments.Macros.Single().Steps[0].Event == "MouseForward Down"
            && repairedAssignments.Macros.Single().Steps[1] is { RecordedActionKind: ActionKind.Mouse, RecordedActionValue: "MouseForward" }
            && ConfigValidator.Validate(repairedAssignments).Count == 0;
        cases++;
        report.Check(repairedImpossibleAssignments,
            "loading and saving automatically removes unreachable sources and long actions while migrating legacy MouseX output to MouseForward");

        bool rejected = true;
        foreach (Mapping invalid in new[]
        {
            new Mapping { Input = "MouseLeft", Layer = "通常", Kind = ActionKind.Disabled },
            new Mapping { Input = "Taskbar+MouseLeft", Layer = "Taskbar", Kind = ActionKind.Shortcut, Value = "Ctrl+L" },
            new Mapping { Input = "Taskbar+MouseRight", Layer = "Taskbar", Kind = ActionKind.Shortcut, Value = "Ctrl+R" },
            new Mapping { Input = "F6", Layer = "通常", Kind = ActionKind.Shortcut, Value = "DefinitelyUnknownKey" },
            new Mapping { Input = "F7", Layer = "通常", Kind = ActionKind.Mouse, Value = "UnknownMouse" },
            new Mapping { Input = "F8", Layer = "通常", Kind = (ActionKind)99, Value = "bad" },
            new Mapping { Input = "F9", Layer = "通常", Kind = ActionKind.Shortcut, Value = "Ctrl+MouseGarbage" },
            new Mapping { Input = "F10", Layer = "通常", Kind = ActionKind.Key, Value = "あ" },
            new Mapping { Input = "Taskbar+MouseX", Layer = "Taskbar", Kind = ActionKind.Shortcut, Value = "Ctrl+C" }
        })
        {
            var invalidConfig = CompleteConfig();
            invalidConfig.Profiles[0].Mappings.Add(invalid);
            rejected &= ConfigValidator.Validate(invalidConfig).Count > 0;
            cases++;
        }
        report.Check(rejected, "reserved or fake input, unknown shortcut/mouse action, and unknown action kind are rejected");
    }

    static void TestDeckDimensions(VerificationReport report, ref int cases)
    {
        bool passed = Math.Abs((DeckPanelLayout.CellWidth - DeckPanelLayout.KeyWidth)
            - (DeckPanelLayout.CellHeight - DeckPanelLayout.KeyHeight)) < .001;
        foreach (int rows in Enumerable.Range(1, DeckPanelLayout.MaximumRows))
        foreach (int columns in Enumerable.Range(1, DeckPanelLayout.MaximumColumns))
        {
            var layout = new DeckLayoutDefinition { Rows = rows, Columns = columns };
            int visible = rows * columns;
            layout.Mappings.Add(new Mapping { Input = DeckPanelLayout.InputName(1), Layer = DeckPanelLayout.Layer, Kind = ActionKind.Shortcut, Value = "Ctrl+C" });
            layout.Mappings.Add(new Mapping { Input = DeckPanelLayout.InputName(1), Layer = DeckPanelLayout.Layer, Kind = ActionKind.Text, Value = "variant", Application = "editor.exe" });
            layout.Mappings.Add(new Mapping { Input = DeckPanelLayout.InputName(visible), Layer = DeckPanelLayout.Layer, Kind = ActionKind.Shortcut, Value = "Ctrl+V", Description = new string('名', 80) });
            passed &= DeckPanelLayout.VisibleSlotCount(layout) == visible
                && DeckPanelLayout.SlotNumber(DeckPanelLayout.InputName(visible)) == visible
                && DeckPanelLayout.FindMapping(layout, visible)?.Description.Length == 80;
            DeckPanelLayout.SwapSlots(layout, 1, visible);
            passed &= visible == 1 || DeckPanelLayout.FindMapping(layout, 1)?.Value == "Ctrl+V"
                && layout.Mappings.Count(mapping => mapping.Input == DeckPanelLayout.InputName(visible)) == 2
                && layout.Mappings.Any(mapping => mapping.Input == DeckPanelLayout.InputName(visible) && mapping.Value == "variant" && mapping.Application == "editor.exe");
            cases++;
        }
        report.Check(passed, "all 324 Deck row/column combinations retain square spacing, slot identity, actions, and long names");
    }

    static void TestProfileRouting(VerificationReport report, ref int cases)
    {
        bool passed = true;
        var profiles = new[]
        {
            new Profile { Name = "Default" },
            new Profile { Name = "Manual" },
            new Profile { Name = "Browser", AutoSwitchEnabled = true, AutoSwitchApplications = ["chrome.exe"] },
            new Profile { Name = "Editor", AutoSwitchEnabled = true, AutoSwitchApplications = ["Code.exe"] }
        };
        foreach (var (processes, expected) in new[]
        {
            (new[] { "CHROME", "renderer" }, "Browser"),
            (new[] { "helper", "code.exe" }, "Editor"),
            (new[] { "explorer" }, "Default")
        })
        {
            passed &= MainWindow.ResolveAutomaticProfileTarget(profiles, "Default", "", processes, false).Target == expected;
            cases++;
        }
        passed &= MainWindow.ResolveAutomaticProfileTarget(profiles, "Manual", "", ["chrome"], true).Target == "Manual";
        cases++;

        var universal = new Mapping { Input = "F8", Kind = ActionKind.Key, Value = "A" };
        var chromeCondition = new Mapping { Input = "F8", Kind = ActionKind.Key, Value = "B", Application = "chrome.exe" };
        foreach (var (list, expected) in new[]
        {
            (new[] { universal, chromeCondition }, chromeCondition),
            (new[] { chromeCondition, universal }, universal),
            (new[] { chromeCondition }, chromeCondition)
        })
        {
            var profile = new Profile { Name = "Default", Mappings = [.. list] };
            passed &= ReferenceEquals(MainWindow.FindProfileMapping([profile], "Default", "F8", MainWindow.MappingInterceptsInput), expected);
            cases++;
        }
        var conditionalVariants = CompleteConfig();
        conditionalVariants.Profiles[0].Mappings = [universal.Copy(), chromeCondition.Copy()];
        passed &= ConfigValidator.Validate(conditionalVariants).Count == 0;
        cases++;
        conditionalVariants.Profiles[0].Mappings.Add(new Mapping { Input = "F8", Kind = ActionKind.Key, Value = "C", Application = "CHROME.EXE" });
        passed &= ConfigValidator.Validate(conditionalVariants).Any(error => error.Contains("競合", StringComparison.Ordinal));
        cases++;
        report.Check(passed, "profile auto-switch and per-application mapping variants retain deterministic last-visible precedence");
    }

    static void TestMappingApplicationRecovery(VerificationReport report, ref int cases)
    {
        var currentRestored = new Mapping { Input = "F8", Kind = ActionKind.Shortcut, Value = "Ctrl+Shift+X" };
        var currentConditioned = new Mapping { Input = "F9", Kind = ActionKind.Key, Value = "C", Application = "current.exe" };
        var currentAfterAnchor = new Mapping { Input = "F10", Kind = ActionKind.Key, Value = "D" };
        var currentRenamedProfileMapping = new Mapping { Input = "F11", Kind = ActionKind.Key, Value = "E" };
        var current = new AppConfig
        {
            Profiles =
            [
                new Profile { Name = "Default", Mappings = [currentRestored, currentConditioned, currentAfterAnchor] },
                new Profile { Name = "Renamed", Mappings = [currentRenamedProfileMapping] }
            ]
        };
        var preV29 = new AppConfig
        {
            Version = 28,
            Profiles =
            [
                new Profile
                {
                    Name = "Default",
                    Mappings =
                    [
                        new Mapping { Input = "F8", Kind = ActionKind.Key, Value = "old-first", Application = "first.exe" },
                        new Mapping { Input = "F8", Kind = ActionKind.Key, Value = "old-last", Application = " last.exe " },
                        new Mapping { Input = "F9", Kind = ActionKind.Key, Value = "old-existing", Application = "backup.exe" },
                        new Mapping { Input = "F10", Kind = ActionKind.Key, Value = "old-removed", Application = "removed.exe" }
                    ]
                },
                new Profile { Name = "Before Rename", Mappings = [new Mapping { Input = "F11", Application = "renamed.exe" }] }
            ]
        };
        var firstV29 = new AppConfig
        {
            Version = 29,
            Profiles =
            [
                new Profile { Name = "Default", Mappings = [new Mapping { Input = "F8" }, new Mapping { Input = "F9" }] },
                new Profile { Name = "Before Rename", Mappings = [new Mapping { Input = "F11" }] }
            ]
        };

        int restored = ConfigService.RestoreMappingApplications(current, preV29, firstV29);
        bool passed = restored == 1
            && currentRestored.Application == "last.exe"
            && currentRestored.Kind == ActionKind.Shortcut && currentRestored.Value == "Ctrl+Shift+X"
            && currentConditioned.Application == "current.exe"
            && string.IsNullOrEmpty(currentAfterAnchor.Application)
            && string.IsNullOrEmpty(currentRenamedProfileMapping.Application);
        cases += 5;
        report.Check(passed, "v29 recovery restores only the pre-v29 last application match without changing current actions, explicit conditions, post-anchor mappings, or renamed profiles");
    }

    static void TestMacroGraphs(VerificationReport report, ref int cases)
    {
        var config = CompleteConfig();
        config.Macros = Enumerable.Range(1, 5000).Select(index => new MacroDefinition { Id = "m" + index, Name = "M" + index }).ToList();
        for (int index = 0; index < config.Macros.Count - 1; index++)
            config.Macros[index].Steps.Add(new MacroStep { Event = "Mapped", RecordedActionKind = ActionKind.Macro, RecordedActionValue = config.Macros[index + 1].Name });
        bool chainValid = ConfigValidator.Validate(config).Count == 0;
        cases++;
        config.Macros[^1].Steps.Add(new MacroStep { Event = "Mapped", RecordedActionKind = ActionKind.Macro, RecordedActionValue = config.Macros[0].Name });
        bool cycleRejected = ConfigValidator.Validate(config).Any(error => error.Contains("循環", StringComparison.Ordinal));
        cases++;
        report.Check(chainValid && cycleRejected, "5000-level macro chain validates without recursion and a closing cyclic edge is rejected");
    }

    static void TestLayerStateTransitions(VerificationReport report, ref int cases)
    {
        bool passed = true;
        foreach (string layer in new[] { "MouseRight", "MouseBack", "MouseForward" })
        foreach (string secondary in new[] { "J", "MouseLeft", "MouseMiddle", "WheelUp" })
        for (int iteration = 0; iteration < 3; iteration++)
        {
            if (secondary.Equals(layer, StringComparison.OrdinalIgnoreCase))
                continue;
            string expected = layer + "+" + secondary;
            var received = new List<string>();
            using var engine = LayerEngine(expected, received);
            LayerTransition(engine, layer, false);
            SecondaryTransition(engine, secondary);
            LayerTransition(engine, layer, true);
            passed &= received.Count(item => item.Equals(expected, StringComparison.OrdinalIgnoreCase)) == 1
                && !engine.HasCapturedStateForTest();
            cases++;
        }
        report.Check(passed, "Space/CapsLock/right/back/forward layers survive repeated keyboard, mouse, and wheel chords without captured state");
    }

    static void TestModifierClickOutputOrder(VerificationReport report, ref int cases)
    {
        var output = new List<string>();
        InputEngine.KeyOutputForTest = (key, up) => { output.Add($"K:{key}:{(up ? "U" : "D")}"); return true; };
        InputEngine.MouseFlagOutputForTest = (flag, _) => output.Add("M:" + flag);
        try
        {
            bool passed = true;
            foreach (var (name, key) in new[] { ("ShiftDrag", 0x10), ("CtrlDrag", 0x11), ("AltDrag", 0x12) })
            {
                output.Clear();
                InputEngine.SendMouse(name);
                passed &= output.SequenceEqual([$"K:{key}:D", "M:2", "M:4", $"K:{key}:U"]);
                cases++;
                output.Clear();
                InputEngine.SendMouse(name + ":Start");
                InputEngine.SendMouse(name + ":End");
                passed &= output.SequenceEqual([$"K:{key}:D", "M:2", "M:4", $"K:{key}:U"]);
                cases++;
            }
            report.Check(passed, "modifier-click output order is modifier Down, left Down, left Up, modifier Up for click and drag");
        }
        finally
        {
            InputEngine.ReleaseAll();
            InputEngine.KeyOutputForTest = null;
            InputEngine.MouseFlagOutputForTest = null;
        }
    }

    static void TestUnassignedMouseLayerClicks(VerificationReport report, ref int cases)
    {
        const int nextHookResult = 73;
        const int expectedSourceReplays = 24;
        bool passed = true;
        int sourceReplays = 0;
        InputEngine.MouseClickBatchOutputForTest = _ => Interlocked.Increment(ref sourceReplays);
        try
        {
            foreach (string layer in new[] { "MouseRight", "MouseBack", "MouseForward" })
            {
                var received = new List<string>();
                using var engine = LayerEngine(layer + "+J", received);
                (int Down, int Up) otherButton = layer == "MouseRight" ? (0x207, 0x208) : (0x204, 0x205);
                foreach ((int down, int up) in new[] { (0x201, 0x202), otherButton })
                {
                    // The layer is deliberately released before the child
                    // button. An unmapped child used to be stored under a
                    // qualified name, while its later Up arrived unqualified.
                    for (int iteration = 0; iteration < 4; iteration++)
                    {
                        LayerTransition(engine, layer, false);
                        IntPtr childDown = engine.DirectMouseForTest(down);
                        LayerTransition(engine, layer, true);
                        IntPtr childUp = engine.DirectMouseForTest(up);
                        passed &= childDown == new IntPtr(nextHookResult)
                            && childUp == new IntPtr(nextHookResult)
                            && !engine.HasCapturedStateForTest();
                        cases += 3;
                    }
                }
                passed &= engine.DirectMouseForTest(0x201) == new IntPtr(nextHookResult)
                    && engine.DirectMouseForTest(0x202) == new IntPtr(nextHookResult);
                cases += 2;
            }
            passed &= SpinWait.SpinUntil(() => Volatile.Read(ref sourceReplays) == expectedSourceReplays, 2000);
            cases++;
            report.Check(passed, "unassigned left/right clicks pass through every mouse layer without leaving captured state, even when the layer is released first");
        }
        finally
        {
            // Every deferred source replay must finish while the output hook is
            // still installed. Otherwise a late Task.Run could leak a synthetic
            // click into the following test or the active desktop.
            SpinWait.SpinUntil(() => Volatile.Read(ref sourceReplays) >= expectedSourceReplays, 5000);
            InputEngine.MouseClickBatchOutputForTest = null;
        }
    }

    static void TestRawReleaseRecovery(VerificationReport report, ref int cases)
    {
        foreach (string layer in new[] { "MouseRight", "MouseBack", "MouseForward" })
        {
            bool passed = true;
            string expected = layer + "+J";
            var received = new List<string>();
            using var engine = LayerEngine(expected, received);
            engine.SetRawInputMonitorStartedForTest(true);
            LayerTransition(engine, layer, false);
            engine.DirectKeyForTest(0x4A, false);
            engine.DirectKeyForTest(0x4A, true);
            engine.ReconcileRawMouseButtonUpForTest(layer);
            passed &= !engine.HasCapturedStateForTest();

            // A fresh Down must clear any stale low-level-Up token left by the
            // Raw Input recovery so the next complete chord also finishes.
            engine.ObserveRawMouseButtonDownForTest(layer);
            LayerTransition(engine, layer, false);
            SecondaryTransition(engine, "J");
            LayerTransition(engine, layer, true);
            passed &= !engine.HasCapturedStateForTest();
            cases++;
            report.Check(passed, $"Raw Input first-Up recovery clears {layer} and never suppresses its next complete chord");
        }
    }

    static void TestRightWheelMovementDoesNotCreateContextClick(VerificationReport report, ref int cases)
    {
        var output = new List<string>();
        var clickBatches = new List<(uint Flag, uint Data)[]>();
        var received = new List<string>();
        InputEngine.MouseFlagOutputForTest = (flag, _) => { lock (output) output.Add(flag == 8 ? "Down" : flag == 16 ? "Up" : flag.ToString()); };
        InputEngine.MouseMoveOutputForTest = (dx, dy) => { lock (output) output.Add($"Move:{dx},{dy}"); };
        InputEngine.MouseClickBatchOutputForTest = batch => { lock (clickBatches) clickBatches.Add(batch); };
        try
        {
            using (var engine = new InputEngine
            {
                Enabled = true,
                DragPixels = 1,
                NextHookForTest = (_, _, _) => new IntPtr(73),
                HasMapping = input => input is "MouseRight+*" or "MouseRight+WheelUp" or "MouseRight+WheelDown",
                InputReceived = input => { received.Add(input); return true; },
                HasLongPress = _ => false,
                IsGesturePress = _ => false,
                IsGestureLongPress = _ => false,
                IsNativeMouseDrag = _ => false,
                HasLegacyMouseDrag = _ => false
            })
            {
                bool passed = engine.DirectMouseForTest(0x204, 0, 100, 100) == (IntPtr)1;
                passed &= engine.DirectMouseForTest(0x200, 0, 120, 105) == (IntPtr)1;
                passed &= SpinWait.SpinUntil(() => { lock (output) return output.Count >= 2; }, 500);
                passed &= engine.DirectMouseForTest(0x200, 0, 124, 107) == new IntPtr(73);
                passed &= engine.DirectMouseForTest(0x20A, 120 << 16) == (IntPtr)1;
                passed &= SpinWait.SpinUntil(() => { lock (output) return output.Contains("Up"); }, 500);
                passed &= engine.DirectMouseForTest(0x205, 0, 124, 107) == (IntPtr)1;
                string[] sequence;
                lock (output) sequence = [.. output];
                lock (clickBatches)
                    passed &= clickBatches.Count == 0;
                passed &= sequence.SequenceEqual(["Down", "Move:20,5", "Up"])
                    && received.Count(input => input == "MouseRight+WheelUp") == 1
                    && !engine.HasCapturedStateForTest();
                cases += 9;
                report.Check(passed, "a right-wheel mapping preserves native right-drag and emits Down, movement, Up before executing the mapped wheel action");
            }

            lock (output) output.Clear();
            using (var engine = new InputEngine
            {
                Enabled = true,
                DragPixels = 1,
                NextHookForTest = (_, _, _) => new IntPtr(73),
                HasMapping = input => input is "MouseRight+*" or "MouseRight+WheelUp",
                InputReceived = input => { received.Add(input); return true; },
                HasLongPress = _ => false,
                IsGesturePress = _ => false,
                IsGestureLongPress = _ => false,
                IsNativeMouseDrag = _ => false,
                HasLegacyMouseDrag = _ => false
            })
            {
                engine.DirectMouseForTest(0x204, 0, 200, 200);
                engine.DirectMouseForTest(0x200, 0, 220, 200);
                bool started = SpinWait.SpinUntil(() => { lock (output) return output.Count >= 2; }, 500);
                engine.DirectMouseForTest(0x205, 0, 220, 200);
                bool ended = SpinWait.SpinUntil(() => { lock (output) return output.Contains("Up"); }, 500);
                string[] sequence;
                lock (output) sequence = [.. output];
                cases += 3;
                report.Check(started && ended && sequence.SequenceEqual(["Down", "Move:20,0", "Up"]) && !engine.HasCapturedStateForTest(),
                    "native right-drag remains available even when the same right layer also has a wheel assignment");
            }
        }
        finally
        {
            InputEngine.ReleaseAll();
            InputEngine.MouseFlagOutputForTest = null;
            InputEngine.MouseMoveOutputForTest = null;
            InputEngine.MouseClickBatchOutputForTest = null;
        }
    }

    static void TestExtendedMouseButtonIdentity(VerificationReport report, ref int cases)
    {
        using var engine = new InputEngine { NextHookForTest = (_, _, _) => new IntPtr(73) };
        engine.SetRawInputMonitorStartedForTest(true);
        engine.DirectMouseForTest(0x20B, 1 << 16);
        bool backOnly = InputEngine.IsObservedPhysicalMouseButtonDownForTest(4)
            && !InputEngine.IsObservedPhysicalMouseButtonDownForTest(5);
        engine.DirectMouseForTest(0x20B, 2 << 16);
        bool both = InputEngine.IsObservedPhysicalMouseButtonDownForTest(4)
            && InputEngine.IsObservedPhysicalMouseButtonDownForTest(5);
        engine.DirectMouseForTest(0x20C, 1 << 16);
        engine.ReconcileRawMouseButtonUpForTest("MouseBack");
        bool forwardOnly = !InputEngine.IsObservedPhysicalMouseButtonDownForTest(4)
            && InputEngine.IsObservedPhysicalMouseButtonDownForTest(5);
        engine.DirectMouseForTest(0x20C, 2 << 16);
        engine.ReconcileRawMouseButtonUpForTest("MouseForward");
        bool neither = !InputEngine.IsObservedPhysicalMouseButtonDownForTest(4)
            && !InputEngine.IsObservedPhysicalMouseButtonDownForTest(5);
        cases += 4;
        report.Check(backOnly && both && forwardOnly && neither,
            "XBUTTON1 Back and XBUTTON2 Forward retain independent low-level and Raw Input identities");
    }

    static InputEngine LayerEngine(string mapping, List<string> received)
    {
        string prefix = mapping[..(mapping.IndexOf('+') + 1)];
        return new InputEngine
        {
            Enabled = true,
            TreatF13AsCapsLock = true,
            SpaceHoldRepeatEnabled = false,
            NextHookForTest = (_, _, _) => new IntPtr(73),
            HasMapping = input => input.Equals(mapping, StringComparison.OrdinalIgnoreCase)
                || input.Equals(prefix + "*", StringComparison.OrdinalIgnoreCase),
            InputReceived = input => { received.Add(input); return true; },
            HasLongPress = _ => false,
            IsGesturePress = _ => false,
            IsGestureLongPress = _ => false,
            IsNativeMouseDrag = _ => false,
            HasLegacyMouseDrag = _ => false
        };
    }

    static void LayerTransition(InputEngine engine, string layer, bool up)
    {
        switch (layer)
        {
            case "Space": engine.DirectKeyForTest(0x20, up); break;
            case "CapsLock": engine.DirectKeyForTest(0x7C, up); break;
            case "MouseRight": engine.DirectMouseForTest(up ? 0x205 : 0x204); break;
            case "MouseBack": engine.DirectMouseForTest(up ? 0x20C : 0x20B, 1 << 16); break;
            case "MouseForward": engine.DirectMouseForTest(up ? 0x20C : 0x20B, 2 << 16); break;
        }
    }

    static void SecondaryTransition(InputEngine engine, string secondary)
    {
        switch (secondary)
        {
            case "J": engine.DirectKeyForTest(0x4A, false); engine.DirectKeyForTest(0x4A, true); break;
            case "MouseLeft": engine.DirectMouseForTest(0x201); engine.DirectMouseForTest(0x202); break;
            case "MouseMiddle": engine.DirectMouseForTest(0x207); engine.DirectMouseForTest(0x208); break;
            case "WheelUp": engine.DirectMouseForTest(0x20A, 120 << 16); break;
        }
    }

    static AppConfig CompleteConfig()
    {
        var config = new AppConfig();
        config.Profiles[0].Name = "Default";
        config.ActiveProfile = "Default";
        config.Macros = [new MacroDefinition { Id = "macro", Name = "Macro" }];
        config.Gestures = [new GestureDefinition { Name = "Gesture", CenterKind = ActionKind.Shortcut, CenterValue = "Esc" }];
        return config;
    }

    static IEnumerable<(ActionKind Kind, string Value, string Prefix)> ExecutableActions()
    {
        yield return (ActionKind.Disabled, "", "");
        yield return (ActionKind.Key, "Enter", "shortcut:");
        yield return (ActionKind.Shortcut, "Ctrl+C", "shortcut:");
        yield return (ActionKind.Text, "日本語", "text:");
        yield return (ActionKind.Launch, "sample.exe", "launch:");
        yield return (ActionKind.Mouse, "MouseRight", "mouse:");
        yield return (ActionKind.Macro, "Macro", "macro:");
        yield return (ActionKind.Profile, "Default", "profile:");
    }

    static string LayerFor(string input)
    {
        int separator = input.IndexOf('+');
        return separator < 0 ? "通常" : input[..separator];
    }

    sealed class FakeOutput : IInputOutput
    {
        internal List<string> Calls { get; } = [];
        public void NeutralizeSourceKey(string input) => Calls.Add("neutralize:" + input);
        public void SendShortcut(string value) => Calls.Add("shortcut:" + value);
        public void SendText(string value) => Calls.Add("text:" + value);
        public void SendMouse(string value) => Calls.Add("mouse:" + value);
        public void Launch(string value) => Calls.Add("launch:" + value);
        public void RunMacro(string name) => Calls.Add("macro:" + name);
        public void SwitchProfile(string name) => Calls.Add("profile:" + name);
        public void ShowOverlay(string value) => Calls.Add("overlay:" + value);
    }
}
