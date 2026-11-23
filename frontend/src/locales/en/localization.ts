import type { Entries } from '../../i18n/types';

/** Translation management page (`pages/Localization.tsx`, route `/localization`). */
export const localization = {
  // ── Page header and toolbar ──
  'localization.page.subtitle': 'Manage multilingual translations. Imported translations are applied to the interface immediately.',
  'localization.page.exportTitle': 'Export translations',
  'localization.page.importTitle': 'Import translations',

  // ── Stats cards (counted over the page's key set; only the affixes change with the language) ──
  // Text comes from the built-in source plus the DB override layer, so these count entries
  // **customized in the database**, not "whether the language is fully translated"
  // (the built-in source is already a complete translation).
  'localization.stats.translated': 'Customized {count}',
  'localization.stats.untranslated': 'Not customized {count}',
  'localization.stats.total': 'Total {count}',

  // ── Entry list ──
  'localization.page.searchPlaceholder': 'Search translation keys or content...',
  'localization.page.empty': 'No matching results',
  'localization.page.translationPlaceholder': 'Enter the translation text',

  // ── Messages (success/failure comes from state, not from sniffing the text) ──
  'localization.message.saveFailed': 'Save failed',
  'localization.message.fileTooLarge': 'The file must not exceed 5 MB',
  'localization.message.exportSucceeded': 'Exported {count} entries',
  'localization.message.exportEmpty': 'There is nothing to export for this language',
  'localization.message.importEmpty': 'Import failed: the file contains no entries',
  'localization.message.importInvalidEntries': 'Import failed: {count} entries are invalid (keys must not be empty and values must be text)',
  'localization.message.importInvalidObject': 'Import failed: the file must be a JSON object of { "key": "value" } pairs',
  'localization.message.importMalformedJson': 'Import failed: the file is not valid JSON',
  'localization.message.importFailedPrefix': 'Import failed: ',
  'localization.message.importRequestFailed': 'the request did not succeed, please check the file and try again',
  'localization.message.unknownError': 'Unknown error',
  'localization.message.importSucceeded': 'Import succeeded: {count} entries',
} satisfies Entries;
