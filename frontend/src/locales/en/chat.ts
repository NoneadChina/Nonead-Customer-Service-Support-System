import type { Entries } from '../../i18n/types';

/**
 * English strings for the live chat agent console and the customer-facing chat widget.
 *
 * Language ownership (decision for this wave): the agent-side screens follow the current UI
 * language (`useI18n()`), like the rest of the app. `CustomerChatWidget` is mounted on the
 * public route `/chat/widget` (see `routes/index.tsx` `publicRoutes`) without authentication,
 * so `I18nProvider` resolves the visitor's own language
 * (`localStorage` then `navigator.language`, falling back to `zh-Hans`). Reuse
 * `useI18n()`/`t` there as well; do not add a separate language switch and never hard-code
 * an agent language.
 *
 * Conversation status labels come from the backend as Chinese text
 * (`MappingExtensions.ToDto(ChatSession)`, `MappingExtensions.cs:600-607`). The frontend now
 * resolves them from this dictionary by stable code; the five codes are exactly
 * `waiting` / `active` / `awaiting_rating` / `closed` / `transferred`, and unknown codes
 * fall back to rendering the raw code.
 *
 * Wording follows the design glossary: conversation (not ticket), agent, customer,
 * live chat, chat widget. Customer-facing copy avoids internal jargon.
 *
 * Inline value styling: never move a styled dynamic value inside the translation string
 * (the value would lose its own element). This wave either splits a before/after key pair
 * so the value keeps its element (`chat.page.trashCountBefore` / `After`), or keeps the
 * styled element and only replaces the plain text inside it.
 */
export const chat = {
  // ── Chat session status (backend code → label; mirrors MappingExtensions.cs:600-607) ──
  'chat.status.waiting': 'Waiting',
  'chat.status.active': 'Active',
  'chat.status.awaitingRating': 'Awaiting rating',
  'chat.status.closed': 'Closed',
  'chat.status.transferred': 'Converted to conversation',

  // ── chat.page：在线聊天页（pages/Chat.tsx）——概览 / 垃圾箱 / 删除确认 ──
  // ── Header / overview ──
  'chat.page.title': 'Live chat',
  'chat.page.subtitle': 'Manage live chat conversations and talk to customers in real time',
  'chat.page.refresh': 'Refresh',
  'chat.page.statTotal': 'Total conversations',
  'chat.page.statUnread': 'Unread messages',
  'chat.page.accept': 'Accept',
  'chat.page.noOverview': 'No overview data',
  'chat.page.noOverviewHint': 'Use the refresh button in the top right to try again',
  'chat.page.noSessions': 'No conversations yet',
  'chat.page.noSessionsHint': 'New customer chat requests will appear in the list on the left',

  // ── Toasts ──
  'chat.page.toastAccepted': 'Conversation accepted',
  'chat.page.acceptFailed': 'Failed to accept the conversation',
  'chat.page.ratingRequested': 'Rating request sent, waiting for the customer to rate',
  'chat.page.sessionClosed': 'Conversation closed',
  'chat.page.closeFailed': 'Failed to close the conversation',
  'chat.page.convertedToConversation': 'Converted to conversation',
  'chat.page.movedToTrash': 'Moved to trash',
  'chat.page.deleteFailed': 'Failed to delete',
  'chat.page.bulkDeleted': {
    one: 'Deleted {count} conversation',
    other: 'Deleted {count} conversations',
  },
  'chat.page.bulkDeleteFailed': 'Failed to delete the selected conversations',
  'chat.page.restored': 'Conversation restored',
  'chat.page.restoreFailed': 'Failed to restore the conversation',
  'chat.page.permanentlyDeleted': 'Permanently deleted',
  'chat.page.trashCleared': {
    one: 'Trash emptied. {count} conversation permanently deleted.',
    other: 'Trash emptied. {count} conversations permanently deleted.',
  },
  'chat.page.clearTrashFailed': 'Failed to empty the trash',

  // ── Confirmation dialogs ──
  // The dialog title and the confirm button share one label key per action, because the
  // Chinese source uses the exact same literal in both places; messages stay separate.
  'chat.page.confirmClose': 'Close conversation',
  'chat.page.confirmCloseMessage':
    'Close this live chat conversation? The customer will not be able to send more messages.',
  'chat.page.moveToTrash': 'Move to trash',
  'chat.page.moveToTrashMessage':
    'Move this conversation to the trash? You can restore it or delete it permanently from the trash.',
  'chat.page.bulkMoveToTrash': 'Move to trash',
  'chat.page.bulkMoveToTrashMessage': {
    one: 'Move the selected {count} conversation to the trash? You can restore it or delete it permanently from the trash.',
    other: 'Move the selected {count} conversations to the trash? You can restore them or delete them permanently from the trash.',
  },
  'chat.page.permanentlyDelete': 'Delete permanently',
  'chat.page.permanentlyDeleteMessage':
    'Delete this conversation permanently? This action cannot be undone.',
  'chat.page.clearTrash': 'Empty trash',
  'chat.page.clearTrashMessage':
    'Empty the trash? All conversations will be permanently deleted. This action cannot be undone.',

  // ── Trash modal ──
  'chat.page.trashTitle': 'Trash - Live chat conversations',
  'chat.page.trashSearchPlaceholder': 'Search deleted conversations (customer name/email)...',
  // The count line keeps its own bold span, so it is split into a before/after pair
  // (each part carries the original spacing).
  'chat.page.trashCountBefore': 'Total ',
  'chat.page.trashCountAfter': {
    one: ' deleted conversation',
    other: ' deleted conversations',
  },
  'chat.page.trashEmpty': 'Trash is empty',
  'chat.page.trashNoMatch': 'No matching deleted conversations',
  'chat.page.trashEmptyHint': 'No deleted live chat conversations',
  'chat.page.colCustomer': 'Customer',
  'chat.page.colEmail': 'Email',
  'chat.page.colStatus': 'Status',
  'chat.page.colDeletedAt': 'Deleted',
  'chat.page.colActions': 'Actions',
  'chat.page.unknownCustomer': 'Unknown customer',
  'chat.page.unknownCustomerParen': '(Unknown customer)',
  'chat.page.restore': 'Restore',
  'chat.page.restoreAria': 'Restore conversation',
  'chat.page.permanentlyDeleteAria': 'Delete conversation permanently',
  'chat.page.mobileEmail': 'Email: ',
  'chat.page.mobileDeletedAt': 'Deleted: ',
  'chat.page.trashPageOf': 'Page {page} of {totalPages}',
  'chat.page.prevPage': 'Previous page',
  'chat.page.nextPage': 'Next page',

  // ── chat.list：会话列表（components/chat/ChatSessionList.tsx） ──
  // ── Status filter tabs (short labels; the full status wording lives in chat.status.*) ──
  'chat.list.filter.all': 'All',
  'chat.list.filter.waiting': 'Waiting',
  'chat.list.filter.active': 'Active',
  'chat.list.filter.awaitingRating': 'Awaiting rating',
  'chat.list.filter.closed': 'Closed',
  'chat.list.filter.transferred': 'Converted',

  // ── Panel heading ──
  'chat.list.title': 'Conversations',
  'chat.list.subtitle': 'Live chats and history',

  // ── Toolbar / search / multi-select ──
  'chat.list.selectMode': 'Select mode',
  'chat.list.exitSelectMode': 'Exit select mode',
  'chat.list.trash': 'Trash',
  'chat.list.searchPlaceholder': 'Search by customer name or email',
  'chat.list.clearSearch': 'Clear search',
  'chat.list.allMailboxes': 'All mailboxes',
  'chat.list.selectAll': 'Select all',
  'chat.list.deselectAll': 'Deselect all',
  'chat.list.selectedCount': '{selected} of {total} selected',
  'chat.list.bulkDelete': 'Delete selected',

  // ── Empty states ──
  'chat.list.emptyTitle': 'No conversations',
  'chat.list.emptyFiltered': 'No matching conversations. Try adjusting the filters.',
  'chat.list.emptyHint': 'No live chats yet. New customer inquiries appear here in real time.',

  // ── Session card ──
  'chat.list.moveToTrash': 'Move to trash',
  'chat.list.awaitingRatingHint': 'Awaiting customer rating',
  'chat.list.unknownCustomer': 'Unknown customer',
  'chat.list.accept': 'Accept',

  // ── Pagination ──
  'chat.list.prevPage': 'Previous page',
  'chat.list.nextPage': 'Next page',

  // ── chat.conversation：会话详情（components/chat/ChatConversation.tsx） ──
  // ── Date separators ──
  'chat.conversation.dateToday': 'Today',
  'chat.conversation.dateYesterday': 'Yesterday',
  'chat.conversation.dateTomorrow': 'Tomorrow',

  // ── Conversation header ──
  'chat.conversation.typing': '{name} is typing...',
  'chat.conversation.back': 'Back',
  'chat.conversation.unknownCustomer': 'Unknown customer',
  'chat.conversation.awaitingRating': 'Awaiting customer rating',
  'chat.conversation.searchMessages': 'Search messages',
  'chat.conversation.transferDisabledAwaitingRating':
    'Cannot convert to conversation while awaiting the customer rating',
  'chat.conversation.transfer': 'Convert to conversation',
  'chat.conversation.awaitingRatingTimeout': 'Awaiting rating. Closes automatically on timeout.',

  // ── Search bar ──
  'chat.conversation.searchInConversation': 'Search in this conversation...',
  'chat.conversation.noMatches': 'No matches',
  'chat.conversation.prevMatch': 'Previous match',
  'chat.conversation.nextMatch': 'Next match',
  'chat.conversation.closeSearch': 'Close search',

  // ── Message stream ──
  'chat.conversation.loadingMessages': 'Loading messages...',
  'chat.conversation.startNewTitle': 'Start a new conversation',
  'chat.conversation.startNewHint': 'Send your first message to the customer in the box below',

  // ── chat.bubble：消息气泡（components/chat/ChatMessageBubble.tsx） ──
  // ── System message banner ──
  'chat.bubble.satisfactionSurvey': 'Satisfaction survey',
  'chat.bubble.systemNotice': 'System notice',

  // ── Read receipt / edited marker ──
  'chat.bubble.readAt': 'Read {time}',
  'chat.bubble.unread': 'Unread',
  'chat.bubble.editedAt': 'Edited {time}',
  'chat.bubble.edited': 'Edited',

  // ── Sender fallback name / recall ──
  'chat.bubble.agent': 'Agent',
  'chat.bubble.recalledBy': '{name} recalled a message',

  // ── Edit mode ──
  'chat.bubble.editHint': 'Enter to save · Esc to cancel',
  'chat.bubble.saving': 'Saving',

  // ── Attachments ──
  'chat.bubble.viewImage': 'View image',
  'chat.bubble.image': 'Image',
  'chat.bubble.attachment': 'Attachment',
  'chat.bubble.clickToDownload': 'Download',

  // ── Actions ──
  'chat.bubble.copyMessage': 'Copy message',
  'chat.bubble.moreActions': 'More actions',
  'chat.bubble.recall': 'Recall',

  // ── chat.composer：输入区（components/chat/ChatComposer.tsx） ──
  // ── @mention autocomplete (ChatComposer.tsx, agent side only) ──
  'chat.composer.mentionTitle': 'Mentions',
  'chat.composer.mentionNoMatch': 'No matching users',
  'chat.composer.mentionEmpty': 'No users available to mention',
  'chat.composer.mentionHint': '↑↓ navigate · Enter select · Esc cancel',
  'chat.composer.mentionCount': '{count} people',

  // ── Canned response popover ──
  'chat.composer.cannedTitle': 'Canned responses',
  'chat.composer.cannedSubtitle': 'Quickly insert frequently used replies',
  'chat.composer.cannedSearchPlaceholder': 'Search canned responses...',
  'chat.composer.cannedNoMatch': 'No matching canned responses',
  'chat.composer.cannedEmpty': 'No canned response templates yet',

  // ── Emoji panel ──
  'chat.composer.emojiTitle': 'Emoji',
  'chat.composer.emojiSubtitle': 'Common emoji',
  'chat.composer.insertEmoji': 'Insert emoji {emoji}',

  // ── Attachment preview and upload ──
  'chat.composer.removeAttachment': 'Remove attachment',
  'chat.composer.toolbar': 'Toolbar',
  'chat.composer.uploadAttachmentHint': 'Upload attachment (max 25MB)',
  'chat.composer.uploadAttachment': 'Upload attachment',

  // ── Input box and send button ──
  'chat.composer.sessionClosed': 'Conversation is closed',
  'chat.composer.placeholder': 'Type a message, Enter to send · Shift+Enter for a new line',
  'chat.composer.sendMessage': 'Send message',
  'chat.composer.enterContent': 'Enter a message',
  'chat.composer.send': 'Send',

  // ── Validation and failure toasts ──
  'chat.composer.fileTooLarge': 'File size must not exceed 25MB (currently {size})',
  'chat.composer.unsupportedFileType': 'Unsupported file type, upload an image, a document or an archive',
  'chat.composer.uploadFailed': 'Attachment upload failed',
  'chat.composer.sendFailed': 'Failed to send',

  // ── chat.info：信息面板（components/chat/ChatInfoPanel.tsx） ──
  'chat.info.unknownCustomer': 'Unknown customer',

  // ── Rating ──
  'chat.info.customerRating': 'Customer rating',

  // ── Customer ──
  'chat.info.customerSection': 'Customer information',
  'chat.info.email': 'Email',
  'chat.info.notProvided': 'Not provided',
  'chat.info.phone': 'Phone',
  'chat.info.ipAddress': 'IP address',
  'chat.info.customerId': 'Customer ID',
  'chat.info.notLinked': 'Not linked',

  // ── Conversation ──
  'chat.info.conversationSection': 'Conversation information',
  'chat.info.subject': 'Subject',
  'chat.info.noSubject': 'No subject',
  'chat.info.mailbox': 'Mailbox',
  'chat.info.agent': 'Agent',
  'chat.info.messageCount': 'Total messages',
  'chat.info.unreadCount': '{count} unread',
  'chat.info.createdAt': 'Created',
  'chat.info.updatedAt': 'Last updated',
  'chat.info.closedAt': 'Closed',

  // ── Linked conversation (the product glossary calls a converted ticket a conversation) ──
  'chat.info.linkedConversation': 'Linked conversation',
  'chat.info.viewConversation': 'View conversation',

  // ── chat.transfer：转工单弹窗（components/chat/ChatTransferModal.tsx） ──
  // ── Convert-to-conversation dialog (ChatTransferModal.tsx) ──
  'chat.transfer.title': 'Convert to conversation',
  'chat.transfer.bannerTitle': 'Convert this live chat to a conversation',
  'chat.transfer.descForCustomer': 'The live chat with {name} will be converted to a conversation for further follow-up. The chat history will be kept as an attachment.',
  'chat.transfer.descCurrent': 'The current live chat will be converted to a conversation for further follow-up. The chat history will be kept as an attachment.',
  'chat.transfer.mailboxLabel': 'Target mailbox',
  'chat.transfer.optional': '(optional)',
  'chat.transfer.useOriginalMailbox': 'Use the original mailbox',
  'chat.transfer.agentLabel': 'Assign agent',
  'chat.transfer.unassignedOption': 'Unassigned (auto-assign)',
  'chat.transfer.subjectLabel': 'Conversation subject',
  'chat.transfer.subjectPlaceholder': 'Leave empty to generate automatically',
  'chat.transfer.next': 'Next',

  // ── Second confirmation ──
  'chat.transfer.confirmTitle': 'Confirm conversion',
  'chat.transfer.confirmMessage': 'Convert this live chat to a conversation? It will then be marked as Converted to conversation.',
  'chat.transfer.converting': 'Converting...',
  'chat.transfer.confirmAction': 'Convert to conversation',

  // ── chat.widget：客户侧聊天挂件（pages/CustomerChatWidget.tsx，公开页 /chat/widget） ──
  // ── Initiate form: validation errors (numbers mirror the maxLength props) ──
  'chat.widget.form.error.nameRequired': 'Enter your name',
  'chat.widget.form.error.nameTooLong': 'Name cannot exceed 50 characters',
  'chat.widget.form.error.emailRequired': 'Enter your email address',
  'chat.widget.form.error.emailInvalid': 'Enter a valid email address, for example example@domain.com',
  'chat.widget.form.error.emailTooLong': 'Email cannot exceed 100 characters',
  'chat.widget.form.error.phoneRequired': 'Enter your phone number',
  'chat.widget.form.error.phoneInvalid': 'Enter a valid phone number, for example 13812345678 or +86 138 1234 5678',
  'chat.widget.form.error.subjectTooLong': 'Subject cannot exceed 200 characters',
  'chat.widget.form.error.messageTooLong': 'Message cannot exceed 2000 characters',

  // ── Welcome card ──
  'chat.widget.welcome.title': 'Hello, we are here to help',
  'chat.widget.welcome.subtitle': 'Our support team is ready to help you. Fill in your details to start a chat; the first reply usually arrives within 2 minutes',
  'chat.widget.welcome.online': 'Support online',
  'chat.widget.welcome.secure': 'Encrypted',
  'chat.widget.welcome.private': 'Privacy protected',

  // ── Form labels and placeholders ──
  'chat.widget.form.name': 'Your name',
  'chat.widget.form.namePlaceholder': 'Enter your name or nickname',
  'chat.widget.form.email': 'Contact email',
  'chat.widget.form.phone': 'Contact phone',
  'chat.widget.form.phonePlaceholder': '13812345678 or +86 138 1234 5678',
  'chat.widget.form.subject': 'Subject',
  'chat.widget.form.optional': '(optional)',
  'chat.widget.form.subjectPlaceholder': 'Briefly describe your question',
  'chat.widget.form.message': 'Description',
  'chat.widget.form.messagePlaceholder': 'Describe your issue (Enter to send, Shift+Enter for a new line)',
  'chat.widget.form.connecting': 'Connecting you to support...',
  'chat.widget.form.start': 'Start chat',
  'chat.widget.form.terms': 'By starting a chat you agree to our terms of service',

  // ── Message bubbles and content rendering ──
  'chat.widget.message.imageAlt': 'Image',
  'chat.widget.message.fileFallback': 'File',
  'chat.widget.message.satisfactionSurvey': 'Satisfaction survey',
  'chat.widget.message.systemNotice': 'System notice',
  'chat.widget.message.agentInitial': 'S',
  'chat.widget.message.you': 'You',
  'chat.widget.message.agent': 'Agent',
  'chat.widget.message.edited': 'Edited',
  'chat.widget.message.recalled': 'This message was withdrawn',
  'chat.widget.message.viewAttachment': 'View attachment',
  'chat.widget.message.read': 'Read',
  'chat.widget.message.sent': 'Sent',

  // ── Typing indicator ({name} is the other party) ──
  'chat.widget.typing': '{name} is typing...',

  // ── Rating form ──
  'chat.widget.rating.veryDissatisfied': 'Very dissatisfied',
  'chat.widget.rating.dissatisfied': 'Dissatisfied',
  'chat.widget.rating.neutral': 'Neutral',
  'chat.widget.rating.satisfied': 'Satisfied',
  'chat.widget.rating.verySatisfied': 'Very satisfied',
  'chat.widget.rating.required': 'Select a rating from 1 to 5 stars',
  'chat.widget.rating.title': 'Conversation ended',
  'chat.widget.rating.subtitle': 'Thank you for your time. Rate this conversation to help us improve',
  'chat.widget.rating.countdownBefore': 'Please rate within',
  'chat.widget.rating.countdownAfter': 'or the conversation will close automatically',
  'chat.widget.rating.starAria': '{star} of 5 stars - {label}',
  'chat.widget.rating.starTitle': '{star} of 5 stars: {label}',
  'chat.widget.rating.hint': 'Select a star to rate',
  'chat.widget.rating.comment': 'Comments',
  'chat.widget.rating.commentPlaceholder': 'Tell us about your experience and how we can improve...',
  'chat.widget.rating.skip': 'Not now',
  'chat.widget.rating.submitting': 'Submitting...',
  'chat.widget.rating.submit': 'Submit rating',

  // ── Request failure notices ──
  'chat.widget.error.initiateFailed': 'Could not start the chat. Try again in a moment',
  'chat.widget.error.sendFailed': 'Message could not be sent',
  'chat.widget.error.closeFailed': 'Could not close the conversation',
  'chat.widget.error.rateFailed': 'Could not submit your rating',

  // ── SignalR connection state ──
  'chat.widget.connection.connected': 'Connected',
  'chat.widget.connection.connecting': 'Connecting',
  'chat.widget.connection.reconnecting': 'Reconnecting',
  'chat.widget.connection.disconnected': 'Disconnected',

  // ── Accessibility labels and tooltips ──
  'chat.widget.a11y.openChat': 'Open chat',
  'chat.widget.a11y.startChat': 'Start a live chat',
  'chat.widget.a11y.minimize': 'Minimize',
  'chat.widget.a11y.minimizeToBubble': 'Minimize to the floating button',
  'chat.widget.a11y.endChat': 'End conversation',
  'chat.widget.a11y.endChatHint': 'End this conversation and rate it',
  'chat.widget.a11y.newChat': 'New conversation',
  'chat.widget.a11y.newChatHint': 'Start a new conversation',
  'chat.widget.a11y.dismissError': 'Dismiss error',
  'chat.widget.a11y.emoji': 'Emoji',
  'chat.widget.a11y.insertEmoji': 'Insert emoji',
  'chat.widget.a11y.send': 'Send',
  'chat.widget.a11y.sendMessage': 'Send message',
  'chat.widget.a11y.sendDisabled': 'Type a message to send',

  // ── Header and empty state ──
  'chat.widget.header.title': 'Live chat',
  'chat.widget.loading.messages': 'Loading messages...',
  'chat.widget.loading.pleaseWait': 'Just a moment',
  'chat.widget.empty.title': 'Welcome to live chat',
  'chat.widget.empty.subtitle': 'Type your question below and our support team will reply as soon as possible',
  'chat.widget.composer.placeholder': 'Type a message, Enter to send, Shift+Enter for a new line...',

  // ── Completed page ──
  'chat.widget.completed.ratedTitle': 'Thank you for your feedback',
  'chat.widget.completed.timeoutTitle': 'The conversation has ended automatically',
  'chat.widget.completed.ratedSubtitle': 'Your feedback matters to us and helps us improve our service.',
  'chat.widget.completed.timeoutSubtitle': 'The conversation was closed automatically because no rating was submitted in time.',
  'chat.widget.completed.retryHint': 'To start another chat, use the button below',
  'chat.widget.completed.newChat': 'Start a new chat',
} satisfies Entries;
