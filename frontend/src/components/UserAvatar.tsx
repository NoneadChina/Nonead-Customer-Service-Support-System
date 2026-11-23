import { useState, useEffect, useMemo } from 'react';
import { getAvatar } from '../hooks/usePreferences';
import { safeUrl } from '../utils/format';

export type AvatarSize = 'xs' | 'sm' | 'md' | 'lg' | 'xl' | '2xl';

const sizeClasses: Record<AvatarSize, string> = {
  xs: 'w-6 h-6 text-[10px]',
  sm: 'w-8 h-8 text-xs',
  md: 'w-10 h-10 text-sm',
  lg: 'w-12 h-12 text-base',
  xl: 'w-16 h-16 text-2xl',
  // 额外尺寸
  '2xl': 'w-20 h-20 text-3xl',
};

// 基于名称生成稳定的颜色（与 Customers/Conversations 页面一致）
const avatarColors = [
  'bg-blue-500',
  'bg-green-500',
  'bg-purple-500',
  'bg-orange-500',
  'bg-pink-500',
  'bg-teal-500',
  'bg-indigo-500',
  'bg-red-500',
  'bg-cyan-500',
  'bg-amber-500',
];

function getColorForName(name: string): string {
  let hash = 0;
  for (let i = 0; i < name.length; i++) {
    hash = name.charCodeAt(i) + ((hash << 5) - hash);
  }
  return avatarColors[Math.abs(hash) % avatarColors.length];
}

interface UserAvatarProps {
  userId?: number;
  name?: string;
  size?: AvatarSize;
  className?: string;
  /**
   * 外部传入的头像 URL。
   * - 传入字符串：使用该 URL 显示头像
   * - 传入 null：明确表示无头像，回退到首字母占位符（不再读 localStorage）
   * - 传入 undefined：未指定，回退到 localStorage 缓存（兼容旧逻辑）
   */
  avatarUrl?: string | null;
}

/**
 * 通用用户头像组件
 * 优先显示外部传入的头像 URL，回退到 localStorage 缓存（仅 avatarUrl 为 undefined 时），最后回退到首字母
 */
export default function UserAvatar({
  userId,
  name,
  size = 'sm',
  className = '',
  avatarUrl,
}: UserAvatarProps) {
  const [avatar, setAvatar] = useState<string | null>(avatarUrl ?? null);

  useEffect(() => {
    if (avatarUrl !== undefined) {
      // 显式传入（包括 null）：以传入值为准，null 表示无头像
      setAvatar(avatarUrl);
    } else {
      // 未传入：回退到 localStorage 缓存（用于其他用户的头像显示）
      setAvatar(getAvatar(userId));
    }
  }, [userId, avatarUrl]);

  const displayName = name?.trim() || '?';
  const initial = displayName.charAt(0).toUpperCase();
  const colorClass = getColorForName(displayName);
  const sizeClass = sizeClasses[size];
  const safeAvatar = useMemo(() => safeUrl(avatar), [avatar]);

  if (safeAvatar && safeAvatar !== '#') {
    return (
      <img
        src={safeAvatar}
        alt={displayName}
        className={`${sizeClass} rounded-full object-cover border border-gray-200 dark:border-slate-600 ${className}`}
      />
    );
  }

  return (
    <div
      className={`${sizeClass} ${colorClass} rounded-full flex items-center justify-center text-white font-semibold flex-shrink-0 ${className}`}
    >
      {initial}
    </div>
  );
}
