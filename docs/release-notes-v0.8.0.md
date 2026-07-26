# PBI Port Wrapper v0.8.0

**The release where a model stops being addressed by a *port* and starts being
addressed by its *name*.**

Every served model now answers on one HTTP XMLA endpoint, at its own path:

```
Provider=MSOLAP;Data Source=http://your-pc:55555/Sales;Initial Catalog=Sales
```

One address, one firewall rule, one authentication setting — for every model. And
for the first time, **from another machine, as another Windows user**.

## Why this replaces port forwarding

Power BI Desktop's engine (`msmdsrv.exe`) runs as the logged-in user and accepts
only that user, over local TCP. Forwarding a port faithfully forwards that
restriction: a colleague on another machine was never getting in, and a forwarded
port still left the database named as a per-session GUID.

The endpoint changes the shape of the problem. The wrapper is a first-class client
of the engine — it is the owner — so it authenticates callers itself and relays
their XMLA requests through. Serving already renames the database to your alias at
the source, so the name a client asks for is the name the engine actually has, and
nothing has to be rewritten in flight.

Port forwarding is therefore **gone**, not deprecated. So are per-model ports,
network toggles, and the *Set Port* UI. The alias replaced all of it.

## Added

- **XMLA-over-HTTP endpoint** — every served model on one
  port, each at its own path. One path is one server holding one engine, which is
  what lets several open models work at the same time: an XMLA session belongs to
  a server, so a shared address would land every session on whichever engine
  answered first.
- **Remote and cross-user access** — the gap that port forwarding could not
  close. Verified end to end with Excel on a second machine.
- **Authentication modes** — **Password sign-in** (default) checks the
  caller against a real Windows account on the host, so it works on a workgroup;
  **Windows sign-in** for domains; **No authentication** for isolated networks.
- **Endpoint settings in the tray and the dashboard** — enable, port,
  hostname override, authentication, live status and restart. Both surfaces write
  the same config, so they cannot disagree.
- **Copy endpoint URL** per served model, and `.odc` files carrying it.

## Changed

- **The grid speaks aliases** — *Fixed Port* became an editable **Alias**
  column, *Network* is gone, and the *Action* cell is a direct **Serve** / **Stop**
  button.
- **Two states: Off and Serve** — *Forward* is gone as a state, an
  action, and an on-detection policy.
- **`config.json`** calls the model list `Models` rather than `PortMappings`, and
  the per-model port and network fields are gone. Existing config files load
  unchanged — aliases and on-detection policies are preserved.

## Fixed

- **HTTP Basic accepted any password.** `HttpListener` decodes the
  credential header but does not verify it — which was misread as validation for
  three releases in a row. Measured: a nonexistent user with an invented password
  was admitted exactly like a real one. Passwords are now verified against Windows
  directly, and nothing is stored.
- Serving no longer binds or validates a port, so it cannot fail — or roll back a
  completed rename — because some unrelated port was busy.
- Endpoint status no longer says "LAN" when it means every interface.

## ⚠️ Before you expose it

- **There is no TLS.** Password sign-in sends credentials base64-encoded — encoding,
  not encryption — and queries and results travel in the clear. Use it on a trusted
  LAN, never port-forward it from a router, and prefer a dedicated local Windows
  account for remote callers. HTTPS is planned.
- **Remote access needs two one-time administrative steps**, or the endpoint quietly
  serves `localhost` only:

  ```powershell
  netsh http add urlacl url=http://+:55555/ user=Everyone
  New-NetFirewallRule -DisplayName "PBI Port Wrapper XMLA" -Direction Inbound -LocalPort 55555 -Protocol TCP -Action Allow
  ```

  The tray notices the localhost fallback and offers to copy the first command.

## Upgrading

Install over the previous version; your `config.json` is read as-is and rewritten
in the new shape on the next save. Aliases and on-detection policies carry over.

Anything that connected through a **forwarded port** needs to be repointed at the
model's URL — tray → the model → **Copy endpoint URL**. Saved `.odc` files from
v0.7.x point at the old port and should be regenerated.

## Install

- **Installer (recommended):** download `PBIPortWrapper.msi`, run it, and launch
  from the Start Menu or the Power BI Desktop External Tools ribbon.
- **Portable ZIP:** download and extract `PBIPortWrapper-v0.8.0-win-x64.zip`, then
  run `PBIPortWrapper.exe`.

The installer and executable are **not code-signed**; Windows SmartScreen / Defender
warns on first run (*More info → Run anyway*). Full details in
[CHANGELOG.md](../CHANGELOG.md).
