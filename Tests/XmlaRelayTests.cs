using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Xml.Linq;
using Microsoft.AnalysisServices;
using Newtonsoft.Json;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    public class XmlaRelayTests
    {
        // Real envelopes, captured from a live Power BI Desktop workspace while proving
        // the relay approach. Keeping the genuine article means these tests exercise the
        // shape MSOLAP actually sends, not an idealised one.
        private const string DiscoverEnvelope =
            "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body>" +
            "<Discover xmlns=\"urn:schemas-microsoft-com:xml-analysis\">" +
            "<RequestType>MDSCHEMA_CUBES</RequestType>" +
            "<Restrictions><RestrictionList /></Restrictions>" +
            "<Properties><PropertyList><Catalog>Sales</Catalog></PropertyList></Properties>" +
            "</Discover></soap:Body></soap:Envelope>";

        private const string ExecuteEnvelope =
            "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body>" +
            "<Execute xmlns=\"urn:schemas-microsoft-com:xml-analysis\">" +
            "<Command><Statement>EVALUATE ROW(\"x\",1)</Statement></Command>" +
            "<Properties><PropertyList><Catalog>Sales</Catalog></PropertyList></Properties>" +
            "</Execute></soap:Body></soap:Envelope>";

        // What a client sends before it knows what the endpoint offers.
        private const string CatalogListEnvelope =
            "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body>" +
            "<Discover xmlns=\"urn:schemas-microsoft-com:xml-analysis\">" +
            "<RequestType>DBSCHEMA_CATALOGS</RequestType>" +
            "<Restrictions><RestrictionList /></Restrictions>" +
            "<Properties><PropertyList /></Properties>" +
            "</Discover></soap:Body></soap:Envelope>";

        /// <summary>A catalog rowset shaped like the engine's, with one row.</summary>
        private static string CatalogRowset(string catalogName) =>
            "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body>" +
            "<DiscoverResponse xmlns=\"urn:schemas-microsoft-com:xml-analysis\"><return>" +
            "<root xmlns=\"urn:schemas-microsoft-com:xml-analysis:rowset\">" +
            "<row><CATALOG_NAME>" + catalogName + "</CATALOG_NAME></row>" +
            "</root></return></DiscoverResponse></soap:Body></soap:Envelope>";

        /// <summary>
        /// Stands in for msmdsrv: records what the relay would have sent and returns a
        /// canned response per port, so routing and pass-through are provable without a
        /// live engine.
        /// </summary>
        private sealed class RecordingRelay : XmlaRelay
        {
            public readonly List<(int Port, XmlaRequestType Verb, string Envelope)> Sends = new();
            public Func<int, string> ResponseForPort = _ =>
                "<soap:Envelope><soap:Body><DiscoverResponse /></soap:Body></soap:Envelope>";

            public readonly HashSet<int> FailingPorts = new();

            public RecordingRelay(params ServedCatalog[] served)
                : base(() => served) { }

            protected override string SendToEngine(int port, XmlaRequestType verb, string soapEnvelope)
            {
                Sends.Add((port, verb, soapEnvelope));
                if (FailingPorts.Contains(port)) throw new InvalidOperationException($"port {port} is down");
                return ResponseForPort(port);
            }
        }

        private static RecordingRelay SalesOn(int port) => new RecordingRelay(new ServedCatalog("Sales", port));

        // --- The property the whole design rests on -------------------------------

        [Fact]
        public void Relay_SendsClientEnvelopeToEngineByteForByte()
        {
            var relay = SalesOn(55001);

            relay.Relay(DiscoverEnvelope, "\"urn:schemas-microsoft-com:xml-analysis:Discover\"", "/Sales");

            // Not "equivalent XML" — identical. The moment the relay reformats or
            // rewrites anything, it has started re-implementing the protocol.
            Assert.Equal(DiscoverEnvelope, relay.Sends.Single().Envelope);
        }

        [Fact]
        public void Relay_ReturnsEngineResponseUnchanged()
        {
            var relay = SalesOn(55001);
            relay.ResponseForPort = _ => "<soap:Envelope><soap:Body><root><row><X>1</X></row></root></soap:Body></soap:Envelope>";

            string response = relay.Relay(DiscoverEnvelope, null, "/Sales");

            Assert.Equal(relay.ResponseForPort(0), response);
        }

        // --- Routing by path -------------------------------------------------------

        [Fact]
        public void Relay_RoutesToTheModelNamedInThePath()
        {
            var relay = new RecordingRelay(
                new ServedCatalog("Finance", 60001),
                new ServedCatalog("Sales", 60002),
                new ServedCatalog("Ops", 60003));

            relay.Relay(DiscoverEnvelope, null, "/Ops");

            // The envelope names Sales; the path names Ops. The path decides.
            Assert.Equal(60003, relay.Sends.Single().Port);
        }

        [Fact]
        public void Relay_EveryRequestOnAPathReachesExactlyOneEngine()
        {
            // The reason per-model paths exist: an XMLA session belongs to the server
            // that issued it. A request that fanned out to several engines would hand
            // all but one of them a session they never issued
            // (XMLAnalysisError.0xc10c000a), which is what broke the second model.
            var relay = new RecordingRelay(
                new ServedCatalog("Sales", 60001),
                new ServedCatalog("Finance", 60002));
            relay.ResponseForPort = _ => CatalogRowset("Sales");

            string response = relay.Relay(CatalogListEnvelope, null, "/Sales");

            Assert.Equal(60001, relay.Sends.Single().Port);
            Assert.Equal(CatalogRowset("Sales"), response);
        }

        [Fact]
        public void Relay_MatchesThePathCaseInsensitively()
        {
            var relay = new RecordingRelay(new ServedCatalog("SALES", 60002));

            relay.Relay(DiscoverEnvelope, null, "/sales");

            Assert.Equal(60002, relay.Sends.Single().Port);
        }

        [Fact]
        public void Relay_MatchesAnAliasContainingASpaceThroughItsEncodedPath()
        {
            var relay = new RecordingRelay(new ServedCatalog("My Model", 60005));

            relay.Relay(DiscoverEnvelope, null, "/My%20Model");

            Assert.Equal(60005, relay.Sends.Single().Port);
        }

        [Fact]
        public void Relay_UnknownModel_FaultsWithoutReachingAnyEngine()
        {
            var relay = new RecordingRelay(new ServedCatalog("Finance", 60001));

            string response = relay.Relay(DiscoverEnvelope, null, "/Sales");

            Assert.Contains("<faultcode>soap:Server</faultcode>", response);
            Assert.Contains("Sales", response);
            Assert.Contains("/Finance", response);   // names what does work
            Assert.Empty(relay.Sends);
        }

        [Fact]
        public void Relay_BareEndpoint_FaultsNamingTheModelPaths()
        {
            // Connecting to the endpoint root names no model, so there is nothing to
            // route to. The fault is the only place the user is looking, so it carries
            // the addresses that do resolve.
            var relay = new RecordingRelay(
                new ServedCatalog("Sales", 60001),
                new ServedCatalog("My Model", 60002));

            string response = relay.Relay(DiscoverEnvelope, null, "/");

            Assert.Contains("<faultcode>soap:Client</faultcode>", response);
            Assert.Contains("/Sales", response);
            Assert.Contains("/My%20Model", response);   // written as a client must send it
            Assert.Empty(relay.Sends);
        }

        [Fact]
        public void Relay_NothingServed_SaysSo()
        {
            var relay = new RecordingRelay(Array.Empty<ServedCatalog>());

            string response = relay.Relay(DiscoverEnvelope, null, "/Sales");

            Assert.Contains("No models are currently served", response);
            Assert.Empty(relay.Sends);
        }

        [Fact]
        public void Relay_CatalogNamingADifferentModel_IsStillForwardedUntouched()
        {
            // A mismatch is logged, never corrected: rewriting a request to agree with
            // its path is the translator growing back. The engine is the authority on
            // what it will accept.
            var relay = new RecordingRelay(
                new ServedCatalog("Sales", 60001),
                new ServedCatalog("Finance", 60002));

            relay.Relay(DiscoverEnvelope, null, "/Finance");

            Assert.Equal(60002, relay.Sends.Single().Port);
            Assert.Equal(DiscoverEnvelope, relay.Sends.Single().Envelope);
        }

        private const string SessionHeader =
            "<soap:Header><Session SessionId=\"2BDA015E\" xmlns=\"urn:schemas-microsoft-com:xml-analysis\" /></soap:Header>";

        [Fact]
        public void Relay_KeepsTheClientsSession()
        {
            // Excel's BeginSession must return a real session or its client errors out
            // ("PCXMLAClient::BeginSession"). With one engine per path there is no
            // longer any case in which a session must be removed.
            string envelope = DiscoverEnvelope.Replace("<soap:Body>", SessionHeader + "<soap:Body>");

            var relay = SalesOn(60001);
            relay.Relay(envelope, null, "/Sales");

            Assert.Equal(envelope, relay.Sends.Single().Envelope);
        }

        // --- Path parsing ----------------------------------------------------------

        [Theory]
        [InlineData("/Sales", "Sales")]
        [InlineData("Sales", "Sales")]
        [InlineData("/Sales/", "Sales")]
        [InlineData("/Sales/anything", "Sales")]
        [InlineData("/My%20Model", "My Model")]
        [InlineData("/", "")]
        [InlineData("", "")]
        [InlineData(null, "")]
        public void ModelFromPath_TakesTheFirstSegmentDecoded(string path, string expected)
        {
            Assert.Equal(expected, XmlaRelay.ModelFromPath(path));
        }

        [Theory]
        [InlineData("[Sales]", "Sales")]
        [InlineData("Sales", "Sales")]
        [InlineData("  [Sales]  ", "Sales")]
        [InlineData("[Odd]]Name]", "Odd]Name")]   // ]] escapes a literal ] in MDX
        [InlineData("", "")]
        public void NormalizeCatalogName_UnquotesMdxNames(string written, string expected)
        {
            Assert.Equal(expected, XmlaRelay.NormalizeCatalogName(written));
        }

        // --- Verb resolution -------------------------------------------------------

        [Fact]
        public void ResolveVerb_PrefersBodyOverSoapActionHeader()
        {
            XDocument doc = XDocument.Parse(ExecuteEnvelope);
            Assert.Equal(XmlaRequestType.Execute,
                XmlaRelay.ResolveVerb(doc, "\"urn:schemas-microsoft-com:xml-analysis:Discover\""));
        }

        [Fact]
        public void ResolveVerb_FallsBackToSoapActionWhenBodyIsUnrecognised()
        {
            XDocument doc = XDocument.Parse(
                "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body><Unknown /></soap:Body></soap:Envelope>");
            Assert.Equal(XmlaRequestType.Execute,
                XmlaRelay.ResolveVerb(doc, "\"urn:schemas-microsoft-com:xml-analysis:Execute\""));
        }

        [Fact]
        public void Relay_PassesExecuteVerbForExecuteBody()
        {
            var relay = SalesOn(55001);
            relay.Relay(ExecuteEnvelope, null, "/Sales");
            Assert.Equal(XmlaRequestType.Execute, relay.Sends.Single().Verb);
        }

        // --- Request parsing -------------------------------------------------------

        [Fact]
        public void ExtractCatalog_ReadsCatalogFromPropertyList()
        {
            Assert.Equal("Sales", XmlaRelay.ExtractCatalog(XDocument.Parse(DiscoverEnvelope)));
        }

        [Fact]
        public void ExtractCatalog_IgnoresEmptyCatalogElement()
        {
            XDocument doc = XDocument.Parse(
                "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\"><soap:Body>" +
                "<Discover xmlns=\"urn:schemas-microsoft-com:xml-analysis\">" +
                "<Properties><PropertyList><Catalog></Catalog></PropertyList></Properties>" +
                "</Discover></soap:Body></soap:Envelope>");
            Assert.Null(XmlaRelay.ExtractCatalog(doc));
        }

        [Fact]
        public void ExtractRequestType_ReadsTheRowsetName()
        {
            Assert.Equal("DBSCHEMA_CATALOGS", XmlaRelay.ExtractRequestType(XDocument.Parse(CatalogListEnvelope)));
            Assert.Null(XmlaRelay.ExtractRequestType(XDocument.Parse(ExecuteEnvelope)));
        }

        // --- Faults ----------------------------------------------------------------

        [Fact]
        public void Relay_EmptyRequest_ReturnsClientFault()
        {
            string response = SalesOn(55001).Relay("", null, "/Sales");
            Assert.Contains("<faultcode>soap:Client</faultcode>", response);
        }

        [Fact]
        public void Relay_MalformedXml_ReturnsClientFaultNotAnException()
        {
            string response = SalesOn(55001).Relay("<soap:Envelope><unclosed>", null, "/Sales");
            Assert.Contains("<faultcode>soap:Client</faultcode>", response);
            Assert.Contains("Malformed XMLA request", response);
        }

        [Fact]
        public void Relay_EngineFailure_ComesBackAsSoapFault()
        {
            var relay = SalesOn(55001);
            relay.FailingPorts.Add(55001);

            string response = relay.Relay(DiscoverEnvelope, null, "/Sales");

            Assert.Contains("<faultcode>soap:Server</faultcode>", response);
            Assert.Contains("is down", response);
        }

        [Fact]
        public void CreateSoapFault_EscapesMarkupInTheMessage()
        {
            string fault = XmlaRelay.CreateSoapFault("Server", "bad <tag> & 'quote'");
            Assert.Contains("bad &lt;tag&gt; &amp; &apos;quote&apos;", fault);
        }

        // --- Configuration ---------------------------------------------------------

        [Fact]
        public void HttpBridgeConfig_DefaultsAreSafe()
        {
            var config = new HttpBridgeConfig();

            Assert.False(config.Enabled);                            // never on implicitly
            Assert.Equal(BridgeAuthMode.Basic, config.AuthMode);     // works on a workgroup
            Assert.False(config.LogPayloads);                        // no query results in log.txt
            Assert.Equal(55555, config.Port);
        }

        [Theory]
        [InlineData(BridgeAuthMode.Basic, AuthenticationSchemes.Basic)]
        [InlineData(BridgeAuthMode.Anonymous, AuthenticationSchemes.Anonymous)]
        [InlineData(BridgeAuthMode.Windows, AuthenticationSchemes.IntegratedWindowsAuthentication)]
        public void AuthMode_MapsToBuiltInListenerScheme(BridgeAuthMode mode, AuthenticationSchemes expected)
        {
            // Auth stays the listener's job: every mode is a built-in scheme, so no
            // credential handling of our own can creep back in.
            Assert.Equal(expected, HttpBridgeService.ToSchemes(mode));
        }

        [Fact]
        public void ProxyConfiguration_WithoutBridgeSection_LoadsWithBridgeDisabled()
        {
            var config = JsonConvert.DeserializeObject<ProxyConfiguration>("{\"ConfigVersion\":1}");

            Assert.NotNull(config.HttpBridge);
            Assert.False(config.HttpBridge.Enabled);
        }

        [Fact]
        public void ProxyConfiguration_BridgeSettingsRoundTrip()
        {
            var original = new ProxyConfiguration();
            original.HttpBridge.Enabled = true;
            original.HttpBridge.Port = 60000;
            original.HttpBridge.AuthMode = BridgeAuthMode.Anonymous;

            var restored = JsonConvert.DeserializeObject<ProxyConfiguration>(JsonConvert.SerializeObject(original));

            Assert.True(restored.HttpBridge.Enabled);
            Assert.Equal(60000, restored.HttpBridge.Port);
            Assert.Equal(BridgeAuthMode.Anonymous, restored.HttpBridge.AuthMode);
        }
    }
}
