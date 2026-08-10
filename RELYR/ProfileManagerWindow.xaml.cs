using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RELYR;

internal sealed record AutoSwitchApplicationInfo(string Label, string Value);

public partial class ProfileManagerWindow : Window
{
    readonly List<Profile> profiles;
    bool loading;
    List<Mapping>? copiedAssignments;
    string activeProfile;

    internal IReadOnlyList<Profile> ResultProfiles => profiles;
    internal string ResultActiveProfile => activeProfile;
    internal bool ResultAutoSwitchProfilesByCursor => CursorProfileSwitchBox.IsChecked == true;
    internal bool TitleBarUsesDarkMode
    {
        get; private set;
    }
    Profile? SelectedProfile => ProfileList.SelectedItem as Profile;

    internal ProfileManagerWindow(IReadOnlyList<Profile> source, string selectedProfile, bool autoSwitchProfilesByCursor = true)
    {
        profiles = [.. source.Select(CloneProfile)];
        if (profiles.Count == 0)
            profiles.Add(new Profile { Name = "標準" });
        activeProfile = profiles.Any(x => x.Name == selectedProfile) ? selectedProfile : profiles[0].Name;
        InitializeComponent();
        CursorProfileSwitchBox.IsChecked = autoSwitchProfilesByCursor;
        MainWindow.FollowWindowsTitleBarTheme(this, value => TitleBarUsesDarkMode = value);
        RefreshProfiles(activeProfile);
        RefreshRunningApplications();
    }

    void RefreshProfiles(string? selectName = null)
    {
        loading = true;
        ProfileList.ItemsSource = null;
        ProfileList.ItemsSource = profiles;
        ProfileList.SelectedItem = profiles.FirstOrDefault(x => x.Name == (selectName ?? activeProfile)) ?? profiles[0];
        loading = false;
        RefreshSelectedProfile();
    }
    void ProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (loading || SelectedProfile == null)
            return;
        activeProfile = SelectedProfile.Name;
        RefreshSelectedProfile();
    }
    void RefreshSelectedProfile()
    {
        var profile = SelectedProfile;
        if (profile == null)
            return;
        loading = true;
        SelectedProfileTitle.Text = profile.Name;
        AutoSwitchBox.IsEnabled = !ReferenceEquals(profile, profiles[0]);
        AutoSwitchBox.IsChecked = !ReferenceEquals(profile, profiles[0]) && profile.AutoSwitchEnabled;
        AutoSwitchBox.Content = AutoSwitchBox.IsChecked == true ? "● 自動切替 オン" : "○ 自動切替 オフ";
        AssignedApplicationList.ItemsSource = null;
        AssignedApplicationList.ItemsSource = profile.AutoSwitchApplications.OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase).ToList();
        loading = false;
        StatusText.Text = ReferenceEquals(profile, profiles[0]) ? "標準プロファイルは、自動切替対象がない場合の戻り先です。" : $"割り当て {profile.Mappings.Count}件 / 対象アプリ {profile.AutoSwitchApplications.Count}件";
    }
    void AddProfileManager_Click(object sender, RoutedEventArgs e)
    {
        string? name = PromptName("プロファイルを追加", "新しいプロファイル名", UniqueName("新しいプロファイル"));
        if (string.IsNullOrWhiteSpace(name))
            return;
        if (profiles.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowStatus("同じ名前のプロファイルがあります。", true);
            return;
        }
        var profile = new Profile { Name = name };
        profiles.Add(profile);
        activeProfile = name;
        RefreshProfiles(name);
        ShowStatus("プロファイルを追加しました。必要ならコピーした割り当てを貼り付けてください。");
    }
    void RenameProfileManager_Click(object sender, RoutedEventArgs e)
    {
        var profile = SelectedProfile;
        if (profile == null)
            return;
        if (ReferenceEquals(profile, profiles[0]))
        {
            ShowStatus("標準プロファイルの名前は変更できません。", true);
            return;
        }
        string old = profile.Name;
        string? name = PromptName("プロファイル名を変更", "新しい名前", old);
        if (string.IsNullOrWhiteSpace(name) || name == old)
            return;
        if (profiles.Any(x => !ReferenceEquals(x, profile) && x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowStatus("同じ名前のプロファイルがあります。", true);
            return;
        }
        profile.Name = name;
        if (activeProfile == old)
            activeProfile = name;
        foreach (var map in profiles.SelectMany(x => x.Mappings))
        {
            if (map.Kind == ActionKind.Profile && map.Value == old)
                map.Value = name;
            if (map.LongPressKind == ActionKind.Profile && map.LongPressValue == old)
                map.LongPressValue = name;
        }
        RefreshProfiles(name);
        ShowStatus("名前を変更しました。");
    }
    void DeleteProfileManager_Click(object sender, RoutedEventArgs e)
    {
        var profile = SelectedProfile;
        if (profile == null)
            return;
        if (ReferenceEquals(profile, profiles[0]))
        {
            ShowStatus("標準プロファイルは削除できません。", true);
            return;
        }
        if (AppDialog.Show(this, $"「{profile.Name}」を削除しますか？\nこの操作は『変更を反映』を押すまで確定しません。", "プロファイルを削除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        string deleted = profile.Name;
        profiles.Remove(profile);
        foreach (var map in profiles.SelectMany(x => x.Mappings))
        {
            if (map.Kind == ActionKind.Profile && map.Value == deleted)
            {
                map.Kind = ActionKind.None;
                map.Value = "";
            }
            if (map.LongPressKind == ActionKind.Profile && map.LongPressValue == deleted)
            {
                map.LongPressKind = ActionKind.None;
                map.LongPressValue = "";
            }
        }
        activeProfile = profiles[0].Name;
        RefreshProfiles(activeProfile);
        ShowStatus("プロファイルを削除しました。");
    }
    void CopyAssignments_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null)
            return;
        copiedAssignments = [.. SelectedProfile.Mappings.Select(CloneMapping)];
        ShowStatus($"「{SelectedProfile.Name}」の割り当て {copiedAssignments.Count}件をコピーしました。");
    }
    void PasteAssignments_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null)
            return;
        if (copiedAssignments == null)
        {
            ShowStatus("先にコピー元プロファイルの割り当てをコピーしてください。", true);
            return;
        }
        if (AppDialog.Show(this, $"「{SelectedProfile.Name}」の割り当てを、コピーした {copiedAssignments.Count}件で置き換えますか？", "割り当てを貼り付け", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        SelectedProfile.Mappings = [.. copiedAssignments.Select(CloneMapping)];
        RefreshSelectedProfile();
        ShowStatus("割り当てを貼り付けました。");
    }
    void ProfileList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(ProfileList, e.OriginalSource as DependencyObject) is ListBoxItem item)
            item.IsSelected = true;
    }
    void AutoSwitchChanged(object sender, RoutedEventArgs e)
    {
        if (loading || SelectedProfile == null || ReferenceEquals(SelectedProfile, profiles[0]))
            return;
        SelectedProfile.AutoSwitchEnabled = AutoSwitchBox.IsChecked == true;
        AutoSwitchBox.Content = SelectedProfile.AutoSwitchEnabled ? "● 自動切替 オン" : "○ 自動切替 オフ";
        RefreshProfileListWithoutChangingSelection();
    }
    void RefreshProfileListWithoutChangingSelection()
    {
        string name = SelectedProfile?.Name ?? activeProfile;
        loading = true;
        ProfileList.Items.Refresh();
        ProfileList.SelectedItem = profiles.FirstOrDefault(x => x.Name == name);
        loading = false;
        RefreshSelectedProfile();
    }
    void RefreshRunningApplications_Click(object sender, RoutedEventArgs e) => RefreshRunningApplications();
    void RefreshRunningApplications()
    {
        var apps = new List<AutoSwitchApplicationInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(process.MainWindowTitle))
                        apps.Add(new($"{process.MainWindowTitle}  —  {process.ProcessName}.exe", process.ProcessName + ".exe"));
                }
                catch { }
            }
        }
        RunningApplicationList.ItemsSource = apps.GroupBy(x => x.Value, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    void AddRunningApplication_Click(object sender, RoutedEventArgs e)
    {
        if (RunningApplicationList.SelectedItem is AutoSwitchApplicationInfo app)
            AddAutoSwitchApplication(app.Value);
        else
            ShowStatus("右側の一覧からアプリを選択してください。", true);
    }
    void RemoveAutoSwitchApplication_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null || AssignedApplicationList.SelectedItem is not string app)
            return;
        SelectedProfile.AutoSwitchApplications.RemoveAll(x => x.Equals(app, StringComparison.OrdinalIgnoreCase));
        RefreshSelectedProfile();
        ShowStatus(app + " を対象から外しました。");
    }
    void AddInstalledApplication_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ApplicationPickerWindow(true) { Owner = this };
        if (picker.ShowDialog() != true || picker.SelectedApplication == null)
            return;
        string? executable = ExecutableNameForAutoSwitch(picker.SelectedApplication);
        if (string.IsNullOrWhiteSpace(executable))
        {
            ShowStatus("このアプリの実行ファイル名を取得できませんでした。起動中のアプリ一覧から選んでください。", true);
            return;
        }
        AddAutoSwitchApplication(executable);
    }
    void AddAutoSwitchApplication(string executable)
    {
        var profile = SelectedProfile;
        if (profile == null)
            return;
        if (ReferenceEquals(profile, profiles[0]))
        {
            ShowStatus("標準プロファイルは自動切替の戻り先です。", true);
            return;
        }
        if (!profile.AutoSwitchApplications.Contains(executable, StringComparer.OrdinalIgnoreCase))
            profile.AutoSwitchApplications.Add(executable);
        profile.AutoSwitchEnabled = true;
        RefreshProfileListWithoutChangingSelection();
        ShowStatus(executable + " を自動切替の対象に追加しました。");
    }
    internal static string? ExecutableNameForAutoSwitch(InstalledApplicationInfo app)
    {
        string path = app.LaunchPath;
        if (path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            path = ShortcutService.ResolveShortcutTarget(path) ?? "";
        return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileName(path) : null;
    }
    void Apply_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
    void ShowStatus(string message, bool error = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = ThemeService.Brush(error ? "DangerBrush" : "AccentBrush");
    }
    string UniqueName(string basis)
    {
        string name = basis;
        int number = 2;
        while (profiles.Any(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            name = $"{basis} {number++}";
        return name;
    }
    string? PromptName(string title, string label, string initial)
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
        return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }
    static Profile CloneProfile(Profile profile) => new() { Name = profile.Name, DefaultDeckLayoutId = profile.DefaultDeckLayoutId, AutoSwitchEnabled = profile.AutoSwitchEnabled, AutoSwitchApplications = [.. profile.AutoSwitchApplications], Mappings = [.. profile.Mappings.Select(CloneMapping)] };
    static Mapping CloneMapping(Mapping x) => new() { Input = x.Input, Kind = x.Kind, Value = x.Value, LongPressKind = x.LongPressKind, LongPressValue = x.LongPressValue, DragValue = x.DragValue, DragEndValue = x.DragEndValue, LongPressMs = x.LongPressMs, Application = x.Application, Layer = x.Layer, Description = x.Description, DeckColor = x.DeckColor, DeckFilePath = x.DeckFilePath };
}
