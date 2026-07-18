using System.Diagnostics;
using System.IO;
using System.Windows;

namespace RELYR;

public partial class SettingsWindow:Window
{
    internal const string ExportFileName="relyr-settings.relyr";
    internal const string ExportFileFilter="RELYR 設定ファイル (*.relyr)|*.relyr";
    internal const string ImportFileFilter="RELYR 設定ファイル (*.relyr)|*.relyr|以前のRELYR設定 (*.json)|*.json";
    readonly AppConfig config;
    readonly bool initialStartWithWindows;
    public AppConfig? ImportedConfig { get; private set; }
    public bool ImportedCapsLockNeedsRestart { get; private set; }
    public bool ImportedCapsLockEnabled { get; private set; }
    public bool StartWithWindows=>StartupBox.IsChecked==true;
    internal bool StartWithWindowsChanged=>StartWithWindows!=initialStartWithWindows;
    public bool AutoExtract=>ExtractBox.IsChecked==true;
    public bool DeleteAfterExtract=>DeleteBox.IsChecked==true;
    public string ArchiveWatchFolder=>ArchiveWatchFolderBox.Text.Trim();
    public string ArchiveDestinationFolder=>ArchiveDestinationFolderBox.Text.Trim();
    public bool ShowDesktopNumberInTray=>DesktopNumberTrayBox.IsChecked==true;
    public bool CheckForUpdates=>CheckForUpdatesBox.IsChecked==true;
    public bool CloseWindowUnderCursor=>CursorWindowTargetBox.IsChecked==true;
    public bool AutoSave=>AutoSaveBox.IsChecked==true;
    public bool SpaceHoldRepeat=>SpaceRepeatBox.IsChecked==true;
    public int SpaceHoldRepeatDelay=>int.TryParse(SpaceRepeatDelayBox.Text,out var value)?Math.Clamp(value,100,2000):400;
    public bool CapsRemapChanged { get; private set; }
    public AppConfig? ResetConfig { get; private set; }
    public bool ResetNeedsRestart { get; private set; }
    internal bool TitleBarUsesDarkMode{get;private set;}

    public SettingsWindow(AppConfig config)
    {
        this.config=config;InitializeComponent();MainWindow.FollowWindowsTitleBarTheme(this,value=>TitleBarUsesDarkMode=value);MaxHeight=Math.Max(MinHeight,SystemParameters.WorkArea.Height-40);Height=Math.Min(Height,MaxHeight);initialStartWithWindows=StartupService.IsEnabled();StartupBox.IsChecked=initialStartWithWindows;DesktopNumberTrayBox.IsChecked=config.ShowDesktopNumberInTray;CheckForUpdatesBox.IsChecked=config.CheckForUpdates;ActiveWindowTargetBox.IsChecked=!config.CloseWindowUnderCursor;CursorWindowTargetBox.IsChecked=config.CloseWindowUnderCursor;AutoSaveBox.IsChecked=config.AutoSave;SpaceRepeatBox.IsChecked=config.SpaceHoldRepeatEnabled;SpaceRepeatDelayBox.Text=config.SpaceHoldRepeatDelayMs.ToString();ArchiveWatchFolderBox.Text=string.IsNullOrWhiteSpace(config.ArchiveWatchFolder)?Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory):config.ArchiveWatchFolder;ArchiveDestinationFolderBox.Text=config.ArchiveDestinationFolder;ExtractBox.IsChecked=config.AutoExtractDesktopArchives;DeleteBox.IsChecked=config.DeleteArchiveAfterExtract;ExtractBox.Checked+=(_,_)=>DeleteBox.IsEnabled=true;ExtractBox.Unchecked+=(_,_)=>DeleteBox.IsEnabled=false;DeleteBox.IsEnabled=ExtractBox.IsChecked==true;RefreshCapsRemapStatus();
    }
    void Category_SelectionChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e){if(GeneralPanel==null)return;string selected=(CategoryList.SelectedItem as System.Windows.Controls.ListBoxItem)?.Tag?.ToString()??"General";GeneralPanel.Visibility=selected=="General"?Visibility.Visible:Visibility.Collapsed;LayersPanel.Visibility=selected=="Layers"?Visibility.Visible:Visibility.Collapsed;ArchivePanel.Visibility=selected=="Archive"?Visibility.Visible:Visibility.Collapsed;DataPanel.Visibility=selected=="Data"?Visibility.Visible:Visibility.Collapsed;}
    void Save_Click(object sender,RoutedEventArgs e)
    {
        if(AutoExtract)
        {
            if(!Directory.Exists(ArchiveWatchFolder)){System.Windows.MessageBox.Show(this,"監視するフォルダーが見つかりません。［参照］から実在するフォルダーを選択してください。","自動解凍の設定",MessageBoxButton.OK,MessageBoxImage.Warning);return;}
            if(!string.IsNullOrWhiteSpace(ArchiveDestinationFolder)&&!Directory.Exists(ArchiveDestinationFolder)){System.Windows.MessageBox.Show(this,"解凍後の保存先が見つかりません。［参照］から実在するフォルダーを選択してください。","自動解凍の設定",MessageBoxButton.OK,MessageBoxImage.Warning);return;}
        }
        DialogResult=true;
    }
    void Cancel_Click(object sender,RoutedEventArgs e){DialogResult=false;}
    void ShowTutorial_Click(object sender,RoutedEventArgs e)=>new SetupWindow(true){Owner=this}.ShowDialog();
    void Export_Click(object sender,RoutedEventArgs e){var dialog=new Microsoft.Win32.SaveFileDialog{Filter=ExportFileFilter,FileName=ExportFileName,DefaultExt=".relyr",AddExtension=true};if(dialog.ShowDialog()==true)new ConfigService().Export(config,dialog.FileName);}
    void Import_Click(object sender,RoutedEventArgs e)
    {
        var dialog=new Microsoft.Win32.OpenFileDialog{Filter=ImportFileFilter};if(dialog.ShowDialog()!=true)return;
        try
        {
            var imported=new ConfigService().Import(dialog.FileName);bool current=LegacyKeyRemapService.HasCapsLockToF13();bool desired=imported.CapsLockLayerEnabled;
            if(desired)
            {
                string warning="この設定ではCapsLockレイヤーがオンです。\n\nインポートするとCapsLockはF13へ割り当てられ、元のCapsLock機能は使用できなくなります。変更はWindowsを再起動するまで有効にならず、それまではCapsLockレイヤーも使用できません。\n\nこの設定をインポートしますか？";
                if(System.Windows.MessageBox.Show(warning,"CapsLockレイヤーを含む設定",MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)return;
            }
            if(desired!=current)
            {
                if(!ChangeCapsRemap(desired,imported,false))return;ImportedCapsLockNeedsRestart=true;ImportedCapsLockEnabled=desired;
            }
            else if(LegacyKeyRemapService.IsRestartStillPending(config))
            {
                imported.CapsLockRemapPendingRestart=true;imported.CapsLockRemapEffectiveBeforeRestart=config.CapsLockRemapEffectiveBeforeRestart;imported.CapsLockRemapChangedAtUtcTicks=config.CapsLockRemapChangedAtUtcTicks;new ConfigService().Save(imported);ImportedCapsLockNeedsRestart=true;ImportedCapsLockEnabled=desired;
            }
            ImportedConfig=imported;DialogResult=true;
        }
        catch(Exception ex){System.Windows.MessageBox.Show(ex.Message,"インポートできません",MessageBoxButton.OK,MessageBoxImage.Warning);}
    }
    void RefreshCapsRemapStatus()
    {
        bool enabled=LegacyKeyRemapService.HasCapsLockToF13();bool pending=LegacyKeyRemapService.IsRestartStillPending(config);
        CapsRemapStatus.Text=pending?(enabled?"設定済み（再起動待ち）— 再起動するまでCapsLockレイヤーは機能しません。":"復元済み（再起動待ち）— 再起動するまではF13レイヤーの状態が続きます。"):(enabled?"設定済み — CapsLockをF13レイヤーキーとして使用します。":"未設定 — CapsLockレイヤーは動作せず、通常のCapsLockとしてWindowsへ渡します。");
        CapsRemapStatus.Foreground=new System.Windows.Media.SolidColorBrush(pending?System.Windows.Media.Color.FromRgb(246,198,106):enabled?System.Windows.Media.Color.FromRgb(114,224,193):System.Windows.Media.Color.FromRgb(246,198,106));EnableCapsRemapButton.IsEnabled=!enabled;DisableCapsRemapButton.IsEnabled=enabled;
    }
    void EnableCapsRemap_Click(object sender,RoutedEventArgs e)
    {
        if(System.Windows.MessageBox.Show("CapsLock本来の機能を無効にし、CapsLockレイヤー専用キーへ変更します。CapsLockはF13へ割り当てられ、元のCapsLock機能は使用できなくなります。\n\n設定しますか？","CapsLockレイヤーを有効化",MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)return;
        if(ChangeCapsRemap(true,config,true))PromptForWindowsRestart(this,true);
    }
    void DisableCapsRemap_Click(object sender,RoutedEventArgs e)
    {
        if(System.Windows.MessageBox.Show("CapsLockレイヤーを無効にし、元のCapsLockへ戻します。\n\n設定しますか？","CapsLockへ戻す",MessageBoxButton.OKCancel,MessageBoxImage.Question)!=MessageBoxResult.OK)return;
        if(ChangeCapsRemap(false,config,true))PromptForWindowsRestart(this,false);
    }
    bool ChangeCapsRemap(bool enabled,AppConfig target,bool refresh)
    {
        try
        {
            if(!StartupService.IsProcessElevated())throw new UnauthorizedAccessException("管理者モードで起動されていません。RELYRを再インストールしてください。");
            bool effectiveBefore=LegacyKeyRemapService.HasCapsLockToF13();LegacyKeyRemapService.SetCapsLockToF13(enabled);
            target.CapsLockLayerEnabled=enabled;target.CapsLockRemapPendingRestart=true;target.CapsLockRemapEffectiveBeforeRestart=effectiveBefore;target.CapsLockRemapChangedAtUtcTicks=DateTime.UtcNow.Ticks;new ConfigService().Save(target);CapsRemapChanged=true;if(refresh)RefreshCapsRemapStatus();return true;
        }
        catch(Exception ex){System.Windows.MessageBox.Show(ex.Message,"設定できません",MessageBoxButton.OK,MessageBoxImage.Error);return false;}
    }

    void BrowseArchiveWatchFolder_Click(object sender,RoutedEventArgs e)=>BrowseFolder(ArchiveWatchFolderBox,"圧縮ファイルを監視するフォルダーを選択");
    void BrowseArchiveDestinationFolder_Click(object sender,RoutedEventArgs e)=>BrowseFolder(ArchiveDestinationFolderBox,"解凍後のファイルを保存するフォルダーを選択");
    void BrowseFolder(System.Windows.Controls.TextBox target,string description)
    {
        using var dialog=new System.Windows.Forms.FolderBrowserDialog{Description=description,UseDescriptionForTitle=true,ShowNewFolderButton=true};
        if(Directory.Exists(target.Text))dialog.InitialDirectory=target.Text;
        if(dialog.ShowDialog()==System.Windows.Forms.DialogResult.OK)target.Text=dialog.SelectedPath;
    }

    void ResetAll_Click(object sender,RoutedEventArgs e)
    {
        string message="全プロファイル、全レイヤーの割り当て、マクロ、アプリ設定を初期状態へ戻します。\n\nこの操作は元に戻せません。続行しますか？";
        if(System.Windows.MessageBox.Show(this,message,"すべての設定をリセット",MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)return;
        try
        {
            if(!StartupService.IsProcessElevated())throw new UnauthorizedAccessException("管理者モードで起動されていません。RELYRを再インストールしてください。");
            bool pending=LegacyKeyRemapService.IsRestartStillPending(config);
            bool remapRegistered=LegacyKeyRemapService.HasCapsLockToF13();
            bool effectiveBefore=pending?config.CapsLockRemapEffectiveBeforeRestart:remapRegistered;
            ResetNeedsRestart=pending||remapRegistered||config.CapsLockLayerEnabled;
            LegacyKeyRemapService.SetCapsLockToF13(false);
            StartupService.SetEnabled(false);
            var reset=ConfigService.CreateDefault();reset.FirstRunCompleted=true;
            if(ResetNeedsRestart){reset.CapsLockRemapPendingRestart=true;reset.CapsLockRemapEffectiveBeforeRestart=effectiveBefore;reset.CapsLockRemapChangedAtUtcTicks=DateTime.UtcNow.Ticks;}
            new ConfigService().Save(reset);ResetConfig=reset;CapsRemapChanged=ResetNeedsRestart;DialogResult=true;
        }
        catch(Exception ex){System.Windows.MessageBox.Show(this,"設定をリセットできませんでした。\n\n"+ex.Message,"リセットできません",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    internal static void PromptForWindowsRestart(Window owner,bool enabled)
    {
        string pending=enabled?"［キャンセル］を押した場合もCapsLockレイヤーはオンのままですが、再起動するまでは機能しません。":"［キャンセル］を押した場合も復元設定は保存されますが、再起動するまではF13レイヤーの状態が続きます。";
        string message=$"CapsLockの設定を変更しました。再起動しないと有効になりません。\n\n［OK］を押すと今すぐWindowsを再起動します。\n{pending}\n\n今すぐ再起動しますか？";
        if(System.Windows.MessageBox.Show(owner,message,"Windowsの再起動が必要です",MessageBoxButton.OKCancel,MessageBoxImage.Information)!=MessageBoxResult.OK)return;
        try{Process.Start(new ProcessStartInfo("shutdown.exe","/r /t 0"){UseShellExecute=true});}catch(Exception ex){System.Windows.MessageBox.Show(owner,"Windowsを再起動できませんでした。手動で再起動してください。\n\n"+ex.Message,"再起動できません",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
}
