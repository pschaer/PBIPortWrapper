namespace PBIPortWrapper.Models
{
    /// <summary>
    /// The serve-lifecycle state of a single model, owned by
    /// <see cref="Services.ServeLifecycleMachine"/> (the v0.7 consolidation of the
    /// auto-serve × serve-session seam — see
    /// docs/HANDOVER-2026-07-24-serve-lifecycle.md).
    ///
    /// This is the *serve* axis only. Plain port forwarding (Off ↔ Forward, driven
    /// by AutoConnectService) stays a separate concern for now; unifying the two
    /// owners is #88. So the tray-facing <see cref="HostState"/> (Off/Forward/Serve)
    /// is a projection, not this type: <see cref="Grace"/> and <see cref="Recovering"/>
    /// are interim serve states that project down to Off until the serve lands.
    /// </summary>
    public enum ServeLifecycleState
    {
        /// <summary>Not serving. (May still be forwarding — that is AutoConnect's state.)</summary>
        Off = 0,

        /// <summary>Counting down to an automatic serve (ServeAfterGrace), with an "edit instead" escape.</summary>
        Grace = 1,

        /// <summary>Serving: database renamed to the alias and the fixed port forwarded.</summary>
        Serving = 2,

        /// <summary>A crash-recovery record matched a live instance; awaiting the user's resume/restore choice (#58).</summary>
        Recovering = 3
    }

    /// <summary>
    /// An event that can change a model's <see cref="ServeLifecycleState"/>. The
    /// (state, trigger) → (state, commands) table lives in
    /// <see cref="Services.ServeLifecycleMachine"/>; every cell is defined so the
    /// interactions that used to be implicit gaps (exit, stop-then-reserve, the
    /// recovery race) are now explicit.
    /// </summary>
    public enum ServeTrigger
    {
        /// <summary>The model's Desktop instance is present in the latest detection snapshot.</summary>
        Detected = 0,

        /// <summary>The model's instance left the snapshot — Desktop closed (E5).</summary>
        InstanceGone = 1,

        /// <summary>User asked to serve this model now.</summary>
        UserServe = 2,

        /// <summary>User asked to stop serving (or, during Grace, chose "edit instead").</summary>
        UserStop = 3,

        /// <summary>The grace countdown elapsed without a cancel.</summary>
        GraceElapsed = 4,

        /// <summary>The application is exiting (graceful shutdown).</summary>
        AppExit = 5,

        /// <summary>Startup recovery matched a persisted record to this live instance (#58).</summary>
        RecoveryMatched = 6,

        /// <summary>User chose "resume serving" on the recovery prompt.</summary>
        RecoveryResume = 7,

        /// <summary>User chose "restore original name" on the recovery prompt.</summary>
        RecoveryRestore = 8
    }

    /// <summary>
    /// A side effect the executor (the app-layer coordinator) must perform for a
    /// transition. The machine is pure and only *names* the effects; the coordinator
    /// carries them out against ServeSessionService / ProxyManager / timers / toasts.
    /// </summary>
    public enum ServeCommand
    {
        /// <summary>Do nothing.</summary>
        None = 0,

        /// <summary>Surface the one-time "new model detected — host it?" prompt.</summary>
        NotifyNewModel = 1,

        /// <summary>Start serving now: persist recovery record, rename DB → alias, start proxy, register session.</summary>
        StartServe = 2,

        /// <summary>Start the grace countdown toward an automatic serve.</summary>
        StartGrace = 3,

        /// <summary>Cancel a running grace countdown.</summary>
        CancelGrace = 4,

        /// <summary>Stop serving gracefully: rename the DB back to its original id, stop the proxy, clear the record.</summary>
        StopServe = 5,

        /// <summary>End the session without a rename-back — Desktop already closed with its database (E5): stop proxy, clear record.</summary>
        EndServeNoRestore = 6,

        /// <summary>Suppress auto-(re)serve for this model until its instance leaves the snapshot (#96).</summary>
        Suppress = 7,

        /// <summary>Show the crash-recovery prompt (resume / restore) for this model (#58).</summary>
        PromptRecovery = 8,

        /// <summary>Resume serving from a recovery record: re-apply alias if needed, start proxy, register session.</summary>
        ResumeServe = 9,

        /// <summary>Restore the original database name from a recovery record and clear it.</summary>
        RestoreName = 10
    }
}
