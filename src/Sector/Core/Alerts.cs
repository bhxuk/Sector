using System;
using System.Collections.Generic;
using System.Linq;
using Sector.Shared;

namespace Sector.Core
{
    /// <summary>
    /// Watches every machine's readings and raises a notification when a drive, CPU or GPU runs hot,
    /// a drive's health gets worse, a fan stops, or a PC drops off the network.
    /// Each problem notifies once, again every so often while it lasts, and logs when it clears.
    /// </summary>
    public static class Alerts
    {
        private class Active
        {
            public string Level;
            public DateTime Notified;
        }

        private static readonly Dictionary<string, Active> State = new Dictionary<string, Active>();
        private static readonly Dictionary<string, float> LastFan = new Dictionary<string, float>();
        private static readonly HashSet<string> OfflineNotified = new HashSet<string>();

        public static event Action Logged;

        public static void Evaluate(MachineState m)
        {
            var a = Settings.Current.Alerts;
            if (!a.Enabled || !m.Entry.Alerts) return;
            string mid = m.Entry.Id;

            if (!m.Online)
            {
                if (a.Offline && m.EverOnline && m.FailingSince != DateTime.MinValue &&
                    DateTime.UtcNow - m.FailingSince > TimeSpan.FromMinutes(Math.Max(1, a.OfflineMinutes)) && OfflineNotified.Add(mid))
                {
                    Raise(m, "Critical", m.Name + " is offline", m.Error);
                }
                return;
            }
            if (OfflineNotified.Remove(mid)) Raise(m, "Resolved", m.Name + " is back online", "Sector can read it again.", notify: true);

            var snap = m.Snapshot;
            if (snap == null) return;

            foreach (var d in snap.Drives)
            {
                var (warn, crit) = Format.DriveLimits(mid, d);
                string name = Settings.Current.DriveName(mid, d);
                Check(m, "drive|" + Settings.DriveKey(mid, d), d.Temperature, warn, crit,
                      $"{name} is running hot", $"{Format.Temp(d.Temperature)} on {m.Name}. Your limit is {Format.Temp(warn)}.");

                if (a.Health) CheckHealth(m, d, name);
            }

            var s = snap.Summary;
            Check(m, "cpu|" + mid, s.CpuTemp, a.CpuWarn, a.CpuCritical, $"The CPU on {m.Name} is running hot", $"{Format.Temp(s.CpuTemp)}. Your limit is {Format.Temp(a.CpuWarn)}.");
            Check(m, "gpu|" + mid, s.GpuTemp, a.GpuWarn, a.GpuCritical, $"The graphics card on {m.Name} is running hot", $"{Format.Temp(s.GpuTemp)}. Your limit is {Format.Temp(a.GpuWarn)}.");

            if (a.FanStopped)
            {
                foreach (var hw in snap.Hardware)
                    foreach (var f in hw.Sensors.Where(x => x.Type == "Fan" && x.Value.HasValue))
                    {
                        string key = mid + "|" + f.Id;
                        if (LastFan.TryGetValue(key, out var before) && before >= 300 && f.Value.Value < 50)
                            Raise(m, "Warning", $"{f.Name} stopped on {m.Name}", $"It was spinning at {Format.Rpm(before)} a moment ago.");
                        LastFan[key] = f.Value.Value;
                    }
            }
        }

        private static void Check(MachineState m, string key, float? value, float warn, float crit, string title, string text)
        {
            if (!value.HasValue) return;
            State.TryGetValue(key, out var act);
            string level = value.Value >= crit ? "Critical" : value.Value >= warn ? "Warning" : null;

            if (level == null)
            {
                // Two degrees of slack so a reading hovering on the line doesn't flap.
                if (act != null && value.Value < warn - 2)
                {
                    State.Remove(key);
                    Raise(m, "Resolved", title.Replace("is running hot", "has cooled down").Replace("are running hot", "have cooled down"),
                          $"Now {Format.Temp(value)}.", notify: false);
                }
                return;
            }

            int repeat = Math.Max(5, Settings.Current.Alerts.RepeatMinutes);
            bool rose = act == null || (act.Level == "Warning" && level == "Critical");
            bool again = act != null && DateTime.UtcNow - act.Notified > TimeSpan.FromMinutes(repeat);
            if (rose || again)
            {
                State[key] = new Active { Level = level, Notified = DateTime.UtcNow };
                Raise(m, level, title, text);
            }
        }

        private static void CheckHealth(MachineState m, DriveInfo d, string name)
        {
            string key = Settings.DriveKey(m.Entry.Id, d);
            string now = $"{d.Health}|{d.Reallocated ?? 0}|{d.Pending ?? 0}|{d.Uncorrectable ?? 0}";
            var seen = Settings.Current.HealthSeen;
            seen.TryGetValue(key, out var before);
            if (before == now) return;
            seen[key] = now;
            Settings.Save();
            if (before == null && d.Health == "Good") return;   // first look at a healthy drive: nothing to say
            if (d.Health == "Unknown") return;

            if (d.Health == "Good")
            {
                Raise(m, "Resolved", $"{name} looks healthy again", "Its SMART readings are back to normal.", notify: false);
                return;
            }
            string level = d.Health == "Bad" ? "Critical" : "Warning";
            Raise(m, level, $"{name} on {m.Name}: {Format.HealthWords(d.Health).ToLowerInvariant()}",
                  d.HealthNotes.Count > 0 ? string.Join(" ", d.HealthNotes) : "Its SMART readings have changed.");
        }

        private static void Raise(MachineState m, string level, string title, string text, bool notify = true)
        {
            Settings.Current.AlertLog.Add(new AlertEvent { Machine = m.Name, Level = level, Title = title, Text = text ?? "" });
            Settings.Save();
            if (notify) App.Instance?.Tray?.Notify(title, text ?? "", () => App.Instance.ShowHub("alerts"));
            Logged?.Invoke();
        }

        /// <summary>What's wrong right now, for the Alerts page.</summary>
        public static int ActiveCount => State.Count + OfflineNotified.Count;
    }
}
