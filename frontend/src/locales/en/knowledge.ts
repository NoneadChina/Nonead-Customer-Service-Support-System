import type { Entries } from '../../i18n/types';

/**
 * Knowledge base copy (English values). Mirrors `zh-Hans/knowledge.ts` key-for-key:
 * one missing entry makes `en/index.ts`'s `Record<TranslationKey, Entry>` fail with TS2741.
 * Must use `satisfies Entries`, never a type annotation.
 *
 * Plural entries (`{ one, other }`) are only used where the English wording depends on `count`;
 * split fragments that carry a styled value receive `count` in params purely to pick the form
 * (their template does not reference `{count}`).
 */
export const knowledge = {
  // ── Page shell ──
  'knowledge.title': 'Knowledge base',
  'knowledge.subtitle':
    'Manage knowledge base categories and articles to capture team experience and best practices',

  // ── Article status (backend code → label) ──
  'knowledge.status.draft': 'Draft',
  'knowledge.status.published': 'Published',
  'knowledge.status.archived': 'Archived',

  // ── Shared buttons / labels ──
  'knowledge.common.newArticle': 'New article',
  'knowledge.common.editArticle': 'Edit article',
  'knowledge.common.refresh': 'Refresh',
  'knowledge.common.clearSearch': 'Clear search',
  'knowledge.common.view': 'View',
  'knowledge.common.edit': 'Edit',
  'knowledge.common.delete': 'Delete',
  'knowledge.common.restore': 'Restore',
  'knowledge.common.hardDelete': 'Delete permanently',
  'knowledge.common.clear': 'Empty',
  'knowledge.common.emptyTrash': 'Empty trash',
  'knowledge.common.cancel': 'Cancel',
  'knowledge.common.save': 'Save',
  'knowledge.common.saving': 'Saving...',
  'knowledge.common.saveFailed': 'Save failed',
  'knowledge.common.actionFailed': 'Action failed',
  'knowledge.common.noCategory': 'Uncategorized',
  'knowledge.common.disabled': 'Disabled',
  'knowledge.common.previousPage': 'Previous page',
  'knowledge.common.nextPage': 'Next page',

  'knowledge.common.totalBefore': 'Total ',
  'knowledge.common.paginationTotalBetween': {
    one: ' item, page ',
    other: ' items, page ',
  },
  'knowledge.common.paginationTotalAfter': ' / {totalPages}',
  'knowledge.common.sortLabel': 'Sort order',

  // ── Table columns ──
  'knowledge.common.col.title': 'Title',
  'knowledge.common.col.category': 'Category',
  'knowledge.common.col.status': 'Status',
  'knowledge.common.col.author': 'Author',
  'knowledge.common.col.actions': 'Actions',
  'knowledge.common.col.name': 'Name',
  'knowledge.common.col.description': 'Description',
  'knowledge.common.col.deletedAt': 'Deleted',

  // ── Overview cards ──
  'knowledge.list.overview.totalArticles': 'Total articles',
  'knowledge.list.overview.categories': 'Categories',

  // ── Articles ──
  'knowledge.list.title': 'Articles',
  'knowledge.list.searchPlaceholder': 'Search by title, content, or tag...',
  'knowledge.list.allStatuses': 'All statuses',
  'knowledge.list.allCategories': 'All categories',
  'knowledge.list.pinnedOnly': 'Pinned only',
  'knowledge.list.col.viewsLikes': 'Views / Likes',
  'knowledge.list.col.updatedAt': 'Updated',
  'knowledge.list.byAuthor': 'By {name}',
  'knowledge.list.updatedAt': 'Updated {date}',
  'knowledge.list.emptyFiltered': 'No matching articles',
  'knowledge.list.empty': 'No articles yet',
  'knowledge.list.emptyFilteredHint': 'Try adjusting the search terms or filters',
  'knowledge.list.emptyHint': 'Select "New article" to start building your knowledge base',
  'knowledge.list.recentTitle': 'Recent articles',
  'knowledge.list.recentSubtitle': 'Latest 5',
  'knowledge.list.popularTitle': 'Popular articles',
  'knowledge.list.popularSubtitle': 'Top 5 by views',
  'knowledge.list.loadArticlesFailed': 'Failed to load articles',

  // ── FTS5 global search ──
  'knowledge.search.placeholder': 'Full-text search (FTS5) across title, content, tag, category...',
  'knowledge.search.previewHint': 'Preview · full results appear in the main area',
  'knowledge.search.viewAllResults': 'View all results',
  'knowledge.search.emptyPrompt':
    'Enter a keyword to search articles (title, content, tag, or category)',
  'knowledge.search.searching': 'Searching...',
  'knowledge.search.searchingPleaseWait': 'Searching, please wait...',
  'knowledge.search.refreshing': 'Refreshing results...',
  'knowledge.search.sectionAria': 'Knowledge base search results',
  'knowledge.search.resultsTitle': 'Search results',
  'knowledge.search.ftsBadge': 'FTS5 full text',
  'knowledge.search.keywordPrefix': 'Keyword: ',
  'knowledge.search.totalResultsAfter': {
    one: ' result',
    other: ' results',
  },
  'knowledge.search.pageLabel': 'Page {page}',
  'knowledge.search.matchedBefore': 'Found ',
  'knowledge.search.matchedAfter': {
    one: ' matching article',
    other: ' matching articles',
  },
  'knowledge.search.matchedPagePrefix': ' · Page ',
  'knowledge.search.matchedPageSuffix': ' / {totalPages}',
  'knowledge.search.perPageAfter': {
    one: ' item, {pageSize} per page',
    other: ' items, {pageSize} per page',
  },
  'knowledge.search.noMatchHint': 'No matching articles. Try a different keyword.',
  'knowledge.search.noResultsTitle': 'No matching articles',
  'knowledge.search.noResultsHint':
    'Try a different keyword, or clear the search to return to the full article list',
  'knowledge.search.searchFailed': 'Search failed',

  // ── Trash ──
  'knowledge.trash.tab': 'Trash',
  'knowledge.trash.searchArticlesPlaceholder': 'Search deleted articles...',
  'knowledge.trash.searchCategoriesPlaceholder': 'Search deleted categories...',
  'knowledge.trash.emptyArticles': 'Trash is empty',
  'knowledge.trash.emptyCategories': 'Category trash is empty',
  'knowledge.trash.retentionArticles': 'Deleted articles are kept here for 30 days',
  'knowledge.trash.retentionCategories': 'Deleted categories are kept here for 30 days',
  'knowledge.trash.clearedArticles': {
    one: 'Emptied the article trash. {count} item deleted.',
    other: 'Emptied the article trash. {count} items deleted.',
  },
  'knowledge.trash.clearedCategories': {
    one: 'Emptied the category trash. {count} item deleted.',
    other: 'Emptied the category trash. {count} items deleted.',
  },
  'knowledge.trash.clearConfirmMessage': 'Empty the {target} trash? This cannot be undone.',
  'knowledge.trash.targetArticles': 'article',
  'knowledge.trash.targetCategories': 'category',
  'knowledge.trash.loadFailed': 'Failed to load the trash',
  'knowledge.trash.clearFailed': 'Failed to empty the trash',

  // ── Categories ──
  'knowledge.categories.title': 'Categories',
  'knowledge.categories.description':
    'Categories support nested levels. Deleting a category leaves its articles uncategorized.',
  'knowledge.categories.expandAll': 'Expand all',
  'knowledge.categories.collapseAll': 'Collapse all',
  'knowledge.categories.newCategory': 'New category',
  'knowledge.categories.editCategory': 'Edit category',
  'knowledge.categories.addSubcategory': 'New subcategory',
  'knowledge.categories.empty': 'No categories yet',
  'knowledge.categories.emptyHint': 'Create categories to organize your knowledge base articles',
  'knowledge.categories.articleCount': {
    one: '{count} article',
    other: '{count} articles',
  },
  'knowledge.categories.articleCountLong': {
    one: '{count} article',
    other: '{count} articles',
  },
  'knowledge.categories.nameLabel': 'Category name',
  'knowledge.categories.namePlaceholder': 'Enter a category name',
  'knowledge.categories.nameRequired': 'Enter a category name',
  'knowledge.categories.parentLabel': 'Parent category',
  'knowledge.categories.parentNone': 'None (top level)',
  'knowledge.categories.descriptionLabel': 'Description',
  'knowledge.categories.descriptionPlaceholder': 'Optional short description',
  'knowledge.categories.iconLabel': 'Icon',
  'knowledge.categories.iconPlaceholder': 'Custom, or pick one below',
  'knowledge.categories.iconPresetHint': 'Or choose a preset:',
  'knowledge.categories.mailboxLabel': 'Mailbox',
  'knowledge.categories.mailboxAll': 'General (all mailboxes)',
  'knowledge.categories.enableLabel': 'Enable this category',
  'knowledge.categories.enableHint': 'Disabled categories are hidden from category selectors',
  'knowledge.categories.created': 'Category created',
  'knowledge.categories.updated': 'Category updated',
  'knowledge.categories.deleted': 'Category moved to trash',
  'knowledge.categories.restored': 'Category restored',
  'knowledge.categories.hardDeleted': 'Category permanently deleted',
  'knowledge.categories.loadCategoriesFailed': 'Failed to load categories',
  'knowledge.categories.deleteConfirmTitle': 'Delete category',
  'knowledge.categories.deleteConfirmMessage': 'Move category "{name}" to trash?',
  'knowledge.categories.restoreConfirmTitle': 'Restore category',
  'knowledge.categories.restoreConfirmMessage': 'Restore category "{name}"?',
  'knowledge.categories.hardDeleteConfirmTitle': 'Delete category permanently',
  'knowledge.categories.hardDeleteConfirmMessage':
    'Permanently delete category "{name}"? This cannot be undone.',

  // ── Article editor ──
  'knowledge.editor.titleLabel': 'Title',
  'knowledge.editor.titlePlaceholder': 'Enter an article title',
  'knowledge.editor.titleRequired': 'Enter an article title',
  'knowledge.editor.categoryLabel': 'Category',
  'knowledge.editor.statusLabel': 'Status',
  'knowledge.editor.summaryLabel': 'Summary',
  'knowledge.editor.summaryPlaceholder': 'One-line summary of the article (optional)',
  'knowledge.editor.tagsLabel': 'Tags (comma separated)',
  'knowledge.editor.tagsPlaceholder': 'For example: how-to, FAQ, troubleshooting',
  'knowledge.editor.contentRequired': 'Enter the article content',
  'knowledge.editor.contentTooLong':
    'Article content exceeds the limit ({current} characters now, limit {max})',
  'knowledge.editor.contentLabelBefore': 'Content (',
  'knowledge.editor.contentLabelAfter': ')',
  'knowledge.editor.contentSwitchTitle': 'Switch to {format} mode',
  'knowledge.editor.contentSwitched':
    'Switched to {format} mode. Existing content is unchanged (no conversion).',
  'knowledge.editor.stats': '{words} / {max} characters · about {minutes} min read',
  'knowledge.editor.previewMode': 'Preview',
  'knowledge.editor.htmlHint':
    'HTML mode: write HTML directly. Dangerous tags (script, iframe, and similar) are removed during preview.',
  'knowledge.editor.htmlPlaceholder':
    '<h1>Heading</h1>\n\n<p>Write the article content here. HTML is supported...</p>\n\n<ul>\n  <li>List item</li>\n</ul>',
  'knowledge.editor.markdownPlaceholder':
    '# Heading\n\nWrite the article content here. Markdown is supported...\n\n**bold** *italic* `code` [link](url)\n\nTip: paste an image straight into the editor with Ctrl+V; in Markdown mode it is uploaded and linked automatically',
  'knowledge.editor.previewEmpty': 'Nothing to preview yet. Switch to edit mode to start writing.',
  'knowledge.editor.pinLabel': 'Pin article',
  'knowledge.editor.pinHint': 'Shown first in the list',
  'knowledge.editor.created': 'Article created',
  'knowledge.editor.updated': 'Article updated',

  // Markdown toolbar
  'knowledge.editor.md.heading1': 'Heading 1',
  'knowledge.editor.md.heading2': 'Heading 2',
  'knowledge.editor.md.heading3': 'Heading 3',
  'knowledge.editor.md.boldTitle': 'Bold',
  'knowledge.editor.md.boldPlaceholder': 'bold text',
  'knowledge.editor.md.italicTitle': 'Italic',
  'knowledge.editor.md.italicPlaceholder': 'italic text',
  'knowledge.editor.md.inlineCodeTitle': 'Inline code',
  'knowledge.editor.md.inlineCodePlaceholder': 'code',
  'knowledge.editor.md.codeBlockTitle': 'Code block',
  'knowledge.editor.md.codeBlockPlaceholder': 'code block',
  'knowledge.editor.md.unorderedListTitle': 'Bulleted list',
  'knowledge.editor.md.listItemPlaceholder': 'list item',
  'knowledge.editor.md.quoteTitle': 'Quote',
  'knowledge.editor.md.quotePlaceholder': 'quoted text',
  'knowledge.editor.md.linkTitle': 'Link',
  'knowledge.editor.md.linkPlaceholder': 'link text',
  'knowledge.editor.md.imageTitle': 'Image',
  'knowledge.editor.md.imagePlaceholder': 'image description',

  // ── Attachments ──
  'knowledge.attachments.sectionLabel': 'Attachments (PDF / image / document)',
  'knowledge.attachments.formatsHint':
    'Supported: PDF, image, Office, text, archive · Up to {limit} per file',
  'knowledge.attachments.uploadButton': 'Select a file to upload',
  'knowledge.attachments.uploading': 'Uploading...',
  'knowledge.attachments.uploadedList': 'Uploaded attachments ({count})',
  'knowledge.attachments.pdfPreviewable': '· PDF preview available',
  'knowledge.attachments.imagePreviewable': '· Image preview available',
  'knowledge.attachments.previewOrDownload': 'Preview / download',
  'knowledge.attachments.deleteAttachment': 'Delete attachment',
  'knowledge.attachments.saveArticleFirst': 'Save the article before uploading attachments',
  'knowledge.attachments.newModeHint':
    'Attachments can only be uploaded after the article is saved. Save the article first.',
  'knowledge.attachments.unsupportedType': 'Unsupported file type: .{ext} (allowed: {allowed})',
  'knowledge.attachments.tooLarge': 'File is too large ({current} now, limit {max})',
  'knowledge.attachments.uploaded': 'Attachment uploaded',
  'knowledge.attachments.uploadFailed': 'Attachment upload failed',
  'knowledge.attachments.deleted': 'Attachment deleted',
  'knowledge.attachments.deleteFailed': 'Failed to delete the attachment',
  'knowledge.attachments.downloadFailed': 'Failed to download the attachment',
  'knowledge.attachments.unsupportedImage': 'Unsupported image format: {ext}',
  'knowledge.attachments.imageTooLarge': 'Image is too large ({size} MB). The limit is {limit} MB.',
  'knowledge.attachments.pastedInline':
    'Pasted as an inline image. Save the article to migrate it to an attachment.',
  'knowledge.attachments.clipboardReadFailed': 'Failed to read the image from the clipboard',
  'knowledge.attachments.imageUploaded': 'Image uploaded and inserted into the editor',
  'knowledge.attachments.imageUploadFailed': 'Image upload failed',
  'knowledge.attachments.sectionTitle': 'Attachments ({count})',
  'knowledge.attachments.preview': 'Preview',
  'knowledge.attachments.download': 'Download',
  'knowledge.attachments.close': 'Close',
  'knowledge.attachments.loading': 'Loading attachment...',
  'knowledge.attachments.loadFailed':
    'Failed to load the attachment. Try downloading it instead.',

  // ── Article detail ──
  'knowledge.view.title': 'Article',
  'knowledge.view.back': 'Back',
  'knowledge.view.like': 'Like',
  'knowledge.view.unlike': 'Unlike',
  'knowledge.view.liked': 'Liked',
  'knowledge.view.likedHint': 'Liked. Select to unlike.',
  'knowledge.view.likeHint': 'Like this article',
  'knowledge.view.readTime': 'about {minutes} min read',
  'knowledge.view.views': {
    one: '{count} view',
    other: '{count} views',
  },
  'knowledge.view.footerViews': {
    one: '{count} view',
    other: '{count} views',
  },
  'knowledge.view.footerLikes': {
    one: '{count} like',
    other: '{count} likes',
  },
  'knowledge.view.likeSucceeded': 'Liked',
  'knowledge.view.unlikeSucceeded': 'Unliked',
  'knowledge.view.articleDeleted': 'Article deleted',
  'knowledge.view.articleRestored': 'Article restored',
  'knowledge.view.articleHardDeleted': 'Article permanently deleted',
  'knowledge.view.deleteFailed': 'Delete failed',
  'knowledge.view.restoreFailed': 'Restore failed',
  'knowledge.view.hardDeleteFailed': 'Permanent delete failed',
  'knowledge.view.deleteConfirmTitle': 'Delete article',
  'knowledge.view.deleteConfirmMessage': 'Move article "{title}" to trash?',
  'knowledge.view.restoreConfirmTitle': 'Restore article',
  'knowledge.view.restoreConfirmMessage': 'Restore article "{title}"?',
  'knowledge.view.hardDeleteConfirmTitle': 'Delete article permanently',
  'knowledge.view.hardDeleteConfirmMessage':
    'Permanently delete article "{title}"? This cannot be undone.',

  // ── Local draft conflict ──
  'knowledge.draft.restoredUnsaved': 'Restored an unsaved draft ({date})',
  'knowledge.draft.conflictTitle': 'Unsaved local draft detected',
  'knowledge.draft.conflictMessage':
    'A local draft is newer than the server version. Restore the draft, or use the server version?',
  'knowledge.draft.localLabel': 'Local draft',
  'knowledge.draft.serverLabel': 'Server version',
  'knowledge.draft.discard': 'Discard draft',
  'knowledge.draft.restore': 'Restore draft',
  'knowledge.draft.discarded': 'Local draft discarded. Using the server version.',
  'knowledge.draft.restored': 'Local draft restored',
} satisfies Entries;
