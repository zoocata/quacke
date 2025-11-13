# CLAUDE.md - Quacke Server Manager

**Platform:** C# WPF + SSH.NET + Docker (Windows desktop)  
**Target size:** <5KB (currently: ~4.2KB)

---

## Why This File Exists

**Problem:** Claude Code re-reads 500+ lines of SSH services every session (~15K tokens)

**Solution:** Reference docs point Claude to patterns without re-reading implementation

**Usage:** Read CLAUDE.md → find relevant reference doc → implement using established patterns

---

## 1. Quick Orientation

You implement features for Quacke Server Manager - a Windows GUI tool that manages Quake III Arena CPMA server instances across remote VPSs via SSH and Docker.

**Your role:**
- Read implementation guide (what to build)
- Reference domain glossary for VPS/deployment concepts
- Reference architecture patterns to reuse established code patterns
- Ask when integration points unclear

**You generate 100% of C# implementation code.**

---

## 2. Quick Decision Tree ⭐ START HERE

### By Token Impact (Read High Impact First)

**🔥 High Impact - Domain Knowledge**  
→ `/mnt/project/quacke_definitions.md` ✅ EXISTS

**🔥 High Impact - SSH Architecture** (~15K tokens saved/session)  
→ `docs/reference/SSH-OPERATIONS.md` ✅ EXISTS

**📊 Medium Impact - Deployment** (~8K tokens saved/session)  
→ `docs/reference/DEPLOYMENT-PATTERNS.md` ✅ EXISTS

**📊 Medium Impact - Map Sync** (~5K tokens saved/session)  
→ `docs/reference/MAP-SYNC-PATTERNS.md` ⚠️ TODO - read `MapSyncService.cs` directly

**📝 Low Impact - Data Persistence** (~3K tokens saved/session)  
→ `docs/reference/DATA-PATTERNS.md` ⚠️ TODO - read `DataService.cs` directly

**📝 Low Impact - UI Patterns** (~2K tokens saved/session)  
→ `docs/reference/UI-PATTERNS.md` ⚠️ TODO - read `ViewModels/` directly

---

## 2.1 Feature Type → Reference Doc Mapping

| I need to... | Read this first | Then read if needed |
|-------------|----------------|---------------------|
| Connect to VPS | SSH-OPERATIONS.md §1 | ConnectionService.cs |
| Upload files to VPS | SSH-OPERATIONS.md §2 | FileTransferService.cs |
| Start/stop servers | SSH-OPERATIONS.md §3 | ServerControlService.cs |
| Deploy new servers | DEPLOYMENT-PATTERNS.md | DockerDeploymentService.cs |
| Sync maps | MapSyncService.cs | - |
| Save/load profiles | DataService.cs | - |
| Add UI controls | MainViewModel.cs | - |

**Keywords → Docs:**
- "ssh", "connection", "remote" → SSH-OPERATIONS.md
- "deploy", "docker", "container" → DEPLOYMENT-PATTERNS.md
- "map", "upload", "sync" → MapSyncService.cs
- "save", "load", "profile" → DataService.cs

---

## 2.2 How to Use Reference Docs

**Priority order:**
1. Read reference doc FIRST (~2K tokens) for pattern understanding
2. Read source ONLY if doc doesn't cover your case
3. Trust source over doc when conflict exists - source is truth

**Token cost comparison:**
- Doc only: ~2K tokens
- Doc + source: ~3K tokens  
- Source only (no doc): ~8K tokens

---

## 3. Project Structure

```
E:\Quacke Manager\quacke\QuakeServerManager\
├── Models/
│   ├── VpsConnection.cs      # VPS connection model
│   ├── ServerInstance.cs     # Server instance model
│   ├── DeploymentState.cs    # Deployment state machine
│   ├── MapManifest.cs        # Map sync manifest
│   └── Class.cs              # CPMA class configs
│
├── Services/
│   ├── Ssh/                  # SSH architecture (6 services)
│   │   ├── ConnectionService.cs       # Connection lifecycle (69 lines)
│   │   ├── FileTransferService.cs     # SFTP operations (141 lines)
│   │   ├── DockerDeploymentService.cs # Docker deploy (507 lines)
│   │   ├── ServerControlService.cs    # Start/stop/restart (107 lines)
│   │   ├── ImportService.cs           # Import existing configs
│   │   └── SshClientExtensions.cs     # SSH.NET helpers
│   │
│   ├── SshService.cs         # Facade coordinating SSH services (77 lines)
│   ├── DataService.cs        # Profile/config persistence
│   ├── MapSyncService.cs     # Map change detection (218 lines)
│   ├── Q3ValidationService.cs # Quake 3 validation
│   ├── CryptoHelper.cs       # Credential encryption
│   └── DialogService.cs      # UI dialogs
│
├── ViewModels/               # MVVM pattern
│   ├── MainViewModel.cs
│   ├── ServerManagerViewModel.cs
│   ├── VpsManagerViewModel.cs
│   ├── ViewModelBase.cs
│   └── RelayCommand.cs
│
├── Views/                    # WPF dialogs
│   ├── InputDialog.xaml
│   └── MapUploadDialog.xaml
│
└── Converters/               # WPF value converters (5 files)
```

---

## 4. Core Architecture Quick Reference

**SSH Layer:** Facade pattern via `SshService.cs`
- Coordinates 5 specialized services (Connection, FileTransfer, Deploy, Control, Import)
- Event-based logging propagated through facade
- All services use Task-based async

**Docker Deployment:** `DockerDeploymentService.cs` (507 lines)
- Complex orchestration logic
- Progress callback pattern
- Read DEPLOYMENT-PATTERNS.md before modifying

**Map Sync:** `MapSyncService.cs` (218 lines)
- Change detection via manifest comparison
- Selective upload queue

**Data Persistence:** `DataService.cs`
- JSON serialization for profiles
- Encrypted credential storage via CryptoHelper

**MVVM:** Standard WPF pattern
- ViewModelBase with INotifyPropertyChanged
- RelayCommand for command binding
- DialogService for UI separation

---

## 5. Critical Constraints

**Core Services (Stable - Extend, Don't Modify):**
```
QuakeServerManager/Services/Ssh/ConnectionService.cs
QuakeServerManager/Services/Ssh/FileTransferService.cs
QuakeServerManager/Models/VpsConnection.cs
QuakeServerManager/Models/ServerInstance.cs
```

---

## 5.1 When Can I Modify Core Files?

**✅ Safe to modify:**
- Bug fixes
- Adding optional parameters with defaults
- Internal optimization (same signature)
- Documentation/comments

**⚠️ Ask first:**
- Signature changes (parameters, return types)
- Breaking changes (removing methods)
- Architecture changes (sync to async)

**❌ Never modify:**
- To avoid creating new service
- Because "it's easier"
- During experimentation

---

## 6. Anti-Patterns (Don't Do This)

### ❌ Creating SSH Clients Directly
```csharp
var client = new SshClient(host, username, password); // NO
```
Use ConnectionService.GetConnectionInfo() instead.

### ❌ Blocking UI Thread
```csharp
sftpClient.UploadFile(stream, remotePath); // NO - blocks UI
```
Wrap in Task.Run() and use async/await.

### ❌ Business Logic in ViewModels
```csharp
public class MainViewModel {
    public void Connect() {
        var client = new SshClient(...); // NO - logic in VM
    }
}
```
Call SshService methods, keep ViewModels thin.

### ❌ Hard-Coding Paths
```csharp
var remotePath = "C:\\root\\cpma\\server.cfg"; // NO - Windows path
```
Use Path.Combine() and .Replace('\\', '/') for Linux.

### ❌ Ignoring Progress Callbacks
Use progress normalization pattern (see DEPLOYMENT-PATTERNS.md).

---

## 7. Implementation Checklist

**Code Quality:**
- [ ] Used existing services (no new SSH clients)
- [ ] Async/await pattern (all I/O is async)
- [ ] Event-based logging (LogMessage events)
- [ ] Disposed resources (using statements)
- [ ] Exception handling

**Pattern Compliance:**
- [ ] SSH operations use ConnectionService.GetConnectionInfo()
- [ ] File transfers use FileTransferService
- [ ] Server control uses ServerControlService via SshService facade
- [ ] Progress callbacks normalized 0-100
- [ ] Linux paths (forward slashes)

**Testing:**
- [ ] Compiles without errors
- [ ] Tested against VPS or test plan described
- [ ] Error cases handled

**Documentation:**
- [ ] Updated reference doc if new pattern
- [ ] Git commit describes what and why

---

## 8. Git Workflow + Auto Doc Sync

**Git hook automatically reminds you to update docs when committing changes to:**
- Services/Ssh/** → SSH-OPERATIONS.md
- DockerDeploymentService.cs → DEPLOYMENT-PATTERNS.md
- MapSyncService.cs → MAP-SYNC-PATTERNS.md
- DataService.cs → DATA-PATTERNS.md

**Helper script after updating docs:**
```bash
scripts\update-docs.bat ssh          # Updates timestamp
scripts\update-docs.bat deployment
```

**Setup (one-time):**
```bash
cd QuakeServerManager
..\scripts\setup.bat
```

See `scripts/README.md` for full documentation.

**⚠️ Git Ignore:**
- bin/, obj/, .vs/ folders
- *.csproj.user files
- Real credentials in meta.json

---

## 9. Example: Good vs Bad Implementation

**Task:** Add SSH connection timeout parameter

### ❌ Bad
```csharp
public async Task<bool> ConnectWithTimeout(string host, int timeout) {
    var client = new SshClient(host, "root", "password");
    client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(timeout);
    await Task.Run(() => client.Connect());
    return client.IsConnected;
}
```
Problems: Doesn't use ConnectionService, hard-coded credentials, no logging, breaks facade pattern.

### ✅ Good
```csharp
// In ConnectionService.cs
public ConnectionInfo GetConnectionInfo(VpsConnection connection, int timeoutSeconds = 30) {
    var authMethods = connection.AuthMethod == AuthMethod.Password
        ? new AuthenticationMethod[] { new PasswordAuthenticationMethod(connection.Username, connection.Password) }
        : new AuthenticationMethod[] { new PrivateKeyAuthenticationMethod(connection.Username, new PrivateKeyFile(connection.PrivateKeyPath)) };
    
    var connInfo = new ConnectionInfo(connection.Ip, connection.Port, connection.Username, authMethods);
    connInfo.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
    return connInfo;
}

// In SshService.cs
public Task<bool> TestConnectionAsync(VpsConnection connection, int timeoutSeconds = 30)
    => _connectionService.TestConnectionAsync(connection, timeoutSeconds);
```
Extends existing method, uses ConnectionService pattern, follows facade, logging wired up, no breaking changes.

---

## 10. Success Metrics

**Track to validate ROI:**
- Tokens per Claude Code session (before/after)
- Time to context (seconds until implementation starts)
- Pattern consistency (reuse vs reinvent)

**Target:** Save 15K+ tokens per session with SSH-OPERATIONS.md alone.

---

**Last updated:** 2025-11-13  
**Version:** 2.0 (Production Ready)
