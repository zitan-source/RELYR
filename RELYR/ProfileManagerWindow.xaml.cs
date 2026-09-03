using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RELYR;

public partial class ProfileManagerWindow : Window
{
    readonly List<Profile> profiles;
    bool loading;
    bool showingInstalledApplications;
    bool installedApplicationsLoaded;
    bool installedApplicationsLoading;
    List<Mapping>? copiedAssignments;
    List<ApplicationDisplayItem> runningApplications = [];
    List<ApplicationDisplayItem> installedApplications = [];
    string activeProfile;

    internal IReadOnlyList<Profile> ResultProfiles => profiles;
    internal string ResultActiveProfile => activeProfile;
    internal bool TitleBarUsesDarkMode
    {
        get; private set;
    }
    Profile? SelectedProfile => ProfileList.SelectedItem as Profile;

    internal ProfileManagerWindow(IReadOnlyList<Profile> source, string selectedProfile)
    {
        profiles = [.. source.Select(CloneProfile)];
        if (profiles.Count == 0)
            profiles.Add(new Profile { Name = "標準" });
        activeProfile = profiles.Any(x => x.Name == selectedProfile) ? selectedProfile : profiles[0].Name;
        InitializeComponent();
        MainWindow.FollowWindowsTitleBarTheme(this, value => TitleBarUsesDarkMode = value);
        RefreshProfiles(activeProfile);
        RefreshRunningApplications();
        RunningApplicationsTab.IsChecked = true;
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
        SelectedProfileTitle.Text = LocalizationService.DisplayGeneratedName(profile.Name);
        AutoSwitchBox.IsEnabled = !ReferenceEquals(profile, profiles[0]);
        AutoSwitchBox.IsChecked = !ReferenceEquals(profile, profiles[0]) && profile.AutoSwitchEnabled;
        AssignedApplicationList.ItemsSource = null;
        AssignedApplicationList.ItemsSource = profile.AutoSwitchApplications
            .OrderBy(x => x, StringComparer.CurrentCultureIgnoreCase)
            .Select(ApplicationDisplayItem.FromExecutable)
            .ToList();
        loading = false;
        UpdateCommandStates();
        StatusText.Text = LocalizationService.Text(ReferenceEquals(profile, profiles[0]) ? "標準プロファイルは、自動切替対象がない場合の戻り先です。" : $"割り当て {profile.Mappings.Count}件 / 対象アプリ {profile.AutoSwitchApplications.Count}件");
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
        if (AppDialog.Show(this, LocalizationService.Format("「{0}」を削除しますか？\nこの操作は『変更を反映』を押すまで確定しません。", profile.Name), "プロファイルを削除", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
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
        UpdateCommandStates();
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
        if (AppDialog.Show(this, LocalizationService.Format("「{0}」の割り当てを、コピーした {1}件で置き換えますか？", SelectedProfile.Name, copiedAssignments.Count), "割り当てを貼り付け", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
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
    async void RefreshApplications_Click(object sender, RoutedEventArgs e)
    {
        if (showingInstalledApplications)
            await LoadInstalledApplicationsAsync(true);
        else
            RefreshRunningApplications();
    }
    async void ApplicationSourceChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || sender is not System.Windows.Controls.RadioButton { IsChecked: true } selected)
            return;
        showingInstalledApplications = ReferenceEquals(selected, InstalledApplicationsTab);
        if (showingInstalledApplications)
            await LoadInstalledApplicationsAsync(false);
        else
        {
            UpdateInstalledApplicationsLoadingState();
            RefreshAvailableApplications();
        }
    }
    void RefreshRunningApplications()
    {
        var apps = new List<ApplicationDisplayItem>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(process.MainWindowTitle))
                    {
                        string executable = process.ProcessName + ".exe";
                        apps.Add(new(process.MainWindowTitle, executable, ApplicationIconService.TryGetProcessPath(process)));
                    }
                }
                catch { }
            }
        }
        runningApplications = [.. apps.GroupBy(x => x.Value, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase)];
        if (!showingInstalledApplications)
            RefreshAvailableApplications();
    }
    async Task LoadInstalledApplicationsAsync(bool force)
    {
        if (installedApplicationsLoaded && !force)
        {
            RefreshAvailableApplications();
            return;
        }

        RefreshApplicationsButton.IsEnabled = false;
        InstalledApplicationsTab.IsEnabled = false;
        installedApplicationsLoading = true;
        UpdateInstalledApplicationsLoadingState();
        ShowStatus("インストール済みのアプリを読み込んでいます。");
        try
        {
            var discovered = await Task.Run(ApplicationPickerWindow.DiscoverApplications);
            if (!IsLoaded)
                return;
            installedApplications = [.. discovered
                .Select(app => (Application: app, Executable: ExecutableNameForAutoSwitch(app)))
                .Where(item => !string.IsNullOrWhiteSpace(item.Executable))
                .GroupBy(item => item.Executable!, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.OrderBy(item => item.Application.Name.Length).First())
                .Select(item => new ApplicationDisplayItem(item.Application.Name, item.Executable!, item.Application.LaunchPath))
                .OrderBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase)];
            installedApplicationsLoaded = true;
            ShowStatus($"インストール済みのアプリ {installedApplications.Count}件を表示しています。");
            if (showingInstalledApplications)
                RefreshAvailableApplications();
        }
        catch (Exception ex)
        {
            ShowStatus("インストール済みのアプリを読み込めませんでした: " + ex.Message, true);
        }
        finally
        {
            installedApplicationsLoading = false;
            UpdateInstalledApplicationsLoadingState();
            if (IsLoaded)
            {
                InstalledApplicationsTab.IsEnabled = true;
                RefreshApplicationsButton.IsEnabled = true;
            }
        }
    }
    void UpdateInstalledApplicationsLoadingState()
    {
        if (!IsInitialized)
            return;
        bool visible = showingInstalledApplications && installedApplicationsLoading;
        InstalledApplicationsLoadingPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        RunningApplicationList.Opacity = visible ? 0.24 : 1;
        RunningApplicationList.IsHitTestVisible = !visible;
    }
    internal void SetInstalledApplicationsLoadingStateForTest(bool showingInstalled, bool loadingInstalled)
    {
        showingInstalledApplications = showingInstalled;
        installedApplicationsLoading = loadingInstalled;
        UpdateInstalledApplicationsLoadingState();
    }
    void RefreshAvailableApplications()
    {
        RunningApplicationList.ItemsSource = null;
        RunningApplicationList.ItemsSource = showingInstalledApplications ? installedApplications : runningApplications;
        UpdateCommandStates();
    }
    void ApplicationSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateCommandStates();
    void UpdateCommandStates()
    {
        if (!IsInitialized || profiles.Count == 0)
            return;
        var profile = SelectedProfile;
        bool editable = profile != null && !ReferenceEquals(profile, profiles[0]);
        AddProfileButton.IsEnabled = true;
        RenameProfileButton.IsEnabled = editable;
        DeleteProfileButton.IsEnabled = editable;
        CopyAssignmentsButton.IsEnabled = profile != null;
        PasteAssignmentsButton.IsEnabled = profile != null && copiedAssignments != null;
        AutoSwitchBox.IsEnabled = editable;
        RegisterApplicationButton.IsEnabled = editable && RunningApplicationList.SelectedItem != null;
        UnregisterApplicationButton.IsEnabled = editable && AssignedApplicationList.SelectedItem != null;
    }
    void AddRunningApplication_Click(object sender, RoutedEventArgs e)
    {
        if (RunningApplicationList.SelectedItem is ApplicationDisplayItem app)
            AddAutoSwitchApplication(app.Value);
        else
            ShowStatus("右側の一覧からアプリを選択してください。", true);
    }
    void RemoveAutoSwitchApplication_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedProfile == null || AssignedApplicationList.SelectedItem is not ApplicationDisplayItem selected)
            return;
        string app = selected.Value;
        SelectedProfile.AutoSwitchApplications.RemoveAll(x => x.Equals(app, StringComparison.OrdinalIgnoreCase));
        RefreshSelectedProfile();
        ShowStatus(app + " を対象から外しました。");
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
        StatusText.Text = LocalizationService.Text(message);
        StatusText.Foreground = ThemeService.Brush(error ? "DangerBrush" : "AccentTextBrush");
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
    static Profile CloneProfile(Profile profile) => new() { Id = profile.Id, Name = profile.Name, DefaultDeckLayoutId = profile.DefaultDeckLayoutId, AutoSwitchEnabled = profile.AutoSwitchEnabled, AutoSwitchApplications = [.. profile.AutoSwitchApplications], Mappings = [.. profile.Mappings.Select(CloneMapping)] };
    static Mapping CloneMapping(Mapping x) => new() { Input = x.Input, Kind = x.Kind, Value = x.Value, LongPressKind = x.LongPressKind, LongPressValue = x.LongPressValue, DragValue = x.DragValue, DragEndValue = x.DragEndValue, LongPressMs = x.LongPressMs, Application = x.Application, Layer = x.Layer, Description = x.Description, DeckColor = x.DeckColor, DeckFilePath = x.DeckFilePath, DeckIcon = x.DeckIcon, DeckIconPath = x.DeckIconPath, DeckIconAutoAssigned = x.DeckIconAutoAssigned, DeckIconHidden = x.DeckIconHidden, DeckMonitor = x.DeckMonitor };
}
