import { useEffect, useRef } from 'react';
import { usePreferences } from './usePreferences';

interface UseAutoRefreshOptions {
  enabled?: boolean;
  defaultInterval?: number;
}

export function useAutoRefresh(
  refresh: () => void,
  deps: unknown[] = [],
  options: UseAutoRefreshOptions = {}
) {
  const { enabled = true, defaultInterval } = options;
  const { preferences } = usePreferences();
  const refreshRef = useRef(refresh);

  refreshRef.current = refresh;

  const interval = defaultInterval ?? preferences.autoRefreshInterval;

  useEffect(() => {
    if (!enabled || interval <= 0) return;

    const timer = setInterval(() => {
      refreshRef.current();
    }, interval * 1000);

    return () => clearInterval(timer);
    // deps 为调用方传入的动态依赖数组，无法静态推断其元素，故关闭该项静态检查
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [interval, enabled, ...deps]);
}
