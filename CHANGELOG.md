# Changelog

All notable changes to PBI Port Wrapper will be documented in this file.

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