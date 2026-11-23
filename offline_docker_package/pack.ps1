# ============================================================
# NoneadCSS 离线安装包 - 打包脚本 (Windows / PowerShell)
# 在仓库根目录执行：powershell -ExecutionPolicy Bypass -File "Offline Docker\pack.ps1"
#
# 功能：
#   1. 检查本地镜像 noneadcss:latest 是否存在
#   2. docker save 导出镜像为 images/noneadcss-latest.tar
#   3. 从运行中的生产容器（noneadcss-prod）导出「空数据 + 当前完整 schema」
#      的种子库 data/fresh_schema.db（离线安装首次启动用，绕过 EF 迁移链
#      无法从空库 bootstrap 的历史漂移问题）
# ============================================================

$ErrorActionPreference = 'Stop'

$RepoRoot   = Split-Path -Parent $PSScriptRoot          # C:\noneadcss
$OfflineDir = $PSScriptRoot                              # C:\noneadcss\Offline Docker
$ImagesDir  = Join-Path $OfflineDir 'images'
$DataDir    = Join-Path $OfflineDir 'data'
$ImageTar   = Join-Path $ImagesDir 'noneadcss-latest.tar'
$SeedDb     = Join-Path $DataDir 'fresh_schema.db'
$ImageTag   = 'noneadcss:latest'

function Write-Step([string]$m) { Write-Host "[PACK] $m" -ForegroundColor Cyan }
function Write-Ok([string]$m)   { Write-Host "  [OK]   $m" -ForegroundColor Green }
function Write-Err([string]$m)  { Write-Host "  [ERR]  $m" -ForegroundColor Red }

New-Item -ItemType Directory -Force -Path $ImagesDir, $DataDir | Out-Null

# ---------- 1. 检查镜像 ----------
Write-Step "检查镜像 $ImageTag ..."
$img = docker images $ImageTag --format '{{.Repository}}:{{.Tag}}' 2>$null
if (-not $img) {
    Write-Err "本地不存在镜像 $ImageTag 。请先构建："
    Write-Err "  cd $RepoRoot"
    Write-Err "  docker build -t noneadcss:latest -f Docker/Dockerfile.prod ."
    exit 1
}
Write-Ok "找到镜像 $ImageTag"

# ---------- 2. 导出镜像 ----------
Write-Step "导出镜像 -> $ImageTar (可能需要数分钟)..."
if (Test-Path $ImageTar) { Remove-Item $ImageTar -Force }
docker save $ImageTag -o $ImageTar
if ($LASTEXITCODE -ne 0) { Write-Err "docker save 失败 (exit=$LASTEXITCODE)"; exit 1 }
$sizeMB = [math]::Round((Get-Item $ImageTar).Length / 1MB, 1)
Write-Ok "导出完成 ($sizeMB MB)"

# ---------- 3. 生成空库种子（从运行中的生产容器复制 schema） ----------
Write-Step "生成空库种子 data/fresh_schema.db ..."
$running = docker ps --filter "name=noneadcss-prod" --format '{{.Names}}' 2>$null
if (-not $running) {
    Write-Err "生产容器 noneadcss-prod 未运行，跳过种子库生成。"
    Write-Err "请先启动生产容器后再运行 pack.ps1，或手动用 build_fresh_schema 流程生成。"
} else {
    $tmpDir = Join-Path $RepoRoot '_pack_seed_tmp'
    New-Item -ItemType Directory -Force -Path $tmpDir | Out-Null
    docker cp "${running}:/app/Data/noneadcss.db" (Join-Path $tmpDir 'noneadcss.db') | Out-Null
    docker cp "${running}:/app/Data/noneadcss.db-wal" (Join-Path $tmpDir 'noneadcss.db-wal') | Out-Null
    # 用 Python 合并 WAL 并生成空库种子（临时脚本）
    $py = @'
import sqlite3, os, sys, shutil

tmp = sys.argv[1]
src_db = os.path.join(tmp, "noneadcss.db")
dst = sys.argv[2]

con = sqlite3.connect(src_db)
con.execute("PRAGMA wal_checkpoint(TRUNCATE)")
con.commit()
con.close()
for suf in ("-wal", "-shm"):
    p = src_db + suf
    if os.path.exists(p):
        os.remove(p)

if os.path.exists(dst):
    os.remove(dst)
src = sqlite3.connect(src_db)
dstc = sqlite3.connect(dst)
dstc.execute("PRAGMA journal_mode=DELETE")
dstc.execute("PRAGMA foreign_keys=OFF")
ddls = src.execute(
    "SELECT type, name, sql FROM sqlite_master WHERE sql IS NOT NULL AND name NOT IN ('sqlite_sequence') "
    "ORDER BY CASE type WHEN 'table' THEN 1 WHEN 'index' THEN 2 WHEN 'trigger' THEN 3 ELSE 4 END, name"
).fetchall()
for typ, name, sql in ddls:
    try:
        dstc.execute(sql)
    except Exception:
        pass
hist = src.execute("SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory ORDER BY MigrationId").fetchall()
dstc.execute("DROP TABLE IF EXISTS __EFMigrationsHistory")
dstc.execute('''CREATE TABLE "__EFMigrationsHistory" ("MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY, "ProductVersion" TEXT NOT NULL)''')
dstc.executemany("INSERT INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES (?, ?)", hist)
dstc.commit()
print("seed ok, integrity=" + dstc.execute("PRAGMA integrity_check").fetchone()[0] + ", migrations=" + str(len(hist)))
dstc.close()
src.close()
'@
    $pyFile = Join-Path $tmpDir 'make_seed.py'
    Set-Content -Path $pyFile -Value $py -Encoding UTF8
    $out = python $pyFile $tmpDir $SeedDb 2>&1
    if ($LASTEXITCODE -ne 0) { Write-Err "种子库生成失败: $out"; exit 1 }
    $out | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    Remove-Item $tmpDir -Recurse -Force
    $seedMB = [math]::Round((Get-Item $SeedDb).Length / 1MB, 2)
    Write-Ok "种子库生成完成 ($seedMB MB)"
}

Write-Host ""
Write-Host "============================================================" -ForegroundColor Green
Write-Host " 离线安装包已更新: $OfflineDir"                                 -ForegroundColor Green
Write-Host " 镜像文件: $ImageTar ($sizeMB MB)"                              -ForegroundColor Green
Write-Host " 种子库:   $SeedDb"                                             -ForegroundColor Green
Write-Host " 目标机部署: 拷贝整个 Offline Docker 目录 -> 运行 install 脚本"  -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
