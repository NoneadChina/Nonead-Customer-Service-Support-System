import Modal from './Modal';
import ConfirmDialog from './ConfirmDialog';
import { useI18n } from '../context/I18nContext';
import { formatDateTime } from '../utils/format';
import { Search, RotateCcw, Trash2, ChevronLeft, ChevronRight } from 'lucide-react';
import type { MacroDto } from '../api/types';
import type { PagedList, TrashQuery } from '../hooks/useCrudPage';

export interface MacroTrashModalProps {
  open: boolean;
  onClose: () => void;
  query: TrashQuery;
  onQueryChange: (query: TrashQuery) => void;
  list: PagedList<MacroDto> | null;
  loading: boolean;
  count: number;
  actionLoading: boolean;
  restoreTarget: MacroDto | null;
  onSetRestoreTarget: (m: MacroDto | null) => void;
  hardDeleteTarget: MacroDto | null;
  onSetHardDeleteTarget: (m: MacroDto | null) => void;
  clearTrashOpen: boolean;
  onSetClearTrashOpen: (v: boolean) => void;
  onRestore: () => void;
  onHardDelete: () => void;
  onClearTrash: () => void;
}

export default function MacroTrashModal({
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
}: MacroTrashModalProps) {
  const { t } = useI18n();
  const data = list?.data ?? [];
  const total = list?.totalItems ?? 0;
  const totalPages = list?.totalPages ?? 0;
  const currentPage = list?.currentPage ?? 1;

  return (
    <>
      {open && (
        <Modal
          open
          title={t('automation.macros.trash.title')}
          onClose={onClose}
          size="lg"
          footer={
            <div className="flex items-center justify-between w-full">
              <button
                onClick={() => onSetClearTrashOpen(true)}
                disabled={actionLoading || count === 0}
                className="px-4 py-2 text-sm font-medium text-red-600 bg-red-50 border border-red-200 rounded-lg hover:bg-red-100 disabled:opacity-50 transition"
              >
                {t('automation.common.clearTrash')}
              </button>
              <button
                onClick={onClose}
                className="px-4 py-2 text-sm bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700"
              >
                {t('common.close')}
              </button>
            </div>
          }
        >
          <div className="space-y-4">
            <div className="relative">
              <Search className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
              <input
                type="text"
                placeholder={t('automation.macros.trash.searchPlaceholder')}
                value={query.keyword}
                onChange={(e) => onQueryChange({ ...query, keyword: e.target.value, page: 1 })}
                className="w-full pl-9 pr-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-900"
              />
            </div>
            <div className="overflow-x-auto">
              <table className="w-full">
                <thead className="bg-gray-50 dark:bg-slate-900 border-b border-gray-200 dark:border-slate-700">
                  <tr>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.macros.trash.columns.name')}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.common.mailboxColumn')}</th>
                    <th className="px-4 py-3 text-left text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.common.deletedAt')}</th>
                    <th className="px-4 py-3 text-right text-xs font-medium text-gray-500 dark:text-slate-400 uppercase">{t('automation.common.actions')}</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-gray-100">
                  {loading ? (
                    <tr><td colSpan={4} className="px-4 py-12 text-center text-gray-400 text-sm">{t('common.shell.loading')}</td></tr>
                  ) : data.length === 0 ? (
                    <tr><td colSpan={4} className="px-4 py-12 text-center text-gray-400 text-sm">{t('automation.common.trashEmpty')}</td></tr>
                  ) : (
                    data.map((m) => (
                      <tr key={m.id} className="hover:bg-gray-50 dark:hover:bg-slate-700 dark:bg-slate-900">
                        <td className="px-4 py-3">
                          <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{m.name}</div>
                          {m.description && <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 line-clamp-1">{m.description}</div>}
                        </td>
                        <td className="px-4 py-3 text-sm text-gray-700 dark:text-slate-300">
                          {!m.mailboxId ? <span className="text-gray-400">{t('automation.common.allMailboxes')}</span> : m.mailboxName || t('automation.common.mailboxNumber', { id: m.mailboxId })}
                        </td>
                        <td className="px-4 py-3 text-xs text-gray-500 dark:text-slate-400">
                          {m.deletedAt ? formatDateTime(m.deletedAt) : '—'}
                        </td>
                        <td className="px-4 py-3 text-right">
                          <div className="flex items-center justify-end gap-1">
                            <button
                              onClick={() => onSetRestoreTarget(m)}
                              className="p-2 text-green-600 hover:bg-green-50 rounded-lg transition-colors"
                              title={t('automation.common.restore')}
                            >
                              <RotateCcw className="w-4 h-4" />
                            </button>
                            <button
                              onClick={() => onSetHardDeleteTarget(m)}
                              className="p-2 text-red-600 hover:bg-red-50 rounded-lg transition-colors"
                              title={t('automation.common.hardDelete')}
                            >
                              <Trash2 className="w-4 h-4" />
                            </button>
                          </div>
                        </td>
                      </tr>
                    ))
                  )}
                </tbody>
              </table>
            </div>
            {totalPages > 1 && (
              <div className="flex items-center justify-between">
                <div className="text-sm text-gray-500 dark:text-slate-400">
                  {t('automation.common.paginationTotalAndPage', { count: total, page: currentPage, total: totalPages })}
                </div>
                <div className="flex items-center gap-1">
                  <button
                    onClick={() => onQueryChange({ ...query, page: query.page - 1 })}
                    disabled={currentPage <= 1}
                    className="p-2 text-gray-600 dark:text-slate-400 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50"
                  >
                    <ChevronLeft className="w-4 h-4" />
                  </button>
                  <button
                    onClick={() => onQueryChange({ ...query, page: query.page + 1 })}
                    disabled={currentPage >= totalPages}
                    className="p-2 text-gray-600 dark:text-slate-400 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50"
                  >
                    <ChevronRight className="w-4 h-4" />
                  </button>
                </div>
              </div>
            )}
          </div>
        </Modal>
      )}

      <ConfirmDialog
        open={!!restoreTarget}
        title={t('automation.macros.restore.title')}
        message={t('automation.macros.restore.message', { name: restoreTarget?.name ?? '' })}
        confirmText={t('automation.common.restore')}
        variant="primary"
        onConfirm={onRestore}
        onCancel={() => onSetRestoreTarget(null)}
        loading={actionLoading}
      />

      <ConfirmDialog
        open={!!hardDeleteTarget}
        title={t('automation.common.hardDelete')}
        message={t('automation.macros.hardDelete.message', { name: hardDeleteTarget?.name ?? '' })}
        confirmText={t('automation.common.hardDelete')}
        variant="danger"
        onConfirm={onHardDelete}
        onCancel={() => onSetHardDeleteTarget(null)}
        loading={actionLoading}
      />

      <ConfirmDialog
        open={clearTrashOpen}
        title={t('automation.macros.clearTrash.title')}
        message={t('automation.macros.clearTrash.message')}
        confirmText={t('automation.common.clear')}
        variant="danger"
        onConfirm={onClearTrash}
        onCancel={() => onSetClearTrashOpen(false)}
        loading={actionLoading}
      />
    </>
  );
}
