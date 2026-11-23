# 贡献指南

感谢您对 NoneadCSS（拓德客户服务与支持系统）的关注！本文档介绍如何为项目做出贡献。

---

## 目录

1. [行为准则](#1-行为准则)
2. [如何贡献](#2-如何贡献)
3. [开发环境搭建](#3-开发环境搭建)
4. [代码规范](#4-代码规范)
5. [提交规范](#5-提交规范)
6. [Pull Request 流程](#6-pull-request-流程)
7. [技术债务管理](#7-技术债务管理)
8. [报告问题](#8-报告问题)

---

## 1. 行为准则

作为本项目的贡献者，我们承诺：

- 尊重每一位贡献者，无论其经验水平、性别、性别认同、性取向、残疾、个人外貌、体型、种族、民族、年龄、宗教或国籍
- 保持专业和友善的沟通氛围
- 聚焦于对项目最有利的事情
- 欢迎新贡献者并帮助他们成长

### 不当行为示例

- 使用性化的语言或图像
- 恶意评论、侮辱性或贬损性的言论
- 公开或私下骚扰
- 未经明确许可发布他人的私人信息（如物理地址或电子邮件地址）
- 其他在专业环境中可被合理认为不当的行为

---

## 2. 如何贡献

您可以通过多种方式为项目做出贡献：

### 2.1 报告 Bug

如果您发现了 Bug，请在 Issue 中提供以下信息：

- **复现步骤**：详细描述如何复现问题
- **预期行为**：您期望发生什么
- **实际行为**：实际发生了什么
- **环境信息**：操作系统、.NET 版本、浏览器版本等
- **截图/录屏**：如有必要，附上可视化材料

### 2.2 提交功能建议

我们欢迎新功能建议！请在 Issue 中说明：

- **功能描述**：这个功能解决了什么问题
- **使用场景**：在什么情况下会用到这个功能
- **实现思路**：您对实现方式的想法（可选）

### 2.3 贡献代码

如果您想直接贡献代码，请遵循以下流程：

1. Fork 本仓库
2. 创建功能分支 (`git checkout -b feature/amazing-feature`)
3. 提交您的更改 (`git commit -m 'Add some amazing feature'`)
4. 推送到分支 (`git push origin feature/amazing-feature`)
5. 开启 Pull Request

---

## 3. 开发环境搭建

### 3.1 前置要求

- .NET 10 SDK
- Node.js >= 20.19 或 >= 22.12（Vite 8 的硬性要求；Node 18 已不满足）
- SQLite（开发环境默认）或 SQL Server

### 3.2 克隆项目

```bash
git clone <repository-url>
cd NoneadCSS
```

### 3.3 后端启动

```bash
dotnet restore
cd NoneadCSS.Api
dotnet run
```

### 3.4 前端启动

```bash
cd frontend
npm install
npm run dev
```

### 3.5 一键启动脚本（推荐）

> 手动启动前后端麻烦？直接用仓库内置的 PowerShell 脚本，会先按端口精准杀掉旧进程，再按顺序启动前后端并做 HTTP 就绪探测。

**开发环境（无 Nginx，仅后端 5008 + 前端 5171）：**
```powershell
.\deploy\restart-dev.ps1
```

**生产环境（v2.2.3+ 标准路径：仓库内 `Production/` 目录，含 Nginx 5173 + 前端 5172 + 后端 5009，`.gitignore` 已排除版本控制）：**
```powershell
# 标准启动：先停再启（阻塞 RunCommand 环境务必使用 blocking=false + command_type=long_running_process，否则脚本退出会连带销毁所有子进程）
.\Production\stop-prod.ps1
.\Production\start-prod.ps1

# 等价的一键重启（Production/restart-prod.ps1 = stop + start + 健康检查）
.\Production\restart-prod.ps1
```

> 📌 **历史兼容**：v2.2.1 及更早部署在外部独立目录 `C:\noneadcss_prod\` 时，使用：
> ```powershell
> cd C:\noneadcss_prod
> .\restart-prod.ps1
> ```
> 建议尽快迁移到仓库内 `Production/` 标准目录，方便 Git 同步脚本和配置模板。

**端口分配表（v2.1.0 + 2026-08-08 补丁）：**

| 服务 | 开发环境 | 生产环境 | 配置入口 |
|------|----------|----------|----------|
| 前端 Vite dev server | 5171 | — | `frontend/vite.config.ts` server.port |
| 前端 Vite preview | — | 5172 | `frontend/vite.config.ts` preview.port |
| 后端 Kestrel | 5008 | 5009 | 脚本 `--urls` 参数 |
| Nginx 反向代理入口 | — | 5173 | `deploy/nginx.conf` listen + upstream |

### 3.6 必需环境变量（Fail-Fast 校验清单）

> 2026-08-08 补丁起，生产启动脚本的 `[0/4]` 阶段会 Fail-Fast 强制校验以下 4 项用户级环境变量；缺任一项立即 `exit 1` 并输出可复制修复命令（不再让 dotnet 启动 10s+ 才抛错）。该校验同时存在于 `Production/restart-prod.ps1`（一键重启）与 `Production/start-prod.ps1`（启动）。

**生产环境必需（用户级，设置一次即可）：**
```powershell
# JWT 签名密钥（>=32 字节，任意 ASCII）
[Environment]::SetEnvironmentVariable("JWT__Key", "替换为-你的-JWT-SECRET-KEY-请务必32字节以上", "User")

# AES 字段加密密钥（>=32 字节，任意 ASCII；不要与 JWT 同值）
[Environment]::SetEnvironmentVariable("ENCRYPTION__Key", "替换为-你的-ENC-KEY-请务必32字节以上独立值", "User")

# 审计日志 HMAC 签名密钥（任意字符串，建议 >= 32 字节）
[Environment]::SetEnvironmentVariable("AUDIT__HmacKey", "替换为-你的-AUDIT-HMAC-KEY", "User")

# 满意度评价链接签名密钥（任意字符串，建议 >= 32 字节；留空则回退 JWT__Key）
[Environment]::SetEnvironmentVariable("APP__SatisfactionTokenKey", "替换为-你的-满意度签名密钥", "User")
```

> 建议 4 条命令先 copy 到 PowerShell 执行一次，再跑启动脚本（`Production/start-prod.ps1` 或 `Production/restart-prod.ps1`）；否则 `[0/4]` 秒级 Fail-Fast 会让你返回本小节。

**开发环境可选：**
建议同样设置上述 4 项，再叠加：
```powershell
[Environment]::SetEnvironmentVariable("ADMIN_PASSWORD", "你的管理员登录密码", "User")
[Environment]::SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development", "User")
```

### 3.7 开发环境配置

> **注意**：`appsettings.Development.json` 已从仓库移除（已被 `.gitignore` 排除），开发者首次克隆仓库后需在 `NoneadCSS.Api` 目录下手动创建该文件，用于配置本地开发环境所需的密钥与连接信息。

建议在 `appsettings.Development.json` 中配置固定密钥（避免每次重启密钥变化）：

```json
{
  "Encryption": {
    "Key": "your-dev-encryption-key-here"
  },
  "Jwt": {
    "SecretKey": "your-dev-jwt-secret-key-here"
  }
}
```

管理员密码通过环境变量 `ADMIN_PASSWORD` 配置：

| 环境变量 | 说明 |
|---------|------|
| `ADMIN_PASSWORD` | 管理员密码。首次启动时如未设置，系统会自动生成随机密码并输出到日志，请从日志中获取并妥善保存 |

PowerShell 示例：

```powershell
$env:ADMIN_PASSWORD = "your-strong-password"
```

Bash 示例：

```bash
export ADMIN_PASSWORD="your-strong-password"
```

### 3.8 访问地址

**开发环境（直接打前后端）：**
- 前端: http://localhost:5171
- 后端 API: http://localhost:5008
- API 文档: http://localhost:5008/docs
- 健康检查（liveness）: http://localhost:5008/health
- 健康检查（readiness）: http://localhost:5008/health/ready

**生产环境（走 Nginx 5173）：**
- 统一入口（含前端、API、WebSocket）: http://localhost:5173
- 直连前端（绕过 Nginx 排查）: http://localhost:5172
- 直连后端（绕过 Nginx 排查）: http://localhost:5009
- 直连 API 文档: http://localhost:5009/docs

### 3.9 默认账号

- 用户名: `admin@example.com`
- 密码: 通过环境变量 `ADMIN_PASSWORD` 配置。首次启动时如未设置 `ADMIN_PASSWORD`，系统会自动生成随机密码并输出到启动日志，请从日志中获取并妥善保存

---

## 4. 代码规范

### 4.1 C# 后端规范

- 使用 C# 12 特性（顶层语句、文件范围命名空间）
- 启用 `Nullable` 引用类型
- 遵循 .NET 命名约定：
  - 类名、方法名、属性名：PascalCase
  - 参数名、局部变量：camelCase
  - 私有字段：_camelCase
- 接口以 `I` 开头
- 异步方法以 `Async` 结尾

### 4.2 分层架构原则

- **依赖方向**：API → Application → Domain，Infrastructure 实现 Domain/Application 接口
- **领域驱动**：实体包含业务逻辑，不只是数据容器
- **仓储模式**：数据访问通过 Repository 接口，不直接使用 DbContext
- **工作单元**：通过 IUnitOfWork 管理事务和保存
- **配置化设计**：业务硬编码的 `TimeSpan`、阈值、超时常量不得直接写死在服务实现中，必须通过 `IOptions<T>` 注入：
  - 新增配置节：在 `NoneadCSS.Application/Configuration/` 下新建 `{子域}Options.cs`（如 `ChatOptions`、`CacheOptions`、`RetryOptions`）
  - 注册：在 `Program.cs` 中使用 `services.Configure<TOptions>(configuration.GetSection("..."))`
  - 注入：构造函数注入 `IOptions<TOptions>`（不要直接注入 `IConfiguration`）
  - 配置：`NoneadCSS.Api/appsettings.json` 新增 `Performance` / `FeatureToggles` 等对应节，并提供开发环境合理默认值

### 4.3 TypeScript 前端规范

- 使用 TypeScript 严格模式
- 组件使用函数组件 + Hooks
- 数据获取优先使用 React Query (`@tanstack/react-query`)
- 样式使用 Tailwind CSS
- 文件/组件命名使用 PascalCase
- **catch 块类型守卫**：禁止将 catch 参数声明为 `any`；必须使用 `unknown`，并通过类型守卫提取信息：

  ```ts
  // ✅ 正确示例
  try {
    await someAsyncFn();
  } catch (err: unknown) {
    toast.error(getApiErrorMessage(err, '操作失败'));
  }

  // ❌ 错误示例
  try { ... } catch (e: any) {
    toast.error(e.message);
  }
  ```

  项目已提供 `getApiErrorMessage(err, fallback)` 工具函数，统一处理 `AxiosError`、`Error`、对象 message 字段、字符串等多种错误形状。
- **前端测试**：工具函数、Hooks、核心组件必须提供 Vitest 单元测试：
  - 组件测试使用 `@testing-library/react`，以用户行为为中心（`userEvent.click` 等），避免断言内部实现细节
  - DOM 环境使用 `happy-dom`（在 `vitest.config.ts` 的 `environment` 指定）
  - 运行命令：`npm test`（单测）、`npm run test:watch`（监视模式）、`npm run coverage`（覆盖率报告）

### 4.4 性能优化规范

- 避免 N+1 查询问题，使用批量查询
- 统计方法优先使用数据库聚合（`CountAsync`、`SumAsync`、EF Core `GroupBy`）
- 关键操作使用事务保证原子性
- 前端避免不必要的重渲染
- **EF Core AsNoTracking 强制规则**（CI 检查项）：
  - 仓储层所有**只读查询方法**（命名以 `Get`/`List`/`Find`/`Query`/`Count` 开头且不用于后续修改）必须在 `IQueryable` 最开始调用 `.AsNoTracking()`
  - 命名以 `ForUpdate` 结尾（如 `GetByIdForUpdateAsync`）或在 SaveChanges 前会修改实体属性的查询**不要**加 AsNoTracking
  - 已完成补齐的仓储：`WorkflowRepository`、`SlaRepository`、`NotificationRuleRepository`、`CustomerRepository`、`ThreadRepository` 等核心仓储；新增或修改仓储方法时请自查该约定

### 4.5 安全编码规范

#### 4.5.1 认证与令牌安全

- **JWT 密钥编码**：JWT 签名密钥必须使用 `Encoding.UTF8.GetBytes()` 编码，禁止使用 ASCII（非 ASCII 密钥会被损坏）
- **ClockSkew 归零**：JWT TokenValidationParameters 必须显式设置 `ClockSkew = TimeSpan.Zero`，消除默认 5 分钟宽限期
- **密钥长度校验**：JWT SecretKey 长度必须 ≥ 32 字节，启动时校验，不满足则抛出异常
- **密钥缺失处理**：SecretKey 未配置时必须抛出 `InvalidOperationException`，禁止空字符串回退
- **Refresh Token 轮换**：每次刷新生成新 token，旧 token 标记撤销，使用事务保证原子性
- **重用检测**：检测到已撤销的 Refresh Token 被使用时，立即撤销该用户所有 token
- **登录失败锁定**：连续失败 5 次锁定 15 分钟，使用 `FailedLoginAttempts` 和 `LockoutEnd` 字段

#### 4.5.2 密码安全

- **禁止硬编码敏感信息**：禁止在代码中硬编码密码、API Key、密钥等敏感信息
- **敏感数据存储**：敏感数据必须通过环境变量或 `IEncryptionService` 加密存储
- **密码策略**：使用 `IPasswordPolicyService` 校验密码强度，包含：
  - 最小长度 ≥ 10 位
  - 弱密码字典检查
  - 常见模式检测（连续字符、键盘序列、重复字符）
- **密码重置安全**：
  - 重置成功后失效用户所有未使用的重置令牌
  - 同步撤销所有 Refresh Token，防止会话劫持
  - 所有操作在同一事务内原子执行

#### 4.5.3 注入防护与输入验证

- **XSS 防护（后端）**：邮件模板等 HTML 上下文中插入变量时必须使用 `WebUtility.HtmlEncode` 编码
- **前端 XSS 防护**：前端禁止使用 `dangerouslySetInnerHTML`，必须使用 `DOMPurify` 进行 HTML 消毒
- **路径遍历防护**：文件操作前使用 `Path.GetFullPath()` 规范化路径，再用 `StartsWith()` 校验是否在允许目录内
- **CRLF 注入防护**：过滤输入中的 `\r`、`\n` 字符，防止 HTTP 响应拆分与邮件头注入
- **SSRF 防护**：对外部 URL 请求进行目标地址校验，阻止访问内网地址
- **输入验证**：验证所有用户输入，使用 Data Annotations + FluentValidation

#### 4.5.4 异常处理与信息泄露

- **全局异常处理**：使用 `GlobalExceptionMiddleware` 统一处理异常
- **消息脱敏**：客户端返回安全通用提示，绝不返回 `exception.Message` 或堆栈跟踪
- **日志保留**：完整异常信息（堆栈、上下文）仅记录到服务端日志

#### 4.5.5 数据访问与 EF Core 最佳实践

- **异步数据库查询**：数据库查询使用异步方法（`async`/`await`），避免阻塞线程
- **追踪查询 + SaveChanges**：修改实体使用追踪查询（`GetQueryable(false)`），直接修改属性后 `SaveChangesAsync()`
- **禁止 AsNoTracking + UpdateAsync**：避免 AsNoTracking 查询后调用 UpdateAsync 导致的实体追踪冲突
- **软删除过滤**：查询自动过滤已软删除数据（EF Core 全局查询过滤器）
- **唯一索引 + 软删除**：唯一索引必须包含软删除过滤条件 `.HasFilter("\"DeletedAt\" IS NULL")`

#### 4.5.6 其他安全规范

- **常量时间比较**：比较 token、密钥等安全相关数据时使用 `CryptographicOperations.FixedTimeEquals`，防止时序攻击
- **安全随机数**：生成安全相关的随机数（如 token、密码重置链接）使用 `RandomNumberGenerator`，而非 `Random`
- **Job 作用域**：Quartz Job 必须使用 `IServiceProvider.CreateScope()` 解析 Scoped 服务，避免捕获根容器导致的 disposing 风险
- **Job 并发控制**：所有 Job 必须标注 `[DisallowConcurrentExecution]`，防止同一 Job 多实例并发执行

### 4.6 数据库索引规范

- 高频查询表（Conversation、ConversationThread、SendLog、Customer）必须添加复合索引
- 唯一索引必须包含软删除过滤条件：`.HasFilter("\"DeletedAt\" IS NULL")`
- 报表查询优先使用数据库聚合（`GroupBy`、`CountAsync`），而非内存 `ToList() + GroupBy`
- 大数据量查询必须使用 `AsNoTracking()` 和 `Take()` 限制返回条数

### 4.7 速率限制规范

- 搜索、报表等高资源消耗端点必须应用速率限制特性：`[EnableRateLimiting("PolicyName")]`
- 新增限流策略需在 `Program.cs` 中通过 `AddSlidingWindowLimiter` 注册
- 限流策略命名规范：`Entity + Policy`（如 `SearchPolicy`、`ReportsPolicy`）

---

## 5. 提交规范

### 5.1 提交消息格式

使用 Conventional Commits 规范：

```
<type>(<scope>): <subject>

<body>

<footer>
```

### 5.2 Type 类型

| 类型 | 说明 |
|------|------|
| `feat` | 新功能 |
| `fix` | Bug 修复 |
| `docs` | 文档更新 |
| `style` | 代码格式（不影响功能） |
| `refactor` | 重构（既不新功能也不修 bug） |
| `perf` | 性能优化 |
| `test` | 测试相关 |
| `chore` | 构建/工具/依赖等辅助工具变更 |
| `security` | 安全相关 |

### 5.3 示例

```
feat(conversations): 添加批量分配功能

- 支持选择多个会话批量分配给指定客服
- 添加批量操作 API 端点
- 前端添加批量选择和分配 UI

Closes #123
```

---

## 6. Pull Request 流程

### 6.1 PR 要求

- [ ] 代码通过编译（无错误、无警告）
- [ ] 遵循项目代码规范
- [ ] 更新相关文档（如适用）
- [ ] 添加必要的注释（复杂逻辑处）
- [ ] 自测通过

### 6.2 PR 描述模板

```markdown
## 变更类型

- [ ] Bug 修复
- [ ] 新功能
- [ ] 性能优化
- [ ] 文档更新
- [ ] 其他（请描述）

## 变更描述

请简要描述您做了什么更改。

## 关联 Issue

Closes #123

## 测试步骤

请描述如何验证您的更改：

1. 打开 '...'
2. 点击 '...'
3. 验证 '...'

## 截图（如适用）

请添加相关截图。
```

### 6.3 审查流程

1. 提交 PR 后，请等待维护者审查
2. 根据审查意见进行修改（如有）
3. 审查通过后，维护者将合并您的 PR

---

## 7. 技术债务管理

项目维护一份技术债务清单 [NoneadCSS_TechnicalDebt_2026Q4.md](./docs/NoneadCSS_TechnicalDebt_2026Q4.md)，记录高风险/范围大的问题，安排下季度专项迭代。

### 7.1 贡献者注意事项

- 修复技术债务项前，请先在 Issue 中认领并讨论修复方案
- 每项修复需通过验收标准（详见技术债务清单）：
  1. 后端 Release 编译 0 错误（允许既有可空警告）
  2. 单元测试全部通过（当前基线：**124 个测试类 / 2233 个用例，2228 通过，0 失败，5 跳过**；本机 `dotnet test` 恒失败时用 [scripts/backend-test-runner/README.md](./scripts/backend-test-runner/README.md) 的进程内执行器）
  3. 前端 TypeScript 类型检查 0 错误
  4. 前端 Vite 生产构建成功
  5. 新增的测试用例覆盖修复点
- 修复后请在 PR 描述中关联对应的 TD 编号（如 `Closes TD-01`）

### 7.2 当前技术债务清单（2026 Q4）

| 编号 | 问题 | 优先级 | 工作量 |
|------|------|--------|--------|
| TD-01 | 仓储层事务嵌套缺陷（UnitOfWork 不支持嵌套） | P0 | 3-5 天 |
| TD-02 | 仓储层普遍调用 SaveChangesAsync（30+ 处违规） | P1 | 1-2 周 |
| TD-03 | ReportService 报表全量内存化（含 SQLite 999 参数上限 Bug） | P0 | 1 周 |
| TD-04 | ConversationService 批量操作工作流触发（250+ DB 往返） | P1 | 1-2 周 |
| TD-05 | UserRoleResolver 权限解析完全无缓存（每页面数十次 DB 往返） | P0 | 3-5 天 |
| TD-06 | ConversationThread.MessageId 缺唯一约束（TOCTOU 竞态） | P0 | 3-5 天 |
| TD-07 | 高并发表缺乐观锁 + 全部 Controller 无 API 版本控制 | P1 | 1-2 周 |

> 复核（2026-11-23，v2.3.0 工作树）：TD-07 的 7a（乐观锁）已完成并扩展到 13 个实体，服务层覆盖 Users/ApiKeys/Webhooks/Customers/Knowledge/Chat/TimeEntries，前端日历、时间追踪与知识库互动发 `If-Match`；7b（`/v1/` 版本控制）未做。TD-04 涉及的 `TriggerActionJob` 已于 2026-11-21 移除（该需求仍需一个正式注册的作业）。

---

## 8. 报告问题

### 8.1 安全问题

**请勿在公开 Issue 中报告安全漏洞！**

如果您发现了安全问题，请通过以下方式私下报告：

- 发送邮件至：security@example.com
- 描述问题的详细信息和复现步骤
- 我们会尽快确认并处理

### 8.2 功能问题

对于一般功能问题，请使用 GitHub Issue 模板提交，包含：

- 问题描述
- 复现步骤
- 预期行为
- 实际行为
- 环境信息

---

## 9. 生产版本发布清单（Release Checklist）

从 **v2.2.3（2026-08-17）** 起，所有生产发布必须完成以下 **24 项**检查（前 12 项为 v2.2.1 基线，13-16 为 P3 乐观锁 + SOP 固化新增，17 为 v2.2.9 热修复 DDL + IPv4 基线补充，18 为 v2.2.10 审计日志重签 + 中文标签验证，19 为 v2.2.11 全面审核回归 + 微信 Token 验证，20 为 v2.2.12 审计隔离 + 日志清空验证，21 为 v2.2.13-15 Webhook 自动触发 + 日历/邮件修复 + 邮箱泄露回归，22 为 v2.2.16-17 全站双语 + 翻译管理闭环，23 为 2.3.0 版本单一真源与四 IM 渠道，24 为文档同步与开源预检），任何一项不通过禁止打 tag：

1. 后端 `dotnet build NoneadCSS.sln -c Release` → 0 警告 0 错误
2. 后端 `dotnet test NoneadCSS.Tests` → 通过率 ≥ 99.5%
3. 前端 `cd frontend ; npx tsc --noEmit` → 0 错误
4. 前端 `npm run build` → 成功，无 warning 级告警升级
5. 安全：确认 `EncryptedStringConverter.DecryptSafely` 在解密失败时**不返回密文**
6. 安全：确认 LegacyKey 告警在启动首屏出现
7. 部署：`Production/stop-prod.ps1` 执行后，端口 5009 / 5172 / 5173 全部释放；进程名 `NoneadCSS` 与 `NoneadCSS.Api` 两者都要核对（否则 publish 会被 exe 文件锁 0x80070020 拒绝访问）
8. 部署：`Production/start-prod.ps1` 三服务（后端:5009 / vite preview:5172 / Nginx:5173）健康检查全部通过；**start-prod 必须用非阻塞模式**（blocking=false + long_running_process），脚本末尾必须有 `while ($true) { Start-Sleep -Seconds 60 }` 防止父进程退出连带销毁子进程组
9. 部署：`curl -I http://css.example.com:5173/chat/widget` 返回 CSP 头并含 frame-ancestors 白名单
10. 部署：`backup-db.ps1` 已执行，备份文件大小 > 上次备份的 95%；若升级涉及 schema 变更（如新增 RowVersion），额外保留 `*.pre_rowversion_yyyymmddhhmm.db` 冷备份
11. 文档：`CHANGELOG.md`、`ReleaseNotes_vX.Y.Z.md` 同步更新；`docs/development.md` 追加对应「2026-0X-XX 批量修复」章节
12. 版本号：README 顶部 `版本 vX.Y.Z · YYYY-MM-DD` 与 release notes 一致
13. **DDL 补丁（RowVersion 基线）**：若目标库是 < v2.2.3 的老库，**必须先补 `RowVersion` 列**——为 Users/Customers/ChatSessions/ChatMessages/ApiKeys/Webhooks/KnowledgeCategories/KnowledgeArticles 加 `RowVersion BLOB NOT NULL DEFAULT x'0000000000000000'`（幂等做法是先探测再 ALTER）。验证 SQL：`SELECT COUNT(*) FROM pragma_table_info("Users") WHERE name = "RowVersion"` 应 =1。历史上这一步由内部一次性工具 `_DbPatchRowVersion` 完成，**该工具未随本仓库发布**；全新安装的库由迁移链直接建列，不需要这一步。逐表 SQL 与探测方式见 [docs/development.md §18.10](./docs/development.md#1810-旧库升级rowversion-ddl-补丁)。
14. **端口负向断言**：发布后显式验证 `Get-NetTCPConnection -LocalPort 5008 -ErrorAction SilentlyContinue` 结果**为空**。5008 是开发端口，生产若被监听代表启动命令 `--urls http://127.0.0.1:5009` / `appsettings.Production.json` 双保险至少有一处失效
15. **appsettings.Production.json 同步**：`dotnet publish` 之后必须执行 `Copy-Item -Force NoneadCSS.Api/appsettings.Production.json Production/backend/appsettings.Production.json`，并 `Get-Content ... | Select-Object -First 5` 看到首行有 `"Urls": "http://127.0.0.1:5009"`（publish 的合并输出会丢失该字段，是 EF/.NET SDK 已知行为）
16. Git：`git tag vX.Y.Z` 已 push 到 origin；tag message 首行引用 CHANGELOG.md 中该版本段落标题
17. **DDL 热补丁 + IPv4 基线（v2.2.9 新增）**：① `Program.cs` 启动幂等补丁必须覆盖 `KnowledgeArticleLikes`（联合主键 UserId+ArticleId）、`CalendarEvents AttendeesJson/ReminderEmails` 列、`OutlookSyncSettings 4 列用户级凭据`、`KnowledgeCategories RowVersion`；验证查询 `SELECT name FROM sqlite_master WHERE type='table' AND name='KnowledgeArticleLikes'` 应命中；② `vite preview` 必须带 `--host 127.0.0.1`（默认 IPv6 `[::1]:5172` 会导致 Nginx 502），启动后 `curl -I http://127.0.0.1:5172/` 需返回 200，`Get-NetTCPConnection -LocalPort 5172` 的 LocalAddress 为 127.0.0.1。
18. **审计日志完整性重签 + 中文标签验证（v2.2.10 新增）**：① 升级前若存在历史日志，需先执行 `audit_resign` 工具对存量记录一次性重签；② 升级后登录「系统管理 → 审计日志」，点击「批量校验完整性」确认当前页签名均为绿色 ShieldCheck（无红色 ShieldX）；③ 顶部「操作分布 Top 8」与「实体占比」卡片应显示中文标签（如 更新 / 创建 / 删除、日历事件 / 工作流执行日志 等），不应出现 `calendar.update`、`WorkflowExecutionLog` 等英文原文。
19. **全面审核回归 + 微信 Token 验证（v2.2.11 新增）**：① 回归功能抽查：全局搜索知识库跳转直达文章、知识库点赞后再点取消、用户删除失败在确认框内显示原因、移动端通知面板完整显示、移动端日历视图下拉显示中文；② 微信聊天模块发送一条客服消息，确认日志出现 `微信 access_token 刷新成功（stable_token）` 且无 `Mode=Proxy, StatusCode=401`；③ `Production\secrets.ps1` 中 `WECHAT__ProxyApiKey` 必须是有效密钥（非占位符）且文件保持 **UTF-8 with BOM** 编码（PowerShell 5.1 下无 BOM 会吞掉中文注释后的赋值行）。
20. **审计隔离 + 系统日志清空验证（v2.2.12 新增）**：① 审计日志写入隔离：执行一次业务操作后确认「系统管理 → 审计日志」正常新增记录，后端日志无 `User cannot be tracked` InvalidOperationException；② 系统日志清空功能：进入「系统管理 → 系统工具 → 系统日志」，点击红色「清空日志文件」按钮，确认二次确认后日志视图被清空、文件内容截断为 0 字节且后端日志出现 `系统日志已清空`；③ 清空后继续触发一条新日志，确认新日志正常追加（无 NUL 空洞）；④ `Production\logs\log-*.log` 可被外部进程以写方式打开（Serilog `shared:true` 生效）。
21. **Webhook 自动触发 + 日历/邮件修复 + 邮箱泄露回归（v2.2.13-15 新增）**：① 在相应业务操作（如工单/客服事件）后确认对应 Webhook 事件被自动触发，`Webhooks` 表记录/调用日志出现新事件；② 日历模块切换账户后状态被重置、默认月份显示正确；③ 客户邮件内嵌图片/表格在详情中正常显示；④ `API 密钥`页面刷新后不出现空白，失败时显示错误并有重试按钮；⑤ 在线聊天转工单后，客服在会话回复中只见客户姓名、不再泄漏客户邮箱（`isEmailAddress()` / `displayCustomerName()` 脱敏生效）。
22. **全站双语 + 翻译管理闭环（v2.2.16-17 新增）**：① `npm run i18n:check` 0 处硬编码；② `node scripts/i18n/api-message-dictionary.mjs coverage` → `missingFromDictionary=0`（当前 735 键）；③ `/docs` 与 `/openapi/en.json` 英文侧 0 CJK，487 条路径齐全；④ 翻译管理页导入/导出往返不丢键。
23. **版本单一真源 + 四 IM 渠道（2.3.0 新增）**：① `Directory.Build.props` 的 `<Version>`、`frontend/package.json` 的 `version`、`CHANGELOG.md` 顶部段落与 git tag 四者一致；② 四渠道（Messenger/Telegram/LINE/iMessage，SourceType 11–14）模块行存在且默认关闭，启用后能收发；③ 迁移脚本 `2026_11_16_add_*_module.sql` 与 `DatabaseSeeder` 增量数组口径一致。
24. **文档同步与开源预检（本轮新增）**：① `node scripts/oss-doc-links.mjs check` → `fileUriLinks=0 brokenRelativeLinks=0`；② `node scripts/oss-secret-scan.mjs` 退出码 0（已知基线命中按规则放行，新增命中即为阻止项）；③ README / 用户手册 / development 的版本、渠道、测试数字与本版本一致。

> 发布 7 步标准动作（开发文档 §18.11 / project_memory 生产部署 SOP）：① 停服 + 清理残留 → ② dotnet publish → ③ npm 构建 → ④ 配置同步 → ⑤ start-prod 非阻塞启动 → ⑥ 四端健康 + :5008 负向断言 → ⑦ 进程名二次核对。

## 10. 许可证

通过贡献代码，您同意您的贡献将根据项目的 AGPL-3.0 许可证进行许可。

---

**NoneadCSS** - 拓德客户服务与支持系统
*版本 v2.3.0 · 2026-10-23（工作树；含未发布的四 IM 渠道与全站双语批次，详见 CHANGELOG 的 Unreleased）*
