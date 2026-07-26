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

**Status:** Accepted, and load-bearing rather than incidental. Renaming at the
source is what makes the alias the database's *real* name, so the XMLA endpoint
can hand the engine every request untouched (see docs/serving-workflow.md and
docs/http-bridge.md). The alternative — a transparent proxy rewriting names on
the wire — was investigated and shelved; it makes the wrapper responsible for
being byte-compatible with the engine's serialiser forever.


## 2. Unsigned Installer / Executable
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


## 3. ⚠️ The XMLA Endpoint Has No Encryption

> **⚠️ WARNING — use the XMLA endpoint only on a network you trust.**
> Everything it carries, including passwords, travels in the clear.

**Issue:** The XMLA endpoint speaks plain HTTP. There is no TLS.

**Impact:** Anyone able to observe traffic between a client and this machine can
read:

- the **password** of any `Password sign-in` caller. HTTP Basic transmits
  credentials base64-encoded, which is *encoding, not encryption* — trivially
  reversible by anyone who captures the request. The account is a **real Windows
  account on the host**, so a captured password is a real credential.
- every **query and result** — that is, your model's data.

**Root Cause:** HTTPS needs a certificate bound to the port, which is a
meaningfully larger piece of work than the endpoint itself (issue #132).

**Workaround:** Treat the endpoint as a trusted-LAN feature.

- Do not expose the port to the internet, and do not port-forward it on a router.
- Prefer a **dedicated local Windows account** for remote callers rather than one
  you use elsewhere, so a captured password costs you as little as possible.
- On an untrusted network, put it behind something that does provide transport
  security — a VPN, or an SSH tunnel.
- `No authentication` is worse still: anyone who can reach the port can read
  **and modify** every served model. It is for isolated networks only.

**Status:** Accepted as a deliberate decision. Authentication is genuinely
enforced (the password is verified against Windows), but it is **authenticated,
not confidential**. HTTPS is tracked as issue #132.


## 4. Remote Access Needs Two Manual Steps
**Issue:** Reaching the endpoint from another machine requires a URL reservation
and a firewall rule, both administrative and neither done by the app.

**Impact:** Without the URL reservation the listener falls back to `localhost`
only. That is the awkward part: the endpoint reports itself as running, the tray
shows no error a casual glance would catch, and every remote client simply
cannot connect. Without the firewall rule, connections are dropped instead.

**Root Cause:** Binding `http://+:PORT/` requires either an administrative
process or a standing `netsh http` URL reservation. The app deliberately does
not run elevated, and adding a firewall rule is not something it should do
silently on a user's behalf.

**Workaround:** Run once, as Administrator:

```powershell
netsh http add urlacl url=http://+:55555/ user=Everyone
New-NetFirewallRule -DisplayName "PBI Port Wrapper XMLA" -Direction Inbound -LocalPort 55555 -Protocol TCP -Action Allow
```

The tray detects the localhost fallback, says so in the endpoint status, and
offers to copy the `urlacl` command. Note that a reservation is **per port and
per path** — a reservation for an old port or for `.../xmla/` does not cover
`http://+:55555/`.

**Status:** Accepted. An installer-time reservation is possible (the MSI already
runs elevated) but would bake in a port choice the user can change afterwards.
