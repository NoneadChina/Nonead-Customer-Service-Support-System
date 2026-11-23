import type { Entries } from '../../i18n/types';

/** 仪表盘文案（英文值）。必须用 `satisfies Entries`，不得写成类型标注 */
export const dashboard = {
  // ── Durations (formatDuration) ──
  'dashboard.duration.lessThanOneMinute': '< 1 minute',
  'dashboard.duration.minutes': { one: '{count} minute', other: '{count} minutes' },
  'dashboard.duration.hours': { one: '{count} hour', other: '{count} hours' },
  'dashboard.duration.days': { one: '{count} day', other: '{count} days' },

  // ── Page shell and shared ──
  'dashboard.title': 'Dashboard',
  'dashboard.subtitle': 'Overview of system-wide operations',
  'dashboard.loadFailed': 'Failed to load',
  'dashboard.loadFailedWithReason': 'Failed to load: {message}',
  'dashboard.unknownError': 'Unknown error',
  'dashboard.viewAll': 'View all',
  'dashboard.viewDetails': 'View details →',
  'dashboard.noSubject': '(No subject)',
  'dashboard.customerAvatarAlt': 'Customer',

  // ── Get started ──
  'dashboard.getStarted.title': 'Get started',
  'dashboard.getStarted.subtitle': 'Quick access to common tasks',
  'dashboard.getStarted.viewConversations': 'View conversations',
  'dashboard.getStarted.manageCustomers': 'Manage customers',
  'dashboard.getStarted.auditLog': 'Audit log',
  'dashboard.getStarted.settings': 'Settings',

  // ── Stat cards ──
  'dashboard.stats.totalConversations': 'Total conversations',
  'dashboard.stats.activeConversations': 'Active conversations',
  'dashboard.stats.newToday': 'New today',
  'dashboard.stats.closedToday': 'Closed today',
  'dashboard.stats.last7Days': 'Last 7 days',
  'dashboard.stats.unassignedConversations': 'Unassigned conversations',
  'dashboard.stats.avgFirstResponse': 'Average first response',
  'dashboard.stats.avgResolution': 'Average resolution time',
  'dashboard.stats.satisfactionScore': 'Satisfaction rating',
  'dashboard.stats.ratingCount': { one: '{count} rating', other: '{count} ratings' },

  // ── Conversation status ──
  'dashboard.status.active': 'Active',
  'dashboard.status.pending': 'Pending',
  'dashboard.status.closed': 'Closed',
  'dashboard.status.other': 'Other',
  'dashboard.status.inProgress': 'In progress',
  'dashboard.status.replies': 'Replies',

  // ── SLA alerts ──
  'dashboard.sla.breached': {
    one: 'SLA breached for {count} conversation',
    other: 'SLA breached for {count} conversations',
  },
  'dashboard.sla.breachedHint': 'Handle breached conversations first',
  'dashboard.sla.warning': {
    one: 'SLA about to breach for {count} conversation',
    other: 'SLA about to breach for {count} conversations',
  },
  'dashboard.sla.warningHint': 'Handle these conversations before they breach',

  // ── Unassigned conversations alert ──
  'dashboard.unassigned.count': {
    one: 'There is {count} unassigned active conversation',
    other: 'There are {count} unassigned active conversations',
  },
  'dashboard.unassigned.hint': 'Assign an agent as soon as possible',

  // ── Module overview ──
  'dashboard.modules.title': 'Module overview',
  'dashboard.modules.knowledgeBase': 'Knowledge base',
  'dashboard.modules.articlesAndCategories': 'articles · {count} categories',
  'dashboard.modules.published': 'Published',
  'dashboard.modules.draft': 'Draft',
  'dashboard.modules.views': 'Views',
  'dashboard.modules.calendar': 'Calendar',
  'dashboard.modules.todayEvents': 'Events today · {count} total',
  'dashboard.modules.upcoming': 'Upcoming',
  'dashboard.modules.overdue': 'Overdue',
  'dashboard.modules.workflows': 'Workflows',
  'dashboard.modules.activeRules': 'Active rules · {count} total',
  'dashboard.modules.executionsToday': 'Executions today',
  'dashboard.modules.succeeded': 'Succeeded',
  'dashboard.modules.failed': 'Failed',
  'dashboard.modules.imQueue': 'IM queue',
  'dashboard.modules.pendingAndSentToday': 'Pending · {count} sent today',
  'dashboard.modules.pendingSend': 'Pending',
  'dashboard.modules.sending': 'Sending',
  'dashboard.modules.sentToday': 'Sent today',

  // ── Charts ──
  'dashboard.charts.statusDistribution': 'Conversation status distribution',
  'dashboard.charts.userWorkload': 'Agent workload',
  'dashboard.charts.mailboxStats': 'Mailbox stats',
  'dashboard.charts.conversationTrend': 'Conversation trend, last 7 days',
  'dashboard.charts.newConversations': 'New conversations ({count})',
  'dashboard.charts.closedConversations': 'Closed conversations ({count})',
  'dashboard.charts.totalConversations': 'Total conversations',
  'dashboard.charts.trendTooltip': '{date}\nNew: {newCount}\nClosed: {closedCount}',

  // ── Recent conversations ──
  'dashboard.recent.title': 'Recent conversations',
  'dashboard.recent.empty': 'No conversations',

  // ── Pinned reports ──
  'dashboard.pinnedReports.title': 'Pinned reports',
  'dashboard.pinnedReports.empty': 'No pinned reports',
  'dashboard.pinnedReports.create': 'Create one',
  'dashboard.pinnedReports.loadFailed': 'Failed to load pinned reports: {message}',

  // ── Custom report category / visibility ──
  'dashboard.reportCategory.performance': 'Performance',
  'dashboard.reportCategory.efficiency': 'Efficiency',
  'dashboard.reportCategory.customer': 'Customer',
  'dashboard.reportCategory.workload': 'Workload',
  'dashboard.reportVisibility.private': 'Private',
  'dashboard.reportVisibility.team': 'Team',
  'dashboard.reportVisibility.public': 'Public',

  // ── System resources ──
  'dashboard.resources.title': 'System resources',
  'dashboard.resources.totalUsers': 'Total users',
  'dashboard.resources.totalCustomers': 'Total customers',
  'dashboard.resources.totalMailboxes': 'Total mailboxes',
  'dashboard.resources.pendingConversations': 'Pending conversations',
  'dashboard.resources.totalTags': 'Total tags',
  'dashboard.resources.totalCannedResponses': 'Canned responses',
  'dashboard.resources.knowledgeLikes': 'Knowledge base likes',
} satisfies Entries;
