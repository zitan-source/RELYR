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
    void UpdateStatus()
    {
        EngineStatus.Text = LocalizationService.Text(engine.Enabled ? "● エンジン稼働中" : "■ エンジン停止中");
        EngineStatus.Foreground = ThemeService.Brush(engine.Enabled ? "AccentTextBrush" : "DangerBrush");
    }
    void SetupTray()
    {
        tray.Text = "RELYR v" + DisplayVersion;
        defaultTrayIcon = CreateDefaultTrayIcon();
        tray.Icon = defaultTrayIcon;
        tray.Visible = true;
        RebuildTrayMenu();
        UpdateTrayNumber();
        tray.DoubleClick += (_, _) => Dispatcher.BeginInvoke(ShowFromExternalLaunch);
    }
    internal static bool NativeTrayRegistrationAllowed(bool requestedSuppression)
    {
#if PRODUCTION_PUBLISH
        return !requestedSuppression;
#else
        // A development/test executable can live at dozens of temporary paths.
        // Never let one of those paths claim or duplicate the product tray ID.
        return false;
#endif
    }
    void UpdateTrayNumber()
    {
        if (!config.ShowDesktopNumberInTray)
        {
            numberedTrayIcon?.Dispose();
            numberedTrayIcon = null;
            tray.Icon = defaultTrayIcon;
            tray.Text = "RELYR v" + DisplayVersion;
            return;
        }
        try
        {
            int number = VirtualDesktopAccessor.CurrentNumber + 1;
            var icon = CreateDesktopNumberIcon(number);
            numberedTrayIcon?.Dispose();
            numberedTrayIcon = icon;
            tray.Icon = icon;
            tray.Text = LocalizationService.Text($"RELYR v{DisplayVersion} — デスクトップ {number}");
        }
        catch { tray.Icon = defaultTrayIcon; }
    }
    internal static System.Drawing.Icon CreateDefaultTrayIcon()
    {
        try
        {
            string? executable = Environment.ProcessPath;
            if (!string.IsNullOrWhiteSpace(executable) && System.Drawing.Icon.ExtractAssociatedIcon(executable) is { } icon)
                return icon;
        }
        catch { }
        return (System.Drawing.Icon)System.Drawing.SystemIcons.Application.Clone();
    }
    internal static System.Drawing.Icon CreateDesktopNumberIcon(int number)
    {
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(System.Drawing.Color.Transparent);
            float fontSize = number < 10 ? 36 : number < 100 ? 25 : 17;
            using var font = new System.Drawing.Font("Segoe UI", fontSize, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
            using var format = new System.Drawing.StringFormat { Alignment = System.Drawing.StringAlignment.Center, LineAlignment = System.Drawing.StringAlignment.Center, FormatFlags = System.Drawing.StringFormatFlags.NoClip };
            g.DrawString(number.ToString(), font, System.Drawing.Brushes.White, new System.Drawing.RectangleF(-4, -6, 40, 42), format);
        }
        IntPtr hIcon = bitmap.GetHicon();
        try
        {
            return (System.Drawing.Icon)System.Drawing.Icon.FromHandle(hIcon).Clone();
        }
        finally { DestroyIcon(hIcon); }
    }
    void RebuildTrayMenu()
    {
        var old = tray.ContextMenuStrip;
        var menu = TrayMenuTheme.Create(ThemeService.UsesDark);
        menu.Items.Add(LocalizationService.Text("表示"), null, (_, _) => Dispatcher.BeginInvoke(ShowFromExternalLaunch));
        menu.Items.Add(LocalizationService.Text("有効 / 一時停止"), null, (_, _) => Dispatcher.BeginInvoke(() => EngineToggle.IsChecked = !EngineToggle.IsChecked));
        var profiles = new System.Windows.Forms.ToolStripMenuItem(LocalizationService.Text("プロファイル"));
        foreach (var profile in appliedConfig.Profiles.Where(p => config.Profiles.Any(x => x.Name == p.Name)))
        {
            var item = new System.Windows.Forms.ToolStripMenuItem(profile.Name) { Checked = profile.Name == appliedConfig.ActiveProfile };
            item.Click += (_, _) => Dispatcher.BeginInvoke(() => SwitchProfile(profile.Name, true));
            profiles.DropDownItems.Add(item);
        }
        menu.Items.Add(profiles);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(LocalizationService.Text("押下キーをすべて解除"), null, (_, _) => InputEngine.ReleaseAllDefensively());
        menu.Items.Add(LocalizationService.Text("再起動"), null, (_, _) => Dispatcher.BeginInvoke(RequestApplicationRestart));
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add(LocalizationService.Text("終了"), null, (_, _) => RequestApplicationExit("tray-exit"));
        TrayMenuTheme.Apply(menu, ThemeService.UsesDark);
        tray.ContextMenuStrip = menu;
        old?.Dispose();
    }

}
