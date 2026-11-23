# Release Notes — v2.2.1

> **历史文档（归档）**：本文件是 v2.2.1 发布当时的记录，后续版本见 [`CHANGELOG.md`](../CHANGELOG.md) 与 `docs/development.md` 的版本章节。当前工作树为 v2.3.0（2026-10-23，未打 tag），并含四 IM 渠道与全站中英双语的开源准备批次（见 CHANGELOG `[Unreleased]`）。

**版本**: v2.2.1
**发布日期**: 2026-08-11
**上一版本**: v2.2.0 (2026-08-05)
**代码仓库**: git@<git.example.com>/<org>/noneadcss.git
**分支**: master
**部署目录**: `C:\noneadcss\Production\`
**标签**: `v2.2.1`

---

## 版本概述

v2.2.1 是 v2.2.0 之后的紧急修复版本，聚焦"操作后列表/视图未刷新"类前端交互 bug，涉及 **工作流（Workflows）、宏（Macros）、日历（Calendar）三个模块**，累计修复 P0 9 处、P1 7 处（含上轮 Calendar 修复），共计 **16 处前端刷新逻辑缺陷**。

### 修复内容汇总

| 优先级 | 模块 | 修复函数数 | 根因 |
|--------|------|------------|------|
| P0 | Workflows.tsx | 4 + 1 辅助 | 列表 useEffect 依赖 `[query]`，handle 仅调 `refreshOverview()` |
| P0 | Macros.tsx | 5 | useCrudPage 暴露 `crud.refresh()`，所有兄弟组件均调用，本文件遗漏 |
| P1 | Calendar.tsx | 2 | 清理性操作未调 `setSignalRRev`，日历视图 useEffect 依赖不含 overview |
| 上轮 P1 | Calendar.tsx | 5 | 保存/删除/状态/恢复/硬删除后未调 `setSignalRRev` |

### 详细修复清单

#### P0：Workflows（工作流）列表刷新（4 + 1）

在 [Workflows.tsx](../frontend/src/pages/Workflows.tsx) L173-175 新增：
```typescript
const refreshList = () => {
  setQuery((q) => ({ ...q }));
};
```

然后在以下 4 个 handle 函数成功分支追加 `refreshList()` 调用：
- `handleToggleActive`（L274）：启用/禁用开关
- `handleDelete`（L289）：软删除移垃圾箱
- `handleRestore`（L329）：垃圾箱恢复
- `handleSave`（L383）：新建/编辑保存

#### P0：Macros（宏）列表刷新（5）

在 [Macros.tsx](../frontend/src/pages/Macros.tsx) 以下 5 个 handle 函数成功分支追加 `crud.refresh()` 调用：
- `handleToggleActive`（L154）：启用/禁用
- `handleDelete`（L168）：软删除
- `handleRestore`（L191）：垃圾箱恢复
- `handleSave`（L244）：新建/编辑保存
- `handleDuplicate`（L263）：复制

#### P1：Calendar（日历）视图刷新补齐（2）

在 [Calendar.tsx](../frontend/src/pages/Calendar.tsx) 以下 2 个清理性操作成功分支追加 `setSignalRRev((v) => v + 1)`：
- `handleClearTrash`（L1002）：清空垃圾箱
- `handlePurgeTrash`（L1024）：按期清理回收站

### 验证结果

- TypeScript `tsc --noEmit` → 0 错误 ✅
- 前端生产构建 `npm run build` → 1.38s ✅
- 关键 chunk HTTP 200：
  - `Workflows-pXIrOzGk.js`（46.9 KB）✅
  - `Macros-Inzq3fNi.js`（43.61 KB）✅
  - `Calendar-eYMiEKcD.js`（145.28 KB）✅
- Health endpoint `/health` → HTTP 200 ✅

**升级建议**：所有使用 v2.2.0 的生产环境建议升级到 v2.2.1。若当前生产环境未部署 v2.2.0（仍在 v2.1.0），建议先升级 v2.2.0（含安全修复）再升级 v2.2.1。

---

## 继承自 v2.2.0 的版本概述（保留以作参照）

v2.2.0 是 NoneadCSS（拓德客户服务与支持系统）在 v2.1.0 全面审计后的首个正式发布版本，聚焦 **P0/P1 问题闭环、安全加固、功能完整性、生产部署与跨域 Widget 嵌入** 五条主线：

1. **2 项 P0 安全（S-H1/S-H2）闭环**：AES-GCM 加密解密失败安全返回（不泄漏密文）+ 旧密钥迁移告警；生产环境敏感端点分级保护。
2. **3 项 P0 功能（F-C3/C4/C5）闭环**：邮件链路异常日志结构化、用户恢复邮箱冲突 409、报表硬删除前置垃圾箱校验。
3. **数据库 + 代码质量专项**：Conversation 热路径 N+1 优化（首屏 420ms → 95ms）、`BaseController` 统一 CurrentUserId、移除废弃 `DecryptLegacy` 对外签名、ISoftDelete 硬删除拦截器放行。
4. **前端 + 基础设施修复**：useAuth 登出链路修复、Customer 删除前置提示、.gitignore 构建产物；生产启动脚本、后端端口双保险、Nginx CSP frame-ancestors 修复、数据库备份保留 7 天。
5. **正式生产部署流程**：独立目录 + 三服务（后端 5009 / Nginx 5173）+ 一键启停脚本 + 跨域嵌入人工客服 Widget。

**升级建议**：所有生产环境**必须**升级到 v2.2.x，v2.2.0 修复了 2 项**高风险 P0 安全缺陷**（S-H1：敏感字段密文泄漏；S-H2：长期新旧密钥并存无告警），v2.2.1 在其基础上修复 16 处前端交互 bug。

---

## 升级须知

### 破坏性变更

| 范围 | 变更点 | 影响说明 |
|------|--------|----------|
| `DELETE /api/CustomReports/{id}` | 语义由物理删除变更为软删除移入垃圾箱 | 前端 toast 已同步更新，直接调用 REST 客户端的调用方需显式改为 `/hard` 才能物理删除 |
| `IEncryptionService.DecryptLegacy(string)` | 签名已移除 | 外部调用方请改用 `TryDecrypt`，内部实现已自动回退 legacy |
| 生产环境 dev-only 端点 | `TrackingLegacy` / Feishu 测试注入 / WeChat CheckSignature 返回 404 | 不影响正常业务，仅阻断调试路径在生产暴露 |

### 配置变更

| 配置项 | 文件 | 变更说明 |
|--------|------|----------|
| `Urls` | `NoneadCSS.Api/appsettings.Production.json` | 新增 `http://127.0.0.1:5009` 覆盖开发端口 5008，保证生产后端监听 5009（与 Nginx proxy_pass 对齐） |
| 信任代理列表 | `ForwardedHeaders` | 不再清空 KnownProxies，从 `ForwardedHeaders:KnownProxies` 配置读取，部署时按实际 Nginx 内网 IP 填写 |
| 必需环境变量（用户级） | `Production/secrets.ps1` | 4 项必需：`Encryption__Key`、`Encryption__LegacyKey`、`Jwt__Key`、`ADMIN_PASSWORD`；缺失直接 exit 1 |

### 升级步骤（推荐生产环境）

```powershell
# 1. 停服务
C:\noneadcss_prod\stop-prod.ps1

# 2. 备份数据库
C:\noneadcss_prod\backup-db.ps1

# 3. 拷贝发布产物
dotnet publish NoneadCSS.Api/NoneadCSS.Api.csproj -c Release -o C:\noneadcss_prod\backend
Copy-Item frontend\dist\* C:\noneadcss_prod\frontend\dist\ -Recurse -Force

# 4. 覆盖 Production 目录
Copy-Item Production\* C:\noneadcss_prod\ -Recurse -Force

# 5. 一键重启
C:\noneadcss_prod\restart-prod.ps1
```

---

## 🔒 安全修复（P0/P1）

### P0 安全

#### S-H1：EncryptedStringConverter 解密失败不再返回密文

**严重级**：Critical（信息泄漏）

修复前：`DecryptSafely` 在 `TryDecrypt` 失败时返回 `cipherText` 本体。本系统密文是半结构化的 Base64(JSON)，包含 `KeyData/IV/CipherText` 元数据字段，错误返回会把"密文"当作"明文"透传至 API 响应。

修复后：解密失败统一返回 `string.Empty`，配合 `SensitiveDataReadLog` 审计日志记录失败上下文（密文 SHA256 哈希）。

同步修复了 `EncryptedStringConverter.cs` 和 `NoneadCSSDbContext.cs` 两处独立实现。

#### S-H2：AesEncryptionService 旧密钥迁移告警

**严重级**：High（密钥管理 / 可维护性）

修复前：`Decrypt` 中 HKDF 失败后"静默"回退旧 SHA256 密钥 → 系统长期处于双密钥并存但无人察觉的状态，后续删除 legacy 代码会导致历史数据丢失。

修复后：
1. 构造函数加载 `Encryption:LegacyKey` 时输出 Warning 级启动日志。
2. 真正回退时通过 `Interlocked.CompareExchange` 保证每个进程只记录一次 Warning（后续静默，防日志爆炸）。

### P1 安全

#### S-M1：生产端点分级保护
- `TrackingLegacyController`（旧版 Tracking 回显）、`FeishuController` Stream 手动触发端点、`WeChatController.CheckSignature` 在生产环境直接 `NotFound()`。

#### S-M2：BaseController 统一 CurrentUserId
- 新增 `NoneadCSS.Api/Controllers/BaseController.cs`，所有鉴权 Controller 继承；`CurrentUserId` 走 `int.TryParse(claim)` 而非 `(int)UserId` 拆箱，避免 JWT 被篡改导致的 `InvalidCastException`。

#### S-M3：密码重置链路
- `ForgotPassword` 对不存在邮箱统一成功，不枚举用户。
- `ResetPassword` 成功后**同时**撤销：
  - `RevokeAllRefreshTokensAsync(userId)`（所有 Refresh Token）
  - `InvalidateAllUnusedPasswordResetTokensAsync(userId)`（所有未使用重置令牌）

---

## ✨ 功能修复

### P0 功能（F-C3 / C4 / C5）

| 编号 | 修复内容 | 变更文件 |
|------|----------|----------|
| F-C3 | 邮件链路（ConversationEmailJob → EmailDispatcher → EmailService）三段失败日志结构化，含 conversationId / emailId / reason / stack | `ConversationEmailJob.cs` / `EmailDispatcher.cs` / `EmailService.cs` |
| F-C4 | `UserService.RestoreAsync` 恢复软删除用户前，校验邮箱不与已存在非软删除用户冲突 → 409 Conflict | `UserService.cs` |
| F-C5 | `CustomReportService.HardDeleteAsync` 普通用户目标报表必须处于软删除状态（先入垃圾箱），否则 400；SuperAdmin 不受限 | `CustomReportService.cs` |

### P1 功能（F-M1 + F-M2 = 垃圾箱 Tab）

- `ConversationService.MergeAsync`：合并前校验 ChannelId / Channel 类型一致，否则 400，避免 SignalR 推送找不到 Channel。
- `WorkflowActionExecutor`：`execute_macro` 动作从 `GetRequiredService` 改为 `GetService` + null 校验，未注册 Macro 时返回 400 而非 500。
- 自定义报表垃圾箱 Tab（全部 / 我的置顶 / 垃圾箱三标签页 + 数量角标 + 恢复 / 永久删除 / 清空垃圾箱 + 二级确认框），见 CHANGELOG.md 对应章节。

---

## 🗃️ 数据库与代码质量

### 性能

- **D-C1：Conversation 热路径 N+1 优化**：
  - `GetPagedAsync` 去掉 `ThenInclude(x => x.Conversations)` 反向导航预加载。
  - 所有高频查询统一 `AsNoTracking()`。
  - 压测：会话列表首屏 420ms → 95ms（本地 SQLite）。

### 质量

- **D-M1：BaseController**：消除 12 个 Controller 重复的用户 ID 提取。
- **D-M2：废弃 API 移除**：`IEncryptionService.DecryptLegacy(string)` / `AesEncryptionService.DecryptLegacy(string)` 已内联，不再对外暴露。
- **D-M3：硬删除拦截器放行**：`NoneadCSSDbContext.SaveChangesAsync` 对已软删除的 ISoftDelete 实体执行真正 Remove，允许 `CustomReport.HardDelete()` 配合调度软删除不触发外键约束。

---

## 🎨 前端

| 编号 | 变更 | 文件 |
|------|------|------|
| FE-M1 | `useAuth.logout` 先 `POST /api/auth/logout` 再 `localStorage.clear()`，保证 Refresh Token 被服务端撤销 | `frontend/src/hooks/useAuth.tsx` |
| FE-M2 | `CustomerManagement.tsx` 删除客户前置二级提示"客户下所有会话将一并进入回收站"，防止误删 | `frontend/src/pages/CustomerManagement.tsx` |
| FE-M3 | `.gitignore` 添加 `frontend/dist/**`、`frontend/.vite/**`，避免构建产物入库 | `.gitignore` |
| FE-M4 | 自定义报表垃圾箱 Tab（角标 + 恢复 / 永久删除 / 清空 + 二级确认） | `customReports.ts` + `CustomReports.tsx` |

---

## 🏭 基础设施与生产部署

### 生产端口矩阵

| 服务 | 地址 | 说明 |
|------|------|------|
| 后端 API | `http://127.0.0.1:5009` | `ASPNETCORE_URLS` + `appsettings.Production.json:Urls` 双保险 |
| 前端 preview | `http://127.0.0.1:5172` | `npx vite preview --host 127.0.0.1 --port 5172` |
| Nginx 反向代理 | `http://css.example.com:5173` | `/` → 5172；`/api`、`/hubs` → 5009；`/chat/widget` 专门加 frame-ancestors CSP |

### 关键修复

1. **I-M1：start-prod.ps1 中文注释 bug**
   - 原：`$secretsFile = Join-Path $ProdRoot secrets.ps1`（中文注释在前一行后变量丢失 → `$secretsFile` = `null`）
   - 新：`$secretsFile = "$ProdRoot\secrets.ps1"`

2. **I-M2：生产后端端口双保险**
   - `NoneadCSS.Api/appsettings.Production.json` 显式 `Urls=http://127.0.0.1:5009`，覆盖 `appsettings.json` 的 5008。

3. **I-M3：/chat/widget CSP 修复**
   ```nginx
   location /chat/widget {
       add_header Content-Security-Policy "frame-ancestors 'self' http://www.example.com http://example.com https://css.example.com http://localhost:8080 http://localhost:5171 http://localhost:5173;" always;
       try_files $uri /index.html =404;   # =404 避免内部重定向丢失 location 级 add_header
   }
   ```

4. **I-M4：数据库备份 7 天保留**
   - 文件名：`backup_yyyyMMdd_HHmmss.db`
   - 超过 7 天的备份自动删除。

### 生产环境无 HTTPS/TLS

已按要求取消 Nginx 443 监听。TLS 由上游负载均衡（`css.example.com`）终止。

---

## 📦 产物与验收

### 后端

```
dotnet build NoneadCSS.sln -c Release
```
结果：0 错误 0 警告 ✅

### 前端

```
cd frontend
npx tsc --noEmit    # 0 错误
npm run build       # 成功
```

### 生产验收（12 项全绿）

- [x] restart-prod.ps1 三服务健康检查全部通过
- [x] `/health` → 200
- [x] `/health/ready` → 200
- [x] `/docs` Scalar 文档可打开
- [x] 登录页验证码正常加载（端口 5008/5009 对齐）
- [x] 邮箱配置页敏感字段解密失败返回空字符串（无密文泄漏）
- [x] 启动日志 LegacyKey Warning 出现
- [x] `curl -I http://css.example.com:5173/chat/widget` 返回 frame-ancestors CSP
- [x] `www.example.com` 首页右下角嵌入人工客服 Widget 正常加载
- [x] 删除客户提示"会话将一并进入回收站"
- [x] Custom Reports 垃圾箱 Tab 数量角标 + 恢复 / 永久删除 / 清空全流程
- [x] Nginx 关闭：`nginx.exe -s quit` 优雅退出，不残留子进程

---

**发布版本**：v2.2.0  
**发布人**：Trae AI 自动化流水线  
**发布状态**：Production Ready
