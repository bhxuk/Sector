using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Sector.Controls;
using Sector.Core;
using Sector.Shared;

namespace Sector.Pages
{
    /// <summary>Charts of the recorded history: every drive's temperature on one chart by default, or CPU and GPU, fans, drive activity, or your own pick.</summary>
    public class HistoryPage : PageBase
    {
        public override string Title => "History";

        private static string _preset = "Drives";
        private static int _hours = 24;
        private static readonly HashSet<string> Custom = new HashSet<string>();

        private LineChart _chart;
        private WrapPanel _legend;
        private StackPanel _summary;
        private Border _customBox;
        private TextBlock _status;
        private DispatcherTimer _timer;
        private MachineState _machine;
        private List<HistorySensor> _known;
        private int _loadId;

        private static readonly (string id, string label)[] Presets =
        {
            ("Drives", "Drive temperatures"), ("Chips", "CPU and graphics"), ("Fans", "Fans"), ("Activity", "Drive activity"), ("Load", "Load"), ("Custom", "Pick sensors")
        };

        private static readonly (string label, int hours)[] Ranges =
        {
            ("1 hour", 1), ("6 hours", 6), ("24 hours", 24), ("7 days", 168), ("30 days", 720), ("1 year", 8760)
        };

        protected override string Signature(MachineState m) => (m?.Entry.Id ?? "") + "|" + (m?.Snapshot != null) + "|" + Settings.Current.Fahrenheit;

        protected override UIElement Compose(MachineState m)
        {
            _machine = m;
            if (m?.Snapshot == null) return Empty("No readings yet", m?.Error ?? "", Icons.Chart);

            var presetChips = UI.H(6);
            foreach (var (id, label) in Presets)
            {
                var chip = new Chip(label, id == _preset);
                chip.Click += (s, e) =>
                {
                    _preset = id;
                    foreach (var c in presetChips.Children.OfType<Chip>()) c.Selected = c == chip;
                    _customBox.Visibility = _preset == "Custom" ? Visibility.Visible : Visibility.Collapsed;
                    if (_preset == "Custom") _ = FillCustom();
                    _ = Load();
                };
                presetChips.Children.Add(chip);
            }
            var rangeChips = UI.H(6);
            foreach (var (label, hours) in Ranges)
            {
                var chip = new Chip(label, hours == _hours);
                chip.Click += (s, e) =>
                {
                    _hours = hours;
                    foreach (var c in rangeChips.Children.OfType<Chip>()) c.Selected = c == chip;
                    _ = Load();
                };
                rangeChips.Children.Add(chip);
            }
            var controls = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
            presetChips.Margin = new Thickness(0, 0, 24, 8);
            rangeChips.Margin = new Thickness(0, 0, 0, 8);
            controls.Children.Add(presetChips);
            controls.Children.Add(rangeChips);

            _customBox = new Border { Visibility = _preset == "Custom" ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 16) };
            _chart = new LineChart { Height = 380 };
            _legend = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            _status = UI.Text("", 12, Theme.Muted);
            _status.Margin = new Thickness(0, 10, 0, 0);
            _summary = new StackPanel();

            var page = new StackPanel();
            page.Children.Add(controls);
            page.Children.Add(_customBox);
            page.Children.Add(UI.Card(UI.V(0, _chart, _legend, _status), 20));
            page.Children.Add(new Border { Height = 24 });
            page.Children.Add(Section("Over this period", _summary));

            if (_preset == "Custom") _ = FillCustom();
            _ = Load();

            _timer?.Stop();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _timer.Tick += (s, e) => { if (_hours <= 168) _ = Load(); };
            _timer.Start();
            return page;
        }

        public override void Leave() => _timer?.Stop();

        /// <summary>Which sensors each preset charts, and how to name them.</summary>
        private List<(string id, string name)> Pick(MachineState m)
        {
            var snap = m.Snapshot;
            var list = new List<(string, string)>();
            switch (_preset)
            {
                case "Drives":
                    foreach (var d in snap.Drives.Where(d => !string.IsNullOrEmpty(d.TemperatureSensor) && d.Temperature.HasValue))
                        list.Add((d.TemperatureSensor, Settings.Current.DriveName(m.Entry.Id, d)));
                    break;
                case "Chips":
                    if (!string.IsNullOrEmpty(snap.Summary.CpuTempSensor)) list.Add((snap.Summary.CpuTempSensor, "CPU"));
                    if (!string.IsNullOrEmpty(snap.Summary.GpuTempSensor)) list.Add((snap.Summary.GpuTempSensor, "Graphics"));
                    foreach (var s in snap.Hardware.Where(h => h.Type == "SuperIO" || h.Type == "Motherboard").SelectMany(h => h.Sensors)
                                         .Where(s => s.Type == "Temperature" && s.Value > 0 && s.Value < 120).Take(6))
                        list.Add((s.Id, s.Name));
                    break;
                case "Fans":
                    foreach (var h in snap.Hardware)
                        foreach (var s in h.Sensors.Where(s => s.Type == "Fan" && (s.Max ?? 0) > 0))
                            list.Add((s.Id, s.Name));
                    break;
                case "Activity":
                    foreach (var d in snap.Drives)
                    {
                        var hw = snap.Hardware.FirstOrDefault(h => h.Id == d.HardwareId);
                        var a = hw?.Sensors.FirstOrDefault(s => s.Name == "Total Activity");
                        if (a != null) list.Add((a.Id, Settings.Current.DriveName(m.Entry.Id, d)));
                    }
                    break;
                case "Load":
                    foreach (var h in snap.Hardware)
                        foreach (var s in h.Sensors.Where(s => s.Type == "Load" && (s.Name == "CPU Total" || (h.Type.StartsWith("Gpu") && s.Name == "GPU Core") || (h.Type == "Memory" && s.Name == "Memory"))))
                            list.Add((s.Id, h.Type == "Cpu" ? "CPU" : h.Type == "Memory" ? "Memory" : "Graphics"));
                    break;
                case "Custom":
                    foreach (var id in Custom)
                    {
                        var k = _known?.FirstOrDefault(x => x.Id == id);
                        list.Add((id, k != null ? k.Hardware + ": " + k.Name : id));
                    }
                    break;
            }
            return list.Take(24).ToList();
        }

        private async Task Load()
        {
            var m = _machine;
            if (m?.Snapshot == null || _chart == null) return;
            int load = ++_loadId;
            var pick = Pick(m);
            if (pick.Count == 0)
            {
                _chart.Series.Clear();
                _chart.EmptyText = _preset == "Custom" ? "Tick some sensors above to chart them." : "Nothing to chart here on this PC.";
                _chart.Refresh();
                _legend.Children.Clear();
                _summary.Children.Clear();
                _status.Text = "";
                return;
            }
            _status.Text = "Loading...";
            var to = DateTime.UtcNow;
            var from = to.AddHours(-_hours);
            List<HistorySeries> data;
            try { data = await m.Client.History(pick.Select(p => p.id), from, to); }
            catch (Exception ex)
            {
                if (load != _loadId) return;
                _status.Text = "Couldn't load the history: " + ex.Message;
                return;
            }
            if (load != _loadId) return;

            string type = data.FirstOrDefault()?.Type ?? "Temperature";
            bool temps = type == "Temperature";
            _chart.Series.Clear();
            _chart.Lines.Clear();
            _chart.From = new DateTimeOffset(from).ToUnixTimeSeconds();
            _chart.To = new DateTimeOffset(to).ToUnixTimeSeconds();
            _chart.FloorMin = temps || type == "Fan" || type == "Load" || type == "Throughput" ? 0 : (double?)null;
            _chart.Format = v => temps ? v.ToString("0") + "°" : Format.Sensor(type, (float)v);
            _chart.EmptyText = "No history for this period yet. Sector records a point every minute while the agent runs.";

            int i = 0;
            foreach (var (id, name) in pick)
            {
                var s = data.FirstOrDefault(x => x.Id == id);
                if (s == null) { i++; continue; }
                _chart.Series.Add(new ChartSeries
                {
                    Name = name,
                    Stroke = Format.PaletteBrush(i++),
                    StepSeconds = s.StepSeconds,
                    Points = s.Type == "Temperature" ? DrivesPage.Convert(s.Points) : s.Points
                });
            }

            if (_preset == "Drives")
            {
                var a = Settings.Current.Alerts;
                _chart.Lines.Add(new ChartLine { Value = Format.ToUnit(a.HddWarn), Stroke = Theme.Amber, Label = "Hard drive limit " + Format.Temp(a.HddWarn) });
            }
            _chart.Refresh();

            BuildLegend();
            BuildSummary(type);
            int points = _chart.Series.Sum(x => x.Points.Count);
            var step = data.FirstOrDefault()?.StepSeconds ?? 60;
            _status.Text = points == 0 ? "" : $"Each point is the average of {(step >= 3600 ? (step / 3600) + " hour" + (step >= 7200 ? "s" : "") : (step / 60) + " minute" + (step >= 120 ? "s" : ""))}; the shading shows the lowest and highest. Updated {DateTime.Now:HH:mm}.";
        }

        private void BuildLegend()
        {
            _legend.Children.Clear();
            foreach (var s in _chart.Series)
            {
                var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = s.Stroke, VerticalAlignment = VerticalAlignment.Center };
                var label = UI.Text(s.Name, 13, Theme.Text);
                label.TextWrapping = TextWrapping.NoWrap;
                var b = new FlatButton
                {
                    Content = UI.H(8, dot, label),
                    MinHeight = 30,
                    Height = 30,
                    Padding = new Thickness(10, 0, 12, 0),
                    CornerRadius = new CornerRadius(15),
                    Margin = new Thickness(0, 0, 8, 8),
                    Background = Brushes.Transparent,
                    ToolTip = "Show or hide"
                };
                var series = s;
                b.Click += (o, e) =>
                {
                    series.Visible = !series.Visible;
                    b.Opacity = series.Visible ? 1 : 0.4;
                    _chart.Refresh();
                };
                _legend.Children.Add(b);
            }
        }

        private void BuildSummary(string type)
        {
            _summary.Children.Clear();
            var rows = UI.V(0);
            var head = SummaryRow(UI.Label(""), UI.Label("Lowest"), UI.Label("Average"), UI.Label("Highest"), UI.Label("Highest at"));
            rows.Children.Add(head);
            foreach (var s in _chart.Series.Where(x => x.Points.Count > 0))
            {
                var lo = s.Points.Min(p => p.Min);
                var avg = s.Points.Average(p => p.Avg);
                var hiPoint = s.Points.OrderByDescending(p => p.Max).First();
                string F(double v) => type == "Temperature" ? v.ToString("0.0") + Format.TempUnit : Format.Sensor(type, (float)v);
                var dot = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = s.Stroke, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                var name = UI.Text(s.Name, 13, Theme.Text);
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.TextWrapping = TextWrapping.NoWrap;
                rows.Children.Add(SummaryRow(UI.H(0, dot, name), Num(F(lo)), Num(F(avg)), Num(F(hiPoint.Max), true),
                    Num(DateTimeOffset.FromUnixTimeSeconds(hiPoint.T).LocalDateTime.ToString(_hours > 24 ? "ddd d MMM HH:mm" : "HH:mm"))));
            }
            _summary.Children.Add(UI.Card(rows, 18));
        }

        private static TextBlock Num(string text, bool strong = false)
        {
            var t = UI.Text(text, 13, strong ? Theme.Text : Theme.Muted, strong ? FontWeights.SemiBold : FontWeights.Normal);
            t.TextWrapping = TextWrapping.NoWrap;
            return t;
        }

        private static UIElement SummaryRow(params UIElement[] cells)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            for (int i = 1; i < cells.Length; i++) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i == cells.Length - 1 ? 150 : 100) });
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] is TextBlock t && i > 0) t.TextAlignment = TextAlignment.Right;
                Grid.SetColumn(cells[i], i);
                g.Children.Add(cells[i]);
            }
            return g;
        }

        /// <summary>The sensor picker: every sensor that has history on that PC, with a tick box.</summary>
        private async Task FillCustom()
        {
            var m = _machine;
            if (m == null) return;
            try { _known = await m.Client.HistorySensors(); }
            catch (Exception ex)
            {
                _customBox.Child = UI.Card(UI.Text("Couldn't list the sensors: " + ex.Message, 13, Theme.Muted), 18);
                return;
            }
            var wrap = new FlowGrid(280, 3);
            foreach (var group in _known.GroupBy(k => k.Hardware))
            {
                var stack = UI.V(2, UI.Text(group.Key, 13, Theme.Text, FontWeights.SemiBold));
                foreach (var k in group.OrderBy(x => x.Type).ThenBy(x => x.Name))
                {
                    var cb = new CheckBox
                    {
                        Content = UI.Text($"{k.Name}  ({Format.TypeName(k.Type).ToLowerInvariant()})", 12, Theme.Muted),
                        IsChecked = Custom.Contains(k.Id),
                        Margin = new Thickness(0, 2, 0, 2),
                        Foreground = Theme.Text
                    };
                    string id = k.Id;
                    cb.Checked += (s, e) => { Custom.Add(id); _ = Load(); };
                    cb.Unchecked += (s, e) => { Custom.Remove(id); _ = Load(); };
                    stack.Children.Add(cb);
                }
                wrap.Add(UI.Card(stack, 14));
            }
            var hint = UI.Text("Tick up to 24 sensors. Lines of different kinds (say, fans and temperatures) share one scale, so they're clearest charted separately.", 12, Theme.Muted);
            hint.Margin = new Thickness(0, 0, 0, 10);
            var scroller = new ScrollViewer { Content = wrap, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _customBox.Child = UI.V(0, hint, scroller);
            if (_preset == "Custom") _ = Load();
        }
    }
}
