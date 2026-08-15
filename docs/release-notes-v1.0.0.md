# PBIRelay v1.0.0

Two things happened: the tool got a name that describes what it does, and it earned a 1.0.

**There are no new features in this release.** That is the point. v1.0 adds no scope over
v0.9.0 — scope earned the version numbers up to here, and *use* earned this one.

## The name

This project was called PBI Port Wrapper. It forwarded a TCP port, and the name said so.

Port forwarding was retired in v0.8: there is no forwarded port any more, and no fixed port
per model — models are served **by name** on one endpoint. The name had been describing a
transport that no longer existed for two releases.

It is now **PBIRelay**, which is what it actually does: it relays XMLA to the Power BI
Desktop engine and translates nothing on the way.

What that means for you:

- **Configuration moves** to `%APPDATA%\PBIRelay\`. Nothing migrates it — copy `config.json`
  across by hand, once. Absolute paths *inside* it still point at the old folder; a
  certificate path is the likely one, and the endpoint will name the exact file it cannot
  find if you miss it.
- The install directory, Start Menu entry and External Tools ribbon entry are renamed. The
  MSI keeps its UpgradeCode, so it **upgrades your existing install** rather than sitting
  beside it.
- Release assets are now `PBIRelay-v1.0.0-win-x64.*`. Everything v0.9.0 and earlier keeps
  the old name, because those artifacts install a product that calls itself that.
- The repository is now `pschaer/PBIRelay`. The old URL redirects.

## Why 1.0 now

v0.9.0 shipped HTTPS, read-only-by-default and the access log. What it had not done was
prove any of it outside a test.

The one that mattered was **certificate renewal**. The endpoint picks its certificate per
connection and re-reads the source at most every five minutes, so a certificate replaced in
place goes live without a restart. That is the centrepiece of the HTTPS design — and until
this week it had never once done its job for real.

It has now:

```
[2026-08-16 00:08:19] HTTPS using CN=*.example.net ... valid until 2026-10-18.
[2026-08-16 00:13:26] Certificate renewed: now CN=*.example.net, valid until 2026-11-13.
```

No restart, no configuration change, no downtime. And the replaced file still carried its
**old modification time**, so the renewal never announced itself — which proved something
the planned test would not have: the reload does not watch timestamps. It re-reads on a
timer and compares thumbprints. A design that had watched modification time would have
missed this exact swap.

That is what changed between 0.9.0 and 1.0.0. Not scope — evidence.

## Where the confidence stops

A 1.0 is a claim about trust, so it should say where the trust runs out.

- **Only the PEM-pair certificate route has met reality.** The Windows certificate store and
  PFX routes have unit tests and offline rendering behind them, and no real use.
- **There is no first-run experience.** Deliberately deferred: the app assumes someone who
  already knows what it does.
- **The MSI and executable are unsigned.** SmartScreen warns on first run — *More info → Run
  anyway*.
- **Power BI Desktop must stay open**, and it is unusable for editing while a model is
  served. That is load-bearing rather than incidental.
- **This is not a replacement** for the Power BI Service, Report Server or a Fabric
  capacity. Check your own Power BI licensing before serving models to anyone but yourself.

See [KNOWN_LIMITATIONS.md](../KNOWN_LIMITATIONS.md).

## Upgrading from v0.9.0

1. **Install the MSI.** It replaces PBI Port Wrapper rather than installing alongside it —
   worth confirming in *Settings → Apps* that only one entry remains.
2. **Copy** `%APPDATA%\PBIPortWrapper\config.json` to `%APPDATA%\PBIRelay\`.
3. **Repoint any absolute paths** inside it that still name the old folder.
4. **Re-add the External Tools entry** if you use it — it is now `pbirelay.pbitool.json`.

Nothing else changed. Same endpoint, same port, same connection strings, same aliases.
