using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace PBIPortWrapper.Presenters
{
    // FILE SIZE: MAX 250 lines - enforced by build target
    /// <summary>
    /// Single owner of the serve lifecycle (v0.7 consolidation — see
    /// docs/HANDOVER-2026-07-24-serve-lifecycle.md). Turns each detection snapshot,
    /// grace-timer tick and application exit into <see cref="ServeLifecycleMachine"/>
    /// transitions and executes the resulting commands against ServeSessionService,
    /// grace timers and tray toasts. Replaces AutoServeController and the old
    /// swallow-on-exit RestoreAllAsync (exit now runs Serving×AppExit per session,
    /// logged, #100). Crash recovery stays in ServeRecoveryCoordinator until #88; this
    /// only stands down for a model with a pending recovery record (#102).
    /// </summary>
    public class ServeLifecycleCoordinator
    {
        private const int GraceSeconds = 10;

        private readonly ServeSessionService _sessions;
        private readonly ConfigService _config;
        private readonly IServeToasts _toasts;
        private readonly Action _showDashboard;
        private readonly Action<string> _log;

        private readonly Dictionary<string, WinFormsTimer> _graceTimers = new();
        private readonly HashSet<string> _newModelNotified = new();
        private readonly HashSet<string> _inFlight = new();
        private readonly HashSet<string> _suppressed = new();
        private readonly object _suppressLock = new();

        public ServeLifecycleCoordinator(
            ServeSessionService sessions,
            ConfigService config,
            IServeToasts toasts,
            Action showDashboard,
            Action<string> log)
        {
            _sessions = sessions;
            _config = config;
            _toasts = toasts;
            _showDashboard = showDashboard;
            _log = log;

            // A session ending is a user Stop (suppress re-serve until reopen, #96) or a
            // Desktop close (the instance leaves the snapshot, which clears suppression).
            if (_sessions != null)
                _sessions.SessionEnded += (s, e) => Suppress(e?.Session?.WorkspaceId);
        }

        // ---- detection -------------------------------------------------------------

        /// <summary>Applies the serve lifecycle to the latest detection snapshot.</summary>
        public void OnSnapshot(IReadOnlyList<PowerBIInstance> instances)
        {
            var live = new HashSet<string>(
                instances?.Select(i => i.WorkspaceId).Where(id => !string.IsNullOrEmpty(id))
                ?? Enumerable.Empty<string>());
            Forget(live);

            if (instances == null) return;
            foreach (var instance in instances)
                Evaluate(instance);
        }

        private void Evaluate(PowerBIInstance instance)
        {
            string ws = instance.WorkspaceId;
            if (string.IsNullOrEmpty(ws) || IsUntitled(instance)) return;
            // A serve is mid-flight (session not yet registered): let it land first,
            // otherwise the next snapshot would decide "Off → serve" again.
            if (_inFlight.Contains(ws)) return;

            var rule = _config?.FindRule(instance.FileName);
            var transition = ServeLifecycleMachine.Decide(
                CurrentState(ws), ServeTrigger.Detected, BuildContext(ws, rule));
            Execute(transition, instance, rule);
        }

        private LifecycleContext BuildContext(string ws, PortMappingRule rule)
        {
            bool hasRecovery = _config?.Current?.ServeRecoveryRecords?.Any(r => r.WorkspaceId == ws) == true;
            return new LifecycleContext(
                isKnownModel: rule != null,
                policy: rule?.OnDetection ?? OnDetectionPolicy.DoNothing,
                isServable: ServeLifecycleMachine.IsServable(rule),
                isSuppressed: IsSuppressed(ws),
                hasRecoveryRecord: hasRecovery);
        }

        // The serve state observed from live services; recovery is external for now.
        private ServeLifecycleState CurrentState(string ws)
        {
            if (_sessions?.FindSession(ws) != null) return ServeLifecycleState.Serving;
            if (_graceTimers.ContainsKey(ws)) return ServeLifecycleState.Grace;
            return ServeLifecycleState.Off;
        }

        /// <summary>Runs the non-exit commands of a transition (detection / grace).</summary>
        private void Execute(ServeTransition transition, PowerBIInstance instance, PortMappingRule rule)
        {
            foreach (var command in transition.Commands)
            {
                switch (command)
                {
                    case ServeCommand.StartServe:
                        _ = ServeAsync(instance, rule);
                        break;
                    case ServeCommand.StartGrace:
                        StartGrace(instance, rule);
                        break;
                    case ServeCommand.CancelGrace:
                        StopGrace(instance.WorkspaceId);
                        break;
                    case ServeCommand.Suppress:
                        Suppress(instance.WorkspaceId);
                        break;
                    case ServeCommand.NotifyNewModel:
                        if (_newModelNotified.Add(instance.WorkspaceId))
                            _toasts.NewModel(instance.FileName, _showDashboard);
                        break;
                }
            }
        }

        private void StartGrace(PowerBIInstance instance, PortMappingRule rule)
        {
            string ws = instance.WorkspaceId;
            var timer = new WinFormsTimer { Interval = GraceSeconds * 1000 };
            timer.Tick += (s, e) =>
            {
                // A WinForms Timer repeats until stopped; the countdown is one-shot, so
                // stop it before serving or it re-fires every interval ("Already serving").
                StopGrace(ws);
                Execute(
                    ServeLifecycleMachine.Decide(ServeLifecycleState.Grace, ServeTrigger.GraceElapsed, LifecycleContext.None),
                    instance, rule);
            };
            _graceTimers[ws] = timer;
            timer.Start();
            // "Edit instead": the Grace×UserStop cell cancels the countdown and suppresses.
            _toasts.GracePending(instance.FileName, GraceSeconds, () => Execute(
                ServeLifecycleMachine.Decide(ServeLifecycleState.Grace, ServeTrigger.UserStop, LifecycleContext.None),
                instance, rule));
        }

        private void StopGrace(string ws)
        {
            if (ws != null && _graceTimers.TryGetValue(ws, out var timer))
            {
                timer.Stop();
                timer.Dispose();
                _graceTimers.Remove(ws);
            }
        }

        private async Task ServeAsync(PowerBIInstance instance, PortMappingRule rule)
        {
            string ws = instance.WorkspaceId;
            if (!_inFlight.Add(ws)) return;
            try
            {
                // Auto policies imply consent; the grace toast is the escape hatch and
                // serving is reversible from the tray.
                //
                // Offload: StartServingAsync runs a synchronous UIA probe before its
                // first await; on the UI thread (from a snapshot) that freezes the app.
                // _inFlight and the toast stay on the UI thread (no ConfigureAwait(false)).
                var result = await Task.Run(
                    () => _sessions.StartServingAsync(instance, rule, userConfirmedSaved: true));
                _log?.Invoke(result.Message);
                if (result.Success)
                    _toasts.ServingReady(instance.FileName, ConnectionEndpoint.For(rule));
                else
                    _toasts.ServeFailed(instance.FileName, result.Message);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Auto-serve failed for {instance.FileName}: {ex.Message}");
            }
            finally
            {
                _inFlight.Remove(ws);
            }
        }

        // ---- exit ------------------------------------------------------------------

        /// <summary>
        /// Graceful shutdown: run Serving×AppExit (→ StopServe) for every active session
        /// — rename back + stop proxy. Each is guarded and logged, so a failure is loud
        /// and never leaves a database silently renamed on exit (#100); best-effort.
        /// </summary>
        public async Task OnAppExitAsync()
        {
            foreach (var session in _sessions.ActiveSessions)
            {
                var transition = ServeLifecycleMachine.Decide(
                    ServeLifecycleState.Serving, ServeTrigger.AppExit, LifecycleContext.None);
                foreach (var command in transition.Commands)
                {
                    if (command != ServeCommand.StopServe) continue;
                    try
                    {
                        var result = await _sessions.StopServingAsync(session.WorkspaceId).ConfigureAwait(false);
                        _log?.Invoke($"Exit: {result.Message}");
                    }
                    catch (Exception ex)
                    {
                        _log?.Invoke($"Exit: restoring '{session.FileName}' failed: {ex.Message}");
                    }
                }
            }
        }

        // ---- suppression / housekeeping --------------------------------------------

        private void Forget(HashSet<string> live)
        {
            foreach (var ws in _graceTimers.Keys.Where(k => !live.Contains(k)).ToList())
                StopGrace(ws);
            _newModelNotified.RemoveWhere(ws => !live.Contains(ws));
            _inFlight.RemoveWhere(ws => !live.Contains(ws));
            // A gone instance is a fresh session next time: drop its suppression.
            lock (_suppressLock) _suppressed.RemoveWhere(ws => !live.Contains(ws));
        }

        private void Suppress(string ws)
        {
            if (string.IsNullOrEmpty(ws)) return;
            lock (_suppressLock) _suppressed.Add(ws);
        }

        private bool IsSuppressed(string ws)
        {
            lock (_suppressLock) return _suppressed.Contains(ws);
        }

        private static bool IsUntitled(PowerBIInstance instance) =>
            string.IsNullOrEmpty(instance.FileName)
            || instance.FileName.Equals("Untitled", StringComparison.OrdinalIgnoreCase);
    }
}
