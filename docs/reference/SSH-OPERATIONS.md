# SSH-OPERATIONS.md

**Purpose:** Document SSH architecture patterns to avoid re-reading 500+ lines of SSH services each Claude Code session.

**Token savings:** ~15K tokens per session

---

## Architecture Overview

**Pattern:** Facade + Specialized Services

```
SshService.cs (Facade - 77 lines)
├── ConnectionService.cs (69 lines) - Connection lifecycle
├── FileTransferService.cs (141 lines) - SFTP operations
├── ServerControlService.cs (107 lines) - Docker container control
├── ImportService.cs - Import existing configs
└── DockerDeploymentService.cs (507 lines) - Complex deployment orchestration
```

**Key principle:** `SshService` is a coordinator. All SSH logic lives in specialized services.

---

## 1. Connection Management (ConnectionService)

### Pattern: ConnectionInfo Builder

```csharp
public ConnectionInfo GetConnectionInfo(VpsConnection connection)
{
    var authMethods = connection.AuthMethod == AuthMethod.Password
        ? new AuthenticationMethod[] { new PasswordAuthenticationMethod(connection.Username, connection.Password) }
        : new AuthenticationMethod[] { new PrivateKeyAuthenticationMethod(connection.Username, new PrivateKeyFile(connection.PrivateKeyPath)) };

    return new ConnectionInfo(connection.Ip, connection.Port, connection.Username, authMethods);
}
```

**Usage:** Every service that needs SSH connection calls this to get ConnectionInfo.

### Pattern: Host Key Validation

```csharp
client.HostKeyReceived += (sender, e) =>
{
    e.CanTrust = HostKeyReceived?.Invoke(Convert.ToBase64String(e.HostKey)) ?? false;
};
```

**Critical:** Host key validation is event-driven. UI subscribes to `HostKeyReceived` event.

### Pattern: Connection Testing

```csharp
public async Task<bool> TestConnectionAsync(VpsConnection connection)
{
    var info = GetConnectionInfo(connection);
    using var client = new SshClient(info);
    await Task.Run(() => client.Connect());
    
    if (client.IsConnected)
    {
        // Test with simple echo command
        var cmd = await client.ExecuteCommandAsync("echo 'SSH connection test successful'");
        client.Disconnect();
        return true;
    }
    return false;
}
```

**Usage:** Always test connections before attempting deployment.

---

## 2. File Transfer (FileTransferService)

### Pattern: Single File Upload

```csharp
public async Task UploadFileAsync(SftpClient sftpClient, string localPath, string remotePath)
{
    await Task.Run(() =>
    {
        using var fileStream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        sftpClient.UploadFile(fileStream, remotePath);
    });
}
```

**Critical:** Wrap in `Task.Run()` to avoid blocking UI. Handle `UnauthorizedAccessException` gracefully.

### Pattern: Text Upload (for configs)

```csharp
public async Task UploadTextAsync(SftpClient sftpClient, string content, string remotePath)
{
    await Task.Run(() =>
    {
        sftpClient.WriteAllText(remotePath, content, Encoding.UTF8);
    });
}
```

**Usage:** For generated configs like server.cfg, class files.

### Pattern: Directory Upload with Progress

```csharp
public async Task UploadDirectoryAsync(
    SftpClient sftpClient, 
    string localPath, 
    string remotePath, 
    Action<double, string>? progressCallback = null,
    double startProgress = 0.0, 
    double endProgress = 100.0)
{
    // Step 1: Collect all unique directories
    var allRemoteDirs = new HashSet<string>();
    // ... build directory list
    
    // Step 2: Create directories sequentially (sorted by length for parent-first)
    foreach (var dir in allRemoteDirs.OrderBy(d => d.Length))
    {
        CreateDirectoryRecursive(sftpClient, dir);
    }
    
    // Step 3: Upload files in parallel (max 10 concurrent)
    var semaphore = new SemaphoreSlim(10);
    // ... parallel upload with progress tracking
}
```

**Critical patterns:**
1. **Sequential directory creation** - Avoids race conditions
2. **Parent-first ordering** - Sort by path length ensures parents exist before children
3. **Parallel file uploads** - SemaphoreSlim(10) limits concurrency
4. **Progress normalization** - `startProgress` to `endProgress` for nested operations
5. **Skip access-denied files** - Log warning, continue upload

### Pattern: Recursive Directory Creation

```csharp
private void CreateDirectoryRecursive(SftpClient client, string path)
{
    string current = path.StartsWith("/") ? "/" : "";
    var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
    
    foreach (var part in parts)
    {
        current = Path.Combine(current, part).Replace('\\', '/');
        if (!client.Exists(current))
        {
            client.CreateDirectory(current);
        }
    }
}
```

**Usage:** Always check `Exists()` before creating to avoid errors.

---

## 3. Server Control (ServerControlService)

### Pattern: Docker Container Control

```csharp
private async Task<bool> ExecuteControlScript(VpsConnection connection, string instanceName, string scriptName, string action)
{
    var info = _connectionService.GetConnectionInfo(connection);
    using var client = new SshClient(info);
    client.Connect();
    
    string dockerCommand = scriptName switch
    {
        "start.sh" => $"docker start {instanceName}",
        "stop.sh" => $"docker stop {instanceName}",
        "restart.sh" => $"docker restart {instanceName}",
        _ => throw new ArgumentException($"Unknown script: {scriptName}")
    };
    
    var cmd = await client.ExecuteCommandAsync(dockerCommand);
    return cmd.ExitStatus == 0;
}
```

**Critical:** Uses Docker CLI directly instead of shell scripts. Instance name = Docker container name.

### Pattern: Batch Operations

```csharp
public async Task<bool> StartAllServersAsync(VpsConnection connection, List<string> instanceNames)
{
    var tasks = instanceNames.Select(instanceName => StartServerAsync(connection, instanceName));
    var results = await Task.WhenAll(tasks);
    return results.All(r => r);
}
```

**Usage:** Run server operations in parallel. Return true only if ALL succeed.

---

## 4. Event-Driven Logging

### Pattern: Consistent Across All Services

```csharp
public event Action<string, LogLevel>? LogMessage;

private void Log(string message, LogLevel level = LogLevel.Info)
{
    LogMessage?.Invoke(message, level);
}
```

**Usage in SshService facade:**
```csharp
_connectionService.LogMessage += OnLogMessage;
_serverControlService.LogMessage += OnLogMessage;
// ... subscribe to all services

private void OnLogMessage(string message, LogLevel level)
{
    LogMessage?.Invoke(message, level); // Propagate up
}
```

**Critical:** All services use same logging pattern. Facade propagates events to UI.

---

## 5. Common Patterns

### Always Use ConnectionInfo Builder
```csharp
var info = _connectionService.GetConnectionInfo(connection);
using var client = new SshClient(info);
```

### Always Wrap SSH.NET in Task.Run
```csharp
await Task.Run(() => client.Connect());
await Task.Run(() => sftpClient.UploadFile(stream, path));
```

### Always Dispose Clients
```csharp
using var client = new SshClient(info);
// ... use client
// Automatic disposal
```

### Path Handling for Linux
```csharp
var remotePath = Path.Combine(baseDir, relativePath).Replace('\\', '/');
```

**Critical:** Always replace backslashes with forward slashes for Linux paths.

---

## 6. When to Extend (Don't Modify Core)

**To add new SSH functionality:**

1. Create new service in `Services/Ssh/`
2. Add to `SshService.cs` constructor
3. Subscribe to `LogMessage` event
4. Expose methods via `SshService` facade

**Example:**
```csharp
// New service
public class BackupService
{
    public event Action<string, LogLevel>? LogMessage;
    // ... implementation
}

// In SshService.cs
private readonly BackupService _backupService;

public SshService()
{
    _backupService = new BackupService();
    _backupService.LogMessage += OnLogMessage;
}

public Task BackupServerAsync(...) => _backupService.BackupAsync(...);
```

---

## 7. Reference: SSH.NET Library Usage

**Library:** Renci.SshNet (NuGet package)

**Key classes:**
- `SshClient` - Execute commands
- `SftpClient` - File transfer
- `ConnectionInfo` - Connection configuration
- `PasswordAuthenticationMethod` - Password auth
- `PrivateKeyAuthenticationMethod` - Key-based auth

**Authentication methods supported:** Password, Private Key (both via `VpsConnection.AuthMethod` enum)

---

**Last updated:** 2025-11-13  
**Lines documented:** 317 lines across 3 core services  
**Token savings per session:** ~15K tokens
