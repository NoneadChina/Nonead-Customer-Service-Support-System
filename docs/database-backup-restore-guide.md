# 数据库备份与恢复指南（运维专用）

> 适用版本：≥ v2.3.0（工作树，尚未打 tag；恢复流程与 v2.2.3 起的 7 步版一致，兼容 v2.2.1 及以后的备份）。  
> 2.3.0 未新增独立表：四 IM 渠道（Messenger/Telegram/LINE/iMessage）复用 `Conversations` / `ConversationThreads` / `ImMessageLogs`，只在 `Modules` 与 `RolePermissions` 增加行（由 `Migrations/2026_11_16_add_*_module.sql` 与 `DatabaseSeeder` 增量数组负责），备份/恢复步骤不变。  
> 历史版本要点：v2.2.5 起知识库新增 KnowledgeArticleAttachments 表 + KnowledgeArticlesFts FTS5 虚拟表 + ContentType 字段；v2.2.9 起新增 KnowledgeArticleLikes 联合主键表 + CalendarEvents AttendeesJson/ReminderEmails 列 + OutlookSyncSettings 4 列用户级凭据；同时兼容 v2.2.1~v2.2.8，注意 RowVersion 列差异 + 幂等补丁。  
> 标准部署目录：`C:\noneadcss\Production\`  
> 历史兼容路径：`C:\noneadcss_prod\`（v2.2.1 及更早部署，建议迁移到 Production 标准目录）

---

## 1. 目录约定

```
Production/                         # 标准生产根目录（v2.2.3 起仓库内纳管脚本）
├── data/
│   └── noneadcss.db              # 生产 SQLite 主数据库（当前；注意：实际路径由 start-prod.ps1 的 ConnectionStrings__SqliteConnection 绝对路径指定，避免 WorkingDirectory vs ContentRoot 相对路径陷阱）
│   ├── noneadcss.db-wal          # SQLite WAL 写前日志（实时写入，备份时建议一起复制）
│   └── noneadcss.db-shm          # SQLite 共享内存文件
├── backups/                      # 备份目录（backup-db.ps1 自动创建）
│   ├── backup_20260817_030000.db
│   ├── backup_20260816_030000.db
│   ├── backup_20260815_030000.db
│   ├── backup_20260814_030000.db
│   ├── backup_20260813_030000.db
│   ├── backup_20260812_030000.db
│   └── backup_20260811_030000.db   # 7 天保留，第 8 天最旧备份自动删除
├── backend/                      # dotnet publish 产物（NoneadCSS.Api.dll + appsettings.Production.json）
├── frontend/                     # vite build → dist 同步目录（vite preview :5172 服务根）
├── nginx.conf                    # Nginx 生产配置（反向代理 :5173 → :5172；/api → :5009）
├── conf/                         # Nginx mime.types 等
├── html/                         # Nginx 兜底 50x 页面
├── backup-db.ps1                 # 手动备份脚本（建议：Windows 任务计划每日 03:00 执行）
├── stop-prod.ps1                 # ✅ v2.2.3 标准停止脚本（端口 + 双进程名残留清理）
├── start-prod.ps1                # ✅ v2.2.3 标准启动脚本（blocking=false + long_running_process 生命周期；内嵌 JWT/加密 Key Fail-Fast 校验；设置绝对路径 DB 连接字符串）
├── _DbPatchRowVersion/           # ⚠️ 未随本仓库发布的一次性内部 DDL 补丁工具（历史上为 < v2.2.3 老库补 Users/Customers/ChatSessions/ChatMessages/ApiKeys/Webhooks/KnowledgeCategories/KnowledgeArticles 8 张表 RowVersion 列；手工等效做法见 docs/development.md §18.10）
│   └── DbPatchRowVersion.dll
└── secrets.ps1                   # 生产密钥（备份/恢复过程需保持不变；JWT__Key / ENCRYPTION__Key / AUDIT__HmacKey / APP__SatisfactionTokenKey 等 12 项）
```

---

## 2. 手动备份（生产环境）

```powershell
cd C:\noneadcss\Production
powershell -ExecutionPolicy Bypass -File .\backup-db.ps1
```

**流程**：

1. 检测 `backups/` 目录，不存在则创建。
2. 生成文件名：`backup_yyyyMMdd_HHmmss.db`（示例：`backup_20260817_031522.db`）。
3. 复制 `data/noneadcss.db` → `backups/<文件名>`（建议同时复制 `.db-wal`，或者先 `stop-prod.ps1` 让 WAL 自动 checkpoint 合并到主库）。
4. 保留最近 7 天（滚动窗口，超过 7 天的 `.db` 文件按创建时间升序删除，最多保留 7 份）。
5. 输出：
   ```
   [OK] 备份成功: C:\noneadcss\Production\backups\backup_20260817_031522.db (45.2 MB)
   [INFO] 当前保留 7 份备份，未触发清理。
   ```

**注意（v2.2.3 新增）**：
- 备份前**不需要**停止服务（SQLite WAL 模式下只读拷贝不会损坏文件）。但若要做"冷备份 + 100% 一致性"，建议先 `.\stop-prod.ps1`，等待 5 秒后复制 `data/noneadcss.db`，再 `.\start-prod.ps1`。
- **强烈建议在每日 03:00 左右的 Windows 任务计划中配置定时执行**：
  ```
  Program/script: powershell.exe
  Add arguments: -ExecutionPolicy Bypass -File "C:\noneadcss\Production\backup-db.ps1"
  Start in: C:\noneadcss\Production
  ```
- **v2.2.3 生产目录陷阱规避**：由于 `start-prod.ps1` 设置 `ASPNETCORE_CONTENTROOT = Production\backend`（ContentRoot），但 `-WorkingDirectory = Production\`（CWD），**真实的数据库文件由连接字符串决定**。请运行以下 SQL 确认当前库：
  ```sql
  SELECT 'RowVersion列验证(8张表均应=1)' AS CheckName;
  SELECT 'Users'        AS Tbl, COUNT(*) AS HasRowVersion FROM pragma_table_info('Users')        WHERE name = 'RowVersion' UNION ALL
  SELECT 'Customers',    COUNT(*) FROM pragma_table_info('Customers')    WHERE name = 'RowVersion' UNION ALL
  SELECT 'ChatSessions', COUNT(*) FROM pragma_table_info('ChatSessions') WHERE name = 'RowVersion' UNION ALL
  SELECT 'ChatMessages', COUNT(*) FROM pragma_table_info('ChatMessages') WHERE name = 'RowVersion' UNION ALL
  SELECT 'ApiKeys',      COUNT(*) FROM pragma_table_info('ApiKeys')      WHERE name = 'RowVersion' UNION ALL
  SELECT 'Webhooks',     COUNT(*) FROM pragma_table_info('Webhooks')     WHERE name = 'RowVersion' UNION ALL
  SELECT 'KnowledgeCategories', COUNT(*) FROM pragma_table_info('KnowledgeCategories') WHERE name = 'RowVersion' UNION ALL
  SELECT 'KnowledgeArticles',   COUNT(*) FROM pragma_table_info('KnowledgeArticles')   WHERE name = 'RowVersion';
  ```
  8 张表返回 1 = Schema 正确；返回 0 的表需要补 `DbPatchRowVersion <dbPath> --apply`（见 §5）。

---

## 3. 恢复数据库

### 3.1 恢复前必看

1. **恢复 = 全量回滚**：恢复的备份是"那个时间点的快照"，之后写入的所有数据（会话、客户、报表、用户、日历事件）**都会丢失**。
2. **先备份当前**：恢复前请一定先跑一次 `backup-db.ps1`，把"出错时刻的当前 DB"单独保存（便于后续数据差异比对找回）。
3. **加密密钥要一致**：`secrets.ps1` 里的 `Encryption__Key` / `Encryption__LegacyKey` 必须和备份时的环境变量相同；否则 `Mailbox.Password` 等敏感字段**无法解密**（会返回空字符串，参见安全审计报告 S-H1）。
4. **v2.2.3 RowVersion 列交叉校验（新）**：
   - 若备份来自 < v2.2.3（8 张表无 RowVersion），而当前代码基线是 ≥ v2.2.3：恢复完成后必须执行 §5 的 `DbPatchRowVersion --apply`，否则启动时会抛 `SQLite Error 1: 'no such column: u.RowVersion'`。
   - 若备份来自 ≥ v2.2.3（已有 RowVersion），恢复后直接启动即可。

### 3.2 标准恢复流程（v2.2.3 7 步版）

```powershell
# 1. 停生产服务（避免恢复期间有写入损坏数据库文件；端口 + NoneadCSS/NoneadCSS.Api/nginx/node 双进程名清理）
cd C:\noneadcss\Production
powershell -ExecutionPolicy Bypass -File .\stop-prod.ps1

# 2. 先备份"出错时刻的当前 DB"（安全网备份）
powershell -ExecutionPolicy Bypass -File .\backup-db.ps1
# → 输出会是 backup_当前时间.db，保存到 backups/

# 3. 列出备份，选择恢复点
Get-ChildItem .\backups\*.db | Sort-Object CreationTime -Descending | Format-Table Name, Length, CreationTime

# 4. 覆盖替换（把 backups\backup_20260817_030000.db 替换到 data\noneadcss.db）
$target = ".\backups\backup_20260817_030000.db"
Copy-Item $target ".\data\noneadcss.db" -Force

# 5. [v2.2.3 新增] 如果目标备份是 < v2.2.3 的老库（无 RowVersion 列），必须先跑 DDL 补丁：
#    dotnet .\_DbPatchRowVersion\DbPatchRowVersion.dll .\data\noneadcss.db --apply
#    输出 8 张表 APPLIED 或 SKIPPED，事务 COMMIT 即成功。
#    ⚠️ `_DbPatchRowVersion` 是公司生产库上执行过的一次性内部工具，**未随本仓库发布**。
#    手工等效做法（逐表先探测再 ALTER）见 docs/development.md §18.10：
#      ALTER TABLE "Users" ADD COLUMN "RowVersion" BLOB NOT NULL DEFAULT x'0000000000000000';
#      （Users / Customers / ChatSessions / ChatMessages / ApiKeys / Webhooks /
#        KnowledgeCategories / KnowledgeArticles 共 8 张表，已存在则跳过）

# 6. 重启服务（blocking=false + long_running_process 生命周期；启动脚本内嵌健康检查）
powershell -ExecutionPolicy Bypass -File .\start-prod.ps1

# 7. 验证（v2.2.3 标准健康检查 6 项）
#    - :5009/health → HTTP 200 Healthy
#    - :5172/      → HTTP 200（vite preview 直接服务 frontend）
#    - :5173/      → HTTP 200 含 <title>（Nginx 反代）
#    - :5173/health → HTTP 200
#    - :5008       → 未监听（开发端口负向断言）
#    - GET :5009/api/calendar/events → 未鉴权返回 401（非 500，证明 DI + DB Schema OK）
```

### 3.3 一键恢复脚本（restore-db.ps1，v2.2.3 更新版）

> 保存为 `Production/restore-db.ps1`，与 backup-db.ps1 / stop-prod.ps1 / start-prod.ps1 同目录：

```powershell
# restore-db.ps1 - 生产数据库一键恢复（交互式，v2.2.3 版）
param(
    [string]$BackupName = "",   # 可选：指定 backups/ 目录下的文件名（不含路径）
    [switch]$SkipRowVersionPatch = $false  # 跳过 RowVersion DDL 补丁自动检查
)

$ErrorActionPreference = 'Stop'
$ProdRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$DataDir = Join-Path $ProdRoot "data"
$BackupDir = Join-Path $ProdRoot "backups"
$DbFile = Join-Path $DataDir "noneadcss.db"
$PatchDll = Join-Path $ProdRoot "_DbPatchRowVersion\DbPatchRowVersion.dll"

if (-not (Test-Path $BackupDir)) { throw "备份目录不存在: $BackupDir" }

# 选一个备份文件
if ([string]::IsNullOrWhiteSpace($BackupName)) {
    $backups = Get-ChildItem $BackupDir\*.db | Sort-Object CreationTime -Descending
    Write-Host "`n=== 可用备份 ==="
    for ($i = 0; $i -lt $backups.Count; $i++) {
        Write-Host ("  [{0}] {1}  ({2:N2} MB)  {3}" -f ($i+1), $backups[$i].Name, ($backups[$i].Length/1MB), $backups[$i].CreationTime)
    }
    $idx = Read-Host "`n输入要恢复的编号 (1-$($backups.Count))"
    $idxNum = [int]$idx
    if ($idxNum -lt 1 -or $idxNum -gt $backups.Count) { throw "编号越界" }
    $chosen = $backups[$idxNum-1]
} else {
    $chosenPath = Join-Path $BackupDir $BackupName
    if (-not (Test-Path $chosenPath)) { throw "找不到备份文件: $chosenPath" }
    $chosen = Get-Item $chosenPath
}

Write-Host "`n[1/5] 停止生产服务（端口 5009/5172/5173 + 残留进程清理）..."
powershell -ExecutionPolicy Bypass -File (Join-Path $ProdRoot "stop-prod.ps1") | Out-Null

Write-Host "[2/5] 备份出错时刻的当前 DB 作为安全网..."
powershell -ExecutionPolicy Bypass -File (Join-Path $ProdRoot "backup-db.ps1") | Out-Null
$safeguard = Get-ChildItem $BackupDir\*.db | Sort-Object CreationTime -Descending | Select-Object -First 1
Write-Host "       安全网备份文件: $($safeguard.Name) ($($safeguard.Length/1MB):N2 MB)"

Write-Host "[3/5] 覆盖 data\noneadcss.db  ←  $($chosen.Name)"
Copy-Item $chosen.FullName $DbFile -Force

# [v2.2.3 新增] 自动检测是否需要补 RowVersion DDL 补丁
if (-not $SkipRowVersionPatch -and (Test-Path $PatchDll)) {
    Write-Host "[4/5] v2.2.3 RowVersion DDL 补丁检查 & 自动应用（DbPatchRowVersion）..."
    dotnet $PatchDll $DbFile --apply
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "⚠️  DDL 补丁返回非零退出码，请人工确认。如果是 v2.2.3+ 的备份则可能是全部 SKIPPED（正常）。"
    }
} else {
    Write-Host "[4/5] 跳过 RowVersion DDL 补丁检查（SkipRowVersionPatch = $SkipRowVersionPatch，PatchDll 存在 = $(Test-Path $PatchDll)）"
}

Write-Host "[5/5] 重启生产服务（后端 :5009 / vite preview :5172 / Nginx :5173）..."
powershell -ExecutionPolicy Bypass -File (Join-Path $ProdRoot "start-prod.ps1") | Out-Null

Write-Host "`n[完成] 恢复完成。请立即登录系统验证："
Write-Host "  1) 会话列表  2) 客户管理  3) 邮件发送  4) 自定义报表  5) 渠道消息  6) 日历三视图（拖拽/保存无回弹）"
Write-Host "  如启动报 'no such column: u.RowVersion'，请执行：dotnet `"$PatchDll`" `"$DbFile`" --apply"
Write-Host "  若发现异常，请立即用安全网备份 $($safeguard.Name) 再次执行本脚本回滚。"
```

使用：
```powershell
cd C:\noneadcss\Production
powershell -ExecutionPolicy Bypass -File .\restore-db.ps1
```

---

## 4. 发布流程中的数据库备份（v2.2.3 7 步 SOP 第 2 步）

每次生产发布（`dotnet publish` + `npm run build`）之前，标准 SOP 要求对 **Production 完整目录**做一次 robocopy 镜像备份：

```powershell
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
robocopy C:\noneadcss\Production `
         C:\noneadcss\_backup_prod_$stamp `
         /MIR /R:2 /W:1 /NFL /NDL /NP
# /MIR = 镜像（目标目录严格 = 源目录）；/R:2 重试 2 次；/W:1 每次等待 1 秒
# 备份大小 ≈ 1.9 GB（2026-08-17 基线），耗时约 2-5 分钟。
```

这样发布失败或 DB 损坏时不仅能回滚数据库，还能回滚到旧版本的 backend/frontend/nginx.conf 脚本。

---

## 5. v2.2.3 新增：RowVersion DDL 补丁使用手册（DbPatchRowVersion）

**背景**：v2.2.3 代码基线为 Users/Customers/ChatSessions/ChatMessages/ApiKeys/Webhooks/KnowledgeCategories/KnowledgeArticles 8 张高并发表新增了 `RowVersion BLOB NULL` 列，用于 EF Core 乐观并发控制（`.IsRowVersion().ValueGeneratedOnAddOrUpdate()`）。
从 v2.2.2 或更早版本升级的数据库并未经过 EF Core Migration，直接启动会抛：

```
SQLite Error 1: 'no such column: u.RowVersion'
```

**检查（Dry Run）**：
```powershell
cd C:\noneadcss\Production
dotnet .\_DbPatchRowVersion\DbPatchRowVersion.dll .\data\noneadcss.db
# 输出每表 CHECK: APPLIED（已打过补丁） / SKIPPED（列不存在，需要打） / MISSING（表不存在，告警）
```

**应用补丁（幂等事务，8 条 DDL 一次 COMMIT，可重复执行）**：
```powershell
dotnet .\_DbPatchRowVersion\DbPatchRowVersion.dll .\data\noneadcss.db --apply
# 成功：最后一行出现 [COMMIT] Transaction committed successfully。
# 失败：任何一条 DDL 出错都会 [ROLLBACK]，数据库状态保持不变。
```

**验证**：用 §2 中的 8 表 SQL 检查，8 行返回值全部 = 1 即为通过。

---

## 4. 灾难恢复（服务器挂掉 / 磁盘损坏）

假设服务器物理故障，标准生产目录 `C:\noneadcss\Production\` 整目录丢失，恢复路径（v2.2.3 标准流程）：

1. **代码版本**：从 Git 重建（`git clone ...`；`git checkout v2.2.3`）。
2. **密钥**：从密码管理器拿到 4 个必需环境变量（JWT/Encryption/Audit/SatisfactionTokenKey），重新写 `secrets.ps1`（必须与生产完全一致）。
3. **发布产物**（严格按 7 步 SOP）：
   ```powershell
   # 1) 停服务 + 清残留
   .\Production\stop-prod.ps1
   # 2) 后端 publish
   dotnet publish NoneadCSS.Api/NoneadCSS.Api.csproj -c Release -o Production/backend --self-contained false
   # 3) 前端构建
   cd frontend ; npm ci ; npm run build ; cd ..
   # 4) appsettings.Production.json 同步（dotnet publish 会丢 Urls 字段，必须强制覆盖）
   Copy-Item -Force NoneadCSS.Api/appsettings.Production.json Production/backend/appsettings.Production.json
   # 5) 前端 dist 同步
   Copy-Item frontend\dist\* Production\frontend\dist\ -Recurse -Force
   ```
4. **RowVersion DDL 补丁**（若恢复的是 v2.2.2 之前的备份，必须执行；v2.2.3+ 的备份可跳过）：
   ```powershell
   dotnet .\_DbPatchRowVersion\DbPatchRowVersion.dll .\Production\data\noneadcss.db --apply
   ```
5. **数据库**：从最新备份恢复（`Production/backups/backup_最新.db` 拷到 `Production/data/noneadcss.db`）。
6. **Nginx**：重新下载 Nginx 1.27.3 到 `Production/nginx/`，`conf/nginx.conf` 从仓库 `Production/nginx.conf` 还原。
7. **启动**：
   ```powershell
   .\Production\stop-prod.ps1    # 先停一次，防残留
   .\Production\start-prod.ps1   # 再启动（含 --urls http://127.0.0.1:5009 双保险 + while 保持进程组）
   ```
8. **健康检查**：分别 curl `:5009/health`、`:5172/`、`:5173/`、`:5173/health` → 全部 200；并负向断言 `:5008` 未被监听。

> 📌 **历史兼容**：若仍在使用外部独立目录 `C:\noneadcss_prod\`（v2.2.1 及更早部署），把以上命令中的 `Production/` 路径替换为 `C:\noneadcss_prod\`，`start-prod.ps1` 替换为 `restart-prod.ps1` 即可。建议尽快迁移到仓库内 `Production/` 标准目录。

---

## 5. 加密密钥丢失怎么办？

> ⚠️ **以下结论为不可逆**，请务必把 `Encryption__Key` / `Encryption__LegacyKey` / `Jwt__Key` 存放到密码管理器并多重备份。

- **Encryption__Key 丢失**：所有新写入的 AES-GCM 加密字段（Mailbox.Password、OAuth、微信/钉钉/飞书 Secret、SystemOptions 敏感项）**永远无法解密**（返回空字符串，表现为"邮箱配置页密码全空"）。补救：重新录入所有敏感字段的值。
- **Encryption__LegacyKey 丢失**（但 Key 正确）：v2.2.0 之前使用 SHA256 派生密钥加密的历史数据无法解密 → 现象同上，但数量是"历史记录子集"。补救：同上。
- **Jwt__Key 丢失**：所有已签发的 Access Token 和 Refresh Token 一律失效 → 所有用户重新登录（影响：用户体验中断；不影响数据库）。

**建议**：`secrets.ps1` 同步复制 3 份（密码管理器 1 份、离线加密磁盘 1 份、运维同事 1 份）。

---

## 6. 备份校验（防止备份文件本身损坏）

每周至少执行 1 次：

```powershell
cd C:\noneadcss\Production
$b = Get-ChildItem .\backups\*.db | Sort-Object CreationTime -Descending | Select-Object -First 1
sqlite3 $b.FullName "PRAGMA integrity_check;"
# 预期输出一行：ok
```

> 📌 **历史兼容**：v2.2.1 及更早部署在 `C:\noneadcss_prod\` 时，把 `cd` 路径替换为该目录即可。

如果输出不是 `ok`：
- 找更早的完整备份执行同一命令；
- 若最近 3 份都 `not ok` → 告警；立即停止写入并排查磁盘坏道；
- 备份文件损坏不影响当前运行的 `data/noneadcss.db`（因为恢复流程从未直接覆盖备份）。

---

*文档版本：v1.3 · 2026-08-28（配合 v2.2.9 生产热修复；补充 §5.2 v2.2.9 新增 DDL 补丁验证矩阵：KnowledgeArticleLikes 表 / CalendarEvents 列 / OutlookSyncSettings 4 列 / RowVersion 9 表；§2 版本基线升级为 v2.2.9；附录新增发布前验证 SQL 6 条一键核查 + vite preview IPv4 断言 §附-3）*

*标准部署目录：C:\noneadcss\Production\（v2.2.2+ 推荐）*
*历史兼容部署目录：C:\noneadcss_prod\（v2.2.1 及更早，建议迁移）*

---

## v2.2.1 更新（2026-08-11）

紧急修复版本，修复 Workflows / Macros / Calendar 三处前端"操作后列表/视图未刷新"的刷新触发器 bug（P0+P1 共 16 处）。
