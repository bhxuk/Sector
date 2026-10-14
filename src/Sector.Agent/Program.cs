using System;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sector.Shared;

namespace Sector.Agent
{
    public static class Program
    {
        public static readonly DateTime Started = DateTime.UtcNow;
        public static string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        public static int Main(string[] args)
        {
            // Small command-line jobs the installer and the app use. Each one runs and exits.
            string Arg(string name)
            {
                int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
                return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
            }
            bool Has(string name) => args.Any(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));

            if (Has("--allow-remote"))
            {
                var c = ConfigStore.Load();
                c.AllowRemote = (Arg("--allow-remote") ?? "on").Equals("on", StringComparison.OrdinalIgnoreCase);
                ConfigStore.Save();
                ConfigStore.ApplyFirewall(c.AllowRemote, c.Port);
                Console.WriteLine("Other PCs " + (c.AllowRemote ? "can" : "can't") + " read this PC's stats.");
                return 0;
            }
            if (Has("--new-key"))
            {
                var c = ConfigStore.Load();
                c.Key = ConfigStore.NewKey();
                ConfigStore.Save();
                Console.WriteLine(c.Key);
                return 0;
            }
            if (Has("--show-key"))
            {
                Console.WriteLine(ConfigStore.Load().Key);
                return 0;
            }
            if (Has("--dump"))
            {
                // Prints one full reading. Handy for checking what a PC can see: Sector.Agent.exe --dump > stats.json
                ConfigStore.Load();
                using var reader = new SensorReader();
                reader.Open();
                reader.Poll();
                Thread.Sleep(1500);
                var snap = reader.Poll();
                Console.WriteLine(JsonSerializer.Serialize(snap, new JsonSerializerOptions(Json.Options) { WriteIndented = true }));
                return 0;
            }

            Log.ToConsole = Has("--console");
            try
            {
                Run(args);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Write("Fatal: " + ex);
                return 1;
            }
        }

        private static void Run(string[] args)
        {
            var config = ConfigStore.Load();
            Log.Write($"Sector agent {Version} starting on port {config.Port} (other PCs {(config.AllowRemote ? "allowed" : "not allowed")}).");

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = args,
                ContentRootPath = AppContext.BaseDirectory
            });
            builder.Host.UseWindowsService(o => o.ServiceName = Constants.ServiceName);
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls($"http://*:{config.Port}");
            builder.Services.AddSingleton<SensorReader>();
            builder.Services.AddSingleton<HistoryStore>();
            builder.Services.AddHostedService<PollWorker>();
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = Json.Options.PropertyNamingPolicy;
                o.SerializerOptions.DefaultIgnoreCondition = Json.Options.DefaultIgnoreCondition;
                o.SerializerOptions.NumberHandling = Json.Options.NumberHandling;
            });

            var app = builder.Build();

            // Gatekeeper: other PCs only when allowed, and every data call needs the pairing key.
            app.Use(async (ctx, next) =>
            {
                var ip = ctx.Connection.RemoteIpAddress;
                bool local = ip == null || IPAddress.IsLoopback(ip);
                var c = ConfigStore.Current;
                if (!local && !c.AllowRemote)
                {
                    ctx.Response.StatusCode = 403;
                    await ctx.Response.WriteAsync("This PC isn't sharing its stats. Turn on \"Let my other PCs see this PC\" in Sector on this PC.");
                    return;
                }
                string path = ctx.Request.Path.Value ?? "";
                if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase) && !path.Equals("/api/ping", StringComparison.OrdinalIgnoreCase))
                {
                    if (!ConfigStore.KeyMatches(ctx.Request.Headers[Constants.KeyHeader].ToString()))
                    {
                        ctx.Response.StatusCode = 401;
                        await ctx.Response.WriteAsync("Wrong or missing pairing key.");
                        return;
                    }
                    if (ctx.Request.Method == "POST" && !local)
                    {
                        ctx.Response.StatusCode = 403;
                        await ctx.Response.WriteAsync("Settings can only be changed on this PC.");
                        return;
                    }
                }
                await next();
            });

            app.MapGet("/", () => Results.Text($"Sector agent {Version} on {Environment.MachineName}"));

            app.MapGet("/api/ping", () => Results.Json(new { app = "Sector", version = Version, machine = Environment.MachineName }));

            app.MapGet("/api/info", () => Results.Json(new AgentInfo
            {
                Machine = Environment.MachineName,
                Os = Environment.OSVersion.VersionString,
                AgentVersion = Version,
                PawnIoInstalled = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled,
                PawnIoVersion = LibreHardwareMonitor.PawnIo.PawnIo.Version?.ToString() ?? "",
                AllowRemote = ConfigStore.Current.AllowRemote,
                Port = ConfigStore.Current.Port,
                UptimeSeconds = Environment.TickCount64 / 1000.0,
                StartedUtc = Started,
                HistoryDays = ConfigStore.Current.HistoryDays
            }, Json.Options));

            app.MapGet("/api/snapshot", (SensorReader r) => Results.Json(r.Latest, Json.Options));

            app.MapGet("/api/history/sensors", (HistoryStore h) => Results.Json(h.Sensors(), Json.Options));

            app.MapPost("/api/history", (HistoryRequest req, HistoryStore h) =>
            {
                if (req == null || req.Ids == null || req.Ids.Count == 0) return Results.BadRequest("No sensors asked for.");
                return Results.Json(h.Query(req.Ids.Take(64), req.FromUtc, req.ToUtc), Json.Options);
            });

            app.MapPost("/api/settings", (AgentSettingsChange change) =>
            {
                var c = ConfigStore.Current;
                if (change?.AllowRemote is bool allow && allow != c.AllowRemote)
                {
                    c.AllowRemote = allow;
                    ConfigStore.ApplyFirewall(allow, c.Port);
                    Log.Write("Other PCs " + (allow ? "allowed" : "blocked") + ".");
                }
                if (change?.HistoryDays is int days) c.HistoryDays = Math.Clamp(days, 7, 3650);
                if (change?.NewKey == true) { c.Key = ConfigStore.NewKey(); Log.Write("New pairing key made."); }
                ConfigStore.Save();
                return Results.Json(new { allowRemote = c.AllowRemote, historyDays = c.HistoryDays, key = c.Key }, Json.Options);
            });

            // After PawnIO is installed the agent has to start again to load it. The service restarts itself.
            app.MapPost("/api/restart", (IHostApplicationLifetime life) =>
            {
                Log.Write("Restart asked for.");
                _ = Task.Run(async () => { await Task.Delay(500); Environment.Exit(3); });
                return Results.Ok();
            });

            app.Run();
        }
    }

    /// <summary>Reads the sensors every couple of seconds and feeds the history.</summary>
    public sealed class PollWorker : BackgroundService
    {
        private readonly SensorReader _reader;
        private readonly HistoryStore _history;

        public PollWorker(SensorReader reader, HistoryStore history)
        {
            _reader = reader;
            _history = history;
        }

        protected override async Task ExecuteAsync(CancellationToken stop)
        {
            // Opening the hardware monitor can take a few seconds; don't hold up the web server.
            await Task.Yield();
            try { _reader.Open(); }
            catch (Exception ex) { Log.Write("Couldn't open the hardware monitor: " + ex); }

            int errors = 0;
            while (!stop.IsCancellationRequested)
            {
                var started = DateTime.UtcNow;
                try
                {
                    var snap = _reader.Poll();
                    _history.Add(snap);
                    errors = 0;
                }
                catch (Exception ex)
                {
                    if (errors++ < 5) Log.Write("Poll: " + ex.Message);
                }
                int wait = ConfigStore.Current.PollMilliseconds - (int)(DateTime.UtcNow - started).TotalMilliseconds;
                try { await Task.Delay(Math.Max(250, wait), stop); } catch (TaskCanceledException) { }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);
            try { _history.Dispose(); } catch { }
            try { _reader.Dispose(); } catch { }
            Log.Write("Sector agent stopped.");
        }
    }
}
