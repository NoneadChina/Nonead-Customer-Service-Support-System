import type { Entries } from '../../i18n/types';

/**
 * English values for the calendar cluster (batch 2 wave 13): event editor, calendar views,
 * Outlook sync, subscriptions, trash and the store layer.
 *
 * Sections mirror `zh-Hans/calendar.ts`:
 *   calendar.common.* — common — text that is byte-identical across several files
 *   calendar.editor.* — editor — event editor panel (`panels/EditorPanel.tsx`)
 *   calendar.views.* — views — list / month / week / day / overview (`views/*`)
 *   calendar.panels.* — panels — Outlook sync, subscriptions, shared panel pieces
 *   calendar.trash.* — trash — trash panel and trash view (one shared set of keys)
 *   calendar.store.* — store — `store.ts` / `calendarUtils.ts` / `hooks.ts` (resolved with `tBound`)
 *   calendar.shell.* — shell — page shell, event chips, batch toolbar, filter bar
 *   calendar.a11y.* — a11y — accessibility and reliability text (`a11yReliability.tsx`)
 *
 * ⚠ Leading/trailing spaces are meaningful in several values (the Chinese source was a bare
 *   JSX text node next to a `<span>`/`{expr}`). Do not trim them.
 *
 * Must use `satisfies Entries`, never a type annotation — see `guard.ts`.
 */
export const calendar = {
  // ══ common — text that is byte-identical across several files ══

  // ── Event title fallback ──
  'calendar.common.untitled': '(Untitled)',

  // ── Local cache / concurrency hints ──
  'calendar.common.staleDataRefreshed': 'Changed by another user; refreshed to the latest version',
  'calendar.common.pendingSync': 'Local changes pending sync',
  'calendar.common.pendingSyncHint': 'Local changes are pending server sync (your position is kept after refresh)',

  // ── Top toolbar / view names ──
  'calendar.common.today': 'Today',
  'calendar.common.newEvent': 'New event',
  'calendar.common.overview': 'Overview',
  'calendar.common.trash': 'Trash',
  'calendar.common.outlookSync': 'Outlook sync',

  // ── Event field names (shared by the editor and the list view header) ──
  'calendar.common.title': 'Title',
  'calendar.common.type': 'Type',
  'calendar.common.status': 'Status',
  'calendar.common.priority': 'Priority',
  'calendar.common.progress': 'Progress',
  'calendar.common.location': 'Location',
  'calendar.common.assignee': 'Owner',
  'calendar.common.reminder': 'Reminder',
  'calendar.common.allDay': 'All day',
  'calendar.common.allDayEvent': 'All-day event',
  'calendar.common.custom': 'Custom',
  'calendar.common.complete': 'Complete',
  'calendar.common.conflict': 'Conflict',
  'calendar.common.timeConflict': 'Time conflict',

  // ── Batch selection / pagination ──
  'calendar.common.cancelSelection': 'Clear selection',
  'calendar.common.deleteEvent': 'Delete event',
  'calendar.common.previousPage': 'Previous',
  'calendar.common.nextPage': 'Next',

  // ── Sync / export failures ──
  'calendar.common.syncFailed': 'Sync failed',
  'calendar.common.saveConfigFailed': 'Failed to save settings',
  'calendar.common.exportIcsFailed': 'Failed to export ICS',

  // ── Editor / sync panel buttons and hints ──
  'calendar.common.saving': 'Saving...',
  'calendar.common.closeHint': 'Dismiss',

  // ── Weekday abbreviations (month/week/day view headers, recurrence builder) ──
  'calendar.common.weekdayShort.sun': 'Sun',
  'calendar.common.weekdayShort.mon': 'Mon',
  'calendar.common.weekdayShort.tue': 'Tue',
  'calendar.common.weekdayShort.wed': 'Wed',
  'calendar.common.weekdayShort.thu': 'Thu',
  'calendar.common.weekdayShort.fri': 'Fri',
  'calendar.common.weekdayShort.sat': 'Sat',

  // ══ editor — event editor panel (`panels/EditorPanel.tsx`) ══

  // ── Editor header / undo-stack descriptions (persisted in localStorage) ──
  'calendar.editor.headerEdit': 'Edit event',
  'calendar.editor.headerCreate': 'New event',
  'calendar.editor.subtitleEdit': 'Edit the event details',
  'calendar.editor.subtitleCreate': 'Create a new event',
  'calendar.editor.undoEdit': 'Edit event',
  'calendar.editor.undoDelete': 'Delete event',

  // ── Save / delete / export failures and concurrency hints ──
  'calendar.editor.titleRequired': 'Title is required',
  'calendar.editor.titleRequiredHint': 'Enter a title',
  'calendar.editor.saveFailed': 'Failed to save',
  'calendar.editor.deleteFailed': 'Failed to delete',
  'calendar.editor.staleSyncRefreshed': 'This event was just changed elsewhere; synced to the latest version locally',
  'calendar.editor.staleSyncRetryHint': 'This event was just changed elsewhere; refresh the page and edit again',
  'calendar.editor.staleDataRetryHint': 'Changed by another user; refresh and try again',
  'calendar.editor.icsDownloading': 'Downloading the ICS file',
  'calendar.editor.exportIcsTitle': 'Export as .ics',

  // ── Server 409 conflict list (concatenated into a toast) ──
  'calendar.editor.saveConflictToast': '{message}: {titles}{extra}',
  'calendar.editor.conflictMore': { one: ' and 1 more', other: ' and {count} more' },
  'calendar.editor.titleListSeparator': ', ',

  // ── Live conflict hint inside the editor ──
  'calendar.editor.liveConflictHard': 'Serious conflict',
  'calendar.editor.liveConflictSoft': 'Marginal conflict',
  'calendar.editor.overlapLabel': 'Overlap',
  'calendar.editor.overlapSeconds': { one: '{count} sec', other: '{count} secs' },
  'calendar.editor.overlapMinutes': { one: '{count} min', other: '{count} mins' },
  'calendar.editor.liveConflictPrefix': 'Overlaps with',
  'calendar.editor.liveConflictMiddle': 'events,',
  'calendar.editor.liveConflictHardHint': 'this may cause a scheduling conflict; consider changing the time.',
  'calendar.editor.liveConflictSoftHint': 'the times are only marginally adjacent; adjust if needed.',
  'calendar.editor.moreItems': { one: '…and 1 more', other: '…and {count} more' },
  'calendar.editor.savedConflictNotice': {
    one: 'Saved, but it conflicts with 1 event: {tag}',
    other: 'Saved, but it conflicts with {count} events: {tag}',
  },
  'calendar.editor.conflictHard': 'hard conflict (fully overlapping)',
  'calendar.editor.conflictSoft': 'soft conflict (adjacent times)',

  // ── Event templates ──
  'calendar.editor.templateLabel': 'Template',
  'calendar.editor.templateQuickCreate': '— Create quickly from a template —',
  'calendar.editor.templateMeta': ' ({duration}, {type})',
  'calendar.editor.templateDuration': '{minutes} min',
  'calendar.editor.durationMinutes': '{minutes} min',
  'calendar.editor.saveAsTemplate': 'Save as template',
  'calendar.editor.saveAsTemplateTitle': 'Save the current event details as a template',
  'calendar.editor.manage': 'Manage',
  'calendar.editor.manageTemplates': 'Manage templates',
  'calendar.editor.manageTemplatesTitle': 'Manage the saved templates',
  'calendar.editor.noTemplates': 'No templates yet — save one first',
  'calendar.editor.templateName': 'Template name',
  'calendar.editor.templateNamePlaceholder': 'e.g. Weekly sync, Customer visit, Interview...',
  'calendar.editor.templateSaved': 'Template "{name}" saved',
  'calendar.editor.saveTemplateFailed': 'Failed to save the template',
  'calendar.editor.templateDeleted': 'Template deleted',
  'calendar.editor.deleteTemplateFailed': 'Failed to delete the template',
  'calendar.editor.deleteTemplate': 'Delete template',
  'calendar.editor.confirmSave': 'Confirm save',
  'calendar.editor.saveTemplateMessage': 'Save the current "{title}" as a template for quick reuse later.',
  'calendar.editor.unnamedEvent': 'Untitled event',

  // ── Basic fields ──
  'calendar.editor.titlePlaceholder': 'e.g. Team weekly meeting',
  'calendar.editor.descriptionLabel': 'Description',
  'calendar.editor.descriptionPlaceholder': 'Detailed description...',
  'calendar.editor.startTime': 'Start time',
  'calendar.editor.endTime': 'End time',
  'calendar.editor.locationPlaceholder': 'e.g. Meeting room A',
  'calendar.editor.dueAt': 'Due time',

  // ── Meeting-only fields ──
  'calendar.editor.meetingLink': 'Meeting link',
  'calendar.editor.hosts': 'Hosts',
  'calendar.editor.selectHostsPlaceholder': '— Select hosts —',
  'calendar.editor.removeHost': 'Remove {name}',
  'calendar.editor.noAssignableUsers': 'No users available',
  'calendar.editor.agendaIdsLabel': 'Agenda IDs (comma-separated, optional)',
  'calendar.editor.agendaIdsPlaceholder': 'e.g. 101, 102',
  'calendar.editor.selectAssigneePlaceholder': '— Select an owner —',
  'calendar.editor.userFallback': 'User #{id}',

  // ── Task priority (codes LOW/MEDIUM/HIGH/URGENT) ──
  'calendar.editor.priorityLow': 'Low',
  'calendar.editor.priorityMedium': 'Medium',
  'calendar.editor.priorityHigh': 'High',
  'calendar.editor.priorityUrgent': 'Urgent',

  // ── Color ──
  'calendar.editor.color': 'Color',
  'calendar.editor.colorHint': 'Click to select · the far-right one is custom',
  'calendar.editor.presetColors': 'Preset colors',
  'calendar.editor.customColor': 'Custom color',

  // ── Recurrence accordion ──
  'calendar.editor.recurrenceRule': 'Recurrence',
  'calendar.editor.recurrenceEnabled': 'Enabled',
  'calendar.editor.recurrenceActive': 'Recurrence enabled',
  'calendar.editor.frequency': 'Frequency',
  'calendar.editor.recurrenceNone': 'None',
  'calendar.editor.interval': 'Interval',
  'calendar.editor.weekdaysLabel': 'Days of week',
  'calendar.editor.recurrenceWeekdays': 'Recurrence days of week',
  'calendar.editor.weekdays': 'Weekdays',
  'calendar.editor.everyDay': 'Every day',
  'calendar.editor.clear': 'Clear',
  'calendar.editor.recurrenceEndMode': 'Ends',
  'calendar.editor.endModeNever': 'Never',
  'calendar.editor.endModeNeverDesc': 'Repeats forever',
  'calendar.editor.endModeCount': 'After N times',
  'calendar.editor.endModeCountDesc': 'A set number of times',
  'calendar.editor.endModeDate': 'On a date',
  'calendar.editor.endModeDateDesc': 'Until a date',
  'calendar.editor.recurrenceCountPrefix': 'For',
  'calendar.editor.recurrenceCountSuffix': 'times',
  'calendar.editor.recurrenceUntilPrefix': 'Until',

  // ── Reminder accordion ──
  'calendar.editor.loadingDots': 'Loading...',
  'calendar.editor.reminderLeadTime': 'Lead time',
  'calendar.editor.noReminder': 'No reminder',
  'calendar.editor.customDots': 'Custom...',
  'calendar.editor.reminderTypes': 'Reminder method',
  'calendar.editor.reminderTypesOption': ' (multiple allowed)',
  'calendar.editor.reminderTypesAria': 'Reminder methods (multiple allowed)',
  'calendar.editor.reminderDefaultHint': 'Unchecked = default (in-app + email)',
  'calendar.editor.reminderLeadShort': 'Lead',
  'calendar.editor.minutesBefore': 'minutes before',
  'calendar.editor.approxDays': { one: '≈ {count} day', other: '≈ {count} days' },
  'calendar.editor.approxHours': { one: '≈ {count} hour', other: '≈ {count} hours' },
  'calendar.editor.reminderTypesHelp': 'Reminder methods: in-app notification (popup) / email reminder (sent to attendee addresses) / SMS reminder. Not triggered when the lead time is "On time".',
  'calendar.editor.emailReminderNeedsAttendee': '⚠ Email reminders need attendee addresses; add them in the Attendees panel below.',

  // ── Attendees accordion ──
  'calendar.editor.attendees': 'Attendees',
  'calendar.editor.attendeesAdded': { one: '1 attendee added', other: '{count} attendees added' },
  'calendar.editor.clearAllAttendeesAria': 'Clear all attendees',
  'calendar.editor.clearAll': 'Clear all',
  'calendar.editor.removeAttendee': 'Remove {name}',
  'calendar.editor.attendeeInputHint': 'You can paste addresses (separated by Enter/comma/space); duplicates are removed on save. They are also used as reminder recipients.',
  'calendar.editor.attendeeInputPlaceholder': 'Enter an email (press Enter to add; paste multiple)',
  'calendar.editor.invalidEmailSingle': '"{email}" is not a valid email address',
  'calendar.editor.invalidEmailsMany': {
    one: '1 invalid email address: {list}',
    other: '{count} invalid email addresses: {list}',
  },
  'calendar.editor.invalidEmailsIgnored': {
    one: 'Ignored 1 invalid email address: {list}',
    other: 'Ignored {count} invalid email addresses: {list}',
  },
  'calendar.editor.listSeparator': ', ',

  // ── Footer actions / delete confirmation / template modals ──
  'calendar.editor.deletingDots': 'Deleting...',
  'calendar.editor.confirmDelete': 'Confirm delete',
  'calendar.editor.confirmDeleteMessage': 'Delete "{title}"? This action cannot be undone.',

  // ══ views — list / month / week / day / overview (`views/*`) ══

  // ── Shared across MonthView / DayView / WeekView (byte-identical in zh) ──
  // The Chinese source prefixes the weekday abbreviation; English needs no prefix (Mon/Sun stand alone)
  'calendar.views.shared.weekdayHeader': '{day}',
  'calendar.views.shared.daySuffix': '{day}',
  'calendar.views.shared.nameSeparator': ', ',
  // Trails the closing bracket in the drop-conflict toast; leading space is deliberate
  'calendar.views.shared.etc': ' etc.',
  'calendar.views.shared.dropBlocked': 'Cannot drop: {summary}',
  'calendar.views.shared.dropConflictWithNames': 'Overlaps {minutes} min with "{names}"{etc}',
  'calendar.views.shared.dropConflictNoNames': {
    one: '{count} hard conflict',
    other: '{count} hard conflicts',
  },
  'calendar.views.shared.dropConflictNoNamesWithOverlap': {
    one: '{count} hard conflict (overlap {minutes} min)',
    other: '{count} hard conflicts (overlap {minutes} min)',
  },

  // ── ListView.tsx ──
  'calendar.views.list.presetToday': 'Today',
  'calendar.views.list.presetWeek': 'This week',
  'calendar.views.list.presetMonth': 'This month',
  'calendar.views.list.colTime': 'Time',
  'calendar.views.list.recurrence': 'Repeat',
  'calendar.views.list.reminderMinutes': '{count} min before',
  'calendar.views.list.reminderHours': {
    one: '{count} hour before',
    other: '{count} hours before',
  },
  'calendar.views.list.reminderDays': {
    one: '{count} day before',
    other: '{count} days before',
  },
  'calendar.views.list.columnSettingsTitle': 'Shown columns',
  'calendar.views.list.restoreDefaults': 'Restore defaults',
  'calendar.views.list.markComplete': 'Mark complete',
  'calendar.views.list.actionDuplicate': 'Duplicate',
  'calendar.views.list.selectItem': 'Select',
  'calendar.views.list.paginationRange': {
    one: 'Showing {start}–{end} of {total} item',
    other: 'Showing {start}–{end} of {total} items',
  },
  'calendar.views.list.pageIndicator': 'Page {page} / {total}',
  'calendar.views.list.dateRangeSeparator': 'to',
  'calendar.views.list.searchPlaceholder': 'Search title / description / attendee...',
  'calendar.views.list.allTypes': 'All types',
  'calendar.views.list.allStatuses': 'All statuses',
  'calendar.views.list.columnSettings': 'Columns',
  'calendar.views.list.emptyTitle': 'No events yet',
  'calendar.views.list.undoComplete': 'Completed "{title}"',
  'calendar.views.list.toastCompleted': 'Marked as complete',
  'calendar.views.list.toastCompleteFailed': 'Action failed, please try again',
  'calendar.views.list.confirmDelete': 'Delete "{title}"?',
  'calendar.views.list.undoDelete': 'Deleted "{title}"',
  'calendar.views.list.toastDeleted': 'Deleted',
  'calendar.views.list.toastDeleteFailed': 'Delete failed, please try again',
  'calendar.views.list.toastDuplicated': 'Duplicated',
  'calendar.views.list.toastDuplicateFailed': 'Duplicate failed, please try again',
  'calendar.views.list.selectPage': 'Select this page',
  'calendar.views.list.deselectPage': 'Deselect this page',
  'calendar.views.list.partiallySelected': '(partially selected)',

  // ── MonthView.tsx ──
  'calendar.views.month.monthDay': '{month}/{day}',
  // Lunar month opening day; {month} comes from lunar-javascript (a Chinese month name)
  'calendar.views.month.lunarFirstDay': '{month} (lunar 1st)',
  'calendar.views.month.ariaDate': '{month}/{day}/{year}',
  'calendar.views.month.ariaOutsideMonth': 'Outside the displayed month',
  'calendar.views.month.ariaHardConflict': {
    one: 'At least 1 hard conflict among {count} overlapping item',
    other: 'At least 1 hard conflict among {count} overlapping items',
  },
  'calendar.views.month.ariaSoftConflict': 'Minor time conflict',
  'calendar.views.month.ariaEventCount': {
    one: '{count} event in total',
    other: '{count} events in total',
  },
  'calendar.views.month.ariaSeparator': ', ',
  'calendar.views.month.doubleClickHint': 'Double-click to create an event',
  'calendar.views.month.hardConflictDot': 'Severe conflict on this day',
  'calendar.views.month.softConflictDot': 'Minor conflict on this day',
  'calendar.views.month.severityHard': 'severe',
  'calendar.views.month.severitySoft': 'minor',
  'calendar.views.month.dragConflictTitle': {
    one: 'Predicted {severity} conflict: {count} item · overlap {minutes} min',
    other: 'Predicted {severity} conflict: {count} items · overlap {minutes} min',
  },
  'calendar.views.month.dragConflictWith': ' · includes: {names}{etc}',
  'calendar.views.month.dragMoreSuffix': ' etc.',
  'calendar.views.month.conflictCountBadge': {
    one: '{count} conflict',
    other: '{count} conflicts',
  },
  'calendar.views.month.conflictBadge': 'Conflict',
  'calendar.views.month.dragHardConflictAria': 'Severe conflict at the drop target; dropping is blocked',
  'calendar.views.month.dragSoftConflictAria': 'Minor conflict at the drop target',
  'calendar.views.month.gridAriaLabel': 'Month calendar grid with 6 rows and 7 columns; use the up and down arrow keys to move between adjacent date cells',
  'calendar.views.month.emptyTitle': 'No events this month',
  'calendar.views.month.gotoDayView': 'Go to day view',
  'calendar.views.month.copyDate': 'Copy date',
  'calendar.views.month.dateCopied': 'Date {date} copied to clipboard',
  'calendar.views.month.dateCopiedFallback': 'Date {date} copied (compatibility mode)',
  'calendar.views.month.copyFailed': 'Copy failed, please select and copy manually',

  // ── DayView.tsx ──
  'calendar.views.day.yearMonth': '{month}/{year}',
  'calendar.views.day.emptyTitle': 'No events today',

  // ── WeekView.tsx ──
  'calendar.views.week.emptyTitle': 'No events this week',

  // ── Overview.tsx ──
  'calendar.views.overview.barTooltip': {
    one: '{date}: {count} event',
    other: '{date}: {count} events',
  },
  'calendar.views.overview.todayWithDate': 'Today {weekday}, {month}/{day}/{year}',
  'calendar.views.overview.createEvent': 'Create event',
  'calendar.views.overview.listView': 'List view',
  'calendar.views.overview.kpiToday': "Today's events",
  'calendar.views.overview.kpiCompletedThisWeek': 'Completed this week',
  'calendar.views.overview.kpiConflicts': 'Conflicting events',
  'calendar.views.overview.kpiTomorrow': "Tomorrow's schedule",
  'calendar.views.overview.weekDistributionTitle': 'Event distribution over the last 7 days',
  'calendar.views.overview.weekDistributionHint': 'Click any bar to open that day',
  'calendar.views.overview.live': 'Live',
  'calendar.views.overview.trendTitle': 'Weekly event trend',
  'calendar.views.overview.trendHint': 'Event volume over the last 7 days (mini trend)',
  'calendar.views.overview.sparklineTooltip': {
    one: 'Last 7 days, day {day}: {count} event',
    other: 'Last 7 days, day {day}: {count} events',
  },
  'calendar.views.overview.upcomingTitle': 'Upcoming events',
  'calendar.views.overview.upcomingHint': 'Click any row to edit that event',
  'calendar.views.overview.upcomingCount': {
    one: '{count} event',
    other: '{count} events',
  },
  'calendar.views.overview.upcomingEmpty': 'No upcoming events',
  'calendar.views.overview.createFirstEvent': '+ Create your first event',

  // ══ panels — Outlook sync, subscriptions, shared panel pieces ══

  // ═══════════ OutlookSyncPanel — sync direction options ═══════════
  'calendar.panels.outlook.dirPushOnlyLabel': 'Push only',
  'calendar.panels.outlook.dirPushOnlyDesc': 'System → Outlook',
  'calendar.panels.outlook.dirPullOnlyLabel': 'Pull only',
  'calendar.panels.outlook.dirPullOnlyDesc': 'Outlook → System',
  'calendar.panels.outlook.dirBothLabel': 'Two-way sync',
  'calendar.panels.outlook.dirBothDesc': 'Push + pull',

  // ═══════════ Connection status card ═══════════
  // Connected: prefix + mailbox address (the address itself is data)
  'calendar.panels.outlook.connectedPrefix': 'Connected as ',
  'calendar.panels.outlook.notConnected': 'Outlook not connected',
  'calendar.panels.outlook.loadingStatus': 'Loading sync status...',
  'calendar.panels.outlook.connectedHint': 'Two-way sync is available; event changes flow automatically per your settings',
  'calendar.panels.outlook.notConnectedHint': 'Microsoft OAuth authorization is required before two-way sync can run',
  'calendar.panels.outlook.syncing': 'Syncing...',
  'calendar.panels.outlook.configureCredentials': 'Configure Azure AD credentials',

  // ═══════════ Not connected — capability card ═══════════
  'calendar.panels.outlook.capabilitiesTitle': 'About two-way sync',
  'calendar.panels.outlook.pushBold': 'Push:',
  'calendar.panels.outlook.pushDesc': 'Events created or changed in the system are pushed to your Outlook calendar automatically',
  'calendar.panels.outlook.pullBold': 'Pull:',
  'calendar.panels.outlook.pullDesc': 'Events created or changed in Outlook are pulled back into the system for management',
  'calendar.panels.outlook.conflictBold': 'Conflicts:',
  'calendar.panels.outlook.conflictDesc': 'A built-in conflict check keeps both sides; the last writer wins by default',

  // ═══════════ Not connected — Azure AD warning ═══════════
  'calendar.panels.outlook.notEnabledTitle': 'Outlook sync is not enabled',
  // This paragraph is split by <code> environment-variable names, hence the trailing space
  'calendar.panels.outlook.azureAdHint': 'Azure AD (Microsoft Entra ID) credentials are not configured, so OAuth authorization cannot be started. Click "Configure Azure AD credentials" above to set up your own credentials, or set these environment variables ',
  'calendar.panels.outlook.codeSeparator': ', ',
  'calendar.panels.outlook.codeTerminator': '.',

  // ═══════════ Not connected — connect button ═══════════
  'calendar.panels.outlook.connectDisabledTitle': 'Outlook sync is not configured. Configure Azure AD credentials first.',
  'calendar.panels.outlook.connecting': 'Connecting to Outlook...',
  'calendar.panels.outlook.connectOAuth': 'Connect an Outlook account (OAuth)',
  'calendar.panels.outlook.notConfiguredShort': 'Outlook sync not configured',

  // ═══════════ Connected — direction / auto sync / last sync ═══════════
  'calendar.panels.outlook.directionLabel': 'Sync direction',
  'calendar.panels.outlook.autoSyncTitle': 'Enable automatic sync',
  'calendar.panels.outlook.autoSyncHint': 'When off, scheduled two-way sync stops (you can still use "Sync now" manually)',
  'calendar.panels.outlook.lastSyncLabel': 'Last sync: ',
  'calendar.panels.outlook.neverSynced': 'Never synced; use "Sync now" first',

  // ═══════════ Connected — action buttons ═══════════
  'calendar.panels.outlook.saveSettings': 'Save settings',
  'calendar.panels.outlook.syncNow': 'Sync now',
  'calendar.panels.outlook.disconnect': 'Disconnect',

  // ═══════════ Sync result panel ═══════════
  'calendar.panels.outlook.syncComplete': 'Sync complete',
  'calendar.panels.outlook.durationLabel': 'Duration: ',
  'calendar.panels.outlook.metricPushed': 'Pushed to Outlook',
  'calendar.panels.outlook.metricPulled': 'Pulled from Outlook',
  'calendar.panels.outlook.metricUpdated': 'Updated',
  'calendar.panels.outlook.metricConflicts': 'Conflicts / skipped',
  'calendar.panels.outlook.unknownError': 'Unknown error',

  // ═══════════ Credentials modal ═══════════
  'calendar.panels.outlook.credentialsSubtitle': 'Microsoft Entra ID app registration credentials required by Outlook sync',
  'calendar.panels.outlook.loadingCredentials': 'Loading settings...',
  'calendar.panels.outlook.loadCredentialsFailed': 'Failed to load settings',
  'calendar.panels.outlook.credentialsConfigured': 'Credentials configured',
  'calendar.panels.outlook.credentialsNotConfigured': 'Credentials not configured',
  'calendar.panels.outlook.sourceLabel': 'Source: ',
  // Codes produced by the backend OutlookAdminSettingsDto.Source: user / appsettings
  'calendar.panels.outlook.sourceUser': 'Configured by user',
  'calendar.panels.outlook.sourceAppsettings': 'appsettings / environment variables',
  'calendar.panels.outlook.updatedAtPrefix': ' · Updated ',

  // ── Form fields ──
  'calendar.panels.outlook.clientIdLabel': 'Client ID (application client ID)',
  'calendar.panels.outlook.clientIdPlaceholder': 'Azure portal → App registrations → Overview → Application (client) ID',
  'calendar.panels.outlook.clientSecretLabel': 'Client Secret (client password)',
  'calendar.panels.outlook.clientSecretConfiguredPlaceholder': 'Configured (leave blank to keep it, enter a new value to overwrite)',
  'calendar.panels.outlook.clientSecretPlaceholder': 'Azure portal → Certificates & secrets → New client secret',
  'calendar.panels.outlook.clearSecret': 'Clear the configured ClientSecret',
  'calendar.panels.outlook.tenantIdLabel': 'Tenant ID (directory tenant ID)',
  'calendar.panels.outlook.tenantIdPlaceholder': 'common (multi-tenant) or Azure portal → Overview → Directory (tenant) ID',
  'calendar.panels.outlook.redirectUriLabel': 'Redirect URI (callback URL)',
  'calendar.panels.outlook.redirectUriPlaceholder': 'https://<your-domain>/api/outlooksync/oauth/callback (must match the Azure app registration)',
  'calendar.panels.outlook.scopesLabel': 'Scopes (permission scopes)',
  'calendar.panels.outlook.scopesPlaceholder': 'Calendars.ReadWrite User.Read offline_access (space-separated)',

  // ── Feedback and saving ──
  'calendar.panels.outlook.noChanges': 'No changes to save',
  'calendar.panels.outlook.credentialsSaved': 'Settings saved; the new credentials take effect immediately',
  'calendar.panels.outlook.clearSecretConfirm': 'Clear the configured ClientSecret? Outlook sync will no longer be able to start OAuth authorization.',
  'calendar.panels.outlook.secretCleared': 'ClientSecret cleared',
  'calendar.panels.outlook.clearSecretFailed': 'Failed to clear the ClientSecret',
  'calendar.panels.outlook.saveConfiguration': 'Save settings',
  'calendar.panels.outlook.doneBack': 'Done — back to the sync panel →',

  // ── Azure portal setup guide (split by <code> elements) ──
  'calendar.panels.outlook.guideTitle': 'Setup guide:',
  'calendar.panels.outlook.guideStep1Prefix': '1. Sign in at ',
  'calendar.panels.outlook.guideStep1Suffix': ' → Microsoft Entra ID → App registrations → New registration',
  'calendar.panels.outlook.guideStep2': '2. Choose "Web" as the redirect URI type and use the Redirect URI above as its value',
  'calendar.panels.outlook.guideStep3': '3. Certificates & secrets → New client secret → copy the "Value" (not the secret ID)',
  'calendar.panels.outlook.guideStep4Prefix': '4. API permissions → Add ',
  'calendar.panels.outlook.guideStep4Suffix': ' (delegated permission)',
  'calendar.panels.outlook.guideStep5': '5. Copy the "Application (client) ID" and "Directory (tenant) ID" from the Overview page into the form above',

  // ═══════════ SubscriptionPanel — calendar subscription ═══════════
  'calendar.panels.subscription.title': 'Calendar subscription',
  'calendar.panels.subscription.subtitle': 'Sync to external calendar apps through an iCal link',
  'calendar.panels.subscription.autoSyncTitle': 'One-click automatic sync',
  'calendar.panels.subscription.standardBadge': 'iCal / .ics standard',
  'calendar.panels.subscription.bullet1': 'Copy the link into Outlook / Apple Calendar / Google Calendar to sync automatically',
  'calendar.panels.subscription.bullet2': 'The subscription link is read-only; external apps cannot change events in the system',
  'calendar.panels.subscription.bullet3': 'Regenerating invalidates the old link immediately and protects your calendar privacy',
  'calendar.panels.subscription.linkLabel': 'Subscription link',
  'calendar.panels.subscription.copiedLabel': 'Copied ✓',
  'calendar.panels.subscription.copyLink': 'Copy link',
  'calendar.panels.subscription.regenerate': 'Regenerate',
  'calendar.panels.subscription.loadingLink': 'Loading subscription link...',
  'calendar.panels.subscription.emptyTitle': 'No subscription link yet',
  'calendar.panels.subscription.emptyHint': 'Click the button below to generate your own iCal subscription link; it can sync to Outlook / Apple Calendar / Google Calendar',
  'calendar.panels.subscription.generateLink': 'Generate subscription link',
  'calendar.panels.subscription.copiedToast': 'Subscription link copied to the clipboard',
  'calendar.panels.subscription.copiedFallbackToast': 'Subscription link copied to the clipboard (compatibility mode)',
  'calendar.panels.subscription.copyFailedToast': 'Copy failed; select the text and copy it manually',
  'calendar.panels.subscription.regeneratedToast': 'A new subscription link was generated; the old link is now invalid',
  'calendar.panels.subscription.regenerateFailedToast': 'Failed to regenerate the subscription link',

  // ═══════════ shared.tsx — shared panel primitives ═══════════
  // The close button's aria-label reuses common.close and the toast close uses calendar.common.closeHint
  'calendar.panels.shared.toggleAriaLabel': 'Toggle switch',

  // ══ trash — trash panel and trash view (one shared set of keys) ══

  // ── Row actions (table row and mobile card share these) ──
  'calendar.trash.restore': 'Restore',
  'calendar.trash.hardDelete': 'Delete permanently',

  // ── Toolbar ──
  'calendar.trash.searchPlaceholder': 'Search deleted titles, locations, or notes...',
  'calendar.trash.filterHint': 'Filter: full-text match on title/location',
  'calendar.trash.restoreAll': 'Restore all restorable',
  'calendar.trash.clearTrash': 'Empty trash',
  'calendar.trash.purgeExpired': 'Purge expired items',

  // ── Table headers / empty state ──
  'calendar.trash.colTitleLocation': 'Title / location',
  'calendar.trash.colDeletedAt': 'Deleted at',
  'calendar.trash.colActions': 'Actions',
  'calendar.trash.emptyTitle': 'Trash is empty',
  'calendar.trash.emptyHint': 'Deleted events are kept here for a while and can be restored at any time',

  // ── Count sentences (split so the interpolated value keeps its own <span> emphasis) ──
  // Shared by the panel and the page view ('共 ' prefix, followed by the styled count span)
  'calendar.trash.countPrefix': 'Total ',
  // Pagination: Total {count} items (page {page} / {total})
  'calendar.trash.paginationMiddle': ' items (page ',
  'calendar.trash.paginationSuffix': ')',
  // Panel header subtitle (template literal, no styled element, so plain interpolation)
  'calendar.trash.headerSubtitle': {
    one: '1 item, kept for {days} days',
    other: '{count} items, kept for {days} days',
  },
  // Page-view header: Total {count} deleted events, kept for {days} days before automatic cleanup
  'calendar.trash.viewHeaderMiddle': ' deleted events, kept for ',
  'calendar.trash.viewHeaderSuffix': ' days before automatic cleanup',

  // ── Restore confirmation ──
  'calendar.trash.restoreDialogTitle': 'Restore event',
  'calendar.trash.restoreConfirmMsg': 'Restore the event "{title}"?',

  // ── Permanent-delete confirmation ──
  'calendar.trash.hardDeleteDialogTitle': '⚠️ Dangerous action: permanently delete event',
  'calendar.trash.hardDeleteConfirmMsg': 'This cannot be undone; the event will be permanently deleted from the database.\n\nDelete the event "{title}".',
  'calendar.trash.permanentDelete': 'Delete permanently',
  'calendar.trash.hardDeleteWarn': 'This cannot be undone; the event data will be removed from the system entirely',

  // ── Empty-trash confirmation ──
  'calendar.trash.clearDialogTitle': '⚠️ Dangerous action: empty the trash',
  'calendar.trash.clearConfirmMsg': {
    one: 'This cannot be undone; events will be permanently deleted from the database.\n\nThis will empty the trash, removing the single event it contains.',
    other: 'This cannot be undone; events will be permanently deleted from the database.\n\nThis will empty the trash, removing all {count} events it contains.',
  },
  'calendar.trash.confirmClear': 'Empty trash',
  'calendar.trash.clearWarn': 'After emptying, every event in the trash will be permanently deleted and cannot be restored',

  // ── Purge-by-age confirmation ──
  'calendar.trash.purgeDialogTitle': '⚠️ Dangerous action: purge trash by age',
  'calendar.trash.purgeConfirmMsg': 'Trash events deleted before the specified number of days ago will be permanently deleted. This cannot be undone.',
  'calendar.trash.confirmPurge': 'Purge',
  'calendar.trash.purgeWarn': 'After purging, matching trash events will be permanently deleted and cannot be restored',
  'calendar.trash.retentionLabel': 'Retention days:',
  'calendar.trash.retentionUnit': 'days (0-3650)',

  // ══ store — `store.ts` / `calendarUtils.ts` / `hooks.ts` (resolved with `tBound`) ══

  // ── Event types (EVENT_TYPE_STYLE has 8 codes / EVENT_TYPE_OPTIONS has 4; identical labels share keys) ──
  'calendar.store.eventType.dueDate': 'Due date',
  'calendar.store.eventType.task': 'Task',
  'calendar.store.eventType.reminder': 'Reminder',
  'calendar.store.eventType.meeting': 'Meeting',
  'calendar.store.eventType.personal': 'Personal',
  'calendar.store.eventType.travel': 'Travel',
  'calendar.store.eventType.work': 'Work',
  'calendar.store.eventType.other': 'Other',

  // ── Event status ──
  // STATUS_STYLE.pending ('To do', chip badge) and STATUS_OPTIONS.pending ('Pending', dropdown) differ.
  'calendar.store.status.pending': 'To do',
  'calendar.store.status.pendingOption': 'Pending',
  'calendar.store.status.inProgress': 'In progress',
  'calendar.store.status.completed': 'Completed',
  'calendar.store.status.cancelled': 'Cancelled',
  'calendar.store.status.missed': 'Missed',

  // ── Priority (calendar-owned 1-5; distinct from the conversation priority in format.priority.*) ──
  'calendar.store.priority.unset': 'Not set',
  'calendar.store.priority.low': 'Low',
  'calendar.store.priority.lower': 'Fairly low',
  'calendar.store.priority.medium': 'Medium',
  'calendar.store.priority.higher': 'Fairly high',
  'calendar.store.priority.high': 'High',

  // ── Reminder presets (REMINDER_OPTIONS labels) ──
  'calendar.store.reminderOption.onTime': 'On time',
  'calendar.store.reminderOption.minutes5': '5 minutes before',
  'calendar.store.reminderOption.minutes15': '15 minutes before',
  'calendar.store.reminderOption.minutes30': '30 minutes before',
  'calendar.store.reminderOption.hours1': '1 hour before',
  'calendar.store.reminderOption.days1': '1 day before',
  'calendar.store.reminderOption.weeks1': '1 week before',

  // ── Reminder fallbacks for non-preset offsets ──
  'calendar.store.reminder.onTime': 'Remind on time',
  'calendar.store.reminder.minutesAgo': '{minutes} minutes before',
  'calendar.store.reminder.hoursAgo': '{hours} hours before',
  'calendar.store.reminder.daysAgo': '{days} days before',

  // ── Reminder channels (REMINDER_TYPE_OPTIONS / REMINDER_TYPE_LABELS) ──
  'calendar.store.reminderType.popup': 'In-app notification',
  'calendar.store.reminderType.email': 'Email reminder',
  'calendar.store.reminderType.sms': 'SMS reminder',
  // Fallback for an empty reminderTypesLabel value
  'calendar.store.reminderType.default': 'Default (in-app + email)',

  // ── Recurrence type (RECURRENCE_TYPE_OPTIONS) ──
  'calendar.store.recurrenceType.daily': 'Daily',
  'calendar.store.recurrenceType.weekly': 'Weekly',
  'calendar.store.recurrenceType.monthly': 'Monthly',
  'calendar.store.recurrenceType.yearly': 'Yearly',

  // ── Recurrence unit (RECURRENCE_UNIT_LABELS; English pluralizes on `count`) ──
  'calendar.store.recurrenceUnit.day': { one: 'day', other: 'days' },
  'calendar.store.recurrenceUnit.week': { one: 'week', other: 'weeks' },
  'calendar.store.recurrenceUnit.month': { one: 'month', other: 'months' },
  'calendar.store.recurrenceUnit.year': { one: 'year', other: 'years' },

  // ── Recurrence sentence (shared suffix by getRecurrenceLabel / generateRRuleDescription) ──
  'calendar.store.recurrence.every': 'Every {interval} {unit}',
  'calendar.store.recurrence.countSuffix': { one: ', {count} time', other: ', {count} times' },
  'calendar.store.recurrence.untilSuffix': ', until {date}',

  // ── RRULE description fragments (generateRRuleDescription) ──
  'calendar.store.rrule.everyPrefix': 'Every ',
  // Inserted only when interval > 1 (trailing space is intentional)
  'calendar.store.rrule.intervalMiddle': '{interval} ',
  'calendar.store.rrule.weekdayPrefix': ' on ',
  'calendar.store.rrule.weekdaySeparator': ', ',

  // ── Duration text (getDurationLabel / getEventDuration) ──
  'calendar.store.duration.allDayDays': { one: 'Total: {days} day', other: 'Total: {days} days' },
  'calendar.store.duration.daysHours': { one: 'Total: {days} day {hours}h', other: 'Total: {days} days {hours}h' },
  'calendar.store.duration.hoursOnly': { one: 'Total: {hours} hour', other: 'Total: {hours} hours' },
  // Appended only when minutes > 0 (leading space is intentional)
  'calendar.store.duration.minutesSuffix': { one: ' {minutes} minute', other: ' {minutes} minutes' },
  'calendar.store.duration.minutesOnly': { one: 'Total: {minutes} minute', other: 'Total: {minutes} minutes' },
  // Compact form for getEventDuration
  'calendar.store.duration.compactMinutes': '{minutes}m',
  'calendar.store.duration.compactHoursMinutes': '{hours}h{minutes}m',
  'calendar.store.duration.compactHours': '{hours}h',
  'calendar.store.duration.compactDaysHours': '{days}d{hours}h',
  'calendar.store.duration.compactDays': '{days}d',

  // ── Color options (COLOR_OPTIONS; UI palette only — the backend stores a free-form hex string) ──
  'calendar.store.color.red': 'Red',
  'calendar.store.color.rose': 'Rose',
  'calendar.store.color.crimson': 'Crimson',
  'calendar.store.color.orange': 'Orange',
  'calendar.store.color.amber': 'Amber',
  'calendar.store.color.yellow': 'Yellow',
  'calendar.store.color.gold': 'Gold',
  'calendar.store.color.lime': 'Lime',
  'calendar.store.color.brightGreen': 'Bright green',
  'calendar.store.color.emerald': 'Emerald',
  'calendar.store.color.darkGreen': 'Dark green',
  'calendar.store.color.teal': 'Teal',
  'calendar.store.color.sky': 'Sky blue',
  'calendar.store.color.lightBlue': 'Light blue',
  'calendar.store.color.blue': 'Blue',
  'calendar.store.color.darkBlue': 'Dark blue',
  'calendar.store.color.indigo': 'Indigo',
  'calendar.store.color.purple': 'Purple',
  'calendar.store.color.brightPurple': 'Bright purple',
  'calendar.store.color.magenta': 'Magenta',
  'calendar.store.color.pink': 'Pink',
  'calendar.store.color.slate': 'Slate gray',
  'calendar.store.color.warmGray': 'Warm gray',
  'calendar.store.color.coffee': 'Coffee',
  'calendar.store.color.neonLime': 'Neon lime',
  'calendar.store.color.wine': 'Wine red',

  // ── Lunar calendar (lunar-javascript only emits Chinese; the month name is an interpolated value) ──
  'calendar.store.lunar.monthFirstDay': '{month} (lunar 1st)',

  // ── store.ts: sync / trash / undo-redo transient toasts ──
  'calendar.store.toast.pendingPatchExpired': { one: '{count} event has local changes pending sync for over 10 minutes; its position was kept', other: '{count} events have local changes pending sync for over 10 minutes; their positions were kept' },
  'calendar.store.toast.outlookStatusFailed': 'Failed to load the Outlook sync status',
  'calendar.store.toast.outlookNotConfigured': 'Outlook sync has no Azure AD credentials configured; contact your administrator',
  'calendar.store.toast.outlookAuthorizeUrlFailed': 'Failed to get the Outlook authorization link',
  'calendar.store.toast.outlookDisconnected': 'Disconnected from Outlook',
  'calendar.store.toast.outlookDisconnectFailed': 'Failed to disconnect',
  'calendar.store.toast.outlookSettingSaved': 'Outlook sync settings saved',
  'calendar.store.toast.outlookSyncDone': 'Sync finished — pushed: {pushed}, pulled: {pulled}, conflicts: {conflicts}',
  'calendar.store.toast.staleRestoreRetry': 'Changed by another user; refreshed to the latest version — click restore again',
  'calendar.store.toast.staleRestoreRetryShort': 'Changed by another user; refreshed — click restore again',
  'calendar.store.toast.restoreFailed': 'Restore failed',
  'calendar.store.toast.restoredCount': { one: 'Restored {count} event', other: 'Restored {count} events' },
  'calendar.store.toast.someEventsNotRestored': 'Some events could not be restored',
  'calendar.store.toast.noRestorableEvents': 'No restorable events',
  'calendar.store.toast.unknownReason': 'Unknown reason',
  'calendar.store.toast.eventsNotRestored': { one: '{count} event could not be restored: {reason}', other: '{count} events could not be restored: {reason}' },
  'calendar.store.toast.batchRestoreFailed': 'Bulk restore failed',
  'calendar.store.toast.staleHardDeleteRetry': 'Changed by another user; refreshed to the latest version — click permanent delete again',
  'calendar.store.toast.hardDeleteFailed': 'Permanent delete failed',
  'calendar.store.toast.clearTrashFailed': 'Failed to empty the trash',
  'calendar.store.toast.purgeTrashFailed': 'Failed to purge expired records',

  // ── 409 time-conflict toast fragments (separator / prefix / 'and N more' suffix) ──
  'calendar.store.conflict.titleSeparator': ', ',
  'calendar.store.conflict.prefix': 'Time conflict: ',
  // Appended only when more than 3 conflicts exist (leading space is intentional)
  'calendar.store.conflict.more': ' and {count} more',

  // ── Undo / redo (undoStack description and toasts) ──
  'calendar.store.undo.move': 'Move event',
  'calendar.store.undo.resize': 'Resize event',
  'calendar.store.undo.undoneToast': 'Undone: {description}',
  'calendar.store.undo.failPrefix': 'Undo failed — ',
  'calendar.store.undo.failed': 'Undo failed',
  'calendar.store.redo.redoneToast': 'Redone: {description}',
  'calendar.store.redo.failPrefix': 'Redo failed — ',
  'calendar.store.redo.failed': 'Redo failed',

  // ══ shell — page shell, event chips, batch toolbar, filter bar ══

  // ── Calendar.tsx · view names / toolbar / navigation ──
  'calendar.shell.page.viewCalendar': 'Calendar',
  'calendar.shell.page.viewList': 'List',
  'calendar.shell.page.viewSubscription': 'Subscriptions',
  'calendar.shell.page.prevPeriod': 'Previous period',
  'calendar.shell.page.nextPeriod': 'Next period',
  'calendar.shell.page.subViewMonth': 'Month',
  'calendar.shell.page.subViewWeek': 'Week',
  'calendar.shell.page.subViewDay': 'Day',
  // Date joins: en-US numeric order (Intl's default); zh keeps the original spacing
  'calendar.shell.page.navMonth': '{month}/{year}',
  'calendar.shell.page.navWeek': '{startMonth}/{startDay}/{startYear} - {endMonth}/{endDay}',
  'calendar.shell.page.navDay': '{weekday}, {month}/{day}/{year}',
  'calendar.shell.page.shortcutN': 'Shortcut: N',
  'calendar.shell.page.newEventOffline': 'You are offline; creating events is disabled',
  'calendar.shell.page.undo': 'Undo',

  // ── Calendar.tsx · status banners / view load failure ──
  'calendar.shell.page.signalRReconnecting': 'Live update connection lost; reconnecting automatically (backoff)… reminders, undo and conflict broadcasts are delayed until it recovers.',
  'calendar.shell.page.signalROffline': 'The live update service is offline. Refresh the page or try again later to reconnect.',
  'calendar.shell.page.viewLoadFailed': 'Failed to load the calendar view',
  'calendar.shell.page.viewLoadFailedDesc': 'This view module threw an error and was isolated. Use the recover button to reload it.',

  // ── Calendar.tsx · uncaught error toasts ──
  'calendar.shell.page.unexpectedError': 'An unexpected error occurred on this page',
  'calendar.shell.page.uncaughtErrorToast': 'Something went wrong: {message} (logged; please retry or refresh later)',
  'calendar.shell.page.asyncTaskFailed': 'The network request or async task failed',
  'calendar.shell.page.asyncOperationFailed': 'The async operation failed',
  'calendar.shell.page.operationFailed': 'Operation failed: {message}',

  // ── Calendar.tsx · ICS import / export ──
  'calendar.shell.page.importExportIcsTitle': 'Import / export ICS',
  'calendar.shell.page.importExportIcsAria': 'Import and export ICS',
  'calendar.shell.page.importExportSection': 'Import/export',
  'calendar.shell.page.importing': 'Importing...',
  'calendar.shell.page.importIcsFile': 'Import ICS file',
  'calendar.shell.page.preparingExport': 'Preparing export...',
  'calendar.shell.page.exportRangeIcs': 'Export current range as ICS',
  'calendar.shell.page.subscribeWebcal': 'Subscribe to calendar (WebCal)',
  'calendar.shell.page.icsImportDone': {
    one: 'Import finished: {imported} event added',
    other: 'Import finished: {imported} events added',
  },
  'calendar.shell.page.icsImportNone': 'No events added',
  'calendar.shell.page.icsImportSkipped': ', {skipped} skipped',
  'calendar.shell.page.icsImportFailed': 'Import failed; make sure the file is a valid .ics file',
  'calendar.shell.page.icsExporting': 'Exporting ICS file',

  // ── Calendar.tsx · mailbox filter pill ──
  'calendar.shell.page.mailboxAll': 'All mailboxes',
  'calendar.shell.page.mailboxOne': '1 mailbox',
  'calendar.shell.page.mailboxCount': { one: '{count} mailbox', other: '{count} mailboxes' },
  'calendar.shell.page.mailboxIdFallback': 'Mailbox #{id}',
  'calendar.shell.page.mailboxFilter': 'Mailbox filter',
  'calendar.shell.page.clearMailboxFilter': 'Clear mailbox filter',
  'calendar.shell.page.selectAll': 'Select all',

  // ── Calendar.tsx · screen-reader live announcements (aria-live text) ──
  'calendar.shell.page.announceCleared': 'Selection cleared',
  'calendar.shell.page.announceSelectedOne': '1 event selected',
  'calendar.shell.page.announceSelectedMany': {
    one: 'Batch: {count} event selected',
    other: 'Batch: {count} events selected',
  },
  'calendar.shell.page.announceNoConflict': 'No conflicting events in the current overview range',
  'calendar.shell.page.announceConflictOne': '1 time conflict detected',
  'calendar.shell.page.announceConflictMany': {
    one: '{count} time conflict detected',
    other: '{count} time conflicts detected',
  },

  // ── chips.tsx · event chips (EventChip / AllDayEventBar / HourRow) ──
  'calendar.shell.chips.listSeparator': ', ',
  'calendar.shell.chips.allDaySpan': { one: '1 day from {date}', other: '{days} days from {date}' },
  'calendar.shell.chips.conflictWithTitle': 'Time overlaps with {list}',
  'calendar.shell.chips.untitledEvent': '(Untitled event)',
  'calendar.shell.chips.conflictCount': { one: '{count} conflict', other: '{count} conflicts' },
  'calendar.shell.chips.creatorSuffix': ' · Created by: {name}',
  'calendar.shell.chips.conflictDetail': {
    one: '{count} time conflict ({kind})',
    other: '{count} time conflicts ({kind})',
  },
  'calendar.shell.chips.severeOverlap': 'severe overlap',
  'calendar.shell.chips.edgeOverlap': 'partial overlap',
  'calendar.shell.chips.moreConflicts': '+{count} more',
  'calendar.shell.chips.hoverHint': 'Click to open · drag to move to another date',
  'calendar.shell.chips.eventAriaTime': {
    one: 'starts at minute {offset}, lasting {duration} minute',
    other: 'starts at minute {offset}, lasting {duration} minutes',
  },
  'calendar.shell.chips.eventAriaSelected': ', selected',
  'calendar.shell.chips.searchCurrent': 'Searching for "{keyword}"',

  // ── chips.tsx · EmptyState defaults for the six variants ──
  'calendar.shell.chips.empty.month.title': 'No events this month',
  'calendar.shell.chips.empty.month.desc': 'Use the button below to create your first event',
  'calendar.shell.chips.empty.month.cta': '+ Create your first event',
  'calendar.shell.chips.empty.week.title': 'Nothing scheduled this week',
  'calendar.shell.chips.empty.week.desc': 'Start planning your week here',
  'calendar.shell.chips.empty.week.cta': '+ Add to this week',
  'calendar.shell.chips.empty.day.title': 'Nothing scheduled today',
  'calendar.shell.chips.empty.day.desc': 'Take a coffee break and plan today',
  'calendar.shell.chips.empty.day.cta': '+ Plan today',
  'calendar.shell.chips.empty.list.title': 'No events',
  'calendar.shell.chips.empty.list.desc': 'Events you create will appear in this list',
  'calendar.shell.chips.empty.overview.title': 'No data',
  'calendar.shell.chips.empty.overview.desc': 'Create an event to see the stats overview',
  'calendar.shell.chips.empty.overview.cta': '+ Create an event to start tracking',
  'calendar.shell.chips.empty.search.title': 'No matching events found',
  'calendar.shell.chips.empty.search.desc': 'Try a different keyword',
  'calendar.shell.chips.empty.search.cta': 'Clear search',
  'calendar.shell.chips.empty.newEventCta': '+ New event',

  // ── BatchMultiSelectToolbar.tsx ──
  'calendar.shell.batch.listSeparator': ', ',
  'calendar.shell.batch.doneWithFailures': 'Batch operation finished: {success} succeeded, {failed} failed',
  'calendar.shell.batch.failureReasons': '. Reasons: {reasons}',
  'calendar.shell.batch.done': {
    one: 'Batch operation finished: {count} item succeeded',
    other: 'Batch operation finished: {count} items succeeded',
  },
  'calendar.shell.batch.undoDelete': { one: 'Deleted {count} item', other: 'Deleted {count} items' },
  'calendar.shell.batch.undoUpdate': { one: 'Updated {count} item', other: 'Updated {count} items' },
  'calendar.shell.batch.failed': 'Batch operation failed: {message}',
  'calendar.shell.batch.selectedCount': '{count} selected',
  'calendar.shell.batch.selectAll': 'Select all',
  'calendar.shell.batch.changeType': 'Change type…',
  'calendar.shell.batch.changeStatus': 'Change status…',
  'calendar.shell.batch.clearSelectionHint': 'Clear current selection',
  'calendar.shell.batch.noSelectionHint': 'No events selected (click an event to select it)',
  'calendar.shell.batch.exitMultiSelectHint': 'Exit multi-select mode',
  'calendar.shell.batch.exitMultiSelect': 'Exit multi-select',
  'calendar.shell.batch.enterMultiSelectHint': 'Enter multi-select mode (batch change type/status/delete)',
  'calendar.shell.batch.multiSelect': 'Multi-select',
  'calendar.shell.batch.deleteConfirm': {
    one: 'Delete the {count} selected event? You can restore it from Trash within 30 days.',
    other: 'Delete the {count} selected events? You can restore them from Trash within 30 days.',
  },

  // ── EventTypeFilterBar.tsx ──
  'calendar.shell.filter.all': 'All',

  // ── GlobalCalendarReminderListener.tsx ──
  'calendar.shell.reminder.title': 'Event reminder',
  'calendar.shell.reminder.minutesAhead': {
    one: ' · {minutes} minute before',
    other: ' · {minutes} minutes before',
  },
  'calendar.shell.reminder.untitled': '(Unnamed event)',
  'calendar.shell.reminder.close': 'Dismiss reminder',
  'calendar.shell.reminder.confirming': 'Confirming...',
  'calendar.shell.reminder.confirmReceived': 'Acknowledge',
  'calendar.shell.reminder.viewDetails': 'View details',

  // ══ a11y — accessibility and reliability text (`a11yReliability.tsx`) ══

  // ── Error boundary (CalendarErrorBoundary) ──
  'calendar.a11y.crashTitle': 'This page component crashed',
  'calendar.a11y.crashDescription': 'Part of this page failed and was isolated so other features keep working. Try to recover.',
  'calendar.a11y.recover': 'Recover and reload',

  // ── Offline detection (OfflineBanner / announceOfflineBlock) ──
  'calendar.a11y.offlineBanner': 'You are offline; creating / editing / deleting is disabled. It clears automatically once the network is back.',
  'calendar.a11y.offlineBlockedRetry': 'You are offline and write operations are blocked. Restore the network and try again.',
  'calendar.a11y.offlineBlockedShort': 'You are offline and write operations are blocked',
} satisfies Entries;
