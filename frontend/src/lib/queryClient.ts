import { QueryClient, QueryClientConfig } from '@tanstack/react-query';
import { ApiError, dispatchAuthExpired } from '../api/client';

// 通过结构化的错误类型判断网络错误，而非字符串匹配
const isNetworkError = (error: unknown) => {
  if (error instanceof Error) {
    const msg = error.message;
    return (
      msg.includes('Network Error') ||
      msg.includes('Failed to fetch') ||
      msg.includes('net::')
    );
  }
  // axios 网络错误通常没有 response，且不是 ApiError
  return !(error && typeof error === 'object' && 'status' in error && (error as { status: unknown }).status);
};

const shouldRetry = (failureCount: number, error: unknown) => {
  if (failureCount >= 1) return false;
  if (isNetworkError(error)) return true;
  // 使用结构化的 status code 判断，而非字符串匹配
  if (error instanceof ApiError) {
    const status = error.status;
    if (status === 401 || status === 403 || status === 404 || status === 400) {
      return false;
    }
  }
  return false;
};

const defaultOptions: QueryClientConfig = {
  defaultOptions: {
    queries: {
      staleTime: 1000 * 60,
      gcTime: 1000 * 60 * 10,
      refetchOnWindowFocus: false,
      refetchOnReconnect: true,
      retry: shouldRetry,
      retryDelay: (attemptIndex) => Math.min(1000 * 2 ** attemptIndex, 10000),
      throwOnError: false,
    },
    mutations: {
      retry: false,
      throwOnError: false,
      onError: (error) => {
        // 统一基于结构化 status code 处理，不再依赖错误消息字符串匹配
        // 注意：401 的刷新/跳转已在 client.ts 拦截器中处理，这里仅做兜底
        if (error instanceof ApiError) {
          if (error.status === 401) {
            // 兜底：若 client.ts 拦截器未能完成跳转（例如已被 _retry 标记），这里清理本地状态
            if (window.location.pathname !== '/login') {
              localStorage.removeItem('user');
              dispatchAuthExpired();
              window.dispatchEvent(new CustomEvent('app:navigate', { detail: { to: '/login', replace: true } }));
            }
          }
          // 403/404/400 等状态码由调用方在 try/catch 中针对 UI 提示
        }
      },
    },
  },
};

export const queryClient = new QueryClient(defaultOptions);

export default queryClient;
