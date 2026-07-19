# Session Handoff — 2026-07-18 (macOS → Windows)

Distillation of a Claude Code planning session held on macOS (where nothing could be
run — no Power BI Desktop, no WinForms). Development continues on Windows. Start a
new Claude Code session in this repo and say:
*"Read docs/HANDOFF.md and docs/serving-workflow.md, then continue."*

## Where the project stands

- v0.3 shipped: TCP port forwarding, multi-instance, tray, FileSystemWatcher detection.
- ~2,600 lines C#, net8.0-windows, WinForms, MVP-ish structure with build-enforced
  file size limits (MainForm ≤ 400 lines, Presenters ≤ 250 — see csproj target).
- `Services/XmlaProxyService.cs` is a dormant stub for wire-level DB rewriting —
  superseded by the serving-workflow design; keep until v0.5 proves it unnecessary.

## Decisions made this session

1. **C# stays.** The AS ecosystem (AdomdClient/AMO/TOM), Windows plumbing (WMI,
   FileSystemWatcher, SSPI via `NegotiateAuthentication`), and the contributor pool
   are all .NET. Only trade-off is runtime size — cosmetic for this audience.
2. **Architecture problem identified:** the DataGridView *is* the data model. State
   lives in grid cells, logic dispatches on button label strings, validation scans
   grid rows, config is scraped from cells. Fix = extract a headless
   `PBIPortWrapper.Core` (instance monitor, rules engine, proxy engine, config
   store; observable state, no WinForms ref) and make the UI a thin projection.
   This is also the prerequisite for CLI automation and service/auto-start.
3. **UI:** don't rewrite the framework; rewrite the *workflow*. Ideal UX is
   tray-first with almost no interaction (toast on detection, copy connection
   string, settings for exceptions). Grid becomes admin/diagnostics view. Stay
   WinForms (or WPF) — Power BI Desktop is Windows-only, cross-platform buys nothing.
4. **Transparent TCP MITM proxy: shelved.** Research findings (kept for reference):
   - Protocol is publicly documented, not reverse engineering:
     [MS-SSAS Transport](https://learn.microsoft.com/en-us/openspecs/sql_server_protocols/ms-ssas/cc9c04c8-df61-40aa-b9bf-49d06b3ac888)
     (DIME framing, cleartext or binary XML, optional compression, GSS-API recommended),
     [Content Type Negotiation](https://learn.microsoft.com/en-us/openspecs/sql_server_protocols/ms-ssas/4392864b-042b-4f1d-a9b6-4ce0b74ff837)
     (`RESP_SX`/`RESP_XPRESS` flags — a proxy can negotiate encoding *down* to
     cleartext/uncompressed), [MS-BINXML](https://learn.microsoft.com/en-us/openspecs/sql_server_protocols/ms-binxml/11ab6e8d-2472-44d1-a9e6-bddf000e12f6),
     Xpress9 open impls ([xpress9-python](https://github.com/Hugoberry/xpress9-python),
     [pyxca](https://github.com/jborean93/pyxca) for MS-XCA).
   - But: no server-side library exists (AdomdClient is client-only), no open-source
     implementation anywhere, and NTLM loopback/reflection protections make the
     MITM auth handshake the key unknown.
5. **Pre-mortem verdict:** the likely killers were product risks, not technical —
   (a) building the hardest feature for the thinnest user need, (b) solo-maintainer
   stall from parallel workstreams. Therefore: demand-check before protocol work,
   serialize releases, keep every version shippable.
6. **v1.0 redefined** (user decision): stability including database name, achieved by
   **renaming the workspace DB at the source** (user's preliminary tests confirm it
   works; Desktop breaks while renamed) + a **serve-only session workflow**.
   Target use cases: **Excel self-service** and **LAN sharing/demos**.
   Full design: [serving-workflow.md](serving-workflow.md).

## Immediate next steps (in order)

1. Run experiments **E1–E5** from serving-workflow.md (needs Power BI Desktop; E1
   ideally needs a second Windows account/machine). E1 gates the LAN promise.
2. Optional, cheap: GitHub Discussion to demand-check DB-name stability use cases.
3. **v0.4 core extraction** — new `PBIPortWrapper.Core` project, move state/logic
   out of the grid, unit tests, GitHub Actions CI on `windows-latest`.
4. v0.5 serve profiles + rename engine per the design doc.

## Prior art already in Gitea (found during handoff — review before building!)

The Gitea repo (real dev repo; GitHub is the curated public mirror, histories have
diverged) contains an earlier v1.0 investigation that predates this session's design:

- Branch `feature/v1.0-database-alias` (also archived as tag
  `archive/v1.0-database-alias-investigation`), Gitea issues #15–#17:
  `XmlaRewriteService` (XMLA message rewriting), `AdomdProxyService` (XMLA message
  translation), later replaced by TCP forwarding in the XMLA proxy, plus
  `StableAlias` plumbing from UI to ProxyManager.
- This is presumably where the "rename works but Desktop breaks" observation comes
  from. **First task on Windows: diff this branch against the serving-workflow
  design** — salvage the StableAlias UI plumbing and any rename code; the
  wire-rewriting services are superseded by the serve-only-session approach.
- `develop` is ahead of `main` (installer project, UI fixes) — base v0.4 work on
  `develop`, not `main`.

## Environment notes

- Dev machine: Windows with .NET 8 SDK + Power BI Desktop. Build: `dotnet build`
  (note the PowerShell-based size-limit target in the csproj runs after build).
- Private WIP flows through the homelab **Gitea** remote; GitHub remains the public
  origin for releases.
