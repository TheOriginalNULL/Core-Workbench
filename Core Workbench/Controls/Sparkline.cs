using System.Windows;
using System.Windows.Media;

namespace Core_Workbench.Controls
{
    /// <summary>
    /// A minimal rolling line graph. Call <see cref="Push"/> with each new value
    /// (0–<see cref="Max"/>); the newest sample sits at the right edge and older
    /// ones scroll left. Lightweight — draws a single polyline in OnRender.
    /// </summary>
    public sealed class Sparkline : FrameworkElement
    {
        private readonly Queue<double> _data = new();

        public int Capacity { get; set; } = 60;
        public double Max { get; set; } = 100;
        public Brush LineBrush { get; set; } = Brushes.White;
        public double LineThickness { get; set; } = 1.5;

        /// <summary>Scale the graph to its own recent peak (for unbounded values like network speed).</summary>
        public bool AutoScale { get; set; }

        public void Push(double value)
        {
            _data.Enqueue(value);
            while (_data.Count > Capacity) _data.Dequeue();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0 || _data.Count < 2) return;

            var pen = new Pen(LineBrush, LineThickness)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            pen.Freeze();

            double[] pts = _data.ToArray();
            int n = pts.Length;
            double step = w / (Capacity - 1);
            double pad = LineThickness;

            double max = Max;
            if (AutoScale)
            {
                max = 1;
                foreach (double d in pts) if (d > max) max = d;
            }

            Point P(int i)
            {
                double v = Math.Clamp(pts[i], 0, max);
                double y = h - (v / max) * (h - pad * 2) - pad;
                double x = w - (n - 1 - i) * step;   // newest at the right edge
                return new Point(x, y);
            }

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(P(0), false, false);
                for (int i = 1; i < n; i++) ctx.LineTo(P(i), true, false);
            }
            geo.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            InvalidateVisual();
        }
    }
}
