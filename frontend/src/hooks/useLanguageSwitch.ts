import { useCallback, useState } from 'react';
import { profileApi } from '../api';
import { useAuth } from '../context/AuthContext';
import {
  normalizeLanguage,
  SUPPORTED_LANGUAGES,
  useI18n,
  type SupportedLanguage,
} from '../context/I18nContext';

/**
 * 语言显示名。`Record<SupportedLanguage, string>` 由编译器保证：
 * 新增受支持语言时这张表必须同步补齐，否则 `tsc` 报错。
 *
 * ⚠ **这里刻意不翻译、也不跟界面语言走**：语言选择器按惯例必须用各语言的**自称（endonym）**
 * ——中文用户在任何界面下都该看到 `简体中文`，而不是英文界面下的 `Chinese`。因此它是硬编码
 * 中文字面量，会被 `scripts/check-hardcoded-chinese.mjs` 计入基线，属**已登记的接受例外**
 * （批次 2 波次 7 裁定），不要为了消掉这处命中把值改成转义/变量。
 */
const LANGUAGE_LABELS: Record<SupportedLanguage, string> = {
  'zh-Hans': '简体中文', // i18n-exempt: 语言自称（endonym），不随界面语言变化
  en: 'English',
};

/**
 * 受支持语言的**唯一展示来源**。
 *
 * 顶部切换器、个人中心下拉框、翻译管理页的语言选择全部由它渲染。
 * 此前三处各写一份手写的 `<option>`，收敛语言范围（去掉 zh-Hant / ja）时必须改三遍，
 * 漏一处就会出现"下拉里能选、后端没有资源"的假选项。
 */
export const LANGUAGE_OPTIONS: ReadonlyArray<{ value: SupportedLanguage; label: string }> =
  SUPPORTED_LANGUAGES.map((value) => ({ value, label: LANGUAGE_LABELS[value] }));

export interface LanguageSwitch {
  /** 当前界面语言（受支持语言之一） */
  language: SupportedLanguage;
  /** **本实例**正在写回后端资料：调用方据此禁用切换控件，让"在途时被丢弃的点击"可见 */
  saving: boolean;
  /**
   * 切换语言：乐观生效 → 写回资料 → 失败回滚。
   *
   * 返回是否**落到了期望状态**，调用方必须据此提示成败（翻译管理页此前 `void` 掉它，
   * 写回失败也照报「保存成功」）：
   * - `true`：语言已是目标语言（含"本来就一致，无需切换"），且需要写回时写回成功；
   * - `false`：入参不受支持（归一化后为 null）、与**其它实例**的在途写回冲突、
   *   或写回失败（此时已回滚到原语言）。
   */
  switchLanguage: (next: string | null | undefined) => Promise<boolean>;
}

/**
 * 模块级"写回在途"标记：跨 hook 实例**共享**，这是本文件唯一的状态例外。
 *
 * `Layout` 顶栏的切换器与 `ProfileTab` 的语言下拉**同时挂载**，各自持有一份
 * `useState(saving)` 时，一次往返内的两次切换会双双通过 `|| saving` 检查，
 * 发出两个并发的 `PUT /api/profile`；两个响应里谁后到谁被 `updateUser`/纠正 effect 采纳，
 * 用户最后一次点击就被前一次的响应改回去。放在模块作用域后，第二个实例的点击
 * 会在第一个实例的请求未落地前直接返回 `false`（调用方据此提示失败），不会再发第二次 PUT。
 *
 * 只保护一个 HTTP 在途窗口，且 `finally` 里清空；本项目没有全局 store，
 * 这是不引入依赖的最小共享方式。
 */
let switchInFlight = false;

/**
 * 语言切换的唯一入口。
 *
 * 三处调用点此前语义各不相同：顶部切换器"即时生效并持久化"、个人中心下拉
 * "即时生效但要等『保存资料』才持久化"、翻译管理页"保存/导入时切换却从不持久化"。
 * 这里统一为一步：**乐观切换界面 → 用完整 payload 写回资料 → 失败回滚**。
 *
 * 两个必须守住的前提：
 * ① `PUT /api/profile` 是**整份替换**（少传字段会把该字段清空），因此只在资料完整时写后端；
 *    资料未就绪就退化为"仅本次会话生效"，宁可不持久化也不覆盖用户资料。
 * ② 写回失败（例如非超管用户曾经的 `403`）必须回滚到原语言且**不向上抛错**：
 *    界面语言与后端必须一致，切换控件也不该因为一次失败而崩掉——失败通过返回值上报。
 */
export function useLanguageSwitch(): LanguageSwitch {
  const { language, setLanguage } = useI18n();
  const { user, updateUser } = useAuth();
  // 互斥判断用模块级的 `switchInFlight`；这个每实例状态只驱动 `disabled` UI
  const [saving, setSaving] = useState(false);

  const switchLanguage = useCallback(
    async (next: string | null | undefined): Promise<boolean> => {
      // 归一化：不支持的值直接忽略（翻译管理页可以编辑本产品不提供的语言）
      const target = normalizeLanguage(next);
      if (!target) return false;
      // 已经是目标语言：无需切换、也无需写回
      if (target === language) return true;
      // 另一实例（顶栏切换器 / 个人中心下拉）正在写回：不再发第二个 PUT
      if (switchInFlight) return false;

      const previous = language;
      setLanguage(target); // 乐观切换：界面立即响应

      // MinimalUserInfo 只存 { id, authenticated }；字段缺失时不写后端。
      // 语言已在本次会话生效，只是不持久化——对调用点而言不算失败。
      if (!user || !user.firstName || !user.lastName || !user.timezone) return true;

      switchInFlight = true;
      setSaving(true);
      try {
        const updated = await profileApi.update({
          firstName: user.firstName,
          lastName: user.lastName,
          timezone: user.timezone,
          locale: target,
          avatar: user.avatar ?? undefined, // 无头像时保持 undefined，与 ProfileTab 一致
        });
        updateUser(updated); // 同步全局 user，避免下次 effect 用旧 locale 纠正回去
        return true;
      } catch {
        setLanguage(previous); // 写失败就回滚：界面语言与后端必须一致
        return false;
      } finally {
        switchInFlight = false;
        setSaving(false);
      }
    },
    [language, setLanguage, updateUser, user]
  );

  return { language, saving, switchLanguage };
}
