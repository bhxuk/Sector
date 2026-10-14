using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Sector.Core;

namespace Sector.Pages
{
    /// <summary>What needs attention now on any PC, and everything Sector has warned about.</summary>
    public class AlertsPage : PageBase
    {
        public override string Title => "Alerts";
        public override bool MachineSpecific => false;

        public override string Subtitle(MachineState m) =>
            Settings.Current.Alerts.Enabled ? "Sector warns you by the clock when something runs hot, a drive's health changes or a PC goes offline." : "Alerts are off. Turn them on in Settings.";

        protected override string Signature(MachineState m) =>
            Settings.Current.AlertLog.Count + "|" + string.Join(",", Problems().Select(p => p.title)) + "|" + Settings.Current.Fahrenheit;

        private static System.Collections.Generic.List<(string level, string title, string text)> Problems()
        {
            var list = new System.Collections.Generic.List<(string, string, string)>();
            var a = Settings.Current.Alerts;
            foreach (var m in Fleet.Machines)
            {
                if (!m.Online)
                {
                    if (m.EverOnline) list.Add(("Critical", m.Name + " is offline", m.Error));
                    continue;
                }
                if (m.Snapshot == null) continue;
                foreach (var d in m.Snapshot.Drives)
                {
                    var (w, c) = Format.DriveLimits(m.Entry.Id, d);
                    string name = Settings.Current.DriveName(m.Entry.Id, d);
                    if (d.Temperature >= w)
                        list.Add((d.Temperature >= c ? "Critical" : "Warning", $"{name} on {m.Name} is at {Format.Temp(d.Temperature)}", $"Limit {Format.Temp(w)}."));
                    if (d.Health == "Caution" || d.Health == "Bad")
                        list.Add((d.Health == "Bad" ? "Critical" : "Warning", $"{name} on {m.Name}: {Format.HealthWords(d.Health).ToLowerInvariant()}", string.Join(" ", d.HealthNotes)));
                }
                var s = m.Snapshot.Summary;
                if (s.CpuTemp >= a.CpuWarn) list.Add((s.CpuTemp >= a.CpuCritical ? "Critical" : "Warning", $"CPU on {m.Name} is at {Format.Temp(s.CpuTemp)}", ""));
                if (s.GpuTemp >= a.GpuWarn) list.Add((s.GpuTemp >= a.GpuCritical ? "Critical" : "Warning", $"Graphics on {m.Name} is at {Format.Temp(s.GpuTemp)}", ""));
            }
            return list;
        }

        protected override UIElement Compose(MachineState m)
        {
            var page = new StackPanel();

            var now = Problems();
            if (now.Count == 0)
            {
                var ok = UI.H(14, UI.GridIcon(Icons.Check, 22, Theme.Green), UI.V(2,
                    UI.Text("Everything looks fine", 16, Theme.Text, FontWeights.SemiBold),
                    UI.Text("No drive, CPU or graphics card is over its limit, and every drive is healthy.", 13, Theme.Muted)));
                page.Children.Add(Section("Right now", UI.Card(ok, 18)));
            }
            else
            {
                var stack = UI.V(10);
                foreach (var (level, title, text) in now) stack.Children.Add(Item(level, title, text, null));
                page.Children.Add(Section("Right now", UI.Card(stack, 18)));
            }

            var log = Settings.Current.AlertLog.AsEnumerable().Reverse().Take(150).ToList();
            var clear = FlatButton.Secondary("Clear", () =>
            {
                Settings.Current.AlertLog.Clear();
                Settings.Save();
                Update(Fleet.Selected);
            });
            clear.MinHeight = 34;
            if (log.Count == 0)
            {
                page.Children.Add(Section("Earlier", UI.Card(UI.Text("Nothing yet. When Sector warns you about something, it's listed here too.", 13, Theme.Muted), 18)));
            }
            else
            {
                var stack = UI.V(12);
                foreach (var e in log) stack.Children.Add(Item(e.Level, e.Title, e.Text, e.TimeUtc));
                page.Children.Add(Section("Earlier", UI.Card(stack, 18), clear));
            }
            return page;
        }

        private static UIElement Item(string level, string title, string text, DateTime? when)
        {
            var tone = level switch { "Critical" => Theme.Red, "Warning" => Theme.Amber, "Resolved" => Theme.Green, _ => Theme.Teal };
            string icon = level switch { "Resolved" => Icons.Check, "Info" => Icons.Bell, _ => Icons.Warning };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var i = UI.GridIcon(icon, 18, tone);
            i.Margin = new Thickness(0, 1, 12, 0);
            i.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(i);
            var body = UI.V(2, UI.Text(title, 14, Theme.Text, FontWeights.SemiBold));
            if (!string.IsNullOrWhiteSpace(text)) body.Children.Add(UI.Text(text, 12, Theme.Muted));
            Grid.SetColumn(body, 1);
            g.Children.Add(body);
            if (when.HasValue)
            {
                var t = UI.Text(when.Value.ToLocalTime().ToString(when.Value.ToLocalTime().Date == DateTime.Today ? "HH:mm" : "d MMM HH:mm"), 12, Theme.Muted);
                t.TextWrapping = TextWrapping.NoWrap;
                t.Margin = new Thickness(12, 2, 0, 0);
                Grid.SetColumn(t, 2);
                g.Children.Add(t);
            }
            return g;
        }
    }
}
