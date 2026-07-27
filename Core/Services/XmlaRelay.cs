using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.AnalysisServices;
using PBIPortWrapper.Models;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Relays XMLA SOAP requests from the HTTP endpoint to the local Power BI Desktop
    /// Analysis Services instances (#77).
    ///
    /// This is deliberately NOT a protocol translator. AMO's
    /// <see cref="Server.SendXmlaRequest"/> takes the client's SOAP envelope verbatim
    /// and returns the engine's own SOAP response, so msmdsrv produces the rowsets,
    /// XSD schema and value formatting that MSOLAP expects. Re-serialising rowsets by
    /// hand means chasing byte-compatibility with the engine forever; relaying means
    /// the engine is always right by construction.
    ///
    /// Routing is by URL path: each served model answers on its own path of the one
    /// port (#136), so every address is exactly one server backed by exactly one
    /// engine. That is what makes sessions work — an XMLA session belongs to the
    /// server that issued it, and a client opens its session before it picks a
    /// database, so an address that spans engines hands the wrong engine a session it
    /// never issued. One engine per path removes the possibility.
    ///
    /// It also leaves this component with nothing to rewrite in either direction:
    /// requests and responses both pass through untouched.
    /// </summary>
    public class XmlaRelay
    {
        private readonly Func<IReadOnlyList<ServedCatalog>> _servedCatalogs;
        private readonly ILogger _logger;

        public XmlaRelay(Func<IReadOnlyList<ServedCatalog>> servedCatalogs, ILogger logger = null)
        {
            _servedCatalogs = servedCatalogs ?? throw new ArgumentNullException(nameof(servedCatalogs));
            _logger = logger;
        }

        /// <summary>
        /// Relays one SOAP envelope, addressed to the model named by
        /// <paramref name="requestPath"/>, and returns the response envelope to write
        /// back. Never throws: transport-level problems come back as SOAP faults, which
        /// is what an XMLA client knows how to display.
        /// </summary>
        public string Relay(string soapEnvelope, string soapAction, string requestPath)
        {
            if (string.IsNullOrWhiteSpace(soapEnvelope))
            {
                return CreateSoapFault("Client", "Empty XMLA request.");
            }

            XDocument doc;
            try
            {
                doc = XDocument.Parse(soapEnvelope);
            }
            catch (XmlException ex)
            {
                return CreateSoapFault("Client", $"Malformed XMLA request: {ex.Message}");
            }

            XmlaRequestType verb = ResolveVerb(doc, soapAction);
            string requested = ModelFromPath(requestPath);

            IReadOnlyList<ServedCatalog> served = _servedCatalogs() ?? Array.Empty<ServedCatalog>();
            if (served.Count == 0)
            {
                _logger?.LogWarning("XmlaEndpoint", $"Rejected {verb} on '{requestPath}': no models are served.");
                return CreateSoapFault("Server",
                    "No models are currently served. Serve a model in PBI Port Wrapper first.");
            }

            if (requested.Length == 0)
            {
                // The bare endpoint names no model, so there is nothing to route to.
                // Naming the paths that do work turns an obscure failure into an
                // instruction, in the one place the user is already looking.
                _logger?.LogWarning("XmlaEndpoint", "Rejected a request with no model in the URL path.");
                return CreateSoapFault("Client",
                    $"This endpoint addresses each model by its own path. {AvailableModels(served)}");
            }

            string wanted = NormalizeCatalogName(requested);
            ServedCatalog model = served.FirstOrDefault(
                c => wanted.Equals(NormalizeCatalogName(c.Alias), StringComparison.OrdinalIgnoreCase));

            if (model == null)
            {
                _logger?.LogWarning("XmlaEndpoint", $"Rejected {verb} for '{requested}': not served.");
                return CreateSoapFault("Server",
                    $"No served model named '{requested}'. Serve that model in PBI Port Wrapper first. " +
                    AvailableModels(served));
            }

            WarnOnCatalogMismatch(doc, model);

            if (verb == XmlaRequestType.Execute && model.ReadOnly &&
                XmlaCommandClassifier.Mutates(doc, out string mutation))
            {
                // Refused here, so nothing reaches msmdsrv. The fault names both the
                // command and the way to allow it, because "read-only" on its own gives
                // the user at the client end nothing to act on.
                _logger?.LogWarning("XmlaEndpoint",
                    $"Refused {mutation} on '{model.Alias}': the model is served read-only.");
                return CreateSoapFault("Client",
                    $"'{model.Alias}' is served read-only, so it refused {mutation}. " +
                    "Queries are unaffected. Clear Read-only for this model in PBI Port Wrapper to allow changes.");
            }

            try
            {
                // Debug, not Info: a single Excel session is ~50 requests, and the
                // dashboard mirrors this log. Successful routing is only interesting
                // while diagnosing, which LogPayloads turns on. Everything that went
                // wrong below stays at Warning.
                _logger?.LogDebug("XmlaEndpoint",
                    $"{verb} {ExtractRequestType(doc) ?? "command"} on '{model.Alias}' -> localhost:{model.Port}");

                string response = SendToEngine(model.Port, verb, soapEnvelope);
                ReportIfFault(response, model.Alias, verb.ToString());
                return response;
            }
            catch (Exception ex)
            {
                _logger?.LogError("XmlaEndpoint", $"Relay of {verb} to '{model.Alias}' failed: {ex.Message}", ex);
                return CreateSoapFault("Server", ex.Message);
            }
        }

        /// <summary>
        /// The model a request is addressed to: the first segment of the URL path.
        /// Percent-encoding is decoded here, so a model whose alias contains a space is
        /// reachable as <c>/My%20Model</c>. Empty when the request went to the bare
        /// endpoint.
        /// </summary>
        public static string ModelFromPath(string requestPath)
        {
            if (string.IsNullOrWhiteSpace(requestPath)) return string.Empty;

            string path = requestPath.Trim().Trim('/');
            if (path.Length == 0) return string.Empty;

            int slash = path.IndexOf('/');
            if (slash >= 0) path = path.Substring(0, slash);

            // Decoded after the split, so an encoded slash inside a name is a character
            // rather than a separator.
            try { return Uri.UnescapeDataString(path).Trim(); }
            catch (UriFormatException) { return path.Trim(); }
        }

        /// <summary>The paths that do resolve, as a client should write them.</summary>
        private static string AvailableModels(IReadOnlyList<ServedCatalog> served) =>
            "Served models: " + string.Join(", ", served.Select(c => "/" + Uri.EscapeDataString(c.Alias ?? string.Empty)));

        /// <summary>
        /// Notes when a request names a different catalog than the path it arrived on.
        /// The engine stays the authority and the request is forwarded either way — but
        /// a mismatch is the one thing that makes a correctly routed request fault, and
        /// a failure path that logs nothing is a defect in its own right.
        /// </summary>
        private void WarnOnCatalogMismatch(XDocument doc, ServedCatalog model)
        {
            if (_logger == null) return;

            string catalog = ExtractCatalog(doc);
            if (string.IsNullOrEmpty(catalog)) return;

            if (NormalizeCatalogName(catalog).Equals(
                    NormalizeCatalogName(model.Alias), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _logger.LogWarning("XmlaEndpoint",
                $"Request on '{model.Alias}' names catalog '{catalog}'; forwarding it as sent. " +
                $"That model answers on its own path.");
        }

        /// <summary>
        /// A catalog name as written by a client, reduced to a comparable form. Clients
        /// quote names in MDX bracket notation — Excel asks for `[Sample02]` — and that
        /// names the same database as `Sample02`. Only comparisons use this; the request
        /// itself keeps whatever the client wrote.
        /// </summary>
        public static string NormalizeCatalogName(string catalog)
        {
            if (string.IsNullOrWhiteSpace(catalog)) return string.Empty;

            string trimmed = catalog.Trim();
            if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[trimmed.Length - 1] == ']')
            {
                // ]] is how MDX escapes a literal ] inside a quoted name.
                trimmed = trimmed.Substring(1, trimmed.Length - 2).Replace("]]", "]");
            }
            return trimmed;
        }

        private static XDocument ParseOrNull(string xml)
        {
            try { return XDocument.Parse(xml); }
            catch (XmlException) { return null; }
        }

        private static string FaultText(XDocument doc)
        {
            XElement fault = doc?.Descendants()
                .FirstOrDefault(e => e.Name.LocalName.Equals("Fault", StringComparison.OrdinalIgnoreCase));
            if (fault == null) return null;

            string text = string.Join(" ", fault.Value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
            return text.Length > 300 ? text.Substring(0, 300) : text;
        }

        /// <summary>
        /// Surfaces an engine's fault in the log. The client sees it either way, but a
        /// fault that only ever reaches the client leaves nothing here to diagnose from —
        /// which has now cost two rounds of guessing.
        /// </summary>
        private void ReportIfFault(string response, string alias, string what)
        {
            if (_logger == null || response == null) return;
            if (!XmlaRequestSummary.IsFault(response)) return;

            string text = FaultText(ParseOrNull(response));
            if (text != null) _logger.LogWarning("XmlaEndpoint", $"{what} on '{alias}' returned a fault: {text}");
        }

        /// <summary>
        /// Sends the envelope to msmdsrv as the logged-in owner and returns the raw
        /// response. Virtual so tests can assert pass-through without a live engine.
        /// </summary>
        protected virtual string SendToEngine(int port, XmlaRequestType verb, string soapEnvelope)
        {
            var server = new Server();
            try
            {
                server.Connect($"Data Source=localhost:{port}");

                // The returned reader holds the connection open until it is closed,
                // so a Server instance carries exactly one request at a time.
                using (XmlReader reader = server.SendXmlaRequest(verb, new StringReader(soapEnvelope)))
                {
                    var sb = new StringBuilder();
                    using (XmlWriter writer = XmlWriter.Create(sb, new XmlWriterSettings { OmitXmlDeclaration = true }))
                    {
                        writer.WriteNode(reader, defattr: true);
                    }
                    return sb.ToString();
                }
            }
            finally
            {
                try { server.Disconnect(); } catch { /* teardown must not mask the result */ }
                try { server.Dispose(); } catch { }
            }
        }

        /// <summary>
        /// Reads the catalog named in the envelope's PropertyList. Read-only, and used
        /// only to notice a mismatch worth logging: routing is the path's job.
        /// </summary>
        public static string ExtractCatalog(XDocument doc)
        {
            XElement catalogElem = doc?.Descendants()
                .FirstOrDefault(e => !e.HasElements &&
                                     (e.Name.LocalName.Equals("Catalog", StringComparison.OrdinalIgnoreCase) ||
                                      e.Name.LocalName.Equals("CatalogName", StringComparison.OrdinalIgnoreCase)) &&
                                     !string.IsNullOrWhiteSpace(e.Value));
            return catalogElem?.Value?.Trim();
        }

        /// <summary>The Discover RequestType, e.g. DBSCHEMA_CATALOGS. Null for Execute.</summary>
        public static string ExtractRequestType(XDocument doc)
        {
            XElement requestType = doc?.Descendants()
                .FirstOrDefault(e => e.Name.LocalName.Equals("RequestType", StringComparison.OrdinalIgnoreCase));
            return string.IsNullOrWhiteSpace(requestType?.Value) ? null : requestType.Value.Trim();
        }

        /// <summary>
        /// Picks the XMLA verb. The body is authoritative; the SOAPAction header is
        /// only consulted when the body is unrecognised, since headers can lie.
        /// </summary>
        public static XmlaRequestType ResolveVerb(XDocument doc, string soapAction)
        {
            XElement body = doc?.Root?.Elements()
                .FirstOrDefault(e => e.Name.LocalName.Equals("Body", StringComparison.OrdinalIgnoreCase));
            string command = body?.Elements().FirstOrDefault()?.Name.LocalName;

            if (string.Equals(command, "Execute", StringComparison.OrdinalIgnoreCase)) return XmlaRequestType.Execute;
            if (string.Equals(command, "Discover", StringComparison.OrdinalIgnoreCase)) return XmlaRequestType.Discover;

            if (!string.IsNullOrEmpty(soapAction) &&
                soapAction.IndexOf("Execute", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return XmlaRequestType.Execute;
            }

            return XmlaRequestType.Discover;
        }

        public static string CreateSoapFault(string faultCode, string faultString)
        {
            return "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                   "<soap:Body><soap:Fault>" +
                   $"<faultcode>soap:{faultCode}</faultcode>" +
                   $"<faultstring>{XmlEscape(faultString)}</faultstring>" +
                   "</soap:Fault></soap:Body></soap:Envelope>";
        }

        private static string XmlEscape(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                       .Replace("\"", "&quot;").Replace("'", "&apos;");
        }
    }
}
