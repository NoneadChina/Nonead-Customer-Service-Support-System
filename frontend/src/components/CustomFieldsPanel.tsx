import { useState, useEffect, useCallback, useRef } from 'react';
import { customFieldApi } from '../api/modules/automation';
import type { CustomFieldDto } from '../api/types/automation';
import { Check, Loader2, AlertCircle, Settings2 } from 'lucide-react';
import { useI18n } from '../context/I18nContext';

interface Props {
  entityType: 'conversation' | 'customer';
  entityId: number | null | undefined;
  /** 会话来源渠道（SourceType 枚举名小写，如 "wechat"）。仅 conversation 字段需要；customer 字段忽略此参数 */
  sourceType?: string | null;
  readOnly?: boolean;
  title?: string;
  /** compact = 侧边栏紧凑模式; full = 弹窗完整模式 */
  variant?: 'compact' | 'full';
}

// 字段类型对应的输入控件
function FieldInput({
  field,
  value,
  onChange,
  onBlur,
  compact,
}: {
  field: CustomFieldDto;
  value: string | null;
  onChange: (v: string) => void;
  onBlur: () => void;
  compact: boolean;
}) {
  const { t } = useI18n();
  const inputClass = compact
    ? 'w-full px-2 py-1 text-xs border rounded-md focus:outline-none focus:ring-1 focus:ring-primary-400 bg-white dark:bg-slate-700 border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-200'
    : 'w-full px-3 py-2 text-sm border rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 bg-white dark:bg-slate-700 border-gray-200 dark:border-slate-600 text-gray-700 dark:text-slate-200';

  switch (field.type) {
    case 'textarea':
      return (
        <textarea
          value={value ?? ''}
          placeholder={field.placeholder ?? ''}
          onChange={(e) => onChange(e.target.value)}
          onBlur={onBlur}
          rows={compact ? 2 : 3}
          className={inputClass + ' resize-y'}
        />
      );
    case 'select':
      return (
        <select
          value={value ?? ''}
          onChange={(e) => { onChange(e.target.value); onBlur(); }}
          className={inputClass + ' cursor-pointer'}
        >
          <option value="">{t('customers.customFields.selectPlaceholder')}</option>
          {field.options.map((opt) => (
            <option key={opt} value={opt}>{opt}</option>
          ))}
        </select>
      );
    case 'date':
      return (
        <input
          type="date"
          value={value ?? ''}
          onChange={(e) => onChange(e.target.value)}
          onBlur={onBlur}
          className={inputClass}
        />
      );
    case 'checkbox':
      return (
        <label className="flex items-center gap-2 cursor-pointer">
          <input
            type="checkbox"
            checked={value === 'true' || value === '1'}
            onChange={(e) => { onChange(e.target.checked ? 'true' : 'false'); onBlur(); }}
            className="w-4 h-4 rounded border-gray-300 text-primary-600 focus:ring-primary-500 dark:border-slate-600 dark:bg-slate-700"
          />
          <span className="text-xs text-gray-600 dark:text-slate-400">
            {value === 'true' || value === '1' ? t('customers.customFields.yes') : t('customers.customFields.no')}
          </span>
        </label>
      );
    case 'number':
      return (
        <input
          type="number"
          value={value ?? ''}
          placeholder={field.placeholder ?? ''}
          onChange={(e) => onChange(e.target.value)}
          onBlur={onBlur}
          className={inputClass}
        />
      );
    default: // text
      return (
        <input
          type="text"
          value={value ?? ''}
          placeholder={field.placeholder ?? ''}
          onChange={(e) => onChange(e.target.value)}
          onBlur={onBlur}
          className={inputClass}
        />
      );
  }
}

export default function CustomFieldsPanel({
  entityType,
  entityId,
  sourceType,
  readOnly = false,
  title,
  variant = 'compact',
}: Props) {
  const { t } = useI18n();
  const [fields, setFields] = useState<CustomFieldDto[]>([]);
  const [values, setValues] = useState<Record<number, string | null>>({});
  const [originalValues, setOriginalValues] = useState<Record<number, string | null>>({});
  const [loading, setLoading] = useState(true);
  const [savingFieldId, setSavingFieldId] = useState<number | null>(null);
  const [savedFieldId, setSavedFieldId] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const isMountedRef = useRef(true);

  useEffect(() => {
    isMountedRef.current = true;
    return () => { isMountedRef.current = false; };
  }, []);

  // 加载字段定义（按 sourceType 过滤：conversation 字段需要，customer 字段不需要）
  const loadFields = useCallback(async () => {
    try {
      const result = await customFieldApi.getForEntity(
        entityType,
        entityType === 'conversation' ? sourceType : undefined,
      );
      if (!isMountedRef.current) return;
      // 按 sortOrder 排序
      result.sort((a, b) => a.sortOrder - b.sortOrder);
      setFields(result);
    } catch {
      if (!isMountedRef.current) return;
      setError(t('customers.customFields.panelLoadFieldsFailed'));
    }
  }, [entityType, sourceType, t]);

  // 加载已有值
  const loadValues = useCallback(async () => {
    if (!entityId || entityId <= 0) {
      setValues({});
      setOriginalValues({});
      return;
    }
    try {
      const result = await customFieldApi.getEntityValues(entityType, entityId);
      if (!isMountedRef.current) return;
      const map: Record<number, string | null> = {};
      for (const v of result) {
        map[v.customFieldId] = v.value;
      }
      // 为没有值的字段填充默认值
      for (const f of fields) {
        if (!(f.id in map)) {
          map[f.id] = f.defaultValue ?? null;
        }
      }
      setValues(map);
      setOriginalValues({ ...map });
    } catch {
      if (!isMountedRef.current) return;
      setError(t('customers.customFields.panelLoadValuesFailed'));
    }
  }, [entityType, entityId, fields, t]);

  // 初次加载字段定义
  useEffect(() => {
    setLoading(true);
    setError(null);
    loadFields().finally(() => {
      if (isMountedRef.current) setLoading(false);
    });
  }, [loadFields]);

  // 字段定义加载后或 entityId 变化时加载值
  useEffect(() => {
    loadValues();
  }, [loadValues]);

  // 保存单个字段值
  const saveField = useCallback(async (fieldId: number, value: string | null) => {
    if (!entityId || entityId <= 0) return;
    // 值未变化则不保存
    if (originalValues[fieldId] === value) return;
    setSavingFieldId(fieldId);
    setError(null);
    try {
      await customFieldApi.setEntityValue(entityType, entityId, fieldId, value);
      if (!isMountedRef.current) return;
      setOriginalValues((prev) => ({ ...prev, [fieldId]: value }));
      setSavedFieldId(fieldId);
      setTimeout(() => {
        if (isMountedRef.current) setSavedFieldId(null);
      }, 1500);
    } catch {
      if (!isMountedRef.current) return;
      setError(t('customers.customFields.panelSaveFailed'));
      // 回滚
      setValues((prev) => ({ ...prev, [fieldId]: originalValues[fieldId] ?? null }));
    } finally {
      if (isMountedRef.current) setSavingFieldId(null);
    }
  }, [entityType, entityId, originalValues, t]);

  const handleFieldChange = (fieldId: number, value: string) => {
    setValues((prev) => ({ ...prev, [fieldId]: value }));
  };

  const handleFieldBlur = (fieldId: number) => {
    saveField(fieldId, values[fieldId] ?? null);
  };

  if (loading) {
    return (
      <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4">
        <div className="flex items-center gap-2 text-xs text-gray-400 dark:text-slate-500">
          <Loader2 className="w-3.5 h-3.5 animate-spin" />
          {t('customers.customFields.panelLoading')}
        </div>
      </div>
    );
  }

  // 没有字段定义时不显示
  if (fields.length === 0) return null;

  const compact = variant === 'compact';

  return (
    <div className="bg-white dark:bg-slate-800 rounded-xl border border-gray-200 dark:border-slate-700 p-4">
      <h3 className={`font-semibold text-gray-900 dark:text-slate-100 mb-3 flex items-center gap-2 ${compact ? 'text-sm' : 'text-base'}`}>
        <Settings2 className={`text-primary-500 ${compact ? 'w-4 h-4' : 'w-5 h-5'}`} />
        {title ?? t('customers.customFields.title')}
      </h3>

      {error && (
        <div className="mb-2 text-xs text-red-600 dark:text-red-400 flex items-center gap-1">
          <AlertCircle className="w-3 h-3 flex-shrink-0" />
          {error}
        </div>
      )}

      {!entityId || entityId <= 0 ? (
        <div className="text-xs text-gray-400 dark:text-slate-500 italic">
          {t('customers.customFields.panelSaveFirst')}
        </div>
      ) : (
        <div className={compact ? 'space-y-3' : 'space-y-4'}>
          {fields.map((field) => {
            const value = values[field.id] ?? null;
            const isSaving = savingFieldId === field.id;
            const isSaved = savedFieldId === field.id;
            return (
              <div key={field.id}>
                <label className={`block mb-1 font-medium text-gray-700 dark:text-slate-300 flex items-center gap-1 ${compact ? 'text-xs' : 'text-sm'}`}>
                  {field.name}
                  {field.isRequired && <span className="text-red-500">*</span>}
                  {isSaving && <Loader2 className="w-3 h-3 animate-spin text-gray-400" />}
                  {isSaved && <Check className="w-3 h-3 text-green-500" />}
                </label>
                {readOnly ? (
                  <div className={`text-gray-700 dark:text-slate-200 ${compact ? 'text-xs' : 'text-sm'}`}>
                    {field.type === 'checkbox'
                      ? (value === 'true' || value === '1' ? t('customers.customFields.yes') : t('customers.customFields.no'))
                      : (value || <span className="text-gray-400 dark:text-slate-500">—</span>)}
                  </div>
                ) : (
                  <FieldInput
                    field={field}
                    value={value}
                    onChange={(v) => handleFieldChange(field.id, v)}
                    onBlur={() => handleFieldBlur(field.id)}
                    compact={compact}
                  />
                )}
                {field.helpText && (
                  <p className="mt-0.5 text-[10px] text-gray-400 dark:text-slate-500">{field.helpText}</p>
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
