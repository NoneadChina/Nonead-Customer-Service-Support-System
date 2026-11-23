import type { PasswordPolicyResponse } from '../api/types';
import { BUILT_IN_ENTRIES, FALLBACK_LOCALE, getBoundLocale } from '../i18n/locale';
import { translate } from '../i18n/translate';
import type { TranslateParams } from '../i18n/types';
import type { TranslationKey } from '../locales/zh-Hans';

interface PasswordPolicyHintProps {
  policy: PasswordPolicyResponse | null;
  password?: string;
  className?: string;
}

/**
 * 按**当前界面语言**取词条（带插值）。
 *
 * 本组件刻意**不**用 `useI18n()`：它的渲染不依赖 Provider——`PasswordPolicyHint.test.tsx`
 * 就是直接 `render(<PasswordPolicyHint …/>)` 的，套上 `useI18n()` 会让组件在无 Provider 时抛错
 * （等于把"组件可选依赖"升级成"必须依赖"）。语言由 `I18nProvider` 在渲染体内
 * `setBoundLocale()` 绑定（与 `constants/timezones.ts` 同一手法），故消费方
 * （`ProfileTab` / `SecuritySettings`）重渲染时这里就跟上新语言。
 *
 * `tBound()` 不支持插值，而策略条目 `至少 {count} 个字符` 必须带数字，故直接走 `translate()`。
 */
function tPolicy(key: TranslationKey, params?: TranslateParams): string {
  const locale = getBoundLocale();
  return translate(BUILT_IN_ENTRIES[locale] ?? BUILT_IN_ENTRIES[FALLBACK_LOCALE], locale, key, params);
}

/**
 * 密码策略提示组件
 * - 不传 password 时，仅展示策略要求列表
 * - 传 password 时，对每条要求进行实时校验并显示通过/未通过状态
 */
export default function PasswordPolicyHint({
  policy,
  password,
  className = '',
}: PasswordPolicyHintProps) {
  if (!policy) return null;

  const items = buildItems(policy);

  return (
    <ul
      className={`mt-1.5 space-y-1 text-xs text-gray-500 dark:text-slate-400 ${className}`}
    >
      {items.map((item, idx) => {
        const ok = password !== undefined && item.check(password);
        const showStatus = password !== undefined && password.length > 0;
        return (
          <li
            key={idx}
            className={`flex items-center gap-1.5 transition-colors ${
              showStatus
                ? ok
                  ? 'text-emerald-600 dark:text-emerald-400'
                  : 'text-red-500 dark:text-red-400'
                : ''
            }`}
          >
            {showStatus && (
              <svg
                className="w-3 h-3 flex-shrink-0"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={3}
              >
                {ok ? (
                  <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
                ) : (
                  <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
                )}
              </svg>
            )}
            <span>{tPolicy(item.labelKey, item.params)}</span>
          </li>
        );
      })}
    </ul>
  );
}

/**
 * 策略条目表：**只存 key + 插值参数**，文案在渲染时按当前语言解析
 * （存 `label` 字面量会让语言切换后文案不跟随，也会让漏翻绕过类型检查）。
 */
function buildItems(
  policy: PasswordPolicyResponse
): { labelKey: TranslationKey; params?: TranslateParams; check: (pwd: string) => boolean }[] {
  const items: { labelKey: TranslationKey; params?: TranslateParams; check: (pwd: string) => boolean }[] = [
    {
      labelKey: 'settings.password.rule.minLength',
      params: { count: policy.minLength },
      check: (pwd) => pwd.length >= policy.minLength,
    },
  ];
  if (policy.requireUppercase) {
    items.push({
      labelKey: 'settings.password.rule.uppercase',
      check: (pwd) => /[A-Z]/.test(pwd),
    });
  }
  if (policy.requireLowercase) {
    items.push({
      labelKey: 'settings.password.rule.lowercase',
      check: (pwd) => /[a-z]/.test(pwd),
    });
  }
  if (policy.requireDigit) {
    items.push({
      labelKey: 'settings.password.rule.digit',
      check: (pwd) => /\d/.test(pwd),
    });
  }
  if (policy.requireNonAlphanumeric) {
    items.push({
      labelKey: 'settings.password.rule.symbol',
      check: (pwd) => /[^A-Za-z0-9]/.test(pwd),
    });
  }
  return items;
}
