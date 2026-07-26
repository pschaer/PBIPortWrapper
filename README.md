[![GitHub release](https://img.shields.io/github/v/release/pschaer/PBIPortWrapper)](https://github.com/pschaer/PBIPortWrapper/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

# PBI Port Wrapper

One stable HTTP XMLA endpoint over the Power BI Desktop models on this machine — so
Excel, Tabular Editor and other XMLA clients can connect to a local semantic model by
**name**, keep working across Desktop restarts, and reach it **from another computer,
as another user**.

```
Provider=MSOLAP;Data Source=http://your-pc:55555/Sales;Initial Catalog=Sales
```

## 🎯 Problem solved

Power BI Desktop's local Analysis Services engine is only intended for authoring:

- the **port** it listens on is random every session,
- the **database name** is a per-session GUID,
- and it only accepts the **logged-in Windows user**, over local TCP.

PBI Port Wrapper *serves* a semantic model instead: it renames that model's database
to a stable alias you choose, and publishes it on one HTTP XMLA endpoint at its own
path:

```
http://your-pc:55555/Sales      →  the Desktop instance holding Sales
http://your-pc:55555/Finance    →  the Desktop instance holding Finance
```

The address never changes, the name never changes, and callers authenticate to the
wrapper as themselves — allowing you to host semantic models in a private, stable 
and free way.

## ✨ Features

- ✅ **One endpoint, many models** — every served model on one port, addressed by name
- ✅ **Stable database name** — a *Serve Alias* per model, applied to the real database
  while serving, so saved workbook connections survive Desktop restarts
- ✅ **Works from other machines and other users** — the wrapper owns authentication;
  the engine only ever talks to its owner
- ✅ **Instant detection** — Desktop instances appear as they start (FileSystemWatcher)
- ✅ **On-detection policy per model** — do nothing, serve at once, or serve after a
  grace period with an *edit instead* escape hatch
- ✅ **Tray-first** — everything routine lives in the tray menu; the dashboard is for
  diagnostics and settings
- ✅ **Save .odc…** — hand a colleague a file they double-click to get an Excel
  PivotTable, no connection string typed or seen
- ✅ **Auto-start with Windows** — starts silently to the tray
- ✅ **Crash recovery** — if the wrapper dies mid-serve, the next start offers to
  resume or restore the original database name

> ⚠️ **While a model is served, Power BI Desktop shows "Cannot load model".** That is
> expected — its database has been renamed out from under it. Don't troubleshoot in
> Desktop; click **Stop** and it recovers immediately. Serving is a deliberate,
> serve-only session (see [docs/serving-workflow.md](docs/serving-workflow.md)).

## 📋 Requirements

- Windows 10/11
- Power BI Desktop (any version)

No additional runtime to install — .NET is included in the build.

## 🚀 Quick start

**1. Install**, either way:

- **Installer (recommended):** download `PBIPortWrapper.msi` and run it. It adds a
  Start Menu entry and registers the app on the Power BI Desktop **External Tools**
  ribbon. The MSI is unsigned, so approve SmartScreen's *"More info → Run anyway"*.
  See [docs/installer.md](docs/installer.md).
- **Portable ZIP:** extract and run `PBIPortWrapper.exe`.

**2. Name a model.** Start Power BI Desktop with your model; it appears in the grid.
Type a name in the **Alias** column — this is the stable name clients will use.

**3. Serve it.** Click **Serve** in the row's Action cell (or set **On detection** to
*Serve* and it happens by itself next time). The row shows *Serving*.

**4. Turn the endpoint on.** Tray icon → **XMLA endpoint → Enabled**. It is off by
default, because it exposes served models to the network.

**5. Connect.** Tray → the model → **Copy endpoint URL** (or **Save .odc…**), and
paste it into Excel or DAX Studio. Details below.

**6. When you're done**, click **Stop** — the original database name is restored and
Desktop is usable again.

## 🔌 Connecting from tools

The model's URL *is* the server address. The alias is the database on it.

### Excel

1. Data → Get Data → From Database → **From Analysis Services**
2. Server name: `http://your-pc:55555/Sales`
3. Authentication: **Use the following User Name and Password** — a Windows account
   **on the machine running the wrapper** (see *Authentication* below)
4. Select the database (it carries your alias)

Or skip all of that: tray → the model → **Save .odc…**, and double-click the file.

### Tabular Editor

Connect to `http://your-pc:55555/Sales` as an Analysis Services server. Reading and
writing both work.

### Same machine

`http://localhost:55555/Sales` works identically.

### Client compatibility

Any MSOLAP client should work — the endpoint relays XMLA rather than reimplementing
it — but not every client has been through a full session yet:

| Client | Status |
|---|---|
| Excel (local and remote) | Confirmed, including password sign-in |
| Tabular Editor | Confirmed, read and write |
| DAX Studio | Does not connect with password sign-in — under investigation |
| Power BI Desktop (Get Data → Analysis Services) | Fails with a DIME protocol error — under investigation |

## 🌐 Reaching it from another computer

Two one-time steps, both as Administrator:

**1. Let the endpoint bind all addresses.** Without this the listener silently falls
back to localhost — it looks healthy but no remote client can reach it. The tray menu
offers to copy this command when it detects that state:

```powershell
netsh http add urlacl url=http://+:55555/ user=Everyone
```

**2. Open the firewall:**

```powershell
New-NetFirewallRule -DisplayName "PBI Port Wrapper XMLA" -Direction Inbound -LocalPort 55555 -Protocol TCP -Action Allow
```

Then use the machine's name or IP instead of `localhost`.

## 🔑 Authentication

Set in the tray, or in the dashboard's endpoint settings:

| Mode | What it means |
|---|---|
| **Password sign-in** (default) | The caller supplies a Windows account **on this machine**; the password is verified against Windows. Works on a workgroup — just create a local account for them. |
| **Windows sign-in** | Integrated Windows authentication. Needs a **domain**; on a workgroup the handshake cannot complete and clients hang. |
| **No authentication** | Anyone who can reach the port can query **and modify** every served model. Isolated networks only. |

> ⚠️ **There is no TLS.** Password sign-in sends credentials base64-encoded, which is
> encoding, not encryption — and queries and results travel in the clear too. Use the
> endpoint on a trusted LAN only, never port-forward it from a router, and prefer a
> dedicated local account for remote callers. See
> [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md) §3. HTTPS is planned.

## ⚙️ Configuration

### Per model
- **Alias** — the stable database name, and the path clients address
- **On detection** — *Do nothing*, *Serve*, or *Serve after grace period*

### Endpoint (global)
- **Enabled**, **Port** (default 55555), **Hostname** (override the address shown in
  generated URLs, e.g. a DNS name or the right NIC), **Authentication**

### Files

```
%APPDATA%\PBIPortWrapper\config.json
%APPDATA%\PBIPortWrapper\log.txt      (rotates at 5 MB, keeps 5 files)
```

### Install as a Power BI Desktop External Tool

**The MSI does this for you.** For the portable ZIP:

1. Copy `pbiportwrapper.pbitool.json` to
   `\Program Files (x86)\Common Files\Microsoft Shared\Power BI Desktop\External Tools`
2. Edit its `path` to point at your `PBIPortWrapper.exe`
3. Restart Power BI Desktop

## 🐛 Known limitations

- ⚠️ **Desktop errors while serving** — expected; click **Stop** to restore it
- ⚠️ **No encryption on the endpoint** — trusted LAN only (see above)
- ⚠️ **Conservative unsaved-changes check** — serving may ask for confirmation even
  right after a save
- ⚠️ **Unsigned installer** — SmartScreen warns on first run; *More info → Run anyway*

See [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md) for the full write-ups.

## 🗺️ Roadmap

### v0.1 – v0.4 ✅
Port-forwarding proxy: multi-instance support, per-instance settings, tray, structured
logging, and the headless `PBIPortWrapper.Core` library.

### v0.5 / v0.5.1 ✅
Serve sessions — stable database names via a per-model alias, crash recovery,
unsaved-changes preflight, single-instance guard.

### v0.6.0 ✅
Windows MSI installer with Start Menu entry and automatic External Tool registration;
silent install ([docs/installer.md](docs/installer.md)).

### v0.7.0 / v0.7.1 ✅
Tray-first workflow, auto-serve with a per-model on-detection policy, grid ↔ tray
convergence, auto-start with Windows, `.odc` export.

### v0.8.0 ✅ (this release)
- **XMLA-over-HTTP endpoint** — one address for every served model, each on its own
  path, reachable from other machines and other users
- **Port forwarding retired** — the alias replaced the fixed port entirely
- Endpoint settings in the tray and the dashboard; password sign-in verified against
  Windows

### v1.0
- Access logging (who connected, to which model)
- Read-only mode for `Execute`
- Documentation pass and release hardening

### v1.x
- HTTPS for the endpoint
- XMLA capture diagnostic
- First-run experience

## 📄 License

MIT — see [LICENSE.txt](LICENSE.txt).

## ⚠️ Disclaimer

This is an unofficial tool and is not affiliated with, endorsed by, or supported by
Microsoft Corporation. Use at your own risk.

---

**Made with ❤️ for the Power BI community**
