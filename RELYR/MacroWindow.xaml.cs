using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MessageBox=System.Windows.MessageBox;
using WpfKeyEventArgs=System.Windows.Input.KeyEventArgs;
using WpfBrushes=System.Windows.Media.Brushes;
using WpfColor=System.Windows.Media.Color;

namespace RELYR;

public partial class MacroWindow:Window
{
    readonly AppConfig config;
    readonly Action<bool,bool,bool> setRecording;
    readonly bool allowAssignment;
    List<MacroDefinition> savedMacros=[];
    Dictionary<Mapping,(ActionKind Kind,string Value,ActionKind LongKind,string LongValue)> savedMappingActions=[];
    bool savedRecordKeyboard;
    bool savedRecordMappedActions;
    bool savedRecordMouseMoves;
    bool savedRelativeMouseMoves;
    readonly HashSet<Key> manualHeld=[];
    readonly HashSet<MacroStep> recordedMovesInsideWindow=[];
    readonly HashSet<string> suppressedMappedInputs=new(StringComparer.OrdinalIgnoreCase);
    MacroDefinition? current;
    (int X,int Y)? lastRecordedMousePosition;
    Stopwatch? sinceLast;
    bool recording,manualCaptureActive,loading,refreshingList,loadingOption,editingName,accepted;
    int recordingStartIndex;
    string nameBeforeEdit="";
    readonly MacroStopShortcut stopShortcut=new();

    public bool Changed{get;private set;}
    public bool SaveRequested{get;private set;}
    public string? SelectedMacroName=>current?.Name;
    public string? ShortcutCreatedPath{get;private set;}
    internal bool TitleBarUsesDarkMode{get;private set;}
    public event Action? Saved;

    public MacroWindow(AppConfig config,Action<bool,bool,bool> setRecording,bool allowAssignment=false,string assignmentTarget="")
    {
        InitializeComponent();
        this.config=config;this.setRecording=setRecording;this.allowAssignment=allowAssignment;
        CaptureSavedState();
        MainWindow.FollowWindowsTitleBarTheme(this,value=>TitleBarUsesDarkMode=value);
        loadingOption=true;RecordKeyboardBox.IsChecked=config.RecordKeyboardInputInMacros;RecordMappedActionsBox.IsChecked=config.RecordMappedActionsInMacros;RecordPhysicalInputBox.IsChecked=!config.RecordMappedActionsInMacros;RecordMouseMovesBox.IsChecked=config.RecordMouseMovementInMacros;RelativeMouseMovementBox.IsChecked=config.RecordMouseMovementRelativeInMacros;FixedMousePositionBox.IsChecked=!config.RecordMouseMovementRelativeInMacros;loadingOption=false;UpdateMouseMovementModeState();
        UseButton.Visibility=allowAssignment?Visibility.Visible:Visibility.Collapsed;
        AssignmentTargetText.Text=allowAssignment&&!string.IsNullOrWhiteSpace(assignmentTarget)?"割り当て先: "+assignmentTarget:"マクロを保存するだけの場合は［保存］を選んでください。";
        RefreshMacros();if(config.Macros.Count>0)MacroList.SelectedIndex=0;else SetEditorState();
    }

    void RefreshMacros(){var selected=current;refreshingList=true;MacroList.ItemsSource=null;MacroList.ItemsSource=config.Macros;if(selected!=null)MacroList.SelectedItem=selected;refreshingList=false;}
    void SetEditorState()
    {
        bool available=current!=null;EditorPanel.IsEnabled=available;EditorPanel.Opacity=available?1:.45;EditorPanel.Visibility=available?Visibility.Visible:Visibility.Collapsed;EmptyHint.Visibility=available?Visibility.Collapsed:Visibility.Visible;
        EditMacroButton.IsEnabled=available;DeleteMacroButton.IsEnabled=available;SaveButton.IsEnabled=available;ShortcutButton.IsEnabled=available&&current!.Steps.Count>0;UseButton.IsEnabled=allowAssignment&&available&&current!.Steps.Count>0;
    }
    void CreateMacro(){int n=1;string name;do{name=$"マクロ {n++}";}while(config.Macros.Any(x=>x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)));var macro=new MacroDefinition{Name=name};config.Macros.Add(macro);current=macro;Changed=true;RefreshMacros();MacroList.SelectedItem=macro;SetEditorState();BeginNameEdit();}
    void New_Click(object s,RoutedEventArgs e){StopRecording();StopManualCapture();CommitNameEdit(false);CreateMacro();}
    void EditMacro_Click(object s,RoutedEventArgs e){if(current!=null)BeginNameEdit();}
    void BeginNameEdit(){if(current==null)return;nameBeforeEdit=current.Name;editingName=true;NameBox.IsReadOnly=false;ConfirmNameButton.Visibility=Visibility.Visible;NameBox.Text=current.Name;Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background,new Action(()=>{NameBox.Focus();NameBox.SelectAll();}));}
    bool CommitNameEdit(bool showError)
    {
        if(!editingName||current==null)return true;string name=NameBox.Text.Trim();
        if(name.Length==0||config.Macros.Any(x=>x!=current&&x.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))
        {
            if(showError)MessageBox.Show(name.Length==0?"マクロ名を入力してください。":"同じ名前のマクロがあります。","マクロ名",MessageBoxButton.OK,MessageBoxImage.Warning);
            loading=true;NameBox.Text=nameBeforeEdit;loading=false;current.Name=nameBeforeEdit;NameBox.IsReadOnly=true;ConfirmNameButton.Visibility=Visibility.Collapsed;editingName=false;MacroList.Items.Refresh();return false;
        }
        string old=current.Name;current.Name=name;
        if(!old.Equals(name,StringComparison.OrdinalIgnoreCase))
        {
            foreach(var map in config.Profiles.SelectMany(x=>x.Mappings)){if(map.Kind==ActionKind.Macro&&map.Value.Equals(old,StringComparison.OrdinalIgnoreCase))map.Value=name;if(map.LongPressKind==ActionKind.Macro&&map.LongPressValue.Equals(old,StringComparison.OrdinalIgnoreCase))map.LongPressValue=name;}
            Changed=true;
        }
        loading=true;NameBox.Text=current.Name;loading=false;NameBox.IsReadOnly=true;ConfirmNameButton.Visibility=Visibility.Collapsed;editingName=false;MacroList.Items.Refresh();FooterStatus.Text=$"マクロ名を「{current.Name}」に確定しました。保存すると反映されます。";return true;
    }
    void MacroChanged(object s,SelectionChangedEventArgs e)
    {
        if(refreshingList)return;StopRecording();StopManualCapture();CommitNameEdit(false);current=MacroList.SelectedItem as MacroDefinition;loading=true;NameBox.Text=current?.Name??"";loading=false;RefreshSteps();SetEditorState();FooterStatus.Text="";
    }
    void NameChanged(object s,TextChangedEventArgs e){if(!loading&&editingName)FooterStatus.Text="［名前を確定］を押すか Enter キーで確定してください。";}
    void ConfirmName_Click(object s,RoutedEventArgs e)=>CommitNameEdit(true);
    void NameBox_KeyDown(object s,WpfKeyEventArgs e){if(e.Key==Key.Enter){CommitNameEdit(true);e.Handled=true;}else if(e.Key==Key.Escape){loading=true;NameBox.Text=nameBeforeEdit;loading=false;CommitNameEdit(false);e.Handled=true;}}

    void DeleteMacro_Click(object s,RoutedEventArgs e)
    {
        if(current==null)return;int references=config.Profiles.SelectMany(x=>x.Mappings).Count(x=>(x.Kind==ActionKind.Macro&&x.Value.Equals(current.Name,StringComparison.OrdinalIgnoreCase))||(x.LongPressKind==ActionKind.Macro&&x.LongPressValue.Equals(current.Name,StringComparison.OrdinalIgnoreCase)));string note=references>0?$"\nこのマクロを使う割り当て {references} 件も未設定に戻します。":"";
        if(MessageBox.Show($"「{current.Name}」を削除しますか？{note}","確認",MessageBoxButton.YesNo,MessageBoxImage.Warning)!=MessageBoxResult.Yes)return;
        StopRecording();StopManualCapture();foreach(var map in config.Profiles.SelectMany(x=>x.Mappings)){if(map.Kind==ActionKind.Macro&&map.Value.Equals(current.Name,StringComparison.OrdinalIgnoreCase)){map.Kind=ActionKind.None;map.Value="";}if(map.LongPressKind==ActionKind.Macro&&map.LongPressValue.Equals(current.Name,StringComparison.OrdinalIgnoreCase)){map.LongPressKind=ActionKind.None;map.LongPressValue="";}}
        config.Macros.Remove(current);current=null;Changed=true;RefreshMacros();if(config.Macros.Count>0)MacroList.SelectedIndex=0;else{loading=true;NameBox.Clear();loading=false;RefreshSteps();SetEditorState();}
    }

    void ManualCapture_GotKeyboardFocus(object s,KeyboardFocusChangedEventArgs e)
    {
        if(current==null)return;StopRecording();if(manualCaptureActive)return;manualCaptureActive=true;manualHeld.Clear();setRecording(true,false,false);ManualCaptureButton.Content="手動入力中 — 追加するキーを押してください";ManualStatus.Text="キーを押す／離す操作を、手順の末尾へ追加しています。終了するには画面内の別の場所をクリックします。";
    }
    void ManualCapture_LostKeyboardFocus(object s,KeyboardFocusChangedEventArgs e)=>StopManualCapture();
    void StopManualCapture()
    {
        if(!manualCaptureActive)return;foreach(var key in manualHeld.ToArray())AddManualEvent(key,false);manualHeld.Clear();manualCaptureActive=false;setRecording(false,false,false);ManualCaptureButton.Content="ここを選択して、追加するキーを押してください";ManualStatus.Text="手動入力では、キーを1つずつ手順の末尾へ追加します。";
    }
    static Key EventKey(WpfKeyEventArgs e)=>e.Key==Key.System?e.SystemKey:e.Key==Key.ImeProcessed?e.ImeProcessedKey:e.Key;
    void ManualCapture_KeyDown(object s,WpfKeyEventArgs e)
    {
        var key=EventKey(e);e.Handled=true;if(current==null||key==Key.None||e.IsRepeat||!manualHeld.Add(key))return;AddManualEvent(key,true);
    }
    void ManualCapture_KeyUp(object s,WpfKeyEventArgs e)
    {
        var key=EventKey(e);e.Handled=true;if(current==null||key==Key.None||!manualHeld.Remove(key))return;AddManualEvent(key,false);
    }
    void AddManualEvent(Key key,bool down)
    {
        if(current==null)return;int vk=KeyInterop.VirtualKeyFromKey(key);string name=InputEngine.KeyName(vk);string value=$"{name} {(down?"Down":"Up")}";if(vk==0||!InputEngine.IsValidRecordedEvent(value))return;current.Steps.Add(new MacroStep{Event=value});Changed=true;RefreshSteps();StepList.ScrollIntoView(StepList.Items[^1]);SetEditorState();
    }
    internal void AddManualKeyForTest(Key key){AddManualEvent(key,true);AddManualEvent(key,false);}

    void Record_Click(object s,RoutedEventArgs e)
    {
        if(recording){StopRecording();return;}if(current==null)return;StopManualCapture();recordingStartIndex=current.Steps.Count;stopShortcut.Reset();recording=true;sinceLast=Stopwatch.StartNew();lastRecordedMousePosition=null;recordedMovesInsideWindow.Clear();suppressedMappedInputs.Clear();RecordKeyboardBox.IsEnabled=false;RecordMappedActionsBox.IsEnabled=false;RecordPhysicalInputBox.IsEnabled=false;RecordMouseMovesBox.IsEnabled=false;RelativeMouseMovementBox.IsEnabled=false;FixedMousePositionBox.IsEnabled=false;setRecording(true,RecordMouseMovesBox.IsChecked==true,config.RecordMappedActionsInMacros);RecordButton.Content="■ 記録停止";RecordButton.Background=ThemeService.Brush("AccentStrongBrush");RecordStatus.Text=$"末尾へ記録中（{(config.RecordMappedActionsInMacros?"割り当て後のアクション":"物理キー")}／キーボード{(RecordKeyboardBox.IsChecked==true?"あり":"なし")}／カーソル{(config.RecordMouseMovementRelativeInMacros?"相対移動":"固定位置")}）… Ctrl + Shift + F12 で終了";RecordStatus.Foreground=ThemeService.Brush("AccentBrush");
    }
    public void Capture(string text)
    {
        if(!recording||current==null)return;bool moveInsideWindow=false;if(text.StartsWith("MouseMove:",StringComparison.OrdinalIgnoreCase)&&TryParseMousePoint(text,out int mouseX,out int mouseY)){moveInsideWindow=IsScreenPointInsideWindow(mouseX,mouseY);if(config.RecordMouseMovementRelativeInMacros){if(lastRecordedMousePosition is not { } previous){lastRecordedMousePosition=(mouseX,mouseY);return;}text=$"MouseMoveRelative:{mouseX-previous.X},{mouseY-previous.Y}";lastRecordedMousePosition=(mouseX,mouseY);}}bool down=text.EndsWith(" Down",StringComparison.OrdinalIgnoreCase),up=text.EndsWith(" Up",StringComparison.OrdinalIgnoreCase);
        if(stopShortcut.Process(text)){while(current.Steps.Count>recordingStartIndex&&current.Steps.LastOrDefault() is { } last&&(last.Event.StartsWith("LeftCtrl ")||last.Event.StartsWith("RightCtrl ")||last.Event.StartsWith("LeftShift ")||last.Event.StartsWith("RightShift ")))current.Steps.RemoveAt(current.Steps.Count-1);StopRecording();return;}
        if(config.RecordMappedActionsInMacros&&TryPhysicalEventName(text,out string physicalName,out bool physicalUp)&&suppressedMappedInputs.Contains(physicalName)){if(physicalUp)suppressedMappedInputs.Remove(physicalName);return;}
        if(!ShouldRecordEvent(text,RecordKeyboardBox.IsChecked==true))return;bool supported=text.StartsWith("MouseMove:",StringComparison.OrdinalIgnoreCase)||text.StartsWith("MouseMoveRelative:",StringComparison.OrdinalIgnoreCase)||down||up;if(!supported)return;if(current.Steps.Count==recordingStartIndex&&sinceLast?.ElapsedMilliseconds<300)return;
        int delay=(int)Math.Clamp(sinceLast?.ElapsedMilliseconds??0,0,600000);sinceLast?.Restart();var step=new MacroStep{Event=text,DelayMs=delay};current.Steps.Add(step);if(moveInsideWindow)recordedMovesInsideWindow.Add(step);Changed=true;RefreshSteps();StepList.ScrollIntoView(StepList.Items[^1]);SetEditorState();
    }
    internal void CaptureMappedAction(Mapping map,string eventName)
    {
        if(!recording||current==null||!config.RecordMappedActionsInMacros||!MappingExecutor.TryGetRecordedAction(map,eventName,out var kind,out string value))return;
        string baseInput=eventName.Split(':',2)[0];var physicalNames=baseInput.Split('+',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Where(x=>!x.Equals("Taskbar",StringComparison.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int delay=(int)Math.Clamp(sinceLast?.ElapsedMilliseconds??0,0,600000);
        while(current.Steps.Count>recordingStartIndex&&current.Steps[^1].RecordedActionKind==null&&TryPhysicalEventName(current.Steps[^1].Event,out string name,out _)&&physicalNames.Contains(name)){delay=Math.Min(600000,delay+current.Steps[^1].DelayMs);current.Steps.RemoveAt(current.Steps.Count-1);}
        sinceLast?.Restart();
        bool layerFiresBeforeRelease=baseInput.StartsWith("Space+",StringComparison.OrdinalIgnoreCase)||baseInput.StartsWith("CapsLock+",StringComparison.OrdinalIgnoreCase);
        if(layerFiresBeforeRelease)foreach(string name in physicalNames)suppressedMappedInputs.Add(name);
        var step=new MacroStep{Event=$"割り当て: {value}",DelayMs=delay,RecordedActionKind=kind,RecordedActionValue=value};current.Steps.Add(step);Changed=true;RefreshSteps();StepList.ScrollIntoView(StepList.Items[^1]);SetEditorState();
    }
    static bool TryPhysicalEventName(string text,out string name,out bool up)
    {
        up=text.EndsWith(" Up",StringComparison.OrdinalIgnoreCase);bool down=text.EndsWith(" Down",StringComparison.OrdinalIgnoreCase);if(!up&&!down){name="";return false;}name=text[..^(up?3:5)].TrimEnd();return name.Length>0;
    }
    internal static bool ShouldRecordEvent(string text,bool recordKeyboard)
    {
        bool keyEvent=(text.EndsWith(" Down",StringComparison.OrdinalIgnoreCase)||text.EndsWith(" Up",StringComparison.OrdinalIgnoreCase))&&!text.StartsWith("Mouse",StringComparison.OrdinalIgnoreCase)&&!text.StartsWith("Wheel",StringComparison.OrdinalIgnoreCase)&&!text.StartsWith("Tilt",StringComparison.OrdinalIgnoreCase);
        return recordKeyboard||!keyEvent;
    }
    void StopRecording()
    {
        if(!recording)return;recording=false;stopShortcut.Reset();while(current!=null&&current.Steps.Count>recordingStartIndex&&current.Steps.LastOrDefault() is { } last&&(recordedMovesInsideWindow.Contains(last)||IsMoveInsideWindow(last.Event)||(last.Event=="MouseLeft Down"&&last.DelayMs<1000)))current.Steps.RemoveAt(current.Steps.Count-1);sinceLast=null;lastRecordedMousePosition=null;recordedMovesInsideWindow.Clear();suppressedMappedInputs.Clear();setRecording(false,false,false);RecordKeyboardBox.IsEnabled=true;RecordMappedActionsBox.IsEnabled=true;RecordPhysicalInputBox.IsEnabled=true;RecordMouseMovesBox.IsEnabled=true;UpdateMouseMovementModeState();RecordButton.Content="● マクロの記録を開始（末尾へ追記）";RecordButton.Background=ThemeService.Brush("DangerBackground");RecordStatus.Text="停止中";RecordStatus.Foreground=ThemeService.Brush("SecondaryText");RefreshSteps();SetEditorState();
    }
    void RecordKeyboardChanged(object s,RoutedEventArgs e){if(loadingOption)return;config.RecordKeyboardInputInMacros=RecordKeyboardBox.IsChecked==true;Changed=true;}
    void KeyRecordingModeChanged(object s,RoutedEventArgs e){if(loadingOption)return;config.RecordMappedActionsInMacros=RecordMappedActionsBox.IsChecked==true;Changed=true;}
    void RecordMouseMovesChanged(object s,RoutedEventArgs e){if(loadingOption)return;config.RecordMouseMovementInMacros=RecordMouseMovesBox.IsChecked==true;UpdateMouseMovementModeState();Changed=true;}
    void MouseMovementModeChanged(object s,RoutedEventArgs e){if(loadingOption)return;config.RecordMouseMovementRelativeInMacros=RelativeMouseMovementBox.IsChecked==true;Changed=true;}
    void UpdateMouseMovementModeState(){MouseMovementModePanel.Opacity=RecordMouseMovesBox.IsChecked==true?1:.45;RelativeMouseMovementBox.IsEnabled=!recording&&RecordMouseMovesBox.IsChecked==true;FixedMousePositionBox.IsEnabled=!recording&&RecordMouseMovesBox.IsChecked==true;}
    static bool TryParseMousePoint(string value,out int x,out int y){x=0;y=0;if(!value.StartsWith("MouseMove:",StringComparison.OrdinalIgnoreCase))return false;var p=value[10..].Split(',');return p.Length==2&&int.TryParse(p[0],out x)&&int.TryParse(p[1],out y);}
    bool IsScreenPointInsideWindow(int x,int y){var point=PointFromScreen(new System.Windows.Point(x,y));return point.X>=0&&point.Y>=0&&point.X<=ActualWidth&&point.Y<=ActualHeight;}
    bool IsMoveInsideWindow(string value){if(!value.StartsWith("MouseMove:",StringComparison.OrdinalIgnoreCase))return false;try{var p=value[10..].Split(',');var point=PointFromScreen(new System.Windows.Point(double.Parse(p[0]),double.Parse(p[1])));return point.X>=0&&point.Y>=0&&point.X<=ActualWidth&&point.Y<=ActualHeight;}catch{return false;}}
    void AddWait_Click(object s,RoutedEventArgs e){if(current==null)return;if(!int.TryParse(WaitBox.Text,out int ms)||ms<1||ms>600000){MessageBox.Show("待機時間は1～600000ミリ秒で入力してください。");return;}current.Steps.Add(new MacroStep{Event="Wait",DelayMs=ms});Changed=true;RefreshSteps();SetEditorState();}
    void RefreshSteps(){StepList.ItemsSource=current?.Steps.Select((x,i)=>$"{i+1,2}.  {(x.Event=="Wait"?$"待機 {x.DelayMs} ms":$"{x.DelayMs} ms 後  {x.Event}")}").ToList();}
    void DeleteStep_Click(object s,RoutedEventArgs e)
    {
        if(current==null||StepList.SelectedItems.Count==0)return;
        var indexes=StepList.SelectedItems.Cast<object>().Select(item=>StepList.Items.IndexOf(item)).Where(index=>index>=0).Distinct().OrderByDescending(index=>index).ToArray();
        foreach(int index in indexes)current.Steps.RemoveAt(index);
        Changed=true;RefreshSteps();
        if(current.Steps.Count>0)StepList.SelectedIndex=Math.Min(indexes.Min(),current.Steps.Count-1);
        SetEditorState();
    }
    void Clear_Click(object s,RoutedEventArgs e){if(current==null)return;current.Steps.Clear();Changed=true;RefreshSteps();SetEditorState();}
    void MoveUp_Click(object s,RoutedEventArgs e)=>Move(-1);void MoveDown_Click(object s,RoutedEventArgs e)=>Move(1);
    void Move(int offset){if(current==null)return;int from=StepList.SelectedIndex,to=from+offset;if(from<0||to<0||to>=current.Steps.Count)return;(current.Steps[from],current.Steps[to])=(current.Steps[to],current.Steps[from]);Changed=true;RefreshSteps();StepList.SelectedIndex=to;}

    bool ValidateCurrent(bool requireSteps)
    {
        if(!CommitNameEdit(true)||current==null){MessageBox.Show("先に［＋ 新規］からマクロを作成してください。","マクロ",MessageBoxButton.OK,MessageBoxImage.Information);return false;}
        if(requireSteps&&current.Steps.Count==0){MessageBox.Show("先にキー操作、記録、または待機時間を追加してください。","マクロ",MessageBoxButton.OK,MessageBoxImage.Information);return false;}return true;
    }
    void Save_Click(object s,RoutedEventArgs e)
    {
        if(!ValidateCurrent(false))return;StopRecording();StopManualCapture();new ConfigService().Save(config);UpdateRenamedShortcuts();CaptureSavedState();SaveRequested=true;Changed=true;Saved?.Invoke();FooterStatus.Text="保存して反映しました。この画面を開いたまま編集を続けられます。";
    }
    void CreateShortcut_Click(object s,RoutedEventArgs e)
    {
        if(!ValidateCurrent(true)||current==null)return;StopRecording();StopManualCapture();try{new ConfigService().Save(config);UpdateRenamedShortcuts();ShortcutCreatedPath=ShortcutService.CreateMacroShortcut(current);CaptureSavedState();SaveRequested=true;Changed=true;Saved?.Invoke();FooterStatus.Text=$"デスクトップに「{System.IO.Path.GetFileName(ShortcutCreatedPath)}」を作成しました。名前を後から変更しても実行できます。";}catch(Exception ex){MessageBox.Show("ショートカットを作成できませんでした。\n\n"+ex.Message,"ショートカット作成",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    void Use_Click(object s,RoutedEventArgs e){if(!allowAssignment||!ValidateCurrent(true))return;StopRecording();StopManualCapture();accepted=true;DialogResult=true;}
    void Close_Click(object s,RoutedEventArgs e){StopRecording();StopManualCapture();DialogResult=false;}
    void CaptureSavedState()
    {
        var snapshot=new ConfigService().Clone(config);savedMacros=snapshot.Macros;savedRecordKeyboard=config.RecordKeyboardInputInMacros;savedRecordMappedActions=config.RecordMappedActionsInMacros;savedRecordMouseMoves=config.RecordMouseMovementInMacros;savedRelativeMouseMoves=config.RecordMouseMovementRelativeInMacros;
        savedMappingActions=config.Profiles.SelectMany(x=>x.Mappings).ToDictionary(x=>x,x=>(x.Kind,x.Value,x.LongPressKind,x.LongPressValue));
    }
    void UpdateRenamedShortcuts()
    {
        foreach(var macro in config.Macros)
        {
            var previous=savedMacros.FirstOrDefault(x=>x.Id.Equals(macro.Id,StringComparison.OrdinalIgnoreCase));if(previous==null||previous.Name.Equals(macro.Name,StringComparison.Ordinal))continue;
            try{ShortcutService.MigrateRenamedMacroShortcut(previous.Name,macro);}catch(Exception ex){FooterStatus.Text="マクロは保存しましたが、既存ショートカットの名前変更に失敗しました: "+ex.Message;}
        }
        foreach(var macro in config.Macros)try{ShortcutService.UpgradeExistingMacroShortcut(macro);}catch(Exception ex){FooterStatus.Text="マクロは保存しましたが、既存ショートカットの更新に失敗しました: "+ex.Message;}
    }
    void RestoreUncommittedChanges()
    {
        config.Macros=new ConfigService().Clone(new AppConfig{Macros=savedMacros,Profiles=[new Profile()]}).Macros;config.RecordKeyboardInputInMacros=savedRecordKeyboard;config.RecordMappedActionsInMacros=savedRecordMappedActions;config.RecordMouseMovementInMacros=savedRecordMouseMoves;config.RecordMouseMovementRelativeInMacros=savedRelativeMouseMoves;
        foreach(var pair in savedMappingActions){pair.Key.Kind=pair.Value.Kind;pair.Key.Value=pair.Value.Value;pair.Key.LongPressKind=pair.Value.LongKind;pair.Key.LongPressValue=pair.Value.LongValue;}Changed=false;
    }
    void Window_Closing(object? s,CancelEventArgs e){StopRecording();StopManualCapture();CommitNameEdit(false);if(!accepted)RestoreUncommittedChanges();}
}
