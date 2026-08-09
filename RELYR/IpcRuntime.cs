using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace RELYR;

internal static class IpcRuntime
{
    static ElevatedIpcClient? client;
    static readonly Lock sync = new();
    static int shutdownRequested;

    internal static bool IsUiHost
    {
        get; set;
    }
    internal static bool IsElevatedHelper
    {
        get; set;
    }
    internal static bool IsConnected
    {
        get
        {
            lock (sync)
                return client != null;
        }
    }
    internal static IpcPeerInfo? HelperIdentity
    {
        get
        {
            lock (sync)
            {
                return client?.ServerIdentity;
            }
        }
    }

    internal static async Task<bool> StartUiHostAsync(MainWindow window)
    {
        if (!IsUiHost || StartupService.IsProcessElevated())
            return false;
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("RELYRの実行パスを取得できません。");
        string pipeName = IpcTransport.NewName("main");
        string bootstrapName = IpcTransport.NewName("bootstrap");
        string secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        DeckIpcDiagnostics.LogIpcStartup("UI helper startup", $"begin; Pipe={pipeName}; Bootstrap={bootstrapName}");
        await using var bootstrap = new IpcBootstrapServer(bootstrapName, secret, executable);
        if (!TryStartElevatedHelper(executable, pipeName, bootstrapName, out string error))
        {
            DeckIpcDiagnostics.LogIpcStartup("UI helper startup", "launch failed: " + error);
            Debug.WriteLine(error);
            return false;
        }
        DeckIpcDiagnostics.LogIpcStartup("UI helper startup", "launch task accepted");

        // The helper must first receive the one-time secret through the verified
        // bootstrap pipe. Starting the main-pipe timeout before that cold start
        // completes can let an almost-expired client connect and immediately
        // disconnect, which also terminates the single-client helper server.
        await bootstrap.Completion.ConfigureAwait(false);
        if (!bootstrap.Succeeded)
        {
            DeckIpcDiagnostics.LogIpcStartup("UI helper startup", "bootstrap failed: " + bootstrap.Outcome);
            return false;
        }

        var candidate = new ElevatedIpcClient(pipeName, secret, executable);
        DateTime deadline = DateTime.UtcNow.AddSeconds(12);
        while (DateTime.UtcNow < deadline)
        {
            if (await candidate.ConnectAsync(TimeSpan.FromMilliseconds(700)).ConfigureAwait(false))
            {
                lock (sync)
                {
                    client = candidate;
                }
                DeckIpcDiagnostics.LogIpcStartup("UI helper startup", $"connected; HelperPid={candidate.ServerIdentity?.ProcessId}; HelperIntegrity={candidate.ServerIdentity?.IntegrityLevel}; Bootstrap={bootstrap.Outcome}");
                return true;
            }
            await Task.Delay(150).ConfigureAwait(false);
        }
        await candidate.DisposeAsync();
        DeckIpcDiagnostics.LogIpcStartup("UI helper startup", "connection failed; Bootstrap=" + bootstrap.Outcome);
        return false;
    }

    static bool TryStartElevatedHelper(string executable, string pipeName, string bootstrapName, out string error)
    {
#if !PRODUCTION_PUBLISH
        try
        {
            string arguments = $"--elevated-helper \"{pipeName}\" \"{bootstrapName}\"";
            Process.Start(new ProcessStartInfo(executable, arguments) { UseShellExecute = true, Verb = "runas" });
            error = "";
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
#else
        if(StartupService.TryRunElevated(["--elevated-helper",pipeName,bootstrapName],out error))return true;
        return false;
#endif
    }

    internal static async Task RunElevatedHelperAsync(string pipeName, string bootstrapName, MainWindow window)
    {
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("RELYR executable path is unavailable.");
        DeckIpcDiagnostics.LogIpcStartup("Helper bootstrap", "connecting to UI bootstrap pipe");
        string? secret = await IpcBootstrapClient.ReceiveSecretAsync(bootstrapName, executable, TimeSpan.FromSeconds(10)).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(secret))
        {
            DeckIpcDiagnostics.LogIpcStartup("Helper bootstrap", "secret was not received");
            return;
        }
        DeckIpcDiagnostics.LogIpcStartup("Helper bootstrap", "secret received; waiting for UI IPC connection");
        // The Deck/UI host intentionally remains non-elevated so Explorer can
        // deliver file drops.  Only this helper is elevated.
        await using var server = new ElevatedIpcServer(pipeName, secret, executable, (_, _) => Task.FromResult(new IpcMessage(IpcCommand.Ping, "", "ok", secret)), expectedClientElevated: false);
        server.ReplaceHandler((message, cancellationToken) => HandleHelperMessageAsync(message, cancellationToken, window, secret));
        await server.Completion.ConfigureAwait(false);
        DeckIpcDiagnostics.LogIpcStartup("Helper IPC", "server completed; Outcome=" + server.Outcome);
    }

    static async Task<IpcMessage> HandleHelperMessageAsync(IpcMessage message, CancellationToken cancellationToken, MainWindow window, string secret)
    {
        string result = message.Command switch
        {
            IpcCommand.Ping => "ok",
            IpcCommand.ReloadConfig => await InvokeOnWindowAsync(window, window.ReloadRuntimeConfigForIpc, cancellationToken).ConfigureAwait(false),
            IpcCommand.SetCapsLockRemap => SetCapsLockRemapOnHelper(message.Value),
            IpcCommand.ExecuteShortcut => await ExecuteShortcutOnHelperAsync(window, message.Value, cancellationToken).ConfigureAwait(false),
            IpcCommand.ExecuteText => await InvokeOnWindowAsync(window, () => window.ExecuteTextForIpc(message.Value), cancellationToken).ConfigureAwait(false),
            IpcCommand.ExecuteMouse => await InvokeOnWindowAsync(window, () => window.ExecuteMouseForIpc(message.Value), cancellationToken).ConfigureAwait(false),
            IpcCommand.Shutdown => await ShutdownHelperAsync(window, cancellationToken).ConfigureAwait(false),
            _ => "rejected"
        };
        return new IpcMessage(message.Command, message.RequestId, result, secret);
    }

    static string SetCapsLockRemapOnHelper(string value)
    {
        if (!bool.TryParse(value, out bool enabled))
            return "rejected";
        LegacyKeyRemapService.SetCapsLockToF13(enabled);
        return "ok";
    }

    static async Task<string> ExecuteShortcutOnHelperAsync(MainWindow window, string value, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<IpcShortcutRequest>(value)
            ?? throw new InvalidDataException("ショートカットIPC要求が不正です。");
        if (string.IsNullOrWhiteSpace(request.Shortcut))
            throw new InvalidDataException("ショートカットIPC要求が空です。");
        if (!Enum.IsDefined(request.WindowActionTarget))
            throw new InvalidDataException("ウィンドウ対象が不正です。");
        return await InvokeOnWindowAsync(window, () => window.ExecuteShortcutForIpc(request.Shortcut, request.WindowActionTarget), cancellationToken).ConfigureAwait(false);
    }

    static async Task<string> InvokeOnWindowAsync(MainWindow window, Action action, CancellationToken cancellationToken)
    {
        await window.Dispatcher.InvokeAsync(action).Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return "ok";
    }
    static Task<string> ShutdownHelperAsync(MainWindow window, CancellationToken cancellationToken)
    {
        // Return the acknowledgement first.  Exiting from inside the dispatcher
        // callback can otherwise tear down the pipe before the response frame is
        // flushed, leaving the UI host believing the helper crashed.
        window.Dispatcher.BeginInvoke(window.RequestApplicationExit);
        return Task.FromResult("ok");
    }

    internal static bool TrySendShortcut(string value, WindowActionTarget target)
        => TrySend(IpcCommand.ExecuteShortcut, JsonSerializer.Serialize(new IpcShortcutRequest(value, target)));
    internal static bool TrySendText(string value)
        => TrySend(IpcCommand.ExecuteText, value);
    internal static bool TrySendMouse(string value)
        => TrySend(IpcCommand.ExecuteMouse, value);
    internal static async Task<bool> TrySetCapsLockRemapAsync(bool enabled)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(12);
        do
        {
            if (await TrySendAsync(IpcCommand.SetCapsLockRemap, enabled.ToString()).ConfigureAwait(false))
                return true;
            await Task.Delay(100).ConfigureAwait(false);
        }
        while (DateTime.UtcNow < deadline);
        return false;
    }
    internal static void RequestReload()
        => _ = TrySendAsync(IpcCommand.ReloadConfig, "");

    static bool TrySend(IpcCommand command, string value)
        => TrySendAsync(command, value).GetAwaiter().GetResult();
    static async Task<bool> TrySendAsync(IpcCommand command, string value)
    {
        ElevatedIpcClient? current;
        lock (sync)
        {
            current = client;
        }
        if (current == null)
            return false;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            var response = await current.SendAsync(command, value, timeout.Token).ConfigureAwait(false);
            return response?.Value == "ok";
        }
        catch (OperationCanceledException) { return false; }
        catch (IOException) { return false; }
        catch (ObjectDisposedException) { return false; }
    }

    internal static async Task StopAsync()
    {
        if (Interlocked.Exchange(ref shutdownRequested, 1) != 0)
            return;
        ElevatedIpcClient? current;
        lock (sync)
        {
            current = client;
            client = null;
        }
        if (current != null)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await current.SendAsync(IpcCommand.Shutdown, cancellationToken: timeout.Token).ConfigureAwait(false);
            }
            catch { }
            await current.DisposeAsync();
        }
    }
}
