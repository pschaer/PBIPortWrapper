using System;
using System.Linq;
using PBIPortWrapper.Models;

namespace PBIPortWrapper.Services
{
    public class ConfigService
    {
        private readonly ConfigurationManager _configManager;
        
        public ProxyConfiguration Current { get; private set; }
        
        public event EventHandler ConfigurationChanged;

        public ConfigService(ConfigurationManager configManager)
        {
            _configManager = configManager;
        }

        public void Load()
        {
            Current = _configManager.LoadConfiguration();
            OnConfigurationChanged();
        }

        public void Save()
        {
            if (Current == null) return;
            _configManager.SaveConfiguration(Current);
            OnConfigurationChanged();
        }

        /// <summary>
        /// The single rule-lookup used by every read and write path. Matching is by
        /// exact model name; despite the field's name, ModelNamePattern has always
        /// been treated as an exact name by the grid and auto-connect.
        /// </summary>
        public PortMappingRule FindRule(string modelName)
        {
            if (Current == null || string.IsNullOrEmpty(modelName)) return null;
            return Current.PortMappings.FirstOrDefault(r => r.ModelNamePattern == modelName);
        }

        public void UpdateRule(string modelName, int fixedPort, bool autoConnect, bool allowNetwork)
        {
            if (Current == null) return;
            if (string.IsNullOrEmpty(modelName)) return;
            if (modelName.Equals("Untitled", StringComparison.OrdinalIgnoreCase)) return;

            var rule = FindRule(modelName);
            if (rule == null)
            {
                if (fixedPort <= 0) return; // Don't create invalid rules

                rule = new PortMappingRule
                {
                    ModelNamePattern = modelName,
                    FixedPort = fixedPort,
                    AutoConnect = autoConnect,
                    AllowNetworkAccess = allowNetwork
                };
                Current.PortMappings.Add(rule);
            }
            else
            {
                // If setting to 0, might mean delete? Or just disable?
                // The original logic kept the rule but updated values.
                // However, usually 0 fixed port implies invalid/removed in this app's context.
                // But let's stick to update behavior.
                rule.FixedPort = fixedPort;
                rule.AutoConnect = autoConnect;
                rule.AllowNetworkAccess = allowNetwork;
            }

            Save();
        }

        public void RemoveRule(string modelName)
        {
            if (Current == null) return;

            var rule = FindRule(modelName);
            if (rule != null)
            {
                Current.PortMappings.Remove(rule);
                Save();
            }
        }

        public void SetStableAlias(string modelName, string alias)
        {
            if (Current == null) return;
            if (string.IsNullOrEmpty(modelName)) return;
            // #9: rules match by file name; a rule saved under "Untitled" is
            // orphaned the moment the model is saved under its real name.
            if (modelName.Equals("Untitled", StringComparison.OrdinalIgnoreCase)) return;

            var rule = FindRule(modelName);
            if (rule == null)
            {
                rule = new PortMappingRule { ModelNamePattern = modelName };
                Current.PortMappings.Add(rule);
            }

            rule.StableAlias = alias;
            Save();
        }

        /// <summary>
        /// Persists a serve-session crash anchor (#57). Replaces any stale record
        /// for the same workspace so a re-serve after an unclean end can't leave two.
        /// </summary>
        public void AddServeRecoveryRecord(ServeRecoveryRecord record)
        {
            if (Current == null || record == null) return;
            if (string.IsNullOrEmpty(record.WorkspaceId)) return;

            Current.ServeRecoveryRecords.RemoveAll(r => r.WorkspaceId == record.WorkspaceId);
            Current.ServeRecoveryRecords.Add(record);
            Save();
        }

        public void RemoveServeRecoveryRecord(string workspaceId)
        {
            if (Current == null || string.IsNullOrEmpty(workspaceId)) return;

            if (Current.ServeRecoveryRecords.RemoveAll(r => r.WorkspaceId == workspaceId) > 0)
                Save();
        }

        /// <summary>
        /// Sets a model's on-detection policy (#85b). Keeps the legacy AutoConnect
        /// flag consistent so the forward path (AutoConnectService) and the serve
        /// path (auto-serve) never fight over the same port: Forward implies
        /// AutoConnect; serve and do-nothing policies clear it.
        /// </summary>
        public void SetOnDetection(string modelName, OnDetectionPolicy policy)
        {
            if (Current == null || string.IsNullOrEmpty(modelName)) return;
            if (modelName.Equals("Untitled", StringComparison.OrdinalIgnoreCase)) return;

            var rule = FindRule(modelName);
            if (rule == null)
            {
                rule = new PortMappingRule { ModelNamePattern = modelName };
                Current.PortMappings.Add(rule);
            }

            rule.OnDetection = policy;
            rule.AutoConnect = policy == OnDetectionPolicy.Forward;
            Save();
        }

        /// <summary>
        /// Sets a model's LAN exposure (advanced; same-user only - E1). Takes effect
        /// when the proxy next starts, so it is a config change, not a live rebind.
        /// </summary>
        public void SetNetwork(string modelName, bool allowNetwork)
        {
            if (Current == null || string.IsNullOrEmpty(modelName)) return;
            if (modelName.Equals("Untitled", StringComparison.OrdinalIgnoreCase)) return;

            var rule = FindRule(modelName);
            if (rule == null)
            {
                rule = new PortMappingRule { ModelNamePattern = modelName };
                Current.PortMappings.Add(rule);
            }

            if (rule.AllowNetworkAccess == allowNetwork) return;
            rule.AllowNetworkAccess = allowNetwork;
            Save();
        }

        public void SetMinimizeToTray(bool enabled)
        {
            if (Current == null) return;
            if (Current.MinimizeToTray == enabled) return;

            Current.MinimizeToTray = enabled;
            Save();
        }

        /// <summary>
        /// Toggles the auto-start-with-Windows setting (#87). Keeps the HKCU
        /// Run registry key in sync so the wrapper launches at login.
        /// </summary>
        public void SetStartWithWindows(bool enabled)
        {
            if (Current == null) return;
            if (Current.StartWithWindows == enabled) return;

            Current.StartWithWindows = enabled;
            if (enabled) StartupService.Register();
            else StartupService.Unregister();
            Save();
        }

        /// <summary>
        /// Reconciles the registry Run key with the persisted config (#87).
        /// Call once at startup to self-heal after an exe move or manual
        /// registry edit.
        /// </summary>
        public void ReconcileStartup()
        {
            if (Current == null) return;

            if (Current.StartWithWindows && !StartupService.IsRegistered())
                StartupService.Register();
            else if (!Current.StartWithWindows && StartupService.IsRegistered())
                StartupService.Unregister();
        }

        private void OnConfigurationChanged()
        {
            ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
