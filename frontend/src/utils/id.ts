/**
 * 生成唯一标识符（React 列表 key 等场景使用）。
 *
 * 优先使用 crypto.randomUUID()（安全上下文下可用），
 * 在非安全上下文（HTTP 非 localhost，如局域网 IP / 域名访问）下
 * 回退到基于时间戳 + 随机数的方案，避免 TypeError 导致功能静默失效。
 */
export function genId(): string {
  try {
    if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
      return crypto.randomUUID();
    }
  } catch {
    // crypto.randomUUID 在非安全上下文下可能抛 TypeError，走 fallback
  }
  // Fallback：时间戳 + 随机串，足够作为 React key 使用
  return `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 11)}`;
}
