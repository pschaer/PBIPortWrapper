using System;
using System.Windows.Forms;

namespace PBIPortWrapper.Presenters
{
    /// <summary>
    /// Tray balloon notifications for the auto-host workflow (#85b). NotifyIcon
    /// exposes a single BalloonTipClicked event, so one click action is attached
    /// per balloon (the latest one wins) - enough for the "click to act" toasts.
    /// </summary>
    public class TrayToastService : IServeToasts
    {
        private readonly NotifyIcon _icon;
        private readonly Func<string, string> _connectionStringFor;
        private Action _onClick;

        /// <summary>
        /// <paramref name="connectionStringFor"/> maps a served model's alias to the
        /// connection string a client would use, or empty when the model is not
        /// reachable (endpoint off). The toast offered to copy "the connection string"
        /// and copied the bare alias until this was wired.
        /// </summary>
        public TrayToastService(NotifyIcon icon, Func<string, string> connectionStringFor = null)
        {
            _icon = icon;
            _connectionStringFor = connectionStringFor ?? (_ => string.Empty);
            _icon.BalloonTipClicked += (s, e) =>
            {
                var action = _onClick;
                _onClick = null;
                action?.Invoke();
            };
        }

        public void Show(string title, string text, Action onClick = null, ToolTipIcon icon = ToolTipIcon.Info)
        {
            _onClick = onClick;
            _icon.Visible = true;
            _icon.ShowBalloonTip(8000, title, text, icon);
        }

        public void ServingReady(string model, string alias)
        {
            string connectionString = _connectionStringFor(alias);

            // Nothing to copy when the endpoint is off: the model is served but not
            // reachable, so say that instead of offering a string that cannot connect.
            if (string.IsNullOrEmpty(connectionString))
            {
                Show("Serving " + model,
                    $"Ready as '{alias}'. Turn the XMLA endpoint on to connect to it.");
                return;
            }

            Show("Serving " + model,
                $"Ready as '{alias}'. Click to copy the connection string.",
                () => { try { Clipboard.SetText(connectionString); } catch { } });
        }

        public void GracePending(string model, int seconds, Action onCancel) =>
            Show(model,
                $"Serving in {seconds}s - click to cancel (keeps it editable).",
                onCancel, ToolTipIcon.Warning);

        public void NewModel(string model, Action onSetUp) =>
            Show("New model detected",
                $"{model} - click to set it up in the dashboard.",
                onSetUp);

        public void ServeFailed(string model, string message) =>
            Show("Could not serve " + model, message, null, ToolTipIcon.Error);

        /// <summary>
        /// The endpoint was asked to run and did not. Announced rather than left in the
        /// log and the menu label, because the symptom otherwise is a client that cannot
        /// connect and no indication anywhere that the machine knows why.
        /// </summary>
        public void EndpointFailed(string reason, Action onClick) =>
            Show("XMLA endpoint is not running",
                reason + " - click to open the endpoint settings.",
                onClick, ToolTipIcon.Error);
    }
}
