// 前端 UI 翻译 key 列表（供翻译管理页导入/导出与校验使用）
// 真源在 src/locales/（zh-Hans 与 en 各自一份，键集必须一致），此处仅为派生视图，避免两处维护
import { zhHans } from './locales/zh-Hans';
import { en } from './locales/en';
import type { Entry } from './i18n/types';

/** 全部合法 key */
export function getUiTranslationKeys(): string[] {
  return Object.keys(zhHans);
}

/**
 * 该语言的前端 UI 真源：key → 出厂文案。
 *
 * - 翻译管理页用它给未翻译项做占位与对照（缺省 `zh-Hans`，即中文真源）；
 * - **导出时也用同一张表做底座**（传当前所选语言）：导出必须与页面可编辑的词条集一致，
 *   而页面列出的正是这数千条前端 UI 键——只导后端 resx 会漏掉 99% 的词条。
 *
 * 复数词条（`{ one, other }`）在管理页只有一个输入框，统一展示 `other`
 * ——复数形态的翻译由词条真源维护，不在这个页面里编辑。
 *
 * 语言参数按 `string` 收（与页面 `culture` 状态、后端 `GetCultureSuffix` 的宽容口径一致）：
 * 非 `en` 一律按中文真源处理，未知语言不会静默变成英文。
 */
export function getUiTranslationDefaults(locale: string = 'zh-Hans'): Record<string, string> {
  const source = (locale === 'en' ? en : zhHans) as Record<string, Entry>;
  return Object.fromEntries(
    // 真源当前无复数词条，`satisfies` 保留下来的字面量类型会把值收窄成 `string`，
    // 使 `v.other` 分支不可达（TS2339）；这里按 `Entry` 读，保住复数词条走 `other` 的语义。
    Object.entries(source).map(([k, v]) => [k, typeof v === 'string' ? v : v.other])
  );
}

// 曾导出的 `isValidTranslationKey(key)` 已删除（2026-09-23，Task 6/7 审查修复轮）。
//
// 它没有任何调用方，且**不能**用在翻译管理页的导入流程里过滤未知 key：
// 该页可编辑的是**并集**——前端 UI 键（`src/locales/*`，点号键，数千条）∪
// 后端资源键（`NoneadCSS.Application.Resources.Messages*.resx`，PascalCase，62/65 条）。
// 两者交集为 0（前端 `nav.localization` / 后端 `LoginFailed`），
// 按前端真源过滤会把用户导入的后端词条**全部丢掉**，还报"导入成功"。
// 与其留一个语义上危险、又没人调用的导出，不如删掉。
