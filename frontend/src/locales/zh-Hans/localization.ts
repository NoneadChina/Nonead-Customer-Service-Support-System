import type { Entries } from '../../i18n/types';

/**
 * 翻译管理页（`pages/Localization.tsx`，路由 `/localization`）。
 *
 * 归属：`/localization` 是**顶层页面**，因此自成一个模块（与 `data` / `integration` /
 * `serviceOps` 同例），不挂到 `settings` 下；`nav.localization` 是侧边栏标签，两者互不覆盖。
 *
 * **复用而非新造同义键**（本页沿用既有通用键）：
 * `nav.localization`（页头标题）· `data.common.export`（导出）· `data.common.exportFailed`（导出失败）·
 * `common.import`（导入）· `common.save`（保存）· `common.saveSucceeded` /
 * `common.savedButLanguageSwitchFailed` / `common.languageSwitchFailedSuffix`（保存反馈）·
 * `common.shell.loading`（加载中...）。本模块只收录该页**特有**的文案。
 *
 * `{ "key": "value" }` 里的花括号**不是**插值占位符（占位符形如 `{count}`），
 * 因此该文案不会在插值时被替换。
 */
export const localization = {
  // ── 页头与工具栏 ──
  'localization.page.subtitle': '管理多语言翻译内容，导入翻译后界面将立即应用',
  'localization.page.exportTitle': '导出翻译',
  'localization.page.importTitle': '导入翻译',

  // ── 统计卡片（页面上报的是"页面词条集"口径：数值由页面算出，随语言只变前后缀） ──
  // 词条文案 = 前端/后端真源 + DB 覆盖层，所以这里统计的是**被 DB 定制过**的条数，
  // 不是"该语言是否翻译完整"（内置真源本身已是完整译文）。
  'localization.stats.translated': '已定制 {count}',
  'localization.stats.untranslated': '未定制 {count}',
  'localization.stats.total': '共 {count}',

  // ── 词条列表 ──
  'localization.page.searchPlaceholder': '搜索翻译键或内容...',
  'localization.page.empty': '无匹配结果',
  'localization.page.translationPlaceholder': '请输入翻译内容',

  // ── 提示消息（成功/失败由状态位决定，**不再从文案里嗅探「失败」二字**） ──
  'localization.message.saveFailed': '保存失败',
  'localization.message.fileTooLarge': '文件大小不能超过 5MB',
  'localization.message.exportSucceeded': '导出成功，共 {count} 条',
  'localization.message.exportEmpty': '当前语言没有可导出的词条',
  'localization.message.importEmpty': '导入失败：文件里没有任何词条',
  'localization.message.importInvalidEntries': '导入失败：有 {count} 条不合规（键不能为空，值必须是文本）',
  'localization.message.importInvalidObject': '导入失败：文件格式应为 { "key": "value" } 的 JSON 对象',
  'localization.message.importMalformedJson': '导入失败：文件不是有效的 JSON 格式',
  'localization.message.importFailedPrefix': '导入失败：',
  'localization.message.importRequestFailed': '请求未成功，请检查文件内容后重试',
  'localization.message.unknownError': '未知错误',
  'localization.message.importSucceeded': '导入成功，共 {count} 条',
} satisfies Entries;
