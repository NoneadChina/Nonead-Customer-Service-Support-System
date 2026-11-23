import type { Entries } from '../../i18n/types';

/**
 * Integrations & operations (wave 14): Webhook management / API keys / Modules / System tools.
 *
 * Same key set as `zh-Hans/integration.ts`; a missing key is a compile error (TS2741).
 *
 * ⚠ Several values **must keep their leading/trailing spaces** because the original JSX had bare
 * text nodes hugging `{expr}` / `<span>` / `<strong>`:
 *   - `integration.apiKeys.loadFailedPrefix` and `integration.apiKeys.created.warningStrong` end
 *     with a space (Chinese uses a full-width colon that supplies its own spacing);
 *   - `integration.modules.paginationPrefix` / `.paginationMiddle` / `.paginationSuffix` carry the
 *     spaces that used to sit next to the two numeric `<span>`s.
 *   Do **not** trim these, and do not move the space into the JSX — that would change the Chinese
 *   rendering (`共26条`).
 */
export const integration = {
  // ════════ Shared by all four pages ════════
  'integration.common.tabOverview': 'Overview',
  'integration.common.newShort': 'New',
  // stat-card labels ("how many are in use")
  'integration.common.statsActive': 'Active',
  'integration.common.statsInactive': 'Inactive',
  // status badges and filter options ("is the switch on")
  'integration.common.enabled': 'Enabled',
  'integration.common.disabled': 'Disabled',
  'integration.common.allStatuses': 'All statuses',
  'integration.common.status': 'Status',
  'integration.common.actions': 'Actions',
  'integration.common.name': 'Name',
  'integration.common.trash': 'Trash',
  'integration.common.trashEmpty': 'Trash is empty',
  'integration.common.clearTrash': 'Empty trash',
  'integration.common.trashCleared': 'Trash emptied',
  'integration.common.clearFailed': 'Failed to empty the trash',
  'integration.common.restore': 'Restore',
  'integration.common.restoreFailed': 'Failed to restore',
  'integration.common.hardDelete': 'Delete permanently',
  'integration.common.hardDeleteFailed': 'Failed to delete permanently',
  'integration.common.deleted': 'Deleted',
  'integration.common.clickToDisable': 'Click to disable',
  'integration.common.clickToEnable': 'Click to enable',
  'integration.common.saving': 'Saving…',
  'integration.common.saveFailed': 'Failed to save',
  'integration.common.deleteFailed': 'Failed to delete',
  'integration.common.operationFailed': 'Operation failed',
  'integration.common.toggleSucceeded': 'Status updated',
  'integration.common.paginationTotal': {
    one: 'Total {count} item, page {page} / {total}',
    other: 'Total {count} items, page {page} / {total}',
  },

  // ════════ Webhooks page ════════
  'integration.webhooks.pageTitle': 'Webhook management',
  'integration.webhooks.subtitle':
    'Configure Webhook callbacks to push event notifications to third-party systems in real time',
  // Noun phrase for the shared `TrashDialogs` shell (`itemLabel`)
  'integration.webhooks.title': 'Webhook',
  'integration.webhooks.newWebhook': 'New Webhook',
  'integration.webhooks.tabList': 'Webhook list',
  'integration.webhooks.listShort': 'List',
  'integration.webhooks.tabLogs': 'Trigger log',
  'integration.webhooks.clearShort': 'Empty',
  // This page words "disabled" as 「已禁用」, unlike ApiKeys/Modules — hence a page-local key
  'integration.webhooks.disabled': 'Disabled',
  'integration.webhooks.searchPlaceholder': 'Search name or URL...',
  'integration.webhooks.globalMailbox': 'Global',

  'integration.webhooks.columns.event': 'Event',
  'integration.webhooks.columns.method': 'Method',
  'integration.webhooks.columns.time': 'Time',
  'integration.webhooks.columns.duration': 'Duration',
  'integration.webhooks.columns.statusCode': 'Status code',
  'integration.webhooks.columns.triggerSuccessFail': 'Triggered / succeeded / failed',

  'integration.webhooks.success': 'Success',
  'integration.webhooks.failed': 'Failed',
  'integration.webhooks.triggerLabel': 'Triggered',

  'integration.webhooks.empty': 'No Webhooks yet',
  'integration.webhooks.noTriggers': 'No triggers yet',
  'integration.webhooks.noLogs': 'No log entries yet',

  'integration.webhooks.overview.totalWebhooks': 'Total Webhooks',
  'integration.webhooks.overview.totalTriggers': 'Total triggers',
  'integration.webhooks.overview.successRate': 'Success rate',
  'integration.webhooks.overview.distribution': 'Distribution',
  'integration.webhooks.overview.globalWebhooks': 'Global Webhooks',
  'integration.webhooks.overview.mailboxWebhooks': 'Mailbox-specific',
  'integration.webhooks.overview.successCount': 'Succeeded',
  'integration.webhooks.overview.failCount': 'Failed',
  'integration.webhooks.overview.eventTypeDistribution': 'Event type distribution',
  'integration.webhooks.overview.recentTriggers': 'Recent triggers',

  'integration.webhooks.testResult.title': 'Test result',
  'integration.webhooks.testResult.sentSuccess': 'Sent successfully',
  'integration.webhooks.testResult.sentFailed': 'Failed to send',
  'integration.webhooks.testResult.responseBody': 'Response body',
  'integration.webhooks.action.test': 'Test',

  'integration.webhooks.editor.editTitle': 'Edit Webhook',
  'integration.webhooks.editor.namePlaceholder': 'e.g. New conversation',
  'integration.webhooks.editor.callbackUrl': 'Callback URL',
  'integration.webhooks.editor.method': 'HTTP method',
  'integration.webhooks.editor.events': 'Trigger events',
  'integration.webhooks.editor.headers': 'Custom request headers (JSON)',
  'integration.webhooks.editor.secret': 'Signing secret (optional)',
  'integration.webhooks.editor.secretPlaceholder': 'Used to generate the X-Webhook-Signature header',
  'integration.webhooks.editor.secretHint': 'Leave empty to disable signature verification',
  'integration.webhooks.editor.mailbox': 'Mailbox',
  'integration.webhooks.editor.mailboxAllOption': 'All mailboxes (global)',
  // The two mailbox names in the same select are DATA (see the note in zh-Hans/integration.ts).
  'integration.webhooks.editor.enableWebhook': 'Enable this Webhook',
  'integration.webhooks.editor.errNameRequired': 'Enter a name',
  'integration.webhooks.editor.errUrlRequired': 'Enter a URL',
  'integration.webhooks.editor.errEventsRequired': 'Select at least one event',
  'integration.webhooks.editor.errHeaderValueType':
    'The value of request header "{key}" must be a string (currently {type}). Wrap it in quotes, for example "value"',
  'integration.webhooks.editor.errHeadersJson': 'The request headers are not valid JSON',
  'integration.webhooks.editor.create': 'Create',

  'integration.webhooks.toast.movedToTrash': 'Webhook moved to trash',
  'integration.webhooks.toast.restored': 'Webhook restored',
  'integration.webhooks.toast.hardDeleted': 'Deleted permanently',
  'integration.webhooks.toast.updated': 'Webhook updated',
  'integration.webhooks.toast.created': 'Webhook created',

  // ── Event type catalogue ──
  // Not shared with `notifications.eventType.*`: that is the *notification* event catalogue
  // (22 semantic entries) and words the same concepts differently, so sharing would change Chinese copy.
  'integration.webhooks.eventCategory.conversation': 'Conversation',
  'integration.webhooks.eventCategory.customer': 'Customer',
  'integration.webhooks.eventCategory.user': 'User',
  'integration.webhooks.event.conversationCreated': 'Conversation created',
  'integration.webhooks.event.conversationUpdated': 'Conversation updated',
  'integration.webhooks.event.conversationDeleted': 'Conversation deleted',
  'integration.webhooks.event.conversationStatusChanged': 'Conversation status changed',
  'integration.webhooks.event.customerCreated': 'Customer created',
  'integration.webhooks.event.customerUpdated': 'Customer updated',
  'integration.webhooks.event.threadCreated': 'Reply created',
  'integration.webhooks.event.userCreated': 'User created',
  'integration.webhooks.event.userUpdated': 'User updated',

  // ════════ API keys page ════════
  'integration.apiKeys.pageTitle': 'API key management',
  'integration.apiKeys.subtitle': 'Manage API access keys for third-party system integrations',
  // Noun phrase for the shared `TrashDialogs` shell (`itemLabel`)
  'integration.apiKeys.title': 'API key',
  'integration.apiKeys.newKey': 'New key',
  'integration.apiKeys.tabList': 'Key list',
  'integration.apiKeys.tabActive': 'Active keys',
  // ⚠ trailing space is intentional (the error text follows immediately)
  'integration.apiKeys.loadFailedPrefix': 'Load failed: ',
  'integration.apiKeys.error.overviewLoadFailed': 'Failed to load the API key overview',
  'integration.apiKeys.error.listLoadFailed': 'Failed to load the API key list',
  'integration.apiKeys.error.trashLoadFailed': 'Failed to load the trashed keys',
  'integration.apiKeys.statTotalKeys': 'Total keys',
  'integration.apiKeys.statTotalUsage': 'Total calls',
  'integration.apiKeys.recentKeys': 'Recently created keys',
  'integration.apiKeys.noKeys': 'No keys yet',
  'integration.apiKeys.scopesDistribution': 'Scope distribution',
  'integration.apiKeys.columns.keyPrefix': 'Key prefix',
  'integration.apiKeys.columns.createdBy': 'Created by',
  'integration.apiKeys.columns.usageCount': 'Calls',
  'integration.apiKeys.columns.createdAt': 'Created at',
  'integration.apiKeys.noApiKeys': 'No API keys yet',
  'integration.apiKeys.searchPlaceholder': 'Search key name...',
  'integration.apiKeys.callLabel': 'Calls',

  'integration.apiKeys.created.title': 'API key created',
  'integration.apiKeys.created.saved': 'I have saved it',
  // ⚠ trailing space is intentional (the sentence follows the <strong> immediately)
  'integration.apiKeys.created.warningStrong': 'Important: ',
  'integration.apiKeys.created.warningBody':
    'Copy and save this key now. Once this window is closed the full key cannot be viewed again.',
  'integration.apiKeys.created.keyLabel': 'Key',
  'integration.apiKeys.created.copy': 'Copy',

  'integration.apiKeys.editor.editTitle': 'Edit API key',
  'integration.apiKeys.editor.createTitle': 'New API key',
  'integration.apiKeys.editor.name': 'Key name',
  'integration.apiKeys.editor.namePlaceholder': 'Enter a key name',
  'integration.apiKeys.editor.description': 'Description',
  'integration.apiKeys.editor.descriptionPlaceholder': 'Enter a key description (optional)',
  'integration.apiKeys.editor.scopes': 'Scopes',
  'integration.apiKeys.editor.activateNow': 'Enable immediately',
  'integration.apiKeys.editor.update': 'Update',
  'integration.apiKeys.editor.create': 'Create',
  'integration.apiKeys.editor.errNameRequired': 'Enter a key name',
  'integration.apiKeys.editor.errScopesRequired': 'Select at least one scope',

  'integration.apiKeys.regenerate': 'Regenerate',
  'integration.apiKeys.regenerateTitle': 'Regenerate key',
  'integration.apiKeys.regenerateMessage':
    'Regenerate this API key? The old key stops working immediately and every integration that uses it must be updated.',

  'integration.apiKeys.toast.updated': 'API key updated',
  'integration.apiKeys.toast.created': 'API key created. Save the key now.',
  'integration.apiKeys.toast.regenerated': 'The key was regenerated. Save it now.',
  'integration.apiKeys.toast.regenerateFailed': 'Failed to regenerate',
  'integration.apiKeys.toast.movedToTrash': 'API key moved to trash',
  'integration.apiKeys.toast.restored': 'API key restored',
  'integration.apiKeys.toast.hardDeleted': 'API key deleted permanently',
  'integration.apiKeys.toast.copied': 'Copied to clipboard',
  'integration.apiKeys.toast.copyFailed': 'Copy failed. Select the text and copy it manually.',

  // ── Scope catalogue (the backend has no enum or allow-list for these) ──
  // Not shared with `teamPermissions.*`: fine-grained permissions use dots (`webhooks.manage`)
  // while API scopes use colons (`webhooks:manage`) — different vocabularies for the same concept.
  'integration.apiKeys.scopeCategory.conversations': 'Conversations',
  'integration.apiKeys.scopeCategory.customers': 'Customers',
  'integration.apiKeys.scopeCategory.users': 'Users',
  'integration.apiKeys.scopeCategory.mailboxes': 'Mailboxes',
  'integration.apiKeys.scopeCategory.tags': 'Tags',
  'integration.apiKeys.scopeCategory.reports': 'Reports',
  'integration.apiKeys.scopeCategory.webhooks': 'Webhooks',
  'integration.apiKeys.scopeCategory.admin': 'Admin',
  'integration.apiKeys.scope.conversationsRead': 'Conversations - read',
  'integration.apiKeys.scope.conversationsWrite': 'Conversations - write',
  'integration.apiKeys.scope.customersRead': 'Customers - read',
  'integration.apiKeys.scope.customersWrite': 'Customers - write',
  'integration.apiKeys.scope.usersRead': 'Users - read',
  'integration.apiKeys.scope.usersWrite': 'Users - write',
  'integration.apiKeys.scope.mailboxesRead': 'Mailboxes - read',
  'integration.apiKeys.scope.tagsRead': 'Tags - read',
  'integration.apiKeys.scope.tagsWrite': 'Tags - write',
  'integration.apiKeys.scope.reportsRead': 'Reports - read',
  'integration.apiKeys.scope.webhooksManage': 'Webhooks - manage',
  'integration.apiKeys.scope.adminFull': 'Admin - full access',

  // ════════ Modules page ════════
  'integration.modules.pageTitle': 'Module management',
  'integration.modules.subtitle':
    'Manage every feature module: enable or disable extensions and tailor the support suite to your needs',
  'integration.modules.tabList': 'Module list',
  'integration.modules.newModule': 'New module',
  'integration.modules.statTotal': 'Total modules',
  'integration.modules.byCategory': 'By category',
  'integration.modules.countTotal': 'Total',
  'integration.modules.empty': 'No modules yet',
  'integration.modules.searchPlaceholder': 'Search module name or description...',
  'integration.modules.allCategories': 'All categories',
  'integration.modules.systemModule': 'Core module',
  'integration.modules.updatedAt': 'Updated',
  'integration.modules.previousPage': 'Previous',
  'integration.modules.nextPage': 'Next',
  // Pager row: `{prefix}<span>{n}</span>{middle}<span>{page}</span>{suffix}` — spaces intentional
  'integration.modules.paginationPrefix': 'Total ',
  'integration.modules.paginationMiddle': ' items, page ',
  'integration.modules.paginationSuffix': '/{total}',

  'integration.modules.editor.editTitle': 'Edit module',
  'integration.modules.editor.name': 'Module name',
  'integration.modules.editor.namePlaceholder': 'e.g. telegram',
  'integration.modules.editor.title': 'Module title',
  'integration.modules.editor.titlePlaceholder': 'e.g. Telegram integration',
  'integration.modules.editor.description': 'Module description',
  'integration.modules.editor.descriptionPlaceholder': 'Short description of what the module does...',
  'integration.modules.editor.version': 'Version',
  'integration.modules.editor.author': 'Author',
  'integration.modules.editor.authorPlaceholder': 'Author name',
  'integration.modules.editor.category': 'Category',
  'integration.modules.editor.icon': 'Icon',
  'integration.modules.editor.iconPlaceholder': 'Icon name',
  'integration.modules.editor.sortOrder': 'Sort order',
  'integration.modules.editor.errNameRequired': 'Enter the module name',
  'integration.modules.editor.errTitleRequired': 'Enter the module title',

  'integration.modules.confirmDeleteTitle': 'Confirm deletion',
  'integration.modules.confirmDeleteMessage': 'Delete this module? This cannot be undone.',

  'integration.modules.action.disable': 'Disable',
  'integration.modules.action.enable': 'Enable',
  'integration.modules.err.systemCannotDisable': 'A core module cannot be disabled',
  'integration.modules.err.systemCannotDelete': 'A core module cannot be deleted',

  'integration.modules.toast.updated': 'Module updated',
  'integration.modules.toast.created': 'Module created',
  'integration.modules.toast.deleted': 'Module deleted',
  'integration.modules.toast.disabled': 'Module disabled',
  'integration.modules.toast.enabled': 'Module enabled',

  // ── Category catalogue (`ModuleService.GetCategoryLabel`, 6 codes; unknown codes fall back to the raw code) ──
  'integration.modules.category.integration': 'Integration',
  'integration.modules.category.communication': 'Communication',
  'integration.modules.category.reports': 'Reports',
  'integration.modules.category.tools': 'Tools',
  'integration.modules.category.general': 'General',
  'integration.modules.category.automation': 'Automation',

  // ════════ System tools page ════════
  'integration.systemTools.pageTitle': 'System tools',
  'integration.systemTools.subtitle': 'Run system maintenance and cleanup operations',
  'integration.systemTools.confirmExecute': 'Run "{name}"?',
  'integration.systemTools.action.execute': 'Run',
  'integration.systemTools.action.executing': 'Running…',
  // The backend answers these endpoints with Chinese free text (`SystemController.cs`); these
  // frontend strings are only the fallback used when the payload carries no message.
  'integration.systemTools.toast.succeeded': 'Completed',
  'integration.systemTools.toast.failed': 'Failed. Try again later.',
  'integration.systemTools.toast.logsCleared': 'Log files emptied',
  'integration.systemTools.toast.clearLogsFailed': 'Failed to empty the log files. Try again later.',
  'integration.systemTools.toast.logsLoadFailed': 'Failed to load the logs',
  'integration.systemTools.clearLogsConfirm':
    'Empty the log files? This deletes every log record in the system log (the files are truncated to 0 bytes and cannot be recovered). Proceed with caution.',

  'integration.systemTools.tool.clearCache.name': 'Clear cache',
  'integration.systemTools.tool.clearCache.description':
    'Delete temporary files, expired tokens and old log files',
  'integration.systemTools.tool.optimizeDb.name': 'Optimize database',
  'integration.systemTools.tool.optimizeDb.description':
    'Run VACUUM and remove old deleted conversations',
  'integration.systemTools.tool.logoutUsers.name': 'Force all users to sign out',
  'integration.systemTools.tool.logoutUsers.description': 'Invalidate every user session',

  'integration.systemTools.stat.total': 'Total logs',
  'integration.systemTools.stat.normal': 'Normal',
  'integration.systemTools.stat.warning': 'Warnings',
  'integration.systemTools.stat.error': 'Errors',
  'integration.systemTools.health': 'Health',
  'integration.systemTools.logTitle': 'System log',

  'integration.systemTools.live.pauseTitle': 'Click to pause live monitoring',
  'integration.systemTools.live.startTitle': 'Click to start live monitoring (refreshes every 2 seconds)',
  'integration.systemTools.live.pause': 'Pause',
  'integration.systemTools.live.start': 'Live',
  'integration.systemTools.refresh.disabledTitle': 'Manual refresh is disabled while live monitoring is on',
  'integration.systemTools.refresh.title': 'Refresh logs manually',
  'integration.systemTools.refresh.refreshing': 'Refreshing…',
  'integration.systemTools.refresh.label': 'Refresh',
  'integration.systemTools.download.title': 'Download the filtered log file',
  'integration.systemTools.download.label': 'Download',
  'integration.systemTools.clearView.title': 'Clear the current log view (the log files are untouched)',
  'integration.systemTools.clearView.label': 'Clear',
  'integration.systemTools.clearFiles.title':
    'Delete every log record in the system log (empties the log files, cannot be undone)',
  'integration.systemTools.clearFiles.label': 'Empty log files',
  'integration.systemTools.polling.every2s': 'Every 2s',
  'integration.systemTools.polling.stopped': 'Stopped',
  'integration.systemTools.filter.all': 'All',
  'integration.systemTools.searchPlaceholder': 'Search logs...',

  'integration.systemTools.empty.noLogs': 'No logs yet',
  'integration.systemTools.empty.noMatch': 'No matching logs',
  'integration.systemTools.clearFilters': 'Clear filters',
  'integration.systemTools.newLogs.title': {
    one: 'View {count} new log',
    other: 'View {count} new logs',
  },
  'integration.systemTools.newLogs.suffix': 'new',
  'integration.systemTools.scrollToBottom.title': 'Scroll to the bottom and resume auto-follow',
  'integration.systemTools.scrollToBottom.label': 'Latest',
  'integration.systemTools.footerWarning':
    'These operations can affect how the system runs. Use them with care and back up important data first.',
} satisfies Entries;
