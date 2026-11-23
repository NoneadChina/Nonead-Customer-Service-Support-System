import { lazy, type LazyExoticComponent, type ComponentType } from 'react';

/**
 * 懒加载模块（React.lazy / 动态 import()）的失败重试工具。
 *
 * 主根因：SPA 做滚动部署时，新构建的 chunk hash 与用户浏览器中正在运行的旧 index.html
 * 引用的旧 hash 不一致 → 点击侧边栏跳转到未访问过的路由时，React.lazy 触发 import() 下载
 * 旧 chunk → 404 / Failed to fetch dynamically imported module → Promise reject →
 * 被最外层 ErrorBoundary 捕获 → 「页面发生错误，请尝试重新加载页面」。
 * 点击「重新加载」后拿到新版 index.html（正确 hash），自然恢复。
 *
 * 防御策略：
 *   1. 对 import() Promise 做最多 3 次指数退避重试（解决偶发网络抖动导致的 fetch 失败）。
 *   2. 3 次重试后仍然失败：说明大概率是 chunk hash 对不上（版本更新场景）。在 window 上做
 *      标记，让 ErrorBoundary 识别到这种错误时直接自动刷新页面，不用用户手动点击按钮。
 */

export function isLazyChunkLoadError(e: unknown): boolean {
  if (!(e instanceof Error)) return false;
  const msg = (e.message || '').toLowerCase();
  return (
    /failed to fetch dynamically imported module/i.test(e.message) ||
    /loading chunk .* failed/i.test(msg) ||
    /(?:chunk|module) .* failed to load/i.test(msg) ||
    /network error/i.test(msg) ||
    /load(?:ing)? script error/i.test(msg) ||
    e.name === 'ChunkLoadError'
  );
}

/**
 * 把一个 Promise<{ default: T }> 的 import 工厂
 * 变成「最多 3 次指数退避重试 + 标记版本不匹配」的工厂。
 */
export function retryImport<T>(
  loader: () => Promise<T>,
  options?: { maxRetries?: number; initialDelayMs?: number },
): Promise<T> {
  const maxRetries = options?.maxRetries ?? 3;
  const initialDelayMs = options?.initialDelayMs ?? 120;

  function attempt(retryNo: number): Promise<T> {
    return loader().catch((err) => {
      if (!isLazyChunkLoadError(err)) {
        // 模块内部语法/运行时错误 → 不重试
        return Promise.reject(err);
      }
      if (retryNo < maxRetries) {
        const delay = initialDelayMs * 2 ** retryNo; // 120 / 240 / 480 ms
        return new Promise<T>((resolve, reject) => {
          window.setTimeout(() => {
            attempt(retryNo + 1).then(resolve, reject);
          }, delay);
        });
      }
      // 判定为"版本不匹配"，ErrorBoundary 看到这个标记会直接自动刷新
      try {
        (window as unknown as { __spaChunkMismatchPendingReload?: boolean }).__spaChunkMismatchPendingReload = true;
      } catch {
        /* noop */
      }
      return Promise.reject(err);
    });
  }

  return attempt(0);
}

/**
 * 给路由配置批量用的便捷包装：
 *   替换前：const Calendar = lazy(() => import('../pages/Calendar'));
 *   替换后：const Calendar = lazyWithRetry(() => import('../pages/Calendar'));
 */
export function lazyWithRetry<T extends ComponentType>(
  loader: () => Promise<{ default: T }>,
): LazyExoticComponent<T> {
  return lazy(() => retryImport(loader));
}
