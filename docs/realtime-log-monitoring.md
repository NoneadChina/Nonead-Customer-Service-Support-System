# 系统日志实时监控优化方案

> **状态（2026-11-23 复核，v2.3.0 工作树）**：方案已落地——系统工具 → 系统日志提供实时尾随、按级别过滤与「清空日志文件」（后端 `SystemController` + Serilog `shared:true`）。本文保留为设计说明，操作步骤以用户手册与 `README.md` 的「系统工具」入口为准。

> 文件位置：`docs/realtime-log-monitoring.md`
> 对应代码：`frontend/src/pages/SystemTools.tsx`、`NoneadCSS.Api/Controllers/SystemController.cs`、`NoneadCSS.Api/Program.cs`
> 最近更新：2026-08-28（配合 v2.2.9 知识库 P1 Bugfix 排障场景补充：日志中 `SQLite Error 1: no such table` / `no such column` 两类 schema 漂移错误的定位 → 手动 DDL 热修复 → Program.cs 幂等补丁回填三段式；新增 §七「常见 schema 漂移日志指纹与热修复速查表」）

---

## 一、背景与目标

「系统管理 → 系统工具 → 系统日志」页面原为一次性加载 50 行的静态列表，存在以下问题：

1. 无实时性，问题发生时需手动刷新
2. 早期接入实时轮询后命中全局限流（300 次/分钟），频繁报 429
3. 界面单调，缺少日志健康度、错误告警、新日志提示等运维视角
4. 无搜索、筛选、下载、清空等运维操作能力

优化目标：构建一个**专业终端风格**的实时日志监控界面，兼顾性能、稳定性和可视化体验。

---

## 二、整体架构

```
┌─────────────────────────────────────────────────────────────┐
│                       前端 SystemTools.tsx                  │
│                                                             │
│  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌────────┐ │
│  │ 统计卡片区 │→ │ 健康度进度 │→ │ 终端标题栏 │→ │ 日志体 │ │
│  │ 4 列 KPI  │  │  条        │  │ + 工具栏   │  │  区    │ │
│  └────────────┘  └────────────┘  └────────────┘  └────────┘ │
│         ▲                                  ▲                 │
│         │                                  │                 │
│         └────────── useMemo 派生 ──────────┘                 │
│                                                             │
│   状态：logs / liveMode / autoScroll / searchQuery /        │
│         levelFilter / newLogCount / errorPulse / lastUpdate │
└─────────────────────────────────────────────────────────────┘
                            │
                            │  每 2s 轮询
                            ▼
┌─────────────────────────────────────────────────────────────┐
│              后端 SystemController.GetSystemLogs            │
│                                                             │
│  [EnableRateLimiting("SystemLogsPolicy")]                   │
│   └─ 120 次/分钟/IP，独立于全局 300/分钟                    │
│                                                             │
│  1. 双路径查找 logs 目录（ContentRoot / CurrentDirectory）  │
│  2. 仅读取 Serilog 的 log-*.log 文件                         │
│  3. 显式 UTF-8 解码                                          │
│  4. 按时间戳排序取最新 N 行                                  │
└─────────────────────────────────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│                     Serilog File Sink                       │
│   位置：Production/logs/log-*.log（UTF-8 without BOM）      │
│   排除：backend-stdout.log（GBK，会乱码）                    │
└─────────────────────────────────────────────────────────────┘
```

---

## 三、核心功能清单

### 3.1 顶部统计卡片（4 列 KPI）

| 卡片 | 数据源 | 配色 | 特性 |
|------|--------|------|------|
| 总日志 | `parsedLogs.length` | 灰色渐变 | 基础计数 |
| 正常 INFO | `levelCounts['INF']` | 绿色渐变 | 健康日志 |
| 警告 WRN | `levelCounts['WRN']` | 琥珀渐变 | 提醒日志 |
| 错误 ERR+FTL | `levelCounts['ERR'] + levelCounts['FTL']` | 玫红渐变 | 有错误时数字旁带脉冲红点；实时模式下收到新错误时整张卡片高亮闪烁 1.5s |

### 3.2 健康度进度条

- 计算公式：`healthy = round((total - errors - warnings) / total * 100)`
- 颜色档位：
  - `≥90%`：emerald → green 渐变
  - `≥70%`：amber → orange 渐变
  - `<70%`：rose → red 渐变
- 过渡动画 500ms，平滑变化

### 3.3 终端窗口标题栏

- macOS 风格三圆点（红/黄/绿），实时模式下变为更柔和的色调
- 终端图标（实时模式玫红渐变，静态模式深灰渐变）
- 标题 + 状态徽章：
  - **LIVE**：玫红边框 + 脉冲红点 + 可选 `+N` 新日志计数（>99 显示 `99+`）
  - **PAUSED**：灰色静态徽章

### 3.4 工具栏按钮组

| 按钮 | 行为 | 禁用条件 |
|------|------|----------|
| 实时监控 / 暂停 | 切换 `liveMode`，启动/停止 2s 轮询 | 无 |
| 刷新 | 手动拉取 50 行 | `isRefreshing \|\| liveMode` |
| 下载 | 导出筛选后日志为 `.log` 文件 | `filteredLogs.length === 0` |
| 清空 | 清空当前视图（不影响日志文件） | `logs.length === 0` |

### 3.5 状态栏

- 左：轮询频率（每 2s / 已停止）、最后更新时间、`filteredLogs.length/logs.length 行`
- 中：级别筛选胶囊（全部/INF/WRN/ERR/DBG），每项显示该级别计数，激活态使用对应配色
- 右：搜索框，聚焦时宽度从 `w-40` 扩展到 `w-56`，支持一键清空

### 3.6 日志内容区

- 黑色终端背景（`bg-gray-950` / `dark:bg-black`）
- 每行结构：行号 + 级别标签（带圆点） + 时间戳 + 内容
- 级别配色：

  ```ts
  INF: emerald-400     // 正常
  WRN: amber-400       // 警告
  ERR: rose-400        // 错误
  DBG: sky-400         // 调试
  FTL: fuchsia-400     // 致命
  UNKNOWN: slate-400   // 未识别
  ```

- 行 hover 高亮（`hover:bg-white/5`）
- 最大高度 32rem，垂直滚动

### 3.7 新日志浮动提示按钮

触发条件：`!autoScroll || (newLogCount > 0 && liveMode)`

- **用户上滚离开底部**：灰色按钮"最新"，点击回到底部
- **实时模式有新日志且用户不在底部**：玫红渐变按钮"N 条新日志"，带 `animate-pulse` 脉冲动画，点击跳转底部并清零计数

### 3.8 错误脉冲告警

实时模式下检测到新的 ERR/FTL 日志时：
1. 顶部统计卡区域出现玫红色 `ring-2 ring-rose-400/50` 高亮
2. 错误卡片背景变为 `bg-rose-50`
3. 错误图标放大 `scale-110` + 阴影增强
4. 1.5s 后自动恢复

---

## 四、关键代码结构

### 4.1 类型定义

```ts
type LogLevel = 'INF' | 'WRN' | 'ERR' | 'DBG' | 'FTL' | 'UNKNOWN';

interface LogLine {
  raw: string;       // 原始行
  level: LogLevel;   // 解析后的级别
  timestamp: string; // 时间戳
  content: string;   // 日志正文
}
```

### 4.2 日志解析

支持两种格式：

| 格式 | 示例 | 正则 |
|------|------|------|
| Serilog File | `2026-08-12 11:40:40.778 +08:00 [INF] 内容` | `/^(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:\s+[+-]\d{2}:\d{2})?)\s*\[(\w+)\]\s*(.*)$/` |
| Console | `[11:40:40 INF] 内容` | `/^\[(\d{2}:\d{2}:\d{2})\s+(\w+)\]\s*(.*)$/` |

### 4.3 状态管理

```ts
// 基础状态
const [logs, setLogs] = useState<string[]>([]);
const [liveMode, setLiveMode] = useState(false);
const [autoScroll, setAutoScroll] = useState(true);
const [searchQuery, setSearchQuery] = useState('');
const [levelFilter, setLevelFilter] = useState<LogLevel | 'ALL'>('ALL');
const [lastUpdate, setLastUpdate] = useState<Date | null>(null);

// 增量监控状态
const [newLogCount, setNewLogCount] = useState(0);   // 新日志计数
const [errorPulse, setErrorPulse] = useState(false); // 错误脉冲告警

// 派生数据（useMemo）
const parsedLogs = useMemo(() => logs.map(parseLogLine), [logs]);
const filteredLogs = useMemo(() => /* 级别+搜索筛选 */, [parsedLogs, levelFilter, searchQuery]);
const levelCounts = useMemo(() => /* 各级别计数 */, [parsedLogs]);
const stats = useMemo(() => /* total/errors/warnings/info/healthy */, [parsedLogs, levelCounts]);

// Refs
const logContainerRef = useRef<HTMLDivElement>(null);
const liveTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
const pendingFetchRef = useRef(false);  // 防止实时轮询请求堆积
```

### 4.4 实时轮询与增量合并

```ts
useEffect(() => {
  if (liveMode) {
    void fetchLogs(true);  // 立即拉取一次
    liveTimerRef.current = setInterval(() => void fetchLogs(true), 2000);
    return () => clearInterval(liveTimerRef.current);
  }
}, [liveMode]);
```

增量合并逻辑（`fetchLogs` 内）：

1. 取上一批最后一行 `lastLine`
2. 在新批次中查找 `lastLine` 的位置 `lastIdx`
3. 若找到且非末尾，追加 `parsed.slice(lastIdx + 1)`
4. 统计新增行中的 ERR/FTL，触发 `errorPulse`
5. 累加 `newLogCount`
6. 超过 500 行时丢弃最旧的

### 4.5 自动滚动与新日志计数

```ts
useEffect(() => {
  if (autoScroll && logContainerRef.current) {
    logContainerRef.current.scrollTop = logContainerRef.current.scrollHeight;
    if (newLogCount > 0) setNewLogCount(0);  // 用户在底部，新日志已可见，清零
  }
}, [logs, autoScroll]);
```

用户手动滚动检测：

```ts
const handleScroll = () => {
  const el = logContainerRef.current;
  if (!el) return;
  const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 30;
  setAutoScroll(atBottom);
};
```

### 4.6 下载日志

```ts
const downloadLogs = useCallback(() => {
  const content = filteredLogs.map(l => l.raw).join('\n');
  const blob = new Blob([content], { type: 'text/plain;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `system-logs_${new Date().toISOString().slice(0,19).replace(/[:-]/g,'').replace('T','_')}.log`;
  document.body.appendChild(a); a.click(); document.body.removeChild(a);
  URL.revokeObjectURL(url);
}, [filteredLogs]);
```

---

## 五、后端关键实现

### 5.1 独立限流策略

`NoneadCSS.Api/Program.cs`：

```csharp
// 系统日志查看限流：每 IP 每分钟最多 120 次
// 实时监控每 2 秒 1 次 = 30 次/分钟，留 4 倍余量
// 独立于全局限流器（300/分钟），避免与全局配额争抢
options.AddPolicy("SystemLogsPolicy", context =>
{
    var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    return RateLimitPartition.GetSlidingWindowLimiter(ipAddress, _ => new SlidingWindowRateLimiterOptions
    {
        PermitLimit = 120,
        Window = perMinuteWindow,
        SegmentsPerWindow = 6,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        QueueLimit = 0
    });
});
```

**设计要点**：
- 独立于全局限流，保证实时监控稳定
- 滑动窗口 6 段，平滑限流
- `QueueLimit = 0`：超限立即拒绝，不排队等待

### 5.2 日志读取

`SystemController.cs` 的 `GetSystemLogs`：

```csharp
[HttpGet("tools/logs")]
[RequireSuperAdmin]
[EnableRateLimiting("SystemLogsPolicy")]
public IActionResult GetSystemLogs([FromQuery] int lines = 100)
{
    lines = Math.Min(Math.Max(lines, 1), 1000);  // 防 DoS

    // 双路径查找：ContentRootPath/logs 优先，回退 CurrentDirectory/logs
    var candidatePaths = new[]
    {
        Path.Combine(_environment.ContentRootPath, "logs"),
        Path.Combine(Directory.GetCurrentDirectory(), "logs")
    };
    var logsPath = candidatePaths.FirstOrDefault(Directory.Exists);

    // 仅读取 Serilog 的 log-*.log，排除 GBK 编码的 backend-stdout.log
    var logFiles = Directory.GetFiles(logsPath, "log-*.log")
        .OrderByDescending(f => File.GetLastWriteTime(f))
        .Take(3)
        .ToList();

    // FileShare.ReadWrite 避免与写入进程冲突
    // 显式 UTF-8 解码（Serilog 使用 UTF-8 without BOM）
    using var fileStream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    using var reader = new StreamReader(fileStream, Encoding.UTF8);

    // 按时间戳排序取最新 N 行
    return Ok(new { logs = sortedLogs });
}
```

**为什么需要双路径查找**：
- 生产环境 `start-prod.ps1` 设置 `ASPNETCORE_CONTENTROOT=backend/`，但 `WorkingDirectory=Production/`
- Serilog File sink 的相对路径 `logs/log-.log` 相对于 `Directory.GetCurrentDirectory()` 解析
- `_environment.ContentRootPath` 可能与工作目录不同

**为什么只读 `log-*.log`**：
- `backend-stdout.log` 由 PowerShell `Start-Process -RedirectStandardOutput` 写入
- 中文 Windows 上使用 GBK 编码，用 UTF-8 读取会乱码
- Serilog File sink 始终使用 UTF-8 without BOM

---

## 六、性能优化

### 6.1 前端

| 优化点 | 手段 | 效果 |
|--------|------|------|
| 防止请求堆积 | `pendingFetchRef` 闸门 | 上一次请求未完成时跳过本次轮询 |
| 解析缓存 | `useMemo` 缓存 `parsedLogs` | 避免每次渲染重新解析 |
| 筛选缓存 | `useMemo` 缓存 `filteredLogs` | 仅依赖变化时重算 |
| 日志上限 | 实时合并后限制 500 行 | 防止内存无限增长 |
| 手动刷新节流 | `refreshLock` + 400ms 冷却 | 防止用户狂点 |

### 6.2 后端

| 优化点 | 手段 | 效果 |
|--------|------|------|
| 行数上限 | `Math.Min(lines, 1000)` | 防 DoS |
| 仅读 3 个最新文件 | `Take(3)` | 减少磁盘 IO |
| 共享读取 | `FileShare.ReadWrite` | 不阻塞 Serilog 写入 |
| 独立限流 | `SystemLogsPolicy` 120/分钟 | 不挤占全局配额 |

---

## 七、运维与维护指南

### 7.1 修改轮询频率

修改 `SystemTools.tsx` 中 `setInterval` 的 2000（毫秒）：

```ts
liveTimerRef.current = setInterval(() => void fetchLogs(true), 2000);
```

⚠️ 同时检查后端 `SystemLogsPolicy` 的 `PermitLimit`，确保：
```
PermitLimit ≥ 轮询频率(次/分钟) × 4
```

例：改为 1 秒一次（60 次/分钟）时，`PermitLimit` 应 ≥ 240。

### 7.2 修改日志上限

前端实时合并上限在 `fetchLogs` 内：

```ts
return appended.length > 500 ? appended.slice(appended.length - 500) : appended;
```

后端单次返回上限在 `GetSystemLogs`：

```csharp
lines = Math.Min(Math.Max(lines, 1), 1000);
```

前端请求行数在 `fetchLogs`：

```ts
const data = await systemToolsApi.getLogs(isLive ? 200 : 50);
```

### 7.3 新增日志级别

1. 在 `LogLevel` 类型中加入新级别
2. 在 `LEVEL_STYLES` 中添加配色（`text` / `bg` / `dot` / `label`）
3. 在 `LOG_LEVEL_PATTERNS` 中添加正则
4. 在级别筛选胶囊的数组中加入：`['ALL', 'INF', 'WRN', 'ERR', 'DBG', '新级别']`
5. 如需计入健康度，更新 `stats` 计算逻辑

### 7.4 调整健康度阈值

修改 `stats` 计算和进度条颜色判断：

```ts
// 计算公式
const healthy = total > 0 ? Math.round(((total - errors - warnings) / total) * 100) : 100;

// 进度条颜色档位（当前：90/70）
stats.healthy >= 90  // 绿色
stats.healthy >= 70  // 琥珀
// 否则红色
```

### 7.5 生产环境验证清单

部署后按以下顺序验证：

1. 访问 `http://127.0.0.1:5173/`，登录超管账号
2. 进入「系统管理 → 系统工具」
3. 检查统计卡片是否正确显示日志总数和各级别计数
4. 点击「实时监控」按钮，确认 LIVE 徽章出现脉冲红点
5. 等待 2-4 秒，确认最后更新时间刷新
6. 在搜索框输入关键词，确认筛选生效
7. 点击级别筛选胶囊，确认仅显示对应级别
8. 向上滚动日志区，确认自动滚动暂停
9. 等待新日志产生，确认右下角出现"N 条新日志"按钮
10. 点击该按钮，确认跳转到底部
11. 点击「下载」，确认浏览器下载了 `.log` 文件
12. 点击「清空」，确认日志视图清空
13. 触发一个错误日志（如访问不存在的 API），确认错误卡片脉冲高亮 1.5s
14. 持续监控 1 分钟，确认不再出现 429 错误

### 7.6 常见问题排查

| 现象 | 可能原因 | 解决方案 |
|------|----------|----------|
| 显示「暂无日志」 | logs 目录不存在或无 `log-*.log` 文件 | 检查 `Production/logs/` 目录，确认 Serilog 已配置 File sink |
| 日志乱码 | 读取了非 Serilog 文件 | 确认 `GetSystemLogs` 仅读取 `log-*.log` 模式 |
| 429 错误 | 限流策略未生效或被全局策略拦截 | 确认 `[EnableRateLimiting("SystemLogsPolicy")]` 已添加，且 `Program.cs` 中已注册策略 |
| 实时模式不更新 | `pendingFetchRef` 卡住或后端无新日志 | 检查浏览器 Network 面板，确认请求正常返回；检查后端日志是否有写入 |
| 新日志计数不归零 | `autoScroll` 为 false 且未点击按钮 | 点击右下角"N 条新日志"按钮，或滚动到底部 |
| 错误脉冲不触发 | 正则未匹配 ERR/FTL | 检查日志格式，确认 `[ERR]` 或 `[FTL]` 标记存在 |

---

## 八、相关文件清单

| 文件 | 作用 |
|------|------|
| `frontend/src/pages/SystemTools.tsx` | 前端主页面，包含所有 UI 和实时监控逻辑 |
| `frontend/src/api/index.ts` | `systemToolsApi` 定义（`getLogs` / `clearCache` 等） |
| `NoneadCSS.Api/Controllers/SystemController.cs` | 后端日志查询接口 `GetSystemLogs` |
| `NoneadCSS.Api/Program.cs` | 限流策略 `SystemLogsPolicy` 注册 |
| `NoneadCSS.Application/Configuration/RateLimitOptions.cs` | 限流窗口配置 |
| `NoneadCSS.Api/appsettings.json` | Serilog File sink 配置（`logs/log-.log`） |
| `Production/start-prod.ps1` | 生产环境启动脚本（设置 ContentRoot 和 WorkingDirectory） |

---

## 九、后续可扩展方向

1. **WebSocket 推送**：替代轮询，实现真正的实时推送，延迟从 2s 降至毫秒级
2. **日志归档与搜索**：接入 Elasticsearch / Meilisearch，支持全文检索和历史回溯
3. **结构化日志展示**：解析 Serilog 的 JSON 模板字段，支持按属性过滤
4. **告警规则配置**：允许用户配置「某级别日志频率超过阈值时触发通知」
5. **多日志源聚合**：同时展示 backend / frontend / nginx 三类日志，按颜色区分
6. **日志书签**：允许用户标记重要日志行，便于后续回顾

---

## 十、技术选型对比表

### 10.1 实时推送方案对比（对应扩展方向 1）

| 维度 | HTTP 轮询（当前方案） | WebSocket | Server-Sent Events (SSE) | SignalR（已集成） |
|------|----------------------|-----------|--------------------------|-------------------|
| **实现复杂度** | ★ 低 | ★★★ 高 | ★★ 中 | ★★ 中 |
| **延迟** | 2 秒 | 毫秒级 | 毫秒级 | 毫秒级 |
| **双向通信** | 否（单向拉取） | 是 | 否（仅服务端推送） | 是 |
| **浏览器兼容性** | 全兼容 | 全兼容 | 除 IE 外全兼容 | 全兼容（自动降级） |
| **连接管理** | 无状态 | 需心跳保活 | 自动重连 | 内置心跳和重连 |
| **服务端资源** | 每请求新建-销毁 | 长连接占用 | 长连接占用 | 长连接占用 |
| **限流影响** | 受 120 次/分钟限制 | 不受 HTTP 限流影响 | 不受 HTTP 限流影响 | 不受 HTTP 限流影响 |
| **现有依赖** | 无 | 需新增 `System.Net.WebSockets` | 需新增中间件 | ✅ 项目已集成 SignalR |
| **代码改动量** | - | 大（前后端均需重构） | 中（后端新增 endpoint） | 小（复用现有 Hub 基础设施） |
| **推荐度** | - | ★★ | ★★★ | ★★★★（推荐） |
| **适用场景** | 低频更新 | 强实时双向场景 | 仅推送场景 | 已有 SignalR 的项目 |

**结论**：项目已集成 SignalR（用于即时通讯），**推荐复用 SignalR** 新增 `LogStreamHub`，改动最小且与现有架构一致。

---

### 10.2 日志存储与检索方案对比（对应扩展方向 2）

| 维度 | SQLite FTS5（当前 DB） | Elasticsearch | Meilisearch | Loki | Serilog File（当前方案） |
|------|----------------------|---------------|-------------|------|--------------------------|
| **部署复杂度** | ★ 零（已内置） | ★★★★ 高 | ★★ 中 | ★★★ 高 | ★ 零 |
| **资源占用** | <50MB | 2GB+ JVM | 100-300MB | 500MB+ | 几乎为零 |
| **全文检索** | ✅ FTS5 支持 | ✅ 强大 | ✅ 强大 | ⚠️ 仅标签索引 | ❌ 需手动 grep |
| **结构化查询** | SQL 语法 | JSON DSL | REST API | LogQL | ❌ 无 |
| **水平扩展** | ❌ 单机 | ✅ 原生支持 | ⚠️ 有限 | ✅ 原生支持 | ❌ 单机 |
| **数据保留** | 需手动清理 | 索引生命周期管理 | 自动清理 | 日志流压缩 | 需脚本清理 |
| **运维成本** | 极低 | 高（需 JVM 调优） | 低 | 中（需 Grafana 配合） | 极低 |
| **与现有架构契合度** | ✅ 已使用 SQLite | ❌ 需引入 JVM | ⚠️ 需新增服务 | ❌ 需完整技术栈 | ✅ 已使用 |
| **日志量上限** | 百万级 | 亿级 | 千万级 | 亿级 | 取决于磁盘 |
| **推荐度** | ★★★★ | ★★ | ★★★ | ★ | ★★ |
| **适用场景** | 中小规模日志 | 海量日志+复杂分析 | 中等规模+快速搜索 | 云原生日志聚合 | 简单查看 |

**结论**：当前日志量级（单机部署）下，**推荐 SQLite FTS5** 扩展，零运维成本；若未来日志量突破百万级，再评估 Meilisearch。

---

### 10.3 结构化日志方案对比（对应扩展方向 3）

| 维度 | 纯文本解析（当前方案） | Serilog JSON 格式化 | Seq（Serilog 专用） | Application Insights |
|------|----------------------|---------------------|---------------------|----------------------|
| **解析方式** | 正则匹配 | JSON.parse | 内置查询语言 | Kusto 查询语言 |
| **字段提取** | 时间戳+级别+正文 | 任意属性 | 任意属性+关联 ID | 任意属性+遥测上下文 |
| **按属性过滤** | ❌ 不支持 | ✅ 前端可实现 | ✅ 原生支持 | ✅ 原生支持 |
| **可视化 UI** | 自研终端界面 | 需自研 | ✅ 内置专业 UI | ✅ Azure 门户 |
| **部署成本** | 零 | 零（改配置） | 中（需部署 Seq 服务） | 高（云服务付费） |
| **数据保留** | 自定义 | 自定义 | 可配置 | 90 天默认 |
| **与 Serilog 集成** | File sink | File（JSON）+ Compact sink | ✅ 原生 Sink | ✅ 原生 Sink |
| **代码改动** | - | 仅改 `appsettings.json` | 配置 + 部署 | 配置 + 云账号 |
| **离线可用** | ✅ | ✅ | ✅ | ❌ 需联网 |
| **推荐度** | - | ★★★★（推荐） | ★★★ | ★ |
| **适用场景** | 简单运维 | 需要结构化字段 | 专业日志分析 | 云原生监控 |

**结论**：**推荐 Serilog JSON 格式化**，仅需修改 `appsettings.json` 输出模板，前端解析 JSON 后即可实现按属性过滤，零部署成本。

---

### 10.4 告警规则方案对比（对应扩展方向 4）

| 维度 | 前端轮询检测 | 后端定时任务 | 工作流引擎（已集成） | Prometheus + AlertManager |
|------|-------------|-------------|---------------------|---------------------------|
| **实现位置** | 浏览器 | 后端 BackgroundService | 复用现有 Workflow | 独立服务 |
| **触发延迟** | 2-4 秒 | 取决于轮询间隔 | 取决于轮询间隔 | 秒级 |
| **规则配置** | 硬编码或 localStorage | 数据库存储 | ✅ 现有规则 UI | YAML 配置文件 |
| **通知渠道** | 仅浏览器通知 | 邮件/IM/Webhook | ✅ 邮件/IM/Webhook 全支持 | 邮件/IM/Webhook |
| **规则复杂度** | 简单计数 | 中等 | ✅ 高（可视化编排） | 高（PromQL） |
| **资源占用** | 客户端 | 服务端少量 | 服务端少量 | 独立服务 |
| **离线可用** | ❌ 需浏览器打开 | ✅ | ✅ | ✅ |
| **与现有架构契合度** | ⚠️ 仅前端可见 | ⚠️ 需新增服务 | ✅ 复用现有工作流系统 | ❌ 需引入完整技术栈 |
| **开发成本** | 低 | 中 | 低（扩展规则类型） | 高 |
| **推荐度** | ★★ | ★★★ | ★★★★（推荐） | ★ |
| **适用场景** | 仅前端提醒 | 后端定时检查 | 已有工作流系统 | 专业监控告警 |

**结论**：项目已有完整的工作流引擎（支持邮件/IM/Webhook），**推荐扩展现有 Workflow** 新增「日志告警」触发器，复用通知通道和规则 UI。

---

### 10.5 多日志源聚合方案对比（对应扩展方向 5）

| 维度 | 单文件读取（当前方案） | 多目录扫描 | 统一日志收集器 | Docker Logs |
|------|----------------------|-----------|---------------|-------------|
| **日志源** | 仅 Serilog | backend+frontend+nginx | 所有服务 | 容器化服务 |
| **部署方式** | 零 | 零 | 需部署 Agent | 需容器化 |
| **颜色区分** | ❌ | 前端按来源着色 | 前端按来源着色 | 前端按容器着色 |
| **时间同步** | ✅ 同机时间戳 | ✅ 同机 | ⚠️ 需 NTP 校准 | ✅ 容器宿主时间 |
| **过滤能力** | 按级别 | 按级别+来源 | 按级别+来源+标签 | 按级别+容器 |
| **资源占用** | 极低 | 低 | 中 | 低 |
| **编码问题** | UTF-8 统一 | ⚠️ 需处理 GBK 文件 | UTF-8 统一 | UTF-8 统一 |
| **改动复杂度** | - | 低（扩展 GetSystemLogs） | 高（引入 Filebeat 等） | 高（需容器化改造） |
| **推荐度** | - | ★★★★（推荐） | ★★ | ★ |
| **适用场景** | 单一后端日志 | 单机多服务 | 分布式系统 | 容器化部署 |

**结论**：当前单机部署场景下，**推荐多目录扫描**，扩展 `GetSystemLogs` 支持读取 `frontend-*.log` / `nginx-*.log`，前端按来源着色区分。需注意 GBK 编码文件的 UTF-8 转换。

---

### 10.6 日志书签方案对比（对应扩展方向 6）

| 维度 | localStorage | IndexedDB | 后端数据库 | 文件标记 |
|------|-------------|-----------|-----------|---------|
| **存储位置** | 浏览器 | 浏览器 | 服务端 | 日志文件内 |
| **容量上限** | 5-10MB | 数百 MB | 无限 | 无限 |
| **跨设备同步** | ❌ | ❌ | ✅ | ✅ |
| **查询性能** | 快 | 中 | 取决于网络 | 慢（需扫描） |
| **数据持久性** | 清缓存丢失 | 清缓存丢失 | 永久 | 随日志轮转丢失 |
| **协作共享** | ❌ | ❌ | ✅ | ❌ |
| **实现复杂度** | ★ 低 | ★★ 中 | ★★ 中 | ★★★ 高 |
| **与现有架构契合度** | 无 | 无 | ✅ 复用 SQLite | 无 |
| **开发成本** | 低 | 中 | 中 | 高 |
| **推荐度** | ★★ | ★★ | ★★★★（推荐） | ★ |
| **适用场景** | 个人临时标记 | 个人大量标记 | 团队协作 | 审计追踪 |

**结论**：**推荐后端数据库存储**，新增 `LogBookmarks` 表（字段：日志哈希、备注、创建人、时间戳），支持跨设备同步和团队协作，与现有 SQLite 架构一致。

---

### 10.7 综合演进路线建议

```
近期（1-2 周内）           中期（1-2 月）              远期（3-6 月）
┌──────────────────┐    ┌──────────────────┐    ┌──────────────────┐
│ 1. Serilog JSON  │    │ 4. SignalR 推送  │    │ 6. 工作流告警    │
│    格式化（10.3） │ →  │    替代轮询（10.1）│ →  │    扩展（10.4）   │
│ 2. 多日志源聚合  │    │ 5. 日志书签      │    │ 7. SQLite FTS5   │
│    （10.5）       │    │    （10.6）       │    │    全文检索（10.2）│
│ 3. 前端结构化    │    │                  │    │                  │
│    过滤 UI       │    │                  │    │                  │
└──────────────────┘    └──────────────────┘    └──────────────────┘
   低成本快速见效          架构升级                规模化演进
```

**演进原则**：
1. **优先零部署成本方案**：Serilog JSON、多日志源聚合、前端 UI 升级
2. **复用现有基础设施**：SignalR、Workflow 引擎、SQLite
3. **避免引入重量级依赖**：暂不引入 Elasticsearch / Prometheus / Docker
4. **按日志量级驱动演进**：日志量 <10 万行/天时当前方案足够，>100 万行/天再评估专业方案

---

## 七、常见 schema 漂移日志指纹与热修复速查表

> v2.2.9 新增章节。生产环境出现 `SQLite Error 1` 时先在实时日志页按以下指纹定位，**热修复 SQL 可直接在 Python/sqlite3 中执行（SQLite DDL 即时生效，不重启后端）**，然后再回填 Program.cs 幂等补丁避免下次回归。

| 日志指纹 | 影响模块 | 根因 | 即时热修复 SQL | Program.cs 对应补丁方法 |
|---|---|---|---|---|
| `no such column: k.RowVersion` / 任意 `T.RowVersion` | 知识库 / 客户 / 会话等 | 老库升级未执行 RowVersion DDL 补丁 | `ALTER TABLE "KnowledgeCategories" ADD COLUMN "RowVersion" BLOB NULL;`（按报错表名替换） | `EnsureRowVersionColumnsAsync` |
| `no such table: KnowledgeArticleLikes` | 知识库点赞 | 迁移未跑导致点赞表缺失 | `CREATE TABLE IF NOT EXISTS "KnowledgeArticleLikes" ("UserId" INTEGER NOT NULL,"ArticleId" INTEGER NOT NULL,"CreatedAt" TEXT NOT NULL,PRIMARY KEY("UserId","ArticleId")); CREATE INDEX IF NOT EXISTS "IX_KnowledgeArticleLikes_ArticleId" ON "KnowledgeArticleLikes"("ArticleId");` | `EnsureKnowledgeArticleLikesTableAsync` |
| `no such column: CalendarEvents.AttendeesJson` | 日历参会人 | 老库 CalendarEvents 缺列 | `ALTER TABLE "CalendarEvents" ADD COLUMN "AttendeesJson" TEXT NULL; ALTER TABLE "CalendarEvents" ADD COLUMN "ReminderEmails" TEXT NULL; ALTER TABLE "CalendarEvents" ADD COLUMN "ExtrasJson" TEXT NULL;` | `EnsureCalendarEventsAttendeeColumnsAsync` |
| `no such column: OutlookSyncSettings.ClientId` | 日历-Outlook 同步 | 用户级 4 列凭据缺失 | `ALTER TABLE "OutlookSyncSettings" ADD COLUMN "TenantId" TEXT NULL; ALTER TABLE "OutlookSyncSettings" ADD COLUMN "ClientId" TEXT NULL; ALTER TABLE "OutlookSyncSettings" ADD COLUMN "ClientSecret" TEXT NULL; ALTER TABLE "OutlookSyncSettings" ADD COLUMN "RedirectUri" TEXT NULL;` | `EnsureOutlookSyncUserCredentialColumnsAsync` |
| `Nginx 502 Bad Gateway` / upstream connect failed (111) | 生产发布 | vite preview 默认绑 IPv6 `[::1]:5172`，Nginx 反代 IPv4 | 重新执行：`npx vite preview --port 5172 --host 127.0.0.1` | start-prod.ps1 第 3 阶段启动参数强制 |

**三段式 SOP（避免单点修复遗漏）**：
1. **指纹定位**：日志页等级筛选 `ERR+FTL`，关键字 `SQLite Error 1` 或 `502 Bad Gateway`。
2. **即时热修复**：按上表直接执行 SQL / 重启前端。
3. **幂等补丁回填**：热修复后立即在 `Program.cs` 对应 Ensure* 方法补齐等价语句 + `CHANGELOG.md` / `development.md §50.1` 记录。

*v2.2.9 · 2026-08-28 知识库 P1 热修复专题沉淀*
