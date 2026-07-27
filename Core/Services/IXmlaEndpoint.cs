using PBIPortWrapper.Models;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// The listener half of the XMLA endpoint, as <see cref="XmlaEndpointCoordinator"/>
    /// needs it (#125).
    ///
    /// It exists so the coordinator's decisions — when to restart, when to leave a
    /// running listener alone, what to report after a bind failure — are testable
    /// without binding a real port. <see cref="HttpBridgeService"/> is the real one.
    /// </summary>
    public interface IXmlaEndpoint
    {
        bool IsRunning { get; }

        /// <summary>The prefix actually bound, which may be the localhost fallback.</summary>
        string BoundPrefix { get; }

        /// <summary>Binds and starts accepting. Throws if it cannot bind.</summary>
        void Start(HttpBridgeConfig config);

        void Stop();
    }
}
