using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace RGC.ClientAgent.Services;

/// <summary>
/// While an announcement is open, the rest of the PC can't be used: a dark cover over every
/// monitor (taskbar included) sits under the popup, and the keys that switch away from it
/// (Windows key, Alt+Tab, Alt+Esc, Ctrl+Esc, Ctrl+Shift+Esc) are swallowed.
/// Ctrl+Alt+Del is handled by Windows itself and can't be blocked; locking the PC or
/// shutting down still works, and the popup comes back afterwards.
/// </summary>
public sealed class ScreenLock : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100, WM_SYSKEYDOWN = 0x0104;
    private const int VK_TAB = 0x09, VK_ESCAPE = 0x1B, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_APPS = 0x5D;
    private const int VK_CONTROL = 0x11, VK_MENU = 0x12;
    private const uint LLKHF_ALTDOWN = 0x20;
    private const int SM_XVIRTUALSCREEN = 76, SM_YVIRTUALSCREEN = 77, SM_CXVIRTUALSCREEN = 78, SM_CYVIRTUALSCREEN = 79;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;

    private readonly Window _cover;
    private readonly DispatcherTimer _topmostTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly LowLevelKeyboardProc _hookProc;
    private IntPtr _hook;

    /// <summary>The cover window; the popup is owned by it so it always stays above.</summary>
    public Window Cover => _cover;

    public ScreenLock()
    {
        _hookProc = HookCallback;
        _cover = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = new SolidColorBrush(Color.FromArgb(0xE6, 0x0B, 0x16, 0x2C)),
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            Title = "RGC",
            Left = SystemParameters.VirtualScreenLeft,
            Top = SystemParameters.VirtualScreenTop,
            Width = SystemParameters.VirtualScreenWidth,
            Height = SystemParameters.VirtualScreenHeight
        };
        _cover.SourceInitialized += (_, _) => CoverAllScreens();
        _topmostTimer.Tick += (_, _) => CoverAllScreens();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
    }

    public void Show()
    {
        _cover.Show();
        CoverAllScreens();
        _topmostTimer.Start();
        try
        {
            _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _hookProc, GetModuleHandle(null), 0);
            if (_hook == IntPtr.Zero) AgentLog.Error($"Keyboard lock unavailable (error {Marshal.GetLastWin32Error()})");
        }
        catch (Exception ex)
        {
            AgentLog.Error("Keyboard lock unavailable", ex);
        }
    }

    public void Dispose()
    {
        _topmostTimer.Stop();
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        _cover.Close();
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => _cover.Dispatcher.InvokeAsync(CoverAllScreens);

    /// <summary>Stretch over all monitors in physical pixels (exact with mixed scaling) and stay on top.</summary>
    private void CoverAllScreens()
    {
        var hwnd = new WindowInteropHelper(_cover).Handle;
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, HWND_TOPMOST,
            GetSystemMetrics(SM_XVIRTUALSCREEN), GetSystemMetrics(SM_YVIRTUALSCREEN),
            GetSystemMetrics(SM_CXVIRTUALSCREEN), GetSystemMetrics(SM_CYVIRTUALSCREEN),
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var info = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
            if (ShouldBlock(info, (int)wParam)) return 1;
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private static bool ShouldBlock(KBDLLHOOKSTRUCT key, int message)
    {
        var vk = (int)key.vkCode;
        // Windows key (Start menu, Win+D, Win+Tab, ...) and the context-menu key: up and down.
        if (vk is VK_LWIN or VK_RWIN or VK_APPS) return true;
        if (message is not (WM_KEYDOWN or WM_SYSKEYDOWN)) return false;

        var alt = (key.flags & LLKHF_ALTDOWN) != 0 || IsDown(VK_MENU);
        var ctrl = IsDown(VK_CONTROL);
        if (alt && vk is (VK_TAB or VK_ESCAPE)) return true;   // Alt+Tab, Alt+Esc
        if (ctrl && vk == VK_ESCAPE) return true;            // Ctrl+Esc (Start), Ctrl+Shift+Esc (Task Manager)
        return false;
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT
    {
        public uint vkCode, scanCode, flags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
}
