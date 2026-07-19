# PBI Port Wrapper v0.5.1 — hardening patch

Two user-facing fixes on top of the v0.5.0 serve-sessions release:

- **Single-instance guard (#64)** — launching a second wrapper now fronts the
  existing window and exits, instead of two processes silently sharing
  config/log and competing for the same fixed ports. A crashed wrapper never
  blocks the next start, so crash recovery keeps working.
- **Unsaved ("Untitled") models (#9)** — configuration is now blocked with a
  visible *Unsaved* status until the .pbix is saved. Previously, Set Port
  looked like it worked but the rule was silently dropped or orphaned once the
  model got its real name.

Full details in [CHANGELOG.md](CHANGELOG.md).
