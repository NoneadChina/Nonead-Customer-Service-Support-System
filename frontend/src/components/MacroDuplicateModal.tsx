import { useState, useEffect } from 'react';
import Modal from './Modal';
import { useI18n } from '../context/I18nContext';
import { tBound } from '../i18n/locale';
import { describeMacroActions } from './MacroPreviewModal';
import type { MacroDto } from '../api/types';

export interface MacroDuplicateModalProps {
  target: MacroDto | null;
  onClose: () => void;
  onConfirm: (name: string) => void;
  loading: boolean;
}

export default function MacroDuplicateModal({ target, onClose, onConfirm, loading }: MacroDuplicateModalProps) {
  const { t } = useI18n();
  const [name, setName] = useState('');

  useEffect(() => {
    if (target) {
      // 这里用 `tBound` 而不是把 `t` 放进依赖数组：DB 覆盖层加载完成会换掉 `t` 的身份，
      // 若进依赖本 effect 会重跑，把用户已经输入的新名称覆盖回默认值。
      setName(target.name + tBound('automation.macros.copySuffix'));
    } else {
      setName('');
    }
  }, [target]);

  if (!target) return null;

  return (
    <Modal
      open
      title={t('automation.macros.duplicate.title')}
      onClose={onClose}
      size="md"
      footer={
        <>
          <button
            onClick={onClose}
            disabled={loading}
            className="px-4 py-2 text-sm bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded-lg hover:bg-gray-50 dark:hover:bg-slate-700 disabled:opacity-50"
          >
            {t('common.cancel')}
          </button>
          <button
            onClick={() => onConfirm(name.trim())}
            disabled={loading || !name.trim()}
            className="px-4 py-2 text-sm text-white bg-emerald-600 rounded-lg hover:bg-emerald-700 disabled:opacity-50"
          >
            {loading ? t('automation.macros.duplicate.loading') : t('automation.macros.duplicate.confirm')}
          </button>
        </>
      }
    >
      <div className="space-y-3">
        <div className="text-sm text-gray-600 dark:text-slate-300">
          {t('automation.macros.duplicate.introPrefix')}
          <span className="font-medium">{target.name}</span>
          {t('automation.macros.duplicate.introSuffix')}
        </div>
        <div>
          <label className="block text-sm font-medium text-gray-700 dark:text-slate-300 mb-1">{t('automation.macros.duplicate.nameLabel')}</label>
          <input
            type="text"
            value={name}
            onChange={(e) => setName(e.target.value)}
            className="w-full px-3 py-2 text-sm border border-gray-300 dark:border-slate-600 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-800 dark:text-slate-200"
          />
        </div>
        <div className="text-xs text-gray-500 dark:text-slate-400">
          {t('automation.macros.duplicate.currentActionsPrefix')}
          {target.actions.length > 0
            ? describeMacroActions(t, target.actions)
            : t('automation.common.actionCount', { count: target.actions.length })}
        </div>
      </div>
    </Modal>
  );
}
