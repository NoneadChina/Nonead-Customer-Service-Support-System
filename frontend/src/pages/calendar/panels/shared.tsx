/* eslint-disable react-refresh/only-export-components -- 文件同时导出组件与共享常量/hook，fast refresh 非关注点 */
import { useEffect, useRef } from 'react';
import { useI18n } from '../../../context/I18nContext';

/* =========================================================
   侧边面板共享工具与基础组件
   由 TrashPanel / SubscriptionPanel / OutlookSyncPanel 共用
   ========================================================= */

/** 通用分页大小 */
export const PAGE_SIZE = 20;

/** 浅比较（用于 zustand selector 与 React.memo） */
export const shallowEq = <T,>(a: T, b: T): boolean => {
  if (a === b) return true;
  if (typeof a !== 'object' || !a || typeof b !== 'object' || !b) return false;
  const ka = Object.keys(a as Record<string, unknown>);
  const kb = Object.keys(b as Record<string, unknown>);
  if (ka.length !== kb.length) return false;
  return ka.every((k) => (a as Record<string, unknown>)[k] === (b as Record<string, unknown>)[k]);
};

/** 分页器页码列表（含省略号） */
export const getPageList = (c: number, t: number): (number | 'ellipsis')[] =>
  t <= 7 ? Array.from({ length: t }, (_, i) => i + 1)
  : c <= 4 ? [1, 2, 3, 4, 5, 'ellipsis', t]
  : c >= t - 3 ? [1, 'ellipsis', t - 4, t - 3, t - 2, t - 1, t]
  : [1, 'ellipsis', c - 1, c, c + 1, 'ellipsis', t];

/** 生成 8 位随机十六进制字符串（订阅 URL 用） */
export const rndHex8 = () => Array.from({ length: 8 }, () => '0123456789abcdef'[Math.floor(Math.random() * 16)]).join('');

export type IconName = 'x' | 'search' | 'trash' | 'sparkles' | 'rotate' | 'chevronL' | 'chevronR' | 'copy' | 'check' | 'link' | 'mail' | 'arrows' | 'refresh' | 'unlink' | 'zap' | 'checkCircle' | 'spinner' | 'calendar' | 'users' | 'clock' | 'bell' | 'shield' | 'alertCircle' | 'download' | 'upload' | 'settings' | 'tag' | 'info' | 'folderTrash' | 'filter' | 'archive' | 'sync';

/** 内联 SVG 图标集（避免引入 lucide-react 整包） */
export const Icon = ({ name, className = 'w-4 h-4' }: { name: IconName; className?: string }) => {
  const P = { xmlns: 'http://www.w3.org/2000/svg', viewBox: '0 0 24 24', fill: 'none', stroke: 'currentColor', strokeWidth: 2, strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const };
  const S = (children: React.ReactNode) => <svg className={`${name === 'spinner' ? 'animate-spin ' : ''}${className}`} {...P}>{children}</svg>;
  switch (name) {
    case 'spinner': return S(<><circle className="opacity-25" cx="12" cy="12" r="10" strokeWidth="4" /><path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" /></>);
    case 'x': return S(<><line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" /></>);
    case 'search': return S(<><circle cx="11" cy="11" r="8" /><line x1="21" y1="21" x2="16.65" y2="16.65" /></>);
    case 'trash': return S(<><polyline points="3 6 5 6 21 6" /><path d="M19 6l-2 14a2 2 0 0 1-2 2H9a2 2 0 0 1-2-2L5 6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" /></>);
    case 'sparkles': return S(<path d="M12 3l1.9 5.8a2 2 0 0 0 1.3 1.3L21 12l-5.8 1.9a2 2 0 0 0-1.3 1.3L12 21l-1.9-5.8a2 2 0 0 0-1.3-1.3L3 12l5.8-1.9a2 2 0 0 0 1.3-1.3L12 3z" />);
    case 'rotate': return S(<><polyline points="23 4 23 10 17 10" /><path d="M20.49 15a9 9 0 1 1-2.12-9.36L23 10" /></>);
    case 'chevronL': return S(<polyline points="15 18 9 12 15 6" />);
    case 'chevronR': return S(<polyline points="9 18 15 12 9 6" />);
    case 'copy': return S(<><rect x="9" y="9" width="13" height="13" rx="2" /><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1" /></>);
    case 'check': return S(<polyline points="20 6 9 17 4 12" />);
    case 'link': return S(<><path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71" /><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71" /></>);
    case 'mail': return S(<><path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z" /><polyline points="22,6 12,13 2,6" /></>);
    case 'arrows': return S(<><line x1="17" y1="1" x2="21" y2="5" /><polyline points="15 5 21 5 21 11" /><line x1="7" y1="23" x2="3" y2="19" /><polyline points="9 19 3 19 3 13" /></>);
    case 'refresh': return S(<><polyline points="23 4 23 10 17 10" /><polyline points="1 20 1 14 7 14" /><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" /></>);
    case 'unlink': return S(<><path d="M18.84 12.25l1.72-1.71a5 5 0 0 0-7.07-7.07l-1.72 1.71" /><path d="M5.17 11.75l-1.71 1.71a5 5 0 0 0 7.07 7.07l1.71-1.71" /><line x1="8" y1="2" x2="8" y2="5" /><line x1="2" y1="8" x2="5" y2="8" /><line x1="16" y1="19" x2="16" y2="22" /><line x1="19" y1="16" x2="22" y2="16" /></>);
    case 'zap': return S(<polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2" />);
    case 'checkCircle': return S(<><path d="M22 11.08V12a10 10 0 1 1-5.93-9.14" /><polyline points="22 4 12 14.01 9 11.01" /></>);
    case 'calendar': return S(<><rect x="3" y="4" width="18" height="18" rx="2" ry="2" /><line x1="16" y1="2" x2="16" y2="6" /><line x1="8" y1="2" x2="8" y2="6" /><line x1="3" y1="10" x2="21" y2="10" /></>);
    case 'users': return S(<><path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2" /><circle cx="9" cy="7" r="4" /><path d="M23 21v-2a4 4 0 0 0-3-3.87" /><path d="M16 3.13a4 4 0 0 1 0 7.75" /></>);
    case 'clock': return S(<><circle cx="12" cy="12" r="10" /><polyline points="12 6 12 12 16 14" /></>);
    case 'bell': return S(<><path d="M18 8A6 6 0 0 0 6 8c0 7-3 9-3 9h18s-3-2-3-9" /><path d="M13.73 21a2 2 0 0 1-3.46 0" /></>);
    case 'shield': return S(<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />);
    case 'alertCircle': return S(<><circle cx="12" cy="12" r="10" /><line x1="12" y1="8" x2="12" y2="12" /><line x1="12" y1="16" x2="12.01" y2="16" /></>);
    case 'download': return S(<><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" /><polyline points="7 10 12 15 17 10" /><line x1="12" y1="15" x2="12" y2="3" /></>);
    case 'upload': return S(<><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4" /><polyline points="17 8 12 3 7 8" /><line x1="12" y1="3" x2="12" y2="15" /></>);
    case 'settings': return S(<><circle cx="12" cy="12" r="3" /><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z" /></>);
    case 'tag': return S(<><path d="M20.59 13.41l-7.17 7.17a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82z" /><line x1="7" y1="7" x2="7.01" y2="7" /></>);
    case 'info': return S(<><circle cx="12" cy="12" r="10" /><line x1="12" y1="16" x2="12" y2="12" /><line x1="12" y1="8" x2="12.01" y2="8" /></>);
    case 'folderTrash': return S(<><path d="M3 6a2 2 0 0 1 2-2h3l1 2h8a2 2 0 0 1 2 2v9a3 3 0 0 1-3 3H6a3 3 0 0 1-3-3V6z" /><line x1="9" y1="13" x2="9" y2="17" /><line x1="12" y1="11" x2="12" y2="18" /><line x1="15" y1="13" x2="15" y2="17" /></>);
    case 'filter': return S(<polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3" />);
    case 'archive': return S(<><polyline points="21 8 21 21 3 21 3 8" /><rect x="1" y="3" width="22" height="5" /><line x1="10" y1="12" x2="14" y2="12" /></>);
    case 'sync': return S(<><polyline points="23 4 23 10 17 10" /><polyline points="1 20 1 14 7 14" /><path d="M3.51 9a9 9 0 0 1 14.85-3.36L23 10M1 14l4.64 4.36A9 9 0 0 0 20.49 15" /></>);
  }
};

/** 模态头部主题配置 */
export type ModalHeaderTheme =
  | 'default'
  | 'trash'         // 垃圾箱：红/橙渐变
  | 'subscription'  // 订阅：蓝/青渐变
  | 'outlook'       // Outlook：蓝/靛渐变
  | 'editor';       // 编辑日程：由调用方传颜色

const THEME_CONFIG: Record<Exclude<ModalHeaderTheme, 'editor'>, {
  bar: string;
  iconBox: string;
  iconColor: string;
  labelBg?: string;
}> = {
  default: {
    bar: 'linear-gradient(90deg,#64748b 0%,#94a3b8 50%,#64748b 100%)',
    iconBox: 'bg-slate-100 dark:bg-slate-800 text-slate-500 dark:text-slate-300',
    iconColor: '#64748b',
  },
  trash: {
    bar: 'linear-gradient(90deg,#ef4444 0%,#f97316 40%,#f59e0b 70%,#ef4444 100%)',
    iconBox: 'bg-gradient-to-br from-rose-100 to-orange-100 dark:from-rose-900/40 dark:to-orange-900/30 text-rose-600 dark:text-rose-300',
    iconColor: '#ef4444',
  },
  subscription: {
    bar: 'linear-gradient(90deg,#0ea5e9 0%,#06b6d4 40%,#10b981 70%,#0ea5e9 100%)',
    iconBox: 'bg-gradient-to-br from-sky-100 to-emerald-100 dark:from-sky-900/40 dark:to-emerald-900/30 text-sky-600 dark:text-sky-300',
    iconColor: '#0ea5e9',
  },
  outlook: {
    bar: 'linear-gradient(90deg,#2563eb 0%,#6366f1 40%,#8b5cf6 70%,#2563eb 100%)',
    iconBox: 'bg-gradient-to-br from-blue-100 to-indigo-100 dark:from-blue-900/40 dark:to-indigo-900/30 text-blue-600 dark:text-blue-300',
    iconColor: '#2563eb',
  },
};

/** 模态头部：顶部彩色渐变装饰条 + 图标方块 + 标题 + 副标题 + 右侧自定义动作 + 关闭按钮 */
export function ModalHeader({
  title,
  subtitle,
  onClose,
  icon,
  theme = 'default',
  accentColor,
  rightActions,
}: {
  title: string;
  subtitle?: string;
  onClose: () => void;
  icon?: React.ReactNode;
  theme?: ModalHeaderTheme;
  /** theme='editor' 时传入选中的颜色 */
  accentColor?: string;
  /** 右侧自定义动作（显示在关闭按钮左侧） */
  rightActions?: React.ReactNode;
}) {
  const { t } = useI18n();
  const cfg = theme === 'editor'
    ? {
        bar: `linear-gradient(90deg, ${accentColor ?? '#0ea5e9'} 0%, ${accentColor ?? '#0ea5e9'}cc 50%, ${accentColor ?? '#0ea5e9'}80 100%)`,
        iconBox: '',
      }
    : THEME_CONFIG[theme];

  const iconBoxStyle = theme === 'editor'
    ? { background: `linear-gradient(135deg, ${accentColor ?? '#0ea5e9'}22, ${accentColor ?? '#0ea5e9'}40)`, color: accentColor ?? '#0ea5e9' }
    : undefined;

  return (
    <div className="relative">
      {/* 顶部彩色渐变装饰条 */}
      <div className="h-1.5 w-full" style={{ background: cfg.bar }} />
      <div className="flex items-center justify-between px-6 py-4 border-b border-slate-200 dark:border-slate-700/60 bg-gradient-to-r from-slate-50 via-white to-slate-50 dark:from-slate-800/40 dark:via-slate-900 dark:to-slate-800/40">
        <div className="flex items-center gap-3">
          {icon && (
            <div
              className={`flex items-center justify-center w-10 h-10 rounded-xl shadow-sm ${theme !== 'editor' ? cfg.iconBox : ''}`}
              style={iconBoxStyle}
            >
              {icon}
            </div>
          )}
          <div>
            <h3 className="text-lg font-bold text-slate-800 dark:text-slate-100 leading-tight">{title}</h3>
            {subtitle && <p className="text-xs text-slate-400 dark:text-slate-500 mt-0.5">{subtitle}</p>}
          </div>
        </div>
        <div className="flex items-center gap-1">
          {rightActions}
          <button type="button" onClick={onClose} aria-label={t('common.close')} className="p-2 text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-700 hover:text-slate-600 dark:hover:text-slate-200 hover:rotate-90 rounded-lg transition-all duration-200">
            <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"><line x1="18" y1="6" x2="6" y2="18"/><line x1="6" y1="6" x2="18" y2="18"/></svg>
          </button>
        </div>
      </div>
    </div>
  );
}

/** 蒙层 + 容器：渐变背景、backdrop-blur、圆角 2xl、ring 边框；支持 Escape 关闭与点击蒙层关闭 */
export const Overlay = ({ onClose, children, w = 'max-w-4xl' }: { onClose: () => void; children: React.ReactNode; w?: string }) => {
  const ref = useRef<HTMLDivElement>(null);
  useEffect(() => {
    const h = (e: KeyboardEvent) => { if (e.key === 'Escape') onClose(); };
    window.addEventListener('keydown', h);
    return () => window.removeEventListener('keydown', h);
  }, [onClose]);
  return (
    <div
      ref={ref}
      className="fixed inset-0 z-50 bg-gradient-to-br from-slate-900/60 to-slate-800/50 backdrop-blur-md flex items-center justify-center p-4"
      style={{ animation: 'cal-fade-in-up 180ms cubic-bezier(0.2,0,0,1) forwards' }}
      onClick={(e) => { if (e.target === ref.current) onClose(); }}
    >
      <div
        className={`w-full ${w} bg-white dark:bg-slate-900 rounded-2xl shadow-2xl shadow-slate-900/25 ring-1 ring-slate-200/60 dark:ring-slate-700/50 flex flex-col max-h-[90vh] overflow-hidden`}
        style={{ animation: 'cal-pop-in 240ms ease-out both', willChange: 'transform, opacity' }}
      >
        {children}
      </div>
    </div>
  );
};

/** 确认对话框变体 */
export type ConfirmVariant = 'default' | 'primary' | 'danger' | 'warning' | 'success';

const CONFIRM_VARIANT_CFG: Record<ConfirmVariant, {
  bar: string;
  iconName: IconName;
  iconBox: string;
  btn: string;
}> = {
  default: {
    bar: 'linear-gradient(90deg,#64748b 0%,#94a3b8 50%,#64748b 100%)',
    iconName: 'info',
    iconBox: 'bg-gradient-to-br from-slate-100 to-slate-200 dark:from-slate-800 dark:to-slate-700 text-slate-600 dark:text-slate-300',
    btn: 'bg-gradient-to-r from-slate-700 to-slate-800 hover:from-slate-800 hover:to-slate-900 shadow-md text-white',
  },
  primary: {
    bar: 'linear-gradient(90deg,#0ea5e9 0%,#3b82f6 50%,#0ea5e9 100%)',
    iconName: 'checkCircle',
    iconBox: 'bg-gradient-to-br from-sky-100 to-blue-100 dark:from-sky-900/40 dark:to-blue-900/30 text-sky-600 dark:text-sky-300',
    btn: 'bg-gradient-to-r from-sky-500 to-blue-600 hover:from-sky-600 hover:to-blue-700 shadow-md shadow-sky-500/25 text-white',
  },
  danger: {
    bar: 'linear-gradient(90deg,#ef4444 0%,#f97316 50%,#ef4444 100%)',
    iconName: 'trash',
    iconBox: 'bg-gradient-to-br from-red-100 to-rose-100 dark:from-red-900/40 dark:to-rose-900/30 text-red-600 dark:text-red-400',
    btn: 'bg-gradient-to-r from-red-500 to-rose-600 hover:from-red-600 hover:to-rose-700 shadow-md shadow-red-500/25 text-white',
  },
  warning: {
    bar: 'linear-gradient(90deg,#f59e0b 0%,#f97316 50%,#f59e0b 100%)',
    iconName: 'alertCircle',
    iconBox: 'bg-gradient-to-br from-amber-100 to-orange-100 dark:from-amber-900/40 dark:to-orange-900/30 text-amber-600 dark:text-amber-400',
    btn: 'bg-gradient-to-r from-amber-500 to-orange-600 hover:from-amber-600 hover:to-orange-700 shadow-md shadow-amber-500/25 text-white',
  },
  success: {
    bar: 'linear-gradient(90deg,#10b981 0%,#06b6d4 50%,#10b981 100%)',
    iconName: 'checkCircle',
    iconBox: 'bg-gradient-to-br from-emerald-100 to-teal-100 dark:from-emerald-900/40 dark:to-teal-900/30 text-emerald-600 dark:text-emerald-400',
    btn: 'bg-gradient-to-r from-emerald-500 to-teal-600 hover:from-emerald-600 hover:to-teal-700 shadow-md shadow-emerald-500/25 text-white',
  },
};

/** 通用确认对话框：顶部渐变装饰条 + 图标方块 + 多种语义变体 */
export const ConfirmDialog = ({ open, title, msg, confirmText, cancelText, variant = 'default', loading, onConfirm, onCancel, children }: {
  open: boolean; title: string; msg: string; confirmText: string; cancelText?: string;
  variant?: ConfirmVariant; loading?: boolean; onConfirm: () => void; onCancel: () => void; children?: React.ReactNode;
}) => {
  const { t } = useI18n();
  // 缺省文案在函数体内解析（形参默认值早于 useI18n() 求值）
  const cancelLabel = cancelText ?? t('common.cancel');
  if (!open) return null;
  const cfg = CONFIRM_VARIANT_CFG[variant];
  return (
    <Overlay onClose={onCancel} w="max-w-md">
      {/* 顶部渐变装饰条（与 ModalHeader 风格一致） */}
      <div className="h-1.5 w-full rounded-t-2xl" style={{ background: cfg.bar }} />
      <div className="p-6 space-y-4">
        <div className="flex items-start gap-3">
          <div className={`flex items-center justify-center w-11 h-11 rounded-xl shrink-0 shadow-sm ${cfg.iconBox}`}>
            <Icon name={cfg.iconName} className="w-[22px] h-[22px]" />
          </div>
          <div className="flex-1 pt-0.5">
            <h3 className="text-lg font-bold text-slate-800 dark:text-slate-100 leading-tight">{title}</h3>
            <p className="text-sm text-slate-500 dark:text-slate-400 mt-1 whitespace-pre-line leading-relaxed">{msg}</p>
          </div>
        </div>
        {children}
      </div>
      <div className="flex items-center justify-end gap-2 px-6 py-4 border-t border-slate-200 dark:border-slate-700 bg-gradient-to-r from-slate-50 via-white to-slate-50 dark:from-slate-800/30 dark:via-slate-900 dark:to-slate-800/30 rounded-b-2xl">
        <button
          onClick={onCancel}
          disabled={loading}
          className="px-4 py-2 text-sm font-medium text-slate-600 dark:text-slate-300 bg-white dark:bg-slate-700 border border-slate-300 dark:border-slate-600 rounded-xl hover:bg-slate-50 dark:hover:bg-slate-600 disabled:opacity-50 disabled:cursor-not-allowed transition-all hover:shadow-sm"
        >
          {cancelLabel}
        </button>
        <button
          onClick={onConfirm}
          disabled={loading}
          className={`flex items-center gap-1.5 px-4 py-2 text-sm font-medium rounded-xl disabled:opacity-50 disabled:cursor-not-allowed transition-all active:scale-[0.98] ${cfg.btn}`}
        >
          {loading && <Icon name="spinner" className="w-4 h-4" />}
          {confirmText}
        </button>
      </div>
    </Overlay>
  );
};

/** 共享 Toggle 开关：与 EditorPanel 全天开关风格一致 */
export function ToggleSwitch({
  checked,
  onChange,
  disabled,
  label,
  ariaLabel,
  size = 'md',
}: {
  checked: boolean;
  onChange: (next: boolean) => void;
  disabled?: boolean;
  label?: string;
  ariaLabel?: string;
  size?: 'sm' | 'md';
}) {
  const { t } = useI18n();
  const w = size === 'sm' ? 'h-5 w-9' : 'h-6 w-11';
  const dot = size === 'sm' ? 'h-3.5 w-3.5' : 'h-4 w-4';
  const dotOn = size === 'sm' ? 'translate-x-5' : 'translate-x-6';
  const dotOff = size === 'sm' ? 'translate-x-0.5' : 'translate-x-1';
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked ? 'true' : 'false'}
      aria-label={ariaLabel ?? label ?? t('calendar.panels.shared.toggleAriaLabel')}
      disabled={disabled}
      onClick={() => !disabled && onChange(!checked)}
      className={`relative inline-flex items-center ${w} shrink-0 rounded-full transition-all duration-200 ${
        checked
          ? 'bg-gradient-to-r from-sky-500 to-blue-500 shadow-sm shadow-sky-500/40'
          : 'bg-slate-300 dark:bg-slate-600'
      } ${disabled ? 'opacity-60 cursor-not-allowed' : 'cursor-pointer'}`}
    >
      <span
        className={`inline-block ${dot} transform rounded-full bg-white shadow transition-transform duration-200 ${
          checked ? dotOn : dotOff
        }`}
      />
    </button>
  );
}

/* =========================================================
   P1-C#1: TransientToast — 通用瞬态 toast
   用途：替换 alert/confirm 的结果反馈、剪贴板复制、批量操作、mock API 状态等
   主题：语义化颜色（成功=绿/错误=红/警告=黄/信息=蓝）+ 渐变装饰条 + 可点击关闭
   ========================================================= */
export type TransientToastKind = 'success' | 'error' | 'info' | 'warning';

const TRANSIENT_TOAST_CFG: Record<TransientToastKind, {
  bar: string;
  icon: IconName;
  iconClass: string;
  ring: string;
}> = {
  success: {
    bar: 'from-emerald-400 to-green-500',
    icon: 'checkCircle',
    iconClass: 'text-emerald-600 dark:text-emerald-400',
    ring: 'ring-emerald-100 dark:ring-emerald-950',
  },
  error: {
    bar: 'from-rose-400 to-red-500',
    icon: 'alertCircle',
    iconClass: 'text-rose-600 dark:text-rose-400',
    ring: 'ring-rose-100 dark:ring-rose-950',
  },
  warning: {
    bar: 'from-amber-400 to-orange-500',
    icon: 'bell',
    iconClass: 'text-amber-600 dark:text-amber-400',
    ring: 'ring-amber-100 dark:ring-amber-950',
  },
  info: {
    bar: 'from-sky-400 to-blue-500',
    icon: 'info',
    iconClass: 'text-sky-600 dark:text-sky-400',
    ring: 'ring-sky-100 dark:ring-sky-950',
  },
};

export interface TransientToastProps {
  open: boolean;
  type?: TransientToastKind;
  message: string;
  onClose?: () => void;
  className?: string;
}

export function TransientToast({ open, type = 'info', message, onClose, className }: TransientToastProps) {
  const { t } = useI18n();
  if (!open) return null;
  const cfg = TRANSIENT_TOAST_CFG[type];
  return (
    <div
      role="status"
      aria-live="polite"
      className={[
        'pointer-events-auto relative mx-auto w-[min(92vw,440px)] overflow-hidden rounded-xl bg-white dark:bg-slate-900',
        'ring-1 shadow-xl shadow-slate-900/10 dark:shadow-black/40 backdrop-blur border border-slate-200 dark:border-slate-800',
        'animate-in fade-in slide-in-from-bottom-2 duration-200',
        cfg.ring,
        className ?? '',
      ].join(' ').trim()}
    >
      <div className={`absolute inset-x-0 top-0 h-1 bg-gradient-to-r ${cfg.bar}`} />
      <div className="flex items-start gap-3 px-4 py-3">
        <div className={`mt-0.5 shrink-0 ${cfg.iconClass}`}>
          <Icon name={cfg.icon} className="w-5 h-5" />
        </div>
        <p className="flex-1 text-[13px] leading-snug text-slate-800 dark:text-slate-100 whitespace-pre-wrap break-words">
          {message}
        </p>
        {onClose && (
          <button
            type="button"
            aria-label={t('calendar.common.closeHint')}
            onClick={onClose}
            className="shrink-0 p-1 -mr-1 rounded-md text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-800 transition-colors"
          >
            <Icon name="x" className="w-3.5 h-3.5" />
          </button>
        )}
      </div>
    </div>
  );
}

