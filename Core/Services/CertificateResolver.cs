using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography.X509Certificates;

namespace PBIPortWrapper.Services
{
    /// <summary>The certificate the endpoint should serve, or why there isn't one.</summary>
    public sealed class CertificateResolution
    {
        public X509Certificate2 Certificate { get; }

        /// <summary>Null when a certificate was found; otherwise what to do about it.</summary>
        public string Problem { get; }

        /// <summary>Where it came from, for the status line.</summary>
        public string Source { get; }

        private CertificateResolution(X509Certificate2 certificate, string problem, string source)
        {
            Certificate = certificate;
            Problem = problem;
            Source = source;
        }

        public bool Ok => Certificate != null;

        public static CertificateResolution Found(X509Certificate2 certificate, string source) =>
            new CertificateResolution(certificate, null, source);

        public static CertificateResolution Missing(string problem) =>
            new CertificateResolution(null, problem, null);
    }

    /// <summary>
    /// Finds the certificate the XMLA endpoint serves HTTPS with (#132).
    ///
    /// The app CONSUMES a certificate; it never creates or obtains one. Being trusted is
    /// the hard half of HTTPS, and a certificate this app generated would be trusted by
    /// nobody — every client machine would need it installed by hand, which is exactly
    /// the cost that makes self-hosted TLS not worth doing. A certificate from a CA the
    /// clients already trust needs no client-side work at all, and anyone in a position
    /// to run this already has a way to get one.
    ///
    /// Three sources, because the ways people already have one differ:
    ///
    /// - a <b>PEM pair</b> - `fullchain.pem` and `privkey.pem` - which is what Let's
    ///   Encrypt clients emit and what a reverse proxy hands out;
    /// - a <b>PFX file</b>, which is what some ACME clients on another host produce;
    /// - a <b>thumbprint</b> in the Windows certificate store, which is where a Windows
    ///   ACME client puts it.
    ///
    /// The PEM pair is the one that makes renewal hands-off. A certificate renews every
    /// sixty days or so; a store thumbprint CHANGES on renewal, so that route quietly
    /// needs a re-import and a config edit each time, and converting to PFX needs the
    /// conversion re-run. Two files rewritten in place need neither.
    ///
    /// There is deliberately no place to configure a PFX password. A password in
    /// config.json is a stored credential in clear text, which this project does not do
    /// anywhere else and will not start doing for the feature whose whole point is
    /// confidentiality. A protected PFX belongs in the certificate store, where Windows
    /// guards the key; then configure the thumbprint.
    /// </summary>
    public static class CertificateResolver
    {
        public static CertificateResolution Resolve(string certificatePath, string thumbprint, string keyPath = null)
        {
            // The thumbprint first: a certificate in the store has its private key
            // protected by Windows, which is the strongest of the three.
            if (!string.IsNullOrWhiteSpace(thumbprint)) return FromStore(thumbprint);

            if (!string.IsNullOrWhiteSpace(certificatePath))
            {
                return string.IsNullOrWhiteSpace(keyPath)
                    ? FromFile(certificatePath)
                    : FromPem(certificatePath, keyPath);
            }

            return CertificateResolution.Missing(
                "No certificate is configured. Set either a thumbprint from the Windows " +
                "certificate store, a PEM certificate and its key, or the path to a PFX file.");
        }

        private static CertificateResolution FromFile(string path)
        {
            if (!File.Exists(path))
                return CertificateResolution.Missing($"No certificate file at '{path}'.");

            X509Certificate2 certificate;
            try
            {
                certificate = new X509Certificate2(path);
            }
            catch (Exception ex)
            {
                // Two very different mistakes reach here, and the same advice cannot serve
                // both: a protected PFX belongs in the certificate store, but a PEM file
                // that will not parse is usually the KEY handed over as the certificate,
                // where "import it into the store" is no help at all.
                string advice = LooksLikePem(path)
                    ? "If this is the private key, it belongs in CertificateKeyPath and the " +
                      "certificate - fullchain.pem - in CertificatePath."
                    : "If it is password-protected, import it into the Windows certificate " +
                      "store and configure its thumbprint instead.";

                return CertificateResolution.Missing(
                    $"'{path}' could not be read as a certificate ({ex.Message}). {advice}");
            }

            if (!certificate.HasPrivateKey)
            {
                certificate.Dispose();

                // A PEM certificate never carries its key - that is what the second file
                // is - so this is the expected way in for anyone who set only one path,
                // and it should name the setting rather than suggest a re-export.
                return CertificateResolution.Missing(LooksLikePem(path)
                    ? $"'{path}' is a certificate without its private key, which is normal for " +
                      "PEM: set CertificateKeyPath to the matching privkey.pem."
                    : $"'{path}' has no private key, so it cannot serve HTTPS. Export it again " +
                      "including the private key.");
            }

            return CertificateResolution.Found(certificate, path);
        }

        /// <summary>
        /// A PEM certificate and its separate key file, read as one certificate.
        /// </summary>
        private static CertificateResolution FromPem(string certificatePath, string keyPath)
        {
            if (!File.Exists(certificatePath))
                return CertificateResolution.Missing($"No certificate file at '{certificatePath}'.");

            if (!File.Exists(keyPath))
                return CertificateResolution.Missing($"No private key file at '{keyPath}'.");

            X509Certificate2 loaded;
            try
            {
                loaded = X509Certificate2.CreateFromPemFile(certificatePath, keyPath);
            }
            catch (Exception ex)
            {
                return CertificateResolution.Missing(
                    $"'{certificatePath}' and '{keyPath}' could not be read as a certificate and " +
                    $"its key ({ex.Message}). Both must be PEM text, the key must match the " +
                    "certificate, and the key must not be passphrase-protected.");
            }

            // THE TRAP, and the reason this is not simply the line above:
            //
            // A certificate built from PEM on Windows carries an EPHEMERAL private key,
            // and SChannel refuses to use one. Kestrel accepts the certificate, reports
            // HasPrivateKey as true, and then every TLS handshake dies with the client
            // seeing nothing but a closed connection - no error naming the cause.
            //
            // Exporting to PKCS#12 and reading it back gives the key a home Windows will
            // serve from. Verified against a real endpoint: direct use fails the
            // handshake, this round-trip succeeds.
            try
            {
                using (loaded)
                {
                    return CertificateResolution.Found(
                        new X509Certificate2(loaded.Export(X509ContentType.Pfx)), certificatePath);
                }
            }
            catch (Exception ex)
            {
                return CertificateResolution.Missing(
                    $"The certificate at '{certificatePath}' could not be prepared for use " +
                    $"({ex.Message}).");
            }
        }

        private static bool LooksLikePem(string path) =>
            path.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".key", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".crt", StringComparison.OrdinalIgnoreCase);

        private static CertificateResolution FromStore(string thumbprint)
        {
            string wanted = NormalizeThumbprint(thumbprint);
            if (wanted.Length == 0)
                return CertificateResolution.Missing($"'{thumbprint}' is not a certificate thumbprint.");

            // The machine store first, because that is where a service-installed
            // certificate lands, then the user's own.
            foreach (StoreLocation location in new[] { StoreLocation.LocalMachine, StoreLocation.CurrentUser })
            {
                X509Certificate2 match = FindIn(location, wanted);
                if (match == null) continue;

                if (!match.HasPrivateKey)
                {
                    match.Dispose();
                    return CertificateResolution.Missing(
                        $"The certificate {wanted} is in the {location} store but its private key " +
                        "is not available to this account, so it cannot serve HTTPS.");
                }

                return CertificateResolution.Found(match, $"{location} store");
            }

            return CertificateResolution.Missing(
                $"No certificate with thumbprint {wanted} in the LocalMachine or CurrentUser store.");
        }

        private static X509Certificate2 FindIn(StoreLocation location, string thumbprint)
        {
            try
            {
                using (var store = new X509Store(StoreName.My, location))
                {
                    store.Open(OpenFlags.ReadOnly);
                    return store.Certificates
                        .OfType<X509Certificate2>()
                        .FirstOrDefault(c => NormalizeThumbprint(c.Thumbprint) == thumbprint);
                }
            }
            catch
            {
                // A store this account cannot open is a store with no match in it.
                return null;
            }
        }

        /// <summary>
        /// A thumbprint reduced to comparable form.
        ///
        /// Copying one out of the Windows certificate dialog brings invisible
        /// left-to-right marks and spaces with it, and pasting that into a config file
        /// produces a value that looks identical to the correct one and matches nothing.
        /// Keeping only hex characters removes an afternoon of confusion.
        /// </summary>
        public static string NormalizeThumbprint(string thumbprint)
        {
            if (string.IsNullOrWhiteSpace(thumbprint)) return string.Empty;

            var kept = thumbprint
                .Where(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'))
                .ToArray();

            return new string(kept).ToUpperInvariant();
        }
    }
}
