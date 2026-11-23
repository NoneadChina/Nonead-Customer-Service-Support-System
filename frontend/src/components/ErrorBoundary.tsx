import { Component, ErrorInfo, ReactNode } from 'react';
import { isLazyChunkLoadError } from '../lib/lazyRetry';
import { tBound } from '../i18n/locale';

interface ErrorBoundaryProps {
  children: ReactNode;
}

interface ErrorBoundaryState {
  hasError: boolean;
  error?: Error;
  /** 检测到是 chunk 加载失败（大概率是版本更新场景）→ 展示专门 UI 并自动刷新 */
  isChunkMismatch: boolean;
}

export default class ErrorBoundary extends Component<
  ErrorBoundaryProps,
  ErrorBoundaryState
> {
  private autoReloadTimer: number | null = null;

  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = { hasError: false, isChunkMismatch: false };
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    const flagFromLazyRetry = (window as unknown as { __spaChunkMismatchPendingReload?: boolean })
      .__spaChunkMismatchPendingReload;
    const isChunk = !!flagFromLazyRetry || isLazyChunkLoadError(error);
    return { hasError: true, error, isChunkMismatch: isChunk };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    // 保留到控制台，方便开发/运维事后排查
    console.error('[ErrorBoundary] caught an error:', error, errorInfo);
  }

  componentDidMount(): void {
    this.scheduleAutoReloadIfChunk();
  }

  componentDidUpdate(_prevProps: ErrorBoundaryProps, prevState: ErrorBoundaryState): void {
    // hasError 变 true / isChunkMismatch 变 true 的瞬间开定时器
    if (!prevState.hasError && this.state.hasError) {
      this.scheduleAutoReloadIfChunk();
    }
  }

  componentWillUnmount(): void {
    if (this.autoReloadTimer !== null) {
      window.clearTimeout(this.autoReloadTimer);
      this.autoReloadTimer = null;
    }
  }

  private scheduleAutoReloadIfChunk(): void {
    if (this.autoReloadTimer !== null) return;
    if (!this.state.hasError || !this.state.isChunkMismatch) return;
    // 600ms 是"让用户看清提示"的一个小延迟，避免闪屏
    this.autoReloadTimer = window.setTimeout(() => {
      // 只在检测到版本不匹配（chunk hash 对不上）时才走这里
      try {
        (window as unknown as { __spaChunkMismatchPendingReload?: boolean }).__spaChunkMismatchPendingReload = false;
      } catch {
        /* noop */
      }
      window.location.reload();
    }, 600);
  }

  handleReload = (): void => {
    window.location.reload();
  };

  /**
   * 对于非 chunk 类错误（比如组件内部某处临时数据非法导致 throw），尝试重置错误状态
   * 让 React 重新渲染子树，通常就能恢复，无需全页 reload（避免用户丢失正在编辑的内容）。
   */
  handleTryReset = (): void => {
    if (this.autoReloadTimer !== null) {
      window.clearTimeout(this.autoReloadTimer);
      this.autoReloadTimer = null;
    }
    this.setState({ hasError: false, error: undefined, isChunkMismatch: false });
  };

  render(): ReactNode {
    if (!this.state.hasError) {
      return this.props.children;
    }

    const isDev = import.meta.env.DEV;
    const { error, isChunkMismatch } = this.state;

    if (isChunkMismatch) {
      return (
        <div className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-slate-900 px-4">
          <div className="max-w-xl w-full bg-white dark:bg-slate-800 rounded-2xl shadow-lg p-8 text-center">
            <div className="w-16 h-16 mx-auto mb-4 rounded-full bg-sky-100 dark:bg-sky-900/30 flex items-center justify-center animate-[spin_1.4s_linear_infinite]">
              <svg
                className="w-8 h-8 text-sky-600 dark:text-sky-300"
                fill="none"
                viewBox="0 0 24 24"
                stroke="currentColor"
                strokeWidth={2}
              >
                <path
                  strokeLinecap="round"
                  strokeLinejoin="round"
                  d="M4 4v5h5M20 20v-5h-5M4 9a9 9 0 0 1 14.2-4.2M20 15a9 9 0 0 1-14.2 4.2"
                />
              </svg>
            </div>
            <h1 className="text-xl font-semibold text-gray-900 dark:text-slate-100 mb-2">
              {tBound('common.shell.updateDetected')}
            </h1>
            <p className="text-gray-500 dark:text-slate-400 text-sm mb-2">
              {tBound('common.shell.updateRefreshing')}
            </p>
            <p className="text-gray-400 dark:text-slate-500 text-xs">
              {tBound('common.shell.updateManualPrefix')}
              <button
                onClick={this.handleReload}
                className="mx-1 underline text-sky-600 dark:text-sky-400 hover:text-sky-700 dark:hover:text-sky-300"
              >
                {tBound('common.shell.updateManualAction')}
              </button>
            </p>
          </div>
        </div>
      );
    }

    return (
      <div className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-slate-900 px-4">
        <div className="max-w-2xl w-full bg-white dark:bg-slate-800 rounded-2xl shadow-lg p-8 text-center">
          <div className="w-16 h-16 mx-auto mb-4 rounded-full bg-red-100 dark:bg-red-900/30 flex items-center justify-center">
            <svg
              className="w-8 h-8 text-red-500"
              fill="none"
              viewBox="0 0 24 24"
              stroke="currentColor"
              strokeWidth={2}
            >
              <path
                strokeLinecap="round"
                strokeLinejoin="round"
                d="M12 9v2m0 4h.01m-6.938 4h13.856c1.54 0 2.502-1.667 1.732-3L13.732 4c-.77-1.333-2.694-1.333-3.464 0L3.34 16c-.77 1.333.192 3 1.732 3z"
              />
            </svg>
          </div>
          <h1 className="text-xl font-semibold text-gray-900 dark:text-slate-100 mb-2">
            {tBound('common.shell.errorTitle')}
          </h1>
          <p className="text-gray-500 dark:text-slate-400 text-sm mb-6">
            {tBound('common.shell.errorDescription')}
          </p>
          {isDev && error && (
            <div className="mb-6 text-left">
              <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-4 overflow-auto max-h-96">
                <p className="text-red-700 dark:text-red-300 font-mono text-sm font-semibold mb-2">
                  {error.name}: {error.message}
                </p>
                {error.stack && (
                  <pre className="text-red-600 dark:text-red-400 font-mono text-xs whitespace-pre-wrap break-words">
                    {error.stack}
                  </pre>
                )}
              </div>
            </div>
          )}
          <div className="flex items-center justify-center gap-3 flex-wrap">
            <button
              onClick={this.handleTryReset}
              className="inline-flex items-center px-5 py-2.5 rounded-xl bg-slate-700 hover:bg-slate-800 dark:bg-slate-600 dark:hover:bg-slate-500 text-white font-medium transition-colors"
            >
              {tBound('common.shell.errorRetry')}
            </button>
            <button
              onClick={this.handleReload}
              className="inline-flex items-center px-5 py-2.5 rounded-xl bg-primary-600 hover:bg-primary-700 text-white font-medium transition-colors"
            >
              {tBound('common.shell.errorReload')}
            </button>
          </div>
        </div>
      </div>
    );
  }
}
