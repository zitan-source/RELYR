using System.Diagnostics;

namespace RELYR;

public interface IInputOutput
{
    void NeutralizeSourceKey(string input) { }
    void SendShortcut(string value);
    void SendText(string value);
    void SendMouse(string value);
    void Launch(string value);
    void RunMacro(string name);
    void SwitchProfile(string name);
}

public sealed class SystemInputOutput(Func<string,MacroDefinition?> findMacro,Action<string>? switchProfile=null,Func<bool>? useUsLayout=null,Func<AppConfig?>? getConfig=null):IInputOutput
{
    public void NeutralizeSourceKey(string input)=>InputEngine.NeutralizePhysicalSourceKey(input);
    public void SendShortcut(string value)
    {
        InputEngine.SendShortcut(value,useUsLayout?.Invoke()==true,getConfig?.Invoke()?.WindowActionTarget??WindowActionTarget.ActiveWindow);
    }
    public void SendText(string value)=>InputEngine.SendText(value,useUsLayout?.Invoke()==true);
    public void SendMouse(string value)=>InputEngine.SendMouse(value);
    public void Launch(string value)
    {
        using var process=Process.Start(new ProcessStartInfo(value){UseShellExecute=true});
    }
    public void RunMacro(string name){var macro=findMacro(name)??throw new InvalidOperationException("マクロが見つかりません: "+name);MacroPlayer.Play(macro,getConfig?.Invoke(),switchProfile);}
    public void SwitchProfile(string name)=>(switchProfile??throw new InvalidOperationException("プロファイル切替を利用できません。"))(name);
}

public sealed class MappingExecutor(IInputOutput output)
{
    internal static bool TryGetRecordedAction(Mapping map,string eventName,out ActionKind kind,out string value)
    {
        bool longPress=eventName.EndsWith(":Long",StringComparison.OrdinalIgnoreCase);
        bool dragStart=eventName.EndsWith(":DragStart",StringComparison.OrdinalIgnoreCase);
        bool dragEnd=eventName.EndsWith(":DragEnd",StringComparison.OrdinalIgnoreCase);
        bool pressStart=eventName.EndsWith(":PressStart",StringComparison.OrdinalIgnoreCase);
        bool pressEnd=eventName.EndsWith(":PressEnd",StringComparison.OrdinalIgnoreCase);
        if(!longPress&&map.Kind==ActionKind.Mouse&&IsModifierDrag(map.Value))
        {
            kind=ActionKind.Mouse;value=map.Value+(pressStart||dragStart?":Start":pressEnd||dragEnd?":End":"");return true;
        }
        if(pressStart||pressEnd){kind=ActionKind.None;value="";return false;}
        kind=longPress&&map.LongPressKind!=ActionKind.None?map.LongPressKind:map.Kind;
        value=longPress?map.LongPressValue:dragStart?map.DragValue:dragEnd?map.DragEndValue:map.Value;
        if(kind is ActionKind.None or ActionKind.Disabled)
        {
            if(kind==ActionKind.None&&!longPress&&!dragStart&&!dragEnd&&map.Layer=="通常"&&!string.IsNullOrWhiteSpace(map.LongPressValue)){kind=ActionKind.Shortcut;value=map.Input;return true;}
            return false;
        }
        if((longPress||dragStart||dragEnd)&&string.IsNullOrWhiteSpace(value))return false;
        if(!longPress&&!dragStart&&!dragEnd&&string.IsNullOrWhiteSpace(value))
        {
            if(map.Layer!="通常")return false;
            kind=ActionKind.Shortcut;value=map.Input;
        }
        return true;
    }

    public bool Execute(Mapping map,string eventName,out string executedValue)
    {
        bool longPress=eventName.EndsWith(":Long",StringComparison.OrdinalIgnoreCase);
        bool dragStart=eventName.EndsWith(":DragStart",StringComparison.OrdinalIgnoreCase);
        bool dragEnd=eventName.EndsWith(":DragEnd",StringComparison.OrdinalIgnoreCase);
        bool pressStart=eventName.EndsWith(":PressStart",StringComparison.OrdinalIgnoreCase);
        bool pressEnd=eventName.EndsWith(":PressEnd",StringComparison.OrdinalIgnoreCase);
        if(!longPress&&map.Kind==ActionKind.Mouse&&IsModifierDrag(map.Value))
        {
            executedValue=map.Value+(pressStart||dragStart?":Start":pressEnd||dragEnd?":End":"");
            try{output.SendMouse(executedValue);return true;}catch(Exception ex){InputEngine.ReleaseAll();executedValue="エラー: "+ex.Message;return true;}
        }
        if(pressStart||pressEnd){executedValue="";return false;}
        executedValue=longPress?map.LongPressValue:dragStart?map.DragValue:dragEnd?map.DragEndValue:map.Value;
        ActionKind kind=longPress&&map.LongPressKind!=ActionKind.None?map.LongPressKind:map.Kind;
        if(kind==ActionKind.None)
        {
            if(!longPress&&!dragStart&&!dragEnd&&map.Layer=="通常"&&!string.IsNullOrWhiteSpace(map.LongPressValue)){executedValue=map.Input;output.SendShortcut(map.Input);return true;}
            return false;
        }
        if(kind==ActionKind.Disabled)return true;
        if((longPress||dragStart||dragEnd)&&string.IsNullOrWhiteSpace(executedValue))return true;
        if(!longPress&&!dragStart&&!dragEnd&&string.IsNullOrWhiteSpace(executedValue))
        {
            if(map.Layer=="通常")
            {
                string original=map.Input;output.SendShortcut(original);executedValue=original;return true;
            }
            return true;
        }
        try
        {
            if(longPress)output.NeutralizeSourceKey(map.Input);
            switch(kind)
            {
                case ActionKind.Key:case ActionKind.Shortcut:output.SendShortcut(executedValue);break;
                case ActionKind.Text:output.SendText(executedValue);break;
                case ActionKind.Mouse:output.SendMouse(executedValue);break;
                case ActionKind.Launch:output.Launch(executedValue);break;
                case ActionKind.Macro:output.RunMacro(executedValue);break;
                case ActionKind.Profile:output.SwitchProfile(executedValue);break;
                default:return false;
            }
            return true;
        }
        catch(Exception ex){InputEngine.ReleaseAll();executedValue="エラー: "+ex.Message;return true;}
    }
    internal static bool IsModifierDrag(string? value)=>value is not null&&(value.Equals("ShiftDrag",StringComparison.OrdinalIgnoreCase)||value.Equals("CtrlDrag",StringComparison.OrdinalIgnoreCase));
}
