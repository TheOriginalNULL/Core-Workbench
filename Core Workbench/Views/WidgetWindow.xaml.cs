using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Core_Workbench.Controls;
using Core_Workbench.Models;
using Core_Workbench.Services;

namespace Core_Workbench.Views
{
    /// <summary>
    /// A desktop widget window. The chrome (drag, resize, opacity, desktop/float
    /// mode, persistence) is shared; the body is built per <see cref="WidgetType"/>
    /// — a note, a live CPU/GPU/RAM gauge, or a clock.
    /// </summary>
    public partial class WidgetWindow : Window
    {
        private readonly NotesService _notes = new();
        private readonly WidgetType _type;
        private readonly Guid _noteId;

        private bool _onDesktop;
        private bool _pinTopmost = true;
        private HwndSource? _source;
        private DispatcherTimer? _timer;

        // live widget visuals
        private RadialGauge? _gauge;
        private TextBlock? _sub;
        private TextBlock? _clockTime;
        private TextBlock? _clockDate;
        private FlowDocumentScrollViewer? _viewer;
        private Sparkline? _sCpu, _sGpu, _sRam, _sNet;
        private TextBlock? _vCpu, _vGpu, _vRam, _vNet;
        private readonly NetworkService _net = new();
        private bool _busy;

        private static readonly Brush CpuColor = new SolidColorBrush(Color.FromRgb(0x8B, 0x6F, 0xE0));
        private static readonly Brush GpuColor = new SolidColorBrush(Color.FromRgb(0x5B, 0xE3, 0xA0));
        private static readonly Brush RamColor = new SolidColorBrush(Color.FromRgb(0x6F, 0xB1, 0xFF));
        private static readonly Brush NetColor = new SolidColorBrush(Color.FromRgb(0xFF, 0xB4, 0x54));

        // manual drag
        private bool _dragging;
        private POINT _dragStart;
        private double _startLeft, _startTop;

        // manual resize (custom grip)
        private bool _resizing;
        private POINT _resizeStart;
        private double _startWidth, _startHeight;

        public WidgetType Type => _type;
        public string TitleLabel => TitleText.Text;

        public WidgetWindow(WidgetState state)
        {
            InitializeComponent();
            _type = state.Type;
            _noteId = state.NoteId;

            Width = state.Width;
            Height = state.Height;
            Opacity = Math.Clamp(state.Opacity, 0.3, 1.0);
            _pinTopmost = state.Topmost;
            _onDesktop = state.OnDesktop;
            Topmost = !_onDesktop && _pinTopmost;

            if (!double.IsNaN(state.Left) && !double.IsNaN(state.Top))
            {
                Left = state.Left;
                Top = state.Top;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                var wa = SystemParameters.WorkArea;
                Left = wa.Right - Width - 40;
                Top = wa.Top + 60;
            }

            BuildBody();
            UpdateVisuals();

            SourceInitialized += OnSourceInitialized;
            Closed += (_, _) => _timer?.Stop();
        }

        private void OnSourceInitialized(object? sender, EventArgs e)
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(WndProc);
            if (_onDesktop) ApplyDesktop(true);
        }

        // ---------------- body per type ----------------

        private void BuildBody()
        {
            switch (_type)
            {
                case WidgetType.Note:
                    TitleText.Text = "Note";
                    _viewer = new FlowDocumentScrollViewer
                    {
                        IsToolBarVisible = false,
                        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Background = Brushes.Transparent,
                        Foreground = Brush("TextBrush"),
                        Padding = new Thickness(6, 0, 6, 6),
                    };
                    Body.Content = _viewer;
                    ReloadContent();
                    break;

                case WidgetType.Bar:
                    TitleText.Text = "System";
                    Body.Content = BuildBar();
                    StartTimer(TimeSpan.FromSeconds(1), async () => await TickBar());
                    _ = TickBar();
                    break;

                case WidgetType.Clock:
                    TitleText.Text = "Clock";
                    _clockTime = new TextBlock
                    {
                        FontSize = 36, FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Foreground = Brush("TextBrush"),
                    };
                    _clockDate = Subtle();
                    _clockDate.HorizontalAlignment = HorizontalAlignment.Center;
                    _clockDate.Margin = new Thickness(0, 4, 0, 0);
                    Body.Content = new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Children = { _clockTime, _clockDate },
                    };
                    StartTimer(TimeSpan.FromSeconds(1), TickClock);
                    TickClock();
                    break;

                default: // Cpu / Gpu / Ram
                    TitleText.Text = _type switch
                    {
                        WidgetType.Cpu => "CPU",
                        WidgetType.Gpu => "GPU",
                        _ => "Memory",
                    };
                    _gauge = new RadialGauge
                    {
                        Width = 120, Height = 120,
                        Caption = _type == WidgetType.Ram ? "used" : "load",
                        HorizontalAlignment = HorizontalAlignment.Center,
                    };
                    _sub = Subtle();
                    _sub.HorizontalAlignment = HorizontalAlignment.Center;
                    _sub.Margin = new Thickness(0, 6, 0, 0);
                    Body.Content = new StackPanel
                    {
                        VerticalAlignment = VerticalAlignment.Center,
                        Children = { _gauge, _sub },
                    };
                    StartTimer(TimeSpan.FromSeconds(1), async () => await TickHardware());
                    _ = TickHardware();
                    break;
            }
        }

        /// <summary>Reload note content (no-op for non-note widgets).</summary>
        public void ReloadContent()
        {
            if (_type != WidgetType.Note || _viewer == null) return;
            var note = _notes.Load().FirstOrDefault(n => n.Id == _noteId);
            if (note == null) { Close(); return; }
            TitleText.Text = note.DisplayTitle;
            _viewer.Document = LoadDocument(note);
        }

        // ---------------- system bar ----------------

        private FrameworkElement BuildBar()
        {
            var grid = new Grid();
            // 4 metrics, each followed by a divider, then the clock.
            for (int i = 0; i < 4; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var cpu = MakeMetric("CPU", CpuColor); _sCpu = cpu.spark; _vCpu = cpu.value;
            var gpu = MakeMetric("GPU", GpuColor); _sGpu = gpu.spark; _vGpu = gpu.value;
            var ram = MakeMetric("RAM", RamColor); _sRam = ram.spark; _vRam = ram.value;
            var net = MakeMetric("NET", NetColor); _sNet = net.spark; _sNet.AutoScale = true; _vNet = net.value;

            Place(grid, cpu.cell, 0);
            Place(grid, Divider(), 1);
            Place(grid, gpu.cell, 2);
            Place(grid, Divider(), 3);
            Place(grid, ram.cell, 4);
            Place(grid, Divider(), 5);
            Place(grid, net.cell, 6);
            Place(grid, Divider(), 7);

            _clockTime = new TextBlock
            {
                FontSize = 18, FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Foreground = Brush("TextBrush"),
            };
            _clockDate = Subtle();
            _clockDate.FontSize = 10;
            _clockDate.HorizontalAlignment = HorizontalAlignment.Center;
            var clock = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 8, 0),
                Children = { _clockTime, _clockDate },
            };
            Place(grid, clock, 8);
            return grid;
        }

        private static void Place(Grid g, FrameworkElement el, int col)
        {
            Grid.SetColumn(el, col);
            g.Children.Add(el);
        }

        private static Border Divider() => new()
        {
            Width = 1,
            Background = Brush("BorderBrush"),
            Margin = new Thickness(0, 12, 0, 12),
        };

        private static (FrameworkElement cell, Sparkline spark, TextBlock value) MakeMetric(string label, Brush color)
        {
            var top = new Grid();
            top.Children.Add(new TextBlock
            {
                Text = label, FontWeight = FontWeights.SemiBold, FontSize = 10,
                Foreground = Brush("TextMutedBrush"),
            });
            var value = new TextBlock
            {
                Text = "--", FontSize = 11, FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right, Foreground = color,
            };
            top.Children.Add(value);

            var spark = new Sparkline { LineBrush = color, LineThickness = 1.4, Margin = new Thickness(0, 4, 0, 0) };

            var dock = new DockPanel { Margin = new Thickness(10, 4, 10, 6) };
            DockPanel.SetDock(top, Dock.Top);
            dock.Children.Add(top);
            dock.Children.Add(spark);
            return (dock, spark, value);
        }

        private async Task TickBar()
        {
            if (_busy) return;
            _busy = true;
            try
            {
                HardwareSnapshot s = await Task.Run(() => HardwareMonitor.Instance.Read());
                _sCpu?.Push(s.CpuLoad); if (_vCpu != null) _vCpu.Text = $"{s.CpuLoad:0}%";
                _sGpu?.Push(s.GpuLoad); if (_vGpu != null) _vGpu.Text = $"{s.GpuLoad:0}%";
                _sRam?.Push(s.RamLoad); if (_vRam != null) _vRam.Text = $"{s.RamLoad:0}%";

                var adapters = _net.Read();
                double down = adapters.Sum(a => a.DownBytesPerSec);
                double up = adapters.Sum(a => a.UpBytesPerSec);
                _sNet?.Push(down);
                if (_vNet != null) _vNet.Text = $"↓{ShortRate(down)} ↑{ShortRate(up)}";

                if (_clockTime != null)
                {
                    _clockTime.Text = DateTime.Now.ToString("HH:mm");
                    _clockDate!.Text = DateTime.Now.ToString("ddd, MMM d");
                }
            }
            catch { }
            finally { _busy = false; }
        }

        private void TickClock()
        {
            if (_clockTime == null) return;
            _clockTime.Text = DateTime.Now.ToString("HH:mm:ss");
            _clockDate!.Text = DateTime.Now.ToString("dddd, MMM d");
        }

        private async Task TickHardware()
        {
            if (_busy || _gauge == null) return;
            _busy = true;
            try
            {
                HardwareSnapshot s = await Task.Run(() => HardwareMonitor.Instance.Read());
                switch (_type)
                {
                    case WidgetType.Cpu:
                        _gauge.Value = s.CpuLoad;
                        _sub!.Text = s.CpuTemp.HasValue ? $"{s.CpuTemp:0} °C" : "";
                        break;
                    case WidgetType.Gpu:
                        _gauge.Value = s.GpuLoad;
                        _sub!.Text = s.GpuTemp.HasValue ? $"{s.GpuTemp:0} °C" : "";
                        break;
                    case WidgetType.Ram:
                        _gauge.Value = s.RamLoad;
                        _gauge.ValueText = $"{s.RamLoad:0}%";
                        _sub!.Text = s.RamTotalGb > 0 ? $"{s.RamUsedGb:0.0} / {s.RamTotalGb:0.0} GB" : "";
                        break;
                }
            }
            catch { }
            finally { _busy = false; }
        }

        private void StartTimer(TimeSpan interval, Action tick)
        {
            _timer = new DispatcherTimer { Interval = interval };
            _timer.Tick += (_, _) => tick();
            _timer.Start();
        }

        public WidgetState GetState() => new()
        {
            Type = _type,
            NoteId = _noteId,
            Left = Left, Top = Top, Width = Width, Height = Height,
            Opacity = Opacity, Topmost = _pinTopmost, OnDesktop = _onDesktop,
        };

        // ---------------- header buttons ----------------

        private void Desktop_Click(object sender, RoutedEventArgs e)
        {
            _onDesktop = !_onDesktop;
            ApplyDesktop(_onDesktop);
            UpdateVisuals();
            WidgetManager.SaveAll();
        }

        private void Pin_Click(object sender, RoutedEventArgs e)
        {
            _pinTopmost = !_pinTopmost;
            if (!_onDesktop) Topmost = _pinTopmost;
            UpdateVisuals();
            WidgetManager.SaveAll();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void UpdateVisuals()
        {
            DesktopButton.Opacity = _onDesktop ? 1.0 : 0.4;
            PinButton.Opacity = (!_onDesktop && _pinTopmost) ? 1.0 : 0.4;
            PinButton.IsEnabled = !_onDesktop;
        }

        // ---------------- dragging ----------------

        private void Header_Down(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            GetCursorPos(out _dragStart);
            _startLeft = Left; _startTop = Top;
            _dragging = true;
            ((UIElement)sender).CaptureMouse();
        }

        private void Header_Move(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            GetCursorPos(out POINT p);
            Left = _startLeft + (p.X - _dragStart.X);
            Top = _startTop + (p.Y - _dragStart.Y);
        }

        private void Header_Up(object sender, MouseButtonEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            ((UIElement)sender).ReleaseMouseCapture();
            if (_onDesktop) SendToBottom();
            WidgetManager.SaveAll();
        }

        private void Grip_Down(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState != MouseButtonState.Pressed) return;
            GetCursorPos(out _resizeStart);
            _startWidth = Width;
            _startHeight = Height;
            _resizing = true;
            ((UIElement)sender).CaptureMouse();
            e.Handled = true;
        }

        private void Grip_Move(object sender, MouseEventArgs e)
        {
            if (!_resizing) return;
            GetCursorPos(out POINT p);
            Width = Math.Max(MinWidth, _startWidth + (p.X - _resizeStart.X));
            Height = Math.Max(MinHeight, _startHeight + (p.Y - _resizeStart.Y));
        }

        private void Grip_Up(object sender, MouseButtonEventArgs e)
        {
            if (!_resizing) return;
            _resizing = false;
            ((UIElement)sender).ReleaseMouseCapture();
            if (_onDesktop) SendToBottom();
            WidgetManager.SaveAll();
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            Opacity = Math.Clamp(Opacity + (e.Delta > 0 ? 0.05 : -0.05), 0.3, 1.0);
            e.Handled = true;
        }

        // ---------------- desktop (bottom-most) mode ----------------

        private void ApplyDesktop(bool on)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (on)
            {
                Topmost = false;
                SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
                SendToBottom();
            }
            else
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, ex & ~(WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW));
                Topmost = _pinTopmost;
                SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                Activate();
            }
        }

        private void SendToBottom()
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
                SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (_onDesktop && msg == WM_WINDOWPOSCHANGING)
            {
                var wp = Marshal.PtrToStructure<WINDOWPOS>(lParam);
                wp.hwndInsertAfter = HWND_BOTTOM;
                wp.flags &= ~SWP_NOZORDER;
                wp.flags |= SWP_NOACTIVATE;
                Marshal.StructureToPtr(wp, lParam, false);
            }
            return IntPtr.Zero;
        }

        // ---------------- helpers ----------------

        private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

        /// <summary>Compact transfer rate, e.g. "1.2M" (MB/s) or "34K" (KB/s).</summary>
        private static string ShortRate(double bytesPerSec)
        {
            if (bytesPerSec >= 1024 * 1024) return $"{bytesPerSec / 1024 / 1024:0.0}M";
            if (bytesPerSec >= 1024) return $"{bytesPerSec / 1024:0}K";
            return $"{bytesPerSec:0}B";
        }

        private static TextBlock Subtle() => new()
        {
            Foreground = Brush("TextMutedBrush"),
            FontSize = 12,
        };

        private static FlowDocument LoadDocument(Note note)
        {
            if (!string.IsNullOrEmpty(note.DocumentXaml))
            {
                try { return (FlowDocument)XamlReader.Parse(note.DocumentXaml); }
                catch { }
            }
            var doc = new FlowDocument();
            doc.Blocks.Add(new Paragraph(new Run(note.Body ?? string.Empty)));
            return doc;
        }

        // ---------------- P/Invoke ----------------

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WM_WINDOWPOSCHANGING = 0x0046;
        private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
        private static readonly IntPtr HWND_BOTTOM = new(1);
        private static readonly IntPtr HWND_TOP = new(0);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWPOS
        {
            public IntPtr hwnd;
            public IntPtr hwndInsertAfter;
            public int x, y, cx, cy;
            public uint flags;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorPos(out POINT p);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    }
}
