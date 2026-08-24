using System.Windows;

namespace RELYR;

// Keeps test-only accessors out of the main UI workflow. They intentionally
// delegate to the same production methods exercised by the UI.
public partial class MainWindow
{
    internal bool IsInputHookDisposedForTest => engine.IsDisposedForTest;
    internal bool IsInputEngineReadyForTest => engineStarted && engine.Enabled;
    internal bool IsEditorUiInitializedForTest => editorUiInitialized;
    internal void InitializeEditorUiForTest() => EnsureEditorUiInitialized();
    internal SettingsWindow? SettingsWindowForTest => settingsWindow;
    internal void OpenSettingsForTest() => OpenSettingsFrom(this);
    internal bool HasDestinationInputTargetForTest => destinationInputTarget != null;
    internal bool IsEditingSelectedInputForTest => editingSelectedInput;
    internal bool ShouldInterceptPhysicalInputForTest => engine.ShouldInterceptInput?.Invoke() ?? true;
    internal bool ShouldInterceptPhysicalMouseForTest => (engine.ShouldInterceptMouseInput ?? engine.ShouldInterceptInput)?.Invoke() ?? true;
    internal bool InputProcessingSuppressedForTest => Volatile.Read(ref inputProcessingSuppressedForForeground);
    internal void SetInputDisabledApplicationsForTest(IEnumerable<string> applications, string foregroundProcess)
    {
        appliedConfig.InputDisabledApplications = [.. applications];
        RefreshInputProcessingSuppression(foregroundProcess);
    }
    internal void ColorButtonsForTest() => ColorButtons();
    internal Profile CurrentProfileForTest => CurrentProfile;
    internal AppConfig ConfigForTest => config;
    internal IList<Profile> ProfilesForTest => config.Profiles;
    internal string AppliedProfileNameForTest => AppliedProfile.Name;
    internal IReadOnlyList<Mapping> AppliedMappingsForTest => AppliedProfile.Mappings;
    internal Mapping? AppliedMappingForTest(string input) => FindProfileMapping(appliedConfig.Profiles, AppliedProfile.Name, input, MappingInterceptsInput);
    internal Mapping? RuntimeMappingForTest(string input) => FindMapping(input);
    internal bool RuntimeInterceptsInputForTest(string input) => HasMapping(input);
    internal void AddAppliedMappingForTest(Mapping mapping) => AppliedProfile.Mappings.Add(mapping);
    internal void RemoveAppliedMappingForTest(Mapping mapping) => AppliedProfile.Mappings.Remove(mapping);
    internal void BeginLayerMappingScopeForTest(string layer) => CaptureLayerMappings(layer);
    internal void EndLayerMappingScopeForTest(string layer) => ReleaseLayerMappings(layer);
    internal bool ExecuteMappingForTest(Mapping mapping, string input) => executor.Execute(mapping, input, out _);
    internal void SwitchProfileForTest(string name) => SwitchProfile(name, true, false);
    internal void ApplyProfileManagerResultForTest(IReadOnlyList<Profile> profiles, string activeProfile)
        => ApplyProfileManagerResult(profiles, activeProfile);
    internal bool ApplyAutomaticProfileForTest(IReadOnlyCollection<string> processes)
        => TryApplyAutomaticProfileForProcesses(processes, false, out _);
    internal void ResetAutomaticProfileCandidateForTest() => ResetAutomaticProfileCandidate();
    internal bool ObserveAutomaticProfileCandidateForTest(string candidate, string activeProfile)
        => ObserveAutomaticProfileCandidate(candidate, AutomaticProfileRequiredSamples(appliedConfig.Profiles, candidate));
    internal bool IsProfileOverlayVisibleForTest => profileOverlay?.IsVisible == true;
    internal ProfileSwitchOverlay? ProfileOverlayForTest => profileOverlay;
    internal AppConfig DeckOverlayConfigForTest => DeckOverlayConfig();
    internal void ShowProfileOverlayForTest(string name) => ShowProfileOverlay(name);
    internal IReadOnlyList<System.Windows.Controls.Button> VisualInputButtonsForTest => VisualInputButtons().ToList();
    internal bool IsActionPaletteOpenForTest => actionPaletteOpen;
    internal bool CanUndoPaletteActionForTest => actionPaletteUndoState != null;
    internal TimeSpan ActionPaletteUndoDurationForTest => actionPaletteUndoTimer.Interval;
    internal IReadOnlyList<CatalogAction> ActionPaletteActionsForTest => actionPaletteItems.Select(item => item.Action).ToArray();
    internal IReadOnlyList<CatalogAction> VisibleActionPaletteActionsForTest => ActionPaletteList.Items.Cast<ActionPaletteItem>().Select(item => item.Action).ToArray();
    internal void SetActionPaletteApplicationsForTest(params InstalledApplicationInfo[] applications)
    {
        actionPaletteApplicationDiscoveryStarted = true;
        actionPaletteApplications = applications;
        RefreshActionPalette();
    }
    internal void AddActionPaletteShortcutForTest(string shortcut) => AddActionPaletteShortcut(shortcut);
    internal void OpenActionPaletteForTest() => OpenActionPalette_Click(ActionPaletteButton, new RoutedEventArgs());
    internal void CloseActionPaletteForTest() => CloseActionPalette(animated: false);
    internal void CloseActionPaletteAnimatedForTest() => CloseActionPalette(animated: true);
    internal bool ExerciseFrozenActionLaunchMotionForTest()
    {
        ActionPaletteButton.ApplyTemplate();
        if (ActionPaletteButton.Template.FindName("LaunchHalo", ActionPaletteButton) is not FrameworkElement halo
            || ActionPaletteButton.Template.FindName("LaunchBorder", ActionPaletteButton) is not FrameworkElement border)
            return false;
        var frozenHalo = new System.Windows.Media.ScaleTransform(.86, .86);
        var frozenBorder = new System.Windows.Media.ScaleTransform(1, 1);
        frozenHalo.Freeze();
        frozenBorder.Freeze();
        halo.RenderTransform = frozenHalo;
        border.RenderTransform = frozenBorder;
        SetActionPaletteLaunchMotion(ActionPaletteButton, true);
        return halo.RenderTransform is System.Windows.Media.ScaleTransform { IsFrozen: false, HasAnimatedProperties: true }
            && border.RenderTransform is System.Windows.Media.ScaleTransform { IsFrozen: false, HasAnimatedProperties: true };
    }
    internal void ClickActionPaletteBlankForTest()
    {
        var click = new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice,
            Environment.TickCount,
            System.Windows.Input.MouseButton.Left)
        {
            RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent
        };
        KeyboardWorkspace.RaiseEvent(click);
    }
    internal void SelectActionPalettePopupItemForTest(string category)
    {
        var popupItem = new System.Windows.Controls.ComboBoxItem { Content = category };
        var click = new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice,
            Environment.TickCount,
            System.Windows.Input.MouseButton.Left)
        {
            RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent,
            Source = popupItem
        };
        MainWindow_PreviewMouseDown(this, click);
        ActionPaletteCategoryBox.SelectedItem = category;
    }
    internal bool ApplyPaletteActionForTest(CatalogAction action, string targetInput, string targetKey)
        => ApplyPaletteActionDrop(action, targetInput, targetKey);
    internal bool ApplyPaletteMonitorForTest(string monitorId, string targetInput)
        => DeckMonitorCatalog.TryGet(monitorId, out var monitor) && ApplyPaletteMonitorDrop(monitor, targetInput);
    internal void UndoPaletteActionForTest() => UndoActionPaletteAssignment_Click(ActionPaletteButton, new RoutedEventArgs());
    internal bool HasPaletteDropMotionForTest(System.Windows.Controls.Button button)
    {
        button.ApplyTemplate();
        return InputScaleTransform(button).HasAnimatedProperties
            && button.Template.FindName("DropTargetTint", button) is UIElement { HasAnimatedProperties: true };
    }
    internal IReadOnlyList<System.Windows.Controls.Button> DeckManagementButtonsForTest => deckManagementButtons;
    internal IReadOnlyList<System.Windows.Controls.Button> DeckGridButtonsForTest => deckGridButtons;
    internal int DeckVisualUpdateCountForTest { get; private set; }
    internal void ResetDeckVisualUpdateCountForTest() => DeckVisualUpdateCountForTest = 0;
    internal DeckLayoutDefinition? SelectedDeckLayoutForTest => selectedDeckLayout;
    internal bool IsDeckEditorAudioPlayingForTest => deckEditorAudioPlayer != null;
    internal bool IsDeckEditorThumbnailOpenForTest => deckEditorThumbnailPopup?.IsOpen == true;
    internal Action<Window>? NewDeckDialogLoadedForTest { get; set; }
#if !PRODUCTION_PUBLISH
    internal Func<bool, string?, CatalogAction?>? ActionPickerRequestedForTest { get; set; }
    internal Action<MacroInputPickerWindow>? KeypadInputRequestedForTest { get; set; }
#endif
    internal WindowActionTarget DeckWindowActionTargetForTest => DeckExecutionConfig().WindowActionTarget;
    internal WindowActionTarget TaskbarWindowActionTargetForTest => TaskbarExecutionConfig().WindowActionTarget;

    internal DeckLayoutDefinition AddDeckLayoutForTest(string name, int columns, int rows)
    {
        var layout = new DeckLayoutDefinition { Name = name, Columns = columns, Rows = rows };
        config.DeckLayouts.Add(layout);
        RefreshDeckLayoutCards();
        return layout;
    }

    internal void EditDeckLayoutForTest(DeckLayoutDefinition layout) => EditDeckLayout(layout);
    internal void ClickDeckPreviewBackgroundForTest()
    {
        var click = new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice,
            Environment.TickCount,
            System.Windows.Input.MouseButton.Left)
        {
            RoutedEvent = System.Windows.Input.Mouse.PreviewMouseDownEvent
        };
        DeckPreviewSurface.RaiseEvent(click);
    }
    internal void CopyDeckAssignmentForTest(string input) => CopyDeckAssignment(input);
    internal void PasteDeckAssignmentForTest(string input) => PasteDeckAssignment(input);
    internal bool HasCopiedDeckAssignmentForTest => copiedDeckMapping != null;
    internal string[] MultiSelectedInputsForTest => [.. multiSelectedInputs];
    internal void SetAssignmentDropTargetForTest(System.Windows.Controls.Button button, bool active)
    {
        SetAssignmentDropTargetVisual(button, active);
        if (active)
            return;
        if (button.Tag is string input && DeckPanelLayout.IsInputName(input))
            UpdateDeckManagementButtonVisual(button);
        else
            UpdateInputButtonVisual(button, IsDescendantOf(button, KeyboardPanel) || IsDescendantOf(button, SecondaryKeyboardPanel));
    }
    internal void SetInputHoverForTest(System.Windows.Controls.Button button, bool active)
    {
        if (active)
            InputButtonHoverEntered(button, new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount));
        else
            InputButtonHoverExited(button, new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount));
    }
    internal AssignmentTransferResult TransferCurrentLayerAssignmentsForTest(string sourceKey, string targetKey)
    {
        var result = TransferAssignments(CurrentProfile.Mappings, InputForCurrentLayer(sourceKey), InputForCurrentLayer(targetKey));
        if (result != AssignmentTransferResult.None)
            ColorButtons();
        return result;
    }
    internal void ApplyDeckSizeForTest(int columns, int rows) => ApplyDeckSize(columns, rows);
    internal void ApplyDeckSliderSizeForTest(int columns, int rows)
        => ApplyDeckSize(columns, rows, synchronizeSliders: false, deferDeckRefresh: true);
    internal void FlushDeckCustomizationRefreshForTest() => FlushDeckCustomizationRefresh();
    internal void ShowDeckLayoutListForTest() => ShowDeckLayoutList();
    internal void ShowNewDeckDialogForTest() => PromptNewDeckLayout();

#if !PRODUCTION_PUBLISH
    internal string[] TrayMenuItemTextsForTest()
        => tray.ContextMenuStrip?.Items.OfType<System.Windows.Forms.ToolStripItem>().Select(item => item.Text ?? "").ToArray() ?? [];

    internal void ExecuteTrayExitMenuItemForTest()
    {
        if (!Dispatcher.CheckAccess())
            throw new InvalidOperationException("トレイ終了テストはUIスレッドで実行する必要があります。");

        var item = tray.ContextMenuStrip?.Items.OfType<System.Windows.Forms.ToolStripItem>()
            .SingleOrDefault(menuItem => menuItem.Text == "終了")
            ?? throw new InvalidOperationException("トレイの終了メニュー項目が見つかりません。");
        item.PerformClick();
    }

    internal bool ExecuteDeckActionForTest(Mapping mapping)
        => deckExecutor.Execute(mapping, mapping.Input, out _);
#endif

    internal void ApplyCatalogActionForTest(CatalogAction action, bool longPress = false)
        => ApplyCatalogAction(action, longPress);

    internal void ApplyApplicationSelectionForTest(bool longPress, string path)
        => ApplyApplicationSelection(longPress, path);

    internal void ApplyProfileActionForTest(string profileName, bool longPress)
        => ApplyProfileAction(profileName, longPress);

    internal static bool HasSelectionPulseAnimationForTest(System.Windows.Controls.Button button)
    {
        button.ApplyTemplate();
        return button.Template.FindName("SelectionPulse", button) is UIElement pulse && pulse.Opacity > 0;
    }

    internal void BeginInputDetectionForTest() => Detect_Click(ActionPaletteButton, new RoutedEventArgs());
    internal void FeedDetectedInputForTest(string text) => HandleDetectedInput(text);
    internal void CompleteDestinationInputForTest() => CompleteDestinationInput();
    internal void RefreshLayerButtonsForTest() => UpdateLayerButtons();
    internal void SaveAndApplyForTest() => SaveAndApply("テスト：設定を保存し、エンジンへ反映しました");
    internal void HandleOverlayDeckSlotsChangedForTest(string layoutId, int firstSlot, int secondSlot)
        => HandleOverlayDeckSlotsChanged(layoutId, firstSlot, secondSlot);
    internal bool HandleInputForTest(string input) => HandleInput(input);
    internal bool QueueModifierDragForTest(string value, bool start)
        => QueueDragAction(new Mapping { Input = "Space+MouseLeft", Layer = "Space", Kind = ActionKind.Mouse, Value = value }, "Space+MouseLeft:" + (start ? "PressStart" : "PressEnd"));
    internal IntPtr DirectPhysicalKeyForTest(ushort virtualKey, bool up) => engine.DirectKeyForTest(virtualKey, up);
    internal IntPtr DirectPhysicalMouseForTest(int message, int x = 0, int y = 0) => engine.DirectMouseForTest(message, 0, x, y);
    internal IntPtr DirectPhysicalMouseForTest(int message, int mouseData, int x, int y) => engine.DirectMouseForTest(message, mouseData, x, y);
    internal bool HasCapturedInputStateForTest => engine.HasCapturedStateForTest();
    internal bool NativeMouseDragReadyForTest(string input) => engine.IsNativeMouseDragReadyForTest(input);
    internal bool InputEngineEnabledForTest => engine.Enabled;
    internal bool TaskbarClickReplayFailedForTest => Volatile.Read(ref taskbarClickReplayFailed) != 0;
    internal void FailOpenAfterTaskbarClickReplayFailureForTest() => FailOpenAfterTaskbarClickReplayFailure();

    internal void OpenKeypadInputForTest(bool longPress = false)
        => KeypadInput_Click(longPress ? LongKindBox : KindBox, new RoutedEventArgs());

    internal void OpenDeckPanelPickerForTest(bool longPress = false)
        => OpenDeckPanelPicker(longPress ? LongKindBox : KindBox, longPress);

    internal void SetCapsLockRemapForTest(bool enabled)
    {
        capsLockRemapped = enabled;
        engine.TreatF13AsCapsLock = enabled;
    }
}
