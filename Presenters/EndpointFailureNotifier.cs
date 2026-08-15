using System;
using PBIRelay.Models;
using PBIRelay.Services;

namespace PBIRelay.Presenters
{
    // FILE SIZE: MAX 250 lines - enforced by build target
    /// <summary>
    /// Announces an XMLA endpoint that was asked to run and is not running (#132).
    ///
    /// A failed start already reaches log.txt, the tray menu's label and the endpoint
    /// settings dialog - but all three have to be gone looking for, and the symptom that
    /// sends anyone looking is a client that will not connect. So it gets said out loud.
    ///
    /// <see cref="EndpointFailureWatcher"/> owns the "once per distinct failure" rule and
    /// is tested; this is the wiring that cannot be.
    /// </summary>
    public class EndpointFailureNotifier
    {
        private readonly EndpointFailureWatcher _watcher = new EndpointFailureWatcher();
        private readonly XmlaEndpointCoordinator _endpoint;
        private readonly TrayToastService _toasts;
        private readonly Action _openSettings;
        private readonly Action<string> _log;

        /// <param name="onUiThread">
        /// Marshals to the UI thread. Status can be raised from a background serve, and
        /// a balloon is UI.
        /// </param>
        public EndpointFailureNotifier(
            XmlaEndpointCoordinator endpoint,
            TrayToastService toasts,
            Action openSettings,
            Action<string> log,
            Action<Action> onUiThread)
        {
            _endpoint = endpoint;
            _toasts = toasts;
            _openSettings = openSettings;
            _log = log;

            if (_endpoint != null)
                _endpoint.StatusChanged += (s, status) => onUiThread(() => Announce(status));
        }

        /// <summary>
        /// Announces the status as it already stands.
        ///
        /// Needed because <c>ApplicationPresenter</c> applies the configuration in its own
        /// constructor: a start that failed there raised its status before anything here
        /// existed, and the startup failure is the one most worth hearing about. Call it
        /// once the window exists - a balloon needs one.
        /// </summary>
        public void CheckNow() => Announce(_endpoint?.Status);

        private void Announce(EndpointStatus status)
        {
            if (!_watcher.ShouldAnnounce(status)) return;

            string reason = string.IsNullOrEmpty(status.Error) ? "It is not running" : status.Error;
            _toasts?.EndpointFailed(reason, _openSettings);
            _log?.Invoke($"XMLA endpoint is not running: {reason}");
        }
    }
}
