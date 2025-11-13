@echo off
REM Helper script to update reference docs
REM Just adds a "Last verified" timestamp to the relevant doc

setlocal

if "%1"=="" (
    echo Usage: update-docs.bat [ssh^|deployment^|mapsync^|data]
    echo.
    echo Examples:
    echo   update-docs.bat ssh          - Mark SSH-OPERATIONS.md as verified today
    echo   update-docs.bat deployment   - Mark DEPLOYMENT-PATTERNS.md as verified today
    exit /b 1
)

set DOC_TYPE=%1
set TODAY=%DATE:~10,4%-%DATE:~4,2%-%DATE:~7,2%

if /i "%DOC_TYPE%"=="ssh" (
    set DOC_FILE=docs\reference\SSH-OPERATIONS.md
    set DOC_NAME=SSH-OPERATIONS.md
) else if /i "%DOC_TYPE%"=="deployment" (
    set DOC_FILE=docs\reference\DEPLOYMENT-PATTERNS.md
    set DOC_NAME=DEPLOYMENT-PATTERNS.md
) else if /i "%DOC_TYPE%"=="mapsync" (
    set DOC_FILE=docs\reference\MAP-SYNC-PATTERNS.md
    set DOC_NAME=MAP-SYNC-PATTERNS.md
) else if /i "%DOC_TYPE%"=="data" (
    set DOC_FILE=docs\reference\DATA-PATTERNS.md
    set DOC_NAME=DATA-PATTERNS.md
) else (
    echo Error: Unknown doc type "%DOC_TYPE%"
    exit /b 1
)

if not exist "%DOC_FILE%" (
    echo Error: %DOC_FILE% not found!
    exit /b 1
)

echo Updating %DOC_NAME%...
echo.

REM Read the file and update the "Last updated" line
powershell -Command "(Get-Content '%DOC_FILE%') -replace 'Last updated: .*', 'Last updated: %TODAY%' | Set-Content '%DOC_FILE%'"

echo ✓ Updated "Last updated" timestamp to %TODAY%
echo.
echo Don't forget to actually review and update the content if needed!
echo Then: git add %DOC_FILE%

exit /b 0
