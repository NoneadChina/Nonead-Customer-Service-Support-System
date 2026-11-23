import type { Entries } from '../../i18n/types';

/**
 * Notification center copy (English values).
 * Must use `satisfies Entries`, never a type annotation.
 *
 * 与 `zh-Hans/notifications.ts` 逐键对齐：少一条即 `en/index.ts` 的
 * `Record<TranslationKey, Entry>` 报 TS2741。
 */
export const notifications = {
  // ── 事件类型 ──
  'notifications.eventType.newConversation': 'New conversation',
  'notifications.eventType.newReply': 'New reply',
  'notifications.eventType.conversationAssigned': 'Conversation assigned',
  'notifications.eventType.conversationUpdated': 'Conversation updated',
  'notifications.eventType.conversationClosed': 'Conversation closed',
  'notifications.eventType.customerReply': 'Customer reply',
  'notifications.eventType.mention': 'Mentioned',
  'notifications.eventType.slaViolation': 'SLA breach',
  'notifications.eventType.slaWarning': 'SLA warning',
  'notifications.eventType.conversationTimeout': 'Conversation timed out',
  'notifications.eventType.ticketEscalated': 'Ticket escalated',
  // Recognised by the backend switch but not exposed as a UI filter (nothing emits them yet);
  // kept so an unexpected code can never fall through to the raw identifier.
  'notifications.eventType.ticketCreated': 'Ticket created',
  'notifications.eventType.ticketUpdated': 'Ticket updated',
  'notifications.eventType.customerSatisfaction': 'Satisfaction rating',
  'notifications.eventType.autoReply': 'Auto reply',
  'notifications.eventType.userWelcome': 'New user welcome',
  'notifications.eventType.passwordReset': 'Password reset',
  'notifications.eventType.bulkImportCompleted': 'Bulk import completed',
  'notifications.eventType.conversationReopened': 'Conversation reopened',
  'notifications.eventType.conversationTransferred': 'Conversation transferred',
  'notifications.eventType.satisfactionSubmitted': 'Satisfaction rating submitted',
  'notifications.eventType.dailyReport': 'Daily report summary',
  'notifications.eventType.accountLocked': 'Account locked',
  'notifications.eventType.systemMaintenance': 'System maintenance',

  // ── 通用 ──
  'notifications.common.all': 'All',
  'notifications.common.new': 'New',
  'notifications.common.enabled': 'Enabled',
  'notifications.common.disabled': 'Disabled',
  'notifications.common.enable': 'Enable',
  'notifications.common.disable': 'Disable',
  'notifications.common.onlyEnabled': 'Enabled only',
  'notifications.common.onlyDisabled': 'Disabled only',
  'notifications.common.allEvents': '🗂 All events',
  'notifications.common.resetFilters': 'Reset filters',
  'notifications.common.clearSearch': 'Clear search',
  'notifications.common.currentFilters': 'Current filters:',
  'notifications.common.refresh': 'Refresh',
  'notifications.common.saving': 'Saving...',
  'notifications.common.selectEventType': 'Select an event type',
  'notifications.common.allMailboxes': 'All mailboxes',
  'notifications.common.unsetSubject': '(No subject)',
  'notifications.common.unbound': 'Not bound',
  'notifications.common.unboundTemplate': 'No template',
  'notifications.common.unknownError': 'Unknown error',

  // ── 表头 / 字段标签 ──
  'notifications.common.name': 'Name',
  'notifications.common.subject': 'Subject',
  'notifications.common.eventType': 'Event type',
  'notifications.common.status': 'Status',
  'notifications.common.updatedAt': 'Updated at',
  'notifications.common.actions': 'Actions',

  // ── 分页（三截文本包住两个加粗数字元素） ──
  'notifications.common.previousPage': 'Previous',
  'notifications.common.nextPage': 'Next',
  'notifications.common.paginationPrefix': 'Total ',
  'notifications.common.paginationMiddle': ' items (page ',
  'notifications.common.paginationSuffix': ')',

  // ── 垃圾箱 ──
  'notifications.common.trash': 'Trash',
  'notifications.common.moveToTrash': 'Move to trash',
  'notifications.common.restore': 'Restore',
  'notifications.common.deleteForever': 'Delete permanently',
  'notifications.common.emptyTrash': 'Empty trash',
  'notifications.common.deletedAt': 'Deleted at',

  // ── 错误 / 提示 ──
  'notifications.common.loadFailed': 'Failed to load: {message}',
  'notifications.common.saveFailed': 'Failed to save',
  'notifications.common.deleteFailed': 'Failed to delete',
  'notifications.common.copyFailed': 'Failed to copy: {message}',
  'notifications.common.opFailed': 'Operation failed: {message}',
  'notifications.common.previewFailed': 'Preview failed',
  'notifications.common.trashLoadFailed': 'Failed to load trash',
  'notifications.common.restoreFailed': 'Failed to restore',
  'notifications.common.clearFailed': 'Failed to empty trash',
  'notifications.common.copiedAs': 'Copied as "{name}"',

  // ── 顶栏个人通知铃铛 ──
  'notifications.bell.login': 'Sign in',
  'notifications.bell.logout': 'Sign out',
  'notifications.bell.create': 'Create',
  'notifications.bell.update': 'Update',
  'notifications.bell.assign': 'Assign',
  'notifications.bell.statusChange': 'Status change',
  'notifications.bell.reply': 'Reply',
  'notifications.bell.note': 'Note',
  'notifications.bell.title': 'Personal notifications',
  'notifications.bell.unreadCount': 'Personal notifications, {count} unread',
  'notifications.bell.unreadOnly': '{count} unread',
  'notifications.bell.allRead': 'All read',
  'notifications.bell.markAllRead': 'Mark all as read',
  'notifications.bell.empty': 'No notifications',
  'notifications.bell.emptyHint': 'Your activity will appear here',
  'notifications.bell.viewAllLogs': 'View all audit logs →',
  // =====================================================================
  // 通知模板页（pages/NotificationTemplates.tsx）
  // =====================================================================
  // ── 页头 / 标签页 ──
  'notifications.templates.title': 'Notification templates',
  'notifications.templates.subtitle':
    'Manage email templates for every notification event · 50+ built-in variables · live HTML preview · instant local variable substitution',
  'notifications.templates.heroBadge': 'Live rendering engine',
  'notifications.templates.tabOverview': 'Overview',
  'notifications.templates.tabList': 'Template list',

  // ── 概览 ──
  'notifications.templates.statTotal': 'Total templates',
  'notifications.templates.statTotalHint': 'All templates, including disabled ones',
  'notifications.templates.statActiveHint': 'Currently matched by notifications',
  'notifications.templates.statInactiveHint': 'Not matched by notifications',
  'notifications.templates.statUnit': 'templates',
  'notifications.templates.distributionTitle': 'Distribution by event type',
  'notifications.templates.distributionSubtitle':
    'Shows how many templates each event has and how many are enabled',
  'notifications.templates.activeRatio': 'Enabled share',
  'notifications.templates.inactiveRatio': 'Disabled share',
  'notifications.templates.ratioLabel': 'Share',

  // ── 空态 ──
  'notifications.templates.emptyNone': 'No templates',
  'notifications.templates.emptyOverviewHint': 'Create your first notification template on the list tab',
  'notifications.templates.emptyListFiltered': 'No templates match the current filters; try clearing them',
  'notifications.templates.emptyListNone': 'No templates yet; create the first one with the button above',

  // ── 筛选栏 ──
  'notifications.templates.filterSearch': 'Search: ',
  'notifications.templates.filterEvent': 'Event: ',
  'notifications.templates.filterStatus': 'Status: ',
  'notifications.templates.searchPlaceholder': 'Search template name or subject...',
  'notifications.templates.allEventTypes': 'All event types',
  'notifications.templates.loadingList': 'Loading templates...',
  'notifications.templates.clearAllFilters': 'Clear all filters',
  'notifications.templates.newTemplate': 'New template',

  // ── 表格 / 卡片 ──
  'notifications.templates.templateName': 'Template name',
  'notifications.templates.createdId': 'Created ID · {id}',
  'notifications.templates.subjectNotSet': '(Not set)',
  'notifications.templates.subjectCopied': 'Subject copied to clipboard',
  'notifications.templates.copySubject': 'Copy subject',
  'notifications.templates.clickToDisable': 'Click to disable',
  'notifications.templates.clickToEnable': 'Click to enable',
  'notifications.templates.previewTemplate': 'Preview template',
  'notifications.templates.duplicateTemplate': 'Duplicate template',
  'notifications.templates.editTemplate': 'Edit template',

  // ── 编辑器：标题栏 / 基础字段 ──
  'notifications.templates.editorTitleEdit': 'Edit notification template',
  'notifications.templates.editorTitleCreate': 'New notification template',
  'notifications.templates.editorBadge': 'HTML · Variables · Preview',
  'notifications.templates.editorSubtitle':
    'Rich HTML content, 50+ built-in variable substitutions, and instant local preview',
  'notifications.templates.closeEditor': 'Close editor',
  'notifications.templates.basicInfo': 'Template basics',
  'notifications.templates.requiredHint': 'Fields marked * are required',
  'notifications.templates.namePlaceholder': 'e.g. New conversation notification template - Support',
  'notifications.templates.fieldSubject': 'Notification subject',
  'notifications.templates.switchInsertTargetSubjectTitle':
    'Switch the variable insertion target to the subject line',
  'notifications.templates.insertTargetSubject': 'Insert target: Subject',
  'notifications.templates.switchToSubject': 'Switch to subject',
  'notifications.templates.subjectPlaceholder':
    'e.g. You have a new conversation #{conversation_number} - {subject}',
  'notifications.templates.subjectPreview': 'Subject preview',
  'notifications.templates.emptyValue': '(Empty)',

  // ── 编辑器：视图模式 / 代码与预览 ──
  'notifications.templates.viewModeEditor': 'Code only',
  'notifications.templates.viewModeEditorHint': 'Full-width code editor',
  'notifications.templates.viewModeSplit': 'Split view',
  'notifications.templates.viewModeSplitHint': 'Code and live preview',
  'notifications.templates.viewModePreview': 'Preview only',
  'notifications.templates.viewModePreviewHint': 'Live HTML rendering',
  'notifications.templates.insertTargetLabel': 'Insert target: ',
  'notifications.templates.htmlBody': 'HTML body',
  'notifications.templates.subjectLine': 'Subject line',
  'notifications.templates.htmlCodeEditor': 'HTML · Code editor',
  'notifications.templates.charUnit': 'chars',
  'notifications.templates.bodyPlaceholderEditor':
    'Supports HTML and variables, for example:\n<p>Hi <b>{customer_name}</b>,</p>\n<p>Conversation <a href="{conversation_url}">#{conversation_number}</a> has a new reply.</p>',
  'notifications.templates.mailtoPreview': '📧 mailto:customer@example.com · Live preview',
  'notifications.templates.localLivePreview': 'Local live preview',
  'notifications.templates.htmlPreviewArea': 'HTML preview',
  'notifications.templates.htmlPreviewAreaHint':
    'Enter HTML or insert variables in the code editor to see the rendered result here',
  'notifications.templates.codeLabel': 'Code',
  'notifications.templates.bodyPlaceholderSplit': 'e.g.\n<p>Hi <b>{customer_name}</b>,</p>',
  'notifications.templates.livePreview': 'Live preview',
  'notifications.templates.debounce200': 'Debounced 200ms',
  'notifications.templates.previewEmptyTitle': 'Live output appears here once you enter HTML',
  'notifications.templates.previewEmptyHint': 'Variables are replaced with sample data',

  // ── 编辑器：变量检测 / 启用开关 ──
  'notifications.templates.variableCheck': 'Variable check',
  'notifications.templates.variableCheckHint': 'Detects the variables used in the current subject and body',
  'notifications.templates.variablesCount': '{count} variables',
  'notifications.templates.commonVariablesCount': 'Common variables · {count}',
  'notifications.templates.uncommonVariablesCount': 'Uncommon variables · {count}',
  'notifications.templates.noVariablesDetected':
    'No variables detected yet; insert one from the panel on the right',
  'notifications.templates.templateActive': 'Template enabled',
  'notifications.templates.templateInactive': 'Template disabled',
  'notifications.templates.templateActiveHint':
    'This template is matched by notifications and renders the content when the event fires',
  'notifications.templates.templateInactiveHint':
    'While disabled, notification rules will not match this template; useful as a draft',

  // ── 编辑器：变量面板 ──
  'notifications.templates.availableVariables': 'Available variables',
  'notifications.templates.availableCountPrefix': 'Total ',
  'notifications.templates.availableCountSuffix': ' variables',
  'notifications.templates.variableSearchPlaceholder': 'Search variable name or category...',
  'notifications.templates.emailTitle': 'Email title',
  'notifications.templates.emailContent': 'Email content',
  'notifications.templates.noMatchingVariables': 'No matching variables',
  'notifications.templates.noMatchingVariablesHint': 'Try another keyword or clear the search',
  'notifications.templates.insertVariableTooltip': '{label} - Click to insert {variable} at the {target}',
  'notifications.templates.insertTargetSubjectEnd': 'end of the subject line',
  'notifications.templates.insertTargetBodyEnd': 'end of the HTML body',
  'notifications.templates.clickVariableToInsert': 'Click a variable to insert it at the ',

  // ── 编辑器：底部操作栏 ──
  'notifications.templates.backendRendering': 'Rendering on server...',
  'notifications.templates.backendFullPreview': 'Full server-side preview',
  'notifications.templates.saveTemplate': 'Save template',

  // ── Toast / 表单错误 ──
  'notifications.templates.overviewLoadFailed': 'Failed to load overview: {message}',
  'notifications.templates.listLoadFailed': 'Failed to load list: {message}',
  'notifications.templates.templateLoadFailed': 'Failed to load template: {message}',
  'notifications.templates.nameRequired': 'Enter a template name',
  'notifications.templates.subjectRequired': 'Enter an email subject',
  'notifications.templates.templateUpdated': 'Template updated',
  'notifications.templates.templateCreated': 'Template created',
  'notifications.templates.toggleSuccess': 'Template {state}',
  'notifications.templates.templateMovedToTrash': 'Template moved to trash',
  'notifications.templates.deleteFailedWithDetail': 'Failed to delete: {message}',
  'notifications.templates.templateRestored': 'Template "{name}" restored',
  'notifications.templates.templateHardDeleted': 'Template "{name}" permanently deleted',
  'notifications.templates.trashEmptied': 'Trash emptied: {count} items',

  // ── 垃圾箱 ──
  'notifications.templates.trashModalTitle': 'Notification template trash',
  'notifications.templates.trashCountPrefix': 'Trash holds ',
  'notifications.templates.trashCountSuffix': ' templates',
  'notifications.templates.trashSearchPlaceholder': 'Search deleted template name or subject...',
  'notifications.templates.keywordChip': 'Keyword: {keyword}',
  'notifications.templates.trashEmpty': 'Trash is empty',
  'notifications.templates.trashNoKeywordMatch': 'No match for this keyword; try another one',
  'notifications.templates.trashEmptyHint':
    'Deleted templates stay here until you restore them or delete them permanently',
  'notifications.templates.restoreToList': 'Restore to the template list',
  'notifications.templates.hardDeleteWarning': 'Delete permanently; this cannot be undone',

  // ── 确认弹窗 ──
  'notifications.templates.confirmMoveToTrash': 'Move notification template "{name}" to trash?',
  'notifications.templates.restoreTemplate': 'Restore template',
  'notifications.templates.confirmRestore': 'Restore notification template "{name}"?',
  'notifications.templates.confirmHardDelete':
    'Permanently delete notification template "{name}"? This cannot be undone.',
  'notifications.templates.confirmEmptyTrash':
    'Empty the trash? All deleted templates will be permanently removed and this cannot be undone.',
  'notifications.templates.emptyTrashConfirm': 'Empty',

  // ── 预览弹窗 ──
  'notifications.templates.templatePreview': 'Template preview',
  'notifications.templates.emailSubject': 'Email subject',
  'notifications.templates.emptySubject': '(No subject)',
  'notifications.templates.htmlPreviewTab': 'HTML preview',
  'notifications.templates.htmlSourceTab': 'HTML source',
  'notifications.templates.htmlSourceCode': 'HTML source code',
  'notifications.templates.htmlRenderResult': 'HTML render result',
  'notifications.templates.renderComplete': 'Render complete',
  'notifications.templates.bodyEmpty': 'Body is empty',
  'notifications.templates.bodyEmptyHintHtml': 'This template has no HTML content',
  'notifications.templates.bodyEmptyHintSource': 'This template has no HTML source',

  // ── 内置变量标签 ──
  'notifications.templates.var.conversationId': 'Conversation ID',
  'notifications.templates.var.conversationNumber': 'Conversation number',
  'notifications.templates.var.conversationSubject': 'Conversation subject',
  'notifications.templates.var.conversationUrl': 'Conversation link',
  'notifications.templates.var.createdAt': 'Created at',
  'notifications.templates.var.updatedAt': 'Updated at',
  'notifications.templates.var.priority': 'Priority',
  'notifications.templates.var.status': 'Status',
  'notifications.templates.var.tags': 'Tags',
  'notifications.templates.var.folderName': 'Folder',
  'notifications.templates.var.customerName': 'Customer name',
  'notifications.templates.var.customerEmail': 'Customer email',
  'notifications.templates.var.customerPhone': 'Customer phone',
  'notifications.templates.var.customerCompany': 'Customer company',
  'notifications.templates.var.userName': 'Agent name',
  'notifications.templates.var.userEmail': 'Agent email',
  'notifications.templates.var.userRole': 'Agent role',
  'notifications.templates.var.assignedUserName': 'Assignee name',
  'notifications.templates.var.assignedUserEmail': 'Assignee email',
  'notifications.templates.var.closedBy': 'Closed by',
  'notifications.templates.var.replyFrom': 'Reply from',
  'notifications.templates.var.mailboxName': 'Mailbox name',
  'notifications.templates.var.mailboxEmail': 'Mailbox address',
  'notifications.templates.var.messageBody': 'Raw message HTML',
  'notifications.templates.var.content': 'Message plain text',
  'notifications.templates.var.replyContent': 'Reply content',
  'notifications.templates.var.slaName': 'SLA name',
  'notifications.templates.var.warningTime': 'Warning time',
  'notifications.templates.var.violationTime': 'Breach time',
  'notifications.templates.var.firstResponseDeadline': 'First response deadline',
  'notifications.templates.var.resolveDeadline': 'Resolution deadline',
  'notifications.templates.var.companyName': 'Company name',
  'notifications.templates.var.loginUrl': 'Login link',
  'notifications.templates.var.systemUrl': 'System URL',
  'notifications.templates.var.supportEmail': 'Support email',
  'notifications.templates.var.satisfactionUrl': 'Satisfaction survey link',
  'notifications.templates.var.satisfactionScore': 'Satisfaction score',
  'notifications.templates.var.satisfactionComment': 'Satisfaction comment',
  'notifications.templates.var.resetPasswordUrl': 'Password reset link',
  'notifications.templates.var.accountLockReason': 'Lock reason',
  'notifications.templates.var.lockDuration': 'Lock duration',
  'notifications.templates.var.importTotal': 'Imported total',
  'notifications.templates.var.importSuccess': 'Imported successfully',
  'notifications.templates.var.importFailed': 'Failed imports',
  'notifications.templates.var.reportDate': 'Report date',
  'notifications.templates.var.maintenanceStart': 'Maintenance start',
  'notifications.templates.var.maintenanceEnd': 'Maintenance end',
  'notifications.templates.var.maintenanceContent': 'Maintenance details',

  // ── 内置变量分类 ──
  'notifications.templates.varCategory.conversation': 'Conversation',
  'notifications.templates.varCategory.customer': 'Customer',
  'notifications.templates.varCategory.agent': 'Agent',
  'notifications.templates.varCategory.mailbox': 'Mailbox',
  'notifications.templates.varCategory.message': 'Message',
  'notifications.templates.varCategory.sla': 'SLA',
  'notifications.templates.varCategory.brand': 'Branding',
  'notifications.templates.varCategory.satisfaction': 'Satisfaction',
  'notifications.templates.varCategory.account': 'Account',
  'notifications.templates.varCategory.import': 'Import & reports',
  'notifications.templates.varCategory.maintenance': 'Maintenance',

  // ── 本地实时预览示例数据 ──
  'notifications.templates.sample.subject': 'Test ticket - Sign-in issue',
  'notifications.templates.sample.status': 'In progress',
  'notifications.templates.sample.tags': 'technical support,sign-in',
  'notifications.templates.sample.folderName': 'Technical support',
  'notifications.templates.sample.customerName': 'John Doe',
  'notifications.templates.sample.customerCompany': 'Acme Corporation',
  'notifications.templates.sample.assignedUserName': 'Alex Chen',
  'notifications.templates.sample.replyFrom': 'Agent Alex',
  'notifications.templates.sample.mailboxName': 'Support mailbox',
  'notifications.templates.sample.messageBody':
    '<p>Hi, I cannot sign in to the system; it says the password is wrong.</p>',
  'notifications.templates.sample.content':
    'Hi, I cannot sign in to the system; it says the password is wrong.',
  'notifications.templates.sample.replyContent':
    'Hi, the issue is resolved; please see the attachment.',
  'notifications.templates.sample.slaName': 'Standard SLA',
  'notifications.templates.sample.warningTime': '30 minutes',
  'notifications.templates.sample.violationTime': '1 hour',
  'notifications.templates.sample.firstResponseDeadline': 'within 30 minutes',
  'notifications.templates.sample.resolveDeadline': 'within 4 hours',
  'notifications.templates.sample.satisfactionScore': '5 (very satisfied)',
  'notifications.templates.sample.satisfactionComment':
    'Great service and the issue was resolved quickly.',
  'notifications.templates.sample.accountLockReason': 'Too many failed sign-in attempts',
  'notifications.templates.sample.lockDuration': '30 minutes',
  'notifications.templates.sample.maintenanceContent': 'System upgrade and database optimization',

  // =====================================================================
  // 通知管理页（pages/Notifications.tsx）
  // =====================================================================
  // ── 页头 / Tab 容器 ──
  'notifications.list.pageTitle': 'Notification center · Notification management',
  'notifications.list.pageSubtitle':
    'Manage notification templates, trigger rules, manual sends and template previews in one place, so every customer service notification is precise, efficient and controllable',

  'notifications.list.tab.templates': 'Notification templates',
  'notifications.list.tab.templatesHint': 'Manage email and notification content templates',
  'notifications.list.tab.rules': 'Notification rules',
  'notifications.list.tab.rulesHint': 'Configure event triggers and matching logic',
  'notifications.list.tab.send': 'Send notification',
  'notifications.list.tab.sendHint': 'Trigger a notification send manually',
  'notifications.list.tab.preview': 'Template preview',
  'notifications.list.tab.previewHint': 'Render template variables in real time',

  // ── 跨 Tab 复用 ──
  'notifications.list.loadFailed': 'Failed to load',
  'notifications.list.opFailed': 'Operation failed',
  'notifications.list.refreshList': 'Refresh list',
  'notifications.list.newTemplate': 'New template',
  'notifications.list.newRule': 'New rule',
  'notifications.list.emptyFilteredHint': 'Try adjusting the filters',
  'notifications.list.statUnit': { one: 'item', other: 'items' },
  'notifications.list.allUsers': 'All users',
  'notifications.list.applicableMailbox': 'Mailbox scope',
  'notifications.list.notifyUser': 'Notify user',
  'notifications.list.useTemplate': 'Template',
  'notifications.list.noTemplate': 'No template',
  'notifications.list.conversationId': 'Conversation ID',
  'notifications.list.unspecified': 'Not specified',
  'notifications.list.customVariables': 'Custom variables',
  'notifications.list.varNamePlaceholder': 'Variable name',
  'notifications.list.varValuePlaceholder': 'Variable value',
  'notifications.list.removeVariable': 'Remove variable',
  'notifications.list.editTemplate': 'Edit template',
  'notifications.list.deleteTemplate': 'Delete template',
  'notifications.list.editRule': 'Edit rule',
  'notifications.list.deleteRule': 'Delete rule',

  // ── 模板 Tab ──
  'notifications.list.tplStatTotal': 'Total templates',
  'notifications.list.tplStatTotalSub': 'All configured templates',
  'notifications.list.tplStatActive': 'Enabled templates',
  'notifications.list.tplStatActiveSub': 'Available for rule matching',
  'notifications.list.tplStatInactive': 'Disabled templates',
  'notifications.list.tplStatInactiveSub': 'Excluded from matching',
  'notifications.list.tplSearchPlaceholder': 'Search template name or subject...',
  'notifications.list.tplEmpty': 'No notification templates',
  'notifications.list.tplEmptyFiltered': 'No matching templates',
  'notifications.list.tplEmptyHint': 'Click "New template" in the top right to get started',
  'notifications.list.tplMeta': 'Template configuration',

  'notifications.list.tplFormRequired': 'Name, subject and body are required',
  'notifications.list.tplModalEditTitle': 'Edit notification template',
  'notifications.list.tplModalNewTitle': 'New notification template',
  'notifications.list.tplFormEditHeading': 'Edit template',
  'notifications.list.tplFormNewHeading': 'Create a new template',
  'notifications.list.tplFormSubtitle': 'Configure the notification subject, body and bound event type',
  'notifications.list.tplNameLabel': 'Template name',
  'notifications.list.tplNamePlaceholder': 'e.g. New conversation notification for agents',
  'notifications.list.emailSubjectLabel': 'Email subject',
  'notifications.list.emailSubjectPlaceholder': 'Variables are supported, e.g. New conversation: {subject}',
  'notifications.list.emailBodyLabel': 'Email body',
  'notifications.list.emailBodyPlaceholder':
    'HTML and variables are supported, e.g. <p>Hi {customer_name}, your conversation has received a reply.</p>',
  'notifications.list.quickVariablesInsert': 'Quick variables (click to insert at the end of the body):',
  'notifications.list.clickToInsertBody': 'Click to insert into the body',
  'notifications.list.tplActiveLabel': 'Enable this template (allows notification rules to match it)',
  'notifications.list.confirmDeleteTemplate':
    'Delete the notification template "{name}"? Related rules will lose their template reference.',

  // ── 规则 Tab ──
  'notifications.list.ruleStatTotal': 'Total rules',
  'notifications.list.ruleStatTotalSub': 'All configured notification rules',
  'notifications.list.ruleStatActive': 'Enabled rules',
  'notifications.list.ruleStatActiveSub': 'Currently in effect',
  'notifications.list.ruleStatInactive': 'Disabled rules',
  'notifications.list.ruleStatInactiveSub': 'Not triggering notifications',
  'notifications.list.enableRate': 'Enable rate',
  'notifications.list.ruleSearchPlaceholder': 'Search rule name...',
  'notifications.list.ruleEmpty': 'No notification rules',
  'notifications.list.ruleEmptyFiltered': 'No matching rules',
  'notifications.list.ruleEmptyHint': 'Click "New rule" in the top right to get started',
  'notifications.list.ruleMeta': 'Event rule configuration',

  'notifications.list.ruleNameLabel': 'Rule name',
  'notifications.list.ruleNameRequired': 'Rule name is required',
  'notifications.list.ruleModalEditTitle': 'Edit notification rule',
  'notifications.list.ruleModalNewTitle': 'New notification rule',
  'notifications.list.ruleFormEditHeading': 'Edit rule',
  'notifications.list.ruleFormNewHeading': 'Create a new rule',
  'notifications.list.ruleFormSubtitle':
    'Configure the event trigger, matching scope and notification template',
  'notifications.list.ruleNamePlaceholder': 'e.g. Notify administrators about new conversations',
  'notifications.list.mailboxHint': 'Only events matching this mailbox will trigger',
  'notifications.list.notifyUserHint': 'When set, notifications are sent only to this user',
  'notifications.list.ruleActiveLabel':
    'Enable this rule (match and send notifications automatically when the event occurs)',
  'notifications.list.confirmDeleteRule': 'Delete the notification rule "{name}"?',

  // ── 发送通知 Tab ──
  'notifications.list.sendTitle': 'Send notification manually',
  'notifications.list.sendSubtitle':
    'Match enabled notification rules by event type and send notifications to target users immediately',
  'notifications.list.sendScopeRequired':
    'Select a mailbox or enter a conversation ID to determine the notification scope',
  'notifications.list.sendNoRules':
    'No notification rules matched. Configure an enabled rule for this event type in "Notification rules" first.',
  'notifications.list.sendAllFailedWithErrors':
    'Matched {rules} rules but sent 0 notifications, {failed} failed: {errors}',
  'notifications.list.sendAllFailedNoErrors':
    'Matched {rules} rules but sent 0 notifications. Check the rule recipients and mailbox configuration',
  'notifications.list.sendResultSuccess': 'Sent: {rules} rules matched, {sent} notifications sent.',
  'notifications.list.sendResultPartial': 'Finished: {rules} rules matched, {sent} sent, {failed} failed. {errors}',
  'notifications.list.sendFailed': 'Failed to send',
  'notifications.list.conversationIdOptional': 'Conversation ID (optional)',
  'notifications.list.conversationIdPlaceholder':
    'If set, the template is rendered with this conversation context',
  'notifications.list.conversationIdHint':
    'Leave empty to match all related conversations by event type and mailbox scope',
  'notifications.list.quickAddVariables': 'Quickly add common variables:',
  'notifications.list.varNamePlaceholderLong': 'Variable name (e.g. customer_name or {customer_name})',
  'notifications.list.emptyValue': '(empty)',
  'notifications.list.sending': 'Sending...',
  'notifications.list.sendNow': 'Send notification now',
  'notifications.list.currentConfig': 'Current configuration preview',
  'notifications.list.tips': 'Usage tips',
  'notifications.list.tip1': 'Only enabled rules can send successfully',
  'notifications.list.tip2': 'The conversation ID takes priority over the mailbox scope',
  'notifications.list.tip3': 'Custom variables override placeholders with the same name in the template',

  // ── 模板预览 Tab（含示例数据） ──
  'notifications.list.sampleTemplate':
    '<h2>Hello {customer_name},</h2>\n<p>Your conversation "<b>{subject}</b>" has received a reply.</p>\n<ul>\n  <li>Conversation ID: <code>{conversation_id}</code></li>\n  <li>Agent: <b>{user_name}</b></li>\n  <li>Mailbox: {mailbox_name} ({mailbox_email})</li>\n</ul>\n<p>Thank you for your patience.</p>',
  'notifications.list.sample.customerName': 'John Doe',
  'notifications.list.sample.subject': 'Order inquiry',
  'notifications.list.sample.agentName': 'Alex Chen',
  'notifications.list.sample.mailboxName': 'Customer support',

  'notifications.list.previewInputTitle': 'Template input',
  'notifications.list.previewInputHint':
    'Use the {variable} form to reference variables; HTML tags are supported',
  'notifications.list.templateContentLabel': 'Template content',
  'notifications.list.templateContentPlaceholder':
    'Enter template content, using the {variable} form to reference variables',
  'notifications.list.quickAddVariablesCreate': 'Quickly add variables (creates missing ones):',
  'notifications.list.variableDictionary': 'Variable dictionary',
  'notifications.list.noVariables': 'No variables yet. Use a quick-add button above or add one manually',
  'notifications.list.rendering': 'Rendering...',
  'notifications.list.renderPreview': 'Render preview',
  'notifications.list.renderResult': 'Render result',
  'notifications.list.renderResultSubtitle': 'The actual output after template variables are replaced',
  'notifications.list.copyResult': 'Copy result',
  'notifications.list.htmlPreview': 'HTML render preview',
  'notifications.list.rawText': 'Raw text',
  'notifications.list.waitingPreview': 'Waiting for a preview',
  'notifications.list.waitingPreviewHint':
    'Edit the template and variables, then click "Render preview" to see the result',

  // =====================================================================
  // 通知规则页（pages/NotificationRules.tsx）
  // =====================================================================
  // ── Header / tabs ──
  'notifications.rules.title': 'Notification rule management',
  'notifications.rules.subtitle': 'Configure notification trigger rules that match templates to events and send notifications automatically',
  'notifications.rules.tabOverview': 'Overview',
  'notifications.rules.tabList': 'Rule list',

  // ── Overview: stat cards ──
  'notifications.rules.statTotal': 'Total rules',
  'notifications.rules.statTotalHint': 'All configured notification rules',
  'notifications.rules.statActive': 'Enabled rules',
  'notifications.rules.statActiveHint': 'Currently in effect',
  'notifications.rules.statInactive': 'Disabled rules',
  'notifications.rules.statInactiveHint': 'Not sending notifications',
  'notifications.rules.countUnit': { one: 'rule', other: 'rules' },
  'notifications.rules.enableRate': 'Enable rate',

  // ── Overview: event type distribution ──
  'notifications.rules.eventDistribution': 'Event type distribution',
  'notifications.rules.eventSummary': '{rules} rules across {types} event types',
  'notifications.rules.emptyTitle': 'No rules yet',
  'notifications.rules.emptyHint': 'Click "New rule" at the top right to get started',
  // Trailing space kept on purpose: the label is followed directly by a <code> element.
  'notifications.rules.eventKeyLabel': 'Event key: ',
  'notifications.rules.ratio': '{percent}% of total',

  // ── List: filters ──
  'notifications.rules.searchPlaceholder': 'Search rule name, event, description...',
  'notifications.rules.newRule': 'New rule',
  'notifications.rules.emptyListTitle': 'No matching rules',
  'notifications.rules.emptyListHint': 'Try adjusting the filters or create a new rule',

  // ── List: table and row actions ──
  'notifications.rules.ruleName': 'Rule name',
  'notifications.rules.scope': 'Scope',
  'notifications.rules.linkedTemplate': 'Linked template',
  'notifications.rules.assignedUser': 'Specific user',
  'notifications.rules.toggleToDisable': 'Click to disable',
  'notifications.rules.toggleToEnable': 'Click to enable',
  'notifications.rules.copyRule': 'Duplicate rule',
  'notifications.rules.editRule': 'Edit rule',

  // ── Editor modal ──
  'notifications.rules.modalEditTitle': 'Edit notification rule',
  'notifications.rules.modalCreateTitle': '✨ New notification rule',
  'notifications.rules.modalEditSubtitle': 'Update the trigger and notification template of an existing rule',
  'notifications.rules.modalCreateSubtitle': 'Configure a new notification rule to be alerted about important events',
  'notifications.rules.activeOnSave': 'Enabled after save',
  'notifications.rules.auditNotice': 'All changes are recorded in the audit log',
  'notifications.rules.saveChanges': 'Save changes',
  'notifications.rules.createRule': 'Create rule',
  'notifications.rules.formErrorTitle': 'Check and fix the following',
  'notifications.rules.sectionBasic': 'Basic information',
  'notifications.rules.sectionBasicHint': 'Configure the rule name and trigger event',
  'notifications.rules.nameHint': '· Used to identify this rule',
  'notifications.rules.namePlaceholder': 'e.g. New conversation notifies admins - ticket response SLA alert',
  'notifications.rules.eventTypeHint': '· The event that triggers this rule',
  'notifications.rules.sectionScopeHint': 'Restrict the scope and notification template of the rule',
  'notifications.rules.mailboxLabel': 'Mailbox',
  'notifications.rules.mailboxHint': '· Leave empty to apply globally',
  'notifications.rules.allMailboxesOption': '🌐 All mailboxes (events from any mailbox trigger this rule)',
  'notifications.rules.templateLabel': 'Notification template',
  'notifications.rules.templateHint': '· Renders the notification content',
  'notifications.rules.noTemplateOption': '✖ No template (use the default message format)',
  'notifications.rules.enableRule': 'Enable this rule',
  'notifications.rules.enableRuleOn': 'The rule takes effect right after saving and sends a notification when the event fires',
  'notifications.rules.enableRuleOff': 'The rule stays disabled after saving and does not send any notification',
  'notifications.rules.previewTitle': 'Configuration preview',
  'notifications.rules.notSet': 'Not set',
  'notifications.rules.previewEvent': 'Trigger event',
  'notifications.rules.previewStatus': 'Status after save',
  'notifications.rules.previewStatusActive': 'Enabled · in effect',

  // ── Confirmation dialogs ──
  'notifications.rules.deleteMessage': 'Move notification rule "{name}" to trash?',
  'notifications.rules.restoreTitle': 'Restore rule',
  'notifications.rules.restoreMessage': 'Restore notification rule "{name}"?',
  'notifications.rules.hardDeleteMessage': 'Permanently delete notification rule "{name}"? This action cannot be undone.',
  'notifications.rules.clearTrashMessage': 'Empty the trash? All deleted rules will be permanently deleted. This action cannot be undone.',
  'notifications.rules.confirmClear': 'Empty',

  // ── Trash modal ──
  'notifications.rules.trashTitle': 'Notification rule trash',
  'notifications.rules.trashSubtitle': 'Deleted rules are kept here for 30 days and can be restored or permanently deleted at any time',
  'notifications.rules.trashSearchPlaceholder': '🔍 Search deleted rule name, event, template...',
  'notifications.rules.trashLoading': 'Loading trash...',
  'notifications.rules.trashEmptyTitle': 'The trash is empty ✨',
  'notifications.rules.trashEmptySearch': 'No deleted rules match the search. Try another keyword',
  'notifications.rules.trashEmptyAllCleared': 'All deleted rules have been cleaned up',
  'notifications.rules.usedSpace': 'Used space: {count} items / ∞',
  // Trailing space kept on purpose: the label is followed directly by a styled <span>.
  'notifications.rules.templatePrefix': 'Template: ',
  'notifications.rules.restoreToMainList': 'Restore to the main list',
  'notifications.rules.hardDeleteHint': 'Delete permanently (cannot be undone)',

  // ── Toasts / form errors ──
  'notifications.rules.nameRequired': 'Enter a rule name',
  'notifications.rules.updateSuccess': 'Rule updated',
  'notifications.rules.createSuccess': 'Rule created',
  'notifications.rules.enabledSuccess': 'Rule enabled',
  'notifications.rules.disabledSuccess': 'Rule disabled',
  'notifications.rules.moveToTrashSuccess': 'Rule moved to trash',
  'notifications.rules.deleteFailedWithReason': 'Failed to delete: {message}',
  'notifications.rules.restoreSuccess': 'Rule "{name}" restored',
  'notifications.rules.hardDeleteSuccess': 'Rule "{name}" permanently deleted',
  'notifications.rules.clearTrashSuccess': 'Trash emptied: {count} items removed',
  'notifications.rules.toastSuccess': '✓ Success',
  'notifications.rules.toastError': '✕ Failed',
} satisfies Entries;
