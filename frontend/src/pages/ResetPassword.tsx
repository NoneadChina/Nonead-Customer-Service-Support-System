import { useState, useEffect, FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { authApi } from '../api';
import { useI18n } from '../context/I18nContext';
import { usePasswordPolicy } from '../hooks/usePasswordPolicy';
import PasswordPolicyHint from '../components/PasswordPolicyHint';

export default function ResetPassword() {
  const { t } = useI18n();
  const [searchParams] = useSearchParams();
  const token = searchParams.get('token');

  const { policy, validate: validatePassword } = usePasswordPolicy();
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [loading, setLoading] = useState(false);
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!token) {
      setError(t('auth.reset.invalidToken'));
    }
  }, [token, t]);

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setLoading(true);
    setError('');

    if (newPassword !== confirmPassword) {
      setError(t('auth.common.passwordMismatch'));
      setLoading(false);
      return;
    }

    const pwdErrors = validatePassword(newPassword);
    if (pwdErrors.length > 0) {
      setError(pwdErrors.join(t('auth.common.validationErrorSeparator')));
      setLoading(false);
      return;
    }

    try {
      const data = await authApi.resetPassword({ token: token ?? '', newPassword, confirmPassword });
      if (data.success) {
        setSuccess(true);
      } else {
        setError(data.message || t('auth.reset.failed'));
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('auth.common.networkError'));
    } finally {
      setLoading(false);
    }
  };

  if (success) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-primary-600 via-primary-700 to-primary-900 px-4">
        <div className="max-w-md w-full bg-white dark:bg-slate-800 rounded-2xl shadow-2xl p-8 text-center">
          <div className="inline-flex items-center justify-center w-16 h-16 bg-green-100 dark:bg-green-900/30 rounded-full mb-4">
            <svg className="w-8 h-8 text-green-600 dark:text-green-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
            </svg>
          </div>
          <h2 className="text-xl font-bold text-gray-900 dark:text-slate-100 mb-2">{t('auth.reset.successTitle')}</h2>
          <p className="text-gray-500 dark:text-slate-400 text-sm mb-6">{t('auth.reset.successDescription')}</p>
          <Link
            to="/login"
            className="inline-block w-full bg-primary-600 hover:bg-primary-700 text-white font-medium py-2.5 rounded-lg transition shadow-sm"
          >
            {t('auth.common.goToLogin')}
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen flex items-center justify-center bg-gradient-to-br from-primary-600 via-primary-700 to-primary-900 px-4">
      <div className="max-w-md w-full bg-white dark:bg-slate-800 rounded-2xl shadow-2xl p-8">
        <div className="text-center mb-8">
          <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100">{t('auth.reset.title')}</h1>
          <p className="text-gray-500 dark:text-slate-400 mt-2 text-sm">{t('auth.reset.subtitle')}</p>
        </div>

        <form onSubmit={handleSubmit} className="space-y-5">
          {error && (
            <div role="alert" className="bg-red-50 dark:bg-red-900/30 border border-red-200 dark:border-red-800 text-red-700 dark:text-red-400 px-4 py-2.5 rounded-lg text-sm">
              {error}
            </div>
          )}

          <div>
            <label htmlFor="reset-newPassword" className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('auth.reset.newPasswordLabel')}</label>
            <input
              id="reset-newPassword"
              type="password"
              required
              disabled={!token}
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
              className="w-full px-4 py-2.5 border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent transition disabled:bg-gray-100 dark:disabled:bg-slate-700"
              placeholder={t('auth.common.passwordMinLengthPlaceholder', { min: policy?.minLength ?? 6 })}
            />
            <PasswordPolicyHint policy={policy} password={newPassword} />
          </div>

          <div>
            <label htmlFor="reset-confirmPassword" className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('auth.reset.confirmPasswordLabel')}</label>
            <input
              id="reset-confirmPassword"
              type="password"
              required
              disabled={!token}
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
              className="w-full px-4 py-2.5 border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-900 text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent transition disabled:bg-gray-100 dark:disabled:bg-slate-700"
              placeholder={t('auth.reset.confirmPasswordPlaceholder')}
            />
          </div>

          <button
            type="submit"
            disabled={loading || !token}
            className="w-full bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 text-white font-medium py-2.5 rounded-lg transition shadow-sm"
          >
            {loading ? t('auth.reset.submitting') : t('auth.reset.submit')}
          </button>
        </form>

        <div className="mt-6 text-center">
          <Link to="/login" className="inline-flex items-center gap-1.5 text-sm text-primary-600 dark:text-primary-400 hover:text-primary-700 dark:hover:text-primary-300">
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M10 19l-7-7m0 0l7-7m-7 7h18" />
            </svg>
            {t('auth.common.backToLogin')}
          </Link>
        </div>
      </div>
    </div>
  );
}
