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

## 💡 Use Cases

**Personal analytics with sensitive data**  
You have built a well-groomed dimensional model in Power BI Desktop — tracking personal finances, health data, or any other private information — as a learning exercise or for your own analysis. The model is solid, but Excel is the better tool for open-ended exploration and ad-hoc what-ifs. Serve it locally and connect Excel to a stable endpoint. Your data stays on your machine; nothing transits a cloud service or shared gateway.

**Offline deep-dive on the road**  
A semantic model is handed to you for validation, comparison against reference data, or simply to make sense of a complex dataset before you leave on a long trip. You will be without reliable internet — or you choose not to trust the connection you have. Serve the model before you go, then work with it in Excel, DAX Studio, or Tabular Editor for days or weeks entirely offline. No VPN, no re-authentication, no broken connections when the gateway drops.

## ✨ Features

- ✅ **One endpoint, many models** — every served model on one port, addressed by name
- ✅ **Stable database name** — a *Serve Alias* per model, applied to the real database
  while serving, so saved workbook connections survive Desktop restarts
- ✅ **Works from other machines and other users** — the wrapper owns authentication;
  the engine only ever talks to its owner
- ✅ **HTTPS** — bring a certificate you already have; renewals are picked up without a
  restart
- ✅ **Read-only by default** — mutating XMLA commands are refused unless you allow them
  per model
- ✅ **Access log** — who connected, to which model, when, and how it went
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

### DAX Studio

The server box on the connect dialog connects with integrated security, so it never
answers a password challenge — with **Password sign-in** it fails with `(401)
Unauthorized`. Use the connection-string option instead:

```
Data Source=http://your-pc:55555/Sales;User ID=<windows account on the wrapper's machine>;Password=<password>;
```

With **No authentication** the plain server box works.

### Tabular Editor

Connect to `http://your-pc:55555/Sales` as an Analysis Services server. Reading works
straight away. **Writing needs Read-only cleared for that model** — see below.

### Encryption

Off by default; everything below is about turning it on.

The short way: **XMLA endpoint… → Encrypt connections (HTTPS)**, pick where your
certificate comes from, browse to it. The dialog says what it resolves to — subject and
expiry — and refuses to switch encryption on until it resolves, rather than letting the
endpoint fail to start later.

The same settings live in `%APPDATA%\PBIPortWrapper\config.json`:

The usual case — the two files a Let's Encrypt client or a reverse proxy such as Nginx
Proxy Manager gives you:

```json
"HttpBridge": {
  "UseHttps": true,
  "CertificatePath": "C:\\path\\to\\fullchain.pem",
  "CertificateKeyPath": "C:\\path\\to\\privkey.pem"
}
```

Two alternatives: a certificate already in the Windows certificate store
(`"CertificateThumbprint": "A1B2C3..."`), or a PFX file carrying its private key
(`CertificatePath` on its own).

**Prefer the PEM pair if your certificate renews automatically.** A renewed certificate
is a different certificate with a different thumbprint, so the other two routes need a
re-import or a re-conversion every sixty days. Two files rewritten in place by whatever
already renews them are picked up on their own.

The app uses a certificate you already have — it never creates one. A certificate it
generated itself would be trusted by nobody, and every client machine would need it
installed by hand. One from a CA your clients already trust needs no client-side work at
all.

There is no setting for a certificate password on purpose: that would be a stored
credential in clear text, in the feature whose whole point is confidentiality. So the PEM
key must not be passphrase-protected — and a protected PFX belongs in the Windows
certificate store, used by its thumbprint.

Renewals are picked up automatically, within minutes and without a restart — which
matters, because a certificate that renews every sixty days on an app that runs for
months would otherwise go stale and look like *"clients suddenly cannot connect"*.

### Read-only

Every model is served **read-only by default**: the endpoint refuses XMLA commands that
would change it (`Alter`, `Delete`, `Backup`, a TMSL script, and so on) with a fault
naming the command, and never passes them to the engine. Queries are unaffected.

That default exists because on `No authentication` — and on plain HTTP generally —
anyone who reaches the port would otherwise be able to alter or delete your model, not
just read it.

To allow write-back, clear **Read-only** for that model — in the grid's Read-only
column, or tray → the model → **Read-only**. It is per model, so one model can stay
writable while the rest do not.

### Power BI Desktop

Get Data → Analysis Services, server `http://your-pc:55555/Sales`. Works with
**No authentication**.

It does **not** work with **Password sign-in**: its connector does not answer the
password challenge, and the error it reports is unhelpfully indirect — either a timeout
or `DIME protocol error: The '9' DIME version is not supported`. That number is the
giveaway rather than a red herring: a DIME frame's first byte carries the version in its
top five bits, and an HTTP response begins `"HTTP/1.1 …"` where `'H'` is `0x48` —
`0x48 >> 3` is exactly 9. It is reading the challenge response as if it were data.

Use another client if you need password sign-in. Note that Power BI Desktop is the tool
that *hosts* your model in the first place — it is rarely the one you need to read it
with.

### Who is using my models

Every request is recorded in `access.csv`, next to the log in
`%APPDATA%\PBIPortWrapper\` - timestamp, caller, client, model, what was asked, how it
went, how long it took. Tray -> **XMLA endpoint** -> **Access log**, or the dashboard's
**XMLA endpoint...** dialog, opens it in Excel - as a copy, so that reading the log does
not stop it recording.

It is on by default and safe to leave on: it records *that* a query ran, never the query
or its results. (That is `LogPayloads`, a separate debugging switch that is off by
default and should stay off.)

### Same machine

`http://localhost:55555/Sales` works identically.

### Client compatibility

Any MSOLAP client should work — the endpoint relays XMLA rather than reimplementing
it — but not every client has been through a full session yet:

| Client | No authentication | Password sign-in |
|---|---|---|
| Excel (local and remote) | Confirmed | Confirmed |
| DAX Studio | Confirmed | Confirmed — needs the full connection string above, not the server box |
| Tabular Editor | Confirmed | Confirmed — reads; writes once Read-only is cleared |
| Power BI Desktop | Confirmed | Not working — see below |

Every one of these is MSOLAP or ADOMD.NET underneath, so what differs between them is
how each is *told* to connect, not what they speak.

## 🌐 Reaching it from another computer

One one-time step, as Administrator:

**Open the firewall:**

```powershell
New-NetFirewallRule -DisplayName "PBI Port Wrapper XMLA" -Direction Inbound `
  -Protocol TCP -LocalPort 55555 -Action Allow -Profile Domain,Private
```

`-Profile Domain,Private` keeps the port closed on networks Windows classes as public,
so a laptop on hotel or cafe Wi-Fi does not start offering your model to the room.
Leaving the profile out defaults to *all* networks, which is not what you want from an
endpoint carrying your data — encrypted or not.

There used to be a second step - a `netsh http add urlacl` reservation, without which
the endpoint quietly served localhost only and no remote client could reach it. That is
gone: the endpoint no longer runs on http.sys, so it binds every address as an ordinary
user.

Then use the machine's name or IP instead of `localhost`.

## 🔑 Authentication

Set in the tray, or in the dashboard's endpoint settings:

| Mode | What it means |
|---|---|
| **Password sign-in** (default) | The caller supplies a Windows account **on this machine**; the password is verified against Windows. Works on a workgroup — just create a local account for them. |
| **No authentication** | Anyone who can reach the port can query every served model, and change any whose **Read-only** you have cleared. Isolated networks only. |

*Windows sign-in (Negotiate) was offered until v0.8 and has been removed: it needs a
domain, and on a workgroup the handshake never completed, so clients hung instead of
reporting an error. An existing config that asks for it now uses password sign-in rather
than silently authenticating nobody.*

> ⚠️ **Encryption is off until you turn it on.** On plain HTTP, password sign-in sends
> credentials base64-encoded — encoding, not encryption — and queries and results travel
> in the clear too. Either [turn HTTPS on](#encryption), or use the endpoint on a trusted
> LAN only: never port-forward it from a router, and prefer a dedicated local account for
> remote callers. See [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md) §3.

## ⚙️ Configuration

### Per model
- **Alias** — the stable database name, and the path clients address
- **On detection** — *Do nothing*, *Serve*, or *Serve after grace period*

### Endpoint (global)
- **Enabled**, **Port** (default 55555), **Hostname** (override the address shown in
  generated URLs, e.g. a DNS name or the right NIC), **Authentication**
- **Encrypt connections (HTTPS)** and where the certificate comes from
- **Access log** — record every request

All of them are in the tray and in the dashboard's **XMLA endpoint…** dialog, and the
two always agree: each writes the same setting and the endpoint follows.

### Files

```
%APPDATA%\PBIPortWrapper\config.json
%APPDATA%\PBIPortWrapper\log.txt      (rotates at 5 MB, keeps 5 files)
%APPDATA%\PBIPortWrapper\access.csv   (one line per request, when the access log is on)
```

### Install as a Power BI Desktop External Tool

**The MSI does this for you.** For the portable ZIP:

1. Copy `pbiportwrapper.pbitool.json` to
   `\Program Files (x86)\Common Files\Microsoft Shared\Power BI Desktop\External Tools`
2. Edit its `path` to point at your `PBIPortWrapper.exe`
3. Restart Power BI Desktop

## 🐛 Known limitations

- ⚠️ **Desktop errors while serving** — expected; click **Stop** to restore it
- ⚠️ **Encryption is off by default** — turn HTTPS on, or trusted LAN only (see above)
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

### v0.8.0 ✅
- **XMLA-over-HTTP endpoint** — one address for every served model, each on its own
  path, reachable from other machines and other users
- **Port forwarding retired** — the alias replaced the fixed port entirely
- Endpoint settings in the tray and the dashboard; password sign-in verified against
  Windows

### v0.9.0 ✅ (this release)
- **HTTPS** — bring your own certificate, from a PEM pair, the Windows certificate
  store or a PFX; renewals picked up without a restart
- **Read-only by default** — the endpoint refuses mutating commands unless you clear
  Read-only for that model
- **Access logging** — who connected, to which model, when, and how it went
- The URL reservation is gone: the firewall rule is now the only manual step

### v1.0
- Confidence rather than scope: HTTPS proven in use, including a certificate renewal
- First-run experience (#37)
- Release hardening

### v1.x
- XMLA capture diagnostic (#133)
- Windows sign-in, if the host is ever domain-joined (#164)

## 📄 License

MIT — see [LICENSE.txt](LICENSE.txt).

## ⚠️ Disclaimer

This is an unofficial tool and is not affiliated with, endorsed by, or supported by
Microsoft Corporation. Use at your own risk.

---

**Made with ❤️ for the Power BI community**
