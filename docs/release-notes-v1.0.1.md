# PBIRelay v1.0.1

A maintenance release. It sharpens the taskbar icon, fixes three defects in how the
application logs, and adds one switch for turning log detail up. Serving, the endpoint and
the configuration you already have are all untouched.

## Fixed

- **The taskbar and tray icons looked smeared**, with the leftmost chart bar running into
  its neighbour. The artwork was never the problem: the app built its icon from a single
  256×256 image, so Windows had no other size to choose from and shrank that one picture
  down to 24 pixels for the taskbar and 16 for the tray. At that reduction the one-pixel
  gaps between the bars disappear.

  The icon file has always carried properly drawn small sizes — they simply were not being
  used. Now each place the icon appears asks for the size it actually draws at. **The icon
  itself is unchanged**; it is the same design, finally rendered at the resolution it was
  drawn for.

- **A crash opened a second logger.** The global exception handler constructed its own
  `LoggerService` rather than using the one the application already owned — a second lock
  object writing the same `log.txt`, at the one moment you most want the file to be
  trustworthy. It now shares the single instance, and only falls back to a new one if the
  crash happens before that instance exists.

- **Turning up the log detail forced query results into it.** One setting, `LogPayloads`,
  did two jobs: it decided whether SOAP payloads were written *and* it was the only way to
  raise the log from Info to Debug. So "show me which requests are arriving" and "write
  every query and its results to disk" could not be asked for separately — and the second
  is not something to leave on.

## Added

- **`VerboseLogging`** — the missing half of that split. It raises `log.txt` to Debug
  detail, which is where per-request routing lines live, without writing payloads:

  ```json
  "HttpBridge": { "VerboseLogging": true }
  ```

  Off by default. Like `LogPayloads`, it is a configuration-file setting with no control in
  the UI, and it takes effect at startup. `LogPayloads` still raises the level on its own,
  so nothing you had configured behaves differently.

## Removed

- `LogConnectionInfo` and `LogConnectionClosed` — logging helpers that described the
  forwarding proxy retired back in v0.8, with no callers since — and `SetMinimumLogLevel`,
  an unused duplicate of the setter the endpoint coordinator actually uses. Internal only;
  no configuration key and no UI element disappears with them.

## Upgrading from v1.0.0

Install the MSI over the top. **Nothing to migrate** — same configuration folder, same
endpoint, same connection strings, same aliases. An existing `config.json` loads unchanged;
the new `VerboseLogging` key is simply absent, which reads as off.

If the taskbar icon still looks the way it did, Windows is showing you a cached copy rather
than the new one — it caches icons aggressively. Signing out and back in clears it.

## Install

- **Installer (recommended):** download `PBIRelay-v1.0.1-win-x64.msi`, run it, and launch
  from the Start Menu or the Power BI Desktop External Tools ribbon.
- **Portable ZIP:** download and extract `PBIRelay-v1.0.1-win-x64.zip`, then run
  `PBIRelay.exe`.

The installer and executable are **not code-signed**; Windows SmartScreen warns on first run
(*More info → Run anyway*). The limits on what this tool claims are unchanged from v1.0.0 —
see [KNOWN_LIMITATIONS.md](../KNOWN_LIMITATIONS.md) and the
[v1.0.0 notes](release-notes-v1.0.0.md). Full detail in [CHANGELOG.md](../CHANGELOG.md).
