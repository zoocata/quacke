using Renci.SshNet;
using System;
using System.Threading.Tasks;

namespace QuakeServerManager.Services.Ssh
{
    public static class SshClientExtensions
    {
        public static Task<SshCommand> ExecuteCommandAsync(this SshClient client, string commandText)
        {
            var tcs = new TaskCompletionSource<SshCommand>();
            var command = client.CreateCommand(commandText);
            command.BeginExecute(ar =>
            {
                try
                {
                    command.EndExecute(ar);
                    tcs.SetResult(command);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            return tcs.Task;
        }
    }
}
