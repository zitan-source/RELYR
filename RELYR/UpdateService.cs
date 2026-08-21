using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RELYR;

internal sealed record UpdateInfo(Version Version, string VersionText, Uri InstallerUri, Uri ChecksumUri, string? ApiDigest, string InstallerFileName, string ReleaseNotes = "");
internal sealed record UpdateCheckResult(Version LatestVersion, string LatestVersionText, UpdateInfo? AvailableUpdate, DateTimeOffset CheckedAt);
internal sealed record UpdateDownloadProgress(long BytesReceived, long? TotalBytes, double? Percentage);

internal static partial class UpdateService
{
    internal const string LatestReleaseApi = "https://api.github.com/repos/zitan-source/RELYR/releases/latest";
    static readonly HttpClient Client = CreateClient();

    static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RELYR-Updater", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    internal static async Task<UpdateInfo?> CheckAsync(Version currentVersion, CancellationToken cancellationToken)
        => (await CheckLatestAsync(currentVersion, cancellationToken).ConfigureAwait(false)).AvailableUpdate;

    internal static async Task<UpdateCheckResult> CheckLatestAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        string json = await Client.GetStringAsync(LatestReleaseApi, cancellationToken).ConfigureAwait(false);
        var (latestVersion, latestVersionText) = ParseLatestVersion(json);
        return new UpdateCheckResult(latestVersion, latestVersionText, ParseLatestRelease(json, currentVersion), DateTimeOffset.Now);
    }

    internal static (Version Version, string VersionText) ParseLatestVersion(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            throw new InvalidDataException("公開済みの更新情報を確認できませんでした。");
        if (root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean())
            throw new InvalidDataException("公開済みの更新情報を確認できませんでした。");
        if (!root.TryGetProperty("tag_name", out var tagElement))
            throw new InvalidDataException("更新情報にバージョンがありません。");
        string versionText = tagElement.GetString()?.Trim().TrimStart('v', 'V') ?? "";
        if (!Version.TryParse(versionText, out var version))
            throw new InvalidDataException("更新情報のバージョンを読み取れませんでした。");
        return (version, versionText);
    }

    internal static UpdateInfo? ParseLatestRelease(string json, Version currentVersion)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean())
            return null;
        if (root.TryGetProperty("prerelease", out var prerelease) && prerelease.GetBoolean())
            return null;
        if (!root.TryGetProperty("tag_name", out var tagElement))
            return null;
        string tag = tagElement.GetString()?.Trim() ?? "";
        string versionText = tag.TrimStart('v', 'V');
        if (!Version.TryParse(versionText, out var releaseVersion) || releaseVersion <= currentVersion)
            return null;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        string installerName = $"RELYR-Update-{versionText}.exe";
        JsonElement? installer = null, checksum = null;
        foreach (var asset in assets.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? "" : "";
            if (name.Equals(installerName, StringComparison.OrdinalIgnoreCase))
                installer = asset;
            else if (name.Equals(installerName + ".sha256", StringComparison.OrdinalIgnoreCase))
                checksum = asset;
        }
        if (installer is not { } installerAsset || checksum is not { } checksumAsset)
            return null;
        if (!TryGetTrustedDownloadUri(installerAsset, out var installerUri) || !TryGetTrustedDownloadUri(checksumAsset, out var checksumUri))
            return null;
        string? digest = installerAsset.TryGetProperty("digest", out var digestElement) ? digestElement.GetString() : null;
        string releaseNotes = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString()?.Trim() ?? "" : "";
        return new UpdateInfo(releaseVersion, versionText, installerUri, checksumUri, digest, installerName, releaseNotes);
    }

    static bool TryGetTrustedDownloadUri(JsonElement asset, out Uri uri)
    {
        uri = null!;
        if (asset.TryGetProperty("state", out var state) && !string.Equals(state.GetString(), "uploaded", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!asset.TryGetProperty("browser_download_url", out var value) || !Uri.TryCreate(value.GetString(), UriKind.Absolute, out var parsed))
            return false;
        if (parsed.Scheme != Uri.UriSchemeHttps || !parsed.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || !parsed.AbsolutePath.StartsWith("/zitan-source/RELYR/releases/download/", StringComparison.OrdinalIgnoreCase))
            return false;
        uri = parsed;
        return true;
    }

    internal static async Task<string> DownloadAndVerifyAsync(UpdateInfo update, CancellationToken cancellationToken, IProgress<UpdateDownloadProgress>? progress = null)
    {
        string directory = Path.Combine(Path.GetTempPath(), "RELYR-Update");
        Directory.CreateDirectory(directory);
        string installerPath = Path.Combine(directory, update.InstallerFileName);
        string temporaryPath = installerPath + ".download";
        try
        {
            foreach (string oldFile in Directory.EnumerateFiles(directory))
                try
                {
                    File.Delete(oldFile);
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            string checksumText = await Client.GetStringAsync(update.ChecksumUri, cancellationToken).ConfigureAwait(false);
            var checksumMatch = Sha256Regex().Match(checksumText);
            if (!checksumMatch.Success)
                throw new InvalidDataException("更新ファイルのSHA-256を読み取れませんでした。");
            string expected = checksumMatch.Value.ToUpperInvariant();
            await DownloadFileAsync(update.InstallerUri, temporaryPath, cancellationToken, progress).ConfigureAwait(false);
            string actual;
            await using (var installerStream = File.OpenRead(temporaryPath))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(installerStream, cancellationToken).ConfigureAwait(false));
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(actual)))
                throw new InvalidDataException("更新ファイルのSHA-256が一致しません。安全のため更新を中止しました。");
            if (update.ApiDigest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
            {
                string apiHash = update.ApiDigest[7..].Trim();
                if (!apiHash.Equals(actual, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("GitHubが示す更新ファイルのSHA-256と一致しません。安全のため更新を中止しました。");
            }
            File.Move(temporaryPath, installerPath, true);
            return installerPath;
        }
        catch
        {
            try
            {
                File.Delete(temporaryPath);
            }
            catch { }
            throw;
        }
    }

    static async Task DownloadFileAsync(Uri uri, string destination, CancellationToken cancellationToken, IProgress<UpdateDownloadProgress>? progress)
    {
        using var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous);
        long? total = response.Content.Headers.ContentLength;
        long received = 0;
        byte[] buffer = new byte[81920];
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
                break;
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            progress?.Report(new UpdateDownloadProgress(received, total, total is > 0 ? received * 100d / total.Value : null));
        }
        progress?.Report(new UpdateDownloadProgress(received, total, total is > 0 ? 100d : null));
    }

    internal static string FriendlyError(Exception exception)
    {
        Exception error = exception is AggregateException aggregate && aggregate.InnerExceptions.Count == 1 ? aggregate.InnerExceptions[0] : exception;
        return error switch
        {
            TaskCanceledException => "通信がタイムアウトしました。インターネット接続を確認して、もう一度お試しください。",
            OperationCanceledException => "確認を中止しました。",
            HttpRequestException { StatusCode: HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests } => "GitHubへの確認回数が一時的な上限に達しました。しばらく待ってから、もう一度お試しください。",
            HttpRequestException => "更新サーバーへ接続できませんでした。インターネット接続を確認して、もう一度お試しください。",
            JsonException or InvalidDataException => "更新情報を正しく読み取れませんでした。時間を置いて、もう一度お試しください。",
            UnauthorizedAccessException => "更新ファイルを保存する権限がありません。RELYRを再起動して、もう一度お試しください。",
            IOException => "更新ファイルを保存できませんでした。空き容量を確認して、もう一度お試しください。",
            _ => "アップデートを完了できませんでした。時間を置いて、もう一度お試しください。"
        };
    }

    [GeneratedRegex(@"\b[0-9a-fA-F]{64}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();
}
