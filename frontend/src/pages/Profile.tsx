import { User } from 'lucide-react';
import ProfileTab from '../components/ProfileTab';
import { useI18n } from '../context/I18nContext';

export default function Profile() {
  const { t } = useI18n();
  return (
    <div className="space-y-5">
      {/* 渐变页头 */}
      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-slate-800 dark:to-slate-900 p-6 border border-gray-200/50 dark:border-slate-700/50">
        <div className="absolute -top-10 -right-10 w-40 h-40 bg-primary-200/30 dark:bg-primary-900/20 rounded-full blur-3xl pointer-events-none" />
        <div className="relative flex items-start gap-4">
          <div className="w-12 h-12 rounded-xl bg-gradient-to-br from-primary-500 to-indigo-600 text-white flex items-center justify-center shadow-lg shadow-primary-500/30">
            <User className="w-6 h-6" />
          </div>
          <div>
            <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100">{t('settings.profile.pageTitle')}</h1>
            <p className="text-sm text-gray-600 dark:text-slate-400 mt-1">{t('settings.profile.pageDescription')}</p>
          </div>
        </div>
      </div>

      <ProfileTab />
    </div>
  );
}
