import { useState, useEffect, useCallback, useRef, useMemo } from 'react';
import {
  Wrench,
  Trash2,
  Database,
  LogOut,
  FileText,
  RefreshCw,
  AlertTriangle,
  CheckCircle2,
  XCircle,
  Loader2,
  Radio,
  Pause,
  ArrowDown,
  Search,
  X,
  Terminal,
  Activity,
  Clock,
  Filter,
  Download,
  Eraser,
  TrendingUp,
  Zap,
} from 'lucide-react';
import { systemToolsApi } from '../api';
import { useI18n } from '../context/I18nContext';
import { tBound, toIntlLocale } from '../i18n/locale';
import type { TranslationKey } from '../locales/zh-Hans';

// ====== 日志级别解析与配色 ======
type LogLevel = 'INF' | 'WRN' | 'ERR' | 'DBG' | 'FTL' | 'UNKNOWN';

interface LogLine {
  raw: string;
  level: LogLevel;
  timestamp: string;
  content: string;
}

const LOG_LEVEL_PATTERNS: Array<{ regex: RegExp; level: LogLevel }> = [
  { regex: /\[INF\]|\[INF\]/i, level: 'INF' },
  { regex: /\[WRN\]|\[WRN\]/i, level: 'WRN' },
  { regex: /\[ERR\]|\[ERR\]/i, level: 'ERR' },
  { regex: /\[DBG\]|\[DBG\]/i, level: 'DBG' },
  { regex: /\[FTL\]|\[FTL\]/i, level: 'FTL' },
];

// `label` 是 Serilog 的级别名（INFO/WARN/ERROR/DEBUG/FATAL/LOG），两种语言下都显示同一串技术标识，
// 故不进字典（也是筛选胶囊上除「全部」以外唯一的可见文案）。
const LEVEL_STYLES: Record<LogLevel, { text: string; bg: string; dot: string; label: string }> = {
  INF: { text: 'text-emerald-400', bg: 'bg-emerald-500/10', dot: 'bg-emerald-400', label: 'INFO' },
  WRN: { text: 'text-amber-400', bg: 'bg-amber-500/10', dot: 'bg-amber-400', label: 'WARN' },
  ERR: { text: 'text-rose-400', bg: 'bg-rose-500/10', dot: 'bg-rose-400', label: 'ERROR' },
  DBG: { text: 'text-sky-400', bg: 'bg-sky-500/10', dot: 'bg-sky-400', label: 'DEBUG' },
  FTL: { text: 'text-fuchsia-400', bg: 'bg-fuchsia-500/10', dot: 'bg-fuchsia-400', label: 'FATAL' },
  UNKNOWN: { text: 'text-slate-400', bg: 'bg-slate-500/10', dot: 'bg-slate-400', label: 'LOG' },
};

const parseLogLine = (raw: string): LogLine => {
  // 匹配 Serilog 时间戳格式：2026-08-12 11:40:40.778 +08:00 [INF] 内容
  const match = raw.match(/^(\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:\s+[+-]\d{2}:\d{2})?)\s*\[(\w+)\]\s*(.*)$/);
  if (match) {
    const levelStr = match[2].toUpperCase();
    const level = LOG_LEVEL_PATTERNS.some(p => p.regex.test(`[${levelStr}]`))
      ? (levelStr as LogLevel)
      : 'UNKNOWN';
    return { raw, level, timestamp: match[1], content: match[3] };
  }
  // 匹配 Console 格式：[11:40:40 INF] 内容
  const consoleMatch = raw.match(/^\[(\d{2}:\d{2}:\d{2})\s+(\w+)\]\s*(.*)$/);
  if (consoleMatch) {
    const levelStr = consoleMatch[2].toUpperCase();
    const level = LOG_LEVEL_PATTERNS.some(p => p.regex.test(`[${levelStr}]`))
      ? (levelStr as LogLevel)
      : 'UNKNOWN';
    return { raw, level, timestamp: consoleMatch[1], content: consoleMatch[3] };
  }
  return { raw, level: 'UNKNOWN', timestamp: '', content: raw };
};

interface ToolAction {
  id: string;
  nameKey: TranslationKey;
  descriptionKey: TranslationKey;
  danger?: boolean;
}

const tools: ToolAction[] = [
  {
    id: 'clear-cache',
    nameKey: 'integration.systemTools.tool.clearCache.name',
    descriptionKey: 'integration.systemTools.tool.clearCache.description'
  },
  {
    id: 'optimize-db',
    nameKey: 'integration.systemTools.tool.optimizeDb.name',
    descriptionKey: 'integration.systemTools.tool.optimizeDb.description'
  },
  {
    id: 'logout-users',
    nameKey: 'integration.systemTools.tool.logoutUsers.name',
    descriptionKey: 'integration.systemTools.tool.logoutUsers.description',
    danger: true
  }
];

export default function SystemTools() {
  const { t, language } = useI18n();
  const [loading, setLoading] = useState<string | null>(null);
  const [result, setResult] = useState<{ toolId: string; success: boolean; message: string } | null>(null);
  const [logs, setLogs] = useState<string[]>([]);
  const [logsLoading, setLogsLoading] = useState(false);
  const [refreshLock, setRefreshLock] = useState(false);
  const [liveMode, setLiveMode] = useState(false);
  const [autoScroll, setAutoScroll] = useState(true);
  const [searchQuery, setSearchQuery] = useState('');
  const [levelFilter, setLevelFilter] = useState<LogLevel | 'ALL'>('ALL');
  const [lastUpdate, setLastUpdate] = useState<Date | null>(null);
  const [newLogCount, setNewLogCount] = useState(0);
  const [errorPulse, setErrorPulse] = useState(false);
  const isRefreshing = logsLoading || refreshLock;
  const logContainerRef = useRef<HTMLDivElement>(null);
  const liveTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const pendingFetchRef = useRef(false);

  const executeTool = async (tool: ToolAction) => {
    if (!confirm(t('integration.systemTools.confirmExecute', { name: t(tool.nameKey) }))) return;

    setLoading(tool.id);
    setResult(null);

    try {
      let data: { success?: boolean; message?: string };
      switch (tool.id) {
        case 'clear-cache':
          data = await systemToolsApi.clearCache();
          break;
        case 'optimize-db':
          data = await systemToolsApi.optimizeDatabase();
          break;
        case 'logout-users':
          data = await systemToolsApi.logoutAllUsers();
          break;
        default:
          return;
      }
      setResult({
        toolId: tool.id,
        success: data.success ?? true,
        message: data.message || t('integration.systemTools.toast.succeeded')
      });
    } catch (err: unknown) {
      setResult({
        toolId: tool.id,
        success: false,
        message: err instanceof Error ? err.message : t('integration.systemTools.toast.failed')
      });
    } finally {
      setLoading(null);
    }
  };

  /**
   * 拉取日志。
   *
   * ⚠ 本函数（以及下面的 `clearLogsFiles`）内部的文案走 **`tBound`** 而不是 `t`：
   * 它被 `handleRefresh` 与两个 `useEffect` 捕获，而 `t` 是随语言变化的**新函数**
   * （`useCallback([merged, language])`）。一旦 `t` 进闭包，本函数在
   * `react-hooks/exhaustive-deps` 眼里就变成"reactive"，三个既有依赖数组会新增 3 条警告；
   * 本波不允许加 disable，也不该为此改依赖数组（会把定时轮询变成每次渲染重订阅）。
   * `tBound` 在**取词那一刻**读绑定的语言，语义与此处一致，且不引入新的依赖。
   * 代价：这两条（失败兜底）文案不享受 `Translations` 表覆盖层——它们是前端自持兜底串，
   * 与波次 9 `useCrudPage` 的失败 toast 同一处置。
   */
  const fetchLogs = async (isLive = false) => {
    if (isLive) {
      if (pendingFetchRef.current) return;
      pendingFetchRef.current = true;
    } else {
      setLogsLoading(true);
    }
    try {
      // 实时模式获取更多行以便看到完整上下文
      const data = await systemToolsApi.getLogs(isLive ? 200 : 50);
      const newLogs = typeof data === 'string' ? data : data?.logs;
      const parsed: string[] = typeof newLogs === 'string'
        ? newLogs.split('\n')
        : Array.isArray(newLogs)
          ? newLogs
          : [];
      setLogs((prev) => {
        // 实时模式下合并新旧日志，去重并限制总行数
        if (isLive && prev.length > 0) {
          const lastLine = prev[prev.length - 1];
          const lastIdx = parsed.lastIndexOf(lastLine);
          if (lastIdx >= 0 && lastIdx < parsed.length - 1) {
            // 追加新增的行
            const newLines = parsed.slice(lastIdx + 1);
            const appended = [...prev, ...newLines];
            // 统计新增日志数及错误级别日志
            const newErrors = newLines.filter(l => /\[(ERR|FTL)\]/i.test(l)).length;
            if (newLines.length > 0) {
              setNewLogCount((c) => c + newLines.length);
            }
            if (newErrors > 0) {
              setErrorPulse(true);
              setTimeout(() => setErrorPulse(false), 1500);
            }
            // 限制最多 500 行，超过时丢弃最旧的
            return appended.length > 500 ? appended.slice(appended.length - 500) : appended;
          }
          // 没有重叠或日志已轮转，直接替换
          return parsed;
        }
        return parsed;
      });
      setLastUpdate(new Date());
    } catch (err: unknown) {
      if (!isLive) {
        setLogs([err instanceof Error ? err.message : tBound('integration.systemTools.toast.logsLoadFailed')]);
      }
    } finally {
      if (isLive) {
        pendingFetchRef.current = false;
      } else {
        setLogsLoading(false);
      }
    }
  };

  const handleRefresh = useCallback(() => {
    if (isRefreshing) return;
    setRefreshLock(true);
    try {
      void fetchLogs(false);
    } finally {
      setTimeout(() => setRefreshLock(false), 400);
    }
  }, [isRefreshing]);

  // 实时模式轮询
  useEffect(() => {
    if (liveMode) {
      // 立即拉取一次
      void fetchLogs(true);
      // 每 2 秒拉取一次
      liveTimerRef.current = setInterval(() => {
        void fetchLogs(true);
      }, 2000);
      return () => {
        if (liveTimerRef.current) {
          clearInterval(liveTimerRef.current);
          liveTimerRef.current = null;
        }
      };
    } else {
      if (liveTimerRef.current) {
        clearInterval(liveTimerRef.current);
        liveTimerRef.current = null;
      }
    }
  }, [liveMode]);

  // 自动滚动到底部
  useEffect(() => {
    if (autoScroll && logContainerRef.current) {
      logContainerRef.current.scrollTop = logContainerRef.current.scrollHeight;
      // 用户在底部时，新日志计数自动清零（已即时可见）
      if (newLogCount > 0) setNewLogCount(0);
    }
  }, [logs, autoScroll, newLogCount]);

  // 检测用户手动滚动，暂停自动滚动
  const handleScroll = () => {
    const el = logContainerRef.current;
    if (!el) return;
    const atBottom = el.scrollHeight - el.scrollTop - el.clientHeight < 30;
    setAutoScroll(atBottom);
  };

  useEffect(() => {
    fetchLogs(false);
    return () => {
      if (liveTimerRef.current) {
        clearInterval(liveTimerRef.current);
      }
    };
  }, []);

  // 解析日志行并应用筛选
  const parsedLogs = useMemo(() => logs.map(parseLogLine), [logs]);

  const filteredLogs = useMemo(() => {
    let result = parsedLogs;
    if (levelFilter !== 'ALL') {
      result = result.filter(l => l.level === levelFilter);
    }
    if (searchQuery.trim()) {
      const q = searchQuery.toLowerCase();
      result = result.filter(l => l.raw.toLowerCase().includes(q));
    }
    return result;
  }, [parsedLogs, levelFilter, searchQuery]);

  // 统计各级别数量
  const levelCounts = useMemo(() => {
    const counts: Record<string, number> = {};
    for (const l of parsedLogs) {
      counts[l.level] = (counts[l.level] || 0) + 1;
    }
    return counts;
  }, [parsedLogs]);

  // 综合统计指标
  const stats = useMemo(() => {
    const total = parsedLogs.length;
    const errors = (levelCounts['ERR'] || 0) + (levelCounts['FTL'] || 0);
    const warnings = levelCounts['WRN'] || 0;
    const info = levelCounts['INF'] || 0;
    const healthy = total > 0 ? Math.round(((total - errors - warnings) / total) * 100) : 100;
    return { total, errors, warnings, info, healthy };
  }, [parsedLogs, levelCounts]);

  // 下载筛选后的日志
  const downloadLogs = useCallback(() => {
    const content = filteredLogs.map(l => l.raw).join('\n');
    const blob = new Blob([content], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    const ts = new Date().toISOString().slice(0, 19).replace(/[:-]/g, '').replace('T', '_');
    a.download = `system-logs_${ts}.log`;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
  }, [filteredLogs]);

  // 清空当前日志视图
  const clearLogsView = useCallback(() => {
    setLogs([]);
    setNewLogCount(0);
    setSearchQuery('');
    setLevelFilter('ALL');
    setAutoScroll(true);
  }, []);

  // 删除系统日志中的 log 记录：清空 log-*.log 文件内容（截断为 0 字节，不可恢复）
  // 文案走 `tBound`（理由见 `fetchLogs` 上方的注释）：本 `useCallback` 的依赖数组是 `[]`，
  // 引入 `t` 会让该数组立刻变成不完备依赖。
  const clearLogsFiles = useCallback(async () => {
    if (!confirm(tBound('integration.systemTools.clearLogsConfirm'))) return;
    setLogsLoading(true);
    try {
      const data = await systemToolsApi.clearLogs();
      setResult({
        toolId: 'clear-logs-files',
        success: data.success ?? true,
        message: data.message || tBound('integration.systemTools.toast.logsCleared')
      });
      // 清空视图并立即重新拉取，确认日志文件已清空
      setLogs([]);
      setNewLogCount(0);
      await fetchLogs(false);
    } catch (err: unknown) {
      setResult({
        toolId: 'clear-logs-files',
        success: false,
        message: err instanceof Error ? err.message : tBound('integration.systemTools.toast.clearLogsFailed')
      });
    } finally {
      setLogsLoading(false);
    }
  }, []);

  // 查看新日志：跳到底部并清空新日志计数
  const jumpToNewLogs = useCallback(() => {
    setAutoScroll(true);
    setNewLogCount(0);
    if (logContainerRef.current) {
      logContainerRef.current.scrollTop = logContainerRef.current.scrollHeight;
    }
  }, []);

  const getToolIcon = (toolId: string) => {
    if (toolId === 'clear-cache') return <Trash2 className="w-5 h-5" />;
    if (toolId === 'optimize-db') return <Database className="w-5 h-5" />;
    if (toolId === 'logout-users') return <LogOut className="w-5 h-5" />;
    return <Wrench className="w-5 h-5" />;
  };

  return (
    <div className="space-y-4 sm:space-y-6">
      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-slate-800 dark:to-slate-900 p-4 sm:p-6 border border-gray-200/50 dark:border-slate-700/50">
        <div className="absolute -top-10 -right-10 w-40 h-40 bg-primary-200/30 dark:bg-primary-900/20 rounded-full blur-3xl pointer-events-none" />
        <div className="relative flex items-start justify-between gap-3 sm:gap-4 flex-wrap">
          <div className="flex items-start gap-3 sm:gap-4 min-w-0">
            <div className="w-10 h-10 sm:w-12 sm:h-12 rounded-xl bg-gradient-to-br from-primary-500 to-indigo-600 text-white flex items-center justify-center shadow-lg shadow-primary-500/30 flex-shrink-0">
              <Wrench className="w-5 h-5 sm:w-6 sm:h-6" />
            </div>
            <div className="min-w-0">
              <h1 className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100">{t('integration.systemTools.pageTitle')}</h1>
              <p className="text-xs sm:text-sm text-gray-600 dark:text-slate-400 mt-1">{t('integration.systemTools.subtitle')}</p>
            </div>
          </div>
        </div>
      </div>

      {result && (
        <div className={`px-4 py-3 rounded-xl text-sm flex items-start gap-3 border transition ${
          result.success
            ? 'bg-green-50 text-green-700 border-green-200 dark:bg-green-900/20 dark:text-green-300 dark:border-green-800/50'
            : 'bg-red-50 text-red-700 border-red-200 dark:bg-red-900/20 dark:text-red-300 dark:border-red-800/50'
        }`}>
          {result.success
            ? <CheckCircle2 className="w-5 h-5 flex-shrink-0 mt-0.5" />
            : <XCircle className="w-5 h-5 flex-shrink-0 mt-0.5" />
          }
          <span className="break-words">{result.message}</span>
        </div>
      )}

      <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 gap-3 sm:gap-4">
        {tools.map((tool) => (
          <div
            key={tool.id}
            className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200/50 dark:border-slate-700/50 p-4 sm:p-5 shadow-sm hover:shadow-md hover:-translate-y-0.5 transition"
          >
            <div className="flex items-center gap-3 mb-3">
              <div className={`w-9 h-9 sm:w-10 sm:h-10 rounded-lg flex items-center justify-center text-white shadow-md flex-shrink-0 ${
                tool.danger
                  ? 'bg-gradient-to-br from-red-500 to-rose-600 shadow-red-500/30'
                  : 'bg-gradient-to-br from-blue-500 to-indigo-600 shadow-blue-500/30'
              }`}>
                {getToolIcon(tool.id)}
              </div>
              <h3 className="text-sm sm:text-base font-semibold text-gray-900 dark:text-slate-100 min-w-0">{t(tool.nameKey)}</h3>
            </div>
            <p className="text-xs sm:text-sm text-gray-500 dark:text-slate-400 mb-4">{t(tool.descriptionKey)}</p>
            <button
              onClick={() => executeTool(tool)}
              disabled={loading === tool.id}
              className={`w-full py-2 rounded-xl text-sm font-medium shadow-sm active:scale-[0.98] transition flex items-center justify-center gap-2 ${
                tool.danger
                  ? 'bg-red-600 hover:bg-red-700 disabled:bg-red-300 text-white'
                  : 'bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 text-white'
              }`}
            >
              {loading === tool.id ? (
                <>
                  <Loader2 className="w-4 h-4 animate-spin" />
                  {t('integration.systemTools.action.executing')}
                </>
              ) : (
                <>
                  {getToolIcon(tool.id)}
                  {t('integration.systemTools.action.execute')}
                </>
              )}
            </button>
          </div>
        ))}
      </div>

      <div className="bg-white dark:bg-slate-800 rounded-2xl border border-gray-200/50 dark:border-slate-700/50 shadow-sm overflow-hidden">
        {/* 顶部统计卡片 */}
        <div className={`grid grid-cols-2 md:grid-cols-4 gap-px bg-gray-200/60 dark:bg-slate-700/40 transition-colors ${
          errorPulse ? 'ring-2 ring-rose-400/50 ring-inset' : ''
        }`}>
          {/* 总日志数 */}
          <div className="bg-white dark:bg-slate-800 px-3 sm:px-4 py-2.5 sm:py-3 flex items-center gap-2 sm:gap-3">
            <div className="w-8 h-8 sm:w-9 sm:h-9 rounded-lg bg-gradient-to-br from-slate-500 to-slate-700 text-white flex items-center justify-center shadow-sm flex-shrink-0">
              <FileText className="w-3.5 h-3.5 sm:w-4 sm:h-4" />
            </div>
            <div className="min-w-0">
              <div className="text-[10px] sm:text-[11px] text-gray-500 dark:text-slate-400 uppercase tracking-wide">{t('integration.systemTools.stat.total')}</div>
              <div className="text-base sm:text-lg font-bold text-gray-900 dark:text-slate-100 tabular-nums leading-tight">{stats.total}</div>
            </div>
          </div>
          {/* 正常 INFO */}
          <div className="bg-white dark:bg-slate-800 px-3 sm:px-4 py-2.5 sm:py-3 flex items-center gap-2 sm:gap-3">
            <div className="w-8 h-8 sm:w-9 sm:h-9 rounded-lg bg-gradient-to-br from-emerald-500 to-green-600 text-white flex items-center justify-center shadow-sm flex-shrink-0">
              <CheckCircle2 className="w-3.5 h-3.5 sm:w-4 sm:h-4" />
            </div>
            <div className="min-w-0">
              <div className="text-[10px] sm:text-[11px] text-gray-500 dark:text-slate-400 uppercase tracking-wide">{t('integration.systemTools.stat.normal')}</div>
              <div className="text-base sm:text-lg font-bold text-emerald-600 dark:text-emerald-400 tabular-nums leading-tight">{stats.info}</div>
            </div>
          </div>
          {/* 警告 WRN */}
          <div className="bg-white dark:bg-slate-800 px-3 sm:px-4 py-2.5 sm:py-3 flex items-center gap-2 sm:gap-3">
            <div className="w-8 h-8 sm:w-9 sm:h-9 rounded-lg bg-gradient-to-br from-amber-500 to-orange-600 text-white flex items-center justify-center shadow-sm flex-shrink-0">
              <AlertTriangle className="w-3.5 h-3.5 sm:w-4 sm:h-4" />
            </div>
            <div className="min-w-0">
              <div className="text-[10px] sm:text-[11px] text-gray-500 dark:text-slate-400 uppercase tracking-wide">{t('integration.systemTools.stat.warning')}</div>
              <div className="text-base sm:text-lg font-bold text-amber-600 dark:text-amber-400 tabular-nums leading-tight">{stats.warnings}</div>
            </div>
          </div>
          {/* 错误 ERR+FTL（带脉冲动画） */}
          <div className={`bg-white dark:bg-slate-800 px-3 sm:px-4 py-2.5 sm:py-3 flex items-center gap-2 sm:gap-3 transition-all ${
            errorPulse ? 'bg-rose-50 dark:bg-rose-500/10' : ''
          }`}>
            <div className={`w-8 h-8 sm:w-9 sm:h-9 rounded-lg bg-gradient-to-br from-rose-500 to-red-600 text-white flex items-center justify-center shadow-sm flex-shrink-0 transition-transform ${
              errorPulse ? 'scale-110 shadow-rose-500/40' : ''
            }`}>
              <XCircle className="w-3.5 h-3.5 sm:w-4 sm:h-4" />
            </div>
            <div className="min-w-0">
              <div className="text-[10px] sm:text-[11px] text-gray-500 dark:text-slate-400 uppercase tracking-wide">{t('integration.systemTools.stat.error')}</div>
              <div className={`text-base sm:text-lg font-bold tabular-nums leading-tight transition-colors ${
                stats.errors > 0
                  ? 'text-rose-600 dark:text-rose-400'
                  : 'text-gray-900 dark:text-slate-100'
              }`}>
                {stats.errors}
                {stats.errors > 0 && (
                  <span className="ml-1.5 inline-block w-1.5 h-1.5 rounded-full bg-rose-500 animate-pulse align-middle"></span>
                )}
              </div>
            </div>
          </div>
        </div>

        {/* 健康度进度条 */}
        <div className="px-3 sm:px-5 py-2 bg-gray-50 dark:bg-slate-900/40 border-b border-gray-200/50 dark:border-slate-700/50 flex items-center gap-2 sm:gap-3">
          <TrendingUp className="w-3.5 h-3.5 text-gray-400 dark:text-slate-500 flex-shrink-0" />
          <span className="text-[11px] sm:text-xs text-gray-500 dark:text-slate-400 flex-shrink-0">{t('integration.systemTools.health')}</span>
          <div className="flex-1 h-1.5 bg-gray-200 dark:bg-slate-700 rounded-full overflow-hidden min-w-[40px]">
            <div
              className={`h-full rounded-full transition-all duration-500 ${
                stats.healthy >= 90
                  ? 'bg-gradient-to-r from-emerald-400 to-green-500'
                  : stats.healthy >= 70
                    ? 'bg-gradient-to-r from-amber-400 to-orange-500'
                    : 'bg-gradient-to-r from-rose-400 to-red-500'
              }`}
              style={{ width: `${stats.healthy}%` }}
            ></div>
          </div>
          <span className={`text-[11px] sm:text-xs font-semibold tabular-nums flex-shrink-0 ${
            stats.healthy >= 90
              ? 'text-emerald-600 dark:text-emerald-400'
              : stats.healthy >= 70
                ? 'text-amber-600 dark:text-amber-400'
                : 'text-rose-600 dark:text-rose-400'
          }`}>
            {stats.healthy}%
          </span>
        </div>

        {/* 终端窗口标题栏 */}
        <div className={`px-3 sm:px-5 py-3 sm:py-3.5 border-b transition-colors ${
          liveMode
            ? 'bg-gradient-to-r from-rose-50 to-red-50 dark:from-rose-500/10 dark:to-red-500/10 border-rose-200/50 dark:border-rose-500/20'
            : 'bg-gradient-to-r from-gray-50 to-slate-50 dark:from-slate-800 dark:to-slate-800/50 border-gray-200/50 dark:border-slate-700/50'
        }`}>
          <div className="flex items-center justify-between gap-2 sm:gap-3 flex-wrap">
            <div className="flex items-center gap-2 sm:gap-3 min-w-0">
              {/* macOS 风格三圆点 */}
              <div className="flex items-center gap-1.5 flex-shrink-0">
                <span className={`w-3 h-3 rounded-full ${liveMode ? 'bg-rose-400' : 'bg-rose-500'}`}></span>
                <span className={`w-3 h-3 rounded-full ${liveMode ? 'bg-amber-400' : 'bg-amber-500'}`}></span>
                <span className={`w-3 h-3 rounded-full ${liveMode ? 'bg-emerald-400' : 'bg-emerald-500'}`}></span>
              </div>
              <div className="hidden sm:block h-5 w-px bg-gray-300 dark:bg-slate-600"></div>
              <div className={`w-7 h-7 sm:w-8 sm:h-8 rounded-lg text-white flex items-center justify-center shadow-md transition-colors flex-shrink-0 ${
                liveMode
                  ? 'bg-gradient-to-br from-rose-500 to-red-600 shadow-rose-500/30'
                  : 'bg-gradient-to-br from-slate-600 to-slate-800'
              }`}>
                <Terminal className="w-3.5 h-3.5 sm:w-4 sm:h-4" />
              </div>
              <div className="flex items-center gap-2 min-w-0">
                <h3 className="text-sm sm:text-base font-semibold text-gray-900 dark:text-slate-100 truncate">{t('integration.systemTools.logTitle')}</h3>
                {liveMode ? (
                  <span className="inline-flex items-center gap-1 px-1.5 sm:px-2 py-0.5 rounded-full text-[10px] sm:text-[11px] font-semibold bg-rose-50 text-rose-600 dark:bg-rose-500/10 dark:text-rose-400 border border-rose-200 dark:border-rose-500/20 flex-shrink-0">
                    <span className="relative flex w-1.5 h-1.5">
                      <span className="absolute inset-0 rounded-full bg-rose-500 animate-ping opacity-75"></span>
                      <span className="relative rounded-full bg-rose-500 w-1.5 h-1.5"></span>
                    </span>
                    LIVE
                    {newLogCount > 0 && (
                      <span className="ml-0.5 px-1 py-px rounded-full bg-rose-500 text-white text-[10px] leading-none tabular-nums">
                        +{newLogCount > 99 ? '99+' : newLogCount}
                      </span>
                    )}
                  </span>
                ) : (
                  <span className="hidden sm:inline-flex items-center gap-1 px-2 py-0.5 rounded-full text-[11px] font-medium bg-slate-100 text-slate-500 dark:bg-slate-700 dark:text-slate-400 border border-slate-200 dark:border-slate-600">
                    <span className="w-1.5 h-1.5 rounded-full bg-slate-400"></span>
                    PAUSED
                  </span>
                )}
              </div>
            </div>
            <div className="flex items-center gap-1.5 sm:gap-2 flex-wrap">
              {/* 实时监控开关 */}
              <button
                onClick={() => setLiveMode((v) => !v)}
                className={`px-2.5 sm:px-3 py-1.5 rounded-xl text-xs sm:text-sm font-medium shadow-sm active:scale-[0.98] transition flex items-center gap-1.5 border ${
                  liveMode
                    ? 'bg-rose-50 dark:bg-rose-500/10 text-rose-600 dark:text-rose-400 border-rose-200 dark:border-rose-500/30 hover:bg-rose-100 dark:hover:bg-rose-500/20'
                    : 'bg-white dark:bg-slate-700 text-gray-700 dark:text-slate-300 border-gray-300 dark:border-slate-600 hover:bg-gray-50 dark:hover:bg-slate-600'
                }`}
                title={liveMode ? t('integration.systemTools.live.pauseTitle') : t('integration.systemTools.live.startTitle')}
              >
                {liveMode ? (
                  <>
                    <Pause className="w-3.5 h-3.5" />
                    <span className="hidden sm:inline">{t('integration.systemTools.live.pause')}</span>
                  </>
                ) : (
                  <>
                    <Radio className="w-3.5 h-3.5" />
                    <span className="hidden sm:inline">{t('integration.systemTools.live.start')}</span>
                  </>
                )}
              </button>
              {/* 手动刷新按钮（实时模式下禁用） */}
              <button
                onClick={handleRefresh}
                disabled={isRefreshing || liveMode}
                className="px-2.5 sm:px-3 py-1.5 bg-white dark:bg-slate-700 border border-gray-300 dark:border-slate-600 dark:text-slate-300 rounded-xl text-xs sm:text-sm text-gray-700 hover:bg-gray-50 dark:hover:bg-slate-600 shadow-sm active:scale-[0.98] transition disabled:opacity-60 disabled:cursor-not-allowed flex items-center gap-1.5"
                title={liveMode ? t('integration.systemTools.refresh.disabledTitle') : t('integration.systemTools.refresh.title')}
              >
                <RefreshCw className={`w-3.5 h-3.5 ${isRefreshing ? 'animate-spin' : ''}`} />
                <span className="hidden sm:inline">{isRefreshing ? t('integration.systemTools.refresh.refreshing') : t('integration.systemTools.refresh.label')}</span>
              </button>
              {/* 下载日志按钮 */}
              <button
                onClick={downloadLogs}
                disabled={filteredLogs.length === 0}
                className="px-2.5 sm:px-3 py-1.5 bg-white dark:bg-slate-700 border border-gray-300 dark:border-slate-600 dark:text-slate-300 rounded-xl text-xs sm:text-sm text-gray-700 hover:bg-gray-50 dark:hover:bg-slate-600 shadow-sm active:scale-[0.98] transition disabled:opacity-60 disabled:cursor-not-allowed flex items-center gap-1.5"
                title={t('integration.systemTools.download.title')}
              >
                <Download className="w-3.5 h-3.5" />
                <span className="hidden sm:inline">{t('integration.systemTools.download.label')}</span>
              </button>
              {/* 清空视图按钮 */}
              <button
                onClick={clearLogsView}
                disabled={logs.length === 0}
                className="px-2.5 sm:px-3 py-1.5 bg-white dark:bg-slate-700 border border-gray-300 dark:border-slate-600 dark:text-slate-300 rounded-xl text-xs sm:text-sm text-gray-700 hover:bg-gray-50 dark:hover:bg-slate-600 shadow-sm active:scale-[0.98] transition disabled:opacity-60 disabled:cursor-not-allowed flex items-center gap-1.5"
                title={t('integration.systemTools.clearView.title')}
              >
                <Eraser className="w-3.5 h-3.5" />
                <span className="hidden sm:inline">{t('integration.systemTools.clearView.label')}</span>
              </button>
              {/* 清空日志文件按钮（危险操作：删除 log-*.log 中的记录） */}
              <button
                onClick={() => void clearLogsFiles()}
                disabled={isRefreshing}
                className="px-2.5 sm:px-3 py-1.5 bg-red-50 dark:bg-red-500/10 border border-red-200 dark:border-red-500/30 dark:text-red-400 rounded-xl text-xs sm:text-sm text-red-600 hover:bg-red-100 dark:hover:bg-red-500/20 shadow-sm active:scale-[0.98] transition disabled:opacity-60 disabled:cursor-not-allowed flex items-center gap-1.5"
                title={t('integration.systemTools.clearFiles.title')}
              >
                <Trash2 className="w-3.5 h-3.5" />
                <span className="hidden sm:inline">{t('integration.systemTools.clearFiles.label')}</span>
              </button>
            </div>
          </div>

          {/* 状态栏：轮询信息 + 级别筛选 + 搜索 */}
          <div className="mt-3 flex items-center justify-between gap-2 sm:gap-3 flex-wrap">
            <div className="flex items-center gap-2 sm:gap-3 text-[10px] sm:text-xs text-gray-500 dark:text-slate-400 flex-shrink-0">
              <span className="flex items-center gap-1">
                <Activity className="w-3 h-3" />
                {liveMode ? t('integration.systemTools.polling.every2s') : t('integration.systemTools.polling.stopped')}
              </span>
              <span className="flex items-center gap-1">
                <Clock className="w-3 h-3" />
                {lastUpdate ? lastUpdate.toLocaleTimeString(toIntlLocale(language), { hour12: false }) : '--'}
              </span>
              <span className="tabular-nums">
                {filteredLogs.length}/{logs.length}
              </span>
            </div>

            {/* 级别筛选胶囊 */}
            <div className="flex items-center gap-1 overflow-x-auto max-w-full">
              <Filter className="w-3 h-3 text-gray-400 dark:text-slate-500 mr-1 flex-shrink-0" />
              {(['ALL', 'INF', 'WRN', 'ERR', 'DBG'] as const).map((lvl) => {
                const count = lvl === 'ALL' ? logs.length : (levelCounts[lvl] || 0);
                const isActive = levelFilter === lvl;
                const style = lvl === 'ALL' ? null : LEVEL_STYLES[lvl as LogLevel];
                return (
                  <button
                    key={lvl}
                    onClick={() => setLevelFilter(lvl)}
                    disabled={lvl !== 'ALL' && count === 0}
                    className={`px-1.5 sm:px-2 py-0.5 rounded-md text-[10px] sm:text-[11px] font-medium transition border tabular-nums disabled:opacity-30 disabled:cursor-not-allowed whitespace-nowrap ${
                      isActive
                        ? style
                          ? `${style.bg} ${style.text} border-current shadow-sm`
                          : 'bg-slate-200 dark:bg-slate-600 text-slate-700 dark:text-slate-200 border-slate-300 dark:border-slate-500 shadow-sm'
                        : style
                          ? `${style.bg} ${style.text} border-transparent hover:border-current/30`
                          : 'bg-transparent text-gray-500 dark:text-slate-400 border-transparent hover:bg-gray-100 dark:hover:bg-slate-700'
                    }`}
                  >
                    {lvl === 'ALL' ? t('integration.systemTools.filter.all') : LEVEL_STYLES[lvl as LogLevel].label}
                    {count > 0 && <span className="ml-1 opacity-60">{count}</span>}
                  </button>
                );
              })}
            </div>

            {/* 搜索框 */}
            <div className="relative w-full sm:w-auto">
              <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-400 dark:text-slate-500 pointer-events-none" />
              <input
                type="text"
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                placeholder={t('integration.systemTools.searchPlaceholder')}
                className="pl-8 pr-7 py-1 text-xs bg-white dark:bg-slate-900/80 border border-gray-200 dark:border-slate-700 rounded-lg text-gray-700 dark:text-slate-200 placeholder:text-gray-400 dark:placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-primary-500/30 focus:border-primary-400 w-full sm:w-40 transition-all sm:focus:w-56"
              />
              {searchQuery && (
                <button
                  onClick={() => setSearchQuery('')}
                  className="absolute right-1.5 top-1/2 -translate-y-1/2 w-4 h-4 flex items-center justify-center text-gray-400 hover:text-gray-600 dark:hover:text-slate-300 rounded transition"
                >
                  <X className="w-3 h-3" />
                </button>
              )}
            </div>
          </div>
        </div>

        {/* 日志内容区 */}
        <div className="p-3 sm:p-4">
          {logs.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-12 text-gray-400 dark:text-slate-500">
              <FileText className="w-10 h-10 mb-2 opacity-30" />
              <p className="text-sm">{t('integration.systemTools.empty.noLogs')}</p>
            </div>
          ) : filteredLogs.length === 0 ? (
            <div className="flex flex-col items-center justify-center py-12 text-gray-400 dark:text-slate-500">
              <Filter className="w-10 h-10 mb-2 opacity-30" />
              <p className="text-sm">{t('integration.systemTools.empty.noMatch')}</p>
              <button
                onClick={() => { setSearchQuery(''); setLevelFilter('ALL'); }}
                className="mt-2 text-xs text-primary-600 dark:text-primary-400 hover:underline"
              >
                {t('integration.systemTools.clearFilters')}
              </button>
            </div>
          ) : (
            <div className="relative">
              <div
                ref={logContainerRef}
                onScroll={handleScroll}
                className="bg-gray-950 dark:bg-black border border-gray-800 dark:border-slate-700/50 rounded-xl overflow-auto scroll-smooth shadow-inner"
                style={{ maxHeight: '32rem' }}
              >
                <div className="font-mono text-[10px] sm:text-xs">
                  {filteredLogs.map((line, idx) => {
                    const style = LEVEL_STYLES[line.level];
                    const lineNum = idx + 1;
                    return (
                      <div
                        key={idx}
                        className="group flex items-start hover:bg-white/5 transition-colors px-2"
                      >
                        {/* 行号 */}
                        <span className="select-none text-gray-600 dark:text-slate-700 text-right pr-2 sm:pr-3 pl-1 py-0.5 w-8 sm:w-12 flex-shrink-0 tabular-nums group-hover:text-gray-500 dark:group-hover:text-slate-500">
                          {lineNum}
                        </span>
                        {/* 级别标签 */}
                        <span className={`select-none inline-flex items-center gap-1 px-1 sm:px-1.5 py-0.5 mr-1 sm:mr-2 mt-0.5 rounded ${style.bg} ${style.text} font-semibold text-[9px] sm:text-[10px] flex-shrink-0`}>
                          <span className={`w-1 h-1 rounded-full ${style.dot}`}></span>
                          {style.label}
                        </span>
                        {/* 时间戳 */}
                        {line.timestamp && (
                          <span className="hidden sm:inline-block select-none text-gray-500 dark:text-slate-500 pr-2 py-0.5 flex-shrink-0 tabular-nums">
                            {line.timestamp}
                          </span>
                        )}
                        {/* 日志内容 */}
                        <span className={`py-0.5 pr-2 break-all ${style.text === 'text-slate-400' ? 'text-gray-300 dark:text-slate-300' : style.text}`}>
                          {line.content}
                        </span>
                      </div>
                    );
                  })}
                </div>
              </div>
              {/* 滚动到底部 / 新日志提示按钮 */}
              {(!autoScroll || newLogCount > 0) && (
                <button
                  onClick={jumpToNewLogs}
                  className={`absolute bottom-3 right-3 sm:bottom-4 sm:right-4 px-2.5 sm:px-3 py-1.5 rounded-lg text-xs font-medium shadow-lg flex items-center gap-1.5 transition-all duration-200 border hover:scale-105 ${
                    newLogCount > 0 && liveMode
                      ? 'bg-gradient-to-r from-rose-500 to-red-600 hover:from-rose-600 hover:to-red-700 text-white border-rose-400 shadow-rose-500/40 animate-pulse'
                      : 'bg-gray-800 dark:bg-slate-700 hover:bg-gray-700 dark:hover:bg-slate-600 text-white border-gray-700 dark:border-slate-600'
                  }`}
                  title={newLogCount > 0 ? t('integration.systemTools.newLogs.title', { count: newLogCount }) : t('integration.systemTools.scrollToBottom.title')}
                >
                  {newLogCount > 0 && liveMode ? (
                    <>
                      <Zap className="w-3 h-3" />
                      {newLogCount > 99 ? '99+' : newLogCount} {t('integration.systemTools.newLogs.suffix')}
                    </>
                  ) : (
                    <>
                      <ArrowDown className="w-3 h-3" />
                      {t('integration.systemTools.scrollToBottom.label')}
                    </>
                  )}
                </button>
              )}
            </div>
          )}
        </div>
      </div>

      <div className="relative overflow-hidden bg-gradient-to-r from-yellow-50 to-amber-50 dark:from-amber-900/20 dark:to-yellow-900/20 border border-yellow-200 dark:border-amber-800/50 text-yellow-800 dark:text-amber-200 px-4 py-3 rounded-xl text-xs sm:text-sm flex items-start gap-3">
        <div className="w-8 h-8 rounded-lg bg-gradient-to-br from-yellow-400 to-amber-500 text-white flex items-center justify-center shadow-md flex-shrink-0">
          <AlertTriangle className="w-4 h-4" />
        </div>
        <p className="mt-1.5">{t('integration.systemTools.footerWarning')}</p>
      </div>
    </div>
  );
}
