import { useState, useEffect, useCallback } from 'react';

// 安全说明（M-FE1）：
// 本 Hook 使用 localStorage 持久化用户偏好设置（主题、分页大小、工作时间等），
// 存储的 key 形如 `noneadcss:preferences:{userId}` / `noneadcss:avatar:{userId}`。
// 安全考虑：
// 1. 仅存储 UI 偏好与非敏感的用户 ID，不存储 email/姓名/权限/Token 等敏感信息；
// 2. 实际的 JWT 已迁移至 HttpOnly Cookie，前端 JS 无法读取，因此 localStorage 不含认证凭据；
// 3. 登出时由 AuthContext.logout() 统一清理所有 `noneadcss:` / `freescout:` / `ncss:` 前缀的 key
//    （见 AuthContext.tsx 中 logout 函数的前缀遍历逻辑），避免跨会话/跨用户数据泄漏；
// 4. 该前缀清理同样覆盖 `noneadcss:preferences:*` 与 `noneadcss:avatar:*`，无需在此处重复处理。

// 用户偏好设置类型
export interface UserPreferences {
  defaultMailboxId: number | null;
  pageSize: number;
  timezone: string;
  language: string;
  theme: 'light' | 'dark' | 'system';
  emailNotifications: boolean;
  desktopNotifications: boolean;
  soundEnabled: boolean;
  autoRefreshInterval: number; // 秒，0 表示不自动刷新
  sessionAutoClose: number; // 分钟，0 表示不自动关闭
  workdays: number[]; // 工作日，0=周日，1=周一，...，6=周六
  workStart: string; // 工作开始时间，格式 HH:mm
  workEnd: string; // 工作结束时间，格式 HH:mm
}

const defaultPreferences: UserPreferences = {
  defaultMailboxId: null,
  pageSize: 20,
  timezone: 'Asia/Shanghai',
  language: 'zh-Hans',
  theme: 'light',
  emailNotifications: true,
  desktopNotifications: false,
  soundEnabled: true,
  autoRefreshInterval: 60,
  sessionAutoClose: 0,
  workdays: [1, 2, 3, 4, 5], // 周一到周五
  workStart: '09:00',
  workEnd: '18:00',
};

const storageKey = (userId?: number) => `noneadcss:preferences:${userId ?? 'default'}`;
const avatarKey = (userId?: number) => `noneadcss:avatar:${userId ?? 'default'}`;

const legacyStorageKey = (userId?: number) => `freescout:preferences:${userId ?? 'default'}`;
const legacyAvatarKey = (userId?: number) => `freescout:avatar:${userId ?? 'default'}`;
const legacyThemeKey = 'freescout:theme';
const themeKey = 'noneadcss:theme';

function migrateLegacyPrefs(userId?: number): void {
  try {
    const legacyPrefs = localStorage.getItem(legacyStorageKey(userId));
    if (legacyPrefs && !localStorage.getItem(storageKey(userId))) {
      localStorage.setItem(storageKey(userId), legacyPrefs);
    }
    const legacyAvatar = localStorage.getItem(legacyAvatarKey(userId));
    if (legacyAvatar && !localStorage.getItem(avatarKey(userId))) {
      localStorage.setItem(avatarKey(userId), legacyAvatar);
    }
    const legacyTheme = localStorage.getItem(legacyThemeKey);
    if (legacyTheme && !localStorage.getItem(themeKey)) {
      localStorage.setItem(themeKey, legacyTheme);
    }
  } catch {
    // ignore
  }
}

export function usePreferences(userId?: number) {
  const [preferences, setPreferences] = useState<UserPreferences>(defaultPreferences);

  // 加载偏好
  useEffect(() => {
    try {
      migrateLegacyPrefs(userId);
      const raw = localStorage.getItem(storageKey(userId));
      if (raw) {
        const parsed = JSON.parse(raw);
        const merged = { ...defaultPreferences, ...parsed };
        setPreferences(merged);
        // 同步 theme 到全局 key，确保 useTheme 能读取到
        if (merged.theme) {
          localStorage.setItem(themeKey, merged.theme);
        }
      } else {
        setPreferences(defaultPreferences);
        // 无偏好时使用浅色主题
        localStorage.setItem(themeKey, defaultPreferences.theme);
      }
    } catch {
      setPreferences(defaultPreferences);
      localStorage.setItem(themeKey, defaultPreferences.theme);
    }
  }, [userId]);

  // 保存偏好
  const updatePreferences = useCallback(
    (updates: Partial<UserPreferences>) => {
      setPreferences((prev) => {
        const next = { ...prev, ...updates };
        try {
          localStorage.setItem(storageKey(userId), JSON.stringify(next));
          // 同步更新全局 theme key，确保 useTheme hook 也能读取到
          if (updates.theme) {
            localStorage.setItem(themeKey, updates.theme);
          }
        } catch {
          // ignore
        }
        return next;
      });
    },
    [userId]
  );

  return {
    preferences,
    updatePreferences,
  };
}

// 获取头像的工具函数（供 UserAvatar 组件在未显式传入 avatarUrl 时作为兼容回退使用）
export function getAvatar(userId?: number): string | null {
  return localStorage.getItem(avatarKey(userId));
}

// 设置头像的工具函数（上传头像后同步更新 localStorage 缓存）
export function setAvatar(userId: number | undefined, avatarUrl: string | null): void {
  if (userId == null) return;
  if (avatarUrl) {
    localStorage.setItem(avatarKey(userId), avatarUrl);
  } else {
    localStorage.removeItem(avatarKey(userId));
  }
}
