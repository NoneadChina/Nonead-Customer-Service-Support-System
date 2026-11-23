import { useEffect, useCallback, useRef, memo } from 'react';
import { useCalendarStore } from '../store';
import { formatDateTime } from '../calendarUtils';
import { getEventTypeStyle, getStatusStyle, _timegridBackgroundColor } from '../chips';
import type { CalendarEventDto } from '../../../api/types';
import { useI18n } from '../../../context/I18nContext';
import { Icon, ConfirmDialog, shallowEq, getPageList, PAGE_SIZE } from '../panels/shared';

/* =========================================================
   TrashView —— 垃圾箱页面视图（替代原 Overlay 弹窗）
   功能：搜索/分页/恢复/永久删除/清空/按期清理
   主题：红/橙渐变装饰条（危险操作警示色）
   ========================================================= */

const TrashRow = memo(({ ev, onRestore, onHardDel }: { ev: CalendarEventDto; onRestore: (ev: CalendarEventDto) => void; onHardDel: (ev: CalendarEventDto) => void }) => {
  // 修复"偶发日历颜色显示全部变灰"BUG：统一使用大小写不敏感查找 + 事件类型默认色兜底（不再使用灰色）
  // 事件类型样式原名 t，改名以把 t 让给 useI18n（局部名遮蔽翻译函数会让文案永远取不到词条）
  const { t } = useI18n();
  const typeStyle = getEventTypeStyle(ev.eventType);
  const st = getStatusStyle(ev.status);
  const dot = _timegridBackgroundColor(ev);
  const handleRestore = useCallback(() => onRestore(ev), [ev, onRestore]);
  const handleHardDel = useCallback(() => onHardDel(ev), [ev, onHardDel]);
  return (
    <tr className="group hover:bg-rose-50/40 dark:hover:bg-rose-950/20 transition-colors duration-150 border-l-4" style={{ borderLeftColor: dot }}>
      <td className="px-4 py-3">
        <div className="flex items-start gap-3">
          <div className="w-2.5 h-2.5 rounded-full mt-1.5 flex-shrink-0 shadow-sm ring-1 ring-white/40" style={{ backgroundColor: dot }} />
          <div className="min-w-0 flex-1">
            <div className="text-sm font-semibold text-slate-700 dark:text-slate-300 truncate line-through decoration-slate-400/70 dark:decoration-slate-500/70">{ev.title}</div>
            {ev.location && (
              <div className="text-xs text-slate-500 dark:text-slate-400 truncate max-w-xs mt-0.5 flex items-center gap-1">
                <Icon name="tag" className="w-3 h-3 opacity-60" />
                {ev.location}
              </div>
            )}
          </div>
        </div>
      </td>
      <td className="px-4 py-3 whitespace-nowrap"><span className={`inline-flex items-center px-2.5 py-1 rounded-full text-xs font-semibold shadow-sm ring-1 ring-white/40 ${typeStyle.badgeClass}`}>{typeStyle.label}</span></td>
      <td className="px-4 py-3 whitespace-nowrap"><span className={`inline-flex items-center px-2.5 py-1 rounded-full text-xs font-semibold shadow-sm ring-1 ring-white/40 ${st.badgeClass}`}>{st.label}</span></td>
      <td className="px-4 py-3 whitespace-nowrap">
        <div className="flex items-center gap-1.5 text-sm text-slate-600 dark:text-slate-300">
          <Icon name="clock" className="w-3.5 h-3.5 text-slate-400" />
          <span>{formatDateTime(ev.deletedAt || ev.updatedAt)}</span>
        </div>
      </td>
      <td className="px-4 py-3 whitespace-nowrap text-right">
        <div className="flex items-center justify-end gap-1.5 opacity-70 group-hover:opacity-100 transition-opacity">
          <button onClick={handleRestore} className="inline-flex items-center gap-1 px-2.5 py-1.5 text-xs font-semibold text-emerald-700 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-900/20 hover:bg-emerald-100 dark:hover:bg-emerald-900/40 rounded-lg border border-emerald-100 dark:border-emerald-800/40 transition-colors shadow-sm">
            <Icon name="rotate" className="w-3 h-3" />{t('calendar.trash.restore')}
          </button>
          <button onClick={handleHardDel} className="inline-flex items-center gap-1 px-2.5 py-1.5 text-xs font-semibold text-rose-700 dark:text-rose-400 bg-rose-50 dark:bg-rose-900/20 hover:bg-rose-100 dark:hover:bg-rose-900/40 rounded-lg border border-rose-100 dark:border-rose-800/40 transition-colors shadow-sm">
            <Icon name="trash" className="w-3 h-3" />{t('calendar.trash.hardDelete')}
          </button>
        </div>
      </td>
    </tr>
  );
});
TrashRow.displayName = 'TrashRow';

const TrashMobileCard = memo(({ ev, onRestore, onHardDel }: { ev: CalendarEventDto; onRestore: (ev: CalendarEventDto) => void; onHardDel: (ev: CalendarEventDto) => void }) => {
  // 事件类型样式原名 t，改名以把 t 让给 useI18n（局部名遮蔽翻译函数会让文案永远取不到词条）
  const { t } = useI18n();
  const typeStyle = getEventTypeStyle(ev.eventType);
  const st = getStatusStyle(ev.status);
  const dot = _timegridBackgroundColor(ev);
  const handleRestore = useCallback(() => onRestore(ev), [ev, onRestore]);
  const handleHardDel = useCallback(() => onHardDel(ev), [ev, onHardDel]);
  return (
    <div className="p-3.5 hover:bg-rose-50/40 dark:hover:bg-rose-950/20 transition-colors duration-150 border-l-4" style={{ borderLeftColor: dot }}>
      <div className="flex items-center gap-2 min-w-0 mb-2">
        <div className="w-2.5 h-2.5 rounded-full flex-shrink-0 shadow-sm ring-1 ring-white/40" style={{ backgroundColor: dot }} />
        <span className="text-sm font-semibold text-slate-700 dark:text-slate-300 truncate line-through decoration-slate-400/70 dark:decoration-slate-500/70">{ev.title}</span>
      </div>
      {ev.location && (
        <div className="text-xs text-slate-500 dark:text-slate-400 truncate flex items-center gap-1 mb-2">
          <Icon name="tag" className="w-3 h-3 opacity-60 flex-shrink-0" />
          {ev.location}
        </div>
      )}
      <div className="flex items-center gap-1.5 mb-2 flex-wrap">
        <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-semibold shadow-sm ring-1 ring-white/40 ${typeStyle.badgeClass}`}>{typeStyle.label}</span>
        <span className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-semibold shadow-sm ring-1 ring-white/40 ${st.badgeClass}`}>{st.label}</span>
      </div>
      <div className="flex items-center gap-1.5 text-xs text-slate-600 dark:text-slate-300 mb-2.5">
        <Icon name="clock" className="w-3 h-3 text-slate-400 flex-shrink-0" />
        <span>{formatDateTime(ev.deletedAt || ev.updatedAt)}</span>
      </div>
      <div className="flex items-center gap-2">
        <button onClick={handleRestore} className="flex-1 inline-flex items-center justify-center gap-1 px-2.5 py-1.5 text-xs font-semibold text-emerald-700 dark:text-emerald-400 bg-emerald-50 dark:bg-emerald-900/20 hover:bg-emerald-100 dark:hover:bg-emerald-900/40 rounded-lg border border-emerald-100 dark:border-emerald-800/40 transition-colors shadow-sm">
          <Icon name="rotate" className="w-3 h-3" />{t('calendar.trash.restore')}
        </button>
        <button onClick={handleHardDel} className="flex-1 inline-flex items-center justify-center gap-1 px-2.5 py-1.5 text-xs font-semibold text-rose-700 dark:text-rose-400 bg-rose-50 dark:bg-rose-900/20 hover:bg-rose-100 dark:hover:bg-rose-900/40 rounded-lg border border-rose-100 dark:border-rose-800/40 transition-colors shadow-sm">
          <Icon name="trash" className="w-3 h-3" />{t('calendar.trash.hardDelete')}
        </button>
      </div>
    </div>
  );
});
TrashMobileCard.displayName = 'TrashMobileCard';

const PageBtn = memo(function PageBtn({ page, isCurrent, isDisabled, onClick }: { page: number; isCurrent: boolean; isDisabled: boolean; onClick: (page: number) => void }) {
  const handle = useCallback(() => onClick(page), [page, onClick]);
  return (
    <button
      type="button"
      onClick={handle}
      disabled={isDisabled}
      className={[
        'min-w-[34px] h-8 px-2 text-sm font-semibold rounded-xl tabular-nums transition-all disabled:opacity-50',
        isCurrent
          ? 'bg-gradient-to-br from-rose-500 to-orange-500 text-white shadow-md shadow-rose-500/30 ring-2 ring-rose-500/20 scale-105'
          : isDisabled
            ? 'text-slate-400 dark:text-slate-500 cursor-not-allowed'
            : 'text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-700 hover:shadow-sm',
      ].join(' ')}
    >
      {page}
    </button>
  );
});
PageBtn.displayName = 'PageBtn';

export function TrashView() {
  const s = useCalendarStore((st) => ({
    trashLoading: st.trashLoading, trashKeyword: st.trashKeyword, trashPage: st.trashPage,
    trashList: st.trashList, trashCount: st.trashCount,
    restoreTarget: st.restoreTarget, restoring: st.restoring,
    hardDeleteTarget: st.hardDeleteTarget, hardDeleting: st.hardDeleting,
    showClearTrashConfirm: st.showClearTrashConfirm, clearingTrash: st.clearingTrash,
    showPurgeTrashConfirm: st.showPurgeTrashConfirm, purgeRetentionDays: st.purgeRetentionDays,
    purgingTrash: st.purgingTrash,
    setTrashFilter: st.setTrashFilter,
    setRestoreTarget: st.setRestoreTarget, setHardDeleteTarget: st.setHardDeleteTarget,
    setPurgeDialog: st.setPurgeDialog,
    loadTrashAsync: st.loadTrashAsync,
    restoreAsync: st.restoreAsync, hardDeleteAsync: st.hardDeleteAsync,
    clearTrashAsync: st.clearTrashAsync, purgeTrashAsync: st.purgeTrashAsync,
  }), shallowEq);

  const { t } = useI18n();

  const timersRef = useRef<number[]>([]);
  const debRef = useRef<number | null>(null);

  // 首次挂载 + 切到此视图时加载数据
  useEffect(() => {
    s.loadTrashAsync();
    return () => {
      for (const t of timersRef.current) window.clearTimeout(t);
      timersRef.current = [];
      if (debRef.current) window.clearTimeout(debRef.current);
      debRef.current = null;
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const onKw = useCallback((v: string) => {
    if (debRef.current) window.clearTimeout(debRef.current);
    debRef.current = window.setTimeout(() => s.setTrashFilter(v, 1), 250);
  }, [s]);

  const onClearTrash = useCallback(() => useCalendarStore.setState({ showClearTrashConfirm: true }), []);
  const onPurgeDefault = useCallback(() => s.setPurgeDialog(true, 30), [s]);

  const onRetentionDaysChange = useCallback((v: number) => {
    useCalendarStore.setState({ purgeRetentionDays: isNaN(v) ? 0 : Math.max(0, Math.min(3650, v)) });
  }, []);

  const onRestoreAll = useCallback((ev: CalendarEventDto) => s.setRestoreTarget(ev), [s]);
  const onHardDelAll = useCallback((ev: CalendarEventDto) => s.setHardDeleteTarget(ev), [s]);

  const onPrevPage = useCallback(() => s.setTrashFilter(s.trashKeyword, s.trashPage - 1), [s]);
  const onNextPage = useCallback(() => s.setTrashFilter(s.trashKeyword, s.trashPage + 1), [s]);
  const onGotoPage = useCallback((p: number) => s.setTrashFilter(s.trashKeyword, p), [s]);

  const onCancelRestore = useCallback(() => s.setRestoreTarget(null), [s]);
  const onCancelHardDel = useCallback(() => s.setHardDeleteTarget(null), [s]);
  const onCancelClear = useCallback(() => useCalendarStore.setState({ showClearTrashConfirm: false }), []);
  const onCancelPurge = useCallback(() => s.setPurgeDialog(false), [s]);

  const doRestore = useCallback(async () => {
    if (!s.restoreTarget?.id) return;
    try {
      await s.restoreAsync(s.restoreTarget.id);
    } catch {
      // store 已记录 formError
    }
  }, [s]);

  const doHardDel = useCallback(async () => {
    if (!s.hardDeleteTarget?.id) return;
    try {
      await s.hardDeleteAsync(s.hardDeleteTarget.id);
    } catch {
      // store 已记录 formError
    }
  }, [s]);

  const doClear = useCallback(async () => {
    try {
      await s.clearTrashAsync();
    } catch {
      // store 已记录 formError
    }
  }, [s]);

  const doPurge = useCallback(async () => {
    try {
      await s.purgeTrashAsync();
    } catch {
      // store 已记录 formError
    }
  }, [s]);

  const totalPages = Math.max(1, Math.ceil(s.trashCount / PAGE_SIZE));

  return (
    <>
      <div className="flex flex-col h-full min-h-0 p-4 sm:p-6 space-y-4 sm:space-y-6">
        {/* 页头 */}
        <div className="flex items-start justify-between gap-4 flex-wrap">
          <div>
            <div className="flex items-center gap-3">
              <div className="w-10 h-10 rounded-2xl bg-gradient-to-br from-rose-100 via-orange-50 to-amber-100 dark:from-rose-900/40 dark:via-orange-900/20 dark:to-amber-900/30 flex items-center justify-center ring-1 ring-rose-200/50 dark:ring-rose-800/40 shadow-sm">
                <Icon name="folderTrash" className="w-5 h-5 text-rose-500 dark:text-rose-300" />
              </div>
              <div>
                <h2 className="text-lg sm:text-xl font-bold text-slate-800 dark:text-slate-100 tracking-tight">{t('calendar.common.trash')}</h2>
                <p className="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
                  {t('calendar.trash.countPrefix')}<span className="font-semibold text-slate-700 dark:text-slate-200">{s.trashCount}</span>{t('calendar.trash.viewHeaderMiddle')}<span className="font-semibold">{s.purgeRetentionDays}</span>{t('calendar.trash.viewHeaderSuffix')}
                </p>
              </div>
            </div>
          </div>
        </div>

        {/* 工具栏：搜索 + 批量操作 */}
        <div className="flex flex-wrap items-center gap-3 p-3 rounded-2xl bg-gradient-to-r from-rose-50/60 via-white to-orange-50/50 dark:from-rose-950/20 dark:via-slate-800/30 dark:to-orange-950/20 border border-rose-100/60 dark:border-rose-900/30 shadow-sm">
          <div className="relative w-full sm:flex-1 sm:min-w-[220px] sm:max-w-md">
            <div className="absolute inset-y-0 left-0 flex items-center pl-3 pointer-events-none">
              <div className="w-7 h-7 rounded-lg bg-gradient-to-br from-rose-100 to-orange-100 dark:from-rose-900/40 dark:to-orange-900/30 flex items-center justify-center">
                <Icon name="search" className="w-3.5 h-3.5 text-rose-500 dark:text-rose-300" />
              </div>
            </div>
            <input
              type="text"
              defaultValue={s.trashKeyword}
              onChange={(e) => onKw(e.target.value)}
              placeholder={t('calendar.trash.searchPlaceholder')}
              className="w-full pl-12 pr-3 py-2.5 text-sm border border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-900/60 text-slate-700 dark:text-slate-200 rounded-xl focus:outline-none focus:ring-2 focus:ring-rose-500/30 focus:border-rose-400 transition-all shadow-sm"
            />
          </div>
          <div className="hidden sm:inline-flex items-center gap-1.5 px-3 py-1.5 text-xs rounded-xl bg-slate-100/80 dark:bg-slate-700/60 text-slate-500 dark:text-slate-400 border border-slate-200 dark:border-slate-600">
            <Icon name="filter" className="w-3.5 h-3.5" />
            <span>{t('calendar.trash.filterHint')}</span>
          </div>
          <div className="flex items-center gap-2 w-full sm:w-auto sm:ml-auto">
            <button
              onClick={onClearTrash}
              disabled={s.trashCount === 0 || s.clearingTrash}
              className="flex-1 sm:flex-none inline-flex items-center justify-center gap-1.5 px-4 py-2.5 text-xs sm:text-sm font-semibold text-white bg-gradient-to-r from-red-500 to-rose-600 hover:from-red-600 hover:to-rose-700 rounded-xl disabled:opacity-50 disabled:cursor-not-allowed shadow-lg shadow-red-500/25 hover:shadow-xl hover:shadow-red-500/30 transition-all"
            >
              {s.clearingTrash ? <Icon name="spinner" className="w-4 h-4 animate-spin" /> : <Icon name="trash" className="w-4 h-4" />}
              {t('calendar.trash.clearTrash')}
            </button>
            <button
              onClick={onPurgeDefault}
              disabled={s.trashCount === 0 || s.purgingTrash}
              className="flex-1 sm:flex-none inline-flex items-center justify-center gap-1.5 px-4 py-2.5 text-xs sm:text-sm font-semibold text-white bg-gradient-to-r from-orange-500 to-amber-500 hover:from-orange-600 hover:to-amber-600 rounded-xl disabled:opacity-50 disabled:cursor-not-allowed shadow-lg shadow-orange-500/25 hover:shadow-xl hover:shadow-orange-500/30 transition-all"
            >
              {s.purgingTrash ? <Icon name="spinner" className="w-4 h-4 animate-spin" /> : <Icon name="sparkles" className="w-4 h-4" />}
              {t('calendar.trash.purgeExpired')}
            </button>
          </div>
        </div>

        {/* 结果表格卡片 */}
        <div className="flex-1 min-h-0 bg-white dark:bg-slate-800 rounded-2xl border border-slate-200 dark:border-slate-700 shadow-lg shadow-slate-900/5 overflow-hidden flex flex-col">
          <div className="flex-1 min-h-0 overflow-auto">
            {s.trashLoading ? (
              <div className="p-4 sm:p-6 space-y-3">
                {Array.from({ length: 5 }).map((_, i) => (
                  <div key={`ts-${i}`} className="animate-pulse flex items-center gap-3">
                    <div className="w-2.5 h-2.5 rounded-full bg-slate-200 dark:bg-slate-700 flex-shrink-0" />
                    <div className="flex-1 space-y-1.5">
                      <div className="h-3.5 w-2/3 bg-slate-200 dark:bg-slate-700 rounded" />
                      <div className="h-2.5 w-1/3 bg-slate-100 dark:bg-slate-700/60 rounded" />
                    </div>
                    <div className="h-5 w-14 bg-slate-100 dark:bg-slate-700 rounded-full" />
                  </div>
                ))}
              </div>
            ) : s.trashList.length === 0 ? (
              <div className="px-4 py-16 sm:py-20 text-center">
                <div className="flex flex-col items-center gap-4">
                  <div className="relative">
                    <div className="absolute -inset-3 bg-gradient-to-br from-rose-200/40 to-orange-200/40 dark:from-rose-900/20 dark:to-orange-900/20 rounded-full blur-xl" />
                    <div className="relative bg-gradient-to-br from-rose-100 via-orange-50 to-amber-100 dark:from-rose-900/40 dark:via-orange-900/20 dark:to-amber-900/30 rounded-3xl w-24 h-24 flex items-center justify-center ring-4 ring-white dark:ring-slate-800 shadow-xl">
                      <Icon name="folderTrash" className="w-12 h-12 text-rose-500 dark:text-rose-300 opacity-80" />
                    </div>
                  </div>
                  <div>
                    <p className="text-lg font-bold text-slate-700 dark:text-slate-200">{t('calendar.trash.emptyTitle')}</p>
                    <p className="text-sm text-slate-400 dark:text-slate-500 mt-1">{t('calendar.trash.emptyHint')}</p>
                  </div>
                </div>
              </div>
            ) : (
              <>
                {/* PC 表格视图 */}
                <div className="hidden md:block">
                  <table className="min-w-full divide-y divide-slate-200 dark:divide-slate-700">
                    <thead className="bg-gradient-to-r from-slate-50 via-white to-slate-50 dark:from-slate-900/60 dark:via-slate-900 dark:to-slate-900/60 sticky top-0 z-10">
                      <tr>
                        <th className="px-4 py-3 text-left text-xs font-bold text-slate-500 dark:text-slate-400 uppercase tracking-wider">
                          <div className="flex items-center gap-1.5"><Icon name="tag" className="w-3.5 h-3.5" />{t('calendar.trash.colTitleLocation')}</div>
                        </th>
                        <th className="px-4 py-3 text-left text-xs font-bold text-slate-500 dark:text-slate-400 uppercase tracking-wider">
                          <div className="flex items-center gap-1.5"><Icon name="calendar" className="w-3.5 h-3.5" />{t('calendar.common.type')}</div>
                        </th>
                        <th className="px-4 py-3 text-left text-xs font-bold text-slate-500 dark:text-slate-400 uppercase tracking-wider">
                          <div className="flex items-center gap-1.5"><Icon name="shield" className="w-3.5 h-3.5" />{t('calendar.common.status')}</div>
                        </th>
                        <th className="px-4 py-3 text-left text-xs font-bold text-slate-500 dark:text-slate-400 uppercase tracking-wider">
                          <div className="flex items-center gap-1.5"><Icon name="clock" className="w-3.5 h-3.5" />{t('calendar.trash.colDeletedAt')}</div>
                        </th>
                        <th className="px-4 py-3 text-right text-xs font-bold text-slate-500 dark:text-slate-400 uppercase tracking-wider">
                          <div className="flex items-center justify-end gap-1.5"><Icon name="settings" className="w-3.5 h-3.5" />{t('calendar.trash.colActions')}</div>
                        </th>
                      </tr>
                    </thead>
                    <tbody className="bg-white dark:bg-slate-800 divide-y divide-slate-100 dark:divide-slate-700/60">
                      {s.trashList.map((ev) => (
                        <TrashRow key={ev.recurrenceOriginalStart ? `${ev.id}#${ev.recurrenceOriginalStart}` : `${ev.id}`} ev={ev} onRestore={onRestoreAll} onHardDel={onHardDelAll} />
                      ))}
                    </tbody>
                  </table>
                </div>
                {/* 移动端卡片视图 */}
                <div className="md:hidden divide-y divide-gray-100 dark:divide-slate-700/50">
                  {s.trashList.map((ev) => (
                    <TrashMobileCard key={ev.recurrenceOriginalStart ? `${ev.id}#${ev.recurrenceOriginalStart}` : `${ev.id}`} ev={ev} onRestore={onRestoreAll} onHardDel={onHardDelAll} />
                  ))}
                </div>
              </>
            )}
          </div>
          {totalPages > 1 && (
            <div className="px-4 sm:px-5 py-3 sm:py-4 border-t border-slate-100 dark:border-slate-700 flex items-center justify-between gap-3 flex-wrap bg-gradient-to-r from-slate-50/60 to-white dark:from-slate-800/30 dark:to-slate-900/30 flex-shrink-0">
              <div className="text-xs sm:text-sm text-slate-500 dark:text-slate-400">
                {t('calendar.trash.countPrefix')}<span className="font-bold text-slate-700 dark:text-slate-200">{s.trashCount}</span>{t('calendar.trash.paginationMiddle')}<span className="font-semibold">{s.trashPage}</span> / {totalPages}{t('calendar.trash.paginationSuffix')}
              </div>
              <div className="flex items-center gap-1.5">
                <button
                  onClick={onPrevPage}
                  disabled={s.trashPage <= 1 || s.trashLoading}
                  className="inline-flex items-center gap-1 px-3 py-2 text-sm text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-700 rounded-xl disabled:opacity-40 disabled:cursor-not-allowed border border-slate-200 dark:border-slate-600 shadow-sm transition-all"
                >
                  <Icon name="chevronL" className="w-4 h-4" /><span className="hidden sm:inline font-medium">{t('calendar.common.previousPage')}</span>
                </button>
                <div className="flex items-center gap-1 bg-slate-100/40 dark:bg-slate-800/40 rounded-xl p-1 ring-1 ring-slate-200 dark:ring-slate-700">
                  {getPageList(s.trashPage, totalPages).map((p: number | string, idx: number) => p === 'ellipsis' ? (
                    <span key={`te-${idx}`} className="px-2 text-slate-400 dark:text-slate-500 text-sm select-none font-semibold">…</span>
                  ) : (
                    <PageBtn key={p} page={p as number} isCurrent={p === s.trashPage} isDisabled={s.trashLoading} onClick={onGotoPage} />
                  ))}
                </div>
                <button
                  onClick={onNextPage}
                  disabled={s.trashPage >= totalPages || s.trashLoading}
                  className="inline-flex items-center gap-1 px-3 py-2 text-sm text-slate-600 dark:text-slate-300 hover:bg-slate-100 dark:hover:bg-slate-700 rounded-xl disabled:opacity-40 disabled:cursor-not-allowed border border-slate-200 dark:border-slate-600 shadow-sm transition-all"
                >
                  <span className="hidden sm:inline font-medium">{t('calendar.common.nextPage')}</span><Icon name="chevronR" className="w-4 h-4" />
                </button>
              </div>
            </div>
          )}
        </div>
      </div>

      <ConfirmDialog open={!!s.restoreTarget} title={t('calendar.trash.restoreDialogTitle')} msg={t('calendar.trash.restoreConfirmMsg', { title: s.restoreTarget?.title ?? '' })} confirmText={t('calendar.trash.restore')} variant="primary" loading={s.restoring} onConfirm={doRestore} onCancel={onCancelRestore} />
      <ConfirmDialog open={!!s.hardDeleteTarget} title={t('calendar.trash.hardDeleteDialogTitle')} msg={t('calendar.trash.hardDeleteConfirmMsg', { title: s.hardDeleteTarget?.title ?? '' })} confirmText={t('calendar.trash.permanentDelete')} variant="danger" loading={s.hardDeleting} onConfirm={doHardDel} onCancel={onCancelHardDel}>
        <div className="p-3 rounded-lg border border-red-200 bg-red-50 dark:bg-red-900/20 dark:border-red-900/40"><p className="text-xs font-medium text-red-700 dark:text-red-400">{t('calendar.trash.hardDeleteWarn')}</p></div>
      </ConfirmDialog>
      <ConfirmDialog open={s.showClearTrashConfirm} title={t('calendar.trash.clearDialogTitle')} msg={t('calendar.trash.clearConfirmMsg', { count: s.trashCount })} confirmText={t('calendar.trash.confirmClear')} variant="danger" loading={s.clearingTrash} onConfirm={doClear} onCancel={onCancelClear}>
        <div className="p-3 rounded-lg border border-red-200 bg-red-50 dark:bg-red-900/20 dark:border-red-900/40"><p className="text-xs font-medium text-red-700 dark:text-red-400">{t('calendar.trash.clearWarn')}</p></div>
      </ConfirmDialog>
      <ConfirmDialog open={s.showPurgeTrashConfirm} title={t('calendar.trash.purgeDialogTitle')} msg={t('calendar.trash.purgeConfirmMsg')} confirmText={t('calendar.trash.confirmPurge')} variant="danger" loading={s.purgingTrash} onConfirm={doPurge} onCancel={onCancelPurge}>
        <div className="space-y-3">
          <div className="p-3 rounded-lg border border-orange-200 bg-orange-50 dark:bg-orange-900/20 dark:border-orange-900/40"><p className="text-xs font-medium text-orange-700 dark:text-orange-400">{t('calendar.trash.purgeWarn')}</p></div>
          <div className="flex items-center gap-3">
            <label className="text-sm font-medium text-gray-700 dark:text-slate-200 whitespace-nowrap">{t('calendar.trash.retentionLabel')}</label>
            <input type="number" min={0} max={3650} value={s.purgeRetentionDays} onChange={(e) => onRetentionDaysChange(Number(e.target.value))} className="w-28 px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-900 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500" />
            <span className="text-xs text-gray-500 dark:text-slate-400">{t('calendar.trash.retentionUnit')}</span>
          </div>
        </div>
      </ConfirmDialog>
    </>
  );
}
