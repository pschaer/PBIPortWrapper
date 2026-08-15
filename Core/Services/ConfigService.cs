using System;
using System.Linq;
using PBIRelay.Models;

namespace PBIRelay.Services
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
        public ModelRule FindRule(string modelName)
        {
            if (Current == null || string.IsNullOrEmpty(modelName)) return null;
            return Current.Models.FirstOrDefault(r => r.ModelNamePattern == modelName);
        }

        public void RemoveRule(string modelName)
        {
            if (Current == null) return;

            var rule = FindRule(modelName);
            if (rule != null)
            {
                Current.Models.Remove(rule);
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
                rule = new ModelRule { ModelNamePattern = modelName };
                Current.Models.Add(rule);
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
        /// Sets a model's on-detection policy (#85b).
        ///
        /// The legacy <c>AutoConnect</c> flag is no longer kept in step: it meant
        /// "forward on detect", and forwarding is gone (#126). It survives only so
        /// that a pre-v1 config can still be migrated, and is cleared here so a rule
        /// the user edits stops carrying a stale claim.
        /// </summary>
        public void SetOnDetection(string modelName, OnDetectionPolicy policy)
        {
            if (Current == null || string.IsNullOrEmpty(modelName)) return;
            if (modelName.Equals("Untitled", StringComparison.OrdinalIgnoreCase)) return;

            var rule = FindRule(modelName);
            if (rule == null)
            {
                rule = new ModelRule { ModelNamePattern = modelName };
                Current.Models.Add(rule);
            }

            rule.OnDetection = policy;
            rule.AutoConnect = false;
            Save();
        }

        /// <summary>
        /// Whether this model refuses mutating Execute commands on the endpoint (#129).
        /// Granular like the other per-model setters, so the grid and the tray edit one
        /// value and cannot drift apart.
        /// </summary>
        public void SetReadOnly(string modelName, bool readOnly)
        {
            if (Current == null || string.IsNullOrEmpty(modelName)) return;
            if (modelName.Equals("Untitled", StringComparison.OrdinalIgnoreCase)) return;

            var rule = FindRule(modelName);
            if (rule == null)
            {
                rule = new ModelRule { ModelNamePattern = modelName };
                Current.Models.Add(rule);
            }

            rule.ReadOnly = readOnly;
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
        /// The XMLA endpoint's settings (#125). Granular setters, like the per-model
        /// ones: each writes one field and saves, and XmlaEndpointCoordinator brings
        /// the running listener into line off ConfigurationChanged. Surfaces never
        /// start or stop the endpoint themselves.
        /// </summary>
        public void SetEndpointEnabled(bool enabled)
        {
            if (Current?.HttpBridge == null) return;
            if (Current.HttpBridge.Enabled == enabled) return;

            Current.HttpBridge.Enabled = enabled;
            Save();
        }

        /// <summary>
        /// Sets the port the endpoint listens on. Rejects anything outside the
        /// unprivileged range rather than saving a value that can only fail to bind.
        /// </summary>
        public (bool IsValid, string ErrorMessage) SetEndpointPort(int port)
        {
            if (Current?.HttpBridge == null) return (false, "No configuration loaded.");

            if (port < MinEndpointPort || port > MaxEndpointPort)
                return (false, $"Port must be between {MinEndpointPort} and {MaxEndpointPort}.");

            if (Current.HttpBridge.Port == port) return (true, string.Empty);

            Current.HttpBridge.Port = port;
            Save();
            return (true, string.Empty);
        }

        public const int MinEndpointPort = 1024;
        public const int MaxEndpointPort = 65535;

        public void SetEndpointAuthMode(BridgeAuthMode mode)
        {
            if (Current?.HttpBridge == null) return;
            if (Current.HttpBridge.AuthMode == mode) return;

            Current.HttpBridge.AuthMode = mode;
            Save();
        }

        /// <summary>
        /// Sets the host name published in connection URLs. Empty means detect it.
        /// Display only — it never restarts the listener.
        /// </summary>
        public void SetEndpointHostname(string hostname)
        {
            if (Current?.HttpBridge == null) return;

            string value = string.IsNullOrWhiteSpace(hostname) ? string.Empty : hostname.Trim();
            if (string.Equals(Current.HttpBridge.Hostname ?? string.Empty, value, StringComparison.Ordinal)) return;

            Current.HttpBridge.Hostname = value;
            Save();
        }

        /// <summary>
        /// Turns encryption on or off (#132).
        ///
        /// Refuses to turn it ON while the configured certificate does not resolve, and
        /// says why. The endpoint would otherwise fail to start on the next apply, which
        /// is a worse way to learn the path was mistyped than being told here.
        /// </summary>
        public (bool Ok, string Message) SetUseHttps(bool useHttps)
        {
            if (Current?.HttpBridge == null) return (false, "No configuration loaded.");

            if (useHttps)
            {
                CertificateResolution resolved = CertificateResolver.Resolve(
                    Current.HttpBridge.CertificatePath,
                    Current.HttpBridge.CertificateThumbprint,
                    Current.HttpBridge.CertificateKeyPath);

                if (!resolved.Ok) return (false, resolved.Problem);
                resolved.Certificate.Dispose();
            }

            if (Current.HttpBridge.UseHttps == useHttps) return (true, string.Empty);

            Current.HttpBridge.UseHttps = useHttps;
            Save();
            return (true, string.Empty);
        }

        /// <summary>
        /// Points the endpoint at a certificate, from one source at a time.
        ///
        /// The other sources are CLEARED, which is the whole reason this is one call
        /// rather than three setters. <see cref="CertificateResolver"/> checks the
        /// thumbprint first and the file second, so a thumbprint left behind from an
        /// earlier attempt would quietly win over the PEM pair someone had just chosen -
        /// serving a certificate the settings appear to have replaced.
        /// </summary>
        public void SetCertificate(
            CertificateSource source, string path = null, string keyPath = null, string thumbprint = null)
        {
            if (Current?.HttpBridge == null) return;

            HttpBridgeConfig bridge = Current.HttpBridge;
            string newPath = string.Empty, newKey = string.Empty, newThumb = string.Empty;

            switch (source)
            {
                case CertificateSource.WindowsStore:
                    newThumb = Clean(thumbprint);
                    break;
                case CertificateSource.PfxFile:
                    newPath = Clean(path);
                    break;
                default:
                    newPath = Clean(path);
                    newKey = Clean(keyPath);
                    break;
            }

            if (bridge.CertificatePath == newPath &&
                bridge.CertificateKeyPath == newKey &&
                bridge.CertificateThumbprint == newThumb) return;

            bridge.CertificatePath = newPath;
            bridge.CertificateKeyPath = newKey;
            bridge.CertificateThumbprint = newThumb;
            Save();
        }

        /// <summary>Quotes come free when a path is pasted from Explorer's Copy as path.</summary>
        private static string Clean(string value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().Trim('"');

        /// <summary>
        /// Toggles the auto-start-with-Windows setting (#87). Keeps the HKCU
        /// Run registry key in sync so PBIRelay launches at login.
        /// </summary>
        /// <summary>Whether each request is recorded in access.csv (#128).</summary>
        public void SetAccessLog(bool enabled)
        {
            if (Current?.HttpBridge == null) return;
            Current.HttpBridge.AccessLog = enabled;
            Save();
        }

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
