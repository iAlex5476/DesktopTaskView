// DesktopTaskView v0.3.0
// Single .cs file, builds with the .NET Framework 4.x csc.exe shipped with Windows.
// MIT License. See LICENSE.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("DesktopTaskView")]
[assembly: AssemblyDescription("Click empty desktop to minimize windows. Double-click for Task View.")]
[assembly: AssemblyCompany("DesktopTaskView")]
[assembly: AssemblyProduct("DesktopTaskView")]
[assembly: AssemblyCopyright("MIT License")]
[assembly: AssemblyVersion("0.3.0.0")]
[assembly: AssemblyFileVersion("0.3.0.0")]

namespace DesktopTaskView
{
    internal static class Program
    {
        public const string AppName = "DesktopTaskView";
        public const string Version = "0.3.0";

        [STAThread]
        private static void Main()
        {
            bool firstInstance;
            using (var mutex = new Mutex(true, "Global\\DesktopTaskView_SingleInstance_v2", out firstInstance))
            {
                if (!firstInstance)
                {
                    MessageBox.Show(
                        "DesktopTaskView is already running. Look for the icon in the system tray.",
                        AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Native.SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
                GC.KeepAlive(mutex);
            }
        }
    }

    // ---------------------------------------------------------------------
    // Settings: simple INI-style persistence under %APPDATA%\DesktopTaskView
    // ---------------------------------------------------------------------
    internal sealed class AppSettings
    {
        public bool Enabled { get; set; }
        public int DoubleClickMs { get; set; }
        public int SingleClickDelayMs { get; set; }
        public bool MinimizeOnSingleClick { get; set; }
        public bool TaskViewOnDoubleClick { get; set; }
        public string ExcludedProcesses { get; set; } // comma-separated, lowercase, no .exe
        public string HotkeyToggleDesktop { get; set; }
        public string HotkeyTaskView { get; set; }

        public static string FilePath
        {
            get
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    Program.AppName);
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "settings.ini");
            }
        }

        public static AppSettings Defaults()
        {
            return new AppSettings
            {
                Enabled = true,
                DoubleClickMs = SystemInformation.DoubleClickTime, // ~500ms
                SingleClickDelayMs = SystemInformation.DoubleClickTime + 30,
                MinimizeOnSingleClick = true,
                TaskViewOnDoubleClick = true,
                ExcludedProcesses = "",
                HotkeyToggleDesktop = "Ctrl+Alt+F11",
                HotkeyTaskView = "Ctrl+Alt+F12",
            };
        }

        public static AppSettings Load()
        {
            var s = Defaults();
            try
            {
                if (!File.Exists(FilePath)) return s;
                foreach (var raw in File.ReadAllLines(FilePath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "Enabled": s.Enabled = ParseBool(val, s.Enabled); break;
                        case "DoubleClickMs": s.DoubleClickMs = ParseInt(val, s.DoubleClickMs); break;
                        case "SingleClickDelayMs": s.SingleClickDelayMs = ParseInt(val, s.SingleClickDelayMs); break;
                        case "MinimizeOnSingleClick": s.MinimizeOnSingleClick = ParseBool(val, s.MinimizeOnSingleClick); break;
                        case "TaskViewOnDoubleClick": s.TaskViewOnDoubleClick = ParseBool(val, s.TaskViewOnDoubleClick); break;
                        case "ExcludedProcesses": s.ExcludedProcesses = val; break;
                        case "HotkeyToggleDesktop": s.HotkeyToggleDesktop = val; break;
                        case "HotkeyTaskView": s.HotkeyTaskView = val; break;
                    }
                }
            }
            catch { /* ignore corrupt file, fall back to defaults */ }
            return s;
        }

        public void Save()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# DesktopTaskView settings. Edit with care, or use the Settings UI.");
            sb.AppendLine("Enabled=" + Enabled);
            sb.AppendLine("DoubleClickMs=" + DoubleClickMs);
            sb.AppendLine("SingleClickDelayMs=" + SingleClickDelayMs);
            sb.AppendLine("MinimizeOnSingleClick=" + MinimizeOnSingleClick);
            sb.AppendLine("TaskViewOnDoubleClick=" + TaskViewOnDoubleClick);
            sb.AppendLine("ExcludedProcesses=" + (ExcludedProcesses ?? ""));
            sb.AppendLine("HotkeyToggleDesktop=" + (HotkeyToggleDesktop ?? ""));
            sb.AppendLine("HotkeyTaskView=" + (HotkeyTaskView ?? ""));
            File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
        }

        public HashSet<string> ExcludedSet()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(ExcludedProcesses)) return set;
            foreach (var part in ExcludedProcesses.Split(new[] { ',', ';', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string p = part.Trim();
                if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    p = p.Substring(0, p.Length - 4);
                if (p.Length > 0) set.Add(p);
            }
            return set;
        }

        private static bool ParseBool(string v, bool dflt)
        {
            bool b;
            return bool.TryParse(v, out b) ? b : dflt;
        }
        private static int ParseInt(string v, int dflt)
        {
            int n;
            return int.TryParse(v, out n) ? n : dflt;
        }
    }

    // ---------------------------------------------------------------------
    // Auto-start (HKCU\...\Run)
    // ---------------------------------------------------------------------
    internal static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool IsEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
            {
                if (key == null) return false;
                var v = key.GetValue(Program.AppName) as string;
                return !string.IsNullOrEmpty(v);
            }
        }

        public static void SetEnabled(bool on)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key == null) return;
                if (on)
                {
                    string exe = Application.ExecutablePath;
                    key.SetValue(Program.AppName, "\"" + exe + "\"");
                }
                else
                {
                    if (key.GetValue(Program.AppName) != null)
                        key.DeleteValue(Program.AppName, false);
                }
            }
        }
    }

    // ---------------------------------------------------------------------
    // Tray context: owns icon, settings, mouse hook, hotkeys, message window
    // ---------------------------------------------------------------------
    internal sealed class TrayContext : ApplicationContext
    {
        private readonly NotifyIcon _tray;
        private readonly DesktopClickWatcher _watcher;
        private readonly HotkeyHost _hotkeys;
        private readonly WindowMinimizer _minimizer = new WindowMinimizer();
        private AppSettings _settings;
        private SettingsForm _settingsForm;

        private ToolStripMenuItem _miEnabled;
        private ToolStripMenuItem _miAutoStart;

        public TrayContext()
        {
            _settings = AppSettings.Load();
            _minimizer.SetExcluded(_settings.ExcludedSet());

            _tray = new NotifyIcon
            {
                Icon = IconFactory.Build(),
                Text = Program.AppName + " v" + Program.Version,
                Visible = true,
            };
            _tray.ContextMenuStrip = BuildMenu();
            _tray.MouseClick += OnTrayClick;

            _watcher = new DesktopClickWatcher(this);
            _watcher.Apply(_settings);
            if (_settings.Enabled) _watcher.Start();

            _hotkeys = new HotkeyHost(this);
            _hotkeys.Apply(_settings);
        }

        public AppSettings Settings { get { return _settings; } }
        public WindowMinimizer Minimizer { get { return _minimizer; } }

        public void ApplySettings(AppSettings s)
        {
            _settings = s;
            _settings.Save();
            _minimizer.SetExcluded(_settings.ExcludedSet());
            _watcher.Apply(_settings);
            if (_settings.Enabled) _watcher.Start(); else _watcher.Stop();
            _hotkeys.Apply(_settings);
            RefreshMenu();
        }

        private ContextMenuStrip BuildMenu()
        {
            var m = new ContextMenuStrip();

            _miEnabled = new ToolStripMenuItem("Enabled");
            _miEnabled.Click += (s, e) =>
            {
                _settings.Enabled = !_settings.Enabled;
                ApplySettings(_settings);
            };
            m.Items.Add(_miEnabled);

            m.Items.Add(new ToolStripSeparator());

            var miTaskView = new ToolStripMenuItem("Open Task View");
            miTaskView.Click += (s, e) => DesktopActions.OpenTaskView();
            m.Items.Add(miTaskView);

            var miShowDesktop = new ToolStripMenuItem("Show / Restore Desktop");
            miShowDesktop.Click += (s, e) => _minimizer.Toggle();
            m.Items.Add(miShowDesktop);

            m.Items.Add(new ToolStripSeparator());

            var miSettings = new ToolStripMenuItem("Settings...");
            miSettings.Click += (s, e) => OpenSettings();
            m.Items.Add(miSettings);

            _miAutoStart = new ToolStripMenuItem("Start with Windows");
            _miAutoStart.Click += (s, e) =>
            {
                Startup.SetEnabled(!Startup.IsEnabled());
                RefreshMenu();
            };
            m.Items.Add(_miAutoStart);

            var miAbout = new ToolStripMenuItem("About");
            miAbout.Click += (s, e) => MessageBox.Show(
                Program.AppName + " v" + Program.Version + "\n\n" +
                "Click empty desktop = minimize / restore windows.\n" +
                "Double-click empty desktop = Task View.\n\n" +
                "MIT License.",
                "About " + Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            m.Items.Add(miAbout);

            m.Items.Add(new ToolStripSeparator());

            var miExit = new ToolStripMenuItem("Exit");
            miExit.Click += (s, e) => ExitApp();
            m.Items.Add(miExit);

            m.Opening += (s, e) => RefreshMenu();
            return m;
        }

        private void RefreshMenu()
        {
            if (_miEnabled != null) _miEnabled.Checked = _settings.Enabled;
            if (_miAutoStart != null) _miAutoStart.Checked = Startup.IsEnabled();
        }

        private void OnTrayClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) OpenSettings();
        }

        private void OpenSettings()
        {
            if (_settingsForm != null && !_settingsForm.IsDisposed)
            {
                _settingsForm.WindowState = FormWindowState.Normal;
                _settingsForm.Activate();
                return;
            }
            _settingsForm = new SettingsForm(this, _settings);
            _settingsForm.FormClosed += (s, e) => _settingsForm = null;
            _settingsForm.Show();
        }

        private void ExitApp()
        {
            try { _watcher.Stop(); } catch { }
            try { _hotkeys.Dispose(); } catch { }
            try { _tray.Visible = false; _tray.Dispose(); } catch { }
            Application.Exit();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { _tray.Visible = false; _tray.Dispose(); } catch { }
                try { _watcher.Stop(); } catch { }
                try { _hotkeys.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }

    // ---------------------------------------------------------------------
    // Custom tray icon: small monitor with a curved arrow on it
    // ---------------------------------------------------------------------
    internal static class IconFactory
    {
        public static Icon Build()
        {
            using (var bmp = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                // Monitor body
                using (var bg = new SolidBrush(Color.FromArgb(40, 120, 215)))
                    g.FillRectangle(bg, 3, 5, 26, 18);
                using (var pen = new Pen(Color.White, 2))
                    g.DrawRectangle(pen, 3, 5, 26, 18);

                // Monitor stand
                using (var pen = new Pen(Color.White, 2))
                {
                    g.DrawLine(pen, 16, 23, 16, 27);
                    g.DrawLine(pen, 10, 28, 22, 28);
                }

                // Down arrow on screen (minimize hint)
                using (var pen = new Pen(Color.White, 2))
                {
                    g.DrawLine(pen, 16, 8, 16, 18);
                    g.DrawLine(pen, 12, 14, 16, 18);
                    g.DrawLine(pen, 20, 14, 16, 18);
                }

                IntPtr h = bmp.GetHicon();
                Icon icon = (Icon)Icon.FromHandle(h).Clone();
                Native.DestroyIcon(h);
                return icon;
            }
        }
    }

    // ---------------------------------------------------------------------
    // Window enumeration + minimize/restore (the v0.1.x core, refactored)
    // ---------------------------------------------------------------------
    internal sealed class WindowMinimizer
    {
        private readonly List<KeyValuePair<IntPtr, Native.WINDOWPLACEMENT>> _minimized
            = new List<KeyValuePair<IntPtr, Native.WINDOWPLACEMENT>>();
        private HashSet<string> _excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public void SetExcluded(HashSet<string> excluded)
        {
            _excluded = excluded ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public void Toggle()
        {
            if (_minimized.Count > 0) Restore(); else Minimize();
        }

        public void Minimize()
        {
            _minimized.Clear();
            uint myPid = (uint)Process.GetCurrentProcess().Id;

            Native.EnumWindows((hwnd, lp) =>
            {
                if (!Native.IsWindowVisible(hwnd)) return true;
                if (Native.IsIconic(hwnd)) return true;

                int len = Native.GetWindowTextLength(hwnd);
                if (len <= 0) return true;

                var sb = new StringBuilder(len + 2);
                Native.GetWindowText(hwnd, sb, sb.Capacity);
                if (sb.Length == 0) return true;

                var cls = new StringBuilder(256);
                Native.GetClassName(hwnd, cls, cls.Capacity);
                string className = cls.ToString();

                if (className == "Progman" || className == "WorkerW" ||
                    className == "Shell_TrayWnd" || className == "Shell_SecondaryTrayWnd" ||
                    className == "TaskListThumbnailWnd")
                    return true;
                if (className == "ApplicationFrameWindow" && IsCloaked(hwnd))
                    return true;

                // Skip our own windows
                uint pid;
                Native.GetWindowThreadProcessId(hwnd, out pid);
                if (pid == myPid) return true;

                // Skip cloaked / DWM-hidden
                if (IsCloaked(hwnd)) return true;

                // Skip excluded process names
                if (_excluded.Count > 0)
                {
                    try
                    {
                        var proc = Process.GetProcessById((int)pid);
                        string name = proc.ProcessName;
                        if (_excluded.Contains(name)) return true;
                    }
                    catch { /* process gone, ignore */ }
                }

                // Skip tool windows without an owner (utility popups)
                int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
                if ((ex & Native.WS_EX_TOOLWINDOW) != 0) return true;

                var wp = new Native.WINDOWPLACEMENT();
                wp.length = Marshal.SizeOf(typeof(Native.WINDOWPLACEMENT));
                if (Native.GetWindowPlacement(hwnd, ref wp) &&
                    Native.ShowWindowAsync(hwnd, Native.SW_MINIMIZE))
                {
                    _minimized.Add(new KeyValuePair<IntPtr, Native.WINDOWPLACEMENT>(hwnd, wp));
                }

                return true;
            }, IntPtr.Zero);
        }

        public void Restore()
        {
            for (int i = _minimized.Count - 1; i >= 0; i--)
            {
                IntPtr h = _minimized[i].Key;
                Native.WINDOWPLACEMENT wp = _minimized[i].Value;
                if (!Native.IsWindow(h)) continue;
                if (wp.showCmd == Native.SW_SHOWMINIMIZED)
                    wp.showCmd = Native.SW_SHOWNORMAL;
                Native.SetWindowPlacement(h, ref wp);
            }
            _minimized.Clear();
        }

        public bool HasMinimized { get { return _minimized.Count > 0; } }

        private static bool IsCloaked(IntPtr hwnd)
        {
            int cloaked = 0;
            int hr = Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_CLOAKED, out cloaked, sizeof(int));
            return hr == 0 && cloaked != 0;
        }

    }

    // ---------------------------------------------------------------------
    // Low-level mouse hook + strict desktop hit-testing.
    // Owns its own UI-thread timer to disambiguate single vs double click.
    // ---------------------------------------------------------------------
    internal sealed class DesktopClickWatcher
    {
        private readonly TrayContext _ctx;
        private IntPtr _hookHandle = IntPtr.Zero;
        private Native.LowLevelMouseProc _proc; // keep delegate alive
        private readonly System.Windows.Forms.Timer _singleClickTimer;
        private DateTime _lastDownAt = DateTime.MinValue;
        private Point _lastDownPt;
        private bool _pendingSingle;
        private int _doubleClickMs = 500;
        private int _singleClickDelayMs = 530;

        public DesktopClickWatcher(TrayContext ctx)
        {
            _ctx = ctx;
            _singleClickTimer = new System.Windows.Forms.Timer();
            _singleClickTimer.Tick += OnSingleClickTimer;
        }

        public void Apply(AppSettings s)
        {
            _doubleClickMs = Math.Max(120, s.DoubleClickMs);
            _singleClickDelayMs = Math.Max(_doubleClickMs + 10, s.SingleClickDelayMs);
        }

        public void Start()
        {
            if (_hookHandle != IntPtr.Zero) return;
            _proc = HookCallback;
            using (var curProc = Process.GetCurrentProcess())
            using (var curMod = curProc.MainModule)
            {
                _hookHandle = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _proc,
                    Native.GetModuleHandle(curMod.ModuleName), 0);
            }
        }

        public void Stop()
        {
            if (_hookHandle != IntPtr.Zero)
            {
                Native.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
            _singleClickTimer.Stop();
            _pendingSingle = false;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                if (msg == Native.WM_LBUTTONDOWN)
                {
                    var data = (Native.MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.MSLLHOOKSTRUCT));
                    HandleLeftDown(new Point(data.pt.x, data.pt.y));
                }
            }
            return Native.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
        }

        private void HandleLeftDown(Point pt)
        {
            // Strict desktop hit test. Loose results were the v0.1.1 bug.
            if (!DesktopHitTest.IsOnEmptyDesktop(pt)) return;

            var now = DateTime.UtcNow;
            double sinceMs = (now - _lastDownAt).TotalMilliseconds;
            _lastDownAt = now;

            if (_pendingSingle && sinceMs <= _doubleClickMs &&
                Math.Abs(pt.X - _lastDownPt.X) < 8 && Math.Abs(pt.Y - _lastDownPt.Y) < 8)
            {
                // Promoted to double-click: cancel pending single, fire double.
                _pendingSingle = false;
                _singleClickTimer.Stop();
                _lastDownAt = DateTime.MinValue;
                if (_ctx.Settings.TaskViewOnDoubleClick)
                    BeginInvokeOnUI(DesktopActions.OpenTaskView);
                return;
            }

            // Schedule single click after the double-click window expires.
            _lastDownPt = pt;
            _pendingSingle = true;
            _singleClickTimer.Interval = _singleClickDelayMs;
            _singleClickTimer.Stop();
            _singleClickTimer.Start();
        }

        private void OnSingleClickTimer(object sender, EventArgs e)
        {
            _singleClickTimer.Stop();
            if (!_pendingSingle) return;
            _pendingSingle = false;
            if (!_ctx.Settings.MinimizeOnSingleClick) return;
            _ctx.Minimizer.Toggle();
        }

        private static void BeginInvokeOnUI(Action a)
        {
            // Hook callback runs on the message-pump thread already; safe to call.
            try { a(); } catch { }
        }
    }

    // ---------------------------------------------------------------------
    // Strict "is this point really the desktop" test:
    //   Walk WindowFromPoint -> ancestor chain. Must reach Progman or WorkerW
    //   AND the immediate hit window must be a desktop view class.
    // ---------------------------------------------------------------------
    internal static class DesktopHitTest
    {
        public static bool IsOnEmptyDesktop(Point pt)
        {
            IntPtr hit = Native.WindowFromPoint(new Native.POINT { x = pt.X, y = pt.Y });
            if (hit == IntPtr.Zero) return false;

            string hitClass = GetClass(hit);

            // Direct hit: Progman or WorkerW (no icons present)
            if (hitClass == "Progman" || hitClass == "WorkerW") return true;

            // Must be SHELLDLL_DefView or SysListView32 to continue
            if (hitClass != "SHELLDLL_DefView" && hitClass != "SysListView32") return false;

            // Walk ancestor chain to confirm this list view belongs to the real desktop host
            bool isDesktopHost = false;
            IntPtr cur = hit;
            for (int i = 0; i < 8 && cur != IntPtr.Zero; i++)
            {
                string c = GetClass(cur);
                if (c == "Progman" || c == "WorkerW")
                {
                    IntPtr defView = Native.FindWindowEx(cur, IntPtr.Zero, "SHELLDLL_DefView", null);
                    if (defView != IntPtr.Zero) isDesktopHost = true;
                    break;
                }
                cur = Native.GetParent(cur);
            }
            if (!isDesktopHost) return false;

            // P0-2: if the hit is on SysListView32, check whether a desktop icon was clicked.
            // LVM_HITTEST lParam is a pointer that must live in explorer.exe's address space;
            // use VirtualAllocEx/WriteProcessMemory/ReadProcessMemory for the cross-process call.
            if (hitClass == "SysListView32")
            {
                var clientPt = new Native.POINT { x = pt.X, y = pt.Y };
                if (Native.ScreenToClient(hit, ref clientPt))
                {
                    if (IsDesktopIconAt(hit, clientPt))
                        return false;
                }
            }

            return true;
        }

        // Returns true if a desktop icon item occupies the given client-coordinate point.
        // Uses cross-process memory because LVM_HITTEST lParam must live in explorer.exe's heap.
        private static bool IsDesktopIconAt(IntPtr listView, Native.POINT clientPt)
        {
            uint pid;
            Native.GetWindowThreadProcessId(listView, out pid);
            IntPtr hProcess = Native.OpenProcess(
                Native.PROCESS_VM_OPERATION | Native.PROCESS_VM_READ | Native.PROCESS_VM_WRITE,
                false, pid);
            if (hProcess == IntPtr.Zero) return false;
            try
            {
                int structSize = Marshal.SizeOf(typeof(Native.LVHITTESTINFO));
                IntPtr remotePtr = Native.VirtualAllocEx(hProcess, IntPtr.Zero,
                    (IntPtr)structSize, Native.MEM_COMMIT | Native.MEM_RESERVE, Native.PAGE_READWRITE);
                if (remotePtr == IntPtr.Zero) return false;
                try
                {
                    var hti = new Native.LVHITTESTINFO();
                    hti.pt = clientPt;
                    hti.flags = 0;
                    hti.iItem = -1;
                    hti.iSubItem = 0;
                    hti.iGroup = 0;

                    IntPtr localBuf = Marshal.AllocHGlobal(structSize);
                    try
                    {
                        Marshal.StructureToPtr(hti, localBuf, false);
                        IntPtr written;
                        Native.WriteProcessMemory(hProcess, remotePtr, localBuf,
                            (IntPtr)structSize, out written);
                    }
                    finally { Marshal.FreeHGlobal(localBuf); }

                    Native.SendMessage(listView, Native.LVM_HITTEST, IntPtr.Zero, remotePtr);

                    IntPtr readBuf = Marshal.AllocHGlobal(structSize);
                    try
                    {
                        IntPtr bytesRead;
                        Native.ReadProcessMemory(hProcess, remotePtr, readBuf,
                            (IntPtr)structSize, out bytesRead);
                        var result = (Native.LVHITTESTINFO)Marshal.PtrToStructure(
                            readBuf, typeof(Native.LVHITTESTINFO));
                        return (result.flags & Native.LVHT_ONITEM) != 0;
                    }
                    finally { Marshal.FreeHGlobal(readBuf); }
                }
                finally { Native.VirtualFreeEx(hProcess, remotePtr, 0, Native.MEM_RELEASE); }
            }
            finally { Native.CloseHandle(hProcess); }
        }

        private static string GetClass(IntPtr h)
        {
            var sb = new StringBuilder(256);
            Native.GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }
    }

    // ---------------------------------------------------------------------
    // Task View / Show Desktop helpers
    // ---------------------------------------------------------------------
    internal static class DesktopActions
    {
        public static void OpenTaskView()
        {
            try
            {
                var psi = new ProcessStartInfo("explorer.exe",
                    "shell:::{3080F90E-D7AD-11D9-BD98-0000947B0257}")
                { UseShellExecute = true };
                Process.Start(psi);
            }
            catch
            {
                // Fallback: synthesize Win+Tab
                Native.keybd_event(Native.VK_LWIN, 0, 0, UIntPtr.Zero);
                Native.keybd_event(Native.VK_TAB, 0, 0, UIntPtr.Zero);
                Native.keybd_event(Native.VK_TAB, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
                Native.keybd_event(Native.VK_LWIN, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
        }
    }

    // ---------------------------------------------------------------------
    // Hotkeys: hidden message window registers WM_HOTKEY
    // ---------------------------------------------------------------------
    internal sealed class HotkeyHost : IDisposable
    {
        private readonly TrayContext _ctx;
        private readonly HotkeyWindow _win;
        private int _idToggleDesktop = -1;
        private int _idTaskView = -1;
        private static int _nextId = 0xB100;

        public HotkeyHost(TrayContext ctx)
        {
            _ctx = ctx;
            _win = new HotkeyWindow(this);
        }

        public void Apply(AppSettings s)
        {
            UnregisterAll();

            uint mod1, vk1;
            if (TryParse(s.HotkeyToggleDesktop, out mod1, out vk1))
            {
                _idToggleDesktop = Interlocked.Increment(ref _nextId);
                Native.RegisterHotKey(_win.Handle, _idToggleDesktop, mod1, vk1);
            }
            uint mod2, vk2;
            if (TryParse(s.HotkeyTaskView, out mod2, out vk2))
            {
                _idTaskView = Interlocked.Increment(ref _nextId);
                Native.RegisterHotKey(_win.Handle, _idTaskView, mod2, vk2);
            }
        }

        public void OnHotkey(int id)
        {
            if (id == _idToggleDesktop) _ctx.Minimizer.Toggle();
            else if (id == _idTaskView) DesktopActions.OpenTaskView();
        }

        private void UnregisterAll()
        {
            if (_idToggleDesktop > 0) { Native.UnregisterHotKey(_win.Handle, _idToggleDesktop); _idToggleDesktop = -1; }
            if (_idTaskView > 0) { Native.UnregisterHotKey(_win.Handle, _idTaskView); _idTaskView = -1; }
        }

        public void Dispose()
        {
            UnregisterAll();
            try { _win.DestroyHandle(); } catch { }
        }

        public static bool TryParse(string spec, out uint mods, out uint vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrWhiteSpace(spec)) return false;
            var parts = spec.Split('+');
            foreach (var raw in parts)
            {
                string p = raw.Trim();
                if (p.Length == 0) continue;
                switch (p.ToLowerInvariant())
                {
                    case "ctrl":
                    case "control": mods |= 0x0002; break;
                    case "alt": mods |= 0x0001; break;
                    case "shift": mods |= 0x0004; break;
                    case "win":
                    case "windows": mods |= 0x0008; break;
                    default:
                        if (!TryParseKey(p, out vk)) return false;
                        break;
                }
            }
            return vk != 0;
        }

        private static bool TryParseKey(string p, out uint vk)
        {
            vk = 0;
            // Fn keys
            if (p.Length >= 2 && (p[0] == 'F' || p[0] == 'f'))
            {
                int n;
                if (int.TryParse(p.Substring(1), out n) && n >= 1 && n <= 24)
                {
                    vk = (uint)(0x70 + (n - 1)); // VK_F1..F24
                    return true;
                }
            }
            // Single chars 0-9 / A-Z
            if (p.Length == 1)
            {
                char c = char.ToUpperInvariant(p[0]);
                if (c >= '0' && c <= '9') { vk = (uint)c; return true; }
                if (c >= 'A' && c <= 'Z') { vk = (uint)c; return true; }
            }
            return false;
        }

        private sealed class HotkeyWindow : NativeWindow
        {
            private const int WM_HOTKEY = 0x0312;
            private readonly HotkeyHost _owner;
            public HotkeyWindow(HotkeyHost owner)
            {
                _owner = owner;
                CreateHandle(new CreateParams());
            }
            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY) _owner.OnHotkey(m.WParam.ToInt32());
                base.WndProc(ref m);
            }
        }
    }

    // ---------------------------------------------------------------------
    // Settings UI
    // ---------------------------------------------------------------------
    internal sealed class SettingsForm : Form
    {
        private readonly TrayContext _ctx;
        private readonly AppSettings _working;

        private CheckBox _cbEnabled;
        private CheckBox _cbMinimizeSingle;
        private CheckBox _cbTaskViewDouble;
        private CheckBox _cbAutoStart;
        private NumericUpDown _numDouble;
        private NumericUpDown _numDelay;
        private TextBox _txtExcluded;
        private TextBox _txtHotkeyToggle;
        private TextBox _txtHotkeyTaskView;

        public SettingsForm(TrayContext ctx, AppSettings current)
        {
            _ctx = ctx;
            _working = new AppSettings
            {
                Enabled = current.Enabled,
                DoubleClickMs = current.DoubleClickMs,
                SingleClickDelayMs = current.SingleClickDelayMs,
                MinimizeOnSingleClick = current.MinimizeOnSingleClick,
                TaskViewOnDoubleClick = current.TaskViewOnDoubleClick,
                ExcludedProcesses = current.ExcludedProcesses,
                HotkeyToggleDesktop = current.HotkeyToggleDesktop,
                HotkeyTaskView = current.HotkeyTaskView,
            };

            Text = Program.AppName + " - Settings";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(440, 460);
            Icon = IconFactory.Build();
            Font = SystemFonts.MessageBoxFont;

            BuildUi();
            LoadFromSettings();
        }

        private void BuildUi()
        {
            int y = 12;
            int labelW = 160;
            int inputX = 180;

            _cbEnabled = AddCheckBox("Enabled", 12, ref y);
            _cbMinimizeSingle = AddCheckBox("Minimize on single-click empty desktop", 12, ref y);
            _cbTaskViewDouble = AddCheckBox("Open Task View on double-click empty desktop", 12, ref y);
            _cbAutoStart = AddCheckBox("Start with Windows", 12, ref y);

            y += 8;

            AddLabel("Double-click window (ms):", 12, y, labelW);
            _numDouble = new NumericUpDown { Left = inputX, Top = y - 3, Width = 100, Minimum = 120, Maximum = 1500 };
            Controls.Add(_numDouble);
            y += 30;

            AddLabel("Single-click delay (ms):", 12, y, labelW);
            _numDelay = new NumericUpDown { Left = inputX, Top = y - 3, Width = 100, Minimum = 130, Maximum = 2000 };
            Controls.Add(_numDelay);
            y += 30;

            AddLabel("Excluded processes:", 12, y, labelW);
            y += 20;
            _txtExcluded = new TextBox { Left = 12, Top = y, Width = 416, Height = 60, Multiline = true,
                ScrollBars = ScrollBars.Vertical };
            Controls.Add(_txtExcluded);
            y += 64;
            var lblExcludedHint = new Label
            {
                Text = "comma- or newline-separated process names (no .exe), e.g. notepad, code",
                Left = 12, Top = y, Width = 416, ForeColor = SystemColors.GrayText, AutoSize = false,
            };
            Controls.Add(lblExcludedHint);
            y += 18;

            AddLabel("Hotkey - Toggle Desktop:", 12, y, labelW);
            _txtHotkeyToggle = new TextBox { Left = inputX, Top = y - 3, Width = 200 };
            Controls.Add(_txtHotkeyToggle);
            y += 28;

            AddLabel("Hotkey - Task View:", 12, y, labelW);
            _txtHotkeyTaskView = new TextBox { Left = inputX, Top = y - 3, Width = 200 };
            Controls.Add(_txtHotkeyTaskView);
            y += 30;

            var lblHelp = new Label
            {
                Text = "Hotkey format: Ctrl+Alt+F11   |   Modifiers: Ctrl, Alt, Shift, Win.\n" +
                       "Empty = no hotkey. Restart not required.",
                Left = 12, Top = y, Width = 416, Height = 36,
                ForeColor = SystemColors.GrayText,
            };
            Controls.Add(lblHelp);
            y += 40;

            var btnOk = new Button { Text = "Save", Left = 252, Top = ClientSize.Height - 36, Width = 80 };
            btnOk.Click += OnSaveClicked;
            Controls.Add(btnOk);
            AcceptButton = btnOk;

            var btnCancel = new Button { Text = "Cancel", Left = 340, Top = ClientSize.Height - 36, Width = 88 };
            btnCancel.Click += (s, e) => Close();
            Controls.Add(btnCancel);
            CancelButton = btnCancel;

            var btnReset = new Button { Text = "Reset to defaults", Left = 12, Top = ClientSize.Height - 36, Width = 130 };
            btnReset.Click += (s, e) => { LoadFromDefaults(); };
            Controls.Add(btnReset);
        }

        private CheckBox AddCheckBox(string text, int x, ref int y)
        {
            var cb = new CheckBox { Text = text, Left = x, Top = y, AutoSize = true };
            Controls.Add(cb);
            y += 26;
            return cb;
        }

        private void AddLabel(string text, int x, int y, int w)
        {
            var lbl = new Label { Text = text, Left = x, Top = y, Width = w, AutoSize = false };
            Controls.Add(lbl);
        }

        private void LoadFromSettings()
        {
            _cbEnabled.Checked = _working.Enabled;
            _cbMinimizeSingle.Checked = _working.MinimizeOnSingleClick;
            _cbTaskViewDouble.Checked = _working.TaskViewOnDoubleClick;
            _cbAutoStart.Checked = Startup.IsEnabled();
            _numDouble.Value = Math.Max(_numDouble.Minimum, Math.Min(_numDouble.Maximum, _working.DoubleClickMs));
            _numDelay.Value = Math.Max(_numDelay.Minimum, Math.Min(_numDelay.Maximum, _working.SingleClickDelayMs));
            _txtExcluded.Text = (_working.ExcludedProcesses ?? "").Replace(",", Environment.NewLine);
            _txtHotkeyToggle.Text = _working.HotkeyToggleDesktop ?? "";
            _txtHotkeyTaskView.Text = _working.HotkeyTaskView ?? "";
        }

        private void LoadFromDefaults()
        {
            var d = AppSettings.Defaults();
            _cbEnabled.Checked = d.Enabled;
            _cbMinimizeSingle.Checked = d.MinimizeOnSingleClick;
            _cbTaskViewDouble.Checked = d.TaskViewOnDoubleClick;
            _numDouble.Value = d.DoubleClickMs;
            _numDelay.Value = d.SingleClickDelayMs;
            _txtExcluded.Text = "";
            _txtHotkeyToggle.Text = d.HotkeyToggleDesktop;
            _txtHotkeyTaskView.Text = d.HotkeyTaskView;
        }

        private void OnSaveClicked(object sender, EventArgs e)
        {
            // Validate hotkeys early so user gets feedback
            uint m, v;
            if (!string.IsNullOrWhiteSpace(_txtHotkeyToggle.Text) &&
                !HotkeyHost.TryParse(_txtHotkeyToggle.Text, out m, out v))
            {
                MessageBox.Show("Toggle Desktop hotkey is invalid.\nExample: Ctrl+Alt+F11",
                    "Invalid hotkey", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!string.IsNullOrWhiteSpace(_txtHotkeyTaskView.Text) &&
                !HotkeyHost.TryParse(_txtHotkeyTaskView.Text, out m, out v))
            {
                MessageBox.Show("Task View hotkey is invalid.\nExample: Ctrl+Alt+F12",
                    "Invalid hotkey", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _working.Enabled = _cbEnabled.Checked;
            _working.MinimizeOnSingleClick = _cbMinimizeSingle.Checked;
            _working.TaskViewOnDoubleClick = _cbTaskViewDouble.Checked;
            _working.DoubleClickMs = (int)_numDouble.Value;
            _working.SingleClickDelayMs = (int)_numDelay.Value;
            _working.ExcludedProcesses = string.Join(",",
                _txtExcluded.Text.Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).Where(s => s.Length > 0));
            _working.HotkeyToggleDesktop = _txtHotkeyToggle.Text.Trim();
            _working.HotkeyTaskView = _txtHotkeyTaskView.Text.Trim();

            Startup.SetEnabled(_cbAutoStart.Checked);
            _ctx.ApplySettings(_working);
            Close();
        }
    }

    // ---------------------------------------------------------------------
    // Native interop
    // ---------------------------------------------------------------------
    internal static class Native
    {
        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        public const int WH_MOUSE_LL = 14;
        public const int WM_LBUTTONDOWN = 0x0201;

        public const int SW_SHOWNORMAL    = 1;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_SHOWMAXIMIZED = 3;
        public const int SW_MINIMIZE = 6;
        public const int SW_RESTORE  = 9;

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;

        public const uint DWMWA_CLOAKED = 14;

        public const byte VK_LWIN = 0x5B;
        public const byte VK_TAB = 0x09;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left, top, right, bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT  rcNormalPosition;
        }

        public const int LVM_FIRST   = 0x1000;
        public const int LVM_HITTEST = LVM_FIRST + 18;

        public const uint LVHT_NOWHERE         = 0x00000001;
        public const uint LVHT_ONITEMICON      = 0x00000002;
        public const uint LVHT_ONITEMLABEL     = 0x00000004;
        public const uint LVHT_ONITEMSTATEICON = 0x00000008;
        public const uint LVHT_ONITEM          = LVHT_ONITEMICON | LVHT_ONITEMLABEL | LVHT_ONITEMSTATEICON;

        [StructLayout(LayoutKind.Sequential)]
        public struct LVHITTESTINFO
        {
            public POINT pt;
            public uint  flags;
            public int   iItem;
            public int   iSubItem;
            public int   iGroup;
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

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        public static extern IntPtr WindowFromPoint(POINT pt);

        [DllImport("user32.dll")]
        public static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("dwmapi.dll")]
        public static extern int DwmGetWindowAttribute(IntPtr hwnd, uint dwAttribute, out int pvAttribute, int cbAttribute);

        [DllImport("user32.dll")]
        public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

        [DllImport("user32.dll")]
        public static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool DestroyIcon(IntPtr handle);

        public const uint PROCESS_VM_OPERATION = 0x0008;
        public const uint PROCESS_VM_READ      = 0x0010;
        public const uint PROCESS_VM_WRITE     = 0x0020;

        public const uint MEM_COMMIT    = 0x1000;
        public const uint MEM_RESERVE   = 0x2000;
        public const uint MEM_RELEASE   = 0x8000;
        public const uint PAGE_READWRITE = 0x04;

        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        [DllImport("kernel32.dll")]
        public static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, IntPtr dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll")]
        public static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, int dwSize, uint dwFreeType);

        [DllImport("kernel32.dll")]
        public static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll")]
        public static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, IntPtr lpBuffer, IntPtr nSize, out IntPtr lpNumberOfBytesRead);

        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr hObject);
    }
}
