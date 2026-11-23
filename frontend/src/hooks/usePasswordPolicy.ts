import { useEffect, useState, useCallback } from 'react';
import { authApi } from '../api';
import type { PasswordPolicyResponse } from '../api/types';
import { useI18n } from '../context/I18nContext';

/**
 * 加载并缓存系统密码策略的 Hook
 * - 自动获取一次策略
 * - 提供 validate 方法用于本地校验密码
 *
 * **文案语言**：`validate()` 返回的是**用户可见的校验消息**，故本 hook 内部取 `useI18n()` 的
 * `t`（hook 可以调用 hook）。消费方 `Users` / `ProfileTab` / `SecuritySettings` 都在
 * `I18nProvider` 内，语言随界面切换；`t` 进 `useCallback` 依赖，切语言后 `validate` 会重建。
 * 返回值仍是**纯数据**（`string[]`），不含 React 元素。
 */
export function usePasswordPolicy() {
  const { t } = useI18n();
  const [policy, setPolicy] = useState<PasswordPolicyResponse | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let mounted = true;
    setLoading(true);
    authApi
      .getPasswordPolicy()
      .then((p) => {
        if (mounted) setPolicy(p);
      })
      .catch(() => {
        // 获取失败时使用默认策略（与 SystemOptionSeeder 默认值一致）
        if (mounted) {
          setPolicy({
            minLength: 6,
            requireUppercase: false,
            requireLowercase: false,
            requireDigit: false,
            requireNonAlphanumeric: false,
            requirements: [t('settings.password.rule.minLength', { count: 6 })],
          });
        }
      })
      .finally(() => {
        if (mounted) setLoading(false);
      });
    return () => {
      mounted = false;
    };
    // `t` 刻意不进依赖：它随语言**和** DB 覆盖层加载而换身份，进依赖会让首屏多打一次
    // `getPasswordPolicy`（波次 4 在同类位置上实测过）。兜底策略里的文案只在"请求失败"这一
    // 分支用到，且取的是请求失败那一刻的语言，可接受。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  /**
   * 校验密码是否符合当前策略
   * @returns 错误信息列表，空数组表示通过
   */
  const validate = useCallback(
    (password: string): string[] => {
      if (!policy) return [];
      const errors: string[] = [];
      if (!password) {
        errors.push(t('settings.password.error.empty'));
        return errors;
      }
      if (password.length < policy.minLength) {
        errors.push(t('settings.password.error.minLength', { count: policy.minLength }));
      }
      if (policy.requireUppercase && !/[A-Z]/.test(password)) {
        errors.push(t('settings.password.error.uppercase'));
      }
      if (policy.requireLowercase && !/[a-z]/.test(password)) {
        errors.push(t('settings.password.error.lowercase'));
      }
      if (policy.requireDigit && !/\d/.test(password)) {
        errors.push(t('settings.password.error.digit'));
      }
      if (policy.requireNonAlphanumeric && !/[^A-Za-z0-9]/.test(password)) {
        errors.push(t('settings.password.error.symbol'));
      }
      return errors;
    },
    [policy, t]
  );

  return { policy, loading, validate };
}
