using System.Windows;
using System.Windows.Input;

namespace RELYR;

// Keeps test-only accessors out of the main UI workflow. They intentionally
// delegate to the same production methods exercised by the UI.
public partial class MainWindow
{
    internal bool IsInputHookDisposedForTest => engine.IsDisposedForTest;
    internal bool IsInputEngineReadyForTest => engineStarted && engine.Enabled;
    internal SettingsWindow? SettingsWindowForTest => settingsWindow;
    internal void OpenSettingsForTest() => OpenSettingsFrom(this);
    internal bool HasDestinationInputTargetForTest => destinationInputTarget != null;
    internal bool IsEditingSelectedInputForTest => editingSelectedInput;
    internal bool ShouldInterceptPhysicalInputForTest => engine.ShouldInterceptInput?.Invoke() ?? true;
    internal void ColorButtonsForTest() => ColorButtons();
    internal Profile CurrentProfileForTest => CurrentProfile;
    internal AppConfig ConfigForTest => config;
    internal IList<Profile> ProfilesForTest => config.Profiles;
    internal string AppliedProfileNameForTest => AppliedProfile.Name;
    internal Mapping? AppliedMappingForTest(string input) => FindProfileMapping(appliedConfig.Profiles, AppliedProfile.Name, input, MappingInterceptsInput);
    internal Mapping? RuntimeMappingForTest(string input) => FindMapping(input);
    internal void BeginLayerMappingScopeForTest(string layer) => CaptureLayerMappings(layer);
    internal void EndLayerMappingScopeForTest(string layer) => ReleaseLayerMappings(layer);
    internal bool ExecuteMappingForTest(Mapping mapping, string input) => executor.Execute(mapping, input, out _);
    internal void SwitchProfileForTest(string name) => SwitchProfile(name, true, false);
    internal void ApplyProfileManagerResultForTest(IReadOnlyList<Profile> profiles, string activeProfile, bool autoSwitch)
        => ApplyProfileManagerResult(profiles, activeProfile, autoSwitch);
    internal bool ApplyAutomaticProfileForTest(IReadOnlyCollection<string> processes)
        => TryApplyAutomaticProfileForProcesses(processes, false, out _);
    internal bool IsProfileOverlayVisibleForTest => profileOverlay?.IsVisible == true;
    internal ProfileSwitchOverlay? ProfileOverlayForTest => profileOverlay;
    internal void ShowProfileOverlayForTest(string name) => ShowProfileOverlay(name);
    internal IReadOnlyList<System.Windows.Controls.Button> VisualInputButtonsForTest => VisualInputButtons().ToList();
    internal IReadOnlyList<System.Windows.Controls.Button> DeckManagementButtonsForTest => deckManagementButtons;
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

    internal DeckLayoutDefinition AddDeckLayoutForTest(string name, int columns, int rows)
    {
        var layout = new DeckLayoutDefinition { Name = name, Columns = columns, Rows = rows };
        config.DeckLayouts.Add(layout);
        RefreshDeckLayoutCards();
        return layout;
    }

    internal void EditDeckLayoutForTest(DeckLayoutDefinition layout) => EditDeckLayout(layout);
    internal void ApplyDeckSizeForTest(int columns, int rows) => ApplyDeckSize(columns, rows);
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

    internal void ApplyProfileActionForTest(string profileName, bool longPress)
        => ApplyProfileAction(profileName, longPress);

    internal static bool HasSelectionPulseAnimationForTest(System.Windows.Controls.Button button)
    {
        button.ApplyTemplate();
        return button.Template.FindName("SelectionPulse", button) is UIElement pulse && pulse.Opacity > 0;
    }

    internal void BeginInputDetectionForTest() => Detect_Click(DetectInputButton, new RoutedEventArgs());
    internal void FeedDetectedInputForTest(string text) => HandleDetectedInput(text);
    internal void CompleteDestinationInputForTest() => CompleteDestinationInput();
    internal void RefreshLayerButtonsForTest() => UpdateLayerButtons();
    internal void SaveAndApplyForTest() => SaveAndApply("テスト：設定を保存し、エンジンへ反映しました");

    internal bool EnterPhysicalExecutionKeyForTest(Key key, ModifierKeys modifiers, bool longPress = false)
        => ApplyPhysicalExecutionKey(longPress ? LongValueBox : ValueBox, key, modifiers);
    internal void OpenKeypadInputForTest(bool longPress = false)
        => KeypadInput_Click(longPress ? LongKindBox : KindBox, new RoutedEventArgs());

    internal void SetCapsLockRemapForTest(bool enabled)
    {
        capsLockRemapped = enabled;
        engine.TreatF13AsCapsLock = enabled;
    }
}
