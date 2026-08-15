namespace PBIRelay.Models
{
    /// <summary>
    /// The user-facing labels for <see cref="OnDetectionPolicy"/>, shared by the tray
    /// menu and the diagnostics grid so both surfaces read identically (#88). The
    /// order escalates: do nothing, then serve, with the grace variant last.
    /// </summary>
    public static class OnDetectionPolicyLabel
    {
        /// <summary>Display order for pickers (tray submenu, grid dropdown).</summary>
        public static readonly OnDetectionPolicy[] Order =
        {
            OnDetectionPolicy.DoNothing,
            OnDetectionPolicy.ServeImmediately,
            OnDetectionPolicy.ServeAfterGrace
        };

        public static string For(OnDetectionPolicy policy)
        {
            switch (policy)
            {
                case OnDetectionPolicy.ServeImmediately: return "Serve";
                case OnDetectionPolicy.ServeAfterGrace: return "Serve after grace period";
                default: return "Do nothing";
            }
        }

        /// <summary>Maps a label back to its policy; false if the label is unknown.</summary>
        public static bool TryParse(string label, out OnDetectionPolicy policy)
        {
            foreach (var candidate in Order)
            {
                if (For(candidate) == label)
                {
                    policy = candidate;
                    return true;
                }
            }
            policy = OnDetectionPolicy.DoNothing;
            return false;
        }
    }
}
