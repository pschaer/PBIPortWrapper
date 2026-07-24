# PBI Port Wrapper v0.6.0 — Windows installer

This release adds a real Windows installer alongside the portable ZIP, so getting
PBI Port Wrapper set up as a Power BI Desktop External Tool no longer means copying
files by hand.

- **MSI installer (#33)** — `PBIPortWrapper.msi` installs the app to Program Files,
  replacing the download-extract-run flow. The portable ZIP is still published for
  anyone who prefers it.
- **Start Menu integration (#34)** — a *PBI Port Wrapper* entry launches the app.
- **External Tool auto-registration (#5)** — the installer registers the app as a
  Power BI Desktop External Tool, so it shows up on the **External Tools** ribbon
  after a Desktop restart, with no manual `pbitool.json` copying.
- **Silent / unattended install (#36)** — standard `msiexec /qn` support for
  scripted and Group Policy / SCCM / Intune deployment.
- **Installer documentation (#38)** — build, install, silent-install, and
  troubleshooting guide in [docs/installer.md](docs/installer.md).

## Known limitation

The installer and executable are **not code-signed**, so Windows SmartScreen /
Defender warns on first run — click **More info → Run anyway**. See
[KNOWN_LIMITATIONS.md](../KNOWN_LIMITATIONS.md) §3 (#35).

## Install

- **Installer (recommended):** download `PBIPortWrapper.msi`, run it, and launch
  from the Start Menu or the Power BI Desktop External Tools ribbon.
- **Portable ZIP:** download and extract `PBIPortWrapper-v0.6.0-win-x64.zip`, then
  run `PBIPortWrapper.exe`.

Full details in [CHANGELOG.md](../CHANGELOG.md).
