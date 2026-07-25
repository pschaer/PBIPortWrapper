using System.Linq;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;
using Xunit;

namespace PBIPortWrapper.Core.Tests
{
    /// <summary>
    /// Covers the pure host-state logic (v0.7, #84): detection-policy → target
    /// state, grace-period flag, action → state transitions, the actions offered
    /// per state, and the legacy-boolean → policy mapping used by migration.
    /// </summary>
    public sealed class HostStateMachineTests
    {
        [Theory]
        [InlineData(OnDetectionPolicy.DoNothing, HostState.Off)]
        [InlineData(OnDetectionPolicy.Forward, HostState.Forward)]
        [InlineData(OnDetectionPolicy.ServeAfterGrace, HostState.Serve)]
        [InlineData(OnDetectionPolicy.ServeImmediately, HostState.Serve)]
        public void TargetOnDetection_maps_policy_to_state(OnDetectionPolicy policy, HostState expected)
        {
            Assert.Equal(expected, HostStateMachine.TargetOnDetection(policy));
        }

        [Fact]
        public void TargetOnDetection_null_rule_is_Off()
        {
            Assert.Equal(HostState.Off, HostStateMachine.TargetOnDetection((PortMappingRule)null));
        }

        [Fact]
        public void TargetOnDetection_rule_uses_its_policy()
        {
            var rule = new PortMappingRule { OnDetection = OnDetectionPolicy.ServeImmediately };
            Assert.Equal(HostState.Serve, HostStateMachine.TargetOnDetection(rule));
        }

        [Theory]
        [InlineData(OnDetectionPolicy.ServeAfterGrace, true)]
        [InlineData(OnDetectionPolicy.ServeImmediately, false)]
        [InlineData(OnDetectionPolicy.Forward, false)]
        [InlineData(OnDetectionPolicy.DoNothing, false)]
        public void UsesGracePeriod_only_for_grace_policy(OnDetectionPolicy policy, bool expected)
        {
            Assert.Equal(expected, HostStateMachine.UsesGracePeriod(policy));
        }

        [Theory]
        [InlineData(HostState.Off, HostAction.Forward, HostState.Forward)]
        [InlineData(HostState.Off, HostAction.Serve, HostState.Serve)]
        [InlineData(HostState.Forward, HostAction.Serve, HostState.Serve)]
        [InlineData(HostState.Forward, HostAction.Stop, HostState.Off)]
        [InlineData(HostState.Serve, HostAction.StopServing, HostState.Forward)]
        [InlineData(HostState.Serve, HostAction.Stop, HostState.Off)]
        public void Apply_transitions_to_expected_state(HostState from, HostAction action, HostState expected)
        {
            Assert.Equal(expected, HostStateMachine.Apply(from, action));
        }

        [Fact]
        public void StopServing_drops_to_Forward_not_Off()
        {
            // Stopping a serve session to edit keeps the port forwarded.
            Assert.Equal(HostState.Forward, HostStateMachine.Apply(HostState.Serve, HostAction.StopServing));
        }

        [Theory]
        [InlineData(HostState.Off, new[] { HostAction.Forward, HostAction.Serve })]
        [InlineData(HostState.Forward, new[] { HostAction.Serve, HostAction.Stop })]
        [InlineData(HostState.Serve, new[] { HostAction.StopServing })]
        public void AvailableActions_offers_the_right_moves(HostState state, HostAction[] expected)
        {
            Assert.Equal(expected, HostStateMachine.AvailableActions(state).ToArray());
        }

        [Fact]
        public void AvailableActions_never_offers_the_current_state_as_a_move()
        {
            // No action should resolve back to the state it was offered from.
            foreach (HostState state in new[] { HostState.Off, HostState.Forward, HostState.Serve })
            {
                foreach (var action in HostStateMachine.AvailableActions(state))
                    Assert.NotEqual(state, HostStateMachine.Apply(state, action));
            }
        }

        [Theory]
        [InlineData(false, false, HostState.Off)]
        [InlineData(false, true, HostState.Forward)]
        [InlineData(true, false, HostState.Serve)]  // serving wins even if the forward flag lags
        [InlineData(true, true, HostState.Serve)]
        public void CurrentState_projects_live_service_state(bool serving, bool forwarding, HostState expected)
        {
            Assert.Equal(expected, HostStateMachine.CurrentState(serving, forwarding));
        }

        [Theory]
        [InlineData(false, false, OnDetectionPolicy.DoNothing)]
        [InlineData(true, false, OnDetectionPolicy.Forward)]
        [InlineData(false, true, OnDetectionPolicy.ServeImmediately)]
        [InlineData(true, true, OnDetectionPolicy.ServeImmediately)] // AutoServe wins
        public void FromLegacy_maps_booleans_to_policy(bool autoConnect, bool autoServe, OnDetectionPolicy expected)
        {
            Assert.Equal(expected, HostStateMachine.FromLegacy(autoConnect, autoServe));
        }
    }
}
