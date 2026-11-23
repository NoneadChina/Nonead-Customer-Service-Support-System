import { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useModules } from '../context/ModuleContext';
import { useI18n } from '../context/I18nContext';
import { isPathModuleActive, getRequiredModulesForPath } from '../config/moduleMenuMap';

interface ModuleProtectedRouteProps {
  children: ReactNode;
}

export default function ModuleProtectedRoute({ children }: ModuleProtectedRouteProps) {
  const location = useLocation();
  const { modules, activeModuleNames, loading } = useModules();
  const { t } = useI18n();

  if (loading) {
    return (
      <div
        className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-slate-900"
        role="status"
        aria-live="polite"
        aria-busy="true"
      >
        <div className="text-gray-500 dark:text-slate-400">{t('common.shell.loading')}</div>
      </div>
    );
  }

  const allModuleNames = new Set(modules.map((m) => m.name));

  if (!isPathModuleActive(location.pathname, activeModuleNames, allModuleNames)) {
    const required = getRequiredModulesForPath(location.pathname);
    return (
      <Navigate
        to="/"
        replace
        state={{ from: location.pathname, disabledModules: required }}
      />
    );
  }

  return <>{children}</>;
}