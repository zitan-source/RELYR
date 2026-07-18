using System.IO;

namespace RELYR;

public static class StartupIntegrationTest
{
    public static int Run(TextWriter output)
    {
        var failures=new List<string>();
        void Check(bool value,string name){output.WriteLine((value?"PASS ":"FAIL ")+name);if(!value)failures.Add(name);}
        try
        {
            const string executable=@"C:\Program Files\RELYR\RELYR.exe";
            Check(StartupService.BuildCommand(executable)==$"\"{executable}\" --tray","startup task launches the installed executable in tray mode");
            string launcher=StartupService.BuildLauncherCommand(executable);
            Check(launcher==$"\"{executable}\" --elevated-task \"$(Arg0)\"","manual launch task accepts a safe encoded argument payload");
            string[] arguments=["--macro-id","日本語 マクロ","quoted \"value\""];
            string encoded=StartupService.EncodeArguments(arguments);
            Check(StartupService.DecodeElevatedArguments(encoded).SequenceEqual(arguments),"scheduled-task argument forwarding preserves every argument exactly");
            Check(!encoded.Any(char.IsWhiteSpace),"scheduled-task argument payload contains no command-line whitespace");
        }
        catch(Exception ex){output.WriteLine("FAIL exception: "+ex);failures.Add("exception");}
        output.WriteLine(failures.Count==0?"STARTUP INTEGRATION TEST PASSED":"STARTUP INTEGRATION TEST FAILED");return failures.Count==0?0:1;
    }
}
