import { useState, useEffect } from 'react';
import Modal from './Modal';
import { useI18n } from '../context/I18nContext';
import type { TranslationKey } from '../locales/zh-Hans';
import type { TranslateParams } from '../i18n/types';
import { Eye, Play } from 'lucide-react';
import type { MacroDto, MacroPreviewResponse } from '../api/types';

/**
 * 与 `I18nContext` 的 `t` 签名一致；模块级纯函数需显式传入 `t`。
 * （类型导出不触发 `react-refresh/only-export-components`，无需抑制。）
 */
export type TranslateFn = (key: TranslationKey, params?: TranslateParams) => string;

/**
 * 宏动作类型 code → 文案 key。
 *
 * 后端 `MacroConst.ActionTypes` + `NormalizeActionType` 的合并结果：
 * 字典按**归一化后**的 code 查表，`status`/`assign`/`priority`/`reply` 这 4 个历史 code
 * 归一化后即 `set_status`/`assign_user`/`set_priority`/`send_reply`（开发库内 40 个宏确实含这些旧 code）。
 * 未知 code 回落原始 code（与后端 `GetValueOrDefault(..., a.Type)` 一致）。
 */
const MACRO_ACTION_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  set_status: 'automation.macroAction.setStatus',
  assign_user: 'automation.action.assignUser',
  set_priority: 'automation.action.setPriority',
  add_tag: 'automation.action.addTag',
  remove_tag: 'automation.action.removeTag',
  add_note: 'automation.macroAction.addNote',
  send_reply: 'automation.action.sendReply',
};

/** 后端 `MacroService.NormalizeActionType` 的等价实现（旧命名 → 新命名） */
function normalizeMacroActionType(type: string): string {
  switch (type) {
    case 'status':
      return 'set_status';
    case 'assign':
      return 'assign_user';
    case 'priority':
      return 'set_priority';
    case 'reply':
      return 'send_reply';
    default:
      return type;
  }
}

/** 会话状态 code → 文案 key（与 `format.status.*` 一致；未知 code 回落数字，同后端 `StatusName`） */
const STATUS_VALUE_KEYS: Record<number, TranslationKey> = {
  1: 'format.status.active',
  2: 'format.status.pending',
  3: 'format.status.closed',
  4: 'format.status.spam',
};

/** 优先级 code → 文案 key（未知 code 回落数字，同后端 `PriorityName`） */
const PRIORITY_VALUE_KEYS: Record<number, TranslationKey> = {
  0: 'format.priority.low',
  1: 'format.priority.normal',
  2: 'format.priority.high',
  3: 'format.priority.urgent',
};

/** 动作类型标签；未知 code 回落原始 code（与后端 `GetValueOrDefault(…, a.Type)` 一致） */
function macroActionLabel(t: TranslateFn, type: string): string {
  const key = MACRO_ACTION_LABEL_KEYS[normalizeMacroActionType(type)];
  return key ? t(key) : type;
}

/** 后端 `MacroService.GetValueString` 的等价实现 */
function stringifyActionValue(value: unknown): string {
  if (value === null || value === undefined) return '';
  if (typeof value === 'string') return value;
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  // 对象/数组：对应后端 JsonElement 分支的 `GetRawText()`
  return JSON.stringify(value) ?? '';
}

/** 后端 `MacroService.GetValueInt` 的等价实现 */
function actionValueAsInt(value: unknown): number {
  const parsed = Number.parseInt(stringifyActionValue(value), 10);
  return Number.isNaN(parsed) ? 0 : parsed;
}

/**
 * 宏动作的可读描述，前端组装版（对应后端 `MacroService.DescribeAction`）。
 * 后端下发的中文 `description` / `typeLabel` 不再渲染，故此处必须逐分支对齐后端语义。
 */
function describeMacroAction(t: TranslateFn, type: string, value: unknown): string {
  const normalized = normalizeMacroActionType(type);
  switch (normalized) {
    case 'set_status': {
      const v = actionValueAsInt(value);
      const key = STATUS_VALUE_KEYS[v];
      return t('automation.macros.preview.desc.setStatus', { value: key ? t(key) : String(v) });
    }
    case 'assign_user':
      return t('automation.macros.preview.desc.assignUser', { value: stringifyActionValue(value) });
    case 'set_priority': {
      const v = actionValueAsInt(value);
      const key = PRIORITY_VALUE_KEYS[v];
      return t('automation.macros.preview.desc.setPriority', { value: key ? t(key) : String(v) });
    }
    case 'add_tag':
      return t('automation.macros.preview.desc.addTag', { value: stringifyActionValue(value) });
    case 'remove_tag':
      return t('automation.macros.preview.desc.removeTag', { value: stringifyActionValue(value) });
    case 'add_note':
      return t('automation.macros.preview.desc.addNote', { count: stringifyActionValue(value).length });
    case 'send_reply':
      return t('automation.macros.preview.desc.sendReply', { count: stringifyActionValue(value).length });
    default:
      return t('automation.macros.preview.desc.fallback', { type, value: stringifyActionValue(value) });
  }
}

/**
 * 动作列表的可读描述串，前端组装版（对应后端 `MacroService.MapToDto` 的
 * `string.Join(" → ", actions.Select(DescribeAction))`）。分隔符与后端一致，
 * 故 `MacroDto.actionsDescription` 的渲染点可以整体换成它——**必须换**：
 * 后端那份是中文串，英文界面下会直出中文。
 *
 * 导出给 `Macros.tsx` 复用（宏列表的"动作预览"列 + 复制弹窗的"当前动作"），
 * 与预览弹窗共用同一套描述语义，避免两处各写一份而漂移。
 * 抑制理由：本模块同时导出组件与纯函数，`react-refresh` 建议拆文件；
 * 拆走会把"描述语义只此一份"打散，故就地抑制（与 `I18nContext.tsx` 同类处理）。
 */
// eslint-disable-next-line react-refresh/only-export-components -- 与预览弹窗共用同一份描述语义，拆文件反而会漂移
export function describeMacroActions(t: TranslateFn, actions: { type: string; value: unknown }[]): string {
  return actions.map((a) => describeMacroAction(t, a.type, a.value)).join(' → ');
}

export interface MacroPreviewModalProps {
  target: MacroDto | null;
  initialConversationId?: number;
  onClose: () => void;
  onPreview: (conversationId: number) => void;
  onExecute: (conversationId: number) => void;
  previewLoading: boolean;
  executeLoading: boolean;
  previewData: MacroPreviewResponse | null;
  executeResult: { success: boolean; message: string } | null;
}

export default function MacroPreviewModal({
  target,
  initialConversationId = 1,
  onClose,
  onPreview,
  onExecute,
  previewLoading,
  executeLoading,
  previewData,
  executeResult,
}: MacroPreviewModalProps) {
  const { t } = useI18n();
  const [conversationId, setConversationId] = useState<number>(initialConversationId);

  useEffect(() => {
    if (target) {
      setConversationId(initialConversationId);
    }
  }, [target, initialConversationId]);

  if (!target) return null;

  return (
    <Modal
      open
      title={t('automation.macros.preview.title', { name: target.name })}
      onClose={onClose}
      size="lg"
      footer={
        <>
          <button
            onClick={onClose}
            className="px-4 py-2 text-sm bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700"
          >
            {t('common.close')}
          </button>
          {previewData?.success && (
            <button
              onClick={() => onExecute(conversationId)}
              disabled={executeLoading || !previewData.success}
              className="px-4 py-2 text-sm text-white bg-emerald-600 rounded-lg hover:bg-emerald-700 disabled:opacity-50 flex items-center gap-1.5"
            >
              <Play className="w-4 h-4" />
              {executeLoading ? t('automation.macros.preview.executing') : t('automation.macros.preview.execute')}
            </button>
          )}
        </>
      }
    >
      <div className="space-y-4">
        <div className="bg-blue-50 dark:bg-slate-800/60 border border-blue-200 dark:border-slate-700 rounded-lg p-3 text-sm text-blue-800 dark:text-slate-300">
          {t('automation.macros.preview.hint')}
        </div>

        <div className="grid grid-cols-3 gap-3 items-end">
          <div>
            <label className="block text-xs font-medium text-gray-500 dark:text-slate-400 mb-1">{t('automation.macros.preview.conversationIdLabel')}</label>
            <input
              type="number"
              min={1}
              value={conversationId}
              onChange={(e) => {
                setConversationId(Number(e.target.value) || 0);
              }}
              className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
            />
          </div>
          <div className="col-span-2 flex items-end gap-2">
            <button
              onClick={() => onPreview(conversationId)}
              disabled={previewLoading || !conversationId}
              className="px-4 py-2 text-sm text-white bg-primary-600 rounded-lg hover:bg-primary-700 disabled:opacity-50 flex items-center gap-1.5"
            >
              <Eye className="w-4 h-4" />
              {previewLoading ? t('common.shell.loading') : t('automation.macros.preview.generate')}
            </button>
            {previewData?.success && (
              <button
                onClick={() => onExecute(conversationId)}
                disabled={executeLoading}
                className="px-4 py-2 text-sm text-white bg-emerald-600 rounded-lg hover:bg-emerald-700 disabled:opacity-50 flex items-center gap-1.5"
              >
                <Play className="w-4 h-4" />
                {executeLoading ? t('automation.macros.preview.executing') : t('automation.macros.preview.execute')}
              </button>
            )}
          </div>
        </div>

        {executeResult && (
          <div className={`p-3 rounded-lg text-sm ${executeResult.success ? 'bg-green-50 dark:bg-green-900/20 text-green-700 dark:text-green-400 border border-green-200 dark:border-green-800' : 'bg-red-50 dark:bg-red-900/20 text-red-700 dark:text-red-400 border border-red-200 dark:border-red-800'}`}>
            {executeResult.message}
          </div>
        )}

        {previewData && (
          <div className="space-y-3">
            {previewData.success ? (
              <>
                <div className="text-sm text-gray-700 dark:text-slate-300">
                  {t('automation.macros.preview.subjectPrefix')}<span className="font-medium">{previewData.conversationSubject || `#${previewData.conversationId}`}</span>
                </div>
                <div className="border border-gray-200 dark:border-slate-700 rounded-lg overflow-hidden">
                  <div className="bg-gray-50 dark:bg-slate-800 px-3 py-2 text-xs font-medium text-gray-500 dark:text-slate-400 uppercase flex items-center justify-between">
                    <span>{t('automation.macros.preview.actionCount', { count: previewData.actionCount })}</span>
                    <span className="text-primary-600">{target.name}</span>
                  </div>
                  <ol className="divide-y divide-gray-100 dark:divide-slate-700">
                    {previewData.actions.map((a, idx) => (
                      <li key={idx} className="px-3 py-2.5 flex items-start gap-3 hover:bg-gray-50 dark:hover:bg-slate-700/50 transition-colors">
                        <span className="flex-shrink-0 w-7 h-7 rounded-full bg-primary-100 dark:bg-primary-900/30 text-primary-700 dark:text-primary-400 flex items-center justify-center text-xs font-bold">
                          {idx + 1}
                        </span>
                        <div className="flex-1 min-w-0">
                          <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{macroActionLabel(t, a.type)}</div>
                          <div className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">{describeMacroAction(t, a.type, a.value)}</div>
                        </div>
                      </li>
                    ))}
                  </ol>
                </div>
              </>
            ) : (
              <div className="p-4 text-center text-red-600 bg-red-50 dark:bg-red-900/20 rounded-lg border border-red-200 dark:border-red-800">
                {previewData.message}
              </div>
            )}
          </div>
        )}
      </div>
    </Modal>
  );
}
