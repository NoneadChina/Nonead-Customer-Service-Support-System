import type { Entries } from '../../i18n/types';

/**
 * 通知中心文案（真源）。抽取自：
 * `pages/NotificationTemplates.tsx`、`pages/Notifications.tsx`、
 * `pages/NotificationRules.tsx`、`components/NotificationBell.tsx`。
 *
 * 分区约定：
 * - `notifications.eventType.*`  事件类型标签（三个页面共用同一份 22 项清单）
 * - `notifications.common.*`     跨页面复用的通用标签与提示
 * - `notifications.templates.*`  通知模板页（NotificationTemplates.tsx）
 * - `notifications.list.*`       通知管理页（Notifications.tsx）
 * - `notifications.rules.*`      通知规则页（NotificationRules.tsx）
 * - `notifications.bell.*`       顶栏个人通知铃铛（NotificationBell.tsx）
 *
 * **状态 / 优先级 / 来源类型 / 角色 / 时区标签不在这里**：它们由 `format` 模块持有
 * （`format.status.*` 等），本模块只引用、不重复造键。
 */
export const notifications = {
  // =====================================================================
  // 事件类型（NotificationTemplates / Notifications / NotificationRules 三页共用）
  // =====================================================================
  'notifications.eventType.newConversation': '新会话',
  'notifications.eventType.newReply': '新回复',
  'notifications.eventType.conversationAssigned': '会话已分配',
  'notifications.eventType.conversationUpdated': '会话已更新',
  'notifications.eventType.conversationClosed': '会话已关闭',
  'notifications.eventType.customerReply': '客户回复',
  'notifications.eventType.mention': '被提及',
  'notifications.eventType.slaViolation': 'SLA 违规',
  'notifications.eventType.slaWarning': 'SLA 预警',
  'notifications.eventType.conversationTimeout': '会话超时未响应',
  'notifications.eventType.ticketEscalated': '工单已升级',
  // 后端 `NotificationService.GetEventTypeLabel` 识别、但当前 UI 不作为筛选项暴露的两个码：
  // 它们没有任何产出方（全库无此类模板/规则），登记在此只为"万一出现也不把原始码落到界面"
  'notifications.eventType.ticketCreated': '工单已创建',
  'notifications.eventType.ticketUpdated': '工单已更新',
  'notifications.eventType.customerSatisfaction': '客户满意度评价',
  'notifications.eventType.autoReply': '自动回复',
  'notifications.eventType.userWelcome': '新用户欢迎',
  'notifications.eventType.passwordReset': '密码重置',
  'notifications.eventType.bulkImportCompleted': '批量导入完成',
  'notifications.eventType.conversationReopened': '会话已重新打开',
  'notifications.eventType.conversationTransferred': '会话已转交',
  'notifications.eventType.satisfactionSubmitted': '满意度评价已提交',
  'notifications.eventType.dailyReport': '每日报表汇总',
  'notifications.eventType.accountLocked': '账户已锁定',
  'notifications.eventType.systemMaintenance': '系统维护',

  // =====================================================================
  // 跨页面复用的通用标签与提示
  // =====================================================================
  'notifications.common.all': '全部',
  'notifications.common.new': '新建',
  'notifications.common.enabled': '已启用',
  'notifications.common.disabled': '已停用',
  'notifications.common.enable': '启用',
  'notifications.common.disable': '停用',
  'notifications.common.onlyEnabled': '仅已启用',
  'notifications.common.onlyDisabled': '仅已停用',
  'notifications.common.allEvents': '🗂 所有事件',
  'notifications.common.resetFilters': '重置筛选',
  'notifications.common.clearSearch': '清除搜索',
  'notifications.common.currentFilters': '当前筛选：',
  'notifications.common.refresh': '刷新',
  'notifications.common.saving': '保存中...',
  'notifications.common.selectEventType': '请选择事件类型',
  'notifications.common.allMailboxes': '全部邮箱',
  'notifications.common.unsetSubject': '(未设置主题)',
  'notifications.common.unbound': '未绑定',
  'notifications.common.unboundTemplate': '未绑定模板',
  'notifications.common.unknownError': '未知错误',

  // ── 表头 / 字段标签 ──
  'notifications.common.name': '名称',
  'notifications.common.subject': '主题',
  'notifications.common.eventType': '事件类型',
  'notifications.common.status': '状态',
  'notifications.common.updatedAt': '更新时间',
  'notifications.common.actions': '操作',

  // ── 分页 ──
  'notifications.common.previousPage': '上一页',
  'notifications.common.nextPage': '下一页',
  // 分页文本被两个带样式的数字元素（`<span className="font-bold…">` / `<b>`）切成三截，
  // 故拆成 前缀 / 中缀 / 后缀 三条，**不要**合并成一条带插值的词条：
  // 合并会把数字从加粗元素里挪进译文字符串，丢掉原有样式（波次 3 的已知缺陷类别）。
  'notifications.common.paginationPrefix': '共 ',
  'notifications.common.paginationMiddle': ' 条，第 ',
  'notifications.common.paginationSuffix': ' 页',

  // ── 垃圾箱（模板页与规则页共用） ──
  'notifications.common.trash': '垃圾箱',
  'notifications.common.moveToTrash': '移入垃圾箱',
  'notifications.common.restore': '恢复',
  'notifications.common.deleteForever': '彻底删除',
  'notifications.common.emptyTrash': '清空垃圾箱',
  'notifications.common.deletedAt': '删除时间',

  // ── 错误 / 提示（多页共用） ──
  'notifications.common.loadFailed': '加载失败：{message}',
  'notifications.common.saveFailed': '保存失败',
  'notifications.common.deleteFailed': '删除失败',
  'notifications.common.copyFailed': '复制失败：{message}',
  'notifications.common.opFailed': '操作失败：{message}',
  'notifications.common.previewFailed': '预览失败',
  'notifications.common.trashLoadFailed': '加载垃圾箱失败',
  'notifications.common.restoreFailed': '恢复失败',
  'notifications.common.clearFailed': '清空失败',
  'notifications.common.copiedAs': '已复制为「{name}」',

  // =====================================================================
  // 顶栏个人通知铃铛（components/NotificationBell.tsx）
  // =====================================================================
  'notifications.bell.login': '登录',
  'notifications.bell.logout': '登出',
  'notifications.bell.create': '创建',
  'notifications.bell.update': '更新',
  'notifications.bell.assign': '分配',
  'notifications.bell.statusChange': '状态变更',
  'notifications.bell.reply': '回复',
  'notifications.bell.note': '备注',
  'notifications.bell.title': '个人通知',
  'notifications.bell.unreadCount': '个人通知，{count} 条未读',
  'notifications.bell.unreadOnly': '{count} 条未读',
  'notifications.bell.allRead': '全部已读',
  'notifications.bell.markAllRead': '全部标为已读',
  'notifications.bell.empty': '暂无通知',
  'notifications.bell.emptyHint': '您的操作记录会显示在这里',
  'notifications.bell.viewAllLogs': '查看全部审计日志 →',
  // =====================================================================
  // 通知模板页（pages/NotificationTemplates.tsx）
  // =====================================================================
  // =====================================================================
  // 页头 / 标签页
  // =====================================================================
  'notifications.templates.title': '通知模板管理',
  'notifications.templates.subtitle':
    '管理各类通知事件的邮件模板 · 支持 50+ 内置变量 · 实时 HTML 预览 · 本地变量替换秒级呈现',
  'notifications.templates.heroBadge': '实时渲染引擎',
  'notifications.templates.tabOverview': '概览',
  'notifications.templates.tabList': '模板列表',

  // =====================================================================
  // 概览：统计卡片 / 事件分布
  // =====================================================================
  'notifications.templates.statTotal': '模板总数',
  'notifications.templates.statTotalHint': '全部模板（含停用）',
  'notifications.templates.statActiveHint': '当前参与通知匹配',
  'notifications.templates.statInactiveHint': '暂不参与通知匹配',
  'notifications.templates.statUnit': '个',
  'notifications.templates.distributionTitle': '按事件类型分布',
  'notifications.templates.distributionSubtitle': '展示每种事件已配置模板数及启用情况',
  'notifications.templates.activeRatio': '启用占比',
  'notifications.templates.inactiveRatio': '停用占比',
  'notifications.templates.ratioLabel': '占比',

  // =====================================================================
  // 空态
  // =====================================================================
  'notifications.templates.emptyNone': '暂无模板',
  'notifications.templates.emptyOverviewHint': '先去列表页新建一个通知模板吧',
  'notifications.templates.emptyListFiltered': '没有匹配筛选条件的模板，试试清除筛选',
  'notifications.templates.emptyListNone': '还没有任何模板，点击右上角新建第一个',

  // =====================================================================
  // 列表页：筛选栏
  // =====================================================================
  'notifications.templates.filterSearch': '搜索：',
  'notifications.templates.filterEvent': '事件：',
  'notifications.templates.filterStatus': '状态：',
  'notifications.templates.searchPlaceholder': '搜索模板名称或主题...',
  'notifications.templates.allEventTypes': '所有事件类型',
  'notifications.templates.loadingList': '加载模板列表...',
  'notifications.templates.clearAllFilters': '清除所有筛选',
  'notifications.templates.newTemplate': '新建模板',

  // =====================================================================
  // 列表页：表格 / 卡片
  // =====================================================================
  'notifications.templates.templateName': '模板名称',
  'notifications.templates.createdId': '创建 ID · {id}',
  'notifications.templates.subjectNotSet': '(未设置)',
  'notifications.templates.subjectCopied': '主题已复制到剪贴板',
  'notifications.templates.copySubject': '复制主题',
  'notifications.templates.clickToDisable': '点击停用',
  'notifications.templates.clickToEnable': '点击启用',
  'notifications.templates.previewTemplate': '预览模板',
  'notifications.templates.duplicateTemplate': '复制模板',
  'notifications.templates.editTemplate': '编辑模板',

  // =====================================================================
  // 编辑器：标题栏 / 基础字段
  // =====================================================================
  'notifications.templates.editorTitleEdit': '编辑通知模板',
  'notifications.templates.editorTitleCreate': '新建通知模板',
  'notifications.templates.editorBadge': 'HTML · 变量 · 预览',
  'notifications.templates.editorSubtitle':
    '支持富文本 HTML 内容、50+ 内置变量替换与秒级本地实时预览',
  'notifications.templates.closeEditor': '关闭编辑器',
  'notifications.templates.basicInfo': '模板基本信息',
  'notifications.templates.requiredHint': '带 * 为必填项',
  'notifications.templates.namePlaceholder': '例如：新会话通知模板 - 客服版本',
  'notifications.templates.fieldSubject': '通知主题',
  'notifications.templates.switchInsertTargetSubjectTitle': '点此切换变量插入目标为主题行',
  'notifications.templates.insertTargetSubject': '插入目标：主题',
  'notifications.templates.switchToSubject': '切换插入到主题',
  'notifications.templates.subjectPlaceholder':
    '例如：您有一个新的会话 #{conversation_number} - {subject}',
  'notifications.templates.subjectPreview': '主题预览',
  'notifications.templates.emptyValue': '(空)',

  // =====================================================================
  // 编辑器：视图模式 / 代码与预览
  // =====================================================================
  'notifications.templates.viewModeEditor': '仅代码',
  'notifications.templates.viewModeEditorHint': '全宽代码编辑',
  'notifications.templates.viewModeSplit': '左右分栏',
  'notifications.templates.viewModeSplitHint': '代码+实时预览',
  'notifications.templates.viewModePreview': '仅预览',
  'notifications.templates.viewModePreviewHint': '实时 HTML 渲染',
  'notifications.templates.insertTargetLabel': '插入目标：',
  'notifications.templates.htmlBody': 'HTML 正文',
  'notifications.templates.subjectLine': '主题行',
  'notifications.templates.htmlCodeEditor': 'HTML · 代码编辑器',
  'notifications.templates.charUnit': '字符',
  'notifications.templates.bodyPlaceholderEditor':
    '支持 HTML + 变量，示例：\n<p>您好 <b>{customer_name}</b>，</p>\n<p>会话 <a href="{conversation_url}">#{conversation_number}</a> 已收到新回复。</p>',
  'notifications.templates.mailtoPreview': '📧 mailto:customer@example.com · 实时预览',
  'notifications.templates.localLivePreview': '本地实时预览',
  'notifications.templates.htmlPreviewArea': 'HTML 预览区',
  'notifications.templates.htmlPreviewAreaHint':
    '在代码编辑器输入 HTML 内容或插入变量，此处实时显示渲染效果',
  'notifications.templates.codeLabel': '代码',
  'notifications.templates.bodyPlaceholderSplit': '例如：\n<p>您好 <b>{customer_name}</b>！</p>',
  'notifications.templates.livePreview': '实时预览',
  'notifications.templates.debounce200': '防抖 200ms',
  'notifications.templates.previewEmptyTitle': '输入 HTML 后此处实时显示',
  'notifications.templates.previewEmptyHint': '变量替换使用示例数据',

  // =====================================================================
  // 编辑器：变量检测 / 启用开关
  // =====================================================================
  'notifications.templates.variableCheck': '变量检测',
  'notifications.templates.variableCheckHint': '识别当前主题与正文中共使用的变量',
  'notifications.templates.variablesCount': '共 {count} 个变量',
  'notifications.templates.commonVariablesCount': '常用变量 · {count} 个',
  'notifications.templates.uncommonVariablesCount': '非常用变量 · {count} 个',
  'notifications.templates.noVariablesDetected':
    '当前未识别到任何变量，试试在右侧面板插入一个变量',
  'notifications.templates.templateActive': '模板已启用',
  'notifications.templates.templateInactive': '模板已停用',
  'notifications.templates.templateActiveHint':
    '当前模板参与通知匹配，事件触发时将使用此模板渲染通知内容',
  'notifications.templates.templateInactiveHint':
    '停用后，此模板将不会被通知规则匹配到，适合暂存或草稿',

  // =====================================================================
  // 编辑器：变量面板
  // =====================================================================
  'notifications.templates.availableVariables': '可用变量',
  // 计数被包在带样式的 <span> 里（加粗 + 主色），故拆成前缀 / 后缀两条，
  // 数字仍留在原元素内（波次 3 的已知缺陷类别：合并会丢掉元素样式）。
  'notifications.templates.availableCountPrefix': '共 ',
  'notifications.templates.availableCountSuffix': ' 个',
  'notifications.templates.variableSearchPlaceholder': '搜索变量名或分类...',
  'notifications.templates.emailTitle': '邮件标题',
  'notifications.templates.emailContent': '邮件内容',
  'notifications.templates.noMatchingVariables': '未找到匹配的变量',
  'notifications.templates.noMatchingVariablesHint': '尝试其他关键词或清除搜索',
  'notifications.templates.insertVariableTooltip': '{label} — 点击插入「{variable}」到{target}',
  'notifications.templates.insertTargetSubjectEnd': '主题行末尾',
  'notifications.templates.insertTargetBodyEnd': 'HTML正文末尾',
  'notifications.templates.clickVariableToInsert': '点击变量即可插入到',

  // =====================================================================
  // 编辑器：底部操作栏
  // =====================================================================
  'notifications.templates.backendRendering': '后端渲染中...',
  'notifications.templates.backendFullPreview': '后端完整渲染预览',
  'notifications.templates.saveTemplate': '保存模板',

  // =====================================================================
  // Toast / 表单错误
  // =====================================================================
  'notifications.templates.overviewLoadFailed': '加载概览失败：{message}',
  'notifications.templates.listLoadFailed': '加载列表失败：{message}',
  'notifications.templates.templateLoadFailed': '加载模板失败：{message}',
  'notifications.templates.nameRequired': '请输入模板名称',
  'notifications.templates.subjectRequired': '请输入邮件主题',
  'notifications.templates.templateUpdated': '模板更新成功',
  'notifications.templates.templateCreated': '模板创建成功',
  'notifications.templates.toggleSuccess': '模板已{state}',
  'notifications.templates.templateMovedToTrash': '模板已移入垃圾箱',
  'notifications.templates.deleteFailedWithDetail': '删除失败：{message}',
  'notifications.templates.templateRestored': '模板「{name}」已恢复',
  'notifications.templates.templateHardDeleted': '模板「{name}」已彻底删除',
  'notifications.templates.trashEmptied': '已清空垃圾箱：共 {count} 条',

  // =====================================================================
  // 垃圾箱
  // =====================================================================
  'notifications.templates.trashModalTitle': '通知模板垃圾箱',
  // 计数被 <b> 包住，同 availableCount* 的处理方式
  'notifications.templates.trashCountPrefix': '垃圾箱共 ',
  'notifications.templates.trashCountSuffix': ' 个模板',
  'notifications.templates.trashSearchPlaceholder': '搜索已删除模板的名称或主题...',
  'notifications.templates.keywordChip': '关键词：{keyword}',
  'notifications.templates.trashEmpty': '垃圾箱为空',
  'notifications.templates.trashNoKeywordMatch': '未匹配到关键词，请换个词试试',
  'notifications.templates.trashEmptyHint': '已删除的模板将在这里保留，可恢复或彻底删除',
  'notifications.templates.restoreToList': '恢复到模板列表',
  'notifications.templates.hardDeleteWarning': '彻底删除，不可恢复',

  // =====================================================================
  // 确认弹窗
  // =====================================================================
  'notifications.templates.confirmMoveToTrash': '确认将通知模板「{name}」移入垃圾箱吗？',
  'notifications.templates.restoreTemplate': '恢复模板',
  'notifications.templates.confirmRestore': '确认恢复通知模板「{name}」吗？',
  'notifications.templates.confirmHardDelete':
    '确认彻底删除通知模板「{name}」吗？此操作不可撤销。',
  'notifications.templates.confirmEmptyTrash':
    '确定要清空垃圾箱吗？所有已删除的模板将被彻底删除，此操作不可撤销。',
  'notifications.templates.emptyTrashConfirm': '清空',

  // =====================================================================
  // 预览弹窗
  // =====================================================================
  'notifications.templates.templatePreview': '模板预览',
  'notifications.templates.emailSubject': '邮件主题',
  'notifications.templates.emptySubject': '(空主题)',
  'notifications.templates.htmlPreviewTab': 'HTML 预览',
  'notifications.templates.htmlSourceTab': 'HTML 原文',
  'notifications.templates.htmlSourceCode': 'HTML 原始代码',
  'notifications.templates.htmlRenderResult': 'HTML 渲染结果',
  'notifications.templates.renderComplete': '渲染完成',
  'notifications.templates.bodyEmpty': '正文为空',
  'notifications.templates.bodyEmptyHintHtml': '此模板暂无 HTML 内容',
  'notifications.templates.bodyEmptyHintSource': '此模板暂无 HTML 源码',

  // =====================================================================
  // 内置变量标签（builtInNotificationVariables）
  // =====================================================================
  'notifications.templates.var.conversationId': '会话 ID',
  'notifications.templates.var.conversationNumber': '会话编号',
  'notifications.templates.var.conversationSubject': '会话主题',
  'notifications.templates.var.conversationUrl': '会话链接',
  'notifications.templates.var.createdAt': '创建时间',
  'notifications.templates.var.updatedAt': '更新时间',
  'notifications.templates.var.priority': '优先级',
  'notifications.templates.var.status': '状态',
  'notifications.templates.var.tags': '标签',
  'notifications.templates.var.folderName': '所属文件夹',
  'notifications.templates.var.customerName': '客户姓名',
  'notifications.templates.var.customerEmail': '客户邮箱',
  'notifications.templates.var.customerPhone': '客户电话',
  'notifications.templates.var.customerCompany': '客户公司',
  'notifications.templates.var.userName': '处理人姓名',
  'notifications.templates.var.userEmail': '处理人邮箱',
  'notifications.templates.var.userRole': '处理人角色',
  'notifications.templates.var.assignedUserName': '被分配人姓名',
  'notifications.templates.var.assignedUserEmail': '被分配人邮箱',
  'notifications.templates.var.closedBy': '关闭人',
  'notifications.templates.var.replyFrom': '回复人',
  'notifications.templates.var.mailboxName': '邮箱名称',
  'notifications.templates.var.mailboxEmail': '邮箱地址',
  'notifications.templates.var.messageBody': '原始消息 HTML',
  'notifications.templates.var.content': '消息内容纯文本',
  'notifications.templates.var.replyContent': '回复内容',
  'notifications.templates.var.slaName': 'SLA 名称',
  'notifications.templates.var.warningTime': '预警时间',
  'notifications.templates.var.violationTime': '违规时间',
  'notifications.templates.var.firstResponseDeadline': '首次响应截止',
  'notifications.templates.var.resolveDeadline': '解决截止',
  'notifications.templates.var.companyName': '公司名称',
  'notifications.templates.var.loginUrl': '登录链接',
  'notifications.templates.var.systemUrl': '系统地址',
  'notifications.templates.var.supportEmail': '支持邮箱',
  'notifications.templates.var.satisfactionUrl': '满意度调查链接',
  'notifications.templates.var.satisfactionScore': '满意度评分',
  'notifications.templates.var.satisfactionComment': '满意度评价',
  'notifications.templates.var.resetPasswordUrl': '密码重置链接',
  'notifications.templates.var.accountLockReason': '锁定原因',
  'notifications.templates.var.lockDuration': '锁定时长',
  'notifications.templates.var.importTotal': '导入总数',
  'notifications.templates.var.importSuccess': '导入成功数',
  'notifications.templates.var.importFailed': '导入失败数',
  'notifications.templates.var.reportDate': '报表日期',
  'notifications.templates.var.maintenanceStart': '维护开始时间',
  'notifications.templates.var.maintenanceEnd': '维护结束时间',
  'notifications.templates.var.maintenanceContent': '维护内容',

  // =====================================================================
  // 内置变量分类
  // =====================================================================
  'notifications.templates.varCategory.conversation': '会话信息',
  'notifications.templates.varCategory.customer': '客户信息',
  'notifications.templates.varCategory.agent': '客服信息',
  'notifications.templates.varCategory.mailbox': '邮箱信息',
  'notifications.templates.varCategory.message': '消息内容',
  'notifications.templates.varCategory.sla': 'SLA 相关',
  'notifications.templates.varCategory.brand': '系统品牌',
  'notifications.templates.varCategory.satisfaction': '满意度',
  'notifications.templates.varCategory.account': '密码账户',
  'notifications.templates.varCategory.import': '导入报表',
  'notifications.templates.varCategory.maintenance': '维护通知',

  // =====================================================================
  // 本地实时预览用示例数据（用户可见）
  // =====================================================================
  'notifications.templates.sample.subject': '测试工单 - 系统登录问题',
  'notifications.templates.sample.status': '处理中',
  'notifications.templates.sample.tags': '技术支持,登录',
  'notifications.templates.sample.folderName': '技术支持',
  'notifications.templates.sample.customerName': '测试客户',
  'notifications.templates.sample.customerCompany': '示例科技有限公司',
  'notifications.templates.sample.assignedUserName': '张三',
  'notifications.templates.sample.replyFrom': '客服小张',
  'notifications.templates.sample.mailboxName': '支持邮箱',
  'notifications.templates.sample.messageBody': '<p>您好，我无法登录系统，提示密码错误。</p>',
  'notifications.templates.sample.content': '您好，我无法登录系统，提示密码错误。',
  'notifications.templates.sample.replyContent': '您好，问题已经解决，请查看附件。',
  'notifications.templates.sample.slaName': '标准SLA',
  'notifications.templates.sample.warningTime': '30分钟',
  'notifications.templates.sample.violationTime': '1小时',
  'notifications.templates.sample.firstResponseDeadline': '30 分钟内',
  'notifications.templates.sample.resolveDeadline': '4 小时内',
  'notifications.templates.sample.satisfactionScore': '5 分（非常满意）',
  'notifications.templates.sample.satisfactionComment': '服务态度非常好，问题解决及时！',
  'notifications.templates.sample.accountLockReason': '多次登录失败',
  'notifications.templates.sample.lockDuration': '30 分钟',
  'notifications.templates.sample.maintenanceContent': '系统升级与数据库优化',

  // =====================================================================
  // 通知管理页（pages/Notifications.tsx）
  // =====================================================================
  // =====================================================================
  // 页头 / Tab 容器
  // =====================================================================
  'notifications.list.pageTitle': '通知中心 · 通知管理',
  'notifications.list.pageSubtitle':
    '统一管理通知模板、触发规则、手动发送与模板预览 —— 让每一条客服通知都精准、高效、可控',

  'notifications.list.tab.templates': '通知模板',
  'notifications.list.tab.templatesHint': '管理邮件/通知内容模板',
  'notifications.list.tab.rules': '通知规则',
  'notifications.list.tab.rulesHint': '配置事件触发与匹配逻辑',
  'notifications.list.tab.send': '发送通知',
  'notifications.list.tab.sendHint': '手动触发一次通知发送',
  'notifications.list.tab.preview': '模板预览',
  'notifications.list.tab.previewHint': '实时渲染模板变量效果',

  // =====================================================================
  // 跨 Tab 复用（本页独有）
  // =====================================================================
  'notifications.list.loadFailed': '加载失败',
  'notifications.list.opFailed': '操作失败',
  'notifications.list.refreshList': '刷新列表',
  'notifications.list.newTemplate': '新建模板',
  'notifications.list.newRule': '新建规则',
  'notifications.list.emptyFilteredHint': '试试调整筛选条件',
  // 统计卡片大数字右侧的单位（独立 `<span className="text-sm …">` 元素），
  // 数字本身不在词条里——英文按单复数取 one/other，故调用处要传 count。
  'notifications.list.statUnit': '条',
  'notifications.list.allUsers': '全部用户',
  'notifications.list.applicableMailbox': '适用邮箱',
  'notifications.list.notifyUser': '通知用户',
  'notifications.list.useTemplate': '使用模板',
  'notifications.list.noTemplate': '不使用模板',
  'notifications.list.conversationId': '会话 ID',
  'notifications.list.unspecified': '未指定',
  'notifications.list.customVariables': '自定义变量',
  'notifications.list.varNamePlaceholder': '变量名',
  'notifications.list.varValuePlaceholder': '变量值',
  'notifications.list.removeVariable': '移除此变量',
  'notifications.list.editTemplate': '编辑模板',
  'notifications.list.deleteTemplate': '删除模板',
  'notifications.list.editRule': '编辑规则',
  'notifications.list.deleteRule': '删除规则',

  // =====================================================================
  // 模板 Tab
  // =====================================================================
  'notifications.list.tplStatTotal': '模板总数',
  'notifications.list.tplStatTotalSub': '所有已配置模板',
  'notifications.list.tplStatActive': '已启用模板',
  'notifications.list.tplStatActiveSub': '可被规则匹配',
  'notifications.list.tplStatInactive': '已停用模板',
  'notifications.list.tplStatInactiveSub': '暂不参与匹配',
  'notifications.list.tplSearchPlaceholder': '搜索模板名称、主题...',
  'notifications.list.tplEmpty': '暂无通知模板',
  'notifications.list.tplEmptyFiltered': '暂无匹配的模板',
  'notifications.list.tplEmptyHint': '点击右上角「新建模板」开始配置',
  'notifications.list.tplMeta': '创建模板',

  'notifications.list.tplFormRequired': '名称、主题、正文均不能为空',
  'notifications.list.tplModalEditTitle': '编辑通知模板',
  'notifications.list.tplModalNewTitle': '新建通知模板',
  'notifications.list.tplFormEditHeading': '编辑模板',
  'notifications.list.tplFormNewHeading': '创建新模板',
  'notifications.list.tplFormSubtitle': '配置通知的主题、正文与绑定的事件类型',
  'notifications.list.tplNameLabel': '模板名称',
  'notifications.list.tplNamePlaceholder': '如：新会话通知客服',
  'notifications.list.emailSubjectLabel': '邮件主题',
  'notifications.list.emailSubjectPlaceholder': '支持变量，如：新会话: {subject}',
  'notifications.list.emailBodyLabel': '邮件正文',
  'notifications.list.emailBodyPlaceholder':
    '支持 HTML 和变量，如：<p>您好 {customer_name}，您的会话已收到回复。</p>',
  'notifications.list.quickVariablesInsert': '快捷变量（点击插入到正文末尾）：',
  'notifications.list.clickToInsertBody': '点击插入到正文',
  'notifications.list.tplActiveLabel': '启用此模板（允许通知规则匹配使用）',
  'notifications.list.confirmDeleteTemplate':
    '确认删除通知模板「{name}」吗？关联的规则将失去模板引用。',

  // =====================================================================
  // 规则 Tab
  // =====================================================================
  'notifications.list.ruleStatTotal': '规则总数',
  'notifications.list.ruleStatTotalSub': '所有已配置通知规则',
  'notifications.list.ruleStatActive': '已启用规则',
  'notifications.list.ruleStatActiveSub': '当前正在生效中',
  'notifications.list.ruleStatInactive': '已停用规则',
  'notifications.list.ruleStatInactiveSub': '暂未触发通知',
  'notifications.list.enableRate': '启用率',
  'notifications.list.ruleSearchPlaceholder': '搜索规则名称...',
  'notifications.list.ruleEmpty': '暂无通知规则',
  'notifications.list.ruleEmptyFiltered': '暂无匹配的规则',
  'notifications.list.ruleEmptyHint': '点击右上角「新建规则」开始配置',
  'notifications.list.ruleMeta': '事件规则配置',

  'notifications.list.ruleNameLabel': '规则名称',
  'notifications.list.ruleNameRequired': '规则名称不能为空',
  'notifications.list.ruleModalEditTitle': '编辑通知规则',
  'notifications.list.ruleModalNewTitle': '新建通知规则',
  'notifications.list.ruleFormEditHeading': '编辑规则',
  'notifications.list.ruleFormNewHeading': '创建新规则',
  'notifications.list.ruleFormSubtitle': '配置事件触发、匹配范围与使用的通知模板',
  'notifications.list.ruleNamePlaceholder': '如：新会话通知管理员',
  'notifications.list.mailboxHint': '只有匹配该邮箱的事件才会触发',
  'notifications.list.notifyUserHint': '指定后仅向该用户发送通知',
  'notifications.list.ruleActiveLabel': '启用此规则（事件发生时自动匹配并发送通知）',
  'notifications.list.confirmDeleteRule': '确认删除通知规则「{name}」吗？',

  // =====================================================================
  // 发送通知 Tab
  // =====================================================================
  'notifications.list.sendTitle': '手动发送通知',
  'notifications.list.sendSubtitle': '根据事件类型匹配启用的通知规则，向目标用户即时发送通知',
  'notifications.list.sendScopeRequired': '请选择适用邮箱或填写会话 ID，以便确定通知范围',
  'notifications.list.sendNoRules':
    '未匹配到任何通知规则，请先在"通知规则"中为该事件类型配置启用的规则。',
  // 发送结果提示是纯文本（无元素包裹、无加粗数字），故用整句插值而不拆段。
  'notifications.list.sendAllFailedWithErrors':
    '共匹配 {rules} 条规则，但成功发送 0 条，失败 {failed} 条：{errors}',
  'notifications.list.sendAllFailedNoErrors':
    '共匹配 {rules} 条规则，但成功发送 0 条，请检查规则收件人及邮箱配置',
  'notifications.list.sendResultSuccess': '发送成功：共 {rules} 条规则，成功发送 {sent} 条通知。',
  'notifications.list.sendResultPartial':
    '发送完成：共 {rules} 条规则，成功 {sent} 条，失败 {failed} 条。{errors}',
  'notifications.list.sendFailed': '发送失败',
  'notifications.list.conversationIdOptional': '会话 ID（可选）',
  'notifications.list.conversationIdPlaceholder': '如指定，将使用该会话上下文渲染模板',
  'notifications.list.conversationIdHint': '留空则按事件类型和邮箱范围匹配所有相关会话',
  'notifications.list.quickAddVariables': '快捷添加常用变量：',
  'notifications.list.varNamePlaceholderLong': '变量名（如 customer_name 或 {customer_name}）',
  'notifications.list.emptyValue': '(空)',
  'notifications.list.sending': '发送中...',
  'notifications.list.sendNow': '立即发送通知',
  'notifications.list.currentConfig': '当前配置预览',
  'notifications.list.tips': '使用提示',
  'notifications.list.tip1': '匹配启用中的规则才能成功发送',
  'notifications.list.tip2': '会话 ID 优先级高于邮箱范围',
  'notifications.list.tip3': '自定义变量会覆盖模板中同名占位',

  // =====================================================================
  // 模板预览 Tab（含示例数据）
  // =====================================================================
  // 示例模板正文：`{customer_name}` 等是**字面量变量占位**，调用处不传插值参数，
  // 故 `translate` 会原样返回（占位符只在 params 提供时才被替换）。
  'notifications.list.sampleTemplate':
    '<h2>您好 {customer_name}，</h2>\n<p>您的会话「<b>{subject}</b>」已收到回复。</p>\n<ul>\n  <li>会话编号：<code>{conversation_id}</code></li>\n  <li>处理客服：<b>{user_name}</b></li>\n  <li>所属邮箱：{mailbox_name} ({mailbox_email})</li>\n</ul>\n<p>感谢您的耐心等待！</p>',
  'notifications.list.sample.customerName': '张三',
  'notifications.list.sample.subject': '订单问题咨询',
  'notifications.list.sample.agentName': '客服小李',
  'notifications.list.sample.mailboxName': '客服支持',

  'notifications.list.previewInputTitle': '模板输入',
  'notifications.list.previewInputHint': '使用 {variable} 形式引用变量，支持 HTML 标签',
  'notifications.list.templateContentLabel': '模板内容',
  'notifications.list.templateContentPlaceholder': '输入模板内容，使用 {variable} 形式引用变量',
  'notifications.list.quickAddVariablesCreate': '快捷添加变量（不存在则新建）：',
  'notifications.list.variableDictionary': '变量字典',
  'notifications.list.noVariables': '暂无变量，点击上方快捷按钮或手动添加',
  'notifications.list.rendering': '渲染中...',
  'notifications.list.renderPreview': '渲染预览',
  'notifications.list.renderResult': '渲染结果',
  'notifications.list.renderResultSubtitle': '模板变量替换后的实际效果',
  'notifications.list.copyResult': '复制结果',
  'notifications.list.htmlPreview': 'HTML 渲染预览',
  'notifications.list.rawText': '原始文本',
  'notifications.list.waitingPreview': '等待渲染预览',
  'notifications.list.waitingPreviewHint': '编辑模板和变量后，点击「渲染预览」查看结果',

  // =====================================================================
  // 通知规则页（pages/NotificationRules.tsx）
  // =====================================================================
  // ── 页头 / Tab ──
  'notifications.rules.title': '通知规则管理',
  'notifications.rules.subtitle': '配置通知触发规则，根据事件自动匹配模板发送通知',
  'notifications.rules.tabOverview': '概览',
  'notifications.rules.tabList': '规则列表',

  // ── 概览：统计卡片 ──
  'notifications.rules.statTotal': '规则总数',
  'notifications.rules.statTotalHint': '所有已配置通知规则',
  'notifications.rules.statActive': '已启用规则',
  'notifications.rules.statActiveHint': '当前正在生效中',
  'notifications.rules.statInactive': '已停用规则',
  'notifications.rules.statInactiveHint': '暂未触发通知',
  // 大数字后的量词：英文按 count 取单复数（调用点必须带 count 参数）
  'notifications.rules.countUnit': '条',
  'notifications.rules.enableRate': '启用率',

  // ── 概览：事件类型分布 ──
  'notifications.rules.eventDistribution': '事件类型分布',
  'notifications.rules.eventSummary': '共 {rules} 条规则，涵盖 {types} 类事件',
  'notifications.rules.emptyTitle': '还没有任何规则',
  'notifications.rules.emptyHint': '点击右上角「新建规则」开始配置',
  // 标签后紧跟 <code> 元素，故英文侧值自带一个尾随空格（与 paginationPrefix 同法）
  'notifications.rules.eventKeyLabel': '事件键名：',
  'notifications.rules.ratio': '占比 {percent}%',

  // ── 列表：筛选栏 ──
  'notifications.rules.searchPlaceholder': '搜索规则名称、事件、描述...',
  'notifications.rules.newRule': '新建规则',
  'notifications.rules.emptyListTitle': '暂无匹配的规则',
  'notifications.rules.emptyListHint': '试试调整筛选条件或新建一条规则',

  // ── 列表：表格与行内动作 ──
  'notifications.rules.ruleName': '规则名称',
  'notifications.rules.scope': '适用范围',
  'notifications.rules.linkedTemplate': '关联模板',
  'notifications.rules.assignedUser': '指定用户',
  'notifications.rules.toggleToDisable': '点击切换为停用',
  'notifications.rules.toggleToEnable': '点击切换为启用',
  'notifications.rules.copyRule': '复制规则',
  'notifications.rules.editRule': '编辑规则',

  // ── 编辑器弹窗 ──
  'notifications.rules.modalEditTitle': '编辑通知规则',
  'notifications.rules.modalCreateTitle': '✨ 新建通知规则',
  'notifications.rules.modalEditSubtitle': '修改现有规则的触发条件和通知模板',
  'notifications.rules.modalCreateSubtitle': '配置一条新的通知规则，让重要事件自动提醒',
  'notifications.rules.activeOnSave': '保存后启用',
  'notifications.rules.auditNotice': '所有修改均会被审计记录',
  'notifications.rules.saveChanges': '保存修改',
  'notifications.rules.createRule': '创建规则',
  'notifications.rules.formErrorTitle': '请检查并修正',
  'notifications.rules.sectionBasic': '基本信息',
  'notifications.rules.sectionBasicHint': '配置规则名称和触发事件',
  'notifications.rules.nameHint': '· 用于区分和识别这条规则',
  'notifications.rules.namePlaceholder': '例如：新会话通知管理员 - 工单响应SLA 提醒',
  'notifications.rules.eventTypeHint': '· 选择触发此规则的事件',
  'notifications.rules.sectionScopeHint': '限定规则的作用域和通知模板',
  'notifications.rules.mailboxLabel': '适用邮箱',
  'notifications.rules.mailboxHint': '· 不选则全局生效',
  'notifications.rules.allMailboxesOption': '🌐 全部邮箱（所有邮箱的事件都会触发）',
  'notifications.rules.templateLabel': '通知模板',
  'notifications.rules.templateHint': '· 渲染通知内容',
  'notifications.rules.noTemplateOption': '✖ 不使用模板（使用默认消息格式）',
  'notifications.rules.enableRule': '启用此规则',
  'notifications.rules.enableRuleOn': '保存后规则立即生效，事件触发时会发送通知',
  'notifications.rules.enableRuleOff': '保存后规则处于停用状态，不会触发任何通知',
  'notifications.rules.previewTitle': '配置预览',
  'notifications.rules.notSet': '未设置',
  'notifications.rules.previewEvent': '触发事件',
  'notifications.rules.previewStatus': '保存后状态',
  'notifications.rules.previewStatusActive': '已启用 · 生效中',

  // ── 确认弹窗 ──
  'notifications.rules.deleteMessage': '确认将通知规则「{name}」移入垃圾箱吗？',
  'notifications.rules.restoreTitle': '恢复规则',
  'notifications.rules.restoreMessage': '确认恢复通知规则「{name}」吗？',
  'notifications.rules.hardDeleteMessage': '确认彻底删除通知规则「{name}」吗？此操作不可撤销。',
  'notifications.rules.clearTrashMessage': '确定要清空垃圾箱吗？所有已删除的规则将被彻底删除，此操作不可撤销。',
  'notifications.rules.confirmClear': '清空',

  // ── 垃圾箱弹窗 ──
  'notifications.rules.trashTitle': '通知规则垃圾箱',
  'notifications.rules.trashSubtitle': '已删除的规则会在此保留 30 天，可随时恢复或彻底删除',
  'notifications.rules.trashSearchPlaceholder': '🔍 搜索已删除的规则名称、事件、模板...',
  'notifications.rules.trashLoading': '加载垃圾箱中...',
  'notifications.rules.trashEmptyTitle': '垃圾箱干干净净 ✨',
  'notifications.rules.trashEmptySearch': '没有找到匹配搜索条件的已删除规则，试试其他关键字',
  'notifications.rules.trashEmptyAllCleared': '所有已删除规则都已清理完毕',
  'notifications.rules.usedSpace': '已用空间：{count} 条 / ∞',
  // 同上：标签后紧跟带样式的 <span> 元素
  'notifications.rules.templatePrefix': '模板：',
  'notifications.rules.restoreToMainList': '恢复到主列表',
  'notifications.rules.hardDeleteHint': '彻底删除（不可恢复）',

  // ── Toast / 表单错误 ──
  'notifications.rules.nameRequired': '请输入规则名称',
  'notifications.rules.updateSuccess': '规则更新成功',
  'notifications.rules.createSuccess': '规则创建成功',
  'notifications.rules.enabledSuccess': '规则已启用',
  'notifications.rules.disabledSuccess': '规则已停用',
  'notifications.rules.moveToTrashSuccess': '规则已移入垃圾箱',
  'notifications.rules.deleteFailedWithReason': '删除失败：{message}',
  'notifications.rules.restoreSuccess': '规则「{name}」已恢复',
  'notifications.rules.hardDeleteSuccess': '规则「{name}」已彻底删除',
  'notifications.rules.clearTrashSuccess': '已清空垃圾箱：共 {count} 条',
  'notifications.rules.toastSuccess': '✓ 操作成功',
  'notifications.rules.toastError': '✕ 操作失败',
} satisfies Entries;
