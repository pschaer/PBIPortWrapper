using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using PBIRelay.Models;
using PBIRelay.Services;
using Xunit;

namespace PBIRelay.Core.Tests
{
    public class CertificateResolverTests : IDisposable
    {
        private readonly string _dir = Path.Combine(
            Path.GetTempPath(), "pbipw-cert-" + Guid.NewGuid().ToString("N"));

        public CertificateResolverTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        /// <summary>A real certificate on disk, so the resolver is exercised against files.</summary>
        private string WritePfx(string name, bool withPrivateKey = true, string password = null)
        {
            using RSA rsa = RSA.Create(2048);
            var request = new CertificateRequest(
                "CN=case.example.com", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

            using X509Certificate2 cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(90));

            string path = Path.Combine(_dir, name);
            byte[] bytes = withPrivateKey
                ? cert.Export(X509ContentType.Pfx, password)
                : cert.Export(X509ContentType.Cert);

            File.WriteAllBytes(path, bytes);
            return path;
        }

        [Fact]
        public void A_pfx_with_its_private_key_is_usable()
        {
            var resolved = CertificateResolver.Resolve(WritePfx("good.pfx"), thumbprint: null);

            Assert.True(resolved.Ok);
            Assert.True(resolved.Certificate.HasPrivateKey);
            Assert.Contains("case.example.com", resolved.Certificate.Subject);
            resolved.Certificate.Dispose();
        }

        /// <summary>
        /// The pair a Let's Encrypt client leaves on disk: the certificate in one file,
        /// the key in another.
        /// </summary>
        private (string cert, string key) WritePemPair(
            string certName = "fullchain.pem", string keyName = "privkey.pem")
        {
            using ECDsa ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP384);
            var request = new CertificateRequest("CN=case.example.com", ecdsa, HashAlgorithmName.SHA256);

            using X509Certificate2 cert = request.CreateSelfSigned(
                DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(90));

            string certPath = Path.Combine(_dir, certName);
            string keyPath = Path.Combine(_dir, keyName);

            File.WriteAllText(certPath, cert.ExportCertificatePem());
            File.WriteAllText(keyPath, ecdsa.ExportPkcs8PrivateKeyPem());

            return (certPath, keyPath);
        }

        [Fact]
        public void A_pem_certificate_and_its_key_are_usable()
        {
            // What Nginx Proxy Manager and every Let's Encrypt client hand out, read
            // without a conversion step - which is what keeps renewal hands-off.
            var (cert, key) = WritePemPair();

            var resolved = CertificateResolver.Resolve(cert, thumbprint: null, keyPath: key);

            Assert.True(resolved.Ok, resolved.Problem);
            Assert.True(resolved.Certificate.HasPrivateKey);
            Assert.Contains("case.example.com", resolved.Certificate.Subject);
            resolved.Certificate.Dispose();
        }

        [Fact]
        public void A_pem_private_key_survives_being_made_serveable()
        {
            // A certificate built straight from PEM on Windows has an EPHEMERAL key that
            // SChannel will not serve with: the handshake dies with the client seeing a
            // closed connection and no reason. The resolver round-trips through PKCS#12
            // to avoid it, so the key has to still be there and still be usable
            // afterwards. (That the round-trip is what fixes the handshake is verified
            // against a real endpoint, not here - SChannel is not in a unit test.)
            var (cert, key) = WritePemPair();

            var resolved = CertificateResolver.Resolve(cert, null, key);

            Assert.True(resolved.Ok, resolved.Problem);
            using ECDsa privateKey = resolved.Certificate.GetECDsaPrivateKey();
            Assert.NotNull(privateKey);
            Assert.NotEmpty(privateKey.SignData(new byte[] { 1, 2, 3 }, HashAlgorithmName.SHA256));
            resolved.Certificate.Dispose();
        }

        [Fact]
        public void A_pem_certificate_on_its_own_names_the_setting_that_is_missing()
        {
            // Setting only CertificatePath is the obvious first attempt, and a PEM
            // certificate never carries its key - so this is a signpost, not a failure.
            var (cert, _) = WritePemPair();

            var resolved = CertificateResolver.Resolve(cert, thumbprint: null);

            Assert.False(resolved.Ok);
            Assert.Contains("CertificateKeyPath", resolved.Problem);
        }

        [Fact]
        public void The_key_handed_over_as_the_certificate_is_told_which_way_round_they_go()
        {
            // The other obvious mistake, and the PFX advice - "import it into the
            // certificate store" - would be useless for it.
            var (_, key) = WritePemPair();

            var resolved = CertificateResolver.Resolve(key, thumbprint: null);

            Assert.False(resolved.Ok);
            Assert.Contains("CertificateKeyPath", resolved.Problem);
            Assert.DoesNotContain("password-protected", resolved.Problem);
        }

        [Fact]
        public void A_missing_key_file_is_named_rather_than_blamed_on_the_certificate()
        {
            var (cert, _) = WritePemPair();

            var resolved = CertificateResolver.Resolve(cert, null, Path.Combine(_dir, "gone.pem"));

            Assert.False(resolved.Ok);
            Assert.Contains("gone.pem", resolved.Problem);
            Assert.Contains("key", resolved.Problem);
        }

        [Fact]
        public void A_key_that_does_not_match_the_certificate_is_refused_with_a_reason()
        {
            // Two renewals mixed up in a folder is an easy way to get here.
            var (cert, _) = WritePemPair();
            var (_, otherKey) = WritePemPair("other-fullchain.pem", "other-privkey.pem");

            var resolved = CertificateResolver.Resolve(cert, null, otherKey);

            Assert.False(resolved.Ok);
            Assert.Contains("key", resolved.Problem);
        }

        [Fact]
        public void A_certificate_without_its_private_key_cannot_serve_https()
        {
            // A .cer exported without the key looks like a certificate and is useless
            // for this, so the reason has to say which half is missing.
            var resolved = CertificateResolver.Resolve(WritePfx("public.cer", withPrivateKey: false), null);

            Assert.False(resolved.Ok);
            Assert.Contains("private key", resolved.Problem);
        }

        [Fact]
        public void A_password_protected_pfx_says_where_it_belongs_instead()
        {
            // There is deliberately nowhere to configure a password - it would be a
            // stored credential in clear text - so the message has to point at the
            // alternative rather than just failing.
            var resolved = CertificateResolver.Resolve(WritePfx("locked.pfx", password: "secret"), null);

            Assert.False(resolved.Ok);
            Assert.Contains("certificate store", resolved.Problem);
        }

        [Fact]
        public void A_missing_file_is_named_in_the_reason()
        {
            var resolved = CertificateResolver.Resolve(Path.Combine(_dir, "nope.pfx"), null);

            Assert.False(resolved.Ok);
            Assert.Contains("nope.pfx", resolved.Problem);
        }

        [Fact]
        public void Configuring_nothing_says_what_to_configure()
        {
            var resolved = CertificateResolver.Resolve(null, null);

            Assert.False(resolved.Ok);
            Assert.Contains("thumbprint", resolved.Problem);
            Assert.Contains("PFX", resolved.Problem);
        }

        [Fact]
        public void An_unknown_thumbprint_is_reported_rather_than_falling_back_to_a_file()
        {
            // Silently serving a different certificate than the one named would be the
            // worst possible way to be helpful.
            var resolved = CertificateResolver.Resolve(WritePfx("good.pfx"), "ABCDEF0123456789");

            Assert.False(resolved.Ok);
            Assert.Contains("ABCDEF0123456789", resolved.Problem);
        }

        [Theory]
        [InlineData("ab cd ef", "ABCDEF")]
        [InlineData("AB:CD:EF", "ABCDEF")]
        [InlineData("‎ab‎cd", "ABCD")]     // the invisible marks Windows copies
        [InlineData("  abcdef  ", "ABCDEF")]
        public void A_thumbprint_copied_out_of_windows_still_matches(string pasted, string expected)
        {
            // Copying a thumbprint from the certificate dialog brings left-to-right
            // marks and spaces with it. Pasted into a config file it looks identical to
            // the correct value and matches nothing, which costs an afternoon.
            Assert.Equal(expected, CertificateResolver.NormalizeThumbprint(pasted));
        }

        [Fact]
        public void Https_is_off_by_default_and_carries_no_password_field()
        {
            // An endpoint that stopped answering after an upgrade would be worse than
            // one that is not yet encrypted.
            var config = new HttpBridgeConfig();

            Assert.False(config.UseHttps);
            Assert.Empty(config.CertificatePath);
            Assert.Empty(config.CertificateKeyPath);
            Assert.Empty(config.CertificateThumbprint);
            Assert.DoesNotContain(
                typeof(HttpBridgeConfig).GetProperties(),
                p => p.Name.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        [Fact]
        public void A_config_written_before_https_existed_loads_with_it_off()
        {
            var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<HttpBridgeConfig>(
                "{ \"Enabled\": true, \"Port\": 55555 }");

            Assert.False(restored.UseHttps);
        }
    }

    public class HttpsUrlTests
    {
        [Fact]
        public void The_scheme_follows_what_the_endpoint_serves()
        {
            // Every connection string, .odc file and copied URL comes from here, so a
            // hard-coded scheme would hand out addresses that cannot connect the moment
            // HTTPS is switched on - each one looking perfectly correct.
            Assert.Equal("http://host:55555/Sales", EndpointUrlBuilder.For("host", 55555, "Sales"));
            Assert.Equal("https://host:55555/Sales", EndpointUrlBuilder.For("host", 55555, "Sales", https: true));
        }

        [Fact]
        public void The_status_line_says_when_it_is_encrypted()
        {
            var plain = new EndpointStatus(true, true, 55555, BridgeAuthMode.Basic);
            var secure = new EndpointStatus(true, true, 55555, BridgeAuthMode.Basic, https: true);

            Assert.DoesNotContain("HTTPS", plain.Summary);
            Assert.Contains("HTTPS", secure.Summary);
        }
    }
}
