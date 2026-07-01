using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Core_Workbench.Services;
using Core_Workbench.Views;

namespace Core_Workbench
{
    /// <summary>
    /// Application shell: custom title bar + sidebar navigation that swaps tool
    /// pages into the content area. To add a tool: create a UserControl in Views/,
    /// add a RadioButton in MainWindow.xaml, and register it in GetPage().
    /// </summary>
    public partial class MainWindow : Window
    {
        // Pages are created once and reused so their state survives navigation.
        private readonly Dictionary<string, UserControl> _pages = new();
        private TrayIcon? _tray;
        private bool _exiting;

        public MainWindow()
        {
            InitializeComponent();
            StatusText.Text = IsAdministrator() ? "admin · temps on" : "limited · no temps";

            SourceInitialized += OnSourceInitialized;
            StateChanged += MainWindow_StateChanged;
            Closing += MainWindow_Closing;

            _tray = new TrayIcon(onOpen: RestoreFromTray, onExit: ExitApp);

            Navigate("Dashboard");

            // Bring back any note widgets pinned in a previous session.
            WidgetManager.RestoreSaved();

            // Re-show the performance overlay if it was left enabled.
            OverlayManager.RestoreIfEnabled();
        }

        // ---------------- Tray / minimize behaviour ----------------

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            UpdateMaxGlyph();
            if (WindowState == WindowState.Minimized && AppSettings.Current.MinimizeToTray)
            {
                Hide();
                _tray?.ShowBalloon("Core Workbench", "Still running in the tray.");
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            if (_exiting) return;   // cleanup handled by the caller

            // X button minimizes to tray instead of quitting, if enabled.
            if (AppSettings.Current.CloseToTray)
            {
                e.Cancel = true;
                Hide();
                _tray?.ShowBalloon("Core Workbench", "Minimized to tray. Right-click the icon → Exit to quit.");
                return;
            }

            // Normal close → exit the whole app (and its desktop widgets).
            _exiting = true;
            _tray?.Dispose();
            WidgetManager.PrepareForShutdown();
            Application.Current.Shutdown();
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void ExitApp()
        {
            _exiting = true;
            _tray?.Dispose();
            WidgetManager.PrepareForShutdown();
            Application.Current.Shutdown();
        }

        // ---------------- Navigation ----------------

        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Content is string name && IsLoaded)
                Navigate(name);
        }

        private void Navigate(string name)
        {
            if (!_pages.TryGetValue(name, out var page))
            {
                page = GetPage(name);
                _pages[name] = page;
            }
            ContentHost.Content = page;
        }

        private static UserControl GetPage(string name) => name switch
        {
            "Dashboard" => new DashboardPage(),
            "Performance" => new PerformancePage(),
            "Processes" => new ProcessesPage(),
            "Overlay" => new OverlayPage(),
            "Cleaner" => new CleanerPage(),
            "Startup" => new StartupPage(),
            "Apps" => new ProgramsPage(),
            "Notes" => new NotesPage(),
            "Widgets" => new WidgetsPage(),
            "System Info" => new SystemInfoPage(),
            "Settings" => new SettingsPage(),
            _ => new DashboardPage(),
        };

        /// <summary>Programmatic navigation (used by the dashboard's quick-access cards).</summary>
        public void NavigateTo(string name)
        {
            RadioButton? target = name switch
            {
                "Dashboard" => NavDashboard,
                "Performance" => NavPerformance,
                "Processes" => NavProcesses,
                "Overlay" => NavOverlay,
                "Cleaner" => NavCleaner,
                "Startup" => NavStartup,
                "Apps" => NavApps,
                "Notes" => NavNotes,
                "Widgets" => NavWidgets,
                "System Info" => NavSystemInfo,
                "Settings" => NavSettings,
                _ => null,
            };
            if (target != null) target.IsChecked = true;   // fires Nav_Checked → Navigate
        }

        // ---------------- Title bar buttons ----------------

        private void Minimize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void Maximize_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void UpdateMaxGlyph()
            // Swap the vector icon: restore when maximized, else maximize.
            => MaxButton.Tag = FindResource(WindowState == WindowState.Maximized ? "IconRestore" : "IconMax");

        private static bool IsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        // ---------------- Maximize fix (don't cover the taskbar) ----------------

        private IntPtr _hwnd;

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(_hwnd)?.AddHook(WindowProc);
            ApplyOverlayHotkey();
        }

        /// <summary>(Re)register the global overlay-toggle hotkey from settings. Called by the Overlay page too.</summary>
        public void ApplyOverlayHotkey()
        {
            if (_hwnd == IntPtr.Zero) return;
            UnregisterHotKey(_hwnd, HOTKEY_ID);
            var s = OverlaySettings.Current;
            if (s.HotkeyEnabled && s.HotkeyVk != 0)
                RegisterHotKey(_hwnd, HOTKEY_ID, s.HotkeyMods, s.HotkeyVk);
        }

        private const int WM_GETMINMAXINFO = 0x0024;
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 0xB001;
        private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

        private IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_GETMINMAXINFO)
            {
                WmGetMinMaxInfo(hwnd, lParam);
                handled = true;
            }
            else if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                OverlayManager.SetEnabled(!OverlaySettings.Current.Enabled);
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void WmGetMinMaxInfo(IntPtr hwnd, IntPtr lParam)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (monitor != IntPtr.Zero)
            {
                var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                GetMonitorInfo(monitor, ref info);
                RECT work = info.rcWork, mon = info.rcMonitor;

                // Position/size the maximized window to the monitor's WORK area
                // (excludes the taskbar), expressed relative to the monitor origin.
                mmi.ptMaxPosition.x = work.left - mon.left;
                mmi.ptMaxPosition.y = work.top - mon.top;
                mmi.ptMaxSize.x = work.right - work.left;
                mmi.ptMaxSize.y = work.bottom - work.top;

                // Respect the window's minimum size.
                mmi.ptMinTrackSize.x = (int)MinWidth;
                mmi.ptMinTrackSize.y = (int)MinHeight;
            }
            Marshal.StructureToPtr(mmi, lParam, true);
        }

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int x; public int y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public POINT ptReserved;
            public POINT ptMaxSize;
            public POINT ptMaxPosition;
            public POINT ptMinTrackSize;
            public POINT ptMaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }
    }
}
