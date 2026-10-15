using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sector.Shared;

namespace Sector.Core
{
    /// <summary>
    /// One-click updates. The agent on each PC does the download and install (it's a service, so Windows
    /// doesn't ask for permission, and it works on a NAS from across the network). This side shows the offer,
    /// starts it, and closes and reopens this window around the install.
    /// </summary>
    public static class AppUpdater
    {
        private static readonly HashSet<string> Offered = new HashSet<string>();
        private static bool _closing;
        private static bool _notifyQueued;

        public static event Action Changed;

        public static MachineState LocalMachine => Fleet.Machines.FirstOrDefault(m => m.Entry.IsLocal);

        /// <summary>The update state of a PC's agent, or null if its agent is too old to update itself.</summary>
        public static UpdateStatus StatusOf(MachineState m) => m?.Online == true ? m.Snapshot?.Update : null;

        public static bool Busy(UpdateStatus u) => u != null && (u.State == "Downloading" || u.State == "Installing");

        /// <summary>Called after every reading from every PC.</summary>
        public static void Observe(MachineState m)
        {
            var u = StatusOf(m);
            if (u == null) return;

            if (m.Entry.IsLocal && u.State == "Installing" && !_closing)
            {
                CloseForInstall(u.Auto);
                return;
            }

            if (u.Available && !Busy(u) && Offered.Add(m.Entry.Id + "|" + u.Latest))
            {
                // One notification for the lot: clicking it updates every PC that needs it.
                if (!_notifyQueued)
                {
                    _notifyQueued = true;
                    App.Instance?.Dispatcher.BeginInvoke(new Action(async () =>
                    {
                        await Task.Delay(TimeSpan.FromSeconds(8));   // let the other PCs report in first
                        _notifyQueued = false;
                        var pending = Pending();
                        if (pending.Count == 0) return;
                        string names = string.Join(" and ", pending.Select(x => x.Entry.IsLocal ? "this PC" : x.Name));
                        App.Instance?.Tray?.Notify($"Sector {u.Latest} is ready for {names}", "Click here to update. It takes about a minute and Sector reopens by itself.",
                            () => _ = InstallAll());
                    }));
                }
                if (m.Entry.IsLocal) App.Instance?.Tray?.ShowUpdateItem(u.Latest);
            }
            Changed?.Invoke();
        }

        /// <summary>The one click: tells that PC's agent to download and install the update.</summary>
        public static async Task<string> Install(MachineState m)
        {
            if (m == null) return "No PC selected.";
            var u = StatusOf(m);
            if (u == null)
            {
                // Agents from 1.0.0 can't update themselves: fall back to running Setup here (Windows will ask first).
                if (!m.Entry.IsLocal) return $"{m.Name} is on an older version. Update it once from Sector on that PC; after that it can be updated from here.";
                bool ok = await App.Instance.InstallUpdate();
                return ok ? "" : "Couldn't update. Try again from Settings.";
            }
            try
            {
                var s = await m.Client.StartUpdate();
                Changed?.Invoke();
                return s.State == "Failed" ? s.Message : "";
            }
            catch (Exception ex) { return "Couldn't start the update: " + ex.Message; }
        }

        /// <summary>Every online PC with an update waiting, other PCs first and this PC last (this PC's window closes for its install).</summary>
        public static List<MachineState> Pending() =>
            Fleet.Machines.Where(x => StatusOf(x) is UpdateStatus s && s.Available && !Busy(s))
                          .OrderBy(x => x.Entry.IsLocal ? 1 : 0).ToList();

        /// <summary>The one click for everything: starts the update on every PC that needs it.</summary>
        public static async Task<string> InstallAll()
        {
            var list = Pending();
            if (list.Count == 0) return "Everything is already up to date.";
            var errors = new List<string>();
            foreach (var m in list)
            {
                string err = await Install(m);
                if (!string.IsNullOrEmpty(err)) errors.Add(m.Name + ": " + err);
            }
            return string.Join("\n", errors);
        }

        public static async Task<UpdateStatus> CheckNow(MachineState m)
        {
            try { return await m.Client.CheckUpdate(); }
            catch { return null; }
        }

        /// <summary>
        /// The agent is about to run Setup, which replaces Sector.exe. Start a small hidden helper that waits for
        /// Setup to finish (it writes a marker file) and then starts Sector again, then close.
        /// </summary>
        private static void CloseForInstall(bool automatic)
        {
            _closing = true;
            try
            {
                string exe = Environment.ProcessPath.Replace("'", "''");
                string marker = Constants.UpdatedMarker.Replace("'", "''");
                string args = automatic ? " -ArgumentList '--background'" : "";
                string script =
                    $"$m='{marker}';$e='{exe}';" +
                    "for($i=0;$i -lt 600;$i++){if(Test-Path -LiteralPath $m){Start-Sleep -Seconds 2;break};Start-Sleep -Seconds 1};" +
                    $"Start-Process -FilePath $e{args}";
                Process.Start(new ProcessStartInfo("powershell.exe",
                    "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -Command \"" + script + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                Log.Write("Closing for an update; a helper will reopen Sector.");
            }
            catch (Exception ex)
            {
                Log.Write("Couldn't arrange to reopen after the update: " + ex.Message);
            }
            if (!automatic) App.Instance?.Tray?.Notify("Updating Sector", "Sector will reopen in about a minute.");
            App.Instance?.Dispatcher.BeginInvoke(new Action(() => App.Instance.Quit()));
        }
    }
}
