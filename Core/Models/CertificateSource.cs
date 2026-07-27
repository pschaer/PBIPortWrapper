using System;

namespace PBIPortWrapper.Models
{
    /// <summary>
    /// Where the endpoint's certificate comes from (#132).
    ///
    /// Deliberately NOT persisted: configuration stores the paths and the thumbprint,
    /// and which of them is filled in IS the choice. A stored enum could disagree with
    /// the fields it describes, and then two things would claim to be the answer.
    /// </summary>
    public enum CertificateSource
    {
        /// <summary>`fullchain.pem` + `privkey.pem`, what Let's Encrypt clients emit.</summary>
        PemPair = 0,

        /// <summary>A certificate in the Windows certificate store, by thumbprint.</summary>
        WindowsStore = 1,

        /// <summary>A PFX file carrying its private key, with no password.</summary>
        PfxFile = 2
    }

    /// <summary>
    /// Labels and descriptions for <see cref="CertificateSource"/>, shared by every
    /// surface so two of them cannot drift apart - the same reason
    /// <see cref="BridgeAuthModeLabel"/> and OnDetectionPolicyLabel exist.
    /// </summary>
    public static class CertificateSourceLabel
    {
        /// <summary>
        /// Presentation order. The PEM pair leads because it is the one where renewal
        /// needs no further action: a renewed certificate is a DIFFERENT certificate, so
        /// the store route needs a re-import and a new thumbprint every sixty days and
        /// the PFX route needs its conversion re-run. Files rewritten in place need
        /// neither.
        /// </summary>
        public static readonly CertificateSource[] Order =
        {
            CertificateSource.PemPair,
            CertificateSource.WindowsStore,
            CertificateSource.PfxFile
        };

        public static string For(CertificateSource source)
        {
            switch (source)
            {
                case CertificateSource.WindowsStore: return "Windows certificate store";
                case CertificateSource.PfxFile: return "PFX file";
                default: return "Certificate and key files (PEM)";
            }
        }

        public static string Describe(CertificateSource source)
        {
            switch (source)
            {
                case CertificateSource.WindowsStore:
                    return "A certificate already installed on this machine, chosen by thumbprint. " +
                           "Windows guards the private key. Note that a renewal is a different " +
                           "certificate: its thumbprint changes, so this needs updating each time.";
                case CertificateSource.PfxFile:
                    return "A .pfx file carrying its private key. It must not be password-protected - " +
                           "a password here would be a stored credential in clear text. A protected " +
                           "PFX belongs in the certificate store.";
                default:
                    return "The fullchain.pem and privkey.pem pair from Let's Encrypt, a reverse " +
                           "proxy, or any ACME client. Recommended: when they are replaced in place " +
                           "the renewal is picked up within minutes, with nothing to update here.";
            }
        }

        /// <summary>
        /// Which source the configuration currently describes, decided the same way
        /// <see cref="PBIPortWrapper.Services.CertificateResolver"/> decides - so what
        /// the dialog shows is what would actually be served.
        /// </summary>
        public static CertificateSource SourceOf(HttpBridgeConfig config)
        {
            if (config == null) return CertificateSource.PemPair;

            if (!string.IsNullOrWhiteSpace(config.CertificateThumbprint))
                return CertificateSource.WindowsStore;

            if (!string.IsNullOrWhiteSpace(config.CertificateKeyPath))
                return CertificateSource.PemPair;

            if (!string.IsNullOrWhiteSpace(config.CertificatePath))
                return CertificateSource.PfxFile;

            // Nothing configured yet: offer the recommended one.
            return CertificateSource.PemPair;
        }
    }
}
