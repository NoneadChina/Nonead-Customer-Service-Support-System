import Modal from './Modal';
import { AlertCircle, AlertTriangle, Info, CheckCircle2, Loader2 } from 'lucide-react';
import { useI18n } from '../context/I18nContext';

interface ConfirmDialogProps {
  open: boolean;
  title?: string;
  message: string;
  confirmText?: string;
  cancelText?: string;
  variant?: 'danger' | 'warning' | 'info' | 'primary';
  onConfirm: () => void;
  onCancel: () => void;
  loading?: boolean;
  error?: string;
  children?: React.ReactNode;
}

const variantStyles = {
  danger: {
    headerBg: 'from-red-50 via-rose-50 to-pink-50 dark:from-red-950/40 dark:via-rose-950/30 dark:to-pink-950/30',
    headerBorder: 'border-red-100/70 dark:border-red-900/30',
    iconWrap: 'bg-gradient-to-br from-red-500/20 to-rose-500/20 dark:from-red-500/30 dark:to-rose-500/30 text-red-600 dark:text-red-400 ring-2 ring-red-500/30 shadow-lg shadow-red-500/20',
    btn: 'bg-gradient-to-br from-red-600 via-rose-600 to-pink-600 hover:from-red-700 hover:via-rose-700 hover:to-pink-700 disabled:from-red-300 disabled:via-rose-300 disabled:to-pink-300 shadow-xl shadow-red-500/30 hover:shadow-2xl hover:shadow-red-500/40 hover:-translate-y-0.5',
    accentText: 'text-red-700 dark:text-red-300',
    iconPulse: 'after:bg-red-500/40',
  },
  warning: {
    headerBg: 'from-amber-50 via-orange-50 to-yellow-50 dark:from-amber-950/40 dark:via-orange-950/30 dark:to-yellow-950/30',
    headerBorder: 'border-amber-100/70 dark:border-amber-900/30',
    iconWrap: 'bg-gradient-to-br from-amber-500/20 to-orange-500/20 dark:from-amber-500/30 dark:to-orange-500/30 text-amber-600 dark:text-amber-400 ring-2 ring-amber-500/30 shadow-lg shadow-amber-500/20',
    btn: 'bg-gradient-to-br from-amber-600 via-orange-600 to-yellow-600 hover:from-amber-700 hover:via-orange-700 hover:to-yellow-700 disabled:from-amber-300 disabled:via-orange-300 disabled:to-yellow-300 shadow-xl shadow-amber-500/30 hover:shadow-2xl hover:shadow-amber-500/40 hover:-translate-y-0.5',
    accentText: 'text-amber-700 dark:text-amber-300',
    iconPulse: 'after:bg-amber-500/40',
  },
  info: {
    headerBg: 'from-blue-50 via-sky-50 to-indigo-50 dark:from-blue-950/40 dark:via-sky-950/30 dark:to-indigo-950/30',
    headerBorder: 'border-blue-100/70 dark:border-blue-900/30',
    iconWrap: 'bg-gradient-to-br from-blue-500/20 to-indigo-500/20 dark:from-blue-500/30 dark:to-indigo-500/30 text-blue-600 dark:text-blue-400 ring-2 ring-blue-500/30 shadow-lg shadow-blue-500/20',
    btn: 'bg-gradient-to-br from-blue-600 via-indigo-600 to-sky-600 hover:from-blue-700 hover:via-indigo-700 hover:to-sky-700 disabled:from-blue-300 disabled:via-indigo-300 disabled:to-sky-300 shadow-xl shadow-blue-500/30 hover:shadow-2xl hover:shadow-blue-500/40 hover:-translate-y-0.5',
    accentText: 'text-blue-700 dark:text-blue-300',
    iconPulse: 'after:bg-blue-500/40',
  },
  primary: {
    headerBg: 'from-emerald-50 via-teal-50 to-green-50 dark:from-emerald-950/40 dark:via-teal-950/30 dark:to-green-950/30',
    headerBorder: 'border-emerald-100/70 dark:border-emerald-900/30',
    iconWrap: 'bg-gradient-to-br from-emerald-500/20 to-teal-500/20 dark:from-emerald-500/30 dark:to-teal-500/30 text-emerald-600 dark:text-emerald-400 ring-2 ring-emerald-500/30 shadow-lg shadow-emerald-500/20',
    btn: 'bg-gradient-to-br from-emerald-600 via-teal-600 to-green-600 hover:from-emerald-700 hover:via-teal-700 hover:to-green-700 disabled:from-emerald-300 disabled:via-teal-300 disabled:to-green-300 shadow-xl shadow-emerald-500/30 hover:shadow-2xl hover:shadow-emerald-500/40 hover:-translate-y-0.5',
    accentText: 'text-emerald-700 dark:text-emerald-300',
    iconPulse: 'after:bg-emerald-500/40',
  },
};

const variantIcons = {
  danger: AlertCircle,
  warning: AlertTriangle,
  info: Info,
  primary: CheckCircle2,
};

export default function ConfirmDialog({
  open,
  title,
  message,
  confirmText,
  cancelText,
  variant = 'danger',
  onConfirm,
  onCancel,
  loading = false,
  error,
  children,
}: ConfirmDialogProps) {
  const { t } = useI18n();
  // 缺省文案在函数体内解析（不能用形参默认值：默认值在调用 `useI18n()` 之前求值）。
  // 调用方传了值的行为与改造前逐字一致，只有"没传"的少数调用点改为跟随界面语言。
  const dialogTitle = title ?? t('common.confirmAction');
  const okText = confirmText ?? t('common.confirm');
  const noText = cancelText ?? t('common.cancel');
  const styles = variantStyles[variant];
  const IconComp = variantIcons[variant];

  return (
    <Modal
      open={open}
      title={
        <span className="sr-only">{dialogTitle}</span>
      }
      onClose={onCancel}
      size="sm"
      footer={
        <div className="w-full flex items-center justify-end gap-2.5">
          <button
            onClick={onCancel}
            disabled={loading}
            className="group px-5 py-2.5 text-sm text-gray-700 dark:text-slate-300 bg-gradient-to-b from-white to-gray-50 dark:from-slate-700 dark:to-slate-800 border border-gray-200 dark:border-slate-600 rounded-xl hover:from-gray-50 hover:to-gray-100 dark:hover:from-slate-600 dark:hover:to-slate-700 transition-all duration-200 font-semibold disabled:opacity-50 shadow-sm hover:shadow-md hover:-translate-y-0.5 active:translate-y-0"
          >
            {noText}
          </button>
          <button
            onClick={onConfirm}
            disabled={loading}
            className={`px-6 py-2.5 text-sm text-white rounded-xl disabled:cursor-not-allowed disabled:opacity-60 transition-all duration-200 font-semibold active:scale-[0.97] ${styles.btn}`}
          >
            {loading ? (
              <span className="inline-flex items-center gap-1.5">
                <Loader2 className="w-4 h-4 animate-spin" />
                {t('common.processing')}
              </span>
            ) : (
              okText
            )}
          </button>
        </div>
      }
    >
      {/* 顶部视觉 Header 区 */}
      <div className={`-mx-6 -my-5 mb-5 -mt-5 px-6 pt-6 pb-5 bg-gradient-to-br ${styles.headerBg} border-b ${styles.headerBorder} relative overflow-hidden`}>
        {/* 背景装饰圈 */}
        <div className="absolute -top-20 -right-20 w-48 h-48 rounded-full opacity-40 blur-2xl pointer-events-none" style={{ background: variant === 'danger' ? 'radial-gradient(circle, #fecdd3 0%, transparent 70%)' : variant === 'warning' ? 'radial-gradient(circle, #fde68a 0%, transparent 70%)' : variant === 'info' ? 'radial-gradient(circle, #bfdbfe 0%, transparent 70%)' : 'radial-gradient(circle, #a7f3d0 0%, transparent 70%)' }} />
        <div className="relative flex items-start gap-4">
          <div className={`relative flex-shrink-0 w-14 h-14 rounded-2xl flex items-center justify-center ${styles.iconWrap} before:absolute before:inset-0 before:rounded-2xl before:animate-ping before:opacity-30 ${styles.iconPulse} before:-z-0`}>
            <IconComp className="relative z-10 w-7 h-7" strokeWidth={2.2} />
          </div>
          <div className="flex-1 pt-1 min-w-0">
            <p className="text-[11px] text-gray-500/90 dark:text-slate-400/80 mb-1 uppercase tracking-[0.15em] font-bold">{t('common.confirmHint')}</p>
            <h3 className={`text-xl font-black ${styles.accentText} tracking-tight leading-tight`}>{dialogTitle}</h3>
          </div>
        </div>
      </div>

      <div className="space-y-4">
        <div className="relative rounded-2xl bg-gray-50/60 dark:bg-slate-900/40 border border-gray-100 dark:border-slate-700/50 p-4">
          <p className="text-sm text-gray-700 dark:text-slate-200 leading-relaxed font-medium">
            {message}
          </p>
        </div>
        {children}
        {error && (
          <div className="text-sm text-red-700 dark:text-red-300 bg-gradient-to-r from-red-50 to-rose-50 dark:from-red-950/40 dark:to-rose-950/40 border border-red-200/70 dark:border-red-900/40 px-4 py-3 rounded-xl flex items-start gap-2.5 shadow-sm">
            <AlertCircle className="w-4.5 h-4.5 flex-shrink-0 mt-0.5" />
            <span className="font-medium">{error}</span>
          </div>
        )}
      </div>
    </Modal>
  );
}
