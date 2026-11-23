import { useState, useEffect } from 'react';
import { tagApi } from '../api';
import { genId } from '../utils/id';
import { getApiErrorMessage } from '../utils/error';
import { useI18n } from '../context/I18nContext';
import type { TranslationKey } from '../locales/zh-Hans';
import type { TranslateParams } from '../i18n/types';
import type {
  MacroDto,
  MacroActionDto,
  CreateMacroRequest,
  MailboxResponse,
  TagResponse,
} from '../api/types';
import Modal from './Modal';
import ConfirmDialog from './ConfirmDialog';
import { Plus, Star, Zap, Trash2 } from 'lucide-react';

type ActionValue = string | number;

/** 与 `I18nContext` 的 `t` 签名一致；模块级纯函数需显式传入 `t` */
type TranslateFn = (key: TranslationKey, params?: TranslateParams) => string;

/**
 * 宏动作类型选项。**只持有真源 key**（`value` + key），渲染处一律 `t(o.labelKey)`——
 * 直接渲染表里的值会把 `automation.…` 这样的原始 key 漏到界面上。
 * `value` 是提交给后端的稳定 code（对应 `MacroConst.ActionTypes` 的 7 个键，不得翻译）。
 */
const ACTION_TYPE_OPTIONS: { value: string; labelKey: TranslationKey }[] = [
  { value: 'set_status', labelKey: 'automation.macroAction.setStatus' },
  { value: 'assign_user', labelKey: 'automation.action.assignUser' },
  { value: 'set_priority', labelKey: 'automation.action.setPriority' },
  { value: 'add_tag', labelKey: 'automation.action.addTag' },
  { value: 'remove_tag', labelKey: 'automation.action.removeTag' },
  { value: 'add_note', labelKey: 'automation.macroAction.addNote' },
  { value: 'send_reply', labelKey: 'automation.action.sendReply' },
];

/** 会话状态 code（后端 `ConversationStatus`）→ 文案 key；`value` 是提交给后端的稳定数字 */
const STATUS_VALUE_OPTIONS: { value: number; labelKey: TranslationKey }[] = [
  { value: 1, labelKey: 'format.status.active' },
  { value: 2, labelKey: 'format.status.pending' },
  { value: 3, labelKey: 'format.status.closed' },
  { value: 4, labelKey: 'format.status.spam' },
];

/** 优先级 code（0=Low…3=Urgent）→ 文案 key */
const PRIORITY_VALUE_OPTIONS: { value: number; labelKey: TranslationKey }[] = [
  { value: 0, labelKey: 'format.priority.low' },
  { value: 1, labelKey: 'format.priority.normal' },
  { value: 2, labelKey: 'format.priority.high' },
  { value: 3, labelKey: 'format.priority.urgent' },
];

interface QuickTemplateAction {
  type: string;
  /** 静态值（数字等不随语言变化的值） */
  value?: string | number;
  /** 随界面语言变化的字符串值（标签名 / 备注正文）；存在时覆盖 `value` */
  valueKey?: TranslationKey;
}

interface QuickTemplate {
  nameKey: TranslationKey;
  descriptionKey: TranslationKey;
  actions: QuickTemplateAction[];
}

/**
 * 快速模板。
 *
 * `name`/`description` 会填入表单并随宏一起入库，标签名（`add_tag`/`remove_tag` 的 value）
 * 与备注正文（`add_note` 的 value）同样会持久化——它们都**随界面语言变化**（英文界面下
 * 生成的是英文标签名/英文备注），因此全部走字典，不保留中文字面量。
 * 注意：`add_tag` 的标签名在后端不存在时会**新建**同名标签（MacroService add_tag 分支），
 * 故这里不是"引用既有数据"，而是"生成数据"，语言随界面是正确语义。
 */
const QUICK_TEMPLATES: QuickTemplate[] = [
  {
    nameKey: 'automation.macros.tpl.closeResolved.name',
    descriptionKey: 'automation.macros.tpl.closeResolved.description',
    actions: [
      { type: 'set_status', value: 3 },
      { type: 'add_tag', valueKey: 'automation.macros.tpl.tagResolved' },
    ],
  },
  {
    nameKey: 'automation.macros.tpl.closeDuplicate.name',
    descriptionKey: 'automation.macros.tpl.closeDuplicate.description',
    actions: [
      { type: 'set_status', value: 3 },
      { type: 'add_tag', valueKey: 'automation.macros.tpl.tagDuplicate' },
    ],
  },
  {
    nameKey: 'automation.macros.tpl.closeUnresolvable.name',
    descriptionKey: 'automation.macros.tpl.closeUnresolvable.description',
    actions: [
      { type: 'set_status', value: 3 },
      { type: 'add_tag', valueKey: 'automation.macros.tpl.tagUnresolvable' },
    ],
  },
  {
    nameKey: 'automation.macros.tpl.markSpam.name',
    descriptionKey: 'automation.macros.tpl.markSpam.description',
    actions: [
      { type: 'set_status', value: 4 },
      { type: 'add_tag', valueKey: 'automation.macros.tpl.tagSpam' },
    ],
  },
  {
    nameKey: 'automation.macros.tpl.escalate.name',
    descriptionKey: 'automation.macros.tpl.escalate.description',
    actions: [
      { type: 'set_priority', value: 3 },
      { type: 'add_tag', valueKey: 'automation.macros.tpl.tagUrgent' },
      { type: 'set_status', value: 2 },
    ],
  },
  {
    nameKey: 'automation.macros.tpl.downgrade.name',
    descriptionKey: 'automation.macros.tpl.downgrade.description',
    actions: [
      { type: 'set_priority', value: 1 },
      { type: 'remove_tag', valueKey: 'automation.macros.tpl.tagUrgent' },
    ],
  },
  {
    nameKey: 'automation.macros.tpl.awaitCustomer.name',
    descriptionKey: 'automation.macros.tpl.awaitCustomer.description',
    actions: [
      { type: 'set_status', value: 2 },
      { type: 'add_tag', valueKey: 'automation.macros.tpl.tagAwaitCustomer' },
    ],
  },
  {
    nameKey: 'automation.macros.tpl.vip.name',
    descriptionKey: 'automation.macros.tpl.vip.description',
    actions: [
      { type: 'set_priority', value: 2 },
      { type: 'add_tag', valueKey: 'automation.macros.tpl.tagVip' },
    ],
  },
  {
    nameKey: 'automation.macros.tpl.reopen.name',
    descriptionKey: 'automation.macros.tpl.reopen.description',
    actions: [
      { type: 'set_status', value: 1 },
      { type: 'add_tag', valueKey: 'automation.macros.tpl.tagReopen' },
    ],
  },
  {
    nameKey: 'automation.macros.tpl.followUpNote.name',
    descriptionKey: 'automation.macros.tpl.followUpNote.description',
    actions: [
      { type: 'add_note', valueKey: 'automation.macros.tpl.followUpNote.body' },
    ],
  },
];

/** 把模板的动作按当前语言解析为可提交/可渲染的动作列表 */
function resolveTemplateActions(t: TranslateFn, tpl: QuickTemplate): MacroActionDto[] {
  return tpl.actions.map(({ type, value, valueKey }) => ({
    type,
    value: valueKey ? t(valueKey) : (value ?? ''),
  }));
}

export interface MacroEditorProps {
  editing: MacroDto | null;
  mailboxes: MailboxResponse[];
  users: { id: number; fullName: string }[];
  onSave: (data: CreateMacroRequest, id?: number) => Promise<void>;
  onCancel: () => void;
}

export default function MacroEditor({ editing, mailboxes, users, onSave, onCancel }: MacroEditorProps) {
  const { t } = useI18n();
  const [name, setName] = useState(editing?.name ?? '');
  const [description, setDescription] = useState(editing?.description ?? '');
  const [mailboxId, setMailboxId] = useState<number | null>(editing?.mailboxId ?? null);
  const [isActive, setIsActive] = useState(editing?.isActive ?? true);
  const [sortOrder, setSortOrder] = useState(editing?.sortOrder ?? 0);
  const [actions, setActions] = useState<MacroActionDto[]>(editing?.actions ?? []);
  const [actionKeys, setActionKeys] = useState<string[]>(() =>
    (editing?.actions ?? []).map(() => genId())
  );
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [showTemplates, setShowTemplates] = useState(false);
  const [overwriteConfirmOpen, setOverwriteConfirmOpen] = useState(false);
  const [pendingTemplate, setPendingTemplate] = useState<QuickTemplate | null>(null);
  const [tags, setTags] = useState<TagResponse[]>([]);
  const [tagsLoading, setTagsLoading] = useState(false);

  useEffect(() => {
    setTagsLoading(true);
    tagApi.list(mailboxId ?? undefined)
      .then((res) => {
        setTags(Array.isArray(res) ? res : []);
      })
      .catch((err: unknown) => {
        console.error('加载标签列表失败:', err);
        setTags([]);
      })
      .finally(() => setTagsLoading(false));
  }, [mailboxId]);

  const handleAddAction = () => {
    setActions((prev) => [...prev, { type: 'set_status', value: 3 }]);
    setActionKeys((prev) => [...prev, genId()]);
  };

  const handleRemoveAction = (index: number) => {
    setActions((prev) => prev.filter((_, i) => i !== index));
    setActionKeys((prev) => prev.filter((_, i) => i !== index));
  };

  const handleApplyTemplate = (tpl: QuickTemplate) => {
    if (actions.length > 0) {
      setPendingTemplate(tpl);
      setOverwriteConfirmOpen(true);
      return;
    }
    if (!name.trim()) setName(t(tpl.nameKey));
    if (!description.trim()) setDescription(t(tpl.descriptionKey));
    const resolved = resolveTemplateActions(t, tpl);
    setActions((prev) => [...prev, ...resolved]);
    setActionKeys((prev) => [...prev, ...resolved.map(() => genId())]);
    setShowTemplates(false);
  };

  const handleConfirmApplyTemplate = () => {
    if (!pendingTemplate) return;
    const tpl = pendingTemplate;
    if (!name.trim()) setName(t(tpl.nameKey));
    if (!description.trim()) setDescription(t(tpl.descriptionKey));
    const resolved = resolveTemplateActions(t, tpl);
    setActions((prev) => [...prev, ...resolved]);
    setActionKeys((prev) => [...prev, ...resolved.map(() => genId())]);
    setShowTemplates(false);
    setOverwriteConfirmOpen(false);
    setPendingTemplate(null);
  };

  const handleActionTypeChange = (index: number, type: string) => {
    setActions((prev) => {
      const next = [...prev];
      let defaultValue: ActionValue = '';
      switch (type) {
        case 'set_status':
          defaultValue = 3;
          break;
        case 'set_priority':
          defaultValue = 1;
          break;
        case 'assign_user':
          defaultValue = users[0]?.id ?? 1;
          break;
        case 'add_tag':
        case 'remove_tag':
          defaultValue = tags[0]?.name ?? '';
          break;
        case 'add_note':
          defaultValue = '';
          break;
        case 'send_reply':
          defaultValue = '';
          break;
        default:
          defaultValue = '';
      }
      next[index] = { type, value: defaultValue };
      return next;
    });
  };

  const handleActionValueChange = (index: number, value: ActionValue) => {
    setActions((prev) => {
      const next = [...prev];
      next[index] = { ...next[index], value };
      return next;
    });
  };

  const handleSubmit = async () => {
    if (!name.trim()) {
      setError(t('automation.macros.editor.nameRequired'));
      return;
    }
    setSaving(true);
    setError('');
    try {
      await onSave(
        {
          name: name.trim(),
          description: description.trim() || null,
          mailboxId,
          isActive,
          sortOrder,
          actions,
        },
        editing?.id,
      );
    } catch (e: unknown) {
      setError(getApiErrorMessage(e, t('automation.common.saveFailed')));
    } finally {
      setSaving(false);
    }
  };

  return (
    <>
      <Modal
        open
        title={editing ? t('automation.macros.editor.editTitle') : t('automation.macros.editor.createTitle')}
        onClose={onCancel}
        size="lg"
        footer={
          <>
            <button
              onClick={onCancel}
              disabled={saving}
              className="px-4 py-2 text-sm bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 dark:bg-slate-900 disabled:opacity-50"
            >
              {t('common.cancel')}
            </button>
            <button
              onClick={handleSubmit}
              disabled={saving}
              className="px-4 py-2 text-sm text-white bg-primary-600 rounded-lg hover:bg-primary-700 disabled:opacity-50"
            >
              {saving ? t('automation.common.saving') : t('common.save')}
            </button>
          </>
        }
      >
        <div className="space-y-4">
          {error && (
            <div className="p-3 text-sm text-red-700 bg-red-50 rounded-lg border border-red-200">{error}</div>
          )}

          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('automation.macros.editor.nameLabel')}</label>
            <input
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder={t('automation.macros.editor.namePlaceholder')}
              className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('automation.common.description')}</label>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder={t('automation.macros.editor.descriptionPlaceholder')}
              rows={2}
              className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 resize-none bg-white dark:bg-slate-800 dark:text-slate-200"
            />
          </div>

          <div className="grid grid-cols-2 gap-4">
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('automation.common.mailboxColumn')}</label>
              <select
                value={mailboxId ?? ''}
                onChange={(e) => setMailboxId(e.target.value ? Number(e.target.value) : null)}
                className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
              >
                <option value="">{t('automation.common.allMailboxes')}</option>
                {mailboxes.map((m) => (
                  <option key={m.id} value={m.id}>
                    {m.name}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('automation.common.sortOrder')}</label>
              <input
                type="number"
                value={sortOrder}
                onChange={(e) => setSortOrder(Number(e.target.value))}
                className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
              />
            </div>
          </div>

          <div className="flex items-center gap-3">
            <label className="flex items-center gap-2 cursor-pointer">
              <input
                type="checkbox"
                checked={isActive}
                onChange={(e) => setIsActive(e.target.checked)}
                className="rounded border-gray-300 dark:border-slate-600 text-primary-600 focus:ring-primary-500"
              />
              <span className="text-sm text-gray-700 dark:text-slate-300">{t('automation.macros.editor.enableMacro')}</span>
            </label>
          </div>

          <div>
            <div className="flex items-center justify-between mb-3">
              <div className="flex items-center gap-2">
                <Zap className="w-4 h-4 text-amber-500" />
                <label className="text-sm font-medium text-gray-700 dark:text-slate-300">{t('automation.macros.editor.actionsTitle')}</label>
              </div>
              <div className="flex items-center gap-3">
                <button
                  onClick={() => setShowTemplates((v) => !v)}
                  className="text-xs text-amber-600 hover:text-amber-700 flex items-center gap-1.5 font-medium"
                >
                  <Star className="w-3.5 h-3.5" />
                  {showTemplates ? t('automation.macros.editor.collapseTemplates') : t('automation.macros.editor.useTemplates')}
                </button>
                <button
                  onClick={handleAddAction}
                  className="text-xs text-primary-600 hover:text-primary-700 flex items-center gap-1.5 font-medium"
                >
                  <Plus className="w-3.5 h-3.5" />
                  {t('automation.macros.editor.addAction')}
                </button>
              </div>
            </div>

            {showTemplates && (
              <div className="mb-3 p-3 bg-amber-50 dark:bg-slate-800/60 border border-amber-200 dark:border-slate-700 rounded-lg">
                <div className="text-xs text-amber-700 dark:text-amber-300 mb-2 flex items-center gap-1">
                  <Star className="w-3.5 h-3.5" />
                  {t('automation.macros.editor.templateHint')}
                </div>
                <div className="grid grid-cols-2 gap-2">
                  {QUICK_TEMPLATES.map((tpl) => (
                    <button
                      key={tpl.nameKey}
                      onClick={() => handleApplyTemplate(tpl)}
                      className="text-left px-3 py-2 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded-lg hover:border-primary-400 hover:shadow-sm transition-all"
                    >
                      <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{t(tpl.nameKey)}</div>
                      <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5 line-clamp-2">{t(tpl.descriptionKey)}</div>
                      <div className="text-[10px] text-primary-600 dark:text-primary-400 mt-1">{t('automation.common.actionCount', { count: tpl.actions.length })}</div>
                    </button>
                  ))}
                </div>
              </div>
            )}

            <div className="space-y-3">
              {actions.length === 0 && (
                <div className="text-center py-6 text-sm text-gray-400 border border-dashed border-gray-300 dark:border-slate-600 rounded-lg bg-white dark:bg-slate-800">
                  {t('automation.macros.editor.noActions')}
                </div>
              )}
              {actions.map((action, index) => {
                const isTextareaAction = action.type === 'add_note' || action.type === 'send_reply';
                return (
                  <div
                    key={actionKeys[index] ?? index}
                    className={`p-3 bg-white dark:bg-slate-800 rounded-lg border border-gray-200 dark:border-slate-700 shadow-sm ${isTextareaAction ? 'space-y-2' : 'flex items-center gap-2'}`}
                  >
                    <div className={`flex items-center gap-2 ${isTextareaAction ? '' : 'flex-1 min-w-0'}`}>
                      <span className="text-xs text-gray-400 w-6 text-center font-medium flex-shrink-0">{index + 1}</span>
                      <select
                        value={action.type}
                        onChange={(e) => handleActionTypeChange(index, e.target.value)}
                        className="px-2 py-1.5 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 min-w-[120px] flex-shrink-0"
                      >
                        {ACTION_TYPE_OPTIONS.map((o) => (
                          <option key={o.value} value={o.value}>
                            {t(o.labelKey)}
                          </option>
                        ))}
                      </select>
                      {!isTextareaAction && (
                        <>
                          {action.type === 'set_status' && (
                            <select
                              value={Number(action.value)}
                              onChange={(e) => handleActionValueChange(index, Number(e.target.value))}
                              className="flex-1 px-2 py-1.5 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
                            >
                              {STATUS_VALUE_OPTIONS.map((o) => (
                                <option key={o.value} value={o.value}>
                                  {t(o.labelKey)}
                                </option>
                              ))}
                            </select>
                          )}
                          {action.type === 'set_priority' && (
                            <select
                              value={Number(action.value)}
                              onChange={(e) => handleActionValueChange(index, Number(e.target.value))}
                              className="flex-1 px-2 py-1.5 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
                            >
                              {PRIORITY_VALUE_OPTIONS.map((o) => (
                                <option key={o.value} value={o.value}>
                                  {t(o.labelKey)}
                                </option>
                              ))}
                            </select>
                          )}
                          {action.type === 'assign_user' && (
                            <select
                              value={Number(action.value)}
                              onChange={(e) => handleActionValueChange(index, Number(e.target.value))}
                              className="flex-1 px-2 py-1.5 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
                            >
                              {users.map((u) => (
                                <option key={u.id} value={u.id}>
                                  {u.fullName}
                                </option>
                              ))}
                            </select>
                          )}
                          {(action.type === 'add_tag' || action.type === 'remove_tag') && (
                            <select
                              value={String(action.value || '')}
                              onChange={(e) => handleActionValueChange(index, e.target.value)}
                              className="flex-1 px-2 py-1.5 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
                            >
                              <option value="">{t('automation.common.selectTag')}</option>
                              {tagsLoading && (
                                <option value="" disabled>{t('common.shell.loading')}</option>
                              )}
                              {!tagsLoading && tags.length === 0 && (
                                <option value="" disabled>{t('automation.common.noTags')}</option>
                              )}
                              {tags.map((tg) => (
                                <option key={tg.id} value={tg.name}>{tg.name}</option>
                              ))}
                            </select>
                          )}
                        </>
                      )}
                    </div>
                    {isTextareaAction && (
                      <textarea
                        value={String(action.value || '')}
                        onChange={(e) => handleActionValueChange(index, e.target.value)}
                        placeholder={action.type === 'add_note' ? t('automation.macros.editor.notePlaceholder') : t('automation.macros.editor.replyPlaceholder')}
                        rows={3}
                        className="w-full px-2 py-1.5 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200 resize-y"
                      />
                    )}
                    <button
                      onClick={() => handleRemoveAction(index)}
                      className={`p-1.5 text-gray-400 hover:text-red-500 hover:bg-red-50 rounded-lg transition-colors flex-shrink-0 ${isTextareaAction ? 'self-start' : ''}`}
                      title={t('automation.macros.editor.removeAction')}
                    >
                      <Trash2 className="w-4 h-4" />
                    </button>
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      </Modal>
      <ConfirmDialog
        open={overwriteConfirmOpen}
        title={t('automation.macros.editor.applyTemplateTitle')}
        message={t('automation.macros.editor.applyTemplateMessage')}
        confirmText={t('automation.macros.editor.continue')}
        variant="primary"
        onConfirm={handleConfirmApplyTemplate}
        onCancel={() => { setOverwriteConfirmOpen(false); setPendingTemplate(null); }}
      />
    </>
  );
}
