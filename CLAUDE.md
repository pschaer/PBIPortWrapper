# PBI Port Wrapper

One HTTP XMLA endpoint over the Power BI Desktop models on this machine. Serving a
model renames its database to a stable alias and publishes it at its own path on that
endpoint (`http://host:55555/Sales`), so Excel — including from another machine, as
another user — keeps working across Desktop restarts.

It began as a TCP port-forwarding proxy; that transport was retired in v0.8 (#126),
because a forwarded port gave no stable database name and only ever worked for the
same Windows user.

## Build & constraints

- `dotnet build` — net8.0-windows, WinForms. Windows-only (WMI, tray, Power BI Desktop).
- The csproj has an `EnforceCodeSizeLimits` post-build target (PowerShell):
  **MainForm.cs ≤ 500 lines (error; warns past 400 — it is a composition root),
  Presenters/*.cs ≤ 250 lines (error).**
  Split code rather than fighting these limits.

## Architecture direction (see docs/)

- **[docs/HANDOVER-2026-07-26-v1.0-endpoint.md](docs/HANDOVER-2026-07-26-v1.0-endpoint.md)
  — read first if resuming.** The v1.0 direction (epic #124): one HTTP XMLA endpoint,
  models addressed by alias on per-model paths, forwarding retired. Carries the
  relay-not-translator rule and the traps that cost time.
- [docs/http-bridge.md](docs/http-bridge.md) — the endpoint's implementation reference.
- [docs/HANDOVER-2026-07-25-v0.7-release.md](docs/HANDOVER-2026-07-25-v0.7-release.md)
  — the v0.7 release checklist (historical context).
- [docs/HANDOVER-2026-07-24-serve-lifecycle.md](docs/HANDOVER-2026-07-24-serve-lifecycle.md)
  — the serve-lifecycle consolidation that shaped v0.7 (historical context).

- [docs/serving-workflow.md](docs/serving-workflow.md) — the serve-session design:
  serve profiles, serve-only sessions, DB rename at the source, experiments E1–E5.
- [docs/tray-workflow.md](docs/tray-workflow.md) — the v0.7 tray-first workflow
  design ("local SSAS" persona, on-detection policies, auto-serve). Historical on
  vocabulary: it predates #126, so it still says Forward where the states are now
  only Off and Serve.
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
