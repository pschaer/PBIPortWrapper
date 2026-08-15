using PBIRelay.Models;

namespace PBIRelay.Services
{
    /// <summary>
    /// Decides when an endpoint failure is worth interrupting someone about.
    ///
    /// A failed start already reaches log.txt, the tray menu's label and the endpoint
    /// settings dialog - but every one of those has to be gone looking for. The endpoint
    /// simply does not answer, and the reason sits somewhere the user has no cause to
    /// open. So the failure gets announced instead of waiting to be found.
    ///
    /// What it must NOT do is nag. <c>ConfigurationChanged</c> fires for every serve,
    /// rule edit and policy change, and each one produces a status. Announcing the same
    /// unchanged failure every time would train the user to dismiss it, which is a worse
    /// place to be than silence.
    ///
    /// So: announce a failure once, announce a DIFFERENT failure again, and rearm when
    /// the endpoint recovers or is switched off - a failure after a good run is news.
    ///
    /// Pure and separate from the toast because the app layer is not reachable from the
    /// tests, and this rule is the part worth locking down.
    /// </summary>
    public class EndpointFailureWatcher
    {
        private string _announced;

        /// <summary>
        /// True when <paramref name="status"/> is a failure that has not been announced
        /// yet. Records it, so a second identical status returns false.
        /// </summary>
        public bool ShouldAnnounce(EndpointStatus status)
        {
            string failure = FailureOf(status);

            if (failure == null)
            {
                // Recovered, or deliberately off. Either way the next failure is news
                // again, even if it happens to have the same reason as the last one.
                _announced = null;
                return false;
            }

            if (failure == _announced) return false;

            _announced = failure;
            return true;
        }

        /// <summary>
        /// The reason to announce, or null when there is nothing wrong.
        ///
        /// "Enabled but not running" is the failure, whether or not an exception message
        /// came with it: an endpoint that was asked to run and is not running has failed,
        /// and a user who turned it on is owed that either way.
        /// </summary>
        private static string FailureOf(EndpointStatus status)
        {
            if (status == null) return null;
            if (!status.Enabled || status.Running) return null;

            return string.IsNullOrEmpty(status.Error) ? "Not running" : status.Error;
        }
    }
}
