# Documentation Sync Scripts

## Overview

These scripts help keep reference docs in sync with code changes **without requiring API keys or external services**.

---

## How It Works

### Automatic: Git Pre-Commit Hook

When you try to commit code that changes core services, you'll see:

```
⚠️  SSH Services modified!
   📝 Consider updating: docs/reference/SSH-OPERATIONS.md

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Reference docs may need updating!

Options:
  1. Update docs now (recommended)
  2. Continue with commit (add TODO later)
  3. Skip this check

Choose [1/2/3]:
```

**Choose 1:** Abort commit, update docs, then commit again  
**Choose 2:** Commit proceeds, you update docs later  
**Choose 3:** Commit proceeds, docs may be stale

---

## Helper Scripts

### update-docs.bat

Quick way to mark a doc as "verified today" after you've reviewed it:

```bash
# After updating SSH code and docs
scripts\update-docs.bat ssh

# After updating deployment code and docs  
scripts\update-docs.bat deployment

# After updating map sync code and docs
scripts\update-docs.bat mapsync

# After updating data service code and docs
scripts\update-docs.bat data
```

This updates the "Last updated" timestamp in the doc.

---

## Typical Workflow

### Scenario 1: Small Change (No Doc Update Needed)

```bash
# You fix a bug in ConnectionService.cs
git add QuakeServerManager/Services/Ssh/ConnectionService.cs
git commit -m "fix: handle null connection info"

# Hook triggers:
⚠️  SSH Services modified!
Choose [1/2/3]: 2

# Commit proceeds with TODO reminder
```

### Scenario 2: Pattern Change (Doc Update Needed)

```bash
# You add connection pooling to ConnectionService.cs
git add QuakeServerManager/Services/Ssh/ConnectionService.cs
git commit -m "feat: add connection pooling"

# Hook triggers:
⚠️  SSH Services modified!
Choose [1/2/3]: 1

# Commit aborted. You now:
1. Open docs/reference/SSH-OPERATIONS.md
2. Add section about connection pooling pattern
3. Run: scripts\update-docs.bat ssh
4. git add docs/reference/SSH-OPERATIONS.md
5. git commit -m "feat: add connection pooling + update docs"
```

### Scenario 3: Just Experimenting

```bash
# You're testing something, docs don't matter yet
git commit -m "wip: testing new approach"

# Hook triggers:
Choose [1/2/3]: 3

# Commit proceeds, you'll update docs when feature is done
```

---

## Files Monitored

The pre-commit hook watches:

- `QuakeServerManager/Services/Ssh/**` → SSH-OPERATIONS.md
- `DockerDeploymentService.cs` → DEPLOYMENT-PATTERNS.md
- `MapSyncService.cs` → MAP-SYNC-PATTERNS.md
- `DataService.cs` → DATA-PATTERNS.md

---

## Disabling the Hook (If Needed)

If the hook becomes annoying:

```bash
# Temporary disable (one commit)
git commit --no-verify -m "your message"

# Permanent disable
rm .git/hooks/pre-commit
rm .git/hooks/pre-commit.bat
```

---

## GitHub Sync

Everything is automatically synced to GitHub when you push:

```bash
git push origin main
```

Your code changes AND doc updates go to GitHub together.

---

## Cost

**$0.00** - No API keys, no external services, runs 100% locally.

---

## Questions?

Check CLAUDE.md section "Using Reference Docs" for more info.
