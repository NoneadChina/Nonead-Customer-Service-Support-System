import { Fragment, useState } from 'react';
import {
  CheckSquare as CheckSquareIcon,
  Square as SquareIcon,
  Tag as TagIcon,
  Trash2 as Trash2Icon,
} from 'lucide-react';
import { EVENT_TYPE_STYLE, STATUS_STYLE, buildEventsByDate } from '../calendarUtils';
import { calendarApi } from '../../../api/modules/engagement';
import type { CalendarBatchOperationRequest } from '../../../api/types';
import { useCalendarStore } from '../store';
import { ConfirmDialog } from '../panels/shared';
import { useI18n } from '../../../context/I18nContext';

/** P1-A#4: 多选批量操作工具栏（完全受控式 Props）。
 *
 *  - variant="compact": 周/天视图（仅"已选N项 + 取消选择 + 多选切换"）
 *  - variant="full":    月/列表视图（额外含 全选/改类型/改状态/删除）
 *
 *  multiSelectMode / onEnter / onExit / onClearSelection 必传，
 *  因为不同视图多选状态维护方式不同（MonthView 曾使用 local state，ListView 使用外部 store），
 *  这里不做任何内部假设。
 */
export type MultiSelectToolbarVariant = 'compact' | 'full';

export interface BatchMultiSelectToolbarProps {
  variant?: MultiSelectToolbarVariant;
  multiSelectMode: boolean;
  selectedCount: number;
  totalVisibleCount?: number;
  onEnterMultiSelect: () => void;
  onExitMultiSelect: () => void;
  onClearSelection: () => void;
  onSelectAllOverride?: () => void;
  className?: string;
}

export function BatchMultiSelectToolbar({
  variant = 'compact',
  multiSelectMode,
  selectedCount,
  totalVisibleCount,
  onEnterMultiSelect,
  onExitMultiSelect,
  onClearSelection,
  onSelectAllOverride,
  className,
}: BatchMultiSelectToolbarProps) {
  const { t } = useI18n();
  const selectedEventIds = useCalendarStore((s) => s.selectedEventIds);
  const hasSelection = selectedCount > 0;

  const [batchLoading, setBatchLoading] = useState(false);
  const [opEventType, setOpEventType] = useState('');
  const [opStatus, setOpStatus] = useState('');
  // P1-C#2: 删除确认对话框（替换原生 confirm）
  const [deleteConfirmOpen, setDeleteConfirmOpen] = useState(false);

  const clearSelectionStore = useCalendarStore((s) => s.clearSelection);

  const runBatch = async (
    op: CalendarBatchOperationRequest['op'],
    extra?: Partial<CalendarBatchOperationRequest>,
  ) => {
    const ids = Array.from(selectedEventIds);
    if (ids.length === 0) return;
    const store = useCalendarStore.getState();
    const showToast = store.showTransientToast;
    // P2-4: 保存批量操作前的快照，用于 undo
    const idSet = new Set(ids);
    const beforeSnapshots = store.allEvents.filter((e) => idSet.has(e.id)).map((e) => ({ ...e }));
    try {
      setBatchLoading(true);
      const req: CalendarBatchOperationRequest = { ids, op, ...(extra ?? {}) } as CalendarBatchOperationRequest;
      const res = await calendarApi.batchOperation(req);
      if (res.failedCount && res.failedCount > 0) {
        const failTitles = res.failures
          ?.slice(0, 3)
          .map((f) => f.reason || f.message || `#${f.id}`)
          .join(t('calendar.shell.batch.listSeparator'));
        const doneMsg = t('calendar.shell.batch.doneWithFailures', { success: res.successCount ?? 0, failed: res.failedCount ?? 0 });
        showToast(
          failTitles ? doneMsg + t('calendar.shell.batch.failureReasons', { reasons: failTitles }) : doneMsg,
          'warning', 3200,
        );
      } else {
        showToast(t('calendar.shell.batch.done', { count: res.successCount ?? 0 }), 'success', 2200);
      }
      // P2-4: 推入 undo 栈
      const successIds = new Set(
        res.failures ? ids.filter((id) => !res.failures!.some((f) => f.id === id)) : ids,
      );
      const successBeforeSnapshots = beforeSnapshots.filter((e) => successIds.has(e.id));
      if (successBeforeSnapshots.length > 0) {
        useCalendarStore.getState().pushUndo({
          type: op === 'delete' ? 'batch_delete' : 'batch_edit',
          description: op === 'delete'
            ? t('calendar.shell.batch.undoDelete', { count: successBeforeSnapshots.length })
            : t('calendar.shell.batch.undoUpdate', { count: successBeforeSnapshots.length }),
          beforeSnapshots: successBeforeSnapshots,
          afterSnapshots: [],
          eventId: successBeforeSnapshots[0]?.id,
        });
      }
      // 批量删除：后端只推送 CalendarRefresh（非逐条 CalendarEventChanged delete），
      // 而 setAllEvents 是增量合并模式，不会移除 prevArr 中有但 incomingArr 中没有的事件。
      // 必须手动从 allEvents 移除已删除的事件，否则色块残留。
      // 同步更新 list 和 overview，保持四视图一致（与 optimisticUpsertEvent 软删除分支对齐）。
      if (op === 'delete' && res.successCount > 0) {
        const failedIdSet = new Set(res.failures?.map((f) => f.id) ?? []);
        const deletedIdSet = new Set(ids.filter((id) => !failedIdSet.has(id)));
        const prev = store.allEvents;
        const next = prev.filter((e) => !deletedIdSet.has(e.id));
        if (next.length !== prev.length) {
          // 同步过滤 list（列表视图）和 overview.upcomingEvents（概览视图）
          const prevList = store.list;
          const nextList = prevList
            ? {
                ...prevList,
                data: prevList.data.filter((x) => !deletedIdSet.has(x.id)),
                totalItems: Math.max(0, prevList.totalItems - deletedIdSet.size),
              }
            : prevList;
          const prevOv = store.overview;
          const nextOv = prevOv
            ? { ...prevOv, upcomingEvents: prevOv.upcomingEvents.filter((x) => !deletedIdSet.has(x.id)) }
            : prevOv;
          useCalendarStore.setState({
            allEvents: next,
            eventsByDate: buildEventsByDate(next),
            list: nextList,
            overview: nextOv,
          });
          // P2-5: 批量删除也清理 pending patch
          for (const id of deletedIdSet) {
            store.dropPendingPatch(id);
          }
        }
      }
      clearSelectionStore();
      setOpStatus('');
      setOpEventType('');
      useCalendarStore.setState((s) => ({ signalRRev: s.signalRRev + 1 }));
    } catch (e: unknown) {
      const err = e as { response?: { data?: { message?: string } }; message?: string };
      showToast(t('calendar.shell.batch.failed', { message: err?.response?.data?.message ?? err?.message ?? String(e) }), 'error', 3500);
    } finally {
      setBatchLoading(false);
    }
  };

  const handleToggleMode = () => {
    if (multiSelectMode) {
      if (hasSelection) onClearSelection();
      onExitMultiSelect();
    } else {
      onEnterMultiSelect();
    }
  };

  return (
    <Fragment>
      <div
        className={[
          'ml-auto flex items-center gap-2 flex-wrap max-md:w-full max-md:justify-end',
          className ?? '',
        ]
          .join(' ')
          .trim()}
      >
        {/* CAL-001 FIX: 
            进入多选模式即渲染工具栏（不再依赖 hasSelection 才出现），
            需要选中才能执行的操作改为 disabled + 灰态，而非完全不渲染。
            这样用户能看到"多选模式"提供了哪些功能，也能直接退出多选。 */}
        {multiSelectMode && (
          <>
            <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-full text-xs font-medium text-slate-700 dark:text-slate-200 bg-slate-100 dark:bg-slate-800">
              {t('calendar.shell.batch.selectedCount', { count: selectedCount })}
              {variant === 'full' && typeof totalVisibleCount === 'number' && (
                <span className="text-slate-400">/ {totalVisibleCount}</span>
              )}
            </span>

            {variant === 'full' && (
              <button
                type="button"
                onClick={() => onSelectAllOverride?.()}
                disabled={batchLoading || !onSelectAllOverride}
                className="rounded px-2 py-0.5 text-xs font-medium text-sky-600 dark:text-sky-400 hover:bg-sky-50 dark:hover:bg-sky-950/40 border border-transparent transition-colors focus:outline-none focus:ring-2 focus:ring-sky-500/40 disabled:opacity-50"
              >
                {t('calendar.shell.batch.selectAll')}
              </button>
            )}

            {variant === 'full' && (
              <>
                <div className="flex items-center gap-1">
                  <TagIcon className="w-3.5 h-3.5 text-slate-400" />
                  <select
                    value={opEventType}
                    onChange={(e) => {
                      const v = e.target.value;
                      if (!v) return;
                      setOpEventType(v);
                      void runBatch('setEventType', { eventType: v });
                    }}
                    disabled={batchLoading || !hasSelection}
                    className="h-7 px-2 rounded border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-xs focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 transition-all disabled:opacity-50"
                  >
                    <option value="">{t('calendar.shell.batch.changeType')}</option>
                    {Object.keys(EVENT_TYPE_STYLE).map((v) => (
                      <option key={v} value={v}>
                        {EVENT_TYPE_STYLE[v]?.label ?? v}
                      </option>
                    ))}
                  </select>
                </div>

                <div className="flex items-center gap-1">
                  <CheckSquareIcon className="w-3.5 h-3.5 text-slate-400" />
                  <select
                    value={opStatus}
                    onChange={(e) => {
                      const v = e.target.value;
                      if (!v) return;
                      setOpStatus(v);
                      void runBatch('setStatus', { status: v });
                    }}
                    disabled={batchLoading || !hasSelection}
                    className="h-7 px-2 rounded border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-xs focus:outline-none focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 transition-all disabled:opacity-50"
                  >
                    <option value="">{t('calendar.shell.batch.changeStatus')}</option>
                    {Object.keys(STATUS_STYLE).map((v) => (
                      <option key={v} value={v}>
                        {STATUS_STYLE[v]?.label ?? v}
                      </option>
                    ))}
                  </select>
                </div>

                <button
                  type="button"
                  onClick={() => hasSelection && setDeleteConfirmOpen(true)}
                  disabled={batchLoading || !hasSelection}
                  className="inline-flex items-center gap-1 px-2 py-0.5 rounded text-xs border border-red-300 dark:border-red-800 text-red-600 dark:text-red-300 hover:bg-red-50 dark:hover:bg-red-950/40 transition-colors disabled:opacity-50"
                >
                  <Trash2Icon className="w-3.5 h-3.5" />
                  {t('common.delete')}
                </button>
              </>
            )}

            <button
              type="button"
              onClick={onClearSelection}
              disabled={batchLoading || !hasSelection}
              title={hasSelection ? t('calendar.shell.batch.clearSelectionHint') : t('calendar.shell.batch.noSelectionHint')}
              className="rounded px-2 py-0.5 text-xs font-medium text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-800 border border-transparent transition-colors focus:outline-none focus:ring-2 focus:ring-sky-500/40 disabled:opacity-50"
            >
              {t('calendar.common.cancelSelection')}
            </button>
          </>
        )}

        {/* 多选模式切换（始终渲染） */}
        {multiSelectMode ? (
          <button
            type="button"
            onClick={handleToggleMode}
            title={t('calendar.shell.batch.exitMultiSelectHint')}
            disabled={batchLoading}
            className="inline-flex items-center gap-1 px-2.5 py-1 rounded border border-transparent text-xs font-medium bg-gradient-to-r from-sky-500 to-blue-600 text-white shadow-md shadow-sky-500/25 hover:from-sky-600 hover:to-blue-700 focus:outline-none focus:ring-2 focus:ring-sky-500/40 disabled:opacity-50"
          >
            <CheckSquareIcon className="w-3.5 h-3.5" />
            {t('calendar.shell.batch.exitMultiSelect')}
          </button>
        ) : (
          <button
            type="button"
            onClick={handleToggleMode}
            title={t('calendar.shell.batch.enterMultiSelectHint')}
            disabled={batchLoading}
            className="inline-flex items-center gap-1 px-2.5 py-1 rounded border border-slate-300 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-800 transition-colors text-xs font-medium focus:outline-none focus:ring-2 focus:ring-sky-500/40 disabled:opacity-50"
          >
            <SquareIcon className="w-3.5 h-3.5" />
            {t('calendar.shell.batch.multiSelect')}
          </button>
        )}
      </div>

      {/* P1-C#2: 批量删除确认对话框（替换原生 confirm） */}
      <ConfirmDialog
        open={deleteConfirmOpen}
        title={t('calendar.common.deleteEvent')}
        msg={t('calendar.shell.batch.deleteConfirm', { count: selectedCount })}
        confirmText={t('common.delete')}
        variant="danger"
        loading={batchLoading}
        onConfirm={() => { setDeleteConfirmOpen(false); void runBatch('delete'); }}
        onCancel={() => setDeleteConfirmOpen(false)}
      />
    </Fragment>
  );
}
