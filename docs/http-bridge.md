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

### Read-only models (#129)

Read-only is **per model**, not per endpoint, and lives on the model's rule:

```json
"Models": [ { "ModelNamePattern": "Sales", "RenamedDatabaseName": "Sales", "ReadOnly": true } ]
```

Per model rather than global because each model answers on its own path, so the relay
always knows which one a request is for — unlike reachability, which is one listener and
could only ever be global.

It defaults to **on**, including for config files written before it existed: the
property initializer supplies the value when the JSON has no such key, so upgrading
tightens rather than quietly leaving every model writable. `ConfigVersion` stays at
`2` — no migration runs over these files.

`XmlaCommandClassifier` decides. It is an **allow list**, not a deny list of the
mutating ones: a deny list fails open, and anything added to XMLA later would pass a
gate whose entire purpose is to stop it. Allowed are:

| Command | Why it is a read |
|---|---|
| `Statement` | Carries the query — unless it carries TMSL, see below |
| `Discover` | The XMLA read verb; returns metadata and has no form that writes. One arriving as the body's own verb never reaches the classifier at all, so refusing a nested one contradicted the surrounding code |
| `Cancel` | Aborts a running request; changes no model state |
| `BeginTransaction` / `CommitTransaction` / `RollbackTransaction` | Cannot themselves change a model, and everything they wrap is judged on its own — a refused command stays refused whether or not a transaction is open |

The list is a closed set of commands shown to be reads, not a growing list of things
that turned out to break. A command that provably cannot mutate can only ever prevent a
false refusal, never cause one.

A `Statement` carrying **TMSL** — JSON rather than DAX or MDX — counts as a write. That
is how Tabular Editor writes, so a gate that waved every `Statement` through would allow
exactly what it promised to refuse.

Container commands (`Batch`, `Sequence`, `Parallel`) carry no meaning of their own, so
they are judged by their contents: a container is a read exactly when everything inside
it is, however deeply nested. Refusing them whole was a regression — Tabular Editor
reads a model's state through a `Batch`, so it could not open a read-only model at all.
A refusal names the path to the offending command (`Batch > Parallel > Process`), in the
log and in the fault the client displays.

Session-scoped MDX (`CREATE SESSION CUBE`, `CREATE MEMBER`) is deliberately allowed: it
is how Excel builds calculated members, it dies with the session, and it never reaches
the model. DAX has no write syntax, and MDX cell writeback needs a writeback-enabled
partition that a Power BI Desktop model does not have.

A refused command is rejected **before** `SendToEngine`, so nothing reaches msmdsrv, and
the SOAP fault names both the command and how to allow it.

### Access logging (#128)

One CSV line per request in `access.csv`, next to `log.txt` in
`%APPDATA%\PBIPortWrapper\`. On by default.

```
Timestamp,Caller,RemoteAddress,Client,Model,Verb,Detail,Outcome,DurationMs
2026-07-27 12:28:07,PASCAL,10.9.20.21,MSOLAP 17.0 Client,Sales,Discover,MDSCHEMA_CUBES,ok,41
2026-07-27 12:28:19,PASCAL,10.9.20.21,ADOMD.NET,Sales,Execute,Alter,fault,0
```

`Outcome` is `ok`, `fault`, `unauthorized` or `not-allowed`.

**A bare challenge is not recorded.** Under `Password sign-in` a client sends its first
request without credentials, is challenged, and retries with them - and because a client
opens a new connection per request, that happens every time. Recording it would put a
blank row in front of every genuine one and double a file whose whole purpose is to be
readable. The request that carried credentials is the access event; the challenge is the
handshake before it. A client that never authenticates at all is still visible, in
`log.txt`, through the once-per-run arrival line.

`unauthorized` - credentials supplied and *wrong* - is a different matter and every
attempt is recorded here individually. `log.txt` warns about the first and then reports
a count, because clients retry and one wrong password produced three identical lines;
this file keeps them all, because it is the record rather than the running commentary. A fault is still a
completed request and is recorded as one - "who connected" is less useful than "who
connected and got nowhere". `Detail` is a Discover's RequestType or an Execute's
command, which is the difference between someone browsing metadata and someone running
a query.

**It never contains the data, or the question asked of it.** A `Statement` is recorded
as `Statement`, never as the query it carries. That is the whole distinction from
`LogPayloads`, which writes entire SOAP bodies including results and is a debugging
switch: an access log has to be safe to leave on, so this one is, and it is on by
default. A config written before it existed loads with it on.

A separate file rather than lines in `log.txt` because Excel sends around fifty
requests per session and `log.txt` is mirrored into the dashboard - per-request entries
there would bury everything else. CSV because the people running this already have
Excel pointed at the endpoint, and "sort by caller" should not need a log viewer.

It rotates to `access.prev.csv` at 5 MB, keeping one generation; more would be
archiving. If it cannot be written it says so once and then stays quiet - the
endpoint's job is to serve, not to keep a diary.

Tray -> **XMLA endpoint** -> **Access log**, or the dashboard's **XMLA endpoint...**
dialog, which carries the same switch and an **Access log...** button. Both surfaces
show one setting, so neither can drift from the other.

**Opening it opens a copy.** Excel holds an open workbook for as long as its window is
open, and a held file cannot be appended to - so opening the live access log to read it
would stop it recording the very requests you opened it to look at. Both surfaces
therefore copy it to `%TEMP%\PBIPortWrapper\` first and open that. The snapshot does not
update, which is the correct trade: a log you can read and that keeps recording beats a
live view that costs you the data.

If the live file does get held anyway - by opening it directly from Explorer, say -
every write is still retried, so recording resumes by itself the moment it is released.
Requests during the hold are lost, and the endpoint says so once rather than once per
request. An earlier version gave up for the rest of the run, which made looking at the
log a way to lose it.

**One gap worth knowing.** A client that never answers the password challenge is not in
here: with `Basic` the listener answers the 401 itself, so the request never reaches the
handler that writes these lines. Those appear in `log.txt` as `XMLA request arriving
from ...` instead - see the diagnosis note below.

### Client compatibility (#149)

Measured against a real served model, not inferred. Every client here is MSOLAP or
ADOMD.NET underneath, so what differs is how each one is *told* to connect, not the
protocol.

| Client | User-agent in the log | Result |
|---|---|---|
| Excel | `MSOLAP <version> Client` | Works in both authentication modes |
| DAX Studio | `ADOMD.NET` | Works. With `Password sign-in` its server box fails `(401)` — it connects with integrated security and never answers the Basic challenge. A full connection string with `User ID=`/`Password=` works |
| Tabular Editor | `ADOMD.NET` | Reads through a `Discover` inside a `Batch`; saves through a `Batch > Create`, which a read-only model refuses by design |
| Power BI Desktop | `MSOLAP <version> Client` | Works with `Anonymous`. Fails with `Basic`: it does not answer the challenge |

**Power BI Desktop, and a diagnosis that was half right.** Desktop works over
`Anonymous`, so it does speak XMLA over HTTP. It fails over `Basic`.

The reported `DIME protocol error: The '9' DIME version is not supported` is worth
recording, because the number identifies the mechanism. A DIME record header carries
`VERSION` in its top five bits, so version = `byte >> 3`; every HTTP response begins
`"HTTP/1.1 …"`, and `'H'` is `0x48`, giving exactly 9. So the client read an HTTP
response where it expected framed data.

The first conclusion drawn from that — that Desktop must therefore be speaking native
TCP rather than HTTP — was **wrong**, and Desktop working under `Anonymous` disproves it.
The response it choked on was the **401 challenge**: it does not answer a password
challenge, and then fails to parse the challenge response. Same root cause as DAX
Studio's server box, surfacing as a much stranger message.

The lesson worth keeping: an explanation that accounts for the evidence is not the same
as the only explanation that does. The arithmetic was right about *what* was being
misread and wrong about *why* it was there.

**Diagnosing a client that will not connect.** `XMLA request arriving from …` is logged
at Info before authentication, so it appears even for a request the listener rejects
with a 401. That is the discriminator:

- **A line appears** → it reached us. The problem is authentication or routing, and the
  following lines say which.
- **No line at all** → it never sent an HTTP request. The problem is in the client, and
  no change here will fix it.

Every request reaches the handler, including one about to be refused, so a 401 appears
in the access log as `unauthorized` with the client named. That was impossible while the
endpoint ran on http.sys, which answered the Basic challenge itself and never handed the
request over - the blind spot that made a DAX Studio attempt look like it had never
arrived (#149).

### LAN reachability

The endpoint binds every address as an ordinary user, so the only thing standing between
a remote client and the port is the firewall. Once, as Administrator:

```powershell
New-NetFirewallRule -DisplayName "PBI Port Wrapper XMLA Bridge" -Direction Inbound `
  -Protocol TCP -LocalPort 55555 -Action Allow -Profile Domain,Private
```

> **Upgrading from a version before #132:** the endpoint used to run on http.sys, which
> needed a `netsh http add urlacl` reservation to bind anything but localhost, and fell
> back to localhost silently without one. Kestrel does not, so any reservation you made
> is now unused. `netsh http show urlacl` lists them and
> `netsh http delete urlacl url=http://+:55555/` removes one.

### HTTPS (#132)

Off by default. An endpoint that stopped answering after an upgrade would be a worse
failure than one that is not yet encrypted.

```json
"HttpBridge": {
  "Enabled": true, "Port": 55555, "AuthMode": 2,
  "UseHttps": true,
  "CertificateThumbprint": "",
  "CertificatePath": "C:\\Users\\you\\AppData\\Roaming\\PBIPortWrapper\\certs\\fullchain.pem",
  "CertificateKeyPath": "C:\\Users\\you\\AppData\\Roaming\\PBIPortWrapper\\certs\\privkey.pem"
}
```

**The app consumes a certificate; it never creates or obtains one.** Serving TLS is the
easy half - being *trusted* is the hard one, and a certificate this app generated would
be trusted by nobody, so every client machine would need it installed by hand. That cost
is what makes self-hosted TLS not worth doing. A certificate from a CA the clients
already trust needs no client-side work at all, and anyone in a position to run this
already has a way to get one.

Three sources, because the ways people already have one differ:

| Setting | For |
|---|---|
| `CertificatePath` + `CertificateKeyPath` | A **PEM pair** - `fullchain.pem` and `privkey.pem` - which is what Let's Encrypt clients emit and what a reverse proxy like Nginx Proxy Manager hands out. **Recommended:** the only route where renewal is hands-off. |
| `CertificateThumbprint` | A certificate in the Windows certificate store - where a Windows ACME client puts it. Windows guards the private key, which is the strongest of the three. LocalMachine is searched first, then CurrentUser. |
| `CertificatePath` alone | A PFX file, which is what some ACME clients on another host produce. It must carry its private key. |

A thumbprint is checked first, so a configuration with both never silently serves a
different certificate than the one it names.

**Why the PEM pair is the one to reach for.** A renewed certificate is a *different*
certificate: its thumbprint changes, so the store route needs a re-import **and** a
config edit every sixty days, and the PFX route needs the conversion re-run. Two files
rewritten in place by whatever already renews them need neither - which is the whole
point of the per-connection reload below.

> **The trap this route hides.** A certificate built from PEM on Windows carries an
> *ephemeral* private key, and SChannel refuses to serve with one: Kestrel accepts the
> certificate, `HasPrivateKey` reports true, and every handshake then dies with the
> client seeing nothing but a closed connection. `CertificateResolver` round-trips it
> through PKCS#12 in memory, which gives the key a home Windows will serve from. Verified
> against a real endpoint both ways - direct use fails, the round-trip succeeds.

**There is deliberately nowhere to configure a PFX password**, and for the same reason
the PEM key may not be passphrase-protected. A password in
`config.json` is a stored credential in clear text, which this project does not do
anywhere else and will not start doing for the feature whose entire point is
confidentiality. A protected PFX belongs in the certificate store, where Windows guards
the key; then configure its thumbprint. The error message says so when it hits one.

Thumbprints are normalised before comparison. Copying one out of the Windows certificate
dialog brings invisible left-to-right marks and spaces with it, and pasted into a config
file that value looks identical to the correct one and matches nothing.

#### Renewal

A Let's Encrypt certificate is replaced roughly every sixty days, and this app runs for
months from a login. A certificate captured at startup would quietly go stale and present
as *"clients suddenly cannot connect"*.

So the certificate is chosen per connection, through Kestrel's
`ServerCertificateSelector`, and the source is re-read at most every five minutes. A
renewal is live within minutes, with no restart and nothing for anyone to remember; it is
announced in the log when it happens. Clients already connected keep the old certificate
until they reconnect, which is simply how TLS works.

A re-read that fails - which is what a renewal looks like while the file is being written
- keeps the certificate already in hand rather than taking the endpoint down.

Startup names the certificate and its expiry, and warns inside fourteen days or if it has
already expired. That is a thing to notice on a Tuesday rather than discover from a client
one morning.

#### In the app

**XMLA endpoint… → Encrypt connections (HTTPS)** carries all of it: the switch, a
**source** picker (PEM pair / Windows certificate store / PFX file) showing only the
fields that source uses, a file browser, and a line saying what the settings currently
resolve to - the subject and expiry, or the resolver's reason there is no certificate.

Two rules the UI enforces that the config file cannot:

- **One source at a time.** Choosing a source clears the others. The resolver checks the
  thumbprint first, so one left behind from an earlier attempt would quietly win over the
  pair just chosen - serving a certificate the settings appear to have replaced.
- **HTTPS will not switch on while the certificate does not resolve**, and says why.
  Otherwise the setting is accepted, saved, and the endpoint fails to start on the next
  apply - a much worse way to find out a path was mistyped.

The status line at the top of the dialog names the certificate being **served**, which is
not necessarily what the fields below resolve to: those describe what the next start
would use.

### Why Kestrel rather than HttpListener

`HttpListener` runs on http.sys, where two things need administrative rights: binding
anything but localhost (`netsh http add urlacl`) and serving TLS (`netsh http add
sslcert`). Kestrel binds the socket itself and takes a certificate in code, so neither
does. Verified unelevated before the change was made: a self-signed certificate served a
real HTTPS request with no `sslcert` binding created or needed.

That makes HTTPS (#132) possible at all here, and removes an administrative step in the
same move.

The cost is that `HttpListener` supplied Basic and Negotiate through
`AuthenticationSchemes`, and Kestrel does not. Basic is now decoded by
`BasicCredentials` and validated by `WindowsCredentialValidator` exactly as before —
nothing is stored and Windows is still the only thing that says yes. Negotiate is no
longer offered (#164): it has never worked on a host that is not domain-joined, which is
where this runs.

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
