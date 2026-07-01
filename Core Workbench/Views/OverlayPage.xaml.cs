using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Core_Workbench.Services;

namespace Core_Workbench.Views
{
    /// <summary>Configures the performance overlay; changes apply live.</summary>
    public partial class OverlayPage : UserControl
    {
        private readonly OverlaySettings _s = OverlaySettings.Current;

        public OverlayPage()
        {
            InitializeComponent();

            EnableCheck.IsChecked = _s.Enabled;
            MCpu.IsChecked = _s.ShowCpu;
            MCpuTemp.IsChecked = _s.ShowCpuTemp;
            MCpuClock.IsChecked = _s.ShowCpuClock;
            MCpuPower.IsChecked = _s.ShowCpuPower;
            MCpuCores.IsChecked = _s.ShowCpuCores;
            MGpu.IsChecked = _s.ShowGpu;
            MGpuTemp.IsChecked = _s.ShowGpuTemp;
            MGpuClock.IsChecked = _s.ShowGpuClock;
            MGpuMemClock.IsChecked = _s.ShowGpuMemClock;
            MGpuVram.IsChecked = _s.ShowGpuVram;
            MGpuPower.IsChecked = _s.ShowGpuPower;
            MGpuFan.IsChecked = _s.ShowGpuFan;
            MRam.IsChecked = _s.ShowRam;
            MNet.IsChecked = _s.ShowNet;
            MClock.IsChecked = _s.ShowClock;
            MFps.IsChecked = _s.ShowFps;
            MFrametime.IsChecked = _s.ShowFrametime;
            MLows.IsChecked = _s.ShowLows;
            ClickThroughCheck.IsChecked = _s.ClickThrough;

            UpdateLabels();
            UpdatePosButtons();
            UpdateColumnButtons();
            BuildColorRows();

            HotkeyEnabledCheck.IsChecked = _s.HotkeyEnabled;
            UpdateHotkeyText();
            PreviewKeyDown += OnPreviewKeyDown;
        }

        // ---- global hotkey ----

        private bool _capturingHotkey;

        private void HotkeyEnabled_Click(object sender, RoutedEventArgs e)
        {
            _s.HotkeyEnabled = HotkeyEnabledCheck.IsChecked == true;
            _s.Save();
            UpdateHotkeyText();
            (Application.Current.MainWindow as MainWindow)?.ApplyOverlayHotkey();
        }

        private void HotkeyChange_Click(object sender, RoutedEventArgs e)
        {
            _capturingHotkey = true;
            HotkeyChangeBtn.Content = "Press keys…";
            Focusable = true;
            Focus();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_capturingHotkey) return;

            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
                    or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System)
                return;   // wait for the non-modifier key

            uint mods = 0;
            var m = Keyboard.Modifiers;
            if (m.HasFlag(ModifierKeys.Alt)) mods |= 1;
            if (m.HasFlag(ModifierKeys.Control)) mods |= 2;
            if (m.HasFlag(ModifierKeys.Shift)) mods |= 4;
            if (m.HasFlag(ModifierKeys.Windows)) mods |= 8;

            _s.HotkeyMods = mods;
            _s.HotkeyVk = (uint)KeyInterop.VirtualKeyFromKey(key);
            _s.Save();

            _capturingHotkey = false;
            HotkeyChangeBtn.Content = "Change";
            UpdateHotkeyText();
            (Application.Current.MainWindow as MainWindow)?.ApplyOverlayHotkey();
            e.Handled = true;
        }

        private void UpdateHotkeyText()
        {
            if (!_s.HotkeyEnabled) { HotkeyText.Text = "Disabled"; return; }
            var parts = new List<string>();
            if ((_s.HotkeyMods & 2) != 0) parts.Add("Ctrl");
            if ((_s.HotkeyMods & 1) != 0) parts.Add("Alt");
            if ((_s.HotkeyMods & 4) != 0) parts.Add("Shift");
            if ((_s.HotkeyMods & 8) != 0) parts.Add("Win");
            parts.Add(KeyInterop.KeyFromVirtualKey((int)_s.HotkeyVk).ToString());
            HotkeyText.Text = string.Join(" + ", parts);
        }

        private void Columns_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is string t && int.TryParse(t, out int cols))
            {
                _s.Columns = cols;
                UpdateColumnButtons();
                OverlayManager.Apply();
            }
        }

        private void SpacingDown_Click(object sender, RoutedEventArgs e) => AdjustSpacing(-1);
        private void SpacingUp_Click(object sender, RoutedEventArgs e) => AdjustSpacing(+1);

        private void AdjustSpacing(double d)
        {
            _s.LineSpacing = Math.Clamp(_s.LineSpacing + d, 0, 14);
            UpdateLabels();
            OverlayManager.Apply();
        }

        private void UpdateColumnButtons()
        {
            Highlight(Col1, _s.Columns == 1);
            Highlight(Col2, _s.Columns == 2);
            Highlight(Col3, _s.Columns == 3);
        }

        // ---- color editor ----

        private static readonly string[] Presets =
        {
            "#FFFFFF", "#FF6B6B", "#FFB454", "#FFD96B", "#5BE3A0",
            "#6FE3C4", "#6FB1FF", "#8B6FE0", "#E07FD0", "#9A8FB8",
        };

        private void BuildColorRows()
        {
            (string key, string label)[] defs =
            {
                ("Cpu", "CPU"), ("Gpu", "GPU"), ("Ram", "RAM"),
                ("Net", "Network"), ("Value", "Values"), ("Back", "Background"),
            };

            foreach (var (key, label) in defs)
                ColorList.Children.Add(BuildColorRow(key, label));
        }

        private FrameworkElement BuildColorRow(string key, string label)
        {
            var grid = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var name = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 0);
            grid.Children.Add(name);

            var hex = new TextBox
            {
                Text = GetColor(key), VerticalContentAlignment = VerticalAlignment.Center,
                Height = 30, Margin = new Thickness(0, 0, 12, 0),
            };

            var swatch = new Border
            {
                Width = 26, Height = 26, CornerRadius = new CornerRadius(6),
                BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1),
                Background = Swatch(GetColor(key)), Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
                ToolTip = "Click to open the color picker",
            };
            // Click the swatch → native color picker; result flows back through the hex box.
            swatch.MouseLeftButtonUp += (_, _) => OpenPicker(hex.Text, picked => hex.Text = picked);
            Grid.SetColumn(swatch, 1);
            grid.Children.Add(swatch);

            Grid.SetColumn(hex, 2);
            grid.Children.Add(hex);

            var presets = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(presets, 3);
            foreach (string p in Presets)
            {
                var b = new Button
                {
                    Width = 20, Height = 20, Margin = new Thickness(0, 0, 4, 0), Cursor = Cursors.Hand,
                    Background = Swatch(p), BorderBrush = (Brush)FindResource("BorderBrush"),
                    BorderThickness = new Thickness(1), ToolTip = p,
                };
                b.Template = SwatchButtonTemplate();
                string preset = p;
                b.Click += (_, _) => hex.Text = preset;   // triggers TextChanged below
                presets.Children.Add(b);
            }
            grid.Children.Add(presets);

            hex.TextChanged += (_, _) =>
            {
                string val = hex.Text.Trim();
                if (TryColor(val, out _))
                {
                    swatch.Background = Swatch(val);
                    SetColor(key, val);
                    OverlayManager.Apply();
                }
            };

            return grid;
        }

        private static ControlTemplate SwatchButtonTemplate()
        {
            var t = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetValue(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            t.VisualTree = border;
            return t;
        }

        /// <summary>Open the custom themed colour picker, seeded with the current colour.</summary>
        private void OpenPicker(string currentHex, Action<string> onPicked)
        {
            string? picked = ColorPickerWindow.Pick(Window.GetWindow(this), currentHex);
            if (picked != null) onPicked(picked);
        }

        private static Brush Swatch(string hex) => TryColor(hex, out var c) ? new SolidColorBrush(c) : Brushes.Transparent;

        private static bool TryColor(string hex, out Color color)
        {
            try { color = (Color)ColorConverter.ConvertFromString(hex); return true; }
            catch { color = Colors.Transparent; return false; }
        }

        private string GetColor(string key) => key switch
        {
            "Cpu" => _s.CpuColor,
            "Gpu" => _s.GpuColor,
            "Ram" => _s.RamColor,
            "Net" => _s.NetColor,
            "Value" => _s.ValueColor,
            "Back" => _s.BackColor,
            _ => "#FFFFFF",
        };

        private void SetColor(string key, string hex)
        {
            switch (key)
            {
                case "Cpu": _s.CpuColor = hex; break;
                case "Gpu": _s.GpuColor = hex; break;
                case "Ram": _s.RamColor = hex; break;
                case "Net": _s.NetColor = hex; break;
                case "Value": _s.ValueColor = hex; break;
                case "Back": _s.BackColor = hex; break;
            }
        }

        private void Enable_Click(object sender, RoutedEventArgs e)
            => OverlayManager.SetEnabled(EnableCheck.IsChecked == true);

        private void Metric_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not CheckBox cb) return;
            bool on = cb.IsChecked == true;
            switch (cb.Tag as string)
            {
                case "Cpu": _s.ShowCpu = on; break;
                case "CpuTemp": _s.ShowCpuTemp = on; break;
                case "CpuClock": _s.ShowCpuClock = on; break;
                case "CpuPower": _s.ShowCpuPower = on; break;
                case "CpuCores": _s.ShowCpuCores = on; break;
                case "Gpu": _s.ShowGpu = on; break;
                case "GpuTemp": _s.ShowGpuTemp = on; break;
                case "GpuClock": _s.ShowGpuClock = on; break;
                case "GpuMemClock": _s.ShowGpuMemClock = on; break;
                case "GpuVram": _s.ShowGpuVram = on; break;
                case "GpuPower": _s.ShowGpuPower = on; break;
                case "GpuFan": _s.ShowGpuFan = on; break;
                case "Ram": _s.ShowRam = on; break;
                case "Net": _s.ShowNet = on; break;
                case "Clock": _s.ShowClock = on; break;
                case "Fps": _s.ShowFps = on; break;
                case "Frametime": _s.ShowFrametime = on; break;
                case "Lows": _s.ShowLows = on; break;
            }
            OverlayManager.Apply();
        }

        private void Pos_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as Button)?.Tag is string tag && Enum.TryParse<OverlayCorner>(tag, out var c))
            {
                _s.Corner = c;
                UpdatePosButtons();
                OverlayManager.Apply();
            }
        }

        private void OpacityDown_Click(object sender, RoutedEventArgs e) => AdjustOpacity(-0.1);
        private void OpacityUp_Click(object sender, RoutedEventArgs e) => AdjustOpacity(+0.1);

        private void AdjustOpacity(double d)
        {
            _s.Opacity = Math.Clamp(Math.Round(_s.Opacity + d, 2), 0.2, 1.0);
            UpdateLabels();
            OverlayManager.Apply();
        }

        private void FontDown_Click(object sender, RoutedEventArgs e) => AdjustFont(-0.1);
        private void FontUp_Click(object sender, RoutedEventArgs e) => AdjustFont(+0.1);

        private void AdjustFont(double d)
        {
            _s.FontScale = Math.Clamp(Math.Round(_s.FontScale + d, 2), 0.6, 2.0);
            UpdateLabels();
            OverlayManager.Apply();
        }

        private void ClickThrough_Click(object sender, RoutedEventArgs e)
        {
            _s.ClickThrough = ClickThroughCheck.IsChecked == true;
            OverlayManager.Apply();
        }

        private void UpdateLabels()
        {
            OpacityText.Text = $"{_s.Opacity * 100:0}%";
            FontText.Text = $"{_s.FontScale * 100:0}%";
            SpacingText.Text = $"{_s.LineSpacing:0} px";
        }

        private void UpdatePosButtons()
        {
            Highlight(PosTL, _s.Corner == OverlayCorner.TopLeft);
            Highlight(PosTR, _s.Corner == OverlayCorner.TopRight);
            Highlight(PosBL, _s.Corner == OverlayCorner.BottomLeft);
            Highlight(PosBR, _s.Corner == OverlayCorner.BottomRight);
        }

        private void Highlight(Button b, bool active)
            => b.Background = (Brush)FindResource(active ? "AccentBrush" : "SurfaceAltBrush");
    }
}
