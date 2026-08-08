using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;

namespace RELYR;

internal sealed record IpcPeerInfo(uint ProcessId, string ImagePath, bool IsElevated, string IntegrityLevel);

internal sealed class ElevatedIpcServer : IAsyncDisposable
{
    readonly string pipeName;
    readonly string secret;
    readonly string expectedClientPath;
    Func<IpcMessage, CancellationToken, Task<IpcMessage>> handler;
    readonly CancellationTokenSource stop = new();
    readonly Task runTask;
    NamedPipeServerStream? pipe;
    string outcome = "creating IPC pipe";

    readonly bool expectedClientElevated;

    internal ElevatedIpcServer(string pipeName, string secret, string expectedClientPath, Func<IpcMessage, CancellationToken, Task<IpcMessage>> handler, bool expectedClientElevated = true)
    {
        this.pipeName = pipeName;
        this.secret = secret;
        this.expectedClientPath = expectedClientPath;
        this.handler = handler;
        this.expectedClientElevated = expectedClientElevated;
        runTask = Task.Run(RunAsync);
    }

    internal void ReplaceHandler(Func<IpcMessage, CancellationToken, Task<IpcMessage>> replacement)
        => handler = replacement;
    internal string Outcome => Volatile.Read(ref outcome);

    async Task RunAsync()
    {
        try
        {
            Volatile.Write(ref outcome, "waiting for UI connection");
            pipe = NamedPipeServerStreamAcl.Create(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, IpcTransport.CreateCurrentUserPipeSecurity());
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(15));
            await pipe.WaitForConnectionAsync(connectTimeout.Token).ConfigureAwait(false);
            Volatile.Write(ref outcome, "validating UI identity");
            using var identity = IpcProcessIdentity.FromPipe(pipe.SafePipeHandle, true);
            if (identity == null)
            {
                Volatile.Write(ref outcome, "rejected: UI identity unavailable");
                return;
            }
            if (!identity.MatchesCurrentUser())
            {
                Volatile.Write(ref outcome, "rejected: UI user or session mismatch");
                return;
            }
            if (!identity.MatchesExecutable(expectedClientPath))
            {
                Volatile.Write(ref outcome, "rejected: UI executable mismatch");
                return;
            }
            if (!identity.MatchesElevation(expectedClientElevated))
            {
                Volatile.Write(ref outcome, $"rejected: UI elevation mismatch (actual={identity.IntegrityLevel})");
                return;
            }
            var first = await IpcTransport.ReadMessageAsync(pipe, stop.Token).ConfigureAwait(false);
            if (first == null)
            {
                Volatile.Write(ref outcome, "rejected: UI closed before handshake");
                return;
            }
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(first.Nonce)))
            {
                Volatile.Write(ref outcome, "rejected: UI handshake secret mismatch");
                return;
            }
            if (first.Command != IpcCommand.Ping)
            {
                Volatile.Write(ref outcome, "rejected: UI handshake command mismatch");
                return;
            }
            await IpcTransport.WriteMessageAsync(pipe, new IpcMessage(IpcCommand.Ping, first.RequestId, "ok", secret), stop.Token).ConfigureAwait(false);
            Volatile.Write(ref outcome, "connected");
            while (!stop.IsCancellationRequested)
            {
                var message = await IpcTransport.ReadMessageAsync(pipe, stop.Token).ConfigureAwait(false);
                if (message == null)
                {
                    Volatile.Write(ref outcome, "UI disconnected");
                    break;
                }
                if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(message.Nonce)))
                {
                    Volatile.Write(ref outcome, "rejected: command secret mismatch");
                    break;
                }
                IpcMessage response;
                try
                {
                    response = await handler(message, stop.Token).ConfigureAwait(false);
                }
                catch (Exception ex) { response = new IpcMessage(message.Command, message.RequestId, "error", ex.GetType().Name); }
                await IpcTransport.WriteMessageAsync(pipe, response, stop.Token).ConfigureAwait(false);
                if (message.Command == IpcCommand.Shutdown)
                {
                    Volatile.Write(ref outcome, "shutdown requested by UI");
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { Volatile.Write(ref outcome, "stopped"); }
        catch (OperationCanceledException) { Volatile.Write(ref outcome, "connection timeout"); }
        catch (IOException error) { Volatile.Write(ref outcome, "I/O failure: " + error.GetType().Name); }
        catch (ObjectDisposedException error) { Volatile.Write(ref outcome, "disposed: " + error.GetType().Name); }
        catch (Exception error) { Volatile.Write(ref outcome, "failure: " + error.GetType().Name); }
        finally
        {
            try
            {
                pipe?.Disconnect();
            }
            catch { }
            pipe?.Dispose();
            pipe = null;
        }
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

internal sealed class ElevatedIpcClient : IAsyncDisposable
{
    readonly string pipeName;
    readonly string secret;
    readonly string expectedServerPath;
    readonly bool expectedServerElevated;
    readonly SemaphoreSlim gate = new(1, 1);
    NamedPipeClientStream? pipe;
    bool connected;
    internal IpcPeerInfo? ServerIdentity
    {
        get; private set;
    }

    internal ElevatedIpcClient(string pipeName, string secret, string expectedServerPath, bool expectedServerElevated = true)
    {
        this.pipeName = pipeName;
        this.secret = secret;
        this.expectedServerPath = expectedServerPath;
        this.expectedServerElevated = expectedServerElevated;
    }

    internal async Task<bool> ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (connected)
            return true;
        var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await client.ConnectAsync(timeoutSource.Token).ConfigureAwait(false);
            using var identity = IpcProcessIdentity.FromPipe(client.SafePipeHandle, false);
            if (identity == null || !identity.MatchesCurrentUser() || !identity.MatchesExecutable(expectedServerPath) || !identity.MatchesElevation(expectedServerElevated))
            {
                client.Dispose();
                return false;
            }
            ServerIdentity = new IpcPeerInfo(identity.ProcessId, identity.ImagePath, identity.IsElevated, identity.IntegrityLevel);
            pipe = client;
            var response = await SendCoreAsync(new IpcMessage(IpcCommand.Ping, Guid.NewGuid().ToString("N"), "", secret), timeoutSource.Token).ConfigureAwait(false);
            connected = response.Command == IpcCommand.Ping && response.Value == "ok";
            if (!connected)
            {
                pipe.Dispose();
                pipe = null;
            }
            return connected;
        }
        catch (OperationCanceledException) { client.Dispose(); return false; }
        catch (IOException) { client.Dispose(); return false; }
    }

    internal async Task<IpcMessage?> SendAsync(IpcCommand command, string value = "", CancellationToken cancellationToken = default)
    {
        if (!connected || pipe == null)
            return null;
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SendCoreAsync(new IpcMessage(command, Guid.NewGuid().ToString("N"), value, secret), cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { connected = false; return null; }
        catch (IOException) { connected = false; return null; }
        catch (ObjectDisposedException) { connected = false; return null; }
        finally { gate.Release(); }
    }

    async Task<IpcMessage> SendCoreAsync(IpcMessage message, CancellationToken cancellationToken)
    {
        if (pipe == null)
            throw new IOException("IPCが接続されていません。");
        await IpcTransport.WriteMessageAsync(pipe, message, cancellationToken).ConfigureAwait(false);
        var response = await IpcTransport.ReadMessageAsync(pipe, cancellationToken).ConfigureAwait(false) ?? throw new EndOfStreamException();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(response.Nonce))
           || !response.RequestId.Equals(message.RequestId, StringComparison.Ordinal)
           || response.Command != message.Command)
            throw new InvalidDataException("IPC response validation failed.");
        return response;
    }

    public async ValueTask DisposeAsync()
    {
        connected = false;
        try
        {
            pipe?.Dispose();
        }
        catch { }
        gate.Dispose();
        await Task.CompletedTask;
    }
}
