using QuakeServerManager.Models;
using Renci.SshNet;
using System;
using System.IO;
using System.Threading.Tasks;

namespace QuakeServerManager.Services.Ssh
{
    public class ConnectionService
    {
        public event Action<string, LogLevel>? LogMessage;
        public event Func<string, bool>? HostKeyReceived;

        private void Log(string message, LogLevel level = LogLevel.Info)
        {
            LogMessage?.Invoke(message, level);
        }

        public ConnectionInfo GetConnectionInfo(VpsConnection connection)
        {
            var authMethods = connection.AuthMethod == AuthMethod.Password
                ? new AuthenticationMethod[] { new PasswordAuthenticationMethod(connection.Username, connection.Password) }
                : new AuthenticationMethod[] { new PrivateKeyAuthenticationMethod(connection.Username, new PrivateKeyFile(connection.PrivateKeyPath)) };

            return new ConnectionInfo(connection.Ip, connection.Port, connection.Username, authMethods);
        }

        public async Task<bool> TestConnectionAsync(VpsConnection connection)
        {
            try
            {
                Log($"Attempting SSH connection to {connection.Ip}:{connection.Port} as {connection.Username}...");
                
                var info = GetConnectionInfo(connection);
                using var client = new SshClient(info);
                client.HostKeyReceived += (sender, e) =>
                {
                    e.CanTrust = HostKeyReceived?.Invoke(Convert.ToBase64String(e.HostKey)) ?? false;
                };
                
                Log("Connecting to SSH server...");
                await Task.Run(() => client.Connect());
                
                if (client.IsConnected)
                {
                    Log("SSH connection established successfully!", LogLevel.Success);
                    
                    // Test a simple command
                    var cmd = await client.ExecuteCommandAsync("echo 'SSH connection test successful'");
                    Log($"Test command result: {cmd.Result}");
                    
                    client.Disconnect();
                    return true;
                }
                else
                {
                    Log("Failed to establish SSH connection.", LogLevel.Error);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log($"SSH connection failed: {ex.Message}", LogLevel.Error);
                return false;
            }
        }
    }
}
