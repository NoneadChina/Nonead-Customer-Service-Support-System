/**
 * 国际化词条类型。
 *
 * 词条可以是普通字符串，或带单复数的对象：
 * 英文需要 "1 conversation" / "2 conversations" 两态，中文两态相同（写成普通字符串即可）。
 */
export type Entry = string | { one: string; other: string };

/**
 * 词条集合的形状：**宽键**记录，不约束键名（任何 string 键都合法）。
 *
 * `TranslationKey`（自 `zh-Hans` 的 `zhHans` 派生）**定义**了合法键集合，
 * 但派生本身不保证任何完整性——少键不会因此报错。
 * 完整性**只**由 `en/index.ts` 的 `en: Record<TranslationKey, Entry>` 标注强制；
 * 把那一行放宽成 `Record<string, Entry>`，"漏翻 = 编译错误"这一保证即整体静默失效。
 */
export type Entries = Record<string, Entry>;

/** 插值参数：{count} 之类的占位符值 */
export type TranslateParams = Record<string, string | number>;
