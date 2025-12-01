# Changelog

All notable changes to PBI Port Wrapper will be documented in this file.

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