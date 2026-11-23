import { zhHans, type TranslationKey } from '../locales/zh-Hans';
import { en } from '../locales/en';
import { translate } from './translate';
import type { Entries } from './types';

/**
 * 非组件模块的"当前语言"宿主。
 *
 * 为什么需要它：`utils/format.ts`、`utils/permissions.ts`、`constants/timezones.ts` 都是
 * **模块级纯函数/常量**，调用点遍布全站（`relativeTime(iso)`、`getRoleLabel(role)`、
 * `timezoneOptions`），把它们逐个改成"多收一个 t 或 locale 参数"要么改动上百个调用点，
 * 要么在 wave 内越界改别人的页面。
 *
 * 因此按任务约定采用**由 Provider 绑定一次**的方案：
 * `I18nProvider` 在渲染时调用 `setBoundLocale(language)`，这些模块读 `getBoundLocale()`。
 *
 * ⚠ 两条纪律：
 * 1. **不要**在这里放可变默认值以外的东西——它必须始终由 Provider 初始化；
 *    未挂载 Provider（单测、SSR 片段）时回落到真源语言 `zh-Hans`，与既有测试期望一致。
 * 2. 各函数仍保留**显式 locale 入参**（可选），单测与特殊场景可以直接指定语言，
 *    不必依赖全局绑定。
 */

/** 内置真源。与 `I18nContext` 同一份导出，避免两处各写一张表而漂移 */
export const BUILT_IN_ENTRIES: Record<string, Entries> = { 'zh-Hans': zhHans, en };

/** 缺省语言：与 `I18nContext` 的 FALLBACK_LANGUAGE 一致（中文为真源） */
export const FALLBACK_LOCALE = 'zh-Hans';

/**
 * 语言 → `Intl` 标签。`zh-Hans` 沿用现状的 `zh-CN`；`en` 用 `en-US`
 * （默认的月/日/年顺序 + 12 小时制，设计文档 §10）。
 *
 * 供 `utils/format.ts` 与页面里直接调用 `toLocale*` 的地方共用，
 * 避免"每个调用点各写一份 en ? 'en-US' : 'zh-CN'"而漂移。
 */
export function toIntlLocale(locale: string): string {
  return locale.toLowerCase().startsWith('en') ? 'en-US' : 'zh-CN';
}

let boundLocale: string = FALLBACK_LOCALE;

/** 由 `I18nProvider` 调用；非受支持语言（含 undefined）一律回落，绝不留半绑定状态 */
export function setBoundLocale(locale: string | null | undefined): void {
  boundLocale = locale && BUILT_IN_ENTRIES[locale] ? locale : FALLBACK_LOCALE;
}

export function getBoundLocale(): string {
  return boundLocale;
}

/**
 * 按当前语言取词条。
 *
 * 不传 `isDev`：缺 key 时 `translate` 返回 key 本身而**不抛错**（失败安全）。
 * 这条路径可能在渲染期被调用，抛错会把"漏译"升级成白屏。
 *
 * @param key 真源 key
 * @param locale 显式语言（可选）；缺省用绑定的当前语言
 */
export function tBound(key: TranslationKey, locale?: string): string {
  const effective = locale && BUILT_IN_ENTRIES[locale] ? locale : boundLocale;
  const entries = BUILT_IN_ENTRIES[effective] ?? BUILT_IN_ENTRIES[FALLBACK_LOCALE];
  return translate(entries, effective, key);
}
