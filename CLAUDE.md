# PBIRelay

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
- `Core` carries `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (Kestrel,
  #132). The app and test projects therefore need **`<RollForward>LatestMajor</RollForward>`**
  on a machine with a newer ASP.NET Core than .NET runtime — plain `Major` fails, because
  AspNetCore rolls forward while NETCore stays pinned. Scratch probe projects need it too.
- A running instance locks `bin\Debug`. Build to a scratch `-o` directory rather than
  killing it; ask before stopping an instance you did not start.

## Architecture direction (see docs/)

- **The v1.0 direction (epic #124):** one HTTP XMLA endpoint, models addressed by alias
  on per-model paths, forwarding retired (#126). **The relay translates nothing** — it
  hands the engine every request as the client sent it.
- [docs/http-bridge.md](docs/http-bridge.md) — the endpoint's implementation reference.
- [docs/serving-workflow.md](docs/serving-workflow.md) — the serve-session design:
  serve profiles, serve-only sessions, DB rename at the source, experiments E1–E5.
- [docs/tray-workflow.md](docs/tray-workflow.md) — the v0.7 tray-first workflow
  design ("local SSAS" persona, on-detection policies, auto-serve). Historical on
  vocabulary: it predates #126, so it still says Forward where the states are now
  only Off and Serve.
- [docs/research/](docs/research/README.md) — archived December 2025 investigation
  (salvaged from the retired Gitea wiki). **Read before proposing anything that
  rewrites the wire: a full XMLA proxy and an ADOMD.NET proxy were both tried and
  rejected there**, which is why the relay translates nothing.
- `PBIRelay.Core` holds the headless state/logic (extracted in v0.4); the UI
  is a thin projection. Prefer moving logic toward Core over adding it to presenters.
- `Services/XmlaProxyService.cs` is dormant (wire-level MITM, shelved) — don't
  extend it; read [docs/research/](docs/research/README.md) before touching.

## Conventions

- **Test before merge.** If a PR has a manual checklist, build the branch, say so, and
  let it be tested *before* it is merged; fix on the same branch. One PR = one working
  increment. A defect in what the PR claimed belongs on that PR's branch; something newly
  thought of gets its own. (Docs-only changes, having no runtime surface, may merge first.)
- **A green suite says nothing about whether a UI reads correctly.** Render dialogs and
  look at them — a scratch WinForms harness referencing `PBIRelay.csproj` plus
  `PrintWindow` does it offline, without launching the app against live Power BI models.
- **When a capability ships, sweep the docs *and* the UI strings for claims it falsifies.**
  Both have gone stale the same way: a heading updated and a rationale left behind.
- MVP-style: MainForm wires services/presenters; presenters own behavior.
- Config and logs persisted via `ConfigurationManager` under `%APPDATA%\PBIRelay\`
  (`config.json`, `log.txt`) — not next to the executable, so the app runs from
  read-only locations like Program Files.
- Private WIP goes to the Gitea remote; GitHub is the public origin for releases.
