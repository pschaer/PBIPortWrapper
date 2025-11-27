using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Text;
using Microsoft.AnalysisServices.AdomdClient;
using PBIPortWrapper.Models;

namespace PBIPortWrapper.Services
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
                            string portText = ReadPortFile(portFile);

                            if (int.TryParse(portText, out int port))
                            {
                                string databaseName = GetDatabaseName(port);
                                var (processId, parentProcessId, friendlyName) = GetProcessInfo(workspaceDir);

                                var instance = new PowerBIInstance
                                {
                                    WorkspaceId = Path.GetFileName(workspaceDir),
                                    Port = port,
                                    DatabaseName = databaseName,
                                    LastModified = Directory.GetLastWriteTime(workspaceDir),
                                    FilePath = workspaceDir,
                                    FileName = friendlyName ?? GetFriendlyNameFallback(workspaceDir),
                                    ProcessId = processId,
                                    ParentProcessId = parentProcessId
                                };

                                instances.Add(instance);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Skip this workspace if we can't process it
                        System.Diagnostics.Debug.WriteLine($"Error processing workspace: {ex.Message}");
                    }
                }

                return instances.OrderByDescending(i => i.LastModified).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error detecting instances: {ex.Message}");
                return instances;
            }
        }

        private (int processId, int parentProcessId, string friendlyName) GetProcessInfo(string workspaceDir)
        {
            try
            {
                // Normalize path for comparison
                string normalizedWorkspacePath = Path.GetFullPath(workspaceDir).TrimEnd(Path.DirectorySeparatorChar);

                // Query WMI for msmdsrv.exe processes
                string query = "SELECT ProcessId, ParentProcessId, CommandLine FROM Win32_Process WHERE Name = 'msmdsrv.exe'";
                using (var searcher = new ManagementObjectSearcher(query))
                using (var collection = searcher.Get())
                {
                    foreach (var process in collection)
                    {
                        string commandLine = process["CommandLine"]?.ToString();
                        if (string.IsNullOrEmpty(commandLine)) continue;

                        // Check if this msmdsrv instance is running from our workspace
                        if (commandLine.IndexOf(normalizedWorkspacePath, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            int processId = Convert.ToInt32(process["ProcessId"]);
                            int parentProcessId = Convert.ToInt32(process["ParentProcessId"]);
                            string friendlyName = null;

                            try
                            {
                                var parentProcess = Process.GetProcessById(parentProcessId);
                                if (parentProcess.ProcessName.Equals("PBIDesktop", StringComparison.OrdinalIgnoreCase))
                                {
                                    string title = parentProcess.MainWindowTitle;
                                    // Title format is usually "Filename - Power BI Desktop"
                                    int separatorIndex = title.LastIndexOf(" - Power BI Desktop");
                                    if (separatorIndex > 0)
                                    {
                                        friendlyName = title.Substring(0, separatorIndex);
                                    }
                                    else
                                    {
                                        friendlyName = title;
                                    }
                                }
                            }
                            catch
                            {
                                // Parent process might have exited or we can't access it
                            }

                            return (processId, parentProcessId, friendlyName);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting process info: {ex.Message}");
            }

            return (0, 0, null);
        }

        private string ReadPortFile(string portFile)
        {
            // Try UTF-16 LE (Little Endian) first - Power BI's encoding
            try
            {
                var content = File.ReadAllText(portFile, Encoding.Unicode);
                var trimmed = content.Trim('\0', ' ', '\r', '\n', '\t');

                if (!string.IsNullOrEmpty(trimmed) && trimmed.All(char.IsDigit))
                {
                    return trimmed;
                }
            }
            catch { }

            // Fallback to UTF-8
            try
            {
                var content = File.ReadAllText(portFile, Encoding.UTF8);
                return content.Trim('\0', ' ', '\r', '\n', '\t');
            }
            catch { }

            // Last resort - default encoding
            try
            {
                var content = File.ReadAllText(portFile);
                return content.Trim('\0', ' ', '\r', '\n', '\t');
            }
            catch
            {
                return null;
            }
        }

        private string GetDatabaseName(int port)
        {
            try
            {
                string connectionString = $"Data Source=localhost:{port};";

                using (var connection = new AdomdConnection(connectionString))
                {
                    connection.Open();

                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT [CATALOG_NAME] FROM $SYSTEM.DBSCHEMA_CATALOGS";

                        using (var reader = cmd.ExecuteReader())
                        {
                            if (reader.Read())
                            {
                                return reader.GetString(0);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting database name for port {port}: {ex.Message}");
            }

            return null;
        }

        private string GetFriendlyNameFallback(string workspaceDir)
        {
            var dirName = Path.GetFileName(workspaceDir);

            // Extract just the first part of the workspace ID for display
            if (dirName.Length > 20)
            {
                return $"Workspace-{dirName.Substring(0, 8)}";
            }

            return $"Workspace-{dirName}";
        }

        public bool IsWorkspacePathValid()
        {
            return Directory.Exists(_workspacesPath);
        }
    }
}