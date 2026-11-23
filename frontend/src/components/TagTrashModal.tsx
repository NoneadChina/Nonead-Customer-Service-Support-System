import Modal from './Modal';
import ConfirmDialog from './ConfirmDialog';
import { formatDate } from '../utils/format';
import { useI18n } from '../context/I18nContext';
import type { TagResponse, MailboxResponse } from '../api/types';
import type { PagedList, TrashQuery } from '../hooks/useCrudPage';

export interface TagTrashModalProps {
  open: boolean;
  onClose: () => void;
  query: TrashQuery;
  onQueryChange: (query: TrashQuery) => void;
  list: PagedList<TagResponse> | null;
  loading: boolean;
  count: number;
  actionLoading: boolean;
  restoreTarget: TagResponse | null;
  onSetRestoreTarget: (t: TagResponse | null) => void;
  hardDeleteTarget: TagResponse | null;
  onSetHardDeleteTarget: (t: TagResponse | null) => void;
  clearTrashOpen: boolean;
  onSetClearTrashOpen: (v: boolean) => void;
  onRestore: () => void;
  onHardDelete: () => void;
  onClearTrash: () => void;
  mailboxes: MailboxResponse[];
}

export default function TagTrashModal({
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
}: TagTrashModalProps) {
  const { t } = useI18n();
  const data = list?.data ?? [];
  const total = list?.totalItems ?? 0;
  const totalPages = list?.totalPages ?? 0;
  const currentPage = list?.currentPage ?? 1;

  const getMailboxName = (id: number | null) => {
    if (id === null) return t('customers.common.global');
    return mailboxes.find((m) => m.id === id)?.name ?? `#${id}`;
  };

  return (
    <>
      {open && (
        <Modal
          open
          title={t('customers.tags.trashTitle')}
          onClose={onClose}
          size="xl"
          footer={
            <div className="flex items-center justify-between w-full">
              <button
                onClick={() => onSetClearTrashOpen(true)}
                disabled={actionLoading || count === 0}
                className="px-4 py-2 text-sm font-medium text-red-600 bg-red-50 border border-red-200 rounded-lg hover:bg-red-100 disabled:opacity-50 transition"
              >
                {t('customers.common.clearTrash')}
              </button>
              <button
                onClick={onClose}
                className="px-4 py-2 text-sm bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 dark:text-slate-200"
              >
                {t('customers.common.close')}
              </button>
            </div>
          }
        >
          <div className="space-y-4">
            <div className="relative">
              <svg className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <circle cx="11" cy="11" r="8" />
                <path d="M21 21l-4.35-4.35" />
              </svg>
              <input
                type="text"
                placeholder={t('customers.tags.trashSearchPlaceholder')}
                value={query.keyword}
                onChange={(e) => onQueryChange({ ...query, keyword: e.target.value, page: 1 })}
                className="w-full pl-9 pr-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-900 dark:text-slate-200"
              />
            </div>

            <div className="text-sm text-gray-500 dark:text-slate-400">
              {t('customers.tags.trashTotalPrefix')}<span className="font-semibold text-gray-900 dark:text-slate-100">{total}</span>{t('customers.tags.trashTotalSuffix')}
            </div>

            <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-100 dark:border-slate-700/50 overflow-hidden">
              {loading ? (
                <div className="p-8 text-center">
                  <div className="w-8 h-8 border-4 border-primary-200 dark:border-primary-700 border-t-primary-600 rounded-full animate-spin mx-auto mb-2" />
                  <p className="text-sm text-gray-500 dark:text-slate-400">{t('customers.common.loading')}</p>
                </div>
              ) : data.length === 0 ? (
                <div className="p-8 text-center">
                  <div className="w-12 h-12 bg-gray-100 dark:bg-slate-700/50 rounded-full flex items-center justify-center mx-auto mb-3">
                    <svg className="w-6 h-6 text-gray-400 dark:text-slate-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                    </svg>
                  </div>
                  <p className="text-sm font-medium text-gray-900 dark:text-slate-100 mb-1">{t('customers.common.trashEmpty')}</p>
                  <p className="text-sm text-gray-500 dark:text-slate-400">
                    {query.keyword ? t('customers.tags.trashEmptySearch') : t('customers.tags.trashEmptyHint')}
                  </p>
                </div>
              ) : (
                <div className="overflow-x-auto">
                  <table className="min-w-full divide-y divide-gray-100 dark:divide-slate-700/50">
                    <thead className="bg-gray-50 dark:bg-slate-900/50">
                      <tr>
                        <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">ID</th>
                        <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('customers.tags.columnTag')}</th>
                        <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('customers.common.relatedMailbox')}</th>
                        <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('customers.common.deletedAt')}</th>
                        <th className="px-4 py-3 text-left text-xs font-semibold text-slate-700 dark:text-slate-300 uppercase tracking-wider">{t('customers.common.actions')}</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-gray-100 dark:divide-slate-700/30">
                      {data.map((tag) => (
                        <tr key={tag.id} className="hover:bg-gray-50 dark:hover:bg-slate-700/30 transition-colors">
                          <td className="px-4 py-3 text-sm text-gray-500 dark:text-slate-400 font-mono">#{tag.id}</td>
                          <td className="px-4 py-3">
                            <div className="flex items-center gap-2">
                              <span
                                className="flex-shrink-0 w-6 h-6 rounded-full flex items-center justify-center"
                                style={{
                                  backgroundColor: `${tag.color}20`,
                                  border: `2px solid ${tag.color}`,
                                }}
                              >
                                <span className="w-2 h-2 rounded-full" style={{ backgroundColor: tag.color }} />
                              </span>
                              <span className="text-sm font-medium text-gray-900 dark:text-slate-100">{tag.name}</span>
                            </div>
                          </td>
                          <td className="px-4 py-3 text-sm text-gray-700 dark:text-slate-300">{getMailboxName(tag.mailboxId)}</td>
                          <td className="px-4 py-3 text-sm text-gray-500 dark:text-slate-400 whitespace-nowrap">
                            {formatDate(tag.deletedAt || tag.updatedAt)}
                          </td>
                          <td className="px-4 py-3 text-sm">
                            <div className="flex items-center gap-1">
                              <button
                                onClick={() => onSetRestoreTarget(tag)}
                                className="p-1.5 text-gray-400 hover:text-green-600 hover:bg-green-50 dark:hover:bg-green-900/20 rounded-md transition"
                                title={t('customers.common.restore')}
                              >
                                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                                  <path strokeLinecap="round" strokeLinejoin="round" d="M3 10h10a8 8 0 018 8v2M3 10l6 6m-6-6l6-6" />
                                </svg>
                              </button>
                              <button
                                onClick={() => onSetHardDeleteTarget(tag)}
                                className="p-1.5 text-gray-400 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-md transition"
                                title={t('customers.common.hardDelete')}
                              >
                                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                                  <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                                </svg>
                              </button>
                            </div>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </div>

            {totalPages > 1 && (
              <div className="flex items-center justify-center gap-2">
                <button
                  onClick={() => onQueryChange({ ...query, page: Math.max(1, query.page - 1) })}
                  disabled={currentPage <= 1}
                  className="px-3 py-1.5 text-sm border rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition"
                >
                  {t('customers.common.prevPage')}
                </button>
                <span className="text-sm text-gray-500 dark:text-slate-400">
                  {t('customers.common.pageOf', { page: currentPage, total: totalPages })}
                </span>
                <button
                  onClick={() => onQueryChange({ ...query, page: Math.min(totalPages, query.page + 1) })}
                  disabled={currentPage >= totalPages}
                  className="px-3 py-1.5 text-sm border rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-40 disabled:cursor-not-allowed transition"
                >
                  {t('customers.common.nextPage')}
                </button>
              </div>
            )}
          </div>
        </Modal>
      )}

      <ConfirmDialog
        open={!!restoreTarget}
        title={t('customers.tags.restoreTitle')}
        message={t('customers.tags.restoreMessage', { name: restoreTarget?.name ?? '' })}
        confirmText={t('customers.common.restore')}
        variant="primary"
        onConfirm={onRestore}
        onCancel={() => onSetRestoreTarget(null)}
        loading={actionLoading}
      />

      <ConfirmDialog
        open={!!hardDeleteTarget}
        title={t('customers.tags.hardDeleteTitle')}
        message={t('customers.tags.hardDeleteMessage', { name: hardDeleteTarget?.name ?? '' })}
        confirmText={t('customers.common.hardDelete')}
        variant="danger"
        onConfirm={onHardDelete}
        onCancel={() => onSetHardDeleteTarget(null)}
        loading={actionLoading}
      />

      <ConfirmDialog
        open={clearTrashOpen}
        title={t('customers.common.clearTrash')}
        message={t('customers.tags.clearTrashMessage')}
        confirmText={t('customers.tags.confirmClearTrash')}
        variant="danger"
        onConfirm={onClearTrash}
        onCancel={() => onSetClearTrashOpen(false)}
        loading={actionLoading}
      />
    </>
  );
}
