import type { Entries } from '../../i18n/types';

/**
 * Conversation feature copy (English values).
 * Must use `satisfies Entries`, never a type annotation.
 *
 * Status / priority / source type / role / timezone labels are **not** here: the `format`
 * module owns them (`format.status.*`, `format.priority.*`, `format.sourceType.*`,
 * `format.role.*`, `format.timezone.*`).
 *
 * **Prefix/suffix split — the interpolated value must keep its own `<span>`.** When the pre-i18n
 * markup wrapped the *value* in its own element (`font-mono` timestamps, `font-semibold` counts),
 * the sentence must not become a single `{x}` entry: the value would lose that styling (revision
 * log row 39, handled as one finish-pass sweep). Split into `…Prefix` / `…Suffix` (one pair per
 * position for multi-value sentences) and put the value back into its `<span>` in JSX. The sentence
 * no longer contains `{count}`, so **the English suffix carries the plural via `{ one, other }`**
 * while the caller still passes `count`.
 * ⚠ `locales.test.ts` rejects empty entries, so every suffix needs real text. Where Chinese has a
 * trailing particle but English naturally ends with the value, the English suffix is a sentence-final
 * period — no invented information and no change to the Chinese copy.
 */
export const conversations = {
  // ── Shared across files ──
  'conversations.shared.noSubject': '(No subject)',
  'conversations.shared.wechatCustomer': 'WeChat customer',

  // =====================================================================
  // Conversation list (pages/Conversations.tsx)
  // =====================================================================
  'conversations.list.title': 'Conversations',
  'conversations.list.subtitle': 'Manage all customer support conversations',
  'conversations.list.refresh': 'Refresh',
  'conversations.list.trash': 'Trash',
  'conversations.list.loading': 'Loading...',
  'conversations.list.loadFailed': 'Failed to load: {message}',

  // ── Quick source filters and filter bar ──
  'conversations.list.all': 'All',
  'conversations.list.search': 'Search',
  'conversations.list.searchPlaceholder': 'Search subject, customer email...',
  'conversations.list.clearSearch': 'Clear search',
  'conversations.list.mailbox': 'Mailbox',
  'conversations.list.allMailboxes': 'All mailboxes',
  'conversations.list.status': 'Status',
  'conversations.list.allStatuses': 'All statuses',
  'conversations.list.priority': 'Priority',
  'conversations.list.allPriorities': 'All priorities',
  'conversations.list.source': 'Source',
  'conversations.list.allSources': 'All sources',
  'conversations.list.follow': 'Following',
  'conversations.list.allConversations': 'All conversations',
  'conversations.list.onlyFollowed': 'Only ones I follow',
  'conversations.list.filters': 'Filters',
  'conversations.list.resultCountPrefix': 'Found ',
  'conversations.list.resultCountSuffix': { one: ' matching result', other: ' matching results' },

  // ── Empty states ──
  'conversations.list.emptySourceTitle': 'No {source} conversations yet',
  'conversations.list.emptySourceHint': 'Messages customers send via {source} appear here',
  'conversations.list.emptySearch': 'No matching conversations',
  'conversations.list.emptySearchHint': 'Try adjusting your search or filters',
  'conversations.list.empty': 'No conversations yet',
  'conversations.list.emptyHint': 'New customer conversations will appear here',

  // ── List item badges ──
  'conversations.list.new': 'New',
  'conversations.list.newMessage': 'New message',
  'conversations.list.hasNewMessage': 'Has a new message',

  // ── Table headers ──
  'conversations.list.columnType': 'Type',
  'conversations.list.columnSubject': 'Subject',
  'conversations.list.columnCustomer': 'Customer',
  'conversations.list.columnAssignee': 'Assignee',
  'conversations.list.columnAttachments': 'Attachments',
  'conversations.list.columnLastReply': 'Last reply',
  'conversations.list.columnCreatedAt': 'Created',

  // ── Pagination ──
  // Page summary: the first part stays in the `hidden sm:inline` wrapper, the page numbers are
  // outside it (mobile showed the page indicator before wave 3) - the split mirrors that structure.
  'conversations.list.pageSummaryPrefix': 'Total ',
  'conversations.list.pageSummaryAfterTotal': { one: ' item, ', other: ' items, ' },
  'conversations.list.pageSummaryBeforePage': 'page ',
  'conversations.list.pageSummaryBetween': ' of ',
  'conversations.list.pageSummarySuffix': { one: ' page', other: ' pages' },

  // ── Bulk action toolbar ──
  'conversations.list.selectedCount': '{count} conversations selected',
  'conversations.list.assignAssignee': 'Assign assignee',
  'conversations.list.updateStatus': 'Update status',
  'conversations.list.setPriority': 'Set priority',
  'conversations.list.addTags': 'Add tags',
  'conversations.list.clearSelection': 'Clear selection',

  // ── Bulk action results and errors ──
  'conversations.list.errorSelectAssignee': 'Select an assignee',
  'conversations.list.assignSuccess': {
    one: 'Assigned {count} conversation',
    other: 'Assigned {count} conversations',
  },
  'conversations.list.assignFailed': 'Assignment failed',
  'conversations.list.statusUpdateSuccess': {
    one: 'Updated the status of {count} conversation',
    other: 'Updated the status of {count} conversations',
  },
  'conversations.list.statusUpdateFailed': 'Failed to update status',
  'conversations.list.priorityUpdateSuccess': {
    one: 'Updated the priority of {count} conversation',
    other: 'Updated the priority of {count} conversations',
  },
  'conversations.list.priorityUpdateFailed': 'Failed to update priority',
  'conversations.list.deleteSuccess': {
    one: 'Deleted {count} conversation. You can restore it from the trash.',
    other: 'Deleted {count} conversations. You can restore them from the trash.',
  },
  'conversations.list.deleteFailed': 'Delete failed',
  'conversations.list.deleteFailedAll': {
    one: 'Delete failed for {count} conversation',
    other: 'Delete failed for {count} conversations',
  },
  'conversations.list.deletePartialSuccess':
    'Partially deleted: {success} succeeded, {failed} failed, out of {total}',
  'conversations.list.withFailureDetails': '{message} (reasons: {details})',
  'conversations.list.restoreSuccess': 'Conversation #{id} restored',
  'conversations.list.restoreFailed': 'Restore failed',
  'conversations.list.hardDeleteSuccess': 'Conversation #{id} permanently deleted',
  'conversations.list.hardDeleteFailed': 'Permanent delete failed',
  'conversations.list.clearTrashSuccess': {
    one: 'Trash emptied. {count} conversation permanently deleted.',
    other: 'Trash emptied. {count} conversations permanently deleted.',
  },
  'conversations.list.clearTrashFailed': 'Failed to empty the trash',
  'conversations.list.errorSelectTag': 'Select at least one tag',
  'conversations.list.tagUpdateSuccess': 'Tags updated',
  'conversations.list.tagAddSuccess': {
    one: 'Added tags to {count} conversation',
    other: 'Added tags to {count} conversations',
  },
  'conversations.list.tagUpdateFailed': 'Failed to update tags',

  // ── Bulk action dialogs ──
  'conversations.list.assigning': 'Assigning...',
  'conversations.list.confirmAssign': 'Assign',
  'conversations.list.assignModalTitle': {
    one: 'Assign assignee ({count} conversation)',
    other: 'Assign assignee ({count} conversations)',
  },
  'conversations.list.assignModalHintPrefix': 'Assign the selected ',
  'conversations.list.assignModalHintSuffix': {
    one: ' conversation to:',
    other: ' conversations to:',
  },
  'conversations.list.selectUser': 'Select a user',
  'conversations.list.updating': 'Updating...',
  'conversations.list.confirmUpdate': 'Update',
  'conversations.list.statusModalTitle': {
    one: 'Update status ({count} conversation)',
    other: 'Update status ({count} conversations)',
  },
  'conversations.list.statusModalHintPrefix': 'Set the status of the selected ',
  'conversations.list.statusModalHintSuffix': {
    one: ' conversation to:',
    other: ' conversations to:',
  },
  'conversations.list.priorityModalTitle': {
    one: 'Set priority ({count} conversation)',
    other: 'Set priority ({count} conversations)',
  },
  'conversations.list.priorityModalHintPrefix': 'Set the priority of the selected ',
  'conversations.list.priorityModalHintSuffix': {
    one: ' conversation to:',
    other: ' conversations to:',
  },
  'conversations.list.tagModalTitleSingle': 'Manage tags (1 conversation)',
  'conversations.list.tagModalTitle': 'Add tags ({count} conversations)',
  'conversations.list.tagModalHintSingle':
    'Tags already on this conversation are highlighted. Click to add or remove:',
  'conversations.list.tagModalHintPrefix': 'Choose tags for the selected ',
  'conversations.list.tagModalHintSuffix': {
    one: ' conversation:',
    other: ' conversations:',
  },
  'conversations.list.noTags': 'No tags yet. Create one first.',
  'conversations.list.saving': 'Saving...',
  'conversations.list.confirmAddTags': 'Add tags',

  // ── Delete / trash confirmations ──
  'conversations.list.bulkDeleteTitle': 'Delete conversations',
  'conversations.list.bulkDeleteMessage':
    'Delete the selected {count} conversations? They move to the trash, where you can restore them or delete them permanently.',
  'conversations.list.confirmDelete': 'Delete',
  'conversations.list.trashTitle': 'Trash - {mailbox}',
  'conversations.list.clearTrash': 'Empty trash',
  'conversations.list.trashEmpty': 'Trash is empty',
  'conversations.list.trashItemMeta': 'Customer: {customer} · Assignee: {assignee}',
  'conversations.list.restore': 'Restore',
  'conversations.list.hardDelete': 'Delete permanently',
  'conversations.list.trashHint':
    'Note: conversations in the trash are soft-deleted. Use "Restore" to return one to the list, or "Delete permanently" to remove it for good (this cannot be undone).',
  'conversations.list.clearTrashMessage':
    'Permanently delete every soft-deleted conversation in this mailbox? {count} in total. This cannot be undone.',
  'conversations.list.confirmClearTrash': 'Empty trash',
  'conversations.list.hardDeleteTitle': 'Delete conversation permanently',
  'conversations.list.hardDeleteMessage':
    'Permanently delete conversation #{id}? This cannot be undone.',
  'conversations.list.confirmHardDelete': 'Delete permanently',

  // =====================================================================
  // Conversation detail (pages/ConversationDetail.tsx)
  // =====================================================================
  'conversations.detail.loading': 'Loading conversation details...',
  'conversations.detail.notFound': 'Conversation not found',
  'conversations.detail.backToList': 'Back to conversations',
  'conversations.detail.breadcrumbList': 'Conversations',
  'conversations.detail.wechatServiceAccount': 'WeChat service account conversation',
  'conversations.detail.customerAvatar': 'Customer avatar',
  'conversations.detail.customerLabel': 'Customer: {name}',
  'conversations.detail.assigneeLabel': 'Assignee: {name}',

  // ── Header actions ──
  'conversations.detail.actions': 'Actions',
  'conversations.detail.addNote': 'Add note',
  'conversations.detail.forward': 'Forward',
  'conversations.detail.clone': 'Clone',
  'conversations.detail.print': 'Print',
  'conversations.detail.forwardConversation': 'Forward conversation',
  'conversations.detail.cloneConversation': 'Clone conversation',
  'conversations.detail.printConversation': 'Print conversation',

  // ── Conversation history ──
  'conversations.detail.threads': 'Conversation history',
  'conversations.detail.refresh': 'Refresh',
  'conversations.detail.jumpToLatest': 'Jump to the latest message',
  'conversations.detail.latestMessage': 'Latest message',

  // ── Action feedback ──
  'conversations.detail.followed': 'Now following this conversation',
  'conversations.detail.unfollowed': 'Unfollowed',
  'conversations.detail.operationFailed': 'Operation failed',
  'conversations.detail.statusUpdated': 'Status updated to: {status}',
  'conversations.detail.statusUpdateFailed': 'Failed to update status',
  'conversations.detail.priorityUpdated': 'Priority updated to: {priority}',
  'conversations.detail.priorityUpdateFailed': 'Failed to update priority',
  'conversations.detail.unassigned': 'Assignment removed',
  'conversations.detail.assignedTo': 'Assigned to {name}',
  'conversations.detail.assignFailed': 'Assignment failed',
  'conversations.detail.replyFailed': 'Failed to send the reply',
  'conversations.detail.emptyReply': 'Enter a reply or add an attachment',
  'conversations.detail.draftSaved': 'Draft saved',
  'conversations.detail.draftSaveFailed': 'Failed to save the draft',
  'conversations.detail.addNoteFailed': 'Failed to add the note',
  'conversations.detail.cloneConfirm': 'Clone this conversation? A new conversation with the same subject will be created.',
  'conversations.detail.cloneSuccess': 'Conversation cloned',
  'conversations.detail.cloneFailed': 'Clone failed',

  // ── Channel reply delivery results ──
  'conversations.detail.threadCreateFailedWithReason':
    'The message reached the customer, but the conversation thread record could not be created: {message}',
  'conversations.detail.threadCreateFailed':
    'The message reached the customer, but the conversation thread record could not be created (data may be inconsistent). Refresh the page later to confirm.',
  'conversations.detail.partialSend': 'Some messages were sent: {message}',
  'conversations.detail.wechatSendFailed': 'Failed to send the WeChat reply',
  'conversations.detail.wechatSent': 'WeChat reply sent',
  'conversations.detail.wechatFailed': 'WeChat reply failed',
  'conversations.detail.whatsAppSendFailed': 'Failed to send the WhatsApp reply',
  'conversations.detail.whatsAppSent': 'WhatsApp reply sent',
  'conversations.detail.whatsAppFailed': 'WhatsApp reply failed',
  'conversations.detail.feishuSendFailed': 'Failed to send the Feishu reply',
  'conversations.detail.feishuSent': 'Feishu reply sent',
  'conversations.detail.feishuFailed': 'Feishu reply failed',
  'conversations.detail.dingtalkSendFailed': 'Failed to send the DingTalk reply',
  'conversations.detail.dingtalkSent': 'DingTalk reply sent',
  'conversations.detail.dingtalkFailed': 'DingTalk reply failed',

  // ── Service window remaining time (formatRemaining / dingDingWindowRemainingText) ──
  'conversations.detail.expired': 'Expired',
  'conversations.detail.remainingHoursMinutes': '{hours}h {minutes}m',
  'conversations.detail.remainingHours': '{hours}h',
  'conversations.detail.remainingMinutes': '{minutes}m',
  'conversations.detail.remainingSeconds': '{seconds}s',

  // ── Shared window status fragments ──
  'conversations.detail.windowStatusOk': 'Active',
  'conversations.detail.windowStatusWarning': 'Expiring soon',
  'conversations.detail.remainingLabel': '{remaining} remaining',
  'conversations.detail.lastInteraction': 'Last interaction: ',
  'conversations.detail.lastInteractionShort': 'Interaction: ',
  'conversations.detail.lastInteractionTitle': 'Last interaction: {time}',
  'conversations.detail.goToTemplate': 'Go to template messages',
  'conversations.detail.windowExpiresAtTitle': 'The window expires at {time}',
  'conversations.detail.windowExpiredAtPrefix': 'Expired at ',
  // English ends with the timestamp; the suffix only carries sentence-final punctuation.
  'conversations.detail.windowExpiredAtSuffix': '.',
  'conversations.detail.windowWillExpireAtPrefix': 'Expires at ',
  'conversations.detail.windowWillExpireAtSuffix': '.',

  // ── WeChat 48-hour window ──
  'conversations.detail.wechatWindowOk': 'Service message window is open. {remaining} left.',
  'conversations.detail.wechatWindowWarning': 'Service message window is about to expire. {remaining} left.',
  'conversations.detail.wechatWindowExpired':
    'The service message window has expired. Customer messages may not be delivered; use a template message instead.',
  'conversations.detail.wechatWindowExpiredAdvice': 'Service messages may not be delivered; send a template message instead',
  'conversations.detail.wechatWindowUnknown': 'No customer interaction yet. You can reply once the customer writes in.',
  'conversations.detail.wechatWindowSrText':
    'WeChat service message 48-hour window: {status}, {percent}% used, {remaining}',
  'conversations.detail.wechatWindowProgressAria': '48-hour service message window, {remaining} left',
  'conversations.detail.wechatUsedRemaining': '{used}h used · {remaining} left',
  'conversations.detail.wechatTemplateLinkAria': 'Open WeChat settings to send a template message',
  'conversations.detail.wechatTemplateLinkTitle': 'Open WeChat settings → template messages in a new page',

  // ── WhatsApp 24-hour window ──
  'conversations.detail.whatsAppWindowOk': '✅ 24-hour customer service window',
  'conversations.detail.whatsAppActive': 'Active',
  'conversations.detail.whatsAppWindowWarning': '⚠️ 24-hour window about to expire',
  'conversations.detail.whatsAppReplySoon': 'Reply as soon as possible',
  'conversations.detail.whatsAppWindowExpired': '🔴 24-hour window has expired',
  'conversations.detail.whatsAppTemplateOnly': 'Template messages only',
  'conversations.detail.whatsAppAwaitingCustomer': '📭 Waiting for the customer to write in',
  'conversations.detail.whatsAppOkDesc': 'The window is open: send text and media freely. Time left:',
  'conversations.detail.whatsAppWarningDesc': 'The window is about to close. Reply to the customer soon. Time left:',
  'conversations.detail.whatsAppExpiredDesc':
    'The customer has been inactive for over 24 hours, so free-form messages cannot be delivered. Send an approved template message to reach them again.',
  'conversations.detail.whatsAppUnknownDesc':
    'No customer interaction yet. The 24-hour service window opens once the customer writes in.',
  'conversations.detail.whatsAppWindowSrText':
    '24-hour customer service window: {status}, {percent}% used, {remaining}',
  'conversations.detail.whatsAppFreeMessageBlocked': 'Free-form messages cannot be delivered; send a template message instead',
  'conversations.detail.whatsAppWindowProgressAria': '24-hour customer service window, {remaining} left',
  'conversations.detail.whatsAppRulerStart': '0h · start',
  'conversations.detail.whatsAppRulerEnd': '24h · expiry',
  'conversations.detail.whatsAppStopped': '⏹ Expired',
  'conversations.detail.whatsAppConsumed': '{percent}% used · {remaining} left',
  'conversations.detail.whatsAppWillExpireAtPrefix': 'Expires at ',
  'conversations.detail.whatsAppWillExpireAtSuffix': '.',
  'conversations.detail.whatsAppTemplateLinkAria': 'Open WhatsApp settings to send a template message',
  'conversations.detail.whatsAppTemplateLinkTitle': 'Open WhatsApp settings → template messages',

  // ── Forward conversation dialog ──
  'conversations.detail.forwarding': 'Forwarding...',
  'conversations.detail.confirmForward': 'Forward',
  'conversations.detail.forwardHintPrefix': 'Forward everything in conversation ',
  'conversations.detail.forwardHintSuffix': ' to the address below.',
  'conversations.detail.forwardRecipient': 'Recipient email *',
  'conversations.detail.forwardNoteLabel': 'Note (optional)',
  'conversations.detail.forwardNotePlaceholder': 'Add a short note...',
  'conversations.detail.forwardRecipientRequired': 'Enter the recipient email address',
  'conversations.detail.forwardRecipientInvalid': 'The recipient email address is not valid',
  'conversations.detail.forwardSuccess': 'Forwarded to {email}',
  'conversations.detail.forwardFailed': 'Forward failed',
  'conversations.detail.forwardIncludes': 'The forwarded message includes:',
  'conversations.detail.forwardIncludesMeta': '· Conversation subject, customer email, assignee, creation time',
  'conversations.detail.forwardIncludesMessages':
    '· All {count} messages (customer messages and agent replies, excluding internal notes)',
  'conversations.detail.forwardIncludesSender': '· Sending mailbox: {email}',
  'conversations.detail.defaultMailbox': 'Default mailbox',

  // ── Forwarded email body (outbound email; currently produced in the UI language) ──
  'conversations.detail.forwardBodyGreeting': 'Hello,',
  'conversations.detail.forwardBodyHeading': '— Forwarded conversation —',
  'conversations.detail.forwardBodySubject': 'Subject:',
  'conversations.detail.forwardBodyCustomer': 'Customer:',
  'conversations.detail.forwardBodyAssignee': 'Assignee:',
  'conversations.detail.forwardBodyCreatedAt': 'Created:',
  'conversations.detail.forwardSubjectPrefix': '[Forwarded] {subject}',
  'conversations.detail.threadTypeCustomer': 'Customer message',
  'conversations.detail.threadTypeReply': 'Agent reply',
  'conversations.detail.threadTypeNote': 'Internal note',
  'conversations.detail.threadTypeSystem': 'System message',

  // =====================================================================
  // Shared reply box shell (components/ReplyBox.tsx + the four channel reply boxes)
  // =====================================================================
  'conversations.reply.noteTitle': 'Internal note',
  'conversations.reply.replyTitle': 'Reply to customer',
  'conversations.reply.addNote': 'Add internal note',
  'conversations.reply.readOnlyHint': 'You have read-only access and cannot send replies',
  'conversations.reply.from': 'From: {email}',
  'conversations.reply.quickSend': 'Quick send',

  // ── Rich text toolbar ──
  'conversations.reply.bold': 'Bold',
  'conversations.reply.boldPlaceholder': 'bold text',
  'conversations.reply.italic': 'Italic',
  'conversations.reply.italicPlaceholder': 'italic text',
  'conversations.reply.underline': 'Underline',
  'conversations.reply.underlinePlaceholder': 'underlined text',
  'conversations.reply.bulletList': 'Bulleted list',
  'conversations.reply.numberList': 'Numbered list',
  'conversations.reply.link': 'Link',
  'conversations.reply.linkPlaceholder': 'link text',
  'conversations.reply.heading': 'Heading',
  'conversations.reply.headingPlaceholder': 'Heading',
  'conversations.reply.quote': 'Quote',
  'conversations.reply.quotePlaceholder': 'quoted text',
  'conversations.reply.code': 'Code',
  'conversations.reply.codePlaceholder': 'code',
  'conversations.reply.templates': 'Canned response templates',
  'conversations.reply.htmlSupported': 'HTML is supported',

  // ── CC / BCC ──
  'conversations.reply.cc': 'Cc (CC)',
  'conversations.reply.bcc': 'Bcc (BCC)',
  'conversations.reply.multipleEmails': 'Separate multiple addresses with commas',

  // ── Editor ──
  'conversations.reply.notePlaceholder': 'Write an internal note... (visible to agents only)',
  'conversations.reply.replyPlaceholder': 'Write your reply... (rich HTML supported)',
  'conversations.reply.noteAria': 'Internal note content',
  'conversations.reply.replyAria': 'Reply content',
  'conversations.reply.remove': 'Remove',
  'conversations.reply.removeAttachmentAria': 'Remove attachment {name}',
  'conversations.reply.removeImageAria': 'Remove image attachment {name}',
  'conversations.reply.addAttachment': 'Add attachment',
  'conversations.reply.attachmentHint': 'Images, PDFs, documents and more',

  // ── Attachment validation ──
  'conversations.reply.fileTooLarge25': '{name} exceeds the 25MB size limit',
  'conversations.reply.fileTypeNotAllowed': '{name} has a file type that is not allowed',
  'conversations.reply.uploadFailed': 'Upload failed',

  // ── Footer actions ──
  'conversations.reply.savingDraft': 'Saving...',
  'conversations.reply.saveDraft': 'Save draft',
  'conversations.reply.draftSaveFailed': 'Failed to save the draft',
  'conversations.reply.sending': 'Sending...',
  'conversations.reply.saveNote': 'Save note',
  'conversations.reply.sendReply': 'Send reply',
  'conversations.reply.undoCountdownPrefix': 'The email will be sent in ',
  'conversations.reply.undoCountdownSuffix': { one: ' second', other: ' seconds' },
  'conversations.reply.undo': 'Cancel send',

  // ── Canned response dropdown ──
  'conversations.reply.templateSearch': 'Search templates...',
  'conversations.reply.templatesLoading': 'Loading...',
  'conversations.reply.noTemplates': 'No templates yet',
  'conversations.reply.uncategorized': 'Uncategorized',

  // ── Shared channel reply box shell ──
  'conversations.reply.dropToUpload': 'Release the mouse to upload attachments',
  // 术语表（设计 §10）：快捷回复 = Canned response。本模块早年写成 "Quick replies"，
  // 与波次 6 的 `chat.composer.canned*` 冲突，故按术语表统一。
  'conversations.reply.quickReplies': 'Canned responses',
  'conversations.reply.quickRepliesHint': 'Press 1-{count} to insert quickly',
  'conversations.reply.commonEmojis': 'Common emoji',
  'conversations.reply.insertEmoji': 'Insert emoji',
  'conversations.reply.insertEmojiAria': 'Insert emoji {emoji}',
  'conversations.reply.quick': 'Quick',
  'conversations.reply.attachments': 'Attach',
  'conversations.reply.uploading': 'Uploading',
  'conversations.reply.emoji': 'Emoji',
  'conversations.reply.shortcuts': 'Keyboard shortcuts',
  'conversations.reply.shortcutsAria': 'View keyboard shortcuts',
  'conversations.reply.shortcutSend': 'Send message',
  'conversations.reply.shortcutQuickReplies': 'Canned responses (1-{count})',
  'conversations.reply.shortcutQuickRepliesPlain': 'Canned responses',
  'conversations.reply.shortcutClosePanel': 'Close panel',
  'conversations.reply.shortcutPasteImage': 'Paste an image to upload',
  'conversations.reply.sendingEllipsis': 'Sending…',
  'conversations.reply.send': 'Send',
  'conversations.reply.charCountTitle': 'Characters used / limit',

  // ── Canned response phrases (shared by all four channel reply boxes) ──
  'conversations.reply.quickReply1': 'Hello, how can I help you?',
  'conversations.reply.quickReply2': 'One moment, let me look that up for you…',
  'conversations.reply.quickReply3': 'We have received your message and will handle it as soon as possible.',
  'conversations.reply.quickReply4': 'Thank you for your patience.',
  'conversations.reply.quickReply5': 'The issue is resolved. Is there anything else I can help you with?',
  'conversations.reply.quickReply6': 'I have transferred you to a specialist. Please hold on.',

  // =====================================================================
  // Right sidebar (components/ConversationSidebar.tsx)
  // =====================================================================
  'conversations.sidebar.unknownMailbox': 'Unknown mailbox',
  'conversations.sidebar.actions': 'Actions',
  'conversations.sidebar.follow': 'Follow conversation',
  'conversations.sidebar.unfollow': 'Unfollow',
  'conversations.sidebar.statusAria': 'Conversation status',
  'conversations.sidebar.statusOption': 'Status: {status}',
  'conversations.sidebar.priorityAria': 'Conversation priority',
  'conversations.sidebar.priorityOption': 'Priority: {priority}',
  'conversations.sidebar.assignedTo': 'Assigned to: {name}',
  'conversations.sidebar.assignPlaceholder': 'Assign to...',
  'conversations.sidebar.customerInfo': 'Customer information',
  'conversations.sidebar.wechatChannel': 'WeChat channel',
  'conversations.sidebar.copied': 'Copied to clipboard',
  'conversations.sidebar.copyFailed': 'Copy failed. Copy it manually.',
  'conversations.sidebar.copyOpenIdTitle': 'Click to copy the full OpenID\n{id}',
  'conversations.sidebar.copyOpenIdAriaSuccess': 'OpenID copied to clipboard',
  'conversations.sidebar.copyOpenIdAriaFailed': 'Copy failed. Copy the OpenID manually.',
  'conversations.sidebar.copyOpenIdAria': 'Copy the OpenID; the full value is {id}',
  'conversations.sidebar.joinedAt': 'Joined {date}',
  'conversations.sidebar.conversationCount': {
    one: '{count} past conversation',
    other: '{count} past conversations',
  },
  'conversations.sidebar.unknownCustomer': 'Unknown customer',
  'conversations.sidebar.unregisteredCustomer': 'Unregistered customer',
  'conversations.sidebar.conversationInfo': 'Conversation information',
  'conversations.sidebar.conversationId': 'Conversation ID',
  'conversations.sidebar.status': 'Status',
  'conversations.sidebar.priority': 'Priority',
  'conversations.sidebar.source': 'Source',
  'conversations.sidebar.mailbox': 'Mailbox',
  'conversations.sidebar.assignee': 'Assignee',
  'conversations.sidebar.createdAt': 'Created',
  'conversations.sidebar.lastReply': 'Last reply',
  'conversations.sidebar.tags': 'Tags',
  'conversations.sidebar.emailSatisfaction': 'Email satisfaction rating',
  'conversations.sidebar.ratedAt': 'Rated {date}',
  'conversations.sidebar.chatRating': 'Live chat customer rating',
  'conversations.sidebar.chatToConversation': 'Chat to conversation',
  'conversations.sidebar.sessionUpdatedAt': 'Session updated {date}',
  'conversations.sidebar.ratingStars': '{rating} stars',
  'conversations.sidebar.ratingVerySatisfied': 'Very satisfied',
  'conversations.sidebar.ratingSatisfied': 'Satisfied',
  'conversations.sidebar.ratingNeutral': 'Neutral',
  'conversations.sidebar.ratingDissatisfied': 'Dissatisfied',
  'conversations.sidebar.ratingVeryDissatisfied': 'Very dissatisfied',

  // =====================================================================
  // Channel reply boxes (components/{WeChat,WhatsApp,Feishu,DingDing}ReplyBox.tsx)
  // =====================================================================
  'conversations.channel.goToTemplate': 'Go to template messages',
  'conversations.channel.textAndAttachments': 'Text and attachments are supported',
  'conversations.channel.fileTooLarge': '{name} exceeds the {size} size limit',
  'conversations.channel.remainingSuffix': ' ({remaining} left)',

  // ── WeChat ──
  'conversations.channel.wechat.title': 'WeChat reply',
  'conversations.channel.wechat.windowExpired': 'The service message window has expired',
  'conversations.channel.wechat.windowExpiredDesc':
    'The customer has been inactive for over 48 hours, so service messages cannot be delivered. Reach out with a template message instead.',
  'conversations.channel.wechat.windowWarning':
    'The service message window is about to expire. Reply to the customer soon; after it closes you must use a template message.',
  'conversations.channel.wechat.templateLinkAria': 'Open WeChat settings to send a template message',
  'conversations.channel.wechat.templateLinkTitle': 'Open WeChat settings → template messages',
  'conversations.channel.wechat.uploadTitle': 'Upload image/voice/video (image ≤2MB, voice ≤2MB, video ≤10MB)',
  'conversations.channel.wechat.uploadAria': 'Upload an image, voice or video attachment',
  'conversations.channel.wechat.unsupportedType': '{name} is not a supported type; WeChat accepts images, voice and video only',
  'conversations.channel.wechat.placeholderExpired': 'The service message window has expired; service messages cannot be sent',
  'conversations.channel.wechat.placeholder': 'Write a WeChat reply… (Ctrl+Enter to send)',
  'conversations.channel.wechat.textareaAria': 'WeChat reply content',
  'conversations.channel.wechat.byteCountTitle': 'UTF-8 bytes used / WeChat 600-byte limit',
  'conversations.channel.wechat.footerHint':
    'Service messages can only be sent within 48 hours of the customer’s last interaction; attachments are sent as separate messages.',
  'conversations.channel.wechat.footerHintShort': 'Send within the 48-hour window',
  'conversations.channel.wechat.sendAria': 'Send the WeChat reply',

  // ── WhatsApp ──
  'conversations.channel.whatsApp.title': 'WhatsApp reply',
  'conversations.channel.whatsApp.subtitle': 'Cloud API · 24h service window',
  'conversations.channel.whatsApp.dropHint': 'Images, audio, video, documents and WebP stickers are supported',
  'conversations.channel.whatsApp.windowExpired': 'The 24-hour customer service window has expired',
  'conversations.channel.whatsApp.cannotSendFree': 'Free-form messages are unavailable',
  'conversations.channel.whatsApp.windowExpiredDesc':
    'The customer has been inactive for over 24 hours, so WhatsApp will reject free-form messages. Reach out with a template message instead.',
  'conversations.channel.whatsApp.templateLinkAria': 'Open WhatsApp settings to send a template message',
  'conversations.channel.whatsApp.templateLinkTitle': 'Open WhatsApp settings → template messages',
  'conversations.channel.whatsApp.windowWarning': '⚠ 24-hour window about to expire',
  'conversations.channel.whatsApp.windowWarningDesc':
    'Reply to the customer soon. Once the window closes, only template messages can reach them.',
  'conversations.channel.whatsApp.quickRepliesTitle': 'Canned responses (press a number key to insert)',
  'conversations.channel.whatsApp.quickRepliesHint': 'Number keys 1-{count}',
  'conversations.channel.whatsApp.uploadTitle':
    'Upload image/audio/video/document (image ≤5MB, audio ≤16MB, video ≤16MB, document ≤100MB, sticker ≤500KB)',
  'conversations.channel.whatsApp.uploadAria': 'Upload an image, audio, video or document attachment',
  'conversations.channel.whatsApp.unsupportedType':
    '{name} is not a supported type; WhatsApp accepts images, audio, video, documents and stickers only',
  'conversations.channel.whatsApp.pendingAttachments': 'Attachments to send · {count}',
  'conversations.channel.whatsApp.removeNamed': 'Remove {name}',
  'conversations.channel.whatsApp.placeholderExpired': '🔒 The 24-hour window has expired; free-form messages cannot be sent. Go to template messages.',
  'conversations.channel.whatsApp.placeholder': '💬 Write a WhatsApp reply… (Ctrl + Enter to send)',
  'conversations.channel.whatsApp.textareaAria': 'WhatsApp reply content',
  'conversations.channel.whatsApp.charCountTitle': 'Characters used / limit (4096)',
  'conversations.channel.whatsApp.footerHint':
    'You can reply freely within the 24-hour customer service window; attachments are sent separately by type and may produce several messages.',
  'conversations.channel.whatsApp.footerHintShort': 'Reply within the 24h window; attachments are sent separately',
  'conversations.channel.whatsApp.sendAria': 'Send the WhatsApp reply',
  'conversations.channel.whatsApp.sendNow': 'Send now',
  'conversations.channel.whatsApp.sendReady': 'Ready to send',

  // ── Feishu ──
  'conversations.channel.feishu.title': 'Feishu reply',
  'conversations.channel.feishu.uploadTitle': 'Upload image/voice/video (≤25MB)',
  'conversations.channel.feishu.uploadAria': 'Upload an image, voice or video attachment',
  'conversations.channel.feishu.unsupportedType': '{name} is not a supported type; Feishu accepts images, voice and video only',
  'conversations.channel.feishu.fileTooLarge': '{name} exceeds the 25MB size limit',
  'conversations.channel.feishu.footerHint':
    'The Feishu bot can send messages once the user has started a conversation; attachments are sent as separate messages.',
  'conversations.channel.feishu.placeholder': 'Write a Feishu reply… (Ctrl+Enter to send)',
  'conversations.channel.feishu.textareaAria': 'Feishu reply content',
  'conversations.channel.feishu.sendAria': 'Send the Feishu reply',

  // ── DingTalk ──
  'conversations.channel.dingtalk.title': 'DingTalk reply',
  'conversations.channel.dingtalk.windowOk': 'sessionWebhook available',
  'conversations.channel.dingtalk.windowWarning': 'Expiring soon',
  'conversations.channel.dingtalk.windowExpired':
    'sessionWebhook expired; replies go through the bulk send API',
  'conversations.channel.dingtalk.windowUnknown': 'sessionWebhook status unknown',
  'conversations.channel.dingtalk.uploadTitle': 'Upload image/voice/video (≤25MB)',
  'conversations.channel.dingtalk.uploadAria': 'Upload an image, voice or video attachment',
  'conversations.channel.dingtalk.unsupportedType':
    '{name} is not a supported type; DingTalk accepts images, voice and video only',
  'conversations.channel.dingtalk.fileTooLarge': '{name} exceeds the 25MB size limit',
  'conversations.channel.dingtalk.footerHint':
    'The DingTalk bot replies instantly through sessionWebhook (72h window); after the window closes, replies go through the bulk send API.',
  'conversations.channel.dingtalk.placeholder': 'Write a DingTalk reply… (Ctrl+Enter to send)',
  'conversations.channel.dingtalk.textareaAria': 'DingTalk reply content',
  'conversations.channel.dingtalk.sendAria': 'Send the DingTalk reply',

  'conversations.channel.line.title': 'LINE reply (reply first, push after the window lapses)',
  'conversations.channel.line.windowOk': 'Inside reply window',
  'conversations.channel.line.windowExpiring': 'Window about to expire',
  'conversations.channel.line.windowExpired': 'Window expired',
  'conversations.channel.line.windowUnknown': 'No reply window',
  'conversations.channel.line.windowTitle': 'Replies within {ttl}s of the last customer message use the free one-shot replyToken; afterwards push is used and consumes monthly quota',
  'conversations.channel.line.takeoverActive': 'Human handover active: the bot will not auto-reply',
  'conversations.channel.line.takeoverActiveWithRemaining': 'Human handover active ({remaining} left): the bot will not auto-reply',
  'conversations.channel.line.takeoverStart': 'Start handover',
  'conversations.channel.line.takeoverRelease': 'End handover',
  'conversations.channel.line.takeoverFailed': 'Failed to update the handover state',
  'conversations.channel.line.unreachableHint': 'This customer has not added the bot as a friend (or blocked it), so messages cannot be delivered',
  'conversations.channel.line.fileTooLarge': 'Attachment “{name}” exceeds the LINE direct-link limit ({limit})',
  'conversations.channel.line.uploadTitle': 'LINE attachments are sent as signed public HTTPS links from this system (AttachmentPublicBaseUrl must be set)',
  'conversations.channel.line.uploadAria': 'Upload LINE attachment',
  'conversations.channel.line.splitInto': 'Will be sent as {count} messages',
  'conversations.channel.line.placeholder': 'Type the reply to this LINE customer…',
  'conversations.channel.line.textareaAria': 'LINE reply content',
  'conversations.channel.line.attachmentLinkHint': '{count} attachment(s) sent as signed links ({nonNative} are not natively displayed image formats and go as files)',
  'conversations.channel.line.tooManyMessages': 'At most 5 messages per call: this would be {count}, shorten the text or drop attachments',
  'conversations.channel.line.footerHint': '5000 chars per message · image direct links · push after expiry consumes quota',
  'conversations.channel.line.sendAria': 'Send LINE reply',
  'conversations.channel.messenger.title': 'Messenger',
  'conversations.channel.messenger.subtitle': 'Send inside the window; a message_tag is added automatically outside it',
  'conversations.channel.messenger.placeholder': 'Type the Messenger message for this customer',
  'conversations.channel.messenger.placeholderExpired': 'Reply window expired — a message_tag will be attached',
  'conversations.channel.messenger.textareaAria': 'Messenger reply text',
  'conversations.channel.messenger.charCountTitle': 'Characters (longer than the chunk length is split automatically)',
  'conversations.channel.messenger.chunkHint': 'Will send as {count} messages (≤ {chars} characters each)',
  'conversations.channel.messenger.dropHint': 'Drop images, audio, video or files here to upload',
  'conversations.channel.messenger.unsupportedType': 'Unsupported file type: {name}',
  'conversations.channel.messenger.uploadTitle': 'Upload attachment',
  'conversations.channel.messenger.uploadAria': 'Upload Messenger attachment',
  'conversations.channel.messenger.pendingAttachments': '{count} attachment(s) queued',
  'conversations.channel.messenger.removeNamed': 'Remove {name}',
  'conversations.channel.messenger.windowExpired': 'The 24-hour reply window has closed',
  'conversations.channel.messenger.windowExpiredDesc': 'Meta requires a message_tag for agent replies outside the window; it will be attached',
  'conversations.channel.messenger.tagApplied': 'HUMAN_AGENT tag will be applied',
  'conversations.channel.messenger.tagNone': 'Tag policy is "never tag" — the send may be rejected',
  'conversations.channel.messenger.windowWarning': 'Reply window is about to expire',
  'conversations.channel.messenger.windowWarningDesc': 'Afterwards outbound replies carry a message_tag and delivery may drop',
  'conversations.channel.messenger.quickRepliesTitle': 'Quick replies',
  'conversations.channel.messenger.quickRepliesHint': 'Press key {count} to insert',
  'conversations.channel.messenger.footerHint': 'Ctrl+Enter sends; attachments go through the Attachment Upload API first',
  'conversations.channel.messenger.footerHintShort': 'Ctrl+Enter sends',
  'conversations.channel.messenger.sendAria': 'Send Messenger message',
  'conversations.channel.messenger.sendNow': 'Send',
  'conversations.channel.messenger.sendReady': 'Ready to send',

  'conversations.channel.telegram.title': 'Telegram',
  'conversations.channel.telegram.unreachableHint': 'This user blocked the bot or has no open chat, so messages cannot be delivered.',
  'conversations.channel.telegram.clearUnreachable': 'Re-check deliverability',
  'conversations.channel.telegram.parseModeAria': 'Message formatting mode',
  'conversations.channel.telegram.parseMode.none': 'Plain text',
  'conversations.channel.telegram.parseMode.HTML': 'HTML',
  'conversations.channel.telegram.parseMode.Markdown': 'Markdown',
  'conversations.channel.telegram.quoteLast': 'Quote the last message',
  'conversations.channel.telegram.uploadTitle': 'Attach a photo, video, document or audio file',
  'conversations.channel.telegram.uploadAria': 'Attach files',
  'conversations.channel.telegram.splitInto': 'splits into {count} messages',
  'conversations.channel.telegram.placeholder': 'Write your reply…',
  'conversations.channel.telegram.placeholderCaption': 'Add a caption for the attachment…',
  'conversations.channel.telegram.textareaAria': 'Telegram reply',
  'conversations.channel.telegram.largeFileHint': 'A bot can only download files up to 20MB itself',
  'conversations.channel.telegram.footerHint': 'Telegram accepts uploads up to 50MB; over-long text is split automatically.',
  'conversations.channel.telegram.sendAria': 'Send reply',
  'conversations.channel.telegram.fileTooLarge': '{name} is larger than {limit}',
  'conversations.channel.telegram.unsupportedType': '{name} is not a file type Telegram accepts',
  'conversations.channel.iMessage.title': 'Apple Messages (iMessage)',
  'conversations.channel.iMessage.subtitle': 'replies leave through your iMessage bridge',
  'conversations.channel.iMessage.ambSessionNotice': 'Messages for Business follows Apple’s reply window: only send inside an active session.',
  'conversations.channel.iMessage.sessionIdle': 'The session has gone idle, so the customer may never see a new reply.',
  'conversations.channel.iMessage.placeholder': 'Write your reply…',
  'conversations.channel.iMessage.textareaAria': 'iMessage reply',
  'conversations.channel.iMessage.removeAttachment': 'Remove attachment',
  'conversations.channel.iMessage.uploadAria': 'Attach files',
  'conversations.channel.iMessage.uploadTitle': 'Attach images, video or documents (up to 100MB each)',
  'conversations.channel.iMessage.quickRepliesTitle': 'Insert a quick reply',
  'conversations.channel.iMessage.sendNow': 'Send',
  'conversations.channel.iMessage.dropHint': 'Drop files here to attach them',
  'conversations.channel.iMessage.footerHint': 'Delivery depends on the bridge being online and the session staying active.',
  'conversations.channel.iMessage.unsupportedType': 'That file type cannot be sent through iMessage',
  'conversations.channel.iMessage.tooManyAttachments': 'Too many attachments for one message',
  'conversations.channel.iMessage.uploadFailed': 'Failed to upload the attachments',


  'conversations.detail.messengerSendFailed': 'Messenger send failed',
  'conversations.detail.messengerSent': 'Messenger message sent',
  'conversations.detail.messengerSentNoThread': 'Message sent, but the thread record failed to save — please verify before resending',
  'conversations.detail.messengerFailed': 'Failed to send Messenger message',
  'conversations.detail.telegramSendFailed': 'Telegram send failed',
  'conversations.detail.telegramSent': 'Telegram message sent',
  'conversations.detail.telegramFailed': 'Failed to send Telegram message',
  'conversations.detail.telegramUnreachableCleared': 'Unreachable flag cleared; the customer can be messaged again',
  'conversations.detail.lineSendFailed': 'LINE send failed',
  'conversations.detail.lineSent': 'LINE message sent',
  'conversations.detail.lineFailed': 'Failed to send LINE message',
  'conversations.detail.lineTakeoverOn': 'Human takeover started; the LINE bot is muted while the token is active',
  'conversations.detail.lineTakeoverOff': 'Human takeover released; the LINE bot will reply again',
  'conversations.detail.iMessageSendFailed': 'iMessage send failed',
  'conversations.detail.iMessageSent': 'iMessage message sent',
  'conversations.detail.iMessageFailed': 'Failed to send iMessage message',
} satisfies Entries;
