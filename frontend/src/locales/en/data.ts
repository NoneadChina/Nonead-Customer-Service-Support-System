import type { Entries } from '../../i18n/types';

/**
 * English values for the data cluster (batch 2 wave 11): audit log / send log /
 * reports / custom reports / import & export.
 *
 * Sections mirror `zh-Hans/data.ts`:
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
 * Must use `satisfies Entries`, never a type annotation — see `guard.ts`.
 */
export const data = {
  // ══ data.common.* —— 跨页逐字复用的通用文案 ══
  'data.common.refresh': 'Refresh',
  'data.common.export': 'Export',
  'data.common.exportCsv': 'Export CSV',
  'data.common.exportFailed': 'Export failed',
  'data.common.exportSucceeded': 'Export succeeded',
  'data.common.exporting': 'Exporting...',
  'data.common.noTrendData': 'No trend data',
  'data.common.loadFailed': 'Load failed',
  'data.common.loadFailedPrefix': 'Failed to load: ',
  'data.common.all': 'All',
  'data.common.allMailboxes': 'All mailboxes',
  'data.common.allTypes': 'All types',
  'data.common.startDate': 'Start date',
  'data.common.endDate': 'End date',
  'data.common.viewDetails': 'View details',
  'data.common.previousPage': 'Previous',
  'data.common.nextPage': 'Next',
  'data.common.ipAddress': 'IP address',
  'data.common.system': 'System',
  'data.common.metric.newConversations': 'New conversations',
  'data.common.metric.closedConversations': 'Closed conversations',
  'data.common.metric.agentReplies': 'Agent replies',
  'data.common.metric.newCustomers': 'New customers',

  // ══ data.audit.* —— 审计日志页 ══
  // ════════ Page header (first two are seed keys, do not rename) ════════
  'data.audit.title': 'Audit log',
  'data.audit.description': 'Review system operations, entity change history and client context',
  'data.audit.hmacBadge': 'HMAC signature verification',
  // The value keeps the original single space before the relative timestamp
  'data.audit.updatedAtPrefix': 'Updated ',
  'data.audit.verifyIntegrityButton': 'Verify integrity',
  'data.audit.verifyShortButton': 'Verify',
  'data.audit.verifyIntegrityTitle': 'Verify the HMAC signatures of the logs on this page',

  // ════════ KPI cards ════════
  'data.audit.kpi.totalLogs': 'Total records',
  'data.audit.kpi.calculating': 'Calculating...',
  'data.audit.kpi.matchingFilters': 'Matching filters',
  'data.audit.kpi.activeUsers': 'Active users',
  'data.audit.kpi.uniqueUsers': 'Unique users',
  'data.audit.kpi.last24h': 'Last 24 hours',
  'data.audit.kpi.newActions': 'New actions',
  'data.audit.kpi.anomalies': 'Anomalous events',
  'data.audit.kpi.failedOrLocked': 'Failed logins / lockouts',

  // ════════ Action / entity type distribution charts ════════
  'data.audit.chart.actionDistribution': 'Action distribution (global top 8)',
  'data.audit.chart.entityDistribution': 'Entity type distribution',

  // ════════ Filter bar ════════
  'data.audit.filter.keyword': 'Keyword search (server-side)',
  'data.audit.filter.keywordPlaceholder': 'Action / entity / IP / user agent...',
  'data.audit.filter.userIdPlaceholder': 'e.g. 1',
  'data.audit.filter.ipPlaceholder': 'e.g. 192.168.1.1',
  'data.audit.filter.activeHint': 'Filters are applied; statistics and the list are scoped to this filter',
  'data.audit.exportTitle': 'Export the audit logs matching the current filters as CSV (up to 5000 rows)',

  // ════════ Integrity verification panel ════════
  'data.audit.integrity.reportTitle': 'Integrity verification report',
  'data.audit.integrity.summary': 'Checked {checked} records (page {page} of {total}), {tampered} tampered',
  'data.audit.integrity.moreSuffix': ' more',

  // ════════ List: empty state / table header / inline badges ════════
  'data.audit.empty.title': 'No audit log records',
  'data.audit.empty.hint': 'Change the filters or click refresh to load the latest data',
  'data.audit.field.time': 'Time',
  'data.audit.field.user': 'User',
  'data.audit.field.action': 'Action',
  'data.audit.field.entity': 'Entity',
  'data.audit.field.ipClient': 'IP · Client',
  'data.audit.field.details': 'Details',
  'data.audit.signature.invalid': 'Signature verification failed: suspected tampering',
  'data.audit.signature.valid': 'Signature verified',
  'data.audit.userRef': 'User #{id}',
  // Rendered when no User-Agent was recorded; the placeholder itself is language-neutral
  'data.audit.device.unknown': '-',
  'data.audit.device.desktop': 'Desktop',
  'data.audit.device.mobile': 'Mobile',

  // ════════ Pagination (numeric values keep their own styled <span>) ════════
  'data.audit.pagination.pagePrefix': 'Page ',
  'data.audit.pagination.pageTotal': ' / {total}',
  'data.audit.pagination.pageSuffix': ' page',
  'data.audit.pagination.rangePrefix': 'Showing ',
  'data.audit.pagination.rangeMiddle': ' of ',
  'data.audit.pagination.rangeSuffix': ' total',

  // ════════ Detail modal ════════
  'data.audit.detail.title': 'Audit log detail #{id}',
  'data.audit.detail.verifySignature': 'Verify signature',
  'data.audit.detail.exportJson': 'Export JSON',
  'data.audit.signature.validLong': 'Signature verified (record has not been tampered with)',
  'data.audit.signature.invalidLong': 'Signature verification failed (suspected tampering)',
  'data.audit.detail.verifiedAtPrefix': 'Verified at: ',
  'data.audit.detail.storedSignature': 'Stored signature',
  'data.audit.detail.expectedSignature': 'Expected signature',
  'data.audit.emptyValue': '(empty)',
  'data.audit.field.operator': 'Operator',
  'data.audit.field.automatedHint': 'Automated or internal operation',
  'data.audit.field.actionType': 'Action type',
  'data.audit.field.entityType': 'Entity type',
  'data.audit.field.userId': 'User ID',
  'data.audit.field.targetEntity': 'Target entity',
  'data.audit.field.entityIdPrefix': 'Entity ID: ',
  'data.audit.field.networkContext': 'Network and client context',
  'data.audit.field.browser': 'Browser',
  'data.audit.field.fullUserAgent': 'Full user agent',
  'data.audit.field.relatedLogs': 'Related logs',
  'data.audit.field.sourcePrefix': 'Source: ',
  'data.audit.related.sameEntity': 'Same entity',
  'data.audit.related.sameUser': 'Same user',
  'data.audit.related.none': 'None',
  'data.audit.related.countMiddle': ', ',
  'data.audit.related.countSuffix': ' total',
  'data.audit.related.empty': 'No related logs',
  'data.audit.field.changeComparison': 'Field changes',
  'data.audit.field.oldValues': 'Old values',
  'data.audit.field.newValues': 'New values',

  // ════════ Request failure messages (export failure reuses data.common.exportFailed) ════════
  'data.audit.loadFailed': 'Failed to load audit logs',
  'data.audit.integrityFailed': 'Integrity verification failed',
  'data.audit.verifySingleFailed': 'Failed to verify this record',

  // ════════ Audit action codes (backend AuditLogs.Action) ════════
  'data.audit.action.all': 'All actions',
  'data.audit.action.login': 'Login',
  'data.audit.action.logout': 'Logout',
  'data.audit.action.loginfailed': 'Failed login',
  'data.audit.action.lockout': 'Account locked',
  'data.audit.action.create': 'Create',
  'data.audit.action.update': 'Update',
  'data.audit.action.delete': 'Delete',
  'data.audit.action.assign': 'Assign',
  'data.audit.action.status_change': 'Status change',
  'data.audit.action.reply': 'Reply',
  'data.audit.action.note': 'Note',
  'data.audit.action.addnote': 'Add note',
  'data.audit.action.password_change': 'Password change',
  'data.audit.action.reset': 'Password reset',
  'data.audit.action.resetfailed': 'Reset failed',
  'data.audit.action.createdraft': 'Create draft',
  // Calendar events (calendar.* actions written explicitly by CalendarMutationService)
  'data.audit.action.calendarCreate': 'Create calendar event',
  'data.audit.action.calendarUpdate': 'Update calendar event',
  'data.audit.action.calendarDelete': 'Delete calendar event',
  'data.audit.action.calendarStatusChange': 'Calendar status change',
  'data.audit.action.calendarDuplicate': 'Duplicate calendar event',
  'data.audit.action.calendarQuickUpdate': 'Quick-update calendar event',
  'data.audit.action.calendarRestore': 'Restore calendar event',
  'data.audit.action.calendarHardDelete': 'Permanently delete calendar event',
  'data.audit.action.calendarClearTrash': 'Empty calendar trash',
  'data.audit.action.calendarPurgeTrash': 'Purge calendar trash',
  'data.audit.action.calendarReminderConfirmed': 'Calendar reminder confirmed',
  // Time entries (time_entry.* actions written by TimeEntryService)
  'data.audit.action.timeEntryCreate': 'Create time entry',
  'data.audit.action.timeEntryUpdate': 'Update time entry',
  'data.audit.action.timeEntryDelete': 'Delete time entry',
  'data.audit.action.timeEntryTimerStart': 'Start timer',
  'data.audit.action.timeEntryTimerStop': 'Stop timer',
  'data.audit.action.timeEntryRestore': 'Restore time entry',
  // Customer / conversation actions
  'data.audit.action.restore': 'Restore',
  'data.audit.action.hardDelete': 'Permanently delete',
  'data.audit.action.bulkSoftDelete': 'Bulk soft delete',
  'data.audit.action.bulkHardDelete': 'Bulk permanent delete',
  'data.audit.action.bulkRestore': 'Bulk restore',
  // Calendar batch operations (calendar.batch_{op}[_result])
  'data.audit.action.calendarBatchDelete': 'Bulk delete calendar events',
  'data.audit.action.calendarBatchSetstatus': 'Bulk set calendar status',
  'data.audit.action.calendarBatchSeteventtype': 'Bulk set calendar event type',
  'data.audit.action.calendarBatchMoveoffset': 'Bulk shift calendar events',
  'data.audit.action.calendarBatchRestore': 'Bulk restore calendar events',
  'data.audit.action.calendarBatchDeleteResult': 'Bulk delete calendar events result',
  'data.audit.action.calendarBatchSetstatusResult': 'Bulk set calendar status result',
  'data.audit.action.calendarBatchSeteventtypeResult': 'Bulk set calendar event type result',
  'data.audit.action.calendarBatchMoveoffsetResult': 'Bulk shift calendar events result',
  'data.audit.action.calendarBatchRestoreResult': 'Bulk restore calendar events result',
  // Prefix fallbacks: unknown calendar./time_entry. codes degrade to a generic label
  'data.audit.action.calendarFallback': 'Calendar operation',
  'data.audit.action.timeEntryFallback': 'Time entry operation',

  // ════════ Entity type codes (backend AuditLogs.EntityType, matched lower-cased) ════════
  'data.audit.entityType.user': 'User',
  'data.audit.entityType.customer': 'Customer',
  'data.audit.entityType.conversation': 'Conversation',
  'data.audit.entityType.conversationthread': 'Message',
  'data.audit.entityType.mailbox': 'Mailbox',
  'data.audit.entityType.tag': 'Tag',
  'data.audit.entityType.folder': 'Folder',
  'data.audit.entityType.attachment': 'Attachment',
  'data.audit.entityType.auth': 'Auth',
  'data.audit.entityType.draft': 'Draft',
  'data.audit.entityType.sendlog': 'Send log',
  'data.audit.entityType.workflow': 'Workflow',
  'data.audit.entityType.role': 'Role',
  'data.audit.entityType.team': 'Team',
  'data.audit.entityType.mailtemplate': 'Mail template',
  'data.audit.entityType.cannedresponse': 'Canned response',
  'data.audit.entityType.webhook': 'Webhook',
  'data.audit.entityType.sla': 'SLA',
  'data.audit.entityType.macro': 'Macro',
  'data.audit.entityType.customfield': 'Custom field',
  'data.audit.entityType.notificationrule': 'Notification rule',
  'data.audit.entityType.notificationtemplate': 'Notification template',
  'data.audit.entityType.apikey': 'API key',
  'data.audit.entityType.calendarevent': 'Calendar event',
  'data.audit.entityType.knowledgearticle': 'Knowledge base',
  'data.audit.entityType.emailsignature': 'Email signature',
  'data.audit.entityType.workflowexecutionlog': 'Workflow execution log',
  'data.audit.entityType.systemoption': 'System option',
  // — Types written by the EF interceptor (entity class names) or inferred from controller routes —
  'data.audit.entityType.apikeylog': 'API key log',
  'data.audit.entityType.calendareventtemplate': 'Calendar event template',
  'data.audit.entityType.calendarreminderattempt': 'Calendar reminder attempt',
  'data.audit.entityType.chatmessage': 'Chat message',
  'data.audit.entityType.chatsession': 'Chat session',
  'data.audit.entityType.conversationfolder': 'Conversation folder',
  'data.audit.entityType.conversationreadstate': 'Conversation read state',
  'data.audit.entityType.conversationtag': 'Conversation tag',
  'data.audit.entityType.customfieldvalue': 'Custom field value',
  'data.audit.entityType.customreport': 'Custom report',
  'data.audit.entityType.customreportschedule': 'Custom report schedule',
  'data.audit.entityType.distributedlock': 'Distributed lock',
  'data.audit.entityType.email': 'Email',
  'data.audit.entityType.follower': 'Follower',
  'data.audit.entityType.immessagelog': 'IM message log',
  'data.audit.entityType.knowledgearticleattachment': 'Knowledge base attachment',
  'data.audit.entityType.knowledgecategory': 'Knowledge base category',
  'data.audit.entityType.macroexecutionlog': 'Macro execution log',
  'data.audit.entityType.mailboxuser': 'Mailbox user',
  'data.audit.entityType.moduleentity': 'Module entity',
  'data.audit.entityType.outlookeventmapping': 'Outlook event mapping',
  'data.audit.entityType.outlooksynclog': 'Outlook sync log',
  'data.audit.entityType.outlooksyncqueueitem': 'Outlook sync queue item',
  'data.audit.entityType.outlooksyncsetting': 'Outlook sync settings',
  'data.audit.entityType.passwordresettoken': 'Password reset token',
  'data.audit.entityType.refreshtoken': 'Refresh token',
  'data.audit.entityType.satisfactionrating': 'Satisfaction rating',
  'data.audit.entityType.slapolicy': 'SLA policy',
  'data.audit.entityType.slaviolation': 'SLA breach',
  'data.audit.entityType.star': 'Star',
  'data.audit.entityType.teammember': 'Team member',
  'data.audit.entityType.timeentry': 'Time entry',
  'data.audit.entityType.translation': 'Translation',
  'data.audit.entityType.usernotification': 'User notification',
  'data.audit.entityType.userrole': 'User role',
  'data.audit.entityType.webhooklog': 'Webhook log',
  // — Inferred from controller names (strip "Controller", then a trailing "s") —
  'data.audit.entityType.auditlog': 'Audit log',
  'data.audit.entityType.calendar': 'Calendar',
  'data.audit.entityType.calendarfeed': 'Calendar feed',
  'data.audit.entityType.chat': 'Live chat',
  'data.audit.entityType.dashboard': 'Dashboard',
  'data.audit.entityType.dingding': 'DingTalk',
  'data.audit.entityType.emailtest': 'Email test',
  'data.audit.entityType.feishu': 'Feishu',
  'data.audit.entityType.importexport': 'Import / export',
  'data.audit.entityType.knowledge': 'Knowledge base',
  'data.audit.entityType.localization': 'Localization',
  'data.audit.entityType.mail': 'Mail',
  'data.audit.entityType.mailboxe': 'Mailbox',
  'data.audit.entityType.module': 'Module',
  'data.audit.entityType.notification': 'Notification',
  'data.audit.entityType.oauth': 'OAuth',
  'data.audit.entityType.outlooksync': 'Outlook sync',
  'data.audit.entityType.profile': 'Profile',
  'data.audit.entityType.realtime': 'Realtime',
  'data.audit.entityType.report': 'Report',
  'data.audit.entityType.search': 'Search',
  'data.audit.entityType.setup': 'Setup',
  'data.audit.entityType.system': 'System',
  'data.audit.entityType.timetracking': 'Time tracking',
  'data.audit.entityType.tracking': 'Email tracking',
  'data.audit.entityType.userprofile': 'User profile',
  'data.audit.entityType.wechat': 'WeChat',
  'data.audit.entityType.whatsapp': 'WhatsApp',
  // — EF entity class names (Domain/Entities); the interceptor writes `GetType().Name`, so the class name is the code —
  'data.audit.entityType.customerphone': 'Customer phone',
  'data.audit.entityType.customersocialprofile': 'Customer social profile',
  'data.audit.entityType.macroaction': 'Macro action',
  'data.audit.entityType.workflowaction': 'Workflow action',
  'data.audit.entityType.workflowcondition': 'Workflow condition',
  'data.audit.entityType.workflowconditiongroup': 'Workflow condition group',
  'data.audit.entityType.workflowevaluationcontext': 'Workflow evaluation context',

  // ══ data.sendLogs.* —— 发送日志页 ══
  'data.sendLogs.title': 'Send log',
  'data.sendLogs.description': 'Track delivery status, statistics and delivery failures',
  'data.sendLogs.breadcrumb': 'System',
  'data.sendLogs.loadOverviewFailed': 'Failed to load overview',
  'data.sendLogs.loadListFailed': 'Failed to load list',

  // ====== Send status codes (backend SendLogService.GetStatusLabel) ======
  'data.sendLogs.status.received': 'Received',
  'data.sendLogs.status.error': 'Send error',
  'data.sendLogs.status.delivered': 'Delivered',
  'data.sendLogs.status.failed': 'Delivery failed',
  'data.sendLogs.status.opened': 'Opened',
  'data.sendLogs.status.clicked': 'Clicked',
  'data.sendLogs.status.unsubscribed': 'Unsubscribed',
  'data.sendLogs.status.complained': 'Complained',
  'data.sendLogs.status.interrupted': 'Send interrupted',
  'data.sendLogs.status.unknown': 'Unknown',

  // ====== Mail type codes (backend SendLogMailType.GetLabel) ======
  'data.sendLogs.mailType.customerReply': 'Customer reply',
  'data.sendLogs.mailType.userNotification': 'User notification',
  'data.sendLogs.mailType.autoReply': 'Auto reply',
  'data.sendLogs.mailType.invite': 'Invite',
  'data.sendLogs.mailType.passwordChanged': 'Password changed',
  'data.sendLogs.mailType.wrongUserEmail': 'Wrong user email',
  'data.sendLogs.mailType.test': 'Test',
  'data.sendLogs.mailType.alert': 'Alert',
  'data.sendLogs.mailType.calendarReminder': 'Calendar reminder',
  'data.sendLogs.mailType.unknown': 'Unknown',

  // ====== Trend chart ======
  'data.sendLogs.trend.title': 'Send trend',
  'data.sendLogs.trend.subtitle': 'Daily totals, successes and failures',
  'data.sendLogs.trend.total': 'Total',
  'data.sendLogs.trend.success': 'Succeeded',
  'data.sendLogs.trend.failed': 'Failed',

  // ====== Mailbox distribution ======
  'data.sendLogs.mailboxStats.title': 'Mailbox distribution',
  'data.sendLogs.mailboxStats.subtitle': 'Per-mailbox load and failures',
  'data.sendLogs.mailboxBars.failed': 'Failed {count}',

  // ====== Recent failures ======
  'data.sendLogs.recentFailures.title': 'Recent failures',
  'data.sendLogs.recentFailures.subtitle': 'Failures worth investigating',
  'data.sendLogs.noSubject': 'No subject',
  'data.sendLogs.rowAction.details': 'Details',
  'data.sendLogs.rowAction.conversation': 'Conversation',
  'data.sendLogs.viewConversation': 'View conversation',

  // ====== Overview cards ======
  'data.sendLogs.kpi.total': 'Total sent',
  'data.sendLogs.kpi.totalSub': 'Within period',
  'data.sendLogs.kpi.success': 'Delivered',
  'data.sendLogs.kpi.successSub': 'Share {rate}%',
  'data.sendLogs.kpi.failed': 'Failed',
  'data.sendLogs.kpi.failedSub': 'Failure rate {rate}%',
  'data.sendLogs.kpi.pending': 'Pending',
  'data.sendLogs.kpi.pendingSub': 'In accepted queue',

  // ====== Filter bar ======
  'data.sendLogs.filter.mailbox': 'Mailbox',
  'data.sendLogs.filter.allStatuses': 'All statuses',
  'data.sendLogs.filter.keyword': 'Keyword',
  'data.sendLogs.filter.keywordPlaceholder': 'Search email or message ID...',

  // ====== List ======
  'data.sendLogs.listTitle': 'Send logs',
  'data.sendLogs.listEmptyTitle': 'No send logs',
  'data.sendLogs.listEmptySubtitle': 'Adjust the filters or click refresh to load the latest data',

  // ====== Table headers / detail field labels ======
  'data.sendLogs.field.status': 'Status',
  'data.sendLogs.field.type': 'Type',
  'data.sendLogs.field.email': 'Recipient email',
  'data.sendLogs.field.mailType': 'Mail type',
  'data.sendLogs.field.conversationSubject': 'Conversation subject',
  'data.sendLogs.field.agent': 'Agent',
  'data.sendLogs.field.sentAt': 'Sent time',
  'data.sendLogs.field.actions': 'Actions',

  // ====== Pagination ======
  'data.sendLogs.pagination.pagePrefix': 'Page ',
  'data.sendLogs.pagination.pageTotal': ' / {total}',
  'data.sendLogs.pagination.pageSuffix': ' page',
  'data.sendLogs.pagination.totalPrefix': 'Total ',
  'data.sendLogs.pagination.totalSuffix': ' items',
  'data.sendLogs.pagination.first': 'First',
  'data.sendLogs.pagination.last': 'Last',

  // ====== CSV export (one key per column, joined with commas) ======
  'data.sendLogs.csv.id': 'ID',
  'data.sendLogs.csv.messageId': 'Message ID',
  'data.sendLogs.csv.email': 'Recipient email',
  'data.sendLogs.csv.mailType': 'Mail type',
  'data.sendLogs.csv.status': 'Status',
  'data.sendLogs.csv.agent': 'Agent',
  'data.sendLogs.csv.subject': 'Conversation subject',
  'data.sendLogs.csv.sentAt': 'Sent time',

  // ====== Detail modal ======
  'data.sendLogs.detailTitlePrefix': 'Send log details #',
  'data.sendLogs.exportJson': 'Export JSON',
  'data.sendLogs.detail.status': 'Send status',
  'data.sendLogs.detail.customer': 'Customer: {email}',
  'data.sendLogs.detail.context': 'Conversation and agent context',
  'data.sendLogs.detail.noSubject': '(No subject)',
  'data.sendLogs.detail.userFallbackPrefix': 'User #',
  'data.sendLogs.detail.automated': 'Automated send',
  'data.sendLogs.detail.conversationId': 'Conversation ID',
  'data.sendLogs.detail.threadId': 'Thread ID',
  'data.sendLogs.detail.messageId': 'Message ID (Message-ID)',

  // ══ data.reports.* —— 报表统计页 ══
  // ── Page header ──
  'data.reports.title': 'Reports',
  // Prefix: followed directly by a styled date <span>, hence the trailing space
  'data.reports.header.dataRangePrefix': 'Data range: ',
  'data.reports.loadReportFailed': 'Failed to load report',

  // ── Status distribution pie ──
  'data.reports.statusPie.total': 'Total conversations',

  // ── Chart titles and subtitles ──
  'data.reports.trend.title': 'Conversation trend',
  'data.reports.trend.subtitle': 'New, closed and reply counts over time',
  'data.reports.statusDistribution.title': 'Conversation status distribution',
  'data.reports.mailboxLoad.title': 'Mailbox load distribution',

  // ── Agent performance ──
  'data.reports.performance.title': 'Agent performance',
  'data.reports.performance.colAgent': 'Agent',
  'data.reports.performance.colAssigned': 'Assigned',
  'data.reports.performance.colReplies': 'Replies',
  'data.reports.performance.colClosed': 'Closed',
  'data.reports.performance.colResponseHours': 'Response (h)',
  // Prefix: followed directly by a styled numeric <span>, hence the trailing space
  'data.reports.performance.responsePrefix': 'Response ',

  // ── Customer domains ──
  'data.reports.domains.title': 'Top 10 customer domains',
  'data.reports.domains.customers': '{count} customers',

  // ── Average resolution time ──
  'data.reports.resolution.title': 'Average resolution time',
  'data.reports.resolution.subtitle': 'Average time from creation to closing for closed conversations',
  'data.reports.resolution.unitHours': 'hours',
  'data.reports.resolution.unitDays': 'days',

  // ── KPI cards ──
  'data.reports.kpi.avgFirstResponse': 'Avg. first response',
  'data.reports.kpi.avgFirstResponseHint': 'Customer message → Agent reply',
  'data.reports.kpi.slaAchievement': 'SLA achievement',
  'data.reports.kpi.slaTarget': 'Target: respond within 24h',

  // ── CSV export: section heading rows ──
  'data.reports.csv.summaryHeading': '# Report summary',
  'data.reports.csv.trendHeading': '# Trend data',
  'data.reports.csv.performanceHeading': '# Agent performance',
  'data.reports.csv.mailboxLoadHeading': '# Mailbox load',
  'data.reports.csv.customerDomainsHeading': '# Top 10 customer domains',

  // ── CSV export: column names (one key per column, rows joined with ',') ──
  'data.reports.csv.dateRangeCol': 'Date range',
  'data.reports.csv.mailboxFilterCol': 'Mailbox filter',
  'data.reports.csv.metricCol': 'Metric',
  'data.reports.csv.valueCol': 'Value',
  'data.reports.csv.avgFirstResponseHours': 'Avg. first response (hours)',
  'data.reports.csv.avgResolutionHours': 'Avg. resolution time (hours)',
  'data.reports.csv.slaRate': 'SLA achievement (%)',
  'data.reports.csv.dateCol': 'Date',
  'data.reports.csv.newCol': 'New',
  'data.reports.csv.closedCol': 'Closed',
  'data.reports.csv.replyCol': 'Replies',
  'data.reports.csv.nameCol': 'Name',
  'data.reports.csv.emailCol': 'Email',
  'data.reports.csv.currentAssignedCol': 'Currently assigned',
  'data.reports.csv.repliesCol': 'Replies',
  'data.reports.csv.closedCountCol': 'Closed',
  'data.reports.csv.mailboxCol': 'Mailbox',
  'data.reports.csv.percentageCol': 'Share (%)',
  'data.reports.csv.domainCol': 'Domain',
  'data.reports.csv.conversationsCol': 'Conversations',
  'data.reports.csv.uniqueCustomersCol': 'Unique customers',

  // ══ data.customReports.* —— 自定义报表列表页 ══
  'data.customReports.title': 'Custom reports',
  'data.customReports.description': 'Create and manage custom analytics reports for your team',

  'data.customReports.searchPlaceholder': 'Search report names...',
  'data.customReports.totalCount': 'Total {count} items',

  // Renders as: `Total <span>{totalItems}</span> items · {pageSize} per page · page {currentPage} of {totalPages}`
  // The leading/trailing spaces keep the styled value's element in place without gluing words together.
  'data.customReports.pagination.totalPrefix': 'Total ',
  'data.customReports.pagination.summary': ' items · {pageSize} per page · page {currentPage} of {totalPages}',

  'data.customReports.category.all': 'All categories',
  'data.customReports.category.performance': 'Performance',
  'data.customReports.category.efficiency': 'Efficiency',
  'data.customReports.category.customer': 'Customer',
  'data.customReports.category.workload': 'Workload',

  'data.customReports.visibility.all': 'All visibility levels',
  'data.customReports.visibility.private': 'Private',
  'data.customReports.visibility.team': 'Team',
  'data.customReports.visibility.public': 'Public',

  'data.customReports.tab.pinned': 'My pinned',
  'data.customReports.tab.trash': 'Trash',

  'data.customReports.action.clearTrash': 'Empty trash',
  'data.customReports.action.clear': 'Empty',
  'data.customReports.action.create': 'New',
  'data.customReports.action.createReport': 'New report',
  'data.customReports.action.deleteReport': 'Delete report',
  'data.customReports.action.restoreReport': 'Restore report',
  'data.customReports.action.hardDeleteReport': 'Permanently delete report',
  'data.customReports.action.restore': 'Restore',
  'data.customReports.action.hardDelete': 'Delete permanently',
  'data.customReports.action.clearAll': 'Empty all',
  'data.customReports.action.view': 'View',
  'data.customReports.action.viewReport': 'View report',
  'data.customReports.action.editConfig': 'Edit settings',
  'data.customReports.action.schedule': 'Set schedule',
  'data.customReports.action.exportAs': 'Export as',
  'data.customReports.action.exportAsCsv': 'CSV format',
  'data.customReports.action.exportAsExcel': 'Excel format',
  'data.customReports.action.exportAsPdf': 'PDF format',
  'data.customReports.action.pin': 'Pin',
  'data.customReports.action.unpin': 'Unpin',

  'data.customReports.deletedBadge': 'Deleted',
  'data.customReports.deletedAt': 'Deleted at',

  'data.customReports.empty.trashTitle': 'Trash is empty',
  'data.customReports.empty.trashDescription': 'Deleted reports are kept here temporarily',
  'data.customReports.empty.pinnedTitle': 'No pinned reports',
  'data.customReports.empty.pinnedDescription': 'Click the pin icon on any report to add it here',
  'data.customReports.empty.allTitle': 'No custom reports',
  'data.customReports.empty.allDescription': 'Click "New report" in the top right to start creating one',

  'data.customReports.toast.loadPinnedFailed': 'Failed to load pinned reports',
  'data.customReports.toast.pinned': 'Pinned',
  'data.customReports.toast.unpinned': 'Unpinned',
  'data.customReports.toast.operationFailed': 'Operation failed',
  'data.customReports.toast.deleted': 'Report deleted and moved to trash',
  'data.customReports.toast.deleteFailed': 'Failed to delete the report',
  'data.customReports.toast.restored': 'Report restored',
  'data.customReports.toast.restoreFailed': 'Failed to restore the report',
  'data.customReports.toast.hardDeleted': 'Report permanently deleted',
  'data.customReports.toast.hardDeleteFailed': 'Failed to permanently delete the report',
  'data.customReports.toast.trashCleared': 'Trash emptied ({count} items removed)',
  'data.customReports.toast.clearFailed': 'Failed to empty the trash',

  'data.customReports.dialog.deleteMessage': 'Are you sure you want to delete the report "{name}"? It will be moved to the trash, where you can restore it.',
  'data.customReports.dialog.restoreMessage': 'Are you sure you want to restore the report "{name}"?',
  'data.customReports.dialog.hardDeleteMessage': 'Warning: you are about to permanently delete the report "{name}"! This cannot be undone and the data cannot be recovered.',
  'data.customReports.dialog.clearTrashMessage': 'Are you sure you want to empty the trash? All {count} records will be permanently deleted and this cannot be undone.',
  'data.customReports.dialog.hardDeleteNote': 'After permanent deletion, the report and its configuration and schedule cannot be recovered.',
  'data.customReports.dialog.clearTrashNote': 'Once emptied, every report in the trash is permanently deleted and cannot be recovered.',

  // ══ data.customReports.view.* —— 自定义报表查看页 ══
  'data.customReports.view.title': 'Custom report',

  // ── Header / breadcrumb / meta ──
  'data.customReports.view.breadcrumbView': 'View',
  // The backend only sends stable codes; labels are resolved here (see VISIBILITY_INFO / CATEGORY_LABEL_KEYS)
  'data.customReports.view.visibility.private': 'Private',
  'data.customReports.view.visibility.team': 'Team',
  'data.customReports.view.visibility.public': 'Public',
  'data.customReports.view.category.performance': 'Performance',
  'data.customReports.view.category.efficiency': 'Efficiency',
  'data.customReports.view.category.customer': 'Customer',
  'data.customReports.view.category.workload': 'Workload',
  // No trailing space: the JSX text node supplies the space (source was `团队: {name}`)
  'data.customReports.view.teamLabel': 'Team:',
  'data.customReports.view.updatedAtLabel': 'Updated:',
  // The Chinese full-width colon has no space after it, so the space lives in this value
  'data.customReports.view.groupByLabel': 'Group by: ',
  'data.customReports.view.groupBy.week': 'weekly',
  'data.customReports.view.groupBy.month': 'monthly',
  'data.customReports.view.reportNotFound': 'Report not found',

  // ── Toolbar ──
  'data.customReports.view.backToList': 'Back to list',
  'data.customReports.view.refreshData': 'Refresh data',
  'data.customReports.view.exportFormat.csv': 'CSV format',
  'data.customReports.view.exportFormat.excel': 'Excel format',
  'data.customReports.view.exportFormat.pdf': 'PDF format',
  'data.customReports.view.pinTooltip': 'Unpin',
  'data.customReports.view.pinToDashboard': 'Pin to dashboard',
  'data.customReports.view.pinned': 'Pinned',
  'data.customReports.view.pin': 'Pin',
  'data.customReports.view.schedule': 'Schedule',

  // ── Loading / not-run / missing states ──
  'data.customReports.view.loadingReport': 'Loading report...',
  'data.customReports.view.executingReport': 'Running report...',
  'data.customReports.view.reportUnavailable': 'This report may have been deleted, or you do not have access to it',
  'data.customReports.view.notExecuted': 'Not run yet',
  'data.customReports.view.notExecutedHint': 'Click "Refresh data" at the top to run the report',
  'data.customReports.view.executeNow': 'Run now',

  // ── Toasts ──
  'data.customReports.view.toast.loadReportFailed': 'Failed to load report',
  'data.customReports.view.toast.reportRefreshed': 'Report refreshed',
  'data.customReports.view.toast.executeReportFailed': 'Failed to run report',
  'data.customReports.view.toast.pinned': 'Pinned',
  'data.customReports.view.toast.unpinned': 'Unpinned',
  'data.customReports.view.toast.operationFailed': 'Operation failed',

  // ── Legend / KPI cards ──
  'data.customReports.view.kpi.averageFirstResponse': 'Avg. first response',
  'data.customReports.view.kpi.averageFirstResponseHint': 'Customer message → Agent reply',
  'data.customReports.view.kpi.slaAchievement': 'SLA achievement',
  'data.customReports.view.kpi.slaTarget': 'Target: reply within 24h',

  // ── Chart titles ──
  'data.customReports.view.chart.trend': 'Conversation trend',
  'data.customReports.view.chart.trendSubtitle': 'New, closed and reply counts over time',
  'data.customReports.view.chart.statusDistribution': 'Status distribution',
  'data.customReports.view.chart.mailboxLoad': 'Mailbox load',
  'data.customReports.view.chart.userPerformance': 'Agent performance',
  'data.customReports.view.chart.customerDomains': 'Top customer domains',
  // Centre label of the status pie (total of the distribution)
  'data.customReports.view.chart.statusTotal': 'Total conversations',
  // Agent performance table headers
  'data.customReports.view.table.agent': 'Agent',
  'data.customReports.view.table.assigned': 'Assigned',
  'data.customReports.view.table.replied': 'Replied',
  'data.customReports.view.table.closed': 'Closed',
  'data.customReports.view.table.responseHours': 'Response (h)',
  'data.customReports.view.customerCount': { one: '{count} customer', other: '{count} customers' },

  // ── Average resolution ──
  'data.customReports.view.avgResolution.title': 'Average resolution time',
  'data.customReports.view.avgResolution.subtitle': 'Average time from creation to closure for closed conversations',
  'data.customReports.view.avgResolution.unitHours': 'hours',
  'data.customReports.view.avgResolution.daysApprox': '≈ {days} days',

  // ══ data.customReports.schedule.* —— 自定义报表调度弹窗 ══
  // Header
  'data.customReports.schedule.title': 'Custom report schedules',
  'data.customReports.schedule.description': 'Deliver a report by email on a schedule, as a PDF / Excel / CSV attachment',
  'data.customReports.schedule.addSchedule': 'Add schedule',

  // Form
  'data.customReports.schedule.defaultSubject': 'Scheduled custom report',
  'data.customReports.schedule.newSchedule': 'New schedule',
  'data.customReports.schedule.editSchedule': 'Edit schedule',
  'data.customReports.schedule.form.recurrenceLabel': 'Frequency *',
  'data.customReports.schedule.form.weekDayLabel': 'Day of week *',
  'data.customReports.schedule.form.sendTimeLabel': 'Send time *',
  'data.customReports.schedule.form.attachmentFormatLabel': 'Attachment format',
  'data.customReports.schedule.form.pdf': 'PDF document (.pdf)',
  'data.customReports.schedule.form.xlsx': 'Excel spreadsheet (.xlsx)',
  'data.customReports.schedule.form.csv': 'CSV data (.csv)',
  'data.customReports.schedule.form.recipientsLabel': 'Recipients * (separated by comma, semicolon or Enter)',
  'data.customReports.schedule.form.recipientsPlaceholder': 'e.g. a@example.com, b@example.com',
  'data.customReports.schedule.form.subjectLabel': 'Email subject *',
  'data.customReports.schedule.form.enableImmediately': 'Enable the schedule right after creation',
  'data.customReports.schedule.form.saving': 'Saving...',

  // Recurrence / weekday (values are the backend's stable codes)
  'data.customReports.schedule.recurrence.daily': 'Daily',
  'data.customReports.schedule.recurrence.weekly': 'Weekly',
  'data.customReports.schedule.recurrence.monthly': 'Monthly',
  'data.customReports.schedule.weekday.sun': 'Sunday',
  'data.customReports.schedule.weekday.mon': 'Monday',
  'data.customReports.schedule.weekday.tue': 'Tuesday',
  'data.customReports.schedule.weekday.wed': 'Wednesday',
  'data.customReports.schedule.weekday.thu': 'Thursday',
  'data.customReports.schedule.weekday.fri': 'Friday',
  'data.customReports.schedule.weekday.sat': 'Saturday',
  /** Prefix for an out-of-range weekday code, followed by the raw number */
  'data.customReports.schedule.weekday.unknownPrefix': 'Weekday ',
  /** Prefix of the weekly summary, followed by the weekday name */
  'data.customReports.schedule.weeklySummaryPrefix': 'Weekly · ',

  // List
  'data.customReports.schedule.status.running': 'Running',
  'data.customReports.schedule.status.disabled': 'Disabled',
  'data.customReports.schedule.status.enabled': 'Enabled',
  'data.customReports.schedule.loadingList': 'Loading schedules...',
  'data.customReports.schedule.emptyTitle': 'No schedules yet',
  'data.customReports.schedule.emptyHint': 'Click "Add schedule" in the top right to create a scheduled send task',
  /** Subject prefix, followed by the schedule subject */
  'data.customReports.schedule.subjectPrefix': 'Subject: ',
  /** Last-run prefix, followed by a localized date and time */
  'data.customReports.schedule.lastRunPrefix': 'Last: ',
  /** Next-run prefix, followed by a localized date and time */
  'data.customReports.schedule.nextRunPrefix': 'Next: ',

  // Row actions
  'data.customReports.schedule.action.disable': 'Disable',
  'data.customReports.schedule.action.enable': 'Enable',

  // Delete confirmation
  'data.customReports.schedule.deleteDialog.title': 'Delete schedule',
  'data.customReports.schedule.deleteDialog.message': 'Are you sure you want to delete this schedule? This action cannot be undone.',

  // Form validation
  'data.customReports.schedule.validation.weekDayRequired': 'Please select the day of the week',
  'data.customReports.schedule.validation.sendTimeRequired': 'Please select a send time',
  'data.customReports.schedule.validation.recipientsRequired': 'Please enter at least one recipient email address',
  /** Invalid-email prefix, followed by the offending address */
  'data.customReports.schedule.validation.invalidEmailPrefix': 'Invalid email address: ',
  'data.customReports.schedule.validation.subjectRequired': 'Please enter an email subject',

  // Result toasts
  'data.customReports.schedule.toast.updateSucceeded': 'Schedule updated',
  'data.customReports.schedule.toast.createSucceeded': 'Schedule created',
  'data.customReports.schedule.toast.saveFailed': 'Failed to save',
  'data.customReports.schedule.toast.operationFailed': 'Operation failed',
  'data.customReports.schedule.toast.deleteSucceeded': 'Deleted',
  'data.customReports.schedule.toast.deleteFailed': 'Failed to delete',
  'data.customReports.schedule.toast.loadListFailed': 'Failed to load the schedule list',

  // ══ data.customReportEditor.* —— 自定义报表编辑器 ══
  // ── Header (seed keys) ──
  'data.customReportEditor.createTitle': 'New custom report',
  'data.customReportEditor.editTitle': 'Edit custom report',

  // ── Breadcrumb / header ──
  'data.customReportEditor.listLink': 'Custom reports',
  'data.customReportEditor.action.new': 'New',
  'data.customReportEditor.subtitle': 'Configure data sources, filters and visualization modules, then save to share with your team or dashboard',

  // ── Toolbar ──
  'data.customReportEditor.backToList': 'Back to list',
  'data.customReportEditor.action.preview': 'Preview',
  'data.customReportEditor.action.executing': 'Running...',
  'data.customReportEditor.action.saving': 'Saving...',

  // ── Form section cards ──
  'data.customReportEditor.card.basicInfo': 'Basic information',
  'data.customReportEditor.card.basicInfoDesc': 'Basic report metadata, shown in the list and on the view page',
  'data.customReportEditor.card.dataRange': 'Data range',
  'data.customReportEditor.card.dataRangeDesc': 'Time range, mailboxes and grouping used by the report',
  'data.customReportEditor.card.sections': 'Visualization modules',
  'data.customReportEditor.card.sectionsDesc': 'Choose the modules to display in the report',
  'data.customReportEditor.card.selectedBadge': '{selected} / {total} selected',

  // ── Field labels ──
  'data.customReportEditor.field.name': 'Report name',
  'data.customReportEditor.field.description': 'Description (optional)',
  'data.customReportEditor.field.category': 'Category',
  'data.customReportEditor.field.visibility': 'Visibility',
  'data.customReportEditor.field.team': 'Team',
  'data.customReportEditor.field.dateRange': 'Date range *',
  'data.customReportEditor.field.mailboxOptional': 'Mailbox (optional, leave blank for all)',
  'data.customReportEditor.field.groupBy': 'Grouping (trend chart)',

  // ── Input placeholders ──
  'data.customReportEditor.placeholder.name': 'e.g. Q4 agent performance monthly report',
  'data.customReportEditor.placeholder.description': 'Briefly describe the purpose and audience of this report',

  // ── Select options ──
  'data.customReportEditor.option.selectTeam': 'Select a team',
  'data.customReportEditor.option.groupNone': 'No grouping (daily)',
  'data.customReportEditor.option.groupWeek': 'Weekly',
  'data.customReportEditor.option.groupMonth': 'Monthly',

  // ── Category options ──
  'data.customReportEditor.category.performance': 'Performance',
  'data.customReportEditor.category.performanceDesc': 'Agent performance metrics',
  'data.customReportEditor.category.efficiency': 'Efficiency',
  'data.customReportEditor.category.efficiencyDesc': 'Team efficiency and throughput',
  'data.customReportEditor.category.customer': 'Customers',
  'data.customReportEditor.category.customerDesc': 'Customer insights',
  'data.customReportEditor.category.workload': 'Workload',
  'data.customReportEditor.category.workloadDesc': 'Conversation volume and load distribution',

  // ── Visibility options ──
  'data.customReportEditor.visibility.private': 'Private',
  'data.customReportEditor.visibility.privateDesc': 'Visible only to you',
  'data.customReportEditor.visibility.team': 'Team',
  'data.customReportEditor.visibility.teamDesc': 'Visible to members of the selected team',
  'data.customReportEditor.visibility.public': 'Public',
  'data.customReportEditor.visibility.publicDesc': 'Visible to the whole company',

  // ── Visualization module options (also used as the matching preview chart titles) ──
  'data.customReportEditor.module.summary': 'Core KPI',
  'data.customReportEditor.module.summaryDesc': 'Key business overview cards',
  'data.customReportEditor.module.trend': 'Conversation trend',
  'data.customReportEditor.module.trendDesc': 'New/closed/reply line chart',
  'data.customReportEditor.module.statusDistribution': 'Status distribution',
  'data.customReportEditor.module.statusDistributionDesc': 'Pie/bar chart by status',
  'data.customReportEditor.module.userPerformance': 'Agent performance',
  'data.customReportEditor.module.userPerformanceDesc': 'Agent performance table',
  'data.customReportEditor.module.mailboxLoad': 'Mailbox load',
  'data.customReportEditor.module.mailboxLoadDesc': 'Conversation volume per mailbox',
  'data.customReportEditor.module.customerDomains': 'Top customer domains',
  'data.customReportEditor.module.customerDomainsDesc': 'Distribution of customer source domains',

  // ── Preview panel ──
  'data.customReportEditor.preview.title': 'Live preview',
  'data.customReportEditor.preview.refresh': 'Refresh preview',
  'data.customReportEditor.preview.generating': 'Generating report...',
  'data.customReportEditor.preview.emptyHint':
    'After configuring data sources and charts, click "Preview" at the top right or "Refresh preview" on the right',
  'data.customReportEditor.preview.emptyHintSub': 'The report is computed live from the current configuration and is not saved',
  'data.customReportEditor.preview.emptySections': 'The report returned no sections; check the selected chart modules',
  'data.customReportEditor.preview.trendPoints': {
    one: 'Conversation trend ({count} point)',
    other: 'Conversation trend ({count} points)',
  },
  'data.customReportEditor.preview.userPerformanceTop': 'Top 5 agents',
  'data.customReportEditor.preview.replies': { one: '{count} reply', other: '{count} replies' },
  'data.customReportEditor.preview.customerDomainsTop': 'Top customer domains',
  // Fallback report name used when the preview request has an empty name
  'data.customReportEditor.preview.fallbackName': 'Preview',

  // ── Loading state ──
  'data.customReportEditor.loading.report': 'Loading report information...',

  // ── Form validation ──
  'data.customReportEditor.validation.nameRequired': 'Please enter a report name',
  'data.customReportEditor.validation.nameMaxLength': 'Name must be at most {max} characters',
  'data.customReportEditor.validation.categoryRequired': 'Please select a category',
  'data.customReportEditor.validation.visibilityRequired': 'Please select a visibility',
  'data.customReportEditor.validation.teamRequired': 'Please select a team',
  'data.customReportEditor.validation.startRequired': 'Please select a start date',
  'data.customReportEditor.validation.endRequired': 'Please select an end date',
  'data.customReportEditor.validation.rangeInvalid': 'The start date cannot be later than the end date',
  'data.customReportEditor.validation.sectionsRequired': 'Please select at least one chart module',

  // ── Toasts / errors ──
  'data.customReportEditor.toast.created': 'Custom report created',
  'data.customReportEditor.toast.updated': 'Changes saved',
  'data.customReportEditor.error.saveFailed': 'Save failed',
  'data.customReportEditor.error.previewFailed': 'Preview failed',
  'data.customReportEditor.error.missingEditId': 'Missing edit ID',
  'data.customReportEditor.error.fixFormErrors': 'Please fix the errors in the form',
  'data.customReportEditor.error.fixFormErrorsFirst': 'Please fix the form errors first',

  // ── Tips block ──
  'data.customReportEditor.tip.title': 'Tips',
  'data.customReportEditor.tip.previewNoSave': 'Click "Preview" to run the report live from the current configuration without saving',
  'data.customReportEditor.tip.savePersists':
    'Click "Save" to persist the report configuration; afterwards you can open the view page to schedule, export and pin it',
  'data.customReportEditor.tip.dateRangeAdvice': 'Keep the date range within 90 days for the best rendering performance',

  // ══ data.importExport.* —— 数据导入导出页 ══
  'data.importExport.title': 'Import and export',
  'data.importExport.description': 'Import and export system data as JSON for backup and restore',
  'data.importExport.loadFailed': 'Failed to load data',
  'data.importExport.selectTypeRequired': 'Select at least one data type to export',
  'data.importExport.fileTooLarge': 'File size must not exceed 5MB',
  'data.importExport.dataRequired': 'Provide the data to import',
  'data.importExport.previewFailed': 'Preview failed',
  'data.importExport.importFailed': 'Import failed',
  'data.importExport.exportSectionTitle': 'Data export',
  'data.importExport.exportSectionDescription': 'Select the data types to export; the system will generate a JSON file',
  'data.importExport.selectAll': 'Select all',
  // Split prefix/separator/suffix so the count keeps its own styled <span>; the spaces live in the values.
  'data.importExport.selectedPrefix': 'Selected ',
  'data.importExport.selectedSeparator': ' / ',
  'data.importExport.selectedSuffix': ' items',
  'data.importExport.exportAction': 'Export data',
  'data.importExport.importSectionTitle': 'Data import',
  'data.importExport.importSectionDescription': 'Import data into the system from a file or by pasting JSON',
  'data.importExport.tabFile': 'Upload file',
  'data.importExport.tabText': 'Paste data',
  'data.importExport.changeFile': 'Change file',
  'data.importExport.dropHint': 'Click or drag a file here to upload',
  'data.importExport.formatHint': 'JSON files are supported',
  'data.importExport.chooseFile': 'Choose file',
  // The newlines between these two values and the ASCII JSON skeleton stay in the component.
  'data.importExport.pasteTextHint': 'Paste JSON data...',
  'data.importExport.pasteExampleHint': 'Example format:',
  'data.importExport.confirmImport': 'Confirm import',
  'data.importExport.importing': 'Importing...',
  'data.importExport.previewAction': 'Preview data',
  'data.importExport.previewing': 'Previewing...',
  'data.importExport.previewTitle': 'Import preview',
  'data.importExport.previewCountPrefix': 'Total ',
  'data.importExport.previewCountSuffix': ' records',
  'data.importExport.previewCountBadgeSuffix': ' items',
  'data.importExport.resultTitle': 'Import result',
  'data.importExport.resultTotal': 'Total',
  'data.importExport.resultSucceeded': 'Succeeded',
  'data.importExport.resultFailed': 'Failed',
  'data.importExport.errorDetails': 'Error details:',
  'data.importExport.moreErrorsPrefix': 'and ',
  'data.importExport.moreErrorsSuffix': ' more errors...',
  // The backend ships Chinese `EntityTypeResponse.Name` / `.Description`; the page maps the
  // stable `type` code to these keys instead (see ENTITY_NAME_KEYS in ImportExport.tsx).
  'data.importExport.entity.customers.name': 'Customers',
  'data.importExport.entity.customers.description': 'Customer contact information',
  'data.importExport.entity.tags.name': 'Tags',
  'data.importExport.entity.tags.description': 'Conversation tags',
  'data.importExport.entity.canned_responses.name': 'Canned responses',
  'data.importExport.entity.canned_responses.description': 'Canned response templates',
  'data.importExport.entity.mail_templates.name': 'Mail templates',
  'data.importExport.entity.mail_templates.description': 'Email notification templates',
  'data.importExport.entity.email_signatures.name': 'Email signatures',
  'data.importExport.entity.email_signatures.description': 'Mailbox signatures',
  'data.importExport.entity.conversations.name': 'Conversations',
  'data.importExport.entity.conversations.description': 'Conversation list (bodies and attachments excluded)',
  'data.importExport.entity.knowledge_articles.name': 'Knowledge base articles',
  'data.importExport.entity.knowledge_articles.description': 'Article title / category / status / body (Markdown text)',
  'data.importExport.entity.users.name': 'Users',
  'data.importExport.entity.users.description': 'System users',

  'data.audit.entityType.messenger': 'Messenger',
  'data.audit.entityType.telegram': 'Telegram',
  'data.audit.entityType.line': 'LINE',
  'data.audit.entityType.imessage': 'Apple Messages',
} satisfies Entries;
