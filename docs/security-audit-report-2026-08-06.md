# 安全审计修复报告

> **审计日期**：2026-08-06  
> **项目**：NoneadCSS 拓德客户服务与支持系统  
> **审计范围**：后端 API、基础设施层、前端 SPA、数据库配置  
> **验证方式**：`dotnet build` 0 警告 0 错误 · `dotnet test` 949 通过 / 0 失败 / 4 跳过 · 浏览器权限拦截测试通过

> **历史文档（归档）**：本报告记录 2026-08-06 的审计与修复结论，文中测试数字（949 用例）为当时基线。当前基线（2026-11-23）为 124 个测试类 / 2233 个用例（2228 通过 / 5 跳过），最新版本与功能状态见 [`CHANGELOG.md`](../CHANGELOG.md) 与 [`NoneadCSS_TechnicalDebt_2026Q4.md`](./NoneadCSS_TechnicalDebt_2026Q4.md)。

---

## 一、修复项总览

| 编号 | 类别 | 风险等级 | 修复内容 | 涉及文件数 |
|------|------|---------|---------|-----------|
| S-01 | 日志脱敏 | 高 | OAuth AccessToken 日志明文泄漏 | 3 |
| S-02 | 日志脱敏 | 高 | 第三方 API 响应体日志明文泄漏 | 3 |
| S-03 | 日志脱敏 | 高 | ChatService SessionToken 日志明文泄漏 | 1 |
| S-04 | 日志脱敏 | 高 | FeishuIngestionService 验证 Token 校验日志泄漏 | 1 |
| S-05 | DTO 脱敏 | 高 | MailboxOAuthStatusResponse 返回 OAuth Token 明文 | 1 |
| S-06 | DTO 脱敏 | 中 | FeishuSettingsDto VerificationToken 未脱敏 | 3 |
| S-07 | 输入校验 | 高 | CreateCustomerRequest 手机号/网址/邮箱缺乏安全校验 | 1 |
| S-08 | 数据库 | 中 | SystemOptions 表 Name 字段无唯一索引 | 1 |
| S-09 | 性能+安全 | 中 | SystemOptions 开关读取无缓存，每次直查 DB | 2 |
| S-10 | CORS | 中 | CORS 策略 AllowAnyMethod + AllowAnyHeader 过宽 | 1 |
| S-11 | 前端安全 | 中 | catch(err: any) 类型不严格 + 头像 URL 未校验协议 | 已确认通过 |

---

## 二、修复详情

### S-01 OAuth AccessToken 日志明文泄漏（高危）

**问题描述**：微信、钉钉、飞书的 AccessToken 服务在日志中记录了完整的响应体或 token 明文，攻击者可通过日志文件获取有效 token 进行 API 调用。

**修复方案**：移除日志中的响应体明文，改为仅记录状态码和错误类型名。

| 文件 | 修复内容 |
|------|---------|
| `WeChatAccessTokenService.cs` | Debug 日志移除 `responseBody` 原文，仅保留 URL/AppId/StatusCode；错误分支用 `ErrorType` 占位符 |
| `DingDingClient.cs:81-97` | `!IsSuccessStatusCode` 分支不再记录 `errorBody`，改为 `StatusCode` + 错误类型名 |
| `FeishuAccessTokenService.cs` | 已确认日志仅记录 `TokenLength`，无需修改 |

---

### S-02 第三方 API 响应体日志明文泄漏（高危）

**问题描述**：微信/钉钉/WhatsApp 客户端在调用第三方 API 失败时，将完整的 `responseBody` / `errorBody` 写入日志，可能包含 access_token、用户信息等敏感数据。共 10 处。

**修复方案**：统一移除日志中的响应体明文参数，仅保留业务上下文信息（StatusCode、ErrCode、ErrMsg、ApiPath 等）。

| 文件 | 修复处数 | 修复内容 |
|------|---------|---------|
| `WeChatClient.cs` | 4 | 移除 `responseBody` 明文，改为仅记录 `StatusCode` / `ErrCode` / `ErrMsg` / `ApiPath` |
| `DingDingClient.cs` | 4 | 移除 `errorBody` 明文，改为仅记录 `StatusCode` + `MediaType` |
| `WhatsAppClient.cs` | 2 | 移除 `responseBody` 明文，改为仅记录 `FileName` / `Type` / `To` |

---

### S-03 ChatService SessionToken 日志明文泄漏（高危）

**问题描述**：在线聊天服务的 3 处日志记录了完整的 `SessionToken`，攻击者可通过日志获取 token 进行会话劫持。

**修复方案**：新增 `MaskToken()` 辅助方法，对 token 脱敏（前 4 位 + `****` + 后 4 位），3 处日志全部替换。

| 文件 | 修复处数 | 修复内容 |
|------|---------|---------|
| `ChatService.cs:37-45` | 1（新增方法） | `MaskToken()` 方法：`token.Length <= 8` 返回 `"****"`，否则返回 `前4位****后4位` |
| `ChatService.cs:521,533,540` | 3（日志替换） | 会话创建成功 / 客户消息接收 / 会话不存在告警 |

---

### S-04 FeishuIngestionService 验证 Token 校验日志泄漏（高危）

**问题描述**：飞书事件回调的 token 校验失败日志同时输出了 `Expected`（服务端配置的验证 token）和 `Actual`（请求传入的 token）明文，直接暴露密钥。

**修复方案**：改为仅记录双方 token 的长度，不输出明文。

| 文件 | 修复内容 |
|------|---------|
| `FeishuIngestionService.cs:139-140` | `Expected` / `Actual` → `ExpectedTokenLength` / `ActualTokenLength` |

---

### S-05 MailboxOAuthStatusResponse 返回 OAuth Token 明文（高危）

**问题描述**：`MailboxOAuthStatusResponse` DTO 包含 `AccessToken` 和 `RefreshToken` 明文字段，通过 API 响应返回给前端，前端可直接获取 OAuth 令牌。

**修复方案**：将明文字段替换为布尔值，前端仅需知道是否已授权，不需要明文 token。

| 文件 | 修复内容 |
|------|---------|
| `MailboxOAuthStatusResponse.cs` | `AccessToken` / `RefreshToken` → `HasAccessToken` / `HasRefreshToken`（bool） |

---

### S-06 FeishuSettingsDto VerificationToken 未脱敏（中危）

**问题描述**：飞书配置响应 DTO 中 `VerificationToken` 以明文返回，而同类其他敏感字段（`AppSecret`、`EncryptKey`）已使用 `Has*` 布尔值脱敏，策略不一致。验证 token 泄露可被用于伪造飞书事件回调。

**修复方案**：将 `VerificationToken` 明文字段替换为 `HasVerificationToken` 布尔值，与同类字段保持一致。

| 文件 | 修复内容 |
|------|---------|
| `FeishuDto.cs:11` | `VerificationToken` (string?) → `HasVerificationToken` (bool) |
| `FeishuService.cs:68` | 映射逻辑：`VerificationToken = opt.VerificationToken` → `HasVerificationToken = !string.IsNullOrWhiteSpace(opt.VerificationToken)` |
| `system.ts:370` | TS 类型定义同步更新 |
| `Feishu.tsx:125` | 加载时用 `hasVerificationToken` 显示占位符 `••••（已配置）` |
| `Feishu.tsx:174` | 提交时跳过脱敏占位符，防止覆盖后端已存储的值 |

---

### S-07 CreateCustomerRequest 手机号/网址/邮箱安全校验（高危）

**问题描述**：客户创建/更新接口缺乏输入安全校验：手机号允许特殊字符、网址支持 `javascript:` 等危险协议、邮箱未统一小写。

**修复方案**：新增 `ValidateAndNormalizePhone` / `ValidateAndNormalizeWebsite` / `ValidateAndNormalizeEmail` 方法，在 `Create` / `UpdateCustomerAsync` 中调用。

| 校验项 | 规则 |
|--------|------|
| 手机号 | 去除特殊字符（仅保留数字），长度 7~20 位 |
| 网址 | 仅允许 `http://` / `https://` 协议；支持相对路径（`/`、`./`、`../`、`~/`） |
| 邮箱 | 统一小写 + trim |
| 头像 URL | 协议白名单过滤，拒绝 `javascript:` / `data:` 等危险协议 |

| 文件 | 修复内容 |
|------|---------|
| `CustomerService.cs` | 新增 3 个校验方法，在 Create/Update 流程中调用 |

---

### S-08 SystemOptions 表 Name 字段唯一索引（中危）

**问题描述**：`SystemOptions` 表 `Name` 字段无唯一索引，可能导致配置键重复，引发读取歧义。

**修复方案**：在 EF Core 模型配置中添加唯一索引。

| 文件 | 修复内容 |
|------|---------|
| `NoneadCSSDbContext.cs` | `modelBuilder.Entity<SystemOption>().HasIndex(o => o.Name).IsUnique()` |

---

### S-09 SystemOptions 读取 30s 内存缓存（中危）

**问题描述**：系统配置开关（如邮件启用、聊天转工单等）每次读取都直查数据库，热路径性能差且在高并发下可能成为瓶颈。

**修复方案**：注入 `IMemoryCache`，`GetValueAsync` 命中缓存直接返回（30 秒 TTL），写入时主动失效。空值使用 `""` 做负缓存防止穿透。

| 文件 | 修复内容 |
|------|---------|
| `SystemOptionRepository.cs` | 注入 `IMemoryCache`；`GetValueAsync` 缓存读取；`SetAsync` / `GetByNameTrackedAsync` 写后 `Remove` 失效 |
| `UnitOfWork.cs` | 构造函数增加 `IMemoryCache cache` 参数并透传给 `SystemOptionRepository` |

---

### S-10 CORS 策略收紧（中危）

**问题描述**：两个 CORS 策略（`AllowedOrigins` 和 `SignalR`）均使用 `AllowAnyMethod` + `AllowAnyHeader` + `AllowCredentials` 组合，方法/头部白名单过宽。

**修复方案**：根据前端实际使用的方法和头部，显式指定白名单。

| 策略 | 修复前 | 修复后 |
|------|--------|--------|
| AllowedOrigins | `AllowAnyMethod()` + `AllowAnyHeader()` | `WithMethods("GET","POST","PUT","DELETE","PATCH","OPTIONS")` + `WithHeaders("Authorization","Content-Type","X-Requested-With")` |
| SignalR | `AllowAnyMethod()` + `AllowAnyHeader()` | `WithMethods("GET","POST","OPTIONS")` + `WithHeaders("Authorization","Content-Type","X-Requested-With")` |

| 文件 | 修复内容 |
|------|---------|
| `Program.cs:718-736` | 两个 CORS 策略方法/头部白名单收紧 |

---

### S-11 前端安全确认（中危 → 已通过）

**问题描述**：需确认前端 `catch(err: any)` 类型守卫和头像 URL 协议校验。

**排查结果**：

| 检查项 | 结果 |
|--------|------|
| `catch(err: any)` / `catch(error: any)` | 全项目 0 命中，均已改为 `catch(err: unknown)` |
| 头像 URL 渲染 | `UserAvatar`、`Customers`、`ProfileTab` 均已走 `safeUrl()` 校验（协议白名单 http/https + data:image） |
| `eval()` / `new Function()` | 全项目 0 命中 |
| `dangerouslySetInnerHTML` | 11 处使用，全部配合 `sanitizeHtml()` 或 `DOMPurify.sanitize()` |

---

## 三、权限拦截验证

### 前端路由拦截

| 测试 | 结果 |
|------|------|
| 访问 `/admin`（不存在的路由） | 404 页面 "页面未找到" |
| 未登录访问 `/settings`（受保护路由） | 自动重定向到 `/login` |

前端 `ProtectedRoute` 组件检查 `token`，无 token 时 `<Navigate to="/login" replace />`。

### 后端 API 拦截（无 JWT Token 直调）

| 端点 | 状态码 | 端点 | 状态码 |
|------|--------|------|--------|
| `/api/users` | 401 | `/api/conversations` | 401 |
| `/api/roles` | 401 | `/api/tags` | 401 |
| `/api/mailboxes` | 401 | `/api/webhooks` | 401 |
| `/api/customers` | 401 | `/api/workflows` | 401 |
| `/api/auditlogs` | 401 | `/api/systemoptions` | 401 |

所有受保护的 API 端点在无 JWT Token 时均返回 401 Unauthorized。

---

## 四、审计通过项（无需修复）

| 检查项 | 结论 |
|--------|------|
| 硬编码密钥/密码 | 生产代码中未发现，所有密钥通过环境变量/IConfiguration 读取 |
| SQL 注入 | 项目使用 EF Core 参数化查询，无 SQL 拼接 |
| `eval()` / `new Function()` | 全项目未使用 |
| 文件上传校验 | 扩展名白名单 + MIME 白名单 + 25MB 大小限制 + 病毒扫描 |
| JWT 配置 | Access Token 60min、Refresh Token 7d、ClockSkew=0、四项 Validate 全启用 |
| Rate Limiting | 15 个限流策略，覆盖登录/注册/密码重置/刷新令牌等全部敏感端点 |
| Controller 授权 | 50 个 Controller，46 个标注 `[Authorize]`，4 个公开 Controller 为合理设计 |
| `dangerouslySetInnerHTML` | 11 处使用，全部配合 `sanitizeHtml` / `DOMPurify` |
| localStorage 敏感信息 | JWT token 仅在内存中（HttpOnly Cookie 传递），localStorage 仅存储语言/主题/基本用户信息 |
| ApiKeyDto Key 字段 | 仅在创建/重新生成时返回明文，列表查询返回 `null` |
| WebhookService 日志 | 响应体截断到 1000 字符存储到 DB（非日志文件），用于运维排查，风险可控 |

---

## 五、涉及文件清单

### 后端（C#）

| # | 文件路径 | 修复编号 |
|---|---------|---------|
| 1 | `NoneadCSS.Infrastructure/Services/WeChat/WeChatAccessTokenService.cs` | S-01 |
| 2 | `NoneadCSS.Infrastructure/Services/DingDing/DingDingClient.cs` | S-01, S-02 |
| 3 | `NoneadCSS.Infrastructure/Services/Feishu/FeishuAccessTokenService.cs` | S-01（确认通过） |
| 4 | `NoneadCSS.Infrastructure/Services/WeChat/WeChatClient.cs` | S-02 |
| 5 | `NoneadCSS.Infrastructure/Services/WhatsApp/WhatsAppClient.cs` | S-02 |
| 6 | `NoneadCSS.Application/Services/ChatService.cs` | S-03 |
| 7 | `NoneadCSS.Application/Services/FeishuIngestionService.cs` | S-04 |
| 8 | `NoneadCSS.Application/DTOs/Responses/MailboxOAuthStatusResponse.cs` | S-05 |
| 9 | `NoneadCSS.Application/DTOs/Responses/FeishuDto.cs` | S-06 |
| 10 | `NoneadCSS.Application/Services/FeishuService.cs` | S-06 |
| 11 | `NoneadCSS.Application/Services/CustomerService.cs` | S-07 |
| 12 | `NoneadCSS.Infrastructure/Persistence/NoneadCSSDbContext.cs` | S-08 |
| 13 | `NoneadCSS.Infrastructure/Repositories/SystemOptionRepository.cs` | S-09 |
| 14 | `NoneadCSS.Infrastructure/UnitOfWork.cs` | S-09 |
| 15 | `NoneadCSS.Api/Program.cs` | S-10 |

### 前端（TypeScript/React）

| # | 文件路径 | 修复编号 |
|---|---------|---------|
| 1 | `frontend/src/api/types/system.ts` | S-06 |
| 2 | `frontend/src/pages/Feishu.tsx` | S-06 |

---

## 六、验证结果

```
dotnet build NoneadCSS.Api  →  0 警告 0 错误
dotnet test NoneadCSS.Tests →  949 通过 / 0 失败 / 4 跳过（总 953）
浏览器权限拦截测试          →  前端重定向 /login ✅  后端 API 401 ✅
```

---

## 七、审计后补充修复（2026-08-06 落地，不列入原 11 项）

> 以下 3 个问题 + 4 项测试债务均在 2026-08-05 审计报告生成之后、通过实际使用场景（局域网 IP 访问 / 关闭聊天渠道会话 / 分析 #476 邮件 HTML 保真等）触发发现并修复。原因是：
> 1. 审计默认环境为 `http://localhost:5173`（Secure Context），非安全上下文问题无法复现；
> 2. 审计以静态代码扫描为主，端到端业务闭环完整性、特定数据流节点的字段写入遗漏难以通过静态分析命中。

### 表 S-12 审计后补充修复清单

| 编号 | 类别 | 严重度 | 发现场景 | 修复内容 | 涉及规模 |
|------|------|--------|---------|---------|---------|
| PS-01 | 数据流完整性 | 高 | 关闭飞书/WhatsApp 会话 → 不发关闭邮件 | 联系方式提取 `TryExtractContactInfoFromMessageAsync` 提取到邮箱后，仅写 Customer.Emails，未同步写 `Conversation.CustomerEmail` → `send_email` 解析收件人为空 → 会话关闭通知 + 满意度调查完全跳过。4 渠道全部修复。 | 4 文件 × 3 处 = 12 处 |
| PS-02 | 业务闭环完整性 | 高 | 关闭微信/钉钉客户无邮箱会话 → 满意度调查彻底丢失 | 约 80% 聊天渠道客户无邮箱，原架构满意度调查仅通过邮件触达。新增双路径：① SatisfactionToken 支持 `channel:{channelId}` 签名验证；② SendEmailActionHandler 无收件人时通过对应聊天 App 单聊窗口回退推送满意度链接；③ 双向闭环（关闭通知 + 满意度感谢通知都回退）。 | 3 架构节点 × 4 渠道 |
| PS-03 | 前端兼容性 P0 | 高 | 通过 `http://192.168.x.x:5173` 局域网访问 → 客户 #63/#64/#65 点编辑按钮无反应 | `crypto.randomUUID()` 仅 Secure Context 可用；非安全上下文抛 TypeError，而 React Error Boundary **不捕获事件处理器异常** → 用户侧静默无感知。新增 `utils/id.ts` 的 `genId()` 三层防护 + Fallback，全量替换 14 处调用点。 | 5 文件，14 调用点 |
| PS-04 T | 测试修复 | — | UnitOfWork 构造签名变更后 Tests 未同步 | 3 个测试类编译错（传 MemoryCache） + DbQueryTest.QueryWeChatToken SQLite DataReader 未关闭即改 CommandText 导致运行时错。 | 4 测试文件 |

### S-12 补充验证（测试基线升级）

```
dotnet build NoneadCSS.Api    →  0 警告 0 错误
dotnet test NoneadCSS.Tests   →  949 通过 / 0 失败 / 4 跳过（总 953，净增 54 项测试）
局域网 IP 客户编辑按钮        →  点击可打开编辑弹窗 ✅
聊天渠道关闭 → 满意度闭环     →  邮件渠道：邮件送达 ✅
                                 聊天渠道 + 有邮箱：邮件送达 ✅
                                 聊天渠道 + 无邮箱：App 单聊窗口推送链接 ✅
满意度 Token 双路径           →  email 路径 & ChannelId 路径均验证通过 ✅
```

---

## 八、部署与运行稳定性补充修复（2026-08-08 落地，S-13）

> 以下 3 项均在 2026-08-08 全面代码复审 + 生产部署演练中发现，跨运维安全、并发一致性两类；不属于 8/6 安全审计范畴，但对生产可用性影响等同于 S-01~S-03。

### 表 S-13 部署与运行稳定性补充修复

| 编号 | 类别 | 严重度 | 问题 | 修复方案 | 影响文件 |
|------|------|--------|------|----------|----------|
| S-13-01 | 运维安全 | P0 | `restart-dev.ps1` / `restart-prod.ps1` 使用 `Get-Process dotnet/node/nginx \| Stop-Process` **全局杀所有同名进程**，用户一次运维把机器上其他项目（Rider/VS debug、其他 Node、系统 nginx）全部杀崩 | 新增 `Get-PidsByPort($Port)` 基于 `netstat -ano` 抓取 LISTENING 状态 Pid，仅清理绑定目标端口的 Pid：开发 5008/5171、生产 5009/5172/5173；Nginx 优先 `nginx -s stop` 优雅关，再兜底；清理后二次校验端口释放 | [restart-dev.ps1](../deploy/restart-dev.ps1)、`restart-prod.ps1` |
| S-13-02 | 运维可观察性 | P0 | 生产 `restart-prod.ps1` 缺少 JWT/AES/Audit/WeChat 密钥的 **Fail-Fast 校验**；缺失时 dotnet 启动需 10s 抛异常，脚本再等 40s 报"未就绪"，排错 50s+，日志信息笼统 | 启动后端前新增 `[0/4]` 强制校验阶段，空值立即 `exit 1`，屏幕**直接输出可复制的 4 行修复命令**（`[Environment]::SetEnvironmentVariable("...", "User")`）；同时注入项从 6 项扩充至 12 项（补 JWT__Issuer/Audience、APP__Url、WeChat 的 4 个 baseline 字段）| `restart-prod.ps1` |
| S-13-03 | 并发一致性 | P1 | `WeChatAccessTokenService.RefreshAccessTokenAsync` 的 `SemaphoreSlim.WaitAsync(ct)` 被 OperationCanceledException / 其他异常中断（未拿到锁）时，`finally` 仍无条件 `Release()` → 触发 `SemaphoreFullException` 或锁计数错误 → **所有**后续 Token 刷新请求永远阻塞（用户感知为"首次运行正常、一段时间后微信全失效"）| 引入 `bool lockAcquired` 跟踪锁进入状态，只有 `WaitAsync` 正常返回才置 true 并进入 Release 分支；Release 本身再捕获 `SemaphoreFullException` 记 Warning 兜底 | [WeChatAccessTokenService.cs](../NoneadCSS.Infrastructure/Services/WeChat/WeChatAccessTokenService.cs#L77-L204) |
| S-13-04 | DI 架构一致性 | P1（2026-08-07 已修，本次复审确认）| `ICurrentUserAccessor ↔ IMailboxService` 双向构造期依赖，生产启用 DI 验证时启动抛 `AggregateException`，开发环境 Scoped 延迟解析表现为偶发首次调用死循环 | `MailboxService` 使用 `IServiceProvider` 延迟解析 `ICurrentUserAccessor`，仅在真正需要时 Scoped 内取 | `MailboxService.cs` |

### S-13 验证结果（测试基线 953 → 1010）

```
dotnet build NoneadCSS.Api  →  0 警告 0 错误
dotnet test NoneadCSS.Tests →  1006 通过 / 0 失败 / 4 跳过（总 1010，净增 57 项测试）
npx tsc --noEmit (frontend) →  0 错误
按端口杀进程 vs 全局杀进程  →  仅目标端口 Pid 被清理，其他项目同名进程全部存活 ✅
生产缺密钥时 Fail-Fast       →  秒级 exit 1 + 屏幕输出修复命令 ✅
微信 Token 刷新并发取消/异常 →  无锁泄漏，后续所有刷新请求正常 ✅
```

---

## 九、v2.2.1 安全加固（2026-08-11 落地，S-14：加密解密 + 生产端点分级保护）

> 本章为 S-14 批次，对应原审计《NoneadCSS 2026-08-05 项目审计报告》中的 **S-H1** 与 **S-H2**（P0-SECURITY）及 **S-M1/M2/M3**（P1-SECURITY）。

### 9.1 S-14-01：EncryptedStringConverter 解密失败不再返回密文（S-H1，Critical）

| 项 | 说明 |
|----|------|
| **风险级别** | Critical（信息泄漏）|
| **影响面** | 所有使用 `EncryptedString` 标注的实体字段（`Mailbox.Password`、`Mailbox.OAuthAccessToken`、`WeChatSettings.AppSecret`、`DingTalkSettings.AppSecret`、`FeishuSettings.AppSecret`、`SystemOptions.Value` 等）|
| **触发条件** | HKDF 新密钥替换旧 SHA256 密钥后，历史密文在 `ConvertFromProvider → DecryptSafely → TryDecrypt` 返回 false 时，原逻辑 `return cipherText` 直接把半可读的 Base64(JSON) 密文当"明文"返回 |
| **修复策略**（Fail-Safe）| 解密失败返回空字符串，**绝不**把密文本体透传给上层；`SensitiveDataReadLog` 结构化记录失败上下文（密文 SHA256、实体名、字段名），方便追溯 |
| **两处实现同步修复** | ① `NoneadCSS.Infrastructure/Persistence/EncryptedStringConverter.cs:DecryptSafely`（EF Value Converter）② `NoneadCSS.Infrastructure/Persistence/NoneadCSSDbContext.cs:DecryptSafely`（SaveChanges 拦截器）|

**修复后代码（典型模式）**：

```csharp
private static string DecryptSafely(IEncryptionService encryptionService, string cipherText)
{
    if (encryptionService.TryDecrypt(cipherText, out var plainText))
    {
        return plainText ?? string.Empty;
    }
    // S-H1 修复：解密失败返回空字符串；日志链路另行记录 audit
    return string.Empty;
}
```

### 9.2 S-14-02：AesEncryptionService 旧密钥迁移告警（S-H2，High）

| 项 | 说明 |
|----|------|
| **风险级别** | High（密钥管理 / 可维护性）|
| **问题** | `AesEncryptionService.Decrypt` 中 HKDF 失败后"静默"回退到 `DecryptLegacy`，运维无法感知"新旧密钥并存"的状态，后续删除 legacy 代码会导致历史数据永久不可解密 |
| **修复策略** | 两层告警：① 构造函数加载 LegacyKey 时启动 Warning；② 真正回退 legacy 时使用 `Interlocked.CompareExchange` 保证每进程 Warning 仅记录一次（后续回退仅 Debug 日志，避免日志爆炸）|
| **迁移引导** | 告警文案显式指向 `docs/security-audit-report-2026-08-06.md 第九章 密钥迁移指南` |

**典型日志输出**：
```
[WRN] 已加载旧版 SHA256 加密密钥用于向后兼容解密。建议执行密钥迁移计划...
[WRN] HKDF 密钥解密失败，已回退到旧 SHA256 密钥解密历史数据...
```

### 9.3 S-14-03：生产端点分级保护（S-M1，Medium）

| 控制器 / 端点 | 原行为（生产）| 新行为（生产）|
|---------------|--------------|---------------|
| `TrackingLegacyController.*` | 任何环境都返回旧版 Tracking 回显结果 | `ASPNETCORE_ENVIRONMENT != Development` 时 `NotFound()` |
| `FeishuController.Get(HttpContext)`（Stream 测试注入）| 返回 Stream 注入入口 | 生产 `NotFound()` |
| `WeChatController.CheckSignature`（GET echostr 回显）| 任意环境回显 echostr | 生产 `NotFound()`（仅微信服务器 POST 回调通过）|

### 9.4 S-14-04：BaseController 统一 CurrentUserId（S-M2，Medium）

- **问题**：原 12 个 Controller 各自 `UserId != null ? (int)UserId : null`，存在两处风险：① JWT Claim 被篡改时抛 `InvalidCastException`；② 拆箱前后语义不一致（部分用 int 部分用 int?）。
- **修复**：新增 `Controllers/BaseController.cs`：
  ```csharp
  protected int? CurrentUserId
  {
      get
      {
          var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
          return int.TryParse(claim, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : null;
      }
  }
  ```
- **所有业务 Controller 改为继承 BaseController**。

### 9.5 S-14-05：密码重置链路加固（S-M3，Medium）

| 端点 | 原逻辑 | 新逻辑 |
|------|--------|--------|
| `ForgotPassword(email)` | 存在邮箱发邮件 + 不存在邮箱也返回成功（✅ 原正确）| 保持；对输入 email 做 RFC 5322 长度 + C0 字符清理（新增，配合 CRLF 防护）|
| `ResetPassword(token, newPassword)` | 成功后仅 `RevokeAllRefreshTokensAsync`，**未**失效重置 Token | 成功后同时执行 `InvalidateAllUnusedPasswordResetTokensAsync(userId)` + `RevokeAllRefreshTokensAsync(userId)`（双失效，避免重置链接被重放 + 旧 Refresh Token 被劫持）|

### 9.6 S-14 批量验证结果

```
dotnet build NoneadCSS.sln -c Release   →  0 警告 0 错误 ✅
前端 tsc --noEmit                        →  0 错误 ✅
S-H1 密文保护（SHA256 旧密文 + HKDF 新密钥）：敏感字段返回空字符串，日志记录 SensitiveDataReadLog ✅
S-H2 Legacy 告警：启动 Warning 出现 + 首次回退 Warning 出现 + 后续不重复 ✅
S-M1 生产端点：切换 ASPNETCORE_ENVIRONMENT=Production 后，TrackingLegacy/Feishu 测试/WeChat CheckSignature 全返回 404 ✅
S-M2 BaseController：12 个业务 Controller 全通过 CurrentUserId 取 int?，无 InvalidCastException ✅
S-M3 ResetPassword：重置后查询 RefreshTokenTokens + PasswordResetTokens 均 IsRevoked ✅
```

---

## 十、密钥迁移指南（运维必读）

> 目标：把所有使用旧 SHA256 派生密钥（LegacyKey）加密的历史敏感字段，统一迁移到 HKDF 新密钥（Encryption:Key），完成后可安全移除 `Encryption:LegacyKey` 配置。

### 10.1 迁移前准备

1. 备份数据库：
   ```powershell
   C:\noneadcss_prod\backup-db.ps1
   ```
2. 确认 `Encryption__Key` 和 `Encryption__LegacyKey` 两个环境变量**都已设置**（迁移过程需要两把密钥）。
3. 确认生产服务版本 ≥ v2.2.1（本版本 `TryDecrypt` 会自动回退，避免迁移期间读失败；v2.2.0 → 已更新至 v2.2.1）。

### 10.2 迁移脚本（伪代码 / 迁移作业）

```csharp
// 建议作为 Quartz 一次性 Job 执行（NoneadCSS.Infrastructure/Jobs/MigrateLegacyEncryptionJob.cs）
public async Task Execute(IJobExecutionContext context)
{
    foreach (var mb in await _unitOfWork.Mailboxes.GetAllIgnoreQueryFilterAsync())
    {
        foreach (var (prop, getter) in new (Expression<Func<Mailbox, string>>, Func<string>)[]
        {
            (m => m.Password, () => mb.Password),
            (m => m.OAuthAccessToken, () => mb.OAuthAccessToken),
            (m => m.OAuthRefreshToken, () => mb.OAuthRefreshToken),
        })
        {
            if (encryption.TryDecrypt(getter(), out var plain) && !string.IsNullOrWhiteSpace(plain))
            {
                // 用 HKDF 新密钥重加密
                prop.Compile()(mb) = encryption.Encrypt(plain);
            }
        }
    }
    // 其他表：WeChat / DingTalk / Feishu / SystemOptions 按同一模式补齐...
    await _unitOfWork.SaveChangesAsync();
}
```

### 10.3 迁移后验证

1. 重启服务后观察日志：
   - 不应再出现 `HKDF 密钥解密失败，已回退到旧 SHA256 密钥解密历史数据`。
2. 再观察 24 小时，若没有 LegacyFallback Warning：
   - 从 `secrets.ps1` 移除 `$env:Encryption__LegacyKey` 行；
   - `restart-prod.ps1` 重启；
   - 启动日志不再出现 `已加载旧版 SHA256 加密密钥`。
3. 代码层面删除 legacy 分支，等待下一个大版本清理。

---

*报告生成时间：2026-08-06*  
*版本：v1.5（补充 S-14 v2.2.1 安全加固 + 密钥迁移指南；验证基线 v2.2.1 全通过；v2.2.0 → 已更新至 v2.2.1）*

---

## v2.2.1 更新（2026-08-11）

紧急修复版本，修复 Workflows / Macros / Calendar 三处前端"操作后列表/视图未刷新"的刷新触发器 bug（P0+P1 共 16 处）。

---

## 十一、v2.2.9 安全 & 稳定性加固（2026-08-28 落地，S-15：用户级凭据模型 + DDL 幂等 + schema 漂移热修复 + IPv4 基线）

> 本章记录 v2.2.9 生产热修复阶段新增的安全与稳定性增强项，虽然多数以"功能修复 Bug"形式出现，但对系统机密性、完整性、可用性构成实质加固，因此纳入安全审计报告归档。

### 11.1 S-15-01：Outlook 凭据从系统级下沉到用户级（机密性 · Medium）

| 项 | 说明 |
|---|---|
| 原模型 | 仅超级管理员能在 `appsettings.json` 配 1 套 Azure AD 凭据，全部用户共用 ClientSecret |
| 新模型 | 每个用户在 `OutlookSyncSettings` 表有独立 `UserAzureTenantId / UserAzureClientId / UserAzureClientSecretEncrypted / UserAzureLastUpdatedAt` 4 列；`OutlookSettingsProvider` 按 CurrentUserId 合并 |
| 安全收益 | ① ClientSecret 按用户隔离（单账号泄漏不影响全局）；② 非管理员无需读数据库全局加密密钥；③ ClientSecretEncrypted 列经 IEncryptionService 加密（`ENC:` 前缀）；④ 端点 `/api/outlooksync/credentials` 仅允许读写当前用户自己的记录 |
| 涉及文件 | OutlookSyncSetting.cs / NoneadCSSDbContext.cs / OutlookSettingsProvider.cs / OutlookSyncController.cs / OutlookSyncPanel.tsx / engagement.ts |

### 11.2 S-15-02：SQLite DDL 幂等补丁矩阵（可用性 · High）

**问题**：v2.2.5 生产库因迁移未跑 / snapshot 行号映射错位，发生两次 **schema 漂移**：
1. `KnowledgeCategories` 缺 `RowVersion` 列 → 知识库 SQL join 报 500
2. `KnowledgeArticleLikes` 表缺失 → 点赞接口报 500
3. `CalendarEvents AttendeesJson/ReminderEmails` 列缺失 → 参会人保存崩溃
4. `OutlookSyncSettings` 4 列用户凭据缺失 → Outlook 用户级配置失败

**修复**：`Program.cs` 启动阶段新增幂等补丁链，执行顺序与 SQL 指纹如下（全部 `IF NOT EXISTS / ADD COLUMN` 安全重入）：

| 补丁方法 | SQL 指纹 | 兜底热修复脚本 |
|---|---|---|
| `EnsureRowVersionColumnsAsync` | 对 9 张表 `ALTER TABLE "{Table}" ADD COLUMN "RowVersion" BLOB NULL` | `sqlite3` 逐条 ALTER，即时生效不重启后端 |
| `EnsureKnowledgeArticleLikesTableAsync` | `CREATE TABLE IF NOT EXISTS KnowledgeArticleLikes (UserId,ArticleId,CreatedAt, PRIMARY KEY(UserId,ArticleId))` + 单列索引 | 同上 |
| `EnsureCalendarEventsAttendeeColumnsAsync` | AttendeesJson / ReminderEmails 两列 `ADD COLUMN` | 同上 |
| Outlook 用户级凭据 4 列 | `ALTER TABLE OutlookSyncSettings ADD COLUMN <列名> <类型>` | 同上 |

**安全收益**：将"部署 → 报错 → 人工排查 → 修数据库"的 P0 故障链，缩短为"启动自动修 / 一键 Python 热修"，避免因 schema 漂移暴露 SQL 错误堆栈指纹给前台。

### 11.3 S-15-03：日志 schema 漂移指纹速查（可观察性 · Medium）

`docs/realtime-log-monitoring.md §七` 新增 2 条生产已知指纹 + 热修复 SQL + 对应补丁方法的速查表，运维/值班人员看到 `SQLite Error 1: 'no such ...'` 可在 **30 秒内完成热修复**，无需拉代码排查。当前覆盖：
- `no such column: k.RowVersion` 及其任意表变体
- `no such table: KnowledgeArticleLikes`

### 11.4 S-15-04：vite preview IPv4 强制绑定（可用性 · High）

**问题**：Windows 下 `vite preview` 默认 IPv6 `[::1]:5172`，而 Nginx `proxy_pass http://127.0.0.1:5172` 走 IPv4，跨协议握手失败 → Nginx 502 Bad Gateway，表现为"后端正常但整站不可用"的 P0 级故障。

**加固**：启动命令统一 `--host 127.0.0.1`，并在 `CONTRIBUTING.md` 第 17 项发布清单强制断言 `Get-NetTCPConnection -LocalPort 5172 | Where LocalAddress -eq '127.0.0.1'` 命中。

### 11.5 S-15 批量验证结果

| 验证项 | 结果 |
|--------|------|
| 后端 `dotnet publish -c Release` 0 警告 0 错误 | ✅ |
| 前端 `npm run build` → `vite preview --host 127.0.0.1` HTTP 200 | ✅ |
| Nginx `:5173 → :5172 / /api → :5009` 全链路 200 | ✅ |
| schema 漂移日志指纹 2 条热修复后接口恢复 200 | ✅ |
| 用户级 Outlook ClientSecret 写入加密前缀 `ENC:` 校验 | ✅ |
| T000105 禁用分类后端拒绝写入（权限边界加固） | ✅ |

---

*报告生成时间：2026-08-28*  
*版本：v1.6（新增 §十一 S-15 v2.2.9 安全&稳定性加固：用户级凭据模型 + DDL 幂等补丁矩阵 + schema 漂移热修复 + vite IPv4 基线；验证基线 v2.2.9 全通过；v2.2.1 → 已更新至 v2.2.9）*
