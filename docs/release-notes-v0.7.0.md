# PBI Port Wrapper v0.7.0 — Tray-first workflow

This release reworks the day-to-day workflow around the system tray and a single
**Off / Forward / Serve** model per Power BI Desktop instance, so keeping several
models reachable for Excel and other tools is near zero-touch.

- **Off / Forward / Serve (#47)** — one per-model control. *Forward* gives the model
  a stable port; *Serve* additionally renames its database to a stable alias so the
  connection string survives Desktop restarts.
- **Auto-serve on detection** — each model has an **On-detection** policy
  (*Do nothing / Forward / Serve / Serve after grace period*). "Serve after grace
  period" shows a countdown toast with an *edit instead* escape before it serves.
- **Tray as the primary surface** — the tray menu drives everything per model:
  Off/Forward/Serve, the On-detection policy, an **Allow network access** toggle,
  copy connection string, plus ready / new-model toasts.
- **Auto-start with Windows (#87)** — opt-in checkbox; adds an `HKCU\…\Run` entry,
  launches silent to the tray, and self-heals the Run key if the exe is moved.
- **Grid ↔ tray convergence (#88)** — the grid is now a diagnostics/admin surface
  that projects the same state as the tray. It gained the On-detection dropdown and
  Network toggle (both sync with the tray *both ways*), and the separate Action and
  Serve columns became a single **Action** menu.

## Fixed

- **Exit while serving now restores the database name (#100)** — a consolidated
  serve lifecycle (one state machine + a single coordinator owning detection and
  exit) fixes the exit-while-serving DB-rename-back regression, a grace-period
  re-serve loop, and a startup UI freeze.
- **Detection under an elevated engine (#94/#95)** — model names resolve by Analysis
  Services port, so detection works even when the Desktop engine runs elevated.

## Known limitation

The installer and executable are **not code-signed**, so Windows SmartScreen /
Defender warns on first run — click **More info → Run anyway**. See
[KNOWN_LIMITATIONS.md](../KNOWN_LIMITATIONS.md) §3 (#35).

`.odc` one-click Excel export (#86) is deferred to a follow-up release.

## Install

- **Installer (recommended):** download `PBIPortWrapper.msi`, run it, and launch
  from the Start Menu or the Power BI Desktop External Tools ribbon.
- **Portable ZIP:** download and extract `PBIPortWrapper-v0.7.0-win-x64.zip`, then
  run `PBIPortWrapper.exe`.

Full details in [CHANGELOG.md](../CHANGELOG.md).
