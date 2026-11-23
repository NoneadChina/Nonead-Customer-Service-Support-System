import { useEffect, useState, useRef } from 'react';
import {
  Languages,
  Download,
  Upload,
  Save,
  Globe,
  Search,
  Key,
  CheckCircle2,
  XCircle,
  Loader2,
} from 'lucide-react';
import { normalizeLanguage, useI18n } from '../context/I18nContext';
import { LANGUAGE_OPTIONS, useLanguageSwitch } from '../hooks/useLanguageSwitch';
import { getUiTranslationDefaults, getUiTranslationKeys } from '../i18nKeys';
import client from '../api/client';

interface TranslationStats {
  culture: string;
  total: number;
  translated: number;
  untranslated: number;
  percentage: number;
}

/**
 * 后端 `/stats` 的单语言条目。
 *
 * `total / translated / untranslated / percentage` 只覆盖**后端 resx** 那 62/65 条词条；
 * 翻译管理页列出的却是"前端 UI 键（数千条）+ 后端 resx 键 + DB 覆盖行"。页面必须自己
 * 补上前端那部分，否则卡片会显示成"共 62 / 已译 0"（与列表严重不符）。
 */
interface LanguageStats {
  culture: string;
  percentage: number;
  translated: number;
  untranslated: number;
  total: number;
  /** DB 覆盖行数（非空值），后端 2026-10-10 起返回 */
  dbKeyCount?: number;
}

/**
 * 语言显示名一律取 `LANGUAGE_OPTIONS`（语言自称，不随界面语言变化）。
 * 后端 stats 可能返回本产品不支持的语言（如 `zh-Hant`），此时回退显示原始 culture 串——
 * 改动前这类语言一律显示成 `English`（错的）。
 */
const labelForCulture = (culture: string) =>
  LANGUAGE_OPTIONS.find((option) => option.value === culture)?.label ?? culture;

/**
 * 导出内容 = 该语言下**页面可编辑词条集的完整有效文案**：
 * 前端 UI 真源（`src/locales/*`，数千条）打底，后端返回的 resx 与 DB 覆盖层依次覆盖（DB 优先）。
 *
 * 只导后端那一份是不够的：页面列出的键里绝大多数是前端 UI 键，而后端导出永远不含它们
 * （前端点号键与后端 PascalCase 键的交集为 0，见 `i18nKeys.ts` 末尾说明）——
 * 拿这样的文件去翻译再导入，等于一条界面文案都没改。
 *
 * 抽成纯函数是为了能直接断言"导出确实覆盖了页面键集"，不必在测试里伪造浏览器下载。
 */
export function mergeExportEntries(
  culture: string,
  serverEntries: Record<string, string>
): Record<string, string> {
  return { ...getUiTranslationDefaults(culture), ...serverEntries };
}

export default function Localization() {
  const { reload, t } = useI18n();
  // 切换语言走统一入口：归一化在 hook 内完成（本页的 culture 可以是后端提供、
  // 本产品并不支持的语言），切换会写回资料——因此**只在语言确实要变时**才调用（见 applyEditedLanguage）
  const { language, switchLanguage } = useLanguageSwitch();
  const [culture, setCulture] = useState('en');
  const [translations, setTranslations] = useState<Record<string, string>>({});
  const [stats, setStats] = useState<TranslationStats[]>([]);
  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [search, setSearch] = useState('');
  const [message, setMessage] = useState('');
  /**
   * 提示是成功还是失败。**必须由状态位给出**：英文界面的提示里没有「失败」二字，
   * 靠 `message.includes('失败')` 嗅探会在英文下永远判成成功（配色与图标一起错）。
   */
  const [messageIsError, setMessageIsError] = useState(false);
  const [editValues, setEditValues] = useState<Record<string, string>>({});
  const messageTimeoutRef = useRef<NodeJS.Timeout | null>(null);

  // 清理 message timeout
  useEffect(() => {
    return () => {
      if (messageTimeoutRef.current) {
        clearTimeout(messageTimeoutRef.current);
      }
    };
  }, []);

  /**
   * 统一入口：设置提示 + 成功/失败状态位，并按需安排自动消失。
   * 不传 `durationMs` 的提示**不会**自动消失（与改动前的行为一致）。
   */
  const showMessage = (text: string, isError: boolean, durationMs?: number) => {
    setMessage(text);
    setMessageIsError(isError);
    if (messageTimeoutRef.current) {
      clearTimeout(messageTimeoutRef.current);
      messageTimeoutRef.current = null;
    }
    if (durationMs) {
      messageTimeoutRef.current = setTimeout(() => setMessage(''), durationMs);
    }
  };

  // 合并所有翻译 key：前端 UI key + 后端返回的翻译 key
  const allKeys = Array.from(new Set([
    ...getUiTranslationKeys(),
    ...Object.keys(translations),
  ])).sort();

  const fetchTranslations = async (c: string) => {
    setLoading(true);
    try {
      const res = await client.get<{ translations: Record<string, string> }>(`/api/localization/translations/${c}`);
      const data = res.data;
      const merged = { ...getUiTranslationDefaults(), ...(data.translations || {}) };
      setTranslations(merged);
      setEditValues(merged);
    } catch {
      console.error('获取翻译失败');
    } finally {
      setLoading(false);
    }
  };

  /**
   * 统计卡片与**页面词条集**对齐（P2）。
   *
   * 页面列出的是：前端 UI 键 ∪ 后端 resx 键 ∪ DB 覆盖行。后端 `/stats` 只知道后端那 62/65 条，
   * 所以这里补上前端键数；"已定制"取 `dbKeyCount`（DB 覆盖行数）——页面会把 DB 行一并列出，
   * 两者口径一致。前端点号键与后端 PascalCase 键交集为 0，直接相加不会重复计数。
   */
  const fetchStats = async () => {
    try {
      const res = await client.get<{ languages: LanguageStats[] }>('/api/localization/stats');
      const uiKeyCount = getUiTranslationKeys().length;
      setStats((res.data.languages || []).map((language) => {
        const total = uiKeyCount + language.total;
        // 钳制：DB 里可能存在既非前端键、也非后端 resx 键的行（手工导入的拼错键就是这种），
        // 它们会出现在列表里但不属于这个口径的键集；不钳制会算出 >100% 的完成度。
        const translated = Math.min(language.dbKeyCount ?? language.translated, total);
        const untranslated = Math.max(0, total - translated);
        return {
          culture: language.culture,
          total,
          translated,
          untranslated,
          percentage: total > 0 ? Math.round((translated / total) * 10000) / 100 : 0,
        };
      }));
    } catch {
      console.error('获取统计失败');
    }
  };

  useEffect(() => {
    fetchTranslations(culture);
    fetchStats();
  }, [culture]);

  /**
   * 保存/导入后把界面语言对齐到刚编辑的语言，返回"语言是否已对齐"供调用点决定提示语。
   *
   * 三条约束（Task 6/7 第二轮审查 R6）：
   * ① 语言已经一致时**不得**调用 `switchLanguage`——它会写回整份 `PUT /api/profile`，
   *    保存一个词条不该顺带改用户资料，失败还会连累"保存成功"的提示；
   * ② 切换失败必须上报（`switched === false`），不能像此前那样 `void` 掉还报成功；
   * ③ 切换成功时 provider 会因语言变化自行重拉覆盖层，**不再显式 `reload()`**；
   *    语言未变（或切换被回滚）时覆盖层仍是旧的，必须 `reload()` 才看得到刚存的词条。
   */
  const applyEditedLanguage = async (next: string): Promise<boolean> => {
    const target = normalizeLanguage(next);
    if (!target || target === language) {
      // 无需切换（含本产品不支持的语言：该语言的词条仍可编辑，只是界面不切）
      reload();
      return true;
    }
    const switched = await switchLanguage(next);
    if (!switched) reload();
    return switched;
  };

  const handleSave = async (key: string) => {
    setSaving(true);
    try {
      const value = editValues[key] || '';
      await client.put<void>(`/api/localization/translations/${culture}`, { [key]: value });
      setTranslations(prev => ({ ...prev, [key]: value }));
      fetchStats();
      const switched = await applyEditedLanguage(culture);
      // 新文案一律走真源（本页其余存量中文属后续批次范围）
      showMessage(
        switched ? t('common.saveSucceeded') : t('common.savedButLanguageSwitchFailed'),
        false,
        2000
      );
    } catch {
      showMessage(t('localization.message.saveFailed'), true);
    } finally {
      setSaving(false);
    }
  };

  /**
   * 导出：后端 `{resx ∪ DB}` 与前端 UI 真源合并后落盘（见 `mergeExportEntries`）。
   *
   * 改动前只把后端那份原样下载，于是"导出 → 交给翻译 → 导入"这条主流程对前端界面文案
   * 完全不生效；而空库时后端那份是 `{}`，用户拿到一个空文件，再导入才看到"导入内容为空"。
   */
  const handleExport = async () => {
    try {
      const res = await client.post(`/api/localization/translations/${culture}/export`, {}, { responseType: 'blob' });
      const serverEntries = JSON.parse(await (res.data as Blob).text()) as Record<string, string>;
      const entries = mergeExportEntries(culture, serverEntries);
      const count = Object.keys(entries).length;
      if (count === 0) {
        // 不再静默下载 `{}`：空文件导入必然失败，必须在这里说清楚
        showMessage(t('localization.message.exportEmpty'), true, 3000);
        return;
      }

      const blob = new Blob([JSON.stringify(entries, null, 2)], { type: 'application/json' });
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `translations_${culture}.json`;
      a.click();
      window.URL.revokeObjectURL(url);
      showMessage(t('localization.message.exportSucceeded', { count }), false, 3000);
    } catch {
      // 「导出失败」复用既有通用键 `data.common.exportFailed`（同文案全仓已有三处，不新造第四处）
      showMessage(t('data.common.exportFailed'), true);
    }
  };

  const handleImport = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;

    // 客户端校验：文件大小限制 5MB
    if (file.size > 5 * 1024 * 1024) {
      showMessage(t('localization.message.fileTooLarge'), true, 3000);
      e.target.value = '';
      return;
    }

    try {
      const text = await file.text();
      const data = JSON.parse(text);

      // 前端格式预校验（三层）。必须在这里拦住：后端收 `Dictionary<string, string>`，
      // 值一旦不是字符串，模型绑定失败返回 400，界面只能拿到英文 .NET 原文。
      if (typeof data !== 'object' || data === null || Array.isArray(data)) {
        showMessage(t('localization.message.importInvalidObject'), true, 3000);
        return;
      }
      const entries = Object.entries(data as Record<string, unknown>);
      if (entries.length === 0) {
        showMessage(t('localization.message.importEmpty'), true, 3000);
        return;
      }
      const invalidCount = entries.filter(
        ([key, value]) => key.length === 0 || typeof value !== 'string'
      ).length;
      if (invalidCount > 0) {
        // 有任何一条不合规就整体拒绝：不做部分写入，避免"导入成功"里混着被静默丢掉的行
        showMessage(t('localization.message.importInvalidEntries', { count: invalidCount }), true, 3000);
        return;
      }

      const res = await client.post<{ importedCount: number; message: string }>(`/api/localization/translations/${culture}/import`, data);
      const result = res.data;
      const importCount = result.importedCount || 0;
      // 刷新翻译管理页面列表
      fetchTranslations(culture);
      fetchStats();
      // 与"保存单个词条"同一套语义：语言确实要变时才切换，切换失败要上报
      const switched = await applyEditedLanguage(culture);
      showMessage(
        t('localization.message.importSucceeded', { count: importCount }) +
          (switched ? '' : t('common.languageSwitchFailedSuffix')),
        false,
        4000
      );
    } catch (err: unknown) {
      if (err instanceof SyntaxError) {
        showMessage(t('localization.message.importMalformedJson'), true, 3000);
      } else {
        // 后端/运行时的自由文本（模型绑定失败时是英文 .NET 原文）不直出给用户：
        // 界面统一给真源文案，原文留在控制台供排查
        console.error('[localization] 导入失败', err);
        showMessage(
          t('localization.message.importFailedPrefix') + t('localization.message.importRequestFailed'),
          true,
          3000
        );
      }
    } finally {
      e.target.value = '';
    }
  };

  const filteredKeys = allKeys.filter(k =>
    k.toLowerCase().includes(search.toLowerCase()) ||
    (translations[k] || '').toLowerCase().includes(search.toLowerCase())
  );

  return (
    <div className="space-y-4 sm:space-y-5">
      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-slate-800 dark:to-slate-900 p-4 sm:p-6 border border-gray-200/50 dark:border-slate-700/50">
        <div className="absolute -top-10 -right-10 w-40 h-40 bg-primary-200/30 dark:bg-primary-900/20 rounded-full blur-3xl pointer-events-none" />
        <div className="relative flex items-start justify-between gap-3 sm:gap-4 flex-wrap">
          <div className="flex items-start gap-3 sm:gap-4 min-w-0">
            <div className="w-10 h-10 sm:w-12 sm:h-12 rounded-xl bg-gradient-to-br from-primary-500 to-indigo-600 text-white flex items-center justify-center shadow-lg shadow-primary-500/30 flex-shrink-0">
              <Languages className="w-5 h-5 sm:w-6 sm:h-6" />
            </div>
            <div className="min-w-0">
              <h1 className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100">{t('nav.localization')}</h1>
              <p className="text-xs sm:text-sm text-gray-600 dark:text-slate-400 mt-1">{t('localization.page.subtitle')}</p>
            </div>
          </div>
          <div className="flex items-center gap-2 sm:gap-3 flex-wrap">
            <div className="relative">
              <Globe className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400 dark:text-slate-500 pointer-events-none" />
              <select
                value={culture}
                onChange={(e) => setCulture(e.target.value)}
                className="pl-9 pr-8 py-2 border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-800 dark:text-slate-300 rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition shadow-sm cursor-pointer appearance-none"
              >
                {LANGUAGE_OPTIONS.map((option) => (
                  <option key={option.value} value={option.value}>
                    {option.label}
                  </option>
                ))}
              </select>
            </div>
            <button
              onClick={handleExport}
              className="px-3 sm:px-4 py-2 text-sm bg-white dark:bg-slate-700 border border-gray-300 dark:border-slate-600 dark:text-slate-300 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-600 shadow-sm active:scale-[0.98] transition flex items-center gap-1.5"
              title={t('localization.page.exportTitle')}
            >
              <Download className="w-4 h-4" />
              <span className="hidden sm:inline">{t('data.common.export')}</span>
            </button>
            <label className="px-3 sm:px-4 py-2 text-sm bg-white dark:bg-slate-700 border border-gray-300 dark:border-slate-600 dark:text-slate-300 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-600 shadow-sm active:scale-[0.98] transition cursor-pointer flex items-center gap-1.5" title={t('localization.page.importTitle')}>
              <Upload className="w-4 h-4" />
              <span className="hidden sm:inline">{t('common.import')}</span>
              <input type="file" accept=".json" className="hidden" onChange={handleImport} />
            </label>
          </div>
        </div>
      </div>

      {message && (
        <div className={`px-4 py-2.5 rounded-xl text-xs sm:text-sm flex items-center gap-2 border transition ${
          messageIsError
            ? 'bg-red-50 text-red-700 border-red-200 dark:bg-red-900/20 dark:text-red-300 dark:border-red-800/50'
            : 'bg-green-50 text-green-700 border-green-200 dark:bg-green-900/20 dark:text-green-300 dark:border-green-800/50'
        }`}>
          {messageIsError
            ? <XCircle className="w-4 h-4 flex-shrink-0" />
            : <CheckCircle2 className="w-4 h-4 flex-shrink-0" />
          }
          <span className="break-words">{message}</span>
        </div>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-2 gap-3 sm:gap-4">
        {stats.map(s => (
          <div
            key={s.culture}
            className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200/50 dark:border-slate-700/50 p-4 sm:p-5 shadow-sm hover:shadow-md hover:-translate-y-0.5 transition"
          >
            <div className="flex items-center justify-between mb-3">
              <div className="flex items-center gap-2.5 min-w-0">
                <div className="w-7 h-7 sm:w-8 sm:h-8 rounded-lg bg-gradient-to-br from-primary-500 to-indigo-600 text-white flex items-center justify-center shadow-md flex-shrink-0">
                  <Globe className="w-3.5 h-3.5 sm:w-4 sm:h-4" />
                </div>
                <span className="text-sm font-medium text-gray-700 dark:text-slate-300 truncate">{labelForCulture(s.culture)}</span>
              </div>
              <span className="text-sm font-bold text-primary-600 dark:text-primary-400 flex-shrink-0">{s.percentage}%</span>
            </div>
            <div className="w-full bg-gray-100 dark:bg-slate-700 rounded-full h-2 overflow-hidden">
              <div
                className="bg-gradient-to-r from-primary-500 to-indigo-500 h-2 rounded-full transition-all"
                style={{ width: `${s.percentage}%` }}
              />
            </div>
            <div className="flex justify-between mt-2 text-[11px] sm:text-xs text-gray-500 dark:text-slate-400">
              <span>{t('localization.stats.translated', { count: s.translated })}</span>
              <span>{t('localization.stats.untranslated', { count: s.untranslated })}</span>
              <span>{t('localization.stats.total', { count: s.total })}</span>
            </div>
          </div>
        ))}
      </div>

      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200/50 dark:border-slate-700/50 shadow-sm">
        <div className="p-3 sm:p-4 border-b border-gray-200/50 dark:border-slate-700/50">
          <div className="relative max-w-md">
            <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400 dark:text-slate-500 pointer-events-none" />
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t('localization.page.searchPlaceholder')}
              className="w-full pl-9 pr-3 py-2 border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-900 dark:text-slate-300 rounded-xl text-sm focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition"
            />
          </div>
        </div>

        {loading ? (
          <div className="p-10 text-center text-gray-500 dark:text-slate-400 flex items-center justify-center gap-2">
            <Loader2 className="w-4 h-4 animate-spin" />
            {t('common.shell.loading')}
          </div>
        ) : filteredKeys.length === 0 ? (
          <div className="p-10 text-center text-gray-500 dark:text-slate-400">
            <Languages className="w-10 h-10 mx-auto mb-3 text-gray-300 dark:text-slate-600" />
            <p>{t('localization.page.empty')}</p>
          </div>
        ) : (
          <div className="divide-y divide-gray-100 dark:divide-slate-700/50 max-h-[600px] overflow-auto">
            {filteredKeys.map(key => (
              <div key={key} className="p-3 sm:p-4 hover:bg-gray-50 dark:hover:bg-slate-700/50 transition">
                {/* 翻译键 */}
                <div className="flex items-center gap-1.5 mb-2">
                  <Key className="w-3.5 h-3.5 text-gray-400 dark:text-slate-500 flex-shrink-0" />
                  <span className="text-xs sm:text-sm font-medium text-gray-900 dark:text-slate-100 truncate">{key}</span>
                </div>
                {/* 输入框 + 保存按钮 */}
                <div className="flex items-stretch gap-2">
                  <input
                    type="text"
                    value={editValues[key] || ''}
                    onChange={(e) => setEditValues(prev => ({ ...prev, [key]: e.target.value }))}
                    className="flex-1 min-w-0 px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-900 dark:text-slate-300 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition"
                    placeholder={t('localization.page.translationPlaceholder')}
                  />
                  <button
                    onClick={() => handleSave(key)}
                    disabled={saving}
                    className="px-3 py-2 text-sm bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 text-white rounded-xl shadow-sm active:scale-[0.98] transition flex items-center gap-1.5 flex-shrink-0"
                  >
                    <Save className="w-3.5 h-3.5" />
                    <span className="hidden sm:inline">{t('common.save')}</span>
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
