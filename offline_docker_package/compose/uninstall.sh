#!/usr/bin/env bash
# ============================================================
# NoneadCSS 离线安装包 - 停止并移除服务 (Linux / macOS)
#
# 用法：
#   ./uninstall.sh            # 仅停止容器
#   ./uninstall.sh --purge    # 同时删除数据卷（数据将丢失！）
# ============================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PURGE="${1:-}"

echo -e "\033[36m[UNINSTALL]\033[0m 停止服务..."
cd "$SCRIPT_DIR"
docker compose --env-file .env down 2>&1 | sed 's/^/  /'
echo -e "  \033[32m[OK]\033[0m 服务已停止"

if [ "$PURGE" = "--purge" ]; then
  echo -e "\033[36m[UNINSTALL]\033[0m 删除数据卷 noneadcss-data（所有数据将永久丢失！）..."
  docker volume rm noneadcss-data 2>&1 | sed 's/^/  /'
  echo -e "  \033[32m[OK]\033[0m 数据卷已删除"
else
  echo -e "  提示: 数据保留在命名卷 noneadcss-data 中。如需彻底清除请加 --purge 参数。"
fi
