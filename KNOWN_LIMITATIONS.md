# Known Limitations

## 1. Power BI Desktop Errors While Serving
**Issue:** While a model is being served, its database carries the stable alias
instead of the session GUID, and Power BI Desktop repeatedly shows "Cannot load
model" errors on its own.

**Impact:** Desktop is not usable for editing during a serve session. Saving
must happen *before* serving starts (the preflight asks when in doubt).

**Root Cause:** Power BI Desktop does not support dynamic database renaming at
runtime; it resolves its own database by name.

**Workaround:** By design, serving is a deliberate, serve-only session: click
**Stop Serving** and Desktop recovers immediately (the original name is
restored). If the wrapper crashes mid-serve, the startup recovery prompt offers
the same restoration.

**Status:** Accepted architectural limitation of the rename-at-the-source
approach (see docs/serving-workflow.md). A transparent XMLA proxy that would
avoid renaming entirely remains a possible v1.0 direction.


## 2. Inline Row Details Navigation
**Issue:** Arrow keys (`Left`, `Right`, `Home`, `End`) do not function correctly
within the **Serve Alias** textbox located in the expanded row details.

**Impact:** Users cannot navigate the text cursor using the keyboard. They must
use the mouse to position the cursor.

**Root Cause:**
The `RowDetailsPanel` is hosted as a child control *inside* a `DataGridView`.
The Windows Forms `DataGridView` architecture aggressively manages keyboard
input for navigation (moving between cells/rows). It intercepts these
keystrokes before they reach the nested child controls, regardless of standard
overrides like `IsInputKey` or `ProcessCmdKey`.

**Workaround:** User must use the mouse for cursor positioning within this
specific field.

**Status:** Accepted architectural limitation to preserve the visual "Inline
Expansion" design. Fixing this would require a complete UI rewrite (e.g.,
Master-Detail sibling panels).


## 3. No Single-Instance Guard (#64)
**Issue:** Nothing prevents launching a second wrapper process. Concurrent
instances share config.json and log.txt without coordination and compete for
the same fixed ports.

**Impact:** Cross-process config lost-updates, interleaved log files, and
confusing port-bind failures in the losing instance. With serve sessions, two
wrappers could each prompt for the same crash recovery record.

**Workaround:** Run only one wrapper at a time (check the tray before starting
a second one).

**Status:** Known bug, ships as a limitation in v0.5.0. Planned fix: named
mutex at startup that fronts the existing instance's window (see issue #64).
