import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { useNavigate } from 'react-router-dom';
import { createPortal } from 'react-dom';
import { customReportApi } from '../api';
import { useI18n } from '../context/I18nContext';
import { useToast } from '../context/ToastContext';
import type { CustomReportDto } from '../api/types';
import type { TranslationKey } from '../locales/zh-Hans';
import ConfirmDialog from '../components/ConfirmDialog';
import CustomReportScheduleModal from '../components/CustomReportScheduleModal';
import { formatDateTime } from '../utils/format';
import {
  Plus,
  Search,
  RefreshCw,
  Pin,
  PinOff,
  MoreHorizontal,
  Eye,
  Edit2,
  Trash2,
  Calendar,
  FileSpreadsheet,
  FileText,
  File,
  Grid3X3,
  BarChart3,
  ChevronLeft,
  ChevronRight,
  Undo2,
  AlertTriangle,
  Trash,
} from 'lucide-react';

// `value` 是后端 `CustomReportDto.category` 直接返回的稳定 code（契约，不翻译）；
// 文案走 `labelKey`。字段名刻意不叫 `label`——漏包 `t()` 的使用点会被 tsc 以 TS2339 抓出。
const CATEGORY_OPTIONS: { value: string; labelKey: TranslationKey }[] = [
  { value: '', labelKey: 'data.customReports.category.all' },
  { value: 'performance', labelKey: 'data.customReports.category.performance' },
  { value: 'efficiency', labelKey: 'data.customReports.category.efficiency' },
  { value: 'customer', labelKey: 'data.customReports.category.customer' },
  { value: 'workload', labelKey: 'data.customReports.category.workload' },
];

/**
 * 后端 `CustomReportService.ValidCategories` 的 4 个码 → 字典键。
 * 用 `| undefined` 而非 `Record<string, TranslationKey>`：码不可识别时运行期确实取不到键
 * （渲染回落成原始 code），标成非空是类型谎言。
 */
const CATEGORY_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  performance: 'data.customReports.category.performance',
  efficiency: 'data.customReports.category.efficiency',
  customer: 'data.customReports.category.customer',
  workload: 'data.customReports.category.workload',
};

const CATEGORY_COLORS: Record<string, string> = {
  performance: 'bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-400 ring-1 ring-blue-200/60 dark:ring-blue-700/40',
  efficiency: 'bg-green-50 text-green-700 dark:bg-green-900/30 dark:text-green-400 ring-1 ring-green-200/60 dark:ring-green-700/40',
  customer: 'bg-purple-50 text-purple-700 dark:bg-purple-900/30 dark:text-purple-400 ring-1 ring-purple-200/60 dark:ring-purple-700/40',
  workload: 'bg-amber-50 text-amber-700 dark:bg-amber-900/30 dark:text-amber-400 ring-1 ring-amber-200/60 dark:ring-amber-700/40',
};

// `value` 是后端 `CustomReportDto.visibility` 直接返回的稳定 code（契约，不翻译）。
const VISIBILITY_OPTIONS: { value: string; labelKey: TranslationKey }[] = [
  { value: '', labelKey: 'data.customReports.visibility.all' },
  { value: 'private', labelKey: 'data.customReports.visibility.private' },
  { value: 'team', labelKey: 'data.customReports.visibility.team' },
  { value: 'public', labelKey: 'data.customReports.visibility.public' },
];

// icon / color 是展示数据不是文案；`labelKey` 供 `t()` 取词。
const VISIBILITY_LABELS: Record<string, { labelKey: TranslationKey; color: string; icon: string }> = {
  private: { labelKey: 'data.customReports.visibility.private', icon: '🔒', color: 'bg-gray-100 text-gray-700 dark:bg-slate-700 dark:text-slate-300' },
  team: { labelKey: 'data.customReports.visibility.team', icon: '👥', color: 'bg-indigo-100 text-indigo-700 dark:bg-indigo-900/40 dark:text-indigo-400' },
  public: { labelKey: 'data.customReports.visibility.public', icon: '🌐', color: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-400' },
};

function Pagination({
  currentPage,
  totalPages,
  totalItems,
  pageSize,
  onPageChange,
}: {
  currentPage: number;
  totalPages: number;
  totalItems: number;
  pageSize: number;
  onPageChange: (p: number) => void;
}) {
  // hook 必须在提前 return 之前无条件调用
  const { t } = useI18n();
  if (totalPages <= 1) return null;
  const pages: number[] = [];
  const maxVisible = 5;
  let start = Math.max(1, currentPage - Math.floor(maxVisible / 2));
  const end = Math.min(totalPages, start + maxVisible - 1);
  if (end - start + 1 < maxVisible) start = Math.max(1, end - maxVisible + 1);
  for (let i = start; i <= end; i++) pages.push(i);
  return (
    <div className="flex items-center justify-between px-4 py-3 border-t border-slate-100 dark:border-slate-700 text-sm">
      <span className="text-slate-500 dark:text-slate-400">
        {/* 带样式的数值必须留在自己的 <span> 里 → 拆前后缀键（英文键自带空格） */}
        {t('data.customReports.pagination.totalPrefix')}
        <span className="font-medium text-slate-700 dark:text-slate-300">{totalItems}</span>
        {t('data.customReports.pagination.summary', { pageSize, currentPage, totalPages })}
      </span>
      <div className="flex items-center gap-1">
        <button
          onClick={() => onPageChange(currentPage - 1)}
          disabled={currentPage <= 1}
          className="p-1.5 rounded-lg border border-slate-200 dark:border-slate-600 disabled:opacity-40 hover:bg-slate-50 dark:hover:bg-slate-700 transition-colors"
        >
          <ChevronLeft size={16} />
        </button>
        {pages.map((p) => (
          <button
            key={p}
            onClick={() => onPageChange(p)}
            className={`min-w-[32px] px-2 py-1.5 rounded-lg text-sm font-medium transition-colors ${
              p === currentPage
                ? 'bg-blue-600 text-white shadow-sm'
                : 'border border-slate-200 dark:border-slate-600 text-slate-600 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700'
            }`}
          >
            {p}
          </button>
        ))}
        <button
          onClick={() => onPageChange(currentPage + 1)}
          disabled={currentPage >= totalPages}
          className="p-1.5 rounded-lg border border-slate-200 dark:border-slate-600 disabled:opacity-40 hover:bg-slate-50 dark:hover:bg-slate-700 transition-colors"
        >
          <ChevronRight size={16} />
        </button>
      </div>
    </div>
  );
}

export default function CustomReports() {
  const navigate = useNavigate();
  const toast = useToast();
  const { t } = useI18n();

  const [tab, setTab] = useState<'all' | 'pinned' | 'trash'>('all');
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const searchTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  const handleSearchChange = (value: string) => {
    setSearch(value);
    if (searchTimer.current) clearTimeout(searchTimer.current);
    searchTimer.current = setTimeout(() => {
      setDebouncedSearch(value);
      setPage(1);
    }, 350);
  };
  const [category, setCategory] = useState('');
  const [visibility, setVisibility] = useState('');
  const [page, setPage] = useState(1);
  const pageSize = 12;

  const [reports, setReports] = useState<CustomReportDto[]>([]);
  const [totalItems, setTotalItems] = useState(0);
  const [totalPages, setTotalPages] = useState(1);
  const [loading, setLoading] = useState(false);

  const [pinnedReports, setPinnedReports] = useState<CustomReportDto[]>([]);
  const [loadingPinned, setLoadingPinned] = useState(false);

  const [trashReports, setTrashReports] = useState<CustomReportDto[]>([]);
  const [trashTotalItems, setTrashTotalItems] = useState(0);
  const [trashTotalPages, setTrashTotalPages] = useState(1);
  const [loadingTrash, setLoadingTrash] = useState(false);

  const [deleteTarget, setDeleteTarget] = useState<CustomReportDto | null>(null);
  const [deleting, setDeleting] = useState(false);

  const [hardDeleteTarget, setHardDeleteTarget] = useState<CustomReportDto | null>(null);
  const [hardDeleting, setHardDeleting] = useState(false);

  const [clearingTrash, setClearingTrash] = useState(false);
  const [confirmingClearTrash, setConfirmingClearTrash] = useState(false);

  const [restoreTarget, setRestoreTarget] = useState<CustomReportDto | null>(null);
  const [restoring, setRestoring] = useState(false);

  const [scheduleModal, setScheduleModal] = useState<{ open: boolean; reportId: number | null }>({
    open: false,
    reportId: null,
  });

  const [openMenuId, setOpenMenuId] = useState<number | null>(null);
  const [exportingId, setExportingId] = useState<number | null>(null);
  const [menuPosition, setMenuPosition] = useState<{ top: number; left: number; upward: boolean; bottom?: number | null } | null>(null);
  const menuTriggerRef = useRef<HTMLButtonElement | null>(null);
  const MENU_WIDTH = 192; // w-48 = 12rem = 192px
  const MENU_HEIGHT_ESTIMATE = 360; // 足夠容納完整菜單（查看+編輯+調度+導出3個+刪除 + 分隔線）

  const fetchReports = useCallback(async () => {
    setLoading(true);
    try {
      const res = await customReportApi.getPaged({
        search: debouncedSearch || undefined,
        category: category || undefined,
        visibility: visibility || undefined,
        page,
        pageSize,
      });
      setReports(res.data ?? []);
      setTotalItems(res.totalItems ?? 0);
      setTotalPages(res.totalPages ?? 1);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.common.loadFailed');
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }, [debouncedSearch, category, visibility, page, pageSize, toast, t]);

  const fetchPinned = useCallback(async () => {
    setLoadingPinned(true);
    try {
      const data = await customReportApi.getPinned(12);
      setPinnedReports(data);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.customReports.toast.loadPinnedFailed');
      toast.error(msg);
    } finally {
      setLoadingPinned(false);
    }
  }, [toast, t]);

  const fetchTrash = useCallback(async () => {
    setLoadingTrash(true);
    try {
      const res = await customReportApi.getTrashPaged({
        search: debouncedSearch || undefined,
        page,
        pageSize,
      });
      setTrashReports(res.data ?? []);
      setTrashTotalItems(res.totalItems ?? 0);
      setTrashTotalPages(res.totalPages ?? 1);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('common.loadTrashFailed');
      toast.error(msg);
    } finally {
      setLoadingTrash(false);
    }
  }, [debouncedSearch, page, pageSize, toast, t]);

  useEffect(() => {
    if (tab === 'all') fetchReports();
    else if (tab === 'pinned') fetchPinned();
    else fetchTrash();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab, debouncedSearch, category, visibility, page]);

  useEffect(() => {
    const onClick = () => {
      setOpenMenuId(null);
      setMenuPosition(null);
    };
    document.addEventListener('click', onClick);
    return () => document.removeEventListener('click', onClick);
  }, []);

  // 计算菜单位置（Portal 渲染，避免被祖先容器 overflow 裁剪）
  // 使用 fixed + viewport 坐标；滚动/尺寸变化会自动关菜单所以无需更新位置
  const openReportMenu = useCallback((_r: CustomReportDto, triggerEl: HTMLElement) => {
    const rect = triggerEl.getBoundingClientRect();
    const vh = window.innerHeight || document.documentElement.clientHeight;
    const vw = window.innerWidth || document.documentElement.clientWidth;
    const spaceBelow = vh - rect.bottom;
    // 下方空间不够且上方足够时，向上弹出
    const upward = spaceBelow < MENU_HEIGHT_ESTIMATE && rect.top > MENU_HEIGHT_ESTIMATE;
    // 水平：菜单右边缘对齐按钮右边缘
    let left = rect.right - MENU_WIDTH;
    const minLeft = 8;
    const maxLeft = vw - MENU_WIDTH - 8;
    if (left < minLeft) left = minLeft;
    if (left > maxLeft) left = maxLeft;
    let top: number;
    let bottom: number | null = null;
    if (upward) {
      // 向上弹出：菜单底边距按钮顶边 4px
      bottom = vh - rect.top + 4;
      top = -1; // 占位，渲染时忽略 top 用 bottom
    } else {
      top = rect.bottom + 4;
    }
    setMenuPosition({
      top,
      bottom,
      left,
      upward,
    });
    menuTriggerRef.current = triggerEl as HTMLButtonElement;
  }, [MENU_HEIGHT_ESTIMATE, MENU_WIDTH]);

  // 窗口滚动 / 尺寸变化时重新定位已打开的菜单，或直接关闭（简单稳妥：关闭）
  useEffect(() => {
    if (openMenuId === null) return;
    const handler = () => {
      setOpenMenuId(null);
      setMenuPosition(null);
    };
    window.addEventListener('scroll', handler, true);
    window.addEventListener('resize', handler);
    return () => {
      window.removeEventListener('scroll', handler, true);
      window.removeEventListener('resize', handler);
    };
  }, [openMenuId]);

  const handleTogglePin = async (r: CustomReportDto) => {
    try {
      const updated = await customReportApi.togglePin(r.id, {
        pinned: !r.isDashboardPinned,
        sortOrder: r.isDashboardPinned ? null : 0,
      });
      setReports((prev) => prev.map((x) => (x.id === r.id ? { ...x, isDashboardPinned: updated.isDashboardPinned } : x)));
      setPinnedReports((prev) => prev.map((x) => (x.id === r.id ? { ...x, isDashboardPinned: updated.isDashboardPinned } : x)));
      toast.success(updated.isDashboardPinned ? t('data.customReports.toast.pinned') : t('data.customReports.toast.unpinned'));
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.customReports.toast.operationFailed');
      toast.error(msg);
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await customReportApi.remove(deleteTarget.id);
      toast.success(t('data.customReports.toast.deleted'));
      setDeleteTarget(null);
      if (tab === 'all') fetchReports();
      else fetchPinned();
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.customReports.toast.deleteFailed');
      toast.error(msg);
    } finally {
      setDeleting(false);
    }
  };

  const handleRestore = async () => {
    if (!restoreTarget) return;
    setRestoring(true);
    try {
      await customReportApi.restore(restoreTarget.id);
      toast.success(t('data.customReports.toast.restored'));
      setRestoreTarget(null);
      fetchTrash();
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.customReports.toast.restoreFailed');
      toast.error(msg);
    } finally {
      setRestoring(false);
    }
  };

  const handleHardDelete = async () => {
    if (!hardDeleteTarget) return;
    setHardDeleting(true);
    try {
      await customReportApi.hardDelete(hardDeleteTarget.id);
      toast.success(t('data.customReports.toast.hardDeleted'));
      setHardDeleteTarget(null);
      fetchTrash();
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.customReports.toast.hardDeleteFailed');
      toast.error(msg);
    } finally {
      setHardDeleting(false);
    }
  };

  const handleClearTrash = async () => {
    setClearingTrash(true);
    try {
      const result = await customReportApi.clearTrash();
      toast.success(t('data.customReports.toast.trashCleared', { count: result.cleared }));
      setConfirmingClearTrash(false);
      fetchTrash();
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.customReports.toast.clearFailed');
      toast.error(msg);
    } finally {
      setClearingTrash(false);
    }
  };

  const handleExport = async (id: number, format: 'csv' | 'xlsx' | 'pdf') => {
    if (exportingId !== null) return;
    setExportingId(id);
    try {
      const blob = await customReportApi.exportFile(id, format);
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `custom_report_${id}_${new Date().toISOString().slice(0, 10)}.${format}`;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      window.URL.revokeObjectURL(url);
      toast.success(t('data.common.exportSucceeded'));
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.common.exportFailed');
      toast.error(msg);
    } finally {
      setExportingId(null);
    }
  };

  const displayReports = useMemo(() => {
    if (tab === 'all') return reports;
    if (tab === 'pinned') return pinnedReports;
    return trashReports;
  }, [tab, reports, pinnedReports, trashReports]);
  const displayLoading = tab === 'all' ? loading : tab === 'pinned' ? loadingPinned : loadingTrash;
  const displayTotalItems = tab === 'trash' ? trashTotalItems : totalItems;
  const displayTotalPages = tab === 'trash' ? trashTotalPages : totalPages;
  const [refreshLock, setRefreshLock] = useState(false);
  const isRefreshing = displayLoading || refreshLock;

  const handleRefresh = useCallback(() => {
    if (isRefreshing) return;
    setRefreshLock(true);
    try {
      if (tab === 'all') fetchReports();
      else if (tab === 'pinned') fetchPinned();
      else fetchTrash();
    } finally {
      setTimeout(() => setRefreshLock(false), 400);
    }
  }, [isRefreshing, tab, fetchReports, fetchPinned, fetchTrash]);

  return (
    <div className="space-y-5">
      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-r from-violet-600 via-indigo-600 to-blue-600 p-5 sm:p-6 shadow-lg">
        <div className="absolute inset-0 bg-[url('data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iNjAiIGhlaWdodD0iNjAiIHZpZXdCb3g9IjAgMCA2MCA2MCIgeG1sbnM9Imh0dHA6Ly93d3cudzMub3JnLzIwMDAvc3ZnIj48ZyBmaWxsPSJub25lIiBmaWxsLXJ1bGU9ImV2ZW5vZGQiPjxwYXRoIGQ9Ik0zNiAzNGgtNHYyaC00di0yaC00VjIyaDQydjEyek0xOCAyNnYyaDEydi0ySDE4em0wIDR2MmgxMnYtMkgxOHoiIGZpbGw9IiNmZmYiIGZpbGwtb3BhY2l0eT0iMC4wNSIvPjwvZz48L3N2Zz4=')] opacity-30" />
        <div className="relative flex items-start justify-between gap-3 flex-wrap">
          <div className="flex items-start gap-3 min-w-0 flex-1">
            <div className="w-10 h-10 sm:w-12 sm:h-12 rounded-xl bg-white/20 backdrop-blur-sm flex items-center justify-center shrink-0">
              <BarChart3 className="w-6 h-6 sm:w-7 sm:h-7 text-white" />
            </div>
            <div className="min-w-0">
              <h1 className="text-xl sm:text-2xl font-bold text-white">{t('data.customReports.title')}</h1>
              <p className="text-xs sm:text-sm text-indigo-100 mt-1 flex items-center gap-1.5 sm:gap-2">
                <Grid3X3 size={14} className="shrink-0" />
                {t('data.customReports.description')}
              </p>
            </div>
          </div>
          <div className="flex items-center gap-2 shrink-0">
            <button
              onClick={handleRefresh}
              disabled={isRefreshing}
              className="flex items-center gap-1.5 sm:gap-2 px-3 sm:px-4 py-2 sm:py-2.5 text-sm font-medium text-white bg-white/20 hover:bg-white/30 backdrop-blur-sm rounded-xl border border-white/20 disabled:opacity-60 disabled:cursor-not-allowed active:scale-[0.98] transition-all duration-200"
            >
              <RefreshCw size={15} className={isRefreshing ? 'animate-spin' : ''} />
              <span className="hidden sm:inline">{t('data.common.refresh')}</span>
            </button>
            {tab === 'trash' ? (
              <button
                onClick={() => setConfirmingClearTrash(true)}
                disabled={displayLoading || trashTotalItems === 0}
                className="flex items-center gap-1.5 sm:gap-2 px-3 sm:px-4 py-2 sm:py-2.5 text-sm font-medium text-white bg-rose-500 hover:bg-rose-600 disabled:opacity-60 disabled:cursor-not-allowed rounded-xl transition-all shadow-sm"
              >
                <Trash size={16} />
                <span className="hidden sm:inline">{t('data.customReports.action.clearTrash')}</span>
                <span className="sm:hidden">{t('data.customReports.action.clear')}</span>
              </button>
            ) : (
              <button
                onClick={() => navigate('/modules/custom-reports/new')}
                className="flex items-center gap-1.5 sm:gap-2 px-3 sm:px-4 py-2 sm:py-2.5 text-sm font-medium text-indigo-700 bg-white hover:bg-indigo-50 rounded-xl transition-all shadow-sm"
              >
                <Plus size={16} />
                <span className="hidden sm:inline">{t('data.customReports.action.createReport')}</span>
                <span className="sm:hidden">{t('data.customReports.action.create')}</span>
              </button>
            )}
          </div>
        </div>
      </div>

      <div className="bg-white dark:bg-slate-800 rounded-xl border border-slate-100 dark:border-slate-700/50 p-3 sm:p-4 shadow-sm">
        <div className="flex flex-wrap items-center gap-2 sm:gap-3">
          <div className="flex items-center gap-2 flex-1 min-w-[160px] sm:min-w-[240px] px-3 py-2 bg-slate-50 dark:bg-slate-900/40 rounded-lg focus-within:ring-2 focus-within:ring-blue-500/20 transition-all">
            <Search size={16} className="text-slate-400 shrink-0" />
            <input
              type="text"
              placeholder={t('data.customReports.searchPlaceholder')}
              value={search}
              onChange={(e) => handleSearchChange(e.target.value)}
              className="flex-1 px-2 py-1 text-sm bg-transparent border-0 focus:outline-none text-slate-700 dark:text-slate-200 placeholder-slate-400 min-w-0"
            />
          </div>
          {tab !== 'trash' && (
            <>
              <select
                value={category}
                onChange={(e) => {
                  setCategory(e.target.value);
                  setPage(1);
                }}
                className="px-3 py-2 text-sm border border-slate-200 dark:border-slate-600 rounded-lg bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-blue-500/20"
              >
                {CATEGORY_OPTIONS.map((o) => (
                  <option key={o.value} value={o.value}>
                    {t(o.labelKey)}
                  </option>
                ))}
              </select>
              <select
                value={visibility}
                onChange={(e) => {
                  setVisibility(e.target.value);
                  setPage(1);
                }}
                className="px-3 py-2 text-sm border border-slate-200 dark:border-slate-600 rounded-lg bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-blue-500/20"
              >
                {VISIBILITY_OPTIONS.map((o) => (
                  <option key={o.value} value={o.value}>
                    {t(o.labelKey)}
                  </option>
                ))}
              </select>
            </>
          )}
        </div>
      </div>

      <div className="flex items-center justify-between border-b border-slate-200 dark:border-slate-700">
        <div className="flex items-center gap-1">
          <button
            onClick={() => {
              setTab('all');
              setPage(1);
            }}
            className={`flex items-center gap-2 px-4 py-2.5 text-sm font-medium border-b-2 transition-all ${
              tab === 'all'
                ? 'border-indigo-500 text-indigo-600 dark:text-indigo-400'
                : 'border-transparent text-slate-500 hover:text-slate-700 dark:hover:text-slate-300'
            }`}
          >
            <Grid3X3 size={16} />
            {t('data.common.all')}
          </button>
          <button
            onClick={() => {
              setTab('pinned');
              setPage(1);
            }}
            className={`flex items-center gap-2 px-4 py-2.5 text-sm font-medium border-b-2 transition-all ${
              tab === 'pinned'
                ? 'border-indigo-500 text-indigo-600 dark:text-indigo-400'
                : 'border-transparent text-slate-500 hover:text-slate-700 dark:hover:text-slate-300'
            }`}
          >
            <Pin size={16} />
            {t('data.customReports.tab.pinned')}
          </button>
          <button
            onClick={() => {
              setTab('trash');
              setPage(1);
            }}
            className={`flex items-center gap-2 px-4 py-2.5 text-sm font-medium border-b-2 transition-all ${
              tab === 'trash'
                ? 'border-rose-500 text-rose-600 dark:text-rose-400'
                : 'border-transparent text-slate-500 hover:text-slate-700 dark:hover:text-slate-300'
            }`}
          >
            <Trash2 size={16} />
            {t('data.customReports.tab.trash')}
            {trashTotalItems > 0 && (
              <span className="ml-1 inline-flex items-center justify-center h-4 min-w-[16px] px-1 text-xs rounded-full bg-rose-100 text-rose-600 dark:bg-rose-900/40 dark:text-rose-400 font-medium">
                {trashTotalItems}
              </span>
            )}
          </button>
        </div>
        <div className="px-4 text-xs text-slate-400">
          {t('data.customReports.totalCount', { count: displayTotalItems })}
        </div>
      </div>

      {displayLoading ? (
        <div className="p-12 text-center text-slate-500 dark:text-slate-400">
          <div className="flex items-center justify-center gap-2">
            <RefreshCw className="w-5 h-5 animate-spin" />
            <span>{t('common.shell.loading')}</span>
          </div>
        </div>
      ) : displayReports.length === 0 ? (
        <div className="p-16 text-center bg-white dark:bg-slate-800 rounded-2xl border border-slate-100 dark:border-slate-700">
          {tab === 'trash' ? (
            <>
              <div className="w-24 h-24 rounded-2xl bg-gradient-to-br from-rose-50 to-amber-50 dark:from-rose-900/20 dark:to-amber-900/20 flex items-center justify-center mx-auto mb-4">
                <Trash2 size={40} className="text-rose-400" />
              </div>
              <h3 className="text-lg font-medium text-slate-700 dark:text-slate-300 mb-2">{t('data.customReports.empty.trashTitle')}</h3>
              <p className="text-sm text-slate-500 dark:text-slate-400">
                {t('data.customReports.empty.trashDescription')}
              </p>
            </>
          ) : (
            <>
              <div className="w-24 h-24 rounded-2xl bg-gradient-to-br from-indigo-50 to-violet-50 dark:from-indigo-900/20 dark:to-violet-900/20 flex items-center justify-center mx-auto mb-4">
                <BarChart3 size={40} className="text-indigo-400" />
              </div>
              <h3 className="text-lg font-medium text-slate-700 dark:text-slate-300 mb-2">
                {tab === 'pinned' ? t('data.customReports.empty.pinnedTitle') : t('data.customReports.empty.allTitle')}
              </h3>
              <p className="text-sm text-slate-500 dark:text-slate-400 mb-4">
                {tab === 'pinned' ? t('data.customReports.empty.pinnedDescription') : t('data.customReports.empty.allDescription')}
              </p>
              {tab === 'all' && (
                <button
                  onClick={() => navigate('/modules/custom-reports/new')}
                  className="inline-flex items-center gap-2 px-4 py-2.5 text-sm font-medium text-white bg-gradient-to-r from-indigo-600 to-violet-600 hover:from-indigo-700 hover:to-violet-700 rounded-lg transition-all shadow-sm"
                >
                  <Plus size={16} />
                  {t('data.customReports.action.createReport')}
                </button>
              )}
            </>
          )}
        </div>
      ) : (
        <div className="bg-white dark:bg-slate-800 rounded-xl border border-slate-100 dark:border-slate-700/50 shadow-sm overflow-visible">
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4 p-4">
            {displayReports.map((r) => {
              const vis = VISIBILITY_LABELS[r.visibility];
              // 码不可识别时取不到键 → 回落渲染原始 code（与原实现一致）
              const categoryKey = CATEGORY_LABEL_KEYS[r.category];
              const open = openMenuId === r.id;
              const isTrash = tab === 'trash';
              return (
                <div
                  key={r.id}
                  className={`group relative bg-white dark:bg-slate-800 rounded-xl border p-5 transition-all ${
                    isTrash
                      ? 'border-slate-200 dark:border-slate-700/50 opacity-80'
                      : 'border-slate-200 dark:border-slate-700/50 hover:shadow-md hover:border-indigo-200 dark:hover:border-indigo-700/50'
                  }`}
                >
                  <div className="flex items-start justify-between gap-2 mb-3">
                    <div className={`w-10 h-10 rounded-xl flex items-center justify-center flex-shrink-0 ${
                      isTrash
                        ? 'bg-gradient-to-br from-slate-100 to-slate-200 dark:from-slate-700/60 dark:to-slate-800/60'
                        : 'bg-gradient-to-br from-indigo-100 to-violet-100 dark:from-indigo-900/40 dark:to-violet-900/40'
                    }`}>
                      <BarChart3 className={`w-5 h-5 ${isTrash ? 'text-slate-500 dark:text-slate-400' : 'text-indigo-600 dark:text-indigo-400'}`} />
                    </div>
                    <div className="flex items-center gap-1">
                      {!isTrash && (
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            handleTogglePin(r);
                          }}
                          title={r.isDashboardPinned ? t('data.customReports.action.unpin') : t('data.customReports.action.pin')}
                          className={`p-1.5 rounded-lg transition-colors ${
                            r.isDashboardPinned
                              ? 'text-amber-500 bg-amber-50 dark:bg-amber-900/30'
                              : 'text-slate-400 hover:text-amber-500 hover:bg-amber-50 dark:hover:bg-amber-900/20'
                          }`}
                        >
                          {r.isDashboardPinned ? <Pin size={16} /> : <PinOff size={16} />}
                        </button>
                      )}
                      <div onClick={(e) => e.stopPropagation()}>
                        <button
                          onClick={(e) => {
                            e.stopPropagation();
                            if (open) {
                              setOpenMenuId(null);
                              setMenuPosition(null);
                            } else {
                              openReportMenu(r, e.currentTarget);
                              setOpenMenuId(r.id);
                            }
                          }}
                          className={`p-1.5 rounded-lg transition-colors ${
                            open
                              ? 'text-slate-700 bg-slate-100 dark:text-slate-200 dark:bg-slate-700'
                              : 'text-slate-400 hover:text-slate-600 hover:bg-slate-100 dark:hover:text-slate-300 dark:hover:bg-slate-700'
                          }`}
                          aria-haspopup="menu"
                          aria-expanded={open}
                        >
                          <MoreHorizontal size={16} />
                        </button>
                      </div>
                    </div>
                  </div>

                  <h3 className={`font-semibold truncate mb-1.5 ${isTrash ? 'text-slate-600 dark:text-slate-400' : 'text-slate-800 dark:text-slate-100 cursor-pointer hover:text-indigo-600 dark:hover:text-indigo-400 transition-colors'}`} onClick={() => !isTrash && navigate(`/modules/custom-reports/view/${r.id}`)}>
                    {r.name}
                  </h3>
                  {r.description && (
                    <p className="text-xs text-slate-500 dark:text-slate-400 line-clamp-2 mb-3 min-h-[2rem]">
                      {r.description}
                    </p>
                  )}

                  <div className="flex flex-wrap items-center gap-1.5 mb-3">
                    <span className={`inline-flex items-center gap-1 px-2 py-0.5 text-xs rounded-full font-medium ${CATEGORY_COLORS[r.category] || ''}`}>
                      {categoryKey ? t(categoryKey) : r.category}
                    </span>
                    <span className={`inline-flex items-center gap-1 px-2 py-0.5 text-xs rounded-full font-medium ${vis.color}`}>
                      <span>{vis.icon}</span>
                      {t(vis.labelKey)}
                    </span>
                    {isTrash && (
                      <span className="inline-flex items-center gap-1 px-2 py-0.5 text-xs rounded-full font-medium bg-rose-50 text-rose-600 dark:bg-rose-900/30 dark:text-rose-400 ring-1 ring-rose-200/60 dark:ring-rose-700/40">
                        <Trash2 size={11} />
                        {t('data.customReports.deletedBadge')}
                      </span>
                    )}
                  </div>

                  <div className="flex items-center justify-between pt-3 border-t border-slate-100 dark:border-slate-700/50 text-xs">
                    <div className="text-slate-500 dark:text-slate-400 flex items-center gap-1.5 min-w-0">
                      <span className="truncate">{r.ownerName || '-'}</span>
                    </div>
                    <div className="text-slate-400 dark:text-slate-500 flex-shrink-0 ml-2">
                      {isTrash && r.deletedAt ? (
                        <span title={t('data.customReports.deletedAt')}>{formatDateTime(r.deletedAt)}</span>
                      ) : (
                        formatDateTime(r.updatedAt)
                      )}
                    </div>
                  </div>

                  <div className="flex items-center gap-1.5 mt-3">
                    {isTrash ? (
                      <>
                        <button
                          onClick={() => setRestoreTarget(r)}
                          className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-medium text-white bg-emerald-600 hover:bg-emerald-700 rounded-lg transition-colors"
                        >
                          <Undo2 size={14} />
                          {t('data.customReports.action.restore')}
                        </button>
                        <button
                          onClick={() => setHardDeleteTarget(r)}
                          className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-medium text-white bg-rose-600 hover:bg-rose-700 rounded-lg transition-colors"
                        >
                          <Trash2 size={14} />
                          {t('data.customReports.action.hardDelete')}
                        </button>
                      </>
                    ) : (
                      <>
                        <button
                          onClick={() => navigate(`/modules/custom-reports/view/${r.id}`)}
                          className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-medium text-slate-700 dark:text-slate-300 bg-slate-100 dark:bg-slate-700/50 hover:bg-slate-200 dark:hover:bg-slate-700 rounded-lg transition-colors"
                        >
                          <Eye size={14} />
                          {t('data.customReports.action.view')}
                        </button>
                        {r.canEdit && (
                          <button
                            onClick={() => navigate(`/modules/custom-reports/edit/${r.id}`)}
                            className="flex-1 inline-flex items-center justify-center gap-1.5 px-3 py-2 text-xs font-medium text-white bg-indigo-600 hover:bg-indigo-700 rounded-lg transition-colors"
                          >
                            <Edit2 size={14} />
                            {t('common.edit')}
                          </button>
                        )}
                      </>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
          {tab !== 'pinned' && (
            <Pagination
              currentPage={page}
              totalPages={displayTotalPages}
              totalItems={displayTotalItems}
              pageSize={pageSize}
              onPageChange={(p) => setPage(p)}
            />
          )}
        </div>
      )}

      {/* 操作菜单 Portal 渲染到 body，脱离祖先 overflow 裁剪与 stacking context 限制 */}
      {openMenuId !== null && menuPosition && (() => {
        const r = displayReports.find((x) => x.id === openMenuId);
        if (!r) return null;
        const isTrash = tab === 'trash';
        const style: React.CSSProperties = {
          position: 'fixed',
          left: menuPosition.left,
          width: MENU_WIDTH,
          zIndex: 9999,
        };
        if (menuPosition.upward && menuPosition.bottom != null) {
          style.bottom = menuPosition.bottom;
        } else {
          style.top = menuPosition.top;
        }
        return createPortal(
          <div
            onClick={(e) => e.stopPropagation()}
            style={style}
            className="w-48 bg-white dark:bg-slate-800 rounded-xl shadow-2xl ring-1 ring-black/5 dark:ring-white/5 border border-slate-100 dark:border-slate-700 py-1.5 origin-top-right animate-in fade-in zoom-in-95"
          >
            {isTrash ? (
              <>
                <button
                  onClick={() => {
                    setOpenMenuId(null);
                    setMenuPosition(null);
                    setRestoreTarget(r);
                  }}
                  className="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-emerald-600 dark:text-emerald-400 hover:bg-emerald-50 dark:hover:bg-emerald-900/30 transition-colors"
                >
                  <Undo2 size={15} />
                  {t('data.customReports.action.restoreReport')}
                </button>
                <div className="border-t border-slate-100 dark:border-slate-700 my-1" />
                <button
                  onClick={() => {
                    setOpenMenuId(null);
                    setMenuPosition(null);
                    setHardDeleteTarget(r);
                  }}
                  className="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-rose-600 dark:text-rose-400 hover:bg-rose-50 dark:hover:bg-rose-900/30 transition-colors"
                >
                  <Trash2 size={15} />
                  {t('data.customReports.action.hardDelete')}
                </button>
              </>
            ) : (
              <>
                <button
                  onClick={() => {
                    setOpenMenuId(null);
                    setMenuPosition(null);
                    navigate(`/modules/custom-reports/view/${r.id}`);
                  }}
                  className="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700/60 transition-colors"
                >
                  <Eye size={15} />
                  {t('data.customReports.action.viewReport')}
                </button>
                {r.canEdit && (
                  <button
                    onClick={() => {
                      setOpenMenuId(null);
                      setMenuPosition(null);
                      navigate(`/modules/custom-reports/edit/${r.id}`);
                    }}
                    className="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700/60 transition-colors"
                  >
                    <Edit2 size={15} />
                    {t('data.customReports.action.editConfig')}
                  </button>
                )}
                <button
                  onClick={() => {
                    setOpenMenuId(null);
                    setMenuPosition(null);
                    setScheduleModal({ open: true, reportId: r.id });
                  }}
                  className="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700/60 transition-colors"
                >
                  <Calendar size={15} />
                  {t('data.customReports.action.schedule')}
                </button>
                <div className="border-t border-slate-100 dark:border-slate-700 my-1" />
                <div className="px-3 py-1.5 text-xs text-slate-500 dark:text-slate-400 font-medium">{t('data.customReports.action.exportAs')}</div>
                <button
                  onClick={() => {
                    setOpenMenuId(null);
                    setMenuPosition(null);
                    handleExport(r.id, 'csv');
                  }}
                  className="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700/60 transition-colors"
                >
                  <FileText size={15} className="text-emerald-500" />
                  {t('data.customReports.action.exportAsCsv')}
                </button>
                <button
                  onClick={() => {
                    setOpenMenuId(null);
                    setMenuPosition(null);
                    handleExport(r.id, 'xlsx');
                  }}
                  className="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700/60 transition-colors"
                >
                  <FileSpreadsheet size={15} className="text-blue-500" />
                  {t('data.customReports.action.exportAsExcel')}
                </button>
                <button
                  onClick={() => {
                    setOpenMenuId(null);
                    setMenuPosition(null);
                    handleExport(r.id, 'pdf');
                  }}
                  className="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700/60 transition-colors"
                >
                  <File size={15} className="text-rose-500" />
                  {t('data.customReports.action.exportAsPdf')}
                </button>
                {r.canDelete && (
                  <>
                    <div className="border-t border-slate-100 dark:border-slate-700 my-1" />
                    <button
                      onClick={() => {
                        setOpenMenuId(null);
                        setMenuPosition(null);
                        setDeleteTarget(r);
                      }}
                      className="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-rose-600 dark:text-rose-400 hover:bg-rose-50 dark:hover:bg-rose-900/30 transition-colors"
                    >
                      <Trash2 size={15} />
                      {t('data.customReports.action.deleteReport')}
                    </button>
                  </>
                )}
              </>
            )}
          </div>,
          document.body
        );
      })()}

      <ConfirmDialog
        open={!!deleteTarget}
        title={t('data.customReports.action.deleteReport')}
        message={t('data.customReports.dialog.deleteMessage', { name: deleteTarget?.name ?? '' })}
        confirmText={t('common.delete')}
        cancelText={t('common.cancel')}
        variant="danger"
        loading={deleting}
        onConfirm={handleDelete}
        onCancel={() => setDeleteTarget(null)}
      />

      <ConfirmDialog
        open={!!restoreTarget}
        title={t('data.customReports.action.restoreReport')}
        message={t('data.customReports.dialog.restoreMessage', { name: restoreTarget?.name ?? '' })}
        confirmText={t('data.customReports.action.restore')}
        cancelText={t('common.cancel')}
        variant="primary"
        loading={restoring}
        onConfirm={handleRestore}
        onCancel={() => setRestoreTarget(null)}
      />

      <ConfirmDialog
        open={!!hardDeleteTarget}
        title={t('data.customReports.action.hardDeleteReport')}
        message={t('data.customReports.dialog.hardDeleteMessage', { name: hardDeleteTarget?.name ?? '' })}
        confirmText={t('data.customReports.action.hardDelete')}
        cancelText={t('common.cancel')}
        variant="danger"
        loading={hardDeleting}
        onConfirm={handleHardDelete}
        onCancel={() => setHardDeleteTarget(null)}
      >
        <div className="mt-2 flex items-start gap-2 p-3 rounded-lg bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800/40">
          <AlertTriangle size={18} className="text-amber-500 flex-shrink-0 mt-0.5" />
          <p className="text-xs text-amber-700 dark:text-amber-400">
            {t('data.customReports.dialog.hardDeleteNote')}
          </p>
        </div>
      </ConfirmDialog>

      <ConfirmDialog
        open={confirmingClearTrash}
        title={t('data.customReports.action.clearTrash')}
        message={t('data.customReports.dialog.clearTrashMessage', { count: trashTotalItems })}
        confirmText={t('data.customReports.action.clearAll')}
        cancelText={t('common.cancel')}
        variant="danger"
        loading={clearingTrash}
        onConfirm={handleClearTrash}
        onCancel={() => setConfirmingClearTrash(false)}
      >
        <div className="mt-2 flex items-start gap-2 p-3 rounded-lg bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800/40">
          <AlertTriangle size={18} className="text-amber-500 flex-shrink-0 mt-0.5" />
          <p className="text-xs text-amber-700 dark:text-amber-400">
            {t('data.customReports.dialog.clearTrashNote')}
          </p>
        </div>
      </ConfirmDialog>

      <CustomReportScheduleModal
        isOpen={scheduleModal.open}
        onClose={() => setScheduleModal({ open: false, reportId: null })}
        reportId={scheduleModal.reportId}
      />
    </div>
  );
}
