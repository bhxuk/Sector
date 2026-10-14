using System;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Sector.Core;
using Sector.Shared;

namespace Sector.Pages
{
    /// <summary>This PC's sharing settings and pairing key, the other PCs Sector watches, and adding a new one.</summary>
    public class MachinesPage : PageBase
    {
        public override string Title => "Machines";
        public override bool MachineSpecific => false;
        public override string Subtitle(MachineState m) => "Watch your NAS (or any Windows PC) from here. Install Sector on it, then add it below.";

        protected override string Signature(MachineState m)
        {
            var local = Fleet.Machines.FirstOrDefault(x => x.Entry.IsLocal);
            return Fleet.Machines.Count + "|" + string.Join(",", Fleet.Machines.Select(x => x.Entry.Id)) + "|" + (local?.Online ?? false) + "|" + (local?.Info != null);
        }

        protected override UIElement Compose(MachineState selected)
        {
            var page = new StackPanel();
            page.Children.Add(Section("This PC", ThisPc()));

            var others = Fleet.Machines.Where(x => !x.Entry.IsLocal).ToList();
            if (others.Count > 0)
            {
                var stack = UI.V(0);
                foreach (var m in others)
                {
                    if (stack.Children.Count > 0) stack.Children.Add(UI.Divider());
                    stack.Children.Add(Remote(m));
                }
                page.Children.Add(Section("Your other PCs", UI.Card(stack, 20)));
            }
            page.Children.Add(Section("Add a PC or NAS", AddForm()));
            return page;
        }

        private UIElement ThisPc()
        {
            var local = Fleet.Machines.First(x => x.Entry.IsLocal);
            var status = UI.Text("", 13, Theme.Text, FontWeights.SemiBold);
            var start = FlatButton.Secondary("Start the agent", async () =>
            {
                if (await LocalAgent.InstallAndStart())
                {
                    await Task.Delay(3000);
                    await Fleet.PollOnce(local);
                }
            });
            start.MinHeight = 36;
            Bind(_ =>
            {
                string s = LocalAgent.Status();
                bool ok = local.Online;
                status.Text = ok ? "Running" : s;
                status.Foreground = ok ? Theme.Green : Theme.Red;
                start.Visibility = ok ? Visibility.Collapsed : Visibility.Visible;
            });

            var share = new ToggleSwitch(local.Info?.AllowRemote == true, "Let my other PCs see this PC");
            var shareNote = UI.Text("", 12, Theme.Muted);
            share.IsEnabled = local.Online;
            share.Toggled += async on =>
            {
                try
                {
                    await local.Client.ChangeSettings(new AgentSettingsChange { AllowRemote = on });
                    local.Info = await local.Client.Info();
                    shareNote.Text = on ? "Shared. Your other PCs can add this one with the key below." : "Not shared.";
                }
                catch (Exception ex)
                {
                    share.IsOn = !on;
                    shareNote.Text = "Couldn't change it: " + ex.Message;
                }
            };

            var cfg = AgentConfig.Load();
            var key = UI.Text(string.IsNullOrEmpty(cfg.Key) ? "Not made yet: start the agent" : cfg.Key, 18, Theme.Text, FontWeights.SemiBold, true);
            key.FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas");
            var copy = FlatButton.Secondary("Copy", () => { try { Clipboard.SetText(key.Text); } catch { } });
            copy.MinHeight = 34;
            var renew = FlatButton.Secondary("New key", async () =>
            {
                if (MessageBox.Show("Make a new key? Other PCs that use the old one will need the new key.", "Sector", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
                try
                {
                    var r = await local.Client.ChangeSettings(new AgentSettingsChange { NewKey = true });
                    key.Text = r.GetProperty("key").GetString();
                    local.Client.ForgetLocalConfig();
                }
                catch (Exception ex) { MessageBox.Show("Couldn't make a new key: " + ex.Message, "Sector"); }
            });
            renew.MinHeight = 34;

            var addresses = UI.V(2);
            foreach (var (addr, label) in Addresses())
                addresses.Children.Add(UI.H(8, UI.Text(addr, 13, Theme.Text, FontWeights.SemiBold), UI.Text(label, 12, Theme.Muted)));
            if (addresses.Children.Count == 0) addresses.Children.Add(UI.Text("No network addresses found.", 13, Theme.Muted));

            var body = UI.V(0,
                UI.SettingRow("Sector agent", "Reads this PC's sensors in the background and keeps their history, even when this window is closed.", UI.H(12, status, start)),
                UI.Divider(),
                UI.SettingRow("Let my other PCs see this PC", "Turn this on for your NAS. Only your local network and Tailscale can connect, and only with the key.", share),
                shareNote,
                UI.Divider(),
                UI.SettingRow("Pairing key", "Type this into Sector on your other PC when you add this one.", UI.H(8, copy, renew)),
                key,
                new Border { Height = 14 },
                UI.Label("This PC's addresses"),
                new Border { Height = 6 },
                addresses,
                new Border { Height = 4 },
                UI.Text($"Port {cfg.Port}. On Tailscale you can also use this PC's machine name ({Environment.MachineName.ToLowerInvariant()}).", 12, Theme.Muted));
            return UI.Card(body, 22);
        }

        private static System.Collections.Generic.IEnumerable<(string addr, string label)> Addresses()
        {
            NetworkInterface[] nics;
            try { nics = NetworkInterface.GetAllNetworkInterfaces(); } catch { yield break; }
            foreach (var nic in nics.Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
            {
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var b = ua.Address.GetAddressBytes();
                    bool tailscale = b[0] == 100 && b[1] >= 64 && b[1] <= 127;
                    if (b[0] == 169 && b[1] == 254) continue;
                    yield return (ua.Address.ToString(), tailscale ? "Tailscale" : nic.Name);
                }
            }
        }

        private UIElement Remote(MachineState m)
        {
            var name = UI.TextInput(m.Entry.Name, 200);
            name.LostFocus += (s, e) =>
            {
                if (name.Text.Trim() == m.Entry.Name) return;
                m.Entry.Name = name.Text.Trim();
                Settings.Save();
                Fleet.RaiseListChanged();
            };
            var state = UI.Text("", 12, Theme.Muted);
            Bind(_ =>
            {
                state.Text = m.Online
                    ? $"Online  ·  {m.Entry.Address}:{m.Entry.Port}  ·  {m.Snapshot?.Drives.Count ?? 0} drives  ·  Sector {m.Info?.AgentVersion}"
                    : $"{m.Entry.Address}:{m.Entry.Port}  ·  {m.Error}";
                state.Foreground = m.Online ? Theme.Muted : Theme.Red;
            });
            // Updates for that PC, run by its own agent: one click from here, or automatic.
            var upd = FlatButton.Primary("Update", null);
            upd.MinHeight = 34;
            upd.Click += async (o, e) =>
            {
                upd.IsEnabled = false;
                string err = await AppUpdater.Install(m);
                if (!string.IsNullOrEmpty(err)) { MessageBox.Show(err, "Sector"); upd.IsEnabled = true; }
            };
            var autoUpd = new ToggleSwitch(false, "Automatic updates for this PC");
            autoUpd.Toggled += async on =>
            {
                try { await m.Client.SetAutoUpdate(on); }
                catch { autoUpd.IsOn = !on; }
            };
            var autoRow = UI.H(8, UI.Text("Auto-update", 13, Theme.Muted), autoUpd);
            Bind(_ =>
            {
                var u = AppUpdater.StatusOf(m);
                upd.Visibility = u?.Available == true && !AppUpdater.Busy(u) ? Visibility.Visible : Visibility.Collapsed;
                upd.Content = u?.Available == true ? "Update to " + u.Latest : "Update";
                autoRow.Visibility = u != null ? Visibility.Visible : Visibility.Collapsed;
                if (u != null && autoUpd.IsOn != u.Auto) autoUpd.IsOn = u.Auto;
            });

            var alerts = new ToggleSwitch(m.Entry.Alerts, "Alerts for this PC");
            alerts.Toggled += on => { m.Entry.Alerts = on; Settings.Save(); };
            var remove = FlatButton.Secondary("Remove", () =>
            {
                if (MessageBox.Show($"Stop watching {m.Name}? Its history stays on that PC.", "Sector", MessageBoxButton.OKCancel) == MessageBoxResult.OK)
                    Fleet.Remove(m);
            });
            remove.MinHeight = 34;

            var g = new Grid { Margin = new Thickness(0, 6, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = UI.V(6, name, state);
            g.Children.Add(left);
            var right = UI.H(14, upd, autoRow, UI.H(8, UI.Text("Alerts", 13, Theme.Muted), alerts), remove);
            right.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            return g;
        }

        private UIElement AddForm()
        {
            var name = UI.TextInput("", 240);
            var address = UI.TextInput("", 240);
            var port = UI.TextInput(Constants.DefaultPort.ToString(), 90);
            var key = UI.TextInput("", 240);
            key.FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas");
            var result = UI.Text("", 13, Theme.Muted);
            FlatButton add = null;
            add = FlatButton.Primary("Test and add", async () =>
            {
                string a = address.Text.Trim();
                if (string.IsNullOrEmpty(a)) { result.Text = "Type the PC's address first."; result.Foreground = Theme.Amber; return; }
                if (!int.TryParse(port.Text.Trim(), out int p) || p <= 0 || p > 65535) p = Constants.DefaultPort;
                add.IsEnabled = false;
                result.Text = "Checking...";
                result.Foreground = Theme.Muted;
                var (ok, message) = await AgentClient.Test(a, p, key.Text.Trim());
                add.IsEnabled = true;
                if (!ok)
                {
                    result.Text = message;
                    result.Foreground = Theme.Red;
                    return;
                }
                if (Fleet.Machines.Any(x => !x.Entry.IsLocal && x.Entry.Address.Equals(a, StringComparison.OrdinalIgnoreCase) && x.Entry.Port == p))
                {
                    result.Text = "That PC is already in your list.";
                    result.Foreground = Theme.Amber;
                    return;
                }
                var entry = new MachineEntry
                {
                    Name = string.IsNullOrWhiteSpace(name.Text) ? message : name.Text.Trim(),
                    Address = a,
                    Port = p,
                    Key = Secret.Protect(key.Text.Trim())
                };
                var m = Fleet.Add(entry);
                Settings.Current.SelectedMachine = entry.Id;
                Settings.Save();
                Host.SelectMachine(m);
            });

            var steps = UI.V(6,
                UI.Text("1.  Install Sector on the NAS (the same Setup.exe). Tick \"Let my other PCs see this PC\" while installing, or turn it on later under Machines there.", 13, Theme.Muted),
                UI.Text("2.  On the NAS, copy the pairing key from Sector > Machines.", 13, Theme.Muted),
                UI.Text("3.  Here, type the NAS's address (its Tailscale name or IP is easiest, like 100.101.102.103) and the key.", 13, Theme.Muted));
            steps.Margin = new Thickness(0, 0, 0, 12);

            var body = UI.V(0,
                steps,
                UI.Divider(),
                UI.SettingRow("Name", "What to call it here, like \"NAS\". Leave empty to use its PC name.", name),
                UI.SettingRow("Address", "Its Tailscale name, Tailscale IP or local IP.", address),
                UI.SettingRow("Port", "Leave as " + Constants.DefaultPort + " unless you changed it.", port),
                UI.SettingRow("Pairing key", "From Sector > Machines on that PC.", key),
                new Border { Height = 10 },
                UI.H(16, add, result));
            result.VerticalAlignment = VerticalAlignment.Center;
            return UI.Card(body, 22);
        }
    }
}
