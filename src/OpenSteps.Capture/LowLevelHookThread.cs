using System.Runtime.InteropServices;
using System.Threading;

namespace OpenSteps.Capture;

/// <summary>
/// Hosts a low-level hook on its own thread with a dedicated message loop. Windows routes all system input through
/// low-level hook callbacks, so running them on the busy UI thread makes the whole machine's mouse and keyboard lag.
/// </summary>
internal sealed class LowLevelHookThread
{
    private const uint WM_QUIT = 0x0012;
    private const uint PM_NOREMOVE = 0x0000;

    private readonly int _hookId;
    private readonly NativeMethods.LowLevelHookProc _callback;
    private Thread? _thread;
    private uint _threadId;

    public LowLevelHookThread(int hookId, NativeMethods.LowLevelHookProc callback)
    {
        _hookId = hookId;
        _callback = callback;
    }

    public bool IsRunning => _thread is not null;

    public void Start(string name)
    {
        if (IsRunning)
        {
            return;
        }

        using var ready = new ManualResetEventSlim();
        var installed = false;
        var thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            PeekMessage(out _, IntPtr.Zero, 0, 0, PM_NOREMOVE);
            var hookHandle = NativeMethods.SetWindowsHookEx(_hookId, _callback, IntPtr.Zero, 0);
            installed = hookHandle != IntPtr.Zero;
            ready.Set();
            if (!installed)
            {
                return;
            }

            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }

            NativeMethods.UnhookWindowsHookEx(hookHandle);
        })
        {
            IsBackground = true,
            Name = name
        };

        thread.Start();
        ready.Wait();
        if (!installed)
        {
            thread.Join();
            throw new InvalidOperationException($"Unable to install the {name}.");
        }

        _thread = thread;
    }

    public void Stop()
    {
        if (_thread is not { } thread)
        {
            return;
        }

        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativeMethods.POINT Pt;
        public uint Private;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG message, IntPtr hwnd, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(out MSG message, IntPtr hwnd, uint filterMin, uint filterMax, uint removeMessage);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG message);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);
}
