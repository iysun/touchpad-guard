// TouchpadGuard - 打字时忽略触摸板/鼠标的移动与点击（防止手掌误触导致光标漂移）
// 编译: csc /target:winexe /out:TouchpadGuard.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll TouchpadGuard.cs
using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TouchpadGuard
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (var mutex = new Mutex(true, "TouchpadGuard_SingleInstance", out created))
            {
                if (!created) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WH_MOUSE_LL = 14;

        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_MBUTTONDOWN = 0x0207;
        private const int WM_MBUTTONUP = 0x0208;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_XBUTTONDOWN = 0x020B;
        private const int WM_XBUTTONUP = 0x020C;
        private const int WM_MOUSEHWHEEL = 0x020E;

        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;
        private const int VK_MBUTTON = 0x04;

        private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        private readonly LowLevelProc _keyboardProc;
        private readonly LowLevelProc _mouseProc;
        private IntPtr _keyboardHook = IntPtr.Zero;
        private IntPtr _mouseHook = IntPtr.Zero;

        private readonly NotifyIcon _tray;
        private readonly ToolStripMenuItem _enableItem;
        private readonly ToolStripMenuItem _autostartItem;
        private readonly ToolStripMenuItem[] _delayItems;

        private volatile bool _enabled = true;
        private int _delayMs = 350;
        private int _lastKeyTick = int.MinValue / 2;
        private readonly System.Windows.Forms.Timer _watchdog;

        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "TouchpadGuard";
        private const string AppKey = @"Software\TouchpadGuard";

        public TrayApp()
        {
            LoadSettings();

            _keyboardProc = KeyboardHook;
            _mouseProc = MouseHook;

            var menu = new ContextMenuStrip();

            _enableItem = new ToolStripMenuItem("打字时忽略触摸板/鼠标", null, OnToggleEnabled)
            {
                CheckOnClick = true,
                Checked = _enabled
            };
            menu.Items.Add(_enableItem);

            var delayMenu = new ToolStripMenuItem("停手后恢复延迟");
            int[] delays = new int[] { 200, 350, 500, 800 };
            _delayItems = new ToolStripMenuItem[delays.Length];
            for (int i = 0; i < delays.Length; i++)
            {
                int ms = delays[i];
                var item = new ToolStripMenuItem(ms + " 毫秒", null, OnPickDelay)
                {
                    Tag = ms,
                    Checked = (ms == _delayMs)
                };
                _delayItems[i] = item;
                delayMenu.DropDownItems.Add(item);
            }
            menu.Items.Add(delayMenu);

            menu.Items.Add(new ToolStripSeparator());

            _autostartItem = new ToolStripMenuItem("开机自动启动", null, OnToggleAutostart)
            {
                CheckOnClick = true,
                Checked = IsAutostartEnabled()
            };
            menu.Items.Add(_autostartItem);

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("退出", null, OnExit));

            _tray = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "",
                ContextMenuStrip = menu,
                Visible = true
            };
            _tray.DoubleClick += OnTrayDoubleClick;
            UpdateTooltip();

            InstallHooks();

            // 睡眠唤醒 / 桌面切换后低级钩子可能失效，定时补装一次
            _watchdog = new System.Windows.Forms.Timer { Interval = 10000 };
            _watchdog.Tick += delegate { InstallHooks(); };
            _watchdog.Start();

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }

        // ---------- 钩子 ----------

        private void InstallHooks()
        {
            if (_keyboardHook == IntPtr.Zero)
            {
                _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, GetModuleHandle(null), 0);
            }
            if (_mouseHook == IntPtr.Zero)
            {
                _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(null), 0);
            }
        }

        private void UninstallHooks()
        {
            if (_keyboardHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
            }
            if (_mouseHook != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_mouseHook);
                _mouseHook = IntPtr.Zero;
            }
        }

        private IntPtr KeyboardHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                // 任何键按下/抬起都记为“正在打字”
                _lastKeyTick = Environment.TickCount;
            }
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        private IntPtr MouseHook(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && _enabled && IsWithinTypingWindow())
            {
                int msg = wParam.ToInt32();

                // 正在按住鼠标键拖动时（如拖选文字）一律放行，避免打断拖拽
                if (IsAnyButtonDown() &&
                    (msg == WM_MOUSEMOVE || msg == WM_LBUTTONUP || msg == WM_RBUTTONUP || msg == WM_MBUTTONUP))
                {
                    return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
                }

                bool blockable =
                    msg == WM_MOUSEMOVE ||
                    msg == WM_LBUTTONDOWN || msg == WM_LBUTTONUP ||
                    msg == WM_RBUTTONDOWN || msg == WM_RBUTTONUP ||
                    msg == WM_MBUTTONDOWN || msg == WM_MBUTTONUP ||
                    msg == WM_XBUTTONDOWN || msg == WM_XBUTTONUP ||
                    msg == WM_MOUSEWHEEL || msg == WM_MOUSEHWHEEL;

                if (blockable)
                {
                    return (IntPtr)1; // 吞掉这次输入
                }
            }
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        private bool IsWithinTypingWindow()
        {
            int delta = unchecked(Environment.TickCount - _lastKeyTick);
            return delta >= 0 && delta < _delayMs;
        }

        private static bool IsAnyButtonDown()
        {
            return (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0
                || (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0
                || (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                UninstallHooks();
                InstallHooks();
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            UninstallHooks();
            InstallHooks();
        }

        // ---------- 菜单交互 ----------

        private void OnToggleEnabled(object sender, EventArgs e)
        {
            _enabled = _enableItem.Checked;
            SaveSettings();
            UpdateTooltip();
        }

        private void OnPickDelay(object sender, EventArgs e)
        {
            var item = (ToolStripMenuItem)sender;
            _delayMs = (int)item.Tag;
            foreach (var d in _delayItems) d.Checked = ReferenceEquals(d, item);
            SaveSettings();
            UpdateTooltip();
        }

        private void OnTrayDoubleClick(object sender, EventArgs e)
        {
            _enabled = !_enabled;
            _enableItem.Checked = _enabled;
            SaveSettings();
            UpdateTooltip();
        }

        private void OnToggleAutostart(object sender, EventArgs e)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (_autostartItem.Checked)
                    {
                        key.SetValue(RunValue, "\"" + Application.ExecutablePath + "\"");
                    }
                    else
                    {
                        key.DeleteValue(RunValue, false);
                    }
                }
            }
            catch (Exception)
            {
                _autostartItem.Checked = IsAutostartEnabled();
            }
        }

        private static bool IsAutostartEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    var value = key.GetValue(RunValue) as string;
                    return !string.IsNullOrEmpty(value);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void UpdateTooltip()
        {
            string state = _enabled ? "开" : "关";
            string text = "触控板打字保护：" + state + "（" + _delayMs + "ms）";
            // NotifyIcon.Text 上限 63 字符
            if (text.Length > 63) text = text.Substring(0, 63);
            _tray.Text = text;
        }

        private void OnExit(object sender, EventArgs e)
        {
            _watchdog.Stop();
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            UninstallHooks();
            _tray.Visible = false;
            _tray.Dispose();
            ExitThread();
        }

        // ---------- 设置持久化 ----------

        private void LoadSettings()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(AppKey, false))
                {
                    if (key == null) return;
                    var enabled = key.GetValue("Enabled");
                    if (enabled is int) _enabled = ((int)enabled) != 0;
                    var delay = key.GetValue("DelayMs");
                    if (delay is int) _delayMs = (int)delay;
                }
            }
            catch (Exception) { }
        }

        private void SaveSettings()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(AppKey))
                {
                    if (key == null) return;
                    key.SetValue("Enabled", _enabled ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("DelayMs", _delayMs, RegistryValueKind.DWord);
                }
            }
            catch (Exception) { }
        }
    }
}
