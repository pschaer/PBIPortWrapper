# Changelog

All notable changes to PBIRelay will be documented in this file. Releases up to and
including 0.9.0 shipped under the project's former name, PBI Port Wrapper, and their
entries below say so deliberately.

## [1.0.0] - 2026-08-16

No new scope over 0.9.0. The project got a name that describes it, and the HTTPS design
was finally proven in use rather than in tests. Scope earned the numbers up to here; use
earned this one.

### Changed
- **Renamed to PBIRelay** (#174, #184). "Port" described a transport retired in 0.8
  (#126) — there is no forwarded port and no fixed port per model. Namespaces, assembly
  names, the installer, the External Tools manifest and the configuration folder all
  move. `%APPDATA%\PBIPortWrapper\` becomes `%APPDATA%\PBIRelay\`, and **nothing
  migrates it**: copy `config.json` across by hand and repoint any absolute paths inside
  it. The MSI keeps its UpgradeCode, so an existing install is upgraded in place rather
  than installed beside the old one.
- The README now states what the tool is for, what it is deliberately not, and the
  licensing posture toward Microsoft (#173, #175).

### Fixed
- The MIT licence in the installer rendered with a gap after every line of the source
  file, because each hard wrap had become its own paragraph. It now flows properly.

### Verified
- **A certificate renewal, in use** — the one thing 1.0 was waiting on. A PEM pair
  replaced in place was picked up inside the five-minute recheck window, with no
  restart, no configuration change and no downtime. The replaced file kept its old
  modification time, so this also proved the reload does not depend on timestamps: it
  re-reads on a timer and compares thumbprints.

## [0.9.0] - 2026-07-27

v0.8 made the endpoint reachable. This release makes it defensible: it can be
encrypted, it refuses to change your models unless you say so, and it tells you
who has been using them.

### Added
- **HTTPS for the endpoint** (#132). Off by default — it needs a certificate, and
  an endpoint that stopped answering after an upgrade would be a worse failure
  than one that is not yet encrypted. The app *consumes* a certificate and never
  creates one: being trusted is the hard half of TLS, and a self-signed
  certificate would need installing by hand on every client machine.
  Three sources — a **PEM pair** (`fullchain.pem` + `privkey.pem`, what Let's
  Encrypt clients and reverse proxies emit), a **Windows certificate store
  thumbprint**, or a password-free **PFX**
- **Renewal without a restart.** The certificate is chosen per connection and its
  source re-read at most every five minutes, so a certificate replaced in place is
  live within minutes. A re-read that fails keeps the one already in hand, because
  that is what a renewal looks like while the file is being written. Startup names
  the certificate and its expiry, and warns inside fourteen days
- **Certificate settings in the dashboard** — **XMLA endpoint… → Encrypt
  connections (HTTPS)**, with a source picker, a file browser, and a line saying
  what the settings resolve to. Encryption will not switch on while the
  certificate does not resolve, and says why
- **Read-only serving, by default** (#129). The endpoint refuses XMLA commands
  that would change a model — `Alter`, `Delete`, `Backup`, TMSL scripts — with a
  fault naming the command, and never passes them to the engine. Clear
  **Read-only** per model to allow write-back
- **Access logging** (#128). One line per request in `access.csv` next to the log:
  who connected, from where, with which client, to which model, what was asked and
  how it went. On by default and safe to leave on — it records *that* a query ran,
  never the query or its results. Opens in Excel as a copy, so reading it does not
  stop it recording
- **The caller is named in the log** (#149) — including a rejected one, which
  previously reached nothing that could record it

### Changed
- **The URL reservation is gone.** Remote access needed `netsh http add urlacl`,
  and skipping it produced the sharpest failure in the design: the endpoint
  reported itself running and every remote client simply could not connect. The
  endpoint no longer runs on http.sys, so it binds every address as an ordinary
  user (#132). The firewall rule is now the only manual step
- **Connection strings, `.odc` files and copied URLs follow the scheme** — a
  hard-coded `http` would have handed out addresses that cannot connect the moment
  HTTPS came on, each one looking perfectly correct
- **Windows sign-in (Negotiate) is no longer offered** (#164). It required a
  domain; on a workgroup the handshake never completed, so clients hung rather
  than reporting an error. A stored configuration asking for it now resolves to
  password sign-in — never to no authentication
- The grid's context menu acts on the row you clicked rather than the selected
  one (#151)

### Fixed
- **An endpoint that failed to start said so.** The reason reached `log.txt`, the
  tray label and the settings dialog — all of which had to be gone looking for,
  while the symptom was a client that would not connect. It is now announced,
  once per distinct failure (#132)
- **Certificate and HTTPS settings restart the listener.** Without that, switching
  encryption on kept the listener on plain HTTP while every published URL became
  `https://` (#132)
- Reading the access log no longer stops it recording (#128)
- A `Discover` is a read wherever it appears, including nested in a `Batch`, so
  read-only no longer refuses legitimate client traffic (#129)
- The serving toast copies the connection string rather than the bare alias

## [0.8.0] - 2026-07-26

The release where a model stops being addressed by a *port* and starts being
addressed by its *name*.

### Added
- **One XMLA-over-HTTP endpoint for every served model** - each served model
  answers on its own path of a single port
  (`http://your-pc:55555/Sales`), so one address, one firewall rule and one
  authentication setting cover all of them. A path is one server holding one
  engine, which is what makes XMLA sessions work natively across several open
  models (#77, #122, #123, #136)
- **Access from other machines and other Windows users.** Power BI Desktop's
  engine only ever accepts its owner over local TCP; the wrapper now owns
  authentication on the remote leg and talks to the engine as itself. This is
  the gap port forwarding could never close (#77)
- **Password sign-in** (default), Windows sign-in and no-authentication modes.
  Password sign-in verifies credentials against a real Windows account on the
  host, so it works on a workgroup - the common case (#122, #140)
- **Endpoint settings in the tray and the dashboard** - enable, port, hostname
  override, authentication, status and restart, editable from either surface
  and always in agreement (#125)
- **Copy endpoint URL** per served model, and `.odc` files that carry it (#126)

### Changed
- **The alias replaced the fixed port.** The grid's *Fixed Port* column is now
  an editable **Alias**; the *Network* column is gone (reachability is one
  endpoint-wide setting); the *Action* cell is a direct **Serve** / **Stop**
  button. A model needs a name, not a port (#126)
- **The two states are Off and Serve.** *Forward* is gone as a state, an action
  and an on-detection policy (#126, #127)
- `config.json` calls the model list **`Models`** instead of `PortMappings`, and
  the per-model `FixedPort` / `AllowNetworkAccess` fields are gone. Existing
  files load unchanged and keep their aliases and policies (#130)
- Endpoint log noise dropped to Debug; failures still log at Warning and above
  (#141)

### Removed
- **TCP port forwarding**, and with it the proxy, port assignment and
  validation, the *Set Port* UI, per-model network access, the Active
  connections column and the row-details connections panel. A forwarded port
  gave no stable database name and only ever worked for the same Windows user
  on the same machine - everything it did, the endpoint does better (#126)

### Fixed
- **HTTP Basic accepted any password.** `HttpListener` decodes the credential
  header but does not verify it, which was misread as validation: a nonexistent
  user with an invented password was admitted exactly like a real one. The
  password is now checked against Windows directly (#140)
- Serving no longer fails, or rolls back a completed rename, because a TCP port
  happened to be busy - it binds nothing at all (#126)
- Row expansion crashed after the connections panel was removed (#126)
- Endpoint status no longer claims "LAN" when it means every interface, and
  names the exception - `this machine only` - instead (#142)
- The "serving" toast offered to copy the connection string and copied the bare
  alias; it now copies the real connection string, and says so plainly when the
  endpoint is off and there is nothing to copy
- The row details panel dropped the *Serve Alias* editor (the alias is a grid
  column since #126) and a `Serving as 'X' on port 0` leftover, and its
  connection actions now match the tray's exactly, `.odc` included

### Known limitations
- The endpoint has **no TLS**: password sign-in is base64 on the wire, and
  queries and results travel in the clear. Trusted LAN only - see
  KNOWN_LIMITATIONS.md §3. HTTPS is tracked as #132
- Remote access needs a one-time `netsh http add urlacl` and a firewall rule;
  without the reservation the endpoint silently serves localhost only. The tray
  detects this and offers the command - see KNOWN_LIMITATIONS.md §4

## [0.7.1] - 2026-07-25

### Added
- **Save .odc… for one-click Excel hand-off** - a served model can be saved as an
  Office Data Connection (`.odc`) file carrying the stable connection string and
  catalog; double-clicking it opens an Excel PivotTable with no connection string
  typed or seen. Available from the tray when a stable alias is set; *Copy
  connection string* stays for DAX Studio / advanced users (#86)

### Fixed
- **Window title showed "v0.5"** - the main window title was hardcoded and never
  bumped; it now derives from the assembly version, so it tracks the release (#113)

## [0.7.0] - 2026-07-25

### Added - Tray-first workflow (#47)
- **Off / Forward / Serve model** - a single per-model control replaces the old
  Start/Serve split; Forward gives a stable port, Serve additionally renames the
  database to its stable alias
- **Auto-serve on detection** - a per-model **On-detection** policy (Do nothing /
  Forward / Serve / Serve after grace period); "Serve after grace period" shows a
  countdown toast with an *edit instead* escape before serving
- **Tray as the primary surface** - per-model submenu with Off/Forward/Serve, the
  On-detection policy, an **Allow network access** toggle, copy connection string,
  and ready / new-model balloon toasts
- **Auto-start with Windows (#87)** - opt-in; adds an `HKCU\...\Run` entry, launches
  silent to the tray, and self-heals the Run key if the executable is moved

### Changed - Grid <-> tray convergence (#88)
- The grid is now a diagnostics/admin surface that projects the same state as the
  tray: an **On-detection** dropdown and a **Network** toggle that sync with the
  tray *both ways*
- The separate Action and Serve columns became a single **Action** menu built from
  the same available-actions the tray uses

### Fixed
- **Exit while serving now restores the database name (#100)** - a consolidated
  serve lifecycle (one `ServeLifecycleMachine` + `ServeLifecycleCoordinator` owning
  detection and exit) fixes the exit-while-serving DB-rename-back regression, a
  grace-period re-serve loop, and a startup UI freeze (the UIA dirty-state probe
  now runs off the UI thread)
- **Model-name detection under an elevated engine (#94/#95)** - model names are now
  resolved by Analysis Services port, so detection works even when the Power BI
  Desktop engine runs elevated
- **Language-independent unsaved-changes probe (#82)** - the UIA dirty-state probe
  matches localized Undo button labels instead of the English word only

### Notes
- The installer and executable remain **not code-signed**; Windows SmartScreen /
  Defender warns on first run (*More info -> Run anyway*) (#35).
- `.odc` one-click Excel export (#86) is deferred to a follow-up release.

## [0.6.0] - 2026-07-24

### Added - Windows Installer
- **MSI installer** - `PBIPortWrapper.msi` installs the app to Program Files,
  replacing the download-extract-run flow; the portable ZIP remains available (#33)
- **Start Menu integration** - a "PBI Port Wrapper" entry that launches the app (#34)
- **External Tool auto-registration** - the installer registers the app as a Power
  BI Desktop External Tool, so it appears on the External Tools ribbon with no
  manual `pbitool.json` copying (#5)
- **Silent / unattended install** - standard `msiexec /qn` support for scripted and
  Group Policy / SCCM / Intune deployment (#36)
- **Installer documentation** - build, install, silent-install, and troubleshooting
  guide in [docs/installer.md](docs/installer.md) (#38)

### Notes
- The installer and executable are **not code-signed**; Windows SmartScreen/Defender
  warns on first run (*More info -> Run anyway*). Documented as a known limitation (#35).
- Built from the single-file, self-contained win-x64 publish - the same binary the
  portable ZIP ships.

## [0.5.1] - 2026-07-20

### Fixed
- **Single-instance guard** - a second wrapper launch now fronts the existing
  window and exits instead of silently sharing config/log and competing for
  ports; a crashed wrapper never blocks the next start (#64)
- **Untitled instances** - configuration is blocked with a visible "Unsaved"
  status until the .pbix is saved; previously Set Port appeared to work but the
  rule was silently dropped (or orphaned under "Untitled") once the model got
  its real name; the alias editor path into the same bug is closed too (#9)

### Housekeeping
- Closed stale issues already fixed by the v0.4 detection rework (#50, #51),
  a v0.3-era tooltip crash superseded by the panel rework (#30), and the
  v1.0-investigation salvage review - fully superseded by v0.5 serving (#44)

## [0.5.0] - 2026-07-19

### Added - Serve Sessions (stable database names)
- **Serve profiles** - per-model stable alias persisted in configuration; the alias
  becomes the database name (Initial Catalog) while the model is being served (#56)
- **ServeSessionService** - serve-only session lifecycle: preflight, crash-anchor
  recovery record, rename to alias, proxy start; *Stop Serving* restores the
  original database name; closing Desktop cleans the session up automatically (#57)
- **Crash recovery on startup** - recovery records are matched against live
  instances by the immutable database ID; the wrapper offers *resume serving* or
  *restore original name*; stale records are cleared silently (#58)
- **Serve/Stop grid actions** - per-row Serve button with the validated warning
  flow ("Cannot load model" errors in Desktop are expected while serving);
  distinct "Serving" status separate from plain port forwarding (#59)
- **Unsaved-changes preflight** - UIA undo-heuristic probe; serving asks for
  explicit confirmation when the model may have unsaved changes (#59)
- **Serve Alias editor** - the details panel's raw "Rename DB" danger flow is
  retired; aliases are edited with validation and applied only by serving (#59)
- **MSOLAP connection string with alias** - one-click copy
  (`Provider=MSOLAP;Data Source=localhost:port;Initial Catalog=alias`) while serving (#59)

### Fixed
- **Config lost-update race** - single-writer rule: every config mutation goes
  through ConfigService, so panel and grid can no longer clobber each other (#62)
- **Manual Stop sticking while Auto is enabled** - stopping a proxy now records
  the intent and Auto no longer restarts it on the next poll (#63)

### Changed
- Details panel, grid tooltip and context menu now label the Analysis Services
  workspace directory honestly ("Workspace") instead of implying a .pbix path (#59)
- Dead top-level `FixedPort`/`AllowNetworkAccess` removed from the configuration
  model; pre-v0.5 config files still load unchanged (#59)

### Known Limitations
- **No single-instance guard** - launching a second wrapper process causes shared
  config/log access and port competition; planned fix is a named mutex (#64)
- **Desktop errors while serving** - Power BI Desktop repeatedly shows
  "Cannot load model" while its database is renamed; this is expected, do not
  troubleshoot in Desktop - click *Stop Serving* to restore it
- **Undo-heuristic is conservative** - the unsaved-changes probe cannot prove a
  model was saved after editing (the undo stack survives saving), so serving may
  ask for confirmation even right after a save

## [0.4.0] - 2026-07-18

### Added - Architecture
- **PBIPortWrapper.Core** - Headless core library (instance detection, port forwarding,
  configuration, database rename engine) with no UI dependencies; the WinForms app is
  now a thin projection over Core
- **InstanceMonitor** - Observable instance state moved out of the DataGridView;
  rows are identified by WorkspaceId instead of grid position
- **Config-driven auto-connect** - AutoConnectService decides forwarding from
  configuration rules instead of scraping grid cells
- **Unit test suite** - 50 tests covering Core services (detection, monitoring,
  validation, configuration, proxy management, rename validation)

### Fixed
- **DPI-aware layout** - Grid row heights, expand/active column widths, and
  RowDetailsPanel now scale correctly on high-DPI displays

## [0.3.0] - 2025-12-01

### Added - User Interface
- **System Tray Integration** - Minimize application to system tray for background operation
- **Copy Connection String** - One-click button to copy connection string to clipboard
- **Set Port Action Button** - Direct port configuration via action button (alternative to field editing)
- **Application Logo/Icon** - Professional branding integrated throughout UI
- **FileSystemWatcher Detection** - Instant Power BI instance detection (faster than polling)
- **External Tool Integration** - Register as Power BI Desktop External Tool for ribbon access

### Added - Logging & Diagnostics
- **Structured Logging System** - Clear log levels (DEBUG, INFO, WARNING, ERROR) with named categories
- **Contextual Logging Details** - Remote IP addresses, port mappings, model names tracked for every operation
- **Automatic Log Rotation** - Logs rotate at 5MB with historical retention (keeps 5 files)
- **Connection Tracking** - Detailed connection/disconnection logs with active connection counts
- **Exception Logging** - Full stack traces and exception details in structured format
- **Thread-Safe Logging** - Safe for concurrent use from multiple proxy threads
- **LoggerService** - Centralized logging infrastructure usable by all services

### Improved - Code Quality & Architecture
- **MVP Pattern Implementation** - Clean separation of concerns with proper MVP architecture
- **Eliminated God Object Anti-Pattern** - Better code organization with ViewEventCoordinator
- **Grid Logic Refactoring** - GridSyncHelper extraction for cleaner presenter code
- **Configuration Immutability** - ProxyConfiguration made read-only where appropriate
- **Global Exception Handling** - Unhandled exceptions now logged with full context
- **ProxyManager Logging** - Tracks proxy lifecycle with associated model names
- **TcpProxyService Logging** - Per-proxy detailed connection information with remote IP tracking

### Improved - User Experience
- **Column Layout** - Model Name column optimized for better visibility
- **Log File Organization** - Professional formatting: [yyyy-MM-dd HH:mm:ss] [LEVEL] [Category] Message
- **Instance Detection Performance** - Significantly faster via FileSystemWatcher vs polling
- **Configuration Handling** - Improved Remove action and config reload on refresh

### Fixed
- **IP Detection Logic** - Corrected identification of remote IP addresses
- **Configuration Persistence** - Fixed in-memory config to preserve Remove deletions
- **Auto-Reconnect Behavior** - Improved auto-restart logic

### Known Limitations
- **Auto-Restart on Stop** - When "Auto" mode is enabled, manually stopping a proxy will restart it on next poll interval if PBI instance still running; workaround: disable Auto to Stop an instance, then re-enable Auto
- **Database Name Changes** - Database name changes when Power BI Desktop restarts (requires reconnection)
- **Network Access Setup** - Manual Windows Firewall configuration required for remote connections


## [0.2.0] - 2025-11-28

### Added
- Multi-instance proxy support - forward multiple Power BI instances simultaneously
- Per-instance port mapping configuration - set fixed ports for each model
- Auto-connect capability - automatically start forwarding for configured instances
- Process detection via WMI - improved instance identification and friendly naming
- DataGrid-based UI - modern interface for managing multiple instances
- Network access per-instance - granular control over remote access settings

### Changed
- **BREAKING**: UI completely redesigned from single-instance layout to multi-instance DataGrid
- **BREAKING**: Architecture refactored from TcpProxyService to ProxyManager for multi-instance support
- Configuration supports managing multiple instances with individual port mapping rules
- Enhanced instance naming using Power BI Desktop window titles
- Improved logging with per-action timestamps

### Fixed
- Better instance detection and tracking across Power BI restarts

### Known Limitations
- Auto-reconnect fires on UI refresh timer (5-second interval)
- Network access configuration requires manual Windows Firewall setup


## [0.1.0] - 2025-11-02

### Added
- Initial release
- TCP port forwarding for Power BI Desktop
- Automatic Power BI instance detection
- Configurable listen port (default: 55555)
- Network access support with explicit credentials
- Activity logging (UI and file)
- Windows Firewall configuration instructions
- Configuration persistence
- Database UUID detection and logging

### Known Limitations
- Database name changes require reconnection after Power BI restart
- Single instance support only
- No automatic reconnection