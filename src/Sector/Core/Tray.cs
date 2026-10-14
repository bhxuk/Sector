using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;

namespace Sector.Core
{
    /// <summary>
    /// The icon by the clock. It can show a live temperature (the hottest drive by default) in a colour
    /// that turns amber or red as it nears your limits, with every drive listed when you hover.
    /// </summary>
    public sealed class Tray : IDisposable
    {
        private readonly System.Windows.Forms.NotifyIcon _icon;
        private readonly Icon _appIcon;
        private Icon _live;
        private string _lastKey = "";
        private Action _onClick;
        private System.Windows.Forms.ToolStripMenuItem _update;

        public Tray()
        {
            try { _appIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath); } catch { _appIcon = SystemIcons.Application; }
            _icon = new System.Windows.Forms.NotifyIcon { Text = "Sector", Icon = _appIcon, Visible = true };

            var menu = new System.Windows.Forms.ContextMenuStrip();
            menu.Items.Add("Open Sector", null, (s, e) => App.Instance.ShowHub());
            menu.Items.Add("Drives", null, (s, e) => App.Instance.ShowHub("drives"));
            menu.Items.Add("History", null, (s, e) => App.Instance.ShowHub("history"));
            menu.Items.Add("Alerts", null, (s, e) => App.Instance.ShowHub("alerts"));
            menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
            menu.Items.Add("Exit Sector", null, (s, e) => App.Instance.Quit());
            _icon.ContextMenuStrip = menu;
            _icon.MouseClick += (s, e) => { if (e.Button == System.Windows.Forms.MouseButtons.Left) App.Instance.ShowHub(); };
            _icon.BalloonTipClicked += (s, e) => { var a = _onClick; _onClick = null; a?.Invoke(); };
            _icon.BalloonTipClosed += (s, e) => _onClick = null;
        }

        public void Notify(string title, string text, Action onClick = null)
        {
            _onClick = onClick;
            try { _icon.ShowBalloonTip(6000, title, string.IsNullOrWhiteSpace(text) ? " " : text, System.Windows.Forms.ToolTipIcon.None); } catch { }
        }

        public void ShowUpdateItem(string version)
        {
            var menu = _icon.ContextMenuStrip;
            if (_update == null)
            {
                _update = new System.Windows.Forms.ToolStripMenuItem();
                _update.Click += (s, e) => _ = App.Instance.InstallUpdate();
                menu.Items.Insert(0, _update);
                menu.Items.Insert(1, new System.Windows.Forms.ToolStripSeparator());
            }
            _update.Text = "Install update " + version;
        }

        /// <summary>Redraws the icon and tooltip from the latest readings.</summary>
        public void Refresh()
        {
            var m = TrayMachine();
            string tip = "Sector";
            float? value = null;
            System.Windows.Media.Brush level = Theme.Muted;

            if (m?.Snapshot != null && m.Online)
            {
                var snap = m.Snapshot;
                var lines = new System.Collections.Generic.List<string> { m.Name };
                switch (Settings.Current.TrayShows)
                {
                    case "CPU":
                        value = snap.Summary.CpuTemp;
                        level = Format.Level(value, Settings.Current.Alerts.CpuWarn, Settings.Current.Alerts.CpuCritical);
                        lines.Add("CPU " + Format.Temp(value));
                        break;
                    case "GPU":
                        value = snap.Summary.GpuTemp;
                        level = Format.Level(value, Settings.Current.Alerts.GpuWarn, Settings.Current.Alerts.GpuCritical);
                        lines.Add("GPU " + Format.Temp(value));
                        break;
                    default:
                        var hot = m.HottestDrive;
                        if (hot != null)
                        {
                            value = hot.Temperature;
                            var (w, c) = Format.DriveLimits(m.Entry.Id, hot);
                            level = Format.Level(value, w, c);
                        }
                        break;
                }
                foreach (var d in snap.Drives.Where(d => d.Temperature.HasValue).OrderByDescending(d => d.Temperature).Take(6))
                    lines.Add(Shorten(Settings.Current.DriveName(m.Entry.Id, d), 20) + " " + Format.Temp(d.Temperature));
                tip = string.Join("\n", lines);
            }
            else if (m != null) tip = m.Name + ": offline";

            // NotifyIcon tooltips are capped at 127 characters.
            _icon.Text = tip.Length > 127 ? tip.Substring(0, 127) : tip;

            if (Settings.Current.TrayShows == "Nothing" || !value.HasValue)
            {
                SetIcon(null, "app");
                return;
            }
            int shown = (int)Math.Round(Format.ToUnit(value.Value));
            string key = shown + "|" + level;
            if (key == _lastKey) return;
            _lastKey = key;
            var c2 = ((System.Windows.Media.SolidColorBrush)level).Color;
            SetIcon(Draw(shown, Color.FromArgb(c2.R, c2.G, c2.B)), key);
        }

        private static string Shorten(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";

        private static MachineState TrayMachine()
        {
            var id = Settings.Current.TrayMachine;
            var m = Fleet.Machines.FirstOrDefault(x => x.Entry.Id == id);
            if (m != null) return m;
            // Default: the PC with the most drives (usually the NAS), else this PC.
            return Fleet.Machines.Where(x => x.Snapshot != null).OrderByDescending(x => x.Snapshot.Drives.Count(d => d.Temperature.HasValue)).FirstOrDefault()
                   ?? Fleet.Machines.FirstOrDefault();
        }

        private void SetIcon(Icon icon, string key)
        {
            var old = _live;
            if (icon == null)
            {
                _lastKey = key;
                _icon.Icon = _appIcon;
                _live = null;
            }
            else
            {
                _icon.Icon = icon;
                _live = icon;
            }
            if (old != null)
            {
                Native.DestroyIcon(old.Handle);
                old.Dispose();
            }
        }

        private static Icon Draw(int value, Color colour)
        {
            const int size = 32;
            using var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                using (var path = Rounded(new Rectangle(0, 0, size - 1, size - 1), 8))
                using (var bg = new SolidBrush(Color.FromArgb(235, 18, 20, 26)))
                using (var pen = new Pen(colour, 2.5f))
                {
                    g.FillPath(bg, path);
                    g.DrawPath(pen, path);
                }
                string text = value > 999 ? "!!" : value.ToString();
                float fontSize = text.Length >= 3 ? 13f : 18f;
                using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
                using var brush = new SolidBrush(colour);
                var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString(text, font, brush, new RectangleF(0, 1, size, size), sf);
            }
            return Icon.FromHandle(bmp.GetHicon());
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
            if (_live != null) { Native.DestroyIcon(_live.Handle); _live.Dispose(); }
        }
    }
}
