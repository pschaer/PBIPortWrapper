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
        private Action _onClick;

        public TrayToastService(NotifyIcon icon)
        {
            _icon = icon;
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

        public void ServingReady(string model, string connection) =>
            Show("Serving " + model,
                $"Ready at {connection}. Click to copy the connection string.",
                () => { try { Clipboard.SetText(connection); } catch { } });

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
    }
}
