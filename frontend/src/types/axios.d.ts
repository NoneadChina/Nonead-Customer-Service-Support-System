import 'axios';

declare module 'axios' {
  interface InternalAxiosRequestConfig {
    _retry?: boolean;
    // 是否跳过 inflight 去重（client.ts 中用于显式禁用 GET 请求复用）
    _skipDedup?: boolean;
  }
  interface AxiosRequestConfig {
    _retry?: boolean;
    // 是否跳过 inflight 去重（client.ts 中用于显式禁用 GET 请求复用）
    _skipDedup?: boolean;
  }
}
