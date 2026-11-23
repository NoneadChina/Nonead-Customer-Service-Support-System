/* eslint-disable react-refresh/only-export-components -- Provider 与 toast 对象/useToast hook 同属 context 模块，fast refresh 非关注点 */
import { createContext, useContext, useState, useCallback, useRef, useEffect, useMemo, ReactNode } from 'react';
import { CheckCircle, XCircle, Info, AlertTriangle, X } from 'lucide-react';
// 用 `tBound` 而不是 `useI18n`：`I18nContext → AuthContext → api/client → ToastContext`
// 已是既有的单向链，再加 `ToastContext → I18nContext` 会闭合成环。`tBound` 读的是
// I18nProvider 在渲染体里绑定的当前语言，Provider 每次重渲染都会重新求值。
import { tBound } from '../i18n/locale';

type ToastType = 'success' | 'error' | 'info' | 'warning';

interface ToastOptions {
  onClick?: () => void;
}

interface ToastItem {
  id: number;
  type: ToastType;
  message: string;
  onClick?: () => void;
}

interface ToastContextValue {
  success: (message: string, opts?: ToastOptions) => void;
  error: (message: string, opts?: ToastOptions) => void;
  info: (message: string, opts?: ToastOptions) => void;
  warning: (message: string, opts?: ToastOptions) => void;
}

const ToastContext = createContext<ToastContextValue | undefined>(undefined);

let imperativeToastApi: ToastContextValue | null = null;

export const toast: ToastContextValue = {
  success: (message, opts) => imperativeToastApi?.success(message, opts),
  error: (message, opts) => imperativeToastApi?.error(message, opts),
  info: (message, opts) => imperativeToastApi?.info(message, opts),
  warning: (message, opts) => imperativeToastApi?.warning(message, opts),
};

const toastStyles: Record<ToastType, { bg: string; icon: React.ReactNode }> = {
  success: {
    bg: 'bg-green-500',
    icon: <CheckCircle className="w-5 h-5 shrink-0" />,
  },
  error: {
    bg: 'bg-red-500',
    icon: <XCircle className="w-5 h-5 shrink-0" />,
  },
  info: {
    bg: 'bg-blue-500',
    icon: <Info className="w-5 h-5 shrink-0" />,
  },
  warning: {
    bg: 'bg-amber-500',
    icon: <AlertTriangle className="w-5 h-5 shrink-0" />,
  },
};

export function ToastProvider({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<ToastItem[]>([]);
  const nextIdRef = useRef(0);
  const timersRef = useRef<Map<number, ReturnType<typeof setTimeout>>>(new Map());

  const removeToast = useCallback((id: number) => {
    // 清理对应的自动消失定时器，避免手动关闭后定时器仍然触发
    const timer = timersRef.current.get(id);
    if (timer) {
      clearTimeout(timer);
      timersRef.current.delete(id);
    }
    setToasts((prev) => prev.filter((t) => t.id !== id));
  }, []);

  const addToast = useCallback((type: ToastType, message: string, opts?: ToastOptions) => {
    const id = nextIdRef.current++;
    setToasts((prev) => [...prev, { id, type, message, onClick: opts?.onClick }]);
    const timer = setTimeout(() => {
      timersRef.current.delete(id);
      setToasts((prev) => prev.filter((t) => t.id !== id));
    }, 3000);
    timersRef.current.set(id, timer);
  }, []);

  // 组件卸载时清理所有未完成的定时器，防止内存泄漏
  useEffect(() => {
    const timers = timersRef.current;
    return () => {
      timers.forEach((timer) => clearTimeout(timer));
      timers.clear();
    };
  }, []);

  const toastApi = useMemo<ToastContextValue>(() => ({
    success: (message, opts) => addToast('success', message, opts),
    error: (message, opts) => addToast('error', message, opts),
    info: (message, opts) => addToast('info', message, opts),
    warning: (message, opts) => addToast('warning', message, opts),
  }), [addToast]);

  useEffect(() => {
    imperativeToastApi = toastApi;
    return () => {
      imperativeToastApi = null;
    };
  }, [toastApi]);

  return (
    <ToastContext.Provider value={toastApi}>
      {children}
      <div
        className="fixed top-4 right-4 z-50 flex flex-col gap-2"
        role="status"
        aria-live="polite"
        aria-atomic="false"
      >
        {toasts.map((toast) => {
          const style = toastStyles[toast.type];
          return (
            <div
              key={toast.id}
              className={`${style.bg} text-white px-4 py-3 rounded-lg shadow-lg flex items-center gap-3 min-w-[280px] max-w-md animate-in slide-in-from-right duration-200${toast.onClick ? ' cursor-pointer' : ''}`}
              role={toast.type === 'error' ? 'alert' : undefined}
              onClick={toast.onClick}
            >
              {style.icon}
              <span className="text-sm flex-1">{toast.message}</span>
              <button
                onClick={(e) => { e.stopPropagation(); removeToast(toast.id); }}
                className="shrink-0 text-white/80 hover:text-white transition-colors"
                aria-label={tBound('common.closeNotification')}
              >
                <X className="w-4 h-4" />
              </button>
            </div>
          );
        })}
      </div>
    </ToastContext.Provider>
  );
}

export function useToast() {
  const ctx = useContext(ToastContext);
  if (!ctx) throw new Error('useToast 必须在 ToastProvider 内部使用'); // i18n-exempt: 开发者诊断消息，非用户可见文案
  return ctx;
}
