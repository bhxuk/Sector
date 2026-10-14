using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Sector.Core;
using Sector.Shared;

namespace Sector.Pages
{
    /// <summary>Every sensor the agent can read, grouped by hardware, with a search box and filters.</summary>
    public class SensorsPage : PageBase
    {
        public override string Title => "All sensors";

        private static string _filter = "All";
        private static string _search = "";
        private StackPanel _results;
        private readonly Dictionary<string, (TextBlock value, TextBlock min, TextBlock max, string type)> _cells =
            new Dictionary<string, (TextBlock, TextBlock, TextBlock, string)>();
        private MachineState _machine;

        private static readonly (string label, string[] types)[] Filters =
        {
            ("All", null),
            ("Temperatures", new[] { "Temperature" }),
            ("Fans", new[] { "Fan", "Control" }),
            ("Load", new[] { "Load", "Level" }),
            ("Clocks", new[] { "Clock", "Frequency" }),
            ("Power", new[] { "Power", "Current", "Energy" }),
            ("Voltages", new[] { "Voltage" }),
            ("Data", new[] { "Data", "SmallData", "Throughput", "Factor" })
        };

        protected override string Signature(MachineState m)
        {
            if (m?.Snapshot == null) return "none|" + m?.Online;
            return m.Entry.Id + "#" + string.Join("|", m.Snapshot.Hardware.Select(h => h.Id + ":" + h.Sensors.Count)) + "#" + Settings.Current.Fahrenheit;
        }

        protected override UIElement Compose(MachineState m)
        {
            _machine = m;
            if (m?.Snapshot == null) return Empty("No readings yet", m?.Error ?? "", Icons.List);

            var search = UI.TextInput(_search, 260);
            search.ToolTip = "Search sensors";
            search.TextChanged += (s, e) => { _search = search.Text; Fill(); };
            var chips = UI.H(6);
            foreach (var (label, _) in Filters)
            {
                var chip = new Chip(label, label == _filter);
                chip.Click += (s, e) =>
                {
                    _filter = label;
                    foreach (var c in chips.Children.OfType<Chip>()) c.Selected = c == chip;
                    Fill();
                };
                chips.Children.Add(chip);
            }
            var bar = new WrapPanel { Margin = new Thickness(0, 0, 0, 20) };
            search.Margin = new Thickness(0, 0, 16, 8);
            chips.Margin = new Thickness(0, 0, 0, 8);
            bar.Children.Add(search);
            bar.Children.Add(chips);

            _results = new StackPanel();
            Fill();
            Bind(mm =>
            {
                var all = mm.Snapshot?.Hardware.SelectMany(h => h.Sensors).GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
                if (all == null) return;
                foreach (var kv in _cells)
                {
                    if (!all.TryGetValue(kv.Key, out var s)) continue;
                    kv.Value.value.Text = Format.Sensor(kv.Value.type, s.Value);
                    kv.Value.min.Text = Format.Sensor(kv.Value.type, s.Min);
                    kv.Value.max.Text = Format.Sensor(kv.Value.type, s.Max);
                    if (kv.Value.type == "Temperature") kv.Value.value.Foreground = Format.TempColour(s.Value);
                }
            });

            var page = new StackPanel();
            page.Children.Add(bar);
            page.Children.Add(_results);
            return page;
        }

        private void Fill()
        {
            if (_results == null || _machine?.Snapshot == null) return;
            _results.Children.Clear();
            _cells.Clear();
            var types = Filters.FirstOrDefault(f => f.label == _filter).types;
            string q = (_search ?? "").Trim();

            int shown = 0;
            foreach (var hw in _machine.Snapshot.Hardware)
            {
                var sensors = hw.Sensors
                    .Where(s => types == null || types.Contains(s.Type))
                    .Where(s => q.Length == 0 || s.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || hw.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(s => Array.FindIndex(Filters, f => f.types != null && f.types.Contains(s.Type)))
                    .ThenBy(s => s.Type)
                    .ToList();
                if (sensors.Count == 0) continue;

                var stack = UI.V(0);
                var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                AddColumns(head);
                var title = UI.V(1, UI.Text(hw.Name, 15, Theme.Text, FontWeights.SemiBold), UI.Text(Format.HardwareKind(hw.Type), 12, Theme.Muted));
                head.Children.Add(title);
                AddHead(head, "Now", 1);
                AddHead(head, "Min", 2);
                AddHead(head, "Max", 3);
                stack.Children.Add(head);

                string lastType = null;
                foreach (var s in sensors)
                {
                    if (s.Type != lastType)
                    {
                        var group = UI.Label(Format.TypeName(s.Type));
                        group.Margin = new Thickness(0, 10, 0, 2);
                        stack.Children.Add(group);
                        lastType = s.Type;
                    }
                    var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                    AddColumns(g);
                    var name = UI.Text(s.Name, 13, Theme.Text);
                    name.TextTrimming = TextTrimming.CharacterEllipsis;
                    name.TextWrapping = TextWrapping.NoWrap;
                    name.ToolTip = s.Id;
                    g.Children.Add(name);
                    var v = Num(g, 1, true);
                    var mn = Num(g, 2, false);
                    var mx = Num(g, 3, false);
                    stack.Children.Add(g);
                    _cells[s.Id] = (v, mn, mx, s.Type);
                    v.Text = Format.Sensor(s.Type, s.Value);
                    mn.Text = Format.Sensor(s.Type, s.Min);
                    mx.Text = Format.Sensor(s.Type, s.Max);
                    if (s.Type == "Temperature") v.Foreground = Format.TempColour(s.Value);
                    shown++;
                }
                var card = UI.Card(stack, 18);
                card.Margin = new Thickness(0, 0, 0, 14);
                _results.Children.Add(card);
            }
            if (shown == 0)
                _results.Children.Add(Empty("Nothing matches", "Try another search or filter.", Icons.Search));
        }

        private static void AddColumns(Grid g)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        }

        private static void AddHead(Grid g, string text, int col)
        {
            var t = UI.Label(text);
            t.TextAlignment = TextAlignment.Right;
            t.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetColumn(t, col);
            g.Children.Add(t);
        }

        private static TextBlock Num(Grid g, int col, bool strong)
        {
            var t = UI.Text("", 13, strong ? Theme.Text : Theme.Muted, strong ? FontWeights.SemiBold : FontWeights.Normal);
            t.TextAlignment = TextAlignment.Right;
            t.TextWrapping = TextWrapping.NoWrap;
            Grid.SetColumn(t, col);
            g.Children.Add(t);
            return t;
        }
    }
}
