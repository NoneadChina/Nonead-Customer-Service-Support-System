import { useEffect, useState, useRef } from 'react';
import { profileApi, mailboxApi, auditLogApi, type ProfileStats } from '../api';
import { useAuth } from '../context/AuthContext';
import { LANGUAGE_OPTIONS, useLanguageSwitch } from '../hooks/useLanguageSwitch';
import type { UserResponse, MailboxResponse, AuditLogResponse } from '../api/types';
import { formatDateTime, formatDate, relativeTime, safeUrl } from '../utils/format';
import { getApiErrorMessage } from '../utils/error';
import { getRoleLabel } from '../utils/permissions';
import PasswordPolicyHint from './PasswordPolicyHint';
import { usePreferences } from '../hooks/usePreferences';
import { useTheme } from '../hooks/useTheme';
import { usePasswordPolicy } from '../hooks/usePasswordPolicy';
import { timezoneOptions } from '../constants/timezones';
import { useI18n } from '../context/I18nContext';
import type { TranslationKey } from '../locales/zh-Hans';
import {
  User,
  Mail,
  Shield,
  Bell,
  MessageSquare,
  Zap,
  Upload,
  Trash2,
  Loader2,
  CheckCircle2,
  AlertCircle,
  LogIn,
  Clock,
  Key,
  SlidersHorizontal,
  Inbox,
  Save,
  ShieldCheck,
  Languages,
  Calendar,
  Globe,
  ChevronRight,
  Network,
  Lock,
  RefreshCw as RefreshIcon,
  MailCheck,
  Activity,
  FileText,
} from 'lucide-react';

function inputClass(extra = '') {
  return `w-full px-3.5 py-2.5 border border-gray-300 dark:border-slate-600 rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition bg-white dark:bg-slate-900 text-gray-900 dark:text-slate-100 ${extra}`;
}

function selectClass(extra = '') {
  return `w-full px-3.5 py-2.5 border border-gray-300 dark:border-slate-600 rounded-xl text-sm bg-white dark:bg-slate-900 text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition ${extra}`;
}

// 左侧锚点导航项。**只持真源 key（labelKey）**，渲染时经 `t()` 解析
const navItems: { id: string; labelKey: TranslationKey; icon: typeof User }[] = [
  { id: 'overview', labelKey: 'settings.profile.nav.overview', icon: User },
  { id: 'stats', labelKey: 'settings.profile.nav.stats', icon: MessageSquare },
  { id: 'basic', labelKey: 'settings.profile.nav.basic', icon: User },
  { id: 'preferences', labelKey: 'settings.profile.nav.preferences', icon: SlidersHorizontal },
  { id: 'password', labelKey: 'settings.profile.nav.password', icon: Key },
  { id: 'history', labelKey: 'settings.profile.nav.history', icon: LogIn },
];

export default function ProfileTab() {
  const { t } = useI18n();
  const { user, logout, updateUser } = useAuth();
  // 语言决策与全局同源：此前的 preferences.language 是纯 localStorage 值（默认写死 zh-Hans），
  // 下拉显示值与界面实际语言可能不一致，且"保存资料"会把它当 locale 写回后端。
  // 切换本身也走统一入口（乐观切换 + 写回资料 + 失败回滚），不再等"保存资料"才持久化。
  const { language, saving: savingLanguage, switchLanguage } = useLanguageSwitch();
  const { policy, validate: validatePassword } = usePasswordPolicy();
  const [profile, setProfile] = useState<UserResponse | null>(user);
  const [firstName, setFirstName] = useState(user?.firstName ?? '');
  const [lastName, setLastName] = useState(user?.lastName ?? '');
  const [oldPassword, setOldPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const messageTimeoutRef = useRef<NodeJS.Timeout | null>(null);
  const [stats, setStats] = useState<ProfileStats | null>(null);

  const { preferences, updatePreferences } = usePreferences(user?.id);
  // 时区是"点保存才生效"的字段（与语言下拉的即时切换语义不同），因此它有自己的本地状态：
  // 初值只作首帧占位（`preferences.timezone` 是纯 localStorage 值，默认写死 Asia/Shanghai），
  // 资料到位后立即由服务端值接管（见下面的 profileApi.get 回调）。
  // 不变量：**ProfileTab 的「保存资料」是时区唯一的持久化入口；下拉初值必须来自服务端，
  // 否则用户改了就存不上**——把 payload 改成读 `profile?.timezone` 会让这个控件彻底失效。
  const [timezone, setTimezone] = useState<string>(preferences.timezone);
  const { theme, setTheme } = useTheme();
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [uploadingAvatar, setUploadingAvatar] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  // 头像优先从后端用户数据读取，localStorage 作为离线缓存回退
  const avatar = profile?.avatar ?? null;

  // 当前滚动到的锚点
  const [activeAnchor, setActiveAnchor] = useState('overview');
  const sectionRefs = useRef<Record<string, HTMLElement | null>>({});

  // 清理 message timeout
  useEffect(() => {
    return () => {
      if (messageTimeoutRef.current) {
        clearTimeout(messageTimeoutRef.current);
      }
    };
  }, []);

  useEffect(() => {
    if (preferences.theme !== theme) {
      setTheme(preferences.theme);
    }
  }, [preferences.theme, theme, setTheme]);

  const handleThemeChange = (newTheme: 'light' | 'dark' | 'system') => {
    updatePreferences({ theme: newTheme });
    setTheme(newTheme);
  };

  // 登录设备列表（从审计日志获取登录记录）
  const [loginHistory, setLoginHistory] = useState<AuditLogResponse[]>([]);

  useEffect(() => {
    // 使用 ignore 标志防止组件卸载后异步回调执行 setState
    let ignore = false;
    profileApi
      .get()
      .then((p) => {
        if (ignore) return;
        setProfile(p);
        setFirstName(p.firstName);
        setLastName(p.lastName);
        // 用服务端已保存的时区给控件做初值：拿不到时才退回本地偏好
        if (p.timezone) setTimezone(p.timezone);
      })
      .catch((err: unknown) => console.error('获取用户资料失败:', err));
    profileApi
      .getStats()
      .then((s) => {
        if (!ignore) setStats(s);
      })
      .catch((err: unknown) => console.error('获取个人统计失败:', err));
    mailboxApi
      .listAll()
      .then((resp) => {
        if (!ignore) setMailboxes((Array.isArray(resp) ? resp : []) as MailboxResponse[]);
      })
      .catch((err: unknown) => console.error('获取邮箱列表失败:', err));
    if (user?.id) {
      auditLogApi
        .getByUser(user.id, 1, 10)
        .then((resp) => {
          if (ignore) return;
          const arr = Array.isArray(resp) ? resp : (resp?.data ?? []);
          setLoginHistory(arr.filter((l) => l.action.toLowerCase() === 'login').slice(0, 5));
        })
        .catch((err: unknown) => console.error('获取登录历史失败:', err));
    }
    return () => {
      ignore = true;
    };
  }, [user?.id]);

  // 滚动监听，更新左侧导航高亮
  useEffect(() => {
    const handleScroll = () => {
      const scrollY = window.scrollY + 120;
      let current = navItems[0]?.id ?? 'overview';
      for (const item of navItems) {
        const el = sectionRefs.current[item.id];
        if (el && el.offsetTop <= scrollY) {
          current = item.id;
        }
      }
      setActiveAnchor(current);
    };
    window.addEventListener('scroll', handleScroll, { passive: true });
    handleScroll();
    return () => window.removeEventListener('scroll', handleScroll);
  }, []);

  const scrollToSection = (id: string) => {
    const el = sectionRefs.current[id];
    if (el) {
      const top = el.offsetTop - 90;
      window.scrollTo({ top, behavior: 'smooth' });
      setActiveAnchor(id);
    }
  };

  const handleSaveProfile = () => {
    setSaving(true);
    setMessage(null);
    profileApi
      .update({
        firstName,
        lastName,
        // 时区取**控件当前值**：它是本页唯一的时区入口，且「保存资料」是它唯一的持久化入口。
        // 不要改成 `profile?.timezone ?? …`——`profile` 恒为非空（useState(user) + profileApi.get），
        // 服务端旧值会永远胜出，用户在这个下拉里改的值就被静默丢弃、页面还报「资料已保存」。
        timezone,
        locale: language,
        // 头像字段必须回传，否则后端 UpdateProfileAsync 会将其置为 null，导致已上传头像丢失
        avatar: profile?.avatar ?? undefined,
      })
      .then((updatedUser) => {
        setProfile(updatedUser);
        updateUser(updatedUser); // 同步全局 user，让顶部导航等位置立即更新
        setMessage({ type: 'success', text: t('settings.profile.basic.saveSucceeded') });
      })
      .catch((err: unknown) => setMessage({ type: 'error', text: getApiErrorMessage(err, t('settings.profile.basic.saveFailed')) }))
      .finally(() => setSaving(false));
  };

  const handleChangePassword = () => {
    if (!oldPassword || !newPassword || !confirmPassword) {
      setMessage({ type: 'error', text: t('settings.profile.password.incomplete') });
      return;
    }
    const pwdErrors = validatePassword(newPassword);
    if (pwdErrors.length > 0) {
      setMessage({ type: 'error', text: pwdErrors.join(t('settings.profile.password.errorSeparator')) });
      return;
    }
    if (newPassword !== confirmPassword) {
      setMessage({ type: 'error', text: t('settings.profile.password.mismatch') });
      return;
    }
    setSaving(true);
    setMessage(null);
    profileApi
      .changePassword({
        currentPassword: oldPassword,
        newPassword,
        confirmPassword,
      })
      .then(() => {
        setMessage({ type: 'success', text: t('settings.profile.password.success') });
        if (messageTimeoutRef.current) {
          clearTimeout(messageTimeoutRef.current);
        }
        messageTimeoutRef.current = setTimeout(() => logout(), 1500);
      })
      .catch((err: unknown) => setMessage({ type: 'error', text: getApiErrorMessage(err, t('settings.profile.password.failed')) }))
      .finally(() => setSaving(false));
  };

  const handleAvatarChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    // 客户端校验：文件类型和大小（与 Customers 头像上传保持一致）
    if (!file.type.startsWith('image/')) {
      setMessage({ type: 'error', text: t('settings.profile.avatar.invalidType') });
      return;
    }
    if (file.size > 2 * 1024 * 1024) {
      setMessage({ type: 'error', text: t('settings.profile.avatar.tooLarge') });
      return;
    }
    setUploadingAvatar(true);
    try {
      // 调用后端头像上传接口，复用通用附件服务（含类型/大小校验、病毒扫描、磁盘持久化）
      const { user: updatedUser } = await profileApi.uploadAvatar(file);
      setProfile(updatedUser);
      updateUser(updatedUser); // 同步全局 user，让顶部导航等位置立即更新
      setMessage({ type: 'success', text: t('settings.profile.avatar.uploadSucceeded') });
    } catch (err: unknown) {
      setMessage({ type: 'error', text: err instanceof Error ? err.message : t('settings.profile.avatar.uploadFailed') });
    } finally {
      setUploadingAvatar(false);
      if (fileInputRef.current) fileInputRef.current.value = '';
    }
  };

  const handleRemoveAvatar = async () => {
    setUploadingAvatar(true);
    try {
      const { user: updatedUser } = await profileApi.deleteAvatar();
      setProfile(updatedUser);
      updateUser(updatedUser); // 同步全局 user
      setMessage({ type: 'success', text: t('settings.profile.avatar.removeSucceeded') });
    } catch (err: unknown) {
      setMessage({ type: 'error', text: err instanceof Error ? err.message : t('settings.profile.avatar.removeFailed') });
    } finally {
      setUploadingAvatar(false);
    }
  };

  const handleSavePreferences = () => {
    setMessage({ type: 'success', text: t('settings.profile.preferences.saveSucceeded') });
    if (messageTimeoutRef.current) {
      clearTimeout(messageTimeoutRef.current);
    }
    messageTimeoutRef.current = setTimeout(() => setMessage(null), 2000);
  };

  const initials = (profile?.firstName?.charAt(0) ?? '') + (profile?.lastName?.charAt(0) ?? '');

  /**
   * 在线时长。数值由代码算出，**单位文案进字典**（不套 Intl：这是时长而非时刻）。
   * 每种组合一条 key，保留原有的"无空格"中文写法（`3小时20分`）。
   */
  const formatOnlineDuration = (minutes?: number) => {
    if (minutes == null || minutes <= 0) return t('settings.profile.duration.zero');
    if (minutes < 60) return t('settings.profile.duration.minutes', { minutes });
    const hours = Math.floor(minutes / 60);
    const remainingMinutes = minutes % 60;
    if (hours < 24) {
      return remainingMinutes > 0
        ? t('settings.profile.duration.hoursMinutes', { hours, minutes: remainingMinutes })
        : t('settings.profile.duration.hours', { hours });
    }
    const days = Math.floor(hours / 24);
    const remainingHours = hours % 24;
    return remainingHours > 0
      ? t('settings.profile.duration.daysHours', { days, hours: remainingHours })
      : t('settings.profile.duration.days', { days });
  };

  return (
    <div className="space-y-5">
      {message && (
        <div className={`px-4 py-3 rounded-xl text-sm flex items-center gap-2 ${
          message.type === 'success'
            ? 'bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-700/50 text-green-700 dark:text-green-400'
            : 'bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-700/50 text-red-700 dark:text-red-400'
        }`}>
          {message.type === 'success' ? (
            <CheckCircle2 className="w-5 h-5 flex-shrink-0" />
          ) : (
            <AlertCircle className="w-5 h-5 flex-shrink-0" />
          )}
          {message.text}
        </div>
      )}

      <div className="grid grid-cols-1 lg:grid-cols-[200px_1fr] gap-5">
        {/* 左侧锚点导航 */}
        <aside className="hidden lg:block">
          <div className="sticky top-20 bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 p-2 shadow-sm">
            <nav className="space-y-1">
              {navItems.map((item) => {
                const isActive = activeAnchor === item.id;
                return (
                  <button
                    key={item.id}
                    onClick={() => scrollToSection(item.id)}
                    className={`w-full flex items-center gap-2.5 px-3 py-2 text-sm rounded-xl transition-all text-left ${
                      isActive
                        ? 'bg-primary-50 dark:bg-primary-900/20 text-primary-700 dark:text-primary-400 font-medium'
                        : 'text-gray-600 dark:text-slate-400 hover:bg-gray-50 dark:hover:bg-slate-700/50'
                    }`}
                  >
                    <item.icon className="w-4 h-4 flex-shrink-0" />
                    <span className="truncate">{t(item.labelKey)}</span>
                    {isActive && <ChevronRight className="w-3.5 h-3.5 ml-auto" />}
                  </button>
                );
              })}
            </nav>
          </div>
        </aside>

        {/* 右侧主内容 */}
        <div className="space-y-5 min-w-0">
          {/* 用户概览卡片 */}
          <section
            ref={(el) => { sectionRefs.current['overview'] = el; }}
            className="bg-gradient-to-br from-primary-500 via-primary-600 to-indigo-600 rounded-2xl p-6 shadow-lg shadow-primary-500/20 relative overflow-hidden"
          >
            <div className="absolute -top-20 -right-20 w-60 h-60 bg-white/10 rounded-full blur-3xl" />
            <div className="absolute -bottom-20 -left-20 w-40 h-40 bg-white/10 rounded-full blur-2xl" />
            <div className="relative flex items-start gap-6">
              <div className="relative">
                {avatar ? (
                  <img src={safeUrl(avatar)} alt={t('settings.profile.avatarAlt')} loading="lazy" className="w-20 h-20 rounded-2xl object-cover border-3 border-white/30 shadow-lg" />
                ) : (
                  <div className="w-20 h-20 rounded-2xl bg-white/20 backdrop-blur text-white flex items-center justify-center text-3xl font-bold border-3 border-white/30">
                    {initials || '?'}
                  </div>
                )}
                <div className="absolute -bottom-1 -right-1 w-6 h-6 bg-green-400 rounded-full border-3 border-primary-500" />
              </div>
              <div className="flex-1 text-white min-w-0">
                <h2 className="text-2xl font-bold truncate">{profile?.firstName} {profile?.lastName}</h2>
                <p className="text-white/80 text-sm mt-1 flex items-center gap-1">
                  <Mail className="w-4 h-4" />
                  <span className="truncate">{profile?.email}</span>
                </p>
                <p className="text-white/70 text-xs mt-2 flex items-center gap-1">
                  <Shield className="w-3.5 h-3.5" />
                  {getRoleLabel(profile?.role ?? 0)}
                </p>
              </div>
              <div className="flex-shrink-0">
                <input
                  ref={fileInputRef}
                  type="file"
                  accept="image/*"
                  onChange={handleAvatarChange}
                  className="hidden"
                />
                <button
                  onClick={() => fileInputRef.current?.click()}
                  disabled={uploadingAvatar}
                  className="inline-flex items-center gap-1.5 px-4 py-2 text-sm bg-white/20 hover:bg-white/30 disabled:bg-white/10 text-white rounded-xl backdrop-blur transition active:scale-[0.98]"
                >
                  <Upload className="w-4 h-4" />
                  {uploadingAvatar ? t('settings.profile.uploading') : t('settings.profile.changeAvatar')}
                </button>
                {avatar && (
                  <button
                    onClick={handleRemoveAvatar}
                    disabled={uploadingAvatar}
                    className="inline-flex items-center gap-1.5 px-4 py-2 text-sm bg-white/10 hover:bg-white/20 disabled:bg-white/5 text-white/90 rounded-xl backdrop-blur transition active:scale-[0.98] mt-2 w-full"
                  >
                    <Trash2 className="w-4 h-4" />
                    {uploadingAvatar ? t('settings.profile.processing') : t('settings.profile.removeAvatar')}
                  </button>
                )}
              </div>
            </div>
          </section>

          {/* 统计卡片 */}
          <section
            ref={(el) => { sectionRefs.current['stats'] = el; }}
            className="grid grid-cols-2 lg:grid-cols-4 gap-4"
          >
            <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200/50 dark:border-slate-700/50 p-4 shadow-sm hover:shadow-md transition">
              <div className="flex items-center gap-3">
                <div className="w-10 h-10 rounded-xl bg-blue-100 dark:bg-blue-900/30 flex items-center justify-center">
                  <MessageSquare className="w-5 h-5 text-blue-600 dark:text-blue-400" />
                </div>
                <div className="min-w-0">
                  <p className="text-xs text-gray-500 dark:text-slate-400">{t('settings.profile.stats.conversations')}</p>
                  <p className="text-xl font-bold text-gray-900 dark:text-slate-100">{stats?.conversationCount ?? '--'}</p>
                </div>
              </div>
            </div>
            <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200/50 dark:border-slate-700/50 p-4 shadow-sm hover:shadow-md transition">
              <div className="flex items-center gap-3">
                <div className="w-10 h-10 rounded-xl bg-green-100 dark:bg-green-900/30 flex items-center justify-center">
                  <MailCheck className="w-5 h-5 text-green-600 dark:text-green-400" />
                </div>
                <div className="min-w-0">
                  <p className="text-xs text-gray-500 dark:text-slate-400">{t('settings.profile.stats.replies')}</p>
                  <p className="text-xl font-bold text-gray-900 dark:text-slate-100">{stats?.replyCount ?? '--'}</p>
                </div>
              </div>
            </div>
            <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200/50 dark:border-slate-700/50 p-4 shadow-sm hover:shadow-md transition">
              <div className="flex items-center gap-3">
                <div className="w-10 h-10 rounded-xl bg-amber-100 dark:bg-amber-900/30 flex items-center justify-center">
                  <Clock className="w-5 h-5 text-amber-600 dark:text-amber-400" />
                </div>
                <div className="min-w-0">
                  <p className="text-xs text-gray-500 dark:text-slate-400">{t('settings.profile.stats.onlineDuration')}</p>
                  <p className="text-xl font-bold text-gray-900 dark:text-slate-100">{formatOnlineDuration(stats?.onlineMinutes)}</p>
                </div>
              </div>
            </div>
            <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200/50 dark:border-slate-700/50 p-4 shadow-sm hover:shadow-md transition">
              <div className="flex items-center gap-3">
                <div className="w-10 h-10 rounded-xl bg-purple-100 dark:bg-purple-900/30 flex items-center justify-center">
                  <Calendar className="w-5 h-5 text-purple-600 dark:text-purple-400" />
                </div>
                <div className="min-w-0">
                  <p className="text-xs text-gray-500 dark:text-slate-400">{t('settings.profile.stats.registeredAt')}</p>
                  <p className="text-lg font-bold text-gray-900 dark:text-slate-100">{profile?.createdAt ? formatDate(profile.createdAt) : '-'}</p>
                </div>
              </div>
            </div>
          </section>

          {/* 基本资料 */}
          <section
            ref={(el) => { sectionRefs.current['basic'] = el; }}
            className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm scroll-mt-20"
          >
            <div className="px-6 py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-primary-50/50 to-transparent dark:from-primary-900/20">
              <h3 className="font-semibold text-gray-900 dark:text-slate-100 flex items-center gap-2">
                <div className="w-7 h-7 rounded-lg bg-primary-100 dark:bg-primary-900/30 flex items-center justify-center">
                  <User className="w-4 h-4 text-primary-600 dark:text-primary-400" />
                </div>
                {t('settings.profile.basic.title')}
              </h3>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 ml-9">{t('settings.profile.basic.description')}</p>
            </div>
            <div className="p-6">
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-5">
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('settings.profile.basic.firstName')}</label>
                  <input
                    type="text"
                    value={firstName}
                    onChange={(e) => setFirstName(e.target.value)}
                    className={inputClass()}
                    placeholder={t('settings.profile.basic.firstNamePlaceholder')}
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('settings.profile.basic.lastName')}</label>
                  <input
                    type="text"
                    value={lastName}
                    onChange={(e) => setLastName(e.target.value)}
                    className={inputClass()}
                    placeholder={t('settings.profile.basic.lastNamePlaceholder')}
                  />
                </div>
                <div className="sm:col-span-2">
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5 flex items-center gap-2">
                    <Mail className="w-4 h-4 text-gray-400" />
                    {t('settings.profile.basic.email')}
                  </label>
                  <input
                    type="email"
                    value={profile?.email ?? ''}
                    disabled
                    className="w-full px-3.5 py-2.5 border border-gray-200 dark:border-slate-700 rounded-xl bg-gray-50 dark:bg-slate-900 text-gray-500 dark:text-slate-400 text-sm"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5 flex items-center gap-2">
                    <Shield className="w-4 h-4 text-gray-400" />
                    {t('settings.profile.basic.role')}
                  </label>
                  <div className="w-full px-3.5 py-2.5 border border-gray-200 dark:border-slate-700 rounded-xl bg-gray-50 dark:bg-slate-900 text-sm">
                    <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 text-xs font-medium rounded-full bg-primary-100 dark:bg-primary-900/30 text-primary-700 dark:text-primary-400">
                      {getRoleLabel(profile?.role ?? 0)}
                    </span>
                  </div>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5 flex items-center gap-2">
                    <Calendar className="w-4 h-4 text-gray-400" />
                    {t('settings.profile.basic.registeredAt')}
                  </label>
                  <input
                    type="text"
                    value={profile?.createdAt ? formatDateTime(profile.createdAt) : '-'}
                    disabled
                    className="w-full px-3.5 py-2.5 border border-gray-200 dark:border-slate-700 rounded-xl bg-gray-50 dark:bg-slate-900 text-gray-600 dark:text-slate-300 text-sm font-mono"
                  />
                </div>
              </div>
              <div className="mt-5 flex justify-end">
                <button
                  onClick={handleSaveProfile}
                  disabled={saving}
                  className="inline-flex items-center gap-2 px-5 py-2.5 bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 text-white text-sm rounded-xl font-medium shadow-sm shadow-primary-500/20 transition hover:shadow-lg hover:shadow-primary-500/30 active:scale-[0.98]"
                >
                  {saving && <Loader2 className="w-4 h-4 animate-spin" />}
                  {saving ? t('settings.profile.basic.saving') : t('settings.profile.basic.save')}
                </button>
              </div>
            </div>
          </section>

          {/* 偏好设置 */}
          <section
            ref={(el) => { sectionRefs.current['preferences'] = el; }}
            className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm scroll-mt-20"
          >
            <div className="px-6 py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-indigo-50/50 to-transparent dark:from-indigo-900/20">
              <h3 className="font-semibold text-gray-900 dark:text-slate-100 flex items-center gap-2">
                <div className="w-7 h-7 rounded-lg bg-indigo-100 dark:bg-indigo-900/30 flex items-center justify-center">
                  <SlidersHorizontal className="w-4 h-4 text-indigo-600 dark:text-indigo-400" />
                </div>
                {t('settings.profile.preferences.title')}
              </h3>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 ml-9">{t('settings.profile.preferences.description')}</p>
            </div>
            <div className="p-6">
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-5">
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5 flex items-center gap-2">
                    <Inbox className="w-4 h-4 text-gray-400" />
                    {t('settings.profile.preferences.defaultMailbox')}
                  </label>
                  <select
                    value={preferences.defaultMailboxId ?? ''}
                    onChange={(e) => updatePreferences({ defaultMailboxId: e.target.value === '' ? null : Number(e.target.value) })}
                    className={selectClass()}
                  >
                    <option value="">{t('settings.profile.preferences.noMailbox')}</option>
                    {mailboxes.map((m) => (
                      <option key={m.id} value={m.id}>{m.name}</option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5 flex items-center gap-2">
                    <FileText className="w-4 h-4 text-gray-400" />
                    {t('settings.profile.preferences.pageSize')}
                  </label>
                  <select
                    value={preferences.pageSize}
                    onChange={(e) => updatePreferences({ pageSize: Number(e.target.value) })}
                    className={selectClass()}
                  >
                    <option value={10}>{t('settings.profile.preferences.pageSizeOption', { count: 10 })}</option>
                    <option value={20}>{t('settings.profile.preferences.pageSizeOption', { count: 20 })}</option>
                    <option value={50}>{t('settings.profile.preferences.pageSizeOption', { count: 50 })}</option>
                    <option value={100}>{t('settings.profile.preferences.pageSizeOption', { count: 100 })}</option>
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5 flex items-center gap-2">
                    <Globe className="w-4 h-4 text-gray-400" />
                    {t('settings.profile.preferences.timezone')}
                  </label>
                  <select
                    value={timezone}
                    onChange={(e) => setTimezone(e.target.value)}
                    className={selectClass()}
                  >
                    {timezoneOptions.map((tz) => (
                      <option key={tz.value} value={tz.value}>{tz.label}</option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5 flex items-center gap-2">
                    <Languages className="w-4 h-4 text-gray-400" />
                    {t('settings.profile.preferences.language')}
                  </label>
                  <select
                    value={language}
                    onChange={(e) => void switchLanguage(e.target.value)}
                    // 与顶部切换器同一语义：写回在途时禁用，避免点击被静默丢弃
                    disabled={savingLanguage}
                    className={selectClass('disabled:opacity-60 disabled:cursor-not-allowed')}
                  >
                    {LANGUAGE_OPTIONS.map((option) => (
                      <option key={option.value} value={option.value}>
                        {option.label}
                      </option>
                    ))}
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5 flex items-center gap-2">
                    <Zap className="w-4 h-4 text-gray-400" />
                    {t('settings.profile.preferences.theme')}
                  </label>
                  <select
                    value={preferences.theme}
                    onChange={(e) => handleThemeChange(e.target.value as 'light' | 'dark' | 'system')}
                    className={selectClass()}
                  >
                    <option value="light">{t('settings.profile.preferences.themeLight')}</option>
                    <option value="dark">{t('settings.profile.preferences.themeDark')}</option>
                    <option value="system">{t('settings.profile.preferences.themeSystem')}</option>
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5 flex items-center gap-2">
                    <RefreshIcon className="w-4 h-4 text-gray-400" />
                    {t('settings.profile.preferences.autoRefreshInterval')}
                  </label>
                  <select
                    value={preferences.autoRefreshInterval}
                    onChange={(e) => updatePreferences({ autoRefreshInterval: Number(e.target.value) })}
                    className={selectClass()}
                  >
                    <option value={0}>{t('settings.profile.preferences.refreshNever')}</option>
                    <option value={30}>{t('settings.profile.preferences.refreshSeconds', { seconds: 30 })}</option>
                    <option value={60}>{t('settings.profile.preferences.refreshSeconds', { seconds: 60 })}</option>
                    <option value={120}>{t('settings.profile.preferences.refreshMinutes', { minutes: 2 })}</option>
                    <option value={300}>{t('settings.profile.preferences.refreshMinutes', { minutes: 5 })}</option>
                  </select>
                </div>
              </div>

              {/* 开关项 */}
              <div className="mt-6 pt-6 border-t border-gray-100 dark:border-slate-700/50">
                <h4 className="text-sm font-medium text-gray-900 dark:text-slate-100 mb-4">{t('settings.profile.preferences.notificationSettings')}</h4>
                <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                  {([
                    { key: 'emailNotifications', labelKey: 'settings.profile.preferences.emailNotifications', descKey: 'settings.profile.preferences.emailNotificationsDesc', icon: <Mail className="w-4 h-4" /> },
                    { key: 'desktopNotifications', labelKey: 'settings.profile.preferences.desktopNotifications', descKey: 'settings.profile.preferences.desktopNotificationsDesc', icon: <Bell className="w-4 h-4" /> },
                    { key: 'soundEnabled', labelKey: 'settings.profile.preferences.soundEnabled', descKey: 'settings.profile.preferences.soundEnabledDesc', icon: <Activity className="w-4 h-4" /> },
                  ] satisfies { key: string; labelKey: TranslationKey; descKey: TranslationKey; icon: React.ReactNode }[]).map((item) => (
                    <button
                      key={item.key}
                      type="button"
                      onClick={() => updatePreferences({ [item.key]: !preferences[item.key as keyof typeof preferences] } as Partial<typeof preferences>)}
                      className={`w-full p-4 rounded-xl border-2 text-left transition-all hover:shadow-md ${
                        preferences[item.key as keyof typeof preferences]
                          ? 'border-primary-200 dark:border-primary-700/50 bg-primary-50/50 dark:bg-primary-900/10'
                          : 'border-gray-200 dark:border-slate-700/50 hover:border-gray-300 dark:hover:border-slate-600'
                      }`}
                    >
                      <div className="flex items-center gap-3">
                        <div className={`w-9 h-9 rounded-lg flex items-center justify-center ${
                          preferences[item.key as keyof typeof preferences]
                            ? 'bg-primary-100 dark:bg-primary-900/30 text-primary-600 dark:text-primary-400'
                            : 'bg-gray-100 dark:bg-slate-700 text-gray-500 dark:text-slate-400'
                        }`}>
                          {item.icon}
                        </div>
                        <div className="flex-1 min-w-0">
                          <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{t(item.labelKey)}</div>
                          <div className="text-xs text-gray-500 dark:text-slate-400 truncate">{t(item.descKey)}</div>
                        </div>
                        <div className={`relative inline-flex h-5 w-10 items-center rounded-full transition-colors ${
                          preferences[item.key as keyof typeof preferences] ? 'bg-primary-600' : 'bg-gray-300 dark:bg-slate-600'
                        }`}>
                          <span className={`inline-block h-3.5 w-3.5 transform rounded-full bg-white transition-transform ${
                            preferences[item.key as keyof typeof preferences] ? 'translate-x-5' : 'translate-x-0.5'
                          }`} />
                        </div>
                      </div>
                    </button>
                  ))}
                </div>
              </div>

              <div className="mt-5 flex justify-end">
                <button
                  onClick={handleSavePreferences}
                  className="inline-flex items-center gap-2 px-5 py-2.5 bg-primary-600 hover:bg-primary-700 text-white text-sm rounded-xl font-medium shadow-sm shadow-primary-500/20 transition hover:shadow-lg hover:shadow-primary-500/30 active:scale-[0.98]"
                >
                  <Save className="w-4 h-4" />
                  {t('settings.profile.preferences.save')}
                </button>
              </div>
            </div>
          </section>

          {/* 修改密码 */}
          <section
            ref={(el) => { sectionRefs.current['password'] = el; }}
            className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm scroll-mt-20"
          >
            <div className="px-6 py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-rose-50/50 to-transparent dark:from-rose-900/20">
              <h3 className="font-semibold text-gray-900 dark:text-slate-100 flex items-center gap-2">
                <div className="w-7 h-7 rounded-lg bg-rose-100 dark:bg-rose-900/30 flex items-center justify-center">
                  <Key className="w-4 h-4 text-rose-600 dark:text-rose-400" />
                </div>
                {t('settings.profile.password.title')}
              </h3>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 ml-9">{t('settings.profile.password.description')}</p>
            </div>
            <div className="p-6">
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5 flex items-center gap-2">
                    <Lock className="w-4 h-4 text-gray-400" />
                    {t('settings.profile.password.current')}
                  </label>
                  <input
                    type="password"
                    value={oldPassword}
                    onChange={(e) => setOldPassword(e.target.value)}
                    className={inputClass()}
                    placeholder={t('settings.profile.password.currentPlaceholder')}
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5 flex items-center gap-2">
                    <Key className="w-4 h-4 text-gray-400" />
                    {t('settings.profile.password.new')}
                  </label>
                  <input
                    type="password"
                    value={newPassword}
                    onChange={(e) => setNewPassword(e.target.value)}
                    className={inputClass()}
                    placeholder={t('settings.profile.password.newPlaceholder', { count: policy?.minLength ?? 6 })}
                  />
                </div>
                <div className="lg:col-span-2">
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5 flex items-center gap-2">
                    <Key className="w-4 h-4 text-gray-400" />
                    {t('settings.profile.password.confirm')}
                  </label>
                  <input
                    type="password"
                    value={confirmPassword}
                    onChange={(e) => setConfirmPassword(e.target.value)}
                    className={inputClass()}
                    placeholder={t('settings.profile.password.confirmPlaceholder')}
                  />
                </div>
                <div className="lg:col-span-2">
                  <PasswordPolicyHint policy={policy} password={newPassword} />
                </div>
              </div>
              <div className="mt-5 flex justify-end">
                <button
                  onClick={handleChangePassword}
                  disabled={saving}
                  className="inline-flex items-center gap-2 px-5 py-2.5 bg-rose-600 hover:bg-rose-700 disabled:bg-rose-300 text-white text-sm rounded-xl font-medium shadow-sm shadow-rose-500/20 transition hover:shadow-lg hover:shadow-rose-500/30 active:scale-[0.98]"
                >
                  {saving && <Loader2 className="w-4 h-4 animate-spin" />}
                  {saving ? t('settings.profile.password.submitting') : t('settings.profile.password.submit')}
                </button>
              </div>
            </div>
          </section>

          {/* 登录设备/历史 */}
          <section
            ref={(el) => { sectionRefs.current['history'] = el; }}
            className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm scroll-mt-20"
          >
            <div className="px-6 py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-sky-50/50 to-transparent dark:from-sky-900/20">
              <div className="flex items-center justify-between">
                <div>
                  <h3 className="font-semibold text-gray-900 dark:text-slate-100 flex items-center gap-2">
                    <div className="w-7 h-7 rounded-lg bg-sky-100 dark:bg-sky-900/30 flex items-center justify-center">
                      <LogIn className="w-4 h-4 text-sky-600 dark:text-sky-400" />
                    </div>
                    {t('settings.profile.history.title')}
                  </h3>
                  <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 ml-9">{t('settings.profile.history.description')}</p>
                </div>
                <span className="inline-flex items-center gap-1.5 text-xs font-medium text-green-700 dark:text-green-400 bg-green-100 dark:bg-green-900/30 px-3 py-1.5 rounded-full">
                  <span className="w-1.5 h-1.5 bg-green-500 rounded-full animate-pulse"></span>
                  {t('settings.profile.history.online')}
                </span>
              </div>
            </div>
            <div className="p-6">
              {loginHistory.length === 0 ? (
                <div className="text-center py-10">
                  <div className="w-14 h-14 mx-auto mb-3 rounded-2xl bg-gray-100 dark:bg-slate-700 flex items-center justify-center">
                    <ShieldCheck className="w-7 h-7 text-gray-400 dark:text-slate-500" />
                  </div>
                  <p className="text-sm text-gray-500 dark:text-slate-400">{t('settings.profile.history.empty')}</p>
                  <p className="text-xs text-gray-400 dark:text-slate-500 mt-1">{t('settings.profile.history.emptyHint')}</p>
                </div>
              ) : (
                <div className="space-y-3">
                  {loginHistory.map((log, idx) => (
                    <div key={log.id} className={`flex items-center gap-4 p-4 rounded-xl border-2 transition-all hover:shadow-md ${
                      idx === 0
                        ? 'border-green-200 dark:border-green-700/50 bg-green-50/30 dark:bg-green-900/10'
                        : 'border-gray-100 dark:border-slate-700/30 hover:border-gray-200 dark:hover:border-slate-600'
                    }`}>
                      <div className={`w-10 h-10 rounded-xl flex items-center justify-center ${
                        idx === 0 ? 'bg-green-100 dark:bg-green-900/30 text-green-600 dark:text-green-400' : 'bg-gray-100 dark:bg-slate-700 text-gray-500 dark:text-slate-400'
                      }`}>
                        <LogIn className="w-5 h-5" />
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2">
                          <span className="text-sm font-medium text-gray-900 dark:text-slate-100">
                            {idx === 0 ? t('settings.profile.history.currentSession') : t('settings.profile.history.entryNumber', { index: loginHistory.length - idx })}
                          </span>
                          {idx === 0 && (
                            <span className="inline-flex items-center gap-1 text-xs font-medium text-green-700 dark:text-green-400 bg-green-100 dark:bg-green-900/30 px-2 py-0.5 rounded-full">
                              <span className="w-1.5 h-1.5 bg-green-500 rounded-full animate-pulse"></span>
                              {t('settings.profile.history.active')}
                            </span>
                          )}
                        </div>
                        <div className="text-xs text-gray-500 dark:text-slate-400 flex items-center gap-3 mt-1">
                          <span className="inline-flex items-center gap-1" title={formatDateTime(log.createdAt)}>
                            <Clock className="w-3 h-3" />
                            {relativeTime(log.createdAt)}
                          </span>
                          {log.ipAddress && (
                            <span className="inline-flex items-center gap-1 font-mono">
                              <Network className="w-3 h-3" />
                              {log.ipAddress}
                            </span>
                          )}
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              )}
              <div className="mt-5 pt-5 border-t border-gray-100 dark:border-slate-700/50 flex items-center justify-between">
                <p className="text-xs text-gray-500 dark:text-slate-400">{t('settings.profile.history.warning')}</p>
                <button
                  onClick={() => scrollToSection('password')}
                  className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-primary-600 dark:text-primary-400 bg-primary-50 dark:bg-primary-900/20 rounded-lg hover:bg-primary-100 dark:hover:bg-primary-900/30 transition"
                >
                  <Key className="w-3 h-3" />
                  {t('settings.profile.password.submit')}
                  <ChevronRight className="w-3 h-3" />
                </button>
              </div>
            </div>
          </section>
        </div>
      </div>
    </div>
  );
}
