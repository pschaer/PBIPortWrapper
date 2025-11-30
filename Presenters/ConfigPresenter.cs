using System;
using System.Linq;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    // FILE SIZE: Current=~100 lines
    // MAX 300 lines - enforced in code review
    public class ConfigPresenter
    {
        private readonly ConfigurationManager _configManager;
        private readonly Action<string> _logCallback;

        public ConfigPresenter(ConfigurationManager configManager, Action<string> logCallback)
        {
            _configManager = configManager;
            _logCallback = logCallback;
        }

        public ProxyConfiguration LoadConfiguration()
        {
            return _configManager.LoadConfiguration();
        }

        public void SaveConfiguration(ProxyConfiguration config)
        {
            _configManager.SaveConfiguration(config);
        }

        public void UpdateAndSaveRule(DataGridViewRow row, ProxyConfiguration config)
        {
            string modelName = row.Cells["colModelName"].Value?.ToString();
            
            if (string.IsNullOrEmpty(modelName)) return;
            
            // Prevent saving "Untitled" to config
            if (modelName.Equals("Untitled", StringComparison.OrdinalIgnoreCase)) return;

            int fixedPort = 0;
            if (row.Cells["colFixedPort"].Value != null)
            {
                int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out fixedPort);
            }

            if (fixedPort == 0) return;

            bool autoConnect = Convert.ToBoolean(row.Cells["colAuto"].Value);
            bool allowNetwork = Convert.ToBoolean(row.Cells["colNetwork"].Value);

            var rule = config.PortMappings.FirstOrDefault(r => r.ModelNamePattern == modelName);
            if (rule == null)
            {
                rule = new PortMappingRule
                {
                    ModelNamePattern = modelName,
                    FixedPort = fixedPort,
                    AutoConnect = autoConnect,
                    AllowNetworkAccess = allowNetwork
                };
                config.PortMappings.Add(rule);
            }
            else
            {
                rule.FixedPort = fixedPort;
                rule.AutoConnect = autoConnect;
                rule.AllowNetworkAccess = allowNetwork;
            }

            SaveConfiguration(config);
        }

        // Returns true if deleted, false if not (e.g. cancelled or invalid)
        // The View is responsible for confirmation dialogs before calling this if needed, 
        // or we can inject a dialog service. For now, we'll assume the View handles the "Are you sure?" 
        // and checks for running status before calling, OR we return a specific result.
        // Actually, to fully encapsulate, let's pass the running check responsibility to the caller (MainForm) 
        // or pass the ProxyManager here. 
        // The original code checked ProxyManager.IsRunning inside DeleteConfiguration.
        // Let's keep it simple: The View (MainForm) has access to ProxyManager and can check IsRunning.
        // The View handles the confirmation.
        // This method just does the deletion.
        public void DeleteConfiguration(string modelName, ProxyConfiguration config)
        {
            var rule = config.PortMappings.FirstOrDefault(r => r.ModelNamePattern == modelName);

            if (rule != null)
            {
                config.PortMappings.Remove(rule);
                SaveConfiguration(config);
            }
        }
    }
}
