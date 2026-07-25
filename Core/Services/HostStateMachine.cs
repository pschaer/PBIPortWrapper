using System.Collections.Generic;
using PBIPortWrapper.Models;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Pure, UI-free logic for the tray-first host state model (v0.7, #84 — see
    /// docs/tray-workflow.md). Maps detection policies to target states, models the
    /// user actions available from each state, and derives a policy from the legacy
    /// AutoConnect/AutoServe booleans for config migration.
    ///
    /// This is the model/state layer the tray (#85) and the diagnostics grid (#88)
    /// project; it does not itself start proxies or renames.
    /// </summary>
    public static class HostStateMachine
    {
        /// <summary>The state a model should move toward when its instance is detected.</summary>
        public static HostState TargetOnDetection(OnDetectionPolicy policy)
        {
            switch (policy)
            {
                case OnDetectionPolicy.Forward: return HostState.Forward;
                case OnDetectionPolicy.ServeAfterGrace: return HostState.Serve;
                case OnDetectionPolicy.ServeImmediately: return HostState.Serve;
                case OnDetectionPolicy.DoNothing:
                default: return HostState.Off;
            }
        }

        /// <summary>
        /// Convenience overload: an unknown (unconfigured) model is a null rule and
        /// stays Off on detection — it is surfaced as a "new model" prompt instead.
        /// </summary>
        public static HostState TargetOnDetection(PortMappingRule rule) =>
            rule == null ? HostState.Off : TargetOnDetection(rule.OnDetection);

        /// <summary>
        /// True when reaching the target should wait out a grace period (with an
        /// "Edit instead" escape) rather than serving immediately. Only meaningful
        /// when <see cref="TargetOnDetection(OnDetectionPolicy)"/> is Serve.
        /// </summary>
        public static bool UsesGracePeriod(OnDetectionPolicy policy) =>
            policy == OnDetectionPolicy.ServeAfterGrace;

        /// <summary>
        /// The current host state observed from the running services: serving wins
        /// over forwarding (a serve session also forwards the port). Used to project
        /// live state into the tray/grid (#85).
        /// </summary>
        public static HostState CurrentState(bool serving, bool forwarding)
        {
            if (serving) return HostState.Serve;
            if (forwarding) return HostState.Forward;
            return HostState.Off;
        }

        /// <summary>The state that results from applying an action to the current state.</summary>
        public static HostState Apply(HostState current, HostAction action)
        {
            switch (action)
            {
                case HostAction.Forward: return HostState.Forward;
                case HostAction.Serve: return HostState.Serve;
                case HostAction.StopServing: return HostState.Forward;
                case HostAction.Stop: return HostState.Off;
                default: return current;
            }
        }

        /// <summary>
        /// The actions that make sense to offer from a given state (drives the tray
        /// menu / grid controls). Off can forward or serve; Forward can upgrade to
        /// serve or stop; Serve can drop to forward (to edit) or stop.
        /// </summary>
        public static IReadOnlyList<HostAction> AvailableActions(HostState state)
        {
            switch (state)
            {
                case HostState.Off:
                    return new[] { HostAction.Forward, HostAction.Serve };
                case HostState.Forward:
                    return new[] { HostAction.Serve, HostAction.Stop };
                case HostState.Serve:
                    // Ending a serve session already stops the proxy, so there is a
                    // single stop (surfaced as "Stop"), not a separate stop-forwarding.
                    return new[] { HostAction.StopServing };
                default:
                    return new HostAction[0];
            }
        }

        /// <summary>
        /// Derives the detection policy from the legacy booleans, used once by the
        /// config migration (#84). AutoServe wins (it meant "serve on detect"), then
        /// AutoConnect ("forward on detect"), else nothing.
        /// </summary>
        public static OnDetectionPolicy FromLegacy(bool autoConnect, bool autoServe)
        {
            if (autoServe) return OnDetectionPolicy.ServeImmediately;
            if (autoConnect) return OnDetectionPolicy.Forward;
            return OnDetectionPolicy.DoNothing;
        }
    }
}
