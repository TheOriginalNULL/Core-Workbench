using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Core_Workbench.Controls
{
    /// <summary>
    /// A 270° circular gauge. Set Value (0–100) and Caption; the arc and colour
    /// update automatically. Colour shifts green → amber → red as Value rises.
    /// </summary>
    public partial class RadialGauge : UserControl
    {
        private const double StartAngle = 135;   // degrees, measured clockwise from 3 o'clock
        private const double SweepRange = 270;   // total span of the gauge

        public RadialGauge()
        {
            InitializeComponent();
            Loaded += (_, _) => Redraw();
            SizeChanged += (_, _) => Redraw();
        }

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(RadialGauge),
                new PropertyMetadata(0.0, OnVisualChanged));

        public static readonly DependencyProperty CaptionProperty =
            DependencyProperty.Register(nameof(Caption), typeof(string), typeof(RadialGauge),
                new PropertyMetadata(string.Empty, OnVisualChanged));

        public static readonly DependencyProperty ValueTextProperty =
            DependencyProperty.Register(nameof(ValueText), typeof(string), typeof(RadialGauge),
                new PropertyMetadata(null, OnVisualChanged));

        public static readonly DependencyProperty OverrideBrushProperty =
            DependencyProperty.Register(nameof(OverrideBrush), typeof(Brush), typeof(RadialGauge),
                new PropertyMetadata(null, OnVisualChanged));

        /// <summary>When set, the arc uses this colour instead of the auto green→red scale.</summary>
        public Brush? OverrideBrush
        {
            get => (Brush?)GetValue(OverrideBrushProperty);
            set => SetValue(OverrideBrushProperty, value);
        }

        /// <summary>Gauge fill, 0–100.</summary>
        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        /// <summary>Small label under the number.</summary>
        public string Caption
        {
            get => (string)GetValue(CaptionProperty);
            set => SetValue(CaptionProperty, value);
        }

        /// <summary>Overrides the centre text. If null, shows "{Value}%".</summary>
        public string? ValueText
        {
            get => (string?)GetValue(ValueTextProperty);
            set => SetValue(ValueTextProperty, value);
        }

        private static void OnVisualChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((RadialGauge)d).Redraw();

        private void Redraw()
        {
            if (Track == null || ActualWidth <= 0) return;

            double pct = Math.Clamp(Value, 0, 100);
            double cx = ActualWidth / 2;
            double cy = ActualHeight / 2;
            double radius = Math.Min(cx, cy) - 8;
            if (radius <= 0) return;

            Track.Data = BuildArc(cx, cy, radius, StartAngle, SweepRange);
            Progress.Data = BuildArc(cx, cy, radius, StartAngle, SweepRange * pct / 100.0);
            Progress.Stroke = OverrideBrush ?? new SolidColorBrush(ColourFor(pct));

            ValueTextRun.Text = ValueText ?? $"{pct:0}%";
            CaptionText.Text = Caption;
        }

        private static Geometry BuildArc(double cx, double cy, double radius,
                                         double startAngle, double sweep)
        {
            if (sweep <= 0.01)
                return Geometry.Empty;

            sweep = Math.Min(sweep, 359.99);
            Point start = PointOnCircle(cx, cy, radius, startAngle);
            Point end = PointOnCircle(cx, cy, radius, startAngle + sweep);

            var fig = new PathFigure { StartPoint = start, IsClosed = false };
            fig.Segments.Add(new ArcSegment
            {
                Point = end,
                Size = new Size(radius, radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = sweep > 180
            });

            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            geo.Freeze();
            return geo;
        }

        private static Point PointOnCircle(double cx, double cy, double r, double angleDeg)
        {
            double rad = angleDeg * Math.PI / 180.0;
            return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
        }

        private static Color ColourFor(double pct)
        {
            // green (low) → amber (mid) → red (high)
            if (pct < 60) return Color.FromRgb(0x6F, 0xE3, 0x9A);
            if (pct < 85) return Color.FromRgb(0xFF, 0xB4, 0x54);
            return Color.FromRgb(0xFF, 0x6B, 0x6B);
        }
    }
}
