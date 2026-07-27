using System;
using System.Collections.Generic;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Reports a repeating event once, then keeps count (#132).
    ///
    /// A failed sign-in is worth a warning; the same failed sign-in three times in one
    /// second is worth one warning and a number. Clients retry, so one wrong password
    /// typed by one person produced three identical lines — and a log where the same
    /// sentence repeats is a log people stop reading, which costs exactly the attention
    /// a failed sign-in is supposed to attract.
    ///
    /// Nothing is lost by summarising here: the access log records every attempt
    /// individually. This governs what is worth *saying*, not what is kept.
    /// </summary>
    public class RepeatedEventLimiter
    {
        private readonly TimeSpan _window;
        private readonly object _lock = new object();
        private readonly Dictionary<string, State> _seen = new Dictionary<string, State>(StringComparer.OrdinalIgnoreCase);

        private sealed class State
        {
            public DateTime LastReported;
            public int SuppressedSince;
        }

        public RepeatedEventLimiter(TimeSpan window)
        {
            _window = window;
        }

        /// <summary>
        /// Whether this occurrence should be reported, and how many went unreported
        /// since the last one that was. The count is only meaningful when the return
        /// value is true — it is what that report should mention.
        /// </summary>
        public bool ShouldReport(string key, DateTime now, out int suppressedSinceLast)
        {
            suppressedSinceLast = 0;
            if (key == null) key = string.Empty;

            lock (_lock)
            {
                if (!_seen.TryGetValue(key, out State state))
                {
                    _seen[key] = new State { LastReported = now };
                    return true;
                }

                if (now - state.LastReported < _window)
                {
                    state.SuppressedSince++;
                    return false;
                }

                suppressedSinceLast = state.SuppressedSince;
                state.SuppressedSince = 0;
                state.LastReported = now;
                return true;
            }
        }

        /// <summary>Forgets everything, so a fresh run starts loud.</summary>
        public void Reset()
        {
            lock (_lock) { _seen.Clear(); }
        }
    }
}
