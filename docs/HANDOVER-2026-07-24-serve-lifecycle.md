# Handover — v0.7 tray-first UI & the serve-lifecycle seam (2026-07-24)

Read this first if you're picking up PBIPortWrapper mid-v0.7. It captures where
things stand, the one problem area that's been generating repeated bugs, an open
regression to fix, and the recommended way out.

## TL;DR

- **v0.6.0 shipped** (Windows installer) to Gitea + GitHub.
- **v0.7 tray-first UI** (#47) is well underway on `develop`: Off/Forward/Serve
  model, a tray-first surface, and **auto-serve on detection**.
- A cluster of bugs (#96, #100, #102, plus an exit deadlock and an open
  exit-restore regression) all come from **one seam**: the new *auto-serve*
  feature interacting with the existing *serve-session lifecycle*. They are not
  random — they are the predictable cost of bolting auto-serve on without
  designing the lifecycle interactions holistically.
- **Recommendation:** stop patching per-report. Do one **serve-lifecycle
  consolidation** (a state model + a single reconciling change) before more
  feature work. See [Recommended path](#recommended-path).

## Where the code is

Released: **v0.6.0** (installer). Design docs: [tray-workflow.md](tray-workflow.md)
(v0.7 design), [serving-workflow.md](serving-workflow.md) (serve sessions),
[HANDOFF.md](HANDOFF.md) (original decisions log).

Merged into `develop` (v0.7 so far):
- #84 Core `OnDetectionPolicy` + `HostState`/`HostStateMachine` + config migration.
- #85a tray-first surface (state projection + Serve/Forward/Stop/Copy actions).
- #85b **auto-serve on detection** (`AutoServeController`, `AutoServePlanner`,
  toasts, grace period) — *this is the feature the seam bugs orbit.*
- #96 auto-serve suppression after manual stop; #99 single "Stop" in Serve state.
- #94 detection fix — match engine to workspace by **AS port**, not command line
  (needed when the Desktop engine runs elevated; the "July 2026" framing was
  wrong — it's process elevation).
- #100 restore served databases on exit; the exit flow is **async** (cancel the
  close, restore, then close) to avoid a UI-thread deadlock.

Open PRs (both off `develop`):
- **#93** — #85c language-independent dirty probe (`UndoButtonMatcher`). Clean,
  independent of the seam. **Safe to merge.**
- **#103** — #102 auto-serve vs crash-recovery collision fix. The recovery half is
  correct, **but do not merge yet**: exit-restore was observed broken while
  testing it (see the regression below). Confirm exit-restore first.

## The problem: auto-serve × serve-session lifecycle

Auto-serve (#85b) drives serving from detection snapshots. The serve-session
lifecycle (#57 start / #58 crash-recovery / stop / exit) was designed for
*manual, one-at-a-time* serving. Wiring auto-serve on top created interactions
that keep surfacing as bugs:

| Bug | Interaction |
|-----|-------------|
| #96 | Auto-serve re-served a model right after a manual **Stop** (no suppression). |
| #100 | **Exit** stopped proxies but didn't restore served DB names. |
| (deadlock) | The exit-restore fix blocked the UI thread while teardown raised events that marshalled back to it. |
| #102 | On startup, auto-serve's own fresh recovery record was caught by the **crash-recovery** check (race in the establishment window) → spurious recovery prompt + "Already serving". |
| **open** | Exit-restore appears broken again (see below). |

Root cause is structural: **there is no single owner of "what state is this model in
and who may change it."** State is spread across `ServeSessionService`
(sessions + recovery records), `AutoServeController` (in-flight/grace/suppression
sets), `ServeRecoveryCoordinator` (startup recovery), `AutoConnectService`
(forward suppression), and `MainForm` (exit). Each fix adds another cross-check;
each new trigger finds a new gap.

## Open regression to fix first: exit no longer restores the DB

**Symptom (2026-07-24):** with a model on `On detection = Serve`, exiting the
wrapper while it's serving closes cleanly but leaves the database renamed
(Desktop stuck on "Cannot load model"). This worked at the point #100/#101 was
first verified ("closes cleanly"), and broke by the time #103 was being tested.

**Likely origin:** the **async exit flow from #101** (already on `develop`), not
#103 (#103 does not touch the exit path). Prime suspects, in order:
1. `ServeSessionService.RestoreAllAsync` swallows per-session exceptions
   (`catch { }`). If `StopServingAsync`'s rename-back throws or fails on shutdown
   (e.g. AMO/ADOMD connection during teardown, or the proxy/engine already gone),
   the DB is silently left renamed and the app still closes cleanly. **First step:
   replace that silent catch with logging** and reproduce exit-while-serving, then
   read `%APPDATA%\PBIPortWrapper\log.txt`.
2. Disposal/ordering timing: confirm `RestoreAllAsync` actually completes its
   renames **before** `StopAll()` disposes anything it depends on.
3. Confirm the model was truly *serving* at exit (an active `ServeSession`); if
   auto-serve hadn't fully registered the session, `RestoreAllAsync` has nothing
   to restore even though the DB is renamed.

Do not guess this one from code alone — it's a timing/integration issue. Add
logging and observe.

## Recommended path

Consolidate the serve lifecycle instead of patching the next report:

1. **Write the state model.** States: `Off`, `Forwarding`, `Serving`, `Grace`,
   `Recovering`. Triggers: instance detected / gone, manual Serve/Stop, auto-policy
   (Serve / ServeAfterGrace / Forward / DoNothing), grace elapse/cancel, app exit,
   crash. Fill the table: for every (state, trigger) say the resulting state and
   who owns the transition. Most current bugs are empty/contradictory cells.
2. **Give the lifecycle a single owner.** Fold auto-serve's in-flight/grace/
   suppression, recovery's startup gating, and exit restoration into one
   coordinator (or into `ServeSessionService`), so there is one place that knows a
   model's state and guards transitions — not five cross-checking each other.
3. **Reconcile in one designed change,** with the interactions reasoned up front,
   rather than five sequential patches. This is the natural scope of **#88**
   (grid → diagnostics) pulled forward; the grid and tray should both project this
   one model.
4. **Verify the integrated flows,** not just units: the Core decision logic is
   well unit-tested, but every seam bug was integration/timing. At minimum,
   instrument the lifecycle with logging and walk each flow manually
   (serve → stop → serve; serve → exit; crash → restart → recovery;
   auto-serve → exit). Full automation needs a live Power BI Desktop and is hard;
   logging + a scripted manual checklist is the realistic bar.

## Why the bugs slipped (method note for next time)

- Unit tests (Core) stayed green throughout; the bugs were **integration/timing**
  between UI + services, which units don't exercise.
- Verification leaned on the maintainer's manual testing, so integration
  regressions surfaced late and one at a time.
- Two regressions were introduced during the fix streak (the exit deadlock, and
  this exit-restore regression). The lesson isn't "more capable model" — it's
  **fewer moving parts (consolidation) + explicit interaction reasoning +
  logging-driven verification of integrated flows.**

## Dev environment quick-reference

- Build/test on Windows: `dotnet build PBIPortWrapper.sln`, `dotnet test`. Targets
  `net8.0-windows`, WinForms. Core logic is in `PBIPortWrapper.Core` (unit-tested);
  the app is a thin projection.
- **Size limits (csproj `EnforceCodeSizeLimits`):** MainForm ≤ 500 lines (error),
  Presenters ≤ 250. Split rather than fight them.
- **Build lock:** a running `PBIPortWrapper.exe` locks `bin\...\PBIPortWrapper.Core.dll`
  and makes `dotnet build` fail with MSB3021/3027 (copy errors, *not* code errors).
  Build the app to a scratch `-o` dir to verify compile without killing a running
  instance.
- Config + logs live under `%APPDATA%\PBIPortWrapper\` (`config.json`, `log.txt`).
- Gitea is the dev remote (`Projects/PBIPortWrapper`); GitHub is the public release
  origin. The `tea` CLI tends to hang — use the Gitea API with the token in
  `%LOCALAPPDATA%\tea\config.yml`. One increment = one branch off `develop` = one PR;
  the maintainer merges.

## v0.7 remaining milestone work

- **#86** — `.odc` generation (headline Excel hand-off).
- **#87** — auto-start with Windows (silent to tray).
- **#88** — grid → diagnostics; the natural home for the lifecycle consolidation
  above (tray and grid should converge on Off/Forward/Serve + the On-detection
  policy).
- **#93 / #103** — merge #93; resolve the exit-restore regression, then #103.
