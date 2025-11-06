/**
 * dotnet new console -n ContextSensor
 * cd ContextSensor
 * dotnet run -c Release
 */
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

#region Program
internal static class Program
{
    public static void Main()
    {
        using var sensor = new MouseClickSensor(capacity: 1024);

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Native.PostQuitMessage(0);
        };

        sensor.Start();

        Native.MSG msg;
        while (Native.GetMessage(out msg, IntPtr.Zero, 0, 0)) { }
    }
}
#endregion

#region MouseClickSensor
internal sealed class MouseClickSensor : IDisposable
{
    private const int WH_MOUSE_LL = 14;
    private const int WM_LBUTTONDOWN = 0x0201;

    private readonly Channel<ClickEvent> _channel;
    private readonly CancellationTokenSource _cts;
    private Task? _consumer;
    private WindowsHook? _hook;

    public MouseClickSensor(int capacity = 1024)
    {
        _channel = Channel.CreateBounded<ClickEvent>(new BoundedChannelOptions(capacity)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        _cts = new();
    }

    public void Start()
    {
        _consumer = Task.Run(ConsumeAsync, _cts.Token);
        _hook = WindowsHook.Install(WH_MOUSE_LL, HookCallback);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONDOWN)
        {
            try
            {
                var hwnd = Native.GetForegroundWindow();
                if (hwnd != IntPtr.Zero) {
                    uint pid = 0;
                    Native.GetWindowThreadProcessId(hwnd, out pid);
                    _channel.Writer.TryWrite(new ClickEvent(pid));
                }
            }
            catch { }
        }
        return Native.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private async Task ConsumeAsync()
    {
        var reader = _channel.Reader;
        var ct = _cts.Token;

        await foreach (var ev in reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            var name = ProcessResolver.ResolveName(ev.Pid);
            Console.WriteLine($"WM_LBUTTONDOWN, {ev.Pid}, {name}");
        }
    }

    public void Dispose()
    {
        try { _channel.Writer.TryComplete(); } catch { }
        try { _cts.Cancel(); } catch { }
        try { _consumer?.Wait(2); } catch { }
        _hook?.Dispose();
        _cts.Dispose();
    }
}
#endregion

#region WindowsHook
internal sealed class WindowsHook : SafeHandleZeroOrMinusOneIsInvalid
{
    private readonly Native.LowLevelMouseProc _proc;

    private WindowsHook(IntPtr h, Native.LowLevelMouseProc proc) : base(true)
    {
        _proc = proc ?? throw new ArgumentNullException(nameof(proc));
        SetHandle(h);
    }

    public static WindowsHook Install(int idHook, Native.LowLevelMouseProc proc)
    {
        var hmod = Native.GetModuleHandle(null);
        var h = Native.SetWindowsHookEx(idHook, proc, hmod, 0);
        if (h == IntPtr.Zero)
            throw new System.ComponentModel.Win32Exception(
                Marshal.GetLastWin32Error(), "SetWindowsHookEx failed");

        return new WindowsHook(h, proc);
    }

    protected override bool ReleaseHandle()
    {
        GC.KeepAlive(_proc);
        return Native.UnhookWindowsHookEx(handle);
    }
}
#endregion

#region Native Interop
internal static class Native
{
    internal delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    internal static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    internal static extern bool GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    internal static extern void PostQuitMessage(int nExitCode);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }
}
#endregion

#region Data + Helper
internal readonly record struct ClickEvent(uint Pid);

internal static class ProcessResolver
{
    public static string ResolveName(uint pid)
    {
        if (pid == 0) return "unknown.exe";
        try
        {
            var p = Process.GetProcessById((int)pid);
            return $"{p.ProcessName}.exe";
        }
        catch
        {
            return "unknown.exe";
        }
    }
}
#endregion

