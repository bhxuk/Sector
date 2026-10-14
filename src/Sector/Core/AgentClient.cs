using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Sector.Shared;

namespace Sector.Core
{
    /// <summary>Talks to the Sector agent on one PC.</summary>
    public sealed class AgentClient : IDisposable
    {
        private readonly MachineEntry _entry;
        private readonly HttpClient _http;
        private AgentConfig _localConfig;
        private DateTime _localRead = DateTime.MinValue;

        public AgentClient(MachineEntry entry)
        {
            _entry = entry;
            _http = new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5), ConnectTimeout = TimeSpan.FromSeconds(4) })
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Sector/" + Updates.CurrentText);
        }

        /// <summary>For this PC, the port and key come from the agent's own settings file.</summary>
        private AgentConfig Local()
        {
            if (_localConfig == null || DateTime.UtcNow - _localRead > TimeSpan.FromSeconds(30))
            {
                _localConfig = AgentConfig.Load();
                _localRead = DateTime.UtcNow;
            }
            return _localConfig;
        }

        public void ForgetLocalConfig() => _localConfig = null;

        private string BaseUrl
        {
            get
            {
                if (_entry.IsLocal) return $"http://127.0.0.1:{Local().Port}/";
                return BuildUrl(_entry.Address, _entry.Port);
            }
        }

        public static string BuildUrl(string address, int port)
        {
            string host = (address ?? "").Trim();
            if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) host = host.Substring(7);
            host = host.TrimEnd('/');
            if (host.Contains(':') && !host.StartsWith("[") && IPAddress.TryParse(host, out var ip) && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                host = "[" + host + "]";
            return $"http://{host}:{port}/";
        }

        private string Key => _entry.IsLocal ? Local().Key : Secret.Unprotect(_entry.Key);

        private HttpRequestMessage Request(HttpMethod method, string path, object body = null)
        {
            var req = new HttpRequestMessage(method, BaseUrl + path);
            req.Headers.Add(Constants.KeyHeader, Key);
            if (body != null) req.Content = JsonContent.Create(body, body.GetType(), options: Json.Options);
            return req;
        }

        private async Task<T> Send<T>(HttpMethod method, string path, object body = null)
        {
            using var req = Request(method, path, body);
            using var res = await _http.SendAsync(req);
            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                if (_entry.IsLocal) ForgetLocalConfig();
                throw new AgentException(_entry.IsLocal ? "Waiting for the Sector agent to finish starting." : "The pairing key isn't right. Check it on the other PC (Sector > Machines).");
            }
            if (res.StatusCode == HttpStatusCode.Forbidden)
                throw new AgentException(await res.Content.ReadAsStringAsync());
            res.EnsureSuccessStatusCode();
            return await res.Content.ReadFromJsonAsync<T>(Json.Options);
        }

        public Task<AgentInfo> Info() => Send<AgentInfo>(HttpMethod.Get, "api/info");
        public Task<Snapshot> Snapshot() => Send<Snapshot>(HttpMethod.Get, "api/snapshot");
        public Task<List<HistorySensor>> HistorySensors() => Send<List<HistorySensor>>(HttpMethod.Get, "api/history/sensors");

        public Task<List<HistorySeries>> History(IEnumerable<string> ids, DateTime fromUtc, DateTime toUtc) =>
            Send<List<HistorySeries>>(HttpMethod.Post, "api/history", new HistoryRequest { Ids = new List<string>(ids), FromUtc = fromUtc, ToUtc = toUtc });

        public Task<JsonElement> ChangeSettings(AgentSettingsChange change) => Send<JsonElement>(HttpMethod.Post, "api/settings", change);

        public Task<UpdateStatus> CheckUpdate() => Send<UpdateStatus>(HttpMethod.Post, "api/update/check");
        public Task<UpdateStatus> StartUpdate() => Send<UpdateStatus>(HttpMethod.Post, "api/update/install");
        public Task<UpdateStatus> SetAutoUpdate(bool on) => Send<UpdateStatus>(HttpMethod.Post, "api/update/auto", new AgentSettingsChange { AutoUpdate = on });

        public async Task Restart()
        {
            using var req = Request(HttpMethod.Post, "api/restart");
            try { using var _ = await _http.SendAsync(req); } catch { }
        }

        /// <summary>Checks an address and key before a machine is added. Returns the PC's name, or an error a person can act on.</summary>
        public static async Task<(bool ok, string message)> Test(string address, int port, string key)
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            string url = BuildUrl(address, port);
            try
            {
                using (var ping = await http.GetAsync(url + "api/ping"))
                {
                    if (ping.StatusCode == HttpStatusCode.Forbidden)
                        return (false, "Found Sector there, but that PC isn't sharing its stats yet. On that PC, open Sector > Machines and turn on \"Let my other PCs see this PC\".");
                    if (!ping.IsSuccessStatusCode) return (false, $"Something answered, but it isn't Sector ({(int)ping.StatusCode}).");
                }
                using var req = new HttpRequestMessage(HttpMethod.Get, url + "api/info");
                req.Headers.Add(Constants.KeyHeader, key);
                using var res = await http.SendAsync(req);
                if (res.StatusCode == HttpStatusCode.Unauthorized) return (false, "Sector is there, but the pairing key doesn't match.");
                res.EnsureSuccessStatusCode();
                var info = await res.Content.ReadFromJsonAsync<AgentInfo>(Json.Options);
                return (true, info?.Machine ?? address);
            }
            catch (TaskCanceledException) { return (false, "No answer. Check the address, that the PC is on, and that Sector is installed on it."); }
            catch (HttpRequestException ex) { return (false, "Couldn't connect: " + ex.Message); }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public void Dispose() => _http.Dispose();
    }

    public class AgentException : Exception
    {
        public AgentException(string message) : base(message) { }
    }
}
