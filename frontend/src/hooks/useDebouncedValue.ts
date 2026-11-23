import { useState, useEffect } from 'react';

/**
 * 防抖值 Hook：在指定延迟后返回最新的值。
 * 用于搜索输入等场景，避免每次输入都触发查询/过滤。
 *
 * @param value 原始值
 * @param delay 防抖延迟（毫秒），默认 200ms
 * @returns 防抖后的值
 */
export function useDebouncedValue<T>(value: T, delay = 200): T {
  const [debounced, setDebounced] = useState<T>(value);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delay);
    return () => clearTimeout(timer);
  }, [value, delay]);

  return debounced;
}
