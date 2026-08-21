using System.Windows;

namespace RELYR;

public partial class UpdateNotesWindow : Window
{
    internal UpdateNotesWindow(string version, string notes)
    {
        InitializeComponent();
        VersionText.Text = $"RELYR v{version} の変更内容";
        NotesText.Text = notes;
        MainWindow.FollowWindowsTitleBarTheme(this);
    }

    void Close_Click(object sender, RoutedEventArgs e) => Close();
}
