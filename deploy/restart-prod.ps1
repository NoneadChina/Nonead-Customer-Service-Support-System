# ============================================================
# NoneadCSS 生产环境重启脚本
# 端口分配：
#   后端 Kestrel      : 5009
#   前端 Vite preview : 5172
#   Nginx 反向代理    : 5173
# 用法：
#   powershell -ExecutionPolicy Bypass -File deploy/restart-prod.ps1
# ============================================================

$ErrorActionPreference = "Continue"

# 项目路径
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ApiDir = Join-Path $ProjectRoot "NoneadCSS.Api"
$FrontendDir = Join-Path $ProjectRoot "frontend"
$NginxConf = Join-Path $PSScriptRoot "nginx.conf"
$NginxHome = if ($env:NGINX_HOME) { $env:NGINX_HOME } else { throw "请先设置环境变量 NGINX_HOME（本机 nginx 安装目录，例如 C:\nginx）" }

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  NoneadCSS 生产环境重启" -ForegroundColor Cyan
Write-Host "  后端   : http://127.0.0.1:5009" -ForegroundColor Cyan
Write-Host "  前端   : http://127.0.0.1:5172" -ForegroundColor Cyan
Write-Host "  Nginx  : http://127.0.0.1:5173" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ------------------------------------------------------------
# 1) 停止现有进程
# ------------------------------------------------------------
Write-Host "[1/4] 停止现有服务..." -ForegroundColor Yellow

# 停止 Nginx
$nginxProcs = Get-Process -Name "nginx" -ErrorAction SilentlyContinue
if ($nginxProcs) {
    & "$NginxHome\nginx.exe" -s stop -p $NginxHome 2>&1 | Out-Null
    Start-Sleep -Seconds 1
    $nginxProcs = Get-Process -Name "nginx" -ErrorAction SilentlyContinue
    if ($nginxProcs) {
        $nginxProcs | Stop-Process -Force
        Start-Sleep -Seconds 1
    }
    Write-Host "  [OK] Nginx 已停止" -ForegroundColor Green
} else {
    Write-Host "  - Nginx 未运行" -ForegroundColor Gray
}

# 停止后端 dotnet 进程
$dotnetProcs = Get-Process -Name "dotnet" -ErrorAction SilentlyContinue
if ($dotnetProcs) {
    $dotnetProcs | Stop-Process -Force
    Write-Host "  [OK] 后端 dotnet 进程已停止" -ForegroundColor Green
} else {
    Write-Host "  - 后端 dotnet 进程未运行" -ForegroundColor Gray
}

# 停止前端 node 进程
$nodeProcs = Get-Process -Name "node" -ErrorAction SilentlyContinue
if ($nodeProcs) {
    $nodeProcs | Stop-Process -Force
    Write-Host "  [OK] 前端 node 进程已停止" -ForegroundColor Green
} else {
    Write-Host "  - 前端 node 进程未运行" -ForegroundColor Gray
}

Start-Sleep -Seconds 2

# ------------------------------------------------------------
# 2) 启动后端（端口 5009）
# ------------------------------------------------------------
Write-Host "[2/4] 启动后端 (端口 5009)..." -ForegroundColor Yellow

# 构建环境变量设置命令（cmd 方式，兼容 PowerShell 5.x）
$envVars = @(
    "ASPNETCORE_ENVIRONMENT=Production",
    # 不使用 Cache=Shared：SQLite 共享缓存走表级锁，冲突抛 SQLITE_LOCKED，
    # 而 busy_timeout 只对 SQLITE_BUSY 生效 → 多 Job/API 并发写单文件时随机报 database is locked。
    # 改用默认私有缓存（WAL + busy_timeout=30000 由 SqliteConnectionInterceptor 统一设置）。
    "ConnectionStrings__SqliteConnection=Data Source=data/noneadcss.db;Mode=ReadWrite"
)
$userVars = @("JWT__Key", "ENCRYPTION__Key", "AUDIT__HmacKey", "APP__SatisfactionTokenKey")
foreach ($v in $userVars) {
    $val = [Environment]::GetEnvironmentVariable($v, "User")
    if ($val) { $envVars += "$v=$val" }
}
$setCmd = ($envVars | ForEach-Object { "set `"$_`"" }) -join " && "

Start-Process -FilePath "cmd" `
    -ArgumentList "/c $setCmd && dotnet run --no-build --no-launch-profile --urls `"http://127.0.0.1:5009`"" `
    -WorkingDirectory $ApiDir `
    -WindowStyle Minimized

Write-Host "  [OK] 后端已启动" -ForegroundColor Green

# ------------------------------------------------------------
# 3) 启动前端 Vite preview（端口 5172）
# ------------------------------------------------------------
Write-Host "[3/4] 启动前端 Vite preview (端口 5172)..." -ForegroundColor Yellow

# 确保前端已构建
$distDir = Join-Path $FrontendDir "dist"
if (-not (Test-Path $distDir)) {
    Write-Host "  dist/ 不存在，执行构建..." -ForegroundColor DarkYellow
    Push-Location $FrontendDir
    npm run build 2>&1 | Out-Host
    Pop-Location
}

Start-Process -FilePath "cmd" `
    -ArgumentList "/c npx vite preview --port 5172 --host" `
    -WorkingDirectory $FrontendDir `
    -WindowStyle Minimized

Write-Host "  [OK] 前端已启动" -ForegroundColor Green

# ------------------------------------------------------------
# 4) 启动 Nginx（端口 5173）
# ------------------------------------------------------------
Write-Host "[4/4] 启动 Nginx (端口 5173)..." -ForegroundColor Yellow

# 确保日志和缓存目录存在
$logDir = Join-Path $PSScriptRoot "logs"
$cacheDir = Join-Path $PSScriptRoot "cache"
foreach ($d in @(
    $logDir,
    (Join-Path $cacheDir "proxy"),
    (Join-Path $cacheDir "client_body"),
    (Join-Path $cacheDir "fastcgi"),
    (Join-Path $cacheDir "uwsgi"),
    (Join-Path $cacheDir "scgi")
)) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d -Force | Out-Null }
}

Start-Process -FilePath "$NginxHome\nginx.exe" `
    -ArgumentList "-c `"$NginxConf`" -p `"$NginxHome`"" `
    -WindowStyle Hidden

Write-Host "  [OK] Nginx 已启动" -ForegroundColor Green

# ------------------------------------------------------------
# 等待服务就绪
# ------------------------------------------------------------
Write-Host ""
Write-Host "等待服务就绪..." -ForegroundColor Yellow

$maxWait = 40
$backendReady = $false
$frontendReady = $false
$nginxReady = $false

for ($i = 1; $i -le $maxWait; $i++) {
    Start-Sleep -Seconds 1
    if (-not $backendReady) {
        try {
            Invoke-WebRequest -Uri "http://127.0.0.1:5009/health" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop | Out-Null
            $backendReady = $true; Write-Host "  [OK] 后端就绪 (5009)" -ForegroundColor Green
        } catch { }
    }
    if (-not $frontendReady) {
        try {
            Invoke-WebRequest -Uri "http://127.0.0.1:5172" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop | Out-Null
            $frontendReady = $true; Write-Host "  [OK] 前端就绪 (5172)" -ForegroundColor Green
        } catch { }
    }
    if (-not $nginxReady) {
        try {
            Invoke-WebRequest -Uri "http://127.0.0.1:5173" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop | Out-Null
            $nginxReady = $true; Write-Host "  [OK] Nginx 就绪 (5173)" -ForegroundColor Green
        } catch { }
    }
    if ($backendReady -and $frontendReady -and $nginxReady) { break }
}

Write-Host ""
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  生产环境重启完成" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  入口 : http://localhost:5173" -ForegroundColor White
Write-Host "  前端 : http://localhost:5172" -ForegroundColor White
Write-Host "  后端 : http://localhost:5009" -ForegroundColor White
Write-Host ""

if (-not $backendReady) { Write-Host "  [!] 后端未就绪，请检查 dotnet 进程" -ForegroundColor Red }
if (-not $frontendReady) { Write-Host "  [!] 前端未就绪，请检查 node 进程" -ForegroundColor Red }
if (-not $nginxReady) { Write-Host "  [!] Nginx 未就绪，请检查 nginx 进程" -ForegroundColor Red }
