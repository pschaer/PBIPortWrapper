using System;
using System.Linq;
using System.Xml.Linq;

namespace PBIRelay.Services
{
    /// <summary>
    /// Names what a request asked for, in one word, for the access log (#128).
    ///
    /// A Discover carries its RequestType — <c>MDSCHEMA_CUBES</c>, <c>DBSCHEMA_CATALOGS</c>
    /// — and an Execute carries a command. Recording it is the difference between a log
    /// that says someone connected and one that says whether they browsed metadata or
    /// ran a query, which is most of what makes "who is using my models" worth asking.
    ///
    /// It never returns the payload itself. The access log has to stay safe to leave on,
    /// so a Statement is reported as "Statement" and never as the query it contains.
    /// </summary>
    public static class XmlaRequestSummary
    {
        public static string Describe(XDocument envelope)
        {
            if (envelope == null) return string.Empty;

            // A Discover says what it wants outright.
            string requestType = XmlaRelay.ExtractRequestType(envelope);
            if (!string.IsNullOrEmpty(requestType)) return requestType;

            XElement command = envelope.Descendants()
                .FirstOrDefault(e => e.Name.LocalName.Equals("Command", StringComparison.OrdinalIgnoreCase));

            XElement first = command?.Elements().FirstOrDefault();
            return first?.Name.LocalName ?? string.Empty;
        }

        /// <summary>
        /// Parses without throwing. A malformed envelope is a thing that happened and
        /// belongs in the access log as much as a valid one does — it just has less to
        /// say about itself.
        /// </summary>
        public static XDocument ParseOrNull(string soapEnvelope)
        {
            if (string.IsNullOrWhiteSpace(soapEnvelope)) return null;
            try { return XDocument.Parse(soapEnvelope); }
            catch (System.Xml.XmlException) { return null; }
        }

        /// <summary>
        /// Whether a response envelope carries a SOAP fault.
        ///
        /// Matches the element name rather than the word: a self-closing
        /// <c>&lt;soap:Fault /&gt;</c> has no <c>Fault&gt;</c> in it, and a faultstring
        /// that happens to contain "fault" is not one. Scanning beats parsing here
        /// because this runs on every response and the answer is one bit.
        /// </summary>
        public static bool IsFault(string soapResponse)
        {
            if (string.IsNullOrEmpty(soapResponse)) return false;

            // The sentinels are spaces: neither starts a name, and whitespace ends
            // one, so a match at either edge of the string decides correctly.
            const string name = "Fault";
            int i = 0;
            while ((i = soapResponse.IndexOf(name, i, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                char before = i > 0 ? soapResponse[i - 1] : ' ';
                int end = i + name.Length;
                char after = end < soapResponse.Length ? soapResponse[end] : ' ';

                bool startsAName = before == '<' || before == ':';
                bool endsAName = after == '>' || after == '/' || char.IsWhiteSpace(after);
                if (startsAName && endsAName) return true;

                i = end;
            }

            return false;
        }
    }
}
