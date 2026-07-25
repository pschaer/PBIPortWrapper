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

        public PowerBIDetector(string workspacesPath = null)
        {
            _workspacesPath = workspacesPath ?? Path.Combine(
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
                                // Liveness check: leftover workspace dirs from crashed
                                // Desktop sessions keep stale port files (#50). A dead
                                // port must not surface as an instance nor get proxied.
                                if (!IsPortAlive(port))
                                {
                                    System.Diagnostics.Debug.WriteLine($"Skipping workspace {workspaceDir}: port {port} not answering");
                                    continue;
                                }

                                string databaseName = GetDatabaseName(port);
                                var (processId, parentProcessId, friendlyName) = GetProcessInfo(port);

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

        /// <summary>
        /// Resolves the engine PID, its Power BI Desktop parent PID, and the model
        /// name for a workspace. Matches the workspace's AS <paramref name="port"/> to
        /// the owning msmdsrv process; this works even when the engine runs elevated
        /// (e.g. Desktop launched from an elevated installer), where its command line
        /// is unreadable to a non-elevated wrapper and path matching would fail (#94).
        /// </summary>
        private (int processId, int parentProcessId, string friendlyName) GetProcessInfo(int port)
        {
            try
            {
                int msmdsrvPid = TcpPortOwner.GetOwningProcessId(port);
                if (msmdsrvPid <= 0) return (0, 0, null);

                int parentProcessId = GetParentProcessId(msmdsrvPid);
                string friendlyName = null;

                if (parentProcessId > 0)
                {
                    try
                    {
                        var parent = Process.GetProcessById(parentProcessId);
                        if (parent.ProcessName.Equals("PBIDesktop", StringComparison.OrdinalIgnoreCase))
                            friendlyName = WindowTitleParser.ModelName(parent.MainWindowTitle);
                    }
                    catch
                    {
                        // Parent may have exited or be inaccessible; fall back to a generic name.
                    }
                }

                return (msmdsrvPid, parentProcessId, friendlyName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting process info: {ex.Message}");
                return (0, 0, null);
            }
        }

        private static int GetParentProcessId(int processId)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {processId}");
                foreach (var mo in searcher.Get())
                    return Convert.ToInt32(mo["ParentProcessId"]);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting parent of {processId}: {ex.Message}");
            }
            return 0;
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
                // Bounded timeout: detection must not hang on a port that accepts
                // TCP but answers slowly (e.g. an instance still loading its model).
                string connectionString = $"Data Source=localhost:{port};Connect Timeout=5;";

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

            // Workspace dirs are named "AnalysisServicesWorkspace<id>", so the first
            // characters are identical for every model - use the distinguishing id
            // suffix instead, or the name shrinks to a useless "Workspace-Analysis".
            const string prefix = "AnalysisServicesWorkspace";
            if (dirName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && dirName.Length > prefix.Length)
                return $"Workspace-{dirName.Substring(prefix.Length)}";

            return $"Workspace-{(dirName.Length > 8 ? dirName.Substring(0, 8) : dirName)}";
        }

        public bool IsWorkspacePathValid()
        {
            return Directory.Exists(_workspacesPath);
        }

        public static bool IsPortAlive(int port, int timeoutMs = 250)
        {
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                var connectTask = client.ConnectAsync(System.Net.IPAddress.Loopback, port);
                return connectTask.Wait(timeoutMs) && client.Connected;
            }
            catch
            {
                return false;
            }
        }
    }
}