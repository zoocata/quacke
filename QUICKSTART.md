# 🚀 Quick Start: Documentation Sync System

**You wanted:** Automatic doc sync that requires zero thought and works with GitHub.

**You got:** Git hooks that remind you to update docs + automatic GitHub sync.

**Cost:** $0 (no API keys needed)

---

## ⚡ 5-Minute Setup

### Step 1: Run Setup Script

```bash
cd "E:\Quacke Manager\quacke\QuakeServerManager"
..\scripts\setup.bat
```

This installs the git hook that watches for code changes.

### Step 2: Done!

That's it. The system is active.

---

## 🎯 How It Works

### When You Commit Code

```bash
# You change SSH code
git add QuakeServerManager/Services/Ssh/ConnectionService.cs
git commit -m "feat: add connection pooling"

# Hook automatically triggers:
⚠️  SSH Services modified!
   📝 Consider updating: docs/reference/SSH-OPERATIONS.md

Choose [1/2/3]:
  1. Update docs now
  2. Continue (add TODO)
  3. Skip check
```

**Choose 1:** Update docs, then commit again  
**Choose 2:** Commit now, docs later (adds TODO to commit message)  
**Choose 3:** Skip (docs may go stale)

### When You Push to GitHub

```bash
git push origin main
```

Everything syncs automatically:
- Your code changes
- Your doc updates
- All in real-time

---

## 📝 Quick Commands

### Update Doc Timestamps

After you've reviewed/updated a doc:

```bash
scripts\update-docs.bat ssh          # SSH-OPERATIONS.md
scripts\update-docs.bat deployment   # DEPLOYMENT-PATTERNS.md
scripts\update-docs.bat mapsync      # MAP-SYNC-PATTERNS.md
scripts\update-docs.bat data         # DATA-PATTERNS.md
```

### Bypass Hook (If Needed)

```bash
git commit --no-verify -m "wip: quick test"
```

---

## 🧪 Test It Now

1. Make a tiny change to any SSH service file
2. Try to commit it
3. You should see the reminder!

```bash
# Example test
echo "// test comment" >> QuakeServerManager/Services/Ssh/ConnectionService.cs
git add QuakeServerManager/Services/Ssh/ConnectionService.cs
git commit -m "test: checking doc sync"
# You should see the reminder!
```

---

## 📚 More Info

- **How it works:** See `scripts/README.md`
- **Usage guide:** See `CLAUDE.md` Section 6
- **Reference docs:** See `docs/reference/`

---

## ❓ Troubleshooting

**Hook not triggering?**
```bash
# Check if hook is installed
dir ..\..\.git\hooks\pre-commit.bat

# Re-run setup
cd QuakeServerManager
..\scripts\setup.bat
```

**Want to disable?**
```bash
# Temporary (one commit)
git commit --no-verify -m "message"

# Permanent
del ..\..\.git\hooks\pre-commit.bat
```

---

**Ready?** Run `scripts\setup.bat` and you're done! 🎉
