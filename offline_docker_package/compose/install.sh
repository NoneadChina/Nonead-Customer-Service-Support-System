#!/usr/bin/env bash
# ============================================================
# NoneadCSS 离线安装包 - 一键安装脚本 (Linux / macOS)
#
# 用法（在 compose 目录下执行）：
#   chmod +x install.sh
#   ./install.sh
#   PORT=80 ./install.sh          # 指定宿主端口
#
# 流程：
#   1. 检查 Docker 是否可用
#   2. docker load 加载离线镜像（../images/noneadcss-latest.tar）
#   3. 若 .env 不存在，则从 .env.example 生成并填充随机密钥
#   4. docker compose up -d 启动服务
#   5. 等待健康检查通过并打印访问信息
# ============================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
IMAGES_DIR="$(dirname "$SCRIPT_DIR")/images"
IMAGE_TAR="$IMAGES_DIR/noneadcss-latest.tar"
CONTAINER="noneadcss-prod"
PORT="${PORT:-0}"

step() { echo -e "\033[36m[INSTALL]\033[0m $1"; }
ok()   { echo -e "  \033[32m[OK]\033[0m   $1"; }
err()  { echo -e "  \033[31m[ERR]\033[0m  $1" >&2; }

# ---------- 1. 检查 Docker ----------
step "检查 Docker 环境..."
if ! command -v docker >/dev/null 2>&1; then
  err "未找到 docker 命令。请先安装 Docker Engine / Docker Desktop。"; exit 1
fi
if ! docker info >/dev/null 2>&1; then
  err "Docker 引擎未运行（或当前用户无权限访问）。"; exit 1
fi
ok "Docker 可用 ($(docker version --format '{{.Server.Version}}'))"

# ---------- 2. 加载离线镜像 ----------
step "加载离线镜像..."
if [ ! -f "$IMAGE_TAR" ]; then
  err "未找到镜像文件: $IMAGE_TAR"
  err "请确认 images/noneadcss-latest.tar 存在。"; exit 1
fi
docker load -i "$IMAGE_TAR" | sed 's/^/  /'
ok "镜像加载完成"

# ---------- 3. 生成 .env ----------
if [ ! -f "$SCRIPT_DIR/.env" ]; then
  step "首次安装：生成 .env 及随机密钥..."
  if [ ! -x "$SCRIPT_DIR/generate-secrets.sh" ]; then chmod +x "$SCRIPT_DIR/generate-secrets.sh"; fi
  bash "$SCRIPT_DIR/generate-secrets.sh"
  ok ".env 已生成（请记录上方管理员账号密码！）"
else
  ok ".env 已存在，跳过密钥生成"
fi

# ---------- 3.5 初始化数据卷（首次安装：将 fresh_schema.db 种子库写入命名卷） ----------
SEED_DB="$(dirname "$SCRIPT_DIR")/data/fresh_schema.db"
VOL_NAME="noneadcss-data"
if ! docker volume ls --format '{{.Name}}' | grep -qx "$VOL_NAME"; then
  step "初始化数据卷 $VOL_NAME ..."
  if [ ! -f "$SEED_DB" ]; then
    err "未找到种子库文件: $SEED_DB （离线包缺少 data/fresh_schema.db）"; exit 1
  fi
  docker volume create "$VOL_NAME" | sed 's/^/  /'
  # 用已加载的应用镜像做一次性搬运（镜像内含 cp/sh，无需额外镜像）
  docker run --rm \
    -v "$VOL_NAME:/app/Data" \
    -v "$SEED_DB:/seed/fresh_schema.db:ro" \
    --entrypoint /bin/sh noneadcss:latest \
    -c 'cp /seed/fresh_schema.db /app/Data/noneadcss.db && chown -R 999:999 /app/Data' | sed 's/^/  /'
  ok "数据卷已初始化（空库种子，首次启动自动迁移并创建管理员）"
else
  ok "数据卷 $VOL_NAME 已存在，跳过初始化（如需重装请先删除该卷）"
fi

# ---------- 4. 启动服务 ----------
step "启动服务 (docker compose up -d)..."
cd "$SCRIPT_DIR"
if [ "$PORT" -gt 0 ]; then export HOST_PORT="$PORT"; fi
docker compose --env-file .env up -d 2>&1 | sed 's/^/  /'
ok "docker compose up 完成"

# ---------- 5. 等待健康检查 ----------
PORT_FINAL="$PORT"
if [ -z "$PORT_FINAL" ] || [ "$PORT_FINAL" = "0" ]; then
  PORT_FINAL=$(grep -E '^HOST_PORT=' "$SCRIPT_DIR/.env" | tail -1 | cut -d= -f2)
fi
[ -z "$PORT_FINAL" ] && PORT_FINAL=5173

step "等待健康检查（最多 120 秒，端口 $PORT_FINAL）..."
HEALTHY=0
for i in $(seq 1 120); do
  sleep 1
  STATUS=$(docker inspect "$CONTAINER" --format '{{.State.Status}}' 2>/dev/null || echo unknown)
  if [ "$STATUS" != "running" ]; then
    err "容器异常退出 (status=$STATUS)。最近日志："
    docker logs "$CONTAINER" 2>&1 | tail -20 | sed 's/^/  /'
    exit 1
  fi
  H=$(docker inspect "$CONTAINER" --format '{{.State.Health.Status}}' 2>/dev/null || echo starting)
  if [ "$H" = "healthy" ]; then HEALTHY=1; break; fi
done
if [ "$HEALTHY" = "1" ]; then ok "健康检查通过"; else echo "  警告: 120 秒内未通过健康检查（可能仍在迁移数据库）"; fi

echo ""
echo "============================================================"
echo " NoneadCSS 离线部署完成"
echo "============================================================"
echo "  Web 应用 : http://127.0.0.1:$PORT_FINAL/"
echo "  健康检查 : http://127.0.0.1:$PORT_FINAL/health"
echo "  日志     : docker logs -f $CONTAINER"
echo "  停止     : cd \"$SCRIPT_DIR\" && docker compose --env-file .env down"
echo "============================================================"
echo ""
