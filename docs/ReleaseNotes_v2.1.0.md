# Release Notes — v2.1.0

> **历史文档（归档）**：本文件是 v2.1.0 发布当时的记录，后续版本见 [`CHANGELOG.md`](../CHANGELOG.md) 与 `docs/development.md` 的版本章节。当前工作树为 v2.3.0（2026-10-23，未打 tag），并含四 IM 渠道与全站中英双语的开源准备批次（见 CHANGELOG `[Unreleased]`）。

**版本**: v2.1.0
**发布日期**: 2026-08-05
**上一版本**: v1.6.0 (2026-08-04)
**代码仓库**: git@<git.example.com>/<org>/noneadcss.git
**分支**: master

---

## 版本概述

v2.1.0 是 NoneadCSS（拓德客户服务与支持系统）的一次重要质量与功能升级版本。本次发布围绕三条主线：

1. **飞书集成模块深度完善**：Stream 长连接、3-Tab 界面重构、双向消息同步、图片发送
2. **全面代码审计与修复**：5 个维度（代码质量、安全、功能完整性、性能、测试）共发现 38 个问题，修复 31 项，7 项高风险问题收录至技术债务清单安排下季度专项迭代
3. **测试质量提升**：单元测试用例数从 846 增长至 895，通过率 99.55%，新增 5 个测试 Mock 同步修复

**升级建议**：建议所有生产环境尽快升级至 v2.1.0，本次修复了 2 项高严重等级安全问题（WhatsApp Webhook 签名绕过、ChatService 实体追踪反模式）。

---

## 升级须知

### 破坏性变更

无破坏性变更，向后兼容。

### 配置变更

| 配置项 | 文件 | 变更说明 |
|--------|------|----------|
| `Performance:DingDing:StatusCheckTimeoutSeconds` | appsettings.json | 新增，钉钉 API 可达性探测超时（默认 3 秒） |
| `Performance:Feishu:StatusCheckTimeoutSeconds` | appsettings.json | 新增，飞书 API 可达性探测超时（默认 3 秒） |
| `Performance:WeChat:StatusCheckTimeoutSeconds` | appsettings.json | 新增，微信 API 可达性探测超时（默认 3 秒） |
| `Feishu:ReceiveMode` | 数据库 SystemOptions | 新增字段，可选 `Stream` / `Http`（默认 `Http`） |

### 飞书 Stream 模式启用步骤

1. 在飞书开放平台将接收方式切换为「长连接」
2. 在系统「飞书聊天」设置页将 ReceiveMode 切换为 `Stream`
3. 保存配置后后端自动建立 WebSocket 连接（无需公网 IP/域名）

### 飞书图片发送权限

飞书客服回复图片需开通 `im:resource:upload` 权限：

1. 登录飞书开放平台 → 应用管理 → 权限管理
2. 搜索并开通 `im:resource:upload`（推荐）或 `im:resource`
3. 创建并发布新版本

> 若暂未开通权限，系统会自动降级为发送文本提示消息。

---

## 新增功能

### 1. 飞书 Stream 长连接模块

无需公网 IP/域名即可接收飞书事件，降低部署门槛。

| 组件 | 文件 | 说明 |
|------|------|------|
| `IFeishuStreamConnectionStatus` | Application/Services/ | 连接状态单例接口 |
| `FeishuStreamProto` | Infrastructure/Services/Feishu/ | Protobuf Frame 编解码（手动实现，无第三方依赖） |
| `FeishuStreamClient` | Infrastructure/Services/Feishu/ | WebSocket 长连接客户端（心跳 30s / 指数退避重连 / ACK 回执） |
| `FeishuStreamHostedService` | Infrastructure/Services/Feishu/ | BackgroundService 托管（配置变更感知、自动重连） |

**特性**：
- 连接建立：POST 获取 WebSocket URL → 建立 wss 连接 → 心跳维持
- 消息格式：Protobuf Frame（SeqID、LogID、Service、Method、Headers、Payload）
- 重连策略：1s/2s/4s/8s/16s 指数退避，最大 60s，首次重连随机抖动
- 配置感知：IMemoryCache + 轮询检测配置变更，自动重启连接

### 2. 飞书聊天界面重构（3-Tab 架构）

参考钉钉聊天页面设计，重构为 3 个 Tab：

| Tab | 功能 |
|-----|------|
| 快速接入 | 接收模式切换卡片（Stream / HTTP）、启用飞书接入开关、连接状态显示 |
| 高级设置 | AppId、AppSecret、EncryptKey、VerificationToken、机器人名称、自动回复消息 |
| 使用指南 | Stream 模式与 HTTP 模式配置指南、权限说明 |

**视觉设计**：飞书品牌蓝 `#3370FF` + 青绿 `#00D6B9`，玻璃拟态连接状态卡片，顶部连接摘要胶囊条。

### 3. 飞书双向消息同步

| 方向 | 支持消息类型 |
|------|-------------|
| 飞书 → 客服系统 | 文本（含表情转换）、图片、文件 |
| 客服系统 → 飞书 | 文本、图片（base64+md5 或 media upload）、文件 |

### 4. 钉钉集成模块深度完善（v1.6.0 起持续完善）

- **Stream 长连接模式**：BackgroundService 托管，配置变更感知，WebSocket 连接 + 心跳 + 指数退避重连
- **API 可达性探测**：3 秒超时，IPv6 不可达兜底（强制 IPv4）
- **双向消息同步**：钉钉 ↔ 客服系统，文本/图片/语音/视频/文件全链路打通
- **表情转换**：钉钉 `[流泪]`/`[微笑]` 等 200+ 表情名称转换为 Unicode emoji
- **媒体附件**：客服回复附件 → 媒体上传 → sampleImageMsg/sampleFile 批量发送
- **客户图片下载**：客户发送图片 → 临时下载 URL → 本地存储 → Attachment 记录

---

## 安全修复

### 高严重等级

| # | 问题 | 文件 | 修复方式 |
|---|------|------|----------|
| S1 | WhatsApp Webhook 签名验证在 AppSecret 为空时被跳过，可绕过验证 | `WhatsAppController.cs` | 强制校验签名，未配置时返回 `Unauthorized` |
| S2 | ChatService `RecallMessageAsync` / `EditMessageAsync` 使用 AsNoTracking + Update 导致全字段 UPDATE（可能覆盖并发修改） | `ChatService.cs` | 改用 `GetByIdTrackedAsync`，删除冗余 `Update()` 调用 |

### 中严重等级

| # | 问题 | 修复方式 |
|---|------|----------|
| S3 | FeishuController 事件签名/Token 校验可绕过 | 强制校验签名头和 Token |
| S4 | FeishuAccessTokenService 日志泄露 access token | 移除 TokenPrefix，仅打印前 8 位 |
| S5 | SatisfactionTokenHelper 使用非常量时间比较（时序攻击风险） | 改用 `CryptographicOperations.FixedTimeEquals` |
| S6 | UserService 头像 URL 未校验协议（可注入 javascript:） | 新增 `ValidateAndNormalizeAvatarUrl`，白名单：https、相对路径、data:image |
| S7 | CaptchaService Development 环境绕过验证码校验 | 移除绕过逻辑，所有环境正确校验 |
| S8 | DingDingIngestionService 消息去重逻辑失效 | 修改查询条件，同时匹配 MsgId 和 `sw:SessionWebhook` |

### 低严重等级

| # | 问题 | 修复方式 |
|---|------|----------|
| S9 | WeChatClient 硬编码代理 URL | 配置化 |
| S10 | ImportExportService JSON 导入无大小限制 | 新增 5MB 上限检查 |

---

## 性能优化

### EF Core 实体跟踪反模式修复

| 文件 | 问题 | 修复方式 |
|------|------|----------|
| `ChatService.cs` | RecallMessage/EditMessage AsNoTracking + Update | 改用 GetByIdTrackedAsync |
| `OutlookSyncService.cs` | GetOrCreateSettingAsync AsNoTracking + Update | 改用追踪查询 |
| `KnowledgeService.cs` | SoftDeleteDescendantsAsync N+1 查询 | 利用已追踪实体直接修改 |

### 查询性能优化

| 文件 | 问题 | 修复方式 |
|------|------|----------|
| `CustomReportService.cs` | 全表加载邮箱 | 改为 `FirstOrDefaultAsync` |
| `DashboardService.cs` | 活跃邮箱无限制 | 添加 `.Take(100)` |
| `ImportExportService.cs` | 全表导出 OOM | 添加 AsNoTracking |
| `WorkflowService.cs` | 循环内逐个查询失败日志（N+1） | 改为 `GetExecutionLogsPagedAsync` 批量查询 |
| `CustomReportService.cs` | MapToDtosAsync 循环内逐个查询 owner/team（N+1） | 改为 `GetByIdsAsync` + `FindAsync` 批量查询 |
| `UserService.cs` | GetAssignableUsersAsync 加载全部用户到内存 | 改为 `FindAsync` 数据库层过滤 |
| `ChatService.cs` | @mention 查询加载全部用户（含密码哈希） | 改为 `Select` 投影仅查询 Id/姓名字段 |

### EF Core 警告修复

| 文件 | 问题 | 修复方式 |
|------|------|----------|
| `DashboardService.cs` | Skip/Take without OrderBy | 添加 `OrderBy(u => u.Id)` |
| `ReportService.cs` | Skip/Take without OrderBy（2 处） | 添加 `OrderBy(u => u.Id)` / `OrderBy(m => m.Id)` |
| `Repository.cs` | GetPagedAsync 默认无 OrderBy | orderBy 为 null 时默认 `EF.Property<object>(e, "Id")` |

### 超时配置化

| 配置项 | 默认值 | 说明 |
|--------|--------|------|
| `DingDing:StatusCheckTimeoutSeconds` | 3 | 钉钉 API 可达性探测超时 |
| `Feishu:StatusCheckTimeoutSeconds` | 3 | 飞书 API 可达性探测超时 |
| `WeChat:StatusCheckTimeoutSeconds` | 3 | 微信 API 可达性探测超时 |

---

## Bug 修复

### 飞书集成模块

| # | 问题 | 修复方式 |
|---|------|----------|
| B1 | 启用飞书接入开关 10 分钟后自动关闭 | `FeishuSettingsProvider` 缓存 miss 时从数据库重新加载，而非回退到 baseline |
| B2 | 仅切换 enabled 状态时 AutoReplyMessage 被意外清空 | 添加 `if (!string.IsNullOrEmpty(...))` 守卫 |
| B3 | 客服回复图片上传失败无降级处理 | 上传失败时回退发送文本提示消息 |
| B4 | 飞书事件签名/Token 校验绕过 | 强制校验签名头和 Token |
| B5 | FeishuAccessTokenService 日志泄露 token | 移除 TokenPrefix，仅打印前 8 位 |

### 钉钉集成模块

| # | 问题 | 修复方式 |
|---|------|----------|
| B6 | `GetAccessTokenAsync` 内部 SemaphoreSlim 锁竞争导致永久阻塞 | 接受取消令牌以释放锁 |
| B7 | Stream 模式下多余 Access Token 请求导致超时 | 新建 `IDingTalkStreamConnectionStatus` 单例跟踪连接状态 |
| B8 | 客户发送图片在会话记录中只显示占位文本 | 实现 DingTalk 媒体文件下载和存储链路 |
| B9 | 消息去重逻辑失效 | 修改查询条件，同时匹配 MsgId 和 `sw:SessionWebhook` |

### 基础设施

| # | 问题 | 修复方式 |
|---|------|----------|
| B10 | CaptchaService Development 环境绕过验证码校验 | 移除绕过逻辑 |
| B11 | Vite proxy ECONNREFUSED/ECONNRESET | 后端绑定从 `0.0.0.0` 改为 `127.0.0.1:5009` |
| B12 | `frontend/vite.config.js` 与 `vite.config.ts` 配置冲突 | 删除 `vite.config.js` |

### 前端

| # | 问题 | 修复方式 |
|---|------|----------|
| B13 | 6 处 useEffect/setTimeout 未在组件卸载时清理 | 添加 cancelled 守卫和 cleanup 函数 |
| B14 | Feishu/DingDing/WhatsApp 冗余 `as unknown as` 类型断言 | 移除冗余断言 |
| B15 | Mailboxes.tsx 局部 fetch 遮蔽外部函数 | 重命名为 `fetchMailboxes` |
| B16 | AuditLogs.tsx 死代码 | 清理 |

---

## 测试

### 测试统计

| 指标 | v1.6.0 | v2.1.0 | 变化 |
|------|--------|--------|------|
| 测试总数 | 846 | 895 | +49 |
| 通过 | 842 | 891 | +49 |
| 失败 | 0 | 0 | — |
| 跳过 | 4 | 4 | — |
| 通过率 | 99.53% | 99.55% | +0.02% |

### 本次修复的测试

| 测试文件 | 用例数 | 问题 | 修复方式 |
|----------|--------|------|----------|
| `ChatServiceTests.cs` | 5 | RecallMessage/EditMessage 测试 Mock setup `GetByIdAsync`，但 ChatService 已改用 `GetByIdTrackedAsync` | 改为 `GetByIdTrackedAsync` |
| `ConversationTagServiceTests.cs` | 1 | 断言 `Times.Exactly(2)` 与实际事务提交次数不符 | 修正为 `Times.Exactly(1)` |
| `WhatsAppServiceTests.cs` | 1 | Mock ISystemOptionRepository Setup 未返回更新后的实体 | 调整 Setup |

受影响的 5 个 ChatServiceTests 用例：
- `RecallMessageAsync_Should_Succeed_ForOwnRecentAgentMessage`
- `RecallMessageAsync_Should_Throw_When_NotSender`
- `RecallMessageAsync_Should_Throw_When_OlderThan2Minutes`
- `EditMessageAsync_Should_UpdateContent_And_SetEditedAt`
- `EditMessageAsync_Should_Throw_When_ContentEmpty`

---

## Post-v2.1.0 补丁包（2026-08-08）

本补丁在 v2.1.0 正式版基础上修复了 3 项部署级 / 运行稳定性问题，并统一开发、生产、Nginx 的端口分配。**无破坏性变更，向后兼容。**

### 🔴 高优先级（部署脚本 P0 × 2）

| 编号 | 问题 | 旧行为 | 新行为 | 修复位置 |
|------|------|--------|--------|----------|
| Patch-01 | 部署脚本全局杀同名进程 | `Get-Process dotnet/node/nginx \| Stop-Process` 杀掉机器上所有同名进程，用户其他项目一起崩溃 | 基于 `netstat -ano` 只杀占用目标端口的 Pid：开发 5008/5171、生产 5009/5172/5173；Nginx 先 `nginx -s stop` 优雅关，再兜底；清理后二次校验端口释放 | `deploy/restart-dev.ps1`、`C:\noneadcss_prod\restart-prod.ps1` |
| Patch-02 | 生产脚本缺 Fail-Fast 密钥校验 | 缺 JWT/AES/Audit/WeChat 密钥时 dotnet 启动 10s 才抛异常，脚本再等 40s 报"未就绪"，排错 50s+ | 启动后端前 `[0/4]` 阶段强制校验 4 项必需环境变量，为空立即 `exit 1`，屏幕直接输出可复制的修复命令（4 行 `[Environment]::SetEnvironmentVariable("...", "User")`）；同时注入项从 6 项扩充到 12 项 | `C:\noneadcss_prod\restart-prod.ps1` |

### 🟠 中优先级（运行稳定性 P1 × 2，1 项早于本次已修）

| 编号 | 问题 | 影响 | 修复位置 |
|------|------|------|----------|
| Patch-03 | `WeChatAccessTokenService.RefreshAccessTokenAsync` 互斥锁泄漏：`SemaphoreSlim.WaitAsync(ct)` 被取消/异常中断（未获得锁）时 finally 仍 Release → 计数错误或 SemaphoreFullException | 运行一段时间后"首次正常、后续微信 Token 刷新永远阻塞" | [WeChatAccessTokenService.cs](../NoneadCSS.Infrastructure/Services/WeChat/WeChatAccessTokenService.cs#L77-L204) — 新增 `bool lockAcquired` 跟踪 + Release 兜底 SemaphoreFullException |
| Patch-04（早于本次已修，本轮复审确认 OK）| `ICurrentUserAccessor ↔ IMailboxService` DI 循环依赖 | 生产启用 DI 验证时启动直接抛 AggregateException；开发表现为偶发首次调用死循环 | `MailboxService.cs` 使用 `IServiceProvider` 延迟解析 `ICurrentUserAccessor` |

### 🔧 端口分配统一（v2.1.0 补丁新增约定）

| 服务 | 开发环境 | 生产环境 | 配置入口 |
|------|----------|----------|----------|
| 前端 Vite dev server | 5171 | - | `frontend/vite.config.ts` server.port |
| 前端 Vite preview | - | 5172 | `frontend/vite.config.ts` preview.port |
| 后端 Kestrel | 5008 | 5009 | `restart-dev.ps1` / `restart-prod.ps1` 的 `--urls` |
| Nginx 反向代理入口 | - | 5173 | `deploy/nginx.conf`（listen + upstream frontend/backend）|

### ✅ 测试基线（v2.1.0 → v2.1.0 + 补丁）

| 指标 | v2.1.0 正式版 | v2.1.0 + 2026-08-08 补丁 | 变动 |
|------|--------------|--------------------------|------|
| 后端 `dotnet build` | 0 警告 0 错误 | 0 警告 0 错误 | — |
| 单元测试（xUnit） | 895 总 / 891 通过 / 0 失败 / 4 跳过 | 1010 总 / 1006 通过 / 0 失败 / 4 跳过 | +115 总 / +115 通过 |
| 前端 `npx tsc --noEmit` | 0 错误 | 0 错误 | — |
| 重启脚本按端口杀进程 | 未覆盖 | 仅目标端口 Pid 被清理，其他项目同名进程存活 ✅ | 新增 |
| 生产脚本 Fail-Fast 校验 | 未覆盖 | 缺密钥秒级 exit 1 + 输出修复命令 ✅ | 新增 |
| 微信锁取消/异常后刷刷新 | 未覆盖 | 无锁泄漏，所有后续请求正常 ✅ | 新增 |

### 跳过的测试说明

4 个跳过的测试位于 `WorkflowServiceTests.cs`，原因是 InMemory DB 不支持 `ExecuteDeleteAsync` / `AsSplitQuery`，需使用 SQL Server 或 SQLite 集成测试环境验证。

### 未覆盖范围

- **飞书集成模块**：Stream 长连接、富文本图片发送等功能已实现但未编写单元测试
- **钉钉集成模块**：DingDingService、DingDingClient、DingDingIngestionService 未编写单元测试
- **前端测试**：基础设施已搭建（Vitest + @testing-library/react + happy-dom），但覆盖范围有限

详见《NoneadCSS 测试验收报告 20260805》（内部留存，不在发布包内）。

---

## 技术债务

本次审计跳过的 7 项高风险/范围大问题已收录至技术债务清单，安排 2026 Q4 专项迭代：

| 编号 | 名称 | 优先级 | 工作量 |
|------|------|--------|--------|
| TD-01 | 仓储层事务嵌套缺陷（架构级根因） | P0 | 中（3-5 天） |
| TD-02 | 多模块 N+1 查询与全表加载性能优化 | P1 | 大（5-8 天） |
| TD-03 | EF Core 迁移脚本与数据库版本管理 | P1 | 中（3-5 天） |
| TD-04 | SettingsProvider sync-over-async 全面治理 | P1 | 中（3-5 天） |
| TD-05 | 前端核心模块测试覆盖率提升 | P2 | 大（5-10 天） |
| TD-06 | 通讯渠道集成模块测试覆盖（飞书/钉钉） | P2 | 中（3-5 天） |
| TD-07 | 破坏性变更：仓储层 AsNoTracking 约定强制化 | P2 | 中（3-5 天） |

详见 [NoneadCSS_TechnicalDebt_2026Q4.md](./NoneadCSS_TechnicalDebt_2026Q4.md)。

---

## 文档更新

| 文档 | 更新内容 |
|------|----------|
| [CHANGELOG.md](../CHANGELOG.md) | 新增 2026-08-05 章节，完整记录飞书集成、审计修复、测试修复 |
| [README.md](../README.md) | 扩展飞书集成章节，测试基线更新为 895，新增技术债务章节 |
| [docs/development.md](development.md) | 新增飞书 Stream 长连接开发指南（章节 30）、审计与修复（章节 31）、技术债务管理（章节 32） |
| `docs/NoneadCSS审核报告.md`（内部留存） | 追加 2026-08-05 全面代码审计与修复附录 |
| `docs/NoneadCSS测试验收报告_20260805.md`（内部留存） | 新增，895 用例通过率统计与关键修复记录 |
| [docs/拓德客户服务与支持系统用户使用手册.md](拓德客户服务与支持系统用户使用手册.md) | 重写 11.8 飞书聊天章节 |
| [CONTRIBUTING.md](../CONTRIBUTING.md) | 新增章节 7 技术债务管理，更新验收标准测试基线 |
| [NoneadCSS_TechnicalDebt_2026Q4.md](./NoneadCSS_TechnicalDebt_2026Q4.md) | 新增，7 项高风险技术债务清单 |

---

## 提交记录

| Hash | 类型 | 说明 |
|------|------|------|
| `978689c` | docs | 同步审计修复记录 + 测试验收报告(895用例/99.55%) + 技术债务清单 |
| `054635e` | fix | 全面代码审计修复31项问题 + 飞书Stream长连接 + 测试Mock同步 |
| `0bbd183` | docs | update README, CHANGELOG, dev guide and user manual for DingTalk integration |
| `0eb53d8` | feat | complete Stream mode, emoji conversion, media attachment send/receive, and performance fixes |

---

## 验证结果

| 验证项 | 结果 | 说明 |
|--------|------|------|
| 后端编译 | ✅ 通过 | 0 错误，0 警告（5 个既有可空警告） |
| 单元测试 | ✅ 通过 | 895 项，891 通过，0 失败，4 跳过（通过率 99.55%） |
| 前端 TypeScript | ✅ 通过 | `tsc --noEmit` 0 错误 |
| 前端构建 | ✅ 通过 | `vite build` 成功 |
| 回归检查 | ✅ 通过 | 5 个失败测试已修复，无新增回归 |

---

## 致谢

感谢所有参与本次审计、修复、测试和文档工作的团队成员。

---

## 相关链接

- [完整变更日志](../CHANGELOG.md)
- 安全审计报告（内部留存，不在发布包内）
- 测试验收报告（内部留存，不在发布包内）
- [技术债务清单 2026 Q4](./NoneadCSS_TechnicalDebt_2026Q4.md)
- [开发文档](development.md)
- [用户使用手册](拓德客户服务与支持系统用户使用手册.md)
- [贡献指南](../CONTRIBUTING.md)

---

**NoneadCSS** - 拓德客户服务与支持系统
**发布版本**: v2.1.0
**发布日期**: 2026-08-05
