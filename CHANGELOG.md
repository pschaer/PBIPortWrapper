# Changelog

All notable changes to PBI Port Wrapper will be documented in this file.

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