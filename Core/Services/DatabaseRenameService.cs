using System;
using System.Threading.Tasks;
using Microsoft.AnalysisServices.Tabular;
using PBIPortWrapper.Models;

namespace PBIPortWrapper.Services
{
    public class DatabaseRenameService : IRenameEngine, IDatabaseIdResolver
    {
        private readonly ILogger _logger;

        public DatabaseRenameService(ILogger logger)
        {
            _logger = logger;
        }

        Task<RenameResult> IRenameEngine.RenameAsync(int port, string currentDbName, string newName)
            => RenameDatabaseAsync(port, currentDbName, newName);

        /// <summary>
        /// Database ID of the single workspace DB on the given port, or null if the
        /// instance cannot be queried. IDs survive renames (E2), so this is the
        /// crash-recovery match key (#58).
        /// </summary>
        public async Task<string> GetDatabaseIdAsync(int port)
        {
            return await Task.Run(() =>
            {
                Server server = new Server();
                try
                {
                    server.Connect($"Data Source=localhost:{port};Connect Timeout=5");
                    return server.Databases.Count > 0 ? server.Databases[0].ID : null;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning("Rename", $"Could not resolve database ID on port {port}: {ex.Message}");
                    return null;
                }
                finally
                {
                    if (server.Connected)
                        server.Disconnect();
                    server.Dispose();
                }
            });
        }

        public async Task<RenameResult> RenameDatabaseAsync(int port, string currentDbName, string newName)
        {
            return await Task.Run(() =>
            {
                Server server = new Server();
                try
                {
                    // Validate Name (shared rules — see AliasValidator)
                    var (isValid, errorMessage) = AliasValidator.ValidateAlias(newName);
                    if (!isValid)
                        return RenameResult.Fail(errorMessage);

                    // Connect
                    string connectionString = $"Data Source=localhost:{port}";
                    server.Connect(connectionString);

                    // Find Database
                    Database database;
                    if (!string.IsNullOrEmpty(currentDbName))
                    {
                        database = server.Databases.FindByName(currentDbName);
                        if (database == null)
                        {
                            // Try finding by ID if Name lookup failed (sometimes they differ, though usually same for PBI)
                            database = server.Databases.Find(currentDbName);
                        }
                    }
                    else
                    {
                        // Fallback: If no current name provided, take the first one (safest for single-model instances)
                        if (server.Databases.Count > 0)
                            database = server.Databases[0];
                        else
                            return RenameResult.Fail("No database found on the instance.");
                    }

                    if (database == null)
                        return RenameResult.Fail($"Database '{currentDbName}' not found.");

                    // Check if name is actually different
                    if (database.Name.Equals(newName, StringComparison.OrdinalIgnoreCase))
                        return RenameResult.Ok("Database already has this name.");

                    // Capture old name for logging
                    string oldName = database.Name;

                    // Rename
                    // TOM allows direct rename
                    database.Name = newName;
                    database.Update(Microsoft.AnalysisServices.UpdateOptions.ExpandFull); // Critical: Update the object

                    return RenameResult.Ok(
                        $"Successfully renamed database from '{oldName}' to '{newName}'.",
                        "Warning: Power BI Desktop will disconnect from this model and may display errors. This is expected."
                    );
                }
                catch (Microsoft.AnalysisServices.AmoException amoEx)
                {
                    return RenameResult.Fail($"Analysis Services Error: {amoEx.Message}");
                }
                catch (Exception ex)
                {
                    return RenameResult.Fail($"Unexpected Error: {ex.Message}");
                }
                finally
                {
                    if (server.Connected)
                        server.Disconnect();
                    server.Dispose();
                }
            });
        }
    }
}
