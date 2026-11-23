import axios, { AxiosError, AxiosInstance, AxiosResponse, InternalAxiosRequestConfig, CancelTokenSource, AxiosResponse as AxiosResp } from 'axios';
import { toast } from '../context/ToastContext';
// 本模块不是组件，取词用 `tBound`（读 I18nProvider 绑定的当前语言）。
// `i18n/locale` 只依赖 locales 与 translate，不反向依赖 api，无环。
import { tBound, getBoundLocale } from '../i18n/locale';

const navigateToLogin = () => {
  window.dispatchEvent(new CustomEvent('app:navigate', { detail: { to: '/login', replace: true } }));
};

// 会话失效（401 且刷新令牌也失败）时派发全局事件。
// AuthContext 监听该事件并重置内存登录态（isAuthenticated/user），
// 否则跳转 /login 后会被 App 的“已登录重定向”弹回首页形成无限 401 循环。
export const dispatchAuthExpired = () => {
  window.dispatchEvent(new CustomEvent('auth:expired'));
};

const API_BASE_URL = import.meta.env.VITE_API_BASE_URL || '';

export class ApiError extends Error {
  public status?: number;
  public code?: number;
  public response?: AxiosResp;

  constructor(message: string, options?: { status?: number; code?: number; response?: AxiosResp }) {
    super(message);
    this.name = 'ApiError';
    this.status = options?.status;
    this.code = options?.code;
    this.response = options?.response;
  }
}

const client: AxiosInstance = axios.create({
  baseURL: API_BASE_URL,
  timeout: 30000,
  headers: {
    'Content-Type': 'application/json',
  },
  withCredentials: true,
  xsrfCookieName: 'XSRF-TOKEN',
  xsrfHeaderName: 'X-XSRF-TOKEN',
});

function isApiResponse(data: unknown): data is { success: boolean; code: number; data: unknown; message: string } {
  if (!data || typeof data !== 'object') return false;
  const obj = data as Record<string, unknown>;
  return (
    'success' in obj && typeof obj.success === 'boolean' &&
    'code' in obj && typeof obj.code === 'number' &&
    'data' in obj &&
    'message' in obj && typeof obj.message === 'string'
  );
}

function unwrapApiResponse<T>(response: AxiosResponse<T>): AxiosResponse<T> | Promise<never> {
  const contentType = (response.headers['content-type'] as string) || '';
  if (contentType.includes('application/json') && isApiResponse(response.data)) {
    const apiResp = response.data;
    if (apiResp.success) {
      response.data = apiResp.data as T;
    } else {
      return Promise.reject(new ApiError(apiResp.message || tBound('common.requestFailed'), {
        status: response.status,
        code: apiResp.code,
        response,
      }));
    }
  }
  return response;
}

const refreshClient = axios.create({
  baseURL: API_BASE_URL,
  headers: { 'Content-Type': 'application/json' },
  withCredentials: true,
  // M12: 刷新请求必须带超时，否则悬挂时 isRefreshing 恒为 true、failedQueue 永久阻塞
  timeout: 15_000,
});

refreshClient.interceptors.request.use((config) => {
  // 同主客户端：刷新令牌端点的失败文案（限流/会话失效）也要按界面语言产出
  config.headers['Accept-Language'] = getBoundLocale();
  return config;
});

refreshClient.interceptors.response.use(
  (response) => unwrapApiResponse(response),
  (error) => Promise.reject(error)
);

let isRefreshing = false;
// refresh-token 429 冷却到期时间戳（ms），持久化到 localStorage 以便页面刷新后仍然有效
const REFRESH_COOLDOWN_KEY = 'auth_refresh_cooldown_until';
function loadRefreshCooldown(): number {
  try {
    const v = localStorage.getItem(REFRESH_COOLDOWN_KEY);
    if (!v) return 0;
    const ts = parseInt(v, 10);
    return Number.isFinite(ts) && ts > Date.now() ? ts : 0;
  } catch {
    return 0;
  }
}
function saveRefreshCooldown(ts: number): void {
  try {
    if (ts > Date.now()) {
      localStorage.setItem(REFRESH_COOLDOWN_KEY, String(ts));
    } else {
      localStorage.removeItem(REFRESH_COOLDOWN_KEY);
    }
  } catch {
    // ignore
  }
}
let refreshCooldownUntil = loadRefreshCooldown();
let failedQueue: Array<{
  resolve: (value?: unknown) => void;
  reject: (reason?: unknown) => void;
}> = [];

const processQueue = (error: unknown) => {
  failedQueue.forEach((promise) => {
    if (error) {
      promise.reject(error);
    } else {
      promise.resolve();
    }
  });
  failedQueue = [];
};

/**
 * 清空 refresh-token 等待队列（failedQueue）。
 * 登出/会话失效（auth:expired）时调用：队列中所有等待 refresh 重放的在途请求直接 reject，
 * 防止 refresh-token 成功后回放已失效（已登出/会话已过期）的请求。
 */
export function clearFailedQueue(): void {
  const queue = failedQueue;
  failedQueue = [];
  queue.forEach((promise) => {
    promise.reject(new ApiError(tBound('common.sessionExpiredRequestCancelled'), { status: 401 }));
  });
}

// 短时间窗口内相同请求复用同一个 Promise（抵御 React 18 StrictMode 双执行）
// 窗口 1s：仅覆盖同一事件循环内连续两次 mount 的重复，不影响正常操作后的手动刷新
const INFLIGHT_WINDOW_MS = 1000;
const inflight = new Map<string, { promise: Promise<AxiosResponse>; expireAt: number }>();

function makeCacheKey(config: InternalAxiosRequestConfig): string {
  const url = (config.baseURL || '') + (config.url || '');
  const method = (config.method || 'get').toLowerCase();
  let paramsStr = '';
  try {
    paramsStr = typeof config.params === 'undefined'
      ? ''
      : JSON.stringify(config.params, Object.keys(config.params || {}).sort());
  } catch {
    paramsStr = String(config.params ?? '');
  }
  let dataStr = '';
  if (typeof config.data === 'string') {
    dataStr = config.data;
  } else if (config.data !== undefined && config.data !== null) {
    try {
      dataStr = JSON.stringify(config.data);
    } catch {
      dataStr = String(config.data);
    }
  }
  return `${method}::${url}::${paramsStr}::${dataStr}`;
}

function cleanupExpiredInflight(): void {
  const now = Date.now();
  for (const [key, value] of inflight) {
    if (value.expireAt < now) inflight.delete(key);
  }
}

/**
 * 清空 inflight 去重缓存（M-FE2）。
 * 在登出时调用，避免上一个会话残留的 GET 缓存 Promise 被下一个用户复用导致跨会话数据泄漏。
 */
export function clearInflightCache(): void {
  inflight.clear();
}

client.interceptors.request.use((config) => {
  const cfg = config as InternalAxiosRequestConfig & { _skipDedup?: boolean };
  // 界面语言 → 后端请求语言。后端 RequestLocalization 只认 `?lang=` / `Accept-Language` /
  // `.AspNetCore.Culture` Cookie，而浏览器自动带的 Accept-Language 是**操作系统语言**，
  // 与用户在本系统里切换的界面语言无关：英文界面下后端仍会按 zh-Hans 产出本地化词条、
  // 枚举标签与已本地化的端点文案。Accept-Language 属 CORS 安全列表头，不会触发预检。
  // 有意放在 _skipDedup 早退之前：语言头与去重无关，任何请求都必须带上。
  cfg.headers['Accept-Language'] = getBoundLocale();
  // 只对幂等或常用的 GET 做去重；显式配置 _skipDedup=true 的请求跳过
  if (cfg._skipDedup) return cfg;
  const method = (cfg.method || 'get').toLowerCase();
  if (method !== 'get') return cfg;

  cleanupExpiredInflight();
  const key = makeCacheKey(cfg);
  const existing = inflight.get(key);
  if (existing && existing.expireAt > Date.now()) {
    // 第二次完全相同的请求：直接复用 inflight Promise，不再发真请求
    // L-FE3：浅克隆 data（对象/数组），避免多个消费者共享同一引用导致交叉修改
    cfg.adapter = () =>
      existing.promise.then((res) => {
        const originalData = res.data;
        const clonedData = originalData !== null && typeof originalData === 'object'
          ? (Array.isArray(originalData) ? [...originalData] : { ...originalData })
          : originalData;
        return {
          ...res,
          data: clonedData,
          config: cfg,
          headers: { ...(res.headers || {}) },
        };
      }) as Promise<AxiosResponse>;
  } else {
    // 第一次请求：保存其 adapter 执行后的 Promise 到 inflight
    // 注意：axios 1.8+ 中 config.adapter / axios.defaults.adapter
    // 可能是字符串数组（如 ["xhr","http","fetch"]）而非函数，
    // 必须通过 axios.getAdapter() 解析为可调用函数，否则 "baseAdapter is not a function"
    const originalAdapterRaw = (cfg as unknown as { adapter?: unknown }).adapter;
    cfg.adapter = (adapterConf: InternalAxiosRequestConfig) => {
      let baseAdapter = originalAdapterRaw as (conf: InternalAxiosRequestConfig) => Promise<AxiosResponse>;
      if (typeof originalAdapterRaw !== 'function' && typeof axios.getAdapter === 'function') {
        // axios 1.8+：axios.defaults.adapter 是字符串数组（如 ["xhr","http","fetch"]），
        // 必须通过 axios.getAdapter() 解析为可调用函数，否则 "baseAdapter is not a function"
        baseAdapter = axios.getAdapter(
          (originalAdapterRaw ?? axios.defaults.adapter) as unknown as Parameters<typeof axios.getAdapter>[0],
        ) as unknown as (conf: InternalAxiosRequestConfig) => Promise<AxiosResponse>;
      }
      const p = baseAdapter(adapterConf);
      const tracked = p.then((r) => ({ ...r, headers: { ...(r.headers || {}) } }));
      inflight.set(key, {
        promise: tracked,
        expireAt: Date.now() + INFLIGHT_WINDOW_MS,
      });
      // S8: 请求完成后立即移除去重缓存——去重仅覆盖“并发中”的相同请求（如 StrictMode 双 mount），
      // 避免 1s 窗口内后续刷新请求命中已完成请求的旧快照
      tracked.finally(() => {
        const cur = inflight.get(key);
        if (cur?.promise === tracked) inflight.delete(key);
      });
      // 失败则立刻移除缓存，允许后续重试
      p.catch(() => { inflight.delete(key); });
      return p;
    };
  }
  return cfg;
});

client.interceptors.response.use(
  (response) => unwrapApiResponse(response),
  async (error: AxiosError<{ message?: string; success?: boolean; code?: number; data?: unknown }>) => {
    const originalRequest = error.config as InternalAxiosRequestConfig | undefined;

    const isAuthEndpoint = originalRequest?.url?.startsWith('/api/auth/login')
      || originalRequest?.url?.startsWith('/api/auth/register')
      || originalRequest?.url?.startsWith('/api/auth/forgot-password')
      || originalRequest?.url?.startsWith('/api/auth/reset-password')
      || originalRequest?.url?.startsWith('/api/auth/refresh-token');

    if (
      originalRequest &&
      error.response?.status === 401 &&
      !originalRequest._retry &&
      !isAuthEndpoint
    ) {
      // 冷却期内不再尝试 refresh-token，直接拒绝并跳转登录页
      if (Date.now() < refreshCooldownUntil) {
        localStorage.removeItem('user');
        dispatchAuthExpired();
        if (window.location.pathname !== '/login') {
          navigateToLogin();
        }
        return Promise.reject(error);
      }

      if (isRefreshing) {
        return new Promise((resolve, reject) => {
          failedQueue.push({ resolve, reject });
        }).then(() => client(originalRequest));
      }

      isRefreshing = true;
      originalRequest._retry = true;

      try {
        await refreshClient.post('/api/auth/refresh-token');
        // refresh-token 成功，清除冷却期
        refreshCooldownUntil = 0;
        saveRefreshCooldown(0);
        processQueue(null);
        return client(originalRequest);
      } catch (refreshError) {
        // refresh-token 被限流（429）时设置 60 秒冷却，避免死循环
        // 持久化到 localStorage，页面刷新后冷却期仍然有效
        const refreshStatus = (refreshError as AxiosError)?.response?.status;
        if (refreshStatus === 429) {
          refreshCooldownUntil = Date.now() + 60_000;
          saveRefreshCooldown(refreshCooldownUntil);
        }
        processQueue(refreshError);
        localStorage.removeItem('user');
        dispatchAuthExpired();
        if (window.location.pathname !== '/login') {
          navigateToLogin();
        }
        return Promise.reject(refreshError);
      } finally {
        isRefreshing = false;
      }
    }

    if (error.response?.status === 403) {
      toast.error(tBound('common.forbidden403'));
      return Promise.reject(error);
    }

    const respData = error.response?.data;
    let message = error.message || tBound('common.requestFailed');
    let code: number | undefined;
    if (respData && typeof respData === 'object') {
      if ('message' in respData && typeof respData.message === 'string') {
        message = respData.message;
      } else if ('Message' in respData && typeof (respData as { Message?: string }).Message === 'string') {
        message = (respData as { Message: string }).Message;
      }
      if ('code' in respData && typeof respData.code === 'number') {
        code = respData.code;
      }
    }

    return Promise.reject(new ApiError(message, {
      status: error.response?.status,
      code,
      response: error.response,
    }));
  }
);

export default client;

export function createCancelToken(): CancelTokenSource {
  return axios.CancelToken.source();
}
