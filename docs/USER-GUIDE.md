# Sector user guide

Everything Sector can do, and how to do it. If something isn't covered here,
[open an issue](https://github.com/bhxuk/sector/issues) and say what you were trying to do.

- [Getting started](#getting-started)
- [Watching your NAS](#watching-your-nas)
- [Overview](#overview)
- [Drives](#drives)
- [All sensors](#all-sensors)
- [History](#history)
- [Alerts](#alerts)
- [The icon by the clock](#the-icon-by-the-clock)
- [Settings](#settings)
- [How hot is too hot?](#how-hot-is-too-hot)
- [Troubleshooting](#troubleshooting)

---

## Getting started

1. Download **[Sector-Setup.exe](https://github.com/bhxuk/sector/releases/latest/download/Sector-Setup.exe)** and double-click it.
   Windows asks for permission because Sector installs a small background service.
2. If Windows shows **"Windows protected your PC"**, click **More info**, then **Run anyway**. This appears because
   the installer isn't code-signed, not because anything is wrong.
3. Leave **Install PawnIO** ticked. It's the driver that lets Sector read CPU temperatures, motherboard sensors and fans.
   Drive temperatures work without it.
4. On a PC you want to watch from elsewhere (your NAS), also tick **Let my other PCs see this PC's stats**.

Sector opens on the **Overview**. It lives by the clock (you may need to click the **^** arrow), showing your hottest
drive's temperature. Closing the window doesn't stop Sector, and the agent keeps recording history even when Sector
isn't open or nobody is signed in. To close the app fully, right-click the icon and choose **Exit Sector**.

## Watching your NAS

Sector runs on each PC you want to watch. The PC you sit at reads from the others over your network.

**On the NAS**

1. Install Sector with **Let my other PCs see this PC's stats** ticked. (Already installed? Open Sector > **Machines**
   and turn on **Let my other PCs see this PC**.)
2. In Sector > **Machines**, click **Copy** next to the pairing key. It looks like `K7QD-M2XP-...`.
   The page also lists the NAS's addresses, including its Tailscale address.

**On your desktop**

1. Open Sector > **Machines** (or **Add a PC or NAS** in the sidebar).
2. Type a name ("NAS"), the NAS's **address** and the **pairing key**, then click **Test and add**.

For the address, the Tailscale IP (`100.x.y.z`) or Tailscale machine name works from anywhere. On the same network
you can also use the NAS's local IP.

The NAS now appears in the sidebar under **Machines**, with a green dot when it's reachable and its hottest drive's
temperature beside it. Click it to see its pages. Alerts cover every PC in the list.

**Is it safe?** Sharing only accepts connections from your local network and Tailscale (the firewall rule is limited to
those addresses), every request needs the key, and other PCs can only read. Settings can only be changed on the PC itself.
To lock out a PC that has the key, click **New key**.

## Overview

- **At a glance**: the CPU's temperature, load, clock speed and power; the same for the graphics card; memory use and uptime;
  and the hottest drive, how many drives there are and whether they're all healthy. The small lines show the last 15 minutes.
- **Drives**: a card for each drive with its temperature (green, amber or red against your limits), the last 15 minutes,
  its peak since the agent started, health, space used and how long it has been powered on. Click a card for the details.
- **Temperatures** and **Fans**: every other temperature sensor and fan on the machine, grouped by the part they belong to.
  The bar under each fan shows its speed against the fastest it has gone.

## Drives

The table lists every drive with its temperature, health, size, space used, power-on time and bad sector count.
Click one to see:

- **Health** in plain words, with the reasons:
  - **Healthy**: no warning signs.
  - **Keep an eye on it**: the drive has swapped out bad areas (reallocated sectors), has areas waiting to be swapped
    (pending sectors), couldn't read something (unreadable sectors), is near the end of its rated life (SSDs), or Windows
    has a warning for it. Make sure it's backed up.
  - **Back it up now**: Windows reports it as unhealthy, the drive reports a critical warning, or it has both pending and
    unreadable sectors.
  - **Unknown**: the drive doesn't share SMART data. Drives in some USB enclosures and behind RAID cards hide it.
- **Temperature** chart for the last 24 hours, 7 days, 30 days or year, with your warning and critical lines.
- **About**, **Right now** and **Over its life**: model, serial, firmware, connection, drive letters, read and write
  speeds, how busy it is, power-on time, times switched on, data read and written, life left (SSDs), and the sector counts.
  **Cable errors** usually mean a loose or poor SATA cable rather than a failing drive.
- **Your settings for this drive**: give it a name ("Bay 2", "Films") that's used everywhere in Sector, and a warning
  temperature of its own.
- **SMART attributes**: the raw table, with the ones that matter most in bold.

## All sensors

Everything the hardware reports, grouped by part, with the current, lowest and highest values since the agent started.
Use the search box ("fan", "vrm", "core") and the filters (Temperatures, Fans, Load, Clocks, Power, Voltages, Data).
Hover a sensor's name to see its internal ID.

## History

Pick what to chart and the period:

- **Drive temperatures**: every drive on one chart. The dashed line is your hard drive limit.
- **CPU and graphics**: the CPU, graphics card and motherboard temperatures.
- **Fans**, **Drive activity** (how busy each drive was) and **Load** (CPU, graphics, memory).
- **Pick sensors**: tick any sensors that have history.

Hover the chart to read every line at that moment. Click a name under the chart to hide or show its line. With one line,
the shading shows the lowest and highest within each point. **Over this period** lists each line's lowest, average and
highest, and when the highest was.

The agent records every temperature, fan, fan control, CPU and graphics power, the main loads, and drive throughput.
It keeps a point every minute for 7 days, and a point every 15 minutes for a year (change this in Settings).
The history lives on each PC in `C:\ProgramData\Sector\history.db`.

## Alerts

Sector notifies you by the clock when:

- a **drive** goes over its warning temperature, and again if it reaches critical;
- the **CPU** or **graphics card** goes over its limit;
- a drive's **health** gets worse (new bad sectors, or Windows flags it);
- a **PC goes offline** for more than a few minutes, and when it's back;
- a **fan stops** (off by default; turn it on in Settings).

While a problem lasts you're reminded every 30 minutes (change this in Settings). When it clears, it's noted on the
**Alerts** page. Click any notification to open the page. **Right now** lists what's wrong at this moment across every PC.

Alerts need the Sector app running (it starts with Windows by default). The agent keeps recording either way.

## The icon by the clock

The icon shows a live number in a coloured frame: green is fine, amber is over your warning limit, red is critical.
Hover it for every drive's temperature. By default it shows the hottest drive on the PC with the most drives (your NAS,
once it's added). Choose CPU, GPU or the plain icon, and which PC, in **Settings > Behaviour**.

Left-click it to open Sector; right-click for Drives, History, Alerts and Exit.

## Settings

- **Look**: dark, light or matching Windows; six accent colours; the glass window on Windows 11; Celsius or Fahrenheit.
- **Behaviour**: start with Windows, how often the window refreshes, and what the icon by the clock shows.
- **Alerts**: limits for hard drives, SSDs, the CPU and graphics, which alerts you want, how often to remind you,
  and a test alert.
- **History on this PC**: how long to keep history, and where it's kept.
- **Updates and about**: update checks, automatic updates, the agent and PawnIO versions, and the log files.

## How hot is too hot?

These are the defaults. Change them in Settings, or per drive under Drives.

| | Warn | Critical | Why |
|---|---|---|---|
| Hard drives | 45°C | 50°C | Most are rated to 55 or 60°C, but they last longest below about 45°C. A NAS running drives in the high 40s for months is worth more airflow. |
| SSDs and NVMe | 65°C | 75°C | They run warmer, and slow themselves down at around 70 to 80°C to protect themselves. |
| CPU | 85°C | 95°C | Modern CPUs boost until they hit about 95 to 100°C, so short peaks are normal under load. |
| Graphics | 83°C | 90°C | Most cards slow down at around 83 to 90°C. |

For a hot NAS, the usual fixes are: a fan blowing directly across the drive cage, gaps between drives, cleaning dust
filters, and making sure the case pulls cool air in at the front and pushes it out at the back. The History page shows
whether a change made a difference.

## Troubleshooting

**"The Sector agent isn't installed / is stopped"**: click **Start the agent** on the banner (Windows asks for permission).
The service is called **Sector Agent** in Windows Services; it restarts itself if it ever stops unexpectedly.

**No CPU temperatures or fans**: install PawnIO from the banner on the Overview. If your motherboard still shows no fans,
it may not share them, or the fans may be plugged into a hub or the power supply, which can't be read.

**A drive says "No reading"**: it doesn't share SMART data, usually because it's in a USB enclosure or behind a RAID card.
Drives connected straight to the motherboard (SATA or NVMe) work best.

**Can't reach the NAS**: check that it's on, that Sector is installed there, that **Let my other PCs see this PC** is on
(Sector > Machines on the NAS), and that both PCs are on Tailscale or the same network. The message under the machine
on the Machines page says which of these it is. The port is 7311.

**Seeing what the agent sees**: run `"C:\Program Files\Sector\Sector.Agent.exe" --dump` in an administrator Command Prompt.
It prints every sensor and drive as JSON. Logs are in `C:\ProgramData\Sector\agent.log` (the agent) and
`%LocalAppData%\Sector\sector.log` (the app).

**Uninstalling**: use Windows Settings > Apps. It removes the service and the firewall rule, and asks whether to delete
the history too.
