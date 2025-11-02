using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerBIPortWrapper.Models;
using Microsoft.AnalysisServices.AdomdClient;

namespace PowerBIPortWrapper.Services
{
    public class PowerBIDetector
    {
        private readonly string _workspacesPath;

        public PowerBIDetector()
        {
            _workspacesPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Power BI Desktop\AnalysisServicesWorkspaces"
            );
        }

        public List<PowerBIInstance> DetectRunningInstances()
        {
            var instances = new List<PowerBIInstance>();

            if (!Directory.Exists(_workspacesPath))
            {
                return instances;
            }

            try
            {
                var workspaceDirs = Directory.GetDirectories(_workspacesPath);

                foreach (var workspaceDir in workspaceDirs)
                {
                    try
                    {
                        var portFile = Path.Combine(workspaceDir, @"Data\msmdsrv.port.txt");

                        if (File.Exists(portFile))
                        {
                            var portText = File.ReadAllText(portFile).Trim();
                            if (int.TryParse(portText, out int port))
                            {
                                // Get database name by connecting to the instance
                                string databaseName = GetDatabaseName(port);

                                var instance = new PowerBIInstance
                                {
                                    WorkspaceId = Path.GetFileName(workspaceDir),
                                    Port = port,
                                    DatabaseName = databaseName,
                                    LastModified = Directory.GetLastWriteTime(workspaceDir),
                                    FilePath = workspaceDir,
                                    FileName = $"Workspace-{Path.GetFileName(workspaceDir).Substring(0, Math.Min(8, Path.GetFileName(workspaceDir).Length))}"
                                };

                                instances.Add(instance);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Error processing workspace {workspaceDir}: {ex.Message}");
                    }
                }

                instances = instances.OrderByDescending(i => i.LastModified).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error detecting instances: {ex.Message}");
            }

            return instances;
        }

        private string GetDatabaseName(int port)
        {
            try
            {
                string connectionString = $"Data Source=localhost:{port};";

                using (var connection = new AdomdConnection(connectionString))
                {
                    connection.Open();

                    // Query for databases using GetSchemaDataSet
                    var schemaTable = connection.GetSchemaDataSet("DBSCHEMA_CATALOGS", null);

                    if (schemaTable != null && schemaTable.Tables.Count > 0 && schemaTable.Tables[0].Rows.Count > 0)
                    {
                        // Get the first database name (CATALOG_NAME column)
                        return schemaTable.Tables[0].Rows[0]["CATALOG_NAME"].ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting database name for port {port}: {ex.Message}");
            }

            return null;
        }

        private int? ReadPortFromFile(string portFile)
        {
            // Try multiple times with different encodings and wait a bit
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    // Wait a bit to ensure file is fully written
                    if (attempt > 0)
                    {
                        System.Threading.Thread.Sleep(100);
                    }

                    // Try reading with different methods
                    string portText = null;

                    // Method 1: ReadAllText with UTF8
                    try
                    {
                        portText = File.ReadAllText(portFile, System.Text.Encoding.UTF8);
                    }
                    catch { }

                    // Method 2: ReadAllText with Default encoding
                    if (string.IsNullOrWhiteSpace(portText))
                    {
                        try
                        {
                            portText = File.ReadAllText(portFile, System.Text.Encoding.Default);
                        }
                        catch { }
                    }

                    // Method 3: ReadAllBytes and convert
                    if (string.IsNullOrWhiteSpace(portText))
                    {
                        try
                        {
                            var bytes = File.ReadAllBytes(portFile);
                            portText = System.Text.Encoding.ASCII.GetString(bytes);
                        }
                        catch { }
                    }

                    if (!string.IsNullOrWhiteSpace(portText))
                    {
                        // Clean up the text - remove any non-numeric characters except digits
                        portText = new string(portText.Where(c => char.IsDigit(c)).ToArray());

                        if (!string.IsNullOrEmpty(portText) && int.TryParse(portText, out int port))
                        {
                            if (port > 1024 && port < 65536) // Validate port range
                            {
                                return port;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Attempt {attempt + 1} failed: {ex.Message}");
                }
            }

            return null;
        }

        public bool IsWorkspacePathValid()
        {
            return Directory.Exists(_workspacesPath);
        }
    }
}
