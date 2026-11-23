import { useState, FormEvent, useEffect } from 'react';
import { Link } from 'react-router-dom';
import { authApi } from '../api';
import { useI18n } from '../context/I18nContext';
import { usePasswordPolicy } from '../hooks/usePasswordPolicy';
import PasswordPolicyHint from '../components/PasswordPolicyHint';
import { Mail, Lock, User, Eye, EyeOff, Sparkles, ArrowLeft, CheckCircle2, ShieldCheck } from 'lucide-react';

export default function Register() {
  const { policy, validate: validatePassword } = usePasswordPolicy();
  const { t } = useI18n();

  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [registrationEnabled, setRegistrationEnabled] = useState<boolean | null>(null);
  const [checkingConfig, setCheckingConfig] = useState(true);

  useEffect(() => {
    let mounted = true;
    setCheckingConfig(true);
    authApi.getPublicConfig()
      .then((cfg) => {
        if (mounted) {
          setRegistrationEnabled(cfg.allowRegistration);
        }
      })
      .catch(() => {
        if (mounted) setRegistrationEnabled(false);
      })
      .finally(() => {
        if (mounted) setCheckingConfig(false);
      });
    return () => { mounted = false; };
  }, []);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setLoading(true);

    if (!firstName.trim() || !lastName.trim()) {
      setError(t('auth.register.nameRequired'));
      setLoading(false);
      return;
    }
    if (!email.trim()) {
      setError(t('auth.register.emailRequired'));
      setLoading(false);
      return;
    }

    const pwdErrors = validatePassword(password);
    if (pwdErrors.length > 0) {
      setError(pwdErrors.join(t('auth.common.validationErrorSeparator')));
      setLoading(false);
      return;
    }

    if (password !== confirmPassword) {
      setError(t('auth.common.passwordMismatch'));
      setLoading(false);
      return;
    }

    try {
      const data = await authApi.register({
        email: email.trim(),
        password,
        firstName: firstName.trim(),
        lastName: lastName.trim(),
      });
      if (data.success) {
        setSuccess(true);
      } else {
        setError(data.errorMessage || t('auth.register.failed'));
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('auth.register.failedRetry'));
    } finally {
      setLoading(false);
    }
  };

  if (checkingConfig) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-slate-50 via-blue-50 to-indigo-100 dark:from-slate-900 dark:via-slate-800 dark:to-indigo-950">
        <div className="text-gray-500 dark:text-slate-400">{t('auth.common.loading')}</div>
      </div>
    );
  }

  if (!registrationEnabled) {
    return (
      <div className="min-h-screen flex items-center justify-center px-4 py-8 relative overflow-hidden bg-gradient-to-br from-slate-50 via-blue-50 to-indigo-100 dark:from-slate-900 dark:via-slate-800 dark:to-indigo-950">
        <div className="absolute inset-0 overflow-hidden pointer-events-none">
          <div className="absolute -top-40 -right-40 w-96 h-96 bg-primary-400/20 dark:bg-primary-600/20 rounded-full blur-3xl animate-pulse" />
          <div className="absolute -bottom-40 -left-40 w-[500px] h-[500px] bg-purple-400/20 dark:bg-purple-600/15 rounded-full blur-3xl animate-pulse" style={{ animationDelay: '1s' }} />
        </div>
        <div className="relative w-full max-w-md">
          <div className="relative bg-white/80 dark:bg-slate-800/80 backdrop-blur-xl rounded-3xl shadow-2xl border border-white/50 dark:border-slate-700/50 p-8 lg:p-10 text-center">
            <div className="w-20 h-20 mx-auto mb-6 bg-gradient-to-br from-amber-400 to-orange-500 rounded-2xl flex items-center justify-center shadow-lg">
              <ShieldCheck className="w-10 h-10 text-white" />
            </div>
            <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100 mb-3">{t('auth.register.disabledTitle')}</h1>
            <p className="text-gray-500 dark:text-slate-400 mb-8">
              {t('auth.register.disabledNote')}<br />{t('auth.register.disabledContactAdmin')}
            </p>
            <Link
              to="/login"
              className="inline-flex items-center gap-2 px-6 py-3 bg-gradient-to-r from-primary-600 to-primary-500 text-white font-semibold rounded-xl shadow-lg shadow-primary-500/25 hover:shadow-xl hover:-translate-y-0.5 transition-all"
            >
              <ArrowLeft className="w-4 h-4" />
              {t('auth.common.backToLogin')}
            </Link>
          </div>
        </div>
      </div>
    );
  }

  if (success) {
    return (
      <div className="min-h-screen flex items-center justify-center px-4 py-8 relative overflow-hidden bg-gradient-to-br from-slate-50 via-blue-50 to-indigo-100 dark:from-slate-900 dark:via-slate-800 dark:to-indigo-950">
        <div className="absolute inset-0 overflow-hidden pointer-events-none">
          <div className="absolute -top-40 -right-40 w-96 h-96 bg-emerald-400/20 dark:bg-emerald-600/20 rounded-full blur-3xl animate-pulse" />
          <div className="absolute -bottom-40 -left-40 w-[500px] h-[500px] bg-blue-400/20 dark:bg-blue-600/15 rounded-full blur-3xl animate-pulse" style={{ animationDelay: '1s' }} />
        </div>
        <div className="relative w-full max-w-md">
          <div className="relative bg-white/80 dark:bg-slate-800/80 backdrop-blur-xl rounded-3xl shadow-2xl border border-white/50 dark:border-slate-700/50 p-8 lg:p-10 text-center">
            <div className="w-20 h-20 mx-auto mb-6 bg-gradient-to-br from-emerald-400 to-teal-500 rounded-2xl flex items-center justify-center shadow-lg">
              <CheckCircle2 className="w-10 h-10 text-white" />
            </div>
            <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100 mb-3">{t('auth.register.successTitle')}</h1>
            <p className="text-gray-500 dark:text-slate-400 mb-2">
              {t('auth.register.successSubmitted')}
            </p>
            <p className="text-sm text-gray-400 dark:text-slate-500 mb-8">
              {t('auth.register.successPending')}
            </p>
            <Link
              to="/login"
              className="inline-flex items-center gap-2 px-6 py-3 bg-gradient-to-r from-primary-600 to-primary-500 text-white font-semibold rounded-xl shadow-lg shadow-primary-500/25 hover:shadow-xl hover:-translate-y-0.5 transition-all"
            >
              <ArrowLeft className="w-4 h-4" />
              {t('auth.common.goToLogin')}
            </Link>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen flex items-center justify-center px-4 py-8 relative overflow-hidden bg-gradient-to-br from-slate-50 via-blue-50 to-indigo-100 dark:from-slate-900 dark:via-slate-800 dark:to-indigo-950">
      <div className="absolute inset-0 overflow-hidden pointer-events-none">
        <div className="absolute -top-40 -right-40 w-96 h-96 bg-primary-400/20 dark:bg-primary-600/20 rounded-full blur-3xl animate-pulse" />
        <div className="absolute -bottom-40 -left-40 w-[500px] h-[500px] bg-purple-400/20 dark:bg-purple-600/15 rounded-full blur-3xl animate-pulse" style={{ animationDelay: '1s' }} />
        <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[600px] h-[600px] bg-blue-300/10 dark:bg-blue-500/10 rounded-full blur-3xl" />
      </div>

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

      <div className="relative w-full max-w-md">
        <div className="absolute -inset-1 bg-gradient-to-r from-primary-500 via-purple-500 to-primary-500 rounded-3xl blur-lg opacity-30 dark:opacity-40 animate-gradient" />

        <div className="relative bg-white/80 dark:bg-slate-800/80 backdrop-blur-xl rounded-3xl shadow-2xl border border-white/50 dark:border-slate-700/50 p-8 lg:p-10">
          <div className="text-center mb-8">
            <div className="relative inline-block mb-5">
              <div className="absolute inset-0 bg-gradient-to-br from-primary-400 to-purple-500 rounded-2xl blur-lg opacity-40 dark:opacity-50 scale-110" />
              <div className="relative w-20 h-20 bg-gradient-to-br from-primary-500 to-primary-600 dark:from-primary-400 dark:to-primary-500 rounded-2xl flex items-center justify-center shadow-lg">
                <img src="/favicon.svg" alt="Logo" className="w-12 h-12" />
                <Sparkles className="absolute -top-1 -right-1 w-5 h-5 text-yellow-300 drop-shadow" />
              </div>
            </div>

            <h1 className="text-2xl lg:text-[26px] font-bold bg-gradient-to-r from-gray-900 via-primary-700 to-gray-900 dark:from-white dark:via-primary-300 dark:to-white bg-clip-text text-transparent">
              {t('auth.register.title')}
            </h1>
            <p className="text-gray-500 dark:text-slate-400 mt-2 text-sm font-medium tracking-wide">
              {t('auth.register.subtitle')}
            </p>
          </div>

          <form onSubmit={handleSubmit} className="space-y-4">
            <div className="grid grid-cols-2 gap-3">
              <div className="group">
                <label htmlFor="register-lastName" className="block text-sm font-semibold text-gray-700 dark:text-slate-200 mb-1.5 ml-1">
                  {t('auth.register.lastNameLabel')} <span className="text-red-500">*</span>
                </label>
                <div className="relative">
                  <User className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400 dark:text-slate-500 group-focus-within:text-primary-500 dark:group-focus-within:text-primary-400 transition-colors" />
                  <input
                    id="register-lastName"
                    type="text"
                    required
                    value={lastName}
                    onChange={(e) => setLastName(e.target.value)}
                    className="w-full pl-10 pr-3 py-3 bg-white/70 dark:bg-slate-900/60 border-2 border-gray-200 dark:border-slate-600 text-gray-900 dark:text-slate-100 rounded-xl focus:outline-none focus:border-primary-500 dark:focus:border-primary-400 focus:bg-white dark:focus:bg-slate-900 focus:ring-4 focus:ring-primary-500/10 dark:focus:ring-primary-400/20 transition-all duration-200 placeholder:text-gray-400 dark:placeholder:text-slate-500 text-sm"
                    placeholder={t('auth.register.lastNamePlaceholder')}
                  />
                </div>
              </div>
              <div className="group">
                <label htmlFor="register-firstName" className="block text-sm font-semibold text-gray-700 dark:text-slate-200 mb-1.5 ml-1">
                  {t('auth.register.firstNameLabel')} <span className="text-red-500">*</span>
                </label>
                <div className="relative">
                  <User className="absolute left-3.5 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400 dark:text-slate-500 group-focus-within:text-primary-500 dark:group-focus-within:text-primary-400 transition-colors" />
                  <input
                    id="register-firstName"
                    type="text"
                    required
                    value={firstName}
                    onChange={(e) => setFirstName(e.target.value)}
                    className="w-full pl-10 pr-3 py-3 bg-white/70 dark:bg-slate-900/60 border-2 border-gray-200 dark:border-slate-600 text-gray-900 dark:text-slate-100 rounded-xl focus:outline-none focus:border-primary-500 dark:focus:border-primary-400 focus:bg-white dark:focus:bg-slate-900 focus:ring-4 focus:ring-primary-500/10 dark:focus:ring-primary-400/20 transition-all duration-200 placeholder:text-gray-400 dark:placeholder:text-slate-500 text-sm"
                    placeholder={t('auth.register.firstNamePlaceholder')}
                  />
                </div>
              </div>
            </div>

            <div className="group">
              <label htmlFor="register-email" className="block text-sm font-semibold text-gray-700 dark:text-slate-200 mb-1.5 ml-1">
                {t('auth.common.emailLabel')} <span className="text-red-500">*</span>
              </label>
              <div className="relative">
                <Mail className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-400 dark:text-slate-500 group-focus-within:text-primary-500 dark:group-focus-within:text-primary-400 transition-colors" />
                <input
                  id="register-email"
                  type="email"
                  required
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  className="w-full pl-12 pr-4 py-3 bg-white/70 dark:bg-slate-900/60 border-2 border-gray-200 dark:border-slate-600 text-gray-900 dark:text-slate-100 rounded-xl focus:outline-none focus:border-primary-500 dark:focus:border-primary-400 focus:bg-white dark:focus:bg-slate-900 focus:ring-4 focus:ring-primary-500/10 dark:focus:ring-primary-400/20 transition-all duration-200 placeholder:text-gray-400 dark:placeholder:text-slate-500"
                  placeholder={t('auth.common.emailPlaceholder')}
                />
              </div>
            </div>

            <div className="group">
              <label htmlFor="register-password" className="block text-sm font-semibold text-gray-700 dark:text-slate-200 mb-1.5 ml-1">
                {t('auth.register.passwordLabel')} <span className="text-red-500">*</span>
              </label>
              <div className="relative">
                <Lock className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-400 dark:text-slate-500 group-focus-within:text-primary-500 dark:group-focus-within:text-primary-400 transition-colors" />
                <input
                  id="register-password"
                  type={showPassword ? 'text' : 'password'}
                  required
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                  className="w-full pl-12 pr-12 py-3 bg-white/70 dark:bg-slate-900/60 border-2 border-gray-200 dark:border-slate-600 text-gray-900 dark:text-slate-100 rounded-xl focus:outline-none focus:border-primary-500 dark:focus:border-primary-400 focus:bg-white dark:focus:bg-slate-900 focus:ring-4 focus:ring-primary-500/10 dark:focus:ring-primary-400/20 transition-all duration-200 placeholder:text-gray-400 dark:placeholder:text-slate-500"
                  placeholder={t('auth.common.passwordMinLengthPlaceholder', { min: policy?.minLength ?? 6 })}
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
              <PasswordPolicyHint policy={policy} password={password} />
            </div>

            <div className="group">
              <label htmlFor="register-confirmPassword" className="block text-sm font-semibold text-gray-700 dark:text-slate-200 mb-1.5 ml-1">
                {t('auth.register.confirmPasswordLabel')} <span className="text-red-500">*</span>
              </label>
              <div className="relative">
                <Lock className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-gray-400 dark:text-slate-500 group-focus-within:text-primary-500 dark:group-focus-within:text-primary-400 transition-colors" />
                <input
                  id="register-confirmPassword"
                  type={showPassword ? 'text' : 'password'}
                  required
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  className="w-full pl-12 pr-4 py-3 bg-white/70 dark:bg-slate-900/60 border-2 border-gray-200 dark:border-slate-600 text-gray-900 dark:text-slate-100 rounded-xl focus:outline-none focus:border-primary-500 dark:focus:border-primary-400 focus:bg-white dark:focus:bg-slate-900 focus:ring-4 focus:ring-primary-500/10 dark:focus:ring-primary-400/20 transition-all duration-200 placeholder:text-gray-400 dark:placeholder:text-slate-500"
                  placeholder={t('auth.register.confirmPasswordPlaceholder')}
                />
              </div>
              {confirmPassword && password !== confirmPassword && (
                <p className="text-xs text-red-500 mt-1.5 ml-1">{t('auth.common.passwordMismatch')}</p>
              )}
              {confirmPassword && password === confirmPassword && (
                <p className="text-xs text-emerald-600 dark:text-emerald-400 mt-1.5 ml-1 flex items-center gap-1">
                  <CheckCircle2 className="w-3.5 h-3.5" />
                  {t('auth.register.passwordMatch')}
                </p>
              )}
            </div>

            {error && (
              <div role="alert" className="bg-gradient-to-r from-red-50 to-orange-50 dark:from-red-900/20 dark:to-orange-900/20 border border-red-200 dark:border-red-800/50 text-red-600 dark:text-red-400 px-4 py-3 rounded-xl text-sm font-medium flex items-center gap-2 animate-shake">
                <svg className="w-5 h-5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
                <span>{error}</span>
              </div>
            )}

            <button
              type="submit"
              disabled={loading}
              className="w-full relative overflow-hidden bg-gradient-to-r from-primary-600 via-primary-500 to-primary-600 dark:from-primary-500 dark:via-primary-400 dark:to-primary-500 hover:from-primary-700 hover:via-primary-600 hover:to-primary-700 dark:hover:from-primary-600 dark:hover:via-primary-500 dark:hover:to-primary-600 disabled:from-gray-400 disabled:via-gray-400 disabled:to-gray-400 text-white font-semibold py-3.5 rounded-xl transition-all duration-300 shadow-lg shadow-primary-500/25 dark:shadow-primary-500/30 hover:shadow-xl hover:shadow-primary-500/30 dark:hover:shadow-primary-400/40 hover:-translate-y-0.5 active:translate-y-0 disabled:cursor-not-allowed disabled:shadow-none disabled:hover:translate-y-0 bg-[length:200%_100%] hover:bg-[length:100%_100%] bg-position-0"
              style={{ transition: 'all 0.3s ease' }}
            >
              <span className="absolute inset-0 -translate-x-full bg-gradient-to-r from-transparent via-white/20 to-transparent animate-shine" />
              <span className="relative flex items-center justify-center gap-2">
                {loading ? (
                  <>
                    <svg className="animate-spin w-5 h-5" viewBox="0 0 24 24" fill="none">
                      <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                      <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
                    </svg>
                    {t('auth.register.submitting')}
                  </>
                ) : (
                  <>
                    {t('auth.register.submit')}
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 7l5 5m0 0l-5 5m5-5H6" />
                    </svg>
                  </>
                )}
              </span>
            </button>
          </form>

          <div className="mt-7 pt-6 border-t border-gray-200/60 dark:border-slate-700/60">
            <p className="text-center text-sm text-gray-500 dark:text-slate-400">
              {t('auth.register.haveAccount')}{' '}
              <Link
                to="/login"
                className="text-primary-600 dark:text-primary-400 hover:text-primary-700 dark:hover:text-primary-300 font-semibold transition-colors hover:underline underline-offset-2"
              >
                {t('auth.common.backToLogin')}
              </Link>
            </p>
          </div>

          <div className="mt-5 text-center">
            <p className="text-xs text-gray-400 dark:text-slate-500">
              © 2024 NoneadCSS. All rights reserved.
            </p>
          </div>
        </div>
      </div>

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
