using QuakeServerManager.Models;
using QuakeServerManager.Services.Ssh;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace QuakeServerManager.Services
{
    public class SshService
    {
        private readonly ConnectionService _connectionService;
        private readonly ServerControlService _serverControlService;
        private readonly ImportService _importService;
        private readonly FileTransferService _fileTransferService;
        private readonly DockerDeploymentService _dockerDeploymentService;

        public event Action<string, LogLevel>? LogMessage;

        public event Func<string, bool>? HostKeyReceived;

        public SshService()
        {
            _connectionService = new ConnectionService();
            _fileTransferService = new FileTransferService();
            _dockerDeploymentService = new DockerDeploymentService(_connectionService, _fileTransferService);
            _serverControlService = new ServerControlService(_connectionService);
            _importService = new ImportService(_connectionService);

            _connectionService.LogMessage += OnLogMessage;
            _serverControlService.LogMessage += OnLogMessage;
            _importService.LogMessage += OnLogMessage;
            _fileTransferService.LogMessage += OnLogMessage;
            _dockerDeploymentService.LogMessage += OnLogMessage;

            _connectionService.HostKeyReceived += (fingerprint) => HostKeyReceived?.Invoke(fingerprint) ?? false;
        }

        private void OnLogMessage(string message, LogLevel level)
        {
            LogMessage?.Invoke(message, level);
        }

        public Task<bool> TestConnectionAsync(VpsConnection connection) => _connectionService.TestConnectionAsync(connection);

        // Docker deployment method
        public Task<bool> DeployDockerServerAsync(
            VpsConnection connection,
            List<ServerInstance> instances,
            string pak0Path,
            string cpmaPath,
            string serverExecutablePath,
            string mapsPath = "",
            List<string>? customMaps = null,
            Action<double, string>? progressCallback = null)
            => _dockerDeploymentService.DeployAsync(connection, instances, pak0Path, cpmaPath, serverExecutablePath, mapsPath, customMaps, progressCallback);

        public Task<bool> StartServerAsync(VpsConnection connection, string instanceName) => _serverControlService.StartServerAsync(connection, instanceName);
        public Task<bool> StopServerAsync(VpsConnection connection, string instanceName) => _serverControlService.StopServerAsync(connection, instanceName);
        public Task<bool> RestartServerAsync(VpsConnection connection, string instanceName) => _serverControlService.RestartServerAsync(connection, instanceName);

        public Task<bool> StartAllServersAsync(VpsConnection connection, List<string> instanceNames) => _serverControlService.StartAllServersAsync(connection, instanceNames);
        public Task<bool> StopAllServersAsync(VpsConnection connection, List<string> instanceNames) => _serverControlService.StopAllServersAsync(connection, instanceNames);
        public Task<bool> RestartAllServersAsync(VpsConnection connection, List<string> instanceNames) => _serverControlService.RestartAllServersAsync(connection, instanceNames);

        public Task<bool> CheckForExistingCpmaInstallationAsync(VpsConnection connection) => _importService.CheckForExistingCpmaInstallationAsync(connection);
        public Task<List<ServerInstance>> ImportExistingServerConfigurationsAsync(VpsConnection connection) => _importService.ImportExistingServerConfigurationsAsync(connection);
        public Task<List<Class>> ImportClassConfigurationsAsync(VpsConnection connection) => _importService.ImportClassConfigurationsAsync(connection);

        public Task<List<string>> GetDeployedInstancesAsync(VpsConnection connection)
        {
            // TODO: Implement using docker ps command
            return Task.FromResult(new List<string>());
        }
    }
}
