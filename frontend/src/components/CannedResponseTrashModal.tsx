import { useEffect, useMemo } from 'react';
import { useI18n } from '../context/I18nContext';
import Modal from './Modal';
import ConfirmDialog from './ConfirmDialog';
import type { CannedResponseDto, MailboxResponse } from '../api/types';
import type { PagedList, TrashQuery } from '../hooks/useCrudPage';
import { formatDateTime, sanitizeHtml } from '../utils/format';
import {
  Trash2,
  RotateCcw,
  Search,
  ChevronLeft,
  ChevronRight,
  Inbox,
  Shield,
  Eye,
  Clock,
  User2,
  Mail,
  Share2,
} from 'lucide-react';

interface Props {
  open: boolean;
  onClose: () => void;
  query: TrashQuery;
  onQueryChange: (next: TrashQuery) => void;
  list: PagedList<CannedResponseDto> | null;
  loading: boolean;
  count: number;
  actionLoading: boolean;
  restoreTarget: CannedResponseDto | null;
  onSetRestoreTarget: (t: CannedResponseDto | null) => void;
  hardDeleteTarget: CannedResponseDto | null;
  onSetHardDeleteTarget: (t: CannedResponseDto | null) => void;
  clearTrashOpen: boolean;
  onSetClearTrashOpen: (open: boolean) => void;
  onRestore: () => Promise<void> | void;
  onHardDelete: () => Promise<void> | void;
  onClearTrash: () => Promise<void> | void;
  mailboxes?: MailboxResponse[];
}

export default function CannedResponseTrashModal(props: Props) {
  const { t } = useI18n();
  const {
    open,
    onClose,
    query,
    onQueryChange,
    list,
    loading,
    count,
    actionLoading,
    restoreTarget,
    onSetRestoreTarget,
    hardDeleteTarget,
    onSetHardDeleteTarget,
    clearTrashOpen,
    onSetClearTrashOpen,
    onRestore,
    onHardDelete,
    onClearTrash,
    mailboxes,
  } = props;

  // 在弹窗打开且关键字/页变化时由父级 hook 的 useEffect 触发刷新
  // 这里只在初次打开 keyword/pageSize 有默认值时保证一次刷新通过父级 watch 链路触发即可
  useEffect(() => {
    // 保持与 NotificationTemplates 一致的空白副作用：打开时父级已在 openTrash() 中主动设置了 query，会触发父 hook 的 watch 刷新
  }, [open]);

  const rows = list?.data ?? [];
  const totalItems = list?.totalItems ?? 0;
  const totalPages = list?.totalPages ?? 0;
  const currentPage = list?.currentPage ?? 1;

  const pageSizeOptions = [5, 10, 20, 50];

  const mailboxNameMap = useMemo(() => {
    const m = new Map<number, string>();
    mailboxes?.forEach((mb) => m.set(mb.id, mb.name));
    return m;
  }, [mailboxes]);

  return (
    <>
      <Modal open={open} onClose={onClose} title={t('mail.canned.trash.title')} size="xl">
        <div className="space-y-4 md:space-y-5">
          {/* 头部渐变说明卡 */}
          <div className="relative rounded-2xl bg-gradient-to-br from-rose-50/70 via-white to-red-50/70 dark:from-slate-900 dark:via-slate-800/60 dark:to-rose-900/20 border border-rose-100 dark:border-slate-700/60 p-4 md:p-5 overflow-hidden shadow-sm">
            <div className="pointer-events-none absolute -top-16 -right-16 w-44 h-44 rounded-full bg-rose-500/15 blur-3xl" />
            <div className="relative flex items-start gap-4">
              <div className="shrink-0 w-11 h-11 md:w-12 md:h-12 rounded-2xl bg-gradient-to-br from-rose-500 to-red-500 text-white shadow-md shadow-rose-500/30 flex items-center justify-center ring-1 ring-white/40 dark:ring-white/10">
                <Trash2 className="w-5 h-5 md:w-5.5 md:h-5.5" strokeWidth={2} />
              </div>
              <div className="flex-1 min-w-0">
                <div className="flex items-center gap-2 flex-wrap">
                  <h4 className="text-sm md:text-base font-extrabold tracking-tight text-gray-900 dark:text-slate-50">
                    {t('mail.canned.trash.heading')}
                  </h4>
                  <span className="inline-flex items-center gap-1 px-2.5 py-0.5 rounded-full bg-gradient-to-br from-rose-100 to-red-100 dark:from-rose-900/40 dark:to-red-900/40 text-rose-700 dark:text-rose-300 text-[10px] font-bold uppercase tracking-wider ring-1 ring-rose-500/20">
                    <Shield className="w-3 h-3" />
                    {t('mail.common.recycleBin')}
                  </span>
                </div>
                <p className="text-[11px] md:text-xs text-gray-500 dark:text-slate-400 mt-1 leading-relaxed">
                  {t('mail.canned.trash.retentionPrefix')}<b className="font-semibold text-emerald-600 dark:text-emerald-400">{t('mail.common.restore')}</b>{t('mail.common.trashRetentionMiddle')}<b className="font-semibold text-red-600 dark:text-red-400">{t('mail.common.hardDelete')}</b>{t('mail.common.trashRetentionSuffix')}
                  {t('mail.common.trashFooterTotalPrefix')}<b className="font-black text-rose-600 dark:text-rose-400">{count}</b>{t('mail.canned.trash.pendingSuffix')}
                </p>
              </div>
            </div>
          </div>

          {/* 筛选栏 */}
          <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm p-3 md:p-4 flex items-center gap-2 md:gap-3 flex-wrap">
            <div className="inline-flex items-center gap-1.5 text-[11px] font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400 mr-0.5">
              <Search className="w-3.5 h-3.5" />
              {t('mail.common.searchTrash')}
            </div>
            <div className="relative flex-1 min-w-[180px] max-w-[420px]">
              <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
              <input
                type="text"
                value={query.keyword}
                onChange={(e) => onQueryChange({ ...query, page: 1, keyword: e.target.value })}
                placeholder={t('mail.canned.trash.searchPlaceholder')}
                className="w-full pl-10 pr-3 py-2 text-sm bg-slate-50 dark:bg-slate-900/60 border border-gray-200 dark:border-slate-700 rounded-xl focus:outline-none focus:ring-2 focus:ring-rose-500/30 focus:border-rose-400 dark:focus:border-rose-500 text-gray-800 dark:text-slate-200 placeholder:text-gray-400 dark:placeholder:text-slate-500 transition-all shadow-sm"
              />
            </div>
            <div className="ml-auto flex items-center gap-2 md:gap-2.5 flex-wrap">
              <div className="inline-flex items-center gap-1.5">
                <span className="text-[11px] font-bold uppercase tracking-wider text-gray-500 dark:text-slate-400">{t('mail.common.perPage')}</span>
                <select
                  value={query.pageSize}
                  onChange={(e) => onQueryChange({ ...query, page: 1, pageSize: Number(e.target.value) })}
                  className="px-2.5 py-1.5 text-xs bg-slate-50 dark:bg-slate-900/60 border border-gray-200 dark:border-slate-700 rounded-lg focus:outline-none focus:ring-2 focus:ring-rose-500/30 text-gray-700 dark:text-slate-200 shadow-sm transition-all cursor-pointer"
                >
                  {pageSizeOptions.map((s) => (
                    <option key={s} value={s}>{t('mail.common.trashPageSizeOption', { count: s })}</option>
                  ))}
                </select>
              </div>
              <button
                onClick={() => count > 0 && onSetClearTrashOpen(true)}
                disabled={count === 0}
                className="inline-flex items-center gap-1.5 px-3.5 py-2 text-xs bg-gradient-to-br from-rose-500 to-red-500 text-white rounded-xl shadow-sm shadow-rose-500/20 hover:shadow-md hover:shadow-rose-500/30 transition-all disabled:opacity-50 disabled:cursor-not-allowed font-bold ring-1 ring-rose-500/20"
              >
                <Trash2 className="w-3.5 h-3.5" />
                {t('mail.canned.trash.emptyAll')}
              </button>
            </div>
          </div>

          {/* 表格 */}
          <div className="rounded-2xl bg-white dark:bg-slate-800/60 border border-gray-200 dark:border-slate-700/60 shadow-sm overflow-hidden">
            <div className="overflow-x-auto">
              <table className="w-full min-w-[780px] text-sm">
                <thead>
                  <tr className="bg-gradient-to-r from-rose-50 via-rose-50/70 to-red-50 dark:from-rose-900/25 dark:via-slate-800 dark:to-red-900/20 text-[11px] font-black uppercase tracking-wider text-rose-700 dark:text-rose-300">
                    <th className="px-4 md:px-5 py-3.5 text-left w-16">ID</th>
                    <th className="px-4 md:px-5 py-3.5 text-left min-w-[180px]">{t('mail.canned.trash.colCannedResponse')}</th>
                    <th className="px-4 md:px-5 py-3.5 text-left min-w-[140px]">{t('mail.common.mailboxAndVisibility')}</th>
                    <th className="px-4 md:px-5 py-3.5 text-left min-w-[160px]">{t('mail.common.trashHeaderDeletion')}</th>
                    <th className="px-4 md:px-5 py-3.5 text-right pr-5 md:pr-6 w-28">{t('mail.common.actions')}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-50 dark:divide-slate-700/50">
                  {loading ? (
                    <tr>
                      <td colSpan={5} className="px-4 py-10">
                        <div className="flex items-center justify-center gap-3">
                          <div className="w-8 h-8 border-4 border-rose-200 dark:border-rose-900 border-t-rose-500 dark:border-t-rose-400 rounded-full animate-spin shadow-sm" />
                          <span className="text-sm font-medium text-gray-500 dark:text-slate-400">{t('mail.common.loadingTrash')}</span>
                        </div>
                      </td>
                    </tr>
                  ) : rows.length === 0 ? (
                    <tr>
                      <td colSpan={5} className="px-4 py-14">
                        <div className="flex flex-col items-center text-center text-gray-400 dark:text-slate-500">
                          <div className="w-20 h-20 rounded-2xl bg-gradient-to-br from-gray-50 to-gray-100 dark:from-slate-800 dark:to-slate-900 border border-gray-100 dark:border-slate-700 flex items-center justify-center mb-4">
                            <Inbox className="w-10 h-10 opacity-30" />
                          </div>
                          <p className="text-sm font-semibold text-gray-500 dark:text-slate-400">{t('mail.common.trashEmpty')}</p>
                          <p className="text-xs mt-1 text-gray-400 dark:text-slate-500 max-w-xs">
                            {t('mail.canned.trash.emptyHint')}
                          </p>
                        </div>
                      </td>
                    </tr>
                  ) : (
                    rows.map((item) => (
                      <tr
                        key={item.id}
                        className="hover:bg-gradient-to-r hover:from-rose-50/30 hover:via-white hover:to-red-50/30 dark:hover:from-slate-800/60 dark:hover:via-slate-800/30 dark:hover:to-slate-800/60 transition-colors"
                      >
                        <td className="px-4 md:px-5 py-3.5 text-xs md:text-sm text-gray-400 dark:text-slate-500 font-mono font-bold">#{item.id}</td>
                        <td className="px-4 md:px-5 py-3.5">
                          <div className="font-bold text-gray-800 dark:text-slate-100 tracking-tight leading-snug break-words">
                            {item.name}
                          </div>
                          <div
                            className="mt-1.5 text-xs text-gray-500 dark:text-slate-400 line-clamp-1"
                            dangerouslySetInnerHTML={{ __html: sanitizeHtml(item.body) }}
                          />
                          <div className="mt-1.5 flex items-center gap-1.5 flex-wrap">
                            {item.category && (
                              <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded-md bg-primary-50 dark:bg-primary-900/30 text-primary-700 dark:text-primary-400 text-[10px] font-bold uppercase tracking-wider ring-1 ring-primary-500/20">
                                {t('mail.canned.trash.categoryPrefix')}{item.category}
                              </span>
                            )}
                            {typeof item.sortOrder === 'number' && (
                              <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded-md bg-gray-100 dark:bg-slate-700/50 text-gray-600 dark:text-slate-400 text-[10px] font-bold ring-1 ring-gray-500/10">
                                {t('mail.common.trashSortOrderPrefix')}{item.sortOrder}
                              </span>
                            )}
                          </div>
                        </td>
                        <td className="px-4 md:px-5 py-3.5">
                          <div className="flex items-start gap-1.5 flex-wrap">
                            {item.mailboxId && mailboxNameMap.get(item.mailboxId) && (
                              <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-gradient-to-br from-primary-50 to-indigo-50 dark:from-primary-900/30 dark:to-indigo-900/20 text-primary-700 dark:text-primary-400 text-[10px] font-bold uppercase tracking-wider ring-1 ring-primary-500/20">
                                <Mail className="w-3 h-3" />
                                {mailboxNameMap.get(item.mailboxId)}
                              </span>
                            )}
                            {!item.isShared ? (
                              <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-gradient-to-br from-amber-50 to-orange-50 dark:from-amber-900/30 dark:to-orange-900/20 text-amber-700 dark:text-amber-400 text-[10px] font-bold uppercase tracking-wider ring-1 ring-amber-500/20">
                                <User2 className="w-3 h-3" />
                                {t('mail.canned.badge.personal')}
                              </span>
                            ) : (
                              <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-gradient-to-br from-emerald-50 to-teal-50 dark:from-emerald-900/30 dark:to-teal-900/20 text-emerald-700 dark:text-emerald-400 text-[10px] font-bold uppercase tracking-wider ring-1 ring-emerald-500/20">
                                <Share2 className="w-3 h-3" />
                                {t('mail.canned.badge.shared')}
                              </span>
                            )}
                          </div>
                        </td>
                        <td className="px-4 md:px-5 py-3.5">
                          <div className="space-y-1">
                            {item.deletedAt && (
                              <div className="flex items-center gap-1.5 text-xs text-gray-600 dark:text-slate-300">
                                <Clock className="w-3.5 h-3.5 text-rose-500 shrink-0" />
                                <span>
                                  {t('mail.common.deletedAtPrefix')} <b className="font-bold">{formatDateTime(item.deletedAt)}</b>
                                </span>
                              </div>
                            )}
                            {'deletedByUserName' in item && (item as CannedResponseDto & { deletedByUserName?: string | null }).deletedByUserName && (
                              <div className="flex items-center gap-1.5 text-xs text-gray-600 dark:text-slate-300">
                                <User2 className="w-3.5 h-3.5 text-rose-500 shrink-0" />
                                <span>
                                  {t('mail.canned.trash.deletedBy')} <b className="font-bold">{(item as CannedResponseDto & { deletedByUserName?: string | null }).deletedByUserName}</b>
                                </span>
                              </div>
                            )}
                            {item.createdByUserName && (
                              <div className="flex items-center gap-1.5 text-[11px] text-gray-400 dark:text-slate-500">
                                <Eye className="w-3 h-3 shrink-0 opacity-70" />
                                <span>{t('mail.canned.trash.createdBy')}{item.createdByUserName}</span>
                              </div>
                            )}
                          </div>
                        </td>
                        <td className="px-4 md:px-5 py-3.5 text-right">
                          <div className="inline-flex items-center gap-1">
                            <button
                              onClick={() => onSetRestoreTarget(item)}
                              className="p-2.5 text-gray-400 dark:text-slate-500 hover:text-emerald-600 dark:hover:text-emerald-400 hover:bg-emerald-50 dark:hover:bg-emerald-900/30 rounded-xl transition-all"
                              title={t('mail.canned.trash.restoreTitle')}
                            >
                              <RotateCcw className="w-[18px] h-[18px]" strokeWidth={2} />
                            </button>
                            <button
                              onClick={() => onSetHardDeleteTarget(item)}
                              className="p-2.5 text-gray-400 dark:text-slate-500 hover:text-red-600 dark:hover:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/30 rounded-xl transition-all"
                              title={t('mail.common.hardDeleteIrreversible')}
                            >
                              <Trash2 className="w-[18px] h-[18px]" strokeWidth={2} />
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>

            {/* 分页 */}
            <div className="px-4 md:px-5 py-3 md:py-3.5 border-t border-gray-100 dark:border-slate-700/60 bg-gradient-to-r from-gray-50/60 via-white to-gray-50/60 dark:from-slate-900/50 dark:via-slate-800/60 dark:to-slate-900/50 flex flex-wrap items-center gap-3 md:gap-4 justify-between">
              <div className="inline-flex items-center gap-2 text-xs text-gray-500 dark:text-slate-400 flex-wrap">
                <span className="inline-flex items-center gap-1 px-2.5 py-1 rounded-lg bg-gradient-to-br from-rose-50 to-red-50 dark:from-rose-900/30 dark:to-red-900/20 text-rose-700 dark:text-rose-300 ring-1 ring-rose-500/20 font-bold">
                  <Inbox className="w-3.5 h-3.5" />
                  {t('mail.common.trashFooterTotalPrefix')}<b className="font-black ml-0.5">{totalItems}</b>{t('mail.common.trashFooterTotalSuffix')}
                </span>
                <span className="text-gray-400 dark:text-slate-500">{t('mail.common.trashRangePrefix')}</span>
                <b className="font-black text-gray-700 dark:text-slate-200 tabular-nums">
                  {totalItems === 0 ? 0 : (currentPage - 1) * query.pageSize + 1}
                </b>
                <span className="text-gray-400 dark:text-slate-500">{t('mail.common.trashRangeSeparator')}</span>
                <b className="font-black text-gray-700 dark:text-slate-200 tabular-nums">
                  {Math.min(currentPage * query.pageSize, totalItems)}
                </b>
                <span className="text-gray-400 dark:text-slate-500">{t('mail.common.trashRangeSuffix')}</span>
              </div>

              <div className="flex items-center gap-1.5 ml-auto">
                <button
                  onClick={() => onQueryChange({ ...query, page: Math.max(1, currentPage - 1) })}
                  disabled={currentPage <= 1}
                  className="inline-flex items-center justify-center w-9 h-9 rounded-xl bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-gray-600 dark:text-slate-300 hover:bg-gray-50 dark:hover:bg-slate-700/60 disabled:opacity-40 disabled:cursor-not-allowed shadow-sm transition-all"
                >
                  <ChevronLeft className="w-4 h-4" strokeWidth={2} />
                </button>
                <div className="inline-flex items-center gap-1 px-2.5 py-1.5 rounded-xl bg-gradient-to-br from-rose-500 to-red-500 text-white text-xs font-black shadow-sm shadow-rose-500/20 ring-1 ring-rose-500/20 tabular-nums">
                  {currentPage} / {Math.max(1, totalPages)}
                </div>
                <button
                  onClick={() => onQueryChange({ ...query, page: Math.min(totalPages, currentPage + 1) })}
                  disabled={currentPage >= totalPages}
                  className="inline-flex items-center justify-center w-9 h-9 rounded-xl bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-700 text-gray-600 dark:text-slate-300 hover:bg-gray-50 dark:hover:bg-slate-700/60 disabled:opacity-40 disabled:cursor-not-allowed shadow-sm transition-all"
                >
                  <ChevronRight className="w-4 h-4" strokeWidth={2} />
                </button>
              </div>
            </div>
          </div>
        </div>
      </Modal>

      {/* 恢复确认 */}
      <ConfirmDialog
        open={!!restoreTarget}
        title={t('mail.canned.trash.restoreTitle')}
        message={t('mail.canned.trash.restoreConfirm', { name: restoreTarget?.name ?? '' })}
        confirmText={t('mail.common.restore')}
        variant="primary"
        loading={actionLoading}
        onConfirm={onRestore}
        onCancel={() => onSetRestoreTarget(null)}
      />

      {/* 彻底删除确认 */}
      <ConfirmDialog
        open={!!hardDeleteTarget}
        title={t('mail.common.hardDelete')}
        message={t('mail.canned.trash.hardDeleteConfirm', { name: hardDeleteTarget?.name ?? '' })}
        confirmText={t('mail.common.hardDelete')}
        variant="danger"
        loading={actionLoading}
        onConfirm={onHardDelete}
        onCancel={() => onSetHardDeleteTarget(null)}
      />

      {/* 清空确认 */}
      <ConfirmDialog
        open={clearTrashOpen}
        title={t('mail.common.clearTrash')}
        message={t('mail.canned.trash.clearConfirm', { count })}
        confirmText={t('mail.common.clearAll')}
        variant="danger"
        loading={actionLoading}
        onConfirm={onClearTrash}
        onCancel={() => onSetClearTrashOpen(false)}
      />
    </>
  );
}
