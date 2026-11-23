# ============================================================
# NoneadCSS 离线安装包 - 停止并移除服务 (Windows / PowerShell)
#
# 用法：
#   powershell.exe -ExecutionPolicy Bypass -File .\uninstall.ps1          # 仅停止容器
#   powershell.exe -ExecutionPolicy Bypass -File .\uninstall.ps1 -Purge   # 同时删除数据卷（数据将丢失！）
# ============================================================

param(
    [switch]$Purge
)

$ErrorActionPreference = 'Stop'
$ComposeDir = $PSScriptRoot

Write-Host "[UNINSTALL] 停止服务..." -ForegroundColor Cyan
Push-Location $ComposeDir
try {
    docker compose --env-file .env down 2>&1 | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
} finally { Pop-Location }
Write-Host "  [OK] 服务已停止" -ForegroundColor Green

if ($Purge) {
    Write-Host "[UNINSTALL] 删除数据卷 noneadcss-data（所有数据将永久丢失！）..." -ForegroundColor Cyan
    docker volume rm noneadcss-data 2>&1 | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    Write-Host "  [OK] 数据卷已删除" -ForegroundColor Green
} else {
    Write-Host "  提示: 数据保留在命名卷 noneadcss-data 中。如需彻底清除请加 -Purge 参数。" -ForegroundColor Yellow
}
