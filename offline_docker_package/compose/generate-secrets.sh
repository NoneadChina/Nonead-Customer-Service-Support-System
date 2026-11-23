#!/usr/bin/env bash
# ============================================================
# NoneadCSS 离线安装包 - 密钥生成脚本（Linux/macOS）
# 基于同目录 .env.example 生成 .env 并填充安全随机密钥。
# 字符集不含 $ 与 #，避免 docker compose 变量插值破坏。
#
# 用法：
#   ./generate-secrets.sh                          # 生成全新密钥 + 默认管理员
#   ./generate-secrets.sh admin@example.com        # 指定管理员邮箱
#   ./generate-secrets.sh admin@example.com 'Passw0rd!Xyz'
# ============================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ENV_FILE="$SCRIPT_DIR/.env"
EXAMPLE_FILE="$SCRIPT_DIR/.env.example"

if [ ! -f "$EXAMPLE_FILE" ]; then
  echo "[ERROR] 未找到模板文件: $EXAMPLE_FILE" >&2
  exit 1
fi

# 生成 n 字节 Base64 随机值（去除 base64 的 76 字符换行，兼容 macOS/Linux）
rand_b64() {
  local n="$1"
  head -c "$n" /dev/urandom | base64 | tr -d '\n'
}

# 生成 n 字符 ASCII 随机值（字符集不含 $ 与 #）
rand_ascii() {
  local n="$1"
  local chars='ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_+-=%&@!?'
  local out=""
  for _ in $(seq 1 "$n"); do
    out+="${chars:$(( RANDOM % ${#chars} )):1}"
  done
  printf '%s' "$out"
}

# 注意：$RANDOM 精度有限，这里用 urandom 增强随机性
# 注意：tr 中 `-` 必须放在字符集末尾，否则会被解释为范围（如 +- 会吞进 < 和 >）
# head -c 提前关闭管道会导致 tr 收到 SIGPIPE(141)，在 set -e + pipefail 下需 || true
rand_ascii_strong() {
  local n="$1"
  tr -dc 'A-Za-z0-9_+=%&@!?-' < /dev/urandom | head -c "$n" || true
}

JWT_KEY="$(rand_b64 64)"
HASHER_KEY="$(rand_b64 64)"
AES_KEY="$(rand_b64 32)"
AUDIT_KEY="$(rand_ascii_strong 72)"
SATISFACTION_KEY="$(rand_ascii_strong 64)"

ADMIN_EMAIL="${1:-admin@example.com}"
if [ $# -ge 2 ]; then
  ADMIN_PASSWORD="$2"
else
  ADMIN_PASSWORD="$(rand_ascii_strong 24)A1!"
fi

cp "$EXAMPLE_FILE" "$ENV_FILE"

# 密钥值通过环境变量传给 perl，避免值中的 @ $ 等字符被 perl 源码二次解析
export NG_JWT="$JWT_KEY" NG_HASHER="$HASHER_KEY" NG_AES="$AES_KEY" \
       NG_AUDIT="$AUDIT_KEY" NG_SATISFACTION="$SATISFACTION_KEY" \
       NG_ADMIN_EMAIL="$ADMIN_EMAIL" NG_ADMIN_PASSWORD="$ADMIN_PASSWORD"

# 用 perl 做多占位符替换（单引号 + $ENV{} 引用，值内特殊字符不会被误解析）
perl -0pi \
  -e 's{<REPLACE_WITH_64BYTES_BASE64_RANDOM>}{PLACEHOLDER_JWT_HASHER}g;' \
  -e 's{JWT__Key=PLACEHOLDER_JWT_HASHER}{JWT__Key=$ENV{NG_JWT}}g;' \
  -e 's{SECRET_HASHER__Key=PLACEHOLDER_JWT_HASHER}{SECRET_HASHER__Key=$ENV{NG_HASHER}}g;' \
  -e 's{<REPLACE_WITH_32BYTES_BASE64_RANDOM>}{$ENV{NG_AES}}g;' \
  -e 's{<REPLACE_WITH_64CHARS_ASCII_RANDOM>}{$ENV{NG_AUDIT}}g;' \
  -e 's{<REPLACE_WITH_AT_LEAST_32CHARS_RANDOM>}{PLACEHOLDER_32CHARS}g;' \
  -e 's{APP__SatisfactionTokenKey=PLACEHOLDER_32CHARS}{APP__SatisfactionTokenKey=$ENV{NG_SATISFACTION}}g;' \
  -e 's{ADMIN_EMAIL=admin\@nonead\.com}{ADMIN_EMAIL=$ENV{NG_ADMIN_EMAIL}}g;' \
  -e 's{ADMIN_PASSWORD=<REPLACE_WITH_STRONG_PASSWORD>}{ADMIN_PASSWORD=$ENV{NG_ADMIN_PASSWORD}}g' \
  "$ENV_FILE"

chmod 600 "$ENV_FILE"

echo "管理员账号: $ADMIN_EMAIL"
echo "管理员密码: $ADMIN_PASSWORD"
echo "[OK] 已生成安全随机密钥写入 $ENV_FILE"
echo "[WARN] 请立即记录以上管理员密码！"
