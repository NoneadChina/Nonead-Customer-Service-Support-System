import type { Entries } from '../../i18n/types';

/**
 * 邮件事务域（邮箱 / 邮件模板 / 邮件签名 / 文件夹 / 快捷回复）文案真源。
 *
 * 分区：
 * - `common`       本波 5 个页面与 3 个垃圾箱弹窗**确实共用**的文案
 *                  （删除/恢复/彻底删除、垃圾箱骨架、分页、筛选条、错误提示）
 * - `mailboxes`    `pages/Mailboxes.tsx` + `context/MailboxContext.tsx`
 * - `templates`    `pages/MailTemplates.tsx` + `components/MailTemplateTrashModal.tsx`
 * - `signatures`   `pages/EmailSignatures.tsx` + `components/EmailSignatureTrashModal.tsx`
 * - `folders`      `pages/Folders.tsx`
 * - `canned`       `pages/CannedResponses.tsx` + `components/CannedResponseTrashModal.tsx`
 *
 * ⚠ 后端下发的 `MailTemplateResponse.typeName` / `MailTemplateTypeCount.typeName`
 * （`MailTemplateService.GetTypeName` 的中文标签）与 `MailTemplateVariable.label` /
 * `.category`（`TemplateVariableRegistry` 的中文标签）**不再直接渲染**：
 * 模板类型按 `type` 数值码走 `mail.templates.type.*`，模板变量按稳定 `key` 走
 * `mail.templates.variable.*` / `mail.templates.variableCategory.*`（见 `MailTemplates.tsx`）。
 *
 * ⚠ 用户数据（邮箱地址、文件夹名、模板/签名/快捷回复正文与名称、分类名）**不进字典**，原样渲染。
 */
export const mail = {
  // ════════════════════════════════════════════════════════════════
  // common —— 五页与三个垃圾箱弹窗共用
  // ════════════════════════════════════════════════════════════════

  // 表头与通用名词
  'mail.common.actions': '操作',
  'mail.common.status': '状态',
  'mail.common.type': '类型',
  'mail.common.overview': '概览',
  'mail.common.system': '系统',
  'mail.common.custom': '自定义',
  'mail.common.global': '全局',
  'mail.common.active': '活跃',
  'mail.common.enabled': '启用',
  'mail.common.disabled': '禁用',
  'mail.common.preview': '预览',
  'mail.common.allStatuses': '全部状态',
  'mail.common.applicableMailbox': '适用邮箱',
  'mail.common.ownerMailbox': '所属邮箱',
  'mail.common.ownerUser': '所属用户',
  'mail.common.globalAllMailboxes': '全局（所有邮箱）',
  'mail.common.mailboxAndNature': '邮箱 / 性质',
  'mail.common.mailboxAndVisibility': '邮箱 / 可见性',
  'mail.common.deletedAtPrefix': '删除于',
  'mail.common.createdAtPrefix': '创建于',
  'mail.common.updatedAtPrefix': '更新于',

  // 筛选条
  'mail.common.currentFilters': '当前筛选',
  'mail.common.currentFiltersColon': '当前筛选：',
  'mail.common.clearAllFilters': '清除全部',
  'mail.common.removeFilter': '移除该筛选',
  'mail.common.searchPrefix': '搜索：',

  // 垃圾箱（弹窗骨架）
  'mail.common.trash': '垃圾箱',
  'mail.common.recycleBin': '回收站',
  'mail.common.clearTrash': '清空垃圾箱',
  'mail.common.clearAll': '全部清空',
  'mail.common.trashEmpty': '垃圾箱为空',
  'mail.common.loadingTrash': '加载垃圾箱...',
  'mail.common.searchTrash': '搜索回收站',
  'mail.common.trashHeaderDeletion': '删除信息',
  'mail.common.perPage': '每页',
  'mail.common.trashPageSizeOption': '{count} 条',
  'mail.common.trashSortOrderPrefix': '排序 #',
  'mail.common.trashFooterTotalPrefix': '共 ',
  'mail.common.trashFooterTotalSuffix': ' 条已删除',
  'mail.common.trashRangePrefix': '第',
  'mail.common.trashRangeSeparator': '–',
  'mail.common.trashRangeSuffix': '条',
  'mail.common.trashRetentionMiddle': '或',
  'mail.common.trashRetentionSuffix': '。',

  // 动作
  'mail.common.restore': '恢复',
  'mail.common.hardDelete': '彻底删除',
  'mail.common.hardDeleteIrreversible': '彻底删除（不可恢复）',

  // 提示与错误
  'mail.common.trashCleared': '已清空垃圾箱：共 {count} 条',
  'mail.common.deleteFailed': '删除失败',
  'mail.common.restoreFailed': '恢复失败',
  'mail.common.hardDeleteFailed': '彻底删除失败',
  'mail.common.clearTrashFailed': '清空失败',
  'mail.common.saveFailed': '保存失败',
  'mail.common.loadFailed': '加载失败',
  'mail.common.operationFailed': '操作失败',

  // ════════════════════════════════════════════════════════════════
  // mailboxes —— pages/Mailboxes.tsx + context/MailboxContext.tsx
  // ════════════════════════════════════════════════════════════════

// ════════ page —— 页头、页级动作、统计卡 ════════
  // 队长在 context/MailboxContext.tsx 引用的"未指定邮箱"哨兵值
  'mail.mailboxes.allMailboxes': '全部邮箱',
  'mail.mailboxes.tabAll': '所有邮箱',
  'mail.mailboxes.page.title': '邮箱管理',
  'mail.mailboxes.page.badge': '邮件中心',
  'mail.mailboxes.page.subtitle': '管理收发邮件的支持邮箱账户，配置 IMAP/SMTP 服务器、自动回复、OAuth 认证',
  'mail.mailboxes.page.refreshTitle': '刷新邮箱列表',
  'mail.mailboxes.refresh': '刷新',
  'mail.mailboxes.newMailbox': '新建邮箱',
  'mail.mailboxes.editMailbox': '编辑邮箱',
  'mail.mailboxes.requestFailed': '请求失败',

  'mail.mailboxes.stat.total': '邮箱总数',
  'mail.mailboxes.stat.totalHint': '所有已配置邮箱',
  'mail.mailboxes.stat.active': '活跃邮箱',
  'mail.mailboxes.stat.activeHint': '禁用 {count} 个',
  'mail.mailboxes.stat.conversations': '会话总数',
  'mail.mailboxes.stat.conversationsHint': '全部邮箱累计',
  'mail.mailboxes.stat.todaySent': '今日发送',
  'mail.mailboxes.stat.todaySentHint': '今日累计发送邮件',
  'mail.mailboxes.stat.totalConversations': '总会话',
  'mail.mailboxes.stat.open': '进行中',

  // ════════ page —— 筛选条与搜索 ════════
  'mail.mailboxes.filter.label': '筛选',
  'mail.mailboxes.filter.statusAll': '全部',
  'mail.mailboxes.filter.statusActive': '状态：活跃',
  'mail.mailboxes.filter.statusDisabled': '状态：禁用',
  'mail.mailboxes.searchPlaceholder': '搜索邮箱名称或地址...',
  // 后接被强调的数字（值保留在 <b> 内），故只取前缀
  'mail.mailboxes.countPrefix': '共',

  // ════════ list —— 列表、空态、卡片 ════════
  'mail.mailboxes.loadingList': '加载邮箱列表...',
  'mail.mailboxes.empty.noMatch': '没有匹配的邮箱',
  'mail.mailboxes.empty.noMatchHint': '尝试调整搜索关键词或筛选条件',
  'mail.mailboxes.empty.noMailboxes': '暂无邮箱',
  'mail.mailboxes.empty.noMailboxesHint': '点击右上角「新建邮箱」开始配置',
  'mail.mailboxes.empty.trashHint': '已删除的邮箱将出现在此处',
  'mail.mailboxes.clearFilters': '清除筛选',
  'mail.mailboxes.testConnection': '测试连接',
  'mail.mailboxes.testSuccess': '连接成功',
  'mail.mailboxes.testFailed': '连接失败',
  'mail.mailboxes.notConfigured': '未配置',
  'mail.mailboxes.fetchEvery': '每 {count} 分钟',
  'mail.mailboxes.lastFetchAt': '上次：{time}',
  'mail.mailboxes.authorizedUsersTitle': '{count} 个授权用户',

  // ════════ form —— 新建 / 编辑弹窗 ════════
  'mail.mailboxes.form.editTitle': '编辑邮箱配置',
  'mail.mailboxes.form.createTitle': '创建新的邮箱账户',
  'mail.mailboxes.form.editSubtitle': '修改邮箱基本信息、服务器配置、自动回复和授权用户',
  'mail.mailboxes.form.createSubtitle': '配置 IMAP/SMTP 服务器、自动回复、OAuth 认证等',
  'mail.mailboxes.form.saving': '保存中...',
  'mail.mailboxes.form.basicInfo': '基本信息',
  'mail.mailboxes.form.basicInfoHint': '邮箱名称、地址和启用状态',
  'mail.mailboxes.form.name': '名称',
  'mail.mailboxes.form.namePlaceholder': '如：技术支持',
  'mail.mailboxes.form.email': '邮箱地址',
  'mail.mailboxes.form.enableMailbox': '启用此邮箱',
  'mail.mailboxes.form.serverHost': '服务器地址',
  'mail.mailboxes.form.port': '端口',
  'mail.mailboxes.form.encryption': '加密方式',
  'mail.mailboxes.form.username': '用户名',
  'mail.mailboxes.form.password': '密码',
  'mail.mailboxes.form.alreadySet': '已设置',
  'mail.mailboxes.form.passwordPlaceholder': '请输入邮箱密码或授权码',
  'mail.mailboxes.form.passwordKeepPlaceholder': '留空表示不修改原密码',
  'mail.mailboxes.form.authCodeHint': '部分邮箱（如 QQ、163、Gmail）需使用授权码而非登录密码',
  'mail.mailboxes.form.validateCert': '验证 SSL 证书',
  'mail.mailboxes.form.sentFolder': 'IMAP 发送文件夹',
  'mail.mailboxes.form.fetchFrequency': '邮件收取频率',
  'mail.mailboxes.form.minutesOption': '{count} 分钟',

  // ════════ form —— 收件 / 发件服务器分区 ════════
  'mail.mailboxes.form.inboundTitle': '收件服务器 (IMAP)',
  'mail.mailboxes.form.inboundHint': '接收邮件的服务器配置',
  'mail.mailboxes.form.outboundTitle': '发件服务器 (SMTP)',
  'mail.mailboxes.form.outboundHint': '发送邮件的服务器配置',

  // ════════ form —— 别名与抄送 ════════
  'mail.mailboxes.form.aliasesTitle': '别名和抄送',
  'mail.mailboxes.form.aliasesHint': '配置邮箱别名和自动密送',
  'mail.mailboxes.form.alias': '别名',
  'mail.mailboxes.form.aliasReply': '别名回复地址',
  'mail.mailboxes.form.autoBcc': '自动密送',

  // ════════ form —— 自动回复 ════════
  'mail.mailboxes.form.autoReplyTitle': '自动回复',
  'mail.mailboxes.form.autoReplyHint': '收到邮件时自动回复的内容',
  'mail.mailboxes.form.autoReplyEnabled': '启用自动回复',
  'mail.mailboxes.form.autoReplySubject': '回复主题',
  'mail.mailboxes.form.autoReplySubjectPlaceholder': '自动回复：收到您的邮件',
  'mail.mailboxes.form.autoReplyMessage': '回复内容',
  'mail.mailboxes.form.autoReplyMessagePlaceholder': '感谢您的来信，我们会尽快回复...',

  // ════════ form —— 签名 ════════
  'mail.mailboxes.form.signatureTitle': '签名设置',
  'mail.mailboxes.form.signatureHint': '回复前文本和邮件签名',
  'mail.mailboxes.form.beforeReply': '回复前文本',
  'mail.mailboxes.form.beforeReplyPlaceholder': '在回复内容前显示的文本',
  'mail.mailboxes.form.signature': '签名',
  'mail.mailboxes.form.signaturePlaceholder': '--\n公司名称',

  // ════════ form —— 授权访问用户 ════════
  'mail.mailboxes.form.authorizedUsersTitle': '授权访问用户',
  'mail.mailboxes.form.authorizedUsersHint': '可访问此邮箱的客服用户列表',
  'mail.mailboxes.form.selectedUsers': '已选择 {count} 个授权用户',

  // ════════ encryption —— 按码取值（前端绑定） ════════
  'mail.mailboxes.encryption.none': '无加密',
  'mail.mailboxes.encryption.ssl': 'SSL',
  'mail.mailboxes.encryption.tls': 'TLS',

  // ════════ oauth ════════
  'mail.mailboxes.oauth.title': 'OAuth 认证',
  'mail.mailboxes.oauth.hint': '使用 OAuth 进行邮箱认证（如 Gmail、Outlook）',
  'mail.mailboxes.oauth.enabled': '启用 OAuth 认证',
  'mail.mailboxes.oauth.provider': 'OAuth 提供商',
  'mail.mailboxes.oauth.selectPlaceholder': '请选择',
  'mail.mailboxes.oauth.other': '其他',
  'mail.mailboxes.oauth.keepPlaceholder': '留空表示不修改',

  // ════════ trash —— 垃圾箱相关确认弹窗 ════════
  'mail.mailboxes.delete.title': '删除邮箱',
  'mail.mailboxes.delete.message': '确认删除邮箱「{name}」({email}) 吗？删除后将移入垃圾箱，可在垃圾箱中恢复。',
  'mail.mailboxes.restore.title': '恢复邮箱',
  'mail.mailboxes.restore.message': '确认恢复邮箱「{name}」({email}) 吗？恢复后将重新出现在邮箱列表中。',
  'mail.mailboxes.hardDelete.title': '彻底删除邮箱',
  'mail.mailboxes.hardDelete.message': '确认彻底删除邮箱「{name}」({email}) 吗？此操作不可撤销，关联数据将永久丢失。',
  'mail.mailboxes.clearTrash.message': '确认清空垃圾箱中的 {count} 个邮箱吗？此操作不可撤销，所有已删除邮箱将永久丢失。',
  'mail.mailboxes.clearTrash.confirm': '清空',

  // ════════ validation ════════
  'mail.mailboxes.validation.nameRequired': '邮箱名称不能为空',
  'mail.mailboxes.validation.emailRequired': '邮箱地址不能为空',
  'mail.mailboxes.validation.hostRequired': '收发件服务器地址不能为空',

  // ════════════════════════════════════════════════════════════════
  // templates —— pages/MailTemplates.tsx + components/MailTemplateTrashModal.tsx
  // ════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════
  // page —— 页头 / 统计 / 概览 / 筛选栏
  // ════════════════════════════════════════════════════════════════
  'mail.templates.page.title': '邮件模板（Mail Templates）',
  'mail.templates.page.badge': '邮件中心',
  'mail.templates.page.subtitle':
    '管理系统各类邮件通知模板，支持自定义变量、HTML 富文本编辑与实时预览',
  'mail.templates.page.create': '新建模板',
  'mail.templates.page.tabList': '模板列表',
  'mail.templates.page.loadingOverview': '正在加载概览数据...',
  'mail.templates.page.byType': '按类型分布',
  'mail.templates.page.totalTemplates': '共 {count} 个模板',
  'mail.templates.page.searchPlaceholder': '搜索模板名称...',
  'mail.templates.page.allTypes': '全部类型',
  'mail.templates.page.statusEnabled': '已启用',
  'mail.templates.page.statusDisabled': '已禁用',
  // 概览统计卡：label 与 hint 都是用户可见文案，均只持有 key
  'mail.templates.page.statsTotal': '模板总数',
  'mail.templates.page.statsTotalHint': '所有邮件模板',
  'mail.templates.page.statsActiveHint': '当前生效中',
  'mail.templates.page.statsSystem': '系统模板',
  'mail.templates.page.statsSystemHint': '内置不可删',
  'mail.templates.page.statsCustom': '自定义模板',
  'mail.templates.page.statsCustomHint': '用户创建',

  // ════════════════════════════════════════════════════════════════
  // list —— 空态 / 表头 / 分页
  // ════════════════════════════════════════════════════════════════
  'mail.templates.list.emptyFilteredTitle': '未找到匹配的模板',
  'mail.templates.list.emptyTitle': '暂无邮件模板',
  'mail.templates.list.emptyFilteredSubtitle': '尝试换个关键词或清除筛选条件',
  'mail.templates.list.emptySubtitle': '点击右上角「新建模板」，快速创建第一个邮件模板吧',
  'mail.templates.list.createFirst': '创建第一个模板',
  'mail.templates.list.colName': '模板名称',
  'mail.templates.list.colSlug': '标识',
  'mail.templates.list.colSource': '来源',
  'mail.templates.list.systemNotDeletable': '系统模板不可删除',
  // 分页：`footerTotalPrefix` / `footerPagePrefix` / `footerPageSuffix` 夹住
  // `<span className="font-bold …">{totalItems}</span>` 与 `{page}`——值留在自己的元素里
  'mail.templates.list.footerTotalPrefix': '共 ',
  'mail.templates.list.footerPagePrefix': ' 条，第 ',
  'mail.templates.list.footerPageSuffix': '/{total} 页',
  'mail.templates.list.prevPage': '上一页',
  'mail.templates.list.nextPage': '下一页',

  // ════════════════════════════════════════════════════════════════
  // editor —— 新建 / 编辑弹窗
  // ════════════════════════════════════════════════════════════════
  'mail.templates.editor.titleEdit': '编辑模板',
  'mail.templates.editor.titleCreate': '新建模板',
  'mail.templates.editor.editing': '正在编辑：{name}',
  'mail.templates.editor.createHint': '填写以下信息创建新的邮件模板',
  'mail.templates.editor.basicInfo': '基本信息',
  'mail.templates.editor.nameLabel': '模板名称',
  'mail.templates.editor.namePlaceholder': '例如：新会话通知邮件',
  'mail.templates.editor.slugLabel': '模板标识 (slug)',
  // slug 示例值是技术标识符（snake_case），保持可读字面量，只译「例如：」前缀
  'mail.templates.editor.slugPlaceholder': '例如：new_conversation_notification',
  'mail.templates.editor.typeLabel': '模板类型',
  'mail.templates.editor.subjectLabel': '邮件主题',
  'mail.templates.editor.subjectPlaceholder': '例如：[新会话] {{subject}}',
  'mail.templates.editor.descriptionLabel': '描述',
  'mail.templates.editor.descriptionPlaceholder': '可选，模板用途说明',
  'mail.templates.editor.modeHtml': 'HTML 格式',
  'mail.templates.editor.modeText': '纯文本',
  'mail.templates.editor.preview': '预览效果',
  'mail.templates.editor.htmlPlaceholder':
    '<html><body>...在这里编写 HTML 模板内容...</body></html>',
  'mail.templates.editor.textPlaceholder': '...在这里编写纯文本模板内容...',
  'mail.templates.editor.availableVariables': '可用变量',
  'mail.templates.editor.variableCount': '{count} 个',
  'mail.templates.editor.clickToInsert': '点击变量插入到编辑器',
  'mail.templates.editor.enableTemplate': '启用此模板',
  'mail.templates.editor.enableHint': '关闭后模板将不会生效',
  'mail.templates.editor.sortOrderLabel': '排序：',
  'mail.templates.editor.saving': '保存中...',

  // ════════════════════════════════════════════════════════════════
  // preview —— 预览弹窗（含示例数据）
  // ════════════════════════════════════════════════════════════════
  'mail.templates.preview.title': '模板预览',
  'mail.templates.preview.subtitle': '渲染效果展示（使用示例数据）',
  'mail.templates.preview.subject': '邮件主题',
  'mail.templates.preview.html': 'HTML 预览',
  'mail.templates.preview.raw': '原文',
  // 示例变量值同样是界面可见文案（会渲染进预览），故进字典
  'mail.templates.preview.sampleSubject': '测试工单 - 系统登录问题',
  'mail.templates.preview.sampleCustomerName': '测试客户',
  'mail.templates.preview.sampleMailboxName': '支持邮箱',
  'mail.templates.preview.sampleMessageBody': '<p>您好，我无法登录系统，提示密码错误。</p>',

  // ════════════════════════════════════════════════════════════════
  // delete —— 删除确认框
  // ════════════════════════════════════════════════════════════════
  'mail.templates.delete.title': '删除邮件模板',
  'mail.templates.delete.message':
    '确认删除模板「{name}」吗？模板将被移至垃圾箱，可在垃圾箱中恢复或彻底删除。',

  // ════════════════════════════════════════════════════════════════
  // toast —— 操作提示
  // ════════════════════════════════════════════════════════════════
  'mail.templates.toast.restored': '模板「{name}」已恢复',
  'mail.templates.toast.hardDeleted': '模板「{name}」已彻底删除',
  'mail.templates.toast.requiredFields': '请填写模板名称、标识和主题',
  'mail.templates.toast.updated': '模板已更新',
  'mail.templates.toast.created': '模板已创建',
  'mail.templates.toast.movedToTrash': '模板「{name}」已移至垃圾箱',
  'mail.templates.toast.previewFailed': '预览失败',

  // ════════════════════════════════════════════════════════════════
  // trash —— 垃圾箱弹窗
  // ════════════════════════════════════════════════════════════════
  'mail.templates.trash.title': '垃圾箱（邮件模板）',
  'mail.templates.trash.heading': '已删除的邮件模板',
  // 「…，可 <b>恢复</b> 或 <b>彻底删除</b> 。 共 <b>N</b> 条…」：
  // 保留 <b> 原样，值不进译串——前缀/后缀拆键，中间与句号复用冻结的 mail.common.* 共享键
  'mail.templates.trash.retentionPrefix': '已删除的模板会保留在回收站，可',
  'mail.templates.trash.pendingSuffix': ' 条已删除模板待处理',
  'mail.templates.trash.searchPlaceholder': '搜索已删除模板（名称 / 标识 / 主题）',
  'mail.templates.trash.clearAll': '清空全部',
  'mail.templates.trash.colTemplate': '模板',
  'mail.templates.trash.emptyFiltered': '未找到匹配的已删除模板',
  'mail.templates.trash.emptyHint': '已删除的邮件模板会显示在这里',
  'mail.templates.trash.unknownType': '类型 {type}',
  'mail.templates.trash.inactive': '已停用',
  'mail.templates.trash.restoreTooltip': '恢复模板',
  'mail.templates.trash.restoreTitle': '恢复邮件模板',
  'mail.templates.trash.restoreMessage':
    '确认恢复模板「{name}」到模板列表？恢复后可在邮件模板列表中正常使用。',
  'mail.templates.trash.hardDeleteMessage': '模板「{name}」将被永久删除且不可恢复，确认继续？',
  'mail.templates.trash.clearMessage':
    '共 {count} 条模板将被永久删除且不可恢复，确认清空整个垃圾箱？',

  // ════════════════════════════════════════════════════════════════
  // type —— 模板类型（Domain/Enums/MailTemplateType.cs 全部 10 码）
  // ════════════════════════════════════════════════════════════════
  // 后端 GetTypeName 只给 8 个中文标签，CalendarReminder(8) / ChatSessionClosed(10)
  // 走 `_ => type.ToString()`，前端补全这两码后界面不再出现原始标识符
  'mail.templates.type.newConversation': '新会话通知',
  'mail.templates.type.newReply': '新回复通知',
  'mail.templates.type.conversationAssigned': '会话分配通知',
  'mail.templates.type.conversationClosed': '会话关闭通知',
  'mail.templates.type.satisfactionSurvey': '满意度调查',
  'mail.templates.type.passwordReset': '密码重置',
  'mail.templates.type.welcome': '欢迎邮件',
  'mail.templates.type.calendarReminder': '日历提醒',
  'mail.templates.type.chatSessionClosed': '聊天会话关闭通知',
  'mail.templates.type.custom': '自定义模板',

  // ════════════════════════════════════════════════════════════════
  // typeCategory —— 模板类型分类（仅用于同构分组，界面未渲染该字段）
  // ════════════════════════════════════════════════════════════════
  'mail.templates.typeCategory.notification': '通知',
  'mail.templates.typeCategory.survey': '调查',
  'mail.templates.typeCategory.system': '系统',
  'mail.templates.typeCategory.custom': '自定义',

  // ════════════════════════════════════════════════════════════════
  // variable —— 模板变量标签（Application/Templates/TemplateVariableRegistry.cs）
  // 中文取后端 `Label` 原文；key 为后端规范名
  // ════════════════════════════════════════════════════════════════
  // 会话
  'mail.templates.variable.subject': '会话主题',
  'mail.templates.variable.conversationId': '会话ID',
  'mail.templates.variable.conversationUrl': '会话链接',
  'mail.templates.variable.priority': '优先级',
  'mail.templates.variable.updateContent': '更新内容',
  // 客户
  'mail.templates.variable.customerName': '客户姓名',
  'mail.templates.variable.customerEmail': '客户邮箱',
  'mail.templates.variable.customerPhone': '客户电话',
  // 用户
  'mail.templates.variable.userName': '客服姓名',
  'mail.templates.variable.userEmail': '客服邮箱',
  'mail.templates.variable.userRole': '客服角色',
  // 邮箱
  'mail.templates.variable.mailboxName': '邮箱名称',
  'mail.templates.variable.mailboxEmail': '邮箱地址',
  // 消息
  'mail.templates.variable.messageBody': '最新消息内容',
  'mail.templates.variable.firstMessageBody': '首条消息内容',
  // 角色
  'mail.templates.variable.assigneeName': '受让人姓名',
  'mail.templates.variable.assignerName': '分配人姓名',
  'mail.templates.variable.replierName': '回复人姓名',
  'mail.templates.variable.closerName': '关闭人姓名',
  'mail.templates.variable.updaterName': '更新人姓名',
  'mail.templates.variable.mentionerName': '提及人姓名',
  'mail.templates.variable.escalatedBy': '升级人',
  // SLA
  'mail.templates.variable.slaName': 'SLA策略名称',
  'mail.templates.variable.slaLevel': 'SLA优先级',
  'mail.templates.variable.slaRemaining': 'SLA剩余时间',
  'mail.templates.variable.slaDeadline': 'SLA截止时间',
  'mail.templates.variable.slaResponseTime': 'SLA响应时长',
  'mail.templates.variable.remainingTime': '剩余时间',
  'mail.templates.variable.violationType': '违规类型',
  // 满意度
  'mail.templates.variable.satisfactionUrl': '满意度评价链接',
  'mail.templates.variable.satisfactionRating': '满意度评级',
  'mail.templates.variable.satisfactionRatingValue': '满意度分值',
  'mail.templates.variable.satisfactionComment': '满意度评论',
  'mail.templates.variable.ratingUrl': '评分链接',
  'mail.templates.variable.feedbackUrl': '反馈链接',
  'mail.templates.variable.ratingStars': '评分星星',
  'mail.templates.variable.ratingLabel': '评分标签',
  'mail.templates.variable.ratingComment': '评分评论',
  // 升级
  'mail.templates.variable.escalationReason': '升级原因',
  'mail.templates.variable.escalatedAt': '升级时间',
  // 聊天
  'mail.templates.variable.chatSessionId': '聊天会话ID',
  'mail.templates.variable.chatCreatedAt': '聊天创建时间',
  'mail.templates.variable.agentName': '接待客服',
  'mail.templates.variable.chatSubject': '聊天主题',
  // 日历
  'mail.templates.variable.eventTitle': '事件标题',
  'mail.templates.variable.eventType': '事件类型',
  'mail.templates.variable.eventStartTime': '开始时间',
  'mail.templates.variable.eventEndTime': '结束时间',
  'mail.templates.variable.eventLocation': '事件地点',
  'mail.templates.variable.eventMeetingUrl': '会议链接',
  'mail.templates.variable.eventDescription': '事件描述',
  'mail.templates.variable.eventPriority': '事件优先级',
  'mail.templates.variable.eventUrl': '事件链接',
  // 系统
  'mail.templates.variable.createdAt': '创建时间',
  'mail.templates.variable.receivedTime': '接收时间',
  'mail.templates.variable.lastReplyTime': '最后回复时间',
  'mail.templates.variable.waitingTime': '等待时长',
  'mail.templates.variable.timeoutMinutes': '超时分钟数',
  'mail.templates.variable.expireTime': '过期时间',
  'mail.templates.variable.companyName': '公司名称',
  'mail.templates.variable.loginUrl': '登录链接',
  'mail.templates.variable.resetPasswordUrl': '重置密码链接',
  'mail.templates.variable.activationUrl': '激活链接',
  // 导入
  'mail.templates.variable.totalCount': '总记录数',
  'mail.templates.variable.successCount': '成功数',
  'mail.templates.variable.failedCount': '失败数',
  'mail.templates.variable.errorReportUrl': '错误报告链接',

  // ════════════════════════════════════════════════════════════════
  // variableCategory —— 变量分类（13 个后端分类 + 1 个防御性分组）
  // ════════════════════════════════════════════════════════════════
  'mail.templates.variableCategory.conversation': '会话',
  'mail.templates.variableCategory.customer': '客户',
  'mail.templates.variableCategory.user': '用户',
  'mail.templates.variableCategory.mailbox': '邮箱',
  'mail.templates.variableCategory.message': '消息',
  'mail.templates.variableCategory.people': '角色',
  'mail.templates.variableCategory.sla': 'SLA',
  'mail.templates.variableCategory.satisfaction': '满意度',
  'mail.templates.variableCategory.escalation': '升级',
  'mail.templates.variableCategory.chat': '聊天',
  'mail.templates.variableCategory.calendar': '日历',
  'mail.templates.variableCategory.system': '系统',
  'mail.templates.variableCategory.import': '导入',
  // 防御性：后端新增变量而前端字典尚未覆盖时，回落原始 key 并归入本组（见 MailTemplates.tsx）
  'mail.templates.variableCategory.other': '其他',

  // ════════════════════════════════════════════════════════════════
  // signatures —— pages/EmailSignatures.tsx + components/EmailSignatureTrashModal.tsx
  // ════════════════════════════════════════════════════════════════

// ════════ 页头 / Tab（EmailSignatures.tsx） ════════
  'mail.signatures.page.title': '邮件签名（Email Signatures）',
  'mail.signatures.page.badge': '邮件中心',
  'mail.signatures.page.subtitle': '管理用户邮件签名，支持按邮箱和用户配置默认签名，支持 HTML 富文本',
  'mail.signatures.page.new': '新建签名',
  'mail.signatures.page.tabsList': '签名列表',
  'mail.signatures.page.loadingOverview': '加载概览数据...',
  'mail.signatures.page.loadingList': '加载签名列表...',

  // ════════ 概览统计卡（labelKey/hintKey 只持有真源 key） ════════
  'mail.signatures.stats.total': '签名总数',
  'mail.signatures.stats.totalHint': '全部签名记录',
  'mail.signatures.stats.default': '默认签名',
  'mail.signatures.stats.defaultHint': '已设为默认的签名',
  'mail.signatures.stats.mailbox': '邮箱专属',
  'mail.signatures.stats.mailboxHint': '绑定特定邮箱',
  'mail.signatures.stats.global': '全局签名',
  'mail.signatures.stats.globalHint': '所有邮箱通用',

  // ════════ 概览 Tab ════════
  'mail.signatures.overview.sectionTitle': '常用签名',
  'mail.signatures.overview.viewAll': '查看全部',
  'mail.signatures.overview.emptyTitle': '暂无签名数据',
  'mail.signatures.overview.emptySubtitle': '点击下方按钮创建第一个邮件签名',

  // ════════ 徽章（默认 / 曾默认；「全局」用 mail.common.global） ════════
  'mail.signatures.badge.default': '默认',

  // ════════ 列表页筛选与表头 ════════
  'mail.signatures.list.filterTitle': '筛选条件',
  'mail.signatures.list.searchPlaceholder': '搜索签名名称...',
  'mail.signatures.filter.allMailboxes': '全部邮箱',
  'mail.signatures.filter.allUsers': '全部用户',
  'mail.signatures.filter.onlyDefault': '仅默认',
  'mail.signatures.filter.nonDefault': '非默认',
  // 筛选胶囊的**文案前缀**：动态值留在独立文本节点里，不进译文串
  'mail.signatures.filter.keywordPrefix': '关键字：',
  'mail.signatures.filter.mailboxPrefix': '邮箱：',
  'mail.signatures.filter.userPrefix': '用户：',
  'mail.signatures.list.appliedColon': '已应用：',
  'mail.signatures.field.name': '签名名称',
  'mail.signatures.list.scopeColumn': '适用范围',
  'mail.signatures.field.sortOrder': '排序',
  'mail.signatures.field.createdAt': '创建时间',
  'mail.signatures.list.statusDefault': '默认签名',
  'mail.signatures.list.statusNormal': '普通',
  'mail.signatures.list.setDefault': '设为默认',
  // 列表分页：「共 N 条」的收尾（前缀 mail.common.trashFooterTotalPrefix，范围三段用 mail.common.trashRange*）
  'mail.signatures.list.totalSuffix': ' 条',

  // ════════ 编辑器 Modal ════════
  'mail.signatures.editor.titleEdit': '编辑签名',
  'mail.signatures.editor.titleCreate': '新建签名',
  'mail.signatures.editor.headerEdit': '编辑邮件签名',
  'mail.signatures.editor.headerCreate': '创建邮件签名',
  'mail.signatures.editor.badgeEdit': '修改',
  'mail.signatures.editor.badgeCreate': '新建',
  // 说明句：加粗片段留在自己的 <b> 里
  'mail.signatures.editor.headerHintPrefix': '支持 ',
  'mail.signatures.editor.headerHintBold': 'HTML 富文本',
  'mail.signatures.editor.headerHintSuffix': '，可设置默认签名与适用邮箱范围',
  'mail.signatures.editor.namePlaceholder': '请输入签名名称',
  'mail.signatures.editor.defaultStatusLabel': '默认状态',
  'mail.signatures.editor.enableDefault': '设为默认签名',
  'mail.signatures.editor.keepNormal': '普通签名',
  'mail.signatures.editor.bodyLabel': '签名内容',
  'mail.signatures.editor.bodyFormatHint': '支持 HTML 格式',
  'mail.signatures.editor.bodyPlaceholder': '请输入签名内容，支持 HTML 格式，例如 <br>、<b>、<a> 等',
  'mail.signatures.editor.previewTitle': '实时预览',
  'mail.signatures.editor.previewBodySample': '邮件正文示例内容...',
  'mail.signatures.editor.previewEmpty': '签名预览将显示在这里',
  'mail.signatures.editor.saving': '保存中...',

  // ════════ 预览 Modal ════════
  'mail.signatures.preview.title': '签名预览',
  'mail.signatures.preview.windowTitle': '邮件预览窗口',
  'mail.signatures.preview.greeting': '尊敬的客户，您好！',
  'mail.signatures.preview.paragraph1': '感谢您的咨询，以下是我们的回复内容...',
  'mail.signatures.preview.paragraph2': '如有任何问题，请随时联系我们。',

  // ════════ 校验与确认 ════════
  'mail.signatures.form.nameRequired': '请输入签名名称',
  'mail.signatures.form.bodyRequired': '请输入签名内容',
  'mail.signatures.delete.title': '删除确认',
  'mail.signatures.delete.message': '签名「{name}」将被移至垃圾箱，可在垃圾箱中恢复或彻底删除。',

  // ════════ 提示（失败类复用 mail.common.*） ════════
  'mail.signatures.toast.restored': '签名「{name}」已恢复',
  'mail.signatures.toast.hardDeleted': '签名「{name}」已彻底删除',
  'mail.signatures.toast.updated': '签名已更新',
  'mail.signatures.toast.created': '签名已创建',
  'mail.signatures.toast.movedToTrash': '签名已移至垃圾箱',
  'mail.signatures.toast.setDefault': '已设为默认签名',
  'mail.signatures.toast.setDefaultFailed': '设置失败',

  // ════════ 垃圾箱弹窗（EmailSignatureTrashModal.tsx） ════════
  'mail.signatures.trash.title': '垃圾箱（邮件签名）',
  'mail.signatures.trash.headerTitle': '已删除的邮件签名',
  // 说明句：前缀 + <b>恢复</b> + mail.common.trashRetentionMiddle + <b>彻底删除</b> + mail.common.trashRetentionSuffix
  'mail.signatures.trash.retentionPrefix': '已删除的签名会保留在回收站，可',
  // 「共 <b>N</b> 条已删除签名待处理」的收尾（前缀用 mail.common.trashFooterTotalPrefix）
  'mail.signatures.trash.pendingSuffix': ' 条已删除签名待处理',
  'mail.signatures.trash.searchPlaceholder': '搜索已删除签名（名称 / 用户 / 邮箱）',
  'mail.signatures.trash.clearAll': '清空全部',
  'mail.signatures.trash.columns.signature': '签名',
  'mail.signatures.trash.noMatch': '未找到匹配的已删除签名',
  'mail.signatures.trash.emptyHint': '已删除的邮件签名会显示在这里',
  'mail.signatures.trash.wasDefault': '曾默认',
  'mail.signatures.trash.restoreTooltip': '恢复签名',
  'mail.signatures.trash.userNumber': '用户 #{id}',
  'mail.signatures.trash.restoreTitle': '恢复邮件签名',
  'mail.signatures.trash.restoreMessage': '确认恢复签名「{name}」到签名列表？恢复后可在邮件签名列表中正常使用。',
  'mail.signatures.trash.hardDeleteMessage': '签名「{name}」将被永久删除且不可恢复，确认继续？',
  'mail.signatures.trash.clearMessage': '共 {count} 条签名将被永久删除且不可恢复，确认清空整个垃圾箱？',

  // ════════════════════════════════════════════════════════════════
  // folders —— pages/Folders.tsx
  // ════════════════════════════════════════════════════════════════

// ════════ 文件夹类型 ════════
  'mail.folders.type.unassigned': '未分配',
  'mail.folders.type.mine': '我的',
  'mail.folders.type.starred': '星标',
  'mail.folders.type.drafts': '草稿',
  'mail.folders.type.assigned': '已分配',
  'mail.folders.type.closed': '已关闭',
  'mail.folders.type.deleted': '已删除',
  'mail.folders.type.spam': '垃圾邮件',
  'mail.folders.type.custom': '自定义文件夹',
  'mail.folders.type.customWithCode': '自定义 ({code})',
  'mail.folders.type.systemLocked': '系统文件夹（不可删除）',

  // ════════ 页头 ════════
  'mail.folders.page.title': '文件夹管理',
  'mail.folders.page.badge': '邮件中心',
  'mail.folders.page.subtitle': '管理邮箱的会话文件夹，系统文件夹自动创建，可添加自定义文件夹',
  'mail.folders.page.ensureSystemTooltip': '为当前邮箱补齐所有系统文件夹',
  'mail.folders.page.ensureSystem': '补齐系统文件夹',
  'mail.folders.page.refreshCountersTooltip': '重新计算文件夹的活跃/总数',
  'mail.folders.page.refreshCounters': '刷新计数',
  'mail.folders.page.create': '新建文件夹',
  'mail.folders.page.statSystemLabel': '系统文件夹',
  'mail.folders.page.statSystemHint': '后端自动创建',
  'mail.folders.page.statCustomLabel': '自定义文件夹',
  'mail.folders.page.statCustomHint': '用户手动添加',
  'mail.folders.page.statActiveLabel': '活跃会话',
  'mail.folders.page.statActiveHint': '未关闭的会话',
  'mail.folders.page.statTotalLabel': '会话总数',
  'mail.folders.page.statTotalHint': '全部文件夹累计',

  // ════════ 列表 ════════
  'mail.folders.list.mailbox': '邮箱',
  'mail.folders.list.loading': '加载文件夹...',
  'mail.folders.list.emptyNoFolders': '该邮箱暂无文件夹',
  'mail.folders.list.emptyNoMailbox': '请先选择一个邮箱',
  'mail.folders.list.emptyNoFoldersHint': '可点击下方按钮补齐系统文件夹',
  'mail.folders.list.emptyNoMailboxHint': '在上方选择一个邮箱以查看文件夹',
  'mail.folders.list.emptyNoMatch': '没有匹配的文件夹',
  'mail.folders.list.emptyNoMatchHint': '尝试调整搜索关键词或筛选条件',
  'mail.folders.list.clearFilters': '清除筛选',
  'mail.folders.list.selectAllTooltip': '全选/取消全选自定义文件夹',
  'mail.folders.list.columnName': '文件夹名称',
  'mail.folders.list.columnActive': '活跃数',
  'mail.folders.list.columnTotal': '总数',
  'mail.folders.list.columnUpdated': '更新时间',
  'mail.folders.list.systemDisabledTooltip': '系统文件夹不可选',
  'mail.folders.list.selectTooltip': '选择此文件夹',
  'mail.folders.list.activeBadge': '活跃 {count}',
  'mail.folders.list.totalBadge': '总数 {count}',

  // ════════ 搜索与筛选 ════════
  'mail.folders.filter.label': '筛选',
  'mail.folders.filter.searchPlaceholder': '搜索文件夹名称或类型...',
  'mail.folders.filter.all': '全部',
  'mail.folders.filter.typeSystem': '类型：系统',
  'mail.folders.filter.typeCustom': '类型：自定义',
  'mail.folders.filter.batchDelete': '批量删除 ({count})',
  'mail.folders.filter.totalPrefix': '共',

  // ════════ 新建 / 编辑弹窗 ════════
  'mail.folders.form.titleEdit': '编辑文件夹',
  'mail.folders.form.editHeading': '编辑文件夹信息',
  'mail.folders.form.createHeading': '创建新的自定义文件夹',
  'mail.folders.form.editHint': '可修改文件夹名称，类型不可更改',
  'mail.folders.form.createHint': '自定义文件夹可用于分类管理会话',
  'mail.folders.form.saving': '保存中...',
  'mail.folders.form.errorRequired': '邮箱和文件夹名称不能为空',
  'mail.folders.form.mailboxPlaceholder': '请选择邮箱',
  'mail.folders.form.nameLabel': '文件夹名称',
  'mail.folders.form.namePlaceholder': '如：待处理、紧急工单、VIP客户',
  'mail.folders.form.nameHint': '最多 100 个字符',
  'mail.folders.form.typeLabel': '文件夹类型',
  'mail.folders.form.typeLockedHint': '文件夹类型不支持修改',
  'mail.folders.form.typeCreateHint': '系统文件夹由后端自动创建，此处仅支持创建自定义文件夹',

  // ════════ 删除确认 ════════
  'mail.folders.delete.title': '删除文件夹',
  'mail.folders.delete.message': '确定要删除文件夹「{name}」吗？该文件夹下的 {count} 个会话将不再关联此文件夹，此操作不可撤销。',
  'mail.folders.delete.batchTitle': '批量删除文件夹',
  'mail.folders.delete.batchMessage': '确定要批量删除选中的 {count} 个文件夹吗？此操作不可撤销。',
  'mail.folders.delete.batchConfirm': '删除 {count} 个',

  // ════════ 操作结果提示 ════════
  'mail.folders.toast.updated': '文件夹「{name}」已更新',
  'mail.folders.toast.created': '文件夹「{name}」已创建',
  'mail.folders.toast.deleted': '文件夹「{name}」已删除',
  'mail.folders.toast.countersRefreshed': '已刷新 {count} 个文件夹的计数',
  'mail.folders.toast.refreshFailed': '刷新失败',
  'mail.folders.toast.systemEnsured': '已为邮箱补齐 {count} 个系统文件夹',
  'mail.folders.toast.systemAlreadyComplete': '系统文件夹已完整，无需补齐',
  'mail.folders.toast.ensureFailed': '补齐失败',
  'mail.folders.toast.batchDeleted': '已批量删除 {count} 个文件夹',
  'mail.folders.toast.batchDeleteFailedSuffix': '，{count} 个失败',
  'mail.folders.toast.batchDeleteFailed': '批量删除失败（{count} 个）',

  // ════════════════════════════════════════════════════════════════
  // canned —— pages/CannedResponses.tsx + components/CannedResponseTrashModal.tsx
  // ════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════
  // page —— 页头与工具条
  // ════════════════════════════════════════════════════════════════
  'mail.canned.page.title': '快捷回复（Canned Replies）',
  'mail.canned.page.badge': '邮件中心',
  'mail.canned.page.subtitle':
    '管理客服回复模板，分类分组，支持 HTML 富文本与共享设置，全面提升回复效率与一致性',
  'mail.canned.page.newCannedResponse': '新建模板',
  'mail.canned.page.allMailboxesGlobal': '全部邮箱（全局）',

  // ════════════════════════════════════════════════════════════════
  // stats —— 概览统计卡（label + hint 两行文案）
  // ════════════════════════════════════════════════════════════════
  'mail.canned.stats.total': '模板总数',
  'mail.canned.stats.totalHint': '所有可用快速回复',
  'mail.canned.stats.shared': '共享模板',
  'mail.canned.stats.sharedHint': '全体客服可见',
  'mail.canned.stats.personal': '个人模板',
  'mail.canned.stats.personalHint': '仅自己可见',
  'mail.canned.stats.categories': '分类数',
  'mail.canned.stats.categoriesHint': '自定义分组',

  // ════════════════════════════════════════════════════════════════
  // category —— 左侧分类导航（"全部模板"/"未分类"是文案；分类名本身是用户数据）
  // ════════════════════════════════════════════════════════════════
  'mail.canned.category.navTitle': '分类导航',
  'mail.canned.category.searchPlaceholder': '搜索模板 / 分类...',
  'mail.canned.category.all': '全部模板',
  'mail.canned.category.uncategorized': '未分类',

  // ════════════════════════════════════════════════════════════════
  // list —— 右侧列表、筛选摘要、分组头
  // ════════════════════════════════════════════════════════════════
  'mail.canned.list.noMatch': '未找到匹配的模板',
  'mail.canned.list.noMatchHint': '尝试换个关键词或清除筛选条件',
  'mail.canned.list.empty': '暂无快速回复模板',
  'mail.canned.list.emptyHint': '点击右上角「新建模板」，快速创建第一个回复模板吧',
  'mail.canned.list.createFirst': '创建第一个模板',
  'mail.canned.list.resetAll': '重置全部',
  'mail.canned.list.groupHint': '点击卡片可预览完整内容',
  // 计数与量词拆开：`{count}` 保留自己的 JSX 表达式位置（英文按 count 取 one/other）
  'mail.canned.list.groupCountSuffix': ' 个模板',
  'mail.canned.list.moveToTrash': '移至垃圾箱',

  // ════════════════════════════════════════════════════════════════
  // badge —— 列表卡片上的可见性徽章（由 isShared 派生）
  // ════════════════════════════════════════════════════════════════
  'mail.canned.badge.personal': '个人',
  'mail.canned.badge.shared': '共享',

  // ════════════════════════════════════════════════════════════════
  // editor —— 新建 / 编辑弹窗
  // ════════════════════════════════════════════════════════════════
  'mail.canned.editor.titleEdit': '编辑快速回复',
  'mail.canned.editor.titleCreate': '新建快速回复',
  'mail.canned.editor.headingEdit': '正在编辑快速回复模板',
  'mail.canned.editor.headingCreate': '创建新的快速回复模板',
  'mail.canned.editor.htmlRichText': 'HTML · 富文本',
  'mail.canned.editor.headingHint':
    '填写模板基础信息与内容，内容区域支持 HTML 语法，可在右侧实时查看渲染效果',
  'mail.canned.editor.sectionInfo': '模板信息',
  'mail.canned.editor.requiredHint': '带 * 为必填项',
  'mail.canned.editor.nameLabel': '模板名称',
  'mail.canned.editor.namePlaceholder': '如：欢迎语、密码重置指引、退款说明',
  'mail.canned.editor.errorNameRequired': '请输入模板名称',
  'mail.canned.editor.categoryLabel': '分组（分类）',
  'mail.canned.editor.categoryPlaceholder': '如：欢迎、技术支持、退款（可新建或选择已有）',
  'mail.canned.editor.sortOrderLabel': '排序权重（数字越小越靠前）',
  'mail.canned.editor.globalAllMailboxesAvailable': '🌐 全局（所有邮箱可用）',
  'mail.canned.editor.sharedToggleOn': '共享给所有客服',
  'mail.canned.editor.sharedToggleOff': '仅自己可见（个人）',
  'mail.canned.editor.sharedToggleHint': '所有坐席/客服在下拉列表中都能看到并使用此模板',
  'mail.canned.editor.personalToggleHint': '仅当前账号可用，其他同事不会在模板库中看到它',
  'mail.canned.editor.bodyLabel': '模板内容',
  'mail.canned.editor.bodySupportsHtml': '支持 HTML 富文本',
  'mail.canned.editor.errorBodyRequired': '请输入模板内容',
  'mail.canned.editor.codeEditor': 'HTML 代码编辑器',
  'mail.canned.editor.bodyPlaceholder':
    '例如：\n<p>您好 <b>{customer.name}</b>，</p>\n<p>感谢您联系我们...</p>',
  'mail.canned.editor.liveRender': '实时 HTML 渲染',
  'mail.canned.editor.noDelay': '无延迟',
  'mail.canned.editor.bodyPreviewLabel': '📧 邮件正文预览',
  'mail.canned.editor.previewPlaceholder': '预览将显示在这里',
  'mail.canned.editor.previewPlaceholderHint': '支持所有标准 HTML 标签与样式',
  'mail.canned.editor.saving': '保存中...',
  'mail.canned.editor.saveChanges': '保存修改',
  'mail.canned.editor.createSubmit': '创建模板',

  // ════════════════════════════════════════════════════════════════
  // preview —— 预览弹窗
  // ════════════════════════════════════════════════════════════════
  'mail.canned.preview.title': '模板预览',
  'mail.canned.preview.sharedBadge': '共享模板',
  'mail.canned.preview.personalBadge': '个人模板',
  'mail.canned.preview.browserBar': '📧 mail-to:customer@example.com · 渲染预览',
  'mail.canned.preview.renderDone': '渲染完成',
  'mail.canned.preview.emptyTitle': '内容为空',
  'mail.canned.preview.emptyHint': '此模板暂无正文内容',
  'mail.canned.preview.createdBy': '创建人',
  'mail.canned.preview.editNow': '立即编辑',

  // ════════════════════════════════════════════════════════════════
  // deleteDialog —— 移入垃圾箱确认
  // ════════════════════════════════════════════════════════════════
  'mail.canned.deleteDialog.title': '删除模板',
  'mail.canned.deleteDialog.message':
    '模板将被移至垃圾箱，可在垃圾箱中恢复或彻底删除。',

  // ════════════════════════════════════════════════════════════════
  // toast —— 操作反馈
  // ════════════════════════════════════════════════════════════════
  'mail.canned.toast.restored': '模板「{name}」已恢复',
  'mail.canned.toast.hardDeleted': '模板「{name}」已彻底删除',
  'mail.canned.toast.updated': '模板已更新',
  'mail.canned.toast.created': '模板已创建',
  'mail.canned.toast.movedToTrash': '模板已移至垃圾箱',

  // ════════════════════════════════════════════════════════════════
  // trash —— 垃圾箱弹窗
  // ════════════════════════════════════════════════════════════════
  'mail.canned.trash.title': '垃圾箱（快速回复）',
  'mail.canned.trash.heading': '已删除的快速回复模板',
  'mail.canned.trash.retentionPrefix': '已删除的模板会保留在回收站，可',
  'mail.canned.trash.pendingSuffix': ' 条已删除模板待处理',
  'mail.canned.trash.searchPlaceholder': '搜索已删除模板（名称/内容/分类）',
  'mail.canned.trash.emptyAll': '清空全部',
  'mail.canned.trash.colCannedResponse': '模板',
  'mail.canned.trash.emptyHint': '已删除的快速回复模板会显示在这里',
  'mail.canned.trash.categoryPrefix': '分类 · ',
  'mail.canned.trash.deletedBy': '删除人',
  'mail.canned.trash.createdBy': '创建人：',
  'mail.canned.trash.restoreTitle': '恢复模板',
  'mail.canned.trash.restoreConfirm':
    '确认恢复模板「{name}」到模板列表？恢复后可在快速回复列表中正常使用。',
  'mail.canned.trash.hardDeleteConfirm': '模板「{name}」将被永久删除且不可恢复，确认继续？',
  'mail.canned.trash.clearConfirm':
    '共 {count} 条模板将被永久删除且不可恢复，确认清空整个垃圾箱？',
} satisfies Entries;
