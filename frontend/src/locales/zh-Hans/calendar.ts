import type { Entries } from '../../i18n/types';

/**
 * 日历簇中文真源（批次 2 波次 13）：日程编辑面板、各视图、Outlook 同步、订阅、垃圾箱、store 层。
 *
 * 分区（命名空间即 key 前缀）：
 *   calendar.common.* —— 跨文件**逐字相同**的共用文案
 *   calendar.editor.* —— 日程编辑面板（`panels/EditorPanel.tsx`）
 *   calendar.views.* —— 列表 / 月 / 周 / 日 / 概览视图（`views/*`）
 *   calendar.panels.* —— Outlook 同步 / 订阅 / 面板共享件（`panels/*`）
 *   calendar.trash.* —— 垃圾箱（`panels/TrashPanel.tsx` / `views/TrashView.tsx` 共用同一批键）
 *   calendar.store.* —— `store.ts` / `calendarUtils.ts` / `hooks.ts`（非组件模块，`tBound` 取词）
 *   calendar.shell.* —— 页面外壳 / 事件条 / 批量条 / 过滤器
 *   calendar.a11y.* —— 无障碍与可靠性文案（`a11yReliability.tsx`）
 *
 * ⚠ 若干词条的值**首尾空格有意义**（原文是 JSX 里紧贴 `<span>`/`{expr}` 的裸文本节点），
 *   例如分页的 `共 ` 与 ` 条`。这类值**不要 trim**，一旦裁掉就会渲染成 `共26条`。
 *
 * 非组件模块（`store.ts` / `calendarUtils.ts` / `hooks.ts` / `a11yReliability.tsx`）的词条
 * 由 `tBound` / `translate` 在取词那一刻按**绑定语言**解析——不要把结果写进持久化 state。
 */
export const calendar = {
  // ══ common —— 跨文件**逐字相同**的共用文案 ══

  // ── 事件标题兜底 ──
  'calendar.common.untitled': '(无标题)',

  // ── 本地缓存 / 并发提示 ──
  'calendar.common.staleDataRefreshed': '数据已被其他用户修改，已刷新到最新版本',
  'calendar.common.pendingSync': '本地修改待同步',
  'calendar.common.pendingSyncHint': '本地修改待后端同步（刷新后仍保留当前位置）',

  // ── 顶部工具条 / 视图名 ──
  'calendar.common.today': '今天',
  'calendar.common.newEvent': '新建事件',
  'calendar.common.overview': '概览',
  'calendar.common.trash': '垃圾箱',
  'calendar.common.outlookSync': 'Outlook 同步',

  // ── 事件属性名（编辑器与列表视图表头共用） ──
  'calendar.common.title': '标题',
  'calendar.common.type': '类型',
  'calendar.common.status': '状态',
  'calendar.common.priority': '优先级',
  'calendar.common.progress': '进度',
  'calendar.common.location': '地点',
  'calendar.common.assignee': '负责人',
  'calendar.common.reminder': '提醒',
  'calendar.common.allDay': '全天',
  'calendar.common.allDayEvent': '全天事件',
  'calendar.common.custom': '自定义',
  'calendar.common.complete': '完成',
  'calendar.common.conflict': '冲突',
  'calendar.common.timeConflict': '时间冲突',

  // ── 批量选择 / 分页 ──
  'calendar.common.cancelSelection': '取消选择',
  'calendar.common.deleteEvent': '删除日程',
  'calendar.common.previousPage': '上一页',
  'calendar.common.nextPage': '下一页',

  // ── 同步 / 导出失败 ──
  'calendar.common.syncFailed': '同步失败',
  'calendar.common.saveConfigFailed': '保存配置失败',
  'calendar.common.exportIcsFailed': '导出 ICS 失败',

  // ── 编辑器 / 同步面板按钮与提示 ──
  'calendar.common.saving': '保存中...',
  'calendar.common.closeHint': '关闭提示',

  // ── 星期缩写（月/周/日视图表头、重复规则构造器共用；中文为单字符） ──
  'calendar.common.weekdayShort.sun': '日',
  'calendar.common.weekdayShort.mon': '一',
  'calendar.common.weekdayShort.tue': '二',
  'calendar.common.weekdayShort.wed': '三',
  'calendar.common.weekdayShort.thu': '四',
  'calendar.common.weekdayShort.fri': '五',
  'calendar.common.weekdayShort.sat': '六',

  // ══ editor —— 日程编辑面板（`panels/EditorPanel.tsx`） ══

  // ── 编辑器头部 / 撤销栈描述（存进 undoStack，随 localStorage 持久化） ──
  'calendar.editor.headerEdit': '编辑日程',
  'calendar.editor.headerCreate': '新建日程',
  'calendar.editor.subtitleEdit': '修改事件详情',
  'calendar.editor.subtitleCreate': '创建一个新的事件',
  'calendar.editor.undoEdit': '编辑事件',
  'calendar.editor.undoDelete': '删除事件',

  // ── 保存 / 删除 / 导出 失败与并发提示 ──
  'calendar.editor.titleRequired': '标题不能为空',
  'calendar.editor.titleRequiredHint': '请输入标题',
  'calendar.editor.saveFailed': '保存失败',
  'calendar.editor.deleteFailed': '删除失败',
  'calendar.editor.staleSyncRefreshed': '该日程刚刚被其他操作修改，本地已同步为最新版本',
  'calendar.editor.staleSyncRetryHint': '该日程刚刚被其他操作修改，请刷新页面后再次编辑',
  'calendar.editor.staleDataRetryHint': '数据已被其他用户修改，请刷新后重试',
  'calendar.editor.icsDownloading': '正在下载 ICS 文件',
  'calendar.editor.exportIcsTitle': '导出为 .ics',

  // ── 服务端 409 冲突列表（拼进 toast 文本） ──
  'calendar.editor.saveConflictToast': '{message}：{titles}{extra}',
  'calendar.editor.conflictMore': ' 等{count}个',
  'calendar.editor.titleListSeparator': '、',

  // ── 编辑器内实时冲突提示 ──
  'calendar.editor.liveConflictHard': '存在严重冲突',
  'calendar.editor.liveConflictSoft': '存在边缘冲突',
  'calendar.editor.overlapLabel': '重叠',
  'calendar.editor.overlapSeconds': '{count} 秒',
  'calendar.editor.overlapMinutes': '{count} 分钟',
  'calendar.editor.liveConflictPrefix': '当前时间与',
  'calendar.editor.liveConflictMiddle': '个日程重叠，',
  'calendar.editor.liveConflictHardHint': '可能造成排期冲突，建议调整时段。',
  'calendar.editor.liveConflictSoftHint': '仅边缘相邻，可按需调整。',
  'calendar.editor.moreItems': '…另有 {count} 个',
  'calendar.editor.savedConflictNotice': '保存成功，但与 {count} 个事件存在{tag}',
  'calendar.editor.conflictHard': '硬冲突（完全重叠）',
  'calendar.editor.conflictSoft': '软冲突（时间相邻）',

  // ── 事件模板 ──
  'calendar.editor.templateLabel': '模板',
  'calendar.editor.templateQuickCreate': '— 从模板快速创建 —',
  'calendar.editor.templateMeta': '（{duration}，{type}）',
  'calendar.editor.templateDuration': '{minutes}分钟',
  'calendar.editor.durationMinutes': '{minutes} 分钟',
  'calendar.editor.saveAsTemplate': '保存为模板',
  'calendar.editor.saveAsTemplateTitle': '将当前填写的事件信息保存为模板',
  'calendar.editor.manage': '管理',
  'calendar.editor.manageTemplates': '管理模板',
  'calendar.editor.manageTemplatesTitle': '管理已保存的模板',
  'calendar.editor.noTemplates': '暂无模板，先保存一个吧',
  'calendar.editor.templateName': '模板名称',
  'calendar.editor.templateNamePlaceholder': '如：每周周会、客户拜访、面试...',
  'calendar.editor.templateSaved': '模板「{name}」已保存',
  'calendar.editor.saveTemplateFailed': '保存模板失败',
  'calendar.editor.templateDeleted': '模板已删除',
  'calendar.editor.deleteTemplateFailed': '删除模板失败',
  'calendar.editor.deleteTemplate': '删除模板',
  'calendar.editor.confirmSave': '确认保存',
  'calendar.editor.saveTemplateMessage': '把当前填写的「{title}」保存为模板，方便后续快速创建。',
  'calendar.editor.unnamedEvent': '未命名事件',

  // ── 基础字段 ──
  'calendar.editor.titlePlaceholder': '例如：团队周会',
  'calendar.editor.descriptionLabel': '描述',
  'calendar.editor.descriptionPlaceholder': '详细描述...',
  'calendar.editor.startTime': '开始时间',
  'calendar.editor.endTime': '结束时间',
  'calendar.editor.locationPlaceholder': '例如：会议室 A',
  'calendar.editor.dueAt': '截止时间',

  // ── 会议类型专属字段 ──
  'calendar.editor.meetingLink': '会议链接',
  'calendar.editor.hosts': '主持人',
  'calendar.editor.selectHostsPlaceholder': '— 请选择主持人 —',
  'calendar.editor.removeHost': '移除 {name}',
  'calendar.editor.noAssignableUsers': '暂无可选用户',
  'calendar.editor.agendaIdsLabel': '议程 ID (逗号分隔，可选)',
  'calendar.editor.agendaIdsPlaceholder': '例如 101, 102',
  'calendar.editor.selectAssigneePlaceholder': '— 请选择负责人 —',
  'calendar.editor.userFallback': '用户#{id}',

  // ── 任务优先级（码值 LOW/MEDIUM/HIGH/URGENT） ──
  'calendar.editor.priorityLow': '低',
  'calendar.editor.priorityMedium': '中',
  'calendar.editor.priorityHigh': '高',
  'calendar.editor.priorityUrgent': '紧急',

  // ── 颜色 ──
  'calendar.editor.color': '颜色',
  'calendar.editor.colorHint': '点击选择 · 最右可自定义',
  'calendar.editor.presetColors': '预设颜色',
  'calendar.editor.customColor': '自定义颜色',

  // ── 重复规则手风琴 ──
  'calendar.editor.recurrenceRule': '重复规则',
  'calendar.editor.recurrenceEnabled': '已启用',
  'calendar.editor.recurrenceActive': '已启用重复',
  'calendar.editor.frequency': '频率',
  'calendar.editor.recurrenceNone': '无',
  'calendar.editor.interval': '间隔',
  'calendar.editor.weekdaysLabel': '周几',
  'calendar.editor.recurrenceWeekdays': '重复周几',
  'calendar.editor.weekdays': '工作日',
  'calendar.editor.everyDay': '每天',
  'calendar.editor.clear': '清除',
  'calendar.editor.recurrenceEndMode': '结束方式',
  'calendar.editor.endModeNever': '永不',
  'calendar.editor.endModeNeverDesc': '一直重复',
  'calendar.editor.endModeCount': 'N 次',
  'calendar.editor.endModeCountDesc': '指定次数',
  'calendar.editor.endModeDate': '至某日',
  'calendar.editor.endModeDateDesc': '到期截止',
  'calendar.editor.recurrenceCountPrefix': '共',
  'calendar.editor.recurrenceCountSuffix': '次后结束',
  'calendar.editor.recurrenceUntilPrefix': '截止到',

  // ── 提醒手风琴 ──
  'calendar.editor.loadingDots': '加载中...',
  'calendar.editor.reminderLeadTime': '提前时间',
  'calendar.editor.noReminder': '不提醒',
  'calendar.editor.customDots': '自定义...',
  'calendar.editor.reminderTypes': '提醒方式',
  'calendar.editor.reminderTypesOption': '（可多选）',
  'calendar.editor.reminderTypesAria': '提醒方式（可多选）',
  'calendar.editor.reminderDefaultHint': '未勾选 = 默认（站内 + 邮件）',
  'calendar.editor.reminderLeadShort': '提前',
  'calendar.editor.minutesBefore': '分钟前',
  'calendar.editor.approxDays': '≈ {count} 天',
  'calendar.editor.approxHours': '≈ {count} 小时',
  'calendar.editor.reminderTypesHelp': '提醒方式：站内通知（弹窗）/ 邮件提醒（发送至参会人邮箱）/ 短信提醒。需提前时间为「准时」时不触发。',
  'calendar.editor.emailReminderNeedsAttendee': '⚠ 邮件提醒需要添加参会人邮箱，请在下方参会人面板添加。',

  // ── 参会人手风琴 ──
  'calendar.editor.attendees': '参会人',
  'calendar.editor.attendeesAdded': '已添加 {count} 位参会人',
  'calendar.editor.clearAllAttendeesAria': '清空全部参会人',
  'calendar.editor.clearAll': '清空全部',
  'calendar.editor.removeAttendee': '删除 {name}',
  'calendar.editor.attendeeInputHint': '支持粘贴邮箱（回车/逗号/空格分隔），保存时自动去重；同时作为提醒通知接收邮箱。',
  'calendar.editor.attendeeInputPlaceholder': '输入邮箱（回车添加，支持粘贴多个）',
  'calendar.editor.invalidEmailSingle': '「{email}」不是有效的邮箱格式',
  'calendar.editor.invalidEmailsMany': '存在 {count} 个无效邮箱：{list}',
  'calendar.editor.invalidEmailsIgnored': '已忽略 {count} 个无效邮箱：{list}',
  'calendar.editor.listSeparator': '、',

  // ── 底部操作栏 / 删除确认 / 模板模态框 ──
  'calendar.editor.deletingDots': '删除中...',
  'calendar.editor.confirmDelete': '确认删除',
  'calendar.editor.confirmDeleteMessage': '确定要删除「{title}」吗？此操作不可撤销。',

  // ══ views —— 列表 / 月 / 周 / 日 / 概览视图（`views/*`） ══

  // ── 跨视图共用（MonthView / DayView / WeekView 逐字相同） ──
  // 星期前缀：中文 '周' + 单字缩写；英文无前缀（缩写本身即 Sun/Mon/...）
  'calendar.views.shared.weekdayHeader': '周{day}',
  // 日期徽章：中文 '23日'（月首为 '9月1日'，走 month.monthDay）
  'calendar.views.shared.daySuffix': '{day}日',
  // 多个事件名拼接分隔符（拖拽冲突徽章 / 硬冲突 toast 共用）
  'calendar.views.shared.nameSeparator': '、',
  // 拖拽硬冲突 toast 里紧贴右直角引号后的「等」（无前导空格）
  'calendar.views.shared.etc': '等',
  'calendar.views.shared.dropBlocked': '无法放置：{summary}',
  'calendar.views.shared.dropConflictWithNames': '与「{names}」{etc} 重叠 {minutes} 分钟',
  'calendar.views.shared.dropConflictNoNames': '存在 {count} 项严重冲突',
  // MonthView 专属变体（比 DayView/WeekView 多一段重叠分钟数，逐字不同故独立成键）
  'calendar.views.shared.dropConflictNoNamesWithOverlap': '存在 {count} 项严重冲突（重叠 {minutes} 分钟）',

  // ── ListView.tsx ──
  'calendar.views.list.presetToday': '今日',
  'calendar.views.list.presetWeek': '本周',
  'calendar.views.list.presetMonth': '本月',
  'calendar.views.list.colTime': '时间',
  // 列头「重复」与单元格内 recLabel 为空时的兜底文案逐字相同，共用一个键
  'calendar.views.list.recurrence': '重复',
  'calendar.views.list.reminderMinutes': '提前 {count} 分钟',
  'calendar.views.list.reminderHours': '提前 {count} 小时',
  'calendar.views.list.reminderDays': '提前 {count} 天',
  'calendar.views.list.columnSettingsTitle': '显示列',
  'calendar.views.list.restoreDefaults': '恢复默认',
  'calendar.views.list.markComplete': '标记完成',
  'calendar.views.list.actionDuplicate': '复制',
  'calendar.views.list.selectItem': '选择',
  'calendar.views.list.paginationRange': '显示 {start}–{end}，共 {total} 条',
  'calendar.views.list.pageIndicator': '第 {page} / {total} 页',
  'calendar.views.list.dateRangeSeparator': '至',
  'calendar.views.list.searchPlaceholder': '搜索标题/描述/参与人...',
  'calendar.views.list.allTypes': '全部类型',
  'calendar.views.list.allStatuses': '全部状态',
  'calendar.views.list.columnSettings': '列设置',
  'calendar.views.list.emptyTitle': '暂无日程事件',
  'calendar.views.list.undoComplete': '完成「{title}」',
  'calendar.views.list.toastCompleted': '已标记完成',
  'calendar.views.list.toastCompleteFailed': '操作失败，请重试',
  'calendar.views.list.confirmDelete': '确定删除"{title}"吗？',
  'calendar.views.list.undoDelete': '删除「{title}」',
  'calendar.views.list.toastDeleted': '已删除',
  'calendar.views.list.toastDeleteFailed': '删除失败，请重试',
  'calendar.views.list.toastDuplicated': '已复制',
  'calendar.views.list.toastDuplicateFailed': '复制失败，请重试',
  'calendar.views.list.selectPage': '全选本页',
  'calendar.views.list.deselectPage': '取消全选本页',
  'calendar.views.list.partiallySelected': '（部分选中）',

  // ── MonthView.tsx ──
  // 每月 1 号显示 'M月D日'，其余显示 'D日'（见 shared.daySuffix）
  'calendar.views.month.monthDay': '{month}月{day}日',
  // 农历初一：'{月名}月初一'；月名由 lunar-javascript 提供（中文）
  'calendar.views.month.lunarFirstDay': '{month}月初一',
  'calendar.views.month.ariaDate': '{year}年{month}月{day}日',
  'calendar.views.month.ariaOutsideMonth': '非当前显示月',
  'calendar.views.month.ariaHardConflict': '时间冲突 {count} 条中至少 1 条硬冲突',
  'calendar.views.month.ariaSoftConflict': '存在轻度时间冲突',
  'calendar.views.month.ariaEventCount': '共 {count} 个事件',
  // aria 句子拼接分隔符（中文全角逗号；英文用 ', '）
  'calendar.views.month.ariaSeparator': '，',
  'calendar.views.month.doubleClickHint': '双击新建事件',
  'calendar.views.month.hardConflictDot': '当日存在严重冲突日程',
  'calendar.views.month.softConflictDot': '当日存在边缘冲突日程',
  'calendar.views.month.severityHard': '严重',
  'calendar.views.month.severitySoft': '边缘',
  'calendar.views.month.dragConflictTitle': '预测{severity}冲突 {count} 项 · 重叠 {minutes} 分钟',
  'calendar.views.month.dragConflictWith': ' · 涉及：{names}{etc}',
  // 拖拽徽章里「等」带前导空格（' 等'），与 shared.etc（'等'，紧贴「」）逐字不同故独立成键
  'calendar.views.month.dragMoreSuffix': ' 等',
  'calendar.views.month.conflictCountBadge': '{count} 冲突',
  'calendar.views.month.conflictBadge': '冲突',
  'calendar.views.month.dragHardConflictAria': '拖拽落点存在严重冲突，禁止放置',
  'calendar.views.month.dragSoftConflictAria': '拖拽落点存在边缘冲突',
  'calendar.views.month.gridAriaLabel': '月视图日历网格，共 6 行 7 列，上下方向键可切换相邻日期单元格',
  'calendar.views.month.emptyTitle': '本月暂无日程',
  'calendar.views.month.gotoDayView': '跳转到日视图',
  'calendar.views.month.copyDate': '复制日期',
  'calendar.views.month.dateCopied': '日期 {date} 已复制到剪贴板',
  'calendar.views.month.dateCopiedFallback': '日期 {date} 已复制（兼容模式）',
  'calendar.views.month.copyFailed': '复制失败，请手动选中复制',

  // ── DayView.tsx ──
  'calendar.views.day.yearMonth': '{year}年{month}月',
  'calendar.views.day.emptyTitle': '今日暂无日程',

  // ── WeekView.tsx ──
  'calendar.views.week.emptyTitle': '本周暂无日程',

  // ── Overview.tsx ──
  'calendar.views.overview.barTooltip': '{date}：{count} 个事件',
  'calendar.views.overview.todayWithDate': '今天 {year}年{month}月{day}日 周{weekday}',
  'calendar.views.overview.createEvent': '创建事件',
  'calendar.views.overview.listView': '列表视图',
  'calendar.views.overview.kpiToday': '今日事件',
  'calendar.views.overview.kpiCompletedThisWeek': '本周已完成',
  'calendar.views.overview.kpiConflicts': '冲突事件',
  'calendar.views.overview.kpiTomorrow': '明日日程',
  'calendar.views.overview.weekDistributionTitle': '近 7 天事件分布',
  'calendar.views.overview.weekDistributionHint': '点击任一条可跳转至对应日视图',
  'calendar.views.overview.live': '实时',
  'calendar.views.overview.trendTitle': '周事件趋势',
  'calendar.views.overview.trendHint': '历史 7 天事件量 mini trend',
  'calendar.views.overview.sparklineTooltip': '近 7 天第 {day} 天：{count} 个事件',
  'calendar.views.overview.upcomingTitle': '即将到来的事件',
  'calendar.views.overview.upcomingHint': '点击任意行可编辑该事件',
  'calendar.views.overview.upcomingCount': '共 {count} 条',
  'calendar.views.overview.upcomingEmpty': '暂无即将到来的事件',
  'calendar.views.overview.createFirstEvent': '+ 创建第一个事件',

  // ══ panels —— Outlook 同步 / 订阅 / 面板共享件（`panels/*`） ══

  // ═══════════ OutlookSyncPanel —— 同步方向选项 ═══════════
  'calendar.panels.outlook.dirPushOnlyLabel': '仅推送',
  'calendar.panels.outlook.dirPushOnlyDesc': '系统 → Outlook',
  'calendar.panels.outlook.dirPullOnlyLabel': '仅拉取',
  'calendar.panels.outlook.dirPullOnlyDesc': 'Outlook → 系统',
  'calendar.panels.outlook.dirBothLabel': '双向同步',
  'calendar.panels.outlook.dirBothDesc': '推送 + 拉取',

  // ═══════════ 连接状态卡片 ═══════════
  // 连接中：'已连接 ' + 邮箱（值本身是动态数据）
  'calendar.panels.outlook.connectedPrefix': '已连接 ',
  'calendar.panels.outlook.notConnected': '未连接 Outlook',
  'calendar.panels.outlook.loadingStatus': '加载同步状态...',
  'calendar.panels.outlook.connectedHint': '双向同步可用，事件变更将按配置自动流转',
  'calendar.panels.outlook.notConnectedHint': '需通过微软 OAuth 授权后才能进行双向同步',
  'calendar.panels.outlook.syncing': '同步中...',
  'calendar.panels.outlook.configureCredentials': '配置 Azure AD 凭据',

  // ═══════════ 未连接：能力说明卡片 ═══════════
  'calendar.panels.outlook.capabilitiesTitle': '双向同步能力说明',
  'calendar.panels.outlook.pushBold': '推送：',
  'calendar.panels.outlook.pushDesc': '系统内新建或修改的事件，会自动推送到 Outlook 日历',
  'calendar.panels.outlook.pullBold': '拉取：',
  'calendar.panels.outlook.pullDesc': 'Outlook 中新建或修改的事件，也会拉取回系统进行管理',
  'calendar.panels.outlook.conflictBold': '冲突处理：',
  'calendar.panels.outlook.conflictDesc': '内置冲突检测机制，默认以最后修改者为准，保留双方变更',

  // ═══════════ 未连接：Azure AD 未配置警告 ═══════════
  'calendar.panels.outlook.notEnabledTitle': 'Outlook 同步未启用',
  // 该段被 <code> 环境变量名切成多个文本节点，故拆键；值保留 JSX 折行处的空格
  'calendar.panels.outlook.azureAdHint': 'Azure AD（Microsoft Entra ID）凭据尚未配置，无法发起 OAuth 授权。 请点击上方「配置 Azure AD 凭据」按钮完成自己的凭据配置，或在服务器环境变量中配置',
  'calendar.panels.outlook.codeSeparator': '、',
  'calendar.panels.outlook.codeTerminator': '。',

  // ═══════════ 未连接：连接按钮 ═══════════
  'calendar.panels.outlook.connectDisabledTitle': 'Outlook 同步未配置，请先配置 Azure AD 凭据',
  'calendar.panels.outlook.connecting': '正在连接 Outlook...',
  'calendar.panels.outlook.connectOAuth': '连接 Outlook 账户（OAuth）',
  'calendar.panels.outlook.notConfiguredShort': 'Outlook 同步未配置',

  // ═══════════ 已连接：同步方向 / 自动同步 / 上次同步 ═══════════
  'calendar.panels.outlook.directionLabel': '同步方向',
  'calendar.panels.outlook.autoSyncTitle': '启用自动同步',
  'calendar.panels.outlook.autoSyncHint': '关闭后将停止定时双向同步（仍可手动「立即同步」触发）',
  'calendar.panels.outlook.lastSyncLabel': '上次同步时间：',
  'calendar.panels.outlook.neverSynced': '从未同步，建议先点击「立即同步」',

  // ═══════════ 已连接：操作按钮 ═══════════
  'calendar.panels.outlook.saveSettings': '保存设置',
  'calendar.panels.outlook.syncNow': '立即同步',
  'calendar.panels.outlook.disconnect': '断开连接',

  // ═══════════ 同步结果面板 ═══════════
  'calendar.panels.outlook.syncComplete': '同步完成',
  'calendar.panels.outlook.durationLabel': '耗时 ',
  'calendar.panels.outlook.metricPushed': '推送 Outlook',
  'calendar.panels.outlook.metricPulled': '从 Outlook 拉取',
  'calendar.panels.outlook.metricUpdated': '更新记录',
  'calendar.panels.outlook.metricConflicts': '冲突 / 跳过',
  'calendar.panels.outlook.unknownError': '未知错误',

  // ═══════════ 凭据配置模态窗 ═══════════
  'calendar.panels.outlook.credentialsSubtitle': 'Outlook 同步所需的 Microsoft Entra ID 应用注册凭据',
  'calendar.panels.outlook.loadingCredentials': '加载配置...',
  'calendar.panels.outlook.loadCredentialsFailed': '加载配置失败',
  'calendar.panels.outlook.credentialsConfigured': '凭据已配置',
  'calendar.panels.outlook.credentialsNotConfigured': '凭据未配置',
  'calendar.panels.outlook.sourceLabel': '配置来源：',
  // 后端 OutlookAdminSettingsDto.Source 产出码：user / appsettings（按码取词，不直出后端值）
  'calendar.panels.outlook.sourceUser': '用户自配置',
  'calendar.panels.outlook.sourceAppsettings': 'appsettings / 环境变量',
  'calendar.panels.outlook.updatedAtPrefix': ' · 更新于 ',

  // ── 表单字段 ──
  'calendar.panels.outlook.clientIdLabel': 'Client ID（应用程序客户端 ID）',
  'calendar.panels.outlook.clientIdPlaceholder': 'Azure 门户 → 应用注册 → 概述 → 应用程序(客户端) ID',
  'calendar.panels.outlook.clientSecretLabel': 'Client Secret（客户端密码）',
  'calendar.panels.outlook.clientSecretConfiguredPlaceholder': '已配置（留空表示不更新，输入新值将覆盖）',
  'calendar.panels.outlook.clientSecretPlaceholder': 'Azure 门户 → 证书和密码 → 新客户端密码',
  'calendar.panels.outlook.clearSecret': '清空已配置的 ClientSecret',
  'calendar.panels.outlook.tenantIdLabel': 'Tenant ID（目录租户 ID）',
  'calendar.panels.outlook.tenantIdPlaceholder': 'common（多租户）或 Azure 门户 → 概述 → 目录(租户) ID',
  'calendar.panels.outlook.redirectUriLabel': 'Redirect URI（回调地址）',
  'calendar.panels.outlook.redirectUriPlaceholder': 'https://<你的域名>/api/outlooksync/oauth/callback（需与 Azure 应用注册一致）',
  'calendar.panels.outlook.scopesLabel': 'Scopes（权限范围）',
  'calendar.panels.outlook.scopesPlaceholder': 'Calendars.ReadWrite User.Read offline_access（空格分隔）',

  // ── 反馈与保存 ──
  'calendar.panels.outlook.noChanges': '配置无变更',
  'calendar.panels.outlook.credentialsSaved': '配置已保存，新凭据即时生效',
  'calendar.panels.outlook.clearSecretConfirm': '确认清空已配置的 ClientSecret？清空后 Outlook 同步将无法发起 OAuth 授权。',
  'calendar.panels.outlook.secretCleared': 'ClientSecret 已清空',
  'calendar.panels.outlook.clearSecretFailed': '清空 ClientSecret 失败',
  'calendar.panels.outlook.saveConfiguration': '保存配置',
  'calendar.panels.outlook.doneBack': '完成，返回同步面板 →',

  // ── Azure 门户配置指引（<code> 元素切分，故拆前后缀键） ──
  'calendar.panels.outlook.guideTitle': '配置指引：',
  'calendar.panels.outlook.guideStep1Prefix': '1. 登录 ',
  'calendar.panels.outlook.guideStep1Suffix': ' → Microsoft Entra ID → 应用注册 → 新注册',
  'calendar.panels.outlook.guideStep2': '2. 重定向 URI 类型选「Web」，值填上方 Redirect URI',
  'calendar.panels.outlook.guideStep3': '3. 证书和密码 → 新客户端密码 → 复制「值」（非密码 ID）',
  'calendar.panels.outlook.guideStep4Prefix': '4. API 权限 → 添加 ',
  'calendar.panels.outlook.guideStep4Suffix': '（委托权限）',
  'calendar.panels.outlook.guideStep5': '5. 复制概述页的「应用程序(客户端) ID」与「目录(租户) ID」填入上方表单',

  // ═══════════ SubscriptionPanel —— 日历订阅 ═══════════
  'calendar.panels.subscription.title': '日历订阅',
  'calendar.panels.subscription.subtitle': '通过 iCal 链接同步到外部日历应用',
  'calendar.panels.subscription.autoSyncTitle': '一键自动同步',
  'calendar.panels.subscription.standardBadge': 'iCal / .ics 标准',
  'calendar.panels.subscription.bullet1': '将链接复制到 Outlook / Apple 日历 / Google 日历 即可自动同步',
  'calendar.panels.subscription.bullet2': '订阅链接是只读的，外部应用无法修改系统内的事件',
  'calendar.panels.subscription.bullet3': '重新生成可使旧链接立即失效，保护日程隐私',
  'calendar.panels.subscription.linkLabel': '订阅链接',
  'calendar.panels.subscription.copiedLabel': '已复制 ✓',
  'calendar.panels.subscription.copyLink': '复制链接',
  'calendar.panels.subscription.regenerate': '重新生成',
  'calendar.panels.subscription.loadingLink': '正在加载订阅链接...',
  'calendar.panels.subscription.emptyTitle': '尚未生成订阅链接',
  'calendar.panels.subscription.emptyHint': '点击下方按钮生成专属的 iCal 订阅链接，可同步到 Outlook / Apple 日历 / Google 日历',
  'calendar.panels.subscription.generateLink': '立即生成订阅链接',
  'calendar.panels.subscription.copiedToast': '订阅链接已复制到剪贴板',
  'calendar.panels.subscription.copiedFallbackToast': '订阅链接已复制到剪贴板（兼容模式）',
  'calendar.panels.subscription.copyFailedToast': '复制失败，请手动选中文本复制',
  'calendar.panels.subscription.regeneratedToast': '已生成新的订阅链接，旧链接已立即失效',
  'calendar.panels.subscription.regenerateFailedToast': '重新生成订阅链接失败',

  // ═══════════ shared.tsx —— 侧边面板共享基础组件 ═══════════
  // 关闭按钮 aria-label 用 common.close，提示关闭用 calendar.common.closeHint，此处仅剩开关兜底
  'calendar.panels.shared.toggleAriaLabel': '切换开关',

  // ══ trash —— 垃圾箱（`panels/TrashPanel.tsx` / `views/TrashView.tsx` 共用同一批键） ══

  // ── 行内动作（表格行与移动端卡片共用） ──
  'calendar.trash.restore': '恢复',
  'calendar.trash.hardDelete': '彻底删除',

  // ── 工具栏 ──
  'calendar.trash.searchPlaceholder': '搜索已删除的标题、地点或备注...',
  'calendar.trash.filterHint': '过滤：标题/地点全文匹配',
  'calendar.trash.restoreAll': '恢复全部可恢复',
  'calendar.trash.clearTrash': '清空垃圾箱',
  'calendar.trash.purgeExpired': '清理过期记录',

  // ── 表头 / 空状态 ──
  'calendar.trash.colTitleLocation': '标题 / 地点',
  'calendar.trash.colDeletedAt': '删除时间',
  'calendar.trash.colActions': '操作',
  'calendar.trash.emptyTitle': '垃圾箱为空',
  'calendar.trash.emptyHint': '已删除的事件会在此处保留一段时间，可随时恢复',

  // ── 计数句（拆键以保留插值值自身的 <span> 强调样式） ──
  // 面板与页面视图共用 '共 ' 前缀（后接带样式的条数 span）
  'calendar.trash.countPrefix': '共 ',
  // 分页句：共 {count} 条，第 {page} / {total} 页
  'calendar.trash.paginationMiddle': ' 条，第 ',
  'calendar.trash.paginationSuffix': ' 页',
  // 面板标题副标题（模板字符串，无样式元素，用插值）
  'calendar.trash.headerSubtitle': '共 {count} 条，保留 {days} 天',
  // 页面视图页头：共 {count} 条已删除事件，保留 {days} 天后自动清理
  'calendar.trash.viewHeaderMiddle': ' 条已删除事件，保留 ',
  'calendar.trash.viewHeaderSuffix': ' 天后自动清理',

  // ── 恢复确认 ──
  'calendar.trash.restoreDialogTitle': '恢复事件',
  'calendar.trash.restoreConfirmMsg': '确定要恢复事件「{title}」吗？',

  // ── 永久删除确认 ──
  'calendar.trash.hardDeleteDialogTitle': '⚠️ 危险操作：永久删除事件',
  'calendar.trash.hardDeleteConfirmMsg': '该操作不可恢复，将从数据库中永久删除。\n\n确认删除事件「{title}」。',
  'calendar.trash.permanentDelete': '永久删除',
  'calendar.trash.hardDeleteWarn': '删除后无法恢复，事件数据将从系统中彻底移除',

  // ── 清空垃圾箱确认 ──
  'calendar.trash.clearDialogTitle': '⚠️ 危险操作：清空垃圾箱',
  'calendar.trash.clearConfirmMsg': '该操作不可恢复，将从数据库中永久删除。\n\n本次将清空垃圾箱内全部事件，共 {count} 条。',
  'calendar.trash.confirmClear': '确认清空',
  'calendar.trash.clearWarn': '清空后垃圾箱内所有事件将被永久删除，无法恢复',

  // ── 按期清理确认 ──
  'calendar.trash.purgeDialogTitle': '⚠️ 按期清理回收站',
  'calendar.trash.purgeConfirmMsg': '将永久删除删除时间早于指定天数前的回收站事件。不可恢复。',
  'calendar.trash.confirmPurge': '确认清理',
  'calendar.trash.purgeWarn': '清理后符合条件的回收站事件将被永久删除，无法恢复',
  'calendar.trash.retentionLabel': '保留天数：',
  'calendar.trash.retentionUnit': '天（0～3650）',

  // ══ store —— `store.ts` / `calendarUtils.ts` / `hooks.ts`（非组件模块，`tBound` 取词） ══

  // ── 事件类型（EVENT_TYPE_STYLE 8 码 / EVENT_TYPE_OPTIONS 4 码，label 逐字相同故共用） ──
  'calendar.store.eventType.dueDate': '截止日期',
  'calendar.store.eventType.task': '任务',
  'calendar.store.eventType.reminder': '提醒',
  'calendar.store.eventType.meeting': '会议',
  'calendar.store.eventType.personal': '个人',
  'calendar.store.eventType.travel': '出行',
  'calendar.store.eventType.work': '工作',
  'calendar.store.eventType.other': '其他',

  // ── 事件状态 ──
  // STATUS_STYLE 的 pending 是「待办」（chip 徽章），STATUS_OPTIONS 的 pending 是「待处理」（下拉选项），
  // 两者逐字不同 → 必须两条；其余 4 码两张表逐字相同 → 共用。
  'calendar.store.status.pending': '待办',
  'calendar.store.status.pendingOption': '待处理',
  'calendar.store.status.inProgress': '进行中',
  'calendar.store.status.completed': '已完成',
  'calendar.store.status.cancelled': '已取消',
  'calendar.store.status.missed': '已错过',

  // ── 优先级（日历自有 1-5；与会话优先级 format.priority.* 语义不同，不混用） ──
  'calendar.store.priority.unset': '未设置',
  'calendar.store.priority.low': '低',
  'calendar.store.priority.lower': '较低',
  'calendar.store.priority.medium': '中',
  'calendar.store.priority.higher': '较高',
  'calendar.store.priority.high': '高',

  // ── 提醒预设（REMINDER_OPTIONS 的 label） ──
  'calendar.store.reminderOption.onTime': '准时',
  'calendar.store.reminderOption.minutes5': '5 分钟前',
  'calendar.store.reminderOption.minutes15': '15 分钟前',
  'calendar.store.reminderOption.minutes30': '30 分钟前',
  'calendar.store.reminderOption.hours1': '1 小时前',
  'calendar.store.reminderOption.days1': '1 天前',
  'calendar.store.reminderOption.weeks1': '1 周前',

  // ── 提醒兜底文案（非预设值的动态拼接；空格逐字保留） ──
  'calendar.store.reminder.onTime': '准时提醒',
  'calendar.store.reminder.minutesAgo': '{minutes} 分钟前',
  'calendar.store.reminder.hoursAgo': '{hours} 小时前',
  'calendar.store.reminder.daysAgo': '{days} 天前',

  // ── 提醒方式（REMINDER_TYPE_OPTIONS / REMINDER_TYPE_LABELS 共用同一批值） ──
  'calendar.store.reminderType.popup': '站内通知',
  'calendar.store.reminderType.email': '邮件提醒',
  'calendar.store.reminderType.sms': '短信提醒',
  // reminderTypesLabel 的空值兜底（全角括号，逐字保留）
  'calendar.store.reminderType.default': '默认（站内+邮件）',

  // ── 重复类型（RECURRENCE_TYPE_OPTIONS） ──
  'calendar.store.recurrenceType.daily': '每日',
  'calendar.store.recurrenceType.weekly': '每周',
  'calendar.store.recurrenceType.monthly': '每月',
  'calendar.store.recurrenceType.yearly': '每年',

  // ── 重复单位（RECURRENCE_UNIT_LABELS；英文为 { one, other } 复数词条） ──
  'calendar.store.recurrenceUnit.day': '天',
  'calendar.store.recurrenceUnit.week': '周',
  'calendar.store.recurrenceUnit.month': '月',
  'calendar.store.recurrenceUnit.year': '年',

  // ── 重复描述句（getRecurrenceLabel / generateRRuleDescription 共用后缀） ──
  'calendar.store.recurrence.every': '每 {interval} {unit}',
  'calendar.store.recurrence.countSuffix': '，共 {count} 次',
  'calendar.store.recurrence.untilSuffix': '，至 {date}',

  // ── RRULE 中文描述构造（generateRRuleDescription 的三段拼接，顺序与空格逐字保留） ──
  'calendar.store.rrule.everyPrefix': '每',
  // 间隔 > 1 时插入的中缀（含尾随空格）
  'calendar.store.rrule.intervalMiddle': '{interval} ',
  'calendar.store.rrule.weekdayPrefix': '周',
  'calendar.store.rrule.weekdaySeparator': '、',

  // ── 时长文案（getDurationLabel / getEventDuration；空格与「共」前缀逐字保留） ──
  'calendar.store.duration.allDayDays': '共 {days} 天',
  'calendar.store.duration.daysHours': '共 {days} 天 {hours} 小时',
  'calendar.store.duration.hoursOnly': '共 {hours} 小时',
  // 仅当 minutes > 0 时追加，**含前导空格**
  'calendar.store.duration.minutesSuffix': ' {minutes} 分钟',
  'calendar.store.duration.minutesOnly': '共 {minutes} 分钟',
  // getEventDuration 的紧凑格式（无空格）
  'calendar.store.duration.compactMinutes': '{minutes}分钟',
  'calendar.store.duration.compactHoursMinutes': '{hours}小时{minutes}分',
  'calendar.store.duration.compactHours': '{hours}小时',
  'calendar.store.duration.compactDaysHours': '{days}天{hours}小时',
  'calendar.store.duration.compactDays': '{days}天',

  // ── 颜色选项（COLOR_OPTIONS；纯前端调色板，后端 Color 为自由 hex 字符串） ──
  'calendar.store.color.red': '红色',
  'calendar.store.color.rose': '玫红',
  'calendar.store.color.crimson': '深红',
  'calendar.store.color.orange': '橙色',
  'calendar.store.color.amber': '琥珀',
  'calendar.store.color.yellow': '黄色',
  'calendar.store.color.gold': '金棕',
  'calendar.store.color.lime': '青柠',
  'calendar.store.color.brightGreen': '亮绿',
  'calendar.store.color.emerald': '翡翠',
  'calendar.store.color.darkGreen': '深绿',
  'calendar.store.color.teal': '青色',
  'calendar.store.color.sky': '天蓝',
  'calendar.store.color.lightBlue': '浅蓝',
  'calendar.store.color.blue': '蓝色',
  'calendar.store.color.darkBlue': '深蓝',
  'calendar.store.color.indigo': '靛蓝',
  'calendar.store.color.purple': '紫色',
  'calendar.store.color.brightPurple': '亮紫',
  'calendar.store.color.magenta': '品红',
  'calendar.store.color.pink': '粉色',
  'calendar.store.color.slate': '石板灰',
  'calendar.store.color.warmGray': '暖灰',
  'calendar.store.color.coffee': '咖啡',
  'calendar.store.color.neonLime': '荧光青柠',
  'calendar.store.color.wine': '酒红',

  // ── 农历（lunar-javascript 只产出中文；月名由库提供，故作为插值传入） ──
  'calendar.store.lunar.monthFirstDay': '{month}月初一',

  // ── store.ts：同步 / 垃圾箱 / 撤销重做的 transientToast 与 undoToast ──
  'calendar.store.toast.pendingPatchExpired': '有 {count} 条日程修改本地待同步超过 10 分钟，已保留原位置',
  'calendar.store.toast.outlookStatusFailed': '获取 Outlook 同步状态失败',
  'calendar.store.toast.outlookNotConfigured': 'Outlook 同步未配置 Azure AD 凭据，请联系管理员',
  'calendar.store.toast.outlookAuthorizeUrlFailed': '获取 Outlook 授权链接失败',
  'calendar.store.toast.outlookDisconnected': '已断开与 Outlook 的连接',
  'calendar.store.toast.outlookDisconnectFailed': '断开连接失败',
  'calendar.store.toast.outlookSettingSaved': 'Outlook 同步配置已保存',
  'calendar.store.toast.outlookSyncDone': '同步完成：推送 {pushed} 条，拉回 {pulled} 条，冲突 {conflicts} 条',
  'calendar.store.toast.staleRestoreRetry': '数据已被其他用户修改，已刷新最新版本，请再次点击恢复',
  'calendar.store.toast.staleRestoreRetryShort': '数据已被其他用户修改，已刷新，请再次点击恢复',
  'calendar.store.toast.restoreFailed': '恢复失败',
  'calendar.store.toast.restoredCount': '已恢复 {count} 个事件',
  'calendar.store.toast.someEventsNotRestored': '有事件无法恢复',
  'calendar.store.toast.noRestorableEvents': '没有可恢复的事件',
  'calendar.store.toast.unknownReason': '未知原因',
  'calendar.store.toast.eventsNotRestored': '{count} 个事件无法恢复：{reason}',
  'calendar.store.toast.batchRestoreFailed': '批量恢复失败',
  'calendar.store.toast.staleHardDeleteRetry': '数据已被其他用户修改，已刷新最新版本，请再次点击彻底删除',
  'calendar.store.toast.hardDeleteFailed': '彻底删除失败',
  'calendar.store.toast.clearTrashFailed': '清空垃圾箱失败',
  'calendar.store.toast.purgeTrashFailed': '清理过期记录失败',

  // ── 409 时间冲突 toast 的三段拼接（分隔符 / 前缀 / 「等 N 个」后缀） ──
  'calendar.store.conflict.titleSeparator': '、',
  'calendar.store.conflict.prefix': '时间冲突：',
  // 仅当冲突数 > 3 时追加，**含前导空格**
  'calendar.store.conflict.more': ' 等{count}个',

  // ── 撤销 / 重做（undoStack description 与 toast） ──
  'calendar.store.undo.move': '移动事件',
  'calendar.store.undo.resize': '调整时长',
  'calendar.store.undo.undoneToast': '已撤销: {description}',
  'calendar.store.undo.failPrefix': '撤销失败——',
  'calendar.store.undo.failed': '撤销失败',
  'calendar.store.redo.redoneToast': '已重做: {description}',
  'calendar.store.redo.failPrefix': '重做失败——',
  'calendar.store.redo.failed': '重做失败',

  // ══ shell —— 页面外壳 / 事件条 / 批量条 / 过滤器 ══

  // ── Calendar.tsx · 视图名 / 工具条 / 导航 ──
  'calendar.shell.page.viewCalendar': '日历',
  'calendar.shell.page.viewList': '列表',
  'calendar.shell.page.viewSubscription': '订阅',
  'calendar.shell.page.prevPeriod': '上一周期',
  'calendar.shell.page.nextPeriod': '下一周期',
  'calendar.shell.page.subViewMonth': '月',
  'calendar.shell.page.subViewWeek': '周',
  'calendar.shell.page.subViewDay': '天',
  // 日期拼接（逐字保留原字面量里的空格/连字符，故不合并成一条）
  'calendar.shell.page.navMonth': '{year}年 {month}月',
  'calendar.shell.page.navWeek': '{startYear}年{startMonth}月{startDay}日 - {endMonth}月{endDay}日',
  'calendar.shell.page.navDay': '{year}年{month}月{day}日 {weekday}',
  'calendar.shell.page.shortcutN': '快捷键 N',
  'calendar.shell.page.newEventOffline': '当前处于离线状态，新建事件已禁用',
  'calendar.shell.page.undo': '撤销',

  // ── Calendar.tsx · 状态横幅 / 加载失败 ──
  'calendar.shell.page.signalRReconnecting': '实时通知连接中断，正在自动重连（退避重试）… 提醒/撤销/冲突广播将延后到恢复。',
  'calendar.shell.page.signalROffline': '实时通知服务离线。刷新页面或稍后重试即可重新建立连接。',
  'calendar.shell.page.viewLoadFailed': '日历视图加载失败',
  'calendar.shell.page.viewLoadFailedDesc': '该视图模块发生异常，已自动隔离。可点击恢复按钮重新加载。',

  // ── Calendar.tsx · 未捕获异常 toast ──
  'calendar.shell.page.unexpectedError': '页面发生未预期的错误',
  'calendar.shell.page.uncaughtErrorToast': '出现异常：{message}（已记录，请稍后重试或刷新）',
  'calendar.shell.page.asyncTaskFailed': '网络请求或异步任务失败',
  'calendar.shell.page.asyncOperationFailed': '异步操作失败',
  'calendar.shell.page.operationFailed': '操作失败：{message}',

  // ── Calendar.tsx · ICS 导入 / 导出 ──
  'calendar.shell.page.importExportIcsTitle': '导入 / 导出 ICS',
  'calendar.shell.page.importExportIcsAria': '导入导出 ICS',
  'calendar.shell.page.importExportSection': '导入/导出',
  'calendar.shell.page.importing': '导入中...',
  'calendar.shell.page.importIcsFile': '导入 ICS 文件',
  'calendar.shell.page.preparingExport': '准备导出...',
  'calendar.shell.page.exportRangeIcs': '导出当前范围 ICS',
  'calendar.shell.page.subscribeWebcal': '订阅日历（WebCal）',
  'calendar.shell.page.icsImportDone': '导入完成：新增 {imported} 项',
  'calendar.shell.page.icsImportNone': '未新增事件',
  // 前导全角逗号与「跳过」之间的空格逐字保留
  'calendar.shell.page.icsImportSkipped': '，跳过 {skipped} 项',
  'calendar.shell.page.icsImportFailed': '导入失败，请确认是有效的 .ics 文件',
  'calendar.shell.page.icsExporting': '正在导出 ICS 文件',

  // ── Calendar.tsx · 邮箱筛选 pill ──
  'calendar.shell.page.mailboxAll': '全部邮箱',
  'calendar.shell.page.mailboxOne': '1 个邮箱',
  'calendar.shell.page.mailboxCount': '{count} 个邮箱',
  'calendar.shell.page.mailboxIdFallback': '邮箱 #{id}',
  'calendar.shell.page.mailboxFilter': '邮箱筛选',
  'calendar.shell.page.clearMailboxFilter': '清除邮箱筛选',
  'calendar.shell.page.selectAll': '全选',

  // ── Calendar.tsx · 屏幕阅读器实时公告（aria-live 文本） ──
  'calendar.shell.page.announceCleared': '已取消全部选中',
  'calendar.shell.page.announceSelectedOne': '已选中 1 个事件',
  'calendar.shell.page.announceSelectedMany': '批量已选中 {count} 个事件',
  'calendar.shell.page.announceNoConflict': '当前概览范围内无冲突事件',
  'calendar.shell.page.announceConflictOne': '检测到 1 条时间冲突',
  'calendar.shell.page.announceConflictMany': '检测到 {count} 条时间冲突',

  // ── chips.tsx · 事件条（EventChip / AllDayEventBar / HourRow） ──
  // 多个冲突事件名拼接分隔符
  'calendar.shell.chips.listSeparator': '、',
  // 全天跨天条：'{日期} 起 共 N 天'（日期由 formatDate(_, 'short') 提供）
  'calendar.shell.chips.allDaySpan': '{date} 起 共 {days} 天',
  'calendar.shell.chips.conflictWithTitle': '与 {list} 时间重叠',
  'calendar.shell.chips.untitledEvent': '(无标题事件)',
  'calendar.shell.chips.conflictCount': '{count} 冲突',
  // 前导 ' · ' 逐字保留
  'calendar.shell.chips.creatorSuffix': ' · 创建者: {name}',
  'calendar.shell.chips.conflictDetail': '时间冲突 {count} 项 ({kind})',
  'calendar.shell.chips.severeOverlap': '严重重叠',
  'calendar.shell.chips.edgeOverlap': '边缘重叠',
  'calendar.shell.chips.moreConflicts': '+{count} 项',
  'calendar.shell.chips.hoverHint': '单击查看详情 · 拖拽移动到其他日期',
  // aria-label 片段：'{起始分钟} 分开始, 时长 {时长} 分钟'
  'calendar.shell.chips.eventAriaTime': '{offset} 分开始, 时长 {duration} 分钟',
  'calendar.shell.chips.eventAriaSelected': '，已选中',
  'calendar.shell.chips.searchCurrent': '当前搜索："{keyword}"',

  // ── chips.tsx · EmptyState 六种变体的默认文案 ──
  'calendar.shell.chips.empty.month.title': '本月暂无日程',
  'calendar.shell.chips.empty.month.desc': '点击下方按钮创建你的第一个日程',
  'calendar.shell.chips.empty.month.cta': '+ 新建第一个事件',
  'calendar.shell.chips.empty.week.title': '本周暂无安排',
  'calendar.shell.chips.empty.week.desc': '规划你的一周，从此开始',
  'calendar.shell.chips.empty.week.cta': '+ 添加本周安排',
  'calendar.shell.chips.empty.day.title': '今天还没有安排',
  'calendar.shell.chips.empty.day.desc': '一杯咖啡的时间，规划今天',
  'calendar.shell.chips.empty.day.cta': '+ 规划今天',
  'calendar.shell.chips.empty.list.title': '暂无事件',
  'calendar.shell.chips.empty.list.desc': '创建事件后将会在此列表展示',
  'calendar.shell.chips.empty.overview.title': '暂无数据',
  'calendar.shell.chips.empty.overview.desc': '创建事件后即可查看统计概览',
  'calendar.shell.chips.empty.overview.cta': '+ 创建事件开始统计',
  'calendar.shell.chips.empty.search.title': '未找到匹配的事件',
  'calendar.shell.chips.empty.search.desc': '换个关键词试试',
  'calendar.shell.chips.empty.search.cta': '清除搜索',
  // list 变体 CTA 与「无变体」兜底 CTA 逐字相同，共用一个键
  'calendar.shell.chips.empty.newEventCta': '+ 新建事件',

  // ── BatchMultiSelectToolbar.tsx ──
  // 失败原因多个事件名拼接分隔符（与 chips 分属两个文件，逐字相同但各写各的，避免跨文件耦合）
  'calendar.shell.batch.listSeparator': '、',
  'calendar.shell.batch.doneWithFailures': '批量操作完成：成功 {success}，失败 {failed}',
  'calendar.shell.batch.failureReasons': '。失败原因：{reasons}',
  'calendar.shell.batch.done': '批量操作完成：成功 {count} 条',
  'calendar.shell.batch.undoDelete': '批量删除 {count} 项',
  'calendar.shell.batch.undoUpdate': '批量更新 {count} 项',
  'calendar.shell.batch.failed': '批量操作失败：{message}',
  'calendar.shell.batch.selectedCount': '已选 {count} 项',
  'calendar.shell.batch.selectAll': '全选',
  'calendar.shell.batch.changeType': '改类型…',
  'calendar.shell.batch.changeStatus': '改状态…',
  'calendar.shell.batch.clearSelectionHint': '清空当前选择',
  'calendar.shell.batch.noSelectionHint': '未选择任何事件（点击事件进行选择）',
  'calendar.shell.batch.exitMultiSelectHint': '退出多选模式',
  'calendar.shell.batch.exitMultiSelect': '退出多选',
  'calendar.shell.batch.enterMultiSelectHint': '进入多选模式（批量改类型/状态/删除）',
  'calendar.shell.batch.multiSelect': '多选',
  'calendar.shell.batch.deleteConfirm': '确认删除已选的 {count} 条日程？此操作可在"垃圾箱"内 30 天内撤回。',

  // ── EventTypeFilterBar.tsx ──
  'calendar.shell.filter.all': '全部',

  // ── GlobalCalendarReminderListener.tsx ──
  'calendar.shell.reminder.title': '日程提醒',
  // 前导 ' · ' 逐字保留
  'calendar.shell.reminder.minutesAhead': ' · 提前 {minutes} 分钟',
  'calendar.shell.reminder.untitled': '（未命名日程）',
  'calendar.shell.reminder.close': '关闭提醒',
  'calendar.shell.reminder.confirming': '确认中...',
  'calendar.shell.reminder.confirmReceived': '确认已收到',
  'calendar.shell.reminder.viewDetails': '查看详情',

  // ══ a11y —— 无障碍与可靠性文案（`a11yReliability.tsx`） ══

  // ── 错误边界（CalendarErrorBoundary）──
  'calendar.a11y.crashTitle': '页面组件崩溃了',
  'calendar.a11y.crashDescription': '页面的某个模块出现异常，已自动隔离以免影响其他功能。请尝试恢复。',
  'calendar.a11y.recover': '恢复并重新加载',

  // ── 离线检测（OfflineBanner / announceOfflineBlock）──
  'calendar.a11y.offlineBanner': '当前处于离线状态，已禁止新建 / 修改 / 删除操作。网络恢复后自动解除。',
  'calendar.a11y.offlineBlockedRetry': '当前处于离线状态，写操作已被阻断。请恢复网络后再试。',
  'calendar.a11y.offlineBlockedShort': '当前处于离线状态，写操作已被阻断',
} satisfies Entries;
