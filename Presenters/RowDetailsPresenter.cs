using System;
using System.Collections.Generic;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    public class RowDetailsPresenter
    {
        private PowerBIInstance _instance;
        private readonly ProxyManager _proxyManager;
        private readonly ConfigService _configService;
        private readonly ServeSessionService _serveSessions;

        public RowDetailsPresenter(
            PowerBIInstance instance,
            ProxyManager proxyManager,
            ConfigService configService,
            ServeSessionService serveSessions)
        {
            _instance = instance;
            _proxyManager = proxyManager;
            _configService = configService;
            _serveSessions = serveSessions;
        }

        /// <summary>
        /// Instances are recreated on every scan; without this the panel keeps a
        /// stale snapshot and misses live changes (DB name after a serve rename).
        /// </summary>
        public void UpdateInstance(PowerBIInstance instance)
        {
            if (instance != null) _instance = instance;
        }

        public DetailsDisplayData GetDisplayData()
        {
            string fullTitle = $"PBI Desktop - {_instance.FileName} - {_instance.Port}";
            // FilePath is the AS workspace dir, not a .pbix — label it honestly (#59).
            string tooltip = $"Workspace: {_instance.FilePath}";

            var rule = _configService.FindRule(_instance.FileName);

            int fixedPort = rule?.FixedPort ?? 0;
            string connString = fixedPort > 0 ? $"localhost:{fixedPort}" : string.Empty;
            string alias = rule?.StableAlias ?? string.Empty;

            return new DetailsDisplayData
            {
                ModelName = _instance.FileName,
                PbiPort = _instance.Port,
                FixedPort = fixedPort,
                ConnectionString = connString,
                DatabaseOriginalName = _instance.DatabaseName,
                DatabaseAlias = alias,
                IsServing = _serveSessions.FindSession(_instance.WorkspaceId) != null,
                FullTitle = fullTitle,
                TooltipText = tooltip
            };
        }

        public IReadOnlyList<ConnectionInfo> GetActiveConnections(int fixedPort)
        {
            if (fixedPort <= 0) return new List<ConnectionInfo>();
            return _proxyManager.GetConnectionDetails(fixedPort);
        }

        public void SaveDatabaseAlias(string newName)
        {
            // Single-writer rule: all config mutations go through ConfigService so the
            // grid's cached Current and this panel can never clobber each other (#62).
            _configService.SetStableAlias(_instance.FileName, newName);
        }
    }
}
