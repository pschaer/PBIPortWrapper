using System.Collections.Generic;
using PBIPortWrapper.Models;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Pure, UI-free logic for a model's presented state: what detection should aim
    /// for, and what the user can do from where it is (#84).
    ///
    /// This is the layer the tray and the diagnostics grid both project, so they offer
    /// the same actions with the same labels. It starts nothing itself.
    ///
    /// Since v1.0 the axis is just Off ↔ Serve (#126). The serve *lifecycle* — grace
    /// countdowns, recovery, exit — is a separate and richer machine,
    /// <see cref="ServeLifecycleMachine"/>; this one only answers "what does the user
    /// see, and what can they press".
    /// </summary>
    public static class HostStateMachine
    {
        /// <summary>The state a model should move toward when its instance is detected.</summary>
        public static HostState TargetOnDetection(OnDetectionPolicy policy)
        {
            switch (policy)
            {
                case OnDetectionPolicy.ServeAfterGrace:
                case OnDetectionPolicy.ServeImmediately:
                    return HostState.Serve;
                case OnDetectionPolicy.DoNothing:
                default:
                    return HostState.Off;
            }
        }

        /// <summary>
        /// Convenience overload: an unknown (unconfigured) model is a null rule and
        /// stays Off on detection — it is surfaced as a "new model" prompt instead.
        /// </summary>
        public static HostState TargetOnDetection(ModelRule rule) =>
            rule == null ? HostState.Off : TargetOnDetection(rule.OnDetection);

        /// <summary>
        /// True when reaching the target should wait out a grace period (with an
        /// "Edit instead" escape) rather than serving immediately.
        /// </summary>
        public static bool UsesGracePeriod(OnDetectionPolicy policy) =>
            policy == OnDetectionPolicy.ServeAfterGrace;

        /// <summary>The state observed from the running services.</summary>
        public static HostState CurrentState(bool serving) =>
            serving ? HostState.Serve : HostState.Off;

        /// <summary>The state that results from applying an action to the current state.</summary>
        public static HostState Apply(HostState current, HostAction action)
        {
            switch (action)
            {
                case HostAction.Serve: return HostState.Serve;
                case HostAction.Stop: return HostState.Off;
                default: return current;
            }
        }

        /// <summary>
        /// The actions worth offering from a given state, which drives the tray menu
        /// and the grid's Action menu.
        /// </summary>
        public static IReadOnlyList<HostAction> AvailableActions(HostState state)
        {
            switch (state)
            {
                case HostState.Off:
                    return new[] { HostAction.Serve };
                case HostState.Serve:
                    return new[] { HostAction.Stop };
                default:
                    return new HostAction[0];
            }
        }

        /// <summary>
        /// Derives the detection policy from the legacy booleans, used once by the
        /// config migration (#84).
        ///
        /// <c>AutoServe</c> meant "serve on detect" and still has a home.
        /// <c>AutoConnect</c> meant "forward on detect", and forwarding no longer
        /// exists — so it becomes "do nothing" rather than being silently promoted to
        /// serving, which would rename databases and block editing without anyone
        /// asking (#126).
        /// </summary>
        public static OnDetectionPolicy FromLegacy(bool autoConnect, bool autoServe) =>
            autoServe ? OnDetectionPolicy.ServeImmediately : OnDetectionPolicy.DoNothing;
    }
}
