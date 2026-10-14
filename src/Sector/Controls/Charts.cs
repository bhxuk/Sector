using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sector.Core;

namespace Sector.Controls
{
    /// <summary>A small line of recent values, drawn straight to the screen.</summary>
    public class Sparkline : FrameworkElement
    {
        private float[] _values = Array.Empty<float>();
        public Brush Stroke { get; set; } = Theme.Amber;
        public double? FixedMin { get; set; }
        public double? FixedMax { get; set; }

        public Sparkline(double width = 120, double height = 32)
        {
            Width = width;
            Height = height;
            SnapsToDevicePixels = true;
        }

        public void SetValues(IEnumerable<float> values)
        {
            _values = values?.ToArray() ?? Array.Empty<float>();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (_values.Length < 2 || w <= 0 || h <= 0) return;
            double min = FixedMin ?? _values.Min(), max = FixedMax ?? _values.Max();
            if (max - min < 2) { double mid = (max + min) / 2; min = mid - 1; max = mid + 1; }
            double pad = 2;
            var geo = new StreamGeometry();
            var fill = new StreamGeometry();
            using (var g = geo.Open())
            using (var f = fill.Open())
            {
                for (int i = 0; i < _values.Length; i++)
                {
                    double x = i * (w / (_values.Length - 1));
                    double y = pad + (h - 2 * pad) * (1 - (_values[i] - min) / (max - min));
                    if (i == 0) { g.BeginFigure(new Point(x, y), false, false); f.BeginFigure(new Point(0, h), true, true); f.LineTo(new Point(x, y), false, false); }
                    else { g.LineTo(new Point(x, y), true, true); f.LineTo(new Point(x, y), false, false); }
                }
                f.LineTo(new Point(w, h), false, false);
            }
            geo.Freeze();
            fill.Freeze();
            if (Stroke is SolidColorBrush sb)
            {
                var c = sb.Color;
                var grad = new LinearGradientBrush(Color.FromArgb(0x40, c.R, c.G, c.B), Color.FromArgb(0x00, c.R, c.G, c.B), 90);
                grad.Freeze();
                dc.DrawGeometry(grad, null, fill);
            }
            var pen = new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            dc.DrawGeometry(null, pen, geo);
        }
    }

    /// <summary>A thin horizontal bar, 0 to 1.</summary>
    public class Meter : Grid
    {
        private readonly Border _fill;

        public Meter(double height = 6, double width = double.NaN)
        {
            Height = height;
            if (!double.IsNaN(width)) Width = width;
            Children.Add(new Border { CornerRadius = new CornerRadius(height / 2), Background = Theme.Subtle });
            _fill = new Border { CornerRadius = new CornerRadius(height / 2), Background = Theme.Amber, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            Children.Add(_fill);
            SizeChanged += (s, e) => Layout();
        }

        private double _fraction;
        public Brush Fill { set => _fill.Background = value; }

        public void SetFraction(double f)
        {
            _fraction = double.IsNaN(f) ? 0 : Math.Max(0, Math.Min(1, f));
            Layout();
        }

        private void Layout() => _fill.Width = Math.Max(_fraction > 0 ? Height : 0, ActualWidth * _fraction);
    }

    public class ChartSeries
    {
        public string Name { get; set; } = "";
        public Brush Stroke { get; set; } = Theme.Amber;
        public bool Visible { get; set; } = true;
        public int StepSeconds { get; set; } = 60;
        public List<Shared.HistoryPoint> Points { get; set; } = new List<Shared.HistoryPoint>();
    }

    public class ChartLine
    {
        public double Value;
        public Brush Stroke;
        public string Label;
    }

    /// <summary>
    /// A history chart: one or more lines over time, with the min-max range shaded when there's a single line,
    /// threshold lines, and a hover readout that shows every line's value at that moment.
    /// </summary>
    public class LineChart : FrameworkElement
    {
        public List<ChartSeries> Series { get; } = new List<ChartSeries>();
        public List<ChartLine> Lines { get; } = new List<ChartLine>();
        public Func<double, string> Format { get; set; } = v => v.ToString("0", CultureInfo.CurrentCulture);
        public long From { get; set; }
        public long To { get; set; }
        public double? FloorMin { get; set; }
        public string EmptyText { get; set; } = "No history yet. Sector records a point every minute, so check back shortly.";

        private Point? _hover;
        private const double Left = 52, Right = 16, Top = 14, Bottom = 30;

        private static readonly Typeface Face = new Typeface(Theme.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        private static readonly Typeface Bold = new Typeface(Theme.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);

        public LineChart()
        {
            MinHeight = 220;
            Cursor = Cursors.Cross;
            MouseMove += (s, e) => { _hover = e.GetPosition(this); InvalidateVisual(); };
            MouseLeave += (s, e) => { _hover = null; InvalidateVisual(); };
        }

        public void Refresh() => InvalidateVisual();

        private FormattedText Text(string s, double size, Brush b, bool bold = false) =>
            new FormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bold ? Bold : Face, size, b, VisualTreeHelper.GetDpi(this).PixelsPerDip);

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            var visible = Series.Where(s => s.Visible && s.Points.Count > 0).ToList();
            if (w < 100 || h < 80) return;
            if (visible.Count == 0)
            {
                var t = Text(EmptyText, 13, Theme.Muted);
                t.MaxTextWidth = Math.Max(50, w - 40);
                t.TextAlignment = TextAlignment.Center;
                dc.DrawText(t, new Point(20, h / 2 - t.Height / 2));
                return;
            }

            long from = From, to = To;
            if (to <= from) { from = visible.Min(s => s.Points[0].T); to = visible.Max(s => s.Points[^1].T); }
            if (to <= from) to = from + 60;

            bool band = visible.Count == 1;
            double min = visible.Min(s => s.Points.Min(p => band ? p.Min : p.Avg));
            double max = visible.Max(s => s.Points.Max(p => band ? p.Max : p.Avg));
            foreach (var l in Lines)
                if (l.Value <= max * 1.25 + 5 && l.Value >= min - 5) { min = Math.Min(min, l.Value); max = Math.Max(max, l.Value); }
            if (FloorMin.HasValue) min = Math.Max(FloorMin.Value, min);
            double span = max - min;
            if (span < 4) { min -= 2; max += 2; span = max - min; }
            double stepY = NiceStep(span / 4);
            min = Math.Floor(min / stepY) * stepY;
            max = Math.Ceiling(max / stepY) * stepY;
            if (FloorMin.HasValue) min = Math.Max(FloorMin.Value, min);

            var plot = new Rect(Left, Top, Math.Max(10, w - Left - Right), Math.Max(10, h - Top - Bottom));
            double X(long t) => plot.Left + (t - from) / (double)(to - from) * plot.Width;
            double Y(double v) => plot.Bottom - (v - min) / (max - min) * plot.Height;

            var grid = new Pen(Theme.Line, 1);
            grid.Freeze();
            for (double v = min; v <= max + stepY / 2; v += stepY)
            {
                double y = Math.Round(Y(v)) + 0.5;
                dc.DrawLine(grid, new Point(plot.Left, y), new Point(plot.Right, y));
                var lbl = Text(Format(v), 11, Theme.Muted);
                dc.DrawText(lbl, new Point(plot.Left - lbl.Width - 8, y - lbl.Height / 2));
            }

            foreach (var tick in TimeTicks(from, to, plot.Width))
            {
                double x = Math.Round(X(tick.t)) + 0.5;
                dc.DrawLine(grid, new Point(x, plot.Top), new Point(x, plot.Bottom));
                var lbl = Text(tick.label, 11, Theme.Muted);
                double lx = Math.Max(plot.Left, Math.Min(plot.Right - lbl.Width, x - lbl.Width / 2));
                dc.DrawText(lbl, new Point(lx, plot.Bottom + 8));
            }

            foreach (var l in Lines)
            {
                if (l.Value < min || l.Value > max) continue;
                var pen = new Pen(l.Stroke, 1.2) { DashStyle = new DashStyle(new double[] { 4, 4 }, 0) };
                pen.Freeze();
                double y = Y(l.Value);
                dc.DrawLine(pen, new Point(plot.Left, y), new Point(plot.Right, y));
                if (!string.IsNullOrEmpty(l.Label))
                {
                    var lbl = Text(l.Label, 11, l.Stroke);
                    dc.DrawText(lbl, new Point(plot.Right - lbl.Width - 4, y - lbl.Height - 2));
                }
            }

            dc.PushClip(new RectangleGeometry(new Rect(plot.Left - 2, plot.Top - 2, plot.Width + 4, plot.Height + 4)));
            foreach (var s in visible)
            {
                long gap = Math.Max(s.StepSeconds, 60) * 3L;
                if (band && s.Stroke is SolidColorBrush sb)
                {
                    var bandGeo = new StreamGeometry();
                    using (var g = bandGeo.Open())
                    {
                        foreach (var run in Runs(s.Points, gap))
                        {
                            if (run.Count < 2) continue;
                            g.BeginFigure(new Point(X(run[0].T), Y(run[0].Max)), true, true);
                            for (int i = 1; i < run.Count; i++) g.LineTo(new Point(X(run[i].T), Y(run[i].Max)), false, false);
                            for (int i = run.Count - 1; i >= 0; i--) g.LineTo(new Point(X(run[i].T), Y(run[i].Min)), false, false);
                        }
                    }
                    bandGeo.Freeze();
                    var fill = new SolidColorBrush(Color.FromArgb(0x2C, sb.Color.R, sb.Color.G, sb.Color.B));
                    fill.Freeze();
                    dc.DrawGeometry(fill, null, bandGeo);
                }

                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    foreach (var run in Runs(s.Points, gap))
                    {
                        g.BeginFigure(new Point(X(run[0].T), Y(run[0].Avg)), false, false);
                        for (int i = 1; i < run.Count; i++) g.LineTo(new Point(X(run[i].T), Y(run[i].Avg)), true, true);
                        if (run.Count == 1) g.LineTo(new Point(X(run[0].T) + 1, Y(run[0].Avg)), true, true);
                    }
                }
                geo.Freeze();
                var linePen = new Pen(s.Stroke, 1.8) { LineJoin = PenLineJoin.Round };
                linePen.Freeze();
                dc.DrawGeometry(null, linePen, geo);
            }
            dc.Pop();

            if (_hover is Point hp && plot.Contains(hp)) DrawHover(dc, plot, hp, visible, from, to, X, Y, band);
        }

        private void DrawHover(DrawingContext dc, Rect plot, Point hp, List<ChartSeries> visible, long from, long to,
                               Func<long, double> X, Func<double, double> Y, bool band)
        {
            long t = from + (long)((hp.X - plot.Left) / plot.Width * (to - from));
            var rows = new List<(string name, string value, Brush brush, double y)>();
            foreach (var s in visible)
            {
                var p = Nearest(s.Points, t);
                if (p == null || Math.Abs(p.T - t) > Math.Max(s.StepSeconds, 60) * 3L) continue;
                string v = Format(p.Avg);
                if (band && Math.Abs(p.Max - p.Min) >= 1) v += $"  ({Format(p.Min)} to {Format(p.Max)})";
                rows.Add((s.Name, v, s.Stroke, Y(p.Avg)));
            }
            if (rows.Count == 0) return;

            var cross = new Pen(Theme.Muted, 1);
            cross.Freeze();
            dc.DrawLine(cross, new Point(hp.X, plot.Top), new Point(hp.X, plot.Bottom));
            foreach (var r in rows) dc.DrawEllipse(r.brush, new Pen(Theme.Bg, 2), new Point(hp.X, r.y), 4, 4);

            var when = DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime;
            string head = (to - from) > 2 * 86400 ? when.ToString("ddd d MMM, HH:mm") : when.ToString("HH:mm");
            // Highest value first, matching the order of the lines on screen.
            var lines = rows.OrderBy(r => r.y).Take(14).ToList();
            var headText = Text(head, 12, Theme.Muted, true);
            var texts = lines.Select(r => (name: Text(r.name, 12, Theme.Text), value: Text(r.value, 12, Theme.Text, true), r.brush)).ToList();
            double width = Math.Max(headText.Width, texts.Count == 0 ? 0 : texts.Max(x => 18 + x.name.Width + 16 + x.value.Width)) + 24;
            double height = 12 + headText.Height + 6 + texts.Count * 20 + 8;
            double bx = hp.X + 16;
            if (bx + width > plot.Right) bx = hp.X - 16 - width;
            double by = Math.Max(plot.Top, Math.Min(plot.Bottom - height, hp.Y - height / 2));
            dc.DrawRoundedRectangle(Theme.Popup, new Pen(Theme.GlassBorder, 1), new Rect(bx, by, width, height), 10, 10);
            dc.DrawText(headText, new Point(bx + 12, by + 10));
            double ty = by + 12 + headText.Height + 6;
            foreach (var x in texts)
            {
                dc.DrawEllipse(x.brush, null, new Point(bx + 16, ty + 8), 4, 4);
                dc.DrawText(x.name, new Point(bx + 28, ty));
                dc.DrawText(x.value, new Point(bx + width - 12 - x.value.Width, ty));
                ty += 20;
            }
        }

        private static Shared.HistoryPoint Nearest(List<Shared.HistoryPoint> pts, long t)
        {
            if (pts.Count == 0) return null;
            int lo = 0, hi = pts.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (pts[mid].T < t) lo = mid + 1; else hi = mid;
            }
            if (lo > 0 && Math.Abs(pts[lo - 1].T - t) < Math.Abs(pts[lo].T - t)) lo--;
            return pts[lo];
        }

        private static IEnumerable<List<Shared.HistoryPoint>> Runs(List<Shared.HistoryPoint> pts, long gap)
        {
            var run = new List<Shared.HistoryPoint>();
            for (int i = 0; i < pts.Count; i++)
            {
                if (run.Count > 0 && pts[i].T - run[^1].T > gap) { yield return run; run = new List<Shared.HistoryPoint>(); }
                run.Add(pts[i]);
            }
            if (run.Count > 0) yield return run;
        }

        private static double NiceStep(double raw)
        {
            if (raw <= 0) return 1;
            double mag = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double n = raw / mag;
            double nice = n <= 1 ? 1 : n <= 2 ? 2 : n <= 2.5 ? 2.5 : n <= 5 ? 5 : 10;
            return nice * mag;
        }

        private static IEnumerable<(long t, string label)> TimeTicks(long from, long to, double width)
        {
            long range = to - from;
            int count = Math.Max(2, (int)(width / 110));
            long[] steps = { 300, 600, 900, 1800, 3600, 2 * 3600, 3 * 3600, 6 * 3600, 12 * 3600, 86400, 2 * 86400, 7 * 86400, 14 * 86400, 30 * 86400, 61 * 86400 };
            long step = steps.FirstOrDefault(s => range / s <= count);
            if (step == 0) step = steps[^1];
            // Align ticks to local time so "00:00" and "Mon" land where you'd expect.
            var offset = (long)TimeZoneInfo.Local.GetUtcOffset(DateTime.UtcNow).TotalSeconds;
            long first = ((from + offset) / step + 1) * step - offset;
            for (long t = first; t <= to; t += step)
            {
                var local = DateTimeOffset.FromUnixTimeSeconds(t).LocalDateTime;
                string label = step >= 86400 ? local.ToString(step >= 7 * 86400 ? "d MMM" : "ddd d")
                             : local.Hour == 0 && local.Minute == 0 && range > 86400 ? local.ToString("ddd")
                             : local.ToString("HH:mm");
                yield return (t, label);
            }
        }
    }
}
