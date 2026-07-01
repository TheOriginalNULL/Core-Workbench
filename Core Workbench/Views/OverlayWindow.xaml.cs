using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Core_Workbench.Services;

namespace Core_Workbench.Views
{
    /// <summary>
    /// Always-on-top performance OSD. Shows the metrics enabled in
    /// <see cref="OverlaySettings"/>, snaps to a screen corner, and (optionally)
    /// lets clicks pass through so it doesn't get in the way of a game.
    /// Overlays windowed / borderless-fullscreen apps; exclusive fullscreen can't
    /// be drawn over without render-hook injection.
    /// </summary>
    public partial class OverlayWindow : Window
    {
        private static readonly Brush LabelCpu = Frozen(0x8B, 0x6F, 0xE0);
        private static readonly Brush LabelGpu = Frozen(0x5B, 0xE3, 0xA0);
        private static readonly Brush LabelRam = Frozen(0x6F, 0xB1, 0xFF);
        private static readonly Brush LabelNet = Frozen(0xFF, 0xB4, 0x54);
        private static readonly Brush ValueBrush = Frozen(0xFF, 0xFF, 0xFF);

        private static readonly Brush LabelFps = Frozen(0xFF, 0xD9, 0x6B);

        private readonly HardwareMonitorService _hw = HardwareMonitor.Instance;
        private readonly NetworkService _net = new();
        private readonly FpsService _fps = new();
        private readonly DispatcherTimer _timer;
        private bool _busy;
        private double _fontSize = 13;
        private int _columns = 1;
        private double _lineSpacing = 1;

        private Brush _cpuBrush = LabelCpu, _gpuBrush = LabelGpu, _ramBrush = LabelRam,
                      _netBrush = LabelNet, _valueBrush = ValueBrush;

        public OverlayWindow()
        {
            InitializeComponent();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _timer.Tick += async (_, _) => await Tick();
            Loaded += async (_, _) => { _fps.Start(); ApplyLook(); await Tick(); _timer.Start(); };
            SizeChanged += (_, _) => PositionToCorner();
            SourceInitialized += (_, _) => ApplyClickThrough();
            Closed += (_, _) => { _timer.Stop(); _fps.Dispose(); };
        }

        /// <summary>Apply opacity, font scale, click-through, and reposition.</summary>
        public void ApplyLook()
        {
            var s = OverlaySettings.Current;
            Opacity = Math.Clamp(s.Opacity, 0.2, 1.0);
            _fontSize = 13 * Math.Clamp(s.FontScale, 0.6, 2.0);
            _columns = Math.Clamp(s.Columns, 1, 3);
            _lineSpacing = Math.Clamp(s.LineSpacing, 0, 14);

            _cpuBrush = ToBrush(s.CpuColor, LabelCpu);
            _gpuBrush = ToBrush(s.GpuColor, LabelGpu);
            _ramBrush = ToBrush(s.RamColor, LabelRam);
            _netBrush = ToBrush(s.NetColor, LabelNet);
            _valueBrush = ToBrush(s.ValueColor, ValueBrush);
            Backplate.Background = BackBrush(s.BackColor, s.BackAlpha);

            ApplyClickThrough();
            _ = Tick();   // rebuild now so the new size/metrics apply immediately
            PositionToCorner();
        }

        private async Task Tick()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                var s = OverlaySettings.Current;
                HardwareSnapshot hw = await Task.Run(() => _hw.Read());

                var rows = new List<(string label, Brush color, string value)>();

                if (s.ShowCpu) rows.Add(("CPU", _cpuBrush, $"{hw.CpuLoad:0} %"));
                if (s.ShowCpuTemp && hw.CpuTemp.HasValue) rows.Add(("CPU temp", _cpuBrush, $"{hw.CpuTemp:0} °C"));
                if (s.ShowCpuClock && hw.CpuClockMhz.HasValue) rows.Add(("CPU clock", _cpuBrush, $"{hw.CpuClockMhz:0} MHz"));
                if (s.ShowCpuPower && hw.CpuPowerW.HasValue) rows.Add(("CPU power", _cpuBrush, $"{hw.CpuPowerW:0} W"));
                if (s.ShowCpuCores)
                    foreach (var core in hw.CpuCores)
                        rows.Add(($"Core {core.Index}", _cpuBrush,
                            core.ClockMhz.HasValue ? $"{core.Load:0} %  {core.ClockMhz:0} MHz" : $"{core.Load:0} %"));

                if (s.ShowGpu) rows.Add(("GPU", _gpuBrush, $"{hw.GpuLoad:0} %"));
                if (s.ShowGpuTemp && hw.GpuTemp.HasValue) rows.Add(("GPU temp", _gpuBrush, $"{hw.GpuTemp:0} °C"));
                if (s.ShowGpuClock && hw.GpuCoreClockMhz.HasValue) rows.Add(("GPU clock", _gpuBrush, $"{hw.GpuCoreClockMhz:0} MHz"));
                if (s.ShowGpuMemClock && hw.GpuMemClockMhz.HasValue) rows.Add(("GPU mem", _gpuBrush, $"{hw.GpuMemClockMhz:0} MHz"));
                if (s.ShowGpuVram && hw.GpuVramUsedMb.HasValue) rows.Add(("VRAM", _gpuBrush, $"{hw.GpuVramUsedMb:0} MB"));
                if (s.ShowGpuPower && hw.GpuPowerW.HasValue) rows.Add(("GPU power", _gpuBrush, $"{hw.GpuPowerW:0} W"));
                if (s.ShowGpuFan && hw.GpuFanPercent.HasValue) rows.Add(("GPU fan", _gpuBrush, $"{hw.GpuFanPercent:0} %"));

                if (s.ShowRam) rows.Add(("RAM", _ramBrush,
                    hw.RamTotalGb > 0 ? $"{hw.RamUsedGb:0.0}/{hw.RamTotalGb:0.0} GB" : $"{hw.RamLoad:0} %"));
                if (s.ShowNet)
                {
                    var ad = _net.Read();
                    rows.Add(("NET", _netBrush, $"↓{Rate(ad.Sum(a => a.DownBytesPerSec))} ↑{Rate(ad.Sum(a => a.UpBytesPerSec))}"));
                }
                if (s.ShowFps || s.ShowFrametime || s.ShowLows)
                {
                    FpsSample? fps = _fps.Available ? _fps.Get(ForegroundPid()) : null;
                    if (s.ShowFps) rows.Add(("FPS", LabelFps, fps != null ? $"{fps.Fps:0}" : "—"));
                    if (s.ShowFrametime) rows.Add(("Frametime", LabelFps, fps != null ? $"{fps.FrametimeMs:0.0} ms" : "—"));
                    if (s.ShowLows && fps != null && fps.Low1 > 0)
                    {
                        rows.Add(("1% low", LabelFps, $"{fps.Low1:0}"));
                        rows.Add(("0.1% low", LabelFps, $"{fps.Low01:0}"));
                    }
                }

                if (s.ShowClock) rows.Add(("TIME", _valueBrush, DateTime.Now.ToString("HH:mm:ss")));

                if (rows.Count == 0)
                    rows.Add(("", _valueBrush, "Pick metrics in the Overlay tab"));

                Lines.Children.Clear();
                Lines.Children.Add(BuildContent(rows));
                PositionToCorner();
            }
            catch { }
            finally { _busy = false; }
        }

        /// <summary>Lay the rows out in one or more side-by-side columns.</summary>
        private UIElement BuildContent(List<(string label, Brush color, string value)> rows)
        {
            if (_columns <= 1) return BuildGrid(rows);

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            int per = (int)Math.Ceiling(rows.Count / (double)_columns);
            for (int c = 0; c < _columns; c++)
            {
                var chunk = rows.Skip(c * per).Take(per).ToList();
                if (chunk.Count == 0) continue;
                var g = BuildGrid(chunk);
                g.Margin = new Thickness(c == 0 ? 0 : 22, 0, 0, 0);
                panel.Children.Add(g);
            }
            return panel;
        }

        private Grid BuildGrid(List<(string label, Brush color, string value)> rows)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            for (int i = 0; i < rows.Count; i++)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var (label, color, value) = rows[i];

                var lbl = new TextBlock
                {
                    Text = label, Foreground = color, FontWeight = FontWeights.Bold,
                    FontSize = _fontSize, Margin = new Thickness(0, _lineSpacing, 14, _lineSpacing),
                };
                Grid.SetRow(lbl, i); Grid.SetColumn(lbl, 0);
                grid.Children.Add(lbl);

                var val = new TextBlock
                {
                    Text = value, Foreground = _valueBrush, FontSize = _fontSize,
                    Margin = new Thickness(0, _lineSpacing, 0, _lineSpacing), TextAlignment = TextAlignment.Right,
                };
                Grid.SetRow(val, i); Grid.SetColumn(val, 1);
                grid.Children.Add(val);
            }
            return grid;
        }

        private void PositionToCorner()
        {
            var wa = SystemParameters.WorkArea;
            const double m = 12;
            double w = ActualWidth, h = ActualHeight;
            switch (OverlaySettings.Current.Corner)
            {
                case OverlayCorner.TopLeft: Left = wa.Left + m; Top = wa.Top + m; break;
                case OverlayCorner.BottomLeft: Left = wa.Left + m; Top = wa.Bottom - h - m; break;
                case OverlayCorner.BottomRight: Left = wa.Right - w - m; Top = wa.Bottom - h - m; break;
                default: Left = wa.Right - w - m; Top = wa.Top + m; break;   // TopRight
            }
        }

        // ---- click-through ----

        private void ApplyClickThrough()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            int ex = GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_LAYERED | WS_EX_TOOLWINDOW;
            if (OverlaySettings.Current.ClickThrough) ex |= WS_EX_TRANSPARENT;
            else ex &= ~WS_EX_TRANSPARENT;
            SetWindowLong(hwnd, GWL_EXSTYLE, ex);
        }

        /// <summary>PID of the focused window — the game we want FPS for (the overlay never steals focus).</summary>
        private static int ForegroundPid()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return -1;
                GetWindowThreadProcessId(hwnd, out int pid);
                return pid;
            }
            catch { return -1; }
        }

        private static string Rate(double bps)
        {
            if (bps >= 1024 * 1024) return $"{bps / 1024 / 1024:0.0}M";
            if (bps >= 1024) return $"{bps / 1024:0}K";
            return $"{bps:0}B";
        }

        private static Brush Frozen(byte r, byte g, byte b)
        {
            var br = new SolidColorBrush(Color.FromRgb(r, g, b));
            br.Freeze();
            return br;
        }

        private static Brush ToBrush(string hex, Brush fallback)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                var b = new SolidColorBrush(c);
                b.Freeze();
                return b;
            }
            catch { return fallback; }
        }

        private static Brush BackBrush(string hex, double alpha)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                c.A = (byte)(Math.Clamp(alpha, 0, 1) * 255);
                var b = new SolidColorBrush(c);
                b.Freeze();
                return b;
            }
            catch
            {
                var b = new SolidColorBrush(Color.FromArgb(0xB2, 0, 0, 0));
                b.Freeze();
                return b;
            }
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TOOLWINDOW = 0x80;

        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);
    }
}
