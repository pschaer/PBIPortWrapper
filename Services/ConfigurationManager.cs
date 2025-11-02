using System;
using System.IO;
using Newtonsoft.Json;
using PowerBIPortWrapper.Models;

namespace PowerBIPortWrapper.Services
{
    public class ConfigurationManager
    {
        private readonly string _configFilePath;

        public ConfigurationManager()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PowerBIPortWrapper"
            );

            Directory.CreateDirectory(appDataPath);
            _configFilePath = Path.Combine(appDataPath, "config.json");
        }

        public ProxyConfiguration LoadConfiguration()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = File.ReadAllText(_configFilePath);
                    return JsonConvert.DeserializeObject<ProxyConfiguration>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading configuration: {ex.Message}");
            }

            // Return default configuration
            return new ProxyConfiguration();
        }

        public void SaveConfiguration(ProxyConfiguration config)
        {
            try
            {
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving configuration: {ex.Message}");
                throw;
            }
        }

        public string GetConfigFilePath()
        {
            return _configFilePath;
        }
    }
}
