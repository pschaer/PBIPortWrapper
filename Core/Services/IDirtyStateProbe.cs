namespace PBIPortWrapper.Services
{
    /// <summary>
    /// What a probe can conclude about unsaved changes in a Desktop instance.
    /// Verified 2026-07-19 on Desktop 2.155: no title marker exists at the Win32 or
    /// UIA level (see #57), so a definite Dirty is currently unobtainable — the best
    /// available probe (UIA undo-button heuristic, #59) yields MaybeDirty at most.
    /// </summary>
    public enum DirtyState
    {
        Unknown,
        Clean,
        MaybeDirty,
        Dirty
    }

    /// <summary>
    /// Preflight seam for serve sessions: asked before any mutation whether the
    /// Desktop process might have unsaved changes. Implementations live in the app
    /// layer (UIA); Core ships only <see cref="NullDirtyStateProbe"/>.
    /// </summary>
    public interface IDirtyStateProbe
    {
        DirtyState Probe(int processId);
    }

    /// <summary>Always answers Unknown — the UI must ask the user to confirm.</summary>
    public sealed class NullDirtyStateProbe : IDirtyStateProbe
    {
        public DirtyState Probe(int processId) => DirtyState.Unknown;
    }
}
