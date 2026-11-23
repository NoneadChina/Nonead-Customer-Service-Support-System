import type { Entries } from '../../i18n/types';

/** 跨模块复用的通用文案（英文值）。必须用 `satisfies Entries`，不得写成类型标注 */
export const common = {
  'common.save': 'Save',
  'common.cancel': 'Cancel',
  'common.delete': 'Delete',
  'common.edit': 'Edit',
  // Import button on the translation management page: there was no generic import key
  // (`mail.templates.variableCategory.import` is a variable-category label).
  // "Export" on that page reuses the existing `data.common.export`.
  'common.import': 'Import',
  'common.confirm': 'Confirm',
  'common.close': 'Close',
  'common.confirmAction': 'Confirm action',
  'common.processing': 'Processing...',
  'common.confirmHint': 'Please confirm the following action',
  'common.closeDialog': 'Close dialog',
  'common.search': 'Search',
  'common.reset': 'Reset',
  'common.retry': 'Retry',
  'common.loading': 'Loading…',
  'common.empty': 'No data',
  // Failure toasts raised by `useCrudPage` (shared by list pages and their trash panels)
  'common.loadListFailed': 'Failed to load the list',
  'common.loadTrashFailed': 'Failed to load the trash',
  'common.language': 'Language',
  'common.switchLanguage': 'Switch language',
  'common.saveSucceeded': 'Saved',
  'common.savedButLanguageSwitchFailed': 'Saved, but the interface language could not be switched (keeping the current language)',
  'common.languageSwitchFailedSuffix': '; however the interface language could not be switched, so the current language is kept',

  // ── App shell: route placeholders and 404 (App.tsx) ──
  'common.shell.loading': 'Loading...',
  'common.shell.notFoundTitle': 'Page not found',
  'common.shell.notFoundDescription': 'Sorry, the page you requested does not exist or has been moved.',
  'common.shell.backToHome': 'Back to home',

  // ── App shell: sidebar / top bar / user menu (Layout.tsx) ──
  'common.layout.adminConsole': 'Admin console',
  'common.layout.apiDocs': 'API docs',
  'common.layout.openMenu': 'Open menu',
  'common.layout.searchPlaceholder': 'Search...',
  'common.layout.userMenu': 'User menu',
  'common.layout.unknownUser': 'Unknown user',
  'common.layout.profile': 'Profile',
  'common.layout.notificationCenter': 'Notification center',
  'common.layout.emailSignatures': 'Email signatures',
  'common.layout.logout': 'Sign out',
  'common.layout.colleagueFallback': 'A colleague',
  'common.layout.mentionWithPreview': '{name} mentioned you in a chat: {preview}',
  'common.layout.mentionInChat': '{name} mentioned you in a chat',
  'common.layout.mentionedYou': '{name} mentioned you',

  // ── App shell: document title / 403 / version update / runtime exception ──
  //    Forbidden.tsx · ErrorBoundary.tsx (class component, uses `tBound`) ·
  //    ModuleProtectedRoute.tsx (reuses `common.shell.loading`)
  //    `documentTitle` is mirrored to `document.title` by I18nProvider; the static
  //    title in `index.html` is only the pre-JS fallback.
  'common.shell.documentTitle': 'Nonead Customer Service & Support System',
  'common.shell.forbiddenTitle': 'Access denied',
  'common.shell.forbiddenDescription': 'You do not have permission to view this page. Ask an administrator to grant you access.',
  'common.shell.updateDetected': 'A new version is available',
  'common.shell.updateRefreshing': 'Refreshing the page to load the latest version…',
  // The "please + link" sentence is split by a <button>; both languages keep the same order
  'common.shell.updateManualPrefix': 'If the page does not refresh automatically, ',
  'common.shell.updateManualAction': 'click here to refresh manually',
  'common.shell.errorTitle': 'Something went wrong',
  'common.shell.errorDescription': 'The app hit an unexpected error. Try "Retry" first; if the problem persists, reload the page or contact an administrator.',
  'common.shell.errorRetry': 'Retry (no reload)',
  'common.shell.errorReload': 'Reload',

  // ── App shell: global search (GlobalSearch.tsx) ──
  'common.globalSearch.dialogLabel': 'Global search',
  'common.globalSearch.placeholder': 'Search conversations, customers, knowledge base...',
  'common.globalSearch.inputLabel': 'Search conversations, customers, knowledge base',
  'common.globalSearch.type.conversation': 'Conversations',
  'common.globalSearch.type.customer': 'Customers',
  'common.globalSearch.type.knowledge': 'Knowledge base',
  'common.globalSearch.noSubject': '(no subject)',
  'common.globalSearch.unnamedCustomer': '(unnamed)',
  'common.globalSearch.hint': 'Type a keyword to start searching',
  'common.globalSearch.hintSub': 'Searches conversation subjects, customer names and e-mail addresses, and knowledge base articles',
  'common.globalSearch.minChars': 'Type at least 2 characters',
  'common.globalSearch.searching': 'Searching...',
  'common.globalSearch.noResults': 'No results for "{query}"',
  'common.globalSearch.navigateHint': 'navigate',
  'common.globalSearch.selectHint': 'select',
  'common.globalSearch.resultCount': { one: '{count} result', other: '{count} results' },

  // ── Shared: accessibility labels and request-failure messages ──
  //    `api/client.ts` is not a component module, so it resolves these with `tBound`
  'common.closeNotification': 'Close notification',
  'common.requestFailed': 'Request failed',
  'common.sessionExpiredRequestCancelled': 'Your session has expired; the request was cancelled',
  'common.forbidden403': 'You do not have permission to do this. Contact an administrator (403).',
  'common.readOnlyTooltip': 'Read-only: your role cannot change this configuration',
  'common.superAdminOnlyTooltip': 'Super admin only: test, save and subscribe require the super administrator role',
} satisfies Entries;
