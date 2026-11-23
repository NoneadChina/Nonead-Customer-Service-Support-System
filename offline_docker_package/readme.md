# NoneadCSS 离线安装包

本目录是 **NoneadCSS** 客户服务系统的 Docker 离线安装包，适用于**目标服务器没有外网**（或无法访问 Docker Hub / 镜像仓库）的部署场景。所有镜像均已导出为本地文件，安装过程全程离线，无需联网拉取任何镜像。

---

## 一、包内文件说明

```
Offline Docker/
├── readme.md                      ← 本说明文档
├── pack.ps1                       ← 重新打包脚本（代码更新后重新导出镜像 + 生成空库种子）
├── images/
│   └── noneadcss-latest.tar       ← 应用镜像（约 480 MB，docker save 导出）
├── data/
│   └── fresh_schema.db            ← 空库种子（当前完整 schema + 全部迁移历史，无业务数据，约 1.2 MB）
└── compose/
    ├── docker-compose.yml         ← 离线版编排文件（无 build、无联网依赖）
    ├── .env.example               ← 环境变量模板（勿直接使用，请生成 .env）
    ├── generate-secrets.ps1       ← 密钥生成脚本（Windows）
    ├── generate-secrets.sh        ← 密钥生成脚本（Linux/macOS）
    ├── install.ps1                ← 一键安装脚本（Windows）
    ├── install.sh                 ← 一键安装脚本（Linux/macOS）
    ├── uninstall.ps1              ← 停止/卸载脚本（Windows）
    └── uninstall.sh               ← 停止/卸载脚本（Linux/macOS）
```

> 与在线版（`Docker/docker-compose.yml`）的区别：离线版**不执行镜像构建**、**不依赖宿主机 Production/data 路径**。首次启动前安装脚本会从 `data/fresh_schema.db` 初始化一个**空数据库**（含当前完整 schema 与全部迁移历史），应用启动时迁移检查无待应用项，随后自动创建管理员账号。

> ⚠️ 为什么需要空库种子：EF 迁移链存在历史漂移，无法从**完全空白**的数据库自举（在线版通过 `init-data` 从既有生产库复制规避）。离线包因此内置一份「空数据 + 当前 schema」种子库，保证全新机器可正常初始化。

---

## 二、目标服务器要求

| 项目 | 要求 |
|------|------|
| 操作系统 | Windows（Docker Desktop）或 Linux（Docker Engine），x86_64 |
| Docker | Docker 20.10+，`docker compose` v2 插件可用 |
| 端口 | 默认占用宿主机 **5173**（可通过 `.env` 的 `HOST_PORT` 修改） |
| 磁盘 | 至少 **2 GB** 可用空间（镜像约 480 MB + 数据卷） |
| 内存 | 建议 **2 GB** 以上 |

验证 Docker 环境：

```bash
docker version                # 客户端 + 服务端都正常
docker compose version        # 输出 v2.x
```

---

## 三、安装部署

### 方式 A：Linux / macOS（推荐用于服务器）

```bash
# 1. 将整个 Offline Docker 目录拷贝到目标服务器，然后进入 compose 目录
cd "Offline Docker/compose"

# 2. 赋予执行权限
chmod +x install.sh generate-secrets.sh uninstall.sh

# 3. 一键安装（自动完成：加载镜像 → 生成密钥 → 启动 → 健康检查）
./install.sh

# 如需指定端口（默认 5173）：
# PORT=8080 ./install.sh
```

### 方式 B：Windows

```powershell
# 1. 将整个 Offline Docker 目录拷贝到目标机器，进入 compose 目录
cd "Offline Docker\compose"

# 2. 一键安装
powershell.exe -ExecutionPolicy Bypass -File .\install.ps1

# 如需指定端口：
# powershell.exe -ExecutionPolicy Bypass -File .\install.ps1 -Port 8080
```

### 安装过程说明

安装脚本会依次执行：

1. **加载离线镜像**：`docker load -i images/noneadcss-latest.tar`
2. **生成密钥**：首次安装时自动运行密钥脚本，从 `.env.example` 生成 `.env` 并填入安全随机密钥，同时在终端显示**管理员账号和密码**（请立即记录！）
3. **初始化数据卷**：首次安装时创建命名卷 `noneadcss-data` 并将 `data/fresh_schema.db`（空库种子）写入卷内，应用启动时将自动完成迁移检查并创建管理员账号
4. **启动服务**：`docker compose up -d`（容器名 `noneadcss-prod`，数据持久化在命名卷 `noneadcss-data`）
5. **健康检查**：等待 `http://127.0.0.1:5173/health` 返回 `{"status":"Healthy"}`（最多 120 秒）

### 部署完成后

- **Web 应用**：`http://<服务器IP>:5173/`
- **健康检查**：`http://<服务器IP>:5173/health`
- **管理员登录**：使用安装时生成的管理员邮箱和密码（首次启动自动创建超级管理员）

> ⚠️ 请登录系统后**立即修改管理员密码**，并妥善保管。生产环境若未设置 `ADMIN_EMAIL` / `ADMIN_PASSWORD`，应用会拒绝启动（不会回退到弱默认凭据）。

---

## 四、环境变量说明（.env）

安装脚本会自动生成 `.env`。如需手工配置，可复制 `.env.example` 为 `.env` 并运行密钥脚本。

```bash
# 常用配置
HOST_PORT=5173                                   # 宿主机对外端口

# 密钥（generate-secrets 脚本自动生成，无需手工填写）
JWT__Key=<随机>
SECRET_HASHER__Key=<随机>
ENCRYPTION__Key=<随机>
AUDIT__HmacKey=<随机>
APP__SatisfactionTokenKey=<随机>

# 超级管理员（首次启动创建，仅 Users 表为空时生效）
ADMIN_EMAIL=admin@example.com
ADMIN_PASSWORD=<强密码>
```

**密钥安全提醒：**
- `.env` 包含生产密钥，**严禁提交到 Git**、严禁随安装包原样分发。
- 正式投入使用前建议重新运行密钥脚本轮换一次（`RotateOnly` 模式保留管理员账号）：
  ```powershell
  powershell -ExecutionPolicy Bypass -File .\generate-secrets.ps1 -Mode RotateOnly
  ```
  ```bash
  ./generate-secrets.sh
  ```
- `ENCRYPTION__Key` 一旦用于生产即为长期凭据，轮换将导致历史加密数据无法解密，请慎重。

---

## 五、日常运维

```bash
# 查看服务状态
docker ps --filter "name=noneadcss-prod"

# 查看实时日志
docker logs -f noneadcss-prod

# 停止服务（数据保留）
docker compose --env-file .env down        # 在 compose 目录下执行

# 重启服务
docker compose --env-file .env up -d       # 在 compose 目录下执行

# 彻底卸载（删除数据卷，数据将丢失）
./uninstall.sh --purge                      # Linux
.\uninstall.ps1 -Purge                      # Windows
```

**数据持久化：** 全部数据（SQLite 数据库 + 附件）存储在 Docker 命名卷 `noneadcss-data` 中，卸载容器不会丢失数据。

---

## 六、导入既有数据（从旧环境迁移）

> 离线安装默认为**全新数据库**。若需将旧环境（如现有生产库）的数据迁移过来，按以下步骤操作。

**第 1 步：备份旧环境数据库。** 在旧环境停止服务后，用 SQLite 将 WAL 合并进主库（避免复制到不匹配的 WAL 导致 schema 损坏）：

```bash
# 在旧环境的数据库文件目录（含 noneadcss.db / -wal / -shm）执行：
sqlite3 noneadcss.db "PRAGMA wal_checkpoint(TRUNCATE); PRAGMA integrity_check;"
```

**第 2 步：拷贝到新环境并导入命名卷：**

```bash
# 新环境：先停止服务
docker compose --env-file .env down

# 创建临时容器，将旧数据库放入命名卷（使用包内应用镜像，无需额外镜像）
docker run --rm -v noneadcss-data:/target -v "$PWD/backup:/source:ro" \
  --entrypoint /bin/sh noneadcss:latest \
  -c "cp /source/noneadcss.db /target/noneadcss.db && rm -f /target/noneadcss.db-wal /target/noneadcss.db-shm && chown -R 999:999 /target"

# 重新启动
docker compose --env-file .env up -d
```

> ⚠️ 导入前请务必备份 `noneadcss-data` 卷。数据库版本必须与当前镜像匹配（应用启动时会自动执行增量迁移，但跨大版本建议先小版本逐级升级验证）。

---

## 七、备份与恢复

```bash
# 备份数据卷（先停止服务确保一致性，或应用运行中时注意 WAL）
docker run --rm -v noneadcss-data:/data -v "$PWD:/backup" \
  --entrypoint /bin/sh noneadcss:latest \
  -c "tar czf /backup/noneadcss-data-$(date +%Y%m%d).tar.gz -C /data ."
```

恢复：解压后将内容放回 `noneadcss-data` 卷（参考第六节导入流程），再 `docker compose up -d`。

---

## 八、升级（新版本离线包）

1. 在开发/打包环境构建新镜像并重新打包：
   ```powershell
   cd C:\noneadcss
   docker build -t noneadcss:latest -f Docker/Dockerfile.prod .
   powershell -ExecutionPolicy Bypass -File "Offline Docker\pack.ps1"
   ```
   > `pack.ps1` 会自动完成两件事：导出新镜像 `images/noneadcss-latest.tar`，并从运行中的生产容器生成最新的空库种子 `data/fresh_schema.db`（**生产容器需在线运行**，若未运行会跳过种子库并给出提示）。
2. 将新的 `Offline Docker` 目录拷贝到目标服务器（升级时只需替换 `images/noneadcss-latest.tar`；`data/fresh_schema.db` 仅用于全新安装，已有数据的服务器无需替换）。
3. 在 compose 目录执行：
   ```bash
   docker compose --env-file .env up -d     # 自动使用新镜像，启动时执行数据库迁移
   ```
4. 等待健康检查通过，检查日志确认迁移完成：
   ```bash
   docker logs -f noneadcss-prod | grep -i "迁移\|migration"
   ```

---

## 九、常见问题排查

| 现象 | 可能原因 | 处理 |
|------|----------|------|
| `docker load` 很慢 | 镜像约 480 MB 属正常 | 耐心等待；拷贝包时用移动硬盘/USB 减少 IO 损耗 |
| 容器反复重启 / exit 139 | 数据卷中存在与主库不匹配的 WAL（常见于直接拷贝 `-wal` 文件） | 见第六节：先 `wal_checkpoint(TRUNCATE)` 再导入；或 `docker volume rm noneadcss-data` 重置后全新安装 |
| 健康检查一直未通过 | 首次启动正在执行数据库迁移（1~3 分钟） | `docker logs -f noneadcss-prod` 查看进度 |
| 端口被占用 | 5173 已被其他程序使用 | 修改 `.env` 的 `HOST_PORT` 后重启 |
| 登录提示管理员凭据错误 | `.env` 中管理员账号未生效或已被修改 | 首次启动后管理员密码在系统内可修改；重置需操作数据库或联系维护方 |
| `.env` 中的密钥含 `$` / `#` | docker compose 变量插值会破坏密钥 | 使用包内密钥脚本重新生成（字符集已排除 `$`/`#`） |

---

## 十、手动安装步骤（不依赖安装脚本）

如需完全手动操作：

```bash
# 1. 加载镜像
docker load -i images/noneadcss-latest.tar

# 2. 生成 .env
cp compose/.env.example compose/.env
# 填写密钥（或用 generate-secrets 脚本自动生成）

# 3. 首次安装：初始化数据卷（空库种子）。已有数据卷请跳过本步！
docker volume create noneadcss-data
docker run --rm -v noneadcss-data:/app/Data -v "$PWD/data/fresh_schema.db:/seed/fresh_schema.db:ro" \
  --entrypoint /bin/sh noneadcss:latest \
  -c "cp /seed/fresh_schema.db /app/Data/noneadcss.db && chown -R 999:999 /app/Data"

# 4. 启动
cd compose
docker compose --env-file .env up -d

# 5. 验证
curl http://127.0.0.1:5173/health
```

---

*生成时间：2026-09-01 · 镜像版本：noneadcss:latest（历史快照，含 v2.2.12：审计日志写入隔离 + MailboxUsers 并发兜底 + 系统工具-系统日志清空功能）*

> **注意（2026-11-23）**：本离线包内镜像为 v2.2.12 时点快照；当前工作树为 **v2.3.0（2026-10-23，未打 tag）**，含 Messenger/Telegram/LINE/iMessage 四渠道与全站中英双语。若要交付 2.3.0，请先用 `pack.ps1` 重新导出镜像与空库种子再分发；数据库无需新增独立表（四渠道复用会话相关表，迁移脚本与 `DatabaseSeeder` 只补 `Modules`/`RolePermissions` 行）。
