using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sector.Shared
{
    /// <summary>What the agent says about itself.</summary>
    public class AgentInfo
    {
        public string Machine { get; set; } = "";
        public string Os { get; set; } = "";
        public string AgentVersion { get; set; } = "";
        public bool PawnIoInstalled { get; set; }
        public string PawnIoVersion { get; set; } = "";
        public bool AllowRemote { get; set; }
        public int Port { get; set; }
        public double UptimeSeconds { get; set; }
        public DateTime StartedUtc { get; set; }
        public int HistoryDays { get; set; }
    }

    /// <summary>Everything the agent can see right now.</summary>
    public class Snapshot
    {
        public string Machine { get; set; } = "";
        public DateTime TimeUtc { get; set; }
        public List<HardwareNode> Hardware { get; set; } = new List<HardwareNode>();
        public List<DriveInfo> Drives { get; set; } = new List<DriveInfo>();
        public SystemSummary Summary { get; set; } = new SystemSummary();
        public bool PawnIoInstalled { get; set; }
    }

    /// <summary>The headline numbers, worked out on the agent so every client agrees.</summary>
    public class SystemSummary
    {
        public string CpuName { get; set; } = "";
        public float? CpuTemp { get; set; }
        public float? CpuLoad { get; set; }
        public float? CpuClock { get; set; }
        public float? CpuPower { get; set; }
        public string GpuName { get; set; } = "";
        public float? GpuTemp { get; set; }
        public float? GpuLoad { get; set; }
        public float? GpuMemoryUsed { get; set; }   // MB
        public float? GpuMemoryTotal { get; set; }  // MB
        public float? GpuPower { get; set; }
        public float? MemoryLoad { get; set; }
        public float? MemoryUsed { get; set; }       // GB
        public float? MemoryAvailable { get; set; }  // GB
        public float? MotherboardTemp { get; set; }
        public double UptimeSeconds { get; set; }
        public string CpuTempSensor { get; set; } = "";
        public string GpuTempSensor { get; set; } = "";
    }

    public class HardwareNode
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";       // Cpu, GpuNvidia, Storage, Motherboard, SuperIO...
        public string Parent { get; set; } = "";
        public List<SensorReading> Sensors { get; set; } = new List<SensorReading>();
    }

    public class SensorReading
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";       // Temperature, Fan, Load, Clock...
        public float? Value { get; set; }
        public float? Min { get; set; }
        public float? Max { get; set; }
    }

    /// <summary>One physical drive: identity from Windows, live numbers and SMART from the drive itself.</summary>
    public class DriveInfo
    {
        public int Number { get; set; }
        public string HardwareId { get; set; } = "";
        public string Model { get; set; } = "";
        public string Serial { get; set; } = "";
        public string Firmware { get; set; } = "";
        public string Media { get; set; } = "";      // HDD, SSD, NVMe, Unknown
        public string Bus { get; set; } = "";        // SATA, NVMe, USB, SAS, RAID...
        public double SizeGB { get; set; }
        public string Letters { get; set; } = "";    // "D: E:"
        public string Health { get; set; } = "";     // Good, Caution, Bad, Unknown (from SMART)
        public string WindowsHealth { get; set; } = ""; // Healthy, Warning, Unhealthy, Unknown
        public List<string> HealthNotes { get; set; } = new List<string>();
        public float? Temperature { get; set; }
        public float? TemperatureMin { get; set; }
        public float? TemperatureMax { get; set; }
        public float? WarningTemperature { get; set; }
        public float? CriticalTemperature { get; set; }
        public string TemperatureSensor { get; set; } = "";
        public float? UsedPercent { get; set; }
        public float? FreeGB { get; set; }
        public float? Life { get; set; }             // SSD life left %
        public float? PowerOnHours { get; set; }
        public float? PowerOnCount { get; set; }
        public float? DataReadGB { get; set; }
        public float? DataWrittenGB { get; set; }
        public float? ReadRate { get; set; }         // bytes/s
        public float? WriteRate { get; set; }        // bytes/s
        public float? Activity { get; set; }         // %
        public long? Reallocated { get; set; }
        public long? Pending { get; set; }
        public long? Uncorrectable { get; set; }
        public long? CrcErrors { get; set; }
        public bool SmartAvailable { get; set; }
        public List<SmartRow> Smart { get; set; } = new List<SmartRow>();
    }

    public class SmartRow
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public double Raw { get; set; }
        public int Threshold { get; set; }
    }

    public class HistoryRequest
    {
        public List<string> Ids { get; set; } = new List<string>();
        public DateTime FromUtc { get; set; }
        public DateTime ToUtc { get; set; }
    }

    public class HistorySeries
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Hardware { get; set; } = "";
        public int StepSeconds { get; set; }
        public List<HistoryPoint> Points { get; set; } = new List<HistoryPoint>();
    }

    public class HistoryPoint
    {
        /// <summary>Unix seconds, UTC.</summary>
        public long T { get; set; }
        public float Min { get; set; }
        public float Avg { get; set; }
        public float Max { get; set; }
    }

    public class HistorySensor
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public string Hardware { get; set; } = "";
        public long FirstSeen { get; set; }
        public long LastSeen { get; set; }
    }

    public class AgentSettingsChange
    {
        public bool? AllowRemote { get; set; }
        public int? HistoryDays { get; set; }
        public bool? NewKey { get; set; }
    }

    public static class Json
    {
        public static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
            WriteIndented = false
        };
    }

    public static class Constants
    {
        public const int DefaultPort = 7311;
        public const string KeyHeader = "X-Sector-Key";
        public const string ServiceName = "SectorAgent";

        /// <summary>C:\ProgramData\Sector: shared by the agent (writes) and the app on the same PC (reads the key).</summary>
        public static string DataFolder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Sector");
        public static string AgentConfigPath => System.IO.Path.Combine(DataFolder, "agent.json");
    }

    /// <summary>The agent's settings file. The app on the same PC reads the key and port from it.</summary>
    public class AgentConfig
    {
        public int Port { get; set; } = Constants.DefaultPort;
        public string Key { get; set; } = "";
        public bool AllowRemote { get; set; } = false;
        public int HistoryDays { get; set; } = 365;
        public int PollMilliseconds { get; set; } = 2000;

        public static AgentConfig Load()
        {
            try
            {
                if (System.IO.File.Exists(Constants.AgentConfigPath))
                {
                    var c = JsonSerializer.Deserialize<AgentConfig>(System.IO.File.ReadAllText(Constants.AgentConfigPath), Json.Options);
                    if (c != null) return c;
                }
            }
            catch { }
            return new AgentConfig();
        }
    }
}
