using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RELYR;

/// <summary>
/// A notification-area icon with one permanent Shell identity. WinForms'
/// NotifyIcon uses the legacy window/id identity, which allows each executable
/// copy used by a development build to become another entry in Windows Settings.
/// </summary>
internal sealed class StableNotifyIcon : IDisposable
{
    internal static readonly Guid Identifier = new("b0c52fd8-c5b7-48c0-83b2-9bfdcab49a68");

    const uint NimAdd = 0x00000000;
    const uint NimModify = 0x00000001;
    const uint NimDelete = 0x00000002;
    const uint NifMessage = 0x00000001;
    const uint NifIcon = 0x00000002;
    const uint NifTip = 0x00000004;
    const uint NifGuid = 0x00000020;
    const uint NifShowTip = 0x00000080;
    const int CallbackMessage = 0x0400 + 0x2A1;
    const int WmLeftButtonDoubleClick = 0x0203;
    const int WmRightButtonUp = 0x0205;
    const int WmContextMenu = 0x007B;

    readonly MessageWindow messageWindow;
    readonly uint taskbarCreatedMessage;
    System.Drawing.Icon? icon;
    ContextMenuStrip? contextMenuStrip;
    string text = "";
    bool visible;
    bool added;
    bool disposed;

    internal StableNotifyIcon()
    {
        taskbarCreatedMessage = RegisterWindowMessage("TaskbarCreated");
        messageWindow = new MessageWindow(CallbackMessage, taskbarCreatedMessage, HandleTrayMessage, RestoreAfterExplorerRestart);
    }

    internal event EventHandler? DoubleClick;

    internal ContextMenuStrip? ContextMenuStrip
    {
        get => contextMenuStrip;
        set => contextMenuStrip = value;
    }

    internal System.Drawing.Icon? Icon
    {
        get => icon;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            icon = value;
            Modify();
        }
    }

    internal string Text
    {
        get => text;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            text = value ?? "";
            Modify();
        }
    }

    internal bool Visible
    {
        get => visible;
        set
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (visible == value)
                return;
            visible = value;
            if (visible)
                Add();
            else
                Delete();
        }
    }

    void Add()
    {
        if (added || icon == null || messageWindow.Handle == IntPtr.Zero)
            return;
        var data = CreateData(NifMessage | NifIcon | NifTip | NifGuid | NifShowTip);
        added = Shell_NotifyIcon(NimAdd, ref data);
    }

    void Modify()
    {
        if (!visible)
            return;
        if (!added)
        {
            Add();
            return;
        }
        var data = CreateData(NifIcon | NifTip | NifGuid | NifShowTip);
        if (!Shell_NotifyIcon(NimModify, ref data))
        {
            added = false;
            Add();
        }
    }

    void Delete()
    {
        if (!added)
            return;
        var data = CreateData(NifGuid);
        Shell_NotifyIcon(NimDelete, ref data);
        added = false;
    }

    NotifyIconData CreateData(uint flags)
        => new()
        {
            cbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            hWnd = messageWindow.Handle,
            uFlags = flags,
            uCallbackMessage = CallbackMessage,
            hIcon = icon?.Handle ?? IntPtr.Zero,
            szTip = text.Length <= 127 ? text : text[..127],
            guidItem = Identifier
        };

    void RestoreAfterExplorerRestart()
    {
        added = false;
        if (visible)
            Add();
    }

    void HandleTrayMessage(int message)
    {
        if (message == WmLeftButtonDoubleClick)
        {
            DoubleClick?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (message is not (WmRightButtonUp or WmContextMenu) || contextMenuStrip == null)
            return;
        SetForegroundWindow(messageWindow.Handle);
        contextMenuStrip.Show(Cursor.Position);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        if (visible)
        {
            visible = false;
            Delete();
        }
        contextMenuStrip?.Dispose();
        contextMenuStrip = null;
        messageWindow.Dispose();
        disposed = true;
    }

    sealed class MessageWindow : NativeWindow, IDisposable
    {
        readonly int callbackMessage;
        readonly uint taskbarCreatedMessage;
        readonly Action<int> callback;
        readonly Action taskbarCreated;

        internal MessageWindow(int callbackMessage, uint taskbarCreatedMessage, Action<int> callback, Action taskbarCreated)
        {
            this.callbackMessage = callbackMessage;
            this.taskbarCreatedMessage = taskbarCreatedMessage;
            this.callback = callback;
            this.taskbarCreated = taskbarCreated;
            CreateHandle(new CreateParams { Caption = "RELYR.TrayIconHost" });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == callbackMessage)
            {
                callback(unchecked((int)(long)m.LParam));
                return;
            }
            if ((uint)m.Msg == taskbarCreatedMessage)
                taskbarCreated();
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (Handle != IntPtr.Zero)
                DestroyHandle();
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct NotifyIconData
    {
        internal uint cbSize;
        internal IntPtr hWnd;
        internal uint uID;
        internal uint uFlags;
        internal uint uCallbackMessage;
        internal IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] internal string szTip;
        internal uint dwState;
        internal uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] internal string szInfo;
        internal uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] internal string szInfoTitle;
        internal uint dwInfoFlags;
        internal Guid guidItem;
        internal IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint RegisterWindowMessage(string message);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool SetForegroundWindow(IntPtr window);
}
