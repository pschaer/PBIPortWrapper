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


## 3. Unsigned Installer / Executable
**Issue:** The `PBIPortWrapper.msi` installer and the executable are not
code-signed with an Authenticode certificate.

**Impact:** On first run, Windows SmartScreen shows *"Windows protected your PC"*
and Defender may prompt. Users must click **More info → Run anyway** to proceed.

**Root Cause:** No code-signing certificate is in place. A trusted certificate
carries an ongoing cost and, for standard (OV) certificates, still requires a
SmartScreen reputation ramp.

**Workaround:** Approve the SmartScreen prompt (**More info → Run anyway**), and
verify the download against the release's published hash/size beforehand. The
portable ZIP behaves the same way.

**Status:** Accepted for now (see issue #35). Signing may be revisited if adoption
warrants the cost. See [docs/installer.md](docs/installer.md) for details.
