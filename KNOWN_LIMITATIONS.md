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
**Stop** — in the row's Action menu or the tray — and Desktop recovers
immediately, because the original name is restored. If the wrapper crashes
mid-serve, the startup recovery prompt offers the same restoration; and closing
the wrapper restores every served database on the way out.

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


## 3. ⚠️ The XMLA Endpoint Is Not Encrypted Unless You Turn HTTPS On

> **⚠️ WARNING — on plain HTTP, everything it carries, including passwords,
> travels in the clear. Use it only on a network you trust, or turn HTTPS on.**

**Issue:** The XMLA endpoint speaks plain HTTP **by default**. HTTPS is available
(#132) but is off until configured with a certificate, because an endpoint that
stopped answering after an upgrade would be a worse failure than one that is not
yet encrypted. Everything below describes the default.

**Impact:** Anyone able to observe traffic between a client and this machine can
read:

- the **password** of any `Password sign-in` caller. HTTP Basic transmits
  credentials base64-encoded, which is *encoding, not encryption* — trivially
  reversible by anyone who captures the request. The account is a **real Windows
  account on the host**, so a captured password is a real credential.
- every **query and result** — that is, your model's data.

Read-only (#129) does not help here: it restricts what a caller may *do*, not what
an observer may *see*.

**Root Cause:** Encryption cannot be switched on for you, because it needs a
certificate only you can supply.

**Fix:** Turn HTTPS on. It needs a certificate you already have — the app never
creates one, because a certificate it generated would be trusted by nobody and
every client machine would need it installed by hand.

```json
"HttpBridge": {
  "UseHttps": true,
  "CertificatePath": "C:\\path\\to\\fullchain.pem",
  "CertificateKeyPath": "C:\\path\\to\\privkey.pem"
}
```

That is the `fullchain.pem` / `privkey.pem` pair a Let's Encrypt client or a
reverse proxy hands out; a certificate in the Windows certificate store
(`CertificateThumbprint`) and a password-free PFX (`CertificatePath` alone) also
work. Renewals are picked up within minutes without a restart. See
[README](README.md#encryption) and [docs/http-bridge.md](docs/http-bridge.md).

Or set it in the app: **XMLA endpoint… → Encrypt connections (HTTPS)**, which is
the same settings with a file picker and tells you what the certificate resolves
to before you switch it on.

**Workaround, if you have no certificate:** treat the endpoint as a trusted-LAN
feature.

- Do not expose the port to the internet, and do not port-forward it on a router.
- Prefer a **dedicated local Windows account** for remote callers rather than one
  you use elsewhere, so a captured password costs you as little as possible.
- On an untrusted network, put it behind something that does provide transport
  security — a VPN, or an SSH tunnel.
- `No authentication` is worse still: anyone who can reach the port can read
  every served model. It is for isolated networks only. Models are served
  **read-only by default** (#129), which blunts this considerably — a caller who
  reaches the port can read the model but cannot alter or delete it — so clear
  Read-only only for a model you actually write to, and prefer not to leave it
  clear on `No authentication`.

**Status:** Resolved for anyone who turns HTTPS on; the *default* remains
plain HTTP and is what this section describes. On that default, authentication is
genuinely enforced — the password is verified against a real Windows account — but
the channel is **authenticated, not confidential**.


## 4. Remote Access Needs One Manual Step

**Issue:** Reaching the endpoint from another machine requires a firewall rule,
which is administrative and not something the app does on a user's behalf.

**Impact:** Without it, remote connections are dropped.

**Root Cause:** Opening a port in Windows Firewall needs elevation, and an
application that silently opened one would be doing something the user did not
ask for.

**Workaround:** Run once, as Administrator:

```powershell
New-NetFirewallRule -DisplayName "PBI Port Wrapper XMLA" -Direction Inbound `
  -Protocol TCP -LocalPort 55555 -Action Allow -Profile Domain,Private
```

`-Profile Domain,Private` deliberately excludes the public profile: on the default
plain-HTTP setting this endpoint carries credentials and data in the clear
(section 3), so it should not be reachable on a network Windows already considers
untrusted. Omitting the profile opens all three.

**This used to be two steps.** A `netsh http add urlacl` URL reservation was also
required, and skipping it produced the sharpest failure in the design: the
endpoint reported itself as running, the tray showed nothing a casual glance would
catch, and every remote client simply could not connect. That is gone (#132) — the
endpoint no longer runs on http.sys, so it binds every address as an ordinary user
and there is no fallback left to fall into. Any reservation you added for an
earlier version is now harmless and can be removed with
`netsh http delete urlacl url=http://+:55555/`.

**Status:** Accepted, and now one step rather than two.
