using System;
using System.Collections.Generic;
using System.Linq;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Storage;
using Sector.Shared;

namespace Sector.Agent
{
    /// <summary>
    /// Owns the hardware monitor. Every poll it updates all sensors, builds a snapshot for the API
    /// and hands the values to the history store.
    /// </summary>
    public sealed class SensorReader : IDisposable
    {
        private readonly Computer _computer;
        private readonly object _gate = new object();
        private Snapshot _latest = new Snapshot();

        public SensorReader()
        {
            // Drives change temperature slowly, and asking a sleeping disk too often can keep it awake.
            StorageDevice.ThrottleInterval = TimeSpan.FromSeconds(10);

            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsStorageEnabled = true,
                IsNetworkEnabled = false,
                IsPsuEnabled = true,
                IsPowerMonitorEnabled = true,
                IsBatteryEnabled = false
            };
        }

        public void Open()
        {
            _computer.Open();
            Log.Write("Hardware found: " + string.Join(", ", _computer.Hardware.Select(h => h.HardwareType + " '" + h.Name + "'")));
        }

        public Snapshot Latest
        {
            get { lock (_gate) return _latest; }
        }

        /// <summary>Reads every sensor once and returns the new snapshot.</summary>
        public Snapshot Poll()
        {
            var nodes = new List<HardwareNode>();
            var drives = new List<DriveInfo>();
            var winDisks = WindowsDisks.Get();

            foreach (var hw in _computer.Hardware.ToList())
            {
                try { hw.Update(); } catch (Exception ex) { Log.Write("Update " + hw.Name + ": " + ex.Message); }
                foreach (var sub in hw.SubHardware)
                {
                    try { sub.Update(); } catch { }
                }
                AddNode(nodes, hw, "");

                if (hw is StorageDevice sd)
                {
                    try { drives.Add(BuildDrive(sd, winDisks)); }
                    catch (Exception ex) { Log.Write("Drive " + hw.Name + ": " + ex.Message); }
                }
            }

            var snap = new Snapshot
            {
                Machine = Environment.MachineName,
                TimeUtc = DateTime.UtcNow,
                Hardware = nodes,
                Drives = drives.OrderBy(d => d.Number).ToList(),
                Summary = Summarise(nodes),
                PawnIoInstalled = LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled
            };
            lock (_gate) _latest = snap;
            return snap;
        }

        private static void AddNode(List<HardwareNode> nodes, IHardware hw, string parent)
        {
            var node = new HardwareNode
            {
                Id = hw.Identifier.ToString(),
                Name = hw.Name,
                Type = hw.HardwareType.ToString(),
                Parent = parent
            };
            foreach (var s in hw.Sensors)
            {
                // The hardware monitor can keep a day of readings per sensor; history lives in our own store instead.
                if (s.ValuesTimeWindow != TimeSpan.Zero) s.ValuesTimeWindow = TimeSpan.Zero;
                node.Sensors.Add(new SensorReading
                {
                    Id = s.Identifier.ToString(),
                    Name = s.Name,
                    Type = s.SensorType.ToString(),
                    Value = Clean(s.Value),
                    Min = Clean(s.Min),
                    Max = Clean(s.Max)
                });
            }
            nodes.Add(node);
            foreach (var sub in hw.SubHardware) AddNode(nodes, sub, node.Id);
        }

        private static float? Clean(float? v)
        {
            if (!v.HasValue) return null;
            if (float.IsNaN(v.Value) || float.IsInfinity(v.Value)) return null;
            return v;
        }

        private static DriveInfo BuildDrive(StorageDevice sd, Dictionary<int, WindowsDisks.Disk> winDisks)
        {
            var st = sd.Storage;
            int number = Convert.ToInt32(st.DriveNumber);
            winDisks.TryGetValue(number, out var win);

            ISensor S(string name, SensorType type) => sd.Sensors.FirstOrDefault(x => x.SensorType == type && x.Name == name);
            float? V(ISensor s) => s == null ? null : Clean(s.Value);

            var temp = S("Temperature", SensorType.Temperature) ?? S("Composite Temperature", SensorType.Temperature);
            float? tempValue = V(temp);
            // No SMART (some USB enclosures and RAID cards): the monitor reports 0, which isn't a real reading.
            if (tempValue.HasValue && tempValue.Value <= 0) tempValue = null;

            var d = new DriveInfo
            {
                Number = number,
                HardwareId = sd.Identifier.ToString(),
                Model = !string.IsNullOrWhiteSpace(st.Model) ? st.Model.Trim() : win?.Name ?? sd.Name,
                Serial = win?.Serial ?? "",
                Firmware = !string.IsNullOrWhiteSpace(st.Firmware) ? st.Firmware.Trim() : win?.Firmware ?? "",
                Media = st.IsNVMe ? "NVMe" : (string.IsNullOrEmpty(win?.Media) ? "Unknown" : win.Media),
                Bus = st.IsNVMe ? "NVMe" : win?.Bus ?? "",
                SizeGB = win != null && win.SizeGB > 0 ? win.SizeGB : Convert.ToDouble(st.TotalSize) / 1e9,
                Letters = win != null ? string.Join(" ", win.Letters) : "",
                WindowsHealth = win?.Health ?? "Unknown",
                Temperature = tempValue,
                TemperatureMin = tempValue.HasValue ? Positive(temp?.Min) : null,
                TemperatureMax = tempValue.HasValue ? Positive(temp?.Max) : null,
                TemperatureSensor = temp?.Identifier.ToString() ?? "",
                WarningTemperature = Positive(V(S("Warning Temperature", SensorType.Temperature))),
                CriticalTemperature = Positive(V(S("Critical Temperature", SensorType.Temperature))),
                UsedPercent = V(S("Used Space", SensorType.Load)),
                FreeGB = V(S("Free Space", SensorType.Data)),
                Life = V(S("Life", SensorType.Level)),
                PowerOnHours = Positive(V(S("Power On Hours", SensorType.Factor))),
                PowerOnCount = Positive(V(S("Power On Count", SensorType.Factor))),
                DataReadGB = Positive(V(S("Data Read", SensorType.Data))),
                DataWrittenGB = Positive(V(S("Data Written", SensorType.Data))),
                ReadRate = V(S("Read Rate", SensorType.Throughput)),
                WriteRate = V(S("Write Rate", SensorType.Throughput)),
                Activity = V(S("Total Activity", SensorType.Load))
            };

            foreach (var a in sd.Attributes)
            {
                d.Smart.Add(new SmartRow { Id = a.Id, Name = a.Name ?? "", Raw = a.Value, Threshold = a.Threshold });
            }
            d.SmartAvailable = d.Smart.Count > 0 || tempValue.HasValue;

            JudgeHealth(d, st.IsNVMe);
            return d;
        }

        private static float? Positive(float? v) => v.HasValue && v.Value > 0 ? v : null;

        /// <summary>
        /// A plain-English verdict in the spirit of CrystalDiskInfo: any remapped, pending or unreadable sectors
        /// means "keep an eye on it"; Windows calling the disk unhealthy, or an NVMe critical warning, means "back it up now".
        /// </summary>
        private static void JudgeHealth(DriveInfo d, bool nvme)
        {
            var notes = d.HealthNotes;
            bool caution = false, bad = false;

            if (!nvme)
            {
                long? Raw(int id)
                {
                    var r = d.Smart.FirstOrDefault(x => x.Id == id);
                    // The low 32 bits hold the count; some drives pack other data in the high bytes.
                    return r == null ? null : (long)((ulong)r.Raw & 0xFFFFFFFF);
                }
                d.Reallocated = Raw(5);
                d.Pending = Raw(197);
                d.Uncorrectable = Raw(198);
                d.CrcErrors = Raw(199);

                if (d.Reallocated > 0) { caution = true; notes.Add($"{d.Reallocated} reallocated sector{(d.Reallocated == 1 ? "" : "s")}: the drive has swapped out bad areas."); }
                if (d.Pending > 0) { caution = true; notes.Add($"{d.Pending} sector{(d.Pending == 1 ? "" : "s")} waiting to be remapped (unstable)."); }
                if (d.Uncorrectable > 0) { caution = true; notes.Add($"{d.Uncorrectable} sector{(d.Uncorrectable == 1 ? "" : "s")} couldn't be read at all."); }
                if (d.CrcErrors > 0) notes.Add($"{d.CrcErrors} cable (CRC) errors. Usually a loose or poor SATA cable, not the drive.");
                if (d.Pending > 0 && d.Uncorrectable > 0) bad = true;
            }
            else
            {
                foreach (var r in d.Smart)
                {
                    string n = r.Name.ToLowerInvariant();
                    if (n.Contains("critical warning") && r.Raw > 0) { bad = true; notes.Add("The drive is reporting a critical warning."); }
                    else if (n.Contains("media") && n.Contains("error") && r.Raw > 0) { caution = true; notes.Add($"{r.Raw:0} media and data integrity errors."); d.Uncorrectable = (long)r.Raw; }
                }
            }

            if (d.Life.HasValue && d.Life.Value <= 10) { caution = true; notes.Add($"Only {d.Life:0}% of its rated life is left."); }
            if (d.WindowsHealth == "Unhealthy") { bad = true; notes.Add("Windows reports this disk as unhealthy."); }
            else if (d.WindowsHealth == "Warning") { caution = true; notes.Add("Windows reports a warning for this disk."); }

            if (!d.SmartAvailable)
            {
                d.Health = "Unknown";
                notes.Add("This drive doesn't share SMART data. Drives in some USB enclosures and RAID cards hide it.");
            }
            else d.Health = bad ? "Bad" : caution ? "Caution" : "Good";
        }

        private static SystemSummary Summarise(List<HardwareNode> nodes)
        {
            var sum = new SystemSummary { UptimeSeconds = Environment.TickCount64 / 1000.0 };

            var cpu = nodes.FirstOrDefault(n => n.Type == "Cpu");
            if (cpu != null)
            {
                sum.CpuName = cpu.Name;
                var t = Pick(cpu, "Temperature", "Core (Tctl/Tdie)", "CPU Package", "Package", "Core (Tdie)", "Core (Tctl)", "Core Average", "Core Max", "CPU Cores");
                t ??= cpu.Sensors.Where(s => s.Type == "Temperature" && s.Value > 0).OrderByDescending(s => s.Value).FirstOrDefault();
                sum.CpuTemp = t?.Value;
                sum.CpuTempSensor = t?.Id ?? "";
                sum.CpuLoad = Pick(cpu, "Load", "CPU Total")?.Value;
                sum.CpuPower = Pick(cpu, "Power", "CPU Package", "Package")?.Value;
                var clocks = cpu.Sensors.Where(s => s.Type == "Clock" && s.Name.StartsWith("Core #") && s.Value > 0).Select(s => s.Value.Value).ToList();
                if (clocks.Count > 0) sum.CpuClock = clocks.Average();
            }

            // A real graphics card beats the integrated one.
            var gpu = nodes.FirstOrDefault(n => n.Type == "GpuNvidia") ?? nodes.FirstOrDefault(n => n.Type == "GpuAmd") ?? nodes.FirstOrDefault(n => n.Type == "GpuIntel");
            if (gpu != null)
            {
                sum.GpuName = gpu.Name;
                var t = Pick(gpu, "Temperature", "GPU Core", "GPU Hot Spot");
                sum.GpuTemp = t?.Value;
                sum.GpuTempSensor = t?.Id ?? "";
                sum.GpuLoad = Pick(gpu, "Load", "GPU Core", "D3D 3D")?.Value;
                sum.GpuMemoryUsed = Pick(gpu, "SmallData", "GPU Memory Used", "D3D Dedicated Memory Used")?.Value;
                sum.GpuMemoryTotal = Pick(gpu, "SmallData", "GPU Memory Total", "D3D Dedicated Memory Total")?.Value;
                sum.GpuPower = Pick(gpu, "Power", "GPU Package", "GPU Power", "GPU Core")?.Value;
            }

            var mem = nodes.FirstOrDefault(n => n.Type == "Memory" && n.Name.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) < 0)
                      ?? nodes.FirstOrDefault(n => n.Type == "Memory");
            if (mem != null)
            {
                sum.MemoryLoad = Pick(mem, "Load", "Memory")?.Value;
                sum.MemoryUsed = Pick(mem, "Data", "Memory Used")?.Value;
                sum.MemoryAvailable = Pick(mem, "Data", "Memory Available")?.Value;
            }

            var board = nodes.Where(n => n.Type == "SuperIO" || n.Type == "Motherboard").SelectMany(n => n.Sensors)
                             .FirstOrDefault(s => s.Type == "Temperature" && s.Value > 0 &&
                                                  (s.Name.Contains("System") || s.Name.Contains("Motherboard") || s.Name.Contains("Mainboard")));
            sum.MotherboardTemp = board?.Value;
            return sum;
        }

        private static SensorReading Pick(HardwareNode node, string type, params string[] names)
        {
            foreach (var n in names)
            {
                var s = node.Sensors.FirstOrDefault(x => x.Type == type && x.Name == n && x.Value.HasValue);
                if (s != null) return s;
            }
            return null;
        }

        public void Dispose()
        {
            try { _computer.Close(); } catch { }
        }
    }
}
