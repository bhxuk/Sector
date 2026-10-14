using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Sector.Controls;
using Sector.Core;
using Sector.Shared;

namespace Sector.Pages
{
    /// <summary>The card for one drive: big temperature, its last 15 minutes, health and space.</summary>
    public static class DriveCard
    {
        public static DriveInfo Find(MachineState m, string key) =>
            m?.Snapshot?.Drives.FirstOrDefault(d => Settings.DriveKey(m.Entry.Id, d) == key);

        public static Border Build(MachineState machine, DriveInfo drive, Action onClick, Action<Action<MachineState>> bind)
        {
            string key = Settings.DriveKey(machine.Entry.Id, drive);

            var name = UI.Text("", 15, Theme.Text, FontWeights.SemiBold);
            name.TextWrapping = TextWrapping.NoWrap;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            var health = PillBox();
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = UI.GridIcon(drive.Media == "HDD" || drive.Media == "Unknown" ? Icons.Drive : Icons.Memory, 18, Theme.Muted);
            icon.Margin = new Thickness(0, 0, 10, 0);
            top.Children.Add(icon);
            Grid.SetColumn(name, 1);
            name.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(name);
            Grid.SetColumn(health, 2);
            health.Margin = new Thickness(8, 0, 0, 0);
            top.Children.Add(health);

            var sub = UI.Text("", 12, Theme.Muted);
            sub.TextWrapping = TextWrapping.NoWrap;
            sub.TextTrimming = TextTrimming.CharacterEllipsis;
            sub.Margin = new Thickness(28, 2, 0, 12);

            var temp = UI.Text("–", 38, Theme.Text, FontWeights.SemiBold, true);
            temp.TextWrapping = TextWrapping.NoWrap;
            var tempNote = UI.Text("", 12, Theme.Muted);
            tempNote.TextWrapping = TextWrapping.NoWrap;
            var spark = new Sparkline(double.NaN, 46) { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(16, 6, 0, 0) };
            var mid = new Grid();
            mid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            mid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var tempStack = UI.V(0, temp, tempNote);
            mid.Children.Add(tempStack);
            Grid.SetColumn(spark, 1);
            mid.Children.Add(spark);

            var meter = new Meter(6) { Margin = new Thickness(0, 14, 0, 6) };
            var space = UI.Text("", 12, Theme.Muted);
            space.TextWrapping = TextWrapping.NoWrap;
            var hours = UI.Text("", 12, Theme.Muted);
            hours.TextWrapping = TextWrapping.NoWrap;
            var foot = new Grid();
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.Children.Add(space);
            Grid.SetColumn(hours, 1);
            foot.Children.Add(hours);

            var body = UI.V(0, top, sub, mid, meter, foot);
            var card = UI.Card(body, 18);
            if (onClick != null)
            {
                card.Cursor = Cursors.Hand;
                card.MouseLeftButtonUp += (s, e) => onClick();
                card.MouseEnter += (s, e) => card.BorderBrush = Theme.GlassBorder;
                card.MouseLeave += (s, e) => card.BorderBrush = Theme.Line;
            }

            bind(m =>
            {
                var d = Find(m, key);
                if (d == null) return;
                var (warn, crit) = Format.DriveLimits(m.Entry.Id, d);
                string custom = Settings.Current.DriveName(m.Entry.Id, d);
                name.Text = custom;
                var bits = new System.Collections.Generic.List<string>();
                if (custom != d.Model) bits.Add(d.Model);
                bits.Add(Format.Gb(d.SizeGB));
                bits.Add(d.Media == "Unknown" ? (string.IsNullOrEmpty(d.Bus) ? "Drive" : d.Bus) : d.Media);
                if (!string.IsNullOrEmpty(d.Letters)) bits.Add(d.Letters);
                sub.Text = string.Join("  ·  ", bits);

                var tone = Format.Level(d.Temperature, warn, crit);
                temp.Text = d.Temperature.HasValue ? Format.Temp(d.Temperature) : "No reading";
                temp.FontSize = d.Temperature.HasValue ? 38 : 20;
                temp.Foreground = d.Temperature.HasValue ? tone : Theme.Muted;
                tempNote.Text = d.TemperatureMax.HasValue
                    ? $"Peak {Format.Temp(d.TemperatureMax)}  ·  limit {Format.Temp(warn)}"
                    : d.Temperature.HasValue ? $"Limit {Format.Temp(warn)}" : "SMART not available";
                spark.Stroke = tone;
                spark.SetValues(m.RecentFor(d.TemperatureSensor));

                SetHealth(health, d);

                if (d.UsedPercent.HasValue)
                {
                    meter.Visibility = Visibility.Visible;
                    meter.SetFraction(d.UsedPercent.Value / 100.0);
                    meter.Fill = d.UsedPercent.Value >= 95 ? Theme.Red : d.UsedPercent.Value >= 85 ? Theme.Amber : Theme.Teal;
                    space.Text = $"{Format.Percent(d.UsedPercent)} used" + (d.FreeGB.HasValue ? $"  ·  {Format.Gb(d.FreeGB.Value * 1.073741824)} free" : "");
                }
                else
                {
                    meter.Visibility = Visibility.Hidden;
                    space.Text = d.Activity.HasValue ? $"{Format.Percent(d.Activity)} busy" : "";
                }
                hours.Text = d.PowerOnHours.HasValue ? "On for " + Format.Hours(d.PowerOnHours) : "";
            });
            return card;
        }

        public static Border PillBox()
        {
            var t = UI.Text("", 12, Theme.Muted, FontWeights.SemiBold);
            t.TextWrapping = TextWrapping.NoWrap;
            return new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(9, 2, 9, 3), VerticalAlignment = VerticalAlignment.Center, Child = t };
        }

        public static void SetHealth(Border pill, DriveInfo d)
        {
            var tone = Format.Health(d.Health);
            var c = ((SolidColorBrush)tone).Color;
            pill.Background = Theme.Brush(Color.FromArgb(0x26, c.R, c.G, c.B));
            var t = (TextBlock)pill.Child;
            t.Text = d.Health == "Good" ? "Healthy" : Format.HealthWords(d.Health);
            t.Foreground = tone;
            pill.ToolTip = d.HealthNotes.Count > 0 ? string.Join("\n", d.HealthNotes) : "No warning signs in its SMART readings.";
        }
    }
}
