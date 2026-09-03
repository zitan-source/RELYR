namespace RELYR;

using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;

public static class SelfTest
{
    public static int Run(TextWriter output)
    {
        var report = new VerificationReport(output);
        Action<bool, string> Check = report.Check;
        Check(new AppConfig() is { AutoSave: true, UiAnimationsEnabled: true, DetailedDiagnosticsEnabled: false, UiLanguage: LocalizationService.Japanese }, "new installations default to Japanese, auto-save and RELYR animations on, while detailed diagnostics stay opt-in");
        string dir = VerificationPaths.CreateRunDirectory("self-test");
        try
        {
            string ipcPipe = IpcTransport.NewName("self-test");
            string ipcSecret = "self-test-" + Guid.NewGuid().ToString("N");
            string ipcExecutable = Environment.ProcessPath ?? "RELYR.exe";
            bool selfTestElevation = StartupService.IsProcessElevated();
            var ipcServer = new ElevatedIpcServer(ipcPipe, ipcSecret, ipcExecutable, (message, _) => Task.FromResult(new IpcMessage(message.Command, message.RequestId, message.Command == IpcCommand.ReloadConfig ? "reloaded" : "ok", ipcSecret)), selfTestElevation);
            var ipcClient = new ElevatedIpcClient(ipcPipe, ipcSecret, ipcExecutable, selfTestElevation);
            bool ipcConnected = ipcClient.ConnectAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            var ipcResponse = ipcClient.SendAsync(IpcCommand.ReloadConfig).GetAwaiter().GetResult();
            bool ipcPassed = ipcConnected && ipcResponse?.Value == "reloaded";
            Check(ipcPassed, "IPC ACL, process identity, elevation check, handshake, and framed command roundtrip");
            try
            {
                File.WriteAllText(VerificationPaths.GetFile("ipc-security-test.log"), $"{DateTimeOffset.Now:O} PASS={ipcPassed} currentElevated={selfTestElevation} sameProcessIdentity=true{Environment.NewLine}");
            }
            catch { }
            ipcClient.DisposeAsync().GetAwaiter().GetResult();
            ipcServer.DisposeAsync().GetAwaiter().GetResult();
            string safeDiagnosticPath = Path.Combine(dir, "safe-diagnostics.log");
            string expiredDiagnosticPath = Path.Combine(dir, "expired-diagnostics.log");
            DiagnosticLogStorage.Configure(false);
            DiagnosticLogStorage.WriteStatus(safeDiagnosticPath, "ipc", "connection-ready");
            string safeDiagnosticText = File.ReadAllText(safeDiagnosticPath);
            DiagnosticLogStorage.WriteDetailed("privacy-test", "mapped-action", @"Value=private text; ForegroundWindowPath=C:\Users\Person\private.exe");
            bool detailedSuppressedByDefault = !File.Exists(DiagnosticLogStorage.DetailedLogPath);
            File.WriteAllText(expiredDiagnosticPath, "legacy private content");
            File.SetLastWriteTimeUtc(expiredDiagnosticPath, DateTime.UtcNow.Subtract(DiagnosticLogStorage.MaximumLogAge).AddMinutes(-1));
            DiagnosticLogStorage.AppendBounded(expiredDiagnosticPath, "fresh status only");
            bool expiredContentRemoved = File.ReadAllText(expiredDiagnosticPath).Trim() == "fresh status only";
            DiagnosticLogStorage.Configure(true);
            DiagnosticLogStorage.WriteDetailed("privacy-test", "mapped-action", "explicit-consent-detail");
            bool detailedWrittenAfterConsent = File.ReadAllText(DiagnosticLogStorage.DetailedLogPath).Contains("explicit-consent-detail", StringComparison.Ordinal);
            DiagnosticLogStorage.Configure(false);
            Check(safeDiagnosticText.Contains("event=connection-ready", StringComparison.Ordinal)
                && !safeDiagnosticText.Contains("detail=", StringComparison.Ordinal)
                && detailedSuppressedByDefault
                && expiredContentRemoved
                && detailedWrittenAfterConsent
                && !File.Exists(DiagnosticLogStorage.DetailedLogPath),
                "production-style status logs exclude details, detailed diagnostics require explicit consent, and old or disabled details are deleted");
            var service = new ConfigService(dir);
            var config = service.Load();
            string languageDirectory = Path.Combine(dir, "language-roundtrip");
            var languageService = new ConfigService(languageDirectory);
            languageService.Save(new AppConfig { UiLanguage = "english" });
            var englishConfig = languageService.Load();
            LocalizationService.Apply(englishConfig.UiLanguage);
            bool englishLocalized = englishConfig.UiLanguage == LocalizationService.English
                && LocalizationService.Text("設定") == "Settings"
                && LocalizationService.Text("GPU温度") == "GPU temperature";
            bool everyLanguageLoaded = true;
            foreach (var language in LocalizationService.SupportedLanguages)
            {
                LocalizationService.Apply(language.Code);
                everyLanguageLoaded &= LocalizationService.CurrentLanguage == language.Code
                    && !string.IsNullOrWhiteSpace(LocalizationService.Text("設定"))
                    && (language.Code == LocalizationService.Japanese || LocalizationService.Text("お気に入り") != "お気に入り")
                    && (language.Code is LocalizationService.Japanese or LocalizationService.English || LocalizationService.CurrentCatalogCountForTest >= 815);
            }
            LocalizationService.Apply(LocalizationService.Japanese);
            Check(englishLocalized && everyLanguageLoaded && LocalizationService.SupportedLanguages.Count == 8
                && LocalizationService.Normalize("zh-Hant-HK") == LocalizationService.ChineseTraditional
                && LocalizationService.Normalize("unsupported") == LocalizationService.Japanese,
                "all eight display languages load complete catalogs, normalize aliases, persist, translate shared UI, and safely fall back to Japanese");
            var inputPanelPositions = new AppConfig { NumpadPanelLeft = 123.5, NumpadPanelTop = 234.5, ExtendedKeypadPanelLeft = 345.5, ExtendedKeypadPanelTop = 456.5 };
            (bool Extended, double Left, double Top)? persistedInputPosition = null;
            var savedNumpad = new InputPanelOverlayWindow(false, config: inputPanelPositions, positionChanged: (extended, left, top) => persistedInputPosition = (extended, left, top));
            var savedExtended = new InputPanelOverlayWindow(true, config: inputPanelPositions);
            var expectedNumpadPosition = InputPanelOverlayWindow.InitialPosition(inputPanelPositions, false, savedNumpad.Width, savedNumpad.Height);
            var expectedExtendedPosition = InputPanelOverlayWindow.InitialPosition(inputPanelPositions, true, savedExtended.Width, savedExtended.Height);
            bool restoredSeparatePositions = Math.Abs(savedNumpad.Left - expectedNumpadPosition.X) < .1 && Math.Abs(savedNumpad.Top - expectedNumpadPosition.Y) < .1 && Math.Abs(savedExtended.Left - expectedExtendedPosition.X) < .1 && Math.Abs(savedExtended.Top - expectedExtendedPosition.Y) < .1;
            savedNumpad.MoveAndPersistForTest(180, 210);
            Check(restoredSeparatePositions && persistedInputPosition is { Extended: false, Left: 180, Top: 210 }, "numpad and extended keypad retain separate last overlay positions");
            string oldData = Path.Combine(dir, "old-appdata"), newData = Path.Combine(dir, "new-appdata");
            var oldService = new ConfigService(oldData);
            oldService.Save(new AppConfig { AutoSave = true, Profiles = [new Profile { Name = "移行テスト" }] });
            Check(ConfigService.MigrateLegacyDirectory(oldData, newData) && !Directory.Exists(oldData) && new ConfigService(newData).Load().Profiles[0].Name == "移行テスト", "legacy settings move to the RELYR AppData folder without data loss");
            Directory.CreateDirectory(oldData);
            Directory.CreateDirectory(newData);
            File.WriteAllText(Path.Combine(oldData, "old.json"), "{}");
            File.WriteAllText(Path.Combine(newData, "settings.json"), "{}");
            Check(ConfigService.DeleteUserDataDirectories(oldData, newData) && !Directory.Exists(oldData) && !Directory.Exists(newData), "complete uninstall removes both current and legacy AppData settings folders");
            Check(config.Profiles.Count == 1 && config.Profiles[0].Name == "標準" && config.Profiles[0].Mappings.Count == 0, "only standard profile exists by default");
            var dragAssignments = new List<Mapping>
            {
                new() { Input = "F8", Layer = "通常", Kind = ActionKind.Text, Value = "source-default", Application = "editor.exe" },
                new() { Input = "F8", Layer = "通常", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+Shift+A", Application = "browser.exe" },
                new() { Input = "F9", Layer = "通常", Kind = ActionKind.Shortcut, Value = "Ctrl+B", Description = "target-default" }
            };
            var swapResult = MainWindow.TransferAssignments(dragAssignments, "F8", "F9");
            bool profileDragSwapPreservedEveryVariant = swapResult == AssignmentTransferResult.Swapped
                && dragAssignments.Count(mapping => mapping.Input == "F9") == 2
                && dragAssignments.Count(mapping => mapping.Input == "F8") == 1
                && dragAssignments.Where(mapping => mapping.Input == "F9").Select(mapping => mapping.Application).Order().SequenceEqual(new[] { "browser.exe", "editor.exe" })
                && dragAssignments.Single(mapping => mapping.Input == "F8").Description == "target-default";
            string[] draggableLayers = ["Space", "CapsLock", "MouseRight", "MouseBack", "MouseForward", "Taskbar"];
            bool everyLayerMovesWithoutLoss = true;
            foreach (string layer in draggableLayers)
            {
                string source = layer + "+C", target = layer + "+D";
                dragAssignments.Add(new Mapping { Input = source, Layer = layer, Kind = ActionKind.Key, Value = "Enter", DragValue = "CtrlDrag", DragEndValue = "CtrlDragEnd" });
                everyLayerMovesWithoutLoss &= MainWindow.TransferAssignments(dragAssignments, source, target) == AssignmentTransferResult.Moved
                    && dragAssignments.Single(mapping => mapping.Input == target) is { Layer: var movedLayer, Value: "Enter", DragValue: "CtrlDrag", DragEndValue: "CtrlDragEnd" }
                    && movedLayer == layer
                    && !dragAssignments.Any(mapping => mapping.Input == source);
            }
            Check(profileDragSwapPreservedEveryVariant && everyLayerMovesWithoutLoss,
                "dragging assignments moves empty targets, swaps occupied targets, and preserves every app variant and action field across all editable layers");
            var invalidDragAssignments = new List<Mapping>
            {
                new() { Input = "F8", Layer = "通常", Kind = ActionKind.Gesture, Value = "Gesture" },
                new() { Input = "F9", Layer = "通常", Kind = ActionKind.Shortcut, Value = "Ctrl+C", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+K" }
            };
            Check(MainWindow.TransferAssignments(invalidDragAssignments, "F8", "WheelUp") == AssignmentTransferResult.None
                && MainWindow.TransferAssignments(invalidDragAssignments, "F9", "O") == AssignmentTransferResult.None
                && invalidDragAssignments[0].Input == "F8" && invalidDragAssignments[1].Input == "F9",
                "dragging cannot move a gesture onto an impulse input or a long action onto a normal alphabet key");
            byte[] legacyMap = [0, 0, 0, 0, 0, 0, 0, 0, 3, 0, 0, 0, 0x64, 0, 0x3A, 0, 0x0C, 0, 0x79, 0, 0, 0, 0, 0];
            Check(LegacyKeyRemapService.ContainsCapsLockToF13(legacyMap), "legacy CapsLock-to-F13 registry mapping detection");
            var remapRemoved = LegacyKeyRemapService.UpdateCapsLockToF13(legacyMap, false);
            var remapRestored = LegacyKeyRemapService.UpdateCapsLockToF13(remapRemoved, true);
            Check(!LegacyKeyRemapService.ContainsCapsLockToF13(remapRemoved) && BitConverter.ToUInt32(remapRemoved, 12) == 0x0079000C && LegacyKeyRemapService.ContainsCapsLockToF13(remapRestored), "CapsLock F13 setting preserves unrelated registry remaps and can be restored");
            string legacyDir = Path.Combine(dir, "legacy-v7");
            var legacyService = new ConfigService(legacyDir);
            legacyService.Save(new AppConfig { Version = 7, EngineEnabled = false });
            var repairedLegacy = legacyService.Load();
            using var repairedDocument = JsonDocument.Parse(File.ReadAllText(legacyService.FilePath));
            Check(repairedLegacy.Version == ConfigService.CurrentVersion && repairedLegacy.EngineEnabled && repairedDocument.RootElement.GetProperty("Version").GetInt32() == ConfigService.CurrentVersion && repairedDocument.RootElement.GetProperty("EngineEnabled").GetBoolean(), "v7 reboot-disabled engine is repaired and persistently migrated once");
            string legacyDisabledDir = Path.Combine(dir, "legacy-disabled-mapping");
            Directory.CreateDirectory(legacyDisabledDir);
            File.WriteAllText(Path.Combine(legacyDisabledDir, "settings.json"), "{\"Version\":17,\"Profiles\":[{\"Name\":\"標準\",\"Mappings\":[{\"Input\":\"Q\",\"Kind\":\"Key\",\"Value\":\"A\",\"Enabled\":false},{\"Input\":\"W\",\"Kind\":\"Key\",\"Value\":\"B\",\"Enabled\":true}]}]}");
            var migratedDisabledService = new ConfigService(legacyDisabledDir);
            var migratedDisabled = migratedDisabledService.Load();
            string migratedDisabledJson = File.ReadAllText(migratedDisabledService.FilePath);
            Check(migratedDisabled.Version == ConfigService.CurrentVersion && migratedDisabled.Profiles[0].Mappings is [{ Input: "W" }] && !migratedDisabledJson.Contains("\"Enabled\"", StringComparison.Ordinal), "legacy disabled assignments are deleted while enabled assignments remain active");
            string staleLongDir = Path.Combine(dir, "legacy-long-value");
            var staleLongService = new ConfigService(staleLongDir);
            staleLongService.Save(new AppConfig { Version = 8, Profiles = [new Profile { Mappings = [new Mapping { Input = "Space+K", Kind = ActionKind.Key, Value = "Enter", LongPressKind = ActionKind.None, LongPressValue = "q" }, new Mapping { Input = "Space+L", Kind = ActionKind.Key, Value = "Right", LongPressKind = ActionKind.Launch, LongPressValue = "app.exe" }] }] });
            var migratedLong = staleLongService.Load();
            Check(migratedLong.Version == ConfigService.CurrentVersion && migratedLong.Profiles[0].Mappings[0].LongPressValue == "" && migratedLong.Profiles[0].Mappings[1].LongPressValue == "app.exe", "stale disabled long-press values are removed without changing explicit long actions");
            string mouseFixDir = Path.Combine(dir, "legacy-mouse-action");
            var mouseFixService = new ConfigService(mouseFixDir);
            mouseFixService.Save(new AppConfig { Version = 15, Profiles = [new Profile { Mappings = [new Mapping { Input = "Space+MouseLeft", Kind = ActionKind.Key, Value = "ShiftDrag" }, new Mapping { Input = "Space+MouseRight", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+MouseLeft" }] }] });
            var fixedMouse = mouseFixService.Load();
            Check(fixedMouse.Version == ConfigService.CurrentVersion && fixedMouse.Profiles[0].Mappings[0] is { Kind: ActionKind.Mouse, Value: "ShiftDrag" } && fixedMouse.Profiles[0].Mappings[1] is { LongPressKind: ActionKind.Mouse, LongPressValue: "CtrlDrag" }, "misclassified modifier-click assignments are repaired during settings upgrade");
            string gestureMigrationDir = Path.Combine(dir, "legacy-long-gesture");
            Directory.CreateDirectory(gestureMigrationDir);
            File.WriteAllText(Path.Combine(gestureMigrationDir, "settings.json"), "{\"Version\":19,\"Gestures\":[{\"Name\":\"ウィンドウ操作\"}],\"Profiles\":[{\"Name\":\"標準\",\"Mappings\":[{\"Input\":\"MouseRight+Space\",\"Kind\":\"None\",\"Value\":\"\",\"LongPressKind\":\"Gesture\",\"LongPressValue\":\"ウィンドウ操作\"}]}]}");
            var migratedGesture = new ConfigService(gestureMigrationDir).Load();
            Check(migratedGesture.Version == ConfigService.CurrentVersion && migratedGesture.Profiles[0].Mappings[0] is { Kind: ActionKind.Gesture, Value: "ウィンドウ操作", LongPressKind: ActionKind.None, LongPressValue: "" }, "v19 long-press-only gestures migrate to immediate short-press gestures without losing the reference");
            string profileScopedMappingDir = Path.Combine(dir, "legacy-profile-scoped-mappings-v28");
            var profileScopedMappingService = new ConfigService(profileScopedMappingDir);
            profileScopedMappingService.Save(new AppConfig
            {
                Version = 28,
                ActiveProfile = "標準",
                Profiles =
                [
                    new Profile
                    {
                        Name = "標準",
                        Mappings =
                        [
                            new Mapping { Input = "F8", Kind = ActionKind.Key, Value = "A", Application = "" },
                            new Mapping { Input = "F8", Kind = ActionKind.Key, Value = "B", Application = "notepad.exe" },
                            new Mapping { Input = "Space+J", Kind = ActionKind.Key, Value = "Left", Application = "code.exe" }
                        ]
                    }
                ]
            });
            var migratedProfileScopedMappings = profileScopedMappingService.Load();
            var migratedProfileMappings = migratedProfileScopedMappings.Profiles[0].Mappings;
            Check(migratedProfileScopedMappings.Version == ConfigService.CurrentVersion
                && migratedProfileMappings.Count == 3
                && migratedProfileMappings.Single(mapping => mapping.Input.Equals("F8", StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(mapping.Application)) is { Value: "A" }
                && migratedProfileMappings.Single(mapping => mapping.Input.Equals("F8", StringComparison.OrdinalIgnoreCase) && mapping.Application.Equals("notepad.exe", StringComparison.OrdinalIgnoreCase)) is { Value: "B" }
                && migratedProfileMappings.Single(mapping => mapping.Input.Equals("Space+J", StringComparison.OrdinalIgnoreCase)) is { Value: "Left", Application: "code.exe" }
                && ConfigValidator.Validate(migratedProfileScopedMappings).Count == 0,
                "v28 per-mapping application conditions and same-input application variants are preserved");
            string protectedLeftClickDir = Path.Combine(dir, "protected-normal-left-click");
            var protectedLeftClickService = new ConfigService(protectedLeftClickDir);
            protectedLeftClickService.Save(new AppConfig
            {
                Version = ConfigService.CurrentVersion,
                Profiles =
                [
                    new Profile
                    {
                        Mappings =
                        [
                            new Mapping { Input = "MouseLeft", Kind = ActionKind.Disabled },
                            new Mapping { Input = "MouseLeft+J", Kind = ActionKind.Key, Value = "A" },
                            new Mapping { Input = "Space+MouseLeft", Kind = ActionKind.Key, Value = "B" },
                            new Mapping { Input = "Taskbar+MouseLeft", Kind = ActionKind.Key, Value = "C" },
                            new Mapping { Input = "Taskbar+MouseLeft", Kind = ActionKind.Shortcut, Value = OverlayService.DeckPanelAction, LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+Shift+Escape", Application = "notepad.exe" }
                        ]
                    }
                ]
            });
            var protectedLeftClickMappings = protectedLeftClickService.Load().Profiles[0].Mappings;
            Check(protectedLeftClickMappings.Select(mapping => mapping.Input).SequenceEqual(["Space+MouseLeft"]),
                "normal left click and every taskbar left-click assignment are removed while explicit Space+MouseLeft remains available");
            var pendingCaps = new AppConfig { CapsLockRemapPendingRestart = true, CapsLockRemapChangedAtUtcTicks = DateTime.UtcNow.AddMinutes(-1).Ticks };
            Check(LegacyKeyRemapService.IsRestartStillPending(pendingCaps, DateTime.UtcNow, (long)TimeSpan.FromHours(1).TotalMilliseconds) && !LegacyKeyRemapService.IsRestartStillPending(pendingCaps, DateTime.UtcNow, (long)TimeSpan.FromSeconds(10).TotalMilliseconds), "CapsLock remap distinguishes the current boot from a completed restart");
            Check(!App.UninstallRestartNeeded(new AppConfig(), false, false) && App.UninstallRestartNeeded(new AppConfig(), true, false) && !App.UninstallRestartNeeded(new AppConfig { CapsLockLayerEnabled = true }, false, false) && App.UninstallRestartNeeded(new AppConfig(), false, true), "normal updates and a saved CapsLock preference do not request a restart; only an active or pending system remap does");
            Check(config.Profiles.SelectMany(p => p.Mappings).All(m => !m.Input.StartsWith("F13", StringComparison.OrdinalIgnoreCase) && !m.Layer.Equals("F13", StringComparison.OrdinalIgnoreCase)), "F13 layer settings migrate to CapsLock");
            config.Macros.Add(new MacroDefinition { Name = "テストマクロ", Steps = [new() { Event = "A Down", DelayMs = 100 }, new() { Event = "A Up", DelayMs = 25 }, new() { Event = "Wait", DelayMs = 500 }, new() { Event = "割り当て: Win+Left", RecordedActionKind = ActionKind.Shortcut, RecordedActionValue = "Win+Left" }] });
            config.Gestures.Add(new GestureDefinition { Name = "ウィンドウ操作", GestureThresholdPixels = 18, LockCursorDuringGesture = false, UpKind = ActionKind.Shortcut, UpValue = "Win+Up", DownKind = ActionKind.Shortcut, DownValue = "Win+Down", CenterKind = ActionKind.Key, CenterValue = "Enter" });
            config.Profiles[0].Mappings.Add(new Mapping { Input = "G", Kind = ActionKind.Gesture, Value = "ウィンドウ操作" });
            Check(ConfigValidator.Validate(config).Count == 0, "default config validation");
            Check(MainWindow.GestureAction(config.Gestures[0], "Up") == (ActionKind.Shortcut, "Win+Up") && MainWindow.GestureAction(config.Gestures[0], "Center") == (ActionKind.Key, "Enter"), "gesture directions and center resolve to their configured actions");
            var invalidGesture = service.Clone(config);
            invalidGesture.Gestures[0].LeftKind = ActionKind.Gesture;
            invalidGesture.Gestures[0].LeftValue = "ウィンドウ操作";
            Check(ConfigValidator.Validate(invalidGesture).Any(x => x.Contains("入れ子")), "nested gestures are rejected before saving");
            var missingGesture = service.Clone(config);
            missingGesture.Profiles[0].Mappings[0].Value = "存在しないジェスチャー";
            Check(ConfigValidator.Validate(missingGesture).Any(x => x.Contains("ジェスチャー「存在しないジェスチャー」")), "missing gesture references are rejected before saving");
            var gestureReferences = service.Clone(config).Profiles;
            GestureManagerWindow.RenameReferences(gestureReferences, "ウィンドウ操作", "名前変更後");
            Check(gestureReferences[0].Mappings[0].Value == "名前変更後", "renaming a gesture updates every mapping reference");
            GestureManagerWindow.ClearReferences(gestureReferences, "名前変更後");
            Check(gestureReferences[0].Mappings[0] is { Kind: ActionKind.None, Value: "" }, "deleting a gesture clears every mapping reference");
            Check(GestureManagerWindow.SupportedActionChoices.Select(x => x.Kind).SequenceEqual([ActionKind.Key, ActionKind.Profile, ActionKind.Shortcut, ActionKind.Text, ActionKind.Launch, ActionKind.Macro]), "gesture directions expose key, profile, shortcut, text, app, and macro choices");
            var blankAction = service.Clone(config);
            blankAction.Profiles[0].Mappings.Add(new Mapping { Input = "Q", Kind = ActionKind.Shortcut, Value = "" });
            Check(ConfigValidator.Validate(blankAction).Count == 0, "blank execution value can be left unfinished without a validation error");
            Check(!MainWindow.MappingInterceptsInput(new Mapping { Input = "MouseBack", Kind = ActionKind.None, Value = "stale" }) && !MainWindow.MappingInterceptsInput(new Mapping { Input = "M", Kind = ActionKind.Shortcut, Value = "" }) && MainWindow.MappingInterceptsInput(new Mapping { Input = "MouseBack", Kind = ActionKind.Disabled }) && MainWindow.MappingInterceptsInput(new Mapping { Input = "MouseBack", Kind = ActionKind.None, LongPressKind = ActionKind.Key, LongPressValue = "Enter" }), "unfinished mappings do not look assigned or block native input while disabled and long-only mappings still intercept");
            Check(MainWindow.IsElevatedInputMappingForTest(new Mapping { Input = "F1", Kind = ActionKind.None, LongPressKind = ActionKind.Shortcut, LongPressValue = OverlayService.DeckPanelAction }) && MainWindow.IsElevatedInputMappingForTest(new Mapping { Input = "F1", Kind = ActionKind.None, LongPressKind = ActionKind.Launch, LongPressValue = "notepad.exe" }), "the elevated helper owns every configured action kind while the ordinary UI host yields elevated foreground input");
            Check(ConditionMatcher.IsVirtualMachineConsoleProcess("VirtualBoxVM.exe") && ConditionMatcher.IsVirtualMachineConsoleProcess("virtualboxvm") && !ConditionMatcher.IsVirtualMachineConsoleProcess("VirtualBox.exe"), "VirtualBox VM console process detection remains available to the elevated helper without restricting the normal UI runtime");
            Check(MainWindow.ShouldFocusExecutionForSelectedInput(null) && !MainWindow.ShouldFocusExecutionForSelectedInput(new Mapping { Input = "A", Kind = ActionKind.Key, Value = "B" }), "only an unassigned key automatically focuses the execution-value editor");
            var hoverMapping = new Mapping { Input = "Space+K", Kind = ActionKind.Shortcut, Value = "Ctrl+C", LongPressKind = ActionKind.Launch, LongPressValue = @"C:\Apps\Sample.exe", LongPressMs = 600 };
            string? hoverText = MainWindow.AssignmentToolTipText(hoverMapping);
            var hoverRows = MainWindow.AssignmentToolTipRows(hoverMapping);
            var supportedHoverRows = new[]
            {
                new Mapping { Kind = ActionKind.Disabled },
                new Mapping { Kind = ActionKind.Key, Value = "Enter" },
                new Mapping { Kind = ActionKind.Shortcut, Value = "Ctrl+V" },
                new Mapping { Kind = ActionKind.Text, Value = "サンプル テキスト" },
                new Mapping { Kind = ActionKind.Launch, Value = @"C:\Apps\Sample.exe" },
                new Mapping { Kind = ActionKind.Mouse, Value = "MouseRight" },
                new Mapping { Kind = ActionKind.Macro, Value = "動画編集" },
                new Mapping { Kind = ActionKind.Profile, Value = "仕事" },
                new Mapping { Kind = ActionKind.Gesture, Value = "ウィンドウ操作" }
            }.SelectMany(mapping => MainWindow.AssignmentToolTipRows(mapping)).ToArray();
            var overlayHoverRow = MainWindow.AssignmentToolTipRows(new Mapping { Kind = ActionKind.Shortcut, Value = OverlayService.ClockAction }).Single();
            Check(MainWindow.AssignmentToolTipText(null) == null
                && hoverText?.Contains("TAP  コピー  Ctrl + C") == true
                && hoverText.Contains("HOLD  Sample")
                && !hoverText.Contains("ms", StringComparison.OrdinalIgnoreCase)
                && !hoverText.Contains("アクション：")
                && !hoverText.Contains("実行内容：")
                && hoverRows is [{ Slot: "TAP", Name: "コピー" }, { Slot: "HOLD", Name: "Sample" }]
                && hoverRows[0].Keycaps.SequenceEqual(["Ctrl", "C"])
                && supportedHoverRows.Length == Enum.GetValues<ActionKind>().Count(kind => kind != ActionKind.None)
                && supportedHoverRows.All(row => !string.IsNullOrWhiteSpace(row.Slot) && !string.IsNullOrWhiteSpace(row.Name))
                && overlayHoverRow is { Name: "クロック", Detail: "オーバーレイ", Keycaps.Count: 0 },
                "assigned-key hover uses concise TAP/HOLD rows and provides a non-empty friendly display for shortcuts, overlays, keys, text, apps, mouse actions, macros, profiles, gestures, and disabled actions");
            Check(MainWindow.DisplayInputName("MouseRight+K") == "右クリック + K" && MainWindow.DisplayInputName("Taskbar+MouseMiddle") == "タスクバー + ホイールクリック" && MainWindow.DisplayInputName("MouseBack+WheelUp") == "戻る + ホイール上", "internal layer names are presented as beginner-friendly input names");
            var staleTaskbarLeftHold = new Mapping { Input = "Taskbar+MouseLeft", Kind = ActionKind.None, LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+Shift+Escape" };
            var taskbarRightLongOnly = new Mapping { Input = "Taskbar+MouseRight", Kind = ActionKind.None, LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+Shift+Escape" };
            var staleTaskbarTap = new Mapping { Input = "Taskbar+MouseLeft", Kind = ActionKind.Shortcut, Value = OverlayService.DeckPanelAction };
            var staleTaskbarRightTap = new Mapping { Input = "Taskbar+MouseRight", Kind = ActionKind.Shortcut, Value = OverlayService.DeckPanelAction };
            var taskbarReplayEvents = new List<string>();
            int taskbarReplayFailures = 0;
            MainWindow.ProcessTaskbarClickReplays(["MouseLeft", "MouseRight"], click => { taskbarReplayEvents.Add(click); return true; }, () => { }, () => taskbarReplayFailures++);
            var failedTaskbarReplayEvents = new List<string>();
            int defensiveTaskbarReleases = 0;
            MainWindow.ProcessTaskbarClickReplays(["MouseLeft", "MouseRight"], click => { failedTaskbarReplayEvents.Add(click); return false; }, () => defensiveTaskbarReleases++, () => taskbarReplayFailures++);
            var taskbarReplayBatches = new List<(uint Flag, uint Data)[]>();
            var taskbarHookReturn = InputEngine.CreateHookReturnBarrierForTest();
            int taskbarSendAfterHookReturn = 0;
            var taskbarBarrierReplay = Task.Run(() => MainWindow.ProcessTaskbarClickReplays(
                [new MainWindow.TaskbarClickReplayRequest("MouseRight", taskbarHookReturn.Barrier)],
                _ => { Interlocked.Increment(ref taskbarSendAfterHookReturn); return true; },
                () => { },
                () => taskbarReplayFailures++));
            bool taskbarReplayWaitedForHookReturn = !SpinWait.SpinUntil(() => Volatile.Read(ref taskbarSendAfterHookReturn) != 0, 100);
            taskbarHookReturn.Complete();
            bool taskbarReplayCompletedAfterHookReturn = taskbarBarrierReplay.Wait(1000)
                && Volatile.Read(ref taskbarSendAfterHookReturn) == 1;
            using var completedTaskbarReplayQueue = new BlockingCollection<MainWindow.TaskbarClickReplayRequest>();
            completedTaskbarReplayQueue.CompleteAdding();
            MainWindow.TaskbarClickReplayRequest? taskbarReplayFallback = null;
            var fallbackRequest = new MainWindow.TaskbarClickReplayRequest("MouseRight", Task.CompletedTask);
            bool taskbarReplayQueuedAfterCompletion = MainWindow.TryQueueTaskbarClickReplay(
                completedTaskbarReplayQueue,
                fallbackRequest,
                request => taskbarReplayFallback = request);
            try
            {
                InputEngine.MouseClickBatchOutputForTest = batch => taskbarReplayBatches.Add(batch);
                MainWindow.ProcessTaskbarClickReplays(["MouseLeft", "MouseRight"], InputEngine.SendMouseClickAtomic, () => defensiveTaskbarReleases++, () => taskbarReplayFailures++);
            }
            finally
            {
                InputEngine.MouseClickBatchOutputForTest = null;
            }
            Check(MainWindow.TaskbarShortClickReplay(staleTaskbarLeftHold, "Taskbar+MouseLeft") == null
                && MainWindow.TaskbarShortClickReplay(staleTaskbarTap, "Taskbar+MouseLeft") == null
                && MainWindow.TaskbarShortClickReplay(taskbarRightLongOnly, "Taskbar+MouseRight") == "MouseRight"
                && MainWindow.TaskbarShortClickReplay(staleTaskbarRightTap, "Taskbar+MouseRight") == "MouseRight"
                && MainWindow.TaskbarShortClickReplay(taskbarRightLongOnly, "MouseLeft") == null
                && MainWindow.TaskbarShortClickReplay(taskbarRightLongOnly, "Taskbar+MouseRight", longPress: true) == null
                && !MainWindow.MappingInterceptsTaskbarInvocation(staleTaskbarLeftHold)
                && MainWindow.MappingInterceptsTaskbarInvocation(taskbarRightLongOnly)
                && !MainWindow.MappingInterceptsTaskbarInvocation(staleTaskbarTap)
                && !MainWindow.MappingInterceptsTaskbarInvocation(staleTaskbarRightTap)
                && taskbarReplayEvents.SequenceEqual(["MouseLeft", "MouseRight"])
                && failedTaskbarReplayEvents.SequenceEqual(["MouseLeft", "MouseRight"])
                && taskbarReplayFailures == 1
                && defensiveTaskbarReleases == 2
                && taskbarReplayBatches.Count == 2
                && taskbarReplayBatches[0].SequenceEqual([(2u, 0u), (4u, 0u)])
                && taskbarReplayBatches[1].SequenceEqual([(8u, 0u), (16u, 0u)])
                && taskbarReplayWaitedForHookReturn
                && taskbarReplayCompletedAfterHookReturn
                && !taskbarReplayQueuedAfterCompletion
                && taskbarReplayFallback == fallbackRequest,
                "taskbar left click is never intercepted, while taskbar right HOLD short-click restoration survives queue completion, waits for the physical hook to return, and replays one atomic input batch");
            Check(MainWindow.IsTaskbarMappedInput("Taskbar+MouseMiddle")
                  && MainWindow.IsTaskbarMappedInput("Taskbar+MouseMiddle:Long")
                  && !MainWindow.IsTaskbarMappedInput("MouseMiddle"),
                "taskbar short and long actions keep their dedicated execution context");
            var mouseDisplayCases = new Dictionary<string, string> { { "MouseLeft", "マウス：左クリック" }, { "MouseRight", "マウス：右クリック" }, { "MouseMiddle", "マウス：ホイールクリック" }, { "MouseBack", "マウス：戻る" }, { "MouseForward", "マウス：進む" }, { "WheelUp", "マウス：ホイール上" }, { "WheelDown", "マウス：ホイール下" }, { "TiltLeft", "マウス：チルト左" }, { "TiltRight", "マウス：チルト右" }, { "ShiftDrag", "マウス：Shift + 左クリック" }, { "CtrlDrag", "マウス：Ctrl + 左クリック" }, { "AltDrag", "マウス：Alt + 左クリック" } };
            Check(mouseDisplayCases.All(x => MainWindow.DisplayActionValue(ActionKind.Mouse, x.Key) == x.Value && MainWindow.NormalizeEditorAction(ActionKind.Shortcut, x.Value, ActionKind.Mouse, x.Key) == (ActionKind.Mouse, x.Key))
                && ActionCatalog.TryNormalizeMouseAction("MouseX", out string legacyMouseX) && legacyMouseX == "MouseForward",
                "every supported mouse execution value round-trips and legacy MouseX output safely becomes MouseForward");
            var unsupportedModifierLong = new Mapping { Input = "Space+MouseRight", Layer = "Space", Kind = ActionKind.Mouse, Value = "CtrlDrag", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+K" };
            bool clearedUnsupportedModifierLong = MainWindow.ClearUnsupportedLongPress(unsupportedModifierLong);
            var normalAlphabetLong = new Mapping { Input = "O", Layer = "通常", LongPressKind = ActionKind.Profile, LongPressValue = "標準" };
            bool clearedNormalAlphabetLong = MainWindow.ClearUnsupportedLongPress(normalAlphabetLong);
            Check(new[] { "ShiftDrag", "CtrlDrag", "AltDrag" }.All(value => !MainWindow.IsLongPressSupportedFor(new Mapping { Input = "Space+MouseRight", Layer = "Space", Kind = ActionKind.Mouse, Value = value }))
                && clearedUnsupportedModifierLong && unsupportedModifierLong is { LongPressKind: ActionKind.None, LongPressValue: "" }
                && clearedNormalAlphabetLong && normalAlphabetLong is { LongPressKind: ActionKind.None, LongPressValue: "" }
                && InputAssignmentPolicy.LongPressUnavailableReason(new Mapping { Input = "O", Layer = "通常" }) == "通常の英字では長押し不可"
                && InputAssignmentPolicy.LongPressUnavailableReason(new Mapping { Input = "Space+MouseRight", Layer = "Space", Kind = ActionKind.Mouse, Value = "CtrlDrag" }) == "修飾クリックとの併用不可"
                && MainWindow.IsLongPressSupportedFor(new Mapping { Input = "Space+MouseRight", Layer = "Space", Kind = ActionKind.Mouse, Value = "MouseRight" })
                && MainWindow.IsLongPressSupportedFor(new Mapping { Input = "Space+MouseRight", Layer = "Space", Kind = ActionKind.None, LongPressKind = ActionKind.Mouse, LongPressValue = "CtrlDrag" }),
                "modifier clicks and normal alphabet keys reject unreachable long actions while ordinary short actions and long-side modifier clicks remain supported");
            var rightLayerConflict = new List<Mapping>
            {
                new() { Input = "MouseRight", Layer = "通常", Kind = ActionKind.Mouse, Value = "MouseRight", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+K" },
                new() { Input = "MouseRight+K", Layer = "MouseRight", Kind = ActionKind.Shortcut, Value = "Ctrl+C" }
            };
            var staleSpaceMappings = new List<Mapping>
            {
                new() { Input = "Space", Layer = "通常", Kind = ActionKind.Shortcut, Value = "DeckPanel:stale" },
                new() { Input = "CapsLock", Layer = "通常", Kind = ActionKind.Shortcut, Value = "DeckPanel:stale-caps" },
                new() { Input = "Space+K", Layer = "Space", Kind = ActionKind.Shortcut, Value = "Ctrl+K" }
            };
            var staleTaskbarMappings = new List<Mapping>
            {
                new() { Input = "Taskbar+MouseLeft", Layer = "Taskbar", Kind = ActionKind.Shortcut, Value = "Ctrl+L", LongPressKind = ActionKind.Key, LongPressValue = "F9" },
                new() { Input = "Taskbar+MouseRight", Layer = "Taskbar", Kind = ActionKind.Shortcut, Value = "Ctrl+R", LongPressKind = ActionKind.Key, LongPressValue = "F10" }
            };
            Check(InputAssignmentPolicy.IsImpulseInput("Space+WheelUp")
                && !InputAssignmentPolicy.SupportsGesture("CapsLock+TiltRight")
                && new[] { "Space", "CapsLock", "Space+Space", "CapsLock+CapsLock", "MouseRight+MouseRight", "MouseBack+MouseBack", "MouseForward+MouseForward", "MouseX", "Taskbar+MouseX", "Taskbar+MouseLeft" }.All(InputAssignmentPolicy.IsUnreachableInput)
                && InputAssignmentPolicy.PreservesNativeShortPress("Taskbar+MouseLeft")
                && InputAssignmentPolicy.PreservesNativeShortPress("Taskbar+MouseRight")
                && !InputAssignmentPolicy.CanAssignShortPress("Taskbar+MouseLeft")
                && !InputAssignmentPolicy.CanAssignShortPress("Taskbar+MouseRight")
                && !InputAssignmentPolicy.CanExecuteLongPress(new Mapping { Input = "Taskbar+MouseLeft", Layer = "Taskbar", Kind = ActionKind.None })
                && InputAssignmentPolicy.CanAssignShortPress("Taskbar+MouseMiddle")
                && InputEngine.MustReplayNativeLayerTap("Space") && !InputEngine.MustReplayNativeLayerTap("CapsLock")
                && InputAssignmentPolicy.SanitizeMappings(staleSpaceMappings)
                && staleSpaceMappings.Count == 1 && staleSpaceMappings[0].Input == "Space+K"
                && InputAssignmentPolicy.SanitizeMappings(staleTaskbarMappings)
                && staleTaskbarMappings is [{ Input: "Taskbar+MouseRight", Kind: ActionKind.None, Value: "", LongPressKind: ActionKind.Key, LongPressValue: "F10" }]
                && InputAssignmentPolicy.SanitizeMappings(rightLayerConflict)
                && rightLayerConflict[0] is { LongPressKind: ActionKind.None, LongPressValue: "" },
                "the shared assignment policy fully reserves taskbar left click/drag, reserves taskbar right TAP, and rejects impulse gestures, stale layer-source actions, fake X1/self-layer inputs, and conflicting direct mouse long press");
            var protectedTransferMappings = new List<Mapping>
            {
                new() { Input = "Taskbar+MouseRight", Layer = "Taskbar", Kind = ActionKind.None, LongPressKind = ActionKind.Key, LongPressValue = "F10" }
            };
            var shortOnlyTransfer = new Mapping { Input = "F8", Layer = "通常", Kind = ActionKind.Shortcut, Value = "Ctrl+8" };
            var dualTransfer = new Mapping { Input = "F9", Layer = "通常", Kind = ActionKind.Shortcut, Value = "Ctrl+9", LongPressKind = ActionKind.Key, LongPressValue = "F11" };
            bool shortTransferPrepared = MainWindow.TryPrepareTransferredMapping(shortOnlyTransfer, "Taskbar+MouseRight", "Taskbar", protectedTransferMappings, out _);
            bool dualTransferPrepared = MainWindow.TryPrepareTransferredMapping(dualTransfer, "Taskbar+MouseRight", "Taskbar", protectedTransferMappings, out var protectedDualTransfer);
            bool taskbarLeftTransferPrepared = MainWindow.TryPrepareTransferredMapping(dualTransfer, "Taskbar+MouseLeft", "Taskbar", protectedTransferMappings, out _);
            int protectedAllLayerApplied = MainWindow.AssignMappingToAllLayers(protectedTransferMappings, "MouseRight", shortOnlyTransfer);
            var nonDefaultAllLayerMappings = new List<Mapping>
            {
                new() { Input = "Space+F8", Layer = "Space", Kind = ActionKind.Text, Value = "all-layers" }
            };
            int nonDefaultAllLayerApplied = MainWindow.AssignMappingToAllLayers(nonDefaultAllLayerMappings, "F8", nonDefaultAllLayerMappings[0]);
            Check(!shortTransferPrepared
                && dualTransferPrepared
                && !taskbarLeftTransferPrepared
                && protectedDualTransfer is { Kind: ActionKind.None, Value: "", LongPressKind: ActionKind.Key, LongPressValue: "F11" }
                && protectedAllLayerApplied == MainWindow.AllAssignmentLayerNames.Count - 1
                && protectedTransferMappings.Single(mapping => mapping.Input == "Taskbar+MouseRight") is { Kind: ActionKind.None, LongPressKind: ActionKind.Key, LongPressValue: "F10" },
                "copy, multi-copy, and all-layer transfers cannot target taskbar left click/drag or overwrite taskbar right TAP while valid destinations still receive the Action");
            Check(nonDefaultAllLayerApplied == MainWindow.AllAssignmentLayerNames.Count
                && nonDefaultAllLayerMappings.Any(mapping => mapping.Input == "F8" && mapping.Layer == "通常")
                && MainWindow.AllAssignmentLayerNames.All(layer => nonDefaultAllLayerMappings.Any(mapping => mapping.Input == layer + "+F8" && mapping.Value == "all-layers")),
                "all-layer assignment started from a non-default layer also fills the default layer while preserving its source");
            var allProfileSource = new Profile { Name = "元", Mappings = [new Mapping { Input = "F8", Kind = ActionKind.Text, Value = "source" }] };
            var allProfileFirstTarget = new Profile { Name = "先1", Mappings = [new Mapping { Input = "F8", Kind = ActionKind.Shortcut, Value = "Ctrl+8" }] };
            var allProfileSecondTarget = new Profile { Name = "先2", Mappings = [new Mapping { Input = "A", Kind = ActionKind.Key, Value = "B" }] };
            var allProfileTemplate = new Mapping { Input = "F8", Layer = "通常", Kind = ActionKind.Text, Value = "全体", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+Shift+8", LongPressMs = 720, Application = "notepad.exe" };
            int allProfileApplied = MainWindow.AssignMappingToAllProfiles([allProfileSource, allProfileFirstTarget, allProfileSecondTarget], allProfileSource, "F8", allProfileTemplate);
            Check(allProfileApplied == 2
                && allProfileSource.Mappings.Single(mapping => mapping.Input == "F8").Value == "source"
                && new[] { allProfileFirstTarget, allProfileSecondTarget }.All(profile => profile.Mappings.Single(mapping => mapping.Input == "F8") is { Kind: ActionKind.Text, Value: "全体", LongPressKind: ActionKind.Shortcut, LongPressValue: "Ctrl+Shift+8", LongPressMs: 720, Application: "notepad.exe" }),
                "all-profile assignment replaces the same input in every other profile with the complete source assignment without changing the current profile");
            config.Profiles.Add(new Profile { Name = "アプリ用", AutoSwitchEnabled = true, AutoSwitchApplications = ["notepad.exe"] });
            config.ActiveProfile = "アプリ用";
            config.AutoSwitchProfilesByCursor = false;
            config.ShowProfileSwitchOverlay = false;
            config.ShowDesktopNumberInTray = true;
            config.CheckForUpdates = false;
            config.DismissedUpdateVersion = "9.9.9";
            config.PendingUpdateNotesVersion = "9.9.10";
            config.PendingUpdateNotesBody = "- Deckを改善";
            config.LastShownUpdateNotesVersion = "9.9.8";
            config.WindowActionTarget = WindowActionTarget.WindowUnderCursor;
            config.ThemeMode = AppThemeMode.Light;
            config.UiAnimationsEnabled = false;
            config.DetailedDiagnosticsEnabled = true;
            config.LastUpdateCheckUtcTicks = DateTimeOffset.UtcNow.UtcTicks;
            config.RecordKeyboardInputInMacros = false;
            config.RecordMappedActionsInMacros = true;
            config.RecordMouseMovementInMacros = true;
            config.RecordMouseMovementRelativeInMacros = false;
            config.ArchiveWatchFolder = @"C:\Watch";
            config.ArchiveDestinationFolder = @"D:\Extracted";
            config.ShowArchiveExtractionOverlay = false;
            config.AutoSave = true;
            config.KeyboardLayout = "US";
            config.SpaceHoldRepeatEnabled = true;
            config.SpaceHoldRepeatDelayMs = 450;
            config.InputDisabledApplications = ["RobloxPlayerBeta.exe", "game.exe"];
            config.GestureThresholdPixels = 12;
            config.ClockBackgroundMode = ClockBackgroundMode.Image;
            config.ClockDisplayMode = ClockDisplayMode.FullDateAndTime;
            config.ClockBackgroundImage = @"C:\Wallpapers\clock.jpg";
            config.ClockSolidColor = "#123456";
            config.ClockShowOnAllMonitors = false;
            config.InputPanelOpacityPercent = 67;
            config.DeckChromeOpacityPercent = 23;
            config.UseSharedDeckPanel = true;
            config.SharedDeckMappings = [new Mapping { Input = "Deck+01", Layer = "Deck", Kind = ActionKind.Key, Value = "A", Description = "共通", DeckIcon = "home", DeckIconPath = @"C:\Icons\home.png" }];
            config.DeckPanelLeft = 123.5;
            config.DeckPanelTop = 234.5;
            config.DeckPanelCollapsedLeft = 246.5;
            config.DeckPanelCollapsedTop = 357.5;
            config.DeckPanelWidth = 987.5;
            config.DeckPanelHeight = 543.5;
            config.DeckAfterActionBehavior = DeckAutoDismissBehavior.Hide;
            config.DeckPointerLeaveBehavior = DeckAutoDismissBehavior.StayVisible;
            config.DeckLayouts[0].PanelPinned = true;
            config.DeckLayouts[0].PanelLeft = 111.5;
            config.DeckLayouts[0].PanelTop = 222.5;
            config.DeckLayouts[0].PanelCollapsedLeft = 333.5;
            config.DeckLayouts[0].PanelCollapsedTop = 444.5;
            config.DeckLayouts[0].PanelPadding = 18;
            config.DeckLayouts[0].PanelCornerRadius = 9;
            config.DeckLayouts[0].HoverAnimationEnabled = false;
            config.DeckLayouts[0].Mappings.Add(new Mapping { Input = "Deck+02", Layer = DeckPanelLayout.Layer, DeckMonitor = "battery" });
            config.DeckLayouts[0].Mappings.Add(new Mapping { Input = "Deck+03", Layer = DeckPanelLayout.Layer, Kind = ActionKind.Shortcut, Value = "Desktop3", DeckIconHidden = true });
            config.NumpadPanelLeft = 345.5;
            config.NumpadPanelTop = 456.5;
            config.ExtendedKeypadPanelLeft = 567.5;
            config.ExtendedKeypadPanelTop = 678.5;
            service.Save(config);
            var loaded = service.Load();
            Check(loaded.ActiveProfile == "アプリ用", "JSON roundtrip");
            Check(!loaded.AutoSwitchProfilesByCursor, "legacy cursor profile option remains disabled after roundtrip");
            Check(!loaded.ShowProfileSwitchOverlay, "profile switch overlay option roundtrip");
            Check(loaded.ShowDesktopNumberInTray, "tray desktop number setting roundtrip");
            Check(!loaded.CheckForUpdates && loaded.DismissedUpdateVersion == "9.9.9" && loaded.PendingUpdateNotesVersion == "9.9.10" && loaded.PendingUpdateNotesBody == "- Deckを改善" && loaded.LastShownUpdateNotesVersion == "9.9.8", "update-check, dismissal, and one-time release-note settings roundtrip");
            Check(loaded.WindowActionTarget == WindowActionTarget.WindowUnderCursor, "window action target setting roundtrip");
            Check(loaded.ThemeMode == AppThemeMode.Light && loaded.LastUpdateCheckUtcTicks == config.LastUpdateCheckUtcTicks, "theme mode and last update check roundtrip");
            Check(!loaded.UiAnimationsEnabled, "explicitly disabled RELYR animations remain disabled after roundtrip");
            Check(loaded.DetailedDiagnosticsEnabled, "the explicit detailed-diagnostics consent setting roundtrips without being enabled by default");
            Check(loaded.SharedDeckMappings.Single().DeckIcon == "home" && loaded.SharedDeckMappings.Single().DeckIconPath == @"C:\Icons\home.png", "Deck preset and custom icon settings roundtrip");
            Check(DeckPanelLayout.FindMapping(loaded.DeckLayouts[0], 2) is { DeckMonitor: "battery" }, "Deck monitor identity roundtrip");
            Check(DeckPanelLayout.FindMapping(loaded.DeckLayouts[0], 3) is { DeckIconHidden: true }, "an explicitly hidden Deck icon remains hidden after restart");
            Check(DeckMonitorCatalog.Items.Count >= 27 && DeckMonitorCatalog.Items.Any(item => item.Id == "battery") && DeckMonitorCatalog.Items.Any(item => item.Id == "brightness") && DeckMonitorCatalog.Items.Any(item => item.Id == "virtual-desktop") && DeckMonitorCatalog.TryGet("auto-extract", out var autoExtractMonitor) && autoExtractMonitor.Interaction == DeckMonitorInteraction.AutoExtractToggle && DeckMonitorCatalog.TryGet("timer", out var timerMonitor) && timerMonitor.Interaction == DeckMonitorInteraction.Timer, "Deck monitor catalog includes status, desktop, direct-control, auto-extraction, and timer tiles");
            Check(DeckMonitorCatalog.Items.All(item => item.Glyph.Length == 1 && item.Glyph[0] is >= '\uE000' and <= '\uF8FF'), "every Deck monitor uses one supported private-use Fluent icon instead of text rendered through an icon font");
            Check(DeckMonitorCatalog.Items.All(item => item.Name.All(character => character <= 0x7f))
                && DeckMonitorCatalog.TryGet("disk-write", out var writeMonitor) && writeMonitor.Name == "WRITE"
                && DeckMonitorCatalog.TryGet("volume", out var volumeMonitor) && volumeMonitor.Name == "VOLUME"
                && DeckMonitorCatalog.TryGet("network-up", out var uploadMonitor) && uploadMonitor.Name == "UPLOAD"
                && DeckMonitorCatalog.TryGet("network-down", out var downloadMonitor) && downloadMonitor.Name == "DOWNLOAD",
                "Deck monitor face labels use compact English terminology consistently");
            Check(DeckMonitorCatalog.PaletteDescription("cpu") == "CPU使用率"
                && DeckMonitorCatalog.PaletteDescription("brightness") == "画面の明るさ"
                && DeckMonitorCatalog.PaletteDescription("timer") == "タイマーの残り時間"
                && !DeckMonitorCatalog.PaletteDescription("volume").Contains("クリック", StringComparison.Ordinal),
                "Deck monitor library uses concise Japanese explanations without operation instructions on Action cards");
            Check(DeckTimerService.FormatRemaining(TimeSpan.FromSeconds(61)) == "01:01"
                && DeckTimerService.FormatRemaining(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(2)) == "1:00:02",
                "Deck timer countdown formatting remains compact on the English Deck face");
            var desktopReading = SystemMonitorService.VirtualDesktopReading(4, 2);
            Check(desktopReading is { Text: "2", Detail: "OF 4", Available: true }
                && Math.Abs(desktopReading.Level!.Value - .5) < .001
                && !SystemMonitorService.VirtualDesktopReading(0, 0).Available,
                "virtual desktop monitor renders a one-based current number and rejects invalid native state safely");
            Check(SystemMonitorService.WifiReading(true, null) is { Text: "ON", Detail: "CONNECTED", Available: true }
                && !SystemMonitorService.WifiReading(false, null).Available,
                "an active Wi-Fi connection remains authoritative when the separate radio-state query is unavailable");
            Check(DeckPanelOverlayWindow.WheelAdjustedPercent(51, 120, 2) == 53
                && DeckPanelOverlayWindow.WheelAdjustedPercent(1, -240, 2) == 0,
                "Deck volume wheel changes in bounded two-percent steps including multi-notch input");
            var newExplorerStart = SystemInputOutput.CreateLaunchStartInfo(SystemInputOutput.NewExplorerWindowAction);
            Check(newExplorerStart.FileName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
                && newExplorerStart.Arguments == "/n," && newExplorerStart.UseShellExecute,
                "the dedicated Explorer action requests a separate Windows Explorer window without changing standard Win+E");
            var calculatorApp = new InstalledApplicationInfo("Calculator", @"C:\Windows\System32\calc.exe", "test");
            var notepadApp = new InstalledApplicationInfo("メモ帳", @"C:\Windows\System32\notepad.exe", "test");
            Check(ApplicationPickerWindow.MatchesSearch(calculatorApp, "C")
                && !ApplicationPickerWindow.MatchesSearch(notepadApp, "C")
                && ApplicationPickerWindow.MatchesSearch(notepadApp, "pad")
                && ProfileManagerWindow.ExecutableNameForAutoSwitch(new InstalledApplicationInfo("Example", @"C:\Apps\Example.exe", "test")) == "Example.exe",
                "single-character app search behaves as an initial-letter jump while installed profile targets retain a precise executable identity");
            var integratedGpuMemory = SystemMonitorService.SelectGpuMemoryUsage(0, 2.5 * 1024 * 1024 * 1024, 1);
            Check(integratedGpuMemory is { Detail: "SHARED VRAM" } && integratedGpuMemory.Bytes > 2d * 1024 * 1024 * 1024
                && SystemMonitorService.SelectGpuMemoryUsage(0, 0, 1) == null,
                "integrated GPUs fall back to shared GPU memory and a genuine unavailable value is never formatted as 0M");
            Check(MainWindow.DeckPresetForSize(3, 3) == "3x3"
                && MainWindow.DeckPresetForSize(6, 4) == "6x4"
                && MainWindow.DeckPresetForSize(9, 5) == "9x5"
                && MainWindow.DeckPresetForSize(8, 2) == "custom"
                && MainWindow.TryResolveDeckLayoutSize("6x4", "", "", out int mediumDeckColumns, out int mediumDeckRows)
                && (mediumDeckColumns, mediumDeckRows) == (6, 4),
                "Deck size presets grow consistently while legacy 8x2 Deck dimensions remain custom data");
            var clockWork = SystemMonitorService.WorkPlan(["clock"]);
            var temperatureWork = SystemMonitorService.WorkPlan(["temperature"]);
            var gpuWork = SystemMonitorService.WorkPlan(["gpu", "vram"]);
            var diskWork = SystemMonitorService.WorkPlan(["disk-read"]);
            var latencyWork = SystemMonitorService.WorkPlan(["network-latency"]);
            Check(SystemMonitorService.DiskThroughputQuery.Contains("PerfDisk_PhysicalDisk", StringComparison.Ordinal)
                && !SystemMonitorService.DiskThroughputQuery.Contains("LogicalDisk", StringComparison.Ordinal)
                && SystemMonitorService.RateLevel(0, 1024) == 0
                && SystemMonitorService.RateLevel(1024, 1024) == 1
                && clockWork == new SystemMonitorService.MonitorWorkPlan(false, false, false, false)
                && temperatureWork.HardwareSensors && !temperatureWork.GpuWmi
                && gpuWork.GpuWmi && !gpuWork.HardwareSensors
                && diskWork.DiskWmi && latencyWork.GatewayPing,
                "Deck monitoring requests expensive sensor, GPU, disk, and ping work only for visible tiles while keeping the physical-disk scale bounded");
            Check(AnimatedGifIcon.MaxCachedAnimations <= 32
                && DeckPanelLayout.MaxCachedThumbnails <= 192
                && ApplicationIconService.MaxCacheEntries <= 256
                && OverlayService.MaxHiddenDeckPanels <= 1,
                "image, thumbnail, application-icon, and reusable Deck caches have explicit low memory ceilings");
            Check(DeckMonitorView.VisualKind("cpu") == MonitorVisualKind.Sparkline
                && DeckMonitorView.VisualKind("disk-read") == MonitorVisualKind.Columns
                && DeckMonitorView.VisualKind("memory") == MonitorVisualKind.Dots
                && DeckMonitorView.VisualKind("battery") == MonitorVisualKind.Gauge
                && DeckMonitorView.MonitorAccentColor("cpu", true) != DeckMonitorView.MonitorAccentColor("temperature", true)
                && DeckMonitorView.MonitorAccentColor("cpu", true) != DeckMonitorView.MonitorAccentColor("cpu", false),
                "Deck monitors use live metric-specific graph styles and readable dark/light accent colors");
            double[] visibleHistory = DeckMonitorView.NormalizeHistoryForDisplay([.14, .15, .13, .16]);
            double[] flatHistory = DeckMonitorView.NormalizeHistoryForDisplay([.14, .14, .14]);
            Check(visibleHistory.Max() - visibleHistory.Min() >= .6
                && flatHistory.All(value => Math.Abs(value - .5) < .001),
                "compact Deck charts amplify real short-term variation while a genuinely flat signal stays visually flat");
            Check(RadioMonitorService.AggregateBluetoothState([]) == null
                && RadioMonitorService.AggregateBluetoothState([Windows.Devices.Radios.RadioState.Off]) == false
                && RadioMonitorService.AggregateBluetoothState([Windows.Devices.Radios.RadioState.Off, Windows.Devices.Radios.RadioState.On]) == true,
                "Bluetooth monitor aggregates every radio into unavailable, off, or on without blocking the UI thread");
            var selectedSensors = HardwareSensorProvider.Select([
                new HardwareSensorCandidate(LibreHardwareMonitor.Hardware.HardwareType.Cpu, LibreHardwareMonitor.Hardware.SensorType.Temperature, "Core #1", 59),
                new HardwareSensorCandidate(LibreHardwareMonitor.Hardware.HardwareType.Cpu, LibreHardwareMonitor.Hardware.SensorType.Temperature, "CPU Package", 63),
                new HardwareSensorCandidate(LibreHardwareMonitor.Hardware.HardwareType.GpuIntel, LibreHardwareMonitor.Hardware.SensorType.Temperature, "GPU Core", 54),
                new HardwareSensorCandidate(LibreHardwareMonitor.Hardware.HardwareType.Motherboard, LibreHardwareMonitor.Hardware.SensorType.Fan, "CPU Fan", 1280)
            ]);
            Check(selectedSensors is { CpuTemperature: 63, GpuTemperature: 54, FanRpm: 1280 }, "hardware sensor selection prefers CPU package, GPU core, and CPU fan readings deterministically");
            var verifiedSensors = HardwareSensorProvider.KeepVerifiedHardwareSensors(
                new HardwareSensorSnapshot(CpuTemperature: 63, CpuTemperatureName: "CPU Package"));
            Check(verifiedSensors is { CpuTemperature: 63, CpuTemperatureName: "CPU Package", FanRpm: null },
                "hardware monitors keep verified package/RPM readings and never substitute ACPI zones or requested fan targets");
            Check(!loaded.RecordKeyboardInputInMacros, "macro keyboard recording option roundtrip");
            Check(loaded.RecordMappedActionsInMacros, "macro mapped-action recording option roundtrip");
            Check(loaded.RecordMouseMovementInMacros, "macro mouse trajectory option roundtrip");
            Check(!loaded.RecordMouseMovementRelativeInMacros, "macro mouse fixed/relative movement mode roundtrip");
            Check(loaded.ArchiveWatchFolder == @"C:\Watch" && loaded.ArchiveDestinationFolder == @"D:\Extracted" && !loaded.ShowArchiveExtractionOverlay, "archive folders and extraction overlay preference roundtrip");
            Check(MainWindow.OwnsArchiveAutomation(RuntimeRole.Standard)
                && MainWindow.OwnsArchiveAutomation(RuntimeRole.UiHost)
                && !MainWindow.OwnsArchiveAutomation(RuntimeRole.ElevatedHelper),
                "only the medium UI process owns archive watching so the elevated helper cannot create a duplicate destination");
            Check(loaded.AutoSave, "auto-save setting roundtrip");
            Check(loaded.KeyboardLayout == "US", "keyboard layout setting roundtrip");
            Check(loaded.SpaceHoldRepeatEnabled && loaded.SpaceHoldRepeatDelayMs == 450, "Space hold repeat setting roundtrip");
            Check(loaded.InputDisabledApplications.SequenceEqual(["RobloxPlayerBeta.exe", "game.exe"])
                && MainWindow.IsInputProcessingDisabledForApplication(loaded.InputDisabledApplications, "RobloxPlayerBeta")
                && MainWindow.IsInputProcessingDisabledForApplication(loaded.InputDisabledApplications, @"C:\Games\GAME.EXE")
                && !MainWindow.IsInputProcessingDisabledForApplication(loaded.InputDisabledApplications, "notepad"),
                "foreground application input-disable list roundtrip and exact process matching");
            Check(loaded.Gestures.Any(x => x.Name == "ウィンドウ操作" && x.GestureThresholdPixels == 18 && !x.LockCursorDuringGesture && x.UpValue == "Win+Up" && x.CenterValue == "Enter") && loaded.Profiles[0].Mappings.Any(x => x.Kind == ActionKind.Gesture && x.Value == "ウィンドウ操作"), "gesture definitions, references, center action, sensitivity, and per-gesture cursor behavior roundtrip");
            Check(loaded.ClockBackgroundMode == ClockBackgroundMode.Image && loaded.ClockDisplayMode == ClockDisplayMode.FullDateAndTime && loaded.ClockBackgroundImage == @"C:\Wallpapers\clock.jpg" && loaded.ClockSolidColor == "#123456" && !loaded.ClockShowOnAllMonitors, "clock overlay background, solid color, date format, image, and monitor scope roundtrip");
            Check(loaded.InputPanelOpacityPercent == 67 && loaded.DeckChromeOpacityPercent == 23, "input-panel and Deck chrome opacity settings roundtrip independently");
            var legacyDeckOpacityService = new ConfigService(Path.Combine(dir, "deck-opacity-migration"));
            Directory.CreateDirectory(legacyDeckOpacityService.DirectoryPath);
            File.WriteAllText(legacyDeckOpacityService.FilePath, """{"Version":37,"InputPanelOpacityPercent":17}""");
            var migratedDeckOpacity = legacyDeckOpacityService.Load();
            Check(migratedDeckOpacity.Version == ConfigService.CurrentVersion
                && migratedDeckOpacity.InputPanelOpacityPercent == 40
                && migratedDeckOpacity.DeckChromeOpacityPercent == 17,
                "the former shared opacity migrates its exact pre-clamp value to Deck chrome while the keypad keeps its independent 40-percent minimum");
            Check(loaded.Version == ConfigService.CurrentVersion && !loaded.UseSharedDeckPanel && loaded.DeckLayouts.Count == 1 && DeckPanelLayout.DefaultLayout(loaded)?.Id == loaded.DefaultDeckLayoutId && loaded.DeckPanelLeft == 123.5 && loaded.DeckPanelTop == 234.5 && loaded.DeckPanelCollapsedLeft == 246.5 && loaded.DeckPanelCollapsedTop == 357.5 && loaded.DeckPanelWidth == 987.5 && loaded.DeckPanelHeight == 543.5 && loaded.NumpadPanelLeft == 345.5 && loaded.NumpadPanelTop == 456.5 && loaded.ExtendedKeypadPanelLeft == 567.5 && loaded.ExtendedKeypadPanelTop == 678.5 && loaded.DeckAfterActionBehavior == DeckAutoDismissBehavior.Hide && loaded.DeckPointerLeaveBehavior == DeckAutoDismissBehavior.StayVisible && loaded.DeckLayouts[0] is { PanelPinned: true, PanelLeft: 111.5, PanelTop: 222.5, PanelCollapsedLeft: 333.5, PanelCollapsedTop: 444.5, PanelPadding: 18, PanelCornerRadius: 9, HoverAnimationEnabled: false }, "global fallback and per-Deck expanded/collapsed positions, size, pin, appearance, display behavior, and keypad positions roundtrip independently");
            Check(ScreenOverlayWindow.ParseClockColor("#123456") == System.Windows.Media.Color.FromRgb(0x12, 0x34, 0x56) && ScreenOverlayWindow.ParseClockColor("invalid") == System.Windows.Media.Color.FromRgb(16, 31, 46), "clock solid colors accept hex values and safely fall back from invalid input");
            var gestureMigrationService = new ConfigService(Path.Combine(dir, "gesture-threshold-migration"));
            gestureMigrationService.Save(new AppConfig { Version = 21, GestureThresholdPixels = 24 });
            var migratedGestureConfig = gestureMigrationService.Load();
            Check(migratedGestureConfig.Version == ConfigService.CurrentVersion && migratedGestureConfig.GestureThresholdPixels == 12, "the former 24-pixel gesture default migrates to a more forgiving 12-pixel movement threshold");
            var cursorBehaviorMigrationService = new ConfigService(Path.Combine(dir, "gesture-cursor-migration"));
            Directory.CreateDirectory(cursorBehaviorMigrationService.DirectoryPath);
            File.WriteAllText(cursorBehaviorMigrationService.FilePath, """{"Version":35,"LockCursorDuringGesture":false,"Gestures":[{"Name":"移行ジェスチャー"}],"Profiles":[{"Name":"標準","Mappings":[]}]}""");
            var migratedCursorBehavior = cursorBehaviorMigrationService.Load();
            Check(migratedCursorBehavior.Version == ConfigService.CurrentVersion && migratedCursorBehavior.Gestures is [{ LockCursorDuringGesture: false }], "the former global cursor-lock option migrates to every existing gesture without changing behavior");
            var sensitivityMigrationService = new ConfigService(Path.Combine(dir, "gesture-sensitivity-migration"));
            Directory.CreateDirectory(sensitivityMigrationService.DirectoryPath);
            File.WriteAllText(sensitivityMigrationService.FilePath, """{"Version":36,"GestureThresholdPixels":27,"Gestures":[{"Name":"感度A"},{"Name":"感度B"}],"Profiles":[{"Name":"標準","Mappings":[]}]}""");
            var migratedSensitivity = sensitivityMigrationService.Load();
            Check(migratedSensitivity.Version == ConfigService.CurrentVersion && migratedSensitivity.Gestures.All(gesture => gesture.GestureThresholdPixels == 27), "the former global gesture sensitivity migrates to every existing gesture without changing behavior");
            var deckBehaviorMigrationService = new ConfigService(Path.Combine(dir, "deck-behavior-migration"));
            Directory.CreateDirectory(deckBehaviorMigrationService.DirectoryPath);
            File.WriteAllText(deckBehaviorMigrationService.FilePath, """{"Version":31,"DeckAutoHideAfterAction":false,"DeckAutoHideOnPointerLeave":true,"Profiles":[{"Name":"標準","Mappings":[]}]}""");
            var migratedDeckBehavior = deckBehaviorMigrationService.Load();
            Check(migratedDeckBehavior.Version == ConfigService.CurrentVersion
                && migratedDeckBehavior.DeckAfterActionBehavior == DeckAutoDismissBehavior.StayVisible
                && migratedDeckBehavior.DeckPointerLeaveBehavior == DeckAutoDismissBehavior.CollapseToEdge,
                "legacy Deck auto-hide switches migrate without changing existing user behavior");
            Check(loaded.Profiles[1].AutoSwitchEnabled && loaded.Profiles[1].AutoSwitchApplications.Contains("notepad.exe"), "profile application auto-switch roundtrip");
            const string releaseJson = """{"tag_name":"v0.1.69","draft":false,"prerelease":false,"body":"- Deckを改善\n- 操作性を向上","assets":[{"name":"RELYR-Update-0.1.69.exe","state":"uploaded","browser_download_url":"https://github.com/zitan-source/RELYR/releases/download/v0.1.69/RELYR-Update-0.1.69.exe","digest":"sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},{"name":"RELYR-Update-0.1.69.exe.sha256","state":"uploaded","browser_download_url":"https://github.com/zitan-source/RELYR/releases/download/v0.1.69/RELYR-Update-0.1.69.exe.sha256"},{"name":"RELYR-Setup-0.1.69.exe","state":"uploaded","browser_download_url":"https://github.com/zitan-source/RELYR/releases/download/v0.1.69/RELYR-Setup-0.1.69.exe"}]}""";
            var availableUpdate = UpdateService.ParseLatestRelease(releaseJson, new Version(0, 1, 68));
            var latestVersion = UpdateService.ParseLatestVersion(releaseJson);
            Check(availableUpdate is { VersionText: "0.1.69" } && availableUpdate.InstallerFileName == "RELYR-Update-0.1.69.exe" && availableUpdate.ReleaseNotes == "- Deckを改善\n- 操作性を向上" && UpdateService.ParseLatestRelease(releaseJson, new Version(0, 1, 69)) == null && latestVersion.Version == new Version(0, 1, 69) && latestVersion.VersionText == "0.1.69", "GitHub release parser accepts only a newer trusted update installer, preserves its release notes, and ignores the full setup asset");
            const string localizedReleaseNotes = """
                ## English
                <!-- RELYR-RELEASE-NOTES:en-US -->
                - Improved Deck usability
                - Added safer update checks
                <!-- /RELYR-RELEASE-NOTES -->

                ## 日本語
                <!-- RELYR-RELEASE-NOTES:ja-JP -->
                - Deckの操作性を改善
                - 更新確認の安全性を向上
                <!-- /RELYR-RELEASE-NOTES -->
                """;
            Check(ReleaseNotesLocalization.Select(localizedReleaseNotes, "ja-JP") == "- Deckの操作性を改善\n- 更新確認の安全性を向上"
                && ReleaseNotesLocalization.Select(localizedReleaseNotes, "en-US") == "- Improved Deck usability\n- Added safer update checks"
                && ReleaseNotesLocalization.Select(localizedReleaseNotes, "de-DE") == "- Improved Deck usability\n- Added safer update checks"
                && ReleaseNotesLocalization.ParseSections(localizedReleaseNotes).Count == 2,
                "localized release notes select Japanese only for Japanese and English for every overseas language");
            Check(ReleaseNotesLocalization.Select("- 日本語だけの古い更新内容", "ja-JP") == "- 日本語だけの古い更新内容"
                && ReleaseNotesLocalization.Select("- 日本語だけの古い更新内容", "fr-FR") == ReleaseNotesLocalization.EnglishUnavailable
                && ReleaseNotesLocalization.Select("- English-only legacy notes", "es-ES") == "- English-only legacy notes",
                "legacy release notes never expose Japanese-only content to overseas users and retain usable English content");
            Check(MainWindow.ShouldShowPendingUpdateNotes(new AppConfig { PendingUpdateNotesVersion = "0.1.69", LastShownUpdateNotesVersion = "0.1.68" }, "0.1.69")
                && !MainWindow.ShouldShowPendingUpdateNotes(new AppConfig { PendingUpdateNotesVersion = "0.1.69", LastShownUpdateNotesVersion = "0.1.69" }, "0.1.69")
                && !MainWindow.ShouldShowPendingUpdateNotes(new AppConfig { PendingUpdateNotesVersion = "0.1.68" }, "0.1.69"),
                "release notes appear exactly once and only after the matching installed update starts");
            string friendlyUpdateError = UpdateService.FriendlyError(new System.Net.Http.HttpRequestException("secret raw details"));
            Check(friendlyUpdateError.Contains("接続") && !friendlyUpdateError.Contains("secret raw details"), "update errors are translated into a beginner-friendly message without raw exception details");
            var updateNow = new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
            Check(MainWindow.IsAutomaticUpdateCheckDue(updateNow, default, 0) && !MainWindow.IsAutomaticUpdateCheckDue(updateNow, updateNow.AddHours(-23), 0) && MainWindow.IsAutomaticUpdateCheckDue(updateNow, updateNow.AddHours(-24), 0), "automatic update checks run on first display and then at most once per day");
            Check(!MainWindow.IsAutomaticUpdateCheckDue(updateNow, default, updateNow.AddHours(-23).UtcTicks) && MainWindow.IsAutomaticUpdateCheckDue(updateNow, default, updateNow.AddHours(-25).UtcTicks), "persisted successful update checks also enforce the one-day interval");
            Check(loaded.Macros.Any(x => x.Name == "テストマクロ" && x.Steps.Count == 4 && x.Steps.Last().RecordedActionKind == ActionKind.Shortcut && x.Steps.Last().RecordedActionValue == "Win+Left" && !string.IsNullOrWhiteSpace(x.Id)), "macro JSON roundtrip with stable identity and mapped action");
            string macroArguments = ShortcutService.BuildMacroArguments("日本語 マクロ");
            var macroArgumentParts = macroArguments.Split(' ', 2);
            Check(ShortcutService.TryReadMacroName(macroArgumentParts, out string shortcutMacro) && shortcutMacro == "日本語 マクロ", "legacy macro shortcut arguments preserve the exact macro name");
            string shortcutDirectory = Path.Combine(dir, "shortcuts");
            var shortcutDefinition = new MacroDefinition { Name = "日本語 マクロ" };
            string shortcutPath = ShortcutService.CreateMacroShortcut(shortcutDefinition, shortcutDirectory, Environment.ProcessPath);
            var idArgumentParts = ShortcutService.BuildMacroIdArguments(shortcutDefinition.Id).Split(' ', 2);
            Check(File.Exists(shortcutPath) && ShortcutService.TryReadMacroId(idArgumentParts, out string shortcutMacroId) && shortcutMacroId == shortcutDefinition.Id, "macro desktop shortcut uses stable identity");
            string? shortcutIcon = ShortcutService.ResolveShortcutIconLocation(shortcutPath);
            Check(File.Exists(Path.Combine(AppContext.BaseDirectory, "RELYR-Macro.ico")) && shortcutIcon?.EndsWith("RELYR-Macro.ico,0", StringComparison.OrdinalIgnoreCase) == true, "macro desktop shortcut uses the distinct packaged macro icon");
            string oldShortcut = ShortcutService.CreateMacroShortcut("変更前", shortcutDirectory, Environment.ProcessPath);
            shortcutDefinition.Name = "変更後";
            string? renamedShortcut = ShortcutService.MigrateRenamedMacroShortcut("変更前", shortcutDefinition, shortcutDirectory, Environment.ProcessPath);
            Check(!File.Exists(oldShortcut) && renamedShortcut != null && File.Exists(renamedShortcut), "renaming a macro migrates its existing desktop shortcut");
            var legacyShortcutMacro = new MacroDefinition { Name = "旧ショートカット" };
            string legacyShortcutPath = Path.Combine(shortcutDirectory, "旧ショートカット - Input Customizer.lnk");
            File.WriteAllText(legacyShortcutPath, "");
            string? upgradedLegacyShortcut = ShortcutService.UpgradeExistingMacroShortcut(legacyShortcutMacro, shortcutDirectory, Environment.ProcessPath);
            Check(!File.Exists(legacyShortcutPath) && upgradedLegacyShortcut != null && File.Exists(upgradedLegacyShortcut) && Path.GetFileName(upgradedLegacyShortcut).EndsWith(" - RELYR.lnk"), "legacy product-name macro shortcuts are replaced by RELYR shortcuts");
            string archiveTests = Path.Combine(dir, "archives");
            Directory.CreateDirectory(archiveTests);
            string archiveSource = Path.Combine(archiveTests, "source");
            Directory.CreateDirectory(archiveSource);
            File.WriteAllText(Path.Combine(archiveSource, "hello.txt"), "RELYR archive test");
            string zipPath = Path.Combine(archiveTests, "sample.zip");
            System.IO.Compression.ZipFile.CreateFromDirectory(archiveSource, zipPath);
            string zipOutput = ArchiveWatcher.ExtractArchive(zipPath);
            Check(File.ReadAllText(Path.Combine(zipOutput, "hello.txt")) == "RELYR archive test", "ZIP extraction works with real archive");
            System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
            string legacyZipPath = Path.Combine(archiveTests, "legacy-japanese.zip");
            using (var legacyZipFile = File.Create(legacyZipPath))
            using (var legacyZip = new System.IO.Compression.ZipArchive(legacyZipFile, System.IO.Compression.ZipArchiveMode.Create, false, System.Text.Encoding.GetEncoding(932)))
            using (var legacyWriter = new StreamWriter(legacyZip.CreateEntry("日本語ファイル.txt").Open()))
                legacyWriter.Write("legacy filename");
            string legacyZipOutput = ArchiveWatcher.ExtractArchive(legacyZipPath);
            Check(File.ReadAllText(Path.Combine(legacyZipOutput, "日本語ファイル.txt")) == "legacy filename", "ZIP filenames stored with Japanese Windows CP932 encoding are extracted without garbling");
            string tarGzPath = Path.Combine(archiveTests, "sample-tar.tar.gz");
            using (var compressedOutput = File.Create(tarGzPath))
            using (var gzip = new System.IO.Compression.GZipStream(compressedOutput, System.IO.Compression.CompressionLevel.SmallestSize))
            using (var tar = new System.Formats.Tar.TarWriter(gzip, leaveOpen: false))
                tar.WriteEntry(Path.Combine(archiveSource, "hello.txt"), "folder/hello.txt");
            string tarOutput = ArchiveWatcher.ExtractArchive(tarGzPath);
            Check(File.ReadAllText(Path.Combine(tarOutput, "folder", "hello.txt")) == "RELYR archive test", "TAR.GZ extraction works with real archive");
            string sevenPath = Path.Combine(archiveTests, "sample.7z");
            using (var seven = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("tar.exe", $"-a -cf \"{sevenPath}\" -C \"{archiveSource}\" .") { UseShellExecute = false, CreateNoWindow = true }))
                seven?.WaitForExit(10000);
            bool sevenOk = File.Exists(sevenPath);
            if (sevenOk)
            {
                string sevenOutput = ArchiveWatcher.ExtractArchive(sevenPath);
                sevenOk = File.Exists(Path.Combine(sevenOutput, "hello.txt"));
            }
            Check(sevenOk, "7Z extraction works with real archive");
            string unsafeZip = Path.Combine(archiveTests, "unsafe.zip");
            using (var file = File.Create(unsafeZip))
            using (var zip = new System.IO.Compression.ZipArchive(file, System.IO.Compression.ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("../outside.txt").Open()))
                writer.Write("blocked");
            bool traversalBlocked = false;
            try
            {
                ArchiveWatcher.ExtractArchive(unsafeZip);
            }
            catch (InvalidDataException) { traversalBlocked = true; }
            Check(traversalBlocked && !File.Exists(Path.Combine(archiveTests, "outside.txt")), "archive path traversal is blocked");
            string watchedDesktop = Path.Combine(archiveTests, "desktop"), watchedOutput = Path.Combine(archiveTests, "output");
            Directory.CreateDirectory(watchedDesktop);
            Directory.CreateDirectory(watchedOutput);
            using (var extracted = new ManualResetEventSlim())
            using (var duplicateExtraction = new ManualResetEventSlim())
            using (var watcher = new ArchiveWatcher(watchedDesktop))
            {
                int extractionCount = 0;
                var archiveActivities = new System.Collections.Concurrent.ConcurrentQueue<ArchiveActivityState>();
                watcher.ActivityChanged += activity => archiveActivities.Enqueue(activity.State);
                watcher.Status += message =>
                {
                    if (!message.StartsWith("自動解凍しました"))
                        return;
                    if (Interlocked.Increment(ref extractionCount) == 1)
                        extracted.Set();
                    else
                        duplicateExtraction.Set();
                };
                watcher.Apply(new AppConfig { AutoExtractDesktopArchives = true, ArchiveDestinationFolder = watchedOutput });
                string watchedZip = Path.Combine(watchedDesktop, "watched.zip");
                System.IO.Compression.ZipFile.CreateFromDirectory(archiveSource, watchedZip);
                bool watcherDone = extracted.Wait(8000);
                Check(watcherDone && File.Exists(Path.Combine(watchedOutput, "watched", "hello.txt"))
                    && archiveActivities.ToArray().SequenceEqual([ArchiveActivityState.Extracting, ArchiveActivityState.Completed]),
                    "custom watch folder extracts once and reports a truthful start/completion lifecycle for the compact progress overlay");
                watcher.StartExtractForTest(watchedZip, watchedOutput);
                bool extractedTwice = duplicateExtraction.Wait(1800);
                Check(!extractedTwice && extractionCount == 1 && !Directory.Exists(Path.Combine(watchedOutput, "watched (2)")), "duplicate Created and Renamed notifications never extract the same archive twice");
            }
            service.Save(loaded);
            Check(Directory.GetFiles(dir, "*.bak.json").Length >= 1, "automatic backup");
            for (int i = 0; i < 25; i++)
            {
                loaded.DoubleClickMs = 300 + i;
                service.Save(loaded);
            }
            Check(Directory.GetFiles(dir, "*.bak.json").Length == 20, "20 generation backup retention");
            Check(!File.Exists(service.FilePath + ".tmp"), "atomic save cleanup");
            string exported = Path.Combine(dir, SettingsWindow.ExportFileName);
            service.Export(loaded, exported);
            var imported = service.Import(exported);
            string legacyExport = Path.Combine(dir, "legacy-settings.json");
            service.Export(loaded, legacyExport);
            var importedLegacy = service.Import(legacyExport);
            Check(Path.GetExtension(exported) == ".relyr" && SettingsWindow.ExportFileFilter.Contains("*.relyr") && SettingsWindow.ImportFileFilter.Contains("*.json") && imported.Profiles.Count == loaded.Profiles.Count && imported.Macros.Count == loaded.Macros.Count && importedLegacy.Profiles.Count == loaded.Profiles.Count, "dedicated RELYR settings file export and legacy JSON import");
            string resetDir = Path.Combine(dir, "reset");
            var resetService = new ConfigService(resetDir);
            resetService.Save(new AppConfig { ActiveProfile = "カスタム", StartWithWindows = true, CapsLockLayerEnabled = true, Macros = [new MacroDefinition()], Profiles = [new Profile { Name = "カスタム", Mappings = [new Mapping { Input = "A", Kind = ActionKind.Key, Value = "B" }] }] });
            var reset = resetService.ResetToDefaults();
            Check(reset.FirstRunCompleted && !reset.StartWithWindows && !reset.CapsLockLayerEnabled && reset.Macros.Count == 0 && reset.Profiles.Count == 1 && reset.Profiles[0].Name == "標準" && reset.Profiles[0].Mappings.Count == 0, "all-settings reset returns every profile, layer assignment, macro and app option to defaults");
            var missingMacro = service.Clone(loaded);
            missingMacro.Profiles[0].Mappings.Add(new Mapping { Input = "Q", Kind = ActionKind.Macro, Value = "存在しないマクロ" });
            Check(ConfigValidator.Validate(missingMacro).Any(x => x.Contains("マクロ「存在しないマクロ」")), "missing macro validation");
            var missingProfile = service.Clone(loaded);
            missingProfile.Profiles[0].Mappings.Add(new Mapping { Input = "W", Kind = ActionKind.Profile, Value = "存在しないプロファイル" });
            Check(ConfigValidator.Validate(missingProfile).Any(x => x.Contains("プロファイル「存在しないプロファイル」")), "missing profile validation");
            loaded.Profiles[0].Mappings.Add(new Mapping { Input = "A", Kind = ActionKind.Key, Value = "B" });
            loaded.Profiles[0].Mappings.Add(new Mapping { Input = "A", Kind = ActionKind.Key, Value = "C" });
            Check(ConfigValidator.Validate(loaded).Any(x => x.Contains("競合")), "conflict detection");
            Check(InputEngineSmokeTest(), "input engine construct/dispose");
            using (var excludedApplicationEngine = new InputEngine { Enabled = true })
            {
                bool excludedApplicationActive = false;
                var excludedApplicationActions = new List<string>();
                excludedApplicationEngine.HasMapping = input => input is "A" or "Space+*" or "Space+K";
                excludedApplicationEngine.InputReceived = input =>
                {
                    excludedApplicationActions.Add(input);
                    return input is "A" or "Space+K";
                };
                excludedApplicationEngine.ShouldInterceptInput = () => excludedApplicationEngine.HasCapturedPhysicalInput || !excludedApplicationActive;
                excludedApplicationEngine.ShouldInterceptMouseInput = excludedApplicationEngine.ShouldInterceptInput;
                IntPtr mappedDown = excludedApplicationEngine.DirectKeyForTest(0x41, false);
                excludedApplicationActive = true;
                IntPtr matchingUp = excludedApplicationEngine.DirectKeyForTest(0x41, true);
                IntPtr excludedKey = excludedApplicationEngine.DirectKeyForTest(0x42, false);
                IntPtr excludedMouse = excludedApplicationEngine.DirectMouseForTest(0x201);
                Check(mappedDown == (IntPtr)1 && matchingUp == (IntPtr)1
                    && excludedKey != (IntPtr)1 && excludedMouse != (IntPtr)1
                    && !excludedApplicationEngine.HasCapturedStateForTest(),
                    "an input-disabled foreground app finishes only an already captured press, then passes every keyboard and mouse input through");

                excludedApplicationActive = false;
                excludedApplicationActions.Clear();
                IntPtr oneSidedOemDown = excludedApplicationEngine.DirectKeyForTest(0xF2, false);
                bool oneSidedOemIsNotOwned = !excludedApplicationEngine.HasCapturedPhysicalInput;
                excludedApplicationActive = true;
                IntPtr[] excludedLayerInput =
                [
                    excludedApplicationEngine.DirectKeyForTest(0x20, false),
                    excludedApplicationEngine.DirectKeyForTest(0x4B, false),
                    excludedApplicationEngine.DirectKeyForTest(0x4B, true),
                    excludedApplicationEngine.DirectKeyForTest(0x20, true)
                ];
                bool disabledApplicationDidNotRunLayer = excludedApplicationActions.Count == 0;
                excludedApplicationActive = false;
                IntPtr[] restoredLayerInput =
                [
                    excludedApplicationEngine.DirectKeyForTest(0x20, false),
                    excludedApplicationEngine.DirectKeyForTest(0x4B, false),
                    excludedApplicationEngine.DirectKeyForTest(0x4B, true),
                    excludedApplicationEngine.DirectKeyForTest(0x20, true)
                ];
                IntPtr oneSidedOemCleanup = excludedApplicationEngine.DirectKeyForTest(0xF2, true);
                Check(oneSidedOemDown != (IntPtr)1 && oneSidedOemIsNotOwned
                    && excludedLayerInput.All(result => result != (IntPtr)1)
                    && disabledApplicationDidNotRunLayer
                    && restoredLayerInput.All(result => result == (IntPtr)1)
                    && excludedApplicationActions.SequenceEqual(["Space+K"])
                    && oneSidedOemCleanup != (IntPtr)1
                    && !excludedApplicationEngine.HasCapturedStateForTest(),
                    "a missing Up from an unassigned OEM/IME key cannot bypass an input-disabled app, and leaving that app restores the original Space layer");
            }
            using (var perGestureSettingsEngine = new InputEngine { Enabled = true, GestureCursorForTest = (100, 100) })
            {
                var gestureEvents = new List<string>();
                var thresholds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["G"] = 8, ["H"] = 30 };
                var cursorLocks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { ["G"] = true, ["H"] = false };
                var forwarded = (IntPtr)0x4711;
                perGestureSettingsEngine.HasMapping = input => input is "G" or "H";
                perGestureSettingsEngine.IsGesturePress = input => input is "G" or "H";
                perGestureSettingsEngine.GestureThresholdForInput = input => thresholds[input];
                perGestureSettingsEngine.GestureLocksCursor = input => cursorLocks[input];
                perGestureSettingsEngine.InputReceived = input => { gestureEvents.Add(input); return true; };
                perGestureSettingsEngine.NextHookForTest = (_, _, _) => forwarded;
                perGestureSettingsEngine.EnableDirectTestInput();

                perGestureSettingsEngine.DirectKeyForTest(0x47, false);
                thresholds["G"] = 100;
                cursorLocks["G"] = false;
                IntPtr fixedMove = perGestureSettingsEngine.DirectMouseForTest(0x200, 0, 112, 100);
                Thread.Sleep(140);
                perGestureSettingsEngine.DirectKeyForTest(0x47, true);

                perGestureSettingsEngine.DirectKeyForTest(0x48, false);
                thresholds["H"] = 3;
                cursorLocks["H"] = true;
                IntPtr movingMove = perGestureSettingsEngine.DirectMouseForTest(0x200, 0, 112, 100);
                Thread.Sleep(140);
                perGestureSettingsEngine.DirectKeyForTest(0x48, true);

                Check(fixedMove == (IntPtr)1 && movingMove == forwarded
                    && gestureEvents.SequenceEqual(["G:Gesture:Right", "H:Gesture:Center"])
                    && !perGestureSettingsEngine.HasCapturedStateForTest(),
                    "each gesture snapshots its own cursor behavior and movement threshold for the complete input press state");

                gestureEvents.Clear();
                perGestureSettingsEngine.ResetStateForTest();
                thresholds["G"] = 8;
                cursorLocks["G"] = true;
                perGestureSettingsEngine.DirectKeyForTest(0x47, false);
                perGestureSettingsEngine.DirectMouseForTest(0x200, 0, 112, 100);
                Thread.Sleep(140);
                perGestureSettingsEngine.DirectMouseForTest(0x200, 0, 112, 100);
                Thread.Sleep(140);
                perGestureSettingsEngine.DirectMouseForTest(0x200, 0, 112, 100);
                perGestureSettingsEngine.DirectKeyForTest(0x47, true);
                perGestureSettingsEngine.DirectKeyForTest(0x47, false);
                perGestureSettingsEngine.DirectKeyForTest(0x47, true);
                Check(gestureEvents.SequenceEqual(["G:Gesture:Right", "G:Gesture:Right", "G:Gesture:Right", "G:Gesture:Center"])
                    && !perGestureSettingsEngine.HasCapturedStateForTest(),
                    "any gesture-capable key repeats same-direction move-stop Actions while held and its first fresh tap executes Center after release");

                gestureEvents.Clear();
                perGestureSettingsEngine.ResetStateForTest();
                perGestureSettingsEngine.DirectKeyForTest(0x47, false);
                perGestureSettingsEngine.DirectMouseForTest(0x200, 0, 112, 100);
                Thread.Sleep(140);
                bool resynchronized = perGestureSettingsEngine.TryPrepareForProfileChange(_ => false);
                perGestureSettingsEngine.DirectKeyForTest(0x47, false);
                perGestureSettingsEngine.DirectKeyForTest(0x47, true);
                perGestureSettingsEngine.DirectKeyForTest(0x47, false);
                perGestureSettingsEngine.DirectKeyForTest(0x47, true);
                Check(resynchronized
                    && gestureEvents.SequenceEqual(["G:Gesture:Right", "G:Gesture:Center"])
                    && !perGestureSettingsEngine.HasCapturedStateForTest(),
                    "profile resynchronization still suppresses a repeated Down from the held gesture without consuming the following fresh tap");
            }
            var defensiveMouseReleases = new List<(uint Flag, uint Data)>();
            try
            {
                InputEngine.MouseFlagOutputForTest = (flag, data) => defensiveMouseReleases.Add((flag, data));
                InputEngine.ReleaseForProcessLifecycle();
                Check(!InputEngine.SystemHooksStartedInProcessForTest && defensiveMouseReleases.Count == 0,
                    "no-hook validation lifecycle never emits real mouse-button releases");
                InputEngine.ReleaseAllDefensively();
            }
            finally { InputEngine.MouseFlagOutputForTest = null; }
            Check(new[] { (4u, 0u), (16u, 0u), (64u, 0u), (0x100u, 1u), (0x100u, 2u) }
                .All(release => defensiveMouseReleases.Contains(release)),
                "shutdown and emergency recovery release every mouse button even when another process owned the missing Down state");
            Check(!InputEngine.HookMissedRawTransitions(10, 10) && InputEngine.HookMissedRawTransitions(11, 10), "hook recovery requires proven Raw Input transitions missing from the low-level hook");
            using (var rawInputFaultEngine = new InputEngine())
                Check(rawInputFaultEngine.RawInputFaultIsContainedForTest(), "a Raw Input decode fault is contained instead of terminating both low-level hooks");
            TestMappingActions(Check);
            Check(ConditionMatcher.Matches("notepad.exe", "NOTEPAD"), "application condition matching");
            var autoProfiles = new[] { new Profile { Name = "標準" }, new Profile { Name = "メモ帳", AutoSwitchEnabled = true, AutoSwitchApplications = ["notepad.exe"] } };
            Check(MainWindow.SelectAutomaticProfile(autoProfiles, "notepad").Name == "メモ帳" && MainWindow.SelectAutomaticProfile(autoProfiles, "explorer").Name == "標準", "foreground application profile selection");
            Check(MainWindow.SelectAutomaticProfileNameForLocation(autoProfiles, "メモ帳", "explorer", true) == "メモ帳" && MainWindow.SelectAutomaticProfileNameForLocation(autoProfiles, "メモ帳", "explorer", false) == "標準", "taskbar keeps the previously selected application profile");
            Check(MainWindow.ShouldKeepExplicitProfile("chrome.exe", "chrome", false) && MainWindow.ShouldKeepExplicitProfile("chrome.exe", "explorer", true) && !MainWindow.ShouldKeepExplicitProfile("chrome.exe", "notepad", false), "an explicit profile action remains active for the current foreground app and yields after foreground changes");
            Check(MainWindow.IsOwnProcess("RELYR", @"C:\Program Files\RELYR\RELYR.exe")
                && !MainWindow.IsOwnProcess("chrome", @"C:\Program Files\RELYR\RELYR.exe"),
                "RELYR-owned Deck and notification windows remain neutral to application profile routing");
            var editingProfileConfig = new AppConfig { ActiveProfile = "メモ帳", Profiles = [.. autoProfiles] };
            var runtimeProfileConfig = service.Clone(editingProfileConfig);
            Check(MainWindow.ApplyAutomaticProfile(editingProfileConfig, runtimeProfileConfig, "標準") && editingProfileConfig.ActiveProfile == "メモ帳" && runtimeProfileConfig.ActiveProfile == "標準", "automatic profile switching changes runtime behavior without moving the profile being edited");
            var guardedRuntimeConfig = service.Clone(editingProfileConfig);
            Check(!MainWindow.TryApplyAutomaticProfile(editingProfileConfig, guardedRuntimeConfig, "標準", () => false) && guardedRuntimeConfig.ActiveProfile == "メモ帳" && MainWindow.TryApplyAutomaticProfile(editingProfileConfig, guardedRuntimeConfig, "標準", () => true) && guardedRuntimeConfig.ActiveProfile == "標準", "automatic profile switching waits for captured input to be fully released");
            var mixedAutoProfiles = new[] { new Profile { Name = "標準" }, new Profile { Name = "Chrome" }, new Profile { Name = "Filmora", AutoSwitchEnabled = true, AutoSwitchApplications = ["Wondershare Filmora.exe"] } };
            var keepChrome = MainWindow.ResolveAutomaticProfileTarget(mixedAutoProfiles, "Chrome", "", "explorer.exe", false);
            var enterFilmora = MainWindow.ResolveAutomaticProfileTarget(mixedAutoProfiles, "Chrome", "", "Wondershare Filmora.exe", false);
            var (Target, ReturnProfile) = MainWindow.ResolveAutomaticProfileTarget(mixedAutoProfiles, "標準", "", ["QtWebEngineProcess", "Wondershare Filmora"], false);
            var leaveFilmora = MainWindow.ResolveAutomaticProfileTarget(mixedAutoProfiles, "Filmora", "Chrome", "explorer.exe", false);
            var filmoraOnOtherDesktop = MainWindow.ResolveAutomaticProfileTarget(mixedAutoProfiles, "Filmora", "", "explorer.exe", false);
            Check(keepChrome == ("Chrome", "") && enterFilmora == ("Filmora", "Chrome") && Target == "Filmora" && leaveFilmora == ("Chrome", "") && filmoraOnOtherDesktop == ("標準", ""), "automatic profile resolution recognizes a host app behind its render child, preserves a manual profile, and returns when the app is absent");
            var runtimeAutoConfig = new AppConfig { ActiveProfile = "標準", Profiles = [.. mixedAutoProfiles] };
            var editingAutoConfig = service.Clone(runtimeAutoConfig);
            string deferredReturn = "";
            bool deferredSwitch = MainWindow.TryResolveAndApplyAutomaticProfile(editingAutoConfig, runtimeAutoConfig, ["Wondershare Filmora"], false, () => false, ref deferredReturn, out _);
            string returnAfterReject = deferredReturn;
            bool appliedSwitch = MainWindow.TryResolveAndApplyAutomaticProfile(editingAutoConfig, runtimeAutoConfig, ["Wondershare Filmora"], false, () => true, ref deferredReturn, out string appliedTarget);
            var filmoraCandidateFromHost = MainWindow.ResolveAutomaticProfileTarget(mixedAutoProfiles, "標準", "", ["Wondershare Filmora"], false);
            var filmoraCandidateFromChangingChildren = MainWindow.ResolveAutomaticProfileTarget(mixedAutoProfiles, "標準", "", ["QtWebEngineProcess", "Wondershare Filmora", "Filmora Helper"], false);
            Check(!deferredSwitch && returnAfterReject == "" && appliedSwitch && appliedTarget == "Filmora" && runtimeAutoConfig.ActiveProfile == "Filmora" && deferredReturn == "標準" && filmoraCandidateFromHost.Target == filmoraCandidateFromChangingChildren.Target, $"a rejected automatic switch keeps its return state unchanged, retries after input release, and resolves changing child-process lists to the same profile (rejected={deferredSwitch}, afterReject={returnAfterReject}, applied={appliedSwitch}, target={appliedTarget}, active={runtimeAutoConfig.ActiveProfile}, return={deferredReturn}, hostCandidate={filmoraCandidateFromHost.Target}, childCandidate={filmoraCandidateFromChangingChildren.Target})");
            var inheritedLongPress = new Mapping { Input = "F6", Kind = ActionKind.None, LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+Win+Right" };
            var editorMapping = MainWindow.SelectEditorMapping([], inheritedLongPress, "F6");
            Check(editorMapping.LongPressKind == ActionKind.Shortcut && editorMapping.LongPressValue == "Ctrl+Win+Right" && !ReferenceEquals(editorMapping, inheritedLongPress), "inherited long-press mapping is visible in the editor without mutating its source");
            var baseMapping = new Mapping { Input = "CapsLock+U", Kind = ActionKind.Key, Value = "Enter" };
            var overrideMapping = new Mapping { Input = "A", Kind = ActionKind.Key, Value = "B" };
            var independentProfiles = new[] { new Profile { Name = "標準", Mappings = [baseMapping] }, new Profile { Name = "Chrome", Mappings = [overrideMapping] } };
            Check(MainWindow.FindProfileMapping(independentProfiles, "Chrome", "CapsLock+U", MainWindow.MappingInterceptsInput) == null && ReferenceEquals(MainWindow.FindProfileMapping(independentProfiles, "Chrome", "A", MainWindow.MappingInterceptsInput), overrideMapping) && ReferenceEquals(MainWindow.FindProfileMapping(independentProfiles, "標準", "CapsLock+U", MainWindow.MappingInterceptsInput), baseMapping), "profiles are independent so no-copy creation and assignment deletion never reveal standard-profile mappings");
            var universalMapping = new Mapping { Input = "F8", Kind = ActionKind.Key, Value = "A" };
            var applicationMapping = new Mapping { Input = "F8", Kind = ActionKind.Key, Value = "B", Application = "notepad.exe" };
            var profileScopedMappings = new Profile { Name = "標準", Mappings = [universalMapping, applicationMapping] };
            var reversedProfileScopedMappings = new Profile { Name = "標準", Mappings = [applicationMapping, universalMapping] };
            var lastProfileMapping = MainWindow.FindProfileMapping([profileScopedMappings], "標準", "F8", MainWindow.MappingInterceptsInput);
            var reversedLastProfileMapping = MainWindow.FindProfileMapping([reversedProfileScopedMappings], "標準", "F8", MainWindow.MappingInterceptsInput);
            Check(ReferenceEquals(lastProfileMapping, applicationMapping) && ReferenceEquals(reversedLastProfileMapping, universalMapping),
                "per-application mapping variants remain separate while editor lookup keeps deterministic last-visible precedence");
            Check(!ConditionMatcher.Matches("notepad.exe", "excel"), "application condition rejection");
            string ownProcessName = Path.GetFileNameWithoutExtension(Environment.ProcessPath ?? "RELYR");
            Check(MainWindow.MappingApplicationMatches(applicationMapping, ownProcessName), "RELYR main window does not suppress application-scoped mappings");
            Check(ConditionMatcher.IsTaskbarClass("Shell_TrayWnd") && ConditionMatcher.IsTaskbarClass("Shell_SecondaryTrayWnd"), "taskbar class detection");
            Check(ActionCatalog.Items.Any(x => x.Name == "コピー" && x.Value == "Ctrl+C"), "action catalog copy preset");
            var iconPresetIds = DeckIconCatalog.Presets.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Check(ActionCatalog.Items.All(action => iconPresetIds.Contains(DeckIconCatalog.SuggestedPresetId(action)))
                && ActionCatalog.Items.All(action => DeckIconCatalog.IsAnimatedPreset(DeckIconCatalog.AnimatedId(DeckIconCatalog.SuggestedPresetId(action)))),
                "every catalog action resolves to a paired static and animated Deck icon");
            var windowsApps = ActionCatalog.Items.Where(x => x.MajorCategory == "Windowsアプリ").ToList();
            var windowsAppCategories = ActionPickerWindow.CategoriesForMajor(ActionCatalog.Items, "Windowsアプリ");
            Check(new[] { "設定", "コントロールパネル", "ディスクの管理", "タスクマネージャー", "デバイスマネージャー" }.All(name => windowsApps.Any(x => x.Name == name)) && windowsAppCategories.FirstOrDefault() == ActionPickerWindow.AllCategories && ActionPickerWindow.ActionsForCategory(ActionCatalog.Items, "Windowsアプリ", ActionPickerWindow.AllCategories).Count == windowsApps.Count, "Windows applications are comprehensive and every major category begins with an all-actions view");
            Check(ActionCatalog.Items.Select(x => x.MajorCategory).Distinct().Count() >= 8 && ActionCatalog.Search("キャッシュ").Any(x => x.Name == "キャッシュを無視して更新"), "action catalog has major categories and searchable content");
            Check(new[] { "ImeOn", "ImeOff", "ImeToggle" }.All(value => ActionCatalog.Items.Any(x => x.Category == "IME・日本語入力" && x.Value == value)) && InputEngine.TryGetImeAction("ImeOn", out int imeOn) && imeOn == 1 && InputEngine.TryGetImeAction("ImeOff", out int imeOff) && imeOff == 0 && InputEngine.TryGetImeAction("ImeToggle", out int imeToggle) && imeToggle == 2, "IME on, off and toggle actions");
            Check(ActionCatalog.Items.Any(x => x.Category == "仮想デスクトップ"), "action catalog virtual desktop category");
            Check(Enumerable.Range(1, 8).All(n => ActionCatalog.Items.Any(x => x.Value == "Desktop" + n)), "direct desktop actions 1 through 8");
            Check(ActionCatalog.Items.Any(x => x.Name.StartsWith("ズームイン") && x.Value == "Ctrl+Add") && ActionCatalog.Items.Any(x => x.Name.StartsWith("ズームアウト") && x.Value == "Ctrl+Subtract"), "action catalog zoom presets");
            Check(ActionCatalog.Items.Any(x => x.Category == "マウス・ホイール" && x.Name == "Shift+左クリック" && x.Kind == ActionKind.Mouse && x.Value == "ShiftDrag") && ActionCatalog.Items.Any(x => x.Category == "マウス・ホイール" && x.Name == "Ctrl+左クリック" && x.Kind == ActionKind.Mouse && x.Value == "CtrlDrag") && ActionCatalog.Items.Any(x => x.Category == "マウス・ホイール" && x.Name == "Alt+左クリック" && x.Kind == ActionKind.Mouse && x.Value == "AltDrag"), "Shift, Ctrl, and Alt click actions keep drag-capable behavior under clearer names");
            Check(ActionCatalog.Items.Any(x => x.Category == "ブラウザー・タブ操作" && x.Name == "右のタブへ移動" && x.Value == "Ctrl+Tab") && ActionCatalog.Items.Any(x => x.Category == "ブラウザー・ページ操作" && x.Name == "キャッシュを無視して更新" && x.Value == "Ctrl+Shift+R"), "browser navigation and hard refresh actions");
            Check(ActionCatalog.Items.Any(x => x.Category == "エクスプローラー・移動" && x.Name == "上の階層へ" && x.Value == "Alt+Up") && ActionCatalog.Items.Any(x => x.Category == "エクスプローラー・タブ" && x.Name == "タブを閉じる" && x.Value == "Ctrl+W"), "file explorer navigation and tab actions");
            Check(ActionCatalog.Items.Any(x => x.Category == "音量・メディア" && x.Value == "VolumeMute") && ActionCatalog.Items.Any(x => x.Category == "入力・アクセシビリティ" && x.Value == "Win+."), "media and accessibility actions");
            Check(ActionCatalog.Items.Any(x => x.Value == "MoveWindowDesktopRight"), "move active window to right desktop action");
            Check(InputEngine.TryGetDirectDesktopStep("Ctrl+Win+Left", out int leftStep) && leftStep == -1 && InputEngine.TryGetDirectDesktopStep("Ctrl+Win+Right", out int rightStep) && rightStep == 1, "adjacent desktop actions use the direct API instead of injectable shortcuts");
            Check(new[] { "Left", "Right", "Up", "Down" }.All(d => ActionCatalog.Items.Any(x => x.Value == "MoveWindowMonitor" + d)), "move active window to four monitor directions");
            Check(ActionCatalog.Items.Any(x => x.Value == "SnapWindowLeft" && x.Name.Contains("左半分")) && ActionCatalog.Items.Any(x => x.Value == "SnapWindowRight" && x.Name.Contains("右半分")), "target-aware window snap actions are available for both halves");
            Check(ActionCatalog.Items.Any(x => x.Value == OverlayService.DeckPanelAction && x.Name.Contains("Deck")) && InputEngine.IsRecognizedShortcut(OverlayService.DeckPanelAction), "Deck panel overlay is available as an assignable action");
            string deckMigrationDir = Path.Combine(dir, "deck-v24-profile");
            var deckMigrationService = new ConfigService(deckMigrationDir);
            deckMigrationService.Save(new AppConfig { Version = 24, Profiles = [new Profile { Name = "標準", Mappings = [new Mapping { Input = DeckPanelLayout.InputName(1), Layer = DeckPanelLayout.Layer, Kind = ActionKind.Shortcut, Value = "Ctrl+C", Description = "コピー" }] }] });
            var deckClone = deckMigrationService.Load();
            var migratedDeck = DeckPanelLayout.DefaultLayout(deckClone);
            Check(deckClone.Version == ConfigService.CurrentVersion && DeckPanelLayout.SlotCount == 45 && DeckPanelLayout.MaximumSlotCount == 324 && migratedDeck is { Columns: 9, Rows: 5 } && DeckPanelLayout.FindMapping(migratedDeck, 1) is { Value: "Ctrl+C", Description: "コピー" }, "v24 profile Deck migrates to the standard layout without changing its 45 assignments");
            migratedDeck!.Mappings.Add(new Mapping { Input = DeckPanelLayout.InputName(300), Layer = DeckPanelLayout.Layer, Kind = ActionKind.Key, Value = "Z" });
            migratedDeck.Columns = 3;
            migratedDeck.Rows = 3;
            int hiddenCount = migratedDeck.Mappings.Count;
            migratedDeck.Columns = 18;
            migratedDeck.Rows = 18;
            Check(migratedDeck.Mappings.Count == hiddenCount && DeckPanelLayout.FindMapping(migratedDeck, 300)?.Value == "Z", "shrinking and expanding a Deck changes visibility without deleting out-of-range assignments");
            string deckColorDir = Path.Combine(dir, "deck-button-color");
            var deckColorService = new ConfigService(deckColorDir);
            var deckColorConfig = new AppConfig();
            DeckPanelLayout.DefaultLayout(deckColorConfig)!.Mappings.Add(new Mapping { Input = "Deck+01", Layer = "Deck", Kind = ActionKind.Key, Value = "A", Description = "コピー", DeckColor = "#146C94" });
            deckColorService.Save(deckColorConfig);
            var loadedDeckColor = DeckPanelLayout.FindMapping(DeckPanelLayout.DefaultLayout(deckColorService.Load()), 1);
            Check(loadedDeckColor is { DeckColor: "#146C94" } && DeckPanelLayout.TryGetButtonColor(loadedDeckColor, out var loadedColor) && loadedColor.R == 0x14 && loadedColor.G == 0x6C && loadedColor.B == 0x94, "Deck button names and custom colors persist with their assignments");
            string sharedDeckMigrationDir = Path.Combine(dir, "deck-v24-shared");
            var sharedDeckMigrationService = new ConfigService(sharedDeckMigrationDir);
            sharedDeckMigrationService.Save(new AppConfig { Version = 24, UseSharedDeckPanel = true, SharedDeckMappings = [new Mapping { Input = "Deck+01", Layer = "Deck", Kind = ActionKind.Key, Value = "Z", Description = "共通" }], Profiles = [new Profile { Name = "標準" }, new Profile { Name = "作業" }] });
            var sharedDeck = sharedDeckMigrationService.Load();
            Check(!sharedDeck.UseSharedDeckPanel && sharedDeck.DeckLayouts.Count == 1 && sharedDeck.Profiles.All(x => x.DefaultDeckLayoutId == sharedDeck.DefaultDeckLayoutId) && DeckPanelLayout.FindMapping(DeckPanelLayout.DefaultLayout(sharedDeck), 1) is { Value: "Z", Description: "共通" }, "v24 common Deck migrates once to one global layout without profile switching");
            string v25DeckDir = Path.Combine(dir, "deck-v25-profile-layouts");
            var v25DeckService = new ConfigService(v25DeckDir);
            var v25Decks = new[] { new DeckLayoutDefinition { Name = "標準 デッキ" }, new DeckLayoutDefinition { Name = "作業 デッキ" }, new DeckLayoutDefinition { Name = "動画 デッキ" } };
            v25DeckService.Save(new AppConfig { Version = 25, ActiveProfile = "作業", Profiles = [new Profile { Name = "標準", DefaultDeckLayoutId = v25Decks[0].Id }, new Profile { Name = "作業", DefaultDeckLayoutId = v25Decks[1].Id }, new Profile { Name = "動画", DefaultDeckLayoutId = v25Decks[2].Id }], DeckLayouts = [.. v25Decks], SharedDefaultDeckLayoutId = v25Decks[0].Id });
            var normalizedV25Deck = v25DeckService.Load();
            string globalDeckId = DeckPanelLayout.DefaultLayout(normalizedV25Deck)!.Id;
            normalizedV25Deck.ActiveProfile = "動画";
            Check(normalizedV25Deck.DeckLayouts is [{ Name: "標準Deck" }] && DeckPanelLayout.DefaultLayout(normalizedV25Deck)?.Id == globalDeckId && normalizedV25Deck.Profiles.All(x => x.DefaultDeckLayoutId == globalDeckId), "v25 profile-generated empty Decks collapse to one global Deck and profile changes cannot switch it");
            Check(MainWindow.TryResolveDeckLayoutSize("custom", "18", "18", out int customColumns, out int customRows) && customColumns == 18 && customRows == 18 && MainWindow.TryResolveDeckLayoutSize("custom", "１２", "１８", out int fullWidthColumns, out int fullWidthRows) && fullWidthColumns == 12 && fullWidthRows == 18 && !MainWindow.TryResolveDeckLayoutSize("custom", "19", "5", out _, out _) && !MainWindow.TryResolveDeckLayoutSize("custom", "0", "5", out _, out _), "new Deck dialog accepts half-width and full-width custom sizes from 1x1 through 18x18 only");
            var assignmentKinds = new[] { ActionKind.Key, ActionKind.Shortcut, ActionKind.Text, ActionKind.Launch, ActionKind.Macro, ActionKind.Profile, ActionKind.Gesture, ActionKind.Disabled };
            Check(assignmentKinds.All(kind => { var background = MainWindow.AssignmentColorFor(new Mapping { Kind = kind, Value = "test" }); return DeckPanelLayout.ContrastRatio(background, DeckPanelLayout.TextColorFor(background)) >= 4.5; }), "every assignment color automatically receives black or white text with accessible contrast");
            Check(MainWindow.ContainsJapaneseText("コピー") && MainWindow.ContainsJapaneseText("alpha漢字") && !MainWindow.ContainsJapaneseText("Ctrl+Shift+K"), "Japanese action content is detected for automatic text-action selection");
            var (Width, Height) = MainWindow.DeckPreviewSize(12, 1);
            var squarePreview = MainWindow.DeckPreviewSize(3, 3);
            Check(Math.Abs(Width - 190) < .01 && Math.Abs(Height - 190d / 12) < .01 && Math.Abs(squarePreview.Width - 88) < .01 && Math.Abs(squarePreview.Height - 88) < .01, "Deck thumbnails preserve each layout's grid aspect ratio");
            var explicitDeckAction = DeckPanelLayout.ActionValue(migratedDeck.Id);
            Check(DeckPanelLayout.ResolveActionLayout(deckClone, explicitDeckAction)?.Id == migratedDeck.Id && InputEngine.IsRecognizedShortcut(explicitDeckAction), "Deck actions use stable layout IDs and remain valid after a layout rename");
            var standardDeckProfile = new Profile { Name = "標準" };
            var workDeckProfile = new Profile { Name = "作業" };
            string linkedDeckGroup = Guid.NewGuid().ToString("N");
            var standardLinkedDeck = new DeckLayoutDefinition { Name = "連動Deck", ProfileSwitchEnabled = true, ProfileGroupId = linkedDeckGroup, ProfileId = standardDeckProfile.Id, Columns = 3, Rows = 3, Mappings = [new Mapping { Input = "Deck+01", Layer = "Deck", Kind = ActionKind.Key, Value = "A" }] };
            var workLinkedDeck = new DeckLayoutDefinition { Name = "連動Deck", ProfileSwitchEnabled = true, ProfileGroupId = linkedDeckGroup, ProfileId = workDeckProfile.Id, Columns = 8, Rows = 2 };
            standardDeckProfile.DefaultDeckLayoutId = standardLinkedDeck.Id;
            workDeckProfile.DefaultDeckLayoutId = workLinkedDeck.Id;
            var linkedDeckConfig = new AppConfig { ActiveProfile = "標準", Profiles = [standardDeckProfile, workDeckProfile], DeckLayouts = [standardLinkedDeck, workLinkedDeck], DefaultDeckLayoutId = standardLinkedDeck.Id };
            string linkedDeckAction = DeckPanelLayout.ActionValue(standardLinkedDeck.Id);
            bool standardDeckResolved = DeckPanelLayout.ResolveActionLayout(linkedDeckConfig, linkedDeckAction) == standardLinkedDeck && DeckPanelLayout.DefaultLayout(linkedDeckConfig) == standardLinkedDeck;
            linkedDeckConfig.ActiveProfile = "作業";
            Check(standardDeckResolved && DeckPanelLayout.ResolveActionLayout(linkedDeckConfig, linkedDeckAction) == workLinkedDeck && DeckPanelLayout.DefaultLayout(linkedDeckConfig) == workLinkedDeck && DeckPanelLayout.VisibleSlotCount(workLinkedDeck) == 16 && workLinkedDeck.Mappings.Count == 0, "profile-linked Deck actions switch to the active profile's independent blank layout and preserve different grid sizes");
            var referencedLayout = new DeckLayoutDefinition { Name = "参照中" };
            var fallbackLayout = new DeckLayoutDefinition { Name = "移行先" };
            string referencedAction = DeckPanelLayout.ActionValue(referencedLayout.Id);
            var referenceConfig = new AppConfig
            {
                DefaultDeckLayoutId = referencedLayout.Id,
                SharedDefaultDeckLayoutId = referencedLayout.Id,
                DeckLayouts = [referencedLayout, fallbackLayout],
                Profiles = [new Profile { Name = "標準", DefaultDeckLayoutId = referencedLayout.Id, Mappings = [new Mapping { Input = "A", Kind = ActionKind.Shortcut, Value = referencedAction, LongPressKind = ActionKind.Key, LongPressValue = "B" }] }],
                Macros = [new MacroDefinition { Name = "参照マクロ", Steps = [new MacroStep { RecordedActionKind = ActionKind.Shortcut, RecordedActionValue = referencedAction }] }],
                Gestures = [new GestureDefinition { Name = "参照ジェスチャー", UpKind = ActionKind.Shortcut, UpValue = referencedAction, RightKind = ActionKind.Key, RightValue = "C" }]
            };
            fallbackLayout.Mappings.Add(new Mapping { Input = "Deck+01", Layer = "Deck", LongPressKind = ActionKind.Shortcut, LongPressValue = referencedAction, Description = "残す表示" });
            var referenceSummary = MainWindow.CountDeckLayoutReferences(referenceConfig, referencedLayout);
            var removedReferences = MainWindow.RemoveDeckLayoutReferences(referenceConfig, referencedLayout, fallbackLayout);
            Check(referenceSummary == new MainWindow.DeckLayoutReferenceSummary(3, 2, 1, 1) && removedReferences == referenceSummary && referenceConfig.DefaultDeckLayoutId == fallbackLayout.Id && referenceConfig.SharedDefaultDeckLayoutId == fallbackLayout.Id && referenceConfig.Profiles.All(profile => profile.DefaultDeckLayoutId == fallbackLayout.Id) && referenceConfig.Profiles[0].Mappings.Single() is { Kind: ActionKind.None, Value: "", LongPressKind: ActionKind.Key, LongPressValue: "B" } && fallbackLayout.Mappings.Single() is { LongPressKind: ActionKind.None, LongPressValue: "", Description: "残す表示" } && referenceConfig.Macros[0].Steps.Count == 0 && referenceConfig.Gestures[0] is { UpKind: ActionKind.None, UpValue: "", RightKind: ActionKind.Key, RightValue: "C" } && MainWindow.CountDeckLayoutReferences(referenceConfig, referencedLayout).Total == 0, "deleting a referenced Deck can retarget defaults and remove only the exact key, Deck, macro, and gesture references after confirmation");
            var missingDeckReference = service.Clone(deckClone);
            missingDeckReference.Profiles[0].Mappings.Add(new Mapping { Input = "Q", Kind = ActionKind.Shortcut, Value = DeckPanelLayout.ActionValue("missing") });
            Check(ConfigValidator.Validate(missingDeckReference).Any(x => x.Contains("Deckレイアウト")), "missing Deck layout references are rejected before saving");
            bool showMainRequested = false;
            bool toggleAutoExtractRequested = false;
            InputEngine.ShowRelyrMainWindowOutputForTest = () => showMainRequested = true;
            InputEngine.ToggleAutoExtractOutputForTest = () => toggleAutoExtractRequested = true;
            InputEngine.SendShortcut(ActionCatalog.ShowRelyrMainWindowAction);
            InputEngine.SendShortcut(ActionCatalog.ToggleAutoExtractAction);
            InputEngine.ShowRelyrMainWindowOutputForTest = null;
            InputEngine.ToggleAutoExtractOutputForTest = null;
            Check(showMainRequested && toggleAutoExtractRequested
                && InputEngine.IsRecognizedShortcut(ActionCatalog.ShowRelyrMainWindowAction)
                && InputEngine.IsRecognizedShortcut(ActionCatalog.ToggleAutoExtractAction)
                && ActionCatalog.Items.Last() is { MajorCategory: "その他", Value: ActionCatalog.ToggleAutoExtractAction },
                "the Other category exposes native RELYR display and auto-extraction actions without sending physical keys");
            Check(ActionCatalog.Items.Any(x => x.Value == "ToggleMaximizeWindow" && x.Name.Contains("元のサイズ")), "target-aware toggle maximize action");
            Check(ActionCatalog.Items.Any(x => x.Category == "ウィンドウ・基本操作" && x.Name == "最小化" && x.Value == "MinimizeActiveWindow") && ActionCatalog.Items.Any(x => x.Category == "ウィンドウ・基本操作" && x.Name == "ウィンドウを閉じる" && x.Value == "CloseActiveWindow"), "target-aware minimize and close-window actions");
            InputEngine.ResetMinimizeAllToggleForTest();
            Check(InputEngine.ResolveShortcutAliasForTest("CloseActiveWindow") == "Alt+F4" && InputEngine.ResolveShortcutAliasForTest("ToggleMinimizeAllWindows") == "Win+M" && InputEngine.ResolveShortcutAliasForTest("ToggleMinimizeAllWindows") == "Shift+Win+M" && ActionCatalog.Items.Any(x => x.Value == "Win+M") && ActionCatalog.Items.Any(x => x.Value == "Shift+Win+M"), "close compatibility and minimize-all toggle actions");
            Check(InputEngine.ShortcutMatchesForTest("LeftAlt+F4", "Alt", "F4") && InputEngine.ShortcutMatchesForTest("RightAlt+F4", "Alt", "F4") && InputEngine.ShortcutMatchesForTest("LWin+Left", "Win", "Left"), "left and right modifier names match target-aware window shortcuts");
            Check(new[] { "画面キャプチャ", "編集・クリップボード", "ファイル・文書", "文書の書式", "ウィンドウ・整列", "ブラウザー・タブ操作", "エクスプローラー・ファイル操作" }.All(category => ActionCatalog.Items.Any(x => x.Category == category)), "common actions are divided into task-focused categories");
            var overlayActions = ActionCatalog.Items.Where(x => x.MajorCategory == "オーバーレイ").ToArray();
            Check(overlayActions.Select(x => x.Value).ToHashSet().SetEquals([OverlayService.NumpadAction, OverlayService.ExtendedKeypadAction, OverlayService.DeckPanelAction, OverlayService.BlankAction, OverlayService.ClockAction]), "overlay category exposes the keypad panels, Deck, blank screen, and clock");
            Check(!OverlayService.ShouldDismissFullScreenKeyboard(false, true)
                  && !OverlayService.ShouldDismissFullScreenKeyboard(true, false)
                  && OverlayService.ShouldDismissFullScreenKeyboard(true, true)
                  && !OverlayService.ShouldDismissFullScreenMouse(false, 0x201, false)
                  && !OverlayService.ShouldDismissFullScreenMouse(true, 0x202, false)
                  && OverlayService.ShouldDismissFullScreenMouse(true, 0x201, false)
                  && OverlayService.ShouldDismissFullScreenMouse(true, 0x200, true),
                "fullscreen overlays ignore their source release and close only on a fresh key, click, wheel, or movement");
            Check(OverlayService.RecoverFromFullScreenFailureForTest(), "a fullscreen overlay construction failure clears the global input-consumption flag even when no window can close");
            Check(OverlayService.RecoverFromStalledFullScreenCloseForTest(), "a stalled UI dispatcher cannot leave the fullscreen input-consumption transaction armed");
            InputEngine.CancelCoordinateCapture();
            int coordinateCallbacks = 0;
            Check(InputEngine.BeginCoordinateCapture((_, _) => coordinateCallbacks++)
                  && InputEngine.HandleCoordinateCaptureForTest(0x201, 10, 20)
                  && coordinateCallbacks == 1
                  && InputEngine.CoordinateCapturePendingForTest
                  && !InputEngine.HandleCoordinateCaptureForTest(0x201, 30, 40)
                  && !InputEngine.HandleCoordinateCaptureForTest(0x202, 30, 40)
                  && !InputEngine.CoordinateCapturePendingForTest,
                "coordinate capture fails open on the next physical Down when the captured Up was lost");
            InputEngine.CancelCoordinateCapture();
            var shownOverlays = new List<string>();
            OverlayService.ActionRequestedForTest = shownOverlays.Add;
            InputEngine.SendShortcut(OverlayService.NumpadAction);
            InputEngine.SendShortcut(OverlayService.ExtendedKeypadAction);
            OverlayService.ActionRequestedForTest = null;
            Check(shownOverlays.SequenceEqual([OverlayService.NumpadAction, OverlayService.ExtendedKeypadAction]), "overlay actions dispatch without being parsed as keyboard shortcuts");
            Check(ActionCatalog.Items.Where(x => x.Kind is ActionKind.Key or ActionKind.Shortcut).All(x => InputEngine.IsRecognizedShortcut(x.Value)), "every catalog key and shortcut action is executable");
            Check(ActionCatalog.Items.GroupBy(x => (x.Category, x.Name)).All(x => x.Count() == 1), "catalog has no duplicate action names within a category");
            var monitorAreas = new[] { new System.Drawing.Rectangle(0, 0, 1920, 1080), new System.Drawing.Rectangle(1920, 0, 1920, 1080), new System.Drawing.Rectangle(0, -1080, 1920, 1080), new System.Drawing.Rectangle(0, 1080, 1920, 1080) };
            Check(WindowMonitorService.SelectTargetIndex(monitorAreas, 0, WindowMonitorService.Direction.Right) == 1 && WindowMonitorService.SelectTargetIndex(monitorAreas, 0, WindowMonitorService.Direction.Up) == 2 && WindowMonitorService.SelectTargetIndex(monitorAreas, 0, WindowMonitorService.Direction.Down) == 3 && WindowMonitorService.SelectTargetIndex(monitorAreas, 0, WindowMonitorService.Direction.Left) == -1, "monitor direction target selection");
            var shortcutCandidates = new[]
            {
                new WindowMonitorService.WindowCandidate((IntPtr)1,"Shell_TrayWnd",true),
                new WindowMonitorService.WindowCandidate((IntPtr)2,"Windows.UI.Core.CoreWindow",true),
                new WindowMonitorService.WindowCandidate((IntPtr)3,"XamlExplorerHostIslandWindow",true),
                new WindowMonitorService.WindowCandidate((IntPtr)4,"ApplicationFrameWindow",false),
                new WindowMonitorService.WindowCandidate((IntPtr)5,"Chrome_WidgetWin_1",true)
            };
            Check(WindowMonitorService.SelectShortcutTarget(shortcutCandidates) == (IntPtr)5, "macro shortcut skips taskbar, Windows 11 shell overlays, and hidden windows");
            Check(new[] { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "Windows.UI.Composition.DesktopWindowContentBridge" }.All(WindowMonitorService.IsShellSurfaceClass)
                  && !WindowMonitorService.IsShellSurfaceClass("Chrome_WidgetWin_1")
                  && !WindowMonitorService.IsShellSurfaceClass("CabinetWClass"),
                "window actions reject desktop and taskbar shell surfaces without blocking ordinary application or Explorer folder windows");
            Check(WindowMonitorService.IsShortcutInputTargetClass("Progman")
                  && WindowMonitorService.IsShortcutInputTargetClass("WorkerW")
                  && WindowMonitorService.IsShortcutInputTargetClass("Chrome_WidgetWin_1")
                  && WindowMonitorService.IsShortcutInputTargetClass("CabinetWClass")
                  && !WindowMonitorService.IsShortcutInputTargetClass("Shell_TrayWnd")
                  && !WindowMonitorService.IsShortcutInputTargetClass("Shell_SecondaryTrayWnd")
                  && !WindowMonitorService.IsShortcutInputTargetClass("XamlExplorerHostIslandWindow"),
                "mapped keys and shortcuts can reach the Explorer desktop while taskbars and transient shell surfaces remain invalid targets");
            Check(WindowMonitorService.AreEquivalentShortcutTargetClasses("WorkerW", "Progman")
                  && WindowMonitorService.AreEquivalentShortcutTargetClasses("Progman", "WorkerW")
                  && !WindowMonitorService.AreEquivalentShortcutTargetClasses("Chrome_WidgetWin_1", "Chrome_WidgetWin_1")
                  && !WindowMonitorService.AreEquivalentShortcutTargetClasses("WorkerW", "Chrome_WidgetWin_1")
                  && !WindowMonitorService.AreEquivalentShortcutTargetClasses("", ""),
                "Explorer desktop hosts are equivalent only for keyboard input activation acknowledgement");
            Check(ForegroundWindowTracker.ShouldTrackWindow(shortcutCandidates[4], 40, 99)
                  && !ForegroundWindowTracker.ShouldTrackWindow(shortcutCandidates[0], 40, 99)
                  && !ForegroundWindowTracker.ShouldTrackWindow(shortcutCandidates[4], 99, 99),
                "foreground tracker remembers only visible non-shell windows owned by another process");
            Check(WindowMonitorService.SelectShortcutLaunchTarget(shortcutCandidates[4], (IntPtr)77, handle => handle is (IntPtr)5 or (IntPtr)77) == (IntPtr)5
                  && WindowMonitorService.SelectShortcutLaunchTarget(shortcutCandidates[0], (IntPtr)77, handle => handle == (IntPtr)77) == (IntPtr)77,
                "taskbar shortcut launch prefers a live foreground window and falls back to the remembered window for shell focus");
            string persistedWindowPath = Path.Combine(dir, "last-foreground-window.txt");
            ForegroundWindowTracker.PersistWindow(persistedWindowPath, (IntPtr)4321);
            Check(ForegroundWindowTracker.ReadPersistedWindow(persistedWindowPath) == (IntPtr)4321,
                "taskbar macro target survives the elevation boundary through the persisted fallback");
            Check(WindowMonitorService.SelectRememberedShortcutTarget((IntPtr)4444, handle => handle == (IntPtr)4444) == (IntPtr)4444
                  && WindowMonitorService.SelectRememberedShortcutTarget((IntPtr)4444, _ => false) == IntPtr.Zero,
                "taskbar macro prefers a valid window remembered by the resident process and rejects stale handles");
            Check(WindowMonitorService.SelectShortcutPreparationTarget((IntPtr)4567, handle => handle == (IntPtr)4567) == (IntPtr)4567
                  && WindowMonitorService.SelectShortcutPreparationTarget((IntPtr)4567, _ => false) == IntPtr.Zero,
                "taskbar macro reactivates only its captured valid window before replaying raw shortcuts");
            var leftSecondary = WindowMonitorService.SnapBounds(new System.Drawing.Rectangle(-1920, 40, 1920, 1040), WindowMonitorService.Direction.Left);
            var rightOddSecondary = WindowMonitorService.SnapBounds(new System.Drawing.Rectangle(1920, -200, 1919, 1080), WindowMonitorService.Direction.Right);
            Check(leftSecondary == new System.Drawing.Rectangle(-1920, 40, 960, 1040)
                  && rightOddSecondary == new System.Drawing.Rectangle(2879, -200, 960, 1080),
                "left and right snap bounds stay on negative, offset, and odd-width secondary monitors");
            string[] targeted = App.AttachMacroShortcutTarget(["--run-macro-id", "macro-1"], () => (IntPtr)4567);
            Check(App.ReadMacroShortcutTarget(targeted) == (IntPtr)4567 && targeted.Length == 4, "macro shortcut target survives elevated argument encoding");
            Check(App.AttachMacroShortcutTarget(targeted, () => throw new InvalidOperationException()).SequenceEqual(targeted), "macro shortcut target is captured only once");
            int cursorTargetCalls = 0, activeTargetCalls = 0;
            IntPtr preferredShortcutTarget = WindowMonitorService.SelectResolvedTarget(
                WindowActionTarget.WindowUnderCursor,
                (IntPtr)4567,
                handle => handle == (IntPtr)4567,
                () => { cursorTargetCalls++; return (IntPtr)11; },
                () => { activeTargetCalls++; return (IntPtr)22; });
            Check(preferredShortcutTarget == (IntPtr)4567 && cursorTargetCalls == 0 && activeTargetCalls == 0, "taskbar macro keeps its captured window even when the saved target mode is window-under-cursor");
            IntPtr normalCursorTarget = WindowMonitorService.SelectResolvedTarget(
                WindowActionTarget.WindowUnderCursor,
                null,
                _ => false,
                () => { cursorTargetCalls++; return (IntPtr)11; },
                () => { activeTargetCalls++; return (IntPtr)22; });
            Check(normalCursorTarget == (IntPtr)11 && cursorTargetCalls == 1 && activeTargetCalls == 0, "normal actions without a taskbar shortcut target still use the window under the cursor");
            int shortcutActivationCalls = 0;
            IntPtr activeShortcutTarget = (IntPtr)22;
            Check(WindowMonitorService.EnsureShortcutTargetActive(
                      (IntPtr)11,
                      () => activeShortcutTarget,
                      handle => { shortcutActivationCalls++; activeShortcutTarget = handle; return true; })
                  && activeShortcutTarget == (IntPtr)11
                  && shortcutActivationCalls == 1,
                "cursor-targeted shortcuts activate their resolved window before SendInput");
            Check(WindowMonitorService.EnsureShortcutTargetActive(
                      (IntPtr)11,
                      () => activeShortcutTarget,
                      _ => { shortcutActivationCalls++; return true; })
                  && shortcutActivationCalls == 1,
                "cursor-targeted shortcuts do not reactivate an already active target");
            int delayedActivationPolls = 0;
            Check(WindowMonitorService.EnsureShortcutTargetActive(
                      (IntPtr)44,
                      () => ++delayedActivationPolls >= 3 ? (IntPtr)44 : (IntPtr)11,
                      _ => true,
                      _ => { },
                      300),
                "cursor-targeted actions wait for an asynchronously acknowledged foreground transition");
            Check(!WindowMonitorService.EnsureShortcutTargetActive(
                      (IntPtr)33,
                      () => activeShortcutTarget,
                      _ => false),
                "cursor-targeted shortcuts fail closed when the resolved window cannot be activated");
            int desktopActivationCalls = 0;
            Check(WindowMonitorService.EnsureShortcutTargetActive(
                      (IntPtr)101,
                      () => (IntPtr)202,
                      _ => { desktopActivationCalls++; return false; },
                      targetsMatch: (expected, active) => expected == (IntPtr)101 && active == (IntPtr)202)
                  && desktopActivationCalls == 0,
                "mapped desktop keys do not fail when Explorer reports equivalent Progman and WorkerW roots");
            var hookReturnBarrier = InputEngine.CreateHookReturnBarrierForTest();
            Check(hookReturnBarrier.Barrier is { IsCompleted: false }, "desktop actions queued by a low-level callback wait for that callback to return");
            hookReturnBarrier.Complete();
            Check(hookReturnBarrier.Barrier?.Wait(1000) == true, "desktop action callback barrier is released without delaying the hook thread");
            IntPtr cursorFallbackTarget = WindowMonitorService.SelectResolvedTarget(
                WindowActionTarget.WindowUnderCursor,
                (IntPtr)9999,
                _ => false,
                () => { cursorTargetCalls++; return (IntPtr)11; },
                () => { activeTargetCalls++; return (IntPtr)22; });
            Check(cursorFallbackTarget == (IntPtr)11 && cursorTargetCalls == 2 && activeTargetCalls == 0, "invalid captured taskbar target falls back to the saved target mode");
            var desktopA = Guid.NewGuid();
            var desktopB = Guid.NewGuid();
            var desktopBytes = desktopA.ToByteArray().Concat(desktopB.ToByteArray()).ToArray();
            var parsedDesktops = VirtualDesktopService.ParseDesktopIds(desktopBytes);
            Check(parsedDesktops.SequenceEqual([desktopA, desktopB]), "virtual desktop id parsing");
            var lowResolutionBounds = MainWindow.ConstrainWindowBoundsForTest(new System.Windows.Rect(-80, -60, 1600, 1000), new System.Windows.Rect(0, 0, 1024, 720));
            Check(lowResolutionBounds == new System.Windows.Rect(8, 8, 1008, 704), "main window stays fully movable and keeps its title controls visible on a low-resolution work area");
            Check(InputEngine.KeyName(0x31) == "1" && InputEngine.KeyName(0x2C) == "PrintScreen", "visual and physical key names match");
            Check(InputEngine.KeyName(0xF3) == "半角/全角" && InputEngine.KeyName(0xE2) == "_", "JIS key name normalization");
            Check(InputEngine.KeyName(0x0D) == "Enter" && InputEngine.KeyName(0x1B) == "Esc" && InputEngine.KeyName(0x61) == "NumPad1", "special key names match visual keyboard");
            Check(InputEngine.IsValidRecordedEvent("A Down") && InputEngine.IsValidRecordedEvent("MouseLeft Up") && InputEngine.IsValidRecordedEvent("MouseMoveRelative:12,-4") && !InputEngine.IsValidRecordedEvent("Unknown Down"), "macro event validation including relative mouse movement");
            Check(InputEngine.TryResolveShiftedSymbolForTest("?", false, out ushort jisQuestion) && jisQuestion == 0xBF && InputEngine.TryResolveShiftedSymbolForTest("!", false, out ushort jisExclamation) && jisExclamation == 0x31 && InputEngine.TryResolveShiftedSymbolForTest("?", true, out ushort usQuestion) && usQuestion == 0xBF && InputEngine.TryResolveShiftedSymbolForTest("!", true, out ushort usExclamation) && usExclamation == 0x31, "shifted question and exclamation key resolution");
            Check("!\"#$%&'()=~|`{+*}<>?".All(x => InputEngine.TryResolveShiftedSymbolForTest(x.ToString(), false, out _)) && "~!@#$%^&*()_+{}|:\"<>?".All(x => InputEngine.TryResolveShiftedSymbolForTest(x.ToString(), true, out _)), "JIS and US shifted symbol tables are complete");
            var stopShortcut = new MacroStopShortcut();
            bool premature = stopShortcut.Process("F12 Down");
            stopShortcut.Reset();
            stopShortcut.Process("LeftCtrl Down");
            stopShortcut.Process("LeftShift Down");
            bool macroStop = stopShortcut.Process("F12 Down");
            Check(!premature && macroStop, "macro recording stop shortcut Ctrl+Shift+F12");
            Check(!MacroWindow.ShouldRecordEvent("A Down", false) && !MacroWindow.ShouldRecordEvent("LeftCtrl Up", false) && MacroWindow.ShouldRecordEvent("MouseLeft Down", false) && MacroWindow.ShouldRecordEvent("WheelUp Down", false) && MacroWindow.ShouldRecordEvent("MouseMove:10,20", false) && MacroWindow.ShouldRecordEvent("A Down", true), "macro keyboard recording filter preserves mouse input");
            Check(MacroWindow.DropIndexAfterRemoval(1, 3, 4) == 2 && MacroWindow.DropIndexAfterRemoval(3, 1, 4) == 1 && MacroWindow.DropIndexAfterRemoval(1, 2, 4) == 1, "macro drag insertion keeps before/after positions correct when the source row is removed");
            Check(MacroWindow.VisualKindFor(new MacroStep { Event = "A Down" }) == MacroStepVisualKind.Keyboard
                  && MacroWindow.VisualKindFor(new MacroStep { Event = "MouseLeft Down" }) == MacroStepVisualKind.Mouse
                  && MacroWindow.VisualKindFor(new MacroStep { Event = "Wait" }) == MacroStepVisualKind.Wait
                  && MacroWindow.VisualKindFor(new MacroStep { Event = "割り当て", RecordedActionKind = ActionKind.Shortcut }) == MacroStepVisualKind.Action
                  && MacroWindow.VisualKindFor(new MacroStep { Event = "割り当て", RecordedActionKind = ActionKind.Macro }) == MacroStepVisualKind.Macro
                  && MacroWindow.VisualKindFor(new MacroStep { Event = "割り当て", RecordedActionKind = ActionKind.Text }) == MacroStepVisualKind.Text,
                  "macro steps expose stable visual kinds for keyboard, mouse, wait, action, macro and text rows");
            var macroCycleConfig = new AppConfig { Macros = [new MacroDefinition { Name = "循環A", Steps = [new() { Event = "割り当て: 循環B", RecordedActionKind = ActionKind.Macro, RecordedActionValue = "循環B" }] }, new MacroDefinition { Name = "循環B", Steps = [new() { Event = "割り当て: 循環A", RecordedActionKind = ActionKind.Macro, RecordedActionValue = "循環A" }] }] };
            Check(ConfigValidator.Validate(macroCycleConfig).Any(x => x.Contains("循環")), "recursive macro cycles are rejected before saving");
            bool unknownRejected = false;
            try
            {
                InputEngine.SendShortcut("DefinitelyUnknownKey");
            }
            catch (ArgumentException) { unknownRejected = true; }
            Check(unknownRejected, "unknown shortcut key is rejected");
            Check(StartupService.BuildCommand(@"C:\Program Files\RELYR\RELYR.exe") == "\"C:\\Program Files\\RELYR\\RELYR.exe\" --tray", "startup command quoting");
            string installedShutdown = App.BuildShutdownSignalName(@"C:\Program Files\RELYR\RELYR.exe");
            string developmentShutdown = App.BuildShutdownSignalName(@"C:\Dev\RELYR.exe");
            Check(installedShutdown == App.BuildShutdownSignalName(@"c:\program files\relyr\RELYR.EXE") && installedShutdown != developmentShutdown, "shutdown signal targets only the same executable path");
            var withoutPreset = service.Clone(loaded);
            withoutPreset.Profiles.RemoveAll(x => x.Name == "既存AHK再現");
            service.Save(withoutPreset);
            Check(!service.Load().Profiles.Any(x => x.Name == "既存AHK再現"), "deleted preset stays deleted");
        }
        catch (Exception ex) { report.RecordException("exception", "FAIL exception: ", ex); }
        finally { try { Directory.Delete(dir, true); } catch { } }
        return report.Complete("SELF-TEST PASSED", "SELF-TEST FAILED: ");
    }
    static bool InputEngineSmokeTest()
    {
        using var engine = new InputEngine();
        engine.Enabled = false;
        return true;
    }
    static void TestMappingActions(Action<bool, string> check)
    {
        var recordingOutput = new FakeOutput();
        var executor = new MappingExecutor(recordingOutput);

        TestBasicMappingActions(executor, recordingOutput, check);
        TestModifierDragActions(executor, recordingOutput, check);
        TestNamedMappingActions(executor, recordingOutput, check);
        TestLongPressMappingActions(executor, recordingOutput, check);
    }

    static void TestBasicMappingActions(MappingExecutor executor, FakeOutput output, Action<bool, string> check)
    {
        check(executor.Execute(new Mapping { Kind = ActionKind.Disabled }, "A", out _) && output.Calls.Count == 0, "disabled mapping");
        executor.Execute(new Mapping { Kind = ActionKind.Key, Value = "Esc" }, "A", out _);
        check(output.Calls.Last() == "shortcut:Esc", "key replacement action");
        executor.Execute(new Mapping { Kind = ActionKind.Shortcut, Value = "Ctrl+C" }, "A", out _);
        check(output.Calls.Last() == "shortcut:Ctrl+C", "shortcut action");
        executor.Execute(new Mapping { Kind = ActionKind.Text, Value = "日本語" }, "A", out _);
        check(output.Calls.Last() == "text:日本語", "Unicode text action");
        executor.Execute(new Mapping { Kind = ActionKind.Mouse, Value = "WheelUp" }, "A", out _);
        check(output.Calls.Last() == "mouse:WheelUp", "mouse and wheel action");
    }

    static void TestModifierDragActions(MappingExecutor executor, FakeOutput output, Action<bool, string> check)
    {
        var modifierDrag = new Mapping { Kind = ActionKind.Mouse, Value = "ShiftDrag" };
        executor.Execute(modifierDrag, "Space+MouseLeft", out _);
        check(output.Calls.Last() == "mouse:ShiftDrag", "modifier drag mapping sends a modified single click when not dragged");
        executor.Execute(modifierDrag, "Space+MouseLeft:PressStart", out _);
        check(output.Calls.Last() == "mouse:ShiftDrag:Start", "modifier drag presses the modifier and left button immediately");
        executor.Execute(modifierDrag, "Space+MouseLeft:PressEnd", out _);
        check(output.Calls.Last() == "mouse:ShiftDrag:End", "modifier drag releases the left button and modifier at physical release");
        executor.Execute(modifierDrag, "Space+MouseLeft:DragStart", out _);
        check(output.Calls.Last() == "mouse:ShiftDrag:Start", "legacy drag start remains compatible");
        executor.Execute(modifierDrag, "Space+MouseLeft:DragEnd", out _);
        check(output.Calls.Last() == "mouse:ShiftDrag:End", "legacy drag end remains compatible");
        modifierDrag.Value = "AltDrag";
        executor.Execute(modifierDrag, "Space+MouseLeft:PressStart", out _);
        check(output.Calls.Last() == "mouse:AltDrag:Start", "Alt drag uses the same safe press lifecycle");
        executor.Execute(modifierDrag, "Space+MouseLeft:PressEnd", out _);
        check(output.Calls.Last() == "mouse:AltDrag:End", "Alt drag uses the same safe release lifecycle");
    }

    static void TestNamedMappingActions(MappingExecutor executor, FakeOutput output, Action<bool, string> check)
    {
        executor.Execute(new Mapping { Kind = ActionKind.Launch, Value = "sample.exe" }, "A", out _);
        check(output.Calls.Last() == "launch:sample.exe", "application launch action");
        executor.Execute(new Mapping { Kind = ActionKind.Macro, Value = "作業マクロ" }, "A", out _);
        check(output.Calls.Last() == "macro:作業マクロ", "macro action");
        executor.Execute(new Mapping { Kind = ActionKind.Profile, Value = "ゲーム" }, "A", out _);
        check(output.Calls.Last() == "profile:ゲーム", "profile switch action");
        check(MappingExecutor.TryGetRecordedAction(new Mapping { Input = "Space+Up", Layer = "Space", Kind = ActionKind.Shortcut, Value = "Win+Left" }, "Space+Up", out var recordedKind, out string recordedValue) && recordedKind == ActionKind.Shortcut && recordedValue == "Win+Left", "macro recording resolves the assigned action instead of the physical layer keys");
    }

    static void TestLongPressMappingActions(MappingExecutor executor, FakeOutput output, Action<bool, string> check)
    {
        executor.Execute(new Mapping { Kind = ActionKind.Shortcut, Value = "A", LongPressKind = ActionKind.Shortcut, LongPressValue = "B" }, "A:Long", out _);
        check(output.Calls.Last() == "shortcut:B", "long press action selection");
        output.Calls.Clear();
        executor.Execute(new Mapping { Input = "Space+G", Kind = ActionKind.Shortcut, Value = "Ctrl+V", LongPressKind = ActionKind.Shortcut, LongPressValue = "LWin+V" }, "Space+G:Long", out _);
        check(output.Calls.SequenceEqual(["neutralize:Space+G", "shortcut:LWin+V"]), "long press neutralizes its physical source before sending a Windows shortcut");
        executor.Execute(new Mapping { Kind = ActionKind.Key, Value = "B", LongPressKind = ActionKind.Launch, LongPressValue = "sample.exe" }, "A:Long", out _);
        check(output.Calls.Last() == "launch:sample.exe", "independent long press action kind");
        var longKindOnly = new Mapping { Input = "Q", Layer = "通常", Kind = ActionKind.None, LongPressKind = ActionKind.Launch, LongPressValue = "sample.exe" };
        executor.Execute(longKindOnly, "Q", out _);
        check(output.Calls.Last() == "shortcut:Q", "long-kind-only mapping preserves tap");
        executor.Execute(longKindOnly, "Q:Long", out _);
        check(output.Calls.Last() == "launch:sample.exe", "long-kind-only mapping executes hold action");
        var longOnly = new Mapping { Input = "K", Layer = "通常", Kind = ActionKind.Shortcut, Value = "", LongPressKind = ActionKind.Shortcut, LongPressValue = "Ctrl+K" };
        executor.Execute(longOnly, "K", out _);
        check(output.Calls.Last() == "shortcut:K", "long-only mapping preserves normal tap");
        executor.Execute(longOnly, "K:Long", out _);
        check(output.Calls.Last() == "shortcut:Ctrl+K", "long-only mapping executes hold action");
        check(!MainWindow.HasConfiguredLongPress(new Mapping { Kind = ActionKind.Key, Value = "Enter", LongPressKind = ActionKind.None, LongPressValue = "q" }) && MainWindow.HasConfiguredLongPress(new Mapping { LongPressKind = ActionKind.Key, LongPressValue = "q" }), "disabled stale long-press values never block normal key repeat");
    }
    sealed class FakeOutput : IInputOutput
    {
        public List<string> Calls { get; } = [];
        public void NeutralizeSourceKey(string input) => Calls.Add("neutralize:" + input);
        public void SendShortcut(string value) => Calls.Add("shortcut:" + value);
        public void SendText(string value) => Calls.Add("text:" + value);
        public void SendMouse(string value) => Calls.Add("mouse:" + value);
        public void Launch(string value) => Calls.Add("launch:" + value);
        public void RunMacro(string name) => Calls.Add("macro:" + name);
        public void SwitchProfile(string name) => Calls.Add("profile:" + name);
    }
}
