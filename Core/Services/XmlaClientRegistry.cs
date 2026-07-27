using System;
using System.Collections.Generic;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Remembers which XMLA clients have been seen since the endpoint started, so each
    /// distinct one is announced once instead of on every request (#149).
    ///
    /// Excel alone sends around fifty requests per session, so naming the client on each
    /// of them would be unreadable — and putting it only on the per-request Debug line
    /// would mean it appears solely when full payload logging is on. Once per client, at
    /// Info, is what makes a log answer "which client was this?" without being turned up
    /// first. That question is currently unanswerable, which is why an engine fault seen
    /// on 2026-07-26 still has no client attached to it.
    ///
    /// This is not access logging (#128): it records that a kind of client appeared, not
    /// who did what when.
    /// </summary>
    public class XmlaClientRegistry
    {
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new object();

        /// <summary>
        /// True the first time this user-agent has been seen for this qualifier - the
        /// account it signed in as, or the address it arrived from. A client is
        /// identified by both, so the same
        /// tool signing in as two different users is announced twice — which is exactly
        /// what a compatibility pass across authentication modes needs to see.
        ///
        /// Callers pass distinct qualifier wording for distinct facts, so a client
        /// arriving and a client signing in are announced separately rather than one
        /// swallowing the other.
        /// </summary>
        public bool IsNew(string userAgent, string qualifier)
        {
            string key = Describe(userAgent) + " " + (qualifier ?? string.Empty);
            lock (_lock)
            {
                return _seen.Add(key);
            }
        }

        /// <summary>
        /// Forgotten when the endpoint restarts, so each run announces its clients
        /// again. A run is the unit a diagnosis is scoped to.
        /// </summary>
        public void Reset()
        {
            lock (_lock) { _seen.Clear(); }
        }

        /// <summary>
        /// The user-agent as it should appear in the log. MSOLAP clients identify
        /// themselves here; a caller that sends nothing is worth naming as such, because
        /// "no user-agent" is itself a clue about what is connecting.
        /// </summary>
        public static string Describe(string userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent)) return "(no user-agent)";

            string trimmed = userAgent.Trim();
            return trimmed.Length > 120 ? trimmed.Substring(0, 120) + "…" : trimmed;
        }
    }
}
