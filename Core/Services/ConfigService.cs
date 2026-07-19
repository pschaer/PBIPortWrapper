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

        public void SetMinimizeToTray(bool enabled)
        {
            if (Current == null) return;
            if (Current.MinimizeToTray == enabled) return;

            Current.MinimizeToTray = enabled;
            Save();
        }

        private void OnConfigurationChanged()
        {
            ConfigurationChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
