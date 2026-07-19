using System.Threading.Tasks;
using PBIPortWrapper.Models;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Seam between the serve-session state machine and the live Analysis Services
    /// instance, so ServeSessionService is unit-testable without Desktop running.
    /// DatabaseRenameService is the production implementation.
    /// </summary>
    public interface IRenameEngine
    {
        Task<RenameResult> RenameAsync(int port, string currentDbName, string newName);
    }
}
