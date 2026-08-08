using System.Windows;
using System.Windows.Controls;

namespace RELYR;

public partial class AppDialog : Window
{
    MessageBoxResult result = MessageBoxResult.None;

    internal AppDialog(Window? owner, string message, string caption, MessageBoxButton buttons, MessageBoxImage image)
    {
        InitializeComponent();
        Owner = owner?.IsVisible == true ? owner : null;
        WindowStartupLocation = Owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
        Title = caption;
        HeadingText.Text = caption;
        MessageText.Text = message;
        ConfigureIcon(image);
        ConfigureButtons(buttons);
        MainWindow.FollowWindowsTitleBarTheme(this);
    }

    public static MessageBoxResult Show(string message) => Show(null, message, "RELYR", MessageBoxButton.OK, MessageBoxImage.None);
    public static MessageBoxResult Show(string message, string caption) => Show(null, message, caption, MessageBoxButton.OK, MessageBoxImage.None);
    public static MessageBoxResult Show(string message, string caption, MessageBoxButton buttons) => Show(null, message, caption, buttons, MessageBoxImage.None);
    public static MessageBoxResult Show(string message, string caption, MessageBoxButton buttons, MessageBoxImage image) => Show(null, message, caption, buttons, image);
    public static MessageBoxResult Show(Window owner, string message, string caption, MessageBoxButton buttons) => Show(owner, message, caption, buttons, MessageBoxImage.None);
    public static MessageBoxResult Show(Window? owner, string message, string caption, MessageBoxButton buttons, MessageBoxImage image)
    {
        var dialog = new AppDialog(owner, message, caption, buttons, image);
        dialog.ShowDialog();
        return dialog.result;
    }

    void ConfigureIcon(MessageBoxImage image)
    {
        IconText.Text = image switch
        {
            MessageBoxImage.Error => "×",
            MessageBoxImage.Warning => "!",
            MessageBoxImage.Question => "?",
            _ => "i"
        };
        string brush = image switch
        {
            MessageBoxImage.Error => "DangerBrush",
            MessageBoxImage.Warning => "WarningBrush",
            _ => "AccentBrush"
        };
        IconText.Foreground = ThemeService.Brush(brush);
    }

    void ConfigureButtons(MessageBoxButton buttons)
    {
        switch (buttons)
        {
            case MessageBoxButton.OKCancel:
                AddButton("キャンセル", MessageBoxResult.Cancel, true);
                AddButton("OK", MessageBoxResult.OK, false, true);
                break;
            case MessageBoxButton.YesNo:
                AddButton("いいえ", MessageBoxResult.No, true);
                AddButton("はい", MessageBoxResult.Yes, false, true);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("キャンセル", MessageBoxResult.Cancel, true);
                AddButton("いいえ", MessageBoxResult.No);
                AddButton("はい", MessageBoxResult.Yes, false, true);
                break;
            default:
                AddButton("OK", MessageBoxResult.OK, true, true);
                break;
        }
    }

    void AddButton(string label, MessageBoxResult value, bool cancel = false, bool primary = false)
    {
        var button = new System.Windows.Controls.Button { Content = label, IsCancel = cancel, IsDefault = primary };
        if (primary)
        {
            button.Background = ThemeService.Brush("AccentStrongBrush");
            button.Foreground = ThemeService.Brush("AccentButtonText");
            button.BorderBrush = ThemeService.Brush("AccentBrush");
        }
        button.Click += (_, _) => { result = value; DialogResult = true; };
        ButtonPanel.Children.Add(button);
    }
}
