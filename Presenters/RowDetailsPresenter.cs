using System;
using System.Collections.Generic;
using PBIRelay.Models;
using PBIRelay.Services;

namespace PBIRelay.Presenters
{
    public class RowDetailsPresenter
    {
        private PowerBIInstance _instance;
        private readonly ConfigService _configService;
        private readonly ServeSessionService _serveSessions;
        private readonly Func<string, string> _endpointUrlForAlias;

        public RowDetailsPresenter(
            PowerBIInstance instance,
            ConfigService configService,
            ServeSessionService serveSessions,
            Func<string, string> endpointUrlForAlias = null)
        {
            _instance = instance;
            _configService = configService;
            _serveSessions = serveSessions;
            _endpointUrlForAlias = endpointUrlForAlias ?? (_ => string.Empty);
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
            string alias = rule?.StableAlias ?? string.Empty;

            // The model's live address, or empty when it is not currently reachable.
            string connString = _endpointUrlForAlias(alias);

            return new DetailsDisplayData
            {
                ModelName = _instance.FileName,
                PbiPort = _instance.Port,
                ConnectionString = connString,
                DatabaseOriginalName = _instance.DatabaseName,
                DatabaseAlias = alias,
                IsServing = _serveSessions.FindSession(_instance.WorkspaceId) != null,
                FullTitle = fullTitle,
                WorkspacePath = _instance.FilePath,
                TooltipText = tooltip
            };
        }
    }
}
