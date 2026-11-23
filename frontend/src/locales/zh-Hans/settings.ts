import type { Entries } from '../../i18n/types';

/**
 * 设置与个人资料面的文案真源。
 *
 * 分区（每个分区对应一个页面/组件，便于定位与复用）：
 * `page`（`pages/Settings.tsx`：系统配置 / 邮件测试 / 系统信息三个一级 Tab
 * 与常规/邮件/安全/通知/会话/高级配置六个二级区的标签、说明与下拉选项文案）·
 * `profile`（`components/ProfileTab.tsx` + `pages/Profile.tsx`）·
 * `preferences`（`components/PreferencesSettings.tsx`）·
 * `security`（`components/SecuritySettings.tsx`）。
 *
 * **选项的 value 是后端 code（`app.locale` / `yyyy-MM-dd` / `absolute` 等）不进字典**，
 * 只有展示文案进字典——字典不参与写回，改文案不会改变已存配置。
 */
export const settings = {
  // ── 分区 settings_page ──

  // ── 页头 / 一级 Tab / 二级 Tab ──
  'settings.page.title': '系统设置',
  'settings.page.subtitle': '管理系统配置、邮件测试和系统信息',
  'settings.page.tab.system': '系统配置',
  'settings.page.tab.mail': '邮件测试',
  'settings.page.tab.info': '系统信息',
  'settings.page.subTab.general': '常规设置',
  'settings.page.subTab.email': '邮件设置',
  'settings.page.subTab.security': '安全设置',
  'settings.page.subTab.notification': '通知设置',
  'settings.page.subTab.conversation': '会话设置',
  'settings.page.subTab.advanced': '高级配置',

  // ── 通用：提示 / 报错 / 保存态 ──
  'settings.page.loadFailed': '加载失败',
  'settings.page.saved': '设置已保存',
  'settings.page.saveFailed': '保存失败',
  'settings.page.saving': '保存中...',
  'settings.page.operationFailed': '操作失败',
  'settings.page.deleteFailed': '删除失败',
  'settings.page.status.enabled': '已开启',
  'settings.page.status.disabled': '已关闭',

  // ── 常规设置：基本信息 ──
  'settings.page.general.title': '基本信息',
  'settings.page.general.description': '配置系统的基本信息和显示参数',
  'settings.page.general.systemName': '系统名称',
  'settings.page.general.systemNameHint': '显示在浏览器标题和登录页',
  'settings.page.general.companyName': '公司名称',
  'settings.page.general.companyNameHint': '用于邮件签名等位置',
  'settings.page.general.defaultLanguage': '默认语言',
  'settings.page.language.zhHans': '简体中文',
  'settings.page.language.zhHant': '繁體中文',
  'settings.page.language.en': 'English',
  'settings.page.language.ja': '日本語',
  'settings.page.general.defaultTimezone': '默认时区',
  'settings.page.general.currency': '货币单位',
  'settings.page.currency.cny': '人民币 (CNY)',
  'settings.page.currency.usd': '美元 (USD)',
  'settings.page.currency.eur': '欧元 (EUR)',
  'settings.page.currency.jpy': '日元 (JPY)',
  'settings.page.currency.gbp': '英镑 (GBP)',
  'settings.page.general.dateFormat': '日期格式',
  'settings.page.dateFormat.iso': '2024-01-15',
  'settings.page.dateFormat.slashYmd': '2024/01/15',
  'settings.page.dateFormat.mdy': '01/15/2024',
  'settings.page.dateFormat.dmy': '15/01/2024',
  'settings.page.dateFormat.cjk': '2024年01月15日',

  // ── 邮件设置：收取参数与行为 ──
  'settings.page.email.title': '邮件收取',
  'settings.page.email.description': '配置邮件收取的频率和行为',
  'settings.page.email.fromName': '发件人名称',
  'settings.page.email.fromNameHint': '系统邮件显示的发件人',
  'settings.page.email.fetchFrequency': '收取频率（分钟）',
  'settings.page.email.fetchFrequencyHint': '后台定时收取邮件的间隔',
  'settings.page.email.fetchFrequency.every': '每 {count} 分钟',
  'settings.page.email.maxFetchPerRun': '单次最大收取数',
  'settings.page.email.maxFetchPerRunHint': '每次运行最多收取的邮件数量',
  'settings.page.email.maxFetchPerRun.count': '{count} 封',
  'settings.page.email.behavior': '收取行为',
  'settings.page.email.markAsRead': '收取后标记为已读',
  'settings.page.email.markAsReadDescription': '收取成功后将邮件服务器上的邮件标记为已读',
  'settings.page.email.saveToSent': '发送邮件保存到已发送',
  'settings.page.email.saveToSentDescription': '发送回复后将邮件保存到已发送文件夹',
  'settings.page.email.deleteAfterFetch': '收取后删除服务器邮件',
  'settings.page.email.deleteAfterFetchDescription': '收取成功后从邮件服务器删除原邮件（不建议开启）',

  // ── 邮件设置：客户信息及联系方式收集 ──
  'settings.page.email.contactCollection.title': '客户信息及联系方式收集',
  'settings.page.email.contactCollection.description': '收取邮件时自动从签名块提取客户联系方式并填入客户资料',
  'settings.page.email.contactCollection.detail':
    '收取邮件时从邮件正文（优先签名块）提取客户姓名/手机/邮箱并填入客户资料。已有字段不覆盖，只补全缺失字段；每个客户最多尝试提取 3 次。',

  // ── 安全设置：密码策略 ──
  'settings.page.security.title': '密码策略',
  'settings.page.security.description': '配置用户密码的安全要求',
  'settings.page.security.passwordMinLength': '最小密码长度',
  'settings.page.security.passwordMinLength.chars': '{count} 位',
  'settings.page.security.maxLoginAttempts': '最大登录尝试次数',
  'settings.page.security.maxLoginAttemptsHint': '超过后将锁定账户',
  'settings.page.security.maxLoginAttempts.times': '{count} 次',
  'settings.page.security.maxLoginAttempts.unlimited': '不限制',
  'settings.page.security.requireUppercase': '必须包含大写字母',
  'settings.page.security.requireUppercaseDescription': '密码中至少包含一个大写英文字母 (A-Z)',
  'settings.page.security.requireLowercase': '必须包含小写字母',
  'settings.page.security.requireLowercaseDescription': '密码中至少包含一个小写英文字母 (a-z)',
  'settings.page.security.requireNumber': '必须包含数字',
  'settings.page.security.requireNumberDescription': '密码中至少包含一个数字 (0-9)',
  'settings.page.security.requireSymbol': '必须包含特殊符号',
  'settings.page.security.requireSymbolDescription': '密码中至少包含一个特殊符号 (!@#$%^&*)',

  // ── 会话与注册 ──
  'settings.page.session.title': '会话与注册',
  'settings.page.session.sessionTimeout': '会话超时时间（分钟）',
  'settings.page.session.sessionTimeoutHint': '用户无操作后自动登出',
  'settings.page.session.timeout.minutes30': '30 分钟',
  'settings.page.session.timeout.hours1': '1 小时',
  'settings.page.session.timeout.hours2': '2 小时',
  'settings.page.session.timeout.hours4': '4 小时',
  'settings.page.session.timeout.hours8': '8 小时',
  'settings.page.session.timeout.hours24': '24 小时',
  'settings.page.session.timeout.days7': '7 天',
  'settings.page.session.allowRegistration': '开放用户注册',
  'settings.page.session.allowRegistrationDescription': '允许访客通过注册页面创建账户（需管理员审核）',

  // ── 通知设置：邮件通知 ──
  'settings.page.notification.title': '邮件通知',
  'settings.page.notification.description': '配置哪些事件会发送邮件通知给客服',
  'settings.page.notification.newConversation': '新会话通知',
  'settings.page.notification.newConversationDescription': '有新的客户会话创建时发送通知',
  'settings.page.notification.customerReply': '客户回复通知',
  'settings.page.notification.customerReplyDescription': '客户回复已分配的会话时发送通知',
  'settings.page.notification.newReply': '新回复通知',
  'settings.page.notification.newReplyDescription': '其他客服回复您参与的会话时发送通知',
  'settings.page.notification.mention': '@提及通知',
  'settings.page.notification.mentionDescription': '在内部笔记中被 @ 提及时发送通知',
  'settings.page.notification.assignment': '会话分配通知',
  'settings.page.notification.assignmentDescription': '有新会话分配给您时发送通知',

  // ── 会话设置：默认行为 ──
  'settings.page.conversation.title': '会话默认行为',
  'settings.page.conversation.description': '配置会话的默认状态和显示方式',
  'settings.page.conversation.defaultStatus': '新会话默认状态',
  'settings.page.conversation.defaultStatus.active': '待处理',
  'settings.page.conversation.defaultStatus.pending': '待回复',
  'settings.page.conversation.defaultStatus.open': '处理中',
  'settings.page.conversation.autoClose': '自动关闭时间（小时）',
  'settings.page.conversation.autoCloseHint': '客户回复后超过此时间未操作则自动关闭',
  'settings.page.conversation.autoClose.hours24': '24 小时',
  'settings.page.conversation.autoClose.days2': '2 天',
  'settings.page.conversation.autoClose.days3': '3 天',
  'settings.page.conversation.autoClose.days5': '5 天',
  'settings.page.conversation.autoClose.days7': '7 天',
  'settings.page.conversation.autoClose.days14': '14 天',
  'settings.page.conversation.autoClose.days30': '30 天',
  'settings.page.conversation.autoClose.never': '不自动关闭',
  'settings.page.conversation.threadTime': '会话时间显示方式',
  'settings.page.conversation.threadTime.absolute': '精确时间（2024-01-15 10:30）',
  'settings.page.conversation.threadTime.relative': '相对时间（2 小时前）',
  'settings.page.conversation.threadTime.both': '两者都显示',
  'settings.page.conversation.signaturePosition': '签名位置',
  'settings.page.conversation.signaturePosition.beforeQuote': '引用内容之前',
  'settings.page.conversation.signaturePosition.afterQuote': '引用内容之后',
  'settings.page.conversation.signaturePosition.bottom': '邮件底部',
  'settings.page.conversation.allowDelete': '允许删除会话',
  'settings.page.conversation.allowDeleteDescription': '管理员可以删除会话及其所有回复',

  // ── 高级配置：提示、筛选、表格 ──
  'settings.page.advanced.warning': '直接管理系统的所有配置项，修改不当可能导致系统异常，请谨慎操作。',
  'settings.page.advanced.filterCategory': '分类筛选',
  'settings.page.advanced.allCategories': '全部分类',
  'settings.page.advanced.create': '新建配置',
  'settings.page.advanced.empty': '暂无配置项',
  'settings.page.advanced.col.name': '名称',
  'settings.page.advanced.col.value': '值',
  'settings.page.advanced.col.category': '分类',
  'settings.page.advanced.col.updatedAt': '更新时间',
  'settings.page.advanced.col.actions': '操作',
  'settings.page.advanced.emptyValue': '（空）',
  'settings.page.advanced.updatedAtPrefix': '更新于',

  // ── 高级配置：新增 / 编辑弹窗 ──
  'settings.page.advanced.editTitle': '编辑配置项',
  'settings.page.advanced.createTitle': '新建配置项',
  'settings.page.advanced.nameRequired': '配置项名称不能为空',
  'settings.page.advanced.field.name': '配置项名称',
  'settings.page.advanced.field.namePlaceholder': '如：app.site_name',
  'settings.page.advanced.field.nameImmutable': '配置项名称不可修改',
  'settings.page.advanced.field.value': '配置项值',
  'settings.page.advanced.field.valuePlaceholder': '配置值',
  'settings.page.advanced.field.category': '分类',
  'settings.page.advanced.field.optional': '(可选)',
  'settings.page.advanced.field.categoryPlaceholder': '如：general、email、security',
  'settings.page.advanced.deleteTitle': '删除配置项',
  'settings.page.advanced.deleteConfirm': '确认删除配置项「{name}」吗？此操作不可恢复。',
  'settings.page.advanced.saveSettings': '保存设置',

  // ── 邮件测试：邮箱列表与连接测试 ──
  'settings.page.mail.mailboxList': '邮箱列表',
  'settings.page.mail.mailboxListHint': '选择邮箱进行测试',
  'settings.page.mail.noMailbox': '暂无邮箱配置',
  'settings.page.mail.testConnection': '测试连接',
  'settings.page.mail.connectionOk': '连接正常',
  'settings.page.mail.connectionFailed': '连接失败',
  'settings.page.mail.selectMailbox': '选择邮箱',
  'settings.page.mail.selectMailboxRequired': '请选择邮箱',
  'settings.page.mail.pleaseSelect': '请选择',

  // ── 邮件测试：收取测试 ──
  'settings.page.mail.fetchTestTitle': '邮件收取测试',
  'settings.page.mail.fetchTestDescription': '手动触发从邮箱服务器收取新邮件，将自动创建客户与会话记录。',
  'settings.page.mail.fetching': '收取中...',
  'settings.page.mail.fetchNow': '立即收取',
  'settings.page.mail.fetchSuccess': '成功收取 {count} 封邮件',
  'settings.page.mail.fetchFailed': '收取失败',

  // ── 邮件测试：发送测试 ──
  'settings.page.mail.sendTestTitle': '邮件发送测试',
  'settings.page.mail.sendTestDescription': '通过选定的邮箱发送一封测试邮件，验证 SMTP 配置是否正确。',
  'settings.page.mail.fromMailbox': '发件邮箱',
  'settings.page.mail.recipient': '收件人',
  'settings.page.mail.subject': '主题',
  'settings.page.mail.subjectPlaceholder': '邮件主题',
  'settings.page.mail.body': '正文',
  'settings.page.mail.bodyPlaceholder': '支持 HTML 格式',
  'settings.page.mail.sending': '发送中...',
  'settings.page.mail.sendMail': '发送邮件',
  'settings.page.mail.fillAllFields': '请填写完整邮件内容',
  'settings.page.mail.sendFailed': '发送失败',

  // ── 系统信息：概览卡片与信息表 ──
  'settings.page.info.environment': '运行环境',
  'settings.page.info.database': '数据库',
  'settings.page.info.currentLanguage': '当前语言',
  'settings.page.info.startupTime': '启动时间',
  'settings.page.info.backendVersion': '后端版本',
  'settings.page.info.frontendVersion': '前端版本',
  'settings.page.info.authMethod': '认证方式',
  'settings.page.info.passwordHashing': '密码加密',
  'settings.page.info.apiDocs': 'API 文档',
  'settings.page.info.apiDocsPath': '/docs（同域入口）',
  'settings.page.info.localization': '语言与本地化',
  'settings.page.info.supportedLanguages': '支持的语言',
  'settings.page.info.techStack': '技术栈',
  'settings.page.info.architecture': '架构特性',
  'settings.page.info.arch.ddd': 'DDD 分层架构',
  'settings.page.info.arch.orm': 'ORM 框架',
  'settings.page.info.arch.auth': '安全认证',
  'settings.page.info.arch.mail': '邮件处理',
  'settings.page.info.arch.scheduler': '定时任务',
  'settings.page.info.arch.schedulerDescription': 'Quartz.NET 每 5 分钟',


  // ── 分区 settings_profile ──

  // ── 左侧锚点导航（ProfileTab） ──
  // 本前缀下"最先出现"的几条文案由 ProfileTab 组件渲染（渲染级 key 泄漏探针按前缀取
  // 前 3 条无插值文案做"关键文案在场"断言，取到的必须是该组件真正渲染出的值）。
  'settings.profile.nav.overview': '用户概览',
  'settings.profile.nav.stats': '数据统计',
  'settings.profile.nav.basic': '基本资料',
  'settings.profile.nav.preferences': '偏好设置',
  'settings.profile.nav.password': '修改密码',
  'settings.profile.nav.history': '登录历史',

  // ── 用户概览卡片 ──
  'settings.profile.avatarAlt': '头像',
  'settings.profile.changeAvatar': '更换头像',
  'settings.profile.removeAvatar': '移除头像',
  'settings.profile.uploading': '上传中...',
  'settings.profile.processing': '处理中...',

  // ── 头像上传 / 移除反馈 ──
  'settings.profile.avatar.invalidType': '请上传图片文件',
  'settings.profile.avatar.tooLarge': '图片大小不能超过 2MB',
  'settings.profile.avatar.uploadSucceeded': '头像已更新',
  'settings.profile.avatar.uploadFailed': '头像上传失败',
  'settings.profile.avatar.removeSucceeded': '头像已移除',
  'settings.profile.avatar.removeFailed': '头像移除失败',

  // ── 数据统计卡片 ──
  'settings.profile.stats.conversations': '参与会话',
  'settings.profile.stats.replies': '已回复',
  'settings.profile.stats.onlineDuration': '在线时长',
  'settings.profile.stats.registeredAt': '注册时间',

  // ── 在线时长（数值由代码算出，单位随语言） ──
  'settings.profile.duration.zero': '0分钟',
  'settings.profile.duration.minutes': '{minutes}分钟',
  'settings.profile.duration.hours': '{hours}小时',
  'settings.profile.duration.hoursMinutes': '{hours}小时{minutes}分',
  'settings.profile.duration.days': '{days}天',
  'settings.profile.duration.daysHours': '{days}天{hours}小时',

  // ── 基本资料 ──
  'settings.profile.basic.title': '基本资料',
  'settings.profile.basic.description': '管理您的个人信息',
  'settings.profile.basic.firstName': '名',
  'settings.profile.basic.firstNamePlaceholder': '输入名',
  'settings.profile.basic.lastName': '姓',
  'settings.profile.basic.lastNamePlaceholder': '输入姓',
  'settings.profile.basic.email': '邮箱',
  'settings.profile.basic.role': '角色',
  'settings.profile.basic.registeredAt': '注册时间',
  'settings.profile.basic.save': '保存资料',
  'settings.profile.basic.saving': '保存中...',
  'settings.profile.basic.saveSucceeded': '资料已保存',
  'settings.profile.basic.saveFailed': '保存失败',

  // ── 偏好设置卡片 ──
  'settings.profile.preferences.title': '偏好设置',
  'settings.profile.preferences.description': '自定义您的使用体验',
  'settings.profile.preferences.defaultMailbox': '默认邮箱',
  'settings.profile.preferences.noMailbox': '不指定',
  'settings.profile.preferences.pageSize': '每页显示数',
  'settings.profile.preferences.pageSizeOption': '{count} 条/页',
  'settings.profile.preferences.timezone': '时区',
  'settings.profile.preferences.language': '语言',
  'settings.profile.preferences.theme': '主题',
  'settings.profile.preferences.themeLight': '浅色',
  'settings.profile.preferences.themeDark': '深色',
  'settings.profile.preferences.themeSystem': '跟随系统',
  'settings.profile.preferences.autoRefreshInterval': '自动刷新间隔',
  'settings.profile.preferences.refreshNever': '不自动刷新',
  'settings.profile.preferences.refreshSeconds': '{seconds} 秒',
  'settings.profile.preferences.refreshMinutes': '{minutes} 分钟',
  'settings.profile.preferences.notificationSettings': '通知设置',
  'settings.profile.preferences.emailNotifications': '邮件通知',
  'settings.profile.preferences.emailNotificationsDesc': '有新会话时发送邮件通知',
  'settings.profile.preferences.desktopNotifications': '桌面通知',
  'settings.profile.preferences.desktopNotificationsDesc': '浏览器桌面推送通知',
  'settings.profile.preferences.soundEnabled': '声音提醒',
  'settings.profile.preferences.soundEnabledDesc': '新消息时播放提示音',
  'settings.profile.preferences.save': '保存偏好设置',
  'settings.profile.preferences.saveSucceeded': '偏好设置已保存',

  // ── 修改密码卡片 ──
  'settings.profile.password.title': '修改密码',
  'settings.profile.password.description': '定期更换密码以保护账户安全',
  'settings.profile.password.current': '当前密码',
  'settings.profile.password.currentPlaceholder': '输入当前密码',
  'settings.profile.password.new': '新密码',
  'settings.profile.password.newPlaceholder': '至少 {count} 位',
  'settings.profile.password.confirm': '确认新密码',
  'settings.profile.password.confirmPlaceholder': '再次输入新密码',
  'settings.profile.password.submit': '修改密码',
  'settings.profile.password.submitting': '修改中...',
  'settings.profile.password.incomplete': '请填写完整密码',
  'settings.profile.password.mismatch': '两次输入的新密码不一致',
  'settings.profile.password.success': '密码修改成功，请重新登录',
  'settings.profile.password.failed': '密码修改失败',
  // 密码策略校验失败时，多条后端/策略文案的连接符
  'settings.profile.password.errorSeparator': '；',

  // ── 登录历史 ──
  'settings.profile.history.title': '登录历史',
  'settings.profile.history.description': '最近 5 次登录记录，如有异常请及时修改密码',
  'settings.profile.history.online': '当前在线',
  'settings.profile.history.empty': '暂无登录记录',
  'settings.profile.history.emptyHint': '登录后将显示您的登录历史',
  'settings.profile.history.currentSession': '当前会话',
  'settings.profile.history.entryNumber': '历史登录 #{index}',
  'settings.profile.history.active': '活跃',
  'settings.profile.history.warning': '如发现异常登录，请立即修改密码并联系管理员',

  // ── 个人中心页（pages/Profile.tsx） ──
  // 由 `pages/Profile.tsx`（页面外壳，而非 ProfileTab 组件）渲染，故置于本前缀末尾。
  'settings.profile.pageTitle': '个人中心',
  'settings.profile.pageDescription': '管理您的个人资料、偏好设置、账户安全与登录历史',

  // ── 偏好设置组件（PreferencesSettings.tsx） ──
  'settings.preferences.appearance': '外观',
  'settings.preferences.darkMode': '深色模式',
  'settings.preferences.darkModeDesc': '切换深色/浅色主题',
  'settings.preferences.notifications': '通知',
  'settings.preferences.emailNotifications': '邮件通知',
  'settings.preferences.emailNotificationsDesc': '接收系统邮件通知',
  'settings.preferences.browserNotifications': '浏览器通知',
  'settings.preferences.browserNotificationsDesc': '接收浏览器推送通知',
  'settings.preferences.soundEnabled': '声音提醒',
  'settings.preferences.soundEnabledDesc': '接收声音通知',
  'settings.preferences.session': '会话设置',
  'settings.preferences.sessionAutoClose': '会话自动关闭',
  'settings.preferences.autoRefresh': '自动刷新',
  'settings.preferences.workdays': '工作日设置',
  'settings.preferences.selectAll': '全选',
  'settings.preferences.clearAll': '清空',

  // 会话自动关闭：弹窗选项标签
  'settings.preferences.autoCloseOption.never': '不自动关闭',
  'settings.preferences.autoCloseOption.minutes5': '5 分钟',
  'settings.preferences.autoCloseOption.minutes15': '15 分钟',
  'settings.preferences.autoCloseOption.minutes30': '30 分钟',
  'settings.preferences.autoCloseOption.hour1': '1 小时',
  // 会话自动关闭：当前值显示
  'settings.preferences.autoCloseValue.never': '永不',
  'settings.preferences.autoCloseValue.minutes': '{minutes} 分钟',
  'settings.preferences.autoCloseValue.hour': '1 小时',
  'settings.preferences.autoCloseValue.hours': '{hours} 小时',

  // 自动刷新：弹窗选项标签
  'settings.preferences.refreshOption.never': '不自动刷新',
  'settings.preferences.refreshOption.seconds30': '30 秒',
  'settings.preferences.refreshOption.minutes1': '1 分钟',
  'settings.preferences.refreshOption.minutes5': '5 分钟',
  // 自动刷新：当前值显示
  'settings.preferences.refreshValue.never': '不自动刷新',
  'settings.preferences.refreshValue.seconds': '{seconds} 秒',
  'settings.preferences.refreshValue.minutes': '{minutes} 分钟',

  // 工作日
  'settings.preferences.workday.monday': '周一',
  'settings.preferences.workday.tuesday': '周二',
  'settings.preferences.workday.wednesday': '周三',
  'settings.preferences.workday.thursday': '周四',
  'settings.preferences.workday.friday': '周五',
  'settings.preferences.workday.saturday': '周六',
  'settings.preferences.workday.sunday': '周日',
  'settings.preferences.workdaysUnset': '未设置',
  'settings.preferences.workdaysAll': '全周',
  'settings.preferences.workdaysCount': '{count} 天',

  // ── 安全设置组件（SecuritySettings.tsx） ──
  'settings.security.warningTitle': '密码安全提醒',
  'settings.security.warningDescription': '建议定期更换密码以保护账户安全',
  'settings.security.title': '修改密码',
  'settings.security.currentPassword': '当前密码',
  'settings.security.currentPasswordPlaceholder': '请输入当前密码',
  'settings.security.newPassword': '新密码',
  'settings.security.newPasswordPlaceholder': '请输入新密码',
  'settings.security.confirmPassword': '确认新密码',
  'settings.security.confirmPasswordPlaceholder': '请再次输入新密码',
  'settings.security.submit': '修改密码',
  'settings.security.submitting': '修改中...',
  'settings.security.currentRequired': '请输入当前密码',
  'settings.security.newRequired': '请输入新密码',
  'settings.security.mismatch': '两次输入的新密码不一致',
  'settings.security.success': '密码修改成功',
  'settings.security.failed': '密码修改失败',
  // 密码策略校验失败时，多条策略文案的连接符
  'settings.security.errorSeparator': '；',

  // ── password：密码策略条目与本地校验消息 ──
  // 产出方 `components/PasswordPolicyHint.tsx`（策略条目）与 `hooks/usePasswordPolicy.ts`
  // （本地校验消息 + 拉取策略失败时的兜底策略）。两处都不是页面，故独立一区。
  'settings.password.rule.minLength': '至少 {count} 个字符',
  'settings.password.rule.uppercase': '至少一个大写字母 (A-Z)',
  'settings.password.rule.lowercase': '至少一个小写字母 (a-z)',
  'settings.password.rule.digit': '至少一个数字 (0-9)',
  'settings.password.rule.symbol': '至少一个特殊字符 (!@#$%^&*...)',
  'settings.password.error.empty': '密码不能为空',
  'settings.password.error.minLength': '密码长度至少 {count} 位',
  'settings.password.error.uppercase': '密码必须包含至少一个大写字母',
  'settings.password.error.lowercase': '密码必须包含至少一个小写字母',
  'settings.password.error.digit': '密码必须包含至少一个数字',
  'settings.password.error.symbol': '密码必须包含至少一个特殊字符',
} satisfies Entries;
