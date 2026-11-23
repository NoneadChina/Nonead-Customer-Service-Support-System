import { useState, FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { Mail, User, ArrowLeft, CheckCircle, Loader2, Shield, Lock } from 'lucide-react';
import { authApi } from '../api';
import { useI18n } from '../context/I18nContext';

export default function ForgotPassword() {
  const { t } = useI18n();
  const [firstName, setFirstName] = useState('');
  const [lastName, setLastName] = useState('');
  const [email, setEmail] = useState('');
  const [loading, setLoading] = useState(false);
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState('');

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setError('');

    try {
      const data = await authApi.forgotPassword({ firstName, lastName, email });
      if (data.success) {
        setSuccess(true);
      } else {
        setError(data.message || t('auth.forgot.requestFailed'));
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('auth.common.networkError'));
    } finally {
      setLoading(false);
    }
  };

  if (success) {
    return (
      <div className="min-h-screen flex items-center justify-center px-4 py-8 relative overflow-hidden bg-gradient-to-br from-slate-50 via-blue-50 to-indigo-100 dark:from-slate-900 dark:via-slate-800 dark:to-indigo-950">
        <div className="absolute inset-0 overflow-hidden pointer-events-none">
          <div className="absolute -top-40 -right-40 w-96 h-96 bg-primary-400/20 dark:bg-primary-600/20 rounded-full blur-3xl animate-pulse" />
          <div className="absolute -bottom-40 -left-40 w-[500px] h-[500px] bg-purple-400/20 dark:bg-purple-600/15 rounded-full blur-3xl animate-pulse" style={{ animationDelay: '1s' }} />
          <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[600px] h-[600px] bg-blue-300/10 dark:bg-blue-500/10 rounded-full blur-3xl" />
        </div>

        <div className="absolute inset-0 opacity-[0.03] dark:opacity-[0.05] pointer-events-none" style={{
          backgroundImage: `linear-gradient(to right, currentColor 1px, transparent 1px), linear-gradient(to bottom, currentColor 1px, transparent 1px)`,
          backgroundSize: '50px 50px',
          color: 'rgb(59 130 246)',
        }} />

        <div className="relative w-full max-w-md">
          <div className="absolute -inset-1 bg-gradient-to-r from-green-400 via-emerald-500 to-teal-500 rounded-3xl blur-lg opacity-30 dark:opacity-40 animate-gradient-shift" />

          <div className="relative bg-white/80 dark:bg-slate-800/80 backdrop-blur-xl rounded-3xl shadow-2xl border border-white/50 dark:border-slate-700/50 p-8 lg:p-10 text-center">
            <div className="relative inline-block mb-6 group">
              <div className="absolute inset-0 bg-gradient-to-br from-green-400 to-emerald-500 rounded-full blur-xl opacity-40 dark:opacity-50 scale-125 group-hover:scale-150 transition-transform duration-500" />
              <div className="relative w-24 h-24 bg-gradient-to-br from-green-500 to-emerald-600 dark:from-green-400 dark:to-emerald-500 rounded-full flex items-center justify-center shadow-lg animate-bounce-slow">
                <CheckCircle className="w-12 h-12 text-white" />
              </div>
            </div>

            <div className="mb-6">
              <div className="inline-flex items-center gap-2 bg-gradient-to-r from-green-100 to-emerald-100 dark:from-green-900/30 dark:to-emerald-900/30 px-4 py-2 rounded-full mb-4">
                <Shield className="w-4 h-4 text-green-600 dark:text-green-400" />
                <span className="text-sm font-medium text-green-700 dark:text-green-300">{t('auth.forgot.successBadge')}</span>
              </div>
            </div>

            <h2 className="text-2xl lg:text-[28px] font-bold bg-gradient-to-r from-gray-900 via-green-700 to-gray-900 dark:from-white dark:via-green-300 dark:to-white bg-clip-text text-transparent mb-3">
              {t('auth.forgot.successTitle')}
            </h2>
            
            <p className="text-gray-500 dark:text-slate-400 text-sm font-medium mb-2">
              {t('auth.forgot.successSentTo')}
            </p>
            <p className="text-lg font-semibold text-primary-600 dark:text-primary-400 mb-6 break-all">
              {email}
            </p>
            
            <div className="bg-gray-50 dark:bg-slate-700/50 rounded-xl p-4 mb-8">
              <p className="text-gray-400 dark:text-slate-500 text-xs leading-relaxed">
                <span className="inline-flex items-center justify-center w-2 h-2 bg-green-500 rounded-full mr-2" />
                {t('auth.forgot.successTipExpiry')}
                <br />
                <span className="inline-flex items-center justify-center w-2 h-2 bg-blue-500 rounded-full mr-2" />
                {t('auth.forgot.successTipSpam')}
                <br />
                <span className="inline-flex items-center justify-center w-2 h-2 bg-amber-500 rounded-full mr-2" />
                {t('auth.forgot.successTipSingleUse')}
              </p>
            </div>

            <Link
              to="/login"
              className="inline-flex items-center gap-2 bg-gradient-to-r from-primary-600 via-primary-500 to-primary-600 dark:from-primary-500 dark:via-primary-400 dark:to-primary-500 hover:from-primary-700 hover:via-primary-600 hover:to-primary-700 dark:hover:from-primary-600 dark:hover:via-primary-500 dark:hover:to-primary-600 text-white font-semibold py-3.5 px-8 rounded-xl transition-all duration-300 shadow-lg shadow-primary-500/25 dark:shadow-primary-500/30 hover:shadow-xl hover:shadow-primary-500/30 dark:hover:shadow-primary-400/40 hover:-translate-y-0.5 active:translate-y-0"
            >
              <ArrowLeft className="w-4 h-4" />
              {t('auth.common.backToLogin')}
            </Link>

            <div className="mt-8 pt-6 border-t border-gray-200/60 dark:border-slate-700/60">
              <p className="text-xs text-gray-400 dark:text-slate-500">
                © 2024 NoneadCSS. All rights reserved.
              </p>
            </div>
          </div>
        </div>

        <style>{`
          @keyframes gradient-shift {
            0%, 100% { opacity: 0.3; transform: rotate(0deg); }
            50% { opacity: 0.5; transform: rotate(180deg); }
          }
          .animate-gradient-shift {
            animation: gradient-shift 6s ease-in-out infinite;
          }
          @keyframes bounce-slow {
            0%, 100% { transform: translateY(0); }
            50% { transform: translateY(-5px); }
          }
          .animate-bounce-slow {
            animation: bounce-slow 2s ease-in-out infinite;
          }
        `}</style>
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
        <div className="absolute -inset-1 bg-gradient-to-r from-primary-500 via-purple-500 to-primary-500 rounded-3xl blur-lg opacity-30 dark:opacity-40 animate-gradient-shift" />

        <div className="relative bg-white/80 dark:bg-slate-800/80 backdrop-blur-xl rounded-3xl shadow-2xl border border-white/50 dark:border-slate-700/50 p-8 lg:p-10">
          <div className="text-center mb-8">
            <div className="relative inline-block mb-5 group">
              <div className="absolute inset-0 bg-gradient-to-br from-primary-400 to-purple-500 rounded-2xl blur-lg opacity-40 dark:opacity-50 scale-110 group-hover:scale-125 transition-transform duration-500" />
              <div className="relative w-20 h-20 bg-gradient-to-br from-primary-500 to-primary-600 dark:from-primary-400 dark:to-primary-500 rounded-2xl flex items-center justify-center shadow-lg">
                <Lock className="w-10 h-10 text-white" />
              </div>
            </div>

            <div className="inline-flex items-center gap-2 bg-gradient-to-r from-primary-100 to-purple-100 dark:from-primary-900/30 dark:to-purple-900/30 px-4 py-2 rounded-full mb-4">
              <Shield className="w-4 h-4 text-primary-600 dark:text-primary-400" />
              <span className="text-sm font-medium text-primary-700 dark:text-primary-300">{t('auth.forgot.securityBadge')}</span>
            </div>

            <h1 className="text-2xl lg:text-[28px] font-bold bg-gradient-to-r from-gray-900 via-primary-700 to-gray-900 dark:from-white dark:via-primary-300 dark:to-white bg-clip-text text-transparent">
              {t('auth.forgot.title')}
            </h1>
            <p className="text-gray-500 dark:text-slate-400 mt-2 text-sm font-medium tracking-wide">
              {t('auth.forgot.subtitle')}
            </p>
          </div>

          <form onSubmit={handleSubmit} className="space-y-5">
            {error && (
              <div role="alert" className="bg-gradient-to-r from-red-50 to-orange-50 dark:from-red-900/20 dark:to-orange-900/20 border border-red-200 dark:border-red-800/50 text-red-600 dark:text-red-400 px-4 py-3 rounded-xl text-sm font-medium flex items-center gap-2 animate-shake">
                <svg className="w-5 h-5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 8v4m0 4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                </svg>
                <span>{error}</span>
              </div>
            )}

            <div className="grid grid-cols-2 gap-4">
              <div className="group">
                <label htmlFor="forgot-firstName" className="block text-sm font-semibold text-gray-700 dark:text-slate-200 mb-2 ml-1">
                  {t('auth.forgot.firstNameLabel')}
                </label>
                <div className="relative">
                  <div className="absolute inset-y-0 left-0 pl-4 flex items-center pointer-events-none group-focus-within:animate-icon-bounce">
                    <User className="w-5 h-5 text-gray-400 dark:text-slate-500 group-focus-within:text-primary-500 dark:group-focus-within:text-primary-400 transition-colors" />
                  </div>
                  <input
                    id="forgot-firstName"
                    type="text"
                    required
                    value={firstName}
                    onChange={(e) => setFirstName(e.target.value)}
                    className="w-full pl-12 pr-4 py-3 bg-white/70 dark:bg-slate-900/60 border-2 border-gray-200 dark:border-slate-600 text-gray-900 dark:text-slate-100 rounded-xl focus:outline-none focus:border-primary-500 dark:focus:border-primary-400 focus:bg-white dark:focus:bg-slate-900 focus:ring-4 focus:ring-primary-500/10 dark:focus:ring-primary-400/20 transition-all duration-200 placeholder:text-gray-400 dark:placeholder:text-slate-500"
                    placeholder={t('auth.forgot.firstNamePlaceholder')}
                  />
                  <div className="absolute inset-0 rounded-xl bg-gradient-to-r from-primary-500/5 to-purple-500/5 opacity-0 group-focus-within:opacity-100 transition-opacity pointer-events-none" />
                </div>
              </div>
              <div className="group">
                <label htmlFor="forgot-lastName" className="block text-sm font-semibold text-gray-700 dark:text-slate-200 mb-2 ml-1">
                  {t('auth.forgot.lastNameLabel')}
                </label>
                <div className="relative">
                  <div className="absolute inset-y-0 left-0 pl-4 flex items-center pointer-events-none group-focus-within:animate-icon-bounce">
                    <User className="w-5 h-5 text-gray-400 dark:text-slate-500 group-focus-within:text-primary-500 dark:group-focus-within:text-primary-400 transition-colors" />
                  </div>
                  <input
                    id="forgot-lastName"
                    type="text"
                    required
                    value={lastName}
                    onChange={(e) => setLastName(e.target.value)}
                    className="w-full pl-12 pr-4 py-3 bg-white/70 dark:bg-slate-900/60 border-2 border-gray-200 dark:border-slate-600 text-gray-900 dark:text-slate-100 rounded-xl focus:outline-none focus:border-primary-500 dark:focus:border-primary-400 focus:bg-white dark:focus:bg-slate-900 focus:ring-4 focus:ring-primary-500/10 dark:focus:ring-primary-400/20 transition-all duration-200 placeholder:text-gray-400 dark:placeholder:text-slate-500"
                    placeholder={t('auth.forgot.lastNamePlaceholder')}
                  />
                  <div className="absolute inset-0 rounded-xl bg-gradient-to-r from-primary-500/5 to-purple-500/5 opacity-0 group-focus-within:opacity-100 transition-opacity pointer-events-none" />
                </div>
              </div>
            </div>

            <div className="group">
              <label htmlFor="forgot-email" className="block text-sm font-semibold text-gray-700 dark:text-slate-200 mb-2 ml-1">
                {t('auth.forgot.emailLabel')}
              </label>
              <div className="relative">
                <div className="absolute inset-y-0 left-0 pl-4 flex items-center pointer-events-none group-focus-within:animate-icon-bounce">
                  <Mail className="w-5 h-5 text-gray-400 dark:text-slate-500 group-focus-within:text-primary-500 dark:group-focus-within:text-primary-400 transition-colors" />
                </div>
                <input
                  id="forgot-email"
                  type="email"
                  required
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  className="w-full pl-12 pr-4 py-3 bg-white/70 dark:bg-slate-900/60 border-2 border-gray-200 dark:border-slate-600 text-gray-900 dark:text-slate-100 rounded-xl focus:outline-none focus:border-primary-500 dark:focus:border-primary-400 focus:bg-white dark:focus:bg-slate-900 focus:ring-4 focus:ring-primary-500/10 dark:focus:ring-primary-400/20 transition-all duration-200 placeholder:text-gray-400 dark:placeholder:text-slate-500"
                  placeholder={t('auth.forgot.emailPlaceholder')}
                />
                <div className="absolute inset-0 rounded-xl bg-gradient-to-r from-primary-500/5 to-purple-500/5 opacity-0 group-focus-within:opacity-100 transition-opacity pointer-events-none" />
              </div>
            </div>

            <button
              type="submit"
              disabled={loading}
              className="w-full relative overflow-hidden bg-gradient-to-r from-primary-600 via-primary-500 to-primary-600 dark:from-primary-500 dark:via-primary-400 dark:to-primary-500 hover:from-primary-700 hover:via-primary-600 hover:to-primary-700 dark:hover:from-primary-600 dark:hover:via-primary-500 dark:hover:to-primary-600 disabled:from-gray-400 disabled:via-gray-400 disabled:to-gray-400 text-white font-semibold py-3.5 rounded-xl transition-all duration-300 shadow-lg shadow-primary-500/25 dark:shadow-primary-500/30 hover:shadow-xl hover:shadow-primary-500/30 dark:hover:shadow-primary-400/40 hover:-translate-y-0.5 active:translate-y-0 disabled:cursor-not-allowed disabled:shadow-none disabled:hover:translate-y-0"
            >
              <span className="absolute inset-0 -translate-x-full bg-gradient-to-r from-transparent via-white/20 to-transparent animate-shine" />
              <span className="absolute inset-0 bg-gradient-to-r from-primary-600/0 via-primary-400/30 to-primary-600/0 opacity-0 hover:opacity-100 transition-opacity duration-500" />
              <span className="relative flex items-center justify-center gap-2">
                {loading ? (
                  <>
                    <Loader2 className="animate-spin w-5 h-5" />
                    {t('auth.forgot.submitting')}
                  </>
                ) : (
                  <>
                    {t('auth.forgot.submit')}
                    <svg className="w-4 h-4 transition-transform group-hover:translate-x-1" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                      <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M13 7l5 5m0 0l-5 5m5-5H6" />
                    </svg>
                  </>
                )}
              </span>
            </button>
          </form>

          <div className="mt-6 text-center">
            <Link to="/login" className="inline-flex items-center gap-2 text-primary-600 dark:text-primary-400 hover:text-primary-700 dark:hover:text-primary-300 font-medium transition-colors hover:underline underline-offset-2 group">
              <ArrowLeft className="w-4 h-4 transition-transform group-hover:-translate-x-1" />
              {t('auth.common.backToLogin')}
            </Link>
          </div>

          <div className="mt-7 pt-6 border-t border-gray-200/60 dark:border-slate-700/60">
            <div className="flex items-center justify-center gap-2 text-xs text-gray-400 dark:text-slate-500">
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z" />
              </svg>
              <span>© 2024 NoneadCSS. All rights reserved.</span>
            </div>
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
        @keyframes gradient-shift {
          0%, 100% { opacity: 0.3; transform: rotate(0deg); }
          50% { opacity: 0.5; transform: rotate(180deg); }
        }
        .animate-gradient-shift {
          animation: gradient-shift 6s ease-in-out infinite;
        }
        @keyframes icon-bounce {
          0%, 100% { transform: translateY(0); }
          50% { transform: translateY(-2px); }
        }
        .animate-icon-bounce {
          animation: icon-bounce 0.3s ease-in-out;
        }
      `}</style>
    </div>
  );
}