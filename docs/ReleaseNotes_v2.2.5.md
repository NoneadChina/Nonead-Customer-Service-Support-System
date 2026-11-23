# Release Notes — v2.2.5

> **历史文档（归档）**：本文件是 v2.2.5 发布当时的记录，后续版本见 [`CHANGELOG.md`](../CHANGELOG.md) 与 `docs/development.md` 的版本章节。当前工作树为 v2.3.0（2026-10-23，未打 tag），并含四 IM 渠道与全站中英双语的开源准备批次（见 CHANGELOG `[Unreleased]`）。

**版本**: v2.2.5
**发布日期**: 2026-08-26
**上一版本**: v2.2.4 (2026-08-25)
**代码仓库**: git@<git.example.com>/<org>/noneadcss.git
**分支**: master
**部署目录**: `C:\noneadcss\Production\`
**标签**: `v2.2.5`

---

## 版本概述

v2.2.5 是 NoneadCSS（拓德客户服务与支持系统）在 v2.2.4（日历模块加固）基础上推出的**知识库模块重大升级版**。本版本围绕知识库（Knowledge）从「Markdown 渲染可用」提升到「完整内容协作平台」的目标，聚焦六大主线：

1. **Markdown / HTML 双模式编辑器**：两种源模式一键切换，marked + highlight.js 实现标准 GFM 解析与 30+ 语言代码高亮，两种模式按同一设计体系统一渲染样式。
2. **SQLite FTS5 全文搜索引擎**：创建 KnowledgeArticlesFts 虚拟表 + 三触发器自动维护，bm25 相关度排序 + snippet 上下文片段，前端全局搜索条高亮即时命中。
3. **SVG 图表安全渲染**：通过 DOMPurify 白名单放行 SVG 标签族与 style 标签，支持把 `docs/flowcharts.html` 原生 SVG 流程图直接嵌入知识库文章预览。
4. **附件管理体系完备化**：新建 KnowledgeArticleAttachments 表 + KnowledgeArticles.ContentType 字段，编辑器内已上传附件常驻列表（删除按钮始终可见防误触）+ 预览按钮，PDF 上传后直接预览。
5. **草稿自动保存与冲突检测**：localStorage 每 10s 防抖持久化，打开文章检测本地草稿与服务器版本冲突，弹出选择器；剪贴板图片一键粘贴即上传。
6. **前端类型安全收尾**：扩展 Toast 类型（info/warning）、消除 TimeTracking.tsx 未使用变量告警等遗留技术债务。

累计：
- **前端核心组件**：Knowledge.tsx（双模式编辑器 / FTS 搜索 UI / 附件管理 / 草稿 / 粘贴上传 / SVG 渲染）
- **后端数据层**：2 条 EF Core Migration（KnowledgeArticleAttachments 表 / FTS5 虚拟表 + ContentType 字段）
- **后端服务层**：KnowledgeService 附件 CRUD、KnowledgeArticleRepository.FullTextSearchAsync（FTS5 优先 + LIKE 降级）、IKnowledgeArticleRepository 接口扩展
- **DTO / Mapping**：KnowledgeSearchResult 新增 Snippet / Rank，MappingExtensions.ToDto 支持 snippet+rank
- **前端 Hook**：useTimedToast + useCrudPage toast schema 同步扩展 info/warning 类型

---

## 变更内容汇总矩阵

| 优先级 | 模块 | 功能点 | 效果 |
|--------|------|--------|------|
| ✨ P1 | 前端 · Knowledge | MD / HTML 双模式 Tab 切换 + 实时预览 | 两种编辑模式按需选用 |
| ✨ P1 | 前端 · Knowledge | marked + highlight.js 替换自定义正则解析 | GFM 全语法支持 + 代码块自动语法高亮（30+ 语言） |
| ✨ P1 | 后端 · Infrastructure | 20260825153000_AddKnowledgeArticlesFts5.cs 迁移 | KnowledgeArticlesFts 虚拟表 + 三触发器自动维护 |
| ✨ P1 | 后端 · Repository | `FullTextSearchAsync`：FTS5 MATCH / bm25 / snippet 优先，失败降级 LIKE | 搜索速度 & 相关度显著提升，中文关键字命中上下文片段 |
| ✨ P1 | 前端 · Knowledge | 顶部全局搜索条 + 300ms 防抖 + 高亮摘要卡片 | 关键字秒级命中，点击直接跳转文章 |
| ✨ P1 | 前端 · Knowledge | DOMPurify 放行 SVG 标签族（60+ 标签、120+ 属性）+ style | 可直接嵌入 flowcharts.html 原生 SVG 流程图到知识库预览 |
| ✨ P1 | 后端 · Infrastructure | 20260825150140_AddKnowledgeArticleAttachmentsAndContentType.cs 迁移 | 附件表 + ContentType 枚举字段持久化 |
| ✨ P1 | 前端 · Knowledge | 编辑器底部已上传附件常驻列表（文件名/大小/时间）+「删除」+「预览」按钮始终可见 | 删除按钮不再 hover-only，避免找不到操作入口 |
| ✨ P2 | 前端 · Knowledge | localStorage 每 10s 防抖自动保存草稿（按 articleId 分桶） | 刷新浏览器 / 断电不丢失未提交内容 |
| ✨ P2 | 前端 · Knowledge | 本地草稿 vs 服务器版本冲突检测与选择对话框 | 多人协作避免草稿被服务器版本静默覆盖 |
| ✨ P2 | 前端 · Knowledge | 编辑区 paste 事件监听 → 剪贴板图片自动上传 → 按当前模式插入 MD/HTML 图片语法 | 截图无需保存文件再上传，直接 Ctrl+V |
| ✨ P2 | 前端 · Knowledge | 大屏左右分栏（左编辑右预览）/ 小屏 Tab 自动切换 / 预览 72ch 阅读宽度 | 阅读体验接近专业 Markdown 编辑器 |
| 🐛 P2 | 前端 · TimeTracking.tsx | 移除未使用 CalendarIcon / FileSearch / X 导入 + ENTRY_TYPE_BADGE 常量 | 消除 3 条 TS6133 编译警告 |
| 🔧 P3 | 前端 · Hook | useTimedToast ToastMessage 与 useCrudPage toast schema 扩展 info / warning 类型 | 知识库「无搜索结果」等场景走 info toast 更合适 |
| 🔧 P3 | 前端 · 全局 | 所有 `JSON.parse`（草稿 / FTS 结果 / 配置）增加运行时字段类型守卫，避免 `as any` | 严格类型安全延续 v2.2.4 基线 |

---

## 详细变更清单

### 1. 知识库：Markdown / HTML 双模式编辑器

#### 1.1 编辑器双模式切换

- 新增编辑模式枚举 `ContentType`（Markdown = 0 / Html = 1），后端实体持久化到 `KnowledgeArticles.ContentType`。
- 编辑器页面顶部新增 `Markdown | HTML` 两个 Tab：
  - 切换时自动把当前内容存入对应模式下的 state，不会互相覆盖。
  - 预览区自动按目标模式重新渲染（Markdown 走 marked，HTML 走 DOMPurify.sanitize）。
- 新建文章默认 Markdown 模式；打开历史文章按服务端 ContentType 自动选择模式。

#### 1.2 Markdown 解析引擎升级（marked + highlight.js）

- 新增依赖：`marked@^18.0.11`、`highlight.js@^11.12.0`（前端 package.json 已更新）。
- 替换原有基于正则的 `renderMarkdown` 自定义实现：
  - GFM 表格语法：`| col1 | col2 |` → 带斑马纹 `<table class="md-table border-collapse border border-slate-300">`
  - 任务列表：`- [x] 完成` / `- [ ] 未完成`
  - 围栏代码块：`` ```language `` → `<pre><code class="hljs language-xxx">` 并调用 `hljs.highlightElement(codeEl)`
  - 删除线、脚注、自动链接、定义列表全部支持。
- 统一渲染样式：H1/H2 带下划线分割、blockquote 左边框引用色背景、有序列表/无序列表缩进、HR 分割线渐变色、代码块深色主题 + 16px 左内边距 + 圆角 6px。

#### 1.3 HTML 预览安全渲染 + SVG 放行

- 新增 `SVG_ALLOWED_TAGS` 常量（60+ 标签：`svg, path, rect, circle, line, polyline, polygon, ellipse, text, tspan, defs, marker, pattern, linearGradient, radialGradient, stop, g, filter, clipPath, mask, symbol, use, title, desc, switch, style, animate, animateTransform, animateMotion, foreignObject` 等）。
- 新增 `SVG_ALLOWED_ATTR` 常量（120+ 属性：`d, cx, cy, r, rx, ry, x, y, width, height, fill, stroke, stroke-width, stroke-dasharray, stroke-linecap, stroke-linejoin, transform, translate, rotate, scale, viewBox, preserveAspectRatio, points, x1, y1, x2, y2, class, id, opacity, fill-opacity, stroke-opacity, font-family, font-size, font-weight, text-anchor, dominant-baseline, dx, dy, offset, stop-color, stop-opacity, gradientUnits, gradientTransform, patternUnits, markerEnd, markerStart, markerMid, filter, clip-path, mask, href, xlink:href` 等）。
- `renderHtml(html)` 改为：
  ```ts
  DOMPurify.sanitize(html, {
    ADD_TAGS: [...SVG_ALLOWED_TAGS, 'style'],
    ADD_ATTR: SVG_ALLOWED_ATTR,
    ALLOW_DATA_ATTR: false,
    FORBID_ATTR: ['onload', 'onerror', 'onclick', 'onmouseover' /* ...所有事件属性 */],
  })
  ```
- `renderMarkdown` 的 marked 钩子 `afterSanitize` 同样应用相同 DOMPurify 配置。
- 验证：把 `docs/flowcharts.html` 中 `<svg>...</svg>` 整块粘贴进 HTML 模式编辑器，预览区按原样渲染出完整流程图，无 XSS 风险。

### 2. 知识库：SQLite FTS5 全文搜索引擎

#### 2.1 虚拟表 + 触发器迁移

- 迁移 `20260825153000_AddKnowledgeArticlesFts5.cs`：
  - 使用 `Sql("CREATE VIRTUAL TABLE \"KnowledgeArticlesFts\" USING fts5(\"Title\", \"Summary\", \"Content\", tokenize = 'unicode61 remove_diacritics 2')")`。
  - 三触发器：`after_insert / after_update / after_delete`，以 `rowid = Id` 作为业务关联键。
  - 字符串拼接构造 tokenize 子句（避免 C# 嵌套引号转义错误）。
- 生产库应用方式：生成目标 SQL 脚本，`stop-prod.ps1` 后手动对 Production/data/noneadcss.db 执行（规避历史迁移快照不匹配问题）。

#### 2.2 后端查询：FTS5 优先 + LIKE 降级

- `IKnowledgeArticleRepository` 新增接口：
  ```csharp
  Task<(List<KnowledgeArticle> Items, long TotalCount, Dictionary<long, string>? Snippets, Dictionary<long, double>? Ranks)>
      FullTextSearchAsync(string keyword, long? categoryId, int page, int pageSize,
                          CancellationToken cancellationToken = default);
  ```
- `KnowledgeArticleRepository.FullTextSearchAsync` 实现策略：
  1. 先尝试 `SELECT fts.*, bm25(KnowledgeArticlesFts) as rank, snippet(KnowledgeArticlesFts, 2, '[[', ']]', '…', 24) as snippet FROM KnowledgeArticlesFts WHERE KnowledgeArticlesFts MATCH $keyword ORDER BY rank`；
  2. 若 FTS5 MATCH 抛出异常或无结果，自动降级为 `WHERE "Title" LIKE '%keyword%' OR "Summary" LIKE '%keyword%' OR "Content" LIKE '%keyword%'`；
  3. 两条路径都带 `AND "CategoryId" = @categoryId`（可选）+ `AND "DeletedAt" IS NULL` 软删除过滤 + `LIMIT @pageSize OFFSET @skip` 分页。
- `KnowledgeService.SearchAsync` 改为直接走 `FullTextSearchAsync`，不再使用原 `GetPagedAsync` + LIKE。

#### 2.3 前端全局搜索 UI

- 知识库页面顶部新增搜索条（`.knowledge-global-search`）：
  - `useDebounce(300ms)` 避免每按键一次请求后端。
  - 输入 ≥ 2 字符触发搜索。
- 搜索结果容器：
  - 卡片式列表：标题（关键字加粗）+ Snippet 片段（`[[…]]` 分隔符转为 `<mark class="bg-yellow-200 text-slate-900 px-1 rounded">` 高亮块）+ 分类名 + Rank 可视化（5 格相关度条）。
  - 空状态走 info toast（扩展类型后的首次应用）。
  - 点击卡片：打开文章详情 Modal，搜索关键字保持在 sessionStorage，详情页首屏 scrollIntoView 到首个 `<mark>`。
- 类型定义同步：
  - `KnowledgeSearchResult`：新增 `Snippet?: string | null`、`Rank?: number | null`。
  - `ToDto` 映射扩展：按仓储返回的 Snippets / Ranks 字典填充对应字段。

### 3. 知识库：附件管理体系

#### 3.1 数据库迁移 + 实体 / DTO

- 迁移 `20260825150140_AddKnowledgeArticleAttachmentsAndContentType.cs`：
  ```sql
  CREATE TABLE "KnowledgeArticleAttachments" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_KnowledgeArticleAttachments" PRIMARY KEY AUTOINCREMENT,
    "ArticleId" INTEGER NOT NULL,
    "FileName" TEXT NOT NULL,
    "FilePath" TEXT NOT NULL,
    "FileSizeBytes" INTEGER NOT NULL,
    "MimeType" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_KnowledgeArticleAttachments_KnowledgeArticles_ArticleId"
      FOREIGN KEY ("ArticleId") REFERENCES "KnowledgeArticles" ("Id") ON DELETE CASCADE
  );
  ALTER TABLE "KnowledgeArticles" ADD "ContentType" INTEGER NOT NULL DEFAULT 0; -- 0=Markdown, 1=Html
  CREATE INDEX "IX_KnowledgeArticleAttachments_ArticleId" ON "KnowledgeArticleAttachments" ("ArticleId");
  ```
- 实体层：`KnowledgeArticle` 新增 `ContentType` 枚举属性与 `List<KnowledgeArticleAttachment> Attachments` 导航属性；新增实体 `KnowledgeArticleAttachment`（ISoftDelete 未启用，物理删除即可）。
- 服务层 `KnowledgeService`：
  - `UploadAttachmentAsync(long articleId, IFormFile file)`：25MB 上限 + MIME 白名单（pdf/doc*/xls*/ppt*/png/jpg/jpeg/gif/txt/zip）+ Path.GetFileName 过滤文件名；写入 `wwwroot/uploads/knowledge/{articleId}/{Guid.NewGuid()}_{SanitizedName}`。
  - `DeleteAttachmentAsync(long articleId, long attachmentId)`：先删物理文件，再删除 DB 记录；try-catch 隔离任一侧失败。
  - `GetAttachmentsAsync(long articleId)`：按 ArticleId 查询所有附件，返回 DTO 列表。

#### 3.2 编辑器已上传附件列表（删除按钮始终可见）

- 编辑器底部「附件管理」区域位于文件上传按钮**正下方**：
  - 卡片式布局：浅灰背景（`bg-slate-50`）+ 圆角 8px + 左侧文件类型图标（按 MIME 分支显示 PDF / 图片 / 文档图标）。
  - 每行展示：文件名 + 格式化大小（B/KB/MB，自动换算）+ 上传时间（yyyy-MM-dd HH:mm）。
  - **常驻操作按钮**（右侧始终可见，非 hover-only）：
    - 「预览」（`border border-blue-500 text-blue-600`）：MIME=application/pdf → `window.open(url, '_blank')`；图片 → 图片预览 Modal；其他类型 → 尝试浏览器打开。
    - 「删除」（`border border-red-500 text-red-600 hover:bg-red-50`）：`confirm('确认删除附件「xxx」？此操作不可撤销。')` 二次确认后立即删除并刷新列表。
  - 空状态：`暂无附件，点击上方「上传附件」添加 PDF、图片、文档等。` + 插画。

### 4. 草稿自动保存 + 冲突检测 + 图片粘贴上传

#### 4.1 localStorage 草稿持久化

- 监听 `title / content / contentType` 任意变化，`useEffect` 内 `debouncedSaveDraft(10_000)`：
  - Key：`knowledge_draft_${articleId ?? 'new'}`。
  - Value：`{ title, content, contentType, attachmentIds, updatedAt: Date.now() }`（JSON.stringify）。
- 新建文章点击保存成功后删除 `knowledge_draft_new`；编辑文章保存成功后按新 ID 重新对齐 Key。

#### 4.2 冲突检测对话框

- `useEffect` 在进入编辑页、拿到服务端返回的 `article.updatedAt` 后：
  1. 读取 localStorage 对应草稿。
  2. 若草稿存在且 `draft.updatedAt > article.UpdatedAt.getTime()`：弹出 Modal：
     - 标题：「检测到本地有更新时间更新的未保存草稿」
     - 说明：「本地草稿时间 XXX，服务器版本时间 YYY。多人同时编辑可能引起覆盖。」
     - 按钮：`加载本地草稿`（高亮） / `使用服务器版本`（次要） / `取消`。
  3. 用户选择后：加载本地 → 用草稿覆盖表单；使用服务器 → 清掉本地草稿 Key。

#### 4.3 图片粘贴即上传

- `textareaRef.addEventListener('paste', async (e) => { ... })`：
  - 若 `clipboardData.files` 存在且 `type.startsWith('image/')`：
    - `preventDefault()`，立即调 `uploadAttachment(articleId, file)` 接口。
    - 成功后按当前 `contentType` 插入语法：
      - Markdown：`![](attachmentUrl)`。
      - HTML：`<img src="attachmentUrl" alt="粘贴图片" />`。
    - 插入位置 = 当前 textarea selectionStart / selectionEnd。
  - 非图片文件：走默认粘贴行为，不拦截。

### 5. 技术债务与类型安全收尾

- **Toast 类型扩展**：
  - `useTimedToast.ts` 的 `ToastMessage`：`type: 'success' | 'error'` → `type: 'success' | 'error' | 'info' | 'warning'`；UI 对应 info=蓝、warning=橙。
  - `useCrudPage.ts` 中内部 toast schema 同步放宽同样四类型，避免：
    ```
    TS2322: Type 'ToastMessage | null' is not assignable to type '{ type: "success" | "error"; text: string } | null'.
    ```
- **TimeTracking.tsx 未使用变量清理**：移除未使用的 `CalendarIcon / FileSearch / X` 导入和 `ENTRY_TYPE_BADGE` 常量，消除 3 条 TS6133。

### 6. 数据库迁移与部署说明

#### 6.1 需要执行的新迁移（2 条）

1. `20260825150140_AddKnowledgeArticleAttachmentsAndContentType`：附件表 + ContentType 字段。
2. `20260825153000_AddKnowledgeArticlesFts5`：FTS5 虚拟表 + 触发器。

> ⚠️ 生产环境历史迁移快照不匹配的情况下，**不要直接 `dotnet ef database update`**；应 `Script-Migration` 生成目标范围 SQL，停止后端后手动执行。验证方式：
> ```sql
> SELECT COUNT(*) FROM pragma_table_info("KnowledgeArticles") WHERE name = "ContentType";    -- 期望 1
> SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='KnowledgeArticlesFts';   -- 期望 1
> SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='KnowledgeArticleAttachments'; -- 期望 1
> ```

#### 6.2 生产部署 7 步 SOP（延续 v2.2.4 基线）

1. `Production\stop-prod.ps1`：端口 + 同名残留进程双重清理。
2. `robocopy Production _backup_prod_20260826_HHMMSS /MIR /R:2 /W:1`：完整冷备份。
3. `dotnet publish NoneadCSS.Api.csproj -c Release -o Production/backend`。
4. `frontend/pnpm build` → `robocopy frontend/dist Production/frontend/dist /MIR` → `robocopy frontend/dist Production/backend/wwwroot /MIR` → `robocopy frontend/dist Production/_publish_backend/wwwroot /MIR`。
5. `sqlite3 Production/data/noneadcss.db < migrations_20260826.sql`（或手动执行）。
6. `Copy-Item -Force NoneadCSS.Api/appsettings.Production.json Production/backend/appsettings.Production.json`。
7. `Production\start-prod.ps1`（blocking=false, long_running_process）。
8. 健康检查（发布 Checklist 第 1~16 项全绿后）：
   - `:5009/health` → Healthy。
   - `:5173/` → 200，含 `<title>拓德客户服务与支持系统</title>`。
   - `:5172` vite preview LISTENING。
   - `:5008` 开发端口 NOT LISTENING（负向断言）。
   - 知识库全链路回归：新建 MD 文章 → 切换 HTML → 上传 PDF → 全文搜索关键字 → SVG 嵌入预览。

---

## 涉及变更文件清单（26 个核心文件节选）

### 后端（9）
- [NoneadCSS.Infrastructure/Migrations/20260825150140_AddKnowledgeArticleAttachmentsAndContentType.cs](../NoneadCSS.Infrastructure/Migrations/20260825150140_AddKnowledgeArticleAttachmentsAndContentType.cs)
- [NoneadCSS.Infrastructure/Migrations/20260825153000_AddKnowledgeArticlesFts5.cs](../NoneadCSS.Infrastructure/Migrations/20260825153000_AddKnowledgeArticlesFts5.cs)
- [NoneadCSS.Domain/Entities/KnowledgeArticle.cs](../NoneadCSS.Domain/Entities/KnowledgeArticle.cs)（ContentType + Attachments）
- [NoneadCSS.Domain/Repositories/IKnowledgeArticleRepository.cs](../NoneadCSS.Domain/Repositories/IKnowledgeArticleRepository.cs)（FullTextSearchAsync 接口）
- [NoneadCSS.Infrastructure/Repositories/KnowledgeArticleRepository.cs](../NoneadCSS.Infrastructure/Repositories/KnowledgeArticleRepository.cs)（FTS5 实现 + LIKE 降级）
- [NoneadCSS.Application/Services/KnowledgeService.cs](../NoneadCSS.Application/Services/KnowledgeService.cs)（附件 CRUD + 搜索改造）
- [NoneadCSS.Application/DTOs/Responses/KnowledgeDto.cs](../NoneadCSS.Application/DTOs/Responses/KnowledgeDto.cs)（Snippet / Rank 字段）
- [NoneadCSS.Application/Mappings/MappingExtensions.cs](../NoneadCSS.Application/Mappings/MappingExtensions.cs)（ToDto snippet+rank 映射）

### 前端（6）
- [frontend/src/pages/Knowledge.tsx](../frontend/src/pages/Knowledge.tsx)（双模式编辑器 / FTS 搜索 / 附件管理 / 草稿 / 粘贴上传 / SVG 渲染）
- [frontend/src/api/types/engagement.ts](../frontend/src/api/types/engagement.ts)（KnowledgeSearchResult Snippet/Rank）
- [frontend/src/hooks/useTimedToast.ts](../frontend/src/hooks/useTimedToast.ts)（info/warning 扩展）
- [frontend/src/hooks/useCrudPage.ts](../frontend/src/hooks/useCrudPage.ts)（toast schema 同步扩展）
- [frontend/src/pages/TimeTracking.tsx](../frontend/src/pages/TimeTracking.tsx)（未使用变量清理）
- [frontend/package.json](../frontend/package.json)（version 2.2.5 + marked/highlight.js 依赖版本确认）

### 文档（11）
- [CHANGELOG.md](../CHANGELOG.md)（Unreleased 归档 v2.2.4；新增 [2.2.5] 完整章节）
- [README.md](../README.md)（顶部版本升级到 v2.2.5）
- [CONTRIBUTING.md](../CONTRIBUTING.md)（页脚版本条升级到 v2.2.5）
- [docs/development.md](./development.md)（版本 + 新增第 41 章知识库模块升级）
- [docs/拓德客户服务与支持系统用户使用手册.md](./拓德客户服务与支持系统用户使用手册.md)（版本 + 知识库章节重写 + 版本历史 v2.2.5 条目）
- [docs/database-backup-restore-guide.md](./database-backup-restore-guide.md)（适用版本 ≥ v2.2.5，注明知识库新表）
- [docs/flowcharts.html](./flowcharts.html)（title + meta 版本条 v2.2.5，日期 2026-08-26）
- [docs/ReleaseNotes_v2.2.5.md](./ReleaseNotes_v2.2.5.md)（本文件）

---

## 验证矩阵（v2.2.5 发布回归）

| 分类 | 用例 | 预期结果 | 实际状态 |
|------|------|----------|----------|
| 编辑器 | 新建 MD 文章：`# 标题 \`\`\`ts const x=1 \`\`\` ` 保存并预览 | H1 渲染 + ts 代码块蓝色关键字高亮（hljs-* 类生效） | ✅ |
| 编辑器 | 切换到 HTML 模式，粘贴 `<svg viewBox="0 0 100 100"><circle cx="50" cy="50" r="40"/></svg>`，预览 | 页面出现圆形 SVG，无 DOMPurify 错误无 XSS alert | ✅ |
| FTS 搜索 | 搜索「工单」（存量文章有该词） | 搜索结果卡片标题/摘要含黄色 `<mark>`，按 bm25 rank 排序 | ✅ |
| FTS 降级 | 搜索「$不存在的词#@!」（FTS MATCH 语法不合法） | 自动走 LIKE 降级，无 500；若真无匹配显示 info toast | ✅ |
| 附件管理 | 上传 valid.pdf 后，附件列表「删除」按钮无需 hover 即可点击 | 删除按钮始终可见红色边框；点确认后列表立即刷新，后端 DB 记录 + 物理文件同步删除 | ✅ |
| 附件预览 | 点击 PDF 附件的「预览」按钮 | 新标签页打开浏览器 PDF 阅读器，文件正确 | ✅ |
| 草稿持久化 | 编辑文章 10s+ 后强制关闭浏览器，重新打开同一文章 | 冲突弹窗出现；选择「加载本地草稿」内容完整恢复 | ✅ |
| 图片粘贴 | 编辑器截图 Ctrl+V | 自动上传 → 返回 URL → 按当前模式插入 MD/HTML 图片语法，预览图片正常显示 | ✅ |
| TypeScript | `cd frontend ; npx tsc --noEmit` | 0 错误 0 警告 | ✅ |
| 后端 | `dotnet build NoneadCSS.sln -c Release` | 0 错误 0 警告 | ✅ |
| 部署 | stop-prod → publish → build → 迁移 SQL → start-prod | 端口 5009 / 5172 / 5173 LISTENING；5008 NOT LISTENING | ✅ |

---

## 升级建议

- **建议所有生产环境立即升级到 v2.2.5**：知识库功能已达到可用的专业内容协作级别。
- **升级前必须先冷备份数据库**：本版本涉及 2 条 Schema 变更（新增表+列+虚拟表+触发器），备份后再执行迁移 SQL。
- **数据迁移后必跑验证 SQL**：见 §6.1。
- **前端依赖更新**：前端执行 `cd frontend ; pnpm install` 以拉取 marked / highlight.js 新依赖（v2.2.4 → v2.2.5 新增）。

---

*发布版本*：v2.2.5  
*发布日期*：2026-08-26  
*文档版本*：v1.0（Release Notes 独立文档首次建立标准模板）
