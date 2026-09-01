using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
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
    internal const int ActionDropWaveDurationMs = 500;
    const string ActionPaletteFavoritesCategory = "お気に入り";
    const string ActionPaletteRecentCategory = "最近使ったもの";
    const string ActionPaletteAllCategory = "すべて";
    const string ActionPaletteUsedCategory = "使用中";
    const string ActionPaletteApplicationsCategory = "インストールアプリ";
    const string ActionPaletteKeysCategory = "キー";
    const string ActionPaletteShortcutsCategory = "ショートカット";
    const string ActionPaletteCreateCategory = "パス・文字列";
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
    DeckDragPreviewWindow? actionPaletteDragPreview;
    ActionPaletteUndoState? actionPaletteUndoState;
    List<ActionPaletteItem> actionPaletteItems = [];
    List<ActionPaletteCategoryOption> actionPaletteCategoryOptions = [];
    IReadOnlyList<InstalledApplicationInfo> actionPaletteApplications = [];
    readonly List<CatalogAction> actionPaletteCustomShortcuts = [];
    bool actionPaletteApplicationDiscoveryStarted;
    Func<CatalogAction, string?>? actionPaletteValueResolverForTest;
    readonly System.Windows.Threading.DispatcherTimer actionPaletteUndoTimer = new() { Interval = TimeSpan.FromSeconds(5) };

    sealed record ActionPaletteItem(CatalogAction Action, string Name, string Group, string Detail, string Glyph, int UsageCount,
        bool IsFavorite = false)
    {
        public string FavoriteGlyph => IsFavorite ? "★" : "☆";
        public string FavoriteToolTip => LocalizationService.Text(IsFavorite ? "お気に入りから外す" : "お気に入りに追加");
        public string ToolTipText => string.IsNullOrWhiteSpace(Action.Description)
            ? Name
            : $"{Name}\n{LocalizationService.Text(Action.Description)}";
    }

    internal sealed record ActionPaletteCategoryOption(
        string Name,
        string Section,
        string Glyph,
        string Tone,
        bool StartsSection,
        bool ShowDivider)
    {
        public string DisplayName => LocalizationService.Text(Name);
        public string DisplaySection => LocalizationService.Text(Section);
        public override string ToString() => DisplayName;
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
            SelectActionPaletteCategory(ActionPaletteRecentCategory);
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
        UpdateAssignmentSummary();
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

        var actions = new List<CatalogAction>
        {
            new(
                ActionPaletteCreateCategory,
                "文字列を入力…",
                "ドロップ後に入力した文字列をそのまま送信します",
                ActionKind.Text,
                string.Empty,
                CatalogActionValueRequest.Text),
            new(
                ActionPaletteCreateCategory,
                "アプリ・パス・URLを指定…",
                "ドロップ後にアプリ、ファイル、フォルダー、URLを指定します",
                ActionKind.Launch,
                string.Empty,
                CatalogActionValueRequest.Launch)
        };
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
                DeckMonitorCatalog.PaletteDescription(monitor.Id),
                !LocalizationService.IsJapanese
                    ? $"{monitor.Name}: {LocalizationService.Text(monitor.Description)}"
                    : $"{monitor.Name}：{monitor.Description}",
                ActionKind.Disabled,
                DeckMonitorActionPrefix + monitor.Id)));
        }

        var favoriteSignatures = config.ActionPaletteFavorites.ToHashSet(StringComparer.OrdinalIgnoreCase);
        actionPaletteItems = [.. actions
            .Where(action => action != null && action.Kind != ActionKind.None && !string.IsNullOrWhiteSpace(action.Name))
            .Select(action => new ActionPaletteItem(
                action,
                ActionPaletteDisplayName(action),
                ActionPaletteGroup(action),
                LocalizationService.Text(ActionPaletteItemDetail(action, ActionPaletteGroup(action))),
                ActionPaletteGlyph(action),
                usage.GetValueOrDefault(ActionPaletteSignature(action.Kind, action.Value)),
                favoriteSignatures.Contains(ActionPaletteSignature(action.Kind, action.Value))))
            .GroupBy(item => $"{item.Group}\n{ActionPaletteSignature(item.Action.Kind, item.Action.Value)}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())];

        string previousCategory = SelectedActionPaletteCategory();
        var groups = actionPaletteItems.Select(item => item.Group).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        groups.RemoveAll(group => string.Equals(group, ActionPaletteFavoritesCategory, StringComparison.OrdinalIgnoreCase)
            || string.Equals(group, ActionPaletteAllCategory, StringComparison.OrdinalIgnoreCase)
            || string.Equals(group, ActionPaletteUsedCategory, StringComparison.OrdinalIgnoreCase));
        if (!groups.Contains(ActionPaletteShortcutsCategory, StringComparer.OrdinalIgnoreCase))
            groups.Add(ActionPaletteShortcutsCategory);
        var preferredOrder = new[]
        {
            ActionPaletteCreateCategory, ActionPaletteKeysCategory, "マウス", ActionPaletteShortcutsCategory,
            DeckMonitorCatalog.Category, "Windows", "Windowsアプリ", ActionPaletteApplicationsCategory,
            "プロファイル", "マクロ", "ジェスチャー", "Deckパネル", "オーバーレイ",
            "入力・編集", "ファイル・文書", "メディア", "ウィンドウ・デスクトップ",
            "ブラウザー", "エクスプローラー", "システム操作", "その他"
        };
        var orderedGroups = preferredOrder.Where(group => groups.Remove(group)).Concat(groups.OrderBy(group => group, StringComparer.CurrentCultureIgnoreCase)).ToList();

        refreshingActionPalette = true;
        try
        {
            actionPaletteCategoryOptions = BuildActionPaletteCategoryOptions(
                new[] { ActionPaletteFavoritesCategory, ActionPaletteRecentCategory, ActionPaletteAllCategory, ActionPaletteUsedCategory }.Concat(orderedGroups));
            ActionPaletteCategoryBox.ItemsSource = actionPaletteCategoryOptions;
            SelectActionPaletteCategory(actionPaletteCategoryOptions.Any(option => option.Name.Equals(previousCategory, StringComparison.OrdinalIgnoreCase))
                ? previousCategory
                : ActionPaletteRecentCategory);
        }
        finally
        {
            refreshingActionPalette = false;
        }
        FilterActionPalette();
    }

    static string ActionPaletteDisplayName(CatalogAction action)
    {
        bool userNamed = action.Kind is ActionKind.Profile or ActionKind.Macro or ActionKind.Gesture
            || action.Category is "インストールアプリ" or "プロファイル切替" or "マクロ" or "ジェスチャー" or "Deckパネル";
        return userNamed ? LocalizationService.DisplayGeneratedName(action.Name) : LocalizationService.Text(action.Name);
    }

    static List<ActionPaletteCategoryOption> BuildActionPaletteCategoryOptions(IEnumerable<string> categories)
    {
        var options = new List<ActionPaletteCategoryOption>();
        string previousSection = string.Empty;
        foreach (string category in categories)
        {
            string section = ActionPaletteCategorySection(category);
            bool startsSection = !section.Equals(previousSection, StringComparison.Ordinal);
            options.Add(new ActionPaletteCategoryOption(
                category,
                section,
                ActionPaletteCategoryGlyph(category),
                ActionPaletteCategoryTone(category),
                startsSection,
                startsSection && options.Count > 0));
            previousSection = section;
        }
        return options;
    }

    static string ActionPaletteCategorySection(string category)
    {
        if (category is ActionPaletteFavoritesCategory or ActionPaletteRecentCategory or ActionPaletteAllCategory or ActionPaletteUsedCategory)
            return "ステータス";
        if (category is ActionPaletteCreateCategory)
            return "作成";
        if (category is ActionPaletteKeysCategory or "マウス" or ActionPaletteShortcutsCategory)
            return "キー入力";
        if (category is "Windows" or "Windowsアプリ" or ActionPaletteApplicationsCategory
            or "プロファイル" or "マクロ" or "ジェスチャー" or "Deckパネル" or "オーバーレイ"
            || string.Equals(category, DeckMonitorCatalog.Category, StringComparison.OrdinalIgnoreCase))
            return "機能";
        return "ショートカット";
    }

    static string ActionPaletteCategoryGlyph(string category) => category switch
    {
        ActionPaletteFavoritesCategory => "\uE734",
        ActionPaletteRecentCategory => "\uE81C",
        ActionPaletteAllCategory => "\uE80A",
        ActionPaletteUsedCategory => "\uE73E",
        ActionPaletteCreateCategory => "\uE710",
        ActionPaletteApplicationsCategory => "\uE71D",
        ActionPaletteKeysCategory => "\uE765",
        ActionPaletteShortcutsCategory => "\uE8D7",
        "Windows" => "\uE782",
        "入力・編集" => "\uE70F",
        "ファイル・文書" => "\uE8A5",
        "メディア" => "\uE8D6",
        "ウィンドウ・デスクトップ" => "\uE7F4",
        "ブラウザー" => "\uE774",
        "エクスプローラー" => "\uE8B7",
        "マウス" => "\uE962",
        "オーバーレイ" => "\uE737",
        "Windowsアプリ" => "\uE71D",
        "プロファイル" => "\uE8AB",
        "マクロ" => "\uE8D7",
        "ジェスチャー" => "\uE7C9",
        "Deckパネル" => "\uE80A",
        "モニター" => "\uE7F4",
        _ => "\uE8FD"
    };

    static string ActionPaletteCategoryTone(string category) => category switch
    {
        ActionPaletteKeysCategory => "key",
        "マウス" or ActionPaletteShortcutsCategory => "shortcut",
        ActionPaletteCreateCategory or "入力・編集" => "text",
        ActionPaletteApplicationsCategory or "Windowsアプリ" or "インストールアプリ"
            or "ファイル・文書" or "ブラウザー" or "エクスプローラー" => "launch",
        "マクロ" or "メディア" => "macro",
        "Windows" or "プロファイル" or "ウィンドウ・デスクトップ" => "profile",
        "ジェスチャー" or "Deckパネル" or "オーバーレイ" or "モニター" => "accent",
        ActionPaletteFavoritesCategory => "favorite",
        ActionPaletteRecentCategory => "recent",
        ActionPaletteUsedCategory or "システム操作" => "muted",
        _ => "accent"
    };

    string SelectedActionPaletteCategory()
        => ActionPaletteCategoryBox?.SelectedItem is ActionPaletteCategoryOption option
            ? option.Name
            : ActionPaletteCategoryBox?.SelectedItem?.ToString() ?? ActionPaletteRecentCategory;

    void SelectActionPaletteCategory(string category)
    {
        if (ActionPaletteCategoryBox == null)
            return;
        var option = actionPaletteCategoryOptions
            .FirstOrDefault(item => item.Name.Equals(category, StringComparison.OrdinalIgnoreCase));
        if (option != null)
            ActionPaletteCategoryBox.SelectedItem = option;
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

    internal static string ActionPaletteItemDetail(CatalogAction action, string group)
    {
        if (action.ValueRequest != CatalogActionValueRequest.None)
            return "ドロップ後に指定";
        string value = action.Value?.Trim() ?? string.Empty;
        if (action.Kind == ActionKind.Key
            || (action.Kind == ActionKind.Shortcut
                && (value.Contains('+')
                    || string.Equals(action.Category, "任意のショートカット", StringComparison.OrdinalIgnoreCase))))
            return DisplayInputName(value);

        return action.Kind switch
        {
            ActionKind.Launch => "アプリ",
            ActionKind.Text => "文字列",
            ActionKind.Mouse => "マウス",
            ActionKind.Macro => "マクロ",
            ActionKind.Profile => "プロファイル",
            ActionKind.Gesture => "ジェスチャー",
            ActionKind.Disabled when string.Equals(action.Category, DeckMonitorCatalog.Category, StringComparison.OrdinalIgnoreCase) => "モニター",
            ActionKind.Disabled => "無効化",
            _ => group
        };
    }

    static string ActionPaletteGroupCore(CatalogAction action) => (action.Category ?? string.Empty) switch
    {
        ActionPaletteFavoritesCategory => ActionPaletteFavoritesCategory,
        ActionPaletteCreateCategory => ActionPaletteCreateCategory,
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
        var keys = new List<string>();
        keys.AddRange("ABCDEFGHIJKLMNOPQRSTUVWXYZ".Select(letter => letter.ToString()));
        keys.AddRange(Enumerable.Range(0, 10).Select(number => number.ToString()));
        keys.AddRange(Enumerable.Range(1, 24).Select(number => $"F{number}"));
        keys.AddRange(["Esc", "Tab", "CapsLock", "Shift", "Ctrl", "Win", "Alt", "Space", "Enter", "Backspace"]);
        keys.AddRange(["Insert", "Home", "PageUp", "Delete", "End", "PageDown", "Up", "Left", "Down", "Right"]);
        keys.AddRange(["PrintScreen", "ScrollLock", "Pause", "半角/全角", "無変換", "変換", "カタカナ"]);
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
        SelectActionPaletteCategory(ActionPaletteShortcutsCategory);
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
        if (!empty && SelectedActionPaletteCategory() is ActionPaletteFavoritesCategory or ActionPaletteRecentCategory)
        {
            SelectActionPaletteCategory(ActionPaletteAllCategory);
            return;
        }
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

    void ActionPaletteCategoryDropDownOpened(object sender, EventArgs e)
    {
        if (ActionPaletteCategoryBox == null || ActionPalettePane == null)
            return;
        double categoryBottom = ActionPaletteCategoryBox.TranslatePoint(
            new Point(0, ActionPaletteCategoryBox.ActualHeight), ActionPalettePane).Y;
        double availableHeight = ActionPalettePane.ActualHeight - categoryBottom - 12;
        ActionPaletteCategoryBox.MaxDropDownHeight = Math.Max(160, availableHeight);
    }

    void FilterActionPalette()
    {
        var categoryBox = ActionPaletteCategoryBox;
        if (ActionPaletteList == null || categoryBox == null || refreshingActionPalette)
            return;
        string query = ActionPaletteSearchBox?.Text.Trim() ?? string.Empty;
        string category = SelectedActionPaletteCategory();
        IEnumerable<ActionPaletteItem> filtered = actionPaletteItems;
        if (category == ActionPaletteFavoritesCategory)
            filtered = filtered.Where(item => item.IsFavorite);
        else if (category == ActionPaletteRecentCategory)
        {
            var recentSignatures = config.ActionPaletteRecentActions.ToHashSet(StringComparer.OrdinalIgnoreCase);
            filtered = filtered.Where(item => recentSignatures.Contains(ActionPaletteSignature(item.Action.Kind, item.Action.Value)));
        }
        else if (category == ActionPaletteUsedCategory)
            filtered = filtered.Where(item => item.UsageCount > 0);
        else if (category != ActionPaletteAllCategory)
            filtered = filtered.Where(item => string.Equals(item.Group, category, StringComparison.OrdinalIgnoreCase));
        if (query.Length > 0)
            filtered = filtered.Where(item => new[] { item.Name, item.Group, LocalizationService.Text(item.Group), item.Action.Category, LocalizationService.Text(item.Action.Category), item.Action.Description, LocalizationService.Text(item.Action.Description), item.Action.Value }
                .Any(text => text?.Contains(query, StringComparison.OrdinalIgnoreCase) == true));
        if (category is ActionPaletteFavoritesCategory or ActionPaletteRecentCategory or ActionPaletteAllCategory or ActionPaletteUsedCategory)
            filtered = filtered
                .GroupBy(item => ActionPaletteSignature(item.Action.Kind, item.Action.Value), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(item => item.Group == ActionPaletteKeysCategory ? 1 : 0).First());
        var groupOrder = actionPaletteCategoryOptions
            .Select((option, index) => (option.Name, index))
            .ToDictionary(entry => entry.Name, entry => entry.index, StringComparer.OrdinalIgnoreCase);
        List<ActionPaletteItem> result;
        if (category == ActionPaletteFavoritesCategory)
        {
            result = filtered
                .OrderBy(item => groupOrder.GetValueOrDefault(item.Group, int.MaxValue))
                .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        else if (category == ActionPaletteRecentCategory)
        {
            var recentOrder = config.ActionPaletteRecentActions
                .Select((signature, index) => (signature, index))
                .ToDictionary(entry => entry.signature, entry => entry.index, StringComparer.OrdinalIgnoreCase);
            result = filtered
                .OrderBy(item => recentOrder.GetValueOrDefault(ActionPaletteSignature(item.Action.Kind, item.Action.Value), int.MaxValue))
                .ToList();
        }
        else if (category == ActionPaletteUsedCategory)
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
        ActionPaletteEmptyText.Text = category switch
        {
            ActionPaletteFavoritesCategory when result.Count == 0 => "Action右側の☆を押すと、ここに表示されます",
            ActionPaletteRecentCategory when result.Count == 0 => "Actionを割り当てると、ここに表示されます",
            _ => "一致するActionはありません"
        };
        ActionPaletteEmptyText.Visibility = result.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ActionPaletteCustomShortcutButton.Visibility = category == ActionPaletteShortcutsCategory ? Visibility.Visible : Visibility.Collapsed;
        ActionPaletteClearRecentButton.Visibility = category == ActionPaletteRecentCategory ? Visibility.Visible : Visibility.Collapsed;
        ActionPaletteClearRecentButton.IsEnabled = config.ActionPaletteRecentActions.Count > 0;
    }

    void ClearRecentPaletteActions_Click(object sender, RoutedEventArgs e)
    {
        if (config.ActionPaletteRecentActions.Count == 0)
            return;
        config.ActionPaletteRecentActions.Clear();
        PersistActionPaletteLibraryPreferences();
        FilterActionPalette();
        ShowInlineNotice("最近使ったActionをクリアしました");
    }

    void ActionPaletteItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        for (DependencyObject? current = e.OriginalSource as DependencyObject; current != null && !ReferenceEquals(current, ActionPaletteList); current = GetParent(current))
        {
            if (current is Button { Name: "ActionFavoriteButton" })
            {
                actionPaletteDragItem = null;
                return;
            }
        }
        actionPaletteDragItem = ActionPaletteItemFromSource(e.OriginalSource as DependencyObject);
        actionPaletteDragStart = e.GetPosition(ActionPaletteList);
    }

    void ActionPaletteFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ActionPaletteItem item })
            return;
        ToggleActionPaletteFavorite(item.Action);
        e.Handled = true;
    }

    void ToggleActionPaletteFavorite(CatalogAction action)
    {
        string signature = ActionPaletteSignature(action.Kind, action.Value);
        bool removed = config.ActionPaletteFavorites.RemoveAll(existing => existing.Equals(signature, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!removed)
            config.ActionPaletteFavorites.Add(signature);
        config.ActionPaletteFavorites = [.. config.ActionPaletteFavorites.Distinct(StringComparer.OrdinalIgnoreCase)];
        PersistActionPaletteLibraryPreferences();
        RefreshActionPalette();
    }

    void RememberRecentPaletteAction(CatalogAction action)
    {
        string signature = ActionPaletteSignature(action.Kind, action.Value);
        config.ActionPaletteRecentActions.RemoveAll(existing => existing.Equals(signature, StringComparison.OrdinalIgnoreCase));
        config.ActionPaletteRecentActions.Insert(0, signature);
        if (config.ActionPaletteRecentActions.Count > 16)
            config.ActionPaletteRecentActions.RemoveRange(16, config.ActionPaletteRecentActions.Count - 16);
    }

    void PersistActionPaletteLibraryPreferences()
    {
        AppConfig persisted = store.Load();
        persisted.ActionPaletteFavorites = [.. config.ActionPaletteFavorites];
        persisted.ActionPaletteRecentActions = [.. config.ActionPaletteRecentActions];
        store.Save(persisted);
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

    void RunActionPaletteDrag(FrameworkElement container, ActionPaletteItem item, DataObject data, DragDropEffects allowedEffects = DragDropEffects.Copy)
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
            var row = new Grid { Margin = new Thickness(7, 0, 7, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(glyphFrame);
            Grid.SetColumn(labels, 1);
            row.Children.Add(labels);
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
            actionPaletteDragPreview = preview;
            feedback = (_, args) =>
            {
                var cursor = System.Windows.Forms.Cursor.Position;
                MoveActionPaletteDragPreview(preview, cursor.X, cursor.Y);
                args.UseDefaultCursors = false;
                args.Handled = true;
            };
            container.GiveFeedback += feedback;
            preview.Show();
            var initialCursor = System.Windows.Forms.Cursor.Position;
            MoveActionPaletteDragPreview(preview, initialCursor.X, initialCursor.Y);
            DragDrop.DoDragDrop(container, data, allowedEffects);
        }
        finally
        {
            if (feedback != null)
                container.GiveFeedback -= feedback;
            if (ReferenceEquals(actionPaletteDragPreview, preview))
                DismissActionPaletteDragPreview();
            else if (preview?.IsVisible == true)
            {
                try { preview.Hide(); } catch { }
                try { preview.Close(); } catch { }
            }
        }
    }

    void RepositionActionPaletteDragPreview()
    {
        if (actionPaletteDragPreview?.IsVisible != true)
            return;
        var cursor = System.Windows.Forms.Cursor.Position;
        MoveActionPaletteDragPreview(actionPaletteDragPreview, cursor.X, cursor.Y);
    }

    void MoveActionPaletteDragPreview(DeckDragPreviewWindow preview, int cursorX, int cursorY)
        => preview.MoveToPhysicalAvoiding(cursorX, cursorY, PhysicalScreenBounds(assignmentDropTarget));

    static Rect? PhysicalScreenBounds(FrameworkElement? element)
    {
        if (element?.IsVisible != true || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return null;
        try
        {
            Point topLeft = element.PointToScreen(new Point(0, 0));
            Point bottomRight = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
            return new Rect(topLeft, bottomRight);
        }
        catch
        {
            return null;
        }
    }

    void DismissActionPaletteDragPreview()
    {
        var preview = actionPaletteDragPreview;
        actionPaletteDragPreview = null;
        if (preview == null)
            return;
        // A parameterized drop opens another surface. Hide the drag material
        // synchronously on pointer release so it can never float above that dialog.
        try { preview.Hide(); } catch { }
        try { preview.Close(); } catch { }
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

    bool CanAssignPaletteAction(string input, CatalogAction action, AssignmentDropSlot slot = AssignmentDropSlot.ShortPress)
    {
        if (slot == AssignmentDropSlot.LongPress)
        {
            if (DeckPanelLayout.IsInputName(input) || action.Kind == ActionKind.Gesture)
                return false;
            string longKey = input[(input.LastIndexOf('+') + 1)..];
            return CanUseAssignmentDragKey(longKey, source: false)
                && InputAssignmentPolicy.CanExecuteLongPress(PaletteLongPressProbe(input), CurrentProfile.Mappings);
        }
        if (DeckPanelLayout.IsInputName(input))
            return selectedDeckLayout != null && action.Kind != ActionKind.Gesture;
        string key = input[(input.LastIndexOf('+') + 1)..];
        return InputAssignmentPolicy.CanAssignShortPress(input)
            && CanUseAssignmentDragKey(key, source: false)
            && (action.Kind != ActionKind.Gesture || InputAssignmentPolicy.SupportsGesture(input));
    }

    string PaletteShortPressDropUnavailableReason(CatalogAction action, string targetInput, string targetKey)
    {
        string unavailableInput = PaletteDropTargets(targetInput, targetKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(input => !CanAssignPaletteAction(input, action, AssignmentDropSlot.ShortPress))
            ?? targetInput;
        return InputAssignmentPolicy.ShortPressUnavailableReason(unavailableInput)
            ?? "この入力ではTAPを変更できません";
    }

    Mapping PaletteLongPressProbe(string input)
    {
        Mapping? existing = CurrentProfile.Mappings.LastOrDefault(candidate => candidate.Input.Equals(input, StringComparison.OrdinalIgnoreCase))
            ?? FindProfileMapping(config.Profiles, CurrentProfile.Name, input, MappingInterceptsInput);
        Mapping probe = existing?.Copy() ?? new Mapping();
        probe.Input = input;
        probe.Layer = AssignmentLayerName(input);
        return probe;
    }

    string PaletteLongPressUnavailableReason(string input, CatalogAction action)
    {
        if (DeckPanelLayout.IsInputName(input))
            return "Deckでは長押し不可";
        if (action.Kind == ActionKind.Gesture)
            return "ジェスチャーは長押し枠へ追加不可";
        return InputAssignmentPolicy.LongPressUnavailableReason(PaletteLongPressProbe(input), CurrentProfile.Mappings)
            ?? "このキーでは長押し不可";
    }

    bool CanAssignPaletteDropToSlot(CatalogAction action, string targetInput, string targetKey, AssignmentDropSlot slot)
    {
        if (slot == AssignmentDropSlot.ShortPress)
            return CanAssignPaletteAction(targetInput, action, slot);
        return PaletteDropTargets(targetInput, targetKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .All(input => CanAssignPaletteAction(input, action, slot));
    }

    string PaletteLongPressDropUnavailableReason(CatalogAction action, string targetInput, string targetKey)
    {
        string unavailableInput = PaletteDropTargets(targetInput, targetKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(input => !CanAssignPaletteAction(input, action, AssignmentDropSlot.LongPress))
            ?? targetInput;
        return PaletteLongPressUnavailableReason(unavailableInput, action);
    }

    IReadOnlyList<string> PaletteDropTargets(string targetInput, string targetKey)
    {
        bool targetIsSelected = MultiSelectToggle.IsChecked == true
            && (DeckPanelLayout.IsInputName(targetInput) ? multiSelectedInputs.Contains(targetInput) : multiSelectedInputs.Contains(targetKey));
        if (!targetIsSelected)
            return [targetInput];
        return [.. multiSelectedInputs.Select(MultiSelectionInput).Distinct(StringComparer.OrdinalIgnoreCase)];
    }

    bool ApplyPaletteActionDrop(CatalogAction action, string targetInput, string targetKey, AssignmentDropSlot slot = AssignmentDropSlot.ShortPress)
    {
        CatalogAction? resolvedAction = ResolvePaletteDropAction(action);
        if (resolvedAction == null)
            return false;
        action = resolvedAction;

        var requestedTargets = PaletteDropTargets(targetInput, targetKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (slot == AssignmentDropSlot.LongPress
            && requestedTargets.Any(input => !CanAssignPaletteAction(input, action, slot)))
            return false;
        var targets = requestedTargets.Where(input => CanAssignPaletteAction(input, action, slot)).ToArray();
        if (targets.Length == 0)
            return false;
        if (slot == AssignmentDropSlot.ShortPress
            && action.Kind == ActionKind.Gesture
            && targets.Any(input => input is "MouseRight" or "MouseBack" or "MouseForward")
            && !ConfirmDirectMouseGestureConflict(targets.First(input => input is "MouseRight" or "MouseBack" or "MouseForward")))
            return false;

        var snapshots = targets.Select(CapturePaletteAssignment).ToArray();
        foreach (string input in targets)
            ApplyPaletteActionToInput(action, input, slot);
        RememberRecentPaletteAction(action);

        string slotLabel = slot == AssignmentDropSlot.LongPress ? "の長押し" : string.Empty;
        actionPaletteUndoState = new ActionPaletteUndoState(snapshots, targets.Length == 1
            ? $"{DisplayInputName(targets[0])}{slotLabel}に割り当てました"
            : $"{targets.Length}個の入力{slotLabel}に割り当てました");
        ShowActionPaletteUndo(actionPaletteUndoState.Message);
        CommitPaletteAssignment(actionPaletteUndoState.Message, targets);
        if (!config.AutoSave)
            PersistActionPaletteLibraryPreferences();
        if (selected != null && targets.Contains(selected.Input, StringComparer.OrdinalIgnoreCase))
            SelectInput(selected.Input, false);
        PlayPaletteDropSuccess(targets);
        RefreshActionPalette();
        // Parameterized palette rows and the undo bar can complete template
        // work after the commit. Re-apply the durable key state last so the
        // long-press band cannot be replaced by a freshly realized template.
        ColorButtons();
        return true;
    }

    CatalogAction? ResolvePaletteDropAction(CatalogAction action)
    {
        if (action.ValueRequest == CatalogActionValueRequest.None)
            return action;

        DismissActionPaletteDragPreview();
        string? value = actionPaletteValueResolverForTest != null
            ? actionPaletteValueResolverForTest(action)
            : action.ValueRequest switch
            {
                CatalogActionValueRequest.Text => PromptMultilineText(
                    "文字列Actionを作成",
                    "送信する文字列を入力してください"),
                CatalogActionValueRequest.Launch => SelectPaletteLaunchTarget(),
                _ => null
            };
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string concreteValue = action.ValueRequest == CatalogActionValueRequest.Launch ? value.Trim() : value;
        return action with
        {
            Name = FriendlyActionValue(action.Kind, concreteValue),
            Value = concreteValue,
            ValueRequest = CatalogActionValueRequest.None
        };
    }

    string? SelectPaletteLaunchTarget()
    {
        var dialog = new ApplicationPickerWindow
        {
            Owner = this,
            Title = "割り当てるアプリ・パス・URLを指定"
        };
        dialog.SelectButton.Content = "この内容を割り当てる";
        return dialog.ShowDialog() == true ? dialog.SelectedPath : null;
    }

    bool ApplyPaletteMonitorDrop(DeckMonitorDefinition monitor, string targetInput)
    {
        if (selectedDeckLayout == null || !DeckPanelLayout.IsInputName(targetInput))
            return false;
        var snapshots = new[] { CapturePaletteAssignment(targetInput) };
        ApplyPaletteMonitorToInput(monitor, targetInput);
        RememberRecentPaletteAction(new CatalogAction(
            DeckMonitorCatalog.Category,
            monitor.Name,
            monitor.Description,
            ActionKind.Disabled,
            DeckMonitorActionPrefix + monitor.Id));
        actionPaletteUndoState = new ActionPaletteUndoState(snapshots, $"{DisplayInputName(targetInput)} に{monitor.Name}を配置しました");
        ShowActionPaletteUndo(actionPaletteUndoState.Message);
        CommitPaletteAssignment(actionPaletteUndoState.Message, [targetInput]);
        if (!config.AutoSave)
            PersistActionPaletteLibraryPreferences();
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
            ResetActionDropSuccessVisual(button, wave);
            return;
        }

        var accent = ThemeService.Color("AccentBrush");
        var highlight = MediaColor.FromRgb(
            (byte)((accent.R + byte.MaxValue) / 2),
            (byte)((accent.G + byte.MaxValue) / 2),
            (byte)((accent.B + byte.MaxValue) / 2));
        var waveBrush = new RadialGradientBrush
        {
            Center = new Point(.5, .5),
            GradientOrigin = new Point(.5, .5),
            RadiusX = .68,
            RadiusY = .68,
            GradientStops =
            {
                new GradientStop(MediaColor.FromArgb(242, highlight.R, highlight.G, highlight.B), 0),
                new GradientStop(MediaColor.FromArgb(220, accent.R, accent.G, accent.B), .58),
                new GradientStop(MediaColor.FromArgb(0, accent.R, accent.G, accent.B), 1)
            }
        };
        if (wave is Border border)
            border.Background = waveBrush;
        else if (wave is System.Windows.Shapes.Shape shape)
            shape.Fill = waveBrush;

        wave.RenderTransformOrigin = new Point(.5, .5);
        var waveScale = UiMotionService.MutableScale(wave, .06, .06);
        UiMotionService.StopAndSetDouble(waveScale, ScaleTransform.ScaleXProperty, .06);
        UiMotionService.StopAndSetDouble(waveScale, ScaleTransform.ScaleYProperty, .06);
        UiMotionService.StopAndSetDouble(wave, UIElement.OpacityProperty, 1);
        var waveDuration = TimeSpan.FromMilliseconds(ActionDropWaveDurationMs);
        var expansion = new DoubleAnimation(1.34, waveDuration)
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        var fade = new DoubleAnimationUsingKeyFrames
        {
            KeyFrames =
            {
                new DiscreteDoubleKeyFrame(.96, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new EasingDoubleKeyFrame(.92, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(160))) { EasingFunction = UiMotionService.ResponsiveEaseOut() },
                new EasingDoubleKeyFrame(.60, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(360))) { EasingFunction = UiMotionService.ResponsiveEaseOut() },
                new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(waveDuration)) { EasingFunction = UiMotionService.ResponsiveEaseOut() }
            },
            FillBehavior = FillBehavior.Stop
        };
        fade.Completed += (_, _) => UiMotionService.RunSafely("action-drop-wave-settle", () =>
        {
            UiMotionService.StopAndSetDouble(wave, UIElement.OpacityProperty, 0);
            UiMotionService.StopAndSetDouble(waveScale, ScaleTransform.ScaleXProperty, .06);
            UiMotionService.StopAndSetDouble(waveScale, ScaleTransform.ScaleYProperty, .06);
        });
        waveScale.BeginAnimation(ScaleTransform.ScaleXProperty, expansion, HandoffBehavior.SnapshotAndReplace);
        waveScale.BeginAnimation(ScaleTransform.ScaleYProperty, expansion.Clone(), HandoffBehavior.SnapshotAndReplace);
        wave.BeginAnimation(UIElement.OpacityProperty, fade, HandoffBehavior.SnapshotAndReplace);
    }

    static void ResetActionDropSuccessVisual(Button button, FrameworkElement wave)
    {
        UiMotionService.StopAndSetDouble(wave, UIElement.OpacityProperty, 0);
        if (wave.RenderTransform is ScaleTransform waveScale)
        {
            UiMotionService.StopAndSetDouble(waveScale, ScaleTransform.ScaleXProperty, .06);
            UiMotionService.StopAndSetDouble(waveScale, ScaleTransform.ScaleYProperty, .06);
        }
        var buttonScale = InputScaleTransform(button);
        UiMotionService.StopAndSetDouble(buttonScale, ScaleTransform.ScaleXProperty, 1);
        UiMotionService.StopAndSetDouble(buttonScale, ScaleTransform.ScaleYProperty, 1);
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

    void ApplyPaletteActionToInput(CatalogAction action, string input, AssignmentDropSlot slot)
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
        if (slot == AssignmentDropSlot.LongPress)
        {
            mapping.LongPressKind = action.Kind;
            mapping.LongPressValue = action.Value;
            NormalizeLongOnlyMapping(mapping);
        }
        else
        {
            mapping.Kind = action.Kind;
            mapping.Value = action.Value;
            mapping.DeckMonitor = string.Empty;
            ClearUnsupportedLongPress(mapping, collection);
            if (deckInput)
            {
                mapping.DeckIcon = DeckIconCatalog.SuggestedPresetId(action);
                mapping.DeckIconPath = string.Empty;
                mapping.DeckIconAutoAssigned = true;
            }
        }
        if (!deckInput)
            InputAssignmentPolicy.SanitizeMappings(CurrentProfile.Mappings);
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
