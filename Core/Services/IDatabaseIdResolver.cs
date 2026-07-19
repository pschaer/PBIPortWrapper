using System.Threading.Tasks;

namespace PBIPortWrapper.Services
{
    /// <summary>
    /// Resolves the database ID of the (single) workspace database on a local
    /// Analysis Services port. The ID is immutable through rename (E2/#40), which
    /// makes it the crash-recovery match key (#58). Returns null when the instance
    /// cannot be queried (still loading, gone, refused) — callers must treat null
    /// as "undecided", never as "no match".
    /// </summary>
    public interface IDatabaseIdResolver
    {
        Task<string> GetDatabaseIdAsync(int port);
    }
}
