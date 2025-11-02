[![GitHub release](https://img.shields.io/github/v/release/pschaer/PowerBIPortWrapper)](https://github.com/pschaer/PowerBIPortWrapper/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

# Power BI Port Wrapper

A TCP port forwarding proxy for Power BI Desktop that provides stable port access for external tools like Excel, DAX Studio, and Tabular Editor.

## 🎯 Problem Solved

Power BI Desktop uses dynamic ports that change with each session, making it difficult to:
- Connect from external tools consistently
- Share connection information with team members
- Automate workflows that depend on Power BI models

**Power BI Port Wrapper** solves this by providing a stable, fixed port that forwards connections to the current Power BI Desktop instance.

## ✨ Features

- ✅ **Stable Port Forwarding** - Fixed port number (default: 55555) that doesn't change
- ✅ **Automatic Detection** - Finds running Power BI Desktop instances automatically
- ✅ **Local Connections** - Full Windows Authentication support
- ✅ **Remote Connections** - Network access with explicit credentials
- ✅ **Activity Logging** - Real-time connection monitoring and file logging
- ✅ **Simple UI** - Easy-to-use interface with minimal configuration

## 📋 Requirements

- Windows 10/11
- Power BI Desktop (any version)

**Note:** No additional software installation required - .NET runtime is included.

## 🚀 Quick Start

1. **Download** the latest release
2. **Extract** the ZIP file
3. **Run**```PowerBIPortWrapper.exe```
4. **Start Power BI Desktop** and open a model
5. **Click "Refresh"** to detect the running instance
6. **Click "Start Port Forwarding"**
7. **Connect** from your tool using``` localhost:55555```

## 🔌 Connecting from Tools

### Excel (Same Computer)
1. Data → Get Data → From Database → From Analysis Services
2. Server name:```localhost:55555```
3. Authentication: Use Windows Authentication
4. Select your database

### Excel (Remote Computer)
1. Data → Get Data → From Database → From Analysis Services
2. Server name:```[your-ip]:55555```
3. Authentication: Use the following User Name and Password
   - Username: Your Microsoft Account email or DOMAIN\username
   - Password: Your password
4. Select your database

### DAX Studio
1. Connect → Connection String
2. Enter:```Data Source=localhost:55555```
3. Click Connect

## ⚙️ Configuration

- **Listen Port**: The fixed port to listen on (default: 55555)
- **Allow Network Access**: Enable connections from other computers
  - ⚠️ Requires Windows Firewall configuration
  - Remote clients must use explicit credentials

### Firewall Configuration

To allow remote connections, run this PowerShell command as Administrator:

```powershell
New-NetFirewallRule -DisplayName "Power BI Port Wrapper" -Direction Inbound -LocalPort 55555 -Protocol TCP -Action Allow
```

## 📁 File Locations

- **Configuration**: ```%APPDATA%\PowerBIPortWrapper\config.json```
- **Logs**: ```%APPDATA%\PowerBIPortWrapper\log.txt```

## 🐛 Known Limitations (v0.1)

- ⚠️ **Database name changes** when Power BI Desktop restarts - requires reconnection
- ⚠️ **Single instance** - Can only forward to one Power BI Desktop instance at a time
- ⚠️ **No automatic reconnection** - Must manually restart proxy after Power BI restarts

These limitations will be addressed in v0.2 with full XMLA protocol support.

## 🗺️ Roadmap

### v0.2 (Planned)
- **External Tool Integration** - Register as Power BI Desktop External Tool for one-click launch
 
### v0.3
- Full XMLA protocol proxy with database name abstraction
- Automatic reconnection when Power BI restarts
- Transparent remote authentication
- Multiple instance support


### Future Considerations
- System tray integration with background operation
- Auto-start with Windows option
- Connection pooling and performance optimization
- Configuration profiles for different scenarios
- Command-line interface for automation
- Telemetry and usage statistics (opt-in)


## 📄 License

This project is licensed under the MIT License - see the [LICENSE.txt](LICENSE.txt) file for details.

## ⚠️ Disclaimer

This is an unofficial tool and is not affiliated with, endorsed by, or supported by Microsoft Corporation. Use at your own risk.

---

**Made with ❤️ for the Power BI community**