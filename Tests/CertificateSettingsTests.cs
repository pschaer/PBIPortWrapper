using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    /// <summary>
    /// Choosing where the certificate comes from (#132 step 3). The settings are three
    /// mutually exclusive sources sharing one config object, and the resolver prefers
    /// them in a fixed order - so which fields are filled in has to be exact.
    /// </summary>
    public sealed class CertificateSettingsTests : IDisposable
    {
        private readonly string _dir = Path.Combine(
            Path.GetTempPath(), "pbipw-certui-" + Guid.NewGuid().ToString("N"));
        private readonly ConfigService _config;

        public CertificateSettingsTests()
        {
            Directory.CreateDirectory(_dir);
            _config = new ConfigService(new ConfigurationManager(_dir));
            _config.Load();
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        private HttpBridgeConfig Bridge => _config.Current.HttpBridge;

        /// <summary>A real, resolvable PEM pair on disk.</summary>
        private (string cert, string key) WritePemPair()
        {
            using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=case.example.com", ecdsa, HashAlgorithmName.SHA256);
            using X509Certificate2 cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(90));

            string certPath = Path.Combine(_dir, "fullchain.pem");
            string keyPath = Path.Combine(_dir, "privkey.pem");
            File.WriteAllText(certPath, cert.ExportCertificatePem());
            File.WriteAllText(keyPath, ecdsa.ExportPkcs8PrivateKeyPem());
            return (certPath, keyPath);
        }

        // --- Which source the settings describe --------------------------------------

        [Fact]
        public void A_thumbprint_means_the_windows_store()
        {
            var config = new HttpBridgeConfig { CertificateThumbprint = "A1B2C3" };

            Assert.Equal(CertificateSource.WindowsStore, CertificateSourceLabel.SourceOf(config));
        }

        [Fact]
        public void A_certificate_with_a_key_means_a_pem_pair()
        {
            var config = new HttpBridgeConfig
            {
                CertificatePath = @"C:\certs\fullchain.pem",
                CertificateKeyPath = @"C:\certs\privkey.pem"
            };

            Assert.Equal(CertificateSource.PemPair, CertificateSourceLabel.SourceOf(config));
        }

        [Fact]
        public void A_certificate_without_a_key_means_a_pfx()
        {
            var config = new HttpBridgeConfig { CertificatePath = @"C:\certs\cert.pfx" };

            Assert.Equal(CertificateSource.PfxFile, CertificateSourceLabel.SourceOf(config));
        }

        [Fact]
        public void Nothing_configured_offers_the_recommended_source()
        {
            // The PEM pair: the only one where a renewal needs no further action.
            Assert.Equal(CertificateSource.PemPair,
                CertificateSourceLabel.SourceOf(new HttpBridgeConfig()));
        }

        // --- Choosing one source clears the others -----------------------------------

        [Fact]
        public void Choosing_a_pem_pair_clears_a_thumbprint_left_behind()
        {
            // THE bug this API exists to prevent. CertificateResolver checks the
            // thumbprint FIRST, so one left over from an earlier attempt would quietly
            // win over the pair just chosen - serving a certificate the settings appear
            // to have replaced.
            _config.SetCertificate(CertificateSource.WindowsStore, thumbprint: "A1B2C3");

            _config.SetCertificate(CertificateSource.PemPair,
                path: @"C:\certs\fullchain.pem", keyPath: @"C:\certs\privkey.pem");

            Assert.Empty(Bridge.CertificateThumbprint);
            Assert.Equal(@"C:\certs\fullchain.pem", Bridge.CertificatePath);
            Assert.Equal(@"C:\certs\privkey.pem", Bridge.CertificateKeyPath);
        }

        [Fact]
        public void Choosing_a_pfx_clears_the_key_path()
        {
            // Left behind, the key path would make the resolver read the PFX as PEM.
            _config.SetCertificate(CertificateSource.PemPair,
                path: @"C:\certs\fullchain.pem", keyPath: @"C:\certs\privkey.pem");

            _config.SetCertificate(CertificateSource.PfxFile, path: @"C:\certs\cert.pfx");

            Assert.Empty(Bridge.CertificateKeyPath);
            Assert.Empty(Bridge.CertificateThumbprint);
            Assert.Equal(@"C:\certs\cert.pfx", Bridge.CertificatePath);
        }

        [Fact]
        public void Choosing_the_store_clears_both_paths()
        {
            _config.SetCertificate(CertificateSource.PemPair,
                path: @"C:\certs\fullchain.pem", keyPath: @"C:\certs\privkey.pem");

            _config.SetCertificate(CertificateSource.WindowsStore, thumbprint: "A1B2C3");

            Assert.Empty(Bridge.CertificatePath);
            Assert.Empty(Bridge.CertificateKeyPath);
            Assert.Equal("A1B2C3", Bridge.CertificateThumbprint);
        }

        [Fact]
        public void A_path_pasted_with_quotes_is_cleaned()
        {
            // Explorer's "Copy as path" wraps in quotes, and the file would not be found.
            _config.SetCertificate(CertificateSource.PfxFile, path: "\"C:\\certs\\cert.pfx\"");

            Assert.Equal(@"C:\certs\cert.pfx", Bridge.CertificatePath);
        }

        [Fact]
        public void A_round_trip_through_the_source_survives()
        {
            _config.SetCertificate(CertificateSource.PemPair,
                path: @"C:\certs\fullchain.pem", keyPath: @"C:\certs\privkey.pem");

            Assert.Equal(CertificateSource.PemPair, CertificateSourceLabel.SourceOf(Bridge));
        }

        // --- Turning encryption on ---------------------------------------------------

        [Fact]
        public void Https_cannot_be_turned_on_without_a_usable_certificate()
        {
            // It would be accepted, saved, and then fail to start on the next apply -
            // a much worse way to learn the path was mistyped.
            var (ok, message) = _config.SetUseHttps(true);

            Assert.False(ok);
            Assert.False(Bridge.UseHttps);
            Assert.Contains("certificate", message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Https_cannot_be_turned_on_with_a_path_that_does_not_resolve()
        {
            _config.SetCertificate(CertificateSource.PfxFile, path: Path.Combine(_dir, "missing.pfx"));

            var (ok, message) = _config.SetUseHttps(true);

            Assert.False(ok);
            Assert.False(Bridge.UseHttps);
            Assert.Contains("missing.pfx", message);
        }

        [Fact]
        public void Https_turns_on_with_a_certificate_that_resolves()
        {
            var (cert, key) = WritePemPair();
            _config.SetCertificate(CertificateSource.PemPair, path: cert, keyPath: key);

            var (ok, _) = _config.SetUseHttps(true);

            Assert.True(ok);
            Assert.True(Bridge.UseHttps);
        }

        [Fact]
        public void Https_can_always_be_turned_off()
        {
            // Never blocked by a certificate check: switching encryption off is how
            // someone recovers from a certificate that stopped resolving.
            var (cert, key) = WritePemPair();
            _config.SetCertificate(CertificateSource.PemPair, path: cert, keyPath: key);
            _config.SetUseHttps(true);

            var (ok, _) = _config.SetUseHttps(false);

            Assert.True(ok);
            Assert.False(Bridge.UseHttps);
        }

        [Fact]
        public void The_sign_in_note_stops_claiming_the_password_is_exposed_once_https_is_on()
        {
            // It said "the password is not encrypted in transit" unconditionally, which
            // was true of every configuration when it was written and is now true of
            // only some. A warning that keeps firing after it has been addressed is one
            // people learn to skip - the same rot the docs had.
            string plain = BridgeAuthModeLabel.Describe(BridgeAuthMode.Basic, https: false);
            string secure = BridgeAuthModeLabel.Describe(BridgeAuthMode.Basic, https: true);

            Assert.Contains("not encrypted in transit", plain);
            Assert.DoesNotContain("not encrypted in transit", secure);
            Assert.Contains("protected in transit", secure);
        }

        [Fact]
        public void Every_source_has_a_label_and_a_description()
        {
            // The dialog builds its list from Order, so a missing entry would be a
            // blank line in a dropdown.
            foreach (CertificateSource source in CertificateSourceLabel.Order)
            {
                Assert.False(string.IsNullOrWhiteSpace(CertificateSourceLabel.For(source)));
                Assert.False(string.IsNullOrWhiteSpace(CertificateSourceLabel.Describe(source)));
            }

            Assert.Equal(
                Enum.GetValues(typeof(CertificateSource)).Length,
                CertificateSourceLabel.Order.Length);
        }
    }
}
