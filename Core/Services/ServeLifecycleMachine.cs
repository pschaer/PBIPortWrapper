using System.Collections.Generic;
using PBIPortWrapper.Models;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Context the <see cref="ServeLifecycleMachine"/> needs to decide a
    /// <see cref="ServeTrigger.Detected"/> transition. Everything here is a fact the
    /// coordinator already knows about the model at snapshot time; the machine stays
    /// a pure function of (state, trigger, context).
    /// </summary>
    public readonly struct LifecycleContext
    {
        /// <summary>A serve profile (PortMappingRule) exists for this model.</summary>
        public bool IsKnownModel { get; }

        /// <summary>The profile's OnDetection policy (only meaningful when known).</summary>
        public OnDetectionPolicy Policy { get; }

        /// <summary>The profile has a usable alias and a valid fixed port.</summary>
        public bool IsServable { get; }

        /// <summary>The user stopped (or "edit instead"-cancelled) this model and its instance has not left since (#96).</summary>
        public bool IsSuppressed { get; }

        /// <summary>An unresolved crash-recovery record exists for this model — recovery (#58/#102) owns it, not auto-serve.</summary>
        public bool HasRecoveryRecord { get; }

        public LifecycleContext(
            bool isKnownModel,
            OnDetectionPolicy policy,
            bool isServable,
            bool isSuppressed,
            bool hasRecoveryRecord)
        {
            IsKnownModel = isKnownModel;
            Policy = policy;
            IsServable = isServable;
            IsSuppressed = isSuppressed;
            HasRecoveryRecord = hasRecoveryRecord;
        }

        /// <summary>Context for triggers that do not read the detection policy (stop, exit, recovery, …).</summary>
        public static readonly LifecycleContext None =
            new LifecycleContext(false, OnDetectionPolicy.DoNothing, false, false, false);
    }

    /// <summary>The outcome of a transition: the next state and the side effects to run.</summary>
    public sealed class ServeTransition
    {
        public ServeLifecycleState Next { get; }
        public IReadOnlyList<ServeCommand> Commands { get; }

        public ServeTransition(ServeLifecycleState next, IReadOnlyList<ServeCommand> commands)
        {
            Next = next;
            Commands = commands;
        }

        private static readonly IReadOnlyList<ServeCommand> NoCommands = new ServeCommand[0];

        /// <summary>Stay in the current state with no side effect (an ignored/no-op trigger).</summary>
        public static ServeTransition Stay(ServeLifecycleState state) => new ServeTransition(state, NoCommands);

        /// <summary>Move to <paramref name="next"/> and run the given commands, in order.</summary>
        public static ServeTransition To(ServeLifecycleState next, params ServeCommand[] commands) =>
            new ServeTransition(next, commands);
    }

    /// <summary>
    /// The single, pure serve-lifecycle transition table (v0.7 consolidation — see
    /// docs/HANDOVER-2026-07-24-serve-lifecycle.md). Given a model's current
    /// <see cref="ServeLifecycleState"/>, a <see cref="ServeTrigger"/>, and the
    /// <see cref="LifecycleContext"/>, it returns the next state and the commands the
    /// coordinator must execute. No proxies, renames, timers, or UI here — only the
    /// decision. This replaces the implicit interactions that were spread across
    /// AutoServeController, ServeRecoveryCoordinator, ServeSessionService and MainForm
    /// (and manifested as #96, #100/exit-restore, #102, and the exit deadlock).
    ///
    /// Every (state, trigger) pair resolves to a defined cell; unlisted pairs are
    /// deliberate no-ops (<see cref="ServeTransition.Stay"/>).
    /// </summary>
    public static class ServeLifecycleMachine
    {
        /// <summary>A model can be served only with a usable alias and a valid fixed port.</summary>
        public static bool IsServable(PortMappingRule rule) =>
            rule != null
            && !string.IsNullOrWhiteSpace(rule.StableAlias)
            && rule.FixedPort >= 1024
            && rule.FixedPort <= 65535;

        public static ServeTransition Decide(ServeLifecycleState state, ServeTrigger trigger, LifecycleContext ctx)
        {
            switch (state)
            {
                case ServeLifecycleState.Off: return FromOff(trigger, ctx);
                case ServeLifecycleState.Grace: return FromGrace(trigger);
                case ServeLifecycleState.Serving: return FromServing(trigger);
                case ServeLifecycleState.Recovering: return FromRecovering(trigger);
                default: return ServeTransition.Stay(state);
            }
        }

        private static ServeTransition FromOff(ServeTrigger trigger, LifecycleContext ctx)
        {
            switch (trigger)
            {
                case ServeTrigger.Detected:
                    // Unknown model: never rename anything; just offer to host it once.
                    if (!ctx.IsKnownModel)
                        return ServeTransition.To(ServeLifecycleState.Off, ServeCommand.NotifyNewModel);
                    // Stand down if the user suppressed it (#96), recovery owns it (#102),
                    // or it simply can't be served (no alias / bad port).
                    if (ctx.IsSuppressed || ctx.HasRecoveryRecord || !ctx.IsServable)
                        return ServeTransition.Stay(ServeLifecycleState.Off);
                    switch (ctx.Policy)
                    {
                        case OnDetectionPolicy.ServeImmediately:
                            return ServeTransition.To(ServeLifecycleState.Serving, ServeCommand.StartServe);
                        case OnDetectionPolicy.ServeAfterGrace:
                            return ServeTransition.To(ServeLifecycleState.Grace, ServeCommand.StartGrace);
                        default:
                            // DoNothing / Forward: the serve machine leaves it Off
                            // (Forward is AutoConnect's concern until #88).
                            return ServeTransition.Stay(ServeLifecycleState.Off);
                    }

                // A deliberate user Serve overrides suppression/policy.
                case ServeTrigger.UserServe:
                    return ServeTransition.To(ServeLifecycleState.Serving, ServeCommand.StartServe);

                case ServeTrigger.RecoveryMatched:
                    return ServeTransition.To(ServeLifecycleState.Recovering, ServeCommand.PromptRecovery);

                default:
                    return ServeTransition.Stay(ServeLifecycleState.Off);
            }
        }

        private static ServeTransition FromGrace(ServeTrigger trigger)
        {
            switch (trigger)
            {
                case ServeTrigger.GraceElapsed:
                    return ServeTransition.To(ServeLifecycleState.Serving, ServeCommand.StartServe);

                // User serves now — skip the rest of the countdown.
                case ServeTrigger.UserServe:
                    return ServeTransition.To(ServeLifecycleState.Serving, ServeCommand.CancelGrace, ServeCommand.StartServe);

                // "Edit instead": cancel and suppress so it doesn't just re-arm next snapshot.
                case ServeTrigger.UserStop:
                    return ServeTransition.To(ServeLifecycleState.Off, ServeCommand.CancelGrace, ServeCommand.Suppress);

                case ServeTrigger.InstanceGone:
                    return ServeTransition.To(ServeLifecycleState.Off, ServeCommand.CancelGrace);

                case ServeTrigger.AppExit:
                    return ServeTransition.To(ServeLifecycleState.Off, ServeCommand.CancelGrace);

                // Detected (still counting down) and everything else: hold.
                default:
                    return ServeTransition.Stay(ServeLifecycleState.Grace);
            }
        }

        private static ServeTransition FromServing(ServeTrigger trigger)
        {
            switch (trigger)
            {
                // THE exit-restore fix (#100): exiting while serving renames back and
                // stops — an explicit, executed, logged transition, not a silent gap.
                case ServeTrigger.AppExit:
                    return ServeTransition.To(ServeLifecycleState.Off, ServeCommand.StopServe);

                // User Stop: rename back + stop, and suppress re-serve until reopen (#96).
                case ServeTrigger.UserStop:
                    return ServeTransition.To(ServeLifecycleState.Off, ServeCommand.StopServe, ServeCommand.Suppress);

                // Desktop closed: the instance died with its (renamed) database — nothing
                // to restore (E5); just tear down.
                case ServeTrigger.InstanceGone:
                    return ServeTransition.To(ServeLifecycleState.Off, ServeCommand.EndServeNoRestore);

                // Already serving this workspace, so recovery must not also act (#102);
                // a repeated Detected/UserServe is a no-op.
                default:
                    return ServeTransition.Stay(ServeLifecycleState.Serving);
            }
        }

        private static ServeTransition FromRecovering(ServeTrigger trigger)
        {
            switch (trigger)
            {
                case ServeTrigger.RecoveryResume:
                    return ServeTransition.To(ServeLifecycleState.Serving, ServeCommand.ResumeServe);

                case ServeTrigger.RecoveryRestore:
                    return ServeTransition.To(ServeLifecycleState.Off, ServeCommand.RestoreName);

                // Instance vanished before the user decided: abandon the prompt; the
                // now-stale record is cleared by CheckRecovery on a later launch.
                case ServeTrigger.InstanceGone:
                    return ServeTransition.To(ServeLifecycleState.Off);

                // Exit mid-prompt: leave the database as-is; the record persists and the
                // prompt returns next launch.
                case ServeTrigger.AppExit:
                    return ServeTransition.To(ServeLifecycleState.Off);

                default:
                    return ServeTransition.Stay(ServeLifecycleState.Recovering);
            }
        }
    }
}
