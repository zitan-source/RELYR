using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RELYR;

internal sealed record UpdateInfo(Version Version,string VersionText,Uri InstallerUri,Uri ChecksumUri,string? ApiDigest,string InstallerFileName);

internal static partial class UpdateService
{
    internal const string LatestReleaseApi="https://api.github.com/repos/zitan-source/RELYR/releases/latest";
    static readonly HttpClient Client=CreateClient();

    static HttpClient CreateClient()
    {
        var client=new HttpClient{Timeout=TimeSpan.FromSeconds(15)};
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RELYR-Updater","1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version","2022-11-28");
        return client;
    }

    internal static async Task<UpdateInfo?> CheckAsync(Version currentVersion,CancellationToken cancellationToken)
    {
        string json=await Client.GetStringAsync(LatestReleaseApi,cancellationToken).ConfigureAwait(false);
        return ParseLatestRelease(json,currentVersion);
    }

    internal static UpdateInfo? ParseLatestRelease(string json,Version currentVersion)
    {
        using var document=JsonDocument.Parse(json);
        var root=document.RootElement;
        if(root.TryGetProperty("draft",out var draft)&&draft.GetBoolean())return null;
        if(root.TryGetProperty("prerelease",out var prerelease)&&prerelease.GetBoolean())return null;
        if(!root.TryGetProperty("tag_name",out var tagElement))return null;
        string tag=tagElement.GetString()?.Trim()??"";
        string versionText=tag.TrimStart('v','V');
        if(!Version.TryParse(versionText,out var releaseVersion)||releaseVersion<=currentVersion)return null;
        if(!root.TryGetProperty("assets",out var assets)||assets.ValueKind!=JsonValueKind.Array)return null;

        string installerName=$"RELYR-Setup-{versionText}.exe";
        JsonElement? installer=null,checksum=null;
        foreach(var asset in assets.EnumerateArray())
        {
            string name=asset.TryGetProperty("name",out var nameElement)?nameElement.GetString()??"":"";
            if(name.Equals(installerName,StringComparison.OrdinalIgnoreCase))installer=asset;
            else if(name.Equals(installerName+".sha256",StringComparison.OrdinalIgnoreCase))checksum=asset;
        }
        if(installer is not { } installerAsset||checksum is not { } checksumAsset)return null;
        if(!TryGetTrustedDownloadUri(installerAsset,out var installerUri)||!TryGetTrustedDownloadUri(checksumAsset,out var checksumUri))return null;
        string? digest=installerAsset.TryGetProperty("digest",out var digestElement)?digestElement.GetString():null;
        return new UpdateInfo(releaseVersion,versionText,installerUri,checksumUri,digest,installerName);
    }

    static bool TryGetTrustedDownloadUri(JsonElement asset,out Uri uri)
    {
        uri=null!;
        if(asset.TryGetProperty("state",out var state)&&!string.Equals(state.GetString(),"uploaded",StringComparison.OrdinalIgnoreCase))return false;
        if(!asset.TryGetProperty("browser_download_url",out var value)||!Uri.TryCreate(value.GetString(),UriKind.Absolute,out var parsed))return false;
        if(parsed.Scheme!=Uri.UriSchemeHttps||!parsed.Host.Equals("github.com",StringComparison.OrdinalIgnoreCase)||!parsed.AbsolutePath.StartsWith("/zitan-source/RELYR/releases/download/",StringComparison.OrdinalIgnoreCase))return false;
        uri=parsed;return true;
    }

    internal static async Task<string> DownloadAndVerifyAsync(UpdateInfo update,CancellationToken cancellationToken)
    {
        string directory=Path.Combine(Path.GetTempPath(),"RELYR-Update");
        Directory.CreateDirectory(directory);
        string installerPath=Path.Combine(directory,update.InstallerFileName);
        string temporaryPath=installerPath+".download";
        try
        {
            foreach(string oldFile in Directory.EnumerateFiles(directory))
                try{File.Delete(oldFile);}catch(IOException){}catch(UnauthorizedAccessException){}
            string checksumText=await Client.GetStringAsync(update.ChecksumUri,cancellationToken).ConfigureAwait(false);
            var checksumMatch=Sha256Regex().Match(checksumText);
            if(!checksumMatch.Success)throw new InvalidDataException("更新ファイルのSHA-256を読み取れませんでした。");
            string expected=checksumMatch.Value.ToUpperInvariant();
            await DownloadFileAsync(update.InstallerUri,temporaryPath,cancellationToken).ConfigureAwait(false);
            string actual;
            await using(var installerStream=File.OpenRead(temporaryPath))
                actual=Convert.ToHexString(await SHA256.HashDataAsync(installerStream,cancellationToken).ConfigureAwait(false));
            if(!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected),Convert.FromHexString(actual)))
                throw new InvalidDataException("更新ファイルのSHA-256が一致しません。安全のため更新を中止しました。");
            if(update.ApiDigest?.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase)==true)
            {
                string apiHash=update.ApiDigest[7..].Trim();
                if(!apiHash.Equals(actual,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("GitHubが示す更新ファイルのSHA-256と一致しません。安全のため更新を中止しました。");
            }
            File.Move(temporaryPath,installerPath,true);
            return installerPath;
        }
        catch
        {
            try{File.Delete(temporaryPath);}catch{}
            throw;
        }
    }

    static async Task DownloadFileAsync(Uri uri,string destination,CancellationToken cancellationToken)
    {
        using var response=await Client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var source=await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target=new FileStream(destination,FileMode.Create,FileAccess.Write,FileShare.None,81920,FileOptions.Asynchronous);
        await source.CopyToAsync(target,cancellationToken).ConfigureAwait(false);
    }

    [GeneratedRegex(@"\b[0-9a-fA-F]{64}\b",RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}
