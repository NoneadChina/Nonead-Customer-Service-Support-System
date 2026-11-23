import { useState, FormEvent, useEffect, useCallback } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useI18n } from '../context/I18nContext';
import { authApi } from '../api';
import { RefreshCw, Mail, Lock, Eye, EyeOff, Sparkles, UserPlus } from 'lucide-react';
import DOMPurify from 'dompurify';

/**
 * 判断验证码加载失败是否属于「后端不可达」类故障。
 *
 * 这类失败（Vite 代理返回 502 Bad Gateway、连接被拒、网络中断）与验证码本身无关，
 * 若仍显示「获取验证码失败，请刷新页面重试」，用户会反复刷新而看不到任何改善——
 * 实际情况是 API 进程没在跑。区分开来才能直接给出「先启动后端」的提示。
 */
function isBackendUnreachable(err: unknown): boolean {
  if (!err || typeof err !== 'object') return false;
  const e = err as { status?: number; code?: string | number; message?: string; response?: { status?: number } };
  const status = typeof e.status === 'number' ? e.status : e.response?.status;
  if (typeof status === 'number' && status >= 502 && status <= 504) return true;
  if (typeof e.code === 'string' && ['ERR_NETWORK', 'ERR_BAD_RESPONSE', 'ECONNREFUSED', 'ECONNABORTED'].includes(e.code)) {
    return true;
  }
  const message = typeof e.message === 'string' ? e.message : '';
  return /Network Error|ECONNREFUSED|502 Bad Gateway|504 Gateway/i.test(message);
}

/**
 * 登录页面组件
 * 提供用户登录功能，包含邮箱、密码、验证码输入
 * 支持深色/浅色主题，具有精美的视觉效果和动画
 */
export default function Login() {
  const { login } = useAuth();
  const { t } = useI18n();
  const navigate = useNavigate();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [captchaCode, setCaptchaCode] = useState('');
  const [captchaId, setCaptchaId] = useState('');
  const [captchaSvg, setCaptchaSvg] = useState('');
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [showPassword, setShowPassword] = useState(false);
  const [allowRegistration, setAllowRegistration] = useState(false);
  const [configLoading, setConfigLoading] = useState(true);

  /**
   * 获取图形验证码
   * 调用后端接口获取新的验证码，并更新本地状态
   */
  const loadCaptcha = useCallback(async () => {
    setRefreshing(true);
    setError('');
    try {
      const resp = await authApi.getCaptcha();
      setCaptchaId(resp.captchaId);
      setCaptchaSvg(resp.svgContent);
      setCaptchaCode('');
    } catch (err: unknown) {
      // 后端不可达（502/连接被拒）与验证码无关，必须给出「先启动后端」的提示，
      // 否则用户只会反复刷新页面而看不到任何改善。
      setError(isBackendUnreachable(err) ? t('auth.login.captchaBackendDown') : t('auth.login.captchaFailed'));
    } finally {
      setRefreshing(false);
    }
  }, [t]);

  /**
   * 页面加载时初始化验证码
   */
  useEffect(() => {
    loadCaptcha();
  }, [loadCaptcha]);

  /**
   * 加载公开配置（是否开放注册等）
   */
  useEffect(() => {
    let mounted = true;
    authApi.getPublicConfig()
      .then((cfg) => {
        if (mounted) {
          setAllowRegistration(cfg.allowRegistration);
        }
      })
      .catch(() => {
        if (mounted) setAllowRegistration(false);
      })
      .finally(() => {
        if (mounted) setConfigLoading(false);
      });
    return () => { mounted = false; };
  }, []);

  /**
   * 处理登录表单提交
   * @param e 表单事件
   */
  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);
    try {
      await login({ email, password, captchaId, captchaCode });
      navigate('/');
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : t('auth.login.failed');
      // 登录失败时刷新验证码，再显示错误信息（避免 loadCaptcha 内部清空错误）
      await loadCaptcha();
      setError(msg);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen flex items-center justify-center px-4 py-8 relative overflow-hidden bg-gradient-to-br from-slate-50 via-blue-50 to-indigo-100 dark:from-slate-900 dark:via-slate-800 dark:to-indigo-950">
      {/* 背景装饰 - 浮动圆形渐变 */}
      <div className="absolute inset-0 overflow-hidden pointer-events-none">
        <div className="absolute -top-40 -right-40 w-96 h-96 bg-primary-400/20 dark:bg-primary-600/20 rounded-full blur-3xl animate-pulse" />
        <div className="absolute -bottom-40 -left-40 w-[500px] h-[500px] bg-purple-400/20 dark:bg-purple-600/15 rounded-full blur-3xl animate-pulse" style={{ animationDelay: '1s' }} />
        <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[600px] h-[600px] bg-blue-300/10 dark:bg-blue-500/10 rounded-full blur-3xl" />
      </div>

      {/* 网格背景装饰 */}
      <div
        className="absolute inset-0 opacity-[0.03] dark:opacity-[0.05] pointer-events-none"
        style={{
          backgroundImage: `
            linear-gradient(to right, currentColor 1px, transparent 1px),
            linear-gradient(to bottom, currentColor 1px, transparent 1px)
          `,
          backgroundSize: '50px 50px',
          color: 'rgb(59 130 246)',
        }}
      />

      {/* 登录卡片 */}
      <div className="relative w-full max-w-md">
        {/* 卡片外层光晕 */}
        <div className="absolute -inset-1 bg-gradient-to-r from-primary-500 via-purple-500 to-primary-500 rounded-3xl blur-lg opacity-30 dark:opacity-40 animate-gradient" />

        <div className="relative bg-white/80 dark:bg-slate-800/80 backdrop-blur-xl rounded-3xl shadow-2xl border border-white/50 dark:border-slate-700/50 p-8 lg:p-10">
          {/* Logo 和标题区域 */}
          <div className="text-center mb-8">
            {/* Logo 容器带光晕 */}
            <div className="relative inline-block mb-5">
              <div className="absolute inset-0 bg-gradient-to-br from-primary-400 to-purple-500 rounded-2xl blur-lg opacity-40 dark:opacity-50 scale-110" />
              <div className="relative w-20 h-20 bg-gradient-to-br from-primary-500 to-primary-600 dark:from-primary-400 dark:to-primary-500 rounded-2xl flex items-center justify-center shadow-lg">
                <img src="/favicon.svg" alt="Logo" className="w-12 h-12" />
                <Sparkles className="absolute -top-1 -right-1 w-5 h-5 text-yellow-300 drop-shadow" />
              </div>
            </div>

            <h1 className="text-2xl lg:text-[26px] font-bold bg-gradient-to-r from-gray-900 via-primary-700 to-gray-900 dark:from-white dark:via-primary-300 dark:to-white bg-clip-text text-transparent">
              {t('auth.login.title')}
            </h1>
            <p className="text-gray-500 dark:text-slate-400 mt-2 text-sm font-medium tracking-wide">
              {t('auth.login.tagline')}
            </p>
          </div>

          {/* 登录表单 */}
          <form onSubmit={handleSubmit} className="space-y-5">
            {/* 邮箱输入框 */}
            <div className="group">
              <label htmlFor="login-email" className="block text-sm font-semibold text-gray-700 dark:text-slate-200 mb-2 ml-1">
                {t('auth.common.emailLabel')}
              </label>
              <div className="relative">
                <Mail className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-400 dark:text-slate-500 group-focus-within:text-primary-500 dark:group-focus-within:text-primary-400 transition-colors" />
                <input
                  id="login-email"
                  type="email"
                  required
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  className="w-full pl-12 pr-4 py-3 bg-white/70 dark:bg-slate-900/60 border-2 border-gray-200 dark:border-slate-600 text-gray-900 dark:text-slate-100 rounded-xl focus:outline-none focus:border-primary-500 dark:focus:border-primary-400 focus:bg-white dark:focus:bg-slate-900 focus:ring-4 focus:ring-primary-500/10 dark:focus:ring-primary-400/20 transition-all duration-200 placeholder:text-gray-400 dark:placeholder:text-slate-500"
                  placeholder={t('auth.common.emailPlaceholder')}
                />
              </div>
            </div>

            {/* 密码输入框 */}
            <div className="group">
              <label htmlFor="login-password" className="block text-sm font-semibold text-gray-700 dark:text-slate-200 mb-2 ml-1">
                {t('auth.login.passwordLabel')}
              </label>
              <div className="relative">
                <Lock className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-400 dark:text-slate-500 group-focus-within:text-primary-500 dark:group-focus-within:text-primary-400 transition-colors" />
                <input
                  id="login-password"
                  type={showPassword ? 'text' : 'password'}
                  required
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className="w-full pl-12 pr-12 py-3 bg-white/70 dark:bg-slate-900/60 border-2 border-gray-200 dark:border-slate-600 text-gray-900 dark:text-slate-100 rounded-xl focus:outline-none focus:border-primary-500 dark:focus:border-primary-400 focus:bg-white dark:focus:bg-slate-900 focus:ring-4 focus:ring-primary-500/10 dark:focus:ring-primary-400/20 transition-all duration-200 placeholder:text-gray-400 dark:placeholder:text-slate-500"
                  placeholder={t('auth.login.passwordPlaceholder')}
                />
                <button
                  type="button"
                  onClick={() => setShowPassword(!showPassword)}
                  className="absolute right-3 top-1/2 -translate-y-1/2 p-1.5 text-gray-400 hover:text-gray-600 dark:text-slate-500 dark:hover:text-slate-300 rounded-lg transition-colors"
                  tabIndex={-1}
                  aria-label={showPassword ? t('auth.common.hidePassword') : t('auth.common.showPassword')}
                  aria-pressed={showPassword}
                >
                  {showPassword ? <EyeOff className="w-5 h-5" /> : <Eye className="w-5 h-5" />}
                </button>
              </div>
            </div>

            {/* 验证码输入框 */}
            <div className="group">
              <label htmlFor="login-captcha" className="block text-sm font-semibold text-gray-700 dark:text-slate-200 mb-2 ml-1">
                {t('auth.login.captchaLabel')}
              </label>
              <div className="flex gap-3">
                <div className="relative flex-1">
                  <input
                    id="login-captcha"
                    type="text"
                    required
                    maxLength={4}
                    value={captchaCode}
                    onChange={(e) => setCaptchaCode(e.target.value.toUpperCase())}
                    className="w-full px-4 py-3 bg-white/70 dark:bg-slate-900/60 border-2 border-gray-200 dark:border-slate-600 text-gray-900 dark:text-slate-100 rounded-xl focus:outline-none focus:border-primary-500 dark:focus:border-primary-400 focus:bg-white dark:focus:bg-slate-900 focus:ring-4 focus:ring-primary-500/10 dark:focus:ring-primary-400/20 transition-all duration-200 placeholder:text-gray-400 dark:placeholder:text-slate-500 tracking-[0.3em] font-mono uppercase font-bold text-lg"
                    placeholder={t('auth.login.captchaPlaceholder')}
                  />
                </div>
                <button
                  type="button"
                  onClick={loadCaptcha}
                  disabled={refreshing}
                  aria-label={t('auth.login.refreshCaptcha')}
                  className="flex-shrink-0 h-[52px] w-[130px] border-2 border-gray-200 dark:border-slate-600 rounded-xl bg-gradient-to-br from-gray-50 to-white dark:from-slate-700/50 dark:to-slate-800/50 hover:border-primary-300 dark:hover:border-primary-500/50 transition-all duration-200 disabled:opacity-60 flex items-center justify-center overflow-hidden cursor-pointer group/captcha shadow-sm hover:shadow-md"
                  title={t('auth.login.captchaRefreshHint')}
                >
                  {captchaSvg ? (
                    <div
                      className="h-full w-full flex items-center justify-center scale-100 group-hover/captcha:scale-105 transition-transform duration-300"
                      dangerouslySetInnerHTML={{ __html: DOMPurify.sanitize(captchaSvg, { USE_PROFILES: { svg: true, svgFilters: true } }) }}
                    />
                  ) : (
                    <RefreshCw className={`w-6 h-6 text-gray-400 ${refreshing ? 'animate-spin' : ''}`} />
                  )}
                  {refreshing && (
                    <div className="absolute inset-0 bg-white/50 dark:bg-slate-800/50 backdrop-blur-sm flex items-center justify-center">
                      <RefreshCw className="w-6 h-6 text-primary-500 animate-spin" />
                    </div>
                  )}
                </button>
              </div>
              <div className="flex items-center justify-between mt-2 ml-1">
                <p className="text-xs text-gray-400 dark:text-slate-500">
                  {t('auth.login.captchaHelp')}
                </p>
                <Link
                  to="/forgot-password"
                  className="text-sm text-primary-600 dark:text-primary-400 hover:text-primary-700 dark:hover:text-primary-300 font-medium transition-colors hover:underline underline-offset-2"
                >
                  {t('auth.login.forgotPasswordLink')}
                </Link>
              </div>
            </div>

            {/* 错误提示 */}
            {error && (
              <div role="alert" className="bg-gradient-to-r from-red-50 to-orange-50 dark:from-red-900/20 dark:to-orange-900/20 border border-red-200 dark:border-red-800/50 text-red-600 dark:text-red-400 px-4 py-3 rounded-xl text-sm font-medium flex items-center gap-2 animate-shake">
                <svg className="w-5 h-5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
                <span>{error}</span>
              </div>
            )}

            {/* 登录按钮 */}
            <button
              type="submit"
              disabled={loading}
              className="w-full relative overflow-hidden bg-gradient-to-r from-primary-600 via-primary-500 to-primary-600 dark:from-primary-500 dark:via-primary-400 dark:to-primary-500 hover:from-primary-700 hover:via-primary-600 hover:to-primary-700 dark:hover:from-primary-600 dark:hover:via-primary-500 dark:hover:to-primary-600 disabled:from-gray-400 disabled:via-gray-400 disabled:to-gray-400 text-white font-semibold py-3.5 rounded-xl transition-all duration-300 shadow-lg shadow-primary-500/25 dark:shadow-primary-500/30 hover:shadow-xl hover:shadow-primary-500/30 dark:hover:shadow-primary-400/40 hover:-translate-y-0.5 active:translate-y-0 disabled:cursor-not-allowed disabled:shadow-none disabled:hover:translate-y-0 bg-[length:200%_100%] hover:bg-[length:100%_100%] bg-position-0"
              style={{ transition: 'all 0.3s ease' }}
            >
              {/* 按钮光泽效果 */}
              <span className="absolute inset-0 -translate-x-full bg-gradient-to-r from-transparent via-white/20 to-transparent animate-shine" />
              <span className="relative flex items-center justify-center gap-2">
                {loading ? (
                  <>
                    <svg className="animate-spin w-5 h-5" viewBox="0 0 24 24" fill="none">
                      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                    </svg>
                    {t('auth.login.submitting')}
                  </>
                ) : (
                  <>
                    {t('auth.login.submit')}
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 7l5 5m0 0l-5 5m5-5H6" />
                    </svg>
                  </>
                )}
              </span>
            </button>
          </form>

          {/* 注册链接 */}
          {!configLoading && allowRegistration && (
            <div className="mt-7 pt-6 border-t border-gray-200/60 dark:border-slate-700/60">
              <Link
                to="/register"
                className="flex items-center justify-center gap-2 text-sm text-primary-600 dark:text-primary-400 hover:text-primary-700 dark:hover:text-primary-300 font-semibold transition-colors hover:underline underline-offset-2"
              >
                <UserPlus className="w-4 h-4" />
                {t('auth.login.registerLink')}
              </Link>
            </div>
          )}

          {/* 底部版权信息 */}
          <div className="mt-5 text-center">
            <p className="text-xs text-gray-400 dark:text-slate-500">
              © 2009-2026 Nonead Corporation. All rights reserved.
            </p>
          </div>
        </div>
      </div>

      {/* 自定义动画样式 */}
      {/* TODO(L-FE2): 当前动画样式以内联 <style> 标签形式注入，存在以下问题：
          1. 内联 style 受 CSP 'unsafe-inline' 限制，与 M-FE5 的 nonce 化 CSP 目标冲突；
          2. 动画定义与组件耦合，难以复用；
          后续应将 @keyframes / .animate-* 类迁移至 src/index.css（Tailwind 层），
          并通过 className 引用，配合 nonce 化 CSP 彻底移除 'unsafe-inline'。 */}
      <style>{`
        @keyframes shine {
          0% { transform: translateX(-100%); }
          100% { transform: translateX(100%); }
        }
        .animate-shine {
          animation: shine 2.5s infinite;
        }
        @keyframes shake {
          0%, 100% { transform: translateX(0); }
          25% { transform: translateX(-4px); }
          75% { transform: translateX(4px); }
        }
        .animate-shake {
          animation: shake 0.4s ease-in-out;
        }
        @keyframes gradient {
          0%, 100% { opacity: 0.3; }
          50% { opacity: 0.5; }
        }
        .animate-gradient {
          animation: gradient 3s ease-in-out infinite;
        }
      `}</style>
    </div>
  );
}
