using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Sector.Core;
using Sector.Pages;
using Sector.Shared;

namespace Sector
{
    /// <summary>The main window: machines and pages down the side, the selected page on the right.</summary>
    public class HubWindow : Window
    {
        private readonly StackPanel _machineList = new StackPanel();
        private readonly StackPanel _nav = new StackPanel();
        private readonly ContentControl _page = new ContentControl();
        private readonly ScrollViewer _scroll;
        private readonly TextBlock _title;
        private readonly TextBlock _subtitle;
        private readonly Border _banner = new Border();
        private readonly Border _updateBar = new Border();
        private string _updateKey;
        private readonly Dictionary<string, FlatButton> _navButtons = new Dictionary<string, FlatButton>();
        private readonly Dictionary<string, (FlatButton button, Border dot, TextBlock temp)> _machineButtons = new Dictionary<string, (FlatButton, Border, TextBlock)>();
        private PageBase _current;
        private string _currentId = "overview";
        private readonly bool _glass;

        public HubWindow()
        {
            Title = "Sector";
            Width = Math.Max(960, Settings.Current.WindowWidth);
            Height = Math.Max(640, Settings.Current.WindowHeight);
            MinWidth = 900;
            MinHeight = 600;
            _glass = Glass.Available;
            Theme.GlassActive = _glass;
            Background = _glass ? Brushes.Transparent : Theme.Bg;
            Foreground = Theme.Text;
            FontFamily = Theme.Font;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Icon = IconCache.AppIcon(); } catch { }

            var side = new DockPanel { Width = 250, Background = _glass ? Theme.GlassSide : Theme.Side };
            var logo = UI.H(10, new Image { Source = IconCache.AppIcon(), Width = 30, Height = 30 }, UI.Text("Sector", 20, Theme.Text, FontWeights.Bold, true));
            logo.Margin = new Thickness(10, 0, 0, 22);

            var bottom = UI.V(4, NavButton("machines", "Machines", Icons.Server), NavButton("settings", "Settings", Icons.Settings));
            bottom.Margin = new Thickness(14, 0, 14, 20);
            DockPanel.SetDock(bottom, Dock.Bottom);
            side.Children.Add(bottom);

            var label = UI.Label("Machines");
            label.Margin = new Thickness(12, 0, 0, 8);
            var top = new StackPanel { Margin = new Thickness(14, 24, 14, 0) };
            top.Children.Add(logo);
            top.Children.Add(label);
            top.Children.Add(_machineList);
            var label2 = UI.Label("View");
            label2.Margin = new Thickness(12, 18, 0, 8);
            top.Children.Add(label2);
            top.Children.Add(_nav);
            _nav.Children.Add(NavButton("overview", "Overview", Icons.Gauge));
            _nav.Children.Add(NavButton("drives", "Drives", Icons.Drive));
            _nav.Children.Add(NavButton("sensors", "All sensors", Icons.List));
            _nav.Children.Add(NavButton("history", "History", Icons.Chart));
            _nav.Children.Add(NavButton("alerts", "Alerts", Icons.Bell));
            var sideScroll = new ScrollViewer { Content = top, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            side.Children.Add(sideScroll);

            _title = UI.Text("", 30, Theme.Text, FontWeights.SemiBold, true);
            _subtitle = UI.Text("", 15, Theme.Muted);
            var header = UI.V(6, _title, _subtitle);
            header.Margin = new Thickness(0, 0, 0, 20);
            var pageStack = new StackPanel { MaxWidth = 1240 };
            pageStack.Children.Add(header);
            pageStack.Children.Add(_updateBar);
            pageStack.Children.Add(_banner);
            pageStack.Children.Add(_page);

            _scroll = new ScrollViewer
            {
                Content = new Border { Padding = new Thickness(40, 36, 40, 40), Child = pageStack },
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            SmoothScroll.Enable(_scroll);

            var root = new DockPanel { Background = _glass ? Theme.GlassTint : null };
            DockPanel.SetDock(side, Dock.Left);
            root.Children.Add(side);
            var line = new Border { Width = 1, Background = Theme.Line };
            DockPanel.SetDock(line, Dock.Left);
            root.Children.Add(line);
            root.Children.Add(_scroll);
            Content = root;

            SourceInitialized += (s, e) =>
            {
                var h = new WindowInteropHelper(this).Handle;
                Native.UseDarkTitleBar(h);
                Native.RoundCorners(h);
                if (_glass && !Glass.Apply(this))
                {
                    Theme.GlassActive = false;
                    Background = Theme.Bg;
                    root.Background = null;
                    side.Background = Theme.Side;
                    Navigate(_currentId);
                }
            };

            Fleet.Updated += OnUpdated;
            Fleet.ListChanged += OnListChanged;
            Alerts.Logged += OnAlertLogged;
            Closed += (s, e) =>
            {
                Fleet.Updated -= OnUpdated;
                Fleet.ListChanged -= OnListChanged;
                Alerts.Logged -= OnAlertLogged;
                _current?.Leave();
            };
            SizeChanged += (s, e) =>
            {
                if (WindowState != WindowState.Normal) return;
                Settings.Current.WindowWidth = ActualWidth;
                Settings.Current.WindowHeight = ActualHeight;
            };
            Closing += (s, e) => Settings.Save();

            BuildMachineList();
            Navigate("overview");
        }

        private FlatButton NavButton(string id, string name, string icon)
        {
            var b = new FlatButton
            {
                Content = UI.H(12, UI.GridIcon(icon, 18, Theme.Text), UI.Text(name, 14, Theme.Text, FontWeights.Medium)),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 0, 12, 0),
                MinHeight = 42,
                Background = Brushes.Transparent,
                Margin = new Thickness(0, 0, 0, 2)
            };
            b.Click += (s, e) => Navigate(id);
            _navButtons[id] = b;
            return b;
        }

        private void BuildMachineList()
        {
            _machineList.Children.Clear();
            _machineButtons.Clear();
            foreach (var m in Fleet.Machines)
            {
                var dot = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Theme.Off, VerticalAlignment = VerticalAlignment.Center };
                var icon = UI.GridIcon(m.Entry.IsLocal ? Icons.Devices : Icons.Server, 18, Theme.Text);
                var name = UI.Text(m.Name, 14, Theme.Text, FontWeights.Medium);
                name.TextWrapping = TextWrapping.NoWrap;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                var temp = UI.Text("", 12, Theme.Muted, FontWeights.SemiBold);
                temp.VerticalAlignment = VerticalAlignment.Center;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var left = UI.H(12, icon, name);
                left.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(left, 0);
                Grid.SetColumnSpan(left, 2);
                var right = UI.H(8, temp, dot);
                Grid.SetColumn(right, 2);
                g.Children.Add(left);
                g.Children.Add(right);
                var b = new FlatButton
                {
                    Content = g,
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(12, 0, 12, 0),
                    MinHeight = 42,
                    Background = Brushes.Transparent,
                    Margin = new Thickness(0, 0, 0, 2),
                    ToolTip = m.Entry.IsLocal ? "This PC" : m.Entry.Address
                };
                var mm = m;
                b.Click += (s, e) => SelectMachine(mm);
                _machineButtons[m.Entry.Id] = (b, dot, temp);
                _machineList.Children.Add(b);
                UpdateMachineButton(m);
            }
            var add = new FlatButton
            {
                Content = UI.H(12, UI.GridIcon(Icons.Plus, 18, Theme.Muted), UI.Text("Add a PC or NAS", 13, Theme.Muted)),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 0, 12, 0),
                MinHeight = 36,
                Background = Brushes.Transparent
            };
            add.Click += (s, e) => Navigate("machines");
            _machineList.Children.Add(add);
            HighlightMachine();
        }

        private void UpdateMachineButton(MachineState m)
        {
            if (!_machineButtons.TryGetValue(m.Entry.Id, out var mb)) return;
            mb.dot.Background = m.Online ? Theme.Green : m.EverOnline ? Theme.Red : Theme.Off;
            var hot = m.HottestDrive;
            if (m.Online && hot != null)
            {
                var (w, c) = Format.DriveLimits(m.Entry.Id, hot);
                mb.temp.Text = Format.Temp(hot.Temperature, false);
                mb.temp.Foreground = Format.Level(hot.Temperature, w, c);
                mb.temp.ToolTip = "Hottest drive";
            }
            else mb.temp.Text = "";
        }

        private void HighlightMachine()
        {
            var sel = Fleet.Selected;
            foreach (var kv in _machineButtons)
                kv.Value.button.Background = sel != null && kv.Key == sel.Entry.Id ? Theme.Subtle : Brushes.Transparent;
        }

        public void SelectMachine(MachineState m)
        {
            Settings.Current.SelectedMachine = m.Entry.Id;
            Settings.Save();
            HighlightMachine();
            if (_currentId == "machines" || _currentId == "settings") Navigate("overview");
            else Navigate(_currentId);
            _ = Fleet.PollOnce(m);
        }

        public void Navigate(string id)
        {
            _current?.Leave();
            _currentId = id;
            foreach (var kv in _navButtons) kv.Value.Background = kv.Key == id ? Theme.AmberTint : Brushes.Transparent;

            _current = id switch
            {
                "drives" => new DrivesPage(),
                "sensors" => new SensorsPage(),
                "history" => new HistoryPage(),
                "alerts" => new AlertsPage(),
                "machines" => new MachinesPage(),
                "settings" => new SettingsPage(),
                _ => new OverviewPage()
            };
            _current.Host = this;
            _title.Text = _current.Title;
            _page.Content = _current.Build(Fleet.Selected);
            _scroll.ScrollToTop();
            Refresh(Fleet.Selected);
        }

        private void OnUpdated(MachineState m)
        {
            UpdateMachineButton(m);
            if (m == Fleet.Selected || !_current.MachineSpecific) Refresh(m);
        }

        private void OnListChanged()
        {
            BuildMachineList();
            if (_currentId != "machines") Navigate(_currentId);
        }

        private void OnAlertLogged()
        {
            if (_current is AlertsPage) _current.Update(Fleet.Selected);
        }

        private void Refresh(MachineState m)
        {
            var sel = Fleet.Selected;
            if (_current.MachineSpecific && sel != null)
            {
                string sub = sel.Name;
                if (sel.Online && sel.Snapshot != null)
                {
                    if (!sel.Entry.IsLocal || sel.Entry.Name != sel.Snapshot.Machine) sub = sel.Name + " (" + sel.Snapshot.Machine + ")";
                    sub += "  ·  up " + Format.Uptime(sel.Snapshot.Summary.UptimeSeconds);
                }
                else if (sel.EverOnline) sub += "  ·  last seen " + Format.Ago(sel.LastOk);
                _subtitle.Text = _current.Subtitle(sel) ?? sub;
                _banner.Child = Banner(sel);
                _banner.Margin = _banner.Child == null ? new Thickness(0) : new Thickness(0, 0, 0, 20);
            }
            else
            {
                _subtitle.Text = _current.Subtitle(sel) ?? "";
                _banner.Child = null;
                _banner.Margin = new Thickness(0);
            }
            _current.Update(sel);
            RefreshUpdateBar(sel);
        }

        /// <summary>
        /// The update strip: "Sector 1.2.0 is ready [Update now]" for this PC, or "[Update NAS]" while you're
        /// looking at the NAS. Shows download progress and the install while it happens.
        /// </summary>
        private void RefreshUpdateBar(MachineState sel)
        {
            var local = AppUpdater.LocalMachine;
            MachineState target = null;
            UpdateStatus u = null;
            var su = AppUpdater.StatusOf(sel);
            if (sel != null && !sel.Entry.IsLocal && su != null && (su.Available || AppUpdater.Busy(su))) { target = sel; u = su; }
            else
            {
                var lu = AppUpdater.StatusOf(local);
                if (lu != null && (lu.Available || AppUpdater.Busy(lu) || lu.State == "Failed")) { target = local; u = lu; }
            }

            string key = target == null ? "" : target.Entry.Id + "|" + u.State + "|" + u.Latest + "|" + (u.State == "Downloading" ? u.Progress / 5 : 0) + "|" + u.Message;
            if (key == _updateKey) return;
            _updateKey = key;
            if (target == null)
            {
                _updateBar.Child = null;
                _updateBar.Margin = new Thickness(0);
                return;
            }

            string where = target.Entry.IsLocal ? "" : " for " + target.Name;
            string text;
            UIElement action = null;
            switch (u.State)
            {
                case "Downloading":
                    text = $"Downloading Sector {u.Latest}{where}...  {u.Progress}%";
                    break;
                case "Installing":
                    text = target.Entry.IsLocal ? $"Installing Sector {u.Latest}. Sector will close and reopen by itself." : $"Installing Sector {u.Latest} on {target.Name}. It'll be back in about a minute.";
                    break;
                case "Failed":
                    text = u.Message;
                    action = FlatButton.Secondary("Try again", () => _ = AppUpdater.Install(target));
                    break;
                default:
                    text = target.Entry.IsLocal
                        ? $"Sector {u.Latest} is ready. You're on {u.Current}."
                        : $"{target.Name} is on Sector {u.Current}. {u.Latest} is ready.";
                    var b = FlatButton.Primary(target.Entry.IsLocal ? "Update now" : "Update " + target.Name, null);
                    b.MinHeight = 36;
                    b.Click += async (o, e) =>
                    {
                        b.IsEnabled = false;
                        string err = await AppUpdater.Install(target);
                        if (!string.IsNullOrEmpty(err)) { MessageBox.Show(err, "Sector"); b.IsEnabled = true; }
                    };
                    var notes = FlatButton.Secondary("What's new", () => Shell.Open("https://github.com/" + Updates.Repo + "/blob/main/CHANGELOG.md"));
                    notes.MinHeight = 36;
                    action = UI.H(8, notes, b);
                    break;
            }
            if (action is FlatButton fb) fb.MinHeight = 36;

            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = UI.GridIcon(Icons.Refresh, 18, Theme.Amber);
            icon.Margin = new Thickness(0, 0, 14, 0);
            icon.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(icon);
            var t = UI.Text(text, 14, Theme.Text, FontWeights.SemiBold);
            t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, 1);
            g.Children.Add(t);
            if (action is FrameworkElement fe)
            {
                fe.Margin = new Thickness(16, 0, 0, 0);
                fe.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(fe, 2);
                g.Children.Add(fe);
            }
            var card = UI.Card(g, 14, Theme.AmberTint);
            card.BorderBrush = Theme.Amber;
            _updateBar.Child = card;
            _updateBar.Margin = new Thickness(0, 0, 0, 16);
        }

        private string _bannerKey;
        private UIElement _bannerCache;

        /// <summary>A strip across the top when something needs doing: the agent isn't running, or the driver for CPU and fan readings is missing.</summary>
        private UIElement Banner(MachineState m)
        {
            string key;
            if (!m.Online) key = "off|" + m.Error;
            else if (m.Snapshot != null && !m.Snapshot.PawnIoInstalled && !Settings.Current.Hidden.Contains("pawnio|" + m.Entry.Id)) key = "pawn|" + m.Entry.Id;
            else key = "";
            if (key == _bannerKey) return _bannerCache;
            _bannerKey = key;
            _bannerCache = null;
            if (key == "") return null;

            UIElement actions = null;
            string title, text;
            Brush tone;
            if (!m.Online)
            {
                tone = Theme.Red;
                title = m.EverOnline ? "Can't reach " + m.Name : "Waiting for " + m.Name;
                text = m.Error;
                if (m.Entry.IsLocal && LocalAgent.Status() != "Running")
                {
                    actions = FlatButton.Primary("Start the agent", async () =>
                    {
                        if (await LocalAgent.InstallAndStart()) { await System.Threading.Tasks.Task.Delay(3000); await Fleet.PollOnce(m); }
                    });
                }
            }
            else
            {
                tone = Theme.Amber;
                title = "CPU temperatures and fan speeds need one more driver";
                text = "Drive temperatures already work. For CPU temperatures, motherboard sensors and fan speeds, Windows needs PawnIO, a small signed driver made for tools like this.";
                if (m.Entry.IsLocal)
                {
                    actions = UI.H(10,
                        FlatButton.Primary("Install PawnIO", async () =>
                        {
                            if (await LocalAgent.InstallPawnIo())
                            {
                                await System.Threading.Tasks.Task.Delay(5000);
                                m.Info = null;
                                await Fleet.PollOnce(m);
                            }
                        }),
                        FlatButton.Secondary("Not now", () => { Settings.Current.Hidden.Add("pawnio|" + m.Entry.Id); Settings.Save(); _bannerKey = null; Refresh(m); }));
                }
                else
                {
                    text += " Install it from Sector on " + m.Name + " (the same banner shows there).";
                    actions = FlatButton.Secondary("Not now", () => { Settings.Current.Hidden.Add("pawnio|" + m.Entry.Id); Settings.Save(); _bannerKey = null; Refresh(m); });
                }
            }

            var stack = UI.V(6, UI.Text(title, 16, Theme.Text, FontWeights.SemiBold), UI.Text(text, 13, Theme.Muted));
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = UI.GridIcon(Icons.Warning, 22, tone);
            icon.Margin = new Thickness(0, 2, 16, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(icon);
            Grid.SetColumn(stack, 1);
            g.Children.Add(stack);
            if (actions is FrameworkElement fe)
            {
                fe.Margin = new Thickness(16, 0, 0, 0);
                fe.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(fe, 2);
                g.Children.Add(fe);
            }
            var card = UI.Card(g, 18);
            card.BorderBrush = tone;
            _bannerCache = card;
            return card;
        }
    }
}
