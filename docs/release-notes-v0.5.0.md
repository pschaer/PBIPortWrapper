# PBI Port Wrapper v0.5.0 — Serve sessions with stable database names

External tools (Excel, DAX Studio, Tabular Editor, scripts) can now connect to
Power BI Desktop models with a **stable connection string** — fixed port *and*
fixed database name — that survives Desktop restarts:

```
Provider=MSOLAP;Data Source=localhost:55555;Initial Catalog=YourAlias
```

## Highlights

- **Serve sessions**: give a model a stable alias, click **Serve**, and the
  wrapper renames the workspace database to the alias and forwards the fixed
  port. Click **Stop Serving** and Desktop is restored exactly as it was.
  Saved Excel workbooks reconnect across Desktop sessions (validated E2E).
- **Crash recovery**: if the wrapper dies mid-serve, the next start detects the
  renamed database (matched by immutable database ID) and offers to resume
  serving or restore the original name. Both paths live-validated.
- **Unsaved-changes preflight**: serving warns before touching a model that may
  have unsaved changes (UIA undo-heuristic probe; asks the user when unsure).
- **Safer alias editing**: the raw "Rename DB" danger button is gone; aliases
  are validated and only applied by serve sessions.

## Fixes

- Config lost-update race between grid and details panel (#62)
- Manual Stop no longer bounces back while Auto-connect is enabled (#63)

## Known limitations

- Power BI Desktop shows "Cannot load model" errors *while serving* — expected;
  do not troubleshoot in Desktop, just Stop Serving to restore it.
- No single-instance guard yet (#64): run one wrapper at a time.
- The unsaved-changes probe is conservative and may ask for confirmation even
  right after a save.

Full details in [CHANGELOG.md](CHANGELOG.md).
