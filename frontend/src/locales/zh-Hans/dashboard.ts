import type { Entries } from '../../i18n/types';

/**
 * 仪表盘文案。抽取自 `pages/Dashboard.tsx`。
 *
 * 复数约定：仅英文需要区分单复数的词条写成 `{ one, other }`（中文两态相同，写普通字符串）。
 */
export const dashboard = {
  // ── 时长（formatDuration） ──
  'dashboard.duration.lessThanOneMinute': '< 1 分钟',
  'dashboard.duration.minutes': '{count} 分钟',
  'dashboard.duration.hours': '{count} 小时',
  'dashboard.duration.days': '{count} 天',

  // ── 页面外壳与通用 ──
  'dashboard.title': '仪表盘',
  'dashboard.subtitle': '系统整体运营数据概览',
  'dashboard.loadFailed': '加载失败',
  'dashboard.loadFailedWithReason': '加载失败：{message}',
  'dashboard.unknownError': '未知错误',
  'dashboard.viewAll': '查看全部',
  'dashboard.viewDetails': '查看详情 →',
  'dashboard.noSubject': '(无主题)',
  'dashboard.customerAvatarAlt': '客户',

  // ── 开始工作 ──
  'dashboard.getStarted.title': '开始工作',
  'dashboard.getStarted.subtitle': '快速访问常用功能',
  'dashboard.getStarted.viewConversations': '查看会话',
  'dashboard.getStarted.manageCustomers': '客户管理',
  'dashboard.getStarted.auditLog': '审计日志',
  'dashboard.getStarted.settings': '系统设置',

  // ── 顶部统计卡片 ──
  'dashboard.stats.totalConversations': '会话总数',
  'dashboard.stats.activeConversations': '进行中会话',
  'dashboard.stats.newToday': '今日新增',
  'dashboard.stats.closedToday': '今日关闭',
  'dashboard.stats.last7Days': '近 7 日',
  'dashboard.stats.unassignedConversations': '未分配会话',
  'dashboard.stats.avgFirstResponse': '平均首次响应',
  'dashboard.stats.avgResolution': '平均解决时间',
  'dashboard.stats.satisfactionScore': '满意度评分',
  'dashboard.stats.ratingCount': '{count} 条评价',

  // ── 会话状态（饼图 / 列表内标签复用） ──
  'dashboard.status.active': '活跃',
  'dashboard.status.pending': '待处理',
  'dashboard.status.closed': '已关闭',
  'dashboard.status.other': '其他',
  'dashboard.status.inProgress': '进行中',
  'dashboard.status.replies': '回复数',

  // ── SLA 提醒 ──
  'dashboard.sla.breached': 'SLA 已逾期 {count} 个会话',
  'dashboard.sla.breachedHint': '请优先处理逾期的会话',
  'dashboard.sla.warning': 'SLA 即将逾期 {count} 个会话',
  'dashboard.sla.warningHint': '请及时处理避免逾期',

  // ── 未分配会话提醒 ──
  'dashboard.unassigned.count': '有 {count} 个未分配的活跃会话',
  'dashboard.unassigned.hint': '建议尽快分配负责人跟进',

  // ── 功能模块概览 ──
  'dashboard.modules.title': '功能模块概览',
  'dashboard.modules.knowledgeBase': '知识库',
  'dashboard.modules.articlesAndCategories': '篇文章 · {count} 个分类',
  'dashboard.modules.published': '已发布',
  'dashboard.modules.draft': '草稿',
  'dashboard.modules.views': '浏览',
  'dashboard.modules.calendar': '日历',
  'dashboard.modules.todayEvents': '今日事件 · 共 {count}',
  'dashboard.modules.upcoming': '即将',
  'dashboard.modules.overdue': '逾期',
  'dashboard.modules.workflows': '工作流',
  'dashboard.modules.activeRules': '启用规则 · 共 {count}',
  'dashboard.modules.executionsToday': '今日执行',
  'dashboard.modules.succeeded': '成功',
  'dashboard.modules.failed': '失败',
  'dashboard.modules.imQueue': 'IM 队列',
  'dashboard.modules.pendingAndSentToday': '待发送 · 今日 {count}',
  'dashboard.modules.pendingSend': '待发送',
  'dashboard.modules.sending': '发送中',
  'dashboard.modules.sentToday': '今日成功',

  // ── 图表 ──
  'dashboard.charts.statusDistribution': '会话状态分布',
  'dashboard.charts.userWorkload': '用户负载情况',
  'dashboard.charts.mailboxStats': '邮箱统计',
  'dashboard.charts.conversationTrend': '近 7 日会话趋势',
  'dashboard.charts.newConversations': '新增会话 ({count})',
  'dashboard.charts.closedConversations': '关闭会话 ({count})',
  'dashboard.charts.totalConversations': '总会话数',
  'dashboard.charts.trendTooltip': '{date}\n新增: {newCount}\n关闭: {closedCount}',

  // ── 最近会话 ──
  'dashboard.recent.title': '最近会话',
  'dashboard.recent.empty': '暂无会话',

  // ── 置顶报表 ──
  'dashboard.pinnedReports.title': '置顶报表',
  'dashboard.pinnedReports.empty': '暂无置顶报表',
  'dashboard.pinnedReports.create': '去新建',
  'dashboard.pinnedReports.loadFailed': '加载置顶报表失败: {message}',

  // ── 自定义报表分类 / 可见性 ──
  'dashboard.reportCategory.performance': '绩效',
  'dashboard.reportCategory.efficiency': '效率',
  'dashboard.reportCategory.customer': '客户',
  'dashboard.reportCategory.workload': '负载',
  'dashboard.reportVisibility.private': '私有',
  'dashboard.reportVisibility.team': '团队',
  'dashboard.reportVisibility.public': '公开',

  // ── 系统资源 ──
  'dashboard.resources.title': '系统资源',
  'dashboard.resources.totalUsers': '用户总数',
  'dashboard.resources.totalCustomers': '客户总数',
  'dashboard.resources.totalMailboxes': '邮箱总数',
  'dashboard.resources.pendingConversations': '待处理会话',
  'dashboard.resources.totalTags': '标签总数',
  'dashboard.resources.totalCannedResponses': '快捷回复',
  'dashboard.resources.knowledgeLikes': '知识库点赞',
} satisfies Entries;
