# PBI Port Wrapper

TCP port-forwarding proxy for Power BI Desktop: gives each Desktop instance a stable
port (and, from v0.5, a stable database name) so external tools like Excel, DAX
Studio, and Tabular Editor keep working across sessions.

## Build & constraints

- `dotnet build` — net8.0-windows, WinForms. Windows-only (WMI, tray, Power BI Desktop).
- The csproj has an `EnforceCodeSizeLimits` post-build target (PowerShell):
  **MainForm.cs ≤ 400 lines (error), Presenters/*.cs ≤ 250 lines (error).**
  Split code rather than fighting these limits.

## Architecture direction (see docs/)

- **[docs/HANDOVER-2026-07-25-v0.7-release.md](docs/HANDOVER-2026-07-25-v0.7-release.md)
  — read first if resuming.** All v0.7 features are merged; this is the checklist to
  cut the **v0.7.0 release** (.odc/#86 deferred to v0.7.1).
- [docs/HANDOVER-2026-07-24-serve-lifecycle.md](docs/HANDOVER-2026-07-24-serve-lifecycle.md)
  — the serve-lifecycle consolidation that shaped v0.7 (historical context).

- [docs/serving-workflow.md](docs/serving-workflow.md) — the serve-session design:
  serve profiles, serve-only sessions, DB rename at the source, experiments E1–E5.
- [docs/tray-workflow.md](docs/tray-workflow.md) — the v0.7 tray-first workflow
  design ("local SSAS" persona, hosted models, Off/Forward/Serve, auto-host).
- [docs/HANDOFF.md](docs/HANDOFF.md) — decisions log and research references.
- `PBIPortWrapper.Core` holds the headless state/logic (extracted in v0.4); the UI
  is a thin projection. Prefer moving logic toward Core over adding it to presenters.
- `Services/XmlaProxyService.cs` is dormant (wire-level MITM, shelved) — don't
  extend it; see HANDOFF.md before touching.

## Conventions

- MVP-style: MainForm wires services/presenters; presenters own behavior.
- Config and logs persisted via `ConfigurationManager` under `%APPDATA%\PBIPortWrapper\`
  (`config.json`, `log.txt`) — not next to the executable, so the app runs from
  read-only locations like Program Files.
- Private WIP goes to the Gitea remote; GitHub is the public origin for releases.
