using System;
using System.Text;

namespace PBIPortWrapper.Models
{
    /// <summary>
    /// One request through the XMLA endpoint, as the access log records it (#128).
    ///
    /// This answers "who connected, to which model, when" and nothing else. It is
    /// deliberately not <c>LogPayloads</c>, which writes whole SOAP bodies including
    /// query results and is a debugging switch: an access log has to be safe to leave
    /// on, which means it must never contain the data itself.
    /// </summary>
    public sealed class AccessLogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>The authenticated account, or "anonymous" when nothing was checked.</summary>
        public string Caller { get; set; }

        public string RemoteAddress { get; set; }

        /// <summary>The client's user-agent, which is how a tool identifies itself.</summary>
        public string Client { get; set; }

        /// <summary>The model addressed, taken from the URL path.</summary>
        public string Model { get; set; }

        /// <summary>Discover or Execute.</summary>
        public string Verb { get; set; }

        /// <summary>
        /// What was asked for: a Discover's RequestType, or an Execute's command. It is
        /// the difference between someone browsing metadata and someone running a query,
        /// which is most of what makes this log worth reading.
        /// </summary>
        public string Detail { get; set; }

        /// <summary>
        /// <c>ok</c>, <c>fault</c>, <c>challenged</c>, <c>unauthorized</c> or
        /// <c>not-allowed</c>.
        ///
        /// <c>challenged</c> and <c>unauthorized</c> are deliberately different words.
        /// A caller that has sent no credentials yet is being asked for them, which is
        /// the first half of every Basic exchange and happens once per request; a caller
        /// whose credentials were wrong is a different event entirely, and collapsing
        /// the two would bury it (#132).
        /// </summary>
        public string Outcome { get; set; }

        public long DurationMs { get; set; }
    }

    /// <summary>
    /// The access log's on-disk shape: CSV, so it opens in Excel. That is not a joke —
    /// the people running this are the people who already have Excel pointed at the
    /// endpoint, and "sort by caller" should not require a log viewer.
    /// </summary>
    public static class AccessLogFormat
    {
        public const string Header =
            "Timestamp,Caller,RemoteAddress,Client,Model,Verb,Detail,Outcome,DurationMs";

        public static string Line(AccessLogEntry e)
        {
            if (e == null) return string.Empty;

            var sb = new StringBuilder();
            Append(sb, e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss"));
            Append(sb, e.Caller);
            Append(sb, e.RemoteAddress);
            Append(sb, e.Client);
            Append(sb, e.Model);
            Append(sb, e.Verb);
            Append(sb, e.Detail);
            Append(sb, e.Outcome);
            sb.Append(e.DurationMs.ToString());
            return sb.ToString();
        }

        private static void Append(StringBuilder sb, string value)
        {
            sb.Append(Escape(value));
            sb.Append(',');
        }

        /// <summary>
        /// CSV quoting, because a user-agent is attacker-adjacent free text: it arrives
        /// from the network and routinely contains commas. A field that shifts every
        /// later column by one is worse than no log at all, since it looks readable.
        /// </summary>
        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;

            // Newlines would end the record early, so they are folded rather than quoted.
            string flat = value.Replace("\r", " ").Replace("\n", " ");

            if (flat.IndexOf(',') < 0 && flat.IndexOf('"') < 0) return flat;
            return "\"" + flat.Replace("\"", "\"\"") + "\"";
        }
    }
}
