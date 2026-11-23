import { Suspense, useEffect } from 'react';
import { BrowserRouter, Routes, Route, Navigate, Link, useNavigate } from 'react-router-dom';
import { QueryClientProvider } from '@tanstack/react-query';
import { ReactQueryDevtools } from '@tanstack/react-query-devtools';
import { AuthProvider, useAuth } from './context/AuthContext';
import { I18nProvider, useI18n } from './context/I18nContext';
import { ModuleProvider } from './context/ModuleContext';
import { MailboxProvider } from './context/MailboxContext';
import { ToastProvider } from './context/ToastContext';
import { useTheme } from './hooks/useTheme';
import { queryClient } from './lib/queryClient';
import Layout from './components/Layout';
import ModuleProtectedRoute from './components/ModuleProtectedRoute';
import RequirePermission from './components/RequirePermission';
import ErrorBoundary from './components/ErrorBoundary';
// T000090：全局日历提醒监听器 —— 任意页面都能收到 SignalR 弹窗提醒
import GlobalCalendarReminderListener from './components/GlobalCalendarReminderListener';
import { publicRoutes, protectedRoutes } from './routes';
import { lazyWithRetry } from './lib/lazyRetry';

const Forbidden = lazyWithRetry(() => import('./pages/Forbidden'));

// 路由懒加载时的统一占位 UI
function RouteLoading() {
  const { t } = useI18n();
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

// 404 页面
function NotFound() {
  const { t } = useI18n();
  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-50 dark:bg-slate-900 px-4">
      <div className="text-center">
        <div className="text-7xl font-extrabold bg-gradient-to-r from-primary-500 to-purple-500 bg-clip-text text-transparent">
          404
        </div>
        <h1 className="mt-4 text-2xl font-semibold text-gray-900 dark:text-slate-100">
          {t('common.shell.notFoundTitle')}
        </h1>
        <p className="mt-2 text-sm text-gray-500 dark:text-slate-400">
          {t('common.shell.notFoundDescription')}
        </p>
        <Link
          to="/"
          className="inline-flex items-center mt-6 px-5 py-2.5 rounded-xl bg-primary-600 hover:bg-primary-700 text-white font-medium transition-colors"
        >
          {t('common.shell.backToHome')}
        </Link>
      </div>
    </div>
  );
}

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, loading } = useAuth();
  if (loading) {
    return <RouteLoading />;
  }
  if (!isAuthenticated) {
    return <Navigate to="/login" replace />;
  }
  return (
    <MailboxProvider>
      <ModuleProvider>
        <ModuleProtectedRoute>
          <RequirePermission>
            <Layout>{children}</Layout>
          </RequirePermission>
        </ModuleProtectedRoute>
      </ModuleProvider>
    </MailboxProvider>
  );
}

function AppRoutes() {
  const { isAuthenticated } = useAuth();
  const navigate = useNavigate();

  useEffect(() => {
    const handler = (e: Event) => {
      const { to, replace } = (e as CustomEvent).detail || {};
      if (to) navigate(to, { replace: !!replace });
    };
    window.addEventListener('app:navigate', handler);
    return () => window.removeEventListener('app:navigate', handler);
  }, [navigate]);

  return (
    <Suspense fallback={<RouteLoading />}>
      <Routes>
        {publicRoutes.map((route) => {
          const Component = route.element;
          const isAuthPage = route.path === '/login' || route.path === '/register';
          return (
            <Route
              key={route.path}
              path={route.path}
              element={isAuthPage && isAuthenticated ? <Navigate to="/" replace /> : <Component />}
            />
          );
        })}
        <Route path="/forbidden" element={<Forbidden />} />
        {protectedRoutes.map((route) => {
          const Component = route.element;
          return (
            <Route
              key={route.path}
              path={route.path}
              element={
                <ProtectedRoute>
                  <Component />
                </ProtectedRoute>
              }
            />
          );
        })}
        <Route path="*" element={<NotFound />} />
      </Routes>
    </Suspense>
  );
}

function AppContent() {
  const { isInitializing } = useAuth();

  // 启动占位也要走真源，所以 I18nProvider 必须包住它。
  // 两个分支都以 <I18nProvider> 为根：React 按"同类型 + 同位置"复用同一个实例，
  // isInitializing 翻转时该 Provider 不会重挂载（语言/覆盖层状态保留）；
  // ToastProvider 及其以下仍在就绪之后才挂载，挂载时序与改造前一致。
  if (isInitializing) {
    return (
      <I18nProvider>
        <RouteLoading />
      </I18nProvider>
    );
  }

  return (
    <I18nProvider>
      <ToastProvider>
        <BrowserRouter>
          <ErrorBoundary>
            {/* T000090：全局日历提醒弹层 —— 任意已登录页面均可接收 */}
            <GlobalCalendarReminderListener />
            <AppRoutes />
          </ErrorBoundary>
        </BrowserRouter>
      </ToastProvider>
    </I18nProvider>
  );
}

export default function App() {
  useTheme();

  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>
        <AppContent />
      </AuthProvider>
      {import.meta.env.DEV && (
        <ReactQueryDevtools initialIsOpen={false} buttonPosition="bottom-right" />
      )}
    </QueryClientProvider>
  );
}
