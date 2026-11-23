import type { Entries } from '../../i18n/types';

/**
 * 数据簇中文真源（批次 2 波次 11）：审计日志 / 发送日志 / 报表统计 / 自定义报表 / 导入导出。
 *
 * 分区（命名空间即 key 前缀）：
 *   data.common.* —— 跨页逐字复用的通用文案
 *   data.audit.* —— 审计日志页
 *   data.sendLogs.* —— 发送日志页
 *   data.reports.* —— 报表统计页
 *   data.customReports.* —— 自定义报表列表页
 *   data.customReports.view.* —— 自定义报表查看页
 *   data.customReports.schedule.* —— 自定义报表调度弹窗
 *   data.customReportEditor.* —— 自定义报表编辑器
 *   data.importExport.* —— 数据导入导出页
 *
 * 必须用 `satisfies Entries`：类型标注会让本模块的键从 `TranslationKey` 静默消失
 * （`tsc` 不报错），详见 `guard.ts`。
 */
export const data = {
  // ══ data.common.* —— 跨页逐字复用的通用文案 ══
  'data.common.refresh': '刷新',
  'data.common.export': '导出',
  'data.common.exportCsv': '导出 CSV',
  'data.common.exportFailed': '导出失败',
  'data.common.exportSucceeded': '导出成功',
  'data.common.exporting': '导出中...',
  'data.common.noTrendData': '暂无趋势数据',
  'data.common.loadFailed': '加载失败',
  'data.common.loadFailedPrefix': '加载失败：',
  'data.common.all': '全部',
  'data.common.allMailboxes': '全部邮箱',
  'data.common.allTypes': '全部类型',
  'data.common.startDate': '开始日期',
  'data.common.endDate': '结束日期',
  'data.common.viewDetails': '查看详情',
  'data.common.previousPage': '上一页',
  'data.common.nextPage': '下一页',
  'data.common.ipAddress': 'IP 地址',
  'data.common.system': '系统',
  'data.common.metric.newConversations': '新建会话',
  'data.common.metric.closedConversations': '关闭会话',
  'data.common.metric.agentReplies': '客服回复',
  'data.common.metric.newCustomers': '新增客户',

  // ══ data.audit.* —— 审计日志页 ══
  // ════════ 页头（前两条是队长预置种子键，勿改名）════════
  'data.audit.title': '审计日志',
  'data.audit.description': '查看系统操作记录、实体变更历史与客户端上下文',
  'data.audit.hmacBadge': 'HMAC 签名校验',
  // 值与（已是 i18n 的）相对时间之间原有半角空格，前缀键自带尾空格以免渲染成"更新于3分钟前"
  'data.audit.updatedAtPrefix': '更新于 ',
  'data.audit.verifyIntegrityButton': '完整性校验',
  'data.audit.verifyShortButton': '校验',
  'data.audit.verifyIntegrityTitle': '批量校验当前页日志的 HMAC 签名',

  // ════════ KPI 统计卡 ════════
  'data.audit.kpi.totalLogs': '总记录',
  'data.audit.kpi.calculating': '统计中...',
  'data.audit.kpi.matchingFilters': '符合过滤条件',
  'data.audit.kpi.activeUsers': '操作用户',
  'data.audit.kpi.uniqueUsers': '独立用户数',
  'data.audit.kpi.last24h': '近 24 小时',
  'data.audit.kpi.newActions': '新增操作',
  'data.audit.kpi.anomalies': '异常事件',
  'data.audit.kpi.failedOrLocked': '登录失败/锁定',

  // ════════ 操作 / 实体类型分布图 ════════
  'data.audit.chart.actionDistribution': '操作分布（全局 Top 8）',
  'data.audit.chart.entityDistribution': '实体类型占比',

  // ════════ 筛选条 ════════
  'data.audit.filter.keyword': '关键字搜索（服务端）',
  'data.audit.filter.keywordPlaceholder': '操作 / 实体 / IP / UA...',
  'data.audit.filter.userIdPlaceholder': '如 1',
  'data.audit.filter.ipPlaceholder': '如 192.168.1.1',
  'data.audit.filter.activeHint': '当前已应用过滤条件，统计与列表均基于此过滤范围',
  'data.audit.exportTitle': '导出当前过滤条件下的审计日志为 CSV（最多 5000 条）',

  // ════════ 完整性校验报告面板 ════════
  'data.audit.integrity.reportTitle': '完整性校验报告',
  // 原 JSX 换行在 HTML 里塌缩成一个空格："， 篡改"之间的空格逐字保留
  'data.audit.integrity.summary': '共校验 {checked} 条（第 {page} 页，总计 {total} 条）， 篡改 {tampered} 条',
  'data.audit.integrity.moreSuffix': ' 条',

  // ════════ 列表：空态 / 表头 / 行内徽章 ════════
  'data.audit.empty.title': '暂无审计日志记录',
  'data.audit.empty.hint': '修改筛选条件或点击刷新按钮加载最新数据',
  'data.audit.field.time': '时间',
  'data.audit.field.user': '用户',
  'data.audit.field.action': '操作',
  'data.audit.field.entity': '实体',
  'data.audit.field.ipClient': 'IP · 客户端',
  'data.audit.field.details': '详情',
  'data.audit.signature.invalid': '签名校验失败：疑似篡改',
  'data.audit.signature.valid': '签名校验通过',
  'data.audit.userRef': '用户#{id}',
  // 无 User-Agent 时改造前渲染 '-'，仍由本键给出（保证取词路径统一，值不随语言变化）
  'data.audit.device.unknown': '-',
  'data.audit.device.desktop': '桌面',
  'data.audit.device.mobile': '移动',

  // ════════ 分页（数值由带样式的 <span> 持有，故拆成前缀 / 中缀 / 后缀）════════
  'data.audit.pagination.pagePrefix': '第 ',
  'data.audit.pagination.pageTotal': ' / {total} 页',
  'data.audit.pagination.pageSuffix': ' 页',
  'data.audit.pagination.rangePrefix': '本页 ',
  'data.audit.pagination.rangeMiddle': ' 条，共 ',
  'data.audit.pagination.rangeSuffix': ' 条',

  // ════════ 详情弹窗 ════════
  'data.audit.detail.title': '审计日志详情 #{id}',
  'data.audit.detail.verifySignature': '校验签名',
  'data.audit.detail.exportJson': '导出 JSON',
  'data.audit.signature.validLong': '签名校验通过（记录未被篡改）',
  'data.audit.signature.invalidLong': '签名校验失败（疑似篡改）',
  'data.audit.detail.verifiedAtPrefix': '校验时间：',
  'data.audit.detail.storedSignature': '存储签名',
  'data.audit.detail.expectedSignature': '期望签名',
  'data.audit.emptyValue': '(空)',
  'data.audit.field.operator': '操作人',
  'data.audit.field.automatedHint': '自动化或内部操作',
  'data.audit.field.actionType': '操作类型',
  'data.audit.field.entityType': '实体类型',
  'data.audit.field.userId': '用户 ID',
  'data.audit.field.targetEntity': '目标实体',
  'data.audit.field.entityIdPrefix': '实体 ID: ',
  'data.audit.field.networkContext': '网络与客户端上下文',
  'data.audit.field.browser': '浏览器',
  'data.audit.field.fullUserAgent': '完整 User-Agent',
  'data.audit.field.relatedLogs': '关联日志',
  'data.audit.field.sourcePrefix': '来源：',
  'data.audit.related.sameEntity': '同实体',
  'data.audit.related.sameUser': '同用户',
  'data.audit.related.none': '无',
  'data.audit.related.countMiddle': '，共 ',
  'data.audit.related.countSuffix': ' 条',
  'data.audit.related.empty': '暂无关联日志',
  'data.audit.field.changeComparison': '字段变更对比',
  'data.audit.field.oldValues': '旧值',
  'data.audit.field.newValues': '新值',

  // ════════ 请求失败提示（导出失败复用 data.common.exportFailed）════════
  'data.audit.loadFailed': '加载审计日志失败',
  'data.audit.integrityFailed': '完整性校验失败',
  'data.audit.verifySingleFailed': '单条校验失败',

  // ════════ 审计动作码（后端 AuditLogs.Action）════════
  // 筛选下拉与动作徽章共用同一批键；键名 = 码去掉 '.' 后的驼峰写法。
  'data.audit.action.all': '全部操作',
  'data.audit.action.login': '登录',
  'data.audit.action.logout': '登出',
  'data.audit.action.loginfailed': '登录失败',
  'data.audit.action.lockout': '账户锁定',
  'data.audit.action.create': '创建',
  'data.audit.action.update': '更新',
  'data.audit.action.delete': '删除',
  'data.audit.action.assign': '分配',
  'data.audit.action.status_change': '状态变更',
  'data.audit.action.reply': '回复',
  'data.audit.action.note': '备注',
  'data.audit.action.addnote': '添加备注',
  'data.audit.action.password_change': '密码修改',
  'data.audit.action.reset': '密码重置',
  'data.audit.action.resetfailed': '重置失败',
  'data.audit.action.createdraft': '创建草稿',
  // 日历事件（CalendarMutationService 显式写入的 calendar.* 动作）
  'data.audit.action.calendarCreate': '新建日历事件',
  'data.audit.action.calendarUpdate': '更新日历事件',
  'data.audit.action.calendarDelete': '删除日历事件',
  'data.audit.action.calendarStatusChange': '日历状态变更',
  'data.audit.action.calendarDuplicate': '复制日历事件',
  'data.audit.action.calendarQuickUpdate': '快速更新日历事件',
  'data.audit.action.calendarRestore': '恢复日历事件',
  'data.audit.action.calendarHardDelete': '彻底删除日历事件',
  'data.audit.action.calendarClearTrash': '清空日历回收站',
  'data.audit.action.calendarPurgeTrash': '彻底清除日历回收站',
  'data.audit.action.calendarReminderConfirmed': '日历提醒确认',
  // 工时（TimeEntryService 写入的 time_entry.* 动作）
  'data.audit.action.timeEntryCreate': '新建工时',
  'data.audit.action.timeEntryUpdate': '更新工时',
  'data.audit.action.timeEntryDelete': '删除工时',
  'data.audit.action.timeEntryTimerStart': '启动计时器',
  'data.audit.action.timeEntryTimerStop': '停止计时器',
  'data.audit.action.timeEntryRestore': '恢复工时',
  // 客户 / 会话业务动作（CustomerService / ConversationService 显式写入）
  'data.audit.action.restore': '恢复',
  'data.audit.action.hardDelete': '彻底删除',
  'data.audit.action.bulkSoftDelete': '批量软删除',
  'data.audit.action.bulkHardDelete': '批量彻底删除',
  'data.audit.action.bulkRestore': '批量恢复',
  // 日历批量操作（CalendarMutationService 动态拼接 calendar.batch_{op}[_result]）
  'data.audit.action.calendarBatchDelete': '批量删除日历事件',
  'data.audit.action.calendarBatchSetstatus': '批量设置日历状态',
  'data.audit.action.calendarBatchSeteventtype': '批量设置日历类型',
  'data.audit.action.calendarBatchMoveoffset': '批量偏移日历事件',
  'data.audit.action.calendarBatchRestore': '批量恢复日历事件',
  'data.audit.action.calendarBatchDeleteResult': '批量删除日历事件结果',
  'data.audit.action.calendarBatchSetstatusResult': '批量设置日历状态结果',
  'data.audit.action.calendarBatchSeteventtypeResult': '批量设置日历类型结果',
  'data.audit.action.calendarBatchMoveoffsetResult': '批量偏移日历事件结果',
  'data.audit.action.calendarBatchRestoreResult': '批量恢复日历事件结果',
  // 前缀兜底：未知 calendar./time_entry. 码只回落成"某类操作"，不泄露原始标识符
  'data.audit.action.calendarFallback': '日历操作',
  'data.audit.action.timeEntryFallback': '工时操作',

  // ════════ 实体类型码（后端 AuditLogs.EntityType，一律小写匹配）════════
  'data.audit.entityType.user': '用户',
  'data.audit.entityType.customer': '客户',
  'data.audit.entityType.conversation': '会话',
  'data.audit.entityType.conversationthread': '会话线程',
  'data.audit.entityType.mailbox': '邮箱',
  'data.audit.entityType.tag': '标签',
  'data.audit.entityType.folder': '文件夹',
  'data.audit.entityType.attachment': '附件',
  'data.audit.entityType.auth': '认证',
  'data.audit.entityType.draft': '草稿',
  'data.audit.entityType.sendlog': '发送记录',
  'data.audit.entityType.workflow': '工作流',
  'data.audit.entityType.role': '角色',
  'data.audit.entityType.team': '团队',
  'data.audit.entityType.mailtemplate': '邮件模板',
  'data.audit.entityType.cannedresponse': '快捷回复',
  'data.audit.entityType.webhook': 'Webhook',
  'data.audit.entityType.sla': 'SLA',
  'data.audit.entityType.macro': '宏',
  'data.audit.entityType.customfield': '自定义字段',
  'data.audit.entityType.notificationrule': '通知规则',
  'data.audit.entityType.notificationtemplate': '通知模板',
  'data.audit.entityType.apikey': 'API密钥',
  'data.audit.entityType.calendarevent': '日历事件',
  'data.audit.entityType.knowledgearticle': '知识库',
  'data.audit.entityType.emailsignature': '邮件签名',
  'data.audit.entityType.workflowexecutionlog': '工作流执行日志',
  'data.audit.entityType.systemoption': '系统选项',
  // —— 以下为自动拦截器（AuditSaveChangesInterceptor / EF 实体类名）与控制器路由可能记录的类型 ——
  'data.audit.entityType.apikeylog': 'API密钥日志',
  'data.audit.entityType.calendareventtemplate': '日历事件模板',
  'data.audit.entityType.calendarreminderattempt': '日历提醒尝试',
  'data.audit.entityType.chatmessage': '聊天消息',
  'data.audit.entityType.chatsession': '聊天会话',
  'data.audit.entityType.conversationfolder': '会话文件夹',
  'data.audit.entityType.conversationreadstate': '会话已读状态',
  'data.audit.entityType.conversationtag': '会话标签',
  'data.audit.entityType.customfieldvalue': '自定义字段值',
  'data.audit.entityType.customreport': '自定义报表',
  'data.audit.entityType.customreportschedule': '自定义报表计划',
  'data.audit.entityType.distributedlock': '分布式锁',
  'data.audit.entityType.email': '邮件',
  'data.audit.entityType.follower': '关注者',
  'data.audit.entityType.immessagelog': 'IM消息日志',
  'data.audit.entityType.knowledgearticleattachment': '知识库附件',
  'data.audit.entityType.knowledgecategory': '知识库分类',
  'data.audit.entityType.macroexecutionlog': '宏执行日志',
  'data.audit.entityType.mailboxuser': '邮箱用户',
  'data.audit.entityType.moduleentity': '模块实体',
  'data.audit.entityType.outlookeventmapping': 'Outlook事件映射',
  'data.audit.entityType.outlooksynclog': 'Outlook同步日志',
  'data.audit.entityType.outlooksyncqueueitem': 'Outlook同步队列项',
  'data.audit.entityType.outlooksyncsetting': 'Outlook同步设置',
  'data.audit.entityType.passwordresettoken': '密码重置令牌',
  'data.audit.entityType.refreshtoken': '刷新令牌',
  'data.audit.entityType.satisfactionrating': '满意度评分',
  'data.audit.entityType.slapolicy': 'SLA策略',
  'data.audit.entityType.slaviolation': 'SLA违规',
  'data.audit.entityType.star': '星标',
  'data.audit.entityType.teammember': '团队成员',
  'data.audit.entityType.timeentry': '工时',
  'data.audit.entityType.translation': '翻译',
  'data.audit.entityType.usernotification': '用户通知',
  'data.audit.entityType.userrole': '用户角色',
  'data.audit.entityType.webhooklog': 'Webhook日志',
  // —— 控制器名推断（AuditActionFilter.GetEntityTypeFromController：去 Controller 后缀 + 去尾 s）——
  'data.audit.entityType.auditlog': '审计日志',
  'data.audit.entityType.calendar': '日历',
  'data.audit.entityType.calendarfeed': '日历订阅',
  'data.audit.entityType.chat': '在线聊天',
  'data.audit.entityType.dashboard': '仪表盘',
  'data.audit.entityType.dingding': '钉钉',
  'data.audit.entityType.emailtest': '邮件测试',
  'data.audit.entityType.feishu': '飞书',
  'data.audit.entityType.importexport': '导入导出',
  'data.audit.entityType.knowledge': '知识库',
  'data.audit.entityType.localization': '翻译管理',
  'data.audit.entityType.mail': '邮件',
  'data.audit.entityType.mailboxe': '邮箱',
  'data.audit.entityType.module': '模块',
  'data.audit.entityType.notification': '通知',
  'data.audit.entityType.oauth': 'OAuth',
  'data.audit.entityType.outlooksync': 'Outlook同步',
  'data.audit.entityType.profile': '个人资料',
  'data.audit.entityType.realtime': '实时推送',
  'data.audit.entityType.report': '报表',
  'data.audit.entityType.search': '搜索',
  'data.audit.entityType.setup': '系统初始化',
  'data.audit.entityType.system': '系统',
  'data.audit.entityType.timetracking': '工时统计',
  'data.audit.entityType.tracking': '邮件追踪',
  'data.audit.entityType.userprofile': '用户资料',
  'data.audit.entityType.wechat': '微信',
  'data.audit.entityType.whatsapp': 'WhatsApp',
  // —— EF 实体类名（Domain/Entities；拦截器用 `entry.Entity.GetType().Name` 写入，故类名即码值）——
  'data.audit.entityType.customerphone': '客户电话',
  'data.audit.entityType.customersocialprofile': '客户社交资料',
  'data.audit.entityType.macroaction': '宏动作',
  'data.audit.entityType.workflowaction': '工作流动作',
  'data.audit.entityType.workflowcondition': '工作流条件',
  'data.audit.entityType.workflowconditiongroup': '工作流条件组',
  'data.audit.entityType.workflowevaluationcontext': '工作流求值上下文',

  // ══ data.sendLogs.* —— 发送日志页 ══
  'data.sendLogs.title': '邮件发送日志',
  'data.sendLogs.description': '追踪邮件发送状态、统计分析和失败排查',
  'data.sendLogs.breadcrumb': '系统管理',
  'data.sendLogs.loadOverviewFailed': '加载总览失败',
  'data.sendLogs.loadListFailed': '加载列表失败',

  // ====== 发送状态码（后端 SendLogService.GetStatusLabel，SendLogService.cs:169-181）======
  'data.sendLogs.status.received': '已接收',
  'data.sendLogs.status.error': '发送错误',
  'data.sendLogs.status.delivered': '投递成功',
  'data.sendLogs.status.failed': '投递失败',
  'data.sendLogs.status.opened': '已打开',
  'data.sendLogs.status.clicked': '已点击',
  'data.sendLogs.status.unsubscribed': '已退订',
  'data.sendLogs.status.complained': '已投诉',
  'data.sendLogs.status.interrupted': '发送中断',
  'data.sendLogs.status.unknown': '未知',

  // ====== 邮件类型码（后端 SendLogMailType.GetLabel，SendLogRepository.cs:63-75）======
  'data.sendLogs.mailType.customerReply': '客户回复',
  'data.sendLogs.mailType.userNotification': '用户通知',
  'data.sendLogs.mailType.autoReply': '自动回复',
  'data.sendLogs.mailType.invite': '邀请',
  'data.sendLogs.mailType.passwordChanged': '密码修改',
  'data.sendLogs.mailType.wrongUserEmail': '错误邮件',
  'data.sendLogs.mailType.test': '测试',
  'data.sendLogs.mailType.alert': '警报',
  'data.sendLogs.mailType.calendarReminder': '日程提醒',
  'data.sendLogs.mailType.unknown': '未知',

  // ====== 趋势图 ======
  'data.sendLogs.trend.title': '发送趋势',
  'data.sendLogs.trend.subtitle': '每日发送总数、成功和失败变化',
  'data.sendLogs.trend.total': '总数',
  'data.sendLogs.trend.success': '成功',
  'data.sendLogs.trend.failed': '失败',

  // ====== 邮箱发送分布 ======
  'data.sendLogs.mailboxStats.title': '邮箱发送分布',
  'data.sendLogs.mailboxStats.subtitle': '各邮箱负载与失败情况',
  'data.sendLogs.mailboxBars.failed': '失败 {count}',

  // ====== 最近失败记录 ======
  'data.sendLogs.recentFailures.title': '最近失败记录',
  'data.sendLogs.recentFailures.subtitle': '需要关注的发送失败，建议排查',
  'data.sendLogs.noSubject': '无主题',
  'data.sendLogs.rowAction.details': '详情',
  'data.sendLogs.rowAction.conversation': '会话',
  'data.sendLogs.viewConversation': '查看会话',

  // ====== 总览卡 ======
  'data.sendLogs.kpi.total': '发送总数',
  'data.sendLogs.kpi.totalSub': '统计周期内',
  'data.sendLogs.kpi.success': '投递成功',
  'data.sendLogs.kpi.successSub': '占比 {rate}%',
  'data.sendLogs.kpi.failed': '发送失败',
  'data.sendLogs.kpi.failedSub': '失败率 {rate}%',
  'data.sendLogs.kpi.pending': '待发送',
  'data.sendLogs.kpi.pendingSub': '已接收队列中',

  // ====== 筛选条 ======
  'data.sendLogs.filter.mailbox': '邮箱',
  'data.sendLogs.filter.allStatuses': '全部状态',
  'data.sendLogs.filter.keyword': '关键词',
  'data.sendLogs.filter.keywordPlaceholder': '搜索邮箱或消息 ID...',

  // ====== 列表 ======
  'data.sendLogs.listTitle': '发送日志',
  'data.sendLogs.listEmptyTitle': '暂无发送日志',
  'data.sendLogs.listEmptySubtitle': '修改筛选条件或点击刷新按钮加载最新数据',

  // ====== 表头 / 详情字段名 ======
  'data.sendLogs.field.status': '状态',
  'data.sendLogs.field.type': '类型',
  'data.sendLogs.field.email': '收件邮箱',
  'data.sendLogs.field.mailType': '邮件类型',
  'data.sendLogs.field.conversationSubject': '会话主题',
  'data.sendLogs.field.agent': '操作客服',
  'data.sendLogs.field.sentAt': '发送时间',
  'data.sendLogs.field.actions': '操作',

  // ====== 分页 ======
  'data.sendLogs.pagination.pagePrefix': '第 ',
  'data.sendLogs.pagination.pageTotal': ' / {total} 页',
  'data.sendLogs.pagination.pageSuffix': ' 页',
  'data.sendLogs.pagination.totalPrefix': '共 ',
  'data.sendLogs.pagination.totalSuffix': ' 条',
  'data.sendLogs.pagination.first': '首页',
  'data.sendLogs.pagination.last': '末页',

  // ====== CSV 导出（每列一个键，按逗号 join 还原原表头；消息ID 的紧凑写法逐字保留）======
  'data.sendLogs.csv.id': 'ID',
  'data.sendLogs.csv.messageId': '消息ID',
  'data.sendLogs.csv.email': '收件邮箱',
  'data.sendLogs.csv.mailType': '邮件类型',
  'data.sendLogs.csv.status': '状态',
  'data.sendLogs.csv.agent': '客服',
  'data.sendLogs.csv.subject': '会话主题',
  'data.sendLogs.csv.sentAt': '发送时间',

  // ====== 详情弹窗 ======
  'data.sendLogs.detailTitlePrefix': '发送日志详情 #',
  'data.sendLogs.exportJson': '导出 JSON',
  'data.sendLogs.detail.status': '发送状态',
  'data.sendLogs.detail.customer': '客户：{email}',
  'data.sendLogs.detail.context': '会话与客服上下文',
  'data.sendLogs.detail.noSubject': '(无主题)',
  'data.sendLogs.detail.userFallbackPrefix': '用户#',
  'data.sendLogs.detail.automated': '自动化发送',
  'data.sendLogs.detail.conversationId': '会话 ID',
  'data.sendLogs.detail.threadId': '线程 ID',
  'data.sendLogs.detail.messageId': '消息 ID (Message-ID)',

  // ══ data.reports.* —— 报表统计页 ══
  // ── 页头 ──
  'data.reports.title': '报表统计',
  // 前缀：原文其后紧跟带样式的日期 <span>，故 en 侧自带尾空格
  'data.reports.header.dataRangePrefix': '数据范围：',
  'data.reports.loadReportFailed': '加载报表失败',

  // ── 状态分布饼图 ──
  'data.reports.statusPie.total': '会话总数',

  // ── 图表标题与副标题 ──
  'data.reports.trend.title': '会话趋势',
  'data.reports.trend.subtitle': '新建、关闭和回复数随时间变化',
  'data.reports.statusDistribution.title': '会话状态分布',
  'data.reports.mailboxLoad.title': '邮箱负载分布',

  // ── 客服绩效 ──
  'data.reports.performance.title': '客服绩效',
  'data.reports.performance.colAgent': '客服',
  'data.reports.performance.colAssigned': '分配',
  'data.reports.performance.colReplies': '回复',
  'data.reports.performance.colClosed': '关闭',
  'data.reports.performance.colResponseHours': '响应(h)',
  // 前缀：原文其后紧跟带样式的数值 <span>，故 en 侧自带尾空格
  'data.reports.performance.responsePrefix': '响应 ',

  // ── 客户域名 ──
  'data.reports.domains.title': '客户域名 Top 10',
  'data.reports.domains.customers': '{count} 客户',

  // ── 平均解决时长 ──
  'data.reports.resolution.title': '平均解决时长',
  'data.reports.resolution.subtitle': '已关闭会话从创建到关闭的平均时长',
  'data.reports.resolution.unitHours': '小时',
  'data.reports.resolution.unitDays': '天',

  // ── 核心指标卡片 ──
  'data.reports.kpi.avgFirstResponse': '平均首次响应',
  'data.reports.kpi.avgFirstResponseHint': '客户消息 → 客服回复',
  'data.reports.kpi.slaAchievement': 'SLA 达成率',
  'data.reports.kpi.slaTarget': '目标：24h 内响应',

  // ── CSV 导出：分节标题行 ──
  'data.reports.csv.summaryHeading': '# 报表概要',
  'data.reports.csv.trendHeading': '# 趋势数据',
  'data.reports.csv.performanceHeading': '# 客服绩效',
  'data.reports.csv.mailboxLoadHeading': '# 邮箱负载',
  'data.reports.csv.customerDomainsHeading': '# 客户域名 Top 10',

  // ── CSV 导出：列名（每列一键，整行由 [t(...), t(...)].join(',') 组装） ──
  'data.reports.csv.dateRangeCol': '时间范围',
  'data.reports.csv.mailboxFilterCol': '邮箱筛选',
  'data.reports.csv.metricCol': '指标',
  'data.reports.csv.valueCol': '数值',
  'data.reports.csv.avgFirstResponseHours': '平均首次响应(小时)',
  'data.reports.csv.avgResolutionHours': '平均解决时长(小时)',
  'data.reports.csv.slaRate': 'SLA 达成率(%)',
  'data.reports.csv.dateCol': '日期',
  'data.reports.csv.newCol': '新建',
  'data.reports.csv.closedCol': '关闭',
  'data.reports.csv.replyCol': '回复',
  'data.reports.csv.nameCol': '姓名',
  'data.reports.csv.emailCol': '邮箱',
  'data.reports.csv.currentAssignedCol': '当前分配',
  'data.reports.csv.repliesCol': '回复数',
  'data.reports.csv.closedCountCol': '关闭数',
  'data.reports.csv.mailboxCol': '邮箱',
  'data.reports.csv.percentageCol': '占比(%)',
  'data.reports.csv.domainCol': '域名',
  'data.reports.csv.conversationsCol': '会话数',
  'data.reports.csv.uniqueCustomersCol': '独立客户数',

  // ══ data.customReports.* —— 自定义报表列表页 ══
  'data.customReports.title': '自定义报表',
  'data.customReports.description': '创建并管理团队的个性化数据分析报表',

  'data.customReports.searchPlaceholder': '搜索报表名称...',
  // Tab 栏右侧的"共 N 条"（数值无包裹元素，走插值）
  'data.customReports.totalCount': '共 {count} 条',

  // 分页：`共 <span>{totalItems}</span> 条，每页 {pageSize} 条，第 {currentPage} / {totalPages} 页`
  // 数值 totalItems 被带样式的 <span> 包裹 → 拆前后缀键，让值保留自己的元素
  'data.customReports.pagination.totalPrefix': '共 ',
  'data.customReports.pagination.summary': ' 条，每页 {pageSize} 条，第 {currentPage} / {totalPages} 页',

  // 后端 `CustomReportDto.category` 的 4 个稳定 code（performance/efficiency/customer/workload）
  'data.customReports.category.all': '全部分类',
  'data.customReports.category.performance': '绩效',
  'data.customReports.category.efficiency': '效率',
  'data.customReports.category.customer': '客户',
  'data.customReports.category.workload': '负载',

  // 后端 `CustomReportDto.visibility` 的 3 个稳定 code（private/team/public）
  'data.customReports.visibility.all': '全部可见性',
  'data.customReports.visibility.private': '私有',
  'data.customReports.visibility.team': '团队',
  'data.customReports.visibility.public': '公开',

  'data.customReports.tab.pinned': '我的置顶',
  'data.customReports.tab.trash': '垃圾箱',

  'data.customReports.action.clearTrash': '清空垃圾箱',
  'data.customReports.action.clear': '清空',
  'data.customReports.action.create': '新建',
  'data.customReports.action.createReport': '新建报表',
  'data.customReports.action.deleteReport': '删除报表',
  'data.customReports.action.restoreReport': '恢复报表',
  'data.customReports.action.hardDeleteReport': '永久删除报表',
  'data.customReports.action.restore': '恢复',
  'data.customReports.action.hardDelete': '永久删除',
  'data.customReports.action.clearAll': '清空全部',
  'data.customReports.action.view': '查看',
  'data.customReports.action.viewReport': '查看报表',
  'data.customReports.action.editConfig': '编辑配置',
  'data.customReports.action.schedule': '设置调度',
  'data.customReports.action.exportAs': '导出为',
  'data.customReports.action.exportAsCsv': 'CSV 格式',
  'data.customReports.action.exportAsExcel': 'Excel 格式',
  'data.customReports.action.exportAsPdf': 'PDF 格式',
  'data.customReports.action.pin': '置顶',
  'data.customReports.action.unpin': '取消置顶',

  'data.customReports.deletedBadge': '已删除',
  'data.customReports.deletedAt': '删除时间',

  'data.customReports.empty.trashTitle': '垃圾箱为空',
  'data.customReports.empty.trashDescription': '删除的报表会暂时保存在这里',
  'data.customReports.empty.pinnedTitle': '暂无置顶报表',
  'data.customReports.empty.pinnedDescription': '在全部报表中点击置顶图标即可添加',
  'data.customReports.empty.allTitle': '暂无自定义报表',
  // 引号是源码里的 ASCII 双引号（U+0022，非弯引号），逐字保留
  'data.customReports.empty.allDescription': '点击右上角"新建报表"开始创建',

  'data.customReports.toast.loadPinnedFailed': '加载置顶失败',
  'data.customReports.toast.pinned': '已置顶',
  'data.customReports.toast.unpinned': '已取消置顶',
  'data.customReports.toast.operationFailed': '操作失败',
  'data.customReports.toast.deleted': '删除成功，已移至垃圾箱',
  'data.customReports.toast.deleteFailed': '删除失败',
  'data.customReports.toast.restored': '已恢复报表',
  'data.customReports.toast.restoreFailed': '恢复失败',
  'data.customReports.toast.hardDeleted': '报表已永久删除',
  'data.customReports.toast.hardDeleteFailed': '永久删除失败',
  // 圆括号是全角（U+FF08 / U+FF09），逐字保留
  'data.customReports.toast.trashCleared': '已清空垃圾箱（清除 {count} 项）',
  'data.customReports.toast.clearFailed': '清空失败',

  'data.customReports.dialog.deleteMessage': '确定要删除报表"{name}"吗？删除后报表将移入垃圾箱，你可以在垃圾箱中恢复。',
  'data.customReports.dialog.restoreMessage': '确定要恢复报表"{name}"吗？',
  'data.customReports.dialog.hardDeleteMessage': '警告：你正在永久删除报表"{name}"！此操作不可撤销，数据将无法恢复。',
  'data.customReports.dialog.clearTrashMessage': '确定要清空垃圾箱吗？当前共 {count} 条记录将被永久删除，此操作不可撤销。',
  'data.customReports.dialog.hardDeleteNote': '永久删除后，报表及其配置、调度信息都将无法恢复。',
  'data.customReports.dialog.clearTrashNote': '清空后，所有垃圾箱中的报表将被永久删除，无法恢复。',

  // ══ data.customReports.view.* —— 自定义报表查看页 ══
  'data.customReports.view.title': '自定义报表',

  // ── 页头 / 面包屑 / 元信息 ──
  'data.customReports.view.breadcrumbView': '查看',
  // 后端只下发稳定 code，标签一律由前端按码查字典（见 CustomReportView 的 VISIBILITY_INFO / CATEGORY_LABEL_KEYS）
  'data.customReports.view.visibility.private': '私有',
  'data.customReports.view.visibility.team': '团队',
  'data.customReports.view.visibility.public': '公开',
  'data.customReports.view.category.performance': '绩效',
  'data.customReports.view.category.efficiency': '效率',
  'data.customReports.view.category.customer': '客户',
  'data.customReports.view.category.workload': '负载',
  // 冒号后的空格来自 JSX 文本节点（原文就是 `团队: {name}`），故值内不带空格
  'data.customReports.view.teamLabel': '团队:',
  'data.customReports.view.updatedAtLabel': '更新于:',
  // 全角冒号后**没有**空格（原文 `分组：{值}`），英文侧在值内自带空格
  'data.customReports.view.groupByLabel': '分组：',
  'data.customReports.view.groupBy.week': '按周',
  'data.customReports.view.groupBy.month': '按月',
  'data.customReports.view.reportNotFound': '报表不存在',

  // ── 工具条 ──
  'data.customReports.view.backToList': '返回列表',
  'data.customReports.view.refreshData': '刷新数据',
  'data.customReports.view.exportFormat.csv': 'CSV 格式',
  'data.customReports.view.exportFormat.excel': 'Excel 格式',
  'data.customReports.view.exportFormat.pdf': 'PDF 格式',
  'data.customReports.view.pinTooltip': '取消置顶',
  'data.customReports.view.pinToDashboard': '置顶到仪表盘',
  'data.customReports.view.pinned': '已置顶',
  'data.customReports.view.pin': '置顶',
  'data.customReports.view.schedule': '调度',

  // ── 加载 / 未执行 / 不存在 态 ──
  'data.customReports.view.loadingReport': '加载报表信息...',
  'data.customReports.view.executingReport': '正在执行报表计算...',
  'data.customReports.view.reportUnavailable': '该报表可能已被删除或您没有访问权限',
  'data.customReports.view.notExecuted': '尚未执行',
  'data.customReports.view.notExecutedHint': '点击顶部"刷新数据"开始执行报表',
  'data.customReports.view.executeNow': '立即执行',

  // ── toast ──
  'data.customReports.view.toast.loadReportFailed': '加载报表失败',
  'data.customReports.view.toast.reportRefreshed': '报表已刷新',
  'data.customReports.view.toast.executeReportFailed': '执行报表失败',
  'data.customReports.view.toast.pinned': '已置顶',
  'data.customReports.view.toast.unpinned': '已取消置顶',
  'data.customReports.view.toast.operationFailed': '操作失败',

  // ── 图例 / 指标卡 ──
  'data.customReports.view.kpi.averageFirstResponse': '平均首次响应',
  'data.customReports.view.kpi.averageFirstResponseHint': '客户消息 → 客服回复',
  'data.customReports.view.kpi.slaAchievement': 'SLA 达成率',
  'data.customReports.view.kpi.slaTarget': '目标：24h 内响应',

  // ── 图表标题 ──
  'data.customReports.view.chart.trend': '会话趋势',
  'data.customReports.view.chart.trendSubtitle': '新建、关闭和回复数随时间变化',
  'data.customReports.view.chart.statusDistribution': '会话状态分布',
  'data.customReports.view.chart.mailboxLoad': '邮箱负载分布',
  'data.customReports.view.chart.userPerformance': '客服绩效',
  'data.customReports.view.chart.customerDomains': '客户域名 Top',
  // 饼图圆心标签（分布总数）
  'data.customReports.view.chart.statusTotal': '会话总数',
  // 客服绩效表头
  'data.customReports.view.table.agent': '客服',
  'data.customReports.view.table.assigned': '分配',
  'data.customReports.view.table.replied': '回复',
  'data.customReports.view.table.closed': '关闭',
  'data.customReports.view.table.responseHours': '响应(h)',
  'data.customReports.view.customerCount': '{count} 客户',

  // ── 平均解决时长 ──
  'data.customReports.view.avgResolution.title': '平均解决时长',
  'data.customReports.view.avgResolution.subtitle': '已关闭会话从创建到关闭的平均时长',
  'data.customReports.view.avgResolution.unitHours': '小时',
  'data.customReports.view.avgResolution.daysApprox': '≈ {days} 天',

  // ══ data.customReports.schedule.* —— 自定义报表调度弹窗 ══
  // 页头
  'data.customReports.schedule.title': '自定义报表调度设置',
  'data.customReports.schedule.description': '为报表设置定期邮件发送，支持 PDF / Excel / CSV 附件',
  'data.customReports.schedule.addSchedule': '新增调度',

  // 表单
  'data.customReports.schedule.defaultSubject': '自定义报表定时发送',
  'data.customReports.schedule.newSchedule': '新建调度',
  'data.customReports.schedule.editSchedule': '编辑调度',
  'data.customReports.schedule.form.recurrenceLabel': '频率 *',
  'data.customReports.schedule.form.weekDayLabel': '周几 *',
  'data.customReports.schedule.form.sendTimeLabel': '发送时间 *',
  'data.customReports.schedule.form.attachmentFormatLabel': '附件格式',
  'data.customReports.schedule.form.pdf': 'PDF 文档 (.pdf)',
  'data.customReports.schedule.form.xlsx': 'Excel 表格 (.xlsx)',
  'data.customReports.schedule.form.csv': 'CSV 数据 (.csv)',
  'data.customReports.schedule.form.recipientsLabel': '收件人 *（逗号、分号或回车分隔）',
  'data.customReports.schedule.form.recipientsPlaceholder': '例如: a@example.com, b@example.com',
  'data.customReports.schedule.form.subjectLabel': '邮件主题 *',
  'data.customReports.schedule.form.enableImmediately': '创建后立即启用调度',
  'data.customReports.schedule.form.saving': '保存中...',

  // 频率 / 周几（值域 = 后端稳定 code，不随界面语言变化）
  'data.customReports.schedule.recurrence.daily': '每日',
  'data.customReports.schedule.recurrence.weekly': '每周',
  'data.customReports.schedule.recurrence.monthly': '每月',
  'data.customReports.schedule.weekday.sun': '周日',
  'data.customReports.schedule.weekday.mon': '周一',
  'data.customReports.schedule.weekday.tue': '周二',
  'data.customReports.schedule.weekday.wed': '周三',
  'data.customReports.schedule.weekday.thu': '周四',
  'data.customReports.schedule.weekday.fri': '周五',
  'data.customReports.schedule.weekday.sat': '周六',
  /** 周几码越界（或缺失）时的前缀：后接原始数字，如 `周7` */
  'data.customReports.schedule.weekday.unknownPrefix': '周',
  /** 每周摘要前缀：后接周几名称，如 `每周 · 周一` */
  'data.customReports.schedule.weeklySummaryPrefix': '每周 · ',

  // 列表
  'data.customReports.schedule.status.running': '运行中',
  'data.customReports.schedule.status.disabled': '已停用',
  'data.customReports.schedule.status.enabled': '已启用',
  'data.customReports.schedule.loadingList': '加载调度列表中...',
  'data.customReports.schedule.emptyTitle': '暂无调度',
  'data.customReports.schedule.emptyHint': '点击右上角"新增调度"创建定时发送任务',
  /** 主题前缀：后接收件主题文本，如 `主题：周报` */
  'data.customReports.schedule.subjectPrefix': '主题：',
  /** 上次运行时间前缀：后接本地化日期时间 */
  'data.customReports.schedule.lastRunPrefix': '上次：',
  /** 下次运行时间前缀：后接本地化日期时间 */
  'data.customReports.schedule.nextRunPrefix': '下次：',

  // 行操作
  'data.customReports.schedule.action.disable': '停用',
  'data.customReports.schedule.action.enable': '启用',

  // 删除确认
  'data.customReports.schedule.deleteDialog.title': '删除调度',
  'data.customReports.schedule.deleteDialog.message': '确定要删除该调度吗？此操作无法撤销。',

  // 表单校验（内联错误 / toast 前缀）
  'data.customReports.schedule.validation.weekDayRequired': '请选择周几发送',
  'data.customReports.schedule.validation.sendTimeRequired': '请选择发送时间',
  'data.customReports.schedule.validation.recipientsRequired': '请至少输入一个收件人邮箱',
  /** 邮箱校验失败前缀：后接不合法的邮箱，如 `邮箱格式不正确: a@b` */
  'data.customReports.schedule.validation.invalidEmailPrefix': '邮箱格式不正确: ',
  'data.customReports.schedule.validation.subjectRequired': '请输入邮件主题',

  // 操作结果 toast
  'data.customReports.schedule.toast.updateSucceeded': '调度更新成功',
  'data.customReports.schedule.toast.createSucceeded': '调度创建成功',
  'data.customReports.schedule.toast.saveFailed': '保存失败',
  'data.customReports.schedule.toast.operationFailed': '操作失败',
  'data.customReports.schedule.toast.deleteSucceeded': '删除成功',
  'data.customReports.schedule.toast.deleteFailed': '删除失败',
  'data.customReports.schedule.toast.loadListFailed': '加载调度列表失败',

  // ══ data.customReportEditor.* —— 自定义报表编辑器 ══
  // ── 页头（种子键，别改名）──
  'data.customReportEditor.createTitle': '新建自定义报表',
  'data.customReportEditor.editTitle': '编辑自定义报表',

  // ── 面包屑 / 页头 ──
  'data.customReportEditor.listLink': '自定义报表',
  'data.customReportEditor.action.new': '新建',
  'data.customReportEditor.subtitle': '配置数据源、筛选条件和可视化模块，保存后可分享到团队或仪表盘',

  // ── 右上工具条 ──
  'data.customReportEditor.backToList': '返回列表',
  'data.customReportEditor.action.preview': '预览',
  'data.customReportEditor.action.executing': '执行中...',
  'data.customReportEditor.action.saving': '保存中...',

  // ── 表单分区卡片 ──
  'data.customReportEditor.card.basicInfo': '基础信息',
  'data.customReportEditor.card.basicInfoDesc': '报表基本元信息，会显示在列表与查看页',
  'data.customReportEditor.card.dataRange': '数据范围',
  'data.customReportEditor.card.dataRangeDesc': '报表统计的时间范围、邮箱和分组方式',
  'data.customReportEditor.card.sections': '可视化模块',
  'data.customReportEditor.card.sectionsDesc': '选择要展示在报表中的模块',
  'data.customReportEditor.card.selectedBadge': '已选 {selected} / {total}',

  // ── 字段标签 ──
  'data.customReportEditor.field.name': '报表名称',
  'data.customReportEditor.field.description': '描述（可选）',
  'data.customReportEditor.field.category': '分类',
  'data.customReportEditor.field.visibility': '可见性',
  'data.customReportEditor.field.team': '所属团队',
  'data.customReportEditor.field.dateRange': '日期范围 *',
  'data.customReportEditor.field.mailboxOptional': '邮箱（可选，全部留空）',
  'data.customReportEditor.field.groupBy': '分组（趋势图）',

  // ── 输入占位符 ──
  'data.customReportEditor.placeholder.name': '例如：Q4 客服绩效月报',
  'data.customReportEditor.placeholder.description': '简要说明该报表的用途和受众',

  // ── 下拉选项 ──
  'data.customReportEditor.option.selectTeam': '请选择团队',
  'data.customReportEditor.option.groupNone': '不分组（按日）',
  'data.customReportEditor.option.groupWeek': '按周汇总',
  'data.customReportEditor.option.groupMonth': '按月汇总',

  // ── 分类选项 ──
  'data.customReportEditor.category.performance': '绩效',
  'data.customReportEditor.category.performanceDesc': '客服绩效指标',
  'data.customReportEditor.category.efficiency': '效率',
  'data.customReportEditor.category.efficiencyDesc': '团队效率与产能',
  'data.customReportEditor.category.customer': '客户',
  'data.customReportEditor.category.customerDesc': '客户维度洞察',
  'data.customReportEditor.category.workload': '负载',
  'data.customReportEditor.category.workloadDesc': '会话量与负载分配',

  // ── 可见性选项 ──
  'data.customReportEditor.visibility.private': '私有',
  'data.customReportEditor.visibility.privateDesc': '仅自己可见',
  'data.customReportEditor.visibility.team': '团队',
  'data.customReportEditor.visibility.teamDesc': '所选团队成员可见',
  'data.customReportEditor.visibility.public': '公开',
  'data.customReportEditor.visibility.publicDesc': '全公司可见',

  // ── 可视化模块选项（同时用作预览面板里同名图表的小标题）──
  'data.customReportEditor.module.summary': '核心指标 KPI',
  'data.customReportEditor.module.summaryDesc': '关键业务概览卡片',
  'data.customReportEditor.module.trend': '会话趋势',
  'data.customReportEditor.module.trendDesc': '新建/关闭/回复折线图',
  'data.customReportEditor.module.statusDistribution': '状态分布',
  'data.customReportEditor.module.statusDistributionDesc': '按状态饼图/柱图',
  'data.customReportEditor.module.userPerformance': '客服绩效',
  'data.customReportEditor.module.userPerformanceDesc': '客服工作绩效表',
  'data.customReportEditor.module.mailboxLoad': '邮箱负载',
  'data.customReportEditor.module.mailboxLoadDesc': '各邮箱会话量',
  'data.customReportEditor.module.customerDomains': '客户域名 TOP',
  'data.customReportEditor.module.customerDomainsDesc': '客户来源域名分布',

  // ── 预览面板 ──
  'data.customReportEditor.preview.title': '实时预览',
  'data.customReportEditor.preview.refresh': '刷新预览',
  'data.customReportEditor.preview.generating': '正在生成报表...',
  'data.customReportEditor.preview.emptyHint': '配置数据源与图表后，点击右上角「预览」或右侧「刷新预览」',
  'data.customReportEditor.preview.emptyHintSub': '将基于当前配置实时执行报表计算，不会保存',
  'data.customReportEditor.preview.emptySections': '报表返回空 Sections，检查选择的图表项',
  'data.customReportEditor.preview.trendPoints': '会话趋势 ({count} 点)',
  'data.customReportEditor.preview.userPerformanceTop': '客服绩效 Top 5',
  'data.customReportEditor.preview.replies': '回复 {count}',
  'data.customReportEditor.preview.customerDomainsTop': '客户域名 Top',
  // 预览请求里报表名为空时的兜底名（与工具条按钮同字，但语义是"报表名"而非"动作"）
  'data.customReportEditor.preview.fallbackName': '预览',

  // ── 加载态 ──
  'data.customReportEditor.loading.report': '加载报表信息中...',

  // ── 表单校验 ──
  'data.customReportEditor.validation.nameRequired': '请输入报表名称',
  'data.customReportEditor.validation.nameMaxLength': '名称最多 {max} 字符',
  'data.customReportEditor.validation.categoryRequired': '请选择分类',
  'data.customReportEditor.validation.visibilityRequired': '请选择可见性',
  'data.customReportEditor.validation.teamRequired': '请选择所属团队',
  'data.customReportEditor.validation.startRequired': '请选择开始日期',
  'data.customReportEditor.validation.endRequired': '请选择结束日期',
  'data.customReportEditor.validation.rangeInvalid': '开始日期不能晚于结束日期',
  'data.customReportEditor.validation.sectionsRequired': '请至少选择一个图表模块',

  // ── toast / 错误 ──
  'data.customReportEditor.toast.created': '自定义报表创建成功',
  'data.customReportEditor.toast.updated': '已保存修改',
  'data.customReportEditor.error.saveFailed': '保存失败',
  'data.customReportEditor.error.previewFailed': '预览失败',
  'data.customReportEditor.error.missingEditId': '缺少编辑 ID',
  'data.customReportEditor.error.fixFormErrors': '请修正表单中的错误',
  'data.customReportEditor.error.fixFormErrorsFirst': '请先修正表单错误',

  // ── 右侧提示块 ──
  'data.customReportEditor.tip.title': '提示',
  'data.customReportEditor.tip.previewNoSave': '点击"预览"基于当前配置实时执行报表计算，不会保存',
  'data.customReportEditor.tip.savePersists': '点击"保存"持久化报表配置，保存后可跳转查看页进行调度、导出和置顶',
  'data.customReportEditor.tip.dateRangeAdvice': '建议日期范围不要超过 90 天以获得最佳渲染性能',

  // ══ data.importExport.* —— 数据导入导出页 ══
  'data.importExport.title': '数据导入导出',
  'data.importExport.description': '管理系统数据的导入和导出，支持 JSON 格式的数据备份与恢复',
  'data.importExport.loadFailed': '加载数据失败',
  'data.importExport.selectTypeRequired': '请选择要导出的数据类型',
  'data.importExport.fileTooLarge': '文件大小不能超过 5MB',
  'data.importExport.dataRequired': '请提供要导入的数据',
  'data.importExport.previewFailed': '预览失败',
  'data.importExport.importFailed': '导入失败',
  'data.importExport.exportSectionTitle': '数据导出',
  'data.importExport.exportSectionDescription': '选择要导出的数据类型，系统将生成 JSON 文件',
  'data.importExport.selectAll': '全选',
  // `已选择 <数字> / <总数> 项` 拆成前后缀三键：数字本身带 `font-semibold text-primary-600`
  // 样式元素，整体插值会让它丢掉那个 <span>。空格（含 ` / ` 两侧）逐字保留在值里。
  'data.importExport.selectedPrefix': '已选择 ',
  'data.importExport.selectedSeparator': ' / ',
  'data.importExport.selectedSuffix': ' 项',
  'data.importExport.exportAction': '导出数据',
  'data.importExport.importSectionTitle': '数据导入',
  'data.importExport.importSectionDescription': '通过文件或直接输入 JSON 数据导入系统',
  'data.importExport.tabFile': '上传文件',
  'data.importExport.tabText': '粘贴数据',
  'data.importExport.changeFile': '更换文件',
  'data.importExport.dropHint': '点击或拖拽文件到此处上传',
  'data.importExport.formatHint': '支持 JSON 文件格式',
  'data.importExport.chooseFile': '选择文件',
  // 文本框 placeholder 的两段可译文案；两段之间的换行与 JSON 骨架不是文案，留在 tsx 里。
  'data.importExport.pasteTextHint': '粘贴 JSON 数据...',
  'data.importExport.pasteExampleHint': '示例格式：',
  'data.importExport.confirmImport': '确认导入',
  'data.importExport.importing': '导入中...',
  'data.importExport.previewAction': '预览数据',
  'data.importExport.previewing': '预览中...',
  'data.importExport.previewTitle': '导入预览',
  // `共 <数字> 条记录` / `<数字> 条`：数字与外层包裹元素都保留，只把文字拆成前后缀。
  'data.importExport.previewCountPrefix': '共 ',
  'data.importExport.previewCountSuffix': ' 条记录',
  'data.importExport.previewCountBadgeSuffix': ' 条',
  'data.importExport.resultTitle': '导入结果',
  'data.importExport.resultTotal': '总数',
  'data.importExport.resultSucceeded': '成功',
  'data.importExport.resultFailed': '失败',
  'data.importExport.errorDetails': '错误详情：',
  // `还有 <数字> 个错误...`：数字前后的空格逐字保留在两侧键里。
  'data.importExport.moreErrorsPrefix': '还有 ',
  'data.importExport.moreErrorsSuffix': ' 个错误...',
  // 后端 `EntityTypeResponse.Name` / `.Description` 是中文标签（英文界面下会直出中文），
  // 改由前端按稳定 `type` 码查字典。码表见 ImportExport.tsx 的 ENTITY_NAME_KEYS。
  'data.importExport.entity.customers.name': '客户',
  'data.importExport.entity.customers.description': '客户联系人信息',
  'data.importExport.entity.tags.name': '标签',
  'data.importExport.entity.tags.description': '会话标签',
  'data.importExport.entity.canned_responses.name': '快捷回复',
  'data.importExport.entity.canned_responses.description': '快捷回复模板',
  'data.importExport.entity.mail_templates.name': '邮件模板',
  'data.importExport.entity.mail_templates.description': '邮件通知模板',
  'data.importExport.entity.email_signatures.name': '邮件签名',
  'data.importExport.entity.email_signatures.description': '邮箱签名',
  'data.importExport.entity.conversations.name': '工单',
  'data.importExport.entity.conversations.description': '工单列表（不含正文与附件）',
  'data.importExport.entity.knowledge_articles.name': '知识库文章',
  'data.importExport.entity.knowledge_articles.description': '文章标题/分类/状态/正文（Markdown 文本）',
  'data.importExport.entity.users.name': '用户',
  'data.importExport.entity.users.description': '系统用户',

  'data.audit.entityType.messenger': 'Messenger',
  'data.audit.entityType.telegram': 'Telegram',
  'data.audit.entityType.line': 'LINE',
  'data.audit.entityType.imessage': 'Apple 信息',
} satisfies Entries;
