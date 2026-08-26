using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using ContextMenu = System.Windows.Controls.ContextMenu;
using ListBox = System.Windows.Controls.ListBox;
using MenuItem = System.Windows.Controls.MenuItem;
using TextBox = System.Windows.Controls.TextBox;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfColors = System.Windows.Media.Colors;
using WpfMessageBox = RELYR.AppDialog;

namespace RELYR;

public partial class MainWindow
{
    string? PromptText(string title, string label, string initial)
    {
        var dialog = new Window { Title = title, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Width = 460, Height = 220, ResizeMode = ResizeMode.NoResize, Background = ThemeService.Brush("SurfaceBackground"), Foreground = ThemeService.Brush("PrimaryText"), ShowInTaskbar = false };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 9) });
        var box = new TextBox { Text = initial, FontSize = 15, Height = 40, Padding = new Thickness(12, 0, 12, 0), Background = ThemeService.Brush("InputBackground"), Foreground = ThemeService.Brush("PrimaryText"), BorderBrush = ThemeService.Brush("BorderBrush"), VerticalContentAlignment = VerticalAlignment.Center };
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
        FollowWindowsTitleBarTheme(dialog);
        dialog.Loaded += (_, _) => { box.Focus(); box.SelectAll(); };
        return dialog.ShowDialog() == true ? box.Text.Trim() : null;
    }

    string? PromptMultilineText(string title, string label)
    {
        var dialog = new Window
        {
            Title = title,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 500,
            Height = 330,
            MinWidth = 420,
            MinHeight = 280,
            Background = ThemeService.Brush("SurfaceBackground"),
            Foreground = ThemeService.Brush("PrimaryText"),
            ShowInTaskbar = false
        };
        var grid = new Grid { Margin = new Thickness(24) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });
        var box = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(12, 9, 12, 9),
            Background = ThemeService.Brush("InputBackground"),
            Foreground = ThemeService.Brush("PrimaryText"),
            BorderBrush = ThemeService.Brush("BorderBrush"),
            FontSize = 15
        };
        Grid.SetRow(box, 1);
        grid.Children.Add(box);
        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancel = new System.Windows.Controls.Button
        {
            Content = "キャンセル",
            Width = 112,
            Height = 40,
            Margin = new Thickness(6, 0, 0, 0),
            Style = (Style)System.Windows.Application.Current.FindResource("AppButtonStyle"),
            IsCancel = true
        };
        var ok = new System.Windows.Controls.Button
        {
            Content = "割り当てる",
            Width = 112,
            Height = 40,
            Margin = new Thickness(6, 0, 0, 0),
            Style = (Style)System.Windows.Application.Current.FindResource("AccentAppButtonStyle"),
            IsEnabled = false
        };
        box.TextChanged += (_, _) => ok.IsEnabled = !string.IsNullOrWhiteSpace(box.Text);
        cancel.Click += (_, _) => dialog.DialogResult = false;
        ok.Click += (_, _) => dialog.DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);
        dialog.Content = grid;
        FollowWindowsTitleBarTheme(dialog);
        dialog.Loaded += (_, _) => box.Focus();
        return dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(box.Text) ? box.Text : null;
    }
    Profile? SelectProfile(string title, bool allowNoCopy) => SelectProfile(title, allowNoCopy, out _);
    Profile? SelectProfile(string title, bool allowNoCopy, out bool cancelled)
    {
        bool noCopy = false;
        var dialog = new Window { Title = title, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner, Width = 460, Height = 400, Background = ThemeService.Brush("SurfaceBackground"), Foreground = ThemeService.Brush("PrimaryText"), ShowInTaskbar = false };
        var grid = new Grid { Margin = new Thickness(22) };
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = allowNoCopy ? "コピー元を選ぶか、空のプロファイルとして作成してください。" : "コピー元のプロファイルを選択してください。", Foreground = ThemeService.Brush("SecondaryText"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
        var list = new ListBox { ItemsSource = config.Profiles, DisplayMemberPath = "Name", Background = ThemeService.Brush("CardBackground"), Foreground = ThemeService.Brush("PrimaryText"), BorderBrush = ThemeService.Brush("BorderBrush"), Padding = new Thickness(6) };
        Grid.SetRow(list, 1);
        grid.Children.Add(list);
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new System.Windows.Controls.Button { Content = allowNoCopy ? "コピーせず作成" : "キャンセル", Height = 40, Margin = new Thickness(6, 0, 0, 0), Style = (Style)System.Windows.Application.Current.FindResource("AppButtonStyle") };
        var ok = new System.Windows.Controls.Button { Content = "選択してコピー", Height = 40, Margin = new Thickness(6, 0, 0, 0), Style = (Style)System.Windows.Application.Current.FindResource("AccentAppButtonStyle") };
        cancel.Click += (_, _) => { noCopy = allowNoCopy; dialog.DialogResult = false; };
        ok.Click += (_, _) => { if (list.SelectedItem != null) dialog.DialogResult = true; };
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        Grid.SetRow(buttons, 2);
        grid.Children.Add(buttons);
        dialog.Content = grid;
        FollowWindowsTitleBarTheme(dialog);
        bool? result = dialog.ShowDialog();
        cancelled = result is null || (result == false && !noCopy);
        return result == true ? list.SelectedItem as Profile : null;
    }
    string? SelectRunningApplication() => SelectRunningApplication(this, "自動切替する起動中のアプリを選択");

    internal static string? SelectRunningApplication(Window owner, string title)
    {
        var apps = new List<InstalledApplicationInfo>();
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (process.MainWindowHandle != IntPtr.Zero && !string.IsNullOrWhiteSpace(process.MainWindowTitle))
                    {
                        string executable = process.ProcessName + ".exe";
                        string? path = ApplicationIconService.TryGetProcessPath(process);
                        apps.Add(new(process.MainWindowTitle, path ?? executable, "起動中  ·  " + executable, executable));
                    }
                }
                catch { }
            }
        }
        var uniqueApps = apps.GroupBy(x => x.ExecutableName, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).OrderBy(x => x.Name).ToList();
        var dialog = new ApplicationPickerWindow(uniqueApps) { Title = title, Owner = owner };
        dialog.ManualTargetPanel.Visibility = Visibility.Collapsed;
        if (dialog.ShowDialog() != true || dialog.SelectedApplication == null)
            return null;
        return dialog.SelectedApplication.ExecutableName
            ?? Path.GetFileName(ApplicationIconService.ResolveExecutablePath(dialog.SelectedApplication.LaunchPath) ?? dialog.SelectedApplication.LaunchPath);
    }
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr handle, int attribute, ref int value, int valueSize);
}
