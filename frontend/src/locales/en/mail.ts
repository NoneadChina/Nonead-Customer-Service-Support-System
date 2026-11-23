import type { Entries } from '../../i18n/types';

/**
 * 邮件事务域英文译文。键集合必须与 `zh-Hans/mail.ts` **逐条相等**
 * （由 `en/index.ts` 的 `Record<TranslationKey, Entry>` 标注在编译期强制）。
 *
 * 术语（设计 §10）：Mailbox / Folder / Email template / Email signature /
 * Canned response（**不是** Quick reply）/ Trash / Agent / Customer。
 */
export const mail = {
  // ════════════════════════════════════════════════════════════════
  // common
  // ════════════════════════════════════════════════════════════════

  'mail.common.actions': 'Actions',
  'mail.common.status': 'Status',
  'mail.common.type': 'Type',
  'mail.common.overview': 'Overview',
  'mail.common.system': 'System',
  'mail.common.custom': 'Custom',
  'mail.common.global': 'Global',
  'mail.common.active': 'Active',
  'mail.common.enabled': 'Enabled',
  'mail.common.disabled': 'Disabled',
  'mail.common.preview': 'Preview',
  'mail.common.allStatuses': 'All statuses',
  'mail.common.applicableMailbox': 'Mailbox',
  'mail.common.ownerMailbox': 'Mailbox',
  'mail.common.ownerUser': 'User',
  'mail.common.globalAllMailboxes': 'All mailboxes',
  'mail.common.mailboxAndNature': 'Mailbox / type',
  'mail.common.mailboxAndVisibility': 'Mailbox / visibility',
  'mail.common.deletedAtPrefix': 'Deleted:',
  'mail.common.createdAtPrefix': 'Created:',
  'mail.common.updatedAtPrefix': 'Updated:',

  'mail.common.currentFilters': 'Current filters',
  'mail.common.currentFiltersColon': 'Current filters: ',
  'mail.common.clearAllFilters': 'Clear all',
  'mail.common.removeFilter': 'Remove this filter',
  'mail.common.searchPrefix': 'Search: ',

  'mail.common.trash': 'Trash',
  'mail.common.recycleBin': 'Trash',
  'mail.common.clearTrash': 'Empty trash',
  'mail.common.clearAll': 'Empty all',
  'mail.common.trashEmpty': 'Trash is empty',
  'mail.common.loadingTrash': 'Loading trash…',
  'mail.common.searchTrash': 'Search trash',
  'mail.common.trashHeaderDeletion': 'Deletion',
  'mail.common.perPage': 'Per page',
  'mail.common.trashPageSizeOption': '{count} items',
  'mail.common.trashSortOrderPrefix': 'Sort #',
  'mail.common.trashFooterTotalPrefix': 'Total ',
  'mail.common.trashFooterTotalSuffix': ' items deleted',
  'mail.common.trashRangePrefix': 'Page ',
  'mail.common.trashRangeSeparator': '–',
  'mail.common.trashRangeSuffix': ' items',
  'mail.common.trashRetentionMiddle': ' or ',
  'mail.common.trashRetentionSuffix': '.',

  'mail.common.restore': 'Restore',
  'mail.common.hardDelete': 'Delete permanently',
  'mail.common.hardDeleteIrreversible': 'Delete permanently (irreversible)',

  'mail.common.trashCleared': 'Trash emptied: {count} items',
  'mail.common.deleteFailed': 'Delete failed',
  'mail.common.restoreFailed': 'Restore failed',
  'mail.common.hardDeleteFailed': 'Permanent delete failed',
  'mail.common.clearTrashFailed': 'Empty trash failed',
  'mail.common.saveFailed': 'Save failed',
  'mail.common.loadFailed': 'Load failed',
  'mail.common.operationFailed': 'Operation failed',

  // ════════════════════════════════════════════════════════════════
  // mailboxes —— pages/Mailboxes.tsx + context/MailboxContext.tsx
  // ════════════════════════════════════════════════════════════════

// ════════ page — header, page actions, stat cards ════════
  // Sentinel used by the captain in context/MailboxContext.tsx
  'mail.mailboxes.allMailboxes': 'All mailboxes',
  'mail.mailboxes.tabAll': 'All mailboxes',
  'mail.mailboxes.page.title': 'Mailbox management',
  'mail.mailboxes.page.badge': 'Mail center',
  'mail.mailboxes.page.subtitle':
    'Manage the support mailboxes that receive and send mail: IMAP and SMTP servers, auto replies, OAuth',
  'mail.mailboxes.page.refreshTitle': 'Refresh the mailbox list',
  'mail.mailboxes.refresh': 'Refresh',
  'mail.mailboxes.newMailbox': 'New mailbox',
  'mail.mailboxes.editMailbox': 'Edit mailbox',
  'mail.mailboxes.requestFailed': 'Request failed',

  'mail.mailboxes.stat.total': 'Total mailboxes',
  'mail.mailboxes.stat.totalHint': 'All configured mailboxes',
  'mail.mailboxes.stat.active': 'Active mailboxes',
  'mail.mailboxes.stat.activeHint': '{count} disabled',
  'mail.mailboxes.stat.conversations': 'Total conversations',
  'mail.mailboxes.stat.conversationsHint': 'Across all mailboxes',
  'mail.mailboxes.stat.todaySent': 'Sent today',
  'mail.mailboxes.stat.todaySentHint': 'Emails sent today',
  'mail.mailboxes.stat.totalConversations': 'Total conversations',
  'mail.mailboxes.stat.open': 'Open',

  // ════════ page — filter bar and search ════════
  'mail.mailboxes.filter.label': 'Filter',
  'mail.mailboxes.filter.statusAll': 'All',
  'mail.mailboxes.filter.statusActive': 'Status: active',
  'mail.mailboxes.filter.statusDisabled': 'Status: disabled',
  'mail.mailboxes.searchPlaceholder': 'Search by mailbox name or address...',
  // Precedes a number that keeps its own <b> element, so only the prefix lives here
  'mail.mailboxes.countPrefix': 'Showing',

  // ════════ list — list, empty states, cards ════════
  'mail.mailboxes.loadingList': 'Loading mailboxes...',
  'mail.mailboxes.empty.noMatch': 'No matching mailboxes',
  'mail.mailboxes.empty.noMatchHint': 'Try adjusting the search keyword or the filters',
  'mail.mailboxes.empty.noMailboxes': 'No mailboxes yet',
  'mail.mailboxes.empty.noMailboxesHint': 'Click "New mailbox" in the top right to get started',
  'mail.mailboxes.empty.trashHint': 'Deleted mailboxes will appear here',
  'mail.mailboxes.clearFilters': 'Clear filters',
  'mail.mailboxes.testConnection': 'Test connection',
  'mail.mailboxes.testSuccess': 'Connection succeeded',
  'mail.mailboxes.testFailed': 'Connection failed',
  'mail.mailboxes.notConfigured': 'Not configured',
  'mail.mailboxes.fetchEvery': 'Every {count} min',
  'mail.mailboxes.lastFetchAt': 'Last fetch: {time}',
  'mail.mailboxes.authorizedUsersTitle': {
    one: '{count} authorized user',
    other: '{count} authorized users',
  },

  // ════════ form — create / edit dialog ════════
  'mail.mailboxes.form.editTitle': 'Edit mailbox configuration',
  'mail.mailboxes.form.createTitle': 'Create a new mailbox account',
  'mail.mailboxes.form.editSubtitle':
    'Change the mailbox details, server settings, auto reply and authorized users',
  'mail.mailboxes.form.createSubtitle':
    'Configure IMAP and SMTP servers, auto reply, OAuth and more',
  'mail.mailboxes.form.saving': 'Saving…',
  'mail.mailboxes.form.basicInfo': 'Basic information',
  'mail.mailboxes.form.basicInfoHint': 'Mailbox name, address and enabled state',
  'mail.mailboxes.form.name': 'Name',
  'mail.mailboxes.form.namePlaceholder': 'e.g. Technical support',
  'mail.mailboxes.form.email': 'Email address',
  'mail.mailboxes.form.enableMailbox': 'Enable this mailbox',
  'mail.mailboxes.form.serverHost': 'Server address',
  'mail.mailboxes.form.port': 'Port',
  'mail.mailboxes.form.encryption': 'Encryption',
  'mail.mailboxes.form.username': 'Username',
  'mail.mailboxes.form.password': 'Password',
  'mail.mailboxes.form.alreadySet': 'Set',
  'mail.mailboxes.form.passwordPlaceholder': 'Enter the mailbox password or app password',
  'mail.mailboxes.form.passwordKeepPlaceholder': 'Leave blank to keep the current password',
  'mail.mailboxes.form.authCodeHint':
    'Some providers (QQ, 163, Gmail) require an app password instead of the login password',
  'mail.mailboxes.form.validateCert': 'Verify the SSL certificate',
  'mail.mailboxes.form.sentFolder': 'IMAP sent folder',
  'mail.mailboxes.form.fetchFrequency': 'Fetch frequency',
  'mail.mailboxes.form.minutesOption': '{count} min',

  // ════════ form — incoming / outgoing server sections ════════
  'mail.mailboxes.form.inboundTitle': 'Incoming server (IMAP)',
  'mail.mailboxes.form.inboundHint': 'Server settings for receiving mail',
  'mail.mailboxes.form.outboundTitle': 'Outgoing server (SMTP)',
  'mail.mailboxes.form.outboundHint': 'Server settings for sending mail',

  // ════════ form — aliases and BCC ════════
  'mail.mailboxes.form.aliasesTitle': 'Aliases and BCC',
  'mail.mailboxes.form.aliasesHint': 'Configure mailbox aliases and automatic BCC',
  'mail.mailboxes.form.alias': 'Alias',
  'mail.mailboxes.form.aliasReply': 'Alias reply address',
  'mail.mailboxes.form.autoBcc': 'Automatic BCC',

  // ════════ form — auto reply ════════
  'mail.mailboxes.form.autoReplyTitle': 'Auto reply',
  'mail.mailboxes.form.autoReplyHint': 'The reply sent automatically when mail arrives',
  'mail.mailboxes.form.autoReplyEnabled': 'Enable auto reply',
  'mail.mailboxes.form.autoReplySubject': 'Reply subject',
  'mail.mailboxes.form.autoReplySubjectPlaceholder': 'Auto reply: we received your email',
  'mail.mailboxes.form.autoReplyMessage': 'Reply body',
  'mail.mailboxes.form.autoReplyMessagePlaceholder':
    'Thank you for your message. We will get back to you as soon as possible...',

  // ════════ form — signature ════════
  'mail.mailboxes.form.signatureTitle': 'Signature settings',
  'mail.mailboxes.form.signatureHint': 'Text before reply and email signature',
  'mail.mailboxes.form.beforeReply': 'Text before reply',
  'mail.mailboxes.form.beforeReplyPlaceholder': 'Text shown before the quoted reply',
  'mail.mailboxes.form.signature': 'Signature',
  'mail.mailboxes.form.signaturePlaceholder': '--\nCompany name',

  // ════════ form — authorized users ════════
  'mail.mailboxes.form.authorizedUsersTitle': 'Authorized users',
  'mail.mailboxes.form.authorizedUsersHint': 'Agents who can access this mailbox',
  'mail.mailboxes.form.selectedUsers': {
    one: '{count} authorized user selected',
    other: '{count} authorized users selected',
  },

  // ════════ encryption — keyed by the code the page binds ════════
  'mail.mailboxes.encryption.none': 'No encryption',
  'mail.mailboxes.encryption.ssl': 'SSL',
  'mail.mailboxes.encryption.tls': 'TLS',

  // ════════ oauth ════════
  'mail.mailboxes.oauth.title': 'OAuth authentication',
  'mail.mailboxes.oauth.hint':
    'Authenticate the mailbox with OAuth (for example Gmail or Outlook)',
  'mail.mailboxes.oauth.enabled': 'Enable OAuth authentication',
  'mail.mailboxes.oauth.provider': 'OAuth provider',
  'mail.mailboxes.oauth.selectPlaceholder': 'Select',
  'mail.mailboxes.oauth.other': 'Other',
  'mail.mailboxes.oauth.keepPlaceholder': 'Leave blank to keep the current value',

  // ════════ trash — confirmation dialogs ════════
  'mail.mailboxes.delete.title': 'Delete mailbox',
  'mail.mailboxes.delete.message':
    'Delete the mailbox "{name}" ({email})? It will be moved to the trash and can be restored from there.',
  'mail.mailboxes.restore.title': 'Restore mailbox',
  'mail.mailboxes.restore.message':
    'Restore the mailbox "{name}" ({email})? It will reappear in the mailbox list.',
  'mail.mailboxes.hardDelete.title': 'Delete mailbox permanently',
  'mail.mailboxes.hardDelete.message':
    'Permanently delete the mailbox "{name}" ({email})? This cannot be undone and the related data will be lost permanently.',
  'mail.mailboxes.clearTrash.message': {
    one: 'Empty the trash? The {count} deleted mailbox will be lost permanently and this cannot be undone.',
    other:
      'Empty the trash? All {count} deleted mailboxes will be lost permanently and this cannot be undone.',
  },
  'mail.mailboxes.clearTrash.confirm': 'Empty',

  // ════════ validation ════════
  'mail.mailboxes.validation.nameRequired': 'Mailbox name is required',
  'mail.mailboxes.validation.emailRequired': 'Email address is required',
  'mail.mailboxes.validation.hostRequired': 'Incoming and outgoing server addresses are required',

  // ════════════════════════════════════════════════════════════════
  // templates —— pages/MailTemplates.tsx + components/MailTemplateTrashModal.tsx
  // ════════════════════════════════════════════════════════════════

// ════════ page ════════
  'mail.templates.page.title': 'Email templates',
  'mail.templates.page.badge': 'Mail center',
  'mail.templates.page.subtitle':
    'Manage email notification templates with custom variables, HTML editing and live preview',
  'mail.templates.page.create': 'Create template',
  'mail.templates.page.tabList': 'Template list',
  'mail.templates.page.loadingOverview': 'Loading overview...',
  'mail.templates.page.byType': 'Distribution by type',
  'mail.templates.page.totalTemplates': '{count} templates',
  'mail.templates.page.searchPlaceholder': 'Search template name...',
  'mail.templates.page.allTypes': 'All types',
  'mail.templates.page.statusEnabled': 'Enabled',
  'mail.templates.page.statusDisabled': 'Disabled',
  'mail.templates.page.statsTotal': 'Total templates',
  'mail.templates.page.statsTotalHint': 'All email templates',
  'mail.templates.page.statsActiveHint': 'Currently in effect',
  'mail.templates.page.statsSystem': 'System templates',
  'mail.templates.page.statsSystemHint': 'Built in, cannot be deleted',
  'mail.templates.page.statsCustom': 'Custom templates',
  'mail.templates.page.statsCustomHint': 'Created by users',

  // ════════ list ════════
  'mail.templates.list.emptyFilteredTitle': 'No matching templates',
  'mail.templates.list.emptyTitle': 'No email templates',
  'mail.templates.list.emptyFilteredSubtitle': 'Try a different keyword or clear the filters',
  'mail.templates.list.emptySubtitle':
    'Select Create template in the top right to add your first email template',
  'mail.templates.list.createFirst': 'Create the first template',
  'mail.templates.list.colName': 'Template name',
  'mail.templates.list.colSlug': 'Slug',
  'mail.templates.list.colSource': 'Source',
  'mail.templates.list.systemNotDeletable': 'System templates cannot be deleted',
  'mail.templates.list.footerTotalPrefix': 'Total ',
  'mail.templates.list.footerPagePrefix': ' items, page ',
  'mail.templates.list.footerPageSuffix': '/{total}',
  'mail.templates.list.prevPage': 'Previous',
  'mail.templates.list.nextPage': 'Next',

  // ════════ editor ════════
  'mail.templates.editor.titleEdit': 'Edit template',
  'mail.templates.editor.titleCreate': 'Create template',
  'mail.templates.editor.editing': 'Editing: {name}',
  'mail.templates.editor.createHint': 'Fill in the details below to create an email template',
  'mail.templates.editor.basicInfo': 'Basic information',
  'mail.templates.editor.nameLabel': 'Template name',
  'mail.templates.editor.namePlaceholder': 'For example: New conversation email',
  'mail.templates.editor.slugLabel': 'Template slug',
  'mail.templates.editor.slugPlaceholder': 'For example: new_conversation_notification',
  'mail.templates.editor.typeLabel': 'Template type',
  'mail.templates.editor.subjectLabel': 'Email subject',
  'mail.templates.editor.subjectPlaceholder': 'For example: [New conversation] {{subject}}',
  'mail.templates.editor.descriptionLabel': 'Description',
  'mail.templates.editor.descriptionPlaceholder': 'Optional, what this template is for',
  'mail.templates.editor.modeHtml': 'HTML',
  'mail.templates.editor.modeText': 'Plain text',
  'mail.templates.editor.preview': 'Preview',
  'mail.templates.editor.htmlPlaceholder':
    '<html><body>...Write the HTML template body here...</body></html>',
  'mail.templates.editor.textPlaceholder': '...Write the plain text template body here...',
  'mail.templates.editor.availableVariables': 'Available variables',
  'mail.templates.editor.variableCount': '{count} variables',
  'mail.templates.editor.clickToInsert': 'Select a variable to insert it into the editor',
  'mail.templates.editor.enableTemplate': 'Enable this template',
  'mail.templates.editor.enableHint': 'The template is not used while disabled',
  'mail.templates.editor.sortOrderLabel': 'Sort order:',
  'mail.templates.editor.saving': 'Saving...',

  // ════════ preview ════════
  'mail.templates.preview.title': 'Template preview',
  'mail.templates.preview.subtitle': 'Rendered with sample data',
  'mail.templates.preview.subject': 'Email subject',
  'mail.templates.preview.html': 'HTML preview',
  'mail.templates.preview.raw': 'Source',
  'mail.templates.preview.sampleSubject': 'Sample ticket - Sign-in problem',
  'mail.templates.preview.sampleCustomerName': 'Sample customer',
  'mail.templates.preview.sampleMailboxName': 'Support mailbox',
  'mail.templates.preview.sampleMessageBody':
    '<p>Hello, I cannot sign in. The system says my password is incorrect.</p>',

  // ════════ delete ════════
  'mail.templates.delete.title': 'Delete email template',
  'mail.templates.delete.message':
    'Delete template "{name}"? It moves to the trash, where you can restore or delete it permanently.',

  // ════════ toast ════════
  'mail.templates.toast.restored': 'Template "{name}" restored',
  'mail.templates.toast.hardDeleted': 'Template "{name}" permanently deleted',
  'mail.templates.toast.requiredFields': 'Enter the template name, slug and subject',
  'mail.templates.toast.updated': 'Template updated',
  'mail.templates.toast.created': 'Template created',
  'mail.templates.toast.movedToTrash': 'Template "{name}" moved to the trash',
  'mail.templates.toast.previewFailed': 'Preview failed',

  // ════════ trash ════════
  'mail.templates.trash.title': 'Trash (email templates)',
  'mail.templates.trash.heading': 'Deleted email templates',
  'mail.templates.trash.retentionPrefix': 'Deleted templates stay in the trash. You can ',
  'mail.templates.trash.pendingSuffix': ' deleted templates pending',
  'mail.templates.trash.searchPlaceholder': 'Search deleted templates (name / slug / subject)',
  'mail.templates.trash.clearAll': 'Clear all',
  'mail.templates.trash.colTemplate': 'Template',
  'mail.templates.trash.emptyFiltered': 'No matching deleted templates',
  'mail.templates.trash.emptyHint': 'Deleted email templates appear here',
  'mail.templates.trash.unknownType': 'Type {type}',
  'mail.templates.trash.inactive': 'Disabled',
  'mail.templates.trash.restoreTooltip': 'Restore template',
  'mail.templates.trash.restoreTitle': 'Restore email template',
  'mail.templates.trash.restoreMessage':
    'Restore template "{name}" to the template list? It becomes available in the email template list again.',
  'mail.templates.trash.hardDeleteMessage':
    'Template "{name}" is deleted permanently and cannot be recovered. Continue?',
  'mail.templates.trash.clearMessage':
    'All {count} templates are deleted permanently and cannot be recovered. Empty the whole trash?',

  // ════════ type ════════
  'mail.templates.type.newConversation': 'New conversation',
  'mail.templates.type.newReply': 'New reply',
  'mail.templates.type.conversationAssigned': 'Conversation assigned',
  'mail.templates.type.conversationClosed': 'Conversation closed',
  'mail.templates.type.satisfactionSurvey': 'Satisfaction survey',
  'mail.templates.type.passwordReset': 'Password reset',
  'mail.templates.type.welcome': 'Welcome email',
  'mail.templates.type.calendarReminder': 'Calendar reminder',
  'mail.templates.type.chatSessionClosed': 'Chat session closed',
  'mail.templates.type.custom': 'Custom template',

  // ════════ typeCategory ════════
  'mail.templates.typeCategory.notification': 'Notification',
  'mail.templates.typeCategory.survey': 'Survey',
  'mail.templates.typeCategory.system': 'System',
  'mail.templates.typeCategory.custom': 'Custom',

  // ════════ variable ════════
  'mail.templates.variable.subject': 'Conversation subject',
  'mail.templates.variable.conversationId': 'Conversation ID',
  'mail.templates.variable.conversationUrl': 'Conversation link',
  'mail.templates.variable.priority': 'Priority',
  'mail.templates.variable.updateContent': 'Update content',
  'mail.templates.variable.customerName': 'Customer name',
  'mail.templates.variable.customerEmail': 'Customer email',
  'mail.templates.variable.customerPhone': 'Customer phone',
  'mail.templates.variable.userName': 'Agent name',
  'mail.templates.variable.userEmail': 'Agent email',
  'mail.templates.variable.userRole': 'Agent role',
  'mail.templates.variable.mailboxName': 'Mailbox name',
  'mail.templates.variable.mailboxEmail': 'Mailbox address',
  'mail.templates.variable.messageBody': 'Latest message',
  'mail.templates.variable.firstMessageBody': 'First message',
  'mail.templates.variable.assigneeName': 'Assignee name',
  'mail.templates.variable.assignerName': 'Assigner name',
  'mail.templates.variable.replierName': 'Replier name',
  'mail.templates.variable.closerName': 'Closer name',
  'mail.templates.variable.updaterName': 'Updater name',
  'mail.templates.variable.mentionerName': 'Mentioner name',
  'mail.templates.variable.escalatedBy': 'Escalated by',
  'mail.templates.variable.slaName': 'SLA policy name',
  'mail.templates.variable.slaLevel': 'SLA priority',
  'mail.templates.variable.slaRemaining': 'SLA time remaining',
  'mail.templates.variable.slaDeadline': 'SLA deadline',
  'mail.templates.variable.slaResponseTime': 'SLA response time',
  'mail.templates.variable.remainingTime': 'Time remaining',
  'mail.templates.variable.violationType': 'Violation type',
  'mail.templates.variable.satisfactionUrl': 'Satisfaction rating link',
  'mail.templates.variable.satisfactionRating': 'Satisfaction rating',
  'mail.templates.variable.satisfactionRatingValue': 'Satisfaction score',
  'mail.templates.variable.satisfactionComment': 'Satisfaction comment',
  'mail.templates.variable.ratingUrl': 'Rating link',
  'mail.templates.variable.feedbackUrl': 'Feedback link',
  'mail.templates.variable.ratingStars': 'Rating stars',
  'mail.templates.variable.ratingLabel': 'Rating label',
  'mail.templates.variable.ratingComment': 'Rating comment',
  'mail.templates.variable.escalationReason': 'Escalation reason',
  'mail.templates.variable.escalatedAt': 'Escalated at',
  'mail.templates.variable.chatSessionId': 'Chat session ID',
  'mail.templates.variable.chatCreatedAt': 'Chat created at',
  'mail.templates.variable.agentName': 'Assigned agent',
  'mail.templates.variable.chatSubject': 'Chat subject',
  'mail.templates.variable.eventTitle': 'Event title',
  'mail.templates.variable.eventType': 'Event type',
  'mail.templates.variable.eventStartTime': 'Start time',
  'mail.templates.variable.eventEndTime': 'End time',
  'mail.templates.variable.eventLocation': 'Event location',
  'mail.templates.variable.eventMeetingUrl': 'Meeting link',
  'mail.templates.variable.eventDescription': 'Event description',
  'mail.templates.variable.eventPriority': 'Event priority',
  'mail.templates.variable.eventUrl': 'Event link',
  'mail.templates.variable.createdAt': 'Created at',
  'mail.templates.variable.receivedTime': 'Received at',
  'mail.templates.variable.lastReplyTime': 'Last reply at',
  'mail.templates.variable.waitingTime': 'Waiting time',
  'mail.templates.variable.timeoutMinutes': 'Timeout minutes',
  'mail.templates.variable.expireTime': 'Expire time',
  'mail.templates.variable.companyName': 'Company name',
  'mail.templates.variable.loginUrl': 'Login link',
  'mail.templates.variable.resetPasswordUrl': 'Password reset link',
  'mail.templates.variable.activationUrl': 'Activation link',
  'mail.templates.variable.totalCount': 'Total records',
  'mail.templates.variable.successCount': 'Succeeded',
  'mail.templates.variable.failedCount': 'Failed',
  'mail.templates.variable.errorReportUrl': 'Error report link',

  // ════════ variableCategory ════════
  'mail.templates.variableCategory.conversation': 'Conversation',
  'mail.templates.variableCategory.customer': 'Customer',
  'mail.templates.variableCategory.user': 'User',
  'mail.templates.variableCategory.mailbox': 'Mailbox',
  'mail.templates.variableCategory.message': 'Message',
  'mail.templates.variableCategory.people': 'People',
  'mail.templates.variableCategory.sla': 'SLA',
  'mail.templates.variableCategory.satisfaction': 'Satisfaction',
  'mail.templates.variableCategory.escalation': 'Escalation',
  'mail.templates.variableCategory.chat': 'Chat',
  'mail.templates.variableCategory.calendar': 'Calendar',
  'mail.templates.variableCategory.system': 'System',
  'mail.templates.variableCategory.import': 'Import',
  'mail.templates.variableCategory.other': 'Other',

  // ════════════════════════════════════════════════════════════════
  // signatures —— pages/EmailSignatures.tsx + components/EmailSignatureTrashModal.tsx
  // ════════════════════════════════════════════════════════════════

// ════════ Page header / tabs (EmailSignatures.tsx) ════════
  'mail.signatures.page.title': 'Email signatures',
  'mail.signatures.page.badge': 'Mail center',
  'mail.signatures.page.subtitle': 'Manage user email signatures and configure default signatures per mailbox and user, with HTML rich text support',
  'mail.signatures.page.new': 'New signature',
  'mail.signatures.page.tabsList': 'Signature list',
  'mail.signatures.page.loadingOverview': 'Loading overview…',
  'mail.signatures.page.loadingList': 'Loading signatures…',

  // ════════ Overview stat cards ════════
  'mail.signatures.stats.total': 'Total signatures',
  'mail.signatures.stats.totalHint': 'All signature records',
  'mail.signatures.stats.default': 'Default signatures',
  'mail.signatures.stats.defaultHint': 'Signatures set as default',
  'mail.signatures.stats.mailbox': 'Mailbox-specific',
  'mail.signatures.stats.mailboxHint': 'Bound to a specific mailbox',
  'mail.signatures.stats.global': 'Global signatures',
  'mail.signatures.stats.globalHint': 'Shared across all mailboxes',

  // ════════ Overview tab ════════
  'mail.signatures.overview.sectionTitle': 'Top signatures',
  'mail.signatures.overview.viewAll': 'View all',
  'mail.signatures.overview.emptyTitle': 'No signatures yet',
  'mail.signatures.overview.emptySubtitle': 'Click the button below to create the first email signature',

  // ════════ Badges ════════
  'mail.signatures.badge.default': 'Default',

  // ════════ List filters and table head ════════
  'mail.signatures.list.filterTitle': 'Filters',
  'mail.signatures.list.searchPlaceholder': 'Search signature names…',
  'mail.signatures.filter.allMailboxes': 'All mailboxes',
  'mail.signatures.filter.allUsers': 'All users',
  'mail.signatures.filter.onlyDefault': 'Default only',
  'mail.signatures.filter.nonDefault': 'Non-default',
  'mail.signatures.filter.keywordPrefix': 'Keyword: ',
  'mail.signatures.filter.mailboxPrefix': 'Mailbox: ',
  'mail.signatures.filter.userPrefix': 'User: ',
  'mail.signatures.list.appliedColon': 'Applied: ',
  'mail.signatures.field.name': 'Signature name',
  'mail.signatures.list.scopeColumn': 'Scope',
  'mail.signatures.field.sortOrder': 'Sort order',
  'mail.signatures.field.createdAt': 'Created',
  'mail.signatures.list.statusDefault': 'Default signature',
  'mail.signatures.list.statusNormal': 'Standard',
  'mail.signatures.list.setDefault': 'Set as default',
  'mail.signatures.list.totalSuffix': ' items',

  // ════════ Editor modal ════════
  'mail.signatures.editor.titleEdit': 'Edit signature',
  'mail.signatures.editor.titleCreate': 'New signature',
  'mail.signatures.editor.headerEdit': 'Edit email signature',
  'mail.signatures.editor.headerCreate': 'Create email signature',
  'mail.signatures.editor.badgeEdit': 'Edit',
  'mail.signatures.editor.badgeCreate': 'New',
  'mail.signatures.editor.headerHintPrefix': 'Supports ',
  'mail.signatures.editor.headerHintBold': 'HTML rich text',
  'mail.signatures.editor.headerHintSuffix': '. You can set a default signature and the applicable mailbox scope.',
  'mail.signatures.editor.namePlaceholder': 'Enter a signature name',
  'mail.signatures.editor.defaultStatusLabel': 'Default status',
  'mail.signatures.editor.enableDefault': 'Set as default signature',
  'mail.signatures.editor.keepNormal': 'Standard signature',
  'mail.signatures.editor.bodyLabel': 'Signature content',
  'mail.signatures.editor.bodyFormatHint': 'HTML supported',
  'mail.signatures.editor.bodyPlaceholder': 'Enter signature content. HTML is supported, e.g. <br>, <b>, <a>',
  'mail.signatures.editor.previewTitle': 'Live preview',
  'mail.signatures.editor.previewBodySample': 'Sample email body…',
  'mail.signatures.editor.previewEmpty': 'Signature preview will appear here',
  'mail.signatures.editor.saving': 'Saving…',

  // ════════ Preview modal ════════
  'mail.signatures.preview.title': 'Signature preview',
  'mail.signatures.preview.windowTitle': 'Email preview window',
  'mail.signatures.preview.greeting': 'Dear customer,',
  'mail.signatures.preview.paragraph1': 'Thank you for your inquiry. Here is our reply…',
  'mail.signatures.preview.paragraph2': 'Contact us any time if you have any questions.',

  // ════════ Validation and confirmation ════════
  'mail.signatures.form.nameRequired': 'Enter a signature name',
  'mail.signatures.form.bodyRequired': 'Enter signature content',
  'mail.signatures.delete.title': 'Confirm deletion',
  'mail.signatures.delete.message': 'Signature "{name}" will be moved to the trash. You can restore it or delete it permanently from the trash.',

  // ════════ Toasts (failures reuse mail.common.*) ════════
  'mail.signatures.toast.restored': 'Signature "{name}" restored',
  'mail.signatures.toast.hardDeleted': 'Signature "{name}" deleted permanently',
  'mail.signatures.toast.updated': 'Signature updated',
  'mail.signatures.toast.created': 'Signature created',
  'mail.signatures.toast.movedToTrash': 'Signature moved to trash',
  'mail.signatures.toast.setDefault': 'Set as default signature',
  'mail.signatures.toast.setDefaultFailed': 'Failed to set the default signature',

  // ════════ Trash modal (EmailSignatureTrashModal.tsx) ════════
  'mail.signatures.trash.title': 'Trash (email signatures)',
  'mail.signatures.trash.headerTitle': 'Deleted email signatures',
  'mail.signatures.trash.retentionPrefix': 'Deleted signatures are kept in the trash. You can ',
  'mail.signatures.trash.pendingSuffix': ' deleted signatures pending',
  'mail.signatures.trash.searchPlaceholder': 'Search deleted signatures (name / user / mailbox)',
  'mail.signatures.trash.clearAll': 'Empty all',
  'mail.signatures.trash.columns.signature': 'Signature',
  'mail.signatures.trash.noMatch': 'No matching deleted signatures',
  'mail.signatures.trash.emptyHint': 'Deleted email signatures will appear here',
  'mail.signatures.trash.wasDefault': 'Was default',
  'mail.signatures.trash.restoreTooltip': 'Restore signature',
  'mail.signatures.trash.userNumber': 'User #{id}',
  'mail.signatures.trash.restoreTitle': 'Restore email signature',
  'mail.signatures.trash.restoreMessage': 'Restore signature "{name}" to the signature list? It will be available in the email signature list again.',
  'mail.signatures.trash.hardDeleteMessage': 'Signature "{name}" will be deleted permanently and cannot be recovered. Continue?',
  'mail.signatures.trash.clearMessage': {
    one: 'The {count} signature will be deleted permanently and cannot be recovered. Empty the entire trash?',
    other: 'All {count} signatures will be deleted permanently and cannot be recovered. Empty the entire trash?',
  },

  // ════════════════════════════════════════════════════════════════
  // folders —— pages/Folders.tsx
  // ════════════════════════════════════════════════════════════════

// ════════ Folder types ════════
  'mail.folders.type.unassigned': 'Unassigned',
  'mail.folders.type.mine': 'Mine',
  'mail.folders.type.starred': 'Starred',
  'mail.folders.type.drafts': 'Drafts',
  'mail.folders.type.assigned': 'Assigned',
  'mail.folders.type.closed': 'Closed',
  'mail.folders.type.deleted': 'Deleted',
  'mail.folders.type.spam': 'Spam',
  'mail.folders.type.custom': 'Custom folder',
  'mail.folders.type.customWithCode': 'Custom ({code})',
  'mail.folders.type.systemLocked': 'System folder (cannot be deleted)',

  // ════════ Page header ════════
  'mail.folders.page.title': 'Folder management',
  'mail.folders.page.badge': 'Mail center',
  'mail.folders.page.subtitle': 'Manage the conversation folders of a mailbox. System folders are created automatically; custom folders can be added.',
  'mail.folders.page.ensureSystemTooltip': 'Create every missing system folder for this mailbox',
  'mail.folders.page.ensureSystem': 'Add system folders',
  'mail.folders.page.refreshCountersTooltip': 'Recalculate the active and total counts of these folders',
  'mail.folders.page.refreshCounters': 'Refresh counts',
  'mail.folders.page.create': 'New folder',
  'mail.folders.page.statSystemLabel': 'System folders',
  'mail.folders.page.statSystemHint': 'Created automatically',
  'mail.folders.page.statCustomLabel': 'Custom folders',
  'mail.folders.page.statCustomHint': 'Added by users',
  'mail.folders.page.statActiveLabel': 'Active conversations',
  'mail.folders.page.statActiveHint': 'Not closed yet',
  'mail.folders.page.statTotalLabel': 'Total conversations',
  'mail.folders.page.statTotalHint': 'Across all folders',

  // ════════ List ════════
  'mail.folders.list.mailbox': 'Mailbox',
  'mail.folders.list.loading': 'Loading folders…',
  'mail.folders.list.emptyNoFolders': 'This mailbox has no folders',
  'mail.folders.list.emptyNoMailbox': 'Select a mailbox first',
  'mail.folders.list.emptyNoFoldersHint': 'Use the button below to add the system folders',
  'mail.folders.list.emptyNoMailboxHint': 'Select a mailbox above to view its folders',
  'mail.folders.list.emptyNoMatch': 'No folders match',
  'mail.folders.list.emptyNoMatchHint': 'Try a different keyword or filter',
  'mail.folders.list.clearFilters': 'Clear filters',
  'mail.folders.list.selectAllTooltip': 'Select or clear all custom folders',
  'mail.folders.list.columnName': 'Folder name',
  'mail.folders.list.columnActive': 'Active',
  'mail.folders.list.columnTotal': 'Total',
  'mail.folders.list.columnUpdated': 'Updated',
  'mail.folders.list.systemDisabledTooltip': 'System folders cannot be selected',
  'mail.folders.list.selectTooltip': 'Select this folder',
  'mail.folders.list.activeBadge': 'Active {count}',
  'mail.folders.list.totalBadge': 'Total {count}',

  // ════════ Search and filters ════════
  'mail.folders.filter.label': 'Filter',
  'mail.folders.filter.searchPlaceholder': 'Search folder name or type…',
  'mail.folders.filter.all': 'All',
  'mail.folders.filter.typeSystem': 'Type: System',
  'mail.folders.filter.typeCustom': 'Type: Custom',
  'mail.folders.filter.batchDelete': 'Delete selected ({count})',
  'mail.folders.filter.totalPrefix': 'Showing',

  // ════════ Create / edit dialog ════════
  'mail.folders.form.titleEdit': 'Edit folder',
  'mail.folders.form.editHeading': 'Edit folder details',
  'mail.folders.form.createHeading': 'Create a custom folder',
  'mail.folders.form.editHint': 'You can rename the folder; its type cannot be changed',
  'mail.folders.form.createHint': 'Custom folders help you group conversations',
  'mail.folders.form.saving': 'Saving…',
  'mail.folders.form.errorRequired': 'Mailbox and folder name are required',
  'mail.folders.form.mailboxPlaceholder': 'Select a mailbox',
  'mail.folders.form.nameLabel': 'Folder name',
  'mail.folders.form.namePlaceholder': 'e.g. Follow-up, Urgent, VIP',
  'mail.folders.form.nameHint': 'Up to 100 characters',
  'mail.folders.form.typeLabel': 'Folder type',
  'mail.folders.form.typeLockedHint': 'The folder type cannot be changed',
  'mail.folders.form.typeCreateHint': 'System folders are created by the backend; only custom folders can be created here',

  // ════════ Delete confirmation ════════
  'mail.folders.delete.title': 'Delete folder',
  'mail.folders.delete.message': {
    one: 'Delete the folder "{name}"? Its {count} conversation will no longer be linked to this folder. This action cannot be undone.',
    other: 'Delete the folder "{name}"? Its {count} conversations will no longer be linked to this folder. This action cannot be undone.',
  },
  'mail.folders.delete.batchTitle': 'Delete folders',
  'mail.folders.delete.batchMessage': {
    one: 'Delete the selected folder? This action cannot be undone.',
    other: 'Delete the {count} selected folders? This action cannot be undone.',
  },
  'mail.folders.delete.batchConfirm': {
    one: 'Delete 1 folder',
    other: 'Delete {count} folders',
  },

  // ════════ Result toasts ════════
  'mail.folders.toast.updated': 'Folder "{name}" updated',
  'mail.folders.toast.created': 'Folder "{name}" created',
  'mail.folders.toast.deleted': 'Folder "{name}" deleted',
  'mail.folders.toast.countersRefreshed': {
    one: 'Recalculated the counts of {count} folder',
    other: 'Recalculated the counts of {count} folders',
  },
  'mail.folders.toast.refreshFailed': 'Failed to refresh',
  'mail.folders.toast.systemEnsured': {
    one: 'Added {count} system folder to the mailbox',
    other: 'Added {count} system folders to the mailbox',
  },
  'mail.folders.toast.systemAlreadyComplete': 'All system folders are already present',
  'mail.folders.toast.ensureFailed': 'Failed to add the system folders',
  'mail.folders.toast.batchDeleted': {
    one: 'Deleted {count} folder',
    other: 'Deleted {count} folders',
  },
  'mail.folders.toast.batchDeleteFailedSuffix': ', {count} failed',
  'mail.folders.toast.batchDeleteFailed': {
    one: 'Failed to delete {count} folder',
    other: 'Failed to delete {count} folders',
  },

  // ════════════════════════════════════════════════════════════════
  // canned —— pages/CannedResponses.tsx + components/CannedResponseTrashModal.tsx
  // ════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════
  // page
  // ════════════════════════════════════════════════════════════════
  'mail.canned.page.title': 'Canned responses',
  'mail.canned.page.badge': 'Mail center',
  'mail.canned.page.subtitle':
    'Manage canned responses for agents, group them by category, and support rich HTML and sharing to speed up replies and keep them consistent',
  'mail.canned.page.newCannedResponse': 'New canned response',
  'mail.canned.page.allMailboxesGlobal': 'All mailboxes (global)',

  // ════════════════════════════════════════════════════════════════
  // stats
  // ════════════════════════════════════════════════════════════════
  'mail.canned.stats.total': 'Total canned responses',
  'mail.canned.stats.totalHint': 'All available canned responses',
  'mail.canned.stats.shared': 'Shared',
  'mail.canned.stats.sharedHint': 'Visible to all agents',
  'mail.canned.stats.personal': 'Personal',
  'mail.canned.stats.personalHint': 'Visible only to you',
  'mail.canned.stats.categories': 'Categories',
  'mail.canned.stats.categoriesHint': 'Custom groups',

  // ════════════════════════════════════════════════════════════════
  // category
  // ════════════════════════════════════════════════════════════════
  'mail.canned.category.navTitle': 'Categories',
  'mail.canned.category.searchPlaceholder': 'Search canned responses / categories...',
  'mail.canned.category.all': 'All canned responses',
  'mail.canned.category.uncategorized': 'Uncategorized',

  // ════════════════════════════════════════════════════════════════
  // list
  // ════════════════════════════════════════════════════════════════
  'mail.canned.list.noMatch': 'No matching canned responses',
  'mail.canned.list.noMatchHint': 'Try another keyword or clear the filters',
  'mail.canned.list.empty': 'No canned responses yet',
  'mail.canned.list.emptyHint': 'Click "New canned response" in the top right to create your first one',
  'mail.canned.list.createFirst': 'Create your first canned response',
  'mail.canned.list.resetAll': 'Reset all',
  'mail.canned.list.groupHint': 'Click a card to preview the full content',
  'mail.canned.list.groupCountSuffix': {
    one: ' canned response',
    other: ' canned responses',
  },
  'mail.canned.list.moveToTrash': 'Move to trash',

  // ════════════════════════════════════════════════════════════════
  // badge
  // ════════════════════════════════════════════════════════════════
  'mail.canned.badge.personal': 'Personal',
  'mail.canned.badge.shared': 'Shared',

  // ════════════════════════════════════════════════════════════════
  // editor
  // ════════════════════════════════════════════════════════════════
  'mail.canned.editor.titleEdit': 'Edit canned response',
  'mail.canned.editor.titleCreate': 'New canned response',
  'mail.canned.editor.headingEdit': 'Editing a canned response',
  'mail.canned.editor.headingCreate': 'Create a canned response',
  'mail.canned.editor.htmlRichText': 'HTML · Rich text',
  'mail.canned.editor.headingHint':
    'Fill in the details and the content. The content area supports HTML and renders live on the right',
  'mail.canned.editor.sectionInfo': 'Canned response details',
  'mail.canned.editor.requiredHint': 'Fields marked * are required',
  'mail.canned.editor.nameLabel': 'Canned response name',
  'mail.canned.editor.namePlaceholder': 'e.g. Welcome message, Password reset instructions, Refund policy',
  'mail.canned.editor.errorNameRequired': 'Enter a canned response name',
  'mail.canned.editor.categoryLabel': 'Group (category)',
  'mail.canned.editor.categoryPlaceholder':
    'e.g. Welcome, Technical support, Refund (create one or pick an existing category)',
  'mail.canned.editor.sortOrderLabel': 'Sort weight (lower numbers come first)',
  'mail.canned.editor.globalAllMailboxesAvailable': '🌐 Global (available in all mailboxes)',
  'mail.canned.editor.sharedToggleOn': 'Shared with all agents',
  'mail.canned.editor.sharedToggleOff': 'Visible only to you (personal)',
  'mail.canned.editor.sharedToggleHint':
    'Every agent can see and use this canned response in the reply drop-down',
  'mail.canned.editor.personalToggleHint':
    'Available only to this account. Teammates will not see it in the canned response library',
  'mail.canned.editor.bodyLabel': 'Canned response content',
  'mail.canned.editor.bodySupportsHtml': 'Supports rich HTML',
  'mail.canned.editor.errorBodyRequired': 'Enter the canned response content',
  'mail.canned.editor.codeEditor': 'HTML code editor',
  'mail.canned.editor.bodyPlaceholder':
    'e.g.:\n<p>Hello <b>{customer.name}</b>,</p>\n<p>Thanks for reaching out...</p>',
  'mail.canned.editor.liveRender': 'Live HTML render',
  'mail.canned.editor.noDelay': 'No delay',
  'mail.canned.editor.bodyPreviewLabel': '📧 Email body preview',
  'mail.canned.editor.previewPlaceholder': 'The preview will appear here',
  'mail.canned.editor.previewPlaceholderHint': 'All standard HTML tags and styles are supported',
  'mail.canned.editor.saving': 'Saving…',
  'mail.canned.editor.saveChanges': 'Save changes',
  'mail.canned.editor.createSubmit': 'Create canned response',

  // ════════════════════════════════════════════════════════════════
  // preview
  // ════════════════════════════════════════════════════════════════
  'mail.canned.preview.title': 'Canned response preview',
  'mail.canned.preview.sharedBadge': 'Shared',
  'mail.canned.preview.personalBadge': 'Personal',
  'mail.canned.preview.browserBar': '📧 mail-to:customer@example.com · Render preview',
  'mail.canned.preview.renderDone': 'Rendered',
  'mail.canned.preview.emptyTitle': 'No content',
  'mail.canned.preview.emptyHint': 'This canned response has no body yet',
  'mail.canned.preview.createdBy': 'Created by',
  'mail.canned.preview.editNow': 'Edit now',

  // ════════════════════════════════════════════════════════════════
  // deleteDialog
  // ════════════════════════════════════════════════════════════════
  'mail.canned.deleteDialog.title': 'Delete canned response',
  'mail.canned.deleteDialog.message':
    'The canned response will be moved to the trash, where you can restore it or delete it permanently.',

  // ════════════════════════════════════════════════════════════════
  // toast
  // ════════════════════════════════════════════════════════════════
  'mail.canned.toast.restored': 'Canned response "{name}" restored',
  'mail.canned.toast.hardDeleted': 'Canned response "{name}" permanently deleted',
  'mail.canned.toast.updated': 'Canned response updated',
  'mail.canned.toast.created': 'Canned response created',
  'mail.canned.toast.movedToTrash': 'Canned response moved to trash',

  // ════════════════════════════════════════════════════════════════
  // trash
  // ════════════════════════════════════════════════════════════════
  'mail.canned.trash.title': 'Trash (canned responses)',
  'mail.canned.trash.heading': 'Deleted canned responses',
  'mail.canned.trash.retentionPrefix': 'Deleted canned responses stay in the trash. You can ',
  'mail.canned.trash.pendingSuffix': ' deleted canned responses awaiting action',
  'mail.canned.trash.searchPlaceholder': 'Search deleted canned responses (name / content / category)',
  'mail.canned.trash.emptyAll': 'Empty trash',
  'mail.canned.trash.colCannedResponse': 'Canned response',
  'mail.canned.trash.emptyHint': 'Deleted canned responses will appear here',
  'mail.canned.trash.categoryPrefix': 'Category · ',
  'mail.canned.trash.deletedBy': 'Deleted by',
  'mail.canned.trash.createdBy': 'Created by: ',
  'mail.canned.trash.restoreTitle': 'Restore canned response',
  'mail.canned.trash.restoreConfirm':
    'Restore the canned response "{name}" to the list? It will be available in the canned response list again.',
  'mail.canned.trash.hardDeleteConfirm':
    'The canned response "{name}" will be permanently deleted and cannot be restored. Continue?',
  'mail.canned.trash.clearConfirm': {
    one: '{count} canned response will be permanently deleted and cannot be restored. Empty the entire trash?',
    other:
      '{count} canned responses will be permanently deleted and cannot be restored. Empty the entire trash?',
  },
} satisfies Entries;
