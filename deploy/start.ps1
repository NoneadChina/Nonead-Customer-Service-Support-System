# ============================================================
# NoneadCSS Production Environment Startup Script
# Production dir: this script's own folder ($PSScriptRoot)
# Ports:
#   Backend Kestrel   : 5009
#   Frontend Vite PRV : 5172
#   Nginx Reverse Proxy : 5173
# Usage:
#   powershell -ExecutionPolicy Bypass -File deploy/start.ps1
# ============================================================

$ErrorActionPreference = "Continue"

# ========== Paths ==========
$ProdRoot     = $PSScriptRoot
$BackendExe   = Join-Path $ProdRoot "backend\NoneadCSS.Api.exe"
$FrontendDir  = Join-Path (Split-Path -Parent $ProdRoot) "frontend"
$NginxConf    = Join-Path $ProdRoot "nginx.conf"
$NginxHome    = if ($env:NGINX_HOME) { $env:NGINX_HOME } else { throw "请先设置环境变量 NGINX_HOME（本机 nginx 安装目录，例如 C:\nginx）" }

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  NoneadCSS Production Startup" -ForegroundColor Cyan
Write-Host "  ProdRoot: $ProdRoot" -ForegroundColor DarkGray
Write-Host "  Backend : http://127.0.0.1:5009" -ForegroundColor Cyan
Write-Host "  Frontend: http://127.0.0.1:5172" -ForegroundColor Cyan
Write-Host "  Nginx   : http://127.0.0.1:5173" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ========== Step 0: Check running services ==========
Write-Host "[Pre] Checking existing processes..." -ForegroundColor Yellow

$backendRunning  = [bool](Get-Process -Name "NoneadCSS.Api" -ErrorAction SilentlyContinue)
$frontendRunning = [bool]((netstat -ano | Select-String "127\.0\.0\.1:5172" | Select-String "LISTENING"))
$nginxRunning    = [bool](Get-Process -Name "nginx" -ErrorAction SilentlyContinue)

if ($backendRunning -or $frontendRunning -or $nginxRunning) {
    Write-Host "  Services already running detected:" -ForegroundColor DarkYellow
    Write-Host ("    Backend  (5009): " + $(if($backendRunning){"RUNNING"}else{"STOPPED"}))
    Write-Host ("    Frontend (5172): " + $(if($frontendRunning){"RUNNING"}else{"STOPPED"}))
    Write-Host ("    Nginx    (5173): " + $(if($nginxRunning){"RUNNING"}else{"STOPPED"}))
    Write-Host "  Please run stop.ps1 first." -ForegroundColor Red
    Write-Host ""
    exit 1
} else {
    Write-Host "  [OK] No running services." -ForegroundColor Green
}

# ========== Step 1: Start Backend (5009) via WMI ==========
Write-Host "[1/3] Starting Backend (port 5009) [WMI detached]..." -ForegroundColor Yellow

if (-not (Test-Path $BackendExe)) {
    Write-Host ("  [FAIL] Backend EXE not found: " + $BackendExe) -ForegroundColor Red
    exit 1
}

# Build a wrapper .CMD with all env vars baked in
$wrapperPath = Join-Path $ProdRoot "_start-backend.cmd"
$wrapperLines = New-Object System.Collections.Generic.List[string]
$wrapperLines.Add('@echo off')
$wrapperLines.Add('chcp 65001 >nul')
$wrapperLines.Add('title "NoneadCSS Backend Prod"')
$wrapperLines.Add(('cd /d "' + $ProdRoot + '"'))
$wrapperLines.Add('set "ASPNETCORE_ENVIRONMENT=Production"')
$wrapperLines.Add('set "Database__UseSqlite=true"')
$wrapperLines.Add('set "ConnectionStrings__SqliteConnection=Data Source=data/noneadcss.db;Mode=ReadWrite"')

$userVarNames = @(
    "JWT__Key",
    "ENCRYPTION__Key",
    "AUDIT__HmacKey",
    "APP__SatisfactionTokenKey",
    "SECRET_HASHER__Key",
    "APP__FrontendUrl",
    "APP__BaseUrl",
    "AzureAd__ClientId",
    "AzureAd__ClientSecret",
    "AzureAd__TenantId"
)
foreach ($v in $userVarNames) {
    $val = [Environment]::GetEnvironmentVariable($v, "User")
    if ($val) {
        $wrapperLines.Add(('set "' + $v + '=' + $val + '"'))
    }
}
$wrapperLines.Add('echo [%date% %time%] Starting backend (UseSqlite=%Database__UseSqlite%) >> "%TEMP%\ncss_backend_start.log"')
$wrapperLines.Add(('"' + $BackendExe + '" >> "%TEMP%\ncss_backend_stdout.log" 2>> "%TEMP%\ncss_backend_stderr.log"'))
$wrapperLines.Add('exit /b %ERRORLEVEL%')
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllLines($wrapperPath, $wrapperLines, $utf8NoBom)

# Clear old logs
foreach ($logName in @("ncss_backend_stdout.log","ncss_backend_stderr.log")) {
    $logPath = Join-Path $env:TEMP $logName
    if (Test-Path $logPath) { Remove-Item $logPath -Force -ErrorAction SilentlyContinue }
}

# Launch via WMI to detach from this PS session
$wmiResult = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
    CommandLine      = '"' + $wrapperPath + '"'
    CurrentDirectory = $ProdRoot
}

if ($wmiResult.ReturnValue -ne 0) {
    Write-Host ("  [FAIL] WMI Win32_Process.Create failed with code " + $wmiResult.ReturnValue) -ForegroundColor Red
    exit 1
}
Write-Host ("  [OK] Backend wrapper launched via WMI (PID=" + $wmiResult.ProcessId + ")") -ForegroundColor Green

# ========== Step 2: Start Frontend Vite preview (5172) ==========
Write-Host "[2/3] Starting Frontend Vite preview (port 5172)..." -ForegroundColor Yellow

if (-not (Test-Path $FrontendDir)) {
    Write-Host ("  [FAIL] Frontend dir not found: " + $FrontendDir) -ForegroundColor Red
    exit 1
}

$distDir = Join-Path $FrontendDir "dist"
if (-not (Test-Path $distDir)) {
    Write-Host "  dist/ not found, building frontend..." -ForegroundColor DarkYellow
    Push-Location $FrontendDir
    npm run build 2>&1 | Out-Host
    Pop-Location
}

$feArgs = '/c npx vite preview --port 5172 --host 127.0.0.1'
Start-Process -FilePath "cmd" -ArgumentList $feArgs -WorkingDirectory $FrontendDir -WindowStyle Minimized

Write-Host "  [OK] Frontend launch command sent." -ForegroundColor Green

# ========== Step 3: Start Nginx (5173) ==========
Write-Host "[3/3] Starting Nginx (port 5173)..." -ForegroundColor Yellow

# Ensure log and cache dirs exist
$logDir    = Join-Path $ProdRoot "logs"
$cacheDir  = Join-Path $ProdRoot "cache"
$subDirs = @(
    $logDir,
    (Join-Path $cacheDir "proxy"),
    (Join-Path $cacheDir "client_body"),
    (Join-Path $cacheDir "fastcgi"),
    (Join-Path $cacheDir "uwsgi"),
    (Join-Path $cacheDir "scgi")
)
foreach ($d in $subDirs) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
}

$nginxArgs = '-c "' + $NginxConf + '" -p "' + $NginxHome + '"'
Start-Process -FilePath (Join-Path $NginxHome "nginx.exe") -ArgumentList $nginxArgs -WindowStyle Hidden

Write-Host "  [OK] Nginx launch command sent." -ForegroundColor Green

# ========== Wait for services to be ready ==========
Write-Host ""
Write-Host "Waiting for services (max 60s)..." -ForegroundColor Yellow

$maxWait = 60
$backendReady = $false
$frontendReady = $false
$nginxReady = $false

for ($i = 1; $i -le $maxWait; $i++) {
    Start-Sleep -Seconds 1

    if (-not $backendReady) {
        try {
            Invoke-WebRequest -Uri "http://127.0.0.1:5009/health" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop | Out-Null
            $backendReady = $true
            Write-Host "  [OK] Backend ready (5009)" -ForegroundColor Green
        } catch { }
    }
    if (-not $frontendReady) {
        try {
            Invoke-WebRequest -Uri "http://127.0.0.1:5172" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop | Out-Null
            $frontendReady = $true
            Write-Host "  [OK] Frontend ready (5172)" -ForegroundColor Green
        } catch { }
    }
    if (-not $nginxReady) {
        try {
            Invoke-WebRequest -Uri "http://127.0.0.1:5173" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop | Out-Null
            $nginxReady = $true
            Write-Host "  [OK] Nginx ready (5173)" -ForegroundColor Green
        } catch { }
    }
    if ($backendReady -and $frontendReady -and $nginxReady) { break }
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Production Startup Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Entry   : http://localhost:5173" -ForegroundColor White
Write-Host "  Frontend: http://localhost:5172" -ForegroundColor White
Write-Host "  Backend : http://localhost:5009" -ForegroundColor White
Write-Host ""

if (-not $backendReady) {
    Write-Host "  [!] Backend NOT ready within 60s." -ForegroundColor Red
    Write-Host "      => Check %TEMP%\ncss_backend_stdout.log / ncss_backend_stderr.log" -ForegroundColor Red
}
if (-not $frontendReady) { Write-Host "  [!] Frontend NOT ready, check node (vite preview)" -ForegroundColor Red }
if (-not $nginxReady)    { Write-Host "  [!] Nginx NOT ready, check deploy/logs/nginx-error.log" -ForegroundColor Red }