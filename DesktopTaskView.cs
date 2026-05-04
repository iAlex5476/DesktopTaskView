using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DesktopTaskView
{
    internal static class Program
    {
        private const int DesktopClickDelayMilliseconds = 360;
        private const int DoubleClickPixelTolerance = 30;
        private static readonly bool EnableDebugLog = false;
        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DesktopTaskView.log");

        [STAThread]
        private static void Main()
        {
            NativeMethods.MakeProcessDpiAware();

            bool createdNew;
            using (var mutex = new System.Threading.Mutex(true, "DesktopTaskView.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                Log("App starting");
                Application.Run(new TrayAppContext());
            }
        }

        private static void Log(string message)
        {
            if (!EnableDebugLog)
            {
                return;
            }

            try
            {
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff ") + message + Environment.NewLine);
            }
            catch
            {
            }
        }

        private sealed class TrayAppContext : ApplicationContext
        {
            private readonly NotifyIcon trayIcon;
            private readonly MouseHook mouseHook;
            private readonly HotkeyWindow hotkeyWindow;
            private readonly System.Windows.Forms.Timer singleClickTimer;
            private readonly int doubleClickMilliseconds;
            private readonly System.Collections.Generic.List<IntPtr> minimizedWindows = new System.Collections.Generic.List<IntPtr>();
            private bool pendingDesktopClick;
            private Point pendingClickPoint;
            private DateTime pendingClickTime;
            private bool desktopHiddenByApp;
            private bool enabled = true;

            public TrayAppContext()
            {
                doubleClickMilliseconds = DesktopClickDelayMilliseconds;

                singleClickTimer = new System.Windows.Forms.Timer();
                singleClickTimer.Interval = doubleClickMilliseconds;
                singleClickTimer.Tick += HandleSingleClickTimer;

                var menu = new ContextMenuStrip();
                var enabledItem = new ToolStripMenuItem("Enabled") { Checked = true, CheckOnClick = true };
                enabledItem.CheckedChanged += delegate
                {
                    enabled = enabledItem.Checked;
                    Log("Enabled changed: " + enabled);
                };
                menu.Items.Add(enabledItem);

                menu.Items.Add("Open Task View", null, delegate { OpenTaskView(); });
                menu.Items.Add("Show / Restore Desktop", null, delegate { ToggleDesktop(); });
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("Exit", null, delegate { ExitThread(); });

                trayIcon = new NotifyIcon();
                trayIcon.Icon = SystemIcons.Application;
                trayIcon.Text = "Desktop Task View";
                trayIcon.ContextMenuStrip = menu;
                trayIcon.Visible = true;

                mouseHook = new MouseHook(SynchronizationContext.Current);
                mouseHook.LeftButtonDown += HandleLeftButtonDown;
                mouseHook.Start();
                Log("Mouse hook started");

                hotkeyWindow = new HotkeyWindow();
                hotkeyWindow.ToggleDesktopRequested += delegate { ToggleDesktop(); };
                hotkeyWindow.TaskViewRequested += delegate { OpenTaskView(); };
                Log("Hotkeys registered");
            }

            private void HandleLeftButtonDown(Point point)
            {
                bool isDesktop = NativeMethods.IsDesktopPoint(point);
                Log("Mouse down x=" + point.X + " y=" + point.Y + " enabled=" + enabled + " desktop=" + isDesktop + " pending=" + pendingDesktopClick + " classes=" + NativeMethods.DescribePoint(point));

                if (!enabled || !isDesktop)
                {
                    pendingDesktopClick = false;
                    singleClickTimer.Stop();
                    Log("Mouse ignored");
                    return;
                }

                DateTime now = DateTime.UtcNow;
                bool isSecondClick = pendingDesktopClick
                    && (now - pendingClickTime).TotalMilliseconds <= doubleClickMilliseconds
                    && Math.Abs(point.X - pendingClickPoint.X) <= DoubleClickPixelTolerance
                    && Math.Abs(point.Y - pendingClickPoint.Y) <= DoubleClickPixelTolerance;

                if (isSecondClick)
                {
                    pendingDesktopClick = false;
                    singleClickTimer.Stop();
                    Log("Double click detected -> task view");
                    OpenTaskView();
                    return;
                }

                pendingDesktopClick = true;
                pendingClickPoint = point;
                pendingClickTime = now;
                singleClickTimer.Stop();
                singleClickTimer.Start();
                Log("Single click pending; timer started for " + doubleClickMilliseconds + "ms");
            }

            private void HandleSingleClickTimer(object sender, EventArgs e)
            {
                singleClickTimer.Stop();
                Log("Single click timer fired; pending=" + pendingDesktopClick);

                if (!pendingDesktopClick)
                {
                    return;
                }

                pendingDesktopClick = false;
                Log("Single click confirmed -> toggle windows");
                ToggleWindows();
            }

            protected override void ExitThreadCore()
            {
                mouseHook.Dispose();
                hotkeyWindow.Dispose();
                trayIcon.Visible = false;
                trayIcon.Dispose();
                Log("App exiting");
                base.ExitThreadCore();
            }

            private static void ToggleDesktop()
            {
                Log("Action: toggle desktop");
                NativeMethods.ToggleDesktopWithShell();
            }

            private void ToggleWindows()
            {
                if (desktopHiddenByApp)
                {
                    Log("Action: restore app-minimized windows count=" + minimizedWindows.Count);
                    NativeMethods.RestoreWindows(minimizedWindows);
                    minimizedWindows.Clear();
                    desktopHiddenByApp = false;
                    return;
                }

                minimizedWindows.Clear();
                NativeMethods.MinimizeRegularWindows(minimizedWindows);
                desktopHiddenByApp = true;
                Log("Action: minimize regular windows count=" + minimizedWindows.Count);
            }

            private static void OpenTaskView()
            {
                Log("Action: open task view");
                NativeMethods.OpenTaskViewWithShell();
            }

            private static void OpenDebugLog()
            {
                try
                {
                    if (!File.Exists(LogPath))
                    {
                        File.WriteAllText(LogPath, "");
                    }

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = LogPath,
                        UseShellExecute = true
                    });
                }
                catch
                {
                }
            }
        }

        private sealed class HotkeyWindow : NativeWindow, IDisposable
        {
            private const int WM_HOTKEY = 0x0312;
            private const int MOD_CONTROL = 0x0002;
            private const int MOD_ALT = 0x0001;
            private const int HOTKEY_TOGGLE_DESKTOP = 1;
            private const int HOTKEY_TASK_VIEW = 2;

            public event Action ToggleDesktopRequested;
            public event Action TaskViewRequested;

            public HotkeyWindow()
            {
                CreateHandle(new CreateParams());
                NativeMethods.RegisterHotKey(Handle, HOTKEY_TOGGLE_DESKTOP, MOD_CONTROL | MOD_ALT, NativeMethods.VK_F11);
                NativeMethods.RegisterHotKey(Handle, HOTKEY_TASK_VIEW, MOD_CONTROL | MOD_ALT, NativeMethods.VK_F12);
            }

            public void Dispose()
            {
                NativeMethods.UnregisterHotKey(Handle, HOTKEY_TOGGLE_DESKTOP);
                NativeMethods.UnregisterHotKey(Handle, HOTKEY_TASK_VIEW);
                DestroyHandle();
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY)
                {
                    int id = m.WParam.ToInt32();
                    if (id == HOTKEY_TOGGLE_DESKTOP)
                    {
                        var handler = ToggleDesktopRequested;
                        if (handler != null)
                        {
                            handler();
                        }
                        return;
                    }

                    if (id == HOTKEY_TASK_VIEW)
                    {
                        var handler = TaskViewRequested;
                        if (handler != null)
                        {
                            handler();
                        }
                        return;
                    }
                }

                base.WndProc(ref m);
            }
        }

        private sealed class MouseHook : IDisposable
        {
            private const int WH_MOUSE_LL = 14;
            private const int WM_LBUTTONDOWN = 0x0201;

            private readonly NativeMethods.LowLevelMouseProc proc;
            private readonly SynchronizationContext syncContext;
            private IntPtr hookId = IntPtr.Zero;

            public event Action<Point> LeftButtonDown;

            public MouseHook(SynchronizationContext syncContext)
            {
                this.syncContext = syncContext;
                proc = HookCallback;
            }

            public void Start()
            {
                using (var currentProcess = Process.GetCurrentProcess())
                using (var currentModule = currentProcess.MainModule)
                {
                    IntPtr moduleHandle = NativeMethods.GetModuleHandle(currentModule.ModuleName);
                    hookId = NativeMethods.SetWindowsHookEx(WH_MOUSE_LL, proc, moduleHandle, 0);
                    Log("SetWindowsHookEx result=" + hookId);
                }
            }

            public void Dispose()
            {
                if (hookId != IntPtr.Zero)
                {
                    NativeMethods.UnhookWindowsHookEx(hookId);
                    hookId = IntPtr.Zero;
                }
            }

            private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
            {
                if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONDOWN)
                {
                    var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                    var handler = LeftButtonDown;
                    if (handler != null)
                    {
                        var point = new Point(info.pt.x, info.pt.y);
                        if (syncContext != null)
                        {
                            syncContext.Post(delegate { handler(point); }, null);
                        }
                        else
                        {
                            handler(point);
                        }
                    }
                }

                return NativeMethods.CallNextHookEx(hookId, nCode, wParam, lParam);
            }
        }

        private static class NativeMethods
        {
            private const int KEYEVENTF_KEYUP = 0x0002;
            public const byte VK_LWIN = 0x5B;
            public const byte VK_D = 0x44;
            public const byte VK_T = 0x54;
            public const byte VK_TAB = 0x09;
            public const byte VK_F11 = 0x7A;
            public const byte VK_F12 = 0x7B;
            private const int GA_ROOT = 2;

            public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

            [StructLayout(LayoutKind.Sequential)]
            public struct POINT
            {
                public int x;
                public int y;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct MSLLHOOKSTRUCT
            {
                public POINT pt;
                public uint mouseData;
                public uint flags;
                public uint time;
                public IntPtr dwExtraInfo;
            }

            private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

            [DllImport("user32.dll")]
            public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

            [DllImport("user32.dll")]
            public static extern bool UnhookWindowsHookEx(IntPtr hhk);

            [DllImport("user32.dll")]
            public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

            [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
            public static extern IntPtr GetModuleHandle(string lpModuleName);

            [DllImport("user32.dll")]
            private static extern IntPtr WindowFromPoint(POINT point);

            [DllImport("user32.dll")]
            private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

            [DllImport("user32.dll")]
            private static extern IntPtr GetParent(IntPtr hwnd);

            [DllImport("user32.dll")]
            private static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            private static extern int GetClassName(IntPtr hwnd, StringBuilder className, int maxCount);

            [DllImport("user32.dll", CharSet = CharSet.Auto)]
            private static extern int GetWindowTextLength(IntPtr hwnd);

            [DllImport("user32.dll")]
            private static extern bool IsWindowVisible(IntPtr hwnd);

            [DllImport("user32.dll")]
            private static extern bool IsIconic(IntPtr hwnd);

            [DllImport("user32.dll")]
            private static extern bool ShowWindowAsync(IntPtr hwnd, int nCmdShow);

            [DllImport("user32.dll")]
            private static extern IntPtr GetShellWindow();

            [DllImport("user32.dll")]
            private static extern void keybd_event(byte virtualKey, byte scanCode, int flags, UIntPtr extraInfo);

            private const int SW_MINIMIZE = 6;
            private const int SW_RESTORE = 9;

            [DllImport("user32.dll")]
            public static extern int GetDoubleClickTime();

            [DllImport("user32.dll")]
            public static extern bool RegisterHotKey(IntPtr hwnd, int id, int fsModifiers, int vk);

            [DllImport("user32.dll")]
            public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

            [DllImport("user32.dll")]
            private static extern bool SetProcessDPIAware();

            public static void MakeProcessDpiAware()
            {
                try
                {
                    SetProcessDPIAware();
                }
                catch
                {
                }
            }

            public static bool IsDesktopPoint(Point point)
            {
                IntPtr hwnd = WindowFromPoint(new POINT { x = point.X, y = point.Y });
                if (hwnd == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr current = hwnd;
                bool sawDesktopView = false;

                while (current != IntPtr.Zero)
                {
                    string className = GetWindowClassName(current);

                    if (className == "SysListView32" || className == "SHELLDLL_DefView")
                    {
                        sawDesktopView = true;
                    }

                    if ((className == "Progman" || className == "WorkerW") && sawDesktopView)
                    {
                        return true;
                    }

                    IntPtr parentWindow = GetParent(current);
                    if (parentWindow == IntPtr.Zero)
                    {
                        break;
                    }

                    current = parentWindow;
                }

                return false;
            }

            public static void ToggleDesktopWithShell()
            {
                Type shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                {
                    SendKeyCombo(VK_LWIN, VK_D);
                    return;
                }

                object shell = Activator.CreateInstance(shellType);
                try
                {
                    shellType.InvokeMember("ToggleDesktop", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
                }
                finally
                {
                    if (shell != null && Marshal.IsComObject(shell))
                    {
                        Marshal.ReleaseComObject(shell);
                    }
                }
            }

            public static void OpenTaskViewWithShell()
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = "shell:::{3080F90E-D7AD-11D9-BD98-0000947B0257}",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                }
                catch
                {
                    SendKeyCombo(VK_LWIN, VK_TAB);
                }
            }

            private static void SendKeyCombo(byte modifierKey, byte key)
            {
                keybd_event(modifierKey, 0, 0, UIntPtr.Zero);
                keybd_event(key, 0, 0, UIntPtr.Zero);
                keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(modifierKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }

            public static void MinimizeRegularWindows(System.Collections.Generic.List<IntPtr> minimizedWindows)
            {
                IntPtr shellWindow = GetShellWindow();
                int currentProcessId = Process.GetCurrentProcess().Id;

                EnumWindows(delegate (IntPtr hwnd, IntPtr lParam)
                {
                    if (!IsRegularTopLevelWindow(hwnd, shellWindow, currentProcessId))
                    {
                        return true;
                    }

                    minimizedWindows.Add(hwnd);
                    ShowWindowAsync(hwnd, SW_MINIMIZE);
                    return true;
                }, IntPtr.Zero);
            }

            public static void RestoreWindows(System.Collections.Generic.List<IntPtr> windows)
            {
                foreach (IntPtr hwnd in windows)
                {
                    if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd))
                    {
                        ShowWindowAsync(hwnd, SW_RESTORE);
                    }
                }
            }

            private static bool IsRegularTopLevelWindow(IntPtr hwnd, IntPtr shellWindow, int currentProcessId)
            {
                if (hwnd == IntPtr.Zero || hwnd == shellWindow)
                {
                    return false;
                }

                if (!IsWindowVisible(hwnd) || IsIconic(hwnd))
                {
                    return false;
                }

                if (GetWindowTextLength(hwnd) == 0)
                {
                    return false;
                }

                uint processId;
                GetWindowThreadProcessId(hwnd, out processId);
                if (processId == currentProcessId)
                {
                    return false;
                }

                string className = GetWindowClassName(hwnd);
                if (className == "Progman" ||
                    className == "WorkerW" ||
                    className == "Shell_TrayWnd" ||
                    className == "Shell_SecondaryTrayWnd" ||
                    className == "XamlExplorerHostIslandWindow" ||
                    className.StartsWith("WindowsForms10."))
                {
                    return false;
                }

                return true;
            }

            [DllImport("user32.dll")]
            private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

            private static string GetWindowClassName(IntPtr hwnd)
            {
                var builder = new StringBuilder(256);
                GetClassName(hwnd, builder, builder.Capacity);
                return builder.ToString();
            }

            public static string DescribePoint(Point point)
            {
                var result = new StringBuilder();
                IntPtr hwnd = WindowFromPoint(new POINT { x = point.X, y = point.Y });
                if (hwnd == IntPtr.Zero)
                {
                    Rectangle bounds = SystemInformation.VirtualScreen;
                    return "WindowFromPoint=NULL virtualScreen=" + bounds.Left + "," + bounds.Top + "," + bounds.Width + "x" + bounds.Height;
                }

                int depth = 0;

                while (hwnd != IntPtr.Zero && depth < 8)
                {
                    if (result.Length > 0)
                    {
                        result.Append(" > ");
                    }

                    result.Append(GetWindowClassName(hwnd));

                    IntPtr parent = GetParent(hwnd);
                    if (parent == IntPtr.Zero)
                    {
                        break;
                    }

                    hwnd = parent;
                    depth++;
                }

                return result.ToString();
            }
        }
    }
}
