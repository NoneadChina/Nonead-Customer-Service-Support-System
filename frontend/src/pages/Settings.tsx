import { useCallback, useEffect, useState } from 'react';
import { systemOptionApi, localizationApi, mailApi, mailboxApi, systemApi, type SystemInfo } from '../api';
import { useTimedToast } from '../hooks/useTimedToast';
import type {
  MailboxResponse,
  SystemOptionResponse,
  CreateSystemOptionRequest,
  UpdateSystemOptionRequest,
} from '../api/types';
import { formatDateTime } from '../utils/format';
import { toIntlLocale } from '../i18n/locale';
import { useI18n } from '../context/I18nContext';
import type { TranslationKey } from '../locales/zh-Hans';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import { timezoneOptions } from '../constants/timezones';
import {
  Settings as SettingsIcon,
  Mail,
  Info,
  Shield,
  Bell,
  MessageSquare,
  Zap,
  Trash2,
  Loader2,
  CheckCircle2,
  AlertCircle,
  Clock,
  Send,
  Inbox,
  RefreshCw,
  Server,
  Cpu,
  HardDrive,
  Plus,
  Edit2,
  Save,
  Languages,
  Globe,
  Database,
  MailCheck,
  MailX,
  ExternalLink,
  Layers,
  BookOpen,
  UserCheck,
} from 'lucide-react';

type TabKey = 'system' | 'mail' | 'info';
type SystemSubTab = 'general' | 'email' | 'security' | 'notification' | 'conversation' | 'advanced';

interface TabItem {
  key: TabKey;
  /** 词典 key 而非文案：模块级常量拿不到 `t()`，渲染处写 `t(tab.labelKey)` */
  labelKey: TranslationKey;
  icon: React.ReactNode;
}

const tabs: TabItem[] = [
  {
    key: 'system',
    labelKey: 'settings.page.tab.system',
    icon: <SettingsIcon className="w-4 h-4" />,
  },
  {
    key: 'mail',
    labelKey: 'settings.page.tab.mail',
    icon: <Mail className="w-4 h-4" />,
  },
  {
    key: 'info',
    labelKey: 'settings.page.tab.info',
    icon: <Info className="w-4 h-4" />,
  },
];

// ====== 系统配置 Tab ======
const systemSubTabs: { key: SystemSubTab; labelKey: TranslationKey; icon: React.ReactNode; color: string }[] = [
  { key: 'general', labelKey: 'settings.page.subTab.general', icon: <SettingsIcon className="w-4 h-4" />, color: 'primary' },
  { key: 'email', labelKey: 'settings.page.subTab.email', icon: <Mail className="w-4 h-4" />, color: 'blue' },
  { key: 'security', labelKey: 'settings.page.subTab.security', icon: <Shield className="w-4 h-4" />, color: 'red' },
  { key: 'notification', labelKey: 'settings.page.subTab.notification', icon: <Bell className="w-4 h-4" />, color: 'purple' },
  { key: 'conversation', labelKey: 'settings.page.subTab.conversation', icon: <MessageSquare className="w-4 h-4" />, color: 'cyan' },
  { key: 'advanced', labelKey: 'settings.page.subTab.advanced', icon: <Zap className="w-4 h-4" />, color: 'amber' },
];

/**
 * `app.date_format` 的"中文年月日"形态 code。
 *
 * 它是**持久化 code**（前端写库、原样读回；后端 `SystemOptionSeeder` 的种子值是 `yyyy-MM-dd`，
 * 且全仓库对 `app.date_format` 无任何消费点），**不是展示文案**，所以既不能翻译、也不该被当成漏译。
 * 展示文案见字典 `settings.page.dateFormat.cjk`。
 *
 * ⚠ 这里**刻意写成可读的字面量**：`check-hardcoded-chinese.mjs` 会把这三个汉字计入基线，
 * 那是**已登记的接受例外**（与 `useLanguageSwitch` 的语言自称同类：不随界面语言变化的数据）。
 * **不要**为了骗过门禁把它改成 `String.fromCharCode(...)` 或 `\uXXXX` 转义——
 * 门禁的作用是暴露"待译文案"，不是逼代码变晦涩；真要修就该给门禁加一条显式豁免标记（见批次 2 收尾打磨）。
 */
const CJK_YMD_DATE_FORMAT_CODE = 'yyyy年MM月dd日'; // i18n-exempt: 持久化数据（app.date_format 的入库选项值），非文案

/**
 * 下拉选项表：`value` 是**入库 code**（后端/库里的原样值），`labelKey` 是展示文案。
 * 同形选项（"每 N 分钟 / N 封 / N 位 / N 次"）只用一个带 `{count}` 的键，不逐值造键。
 */
const LANGUAGE_OPTIONS: { value: string; labelKey: TranslationKey }[] = [
  { value: 'zh-Hans', labelKey: 'settings.page.language.zhHans' },
  { value: 'zh-Hant', labelKey: 'settings.page.language.zhHant' },
  { value: 'en', labelKey: 'settings.page.language.en' },
  { value: 'ja', labelKey: 'settings.page.language.ja' },
];

const CURRENCY_OPTIONS: { value: string; labelKey: TranslationKey }[] = [
  { value: 'CNY', labelKey: 'settings.page.currency.cny' },
  { value: 'USD', labelKey: 'settings.page.currency.usd' },
  { value: 'EUR', labelKey: 'settings.page.currency.eur' },
  { value: 'JPY', labelKey: 'settings.page.currency.jpy' },
  { value: 'GBP', labelKey: 'settings.page.currency.gbp' },
];

const DATE_FORMAT_OPTIONS: { value: string; labelKey: TranslationKey }[] = [
  { value: 'yyyy-MM-dd', labelKey: 'settings.page.dateFormat.iso' },
  { value: 'yyyy/MM/dd', labelKey: 'settings.page.dateFormat.slashYmd' },
  { value: 'MM/dd/yyyy', labelKey: 'settings.page.dateFormat.mdy' },
  { value: 'dd/MM/yyyy', labelKey: 'settings.page.dateFormat.dmy' },
  { value: CJK_YMD_DATE_FORMAT_CODE, labelKey: 'settings.page.dateFormat.cjk' },
];

/** 收取频率：8 档同形文案（`每 N 分钟`），键为 `settings.page.email.fetchFrequency.every` */
const FETCH_FREQUENCY_CODES = ['1', '2', '3', '5', '10', '15', '30', '60'];

/** 单次最大收取数：5 档同形文案（`N 封`） */
const MAX_FETCH_PER_RUN_CODES = ['10', '20', '50', '100', '200'];

/** 最小密码长度：5 档同形文案（`N 位`） */
const PASSWORD_MIN_LENGTH_CODES = ['6', '8', '10', '12', '16'];

/** 最大登录尝试次数：3 档同形文案（`N 次`），外加 `999` = 不限制 */
const MAX_LOGIN_ATTEMPT_CODES = ['3', '5', '10'];

const SESSION_TIMEOUT_OPTIONS: { value: string; labelKey: TranslationKey }[] = [
  { value: '30', labelKey: 'settings.page.session.timeout.minutes30' },
  { value: '60', labelKey: 'settings.page.session.timeout.hours1' },
  { value: '120', labelKey: 'settings.page.session.timeout.hours2' },
  { value: '240', labelKey: 'settings.page.session.timeout.hours4' },
  { value: '480', labelKey: 'settings.page.session.timeout.hours8' },
  { value: '1440', labelKey: 'settings.page.session.timeout.hours24' },
  { value: '10080', labelKey: 'settings.page.session.timeout.days7' },
];

/** 新会话默认状态：code 与 `conversation.default_status` 的入库值一致（**不是** `format.status.*` 的码，勿混用） */
const DEFAULT_STATUS_OPTIONS: { value: string; labelKey: TranslationKey }[] = [
  { value: 'active', labelKey: 'settings.page.conversation.defaultStatus.active' },
  { value: 'pending', labelKey: 'settings.page.conversation.defaultStatus.pending' },
  { value: 'open', labelKey: 'settings.page.conversation.defaultStatus.open' },
];

const AUTO_CLOSE_OPTIONS: { value: string; labelKey: TranslationKey }[] = [
  { value: '24', labelKey: 'settings.page.conversation.autoClose.hours24' },
  { value: '48', labelKey: 'settings.page.conversation.autoClose.days2' },
  { value: '72', labelKey: 'settings.page.conversation.autoClose.days3' },
  { value: '120', labelKey: 'settings.page.conversation.autoClose.days5' },
  { value: '168', labelKey: 'settings.page.conversation.autoClose.days7' },
  { value: '336', labelKey: 'settings.page.conversation.autoClose.days14' },
  { value: '720', labelKey: 'settings.page.conversation.autoClose.days30' },
  { value: '0', labelKey: 'settings.page.conversation.autoClose.never' },
];

const THREAD_TIME_OPTIONS: { value: string; labelKey: TranslationKey }[] = [
  { value: 'absolute', labelKey: 'settings.page.conversation.threadTime.absolute' },
  { value: 'relative', labelKey: 'settings.page.conversation.threadTime.relative' },
  { value: 'both', labelKey: 'settings.page.conversation.threadTime.both' },
];

const SIGNATURE_POSITION_OPTIONS: { value: string; labelKey: TranslationKey }[] = [
  { value: 'before_quote', labelKey: 'settings.page.conversation.signaturePosition.beforeQuote' },
  { value: 'after_quote', labelKey: 'settings.page.conversation.signaturePosition.afterQuote' },
  { value: 'bottom', labelKey: 'settings.page.conversation.signaturePosition.bottom' },
];


function Toggle({
  checked,
  onChange,
  label,
  description,
}: {
  checked: boolean;
  onChange: (v: boolean) => void;
  label: string;
  description?: string;
}) {
  return (
    <label className="flex items-start justify-between p-4 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 cursor-pointer border border-transparent hover:border-gray-100 dark:hover:border-slate-700/50 transition">
      <div className="pr-4">
        <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{label}</div>
        {description && <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">{description}</div>}
      </div>
      <button
        type="button"
        onClick={() => onChange(!checked)}
        className={`relative inline-flex h-6 w-11 flex-shrink-0 items-center rounded-full transition-colors duration-200 ease-in-out focus:outline-none focus:ring-2 focus:ring-primary-500 focus:ring-offset-2 ${
          checked ? 'bg-primary-600' : 'bg-gray-300'
        }`}
      >
        <span
          className={`inline-block h-4 w-4 transform rounded-full bg-white shadow transition-transform duration-200 ease-in-out ${
            checked ? 'translate-x-6' : 'translate-x-1'
          }`}
        />
      </button>
    </label>
  );
}

function SettingField({
  label,
  hint,
  children,
}: {
  label: string;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{label}</label>
      {children}
      {hint && <p className="text-xs text-gray-500 dark:text-slate-400 mt-1.5">{hint}</p>}
    </div>
  );
}

function inputClass(extra = '') {
  return `w-full px-3.5 py-2.5 border border-gray-300 dark:border-slate-600 rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition bg-white dark:bg-slate-900 text-gray-900 dark:text-slate-100 ${extra}`;
}

function selectClass(extra = '') {
  return `w-full px-3.5 py-2.5 border border-gray-300 dark:border-slate-600 rounded-xl text-sm bg-white dark:bg-slate-900 text-gray-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition ${extra}`;
}

function SystemOptionsTab() {
  const { t } = useI18n();
  const [subTab, setSubTab] = useState<SystemSubTab>('general');
  const [options, setOptions] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const { message, show: showMessage, clear: clearMessage } = useTimedToast();

  // 高级配置模式
  const [advancedList, setAdvancedList] = useState<SystemOptionResponse[]>([]);
  const [modalOpen, setModalOpen] = useState(false);
  const [editingName, setEditingName] = useState<string | null>(null);
  const [form, setForm] = useState<CreateSystemOptionRequest>({ name: '', value: '', category: '' });
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState('');
  const [deleteTarget, setDeleteTarget] = useState<SystemOptionResponse | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [filterCategory, setFilterCategory] = useState('');

  const fetchOptions = useCallback(async () => {
    setLoading(true);
    try {
      const dict = await systemOptionApi.getDictionary();
      setOptions(dict);
      const list = await systemOptionApi.list();
      setAdvancedList(Array.isArray(list) ? list : []);
    } catch (err: unknown) {
      showMessage({ type: 'error', text: err instanceof Error ? err.message : t('settings.page.loadFailed') });
    } finally {
      setLoading(false);
    }
    // `t` 刻意不进依赖：它随界面语言**和** DB 覆盖层加载而换身份，进依赖会让首屏重复请求
    // `getDictionary` / `list`（波次 4 在同类位置上实测过）。错误兜底文案取的是"出错那一刻"的
    // 语言，可接受；本处沿用仓库既有的"带理由 disable"写法。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [showMessage]);

  useEffect(() => {
    fetchOptions();
  }, [fetchOptions]);

  const setOpt = (key: string, value: string) => {
    setOptions((prev) => ({ ...prev, [key]: value }));
  };

  const getBool = (key: string, def = false) => {
    const v = options[key];
    if (v === undefined) return def;
    return v === 'true' || v === '1';
  };

  const setBool = (key: string, v: boolean) => setOpt(key, v ? 'true' : 'false');

  const handleSave = async () => {
    setSaving(true);
    clearMessage();
    try {
      await systemOptionApi.batchUpdate(options);
      showMessage({ type: 'success', text: t('settings.page.saved') });
    } catch (err: unknown) {
      showMessage({ type: 'error', text: err instanceof Error ? err.message : t('settings.page.saveFailed') });
    } finally {
      setSaving(false);
    }
  };

  // ====== 高级配置方法 ======
  const categories = Array.from(new Set(advancedList.map((o) => o.category).filter((c): c is string => !!c)));
  const filteredAdvanced = filterCategory === '' ? advancedList : advancedList.filter((o) => o.category === filterCategory);

  const openCreate = () => {
    setEditingName(null);
    setForm({ name: '', value: '', category: '' });
    setFormError('');
    setModalOpen(true);
  };

  const openEdit = (o: SystemOptionResponse) => {
    setEditingName(o.name);
    setForm({ name: o.name, value: o.value, category: o.category ?? '' });
    setFormError('');
    setModalOpen(true);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!form.name.trim()) {
      setFormError(t('settings.page.advanced.nameRequired'));
      return;
    }
    setSubmitting(true);
    setFormError('');
    try {
      const payload: CreateSystemOptionRequest = {
        name: form.name.trim(),
        value: form.value,
        category: form.category?.trim() || null,
      };
      if (editingName) {
        const updatePayload: UpdateSystemOptionRequest = { ...payload };
        await systemOptionApi.update(editingName, updatePayload);
      } else {
        await systemOptionApi.create(payload);
      }
      setModalOpen(false);
      await fetchOptions();
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : t('settings.page.operationFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await systemOptionApi.delete(deleteTarget.name);
      setDeleteTarget(null);
      await fetchOptions();
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : t('settings.page.deleteFailed'));
    } finally {
      setDeleting(false);
    }
  };

  return (
    <div className="space-y-4 sm:space-y-5">
      {message && (
        <div
          className={`px-3 sm:px-4 py-3 rounded-xl text-xs sm:text-sm flex items-center gap-2 ${
            message.type === 'success'
              ? 'bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-700/50 text-green-700 dark:text-green-400'
              : 'bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-700/50 text-red-700 dark:text-red-400'
          }`}
        >
          {message.type === 'success' ? (
            <CheckCircle2 className="w-4 h-4 sm:w-5 sm:h-5 flex-shrink-0" />
          ) : (
            <AlertCircle className="w-4 h-4 sm:w-5 sm:h-5 flex-shrink-0" />
          )}
          {message.text}
        </div>
      )}

      {/* 二级 Tab - 移动端横向滚动 */}
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 p-1.5 sm:p-2 flex gap-1.5 sm:gap-2 shadow-sm overflow-x-auto no-scrollbar">
        {systemSubTabs.map((tab) => {
          const colorMap: Record<string, { active: string; icon: string; bg: string }> = {
            primary: { active: 'bg-primary-600 text-white shadow-primary-500/20', icon: 'text-primary-500', bg: 'bg-primary-50 dark:bg-primary-900/20' },
            blue: { active: 'bg-blue-600 text-white shadow-blue-500/20', icon: 'text-blue-500', bg: 'bg-blue-50 dark:bg-blue-900/20' },
            red: { active: 'bg-red-600 text-white shadow-red-500/20', icon: 'text-red-500', bg: 'bg-red-50 dark:bg-red-900/20' },
            purple: { active: 'bg-purple-600 text-white shadow-purple-500/20', icon: 'text-purple-500', bg: 'bg-purple-50 dark:bg-purple-900/20' },
            cyan: { active: 'bg-cyan-600 text-white shadow-cyan-500/20', icon: 'text-cyan-500', bg: 'bg-cyan-50 dark:bg-cyan-900/20' },
            amber: { active: 'bg-amber-600 text-white shadow-amber-500/20', icon: 'text-amber-500', bg: 'bg-amber-50 dark:bg-amber-900/20' },
          };
          const colors = colorMap[tab.color];
          const isActive = subTab === tab.key;
          return (
            <button
              key={tab.key}
              onClick={() => setSubTab(tab.key)}
              className={`inline-flex items-center gap-1.5 sm:gap-2 px-3 sm:px-4 py-2 sm:py-2.5 text-xs sm:text-sm font-medium rounded-xl transition-all whitespace-nowrap flex-shrink-0 ${
                isActive
                  ? `${colors.active} shadow-sm hover:shadow-lg`
                  : `text-gray-600 dark:text-slate-400 hover:${colors.bg} hover:text-gray-900 dark:hover:text-slate-100`
              }`}
            >
              {tab.icon}
              {t(tab.labelKey)}
            </button>
          );
        })}
      </div>

      {loading ? (
        <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 p-12 text-center shadow-sm">
          <div className="inline-flex items-center justify-center w-12 h-12 rounded-full bg-primary-50 dark:bg-primary-900/30 mb-3">
            <Loader2 className="w-6 h-6 text-primary-600 dark:text-primary-400 animate-spin" />
          </div>
          <p className="text-sm text-gray-500 dark:text-slate-400">{t('common.shell.loading')}</p>
        </div>
      ) : (
        <>
          {/* 常规设置 */}
          {subTab === 'general' && (
            <div className="space-y-4 sm:space-y-5">
              <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm">
                <div className="px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-primary-50/50 to-transparent dark:from-primary-900/20">
                  <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base">{t('settings.page.general.title')}</h3>
                  <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">{t('settings.page.general.description')}</p>
                </div>
                <div className="p-4 sm:p-6 space-y-4 sm:space-y-5">
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 sm:gap-5">
                    <SettingField label={t('settings.page.general.systemName')} hint={t('settings.page.general.systemNameHint')}>
                      <input
                        type="text"
                        value={options['app.name'] ?? ''}
                        onChange={(e) => setOpt('app.name', e.target.value)}
                        className={inputClass()}
                      />
                    </SettingField>
                    <SettingField label={t('settings.page.general.companyName')} hint={t('settings.page.general.companyNameHint')}>
                      <input
                        type="text"
                        value={options['app.company_name'] ?? ''}
                        onChange={(e) => setOpt('app.company_name', e.target.value)}
                        className={inputClass()}
                      />
                    </SettingField>
                    <SettingField label={t('settings.page.general.defaultLanguage')}>
                      <select
                        value={options['app.locale'] ?? 'zh-Hans'}
                        onChange={(e) => setOpt('app.locale', e.target.value)}
                        className={selectClass()}
                      >
                        {LANGUAGE_OPTIONS.map((opt) => (
                          <option key={opt.value} value={opt.value}>{t(opt.labelKey)}</option>
                        ))}
                      </select>
                    </SettingField>
                    <SettingField label={t('settings.page.general.defaultTimezone')}>
                      <select
                        value={options['app.timezone'] ?? 'Asia/Shanghai'}
                        onChange={(e) => setOpt('app.timezone', e.target.value)}
                        className={selectClass()}
                      >
                        {timezoneOptions.map((tz) => (
                          <option key={tz.value} value={tz.value}>{tz.label}</option>
                        ))}
                      </select>
                    </SettingField>
                    <SettingField label={t('settings.page.general.currency')}>
                      <select
                        value={options['app.currency'] ?? 'CNY'}
                        onChange={(e) => setOpt('app.currency', e.target.value)}
                        className={selectClass()}
                      >
                        {CURRENCY_OPTIONS.map((opt) => (
                          <option key={opt.value} value={opt.value}>{t(opt.labelKey)}</option>
                        ))}
                      </select>
                    </SettingField>
                    <SettingField label={t('settings.page.general.dateFormat')}>
                      <select
                        value={options['app.date_format'] ?? 'yyyy-MM-dd'}
                        onChange={(e) => setOpt('app.date_format', e.target.value)}
                        className={selectClass()}
                      >
                        {DATE_FORMAT_OPTIONS.map((opt) => (
                          <option key={opt.value} value={opt.value}>{t(opt.labelKey)}</option>
                        ))}
                      </select>
                    </SettingField>
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* 邮件设置 */}
          {subTab === 'email' && (
            <div className="space-y-4 sm:space-y-5">
              <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm">
                <div className="px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-blue-50/50 to-transparent dark:from-blue-900/20">
                  <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base">{t('settings.page.email.title')}</h3>
                  <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">{t('settings.page.email.description')}</p>
                </div>
                <div className="p-4 sm:p-6 space-y-4 sm:space-y-5">
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 sm:gap-5">
                    <SettingField label={t('settings.page.email.fromName')} hint={t('settings.page.email.fromNameHint')}>
                      <input
                        type="text"
                        value={options['mail.from_name'] ?? ''}
                        onChange={(e) => setOpt('mail.from_name', e.target.value)}
                        className={inputClass()}
                      />
                    </SettingField>
                    <SettingField label={t('settings.page.email.fetchFrequency')} hint={t('settings.page.email.fetchFrequencyHint')}>
                      <select
                        value={options['mail.fetch_frequency'] ?? '5'}
                        onChange={(e) => setOpt('mail.fetch_frequency', e.target.value)}
                        className={selectClass()}
                      >
                        {FETCH_FREQUENCY_CODES.map((code) => (
                          <option key={code} value={code}>
                            {t('settings.page.email.fetchFrequency.every', { count: code })}
                          </option>
                        ))}
                      </select>
                    </SettingField>
                    <SettingField label={t('settings.page.email.maxFetchPerRun')} hint={t('settings.page.email.maxFetchPerRunHint')}>
                      <select
                        value={options['mail.max_fetch_per_run'] ?? '50'}
                        onChange={(e) => setOpt('mail.max_fetch_per_run', e.target.value)}
                        className={selectClass()}
                      >
                        {MAX_FETCH_PER_RUN_CODES.map((code) => (
                          <option key={code} value={code}>
                            {t('settings.page.email.maxFetchPerRun.count', { count: code })}
                          </option>
                        ))}
                      </select>
                    </SettingField>
                  </div>
                  <div className="border-t border-gray-100 dark:border-slate-700/50 pt-5 mt-2">
                    <h4 className="text-sm font-medium text-gray-900 dark:text-slate-100 mb-2">{t('settings.page.email.behavior')}</h4>
                    <div className="space-y-1">
                      <Toggle
                        checked={getBool('mail.mark_as_read', true)}
                        onChange={(v) => setBool('mail.mark_as_read', v)}
                        label={t('settings.page.email.markAsRead')}
                        description={t('settings.page.email.markAsReadDescription')}
                      />
                      <Toggle
                        checked={getBool('mail.save_to_sent', true)}
                        onChange={(v) => setBool('mail.save_to_sent', v)}
                        label={t('settings.page.email.saveToSent')}
                        description={t('settings.page.email.saveToSentDescription')}
                      />
                      <Toggle
                        checked={getBool('mail.delete_after_fetch', false)}
                        onChange={(v) => setBool('mail.delete_after_fetch', v)}
                        label={t('settings.page.email.deleteAfterFetch')}
                        description={t('settings.page.email.deleteAfterFetchDescription')}
                      />
                    </div>
                  </div>
                </div>
              </div>

              {/* 客户信息及联系方式收集 */}
              <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm">
                <div className="px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-blue-50/50 to-transparent dark:from-blue-900/20">
                  <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base">{t('settings.page.email.contactCollection.title')}</h3>
                  <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">{t('settings.page.email.contactCollection.description')}</p>
                </div>
                <div className="p-4 sm:p-6">
                  <div
                    className={`relative overflow-hidden rounded-xl border p-4 transition-all duration-300 ${
                      getBool('email.contact_collection_enabled', true)
                        ? 'bg-gradient-to-r from-blue-50/80 to-cyan-50/50 border-blue-200 dark:from-blue-900/20 dark:to-cyan-900/10 dark:border-blue-800/50 shadow-sm shadow-blue-500/5'
                        : 'bg-gray-50/80 border-gray-200 dark:bg-slate-800/50 dark:border-slate-700'
                    }`}
                  >
                    <div className="flex items-start gap-3">
                      {/* 图标 */}
                      <div
                        className={`flex-shrink-0 w-10 h-10 rounded-xl flex items-center justify-center transition-all duration-300 ${
                          getBool('email.contact_collection_enabled', true)
                            ? 'bg-blue-100 dark:bg-blue-900/40 ring-2 ring-blue-200 dark:ring-blue-800/50'
                            : 'bg-gray-200 dark:bg-slate-700'
                        }`}
                      >
                        <UserCheck
                          className={`w-5 h-5 transition-colors ${
                            getBool('email.contact_collection_enabled', true)
                              ? 'text-blue-600 dark:text-blue-400'
                              : 'text-gray-500 dark:text-slate-400'
                          }`}
                        />
                      </div>

                      {/* 文案 */}
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 mb-0.5">
                          <label className="text-sm font-semibold text-gray-900 dark:text-slate-100">
                            {t('settings.page.email.contactCollection.title')}
                          </label>
                          <span
                            className={`inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium transition-all ${
                              getBool('email.contact_collection_enabled', true)
                                ? 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300'
                                : 'bg-gray-200 text-gray-600 dark:bg-slate-700 dark:text-slate-400'
                            }`}
                          >
                            <span
                              className={`inline-block w-1.5 h-1.5 rounded-full ${
                                getBool('email.contact_collection_enabled', true) ? 'bg-blue-500 animate-pulse' : 'bg-gray-400'
                              }`}
                            />
                            {getBool('email.contact_collection_enabled', true) ? t('settings.page.status.enabled') : t('settings.page.status.disabled')}
                          </span>
                        </div>
                        <p className="text-xs text-gray-500 dark:text-slate-400 leading-relaxed">
                          {t('settings.page.email.contactCollection.detail')}
                        </p>
                      </div>

                      {/* 开关 */}
                      <button
                        type="button"
                        role="switch"
                        aria-checked={getBool('email.contact_collection_enabled', true)}
                        onClick={() => setBool('email.contact_collection_enabled', !getBool('email.contact_collection_enabled', true))}
                        className={`relative inline-flex h-6 w-11 flex-shrink-0 items-center rounded-full transition-all duration-300 focus:outline-none focus:ring-2 focus:ring-offset-2 focus:ring-blue-500 dark:focus:ring-offset-slate-900 ${
                          getBool('email.contact_collection_enabled', true)
                            ? 'bg-blue-600 shadow-md shadow-blue-500/30'
                            : 'bg-gray-300 dark:bg-slate-600'
                        }`}
                      >
                        <span
                          className={`inline-block h-5 w-5 transform rounded-full bg-white shadow-sm transition-transform duration-300 ${
                            getBool('email.contact_collection_enabled', true) ? 'translate-x-5' : 'translate-x-0.5'
                          }`}
                        />
                      </button>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* 安全设置 */}
          {subTab === 'security' && (
            <div className="space-y-4 sm:space-y-5">
              <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm">
                <div className="px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-red-50/50 to-transparent dark:from-red-900/20">
                  <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base">{t('settings.page.security.title')}</h3>
                  <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">{t('settings.page.security.description')}</p>
                </div>
                <div className="p-4 sm:p-6 space-y-4 sm:space-y-5">
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 sm:gap-5">
                    <SettingField label={t('settings.page.security.passwordMinLength')}>
                      <select
                        value={options['security.password_min_length'] ?? '10'}
                        onChange={(e) => setOpt('security.password_min_length', e.target.value)}
                        className={selectClass()}
                      >
                        {PASSWORD_MIN_LENGTH_CODES.map((code) => (
                          <option key={code} value={code}>
                            {t('settings.page.security.passwordMinLength.chars', { count: code })}
                          </option>
                        ))}
                      </select>
                    </SettingField>
                    <SettingField label={t('settings.page.security.maxLoginAttempts')} hint={t('settings.page.security.maxLoginAttemptsHint')}>
                      <select
                        value={options['security.max_login_attempts'] ?? '5'}
                        onChange={(e) => setOpt('security.max_login_attempts', e.target.value)}
                        className={selectClass()}
                      >
                        {MAX_LOGIN_ATTEMPT_CODES.map((code) => (
                          <option key={code} value={code}>
                            {t('settings.page.security.maxLoginAttempts.times', { count: code })}
                          </option>
                        ))}
                        <option value="999">{t('settings.page.security.maxLoginAttempts.unlimited')}</option>
                      </select>
                    </SettingField>
                  </div>
                  <div className="space-y-1">
                    <Toggle
                      checked={getBool('security.password_requires_uppercase')}
                      onChange={(v) => setBool('security.password_requires_uppercase', v)}
                      label={t('settings.page.security.requireUppercase')}
                      description={t('settings.page.security.requireUppercaseDescription')}
                    />
                    <Toggle
                      checked={getBool('security.password_requires_lowercase')}
                      onChange={(v) => setBool('security.password_requires_lowercase', v)}
                      label={t('settings.page.security.requireLowercase')}
                      description={t('settings.page.security.requireLowercaseDescription')}
                    />
                    <Toggle
                      checked={getBool('security.password_requires_number')}
                      onChange={(v) => setBool('security.password_requires_number', v)}
                      label={t('settings.page.security.requireNumber')}
                      description={t('settings.page.security.requireNumberDescription')}
                    />
                    <Toggle
                      checked={getBool('security.password_requires_symbol')}
                      onChange={(v) => setBool('security.password_requires_symbol', v)}
                      label={t('settings.page.security.requireSymbol')}
                      description={t('settings.page.security.requireSymbolDescription')}
                    />
                  </div>
                </div>
              </div>

              <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm">
                <div className="px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-orange-50/50 to-transparent dark:from-orange-900/20">
                  <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base">{t('settings.page.session.title')}</h3>
                </div>
                <div className="p-4 sm:p-6 space-y-4 sm:space-y-5">
                  <SettingField label={t('settings.page.session.sessionTimeout')} hint={t('settings.page.session.sessionTimeoutHint')}>
                    <select
                      value={options['security.session_timeout'] ?? '120'}
                      onChange={(e) => setOpt('security.session_timeout', e.target.value)}
                      className={selectClass()}
                    >
                      {SESSION_TIMEOUT_OPTIONS.map((opt) => (
                        <option key={opt.value} value={opt.value}>{t(opt.labelKey)}</option>
                      ))}
                    </select>
                  </SettingField>
                  <Toggle
                    checked={getBool('security.allow_registration')}
                    onChange={(v) => setBool('security.allow_registration', v)}
                    label={t('settings.page.session.allowRegistration')}
                    description={t('settings.page.session.allowRegistrationDescription')}
                  />
                </div>
              </div>
            </div>
          )}

          {/* 通知设置 */}
          {subTab === 'notification' && (
            <div className="space-y-4 sm:space-y-5">
              <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm">
                <div className="px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-purple-50/50 to-transparent dark:from-purple-900/20">
                  <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base">{t('settings.page.notification.title')}</h3>
                  <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">{t('settings.page.notification.description')}</p>
                </div>
                <div className="p-4 sm:p-6 space-y-1">
                  <Toggle
                    checked={getBool('notify.new_conversation', true)}
                    onChange={(v) => setBool('notify.new_conversation', v)}
                    label={t('settings.page.notification.newConversation')}
                    description={t('settings.page.notification.newConversationDescription')}
                  />
                  <Toggle
                    checked={getBool('notify.customer_reply', true)}
                    onChange={(v) => setBool('notify.customer_reply', v)}
                    label={t('settings.page.notification.customerReply')}
                    description={t('settings.page.notification.customerReplyDescription')}
                  />
                  <Toggle
                    checked={getBool('notify.new_reply', true)}
                    onChange={(v) => setBool('notify.new_reply', v)}
                    label={t('settings.page.notification.newReply')}
                    description={t('settings.page.notification.newReplyDescription')}
                  />
                  <Toggle
                    checked={getBool('notify.mention', true)}
                    onChange={(v) => setBool('notify.mention', v)}
                    label={t('settings.page.notification.mention')}
                    description={t('settings.page.notification.mentionDescription')}
                  />
                  <Toggle
                    checked={getBool('notify.assignment', true)}
                    onChange={(v) => setBool('notify.assignment', v)}
                    label={t('settings.page.notification.assignment')}
                    description={t('settings.page.notification.assignmentDescription')}
                  />
                </div>
              </div>
            </div>
          )}

          {/* 会话设置 */}
          {subTab === 'conversation' && (
            <div className="space-y-4 sm:space-y-5">
              <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm">
                <div className="px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-cyan-50/50 to-transparent dark:from-cyan-900/20">
                  <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base">{t('settings.page.conversation.title')}</h3>
                  <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">{t('settings.page.conversation.description')}</p>
                </div>
                <div className="p-4 sm:p-6 space-y-4 sm:space-y-5">
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 sm:gap-5">
                    <SettingField label={t('settings.page.conversation.defaultStatus')}>
                      <select
                        value={options['conversation.default_status'] ?? 'active'}
                        onChange={(e) => setOpt('conversation.default_status', e.target.value)}
                        className={selectClass()}
                      >
                        {DEFAULT_STATUS_OPTIONS.map((opt) => (
                          <option key={opt.value} value={opt.value}>{t(opt.labelKey)}</option>
                        ))}
                      </select>
                    </SettingField>
                    <SettingField label={t('settings.page.conversation.autoClose')} hint={t('settings.page.conversation.autoCloseHint')}>
                      <select
                        value={options['conversation.auto_close_hours'] ?? '168'}
                        onChange={(e) => setOpt('conversation.auto_close_hours', e.target.value)}
                        className={selectClass()}
                      >
                        {AUTO_CLOSE_OPTIONS.map((opt) => (
                          <option key={opt.value} value={opt.value}>{t(opt.labelKey)}</option>
                        ))}
                      </select>
                    </SettingField>
                    <SettingField label={t('settings.page.conversation.threadTime')}>
                      <select
                        value={options['conversation.show_thread_time'] ?? 'absolute'}
                        onChange={(e) => setOpt('conversation.show_thread_time', e.target.value)}
                        className={selectClass()}
                      >
                        {THREAD_TIME_OPTIONS.map((opt) => (
                          <option key={opt.value} value={opt.value}>{t(opt.labelKey)}</option>
                        ))}
                      </select>
                    </SettingField>
                    <SettingField label={t('settings.page.conversation.signaturePosition')}>
                      <select
                        value={options['conversation.signature_position'] ?? 'before_quote'}
                        onChange={(e) => setOpt('conversation.signature_position', e.target.value)}
                        className={selectClass()}
                      >
                        {SIGNATURE_POSITION_OPTIONS.map((opt) => (
                          <option key={opt.value} value={opt.value}>{t(opt.labelKey)}</option>
                        ))}
                      </select>
                    </SettingField>
                  </div>
                  <div className="border-t border-gray-100 dark:border-slate-700/50 pt-5 mt-2">
                    <Toggle
                      checked={getBool('conversation.allow_delete', true)}
                      onChange={(v) => setBool('conversation.allow_delete', v)}
                      label={t('settings.page.conversation.allowDelete')}
                      description={t('settings.page.conversation.allowDeleteDescription')}
                    />
                  </div>
                </div>
              </div>


            </div>
          )}

          {/* 高级配置 */}
          {subTab === 'advanced' && (
            <div className="space-y-4">
              <div className="bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-700/50 rounded-2xl p-3 sm:p-4 flex items-start gap-3">
                <AlertCircle className="w-5 h-5 text-amber-600 dark:text-amber-400 flex-shrink-0 mt-0.5" />
                <div>
                  <p className="text-sm font-medium text-amber-800 dark:text-amber-300">{t('settings.page.subTab.advanced')}</p>
                  <p className="text-xs text-amber-700 dark:text-amber-400 mt-0.5">
                    {t('settings.page.advanced.warning')}
                  </p>
                </div>
              </div>

              <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 p-3 sm:p-4 flex flex-col sm:flex-row sm:items-end gap-3 shadow-sm">
                <div className="flex-1 min-w-0">
                  <label className="block text-xs text-gray-500 dark:text-slate-400 mb-1">{t('settings.page.advanced.filterCategory')}</label>
                  <select
                    value={filterCategory}
                    onChange={(e) => setFilterCategory(e.target.value)}
                    className="w-full sm:w-auto border border-gray-300 dark:border-slate-600 rounded-xl px-3 py-2 text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 bg-white dark:bg-slate-900 text-gray-900 dark:text-slate-100 transition"
                  >
                    <option value="">{t('settings.page.advanced.allCategories')}</option>
                    {categories.map((c) => (
                      <option key={c} value={c}>{c}</option>
                    ))}
                  </select>
                </div>
                <div className="sm:ml-auto">
                  <button
                    onClick={openCreate}
                    className="w-full sm:w-auto inline-flex items-center justify-center gap-1.5 px-4 py-2 text-sm bg-primary-600 hover:bg-primary-700 text-white rounded-xl font-medium shadow-sm transition active:scale-[0.98]"
                  >
                    <Plus className="w-4 h-4" />
                    {t('settings.page.advanced.create')}
                  </button>
                </div>
              </div>

              <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm">
                {filteredAdvanced.length === 0 ? (
                  <div className="p-8 sm:p-12 text-center">
                    <HardDrive className="w-10 h-10 sm:w-12 sm:h-12 mx-auto mb-3 text-gray-300 dark:text-slate-600" />
                    <p className="text-sm text-gray-500 dark:text-slate-400">{t('settings.page.advanced.empty')}</p>
                  </div>
                ) : (
                  <>
                    {/* PC 表格视图 */}
                    <div className="hidden md:block overflow-x-auto">
                      <table className="min-w-full divide-y divide-gray-100 dark:divide-slate-700/50">
                        <thead className="bg-gray-50/50 dark:bg-slate-900/50">
                          <tr>
                            <th className="px-4 py-3 text-left text-xs font-semibold text-gray-600 dark:text-slate-400 uppercase tracking-wider">{t('settings.page.advanced.col.name')}</th>
                            <th className="px-4 py-3 text-left text-xs font-semibold text-gray-600 dark:text-slate-400 uppercase tracking-wider">{t('settings.page.advanced.col.value')}</th>
                            <th className="px-4 py-3 text-left text-xs font-semibold text-gray-600 dark:text-slate-400 uppercase tracking-wider">{t('settings.page.advanced.col.category')}</th>
                            <th className="px-4 py-3 text-left text-xs font-semibold text-gray-600 dark:text-slate-400 uppercase tracking-wider">{t('settings.page.advanced.col.updatedAt')}</th>
                            <th className="px-4 py-3 text-right text-xs font-semibold text-gray-600 dark:text-slate-400 uppercase tracking-wider">{t('settings.page.advanced.col.actions')}</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-gray-50 dark:divide-slate-700/30">
                          {filteredAdvanced.map((o) => (
                            <tr key={o.id} className="hover:bg-gray-50/80 dark:hover:bg-slate-700/50 transition">
                              <td className="px-4 py-3 text-sm font-mono font-medium text-gray-900 dark:text-slate-100">{o.name}</td>
                              <td className="px-4 py-3 text-sm text-gray-700 dark:text-slate-300 max-w-xs">
                                <div className="truncate font-mono text-xs" title={o.value}>{o.value || <span className="text-gray-400 dark:text-slate-500">{t('settings.page.advanced.emptyValue')}</span>}</div>
                              </td>
                              <td className="px-4 py-3 text-sm">
                                {o.category ? (
                                  <span className="inline-flex px-2.5 py-0.5 text-xs font-medium rounded-full bg-primary-50 dark:bg-primary-900/30 text-primary-700 dark:text-primary-400">
                                    {o.category}
                                  </span>
                                ) : (
                                  <span className="text-gray-400 dark:text-slate-500 text-xs">-</span>
                                )}
                              </td>
                              <td className="px-4 py-3 text-sm text-gray-500 dark:text-slate-400 whitespace-nowrap text-xs">
                                {formatDateTime(o.updatedAt)}
                              </td>
                              <td className="px-4 py-3 text-sm text-right">
                                <div className="inline-flex items-center gap-1">
                                  <button
                                    onClick={() => openEdit(o)}
                                    className="p-1.5 text-gray-500 dark:text-slate-400 hover:text-primary-600 dark:hover:text-primary-400 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-lg transition"
                                    title={t('common.edit')}
                                  >
                                    <Edit2 className="w-4 h-4" />
                                  </button>
                                  <button
                                    onClick={() => setDeleteTarget(o)}
                                    className="p-1.5 text-gray-500 dark:text-slate-400 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-lg transition"
                                    title={t('common.delete')}
                                  >
                                    <Trash2 className="w-4 h-4" />
                                  </button>
                                </div>
                              </td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>

                    {/* 移动端卡片视图 */}
                    <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/50">
                      {filteredAdvanced.map((o) => (
                        <div key={o.id} className="p-3.5 hover:bg-gray-50/70 dark:hover:bg-slate-700/30 transition">
                          {/* 卡片头部：名称 + 操作按钮 */}
                          <div className="flex items-start justify-between gap-2 mb-2">
                            <div className="min-w-0 flex-1">
                              <div className="text-sm font-mono font-medium text-gray-900 dark:text-slate-100 break-all" title={o.name}>
                                {o.name}
                              </div>
                              {o.category && (
                                <span className="inline-flex mt-1 px-2 py-0.5 text-[10px] font-medium rounded-full bg-primary-50 dark:bg-primary-900/30 text-primary-700 dark:text-primary-400">
                                  {o.category}
                                </span>
                              )}
                            </div>
                            <div className="flex items-center gap-1 flex-shrink-0">
                              <button
                                onClick={() => openEdit(o)}
                                className="p-1.5 text-gray-500 dark:text-slate-400 hover:text-primary-600 dark:hover:text-primary-400 hover:bg-primary-50 dark:hover:bg-primary-900/30 rounded-lg transition"
                                title={t('common.edit')}
                              >
                                <Edit2 className="w-4 h-4" />
                              </button>
                              <button
                                onClick={() => setDeleteTarget(o)}
                                className="p-1.5 text-gray-500 dark:text-slate-400 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-lg transition"
                                title={t('common.delete')}
                              >
                                <Trash2 className="w-4 h-4" />
                              </button>
                            </div>
                          </div>
                          {/* 值 */}
                          <div className="mb-2">
                            <div className="text-[10px] text-gray-400 dark:text-slate-500 mb-0.5">{t('settings.page.advanced.col.value')}</div>
                            <div className="text-xs font-mono text-gray-700 dark:text-slate-300 break-all bg-gray-50 dark:bg-slate-900/50 p-2 rounded-lg max-h-20 overflow-y-auto">
                              {o.value || <span className="text-gray-400 dark:text-slate-500">{t('settings.page.advanced.emptyValue')}</span>}
                            </div>
                          </div>
                          {/* 更新时间 */}
                          <div className="text-[10px] text-gray-400 dark:text-slate-500">
                            {t('settings.page.advanced.updatedAtPrefix')} {formatDateTime(o.updatedAt)}
                          </div>
                        </div>
                      ))}
                    </div>
                  </>
                )}
              </div>

              <Modal
                open={modalOpen}
                title={editingName ? t('settings.page.advanced.editTitle') : t('settings.page.advanced.createTitle')}
                onClose={() => setModalOpen(false)}
                size="md"
                footer={
                  <>
                    <button
                      onClick={() => setModalOpen(false)}
                      disabled={submitting}
                      className="px-4 py-2 text-sm bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 font-medium text-gray-700 dark:text-slate-300 transition active:scale-[0.98]"
                    >
                      {t('common.cancel')}
                    </button>
                    <button
                      type="submit"
                      form="option-form"
                      disabled={submitting}
                      className="inline-flex items-center gap-1.5 px-4 py-2 text-sm bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 text-white rounded-xl font-medium shadow-sm transition active:scale-[0.98]"
                    >
                      {submitting ? <Loader2 className="w-4 h-4 animate-spin" /> : <Save className="w-4 h-4" />}
                      {submitting ? t('settings.page.saving') : t('common.save')}
                    </button>
                  </>
                }
              >
                <form id="option-form" onSubmit={handleSubmit} className="space-y-4">
                  {formError && (
                    <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-700/50 text-red-700 dark:text-red-400 px-3 py-2 rounded-xl text-sm flex items-center gap-2">
                      <AlertCircle className="w-4 h-4 flex-shrink-0" />
                      {formError}
                    </div>
                  )}
                  <div>
                    <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
                      {t('settings.page.advanced.field.name')} <span className="text-red-500">*</span>
                    </label>
                    <input
                      type="text"
                      required
                      value={form.name}
                      onChange={(e) => setForm({ ...form, name: e.target.value })}
                      disabled={!!editingName}
                      className={inputClass('font-mono disabled:bg-gray-50 dark:disabled:bg-slate-900 disabled:text-gray-500 dark:disabled:text-slate-400')}
                      placeholder={t('settings.page.advanced.field.namePlaceholder')}
                    />
                    {editingName && <p className="text-xs text-gray-500 dark:text-slate-400 mt-1">{t('settings.page.advanced.field.nameImmutable')}</p>}
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
                      {t('settings.page.advanced.field.value')} <span className="text-red-500">*</span>
                    </label>
                    <textarea
                      required
                      value={form.value}
                      onChange={(e) => setForm({ ...form, value: e.target.value })}
                      rows={4}
                      className={inputClass('font-mono text-sm')}
                      placeholder={t('settings.page.advanced.field.valuePlaceholder')}
                    />
                  </div>
                  <div>
                    <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
                      {t('settings.page.advanced.field.category')} <span className="text-gray-400 dark:text-slate-500 text-xs">{t('settings.page.advanced.field.optional')}</span>
                    </label>
                    <input
                      type="text"
                      value={form.category ?? ''}
                      onChange={(e) => setForm({ ...form, category: e.target.value })}
                      className={inputClass()}
                      placeholder={t('settings.page.advanced.field.categoryPlaceholder')}
                      list="category-suggestions-settings"
                    />
                    <datalist id="category-suggestions-settings">
                      {categories.map((c) => (
                        <option key={c} value={c} />
                      ))}
                    </datalist>
                  </div>
                </form>
              </Modal>

              <ConfirmDialog
                open={!!deleteTarget}
                title={t('settings.page.advanced.deleteTitle')}
                message={t('settings.page.advanced.deleteConfirm', { name: deleteTarget?.name ?? '' })}
                confirmText={t('common.delete')}
                variant="danger"
                loading={deleting}
                onConfirm={handleDelete}
                onCancel={() => setDeleteTarget(null)}
              />
            </div>
          )}

          {/* 保存按钮（除了高级配置外显示） */}
          {subTab !== 'advanced' && (
            <div className="flex justify-end">
              <button
                onClick={handleSave}
                disabled={saving}
                className="w-full sm:w-auto inline-flex items-center justify-center gap-2 px-6 py-2.5 bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 text-white text-sm rounded-xl font-medium shadow-sm transition active:scale-[0.98]"
              >
                {saving && <Loader2 className="w-4 h-4 animate-spin" />}
                {saving ? t('settings.page.saving') : t('settings.page.advanced.saveSettings')}
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );
}

// ====== 邮件测试 Tab ======
function MailTestTab() {
  const { t } = useI18n();
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [selectedMailbox, setSelectedMailbox] = useState<number | ''>('');
  const [to, setTo] = useState('');
  const [subject, setSubject] = useState('');
  const [body, setBody] = useState('');
  const [sending, setSending] = useState(false);
  const [fetching, setFetching] = useState(false);
  const [result, setResult] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const [fetchResult, setFetchResult] = useState<{ type: 'success' | 'error'; text: string } | null>(null);
  const [mailboxStatus, setMailboxStatus] = useState<Record<number, boolean | undefined>>({});
  const [testingId, setTestingId] = useState<number | null>(null);

  useEffect(() => {
    mailboxApi
      .listAll()
      .then((resp) => {
        const list = Array.isArray(resp) ? resp : [];
        setMailboxes(list);
        if (list.length > 0) setSelectedMailbox(list[0].id);
      })
      .catch((err: unknown) => console.error('获取邮箱列表失败:', err));
  }, []);

  const testMailboxConnection = async (mailboxId: number) => {
    setTestingId(mailboxId);
    setMailboxStatus((prev) => ({ ...prev, [mailboxId]: undefined }));
    try {
      const r = await mailboxApi.testConnection(mailboxId);
      setMailboxStatus((prev) => ({ ...prev, [mailboxId]: r.success }));
    } catch {
      setMailboxStatus((prev) => ({ ...prev, [mailboxId]: false }));
    } finally {
      setTestingId(null);
    }
  };

  const handleSend = async () => {
    if (!selectedMailbox) {
      setResult({ type: 'error', text: t('settings.page.mail.selectMailboxRequired') });
      return;
    }
    if (!to.trim() || !subject.trim() || !body.trim()) {
      setResult({ type: 'error', text: t('settings.page.mail.fillAllFields') });
      return;
    }
    setSending(true);
    setResult(null);
    try {
      const r = await mailApi.send(selectedMailbox, to.trim(), subject.trim(), body.trim());
      setResult({
        type: r.success ? 'success' : 'error',
        text: r.message,
      });
      if (r.success) {
        setTo('');
        setSubject('');
        setBody('');
      }
    } catch (err: unknown) {
      setResult({ type: 'error', text: err instanceof Error ? err.message : t('settings.page.mail.sendFailed') });
    } finally {
      setSending(false);
    }
  };

  const handleFetch = async () => {
    if (!selectedMailbox) {
      setFetchResult({ type: 'error', text: t('settings.page.mail.selectMailboxRequired') });
      return;
    }
    setFetching(true);
    setFetchResult(null);
    try {
      const r = await mailApi.fetch(selectedMailbox);
      setFetchResult({
        type: r.success ? 'success' : 'error',
        text: r.success
          ? t('settings.page.mail.fetchSuccess', { count: r.count })
          : r.message ?? t('settings.page.mail.fetchFailed'),
      });
    } catch (err: unknown) {
      setFetchResult({ type: 'error', text: err instanceof Error ? err.message : t('settings.page.mail.fetchFailed') });
    } finally {
      setFetching(false);
    }
  };

  const resultBox = (r: { type: 'success' | 'error'; text: string } | null) => {
    if (!r) return null;
    return (
      <div className={`mt-4 px-4 py-3 rounded-xl text-sm flex items-center gap-2 ${
        r.type === 'success'
          ? 'bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-700/50 text-green-700 dark:text-green-400'
          : 'bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-700/50 text-red-700 dark:text-red-400'
      }`}>
        {r.type === 'success' ? (
          <CheckCircle2 className="w-5 h-5 flex-shrink-0" />
        ) : (
          <AlertCircle className="w-5 h-5 flex-shrink-0" />
        )}
        {r.text}
      </div>
    );
  };

  return (
    <div className="space-y-4 sm:space-y-5">
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4 sm:gap-5">
        <div className="lg:col-span-1">
          <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm">
            <div className="px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-blue-50/50 to-transparent dark:from-blue-900/20">
              <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base flex items-center gap-2">
                <div className="w-6 h-6 sm:w-7 sm:h-7 rounded-lg bg-blue-100 dark:bg-blue-900/30 flex items-center justify-center">
                  <Inbox className="w-3.5 h-3.5 sm:w-4 sm:h-4 text-blue-600 dark:text-blue-400" />
                </div>
                {t('settings.page.mail.mailboxList')}
              </h3>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 ml-8 sm:ml-9">{t('settings.page.mail.mailboxListHint')}</p>
            </div>
            <div className="p-3 sm:p-4 space-y-3">
              {mailboxes.length === 0 ? (
                <div className="text-center py-6">
                  <Inbox className="w-8 h-8 mx-auto mb-2 text-gray-300 dark:text-slate-600" />
                  <p className="text-sm text-gray-500 dark:text-slate-400">{t('settings.page.mail.noMailbox')}</p>
                </div>
              ) : (
                mailboxes.map((m) => {
                  const status = mailboxStatus[m.id];
                  return (
                    <button
                      key={m.id}
                      onClick={() => setSelectedMailbox(m.id)}
                      className={`w-full p-3 rounded-xl border-2 text-left transition-all ${
                        selectedMailbox === m.id
                          ? 'border-blue-500 bg-blue-50/50 dark:bg-blue-900/20'
                          : 'border-gray-100 dark:border-slate-700/30 hover:border-gray-200 dark:hover:border-slate-600'
                      }`}
                    >
                      <div className="flex items-center justify-between">
                        <div>
                          <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{m.name}</div>
                          <div className="text-xs text-gray-500 dark:text-slate-400 font-mono">{m.email}</div>
                        </div>
                        <div className="flex items-center gap-2">
                          {(status === undefined || testingId === m.id) && (
                            <button
                              onClick={(e) => {
                                e.stopPropagation();
                                testMailboxConnection(m.id);
                              }}
                              disabled={testingId === m.id}
                              className="p-1.5 text-gray-400 hover:text-blue-500 hover:bg-blue-50 dark:hover:bg-blue-900/20 rounded-lg transition disabled:opacity-60 disabled:cursor-not-allowed"
                              title={t('settings.page.mail.testConnection')}
                            >
                              <RefreshCw className={`w-4 h-4 ${testingId === m.id ? 'animate-spin text-blue-500' : ''}`} />
                            </button>
                          )}
                          {status === true && (
                            <span title={t('settings.page.mail.connectionOk')}><MailCheck className="w-5 h-5 text-green-500" /></span>
                          )}
                          {status === false && (
                            <span title={t('settings.page.mail.connectionFailed')}><MailX className="w-5 h-5 text-red-500" /></span>
                          )}
                        </div>
                      </div>
                    </button>
                  );
                })
              )}
            </div>
          </div>
        </div>

        <div className="lg:col-span-2 space-y-4 sm:space-y-5">
          <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm">
            <div className="px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-blue-50/50 to-transparent dark:from-blue-900/20">
              <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base flex items-center gap-2">
                <div className="w-6 h-6 sm:w-7 sm:h-7 rounded-lg bg-blue-100 dark:bg-blue-900/30 flex items-center justify-center">
                  <RefreshCw className="w-3.5 h-3.5 sm:w-4 sm:h-4 text-blue-600 dark:text-blue-400" />
                </div>
                {t('settings.page.mail.fetchTestTitle')}
              </h3>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 ml-8 sm:ml-9">{t('settings.page.mail.fetchTestDescription')}</p>
            </div>
            <div className="p-4 sm:p-6">
              <div className="flex flex-col sm:flex-row sm:items-end gap-3">
                <div className="flex-1 min-w-0">
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('settings.page.mail.selectMailbox')}</label>
                  <select
                    value={selectedMailbox}
                    onChange={(e) => setSelectedMailbox(e.target.value === '' ? '' : Number(e.target.value))}
                    className={selectClass()}
                  >
                    <option value="">{t('settings.page.mail.pleaseSelect')}</option>
                    {mailboxes.map((m) => (
                      <option key={m.id} value={m.id}>{m.name} ({m.email})</option>
                    ))}
                  </select>
                </div>
                <button
                  onClick={handleFetch}
                  disabled={fetching || !selectedMailbox}
                  className="w-full sm:w-auto inline-flex items-center justify-center gap-2 px-5 py-2.5 bg-blue-600 hover:bg-blue-700 disabled:bg-blue-300 dark:disabled:bg-blue-900/50 text-white text-sm rounded-xl font-medium shadow-sm shadow-blue-500/20 transition hover:shadow-lg hover:shadow-blue-500/30 active:scale-[0.98] sm:mb-0"
                >
                  {fetching ? (
                    <Loader2 className="w-4 h-4 animate-spin" />
                  ) : (
                    <RefreshCw className="w-4 h-4" />
                  )}
                  {fetching ? t('settings.page.mail.fetching') : t('settings.page.mail.fetchNow')}
                </button>
              </div>
              {resultBox(fetchResult)}
            </div>
          </div>

          <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm">
            <div className="px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-primary-50/50 to-transparent dark:from-primary-900/20">
              <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base flex items-center gap-2">
                <div className="w-6 h-6 sm:w-7 sm:h-7 rounded-lg bg-primary-100 dark:bg-primary-900/30 flex items-center justify-center">
                  <Send className="w-3.5 h-3.5 sm:w-4 sm:h-4 text-primary-600 dark:text-primary-400" />
                </div>
                {t('settings.page.mail.sendTestTitle')}
              </h3>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 ml-8 sm:ml-9">{t('settings.page.mail.sendTestDescription')}</p>
            </div>
            <div className="p-4 sm:p-6 space-y-4 sm:space-y-5">
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('settings.page.mail.fromMailbox')}</label>
                <select
                  value={selectedMailbox}
                  onChange={(e) => setSelectedMailbox(e.target.value === '' ? '' : Number(e.target.value))}
                  className={selectClass()}
                >
                  <option value="">{t('settings.page.mail.pleaseSelect')}</option>
                  {mailboxes.map((m) => (
                    <option key={m.id} value={m.id}>{m.name} ({m.email})</option>
                  ))}
                </select>
              </div>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 sm:gap-5">
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('settings.page.mail.recipient')} <span className="text-red-500">*</span></label>
                  <input
                    type="email"
                    value={to}
                    onChange={(e) => setTo(e.target.value)}
                    className={inputClass()}
                    placeholder="recipient@example.com"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('settings.page.mail.subject')} <span className="text-red-500">*</span></label>
                  <input
                    type="text"
                    value={subject}
                    onChange={(e) => setSubject(e.target.value)}
                    className={inputClass()}
                    placeholder={t('settings.page.mail.subjectPlaceholder')}
                  />
                </div>
              </div>
              <div>
                <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('settings.page.mail.body')} <span className="text-red-500">*</span></label>
                <textarea
                  value={body}
                  onChange={(e) => setBody(e.target.value)}
                  rows={6}
                  className={inputClass('font-mono text-sm resize-y')}
                  placeholder={t('settings.page.mail.bodyPlaceholder')}
                />
              </div>
              <div className="flex justify-end pt-2">
                <button
                  onClick={handleSend}
                  disabled={sending}
                  className="w-full sm:w-auto inline-flex items-center justify-center gap-2 px-5 py-2.5 bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 text-white text-sm rounded-xl font-medium shadow-sm shadow-primary-500/20 transition hover:shadow-lg hover:shadow-primary-500/30 active:scale-[0.98]"
                >
                  {sending ? (
                    <Loader2 className="w-4 h-4 animate-spin" />
                  ) : (
                    <Send className="w-4 h-4" />
                  )}
                  {sending ? t('settings.page.mail.sending') : t('settings.page.mail.sendMail')}
                </button>
              </div>
              {resultBox(result)}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

// ====== 系统信息 Tab ======
function SystemInfoTab() {
  const { t, language } = useI18n();
  const [languages, setLanguages] = useState<{ current: string; supported: string[] } | null>(null);
  const [systemInfo, setSystemInfo] = useState<SystemInfo | null>(null);

  useEffect(() => {
    localizationApi
      .getLanguages()
      .then((r) => setLanguages(r))
      .catch((err: unknown) => console.error('获取语言列表失败:', err));
    systemApi
      .getInfo()
      .then((r) => setSystemInfo(r))
      .catch((err: unknown) => console.error('获取系统信息失败:', err));
  }, []);

  const formatStartupTime = (iso?: string) => {
    if (!iso) return '--:--';
    const d = new Date(iso);
    if (isNaN(d.getTime())) return '--:--';
    return d.toLocaleString(toIntlLocale(language), {
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    });
  };

  const InfoRow = ({ label, value }: { label: string; value: React.ReactNode }) => (
    <div className="flex flex-col sm:flex-row sm:items-center gap-1.5 py-2.5 border-b border-gray-100 dark:border-slate-700/50 last:border-0">
      <dt className="text-sm text-gray-500 dark:text-slate-400 sm:w-1/3">{label}</dt>
      <dd className="text-sm text-gray-900 dark:text-slate-100 font-medium">{value}</dd>
    </div>
  );

  const techStack = [
    { name: '.NET Core', version: '10.0', color: 'bg-blue-100 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400' },
    { name: 'React', version: '18', color: 'bg-cyan-100 dark:bg-cyan-900/30 text-cyan-600 dark:text-cyan-400' },
    { name: 'Vite', version: '5', color: 'bg-purple-100 dark:bg-purple-900/30 text-purple-600 dark:text-purple-400' },
    { name: 'SQLite', version: '-', color: 'bg-gray-100 dark:bg-slate-700/50 text-gray-600 dark:text-gray-400' },
    { name: 'Tailwind CSS', version: '3', color: 'bg-teal-100 dark:bg-teal-900/30 text-teal-600 dark:text-teal-400' },
    { name: 'Lucide React', version: '-', color: 'bg-slate-100 dark:bg-slate-600/30 text-slate-600 dark:text-slate-400' },
  ];

  return (
    <div className="space-y-4 sm:space-y-5">
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200/50 dark:border-slate-700/50 p-3 sm:p-4 shadow-sm hover:shadow-md transition-shadow">
          <div className="flex items-center gap-2.5 sm:gap-3">
            <div className="w-9 h-9 sm:w-10 sm:h-10 rounded-xl bg-blue-100 dark:bg-blue-900/30 flex items-center justify-center flex-shrink-0">
              <Server className="w-4 h-4 sm:w-5 sm:h-5 text-blue-600 dark:text-blue-400" />
            </div>
            <div className="min-w-0">
              <p className="text-[10px] sm:text-xs text-gray-500 dark:text-slate-400">{t('settings.page.info.environment')}</p>
              <p className="text-sm sm:text-lg font-bold text-gray-900 dark:text-slate-100 truncate">{systemInfo?.environment || '--'}</p>
            </div>
          </div>
        </div>
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200/50 dark:border-slate-700/50 p-3 sm:p-4 shadow-sm hover:shadow-md transition-shadow">
          <div className="flex items-center gap-2.5 sm:gap-3">
            <div className="w-9 h-9 sm:w-10 sm:h-10 rounded-xl bg-green-100 dark:bg-green-900/30 flex items-center justify-center flex-shrink-0">
              <Database className="w-4 h-4 sm:w-5 sm:h-5 text-green-600 dark:text-green-400" />
            </div>
            <div className="min-w-0">
              <p className="text-[10px] sm:text-xs text-gray-500 dark:text-slate-400">{t('settings.page.info.database')}</p>
              <p className="text-sm sm:text-lg font-bold text-gray-900 dark:text-slate-100">SQLite</p>
            </div>
          </div>
        </div>
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200/50 dark:border-slate-700/50 p-3 sm:p-4 shadow-sm hover:shadow-md transition-shadow">
          <div className="flex items-center gap-2.5 sm:gap-3">
            <div className="w-9 h-9 sm:w-10 sm:h-10 rounded-xl bg-purple-100 dark:bg-purple-900/30 flex items-center justify-center flex-shrink-0">
              <Globe className="w-4 h-4 sm:w-5 sm:h-5 text-purple-600 dark:text-purple-400" />
            </div>
            <div className="min-w-0">
              <p className="text-[10px] sm:text-xs text-gray-500 dark:text-slate-400">{t('settings.page.info.currentLanguage')}</p>
              <p className="text-sm sm:text-lg font-bold text-gray-900 dark:text-slate-100 truncate">{languages?.current || '--'}</p>
            </div>
          </div>
        </div>
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200/50 dark:border-slate-700/50 p-3 sm:p-4 shadow-sm hover:shadow-md transition-shadow">
          <div className="flex items-center gap-2.5 sm:gap-3">
            <div className="w-9 h-9 sm:w-10 sm:h-10 rounded-xl bg-orange-100 dark:bg-orange-900/30 flex items-center justify-center flex-shrink-0">
              <Clock className="w-4 h-4 sm:w-5 sm:h-5 text-orange-600 dark:text-orange-400" />
            </div>
            <div className="min-w-0">
              <p className="text-[10px] sm:text-xs text-gray-500 dark:text-slate-400">{t('settings.page.info.startupTime')}</p>
              <p className="text-sm sm:text-lg font-bold text-gray-900 dark:text-slate-100">{formatStartupTime(systemInfo?.startupTime)}</p>
            </div>
          </div>
        </div>
      </div>

      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm">
        <div className="px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-primary-50/50 to-transparent dark:from-primary-900/20">
          <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base flex items-center gap-2">
            <div className="w-6 h-6 sm:w-7 sm:h-7 rounded-lg bg-primary-100 dark:bg-primary-900/30 flex items-center justify-center">
              <Server className="w-3.5 h-3.5 sm:w-4 sm:h-4 text-primary-600 dark:text-primary-400" />
            </div>
            {t('settings.page.tab.info')}
          </h3>
        </div>
        <div className="p-4 sm:p-6">
          <dl className="grid grid-cols-1 md:grid-cols-2 gap-y-3">
            <InfoRow
              label={t('settings.page.info.backendVersion')}
              value={<span className="font-mono text-blue-600 dark:text-blue-400">.NET Core 10.0</span>}
            />
            <InfoRow
              label={t('settings.page.info.frontendVersion')}
              value={<span className="font-mono text-cyan-600 dark:text-cyan-400">React 18 + Vite 5</span>}
            />
            <InfoRow
              label={t('settings.page.info.database')}
              value={<span className="font-mono">SQLite</span>}
            />
            <InfoRow
              label={t('settings.page.info.authMethod')}
              value={<span className="font-mono">JWT Token</span>}
            />
            <InfoRow
              label={t('settings.page.info.passwordHashing')}
              value={<span className="font-mono">BCrypt</span>}
            />
            <InfoRow
              label={t('settings.page.info.apiDocs')}
              value={
                // H-07：移除 5009 硬编码端口；生产同域部署下 Scalar 在 /docs 路径，
                // 开发环境 vite 会代理到后端，相对路径兼容所有部署拓扑。
                <a href="/docs" target="_blank" rel="noopener noreferrer" className="text-primary-600 dark:text-primary-400 hover:text-primary-700 dark:hover:text-primary-300 hover:underline flex items-center gap-1">
                  <span className="font-mono text-sm">{t('settings.page.info.apiDocsPath')}</span>
                  <ExternalLink className="w-3 h-3" />
                </a>
              }
            />
          </dl>
        </div>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 sm:gap-5">
        <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm">
          <div className="px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-purple-50/50 to-transparent dark:from-purple-900/20">
            <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base flex items-center gap-2">
              <div className="w-6 h-6 sm:w-7 sm:h-7 rounded-lg bg-purple-100 dark:bg-purple-900/30 flex items-center justify-center">
                <Languages className="w-3.5 h-3.5 sm:w-4 sm:h-4 text-purple-600 dark:text-purple-400" />
              </div>
              {t('settings.page.info.localization')}
            </h3>
          </div>
          <div className="p-4 sm:p-6">
            {languages ? (
              <dl className="space-y-4">
                <div>
                  <dt className="text-sm text-gray-500 dark:text-slate-400 mb-2">{t('settings.page.info.currentLanguage')}</dt>
                  <dd>
                    <span className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-full bg-green-100 dark:bg-green-900/30 text-green-700 dark:text-green-400">
                      <span className="w-1.5 h-1.5 rounded-full bg-green-500 animate-pulse"></span>
                      {languages.current}
                    </span>
                  </dd>
                </div>
                <div>
                  <dt className="text-sm text-gray-500 dark:text-slate-400 mb-2">{t('settings.page.info.supportedLanguages')}</dt>
                  <dd className="flex flex-wrap gap-2">
                    {languages.supported.map((l) => (
                      <span
                        key={l}
                        className={`inline-flex px-3 py-1.5 text-xs font-medium rounded-full transition ${
                          l === languages.current
                            ? 'bg-primary-100 dark:bg-primary-900/30 text-primary-700 dark:text-primary-400 ring-1 ring-primary-200 dark:ring-primary-700/50'
                            : 'bg-gray-100 dark:bg-slate-700 text-gray-700 dark:text-slate-300'
                        }`}
                      >
                        {l}
                      </span>
                    ))}
                  </dd>
                </div>
              </dl>
            ) : (
              <div className="flex items-center gap-2 text-sm text-gray-400 dark:text-slate-500">
                <Loader2 className="w-4 h-4 animate-spin" />
                {t('common.shell.loading')}
              </div>
            )}
          </div>
        </div>

        <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm">
          <div className="px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-blue-50/50 to-transparent dark:from-blue-900/20">
            <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base flex items-center gap-2">
              <div className="w-6 h-6 sm:w-7 sm:h-7 rounded-lg bg-blue-100 dark:bg-blue-900/30 flex items-center justify-center">
                <Layers className="w-3.5 h-3.5 sm:w-4 sm:h-4 text-blue-600 dark:text-blue-400" />
              </div>
              {t('settings.page.info.techStack')}
            </h3>
          </div>
          <div className="p-4 sm:p-6">
            <div className="grid grid-cols-2 sm:grid-cols-3 gap-3">
              {techStack.map((tech) => (
                <div
                  key={tech.name}
                  className={`${tech.color} rounded-xl p-3 transition hover:scale-105`}
                >
                  <p className="text-sm font-semibold">{tech.name}</p>
                  <p className="text-xs opacity-80">{tech.version}</p>
                </div>
              ))}
            </div>
          </div>
        </div>
      </div>

      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 overflow-hidden shadow-sm">
        <div className="px-4 sm:px-6 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-indigo-50/50 to-transparent dark:from-indigo-900/20">
          <h3 className="font-semibold text-gray-900 dark:text-slate-100 text-sm sm:text-base flex items-center gap-2">
            <div className="w-6 h-6 sm:w-7 sm:h-7 rounded-lg bg-indigo-100 dark:bg-indigo-900/30 flex items-center justify-center">
              <Cpu className="w-3.5 h-3.5 sm:w-4 sm:h-4 text-indigo-600 dark:text-indigo-400" />
            </div>
            {t('settings.page.info.architecture')}
          </h3>
        </div>
        <div className="p-4 sm:p-6">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-3 sm:gap-4">
            {[
              { icon: Layers, title: t('settings.page.info.arch.ddd'), desc: 'Domain / Application / Infrastructure / Api', color: 'text-blue-600 dark:text-blue-400 bg-blue-100 dark:bg-blue-900/30' },
              { icon: Database, title: t('settings.page.info.arch.orm'), desc: 'Entity Framework Core 10.0', color: 'text-green-600 dark:text-green-400 bg-green-100 dark:bg-green-900/30' },
              { icon: Shield, title: t('settings.page.info.arch.auth'), desc: 'JWT Token + BCrypt', color: 'text-orange-600 dark:text-orange-400 bg-orange-100 dark:bg-orange-900/30' },
              { icon: Mail, title: t('settings.page.info.arch.mail'), desc: 'MailKit IMAP / SMTP', color: 'text-cyan-600 dark:text-cyan-400 bg-cyan-100 dark:bg-cyan-900/30' },
              { icon: Clock, title: t('settings.page.info.arch.scheduler'), desc: t('settings.page.info.arch.schedulerDescription'), color: 'text-purple-600 dark:text-purple-400 bg-purple-100 dark:bg-purple-900/30' },
              { icon: BookOpen, title: t('settings.page.info.apiDocs'), desc: 'Scalar UI', color: 'text-rose-600 dark:text-rose-400 bg-rose-100 dark:bg-rose-900/30' },
            ].map((item, i) => (
              <div key={i} className="flex items-start gap-2.5 sm:gap-3 p-3 sm:p-4 rounded-xl bg-gray-50/50 dark:bg-slate-700/30 hover:bg-gray-100 dark:hover:bg-slate-700/50 transition">
                <div className={`w-9 h-9 sm:w-10 sm:h-10 rounded-xl ${item.color} flex items-center justify-center flex-shrink-0`}>
                  <item.icon className="w-4 h-4 sm:w-5 sm:h-5" />
                </div>
                <div className="min-w-0">
                  <p className="text-xs sm:text-sm font-semibold text-gray-900 dark:text-slate-100">{item.title}</p>
                  <p className="text-[11px] sm:text-xs text-gray-500 dark:text-slate-400 mt-0.5 break-words">{item.desc}</p>
                </div>
              </div>
            ))}
          </div>
        </div>
      </div>
    </div>
  );
}

// ====== 主组件 ======
export default function Settings() {
  const { t } = useI18n();
  const [activeTab, setActiveTab] = useState<TabKey>('system');

  return (
    <div className="space-y-4 sm:space-y-5">
      {/* 渐变页头 */}
      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-slate-800 dark:to-slate-900 p-4 sm:p-6 border border-gray-200/50 dark:border-slate-700/50 mb-4 sm:mb-5">
        <div className="absolute -top-10 -right-10 w-32 h-32 sm:w-40 sm:h-40 bg-primary-200/30 dark:bg-primary-900/20 rounded-full blur-3xl pointer-events-none" />
        <div className="relative flex items-start gap-3 sm:gap-4">
          <div className="w-10 h-10 sm:w-12 sm:h-12 rounded-xl bg-gradient-to-br from-primary-500 to-indigo-600 text-white flex items-center justify-center shadow-lg shadow-primary-500/30 flex-shrink-0">
            <SettingsIcon className="w-5 h-5 sm:w-6 sm:h-6" />
          </div>
          <div className="min-w-0">
            <h1 className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100">{t('settings.page.title')}</h1>
            <p className="text-xs sm:text-sm text-gray-600 dark:text-slate-400 mt-0.5 sm:mt-1">{t('settings.page.subtitle')}</p>
          </div>
        </div>
      </div>

      {/* Tab 导航 */}
      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 p-1.5 inline-flex flex-wrap gap-1 shadow-sm">
        {tabs.map((tab) => (
          <button
            key={tab.key}
            onClick={() => setActiveTab(tab.key)}
            className={`inline-flex items-center gap-2 px-3 sm:px-4 py-2 sm:py-2.5 text-xs sm:text-sm font-medium rounded-xl transition whitespace-nowrap ${
              activeTab === tab.key
                ? 'bg-primary-600 text-white shadow-sm'
                : 'text-gray-600 dark:text-slate-400 hover:bg-gray-100 dark:hover:bg-slate-700 hover:text-gray-900 dark:hover:text-slate-100'
            }`}
          >
            {tab.icon}
            {t(tab.labelKey)}
          </button>
        ))}
      </div>

      {/* Tab 内容 */}
      <div>
        {activeTab === 'system' && <SystemOptionsTab />}
        {activeTab === 'mail' && <MailTestTab />}
        {activeTab === 'info' && <SystemInfoTab />}
      </div>
    </div>
  );
}
