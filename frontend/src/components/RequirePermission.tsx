import { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { usePermission } from '../hooks/usePermission';

interface RequirePermissionProps {
  children: ReactNode;
  path?: string;
}

/**
 * 路由权限守卫组件
 * - 检查当前用户是否有权限访问指定路径
 * - 无权限时重定向到 /forbidden 页面
 * - 首页和个人中心对所有登录用户开放
 */
export default function RequirePermission({ children, path }: RequirePermissionProps) {
  const location = useLocation();
  const { canAccessItem, loading } = usePermission();

  // 权限加载中，显示空白避免闪烁
  if (loading) {
    return (
      <div className="min-h-screen bg-gray-50 dark:bg-slate-900 flex items-center justify-center">
        <div className="w-6 h-6 border-2 border-primary-600 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  // 使用传入的 path 或当前路径
  const checkPath = path || location.pathname;

  // 检查权限
  if (!canAccessItem(checkPath)) {
    return <Navigate to="/forbidden" replace />;
  }

  return <>{children}</>;
}