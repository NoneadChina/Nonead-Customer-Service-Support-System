# ============================================================
# NoneadCSS 离线包 - 覆盖安装脚本 (Windows / PowerShell 5.1 兼容)
#
# 用途：将本目录（修复后的 compose 源）中的 install.ps1、docker-compose.yml、
#       .env.example、generate-secrets.ps1 覆盖到目标机安装现场，修复：
#         - install.ps1 无 UTF-8 BOM，PowerShell 5.1 按 GBK 解析中文
#           导致 ParserError（"&&" 误判、"字符串缺少终止符" 等）
#         - docker-compose.yml 未声明 external volume，compose 输出
#           "volume already exists" warning 被 NativeCommandError 误中断
#           （>= 32 位非空）失败导致崩溃循环；generate-secrets.ps1 现已
#           自动生成随机占位，重新生成 .env 即可修复
#
# 用法（在本目录下执行）：
#   powershell.exe -ExecutionPolicy Bypass -File .\update-compose.ps1
#     # 默认覆盖脚本自身所在目录（可用 -TargetDir 指定其它部署目录）
#   powershell.exe -ExecutionPolicy Bypass -File .\update-compose.ps1 -TargetDir "<你的部署目录>\compose"
# ============================================================

param(
    [string]$TargetDir = $PSScriptRoot
)

$ErrorActionPreference = 'Stop'
$SourceDir = $PSScriptRoot
$Files = @('install.ps1', 'docker-compose.yml', '.env.example', 'generate-secrets.ps1')

function Write-Step([string]$m) { Write-Host "[UPDATE] $m" -ForegroundColor Cyan }
function Write-Ok([string]$m)   { Write-Host "  [OK]   $m" -ForegroundColor Green }
function Write-Err([string]$m)  { Write-Host "  [ERR]  $m" -ForegroundColor Red }

Write-Step "源目录: $SourceDir"
Write-Step "目标目录: $TargetDir"

# ---------- 1. 校验源文件 ----------
foreach ($f in $Files) {
    if (-not (Test-Path (Join-Path $SourceDir $f))) {
        Write-Err "源文件缺失（请确认本脚本与 install.ps1 / docker-compose.yml 在同一目录）: $(Join-Path $SourceDir $f)"
        exit 1
    }
}
Write-Ok '源文件齐全'

# ---------- 2. 创建目标目录 ----------
if (-not (Test-Path $TargetDir)) {
    Write-Step "目标目录不存在，创建: $TargetDir"
    New-Item -ItemType Directory -Force -Path $TargetDir | Out-Null
}

# ---------- 3. 覆盖复制 ----------
foreach ($f in $Files) {
    $src = Join-Path $SourceDir $f
    $dst = Join-Path $TargetDir $f
    Copy-Item -Path $src -Destination $dst -Force
    Write-Ok "已覆盖: $dst"
}

# ---------- 4. 覆盖后校验 ----------
$p = Join-Path $TargetDir 'install.ps1'
$bytes = [System.IO.File]::ReadAllBytes($p)
$bom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
if (-not $bom) {
    Write-Err "警告: install.ps1 缺少 UTF-8 BOM（PowerShell 5.1 可能解析失败）"
} else {
    Write-Ok 'install.ps1 已含 UTF-8 BOM（PowerShell 5.1 兼容）'
}

$gs = Join-Path $TargetDir 'generate-secrets.ps1'
$gBytes = [System.IO.File]::ReadAllBytes($gs)
$gBom = ($gBytes.Length -ge 3 -and $gBytes[0] -eq 0xEF -and $gBytes[1] -eq 0xBB -and $gBytes[2] -eq 0xBF)
if (-not $gBom) {
    Write-Err "警告: generate-secrets.ps1 缺少 UTF-8 BOM（PowerShell 5.1 可能解析失败）"
} else {
    Write-Ok 'generate-secrets.ps1 已含 UTF-8 BOM（PowerShell 5.1 兼容）'
}

$compose = Get-Content -Raw (Join-Path $TargetDir 'docker-compose.yml')
if ($compose -match 'external:\s*true') {
    Write-Ok 'docker-compose.yml 已声明 external volume（不再输出 warning）'
} else {
    Write-Err '警告: docker-compose.yml 未找到 external: true'
}

$example = Get-Content -Raw (Join-Path $TargetDir '.env.example')
if ($example -match 'APP__SatisfactionTokenKey=<') {
    Write-Ok '.env.example 已含凭据占位符（generate-secrets 将自动生成随机密钥）'
} else {
    Write-Err '警告: .env.example 未找到凭据占位符'
}

# ---------- 5. 完成提示 ----------
Write-Host ''
Write-Host '============================================================' -ForegroundColor Green
Write-Host ' 覆盖完成。请进入目标目录重新执行安装：'                      -ForegroundColor Green
Write-Host "   cd $TargetDir"                                             -ForegroundColor Gray
Write-Host '   powershell.exe -ExecutionPolicy Bypass -File .\install.ps1' -ForegroundColor Gray
Write-Host '   [重要] 若此前安装已生成过 .env（凭据占位符未替换），' -ForegroundColor Yellow
Write-Host '          请先删除 .env 再运行 install.ps1，让 generate-secrets 重新生成：' -ForegroundColor Yellow
Write-Host '          Remove-Item .env; powershell.exe -ExecutionPolicy Bypass -File .\install.ps1' -ForegroundColor Gray
Write-Host '============================================================' -ForegroundColor Green
