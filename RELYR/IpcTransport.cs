using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace RELYR;

internal enum IpcCommand
{
    Ping,
    Shutdown,
    ReloadConfig,
    SetCapsLockRemap,
    ExecuteShortcut,
    ExecuteText,
    ExecuteMouse,
    ReadHardwareSensors
}

internal sealed record IpcMessage(IpcCommand Command, string RequestId, string Value, string Nonce);
internal sealed record IpcShortcutRequest(string Shortcut, WindowActionTarget WindowActionTarget);

internal static class IpcTransport
{
    internal const int MaxFrameBytes = 64 * 1024;
    internal static string NewName(string prefix)
        => $"RELYR-{prefix}-{Environment.ProcessId}-{Guid.NewGuid():N}";

    internal static PipeSecurity CreateCurrentUserPipeSecurity()
    {
        var sid = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("現在のユーザーSIDを取得できません。");
        var security = new PipeSecurity();
        security.SetAccessRule(new PipeAccessRule(sid, PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
        return security;
    }

    internal static async Task WriteMessageAsync(Stream stream, IpcMessage message, CancellationToken cancellationToken)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(message);
        if (payload.Length > MaxFrameBytes)
            throw new InvalidDataException("IPCメッセージが大きすぎます。");
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<IpcMessage?> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[4];
        if (!await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false))
            return null;
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaxFrameBytes)
            throw new InvalidDataException("IPCメッセージ長が不正です。");
        byte[] payload = new byte[length];
        if (!await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false))
            return null;
        return JsonSerializer.Deserialize<IpcMessage>(payload);
    }

    internal static async Task WriteSecretAsync(Stream stream, string secret, CancellationToken cancellationToken)
    {
        byte[] payload = Encoding.UTF8.GetBytes(secret);
        if (payload.Length > 1024)
            throw new InvalidDataException("IPCブートストラップ情報が大きすぎます。");
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<string?> ReadSecretAsync(Stream stream, CancellationToken cancellationToken)
    {
        byte[] header = new byte[4];
        if (!await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false))
            return null;
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > 1024)
            throw new InvalidDataException("IPCブートストラップ情報が不正です。");
        byte[] payload = new byte[length];
        if (!await ReadExactAsync(stream, payload, cancellationToken).ConfigureAwait(false))
            return null;
        return Encoding.UTF8.GetString(payload);
    }

    static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                return false;
            offset += read;
        }
        return true;
    }
}

internal sealed class IpcProcessIdentity : IDisposable
{
    internal uint ProcessId
    {
        get;
    }
    internal string ImagePath
    {
        get;
    }
    internal string UserSid
    {
        get;
    }
    internal uint SessionId
    {
        get;
    }
    internal long StartTimeFileTime
    {
        get;
    }
    internal uint ParentProcessId
    {
        get;
    }
    internal bool IsElevated
    {
        get;
    }
    internal string IntegrityLevel
    {
        get;
    }
    readonly SafeProcessHandle processHandle;

    IpcProcessIdentity(uint processId, SafeProcessHandle handle, string imagePath, string userSid, uint sessionId, long startTimeFileTime, uint parentProcessId, bool isElevated, string integrityLevel)
    {
        ProcessId = processId;
        processHandle = handle;
        ImagePath = imagePath;
        UserSid = userSid;
        SessionId = sessionId;
        StartTimeFileTime = startTimeFileTime;
        ParentProcessId = parentProcessId;
        IsElevated = isElevated;
        IntegrityLevel = integrityLevel;
    }

    internal bool HasExited => GetExitCodeProcess(processHandle, out uint code) && code != 259;

    internal static IpcProcessIdentity? FromPipe(SafePipeHandle pipe, bool client)
    {
        bool ok = client ? GetNamedPipeClientProcessId(pipe, out uint pid) : GetNamedPipeServerProcessId(pipe, out pid);
        if (!ok || pid == 0)
            return null;
        const uint ProcessQueryLimitedInformation = 0x1000;
        const uint Synchronize = 0x00100000;
        var handle = new SafeProcessHandle(OpenProcess(ProcessQueryLimitedInformation | Synchronize, false, pid), true);
        if (handle.IsInvalid)
            return null;
        try
        {
            string path = QueryImagePath(handle);
            string sid = QueryUserSid(handle);
            if (!ProcessIdToSessionId(pid, out uint session))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            if (!GetProcessTimes(handle, out _, out _, out _, out long creation))
                throw new Win32Exception(Marshal.GetLastWin32Error());
            uint parent = QueryParentProcessId(handle);
            bool elevated = QueryIsElevated(handle);
            string integrity = QueryIntegrityLevel(handle);
            return new IpcProcessIdentity(pid, handle, path, sid, session, creation, parent, elevated, integrity);
        }
        catch { handle.Dispose(); return null; }
    }

    internal static bool TryGetProcessImagePath(uint processId, out string imagePath)
    {
        const uint ProcessQueryLimitedInformation = 0x1000;
        using var handle = new SafeProcessHandle(OpenProcess(ProcessQueryLimitedInformation, false, processId), true);
        if (handle.IsInvalid)
        {
            imagePath = "";
            return false;
        }
        try
        {
            imagePath = QueryImagePath(handle);
            return !string.IsNullOrWhiteSpace(imagePath);
        }
        catch
        {
            imagePath = "";
            return false;
        }
    }

    static string QueryImagePath(SafeProcessHandle handle)
    {
        var buffer = new StringBuilder(1024);
        int length = buffer.Capacity;
        if (!QueryFullProcessImageName(handle, 0, buffer, ref length))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return buffer.ToString();
    }

    static string QueryUserSid(SafeProcessHandle process)
    {
        if (!OpenProcessToken(process, 0x0008, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        {
            GetTokenInformation(token, 1, IntPtr.Zero, 0, out int length);
            if (length <= 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            IntPtr buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformation(token, 1, buffer, length, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var user = Marshal.PtrToStructure<TOKEN_USER>(buffer);
                var sid = new SecurityIdentifier(user.User.Sid);
                return sid.Value;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }

    internal bool MatchesCurrentUser()
        => string.Equals(UserSid, WindowsIdentity.GetCurrent().User?.Value, StringComparison.OrdinalIgnoreCase)
          && SessionId == Process.GetCurrentProcess().SessionId;

    internal bool MatchesExecutable(string expected)
        => Path.GetFullPath(ImagePath).Equals(Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase);

    internal bool MatchesElevation(bool expectedElevated) => IsElevated == expectedElevated;

    internal static string CurrentIntegrityLevel()
    {
        using var process = Process.GetCurrentProcess();
        try
        {
            return QueryIntegrityLevel(process.SafeHandle);
        }
        catch { return "Unknown"; }
    }

    internal static bool IsProcessElevated(uint processId)
    {
        const uint ProcessQueryLimitedInformation = 0x1000;
        using var process = new SafeProcessHandle(OpenProcess(ProcessQueryLimitedInformation, false, processId), true);
        if (process.IsInvalid)
            return false;
        try
        {
            return QueryIsElevated(process);
        }
        catch { return false; }
    }

    static uint QueryParentProcessId(SafeProcessHandle process)
    {
        var info = new PROCESS_BASIC_INFORMATION();
        int status = NtQueryInformationProcess(process, 0, ref info, Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _);
        return status == 0 ? (uint)info.InheritedFromUniqueProcessId.ToInt64() : 0;
    }

    static bool QueryIsElevated(SafeProcessHandle process)
    {
        const uint TokenQuery = 0x0008;
        const int TokenElevation = 20;
        if (!OpenProcessToken(process, TokenQuery, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        {
            int size = Marshal.SizeOf<TOKEN_ELEVATION>();
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                if (!GetTokenInformation(token, TokenElevation, buffer, size, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                return Marshal.PtrToStructure<TOKEN_ELEVATION>(buffer).TokenIsElevated != 0;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }

    static string QueryIntegrityLevel(SafeProcessHandle process)
    {
        const uint TokenQuery = 0x0008;
        const int TokenIntegrityLevel = 25;
        if (!OpenProcessToken(process, TokenQuery, out var token))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        using (token)
        {
            GetTokenInformation(token, TokenIntegrityLevel, IntPtr.Zero, 0, out int length);
            if (length <= 0)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            IntPtr buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformation(token, TokenIntegrityLevel, buffer, length, out _))
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                var label = Marshal.PtrToStructure<TOKEN_MANDATORY_LABEL>(buffer);
                string sid = new SecurityIdentifier(label.Label.Sid).Value;
                int.TryParse(sid[(sid.LastIndexOf('-') + 1)..], out int rid);
                return rid switch
                {
                    >= 20480 => "Protected",
                    >= 16384 => "System",
                    >= 12288 => "High",
                    >= 8192 => "Medium",
                    >= 4096 => "Low",
                    >= 0 => "Untrusted",
                    _ => "Unknown"
                };
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
    }

    public void Dispose() => processHandle.Dispose();

    [StructLayout(LayoutKind.Sequential)]
    struct SID_AND_ATTRIBUTES
    {
        internal IntPtr Sid; internal uint Attributes;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct TOKEN_USER
    {
        internal SID_AND_ATTRIBUTES User;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct TOKEN_MANDATORY_LABEL
    {
        internal SID_AND_ATTRIBUTES Label;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct TOKEN_ELEVATION
    {
        internal int TokenIsElevated;
    }
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_BASIC_INFORMATION
    {
        internal IntPtr Reserved1, PebBaseAddress, Reserved2_0, Reserved2_1, UniqueProcessId, InheritedFromUniqueProcessId;
    }

    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeClientProcessId(SafePipeHandle pipe, out uint clientProcessId);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder name, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetExitCodeProcess(SafeProcessHandle process, out uint code);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool GetTokenInformation(SafeAccessTokenHandle token, int type, IntPtr buffer, int length, out int returnLength);
    [DllImport("ntdll.dll")] static extern int NtQueryInformationProcess(SafeProcessHandle process, int informationClass, ref PROCESS_BASIC_INFORMATION information, int informationLength, out int returnLength);
}
