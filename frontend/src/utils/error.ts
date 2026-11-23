/**
 * 从 API 错误对象中提取用户友好的错误信息。
 *
 * 这是全仓唯一实现。此前 9 个页面/组件各自复制了一份局部副本，且分成两种变体，
 * 现已收口到本文件：调用点一律 `import { getApiErrorMessage } from '../utils/error'`，
 * 局部定义全部删除（调用点写法不变，行为保持不变）。
 *
 * 收口版本取的是变体 2（比变体 1 多一行 `if (err.message) return err.message;`），
 * 因为变体 2 是变体 1 的超集：`ApiError`（`api/client.ts:20`）同时带 `response` 与
 * `message`，而它是在 `client.ts:63` / `client.ts:339` 用 `response.data.message`
 * 同一个字符串构造的，所以「先读 `response.data.message`、读不到再读顶层
 * `err.message`」与变体 1 落到 `e instanceof Error → e.message` 的结果完全一致。
 * 两者只在「不是 Error 实例、且只有顶层 `message` 字段」的对象上才会给出不同答案，
 * 本仓不存在这种错误对象（axios 原始错误与 `ApiError` 都是 `Error` 实例）。
 *
 * @param e 错误对象 (通常来自 axios catch)
 * @param fallback 默认错误信息
 * @returns 提取到的错误信息
 */
export function getApiErrorMessage(e: unknown, fallback: string): string {
  if (e && typeof e === 'object' && 'response' in e) {
    const err = e as { response?: { data?: { message?: string } }; message?: string };
    if (err.response?.data?.message) return err.response.data.message;
    if (err.message) return err.message;
  }
  if (e instanceof Error) return e.message;
  return fallback;
}
