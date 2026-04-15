# Icarus Server Manager

A Windows app that helps you install, run, and maintain an **Icarus Dedicated Server** without editing files by hand.

**Current release:** 1.0.1  
**Last tested with Icarus Dedicated Server build:** 217

---

## What this tool does (quick feature list)

- **One-click install/update** with bundled SteamCMD.
- **Simple server setup UI** for name, ports, prospect options, and other server settings.
- **Start/stop controls** with live console output.
- **Automatic restart policies** (crash recovery, interval restarts, empty-server, high-memory).
- **Live stats dashboard** (CPU, RAM, uptime, restart history, player hints, CSV export).
- **Discord notifications** with per-event toggles (optional).
- **Presets and config bundles** for backup/migration.
- **Light and dark mode**.

---

## Getting started (non-technical)

If this is your first time, follow these steps in order:

1. **Open Manager Settings**
   - At the top, set your **Server install folder**.
   - Use **Browse…** to pick the folder where Icarus server files should live.
   - You can also use **Setup wizard…** if you want guidance.

2. **Install or update the server**
   - Click **Install/Update Server**.
   - Wait until the console says install/update completed.

3. **Configure your server**
   - Go to **Server Settings**.
   - Fill in your server name, ports, and gameplay options.
   - If unsure, leave most defaults as-is at first.

4. **Save your settings**
   - Click **Save to INI** (game server settings).
   - Click **Save Manager Options** (manager app settings like restart rules, theme, Discord, console behavior).

5. **Start the server**
   - Click **Start Server**.
   - Watch the **Console** tab for startup messages.

6. **(Optional) Turn on automation**
   - In **Manager Settings**, enable restart policies and/or Discord notifications.

That is enough to get a working dedicated server running.

---

## Tabs explained in plain language

- **Console**: Live log output from the server and manager.
- **Server Settings**: Main game/server config. Also includes INI load/save, presets, and backups.
- **Last world**: Shows details from your selected prospect save.
- **Manager Settings**: App behavior (theme, automation, paths, Discord, schedule, console filters).
- **Stats**: Health and performance view (CPU, memory, uptime, restart and player info).

---

## Common tasks

### Change server name or ports
1. Open **Server Settings**.
2. Edit values.
3. Click **Save to INI**.
4. Restart server from the app.

### Back up your world/prospect files
- In **Server Settings**, use the prospect/world backup actions (ZIP or folder copy).

### Save a preset you can reuse later
- In **Server Settings**, save a preset profile, then load it anytime.

### Move setup to another machine
- Export a config bundle, copy it to the new PC, then import it.

---

## Discord notifications (optional)

In **Manager Settings** you can enable Discord webhooks and choose exactly which events post messages, such as:

- server started/stopped
- crash/unexpected exit
- restart warnings and restart failures
- scheduled update window reached
- possible player join/leave/chat/gameplay events
- heartbeat summaries

You can keep this very quiet or very detailed using the toggles.

---

## Console logging controls

The Console tab includes:

- **Logging presets** (Minimal, Balanced, Verbose, QuietGame)
- **Per-type toggles** to show/hide manager and game log categories

This only affects what you see in the app console. It does not remove data from disk log files.

---

## Requirements

- **Windows**
- **.NET 8 Desktop Runtime** (for running published builds)

If building from source, install the **.NET 8 SDK**.

---

## Where files are stored

- **Manager options:** `%LocalAppData%\IcarusServerManager\manager-options.json`
- **Manager logs:** `logs\manager-YYYYMMDD.log` (next to the executable)
- **Server INI (default):**
  `Icarus\Saved\Config\WindowsServer\ServerSettings.ini`

---

## Build from source (developer section)

```bash
dotnet restore IcarusServerManager.sln
dotnet build IcarusServerManager.sln -c Release
dotnet test IcarusServerManager.sln -c Release
```

Publish example:

```bash
dotnet publish IcarusServerManager/IcarusServerManager.csproj -c Release -r win-x64 --self-contained false -o ./publish
```

---

## CI and releases (developer section)

- CI workflow runs build + tests on pushes/PRs.
- Release workflow runs on `v*` tags and publishes win-x64 artifacts.

Example release tag:

```bash
git tag v1.0.1
git push origin v1.0.1
```

---

## Useful reference

- Icarus launch/server parameters wiki:  
  [RocketWerkz Icarus Dedicated Server Wiki](https://github.com/RocketWerkz/IcarusDedicatedServer/wiki/Server-Config-&-Launch-Parameters)

---

## License

See [LICENSE.md](LICENSE.md).
