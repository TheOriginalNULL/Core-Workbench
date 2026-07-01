using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Core_Workbench.Views
{
    /// <summary>
    /// A themed HSV colour picker: saturation/value square + hue slider, with hex
    /// and R/G/B inputs that all stay in sync. Returns a "#RRGGBB" string.
    /// </summary>
    public partial class ColorPickerWindow : Window
    {
        private double _h, _s, _v;   // hue 0–360, sat/val 0–1
        private bool _updating;

        public string SelectedHex { get; private set; } = "#FFFFFF";

        public ColorPickerWindow(string initialHex)
        {
            InitializeComponent();
            Loaded += (_, _) =>
            {
                SetFromColor(Parse(initialHex, Colors.White));
            };
        }

        /// <summary>Show the picker over <paramref name="owner"/>; returns the chosen hex or null.</summary>
        public static string? Pick(Window? owner, string initialHex)
        {
            var w = new ColorPickerWindow(initialHex);
            if (owner != null) w.Owner = owner;
            return w.ShowDialog() == true ? w.SelectedHex : null;
        }

        // ---- saturation / value square ----

        private void Sv_Down(object sender, MouseButtonEventArgs e) { SvCanvas.CaptureMouse(); SvUpdate(e.GetPosition(SvCanvas)); }
        private void Sv_Move(object sender, MouseEventArgs e) { if (SvCanvas.IsMouseCaptured) SvUpdate(e.GetPosition(SvCanvas)); }
        private void Sv_Up(object sender, MouseButtonEventArgs e) => SvCanvas.ReleaseMouseCapture();

        private void SvUpdate(Point p)
        {
            _s = Clamp01(p.X / SvCanvas.Width);
            _v = Clamp01(1 - p.Y / SvCanvas.Height);
            Recompute();
        }

        // ---- hue slider ----

        private void Hue_Down(object sender, MouseButtonEventArgs e) { HueCanvas.CaptureMouse(); HueUpdate(e.GetPosition(HueCanvas)); }
        private void Hue_Move(object sender, MouseEventArgs e) { if (HueCanvas.IsMouseCaptured) HueUpdate(e.GetPosition(HueCanvas)); }
        private void Hue_Up(object sender, MouseButtonEventArgs e) => HueCanvas.ReleaseMouseCapture();

        private void HueUpdate(Point p)
        {
            _h = Clamp01(p.Y / HueCanvas.Height) * 360;
            Recompute();
        }

        // ---- text inputs ----

        private void Hex_Changed(object sender, TextChangedEventArgs e)
        {
            if (_updating) return;
            string t = HexBox.Text.Trim();
            if (TryParse(t, out var c)) SetFromColor(c);
        }

        private void Rgb_Changed(object sender, TextChangedEventArgs e)
        {
            if (_updating) return;
            if (byte.TryParse(RBox.Text, out var r) && byte.TryParse(GBox.Text, out var g) && byte.TryParse(BBox.Text, out var b))
                SetFromColor(Color.FromRgb(r, g, b));
        }

        // ---- core ----

        private void SetFromColor(Color c)
        {
            (_h, _s, _v) = ToHsv(c);
            Recompute();
        }

        private void Recompute()
        {
            _updating = true;

            Color color = FromHsv(_h, _s, _v);
            HueBase.Fill = new SolidColorBrush(FromHsv(_h, 1, 1));
            Preview.Background = new SolidColorBrush(color);
            SelectedHex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

            HexBox.Text = SelectedHex;
            RBox.Text = color.R.ToString();
            GBox.Text = color.G.ToString();
            BBox.Text = color.B.ToString();

            Canvas.SetLeft(SvThumb, _s * SvCanvas.Width - SvThumb.Width / 2);
            Canvas.SetTop(SvThumb, (1 - _v) * SvCanvas.Height - SvThumb.Height / 2);
            Canvas.SetTop(HueThumb, _h / 360 * HueCanvas.Height - HueThumb.Height / 2);

            _updating = false;
        }

        private void Ok_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
        private void TitleBar_Drag(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        // ---- helpers ----

        private static double Clamp01(double x) => Math.Clamp(x, 0, 1);

        private static Color Parse(string hex, Color fallback) => TryParse(hex, out var c) ? c : fallback;

        private static bool TryParse(string hex, out Color color)
        {
            try { color = (Color)ColorConverter.ConvertFromString(hex); color.A = 255; return true; }
            catch { color = Colors.White; return false; }
        }

        private static Color FromHsv(double h, double s, double v)
        {
            h = ((h % 360) + 360) % 360;
            double c = v * s;
            double x = c * (1 - Math.Abs(h / 60 % 2 - 1));
            double m = v - c;
            double r, g, b;
            if (h < 60) (r, g, b) = (c, x, 0);
            else if (h < 120) (r, g, b) = (x, c, 0);
            else if (h < 180) (r, g, b) = (0, c, x);
            else if (h < 240) (r, g, b) = (0, x, c);
            else if (h < 300) (r, g, b) = (x, 0, c);
            else (r, g, b) = (c, 0, x);
            return Color.FromRgb(
                (byte)Math.Round((r + m) * 255),
                (byte)Math.Round((g + m) * 255),
                (byte)Math.Round((b + m) * 255));
        }

        private static (double h, double s, double v) ToHsv(Color col)
        {
            double r = col.R / 255.0, g = col.G / 255.0, b = col.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            double h = 0;
            if (d != 0)
            {
                if (max == r) h = 60 * (((g - b) / d) % 6);
                else if (max == g) h = 60 * (((b - r) / d) + 2);
                else h = 60 * (((r - g) / d) + 4);
            }
            if (h < 0) h += 360;
            return (h, max == 0 ? 0 : d / max, max);
        }
    }
}
