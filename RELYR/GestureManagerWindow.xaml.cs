using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace RELYR;

public partial class GestureManagerWindow : Window
{
    readonly List<GestureDefinition> gestures;
    readonly List<Profile> profiles;
    readonly List<MacroDefinition> macros;
    readonly IReadOnlyList<DeckLayoutDefinition> deckLayouts;
    readonly string keyboardLayout;
    bool loading;
    bool editingGestureName;
    GestureDefinition? editingGesture;
    string gestureNameBeforeEdit = "";

    internal IReadOnlyList<GestureDefinition> ResultGestures => gestures;
    internal IReadOnlyList<Profile> ResultProfiles => profiles;
    internal bool TitleBarUsesDarkMode
    {
        get; private set;
    }
    GestureDefinition? SelectedGesture => GestureList.SelectedItem as GestureDefinition;

    internal GestureManagerWindow(IReadOnlyList<GestureDefinition> source, IReadOnlyList<Profile> sourceProfiles, string keyboardLayout)
        : this(source, sourceProfiles, [], keyboardLayout) { }

    internal GestureManagerWindow(IReadOnlyList<GestureDefinition> source, IReadOnlyList<Profile> sourceProfiles, IReadOnlyList<MacroDefinition> sourceMacros, string keyboardLayout, IReadOnlyList<DeckLayoutDefinition>? sourceDeckLayouts = null)
    {
        gestures = [.. source.Select(CloneGesture)];
        profiles = [.. sourceProfiles.Select(CloneProfile)];
        macros = [.. sourceMacros.Select(CloneMacro)];
        deckLayouts = sourceDeckLayouts ?? [];
        this.keyboardLayout = keyboardLayout;
        InitializeComponent();
        MainWindow.FollowWindowsTitleBarTheme(this, value => TitleBarUsesDarkMode = value);
        RefreshGestures();
    }

    void RefreshGestures(string? selectedName = null)
    {
        loading = true;
        GestureList.ItemsSource = null;
        GestureList.ItemsSource = gestures;
        GestureList.SelectedItem = gestures.FirstOrDefault(x => x.Name == (selectedName ?? SelectedGesture?.Name)) ?? gestures.FirstOrDefault();
        loading = false;
        RefreshEditor();
    }

    void GestureSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!loading)
            RefreshEditor();
    }

    void RefreshEditor()
    {
        var gesture = SelectedGesture;
        bool wasLoading = loading;
        loading = true;
        GestureEditor.IsEnabled = gesture != null;
        RenameGestureButton.IsEnabled = gesture != null;
        DeleteGestureButton.IsEnabled = gesture != null;
        GestureTitle.Text = gesture?.Name ?? "ジェスチャーを追加してください";
        GestureThresholdBox.Text = (gesture?.GestureThresholdPixels ?? 12).ToString();
        LockGestureCursorBox.IsChecked = gesture?.LockCursorDuringGesture ?? true;
        UpActionText.Text = Display(gesture?.UpKind ?? ActionKind.None, gesture?.UpValue ?? "");
        DownActionText.Text = Display(gesture?.DownKind ?? ActionKind.None, gesture?.DownValue ?? "");
        LeftActionText.Text = Display(gesture?.LeftKind ?? ActionKind.None, gesture?.LeftValue ?? "");
        RightActionText.Text = Display(gesture?.RightKind ?? ActionKind.None, gesture?.RightValue ?? "");
        CenterActionText.Text = Display(gesture?.CenterKind ?? ActionKind.None, gesture?.CenterValue ?? "");
        loading = wasLoading;
    }

    void GestureThresholdChanged(object sender, TextChangedEventArgs e)
    {
        if (loading || SelectedGesture is not { } gesture
            || !int.TryParse(GestureThresholdBox.Text, out int threshold)
            || threshold is < 3 or > 100)
            return;
        gesture.GestureThresholdPixels = threshold;
        ShowStatus($"方向を確定する移動量を{threshold}pxにしました。");
    }

    void LockGestureCursorChanged(object sender, RoutedEventArgs e)
    {
        if (loading || SelectedGesture is not { } gesture)
            return;
        gesture.LockCursorDuringGesture = LockGestureCursorBox.IsChecked == true;
        ShowStatus(gesture.LockCursorDuringGesture ? "カーソルを固定します。" : "カーソルを動かします。");
    }

    void AddGesture_Click(object sender, RoutedEventArgs e)
    {
        string? name = PromptName("ジェスチャーを追加", "新しいジェスチャー名", UniqueName("新しいジェスチャー"));
        if (string.IsNullOrWhiteSpace(name))
            return;
        if (gestures.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowStatus("同じ名前のジェスチャーがあります。", true);
            return;
        }
        gestures.Add(new GestureDefinition { Name = name });
        RefreshGestures(name);
        ShowStatus("ジェスチャーを追加しました。");
    }

    void RenameGesture_Click(object sender, RoutedEventArgs e)
    {
        var gesture = SelectedGesture;
        if (gesture == null)
            return;
        editingGesture = gesture;
        gestureNameBeforeEdit = gesture.Name;
        editingGestureName = true;
        GestureTitle.IsReadOnly = false;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() =>
        {
            GestureTitle.Focus();
            GestureTitle.SelectAll();
        }));
    }

    void GestureTitle_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (CommitGestureName(true))
                GestureList.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CancelGestureNameEdit();
            e.Handled = true;
        }
    }

    void GestureTitle_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (editingGestureName)
            CommitGestureName(true);
    }

    bool CommitGestureName(bool showStatus)
    {
        if (!editingGestureName || editingGesture == null)
            return true;
        string name = GestureTitle.Text.Trim();
        if (name.Length == 0 || gestures.Any(x => !ReferenceEquals(x, editingGesture) && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            GestureTitle.Text = gestureNameBeforeEdit;
            ShowStatus(name.Length == 0 ? "ジェスチャー名を入力してください。" : "同じ名前のジェスチャーがあります。", true);
            GestureTitle.Focus();
            GestureTitle.SelectAll();
            return false;
        }
        string old = editingGesture.Name;
        editingGesture.Name = name;
        if (!old.Equals(name, StringComparison.Ordinal))
            RenameReferences(profiles, old, name);
        EndGestureNameEdit();
        RefreshGestures(name);
        if (showStatus)
            ShowStatus("名前と割り当てからの参照を変更しました。");
        return true;
    }

    void CancelGestureNameEdit()
    {
        GestureTitle.Text = gestureNameBeforeEdit;
        EndGestureNameEdit();
        RefreshEditor();
    }

    void EndGestureNameEdit()
    {
        editingGestureName = false;
        editingGesture = null;
        GestureTitle.IsReadOnly = true;
    }

    void DeleteGesture_Click(object sender, RoutedEventArgs e)
    {
        var gesture = SelectedGesture;
        if (gesture == null)
            return;
        if (AppDialog.Show(this, LocalizationService.Format("「{0}」を削除しますか？\nこのジェスチャーを参照している長押し割り当ても解除されます。", gesture.Name), "ジェスチャーを削除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        string deleted = gesture.Name;
        gestures.Remove(gesture);
        ClearReferences(profiles, deleted);
        RefreshGestures();
        ShowStatus("ジェスチャーと参照していた長押し割り当てを削除しました。");
    }

    void SelectAction_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGesture == null || sender is not System.Windows.Controls.Button { Tag: string slot } button)
            return;
        var menu = CreateActionTypeMenu(button, slot);
        menu.IsOpen = true;
    }

    internal ContextMenu CreateActionTypeMenu(Button button, string slot)
    {
        var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var choice in SupportedActionChoices)
        {
            var header = new GestureActionChoiceHeader(choice.Icon, choice.Label, ThemeService.Brush(choice.IconBrush));
            var item = new MenuItem { Header = header, HeaderTemplate = (DataTemplate)FindResource("GestureActionChoiceHeaderTemplate"), Tag = choice.Kind };
            item.Click += (_, _) => ChooseAction(slot, choice.Kind);
            menu.Items.Add(item);
        }
        return menu;
    }

    void ChooseAction(string slot, ActionKind requestedKind)
    {
        if (SelectedGesture == null)
            return;
        ActionKind kind = requestedKind;
        string? value = requestedKind switch
        {
            ActionKind.Key => PickKey(),
            ActionKind.Profile => PickNamedValue("プロファイルを選択", "切り替えるプロファイル", profiles.Select(x => x.Name)),
            ActionKind.Shortcut => PickShortcut(out kind),
            ActionKind.Text => PromptText(),
            ActionKind.Launch => PickApplication(),
            ActionKind.Macro => PickNamedValue("マクロを選択", "実行するマクロ", macros.Select(x => x.Name)),
            _ => null
        };
        if (value == null)
            return;
        SetAction(SelectedGesture, slot, kind, value);
        RefreshEditor();
        ShowStatus($"{SlotLabel(slot)}の動作を設定しました。");
    }

    string? PickKey()
    {
        string? result = null;
        var picker = new MacroInputPickerWindow(keyboardLayout) { Owner = this };
        picker.InputChosen += value => { result = value; picker.Close(); };
        picker.ShowDialog();
        return result;
    }

    string? PickShortcut(out ActionKind kind)
    {
        kind = ActionKind.Shortcut;
        var picker = new ActionPickerWindow(profiles, keyboardLayout, null, false, deckLayouts: deckLayouts) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedAction is not { } action || action.Kind == ActionKind.Gesture)
            return null;
        kind = action.Kind;
        return action.Value;
    }

    string? PickApplication()
    {
        var picker = new ApplicationPickerWindow { Owner = this };
        return picker.ShowDialog() == true ? picker.SelectedPath : null;
    }

    string? PromptText()
    {
        string? value = PromptName("文字列を入力", "入力する文字列", "", false);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    string? PickNamedValue(string title, string heading, IEnumerable<string> values)
    {
        var choices = values.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (choices.Count == 0)
        {
            ShowStatus("選択できる項目がありません。", true);
            return null;
        }
        var dialog = new Window { Title = title, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Width = 460, Height = 420, MinHeight = 320, Background = ThemeService.Brush("SurfaceBackground"), Foreground = ThemeService.Brush("PrimaryText"), ShowInTaskbar = false };
        MainWindow.FollowWindowsTitleBarTheme(dialog);
        var grid = new Grid { Margin = new Thickness(22) };
        grid.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        grid.RowDefinitions.Add(new());
        grid.RowDefinitions.Add(new()
        {
            Height = GridLength.Auto
        });
        var label = new TextBlock { Text = heading, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
        grid.Children.Add(label);
        var list = new System.Windows.Controls.ListBox { ItemsSource = choices, Background = ThemeService.Brush("InputBackground"), Foreground = ThemeService.Brush("PrimaryText"), BorderBrush = ThemeService.Brush("BorderBrush") };
        Grid.SetRow(list, 1);
        grid.Children.Add(list);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "キャンセル", Width = 112, Height = 40, Margin = new Thickness(6, 0, 0, 0), Style = (Style)System.Windows.Application.Current.FindResource("AppButtonStyle") };
        var ok = new Button { Content = "選択", Width = 112, Height = 40, Margin = new Thickness(6, 0, 0, 0), IsDefault = true, IsEnabled = false, Style = (Style)System.Windows.Application.Current.FindResource("AccentAppButtonStyle") };
        list.SelectionChanged += (_, _) => ok.IsEnabled = list.SelectedItem != null;
        list.MouseDoubleClick += (_, _) => { if (list.SelectedItem != null) dialog.DialogResult = true; };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        ok.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);
        dialog.Content = grid;
        return dialog.ShowDialog() == true ? list.SelectedItem as string : null;
    }

    void ClearAction_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGesture == null || sender is not System.Windows.Controls.Button { Tag: string slot })
            return;
        SetAction(SelectedGesture, slot, ActionKind.None, "");
        RefreshEditor();
        ShowStatus($"{SlotLabel(slot)}の動作をクリアしました。");
    }

    static void SetAction(GestureDefinition gesture, string slot, ActionKind kind, string value)
    {
        switch (slot)
        {
            case "Up":
                gesture.UpKind = kind;
                gesture.UpValue = value;
                break;
            case "Down":
                gesture.DownKind = kind;
                gesture.DownValue = value;
                break;
            case "Left":
                gesture.LeftKind = kind;
                gesture.LeftValue = value;
                break;
            case "Right":
                gesture.RightKind = kind;
                gesture.RightValue = value;
                break;
            case "Center":
                gesture.CenterKind = kind;
                gesture.CenterValue = value;
                break;
        }
    }

    internal static readonly GestureActionChoice[] SupportedActionChoices =
    [
        new(ActionKind.Key,"⌨","別のキー","ActionKeyIconBrush"),
        new(ActionKind.Profile,"⇄","プロファイル","ActionProfileIconBrush"),
        new(ActionKind.Shortcut,"↗","ショートカット","ActionShortcutIconBrush"),
        new(ActionKind.Text,"T","文字列","ActionTextIconBrush"),
        new(ActionKind.Launch,"▱","アプリ・パス","ActionLaunchIconBrush"),
        new(ActionKind.Macro,"⌘","マクロ","ActionMacroIconBrush")
    ];

    internal sealed record GestureActionChoice(ActionKind Kind, string Icon, string Label, string IconBrush);
    sealed record GestureActionChoiceHeader(string Icon, string Label, System.Windows.Media.Brush IconBrush)
    {
        public override string ToString() => Label;
    }

    internal static void RenameReferences(IEnumerable<Profile> profiles, string oldName, string newName)
    {
        foreach (var map in profiles.SelectMany(x => x.Mappings))
        {
            if (map.Kind == ActionKind.Gesture && map.Value.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                map.Value = newName;
            if (map.LongPressKind == ActionKind.Gesture && map.LongPressValue.Equals(oldName, StringComparison.OrdinalIgnoreCase))
                map.LongPressValue = newName;
        }
    }

    internal static void ClearReferences(IEnumerable<Profile> profiles, string deletedName)
    {
        foreach (var map in profiles.SelectMany(x => x.Mappings))
        {
            if (map.Kind == ActionKind.Gesture && map.Value.Equals(deletedName, StringComparison.OrdinalIgnoreCase))
            {
                map.Kind = ActionKind.None;
                map.Value = "";
            }
            if (map.LongPressKind == ActionKind.Gesture && map.LongPressValue.Equals(deletedName, StringComparison.OrdinalIgnoreCase))
            {
                map.LongPressKind = ActionKind.None;
                map.LongPressValue = "";
            }
        }
    }

    void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedGesture != null
            && (!int.TryParse(GestureThresholdBox.Text, out int threshold) || threshold is < 3 or > 100))
        {
            ShowStatus("移動量は3～100pxで入力してください。", true);
            GestureThresholdBox.Focus();
            GestureThresholdBox.SelectAll();
            return;
        }
        DialogResult = true;
    }
    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
    void ShowStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = ThemeService.Brush(error ? "DangerBrush" : "AccentTextBrush");
    }
    string UniqueName(string basis)
    {
        string name = basis;
        int number = 2;
        while (gestures.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            name = $"{basis} {number++}";
        return name;
    }
    static string SlotLabel(string slot) => slot switch { "Up" => "上", "Down" => "下", "Left" => "左", "Right" => "右", _ => "短押し" };
    static string Display(ActionKind kind, string value) => kind == ActionKind.None ? "未設定" : kind == ActionKind.Disabled ? "無効化" : $"{MainWindow.ActionKindDisplayName(kind)}：{(kind == ActionKind.Mouse ? MainWindow.DisplayActionValue(kind, value) : value)}";

    string? PromptName(string title, string label, string initial, bool trim = true)
    {
        var dialog = new Window { Title = title, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Width = 460, Height = 220, ResizeMode = ResizeMode.NoResize, Background = ThemeService.Brush("SurfaceBackground"), Foreground = ThemeService.Brush("PrimaryText"), ShowInTaskbar = false };
        MainWindow.FollowWindowsTitleBarTheme(dialog);
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 9) });
        var box = new System.Windows.Controls.TextBox { Text = initial, FontSize = 15, Height = 40, Padding = new Thickness(12, 0, 12, 0), Background = ThemeService.Brush("InputBackground"), Foreground = ThemeService.Brush("PrimaryText"), BorderBrush = ThemeService.Brush("BorderBrush"), VerticalContentAlignment = VerticalAlignment.Center };
        panel.Children.Add(box);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
        var cancel = new System.Windows.Controls.Button { Content = "キャンセル", Width = 112, Height = 40, Margin = new Thickness(6, 0, 0, 0), Style = (Style)System.Windows.Application.Current.FindResource("AppButtonStyle") };
        var ok = new System.Windows.Controls.Button { Content = "決定", Width = 112, Height = 40, Margin = new Thickness(6, 0, 0, 0), Style = (Style)System.Windows.Application.Current.FindResource("AccentAppButtonStyle"), IsDefault = true };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        ok.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        panel.Children.Add(buttons);
        dialog.Content = panel;
        dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return dialog.ShowDialog() == true ? (trim ? box.Text.Trim() : box.Text) : null;
    }

    static GestureDefinition CloneGesture(GestureDefinition x) => new() { Name = x.Name, GestureThresholdPixels = x.GestureThresholdPixels, LockCursorDuringGesture = x.LockCursorDuringGesture, UpKind = x.UpKind, UpValue = x.UpValue, DownKind = x.DownKind, DownValue = x.DownValue, LeftKind = x.LeftKind, LeftValue = x.LeftValue, RightKind = x.RightKind, RightValue = x.RightValue, CenterKind = x.CenterKind, CenterValue = x.CenterValue };
    static MacroDefinition CloneMacro(MacroDefinition x) => new() { Id = x.Id, Name = x.Name, Steps = [.. x.Steps.Select(step => new MacroStep { Event = step.Event, DelayMs = step.DelayMs, RecordedActionKind = step.RecordedActionKind, RecordedActionValue = step.RecordedActionValue })] };
    static Profile CloneProfile(Profile profile) => new() { Name = profile.Name, DefaultDeckLayoutId = profile.DefaultDeckLayoutId, AutoSwitchEnabled = profile.AutoSwitchEnabled, AutoSwitchApplications = [.. profile.AutoSwitchApplications], Mappings = [.. profile.Mappings.Select(CloneMapping)] };
    static Mapping CloneMapping(Mapping x) => new() { Input = x.Input, Kind = x.Kind, Value = x.Value, LongPressKind = x.LongPressKind, LongPressValue = x.LongPressValue, DragValue = x.DragValue, DragEndValue = x.DragEndValue, LongPressMs = x.LongPressMs, Application = x.Application, Layer = x.Layer, Description = x.Description, DeckColor = x.DeckColor, DeckFilePath = x.DeckFilePath, DeckIcon = x.DeckIcon, DeckIconPath = x.DeckIconPath, DeckIconAutoAssigned = x.DeckIconAutoAssigned, DeckMonitor = x.DeckMonitor };
}
