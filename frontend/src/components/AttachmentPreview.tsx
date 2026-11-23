import { useEffect, useState } from 'react';
import type { AttachmentResponse } from '../api/types';
import { attachmentApi } from '../api';
import { formatFileSize, isImage, isAudio, isVideo } from '../utils/format';
import { useI18n } from '../context/I18nContext';

interface Props {
  attachments: AttachmentResponse[];
  initialIndex: number;
  onClose: () => void;
}

export default function AttachmentPreview({ attachments, initialIndex, onClose }: Props) {
  const { t } = useI18n();
  const [index, setIndex] = useState(initialIndex);

  const current = attachments[index];

  // 键盘导航
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
      else if (e.key === 'ArrowLeft' && index > 0) setIndex(index - 1);
      else if (e.key === 'ArrowRight' && index < attachments.length - 1) setIndex(index + 1);
    };
    window.addEventListener('keydown', handler);
    document.body.style.overflow = 'hidden';
    return () => {
      window.removeEventListener('keydown', handler);
      document.body.style.overflow = '';
    };
  }, [index, attachments.length, onClose]);

  if (!current) return null;

  const image = isImage(current.mimeType);
  const audio = isAudio(current.mimeType);
  const video = isVideo(current.mimeType);
  const fileName = current.fileName || `attachment-${current.id}`;

  return (
    <div
      className="fixed inset-0 z-[60] bg-black/80 flex flex-col"
      onClick={onClose}
      role="dialog"
      aria-modal="true"
      aria-label={t('serviceOps.attachments.dialogAria')}
    >
      {/* 顶部工具栏 */}
      <div
        className="flex items-center justify-between px-4 py-3 text-white"
        onClick={(e) => e.stopPropagation()}
      >
        <div className="flex items-center gap-3 min-w-0">
          <svg className="w-5 h-5 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M7 21h10a2 2 0 002-2V9.414a1 1 0 00-.293-.707l-5.414-5.414A1 1 0 0012.586 3H7a2 2 0 00-2 2v14a2 2 0 002 2z" />
          </svg>
          <div className="min-w-0">
            <div className="text-sm font-medium truncate">{fileName}</div>
            <div className="text-xs text-gray-400">
              {formatFileSize(current.size)} · {current.mimeType || t('serviceOps.attachments.unknownType')}
            </div>
          </div>
        </div>
        <div className="flex items-center gap-2 flex-shrink-0">
          <a
            href={attachmentApi.downloadUrl(current.id)}
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center gap-1.5 px-3 py-1.5 text-sm bg-white/10 hover:bg-white/20 rounded-lg transition"
          >
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
            </svg>
            {t('serviceOps.attachments.download')}
          </a>
          <button
            onClick={onClose}
            className="p-1.5 hover:bg-white/10 rounded-lg transition"
            title={t('serviceOps.attachments.closeTitle')}
            aria-label={t('serviceOps.attachments.closeAria')}
          >
            <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M6 18L18 6M6 6l12 12" />
            </svg>
          </button>
        </div>
      </div>

      {/* 主预览区 */}
      <div
        className="flex-1 flex items-center justify-center p-4 relative"
        onClick={onClose}
      >
        {/* 左翻页 */}
        {index > 0 && (
          <button
            onClick={(e) => {
              e.stopPropagation();
              setIndex(index - 1);
            }}
            className="absolute left-4 p-2 text-white/70 hover:text-white hover:bg-white/10 rounded-full transition"
            title={t('serviceOps.attachments.prevTitle')}
            aria-label={t('serviceOps.attachments.prevAria')}
          >
            <svg className="w-8 h-8" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M15 19l-7-7 7-7" />
            </svg>
          </button>
        )}

        {/* 预览内容 */}
        <div onClick={(e) => e.stopPropagation()} className="max-w-[85vw] max-h-[80vh]">
          {image ? (
            <img
              src={attachmentApi.previewUrl(current.id, 1600, 1200)}
              alt={fileName}
              className="max-w-full max-h-[80vh] object-contain rounded-lg shadow-2xl"
            />
          ) : audio ? (
            <div className="bg-white dark:bg-slate-800 rounded-xl p-8 text-center max-w-md w-full">
              <div className="w-20 h-20 mx-auto mb-4 bg-purple-100 dark:bg-purple-900/40 rounded-2xl flex items-center justify-center">
                <svg className="w-10 h-10 text-purple-500 dark:text-purple-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M19.114 5.636a9 9 0 010 12.728M16.463 8.288a5.25 5.25 0 010 7.424M6.75 8.25l4.72-4.72a.75.75 0 011.28.53v15.88a.75.75 0 01-1.28.53l-4.72-4.72H4.51c-.88 0-1.704-.507-1.938-1.354A9.01 9.01 0 012.25 12c0-.83.112-1.633.322-2.396C2.806 8.756 3.63 8.25 4.51 8.25H6.75z" />
                </svg>
              </div>
              <h3 className="text-base font-semibold text-gray-900 dark:text-slate-100 mb-1">{fileName}</h3>
              <p className="text-sm text-gray-500 dark:text-slate-400 mb-4">
                {formatFileSize(current.size)} · {current.mimeType || t('serviceOps.attachments.unknownType')}
              </p>
              <audio
                controls
                className="w-full mb-4"
                preload="metadata"
              >
                <source src={attachmentApi.downloadUrl(current.id)} type={current.mimeType || 'audio/mpeg'} />
                {t('serviceOps.attachments.audioUnsupported')}
              </audio>
              <p className="text-xs text-gray-400 dark:text-slate-500">
                {t('serviceOps.attachments.audioHint')}
              </p>
              <a
                href={attachmentApi.downloadUrl(current.id)}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-1.5 px-4 py-2 mt-3 text-sm bg-primary-600 hover:bg-primary-700 text-white rounded-lg transition"
              >
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                </svg>
                {t('serviceOps.attachments.downloadFile')}
              </a>
            </div>
          ) : video ? (
            <div className="max-w-full max-h-[80vh] w-full flex flex-col items-center">
              <video
                controls
                className="max-w-full max-h-[70vh] rounded-lg shadow-2xl"
                preload="metadata"
              >
                <source src={attachmentApi.downloadUrl(current.id)} type={current.mimeType || 'video/mp4'} />
                {t('serviceOps.attachments.videoUnsupported')}
              </video>
              <div className="mt-3 text-white/70 text-sm flex items-center gap-3">
                <span>{fileName}</span>
                <span className="text-white/40">·</span>
                <span>{formatFileSize(current.size)}</span>
                <span className="text-white/40">·</span>
                <a
                  href={attachmentApi.downloadUrl(current.id)}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex items-center gap-1 text-white/80 hover:text-white underline"
                >
                  <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                  </svg>
                  {t('serviceOps.attachments.download')}
                </a>
              </div>
            </div>
          ) : (
            <div className="bg-white dark:bg-slate-800 rounded-xl p-12 text-center max-w-md">
              <div className="w-20 h-20 mx-auto mb-4 bg-gray-100 dark:bg-slate-700 rounded-2xl flex items-center justify-center">
                <svg className="w-10 h-10 text-gray-400 dark:text-slate-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M7 21h10a2 2 0 002-2V9.414a1 1 0 00-.293-.707l-5.414-5.414A1 1 0 0012.586 3H7a2 2 0 00-2 2v14a2 2 0 002 2z" />
                </svg>
              </div>
              <h3 className="text-base font-semibold text-gray-900 dark:text-slate-100 mb-1">{fileName}</h3>
              <p className="text-sm text-gray-500 dark:text-slate-400 mb-4">
                {formatFileSize(current.size)} · {current.mimeType || t('serviceOps.attachments.unknownType')}
              </p>
              <p className="text-xs text-gray-400 dark:text-slate-500 mb-5">{t('serviceOps.attachments.cannotPreview')}</p>
              <a
                href={attachmentApi.downloadUrl(current.id)}
                target="_blank"
                rel="noopener noreferrer"
                className="inline-flex items-center gap-1.5 px-4 py-2 text-sm bg-primary-600 hover:bg-primary-700 text-white rounded-lg transition"
              >
                <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                </svg>
                {t('serviceOps.attachments.downloadFile')}
              </a>
            </div>
          )}
        </div>

        {/* 右翻页 */}
        {index < attachments.length - 1 && (
          <button
            onClick={(e) => {
              e.stopPropagation();
              setIndex(index + 1);
            }}
            className="absolute right-4 p-2 text-white/70 hover:text-white hover:bg-white/10 rounded-full transition"
            title={t('serviceOps.attachments.nextTitle')}
            aria-label={t('serviceOps.attachments.nextAria')}
          >
            <svg className="w-8 h-8" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M9 5l7 7-7 7" />
            </svg>
          </button>
        )}
      </div>

      {/* 底部缩略图栏 */}
      {attachments.length > 1 && (
        <div
          className="px-4 py-3 flex items-center justify-center gap-2 overflow-x-auto"
          onClick={(e) => e.stopPropagation()}
        >
          {attachments.map((att, i) => (
            <button
              key={att.id}
              onClick={() => setIndex(i)}
              className={`flex-shrink-0 w-14 h-14 rounded-lg overflow-hidden border-2 transition ${
                i === index ? 'border-primary-400 scale-110' : 'border-white/20 opacity-60 hover:opacity-100'
              }`}
              aria-label={t('serviceOps.attachments.thumbnailAria', { index: i + 1, current: i === index ? t('serviceOps.attachments.thumbnailAriaCurrent') : '' })}
              aria-current={i === index}
            >
              {isImage(att.mimeType) ? (
                <img
                  src={attachmentApi.previewUrl(att.id, 80, 80)}
                  alt={att.fileName}
                  loading="lazy"
                  className="w-full h-full object-cover"
                />
              ) : isAudio(att.mimeType) ? (
                <div className="w-full h-full bg-purple-500/20 flex items-center justify-center">
                  <svg className="w-5 h-5 text-purple-300" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M19.114 5.636a9 9 0 010 12.728M16.463 8.288a5.25 5.25 0 010 7.424M6.75 8.25l4.72-4.72a.75.75 0 011.28.53v15.88a.75.75 0 01-1.28.53l-4.72-4.72H4.51c-.88 0-1.704-.507-1.938-1.354A9.01 9.01 0 012.25 12c0-.83.112-1.633.322-2.396C2.806 8.756 3.63 8.25 4.51 8.25H6.75z" />
                  </svg>
                </div>
              ) : isVideo(att.mimeType) ? (
                <div className="w-full h-full bg-orange-500/20 flex items-center justify-center">
                  <svg className="w-5 h-5 text-orange-300" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M15 10l4.553-2.276A1 1 0 0121 8.618v6.764a1 1 0 01-1.447.894L15 14M5 18h8a2 2 0 002-2V8a2 2 0 00-2-2H5a2 2 0 00-2 2v8a2 2 0 002 2z" />
                  </svg>
                </div>
              ) : (
                <div className="w-full h-full bg-white/10 flex items-center justify-center">
                  <svg className="w-5 h-5 text-white/70" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M7 21h10a2 2 0 002-2V9.414a1 1 0 00-.293-.707l-5.414-5.414A1 1 0 0012.586 3H7a2 2 0 00-2 2v14a2 2 0 002 2z" />
                  </svg>
                </div>
              )}
            </button>
          ))}
        </div>
      )}

      {/* 计数 */}
      {attachments.length > 1 && (
        <div className="absolute top-3 left-1/2 -translate-x-1/2 text-xs text-white/60 bg-black/40 px-2.5 py-1 rounded-full">
          {index + 1} / {attachments.length}
        </div>
      )}
    </div>
  );
}
