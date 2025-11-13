using QuakeServerManager.Models;
using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace QuakeServerManager.Services.Ssh
{
    public class ServerControlService
    {
        private readonly ConnectionService _connectionService;

        public event Action<string, LogLevel>? LogMessage;

        public ServerControlService(ConnectionService connectionService)
        {
            _connectionService = connectionService;
        }

        private void Log(string message, LogLevel level = LogLevel.Info)
        {
            LogMessage?.Invoke(message, level);
        }

        public async Task<bool> StartServerAsync(VpsConnection connection, string instanceName)
        {
            return await ExecuteControlScript(connection, instanceName, "start.sh", "Starting");
        }

        public async Task<bool> StopServerAsync(VpsConnection connection, string instanceName)
        {
            return await ExecuteControlScript(connection, instanceName, "stop.sh", "Stopping");
        }

        public async Task<bool> RestartServerAsync(VpsConnection connection, string instanceName)
        {
            return await ExecuteControlScript(connection, instanceName, "restart.sh", "Restarting");
        }

        public async Task<bool> StartAllServersAsync(VpsConnection connection, List<string> instanceNames)
        {
            var tasks = instanceNames.Select(instanceName => StartServerAsync(connection, instanceName));
            var results = await Task.WhenAll(tasks);
            return results.All(r => r);
        }

        public async Task<bool> StopAllServersAsync(VpsConnection connection, List<string> instanceNames)
        {
            var tasks = instanceNames.Select(instanceName => StopServerAsync(connection, instanceName));
            var results = await Task.WhenAll(tasks);
            return results.All(r => r);
        }

        public async Task<bool> RestartAllServersAsync(VpsConnection connection, List<string> instanceNames)
        {
            var tasks = instanceNames.Select(instanceName => RestartServerAsync(connection, instanceName));
            var results = await Task.WhenAll(tasks);
            return results.All(r => r);
        }

        private async Task<bool> ExecuteControlScript(VpsConnection connection, string instanceName, string scriptName, string action)
        {
            try
            {
                Log($"{action} server {instanceName}...");
                var info = _connectionService.GetConnectionInfo(connection);
                using var client = new SshClient(info);
                client.Connect();

                if (!client.IsConnected)
                {
                    Log($"Failed to connect for {action.ToLower()} server.", LogLevel.Error);
                    return false;
                }

                // Use Docker commands directly instead of control scripts
                string dockerCommand = scriptName switch
                {
                    "start.sh" => $"docker start {instanceName}",
                    "stop.sh" => $"docker stop {instanceName}",
                    "restart.sh" => $"docker restart {instanceName}",
                    _ => throw new ArgumentException($"Unknown script: {scriptName}")
                };

                var cmd = await client.ExecuteCommandAsync(dockerCommand);

                if (cmd.ExitStatus == 0)
                {
                    Log($"✓ Server {instanceName} {action.ToLower()}ed successfully.", LogLevel.Success);
                    return true;
                }
                else
                {
                    Log($"✗ Failed to {action.ToLower()} server {instanceName}. Error: {cmd.Error}", LogLevel.Error);
                    return false;
                }
            }
            catch(Exception ex)
            {
                Log($"✗ An error occurred while {action.ToLower()}ing server {instanceName}: {ex.Message}", LogLevel.Error);
                return false;
            }
        }
    }
}
