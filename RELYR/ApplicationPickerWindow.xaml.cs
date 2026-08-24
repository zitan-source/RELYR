using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace RELYR;

internal sealed record InstalledApplicationInfo(string Name, string LaunchPath, string Source, string? ExecutableName = null)
{
    public System.Windows.Media.ImageSource Icon { get; } = ApplicationIconService.GetIcon(LaunchPath);
}

public partial class ApplicationPickerWindow : Window
{
    List<InstalledApplicationInfo> applications = [];
    public string? SelectedPath
    {
        get; private set;
    }
    internal InstalledApplicationInfo? SelectedApplication
    {
        get; private set;
    }
    internal bool TitleBarUsesDarkMode
    {
        get; private set;
    }

    public ApplicationPickerWindow()
    {
        InitializeComponent();
        MainWindow.FollowWindowsTitleBarTheme(this, value => TitleBarUsesDarkMode = value);
        Loaded += ApplicationPickerWindow_Loaded;
    }

    internal ApplicationPickerWindow(IEnumerable<InstalledApplicationInfo> supplied) : this()
    {
        Loaded -= ApplicationPickerWindow_Loaded;
        SetApplications(supplied);
    }
    internal ApplicationPickerWindow(bool forAutoSwitch) : this()
    {
        if (!forAutoSwitch)
            return;
        Title = "自動切替するインストール済みアプリを選択";
        SelectButton.Content = "自動切替の対象に追加";
    }

    async void ApplicationPickerWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ApplicationPickerWindow_Loaded;
        var discovered = await Task.Run(DiscoverApplications);
        if (IsLoaded)
            SetApplications(discovered);
    }

    void SetApplications(IEnumerable<InstalledApplicationInfo> items)
    {
        applications = [.. items.GroupBy(x => x.LaunchPath, StringComparer.OrdinalIgnoreCase).Select(x => x.OrderBy(y => y.Name.Length).First()).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)];
        RefreshList();
    }

    void SearchChanged(object sender, TextChangedEventArgs e) => RefreshList();
    void RefreshList()
    {
        if (ApplicationList == null)
            return;
        string query = SearchBox.Text.Trim();
        var filtered = string.IsNullOrEmpty(query) ? applications : [.. applications.Where(x => MatchesSearch(x, query))];
        ApplicationList.ItemsSource = filtered;
        ResultCount.Text = $"{filtered.Count}件";
        EmptyMessage.Text = applications.Count == 0 ? "起動できるアプリが見つかりませんでした" : "一致するアプリがありません";
        EmptyMessage.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    internal static bool MatchesSearch(InstalledApplicationInfo application, string? query)
    {
        string value = (query ?? string.Empty).Trim();
        if (value.Length == 0)
            return true;

        // A one-character query is normally used as an alphabet jump.  Keep
        // that result focused on applications whose visible name (or actual
        // executable name) begins with the requested character instead of
        // returning every path which merely contains it somewhere.
        if (value.Length == 1)
        {
            string executable = application.ExecutableName
                ?? Path.GetFileNameWithoutExtension(ApplicationIconService.ResolveExecutablePath(application.LaunchPath) ?? application.LaunchPath);
            return application.Name.StartsWith(value, StringComparison.CurrentCultureIgnoreCase)
                || executable.StartsWith(value, StringComparison.OrdinalIgnoreCase);
        }

        return application.Name.Contains(value, StringComparison.CurrentCultureIgnoreCase)
            || application.LaunchPath.Contains(value, StringComparison.OrdinalIgnoreCase)
            || (application.ExecutableName?.Contains(value, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    void ApplicationSelectionChanged(object sender, SelectionChangedEventArgs e) => SelectButton.IsEnabled = ApplicationList.SelectedItem is InstalledApplicationInfo;
    void ApplicationDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ApplicationList.SelectedItem is InstalledApplicationInfo)
            UseSelection();
    }
    void Select_Click(object sender, RoutedEventArgs e) => UseSelection();
    void UseSelection()
    {
        if (ApplicationList.SelectedItem is not InstalledApplicationInfo selected)
            return;
        SelectedApplication = selected;
        SelectedPath = selected.LaunchPath;
        DialogResult = true;
    }
    void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
    void BrowseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "起動するアプリを選択", Filter = "アプリケーション (*.exe)|*.exe|ショートカット (*.lnk;*.appref-ms)|*.lnk;*.appref-ms|すべてのファイル|*.*", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true)
        {
            SelectedPath = dialog.FileName;
            SelectedApplication = new(Path.GetFileNameWithoutExtension(dialog.FileName), dialog.FileName, "参照");
            DialogResult = true;
        }
    }

    internal static IReadOnlyList<InstalledApplicationInfo> DiscoverApplications()
    {
        var found = new List<InstalledApplicationInfo>();
        foreach (string folder in new[] { Environment.GetFolderPath(Environment.SpecialFolder.Programs), Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms) }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            foreach (string file in EnumerateFilesSafe(folder).Where(x => x.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || x.EndsWith(".appref-ms", StringComparison.OrdinalIgnoreCase)))
                Add(found, Path.GetFileNameWithoutExtension(file), file, "スタート メニュー");
        foreach (var (hive, view) in new[] { (RegistryHive.CurrentUser, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry64), (RegistryHive.LocalMachine, RegistryView.Registry32) })
        {
            ReadAppPaths(found, hive, view);
            ReadUninstallEntries(found, hive, view);
        }
        return found.GroupBy(x => x.LaunchPath, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    static void ReadAppPaths(List<InstalledApplicationInfo> found, RegistryHive hive, RegistryView view)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
            if (root == null)
                return;
            foreach (string name in root.GetSubKeyNames())
            {
                using var app = root.OpenSubKey(name);
                if (app != null)
                    Add(found, Path.GetFileNameWithoutExtension(name), NormalizeExecutablePath(app.GetValue(null) as string), "インストール済みアプリ");
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (System.Security.SecurityException) { }
        catch (IOException) { }
    }

    static void ReadUninstallEntries(List<InstalledApplicationInfo> found, RegistryHive hive, RegistryView view)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, view).OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
            if (root == null)
                return;
            foreach (string keyName in root.GetSubKeyNames())
            {
                using var app = root.OpenSubKey(keyName);
                if (app != null)
                    Add(found, app.GetValue("DisplayName") as string, NormalizeExecutablePath(app.GetValue("DisplayIcon") as string), "Windowsに登録されたアプリ");
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (System.Security.SecurityException) { }
        catch (IOException) { }
    }

    static void Add(List<InstalledApplicationInfo> found, string? name, string? path, string source)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path) || name.Contains("uninstall", StringComparison.OrdinalIgnoreCase) || name.Contains("アンインストール", StringComparison.OrdinalIgnoreCase))
            return;
        found.Add(new(name.Trim(), path, source));
    }

    static string? NormalizeExecutablePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        string text = Environment.ExpandEnvironmentVariables(value.Trim());
        if (text.StartsWith('"'))
        {
            int end = text.IndexOf('"', 1);
            if (end > 1)
                text = text[1..end];
        }
        else
        {
            int end = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (end >= 0)
                text = text[..(end + 4)];
        }
        return File.Exists(text) ? text : null;
    }

    static IEnumerable<string> EnumerateFilesSafe(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            string folder = pending.Pop();
            string[] files = [];
            try
            {
                files = Directory.GetFiles(folder);
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            foreach (string file in files)
                yield return file;
            string[] children = [];
            try
            {
                children = Directory.GetDirectories(folder);
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
            foreach (string child in children)
                pending.Push(child);
        }
    }
}
