import { useState, useEffect, useCallback, useMemo } from 'react';
import Modal from './Modal';
import ConfirmDialog from './ConfirmDialog';
import { customReportApi } from '../api';
import { useToast } from '../context/ToastContext';
import { useI18n } from '../context/I18nContext';
import { toIntlLocale } from '../i18n/locale';
import type { CustomReportScheduleDto, CreateScheduleRequest } from '../api/types';
import type { TranslationKey } from '../locales/zh-Hans';
import {
  Plus,
  Edit2,
  Trash2,
  Calendar,
  Clock,
  Mail,
  Save,
  X,
  CheckCircle2,
  XCircle,
  RefreshCw,
} from 'lucide-react';

interface ScheduleForm {
  recurrence: 'daily' | 'weekly' | 'monthly';
  weekDay: number | null;
  sendTime: string;
  recipientsText: string;
  subject: string;
  attachmentFormat: 'csv' | 'xlsx' | 'pdf';
  isActive: boolean;
}

/**
 * 表单默认值。**刻意不含 `subject`**：邮件主题是用户可见文案，必须按当前语言取词条，
 * 而模块级常量拿不到 `t()`——由组件内的 `emptyForm()` 补上（少写 subject 会被 tsc 拦下）。
 */
const EMPTY_FORM: Omit<ScheduleForm, 'subject'> = {
  recurrence: 'daily',
  weekDay: null,
  sendTime: '09:00',
  recipientsText: '',
  attachmentFormat: 'pdf',
  isActive: true,
};

/**
 * 频率标签。**只持有真源 key**，渲染处必须经 `t()` 解析——直接渲染会把原始 key 漏到界面。
 * 键名是后端稳定 code（daily/weekly/monthly），不要改。
 */
const RECURRENCE_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  daily: 'data.customReports.schedule.recurrence.daily',
  weekly: 'data.customReports.schedule.recurrence.weekly',
  monthly: 'data.customReports.schedule.recurrence.monthly',
};

/** 周几选项。`value` 是后端稳定 code（0-6），`labelKey` 只持有真源 key。 */
const WEEKDAY_OPTIONS: { value: number; labelKey: TranslationKey }[] = [
  { value: 0, labelKey: 'data.customReports.schedule.weekday.sun' },
  { value: 1, labelKey: 'data.customReports.schedule.weekday.mon' },
  { value: 2, labelKey: 'data.customReports.schedule.weekday.tue' },
  { value: 3, labelKey: 'data.customReports.schedule.weekday.wed' },
  { value: 4, labelKey: 'data.customReports.schedule.weekday.thu' },
  { value: 5, labelKey: 'data.customReports.schedule.weekday.fri' },
  { value: 6, labelKey: 'data.customReports.schedule.weekday.sat' },
];

interface Props {
  isOpen: boolean;
  onClose: () => void;
  reportId: number | null;
}

export default function CustomReportScheduleModal({ isOpen, onClose, reportId }: Props) {
  const toast = useToast();
  const { t, language } = useI18n();
  /**
   * 新建/重置表单：邮件主题填当前语言的默认主题。
   * 定义在组件内（而非模块级常量）是因为 `t` 只在组件里拿得到。
   */
  const emptyForm = (): ScheduleForm => ({
    ...EMPTY_FORM,
    subject: t('data.customReports.schedule.defaultSubject'),
  });
  const [schedules, setSchedules] = useState<CustomReportScheduleDto[]>([]);
  const [loading, setLoading] = useState(false);
  const [showForm, setShowForm] = useState(false);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [form, setForm] = useState<ScheduleForm>(() => emptyForm());
  const [saving, setSaving] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<CustomReportScheduleDto | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [togglingId, setTogglingId] = useState<number | null>(null);

  const loadSchedules = useCallback(async () => {
    if (reportId == null) return;
    setLoading(true);
    try {
      const data = await customReportApi.listSchedules(reportId);
      setSchedules(data);
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.customReports.schedule.toast.loadListFailed');
      toast.error(msg);
    } finally {
      setLoading(false);
    }
  }, [reportId, toast, t]);

  useEffect(() => {
    if (isOpen && reportId != null) {
      loadSchedules();
      setShowForm(false);
      setEditingId(null);
      setFormError(null);
    }
  }, [isOpen, reportId, loadSchedules]);

  const openNew = () => {
    setEditingId(null);
    setForm(emptyForm());
    setFormError(null);
    setShowForm(true);
  };

  const openEdit = (s: CustomReportScheduleDto) => {
    setEditingId(s.id);
    setForm({
      recurrence: s.recurrence,
      weekDay: s.weekDay ?? null,
      sendTime: s.sendTime,
      recipientsText: Array.isArray(s.recipients) ? s.recipients.join(', ') : '',
      subject: s.subject,
      attachmentFormat: s.attachmentFormat,
      isActive: s.isActive,
    });
    setFormError(null);
    setShowForm(true);
  };

  const cancelForm = () => {
    setShowForm(false);
    setEditingId(null);
    setFormError(null);
  };

  const parsedRecipients = useMemo(() => {
    if (!form.recipientsText.trim()) return [] as string[];
    return form.recipientsText
      .split(/[,，;；\s\n]+/)
      .map((s) => s.trim())
      .filter(Boolean);
  }, [form.recipientsText]);

  const validate = (): string | null => {
    if (form.recurrence === 'weekly' && (form.weekDay == null || form.weekDay < 0 || form.weekDay > 6)) {
      return t('data.customReports.schedule.validation.weekDayRequired');
    }
    if (!form.sendTime) return t('data.customReports.schedule.validation.sendTimeRequired');
    if (parsedRecipients.length === 0) return t('data.customReports.schedule.validation.recipientsRequired');
    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    const invalid = parsedRecipients.find((e) => !emailRegex.test(e));
    // 前缀键自带冒号与空格，动态值紧随其后（不塞进译文串）
    if (invalid) return `${t('data.customReports.schedule.validation.invalidEmailPrefix')}${invalid}`;
    if (!form.subject.trim()) return t('data.customReports.schedule.validation.subjectRequired');
    return null;
  };

  const handleSave = async () => {
    if (reportId == null) return;
    const err = validate();
    if (err) {
      setFormError(err);
      return;
    }
    setFormError(null);
    setSaving(true);
    try {
      const payload: CreateScheduleRequest = {
        recurrence: form.recurrence,
        weekDay: form.recurrence === 'weekly' ? (form.weekDay ?? 1) : null,
        sendTime: form.sendTime,
        recipients: JSON.stringify(parsedRecipients),
        subject: form.subject.trim(),
        attachmentFormat: form.attachmentFormat,
        isActive: form.isActive,
      };
      if (editingId != null) {
        await customReportApi.updateSchedule(editingId, payload);
        toast.success(t('data.customReports.schedule.toast.updateSucceeded'));
      } else {
        await customReportApi.createSchedule(reportId, payload);
        toast.success(t('data.customReports.schedule.toast.createSucceeded'));
      }
      setShowForm(false);
      setEditingId(null);
      loadSchedules();
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.customReports.schedule.toast.saveFailed');
      setFormError(msg);
    } finally {
      setSaving(false);
    }
  };

  const handleToggleActive = async (s: CustomReportScheduleDto) => {
    setTogglingId(s.id);
    try {
      const payload: CreateScheduleRequest = {
        recurrence: s.recurrence,
        weekDay: s.weekDay ?? null,
        sendTime: s.sendTime,
        recipients: JSON.stringify(Array.isArray(s.recipients) ? s.recipients : []),
        subject: s.subject,
        attachmentFormat: s.attachmentFormat,
        isActive: !s.isActive,
      };
      await customReportApi.updateSchedule(s.id, payload);
      toast.success(
        t(
          s.isActive
            ? 'data.customReports.schedule.status.disabled'
            : 'data.customReports.schedule.status.enabled',
        ),
      );
      loadSchedules();
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.customReports.schedule.toast.operationFailed');
      toast.error(msg);
    } finally {
      setTogglingId(null);
    }
  };

  const handleDelete = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      await customReportApi.deleteSchedule(deleteTarget.id);
      toast.success(t('data.customReports.schedule.toast.deleteSucceeded'));
      setDeleteTarget(null);
      loadSchedules();
    } catch (e: unknown) {
      const msg = e instanceof Error ? e.message : t('data.customReports.schedule.toast.deleteFailed');
      toast.error(msg);
    } finally {
      setDeleting(false);
    }
  };

  const formatRecurrence = (s: CustomReportScheduleDto) => {
    if (s.recurrence === 'weekly') {
      // 周几名称走字典；码越界/缺失时沿用原文的 `周{数字}` 回落（前缀键自带与数字的衔接）
      const wdKey = WEEKDAY_OPTIONS.find((w) => w.value === s.weekDay)?.labelKey;
      const wd = wdKey
        ? t(wdKey)
        : `${t('data.customReports.schedule.weekday.unknownPrefix')}${s.weekDay}`;
      return `${t('data.customReports.schedule.weeklySummaryPrefix')}${wd}`;
    }
    // 未知 recurrence 沿用后端原始 code（与改造前一致），不编造文案
    const recurrenceKey = RECURRENCE_LABEL_KEYS[s.recurrence];
    return recurrenceKey ? t(recurrenceKey) : s.recurrence;
  };

  return (
    <>
      <Modal
        open={isOpen}
        title={
          <div className="flex items-center gap-2">
            <Calendar className="w-5 h-5 text-indigo-600 dark:text-indigo-400" />
            <span>{t('data.customReports.schedule.title')}</span>
          </div>
        }
        onClose={onClose}
        size="lg"
      >
        <div className="space-y-4">
          <div className="flex items-center justify-between flex-wrap gap-2">
            <div className="text-sm text-slate-500 dark:text-slate-400">
              {t('data.customReports.schedule.description')}
            </div>
            <button
              onClick={openNew}
              className="inline-flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-white bg-gradient-to-r from-indigo-600 to-violet-600 hover:from-indigo-700 hover:to-violet-700 rounded-lg transition-all shadow-sm"
            >
              <Plus size={16} />
              {t('data.customReports.schedule.addSchedule')}
            </button>
          </div>

          {showForm && (
            <div className="bg-slate-50 dark:bg-slate-900/40 rounded-xl border border-indigo-100 dark:border-indigo-900/50 p-5 space-y-4">
              <div className="flex items-center justify-between">
                <h4 className="font-semibold text-slate-800 dark:text-slate-100 text-sm flex items-center gap-2">
                  {editingId != null ? (
                    <><Edit2 size={16} className="text-indigo-500" /> {t('data.customReports.schedule.editSchedule')}</>
                  ) : (
                    <><Plus size={16} className="text-indigo-500" /> {t('data.customReports.schedule.newSchedule')}</>
                  )}
                </h4>
                <button
                  onClick={cancelForm}
                  className="p-1.5 rounded-lg text-slate-400 hover:text-slate-600 hover:bg-white dark:hover:text-slate-300 dark:hover:bg-slate-800 transition-colors"
                >
                  <X size={16} />
                </button>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                <div>
                  <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-1.5">{t('data.customReports.schedule.form.recurrenceLabel')}</label>
                  <select
                    value={form.recurrence}
                    onChange={(e) => setForm({ ...form, recurrence: e.target.value as ScheduleForm['recurrence'], weekDay: e.target.value === 'weekly' ? (form.weekDay ?? 1) : null })}
                    className="w-full px-3 py-2 text-sm border border-slate-200 dark:border-slate-600 rounded-lg bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500/20"
                  >
                    <option value="daily">{t('data.customReports.schedule.recurrence.daily')}</option>
                    <option value="weekly">{t('data.customReports.schedule.recurrence.weekly')}</option>
                    <option value="monthly">{t('data.customReports.schedule.recurrence.monthly')}</option>
                  </select>
                </div>

                {form.recurrence === 'weekly' && (
                  <div>
                    <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-1.5">{t('data.customReports.schedule.form.weekDayLabel')}</label>
                    <select
                      value={form.weekDay ?? 1}
                      onChange={(e) => setForm({ ...form, weekDay: Number(e.target.value) })}
                      className="w-full px-3 py-2 text-sm border border-slate-200 dark:border-slate-600 rounded-lg bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500/20"
                    >
                      {WEEKDAY_OPTIONS.map((o) => (
                        <option key={o.value} value={o.value}>{t(o.labelKey)}</option>
                      ))}
                    </select>
                  </div>
                )}

                <div>
                  <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-1.5">
                    <Clock size={12} className="inline mr-1" />{t('data.customReports.schedule.form.sendTimeLabel')}
                  </label>
                  <input
                    type="time"
                    value={form.sendTime}
                    onChange={(e) => setForm({ ...form, sendTime: e.target.value })}
                    className="w-full px-3 py-2 text-sm border border-slate-200 dark:border-slate-600 rounded-lg bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500/20"
                  />
                </div>

                <div>
                  <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-1.5">{t('data.customReports.schedule.form.attachmentFormatLabel')}</label>
                  <select
                    value={form.attachmentFormat}
                    onChange={(e) => setForm({ ...form, attachmentFormat: e.target.value as ScheduleForm['attachmentFormat'] })}
                    className="w-full px-3 py-2 text-sm border border-slate-200 dark:border-slate-600 rounded-lg bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500/20"
                  >
                    <option value="pdf">{t('data.customReports.schedule.form.pdf')}</option>
                    <option value="xlsx">{t('data.customReports.schedule.form.xlsx')}</option>
                    <option value="csv">{t('data.customReports.schedule.form.csv')}</option>
                  </select>
                </div>
              </div>

              <div>
                <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-1.5">
                  <Mail size={12} className="inline mr-1" />{t('data.customReports.schedule.form.recipientsLabel')}
                </label>
                <textarea
                  value={form.recipientsText}
                  onChange={(e) => setForm({ ...form, recipientsText: e.target.value })}
                  placeholder={t('data.customReports.schedule.form.recipientsPlaceholder')}
                  rows={2}
                  className="w-full px-3 py-2 text-sm border border-slate-200 dark:border-slate-600 rounded-lg bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500/20 font-mono"
                />
                {parsedRecipients.length > 0 && (
                  <div className="flex flex-wrap items-center gap-1.5 mt-2">
                    {parsedRecipients.map((r, i) => (
                      <span key={i} className="text-xs px-2 py-0.5 rounded-full bg-indigo-50 dark:bg-indigo-900/30 text-indigo-700 dark:text-indigo-400">
                        {r}
                      </span>
                    ))}
                  </div>
                )}
              </div>

              <div>
                <label className="block text-xs font-medium text-slate-600 dark:text-slate-400 mb-1.5">{t('data.customReports.schedule.form.subjectLabel')}</label>
                <input
                  type="text"
                  value={form.subject}
                  onChange={(e) => setForm({ ...form, subject: e.target.value })}
                  className="w-full px-3 py-2 text-sm border border-slate-200 dark:border-slate-600 rounded-lg bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-indigo-500/20"
                />
              </div>

              <div className="flex items-center gap-2">
                <input
                  id="sch-active"
                  type="checkbox"
                  checked={form.isActive}
                  onChange={(e) => setForm({ ...form, isActive: e.target.checked })}
                  className="rounded text-indigo-600 focus:ring-indigo-500/20"
                />
                <label htmlFor="sch-active" className="text-sm text-slate-600 dark:text-slate-300 cursor-pointer">
                  {t('data.customReports.schedule.form.enableImmediately')}
                </label>
              </div>

              {formError && (
                <div className="text-sm text-rose-600 dark:text-rose-400 bg-rose-50 dark:bg-rose-900/30 px-3 py-2 rounded-lg">
                  {formError}
                </div>
              )}

              <div className="flex items-center justify-end gap-2 pt-2 border-t border-slate-200/60 dark:border-slate-700/50">
                <button
                  onClick={cancelForm}
                  className="px-4 py-2 text-sm font-medium text-slate-700 dark:text-slate-300 bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-600 rounded-lg hover:bg-slate-50 dark:hover:bg-slate-700 disabled:opacity-50"
                  disabled={saving}
                >
                  {t('common.cancel')}
                </button>
                <button
                  onClick={handleSave}
                  disabled={saving}
                  className="inline-flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-white bg-gradient-to-r from-indigo-600 to-violet-600 hover:from-indigo-700 hover:to-violet-700 rounded-lg disabled:opacity-50 transition-all shadow-sm"
                >
                  {saving ? <RefreshCw size={14} className="animate-spin" /> : <Save size={14} />}
                  {saving ? t('data.customReports.schedule.form.saving') : t('common.save')}
                </button>
              </div>
            </div>
          )}

          <div className="space-y-2">
            {loading ? (
              <div className="p-8 text-center text-slate-500 dark:text-slate-400">
                <div className="flex items-center justify-center gap-2">
                  <RefreshCw className="w-5 h-5 animate-spin" />
                  <span>{t('data.customReports.schedule.loadingList')}</span>
                </div>
              </div>
            ) : schedules.length === 0 ? (
              <div className="p-10 text-center text-slate-500 dark:text-slate-400 bg-slate-50/60 dark:bg-slate-900/20 rounded-xl border border-dashed border-slate-200 dark:border-slate-700">
                <Calendar className="w-10 h-10 mx-auto mb-3 text-slate-300 dark:text-slate-600" />
                <div className="font-medium text-slate-600 dark:text-slate-300 mb-1">{t('data.customReports.schedule.emptyTitle')}</div>
                <div className="text-sm">{t('data.customReports.schedule.emptyHint')}</div>
              </div>
            ) : (
              <div className="divide-y divide-slate-100 dark:divide-slate-700/50 border border-slate-100 dark:border-slate-700/50 rounded-xl overflow-hidden">
                {schedules.map((s) => (
                  <div key={s.id} className="flex items-start justify-between gap-3 p-4 hover:bg-slate-50/60 dark:hover:bg-slate-900/30 transition-colors">
                    <div className="flex-1 min-w-0">
                      <div className="flex items-center gap-2 flex-wrap">
                        <div className="flex items-center gap-1.5 text-sm font-medium text-slate-700 dark:text-slate-200">
                          <Calendar size={14} className="text-indigo-500" />
                          {formatRecurrence(s)}
                          <span className="text-slate-400 dark:text-slate-500">·</span>
                          <Clock size={14} className="text-slate-400" />
                          {s.sendTime}
                        </div>
                        {s.isActive ? (
                          <span className="inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-full bg-emerald-50 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400">
                            <CheckCircle2 size={11} /> {t('data.customReports.schedule.status.running')}
                          </span>
                        ) : (
                          <span className="inline-flex items-center gap-1 text-xs px-2 py-0.5 rounded-full bg-slate-100 text-slate-600 dark:bg-slate-700/50 dark:text-slate-400">
                            <XCircle size={11} /> {t('data.customReports.schedule.status.disabled')}
                          </span>
                        )}
                        <span className="text-xs px-2 py-0.5 rounded-full bg-slate-100 text-slate-600 dark:bg-slate-700/50 dark:text-slate-300 uppercase font-mono">
                          {s.attachmentFormat}
                        </span>
                      </div>
                      <div className="text-xs text-slate-500 dark:text-slate-400 mt-1.5 truncate">
                        {t('data.customReports.schedule.subjectPrefix')}{s.subject}
                      </div>
                      <div className="flex flex-wrap items-center gap-1 mt-1.5">
                        {Array.isArray(s.recipients) && s.recipients.slice(0, 4).map((r, i) => (
                          <span key={i} className="text-[11px] px-1.5 py-0.5 rounded bg-indigo-50 dark:bg-indigo-900/30 text-indigo-600 dark:text-indigo-400 font-mono truncate max-w-[160px]">
                            {r}
                          </span>
                        ))}
                        {Array.isArray(s.recipients) && s.recipients.length > 4 && (
                          <span className="text-[11px] px-1.5 py-0.5 rounded bg-slate-100 text-slate-500 dark:bg-slate-700/50 dark:text-slate-400">
                            +{s.recipients.length - 4}
                          </span>
                        )}
                      </div>
                      {(s.lastRunAt || s.nextRunAt) && (
                        <div className="flex flex-wrap items-center gap-3 text-[11px] text-slate-400 dark:text-slate-500 mt-1.5">
                          {s.lastRunAt && (
                            <span>{t('data.customReports.schedule.lastRunPrefix')}{new Date(s.lastRunAt).toLocaleString(toIntlLocale(language), { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' })}</span>
                          )}
                          {s.nextRunAt && (
                            <span>{t('data.customReports.schedule.nextRunPrefix')}{new Date(s.nextRunAt).toLocaleString(toIntlLocale(language), { month: '2-digit', day: '2-digit', hour: '2-digit', minute: '2-digit' })}</span>
                          )}
                        </div>
                      )}
                    </div>

                    <div className="flex flex-col items-end gap-1.5 flex-shrink-0">
                      <div className="flex items-center gap-1">
                        <button
                          onClick={() => handleToggleActive(s)}
                          disabled={togglingId === s.id}
                          title={s.isActive ? t('data.customReports.schedule.action.disable') : t('data.customReports.schedule.action.enable')}
                          className={`p-1.5 rounded-lg transition-colors ${
                            s.isActive
                              ? 'text-emerald-600 hover:bg-emerald-50 dark:text-emerald-400 dark:hover:bg-emerald-900/30'
                              : 'text-slate-400 hover:text-slate-600 hover:bg-slate-100 dark:hover:text-slate-300 dark:hover:bg-slate-700'
                          } disabled:opacity-50`}
                        >
                          {togglingId === s.id ? <RefreshCw size={14} className="animate-spin" /> : s.isActive ? <CheckCircle2 size={15} /> : <XCircle size={15} />}
                        </button>
                        <button
                          onClick={() => openEdit(s)}
                          title={t('common.edit')}
                          className="p-1.5 rounded-lg text-slate-400 hover:text-indigo-600 hover:bg-indigo-50 dark:hover:text-indigo-400 dark:hover:bg-indigo-900/30 transition-colors"
                        >
                          <Edit2 size={15} />
                        </button>
                        <button
                          onClick={() => setDeleteTarget(s)}
                          title={t('common.delete')}
                          className="p-1.5 rounded-lg text-slate-400 hover:text-rose-600 hover:bg-rose-50 dark:hover:text-rose-400 dark:hover:bg-rose-900/30 transition-colors"
                        >
                          <Trash2 size={15} />
                        </button>
                      </div>
                    </div>
                  </div>
                ))}
              </div>
            )}
          </div>
        </div>
      </Modal>

      <ConfirmDialog
        open={!!deleteTarget}
        title={t('data.customReports.schedule.deleteDialog.title')}
        message={t('data.customReports.schedule.deleteDialog.message')}
        confirmText={t('common.delete')}
        cancelText={t('common.cancel')}
        variant="danger"
        loading={deleting}
        onConfirm={handleDelete}
        onCancel={() => setDeleteTarget(null)}
      />
    </>
  );
}
