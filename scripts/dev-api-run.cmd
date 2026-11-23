@echo off
rem ---------------------------------------------------------------------------
rem  NoneadCSS API runner with built-in auto-restart.
rem
rem  Why a .cmd loop instead of a PowerShell supervisor:
rem  on this machine a process created through the WMI provider host cannot run
rem  a .ps1 file at all (AppLocker-style script rule), but cmd.exe + dotnet work
rem  fine. So the keep-alive logic lives here, where it is allowed to run.
rem
rem  Usage:  dev-api-run.cmd [port]        (default port 5008)
rem  Stop:   kill this cmd process, then the dotnet child (scripts\dev-down.ps1
rem          does both; killing only dotnet would make this loop restart it).
rem ---------------------------------------------------------------------------
setlocal
set "API_PORT=%~1"
if "%API_PORT%"=="" set "API_PORT=5008"

set "LOG_DIR=%TEMP%\noneadcss-dev"
if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"

cd /d "%~dp0..\NoneadCSS.Api"
if errorlevel 1 (
    echo [dev-api-run] cannot enter NoneadCSS.Api directory
    exit /b 1
)

rem Trailing spaces after a value are part of the value for `set`, which once
rem made environment.IsDevelopment() false. Always quote the whole assignment.
set "ASPNETCORE_ENVIRONMENT=Development"
set "ConnectionStrings__SqliteConnection=Data Source=data/noneadcss.db"

echo [%DATE% %TIME%] dev-api-run start, port=%API_PORT% >> "%LOG_DIR%\api.log"

:loop
echo [%DATE% %TIME%] starting dotnet NoneadCSS.Api.dll >> "%LOG_DIR%\api.log"
dotnet bin\Debug\net10.0\NoneadCSS.Api.dll --urls http://127.0.0.1:%API_PORT% >> "%LOG_DIR%\api.log" 2>&1
set "EXITCODE=%ERRORLEVEL%"
echo [%DATE% %TIME%] API exited with code %EXITCODE%, restarting in 5s >> "%LOG_DIR%\api.log"
timeout /t 5 /nobreak >nul
goto loop
