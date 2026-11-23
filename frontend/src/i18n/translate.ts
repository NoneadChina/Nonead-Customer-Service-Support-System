import type { Entries, TranslateParams } from './types';

export interface TranslateOptions {
  /** 开发环境：缺失 key 直接抛错，让漏翻在开发时立刻暴露 */
  isDev?: boolean;
  /** 生产环境：缺失 key 的上报回调（每个 key 只上报一次） */
  onMissing?: (key: string) => void;
}

/** 已上报过的缺失 key，避免重复刷日志 */
const reported = new Set<string>();

/**
 * 解析词条并插值。
 *
 * - 复数：词条为 { one, other } 时，仅当 locale 以 "en" 开头且 count 归一化后 === 1 取 one，否则取 other
 * - 插值：替换所有 `{name}`（`name` 仅限 `\w+`，即字母/数字/下划线）形式的占位符；
 *   未提供的占位符保持原样；含点号的 `{user.name}` 原样保留（只读自有属性，不沿原型链取值）
 * - 缺失 key：**显式** options.isDev === true 时抛错；否则返回 key 本身并通过 onMissing 上报一次
 *
 * ⚠ 缺省必须保持"不抛错"（失败安全），**不要改成 `options?.isDev ?? true`**。
 * 抛错只应在开发期发生、由调用方显式要求（由调用方显式传入环境标志）。
 * 若缺省为抛错，任何漏传 isDev 的调用方在生产会把"漏译（显示 key）"升级为"请求 500"。
 */
export function translate(
  entries: Entries,
  locale: string,
  key: string,
  params?: TranslateParams,
  options?: TranslateOptions
): string {
  // 只认自有属性：`entries[key]` 会沿原型链取到 `constructor` / `toString` / `__proto__`，
  // 于是 entry 不为 undefined、走进复数分支、返回 undefined（违背声明的 string 返回类型），
  // 同时绕过 isDev 抛错与 onMissing 上报。Task 6 的 DB 覆盖层把运行期数据喂进这张表，路径真实可达。
  const entry = Object.prototype.hasOwnProperty.call(entries, key) ? entries[key] : undefined;

  if (entry === undefined) {
    if (options?.isDev) {
      // 开发者诊断消息：只在**开发期**由调用方显式传 isDev 时抛出，用户永远看不到；
      // 而且这里正是 i18n 内核本身，用 `t()` 会形成自举循环。
      throw new Error(`[i18n] 缺失词条：${key}（locale=${locale}）`); // i18n-exempt: 开发者诊断消息，非用户可见文案
    }
    if (options?.onMissing && !reported.has(key)) {
      reported.add(key);
      options.onMissing(key);
    }
    return key;
  }

  let template: string;
  if (typeof entry === 'string') {
    template = entry;
  } else {
    const useOne = locale.startsWith('en') && Number(params?.count) === 1;
    template = useOne ? entry.one : entry.other;
  }

  if (!params) {
    return template;
  }

  return template.replace(/\{(\w+)\}/g, (match, name: string) => {
    if (!Object.prototype.hasOwnProperty.call(params, name)) return match;
    return String(params[name]);
  });
}
