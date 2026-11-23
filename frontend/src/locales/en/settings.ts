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

  // ── Page header / tabs / sub-tabs ──
  'settings.page.title': 'System settings',
  'settings.page.subtitle': 'Manage system options, mail tests, and system info',
  'settings.page.tab.system': 'System options',
  'settings.page.tab.mail': 'Mail tests',
  'settings.page.tab.info': 'System info',
  'settings.page.subTab.general': 'General',
  'settings.page.subTab.email': 'Email',
  'settings.page.subTab.security': 'Security',
  'settings.page.subTab.notification': 'Notifications',
  'settings.page.subTab.conversation': 'Conversations',
  'settings.page.subTab.advanced': 'Advanced',

  // ── Shared: toasts / errors / save state ──
  'settings.page.loadFailed': 'Failed to load',
  'settings.page.saved': 'Settings saved',
  'settings.page.saveFailed': 'Failed to save',
  'settings.page.saving': 'Saving...',
  'settings.page.operationFailed': 'Operation failed',
  'settings.page.deleteFailed': 'Failed to delete',
  'settings.page.status.enabled': 'On',
  'settings.page.status.disabled': 'Off',

  // ── General: basic information ──
  'settings.page.general.title': 'Basic information',
  'settings.page.general.description': 'Configure basic information and display options',
  'settings.page.general.systemName': 'System name',
  'settings.page.general.systemNameHint': 'Shown in the browser title and on the sign-in page',
  'settings.page.general.companyName': 'Company name',
  'settings.page.general.companyNameHint': 'Used in email signatures and similar places',
  'settings.page.general.defaultLanguage': 'Default language',
  'settings.page.language.zhHans': 'Simplified Chinese',
  'settings.page.language.zhHant': 'Traditional Chinese',
  'settings.page.language.en': 'English',
  'settings.page.language.ja': 'Japanese',
  'settings.page.general.defaultTimezone': 'Default time zone',
  'settings.page.general.currency': 'Currency',
  'settings.page.currency.cny': 'Chinese yuan (CNY)',
  'settings.page.currency.usd': 'US dollar (USD)',
  'settings.page.currency.eur': 'Euro (EUR)',
  'settings.page.currency.jpy': 'Japanese yen (JPY)',
  'settings.page.currency.gbp': 'Pound sterling (GBP)',
  'settings.page.general.dateFormat': 'Date format',
  'settings.page.dateFormat.iso': '2024-01-15',
  'settings.page.dateFormat.slashYmd': '2024/01/15',
  'settings.page.dateFormat.mdy': '01/15/2024',
  'settings.page.dateFormat.dmy': '15/01/2024',
  'settings.page.dateFormat.cjk': '2024 (year) 01 (month) 15 (day)',

  // ── Email: fetch parameters and behavior ──
  'settings.page.email.title': 'Mail fetching',
  'settings.page.email.description': 'Configure how often mail is fetched and how fetching behaves',
  'settings.page.email.fromName': 'Sender name',
  'settings.page.email.fromNameHint': 'Sender shown on system email',
  'settings.page.email.fetchFrequency': 'Fetch frequency (minutes)',
  'settings.page.email.fetchFrequencyHint': 'Interval between scheduled mail fetches',
  'settings.page.email.fetchFrequency.every': { one: 'Every minute', other: 'Every {count} minutes' },
  'settings.page.email.maxFetchPerRun': 'Max messages per fetch',
  'settings.page.email.maxFetchPerRunHint': 'Maximum number of messages fetched per run',
  'settings.page.email.maxFetchPerRun.count': { one: '{count} message', other: '{count} messages' },
  'settings.page.email.behavior': 'Fetch behavior',
  'settings.page.email.markAsRead': 'Mark as read after fetching',
  'settings.page.email.markAsReadDescription': 'Mark messages as read on the mail server after a successful fetch',
  'settings.page.email.saveToSent': 'Save sent email to Sent',
  'settings.page.email.saveToSentDescription': 'Save the reply to the Sent folder after sending',
  'settings.page.email.deleteAfterFetch': 'Delete messages from the server after fetching',
  'settings.page.email.deleteAfterFetchDescription':
    'Delete the original messages from the mail server after a successful fetch (not recommended)',

  // ── Email: customer details and contact collection ──
  'settings.page.email.contactCollection.title': 'Customer details and contact collection',
  'settings.page.email.contactCollection.description':
    'Extract customer contact details from the signature block while fetching mail and fill them into the customer profile',
  'settings.page.email.contactCollection.detail':
    'While fetching mail, extract the customer name, phone, and email from the message body (signature block first) and fill them into the customer profile. Existing fields are not overwritten; only missing fields are filled. Up to 3 extraction attempts per customer.',

  // ── Security: password policy ──
  'settings.page.security.title': 'Password policy',
  'settings.page.security.description': 'Configure the security requirements for user passwords',
  'settings.page.security.passwordMinLength': 'Minimum password length',
  'settings.page.security.passwordMinLength.chars': { one: '{count} character', other: '{count} characters' },
  'settings.page.security.maxLoginAttempts': 'Max sign-in attempts',
  'settings.page.security.maxLoginAttemptsHint': 'The account is locked after this many attempts',
  'settings.page.security.maxLoginAttempts.times': { one: '{count} attempt', other: '{count} attempts' },
  'settings.page.security.maxLoginAttempts.unlimited': 'Unlimited',
  'settings.page.security.requireUppercase': 'Must contain an uppercase letter',
  'settings.page.security.requireUppercaseDescription':
    'The password must contain at least one uppercase letter (A-Z)',
  'settings.page.security.requireLowercase': 'Must contain a lowercase letter',
  'settings.page.security.requireLowercaseDescription':
    'The password must contain at least one lowercase letter (a-z)',
  'settings.page.security.requireNumber': 'Must contain a number',
  'settings.page.security.requireNumberDescription': 'The password must contain at least one digit (0-9)',
  'settings.page.security.requireSymbol': 'Must contain a special character',
  'settings.page.security.requireSymbolDescription':
    'The password must contain at least one special character (!@#$%^&*)',

  // ── Sessions and sign-up ──
  'settings.page.session.title': 'Sessions and sign-up',
  'settings.page.session.sessionTimeout': 'Session timeout (minutes)',
  'settings.page.session.sessionTimeoutHint': 'Sign the user out automatically after inactivity',
  'settings.page.session.timeout.minutes30': '30 minutes',
  'settings.page.session.timeout.hours1': '1 hour',
  'settings.page.session.timeout.hours2': '2 hours',
  'settings.page.session.timeout.hours4': '4 hours',
  'settings.page.session.timeout.hours8': '8 hours',
  'settings.page.session.timeout.hours24': '24 hours',
  'settings.page.session.timeout.days7': '7 days',
  'settings.page.session.allowRegistration': 'Allow user sign-up',
  'settings.page.session.allowRegistrationDescription':
    'Let visitors create an account from the sign-up page (requires admin approval)',

  // ── Notifications: email notifications ──
  'settings.page.notification.title': 'Email notifications',
  'settings.page.notification.description': 'Choose which events send email notifications to agents',
  'settings.page.notification.newConversation': 'New conversation notification',
  'settings.page.notification.newConversationDescription': 'Notify when a new customer conversation is created',
  'settings.page.notification.customerReply': 'Customer reply notification',
  'settings.page.notification.customerReplyDescription':
    'Notify when a customer replies to an assigned conversation',
  'settings.page.notification.newReply': 'New reply notification',
  'settings.page.notification.newReplyDescription':
    'Notify when another agent replies to a conversation you take part in',
  'settings.page.notification.mention': '@-mention notification',
  'settings.page.notification.mentionDescription': 'Notify when you are @-mentioned in an internal note',
  'settings.page.notification.assignment': 'Conversation assignment notification',
  'settings.page.notification.assignmentDescription': 'Notify when a new conversation is assigned to you',

  // ── Conversations: defaults ──
  'settings.page.conversation.title': 'Conversation defaults',
  'settings.page.conversation.description': 'Configure the default status and display of conversations',
  'settings.page.conversation.defaultStatus': 'Default status for new conversations',
  'settings.page.conversation.defaultStatus.active': 'Pending',
  'settings.page.conversation.defaultStatus.pending': 'Awaiting reply',
  'settings.page.conversation.defaultStatus.open': 'In progress',
  'settings.page.conversation.autoClose': 'Auto-close time (hours)',
  'settings.page.conversation.autoCloseHint':
    'Close automatically when no action is taken within this time after a customer reply',
  'settings.page.conversation.autoClose.hours24': '24 hours',
  'settings.page.conversation.autoClose.days2': '2 days',
  'settings.page.conversation.autoClose.days3': '3 days',
  'settings.page.conversation.autoClose.days5': '5 days',
  'settings.page.conversation.autoClose.days7': '7 days',
  'settings.page.conversation.autoClose.days14': '14 days',
  'settings.page.conversation.autoClose.days30': '30 days',
  'settings.page.conversation.autoClose.never': 'Never close automatically',
  'settings.page.conversation.threadTime': 'Conversation time display',
  'settings.page.conversation.threadTime.absolute': 'Exact time (2024-01-15 10:30)',
  'settings.page.conversation.threadTime.relative': 'Relative time (2 hours ago)',
  'settings.page.conversation.threadTime.both': 'Show both',
  'settings.page.conversation.signaturePosition': 'Signature position',
  'settings.page.conversation.signaturePosition.beforeQuote': 'Before the quoted content',
  'settings.page.conversation.signaturePosition.afterQuote': 'After the quoted content',
  'settings.page.conversation.signaturePosition.bottom': 'At the bottom of the email',
  'settings.page.conversation.allowDelete': 'Allow conversation deletion',
  'settings.page.conversation.allowDeleteDescription':
    'Admins can delete a conversation and all of its replies',

  // ── Advanced: notice, filter, table ──
  'settings.page.advanced.warning':
    'Manage every system option directly. Incorrect changes may break the system, so proceed with care.',
  'settings.page.advanced.filterCategory': 'Filter by category',
  'settings.page.advanced.allCategories': 'All categories',
  'settings.page.advanced.create': 'New option',
  'settings.page.advanced.empty': 'No options yet',
  'settings.page.advanced.col.name': 'Name',
  'settings.page.advanced.col.value': 'Value',
  'settings.page.advanced.col.category': 'Category',
  'settings.page.advanced.col.updatedAt': 'Updated at',
  'settings.page.advanced.col.actions': 'Actions',
  'settings.page.advanced.emptyValue': '(empty)',
  'settings.page.advanced.updatedAtPrefix': 'Updated',

  // ── Advanced: create / edit dialog ──
  'settings.page.advanced.editTitle': 'Edit option',
  'settings.page.advanced.createTitle': 'New option',
  'settings.page.advanced.nameRequired': 'The option name is required',
  'settings.page.advanced.field.name': 'Option name',
  'settings.page.advanced.field.namePlaceholder': 'For example: app.site_name',
  'settings.page.advanced.field.nameImmutable': 'The option name cannot be changed',
  'settings.page.advanced.field.value': 'Option value',
  'settings.page.advanced.field.valuePlaceholder': 'Option value',
  'settings.page.advanced.field.category': 'Category',
  'settings.page.advanced.field.optional': '(optional)',
  'settings.page.advanced.field.categoryPlaceholder': 'For example: general, email, security',
  'settings.page.advanced.deleteTitle': 'Delete option',
  'settings.page.advanced.deleteConfirm': 'Delete the option "{name}"? This action cannot be undone.',
  'settings.page.advanced.saveSettings': 'Save settings',

  // ── Mail tests: mailbox list and connection test ──
  'settings.page.mail.mailboxList': 'Mailboxes',
  'settings.page.mail.mailboxListHint': 'Select a mailbox to test',
  'settings.page.mail.noMailbox': 'No mailboxes configured',
  'settings.page.mail.testConnection': 'Test connection',
  'settings.page.mail.connectionOk': 'Connection OK',
  'settings.page.mail.connectionFailed': 'Connection failed',
  'settings.page.mail.selectMailbox': 'Select a mailbox',
  'settings.page.mail.selectMailboxRequired': 'Please select a mailbox',
  'settings.page.mail.pleaseSelect': 'Please select',

  // ── Mail tests: fetch test ──
  'settings.page.mail.fetchTestTitle': 'Mail fetch test',
  'settings.page.mail.fetchTestDescription':
    'Fetch new mail from the mail server manually; customers and conversations are created automatically.',
  'settings.page.mail.fetching': 'Fetching...',
  'settings.page.mail.fetchNow': 'Fetch now',
  'settings.page.mail.fetchSuccess': { one: 'Fetched {count} message', other: 'Fetched {count} messages' },
  'settings.page.mail.fetchFailed': 'Failed to fetch',

  // ── Mail tests: send test ──
  'settings.page.mail.sendTestTitle': 'Mail send test',
  'settings.page.mail.sendTestDescription':
    'Send a test email through the selected mailbox to verify the SMTP settings.',
  'settings.page.mail.fromMailbox': 'From mailbox',
  'settings.page.mail.recipient': 'Recipient',
  'settings.page.mail.subject': 'Subject',
  'settings.page.mail.subjectPlaceholder': 'Email subject',
  'settings.page.mail.body': 'Body',
  'settings.page.mail.bodyPlaceholder': 'HTML is supported',
  'settings.page.mail.sending': 'Sending...',
  'settings.page.mail.sendMail': 'Send email',
  'settings.page.mail.fillAllFields': 'Fill in all email fields',
  'settings.page.mail.sendFailed': 'Failed to send',

  // ── System info: overview cards and info list ──
  'settings.page.info.environment': 'Runtime environment',
  'settings.page.info.database': 'Database',
  'settings.page.info.currentLanguage': 'Current language',
  'settings.page.info.startupTime': 'Startup time',
  'settings.page.info.backendVersion': 'Backend version',
  'settings.page.info.frontendVersion': 'Frontend version',
  'settings.page.info.authMethod': 'Authentication',
  'settings.page.info.passwordHashing': 'Password hashing',
  'settings.page.info.apiDocs': 'API docs',
  'settings.page.info.apiDocsPath': '/docs (same-origin entry)',
  'settings.page.info.localization': 'Language and localization',
  'settings.page.info.supportedLanguages': 'Supported languages',
  'settings.page.info.techStack': 'Tech stack',
  'settings.page.info.architecture': 'Architecture',
  'settings.page.info.arch.ddd': 'DDD layered architecture',
  'settings.page.info.arch.orm': 'ORM framework',
  'settings.page.info.arch.auth': 'Authentication and security',
  'settings.page.info.arch.mail': 'Mail handling',
  'settings.page.info.arch.scheduler': 'Scheduled jobs',
  'settings.page.info.arch.schedulerDescription': 'Quartz.NET every 5 minutes',


  // ── 分区 settings_profile ──

  // ── Anchor navigation (ProfileTab) ──
  // The first values under this prefix are rendered by the ProfileTab component: the
  // render-level key-leak probe derives its landmark strings from the first three
  // literal (non-interpolated) values of the prefix, so they must be ones ProfileTab renders.
  'settings.profile.nav.overview': 'Overview',
  'settings.profile.nav.stats': 'Statistics',
  'settings.profile.nav.basic': 'Basic information',
  'settings.profile.nav.preferences': 'Preferences',
  'settings.profile.nav.password': 'Change password',
  'settings.profile.nav.history': 'Login history',

  // ── Overview card ──
  'settings.profile.avatarAlt': 'Avatar',
  'settings.profile.changeAvatar': 'Change avatar',
  'settings.profile.removeAvatar': 'Remove avatar',
  'settings.profile.uploading': 'Uploading...',
  'settings.profile.processing': 'Processing...',

  // ── Avatar upload / removal feedback ──
  'settings.profile.avatar.invalidType': 'Upload an image file',
  'settings.profile.avatar.tooLarge': 'The image must be 2 MB or smaller',
  'settings.profile.avatar.uploadSucceeded': 'Avatar updated',
  'settings.profile.avatar.uploadFailed': 'Avatar upload failed',
  'settings.profile.avatar.removeSucceeded': 'Avatar removed',
  'settings.profile.avatar.removeFailed': 'Avatar removal failed',

  // ── Statistics cards ──
  'settings.profile.stats.conversations': 'Conversations',
  'settings.profile.stats.replies': 'Replies',
  'settings.profile.stats.onlineDuration': 'Online time',
  'settings.profile.stats.registeredAt': 'Registered',

  // ── Online time (value computed in code, unit follows the language) ──
  'settings.profile.duration.zero': '0 min',
  'settings.profile.duration.minutes': '{minutes} min',
  'settings.profile.duration.hours': '{hours} hr',
  'settings.profile.duration.hoursMinutes': '{hours} hr {minutes} min',
  'settings.profile.duration.days': '{days} days',
  'settings.profile.duration.daysHours': '{days} days {hours} hr',

  // ── Basic information ──
  'settings.profile.basic.title': 'Basic information',
  'settings.profile.basic.description': 'Manage your personal information',
  'settings.profile.basic.firstName': 'First name',
  'settings.profile.basic.firstNamePlaceholder': 'Enter first name',
  'settings.profile.basic.lastName': 'Last name',
  'settings.profile.basic.lastNamePlaceholder': 'Enter last name',
  'settings.profile.basic.email': 'Email',
  'settings.profile.basic.role': 'Role',
  'settings.profile.basic.registeredAt': 'Registered',
  'settings.profile.basic.save': 'Save profile',
  'settings.profile.basic.saving': 'Saving...',
  'settings.profile.basic.saveSucceeded': 'Profile saved',
  'settings.profile.basic.saveFailed': 'Save failed',

  // ── Preferences card ──
  'settings.profile.preferences.title': 'Preferences',
  'settings.profile.preferences.description': 'Customize your experience',
  'settings.profile.preferences.defaultMailbox': 'Default mailbox',
  'settings.profile.preferences.noMailbox': 'Not specified',
  'settings.profile.preferences.pageSize': 'Rows per page',
  'settings.profile.preferences.pageSizeOption': '{count} per page',
  'settings.profile.preferences.timezone': 'Time zone',
  'settings.profile.preferences.language': 'Language',
  'settings.profile.preferences.theme': 'Theme',
  'settings.profile.preferences.themeLight': 'Light',
  'settings.profile.preferences.themeDark': 'Dark',
  'settings.profile.preferences.themeSystem': 'System',
  'settings.profile.preferences.autoRefreshInterval': 'Auto-refresh interval',
  'settings.profile.preferences.refreshNever': 'Never',
  'settings.profile.preferences.refreshSeconds': '{seconds} sec',
  'settings.profile.preferences.refreshMinutes': '{minutes} min',
  'settings.profile.preferences.notificationSettings': 'Notification settings',
  'settings.profile.preferences.emailNotifications': 'Email notifications',
  'settings.profile.preferences.emailNotificationsDesc': 'Send an email when a new conversation starts',
  'settings.profile.preferences.desktopNotifications': 'Desktop notifications',
  'settings.profile.preferences.desktopNotificationsDesc': 'Show browser desktop notifications',
  'settings.profile.preferences.soundEnabled': 'Sound alerts',
  'settings.profile.preferences.soundEnabledDesc': 'Play a sound for new messages',
  'settings.profile.preferences.save': 'Save preferences',
  'settings.profile.preferences.saveSucceeded': 'Preferences saved',

  // ── Change password card ──
  'settings.profile.password.title': 'Change password',
  'settings.profile.password.description': 'Change your password regularly to protect your account',
  'settings.profile.password.current': 'Current password',
  'settings.profile.password.currentPlaceholder': 'Enter current password',
  'settings.profile.password.new': 'New password',
  'settings.profile.password.newPlaceholder': 'At least {count} characters',
  'settings.profile.password.confirm': 'Confirm new password',
  'settings.profile.password.confirmPlaceholder': 'Enter the new password again',
  'settings.profile.password.submit': 'Change password',
  'settings.profile.password.submitting': 'Changing...',
  'settings.profile.password.incomplete': 'Fill in all password fields',
  'settings.profile.password.mismatch': 'The two new passwords do not match',
  'settings.profile.password.success': 'Password changed. Sign in again.',
  'settings.profile.password.failed': 'Password change failed',
  // Joins several password policy messages when validation fails
  'settings.profile.password.errorSeparator': '; ',

  // ── Login history ──
  'settings.profile.history.title': 'Login history',
  'settings.profile.history.description': 'Your last 5 sign-ins. If anything looks wrong, change your password.',
  'settings.profile.history.online': 'Online now',
  'settings.profile.history.empty': 'No sign-in records',
  'settings.profile.history.emptyHint': 'Your login history appears here after you sign in',
  'settings.profile.history.currentSession': 'Current session',
  'settings.profile.history.entryNumber': 'Login history #{index}',
  'settings.profile.history.active': 'Active',
  'settings.profile.history.warning': 'If you notice an unexpected sign-in, change your password and contact an administrator',

  // ── Profile page (pages/Profile.tsx) ──
  // Rendered by the `pages/Profile.tsx` page shell (not ProfileTab), so it sits at the end.
  'settings.profile.pageTitle': 'Profile',
  'settings.profile.pageDescription': 'Manage your profile, preferences, account security, and login history',

  // ── Preferences component (PreferencesSettings.tsx) ──
  'settings.preferences.appearance': 'Appearance',
  'settings.preferences.darkMode': 'Dark mode',
  'settings.preferences.darkModeDesc': 'Switch between the dark and light themes',
  'settings.preferences.notifications': 'Notifications',
  'settings.preferences.emailNotifications': 'Email notifications',
  'settings.preferences.emailNotificationsDesc': 'Receive system email notifications',
  'settings.preferences.browserNotifications': 'Browser notifications',
  'settings.preferences.browserNotificationsDesc': 'Receive browser push notifications',
  'settings.preferences.soundEnabled': 'Sound alerts',
  'settings.preferences.soundEnabledDesc': 'Receive sound notifications',
  'settings.preferences.session': 'Conversation settings',
  'settings.preferences.sessionAutoClose': 'Auto-close conversation',
  'settings.preferences.autoRefresh': 'Auto-refresh',
  'settings.preferences.workdays': 'Working days',
  'settings.preferences.selectAll': 'Select all',
  'settings.preferences.clearAll': 'Clear',

  // Auto-close conversation: dialog option labels
  'settings.preferences.autoCloseOption.never': 'Never auto-close',
  'settings.preferences.autoCloseOption.minutes5': '5 minutes',
  'settings.preferences.autoCloseOption.minutes15': '15 minutes',
  'settings.preferences.autoCloseOption.minutes30': '30 minutes',
  'settings.preferences.autoCloseOption.hour1': '1 hour',
  // Auto-close conversation: current value
  'settings.preferences.autoCloseValue.never': 'Never',
  'settings.preferences.autoCloseValue.minutes': '{minutes} min',
  'settings.preferences.autoCloseValue.hour': '1 hour',
  'settings.preferences.autoCloseValue.hours': '{hours} hr',

  // Auto-refresh: dialog option labels
  'settings.preferences.refreshOption.never': 'Never',
  'settings.preferences.refreshOption.seconds30': '30 sec',
  'settings.preferences.refreshOption.minutes1': '1 min',
  'settings.preferences.refreshOption.minutes5': '5 min',
  // Auto-refresh: current value
  'settings.preferences.refreshValue.never': 'Never',
  'settings.preferences.refreshValue.seconds': '{seconds} sec',
  'settings.preferences.refreshValue.minutes': '{minutes} min',

  // Working days
  'settings.preferences.workday.monday': 'Monday',
  'settings.preferences.workday.tuesday': 'Tuesday',
  'settings.preferences.workday.wednesday': 'Wednesday',
  'settings.preferences.workday.thursday': 'Thursday',
  'settings.preferences.workday.friday': 'Friday',
  'settings.preferences.workday.saturday': 'Saturday',
  'settings.preferences.workday.sunday': 'Sunday',
  'settings.preferences.workdaysUnset': 'Not set',
  'settings.preferences.workdaysAll': 'All week',
  'settings.preferences.workdaysCount': '{count} days',

  // ── Security component (SecuritySettings.tsx) ──
  'settings.security.warningTitle': 'Password security',
  'settings.security.warningDescription': 'Change your password regularly to protect your account',
  'settings.security.title': 'Change password',
  'settings.security.currentPassword': 'Current password',
  'settings.security.currentPasswordPlaceholder': 'Enter current password',
  'settings.security.newPassword': 'New password',
  'settings.security.newPasswordPlaceholder': 'Enter new password',
  'settings.security.confirmPassword': 'Confirm new password',
  'settings.security.confirmPasswordPlaceholder': 'Enter the new password again',
  'settings.security.submit': 'Change password',
  'settings.security.submitting': 'Changing...',
  'settings.security.currentRequired': 'Enter the current password',
  'settings.security.newRequired': 'Enter the new password',
  'settings.security.mismatch': 'The two new passwords do not match',
  'settings.security.success': 'Password changed',
  'settings.security.failed': 'Password change failed',
  // Joins several password policy messages when validation fails
  'settings.security.errorSeparator': '; ',

  // ── password: policy items and local validation messages ──
  // Produced by `components/PasswordPolicyHint.tsx` (policy items) and
  // `hooks/usePasswordPolicy.ts` (validation messages + fallback policy).
  'settings.password.rule.minLength': 'At least {count} characters',
  'settings.password.rule.uppercase': 'At least one uppercase letter (A-Z)',
  'settings.password.rule.lowercase': 'At least one lowercase letter (a-z)',
  'settings.password.rule.digit': 'At least one digit (0-9)',
  'settings.password.rule.symbol': 'At least one special character (!@#$%^&*...)',
  'settings.password.error.empty': 'Password is required',
  'settings.password.error.minLength': 'Password must be at least {count} characters',
  'settings.password.error.uppercase': 'Password must contain at least one uppercase letter',
  'settings.password.error.lowercase': 'Password must contain at least one lowercase letter',
  'settings.password.error.digit': 'Password must contain at least one digit',
  'settings.password.error.symbol': 'Password must contain at least one special character',
} satisfies Entries;
