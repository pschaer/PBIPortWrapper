# XMLA-over-HTTP bridge (#77)

Status: **relay core with per-model paths (#136), settings in the tray and the
dashboard (#125)**

## 1. What it solves

Power BI Desktop's `msmdsrv.exe` runs as the logged-in Windows user and only accepts
that user over its TCP port. Plain port forwarding therefore cannot serve anyone else —
the **E1 gap** in [serving-workflow.md](serving-workflow.md).

The bridge exposes an XMLA-over-HTTP endpoint instead:

- **Remote leg** (client → bridge): SOAP XMLA over HTTP. The wrapper owns authentication,
  so LAN callers authenticate as themselves.
- **Local leg** (bridge → `msmdsrv`): the wrapper is already the model owner, so it just
  talks to the engine as itself — the same thing serving has done since v0.5.

## 2. It is a relay, not a translator

This is the whole design, and the thing to not regress:

```
HTTP POST body (SOAP envelope, verbatim)
        │
        ▼
XmlaRelay ── reads the model from the URL path; the envelope is never touched
        │
        ▼
Server.SendXmlaRequest(verb, envelope)   ← AMO, public API
        │
        ▼
msmdsrv generates the response
        │
        ▼
HTTP response body (SOAP envelope, verbatim)
```

`Microsoft.AnalysisServices.Server.SendXmlaRequest(XmlaRequestType, TextReader)` accepts
the client's SOAP envelope unmodified and returns the engine's own SOAP response, for
both `Discover` and `Execute` — the only two XMLA verbs. The library handles SSPI, DIME
framing and content-type negotiation internally.

**Why this matters:** the alternative is parsing the request, calling
`GetSchemaDataSet`, and re-serialising a rowset with a hand-built XSD. That makes the
wrapper responsible for being byte-compatible with the engine's serialiser across ~50
DISCOVER/MDSCHEMA/TMSCHEMA rowsets plus the MDX cellset format — chasing MSOLAP error
messages forever (element ordering, `xsd:sequence`, lowercase booleans, `{GUID}` brace
formatting, session headers, …). A first attempt down that road took 24 commits to get
one rowset shape right. Relaying makes the engine right by construction.

If you ever find yourself editing response XML in this component, stop: that is the
translator approach growing back.

## 3. One path per model (#136)

**Each served model has its own path on the one port:**

```
http://host:55555/Sales      →  the engine holding Sales
http://host:55555/Finance    →  the engine holding Finance
http://host:55555/My%20Model →  aliases with spaces are percent-encoded
```

### Why not one address for all models

Because **an XMLA session belongs to one server.** A client opens its session at connect
time — *before* the user picks a database — so a single shared address lands the session
on whichever engine answered first. Selecting a different model then hands that engine a
session it never issued:

```
XMLAnalysisError.0xc10c000a  The '258A6ED0-...' session ID cannot be found.
```

Field-confirmed with two served models: the first worked end to end, the second was
selectable but showed no cubes. Routing was never the problem — the session was simply
foreign there.

A path is genuinely *one server, one engine*, so sessions work natively and the mismatch
stops existing rather than being managed. It is also less code: it removed the catalog
fan-out, the rowset merge, the canonical retry and the fan-out session stripping — and
with them the only place the relay ever touched a response. **The no-rewriting rule is
now absolute in both directions.**

The alternatives are weighed in
[HANDOVER-2026-07-26-v1.0-endpoint.md](HANDOVER-2026-07-26-v1.0-endpoint.md); the short
version is that owning session identity means rewriting response headers plus session
lifecycle forever, and a port per model reinstates the per-model network plumbing this
design exists to remove.

### Why only *served* models resolve

A path resolves to a port only if a serve session is active for that alias
(`ServeSessionService.ActiveSessions`). That is deliberate, not a limitation:

Serving already **renames the workspace database to the stable alias at the source**. So
the alias is both the path the client addresses and the catalog `msmdsrv` actually has,
and no name rewriting is needed anywhere. Without serving, the catalog is a per-session
GUID and the client would have to know it.

Unserved names get a SOAP fault telling the user to serve the model — the request never
reaches an engine. The fault names the paths that *do* resolve, and so does a request to
the bare endpoint, so a misaddressed client gets an instruction instead of a puzzle.

A request whose envelope names a different catalog than its path is **forwarded exactly
as sent** and the mismatch is logged. The engine is the authority on what it accepts;
correcting a request to agree with its path would be the translator growing back.

## 4. Authentication

`BridgeAuthMode` maps onto built-in `HttpListener` authentication schemes, so there is
no hand-rolled credential handling:

| Mode | Value | Scheme | Use when |
|---|---|---|---|
| `Basic` (default) | `2` | `Basic` | **Workgroup hosts.** The supplied credentials are checked against a Windows account on this machine. Give the remote user a local account here. Credentials are base64 on the wire — fine on a trusted LAN, wants TLS beyond it. |
| `Anonymous` | `1` | `Anonymous` | Trusted LAN, zero-touch. No authentication: anyone who reaches the port queries as the model owner. |
| `Windows` | `0` | `IntegratedWindowsAuthentication` | **Domain hosts only.** |

Adding a mode is a switch case in `HttpBridgeService.ToSchemes`, not an auth implementation.

### `HttpListener` does not check Basic passwords

This cost a release-blocking bug, so it is worth stating plainly: with
`AuthenticationSchemes.Basic`, `HttpListener` decodes the `Authorization` header,
exposes the claimed name as `context.User.Identity.Name`, and **admits the request
without verifying the password.** Only a request carrying no credentials at all is
challenged. Measured directly: a nonexistent user with an invented password was
accepted exactly like a real one.

So the password check is the application's, in
`HttpBridgeService.IsAuthenticated` → `WindowsCredentialValidator`. It calls
`LogonUser` with a network logon — the same check a file share performs — and stores
nothing: Windows remains the only authority, and account lockout, expiry and disabled
accounts apply for free. A bare name is looked up on this machine; `MACHINE\user` and
`user@domain` are also accepted.

Failed attempts are logged with the name, never the password.

### Why `Windows` is not the default

Negotiate/NTLM requires the host to be able to authenticate the *caller's* Windows
identity. On a domain that works. On a **workgroup** host a remote caller has no
identity this machine accepts, so the handshake fails inside `HttpListener`, the
exception surfaces on the accept path, and **the client never receives a reply at all** —
Excel simply hangs. That is the E1 barrier reappearing one layer up: moving
authentication from `msmdsrv` to the listener only helps if Windows on the host can
actually authenticate the caller.

The bridge now warns at startup if `AuthMode=Windows` on a non-domain host, and a failed
accept is logged with the auth mode named, rather than passing silently.

**The bridge is off by default** and is never enabled implicitly: it exposes served
models to the network.

## 5. Enabling it

**From the tray** (#125): the **XMLA endpoint** item shows what it is doing, and its
submenu carries the on/off switch, the authentication choice and a Restart.

**From the dashboard**: the **XMLA endpoint…** button opens a dialog with all of it in
one place — status, the switch, authentication, and the two values that have to be
typed rather than picked: the **port** and the **host name**.

Settings apply immediately, from either surface — no app restart, and no OK button.
Both write through `ConfigService`, and `XmlaEndpointCoordinator` applies the change,
which is why the two surfaces can never disagree.

Changing a setting the listener binds with (enabled, port, authentication) rebinds it
and drops any connected client. Changing the host name does not: it only shapes the
URLs shown to users.

A model's own URL is on its tray submenu as **Copy endpoint URL**, offered while the
model is served — which is exactly when that URL resolves.

Everything is still stored in `%APPDATA%\PBIPortWrapper\config.json` and can be edited
there instead:

```json
"HttpBridge": { "Enabled": true, "Port": 55555, "AuthMode": 2, "Hostname": "", "LogPayloads": false }
```

`AuthMode` is stored as an int, like `OnDetection`: `0` = Windows, `1` = Anonymous,
`2` = Basic (the default).

`Hostname` overrides the address published in URLs — set it when the detected address is
not the one users should type (a DNS name, or the right NIC on a multi-homed machine).
Empty means detect it.

`LogPayloads` writes full SOAP request and response bodies — including query results —
to `log.txt`. Debugging only.

### LAN reachability

Binding `http://+:<port>/` needs elevation or a one-time URL ACL. Without it the bridge
falls back to `localhost` only and logs a warning saying so. To allow LAN access, once,
as Administrator:

```powershell
netsh http add urlacl url=http://+:55555/ user=Everyone
New-NetFirewallRule -DisplayName "PBI Port Wrapper XMLA Bridge" -Direction Inbound -LocalPort 55555 -Protocol TCP -Action Allow
```

> **Upgrading:** a reservation is per port *and* per path, and both have changed
> during development — the endpoint used to bind `/xmla/` rather than the port root,
> and the default port was briefly 55556 while the TCP forwarder still owned 55555.
> A reservation for `http://+:55556/xmla/` therefore covers nothing you use now, and
> the only symptom is the quiet localhost fallback. Register the root as above and
> drop stale ones with `netsh http delete urlacl url=<the old url>`;
> `netsh http show urlacl` lists what is registered.

## 6. Connecting from Excel

1. **Data → Get Data → From Database → From Analysis Services**
2. Server name: the model's own URL, e.g. `http://<host-ip>:55555/Sales` — one address
   per model, not one address for the server.
3. Credentials, matching `AuthMode`:
   - `Basic` → **Use the following User Name and Password**, using a local account on
     the host machine.
   - `Anonymous` → either option; nothing is checked.
   - `Windows` → **Use Windows Authentication**, and only on a domain.
4. The model appears as the database at that address; pick it and its cube.

## 7. Verified

Increment 1 was driven end-to-end against a live Desktop workspace over real HTTP with
Windows authentication (`Discover DBSCHEMA_CATALOGS`, `Discover MDSCHEMA_CUBES`,
`Execute` DAX all returning engine-generated rowsets; unserved catalog faulting without
reaching the engine; `GET` rejected with 405).

**Verified with real Excel from a second machine (2026-07-26)** — the #77 feasibility
gate. Excel on `10.9.30.31` connected to a served model over the LAN and completed the
full session: ~48 Discover and Execute requests, **zero SOAP faults**, all resolving
catalog `Sample01` (the stable alias) to the live engine port.

Two things that measurement settled:

- The engine answered every rowset Excel asked for without a single complaint, which is
  the whole argument for relaying rather than translating.
- Excel opens a new connection per request, and each one opens its own AMO connection —
  yet ~18 requests completed within one second. Connection pooling was anticipated as
  the likely bottleneck and is not one at this scale. Leave it alone until something
  demonstrates a need.

That run used `Anonymous`. `Basic` against a local account is not yet exercised by a
real client.

**Per-model paths verified against live engines (2026-07-26)** — two Power BI Desktop
engines behind one endpoint over real HTTP, each addressed by its own path: both
answered `DBSCHEMA_CATALOGS` *and* `MDSCHEMA_CUBES` with engine-generated rows. The
second model returning cubes is the point: that is the rowset that came back empty when
one address spanned both engines. The bare endpoint and an unserved name both faulted
without reaching an engine, naming the paths that resolve.

## 8. Known limitations

- **One AMO connection per request.** The response reader holds the connection until
  closed, so a connection cannot be shared. Excel opens many requests per PivotTable, so
  each pays a connect. Session-affine pooling needs to be informed by how MSOLAP actually
  uses XMLA sessions — measure with a real client first.
- **⚠️ No TLS — the endpoint is authenticated, not confidential.** Basic transmits the
  password base64-encoded, which is encoding rather than encryption, and that password
  is a real Windows account on this host. Queries and results are in the clear too.
  Accepted deliberately as a trusted-LAN feature; see
  [KNOWN_LIMITATIONS.md](../KNOWN_LIMITATIONS.md) §3 and issue #132. Some clients also
  refuse plain-HTTP auth outright (PowerShell's `Invoke-WebRequest` needs
  `-AllowUnencryptedAuthentication`).
- **Connection strings and `.odc` still emit the native `host:port` form.** They move to
  the model URL with #126, where forwarding retires and the URL becomes the only form
  there is — doing it sooner would mean maintaining both.
- **One model per connection.** Each address is one engine, so a client connected to
  `/Sales` sees `Sales`, not every served model. Browsing all models inside one
  connection would require the relay to own session identity — see §3.
- **Connection strings and `.odc` still emit the native `host:port` form.** Switching
  them to the model URL belongs with the endpoint UI (#125), where the user can first
  see and enable the endpoint.
- **Execute is not read-only.** A caller can send any XMLA command the owner could,
  including model mutation (#129). Acceptable for a trusted LAN; revisit before
  anything wider.
