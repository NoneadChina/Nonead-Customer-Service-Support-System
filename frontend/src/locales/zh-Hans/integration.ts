import type { Entries } from '../../i18n/types';

/**
 * 集成与运维域（波次 14）：Webhook 管理 / API 密钥 / 模块管理 / 系统工具。
 *
 * 分区：
 *   integration.common.*       —— 四页逐字相同的短文案（状态/垃圾箱/表格列头/toast 失败串）
 *   integration.webhooks.*     —— `pages/Webhooks.tsx`（含事件类型目录与编辑器）
 *   integration.apiKeys.*      —— `pages/ApiKeys.tsx`（含权限范围目录与密钥创建弹窗）
 *   integration.modules.*      —— `pages/Modules.tsx`（含模块分类目录与编辑器）
 *   integration.systemTools.*  —— `pages/SystemTools.tsx`（含工具目录与日志查看器）
 *
 * ⚠ **若干个值的首尾空格有意义**（原文是 JSX 里紧贴 `{expr}` / `<span>` / `<strong>` 的裸文本节点）：
 *   - `integration.common.paginationTotal` 的渲染两端由 JSX 自身提供，值内不 trim；
 *   - `integration.apiKeys.loadFailedPrefix` 的**英文值带尾随空格**（中文用全角冒号自带间距，
 *     英文 `Load failed:` 与后接的错误信息之间必须有空格，否则渲染成 `Load failed:xxx`）；
 *   - `integration.apiKeys.createdWarningStrong` 的**英文值带尾随空格**（同上，后接正文）。
 *   这类值**不要 trim**，也不要为了"整洁"把空格挪进 JSX——那会改变中文态渲染。
 *
 * ⚠ `integration.common.paginationTotal` 用 `{one, other}`：中文侧两态相同由纯字符串表达
 *   （`translate` 只在 locale 以 en 开头且 count===1 时取 one）。
 */
export const integration = {
  // ════════ 四页共用 ════════
  'integration.common.tabOverview': '概览',
  'integration.common.newShort': '新建',
  // 统计卡片标签（`Active` / `Inactive` 强调"在用/不在用"的数量）
  'integration.common.statsActive': '已启用',
  'integration.common.statsInactive': '已停用',
  // 状态徽章与筛选选项（`Enabled` / `Disabled` 强调开关状态）
  'integration.common.enabled': '已启用',
  'integration.common.disabled': '已停用',
  'integration.common.allStatuses': '全部状态',
  'integration.common.status': '状态',
  'integration.common.actions': '操作',
  'integration.common.name': '名称',
  // 垃圾箱（共享外壳 `TrashDialogs` 的句式在 `customers.common.trash.*`，这里只放本域名词与按钮）
  'integration.common.trash': '垃圾箱',
  'integration.common.trashEmpty': '垃圾箱为空',
  'integration.common.clearTrash': '清空垃圾箱',
  'integration.common.trashCleared': '垃圾箱已清空',
  'integration.common.clearFailed': '清空失败',
  'integration.common.restore': '恢复',
  'integration.common.restoreFailed': '恢复失败',
  'integration.common.hardDelete': '彻底删除',
  'integration.common.hardDeleteFailed': '彻底删除失败',
  'integration.common.deleted': '已删除',
  'integration.common.clickToDisable': '点击禁用',
  'integration.common.clickToEnable': '点击启用',
  'integration.common.saving': '保存中...',
  'integration.common.saveFailed': '保存失败',
  'integration.common.deleteFailed': '删除失败',
  'integration.common.operationFailed': '操作失败',
  'integration.common.toggleSucceeded': '状态切换成功',
  'integration.common.paginationTotal': '共 {count} 条，第 {page} / {total} 页',

  // ════════ Webhook 页 ════════
  'integration.webhooks.pageTitle': 'Webhook 管理',
  'integration.webhooks.subtitle': '配置 Webhook 回调，实时推送事件通知到第三方系统',
  // 垃圾箱名词短语（`TrashDialogs` 的 `itemLabel`）：一个字面量与改造前的 `itemLabel="Webhook"` 逐字相同
  'integration.webhooks.title': 'Webhook',
  'integration.webhooks.newWebhook': '新建 Webhook',
  'integration.webhooks.tabList': 'Webhook 列表',
  'integration.webhooks.listShort': '列表',
  'integration.webhooks.tabLogs': '触发日志',
  'integration.webhooks.clearShort': '清空',
  // 本页把"停用"写成「已禁用」（与 ApiKeys/Modules 的「已停用」不同，故自持键而不是上收 common）
  'integration.webhooks.disabled': '已禁用',
  'integration.webhooks.searchPlaceholder': '搜索名称或 URL...',
  'integration.webhooks.globalMailbox': '全局',

  // 表格列头
  'integration.webhooks.columns.event': '事件',
  'integration.webhooks.columns.method': '方法',
  'integration.webhooks.columns.time': '时间',
  'integration.webhooks.columns.duration': '耗时',
  'integration.webhooks.columns.statusCode': '状态码',
  'integration.webhooks.columns.triggerSuccessFail': '触发/成功/失败',

  // 投递结果（徽章与移动端计数标签共用同一对词）
  'integration.webhooks.success': '成功',
  'integration.webhooks.failed': '失败',
  'integration.webhooks.triggerLabel': '触发',

  // 空态
  'integration.webhooks.empty': '暂无 Webhook',
  'integration.webhooks.noTriggers': '暂无触发记录',
  'integration.webhooks.noLogs': '暂无日志记录',

  // 概览
  'integration.webhooks.overview.totalWebhooks': 'Webhook 总数',
  'integration.webhooks.overview.totalTriggers': '总触发次数',
  'integration.webhooks.overview.successRate': '成功率',
  'integration.webhooks.overview.distribution': '分布统计',
  'integration.webhooks.overview.globalWebhooks': '全局 Webhook',
  'integration.webhooks.overview.mailboxWebhooks': '邮箱专属',
  'integration.webhooks.overview.successCount': '成功次数',
  'integration.webhooks.overview.failCount': '失败次数',
  'integration.webhooks.overview.eventTypeDistribution': '事件类型分布',
  'integration.webhooks.overview.recentTriggers': '最近触发',

  // 测试弹窗
  'integration.webhooks.testResult.title': '测试结果',
  'integration.webhooks.testResult.sentSuccess': '发送成功',
  'integration.webhooks.testResult.sentFailed': '发送失败',
  'integration.webhooks.testResult.responseBody': '响应内容',
  'integration.webhooks.action.test': '测试',

  // 编辑器
  'integration.webhooks.editor.editTitle': '编辑 Webhook',
  'integration.webhooks.editor.namePlaceholder': '例如：新会话通知',
  'integration.webhooks.editor.callbackUrl': '回调 URL',
  'integration.webhooks.editor.method': '请求方法',
  'integration.webhooks.editor.events': '触发事件',
  'integration.webhooks.editor.headers': '自定义请求头（JSON 格式）',
  'integration.webhooks.editor.secret': '签名密钥（可选）',
  'integration.webhooks.editor.secretPlaceholder': '用于生成 X-Webhook-Signature 签名',
  'integration.webhooks.editor.secretHint': '留空则不启用签名验证',
  'integration.webhooks.editor.mailbox': '所属邮箱',
  'integration.webhooks.editor.mailboxAllOption': '全部邮箱（全局）',
  // 注：同一 select 里 `技术支持`/`销售咨询` 两项是**邮箱名（数据）**，按队长波次 14 裁定保留为
  // 页面内的可读字面量并登记为接受例外，**不要**为它们建词条（详见 Webhooks.tsx 该处注释）。
  'integration.webhooks.editor.enableWebhook': '启用此 Webhook',
  'integration.webhooks.editor.errNameRequired': '请输入名称',
  'integration.webhooks.editor.errUrlRequired': '请输入 URL',
  'integration.webhooks.editor.errEventsRequired': '请至少选择一个事件',
  'integration.webhooks.editor.errHeaderValueType':
    '请求头「{key}」的值必须是字符串（当前是 {type}），请用引号包裹，例如 "value"',
  'integration.webhooks.editor.errHeadersJson': '请求头 JSON 格式不正确',
  'integration.webhooks.editor.create': '创建',

  // 结果提示
  'integration.webhooks.toast.movedToTrash': 'Webhook 已移入垃圾箱',
  'integration.webhooks.toast.restored': '已恢复 Webhook',
  'integration.webhooks.toast.hardDeleted': '已彻底删除',
  'integration.webhooks.toast.updated': 'Webhook 更新成功',
  'integration.webhooks.toast.created': 'Webhook 创建成功',

  // ── 事件类型目录 ──
  // 产出方：后端 `IWebhookService.TriggerAsync` 的调用点（9 码，见波次 14 报告 §4）。
  // 后端**不提供目录端点**（`WebhooksController` 无 event-types 路由），编辑器选项即前端唯一目录；
  // 9 码与后端实际产出的 9 码一一对应，0 缺 0 余。
  // ⚠ 不复用 `notifications.eventType.*`：那是**通知**事件目录（22 项、语义化键名），
  //   与本目录的 snake_case 码集不同源，且同一码的措辞也不同（如通知 `conversationUpdated`
  //   =「会话已更新」 vs 本目录 `conversation_updated` =「会话更新」），共享会改中文实词。
  'integration.webhooks.eventCategory.conversation': '会话',
  'integration.webhooks.eventCategory.customer': '客户',
  'integration.webhooks.eventCategory.user': '用户',
  'integration.webhooks.event.conversationCreated': '会话创建',
  'integration.webhooks.event.conversationUpdated': '会话更新',
  'integration.webhooks.event.conversationDeleted': '会话删除',
  'integration.webhooks.event.conversationStatusChanged': '会话状态变更',
  'integration.webhooks.event.customerCreated': '客户创建',
  'integration.webhooks.event.customerUpdated': '客户更新',
  'integration.webhooks.event.threadCreated': '会话回复创建',
  'integration.webhooks.event.userCreated': '用户创建',
  'integration.webhooks.event.userUpdated': '用户更新',

  // ════════ API 密钥页 ════════
  'integration.apiKeys.pageTitle': 'API 密钥管理',
  'integration.apiKeys.subtitle': '管理 API 访问密钥，用于第三方系统集成',
  // 垃圾箱名词短语（`TrashDialogs` 的 `itemLabel`）：与改造前的 `itemLabel="API 密钥"` 逐字相同
  'integration.apiKeys.title': 'API 密钥',
  'integration.apiKeys.newKey': '新建密钥',
  'integration.apiKeys.tabList': '密钥列表',
  'integration.apiKeys.tabActive': '活跃密钥',
  // ⚠ 英文值带尾随空格（后接接口返回的错误信息，中文用全角冒号自带间距）
  'integration.apiKeys.loadFailedPrefix': '加载失败：',
  'integration.apiKeys.error.overviewLoadFailed': '加载 API 密钥概览失败',
  'integration.apiKeys.error.listLoadFailed': '加载 API 密钥列表失败',
  'integration.apiKeys.error.trashLoadFailed': '加载回收站密钥失败',
  'integration.apiKeys.statTotalKeys': '密钥总数',
  'integration.apiKeys.statTotalUsage': '总调用次数',
  'integration.apiKeys.recentKeys': '最近创建的密钥',
  'integration.apiKeys.noKeys': '暂无密钥',
  'integration.apiKeys.scopesDistribution': '权限范围分布',
  'integration.apiKeys.columns.keyPrefix': '密钥前缀',
  'integration.apiKeys.columns.createdBy': '创建人',
  'integration.apiKeys.columns.usageCount': '调用次数',
  'integration.apiKeys.columns.createdAt': '创建时间',
  'integration.apiKeys.noApiKeys': '暂无 API 密钥',
  'integration.apiKeys.searchPlaceholder': '搜索密钥名称...',
  'integration.apiKeys.callLabel': '调用',

  // 密钥创建成功弹窗
  'integration.apiKeys.created.title': 'API 密钥创建成功',
  'integration.apiKeys.created.saved': '我已保存',
  // ⚠ 英文值带尾随空格（`<strong>` 后紧接正文文本节点）
  'integration.apiKeys.created.warningStrong': '重要：',
  'integration.apiKeys.created.warningBody':
    '请立即复制并妥善保存此密钥。关闭此窗口后将无法再次查看完整密钥。',
  'integration.apiKeys.created.keyLabel': '密钥',
  'integration.apiKeys.created.copy': '复制',

  // 编辑器
  'integration.apiKeys.editor.editTitle': '编辑 API 密钥',
  'integration.apiKeys.editor.createTitle': '新建 API 密钥',
  'integration.apiKeys.editor.name': '密钥名称',
  'integration.apiKeys.editor.namePlaceholder': '输入密钥名称',
  'integration.apiKeys.editor.description': '描述',
  'integration.apiKeys.editor.descriptionPlaceholder': '输入密钥描述（可选）',
  'integration.apiKeys.editor.scopes': '权限范围',
  'integration.apiKeys.editor.activateNow': '立即启用',
  'integration.apiKeys.editor.update': '更新',
  'integration.apiKeys.editor.create': '创建',
  'integration.apiKeys.editor.errNameRequired': '请输入密钥名称',
  'integration.apiKeys.editor.errScopesRequired': '请至少选择一个权限范围',

  // 重新生成确认
  'integration.apiKeys.regenerate': '重新生成',
  'integration.apiKeys.regenerateTitle': '重新生成密钥',
  'integration.apiKeys.regenerateMessage':
    '确定要重新生成此 API 密钥吗？重新生成后旧密钥将立即失效，使用旧密钥的所有集成需要更新。',

  // 结果提示
  'integration.apiKeys.toast.updated': 'API 密钥更新成功',
  'integration.apiKeys.toast.created': 'API 密钥创建成功，请妥善保存密钥',
  'integration.apiKeys.toast.regenerated': '密钥已重新生成，请妥善保存',
  'integration.apiKeys.toast.regenerateFailed': '重新生成失败',
  'integration.apiKeys.toast.movedToTrash': 'API 密钥已移入垃圾箱',
  'integration.apiKeys.toast.restored': 'API 密钥已恢复',
  'integration.apiKeys.toast.hardDeleted': 'API 密钥已彻底删除',
  'integration.apiKeys.toast.copied': '已复制到剪贴板',
  'integration.apiKeys.toast.copyFailed': '复制失败，请手动选择文本复制',

  // ── 权限范围目录 ──
  // 产出方：**后端无枚举/白名单**（`ApiKeyService` 把 `Scopes` 逗号拼接后原样存取，
  // 全仓唯一的取值引用是 `DatabaseSeeder.cs:2335` 的 `conversations:read,conversations:write`）。
  // 因此本表 = 前端 UI 目录，12 码即全集。
  // ⚠ **不与 `teamPermissions.*` 同源**：细粒度权限用点号（`webhooks.manage`，见
  //   `utils/permissions.ts` 的 `PermissionKey`），而 API 作用域用冒号（`webhooks:manage`），
  //   同一概念两种字符串 ⇒ 语义与用法都不同，复用会张冠李戴。
  'integration.apiKeys.scopeCategory.conversations': '会话',
  'integration.apiKeys.scopeCategory.customers': '客户',
  'integration.apiKeys.scopeCategory.users': '用户',
  'integration.apiKeys.scopeCategory.mailboxes': '邮箱',
  'integration.apiKeys.scopeCategory.tags': '标签',
  'integration.apiKeys.scopeCategory.reports': '报表',
  'integration.apiKeys.scopeCategory.webhooks': 'Webhook',
  'integration.apiKeys.scopeCategory.admin': '管理员',
  'integration.apiKeys.scope.conversationsRead': '会话 - 读取',
  'integration.apiKeys.scope.conversationsWrite': '会话 - 写入',
  'integration.apiKeys.scope.customersRead': '客户 - 读取',
  'integration.apiKeys.scope.customersWrite': '客户 - 写入',
  'integration.apiKeys.scope.usersRead': '用户 - 读取',
  'integration.apiKeys.scope.usersWrite': '用户 - 写入',
  'integration.apiKeys.scope.mailboxesRead': '邮箱 - 读取',
  'integration.apiKeys.scope.tagsRead': '标签 - 读取',
  'integration.apiKeys.scope.tagsWrite': '标签 - 写入',
  'integration.apiKeys.scope.reportsRead': '报表 - 读取',
  'integration.apiKeys.scope.webhooksManage': 'Webhook - 管理',
  'integration.apiKeys.scope.adminFull': '管理员 - 完全权限',

  // ════════ 模块管理页 ════════
  'integration.modules.pageTitle': '模块管理',
  'integration.modules.subtitle': '管理系统的所有功能模块，启用或停用扩展功能，灵活定制您的客服系统',
  'integration.modules.tabList': '模块列表',
  'integration.modules.newModule': '新建模块',
  'integration.modules.statTotal': '模块总数',
  'integration.modules.byCategory': '按分类统计',
  'integration.modules.countTotal': '总数',
  'integration.modules.empty': '暂无模块',
  'integration.modules.searchPlaceholder': '搜索模块名称、描述...',
  'integration.modules.allCategories': '全部分类',
  'integration.modules.systemModule': '核心模块',
  'integration.modules.updatedAt': '更新于',
  'integration.modules.previousPage': '上一页',
  'integration.modules.nextPage': '下一页',
  // 分页行：`{prefix}<span>{n}</span>{middle}<span>{page}</span>{suffix}`
  // 值内的空格**有意保留**（原文是紧贴 `<span>` 的裸文本节点，裁掉会渲染成 `共26条`）
  'integration.modules.paginationPrefix': '共 ',
  'integration.modules.paginationMiddle': ' 条，第 ',
  'integration.modules.paginationSuffix': '/{total} 页',

  // 编辑器
  'integration.modules.editor.editTitle': '编辑模块',
  'integration.modules.editor.name': '模块名称',
  'integration.modules.editor.namePlaceholder': '例如: telegram',
  'integration.modules.editor.title': '模块标题',
  'integration.modules.editor.titlePlaceholder': '例如: Telegram 集成',
  'integration.modules.editor.description': '模块描述',
  'integration.modules.editor.descriptionPlaceholder': '模块功能简介...',
  'integration.modules.editor.version': '版本',
  'integration.modules.editor.author': '作者',
  'integration.modules.editor.authorPlaceholder': '作者名称',
  'integration.modules.editor.category': '分类',
  'integration.modules.editor.icon': '图标',
  'integration.modules.editor.iconPlaceholder': '图标名称',
  'integration.modules.editor.sortOrder': '排序',
  'integration.modules.editor.errNameRequired': '请输入模块名称',
  'integration.modules.editor.errTitleRequired': '请输入模块标题',

  // 删除确认
  'integration.modules.confirmDeleteTitle': '确认删除',
  'integration.modules.confirmDeleteMessage': '确定要删除此模块吗？此操作不可撤销。',

  // 行内动作
  'integration.modules.action.disable': '停用',
  'integration.modules.action.enable': '启用',
  'integration.modules.err.systemCannotDisable': '核心模块无法停用',
  'integration.modules.err.systemCannotDelete': '核心模块无法删除',

  // 结果提示
  'integration.modules.toast.updated': '模块更新成功',
  'integration.modules.toast.created': '模块创建成功',
  'integration.modules.toast.deleted': '模块已删除',
  'integration.modules.toast.disabled': '模块已停用',
  'integration.modules.toast.enabled': '模块已启用',

  // ── 模块分类目录 ──
  // 产出方：`ModuleService.GetCategoryLabel`（`ModuleService.cs:200-212`）的 6 码 switch，
  // 未命中回落 code 本身。`Module.Category` 是自由字符串（创建请求原样入库），故这 6 码是
  // "UI 下拉可选值"，不是后端枚举；概览页的 `CategoryLabel` 字段由前端按本表取词（见报告 §4）。
  'integration.modules.category.integration': '集成',
  'integration.modules.category.communication': '通讯',
  'integration.modules.category.reports': '报表',
  'integration.modules.category.tools': '工具',
  'integration.modules.category.general': '通用',
  'integration.modules.category.automation': '自动化',

  // ════════ 系统工具页 ════════
  'integration.systemTools.pageTitle': '系统工具',
  'integration.systemTools.subtitle': '执行系统维护和清理操作',
  'integration.systemTools.confirmExecute': '确定要执行「{name}」吗？',
  'integration.systemTools.action.execute': '执行',
  'integration.systemTools.action.executing': '执行中...',
  'integration.systemTools.toast.succeeded': '执行成功',
  'integration.systemTools.toast.failed': '执行失败，请稍后重试',
  'integration.systemTools.toast.logsCleared': '日志文件已清空',
  'integration.systemTools.toast.clearLogsFailed': '清空日志文件失败，请稍后重试',
  'integration.systemTools.toast.logsLoadFailed': '获取日志失败',
  'integration.systemTools.clearLogsConfirm':
    '确定要清空日志文件吗？此操作将删除系统日志中的所有 log 记录（文件截断为 0 字节，不可恢复），请谨慎操作。',

  // 工具目录（`id` 是后端路由的最后一段，见 `SystemController.cs` 的 `tools/*`；`clear-logs-files` 为前端自持 id）
  // 产出方：后端返回**结果消息自由文本**（`SystemController.cs:282/312/348/450-452`），不在本表建键 → 见报告 §4。
  'integration.systemTools.tool.clearCache.name': '清理缓存',
  'integration.systemTools.tool.clearCache.description': '清理临时文件、过期令牌和旧日志文件',
  'integration.systemTools.tool.optimizeDb.name': '优化数据库',
  'integration.systemTools.tool.optimizeDb.description': '执行 VACUUM 并清理已删除的旧会话',
  'integration.systemTools.tool.logoutUsers.name': '强制所有用户登出',
  'integration.systemTools.tool.logoutUsers.description': '使所有用户的登录会话失效',

  // 统计卡片
  'integration.systemTools.stat.total': '总日志',
  'integration.systemTools.stat.normal': '正常',
  'integration.systemTools.stat.warning': '警告',
  'integration.systemTools.stat.error': '错误',
  'integration.systemTools.health': '健康度',
  'integration.systemTools.logTitle': '系统日志',

  // 日志查看器工具栏
  'integration.systemTools.live.pauseTitle': '点击暂停实时监控',
  'integration.systemTools.live.startTitle': '点击开启实时监控（每 2 秒刷新）',
  'integration.systemTools.live.pause': '暂停',
  'integration.systemTools.live.start': '实时',
  'integration.systemTools.refresh.disabledTitle': '实时监控中，手动刷新已禁用',
  'integration.systemTools.refresh.title': '手动刷新日志',
  'integration.systemTools.refresh.refreshing': '刷新中...',
  'integration.systemTools.refresh.label': '刷新',
  'integration.systemTools.download.title': '下载筛选后的日志文件',
  'integration.systemTools.download.label': '下载',
  'integration.systemTools.clearView.title': '清空当前日志视图（不影响日志文件）',
  'integration.systemTools.clearView.label': '清空',
  'integration.systemTools.clearFiles.title':
    '删除系统日志中的 log 记录（清空日志文件内容，不可恢复）',
  'integration.systemTools.clearFiles.label': '清空日志文件',
  'integration.systemTools.polling.every2s': '每2s',
  'integration.systemTools.polling.stopped': '停止',
  // 级别筛选第一个胶囊；其余胶囊显示 `LEVEL_STYLES` 的级别名（INFO/WARN/ERROR/DEBUG/FATAL），
  // 那些是两种语言都相同的技术标识，不入字典。
  'integration.systemTools.filter.all': '全部',
  'integration.systemTools.searchPlaceholder': '搜索日志...',

  // 空态与浮动按钮
  'integration.systemTools.empty.noLogs': '暂无日志',
  'integration.systemTools.empty.noMatch': '没有匹配的日志',
  'integration.systemTools.clearFilters': '清除筛选',
  'integration.systemTools.newLogs.title': '查看 {count} 条新日志',
  'integration.systemTools.newLogs.suffix': '新',
  'integration.systemTools.scrollToBottom.title': '滚动到底部并恢复自动跟踪',
  'integration.systemTools.scrollToBottom.label': '最新',
  'integration.systemTools.footerWarning':
    '这些操作可能会影响系统运行，请谨慎使用。建议在执行前备份重要数据。',
} satisfies Entries;
