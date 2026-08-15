using System.Linq;
using PBIRelay.Models;
using PBIRelay.Services;
using Xunit;

namespace PBIRelay.Core.Tests
{
    /// <summary>
    /// Covers the pure host-state logic (#84): detection-policy → target state, the
    /// grace-period flag, action → state transitions, the actions offered per state,
    /// and the legacy-boolean → policy mapping used by migration.
    ///
    /// The axis is Off ↔ Serve since #126 retired forwarding.
    /// </summary>
    public sealed class HostStateMachineTests
    {
        [Theory]
        [InlineData(OnDetectionPolicy.DoNothing, HostState.Off)]
        [InlineData(OnDetectionPolicy.ServeAfterGrace, HostState.Serve)]
        [InlineData(OnDetectionPolicy.ServeImmediately, HostState.Serve)]
        public void TargetOnDetection_maps_policy_to_state(OnDetectionPolicy policy, HostState expected)
        {
            Assert.Equal(expected, HostStateMachine.TargetOnDetection(policy));
        }

        [Fact]
        public void TargetOnDetection_null_rule_is_Off()
        {
            Assert.Equal(HostState.Off, HostStateMachine.TargetOnDetection((ModelRule)null));
        }

        [Fact]
        public void TargetOnDetection_rule_uses_its_policy()
        {
            var rule = new ModelRule { OnDetection = OnDetectionPolicy.ServeImmediately };
            Assert.Equal(HostState.Serve, HostStateMachine.TargetOnDetection(rule));
        }

        [Fact]
        public void TargetOnDetection_of_a_retired_policy_value_is_Off()
        {
            // A config that still carries the retired Forward value (1) reaches this
            // before migration has necessarily run. It must land somewhere harmless
            // rather than fall through to serving.
            var rule = new ModelRule { OnDetection = (OnDetectionPolicy)1 };

            Assert.Equal(HostState.Off, HostStateMachine.TargetOnDetection(rule));
        }

        [Theory]
        [InlineData(OnDetectionPolicy.ServeAfterGrace, true)]
        [InlineData(OnDetectionPolicy.ServeImmediately, false)]
        [InlineData(OnDetectionPolicy.DoNothing, false)]
        public void UsesGracePeriod_only_for_grace_policy(OnDetectionPolicy policy, bool expected)
        {
            Assert.Equal(expected, HostStateMachine.UsesGracePeriod(policy));
        }

        [Theory]
        [InlineData(HostState.Off, HostAction.Serve, HostState.Serve)]
        [InlineData(HostState.Serve, HostAction.Stop, HostState.Off)]
        public void Apply_transitions_to_expected_state(HostState from, HostAction action, HostState expected)
        {
            Assert.Equal(expected, HostStateMachine.Apply(from, action));
        }

        [Theory]
        [InlineData(HostState.Off, new[] { HostAction.Serve })]
        [InlineData(HostState.Serve, new[] { HostAction.Stop })]
        public void AvailableActions_offers_the_right_moves(HostState state, HostAction[] expected)
        {
            Assert.Equal(expected, HostStateMachine.AvailableActions(state).ToArray());
        }

        [Fact]
        public void AvailableActions_never_offers_the_current_state_as_a_move()
        {
            // No action should resolve back to the state it was offered from.
            foreach (HostState state in new[] { HostState.Off, HostState.Serve })
            {
                foreach (var action in HostStateMachine.AvailableActions(state))
                    Assert.NotEqual(state, HostStateMachine.Apply(state, action));
            }
        }

        [Theory]
        [InlineData(false, HostState.Off)]
        [InlineData(true, HostState.Serve)]
        public void CurrentState_projects_live_service_state(bool serving, HostState expected)
        {
            Assert.Equal(expected, HostStateMachine.CurrentState(serving));
        }

        [Theory]
        [InlineData(false, false, OnDetectionPolicy.DoNothing)]
        [InlineData(false, true, OnDetectionPolicy.ServeImmediately)]
        [InlineData(true, true, OnDetectionPolicy.ServeImmediately)] // AutoServe wins
        public void FromLegacy_maps_booleans_to_policy(bool autoConnect, bool autoServe, OnDetectionPolicy expected)
        {
            Assert.Equal(expected, HostStateMachine.FromLegacy(autoConnect, autoServe));
        }

        [Fact]
        public void FromLegacy_does_not_promote_auto_forwarding_to_serving()
        {
            // AutoConnect meant "forward on detect". Forwarding is gone, and quietly
            // upgrading it to serving would start renaming databases and blocking
            // Desktop edits for someone who never asked for that (#126).
            Assert.Equal(OnDetectionPolicy.DoNothing, HostStateMachine.FromLegacy(autoConnect: true, autoServe: false));
        }
    }
}
