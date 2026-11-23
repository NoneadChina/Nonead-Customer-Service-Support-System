import { useState, useEffect, useCallback, useRef } from 'react';
import { importExportApi } from '../api';
import { getApiErrorMessage } from '../utils/error';
import type { EntityTypeResponse, ImportPreviewResponse, ImportResponse } from '../api/types';
import { useI18n } from '../context/I18nContext';
import type { TranslationKey } from '../locales/zh-Hans';
import {
  Database,
  Download,
  Upload,
  FileJson,
  FileUp,
  Clipboard,
  CheckCircle2,
  XCircle,
  AlertCircle,
  Check,
  RefreshCw,
  Users,
  MessageSquare,
  Tag,
  Mail,
  Settings,
  FileText,
  Layers,
  ChevronRight,
} from 'lucide-react';

/**
 * 实体类型码 → 名称/描述的文案 key。
 *
 * 后端 `ImportExportService.GetAvailableEntitiesAsync`
 * （`NoneadCSS.Application/Services/ImportExportService.cs:36-41`）下发的 `name` / `description`
 * 是**中文标签**，英文界面下会直出中文，因此改由前端按稳定 `type` 码查字典。
 *
 * 字段名带 `Keys` 后缀并持有 `TranslationKey`（不持有文案）：使用点漏包 `t()` 会被 `tsc`
 * 以 TS2339 抓出。类型写成 `Record<string, TranslationKey | undefined>` 而**不是**
 * `Record<string, TranslationKey>`——后者在 `strict` 下会让"查不到"分支类型上不可达，
 * 而运行期恰好在后端新增实体类型时可达（类型谎言）。
 */
const ENTITY_NAME_KEYS: Record<string, TranslationKey | undefined> = {
  customers: 'data.importExport.entity.customers.name',
  tags: 'data.importExport.entity.tags.name',
  canned_responses: 'data.importExport.entity.canned_responses.name',
  mail_templates: 'data.importExport.entity.mail_templates.name',
  email_signatures: 'data.importExport.entity.email_signatures.name',
  // 后端 `ImportExportService.cs:41-42` 的这两个实体 SupportsExport=true，
  // 缺映射时页面会回落显示后端的中文 Name，英文界面直出中文——必须一并登记。
  conversations: 'data.importExport.entity.conversations.name',
  knowledge_articles: 'data.importExport.entity.knowledge_articles.name',
  users: 'data.importExport.entity.users.name',
};

const ENTITY_DESCRIPTION_KEYS: Record<string, TranslationKey | undefined> = {
  customers: 'data.importExport.entity.customers.description',
  tags: 'data.importExport.entity.tags.description',
  canned_responses: 'data.importExport.entity.canned_responses.description',
  mail_templates: 'data.importExport.entity.mail_templates.description',
  email_signatures: 'data.importExport.entity.email_signatures.description',
  conversations: 'data.importExport.entity.conversations.description',
  knowledge_articles: 'data.importExport.entity.knowledge_articles.description',
  users: 'data.importExport.entity.users.description',
};

export default function ImportExport() {
  const { t } = useI18n();
  const [entities, setEntities] = useState<EntityTypeResponse[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  // 导出相关状态
  const [exportSelected, setExportSelected] = useState<Set<string>>(new Set());
  const [exporting, setExporting] = useState(false);

  // 导入相关状态
  const [importTab, setImportTab] = useState<'file' | 'text'>('file');
  const [importFile, setImportFile] = useState<File | null>(null);
  const [importText, setImportText] = useState('');
  const [importPreview, setImportPreview] = useState<ImportPreviewResponse | null>(null);
  const [previewing, setPreviewing] = useState(false);
  const [importing, setImporting] = useState(false);
  const [importResult, setImportResult] = useState<ImportResponse | null>(null);

  const fetchEntitiesCancelled = useRef(false);

  /**
   * 取数回调。改用 `useCallback` 并把 `t` 放进依赖：错误文案要按当前语言渲染，
   * 而 `fetchEntities` 一旦闭包引用 `t` 就成了"响应式"值，
   * `react-hooks/exhaustive-deps` 会（正确地）要求它进 effect 依赖数组——
   * 直接把每帧新建的普通函数塞进去会造成取数活锁，故按本仓库既有写法
   * （如 `Chat.tsx` 的 fetchSessions）固化引用。代价只是切语言时多拉一次实体列表。
   */
  const fetchEntities = useCallback(() => {
    fetchEntitiesCancelled.current = false;
    setLoading(true);
    importExportApi.getEntities()
      .then((v) => { if (!fetchEntitiesCancelled.current) setEntities(v); })
      .catch((err: unknown) => { if (!fetchEntitiesCancelled.current) setError(getApiErrorMessage(err, t('data.importExport.loadFailed'))); })
      .finally(() => { if (!fetchEntitiesCancelled.current) setLoading(false); });
  }, [t]);

  useEffect(() => {
    fetchEntities();
  }, [fetchEntities]);

  useEffect(() => {
    return () => { fetchEntitiesCancelled.current = true; };
  }, []);

  const toggleExportSelect = (type: string) => {
    setExportSelected((prev) => {
      const next = new Set(prev);
      if (next.has(type)) {
        next.delete(type);
      } else {
        next.add(type);
      }
      return next;
    });
  };

  const selectAllExport = () => {
    if (exportSelected.size === entities.filter(e => e.supportsExport).length) {
      setExportSelected(new Set());
    } else {
      setExportSelected(new Set(entities.filter(e => e.supportsExport).map(e => e.type)));
    }
  };

  const handleExport = async () => {
    if (exportSelected.size === 0) {
      setError(t('data.importExport.selectTypeRequired'));
      return;
    }
    setExporting(true);
    try {
      const blob = await importExportApi.exportData(Array.from(exportSelected));
      const url = window.URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `noneadcss_export_${new Date().toISOString().slice(0, 19).replace(/:/g, '-')}.json`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      window.URL.revokeObjectURL(url);
      setError('');
      setExportSelected(new Set());
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('data.common.exportFailed'));
    } finally {
      setExporting(false);
    }
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      // 客户端校验：文件大小限制 5MB
      if (file.size > 5 * 1024 * 1024) {
        setError(t('data.importExport.fileTooLarge'));
        e.target.value = '';
        return;
      }
      setImportFile(file);
      setImportText('');
    }
  };

  const handlePreview = async () => {
    let data = '';
    if (importTab === 'file' && importFile) {
      data = await importFile.text();
    } else {
      data = importText;
    }
    if (!data.trim()) {
      setError(t('data.importExport.dataRequired'));
      return;
    }
    setPreviewing(true);
    try {
      const preview = await importExportApi.previewImport(data);
      setImportPreview(preview);
      setImportResult(null);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('data.importExport.previewFailed'));
      setImportPreview(null);
    } finally {
      setPreviewing(false);
    }
  };

  const handleImport = async () => {
    let data = '';
    if (importTab === 'file' && importFile) {
      data = await importFile.text();
    } else {
      data = importText;
    }
    if (!data.trim()) {
      setError(t('data.importExport.dataRequired'));
      return;
    }
    setImporting(true);
    try {
      const result = await importExportApi.importData(data);
      setImportResult(result);
      setImportPreview(null);
      setError('');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : t('data.importExport.importFailed'));
      setImportResult(null);
    } finally {
      setImporting(false);
    }
  };

  const resetImport = () => {
    setImportFile(null);
    setImportText('');
    setImportPreview(null);
    setImportResult(null);
    setError('');
  };

  if (loading) {
    return (
      <div className="flex items-center justify-center min-h-[400px]">
        <div className="animate-spin h-8 w-8 border-4 border-primary-500 border-t-transparent rounded-full" />
      </div>
    );
  }

  /**
   * 实体类型的标签与描述改由前端字典提供。
   * 查不到 key 时回落渲染后端下发的原文（`entity.name` / `entity.description`）——
   * 这是"后端新增数据类型时的安全回落"，保留改造前的兜底行为：宁可显示后端原文，
   * 也不要把 key 本身漏到界面上。
   */
  const entityName = (entity: EntityTypeResponse) => {
    const key = ENTITY_NAME_KEYS[entity.type];
    return key ? t(key) : entity.name;
  };

  const entityDescription = (entity: EntityTypeResponse) => {
    const key = ENTITY_DESCRIPTION_KEYS[entity.type];
    return key ? t(key) : entity.description;
  };

  // 根据实体类型获取图标
  const getEntityIcon = (type: string) => {
    switch (type.toLowerCase()) {
      case 'customers': return <Users className="w-5 h-5" />;
      case 'conversations': return <MessageSquare className="w-5 h-5" />;
      case 'tags': return <Tag className="w-5 h-5" />;
      case 'mailboxes': return <Mail className="w-5 h-5" />;
      case 'users': return <Users className="w-5 h-5" />;
      case 'settings': return <Settings className="w-5 h-5" />;
      default: return <Layers className="w-5 h-5" />;
    }
  };

  return (
    <div className="space-y-4 sm:space-y-5">
      {/* 页头 */}
      <div className="relative overflow-hidden bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-slate-800 dark:to-slate-900 rounded-2xl border border-primary-100 dark:border-slate-700/50 p-4 sm:p-6">
        <div className="absolute top-0 right-0 w-64 h-64 bg-primary-200/30 dark:bg-primary-500/10 rounded-full -translate-y-1/2 translate-x-1/2 blur-3xl" />
        <div className="absolute bottom-0 left-0 w-48 h-48 bg-indigo-200/30 dark:bg-indigo-500/10 rounded-full translate-y-1/2 -translate-x-1/2 blur-3xl" />
        <div className="relative flex items-start justify-between gap-3 sm:gap-4 flex-wrap">
          <div className="flex items-start gap-3 sm:gap-4 min-w-0 flex-1">
            <div className="p-2.5 sm:p-3 bg-white dark:bg-slate-800 rounded-xl shadow-md shrink-0">
              <Database className="w-6 h-6 sm:w-7 sm:h-7 text-primary-600 dark:text-primary-400" />
            </div>
            <div className="min-w-0">
              <h1 className="text-lg sm:text-xl font-bold text-gray-900 dark:text-slate-100">{t('data.importExport.title')}</h1>
              <p className="text-xs sm:text-sm text-gray-600 dark:text-slate-400 mt-1 max-w-xl">
                {t('data.importExport.description')}
              </p>
            </div>
          </div>
        </div>
      </div>

      {error && (
        <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800/50 text-red-700 dark:text-red-400 px-4 sm:px-5 py-3 sm:py-4 rounded-xl text-sm flex items-start sm:items-center gap-2.5 sm:gap-3">
          <AlertCircle className="w-5 h-5 flex-shrink-0 mt-0.5 sm:mt-0" />
          <span className="min-w-0 break-words">{error}</span>
        </div>
      )}

      {/* 导出部分 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 shadow-sm overflow-hidden">
        <div className="px-4 sm:px-5 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-blue-50/50 to-transparent dark:from-blue-900/10">
          <div className="flex items-center gap-3">
            <div className="p-2 bg-blue-100 dark:bg-blue-900/30 rounded-lg shrink-0">
              <Download className="w-5 h-5 text-blue-600 dark:text-blue-400" />
            </div>
            <div className="min-w-0">
              <h2 className="text-base sm:text-lg font-semibold text-gray-900 dark:text-slate-100">{t('data.importExport.exportSectionTitle')}</h2>
              <p className="text-xs sm:text-sm text-gray-500 dark:text-slate-400 truncate">{t('data.importExport.exportSectionDescription')}</p>
            </div>
          </div>
        </div>
        <div className="p-4 sm:p-5">
          <div className="flex items-center justify-between gap-2 mb-4">
            <label className="flex items-center gap-2.5 sm:gap-3 cursor-pointer group">
              <div className="relative">
                <input
                  type="checkbox"
                  checked={exportSelected.size === entities.filter(e => e.supportsExport).length}
                  onChange={selectAllExport}
                  className="sr-only peer"
                />
                <div className="w-5 h-5 border-2 border-gray-300 dark:border-slate-600 rounded-md peer-checked:bg-primary-600 peer-checked:border-primary-600 transition-all">
                  <Check className={`w-4 h-4 text-white mx-auto mt-px transition-opacity ${exportSelected.size === entities.filter(e => e.supportsExport).length ? 'opacity-100' : 'opacity-0'}`} />
                </div>
              </div>
              <span className="text-sm font-medium text-gray-700 dark:text-slate-300 group-hover:text-gray-900 dark:group-hover:text-slate-100 transition-colors">{t('data.importExport.selectAll')}</span>
            </label>
            <div className="text-xs sm:text-sm text-gray-500 dark:text-slate-400 shrink-0">
              {t('data.importExport.selectedPrefix')}<span className="font-semibold text-primary-600 dark:text-primary-400">{exportSelected.size}</span>{t('data.importExport.selectedSeparator')}{entities.filter(e => e.supportsExport).length}{t('data.importExport.selectedSuffix')}
            </div>
          </div>
          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
            {entities.filter(e => e.supportsExport).map((entity) => (
              <div
                key={entity.type}
                className={`flex items-center gap-3 p-3 sm:p-4 rounded-xl border-2 transition-all cursor-pointer group ${
                  exportSelected.has(entity.type)
                    ? 'border-primary-500 bg-primary-50/50 dark:bg-primary-900/20 dark:border-primary-500'
                    : 'border-gray-200 dark:border-slate-700 hover:border-primary-300 dark:hover:border-primary-700 hover:bg-gray-50/50 dark:hover:bg-slate-700/30'
                }`}
                onClick={() => toggleExportSelect(entity.type)}
              >
                <div className={`w-9 h-9 sm:w-10 sm:h-10 rounded-xl flex items-center justify-center flex-shrink-0 transition-all ${
                  exportSelected.has(entity.type)
                    ? 'bg-primary-100 dark:bg-primary-900/30 text-primary-600 dark:text-primary-400'
                    : 'bg-gray-100 dark:bg-slate-700 text-gray-500 dark:text-slate-400 group-hover:bg-primary-50 dark:group-hover:bg-primary-900/20 group-hover:text-primary-500'
                }`}>
                  {getEntityIcon(entity.type)}
                </div>
                <div className="flex-1 min-w-0">
                  <div className="font-semibold text-sm sm:text-base text-gray-900 dark:text-slate-100 truncate">{entityName(entity)}</div>
                  <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 truncate">{entityDescription(entity)}</div>
                </div>
                <div className={`w-5 h-5 rounded-full border-2 flex items-center justify-center flex-shrink-0 transition-all ${
                  exportSelected.has(entity.type)
                    ? 'border-primary-500 bg-primary-500'
                    : 'border-gray-300 dark:border-slate-600 group-hover:border-primary-400'
                }`}>
                  {exportSelected.has(entity.type) && <Check className="w-3 h-3 text-white" />}
                </div>
              </div>
            ))}
          </div>
          <div className="mt-5 flex justify-end">
            <button
              onClick={handleExport}
              disabled={exportSelected.size === 0 || exporting}
              className="inline-flex items-center justify-center gap-2 w-full sm:w-auto px-5 py-2.5 text-sm font-medium bg-primary-600 hover:bg-primary-700 disabled:bg-gray-300 dark:disabled:bg-slate-600 disabled:cursor-not-allowed text-white rounded-xl shadow-sm hover:shadow transition-all active:scale-[0.98]"
            >
              {exporting ? (
                <RefreshCw className="w-4 h-4 animate-spin" />
              ) : (
                <Download className="w-4 h-4" />
              )}
              {exporting ? t('data.common.exporting') : t('data.importExport.exportAction')}
            </button>
          </div>
        </div>
      </div>

      {/* 导入部分 */}
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700/50 shadow-sm overflow-hidden">
        <div className="px-4 sm:px-5 py-3 sm:py-4 border-b border-gray-100 dark:border-slate-700/50 bg-gradient-to-r from-amber-50/50 to-transparent dark:from-amber-900/10">
          <div className="flex items-center gap-3">
            <div className="p-2 bg-amber-100 dark:bg-amber-900/30 rounded-lg shrink-0">
              <Upload className="w-5 h-5 text-amber-600 dark:text-amber-400" />
            </div>
            <div className="min-w-0">
              <h2 className="text-base sm:text-lg font-semibold text-gray-900 dark:text-slate-100">{t('data.importExport.importSectionTitle')}</h2>
              <p className="text-xs sm:text-sm text-gray-500 dark:text-slate-400 truncate">{t('data.importExport.importSectionDescription')}</p>
            </div>
          </div>
        </div>
        <div className="p-4 sm:p-5">
          <div className="flex gap-1.5 p-1 bg-gray-100 dark:bg-slate-700 rounded-xl mb-4">
            <button
              onClick={() => setImportTab('file')}
              className={`inline-flex items-center justify-center gap-1.5 sm:gap-2 flex-1 sm:flex-none px-3 sm:px-4 py-2 text-sm font-medium rounded-lg transition-all ${
                importTab === 'file'
                  ? 'bg-white dark:bg-slate-800 text-primary-700 dark:text-primary-400 shadow-sm'
                  : 'text-gray-600 dark:text-slate-400 hover:text-gray-900 dark:hover:text-slate-200'
              }`}
            >
              <FileUp className="w-4 h-4" />
              <span>{t('data.importExport.tabFile')}</span>
            </button>
            <button
              onClick={() => setImportTab('text')}
              className={`inline-flex items-center justify-center gap-1.5 sm:gap-2 flex-1 sm:flex-none px-3 sm:px-4 py-2 text-sm font-medium rounded-lg transition-all ${
                importTab === 'text'
                  ? 'bg-white dark:bg-slate-800 text-primary-700 dark:text-primary-400 shadow-sm'
                  : 'text-gray-600 dark:text-slate-400 hover:text-gray-900 dark:hover:text-slate-200'
              }`}
            >
              <Clipboard className="w-4 h-4" />
              <span>{t('data.importExport.tabText')}</span>
            </button>
          </div>

          {importTab === 'file' ? (
            <div className="border-2 border-dashed border-gray-300 dark:border-slate-600 rounded-xl p-5 sm:p-8 text-center hover:border-primary-400 dark:hover:border-primary-500 transition-all group">
              {importFile ? (
                <div className="space-y-3">
                  <div className="inline-flex items-center gap-2 px-3 sm:px-4 py-2 bg-green-50 dark:bg-green-900/30 text-green-700 dark:text-green-400 rounded-xl max-w-full">
                    <CheckCircle2 className="w-5 h-5 flex-shrink-0" />
                    <span className="font-medium text-sm truncate">{importFile.name}</span>
                  </div>
                  <button
                    onClick={() => setImportFile(null)}
                    className="text-sm text-gray-600 dark:text-slate-400 hover:text-primary-600 dark:hover:text-primary-400 transition-colors"
                  >
                    {t('data.importExport.changeFile')}
                  </button>
                </div>
              ) : (
                <div className="space-y-4">
                  <div className="w-14 h-14 sm:w-16 sm:h-16 mx-auto bg-gray-100 dark:bg-slate-700 rounded-2xl flex items-center justify-center group-hover:bg-primary-50 dark:group-hover:bg-primary-900/20 transition-colors">
                    <FileJson className="w-7 h-7 sm:w-8 sm:h-8 text-gray-400 dark:text-slate-500 group-hover:text-primary-500 transition-colors" />
                  </div>
                  <p className="text-sm text-gray-600 dark:text-slate-300">{t('data.importExport.dropHint')}</p>
                  <p className="text-xs text-gray-400 dark:text-slate-500">{t('data.importExport.formatHint')}</p>
                  <input
                    type="file"
                    accept=".json"
                    onChange={handleFileChange}
                    className="hidden"
                    id="import-file"
                  />
                  <button
                    onClick={() => document.getElementById('import-file')?.click()}
                    className="px-4 sm:px-5 py-2.5 bg-gray-100 dark:bg-slate-700 text-gray-700 dark:text-slate-200 rounded-xl hover:bg-gray-200 dark:hover:bg-slate-600 transition-colors font-medium text-sm inline-flex items-center gap-2"
                  >
                    <FileUp className="w-4 h-4" />
                    {t('data.importExport.chooseFile')}
                  </button>
                </div>
              )}
            </div>
          ) : (
            /*
             * 改造前这里是 JSX 字符串字面量属性，值里的 `&#10;` 由 JSX 编译器解码成换行符。
             * 文本改由 `t()` 提供后属性是**表达式容器**，其中的 HTML 实体不再被解码——
             * 若把 `&#10;` 原样搬进来，文本框会显示出 `&#10;` 这五个字符本身。
             * 故换行改用等价的 `\n`：改造前的每个 `&#10;` 正好是一个 U+000A，
             * 渲染结果与改造前逐字节相同（`粘贴 JSON 数据...` + LF LF + `示例格式：` +
             * LF + JSON 骨架）。JSON 骨架无中文、不属文案，两段之间的换行也随之留在 JSX 里。
             */
            <textarea
              value={importText}
              onChange={(e) => setImportText(e.target.value)}
              placeholder={`${t('data.importExport.pasteTextHint')}\n\n${t('data.importExport.pasteExampleHint')}\n{\n  "customers": [...],\n  "tags": [...]\n}`}
              className="w-full h-40 sm:h-48 px-3 sm:px-4 py-3 border border-gray-200 dark:border-slate-600 bg-white dark:bg-slate-900 text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 resize-none font-mono text-xs sm:text-sm transition-all"
            />
          )}

          <div className="mt-5 flex flex-col sm:flex-row gap-2.5 sm:gap-3">
            <button
              onClick={resetImport}
              className="px-5 py-2.5 text-sm font-medium bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-300 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-700 transition-all order-2 sm:order-1"
            >
              {t('common.reset')}
            </button>
            {importPreview ? (
              <button
                onClick={handleImport}
                disabled={importing}
                className="flex-1 sm:flex-none px-5 py-2.5 text-sm font-medium bg-primary-600 hover:bg-primary-700 disabled:bg-gray-300 dark:disabled:bg-slate-600 text-white rounded-xl shadow-sm hover:shadow transition-all active:scale-[0.98] order-1 sm:order-2"
              >
                {importing ? t('data.importExport.importing') : t('data.importExport.confirmImport')}
              </button>
            ) : (
              <button
                onClick={handlePreview}
                disabled={previewing}
                className="flex-1 sm:flex-none px-5 py-2.5 text-sm font-medium bg-primary-600 hover:bg-primary-700 disabled:bg-gray-300 dark:disabled:bg-slate-600 text-white rounded-xl shadow-sm hover:shadow transition-all active:scale-[0.98] inline-flex items-center justify-center gap-2 order-1 sm:order-2"
              >
                {previewing ? t('data.importExport.previewing') : t('data.importExport.previewAction')}
                {!previewing && <ChevronRight className="w-4 h-4" />}
              </button>
            )}
          </div>

          {/* 预览结果 */}
          {importPreview && (
            <div className="mt-5 p-4 sm:p-5 bg-gray-50 dark:bg-slate-900/50 rounded-xl border border-gray-200 dark:border-slate-700">
              <div className="flex items-center gap-2 mb-4">
                <FileText className="w-5 h-5 text-gray-500 dark:text-slate-400" />
                <h3 className="font-semibold text-gray-900 dark:text-slate-100">{t('data.importExport.previewTitle')}</h3>
              </div>
              {!importPreview.valid ? (
                <div className="space-y-2">
                  {importPreview.errors.map((err, idx) => (
                    <div key={idx} className="flex items-start gap-2 text-sm text-red-600 dark:text-red-400">
                      <XCircle className="w-4 h-4 flex-shrink-0 mt-0.5" />
                      <span className="break-words">{err}</span>
                    </div>
                  ))}
                </div>
              ) : (
                <div className="space-y-2">
                  {importPreview.entities.map((entity) => (
                    <div key={entity.type} className="flex items-center justify-between gap-2 p-3 bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700">
                      <div className="flex items-center gap-2.5 sm:gap-3 min-w-0">
                        <div className="p-2 bg-green-50 dark:bg-green-900/30 rounded-lg shrink-0">
                          <CheckCircle2 className="w-4 h-4 text-green-600 dark:text-green-400" />
                        </div>
                        <div className="min-w-0">
                          <div className="font-medium text-sm sm:text-base text-gray-900 dark:text-slate-100 truncate">{entity.type}</div>
                          <div className="text-xs text-gray-500 dark:text-slate-400">
                            {t('data.importExport.previewCountPrefix')}{entity.count}{t('data.importExport.previewCountSuffix')}
                          </div>
                        </div>
                      </div>
                      <span className="px-2.5 sm:px-3 py-1 text-xs sm:text-sm font-medium bg-green-100 dark:bg-green-900/30 text-green-700 dark:text-green-400 rounded-full shrink-0">
                        {entity.count}{t('data.importExport.previewCountBadgeSuffix')}
                      </span>
                    </div>
                  ))}
                </div>
              )}
            </div>
          )}

          {/* 导入结果 */}
          {importResult && (
            <div className={`mt-5 p-4 sm:p-5 rounded-xl ${
              importResult.success ? 'bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800/50' : 'bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800/50'
            }`}>
              <div className="flex items-center gap-2 mb-4">
                {importResult.success ? (
                  <CheckCircle2 className="w-5 h-5 text-green-600 dark:text-green-400" />
                ) : (
                  <AlertCircle className="w-5 h-5 text-red-600 dark:text-red-400" />
                )}
                <h3 className="font-semibold text-gray-900 dark:text-slate-100">{t('data.importExport.resultTitle')}</h3>
              </div>
              <div className="grid grid-cols-3 gap-2 sm:gap-4">
                <div className="bg-white/50 dark:bg-slate-800/50 rounded-xl p-2.5 sm:p-4 text-center">
                  <div className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100">{importResult.totalCount}</div>
                  <div className="text-[11px] sm:text-xs text-gray-500 dark:text-slate-400 mt-1">{t('data.importExport.resultTotal')}</div>
                </div>
                <div className="bg-white/50 dark:bg-slate-800/50 rounded-xl p-2.5 sm:p-4 text-center">
                  <div className="text-xl sm:text-2xl font-bold text-green-600 dark:text-green-400">{importResult.importedCount}</div>
                  <div className="text-[11px] sm:text-xs text-gray-500 dark:text-slate-400 mt-1">{t('data.importExport.resultSucceeded')}</div>
                </div>
                <div className="bg-white/50 dark:bg-slate-800/50 rounded-xl p-2.5 sm:p-4 text-center">
                  <div className="text-xl sm:text-2xl font-bold text-red-600 dark:text-red-400">{importResult.failedCount}</div>
                  <div className="text-[11px] sm:text-xs text-gray-500 dark:text-slate-400 mt-1">{t('data.importExport.resultFailed')}</div>
                </div>
              </div>
              {importResult.errors.length > 0 && (
                <div className="mt-4 space-y-2">
                  <div className="font-medium text-sm text-red-700 dark:text-red-400 flex items-center gap-2">
                    <XCircle className="w-4 h-4" />
                    {t('data.importExport.errorDetails')}
                  </div>
                  <div className="bg-white/50 dark:bg-slate-800/50 rounded-lg p-3 max-h-32 overflow-y-auto">
                    {importResult.errors.slice(0, 5).map((err, idx) => (
                      <div key={idx} className="text-xs sm:text-sm text-red-600 dark:text-red-400 break-words">{err}</div>
                    ))}
                    {importResult.errors.length > 5 && (
                      <div className="text-xs sm:text-sm text-red-500 dark:text-red-400 mt-1">{t('data.importExport.moreErrorsPrefix')}{importResult.errors.length - 5}{t('data.importExport.moreErrorsSuffix')}</div>
                    )}
                  </div>
                </div>
              )}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
