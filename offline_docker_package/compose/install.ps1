# ============================================================
# NoneadCSS 离线安装包 - 一键安装脚本 (Windows / PowerShell)
#
# 用法（在 compose 目录下执行）：
#   powershell.exe -ExecutionPolicy Bypass -File .\install.ps1
#   powershell.exe -ExecutionPolicy Bypass -File .\install.ps1 -Port 80
#
# 流程：
#   1. 检查 Docker 是否可用
#   2. docker load 加载离线镜像（../images/noneadcss-latest.tar）
#   3. 若 .env 不存在，则从 .env.example 生成并填充随机密钥
#   4. docker compose up -d 启动服务
#   5. 等待健康检查通过并打印访问信息
#
# 修复说明（v2.2.12）：docker 命令的 stderr（如 compose 对已存在 volume 的
# warning）在 $ErrorActionPreference='Stop' 下会被 PowerShell 5.1 包装为
# NativeCommandError 直接中断脚本。统一经 Invoke-Docker 封装执行，
# 将 stderr 降级为普通输出行、仅以退出码判定成败。
# ============================================================

param(
    [int]$Port = 0   # 0 = 使用 .env 中的 HOST_PORT
)

$ErrorActionPreference = 'Stop'
$ComposeDir = $PSScriptRoot
$ImagesDir  = Join-Path (Split-Path -Parent $ComposeDir) 'images'
$ImageTar   = Join-Path $ImagesDir 'noneadcss-latest.tar'

function Write-Step([string]$m) { Write-Host "[INSTALL] $m" -ForegroundColor Cyan }
function Write-Ok([string]$m)   { Write-Host "  [OK]   $m" -ForegroundColor Green }
function Write-Err([string]$m)  { Write-Host "  [ERR]  $m" -ForegroundColor Red }

# ============================================================
# docker 命令执行辅助：
#   - 临时降级 $ErrorActionPreference，使 stderr（warning/进度条）不触发
#     NativeCommandError（PowerShell 5.1 下 $ErrorActionPreference='Stop'
#     会把原生命令任何 stderr 输出当错误中断脚本）
#   - 返回 { Lines = 文本行数组（stdout+stderr 合并，ErrorRecord 已还原为文本）;
#            ExitCode = docker 进程退出码 }
# ============================================================
function Invoke-Docker {
    param([string[]]$Arguments)
    $prevEA = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & docker @Arguments 2>&1
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prevEA
    }
    $lines = @()
    foreach ($o in $output) {
        if ($o -is [System.Management.Automation.ErrorRecord]) { $lines += [string]$o.Exception.Message }
        else { $lines += [string]$o }
    }
    [pscustomobject]@{ Lines = $lines; ExitCode = $code }
}

# ---------- 1. 检查 Docker ----------
Write-Step '检查 Docker 环境...'
$ver = Invoke-Docker @('version','--format','{{.Server.Version}}')
if ($ver.ExitCode -ne 0 -or -not $ver.Lines) {
    Write-Err "Docker 不可用。请先安装 Docker Desktop 并确保引擎已启动。"
    $ver.Lines | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    exit 1
}
Write-Ok "Docker 可用 (Server=$($ver.Lines -join ''))"

# ---------- 2. 加载离线镜像 ----------
Write-Step '加载离线镜像...'
if (-not (Test-Path $ImageTar)) {
    Write-Err "未找到镜像文件: $ImageTar"
    Write-Err "请确认 images/noneadcss-latest.tar 存在，或运行 pack.ps1 重新打包。"; exit 1
}
$load = Invoke-Docker @('load','-i',$ImageTar)
$load.Lines | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
if ($load.ExitCode -ne 0) { Write-Err "镜像加载失败 (exit=$($load.ExitCode))"; exit 1 }
Write-Ok '镜像加载完成'

# ---------- 3. 生成 .env ----------
Push-Location $ComposeDir
try {
    if (-not (Test-Path (Join-Path $ComposeDir '.env'))) {
        Write-Step '首次安装：生成 .env 及随机密钥...'
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ComposeDir 'generate-secrets.ps1')
        if ($LASTEXITCODE -ne 0) { Write-Err "密钥生成失败 (exit=$LASTEXITCODE)"; exit 1 }
        Write-Ok '.env 已生成（请记录上方管理员账号密码！）'
    } else {
        Write-Ok '.env 已存在，跳过密钥生成'
    }
} finally { Pop-Location }

# ---------- 3.5 初始化数据卷（首次安装：将 fresh_schema.db 种子库写入命名卷） ----------
$SeedDb  = Join-Path (Split-Path -Parent $ComposeDir) 'data\fresh_schema.db'
$volName = 'noneadcss-data'
$volList = Invoke-Docker @('volume','ls','--format','{{.Name}}')
$volExists = $volList.Lines | Where-Object { $_ -eq $volName }
if (-not $volExists) {
    Write-Step "初始化数据卷 $volName ..."
    if (-not (Test-Path $SeedDb)) {
        Write-Err "未找到种子库文件: $SeedDb （离线包缺少 data/fresh_schema.db）"; exit 1
    }
    $vc = Invoke-Docker @('volume','create',$volName)
    $vc.Lines | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    if ($vc.ExitCode -ne 0) { Write-Err "数据卷创建失败 (exit=$($vc.ExitCode))"; exit 1 }
    # 用已加载的应用镜像做一次性搬运（镜像内含 cp/sh，无需额外镜像）
    $seedArgs = @('run','--rm','-v',"${volName}:/app/Data",'-v',"${SeedDb}:/seed/fresh_schema.db:ro",'--entrypoint','/bin/sh','noneadcss:latest','-c','cp /seed/fresh_schema.db /app/Data/noneadcss.db && chown -R 999:999 /app/Data')
    $seed = Invoke-Docker $seedArgs
    $seed.Lines | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    if ($seed.ExitCode -ne 0) { Write-Err "数据卷初始化失败 (exit=$($seed.ExitCode))"; exit 1 }
    Write-Ok "数据卷已初始化（空库种子，首次启动自动迁移并创建管理员）"
} else {
    Write-Ok "数据卷 $volName 已存在，跳过初始化（如需重装请先删除该卷）"
}

# ---------- 4. 启动服务 ----------
$hostPort = if ($Port -gt 0) { $Port } else { 0 }
Write-Step '启动服务 (docker compose up -d)...'
Push-Location $ComposeDir
try {
    # 通过进程级环境变量覆盖 HOST_PORT（compose 优先读 shell 环境变量，其次读 .env）
    if ($hostPort -gt 0) { $env:HOST_PORT = "$hostPort" }
    # 注意：此处 stderr 中如 "volume already exists" 的 warning 属正常提示，
    # 经 Invoke-Docker 处理后不会中断脚本；真正失败以退出码为准。
    $up = Invoke-Docker @('compose','--env-file','.env','up','-d')
    $up.Lines | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    if ($up.ExitCode -ne 0) { Write-Err "docker compose up 失败 (exit=$($up.ExitCode))"; exit 1 }
} finally { Pop-Location }

$container = 'noneadcss-prod'
Start-Sleep -Seconds 5
$insp = Invoke-Docker @('inspect',$container,'--format','{{.State.Status}}')
$status = if ($insp.ExitCode -eq 0) { $insp.Lines -join '' } else { '' }
if ($status -ne 'running') {
    Write-Err "容器未运行 (status=$status)。最近日志："
    $logs = Invoke-Docker @('logs',$container)
    $logs.Lines | Select-Object -Last 30 | ForEach-Object { Write-Err "  $_" }
    exit 1
}
Write-Ok "容器 $container 已运行"

# ---------- 5. 等待健康检查 ----------
$port = $hostPort
if ($port -eq 0) {
    $envVars = @{}
    Get-Content (Join-Path $ComposeDir '.env') | ForEach-Object {
        $line = $_.Trim()
        if ($line -and -not $line.StartsWith('#') -and $line.Contains('=')) {
            $i = $line.IndexOf('='); $envVars[$line.Substring(0,$i).Trim()] = $line.Substring($i+1).Trim()
        }
    }
    $port = if ($envVars['HOST_PORT']) { $envVars['HOST_PORT'] } else { 5173 }
}

Write-Step "等待健康检查（最多 120 秒，端口 $port）..."
$healthy = $false
for ($i = 0; $i -lt 120; $i++) {
    Start-Sleep -Seconds 1
    $st = Invoke-Docker @('inspect',$container,'--format','{{.State.Status}}')
    $state = if ($st.ExitCode -eq 0) { $st.Lines -join '' } else { '' }
    if ($state -ne 'running') {
        Write-Err "容器异常退出 (status=$state)"
        $logs = Invoke-Docker @('logs',$container)
        $logs.Lines | Select-Object -Last 20 | ForEach-Object { Write-Err "  $_" }
        exit 1
    }
    $he = Invoke-Docker @('inspect',$container,'--format','{{.State.Health.Status}}')
    $health = if ($he.ExitCode -eq 0) { $he.Lines -join '' } else { '' }
    if ($health -eq 'healthy') { $healthy = $true; break }
}
if ($healthy) { Write-Ok '健康检查通过' } else { Write-Warning '120 秒内未通过健康检查（可能仍在迁移数据库）' }

Write-Host ''
Write-Host '============================================================' -ForegroundColor Green
Write-Host ' NoneadCSS 离线部署完成'                                     -ForegroundColor Green
Write-Host '============================================================' -ForegroundColor Green
Write-Host "  Web 应用 : http://127.0.0.1:$port/"                        -ForegroundColor Green
Write-Host "  健康检查 : http://127.0.0.1:$port/health"                   -ForegroundColor Green
Write-Host "  日志     : docker logs -f $container"                       -ForegroundColor Green
Write-Host '  停止     : docker compose --env-file .env -f docker-compose.yml down' -ForegroundColor DarkGray
Write-Host '============================================================' -ForegroundColor Green
Write-Host ''
