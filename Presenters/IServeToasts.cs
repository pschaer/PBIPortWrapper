using System;

namespace PBIPortWrapper.Presenters
{
    /// <summary>
    /// The tray-toast surface the <see cref="ServeLifecycleCoordinator"/> drives.
    /// Extracted from <see cref="TrayToastService"/> so the coordinator can be
    /// unit-tested without a live NotifyIcon.
    /// </summary>
    public interface IServeToasts
    {
        void ServingReady(string model, string connection);
        void GracePending(string model, int seconds, Action onCancel);
        void NewModel(string model, Action onSetUp);
        void ServeFailed(string model, string message);
    }
}
