import { ReactNode, useEffect, useRef, useCallback } from 'react';
import { useI18n } from '../context/I18nContext';

interface ModalProps {
  open: boolean;
  title: ReactNode;
  onClose: () => void;
  children: ReactNode;
  footer?: ReactNode;
  size?: 'sm' | 'md' | 'lg' | 'xl';
}

const sizeMap = {
  sm: 'max-w-md',
  md: 'max-w-lg',
  lg: 'max-w-2xl',
  xl: 'max-w-4xl',
};

const FOCUSABLE_SELECTOR =
  'a[href], button:not([disabled]), textarea:not([disabled]), input:not([disabled]), select:not([disabled]), [tabindex]:not([tabindex="-1"])';

export default function Modal({ open, title, onClose, children, footer, size = 'md' }: ModalProps) {
  // 关闭按钮的无障碍标签跟随界面语言（波次 12 队长扩围：它是每页都在的可访问性文案）
  const { t } = useI18n();
  const dialogRef = useRef<HTMLDivElement>(null);
  const previouslyFocused = useRef<HTMLElement | null>(null);
  const previousOverflow = useRef<string>('');

  const handleKeyDown = useCallback(
    (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
        return;
      }
      if (e.key === 'Tab' && dialogRef.current) {
        const focusable = dialogRef.current.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR);
        if (focusable.length === 0) return;
        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        if (e.shiftKey) {
          if (document.activeElement === first || !dialogRef.current.contains(document.activeElement)) {
            e.preventDefault();
            last.focus();
          }
        } else {
          if (document.activeElement === last || !dialogRef.current.contains(document.activeElement)) {
            e.preventDefault();
            first.focus();
          }
        }
      }
    },
    [onClose]
  );

  const hasFocused = useRef(false);
  const isOpening = useRef(false);

  useEffect(() => {
    if (!open) {
      if (isOpening.current) {
        isOpening.current = false;
        hasFocused.current = false;
        previouslyFocused.current?.focus();
      }
      return;
    }
    isOpening.current = true;
    previouslyFocused.current = document.activeElement as HTMLElement;
    previousOverflow.current = document.body.style.overflow;
    document.addEventListener('keydown', handleKeyDown);
    document.body.style.overflow = 'hidden';
    if (!hasFocused.current) {
      hasFocused.current = true;
      requestAnimationFrame(() => {
        if (dialogRef.current) {
          const firstFocusable = dialogRef.current.querySelector<HTMLElement>(FOCUSABLE_SELECTOR);
          if (firstFocusable) {
            firstFocusable.focus();
          } else {
            dialogRef.current.focus();
          }
        }
      });
    }
    return () => {
      document.removeEventListener('keydown', handleKeyDown);
      document.body.style.overflow = previousOverflow.current;
    };
  }, [open, handleKeyDown]);

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center p-4 animate-[fadeIn_0.2s_ease-out]">
      {/* 遮罩 - 多层渐变模糊 */}
      <div
        className="absolute inset-0 bg-gradient-to-br from-slate-900/70 via-slate-900/50 to-indigo-950/60 backdrop-blur-md"
        onClick={onClose}
        aria-hidden="true"
      />
      {/* 装饰性光晕 */}
      <div className="absolute top-1/4 left-1/4 w-96 h-96 bg-primary-500/20 rounded-full blur-3xl pointer-events-none" />
      <div className="absolute bottom-1/4 right-1/4 w-80 h-80 bg-indigo-500/15 rounded-full blur-3xl pointer-events-none" />

      {/* 弹窗主体 */}
      <div
        ref={dialogRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="modal-title"
        tabIndex={-1}
        className={`relative bg-white dark:bg-slate-800 rounded-3xl shadow-[0_25px_50px_-12px_rgba(0,0,0,0.25)] ring-1 ring-gray-200/60 dark:ring-slate-700/60 w-full ${sizeMap[size]} max-h-[92vh] flex flex-col overflow-hidden outline-none animate-[modalIn_0.35s_cubic-bezier(0.34,1.56,0.64,1)]`}
      >
        {/* 顶部高光装饰条 */}
        <div className="absolute top-0 left-0 right-0 h-1 bg-gradient-to-r from-primary-500 via-indigo-500 to-sky-500" />

        {/* 头部 */}
        <div className="relative flex items-center justify-between px-6 py-5 bg-gradient-to-b from-gray-50/80 to-white dark:from-slate-800/80 dark:to-slate-800 border-b border-gray-100/80 dark:border-slate-700/60">
          <div className="min-w-0 pr-4">
            <h3 id="modal-title" className="text-lg font-bold text-gray-900 dark:text-slate-100 tracking-tight">
              {title}
            </h3>
          </div>
          <button
            onClick={onClose}
            aria-label={t('common.closeDialog')}
            className="group relative p-2 text-gray-400 hover:text-gray-700 hover:bg-gray-100 dark:text-slate-400 dark:hover:text-slate-200 dark:hover:bg-slate-700/60 rounded-xl transition-all duration-200 hover:scale-105"
          >
            <svg className="w-5 h-5 transition-transform group-hover:rotate-90" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2.2} aria-hidden="true">
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>

        {/* 内容区 - 自定义滚动条 */}
        <div className="flex-1 overflow-y-auto px-6 py-5 custom-scrollbar">{children}</div>

        {/* 底部 */}
        {footer && (
          <div className="px-6 py-4 border-t border-gray-100/80 dark:border-slate-700/60 flex items-center justify-end gap-2.5 bg-gradient-to-t from-gray-50/70 to-white dark:from-slate-900/50 dark:to-slate-800">
            {footer}
          </div>
        )}
      </div>
    </div>
  );
}
