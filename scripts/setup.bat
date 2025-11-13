@echo off
REM Setup script for Quacke documentation sync system

echo ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
echo Quacke Documentation Sync Setup
echo ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
echo.

REM Check if we're in a git repo
git rev-parse --git-dir >nul 2>&1
if errorlevel 1 (
    echo ❌ Error: Not in a git repository!
    echo    Run this from: E:\Quacke Manager\quacke\QuakeServerManager
    pause
    exit /b 1
)

echo ✓ Git repository detected
echo.

REM Check if hooks directory exists
if not exist "..\..\.git\hooks" (
    echo Creating .git\hooks directory...
    mkdir "..\..\.git\hooks"
)

REM Copy hooks
echo Installing git hooks...
copy "..\scripts\pre-commit.bat" "..\..\.git\hooks\pre-commit.bat" >nul
if errorlevel 1 (
    echo ❌ Failed to copy pre-commit hook
    pause
    exit /b 1
)

echo ✓ Pre-commit hook installed
echo.

REM Configure git to use hooks
git config core.hooksPath .git/hooks
echo ✓ Git hooks configured
echo.

echo ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
echo Setup Complete!
echo ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
echo.
echo What happens now:
echo   • When you commit changes to SSH services, you'll get a reminder
echo   • When you commit changes to deployment/map sync, you'll get a reminder
echo   • You can choose to update docs immediately or add a TODO
echo.
echo Helper scripts:
echo   scripts\update-docs.bat ssh          - Update SSH-OPERATIONS.md timestamp
echo   scripts\update-docs.bat deployment   - Update DEPLOYMENT-PATTERNS.md timestamp
echo.
echo Test it:
echo   1. Make a small change to any file in Services\Ssh\
echo   2. git add [file]
echo   3. git commit -m "test"
echo   4. You should see the doc reminder!
echo.
echo See scripts\README.md for full documentation.
echo.
pause
