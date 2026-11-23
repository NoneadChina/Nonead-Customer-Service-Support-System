# ============================================================
# NoneadCSS 开发环境重启脚本
# 【开发环境不涉及 Nginx】：前端直连 vite dev server，API 由前端代理到后端 5008。
# 端口分配：
#   后端 Kestrel  (dotnet) : 5008
#   前端 Vite dev  (node)  : 5171
# 本脚本只操作：dotnet、node 两个进程，不启动/停止 Nginx。
# 用法：
#   powershell -ExecutionPolicy Bypass -File deploy/restart-dev.ps1
# ============================================================

$ErrorActionPreference = "Continue"

# 项目根目录（脚本位于 deploy/ 下，向上一级即项目根）
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ApiDir = Join-Path $ProjectRoot "NoneadCSS.Api"
$FrontendDir = Join-Path $ProjectRoot "frontend"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  NoneadCSS 开发环境重启" -ForegroundColor Cyan
Write-Host "  后端 : http://127.0.0.1:5008" -ForegroundColor Cyan
Write-Host "  前端 : http://127.0.0.1:5171" -ForegroundColor Cyan
Write-Host "  (开发环境不使用 Nginx)" -ForegroundColor DarkGray
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# ------------------------------------------------------------
# 工具函数：按端口查找绑定的 Pid，避免误杀系统中同名的其他 dotnet/node 进程
# ------------------------------------------------------------
function Get-PidsByPort([int]$Port) {
    try {
        $lines = netstat -ano | Select-String -Pattern "\s+LISTENING\s+"
    } catch { return @() }
    $pids = @()
    foreach ($l in $lines) {
        if ($l.Line -match ":$Port\s+.*LISTENING\s+(\d+)$") { $pids += [int]$Matches[1] }
    }
    if ($pids.Count -eq 0) { return @() }
    return ($pids | Select-Object -Unique)
}

function Stop-ByPort([int]$Port, [string]$Label) {
    $pids = Get-PidsByPort $Port
    if ($pids.Count -eq 0) {
        Write-Host ("  - {0} 端口 {1} 无监听进程" -f $Label, $Port) -ForegroundColor Gray
        return
    }
    foreach ($pidNum in $pids) {
        $proc = Get-Process -Id $pidNum -ErrorAction SilentlyContinue
        $procName = if ($proc) { $proc.ProcessName } else { "(pid=$pidNum 已退出)" }
        Write-Host ("  检测到 {0} 端口 {1} 被 Pid={2} ({3}) 占用，停止中..." -f $Label, $Port, $pidNum, $procName) -ForegroundColor Gray
        if ($proc) {
            try { Stop-Process -Id $pidNum -Force -ErrorAction Stop }
            catch { try { taskkill /F /PID $pidNum /T | Out-Null } catch { } }
        }
    }
    Start-Sleep -Milliseconds 600
    $remain = Get-PidsByPort $Port
    if ($remain.Count -gt 0) {
        Write-Host ("  [!] {0} 端口 {1} 仍被占用：Pid={2}" -f $Label, $Port, ($remain -join ",")) -ForegroundColor Red
    } else {
        Write-Host ("  [OK] {0} 端口 {1} 已释放" -f $Label, $Port) -ForegroundColor Green
    }
}

# ------------------------------------------------------------
# 1) 停止现有进程（只清理占用 5008 / 5171 的进程，Nginx 不在开发环境管理范围）
# ------------------------------------------------------------
Write-Host "[1/3] 停止现有服务（仅清理 5008 / 5171 绑定的进程，Nginx 不处理）..." -ForegroundColor Yellow

Stop-ByPort 5008 "后端 dotnet"
Stop-ByPort 5171 "前端 node"

Start-Sleep -Seconds 2

# ------------------------------------------------------------
# 2) 启动后端（端口 5008）
# ------------------------------------------------------------
Write-Host "[2/3] 启动后端 (端口 5008)..." -ForegroundColor Yellow

# 构建环境变量设置命令（cmd 方式，兼容 PowerShell 5.x）
$envVars = @(
    "ASPNETCORE_ENVIRONMENT=Development",
    "ConnectionStrings__SqliteConnection=Data Source=data/noneadcss.db;Mode=ReadWrite"
)
$userVars = @("JWT__Key", "ENCRYPTION__Key", "AUDIT__HmacKey", "APP__SatisfactionTokenKey")
foreach ($v in $userVars) {
    $val = [Environment]::GetEnvironmentVariable($v, "User")
    if ($val) { $envVars += "$v=$val" }
}
$setCmd = ($envVars | ForEach-Object { "set `"$_`"" }) -join " && "

Start-Process -FilePath "cmd" `
    -ArgumentList "/c $setCmd && dotnet run --no-build --no-launch-profile --urls `"http://127.0.0.1:5008`"" `
    -WorkingDirectory $ApiDir `
    -WindowStyle Minimized

Write-Host "  [OK] 后端已启动" -ForegroundColor Green

# ------------------------------------------------------------
# 3) 启动前端（端口 5171）
# ------------------------------------------------------------
Write-Host "[3/3] 启动前端 (端口 5171)..." -ForegroundColor Yellow

Start-Process -FilePath "cmd" `
    -ArgumentList "/c npx vite --port 5171 --host" `
    -WorkingDirectory $FrontendDir `
    -WindowStyle Minimized

Write-Host "  [OK] 前端已启动" -ForegroundColor Green

# ------------------------------------------------------------
# 等待服务就绪
# ------------------------------------------------------------
Write-Host ""
Write-Host "等待服务就绪..." -ForegroundColor Yellow

$maxWait = 40
$backendReady = $false
$frontendReady = $false

for ($i = 1; $i -le $maxWait; $i++) {
    Start-Sleep -Seconds 1
    if (-not $backendReady) {
        try {
            $r = Invoke-WebRequest -Uri "http://127.0.0.1:5008/health" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
            if ($r.StatusCode -eq 200) { $backendReady = $true; Write-Host "  [OK] 后端就绪 (5008)" -ForegroundColor Green }
        } catch { }
    }
    if (-not $frontendReady) {
        try {
            $r = Invoke-WebRequest -Uri "http://127.0.0.1:5171" -UseBasicParsing -TimeoutSec 2 -ErrorAction Stop
            if ($r.StatusCode -eq 200) { $frontendReady = $true; Write-Host "  [OK] 前端就绪 (5171)" -ForegroundColor Green }
        } catch { }
    }
    if ($backendReady -and $frontendReady) { break }
}

Write-Host ""
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  开发环境重启完成" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  前端 : http://localhost:5171" -ForegroundColor White
Write-Host "  后端 : http://localhost:5008" -ForegroundColor White
Write-Host ""

if (-not $backendReady) { Write-Host "  [!] 后端未就绪，请检查 dotnet 进程" -ForegroundColor Red }
if (-not $frontendReady) { Write-Host "  [!] 前端未就绪，请检查 node 进程" -ForegroundColor Red }
