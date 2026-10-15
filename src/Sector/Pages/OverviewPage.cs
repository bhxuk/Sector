using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Sector.Controls;
using Sector.Core;
using Sector.Shared;

namespace Sector.Pages
{
    /// <summary>The whole machine at a glance: CPU, graphics, memory, every drive, all temperatures and fans.</summary>
    public class OverviewPage : PageBase
    {
        public override string Title => "Overview";

        protected override string Signature(MachineState m)
        {
            if (m?.Snapshot == null) return "none|" + m?.Online;
            var s = m.Snapshot;
            return string.Join("|", s.Drives.Select(d => Settings.DriveKey(m.Entry.Id, d))) + "#" +
                   string.Join("|", s.Hardware.SelectMany(h => h.Sensors.Where(x => x.Type == "Temperature" || x.Type == "Fan").Select(x => x.Id))) +
                   "#" + s.Summary.GpuName + s.Summary.GpuTemp.HasValue + "#" + Settings.Current.Fahrenheit;
        }

        protected override UIElement Compose(MachineState m)
        {
            if (m?.Snapshot == null)
                return Empty(m?.Online == false ? "No readings yet" : "Connecting...", m?.Error ?? "", Icons.Gauge);

            var snap = m.Snapshot;
            var page = new StackPanel();

            // ---- Headline tiles ----
            // Integrated graphics often report no temperature at all; only show the tile when there's something to show.
            bool gpuTile = !string.IsNullOrEmpty(snap.Summary.GpuName) && snap.Summary.GpuTemp.HasValue;
            var tiles = new FlowGrid(210, gpuTile ? 4 : 3);
            tiles.Add(Tile(Icons.Cpu, "Processor", snap.Summary.CpuName, s => s.Summary.CpuTemp, s => s.Summary.CpuTempSensor,
                s => new[]
                {
                    ("Load", Format.Percent(s.Summary.CpuLoad)),
                    ("Clock", Format.Mhz(s.Summary.CpuClock)),
                    ("Power", Format.Watts(s.Summary.CpuPower))
                }, Settings.Current.Alerts.CpuWarn, Settings.Current.Alerts.CpuCritical));
            if (gpuTile)
            {
                tiles.Add(Tile(Icons.Gpu, "Graphics", snap.Summary.GpuName, s => s.Summary.GpuTemp, s => s.Summary.GpuTempSensor,
                    s => new[]
                    {
                        ("Load", Format.Percent(s.Summary.GpuLoad)),
                        ("Memory", s.Summary.GpuMemoryUsed.HasValue && s.Summary.GpuMemoryTotal.HasValue
                            ? $"{s.Summary.GpuMemoryUsed / 1024:0.0} / {s.Summary.GpuMemoryTotal / 1024:0} GB" : "–"),
                        ("Power", Format.Watts(s.Summary.GpuPower))
                    }, Settings.Current.Alerts.GpuWarn, Settings.Current.Alerts.GpuCritical));
            }
            tiles.Add(MemoryTile());
            tiles.Add(DrivesTile(m));
            page.Children.Add(Section("At a glance", tiles));

            // ---- Drives ----
            if (snap.Drives.Count > 0)
            {
                var grid = new FlowGrid(290, 4);
                foreach (var d in snap.Drives)
                {
                    string key = Settings.DriveKey(m.Entry.Id, d);
                    grid.Add(DriveCard.Build(m, d, () => { DrivesPage.Focus = key; Host.Navigate("drives"); }, Bind));
                }
                var all = FlatButton.Secondary("Drive details", () => Host.Navigate("drives"));
                all.MinHeight = 34;
                page.Children.Add(Section("Drives", grid, all));
            }

            // ---- Temperatures and fans ----
            var cols = new FlowGrid(340, 2);
            cols.Add(SensorList(snap, "Temperature", "Temperatures", "No temperature sensors found.", true));
            cols.Add(SensorList(snap, "Fan", "Fans",
                snap.PawnIoInstalled
                    ? "No fan speeds reported. Some motherboards don't share them, and case fans plugged into a hub or the PSU can't be read."
                    : "Fan speeds need the PawnIO driver (see the note at the top).", false));
            page.Children.Add(cols);
            return page;
        }

        private UIElement Tile(string icon, string label, string name, Func<Snapshot, float?> temp, Func<Snapshot, string> sensor,
                               Func<Snapshot, (string, string)[]> rows, float warn, float crit)
        {
            var big = UI.Text("–", 34, Theme.Text, FontWeights.SemiBold, true);
            var spark = new Sparkline(double.NaN, 34) { Margin = new Thickness(0, 8, 0, 10) };
            var rowStack = UI.V(0);
            var valueBlocks = new List<TextBlock>();
            foreach (var (l, _) in rows(new Snapshot()))
            {
                var (row, v) = Row(l);
                rowStack.Children.Add(row);
                valueBlocks.Add(v);
            }
            var nameText = UI.Text(name, 12, Theme.Muted);
            nameText.TextWrapping = TextWrapping.NoWrap;
            nameText.TextTrimming = TextTrimming.CharacterEllipsis;
            nameText.ToolTip = name;
            var body = UI.V(2, UI.H(8, UI.GridIcon(icon, 16, Theme.Muted), UI.Label(label)), nameText, big, spark, rowStack);
            Bind(m =>
            {
                var s = m.Snapshot;
                if (s == null) return;
                var t = temp(s);
                big.Text = t.HasValue ? Format.Temp(t) : "–";
                big.Foreground = Format.Level(t, warn, crit);
                spark.Stroke = big.Foreground;
                spark.SetValues(m.RecentFor(sensor(s)));
                var vals = rows(s);
                for (int i = 0; i < vals.Length && i < valueBlocks.Count; i++) valueBlocks[i].Text = vals[i].Item2;
            });
            return UI.Card(body, 18);
        }

        private UIElement MemoryTile()
        {
            var big = UI.Text("–", 34, Theme.Text, FontWeights.SemiBold, true);
            var meter = new Meter(8) { Margin = new Thickness(0, 14, 0, 14) };
            var (r1, used) = Row("Used");
            var (r2, free) = Row("Available");
            var (r3, up) = Row("Up for");
            var body = UI.V(2, UI.H(8, UI.GridIcon(Icons.Memory, 16, Theme.Muted), UI.Label("Memory")), UI.Text("RAM", 12, Theme.Muted), big, meter, r1, r2, r3);
            Bind(m =>
            {
                var s = m.Snapshot?.Summary;
                if (s == null) return;
                big.Text = Format.Percent(s.MemoryLoad);
                meter.SetFraction((s.MemoryLoad ?? 0) / 100.0);
                meter.Fill = s.MemoryLoad >= 90 ? Theme.Red : s.MemoryLoad >= 75 ? Theme.Amber : Theme.Teal;
                used.Text = Format.GiB(s.MemoryUsed);
                free.Text = Format.GiB(s.MemoryAvailable);
                up.Text = Format.Uptime(s.UptimeSeconds);
            });
            return UI.Card(body, 18);
        }

        private UIElement DrivesTile(MachineState machine)
        {
            var big = UI.Text("–", 34, Theme.Text, FontWeights.SemiBold, true);
            var which = UI.Text("", 12, Theme.Muted);
            which.TextWrapping = TextWrapping.NoWrap;
            which.TextTrimming = TextTrimming.CharacterEllipsis;
            var (r1, count) = Row("Drives");
            var (r2, health) = Row("Health");
            var (r3, range) = Row("Range");
            var body = UI.V(2, UI.H(8, UI.GridIcon(Icons.Drive, 16, Theme.Muted), UI.Label("Hottest drive")), which, big,
                new Border { Height = 18 }, r1, r2, r3);
            Bind(m =>
            {
                var s = m.Snapshot;
                if (s == null) return;
                var hot = m.HottestDrive;
                if (hot != null)
                {
                    var (w, c) = Format.DriveLimits(m.Entry.Id, hot);
                    big.Text = Format.Temp(hot.Temperature);
                    big.Foreground = Format.Level(hot.Temperature, w, c);
                    which.Text = Settings.Current.DriveName(m.Entry.Id, hot);
                }
                else { big.Text = "–"; which.Text = "No drive temperatures"; }
                count.Text = s.Drives.Count.ToString();
                int bad = s.Drives.Count(d => d.Health == "Bad"), caution = s.Drives.Count(d => d.Health == "Caution");
                health.Text = bad > 0 ? $"{bad} failing" : caution > 0 ? $"{caution} to watch" : "All good";
                health.Foreground = bad > 0 ? Theme.Red : caution > 0 ? Theme.Amber : Theme.Green;
                var temps = s.Drives.Where(d => d.Temperature.HasValue).Select(d => d.Temperature.Value).ToList();
                range.Text = temps.Count > 1 ? $"{Format.Temp(temps.Min(), false)} to {Format.Temp(temps.Max())}" : temps.Count == 1 ? Format.Temp(temps[0]) : "–";
            });
            return UI.Card(body, 18);
        }

        /// <summary>Every sensor of one kind, grouped under its hardware. Drive temperatures are on the drive cards instead.</summary>
        private UIElement SensorList(Snapshot snap, string type, string title, string empty, bool skipDrives)
        {
            var groups = snap.Hardware
                .Where(h => !(skipDrives && h.Type == "Storage"))
                .Select(h => (h, sensors: h.Sensors.Where(x => x.Type == type && x.Value.HasValue && !(type == "Fan" && x.Value == 0 && x.Max == 0)).ToList()))
                .Where(g => g.sensors.Count > 0)
                .ToList();

            var stack = UI.V(0);
            if (groups.Count == 0)
            {
                stack.Children.Add(UI.Text(empty, 13, Theme.Muted));
            }
            foreach (var (h, sensors) in groups)
            {
                var head = UI.Text(h.Name, 12, Theme.Muted, FontWeights.SemiBold);
                head.Margin = new Thickness(0, stack.Children.Count == 0 ? 0 : 14, 0, 4);
                stack.Children.Add(head);
                foreach (var s in sensors)
                {
                    var (row, value) = Row(s.Name, "", 13);
                    stack.Children.Add(row);
                    string id = s.Id;
                    // Fans get a little bar of their duty (or speed vs. their own max) under the name.
                    Meter bar = null;
                    if (type == "Fan")
                    {
                        bar = new Meter(3) { Margin = new Thickness(0, 0, 0, 4) };
                        stack.Children.Add(bar);
                    }
                    Bind(m =>
                    {
                        var r = m.Snapshot?.Hardware.SelectMany(x => x.Sensors).FirstOrDefault(x => x.Id == id);
                        if (r == null) return;
                        value.Text = Format.Sensor(type, r.Value);
                        if (type == "Temperature") value.Foreground = Format.TempColour(r.Value);
                        if (bar != null)
                        {
                            float max = Math.Max(r.Max ?? 0, 1500);
                            bar.SetFraction((r.Value ?? 0) / max);
                            bar.Fill = (r.Value ?? 0) < 50 ? Theme.Off : Theme.Teal;
                        }
                    });
                }
            }
            return Section(title, UI.Card(stack, 20));
        }
    }
}
