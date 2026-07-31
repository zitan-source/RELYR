using System.IO;

namespace RELYR;

internal static class VerificationPaths
{
    static readonly Lazy<string> RootValue=new(ResolveRoot);

    internal static string Root=>RootValue.Value;
    internal static string GetFile(string name)=>Path.Combine(Root,name);
    internal static string CreateRunDirectory(string prefix)
    {
        string path=Path.Combine(Root,prefix+"-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    static string ResolveRoot()
    {
        string? configured=Environment.GetEnvironmentVariable("RELYR_VERIFICATION_DIR");
        if(!string.IsNullOrWhiteSpace(configured))
        {
            string explicitPath=Path.GetFullPath(configured);
            Directory.CreateDirectory(explicitPath);
            return explicitPath;
        }

        for(var directory=new DirectoryInfo(AppContext.BaseDirectory);directory!=null;directory=directory.Parent)
        {
            if(File.Exists(Path.Combine(directory.FullName,"RELYR","RELYR.csproj")))
            {
                string repositoryPath=Path.Combine(directory.FullName,".verification");
                Directory.CreateDirectory(repositoryPath);
                return repositoryPath;
            }
        }

        string fallback=Path.Combine(AppContext.BaseDirectory,".verification");
        Directory.CreateDirectory(fallback);
        return fallback;
    }
}
