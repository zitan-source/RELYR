using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DataObject = System.Windows.DataObject;
using DragDrop = System.Windows.DragDrop;
using DragDropEffects = System.Windows.DragDropEffects;
using FontFamily = System.Windows.Media.FontFamily;
using GiveFeedbackEventHandler = System.Windows.GiveFeedbackEventHandler;
using IDataObject = System.Windows.IDataObject;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Button = System.Windows.Controls.Button;
using MediaColor = System.Windows.Media.Color;

namespace RELYR;

public partial class MainWindow
{
    internal const string ActionPaletteDragFormat = "RELYR.CatalogAction.v1";
    internal const string DeckMonitorDragFormat = "RELYR.DeckMonitor.v1";
    const string DeckMonitorActionPrefix = "RELYR:DeckMonitor:";
    internal const double ActionPaletteDragPreviewScale = .82;
    internal const double ActionPaletteDragPreviewMinWidth = 172;
    internal const double ActionPaletteDragPreviewMaxWidth = 220;
    internal const double ActionPaletteDragPreviewHeight = 42;
    const string ActionPaletteAllCategory = "すべて";
    const string ActionPaletteUsedCategory = "使用中";
    const string ActionPaletteApplicationsCategory = "アプリ";
    const string ActionPaletteKeysCategory = "キー";
    const string ActionPaletteShortcutsCategory = "ショートカット";
    static readonly string[] ActionPaletteKeySequence = BuildActionPaletteKeySequence();
    static readonly IReadOnlyDictionary<string, int> ActionPaletteKeyOrder = ActionPaletteKeySequence
        .Select((key, index) => (key, index))
        .ToDictionary(entry => entry.key, entry => entry.index, StringComparer.OrdinalIgnoreCase);

    bool actionPaletteOpen;
    bool refreshingActionPalette;
    bool actionPaletteUndoTimerInitialized;
    int actionPaletteMotionGeneration;
    int actionPaletteUndoMotionGeneration;
    Point actionPaletteDragStart;
    ActionPaletteItem? actionPaletteDragItem;
    ActionPaletteUndoState? actionPaletteUndoState;
    List<ActionPaletteItem> actionPaletteItems = [];
    IReadOnlyList<InstalledApplicationInfo> actionPaletteApplications = [];
    readonly List<CatalogAction> actionPaletteCustomShortcuts = [];
    bool actionPaletteApplicationDiscoveryStarted;
    readonly System.Windows.Threading.DispatcherTimer actionPaletteUndoTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    sealed record ActionPaletteItem(CatalogAction Action, string Name, string Group, string Glyph, int UsageCount)
    {
        internal string ToolTipText => string.IsNullOrWhiteSpace(Action.Description)
            ? Name
            : $"{Name}\n{Action.Description}";
    }

    sealed record IndexedMapping(int Index, Mapping Mapping);
    sealed record ActionPaletteMappingSnapshot(List<Mapping> Collection, string Input, IReadOnlyList<IndexedMapping> Previous);
    sealed record ActionPaletteUndoState(IReadOnlyList<ActionPaletteMappingSnapshot> Snapshots, string Message);

    void ActionPaletteLaunchMotionEntered(object sender, MouseEventArgs e)
        => UiMotionService.RunSafely("action-launch-hover-enter", () => SetActionPaletteLaunchMotion(sender as Button, true));

    void ActionPaletteLaunchMotionExited(object sender, MouseEventArgs e)
        => UiMotionService.RunSafely("action-launch-hover-exit", () => SetActionPaletteLaunchMotion(sender as Button, false));

    static void SetActionPaletteLaunchMotion(Button? button, bool entered)
    {
        if (button == null)
            return;
        button.ApplyTemplate();
        if (button.Template.FindName("LaunchHalo", button) is not FrameworkElement halo
            || button.Template.FindName("LaunchBorder", button) is not FrameworkElement border)
            return;
        var haloScale = UiMotionService.MutableScale(halo, .86, .86);
        var borderScale = UiMotionService.MutableScale(border);

        if (!UiMotionService.Enabled)
        {
            ResetMotionElement(halo, haloScale, 0, .86);
            ResetMotionElement(border, borderScale, 1, 1);
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        halo.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(entered ? .52 : 0, TimeSpan.FromMilliseconds(entered ? 160 : 180)) { EasingFunction = ease },
            HandoffBehavior.SnapshotAndReplace);
        AnimateScale(haloScale, entered ? 1.1 : .86, entered ? 220 : 200, ease);
        AnimateScale(borderScale, entered ? 1.04 : 1, entered ? 160 : 180, ease);
    }

    void ActionPaletteRowMotionEntered(object sender, MouseEventArgs e)
        => UiMotionService.RunSafely("action-row-hover-enter", () => SetActionPaletteRowMotion(sender as ListBoxItem, true));

    void ActionPaletteRowMotionExited(object sender, MouseEventArgs e)
        => UiMotionService.RunSafely("action-row-hover-exit", () => SetActionPaletteRowMotion(sender as ListBoxItem, false));

    static void SetActionPaletteRowMotion(ListBoxItem? item, bool entered)
    {
        if (item == null)
            return;
        item.ApplyTemplate();
        if (item.Template.FindName("ActionRow", item) is not FrameworkElement row)
            return;
        var translate = UiMotionService.MutableTranslate(row);
        translate.BeginAnimation(TranslateTransform.XProperty, null);
        if (!UiMotionService.Enabled)
        {
            translate.X = 0;
            return;
        }
        translate.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(entered ? 2 : 0, TimeSpan.FromMilliseconds(entered ? 140 : 170))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    static void ResetMotionElement(FrameworkElement element, ScaleTransform scale, double opacity, double scaleValue)
    {
        element.BeginAnimation(UIElement.OpacityProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        element.Opacity = opacity;
        scale.ScaleX = scaleValue;
        scale.ScaleY = scaleValue;
    }

    static void AnimateScale(ScaleTransform scale, double target, int durationMs, IEasingFunction ease)
    {
        var duration = TimeSpan.FromMilliseconds(durationMs);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(target, duration) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(target, duration) { EasingFunction = ease }, HandoffBehavior.SnapshotAndReplace);
    }

    void OpenActionPalette_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (destinationInputTarget != null || editingSelectedInput)
                CompleteDestinationInput(sender as FrameworkElement);

            // Build the complete list before changing the visible pane.  A bad
            // imported/runtime value must not leave the inspector half-switched
            // or escape through WPF's dispatcher exception handler.
            RefreshActionPalette();
            StartActionPaletteApplicationDiscovery();
            ++actionPaletteMotionGeneration;
            actionPaletteOpen = true;
            UpdateAssignmentPaneContentView();
            if (ActionPalettePane != null)
            {
                var translate = UiMotionService.MutableTranslate(ActionPalettePane);
                ActionPalettePane.BeginAnimation(UIElement.OpacityProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                if (UiMotionService.Enabled)
                {
                    ActionPalettePane.Opacity = 0;
                    translate.Y = 6;
                    var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                    ActionPalettePane.BeginAnimation(UIElement.OpacityProperty,
                        new DoubleAnimation(1, TimeSpan.FromMilliseconds(175)) { EasingFunction = ease },
                        HandoffBehavior.SnapshotAndReplace);
                    translate.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(0, TimeSpan.FromMilliseconds(205)) { EasingFunction = ease },
                        HandoffBehavior.SnapshotAndReplace);
                }
                else
                {
                    ActionPalettePane.Opacity = 1;
                    translate.Y = 0;
                }
            }
            _ = Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
            {
                if (actionPaletteOpen && ActionPaletteSearchBox.IsVisible)
                    ActionPaletteSearchBox.Focus();
            }));
        }
        catch (Exception ex)
        {
            RecoverFromActionPaletteOpenFailure(ex);
        }
    }

    void RecoverFromActionPaletteOpenFailure(Exception exception)
    {
        actionPaletteOpen = false;
        actionPaletteDragItem = null;
        ++actionPaletteMotionGeneration;
        LifecycleDiagnostics.Write("action-palette-open-failed", exception.ToString());
        try { ResetActionPaletteMotion(); } catch { }
        try { UpdateAssignmentPaneContentView(); } catch { }
        try { ShowInlineNotice("Action一覧を開けませんでした。RELYRは動作を継続しています。"); } catch { }
    }

    void CloseActionPalette_Click(object sender, RoutedEventArgs e)
        => CloseActionPalette(animated: true);

    void CloseActionPalette(bool animated)
    {
        actionPaletteOpen = false;
        actionPaletteDragItem = null;
        int generation = ++actionPaletteMotionGeneration;
        if (!animated || !UiMotionService.Enabled)
        {
            ResetActionPaletteMotion();
            UpdateAssignmentPaneContentView();
            return;
        }
        var translate = UiMotionService.MutableTranslate(ActionPalettePane);

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(135)) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            if (generation != actionPaletteMotionGeneration || actionPaletteOpen)
                return;
            ResetActionPaletteMotion();
            UpdateAssignmentPaneContentView();
        };
        ActionPalettePane.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(5, TimeSpan.FromMilliseconds(145)) { EasingFunction = ease },
            HandoffBehavior.SnapshotAndReplace);
    }

    void ResetActionPaletteMotion()
    {
        ActionPalettePane.BeginAnimation(UIElement.OpacityProperty, null);
        ActionPalettePane.Opacity = 1;
        var translate = UiMotionService.MutableTranslate(ActionPalettePane);
        translate.BeginAnimation(TranslateTransform.YProperty, null);
        translate.Y = 0;
    }

    void UpdateAssignmentPaneContentView()
    {
        if (ActionPalettePane == null)
            return;
        if (actionPaletteOpen)
        {
            ActionPalettePane.Visibility = Visibility.Visible;
            AssignmentScrollViewer.Visibility = Visibility.Collapsed;
            InspectorEmptyState.Visibility = Visibility.Collapsed;
            return;
        }

        ActionPalettePane.Visibility = Visibility.Collapsed;
        AssignmentScrollViewer.Visibility = Visibility.Visible;
        InspectorEmptyState.Visibility = selected == null ? Visibility.Visible : Visibility.Collapsed;
    }

    void RefreshActionPalette()
    {
        if (!editorUiInitialized || ActionPaletteList == null)
            return;

        var profiles = config.Profiles?.Where(profile => profile != null).ToArray() ?? [];
        var macros = config.Macros?.Where(macro => macro != null).ToArray() ?? [];
        var gestures = config.Gestures?.Where(gesture => gesture != null).ToArray() ?? [];
        var deckLayouts = config.DeckLayouts?.Where(layout => layout != null).ToArray() ?? [];
        var usage = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        Mapping[] configuredMappings = [.. profiles.SelectMany(profile => profile.Mappings ?? [])
            .Concat(deckLayouts.SelectMany(layout => layout.Mappings ?? []))
            .Where(mapping => mapping != null)];
        foreach (var mapping in configuredMappings.Where(HasConfiguredShortAction))
        {
            string signature = ActionPaletteSignature(mapping.Kind, mapping.Value);
            usage[signature] = usage.GetValueOrDefault(signature) + 1;
        }

        var actions = new List<CatalogAction>();
        actions.AddRange(ActionPaletteKeyActions());
        actions.AddRange(actionPaletteCustomShortcuts);
        actions.AddRange(ActionCatalog.Items);
        actions.AddRange(actionPaletteApplications
            .Where(application => !string.IsNullOrWhiteSpace(application.Name) && !string.IsNullOrWhiteSpace(application.LaunchPath))
            .Select(application => new CatalogAction(ActionPaletteApplicationsCategory, application.Name, $"{application.Source}からアプリを起動します", ActionKind.Launch, application.LaunchPath)));
        actions.AddRange(profiles
            .Where(profile => !string.IsNullOrWhiteSpace(profile.Name))
            .Select(profile => new CatalogAction("プロファイル切替", profile.Name, "このプロファイルへ切り替えます", ActionKind.Profile, profile.Name)));
        actions.AddRange(macros
            .Where(macro => !string.IsNullOrWhiteSpace(macro.Name))
            .Select(macro => new CatalogAction("マクロ", macro.Name, $"{macro.Steps?.Count ?? 0}手順のマクロを実行します", ActionKind.Macro, macro.Name)));
        actions.AddRange(gestures
            .Where(gesture => !string.IsNullOrWhiteSpace(gesture.Name))
            .Select(gesture => new CatalogAction("ジェスチャー", gesture.Name, "登録済みのジェスチャーを実行します", ActionKind.Gesture, gesture.Name)));
        actions.AddRange(deckLayouts
            .Where(layout => !string.IsNullOrWhiteSpace(layout.Name) && !string.IsNullOrWhiteSpace(layout.Id))
            .Select(layout => new CatalogAction("Deckパネル", layout.Name, $"{layout.Columns}×{layout.Rows}のDeckを表示します", ActionKind.Shortcut, DeckPanelLayout.ActionValue(layout.Id))));

        foreach (var mapping in configuredMappings.Where(HasConfiguredShortAction))
        {
            string signature = ActionPaletteSignature(mapping.Kind, mapping.Value);
            if (actions.Any(action => ActionPaletteSignature(action.Kind, action.Value).Equals(signature, StringComparison.OrdinalIgnoreCase)))
                continue;
            string name = FriendlyActionValue(mapping.Kind, mapping.Value);
            actions.Add(new CatalogAction("使用中のAction", name, "現在の設定で使用しているActionです", mapping.Kind, mapping.Value));
        }

        if (deckManagementMode && selectedDeckLayout != null)
        {
            actions.AddRange(DeckMonitorCatalog.Items.Select(monitor => new CatalogAction(
                DeckMonitorCatalog.Category,
                monitor.Name,
                monitor.Description,
                ActionKind.Disabled,
                DeckMonitorActionPrefix + monitor.Id)));
        }

        actionPaletteItems = [.. actions
            .Where(action => action != null && action.Kind != ActionKind.None && !string.IsNullOrWhiteSpace(action.Name))
            .Select(action => new ActionPaletteItem(
                action,
                action.Name,
                ActionPaletteGroup(action),
                ActionPaletteGlyph(action),
                usage.GetValueOrDefault(ActionPaletteSignature(action.Kind, action.Value))))
            .GroupBy(item => $"{item.Group}\n{ActionPaletteSignature(item.Action.Kind, item.Action.Value)}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())];

        string? previousCategory = ActionPaletteCategoryBox.SelectedItem as string;
        var groups = actionPaletteItems.Select(item => item.Group).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        groups.RemoveAll(group => string.Equals(group, ActionPaletteAllCategory, StringComparison.OrdinalIgnoreCase)
            || string.Equals(group, ActionPaletteUsedCategory, StringComparison.OrdinalIgnoreCase));
        if (!groups.Contains(ActionPaletteShortcutsCategory, StringComparer.OrdinalIgnoreCase))
            groups.Add(ActionPaletteShortcutsCategory);
        var preferredOrder = new[]
        {
            ActionPaletteApplicationsCategory, ActionPaletteKeysCategory, ActionPaletteShortcutsCategory,
            "Windows", "入力・編集", "ファイル・文書", "メディア", "ウィンドウ・デスクトップ",
            "ブラウザー", "エクスプローラー", "マウス", "オーバーレイ", "Windowsアプリ",
            "プロファイル", "マクロ", "ジェスチャー", "Deckパネル", "その他"
        };
        var orderedGroups = preferredOrder.Where(group => groups.Remove(group)).Concat(groups.OrderBy(group => group, StringComparer.CurrentCultureIgnoreCase)).ToList();

        refreshingActionPalette = true;
        try
        {
            ActionPaletteCategoryBox.ItemsSource = new[] { ActionPaletteAllCategory, ActionPaletteUsedCategory }.Concat(orderedGroups).ToList();
            ActionPaletteCategoryBox.SelectedItem = previousCategory != null && ActionPaletteCategoryBox.Items.Contains(previousCategory)
                ? previousCategory
                : ActionPaletteAllCategory;
        }
        finally
        {
            refreshingActionPalette = false;
        }
        FilterActionPalette();
    }

    static string ActionPaletteSignature(ActionKind kind, string? value)
        => $"{kind}:{(value ?? string.Empty).Trim()}";

    static string ActionPaletteGroup(CatalogAction action)
    {
        if (string.Equals(action.Category, DeckMonitorCatalog.Category, StringComparison.OrdinalIgnoreCase))
            return DeckMonitorCatalog.Category;
        if (string.Equals(action.Category, "システム操作", StringComparison.OrdinalIgnoreCase))
            return "システム操作";
        return ActionPaletteGroupCore(action);
    }

    static string ActionPaletteGroupCore(CatalogAction action) => (action.Category ?? string.Empty) switch
    {
        "使用中のAction" => ActionPaletteUsedCategory,
        ActionPaletteApplicationsCategory => ActionPaletteApplicationsCategory,
        ActionPaletteKeysCategory => ActionPaletteKeysCategory,
        "任意のショートカット" => ActionPaletteShortcutsCategory,
        "マクロ" => "マクロ",
        "ジェスチャー" => "ジェスチャー",
        "Deckパネル" => "Deckパネル",
        _ => string.IsNullOrWhiteSpace(action.Category) ? "その他" : ActionCatalog.GetMajorCategory(action.Category)
    };

    static IEnumerable<CatalogAction> ActionPaletteKeyActions()
        => ActionPaletteKeySequence
            .Select(token => new CatalogAction(ActionPaletteKeysCategory, DisplayInputName(token), "このキーを送信します", ActionKind.Key, token));

    static string[] BuildActionPaletteKeySequence()
    {
        var keys = new List<string> { "Esc" };
        keys.AddRange(Enumerable.Range(1, 12).Select(number => $"F{number}"));
        keys.AddRange(["PrintScreen", "ScrollLock", "Pause"]);
        keys.AddRange(Enumerable.Range(13, 12).Select(number => $"F{number}"));
        keys.AddRange(["半角/全角"]);
        keys.AddRange(Enumerable.Range(1, 9).Select(number => number.ToString()));
        keys.AddRange(["0", "Backspace", "Tab"]);
        keys.AddRange("QWERTYUIOP".Select(letter => letter.ToString()));
        keys.AddRange(["CapsLock"]);
        keys.AddRange("ASDFGHJKL".Select(letter => letter.ToString()));
        keys.AddRange(["Enter", "Shift"]);
        keys.AddRange("ZXCVBNM".Select(letter => letter.ToString()));
        keys.AddRange(["Ctrl", "Win", "Alt", "無変換", "Space", "変換", "カタカナ"]);
        keys.AddRange(["Insert", "Home", "PageUp", "Delete", "End", "PageDown", "Up", "Left", "Down", "Right"]);
        keys.AddRange(["NumLock", "Divide", "Multiply", "Subtract", "NumPad7", "NumPad8", "NumPad9", "Add", "NumPad4", "NumPad5", "NumPad6", "NumPad1", "NumPad2", "NumPad3", "NumPadEnter", "NumPad0", "Decimal"]);
        return [.. keys.Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    void CreateActionPaletteShortcut_Click(object sender, RoutedEventArgs e)
    {
        var picker = new MacroInputPickerWindow(config.KeyboardLayout)
        {
            Owner = this,
            Title = "任意のショートカットを作成"
        };
        string shortcut = string.Empty;
        picker.ConfigureShortcutEditing(shortcut);
        picker.ShortcutChanged += value => shortcut = value.Trim();
        picker.ShowDialog();
        AddActionPaletteShortcut(shortcut);
    }

    void AddActionPaletteShortcut(string? shortcut)
    {
        string value = shortcut?.Trim() ?? string.Empty;
        if (value.Length == 0)
            return;
        actionPaletteCustomShortcuts.RemoveAll(action => action.Value.Equals(value, StringComparison.OrdinalIgnoreCase));
        actionPaletteCustomShortcuts.Insert(0, new CatalogAction("任意のショートカット", DisplayInputName(value), "作成したショートカットを送信します", ActionKind.Shortcut, value));
        RefreshActionPalette();
        ActionPaletteCategoryBox.SelectedItem = ActionPaletteShortcutsCategory;
    }

    void StartActionPaletteApplicationDiscovery()
    {
        if (actionPaletteApplicationDiscoveryStarted)
            return;
        actionPaletteApplicationDiscoveryStarted = true;
        _ = LoadActionPaletteApplicationsAsync();
    }

    async Task LoadActionPaletteApplicationsAsync()
    {
        try
        {
            var discovered = await Task.Run(ApplicationPickerWindow.DiscoverApplications);
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                return;
            await Dispatcher.InvokeAsync(() =>
            {
                actionPaletteApplications = discovered;
                if (!actionPaletteOpen)
                    return;
                try
                {
                    RefreshActionPalette();
                }
                catch (Exception ex)
                {
                    LifecycleDiagnostics.Write("action-palette-application-refresh-failed", ex.ToString());
                }
            });
        }
        catch (Exception ex)
        {
            LifecycleDiagnostics.Write("action-palette-application-discovery-failed", ex.ToString());
        }
    }

    static string ActionPaletteGlyph(CatalogAction action)
    {
        if (TryGetMonitorAction(action, out var monitor))
            return monitor.Glyph;
        try
        {
            if (!string.IsNullOrWhiteSpace(action.Value))
            {
                string presetId = DeckIconCatalog.SuggestedPresetId(action);
                string? presetGlyph = DeckIconCatalog.Presets.FirstOrDefault(preset => string.Equals(preset.Id, presetId, StringComparison.OrdinalIgnoreCase))?.Glyph;
                if (!string.IsNullOrWhiteSpace(presetGlyph))
                    return presetGlyph;
            }
        }
        catch
        {
            // Icon suggestion is decorative.  A malformed custom value must
            // never prevent the action library itself from opening.
        }
        return action.Kind switch
            {
                ActionKind.Key => "\uE765",
                ActionKind.Profile => "\uE8AB",
                ActionKind.Text => "T",
                ActionKind.Launch => "\uE71D",
                ActionKind.Macro => "\uE8D7",
                ActionKind.Gesture => "\uE7C9",
                ActionKind.Mouse => "\uE962",
                _ => "\uE8FD"
            };
    }

    void ActionPaletteSearchChanged(object sender, TextChangedEventArgs e)
    {
        bool empty = string.IsNullOrEmpty(ActionPaletteSearchBox.Text);
        if (ActionPaletteSearchHint != null)
            ActionPaletteSearchHint.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (ActionPaletteSearchClearButton != null)
            ActionPaletteSearchClearButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
        FilterActionPalette();
    }

    void ActionPaletteSearchPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBox searchBox || searchBox.Text.Length != 0)
            return;
        searchBox.Focus();
        searchBox.CaretIndex = 0;
        e.Handled = true;
    }

    void ClearActionPaletteSearch_Click(object sender, RoutedEventArgs e)
    {
        ActionPaletteSearchBox.Clear();
        ActionPaletteSearchBox.Focus();
        ActionPaletteSearchBox.CaretIndex = 0;
    }

    void ActionPaletteCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshingActionPalette)
            FilterActionPalette();
    }

    void FilterActionPalette()
    {
        var categoryBox = ActionPaletteCategoryBox;
        if (ActionPaletteList == null || categoryBox == null || refreshingActionPalette)
            return;
        string query = ActionPaletteSearchBox?.Text.Trim() ?? string.Empty;
        string category = categoryBox.SelectedItem as string ?? ActionPaletteAllCategory;
        IEnumerable<ActionPaletteItem> filtered = actionPaletteItems;
        if (category == ActionPaletteUsedCategory)
            filtered = filtered.Where(item => item.UsageCount > 0);
        else if (category != ActionPaletteAllCategory)
            filtered = filtered.Where(item => string.Equals(item.Group, category, StringComparison.OrdinalIgnoreCase));
        if (query.Length > 0)
            filtered = filtered.Where(item => new[] { item.Name, item.Group, item.Action.Category, item.Action.Description, item.Action.Value }
                .Any(text => text?.Contains(query, StringComparison.OrdinalIgnoreCase) == true));
        if (category is ActionPaletteAllCategory or ActionPaletteUsedCategory)
            filtered = filtered
                .GroupBy(item => ActionPaletteSignature(item.Action.Kind, item.Action.Value), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(item => item.Group == ActionPaletteKeysCategory ? 1 : 0).First());
        var groupOrder = categoryBox.Items.Cast<string>()
            .Select((group, index) => (group, index))
            .ToDictionary(entry => entry.group, entry => entry.index, StringComparer.OrdinalIgnoreCase);
        List<ActionPaletteItem> result;
        if (category == ActionPaletteUsedCategory)
        {
            result = filtered
                .OrderByDescending(item => item.UsageCount)
                .ThenBy(item => groupOrder.GetValueOrDefault(item.Group, int.MaxValue))
                .ThenBy(item => item.Group == ActionPaletteKeysCategory ? ActionPaletteKeyOrder.GetValueOrDefault(item.Action.Value, int.MaxValue) : int.MaxValue)
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        else if (category == ActionPaletteKeysCategory)
        {
            result = filtered
                .OrderBy(item => ActionPaletteKeyOrder.GetValueOrDefault(item.Action.Value, int.MaxValue))
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        else
        {
            result = filtered
                .OrderBy(item => groupOrder.GetValueOrDefault(item.Group, int.MaxValue))
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        ActionPaletteList.ItemsSource = result;
        ActionPaletteResultCount.Text = $"{result.Count}";
        ActionPaletteEmptyText.Visibility = result.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ActionPaletteCustomShortcutButton.Visibility = category == ActionPaletteShortcutsCategory ? Visibility.Visible : Visibility.Collapsed;
    }

    void ActionPaletteItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        actionPaletteDragItem = ActionPaletteItemFromSource(e.OriginalSource as DependencyObject);
        actionPaletteDragStart = e.GetPosition(ActionPaletteList);
    }

    void ActionPaletteItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (actionPaletteDragItem == null || e.LeftButton != MouseButtonState.Pressed)
            return;
        Point current = e.GetPosition(ActionPaletteList);
        if (Math.Abs(current.X - actionPaletteDragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - actionPaletteDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        var item = actionPaletteDragItem;
        actionPaletteDragItem = null;
        if (ActionPaletteList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container)
            return;

        var data = new DataObject();
        if (TryGetMonitorAction(item.Action, out var monitor))
            data.SetData(DeckMonitorDragFormat, monitor.Id);
        else
            data.SetData(ActionPaletteDragFormat, item.Action);
        RunActionPaletteDrag(container, item, data);
        e.Handled = true;
    }

    void ActionPaletteItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => actionPaletteDragItem = null;

    ActionPaletteItem? ActionPaletteItemFromSource(DependencyObject? source)
    {
        for (DependencyObject? current = source; current != null && !ReferenceEquals(current, ActionPaletteList); current = GetParent(current))
            if (current is ListBoxItem { DataContext: ActionPaletteItem item })
                return item;
        return null;
    }

    internal static double ActionPaletteDragPreviewWidth(double sourceWidth)
        => Math.Clamp(sourceWidth * ActionPaletteDragPreviewScale, ActionPaletteDragPreviewMinWidth, ActionPaletteDragPreviewMaxWidth);

    static void RunActionPaletteDrag(ListBoxItem container, ActionPaletteItem item, DataObject data)
    {
        DeckDragPreviewWindow? preview = null;
        GiveFeedbackEventHandler? feedback = null;
        try
        {
            double previewWidth = ActionPaletteDragPreviewWidth(container.ActualWidth);
            var glyph = new TextBlock
            {
                Text = item.Glyph,
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Variable"),
                FontSize = 12.5,
                Foreground = ThemeService.Brush("AccentTextBrush"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            var glyphFrame = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(6),
                Background = ThemeService.Brush("SurfaceBackground"),
                Child = glyph,
                VerticalAlignment = VerticalAlignment.Center
            };
            var name = new TextBlock
            {
                Text = item.Name,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = ThemeService.Brush("PrimaryText"),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var group = new TextBlock
            {
                Text = item.Group,
                FontSize = 8.5,
                Foreground = ThemeService.Brush("MutedText"),
                TextWrapping = TextWrapping.NoWrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 1, 0, 0)
            };
            var labels = new StackPanel { Margin = new Thickness(8, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            labels.Children.Add(name);
            labels.Children.Add(group);
            var grip = new TextBlock
            {
                Text = "⋮⋮",
                FontSize = 9,
                Foreground = ThemeService.Brush("MutedText"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            var row = new Grid { Margin = new Thickness(7, 0, 7, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            row.Children.Add(glyphFrame);
            Grid.SetColumn(labels, 1);
            row.Children.Add(labels);
            Grid.SetColumn(grip, 2);
            row.Children.Add(grip);
            var face = new Border
            {
                Width = previewWidth,
                Height = ActionPaletteDragPreviewHeight,
                CornerRadius = new CornerRadius(8),
                Background = ThemeService.Brush("ControlHoverBackground"),
                BorderBrush = ThemeService.Brush("AccentBrush"),
                BorderThickness = new Thickness(1),
                Child = row
            };
            preview = new DeckDragPreviewWindow(face, customWidth: previewWidth, customHeight: ActionPaletteDragPreviewHeight, preservePreviewSurface: true);
            feedback = (_, args) =>
            {
                var cursor = System.Windows.Forms.Cursor.Position;
                preview.MoveToPhysical(cursor.X, cursor.Y);
                args.UseDefaultCursors = false;
                args.Handled = true;
            };
            container.GiveFeedback += feedback;
            preview.Show();
            var initialCursor = System.Windows.Forms.Cursor.Position;
            preview.MoveToPhysical(initialCursor.X, initialCursor.Y);
            DragDrop.DoDragDrop(container, data, DragDropEffects.Copy);
        }
        finally
        {
            if (feedback != null)
                container.GiveFeedback -= feedback;
            preview?.Close();
        }
    }

    internal static bool TryGetPaletteAction(IDataObject data, out CatalogAction action)
    {
        action = null!;
        if (!data.GetDataPresent(ActionPaletteDragFormat) || data.GetData(ActionPaletteDragFormat) is not CatalogAction value)
            return false;
        action = value;
        return true;
    }

    static bool TryGetMonitorAction(CatalogAction action, out DeckMonitorDefinition monitor)
    {
        monitor = null!;
        string value = action.Value ?? string.Empty;
        if (!value.StartsWith(DeckMonitorActionPrefix, StringComparison.OrdinalIgnoreCase))
            return false;
        return DeckMonitorCatalog.TryGet(value[DeckMonitorActionPrefix.Length..], out monitor);
    }

    internal static bool TryGetPaletteMonitor(IDataObject data, out DeckMonitorDefinition monitor)
    {
        monitor = null!;
        if (!data.GetDataPresent(DeckMonitorDragFormat) || data.GetData(DeckMonitorDragFormat) is not string id)
            return false;
        return DeckMonitorCatalog.TryGet(id, out monitor);
    }

    bool CanAssignPaletteAction(string input, CatalogAction action)
    {
        if (DeckPanelLayout.IsInputName(input))
            return selectedDeckLayout != null && action.Kind != ActionKind.Gesture;
        string key = input[(input.LastIndexOf('+') + 1)..];
        return CanUseAssignmentDragKey(key, source: false);
    }

    IReadOnlyList<string> PaletteDropTargets(string targetInput, string targetKey)
    {
        bool targetIsSelected = MultiSelectToggle.IsChecked == true
            && (DeckPanelLayout.IsInputName(targetInput) ? multiSelectedInputs.Contains(targetInput) : multiSelectedInputs.Contains(targetKey));
        if (!targetIsSelected)
            return [targetInput];
        return [.. multiSelectedInputs.Select(MultiSelectionInput).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    bool ApplyPaletteActionDrop(CatalogAction action, string targetInput, string targetKey)
    {
        var targets = PaletteDropTargets(targetInput, targetKey)
            .Where(input => CanAssignPaletteAction(input, action))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (targets.Length == 0)
            return false;
        if (action.Kind == ActionKind.Gesture
            && targets.Any(input => input is "MouseRight" or "MouseBack" or "MouseForward")
            && !ConfirmDirectMouseGestureConflict(targets.First(input => input is "MouseRight" or "MouseBack" or "MouseForward")))
            return false;

        var snapshots = targets.Select(CapturePaletteAssignment).ToArray();
        foreach (string input in targets)
            ApplyPaletteActionToInput(action, input);

        actionPaletteUndoState = new ActionPaletteUndoState(snapshots, targets.Length == 1
            ? $"{DisplayInputName(targets[0])} に割り当てました"
            : $"{targets.Length}個の入力に割り当てました");
        ShowActionPaletteUndo(actionPaletteUndoState.Message);
        CommitPaletteAssignment(actionPaletteUndoState.Message, targets);
        if (selected != null && targets.Contains(selected.Input, StringComparer.OrdinalIgnoreCase))
            SelectInput(selected.Input, false);
        PlayPaletteDropSuccess(targets);
        RefreshActionPalette();
        return true;
    }

    bool ApplyPaletteMonitorDrop(DeckMonitorDefinition monitor, string targetInput)
    {
        if (selectedDeckLayout == null || !DeckPanelLayout.IsInputName(targetInput))
            return false;
        var snapshots = new[] { CapturePaletteAssignment(targetInput) };
        ApplyPaletteMonitorToInput(monitor, targetInput);
        actionPaletteUndoState = new ActionPaletteUndoState(snapshots, $"{DisplayInputName(targetInput)} に{monitor.Name}を配置しました");
        ShowActionPaletteUndo(actionPaletteUndoState.Message);
        CommitPaletteAssignment(actionPaletteUndoState.Message, [targetInput]);
        if (selected?.Input.Equals(targetInput, StringComparison.OrdinalIgnoreCase) == true)
            SelectInput(targetInput, false);
        PlayPaletteDropSuccess([targetInput]);
        RefreshActionPalette();
        return true;
    }

    void ApplyPaletteMonitorToInput(DeckMonitorDefinition monitor, string input)
    {
        if (selectedDeckLayout == null)
            return;
        Mapping? mapping = selectedDeckLayout.Mappings.LastOrDefault(candidate => candidate.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
        if (mapping == null)
        {
            mapping = new Mapping { Input = input, Layer = DeckPanelLayout.Layer };
            selectedDeckLayout.Mappings.Add(mapping);
        }
        mapping.Kind = ActionKind.None;
        mapping.Value = string.Empty;
        mapping.LongPressKind = ActionKind.None;
        mapping.LongPressValue = string.Empty;
        mapping.DragValue = string.Empty;
        mapping.DragEndValue = string.Empty;
        mapping.Application = string.Empty;
        mapping.DeckFilePath = string.Empty;
        mapping.DeckIcon = string.Empty;
        mapping.DeckIconPath = string.Empty;
        mapping.DeckIconAutoAssigned = false;
        mapping.DeckMonitor = monitor.Id;
    }

    void StartActionPaletteUndoTimer()
    {
        if (!actionPaletteUndoTimerInitialized)
        {
            actionPaletteUndoTimer.Tick += (_, _) => ExpireActionPaletteUndo();
            actionPaletteUndoTimerInitialized = true;
        }
        actionPaletteUndoTimer.Stop();
        actionPaletteUndoTimer.Start();
    }

    void ShowActionPaletteUndo(string message)
    {
        ++actionPaletteUndoMotionGeneration;
        ActionPaletteUndoText.Text = message;
        ActionPaletteUndoBar.BeginAnimation(UIElement.OpacityProperty, null);
        var translate = UiMotionService.MutableTranslate(ActionPaletteUndoBar);
        translate.BeginAnimation(TranslateTransform.YProperty, null);
        ActionPaletteUndoBar.Visibility = Visibility.Visible;
        if (UiMotionService.Enabled)
        {
            var motion = UiMotionService.MutableTranslate(ActionPaletteUndoBar);
            ActionPaletteUndoBar.Opacity = 0;
            motion.Y = 5;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            ActionPaletteUndoBar.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(1, TimeSpan.FromMilliseconds(155)) { EasingFunction = ease },
                HandoffBehavior.SnapshotAndReplace);
            motion.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, TimeSpan.FromMilliseconds(185)) { EasingFunction = ease },
                HandoffBehavior.SnapshotAndReplace);
        }
        else
        {
            ActionPaletteUndoBar.Opacity = 1;
            UiMotionService.MutableTranslate(ActionPaletteUndoBar).Y = 0;
        }
        StartActionPaletteUndoTimer();
    }

    void ExpireActionPaletteUndo()
    {
        actionPaletteUndoTimer.Stop();
        actionPaletteUndoState = null;
        HideActionPaletteUndo(animated: true);
    }

    void HideActionPaletteUndo(bool animated)
    {
        int generation = ++actionPaletteUndoMotionGeneration;
        if (!animated || !UiMotionService.Enabled)
        {
            ResetActionPaletteUndoMotion();
            ActionPaletteUndoBar.Visibility = Visibility.Collapsed;
            return;
        }
        var translate = UiMotionService.MutableTranslate(ActionPaletteUndoBar);

        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(145)) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            if (generation != actionPaletteUndoMotionGeneration || actionPaletteUndoState != null)
                return;
            ResetActionPaletteUndoMotion();
            ActionPaletteUndoBar.Visibility = Visibility.Collapsed;
        };
        ActionPaletteUndoBar.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
        translate.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(4, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease },
            HandoffBehavior.SnapshotAndReplace);
    }

    void ResetActionPaletteUndoMotion()
    {
        ActionPaletteUndoBar.BeginAnimation(UIElement.OpacityProperty, null);
        ActionPaletteUndoBar.Opacity = 1;
        var translate = UiMotionService.MutableTranslate(ActionPaletteUndoBar);
        translate.BeginAnimation(TranslateTransform.YProperty, null);
        translate.Y = 0;
    }

    void PlayPaletteDropSuccess(IEnumerable<string> inputs)
    {
        foreach (string input in inputs)
        {
            IEnumerable<Button> buttons = DeckPanelLayout.IsInputName(input)
                ? deckManagementButtons.Where(button => button.IsVisible && string.Equals(button.Tag?.ToString(), input, StringComparison.OrdinalIgnoreCase))
                : VisualInputButtons().Where(button => button.IsVisible && string.Equals(button.Tag?.ToString(), input[(input.LastIndexOf('+') + 1)..], StringComparison.OrdinalIgnoreCase));
            foreach (Button button in buttons)
                UiMotionService.RunSafely("action-drop-success", () => PlayActionDropSuccess(button));
        }
    }

    static void PlayActionDropSuccess(Button button)
    {
        button.ApplyTemplate();
        if (button.Template.FindName("DropTargetTint", button) is not FrameworkElement wave)
            return;
        if (!UiMotionService.Enabled)
        {
            wave.BeginAnimation(UIElement.OpacityProperty, null);
            wave.Opacity = 0;
            return;
        }

        var finalColor = button.Background is SolidColorBrush solid
            ? solid.Color
            : ThemeService.Color("AccentBrush");
        var waveBrush = new RadialGradientBrush
        {
            Center = new Point(.5, .5),
            GradientOrigin = new Point(.5, .5),
            RadiusX = .72,
            RadiusY = .72,
            GradientStops =
            {
                new GradientStop(MediaColor.FromArgb(235, finalColor.R, finalColor.G, finalColor.B), 0),
                new GradientStop(MediaColor.FromArgb(190, finalColor.R, finalColor.G, finalColor.B), .62),
                new GradientStop(MediaColor.FromArgb(0, finalColor.R, finalColor.G, finalColor.B), 1)
            }
        };
        if (wave is Border border)
            border.Background = waveBrush;
        else if (wave is System.Windows.Shapes.Shape shape)
            shape.Fill = waveBrush;

        wave.RenderTransformOrigin = new Point(.5, .5);
        var waveScale = new ScaleTransform(.08, .08);
        wave.RenderTransform = waveScale;
        wave.Opacity = 1;
        var expansion = new DoubleAnimation(1.22, TimeSpan.FromMilliseconds(480))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        var fade = new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            {
                new DiscreteDoubleKeyFrame(.95, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new EasingDoubleKeyFrame(.78, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(250))),
                new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(540)))
            },
            FillBehavior = FillBehavior.Stop
        };
        fade.Completed += (_, _) => wave.Opacity = 0;
        waveScale.BeginAnimation(ScaleTransform.ScaleXProperty, expansion, HandoffBehavior.SnapshotAndReplace);
        waveScale.BeginAnimation(ScaleTransform.ScaleYProperty, expansion, HandoffBehavior.SnapshotAndReplace);
        wave.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);

        double restingScale = button.IsMouseOver ? 1.05 : 1;
        var spring = new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            {
                new EasingDoubleKeyFrame(1.08, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(105)), new CubicEase { EasingMode = EasingMode.EaseOut }),
                new EasingDoubleKeyFrame(.985, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(245)), new CubicEase { EasingMode = EasingMode.EaseInOut }),
                new EasingDoubleKeyFrame(restingScale + .015, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(365)), new CubicEase { EasingMode = EasingMode.EaseOut }),
                new EasingDoubleKeyFrame(restingScale, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(520)), new CubicEase { EasingMode = EasingMode.EaseOut })
            }
        };
        var buttonScale = InputScaleTransform(button);
        buttonScale.BeginAnimation(ScaleTransform.ScaleXProperty, spring, HandoffBehavior.SnapshotAndReplace);
        buttonScale.BeginAnimation(ScaleTransform.ScaleYProperty, spring, HandoffBehavior.SnapshotAndReplace);
    }

    ActionPaletteMappingSnapshot CapturePaletteAssignment(string input)
    {
        List<Mapping> collection = DeckPanelLayout.IsInputName(input) && selectedDeckLayout != null
            ? selectedDeckLayout.Mappings
            : CurrentProfile.Mappings;
        var previous = collection.Select((mapping, index) => (mapping, index))
            .Where(entry => entry.mapping.Input.Equals(input, StringComparison.OrdinalIgnoreCase))
            .Select(entry => new IndexedMapping(entry.index, entry.mapping.Copy()))
            .ToArray();
        return new ActionPaletteMappingSnapshot(collection, input, previous);
    }

    void ApplyPaletteActionToInput(CatalogAction action, string input)
    {
        bool deckInput = DeckPanelLayout.IsInputName(input) && selectedDeckLayout != null;
        List<Mapping> collection = deckInput ? selectedDeckLayout!.Mappings : CurrentProfile.Mappings;
        Mapping? mapping = collection.LastOrDefault(candidate => candidate.Input.Equals(input, StringComparison.OrdinalIgnoreCase));
        if (mapping == null)
        {
            Mapping? visible = deckInput ? null : FindProfileMapping(config.Profiles, CurrentProfile.Name, input, MappingInterceptsInput);
            mapping = SelectEditorMapping(collection, visible, input);
            mapping.Input = input;
            mapping.Layer = deckInput ? DeckPanelLayout.Layer : AssignmentLayerName(input);
            collection.Add(mapping);
        }
        mapping.Kind = action.Kind;
        mapping.Value = action.Value;
        mapping.DeckMonitor = string.Empty;
        if (action.Kind == ActionKind.Gesture)
        {
            mapping.LongPressKind = ActionKind.None;
            mapping.LongPressValue = string.Empty;
        }
        if (deckInput)
        {
            mapping.DeckIcon = DeckIconCatalog.SuggestedPresetId(action);
            mapping.DeckIconPath = string.Empty;
            mapping.DeckIconAutoAssigned = true;
        }
    }

    void CommitPaletteAssignment(string message, IEnumerable<string>? changedInputs = null)
    {
        int[] deckSlots = [.. (changedInputs ?? []).Where(DeckPanelLayout.IsInputName).Select(DeckPanelLayout.SlotNumber).Where(slot => slot > 0).Distinct()];
        bool deckSynchronized = selectedDeckLayout != null && deckSlots.Length > 0;
        if (deckSynchronized)
        {
            // The editor and overlays share one Deck model. Replace only the
            // changed live cells, including cached hidden panels, and prevent
            // auto-save from following with an expensive full-grid rebuild.
            OverlayService.RefreshDeckPanelSlots(selectedDeckLayout!.Id, deckSlots);
            deckOverlayVisualSynchronized = true;
        }
        UpdateLayerButtons();
        ColorButtons();
        if (config.AutoSave)
            SaveAndApply(message);
        else
        {
            MarkDirty(refreshDeckPanel: !deckSynchronized);
            ShowInlineNotice(message + "（未保存）");
        }
    }

    void UndoActionPaletteAssignment_Click(object sender, RoutedEventArgs e)
    {
        if (actionPaletteUndoState == null)
            return;
        var snapshots = actionPaletteUndoState.Snapshots;
        foreach (var snapshot in snapshots)
        {
            snapshot.Collection.RemoveAll(mapping => mapping.Input.Equals(snapshot.Input, StringComparison.OrdinalIgnoreCase));
            foreach (var previous in snapshot.Previous.OrderBy(item => item.Index))
                snapshot.Collection.Insert(Math.Clamp(previous.Index, 0, snapshot.Collection.Count), previous.Mapping.Copy());
        }
        actionPaletteUndoState = null;
        actionPaletteUndoTimer.Stop();
        HideActionPaletteUndo(animated: false);
        CommitPaletteAssignment("直前の割り当てを元に戻しました", snapshots.Select(snapshot => snapshot.Input));
        if (selected != null && snapshots.Any(snapshot => snapshot.Input.Equals(selected.Input, StringComparison.OrdinalIgnoreCase)))
            SelectInput(selected.Input, false);
        RefreshActionPalette();
    }
}
