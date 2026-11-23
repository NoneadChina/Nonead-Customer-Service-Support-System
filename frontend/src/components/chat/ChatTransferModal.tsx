import { useState, useEffect, useCallback } from 'react';
import Modal from '../Modal';
import ConfirmDialog from '../ConfirmDialog';
import { useI18n } from '../../context/I18nContext';
import {
  FileText,
  Mail,
  User,
  AlertCircle,
  ArrowRight,
} from 'lucide-react';

interface MailboxOption {
  id: number;
  name: string;
}

interface UserOption {
  id: number;
  fullName: string;
}

interface ChatTransferModalProps {
  open: boolean;
  onClose: () => void;
  onConfirm: (data: {
    mailboxId?: number | null;
    userId?: number | null;
    subject?: string | null;
  }) => Promise<void>;
  mailboxes: MailboxOption[];
  users: UserOption[];
  customerName?: string;
}

export default function ChatTransferModal({
  open,
  onClose,
  onConfirm,
  mailboxes,
  users,
  customerName,
}: ChatTransferModalProps) {
  const { t } = useI18n();
  const [mailboxId, setMailboxId] = useState<number | null>(null);
  const [userId, setUserId] = useState<number | null>(null);
  const [subject, setSubject] = useState('');
  const [showConfirm, setShowConfirm] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  // 打开时重置表单
  useEffect(() => {
    if (open) {
      setMailboxId(null);
      setUserId(null);
      setSubject('');
      setShowConfirm(false);
      setSubmitting(false);
    }
  }, [open]);

  const handleOpenConfirm = useCallback(() => {
    setShowConfirm(true);
  }, []);

  const handleConfirmTransfer = useCallback(async () => {
    try {
      setSubmitting(true);
      await onConfirm({
        mailboxId: mailboxId ?? undefined,
        userId: userId ?? undefined,
        subject: subject || undefined,
      });
      setShowConfirm(false);
      onClose();
    } catch {
      // 错误由父组件 toast 处理,这里仅关闭确认框
      setShowConfirm(false);
    } finally {
      setSubmitting(false);
    }
  }, [mailboxId, userId, subject, onConfirm, onClose]);

  return (
    <>
      <Modal
        open={open && !showConfirm}
        onClose={onClose}
        title={t('chat.transfer.title')}
        size="md"
      >
        <div className="space-y-5">
          {/* 说明条 */}
          <div className="flex items-start gap-3 p-3 bg-blue-50 dark:bg-blue-900/20 rounded-lg border border-blue-100 dark:border-blue-800/40">
            <AlertCircle className="w-5 h-5 text-blue-500 flex-shrink-0 mt-0.5" />
            <div className="flex-1">
              <p className="text-sm text-blue-700 dark:text-blue-300 font-medium">
                {t('chat.transfer.bannerTitle')}
              </p>
              <p className="text-xs text-blue-600 dark:text-blue-400 mt-1 leading-relaxed">
                {customerName
                  ? t('chat.transfer.descForCustomer', { name: customerName })
                  : t('chat.transfer.descCurrent')}
              </p>
            </div>
          </div>

          {/* 邮箱 */}
          <div>
            <label className="flex items-center gap-1.5 text-sm font-medium text-slate-700 dark:text-slate-200 mb-1.5">
              <Mail className="w-3.5 h-3.5 text-slate-400" />
              {t('chat.transfer.mailboxLabel')}
              <span className="text-xs text-slate-400 font-normal">
                {t('chat.transfer.optional')}
              </span>
            </label>
            <select
              value={mailboxId ?? ''}
              onChange={(e) => setMailboxId(e.target.value ? parseInt(e.target.value) : null)}
              className="w-full px-3 py-2.5 text-sm border border-slate-200 dark:border-slate-700 rounded-lg bg-white dark:bg-slate-800 text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            >
              <option value="">{t('chat.transfer.useOriginalMailbox')}</option>
              {mailboxes.map((m) => (
                <option key={m.id} value={m.id}>
                  {m.name}
                </option>
              ))}
            </select>
          </div>

          {/* 客服 */}
          <div>
            <label className="flex items-center gap-1.5 text-sm font-medium text-slate-700 dark:text-slate-200 mb-1.5">
              <User className="w-3.5 h-3.5 text-slate-400" />
              {t('chat.transfer.agentLabel')}
              <span className="text-xs text-slate-400 font-normal">
                {t('chat.transfer.optional')}
              </span>
            </label>
            <select
              value={userId ?? ''}
              onChange={(e) => setUserId(e.target.value ? parseInt(e.target.value) : null)}
              className="w-full px-3 py-2.5 text-sm border border-slate-200 dark:border-slate-700 rounded-lg bg-white dark:bg-slate-800 text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            >
              <option value="">{t('chat.transfer.unassignedOption')}</option>
              {users.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.fullName}
                </option>
              ))}
            </select>
          </div>

          {/* 主题 */}
          <div>
            <label className="flex items-center gap-1.5 text-sm font-medium text-slate-700 dark:text-slate-200 mb-1.5">
              <FileText className="w-3.5 h-3.5 text-slate-400" />
              {t('chat.transfer.subjectLabel')}
              <span className="text-xs text-slate-400 font-normal">
                {t('chat.transfer.optional')}
              </span>
            </label>
            <input
              type="text"
              value={subject}
              onChange={(e) => setSubject(e.target.value)}
              placeholder={t('chat.transfer.subjectPlaceholder')}
              maxLength={200}
              className="w-full px-3 py-2.5 text-sm border border-slate-200 dark:border-slate-700 rounded-lg bg-white dark:bg-slate-800 text-slate-900 dark:text-slate-100 placeholder-slate-400 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            />
          </div>

          {/* 操作按钮 */}
          <div className="flex justify-end gap-2 pt-2 border-t border-slate-100 dark:border-slate-700/60">
            <button
              onClick={onClose}
              className="px-4 py-2 text-sm font-medium text-slate-600 dark:text-slate-300 bg-white dark:bg-slate-700 border border-slate-200 dark:border-slate-600 rounded-lg hover:bg-slate-50 dark:hover:bg-slate-600 transition-colors"
            >
              {t('common.cancel')}
            </button>
            <button
              onClick={handleOpenConfirm}
              className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-white bg-gradient-to-r from-blue-500 to-indigo-600 rounded-lg hover:from-blue-600 hover:to-indigo-700 transition-all shadow-sm"
            >
              {t('chat.transfer.next')}
              <ArrowRight className="w-4 h-4" />
            </button>
          </div>
        </div>
      </Modal>

      <ConfirmDialog
        open={showConfirm}
        title={t('chat.transfer.confirmTitle')}
        message={t('chat.transfer.confirmMessage')}
        confirmText={
          submitting ? t('chat.transfer.converting') : t('chat.transfer.confirmAction')
        }
        variant="primary"
        onConfirm={handleConfirmTransfer}
        onCancel={() => setShowConfirm(false)}
      />
    </>
  );
}
