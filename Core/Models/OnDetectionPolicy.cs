namespace PBIRelay.Models
{
    /// <summary>
    /// What the app should do with a model when its Desktop instance is detected
    /// (#84 — see docs/tray-workflow.md).
    ///
    /// Integer values are stable — they are what persists in config.json — so members
    /// may be renamed but not renumbered. Value <c>1</c> is deliberately absent: it
    /// was <c>Forward</c>, retired with forwarding in v1.0 (#126). A config still
    /// carrying it is migrated to <see cref="DoNothing"/>; see
    /// <see cref="Services.ConfigMigrator"/>.
    /// </summary>
    public enum OnDetectionPolicy
    {
        /// <summary>Ignore the model until the user acts on it. Target state: Off.</summary>
        DoNothing = 0,

        /// <summary>
        /// Serve after a short grace period with an "Edit instead" escape hatch.
        /// Target state: Serve (the interim countdown is a UI concern).
        /// </summary>
        ServeAfterGrace = 2,

        /// <summary>Serve at once. Target state: Serve.</summary>
        ServeImmediately = 3
    }
}
