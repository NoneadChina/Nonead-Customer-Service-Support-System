@echo off
chcp 65001 >nul
title "NoneadCSS Backend Prod"

REM Run from wherever this script lives (no hard-coded install path).
cd /d "%~dp0"

REM ---------------------------------------------------------------------------
REM Secrets are NOT kept in this file. Create deploy\secrets.cmd (git-ignored,
REM see deploy/secrets.cmd.example) that only contains `set "..."` lines, and it
REM will be loaded here. Never commit real key material.
REM ---------------------------------------------------------------------------
if exist "%~dp0secrets.cmd" (
    call "%~dp0secrets.cmd"
) else (
    echo [ERROR] missing "%~dp0secrets.cmd" - copy deploy\secrets.cmd.example to deploy\secrets.cmd and fill in the values. 1>&2
    exit /b 1
)

set "ASPNETCORE_ENVIRONMENT=Production"
set "Database__UseSqlite=true"
set "ConnectionStrings__SqliteConnection=Data Source=data/noneadcss.db;Mode=ReadWrite"

echo [%date% %time%] Starting backend (UseSqlite=%Database__UseSqlite%) >> "%TEMP%\ncss_backend_start.log"
"%~dp0backend\NoneadCSS.Api.exe" >> "%TEMP%\ncss_backend_stdout.log" 2>> "%TEMP%\ncss_backend_stderr.log"
exit /b %ERRORLEVEL%
