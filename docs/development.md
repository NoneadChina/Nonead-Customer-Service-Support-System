# NoneadCSS 开发文档

> **Nonead Customer Service & Support System · Developer Documentation**
> 版本 2.3.0 · 2026-10-23（版本口径统一 + scripts/css-api 直连工具；当前开发批次：Messenger/Telegram/LINE/iMessage 四渠道 + 微信客服直连 + 全站中英双语 + 开源准备，见 §59–§60）

## 目录

1. [项目架构](#1-项目架构)
2. [领域模型](#2-领域模型)
3. [API 接口](#3-api-接口)
4. [服务层](#4-服务层)
5. [数据访问层](#5-数据访问层)
6. [事件系统](#6-事件系统)
7. [定时任务](#7-定时任务)
8. [认证授权](#8-认证授权)
9. [国际化](#9-国际化)
10. [病毒扫描与附件安全](#10-病毒扫描与附件安全)
11. [草稿功能](#11-草稿功能)
12. [软删除与回收站](#12-软删除与回收站)
13. [OutputCache 缓存](#13-outputcache-缓存)
14. [工作流自动化](#14-工作流自动化)
15. [前端数据获取（React Query）](#15-前端数据获取react-query)
16. [数据库查询性能优化](#16-数据库查询性能优化)
17. [事务管理](#17-事务管理)
18. [部署与运维](#18-部署与运维)
19. [安全机制](#19-安全机制)
20. [全局速率限制](#20-全局速率限制)
21. [全局搜索](#21-全局搜索)
22. [数据库索引优化](#22-数据库索引优化)
23. [单元测试](#23-单元测试)
24. [微信集成开发指南](#24-微信集成开发指南)
25. [前端测试基础设施（Vitest + Testing Library）](#25-前端测试基础设施vitest--testing-library)
26. [IOptions<T> 配置化设计规范](#26-ioptionst-配置化设计规范)
27. [EF Core AsNoTracking 补齐约定](#27-ef-core-asnotracking-补齐约定)
28. [前端错误处理类型守卫规范](#28-前端错误处理类型守卫规范)
29. [钉钉 / 飞书集成模块开发约定](#29-钉钉--飞书集成模块开发约定)
30. [飞书 Stream 长连接开发指南](#30-飞书-stream-长连接开发指南)
31. [2026-08-05 全面代码审计与修复](#31-2026-08-05-全面代码审计与修复)
32. [技术债务管理](#32-技术债务管理)
33. [2026-08-06 审计后落地修复（Post-TD 清理）](#33-2026-08-06-审计后落地修复post-td-清理)
34. [2026-08-08 部署与运行稳定性修复](#34-2026-08-08-部署与运行稳定性修复)
35. [2026-08-10 微信通知消息与自定义菜单优化 + IM 客户信息收集 + 安全加固](#35-2026-08-10-微信通知消息与自定义菜单优化--im-客户信息收集--安全加固)
36. [2026-08-10 生产环境独立部署架构 + 启动脚本 + Nginx 优化](#36-2026-08-10-生产环境独立部署架构--启动脚本--nginx-优化)
37. [2026-08-11 自定义报表软删除与垃圾箱功能](#37-2026-08-11-自定义报表软删除与垃圾箱功能)
38. [2026-08-11 审计后批量修复 v2.2.0（P0 安全 + 功能 + 数据库 + 生产部署）](#38-2026-08-11-审计后批量修复-v220p0-安全--功能--数据库--生产部署)
39. [2026-08-17 v2.2.3 热修复：日历回弹 + 8 实体乐观锁 + DDL 补丁 + 发布 SOP 固化](#39-2026-08-17-v223-热修复日历回弹--8-实体乐观锁--ddl-补丁--发布-sop-固化)
40. [2026-08-25 日历模块多轮稳定性修复 + P3 前端类型安全加固](#40-2026-08-25-日历模块多轮稳定性修复--p3-前端类型安全加固)
41. [2026-08-26 知识库模块重大升级：双模式编辑 + FTS5 + SVG + 附件 + 草稿](#41-2026-08-26-知识库模块重大升级双模式编辑--fts5--svg--附件--草稿)
42. [2026-08-26 时间追踪模块修复 + UI 风格统一](#42-2026-08-26-时间追踪模块修复--ui-风格统一)
43. [2026-08-26 日历权限修复 T000092：客服与客服主管添加日程权限](#43-2026-08-26-日历权限修复-t000092客服与客服主管添加日程权限)
44. [2026-08-27 日历提醒修复 T000090 + 6 项日历模块 Bug 修复](#44-2026-08-27-日历提醒修复-t000090--6-项日历模块-bug-修复)
45. [2026-08-27 测试覆盖率提升：日历前端组件 100% 覆盖](#45-2026-08-27-测试覆盖率提升日历前端组件-100-覆盖)
46. [2026-08-27 全项目功能审查：8 项 Bug 修复（BUG-01~08）](#46-2026-08-27-全项目功能审查8-项-bug-修复bug-0108)
47. [2026-08-27 系统管理-审计日志完善与优化](#47-2026-08-27-系统管理-审计日志完善与优化)
48. [2026-08-28 Outlook 用户级凭据 + 日历参会人保存修复（T000099 / T000100 · P1）](#48-2026-08-28-outlook-用户级凭据--日历参会人保存修复t000099--t000100--p1)
49. [2026-08-28 知识库模块 5 项 P1 热修复（T000101~T000105）](#49-2026-08-28-知识库模块-5-项-p1-热修复t000101t000105)
50. [2026-08-28 生产发布 SOP 增补：DDL 幂等补丁矩阵 + vite preview IPv4 强制](#50-2026-08-28-生产发布-sop-增补ddl-幂等补丁矩阵--vite-preview-ipv4-强制)
51. [2026-08-30 审计日志完整性签名修复 + 前端中文标签全面补齐（v2.2.10）](#51-2026-08-30-审计日志完整性签名修复--前端中文标签全面补齐v2210)
52. [2026-09-01 全面代码审核 T1-T28 修复 + 回归缺陷修复 + 微信 Token 修复（v2.2.11）](#52-2026-09-01-全面代码审核-t1-t28-修复--回归缺陷修复--微信-token-修复v2211)
53. [2026-09-01 审计日志写入隔离 + MailboxUsers 并发兜底 + 系统日志清空功能（v2.2.12）](#53-2026-09-01-审计日志写入隔离--mailboxusers-并发兜底--系统日志清空功能v2212)
54. [2026-09-04 Webhook 业务事件自动触发 + 日历账户切换状态重置/默认月显示修复（v2.2.13）](#54-2026-09-04-webhook-业务事件自动触发--日历账户切换状态重置默认月显示修复v2213)
55. [2026-09-04 API密钥页面刷新空白兜底（v2.2.14）](#55-2026-09-04-api密钥页面刷新空白兜底v2214)
56. [2026-09-04 在线聊天转工单-会话回复客户邮箱泄露修复（v2.2.15）](#56-2026-09-04-在线聊天转工单-会话回复客户邮箱泄露修复v2215)
57. [2026-10-08 全项目国际化（zh-Hans/en）批次 1/2/12 生产发布（v2.2.16）](#57-2026-10-08-全项目国际化zh-hansen批次-1212-生产发布v2216)
58. [2026-10-10 翻译管理导入导出闭环修复与生产发布（v2.2.17）](#58-2026-10-10-翻译管理导入导出闭环修复与生产发布v2217)
59. [2026-10-23 版本口径统一与 scripts/css-api 直连工具（v2.3.0）](#59-2026-10-23-版本口径统一与-scriptscss-api-直连工具v230)
60. [2026-11-23 开源准备批次：四 IM 渠道与全站中英双语（未发布）](#60-2026-11-23-开源准备批次四-im-渠道与全站中英双语未发布)

---

## 1. 项目架构

### 1.1 分层架构

NoneadCSS 采用经典的分层架构，各层职责明确：

| 层级 | 项目 | 职责 |
|------|------|------|
| **API 层** | `NoneadCSS.Api` | 对外 REST API、中间件、路由配置 |
| **应用层** | `NoneadCSS.Application` | 业务服务、DTO、映射、验证 |
| **领域层** | `NoneadCSS.Domain` | 实体、枚举、领域事件、仓储接口 |
| **基础设施层** | `NoneadCSS.Infrastructure` | 数据库访问、定时任务、事件监听、外部服务 |

### 1.2 依赖关系

```
NoneadCSS.Api
    ├── NoneadCSS.Application
    │       └── NoneadCSS.Domain
    └── NoneadCSS.Infrastructure
            ├── NoneadCSS.Domain
            └── NoneadCSS.Application
```

### 1.3 前端架构

前端采用 React 18 + TypeScript + Vite 8 + Tailwind CSS 构建。

| 目录 | 说明 |
|------|------|
| `frontend/src/pages/` | 55 个页面组件（Dashboard、Conversations、Profile、四渠道接入页等） |
| `frontend/src/components/` | 公共组件（Layout、Modal、ConfirmDialog、UserAvatar 等） |
| `frontend/src/context/` | 全局状态：AuthContext、I18nContext、ModuleContext、MailboxContext |
| `frontend/src/hooks/` | 自定义 Hooks：useAuth、useTheme、usePreferences、useSignalR |
| `frontend/src/api/` | Axios 客户端封装与类型定义 |
| `frontend/src/config/` | moduleMenuMap.ts 模块菜单映射 |
| `frontend/src/utils/` | 工具函数（格式化、信号R 服务等） |

#### 1.3.1 全局状态管理

| Context | 职责 |
|---------|------|
| `AuthContext` | 用户认证、Token 管理、登录/登出 |
| `I18nContext` | 多语言切换（zh-Hans / en） |
| `ModuleContext` | 功能模块启用状态 |
| `MailboxContext` | 全局邮箱列表（跨模块共享，避免重复 API 调用） |

#### 1.3.2 模块权限控制

通过 `frontend/src/config/moduleMenuMap.ts` 定义路由与模块的映射关系，`ModuleProtectedRoute` 组件在路由级别控制模块访问权限。管理员在「模块管理」页面启停模块后，侧边栏菜单自动显示/隐藏对应入口。

#### 1.3.3 主题与个性化

- `useTheme` Hook 负责应用/移除 `dark` class 到 `document.documentElement`
- `usePreferences` Hook 管理用户偏好（时区、语言、深色模式、通知开关、工作日等）并持久化到 `localStorage`

### 1.4 核心组件

#### 1.4.1 启动配置

[Program.cs](../NoneadCSS.Api/Program.cs) 是应用的入口文件，负责：

- 初始化 Serilog 日志系统
- 配置请求本地化（支持中文/英文）
- 注册健康检查
- 配置控制器和模型验证
- 配置 OpenAPI/Swagger
- 配置 JWT 认证
- 配置 CORS
- 注册应用层和基础设施层服务
- 配置 SignalR
- 注册事件分发器和监听器
- 配置中间件管道
- JSON 序列化使用 camelCase 命名策略 + `UnsafeRelaxedJsonEscaping`（避免中文转义）

#### 1.4.2 中间件

| 中间件 | 位置 | 作用 |
|--------|------|------|
| `GlobalExceptionMiddleware` | `Middleware/` | 全局异常处理，统一错误响应格式 |
| `ScalarCustomizationMiddleware` | `Middleware/` | 自定义 Scalar API 文档页面：品牌替换 + `/docs` 中英文切换（改写 openApiUrl、注入语言开关与中文界面翻译层） |
| `CsrfProtectionMiddleware` | `Middleware/` | CSRF 防护，token 常量时间比较 |
| `SecurityHeadersMiddleware` | `Middleware/` | 安全响应头（CSP nonce-based、X-Content-Type-Options 等） |

#### 1.4.3 过滤器

| 过滤器 | 位置 | 作用 |
|--------|------|------|
| `AuditActionFilter` | `Filters/` | 记录 API 操作审计日志 |
| `ModuleAuthorizationFilter` | `Filters/` | 基于 Module 实体的功能模块访问控制 |
| `RequireModuleAttribute` | `Filters/` | 控制器/方法级别声明所需模块 |

---

## 2. 领域模型

### 2.1 实体列表

| 实体 | 说明 | 主要字段 |
|------|------|----------|
| `User` | 用户 | Id, Email, Password, FirstName, LastName, Role, Status |
| `Conversation` | 会话/工单 | Id, Number, Subject, Status, State, MailboxId, CustomerId, UserId |
| `ConversationThread` | 会话线程 | Id, ConversationId, Type, Body, From, UserId, CustomerId |
| `Mailbox` | 邮箱 | Id, Name, Email, Host, Port, Protocol, Username, Password |
| `Customer` | 客户 | Id, FirstName, LastName, Emails, CustomFieldValues |
| `Email` | 邮箱地址 | Id, EmailAddress, Type, CustomerId |
| `Folder` | 文件夹 | Id, Name, Type, MailboxId |
| `Attachment` | 附件 | Id, Filename, ContentType, Size, Path, ThreadId, StoredFileName, FileDir |
| `Tag` | 标签 | Id, Name, Color, MailboxId |
| `ConversationTag` | 会话标签关联 | ConversationId, TagId |
| `Follower` | 关注者 | Id, ConversationId, UserId |
| `SendLog` | 发送日志 | Id, ThreadId, MessageId, Email, Status, MailType |
| `AuditLog` | 审计日志 | Id, UserId, Action, EntityType, EntityId, Changes |
| `SystemOption` | 系统选项 | Id, Name, Value |
| `NotificationTemplate` | 通知模板 | Id, EventType, Subject, Body |
| `NotificationRule` | 通知规则 | Id, EventType, TemplateId, UserId |
| `Notification` | 通知 | Id, UserId, Type, Title, Content, IsRead, ConversationId |
| `CannedResponse` | 快捷回复 | Id, Name, Category, Body, IsShared, MailboxId |
| `SatisfactionRating` | 满意度评价 | Id, ConversationId, CustomerId, Rating, Comment |
| `Workflow` | 工作流规则 | Id, Name, TriggerType, ConditionsJson, ActionsJson |
| `WorkflowExecutionLog` | 工作流执行日志 | Id, WorkflowId, ConversationId, Status |
| `SlaPolicy` | SLA 策略 | Id, Name, FirstResponseHours, ResolutionHours |
| `SlaViolation` | SLA 违规 | Id, SlaPolicyId, ConversationId, ViolationType, Status |
| `Macro` | 宏命令 | Id, Name, ActionsJson, IsActive |
| `MacroExecutionLog` | 宏执行日志 | Id, MacroId, ConversationId, Status |
| `CustomField` | 自定义字段 | Id, Name, Type, EntityType, OptionsJson |
| `CustomFieldValue` | 自定义字段值 | Id, CustomFieldId, EntityId, EntityType, Value |
| `Webhook` | Webhook | Id, Name, Url, Events, IsActive |
| `WebhookLog` | Webhook 日志 | Id, WebhookId, EventType, RequestBody, ResponseStatusCode |
| `Role` | 角色 | Id, Name, Slug, PermissionsJson, IsSystem |
| `UserRole` | 用户角色关联 | UserId, RoleId, MailboxId |
| `MailTemplate` | 邮件模板 | Id, Name, Slug, Type, Subject, BodyHtml, BodyText |
| `Team` | 团队 | Id, Name, MailboxId, IsActive |
| `TeamMember` | 团队成员 | TeamId, UserId, Role |
| `EmailSignature` | 邮件签名 | Id, UserId, MailboxId, Name, Content, IsDefault |
| `Module` | 模块 | Id, Name, Category, IsActive |
| `ApiKey` | API 密钥 | Id, Key, UserId, Name, IsActive |
| `ApiKeyLog` | API 密钥日志 | Id, ApiKeyId, Endpoint, StatusCode |
| `Star` | 收藏 | Id, ConversationId, UserId |
| `PasswordResetToken` | 密码重置令牌 | Id, UserId, Token, ExpiresAt |
| `Translation` | 翻译 | Id, Key, Culture, Value |
| `CalendarEvent` | 日历事件 | Id, Title, Type, StartTime, EndTime, MailboxId, Description |
| `KnowledgeCategory` | 知识库分类 | Id, Name, ParentId, Description |
| `KnowledgeArticle` | 知识库文章 | Id, Title, CategoryId, Content, Status, Views, Likes, IsPinned |
| `ChatSession` | 聊天会话 | Id, Status, CustomerInfo, Messages, MailboxId |
| `RefreshToken` | 刷新令牌 | Id, UserId, Token, ExpiresAt, RevokedAt |
| `UserNotification` | 用户通知 | Id, UserId, Type, Title, Content, IsRead, ConversationId, CreatedAt |
| `TimeEntry` | 工时记录 | Id, UserId, ConversationId, DurationMinutes, Description, EntryDate |
| `CustomReport` | 自定义报表 | Id, Name, Type, ConfigJson, MailboxId, CreatedBy, IsActive |
| `CustomReportSchedule` | 自定义报表调度 | Id, ReportId, Frequency, CronExpression, EmailRecipients, LastRunAt |
| `DistributedLock` | 分布式锁 | Id, Resource, LockId, ExpiresAt |
| `OutlookEventMapping` | Outlook 事件映射 | Id, CalendarEventId, OutlookEventId, OutlookIcalUId, SyncHash, LastSyncedAt |
| `OutlookSyncSetting` | Outlook 同步设置 | Id, UserId, AccessTokenEncrypted, RefreshTokenEncrypted, CalendarIds, Direction, SyncEnabled |

### 2.2 核心实体关系

#### 2.2.1 Conversation（会话）

会话是系统的核心实体，包含以下关键关系：

```
Conversation
    ├── 1:N Threads (ConversationThread)
    ├── 1:1 Mailbox
    ├── 1:1 Customer (可选)
    ├── 1:1 User (分配的客服，可选)
    ├── 1:N Followers
    ├── N:N Tags (通过 ConversationTag)
    ├── N:N Folders (通过 ConversationFolder)
    └── 1:N SatisfactionRatings
```

会话实体包含丰富的业务方法：

- `SetStatus()` - 设置会话状态
- `Close()` - 关闭会话
- `Reopen()` - 重新打开会话
- `AssignTo()` - 分配会话
- `Unassign()` - 取消分配
- `SoftDelete()` - 软删除
- `Restore()` - 恢复
- `AddThread()` - 添加线程
- `AddTag()` / `RemoveTag()` - 管理标签
- `AddFollower()` / `RemoveFollower()` - 管理关注者
- `GetFolderType()` - 计算文件夹类型

#### 2.2.2 ConversationThread（会话线程）

会话线程实体包含以下业务方法（2026-07-29 新增）：

- `IsCustomerMessage()` - 是否为客户消息
- `IsUserReply()` - 是否为用户回复
- `IsNote()` - 是否为内部备注
- `MarkAsDraft()` - 标记为草稿
- `MarkAsDeleted()` - 标记为已删除
- `Restore()` - 恢复已删除的线程
- `SetSendStatus()` - 设置发送状态
- `RecordOpen()` - 记录邮件打开

#### 2.2.3 User（用户）

```
User
    ├── 1:N Conversations (分配的会话)
    ├── 1:N MailboxUsers (邮箱权限)
    ├── N:N Roles (通过 UserRole)
    └── 1:N EmailSignatures
```

#### 2.2.4 Mailbox（邮箱）

```
Mailbox
    ├── 1:N Conversations
    ├── 1:N Folders
    ├── N:N Users (通过 MailboxUser)
    └── 1:N CannedResponses
```

### 2.3 实体业务方法清单（DDD 模式）

系统核心实体封装了业务逻辑，避免贫血领域模型：

| 实体 | 业务方法 | 说明 |
|------|---------|------|
| `Conversation` | `SetStatus()`, `Close()`, `Reopen()`, `AssignTo()`, `Unassign()`, `SoftDelete()`, `Restore()` | 会话生命周期管理 |
| `ConversationThread` | `IsCustomerMessage()`, `IsUserReply()`, `IsNote()`, `MarkAsDraft()`, `MarkAsDeleted()`, `Restore()`, `SetSendStatus()`, `RecordOpen()` | 线程状态与类型判断 |
| `PasswordResetToken` | `MarkAsUsed()`, `Create()` | 令牌使用与创建 |
| `RefreshToken` | `Revoke()`, `Create()` | 令牌撤销与创建 |
| `Webhook` | `ShouldFireForEvent()`, `RecordTrigger()`, `RecordSuccess()`, `RecordFailure()`, `Activate()`, `Deactivate()`, `SoftDelete()`, `Restore()` | Webhook 生命周期 |
| `Attachment` | `IsImage()`, `IsDocument()`, `GetFileExtension()`, `GetFileSizeFormatted()` | 附件类型判断与格式化 |
| `SendLog` | `MarkAsSent()`, `MarkAsFailed()`, `RecordOpen()`, `IsSent()`, `IsFailed()` | 发送状态管理 |
| `SatisfactionRating` | `IsPositive()`, `IsNegative()`, `IsNeutral()` | 满意度分类判断 |
| `Tag` | `IsDeleted()`, `SoftDelete()`, `Restore()`, `IsGlobal()`, `IsMailboxSpecific()` | 标签管理 |
| `Folder` | `IsDeleted()`, `SoftDelete()`, `Restore()`, `IsSystemFolder()`, `IsCustomFolder()` | 文件夹管理 |
| `Workflow` | `MatchesTrigger()`, `AppliesToMailbox()`, `CanExecuteFor()`, `RecordMatch()`, `SoftDelete()`, `Restore()` | 工作流执行与管理 |

### 2.4 枚举定义

#### 2.4.1 ConversationStatus（会话状态）

| 值 | 说明 |
|----|------|
| `Active` | 活跃中 |
| `Pending` | 待处理 |
| `Closed` | 已关闭 |
| `Spam` | 垃圾邮件 |

#### 2.3.2 ConversationState（会话状态）

| 值 | 说明 |
|----|------|
| `Draft` | 草稿 |
| `Published` | 已发布 |
| `Deleted` | 已删除（软删除） |

#### 2.3.3 ConversationType（会话类型）

| 值 | 说明 |
|----|------|
| `Email` | 邮件 |
| `Phone` | 电话 |
| `Chat` | 聊天 |

#### 2.3.4 ThreadType（线程类型）

| 值 | 说明 |
|----|------|
| `Message` | 客服消息 |
| `Customer` | 客户消息 |
| `Note` | 内部备注 |
| `LineItem` | 系统记录 |

#### 2.3.5 UserRoleType（用户角色）

| 值 | 说明 |
|----|------|
| `Admin` | 管理员 |
| `Supervisor` | 客服主管 |
| `Agent` | 客服专员 |
| `Viewer` | 只读用户 |

#### 2.3.6 FolderType（文件夹类型）

| 值 | 说明 |
|----|------|
| `Unassigned` | 未分配 |
| `Mine` | 我的 |
| `Assigned` | 已分配 |
| `Closed` | 已关闭 |
| `Spam` | 垃圾邮件 |

---

## 3. API 接口

### 3.1 认证模块 (`/api/auth`)

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/auth/login` | 用户登录 |
| POST | `/api/auth/register` | 用户注册 |
| POST | `/api/auth/forgot-password` | 忘记密码 |
| POST | `/api/auth/reset-password` | 重置密码 |
| GET | `/api/auth/me` | 获取当前用户信息 |

### 3.2 会话模块 (`/api/conversations`)

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/conversations` | 获取会话列表 |
| GET | `/api/conversations/{id}` | 获取会话详情 |
| POST | `/api/conversations` | 创建会话 |
| PUT | `/api/conversations/{id}` | 更新会话 |
| DELETE | `/api/conversations/{id}` | 删除会话 |
| POST | `/api/conversations/{id}/reply` | 回复会话 |
| POST | `/api/conversations/{id}/assign` | 分配会话 |
| POST | `/api/conversations/{id}/unassign` | 取消分配 |
| POST | `/api/conversations/{id}/close` | 关闭会话 |
| POST | `/api/conversations/{id}/reopen` | 重新打开会话 |
| POST | `/api/conversations/{id}/tags` | 添加标签 |
| DELETE | `/api/conversations/{id}/tags/{tagId}` | 移除标签 |
| POST | `/api/conversations/{id}/follow` | 关注会话 |
| DELETE | `/api/conversations/{id}/follow` | 取消关注 |
| POST | `/api/conversations/bulk` | 批量操作 |

### 3.3 邮箱模块 (`/api/mailboxes`)

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/mailboxes` | 获取邮箱列表 |
| GET | `/api/mailboxes/{id}` | 获取邮箱详情 |
| POST | `/api/mailboxes` | 创建邮箱 |
| PUT | `/api/mailboxes/{id}` | 更新邮箱 |
| DELETE | `/api/mailboxes/{id}` | 删除邮箱 |
| POST | `/api/mailboxes/{id}/fetch` | 手动收取邮件 |
| POST | `/api/mailboxes/{id}/test-connection` | 测试连接 |

### 3.4 客户模块 (`/api/customers`)

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/customers` | 获取客户列表 |
| GET | `/api/customers/{id}` | 获取客户详情 |
| POST | `/api/customers` | 创建客户 |
| PUT | `/api/customers/{id}` | 更新客户 |
| DELETE | `/api/customers/{id}` | 删除客户 |
| GET | `/api/customers/{id}/conversations` | 获取客户会话 |

### 3.5 用户模块 (`/api/users`)

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/users` | 获取用户列表 |
| GET | `/api/users/{id}` | 获取用户详情 |
| POST | `/api/users` | 创建用户 |
| PUT | `/api/users/{id}` | 更新用户 |
| DELETE | `/api/users/{id}` | 删除用户 |
| PUT | `/api/users/{id}/status` | 更新用户状态 |
| PUT | `/api/users/{id}/permissions` | 更新用户权限 |

### 3.6 工作流模块 (`/api/workflows`)

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/workflows` | 获取工作流列表 |
| GET | `/api/workflows/{id}` | 获取工作流详情 |
| POST | `/api/workflows` | 创建工作流 |
| PUT | `/api/workflows/{id}` | 更新工作流 |
| DELETE | `/api/workflows/{id}` | 删除工作流 |
| POST | `/api/workflows/{id}/enable` | 启用工作流 |
| POST | `/api/workflows/{id}/disable` | 禁用工作流 |
| POST | `/api/workflows/{id}/test` | 测试工作流 |

### 3.7 SLA 模块 (`/api/sla`)

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/sla/policies` | 获取 SLA 策略列表 |
| GET | `/api/sla/policies/{id}` | 获取策略详情 |
| POST | `/api/sla/policies` | 创建策略 |
| PUT | `/api/sla/policies/{id}` | 更新策略 |
| DELETE | `/api/sla/policies/{id}` | 删除策略 |
| GET | `/api/sla/violations` | 获取违规列表 |

### 3.8 宏模块 (`/api/macros`)

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/macros` | 获取宏列表 |
| GET | `/api/macros/{id}` | 获取宏详情 |
| POST | `/api/macros` | 创建宏 |
| PUT | `/api/macros/{id}` | 更新宏 |
| DELETE | `/api/macros/{id}` | 删除宏 |
| POST | `/api/macros/{id}/execute` | 执行宏 |

### 3.9 Webhook 模块 (`/api/webhooks`)

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/webhooks` | 获取 Webhook 列表 |
| GET | `/api/webhooks/{id}` | 获取 Webhook 详情 |
| POST | `/api/webhooks` | 创建 Webhook |
| PUT | `/api/webhooks/{id}` | 更新 Webhook |
| DELETE | `/api/webhooks/{id}` | 删除 Webhook |
| GET | `/api/webhooks/{id}/logs` | 获取 Webhook 日志 |
| POST | `/api/webhooks/{id}/test` | 测试 Webhook |

### 3.10 标签模块 (`/api/tags`)

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/tags` | 获取标签列表 |
| GET | `/api/tags/{id}` | 获取标签详情 |
| POST | `/api/tags` | 创建标签 |
| PUT | `/api/tags/{id}` | 更新标签 |
| DELETE | `/api/tags/{id}` | 删除标签 |

### 3.11 报告模块 (`/api/reports`)

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/reports/dashboard` | 获取仪表盘数据 |
| GET | `/api/reports/conversations` | 获取会话统计 |
| GET | `/api/reports/satisfaction` | 获取满意度统计 |
| GET | `/api/reports/sla` | 获取 SLA 统计 |
| GET | `/api/reports/agents` | 获取客服统计 |

### 3.12 快捷回复模块 (`/api/canned-responses`)

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/canned-responses` | 获取快捷回复列表 |
| GET | `/api/canned-responses/{id}` | 获取快捷回复详情 |
| POST | `/api/canned-responses` | 创建快捷回复 |
| PUT | `/api/canned-responses/{id}` | 更新快捷回复 |
| DELETE | `/api/canned-responses/{id}` | 删除快捷回复 |

### 3.13 邮件模板模块 (`/api/mail-templates`)

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/mail-templates` | 获取邮件模板列表 |
| GET | `/api/mail-templates/{id}` | 获取邮件模板详情 |
| POST | `/api/mail-templates` | 创建邮件模板 |
| PUT | `/api/mail-templates/{id}` | 更新邮件模板 |
| DELETE | `/api/mail-templates/{id}` | 删除邮件模板 |
| POST | `/api/mail-templates/{id}/preview` | 预览邮件模板 |

### 3.14 团队模块 (`/api/teams`)

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/teams` | 获取团队列表 |
| GET | `/api/teams/{id}` | 获取团队详情 |
| POST | `/api/teams` | 创建团队 |
| PUT | `/api/teams/{id}` | 更新团队 |
| DELETE | `/api/teams/{id}` | 删除团队 |
| POST | `/api/teams/{id}/members` | 添加成员 |
| DELETE | `/api/teams/{id}/members/{userId}` | 移除成员 |

### 3.15 审计日志模块 (`/api/audit-logs`)

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/audit-logs` | 获取审计日志列表 |
| GET | `/api/audit-logs/{id}` | 获取审计日志详情 |

### 3.16 数据导入导出模块 (`/api/import-export`)

| 方法 | 路径 | 说明 |
|------|------|------|
| POST | `/api/import-export/import` | 导入数据 |
| POST | `/api/import-export/export` | 导出数据 |

### 3.17 日历模块 (`/api/calendar`)

| 方法 | 路径 | 说明 | 所需权限 |
|------|------|------|----------|
| GET | `/api/calendar/events` | 获取日历事件列表 | `calendar.view` |
| GET | `/api/calendar/events/{id}` | 获取事件详情 | `calendar.view` |
| POST | `/api/calendar/events` | 创建事件 | `calendar.manage` |
| PUT | `/api/calendar/events/{id}` | 更新事件 | `calendar.manage` |
| DELETE | `/api/calendar/events/{id}` | 删除事件 | `calendar.manage` |
| GET | `/api/calendar/overview` | 获取日历概览 | `calendar.view` |

> **权限说明（v2.2.7 修复）**：`calendar.manage` 权限默认授予 `admin`、`supervisor`（客服主管）和 `agent`（客服专员）角色。v2.2.6 版本中 `supervisor` 和 `agent` 角色缺少此权限，已在 v2.2.7 中修复（T000092）。

### 3.18 知识库模块 (`/api/knowledge`)

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/knowledge/categories` | 获取分类列表 |
| POST | `/api/knowledge/categories` | 创建分类 |
| PUT | `/api/knowledge/categories/{id}` | 更新分类 |
| DELETE | `/api/knowledge/categories/{id}` | 删除分类 |
| GET | `/api/knowledge/articles` | 获取文章列表 |
| GET | `/api/knowledge/articles/{id}` | 获取文章详情 |
| POST | `/api/knowledge/articles` | 创建文章 |
| PUT | `/api/knowledge/articles/{id}` | 更新文章 |
| DELETE | `/api/knowledge/articles/{id}` | 删除文章 |
| POST | `/api/knowledge/articles/{id}/pin` | 置顶/取消置顶 |
| GET | `/api/knowledge/overview` | 获取知识库概览 |

### 3.19 在线聊天模块 (`/api/chat`)

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/chat/sessions` | 获取聊天会话列表 |
| GET | `/api/chat/sessions/{id}` | 获取会话详情 |
| POST | `/api/chat/sessions` | 创建聊天会话 |
| POST | `/api/chat/sessions/{id}/messages` | 发送消息 |
| POST | `/api/chat/sessions/{id}/transfer` | 转为工单 |
| POST | `/api/chat/sessions/{id}/close` | 关闭会话 |
| GET | `/api/chat/overview` | 获取聊天概览 |

### 3.20 个人中心模块 (`/api/profile`)

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/profile` | 获取当前用户资料 |
| PUT | `/api/profile` | 更新个人资料 |
| POST | `/api/profile/avatar` | 上传头像 |
| DELETE | `/api/profile/avatar` | 移除头像 |
| PUT | `/api/profile/password` | 修改密码 |
| GET | `/api/profile/password-policy` | 获取密码策略 |

---

## 4. 服务层

### 4.1 服务接口与实现

服务层采用接口-实现分离的设计模式：

| 接口 | 实现 | 职责 |
|------|------|------|
| `IAuthService` | `AuthService` | 用户认证、注册、密码管理 |
| `IConversationService` | `ConversationService` | 会话管理 |
| `IMailboxService` | `MailboxService` | 邮箱管理 |
| `IUserService` | `UserService` | 用户管理 |
| `ICustomerService` | `CustomerService` | 客户管理 |
| `IFolderService` | `FolderService` | 文件夹管理 |
| `IDashboardService` | `DashboardService` | 仪表盘数据 |
| `ITagService` | `TagService` | 标签管理 |
| `ISystemOptionService` | `SystemOptionService` | 系统选项 |
| `INotificationService` | `NotificationService` | 通知管理 |
| `IReportService` | `ReportService` | 报表服务 |
| `ICannedResponseService` | `CannedResponseService` | 快捷回复 |
| `ISendLogService` | `SendLogService` | 发送日志 |
| `ISatisfactionRatingService` | `SatisfactionRatingService` | 满意度评价 |
| `IWorkflowService` | `WorkflowService` | 工作流管理 |
| `ISlaService` | `SlaService` | SLA 管理 |
| `IMacroService` | `MacroService` | 宏管理 |
| `ICustomFieldService` | `CustomFieldService` | 自定义字段 |
| `IWebhookService` | `WebhookService` | Webhook 管理 |
| `IRoleService` | `RoleService` | 角色管理 |
| `IMailTemplateService` | `MailTemplateService` | 邮件模板 |
| `ITeamService` | `TeamService` | 团队管理 |
| `IEmailSignatureService` | `EmailSignatureService` | 邮件签名 |
| `IModuleService` | `ModuleService` | 模块管理 |
| `IApiKeyService` | `ApiKeyService` | API 密钥 |
| `IImportExportService` | `ImportExportService` | 数据导入导出 |
| `ICalendarService` | `CalendarService` | 日历事件管理 |
| `IKnowledgeService` | `KnowledgeService` | 知识库管理 |
| `IChatService` | `ChatService` | 在线聊天 |
| `IPasswordPolicyService` | `PasswordPolicyService` | 密码策略校验 |
| `ITimeEntryService` | `TimeEntryService` | 工时记录管理 |
| `ICustomReportService` | `CustomReportService` | 自定义报表 |
| `IEncryptionService` | `EncryptionService` | 敏感字段加密/解密 |
| `IOutlookSyncService` | `OutlookSyncService` | Outlook 日历双向同步 |
| `IOutlookOAuthService` | `OutlookOAuthService` | Outlook OAuth 授权流程 |
| `IOutlookGraphService` | `OutlookGraphService` | Outlook Graph API 调用封装 |
| `ICalendarExportService` | `CalendarExportService` | 日历 ICS 导入导出 |
| `IDistributedLockService` | `DistributedLockService` | 分布式锁管理 |

### 4.2 基础设施服务

| 服务 | 职责 |
|------|------|
| `EmailService` | 邮件发送 |
| `EmailProcessingService` | 邮件处理（解析、提取） |
| `EmailQueueService` | 邮件队列管理 |
| `AttachmentService` | 附件管理 |
| `AuditService` | 审计服务 |
| `CaptchaService` | 验证码服务 |
| `LocalizationService` | 本地化服务 |
| `NotificationHub` | SignalR 通知中心 |
| `NotificationHubService` | 通知中心服务 |
| `NotificationPublisher` | 通知发布器 |
| `DomainEventDispatcher` | 领域事件分发器 |
| `BCryptPasswordHasher` | BCrypt 密码哈希 |
| `IFrontendUrlResolver` | 前端 URL 动态解析（HTTP 上下文可用时从当前请求提取域名，后台任务回退到配置值） |

### 4.3 服务注册

服务注册通过依赖注入扩展方法完成：

- [Application/DependencyInjection.cs](../NoneadCSS.Application/DependencyInjection.cs) - 注册应用层服务
- [Infrastructure/DependencyInjection.cs](../NoneadCSS.Infrastructure/DependencyInjection.cs) - 注册基础设施服务

### 4.4 多渠道集成服务（微信 / WhatsApp / 钉钉 / 飞书）

| 接口 | 实现 | 职责 |
|------|------|------|
| `IWeChatService` | `WeChatService` | 微信公众号客服消息回复、客户档案、素材上传 |
| `IWeChatAccessTokenService` | `WeChatAccessTokenService` | 微信 access_token 获取与缓存 |
| `IWeChatIngestionService` | `WeChatIngestionService` | 微信回调消息解析、入库、会话创建 |
| `IWeChatClient` | `WeChatClient` | 微信 API HTTP 调用封装 |
| `IWeChatSettingsProvider` | `WeChatSettingsProvider` | 微信设置（数据库合并 appsettings）获取 |
| `IWhatsAppService` | `WhatsAppService` | WhatsApp Cloud API 消息发送、已读回执 |
| `IWhatsAppIngestionService` | `WhatsAppIngestionService` | WhatsApp Webhook 解析 + 会话入库 |
| `IWhatsAppClient` | `WhatsAppClient` | Meta Cloud API HTTP 调用封装 |
| `IWhatsAppSettingsProvider` | `WhatsAppSettingsProvider` | WhatsApp 敏感配置（AES 加密）读取 |
| `IDingDingService` | `DingDingService` | 钉钉应用消息发送、用户信息拉取 |
| `IDingDingIngestionService` | `DingDingIngestionService` | 钉钉回调/Stream 消息消费 |
| `IDingDingClient` | `DingDingClient` | 钉钉开放平台 HTTP 调用封装 |
| `IDingDingSettingsProvider` | `DingDingSettingsProvider` | 钉钉 AppKey/AppSecret/AgentId 配置获取 |
| `IFeishuService` | `FeishuService` | 飞书消息发送、用户档案 |
| `IFeishuIngestionService` | `FeishuIngestionService` | 飞书事件订阅回调解析 |
| `IFeishuClient` | `FeishuClient` | 飞书开放平台 HTTP 调用封装 |
| `IFeishuAccessTokenService` | `FeishuAccessTokenService` | 飞书 tenant_access_token 获取与缓存 |
| `IFeishuSettingsProvider` | `FeishuSettingsProvider` | 飞书应用配置获取 |

**集成服务通用约定**：
- 设置提供层（SettingsProvider）优先从数据库（SystemOption 或模块配置表）读取，缺失时回退到 `appsettings.json`
- 启动预加载：敏感配置（如微信 token）由 `BootstrapService` 在应用启动时预热，避免首次请求冷启动
- 外部 API 调用：使用 Polly 重试策略（微信 40001/40014/42001 自动刷新 token 并重试 1 次）
- 接收端异常隔离：`IngestionService.IngestAsync` 包裹 try-catch，单条消息失败写日志 + 继续处理下一条，不阻塞收取主流程

---

## 5. 数据访问层

### 5.1 仓储模式

采用 Repository 模式，每个实体对应一个仓储接口和实现：

| 接口 | 实现 |
|------|------|
| `IUserRepository` | `UserRepository` |
| `IConversationRepository` | `ConversationRepository` |
| `IMailboxRepository` | `MailboxRepository` |
| `ICustomerRepository` | `CustomerRepository` |
| `IAttachmentRepository` | `AttachmentRepository` |
| `IFolderRepository` | `FolderRepository` |
| `IAuditLogRepository` | `AuditLogRepository` |
| `IThreadRepository` | `ThreadRepository` |
| `ITagRepository` | `TagRepository` |
| `ISystemOptionRepository` | `SystemOptionRepository` |
| `INotificationTemplateRepository` | `NotificationTemplateRepository` |
| `INotificationRuleRepository` | `NotificationRuleRepository` |
| `IPasswordResetTokenRepository` | `PasswordResetTokenRepository` |
| `ICannedResponseRepository` | `CannedResponseRepository` |
| `ISatisfactionRatingRepository` | `SatisfactionRatingRepository` |
| `IWorkflowRepository` | `WorkflowRepository` |
| `ISlaRepository` | `SlaRepository` |
| `IMacroRepository` | `MacroRepository` |
| `ICustomFieldRepository` | `CustomFieldRepository` |
| `IWebhookRepository` | `WebhookRepository` |
| `IRoleRepository` | `RoleRepository` |
| `IMailTemplateRepository` | `MailTemplateRepository` |
| `ITeamRepository` | `TeamRepository` |
| `IEmailSignatureRepository` | `EmailSignatureRepository` |
| `IModuleRepository` | `ModuleRepository` |
| `IApiKeyRepository` | `ApiKeyRepository` |
| `ITranslationRepository` | `TranslationRepository` |
| `ICalendarEventRepository` | `CalendarEventRepository` |
| `IChatSessionRepository` | `ChatSessionRepository` |

### 5.2 Unit of Work

[IUnitOfWork](../NoneadCSS.Domain/IUnitOfWork.cs) 接口管理事务和保存更改：

```csharp
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    int SaveChanges();
    
    // 专用仓储（需要特殊查询逻辑）
    IUserRepository Users { get; }
    IConversationRepository Conversations { get; }
    IMailboxRepository Mailboxes { get; }
    // ... 其他专用仓储
    
    // 泛型仓储（仅基础 CRUD）
    IRepository<T> Repository<T>() where T : class;
}
```

实现类 [UnitOfWork](../NoneadCSS.Infrastructure/UnitOfWork.cs) 包装了 EF Core 的 `DbContext.SaveChangesAsync()`。

#### 仓储接口精简（2026-07-29）

对于仅使用基础 CRUD 功能的实体，不再在 `IUnitOfWork` 中定义专用属性，改用泛型方法 `Repository<T>()` 动态获取：

| 原属性 | 改用方式 |
|--------|---------|
| `Followers` | `Repository<Follower>()` |
| `Emails` | `Repository<Email>()` |
| `ConversationFolders` | `Repository<ConversationFolder>()` |
| `MailboxUsers` | `Repository<MailboxUser>()` |
| `ConversationTags` | `Repository<ConversationTag>()` |
| `Stars` | `Repository<Star>()` |

**优点**：
- 减少接口膨胀，`IUnitOfWork` 从 40+ 个属性降至 20+ 个
- 保持专用仓储用于复杂查询场景（如 `IConversationRepository`）
- 泛型仓储满足 80% 的基础 CRUD 需求

### 5.3 数据库上下文

[NoneadCSSDbContext](../NoneadCSS.Infrastructure/Persistence/NoneadCSSDbContext.cs) 继承自 `DbContext`，包含：

- 所有实体的 `DbSet<T>` 属性
- `OnModelCreating()` 中的实体关系配置
- 索引配置
- 约束配置

### 5.4 数据库支持

系统支持两种数据库：

| 数据库 | 配置 | 适用场景 |
|--------|------|----------|
| SQLite | `"Database:UseSqlite": true` | 开发、测试、轻量部署 |
| SQL Server | `"Database:UseSqlite": false` | 生产环境、高并发 |

连接字符串配置：

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=NoneadCSS;Trusted_Connection=True",
    "SqliteConnection": "Data Source=NoneadCSS.db"
  }
}
```

### 5.5 审计拦截器

[AuditSaveChangesInterceptor](../NoneadCSS.Infrastructure/Persistence/AuditSaveChangesInterceptor.cs) 在每次保存更改时自动记录审计日志。

### 5.6 数据库迁移

使用 EF Core Migrations 管理数据库版本：

```bash
# 创建新迁移
cd NoneadCSS.Api
dotnet ef migrations add <MigrationName> --project ../NoneadCSS.Infrastructure

# 应用迁移
dotnet ef database update --project ../NoneadCSS.Infrastructure

# 移除最后一个迁移
dotnet ef migrations remove --project ../NoneadCSS.Infrastructure
```

### 5.7 种子数据

[DatabaseSeeder](../NoneadCSS.Api/DatabaseSeeder.cs) 在应用启动时自动初始化数据库和种子数据：

- 默认管理员账户 (`admin@example.com`)
- 测试邮箱 (`support@example.com`)
- 默认文件夹（未分配、我的、已关闭）
- 测试客户和会话
- 快捷回复模板
- 工作流规则
- SLA 策略
- 宏命令
- 自定义字段
- Webhook 配置
- 角色与权限
- 邮件模板

### 5.8 EF Core 变更追踪最佳实践

#### 问题背景

EF Core 的 ChangeTracker 会追踪所有加载的实体。当使用 `AsNoTracking()` 查询获取实体后，再调用 `UpdateAsync()` 将实体附加回 ChangeTracker 时，如果 ChangeTracker 中已存在相同键值的实体（如 User、Customer 等关联实体），会抛出以下错误：

```
The instance of entity type 'User' cannot be tracked because another instance with the same key value for {'Id'} is already being tracked.
```

#### 推荐模式：追踪查询 + 直接修改

**不要**这样做（AsNoTracking 查询 + UpdateAsync 附加）：

```csharp
// ❌ 错误模式
var conversation = await _unitOfWork.Conversations.GetByIdAsync(id); // AsNoTracking，带 Include
// 修改属性...
await _unitOfWork.Conversations.UpdateAsync(conversation); // 附加整个对象图，可能导致追踪冲突
await _unitOfWork.SaveChangesAsync();
```

**应该**这样做（追踪查询 + 直接修改属性 + SaveChanges）：

```csharp
// ✅ 正确模式
var conversation = await _unitOfWork.Conversations.GetByIdForUpdateAsync(id); // 追踪查询，无 Include
conversation.Priority = newPriority; // 直接修改属性
conversation.UpdatedAt = DateTime.UtcNow;
await _unitOfWork.SaveChangesAsync(); // ChangeTracker 自动检测变化并生成更新
```

#### Repository 方法约定

| 方法 | 追踪状态 | Include | 适用场景 |
|------|---------|---------|---------|
| `GetByIdAsync(id)` | `AsNoTracking()` | 完整 Include（User、Customer、Threads 等） | 读取展示用，不需要修改 |
| `GetByIdForUpdateAsync(id)` | 追踪（默认） | 无 Include | 需要修改实体自身属性 |
| `GetDeletedByIdAsync(id)` | `AsNoTracking()` | 完整 Include | 读取已删除实体 |
| `GetDeletedTrackedByIdAsync(id)` | 追踪（默认） | 无 Include | 恢复已删除实体 |

#### 何时使用 UpdateAsync

仅在以下场景使用 `UpdateAsync`：
1. 实体完全来自外部（如 API 请求体映射的 DTO → 实体）
2. 需要更新整个对象图且确定没有追踪冲突
3. 实体是新建的但需要设置某些导航属性

大多数情况下，使用追踪查询 + 直接修改属性是更安全的选择。

---

## 6. 事件系统

### 6.1 领域事件

当前保留的强类型领域事件共 10 个（`NoneadCSS.Domain/Events/DomainEvents.cs`）。此前已删除 6 个「功能已在别处内联、接线只会重复」的事件及其监听器：`UserDeletedEvent`、`RefreshConversationsEvent`、`SwiftMessageReceivedEvent`（SWIFT 通道在本仓库不存在）、`UserCreatedConversationEvent`、`UserAddedNoteEvent`、`DraftCreatedEvent`。

| 事件 | 发布点 | 触发时机 |
|------|--------|----------|
| `UserLoggedInEvent` | `AuthService.LoginAsync`（凭据保存之后） | 登录成功 |
| `PasswordChangedEvent` | `UserService.ChangePasswordAsync`（SaveChanges 之后） | 密码修改成功 |
| `PasswordResetEvent` | **暂无发布点**（监听器保留但未接线） | — |
| `LoginFailedEvent` | `AuthService.LoginAsync` 各失败分支 | 登录失败 |
| `LockoutEvent` | `AuthService.LoginAsync` 已锁定分支 / 达到失败阈值时 | 用户锁定 |
| `UserActivatedEvent` | `UserService.ActivateUserAsync`（SaveChanges 之后） | 用户激活 |
| `MailboxUpdatedEvent` | `MailboxService.UpdateMailboxAsync`（SaveChanges 之后） | 邮箱配置更新 |
| `ConversationStatusChangedEvent` | `ConversationStatusService.UpdateStatusAsync`（事务提交之后；批量接口不发布） | 会话状态变更 |
| `ConversationUserChangedEvent` | `ConversationAssignmentService.AssignAsync`（事务提交之后；批量接口不发布） | 会话分配变更 |
| `UserRepliedEvent` | `ConversationThreadService.AddReplyAsync`（回复保存之后；备注不算回复） | 坐席回复 |

### 6.2 事件监听器

每个事件对应一个或多个监听器（`NoneadCSS.Infrastructure/Listeners/`，共 10 个）。监听器只承担「服务层没有等价实现」的那部分副作用，与 `AuditActionFilter` / `INotificationPublisher` 重复的写入已删除：

| 监听器 | 处理事件 | 职责 |
|--------|----------|------|
| `RememberUserLocaleListener` | `UserLoggedInEvent` | 把本次登录语言回写 `Users.Locale` |
| `SendPasswordChangedListener` | `PasswordChangedEvent` | 站内通知 + 密码变更安全提醒邮件 |
| `LogPasswordResetListener` | `PasswordResetEvent` | 记录密码重置日志（事件未接线，暂不执行） |
| `LogFailedLoginListener` | `LoginFailedEvent` | 写审计 `action="loginfailed"`（仪表盘异常事件数依赖） |
| `LogLockoutListener` | `LockoutEvent` | 写审计 `action="lockout"` |
| `ActivateUserListener` | `UserActivatedEvent` | 发送「账号已激活」站内通知（激活本身由 `UserService` 完成） |
| `UpdateMailboxCountersListener` | `MailboxUpdatedEvent` | 排程 `UpdateFolderCountersJob` 刷新邮箱文件夹计数 |
| `ConversationStatusChangedListener` | `ConversationStatusChangedEvent` | 发送会话状态变更站内通知（审计与 SignalR 已在别处） |
| `ConversationUserChangedListener` | `ConversationUserChangedEvent` | 给被分配坐席发站内通知（审计与 SignalR 已在别处） |
| `UserRepliedListener` | `UserRepliedEvent` | 给会话关注者发站内通知（审计 `action="reply"` 已在别处） |

### 6.3 事件分发

强类型领域事件通过 `IDomainEventDispatcher` 分发：

```csharp
// 强类型领域事件分发器（接口在 NoneadCSS.Domain/Events/IDomainEventDispatcher.cs，
// 实现在 NoneadCSS.Infrastructure/Services/DomainEventDispatcher.cs）
builder.Services.AddSingleton<NoneadCSS.Domain.Events.IDomainEventDispatcher, NoneadCSS.Infrastructure.Services.DomainEventDispatcher>();

// 每个监听器按 IListener<TEvent> 注册，分发器用 CreateAsyncScope + GetServices 解析全部实现
builder.Services.AddScoped<IListener<LoginFailedEvent>, LogFailedLoginListener>();

// 通知发布器
services.AddSingleton<INotificationPublisher, NotificationPublisher>();
```

> 早期还有一套 Eventy 风格的字符串事件层（`IEventDispatcher` / `EventDispatcher`，支持 `Before`/`After`/`Filter` 挂点），
> 但全仓从未注册任何挂点，13 处 `Trigger(...)` 调用只写日志、没有任何订阅者，因此连同注册口一并移除（2026-11-21）。
> 实时通知走 `INotificationPublisher`，审计走 `AuditActionFilter`，副作用监听走上面的强类型管道。

分发器会为每个监听器新建异步作用域（独立 `DbContext`），因此事件必须在数据提交之后发布；单个监听器抛出的异常由分发器捕获并记录日志，不影响业务主流程。

---

## 7. 定时任务

使用 Quartz.NET 实现定时任务：

### 7.1 任务列表

| 任务 | 触发频率 | 职责 |
|------|----------|------|
| `FetchEmailJob` | 默认 5 分钟（优先读 SystemOption `mail.fetch_frequency`，回退 `Quartz:Jobs:FetchEmail` / `Email:FetchCron`） | 收取邮箱邮件（按每个邮箱的 `FetchFrequency` 决定实际收取） |
| `ProcessEmailQueueJob` | 默认 1 分钟（`Quartz:Jobs:ProcessEmailQueue`） | 处理邮件发送队列 |
| `ProcessImMessageQueueJob` | 默认 1 分钟（`Quartz:Jobs:ProcessImMessageQueue`） | 处理 IM 渠道出站队列（微信/飞书/WhatsApp/钉钉/Messenger/Telegram/LINE/iMessage），失败按指数退避重试 |
| `SlaScanJob` | 默认 30 分钟（`Quartz:Jobs:SlaScan`） | 扫描 SLA 违规情况并发送告警 |
| `CalendarReminderJob` | 默认 1 分钟（`Quartz:Jobs:CalendarReminder`） | 扫描到期的日历提醒并推送 |
| `CalendarReminderFallbackJob` | 固定 1 分钟（`0 * * * * ?`） | 提醒推送兜底补发（多级兜底，见 §3.2 #7.3） |
| `PurgeCalendarTrashJob` | 默认每天 03:00（`Quartz:Jobs:PurgeCalendarTrash`） | 清理日历回收站过期条目 |
| `OutlookCalendarSyncJob` | 默认 30 分钟（`Quartz:Jobs:OutlookCalendarSync`） | Outlook 日历双向同步 |
| `ProcessCustomReportSchedulesJob` | 默认 5 分钟（`Quartz:Jobs:ProcessCustomReportSchedules`） | 执行自定义报表定时任务 |
| `PurgeOutlookSyncLogJob` | 默认每天 03:30（`Quartz:Jobs:PurgeOutlookSyncLog`） | 清理过期的 Outlook 同步审计日志 |
| `UpdateFolderCountersJob` | 按需（由 `MailboxUpdatedEvent` 的监听器投递） | 更新邮箱/文件夹计数器 |

> 注册代码在 `NoneadCSS.Infrastructure/DependencyInjection.cs:187-282`（`AddQuartz` 块内 10 个 Cron 任务 + `UpdateFolderCountersJob` 由监听器按需调度）。曾有 4 个从未注册的作业（`SendAlertJob`、`SendEmailReplyErrorJob`、`SendNotificationToUsersJob`、`TriggerActionJob`）已随本轮清理移除。

### 7.2 配置

```csharp
services.AddQuartz(q =>
{
    // 邮件收取 Cron：优先从 SystemOptions mail.fetch_frequency 读取，回退到 Quartz:Jobs:FetchEmail / Email:FetchCron / 默认 5 分钟
    var fetchEmailCron = ResolveFetchEmailCronFromSystemOptions(configuration);
    var processEmailQueueCron = configuration["Quartz:Jobs:ProcessEmailQueue"] ?? "0 * * * * ?";

    var fetchEmailJobKey = new JobKey("FetchEmailJob");
    q.AddJob<FetchEmailJob>(opts => opts.WithIdentity(fetchEmailJobKey));
    q.AddTrigger(opts => opts
        .ForJob(fetchEmailJobKey)
        .WithIdentity("FetchEmailTrigger")
        .WithCronSchedule(fetchEmailCron));

    var processEmailQueueJobKey = new JobKey("ProcessEmailQueueJob");
    q.AddJob<ProcessEmailQueueJob>(opts => opts.WithIdentity(processEmailQueueJobKey));
    q.AddTrigger(opts => opts
        .ForJob(processEmailQueueJobKey)
        .WithIdentity("ProcessEmailQueueTrigger")
        .WithCronSchedule(processEmailQueueCron));
});

services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
```

> **配置说明**：Cron 表达式现在从 `appsettings.json` 的 `Quartz:Jobs` 配置节读取，可在不修改代码的情况下调整各任务的触发频率。

#### Job 执行约定

所有 Job 遵循以下实现约定：

- **`[DisallowConcurrentExecution]`**：所有 Job 类均标注 `[DisallowConcurrentExecution]` 特性，防止同一 Job 的多个实例并发执行，避免重复处理与数据竞争。
- **`IServiceProvider.CreateScope()` 模式**：所有 Job 在 `Execute` 方法内部通过 `IServiceProvider.CreateScope()` 创建子作用域解析 Scoped 服务（如 `IUnitOfWork`、仓储、服务），不直接捕获根容器的依赖，避免 DbContext 等 Scoped 服务被提前释放或跨请求复用。
- **`RetryableJobBase` 基类**：需要重试的 Job 继承自 `RetryableJobBase` 基类，提供统一的异常捕获、重试次数与退避策略，子类只需实现核心业务逻辑。

---

## 8. 认证授权

### 8.1 JWT 认证

使用 JWT Bearer 认证，以下是关键安全配置要点：

```csharp
var jwtKey = builder.Configuration["Jwt:SecretKey"] 
    ?? throw new InvalidOperationException("JWT SecretKey is not configured.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"];
var jwtAudience = builder.Configuration["Jwt:Audience"];

// 密钥长度校验：至少 32 字节
if (Encoding.UTF8.GetByteCount(jwtKey) < 32)
    throw new InvalidOperationException("JWT SecretKey must be at least 32 bytes long.");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtIssuer,
        ValidAudience = jwtAudience,
        // 使用 UTF8 编码，支持非 ASCII 字符密钥
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        // ClockSkew 归零，令牌过期立即失效，无默认 5 分钟宽限期
        ClockSkew = TimeSpan.Zero
    };
});

builder.Services.AddAuthorization();
```

#### 安全配置要点

| 配置项 | 值 | 说明 |
|--------|----|------|
| 密钥编码 | `Encoding.UTF8` | 支持非 ASCII 字符密钥，避免密钥损坏 |
| ClockSkew | `TimeSpan.Zero` | 消除默认 5 分钟宽限期，令牌精确过期 |
| 密钥最小长度 | ≥ 32 字节 | 确保 HMAC-SHA256 签名安全性 |
| 有效期单位 | `AddMinutes` | 与配置项 `AccessTokenExpireMinutes` 命名一致 |
| 缺失密钥处理 | 抛出异常 | 禁止空回退，启动即失败避免运行时安全问题 |

#### Refresh Token 安全机制

- **Token 轮换（Rotation）**：每次刷新令牌生成新的 Refresh Token，旧令牌标记为已撤销
- **重用检测（Reuse Detection）**：检测到已撤销的 Refresh Token 被使用时，立即撤销该用户所有 Refresh Token，防御令牌泄露后的持续利用
- **事务原子性**：新令牌生成与旧令牌撤销在同一事务内提交，避免轮换过程中产生多有效令牌
- **SameSite=Strict**：Refresh Token Cookie 设置 `SameSite=Strict`，防御 CSRF 攻击

#### 登录失败锁定

- **失败计数**：`User.FailedLoginAttempts` 记录连续失败次数
- **锁定时间**：`User.LockoutEnd` 记录锁定结束时间
- **锁定策略**：连续 5 次失败 → 锁定 15 分钟
- **解锁机制**：锁定时间过期后自动解锁，成功登录后重置失败计数

### 8.2 角色权限

系统预定义四种角色：

| 角色 | Slug | 说明 | 日历权限 |
|------|------|------|----------|
| 超级管理员 | `admin` | 拥有所有权限 | 全部（含垃圾箱、永久删除） |
| 客服主管 | `supervisor` | 管理团队和查看报表 | 创建/编辑/删除（下属和自己） |
| 客服专员 | `agent` | 处理分配的会话 | 创建/编辑/删除（自己的） |
| 只读用户 | `viewer` | 只能查看会话和客户 | 仅查看 |

权限通过 `PermissionsJson` 字段存储，支持邮箱级别的权限隔离。

### 8.3 API 密钥认证

除 JWT 外，系统还支持 API 密钥认证，通过 `ApiKey` 实体管理。

### 8.4 CSRF 防护

[CsrfProtectionMiddleware](../NoneadCSS.Api/Middleware/CsrfProtectionMiddleware.cs) 对不安全的 HTTP 方法（POST/PUT/PATCH/DELETE）进行 CSRF 防护：

- **Token 校验**：请求需携带 CSRF Token（通过 Header 或 Cookie 传入），中间件将其与服务端存储的 Token 比对
- **常量时间比较**：使用 `CryptographicOperations.FixedTimeEquals` 进行 Token 比较，防止时序攻击（timing attack）泄露 Token 信息
- **豁免规则**：JWT Bearer 认证的 API 请求（携带 `Authorization: Bearer` 头）以及 GET/HEAD/OPTIONS 等安全方法豁免 CSRF 校验
- **Token 生成**：登录成功后由服务端生成随机 Token 并通过响应返回，前端在后续写请求中附带

### 8.5 安全响应头

[SecurityHeadersMiddleware](../NoneadCSS.Api/Middleware/SecurityHeadersMiddleware.cs) 统一为响应添加安全相关 HTTP 头，共 6 种安全响应头：

| 响应头 | 值 | 作用 |
|--------|----|------|
| **Content-Security-Policy** | 配置化 | 内容安全策略，限制资源加载来源，防御 XSS 和数据注入；采用 nonce-based 策略，每请求生成一次性 nonce |
| **X-Content-Type-Options** | `nosniff` | 阻止浏览器 MIME 类型嗅探，防止 MIME 混淆攻击 |
| **X-Frame-Options** | `DENY` | 防止点击劫持（clickjacking），禁止页面被嵌入 iframe |
| **Referrer-Policy** | `strict-origin-when-cross-origin` | 控制 Referer 头信息泄露，跨域请求仅发送源站 |
| **Permissions-Policy** | 配置化 | 限制浏览器功能权限（摄像头、麦克风、地理位置、支付等） |
| **X-XSS-Protection** | `0` | 显式禁用已废弃的原生 XSS 防护，改用 CSP（避免浏览器内置防护被绕过） |

#### 配置方式

通过 `appsettings.json` 的 `SecurityHeaders` 配置节启用/禁用各安全头：

```json
{
  "SecurityHeaders": {
    "EnableCSP": true,
    "EnableXContentTypeOptions": true,
    "EnableXFrameOptions": true,
    "EnableReferrerPolicy": true,
    "EnablePermissionsPolicy": true,
    "EnableXXSSProtection": true,
    "ReportOnly": false
  }
}
```

#### ForwardedHeaders 配置

在 `Program.cs` 中配置 `ForwardedHeadersOptions`，正确识别反向代理（Nginx/Docker）后的真实客户端 IP 与协议，用于审计日志与限流。信任代理列表从配置读取，不清空 `KnownProxies`。

---

## 9. 国际化

### 9.1 支持语言

- 简体中文 (`zh-Hans`)
- 英文 (`en`)

### 9.2 配置

```csharp
var supportedCultures = new[]
{
    new CultureInfo("zh-Hans"),
    new CultureInfo("en")
};

builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    options.DefaultRequestCulture = new RequestCulture("zh-Hans");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;

    options.RequestCultureProviders.Insert(0, new QueryStringRequestCultureProvider
    {
        QueryStringKey = "lang",
        UIQueryStringKey = "lang"
    });
});
```

### 9.3 资源文件

本地化资源存储在 [Application/Resources/](../NoneadCSS.Application/Resources) 目录，实际只有两份：

- `Messages.resx` - **中性资源，内容就是英文**（因此不存在、也不需要 `Messages.en.resx`；中性侧出现任何中日韩字符即为缺陷，测试会拦住）
- `Messages.zh-Hans.resx` - 简体中文资源

两份必须**键集合完全相等**（当前各 156 条）。只加一侧会让另一语言拿到键名字符串，界面直接显示 `PolicyMinLength` 这类东西；`NoneadCSS.Tests/Localization/ServerLabelCatalogLocalizationTests.cs` 与 `OptionCatalogLocalizationTests.cs` 会读真实 resx 断言双语齐全。数键不要用 `Get-Content` + `[xml]` 强转（那条路径下 `.data.data.Count` 会返回 0），用 XmlDocument 的 `SelectNodes('//data')` 计数。

### 9.4 使用方式

Application/Infrastructure 层注入的是 `NoneadCSS.Application/Services/ILocalizationService.cs`，**不是** `IStringLocalizer<Messages>`——全仓没有后者的用法（`AddDataAnnotationsLocalization()` 虽在 `Program.cs:158` 注册，但 `ErrorMessageResourceType` 为 0 处，DataAnnotations 文案实际走 9.7 的出口词典）。

```csharp
// ILocalizationService 成员：
//   this[string key]                       按当前请求语言取词条
//   GetString(string key, params object[])  取词条并用 string.Format 填槽
//   CurrentLanguage / SupportedLanguages
private readonly ILocalizationService? _localization;

// 给已有服务加本地化时用「可选尾参」，所有旧构造点（含测试）都无需改动：
public MyService(IDependency dep, ILocalizationService? localization = null)
{
    _localization = localization;
}

// 取不到时必须回落到中文原文，不能把键名吐给用户：
// 词条缺失、或后台任务（Quartz 作业没有 HttpContext，CurrentLanguage 回落 zh-Hans）都走这条
private string Translate(string key, string zhFallback, params object[] args)
{
    if (_localization is null) return args.Length == 0 ? zhFallback : string.Format(zhFallback, args);
    var text = _localization.GetString(key, args);
    return string.Equals(text, key, StringComparison.Ordinal) ? zhFallback : text;
}
```

两条硬规则：① **缓存必须语言中立** —— 把已翻译的文案写进 `IMemoryCache`，后面另一种语言的请求会拿到上一语言的结果（`PasswordPolicyService` 原先正是如此，2026-11-22 改为缓存中立数据、在出口处再套文案）；② 带数量的文案用槽位（`PolicyMinLength` = en `At least {0} characters` / zh 「至少 {0} 个字符」），不要把数字写死进词条。

### 9.5 动态翻译

系统还支持通过 `Translation` 实体存储动态翻译，用于数据库驱动的翻译场景。

### 9.6 OpenAPI / Scalar 文档双语（`/docs` · 2026-11-20）

- **两份文档、一套注释**：`AddOpenApi`（默认文档名 `v1`，中文）与 `AddOpenApi("en", …)`（英文）由同一个 `MapOpenApi()` 暴露为 `/openapi/v1.json` 与 `/openapi/en.json`，两份都是 487 条路径。英文文档**不靠第二套 XML 注释**，而是 `NoneadCSS.Api/OpenApi/OpenApiEnglishLocalizer.cs` 在文档 transformer 里按词典 `NoneadCSS.Api/OpenApi/openapi-en-dictionary.json`（906 条 zh→en，csproj 用 `<Content Update=... CopyToOutputDirectory="PreserveNewest">` 随构建复制）精确匹配替换 operation 的 summary/description、path item 文案、参数/请求体/响应描述与 schema 的 title/description；未命中保留原文，命中数为 0 时打 Warning（词典没复制到输出目录是最常见原因）。
- **标题与 info 段不进词典**：`NoneadCSS.Api/OpenApi/OpenApiDocumentationTexts.cs` 的 `ApplyChineseInfo` / `ApplyEnglishInfo` 直接写两套 `OpenApiInfo`。`Contact.Url` 原为内部占位站点（`ncss.local`），开源清理时已移除，两份文档统一为 `Contact.Name = "NoneadCSS"`。
- **语言切换在中间件里做**：`NoneadCSS.Api/Middleware/ScalarCustomizationMiddleware.cs` 只缓冲 `/docs*`，解析顺序 `?lang=` → Cookie `ncss_docs_lang`（`Path=/docs`、HttpOnly、SameSite=Lax、30 天，仅显式带 `?lang=` 时写入）→ 默认 `zh-Hans`；`NormalizeLanguage` 接受 `en`/`en-US`/`zh-cn`/`zh-hant` 等写法，无法识别的值回落中文。英文时把 Scalar 生成的 HTML 里字面量 `/openapi/v1.json` 改写为 `/openapi/en.json` 并替换 `<title>`；两种语言都在 `<body>` 后注入服务端渲染的开关（`#ncss-lang-switch`，当前语言带 `aria-current="true"`）。
- **Scalar 自身没有 locale 能力**：`Scalar.AspNetCore` 2.0.7 的配置项里没有任何语言选项，中文界面靠注入的 DOM 翻译层——`NoneadCSS.Api/Middleware/ScalarChromeLocale.cs`（132 条英文→简体中文，`ToJavaScriptObjectLiteral()` 里把 `</` 转义成 `<\/` 防 `</script>` 截断）+ TreeWalker（跳过 `script/style/textarea/pre/code/.cm-*/.property-name/#ncss-lang-switch`，翻译 `title`/`aria-label`/`placeholder`）+ MutationObserver（rAF 合帧）。Scalar 的 “Required” 是 CSS 生成的，用 `.required:after{content:"必填"!important}` 覆盖。
- **CSP 不挡注入**：`SecurityHeadersMiddleware.BuildCspPolicy` 对 `/docs|/openapi|/scalar` 走 `script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'` 且不做 nonce 替换（`RequiresHtmlNonceInjection` 对这些路径返回 false），因此注入的内联脚本/样式在生产环境同样合法。
- **回归**：`NoneadCSS.Tests/OpenApi/OpenApiEnglishLocalizerTests.cs`（11 例，含「词典必须随输出复制」「词典值不得含 CJK」）、`NoneadCSS.Tests/Infrastructure/ScalarDocsLocalizationTests.cs`（24 例展开为 40 例：URL/title 改写、开关、Cookie 回读、未知 `lang` 回落中文、非 `/docs` 路径不缓冲、`</script>` 注入边界）。
- **实测口径**：`/openapi/en.json` 936264 B、CJK 命中 0；`/openapi/v1.json` 902679 B、CJK 命中 11197；`/docs` 返回 **302 → `/docs/`**（默认中文 8272 B，含语言开关与中文翻译层），带 `?lang=en` 时先写 Cookie `ncss_docs_lang=en` 再 302，跟随后 `/docs/` 为 3166 B（`openApiUrl` 指向 `/openapi/en.json`、无中文翻译层）；`?lang=de` 回落中文。**探测必须跟随重定向**：只看首跳会拿到空 body，误判成「切换失效」（`fetch` 也不会把 302 的 `Set-Cookie` 带进下一跳）。

### 9.7 后端 API 消息双语（出口词典 · 2026-11-21）

- **口径**：不动 200 个源文件、不按 key 重写 resx，而是在**响应出口**做 zh→en 精确匹配替换（用户裁定，见 CHANGELOG 同日条目）。词典 `NoneadCSS.Api/Localization/api-message-en.json`（**735 条**，67646 B，csproj 用 `<Content Update=... CopyToOutputDirectory/PreserveNewest>` 随构建与发布复制），由 `NoneadCSS.Api/Localization/ApiMessageLocalizer.cs` 加载。
- **带槽位的键**：`«N»` 归一化后写进词典键，运行时用同一个正则把「引号段 `"[^"]*"|'[^']*'`」和「数字段 `\d+(?:[.,]\d+)*`」替换成私有区哨符 `\uE000` 再查表，命中后按捕获顺序回填英文里的 `{N}`。**引号分支必须排在数字分支之前**（单次从左到右扫描），否则「模块 'x' 未启用」这类串会被切成两个槽位；提取端与运行端因此必须完全一致，`ApiMessageLocalizerTests` 里用「对词典全部键逐条反解析」锁住这条不变式。任一所引 `{N}` 越界即放弃翻译（宁可留中文，也不输出半翻译文本）。词典里已剔除 7 条含 `{`/`}` 的字面量（大括号/引号配对在字面量与运行时不一致，永远匹配不上）。
- **语言判定**：`ApiRequestCulture.Of(HttpContext)` 优先读 `IRequestCultureFeature`，回落 `CurrentUICulture`。原因是 `GlobalExceptionMiddleware` 注册在 `UseRequestLocalization`（`Program.cs:1173`）**之前**（`:1166`），异常写回时线程文化已被 localization 中间件还原；只对 `en*` 生效，`zh-Hans` 原样返回，词典加载失败时整体自毁为不翻译（不抛进管道）。
- **五个出口点**：① `Program.cs` 的 `InvalidModelStateResponseFactory`——`message` 与 `Data.errors` 都要在构造**前**本地化，因为匿名类型的属性只读，出口改写器动不了它；② `ApiResponseWrapperFilter`——`IsAlreadyWrapped` 早退分支（`AuthService` 那类自带 `Success/Message` 的业务包，状态码 ≥400 时才走）与非 2xx 反射取到的 `Message`；③ `GlobalExceptionMiddleware` 写回前的 `Message = message`；④ 限流 `OnRejected`（裸 429，无 ApiResponse 包）；⑤ `CsrfProtectionMiddleware` 三处 `message`。另有 `LocalizeMessageProperties(graph, context)`：深度 ≤4、集合 ≤100、引用去重，只改**可写的** `Message` / `ErrorMessage` / `Detail` 字符串属性，仅在错误分支调用；对只读匿名包再用 `TryFlattenLocalizedMessage` 把 `data.message` 提升为英文（否则 401 的 `data.message` 会留中文）。字段名白名单 + 只在错误路径调用，保证客户自己写的正文不会被当成系统文案替换。
- **再生成工具**：`scripts/i18n/api-message-dictionary.mjs`，三个子命令——`extract`（扫源码生成 `msg-rows.json` / `msg-batch-NN.json`）、`coverage`（源码 client-visible 字面量与词典对账）、`merge`（合并 `msg-en-NN.json` → 词典）。`merge` 会读 work 目录里**全部** `msg-rows*.json`、按 id 去重、检查重复键冲突，并**把上一版词典里本轮 rows 覆盖不到的键原样保留（只增不减）**；要真正清理孤儿键必须显式加 `--prune`。另保留「新词典条目不足现存 90% 时拒绝写入」的守卫（除非 `--force`）。校验判据：CJK/全角/角括号必须为 0、槽位序列（索引与顺序）两侧一致、剥掉 `{N}` 后英文里不得残留 `{`/`}`。
- **出口点族（extras）与两条必须记住的坑**：`NoneadCSS.Api/Middleware/GlobalExceptionMiddleware.cs:265-293` 那批「通用异常兜底」文案（「请求参数无效，请检查输入。」「没有权限执行此操作。」「操作冲突，数据可能已被修改或存在约束冲突，请重试。」…）以及 `CalendarFeedController` 的订阅令牌提示，是**七个模式族都看不见**的写法（直接 `return $"…"` / 静态类里的 switch），只能由手工维护的 extras 行文件补进词典（`msg-rows-extra*.json` / `msg-rows-dto-A.json`，id 为 `x001`–`x012` / `dtoA001`，译文批次文件必须命名成 `msg-en-NN.json`——`merge` 的批次通配是 `^msg-en-\d+\.json$`，`msg-en-A.json` 不会被读）。坑①：早期 `merge` 按 rows 重建词典，于是这 13 条在每次 merge 时被**静默删除**，条目数只从 722 变 723、90% 守卫拦不住、构建和 `coverage` 都不报错，只有两条本地化用例变红才暴露（现已由「只增不减」修掉）。坑②：重新 `extract` 会**重排行号 id**，旧的 `msg-en-NN.json` 按 id 对齐，直接 merge 会把英文错配到别的键上且不报错——必须按 **key** 重新对齐。`coverage` 里的 `notSeenInSource:12` 就是这批 extras 条目，属预期，不是缺失。
- **回归**：`NoneadCSS.Tests/Localization/ApiMessageLocalizerTests.cs` 18 例（词典必须随输出复制、词典值无 CJK、**全量键可被反解析**、未引号包裹的 `{...}` 槽位、引号含数字时单槽位、多槽位顺序回填、越界槽位放弃、zh 不触发、匿名只读包不被动、`IRequestCultureFeature` 与线程文化两条判定路径）。HTTP 侧证据：校验 400（zh cjk=29 → en cjk=0，`message` 与 `data.errors` 同时生效）、验证码 401（en 下 `message` 与 `data.message` 均 cjk=0）、CSRF 403（`"CSRF validation failed."`），`?lang=en` 与 `Accept-Language: en-GB` 两条链路都通；匿名 401/404 的空响应属认证短路，不是缺陷。
- **前缀键规则（2026-11-22 补）**：源码里有一类「固定中文前缀 + 动态详情」的拼接错误文案——`NoneadCSS.Infrastructure/Services/Line/LineClient.cs` 的 `"LINE 请求异常：" + ex.Message`（:535）、`NoneadCSS.Application/Services/TelegramService.cs` 的 `"getMe 异常：" + ex.Message`（:636）、`NoneadCSS.Application/Services/IMessageService.cs` 的 `"附件全部发送失败，消息未发出：" + string.Join(", ", mediaErrors)`（:354）。这类消息整句命不中词典，动态部分也不是数字或引号段，归一化同样命不中，结果就是英文界面夹一段中文。词典里这类键的**唯一识别特征是以全角冒号 `：` 结尾且英文值不含 `{N}` 槽位**（当前 735 条里恰好 3 条）。运行期在精确匹配与归一化匹配都失败后走 `LocalizePrefix`：按键长降序扫描这类键，只在**消息开头**命中才替换，英文冒号后补一个空格（避免 `LINE request exception:System…` 粘连），动态详情原样接上不做任何改写。中途出现这些字（例如客户数据里包装过的句子）一律不翻。判据见 `NoneadCSS.Tests/Localization/ApiMessagePrefixKeyTests.cs`（6 例：前缀键形状约束、LINE/getMe 拼接翻译、中途命中不翻、整句等于前缀键走精确匹配、非英文文化不翻）。⚠️ 新增这类文案时要写成**非插值**的 `前缀 + 动态值`，插值串产生的键含哨符、命不中前缀规则。
- **仍然不是双语的层**（用户未选，留着）：种子数据与 `Modules.Title`、写入数据库的聊天系统消息与工单 HTML（`ChatService.cs:912/1177-1217`，落库即定语言，出口层无法改）、**逐条记录的**服务端 label 映射（`Application/Mappings/MappingExtensions.cs:418-455` 日历 `EventTypeLabel`/`StatusLabel`、`:537+` 知识库 `StatusLabel`、`:598-624` 会话 `StatusLabel`，前端在 `frontend/src/pages/calendar/chips.tsx:517-531` 用 `preferServerLabel = !getBoundLocale().startsWith('en')` 自行覆盖，因此改这些映射必须保持中文逐字不变）、`NotificationService.cs:1215-1243`、`RoleService.cs:333-560`、`Templates/TemplateVariableRegistry.cs`、导出表头 `Application/Services/CustomReportExportHelper.cs`。**已经双语的**：密码策略要求与校验错误、SLA 违规类型/状态标签与 `SlaConst.FormatHours`、日历/知识库/会话/发送日志的**选项目录**（`eventTypeOptions`/`statusOptions` 等）、`CustomFieldService` 的渠道标签、导入/导出实体目录（`Application/Services/ImportExportService.cs` 的 8 类 × 名称/描述，键名 `ImportEntity_<type>` / `ImportEntityDesc_<type>`，英文与前端 `data.importExport.entity.*` 逐字一致）、Outlook 日历订阅指引（`Api/Controllers/CalendarController.cs` 的 `instructions`，键 `CalendarSubscriptionInstructions`，控制器同样以可选尾参注入 `ILocalizationService` 并回落中文）——这些走 `ILocalizationService` + resx（见 9.4）。⚠️ 出口词典**不会翻译数组元素的任意字段**，因此这类「目录型」响应必须在服务层本地化，不能指望出口层。
- **全量复核口径（2026-11-22 当前构建）**：只判**消息型字段**（`message`/`errorMessage`/`detail`/`requirements`/`options`/`instructions`/`name`/`label`/`description` 等）复扫 OpenAPI 里全部 240 条 GET（跳过 sync/export/download 一类会改状态的路径），结果 **39 条端点仍有中文**；逐条核对后全部属于数据面（模块标题、角色名与权限标签、标签名、邮箱名、模板名、SLA 策略名与描述、会话主题、工作流/宏/规则名等种子或用户数据），代码侧文案已清零。HTTP 实测（zh → en 的中文段数）：`/api/ImportExport/entities` 17→**0**、`/api/CustomFields/channels` 10→**0**、`/api/SendLogs/status-options` 10→**0**、`/api/Auth/password-policy` 8→**0**、`/api/Calendar/overview` 8→**0**、`/api/Chat/overview` 5→**0**、`/api/Knowledge/overview` 4→**0**、`/api/CustomReports` 1→**0**；`/api/Sla/overview` 30→15、`/api/Tags` 21→20，残留部分逐字段确认为种子/用户数据。`SystemOptions` 无 culture 列，故 `app.name` / `mail.from_name` 只能单语。改控制器 XML 注释时须同步重跑 9.6 的 OpenAPI 词典（同样是精确匹配）。

---

### 9.8 开源发布预检（`scripts/oss-preflight.mjs` · 2026-11-22）

发布前一条命令跑完三条只读检查，任何一条失败即退出码 1，可直接挂 CI 或 pre-push 钩子：

```bash
node scripts/oss-preflight.mjs           # 三条全跑
node scripts/oss-preflight.mjs --quick   # 跳过较慢的密钥扫描，用于日常提交
```

| 检查 | 命令 | 判据 |
| --- | --- | --- |
| 密钥/路径扫描 | `node scripts/oss-secret-scan.mjs` | 本机用户目录、仓库绝对路径、盘符源码、内部主机名、PEM 块这类**真泄漏规则必须 0 命中**；剩余命中是已判定的噪声（RFC 5737 示例 IP、四段版本号、JSX 的 `password={x}`、`package-lock.json` 第三方邮箱、测试夹具假口令） |
| 文档链接 | `node scripts/oss-doc-links.mjs check` | `file:///` 链接 0、断链 0；剩余 9 条是文档里的 `[文本](url)` 示例占位 |
| 文案词典覆盖 | `node scripts/i18n/api-message-dictionary.mjs coverage` | `missingFromDictionary:0`（源码里面向客户端的中文字面量全部有英文条目） |

要点：

- 本脚本只调用上面三个脚本，不重复实现规则，也不写任何文件；改规则请改那三个脚本。
- 词典覆盖检查是**会假绿**的：它只认生成器已知的模式族。`errors.Add("中文")` 这类写法当初完全漏采，`coverage` 照样报 0 —— 已在生成器里补成第 7 族（`ListAdd`）。新增任何"面向客户端的中文写法"（例如新的错误累积 API）时，必须同步给生成器加模式族，否则这条门禁形同虚设。
- 密钥扫描的跳过名单里有 `logs`，所以仓库里一旦真提交了日志文件就会漏检：`deploy/logs/` 里的运行日志曾真实含本机绝对路径，靠人工发现后删除，并把 `logs/`、`uploads/`、`data/`、`bin-docs/` 补进 `.gitignore` 与 `.dockerignore`。发布前顺手确认这些目录不存在于工作树。
- `docs/` 只放技术文档。内部评审/验收报告、AI 会话产物、含截图的 PDF/MHTML/DOCX（图片内文字无法用正则脱敏）应在发布前移出仓库（本仓库移到仓库外复核目录，不删除），移出后**必须**重跑链接检查。

---

## 10. 病毒扫描与附件安全

### 10.1 病毒扫描服务

[VirusScanService](../NoneadCSS.Infrastructure/Services/VirusScanService.cs) 实现了 `IVirusScanService` 接口，提供以下安全检查：

| 检查项 | 说明 |
|--------|------|
| 恶意特征码检测 | 检测文件头部 64KB + 尾部 4KB 是否包含已知恶意特征（如 EICAR 测试病毒），覆盖大多数嵌入场景 |
| 可执行文件拦截 | 阻止 40+ 种可执行文件扩展名（.exe/.dll/.bat/.ps1/.sh 等） |
| 可疑 MIME 检测 | 检测可疑的 MIME 类型（application/x-dosexec 等） |
| 扩展名欺骗检测 | 校验文件扩展名与 MIME 类型是否匹配 |

提供两种调用方式：
- `ScanAsync(Stream, fileName, mimeType)` - 从流扫描
- `ScanFileAsync(filePath, fileName, mimeType)` - 从文件路径扫描

### 10.2 附件上传安全

[AttachmentService](../NoneadCSS.Infrastructure/Services/AttachmentService.cs) 的上传流程：

1. **大小校验**：不超过 `Attachment:MaxSize`（默认 20MB）
2. **扩展名白名单**：校验 `Attachment:AllowedExtensions`
3. **MIME 类型白名单**：校验 `Attachment:AllowedMimeTypes`
4. **文件名清理**：移除非法字符，防止路径遍历
5. **病毒扫描**：调用 `IVirusScanService.ScanAsync`
6. **保存文件**：保存到 `Attachment:UploadPath` 目录

支持单文件上传 `UploadAsync` 和多文件上传 `UploadMultipleAsync`。多文件上传时逐个扫描，全部失败时抛出聚合异常。

### 10.3 邮件附件安全

[EmailProcessingService.SaveAttachmentsAsync](../NoneadCSS.Infrastructure/Services/EmailProcessingService.cs#L236) 在保存邮件附件时：

1. 保存文件到磁盘
2. 调用 `_virusScanService.ScanFileAsync` 扫描
3. 扫描不通过则删除磁盘文件并 `continue` 跳过（不写入数据库）
4. 失败被 try-catch 隔离，不影响邮件收取主流程

### 10.4 配置项

```json
{
  "Attachment": {
    "MaxSize": 20971520,
    "AllowedExtensions": [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt", ".zip", ".rar"],
    "AllowedMimeTypes": ["image/", "application/pdf", "application/vnd.", "text/", "application/zip", "application/x-rar-compressed", "application/octet-stream"],
    "UploadPath": "wwwroot/attachments"
  }
}
```

---

## 11. 草稿功能

### 11.1 草稿存储

会话回复草稿存储在 `Conversation` 实体的 `DraftBody`、`DraftCc`、`DraftBcc`、`DraftAttachmentIds` 字段中，按会话粒度存储。

### 11.2 API 接口

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/conversations/{id}/draft` | 获取会话草稿 |
| POST | `/api/conversations/{id}/draft` | 保存草稿 |
| DELETE | `/api/conversations/{id}/draft` | 删除草稿 |
| GET | `/api/conversations/drafts` | 获取当前用户所有草稿 |

### 11.3 前端交互

- 进入会话详情页时自动调用 `GET /api/conversations/{id}/draft` 并回填
- 点击"保存草稿"按钮调用 `POST /api/conversations/{id}/draft`
- 回复发送成功后自动调用 `DELETE /api/conversations/{id}/draft`

---

## 12. 软删除与回收站

### 12.1 软删除机制

系统通过 `ISoftDelete` 接口实现软删除：

```csharp
public interface ISoftDelete
{
    DateTime? DeletedAt { get; set; }
    int? DeletedBy { get; set; }
}
```

14 个实体实现此接口：User、Mailbox、Customer、Tag、MailTemplate、CannedResponse、EmailSignature、Role、Team、SlaPolicy、Macro、Workflow、NotificationTemplate、NotificationRule。

> 自定义报表模块在 2026-08-11 新增软删除与垃圾箱能力，`CustomReport` 与 `CustomReportSchedule` 同样实现 `ISoftDelete` 接口，并复用本节约定的全局查询过滤器与拦截器机制，详见 [§37 自定义报表软删除与垃圾箱功能](#37-2026-08-11-自定义报表软删除与垃圾箱功能)。

### 12.2 全局查询过滤器

[NoneadCSSDbContext](../NoneadCSS.Infrastructure/Persistence/NoneadCSSDbContext.cs) 中配置全局查询过滤器，自动过滤 `DeletedAt != null` 的实体。

`SaveChanges` 拦截器将 `EntityState.Deleted` 转换为设置 `DeletedAt` 和 `DeletedBy`。

### 12.3 回收站操作

| 操作 | 实现方式 |
|------|---------|
| 查询已删除数据 | 使用 `IgnoreQueryFilters()` 绕过全局过滤器 |
| 恢复数据 | 使用 `ExecuteUpdateAsync` 设置 `DeletedAt = null` |
| 永久删除 | 使用 `ExecuteDeleteAsync` 绕过变更追踪器 |
| 已删除实体查询 | 使用 `AsNoTracking()` 避免缓存冲突 |

### 12.4 唯一索引与软删除

对于有唯一索引约束的实体，软删除后可能无法创建同名的新记录。解决方案是为唯一索引添加软删除过滤条件：

```csharp
modelBuilder.Entity<MailTemplate>()
    .HasIndex(mt => mt.Slug)
    .IsUnique()
    .HasFilter("\"DeletedAt\" IS NULL");
```

**已添加软删除过滤的唯一索引**：
| 实体 | 索引字段 | 说明 |
|------|---------|------|
| `Tag` | `(MailboxId, Name)` | 标签名称在邮箱内唯一 |
| `MailTemplate` | `Slug` | 邮件模板标识唯一 |
| `CustomReport` | `(Name, OwnerId)` | 同一所有者下报表名称唯一（2026-08-11 新增，配合软删除避免重名冲突） |

### 12.5 Repository 模式约定

- 软删除查询使用 `IgnoreQueryFilters()`
- 恢复和硬删除使用 `ExecuteUpdateAsync`/`ExecuteDeleteAsync`
- 删除实体查询使用 `AsNoTracking()`
- 仓储类使用私有 `ApplyIncludes()` 方法统一导航属性包含

---

## 13. OutputCache 缓存

### 13.1 启用缓存的接口

只读数据 API 启用了 .NET OutputCache 响应缓存，减少数据库查询：

| 模块 | 控制器 | 缓存策略 |
|------|--------|---------|
| 角色 | `RolesController` | OutputCache |
| 标签 | `TagsController` | OutputCache |
| 自定义字段 | `CustomFieldsController` | OutputCache |
| 系统选项 | `SystemOptionsController` | OutputCache |
| 审计日志 | `AuditLogsController` | OutputCache（1 分钟） |
| 团队 | `TeamsController` | OutputCache（5 分钟） |
| Webhook | `WebhooksController` | OutputCache（5 分钟） |
| 模块 | `ModulesController` | OutputCache（5 分钟） |
| SLA | `SlaController` | OutputCache（5 分钟） |
| 快捷回复 | `CannedResponsesController` | OutputCache（5 分钟） |

### 13.2 缓存策略配置

在 [Program.cs](../NoneadCSS.Api/Program.cs) 中配置：

```csharp
builder.Services.AddOutputCache(options =>
{
    options.AddBasePolicy(builder => builder.Cache());
    options.AddPolicy("CustomPolicy", builder =>
        builder.Expire(TimeSpan.FromMinutes(5)));
});
```

### 13.3 缓存失效

写入操作（POST/PUT/DELETE）完成后，缓存会自动失效，下次 GET 请求时重新查询数据库。

---

## 14. 工作流自动化

### 14.1 触发器类型

| 触发器 | 说明 |
|--------|------|
| `new_conversation` | 新会话创建时触发 |
| `new_reply` | 收到客户回复时触发（仅客户回复，不包括客服回复） |
| `status_changed` | 会话状态变更时触发 |
| `satisfaction_submitted` | 客户提交满意度评价时触发 |
| `agent_replied` | 客服回复时触发 |
| `assignee_changed` | 会话分配客服变更时触发 |
| `conversation_closed` | 会话关闭时触发 |
| `conversation_reopened` | 会话重新打开时触发 |
| `tag_added` | 添加标签时触发 |
| `tag_removed` | 移除标签时触发 |
| `note_added` | 添加内部备注时触发 |
| `priority_changed` | 会话优先级变更时触发 |

### 14.2 动作类型

| 动作 | 说明 |
|------|------|
| `set_status` | 设置会话状态 |
| `assign_user` | 分配客服 |
| `set_priority` | 设置优先级 |
| `send_reply` | 自动回复 |
| `send_email` | 发送自定义邮件模板 |
| `add_tag` / `remove_tag` | 添加/移除标签 |
| `add_note` | 添加内部备注 |
| `trigger_webhook` | 触发 Webhook |
| `forward` | 转发会话 |
| `conversation_process` | 会话流程处理 |
| `execute_macro` | 执行宏命令（支持批量会话操作，复用宏的所有动作组合） |

#### execute_macro 动作说明

- **功能**：在工作流中调用已定义的宏，执行宏包含的所有动作组合
- **优势**：一次定义宏，可在多个工作流中复用；支持复杂的多步操作编排
- **配置**：选择宏 ID 即可，执行逻辑由 `MacroService.ExecuteForWorkflowAsync` 处理
- **事务**：宏执行与工作流执行日志在同一事务内，确保原子性
- **前端**：工作流编辑界面提供宏下拉选择框，列出所有启用的宏

### 14.3 默认工作流

系统内置 6 个工作流，默认全部启用：

| 工作流名称 | 触发器 | 动作 |
|-----------|--------|------|
| 客户回复确认邮件 | `new_reply` | send_email: customer-reply-default → 客户 |
| 紧急工单邮件通知 | `new_conversation` | assign_user: 1 + send_email: sla-warning → 客服+admin |
| 新会话客户回访邮件 | `new_conversation` | send_email: customer-followup → 客户 |
| 会话升级通知邮件 | `new_conversation` | assign_user: 1 + send_email: conversation-escalation → 客服+admin |
| 满意度评价感谢邮件 | `satisfaction_submitted` | send_email: satisfaction-thanks → 客户 |
| 新会话自动分配 | `new_conversation` | assign_user: 轮询分配 |

### 14.4 邮件模板变量

工作流 `send_email` 动作的自定义模板支持以下变量：

| 变量 | 说明 |
|------|------|
| `{{subject}}` | 会话主题 |
| `{{conversation_id}}` | 会话 ID |
| `{{conversation_url}}` | 会话详情页 URL |
| `{{customer_name}}` | 客户姓名 |
| `{{customer_email}}` | 客户邮箱 |
| `{{user_name}}` | 分配客服姓名 |
| `{{user_email}}` | 分配客服邮箱 |
| `{{mailbox_name}}` | 邮箱名称 |
| `{{mailbox_email}}` | 邮箱地址 |
| `{{message_body}}` | 首条消息正文 |
| `{{created_at}}` | 创建时间 |
| `{{company_name}}` | 公司名称 |
| `{{satisfaction_rating}}` | 满意度评价文字（仅 satisfaction_submitted 触发器） |
| `{{satisfaction_rating_value}}` | 满意度评分数值（仅 satisfaction_submitted 触发器） |
| `{{satisfaction_comment}}` | 满意度评价评论（仅 satisfaction_submitted 触发器） |

---

## 15. 前端数据获取（React Query）

### 15.1 为什么使用 React Query

前端从 `useEffect` + `useState` 手动数据获取迁移到 `@tanstack/react-query`，带来以下优势：

| 特性 | 说明 |
|------|------|
| **自动缓存** | 相同 queryKey 的数据自动缓存，避免重复请求 |
| **后台刷新** | 数据在后台静默刷新，用户看到的始终是最新数据 |
| **请求去重** | 同一时间多个相同请求合并为一个 |
| **竞态条件** | 自动处理请求顺序，避免旧响应覆盖新数据 |
| **乐观更新** | 突变操作后立即更新缓存，提升体验 |
| **查询失效** | SignalR 事件触发 `invalidateQueries` 自动刷新 |

### 15.2 目录结构

```
frontend/src/hooks/
├── useAuth.ts              # 认证相关 Hooks
├── useSignalR.ts           # SignalR 实时通信
├── usePreferences.ts       # 用户偏好设置
├── useTheme.ts             # 主题切换
├── useConversations.ts     # 会话相关 React Query Hooks ⭐
├── useMailboxes.ts         # 邮箱相关 Hooks
├── useUsers.ts             # 用户相关 Hooks
└── ...
```

### 15.3 自定义 Hooks 模式

每个模块在 `hooks/` 目录下有对应的 React Query Hooks 文件，遵循以下模式：

```typescript
// 查询键定义
export const queryKeys = {
  conversations: {
    all: ['conversations'] as const,
    lists: (params: Record<string, unknown>) =>
      [...queryKeys.conversations.all, 'list', params] as const,
    detail: (id: number) =>
      [...queryKeys.conversations.all, 'detail', id] as const,
  },
};

// 查询 Hook
export function useConversations(params?: { mailboxId?: number; page?: number }) {
  return useQuery({
    queryKey: queryKeys.conversations.lists(params ?? {}),
    queryFn: () => conversationApi.list(params),
  });
}

// 突变 Hook
export function useBulkAssign() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ ids, userId }: { ids: number[]; userId: number }) =>
      conversationApi.bulkAssign(ids, userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
    },
  });
}
```

### 15.4 查询键设计规范

- 使用分层结构，便于按层级失效
- 列表查询包含参数（页码、过滤条件等）
- 详情查询使用 ID
- 常量集中在 `queryKeys` 对象中，避免硬编码

```typescript
queryKeys.conversations.all           // ['conversations']
queryKeys.conversations.lists(params) // ['conversations', 'list', {...}]
queryKeys.conversations.detail(id)    // ['conversations', 'detail', 123]
```

### 15.5 SignalR 与查询失效

实时事件通过 SignalR 推送后，使用 `invalidateQueries` 触发缓存失效和重新获取：

```typescript
useSignalREvent(SignalREvents.RefreshConversations, (data) => {
  queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
});
```

使用 `all` 层级键可以一次性失效该模块下所有缓存（列表+详情）。

### 15.6 现有迁移模块

| 模块 | Hook 文件 | 状态 |
|------|-----------|------|
| 会话 | `useConversations.ts` | ✅ 已迁移 |
| 会话详情 | `useConversation.ts` | ✅ 已迁移 |
| 邮箱 | `useMailboxes.ts` | ✅ 已迁移 |
| 用户 | `useUsers.ts` | ✅ 已迁移 |
| 标签 | `useTags.ts` | ✅ 已迁移 |
| 收藏 | `useStars.ts` | ✅ 已迁移 |

---

## 16. 数据库查询性能优化

### 16.1 常见性能问题

#### N+1 查询问题

**反模式**：在循环中逐个查询关联数据。

```csharp
// ❌ 不好：循环内查询，N 个会话产生 N 次标签查询
foreach (var conv in conversations)
{
    var tags = await _tagRepository.GetByConversationIdAsync(conv.Id);
}
```

**正确做法**：批量获取后在内存中关联。

```csharp
// ✅ 好：一次批量查询 + 内存分组
var allTags = await _tagRepository.GetByConversationIdsAsync(convIds);
var tagsByConvId = allTags.ToLookup(t => t.ConversationId);
```

#### 内存聚合问题

**反模式**：将全量数据加载到内存后再做统计。

```csharp
// ❌ 不好：加载所有记录到内存再统计
var logs = await query.ToListAsync();
var stats = new SendLogStats
{
    TotalCount = logs.Count,
    SuccessCount = logs.Count(s => s.Status == "success"),
    FailedCount = logs.Count(s => s.Status == "failed"),
};
```

**正确做法**：使用数据库级聚合查询。

```csharp
// ✅ 好：直接在数据库层面统计，只返回统计结果
var total = await query.CountAsync();
var success = await query.CountAsync(s => s.Status == "success");
var failed = await query.CountAsync(s => s.Status == "failed");
```

### 16.2 GroupBy 聚合优化

对于按日期/按分组的统计，优先使用 EF Core 的 `GroupBy` + `Select`：

```csharp
var stats = await query
    .GroupBy(x => new { x.CreatedAt.Date })
    .Select(g => new DailyStats
    {
        Date = g.Key.Date,
        TotalCount = g.Count(),
        SuccessCount = g.Count(x => x.Status == "success"),
    })
    .OrderBy(s => s.Date)
    .ToListAsync();
```

**注意事项**：
- EF Core 的 `GroupBy` 支持有限，复杂逻辑可能需要降级到内存处理
- 先验证生成的 SQL 是否符合预期
- 关联表的 GroupBy 可能效率较低，考虑先用 GroupBy 聚合主键外键，再二次查询关联表

### 16.3 已优化的仓储方法

#### SendLogRepository

| 方法 | 优化方式 |
|------|---------|
| `GetStatsAsync` | 内存计数 → 4 个 CountAsync |
| `GetDailyStatsAsync` | 内存 GroupBy → EF Core GroupBy |
| `GetStatsByMailboxAsync` | 三表 JOIN + 内存分组 → 数据库 GroupBy + 字典关联 |

#### SatisfactionRatingRepository

| 方法 | 优化方式 |
|------|---------|
| `GetStatsAsync` | 内存计数 → CountAsync + SumAsync |
| `GetDailyStatsAsync` | 内存 GroupBy → EF Core GroupBy |
| `GetStatsByMailboxAsync` | JOIN + 内存分组 → 数据库 GroupBy + 字典关联 |
| `GetStatsByUserAsync` | JOIN + 内存分组 → 数据库 GroupBy + 字典关联 |

### 16.4 性能优化检查清单

添加新的统计/查询方法时，检查以下几点：

- [ ] 是否加载了不必要的全量数据到内存？
- [ ] 是否可以用 `CountAsync` / `SumAsync` / `AnyAsync` 替代 `ToListAsync` + LINQ？
- [ ] 是否存在循环内的数据库查询（N+1）？
- [ ] GroupBy 能否下推到数据库执行？
- [ ] 是否添加了合适的索引？
- [ ] 大数据量查询是否有分页限制？

---

## 17. 事务管理

### 17.1 何时使用事务

当一个业务操作涉及**多次数据库写入**且需要保证原子性时，使用事务：

- 创建主记录 + 创建关联记录
- 更新多个实体的状态
- 删除操作 + 记录日志

### 17.2 Unit of Work 事务模式

通过 `IUnitOfWork` 的 `BeginTransactionAsync` 开启事务：

```csharp
public async Task DoSomethingAsync(int id)
{
    using var transaction = await _unitOfWork.BeginTransactionAsync();
    try
    {
        // 操作 1
        await _unitOfWork.Entities.UpdateAsync(entity1);
        
        // 操作 2
        await _unitOfWork.OtherEntities.AddAsync(entity2);
        
        // 统一提交
        await _unitOfWork.SaveChangesAsync();
        await transaction.CommitAsync();
    }
    catch
    {
        // 失败回滚
        await transaction.RollbackAsync();
        throw;
    }
}
```

### 17.3 现有事务保护的方法

| 服务 | 方法 | 保护的操作 |
|------|------|-----------|
| `MacroService` | `ExecuteAsync` | 宏执行 + 使用次数记录 + 执行日志写入 |
| `ConversationService` | `CreatePhoneConversationAsync` | 客户创建/更新 + 会话创建 |
| `EmailQueueService` | `ProcessQueueItemAsync` | SendLog 状态更新 + Thread.MessageId 更新 |
| `ConversationService` | `BulkUpdatePriorityAsync` | 批量更新会话优先级 |
| `ConversationService` | `BulkDeleteAsync` | 批量软删除会话，支持部分成功/失败 |
| `RoleService` | `BatchAssignRoleAsync` | 批量分配角色 |
| `SystemOptionService` | `BatchUpdateAsync` | 批量更新系统选项 |
| `CustomFieldService` | `BatchSetEntityValuesAsync` | 批量设置自定义字段值 |
| `ConversationThreadService` | `AddNoteAsync` | 添加备注 + 附件关联更新 |
| `ConversationThreadService` | `AddReplyAsync` | 添加回复 + 附件关联更新 |

### 17.4 事务最佳实践

1. **保持事务短小**：事务内只包含必要的数据库操作，避免耗时操作
2. **明确 try-catch**：所有事务都应有 try-catch + Rollback
3. **使用 using**：确保事务资源正确释放
4. **避免嵌套事务**：EF Core 不支持嵌套事务，需要时使用保存点
5. ** SaveChanges 位置**：事务内所有修改完成后调用一次 SaveChanges

---

## 18. 部署与运维

### 18.1 配置文件

#### 开发环境

`appsettings.Development.json` - 开发环境配置（**已从仓库移除，已被 `.gitignore` 排除**，开发者需在 `NoneadCSS.Api` 目录下本地创建）

> **重要**：开发环境建议在 `appsettings.Development.json` 中配置固定的加密密钥和 JWT 密钥：
> 
> ```json
> {
>   "Encryption": {
>     "Key": "your-dev-encryption-key"
>   },
>   "Jwt": {
>     "SecretKey": "your-dev-jwt-secret-key"
>   }
> }
> ```
> 
> 如果不配置，系统每次启动会自动生成随机密钥，导致：
> - 重启后已加密的邮箱密码等字段无法解密
> - 用户登录状态失效（JWT 签名密钥变化）
> - 开发环境调试出现"用户名或密码错误"等异常

#### 生产环境

`appsettings.json` - 默认配置

### 18.2 环境变量覆盖

配置项可通过环境变量覆盖：

```bash
export ASPNETCORE_ENVIRONMENT=Production
export Database__UseSqlite=false
export ConnectionStrings__DefaultConnection="Server=prod;Database=NoneadCSS;User Id=sa;Password=xxx"
export Jwt__SecretKey="YourProductionSecretKey"
```

### 18.3 日志配置

使用 Serilog 记录日志，开发环境支持控制台和文件输出，生产环境仅保留控制台输出（避免文件锁定问题）：

**开发环境**（appsettings.Development.json）：

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/log-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 30
        }
      }
    ]
  }
}
```

**生产环境**（appsettings.json）：

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore": "Warning",
        "Microsoft.AspNetCore.Hosting": "Warning",
        "Quartz": "Warning",
        "System.Net.Http": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}"
        }
      }
    ]
  }
}
```

> **生产环境说明**：1.8.1 版本移除了 Serilog 的 File sink，仅保留 Console sink。原因：
> - 文件锁定问题：沙箱环境或异常退出后，日志文件可能被锁定，导致新启动的后端无法写入
> - 日志通过 `start-prod.ps1` 启动时的 `-RedirectStandardOutput` 重定向到 `logs/backend-stdout.log`，无需 Serilog 直接写文件
> - 如需持久化日志，建议通过日志收集系统（如 ELK、Loki）接管 stdout 输出

### 18.4 健康检查

系统提供两个健康检查端点，分别用于 liveness 与 readiness 探测：

| 端点 | 用途 | 说明 |
|------|------|------|
| `GET /health` | Liveness（存活探针） | 仅检查应用进程是否正常运行，不依赖外部资源。用于编排器（Kubernetes/Docker）判断是否需要重启容器 |
| `GET /health/ready` | Readiness（就绪探针） | 检查应用及关键依赖（如数据库连接）是否就绪。用于判断是否可以将流量路由到该实例 |

请求示例：

```
GET /health
GET /health/ready
```

返回：

```json
{
  "status": "Healthy",
  "components": {
    "database": {
      "status": "Healthy"
    }
  }
}
```

> **区别说明**：`/health` 用于快速判断进程存活（即使数据库暂时不可用也返回 Healthy），避免被误重启；`/health/ready` 在数据库等依赖不可用时返回 Unhealthy，使负载均衡/编排器暂停路由流量直到依赖恢复。

### 18.5 API 文档

Scalar API 文档挂载到 `/docs` 路径：

```csharp
app.MapScalarApiReference("/docs", options =>
{
    options
        .WithTitle("NoneadCSS API 文档")
        .WithTheme(ScalarTheme.Purple)
        .WithFavicon("/favicon.svg")
        .WithCustomCss("/scalar-custom.css")
        .WithDarkMode(true)
        .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
});
```

### 18.6 静态资源

静态资源存储在 `wwwroot/` 目录：

- `attachments/` - 上传的附件
- `favicon.svg` - 网站图标
- `logo.svg` - 网站 Logo
- `scalar-custom.css` - Scalar 文档自定义样式

### 18.7 部署方式

#### 本地开发

```bash
cd NoneadCSS.Api
dotnet run
```

#### Docker 部署

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["NoneadCSS.Api/NoneadCSS.Api.csproj", "NoneadCSS.Api/"]
COPY ["NoneadCSS.Application/NoneadCSS.Application.csproj", "NoneadCSS.Application/"]
COPY ["NoneadCSS.Domain/NoneadCSS.Domain.csproj", "NoneadCSS.Domain/"]
COPY ["NoneadCSS.Infrastructure/NoneadCSS.Infrastructure.csproj", "NoneadCSS.Infrastructure/"]
RUN dotnet restore "NoneadCSS.Api/NoneadCSS.Api.csproj"
COPY . .
WORKDIR "/src/NoneadCSS.Api"
RUN dotnet build "NoneadCSS.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "NoneadCSS.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "NoneadCSS.Api.dll"]
```

容器运行时需通过环境变量配置管理员密码：

```bash
docker run -d \
  -p 8080:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e ADMIN_PASSWORD="your-strong-password" \
  -e Jwt__SecretKey="YourProductionSecretKey" \
  noneadcss:latest
```

> **注意**：`ADMIN_PASSWORD` 为必需环境变量。如未设置，容器首次启动时会自动生成随机密码并输出到日志，需从 `docker logs` 中获取。生产环境务必显式设置强密码。

#### IIS 部署

1. 发布项目：`dotnet publish -c Release`
2. 将 `bin/Release/net10.0/publish` 目录内容复制到 IIS 站点
3. 配置应用池使用 .NET 10

### 18.8 端口分配（开发 vs 生产 · 基线 2026-08-14 固化）

> ⚠️ **硬约束（Production 部署 SOP 基线）**：生产目录 `C:\noneadcss\Production` 下 **后端=5009、前端 vite preview=5172、Nginx=5173** 三个端口号**任何情况下不得变更**。独立验证阶段必须显式确认 `:5008` 未被监听，否则意味着启动方式错误。

为避免本地多环境互相干扰，开发 / 生产 / 反向代理端口严格分离：

| 服务 | 开发环境 | 生产环境 | 说明 |
|------|----------|----------|------|
| 前端 Vite dev server | **5171** | - | 开发热更新；API 由 Vite 代理转发到后端 5008，无 Nginx |
| 前端 SPA 托管（vite preview） | - | **5172** | v2.2.3 起重新引入 vite preview（独立进程）作为 Nginx upstream，保证 hash 路由与历史 fallback 行为完全一致 |
| 后端 Kestrel | **5008** | **5009** | 健康检查端点 `/health`、`/health/ready`；启动命令**必须**追加 `--urls http://127.0.0.1:5009`，并在 `appsettings.Production.json` 首行声明 `"Urls": "http://127.0.0.1:5009"`（双保险） |
| Nginx 反向代理入口 | - | **5173** | `/` → upstream `127.0.0.1:5172`（vite preview）；`/api` + `/hubs` → upstream `127.0.0.1:5009`（后端）；支持 WebSocket Upgrade |
| CORS/CSRF 中间件白名单 | 5171 / 5173 | 5172 / 5173 + 实际生产域名 | 在 `Program.cs` 与 `CsrfProtectionMiddleware.cs` 中同步维护 |

> 端口号必须一致配置在四处：`frontend/vite.config.ts`（server / preview）、`Production/nginx.conf`（listen + upstream）、`NoneadCSS.Api/Program.cs`（CORS） + `CsrfProtectionMiddleware.cs`（DevPorts）。若仅修改 nginx.conf 未修改 `--urls`，会出现后端仍监听 5008、Nginx 反代 5009 返回 502 的诡异问题。

### 18.9 一键启动脚本（推荐）

项目提供两个 **PowerShell 一键重启脚本**，统一负责："按端口精准清理旧进程 → 注入环境变量 → 启动服务 → 健康检查等服务就绪"，全程不杀同名其他项目进程。

#### 18.9.1 开发环境（不使用 Nginx）

- **脚本路径**：`deploy/restart-dev.ps1`
- **命令**：
  ```powershell
  powershell -ExecutionPolicy Bypass -File deploy/restart-dev.ps1
  ```
- **行为**：
  1. 仅清理占用 `5008`（后端）与 `5171`（前端）端口的 Pid
  2. 从当前用户级环境变量读取 `JWT__Key`、`ENCRYPTION__Key`、`AUDIT__HmacKey`、`WECHAT__*` 并注入后端启动进程
  3. 后端用 `dotnet run --no-build` 监听 `http://127.0.0.1:5008`
  4. 前端用 `npx vite --port 5171 --host`
  5. 最多等 40 秒，两端健康检查通过后输出结果
- **访问地址**：
  - 前端 👉 http://localhost:5171（Vite 自动代理 `/api` → 5008）
  - 后端直连 👉 http://localhost:5008

#### 18.9.2 生产环境（独立部署目录 + Nginx 反向代理 + vite preview）

> **v2.2.3（2026-08-14）更新**：生产部署目录已固化为 `C:\noneadcss\Production`，采用 **后端 + vite preview(:5172) + Nginx 反代(:5173)** 三层。发布 SOP 已固化为 **7 步强制流程**（见 §18.11），任何一步不通过禁止宣布"发布完成"。详见 [第 36 章](#36-2026-08-10-生产环境独立部署架构--启动脚本--nginx-优化) 与 §18.11。

- **部署目录**：`C:\noneadcss\Production`（位于源码仓库下，通过 `.gitignore` 排除版本控制），目录构成：
  - `backend/`  ← `dotnet publish NoneadCSS.Api -c Release` 输出（NoneadCSS.Api.exe + DLL + appsettings.json）
  - `frontend/dist/`  ← `npm run build` 输出，由 `Production/start-prod.ps1` 用 `npx vite preview --port 5172` 启动
  - `nginx/`  ← Windows 版 Nginx（由 `nginx.conf` 配置 upstream :5172 与 :5009）
  - `data/noneadcss.db`  ← SQLite 运行数据库（通过 `ConnectionStrings__SqliteConnection` **绝对路径**注入，避免 ContentRoot/CWD 路径错配）
  - `logs/`、`cache/`  ← 运行时输出
- **启动脚本**：`C:\noneadcss\Production\start-prod.ps1`
- **停止脚本**：`C:\noneadcss\Production\stop-prod.ps1`
- **启动命令（必须 blocking=false + long_running_process）**：
  ```powershell
  powershell -ExecutionPolicy Bypass -File C:\noneadcss\Production\start-prod.ps1
  ```
- **停止命令**：
  ```powershell
  powershell -ExecutionPolicy Bypass -File C:\noneadcss\Production\stop-prod.ps1
  ```
- **行为**（幂等 + Fail-Fast + 进程保活 while 循环）：
  1. **检测并停止已有服务**：按端口 `5009 / 5172 / 5173` 查找并清理已有进程，并强制清理残留进程名 `NoneadCSS` + `NoneadCSS.Api`（publish 模式下实际 exe 进程名是 `NoneadCSS.Api`，老版本叫 `NoneadCSS`，两者都要杀，否则 exe 文件被锁导致 publish 失败 0x80070020）
  2. **启动后端**：`Start-Process` 启动 `backend/NoneadCSS.Api.exe --urls http://127.0.0.1:5009`，注入 8+ 项环境变量，含：
     - `ASPNETCORE_ENVIRONMENT=Production`
     - `ASPNETCORE_CONTENTROOT=Production/backend`（防止配置文件找不到导致静默退出）
     - `ConnectionStrings__SqliteConnection="Data Source=<绝对路径>;Mode=ReadWrite"`（**彻底消除 CWD vs ContentRoot 路径错配导致的备份/运行操作两套不同数据库文件的陷阱**；不使用 `Cache=Shared`——共享缓存走表级锁抛 `SQLITE_LOCKED`，`busy_timeout` 不覆盖该错误码）
  3. **启动前端 vite preview**：在 `Production/frontend/` 目录下 `npx vite preview --port 5172 --host 127.0.0.1`
  4. **轮询等待 :5009 就绪**：最多 60 秒，通过后执行 `/health` 健康检查
  5. **Nginx 配置预检**：`nginx -t` 验证配置语法
  6. **启动 Nginx**：监听 `5173`，`/` → upstream `127.0.0.1:5172`（vite preview），`/api` + `/hubs` → upstream `127.0.0.1:5009`（后端），支持 WebSocket Upgrade
  7. **脚本末尾 `while ($true) { Start-Sleep -Seconds 60 }`**：**必须**存在，防止自动化脚本（RunCommand blocking=false 等）把子 PowerShell Job Group 连带销毁，3~30 秒内服务全部莫名死掉

> **硬约束**：后端启动必须同时具备两层 5009 保障 —— (1) 命令行参数 `--urls http://127.0.0.1:5009`（优先级最高，覆盖一切配置）；(2) `NoneadCSS.Api/appsettings.Production.json` 首行声明 `"Urls": "http://127.0.0.1:5009"`。**在 `dotnet publish` 之后必须手动执行 `Copy-Item -Force` 把源 `appsettings.Production.json` 覆盖到 `Production/backend/appsettings.Production.json`**，否则 publish 的合并输出会丢失 `Urls` 字段，双保险②失效。

#### 18.9.3 开发环境常驻启动（`scripts/dev-up.ps1` / `scripts/dev-down.ps1` · 2026-11-20）

> **适用场景**：需要"改完代码就一直在"的本地开发环境，尤其是由工具/AI 会话代为启动的场合。

`deploy/restart-dev.ps1` 与手工 `dotnet run` 起的进程都是**当前终端的子进程**：终端关闭、或调用方（编辑器、AI 桌面端）重启时子进程树被连带销毁，后端消失后 Vite 代理 `/api` 只会返回 **502**，登录页于是显示「无法连接后端服务 / Could not load the CAPTCHA」。要长期稳定，进程必须脱离调用者进程树并自带保活。

```powershell
# 启动：构建 → 拉起后端 5008（带保活）→ 拉起前端 5171 → 健康检查
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dev-up.ps1

# 只重启后端（跳过构建）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dev-up.ps1 -SkipBuild -Force

# 停止（默认保留 Vite，-All 一并停）
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dev-down.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/dev-down.ps1 -All
```

- **保活**：`scripts/dev-api-run.cmd` 用 `:loop` 循环跑 `dotnet bin\Debug\net10.0\NoneadCSS.Api.dll`，进程退出后打印退出码并 `timeout /t 5` 再重启；日志写 `%TEMP%\noneadcss-dev\api.log`。停止时必须**先杀 runner 再杀 dotnet**（`dev-down.ps1` 已按此顺序），否则 5 秒后 API 自己回来。
- **脱离进程树**：两个服务都用 `Invoke-CimMethod Win32_Process Create` 启动，父进程是 WMI provider host，不随终端/宿主退出。
- **健康判定**：`dev-up.ps1` 要求 `http://127.0.0.1:5008/api/auth/captcha` 返回 200 **且**响应体含 `svgContent`（只看端口会放过"端口在听但初始化失败"的进程）。
- **只杀自己的进程**：按端口属主的命令行是否匹配 `NoneadCSS\.Api` 判定，别人的进程只提示不动。

两条实测教训（2026-11-20，均已写进脚本，改动脚本前请先读）：

1. **本机 WMI 创建的进程不能执行 `.ps1`**：`cmd /c powershell -File x.ps1` 只得到「拒绝访问。」，直接 `pwsh -File x.ps1` 与 `pwsh -Command "& 'x.ps1'"` 都静默无输出，Windows PowerShell 5.1 同样静默；**只有 `-Command "<内联代码>"` 与 `cmd.exe` + `dotnet`/`npm` 正常**。所以保活逻辑写成 `.cmd` 循环，而不是 PowerShell supervisor。
2. **cmd 里给环境变量赋值必须加引号**：`set ASPNETCORE_ENVIRONMENT=Development && ...` 会把尾随空格算进值（变成 `"Development "`），`IsDevelopment()` 返回 false，随后 `JwtBootstrapValidator` 直接抛「JWT 密钥未配置」，看起来像配置缺失。正确写法 `set "ASPNETCORE_ENVIRONMENT=Development"`。



### 18.10 旧库升级：RowVersion DDL 补丁

> **适用场景**：代码已升级到包含乐观锁的版本（Users/Customers/ChatSessions/ChatMessages/ApiKeys/Webhooks/KnowledgeCategories/KnowledgeArticles + CalendarEvent + TimeEntry 均配置 `RowVersion` / `Version`），但目标数据库仍然是**老 schema**（缺少 `RowVersion` 列）。典型表现：后端启动报 `SQLite Error 1: 'no such column: u.RowVersion'`、EF 迁移无法运行、生产冷升级无法启动。

补丁工具路径：`_DbPatchRowVersion`（控制台项目，依赖 `Microsoft.EntityFrameworkCore.Sqlite` 正确加载 SQLitePCLRaw，避免 PowerShell Add-Type 直接加载 `Microsoft.Data.Sqlite.dll` 的 `FileNotFoundException`）。

> ⚠️ **该工具未随本仓库发布**：`_DbPatchRowVersion/` 是 2026-08-17 在公司生产库上执行的一次性内部补丁程序，开源树里没有这个项目。**全新安装的库不需要它**——`RowVersion` / `Version` 列由 EF 迁移链直接建出。只有把**旧 schema 的库**（< v2.2.3）带到新代码时才需要补列，按下一小节「补丁行为（事务 + 幂等）」列出的 `ALTER TABLE "X" ADD COLUMN "RowVersion" BLOB NOT NULL DEFAULT x'0000000000000000'` 逐表手工执行即可（SQLite 的 `ALTER TABLE ADD COLUMN` 本身幂等性差，执行前先用 `SELECT COUNT(*) FROM pragma_table_info("Users") WHERE name = "RowVersion"` 探测）。

**执行命令**：

```powershell
# 1. 先冷备份（补丁本身是事务幂等，但强烈建议）
Copy-Item C:\noneadcss\Production\data\noneadcss.db "C:\noneadcss\Production\data\noneadcss.db.pre_rowversion_$(Get-Date -Format yyyyMMddHHmm)"

# 2. 运行补丁（默认目标库 = Production/data/noneadcss.db；也可用 --db 显式传）
dotnet run --project _DbPatchRowVersion/_DbPatchRowVersion.csproj -- --db "C:\noneadcss\Production\data\noneadcss.db"
```

**补丁行为（事务 + 幂等）**：

1. 打开目标 SQLite DB，开启 `BEGIN IMMEDIATE` 事务；
2. 对 8 张表（`Users`、`Customers`、`ChatSessions`、`ChatMessages`、`ApiKeys`、`Webhooks`、`KnowledgeCategories`、`KnowledgeArticles`）按以下策略逐张检查：
   - 若 `RowVersion` 列不存在 → `ALTER TABLE "X" ADD COLUMN "RowVersion" BLOB NOT NULL DEFAULT x'0000000000000000'`；
   - 若已存在 → 跳过（幂等）；
3. 对 `CalendarEvents` / `TimeEntries` 同样探测 `RowVersion` 列、对 `Conversations` / `ConversationThreads` / `Mailboxes` 探测 `Version` 列（已存在则跳过），消除 `DbMigrationSafety.FixMigrationHistoryAsync` schema probe 漏判导致 EF 误报"迁移未应用"的问题；
4. `COMMIT` 成功后打印"打补丁完成，表清单：X/Y/Z"，失败自动 `ROLLBACK` 并返回非零 exit code。

**升级后验证**：

```sql
SELECT name FROM pragma_table_info("Users") WHERE name = "RowVersion";    -- 应返回 RowVersion
SELECT name FROM pragma_table_info("ChatMessages") WHERE name = "RowVersion"; -- 应返回 RowVersion
```

> 注意：RowVersion 默认值是 8 字节 0（`x'0000000000000000'`）。首次 `SaveChanges` 时 EF Core 会自动 +1 / 更新值，无需手工初始化。

### 18.11 生产发布 SOP（v2.2.3 固化 · 7 步强制流程 · 2026-08-14）

> 每一步都**必须**执行，任何一步失败禁止进入下一步。步骤数和顺序与 `project_memory.md` Production Deployment SOP 完全一致。

| 步骤 | 动作 | 命令（在仓库根 `C:\noneadcss` 下执行） | 验收门槛 |
|------|------|--------------------------------------------------------|----------|
| ① 停服 + 清理残留 | 先 stop-prod，再强制兜底杀残留进程名（两种名字都要杀） | `powershell -ExecutionPolicy Bypass -File Production/stop-prod.ps1`<br>`Get-Process -Name NoneadCSS,NoneadCSS.Api -ErrorAction SilentlyContinue \| Stop-Process -Force`<br>`Get-Process -Name nginx,node -ErrorAction SilentlyContinue \| ? { $_.Id -in (Get-NetTCPConnection -LocalPort 5172,5173 -ErrorAction SilentlyContinue).OwningProcess } \| Stop-Process -Force` | 端口 5009 / 5172 / 5173 全部 `Get-NetTCPConnection` 返回空 |
| ② 后端 publish | 发布到 `Production/backend`（self-contained false，依赖运行时） | `dotnet publish NoneadCSS.Api/NoneadCSS.Api.csproj -c Release -o Production/backend --self-contained false` | `dotnet publish` 退出码 0；产物 `Production/backend/NoneadCSS.Api.exe` 存在 |
| ③ 前端构建 | 构建到 `frontend/dist`，再同步到 `Production/frontend/dist` | `cd frontend ; npm run build ; cd ..`<br>`Copy-Item frontend/dist/* Production/frontend/dist/ -Recurse -Force` | `npm run build` 成功；`Production/frontend/dist/index.html` mtime ≈ 当前时间 |
| ④ 配置同步 | publish 后覆盖 `appsettings.Production.json`（关键，否则 Urls 丢失） | `Copy-Item -Force NoneadCSS.Api/appsettings.Production.json Production/backend/appsettings.Production.json`<br>`Get-Content Production/backend/appsettings.Production.json \| Select-Object -First 5 \| Out-Host` | 输出首行必须是 `"Urls": "http://127.0.0.1:5009"`（大小写、空格允许不同，但键和值必须存在） |
| ⑤ 启动 | start-prod.ps1 必须以 blocking=false + long_running_process 方式运行，禁止阻塞调用 | `powershell -ExecutionPolicy Bypass -File Production/start-prod.ps1`（非阻塞） | 脚本输出出现 `[OK] 三端就绪摘要`；**脚本进程不得退出**（while true 保活） |
| ⑥ 健康检查 | 四个端点 + 一个负向断言 | 依次 `curl.exe -sS http://127.0.0.1:5009/health` → 200<br>`curl.exe -sS http://127.0.0.1:5172/` → 200<br>`curl.exe -sS http://127.0.0.1:5173/` → 200<br>`curl.exe -sS http://127.0.0.1:5173/health` → 200<br>`Get-NetTCPConnection -LocalPort 5008 -ErrorAction SilentlyContinue` → **必须为空** | 前四个 200，最后一个为空（5008 误监听 = 启动方式错） |
| ⑦ 残留进程名二次核对 | 防止旧版 NoneadCSS.exe（进程名 NoneadCSS）仍监听 5008 造成"双后端混跑" | `Get-Process NoneadCSS,NoneadCSS.Api -ErrorAction SilentlyContinue \| Format-Table Id,ProcessName,Path` | 若看到 `ProcessName=NoneadCSS` 且 `Path` 不是 `Production\backend\NoneadCSS.Api.exe`，立即手动终止并回到步骤① |

> 常见陷阱（踩坑实录）：
> - **陷阱 1：publish 报 0x80070020「拒绝访问」** → 意味着 `Production/backend/NoneadCSS.Api.exe` 被上一轮残留进程占用。步骤①必须同时匹配 `NoneadCSS` 和 `NoneadCSS.Api` 两个名字。
> - **陷阱 2：服务启动 3~30 秒后全部死掉，日志中无异常** → 自动化脚本用了 `blocking=true` 或 start-prod.ps1 末尾缺少 `while ($true) { Start-Sleep -Seconds 60 }`，导致 PowerShell Job Group 被父进程退出回收。回到步骤⑤用阻塞=false + long_running_process 方式启动。
> - **陷阱 3：备份工具备份的是「幽灵 DB」，真实 DB 在别处** → `ConnectionStrings__SqliteConnection` 必须传入**绝对路径**。相对路径 `Data Source=data/noneadcss.db` 会根据"启动脚本的 WorkingDirectory"解析，而 `DbMigrationSafety` 的备份代码使用 `AppContext.BaseDirectory`（= ContentRoot）解析，两者指向不同文件。绝对路径可让两条路径全部收敛到同一文件。

### 18.12 生产必需环境变量（用户级 / User Scope）

以下变量必须在**当前 Windows 用户**下以 [Environment]::SetEnvironmentVariable("X", "Y", "User") 设置一次，重启脚本会自动读取并注入后端：

| 变量名 | 必需 | 默认值 | 说明 |
|--------|------|--------|------|
| `JWT__Key` | ✅ | 无 | JWT HS256 签名密钥，推荐 48 字节以上随机 Base64；**< 32 字节将在启动阶段 Fail-Fast 抛错** |
| `JWT__Issuer` | 推荐 | `NoneadCSS` | JWT iss 声明，应与各端一致 |
| `JWT__Audience` | 推荐 | `NoneadCSSApi` | JWT aud 声明 |
| `ENCRYPTION__Key` | ✅ | 无 | AES-GCM 加密密钥（Base64，32 字节），用于加密邮箱密码、微信 AppSecret、钉钉/飞书凭证等敏感字段 |
| `SECRET_HASHER__Key` | ✅ | 无 | 密码哈希附加密钥（Base64，64 字节），与 BCrypt 配合增强密码存储安全性；缺失将导致后端启动失败 |
| `AUDIT__HmacKey` | ✅ | 无 | 审计日志 HMAC-SHA256 签名密钥，保证审计记录不可篡改；建议使用与 ENCRYPTION__Key 独立的随机值 |
| `APP__Url` | 推荐 | 无 | 前台对外 URL（如 `http://css.example.com`），无 HTTP 上下文时（Quartz、工作流等邮件）生成重置/满意度链接的回退值；等同于旧 `App:FrontendUrl` |
| `APP__SatisfactionTokenKey` | 推荐 | 无 | 满意度评价链接独立签名密钥（HMAC），为空时回退到 `JWT__Key` |
| （微信为直连） | — | — | 微信固定直连 `https://api.weixin.qq.com`，本项目**不含**任何中转/代理网关、也不发送 `X-Proxy-Key`；生产环境需保证服务器可出网访问该域名，并在微信公众平台把出口 IP 加入白名单、把回调地址配成公网 HTTPS |
| `WECHAT__AppId` / `AppSecret` / `Token` / `EncodingAESKey` | 推荐 | 数据库覆盖 | 数据库无 WeChat 配置时作为 baseline 载入；保存配置后以数据库最新值为准 |

设置示例（一次性，管理员权限 PowerShell）：

```powershell
[Environment]::SetEnvironmentVariable("JWT__Key",        "<48+字节随机Base64>",               "User")
[Environment]::SetEnvironmentVariable("ENCRYPTION__Key", "<AES-32字节Base64>",                 "User")
[Environment]::SetEnvironmentVariable("AUDIT__HmacKey",  "<32+字节随机Base64>",                 "User")
[Environment]::SetEnvironmentVariable("APP__SatisfactionTokenKey", "<32+字节随机Base64>", "User")
```

---

## 19. 安全机制

### 19.1 认证安全

系统支持多种认证方式，并对其做了安全加固：

- **JWT + Cookie 双通道**：API 请求使用 JWT Bearer Token；浏览器端可使用 Cookie 认证，Token 写入 HttpOnly Cookie，前端 JS 无法读取，降低 XSS 窃取风险
- **Refresh Token**：刷新令牌存储于数据库并使用 `SameSite=Strict` 属性，防止 CSRF 攻击利用刷新令牌
- **API Key 认证**：通过 `ApiKey` 实体管理长期访问凭证，支持启停、权限范围限定与访问日志记录（`ApiKeyLog`），适用于系统集成场景

### 19.2 CSRF 防护

[CsrfProtectionMiddleware](../NoneadCSS.Api/Middleware/CsrfProtectionMiddleware.cs) 提供服务端 CSRF 防护：

- 对不安全的 HTTP 方法（POST/PUT/PATCH/DELETE）校验 CSRF Token
- Token 比较使用 `CryptographicOperations.FixedTimeEquals` 进行**常量时间比较**，防止时序攻击
- JWT Bearer 请求与安全方法（GET/HEAD/OPTIONS）豁免校验
- 详见 [8.4 CSRF 防护](#84-csrf-防护)

### 19.3 数据加密

敏感数据采用 AES-GCM 算法加密存储：

- **`IEncryptionService`**：统一加密服务接口，对邮箱密码、SMTP 凭证等敏感字段加密后存入数据库
- **AES-GCM**：使用认证加密（Authenticated Encryption），同时保证机密性与完整性
- **密钥管理**：加密密钥通过 `Encryption:Key` 配置项提供（开发环境在 `appsettings.Development.json`，生产环境通过环境变量/密钥管理服务）
- **随机 IV**：每次加密生成随机初始化向量（IV），相同明文产生不同密文

### 19.4 安全响应头

[SecurityHeadersMiddleware](../NoneadCSS.Api/Middleware/SecurityHeadersMiddleware.cs) 统一注入安全响应头：

- **CSP（Content-Security-Policy）**：采用 nonce-based 策略，每请求一次性 nonce，防御 XSS
- **X-Content-Type-Options: nosniff**：阻止 MIME 嗅探
- **X-Frame-Options**：防御点击劫持
- **Referrer-Policy**：限制 Referer 泄露
- 详见 [8.5 安全响应头](#85-安全响应头)

### 19.5 输入验证与注入防护

对用户输入进行多层校验，防御常见 Web 攻击：

- **CRLF 注入防护**：过滤输入中的 `\r`、`\n` 字符，防止 HTTP 响应拆分与邮件头注入
- **邮件头深度防御**：[EmailService](../NoneadCSS.Infrastructure/Services/EmailService.cs) 新增 `SanitizeHeaderInput()` 方法，在 `SendEmailWithHeadersAsync`、`SendSystemNotificationAsync`、`SaveToSentFolderAsync` 三处邮件构造路径中，对 Subject 和 From 显示名进行显式清理——移除 C0 控制字符（0x00-0x1F）、DEL（0x7F），按 RFC 5322 限制单行头字段长度（Subject ≤ 78，显示名 ≤ 200），作为 MimeKit 内部编码之外的深度防御层
- **SSRF 防护**：对外部 URL 请求（如 Webhook 回调、邮件链接抓取）进行目标地址校验，阻止访问内网地址（127.0.0.1、10.x、172.16-31.x、192.168.x、169.254.x 等）
- **XSS 防护（前端）**：前端渲染用户内容时使用 `DOMPurify` 进行 HTML 消毒，禁止使用 `dangerouslySetInnerHTML`
- **XSS 防护（后端 - 邮件模板）**：邮件模板中插入 HTML 上下文的变量值使用 `WebUtility.HtmlEncode` 编码，防止通过变量注入恶意 HTML
- **路径遍历防护**：附件文件路径使用 `Path.GetFullPath()` 规范化后，通过 `StartsWith()` 进行边界检查，阻止 `../` 路径遍历攻击
- **模型验证**：使用 Data Annotations + FluentValidation 校验请求模型，拒绝非法输入

### 19.6 密码安全

- **BCrypt 哈希**：用户密码使用 `BCryptPasswordHasher` 进行 BCrypt 哈希存储，不存明文
- **HMAC-SHA256 签名**：密码重置令牌等使用 HMAC-SHA256 签名，防止伪造
- **密码策略增强**：通过 `IPasswordPolicyService` 校验密码强度，包含：
  - 最小长度要求（默认 `min_length=10`）
  - 弱密码字典检查（内置常见弱密码列表）
  - 常见模式检测：连续字符（abcdef、123456）、键盘序列（qwerty）、重复字符（aaaaaa）
  - 可配置的字符类型要求（大写/小写/数字/特殊字符）
- **安全随机数**：生成 token、密码重置链接等使用 `RandomNumberGenerator` 而非 `Random`
- **管理员密码**：从环境变量 `ADMIN_PASSWORD` 读取，不再有默认密码；首次启动未配置时自动生成随机密码并输出到日志
- **密码重置安全**：
  - 重置成功后失效该用户所有未使用的重置令牌
  - 同步撤销用户所有 Refresh Token，防止会话劫持
  - 密码更新 + 令牌失效 + Refresh Token 撤销在同一事务内原子执行

### 19.7 异常处理与信息泄露防护

#### 全局异常中间件

[GlobalExceptionMiddleware](../NoneadCSS.Api/Middleware/GlobalExceptionMiddleware.cs) 统一处理异常并防止敏感信息泄露：

| 状态码 | 返回给客户端的消息 | 说明 |
|--------|-------------------|------|
| 400 BadRequest | "请求参数无效，请检查输入。" | 不返回具体验证错误详情 |
| 401 Unauthorized | "未授权，请先登录。" | 统一未授权提示 |
| 403 Forbidden | "没有权限执行此操作。" | 不暴露权限判断逻辑 |
| 404 NotFound | "请求的资源不存在。" | 不泄露资源是否存在的细节 |
| 409 Conflict | "操作冲突，请重试。" | 通用冲突提示 |
| 500+ | "服务器内部错误，请稍后重试或联系管理员。" | 绝不返回异常堆栈或内部错误信息 |

#### 日志策略

- **完整日志**：所有异常的完整堆栈、消息、上下文信息均记录到 Serilog 日志（仅服务端可见）
- **用户侧脱敏**：客户端仅收到安全的通用提示，不暴露任何内部实现细节
- **审计日志**：操作审计记录到 `AuditLog` 表，异常信息记录在 `newValues['_error']` 字段

### 19.8 权限控制

- **`RequireSuperAdmin`**：超级管理员专属操作（如系统配置、用户管理）通过 `RequireSuperAdmin` 约束，仅 `admin` 角色可访问
- **IDOR 防护**：资源访问校验所有权/权限，防止越权访问他人资源（如访问他人会话、客户需校验邮箱权限）
- **邮箱权限校验**：通过 `MailboxUser` 关联表校验用户对特定邮箱的访问权限，客服只能操作被授权邮箱下的数据
- **角色权限隔离**：权限通过 `PermissionsJson` 存储，支持邮箱级别细粒度权限隔离

### 19.9 客户头像 URL 协议白名单校验

[CustomerService](../NoneadCSS.Application/Services/CustomerService.cs) 在创建和更新客户时对头像 URL 进行协议级安全校验，防止 XSS/钓鱼/本地文件读取等风险：

```csharp
// 仅允许 http/https/data:image 协议，禁止 ftp/file/javascript/vbscript 等危险协议
private bool IsValidAvatarUrl(string? url)
{
    if (string.IsNullOrEmpty(url)) return true; // 允许空值（清除头像）
    return Regex.IsMatch(url, @"^https?://|^data:image/", RegexOptions.IgnoreCase);
}
```

**校验位置**：
- `CreateCustomerAsync`：创建客户时校验 AvatarUrl
- `UpdateCustomerAsync`：更新客户时校验 AvatarUrl（若未传则跳过，保留原值）

**违规响应**：返回 `400 Bad Request` + `头像 URL 只能使用 http/https 或 data:image 协议`

---

## 附录

### A. 响应格式

#### 成功响应

```json
{
  "data": { ... },
  "message": "操作成功"
}
```

#### 分页响应

```json
{
  "data": [...],
  "total": 100,
  "page": 1,
  "pageSize": 20,
  "totalPages": 5
}
```

#### 错误响应

```json
{
  "message": "错误描述",
  "errors": ["错误详情1", "错误详情2"],
  "traceId": "0HLQ7K123ABC"
}
```

### B. HTTP 状态码

| 状态码 | 说明 |
|--------|------|
| 200 | 成功 |
| 201 | 创建成功 |
| 400 | 请求参数错误 |
| 401 | 未认证 |
| 403 | 无权限 |
| 404 | 资源不存在 |
| 500 | 服务器内部错误 |

### C. 数据库表命名约定

实体映射到数据库表时，使用复数形式：

| 实体 | 表名 |
|------|------|
| `User` | `Users` |
| `Conversation` | `Conversations` |
| `Mailbox` | `Mailboxes` |

### D. 字段命名约定

- 使用 PascalCase 命名属性
- 使用 camelCase 命名 JSON 属性（通过 `JsonPropertyName`）
- 外键字段命名：`{EntityName}Id`

### E. 代码生成

项目支持 EF Core 工具进行数据库操作：

```bash
# 安装工具
dotnet tool install --global dotnet-ef

# 创建模型快照
dotnet ef migrations add InitialCreate --project ../NoneadCSS.Infrastructure
```

---

## 20. 全局速率限制

### 20.1 策略配置

系统使用 .NET 滑动窗口速率限制器，在 [Program.cs](../NoneadCSS.Api/Program.cs) 中配置多级限流策略：

| 策略名称 | 每 IP 限额 | 时间窗口 | 段数 | 应用场景 |
|----------|-----------|---------|------|---------|
| `GlobalPolicy` | 300 次/分钟 | 60 秒 | 6 | 全局默认限流（未指定策略的接口） |
| `SearchPolicy` | 30 次/分钟 | 60 秒 | 4 | 全局搜索接口 |
| `ReportsPolicy` | 10 次/分钟 | 60 秒 | 4 | 报表生成接口 |

### 20.2 使用方式

在控制器方法上应用 `[EnableRateLimiting("PolicyName")]` 特性：

```csharp
[HttpGet]
[EnableRateLimiting("SearchPolicy")]
public async Task<IActionResult> GlobalSearch(...)
```

### 20.3 配置选项

```json
{
  "RateLimiting": {
    "Global": { "PermitLimit": 300, "WindowSeconds": 60, "SegmentsPerWindow": 6 },
    "Search": { "PermitLimit": 30, "WindowSeconds": 60, "SegmentsPerWindow": 4 },
    "Reports": { "PermitLimit": 10, "WindowSeconds": 60, "SegmentsPerWindow": 4 }
  }
}
```

### 20.4 设计说明

- **滑动窗口算法**：将时间窗口分为多段，仅统计最近一段的请求数，比固定窗口更平滑
- **队列处理**：超出限额时默认拒绝（`QueueLimit = 0`），不排队等待
- **IP 级别**：默认按客户端 IP 限流，不区分用户身份
- **响应格式**：超限返回 HTTP 429 Too Many Requests

---

## 21. 全局搜索

### 21.1 概述

[SearchController](../NoneadCSS.Api/Controllers/SearchController.cs) 提供统一的跨实体搜索接口，支持在会话、客户、知识库文章中进行全文搜索。

### 21.2 API 接口

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/search?q=keyword` | 搜索所有实体类型 |
| GET | `/api/search?q=keyword&entity=conversation` | 仅搜索会话 |
| GET | `/api/search?q=keyword&entity=customer` | 仅搜索客户 |
| GET | `/api/search?q=keyword&entity=knowledge` | 仅搜索知识库 |

### 21.3 请求参数

| 参数 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `q` | string | （必填） | 搜索关键词 |
| `entity` | string | `all` | 搜索范围：`all` / `conversation` / `customer` / `knowledge` |
| `page` | int | 1 | 页码 |
| `pageSize` | int | 20 | 每页数量（最大 100） |

### 21.4 响应格式

```json
{
  "results": [
    {
      "entityType": "conversation",
      "id": 123,
      "title": "会话主题",
      "snippet": "匹配内容摘要...",
      "highlightField": "subject",
      "mailboxId": 1,
      "status": "active",
      "createdAt": "2026-07-26T10:00:00Z",
      "url": "/conversations/123"
    }
  ],
  "total": 42,
  "page": 1,
  "pageSize": 20,
  "totalPages": 3
}
```

### 21.5 搜索范围

| 实体类型 | 搜索字段 | 说明 |
|---------|---------|------|
| 会话 (`conversation`) | Subject、Thread 正文 | 支持按邮箱和状态过滤 |
| 客户 (`customer`) | 姓名、邮箱、电话、公司 | 支持按活跃状态过滤 |
| 知识库 (`knowledge`) | 标题、内容、分类名 | 支持按状态和置顶过滤 |

### 21.6 安全设计

- **速率限制**：应用 `SearchPolicy`（30 次/分钟）防止滥用
- **权限校验**：用户只能搜索自己有权限访问的数据
- **分页限制**：`pageSize` 上限为 100 条
- **性能优化**：使用 `AsNoTracking()` 和 `Take()` 限制查询范围

---

## 22. 数据库索引优化

### 22.1 关键索引列表

为高频查询场景添加了复合索引，覆盖报表统计、会话列表、发送日志等核心查询：

| 表 | 索引字段 | 过滤条件 | 优化场景 |
|----|---------|---------|---------|
| `Conversations` | `(MailboxId, CreatedAt)` | `DeletedAt IS NULL` | 按邮箱+日期范围的新建/关闭统计 |
| `ConversationThreads` | `(ConversationId, Type, CreatedAt)` | - | 按会话+类型+时间的报表查询 |
| `ConversationThreads` | `(MessageId)` | - | 邮件线程关联查询（高频） |
| `SendLogs` | `(MailboxId, CreatedAt)` | - | 按邮箱+日期的发送统计聚合 |
| `Customers` | `(MailboxId, CreatedAt)` | `DeletedAt IS NULL` | 按邮箱+日期的客户统计 |
| `Tags` | `(MailboxId, Name)` (唯一) | `DeletedAt IS NULL` | 标签唯一性校验（允许软删除后重建同名标签） |
| `Stars` | `(ConversationId, UserId)` (唯一) | - | 会话收藏唯一性校验 |
| `Stars` | `(UserId)` | - | 按用户列出收藏会话 |
| `Attachments` | `(ThreadId)` | - | 按线程查询附件列表 |
| `NotificationRules` | `(EventType, IsActive, MailboxId, UserId, DeletedAt)` | - | 按事件类型+激活+邮箱+用户+软删除查询（核心通知分发路径） |
| `NotificationRules` | `(MailboxId, IsActive, DeletedAt)` | - | 按邮箱维度列出激活的通知规则 |
| `NotificationRules` | `(UserId, DeletedAt)` | - | 按用户维度查询通知规则 |
| `NotificationRules` | `(TemplateId, DeletedAt)` | - | 按通知模板反查关联的规则 |

### 22.2 SQLite 并发优化

[SqliteConnectionInterceptor](../NoneadCSS.Infrastructure/Persistence/SqliteConnectionInterceptor.cs) 配置 `busy_timeout=5000ms`：

- 当 SQLite 遇到并发写入冲突时，自动等待最多 5 秒
- 有效减少多线程环境下的 `SQLITE_BUSY` 错误
- 适用于开发环境和单机部署场景

### 22.3 索引设计原则

1. **高频查询优先**：为报表、列表页等高频访问路径添加索引
2. **复合索引**：利用 EF Core 多列索引覆盖常用排序/过滤字段
3. **软删除感知**：唯一索引添加 `HasFilter("DeletedAt IS NULL")` 支持软删除后的重建
4. **避免过度索引**：索引增加写入开销，只为高频读取场景添加

---

## 23. 单元测试

### 23.1 测试栈

- **测试框架**：xUnit 2.9.3
- **Mock 库**：Moq 4.20.72
- **断言库**：FluentAssertions 7.0.0
- **覆盖率工具**：coverlet 6.0.4 + ReportGenerator
- **目标框架**：.NET 10

### 23.2 测试基础设施

#### TestAsyncQueryProvider

[AsyncTestHelpers.cs](../NoneadCSS.Tests/Helpers/AsyncTestHelpers.cs) 提供 EF Core 异步 LINQ 操作的 Mock 支持：

```csharp
// 创建支持异步查询的 Mock DbSet
var mockDbSet = TestAsyncQueryProvider.CreateMockDbSet(users.ToList());
_mockUserRepository
    .Setup(r => r.GetQueryable(false))
    .Returns(mockDbSet.Object);
```

**解决的问题**：
- 标准 Moq `DbSet<T>` 不实现 `IAsyncQueryProvider`，导致 `FirstOrDefaultAsync`、`CountAsync` 等异步 LINQ 方法抛异常
- `TestAsyncQueryProvider` 包装同步 `IQueryProvider`，将异步调用转为同步执行后包装为 `Task<T>`
- `TestAsyncEnumerator` 实现 `IAsyncEnumerator<T>`，支持 `async foreach`

#### SecretHasher 初始化

`SecretHasher` 是全局静态加密器，使用前必须调用 `Initialize(byte[] key)`。测试类通过静态构造函数初始化：

```csharp
public class AuthServiceTests
{
    static AuthServiceTests()
    {
        SecretHasher.Initialize("TestSecretKeyForSecretHasherInitialization12345678");
    }
}
```

#### JWT Token 生成（测试过期 token）

生成已过期的 JWT token 时，`NotBefore` 必须早于 `Expires`：

```csharp
var tokenDescriptor = new SecurityTokenDescriptor
{
    NotBefore = DateTime.UtcNow.AddMinutes(-30), // 30 分钟前生效
    Expires = DateTime.UtcNow.AddMinutes(-10),   // 10 分钟前过期
    // ...
};
```

### 23.3 测试覆盖情况

#### 覆盖率指标（2026-08-04）

| 指标 | 数值 |
|------|------|
| 测试总数 | 846 |
| 通过测试 | 842 |
| 失败测试 | 0 |
| 跳过测试 | 4（依赖 In-Memory DB，不支持 `ExecuteDeleteAsync`） |
| 行覆盖率 | 5.1%+ |
| 分支覆盖率 | 13.2%+ |
| 方法覆盖率 | 18.4%+ |

#### 已覆盖服务（单元测试覆盖率 ≥ 90%）

| 服务 | 测试数 | 覆盖率 |
|------|-------|--------|
| ApiKeyService | 31 | 100% |
| DashboardService | 18 | 100% |
| ConversationDraftService | 28 | 100% |
| ConversationAssignmentService | 25 | 100% |
| ModuleService | 41 | 100% |
| ChatService | 46（新增） | 90%+ |
| TagService | 全面 | 100% |
| EmailSignatureService | 全面 | 100% |
| CannedResponseService | 全面 | 100% |
| SystemOptionService | 全面 | 99% |
| PasswordPolicyService | 全面 | 97% |
| TeamService | 47 | 99.5% |
| RoleService | 全面 | 92% |

#### InternalsVisibleTo 测试约定

对于需要在测试项目中访问内部方法（`internal` 访问修饰符）的场景，通过 `InternalsVisibleTo` 程序集属性暴露给测试项目，**避免将方法改为 public 导致 API 面膨胀**：

```xml
<!-- NoneadCSS.Application.csproj -->
<ItemGroup>
  <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleTo">
    <_Parameter1>NoneadCSS.Tests</_Parameter1>
  </AssemblyAttribute>
</ItemGroup>
```

典型场景：
- `CustomerService.ValidateAndNormalizeAvatarUrl`：头像 URL 校验纯函数，标记为 `internal`，通过 InternalsVisibleTo 直接在测试中调用
- 仅在单元测试中需要断言的内部状态或辅助方法
- **注意**：仅对「纯函数 / 无副作用 / 不暴露敏感数据」的内部方法使用此模式；业务入口仍保持 public 接口契约

#### 待覆盖服务（优先级排序）

1. **EmailService** — 邮件发送核心逻辑（IMAP/SMTP）
2. **WorkflowService** — 工作流执行引擎
3. **EmailIngestionService** — 邮件收取与解析
4. **ConversationService** — 会话核心服务（已部分覆盖）
5. **NotificationService** — 通知中心（已部分覆盖）
6. **AuthService** — 认证授权（已部分覆盖）

### 23.4 测试约定

#### 命名规范

- 测试方法：`MethodName_Condition_ExpectedResult`
- 测试类：`{ServiceName}Tests`
- 文件位置：`NoneadCSS.Tests/Services/{ServiceName}Tests.cs`

#### AAA 模式

```csharp
[Fact]
public async Task CreateAsync_WithValidRequest_ShouldCreateAndReturn()
{
    // Arrange - 准备测试数据
    var request = new CreateRequest { Name = "Test" };
    _mockRepo.Setup(r => r.AddAsync(It.IsAny<Tag>()))
        .Callback<Tag>(t => t.Id = 1)
        .ReturnsAsync((Tag t) => t);

    // Act - 执行被测试方法
    var result = await _service.CreateAsync(request);

    // Assert - 验证结果
    result.Should().NotBeNull();
    result.Name.Should().Be("Test");
    _mockUnitOfWork.Verify(u => u.SaveChangesAsync(), Times.Once);
}
```

#### DateTime 断言约定

- 非空 `DateTime`：使用 `.NotBe(default)`，**不要**使用 `.NotBeNull()`
- 可空 `DateTime?`：使用 `.NotBeNull()` 或 `.BeNull()`

```csharp
// ✅ 正确
entity.UpdatedAt.Should().NotBe(default);
entity.DeletedAt.Should().NotBeNull();

// ❌ 错误（编译错误）
entity.UpdatedAt.Should().NotBeNull();
```

#### PagedResponse 断言约定

```csharp
// ✅ 正确
result.Data.Should().HaveCount(2);
result.TotalItems.Should().Be(2);

// ❌ 错误
result.Items.Should().HaveCount(2);
result.Total.Should().Be(2);
```

### 23.5 运行测试与生成覆盖率报告

```bash
# 运行所有测试
dotnet test NoneadCSS.Tests/NoneadCSS.Tests.csproj

# 生成覆盖率数据
dotnet test NoneadCSS.Tests/NoneadCSS.Tests.csproj \
  --collect:"XPlat Code Coverage" \
  --results-directory "TestResults-Coverage"

# 生成 HTML 报告
reportgenerator \
  -reports:"TestResults-Coverage/{guid}/coverage.cobertura.xml" \
  -targetdir:"TestResults-Coverage/Report" \
  -reporttypes:HtmlSummary
```

覆盖率报告位置：`TestResults-Coverage/Report/summary.html`

### 23.6 CI/CD 集成建议

- 在 PR 流程中运行 `dotnet test` 并设置覆盖率阈值（建议 20%）
- 使用 `coverlet` 的 `--threshold` 参数强制最低覆盖率
- 覆盖率下降超过 2% 时阻止合并

### 23.7 端到端测试（Playwright）

```bash
cd frontend
npx playwright install chromium   # 首次：下载浏览器（CI 里可缓存）
pnpm dev                          # 另开终端：http://127.0.0.1:5171
# 后端需在 5008 运行（前端 /api 代理到 5008）
npm run test:e2e
```

- 用例目录 `frontend/e2e/`，配置 `frontend/playwright.config.ts`：`testDir: './e2e'`、`workers: 1`（与登录态/固定端口相关，串行更稳）、失败时保留 trace/截图/录像。
- 基线用例只覆盖**不需要凭据**的场景（未登录访问 `/conversations` 跳转 `/login`、登录页渲染邮箱/密码/验证码）。需要登录的用例通过环境变量注入账号：`E2E_EMAIL` / `E2E_PASSWORD`，未设置则 `test.skip` —— 仓库内不留任何账号口令。
- 验证码是图片，自动化无法识别：登录用例以 `0000` 提交，并把「需人工过一次验证码」标注为已知限制；不要为了跑通用例而放松服务端校验。
- 目标地址可用 `E2E_BASE_URL` 覆盖（默认 `http://127.0.0.1:5171`）。
- 运行产物 `playwright-report/`、`test-results/`、`.playwright/`、`blob-report/` 已写入 `.gitignore`，不要提交。

---

## 24. 微信集成开发指南

### 24.1 架构总览

微信集成模块横跨 **Infrastructure → Application → API** 三层：

```
                          ┌──────────────────────────────────────────────┐
微信服务器 → 回调推 ───┤  WeChatController (匿名)                       │
 GET /api/wechat (签名校验) │  → WeChatMessageParser (SHA1 + AES-CBC)     │
 POST /api/wechat (消息)   │  → WeChatIngestionService (接入主流程)      │
                          │    └── 客户匹配 / 会话匹配 / 事务持久化      │
                          │    └── 媒体素材下载 (DownloadMediaAsync)     │
                          │    └── 触发工作流                            │
                          └──────────────────────────────────────────────┘
┌──────────────────────────────────────────────┐
客服在界面回复  ───┤  WeChatController (需 wechat.manage)             │
 POST /api/wechat/reply│  → WeChatService.ReplyAsync                    │
                      │    └── ClassifyMediaAttachment (按 MIME 分类)   │
                      │    └── UploadMediaAsync (临时素材上传)           │
                      │    └── SendXxxMessageAsync (客服消息推送)       │
                      │    └── 持久化回复 Thread + 关联附件              │
                      │    └── 事务提交后 → SignalR reply.created 推送   │
                      └──────────────────────────────────────────────┘
┌──────────────────────────────────────────────┐
微信基础服务       │  WeChatSettingsProvider (配置)                       │
 (Infrastructure) │  WeChatAccessTokenService (Access Token 缓存/刷新)  │
                   │  WeChatClient (HttpClient + Polly 重试)              │
                   │  WeChatMessageParser (签名/加解密)                    │
                   └──────────────────────────────────────────────┘
```

### 24.2 消息接入流程（WeChatIngestionService）

复刻邮件接入的成熟流水线，统一事务 + 工作流边界：

| 步骤 | 方法 | 说明 |
|------|------|------|
| 1. 签名校验 | `WeChatMessageParser.ValidateSignature()` | 校验 `signature/timestamp/nonce` Token+SHA1 三元素签名，防伪造回调 |
| 2. 消息解析 | `WeChatMessageParser.Parse()` | 支持明文 XML 与 AES-CBC 安全加密模式（EncodingAESKey 43 位 Base64） |
| 3. 消息去重 | `wx_{MsgId}` 指纹 | `ConversationThread.MessageId` 唯一键防重复投递；事件类无 MsgId 则基于事件内容 + CreateTime 生成幂等指纹 |
| 4. 客户匹配 | `TryGetOrCreateCustomerAsync()` | 优先按 `FromUserName` (OpenID) 匹配；匹配失败调用 `GetUserInfoAsync` 提取昵称/头像/OpenID/UnionID 新建客户档案；`ExtractNickname` 内联避免 Application 跨层引用 Infrastructure |
| 5. 会话匹配 | `FindMatchingConversationAsync()` | 24 小时窗口内存在未关闭同客户会话 → 追加；否则新建会话 |
| 6. 事务持久化 | `UoW.BeginTransactionAsync()` | 新建 Customer / Conversation / Thread / Attachment 全部包裹；工作流触发后 SaveChanges + Commit |
| 7. 素材下载 | `EnsureMediaDownloadedAsync()` | 优先级：`DownloadMediaAsync(MediaId)` > 图片消息的 PicUrl（兜底）；失败不阻断主流程，仅记录日志 |
| 8. 事件处理 | 各事件分支 | subscribe → 构建欢迎语 XML 被动回复返回（无需走客服消息）；unsubscribe/click/view/location → 仅归档并触发工作流 |

### 24.3 回复流程（WeChatService.ReplyAsync）

回复流程严格区分**文本**与**富媒体**，并利用 SignalR 保证跨用户刷新实时性：

1. **权限校验**：检查 `wechat.manage` 声明与会话归属
2. **窗口判断**：基于客户最新互动线程创建时间计算 `48h - elapsed`，如窗口已过期，文本和附件全部改为返回模板消息提示（或保存为草稿并 Toast 告警）
3. **附件分类**：`ClassifyMediaAttachment` 通过 `attachment.ContentType` / `attachment.FileName` 后缀判断：
   - `image/*` → `image` 类型，大小上限 2MB
   - `audio/*` / 扩展名 .mp3/.amr/.speex → `voice`，2MB
   - `video/*` → `video`，10MB
   - 其他类型：跳过上传 + 收集到 `mediaErrors`，toast 汇总提示
4. **素材上传**：`_weChatClient.UploadMediaAsync(type, stream, fileName)` 使用 `MultipartFormDataContent` 按微信规范构造 `media` 字段名，成功解析 JSON `{type, media_id, created_at}`，失败检查 errcode
5. **客服消息发送**：
   - 文本 → `SendTextMessageAsync`
   - 图片 → `SendImageMessageAsync`（{touser,msgtype,image:{media_id}}）
   - 语音 → `SendVoiceMessageAsync`
   - 视频 → `SendVideoMessageAsync`（含 title/description 字段）
6. **失败兜底**：单个发送失败不阻断其他附件；错误写入 `SendStatus` + 详细日志
7. **SignalR 推送**：事务提交后显式 `_uow.Entry(reply).Reference(r => r.User).LoadAsync()` 加载 User 导航属性，再 `_notificationPublisher.NotifyMailboxAsync(mailboxId, EventIds.ReplyCreated, threadResponse)`；`try/catch` 隔离失败，推送失败不影响已持久化的回复
8. **幂等**：Reply 内的客户消息窗口判断、附件去重（Thread 内同 AttachmentId 不重复上传）

### 24.4 WeChatClient 接口契约（Infrastructure 层）

```csharp
public interface IWeChatClient
{
    // 客服消息（微信客服端 → 粉丝）
    Task SendTextMessageAsync(string openId, string content, CancellationToken ct);
    Task SendImageMessageAsync(string openId, string mediaId, CancellationToken ct);
    Task SendVoiceMessageAsync(string openId, string mediaId, CancellationToken ct);
    Task SendVideoMessageAsync(string openId, string mediaId, string? title, string? description, CancellationToken ct);

    // 临时素材（media_id 3 天有效，非图文消息永久素材走 UploadMaterialAsync，另有接口可选）
    Task<WeChatMediaUploadResponse> UploadMediaAsync(string type, Stream content, string fileName, CancellationToken ct);
    Task<Stream> DownloadMediaAsync(string mediaId, CancellationToken ct);

    // 客户资料
    Task<JsonDocument?> GetUserInfoAsync(string openId, CancellationToken ct);

    // 模板消息
    Task SendTemplateMessageAsync(string openId, string templateId, string? url, string? miniProgram,
        Dictionary<string, WeChatTemplateDataItem> data, CancellationToken ct);

    // 自定义菜单
    Task CreateMenuAsync(string menuJson, CancellationToken ct);
    Task<JsonDocument?> GetMenuAsync(CancellationToken ct);
    Task DeleteMenuAsync(CancellationToken ct);
}
```

**HttpClient 注入配置**（DependencyInjection.cs）：
- 命名 HttpClient，BaseAddress `https://api.weixin.qq.com/`
- Polly 重试：Transient HTTP 错误 + 微信 API 返回 `errcode != 0` 时自动重试 3 次（指数退避）；40001/40014（Token 失效）走 AccessTokenService 强制刷新后再重试一次
- AddHttpMessageHandler 注入 `WeChatAccessTokenHandler`（DelegatingHandler）：每次外发请求前查询 AccessTokenService，附加 `?access_token={token}` query 参数

### 24.5 Access Token 管理（微信 2000 次/日限额保护）

**问题背景**：AccessToken 微信官方限制每 2 小时过期，且刷新接口每日调用次数有限。简单方案每次请求都刷新会迅速耗尽额度。

**IWeChatAccessTokenService 设计**：
- **一级缓存**：内存缓存 `MemoryCache`，Key = `wechat_access_token`，过期时间 = API 返回 `expires_in` - 5 分钟（提前 5 分钟过期触发主动刷新，避免边界时刻调用失败）
- **二级缓存**：`appsettings.json` / 数据库 SystemOption `WeChatAccessToken` 字段作为兜底冷启动
- **互斥刷新**：`SemaphoreSlim(1,1)` 单飞模式，并发刷新仅第一个请求执行 HTTP 调用，其他请求等待返回同一个新值
- **强制刷新**：当 Handler 观察到 API 返回 `errcode=40001 invalid credential` 或 `40014 invalid access_token`，调用 `ForceRefreshAsync()` 忽略缓存立即刷新并再重试
- **异常处理**：刷新失败时返回旧值（如果仍在理论有效期内）并写 Error 级日志 + 告警 webhook

### 24.6 48 小时客服消息窗口（前端 + 后端配合）

后端 `WeChatService.GetConversationContext` 返回 `lastCustomerInteractionAt`，前端 `ConversationDetail` 用 `useMemo` 计算窗口状态：

```
windowMs = 48h = 172_800_000 ms
elapsed  = now - lastCustomerInteractionAt
remainingMs = windowMs - elapsed

if remainingMs <= 0       → expired (红色 进度条100%)
elif remainingMs <= 12h   → warning (琥珀色 剩余进度条)
else                      → ok      (绿色 剩余进度条)
```

**动态轮询间隔**：通过 `weChatWindowRemainingRef` 记录剩余时间，`useEffect` 的 interval 为：
- remaining > 1h：`setInterval(60_000)` 每分钟刷新一次（避免无谓重渲染）
- remaining < 1h：`setInterval(1_000)` 每秒刷新显示秒级倒计时

**过期跳转**：UI 显示「前往模板消息」胶囊按钮，`navigate('/wechat?tab=template')` 深链直达；WeChat.tsx 支持 `?tab=template` URL searchParam 同步到 Tab state。

### 24.7 安全与合规要点

| 类别 | 做法 |
|------|------|
| **回调防伪造** | 每次回调先校验 SHA1 签名（token+timestamp+nonce 字典序拼接 → SHA1 → 与 signature 对比），不通过直接 403 |
| **消息加解密** | 启用安全模式时 AES-CBC-256：`EncodingAESKey` 为 Base64(32字节密钥+填充)；解密后还要对比 `msg_signature`；**不要把错误明文暴露给调用方**，仅返回通用的 `success` XML 防止探测 |
| **OpenID 脱敏** | UI 显示仅显示 OpenID 末尾 6 位（`OpenID: …XXXXXX`）；复制到剪贴板是完整值；日志中 OpenID 视 GDPR 合规要求决定是否脱敏或哈希 |
| **素材文件校验** | 上传前严格校验扩展名与文件头签名（PNG=8950、JPG=FFD8FF、GIF=474946、MP3=FFFB/ID3、AMR=#!AMR、MP4=ftyp box），MIME 类型与 Content-Type 和扩展名白名单三方对齐 |
| **敏感字段** | AppSecret、EncodingAESKey 使用 `EncryptedStringConverter`（AES-GCM）加密存储，不在日志、toast、错误消息中显示 |
| **速率限制** | `POST /api/wechat` 公开端点单独配置高限流（按 IP 每分钟 500 次）防刷，高于默认的 300 次 |
| **跨层引用约束** | Application 层禁止 `using NoneadCSS.Infrastructure.Services.WeChat`；昵称提取逻辑必须内联，避免污染分层 |

### 24.8 调试与联调 Checklist

本地开发调试微信回调（回调要求公网 HTTPS）：
1. 用 `ngrok`、`frp`、`cloudflared tunnel` 等内网穿透工具把本地 5009 端口暴露到公网 HTTPS
2. 把穿透后的 `https://{abc}.ngrok.io` 作为域名填入微信公众平台回调 URL
3. 如果有 CSRF 中间件，记得在 WeChatController 上显式关闭回调端点的 CSRF（微信服务器不可能携带 CSRF Cookie）
4. 用 Serilog 的 `RequestLogging` 中间件把 `/api/wechat` 请求体完整写入日志（生产环境仅记录摘要）

### 24.9 常见错误码快速参考

| 微信 errcode | 含义 | 自动处理机制 |
|-------------|------|-------------|
| 0 | 成功 | — |
| 40001 | invalid credential（Token 失效） | 强制刷新 Token + 重发 1 次 |
| 40014 | invalid access_token | 同上 |
| 42001 | access_token 超时 | 同上 |
| 43004 | require subscribe（客户未关注） | 提示客户需先关注公众号，或改用模板消息 |
| 45015 | response out of time（48h 窗口过期） | 前端 + 后端双重拦截；转模板消息引导；写告警日志 |
| 40003 / 40004 | 无效 openid / 无效 media_id | 写 error 日志；下次客户再互动时重新拉档案 / 下次回复重新上传素材 |
| 40007 / 40009 | 无效 media_id (图片) / 媒体文件大小超过限制 | 再次校验 2MB/10MB 限制；提示用户压缩后再上传 |
| 45047 | media_id 过期（3 天） | 下次回复时再次上传素材 |

---

## 25. 前端测试基础设施（Vitest + Testing Library）

### 25.1 技术栈选型

| 组件 | 选型 | 说明 |
|------|------|------|
| 测试运行器 | Vitest | Vite 原生集成，启动快，HMR 支持 |
| DOM 模拟 | happy-dom | 轻量 DOM 实现，比 jsdom 更快启动 |
| 组件测试 | @testing-library/react | 以用户行为为中心，避免测试实现细节 |
| 匹配器扩展 | @testing-library/jest-dom | 提供 `toBeInTheDocument()` 等语义化匹配器 |
| 覆盖率 | `@vitest/coverage-v8` | V8 原生覆盖率采集 |

### 25.2 项目结构

```
frontend/
├── vitest.config.ts              # Vitest 配置（引用 vite.config.ts 别名）
├── src/
│   ├── test/
│   │   └── setup.ts              # 全局 setup：DOM matchers + mock localStorage
│   ├── utils/
│   │   └── __tests__/            # 工具函数测试目录
│   ├── components/
│   │   └── **/__tests__/         # 组件测试目录
│   └── pages/
│       └── **/__tests__/         # 页面测试目录
```

### 25.3 运行命令

```bash
cd frontend

# 运行全部测试（单次）
npm run test

# watch 模式（开发时）
npm run test:watch

# 生成覆盖率报告
npm run test:coverage
```

### 25.4 vitest.config.ts 配置示例

```ts
import { defineConfig } from 'vitest/config';
import react from '@vitejs/plugin-react';
import path from 'node:path';

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
    },
  },
  test: {
    environment: 'happy-dom',
    globals: true,                // 避免每个文件都 import describe/it/expect
    setupFiles: ['./src/test/setup.ts'],
    css: false,                   // 跳过 CSS 解析，提升速度
    coverage: {
      provider: 'v8',
      reporter: ['text', 'html', 'cobertura'],
    },
  },
});
```

### 25.5 测试约定

- **工具函数测试**：纯函数优先覆盖，如 `formatDateTimeSafe`、`getApiErrorMessage`
- **组件测试**：从用户交互视角测试（render → fireEvent → assert），避免直接操作 state/ref
- **mock 约定**：
  - API 调用 → mock `@/api/client` axios 实例
  - React Query → 用 `QueryClientProvider` 包装 + `QueryClient({ defaultOptions: { queries: { retry: false } } })`
  - Router → 用 `<MemoryRouter>` 包装
- **文件命名**：`{Name}.{test|spec}.{ts|tsx}`，放在被测文件同级 `__tests__/` 目录下

---

## 26. IOptions<T> 配置化设计规范

### 26.1 适用场景

对于所有硬编码的业务 TimeSpan 字面量（约 45 处，跨 Api/Application/Infrastructure 三层），使用 `IOptions<T>` 注入替代硬编码，实现：
- 支持 appsettings.json + 环境变量 + 命令行 三层覆盖
- 无需重新编译即可调整策略阈值
- 按子域拆分为独立 Options 类，避免单一配置节膨胀

### 26.2 建议拆分的配置节（按子域）

| Options 类 | 配置节路径 | 典型配置项 |
|-----------|-----------|-----------|
| `ChatOptions` | `Performance:Chat` | 撤回时间窗（2min）、输入状态节流（2s）、会话自动关闭超时 |
| `CacheOptions` | `Performance:Cache` | OutputCache 默认过期、IMemoryCache 滑动过期 |
| `OAuthOptions` | `Security:OAuth` | Outlook OAuth state 有效期、授权码过期时间 |
| `IngestionMatchOptions` | `Performance:Ingestion` | 邮件同一会话匹配窗口、微信 48h 窗口、WhatsApp 24h 窗口 |
| `WebhookOptions` | `Integration:Webhook` | HTTP 超时、重试次数、指数退避基数 |
| `SignalROptions` | `Realtime:SignalR` | ConnectionTimeout、KeepAliveInterval、HandshakeTimeout |
| `RetryOptions` | `Resilience:Retry` | 微信/WhatsApp/邮件外部 API 调用 Polly 重试策略 |

### 26.3 Options 类定义约定

```csharp
// NoneadCSS.Application/Configuration/ChatOptions.cs
public class ChatOptions
{
    /// <summary>在线聊天消息可撤回时间窗（默认 2 分钟）</summary>
    public TimeSpan RecallWindow { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>输入状态指示节流阈值（默认 2 秒）</summary>
    public TimeSpan TypingThrottle { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>客户无互动自动关闭会话超时</summary>
    public TimeSpan AutoCloseTimeout { get; set; } = TimeSpan.FromDays(7);
}
```

### 26.4 注册与注入

```csharp
// Program.cs 注册
services.Configure<ChatOptions>(configuration.GetSection("Performance:Chat"));
services.Configure<CacheOptions>(configuration.GetSection("Performance:Cache"));
// ... 其他 Options 批量注册

// 服务构造函数注入
public class ChatService(IOptions<ChatOptions> chatOptions)
{
    private readonly ChatOptions _options = chatOptions.Value; // .Value 是单例缓存
}
```

### 26.5 appsettings.json 配置示例

```json
{
  "Performance": {
    "Chat": {
      "RecallWindow": "00:02:00",
      "TypingThrottle": "00:00:02",
      "AutoCloseTimeout": "7.00:00:00"
    },
    "Cache": {
      "DefaultOutputCacheExpire": "00:05:00"
    }
  }
}
```

---

## 27. EF Core AsNoTracking 补齐约定

### 27.1 强制规则

仓储类中 **所有** 只读查询方法（返回 List/DTO/单个实体且**不会**被调用方修改属性）必须在 `IQueryable` 链最早的位置调用 `.AsNoTracking()`：

```csharp
// ✅ 正确：AsNoTracking 在 Where 之前
public async Task<List<Workflow>> GetActiveByMailboxAsync(int mailboxId, string triggerType)
{
    return await _dbSet.AsNoTracking()
        .Where(w => w.DeletedAt == null && w.IsActive && w.TriggerType == triggerType)
        .OrderBy(w => w.SortOrder)
        .ThenBy(w => w.Id)
        .ToListAsync();
}

// ❌ 错误：遗漏 AsNoTracking，ChangeTracker 会追踪所有返回实体
// ❌ 错误：AsNoTracking 放在 ToListAsync() 之前（虽不报错但不如提前应用好）
```

### 27.2 明确的例外（不使用 AsNoTracking 的场景）

以下查询 **必须** 使用追踪查询（不调用 AsNoTracking）：
1. `GetByIdForUpdateAsync` / `GetXXXForUpdate`：返回的实体会被调用方修改属性后 SaveChanges
2. `GetDeletedTrackedByIdAsync`：恢复软删除时需要设置 `DeletedAt = null`
3. 任何调用方会执行 `entity.SomeProp = newValue` 后依赖 ChangeTracker 自动检测变化的场景

### 27.3 已完成补齐的仓储（37 个核心）

- WorkflowRepository（8 个方法）、SlaRepository（12 个）、NotificationRuleRepository（6 个）
- ChatSessionRepository、ChatMessageRepository（在线聊天高频场景）
- CustomerRepository、ThreadRepository、ConversationRepository 等核心仓储
- **验证方法**：grep 仓储方法体中 `_dbSet` / `GetQueryable` 后的调用链，确认只读路径有 `.AsNoTracking()`

---

## 28. 前端错误处理类型守卫规范

### 28.1 背景与问题

catch 块变量的默认类型在 TypeScript `useUnknownInCatchVariables: true` 下为 `unknown`。历史代码中大量 `catch (e: any)` 配合 `e.message` 直接访问，当抛的不是 Error 对象（如字符串、null、裸对象）时会运行时 TypeError。

### 28.2 强制约定

1. **catch 变量不加类型注解**：让 TypeScript 自动推断为 `unknown`
2. **用统一工具函数提取错误信息**：`getApiErrorMessage(e, fallback)`，内部做类型守卫

```typescript
// src/utils/error.ts
export function getApiErrorMessage(
  err: unknown,
  fallback = '操作失败，请稍后重试'
): string {
  // 1. Axios 错误格式：{ response: { data: { message: 'xxx' } } }
  if (
    typeof err === 'object' && err !== null &&
    'response' in err &&
    typeof (err as any).response?.data?.message === 'string'
  ) {
    return (err as any).response.data.message;
  }
  // 2. 原生 Error
  if (err instanceof Error && err.message) {
    return err.message;
  }
  // 3. 带有 message 字符串字段的裸对象
  if (
    typeof err === 'object' && err !== null &&
    'message' in err &&
    typeof (err as { message?: unknown }).message === 'string'
  ) {
    return (err as { message: string }).message;
  }
  // 4. 字符串字面量
  if (typeof err === 'string' && err.trim()) {
    return err;
  }
  return fallback;
}
```

### 28.3 使用示例

```typescript
// ✅ 正确
try {
  await updateProfile(data);
} catch (e) {
  toast.error(getApiErrorMessage(e, '个人资料更新失败'));
}

// ❌ 错误：any + 直接访问
catch (e: any) {
  toast.error(e.message); // 如果 e 是字符串 → TypeError
}
```

---

## 29. 钉钉 / 飞书集成模块开发约定

### 29.1 模块注册通用流程

所有新渠道接入（钉钉、飞书等）需遵循以下 5 步，确保模块管理联动生效：

1. **数据库迁移 + 种子数据**：在 `DatabaseSeeder.cs` 中插入 Modules 表记录（name=渠道英文标识，category=渠道集成，默认 IsActive=0）
2. **权限注册**：在 `PermissionsSeed` 中插入 `{channel}.view` / `{channel}.manage` 权限；supervisor 和 admin 角色默认授予 view 权限，admin 授予 manage
3. **前端路由**：`routes/index.tsx` 添加 `/{channel}` 路由，`ModuleProtectedRoute` 包裹以校验模块启用状态
4. **侧边栏菜单**：`moduleMenuMap.ts` 添加模块标识 → 菜单项的映射；`Layout.tsx` 用 `isModuleActive(name)` 判断显示
5. **会话来源标签**：`Conversations.tsx` 快捷来源标签中添加对应渠道的 `isModuleActive` 守卫

### 29.2 钉钉集成模块特有配置

| 字段 | 说明 |
|------|------|
| AppKey / AppSecret | 钉钉开放平台应用凭证，AES-GCM 加密存储 |
| AgentId | 企业内部应用 AgentId |
| RobotCode | 机器人编码（优先用于素材下载，为空时回退到 AppKey） |
| 接收模式 | HTTP Callback（回调推送，需填 CallbackToken + EncodingAESKey） / Stream（长连接流式推送，推荐） |
| CallbackToken / EncodingAESKey | HTTP 模式回调加解密参数，Stream 模式自动隐藏 |

前端 DingDing.tsx 配置页使用钉钉品牌蓝 `#1677ff` 渐变 + 闪电纹理装饰，图标 SVG 为独立 DingTalkLogo 组件，避免依赖外部图标库。敏感字段（AppSecret/Token/Key）右侧同排显示「🔒 已配置」状态徽章 + 锁图标。

### 29.2.1 钉钉开发环境域名/网络

- **hosts 绑定**：如通过自定义域名（如 `css.example.com:5173`）访问前端，需在本机 hosts 指向 127.0.0.1；Vite `allowedHosts: true` 放行任意 Host；CORS/CSRF 中间件白名单中配置允许 `css.example.com` 与指定公网 IP（如 203.0.113.10）
- **IPv6 网络不可达兜底**：`api.dingtalk.com` 在多数服务器默认优先解析 AAAA（IPv6）记录但无出口导致 30s 超时 → `DingDingClient` 的 `_httpClient` 自定义 `SocketsHttpHandler.ConnectCallback`，调用 `Dns.GetHostAddressesAsync` 过滤 IPv6，强制取 IPv4 地址
- **Stream 连接凭证**：启动时先 `POST robot/stream/registerConnection` 拿 clientId/endpoint（含 `wss://wss-open-connection.dingtalk.com:443/connect?token=xxx`），再建立 WebSocket；心跳 30s ping/pong，断线指数退避（1/2/4/8/16s，封顶 60s）
- **配置缓存模型**：`DingDingSettingsProvider` 注册为 Singleton，内部使用 `IMemoryCache` 而非 Scoped 实例字段存 `_cached`，否则每请求都拿 baseline 导致保存后 Stream 被跳回 Http；`SystemOptionsRepository.FindAsync` 必须用 tracked 查询，否则 AsNoTracking + 修改属性后 SaveChanges 不会生成 UPDATE

### 29.2.2 钉钉消息接收（双向同步）

- **DingDingIngestionService 统一入口**：Stream MessageDispatch 和 HTTP 回调 POST 都会进入 `ProcessMessageAsync` → 解析为 `DingDingIncomingMessage`（MsgType/DownloadCode/Content/SenderStaffId/SenderNick 等）→ 客户匹配 → 会话匹配（24h 窗口追加 or 新建）→ 事务持久化 → 工作流触发
- **表情转换**：钉钉发送 `text` 表情时内容为 `[流泪]` 这种中文方括号名称 → `DingDingEmojiConverter.Convert`（200+ 表情映射表，`\[([^\[\]]{1,10})\]` 正则）把它替换为对应 Unicode emoji，保证会话记录和客服侧显示真实表情
- **媒体接收（image/voice/video/file）**：message 体里拿到 `DownloadCode` 和 robotCode（为空回退 AppKey）→ `POST /v1.0/robot/messageFiles/download` 获取临时 `downloadUrl` → 再从 downloadUrl 下载实际文件 → 本地 `Data/Attachments/{mailboxId}/{guid}_{fileName}` 保存 → 创建 Attachment 记录（按 MimeType 推断 Type = Image/Voice/Video/File）→ 标记 thread.HasAttachments / conversation.HasAttachments = true

### 29.2.3 钉钉消息发送（客服回复 → 钉钉窗口）

- **两个独立发送端点 / 两条不同代码路径**：
  1. `POST /api/conversations/{id}/reply`（通用回复）→ `ConversationThreadService.AddReplyAsync` → `SendDingDingReplyAsync`（按 Conversation.Channel = DingTalk 分支）
  2. `POST /api/dingding/reply`（钉钉专用回复框）→ `DingDingService.ReplyAsync`
  → 二者都必须：创建 ConversationThread 后显式 `Attachments.GetByIdTrackedAsync(id).ThreadId = thread.Id` + SaveChanges，否则 Attachment.ThreadId 不会写回数据库，会话详情拿不到导航属性；正文发送与附件发送要分别统计，文本空时不发空文本
- **会话 Webhook（即时回复同一条消息链）**：收到消息时把 `sessionWebhook` 写入 `ConversationThread.MessageId`（前缀 `sw:` 避免冲突）→ 发送正文时先尝试 `SendBySessionWebhookAsync(sessionWebhook, "text", new { content = ... })`，失败回退 `BatchSendOtoMessageAsync`
- **附件 / 图片发送到钉钉窗口（最终正确格式）**：
  1. `UploadMediaAsync(type, fileBytes, fileName)`：老版 oapi `https://oapi.dingtalk.com/media/upload?type={type}&access_token=...` + `MultipartFormDataContent`，字段名 `media`；`InferMediaType` 按扩展名/MIME 精确映射 type = image/voice/video/file；**`type` 必填**，否则返回 errcode=40004（不合法的媒体文件类型）
  2. 拿到 media_id：形如 `@lALPM2AlYulscQvNAfTNAfQ`（**自带 @ 前缀，原样保留，不要自己再加/减 @**）
  3. `BatchSendOtoMessageAsync(robotCode, userIds, msgKey, msgParam)`：
     - sampleText：`msgParam = {"content":"你好"}`（扁平，不要嵌套 text 对象，否则钉钉显示乱码 JSON）
     - **sampleImageMsg（官方文档明确扁平）**：`msgParam = {"photoURL": "@media_id"}` （**扁平结构，不要嵌套 `{photo:{photoURL:...}}` 对象**，@ 前缀保留）
     - sampleFile：`msgParam = {"mediaId": "@media_id","fileName":"报价单.xlsx","fileType":"xlsx"}`（扁平，不要嵌套 file）
  4. 调试失败时务必在 BatchSend 里打印 HTTP 状态码 + 完整响应体：钉钉常出现「200 + 业务 errcode」「媒体不兼容（老/新版 media_id）」「@ 前缀有无」等多种静默失败场景

### 29.3 飞书集成模块特有配置

- 模块 Title 统一为「飞书集成」（中文），**不要**用英文 Title，否则前端中文搜索不到
- supervisor 角色需显式授予 `feishu.view` 权限（非 admin 角色不自动继承）
- 三层缓存（后端 OutputCache / IMemoryCache / 前端 ModuleContext）会导致启用后最多 5 分钟才生效，文档与代码注释中必须注明

### 29.3.1 飞书配置字段

| 字段 | 说明 |
|------|------|
| App ID / App Secret | 飞书开放平台应用凭证，AES-GCM 加密存储 |
| Verification Token | 事件订阅校验 Token（HTTP 模式必填，Stream 模式可空） |
| Encrypt Key | 事件内容 AES 加密密钥（可选，若飞书开启加密必填） |
| 关联邮箱 ID | 飞书会话归属的邮箱 |
| 自动回复消息 | 新会话欢迎语（可选） |
| 接收模式 | HTTP Callback（回调推送，需填 Verification Token + Encrypt Key） / Stream（长连接流式推送，推荐，无需公网 IP/域名） |
| 启用飞书接入 | 总开关，关闭后停止任何接入 |

### 29.3.2 飞书事件订阅与权限

**事件订阅**：
- `im.message.receive_v1`：消息接收（文本/图片/文件/富文本/交互卡片）
- `contact.user.updated_v3`：用户变更

**权限清单**：
- 消息与群组：`im:message`、`im:message:send_as_bot`、`im:chat`、`im:resource:upload`（发送图片/文件必需）
- 通讯录：`contact:user.base:readonly`

> ⚠️ 缺少 `im:resource:upload` 权限时，飞书 API 返回 `errcode=99991672`，图片上传失败。代码层会回退发送文本提示，但建议在飞书开放平台开通权限。

### 29.3.3 飞书消息发送（客服回复 → 飞书窗口）

- **`FeishuService.ReplyAsync`** 处理客服回复：
  1. 检测附件类型：图片走 base64+md5 方式（`SendImageAsync`），文件走 media upload + send（`SendFileAsync`）
  2. 文本消息通过 `SendTextAsync` 发送
  3. 上传失败时回退发送文本提示消息，告知用户因权限限制无法显示图片
- **鉴权**：`tenant_access_token` 通过 `IFeishuAccessTokenService` 获取与缓存（2 小时有效期，自动刷新）
- **安全**：事件签名/Token 强制校验（不允许空值绕过）；日志中 token 脱敏（仅打印前 8 位）

### 29.3.4 飞书配置缓存模型

- **`FeishuSettingsProvider`** 注册为 Singleton，内部使用 `IMemoryCache`
- **缓存 miss 时从数据库重新加载**：避免缓存过期后回退到 appsettings.json baseline（Enabled=false）导致启用开关自动关闭
- **`AutoReplyMessage` 更新守卫**：仅切换 enabled 状态时，添加 `if (!string.IsNullOrEmpty(...))` 守卫，避免 AutoReplyMessage 被意外清空
- **`FeishuOptions.StatusCheckTimeoutSeconds`**：状态检查超时时间配置化（默认 3 秒），避免 API 不可达时阻塞页面加载

---

## 30. 飞书 Stream 长连接开发指南

### 30.1 架构概述

飞书 Stream 长连接模式基于 WebSocket 的全双工通信协议，无需公网 IP/域名即可接收事件。包含 4 个核心组件：

| 组件 | 文件 | 职责 |
|------|------|------|
| `IFeishuStreamConnectionStatus` | `NoneadCSS.Infrastructure/Services/Feishu/IFeishuStreamConnectionStatus.cs` | 单例，跟踪连接状态（Idle/Connecting/Connected/Disconnected/Error） |
| `FeishuStreamProto` | `NoneadCSS.Infrastructure/Services/Feishu/FeishuStreamProto.cs` | Protobuf Frame 编解码（无需 protobuf 依赖） |
| `FeishuStreamClient` | `NoneadCSS.Infrastructure/Services/Feishu/FeishuStreamClient.cs` | WebSocket 长连接客户端 |
| `FeishuStreamHostedService` | `NoneadCSS.Infrastructure/Services/Feishu/FeishuStreamHostedService.cs` | `BackgroundService` 托管 |

### 30.2 Protobuf Frame 编解码

飞书 Stream 协议使用 protobuf 编码 Frame 结构：

```protobuf
message Frame {
  FrameType frame_type = 1;  // Control/Data
  uint32 seq_id = 2;
  string log_id = 3;
  string service = 4;
  string method = 5;
  map<string, string> headers = 6;
  bytes payload = 7;
}
```

`FeishuStreamProto.cs` 手动实现编解码（无需引入 protobuf 依赖），支持：
- **控制帧**（FrameType.Control）：心跳 ping/pong
- **数据帧**（FrameType.Data）：事件传输，收到后回送 ACK 帧

### 30.3 连接建立流程

1. POST `https://open.feishu.cn/open-apis/event/v1/outbound_ip` 获取 WebSocket URL
2. 建立 wss 连接
3. 发送初始握手帧
4. 启动心跳循环（30s ping/pong）
5. 接收数据帧 → 解码 → 通过 `IFeishuEventDispatcher` 分发 → 回送 ACK

### 30.4 重连机制

- 断线指数退避重连：1s/2s/4s/8s/16s，封顶 60s
- 首次重连随机抖动（避免雪崩）
- 重连时重置心跳定时器和接收循环

### 30.5 托管服务

`FeishuStreamHostedService` 继承 `BackgroundService`：
- 启动时检查 `FeishuOptions.ReceiveMode == "Stream"`，是则建立长连接
- 配置变更感知：轮询 `IMemoryCache` 中的最新配置，若 ReceiveMode 切换为 HTTP 则断开连接
- `Dispose` 中直接 `WebSocket.Dispose()`，**禁止** sync-over-async（`.GetAwaiter().GetResult()` 会阻塞线程池）

### 30.6 Stream 模式与 HTTP 模式对比

| 维度 | HTTP 回调模式 | Stream 长连接模式（推荐） |
|------|---------------|---------------------------|
| 公网 IP/域名 | 必需 | 不需要 |
| 事件推送 | 飞书主动 POST | WebSocket 推送 |
| 鉴权 | Verification Token + Encrypt Key | 连接凭证（自动获取） |
| 重试机制 | 飞书自动重试 | 客户端 ACK + 重连 |
| 适用场景 | 生产环境（有公网） | 开发/测试/内网环境 |

---

## 31. 2026-08-05 全面代码审计与修复

### 31.1 审计概述

- **审计日期**：2026-08-05
- **审计范围**：代码质量、安全、功能完整性、性能、测试 5 个维度
- **审计结果**：发现 38 个问题（2 高、16 中、20 低），修复 31 个，跳过 7 个高风险问题
- **测试基线**：895 项测试用例，891 通过，0 失败，4 跳过

### 31.2 高严重等级修复

| 文件 | 问题 | 修复方式 |
|------|------|----------|
| `ChatService.cs` | `RecallMessageAsync` / `EditMessageAsync` 使用 AsNoTracking 查询后调用 `Update()` 导致全字段 UPDATE | 改用 `GetByIdTrackedAsync` 并删除冗余 `Update()` 调用 |
| WhatsApp Webhook | 签名验证在 AppSecret 为空时被跳过 | 改为强制校验，未配置时返回 Unauthorized |

### 31.3 中严重等级修复（节选）

| 文件 | 问题 | 修复方式 |
|------|------|----------|
| `FeishuSettingsProvider.cs` | sync-over-async 同步阻塞线程池 | 添加后台异步刷新 + 启动预热机制 |
| `CustomReportService.cs` | 全表加载邮箱 | 改为 `FirstOrDefaultAsync` |
| `DashboardService.cs` | 活跃邮箱无限制 | 添加 `.Take(100)` |
| `ImportExportService.cs` | 全表导出 OOM | 添加 AsNoTracking |
| `UserService.cs` | 头像 URL 未校验 | 调用 `ValidateAndNormalizeAvatarUrl` |
| `SatisfactionTokenHelper.cs` | 时序攻击风险 | 改用 `FixedTimeEquals` |
| `DingDingOptions.cs` / `FeishuOptions.cs` | 超时硬编码 | `StatusCheckTimeoutSeconds` 配置化 |
| `KnowledgeArticleRepository.cs` | 只读查询未 AsNoTracking | 添加 AsNoTracking |
| `DingDingIngestionService.cs` | 消息去重逻辑失效 | 修复查询条件，同时匹配 `MsgId` 和 `"sw:" + SessionWebhook` |
| `DashboardService.cs` / `ReportService.cs` / `Repository.cs` | EF Core `Skip/Take without OrderBy` 警告 | 添加 OrderBy |

### 31.4 低严重等级修复（节选）

- **前端 6 处 useEffect/setTimeout cleanup**：`Knowledge.tsx` 等组件添加 cancelled 守卫和 cleanup 函数
- **前端 3 处冗余类型断言**：`Feishu.tsx` / `DingDing.tsx` / `WhatsApp.tsx` 移除 `as unknown as`
- **`Mailboxes.tsx` 局部 fetch 遮蔽**：重命名为 `fetchMailboxes`
- **`AuditLogs.tsx` 死代码**：清理
- **`WeChatClient.cs` 硬编码代理 URL**：配置化
- **测试修复**：`ConversationTagServiceTests` 断言修正；`WhatsAppServiceTests` Mock Setup 调整
- **ChatServiceTests Mock 同步**：5 个 RecallMessage/EditMessage 测试用例 Mock setup `GetByIdAsync`，但 ChatService 已改用 `GetByIdTrackedAsync`，导致 Mock 未命中返回 null 抛出"消息不存在"。修复方式：将 5 处 `_mockMessages.Setup(m => m.GetByIdAsync(201))` 改为 `_mockMessages.Setup(m => m.GetByIdTrackedAsync(201))`，覆盖以下用例：
  - `RecallMessageAsync_Should_Succeed_ForOwnRecentAgentMessage`
  - `RecallMessageAsync_Should_Throw_When_NotSender`
  - `RecallMessageAsync_Should_Throw_When_OlderThan2Minutes`
  - `EditMessageAsync_Should_UpdateContent_And_SetEditedAt`
  - `EditMessageAsync_Should_Throw_When_ContentEmpty`

### 31.5 安全审计补充确认（无需修复）

- 钉钉/飞书事件签名/Token 校验已强制执行
- WhatsApp Webhook 签名验证已强制执行
- 钉钉消息去重逻辑已修复
- `FeishuAccessTokenService` 日志已脱敏 token
- Stream Client Dispose 已移除 sync-over-async

### 31.6 验证结果（2026-08-05 审计基线）

- **后端编译**：`dotnet build NoneadCSS.Api/NoneadCSS.Api.csproj` — 0 错误（5 个既有可空警告）
- **后端测试**：`dotnet test NoneadCSS.Tests/NoneadCSS.Tests.csproj` — 总计 895 项，891 通过 / 0 失败 / 4 跳过
- **前端 TypeScript 检查**：`cd frontend && npx tsc --noEmit` — 0 错误
- **前端构建**：`cd frontend && npx vite build` — 成功

> 2026-08-06 基线升级后数据见 [33.4 验证结果（测试基线 895 → 953）](#334-验证结果测试基线-895--953)

---

## 32. 技术债务管理

### 32.1 技术债务清单

7 项高风险技术债务详见 [NoneadCSS_TechnicalDebt_2026Q4.md](./NoneadCSS_TechnicalDebt_2026Q4.md)，建议在 2026 Q4 专项迭代中处理：

| 编号 | 问题 | 优先级 | 工作量 | 建议排期 |
|------|------|--------|--------|----------|
| TD-01 | 仓储层事务嵌套缺陷（UnitOfWork 不支持嵌套） | P0 | 3-5 天 | 第 1-2 周 |
| TD-02 | 仓储层普遍调用 SaveChangesAsync（30+ 处违规） | P1 | 1-2 周 | 第 3-5 周（TD-01 完成后） |
| TD-03 | ReportService 报表全量内存化（含 SQLite 999 参数上限 Bug） | P0 | 1 周 | 第 1-2 周 |
| TD-04 | ConversationService 批量操作工作流触发（250+ DB 往返） | P1 | 1-2 周 | 第 4-6 周 |
| TD-05 | UserRoleResolver 权限解析完全无缓存（每页面数十次 DB 往返） | P0 | 3-5 天 | 第 1 周 |
| TD-06 | ConversationThread.MessageId 缺唯一约束（TOCTOU 竞态） | P0 | 3-5 天 | 第 2-3 周（需数据清理窗口） |
| TD-07 | 高并发表缺乐观锁 + 全部 Controller 无 API 版本控制 | P1 | 1-2 周 | 第 5-8 周 |

### 32.2 关键路径与并行性

- **关键路径**：TD-01 → TD-02（事务架构稳定后再做仓储层重构）
- **可并行**：TD-03 / TD-05 / TD-06 互不依赖，可分配给不同开发人员
- **风险最高**：TD-06（涉及生产数据清理，建议在低峰期执行）
- **回归测试重点**：TD-01（宏执行）、TD-02（所有调用仓储的 Service）、TD-06（所有 IngestionService）

### 32.3 验收标准

每项修复完成后需通过：
1. 后端 Release 编译 0 错误（允许既有可空警告）
2. 单元测试全部通过（当前基线：2026-08-06 升级为 **953 项测试用例，949 通过，0 失败，4 跳过**；历史 8/5 基线：895 / 891 / 0 / 4）
3. 前端 TypeScript 类型检查 0 错误
4. 前端 Vite 生产构建成功
5. 新增的测试用例覆盖修复点

---

## 33. 2026-08-06 审计后落地修复（Post-TD 清理）

> 本章节问题均在 2026-08-05 审计报告定稿后通过实际使用反馈（局域网 IP 访问 #63/#64/#65 编辑按钮失效、关闭飞书/微信聊天会话观察满意度闭环）发现并于当日完成修复，不列入 8/5 原 31 项修复与 7 项 TD。

### 33.1 高严重度问题修复（3 项功能/兼容性）

| 编号 | 问题 | 触发条件 | 根因 | 修复方案 | 影响范围 |
|------|------|---------|------|---------|---------|
| Post-01 | 四渠道联系方式提取 → `Conversation.CustomerEmail` 不回填 → 会话关闭无邮件 | 客户在飞书/WhatsApp/微信/钉钉对话中发送邮箱后关闭会话 | `TryExtractContactInfoFromMessageAsync` 仅写 `Customer.Emails` 集合，未同步写会话级 `CustomerEmail`，`send_email` 动作 `ResolveRecipientsAsync` 返回空收件人并静默跳过 | 调用点传 `conversation.Id` → 方法签名新增 `conversationId` → 提取到邮箱后用 `GetByIdForUpdateAsync` 查空值回填；4 渠道统一模式，空时才写，已有邮箱不覆盖 | 4 IngestionService（Feishu/WhatsApp/WeChat/DingDing）各 3 处 = 12 处代码 |
| Post-02 | 聊天渠道无邮箱客户 → 会话关闭满意度调查 100% 丢失（样本偏差） | 80% 聊天渠道客户没有真实邮箱 | 满意度调查仅邮件触达一条路径；无邮箱 → 直接跳过 → 服务质量 CSAT 数据不可信 | 双向闭环：① `SatisfactionTokenHelper` 新增 ChannelId 双路径签名验证（`channel:{channelId}` 前缀隔离域）；② `SendEmailActionHandler` 无收件人时，经统一 `IChatChannelMessageSender` 将模板渲染后的纯文本 + `satisfaction_url` 回推到聊天 App 单聊窗口；③ 关闭通知 + 满意度感谢通知双模板均回退 | SatisfactionTokenHelper / SendEmailActionHandler / 4 渠道 Client |
| Post-03 | HTTP 局域网 IP 访问 → 客户 Phones/Websites 非空时点「编辑」按钮无反应（P0 体验） | `http://192.168.x.x:5173` 非安全上下文访问 + 客户 enrich 后 phones 非空 | `crypto.randomUUID()` Secure Context-only API 非 HTTPS/localhost 抛 TypeError；React Error Boundary 不捕获事件处理器异常 → 静默失败；空数组 `[].map(() => randomUUID())` 回空不调用 → 未 enrich 的客户反而正常，迷惑性极强 | ① 新增 `utils/id.ts` → `genId()`：三层防护 `typeof crypto` → `typeof crypto.randomUUID` → try-catch 包裹，Fallback 为 `Date.now().toString(36)` + `Math.random` 11 位；② 全量替换 14 处调用点（客户编辑 4 + 自定义字段 2 + 宏编辑器 4 + 工作流 4） | 5 前端文件，14 调用点 |

### 33.2 测试债务修复（4 项）

| 类别 | 问题 | 根因 | 修复方式 |
|------|------|------|---------|
| 编译（3） | WorkflowServiceTests / FolderServiceIntegrationTests / NotificationServiceTests 报 UnitOfWork 构造参数缺失 | 生产 `UnitOfWork` 新增 `IMemoryCache` 参数（SystemOptions 读取缓存），Tests 未同步 | 测试构造调用传 `new MemoryCache(new MemoryCacheOptions())`，补 `using Microsoft.Extensions.Caching.Memory` |
| 运行时（1） | `DbQueryTest.QueryWeChatToken` SQLite 抛异常后终止测试进程 | 修改 `cmd.CommandText` 前未 `reader.Close()`，SQLite 不允许 DataReader 开着改命令 | 切换 SQL 前显式 `reader.Close()` |

### 33.3 约定新增：前端浏览器 API 兼容性必做检查

由 Post-03 总结的开发约定补充：

1. 所有调用 `crypto.*` / `window.crypto.subtle` / `navigator.clipboard` 等 Secure Context-only API 的代码，**必须**先做 `typeof + try-catch` 双重检查，禁止裸调用
2. React 事件处理器（onClick/onChange/onSubmit 等）中的**关键路径**（打开弹窗、保存编辑、路由跳转）建议用 [边界组件包裹的 try-catch](https://react.dev/reference/react/Component#catching-rendering-errors-with-an-error-boundary) + `console.error` 可见性日志，防止用户感知为"按钮坏了没反应"
3. 新功能提交前必须完成 3 种环境冒烟：`https://...`（生产模拟）、`http://localhost:5173`（开发默认）、`http://<局域网 IP>:5173`（非安全上下文）

### 33.4 验证结果（测试基线 895 → 953）

```
dotnet build NoneadCSS.Api                → 0 警告 0 错误
dotnet test NoneadCSS.Tests               → 949 通过 / 0 失败 / 4 跳过（总 953，净增 54 项）
客户编辑按钮（HTTP 局域网 IP）            → 弹窗正常打开、保存成功 ✅
客户编辑按钮（HTTPS/localhost）            → 保持不变 ✅
飞书/WhatsApp：对话中提供邮箱后关闭会话     → CustomerEmail 回填 ✅ 邮件送达 ✅
飞书/微信：客户无邮箱关闭会话               → 聊天 App 单聊窗口收到满意度链接 ✅
满意度 Token：email & ChannelId 双路径      → 都能通过 FixedTimeEquals 验证 ✅
```

---

## 34. 2026-08-08 部署与运行稳定性修复

> 本章节问题均在 2026-08-08 全面代码复审 + 部署演练中发现，涉及 DI 循环依赖、部署脚本误杀、微信 Token 刷新互斥锁泄漏三类运行稳定性隐患；修复后通过 `dotnet build` 0 警告 0 错误 + `dotnet test` 1010 用例（1006 通过 / 4 跳过 / 0 失败）。

### 34.1 问题总览与修复表

| 编号 | 严重等级 | 类别 | 问题 | 影响 | 修复文件 |
|------|---------|------|------|------|----------|
| Run-01 | P0 | 部署脚本 | `restart-dev.ps1` 与 `restart-prod.ps1` 使用 `Get-Process dotnet/node/nginx` 全局杀进程，会误杀用户机器上其他项目的同名进程（Visual Studio、Rider、其他 Node 项目、系统 nginx） | 用户运维一次，整台机器相关开发环境全部被杀 | `deploy/restart-dev.ps1`、`C:\noneadcss_prod\restart-prod.ps1` |
| Run-02 | P0 | 部署脚本 | 生产 `restart-prod.ps1` 缺少 Fail-Fast 密钥校验，JWT/Encryption/Audit/SatisfactionTokenKey 任一缺失时 dotnet 启动要 10 秒才抛异常，脚本再等 40 秒才报"未就绪"，难以秒级定位 | 排错成本 > 50 秒；日志信息笼统 | `C:\noneadcss_prod\restart-prod.ps1` |
| Run-03 | P1 | 微信集成 | `WeChatAccessTokenService.RefreshAccessTokenAsync` 的 `SemaphoreSlim.WaitAsync(ct)` 被取消（OperationCanceledException）或抛其他异常时，`finally` 仍无条件 `Release()`，会导致 `SemaphoreFullException` 或信号量计数错误 → 后续**所有** Token 刷新请求永远阻塞 | 客户反馈"首次正常、运行一段时间后微信全失效" | `NoneadCSS.Infrastructure/Services/WeChat/WeChatAccessTokenService.cs` |
| Run-04 | P1（早期） | DI 循环依赖 | `ICurrentUserAccessor ↔ IMailboxService` 双向构造期依赖 → 生产环境（启用 DI 验证）启动直接抛 `AggregateException`，开发环境（Scoped 延迟解析）表现为偶发首次调用死循环 | 生产无法启动；开发偶发 StackOverflow | `NoneadCSS.Application/Services/MailboxService.cs`（2026-08-07 已修，本次复审确认 OK） |

### 34.2 Run-01 修复：按端口绑定 Pid 精准杀进程

新增工具函数 `Get-PidsByPort($Port)`（基于 `netstat -ano` 抓取 LISTENING 状态最后一列 Pid）+ `Stop-ByPort($Port, $Label)`，只清理绑定目标端口的 Pid：
- 开发环境只扫 5008 / 5171
- 生产环境只扫 5009 / 5172 / 5173
- 对 Nginx 进程仍优先 `nginx -s stop` 优雅关闭，worker 退出完成后再兜底 `taskkill /F /PID`
- 清理后二次校验"端口是否已释放"，未释放时输出仍占用的 Pid 列表，方便定位僵尸进程

### 34.3 Run-02 修复：Fail-Fast 环境变量校验

生产 `restart-prod.ps1` 新增 `[0/4]` 步骤，启动后端前先强制检查 4 项必需变量：
- `JWT__Key`、`ENCRYPTION__Key`、`AUDIT__HmacKey`、`APP__SatisfactionTokenKey`

任一项缺失立即 `exit 1`，并在屏幕上直接输出可复制的 4 行 `[Environment]::SetEnvironmentVariable("...", "User")` 修复命令，用户粘贴即完成修复。同时把注入清单由原来 6 项扩充到 12 项，补全 `JWT__Issuer / JWT__Audience / APP__Url / WECHAT__AppId·AppSecret·Token·EncodingAESKey` 等配置。

### 34.4 Run-03 修复：Semaphore 锁泄漏（取消中断时）

**原代码**的反模式：

```csharp
await _refreshLock.WaitAsync(cancellationToken); // 若被取消 → 抛出
try { ... }
finally { _refreshLock.Release(); } // ← 取消时仍 Release → 计数错误
```

**修复后**：引入 `bool lockAcquired` 跟踪，只有 `WaitAsync` 正常返回才置 true 并在 finally 中 Release；对 `Release` 本身再捕获 `SemaphoreFullException` 记 Warning 兜底，防止单点异常影响所有刷新。

```csharp
bool lockAcquired = false;
try {
    await _refreshLock.WaitAsync(cancellationToken);
    lockAcquired = true;
    // 双检缓存 + 刷新请求
} finally {
    if (lockAcquired) {
        try { _refreshLock.Release(); }
        catch (SemaphoreFullException ex) {
            _logger.LogWarning(ex, "微信 Token 刷新互斥锁状态异常，已忽略，避免阻塞后续刷新。");
        }
    }
}
```

### 34.5 验证结果（测试基线 953 → 1010）

```
dotnet build NoneadCSS.Api               → 0 警告 0 错误
dotnet test  NoneadCSS.Tests              → 1006 通过 / 0 失败 / 4 跳过（总 1010，净增 57 项）
npx tsc --noEmit (frontend)               → 0 错误
restart-dev.ps1 按端口杀进程               → 仅 5008/5171 被清理，其他 dotnet/node 进程存活 ✅
restart-prod.ps1 Fail-Fast 校验           → 缺密钥秒级 exit 1 + 输出修复命令 ✅
WeChatAccessTokenService 并发取消/异常    → 无锁泄漏、后续刷新仍正常（单测通过）✅
```

---

## 35. 2026-08-10 微信通知消息与自定义菜单优化 + IM 客户信息收集 + 安全加固

### 35.1 微信公众号通知消息 Tab 优化

#### 35.1.1 会话搜索选择器（替代手动输入会话 ID）

原实现要求客服手输会话 ID 才能发送模板消息，体验差且易错。新增 `ConversationSearchInput` 组件：

```tsx
function ConversationSearchInput({
  value,
  onSelect,
  disabled,
}: {
  value: number | '';
  onSelect: (id: number | '', subject: string, customerName: string) => void;
  disabled?: boolean;
}) {
  // 输入关键词 → debounce 300ms → conversationApi.list({ search, sourceType: 7 })
  // 下拉显示：会话 ID + 客户名 + 最近消息预览
  // 单击选中 → onSelect 回调填入会话 ID
}
```

**实现要点**：
- 复用 `conversationApi.list` 接口，通过 `sourceType: 7`（WeChat）过滤仅微信会话
- `PagedResponse<ConversationResponse>` 返回 `data` 字段（非 `items`），需注意类型匹配
- debounce 300ms 避免频繁请求
- 下拉项显示客户 `fullName` + 会话 `subject` + `preview` 三段信息

#### 35.1.2 模板消息 + 订阅通知双模式切换

页面顶部模式切换按钮在两种微信触达 API 间独立选择：

| 模式 | API | 用途 |
|------|-----|------|
| 模板消息（旧版） | `message/template/send` | 服务号已申请模板消息权限 |
| 订阅通知（新版） | `cgi-bin/message/subscribe/send` | 微信推荐方向，客户主动订阅后一次性触达 |

两种模式互不干扰，按微信运营规则选择。

#### 35.1.3 常用模板预设

内置 3 个常用模板，一键填充模板 ID 和字段值：
- 工单受理通知
- 工单已解决通知
- 新回复提醒通知

#### 35.1.4 实时 JSON 校验 + 卡片预览

编辑模板字段值时：
- 左侧表单实时生成 JSON
- 右侧卡片预览渲染效果，支持 `color` 字段着色（按微信规范 `#RRGGBB`）
- JSON 格式错误时实时提示

#### 35.1.5 发送历史记录

记录最近 10 条发送历史：
- 成功/失败状态
- 发送时间
- 目标客户
- 模板名称
- 支持点击历史项快速重发

### 35.2 微信公众号自定义菜单 Tab 优化

#### 35.2.1 可视化编辑器（MenuButtonEditor 组件）

```tsx
function MenuButtonEditor({
  menu,
  onChange,
  isReadOnly,
}: {
  menu: WeChatMenuSchema | null;
  onChange: (menu: WeChatMenuSchema) => void;
  isReadOnly: boolean;
}) {
  // 表单式增删改菜单按钮
  // 一级菜单最多 3 个，二级菜单最多 5 个
  // 每个菜单项：name + type + 必填字段（key/url/media_id/article_id）
}
```

#### 35.2.2 JSON ↔ 可视化双向同步

页面右上角切换按钮在两种模式间切换：
- **可视化编辑**（默认）：表单式 UI，适合日常维护
- **JSON 编辑**：直接编辑 JSON，适合批量导入/导出

两种模式双向同步菜单结构，切换时数据不丢失。

#### 35.2.3 Schema 校验

按菜单 `type` 实时校验必填字段：

| 类型 | 必填字段 |
|------|----------|
| `click` / `scancode_*` / `pic_*` / `location_select` | `key` |
| `view` | `url` |
| `media_id` | `media_id` |
| `article_id` | `article_id` |
| `miniprogram` | `appid` / `pagepath` / `url` |

缺字段时输入框下方红色提示，发布前再次全量校验。

#### 35.2.4 手机预览

右侧模拟微信底部菜单栏渲染：
- 一级菜单横向排列
- hover 一级菜单展开二级菜单
- 直观查看最终在微信客户端的展示样式

#### 35.2.5 线上菜单操作

- **拉取线上菜单**：调用 `cgi-bin/getcurrentmenu`
- **加载到编辑器**：将拉取到的菜单一键加载到可视化编辑器
- **发布到线上**：校验通过后调用 `cgi-bin/menu/create`
- **删除线上菜单**：调用 `cgi-bin/menu/delete`

### 35.3 IM 渠道客户信息收集策略

四渠道（微信/钉钉/飞书/WhatsApp）统一实现客户信息收集引导消息策略：

#### 35.3.1 触发条件（三重预检查）

1. `ContactCollectionEnabled` 系统选项开关为 true
2. 消息文本内容存在（非空）
3. `ContactExtractAttempts < 3`（避免无限循环）

任一不满足不触发引导消息。

#### 35.3.2 信息提取与回填

- **全量提取**：从所有历史消息中提取姓名/手机/邮箱字段
- **覆盖更新**：提取到的字段覆盖已有值，保证最新信息
- **attempts 自增**：即便本轮无新字段也自增计数，避免无限循环

#### 35.3.3 内部员工识别

钉钉/飞书渠道通过 OpenAPI 自动补全 Name/Phone/Email：
- 三字段齐全 → 判定为内部员工 → 跳过引导消息
- 日志记录：`Customer information is complete (internal employee), skipping guidance`

#### 35.3.4 外部客户兜底

- **微信**：无法获取外部用户信息 → 正常触发引导
- **WhatsApp**：GDPR 限制无法获取 Email → 正常触发引导
- **飞书**：API 权限不足无法补全 → 正常触发引导

#### 35.3.5 系统消息持久化

引导消息作为系统消息写入 ConversationThread：

```csharp
var guidanceThread = new ConversationThread
{
    ConversationId = conversation.Id,
    Type = ThreadType.Message,
    SourceVia = MessageSource.User,
    SourceType = conversation.SourceType,
    From = "system", // system pseudonym
    Body = "为了更好地为您服务，方便后续发送相关资料，请您回复您的姓名，手机，邮箱地址，谢谢！",
    SendStatus = SendStatus.Sent,
    IsInternal = false,
};
// 显式注册到 EF Core tracker，避免 ClearChanges() 后丢失
await _unitOfWork.Threads.AddAsync(guidanceThread);
```

#### 35.3.6 EF Core 追踪修复

**问题**：`conversation.AddThread()` 添加到内存，但 `TriggerPostIngestionWorkflowsAsync` 调用 `ClearChanges()` 会 detach 实体，导致 `SaveChangesAsync()` 不持久化。

**修复**：在 `SaveChangesAsync()` 前显式 `_unitOfWork.Threads.AddAsync(guidanceThread)` 注册到 tracker。

### 35.4 安全与质量批量修复（58 项）

#### 35.4.1 事务与数据一致性

| 问题 | 修复方式 |
|------|----------|
| EmailQueueService 事务回滚持久化 | 修复事务回滚后队列状态未持久化 |
| Message-ID null ThreadId 处理 | EmailQueueService 处理 Message-ID 为空时 ThreadId 容错 |
| DingDingWebhookParser 内存泄漏 | 修复 Webhook 解析器内存泄漏 |
| JWT 密钥分离 | JWT SecretKey 与其他加密密钥分离 |
| CSRF 豁免钉钉回调 | DingDingController 回调端点豁免 CSRF（钉钉服务器 IP 不固定） |
| 微信时间戳重放保护 | WeChatController 校验 timestamp 与当前时间差 ≤ 5 分钟 |
| 飞书签名时序安全比较 | 改用 `CryptographicOperations.FixedTimeEquals` |
| 飞书 EncryptKey 校验 | 强制校验 EncryptKey 非空 |

#### 35.4.2 运行稳定性

| 问题 | 修复方式 |
|------|----------|
| 钉钉 Stream 重试延迟重置 | 重连成功后重置退避计数 |
| DingDingClient 信号量超时 | SemaphoreSlim 添加超时，避免永久阻塞 |
| Fire-and-forget 异常日志 | 所有 `Task.Run` 添加 try-catch + 日志 |
| SatisfactionRatingService `.Result` 阻塞 | 移除 `.Result`，改用 `await` |
| SPA 导航刷新修复 | 修复 SPA 路由切换后页面不刷新 |
| WorkflowService 连续失败告警隔离 | try-catch 隔离，避免影响主流程 |
| N+1 查询优化 | WorkflowService 批量查询执行日志 |
| 实体重载机制 | 工作流执行前显式 Reload 实体 |
| 软删除过滤补齐 | 多个仓储补齐 `DeletedAt IS NULL` 过滤 |
| WebhookService HttpClient 缓存 | HttpClient 复用，避免 socket 耗尽 |

### 35.5 生产环境 SQLite 兼容性修复

#### 35.5.1 ConversationRepository.ApplyFullIncludes SQLite APPLY 不支持

**现象**：访问会话管理页面返回 HTTP 409。

**根因**：`Include` 子句中的 `OrderByDescending + Take` 翻译为 SQL `APPLY` 操作，SQLite 不支持。

**修复**：移除 `Include` 中的 `OrderByDescending + Take`，仅保留 `Where` 过滤。

```csharp
// 修复前（SQLite 不支持 APPLY）
.Include(c => c.Threads.Where(t => t.DeletedAt == null))
    .ThenInclude(t => t.Attachments.OrderByDescending(a => a.CreatedAt).Take(10))

// 修复后（仅保留 Where 过滤，SQLite 兼容）
.Include(c => c.Threads.Where(t => t.DeletedAt == null))
    .ThenInclude(t => t.Attachments)
// 注：SQLite 不支持 Include 内的 OrderByDescending + Take（翻译为 APPLY），需在业务层排序分页
```

#### 35.5.2 WorkflowService MailboxUsers FK fixup 误标记 Added

**现象**：微信工作流触发失败，日志报 UNIQUE 约束冲突（MailboxUsers）。

**根因**：EF Core fixup 机制在将 AsNoTracking Mailbox（含 MailboxUsers）赋值到 tracked conversation 的 Mailbox 导航属性时，误将 MailboxUsers 标记为 Added。

**修复**：
1. WorkflowService 查询 Mailbox 时不 Include MailboxUsers（改用泛型仓储）
2. 五个渠道 IngestionService 的 `TriggerPostIngestionWorkflowsAsync` catch 块添加 `_unitOfWork.ClearChanges()`

### 35.6 验证结果（测试基线 1010 → 1033）

```
dotnet build NoneadCSS.Api               → 0 警告 0 错误
dotnet test  NoneadCSS.Tests              → 1028 通过 / 0 失败 / 5 跳过（总 1033，净增 23 项）
npx tsc --noEmit (frontend)               → 0 错误
npm run build (frontend)                  → 成功，WeChat chunk 86.62 kB（gzip 17.66 kB）
生产部署                                   → nginx 5173 HTTP 200，WeChat chunk 哈希一致 ✅
三渠道回调模式验证                          → 钉钉/飞书/微信签名验证、加解密、事件路由全部通过 ✅
```

---

## 36. 2026-08-10 生产环境独立部署架构 + 启动脚本 + Nginx 优化

> 本章节记录 1.8.1 版本的生产环境部署架构重构，包含独立部署目录、一键启停脚本、Nginx 配置优化、后端 content root 修复四项关键改进。

### 36.1 独立生产部署目录

**背景**：原生产部署目录 `C:\noneadcss_prod` 与源码仓库无关联管理，缺乏标准化结构。1.8.1 版本重构为 `C:\noneadcss\Production`，置于源码仓库下但通过 `.gitignore` 排除版本控制。

**目录结构**：

```
C:\noneadcss\Production\
├── backend/              # dotnet publish 输出（NoneadCSS.Api.exe + 依赖 DLL + appsettings.json）
├── wwwroot/              # npm run build 输出（前端 SPA 静态文件）
│   ├── assets/           # Vite 打包的 JS/CSS（带 hash 文件名）
│   ├── index.html
│   └── favicon.svg
├── data/                 # SQLite 数据库 + 附件存储
│   └── noneadcss.db
├── logs/                 # 运行日志
│   ├── backend-stdout.log
│   ├── backend-stderr.log
│   └── nginx-error.log
├── cache/                # Nginx 缓存目录
│   ├── proxy/
│   ├── client_body/
│   ├── fastcgi/
│   ├── uwsgi/
│   └── scgi/
├── nginx.conf            # Nginx 配置（含反向代理 + SPA 静态托管）
├── start-prod.ps1        # 一键启动脚本
├── stop-prod.ps1         # 一键停止脚本
├── _launch-backend.cmd   # 后端启动 CMD（任务计划程序用）
└── _start-backend.ps1    # 后端启动 PS1（备用）
```

**.gitignore 排除规则**：

```
# === 生产环境部署目录（独立于源码，含运行时数据与密钥配置） ===
Production/
```

### 36.2 后端 content root 修复

**问题**：后端启动后静默退出，无任何错误日志，进程退出码为 0。

**根因分析**：
- 启动脚本将工作目录设为 `Production/`（用于 SQLite 相对路径 `data/noneadcss.db` 解析）
- 但 `appsettings.json` 位于 `Production/backend/` 子目录
- .NET 配置系统默认从 content root（=工作目录）查找 `appsettings.json`
- 找不到配置文件 → Kestrel 无法读取 `Urls` 配置 → 不绑定端口 → 进程正常退出

**修复方案**：通过环境变量 `ASPNETCORE_CONTENTROOT` 显式指定 content root：

```powershell
$env:ASPNETCORE_CONTENTROOT = 'C:\noneadcss\Production\backend'
```

**关键点**：
- `ASPNETCORE_CONTENTROOT` 控制 .NET 配置系统查找 `appsettings.json` 的根目录
- 工作目录（`-WorkingDirectory`）控制相对路径（如 SQLite 数据库路径）的解析基准
- 两者可以分离：content root 指向 backend/（配置文件所在），工作目录指向 Production/（数据文件所在）

### 36.3 一键启停脚本

#### 36.3.1 start-prod.ps1

**路径**：`C:\noneadcss\Production\start-prod.ps1`

**执行流程**：

```
[START] Checking for existing processes...
  → 检测 :5009 / :5173 端口占用，停止已有服务（幂等）
[START] Starting backend service...
  → 设置 7 项环境变量（ASPNETCORE_ENVIRONMENT / CONTENTROOT / Database / ConnectionStrings / JWT / SECRET_HASHER / ENCRYPTION）
  → Start-Process 启动 NoneadCSS.Api.exe（隐藏窗口，重定向日志）
  → 轮询 60 秒等待 :5009 端口监听
  → HTTP GET /health 验证后端健康
[START] Starting Nginx service...
  → nginx -t 预检配置语法
  → Start-Process 启动 nginx（隐藏窗口）
  → 轮询 10 秒等待 :5173 端口监听
  → HTTP GET / 验证前端可访问
  → HTTP GET /health 验证 API 代理正常
[OK] 输出三端就绪摘要
```

**特性**：
- **幂等性**：重复执行不会产生冲突，先清理旧进程再启动
- **Fail-Fast**：后端 60 秒内未监听端口立即报错退出，不继续启动 Nginx
- **健康检查**：启动后 HTTP 验证后端 `/health`、前端首页、API 代理

#### 36.3.2 stop-prod.ps1

**路径**：`C:\noneadcss\Production\stop-prod.ps1`

**执行流程**：

```
[STOP] Stopping Nginx...
  → nginx -s quit（优雅关闭，等待活跃连接完成）
  → 10 秒超时后 nginx -s stop（强制停止）
  → 兜底 Get-Process nginx | Stop-Process -Force
[STOP] Stopping backend service...
  → 通过端口 :5009 查找 PID（回退：按进程名 NoneadCSS 查找）
  → taskkill /PID xxx /T（发送终止信号）
  → 10 秒超时后 Stop-Process -Force
  → 兜底清理残留 NoneadCSS 进程
[OK] 输出停止摘要
```

### 36.4 Nginx 配置优化

#### 36.4.1 移除 vite preview 依赖

**原架构**（3 进程）：
```
Nginx :5173 → vite preview :5172（SPA 静态）
Nginx :5173 → backend :5009（API + WebSocket）
```

**新架构**（2 进程）：
```
Nginx :5173 → wwwroot/（直接 serve SPA 静态文件）
Nginx :5173 → backend :5009（API + WebSocket）
```

**配置变更**：

```nginx
# 移除 upstream frontend { server 127.0.0.1:5172; }

# 新增 root 指令
root c:/noneadcss/Production/wwwroot;

# 前端路由从 proxy_pass 改为 try_files
location / {
    try_files $uri $uri/ /index.html;
}

# 静态资源缓存
location /assets/ {
    expires 30d;
    add_header Cache-Control "public, immutable";
    access_log off;
    try_files $uri =404;
}

location ~* \.(svg|ico|png|jpg|jpeg|gif|webp|woff|woff2|ttf|eot)$ {
    expires 7d;
    add_header Cache-Control "public";
    access_log off;
    try_files $uri =404;
}
```

**收益**：
- 减少一个 Node.js 进程（vite preview）
- 部署更简单：只需 `npm run build` → `dist/` 复制到 `wwwroot/`
- 性能更好：Nginx 直接读取静态文件，无额外 HTTP 转发

#### 36.4.2 路径配置

所有 Nginx 路径统一指向 `Production/` 目录：

```nginx
error_log  c:/noneadcss/Production/logs/nginx-error.log warn;
pid        c:/noneadcss/Production/logs/nginx.pid;
proxy_temp_path       c:/noneadcss/Production/cache/proxy;
client_body_temp_path c:/noneadcss/Production/cache/client_body;

# mime.types 使用绝对路径
include C:/nginx/conf/mime.types;
```

### 36.5 环境变量配置

生产环境后端启动需注入以下环境变量：

| 变量名 | 说明 |
|--------|------|
| `ASPNETCORE_ENVIRONMENT` | 固定 `Production` |
| `ASPNETCORE_CONTENTROOT` | 指向 `Production/backend/`（appsettings.json 所在目录） |
| `Database__UseSqlite` | `true`（使用 SQLite） |
| `ConnectionStrings__SqliteConnection` | `Data Source=data/noneadcss.db;Mode=ReadWrite` |
| `JWT__Key` | JWT HS256 签名密钥（≥32 字节） |
| `SECRET_HASHER__Key` | 密码哈希密钥（64 字节 Base64） |
| `ENCRYPTION__Key` | AES-GCM 加密密钥（32 字节 Base64） |

### 36.6 验证结果

```
start-prod.ps1 执行                    → 后端 :5009 + Nginx :5173 启动成功 ✅
后端健康检查 GET /health                → 200 {"status":"Healthy"} ✅
前端首页 GET /                          → 200 (index.html) ✅
API 代理 GET /health via Nginx         → 200 {"status":"Healthy"} ✅
stop-prod.ps1 执行                     → 两端服务优雅停止 ✅
```

---

## 37. 2026-08-11 自定义报表软删除与垃圾箱功能

> 本章节记录自定义报表（CustomReport）模块的软删除与垃圾箱能力落地：实体层补齐 `ISoftDelete`、仓储层新增垃圾箱查询与物理删除方法、服务层新增恢复/永久删除/清空垃圾箱业务编排、控制器暴露 4 个 REST 端点、前端补齐三标签页（全部 / 我的置顶 / 垃圾箱）与二级确认流程。

### 37.1 功能概述

| 维度 | 变更前 | 变更后 |
|------|--------|--------|
| 删除行为 | `DELETE /api/CustomReports/{id}` 直接物理删除，不可恢复 | 软删除移入垃圾箱，可在垃圾箱中恢复或永久删除 |
| 数据可恢复性 | 删除即丢失 | 软删除后保留 `DeletedAt`，可经 `Restore` 还原 |
| 已删除数据可见性 | 不可见 | 普通用户仅看自己删除的；SuperAdmin 看所有 |
| 永久删除入口 | 主删除接口 | 独立 `DELETE /{id}/hard` 端点 + 二级确认 |
| 批量清理 | 无 | `POST /trash/clear` 一键清空本人/SuperAdmin 可见垃圾箱 |
| 关联调度处理 | 主删除会因外键约束失败 | 永久删除前先软删除关联 `CustomReportSchedule`，再物理删除报表 |
| 前端 UI | 双标签页（全部 / 我的置顶） | 三标签页（全部 / 我的置顶 / 垃圾箱），垃圾箱显示删除时间、恢复/永久删除按钮、清空入口 |

### 37.2 后端实现

#### 37.2.1 实体层

[CustomReport.cs](../NoneadCSS.Domain/Entities/CustomReport.cs) 实现 `ISoftDelete`，并补齐 `IsDeleted()` / `SoftDelete()` / `Restore()` 三个领域方法：

```csharp
public class CustomReport : ISoftDelete
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    // ... 其他业务字段
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }   // ISoftDelete

    public bool IsDeleted() => DeletedAt.HasValue;

    public void SoftDelete()
    {
        DeletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Restore()
    {
        DeletedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }
}
```

`CustomReportSchedule` 同样实现 `ISoftDelete` 并提供相同三方法，便于永久删除时同步软删除关联调度。

`NoneadCSSDbContext` 中为 `CustomReport` 配置基于软删除过滤的唯一索引，避免软删除后同名报表无法重建：

```csharp
modelBuilder.Entity<CustomReport>()
    .HasIndex(x => new { x.Name, x.OwnerId })
    .IsUnique()
    .HasFilter("\"DeletedAt\" IS NULL");
```

#### 37.2.2 仓储层

[CustomReportRepository.cs](../NoneadCSS.Infrastructure/Repositories/CustomReportRepository.cs) 新增三个方法，并在 `GetByIdTrackedAsync` 重写中统一 `IgnoreQueryFilters()`，使恢复/永久删除流程能取到已软删除的实体：

```csharp
public override async Task<CustomReport?> GetByIdTrackedAsync(int id)
{
    return await _dbSet
        .IgnoreQueryFilters()
        .FirstOrDefaultAsync(r => r.Id == id);
}
```

| 方法 | 用途 | 关键实现 |
|------|------|---------|
| `GetTrashPagedAsync` | 分页查询垃圾箱列表 | `IgnoreQueryFilters().AsNoTracking().Where(r => r.DeletedAt != null)`；普通用户追加 `r.OwnerId == userId` 过滤；按 `DeletedAt DESC` 排序 |
| `HardDeleteAsync(entity)` | 物理删除单个实体 | `_dbSet.Remove(entity)`；由 `SaveChangesAsync` 拦截器识别"已软删除的 ISoftDelete"走物理删除分支 |
| `ClearTrashAsync(userId, isSuperAdmin, userTeamIds)` | 批量物理清空垃圾箱 | 直接 `ExecuteDeleteAsync()` 发起 DELETE SQL，绕过 ChangeTracker 软删除拦截，返回受影响行数 |

```csharp
public async Task<int> ClearTrashAsync(int userId, bool isSuperAdmin, List<int> userTeamIds)
{
    var query = _dbSet.IgnoreQueryFilters()
        .Where(r => r.DeletedAt != null);

    if (!isSuperAdmin)
    {
        query = query.Where(r => r.OwnerId == userId);
    }

    // 使用 ExecuteDeleteAsync 直接执行 DELETE SQL，绕过 ChangeTracker 软删除拦截
    return await query.ExecuteDeleteAsync();
}
```

接口契约在 [ICustomReportRepository.cs](../NoneadCSS.Domain/Repositories/ICustomReportRepository.cs) 同步声明。

#### 37.2.3 服务层

[CustomReportService.cs](../NoneadCSS.Application/Services/CustomReportService.cs) 新增四个业务方法，统一通过 `ICurrentUserAccessor` 注入用户身份与 SuperAdmin 标识：

```csharp
public async Task<(List<CustomReportDto> Items, int TotalCount)> GetTrashPagedAsync(
    string? search, int page, int pageSize)
{
    var userTeamIds = await GetCurrentUserTeamIdsAsync();
    var (items, total) = await _unitOfWork.CustomReports.GetTrashPagedAsync(
        _currentUser.UserId,
        _currentUser.IsSuperAdmin,
        userTeamIds,
        search,
        page,
        pageSize);
    var dtos = await MapToDtosAsync(items);
    return (dtos, total);
}

public async Task<CustomReportDto?> RestoreAsync(int id)
{
    var report = await _unitOfWork.CustomReports.GetByIdIgnoreQueryFilterAsync(id);
    if (report == null || !report.IsDeleted())
        return null;

    // 权限：报表创建者或 SuperAdmin 可恢复
    if (!_currentUser.IsSuperAdmin && report.OwnerId != _currentUser.UserId)
        throw new InvalidOperationException("无权恢复此报表");

    report.Restore();
    await _unitOfWork.SaveChangesAsync();
    return await MapToDtoAsync(report);
}

public async Task<bool> HardDeleteAsync(int id)
{
    var report = await _unitOfWork.CustomReports.GetByIdIgnoreQueryFilterAsync(id);
    if (report == null) return false;

    // 权限：SuperAdmin 可永久删除任意报表；普通用户仅能永久删除自己的、已在垃圾箱中的报表
    if (!_currentUser.IsSuperAdmin)
    {
        if (report.OwnerId != _currentUser.UserId)
            throw new InvalidOperationException("无权永久删除此报表");
        if (!report.IsDeleted())
            throw new InvalidOperationException("只能永久删除已在垃圾箱中的报表");
    }

    // 先一并清理关联的调度（软删除），再硬删除报表本身（已软删除的 ISoftDelete 会被物理删除）
    var schedules = await _unitOfWork.CustomReportSchedules.GetByReportIdAsync(id);
    foreach (var s in schedules)
    {
        s.SoftDelete();
    }

    await _unitOfWork.CustomReports.HardDeleteAsync(report);
    await _unitOfWork.SaveChangesAsync();
    return true;
}

public async Task<int> ClearTrashAsync()
{
    var userTeamIds = await GetCurrentUserTeamIdsAsync();
    // ClearTrashAsync 使用 ExecuteDeleteAsync，无需再调用 SaveChanges
    return await _unitOfWork.CustomReports.ClearTrashAsync(
        _currentUser.UserId,
        _currentUser.IsSuperAdmin,
        userTeamIds);
}
```

**关键设计点**：

- `HardDeleteAsync` 强制要求普通用户的目标报表必须先软删除（即在垃圾箱中），防止误用永久删除端点跳过软删除流程；SuperAdmin 不受此约束。
- 永久删除前显式 `SoftDelete()` 关联 `CustomReportSchedule`，避免外键级联约束失败；最终物理删除由 `SaveChangesAsync` 拦截器对"已软删除的 ISoftDelete"识别并放行（参见 `NoneadCSSDbContext.SaveChangesAsync` 中的分类逻辑）。
- `ClearTrashAsync` 走 `ExecuteDeleteAsync`，不进入 ChangeTracker，因此无需调用 `SaveChangesAsync`。

接口契约在 [ICustomReportService.cs](../NoneadCSS.Application/Services/ICustomReportService.cs) 同步声明。

#### 37.2.4 控制器层

[CustomReportsController.cs](../NoneadCSS.Api/Controllers/CustomReportsController.cs) 新增 4 个端点，统一通过 `MapBusinessException` 将"无权"映射为 403、"不存在"映射为 404、其他业务校验失败映射为 400：

```csharp
/// <summary>
/// 获取已删除（垃圾箱）列表 - 支持分页与搜索
/// </summary>
[HttpGet("trash")]
public async Task<ActionResult> GetTrashPaged(
    [FromQuery] string? search = null,
    [FromQuery] int page = 1,
    [FromQuery] int pageSize = PaginationHelper.DefaultPageSize)

/// <summary>
/// 恢复垃圾箱中的报表
/// </summary>
[HttpPost("{id}/restore")]
[Permission("custom_reports.delete")]
public async Task<ActionResult<CustomReportDto>> Restore(int id)

/// <summary>
/// 永久删除（硬删除）垃圾箱中的报表
/// </summary>
[HttpDelete("{id}/hard")]
[Permission("custom_reports.delete")]
public async Task<ActionResult> HardDelete(int id)

/// <summary>
/// 清空垃圾箱（一次永久删除本人可见的所有已删除报表）
/// </summary>
[HttpPost("trash/clear")]
[Permission("custom_reports.delete")]
public async Task<ActionResult<int>> ClearTrash()
```

恢复/永久删除/清空三个写操作统一挂 `[Permission("custom_reports.delete")]` 权限要求，与既有软删除接口对齐；分页参数通过 `PaginationHelper` 钳制到 `[1, MaxPageSize]` 区间。

### 37.3 前端实现

#### 37.3.1 API 客户端

[frontend/src/api/modules/customReports.ts](../frontend/src/api/modules/customReports.ts) 新增 4 个 API 方法，与后端端点一一对应：

```ts
getTrashPaged: (params?: { search?: string; page?: number; pageSize?: number }) =>
  client
    .get<PagedCustomReportsResponse>('/api/customreports/trash', { params: { ... } })
    .then((r) => r.data),

restore: (id: number) =>
  client.post<CustomReportDto>(`/api/customreports/${id}/restore`).then((r) => r.data),

hardDelete: (id: number) =>
  client.delete<boolean>(`/api/customreports/${id}/hard`).then((r) => r.data),

clearTrash: () =>
  client.post<{ cleared: number }>('/api/customreports/trash/clear').then((r) => r.data),
```

#### 37.3.2 页面 UI 交互

[frontend/src/pages/CustomReports.tsx](../frontend/src/pages/CustomReports.tsx) 新增"垃圾箱"标签页与配套交互：

- **三标签页结构**：`tab` 状态由 `'all' | 'pinned'` 扩展为 `'all' | 'pinned' | 'trash'`，`displayReports` / `displayLoading` / `displayTotalItems` / `displayTotalPages` 等派生状态按 tab 切换数据源。
- **垃圾箱徽标**：tab 栏"垃圾箱"按钮右上角显示 `trashTotalItems` 数字徽标，rose 配色提示用户存在可恢复内容。
- **空状态分支**：垃圾箱为空时显示 `Trash2` 图标 + "垃圾箱为空 / 删除的报表会暂时保存在这里"提示。
- **卡片样式**：垃圾箱模式下卡片整体 `opacity-80`、`border-slate` 配色；图标改用灰阶渐变；隐藏置顶按钮；标签区追加"已删除"红色徽标。
- **删除时间展示**：垃圾箱模式下卡片底部时间列改显示 `r.deletedAt`（带 `title="删除时间"`），非垃圾箱模式仍显示 `r.updatedAt`。
- **卡片操作按钮**：垃圾箱模式卡片底部展示两个按钮——绿色"恢复"（`Undo2` 图标，触发 `setRestoreTarget(r)`）与红色"永久删除"（`Trash2` 图标，触发 `setHardDeleteTarget(r)`）。
- **清空垃圾箱入口**：垃圾箱 tab 激活时页面右上操作区出现"清空垃圾箱"按钮（`Trash` 图标），`displayLoading || trashTotalItems === 0` 时禁用，点击触发 `setConfirmingClearTrash(true)`。
- **二级确认对话框**：四个 `ConfirmDialog` 分别承接"删除（移入垃圾箱）"、"恢复报表"、"永久删除报表"、"清空垃圾箱"四类操作；永久删除与清空两个对话框内嵌 amber 警示框，明确告知"不可恢复"。
- **删除/恢复/永久删除/清空后刷新策略**：所有写操作完成后调用 `fetchTrash()` 重新拉取垃圾箱列表，确保徽标数字与卡片列表立即同步。

### 37.4 权限控制设计

| 操作 | 普通用户 | SuperAdmin | 抛错路径 |
|------|---------|------------|---------|
| 查看垃圾箱列表 | 仅看 `OwnerId == userId` 的已删除报表 | 全部已删除报表 | 仓储层 `GetTrashPagedAsync` 内置 `Where` 过滤，无显式异常 |
| 恢复报表 | 仅可恢复自己创建的报表 | 任意报表 | `RestoreAsync` 抛 `InvalidOperationException("无权恢复此报表")` → 403 |
| 永久删除报表 | 仅可永久删除**自己**且**已在垃圾箱中**的报表 | 任意报表（不论是否在垃圾箱） | `HardDeleteAsync` 抛 `"无权永久删除此报表"` → 403；或 `"只能永久删除已在垃圾箱中的报表"` → 400 |
| 清空垃圾箱 | 仅清空自己删除的报表 | 清空全部已删除报表 | 仓储层 `ClearTrashAsync` 内置 `Where` 过滤，无显式异常 |

权限决策通过 `ICurrentUserAccessor.IsSuperAdmin` / `UserId` 完成，不依赖额外权限位；与既有 `CanEditAsync(report)`（SuperAdmin 或 OwnerId）保持一致语义但显式内联，便于在异常路径中精准区分"无权"与"状态非法"。

### 37.5 API 接口列表

| 方法 | 路径 | 权限 | 用途 | 返回 |
|------|------|------|------|------|
| GET | `/api/CustomReports/trash` | 登录 + `custom-reports` 模块 | 分页查询垃圾箱列表（支持 `search` / `page` / `pageSize` 查询参数） | `{ data, totalItems, currentPage, totalPages }` |
| POST | `/api/CustomReports/{id}/restore` | `custom_reports.delete` | 恢复指定报表，清空 `DeletedAt` | `CustomReportDto` |
| DELETE | `/api/CustomReports/{id}/hard` | `custom_reports.delete` | 物理删除指定报表（先软删除关联调度） | `200 OK` |
| POST | `/api/CustomReports/trash/clear` | `custom_reports.delete` | 批量清空当前用户可见的垃圾箱报表 | `{ cleared: number }` |

> 既有 `DELETE /api/CustomReports/{id}` 语义已由"物理删除"变更为"软删除移入垃圾箱"，前端 toast 提示同步更新为"删除成功，已移至垃圾箱"。

---

## 38. 2026-08-11 审计后批量修复 v2.2.0（P0 安全 + 功能 + 数据库 + 生产部署）

> 本章节记录 v2.2.0 版本的全量变更，包含 **S-H1/S-H2（P0 安全）**、**F-C3/C4/C5（P0 功能）**、**D-C1 + D-M1/M2/M3（P0/M 数据库）**、**P1 安全 3 项（S-M1/M2/M3）**、**P1 代码质量（Q-M1/M2）**、**P1 功能逻辑（F-M1）**、**P1 前端（FE-M1/M2）**、**P1 基础设施（I-M1/M2/M3）**，以及正式发布流程。

### 38.1 P0 安全修复

#### 38.1.1 S-H1：EncryptedStringConverter 解密失败不再返回密文

**风险**：`EncryptedStringConverter.ConvertFromProvider` → `DecryptSafely()` 在 `TryDecrypt` 返回 false 时，原代码 `return cipherText`；而本系统使用的 AesEncryptionService 密文是 Base64(JSON) 结构，本身**半可读**（包含 `KeyData`/`IV` 等元数据字段，以及 `CipherText` 本体）。如果在 API 响应中直接暴露，意味着：
1. 历史数据（旧 SHA256 派生密钥加密）无法用 HKDF 新密钥解密 → 半结构化密文泄漏到前端；
2. 日志链路也会记录该"明文"字段，形成二次泄漏；
3. 后续如果 HKDF 密钥变更，而 `LegacyKey` 未设置 → 所有记录的敏感字段泄漏。

**修复**：

```csharp
// EncryptedStringConverter.cs / NoneadCSSDbContext.cs
private static string DecryptSafely(IEncryptionService encryptionService, string cipherText)
{
    if (encryptionService.TryDecrypt(cipherText, out var plainText))
    {
        return plainText ?? string.Empty;
    }
    // 修复：解密失败返回空字符串，不再把密文当"明文"返回
    return string.Empty;
}
```

同步修复 `NoneadCSSDbContext.DecryptSafely`（SaveChanges 拦截器内部的另一处独立实现）。

#### 38.1.2 S-H2：AesEncryptionService 旧密钥回退告警

**风险**：`Decrypt` 中 HKDF 失败后回退到 `DecryptLegacy`，整个链路是静默的。如果运维没有定期跟踪日志，将不知道系统长期处于"新旧密钥并存"状态，后续删除 legacy 代码时历史数据无法解密。

**修复**（`AesEncryptionService.cs`）：

```csharp
// 构造函数 — 检测 LegacyKey 被加载时的启动告警
if (!string.IsNullOrWhiteSpace(_legacyKey))
{
    _logger.LogWarning("已加载旧版 SHA256 加密密钥用于向后兼容解密。建议执行密钥迁移计划，尽快使用 Encryption/Key 重加密所有敏感字段，并移除 Encryption/LegacyKey 配置。");
}

// Decrypt 方法 — 首次回退告警（只记一次）
if (Interlocked.CompareExchange(ref _legacyFallbackLogged, 1, 0) == 0)
{
    _logger.LogWarning("HKDF 密钥解密失败，已回退到旧 SHA256 密钥解密历史数据。请尽快迁移敏感字段至 HKDF 密钥（参考 docs/security-audit-report.md 迁移章节）。");
}
```

### 38.2 P0 功能修复

#### 38.2.1 F-C3：邮件入队失败日志结构化

三段链路均结构化输出：
- `ConversationEmailJob.Execute(IJobExecutionContext)`：`EnqueueAsync` 失败 → `LogWarning("{Job} conversationId={cid} emailId={eid} reason={r} stack={s}")`
- `EmailDispatcher.DispatchAsync`：`SmtpClient.SendAsync` 失败 → `LogError("{Dispatcher} ...")`
- `EmailService.SendNotificationSafeAsync`：`SendAsync` 异常 → `LogWarning` + 不抛（原逻辑部分 catch 吞异常，已补齐）

#### 38.2.2 F-C4：用户恢复邮箱冲突检查

```csharp
// UserService.RestoreAsync
var existing = await _unitOfWork.Users.GetByEmailAsync(user.Email!);
if (existing != null && existing.Id != user.Id && !existing.IsDeleted())
{
    throw new BusinessException(ErrorCode.UserAlreadyExists,
        $"邮箱 {user.Email} 已被现有用户 ID={existing.Id} 占用，无法恢复软删除用户 ID={user.Id}。");
}
```

#### 38.2.3 F-C5：报表硬删除校验

```csharp
if (!_currentUser.IsSuperAdmin)
{
    if (report.OwnerId != _currentUser.UserId)
        throw new InvalidOperationException("无权永久删除此报表");
    // 普通用户硬删除前必须先软删除
    if (!report.IsDeleted())
        throw new InvalidOperationException("只能永久删除已在垃圾箱中的报表");
}
```

### 38.3 P0 数据库 / 基础设施修复

#### 38.3.1 D-C1：Conversation N+1 优化

`ConversationRepository.GetPagedAsync` 原 `Include(c => c.Customer).ThenInclude(x => x.Conversations)` → 去掉反向 ThenInclude；所有查询方法统一 `AsNoTracking()`：
- `GetPagedAsync`
- `GetByIdAsync`
- `GetConversationsByCustomerIdAsync`
- `GetUnassignedCountAsync`
- `GetSlaBreachCountAsync`
- `GetByMailboxIdAsync`

#### 38.3.2 D-M1：BaseController 统一 CurrentUserId

新增 `Controllers/BaseController.cs`：

```csharp
[ApiController]
[Authorize]
public abstract class BaseController : ControllerBase
{
    protected int? CurrentUserId
    {
        get
        {
            var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(claim, System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                ? id
                : null;
        }
    }
}
```

所有业务 Controller（`ConversationsController` / `CustomersController` / `CalendarController` / `KnowledgeBaseController` / `ReportsController` / `CustomReportsController` / `UserProfileController` / `InboxController` / `TeamsController` / `WorkflowsController` / `TagsController` / `WebhookSubscriptionsController`）改为继承 BaseController。

#### 38.3.3 D-M2：移除废弃的 DecryptLegacy

`IEncryptionService` 和 `AesEncryptionService` 同步删除 `DecryptLegacy(string)` 签名；Legacy 逻辑内联到 `Decrypt` 的 try/catch 内，不再对外暴露。

#### 38.3.4 D-M3：ISoftDelete 硬删除拦截器放行

`NoneadCSSDbContext.SaveChangesAsync` 对"状态=Deleted + 实体是 ISoftDelete + 已被软删除（DeletedAt != null）"的实体，允许真正 Remove（不被自动转 SoftDelete）。由此支持 `CustomReport.HardDelete()` 配合 `CustomReportSchedule.SoftDelete()` 不触发外键约束。

### 38.4 P1 批量修复概览

| ID | 类型 | 变更点 |
|----|------|--------|
| S-M1 | P1-SAFETY | 生产端点保护：TrackingLegacy / Feishu 注入 / WeChat CheckSignature 生产环境返回 404 |
| S-M2 | P1-SAFETY | BaseController（见 D-M1）统一用户 ID 提取（拆箱安全） |
| S-M3 | P1-SAFETY | 重置密码链路：ResetPassword 后失效重置 Token + 撤销所有 Refresh Token |
| Q-M1 | P1-QUALITY | 移除 `IEncryptionService.DecryptLegacy` 对外签名（同 D-M2） |
| Q-M2 | P1-QUALITY | WorkflowActionExecutor `execute_macro` 用 `GetService` 防未注册抛 500 |
| F-M1 | P1-FEATURE | ConversationService.Merge 前校验 Channel/ChannelId 一致 |
| FE-M1 | P1-FRONTEND | useAuth.logout 先调后端 logout API 再 clear localStorage |
| FE-M2 | P1-FRONTEND | CustomerManagement 删除客户前置提示（会话也进回收站） |
| FE-M3 | P1-FRONTEND | .gitignore 加入 frontend/dist/** + frontend/.vite/** |
| I-M1 | P1-INFRA | start-prod.ps1 修复 secretsFile（中文注释导致 Join-Path 变量丢失） |
| I-M2 | P1-INFRA | appsettings.Production.json 新增 `"Urls": "http://127.0.0.1:5009"` 覆盖开发端口 |
| I-M3 | P1-INFRA | Production/nginx.conf：/chat/widget 的 frame-ancestors CSP + try_files =404 修复 |
| I-M4 | P1-INFRA | backup-db.ps1：备份文件名加日期 + 保留最近 7 天 |

### 38.5 正式发布流程

1. **编译**（开发机执行）：
   ```powershell
   dotnet build NoneadCSS.sln -c Release
   cd frontend ; npm run build ; cd ..
   ```
2. **打包发布**：
   ```powershell
   dotnet publish NoneadCSS.Api/NoneadCSS.Api.csproj -c Release -o C:\noneadcss_prod\backend
   Copy-Item frontend\dist\* C:\noneadcss_prod\frontend\dist\ -Recurse -Force
   Copy-Item Production\* C:\noneadcss_prod\ -Recurse -Force
   ```
3. **推送 Git**：
   ```powershell
   git commit -a -m "release v2.2.0: P0/P1 audit fix + S-H1/S-H2 security + production deploy + widget CSP"
   git push origin master
   git tag v2.2.0 ; git push origin v2.2.0
   ```
4. **生产重启**：`C:\noneadcss_prod\restart-prod.ps1`

### 38.6 生产验收清单（v2.2.0）

- [x] 后端 0 警告 0 错误编译
- [x] 前端 0 tsc 错误 + build 成功
- [x] restart-prod.ps1 跑完无报错，5009 / 5172 / 5173 全部监听
- [x] `/health` → 200，`/health/ready` → 200
- [x] `/docs` 可打开 Scalar 文档
- [x] 登录页验证码正常加载（开发/生产端口均一致）
- [x] 邮箱配置页敏感字段返回空字符串（无密文泄漏）
- [x] Nginx `curl -I http://css.example.com:5173/chat/widget` 返回 CSP 头含 frame-ancestors
- [x] 启动日志含 LegacyKey Warning 告警
- [x] Customer Management 删除客户前置"会话将一并进入回收站"提示
- [x] Custom Reports 垃圾箱 Tab 角标、恢复/永久删除/清空全流程

---

## 39. 2026-08-17 v2.2.3 热修复：日历回弹 + 8 实体乐观锁 + DDL 补丁 + 发布 SOP 固化

> 本章节记录 v2.2.3 发布后追加到生产的 P3 级稳定性修复：`ConcurrencyException` 通用异常体系落地、8 张高并发表 RowVersion 乐观锁、Calendar 三视图（日历 / 列表 / 概览）乐观更新 + 回弹修复、`_DbPatchRowVersion` DDL 补丁工具、7 步生产发布 SOP 基线。

### 39.1 通用并发异常 ConcurrencyException（替换专用 CalendarConcurrencyException）

为支持所有实体类型的 409 冲突响应，定义通用异常：

```csharp
// NoneadCSS.Domain/Exceptions/ConcurrencyException.cs
public class ConcurrencyException : Exception
{
    public string EntityName { get; }
    public int EntityId { get; }
    public object? LatestSnapshot { get; }
    // ...
}

// 保留派生类，兼容已有代码（Binary Compatible）
public class CalendarConcurrencyException : ConcurrencyException { /* ... */ }
```

`GlobalExceptionMiddleware` 对所有 `ConcurrencyException` 统一返回：

```json
{
  "status": 409,
  "error": "ConcurrencyConflict",
  "entity": "CalendarEvent",
  "id": 123,
  "latest": { /* 后端最新 DTO 快照 */ }
}
```

前端写操作：
1. 提交时把本地 `rowVersion` / `version` 作为 `If-Match`（或 DTO 内 `ifMatch`）携带；
2. 409 冲突 → 用 `latest` 快照 `optimisticUpsertEvent(latest)` 覆盖本地，并 toast "已被他人更新，本地已刷新"；
3. 200 成功 → **必须**以 `response.data` 再次调用 `optimisticUpsertEvent`（含最新 `rowVersion`），否则 SignalR 推送的滞后版本会在 2 秒窗口内把乐观值覆盖"回弹回去"（见 39.3）。

### 39.2 8 实体 RowVersion 乐观锁全景（P3 级别全部落地）

| 实体 | 字段 | EF Core Fluent 配置 | Controller 写入口 | 并发场景 |
|------|------|--------------------|--------------------|----------|
| CalendarEvent | RowVersion (byte[]) | `.IsRowVersion().ValueGeneratedOnAddOrUpdate()` | CalendarController PUT/DELETE / resize / move | 多客服同时拖拽同一事件 |
| TimeEntry | RowVersion (byte[]) | 同上 | CalendarController TimeEntry PUT/DELETE | 多人计时同时停止 |
| User | RowVersion (byte[]) | 同上 | UsersController PUT / ResetPassword | 并发登录递增 FailedLoginAttempts 丢失 |
| Customer | RowVersion (byte[]) | 同上 | CustomersController PUT / Merge | GetOrCreateByEmailAsync 并发 lost update |
| ChatSession | RowVersion (byte[]) | 同上 | ChatSessionsController PUT / Close / MarkRead | 客户与客服同时写 UnreadCount/Status/LastActiveAt |
| ChatMessage | RowVersion (byte[]) | 同上 | ChatSessionsController Recall / Edit | 撤回/编辑并发 |
| ApiKey | RowVersion (byte[]) | 同上 | ApiKeysController PUT / Revoke | UsageCount/LastUsedAt 并发递增丢失 |
| Webhook | RowVersion (byte[]) | 同上 | WebhooksController PUT / TriggerNow | TriggerCount/SuccessCount/FailCount 并发递增丢失 |
| KnowledgeCategory | RowVersion (byte[]) | 同上 | KnowledgeBaseController Category PUT/DELETE | 多人并发重建分类树 |
| KnowledgeArticle | RowVersion (byte[]) | 同上 | KnowledgeBaseController Article PUT/DELETE/Publish | 多人协同编辑同一文章 |
| Conversation | Version (int) | `.IsConcurrencyToken().ValueGeneratedOnAddOrUpdate()` | ConversationsController PUT/Assign/Close | 原实现保留 |
| ConversationThread | Version (int) | 同上 | InboxController AddReply / AddNote | 原实现保留 |
| Mailbox | Version (int) | 同上 | MailboxesController PUT/TestConnection | 原实现保留 |

> 对应数据库层补丁工具与执行方式见 §18.10，任何跳过该补丁直接升级的数据库都会在首次启动时报 `SQLite Error 1: 'no such column: u.RowVersion'`。

### 39.3 日历编辑"保存后回弹"根因 + 三视图乐观同步

**复现（2026-08-17 用户）**：周视图事件 `09:00~17:00` → 双击 EditorPanel → 结束时间改为 `15:00` → 保存 → 格子先走到 `09:00~15:00`，≈2 秒后回弹回 `09:00~17:00`。

**根因链**：

1. 前端 4 处写操作（`EditorPanel.save`、`store.moveEventByDrop`、`store.resizeEvent`、列表/概览行内编辑）`await updateEvent()` 成功后，**未**用后端返回的最新 DTO 再次覆盖本地乐观值；
2. SignalR 的 `signalRRev` 刷新发生在写完成后 2 秒窗口内，此时 `_optimisticMarkers[id]` 被成功分支过早清理，从后端拉到的旧值（API 持久化之前中间缓存版本）把本地乐观值覆盖回去。

**修复（4 处写操作 + store）**：

```ts
// EditorPanel.save 成功分支
const res = await calendarApi.updateEvent(form.id!, payload);
store.optimisticUpsertEvent(res.data);          // ← 新增：以 API 最新 DTO 二次覆盖，含 rowVersion
toast.success("日程已保存");

// store.moveEventByDrop / store.resizeEvent 成功分支同样执行 optimisticUpsertEvent(res.data)
// 同时保护位 TTL 从 30s → 60s，兜底慢 SignalR 推送
const OPTIMISTIC_TTL_MS = 60_000;
```

**三视图同步**：`optimisticUpsertEvent` 现在同时更新：
- `allEvents: Record<id, EventDto>`（日历视图真源）
- `eventsByDate: Record<YYYY-MM-DD, EventDto[]>`（月/周/日桶派生缓存，避免 O(n) 重建）
- `list: EventDto[]`（列表视图状态）
- `overview.upcomingEvents: EventDto[]`（概览 KPI 最近日程桶）

任意入口修改 → 三者一致，修复"日历改完、概览 KPI 仍显示旧时间"的派生桶未同步问题。

**失败回滚**：`moveEventByDrop / resizeEvent` 在 API 抛错（含 409）时，按操作前 `snapshotBefore` 调用 `optimisticUpsertEvent(snapshotBefore)` 还原，toast 显示失败原因。

### 39.4 _DbPatchRowVersion DDL 补丁工具（独立 C# 控制台 · **未随本仓库发布**）

> 一次性内部工具，只在 2026-08-17 的公司生产库上执行过；开源树里没有该项目。全新库由迁移链建列，无需此工具；旧库补列的手工做法见 §18.10。以下内容保留作历史记录。

PowerShell 直接 `Add-Type Microsoft.Data.Sqlite.dll` 会因无法加载 SQLitePCLRaw 报 `FileNotFoundException`，因此用独立 C# 控制台项目引用 `Microsoft.EntityFrameworkCore.Sqlite`，保证依赖正确加载。

**项目位置**：`_DbPatchRowVersion/_DbPatchRowVersion.csproj`（历史位置；该一次性工具未随本仓库发布，见上方说明）。

**关键幂等策略**：

```csharp
// 1. 事务幂等 + 8 张表逐张探测
using var tx = conn.BeginTransaction(System.Data.IsolationLevel.Serializable);
foreach (var (table, col, sql) in Patches)
{
    var exists = conn.ExecuteScalar<int>(
        $"SELECT COUNT(*) FROM pragma_table_info(\"{table}\") WHERE name = @col",
        new { col });
    if (exists == 0)
        conn.Execute(sql, transaction: tx);
}
tx.Commit();
```

### 39.5 7 步生产发布 SOP 基线（与 project_memory.md 一致）

详见 §18.11。发布顺序绝对不能变：**① 停服 + 清理残留 → ② dotnet publish → ③ npm 构建 → ④ 配置同步 → ⑤ start-prod 非阻塞启动 → ⑥ 四端健康 + :5008 负向断言 → ⑦ 进程名二次核对**。

### 39.6 遗留风险

| 风险 | 等级 | 缓解建议 |
|------|------|----------|
| `SqliteMigrationSafety.cs` 行 50-52 在相对连接字符串下仍回退 `AppContext.BaseDirectory`，若有人回退到相对路径仍可能触发"备份错文件" | 低（当前生产使用绝对路径） | 后续可硬编码：只接受绝对路径或使用 `Directory.GetCurrentDirectory()` 解析，消除歧义 |
| Controller 写入口仍有少数未显式校验 If-Match（非 Calendar 模块高并发表 ApiKey/Webhook/ChatSession 等） | 中（P3，已配置 RowVersion 但未做显式 409 + latest 快照回写） | 下一轮 P3-P4 逐个 Controller 补齐 If-Match 校验与前端冲突提示 |

---

## 40. 2026-08-25 日历模块多轮稳定性修复 + P3 前端类型安全加固

> 本章节记录 v2.2.3 发布后连续多轮针对日历模块的回归修复与 P3 前端类型安全加固。修复覆盖色块重复 / 残留、跨日拖拽偏移、undo/redo 回滚、SignalR 重连刷新、垃圾箱/订阅面板真实 API 对接、重复事件展开、与会者邮箱同步、提醒类型选择、RRULE 生成、创建者显示、缓存生命周期、以及 39 处 `as any` / `: any` 类型消除。

### 40.1 拖拽色块重复 + 原位置残留（composite key 不一致）

**现象**：月视图拖拽事件到新位置后，新位置出现两条相同色块，原位置色块残留，刷新后消失。

**根因**：`optimisticUpsertEvent` 使用复合键（`id#recurrenceOriginalStart` 或 `id`）做去重，但后端返回的父事件 DTO 中 `recurrenceOriginalStart` 为 null，导致键不匹配（`id` vs `id#originalStart`），新事件被追加而非替换。

**修复**：在所有 `optimisticUpsertEvent` 调用处，用被操作实例的 `recurrenceOriginalStart` 覆盖后端 DTO 的 null 值，确保复合键一致。

```ts
// store.ts - moveEventByDrop / resizeEvent / EditorPanel.save / ListView quickUpdate
const resWithKey = {
  ...res.data,
  recurrenceOriginalStart: draggedInstance.recurrenceOriginalStart, // 覆盖后端 null
};
store.optimisticUpsertEvent(resWithKey);
```

涉及文件：`store.ts`（drag/resize）、`ListView.tsx`（quick update）、`EditorPanel.tsx`（save）。

### 40.2 删除冲突事件色块残留（setAllEvents 增量合并 + 乐观保护跳过删除推送）

**现象**：删除事件后色块仍残留在视图上，刷新后才消失。

**根因链**：
1. `setAllEvents` 使用增量合并策略，保留旧事件未被替换；
2. 乐观保护窗口（2 秒延迟 `signalRRev` bump）跳过了删除通知的推送。

**修复**：
- 删除分支在 `onChanged` 中直接更新 state，不依赖 SignalR 推送；
- 移除 2 秒延迟，立即 `signalRRev + 1` 触发全量刷新；
- 批量删除场景下，后端只发送 refresh 而非逐条 delete 通知，前端手动按 ID 过滤删除的事件。

### 40.3 跨日拖拽位置错误（moveEventByDrop 用父事件 startAt 而非被拖拽实例 newStartIso）

**现象**：跨日期拖拽日程后，事件出现在错误位置（重叠到原日期）。

**根因**：`moveEventByDrop` 中两处使用父事件 DTO 的 `startAt` 而非被拖拽实例的 `newStartIso`：
1. `optimisticUpsertEvent(resWithKey)` 用父事件 `startAt` 覆盖被拖拽实例 → 重叠；
2. `upsertPendingPatch` 中 `patch.startAt` 使用父事件起始时间 → `deltaMs` 计算错误。

**修复**：保留乐观更新的 `startAt: newStartIso` / `endAt: newEndIso`，仅从 `res` 取元数据；`patch.startAt` 改为 `newStartIso` 以确保正确计算偏移量。

### 40.4 垃圾箱面板（TrashPanel）mock 实现对接真实 API

**问题**：`TrashPanel.tsx` 的恢复/永久删除/清空操作全部是 mock 实现，未调用真实后端 API。

**修复**：对接 `store` 的 async 方法（`restoreEvent` / `purgeEvent` / `emptyTrash`），移除所有 mock 逻辑，添加加载态和错误提示。

### 40.5 订阅面板（SubscriptionPanel）重新生成链接 mock + URL fallback 修复

**问题**：
1. `SubscriptionPanel.tsx` 的"重新生成订阅链接"按钮是 mock 实现；
2. `store.openSubscription` 在订阅 URL 为空时回退到 mock URL（`https://example.com/calendar.ics`）。

**修复**：
- 后端 `CalendarController` 新增 `POST /api/calendar/subscription/regenerate` 端点；
- 前端 `store.openSubscription` 改为拉取真实 URL，移除 mock fallback；
- `SubscriptionPanel` 移除 mock URL，增加空状态和加载态。

### 40.6 undo/redo 失败未回滚乐观修改 + SignalR 重连消息丢失

**P1-1：undo/redo 失败回滚**

**问题**：undo/redo 操作失败时，乐观修改未回滚，导致 UI 显示与后端不一致。

**修复**：在 try 块开始时保存 `prevAllEvents / prevEventsByDate / prevList / prevOverview / prevTrashCount / prevMarkers`，catch 块中恢复。

```ts
// store.ts - undo/redo try/catch
const prevAllEvents = get().allEvents;
const prevEventsByDate = get().eventsByDate;
// ... 其他 prev 快照
try {
  await calendarApi.undoEvent(id);
  // 成功分支
} catch (e) {
  // 失败回滚
  set({
    allEvents: prevAllEvents,
    eventsByDate: prevEventsByDate,
    list: prevList,
    overview: prevOverview,
    trashCount: prevTrashCount,
    _optimisticMarkers: prevMarkers,
  });
  toast.error('撤销失败，已还原');
}
```

**P1-2：SignalR 重连后数据刷新**

**问题**：SignalR 重连后未触发数据刷新，导致重连期间错过的消息丢失。

**修复**：
- `signalR.ts` 的 `onreconnected` 回调中调用 `notifyListeners('_reconnected', null)`；
- `Calendar.tsx` 注册 `_reconnected` 监听器，触发 `signalRRev + 1` 全量刷新。

**P2-1：页面可见时 SignalR 重连**

**问题**：标签页切到后台后 SignalR 连接断开，切回前台时未重连。

**修复**：`Calendar.tsx` 注册 `visibilitychange` 监听器，页面可见且连接断开时触发重连。

### 40.7 setAllEvents 逻辑混乱 + 删除分支 marker 清理

**P2-2：setAllEvents instEndMs 逻辑**

**问题**：`calendarUtils.ts` 中 `setAllEvents` 的 `instEndMs` 计算逻辑混乱，拖拽实例与普通实例混用同一计算分支。

**修复**：拆分为独立的 `if (isDragged)` 分支，避免逻辑耦合。

**P2-3：删除分支 marker 清理**

**问题**：`Calendar.tsx` 的 `onChanged` 删除分支未调用 `store._unmarkOptimistic(payload.eventId)`，导致乐观标记残留，后续 SignalR 推送被跳过。

**修复**：删除分支调用 `store._unmarkOptimistic(payload.eventId)`。

### 40.8 与会者邮箱列表同步 + 提醒类型选择 + RRULE 生成

**问题 1：与会者邮箱与用户 ID 不同步**

`EditorPanel.tsx` 的 `_attendeeEmailList` 与 `attendees`（用户 ID 数组）未同步，导致邮件提醒收件人错误。

**修复**：保存时将 `_attendeeEmailList` 序列化为 CSV 写入 `reminderEmails`；编辑时从 `reminderEmails` 解析回列表。

**问题 2：提醒类型选择 UI 缺失**

原实现只有站内通知和邮件二选一，缺少显式选择 UI。

**修复**：新增下拉选择器（站内通知 / 邮件 / 短信），绑定 `reminderType` 字段。

**问题 3：RRULE 生成不可靠**

原实现手动拼接 RRULE 字符串，易出错（如 9 月 16 日创建每月重复 4 次的日程，不显示 10/16/11/16/12/16）。

**修复**：保存时自动从表单数据生成最新 RRULE，UI 预览独立显示中文描述。

```ts
// EditorPanel.tsx - generateRRuleDescription
function generateRRuleDescription(form: EditorForm): string {
  // 根据 recurrenceType / interval / endDate / count 生成中文描述
  // 例如："每 1 月重复，共 4 次"
}
```

### 40.9 编辑器表单字段清理 + 截止时间字段说明

**问题**：
1. `handleReminderTypeChange` 未清空 `reminderSettings`；
2. `handleEventTypeChange` 未清理残留字段；
3. `handleDueAtChange` 存在时区偏移隐患；
4. 「截止时间」（`dueAt`）字段含义不明确。

**修复**：
- `handleReminderTypeChange` 清空 `reminderSettings`；
- `handleEventTypeChange` 切换事件类型时清理非专属字段；
- `handleDueAtChange` 修复时区偏移；
- 明确 `dueAt` 是任务类型（`eventType === 'task'`）专属字段，与 `endAt` 独立，默认跟随 `endAt` 但用户修改后保持独立值。

### 40.10 重复事件展开修复 + 悬停弹窗创建者显示

**问题 1：重复事件展开失败**

9 月 16 日创建每月重复、间隔 1 月、4 次后结束的日程，未显示 10/16、11/16、12/16 的重复实例。

**根因**：RRULE 生成错误导致展开器无法正确解析。

**修复**：修复 RRULE 生成逻辑（见 40.8）。

**问题 2：悬停弹窗缺少创建者名称**

日期网格色块的悬停弹窗只显示时间，缺少创建者信息。

**修复**：`chips.tsx` 使用 `ev.createdByName` 字段显示"14:00-15:00 · 创建者: 张三"。

### 40.11 MemoryCalendarQueryCache 生命周期 + confirmDelete pendingPatch 清理

**问题 1：MemoryCalendarQueryCache 注册为 Scoped 导致缓存失效**

**现象**：删除色块刷新后重新出现。

**根因**：`MemoryCalendarQueryCache` 注册为 Scoped，其 `_userVersions` 状态未跨请求持久化，导致缓存失效判断错误。

**修复**：`DependencyInjection.cs` 将 `MemoryCalendarQueryCache` 注册改为 Singleton。

**问题 2：EditorPanel.confirmDelete 未 dropPendingPatch**

**现象**：删除事件后 localStorage 中的 pendingPatch 在下次刷新时 replay，导致事件位置回滚。

**修复**：`confirmDelete` 方法调用 `dropPendingPatch(deleteId)` 清理本地补丁。

### 40.12 重复规则 / 提醒 / 与会者面板 UX 优化

**重复规则优化**：
- 实时中文描述预览（`generateRRuleDescription`）；
- 工作日选择条件仅在 `recurrenceType=1`（每周）时显示；
- 新增"工作日 / 每天 / 清除"快捷按钮。

**自定义提醒优化**：
- 分钟输入 + 天/小时转换提示；
- 邮件提醒但未添加与会人时显示警告。

**与会者面板优化**：
- 邮箱格式校验（正则 `/^[^\s@]+@[^\s@]+\.[^\s@]+$/`）；
- "全部无效"和"部分无效"提示；
- "清空"按钮，显示"N 位与会者"；
- 添加与会者后输入框保持焦点（`attendeeInputRef` + `requestAnimationFrame`）。

### 40.13 P3 前端类型安全加固（39 处 `as any` / `: any` 消除）

**范围**：`store.ts`、`EditorPanel.tsx`、`ListView.tsx`、`SubscriptionPanel.tsx`、`MonthView.tsx` 共 39 处 `as any` / `: any`。

**新增错误处理助手**：

```ts
// store.ts - 通用错误处理助手
export function getErrorMessage(error: unknown): string | undefined {
  if (error instanceof Error) return error.message;
  if (typeof error === 'string') return error;
  if (error && typeof error === 'object' && 'message' in error) {
    const msg = (error as Record<string, unknown>).message;
    if (typeof msg === 'string') return msg;
  }
  return undefined;
}

export function getErrorStatus(error: unknown): number | undefined {
  if (error && typeof error === 'object' && 'status' in error) {
    const s = (error as Record<string, unknown>).status;
    if (typeof s === 'number') return s;
  }
  return undefined;
}

export function getErrorData<T = unknown>(error: unknown): T | undefined {
  if (error && typeof error === 'object' && 'data' in error) {
    return (error as Record<string, unknown>).data as T;
  }
  return undefined;
}
```

**新增类型接口**：

```ts
export interface ConflictItem {
  id?: number;
  title?: string;
  startAt?: string;
  endAt?: string;
}

export interface ApiErrorResponseBody {
  message?: string;
  data?: unknown;
}

export interface ConflictResponseData {
  errorType?: string;
  latest?: CalendarEventDto;
  conflicts?: ConflictItem[];
}
```

**关键替换**：

| 原写法 | 替换为 | 涉及文件 |
|---------|--------|----------|
| `catch (e: any)` | `catch (e: unknown)` + `getErrorMessage(e)` / `getErrorStatus(e)` / `getErrorData(e)` | store.ts / EditorPanel.tsx / ListView.tsx / SubscriptionPanel.tsx |
| `(c: any) => c.title`（冲突数据） | `ConflictItem` 接口 + 类型推断 | MonthView.tsx / EditorPanel.tsx / ListView.tsx |
| `calendarApi.create(createData as any)` | `toCreateRequest(snap): CreateCalendarEventRequest`（剥离只读字段） | store.ts |
| `JSON.parse(raw)` 无校验 | 类型守卫 `Array.isArray(parsed)` + 字段类型检查 | store.ts `readPendingPatches` |
| `extras/reminderType as any` | `Record<string, unknown>` + 类型守卫 | store.ts |

**toCreateRequest 函数**（从 `CalendarEventDto` 剥离只读字段，生成符合 `CreateCalendarEventRequest` 的请求体）：

```ts
function toCreateRequest(snap: CalendarEventDto): CreateCalendarEventRequest {
  const {
    id: _id, eventTypeLabel: _etl, conversationSubject: _cs, mailboxName: _mn,
    userName: _un, status: _st, statusLabel: _sl, deletedAt: _da,
    createdAt: _ca, updatedAt: _ua, createdBy: _cb, createdByName: _cbn,
    recurrenceParentId: _rpi, recurrenceOriginalStart: _ros,
    isReminderSent: _irs, taskOwnerName: _ton, rowVersion: _rv,
    ...createData
  } = snap;
  // void 所有剥离字段，避免 TS6133
  void _id; void _etl; void _cs; void _mn; void _un; void _st; void _sl;
  void _da; void _ca; void _ua; void _cb; void _cbn; void _rpi;
  void _ros; void _irs; void _ton; void _rv;
  return createData;
}
```

**readPendingPatches 类型守卫**：

```ts
function readPendingPatches(): PendingPatch[] {
  try {
    const raw = localStorage.getItem(_pendingPatchKeyForUser());
    if (!raw) return [];
    const parsed: unknown = JSON.parse(raw);
    if (!Array.isArray(parsed)) return [];
    const arr = parsed as unknown[];
    const nowMs = Date.now();
    return arr.filter((p): p is PendingPatch => {
      if (!p || typeof p !== 'object') return false;
      const obj = p as Record<string, unknown>;
      return typeof obj.eventId === 'number'
        && typeof obj.appliedAtMs === 'number'
        && nowMs - obj.appliedAtMs <= PENDING_PATCH_TTL_MS;
    });
  } catch (e) {
    console.warn('[calendar] pendingPatch read failed:', e);
    return [];
  }
}
```

### 40.14 extractRecurringInstances 减少代码重复

**问题**：`store.ts` 中 undo/redo 重复事件偏移逻辑代码重复。

**修复**：抽取 `shiftRecurringInstances` 函数，统一处理重复实例的偏移计算。

### 40.15 生产发布（2026-08-25）

按 §18.11 / §39.5 的 7 步生产发布 SOP 执行多轮发布：
1. `tsc --noEmit` + `vite build` 验证通过；
2. `robocopy` 同步 `frontend/dist` → `Production/frontend/dist` / `Production/backend/wwwroot` / `Production/_publish_backend/wwwroot`；
3. 重启 `vite preview --port 5172`；
4. 后端通过环境变量 `ConnectionStrings__SqliteConnection` 指向 `C:\noneadcss\Production\data\noneadcss.db` 启动（端口 5009）；
5. Nginx 反向代理（端口 5173）启动；
6. 三端口 LISTENING 验证：5009 / 5172 / 5173。

### 40.16 验证矩阵

| 修复点 | 验证方式 | 状态 |
|--------|----------|------|
| 拖拽色块重复 | 月视图拖拽事件，观察新位置无重复色块 | ✅ |
| 删除色块残留 | 删除事件，观察色块立即消失 | ✅ |
| 跨日拖拽偏移 | 跨日期拖拽，观察位置正确 | ✅ |
| undo/redo 回滚 | undo 失败后观察 UI 还原 | ✅ |
| SignalR 重连刷新 | 断网恢复后观察数据自动刷新 | ✅ |
| 垃圾箱真实 API | 恢复/永久删除/清空操作生效 | ✅ |
| 订阅链接真实 URL | 重新生成链接返回真实 URL | ✅ |
| 重复事件展开 | 每月重复 4 次显示所有实例 | ✅ |
| 与会者邮箱同步 | 添加与会者后邮件提醒收件人正确 | ✅ |
| 提醒类型选择 | 下拉选择站内/邮件/短信生效 | ✅ |
| RRULE 中文预览 | 选择重复规则后显示中文描述 | ✅ |
| 创建者显示 | 悬停色块显示创建者名称 | ✅ |
| 缓存生命周期 | 删除事件刷新后不重现 | ✅ |
| 类型安全 | `tsc --noEmit` 零 `any` 报错 | ✅ |

### 40.17 遗留风险

| 风险 | 等级 | 缓解建议 |
|------|------|----------|
| 重复事件 undo/redo 的 `endAt` 恢复仍依赖父事件 DTO，若父事件被并发修改可能仍出现偏移 | 低 | 后续可在 undo/redo 响应中返回完整 latest DTO |
| SignalR 最大重连次数后无自动恢复机制（需用户手动刷新页面） | 中 | 后续可增加"连接断开"全局横幅 + 手动重连按钮 |
| 触摸设备的拖拽支持有限（chips.tsx 仅支持鼠标事件） | 低 | 后续可补充 touch 事件处理 |

---

## 41. 2026-08-26 知识库模块重大升级：双模式编辑 + FTS5 + SVG + 附件 + 草稿

> 本章节记录 v2.2.4 发布后知识库模块的全面升级：Markdown / HTML 双模式编辑器切换、marked + highlight.js 替换自定义 Markdown 解析（代码语法高亮）、DOMPurify 放行 SVG 标签（支持 flowcharts.html 风格的原生 SVG 图表在知识库文章内直接渲染）、SQLite FTS5 全文索引 + bm25 排序 + snippet 片段高亮、附件表（KnowledgeArticleAttachments）支持 PDF 等文件上传 + 编辑页附件管理（预览/删除按钮常驻）、localStorage 草稿自动保存 + 冲突检测 + 图片粘贴上传（剪贴板 → FormData → 后端保存 → Markdown/HTML 插入）、全局搜索 UI + 防抖。

### 41.1 后端：附件表 + ContentType 字段

**数据库 Schema 升级**（Migration `20260825150140_AddKnowledgeArticleAttachmentsAndContentType`）：
- `KnowledgeArticles` 表新增 `ContentType` 列（TEXT NOT NULL，默认 `markdown`，值域：`markdown` / `html`），用于区分文章正文的存储格式；
- 新建 `KnowledgeArticleAttachments` 表（BLOB PK `Id`、`ArticleId` FK、`FileName`、`ContentType` MIME、`SizeBytes`、`StoredPath`、`UploadedByUserId`、`CreatedAt` / `UpdatedAt` / `DeletedAt`），支持单文章多附件。

**实体 / DTO / 仓储接口变更**：
- `KnowledgeArticle.cs`：新增 `ContentType`（string）和 `Attachments`（ICollection<KnowledgeArticleAttachment>）导航属性；
- `IKnowledgeArticleRepository.cs`：新增 `FullTextSearchAsync` 接口签名（见 §41.3）；
- `KnowledgeDto.cs`：KnowledgeSearchResult 新增 `Snippet`（高亮片段 HTML，由 FTS5 snippet() 返回）与 `Rank`（bm25 得分，double）字段；
- `MappingExtensions.cs`：`ToDto` 扩展方法支持 snippet/rank 参数传递。

### 41.2 KnowledgeService：附件 CRUD + 内容格式路由

`KnowledgeService.cs` 新增/扩展方法：

| 方法 | 职责 |
|------|------|
| `CreateAttachmentAsync / DeleteAttachmentAsync / GetAttachmentsByArticleAsync` | 附件增删查；删除校验上传者（或超管）权限，软删除后同步清理物理文件 |
| `SearchAsync` | 路由逻辑：优先走 `FullTextSearchAsync`（FTS5）；若 SQLite 运行时未启用 FTS5 则降级为 `WHERE Title LIKE ... OR Content LIKE ...` 的经典查询，保证运行时兼容性 |
| 正文写入分支（Create/Update） | 根据 `article.ContentType` 选择 HtmlSanitizer（HTML 格式）或 Markdown 安全清洗策略；MD 格式保留原文，渲染层交给前端 marked |

**安全约束**（服务层显式校验）：
- 附件大小上限 25 MB（`file.Length > 25 * 1024 * 1024 → BadRequest`）；
- MIME 白名单：`application/pdf`、`image/*`（png/jpeg/gif/webp/svg+xml）、`text/plain`、`application/msword`、`application/vnd.openxmlformats-officedocument.*`；
- 文件名路径穿越防护：`Path.GetFileName(fileName)` + 仅 ASCII + 中文 + `.`/`-`/`_` 字符；

### 41.3 后端：SQLite FTS5 全文索引

**Migration `20260825153000_AddKnowledgeArticlesFts5.cs`**（Dapper 原生 SQL，避免 EF Core 不支持虚拟表）：

```csharp
// 创建 FTS5 虚拟表（tokenize = unicode61 + remove_diacritics 2，支持中文分词字符 + 去音标）
var tokenize = "unicode61 \"remove_diacritics\" \"2\"";
connection.Execute($@"
  CREATE VIRTUAL TABLE IF NOT EXISTS KnowledgeArticlesFts
  USING fts5(Title, Content, Tags, Summary,
             content='KnowledgeArticles',
             content_rowid='Id',
             tokenize='{tokenize}');");
```

**三触发器（写后维护）**：INSERT / UPDATE / DELETE 后自动同步内容到 FTS 索引；UPDATE 使用 `DELETE + INSERT` 语义，保持全文索引与主表一致性。

**查询实现 `KnowledgeArticleRepository.FullTextSearchAsync`**：
- 主查询 `SELECT fts.rowid, bm25(KnowledgeArticlesFts) as Rank, snippet(KnowledgeArticlesFts, 1, '<mark>', '</mark>', '…', 24) as Snippet FROM KnowledgeArticlesFts WHERE KnowledgeArticlesFts MATCH @query ORDER BY Rank LIMIT @skip,@take`；
- 权限过滤（邮箱隔离 + 状态筛选 + DeletedAt IS NULL）通过子查询 JOIN 主表实现，避免绕过 RLS；
- **降级机制**：FTS5 查询抛 `SqliteException` 时（`SQLite Error 1: no such module: fts5`），catch 后回退到 `LIKE` 查询 + 手工截取前后 40 字做伪 snippet，保证老库无 FTS5 也能正常响应。

### 41.4 前端：marked + highlight.js 替换自定义 Markdown 解析

**`Knowledge.tsx` 依赖升级**（package.json 已收录）：
- `marked@^18.0.11`：AST 级 Markdown 解析（替代原手写 10+ 条正则替换，修复嵌套列表/表格/围栏代码错位问题）；
- `highlight.js@^11.12.0`：代码块语法高亮（`hljs.highlightAll()` 在 marked 渲染后 `useEffect` 触发）。

**自定义 marked renderer**（保持知识库原有 UI 风格）：
```ts
// Knowledge.tsx - useMemo 初始化 marked
marked.setOptions({ breaks: true, gfm: true });
const renderer = new marked.Renderer();
renderer.heading = (text, level) => {
  const id = slugify(text); // 保持锚点跳转行为
  return `<h${level} id="${id}" class="kb-heading kb-h${level}">${text}</h${level}>`;
};
renderer.code = (code, lang) => {
  const validLang = hljs.getLanguage(lang || '') ? lang : 'plaintext';
  return `<pre class="hljs kb-code-block"><code class="language-${validLang}">${escapeHtml(code)}</code></pre>`;
};
marked.use({ renderer });
```

### 41.5 DOMPurify 放行 SVG 标签（支持原生 SVG 图表渲染）

**背景**：用户要求知识库文章 HTML/Markdown 预览能直接渲染 `docs/flowcharts.html` 风格的原生 SVG（含 `<g> <path> <rect> <text> <defs> <style>` 等），而默认 DOMPurify 会把 `<svg>` 整体剥离。

**实现**（`Knowledge.tsx`）：
```ts
const SVG_ALLOWED_TAGS = ['svg','g','path','rect','circle','ellipse','line','polyline','polygon','text','tspan','defs','style','use','symbol','linearGradient','radialGradient','stop','clipPath','mask','pattern','marker','filter','feGaussianBlur','feOffset','feMerge','feMergeNode','title','desc'];
const SVG_ALLOWED_ATTR = ['id','class','style','viewBox','width','height','x','y','d','cx','cy','r','rx','ry','x1','y1','x2','y2','points','fill','stroke','stroke-width','stroke-linecap','stroke-linejoin','fill-opacity','stroke-opacity','opacity','transform','translate','scale','rotate','text-anchor','font-size','font-family','font-weight','dx','dy','href','xlink:href','offset','stop-color','gradientUnits','gradientTransform','patternUnits','clipPathUnits','markerUnits','markerWidth','markerHeight','refX','refY','orient'];
```

**两处渲染函数同步放开**：
- `renderMarkdown`：marked 产出 HTML 字符串 → DOMPurify.sanitize(html, { ADD_TAGS: SVG_ALLOWED_TAGS, ADD_ATTR: SVG_ALLOWED_ATTR, ADD_DATA_URI_TAGS: ['image'] })；
- `renderHtml`：用户直接输入的 HTML 正文 → 同上配置；**style 标签放行**是为了让 SVG `<defs><style>` 里的 class 选择器（flowcharts.html 页面级样式语法）生效，避免 SVG 失去配色。

### 41.6 前端：MD / HTML 双模式编辑器切换

编辑器顶部新增「格式切换」Tab：Markdown（默认） | HTML

**状态机**：
```
[MD Tab] 编辑 Markdown 源码 → 切换到 HTML → marked.render(md) 结果作为 HTML 初始值（用户确认弹窗）
[HTML Tab] 编辑 HTML 源码  → 切换到 MD → Turndown（未启用，保留确认弹窗，避免信息丢失）
```

**预览面板**始终按 `formState.contentType` 路由渲染（Markdown 走 marked，HTML 走 innerHTML + DOMPurify），保证「编辑所见即预览」。

### 41.7 前端：附件管理（编辑页常驻 + 预览 + 删除）

位置：文章编辑器底部，「上传附件」按钮下方（独立卡片）。

**UI 关键设计**：
```tsx
<div className="space-y-2">
  <div className="flex items-center gap-2 pt-1">
    <div className="w-1.5 h-1.5 rounded-full bg-slate-400" />
    <span className="text-xs font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wide">
      已上传附件 ({editorAttachments.length})
    </span>
  </div>
  {editorAttachments.map((att) => (
    <div key={att.id} className="group flex items-center gap-3 px-3.5 py-2.5 rounded-xl bg-slate-50 dark:bg-slate-800/60 border border-slate-200 dark:border-slate-700/60">
      {/* 图标 + 文件名 + 大小 */}
      <div className="flex-1 min-w-0">…</div>
      {/* 预览 + 删除 按钮：始终可见，非 hover */}
      <div className="flex items-center gap-1.5 flex-shrink-0">
        <button onClick={() => handlePreviewAttachment(att)}
          className="p-2 rounded-lg text-slate-500 hover:text-slate-700 hover:bg-slate-100 dark:text-slate-400 dark:hover:text-slate-200 dark:hover:bg-slate-700/60">
          <EyeIcon size={15} />
        </button>
        <button onClick={() => handleDeleteAttachment(att.id)}
          className="p-2 rounded-lg text-rose-400 hover:text-rose-600 hover:bg-rose-50 dark:hover:bg-rose-500/10">
          <Trash2 size={15} />
        </button>
      </div>
    </div>
  ))}
</div>
```

**行为**：
- 附件上传成功后 `editorAttachments` 状态立即追加，避免刷新后丢失；
- 预览：`window.open(api/kb/attachments/{id}/download)`（新标签页）；
- 删除：带二次确认弹窗，调用后端 `DeleteAttachmentAsync`，成功后本地状态同步移除。

### 41.8 前端：草稿自动保存 + 冲突检测

**触发时机**：
- 表单任意字段变更（title/summary/content/tags/状态/分类/附件）后 2 秒防抖；
- 页面关闭（beforeunload）同步写入；
- 切换模态窗 Tab 时立即保存；

**存储结构**（localStorage 按文章 + 用户隔离）：
```
key  = kb_draft_{articleId ?? 'new'}_{userId}
data = { savedAtMs, formState, attachmentIds }
TTL  = 7 天（读取时按 savedAtMs 过期判定，过期自动丢弃）
```

**冲突检测（编辑已存在文章）**：
- 进入编辑器：先拉取服务器最新 `updatedAt`（`serverUpdatedAt`），若本地草稿 `savedAtMs > serverUpdatedAt.getTime()` 则弹出冲突选择器：
  1. **使用本地草稿**：草稿内容回填表单；
  2. **使用服务器版本**：丢弃草稿，用服务器值填充。
- 保存成功：`localStorage.removeItem(key)`，避免后续编辑继续加载旧草稿。

### 41.9 前端：图片粘贴上传（剪贴板 → 后端 → Markdown/HTML 自动插入）

**绑定**：`Knowledge.tsx` 编辑器根 div `onPaste={handleEditorPaste}`。

**处理链**：
1. `e.clipboardData.files` 过滤 type 为 `image/*` 且 size ≤ 5MB；
2. FormData 包装 + POST `/api/knowledge/articles/attachments/upload`（复用附件上传接口，`isEmbedded=true` 标识为内嵌图片，便于后期追溯）；
3. 成功后根据当前 contentType 自动插入到光标位置：
   - **Markdown**：`![${fileName}](${downloadUrl})` + 光标位移到末尾；
   - **HTML**：`<img src="${downloadUrl}" alt="${fileName}" />`；
4. 失败 Toast：`toast.error('图片上传失败：' + getErrorMessage(e))`，且不破坏用户已选中的剪贴板文本。

### 41.10 全局搜索 UI（侧边栏搜索框 + 防抖 + 结果页）

**位置**：侧边栏顶部「🔍 全局搜索」（与侧边栏 Logo 同级）。

**行为**：
- 输入 ≥ 3 字符触发 350ms 防抖 → 调用 `GET /api/knowledge/search?keyword=`；
- 命中结果：下拉显示 Top 10（标题 + snippet 高亮片段 + 分类 + 更新时间），点击跳转到知识库文章详情页并自动定位；
- 空结果：显示「未找到匹配的知识库文章」占位；
- 移动端：搜索框折叠为搜索图标按钮，点击展开搜索 Modal。

**实现细节**（避免组件渲染冲突）：
- 搜索结果高亮 `<mark>` 标签在 CSS 中声明独立 `kb-search-mark { background:#fde68a; padding:0 2px; border-radius:2px; }`，与主色调不冲突；
- 搜索请求通过 `AbortController` 在用户继续输入时取消上一个未完成请求，避免"慢请求覆盖快请求"。

### 41.11 数据库迁移冲突解决（历史迁移不匹配）

**背景**：生产库 `Production/data/noneadcss.db` 存在 24 个历史迁移记录与代码基线 Migration ID 不一致，直接 `dotnet ef database update` 抛：
```
SQLite Error 1: 'duplicate column name: IsSystem'
```

**修复方案（v2.2.5 SOP）**：
1. `dotnet ef migrations script 20260825150139_AddCalendarEventIndexes 20260825153000_AddKnowledgeArticlesFts5 -o _tmp_migration.sql --idempotent` 生成增量脚本；
2. `Production/stop-prod.ps1` 停后端（避免写入）；
3. 先备份 `cp Production/data/noneadcss.db Production/data/noneadcss.db.bak_YYYYMMDD`；
4. PowerShell 按顺序加载 SQLitePCLRaw DLL → 打开 `SqliteConnection` → 逐行执行生成的 SQL 脚本；
5. 验证：`SELECT * FROM __EFMigrationsHistory` 包含 20260825150140 与 20260825153000 两条；
6. `Production/start-prod.ps1` 启动。

### 41.12 编译修复（TypeScript TS6133 未使用变量）

升级知识库过程中顺带修复 `frontend/src/pages/TimeTracking.tsx` 的 TS6133 错误：
- 移除未使用的 `CalendarIcon / FileSearch / X` 导入；
- 移除未使用的 `ENTRY_TYPE_BADGE` 常量；
- 同时联动修复 `frontend/src/hooks/useTimedToast.ts` 和 `useCrudPage.ts` 中 `ToastMessage` 类型定义不一致（`type` 字段值域从 `success|error` 扩展为 `success|error|info|warning`），避免升级知识库时复用 Toast 工具的跨组件类型报错。

### 41.13 生产发布（2026-08-26）

按 §18.11 的 7 步生产发布 SOP 执行：
1. `tsc --noEmit` + `dotnet build NoneadCSS.Api` 零错误；
2. `Production/stop-prod.ps1` 停止 5009/5172/5173 端口监听进程；
3. `robocopy Production _backup_prod_20260826_XXXXXX /MIR`；
4. `dotnet publish NoneadCSS.Api.csproj -c Release -o Production/_publish_backend` → robocopy 同步到 `Production/backend/`；
5. 前端 `pnpm build` → robocopy `frontend/dist` 到 `Production/frontend/dist` + `Production/backend/wwwroot` + `Production/_publish_backend/wwwroot`；
6. 数据库：PowerShell 执行 §41.11 生成的增量迁移 SQL（KnowledgeArticleAttachments + ContentType + FTS5 虚拟表 + 三触发器）；
7. `Production/start-prod.ps1`（blocking=false，`ConnectionStrings__SqliteConnection` 显式指向生产库绝对路径）；
8. 健康检查：`:5009/health` Healthy、`:5173/` 200 含 `<title>`、`GET /api/knowledge/search?keyword=测试` HTTP 200 返回数组。

### 41.14 验证矩阵

| 能力 | 验证方式 | 状态 |
|------|----------|------|
| MD→Markdown 渲染 | 输入表格/围栏代码/嵌套列表，预览样式正确 | ✅ |
| HTML→HTML 渲染 | 输入 `<h1>测试</h1>`，预览渲染 H1 字号 | ✅ |
| 代码语法高亮 | 输入 ```ts const x = 1;```，关键字着色 | ✅ |
| SVG 图表支持 | 粘贴 flowcharts.html 中某 `<svg>` 片段，预览渲染线条与文字 | ✅ |
| FTS5 搜索 | 搜索"退款流程"，结果 snippet 显示 `<mark>` 高亮 | ✅ |
| FTS5 降级 | 移除虚拟表后手动验证 LIKE 兜底，仍返回列表 | ✅ |
| 附件上传 | 上传 10MB PDF，"已上传附件"列表显示条目 | ✅ |
| 附件预览 | 点击 EyeIcon，新标签页下载 PDF | ✅ |
| 附件删除 | 点击 Trash2，确认后条目消失，404 访问 | ✅ |
| 草稿保存 | 编辑标题后关闭页面，重新打开 → 回填 | ✅ |
| 草稿冲突 | 手动篡改 serverUpdatedAt，出现冲突选择弹窗 | ✅ |
| 图片粘贴 | Word/截图复制，编辑器内粘贴，自动插入 Markdown | ✅ |
| 全局搜索 | 侧边栏搜索，点击跳转文章详情 | ✅ |
| TS 类型检查 | `tsc --noEmit` 零错误 | ✅ |
| 后端编译 | `dotnet build` 零警告零错误 | ✅ |

### 41.15 遗留风险与后续优化

| 风险 / 优化点 | 等级 | 说明 |
|------|------|------|
| FTS5 unicode61 tokenizer 不支持中文按词切分（只能按字符） | 中 | 搜索"产品安装"能匹配，但短词误匹配率高；后续可接入 Jieba.NET + 自定义 FTS5 tokenizer |
| 草稿保存仅在前端 localStorage，多设备/多浏览器不同步 | 低 | 后续可把草稿实体化到 `KnowledgeArticleDrafts` 表按 userId 存后端 |
| SVG `<script>` 标签虽被 DOMPurify 屏蔽，但 `<use href=data:>` 类攻击面仍需审计 | 中 | 后续在 SVG_ALLOWED_ATTR 中移除 href/xlink:href 并添加单测覆盖 |
| highlight.js 默认加载 40+ 语言 → 体积增加 ~200KB | 低 | 后续可改为 `hljs.registerLanguage` 按需注册 JS/TS/SQL/Shell/CSS 常用语言 |
| 附件物理存储路径在本地磁盘，无副本 + 无 CDN | 中 | 后续可抽象 `IAttachmentStorage` 接口，实现 S3 / OSS Provider |

---

**NoneadCSS** - 拓德客户服务与支持系统
*版本 2.2.7 · 2026-08-26 · 日历权限修复（T000092）+ 时间追踪模块修复 + UI 风格统一 + 知识库模块重大升级 + 日历多轮稳定性修复*

---

## 42. 2026-08-26 时间追踪模块修复 + UI 风格统一

> 本章节记录 v2.2.6 版本的变更：修复停止计时数据丢失 Bug、修复计时器实时显示不运行 Bug、关联工单/客户改为下拉菜单联动、全页面样式对齐知识库简洁风格。

### 42.1 停止计时数据丢失修复（P0）

**问题描述**：用户在时间追踪概览页点击"停止计时"后，前端报错"请求参数无效，请检查输入"。

**根因分析**：`TimeEntryService.StopTimerAsync` 方法在修改 `entry.EndTime` 和 `entry.DurationMinutes` 后**未调用 `SaveChangesAsync`** 持久化到数据库。EF Core 的 ChangeTracker 虽然跟踪了属性变更，但没有 `SaveChanges` 调用，数据库中 EndTime 仍为 null。同样的问题存在于 `UpdateAsync` 方法中。

**修复方案**：

```csharp
// StopTimerAsync（行 ~250）
entry.EndTime = now;
entry.DurationMinutes = CalculateDuration(entry.StartTime, now);
entry.UpdatedAt = now;
await _unitOfWork.TimeEntries.UpdateAsync(entry);  // 新增：持久化
await LogAuditAsync("time_entry.timer_stop", entry, oldValues);

// UpdateAsync（行 ~195）
entry.EndTime = endTime;
entry.DurationMinutes = durationMinutes;
await _unitOfWork.TimeEntries.UpdateAsync(entry);  // 新增：持久化
await LogAuditAsync("time_entry.update", entry, oldValues);
```

仓储层 `TimeEntryRepository.UpdateAsync` 内部调用 `SaveChangesAsync` 完成数据库写入。

**影响文件**：`NoneadCSS.Application/Services/TimeEntryService.cs`

### 42.2 计时器实时显示修复（P1）

**问题描述**：计时器在计时时，页面上"已用时长"始终显示 `00:00:00`，时间不运行。

**根因分析**：`TimeTracking.tsx` 中 `const [, setTick] = useState(0)` 丢弃了 tick 状态值（只取 setter）。`liveSeconds` 的计算只依赖 `Date.now()` 和 `activeTimer.startTime`，未引用 tick 值。React 18 的自动批处理优化检测到 `liveSeconds` 的依赖项未变化（`activeTimer` 引用不变），跳过了 DOM 更新。

**修复方案**：

```tsx
// 修复前
const [, setTick] = useState(0);
const liveSeconds = activeTimer
  ? (Date.now() - new Date(activeTimer.startTime).getTime()) / 1000
  : 0;

// 修复后
const [tick, setTick] = useState(0);
const liveSeconds = activeTimer
  ? (Date.now() - new Date(activeTimer.startTime).getTime()) / 1000 + tick * 0
  : 0;
```

`+ tick * 0` 不影响计算结果，但让 React 识别 `liveSeconds` 依赖 `tick`，每秒 `setTick` 后必然重渲染并更新 DOM。

**影响文件**：`frontend/src/pages/TimeTracking.tsx`

### 42.3 关联工单/客户改为下拉菜单联动

**需求**：新建记录-手动录入中，关联工单 ID 和关联客户 ID 从手动输入改为下拉菜单，选中工单后自动填充客户。

**实现**：

- **关联工单下拉**：`<select>` 元素，选项来自 `conversationApi.getActiveConversations()`（status=1 活跃会话），格式 `#会话ID - 主题内容 - 客户姓名`。
- **关联客户下拉**：`<select>` 元素，选项来自 `customerApi.getCustomers()`（最多 100 条），格式 `#客户ID - 客户姓名`。
- **联动逻辑**：选中工单后，从 `activeConversations` 列表中找到该会话的 `customerId`，若存在则自动填充到 `formData.customerId`。
- **懒加载**：`openCreate` 时触发 `loadRelationOptions()`，并行拉取会话和客户数据，避免页面初始化时的额外请求。
- **加载状态**：`optionsLoading` 期间下拉禁用并显示"加载中…"；空列表显示"暂无活跃会话/暂无客户"。
- **编辑模式**：编辑已有记录时隐藏这两个字段（保持原逻辑）。

**影响文件**：`frontend/src/pages/TimeTracking.tsx`（+70 / -16 行）

### 42.4 全页面 UI 风格对齐知识库

**设计目标**：移除时间追踪模块所有"花哨"视觉元素，统一为知识库页面的简洁风格。

**变更清单**：

| 区域 | 修改前 | 修改后 |
|---|---|---|
| 样式常量 | 渐变背景 + 重 ring + hover 变色 | `bg-white border-slate-200 rounded-xl shadow-sm` |
| Header | 三色渐变 + 装饰光晕 + 网格纹理 | `bg-white border-slate-200 rounded-2xl shadow-sm` |
| 统计卡片 | `rounded-3xl` + 3 装饰层 + `hover:-translate-y-1.5` | `rounded-2xl` + `hover:shadow-md` + 纯色图标 |
| 运行中计时器 | 渐变 + 光晕 + 流动网格 + `animate-ping` | `bg-blue-50 border-blue-200 rounded-2xl` |
| 快速开始 | 三色渐变按钮 | `bg-emerald-500 rounded-xl` |
| Tab 导航 | 渐变容器 + 渐变滑块 | `bg-slate-100` + `bg-white shadow-sm text-blue-600` |
| 图表/列表/分页 | `rounded-3xl` 渐变 | `rounded-2xl bg-white border-slate-200 shadow-sm` |
| 模态框类型切换 | `border-2 border-primary-500 bg-primary-50` | `bg-blue-600 text-white` / `bg-slate-100 text-slate-600` |

**清理项**：移除所有 `animate-ping`、`blur-3xl`、`hover:-translate-y`、`backdrop-blur`、`shadow-2xl/xl` 装饰；保留 LIVE 徽章和 Skeleton 的 `animate-pulse`（功能性动画）。

**影响文件**：`frontend/src/pages/TimeTracking.tsx`（仅 className 变更，无逻辑改动）

---

## 43. 2026-08-26 日历权限修复（T000092）

> 本章节记录 v2.2.7 版本的变更：修复客服（Agent）和客服主管（Supervisor）角色在日历管理模块中无法添加日程的权限缺陷。

### 43.1 问题描述

在「功能模块 → 日历管理」中，客服和客服主管角色无法添加、编辑或删除日历事件。预期所有用户都存在添加日程权限，但实际只有管理员（SuperAdmin）可以执行这些操作。

### 43.2 根因分析

日历写操作权限链路涉及三个层面：

1. **数据库种子文件** (`DatabaseSeeder.cs`)：定义各角色的 `PermissionsJson` 数组
2. **控制器权限特性** (`CalendarController.cs`)：写操作端点标注 `[Permission("calendar.manage")]`
3. **前端权限判断** (`usePermission.ts`)：通过 `hasPermissionKey('calendar.manage')` 控制按钮显隐

**根因**：`DatabaseSeeder.cs` 中 Agent 和 Supervisor 角色的 `PermissionsJson` 仅包含 `calendar.view`（只读），缺少 `calendar.manage`（写操作）。后端 `CalendarController` 的创建、更新、删除接口均标注 `[Permission("calendar.manage")]`，导致这两个角色调用时返回 403 Forbidden。

### 43.3 修复方案

在 `DatabaseSeeder.cs` 的 **4 处位置** 为 Agent 和 Supervisor 角色添加 `"calendar.manage"` 权限项：

| 位置 | 说明 | 行号 |
|------|------|------|
| Supervisor 初始种子 | 新系统初始化时的 Supervisor 角色权限 | L948 |
| Agent 初始种子 | 新系统初始化时的 Agent 角色权限 | L966 |
| Supervisor 更新逻辑 | 已存在数据库的 Supervisor 权限补全 | L1000 |
| Agent 更新逻辑 | 已存在数据库的 Agent 权限补全 | L1014 |

**修复前**（Agent 权限片段）：
```json
["conversations.view", ..., "calendar.view", ...]
```

**修复后**（Agent 权限片段）：
```json
["conversations.view", ..., "calendar.view", "calendar.manage", ...]
```

### 43.4 权限矩阵

修复后各角色的日历权限如下：

| 角色 | 查看 | 创建/编辑/删除 | 垃圾箱 |
|------|------|---------------|--------|
| SuperAdmin | ✅ 全部 | ✅ 任意 | ✅ |
| Supervisor | ✅ 含下属 | ✅ 下属和自己 | ❌ |
| Agent | ✅ 仅自己 | ✅ 自己的 | ❌ |
| ReadOnly | ✅ 仅自己 | ❌ | ❌ |

### 43.5 影响文件

| 文件 | 变更 |
|------|------|
| `NoneadCSS.Api/DatabaseSeeder.cs` | Agent/Supervisor 的 `PermissionsJson` 添加 `"calendar.manage"`（4 处） |

### 43.6 验证

- ✅ 数据库查询确认 Agent 和 Supervisor 角色 `PermissionsJson` 包含 `calendar.manage`
- ✅ `dotnet publish` 编译成功，零错误
- ✅ 后端服务（5009）健康检查通过
- ✅ 前端 Vite Preview（5172）正常运行
- ✅ Nginx 代理（5173）正常转发

---

## 44. 2026-08-27 日历提醒修复 T000090 + 6 项日历模块 Bug 修复

> 本章节记录 2026-08-27 日历模块的两批修复：T000090 提醒触发修复 + 6 项日历模块深度审查 Bug 修复（BUG-01~06）。

### 44.1 日历提醒触发修复（T000090）

**问题描述**：日历事件提醒未按预期触发，客服离开 `/calendar` 页面后无法收到提醒弹窗。

**根因分析**：
1. 后端 `CalendarReminderService` 未检查 `ReminderType`，对所有通知类型都发送
2. 前端仅在 `/calendar` 页面监听 SignalR 提醒事件，离开页面后提醒丢失
3. 短信网关未实现

**修复方案**：
- `CalendarReminderService.cs`：按 `ReminderType` 分发通知（短信降级为站内通知）
- 新增 `GlobalCalendarReminderListener.tsx`：在 `App.tsx` 全局监听提醒，不依赖当前路由
- `Calendar.tsx`：移除重复的提醒处理代码，简化为仅处理页面内的 UI 交互
- 新增 URL 参数 `?focusEventId=N`：支持从提醒弹窗跳转到日程详情并自动打开

### 44.2 日历模块 6 项 Bug 修复（BUG-01~06）

| 编号 | 优先级 | 问题描述 | 修复方案 |
|------|--------|----------|----------|
| BUG-01 | P0 | 邮件模板"查看详情"链接使用相对路径，邮件客户端无法跳转 | `CalendarReminderService.cs` 改用 `_brand.FrontendUrl` 拼接绝对 URL |
| BUG-02 | P0 | `CalendarMutationService.cs` 重复事件拆分条件逻辑反转，Following 模式重复日程被错误拆分 | 修正条件判断逻辑 |
| BUG-03 | P0 | `CalendarMutationService.DuplicateAsync` 缺少 `ReminderType` 等字段映射，复制日程数据丢失 | 补充 `ReminderType / ReminderMinutesBefore / IsRecurring / RecurrenceRule` 字段映射 |
| BUG-04 | P1 | `QuickUpdateAsync` 不支持 `ReminderType` 字段更新 | 扩展快速更新接口支持 `ReminderType` |
| BUG-05 | P1 | 提醒设置变更后未重置发送状态，提醒无法重发 | 变更提醒设置时重置发送状态 |
| BUG-06 | P1 | 提醒弹窗"查看详情"导航时 ACK 丢失，提醒重复弹出 | 修正 ACK 发送时序，确保导航前完成 ACK |

### 44.3 影响文件

| 文件 | 变更 |
|------|------|
| `CalendarReminderService.cs` | 按 ReminderType 分发 + 绝对 URL + 重置发送状态 |
| `CalendarMutationService.cs` | 修正拆分条件 + 补全 DuplicateAsync 字段映射 |
| `CalendarController.cs` | QuickUpdate 支持 ReminderType |
| `GlobalCalendarReminderListener.tsx` | 新增：全局提醒监听 |
| `Calendar.tsx` | 移除重复提醒处理 + focusEventId 支持 |
| `App.tsx` | 挂载全局提醒监听器 |

### 44.4 部署验证

- ✅ 后端编译成功（0 errors）
- ✅ 前端 `tsc -b && vite build` 成功
- ✅ 部署至 `Production\backend` + `Production\frontend\dist`
- ✅ 后端 :5009 健康检查 `{"status":"Healthy"}`
- ✅ 前端 :5172 正常返回
- ✅ Nginx :5173 反代正常
- ✅ API 路由验证：`/api/calendar/list` → 401（鉴权门正常），`/api/calendar/{id}/quick-update` → 403（权限门正常）

---

## 45. 2026-08-27 测试覆盖率提升：日历前端组件 100% 覆盖

> 本章节记录 2026-08-27 的测试覆盖率提升工作：前端日历组件 `chips.tsx` 实现 100% 覆盖率，后端新增 `CalendarConflictServiceTests` 和 `SlaServiceTests` 回归测试。

### 45.1 前端测试新增

| 测试文件 | 用例数 | 覆盖目标 |
|----------|--------|----------|
| `error.test.ts` | 10 | `getApiErrorMessage` / `formatDateTimeSafe` |
| `format.test.ts` | 22 | `sanitizeFileName` / `safeUrl` / `formatFileSize` 等 |
| `calendar-utils-ext.test.ts` | 42 | 常量 / 工具函数 / ISO 转换 |
| `calendar-hooks.test.ts` | ~40 | `_buildRangeWindow` / `_debounceStore` / `useKeyboardShortcuts` / `useRangeCache` / `useCalendarDrag` / `useSwipeNavigation` |
| `calendar-store.test.tsx` | ~43+31+19 | `getEventTypeStyle` / `getStatusStyle` / `AllDayEventBar` / `EmptyState` / `HourRow` / memo 比较函数 |

### 45.2 覆盖率演进

| 指标 | 起始 | 最终 |
|------|------|------|
| 语句覆盖率 | 28.21% | **100%** |
| 分支覆盖率 | 95.23% | **100%** |
| 函数覆盖率 | 78.26% | **100%** |
| 行覆盖率 | 28.21% | **100%** |

### 45.3 后端测试新增

| 测试文件 | 用例数 | 覆盖目标 |
|----------|--------|----------|
| `CalendarConflictServiceTests.cs` | 13 | 冲突检测 / 边界用例 / 异常处理 |
| `SlaServiceTests.cs`（BUG-06 回归） | 17 | `targetHours=0` 除零守卫 / NaN 防御 / 正常路径回归 |

### 45.4 最终测试结果

- **后端**：1091 通过（+13），0 失败
- **前端**：178 通过（+64），0 失败
- **覆盖率报告**：`frontend/docs/chips-coverage-100-report.md`

### 45.5 关键技术沉淀

- **ESM mock 策略**：使用 `vi.mock` 配合 `import.original` 实现对模块导出的精确替换
- **强制 ref.current=null**：通过 `elementRef.current = null` 触发早返回分支
- **Handler 捕获法**：通过 `mockImplementation` 捕获传给 Hook 的回调以独立测试
- **SSR 模拟**：通过临时覆盖 `global.window` 为 `undefined` 模拟服务端渲染路径
- **memo 比较函数直接测试**：导出 `_allDayEventBarMemoComparator` / `_hourRowMemoComparator` 并直接断言，避免 React.Profiler 副作用

---

## 46. 2026-08-27 全项目功能审查：8 项 Bug 修复（BUG-01~08）

> 本章节记录 2026-08-27 对全项目核心模块（认证授权 / 邮件 / IM 渠道 / 前端 / 会话 / SLA / 知识库 / 通知）进行功能审查后发现并修复的 8 项 Bug。

### 46.1 修复优先级与概览

| 编号 | 优先级 | 模块 | 问题 | 状态 |
|------|--------|------|------|------|
| BUG-01 | P1 | 客户导入 | 重复检查仅覆盖前 100 条 | ✅ 已修复 |
| BUG-02 | P1 | 导入导出 | 导出含软删除 + 客户截断 100 | ✅ 已修复 |
| BUG-03 | P1 | 报表导出 | CSV/Excel 公式注入 | ✅ 已修复 |
| BUG-04 | P1 | 文件夹管理 | 系统文件夹可被硬删除 | ✅ 已修复 |
| BUG-05 | P2 | 工作流 | set_status 关闭会话绕过触发器 | ✅ 已修复 |
| BUG-06 | P2 | SLA | CalculateStatus 除零风险 | ✅ 已修复 |
| BUG-07 | P2 | 邮件队列 | 无重试 + 孤立中间状态 | ✅ 已修复 |
| BUG-08 | P2 | 导入导出 | 软删除标签干扰重复检查 | ✅ 已修复 |

### 46.2 各 Bug 修复详情

**BUG-01：客户导入重复检查仅覆盖前 100 条**
- 根因：`ImportExportService.cs` 使用 `Take(100)` 限制邮箱查询
- 修复：`CustomerRepository.cs` 新增 `GetAllActiveEmailsAsync` 查询所有未删除客户邮箱（无 Take 限制）

**BUG-02：导出含软删除记录 + 客户截断**
- 根因：导出查询未过滤 `DeletedAt`，客户导出 `Take(100)` 截断
- 修复：显式过滤 `DeletedAt == null`，客户导出 `Take` 提升至 10000

**BUG-03：CSV/Excel 公式注入**
- 根因：导出未对 `= + - @ \t \r` 开头的单元格转义
- 修复：`CustomReportExportHelper.cs` 对危险前缀单元格前缀单引号

**BUG-04：系统文件夹可被硬删除**
- 根因：`FolderService` 未拦截系统文件夹的删除请求
- 修复：拦截 `IsSystemFolder()` 删除请求，改用 `SoftDelete()`

**BUG-05：工作流 set_status 绕过触发器**
- 根因：`SimpleActionHandlers` 的 `set_status` 动作直接修改状态，未触发 `conversation_closed` / `conversation_reopened` 工作流
- 修复：检测关闭/重开状态转换，使用 `PendingSideEffects` 延迟触发，避免事务内循环依赖

**BUG-06：SLA CalculateStatus 除零风险**
- 根因：`targetHours = 0` 时进入除零计算
- 修复：`SlaService.cs` 和 `SlaPolicy.cs` 增加 `targetHours <= 0` 守卫，直接返回 breached

**BUG-07：邮件队列无重试 + 孤立中间状态**
- 根因：(1) 发送失败立即固化为终态 `SendError`，无重试；(2) 进程崩溃时 `SendIntermediateError` 状态的邮件被永远遗弃
- 修复：`SendLogRepository.GetPendingAsync` 同时拾起 `Accepted` + `SendIntermediateError`，加入 `AttemptCount < MaxRetryCount(3)` 过滤；`EmailQueueService` 失败时未达上限重置为 `Accepted`，达上限固化为 `SendError`

**BUG-08：软删除标签干扰导入重复检查**
- 根因：`ImportExportService` 预加载 `existingTagKeys` 时未调用 `IgnoreQueryFilters()`，全局软删除过滤器排除了软删除标签，导致同名标签被误判为"不存在"并创建重复记录
- 修复：预加载时调用 `IgnoreQueryFilters()` 绕过全局软删除过滤器

### 46.3 回归测试

| 测试文件 | 用例数 | 覆盖 Bug |
|----------|--------|----------|
| `EmailQueueServiceTests.cs` | 9 | BUG-07（队列拾起 / 重试 / 终态 / 孤立态恢复） |
| `SlaServiceTests.cs` | 17 | BUG-06（除零守卫 / NaN 防御） |
| `ImportExportServiceBug08Tests.cs` | 6 | BUG-08（软删除标签干扰 / IgnoreQueryFilters 必要性） |

### 46.4 影响文件

| 文件 | 关联 Bug |
|------|----------|
| `CustomerRepository.cs` | BUG-01 |
| `ImportExportService.cs` | BUG-01, BUG-02, BUG-08 |
| `CustomReportExportHelper.cs` | BUG-03 |
| `FolderService.cs` | BUG-04 |
| `SimpleActionHandlers.cs` | BUG-05 |
| `SlaService.cs` | BUG-06 |
| `SlaPolicy.cs` | BUG-06 |
| `SendLogRepository.cs` | BUG-07 |
| `EmailQueueService.cs` | BUG-07 |

### 46.5 最终测试结果

- **后端测试**：1123 通过（BUG-06 +17, BUG-07 +9, BUG-08 +6），5 skipped（无关历史）
- **部署**：2026-08-27 14:02 部署至生产环境
  - 后端 :5009 健康检查 `{"status":"Healthy"}`
  - 前端 :5172 正常
  - Nginx :5173 反代正常
  - API 验证：`/api/calendar/list` → 401，`/api/calendar/{id}/quick-update` → 403

---

## 47. 2026-08-27 系统管理-审计日志完善与优化（v2.2.9）

### 47.1 任务背景

原有审计日志模块功能单薄，仅支持按用户、操作类型、时间范围筛选，且统计指标依赖当前页数据（分页导致统计失真）；缺少 CSV 导出、完整性校验、关联日志等运维追溯能力。本次完善从过滤、统计、导出、完整性、关联五个维度全面增强审计日志模块。

### 47.2 后端实现

#### 47.2.1 多维过滤（统一 ApplyFilters）

`AuditLogRepository.cs` 抽取 `ApplyFilters` 私有方法集中过滤逻辑，被 `GetPagedAsync` / `GetStatsAsync` / `ExportAsync` 三处复用，避免实现漂移：

```csharp
private static IQueryable<AuditLog> ApplyFilters(
    IQueryable<AuditLog> query,
    string? action, string? entityType, int? userId, int? entityId,
    string? ipAddress, string? keyword,
    DateTime? startDate, DateTime? endDate)
{
    if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action);
    if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(a => a.EntityType == entityType);
    if (userId.HasValue) query = query.Where(a => a.UserId == userId.Value);
    if (entityId.HasValue) query = query.Where(a => a.EntityId == entityId.Value);
    if (!string.IsNullOrWhiteSpace(ipAddress)) query = query.Where(a => a.IpAddress == ipAddress);
    if (!string.IsNullOrWhiteSpace(keyword))
        query = query.Where(a => (a.Action.Contains(keyword)) || (a.EntityType.Contains(keyword)));
    if (startDate.HasValue) query = query.Where(a => a.CreatedAt >= startDate.Value);
    if (endDate.HasValue) query = query.Where(a => a.CreatedAt <= endDate.Value);
    return query;
}
```

#### 47.2.2 全局统计聚合

`IAuditLogRepository.GetStatsAsync` 签名与过滤参数完全一致，返回 `AuditLogStats` DTO。实现要点：
- `totalLogs`：`baseQuery.CountAsync()` 全量计数（非当前页）。
- `uniqueUsers`：`baseQuery.Select(a => a.UserId).Distinct().CountAsync()`。
- `last24hCount`：`baseQuery.CountAsync(a => a.CreatedAt >= DateTime.UtcNow.AddHours(-24))`。
- `failedActions`：`baseQuery.CountAsync(a => a.Action.Contains("Delete") || a.Action.Contains("Failed"))`。
- `actionDistribution` / `entityDistribution`：`GroupBy` + `OrderByDescending(Count)` + `Take(10)`。

#### 47.2.3 CSV 导出

`AuditService.ExportCsvAsync` 拼接 CSV 字符串（首行 BOM + 表头 + 数据行），调用 `ExportAsync(maxRows: 10000)` 取数。Controller 端返回 `File(Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", fileName)`，文件名 `audit-logs_yyyyMMdd-HHmmss.csv`。

#### 47.2.4 HMAC-SHA256 完整性校验

- 单条：`AuditHmacSigner.VerifySignatureAsync(log)` 返回 `AuditLogIntegrityResult { isValid, storedSignature, expectedSignature, verifiedAt }`。
- 批量：`VerifyIntegrityAsync(page, pageSize)` 校验指定页全部记录，返回 `AuditIntegrityReport { page, pageSize, totalLogs, checkedCount, tamperedCount, tamperedLogIds[] }`。

#### 47.2.5 关联日志

`GetRelatedAsync(id, limit)` 逻辑：
1. 取目标日志的 `entityType + entityId + userId`。
2. 若 `entityId` 有值 → 查同实体的最近日志（排除自身，按 `CreatedAt DESC`，`Take(limit)`）。
3. 否则或同实体无结果 → 查同用户的最近日志。
4. 返回 `AuditLogRelatedResponse { source: 'entity' | 'user' | 'none', items: [...] }`。

#### 47.2.6 OutputCache 策略

`Program.cs` 中 `AuditLogs` 策略的 `VaryByQueryKeys` 纳入全部新增参数：

```csharp
options.AddPolicy("AuditLogs", builder => builder
    .Expire(TimeSpan.FromMinutes(2))
    .SetVaryByQuery("action", "entityType", "userId", "entityId",
        "ipAddress", "keyword", "startDate", "endDate", "page", "pageSize"));
```

### 47.3 前端实现

#### 47.3.1 状态管理与过滤参数

```tsx
const [filterStartDate, setFilterStartDate] = useState('');
const [filterEndDate, setFilterEndDate] = useState('');
const [filterUserId, setFilterUserId] = useState('');
const [filterIp, setFilterIp] = useState('');
const [filterKeyword, setFilterKeyword] = useState('');
const debouncedKeyword = useDebounce(filterKeyword, 400);

const filterParams = useMemo(() => ({
  action: filterAction || undefined,
  entityType: filterEntityType || undefined,
  userId: filterUserId ? Number(filterUserId) || undefined : undefined,
  ipAddress: filterIp || undefined,
  keyword: debouncedKeyword || undefined,
  startDate: toUtcStartIso(filterStartDate),
  endDate: toUtcEndIso(filterEndDate),
}), [filterAction, filterEntityType, filterUserId, filterIp, debouncedKeyword, filterStartDate, filterEndDate]);
```

#### 47.3.2 完整性校验回写

批量校验完成后回写 `isTampered` 到列表项，表格 ID 列显示图标：

```tsx
const applyIntegrityToTable = useCallback((report: AuditIntegrityReport) => {
  setIntegrityReport(report);
  setShowIntegrityPanel(true);
  setList((prev) => prev.map((l) => {
    const tampered = report.tamperedLogIds.includes(l.id);
    const isInCheckedPage = report.page === page;
    // isTampered: true=被篡改 / false=未篡改 / null=未校验
    return { ...l, isTampered: isInCheckedPage ? tampered : null } as AuditLogResponse;
  }));
}, [page]);
```

#### 47.3.3 关键技术点

- **声明顺序**：`applyIntegrityToTable` 必须在 `handleVerifyIntegrity` 之前声明，否则 TS2448/TS2454（used before declaration）。
- **防抖搜索**：`useDebounce(filterKeyword, 400)` 避免每次按键触发 API。
- **日期转换**：`toUtcStartIso(dateStr)` 把本地日期转为 UTC ISO 8601 起始时刻；`toUtcEndIso` 转结束时刻。
- **Blob 下载**：`auditLogApi.export()` 返回 `BlobPart`，前端用 `URL.createObjectURL` + `<a download>` 触发下载。
- **图标语义**：`ShieldX`（红色，疑似篡改） / `ShieldCheck`（绿色，校验通过） / 无图标（未校验）。

### 47.4 影响文件

| 层 | 文件 | 说明 |
|----|------|------|
| Domain | `IAuditLogRepository.cs` | 新增 `GetPagedAsync` / `GetStatsAsync` / `ExportAsync` 接口 |
| Infrastructure | `AuditLogRepository.cs` | `ApplyFilters` 集中过滤 + 三方法实现 |
| Application | `IAuditService.cs` / `AuditService.cs` | 新增 `GetStatsAsync` / `ExportCsvAsync` / `VerifySingleAsync` / `VerifyIntegrityAsync` / `GetRelatedAsync` |
| Api | `AuditLogsController.cs` | 新增 `GetStats` / `Export` / `VerifySingle` / `GetRelated` / `VerifyIntegrity` 端点 |
| Api | `Program.cs` | OutputCache 策略纳入新增过滤参数 |
| Application DTO | `AuditLogResponse.cs` | 新增 `HmacSignature` / `IsTampered` 字段 + `AuditLogStatsResponse` / `AuditLogDistributionItem` / `AuditLogIntegrityResult` / `AuditIntegrityReport` / `AuditLogRelatedResponse` |
| Frontend | `AuditLogs.tsx` | 完整重构：日期范围 + 防抖搜索 + 用户/IP 过滤 + KPI 卡片 + 分布图 + 导出 CSV + 完整性校验面板 + 关联日志 + 签名状态图标 |
| Frontend | `api/modules/system.ts` | 新增 `stats / export / verify / verifyIntegrity / getRelated` 方法 |
| Frontend | `api/types/system.ts` | 新增 `AuditLogStatsResponse` / `AuditLogIntegrityResult` / `AuditIntegrityReport` / `AuditLogRelatedResponse` 类型 + `AuditLogResponse` 增加 `hmacSignature` / `isTampered` |

### 47.5 验证结果

- **后端编译**：`dotnet build NoneadCSS.Api -c Release` → 0 errors（8 个已存在警告，无新增）
- **前端类型检查**：`tsc -b` → 0 errors
- **前端 Lint**：`eslint AuditLogs.tsx system.ts types/system.ts` → 0 errors
- **生产部署**：2026-08-27 14:53 部署至生产环境
  - 后端 :5009（PID 6548，127.0.0.1）Production 模式启动正常
  - 前端 :5172（PID 7772，127.0.0.1，强制 IPv4）200 OK
  - Nginx :5173（PID 40300，0.0.0.0）反向代理正常
  - 端点验证（经 Nginx :5173）：
    - `/` 前端首页 → 200 OK
    - `/api/auditlogs/stats` → 401（鉴权生效）
    - `/api/auditlogs/export` → 401（鉴权生效）
    - `/assets/AuditLogs-BimPYkpd.js` → 200 OK（45369 bytes，含完整新功能）
- **备份**：
  - 部署前备份：`Production\_backup_pre_patch_20260827_145101`（backend 配置 + frontend_dist）
  - 数据库冷备份：`Production\data\noneadcss.db.20260827_145324.bak`（90MB）

---

## 48. 2026-08-28 Outlook 用户级凭据 + 日历参会人保存修复（T000099 / T000100 · P1）

### 48.1 T000099：Outlook 同步面板 OAuth 授权 400 + 配置入口缺失

**缺陷表现**：Outlook 同步面板点「OAuth 授权」返回 `Request failed with status code 400`，UI 中无任何 Azure AD 凭据配置入口；原系统级凭据（仅 SuperAdmin 后台统一设置）无法满足多用户/多租户独立适配。

**根因**：配置模型为系统级共享，所有用户复用一套 AppId/TenantId/ClientSecret/RedirectUri；`OutlookSyncPanel.tsx` 未开放凭据按钮。

**修复策略 — 从系统级改为用户级（每用户 4 字段 AES 加密存储）**：

| 层 | 变更 | 关键文件 |
|---|---|---|
| 实体 | `OutlookSyncSetting` 新增 `TenantId / ClientId / ClientSecret / RedirectUri`（`ClientSecret` 使用 `EncryptedStringConverter` AES 加密） | [OutlookSyncSetting.cs](../NoneadCSS.Domain/Entities/OutlookSyncSetting.cs) / [NoneadCSSDbContext.cs](../NoneadCSS.Infrastructure/Persistence/NoneadCSSDbContext.cs) |
| 服务 | `IOutlookSettingsProvider.GetForUserAsync(userId)` 按"用户覆盖 → 系统基线"合并，避免多用户相互覆盖 | [IOutlookSettingsProvider.cs](../NoneadCSS.Application/Services/IOutlookSettingsProvider.cs) / [OutlookSettingsProvider.cs](../NoneadCSS.Infrastructure/Services/Outlook/OutlookSettingsProvider.cs) |
| API | 替换原 Admin-only 端点 → `GET/PUT/DELETE /api/outlook-sync/credentials`（普通用户可独立维护），端点不要求 SuperAdmin | [OutlookSyncController.cs](../NoneadCSS.Api/Controllers/OutlookSyncController.cs) |
| 前端 | `OutlookSyncPanel.tsx` 移除 SuperAdmin 门控，新增「Azure AD 凭据配置」按钮；`api/modules/engagement.ts` 端点路径同步 | [OutlookSyncPanel.tsx](../frontend/src/pages/calendar/panels/OutlookSyncPanel.tsx) / [engagement.ts](../frontend/src/api/modules/engagement.ts) |
| DB 补丁 | `Program.cs` 启动时幂等 `ALTER TABLE "OutlookSyncSettings" ADD COLUMN ...` 补齐 4 列，避免历史库直接抛 SQLite Error 1 | [Program.cs](../NoneadCSS.Api/Program.cs) |

### 48.2 T000100：日历参会人保存报错 + 再次编辑为空（P1）

**缺陷表现**：新建/编辑事件在与会者字段填 `alice@example.com, bob@test.org` 后保存报错；再次打开面板与会者为空。

**根因（双触发）**：
1. 前端 `EditorPanel` 将与会者邮箱写入 `_attendeeEmailList`，提交时只带 `reminderEmails`（CSV 字符串），`attendees: []` 始终为空 → 后端 `AttendeesJson` 写入空 JSON 数组。
2. 后端 `MappingExtensions.ToDto` 使用 `JsonSerializer.Deserialize<T>()` 反序列化 `AttendeesJson / ReminderEmails / ExtrasJson` 等字段；遇到 `null`、空串或非法 JSON 时直接抛异常 → 列表查询和编辑回填均失败。

**修复矩阵（5 段双保险，任一路径都能正确恢复）**：

```
前端 openEdit 合并  ──►  Attendees[] ∪ extras.attendeeEmailList
                              │
                              ▼
EditorPanel 移除空 attendees[]  ──►  payload 不再覆盖服务端 AttendeesJson
                              │
                              ▼
服务端 NormalizeAttendeesAndReminderEmails ──► attendees ↔ reminderEmails CSV 双向互补回填
                              │
                              ▼
MappingExtensions.SafeDeserializeList<T>  ──►  null/非法JSON 返回 default，不抛错
                              │
                              ▼
Program.cs EnsureCalendarEventsAttendeeColumnsAsync ──► 幂等补齐 AttendeesJson/ReminderEmails 列
```

关键落地：
- `MappingExtensions.cs`：`SafeDeserializeList<T>` / `SafeDeserializeObject<T>` 两个通用扩展。
- `CalendarMutationService.cs`：`NormalizeAttendeesAndReminderEmails` 统一入口（Create / Update 两处都会调用）。
- `calendar/store.ts` `openEdit`：DTO.Attendees（用户 ID）+ `extras.attendeeEmailList`（邮箱）双向合并到 UI 状态。
- `EditorPanel.tsx`：提交前 `attendees.length === 0` 时 delete 字段，避免空数组覆盖历史 `AttendeesJson`。

### 48.3 生产验证
- 新建事件「T000100 生产回归 参会人邮箱测试」，填入 `alice@example.com, bob@test.org` → 保存 ✅
- 双击打开同一事件 → 两个邮箱正确回填 ✅
- `GET /api/outlook-sync/credentials` 普通用户调用返回 401（未登录），登录后返回 `200 OK` + 空值；PUT 写入后再次 GET 能读取（ClientSecret 返回 `ENC:*`，不可见明文）✅

---

## 49. 2026-08-28 知识库模块 5 项 P1 热修复（T000101~T000105）

本批为 **v2.2.9 发布后生产紧急热修复**，全量集中在知识库链路，5 个用例均在生产环境（http://127.0.0.1:5173/knowledge）完成 E2E 回归。

### 49.1 T000101 · P1：文章点赞报「服务器错误」

**现象**：任意文章详情页点击 👍 图标 → Toast：`点赞失败：服务器错误，请稍后重试或联系管理员`。

**后端日志**：
```
SQLite Error 1: 'no such table: KnowledgeArticleLikes'
```

**根因**：迁移 `20260826160000_AddKnowledgeArticleLikes.cs` 在开发/生产数据库均未执行；`TryInsertLikeAsync` 中 `INSERT OR IGNORE INTO KnowledgeArticleLikes` 抛错。

**修复**：
1. `Program.cs` 启动新增 `EnsureKnowledgeArticleLikesTableAsync`，幂等：
   ```sql
   CREATE TABLE IF NOT EXISTS "KnowledgeArticleLikes" (
     "UserId" INTEGER NOT NULL,
     "ArticleId" INTEGER NOT NULL,
     "CreatedAt" TEXT NOT NULL,
     PRIMARY KEY ("UserId", "ArticleId")
   );
   CREATE INDEX IF NOT EXISTS "IX_KnowledgeArticleLikes_ArticleId"
     ON "KnowledgeArticleLikes"("ArticleId");
   ```
2. 生产 + 开发库通过 Python `sqlite3` 脚本**立即补建**（DLL 即时生效，无需重启后端）。

**验证**：《NoneadCSS Readme v2.2.5》《NoneadCSS 开发文档 v2.2.5》点赞均 `200 OK`；重复点赞走 `INSERT OR IGNORE`，限流 24h 生效。

### 49.2 T000102 · P1：Markdown 渲染失败（6 种格式无样式）

**现象**：预览模式下加粗/斜体/链接/图片/行内代码/无序列表 6 种语法**不解析**，显示原文 Markdown 符号。

**根因**：marked@18 中同时重写 `paragraph / codespan / link / image / list / listitem` 等 block-level Renderer 会**禁用内部 inline tokenizer**，导致 `**text**`、`` `code` ``、`[t](url)` 等 token 不被解析 → 原文透传。同时 Tailwind preflight 会重置 `ul/ol list-style-type: none`，`.knowledge-content` 未恢复。

**修复（三件套）**：
1. `renderMarkdown` **只保留 heading / code 两种自定义 Renderer**，其他元素用默认解析器。
2. 新增 `postAddTailwindClasses(html)` 函数，对 `<ul> <ol> <li> <code> <strong> <em> <a> <img> <table> <blockquote>` 正则追加 Tailwind 类（`ul.list-disc pl-6`、`code.bg-slate-100.rounded.px-1`、`a.text-indigo-600.hover.underline` 等），避免侵入 marked 内部 tokenize。
3. `index.css` 新增 `.knowledge-content` 下 16 条 typography 显式规则（明暗双主题），覆盖 DOMPurify 后 style 丢失。
4. `DOMPurify.sanitize` 配置添加 `ALLOWED_CSS_PROPERTIES`（通过 `as unknown as Config` 类型断言），保留 style 属性中颜色、text-align。

**验证**：新建「T000102 回归 v2：Markdown 5 种格式 + 无序列表」测试文，**浅色/深色主题下** 5 种必测格式 + 表格/引用/代码块 8 项全部正确渲染。

### 49.3 T000103 · P1：附件上传 — 未保存时，编辑页附件丢失

**现象**：新建文章 → 上传 2 个附件 → **不点保存** → 直接打开编辑 → 编辑页附件区空；查看页附件区仍显示（上传走草稿持久化，查看页读的是服务端保存结果而非草稿）。

**根因**：
- 自动保存 draft 到 localStorage 时只存 `editorContent/editorTitle`，未同步 `editorAttachments`。
- `openEditArticle` 从服务端取文章时仅 `setEditorAttachments(saved.attachments)`，未合并 localStorage 草稿附件。
- 「丢弃草稿 / 恢复草稿」按钮只重置内容字段，未操作附件状态。

**修复（Knowledge.tsx 4 处联动）**：
1. 自动保存 `useEffect` 把 `editorAttachments` 写入 localStorage `knowledge-draft-new-*` / `knowledge-draft-edit-{id}`。
2. `openCreateArticle`：new-article draft 存在附件时 `setEditorAttachments(draftAttachments)`。
3. `openEditArticle`：fetch 服务端文章后 `merge = unionBy(saved, draftAttachments, 'id')` → 本地草稿覆盖服务端，**未保存的本地附件不丢失**。
4. 「丢弃草稿」：强制回退到服务端附件（清空 draft）；「恢复草稿」：再次 unionBy 合并。

### 49.4 T000104 · P1：全局搜索结果被其它 label 遮挡，未在主区域显示

**现象**：顶部搜索框输入关键词 → 下拉弹层渲染结果 → 但父元素 `overflow-hidden` / z-index 不足，结果在 Windows 高 DPI / 缩放场景下被侧边栏或 Tab 面板"吞掉"。

**修复（双保险）**：
1. **下拉弹层 z-index 提升**至 `z-[60]`，并新增「查看全部结果」按钮点击后 `setGlobalSearchResult(...)` 跳转主区域。
2. **主区域新增独立搜索结果 Section**（Knowledge.tsx 条件渲染：`globalSearchResult != null && activeTab === 'articles' && !isTrash`），含渐变 Header + 关键词高亮 + 命中计数 + 分页 + 「清除搜索」按钮。
3. 搜索命中在 Title/Summary/Body 三处分别匹配，最多 5 片命中摘要以 `<mark>` 包裹显示，用户直观看到匹配位置。

### 49.5 T000105 · P1：禁用分类后，新建/编辑文章分类下拉仍可见

**现象**：「分类管理」禁用 `ccc` → 新建文章时分类下拉仍能选 `ccc`（前端未过滤 + 后端写入未校验）。

**修复（前后端双重锁）**：

**前端（UI 层）**：
```tsx
// Knowledge.tsx 新增 activeFlatCategories
const activeFlatCategories = useMemo(
  () => flatCategories.filter(c => c.isActive !== false),
  [flatCategories]
);
```
- 文章筛选器分类下拉 → `activeFlatCategories`
- 新建/编辑文章分类选择器 → `activeFlatCategories`
- 管理面板（禁用/启用分类操作）→ 保留 `flatCategories`（管理员需要看到禁用分类）

**后端（写入层校验，KnowledgeService.cs）**：
```csharp
// CreateArticleAsync / UpdateArticleAsync 统一入口
var cat = await _unitOfWork.KnowledgeCategories.GetByIdAsync(categoryId);
if (cat == null) throw new KeyNotFoundException("分类不存在");
if (!cat.IsActive)
  throw new InvalidOperationException("所选分类已禁用，不能将文章分配到禁用分类");
```
→ 前端绕过（直接 curl 提交禁用分类 ID）也会返回 `所选分类已禁用` 错误。

### 49.6 影响文件清单

| 层 | 文件 | 说明 |
|---|---|---|
| Api | `Program.cs` | 新增 `EnsureKnowledgeArticleLikesTableAsync` / 调整启动补丁顺序 |
| App | `KnowledgeService.cs` | Create/Update 新增禁用分类校验 |
| App | `MappingExtensions.cs` | SafeDeserializeList / SafeDeserializeObject（顺带复用） |
| Frontend | `Knowledge.tsx` | T000102~T000105 四段修复集中落点（~1400 行） |
| Frontend | `index.css` | `.knowledge-content` 新增 16 条 typography + 暗主题 |
| Frontend | `calendar/store.ts` / `EditorPanel.tsx` | T000100 参会人双写 + 回填 |

---

## 50. 2026-08-28 生产发布 SOP 增补：DDL 幂等补丁矩阵 + vite preview IPv4 强制

本节将 v2.2.9 热修复过程中发现的**两类发布级陷阱**固化为 SOP，避免后续版本重复踩坑。同时同步到 `CONTRIBUTING.md §9` Release Checklist 第 17 项。

### 50.1 DDL 幂等补丁矩阵（Program.cs 启动必跑）

跨版本升级时，迁移系统存在「老库直接 attach → 跳过未执行的迁移」现象。v2.2.9 发布当天共发生 3 次：`KnowledgeCategories RowVersion 列`、`KnowledgeArticleLikes 表`、`CalendarEvents AttendeesJson 列`。因此 Program.cs 启动流程中必须将以下补丁放在 WebApplication.Run 之前，且全部使用 `CREATE TABLE IF NOT EXISTS` / `ALTER TABLE ADD COLUMN`（SQLite 可重复执行，无副作用）：

| 补丁方法 | 目标对象 | 幂等语句模式 | 验证 SQL |
|---|---|---|---|
| `EnsureRowVersionColumnsAsync` | Users / Customers / ChatSessions / ChatMessages / ApiKeys / Webhooks / KnowledgeCategories / KnowledgeArticles | `ALTER TABLE "T" ADD COLUMN "RowVersion" BLOB NULL` | `SELECT COUNT(*) FROM pragma_table_info('KnowledgeCategories') WHERE name='RowVersion'` |
| `EnsureKnowledgeArticleLikesTableAsync` | KnowledgeArticleLikes 联合主键表 | `CREATE TABLE IF NOT EXISTS ... PRIMARY KEY(UserId,ArticleId)` | `SELECT name FROM sqlite_master WHERE type='table' AND name='KnowledgeArticleLikes'` |
| `EnsureCalendarEventsAttendeeColumnsAsync` | CalendarEvents AttendeesJson / ReminderEmails / ExtrasJson | 列存在性 check → `ALTER TABLE ADD` | `SELECT COUNT(*) FROM pragma_table_info('CalendarEvents') WHERE name IN ('AttendeesJson','ReminderEmails')` = 2 |
| `EnsureOutlookSyncUserCredentialColumnsAsync` | OutlookSyncSettings 4 列用户级凭据 | 同上 | `SELECT COUNT(*) FROM pragma_table_info('OutlookSyncSettings') WHERE name IN ('TenantId','ClientId','ClientSecret','RedirectUri')` = 4 |

发布后**必须**在生产环境对以上 4 条验证 SQL 分别跑一次并记录结果。

### 50.2 vite preview IPv4 强制（Nginx 502 陷阱）

**现象**：`vite preview --port 5172` 启动后，Windows 下默认绑定 `[::1]:5172`（IPv6）；而 Nginx 配置 `proxy_pass http://127.0.0.1:5172;`（IPv4）→ 握手失败，Nginx 返回 `502 Bad Gateway`。该问题在 v2.2.3 → v2.2.9 多次发布回归中出现过 2 次。

**永久 SOP**：
```powershell
# 启动命令（Production/start-prod.ps1 已固化）
Set-Location Production\frontend\dist
npx vite preview --port 5172 --host 127.0.0.1
```

**发布验证（3 条必须齐）**：
```powershell
# 1. HTTP 可达
curl -I http://127.0.0.1:5172/         # 期望 200
# 2. 绑定地址必须是 IPv4
(Get-NetTCPConnection -LocalPort 5172).LocalAddress    # 期望 = 127.0.0.1
# 3. Nginx 代理也 OK
curl -I http://127.0.0.1:5173/         # 期望 200
```
任一失败都要先修正 vite 启动参数再执行下一项发布检查。

### 50.3 发布 7 步标准动作（v2.2.9 增订版）

① 停服 + 清理残留（stop-prod.ps1 → 端口 + 双进程名二次核对）
② `dotnet publish -c Release` → 同步 `appsettings.Production.json` 到 `Production\backend\`
③ `npm run build` → 同步 `frontend\dist\` 到 `Production\frontend\dist\`
④ 执行以上 §50.1 DDL 补丁 4 条验证 SQL；§50.2 vite preview IPv4 三项断言
⑤ start-prod 非阻塞启动（blocking=false + long_running_process）
⑥ 四端健康检查（:5173 首页 / :5172 / :5009/health / :5009/docs） + :5008 负向断言 = 空
⑦ 发布日志写入 `CHANGELOG.md` / `ReleaseNotes_vX.Y.Z.md` / `development.md` 对应章节

*版本 v2.2.9 · 2026-08-28 · 系统管理-审计日志完善与优化 + Outlook T000099 + 日历 T000100 · P1 + 知识库 T000101~T000105 五项 P1 热修复*

---

## 51. 2026-08-30 审计日志完整性签名修复 + 前端中文标签全面补齐（v2.2.10）

### 51.1 任务背景

v2.2.9 交付的审计日志 HMAC-SHA256 完整性校验在功能完善后暴露出一个**彻底失效的 P0 问题**：单条 / 批量校验结果始终为"疑似篡改"（ShieldX），完整性校验实际不可用。同时，审计日志前端统计卡片（操作分布 Top 8、实体类型占比）中出现英文原文（如 `calendar.update`、`WorkflowExecutionLog`、`SystemOption`），违背中文界面预期。本次 v2.2.10 聚焦解决这两个问题。

### 51.2 根因分析：审计日志签名永不通

在 [AuditHmacSigner.cs](../NoneadCSS.Infrastructure/Services/AuditHmacSigner.cs) 的 `BuildSignaturePayload` 中：

```csharp
return string.Join("|",
    log.UserId?.ToString() ?? "null",
    log.Action,
    log.EntityType,
    log.EntityId?.ToString() ?? "null",
    log.CreatedAt.ToString("O"));
```

`log.CreatedAt.ToString("O")` 在写入与校验时可能输出不同的字符串：

| 场景 | `CreatedAt.Kind` | `ToString("O")` 输出 | 示例 |
|------|------------------|----------------------|------|
| 写入时 | `Utc` | 带 `Z` 后缀 | `2026-08-28T12:34:56.7891234Z` |
| EF Core 从 SQLite 读回 | `Unspecified` | 不带 `Z` 后缀 | `2026-08-28T12:34:56.7891234` |

由于两个字符串不一致，写入签名与校验签名永远不匹配，导致完整性校验功能实质失效。

### 51.3 修复：统一按 Utc 渲染时间

```csharp
var createdAt = log.CreatedAt.Kind == DateTimeKind.Utc
    ? log.CreatedAt
    : DateTime.SpecifyKind(log.CreatedAt, DateTimeKind.Utc);
return string.Join("|",
    log.UserId?.ToString() ?? "null",
    log.Action,
    log.EntityType,
    log.EntityId?.ToString() ?? "null",
    createdAt.ToString("O"));
```

要点：
- `DateTime.SpecifyKind` 只修正 `Kind` 标记，**不偏移时间**，因此不会改变日志的物理时间。
- 修复后写入与校验生成的载荷完全一致，HMAC 签名校验恢复正常，单条 / 批量校验可正确区分"校验通过"与"疑似篡改"。

### 51.4 存量数据重签

对历史存量审计日志执行体系化重签（`audit_resign` 工具），共 **56887** 条记录完成重签。签名基于修复后的载荷重新生成，保证存量记录可被校验通过（绿色 ShieldCheck）。

### 51.5 前端中文标签全面补齐

#### 51.5.1 背景

`AuditLogs.tsx` 中的 `actionStyles` / `entityTypeStyles` 为静态映射表，按小写 key 匹配中文标签 + 颜色 + 图标；未命中时通过 `?? { label: action, ... }` 兜底回退显示原始英文字符串。因映射表覆盖不全，统计卡片出现英文原文。

#### 51.5.2 修复内容

对后端全部 Action / EntityType 值做全面审计（覆盖三条写入路径：EF Core 拦截器、MVC ActionFilter、显式服务/监听器），补齐映射：

- **Action 映射（50 个）**：新增 `calendar.*`（约 11 条）、`time_entry.*`（6 条）、`restore` / `hard_delete` / `bulk_*`、`calendar.batch_*`（8 条），并新增 `calendar.` / `time_entry.` 前缀兜底逻辑。
- **实体类型映射（61 个）**：修正拼写错误 `calendaremindertttempt` → `calendaremindertattempt`（`CalendarReminderAttempt`，标签"日历提醒尝试"）；新增 `workflowexecutionlog`（工作流执行日志）、`systemoption`（系统选项）、`slapolicy`（SLA策略）等。
- **图标导入**：新增 `Star` 等图标配合新增映射，每个标签统一「中文 + 颜色 + 图标」呈现。

### 51.6 影响文件

- `NoneadCSS.Infrastructure/Services/AuditHmacSigner.cs`（`BuildSignaturePayload` 统一 Utc 渲染）
- `frontend/src/pages/AuditLogs.tsx`（`actionStyles` / `entityTypeStyles` 映射表 + 前缀兜底 + 图标导入）
- 审计日志存量数据（重签）
- `README.md` / `CHANGELOG.md` / `docs/development.md` / 用户手册 / `docs/ReleaseNotes_v2.2.10.md`（文档同步）

### 51.7 验证结果

- 后端 `dotnet publish` + 前端 `npm run build` 均通过（tsc / eslint 无错误）。
- `/api/auditlogs/{id}/verify` 与 `/api/auditlogs/verify-integrity` 返回正常；重签后存量记录校验通过（绿色 ShieldCheck）。
- 审计日志页面「操作分布（全局 Top 8）」与「实体类型占比」不再出现英文。
- 部署至生产环境（`Production\backend` + `Production\frontend\dist`）后四端健康检查通过。

### 51.8 架构改进沉淀

1. **时间 `Kind` 一致性约束**：凡参与 HMAC/哈希签名或幂等键构建的 `DateTime`，必须统一 `Kind`（建议 `Utc`），避免写入（`UtcNow`）与读取（`Unspecified`）时 `ToString("O")` 输出不一致导致签名永不通。
2. **前端 → 后端值域全量映射**：凡前端以静态映射表展示后端枚举/字符串值的场景，必须以后端可见值域全集为准补齐映射，并保留前缀/兜底逻辑。
3. **历史数据体系化重签**：修复签名算法后需对存量数据执行体系化重签，保证新旧记录行为一致。

*版本 v2.2.10 · 2026-08-30 · 审计日志完整性签名修复（CreatedAt Utc 渲染 + 56887 条重签）+ 前端中文标签全面补齐（50 Action / 61 实体类型 + 前缀兜底）*

## 52. 2026-09-01 全面代码审核 T1-T28 修复 + 回归缺陷修复 + 微信 Token 修复（v2.2.11）

### 52.1 全面代码审核（T1-T28）

四路并行审核定位 **28 项**问题并全部修复，覆盖部署配置与前端安全与后端可靠性：

- **部署配置**：T1 HTTPS 监听（注释块交付，需证书后启用）、T5 生产 HTTP 启动告警、T15 vite preview 强制 `--host 127.0.0.1`、T16 Docker `KnownProxies` 默认 `172.17.0.1`、T21 nginx API 5xx JSON 透传、T22 Host 头漂移修正、T23 SQLite `Mode=ReadWriteCreate`。
- **前端安全**：T2 `renderHtml` 收紧 `data:` 图片白名单（DOMPurify `ALLOWED_URI_REGEXP`）、T6 附件 URL 协议白名单（`safeAttachmentUrl` 深度防御）。
- **后端可靠性**：T3 IM 消息队列发送异常转入重试/失败路径（`ImMessageSendResult`）、T4 API Key scope 约束、T7 `sessionToken` 移出 URL 改 `X-Session-Token` 头、T8 SetupToken 移除明文日志、T9/T10/T27 飞书/钉钉 Stream ACK 语义 at-least-once + 滑动窗口去重 + 内存保护、T11 `Task.Run` 背压限制、T12 打开计数 `ExecuteUpdate` 原子递增、T17 `FetchEmailJob` 每邮箱独立 scope。
- **低危加固**：乐观锁、分布式锁、Outlook 健康/凭据、Webhook、权限扩展、数据库种子等若干项。

### 52.2 回归缺陷修复（T000109 / T000101 / T000106 / T000107 / T000108）

详见 [CHANGELOG v2.2.11](#)、用户手册 2.2.11 记录。全量后端测试 **1214 通过 / 0 失败**。

### 52.3 微信 Token 失效修复（生产配置）

**症状**：微信聊天模块"Token 失效"、客服消息无法发送。日志持续 `Mode=Proxy, StatusCode=401, {"success":false,"message":"Unauthorized"}`。

**根因（两层）**：
1. `Production\secrets.ps1` 中微信渠道密钥为**占位符**，微信 API（`https://api.weixin.qq.com`）鉴权失败返回 401。
2. 编辑 `secrets.ps1` 时丢失 **UTF-8 BOM**，PowerShell 5.1 按 ANSI(GBK) 解析中文注释导致 **AUDIT/WECHAT/APP/ADMIN_EMAIL 4 行赋值被吞**，后端因 `AUDIT__HmacKey` 缺失启动崩溃。

**修复**：`secrets.ps1` 替换为有效密钥 + 恢复 UTF-8 BOM（逐行验证 8 个环境变量全部正确加载）。**经验教训**：PowerShell 5.1 编辑含中文注释的 `.ps1` 文件必须保留 UTF-8 with BOM。

*版本 v2.2.11 · 2026-09-01 · 全面代码审核 T1-T28 + 回归缺陷修复 + 新迁移 + 微信 Token 代理密钥修复*

## 53. 2026-09-01 审计日志写入隔离 + MailboxUsers 并发兜底 + 系统日志清空功能（v2.2.12）

### 53.1 生产日志异常排查（部署前历史记录定位）

用户在生产日志中粘贴大量异常，经时间戳定位均为 **20:05 新部署前**的历史记录（14:53-17:11）：`SQLite Error 14`（11:08 迁移冷备时 backups 目录不可写，20:05 起冷备正常）、`MailboxUsers UNIQUE constraint failed`（并发竞态，本轮修复）、`Threads.MessageId` 唯一约束（T000113 已修复，不复发）、`URL 不能指向 192.168.x.x`（Webhook 内网地址安全校验，非缺陷）、`User cannot be tracked`（审计冲突，本轮修复）、`HttpRequestException`（钉钉 Stream 10054 自动重连，正常）。

### 53.2 审计日志写入隔离（P0）

- **根因**（日志 15:20:19）：`AuditActionFilter` 记录审计与业务动作共用请求级 DbContext；业务先跟踪 `User`，审计 `SaveChanges` DetectChanges 导航修复时尝试附加同 Id 的第二个 `User` 实例 → IdentityMap 冲突。
- **修复**：`AuditService.LogAsync` 改用 `IServiceScopeFactory.CreateScope()` 独立作用域（全新 DbContext）写入；与请求共享跟踪器完全隔离。
- **集成测试**：`AuditServiceIsolationIntegrationTests` 2 用例（主上下文污染态下审计仍落库 + 无 HttpContext 显式 userId）。

### 53.3 MailboxUsers 并发兜底（P0）

- **根因**（日志 15:22）：并发更新邮箱/用户时内存集合过期，重复插入映射 → `MailboxUsers UNIQUE constraint failed`。
- **修复**：`MailboxService.UpdateMailboxAsync` / `UserService.UpdateUserAsync` 增加数据库级 `UserHasAccessAsync`（AsNoTracking）兜底校验，消除 TOCTOU 竞态。

### 53.4 系统工具-系统日志「清空日志文件」删除 log 记录

- **背景**：原「清空」仅清前端视图（不影响日志文件），后端无删除端点。
- **后端**：新增 `POST /api/system/tools/logs/clear`（SuperAdmin）；`SerilogLogFileMaintenance`（Infrastructure 服务）提供 `ResolveLogsPath`（与 `GetSystemLogs` 共用，避免双目录分裂）与 `ClearLogFiles`（`log-*.log` 截断为 0 字节）。
- **Serilog 配置**：两个 File sink 增加 `shared: true`（原 FileShare.Read 独占无法外部截断，实测确认）；`SharedFileSink` 写前 `Seek(0, End)`，截断后追加无空洞。
- **前端**：`SystemTools.tsx` 新增红色「清空日志文件」危险按钮（二次确认 + 刷新），`system.ts` 新增 `clearLogs`。
- **测试**：`SerilogLogFileMaintenanceTests` 5 用例（含真实 Serilog shared sink 截断后继续追加回归）。全量后端测试 **1292 通过 / 0 失败**。

### 53.5 部署

v2.2.12 已编译并部署至生产环境（`Production\backend` + `Production\frontend\dist`）；后端 :5009、前端 vite preview :5172（强制 IPv4）、Nginx :5173 反向代理均验证通过；`log-*.log` 外部可截断（`shared:true` 生效）。

*版本 v2.2.12 · 2026-09-01 · 审计日志写入隔离 + MailboxUsers 并发兜底 + 系统工具-系统日志清空功能*

## 54. 2026-09-04 Webhook 业务事件自动触发 + 日历账户切换状态重置/默认月显示修复（v2.2.13）

### 54.1 集成与扩展-Webhook：满足触发条件未触发回调请求（T000118）

- **用例编号**：T000118
- **根因**：业务事件持久化后仅在 Webhook 管理端手工调试触发，未在业务服务中挂接自动触发逻辑。
- **修复**：为 9 种业务事件（`conversation_created` / `conversation_updated` / `conversation_deleted` / `conversation_status_changed` / `thread_created` / `customer_created` / `customer_updated` / `user_created` / `user_updated`）在业务服务持久化后直接调用 `IWebhookService.TriggerAsync(eventType, mailboxId, payload, entityType, entityId)` 自动触发回调。各服务以可选尾部参数注入 `IWebhookService?`（`webhookService = null`），通过构造函数/属性赋值并在动作成功后调用，避免影响既有构造链。
- **触发点覆盖**：`conversation_created`（`ConversationService`/`ChatService`）、`conversation_updated`（`DingDingService`/`DingDingIngestionService`/`WeChatIngestionService`/`WhatsAppService`）、`conversation_deleted`（`ConversationService`）、`conversation_status_changed`（`ConversationStatusService`）、`thread_created`（`ConversationThreadService`）、`customer_created`/`customer_updated`（`CustomerService`）、`user_created`/`user_updated`（`UserService`）。
- **验证**：`dotnet build` 0 错误，全量后端测试 **1436 通过 / 0 失败**。

### 54.2 日历管理：切换账户页面显示上一账户日程（T000119）

- **根因**：`useCalendarStore` 是模块级单例（`create` 仅执行一次），内存状态在登出或账户切换后仍残留；SPA 内 `NavLink` 跳转 `/calendar` 不重建 store，`setAllEvents` 增量合并使旧事件残留。
- **修复**：新增导出函数 `resetCalendarStore()`（`useCalendarStore.setState(createInitialState())`），在 `AuthContext` 的 `logout()` 与 `handleAuthExpired` 两处调用，登出/会话失效时重置内存 store。
- **影响文件**：`frontend/src/pages/calendar/store.ts`、`frontend/src/context/AuthContext.tsx`。
- **验证**：`npx tsc -b` 与 `npm run build` 均通过（0 错误）。

### 54.3 日历管理：日历月显示被记忆的手动切换月份（T000120）

- **根因**：日历 store 的 `persistViewState`/`restoreViewState` 会把上次浏览月份持久化到 `localStorage`，`Calendar.tsx` 挂载 effect 检测到已持久化视图状态时跳过 `gotoToday()`。
- **修复**：① 初始化恢复视图状态仅保留 `viewMode`/`calSubView`，不再恢复 `calCursor`/`calMonth`；② `Calendar.tsx` 挂载 effect 无论何种方式进入都调用 `gotoToday()`，保证每次进入默认显示当前月。
- **影响文件**：`frontend/src/pages/calendar/store.ts`、`frontend/src/pages/Calendar.tsx`。
- **验证**：`npx tsc -b` 与 `npm run build` 均通过（0 错误）。

### 54.4 服务台-会话管理：客户邮件正文包含图片/表格不显示（T000121）

- **根因**：内嵌图片不显示存在四层根因（`AllowedSchemes` 不含 `cid:`、`SaveAttachmentsAsync` 未替换 `cid:` 引用、浏览器不解析 `cid:` URI、`GetAttachmentParts` 仅接受 `attachment` 类型）；表格不显示根因为前后端共同剥离内联 `style` 且前端未对 `.html content` 内 `table` 施加样式。
- **修复**：① `Attachment` 实体新增 `ContentId` 字段并生成 EF Migration `20260904090528_AddAttachmentContentId`；② `HtmlSanitizerHelper` 的 `AllowedSchemes` 加入 `cid:` 协议；③ `EmailProcessingService.SaveAttachmentsAsync` 记录附件 `ContentId`，`GetAttachmentParts` 新增 `isInlineWithContentId` 条件；④ `EmailIngestionService.ReplaceEmbeddedImageCidReferences` 将 `thread.Body` 中 `cid:` 引用替换为 `/api/attachments/{id}/download` 下载 URL；⑤ `ThreadList.tsx` 为富文本追加表格与图片样式。
- **影响文件**：`NoneadCSS.Domain/Entities/Attachment.cs`、`NoneadCSS.Application/Common/HtmlSanitizerHelper.cs`、`NoneadCSS.Infrastructure/Services/EmailProcessingService.cs`、`NoneadCSS.Application/Services/EmailIngestionService.cs`、`NoneadCSS.Infrastructure/Migrations/20260904090528_AddAttachmentContentId.cs`、`frontend/src/components/ThreadList.tsx`。
- **验证**：`dotnet build` 0 错误 0 警告；全量后端测试 **1436 通过 / 0 失败**；`npx tsc -b` 0 错误；`npm run build` 通过。

### 54.5 部署

v2.2.13 已编译并部署至生产环境；后端 :5009、前端 vite preview :5172（强制 IPv4）、Nginx :5173 反向代理均验证通过。

---

## 55. 2026-09-04 API密钥页面刷新空白兜底（v2.2.14）

### 55.1 集成与扩展-API密钥：API密钥页面刷新空白显示无加载页面（T000122）

- **用例编号**：T000122
- **根因**：`ApiKeys.tsx` 是全项目唯一一个 `loadOverview()` / `loadList()` / `loadTrash()` 均无 `catch` 块、渲染层无错误兜底 UI 的页面。当请求失败时 `overview` 保持 `null` 而 `loading` 复位为 `false`，渲染条件使页面主体空白。
- **修复**：① 页面新增 `error` 状态；② 三个加载函数补充 `catch` 块并调用 `getApiErrorMessage` 写入错误信息，成功/重新加载时清空；③ 渲染层新增「加载失败：{error}」+「重试」按钮，重试按当前 Tab 重新发起 overview / list 请求。
- **影响文件**：`frontend/src/pages/ApiKeys.tsx`。
- **验证**：`npx tsc -b` 0 错误；`npm run build` 通过。

### 55.2 部署

v2.2.14 已编译并部署至生产环境；后端 :5009、前端 vite preview :5172（强制 IPv4）、Nginx :5173 反向代理均验证通过。

---

## 56. 2026-09-04 在线聊天转工单-会话回复客户邮箱泄露修复（v2.2.15）

### 56.1 服务台-会话管理：在线聊天转工单-会话回复显示客户邮箱信息（T000123）

- **用例编号**：T000123
- **根因**：客户会话消息发送者显示名存在两条泄露客户邮箱的链路——① 客户线程的 `From` 直接使用 `session.CustomerEmail`，前端 `ThreadList.tsx` 的 `displayName = customerName || thread.from || '未知'` 在 `customerName` 为空时回退到邮箱版 `thread.from`；② `CustomerResponse.FullName = customer.GetFullName(true)`，而 `Customer.GetFullName(true)` 在 `FirstName + LastName` 均为空时回退到 `GetMainEmail()`，于是 `customerName` 本身即为邮箱。
- **修复**：① `frontend/src/utils/format.ts` 新增共享辅助函数 `isEmailAddress()` 与 `displayCustomerName()`；② `ThreadList.tsx` 客户消息显示名改为 `nameOrFrom = customerName || thread.from || '未知'`，`isEmailAddress(nameOrFrom)` 为真时用「客户」兜底；③ `ConversationDetail.tsx` 会话头部「客户：」逻辑用 `displayCustomerName(conversation.customer?.fullName)` 简化，避免回退到 `conversation.customerEmail` 泄露邮箱。
- **影响文件**：`frontend/src/utils/format.ts`、`frontend/src/components/ThreadList.tsx`、`frontend/src/pages/ConversationDetail.tsx`。
- **验证**：`npx tsc -b` 0 错误；`npm run build` 通过。

### 56.2 部署

v2.2.15 已编译并部署至生产环境；后端 :5009、前端 vite preview :5172（强制 IPv4）、Nginx :5173 反向代理均验证通过。

*版本 v2.2.15 · 2026-09-04 · Webhook 业务事件自动触发 + 日历账户切换状态重置/默认月显示修复 + 客户邮件内嵌图片/表格显示 + API密钥页面刷新兜底 + 在线聊天转工单会话回复不泄漏客户邮箱*

---

## 57. 2026-10-08 全项目国际化（zh-Hans/en）批次 1/2/12 生产发布（v2.2.16）

### 57.1 发布内容

分支 `feat/i18n-batch1`（发布提交 `64591f7`），相对上一版线上构建 `fd2d114`（2026-09-21）共计 **83 个提交 / 253 个文件变更**（前端 184、文档 47、Api 6、Application 6、Tests 5、Infrastructure 3、其他 2）。主要内容：

- **批次 1**：i18n 基础设施——词条类型与中文真源骨架、`translate()` 插值与复数、47 条导航文案类型化、导航 label 改纯 key。
- **批次 2**：波次 1–15 + 收官批完成全界面文案本地化，外壳/管理页硬编码中文基线 **223 → 0**，引入显式豁免标记。
- **批次 12**：后端消息与响应信封本地化、附件端点直出中文清理、异常文案改错误码。
- **门禁**：硬编码中文基线门禁 + 基线生成器；`tsc -b` / `i18n:check` / `vitest` / `vite build` 纳入 CI。

### 57.2 发布执行（§18.11 七步 SOP 实录）

| 步骤 | 执行情况 |
|---|---|
| ① 停服 + 清理残留 | `Production\stop-prod.ps1` + 按端口/进程名兜底；断言 5009 / 5172 / 5173 全部释放 |
| ② 后端 publish | 先发布到 `Production\_publish_staging_20261008_200727` 暂存（避免构建失败时占用线上），校验 `ProductVersion=1.0.0+64591f7a8797…` 后再同步 |
| ③ 前端构建 | `frontend`：`npx tsc -b` → `npm run i18n:check` → `npx vitest run`（643 通过）→ `npm run build`；产物 `frontend\dist` 同步到 `Production\frontend\dist` |
| ④ 配置同步 | `appsettings.Production.json` 覆盖，首行 `"Urls": "http://127.0.0.1:5009"` 校验通过 |
| ⑤ 启动 | `start-prod.ps1`；本次改为计划任务托管（见 57.4） |
| ⑥ 健康检查 | 四个端点全部 200；`:5008` 负向断言为空 |
| ⑦ 进程名核对 | 仅 `dotnet`（`Production\backend\NoneadCSS.Api.dll`）/ `nginx` / `node`，无旧版 `NoneadCSS.exe` 混跑 |

**同步策略（覆盖边界）**：后端用 `robocopy /E`（只增不删）而非 `/MIR`，以保护 `Production\backend\Data\`（线上附件真源，`appsettings.Production.json` 的 `UploadPath` 指向此处）与 `Production\logs\`；前端 `dist` 为纯构建产物，用 `/MIR` 清理陈旧哈希文件。发布后核对 `backend\Data` 仍为 33 文件 / 61.4 MB，未损。

### 57.3 数据库与备份

- 发布前冷备：`Production\_backup_prod_20261008_201004`（DB 101 MB + 旧 backend 276.7 MB + 旧前端产物 5.4 MB）。
- 迁移：`20260921060825_FixSendLogStatusCheckConstraint` 已应用（`__EFMigrationsHistory` 校验）。
- §50.1 四条 DDL 校验：`KnowledgeCategories.RowVersion`=1、`KnowledgeArticleLikes` 表存在、`CalendarEvents` 参会者列=2、`OutlookSyncSettings` 凭据列=4，全部通过。
- `PRAGMA integrity_check = ok`；业务数据正常（9 用户 / 83 会话）。
- ⚠️ **遗留观察**：启动日志出现 `DbMigrationSafety SQLite 数据库文件不存在（首次创建），跳过冷备: …\Production\backend\data\noneadcss.db`。即冷备的路径解析仍落在 `AppContext.BaseDirectory`（ContentRoot）而非绝对连接字符串指向的 `Production\data\`，与 §39.6 记录的遗留风险一致。本次因无待执行迁移未造成影响，但**后续含迁移的发布不应依赖该自动冷备**，需先手工冷备（本地发布已如此执行）。核查确认未生成 `Production\backend\Data\noneadcss.db`，无双 DB 分裂。

### 57.4 服务托管方式变更

原生产进程由交互式 PowerShell（`start-prod.ps1` 末尾保活循环）托管，随宿主会话结束即被回收。本次改为 Windows 计划任务：

```powershell
$act  = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument '-ExecutionPolicy Bypass -NoProfile -File "C:\noneadcss\Production\start-prod.ps1"'
$set  = New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew
$prin = New-ScheduledTaskPrincipal -UserId "$env:USERDOMAIN\$env:USERNAME" -LogonType Interactive -RunLevel Limited
Register-ScheduledTask -TaskName 'NoneadCSS-Production' -Action $act -Settings $set -Principal $prin -Force
Start-ScheduledTask -TaskName 'NoneadCSS-Production'
```

任务**无触发器**（不随开机自启，保持与既有手工发布习惯一致），仅用于让生产进程脱离交互会话；停止生产用 `Production\stop-prod.ps1`，或 `Stop-ScheduledTask -TaskName NoneadCSS-Production`。

### 57.5 本次发现的两处环境性障碍（不阻断发布，但影响发布方式）

1. **VSTest 在本机自动化执行环境下恒失败**：`testhost` 调用 `Process.EnableRaisingEvents` 打开父进程句柄返回 `Win32Exception (5) Access denied`，与进程树、完整性级别（Medium）、架构（testhost 仅有 x64）、构建节点复用均无关；`--arch x64`、`<TargetPlatform>x64</TargetPlatform>`、WMI 脱离进程树均无效。**后端全量测试必须在普通终端手工执行**：`dotnet test NoneadCSS.Tests/NoneadCSS.Tests.csproj -c Release`。
2. **`vite build` 在默认 `%TEMP%` 下失败**：esbuild 删除其临时文件时报 `Access is denied`（`vite:esbuild-transpile`）。规避：把 `TEMP`/`TMP` 指向工作区目录后再构建，例如 `$env:TEMP='C:\noneadcss\.build-tmp'`。本次前端构建即以此方式通过。

### 57.6 验证结果

- 前端：`tsc -b` 0 错误；`i18n:check` 未超基线（0 处 / 基线 0 处）；`vitest` **643 通过 / 14 文件**；`vite build` 成功。
- 后端：`dotnet publish -c Release` 成功，产物 `ProductVersion` 与发布提交一致。
- 生产：四端 200、`:5008` 无监听、`:5172` 绑定 `127.0.0.1`、DDL 校验通过、`integrity_check=ok`。

*版本 v2.2.16 · 2026-10-08 · 全项目国际化（zh-Hans/en）批次 1 基础设施 + 批次 2 波次 1–15 与收官批 + 批次 12 后端消息与响应信封 · 生产发布*

---

## 58. 2026-10-10 翻译管理导入导出闭环修复与生产发布（v2.2.17）

### 58.1 发布内容

分支 `feat/i18n-batch1`（发布提交 `fbe4f4f`，与远端 `master` 同 SHA），相对上一版线上构建 `64591f7` 共 **4 个提交 / 15 个文件变更**（前端 11、其他 2、文档 1、Api 1）：

- `289766b` fix(i18n)：翻译管理导入导出修复——资源名 / 键集 / 校验 / 鉴权（本版唯一代码改动）。
- `84ebae5` docs(audit)：全项目代码与功能审核报告（阻断 4 / 高 14 / 中 24 / 低 22）。
- `fbe4f4f` chore(frontend)：两个 i18n 审计脚本纳入版本控制。
- `36d7b88` docs(release)：v2.2.16 发布记录。

**无新增 EF 迁移，不改数据库结构。**

### 58.2 根因：resx 嵌入资源名与附属程序集

后端 `/api/localization` 按源码名 `…Messages{后缀}.resx` 查找嵌入资源，而 resx 编译后实际名为 `NoneadCSS.Application.Resources.Messages.resources`（扩展名 `.resources`）；`zh-Hans` 区域性变体不在主程序集，而在附属程序集 `zh-Hans\NoneadCSS.Application.resources.dll`——主程序集 `GetManifestResourceNames()` 里没有该名字。故 `/keys` 恒空、`/stats` 与 `/diff` 恒 0、`/export` 恒输出 `{}`，把该文件原样导入即 400「导入内容为空」。

**修复**：改用静态 `ResourceManager` 按显式 culture 取词（与既有 `LocalizationService` 的 `IStringLocalizer` 用法一致）。**排查手段**：`[Reflection.Assembly]::LoadFrom(<dll>).GetManifestResourceNames()` 直接枚举真实资源名，不要靠猜。

配套修复：导出改为「前端 UI 真源 ∪ 后端 resx/DB（DB 优先）」（覆盖 6454 + 65 条可编辑键）；导入加三层前端预校验且后端报错不再直出英文原文；`translations/{culture}` 去掉 `AllowAnonymous` 并在 `I18nContext` 补「未登录不请求覆盖层」守卫；`/stats` 增 `dbKeyCount` 并按页面键集口径统计，标签改「已定制 / 未定制」并 `min` 钳制。

### 58.3 发布执行（§18.11 七步）

| 步骤 | 执行情况 |
|---|---|
| ① 停服 + 清理残留 | `stop-prod.ps1` + 按端口/进程名兜底 + `Stop-ScheduledTask`（否则 `MultipleInstances=IgnoreNew` 会阻止重启）；断言 5009/5172/5173 全释放 |
| ② 后端 publish | 先发布到 `Production\_publish_staging_20261010_120823`，校验 `ProductVersion=1.0.0+fbe4f4f…` 后再同步 |
| ③ 前端构建 | `tsc -b` → `i18n:check` → `vitest`（648 通过）→ `npm run build`；产物同步到 `Production\frontend\dist` |
| ④ 配置同步 | `appsettings.Production.json` 覆盖，首行 `"Urls": "http://127.0.0.1:5009"` 校验通过 |
| ⑤ 启动 | 计划任务 `NoneadCSS-Production`（本次已补重启策略，见 58.4） |
| ⑥ 健康检查 | 四端全部 200；`:5008` 负向断言为空；`:5172` 绑定 `127.0.0.1` |
| ⑦ 进程名核对 | 仅 `dotnet`（`Production\backend\NoneadCSS.Api.dll`）/ `nginx` / `node`，无旧版 `NoneadCSS.exe` 混跑 |

**覆盖边界**：后端用 `robocopy /E`（只增不删）以保护 `Production\backend\Data\`（线上附件真源）与 `logs\`；前端 `dist` 为纯构建产物，用 `/MIR`。发布后核对 `backend\Data` 仍为 **33 文件 / 61.4 MB**，未损。

### 58.4 发布前的一次生产宕机与运维加固

**现象**：开始本次发布前发现生产已宕机——`:5173`（nginx）在跑但返回 **502**，`:5009`、`:5172` 均拒绝连接；数据库 WAL 停在 12:06。

**定位**：计划任务 `NoneadCSS-Production` 状态为 `Ready`，`LastTaskResult = 3221225786`（`0xC000013A` = `STATUS_CONTROL_C_EXIT`），即监督进程被控制台中断事件终止；任务未配置重启策略，因此**没有自愈**，生产从 12:06 起一直宕到本次修复。

**处置**：① 先 `Start-ScheduledTask` 恢复线上（四端 200），再排本次发布；② 给任务补 `RestartCount=999` / `RestartInterval=PT1M`，失败后 1 分钟内自动重启：

```powershell
$t = Get-ScheduledTask -TaskName 'NoneadCSS-Production'
$s = $t.Settings; $s.RestartCount = 999; $s.RestartInterval = 'PT1M'
Set-ScheduledTask -TaskName 'NoneadCSS-Production' -Settings $s
```

⚠️ **教训**：把生产托管给计划任务时，**必须同时配置失败重启策略**，否则一次外部中断（控制台关闭 / Ctrl+C / 会话回收）就会造成无告警的长时间宕机。排障入口固定为 `Get-ScheduledTaskInfo -TaskName NoneadCSS-Production` 看 `LastTaskResult`。

### 58.5 验证结果

- 前端：`tsc -b` 0 错误；`i18n:check` 未超基线（0 处 / 基线 0 处）；`vitest` **648 通过 / 14 文件**；`vite build` 成功。
- 后端：`dotnet publish -c Release` 成功，产物 `ProductVersion` 与发布提交一致（本次**未执行**后端全量测试，原因见 §57.5：本机自动化环境下 VSTest 恒失败）。
- 数据库：迁移历史无新增；§50.1 四条 DDL 校验 1 / 存在 / 2 / 4 全部通过；`PRAGMA integrity_check = ok`；业务数据 9 用户 / 83 会话正常。
- 生产：四端 200、`:5008` 无监听、`:5172` 绑定 `127.0.0.1`、本次启动 stderr 无异常。

*版本 v2.2.17 · 2026-10-10 · 翻译管理导入导出闭环修复（resx 资源读取 / 键集覆盖 / 校验 / 鉴权）+ 全项目审核报告 + 计划任务自愈加固 · 生产发布*

---

## 59. 2026-10-23 版本口径统一与 scripts/css-api 直连工具（v2.3.0）

### 59.1 版本号单一真源

此前版本号散落在 `Directory.Build.props`、`frontend/package.json`、`CHANGELOG.md` 与 git tag 四处，容易出现「代码 2.3.0、文档 2.2.15」的漂移。本版统一为：

- `Directory.Build.props` 的 `<Version>` 是**唯一真源**，当前为 `2.3.0`；
- `frontend/package.json` 的 `version` 与之一致；
- `CHANGELOG.md` 必须存在对应版本段；
- 发版时以同号 git tag 标记。

发布前跑一次 `node scripts/oss-preflight.mjs --quick`，同时校验版本引用、文档链接与文案词典覆盖，避免只改一处。

### 59.2 scripts/css-api 直连工具

`scripts/css-api/` 提供对**生产实例**的直接调用脚本与端点清单（`endpoints.md` 由生产 OpenAPI 导出：594 个操作 / 50 个分组，认证入口 `POST /api/Auth/login`）。用途是联调、线上数据核对与故障复现，不参与构建与部署。

> 直连生产属于高风险操作：脚本只读取状态或执行显式指定的动作，账号口令走本地环境变量，不写进仓库。

### 59.3 功能全景文档移出仓库

面向售前/内部的「功能全景」文档不属于开源仓库内容，已移出 `docs/`，不随源码发布；公开仓库的功能概览以 [README.md](../README.md) 为准。

*版本 v2.3.0 · 2026-10-23 · 版本口径统一 + scripts/css-api 直连工具 + 功能全景文档移出仓库*

---

## 60. 2026-11-23 开源准备批次：四 IM 渠道与全站中英双语（未发布）

本批次尚未打 tag，内容以 `CHANGELOG.md` 顶部 Unreleased 段为准。这里记录架构口径与验收基线，供后续维护与发版核对。

### 60.1 四个新 IM 渠道

新增 Messenger、Telegram、LINE、iMessage，与既有微信/钉钉/飞书/WhatsApp 共用同一套会话模型：

- **来源枚举**：`SourceType` 11 = Messenger、12 = Telegram、13 = LINE、14 = iMessage；
- **每渠道四件套**：`IXxxClient` / `XxxService` / `XxxIngestionService` / `XxxSettingsProvider`，外加 `XxxWebhookParser`、`XxxController`；
- **共享注册点 A1–A17**：来源枚举、快捷标签、模块种子、权限、前端路由与菜单、会话回复链路、附件下载、自动回复、队列作业 `ProcessImMessageQueueJob`、设置页等 17 处统一登记，清单见 [渠道集成手册](integration/channel-integration-playbook.md) 与 [注册核对清单](integration/channel-registration-checklist.md)；
- **前端**：`/messenger`、`/telegram`、`/line`、`/imessage` 四个页面，模块键分别为 `messenger`、`telegram`、`line`、`imessage`，权限为 `<渠道>.view` / `<渠道>.manage`；
- **数据库**：新增迁移落渠道配置、消息映射与客户身份列，不改既有表语义。

各渠道默认配置（`NoneadCSS.Application/Configuration/*Options.cs`）：

| 渠道 | 关键默认值 |
|------|-----------|
| Messenger | Graph `v25.0`；回调 `/api/messenger`；文本 2000 字符分片；24h 窗口（提前 4h 告警）；media tag `HUMAN_AGENT`；入站媒体上限 25 MB |
| Telegram | 默认 webhook（回调 `/api/telegram/webhook`），可切长轮询；webhook 并发 40；轮询超时 25s / 批量 100；单条 4096 字符；限速 25/s 全局、1/s 单会话；会话匹配窗口 720h |
| LINE | API `https://api.line.me`，数据 API `https://api-data.line.me`；回调 `/api/line/webhook`；文本 5000 字符；单请求最多 5 条；replyToken 25s；入站媒体 100 MiB、出站 10 MiB |
| iMessage | 默认 `bridge`（BlueBubbles），另有 `amb_msp` / `amb_apple_direct`；回调 `/api/imessage/bluebubbles/webhook`；发送默认 `apple-script`（可 `private-api`）；限速 20 条/分钟；匹配窗口 168h |

共同约定：Webhook 一律先验签后读体、失败返回非 2xx 让平台重投；入站媒体流式下载并设字节上限，超限只留占位文本；出站文本按平台上限分片；客户身份按渠道 ID（PSID / Telegram user id / LINE userId / iMessage chatGuid）建档并去重。

### 60.2 微信客服改为直连

去掉 `www.nonead.com` 中转层，服务端直接调用 `api.weixin.qq.com`；回调补 `msg_signature` 校验（安全模式），48 小时客服消息窗口、模板消息/订阅通知兜底逻辑不变。部署侧不再需要中转域名，但服务器必须能直连微信接口。

### 60.3 全站中英双语

- **API 文档**：`/docs` 页面语言开关 + `/openapi/v1.json`（中文）与 `/openapi/en.json`（英文），两份均 **487 条路径**；英文由 `openapi-en-dictionary.json`（**906 条** zh→en）在生成时替换，不维护第二套注释。
- **后端响应出口**：`NoneadCSS.Api/Localization/api-message-en.json` **735 条 / 67646 B**，在响应出口做精确匹配替换；词典按 `merge` 只增不减（避免硬编码提示被误删），并支持「固定中文前缀 + 动态详情」的前缀键规则。
- **资源文件**：`Messages.resx` 与 `Messages.en.resx` 各 156 条，覆盖 156/156。
- **服务端标签目录**：渠道/来源等标签目录提供中英双语，前端按当前语言取值。
- **明确未覆盖**：种子数据、写入数据库的聊天系统消息、服务端枚举标签仍为简体中文（设计内限制），前端对这些值做展示层兜底。

### 60.4 开源准备

- **密钥与内部信息清理**：13 条规则的 `scripts/oss-secret-scan.mjs`（凭据、内网主机、盘符源码路径、用户目录、PEM 等）；`deploy/secrets.cmd` 不入库，只保留 `.example`；数据库导出前用 `scripts/oss-db-scrub.sql` 清洗副本。
- **文档链接**：206 条 `file:///` 历史机器路径已清理，`scripts/oss-doc-links.mjs check` 终检 `fileUriLinks=0`、`brokenRelativeLinks=0`。
- **聚合门禁**：`node scripts/oss-preflight.mjs` 串联密钥扫描、文档链接、文案词典覆盖三项，任一失败退出码 1，可直接挂 CI。
- **内部资料移出**：内部报告与 `docs/superpowers/` 已移出仓库，仅保留面向使用者的文档。

### 60.5 死代码清理与事件接线

- 删除 Eventy 空转层，领域事件改用**强类型事件 + 显式监听器**：当前 10 个事件对应 10 个监听器（见 §6），触发路径不再经过反射分发；
- `TriggerActionJob` 已于 2026-11-21 移除，工作流触发由强类型事件监听器承担；
- 清理未引用服务、DTO 与前端组件；`ConversationService` 批量操作保持 `ExecuteUpdateAsync` 单条 SQL。

### 60.6 验证基线（2026-11-23 实测）

| 项目 | 结果 |
|------|------|
| 后端构建 | `dotnet build NoneadCSS.Tests -c Release`：0 错误 / 34 警告 |
| 后端测试 | 124 个测试类 / 2233 个用例：2228 通过、0 失败、5 跳过 |
| 前端类型检查 | `npx tsc -b`：0 错误 |
| 前端测试 | Vitest 18 个文件 / 684 个用例全部通过 |
| 文案门禁 | `npm run i18n:check`：0 处硬编码中文 |
| 全量重建告警 | 62 条（生产 34 + 测试 28） |

> 本机 `dotnet test` 受 testhost 权限限制（`Win32Exception 5`），后端全量用例通过 `NoneadCSS.Tests/bin/Release/net10.0/BackendTestRunner.dll` 进程内执行，详见 [scripts/backend-test-runner/README.md](../scripts/backend-test-runner/README.md)。

### 60.7 已知限制与后续

- Controller 路由 API 版本控制（`/v1/` 前缀）仍未实施（TD-07）；
- 钉钉/飞书群聊接收、工作流任务持久化、SQLite 全量乐观锁仍按 [架构级遗留项技术方案](NoneadCSS-架构级遗留项技术方案-20261023.md) 排期；
- 导入导出已支持会话（不含正文）与知识库文章（Markdown）导出，导入仍为 `SupportsImport=false`；
- 2026-11-23 同步了 README、开发文档、用户手册、渠道集成文档与各专项文档的版本/计数口径。

*版本 2.3.0 · 2026-11-23 · 四 IM 渠道 + 微信直连 + 全站中英双语 + 开源准备（未发布）*
