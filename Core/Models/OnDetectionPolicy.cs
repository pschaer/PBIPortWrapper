namespace PBIPortWrapper.Models
{
    /// <summary>
    /// What the app should do with a model when its Desktop instance is detected
    /// (v0.7 tray-first workflow, #84 — see docs/tray-workflow.md).
    ///
    /// Supersedes the legacy per-rule booleans: <c>AutoConnect</c> (forward on
    /// detect) maps to <see cref="Forward"/>, and the never-consumed
    /// <c>AutoServe</c> maps to <see cref="ServeImmediately"/>. The mapping is
    /// applied once by the config migration (#84). Integer values are stable —
    /// they are what persists in config.json — so members may be renamed but not
    /// renumbered.
    /// </summary>
    public enum OnDetectionPolicy
    {
        /// <summary>Ignore the model until the user acts on it. Target state: Off.</summary>
        DoNothing = 0,

        /// <summary>Forward the stable port only; Desktop stays editable. Target state: Forward.</summary>
        Forward = 1,

        /// <summary>
        /// Serve after a short grace period with an "Edit instead" escape hatch.
        /// Target state: Serve (the interim countdown is a UI concern).
        /// </summary>
        ServeAfterGrace = 2,

        /// <summary>Serve at once (rename + forward). Target state: Serve.</summary>
        ServeImmediately = 3
    }
}
