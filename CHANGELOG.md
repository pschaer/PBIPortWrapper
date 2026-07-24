# Changelog

All notable changes to PBI Port Wrapper will be documented in this file.

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