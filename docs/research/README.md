# Archived research

Investigations that shaped the current design, kept because the decisions they
justify are still load-bearing. Everything here is **historical**: it describes the
codebase as it was, not as it is.

These pages lived in the Gitea wiki until July 2026, when the wiki was retired —
it had drifted, nothing linked to it, and `docs/` was already where design work
went. They were salvaged rather than deleted; the other three wiki pages were not
(two duplicated `docs/installer.md` and the release build command, and a v0.x
priority matrix had been overtaken by events).

## The v1.0 database-alias investigation, December 2025

Read in order. Together they are the evidence behind the rule stated at the top of
`Core/Services/XmlaRelay.cs` — **the endpoint relays XMLA, it does not translate
it** — and behind serving working by renaming the database at the source instead.

| | |
|---|---|
| [01 — Gap analysis](2025-12-01-gap-analysis.md) | What a full XMLA protocol proxy would have to do: message framing, SOAP parsing, rewriting database references in both directions, stream reassembly. |
| [02 — Alternative approaches](2025-12-02-alternative-approaches.md) | Two cheaper routes proposed instead: a middle-tier ADOMD.NET proxy, and renaming the database directly on the Desktop instance. |
| [03 — Results](2025-12-03-results.md) | Both prototyped, both rejected. ADOMD.NET is a client library and cannot accept connections at all; direct rename worked but broke Power BI Desktop. |

The conclusion said to close the investigation and consider a scoped feature built
on direct rename "with accepted limitations". That is exactly what shipped: serving
renames the database at the source, Desktop is unusable while it does, and that
limitation is documented rather than engineered around
(see [KNOWN_LIMITATIONS.md](../../KNOWN_LIMITATIONS.md) §1).

Worth knowing before proposing anything that rewrites the wire: this has been tried.
