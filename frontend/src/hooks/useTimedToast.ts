import { useState, useRef, useEffect, useCallback } from 'react';

export interface ToastMessage {
  type: 'success' | 'error' | 'info' | 'warning';
  text: string;
}

/**
 * 自动过期的 Toast/Message 管理 Hook。
 * 设置消息后自动在指定时间后清除，组件卸载时自动清理定时器。
 * 替代各页面中重复的 messageTimeoutRef / toastTimeoutRef + setTimeout 模式。
 *
 * @param duration 自动清除时间（毫秒），默认 3000
 * @returns { message, show, clear, setMessage }
 *   - message: 当前消息状态
 *   - show: 设置消息并启动自动清除定时器
 *   - clear: 立即清除消息并取消定时器
 *   - setMessage: 直接设置消息（不启动定时器，用于特殊场景）
 */
export function useTimedToast(duration = 3000) {
  const [message, setMessage] = useState<ToastMessage | null>(null);
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const clearTimer = useCallback(() => {
    if (timerRef.current) {
      clearTimeout(timerRef.current);
      timerRef.current = null;
    }
  }, []);

  const show = useCallback(
    (msg: ToastMessage) => {
      clearTimer();
      setMessage(msg);
      timerRef.current = setTimeout(() => {
        setMessage(null);
        timerRef.current = null;
      }, duration);
    },
    [duration, clearTimer],
  );

  const clear = useCallback(() => {
    clearTimer();
    setMessage(null);
  }, [clearTimer]);

  // 组件卸载时清理定时器，防止内存泄漏
  useEffect(() => {
    return clearTimer;
  }, [clearTimer]);

  return { message, show, clear, setMessage };
}
