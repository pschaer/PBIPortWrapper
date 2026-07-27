# PBI Port Wrapper v0.9.0

v0.8 made the endpoint reachable. This release makes it defensible: it can be
**encrypted**, it **refuses to change your models** unless you say so, and it tells you
**who has been using them**.

## HTTPS

Off by default, because an endpoint that stopped answering after an upgrade would be a
worse failure than one that is not yet encrypted.

The app consumes a certificate and never creates one. Serving TLS is the easy half —
being *trusted* is the hard one, and a certificate this app generated would be trusted by
nobody, so every client machine would need it installed by hand. That cost is exactly
what makes self-hosted TLS not worth doing. One from a CA your clients already trust
needs no client-side work at all.

**XMLA endpoint… → Encrypt connections (HTTPS)**, then pick where the certificate comes
from:

| Source | For |
|---|---|
| **PEM pair** — `fullchain.pem` + `privkey.pem` | What Let's Encrypt clients and reverse proxies such as Nginx Proxy Manager hand out. **Recommended** |
| **Windows certificate store** | A certificate already installed here, by thumbprint |
| **PFX file** | Carrying its private key, and not password-protected |

The dialog says what your settings resolve to — subject and expiry — and will not switch
encryption on until they resolve, rather than letting the endpoint fail to start later.

**Prefer the PEM pair if your certificate renews automatically.** A renewed certificate is
a *different* certificate with a different thumbprint, so the other two routes need a
re-import or a re-conversion every sixty days. Two files replaced in place need neither:
the certificate is chosen per connection and its source re-read every few minutes, so a
renewal is live without a restart and without anything to remember.

There is deliberately nowhere to configure a certificate password. That would be a stored
credential in clear text, in the feature whose entire point is confidentiality.

## Read-only by default

Every model is now served read-only: commands that would change it — `Alter`, `Delete`,
`Backup`, TMSL scripts — are refused with a fault naming the command, and never reach the
engine. Queries are unaffected.

Clear **Read-only** for a model you actually write to. It is per model, so one can be
writable while the rest are not.

## Who is using my models

Every request is recorded in `access.csv`, next to the log: timestamp, caller, client,
model, what was asked, how it went, how long it took. Tray → **XMLA endpoint** →
**Access log** opens it in Excel, as a copy, so reading it does not stop it recording.

On by default and safe to leave on — it records *that* a query ran, never the query or
its results.

## One less manual step

Remote access used to need a `netsh http add urlacl` reservation as well as a firewall
rule, and skipping it produced this design's sharpest failure: the endpoint reported
itself as running, and every remote client simply could not connect.

That is gone. The endpoint no longer runs on http.sys, so it binds every address as an
ordinary user. **The firewall rule is now the only manual step.** Any reservation you
added for an earlier version is harmless and can be removed with
`netsh http delete urlacl url=http://+:55555/`.

## Also

- An endpoint that **fails to start now says so**, instead of leaving the reason in a log
  nobody had cause to open.
- **Windows sign-in (Negotiate) has been removed.** It needed a domain; on a workgroup the
  handshake never completed, so clients hung instead of reporting an error. A stored
  configuration asking for it resolves to password sign-in — never to no authentication.
- Connection strings, `.odc` files and copied URLs carry the right scheme automatically.

## Upgrading

Nothing to do. Existing configuration is read unchanged, and HTTPS stays off until you
turn it on.

If you serve models to clients that write back, note that **read-only is now the default**
— clear it for those models after upgrading.

## Known limitations

Unchanged from v0.8, except that encryption is now available rather than absent. On the
default plain-HTTP setting the endpoint is *authenticated, not confidential*. See
[KNOWN_LIMITATIONS.md](../KNOWN_LIMITATIONS.md).
