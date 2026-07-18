using System.Runtime.InteropServices;
using System.Text;
using System.IO;

namespace RELYR;

internal static class ShortcutService
{
    internal static string BuildMacroArguments(string macroName)=>"--run-macro-base64 "+Convert.ToBase64String(Encoding.UTF8.GetBytes(macroName));
    internal static string BuildMacroIdArguments(string macroId)=>"--run-macro-id "+macroId;
    internal static bool TryReadMacroName(IReadOnlyList<string> args,out string macroName)
    {
        macroName="";if(args.Count<2||!args[0].Equals("--run-macro-base64",StringComparison.OrdinalIgnoreCase))return false;
        try{macroName=Encoding.UTF8.GetString(Convert.FromBase64String(args[1]));return !string.IsNullOrWhiteSpace(macroName);}catch{return false;}
    }
    internal static bool TryReadMacroId(IReadOnlyList<string> args,out string macroId)
    {
        macroId="";if(args.Count<2||!args[0].Equals("--run-macro-id",StringComparison.OrdinalIgnoreCase))return false;
        macroId=args[1].Trim();return macroId.Length>0;
    }
    internal static string CreateMacroShortcut(MacroDefinition macro,string? destinationDirectory=null,string? executablePath=null)
    {
        ArgumentNullException.ThrowIfNull(macro);
        if(string.IsNullOrWhiteSpace(macro.Id))macro.Id=Guid.NewGuid().ToString("N");
        return CreateShortcut(macro.Name,BuildMacroIdArguments(macro.Id),destinationDirectory,executablePath);
    }
    internal static string CreateMacroShortcut(string macroName,string? destinationDirectory=null,string? executablePath=null)
        =>CreateShortcut(macroName,BuildMacroArguments(macroName),destinationDirectory,executablePath);
    static string CreateShortcut(string macroName,string arguments,string? destinationDirectory,string? executablePath)
    {
        if(string.IsNullOrWhiteSpace(macroName))throw new ArgumentException("マクロ名が空です。",nameof(macroName));
        string executable=Path.GetFullPath(executablePath??Environment.ProcessPath??throw new InvalidOperationException("実行ファイルを取得できません。"));
        string directory=destinationDirectory??Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);Directory.CreateDirectory(directory);
        string fileName=string.Concat(macroName.Trim().Select(x=>Path.GetInvalidFileNameChars().Contains(x)?'_':x));if(string.IsNullOrWhiteSpace(fileName))fileName="RELYR マクロ";
        string shortcutPath=Path.Combine(directory,fileName+" - RELYR.lnk");
        Type shellType=Type.GetTypeFromProgID("WScript.Shell")??throw new PlatformNotSupportedException("Windowsショートカット機能を利用できません。");
        object shell=Activator.CreateInstance(shellType)??throw new InvalidOperationException("Windowsショートカット機能を開始できません。");object? shortcut=null;
        try
        {
            shortcut=shellType.InvokeMember("CreateShortcut",System.Reflection.BindingFlags.InvokeMethod,null,shell,[shortcutPath]);if(shortcut==null)throw new InvalidOperationException("ショートカットを作成できません。");Type type=shortcut.GetType();
            type.InvokeMember("TargetPath",System.Reflection.BindingFlags.SetProperty,null,shortcut,[executable]);type.InvokeMember("Arguments",System.Reflection.BindingFlags.SetProperty,null,shortcut,[arguments]);type.InvokeMember("WorkingDirectory",System.Reflection.BindingFlags.SetProperty,null,shortcut,[Path.GetDirectoryName(executable)??""]);type.InvokeMember("Description",System.Reflection.BindingFlags.SetProperty,null,shortcut,[$"RELYR マクロ: {macroName}"]);type.InvokeMember("IconLocation",System.Reflection.BindingFlags.SetProperty,null,shortcut,[$"{executable},0"]);type.InvokeMember("Save",System.Reflection.BindingFlags.InvokeMethod,null,shortcut,null);
        }
        finally{if(shortcut!=null&&Marshal.IsComObject(shortcut))Marshal.FinalReleaseComObject(shortcut);if(Marshal.IsComObject(shell))Marshal.FinalReleaseComObject(shell);}
        return shortcutPath;
    }
    internal static string? MigrateRenamedMacroShortcut(string oldName,MacroDefinition macro,string? destinationDirectory=null,string? executablePath=null)
    {
        if(string.IsNullOrWhiteSpace(oldName)||oldName.Equals(macro.Name,StringComparison.Ordinal))return null;
        string directory=destinationDirectory??Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string oldPath=ExistingShortcutPath(oldName,directory);if(!File.Exists(oldPath))return null;
        string newPath=CreateMacroShortcut(macro,directory,executablePath);
        if(!oldPath.Equals(newPath,StringComparison.OrdinalIgnoreCase))File.Delete(oldPath);
        return newPath;
    }
    internal static string? UpgradeExistingMacroShortcut(MacroDefinition macro,string? destinationDirectory=null,string? executablePath=null)
    {
        string directory=destinationDirectory??Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);string path=ExistingShortcutPath(macro.Name,directory);
        if(!File.Exists(path))return null;
        string upgraded=CreateMacroShortcut(macro,directory,executablePath);if(!path.Equals(upgraded,StringComparison.OrdinalIgnoreCase))File.Delete(path);return upgraded;
    }
    static string ShortcutPath(string macroName,string directory)
    {
        string fileName=string.Concat(macroName.Trim().Select(x=>Path.GetInvalidFileNameChars().Contains(x)?'_':x));if(string.IsNullOrWhiteSpace(fileName))fileName="RELYR マクロ";
        return Path.Combine(directory,fileName+" - RELYR.lnk");
    }
    static string ExistingShortcutPath(string macroName,string directory)
    {
        string current=ShortcutPath(macroName,directory);if(File.Exists(current))return current;
        string fileName=string.Concat(macroName.Trim().Select(x=>Path.GetInvalidFileNameChars().Contains(x)?'_':x));if(string.IsNullOrWhiteSpace(fileName))fileName="Input Customizer マクロ";
        return Path.Combine(directory,fileName+" - Input Customizer.lnk");
    }
}
