import { useEffect, useState, FormEvent } from 'react';
import { tagApi, mailboxApi } from '../api';
import { useCrudPage } from '../hooks/useCrudPage';
import type { TagResponse, MailboxResponse, CreateTagRequest, UpdateTagRequest } from '../api/types';
import { formatDate } from '../utils/format';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import TagTrashModal from '../components/TagTrashModal';
import { useI18n } from '../context/I18nContext';

// 预设颜色
const presetColors = [
  '#ef4444', // red
  '#f97316', // orange
  '#f59e0b', // amber
  '#eab308', // yellow
  '#84cc16', // lime
  '#22c55e', // green
  '#10b981', // emerald
  '#14b8a6', // teal
  '#06b6d4', // cyan
  '#3b82f6', // blue
  '#6366f1', // indigo
  '#8b5cf6', // violet
  '#a855f7', // purple
  '#d946ef', // fuchsia
  '#ec4899', // pink
  '#64748b', // slate
];

interface FormState {
  name: string;
  color: string;
  mailboxId: number | null;
}

const emptyForm: FormState = {
  name: '',
  color: presetColors[9], // 默认蓝色
  mailboxId: null,
};

interface TagQuery {
  page: number;
  pageSize: number;
  keyword: string;
  mailboxId: number | null;
}

import { getApiErrorMessage } from '../utils/error';

export default function Tags() {
  const { t } = useI18n();
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [searchInput, setSearchInput] = useState('');

  // 编辑表单
  const [form, setForm] = useState<FormState>(emptyForm);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [formError, setFormError] = useState('');

  // 删除
  const [deleting, setDeleting] = useState(false);

  // 垃圾箱操作目标
  const [restoreTarget, setRestoreTarget] = useState<TagResponse | null>(null);
  const [hardDeleteTarget, setHardDeleteTarget] = useState<TagResponse | null>(null);
  const [clearTrashOpen, setClearTrashOpen] = useState(false);

  const crud = useCrudPage<TagResponse, TagQuery>({
    defaultQuery: { page: 1, pageSize: 50, keyword: '', mailboxId: null },
    fetchList: async (q) => {
      // tagApi.list 返回全量数组，需客户端过滤+分页
      const all = await tagApi.list(q.mailboxId ?? undefined);
      const arr = Array.isArray(all) ? all : [];
      let filtered = arr;
      if (q.keyword) {
        const kw = q.keyword.toLowerCase();
        filtered = arr.filter((t) => t.name.toLowerCase().includes(kw));
      }
      const page = q.page ?? 1;
      const pageSize = q.pageSize ?? 50;
      const totalItems = filtered.length;
      const totalPages = Math.max(1, Math.ceil(totalItems / pageSize));
      const start = (page - 1) * pageSize;
      const data = filtered.slice(start, start + pageSize);
      return { data, totalItems, totalPages, currentPage: page };
    },
    fetchTrashList: (q) => tagApi.getTrash(q.page, q.pageSize, q.keyword),
    fetchTrashCount: tagApi.getTrashCount,
  });

  const { toast } = crud;

  // 加载邮箱列表
  useEffect(() => {
    let destroyed = false;
    mailboxApi
      .listAll()
      .then((res: MailboxResponse[]) => {
        if (!destroyed) setMailboxes(Array.isArray(res) ? res : []);
      })
      .catch((err: unknown) => console.error('加载邮箱列表失败:', err));
    return () => { destroyed = true; };
  }, []);

  const handleSearch = () => {
    crud.handleFilterChange('keyword', searchInput.trim());
  };

  const handleResetSearch = () => {
    setSearchInput('');
    crud.handleFilterChange('keyword', '');
  };

  const openTrash = () => {
    crud.trash.setOpen(true);
    crud.trash.setQuery({ page: 1, pageSize: 10, keyword: '' });
    crud.trash.refreshCount();
  };

  const handleRestore = async () => {
    if (!restoreTarget) return;
    crud.trash.setActionLoading(true);
    try {
      await tagApi.restore(restoreTarget.id);
      toast.show({ type: 'success', text: t('customers.tags.restored', { name: restoreTarget.name }) });
      setRestoreTarget(null);
      crud.trash.refresh();
      crud.trash.refreshCount();
      crud.refresh();
    } catch (e: unknown) {
      toast.show({ type: 'error', text: getApiErrorMessage(e, t('customers.common.restoreFailed')) });
    } finally {
      crud.trash.setActionLoading(false);
    }
  };

  const handleHardDelete = async () => {
    if (!hardDeleteTarget) return;
    crud.trash.setActionLoading(true);
    try {
      await tagApi.hardDelete(hardDeleteTarget.id);
      toast.show({ type: 'success', text: t('customers.tags.hardDeleted', { name: hardDeleteTarget.name }) });
      setHardDeleteTarget(null);
      crud.trash.refresh();
      crud.trash.refreshCount();
    } catch (e: unknown) {
      toast.show({ type: 'error', text: getApiErrorMessage(e, t('customers.common.hardDeleteFailed')) });
    } finally {
      crud.trash.setActionLoading(false);
    }
  };

  const handleClearTrash = async () => {
    crud.trash.setActionLoading(true);
    try {
      const res: { count?: number } = await tagApi.clearTrash();
      const count = res?.count ?? 0;
      toast.show({ type: 'success', text: t('customers.tags.cleared', { count }) });
      setClearTrashOpen(false);
      crud.trash.refresh();
      crud.trash.refreshCount();
    } catch (e: unknown) {
      toast.show({ type: 'error', text: getApiErrorMessage(e, t('customers.common.clearFailed')) });
    } finally {
      crud.trash.setActionLoading(false);
    }
  };

  const openCreate = () => {
    setEditingId(null);
    setForm(emptyForm);
    setFormError('');
    crud.openEditor();
  };

  const openEdit = (tag: TagResponse) => {
    setEditingId(tag.id);
    setForm({
      name: tag.name,
      color: tag.color,
      mailboxId: tag.mailboxId,
    });
    setFormError('');
    crud.openEditor(tag);
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!form.name.trim()) {
      setFormError(t('customers.tags.nameRequired'));
      return;
    }
    if (!form.color.trim()) {
      setFormError(t('customers.tags.colorRequired'));
      return;
    }

    setSubmitting(true);
    setFormError('');
    try {
      if (editingId) {
        const payload: UpdateTagRequest = {
          name: form.name.trim(),
          color: form.color.trim(),
        };
        await tagApi.update(editingId, payload);
        toast.show({ type: 'success', text: t('customers.tags.updated') });
      } else {
        const payload: CreateTagRequest = {
          name: form.name.trim(),
          color: form.color.trim(),
          mailboxId: form.mailboxId,
        };
        await tagApi.create(payload);
        toast.show({ type: 'success', text: t('customers.tags.created') });
      }
      crud.closeEditor();
      crud.refresh();
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : t('customers.common.operationFailed'));
    } finally {
      setSubmitting(false);
    }
  };

  const handleDelete = async () => {
    if (!crud.deleteTarget) return;
    setDeleting(true);
    try {
      await tagApi.remove(crud.deleteTarget.id);
      toast.show({ type: 'success', text: t('customers.tags.movedToTrash') });
      crud.setDeleteTarget(null);
      crud.trash.refreshCount();
      crud.refresh();
    } catch (err: unknown) {
      toast.show({ type: 'error', text: err instanceof Error ? err.message : t('customers.common.deleteFailed') });
    } finally {
      setDeleting(false);
    }
  };

  const getMailboxName = (id: number | null) => {
    if (id === null) return t('customers.common.global');
    return mailboxes.find((m) => m.id === id)?.name ?? `#${id}`;
  };

  const list = crud.list?.data ?? [];
  const total = crud.list?.totalItems ?? 0;

  return (
    <div className="space-y-4 sm:space-y-5">
      <div className="flex items-center justify-between gap-3">
        <div className="min-w-0">
          <h1 className="text-xl sm:text-2xl font-bold text-gray-900 dark:text-slate-100">{t('customers.tags.title')}</h1>
          <p className="text-sm text-gray-500 dark:text-slate-400 mt-1">{t('customers.tags.subtitle')}</p>
        </div>
        <button
          onClick={openCreate}
          className="inline-flex items-center gap-1.5 px-3 sm:px-4 py-2 text-sm bg-primary-600 hover:bg-primary-700 text-white rounded-lg shadow-sm shrink-0"
        >
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m8-8H4" />
          </svg>
          {t('customers.tags.newTag')}
        </button>
      </div>

      {/* 搜索栏 */}
      <div className="bg-gradient-to-r from-primary-50 to-indigo-50 dark:from-slate-900 dark:to-slate-800 rounded-2xl border border-primary-100 dark:border-slate-700/50 p-3 sm:p-4 shadow-sm">
        <div className="flex flex-col sm:flex-row items-stretch sm:items-end gap-3">
          <div className="flex-1 min-w-0 relative">
            <svg className="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400 dark:text-slate-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <circle cx="11" cy="11" r="8" />
              <path d="M21 21l-4.35-4.35" />
            </svg>
            <input
              type="text"
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              onKeyDown={(e) => e.key === 'Enter' && handleSearch()}
              placeholder={t('customers.tags.searchPlaceholder')}
              className="w-full pl-9 pr-10 py-2 text-sm bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded-xl focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-transparent transition"
            />
            {searchInput && (
              <button
                onClick={handleResetSearch}
                className="absolute right-3 top-1/2 -translate-y-1/2 text-gray-400 hover:text-gray-600 dark:hover:text-slate-300"
              >
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M6 18L18 6M6 6l12 12" />
                </svg>
              </button>
            )}
          </div>
          <div className="flex items-center gap-2">
            <button
              onClick={handleSearch}
              className="inline-flex items-center justify-center gap-1.5 px-3 sm:px-4 py-2 text-sm bg-primary-600 hover:bg-primary-700 text-white rounded-xl transition"
              title={t('customers.common.search')}
            >
              <svg className="w-4 h-4 sm:hidden" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <circle cx="11" cy="11" r="8" />
                <path d="M21 21l-4.35-4.35" />
              </svg>
              <span className="hidden sm:inline">{t('customers.common.search')}</span>
            </button>
            {crud.query.keyword && (
              <button
                onClick={handleResetSearch}
                className="inline-flex items-center justify-center gap-1.5 px-3 sm:px-4 py-2 text-sm bg-white dark:bg-slate-700 border border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-200 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-600 transition"
                title={t('customers.common.reset')}
              >
                <svg className="w-4 h-4 sm:hidden" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
                </svg>
                <span className="hidden sm:inline">{t('customers.common.reset')}</span>
              </button>
            )}
            <button
              onClick={openTrash}
              className="inline-flex items-center justify-center gap-1.5 px-3 sm:px-4 py-2 text-sm bg-white dark:bg-slate-700 border border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-200 rounded-xl hover:bg-gray-50 dark:hover:bg-slate-600 transition"
              title={t('customers.common.trash')}
            >
              <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
              </svg>
              <span className="hidden sm:inline">{t('customers.common.trash')}</span>
              {crud.trash.count > 0 && (
                <span className="px-1.5 py-0.5 text-xs rounded-full bg-red-100 dark:bg-red-900/30 text-red-600 dark:text-red-400">
                  {crud.trash.count}
                </span>
              )}
            </button>
          </div>
        </div>
        {crud.query.keyword && total > 0 && (
          <div className="mt-2 text-xs text-gray-500 dark:text-slate-400">
            {t('customers.tags.searchFoundPrefix')}<span className="font-medium text-primary-600 dark:text-primary-400">{total}</span>{t('customers.tags.searchFoundSuffix')}
          </div>
        )}
      </div>

      {/* 标签列表 - 卡片网格 */}
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4 gap-3">
        {crud.loading ? (
          <div className="col-span-full p-10 text-center">
            <div className="w-10 h-10 border-4 border-primary-200 dark:border-primary-700 border-t-primary-600 dark:border-t-primary-400 rounded-full animate-spin mx-auto mb-3" />
            <p className="text-gray-500 dark:text-slate-400">{t('customers.common.loading')}</p>
          </div>
        ) : list.length === 0 ? (
          <div className="col-span-full p-10 text-center">
            <div className="w-16 h-16 bg-gray-100 dark:bg-slate-800 rounded-full flex items-center justify-center mx-auto mb-4">
              <svg className="w-8 h-8 text-gray-400 dark:text-slate-500" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={1.5} d="M7 21h10a2 2 0 002-2V9.414a1 1 0 00-.293-.707l-5.414-5.414A1 1 0 0012.586 3H7a2 2 0 00-2 2v14a2 2 0 002 2z" />
              </svg>
            </div>
            <p className="text-gray-500 dark:text-slate-400">{crud.query.keyword ? t('customers.tags.emptySearch', { keyword: crud.query.keyword }) : t('customers.tags.empty')}</p>
          </div>
        ) : (
          list.map((tag) => (
            <div
              key={tag.id}
              className="bg-white dark:bg-slate-800 rounded-xl border border-gray-100 dark:border-slate-700/50 p-4 shadow-sm hover:shadow-md hover:border-primary-200 dark:hover:border-primary-700/50 transition-all duration-200 group"
            >
              <div className="flex items-start justify-between">
                <div className="flex items-center gap-3 min-w-0 flex-1">
                  <span
                    className="flex-shrink-0 w-8 h-8 rounded-full flex items-center justify-center transition-transform group-hover:scale-110"
                    style={{
                      backgroundColor: `${tag.color}20`,
                      border: `2px solid ${tag.color}`,
                    }}
                  >
                    <span className="w-3 h-3 rounded-full" style={{ backgroundColor: tag.color }} />
                  </span>
                  <div className="min-w-0 flex-1">
                    <h3 className="font-semibold text-gray-900 dark:text-slate-100 truncate text-sm">{tag.name}</h3>
                    <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 flex items-center gap-1">
                      <svg className="w-3 h-3" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                        <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
                      </svg>
                      {getMailboxName(tag.mailboxId)}
                    </p>
                  </div>
                </div>
                <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition">
                  <button
                    onClick={() => openEdit(tag)}
                    className="p-1.5 text-gray-500 hover:text-primary-600 hover:bg-primary-50 dark:hover:bg-primary-900/20 rounded-md transition"
                    title={t('customers.common.edit')}
                  >
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                    </svg>
                  </button>
                  <button
                    onClick={() => crud.setDeleteTarget(tag)}
                    className="p-1.5 text-gray-500 hover:text-red-600 hover:bg-red-50 dark:hover:bg-red-900/20 rounded-md transition"
                    title={t('customers.common.delete')}
                  >
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                    </svg>
                  </button>
                </div>
              </div>
              <div className="mt-3 pt-3 border-t border-gray-100 dark:border-slate-700/50 flex items-center justify-between text-xs">
                <span className="text-gray-400 dark:text-slate-500">{formatDate(tag.createdAt)}</span>
                <span className="font-mono px-2 py-0.5 rounded" style={{ backgroundColor: `${tag.color}15`, color: tag.color }}>#{tag.id}</span>
              </div>
            </div>
          ))
        )}
      </div>

      {/* 新建/编辑弹窗 */}
      <Modal
        open={crud.showEditor}
        title={editingId ? t('customers.tags.editTag') : t('customers.tags.newTag')}
        onClose={crud.closeEditor}
        size="md"
        footer={
          <>
            <button
              onClick={crud.closeEditor}
              disabled={submitting}
              className="px-4 py-2 text-sm bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 dark:text-slate-200"
            >
              {t('customers.common.cancel')}
            </button>
            <button
              type="submit"
              form="tag-form"
              disabled={submitting}
              className="px-4 py-2 text-sm bg-primary-600 hover:bg-primary-700 disabled:bg-primary-300 text-white rounded-lg"
            >
              {submitting ? t('customers.common.saving') : t('customers.common.save')}
            </button>
          </>
        }
      >
        <form id="tag-form" onSubmit={handleSubmit} className="space-y-5">
          {formError && (
            <div className="bg-red-50 border border-red-200 text-red-700 px-3 py-2 rounded-lg text-sm">
              {formError}
            </div>
          )}

          {/* 预览 */}
          <div className="flex items-center justify-center py-4 bg-gray-50 dark:bg-slate-900 rounded-lg">
            <span
              className="inline-flex items-center gap-1.5 px-4 py-1.5 rounded-full text-sm font-medium"
              style={{
                backgroundColor: `${form.color}20`,
                color: form.color,
                border: `1px solid ${form.color}40`,
              }}
            >
              <span className="w-2 h-2 rounded-full" style={{ backgroundColor: form.color }} />
              {form.name.trim() || t('customers.tags.preview')}
            </span>
          </div>

          {/* 名称 */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
              {t('customers.tags.nameLabel')} <span className="text-red-500">*</span>
            </label>
            <input
              type="text"
              required
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 dark:text-slate-200"
              placeholder={t('customers.tags.namePlaceholder')}
              maxLength={50}
            />
          </div>

          {/* 颜色选择器 */}
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
              {t('customers.tags.colorLabel')} <span className="text-red-500">*</span>
            </label>
            <div className="grid grid-cols-8 gap-2 mb-2">
              {presetColors.map((c) => (
                <button
                  key={c}
                  type="button"
                  onClick={() => setForm({ ...form, color: c })}
                  className={`relative w-9 h-9 rounded-lg transition ${
                    form.color.toLowerCase() === c.toLowerCase()
                      ? 'ring-2 ring-offset-2 ring-gray-700 scale-105'
                      : 'hover:scale-110'
                  }`}
                  style={{ backgroundColor: c }}
                  title={c}
                >
                  {form.color.toLowerCase() === c.toLowerCase() && (
                    <svg className="absolute inset-0 m-auto w-4 h-4 text-white" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={3}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
                    </svg>
                  )}
                </button>
              ))}
            </div>
            <div className="flex items-center gap-2">
              <input
                type="color"
                value={form.color}
                onChange={(e) => setForm({ ...form, color: e.target.value })}
                className="w-10 h-10 rounded cursor-pointer border border-gray-300 dark:border-slate-600"
              />
              <input
                type="text"
                value={form.color}
                onChange={(e) => setForm({ ...form, color: e.target.value })}
                className="flex-1 px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 font-mono text-sm dark:text-slate-200"
                placeholder="#3b82f6"
              />
            </div>
          </div>

          {/* 邮箱关联 */}
          {!editingId && (
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">
                {t('customers.tags.mailboxLabel')}<span className="text-gray-400 text-xs">{t('customers.tags.optional')}</span>
              </label>
              <select
                value={form.mailboxId ?? ''}
                onChange={(e) => setForm({ ...form, mailboxId: e.target.value === '' ? null : Number(e.target.value) })}
                className="w-full px-3 py-2 bg-white dark:bg-slate-900 border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 dark:text-slate-200"
              >
                <option value="">{t('customers.tags.globalMailboxOption')}</option>
                {mailboxes.map((m) => (
                  <option key={m.id} value={m.id}>{m.name} ({m.email})</option>
                ))}
              </select>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-1.5">
                {t('customers.tags.mailboxHint')}
              </p>
            </div>
          )}
          {editingId && form.mailboxId !== null && (
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1.5">{t('customers.common.relatedMailbox')}</label>
              <div className="px-3 py-2 bg-gray-50 dark:bg-slate-900 border border-gray-200 dark:border-slate-700 rounded-lg text-sm text-gray-700 dark:text-slate-300">
                {getMailboxName(form.mailboxId)}
                <span className="text-xs text-gray-400 dark:text-slate-500 ml-2">{t('customers.tags.mailboxEditHint')}</span>
              </div>
            </div>
          )}
        </form>
      </Modal>

      {/* 删除确认 */}
      <ConfirmDialog
        open={!!crud.deleteTarget}
        title={t('customers.tags.deleteTitle')}
        message={t('customers.tags.deleteMessage', { name: crud.deleteTarget?.name ?? '' })}
        confirmText={t('customers.common.delete')}
        variant="danger"
        loading={deleting}
        onConfirm={handleDelete}
        onCancel={() => crud.setDeleteTarget(null)}
      />

      {/* 垃圾箱弹窗 */}
      <TagTrashModal
        open={crud.trash.open}
        onClose={() => crud.trash.setOpen(false)}
        query={crud.trash.query}
        onQueryChange={crud.trash.setQuery}
        list={crud.trash.list}
        loading={crud.trash.loading}
        count={crud.trash.count}
        actionLoading={crud.trash.actionLoading}
        restoreTarget={restoreTarget}
        onSetRestoreTarget={setRestoreTarget}
        hardDeleteTarget={hardDeleteTarget}
        onSetHardDeleteTarget={setHardDeleteTarget}
        clearTrashOpen={clearTrashOpen}
        onSetClearTrashOpen={setClearTrashOpen}
        onRestore={handleRestore}
        onHardDelete={handleHardDelete}
        onClearTrash={handleClearTrash}
        mailboxes={mailboxes}
      />

      {/* Toast */}
      {toast.message && (
        <div className="fixed top-4 right-4 z-50">
          <div
            className={`px-4 py-2 rounded-lg shadow-lg text-white text-sm ${
              toast.message.type === 'success' ? 'bg-green-500' : 'bg-red-500'
            }`}
          >
            {toast.message.text}
          </div>
        </div>
      )}
    </div>
  );
}
