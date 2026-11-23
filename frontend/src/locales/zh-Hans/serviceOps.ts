import type { Entries } from '../../i18n/types';

/**
 * 服务运营域（波次 15）：时间追踪 / 满意度评价 / 会话线程 / 附件预览。
 *
 * 分区：
 *   serviceOps.common.*        —— 本波四页逐字相同的短文案（分页按钮、失败 toast）
 *   serviceOps.timeTracking.*  —— `pages/TimeTracking.tsx`（计时器 / 概览 / 列表 / 编辑器）
 *   serviceOps.satisfaction.*  —— `pages/Satisfaction.tsx` + `pages/SatisfactionForm.tsx`
 *                                （坐席侧统计页与**客户侧公开评价页**共用评分字典）
 *   serviceOps.threads.*       —— `components/ThreadList.tsx`（含内联语音/视频播放器）
 *   serviceOps.attachments.*   —— `components/AttachmentPreview.tsx`
 *
 * 复用（**只在中文逐字相同**时上收，英文侧一并跟随，避免同一中文词两版英文）：
 *   - `common.save` / `common.cancel` / `common.delete` / `common.edit` / `common.empty` /
 *     `common.loading`（`加载中…` 单字符省略号）/ `common.shell.loading`（`加载中...` 三点）
 *   - `format.unknown`（`未知`）、`format.sourceType.liveChat`（`在线聊天`）、
 *     `format.sourceType.wechat`（`微信`）
 *   - `conversations.detail.threadType{Customer,Reply,Note,System}` 与
 *     `conversations.sidebar.chatToConversation`：`ThreadList` 的线程类型徽章与
 *     `ConversationDetail` 自己的线程类型标签是**同一组词**，共用可保证同页不漂移
 *   - `chat.conversation.date{Today,Yesterday,Tomorrow}`：`ThreadList` 的日期分组
 *   - `chat.widget.rating.starAria`：满意度星级按钮的 aria-label，模板逐字相同
 *
 * ⚠ **首尾空格有意义**，不要 trim、也不要为了整洁把空格挪进 JSX（会改变中文态渲染）：
 *   - `serviceOps.timeTracking.filters.foundPrefix`（尾随空格）与 `.foundSuffix`（前置空格）——
 *     原文是 JSX 里紧贴 `{totalItems}` 的两个裸文本节点，两侧各留一个空格；
 *   - `serviceOps.timeTracking.pagination.totalPrefix`（尾随）、`.totalMiddle`（两侧）、
 *     `.totalSuffix`（前置）；`serviceOps.satisfaction.pagination.*` 同理（原文在 `<span>` 内）；
 *   - `serviceOps.timeTracking.form.descriptionLabel` / `.startTimeLabel`（**尾随空格**）——
 *     原文是 `描述 <span>*</span>`，空格在文本节点里，删掉会渲染成 `描述*`；
 *   - `serviceOps.satisfaction.list.customerPrefix` / `.agentPrefix`（中文是全角冒号自带间距，
 *     英文值 `Customer: ` / `Agent: ` **必须带尾随空格**）；
 *   - `serviceOps.attachments.thumbnailAriaCurrent` 的英文值前置空格
 *     （对应中文全角括号自带的可视间距）。
 */
export const serviceOps = {
  // ════════ 本波共用 ════════
  'serviceOps.common.exportCsv': '导出 CSV',
  'serviceOps.common.deleteFailed': '删除失败',
  'serviceOps.common.saveFailed': '保存失败',
  'serviceOps.common.loadFailed': '加载失败',
  'serviceOps.common.noDescription': '无描述',
  'serviceOps.common.confirmDelete': '确认删除',
  'serviceOps.common.previousPage': '上一页',
  'serviceOps.common.nextPage': '下一页',
  'serviceOps.common.firstPage': '首页',
  'serviceOps.common.lastPage': '末页',

  // ════════ 时间追踪：页头与页签 ════════
  'serviceOps.timeTracking.pageTitle': '时间追踪',
  'serviceOps.timeTracking.subtitle': '记录和管理工作时长，支持计时器与手动录入，统计团队工时分布与可计费时长',
  'serviceOps.timeTracking.header.liveTracking': '实时追踪中',
  'serviceOps.timeTracking.newEntry': '新建记录',
  'serviceOps.timeTracking.tab.overview': '概览',
  'serviceOps.timeTracking.tab.list': '时间记录',

  // 时长格式（`formatDuration`）：分钟 / 整小时 / 时+分三态。预设按钮复用前两条模板
  // （`90` 分钟走 `duration.hours` 且参数取 `1.5`，与 `formatDuration(90)` 的 `1小时30分钟` **不同**，
  //  故预设按钮必须继续走「小数小时」，不要合并成 `formatDuration(min)`）
  'serviceOps.timeTracking.duration.minutes': '{minutes}分钟',
  'serviceOps.timeTracking.duration.hours': '{hours}小时',
  'serviceOps.timeTracking.duration.hoursMinutes': '{hours}小时{minutes}分钟',

  // 条目类型码（DTO `EntryType`，后端只有 `timer` / `manual` 两个取值）
  'serviceOps.timeTracking.entryType.timer': '计时器',
  'serviceOps.timeTracking.entryType.manual': '手动录入',

  // 并发冲突兜底（后端 409 的 `data.message` 优先，为空时才用本词条）
  'serviceOps.timeTracking.concurrencyConflict': '记录已被其他人修改，请刷新后重试',
  'serviceOps.timeTracking.timerAlreadyRunning': '已有运行中的计时器，请先停止当前计时器',

  // toast
  'serviceOps.timeTracking.toast.started': '计时已开始',
  'serviceOps.timeTracking.toast.startFailed': '启动失败',
  'serviceOps.timeTracking.toast.stopped': '计时已停止',
  'serviceOps.timeTracking.toast.stopFailed': '停止失败',
  'serviceOps.timeTracking.toast.updated': '记录更新成功',
  'serviceOps.timeTracking.toast.created': '记录创建成功',
  'serviceOps.timeTracking.toast.deleted': '记录已删除',
  'serviceOps.timeTracking.toast.exportSucceeded': '导出成功',
  'serviceOps.timeTracking.toast.exportFailed': '导出失败',

  // 统计卡片
  'serviceOps.timeTracking.stats.today': '今日工时',
  'serviceOps.timeTracking.stats.week': '本周工时',
  'serviceOps.timeTracking.stats.month': '本月工时',
  'serviceOps.timeTracking.stats.billable': '可计费工时',
  'serviceOps.timeTracking.stats.recordCount': '{count} 条记录',
  'serviceOps.timeTracking.stats.totalPrefix': '共 {value}',
  'serviceOps.timeTracking.stats.grandTotalPrefix': '总计 {value}',

  // 运行中计时器
  'serviceOps.timeTracking.activeTimer.badge': 'LIVE · 计时中',
  'serviceOps.timeTracking.activeTimer.startedAt': '开始于 {time}',
  'serviceOps.timeTracking.activeTimer.elapsed': '已用时长',
  'serviceOps.timeTracking.activeTimer.stop': '停止计时',
  'serviceOps.timeTracking.activeTimer.quickStart': '快速开始计时',
  'serviceOps.timeTracking.activeTimer.descriptionPlaceholder': '输入计时描述，按 Enter 立即开始（可选）...',
  'serviceOps.timeTracking.activeTimer.start': '开始计时',

  // 报表（最近 7 天）
  'serviceOps.timeTracking.report.byUserTitle': '按用户工时分布',
  'serviceOps.timeTracking.report.byUserSubtitle': '最近 7 天团队贡献榜',
  'serviceOps.timeTracking.report.byDateTitle': '按日期工时分布',
  'serviceOps.timeTracking.report.byDateSubtitle': '最近 7 天趋势',
  'serviceOps.timeTracking.report.countSuffix': '{count} 条',
  'serviceOps.timeTracking.report.emptyHint': '还没有工时记录',

  // 列表
  'serviceOps.timeTracking.list.colDescription': '描述 / 详情',
  'serviceOps.timeTracking.list.colDuration': '时长',
  'serviceOps.timeTracking.list.billable': '可计费',
  'serviceOps.timeTracking.list.nonBillable': '不可计费',
  'serviceOps.timeTracking.list.inProgress': '进行中',
  'serviceOps.timeTracking.list.startedAt': '开始：{time}',
  'serviceOps.timeTracking.list.endedAt': '结束：{time}',
  'serviceOps.timeTracking.list.emptyFiltered': '未找到匹配的记录',
  'serviceOps.timeTracking.list.emptyTitle': '暂无时间记录',
  'serviceOps.timeTracking.list.emptyFilteredHint': '尝试调整搜索关键词或筛选条件',
  'serviceOps.timeTracking.list.emptyHint': '点击右上角「新建记录」开始追踪工时',

  // 筛选栏（标签带 emoji，与不带 emoji 的同名文案**不是同一串**，刻意分开）
  'serviceOps.timeTracking.filters.keyword': '🔍 关键词搜索',
  'serviceOps.timeTracking.filters.keywordPlaceholder': '搜索描述、工单号...',
  'serviceOps.timeTracking.filters.clearSearch': '清除搜索',
  'serviceOps.timeTracking.filters.entryType': '📋 记录类型',
  'serviceOps.timeTracking.filters.allTypes': '全部类型',
  'serviceOps.timeTracking.filters.timerOption': '⏱ 计时器',
  'serviceOps.timeTracking.filters.manualOption': '✏ 手动录入',
  'serviceOps.timeTracking.filters.billableStatus': '💰 计费状态',
  'serviceOps.timeTracking.filters.allBillable': '全部计费',
  'serviceOps.timeTracking.filters.billableOption': '✅ 可计费',
  'serviceOps.timeTracking.filters.nonBillableOption': '❌ 不可计费',
  'serviceOps.timeTracking.filters.startDate': '📅 开始日期',
  'serviceOps.timeTracking.filters.endDate': '📅 结束日期',
  'serviceOps.timeTracking.filters.reset': '重置筛选',
  'serviceOps.timeTracking.filters.foundPrefix': '找到 ',
  'serviceOps.timeTracking.filters.foundSuffix': ' 条匹配的工时记录',

  // 分页：`共 {N} 条 · 第 {p} / {t} 页`
  'serviceOps.timeTracking.pagination.totalPrefix': '共 ',
  'serviceOps.timeTracking.pagination.totalMiddle': ' 条 · 第 ',
  'serviceOps.timeTracking.pagination.totalSuffix': ' 页',

  // 新建 / 编辑弹窗
  'serviceOps.timeTracking.form.editorTitleEdit': '编辑时间记录',
  'serviceOps.timeTracking.form.editorTitleCreate': '新建时间记录',
  'serviceOps.timeTracking.form.entryTypeSection': '记录类型',
  'serviceOps.timeTracking.form.entryTypeTimerHint': '实时计时，自动记录时长',
  'serviceOps.timeTracking.form.entryTypeManualHint': '手动输入工作时长',
  'serviceOps.timeTracking.form.basicSection': '基本信息',
  'serviceOps.timeTracking.form.descriptionLabel': '描述 ',
  'serviceOps.timeTracking.form.descriptionPlaceholder': '工作内容描述...',
  'serviceOps.timeTracking.form.conversationLabel': '关联工单（可选）',
  'serviceOps.timeTracking.form.customerLabel': '关联客户（可选）',
  'serviceOps.timeTracking.form.noConversation': '不关联工单',
  'serviceOps.timeTracking.form.noCustomer': '不关联客户',
  'serviceOps.timeTracking.form.unknownCustomer': '未知客户',
  'serviceOps.timeTracking.form.noSubject': '(无主题)',
  'serviceOps.timeTracking.form.noActiveConversations': '暂无活跃会话',
  'serviceOps.timeTracking.form.noCustomers': '暂无客户',
  'serviceOps.timeTracking.form.timeSection': '时间信息',
  'serviceOps.timeTracking.form.startTimeLabel': '开始时间 ',
  'serviceOps.timeTracking.form.durationLabel': '时长（分钟）',
  'serviceOps.timeTracking.form.durationPlaceholder': '例如: 30',
  'serviceOps.timeTracking.form.quickSelect': '快速选择：',
  'serviceOps.timeTracking.form.billingSection': '计费选项',
  'serviceOps.timeTracking.form.billableHint': '此时间记录将计入可计费工时',
  'serviceOps.timeTracking.form.nonBillableHint': '此时间记录不计入可计费工时',
  'serviceOps.timeTracking.form.saving': '保存中...',
  'serviceOps.timeTracking.form.descriptionRequired': '请输入描述',
  'serviceOps.timeTracking.form.startTimeRequired': '请选择开始时间',
  'serviceOps.timeTracking.form.durationRequired': '请输入时长（分钟）',
  'serviceOps.timeTracking.form.deleteConfirmMessage': '确定要删除此时间记录吗？此操作不可撤销。',

  // ════════ 满意度（坐席侧 + 客户侧共用评分字典） ════════
  // 评分码：后端 `SatisfactionRatingConst` 1=Bad / 2=Poor / 3=Average / 4=Good / 5=Excellent
  'serviceOps.satisfaction.rating.excellent': '远超期望',
  'serviceOps.satisfaction.rating.good': '物有所值',
  'serviceOps.satisfaction.rating.average': '中规中矩',
  'serviceOps.satisfaction.rating.poor': '略有失望',
  'serviceOps.satisfaction.rating.bad': '无法接受',
  'serviceOps.satisfaction.ratingDesc.excellent': '服务远超期望，值得推荐',
  'serviceOps.satisfaction.ratingDesc.good': '服务物有所值，基本符合期待',
  'serviceOps.satisfaction.ratingDesc.average': '服务中规中矩，仍有提升空间',
  'serviceOps.satisfaction.ratingDesc.poor': '有明显不足，需要改善',
  'serviceOps.satisfaction.ratingDesc.bad': '服务体验极差，完全无法接受',
  // 星级按钮 title：中文用全角冒号，与复用的 `chat.widget.rating.starAria`（半角冒号）不同串
  'serviceOps.satisfaction.starTitle': '{star} 星：{label}',

  'serviceOps.satisfaction.pageTitle': '满意度评价',
  'serviceOps.satisfaction.subtitle': '查看客户对服务的满意度评价和统计分析',
  'serviceOps.satisfaction.trendTitle': '满意度趋势',
  'serviceOps.satisfaction.trendChartAria': '满意度趋势图',
  'serviceOps.satisfaction.distributionTitle': '评分分布',
  'serviceOps.satisfaction.agentRankingTitle': '客服满意度排名',
  'serviceOps.satisfaction.mailboxTitle': '邮箱满意度',
  'serviceOps.satisfaction.axisScore': '分',
  'serviceOps.satisfaction.axisCount': '数',
  'serviceOps.satisfaction.legendAverage': '平均分',
  'serviceOps.satisfaction.reviewCount': '{count} 条评价',
  'serviceOps.satisfaction.mailboxReviewCount': '{count} 条评价 · {email}',
  'serviceOps.satisfaction.deleteConfirm': '确定要删除这条评价吗？',
  'serviceOps.satisfaction.chatRatingNotDeletable': '在线聊天评价数据不可删除',
  // 来源码：`chat_session` 走 `format.sourceType.liveChat`（在线聊天），其余走本键
  'serviceOps.satisfaction.source.ratingLink': '评价链接',

  'serviceOps.satisfaction.stats.total': '总评价数',
  'serviceOps.satisfaction.stats.goodRate': '好评率',
  'serviceOps.satisfaction.stats.goodCount': '好评 {count} 条',
  'serviceOps.satisfaction.stats.averageScore': '平均分',
  'serviceOps.satisfaction.stats.maxScoreHint': '满分 5 分',
  'serviceOps.satisfaction.stats.badShare': '占比 {percent}%',

  'serviceOps.satisfaction.filters.startDate': '开始日期',
  'serviceOps.satisfaction.filters.endDate': '结束日期',
  'serviceOps.satisfaction.filters.mailbox': '邮箱',
  'serviceOps.satisfaction.filters.allMailboxes': '全部邮箱',
  'serviceOps.satisfaction.filters.searchPlaceholder': '搜索客户、评价内容...',
  'serviceOps.satisfaction.filters.allRatings': '全部评分',
  'serviceOps.satisfaction.filters.allAgents': '全部客服',

  'serviceOps.satisfaction.table.source': '来源',
  'serviceOps.satisfaction.table.rating': '评分',
  'serviceOps.satisfaction.table.comment': '评价',
  'serviceOps.satisfaction.table.customer': '客户',
  'serviceOps.satisfaction.table.agent': '客服',
  'serviceOps.satisfaction.table.conversation': '会话',
  'serviceOps.satisfaction.table.time': '时间',
  'serviceOps.satisfaction.table.actions': '操作',
  // CSV 表头（导出在前端拼装）：其余 5 列复用上面的 `table.*`，只补这两列
  'serviceOps.satisfaction.csv.mailbox': '邮箱',
  'serviceOps.satisfaction.csv.ratedAt': '评价时间',

  'serviceOps.satisfaction.list.empty': '暂无评价数据',
  'serviceOps.satisfaction.list.customerPrefix': '客户：',
  'serviceOps.satisfaction.list.agentPrefix': '客服：',
  'serviceOps.satisfaction.list.noConversation': '未关联工单',
  'serviceOps.satisfaction.list.noCommentText': '无文字评价',
  'serviceOps.satisfaction.list.notDeletable': '不可删除',

  // 分页：`第 {p} / {t} 页，共 {n} 条`（槽位顺序与 `timeTracking.pagination` 不同，不可互换）
  'serviceOps.satisfaction.pagination.prefix': '第 ',
  'serviceOps.satisfaction.pagination.middle': ' 页，共 ',
  'serviceOps.satisfaction.pagination.suffix': ' 条',

  // 客户侧公开评价页（`SatisfactionForm`，语言按访客浏览器）
  'serviceOps.satisfaction.form.pageTitle': '服务评价',
  'serviceOps.satisfaction.form.fallbackSubject': '请根据本次服务的真实体验进行评分',
  'serviceOps.satisfaction.form.ratingLabel': '您对本次服务的整体评价',
  'serviceOps.satisfaction.form.ratingHint': '← 请点击上方星星选择评分 →',
  'serviceOps.satisfaction.form.selectedSummary': '{rating} / 5 星 · {label}',
  'serviceOps.satisfaction.form.commentLabel': '您的建议',
  'serviceOps.satisfaction.form.optional': '（可选）',
  'serviceOps.satisfaction.form.commentPlaceholder': '请告诉我们您的真实感受或建议，帮助我们做得更好...',
  'serviceOps.satisfaction.form.submitting': '提交中...',
  'serviceOps.satisfaction.form.submit': '提交评价',
  'serviceOps.satisfaction.form.footer': '您的评价将用于改进我们的服务质量 · 本页面支持 PC 与移动端',
  'serviceOps.satisfaction.form.invalidTitle': '评价链接无效',
  'serviceOps.satisfaction.form.invalidMessage': '该评价链接无效或已过期，请检查链接是否完整后重试。',
  'serviceOps.satisfaction.form.successTitle': '感谢您的评价！',
  'serviceOps.satisfaction.form.successStars': '{rating} / 5 星',
  'serviceOps.satisfaction.form.successLine1': '您的反馈对我们非常重要，感谢您抽出宝贵时间评价我们的服务。',
  'serviceOps.satisfaction.form.successLine2': '我们会持续优化，努力为您带来更好的体验。',
  'serviceOps.satisfaction.form.alreadyTitle': '您已评价过本次服务',
  'serviceOps.satisfaction.form.alreadyMessage': '感谢您的反馈！为保证评价的公平性，每个会话只能评价一次。',
  'serviceOps.satisfaction.form.validateFailed': '验证评价链接失败，请稍后重试',
  'serviceOps.satisfaction.form.selectRating': '请点击上方星星选择一个评分',
  'serviceOps.satisfaction.form.submitFailed': '提交失败，请稍后重试',

  // ════════ 会话线程（ThreadList） ════════
  // 内联语音播放器
  'serviceOps.threads.audio.status': '语音消息',
  'serviceOps.threads.audio.statusError': '语音消息（加载失败）',
  'serviceOps.threads.audio.statusLoading': '语音消息（加载中…）',
  'serviceOps.threads.audio.ariaLoadFailed': '语音消息加载失败：{fileName}',
  'serviceOps.threads.audio.ariaLoading': '语音消息加载中：{fileName}',
  'serviceOps.threads.audio.ariaPause': '暂停语音消息 {fileName}',
  'serviceOps.threads.audio.ariaPlay': '播放语音消息 {fileName}',
  'serviceOps.threads.audio.ariaProgress': '语音播放进度，{percent}%',
  // 内联视频预览
  'serviceOps.threads.video.title': '视频消息',
  'serviceOps.threads.video.collapse': '收起视频预览',
  'serviceOps.threads.video.collapseHint': '按 Esc 收起视频预览',
  'serviceOps.threads.video.playAria': '点击播放视频消息 {fileName}',
  // 线程类型徽章（其余四种复用 `conversations.detail.threadType*` 与 `conversations.sidebar.chatToConversation`）
  'serviceOps.threads.threadType.systemReply': '系统回复',
  // 日期分组：今天/昨天/明天复用 `chat.conversation.date*`，其余两种见下
  'serviceOps.threads.date.monthDay': '{month} 月 {day} 日',
  'serviceOps.threads.date.yearMonthDay': '{year} 年 {month} 月 {day} 日',
  // 兜底显示名与位置
  'serviceOps.threads.locationFallback': '位置',
  'serviceOps.threads.customerFallback': '客户',
  'serviceOps.threads.systemFallback': '系统',
  // 长正文折叠
  'serviceOps.threads.collapseBody': '收起正文',
  'serviceOps.threads.expandFull': '展开全文',
  'serviceOps.threads.collapse': '收起',
  // 附件
  'serviceOps.threads.attachmentsTitle': '附件 ({count})',
  'serviceOps.threads.viewImageAria': '查看图片 {fileName}',
  'serviceOps.threads.previewAttachmentAria': '预览附件 {fileName}',
  'serviceOps.threads.empty': '暂无消息',

  // ════════ 附件预览（AttachmentPreview） ════════
  'serviceOps.attachments.dialogAria': '附件预览',
  'serviceOps.attachments.unknownType': '未知类型',
  'serviceOps.attachments.download': '下载',
  'serviceOps.attachments.downloadFile': '下载文件',
  'serviceOps.attachments.closeTitle': '关闭 (Esc)',
  'serviceOps.attachments.closeAria': '关闭预览',
  'serviceOps.attachments.prevTitle': '上一张 (←)',
  'serviceOps.attachments.prevAria': '上一张',
  'serviceOps.attachments.nextTitle': '下一张 (→)',
  'serviceOps.attachments.nextAria': '下一张',
  'serviceOps.attachments.audioUnsupported': '您的浏览器不支持音频播放',
  'serviceOps.attachments.audioHint': '如无法播放（如 AMR 格式），请下载后使用专用播放器',
  'serviceOps.attachments.videoUnsupported': '您的浏览器不支持视频播放',
  'serviceOps.attachments.cannotPreview': '此文件类型无法在线预览',
  'serviceOps.attachments.thumbnailAria': '查看第 {index} 张附件{current}',
  'serviceOps.attachments.thumbnailAriaCurrent': '（当前）',
} satisfies Entries;
