import { createContext, useContext, useEffect, useState, ReactNode } from 'react';
import { authApi, profileApi } from '../api';
import { clearFailedQueue, clearInflightCache } from '../api/client';
import { tBound } from '../i18n/locale';
import type { LoginRequest, UserResponse } from '../api/types';
import signalRService from '../services/signalR';
import { setAvatar } from '../hooks/usePreferences';
import { clearPersistedUndoRedoForLogout, clearPersistedViewState, clearPendingPatchesForLogout, resetCalendarStore } from '../pages/calendar/store';

interface MinimalUserInfo {
  id: number;
  // 安全说明（M-FE1）：仅存储登录态标志与用户 ID，不存储 email/姓名/头像等 PII，
  // 避免本地存储泄漏个人身份信息。完整用户资料每次从后端 profileApi.get() 拉取。
  authenticated: true;
}

interface AuthContextValue {
  user: UserResponse | null;
  // 登录态标志（语义清晰）：true 表示已登录（HttpOnly Cookie 已通过后端校验），false 表示未登录
  isAuthenticated: boolean;
  loading: boolean;
  isInitializing: boolean;
  login: (req: LoginRequest) => Promise<void>;
  logout: () => void;
  updateUser: (user: UserResponse) => void;
  refreshUser: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

function extractMinimalUserInfo(user: UserResponse): MinimalUserInfo {
  return {
    id: user.id,
    authenticated: true,
  };
}

// 清理知识库草稿（noneadcss_kb_draft_u 前缀），避免跨用户串草稿
function clearKnowledgeBaseDrafts(): void {
  for (let i = localStorage.length - 1; i >= 0; i--) {
    const key = localStorage.key(i);
    if (key && key.startsWith('noneadcss_kb_draft_u')) {
      localStorage.removeItem(key);
    }
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserResponse | null>(null);
  // 登录态标志（boolean）：true 表示已登录，false 表示未登录。
  // 仅保存在内存中（不写入 localStorage），实际的 JWT 已迁移至 HttpOnly Cookie，前端 JS 无法读取。
  const [isAuthenticated, setIsAuthenticated] = useState<boolean>(false);
  const [loading, setLoading] = useState(true);
  const [isInitializing, setIsInitializing] = useState(true);

  const refreshUser = async (): Promise<void> => {
    const fresh = await profileApi.get();
    localStorage.setItem('user', JSON.stringify(extractMinimalUserInfo(fresh)));
    setAvatar(fresh.id, fresh.avatar ?? null);
    setUser(fresh);
  };

  useEffect(() => {
    let cancelled = false;
    // 登录态判断改为检查 localStorage 中是否有基本用户信息
    // 权限数据（role/permissions/stringPermissions）不存储在 localStorage 中
    // 每次页面加载都必须从后端获取完整的用户信息（含权限）
    const storedUser = localStorage.getItem('user');
    if (storedUser) {
      try {
        const parsedMinimal = JSON.parse(storedUser) as MinimalUserInfo;
        // 只做基本结构校验，防止用户篡改 localStorage 中的数据
        if (!parsedMinimal.id || !parsedMinimal.authenticated) {
          throw new Error('Invalid user data');
        }
        // 必须从后端获取完整用户信息（含权限），同时验证 HttpOnly Cookie 是否仍然有效
        // 在获取到后端权限数据前，保持 loading=true 避免受保护页面提前放行
        profileApi.get()
          .then((fresh) => {
            if (cancelled) return;
            localStorage.setItem('user', JSON.stringify(extractMinimalUserInfo(fresh)));
            setAvatar(fresh.id, fresh.avatar ?? null);
            setUser(fresh);
            // Cookie 验证通过，标记为已登录
            setIsAuthenticated(true);
            // 登录态恢复后连接 SignalR
            signalRService.connect().catch((err: unknown) => console.warn('Signalr 连接失败:', err));
          })
          .catch(() => {
            if (cancelled) return;
            // Cookie 无效或已过期，清理本地状态
            localStorage.removeItem('user');
            setUser(null);
            setIsAuthenticated(false);
          })
          .finally(() => {
            if (cancelled) return;
            setLoading(false);
            setIsInitializing(false);
          });
        return () => { cancelled = true; };
      } catch {
        localStorage.removeItem('user');
      }
    }
    setLoading(false);
    setIsInitializing(false);
    return () => { cancelled = true; };
  }, []);

  useEffect(() => {
    // 会话失效（401 + 刷新令牌失败）时，client.ts 会派发 auth:expired 事件。
    // 必须重置内存登录态，否则跳转 /login 后 App 的“已登录重定向”会把用户弹回首页，
    // 再次请求业务接口又 401 → 无限重定向循环。
    const handleAuthExpired = () => {
      signalRService.disconnect();
      // 清空 inflight GET 去重缓存（M-FE2），避免过期请求回放导致跨会话数据泄漏
      clearInflightCache();
      // 清空 refresh-token 等待队列，防止 refresh 成功后回放已失效请求
      clearFailedQueue();
      // 清理知识库草稿，避免跨用户串草稿
      clearKnowledgeBaseDrafts();
      // T000119: 会话失效（401）同样重置日历 Zustand 内存 store，避免跨账户残留
      resetCalendarStore();
      setIsAuthenticated(false);
      setUser(null);
    };
    window.addEventListener('auth:expired', handleAuthExpired);
    return () => window.removeEventListener('auth:expired', handleAuthExpired);
  }, []);

  const login = async (req: LoginRequest) => {
    const resp = await authApi.login(req);
    if (!resp.success || !resp.token || !resp.user) {
      // 兜底文案走真源（`Login.tsx` 会把这里抛出的 message 显示给用户；后端 errorMessage 优先）
      throw new Error(resp.errorMessage || tBound('auth.login.failed'));
    }
    // 后端会通过 Set-Cookie 头设置 HttpOnly Cookie，前端无需也无法读取
    // 登录态标志仅在内存中保留（不再写入 localStorage），用于跟踪登录态
    // localStorage 只存储基本用户信息，不存储权限数据（role/permissions/stringPermissions）
    localStorage.setItem('user', JSON.stringify(extractMinimalUserInfo(resp.user)));
    setAvatar(resp.user.id, resp.user.avatar ?? null);
    setIsAuthenticated(true);
    setUser(resp.user);
    // 登录成功后连接 SignalR
    signalRService.connect().catch((err: unknown) => console.warn('SignalR 连接失败:', err));
  };

  const logout = async () => {
    try {
      await authApi.logout();
    } catch (err: unknown) {
      console.error('登出请求失败:', err);
    } finally {
      signalRService.disconnect();
      // 清空 inflight GET 去重缓存（M-FE2），避免跨会话数据泄漏
      clearInflightCache();
      // 清空 refresh-token 等待队列，防止 refresh 成功后回放已失效请求
      clearFailedQueue();
      localStorage.removeItem('user');
      localStorage.removeItem('lastRoute');
      // 清理知识库草稿，避免跨用户串草稿
      clearKnowledgeBaseDrafts();
      for (const prefix of ['noneadcss:', 'freescout:', 'ncss:']) {
        for (let i = localStorage.length - 1; i >= 0; i--) {
          const key = localStorage.key(i);
          if (key && key.startsWith(prefix)) {
            localStorage.removeItem(key);
          }
        }
      }
      // P2P3 §3.3 #8.2: 登出清理 calendar:undo:* 前缀键
      clearPersistedUndoRedoForLogout();
      // 登出清理日历拖拽挂起补丁（calendar:pendingPatch:* 前缀键），避免跨用户串数据
      clearPendingPatchesForLogout();
      // 登出清理日历视图状态，避免跨用户串数据
      clearPersistedViewState();
      // T000119: 登出/切换账户时重置日历 Zustand 内存 store，避免上一账户日程残留在新账户视图中
      resetCalendarStore();
      for (let i = sessionStorage.length - 1; i >= 0; i--) {
        const key = sessionStorage.key(i);
        if (key && (key.startsWith('noneadcss:') || key.startsWith('freescout:') || key.startsWith('ncss:'))) {
          sessionStorage.removeItem(key);
        }
      }
      setIsAuthenticated(false);
      setUser(null);
    }
  };

  const updateUser = (updated: UserResponse) => {
    localStorage.setItem('user', JSON.stringify(extractMinimalUserInfo(updated)));
    setAvatar(updated.id, updated.avatar ?? null);
    // 若 API 响应未包含 stringPermissions（如旧版后端），保留当前已有的权限列表，
    // 避免更新头像/资料后丢失权限导致菜单消失
    if ((!updated.stringPermissions || updated.stringPermissions.length === 0) && user?.stringPermissions) {
      updated.stringPermissions = user.stringPermissions;
    }
    setUser(updated);
  };

  return (
    <AuthContext.Provider value={{ user, isAuthenticated, loading, isInitializing, login, logout, updateUser, refreshUser }}>
      {children}
    </AuthContext.Provider>
  );
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth 必须在 AuthProvider 内部使用'); // i18n-exempt: 开发者诊断消息，非用户可见文案
  return ctx;
}
