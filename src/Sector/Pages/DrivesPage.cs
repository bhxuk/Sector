using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sector.Controls;
using Sector.Core;
using Sector.Shared;

namespace Sector.Pages
{
    /// <summary>Every drive in a table, and everything about the one you pick: SMART, health, lifetime and its temperature history.</summary>
    public class DrivesPage : PageBase
    {
        /// <summary>The drive to show in detail, by machine + serial. Set before navigating here.</summary>
        public static string Focus { get; set; }

        public override string Title => "Drives";

        private LineChart _chart;
        private string _chartKey;
        private int _chartHours = 24;

        protected override string Signature(MachineState m)
        {
            if (m?.Snapshot == null) return "none|" + m?.Online;
            var keys = m.Snapshot.Drives.Select(d => Settings.DriveKey(m.Entry.Id, d)).ToList();
            if (Focus == null || !keys.Contains(Focus)) Focus = keys.FirstOrDefault();
            return string.Join("|", keys) + "#" + Focus + "#" + Settings.Current.Fahrenheit + "#" +
                   string.Join("|", m.Snapshot.Drives.Select(d => d.Smart.Count));
        }

        protected override UIElement Compose(MachineState m)
        {
            if (m?.Snapshot == null) return Empty("No readings yet", m?.Error ?? "", Icons.Drive);
            if (m.Snapshot.Drives.Count == 0) return Empty("No drives found", "The agent couldn't see any physical drives on this PC.", Icons.Drive);

            var page = new StackPanel();
            page.Children.Add(Section("All drives", Table(m)));
            var d = DriveCard.Find(m, Focus);
            if (d != null) page.Children.Add(Detail(m, d));
            return page;
        }

        // ---------- The table ----------

        private static readonly (string head, double width)[] Columns =
        {
            ("Drive", -1), ("Temp", 80), ("Health", 140), ("Size", 80), ("Used", 70), ("Powered on", 100), ("Bad sectors", 96)
        };

        private UIElement Table(MachineState m)
        {
            var stack = UI.V(0);
            stack.Children.Add(TableRow(Columns.Select(c => (UIElement)UI.Label(c.head)).ToArray(), null, false));
            foreach (var drive in m.Snapshot.Drives)
            {
                string key = Settings.DriveKey(m.Entry.Id, drive);
                var name = UI.Text("", 14, Theme.Text, FontWeights.SemiBold);
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.TextWrapping = TextWrapping.NoWrap;
                var model = UI.Text("", 12, Theme.Muted);
                model.TextTrimming = TextTrimming.CharacterEllipsis;
                model.TextWrapping = TextWrapping.NoWrap;
                var temp = Cell(15, FontWeights.SemiBold);
                var health = DriveCard.PillBox();
                health.HorizontalAlignment = HorizontalAlignment.Left;
                var size = Cell();
                var used = Cell();
                var hours = Cell();
                var bad = Cell();
                var row = TableRow(new UIElement[] { UI.V(1, name, model), temp, health, size, used, hours, bad }, () =>
                {
                    Focus = key;
                    Invalidate();
                    Update(Fleet.Selected);
                }, key == Focus);

                Bind(mm =>
                {
                    var d = DriveCard.Find(mm, key);
                    if (d == null) return;
                    var (w, c) = Format.DriveLimits(mm.Entry.Id, d);
                    name.Text = Settings.Current.DriveName(mm.Entry.Id, d);
                    model.Text = (name.Text != d.Model ? d.Model + "  ·  " : "") + (d.Media == "Unknown" ? d.Bus : d.Media) + (string.IsNullOrEmpty(d.Letters) ? "" : "  ·  " + d.Letters);
                    temp.Text = Format.Temp(d.Temperature);
                    temp.Foreground = Format.Level(d.Temperature, w, c);
                    DriveCard.SetHealth(health, d);
                    size.Text = Format.Gb(d.SizeGB);
                    used.Text = Format.Percent(d.UsedPercent);
                    hours.Text = Format.Hours(d.PowerOnHours);
                    long sectors = (d.Reallocated ?? 0) + (d.Pending ?? 0) + (d.Uncorrectable ?? 0);
                    bad.Text = d.Reallocated.HasValue || d.Pending.HasValue || d.Uncorrectable.HasValue ? sectors.ToString("#,0") : "–";
                    bad.Foreground = sectors > 0 ? Theme.Amber : Theme.Text;
                });
                stack.Children.Add(row);
            }
            return UI.Card(stack, 10);
        }

        private static TextBlock Cell(double size = 13, FontWeight? weight = null)
        {
            var t = UI.Text("", size, Theme.Text, weight ?? FontWeights.Normal);
            t.TextWrapping = TextWrapping.NoWrap;
            t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        private static UIElement TableRow(UIElement[] cells, Action onClick, bool selected)
        {
            var g = new Grid();
            for (int i = 0; i < Columns.Length; i++)
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = Columns[i].width < 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(Columns[i].width) });
            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i] is FrameworkElement fe)
                {
                    fe.VerticalAlignment = VerticalAlignment.Center;
                    fe.Margin = new Thickness(0, 0, 12, 0);
                }
                Grid.SetColumn(cells[i], i);
                g.Children.Add(cells[i]);
            }
            var b = new Border
            {
                Padding = new Thickness(12, onClick == null ? 6 : 10, 12, onClick == null ? 6 : 10),
                CornerRadius = new CornerRadius(12),
                Background = selected ? Theme.AmberTint : Brushes.Transparent,
                Child = g
            };
            if (onClick != null)
            {
                b.Cursor = Cursors.Hand;
                b.MouseEnter += (s, e) => { if (!selected) b.Background = Theme.Subtle; };
                b.MouseLeave += (s, e) => { if (!selected) b.Background = Brushes.Transparent; };
                b.MouseLeftButtonUp += (s, e) => onClick();
            }
            return b;
        }

        // ---------- One drive in detail ----------

        private UIElement Detail(MachineState m, DriveInfo drive)
        {
            string key = Settings.DriveKey(m.Entry.Id, drive);
            var page = new StackPanel();

            // Health notes
            var healthTitle = UI.Text("", 16, Theme.Text, FontWeights.SemiBold);
            var healthPill = DriveCard.PillBox();
            var notes = UI.V(4);
            var healthCard = UI.Card(UI.V(10, UI.H(12, healthPill, healthTitle), notes), 18);
            Bind(mm =>
            {
                var d = DriveCard.Find(mm, key);
                if (d == null) return;
                DriveCard.SetHealth(healthPill, d);
                healthTitle.Text = d.Health switch
                {
                    "Good" => "No warning signs",
                    "Caution" => "Some warning signs: keep an eye on this drive and make sure it's backed up",
                    "Bad" => "This drive may be failing. Copy anything important off it now",
                    _ => "Health unknown"
                };
                notes.Children.Clear();
                foreach (var n in d.HealthNotes) notes.Children.Add(UI.Text("•  " + n, 13, Theme.Muted));
                if (d.HealthNotes.Count == 0 && d.Health == "Good")
                    notes.Children.Add(UI.Text("No remapped, pending or unreadable sectors, and Windows reports it as healthy.", 13, Theme.Muted));
            });

            // Three columns of facts
            var cols = new FlowGrid(260, 3);
            cols.Add(Facts("About", m, key, new (string, Func<DriveInfo, string>)[]
            {
                ("Model", d => d.Model),
                ("Serial", d => string.IsNullOrEmpty(d.Serial) ? "–" : d.Serial),
                ("Firmware", d => string.IsNullOrEmpty(d.Firmware) ? "–" : d.Firmware),
                ("Capacity", d => Format.Gb(d.SizeGB)),
                ("Type", d => d.Media),
                ("Connection", d => string.IsNullOrEmpty(d.Bus) ? "–" : d.Bus),
                ("Letters", d => string.IsNullOrEmpty(d.Letters) ? "None" : d.Letters),
                ("Windows says", d => d.WindowsHealth),
                ("Disk number", d => d.Number.ToString())
            }));
            cols.Add(Facts("Right now", m, key, new (string, Func<DriveInfo, string>)[]
            {
                ("Temperature", d => Format.Temp(d.Temperature)),
                ("Lowest since start", d => Format.Temp(d.TemperatureMin)),
                ("Highest since start", d => Format.Temp(d.TemperatureMax)),
                ("Reading", d => Format.Rate(d.ReadRate)),
                ("Writing", d => Format.Rate(d.WriteRate)),
                ("Busy", d => Format.Percent(d.Activity)),
                ("Used", d => Format.Percent(d.UsedPercent)),
                ("Free", d => d.FreeGB.HasValue ? Format.Gb(d.FreeGB.Value * 1.073741824) : "–"),
                ("Drive's own limit", d => d.WarningTemperature.HasValue ? Format.Temp(d.WarningTemperature) : "–")
            }));
            cols.Add(Facts("Over its life", m, key, new (string, Func<DriveInfo, string>)[]
            {
                ("Powered on", d => d.PowerOnHours.HasValue ? $"{Format.Hours(d.PowerOnHours)} ({d.PowerOnHours:#,0} h)" : "–"),
                ("Times switched on", d => d.PowerOnCount.HasValue ? d.PowerOnCount.Value.ToString("#,0") : "–"),
                ("Data read", d => d.DataReadGB.HasValue ? Format.Gb(d.DataReadGB.Value * 1.073741824) : "–"),
                ("Data written", d => d.DataWrittenGB.HasValue ? Format.Gb(d.DataWrittenGB.Value * 1.073741824) : "–"),
                ("Life left", d => Format.Percent(d.Life)),
                ("Reallocated sectors", d => Count(d.Reallocated)),
                ("Pending sectors", d => Count(d.Pending)),
                ("Unreadable sectors", d => Count(d.Uncorrectable)),
                ("Cable errors", d => Count(d.CrcErrors))
            }));

            // Your settings for this drive
            var nameBox = UI.TextInput(Settings.Current.DriveNames.TryGetValue(key, out var nm) ? nm : "", 240);
            nameBox.ToolTip = "For example \"Bay 2\" or \"Films\". Leave empty to use the model name.";
            nameBox.LostFocus += (s, e) =>
            {
                string v = nameBox.Text.Trim();
                if (string.IsNullOrEmpty(v)) Settings.Current.DriveNames.Remove(key); else Settings.Current.DriveNames[key] = v;
                Settings.Save();
                Fleet.RaiseListChanged();
            };
            var (defWarn, _) = Format.DriveLimits(m.Entry.Id, drive);
            var warnBox = UI.TextInput(Settings.Current.Alerts.DriveWarn.TryGetValue(key, out var cw) ? Format.ToUnit(cw).ToString("0") : "", 90);
            warnBox.ToolTip = "Leave empty to use the limit for all " + (drive.Media == "HDD" ? "hard drives" : "SSDs") + " (Settings).";
            warnBox.LostFocus += (s, e) =>
            {
                if (double.TryParse(warnBox.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out double v) && v > 0)
                    Settings.Current.Alerts.DriveWarn[key] = (float)(Format.F ? (v - 32) * 5 / 9 : v);
                else
                {
                    Settings.Current.Alerts.DriveWarn.Remove(key);
                    warnBox.Text = "";
                }
                Settings.Save();
                if (_chartKey != null) _ = LoadChart(m, key);
            };
            var mine = UI.Card(UI.V(0,
                UI.SettingRow("Name", "Your own name for this drive, shown everywhere in Sector.", nameBox),
                UI.Divider(),
                UI.SettingRow("Warn me at (" + Format.TempUnit + ")", $"Alert when it reaches this. Default: {Format.Temp(defWarn)}.", warnBox)), 20);

            // Temperature history
            _chart = new LineChart { Height = 260, FloorMin = 0 };
            _chartKey = key;
            var ranges = UI.H(6);
            foreach (var (label, hours) in new[] { ("24 hours", 24), ("7 days", 168), ("30 days", 720), ("1 year", 8760) })
            {
                var chip = new Chip(label, hours == _chartHours);
                chip.Click += (s, e) =>
                {
                    _chartHours = hours;
                    foreach (var c in ranges.Children.OfType<Chip>()) c.Selected = c == chip;
                    _ = LoadChart(m, key);
                };
                ranges.Children.Add(chip);
            }
            _ = LoadChart(m, key);

            page.Children.Add(Section(Settings.Current.DriveName(m.Entry.Id, drive), healthCard));
            page.Children.Add(Section("Temperature", UI.Card(_chart, 18), ranges));
            page.Children.Add(new Border { Child = cols, Margin = new Thickness(0, 0, 0, 28) });
            page.Children.Add(Section("Your settings for this drive", mine));
            if (drive.Smart.Count > 0) page.Children.Add(Section("SMART attributes", SmartTable(m, key)));
            return page;
        }

        private static string Count(long? v) => v.HasValue ? v.Value.ToString("#,0") : "–";

        private UIElement Facts(string title, MachineState m, string key, (string label, Func<DriveInfo, string> value)[] rows)
        {
            var stack = UI.V(0, UI.Label(title));
            ((FrameworkElement)stack.Children[0]).Margin = new Thickness(0, 0, 0, 8);
            var blocks = new List<(TextBlock, Func<DriveInfo, string>)>();
            foreach (var (label, value) in rows)
            {
                var (row, v) = Row(label);
                stack.Children.Add(row);
                blocks.Add((v, value));
            }
            Bind(mm =>
            {
                var d = DriveCard.Find(mm, key);
                if (d == null) return;
                foreach (var (b, f) in blocks)
                {
                    string text = f(d) ?? "–";
                    b.Text = text;
                    b.ToolTip = text.Length > 26 ? text : null;
                }
            });
            return UI.Card(stack, 18);
        }

        private async Task LoadChart(MachineState m, string key)
        {
            var chart = _chart;
            var d = DriveCard.Find(m, key);
            if (chart == null || d == null || string.IsNullOrEmpty(d.TemperatureSensor)) return;
            var to = DateTime.UtcNow;
            var from = to.AddHours(-_chartHours);
            try
            {
                var series = await m.Client.History(new[] { d.TemperatureSensor }, from, to);
                if (chart != _chart) return;
                chart.Series.Clear();
                chart.Lines.Clear();
                var (w, c) = Format.DriveLimits(m.Entry.Id, d);
                foreach (var s in series)
                {
                    chart.Series.Add(new ChartSeries
                    {
                        Name = Settings.Current.DriveName(m.Entry.Id, d),
                        Stroke = Theme.Amber,
                        StepSeconds = s.StepSeconds,
                        Points = Convert(s.Points)
                    });
                }
                chart.Lines.Add(new ChartLine { Value = Format.ToUnit(w), Stroke = Theme.Amber, Label = "Warn " + Format.Temp(w) });
                chart.Lines.Add(new ChartLine { Value = Format.ToUnit(c), Stroke = Theme.Red, Label = "Critical " + Format.Temp(c) });
                chart.Format = v => v.ToString("0") + "°";
                chart.From = new DateTimeOffset(from).ToUnixTimeSeconds();
                chart.To = new DateTimeOffset(to).ToUnixTimeSeconds();
                chart.Refresh();
            }
            catch (Exception ex)
            {
                chart.Series.Clear();
                chart.EmptyText = "Couldn't load the history: " + ex.Message;
                chart.Refresh();
            }
        }

        /// <summary>History arrives in °C; convert for the chart if you use °F.</summary>
        public static List<HistoryPoint> Convert(List<HistoryPoint> pts) =>
            !Format.F ? pts : pts.Select(p => new HistoryPoint { T = p.T, Min = (float)Format.ToUnit(p.Min), Avg = (float)Format.ToUnit(p.Avg), Max = (float)Format.ToUnit(p.Max) }).ToList();

        private UIElement SmartTable(MachineState m, string key)
        {
            var stack = UI.V(0);
            var head = SmartRow(UI.Label("ID"), UI.Label("Attribute"), UI.Label("Raw value"), UI.Label("Threshold"));
            stack.Children.Add(head);
            var d0 = DriveCard.Find(m, key);
            var cells = new List<(int id, TextBlock raw)>();
            bool nvme = d0.Media == "NVMe";
            foreach (var r in d0.Smart)
            {
                var raw = UI.Text("", 13, Theme.Text, FontWeights.SemiBold);
                var name = UI.Text(r.Name, 13, Theme.Text);
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.TextWrapping = TextWrapping.NoWrap;
                bool watched = !nvme && (r.Id == 5 || r.Id == 197 || r.Id == 198);
                if (watched) name.FontWeight = FontWeights.SemiBold;
                stack.Children.Add(SmartRow(UI.Text(nvme ? r.Id.ToString() : r.Id.ToString("X2"), 13, Theme.Muted), name, raw,
                    UI.Text(r.Threshold > 0 ? r.Threshold.ToString() : "–", 13, Theme.Muted)));
                cells.Add((r.Id, raw));
            }
            Bind(mm =>
            {
                var d = DriveCard.Find(mm, key);
                if (d == null) return;
                foreach (var (id, raw) in cells)
                {
                    var r = d.Smart.FirstOrDefault(x => x.Id == id);
                    if (r == null) continue;
                    raw.Text = r.Raw.ToString("#,0");
                    raw.Foreground = !nvme && (id == 5 || id == 197 || id == 198) && r.Raw > 0 ? Theme.Amber : Theme.Text;
                }
            });
            return UI.Card(stack, 18);
        }

        private static UIElement SmartRow(UIElement id, UIElement name, UIElement raw, UIElement threshold)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            var all = new[] { id, name, raw, threshold };
            for (int i = 0; i < 4; i++)
            {
                Grid.SetColumn(all[i], i);
                if (all[i] is TextBlock t && i >= 2) t.TextAlignment = TextAlignment.Right;
                g.Children.Add(all[i]);
            }
            return g;
        }
    }
}
