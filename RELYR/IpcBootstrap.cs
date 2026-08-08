using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;

namespace RELYR;

internal sealed class IpcBootstrapServer : IAsyncDisposable
{
    readonly string pipeName;
    readonly string secret;
    readonly string expectedClientPath;
    readonly CancellationTokenSource stop = new();
    readonly Task runTask;
    NamedPipeServerStream? pipe;
    string outcome = "waiting for elevated helper";
    internal bool Succeeded
    {
        get; private set;
    }
    internal string Outcome => Volatile.Read(ref outcome);

    internal IpcBootstrapServer(string pipeName, string secret, string expectedClientPath)
    {
        this.pipeName = pipeName;
        this.secret = secret;
        this.expectedClientPath = expectedClientPath;
        runTask = Task.Run(RunAsync);
    }

    async Task RunAsync()
    {
        try
        {
            Volatile.Write(ref outcome, "creating bootstrap pipe");
            pipe = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, IpcTransport.CreateCurrentUserPipeSecurity());
            await pipe.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);
            Volatile.Write(ref outcome, "elevated helper connected; validating identity");
            using var identity = IpcProcessIdentity.FromPipe(pipe.SafePipeHandle, true);
            if (identity == null)
            {
                Volatile.Write(ref outcome, "rejected: helper identity unavailable");
                return;
            }
            if (!identity.MatchesCurrentUser())
            {
                Volatile.Write(ref outcome, "rejected: helper user or session mismatch");
                return;
            }
            if (!identity.MatchesExecutable(expectedClientPath))
            {
                Volatile.Write(ref outcome, "rejected: helper executable mismatch");
                return;
            }
            if (!identity.MatchesElevation(true))
            {
                Volatile.Write(ref outcome, "rejected: helper is not elevated");
                return;
            }
            await IpcTransport.WriteSecretAsync(pipe, secret, stop.Token).ConfigureAwait(false);
            Succeeded = true;
            Volatile.Write(ref outcome, "secret delivered to verified elevated helper");
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch (IOException error) { Volatile.Write(ref outcome, "bootstrap I/O failure: " + error.GetType().Name); }
        catch (ObjectDisposedException error) { Volatile.Write(ref outcome, "bootstrap disposal: " + error.GetType().Name); }
        catch (Exception error) { Volatile.Write(ref outcome, "bootstrap failure: " + error.GetType().Name); }
        finally { try { pipe?.Disconnect(); } catch { } pipe?.Dispose(); pipe = null; }
    }

    internal Task Completion => runTask;
    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        try
        {
            pipe?.Dispose();
        }
        catch { }
        try
        {
            await runTask.ConfigureAwait(false);
        }
        catch { }
        stop.Dispose();
    }
}

internal static class IpcBootstrapClient
{
    internal static async Task<string?> ReceiveSecretAsync(string pipeName, string expectedServerPath, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await pipe.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);
            using var identity = IpcProcessIdentity.FromPipe(pipe.SafePipeHandle, false);
            if (identity == null || !identity.MatchesCurrentUser() || !identity.MatchesExecutable(expectedServerPath) || identity.IsElevated)
                return null;
            return await IpcTransport.ReadSecretAsync(pipe, timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { return null; }
        catch (IOException) { return null; }
    }
}
