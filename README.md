<div align="center">

<img src="src/Sector/Assets/sector.ico" alt="Sector icon: an amber gauge needle near the red zone" width="96">

# Sector

**Every temperature, fan and drive in your PCs, and your NAS, in one place.**

Live CPU, graphics, motherboard and drive temperatures, fan speeds, loads and power for Windows 10 and 11.
Full SMART health for every hard drive and SSD, a year of history in charts, and alerts when anything runs hot.
Watch a home-made Windows NAS from your desktop over your network or Tailscale.

**[Download Sector for Windows](https://github.com/bhxuk/sector/releases/latest/download/Sector-Setup.exe)**  ·  [User guide](docs/USER-GUIDE.md)  ·  [What's new](CHANGELOG.md)

</div>

---

## What's inside

| Page | What it shows |
|---|---|
| **Overview** | CPU, graphics and memory at a glance, a card for every drive with its temperature, last 15 minutes, health and space, and every temperature sensor and fan on the machine. |
| **Drives** | All drives in one table, and for each one: health in plain English, SMART attributes, power-on time, data written, bad sectors, cable errors, its temperature history, your own name for it ("Bay 2") and its own alert limit. |
| **All sensors** | Everything the hardware reports (temperatures, fans, clocks, voltages, power, loads) with current, lowest and highest values. Search and filter. |
| **History** | Charts of drive temperatures, CPU and graphics, fans, drive activity and load, from the last hour to the last year. Hover for exact values; see the lowest, average and highest for the period. |
| **Alerts** | What needs attention right now on any PC, and everything Sector has warned you about. |
| **Machines** | This PC's pairing key and sharing switch, and the other PCs (like your NAS) that Sector watches. |

And across the app: a **live temperature by the clock** (the hottest drive by default, turning amber and red near your limits),
**notifications** for hot drives, CPUs and graphics cards, drive health changes, fans that stop and PCs that go offline,
**Celsius or Fahrenheit**, **light and dark themes** with the same accents and glass window as [Arrakis](https://github.com/bhxuk/arrakis),
and **automatic updates**.

## How it works

Sector has two parts, and the installer sets up both:

- **Sector agent**: a small Windows service that reads the sensors every 2 seconds, even when nobody is signed in,
  and keeps the history (minute by minute for 7 days, then every 15 minutes for a year).
- **Sector**: the app you look at. It reads from the agent on this PC, and from the agents on your other PCs.

```
 Your desktop                                 Your NAS
 ┌──────────────┐   local network or     ┌──────────────────┐
 │  Sector app  │ ─────── Tailscale ───▶ │  Sector agent    │──▶ drive SMART, temps, fans
 │      │       │   (pairing key)        │  + history       │
 │      ▼       │                        └──────────────────┘
 │ Sector agent │──▶ this PC's sensors
 └──────────────┘
```

## Watching your NAS

1. Run **Sector-Setup.exe on the NAS**. Tick **"Let my other PCs see this PC's stats"**.
2. Open Sector on the NAS, go to **Machines** and copy the **pairing key**.
3. On your desktop, open Sector > **Machines** > **Add a PC or NAS**. Type the NAS's Tailscale name or IP and the key.

The NAS then appears in the sidebar with its hottest drive temperature beside it. Sharing only accepts your local network
and Tailscale addresses, and every request needs the key.

## CPU temperatures and fans

Drive temperatures and SMART work straight away. CPU temperatures, motherboard sensors and fan speeds need
**PawnIO**, a small signed driver made for monitoring tools. The installer offers to install it (recommended), and
Sector shows a one-click install if it's missing. It replaces the older WinRing0 driver that Windows Defender now blocks.

## Building it yourself

You need the .NET 8 SDK and Inno Setup 6 on Windows.

```
dotnet publish src/Sector/Sector.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/app
dotnet publish src/Sector.Agent/Sector.Agent.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/agent
iscc /DMyAppVersion=1.0.0 /DPublishDir=..\publish installer\Sector.iss
```

To see what a PC's sensors report without installing anything, run `Sector.Agent.exe --dump` as administrator.

Every push to `main` builds Setup.exe on GitHub Actions; a new version number in `src/Sector/Sector.csproj` publishes a release.

## Credits

Sensor readings come from [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) (MPL 2.0).
See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Sector itself is MIT licensed.
