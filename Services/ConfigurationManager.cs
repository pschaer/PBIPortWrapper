using System;
using System.IO;
using Newtonsoft.Json;
using PBIPortWrapper.Models;

namespace PBIPortWrapper.Services
{
    public class ConfigurationManager
    {
        private readonly string _configFilePath;
        private readonly string _logFilePath;
        private readonly string _appDataPath;

        public ConfigurationManager()
        {
            _appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PBIPortWrapper"
            );

            Directory.CreateDirectory(_appDataPath);
            _configFilePath = Path.Combine(_appDataPath, "config.json");
            _logFilePath = Path.Combine(_appDataPath, "log.txt");
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

        public string GetConfigFilePath() => _configFilePath;
        public string GetLogFilePath() => _logFilePath;
        public string GetAppDataPath() => _appDataPath;
    }
}