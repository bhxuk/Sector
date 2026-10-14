using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Sector.Core;
using Sector.Shared;

namespace Sector.Pages
{
    public class SettingsPage : PageBase
    {
        public override string Title => "Settings";
        public override bool MachineSpecific => false;
        public override string Subtitle(MachineState m) => "Sector " + Updates.CurrentText;

        protected override string Signature(MachineState m) => "settings|" + Settings.Current.Fahrenheit + "|" + Fleet.Machines.Count;

        private static Settings S => Settings.Current;
        private static AlertSettings A => Settings.Current.Alerts;

        protected override UIElement Compose(MachineState m)
        {
            var page = new StackPanel();

            // ---- Look ----
            var restartNote = UI.Text("", 12, Theme.Amber);
            void NeedsRestart() => restartNote.Text = "Restart Sector to see this change.";
            var restart = FlatButton.Secondary("Restart Sector", () => App.Instance.Restart());
            restart.MinHeight = 34;
            var look = UI.V(0,
                UI.SettingRow("Theme", null, UI.Combo(new[] { "Dark", "Light", "System" }, S.Theme, v => { S.Theme = v; Settings.Save(); NeedsRestart(); })),
                UI.SettingRow("Accent colour", null, UI.Combo(Theme.AccentNames, S.Accent, v => { S.Accent = v; Settings.Save(); NeedsRestart(); })),
                UI.SettingRow("Glass window", "The frosted background on Windows 11.", Switch(S.GlassWindow, v => { S.GlassWindow = v; NeedsRestart(); })),
                UI.SettingRow("Temperatures in", null, UI.Combo(new[] { "Celsius", "Fahrenheit" }, S.Fahrenheit ? "Fahrenheit" : "Celsius", v =>
                {
                    S.Fahrenheit = v == "Fahrenheit";
                    Settings.Save();
                    Update(Fleet.Selected);
                })),
                UI.H(12, restart, restartNote));
            restartNote.VerticalAlignment = VerticalAlignment.Center;
            page.Children.Add(Section("Look", UI.Card(look, 20)));

            // ---- Behaviour ----
            var trayMachines = new[] { "Automatic" }.Concat(Fleet.Machines.Select(x => x.Name)).ToArray();
            string trayNow = Fleet.Machines.FirstOrDefault(x => x.Entry.Id == S.TrayMachine)?.Name ?? "Automatic";
            var behaviour = UI.V(0,
                UI.SettingRow("Start with Windows", "Sector starts quietly by the clock so alerts keep working.", Switch(S.StartWithWindows, v => { S.StartWithWindows = v; AutoStart.Apply(v); })),
                UI.SettingRow("Refresh every", "How often the window updates. The agent always reads every 2 seconds.",
                    UI.Combo(new[] { "1 second", "2 seconds", "5 seconds", "10 seconds" }, S.RefreshSeconds + (S.RefreshSeconds == 1 ? " second" : " seconds"),
                        v => { S.RefreshSeconds = int.Parse(v.Split(' ')[0]); Settings.Save(); })),
                UI.SettingRow("Icon by the clock shows", "A live temperature in the notification area.",
                    UI.Combo(new[] { "Hottest drive", "CPU", "GPU", "Nothing" }, S.TrayShows, v => { S.TrayShows = v; Settings.Save(); App.Instance.Tray.Refresh(); })),
                UI.SettingRow("For", "Automatic picks the PC with the most drives.",
                    UI.Combo(trayMachines, trayNow, v =>
                    {
                        S.TrayMachine = Fleet.Machines.FirstOrDefault(x => x.Name == v)?.Entry.Id ?? "";
                        Settings.Save();
                        App.Instance.Tray.Refresh();
                    })));
            page.Children.Add(Section("Behaviour", UI.Card(behaviour, 20)));

            // ---- Alerts ----
            var alerts = UI.V(0,
                UI.SettingRow("Alerts", "Notifications by the clock. Everything is also listed under Alerts.", Switch(A.Enabled, v => A.Enabled = v)),
                UI.Divider(),
                UI.SettingRow("Hard drives: warn at", "Most hard drives are happiest below 45°C (113°F). Long stretches above 50°C wear them faster.", TempPair(() => A.HddWarn, v => A.HddWarn = v, () => A.HddCritical, v => A.HddCritical = v)),
                UI.SettingRow("SSDs: warn at", "SSDs and NVMe drives run warmer and start slowing down at around 70 to 80°C.", TempPair(() => A.SsdWarn, v => A.SsdWarn = v, () => A.SsdCritical, v => A.SsdCritical = v)),
                UI.SettingRow("CPU: warn at", null, TempPair(() => A.CpuWarn, v => A.CpuWarn = v, () => A.CpuCritical, v => A.CpuCritical = v)),
                UI.SettingRow("Graphics: warn at", null, TempPair(() => A.GpuWarn, v => A.GpuWarn = v, () => A.GpuCritical, v => A.GpuCritical = v)),
                UI.Text("Each drive can have its own limit too: open it under Drives.", 12, Theme.Muted),
                UI.Divider(),
                UI.SettingRow("Drive health changes", "When a drive starts reporting bad or unstable sectors, or Windows flags it.", Switch(A.Health, v => A.Health = v)),
                UI.SettingRow("A PC goes offline", "When Sector can't reach a PC for a few minutes.", Switch(A.Offline, v => A.Offline = v)),
                UI.SettingRow("A fan stops", "When a fan that was spinning drops to zero.", Switch(A.FanStopped, v => A.FanStopped = v)),
                UI.SettingRow("Remind me every", "While a problem lasts.",
                    UI.Combo(new[] { "15 minutes", "30 minutes", "1 hour", "4 hours" }, A.RepeatMinutes switch { 15 => "15 minutes", 60 => "1 hour", 240 => "4 hours", _ => "30 minutes" },
                        v => { A.RepeatMinutes = v switch { "15 minutes" => 15, "1 hour" => 60, "4 hours" => 240, _ => 30 }; Settings.Save(); })),
                UI.H(12, FlatButton.Secondary("Send a test alert", () => App.Instance.Tray.Notify("Sector test alert", "This is how alerts look. Click to open Sector.", () => App.Instance.ShowHub("alerts")))));
            page.Children.Add(Section("Alerts", UI.Card(alerts, 20)));

            // ---- History on this PC ----
            var local = Fleet.Machines.FirstOrDefault(x => x.Entry.IsLocal);
            int days = local?.Info?.HistoryDays ?? AgentConfig.Load().HistoryDays;
            var keep = UI.Combo(new[] { "30 days", "90 days", "1 year", "2 years", "5 years" },
                days switch { 30 => "30 days", 90 => "90 days", 730 => "2 years", 1825 => "5 years", _ => "1 year" },
                async v =>
                {
                    int d = v switch { "30 days" => 30, "90 days" => 90, "2 years" => 730, "5 years" => 1825, _ => 365 };
                    try { if (local != null) await local.Client.ChangeSettings(new AgentSettingsChange { HistoryDays = d }); } catch { }
                });
            var history = UI.V(0,
                UI.SettingRow("Keep history for", "On this PC. Minute-by-minute detail is kept for 7 days, then every 15 minutes. A year is only a few hundred MB.", keep),
                UI.SettingRow("Where it's kept", Constants.DataFolder, FlatButton.Secondary("Open folder", () => Shell.Open(Constants.DataFolder))));
            page.Children.Add(Section("History on this PC", UI.Card(history, 20)));

            // ---- Updates and about ----
            var lu = AppUpdater.StatusOf(local);
            var updateNote = UI.Text(lu == null ? "" : lu.Available ? $"Sector {lu.Latest} is ready." : lu.CheckedUtc > DateTime.MinValue ? "Up to date. Last checked " + Format.Ago(lu.CheckedUtc) + "." : "", 12, Theme.Muted);
            updateNote.VerticalAlignment = VerticalAlignment.Center;
            FlatButton updateNow = null;
            updateNow = FlatButton.Primary("Update now", async () =>
            {
                updateNow.IsEnabled = false;
                string err = await AppUpdater.Install(local);
                updateNote.Text = string.IsNullOrEmpty(err) ? "Updating. Sector will close and reopen by itself." : err;
                if (!string.IsNullOrEmpty(err)) updateNow.IsEnabled = true;
            });
            updateNow.MinHeight = 36;
            updateNow.Visibility = lu?.Available == true ? Visibility.Visible : Visibility.Collapsed;
            var check = FlatButton.Secondary("Check now", async () =>
            {
                updateNote.Text = "Checking...";
                if (local != null && AppUpdater.StatusOf(local) != null)
                {
                    var r = await AppUpdater.CheckNow(local);
                    updateNote.Text = r == null ? "Couldn't reach the Sector agent." : r.Message;
                    updateNow.Visibility = r?.Available == true ? Visibility.Visible : Visibility.Collapsed;
                }
                else if (await Updates.Check())
                {
                    updateNote.Text = "Sector " + Updates.LatestVersion + " is available.";
                    await App.Instance.InstallUpdate(t => updateNote.Text = t);
                }
                else updateNote.Text = "You have the latest version.";
            });
            check.MinHeight = 36;
            var auto = new ToggleSwitch(lu?.Auto == true, "Install updates automatically");
            auto.IsEnabled = lu != null;
            auto.Toggled += async on =>
            {
                try { await local.Client.SetAutoUpdate(on); }
                catch { auto.IsOn = !on; }
            };
            var about = UI.V(0,
                UI.SettingRow("Install updates automatically", "On this PC. Sector checks every few hours, installs new versions quietly and reopens itself. No permission prompts. Your other PCs have their own switch under Machines.", auto),
                UI.H(12, check, updateNow, updateNote),
                UI.Divider(),
                UI.SettingRow("Sector " + Updates.CurrentText, "Agent " + (local?.Info?.AgentVersion ?? "not running") +
                    (local?.Info != null ? (local.Info.PawnIoInstalled ? "  ·  PawnIO " + local.Info.PawnIoVersion : "  ·  PawnIO not installed") : ""),
                    UI.H(8, FlatButton.Secondary("Logs", () => Shell.Open(Settings.Folder)), FlatButton.Secondary("GitHub", () => Shell.Open("https://github.com/" + Updates.Repo)))),
                UI.Text("Sensor readings come from LibreHardwareMonitor (MPL 2.0). CPU and fan access uses the PawnIO driver.", 12, Theme.Muted));
            page.Children.Add(Section("Updates and about", UI.Card(about, 20)));
            return page;
        }

        private static ToggleSwitch Switch(bool on, Action<bool> changed)
        {
            var t = new ToggleSwitch(on);
            t.Toggled += v => { changed(v); Settings.Save(); };
            return t;
        }

        /// <summary>Two temperature boxes: warn and critical. Shown and typed in your chosen unit, stored in °C.</summary>
        private static UIElement TempPair(Func<float> getWarn, Action<float> setWarn, Func<float> getCrit, Action<float> setCrit)
        {
            TextBox Box(Func<float> get, Action<float> set)
            {
                var b = UI.TextInput(Format.ToUnit(get()).ToString("0"), 64);
                b.HorizontalContentAlignment = HorizontalAlignment.Center;
                b.LostFocus += (s, e) =>
                {
                    if (double.TryParse(b.Text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out double v) && v > 0 && v < 400)
                    {
                        set((float)(Format.F ? (v - 32) * 5 / 9 : v));
                        Settings.Save();
                    }
                    b.Text = Format.ToUnit(get()).ToString("0");
                };
                return b;
            }
            var unit = UI.Text(Format.TempUnit, 13, Theme.Muted);
            unit.VerticalAlignment = VerticalAlignment.Center;
            var crit = UI.Text("critical at", 13, Theme.Muted);
            crit.VerticalAlignment = VerticalAlignment.Center;
            var unit2 = UI.Text(Format.TempUnit, 13, Theme.Muted);
            unit2.VerticalAlignment = VerticalAlignment.Center;
            return UI.H(8, Box(getWarn, setWarn), unit, new Border { Width = 10 }, crit, Box(getCrit, setCrit), unit2);
        }
    }
}
