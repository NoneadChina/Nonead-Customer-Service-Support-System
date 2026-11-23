/* eslint-disable react-refresh/only-export-components -- 文件同时导出组件与工具函数/hook/常量，fast refresh 非关注点 */
/* =========================================================
   Calendar a11y + reliability 轻量通用能力
   —— ErrorBoundary / 屏幕阅读器公告 / 离线检测 / 对比度
   ========================================================= */
import {
  Component,
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ErrorInfo,
  type ReactNode,
} from 'react';
import { Wifi, WifiOff, RefreshCw, AlertTriangle } from 'lucide-react';
// 本文件既有 class 组件（不能用 hook）又有模块级函数，故一律走 `tBound`：
// 无 Provider 时回落 zh-Hans，与既有单测期望一致（与 `PasswordPolicyHint` 同一手法）。
import { tBound } from '../../i18n/locale';

/* -----------------------------------------------------------
   1. P2-C#1: 错误边界 —— 渲染失败时不炸整个 App，提供重试按钮
   ----------------------------------------------------------- */
interface ErrorBoundaryProps {
  children: ReactNode;
  /** 崩溃后的展示标题 */
  title?: string;
  /** 额外说明 */
  description?: string;
  /** 重试时调用（通常用于刷新数据/重置 store） */
  onRecover?: () => void | Promise<void>;
}
interface ErrorBoundaryState { error: Error | null; }

export class CalendarErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { error: null };
  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { error };
  }
  componentDidCatch(error: Error, info: ErrorInfo) {
    // 仅在浏览器端打印，避免污染 SSR
     
    console.error('[CalendarErrorBoundary] 组件渲染崩溃:', error, info.componentStack);
  }
  private handleReset = () => {
    this.setState({ error: null });
    try { this.props.onRecover?.(); } catch { /* 忽略 */ }
  };
  render() {
    const { error } = this.state;
    if (!error) return this.props.children;
    const { title = tBound('calendar.a11y.crashTitle'), description = tBound('calendar.a11y.crashDescription') } = this.props;
    return (
      <div
        role="alert"
        aria-live="assertive"
        className="flex flex-col items-center justify-center h-full w-full p-6 sm:p-10 text-center"
      >
        <div className="w-14 h-14 rounded-2xl bg-rose-500/10 dark:bg-rose-500/20 text-rose-500 flex items-center justify-center mb-4">
          <AlertTriangle className="w-7 h-7" />
        </div>
        <h2 className="text-lg sm:text-xl font-semibold text-slate-800 dark:text-slate-100 mb-2">{title}</h2>
        <p className="max-w-md text-sm text-slate-600 dark:text-slate-400 mb-5">{description}</p>
        <pre aria-hidden="true" className="max-w-full overflow-auto text-[11px] font-mono bg-slate-100 dark:bg-slate-800/70 text-slate-500 dark:text-slate-400 rounded-lg px-3 py-2 mb-5 max-h-28 line-clamp-4">
          {error.message}
        </pre>
        <button
          type="button"
          onClick={this.handleReset}
          className="inline-flex items-center gap-2 px-4 py-2.5 text-sm font-medium text-white bg-sky-500 hover:bg-sky-600 rounded-lg shadow shadow-sky-500/20 transition"
        >
          <RefreshCw className="w-4 h-4" />
          {tBound('calendar.a11y.recover')}
        </button>
      </div>
    );
  }
}

/* -----------------------------------------------------------
   2. P2-B#2: 屏幕阅读器区域 + 语义公告上下文
      - aria-live="polite" 视觉隐藏容器
      - 提供 announce() 函数写入内容（例如「选中 3 条 / 发现冲突 2 项 / 撤销成功」）
   ----------------------------------------------------------- */
type AnnouncerCtx = (message: string, politeness?: 'polite' | 'assertive') => void;
const A11yAnnounceCtx = createContext<AnnouncerCtx>(() => {/* noop */});

export const useA11yAnnouncer = (): AnnouncerCtx => useContext(A11yAnnounceCtx);

export function CalendarA11yProvider({ children }: { children: ReactNode }) {
  const politeRef = useRef<HTMLDivElement | null>(null);
  const assertiveRef = useRef<HTMLDivElement | null>(null);

  const announce: AnnouncerCtx = useCallback((message, politeness = 'polite') => {
    const el = politeness === 'assertive' ? assertiveRef.current : politeRef.current;
    if (!el) return;
    // 清空再赋值，保证相同内容也会触发朗读
    el.textContent = '';
    requestAnimationFrame(() => { if (el) el.textContent = message; });
  }, []);

  const regionClass = 'sr-only'; /* 视觉隐藏，仅对屏幕阅读器可见（Tailwind 标准 className） */

  const value = useMemo<AnnouncerCtx>(() => announce, [announce]);

  return (
    <A11yAnnounceCtx.Provider value={value}>
      {children}
      {/* P2-B#2 aria-live 语义区域 */}
      <div ref={politeRef} role="status" aria-live="polite" aria-relevant="additions" className={regionClass} />
      <div ref={assertiveRef} role="alert" aria-live="assertive" aria-atomic="true" className={regionClass} />
    </A11yAnnounceCtx.Provider>
  );
}

/* -----------------------------------------------------------
   3. P2-C#2: 在线/离线检测 Hook
      返回 isOnline + 顶部提示条节点（可渲染）
   ----------------------------------------------------------- */
export function useOnlineStatus(
  options: { onStatusChange?: (online: boolean) => void } = {},
): { isOnline: boolean; OfflineBanner: () => JSX.Element | null; assertOnlineForWrite: () => boolean } {
  const [isOnline, setIsOnline] = useState<boolean>(() => {
    if (typeof navigator === 'undefined') return true;
    return navigator.onLine !== false;
  });

  useEffect(() => {
    const on = () => { setIsOnline(true); options.onStatusChange?.(true); };
    const off = () => { setIsOnline(false); options.onStatusChange?.(false); };
    window.addEventListener('online', on);
    window.addEventListener('offline', off);
    return () => {
      window.removeEventListener('online', on);
      window.removeEventListener('offline', off);
    };
  }, [options]);

  const OfflineBanner = () => isOnline ? null : (
    <div
      role="alert"
      aria-live="assertive"
      className="sticky top-0 z-[60] w-full bg-gradient-to-r from-amber-500 to-orange-500 text-white text-sm font-medium py-2 px-4 flex items-center justify-center gap-2 shadow-md"
    >
      <WifiOff className="w-4 h-4" />
      <span>{tBound('calendar.a11y.offlineBanner')}</span>
      <Wifi className="w-4 h-4 ml-2 opacity-80" />
    </div>
  );

  const assertOnlineForWrite = useCallback((): boolean => {
    if (!isOnline) {
      announceOfflineBlock();
      return false;
    }
    return true;
  }, [isOnline]);

  return { isOnline, OfflineBanner, assertOnlineForWrite };
}

/** 离线阻断时的即时屏幕阅读器/弹窗提示（全局 DOM 内轻量 toast，不依赖额外 store） */
function announceOfflineBlock() {
  const id = '__a11y_offline_block_live';
  let el = document.getElementById(id) as HTMLDivElement | null;
  if (!el) {
    el = document.createElement('div');
    el.id = id;
    el.setAttribute('role', 'alert');
    el.setAttribute('aria-live', 'assertive');
    el.className = 'sr-only';
    document.body.appendChild(el);
  }
  el.textContent = '';
  requestAnimationFrame(() => { if (el) el.textContent = tBound('calendar.a11y.offlineBlockedRetry'); });

  // 页面级可视 toast（自消散）
  // M-03：使用 createElement + appendChild 代替字符串 innerHTML，
  //       彻底避免未来修改时若拼接了动态内容而产生 XSS 入口。
  const toastId = '__a11y_offline_block_toast';
  let toast = document.getElementById(toastId) as HTMLDivElement | null;
  if (!toast) {
    toast = document.createElement('div');
    toast.id = toastId;
    toast.setAttribute('role', 'status');
    toast.style.cssText = [
      'position:fixed', 'top:16px', 'left:50%', 'transform:translateX(-50%)', 'z-index:2147483600',
      'padding:10px 16px', 'border-radius:10px', 'background:rgb(234 88 12 / 0.96)', 'color:#fff',
      'font-size:13px', 'font-weight:500', 'box-shadow:0 10px 30px rgba(0,0,0,.2)', 'pointer-events:none',
      'transition:opacity .2s ease',
    ].join(';');

    const span = document.createElement('span');
    span.style.display = 'inline-flex';
    span.style.alignItems = 'center';
    span.style.gap = '8px';

    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('width', '14');
    svg.setAttribute('height', '14');
    svg.setAttribute('viewBox', '0 0 24 24');
    svg.setAttribute('fill', 'none');
    svg.setAttribute('stroke', 'currentColor');
    svg.setAttribute('stroke-width', '2');
    svg.setAttribute('stroke-linecap', 'round');
    svg.setAttribute('stroke-linejoin', 'round');
    svg.setAttribute('aria-hidden', 'true');
    const svgPaths = [
      ['line', { x1: '1', y1: '1', x2: '23', y2: '23' }],
      ['path', { d: 'M16.72 11.06A10.94 10.94 0 0 1 19 12.55' }],
      ['path', { d: 'M5 12.55a10.94 10.94 0 0 1 5.17-2.39' }],
      ['path', { d: 'M10.71 5.05A16 16 0 0 1 22.58 9' }],
      ['path', { d: 'M1.42 9a15.91 15.91 0 0 1 4.7-2.88' }],
      ['path', { d: 'M8.53 16.11a6 6 0 0 1 6.95 0' }],
      ['line', { x1: '12', y1: '20', x2: '12.01', y2: '20' }],
    ] as const;
    for (const [tag, attrs] of svgPaths) {
      const el = document.createElementNS('http://www.w3.org/2000/svg', tag);
      for (const [k, v] of Object.entries(attrs)) el.setAttribute(k, v);
      svg.appendChild(el);
    }
    span.appendChild(svg);
    const label = document.createElement('span');
    label.textContent = tBound('calendar.a11y.offlineBlockedShort');
    span.appendChild(label);

    toast.appendChild(span);
    document.body.appendChild(toast);
    setTimeout(() => { if (toast && document.body.contains(toast)) document.body.removeChild(toast); }, 2600);
  }
}

/* -----------------------------------------------------------
   4. P2-B#3: 对比度修复 —— 给"状态徽章"提供最小可感知对比度 className
      仅做少量常用对；后续可扩展
   ----------------------------------------------------------- */
export const CONTRAST_SAFE_LABEL_CLASS: Record<string, string> = {
  draft:      'text-slate-700 bg-slate-100 border border-slate-300 dark:text-slate-200 dark:bg-slate-700/80 dark:border-slate-600',
  pending:    'text-amber-800 bg-amber-100 border border-amber-300 dark:text-amber-200 dark:bg-amber-950/40 dark:border-amber-800/60',
  in_progress:'text-sky-800   bg-sky-100   border border-sky-300   dark:text-sky-200   dark:bg-sky-950/40   dark:border-sky-800/60',
  completed:  'text-emerald-800 bg-emerald-100 border border-emerald-300 dark:text-emerald-200 dark:bg-emerald-950/40 dark:border-emerald-800/60',
  cancelled:  'text-rose-800  bg-rose-100  border border-rose-300  dark:text-rose-200  dark:bg-rose-950/40  dark:border-rose-800/60',
  conflict:   'text-rose-800  bg-rose-100  border border-rose-300  dark:text-rose-200  dark:bg-rose-950/40  dark:border-rose-800/60',
};

/* -----------------------------------------------------------
   5. P2-B#1: 简易 ARIA 工具函数 —— 生成唯一 id（避免 hydrate 不一致）
   ----------------------------------------------------------- */
let gUidSeed = 0;
export const ariaId = (prefix: string) => `${prefix}-${typeof performance !== 'undefined' && typeof performance.now === 'function'
  ? Math.floor(performance.now()).toString(36) : 's'}-${(++gUidSeed).toString(36)}`;
