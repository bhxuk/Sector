using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Sector.Core;

namespace Sector
{
    public static class Program
    {
        private static Mutex _mutex;

        [STAThread]
        public static int Main(string[] args)
        {
            bool restarting = args.Any(a => a.Equals("--restarting", StringComparison.OrdinalIgnoreCase));
            _mutex = new Mutex(true, "Sector.SingleInstance.bhxuk", out bool isNew);
            for (int i = 0; !isNew && restarting && i < 50; i++)
            {
                Thread.Sleep(200);
                _mutex.Dispose();
                _mutex = new Mutex(true, "Sector.SingleInstance.bhxuk", out isNew);
            }
            if (!isNew)
            {
                try
                {
                    using var ev = EventWaitHandle.OpenExisting("Sector.Show.bhxuk");
                    ev.Set();
                }
                catch { }
                return 0;
            }

            var app = new App { Background = !restarting && args.Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase)) };
            int code = app.Run();
            GC.KeepAlive(_mutex);
            return code;
        }
    }

    public class App : Application
    {
        public bool Background { get; set; }
        public static App Instance => Current as App;

        public Tray Tray { get; private set; }
        public HubWindow Hub { get; private set; }

        private EventWaitHandle _showEvent;
        private DispatcherTimer _updateTimer;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            DispatcherUnhandledException += (s, ex) => { Log.Write("Unhandled: " + ex.Exception); ex.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => Log.Write("Fatal: " + ex.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, ex) => { Log.Write("Task: " + ex.Exception); ex.SetObserved(); };

            Settings.Load();
            AppStyles.Install();
            AutoStart.Apply(Settings.Current.StartWithWindows);

            Tray = new Tray();
            Fleet.Updated += m =>
            {
                Alerts.Evaluate(m);
                AppUpdater.Observe(m);
                Tray.Refresh();
            };
            Fleet.Start();

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Sector.Show.bhxuk");
            var t = new Thread(() =>
            {
                while (true)
                {
                    _showEvent.WaitOne();
                    Dispatcher.BeginInvoke(new Action(() => ShowHub()));
                }
            }) { IsBackground = true };
            t.Start();

            string version = Updates.CurrentText;
            if (!string.IsNullOrEmpty(Settings.Current.LastVersion) && Settings.Current.LastVersion != version)
            {
                string from = Settings.Current.LastVersion;
                _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() =>
                    Tray.Notify("Sector updated to " + version, "You were on " + from + ". Click to see what's new.",
                        () => Shell.Open("https://github.com/" + Updates.Repo + "/blob/main/CHANGELOG.md")))));
            }
            Settings.Current.LastVersion = version;
            Settings.Save();

            if (Settings.Current.FirstRun || !Background) ShowHub();
            if (Settings.Current.FirstRun)
            {
                Settings.Current.FirstRun = false;
                Settings.Save();
            }

            if (Settings.Current.CheckForUpdates)
            {
                _updateTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
                _updateTimer.Tick += async (s, ev) => await CheckForUpdate();
                _updateTimer.Start();
                _ = Task.Delay(TimeSpan.FromSeconds(20)).ContinueWith(_ => Dispatcher.BeginInvoke(new Action(async () => await CheckForUpdate())));
            }
        }

        public void ShowHub(string page = null)
        {
            if (Hub == null)
            {
                Hub = new HubWindow();
                Hub.Closed += (s, e) => Hub = null;
            }
            if (page != null) Hub.Navigate(page);
            Hub.Show();
            if (Hub.WindowState == WindowState.Minimized) Hub.WindowState = WindowState.Normal;
            Hub.Activate();
        }

        private string _notifiedVersion;

        /// <summary>
        /// Only for agents from 1.0.0, which can't update themselves. Newer agents check GitHub on their own
        /// and the offer arrives through <see cref="AppUpdater"/>.
        /// </summary>
        public async Task CheckForUpdate()
        {
            var local = AppUpdater.LocalMachine;
            if (local == null || !local.Online || AppUpdater.StatusOf(local) != null) return;
            if (!await Updates.Check()) return;
            if (Settings.Current.AutoUpdate)
            {
                await InstallUpdate();
                return;
            }
            if (_notifiedVersion == Updates.LatestVersion) return;
            _notifiedVersion = Updates.LatestVersion;
            Tray.Notify("Sector " + Updates.LatestVersion + " is available", "Click here to install it. Sector reopens by itself.", () => _ = InstallUpdate());
        }

        private bool _installing;

        public async Task<bool> InstallUpdate(Action<string> status = null)
        {
            if (_installing) return false;
            _installing = true;
            Tray.Notify("Updating Sector" + (Updates.LatestVersion != null ? " to " + Updates.LatestVersion : ""), "Downloading now. Sector will reopen in a few seconds.");
            bool ok = await Updates.DownloadAndInstall(status);
            if (!ok)
            {
                _installing = false;
                Tray.Notify("Couldn't update Sector", "Try again from Sector > Settings.");
            }
            return ok;
        }

        public void Restart()
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath, "--restarting") { UseShellExecute = false }); }
            catch (Exception ex) { Log.Write("Restart failed: " + ex.Message); return; }
            Quit();
        }

        public void Quit()
        {
            Fleet.Stop();
            Tray?.Dispose();
            Shutdown();
        }
    }
}
