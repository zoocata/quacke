# DEPLOYMENT-PATTERNS.md

**Purpose:** Document Docker deployment orchestration to avoid re-reading 507 lines each Claude Code session.

**Token savings:** ~8K tokens per session

---

## Deployment Architecture

**Pattern:** Single Docker Image + docker-compose for Multi-Instance

```
Local (Quacke)                   Remote (VPS)
│                                 │
├─ Generate Build Context        │
│  ├─ Dockerfile                 │
│  ├─ cnq3-server-x64           │
│  ├─ cpma/ (folder)            │
│  ├─ maps/ (optional)          │
│  ├─ pak0.pk3                  │
│  ├─ custom_maps/ (optional)   │
│  ├─ configs/                  │
│  │  ├─ server1.cfg            │
│  │  └─ server2.cfg            │
│  └─ docker-compose.yml        │
│                                │
├─ Upload to /root/q3docker ────>│
│                                │
│                                ├─ docker build .
│                                │
│                                └─ docker-compose up -d
```

**Key principle:** Build context is prepared locally, uploaded once, built remotely.

---

## Deployment Flow (10 Steps)

### Step 1: Connect (5%)
```csharp
var connInfo = _connectionService.GetConnectionInfo(connection);
using var sshClient = new SshClient(connInfo);
using var sftpClient = new SftpClient(connInfo);
sshClient.Connect();
sftpClient.Connect();
```

### Step 2: Ensure Docker (10%)
```csharp
if (!await EnsureDockerAsync(sshClient))
{
    Log("Docker installation failed or is unavailable.", LogLevel.Error);
    return false;
}
```

**Pattern:** Check for Docker, install if missing (implementation in service).

### Step 3: Create Local Build Context (15%)
```csharp
var localTempDir = Path.Combine(Path.GetTempPath(), "quacke_build_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(localTempDir);
```

**Critical:** Use GUID for unique temp directory to avoid conflicts.

### Step 4: Generate Dockerfile (20%)
```csharp
var dockerfilePath = Path.Combine(localTempDir, "Dockerfile");
GenerateDockerfile(dockerfilePath, customMaps != null && customMaps.Any());
```

**Pattern:** Dockerfile generation based on whether custom maps exist.

### Step 5: Copy Server Executable (30%)
```csharp
var localExecutable = Path.Combine(localTempDir, "cnq3-server-x64");
File.Copy(serverExecutablePath, localExecutable, true);
```

### Step 6: Copy CPMA Folder (45%)
```csharp
var cpmaDir = Path.Combine(localTempDir, "cpma");
CopyDirectory(cpmaPath, cpmaDir, true);
```

**Helper method:** `CopyDirectory(source, dest, recursive)` - copies entire folder structure.

### Step 7: Copy Maps (Optional) (55%)
```csharp
if (!string.IsNullOrEmpty(mapsPath) && Directory.Exists(mapsPath))
{
    var mapsDir = Path.Combine(localTempDir, "maps");
    CopyDirectory(mapsPath, mapsDir, true);
}
```

### Step 8: Copy pak0.pk3 + Custom Maps (60%)
```csharp
File.Copy(pak0Path, Path.Combine(localTempDir, "pak0.pk3"), true);

if (customMaps != null && customMaps.Any())
{
    var customMapsDir = Path.Combine(localTempDir, "custom_maps");
    Directory.CreateDirectory(customMapsDir);
    
    foreach (var mapPath in customMaps)
    {
        var mapFileName = Path.GetFileName(mapPath);
        File.Copy(mapPath, Path.Combine(customMapsDir, mapFileName), true);
    }
}
```

### Step 9: Generate Configs (70%)
```csharp
var configsDir = Path.Combine(localTempDir, "configs");
Directory.CreateDirectory(configsDir);

foreach (var instance in instances)
{
    var configPath = Path.Combine(configsDir, $"{instance.Name}.cfg");
    GenerateServerConfig(instance, configPath);
}
```

**Critical:** One .cfg file per server instance.

### Step 10: Generate docker-compose.yml (75%)
```csharp
var composePath = Path.Combine(localTempDir, "docker-compose.yml");
GenerateCompose(composePath, instances);
```

**Pattern:** docker-compose defines multiple containers from single image.

---

## Upload & Build (80-100%)

### Upload Build Context (65-80%)
```csharp
await UploadBuildContextAsync(sftpClient, localTempDir, remoteContext, progressCallback);
```

**Pattern:** Uses `FileTransferService.UploadDirectoryAsync()` with progress normalization.

### Build Image (80-90%)
```csharp
var buildSuccess = await BuildDockerImageAsync(sshClient, remoteContext);
if (!buildSuccess)
{
    Log("Failed to build Docker image", LogLevel.Error);
    return false;
}
```

**Implementation:** Executes `docker build -t quake3-cpma .` via SSH.

### Start Containers (90-100%)
```csharp
var startSuccess = await StartContainersAsync(sshClient, remoteContext, instances);
if (!startSuccess)
{
    Log("Failed to start containers", LogLevel.Error);
    return false;
}
```

**Implementation:** Executes `docker-compose up -d` via SSH.

---

## Progress Callback Pattern

**Signature:**
```csharp
Action<double, string>? progressCallback
```

**Usage:**
```csharp
progressCallback?.Invoke(percentage, message);
```

**Progress ranges:**
- 0-5%: Connection
- 5-10%: Docker check
- 10-15%: Context prep
- 15-70%: File copying & config generation
- 70-80%: Upload
- 80-90%: Build
- 90-100%: Start containers

**Nested progress:** For `UploadDirectoryAsync`, pass normalized range:
```csharp
await UploadBuildContextAsync(
    sftpClient, 
    localTempDir, 
    remoteContext, 
    (progress, message) => progressCallback?.Invoke(65 + (progress * 0.15), message)
);
```

This maps the upload's 0-100% to the overall 65-80% range.

---

## Cleanup Pattern

**Always cleanup temp directory:**
```csharp
try
{
    // ... deployment logic
}
finally
{
    try
    {
        Directory.Delete(localTempDir, true);
    }
    catch
    {
        // Ignore cleanup errors
    }
}
```

**Critical:** Use nested try-catch so cleanup failures don't mask deployment errors.

---

## File Generation Methods

### GenerateDockerfile(path, hasCustomMaps)
Creates Dockerfile with:
- Base image (likely Ubuntu + dependencies)
- COPY instructions for executable, CPMA, maps, pak0
- Conditional COPY for custom_maps if `hasCustomMaps == true`
- ENTRYPOINT for cnq3-server-x64

### GenerateServerConfig(instance, path)
Creates server.cfg from `ServerInstance` model:
- Hostname, rcon password
- Map rotation
- Game type settings
- Player limits
- Advanced settings

### GenerateCompose(path, instances)
Creates docker-compose.yml:
- Service per instance
- Port mapping (unique per instance)
- Volume mount for configs
- Container naming matches `instance.Name`

---

## Key Patterns

### Single Image, Multiple Containers
```yaml
services:
  server1:
    image: quake3-cpma
    container_name: server1
    ports:
      - "27960:27960/udp"
    volumes:
      - ./configs/server1.cfg:/root/cpma/cpma/server.cfg
      
  server2:
    image: quake3-cpma
    container_name: server2
    ports:
      - "27961:27960/udp"
    volumes:
      - ./configs/server2.cfg:/root/cpma/cpma/server.cfg
```

**Critical:** 
- Container name = instance name (for ServerControlService)
- Unique external ports
- Config mounted via volume

### Error Handling
```csharp
try
{
    // deployment steps
    return true;
}
catch (Exception ex)
{
    Log($"Docker deployment error: {ex.Message}", LogLevel.Error);
    return false;
}
```

**Pattern:** Log error, return false. UI handles failed deployment.

### Resource Disposal
```csharp
using var sshClient = new SshClient(connInfo);
using var sftpClient = new SftpClient(connInfo);
```

**Critical:** Both clients disposed automatically even if deployment fails.

---

## When to Extend

**To add deployment steps:**
1. Add to local build context preparation (Steps 3-10)
2. Update progress percentages (keep total = 100%)
3. Upload as part of build context
4. Reference in Dockerfile/docker-compose if needed

**To modify Docker behavior:**
- Edit `GenerateDockerfile()` - controls image build
- Edit `GenerateCompose()` - controls container config
- Edit `BuildDockerImageAsync()` - controls build command
- Edit `StartContainersAsync()` - controls startup

**Don't modify:** Core SSH operations (use ConnectionService/FileTransferService)

---

## Common Pitfalls

### ❌ Forgetting to cleanup temp directory
Always use try-finally with nested try-catch.

### ❌ Progress percentages don't add to 100%
Plan percentages before implementing. Total should equal 100%.

### ❌ Hard-coding remote paths
Use variables for `/root/q3docker` in case deployment location changes.

### ❌ Not handling missing optional files
Always check `Directory.Exists()` and `File.Exists()` before copying.

### ❌ Container name mismatch
Container name MUST match `instance.Name` for ServerControlService to work.

---

**Last updated:** 2025-11-13  
**Lines documented:** 507 lines  
**Token savings per session:** ~8K tokens
