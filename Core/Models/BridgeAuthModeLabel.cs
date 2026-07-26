using System.Collections.Generic;

namespace PBIPortWrapper.Models
{
    /// <summary>
    /// The user-facing wording for <see cref="BridgeAuthMode"/>, in one place so every
    /// surface says the same thing — the same reason <c>OnDetectionPolicyLabel</c>
    /// exists (#107).
    ///
    /// The enum names describe the HTTP scheme; these describe what the user gets.
    /// </summary>
    public static class BridgeAuthModeLabel
    {
        /// <summary>
        /// Presentation order: the mode that works on an ordinary home or office
        /// machine first, and the one that authenticates nobody last.
        /// </summary>
        public static readonly IReadOnlyList<BridgeAuthMode> Order = new[]
        {
            BridgeAuthMode.Basic,
            BridgeAuthMode.Windows,
            BridgeAuthMode.Anonymous
        };

        /// <summary>
        /// The one name for this mode, used everywhere it is shown — menu item and
        /// status line alike. Qualifiers like "domain only" belong in
        /// <see cref="Describe"/>: a name that varies by context reads as two
        /// different settings.
        /// </summary>
        public static string For(BridgeAuthMode mode)
        {
            switch (mode)
            {
                case BridgeAuthMode.Anonymous: return "No authentication";
                case BridgeAuthMode.Windows: return "Windows sign-in";
                default: return "Password sign-in";
            }
        }

        /// <summary>The consequence of choosing it, for a tooltip.</summary>
        public static string Describe(BridgeAuthMode mode)
        {
            switch (mode)
            {
                case BridgeAuthMode.Anonymous:
                    return "Anyone who can reach this port can read every served model, and change them. " +
                           "Only reasonable on a network you trust completely.";
                case BridgeAuthMode.Windows:
                    return "Requires a domain. On a workgroup machine the handshake fails before a reply " +
                           "is sent, so clients appear to hang rather than report an error.";
                default:
                    return "Callers sign in with a Windows account that exists on this machine. " +
                           "The password is not encrypted in transit, so use it on a trusted network.";
            }
        }
    }
}
