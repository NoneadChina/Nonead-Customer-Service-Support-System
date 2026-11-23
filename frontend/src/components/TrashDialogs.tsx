import ConfirmDialog from './ConfirmDialog';
import { useI18n } from '../context/I18nContext';

interface TrashDialogsProps<T> {
  deleteTarget: T | null;
  restoreTarget: T | null;
  hardDeleteTarget: T | null;
  clearTrashOpen: boolean;
  onDelete: () => void;
  onRestore: () => void;
  onHardDelete: () => void;
  onClearTrash: () => void;
  onCancelDelete: () => void;
  onCancelRestore: () => void;
  onCancelHardDelete: () => void;
  onCancelClearTrash: () => void;
  getItemName?: (item: T) => string;
  /**
   * 被操作对象的名词短语（如「自定义字段」/「API 密钥」），由调用方以**当前语言**传入。
   * 省略时回落到字典里的通用名词（`customers.common.trash.defaultItem`）。
   * 注意：本组件只本地化**句式**，名词本身仍是调用方的文案——调用方若尚未本地化，
   * 英文态下会出现「英文句式 + 中文名词」的混排（批次 2 后续波次处理那些页面时会自然消失）。
   */
  itemLabel?: string;
  useIdMode?: boolean;
}

export default function TrashDialogs<T>({
  deleteTarget,
  restoreTarget,
  hardDeleteTarget,
  clearTrashOpen,
  onDelete,
  onRestore,
  onHardDelete,
  onClearTrash,
  onCancelDelete,
  onCancelRestore,
  onCancelHardDelete,
  onCancelClearTrash,
  getItemName,
  itemLabel,
  useIdMode = false,
}: TrashDialogsProps<T>) {
  const { t } = useI18n();
  const item = itemLabel ?? t('customers.common.trash.defaultItem');

  const getTargetName = (target: T | null) => {
    if (!target) return '';
    if (useIdMode) return '';
    return getItemName?.(target) ?? '';
  };

  const showName = !useIdMode && typeof deleteTarget !== 'number';

  return (
    <>
      <ConfirmDialog
        open={!!deleteTarget}
        title={t('customers.common.trash.deleteTitle', { item })}
        message={showName
          ? t('customers.common.trash.deleteMessageNamed', { item, name: getTargetName(deleteTarget) })
          : t('customers.common.trash.deleteMessagePlain', { item })}
        confirmText={t('customers.common.trash.moveToTrash')}
        variant="warning"
        onConfirm={onDelete}
        onCancel={onCancelDelete}
      />

      <ConfirmDialog
        open={!!restoreTarget}
        title={t('customers.common.trash.restoreTitle', { item })}
        message={showName
          ? t('customers.common.trash.restoreMessageNamed', { item, name: getTargetName(restoreTarget) })
          : t('customers.common.trash.restoreMessagePlain', { item })}
        confirmText={t('customers.common.restore')}
        variant="info"
        onConfirm={onRestore}
        onCancel={onCancelRestore}
      />

      <ConfirmDialog
        open={!!hardDeleteTarget}
        title={t('customers.common.hardDelete')}
        message={showName
          ? t('customers.common.trash.hardDeleteMessageNamed', { item, name: getTargetName(hardDeleteTarget) })
          : t('customers.common.trash.hardDeleteMessagePlain', { item })}
        confirmText={t('customers.common.hardDelete')}
        variant="danger"
        onConfirm={onHardDelete}
        onCancel={onCancelHardDelete}
      />

      <ConfirmDialog
        open={clearTrashOpen}
        title={t('customers.common.clearTrash')}
        message={t('customers.common.trash.clearMessage', { item })}
        confirmText={t('customers.common.clearTrash')}
        variant="danger"
        onConfirm={onClearTrash}
        onCancel={onCancelClearTrash}
      />
    </>
  );
}
