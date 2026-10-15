# What's new

## 1.1.0 (15 October 2026)

- **Updates in one click, on every PC.** When a new version is out, Sector tells you once. Click the notification,
  or **Update all** in Sector, and your NAS and this PC both update themselves, then Sector reopens. There's no
  installer to run and no Windows permission prompt: the Sector agent does the install, so it works on a NAS
  nobody is signed in to.
- **Automatic updates**, switched on per PC (Settings for this PC, Machines for the others).
- Each PC's Sector version is shown under Machines, with an **Update** button for it.
- Sector says when an update has finished, with a link to what's new.
- Updates keep your sharing setting as it is, instead of turning sharing back on.
- The graphics tile on the Overview is hidden on PCs whose graphics report no temperature.
- A new website: [bhxuk.github.io/Sector](https://bhxuk.github.io/Sector/).

Updating from 1.0.0: click the update notification as usual. That one time, Windows asks for permission, because
1.0.0's agent can't install updates itself. Every update after that is one click with no prompt. Your NAS needs
updating once from Sector on the NAS itself; after that it can be updated from your desk.

## 1.0.0 (15 October 2026)

The first version of Sector.

- **Sector agent**, a small Windows service that reads every sensor every 2 seconds and keeps a year of history,
  even when nobody is signed in.
- **Overview** with the CPU, graphics card, memory, a card for every drive, and every temperature and fan.
- **Drives**: health in plain English, full SMART attributes, power-on time, data written, bad sectors, cable errors,
  temperature history, and your own names and limits for each drive.
- **All sensors**, searchable and filterable, with lowest and highest values.
- **History** charts from an hour to a year: drive temperatures, CPU and graphics, fans, drive activity, load, or any
  sensors you pick, with hover readouts and lowest, average and highest for the period.
- **Watch other PCs**, like a home-made NAS, over your network or Tailscale with a pairing key.
- **Alerts** for hot drives, CPUs and graphics cards, drive health changes, fans that stop and PCs that go offline.
- **A live temperature by the clock**, green, amber or red against your limits.
- **PawnIO** driver included for CPU temperatures and fan speeds.
- Celsius or Fahrenheit, light and dark themes, six accents, the glass window on Windows 11, and automatic updates.
