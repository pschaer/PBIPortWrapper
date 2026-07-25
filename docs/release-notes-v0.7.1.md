# PBI Port Wrapper v0.7.1

A small follow-up to v0.7.0 that adds the deferred Excel hand-off and fixes the
window-title version.

## Added

- **Save .odc… (#86)** — a served model can be handed to Excel as an **Office Data
  Connection** file. From the tray, a model with a stable alias gets a **Save .odc…**
  action; double-clicking the saved file opens an Excel **PivotTable** connected to
  the model — no connection string ever typed or seen. Because the alias and port are
  stable across Desktop restarts, a saved `.odc` keeps resolving. *Copy connection
  string* stays for DAX Studio / Tabular Editor / advanced users.

## Fixed

- **Window title showed "v0.5" (#113)** — the title was hardcoded and never bumped
  across releases. It now derives from the assembly version, so it tracks the release
  automatically.

## Install

- **Installer (recommended):** download `PBIPortWrapper.msi`, run it, and launch
  from the Start Menu or the Power BI Desktop External Tools ribbon.
- **Portable ZIP:** download and extract `PBIPortWrapper-v0.7.1-win-x64.zip`, then
  run `PBIPortWrapper.exe`.

The installer and executable are **not code-signed**; Windows SmartScreen / Defender
warns on first run (*More info → Run anyway*). Full details in
[CHANGELOG.md](../CHANGELOG.md).
