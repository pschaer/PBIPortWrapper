using System.Linq;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    /// <summary>
    /// Exhaustive coverage of the serve-lifecycle transition table
    /// (<see cref="ServeLifecycleMachine"/>) — the v0.7 consolidation of the
    /// auto-serve × serve-session seam. Each seam bug is pinned to the specific cell
    /// that now defines it: #100/exit-restore (Serving × AppExit), #96 (Serving ×
    /// UserStop suppression + Off × Detected suppression), #102 (Off × Detected
    /// recovery stand-down + Serving × RecoveryMatched no-op).
    /// </summary>
    public class ServeLifecycleMachineTests
    {
        private static LifecycleContext Known(
            OnDetectionPolicy policy,
            bool servable = true,
            bool suppressed = false,
            bool hasRecovery = false) =>
            new LifecycleContext(true, policy, servable, suppressed, hasRecovery);

        private static ServeTransition Decide(ServeLifecycleState s, ServeTrigger t, LifecycleContext ctx) =>
            ServeLifecycleMachine.Decide(s, t, ctx);

        // ---- Off × Detected: policy drives the target state ------------------------

        [Fact]
        public void Off_Detected_ServeImmediately_Serves()
        {
            var r = Decide(ServeLifecycleState.Off, ServeTrigger.Detected, Known(OnDetectionPolicy.ServeImmediately));
            Assert.Equal(ServeLifecycleState.Serving, r.Next);
            Assert.Equal(new[] { ServeCommand.StartServe }, r.Commands);
        }

        [Fact]
        public void Off_Detected_ServeAfterGrace_StartsGrace()
        {
            var r = Decide(ServeLifecycleState.Off, ServeTrigger.Detected, Known(OnDetectionPolicy.ServeAfterGrace));
            Assert.Equal(ServeLifecycleState.Grace, r.Next);
            Assert.Equal(new[] { ServeCommand.StartGrace }, r.Commands);
        }

        [Theory]
        [InlineData(OnDetectionPolicy.DoNothing)]
        [InlineData(OnDetectionPolicy.Forward)]
        public void Off_Detected_NonServePolicy_StaysOff(OnDetectionPolicy policy)
        {
            var r = Decide(ServeLifecycleState.Off, ServeTrigger.Detected, Known(policy));
            Assert.Equal(ServeLifecycleState.Off, r.Next);
            Assert.Empty(r.Commands);
        }

        [Fact]
        public void Off_Detected_UnknownModel_NotifiesOnce_StaysOff()
        {
            var r = Decide(ServeLifecycleState.Off, ServeTrigger.Detected, LifecycleContext.None);
            Assert.Equal(ServeLifecycleState.Off, r.Next);
            Assert.Equal(new[] { ServeCommand.NotifyNewModel }, r.Commands);
        }

        // ---- Off × Detected: guards that make auto-serve stand down ----------------

        [Fact] // #96
        public void Off_Detected_Suppressed_StaysOff()
        {
            var r = Decide(ServeLifecycleState.Off, ServeTrigger.Detected,
                Known(OnDetectionPolicy.ServeImmediately, suppressed: true));
            Assert.Equal(ServeLifecycleState.Off, r.Next);
            Assert.Empty(r.Commands);
        }

        [Fact] // #102 — recovery owns a model with a pending record; auto-serve stands down
        public void Off_Detected_PendingRecovery_StaysOff()
        {
            var r = Decide(ServeLifecycleState.Off, ServeTrigger.Detected,
                Known(OnDetectionPolicy.ServeImmediately, hasRecovery: true));
            Assert.Equal(ServeLifecycleState.Off, r.Next);
            Assert.Empty(r.Commands);
        }

        [Fact]
        public void Off_Detected_NotServable_StaysOff()
        {
            var r = Decide(ServeLifecycleState.Off, ServeTrigger.Detected,
                Known(OnDetectionPolicy.ServeImmediately, servable: false));
            Assert.Equal(ServeLifecycleState.Off, r.Next);
            Assert.Empty(r.Commands);
        }

        // ---- Off × other triggers --------------------------------------------------

        [Fact]
        public void Off_UserServe_Serves_OverridingSuppression()
        {
            var r = Decide(ServeLifecycleState.Off, ServeTrigger.UserServe,
                Known(OnDetectionPolicy.DoNothing, suppressed: true));
            Assert.Equal(ServeLifecycleState.Serving, r.Next);
            Assert.Equal(new[] { ServeCommand.StartServe }, r.Commands);
        }

        [Fact]
        public void Off_RecoveryMatched_Prompts()
        {
            var r = Decide(ServeLifecycleState.Off, ServeTrigger.RecoveryMatched, LifecycleContext.None);
            Assert.Equal(ServeLifecycleState.Recovering, r.Next);
            Assert.Equal(new[] { ServeCommand.PromptRecovery }, r.Commands);
        }

        [Theory]
        [InlineData(ServeTrigger.InstanceGone)]
        [InlineData(ServeTrigger.AppExit)]
        [InlineData(ServeTrigger.UserStop)]
        [InlineData(ServeTrigger.GraceElapsed)]
        public void Off_InertTriggers_StayOff(ServeTrigger trigger)
        {
            var r = Decide(ServeLifecycleState.Off, trigger, LifecycleContext.None);
            Assert.Equal(ServeLifecycleState.Off, r.Next);
            Assert.Empty(r.Commands);
        }

        // ---- Grace -----------------------------------------------------------------

        [Fact]
        public void Grace_Elapsed_Serves()
        {
            var r = Decide(ServeLifecycleState.Grace, ServeTrigger.GraceElapsed, LifecycleContext.None);
            Assert.Equal(ServeLifecycleState.Serving, r.Next);
            Assert.Equal(new[] { ServeCommand.StartServe }, r.Commands);
        }

        [Fact]
        public void Grace_UserServe_CancelsThenServes()
        {
            var r = Decide(ServeLifecycleState.Grace, ServeTrigger.UserServe, LifecycleContext.None);
            Assert.Equal(ServeLifecycleState.Serving, r.Next);
            Assert.Equal(new[] { ServeCommand.CancelGrace, ServeCommand.StartServe }, r.Commands);
        }

        [Fact] // "edit instead"
        public void Grace_UserStop_CancelsAndSuppresses()
        {
            var r = Decide(ServeLifecycleState.Grace, ServeTrigger.UserStop, LifecycleContext.None);
            Assert.Equal(ServeLifecycleState.Off, r.Next);
            Assert.Equal(new[] { ServeCommand.CancelGrace, ServeCommand.Suppress }, r.Commands);
        }

        [Theory]
        [InlineData(ServeTrigger.InstanceGone)]
        [InlineData(ServeTrigger.AppExit)]
        public void Grace_InstanceGoneOrExit_CancelsAndGoesOff(ServeTrigger trigger)
        {
            var r = Decide(ServeLifecycleState.Grace, trigger, LifecycleContext.None);
            Assert.Equal(ServeLifecycleState.Off, r.Next);
            Assert.Equal(new[] { ServeCommand.CancelGrace }, r.Commands);
        }

        [Fact]
        public void Grace_Detected_HoldsCountdown()
        {
            var r = Decide(ServeLifecycleState.Grace, ServeTrigger.Detected,
                Known(OnDetectionPolicy.ServeAfterGrace));
            Assert.Equal(ServeLifecycleState.Grace, r.Next);
            Assert.Empty(r.Commands);
        }

        // ---- Serving ---------------------------------------------------------------

        [Fact] // #100 — the regression this consolidation fixes
        public void Serving_AppExit_RenamesBackAndStops()
        {
            var r = Decide(ServeLifecycleState.Serving, ServeTrigger.AppExit, LifecycleContext.None);
            Assert.Equal(ServeLifecycleState.Off, r.Next);
            Assert.Equal(new[] { ServeCommand.StopServe }, r.Commands);
        }

        [Fact] // #96
        public void Serving_UserStop_StopsAndSuppresses()
        {
            var r = Decide(ServeLifecycleState.Serving, ServeTrigger.UserStop, LifecycleContext.None);
            Assert.Equal(ServeLifecycleState.Off, r.Next);
            Assert.Equal(new[] { ServeCommand.StopServe, ServeCommand.Suppress }, r.Commands);
        }

        [Fact] // E5 — Desktop closed: no rename-back
        public void Serving_InstanceGone_EndsWithoutRestore()
        {
            var r = Decide(ServeLifecycleState.Serving, ServeTrigger.InstanceGone, LifecycleContext.None);
            Assert.Equal(ServeLifecycleState.Off, r.Next);
            Assert.Equal(new[] { ServeCommand.EndServeNoRestore }, r.Commands);
        }

        [Theory] // #102 — already serving this ws; recovery and re-detect are no-ops
        [InlineData(ServeTrigger.RecoveryMatched)]
        [InlineData(ServeTrigger.Detected)]
        [InlineData(ServeTrigger.UserServe)]
        public void Serving_NoOpTriggers_StayServing(ServeTrigger trigger)
        {
            var r = Decide(ServeLifecycleState.Serving, trigger, Known(OnDetectionPolicy.ServeImmediately));
            Assert.Equal(ServeLifecycleState.Serving, r.Next);
            Assert.Empty(r.Commands);
        }

        // ---- Recovering ------------------------------------------------------------

        [Fact]
        public void Recovering_Resume_Serves()
        {
            var r = Decide(ServeLifecycleState.Recovering, ServeTrigger.RecoveryResume, LifecycleContext.None);
            Assert.Equal(ServeLifecycleState.Serving, r.Next);
            Assert.Equal(new[] { ServeCommand.ResumeServe }, r.Commands);
        }

        [Fact]
        public void Recovering_Restore_GoesOff()
        {
            var r = Decide(ServeLifecycleState.Recovering, ServeTrigger.RecoveryRestore, LifecycleContext.None);
            Assert.Equal(ServeLifecycleState.Off, r.Next);
            Assert.Equal(new[] { ServeCommand.RestoreName }, r.Commands);
        }

        [Fact]
        public void Recovering_InstanceGone_AbandonsPrompt()
        {
            var r = Decide(ServeLifecycleState.Recovering, ServeTrigger.InstanceGone, LifecycleContext.None);
            Assert.Equal(ServeLifecycleState.Off, r.Next);
            Assert.Empty(r.Commands);
        }

        [Fact]
        public void Recovering_AppExit_LeavesRecordForNextLaunch()
        {
            var r = Decide(ServeLifecycleState.Recovering, ServeTrigger.AppExit, LifecycleContext.None);
            Assert.Equal(ServeLifecycleState.Off, r.Next);
            Assert.Empty(r.Commands);
        }

        [Fact]
        public void Recovering_Detected_Holds()
        {
            var r = Decide(ServeLifecycleState.Recovering, ServeTrigger.Detected, LifecycleContext.None);
            Assert.Equal(ServeLifecycleState.Recovering, r.Next);
            Assert.Empty(r.Commands);
        }

        // ---- IsServable ------------------------------------------------------------

        [Fact]
        public void IsServable_true_for_alias_and_valid_port()
        {
            var rule = new PortMappingRule { StableAlias = "Sales", FixedPort = 55555 };
            Assert.True(ServeLifecycleMachine.IsServable(rule));
        }

        [Theory]
        [InlineData(null, 55555)]  // no alias
        [InlineData("  ", 55555)]  // blank alias
        [InlineData("Sales", 0)]   // no port
        [InlineData("Sales", 80)]  // port out of range
        public void IsServable_false_when_alias_or_port_unusable(string alias, int port)
        {
            var rule = new PortMappingRule { StableAlias = alias, FixedPort = port };
            Assert.False(ServeLifecycleMachine.IsServable(rule));
        }

        [Fact]
        public void IsServable_false_for_null_rule() => Assert.False(ServeLifecycleMachine.IsServable(null));

        // ---- Table completeness: every (state, trigger) resolves without throwing --

        [Fact]
        public void EveryStateTriggerPair_IsDefined()
        {
            var states = (ServeLifecycleState[])System.Enum.GetValues(typeof(ServeLifecycleState));
            var triggers = (ServeTrigger[])System.Enum.GetValues(typeof(ServeTrigger));
            foreach (var s in states)
                foreach (var t in triggers)
                {
                    var r = Decide(s, t, Known(OnDetectionPolicy.ServeImmediately));
                    Assert.NotNull(r);
                    Assert.NotNull(r.Commands);
                    Assert.DoesNotContain(ServeCommand.None, r.Commands); // no-ops are empty lists, never [None]
                }
        }
    }
}
