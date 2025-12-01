using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using PBIPortWrapper.Models;
using PBIPortWrapper.Services;

namespace PBIPortWrapper.Presenters
{
    // FILE SIZE: Current=~100 lines
    // MAX 300 lines - enforced in code review
    public class ProxyPresenter
    {
        private readonly ProxyManager _proxyManager;
        private readonly ValidationService _validationService;
        private readonly Action<string> _logCallback;

        public ProxyPresenter(ProxyManager proxyManager, ValidationService validationService, Action<string> logCallback)
        {
            _proxyManager = proxyManager;
            _validationService = validationService;
            _logCallback = logCallback;
        }

        public void ProcessAutoConnect(List<PowerBIInstance> instances, DataGridView grid)
        {
            foreach (DataGridViewRow row in grid.Rows)
            {
                bool isAuto = Convert.ToBoolean(row.Cells["colAuto"].Value);
                string status = row.Cells["colStatus"].Value?.ToString();
                
                if (isAuto && status == "Ready")
                {
                    // Find instance
                    int? pid = row.Tag as int?;
                    var instance = instances.FirstOrDefault(i => i.ProcessId == pid);
                    
                    if (instance != null)
                    {
                        int fixedPort = 0;
                        if (row.Cells["colFixedPort"].Value != null)
                            int.TryParse(row.Cells["colFixedPort"].Value.ToString(), out fixedPort);

                        if (fixedPort > 0)
                            StartProxySafe(instance, fixedPort, Convert.ToBoolean(row.Cells["colNetwork"].Value));
                    }
                }
            }
        }

        public async Task StartProxyAsync(PowerBIInstance instance, int fixedPort, bool allowNetwork)
        {
            await StartProxySafe(instance, fixedPort, allowNetwork);
        }

        private async Task StartProxySafe(PowerBIInstance instance, int fixedPort, bool allowNetwork)
        {
            try
            {
                if (_proxyManager.IsRunning(fixedPort)) return;

                if (fixedPort < 1024 || fixedPort > 65535)
                {
                    _logCallback($"Invalid port {fixedPort} for {instance.FileName}");
                    return;
                }

                await _proxyManager.StartProxyAsync(fixedPort, instance.Port, allowNetwork, instance.FileName);
            }
            catch (Exception ex)
            {
                _logCallback($"Failed to start {instance.FileName}: {ex.Message}");
            }
        }

        public void StopProxy(int fixedPort)
        {
            _proxyManager.StopProxy(fixedPort);
        }
        
        public void StopAll()
        {
            _proxyManager.StopAll();
        }
    }
}
