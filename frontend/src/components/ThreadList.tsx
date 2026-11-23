import { useState, useMemo, useRef, useEffect, type MouseEvent } from 'react';
import type { ThreadResponse, AttachmentResponse } from '../api/types';
import { ThreadType, ThreadState } from '../api/types';
import { formatDateTime, formatFileSize, isImage, isAudio, isVideo, sanitizeHtml, replaceEmojiShortcodes, relativeTime, isEmailAddress } from '../utils/format';
import { useI18n } from '../context/I18nContext';
import { BUILT_IN_ENTRIES, FALLBACK_LOCALE, getBoundLocale } from '../i18n/locale';
import { translate } from '../i18n/translate';
import type { TranslateParams } from '../i18n/types';
import type { TranslationKey } from '../locales/zh-Hans';
import { attachmentApi } from '../api';
import client from '../api/client';
import BenzAMRRecorder from 'benz-amr-recorder';
import AttachmentPreview from './AttachmentPreview';
import UserAvatar from './UserAvatar';
import SourceTypeIcon from './SourceTypeIcon';
import { MessageCircle as MessageCircleIcon, ChevronDown, ChevronUp } from 'lucide-react';

/** 带插值的取词（缺 key 失败安全）。`formatDateGroup` 是模块级纯函数，语言由 Provider 绑定，
 *  手法与 `pages/calendar/calendarUtils.ts` 的 `tParams` 一致。 */
function tParams(key: TranslationKey, params?: TranslateParams): string {
  const locale = getBoundLocale();
  return translate(BUILT_IN_ENTRIES[locale] ?? BUILT_IN_ENTRIES[FALLBACK_LOCALE], locale, key, params);
}

/** 微信语音消息内联播放器：支持播放/暂停、进度条、时长显示、键盘焦点可达
 *  浏览器原生 <audio> 不支持 AMR 格式，微信语音默认为 AMR，需用 BenzAMRRecorder 解码播放。
 *  MP3 等浏览器原生支持的格式仍用 <audio> 标签以获得更好的兼容性和性能。
 */
function InlineAudioPlayer({ attachmentId, fileName, size, mimeType }: { attachmentId: number; fileName: string; size: number; mimeType: string }) {
  const { t } = useI18n();
  const isAmr = mimeType.toLowerCase() === 'audio/amr';
  const audioRef = useRef<HTMLAudioElement | null>(null);
  const amrRef = useRef<BenzAMRRecorder | null>(null);
  const progressTimerRef = useRef<number | null>(null);
  const [playing, setPlaying] = useState(false);
  const [duration, setDuration] = useState(0);
  const [current, setCurrent] = useState(0);
  const [loadError, setLoadError] = useState(false);
  const [loading, setLoading] = useState(isAmr); // AMR 需要先下载 blob 再解码，有加载过程

  // AMR 播放器：用 axios 下载 blob（带认证 cookie）→ initWithBlob 解码
  useEffect(() => {
    if (!isAmr) return;

    let cancelled = false;
    const amr = new BenzAMRRecorder();
    amrRef.current = amr;

    amr.onPlay(() => { if (!cancelled) setPlaying(true); });
    amr.onPause(() => { if (!cancelled) setPlaying(false); });
    amr.onStop(() => { if (!cancelled) { setPlaying(false); setCurrent(0); } });
    amr.onEnded(() => { if (!cancelled) { setPlaying(false); setCurrent(0); } });

    // 用 axios 下载 blob（确保走拦截器，处理 401 刷新）
    client.get<Blob>(`/api/attachments/${attachmentId}/download`, { responseType: 'blob' })
      .then((resp) => resp.data as Blob)
      .then((blob) => {
        if (cancelled) return;
        return amr.initWithBlob(blob);
      })
      .then(() => {
        if (cancelled) return;
        setDuration(amr.getDuration() || 0);
        setLoading(false);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        console.error('AMR 语音加载失败：', err);
        setLoadError(true);
        setLoading(false);
      });

    // 进度轮询：BenzAMRRecorder 无 timeupdate 事件，用 setInterval 查询当前位置
    progressTimerRef.current = window.setInterval(() => {
      if (!cancelled && amrRef.current && amrRef.current.isPlaying()) {
        setCurrent(amrRef.current.getCurrentPosition());
      }
    }, 200);

    return () => {
      cancelled = true;
      if (progressTimerRef.current !== null) {
        window.clearInterval(progressTimerRef.current);
        progressTimerRef.current = null;
      }
      try { amr.destroy(); } catch { /* 忽略清理异常 */ }
      amrRef.current = null;
    };
   
  }, [isAmr, attachmentId]);

  // 非 AMR 格式：用原生 <audio> 标签
  useEffect(() => {
    if (isAmr) return;
    const audio = audioRef.current;
    if (!audio) return;
    const onTime = () => setCurrent(audio.currentTime);
    const onMeta = () => setDuration(audio.duration || 0);
    const onEnd = () => {
      setPlaying(false);
      setCurrent(0);
    };
    const onError = () => setLoadError(true);
    audio.addEventListener('timeupdate', onTime);
    audio.addEventListener('loadedmetadata', onMeta);
    audio.addEventListener('ended', onEnd);
    audio.addEventListener('error', onError);
    return () => {
      audio.removeEventListener('timeupdate', onTime);
      audio.removeEventListener('loadedmetadata', onMeta);
      audio.removeEventListener('ended', onEnd);
      audio.removeEventListener('error', onError);
    };
  }, [isAmr]);

  const toggle = () => {
    if (loadError || loading) return;

    if (isAmr) {
      const amr = amrRef.current;
      if (!amr) return;
      if (playing) {
        amr.pause();
      } else {
        // 从头播放或继续：AMR 库的 play() 会从头开始，pause 后用 resume()
        if (amr.isPaused()) {
          amr.resume();
        } else {
          amr.play();
        }
        setPlaying(true);
      }
      return;
    }

    const audio = audioRef.current;
    if (!audio) return;
    if (playing) {
      audio.pause();
      setPlaying(false);
    } else {
      void audio.play().then(() => setPlaying(true)).catch(() => {
        // 播放失败（如权限/浏览器自动播放限制）静默处理
      });
    }
  };

  const fmtTime = (s: number): string => {
    if (!Number.isFinite(s) || s <= 0) return '0:00';
    const m = Math.floor(s / 60);
    const sec = Math.floor(s % 60);
    return `${m}:${sec.toString().padStart(2, '0')}`;
  };

  const progressPct = duration > 0 ? (current / duration) * 100 : 0;
  const playButtonDisabled = loadError || loading;

  // 状态文案：加载中 / 加载失败 / 正常
  const statusText = loadError ? t('serviceOps.threads.audio.statusError') : loading ? t('serviceOps.threads.audio.statusLoading') : t('serviceOps.threads.audio.status');
  const ariaLabel = loadError
    ? t('serviceOps.threads.audio.ariaLoadFailed', { fileName })
    : loading ? t('serviceOps.threads.audio.ariaLoading', { fileName })
    : playing ? t('serviceOps.threads.audio.ariaPause', { fileName }) : t('serviceOps.threads.audio.ariaPlay', { fileName });

  return (
    <div className="group flex items-center gap-2 px-3 py-2 bg-white/60 dark:bg-slate-700/40 rounded-lg border border-gray-200/60 dark:border-slate-600/50 hover:border-green-300 dark:hover:border-green-600 transition">
      {!isAmr && <audio ref={audioRef} src={attachmentApi.downloadUrl(attachmentId)} preload="metadata" />}
      <button
        type="button"
        onClick={toggle}
        disabled={playButtonDisabled}
        aria-disabled={playButtonDisabled}
        className="w-8 h-8 bg-purple-100 dark:bg-purple-900/40 rounded-full flex items-center justify-center flex-shrink-0 hover:bg-purple-200 dark:hover:bg-purple-900/60 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-purple-400 focus-visible:ring-offset-1 dark:focus-visible:ring-purple-500 disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:bg-purple-100 dark:disabled:hover:bg-purple-900/40"
        aria-label={ariaLabel}
      >
        {playing ? (
          <svg className="w-4 h-4 text-purple-500 dark:text-purple-400" fill="currentColor" viewBox="0 0 24 24">
            <rect x="6" y="5" width="4" height="14" rx="1" />
            <rect x="14" y="5" width="4" height="14" rx="1" />
          </svg>
        ) : (
          <svg className="w-4 h-4 text-purple-500 dark:text-purple-400 ml-0.5" fill="currentColor" viewBox="0 0 24 24">
            <path d="M8 5v14l11-7z" />
          </svg>
        )}
      </button>
      <div className="flex-1 min-w-0">
        <div className="flex items-center justify-between gap-2 mb-1">
          <span className="text-xs font-medium text-gray-700 dark:text-slate-200 truncate">
            {statusText}
          </span>
          <span className="text-[10px] text-gray-400 dark:text-slate-500 flex-shrink-0 tabular-nums">
            {fmtTime(current)} / {fmtTime(duration)}
          </span>
        </div>
        {/* 进度条：含 sr-only 描述以便屏幕阅读器可读 */}
        <div
          className="h-1 bg-gray-200 dark:bg-slate-600 rounded-full overflow-hidden"
          role="progressbar"
          aria-valuemin={0}
          aria-valuemax={100}
          aria-valuenow={Math.round(progressPct)}
          aria-label={t('serviceOps.threads.audio.ariaProgress', { percent: Math.round(progressPct) })}
        >
          <div className="h-full bg-purple-400 dark:bg-purple-500 rounded-full transition-all duration-150" style={{ width: `${progressPct}%` }} />
        </div>
      </div>
      <span className="text-[10px] text-gray-400 dark:text-slate-500 flex-shrink-0">{formatFileSize(size)}</span>
    </div>
  );
}

/** 微信视频消息内联预览：带封面帧的播放按钮，点击后展开内联视频播放器，支持键盘焦点 */
function InlineVideoPlayer({ attachmentId, fileName, size }: { attachmentId: number; fileName: string; size: number }) {
  const { t } = useI18n();
  const [expanded, setExpanded] = useState(false);
  const videoRef = useRef<HTMLVideoElement | null>(null);
  const expandButtonRef = useRef<HTMLButtonElement | null>(null);

  // 收起时恢复焦点到展开按钮（符合 WCAG 焦点顺序）
  const handleCollapse = () => {
    setExpanded(false);
    requestAnimationFrame(() => expandButtonRef.current?.focus());
  };

  // 展开后支持 Esc 收起并把焦点还给触发按钮
  useEffect(() => {
    if (!expanded) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault();
        handleCollapse();
      }
    };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [expanded]);

  if (expanded) {
    return (
      <div className="mt-1 relative rounded-lg overflow-hidden border border-gray-200/60 dark:border-slate-600/50 bg-black max-w-md">
        <video
          ref={videoRef}
          src={attachmentApi.downloadUrl(attachmentId)}
          controls
          autoPlay
          className="w-full max-h-80 object-contain"
        />
        <button
          type="button"
          onClick={handleCollapse}
          className="absolute top-2 right-2 w-7 h-7 bg-black/60 hover:bg-black/80 text-white rounded-full flex items-center justify-center transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white focus-visible:ring-offset-1 focus-visible:ring-offset-black"
          aria-label={t('serviceOps.threads.video.collapse')}
          title={t('serviceOps.threads.video.collapseHint')}
        >
          <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2} strokeLinecap="round" strokeLinejoin="round">
            <line x1="18" y1="6" x2="6" y2="18" />
            <line x1="6" y1="6" x2="18" y2="18" />
          </svg>
        </button>
      </div>
    );
  }

  return (
    <button
      ref={expandButtonRef}
      type="button"
      onClick={() => setExpanded(true)}
      className="group flex items-center gap-2 px-3 py-2 bg-white/60 dark:bg-slate-700/40 rounded-lg border border-gray-200/60 dark:border-slate-600/50 hover:border-green-300 dark:hover:border-green-600 transition w-full text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-orange-400 focus-visible:ring-offset-1 dark:focus-visible:ring-orange-500"
      aria-label={t('serviceOps.threads.video.playAria', { fileName })}
      aria-expanded={expanded}
    >
      <div className="w-8 h-8 bg-orange-100 dark:bg-orange-900/40 rounded-lg flex items-center justify-center flex-shrink-0 group-hover:bg-orange-200 dark:group-hover:bg-orange-900/60 transition-colors">
        <svg className="w-4 h-4 text-orange-500 dark:text-orange-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M15 10l4.553-2.276A1 1 0 0121 8.618v6.764a1 1 0 01-1.447.894L15 14M5 18h8a2 2 0 002-2V8a2 2 0 00-2-2H5a2 2 0 00-2 2v8a2 2 0 002 2z" />
        </svg>
      </div>
      <div className="flex-1 min-w-0">
        <div className="text-xs font-medium text-gray-700 dark:text-slate-200 truncate">{t('serviceOps.threads.video.title')}</div>
        <div className="text-[10px] text-gray-400 dark:text-slate-500">{formatFileSize(size)}</div>
      </div>
      <svg className="w-3.5 h-3.5 text-gray-400 group-hover:text-orange-500 dark:text-slate-500 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
        <path strokeLinecap="round" strokeLinejoin="round" d="M14.752 11.168l-3.197-2.132A1 1 0 0010 9.87v4.263a1 1 0 001.555.832l3.197-2.132a1 1 0 000-1.664z" />
        <path strokeLinecap="round" strokeLinejoin="round" d="M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
    </button>
  );
}

/**
 * 判断 thread 是否为「聊天转工单」来源：body 内包含后端生成的 .chat-transcript 容器。
 * 使用 useMemo 避免重复计算。
 */
function isChatTranscriptBody(body: string | null | undefined): boolean {
  if (!body) return false;
  return body.includes('chat-transcript');
}

/**
 * 聊天转工单专用样式容器：通过 Tailwind 任意选择器匹配后端输出的语义 class，
 * 视觉效果与在线聊天页 ChatMessageBubble 保持一致，便于客服跨页面阅读。
 *
 * 后端生成的结构（ChatService.TransferToTicketAsync）：
 *   .chat-transcript
 *     h3 标题
 *     .chat-meta 元信息
 *     hr
 *     .chat-messages
 *       .chat-msg.chat-msg-customer / .chat-msg-agent / .chat-msg-system
 *         .chat-msg-header { .chat-time .chat-sender }
 *         .chat-content
 */
const chatTranscriptStyles = [
  // 外层容器
  '[&_.chat-transcript]:space-y-3',
  '[&_.chat-transcript]:text-sm',
  // 转工单标题
  '[&_.chat-transcript>h3]:text-base',
  '[&_.chat-transcript>h3]:font-semibold',
  '[&_.chat-transcript>h3]:text-slate-800',
  'dark:[&_.chat-transcript>h3]:text-slate-100',
  '[&_.chat-transcript>h3]:mb-2',
  // 元信息卡片
  '[&_.chat-meta]:rounded-xl',
  '[&_.chat-meta]:bg-slate-50',
  'dark:[&_.chat-meta]:bg-slate-700/40',
  '[&_.chat-meta]:p-3',
  '[&_.chat-meta]:border',
  '[&_.chat-meta]:border-slate-200/60',
  'dark:[&_.chat-meta]:border-slate-600/40',
  '[&_.chat-meta_strong]:font-semibold',
  '[&_.chat-meta_strong]:text-slate-700',
  'dark:[&_.chat-meta_strong]:text-slate-200',
  '[&_.chat-meta_p]:text-xs',
  '[&_.chat-meta_p]:text-slate-600',
  'dark:[&_.chat-meta_p]:text-slate-300',
  '[&_.chat-meta_p]:leading-relaxed',
  // 分隔线
  '[&_.chat-transcript>hr]:my-4',
  '[&_.chat-transcript>hr]:border-0',
  '[&_.chat-transcript>hr]:border-t',
  '[&_.chat-transcript>hr]:border-slate-200/70',
  'dark:[&_.chat-transcript>hr]:border-slate-600/50',
  // 消息列表
  '[&_.chat-messages]:space-y-4',
  '[&_.chat-messages]:pt-1',
  // ========== 通用 .chat-msg 布局 ==========
  '[&_.chat-msg]:flex',
  '[&_.chat-msg]:gap-2',
  '[&_.chat-msg]:items-start',
  // 系统消息：居中胶囊（与在线聊天页 system 消息一致）
  '[&_.chat-msg-system]:justify-center',
  '[&_.chat-msg-system]:my-2',
  '[&_.chat-msg-system_.chat-content]:inline-flex',
  '[&_.chat-msg-system_.chat-content]:items-center',
  '[&_.chat-msg-system_.chat-content]:gap-1.5',
  '[&_.chat-msg-system_.chat-content]:px-3',
  '[&_.chat-msg-system_.chat-content]:py-1.5',
  '[&_.chat-msg-system_.chat-content]:bg-slate-100',
  'dark:[&_.chat-msg-system_.chat-content]:bg-slate-700/60',
  '[&_.chat-msg-system_.chat-content]:text-slate-500',
  'dark:[&_.chat-msg-system_.chat-content]:text-slate-400',
  '[&_.chat-msg-system_.chat-content]:rounded-full',
  '[&_.chat-msg-system_.chat-content]:text-xs',
  '[&_.chat-msg-system_.chat-time]:hidden',
  // ========== 客户消息：左对齐，白底+浅边（与在线聊天客户气泡一致）==========
  '[&_.chat-msg-customer]:justify-start',
  '[&_.chat-msg-customer]:flex-row',
  // 模拟头像：用 customer 前缀符号位置通过伪类做视觉提示
  // 主体容器：左对齐列布局
  '[&_.chat-msg-customer]:flex-col',
  '[&_.chat-msg-customer_.chat-msg-header]:flex',
  '[&_.chat-msg-customer_.chat-msg-header]:items-center',
  '[&_.chat-msg-customer_.chat-msg-header]:gap-2',
  '[&_.chat-msg-customer_.chat-msg-header]:mb-1',
  '[&_.chat-msg-customer_.chat-sender]:text-xs',
  '[&_.chat-msg-customer_.chat-sender]:font-medium',
  '[&_.chat-msg-customer_.chat-sender]:text-slate-600',
  'dark:[&_.chat-msg-customer_.chat-sender]:text-slate-300',
  '[&_.chat-msg-customer_.chat-time]:text-[11px]',
  '[&_.chat-msg-customer_.chat-time]:text-slate-400',
  'dark:[&_.chat-msg-customer_.chat-time]:text-slate-500',
  // 内容气泡：白底+浅边，与 ChatMessageBubble 客户气泡风格完全对齐
  '[&_.chat-msg-customer_.chat-content]:max-w-[75%]',
  '[&_.chat-msg-customer_.chat-content]:whitespace-pre-wrap',
  '[&_.chat-msg-customer_.chat-content]:break-words',
  '[&_.chat-msg-customer_.chat-content]:leading-relaxed',
  '[&_.chat-msg-customer_.chat-content]:px-4',
  '[&_.chat-msg-customer_.chat-content]:py-2.5',
  '[&_.chat-msg-customer_.chat-content]:shadow-sm',
  '[&_.chat-msg-customer_.chat-content]:bg-white',
  'dark:[&_.chat-msg-customer_.chat-content]:bg-slate-800',
  '[&_.chat-msg-customer_.chat-content]:text-slate-900',
  'dark:[&_.chat-msg-customer_.chat-content]:text-slate-100',
  '[&_.chat-msg-customer_.chat-content]:rounded-2xl',
  '[&_.chat-msg-customer_.chat-content]:rounded-bl-md',
  '[&_.chat-msg-customer_.chat-content]:border',
  '[&_.chat-msg-customer_.chat-content]:border-slate-200/70',
  'dark:[&_.chat-msg-customer_.chat-content]:border-slate-700/60',
  // ========== 客服消息：右对齐，蓝靛渐变+白字（与在线聊天客服气泡一致）==========
  '[&_.chat-msg-agent]:justify-end',
  '[&_.chat-msg-agent]:flex-col',
  '[&_.chat-msg-agent]:items-end',
  '[&_.chat-msg-agent_.chat-msg-header]:flex',
  '[&_.chat-msg-agent_.chat-msg-header]:items-center',
  '[&_.chat-msg-agent_.chat-msg-header]:gap-2',
  '[&_.chat-msg-agent_.chat-msg-header]:mb-1',
  '[&_.chat-msg-agent_.chat-sender]:text-xs',
  '[&_.chat-msg-agent_.chat-sender]:font-medium',
  '[&_.chat-msg-agent_.chat-sender]:text-slate-600',
  'dark:[&_.chat-msg-agent_.chat-sender]:text-slate-300',
  '[&_.chat-msg-agent_.chat-time]:text-[11px]',
  '[&_.chat-msg-agent_.chat-time]:text-slate-400',
  'dark:[&_.chat-msg-agent_.chat-time]:text-slate-500',
  // 内容气泡：蓝靛渐变
  '[&_.chat-msg-agent_.chat-content]:max-w-[75%]',
  '[&_.chat-msg-agent_.chat-content]:whitespace-pre-wrap',
  '[&_.chat-msg-agent_.chat-content]:break-words',
  '[&_.chat-msg-agent_.chat-content]:leading-relaxed',
  '[&_.chat-msg-agent_.chat-content]:px-4',
  '[&_.chat-msg-agent_.chat-content]:py-2.5',
  '[&_.chat-msg-agent_.chat-content]:shadow-sm',
  '[&_.chat-msg-agent_.chat-content]:bg-gradient-to-br',
  '[&_.chat-msg-agent_.chat-content]:from-blue-500',
  '[&_.chat-msg-agent_.chat-content]:to-indigo-600',
  '[&_.chat-msg-agent_.chat-content]:text-white',
  '[&_.chat-msg-agent_.chat-content]:rounded-2xl',
  '[&_.chat-msg-agent_.chat-content]:rounded-br-md',
].join(' ');

/**
 * 智能时间显示：24 小时内显示相对时间（刚刚 / x 分钟前 / x 小时前），
 * 超过 24 小时显示完整日期时间；title 始终提供完整时间作为 tooltip。
 */
function SmartTime({ iso }: { iso: string }) {
  const fullText = formatDateTime(iso);
  const d = new Date(iso).getTime();
  const diff = Date.now() - d;
  const day = 24 * 60 * 60 * 1000;
  const display = diff < day ? relativeTime(iso) : fullText;
  return (
    <span className="text-xs text-gray-400 dark:text-slate-500" title={fullText}>
      {display}
    </span>
  );
}

/**
 * 日期分组标签：今天 / 昨天 / 具体日期。
 */
function formatDateGroup(iso: string): string {
  const d = new Date(iso);
  if (isNaN(d.getTime())) return '';
  const now = new Date();
  const today = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  const target = new Date(d.getFullYear(), d.getMonth(), d.getDate());
  const diffDays = Math.round((today.getTime() - target.getTime()) / (24 * 60 * 60 * 1000));
  if (diffDays === 0) return tParams('chat.conversation.dateToday');
  if (diffDays === 1) return tParams('chat.conversation.dateYesterday');
  if (diffDays === -1) return tParams('chat.conversation.dateTomorrow');
  // 同年省略年份
  if (d.getFullYear() === now.getFullYear()) {
    return tParams('serviceOps.threads.date.monthDay', { month: d.getMonth() + 1, day: d.getDate() });
  }
  return tParams('serviceOps.threads.date.yearMonthDay', { year: d.getFullYear(), month: d.getMonth() + 1, day: d.getDate() });
}

/** 取日期分组 key（YYYY-MM-DD） */
function dateGroupKey(iso: string): string {
  const d = new Date(iso);
  if (isNaN(d.getTime())) return 'unknown';
  return `${d.getFullYear()}-${d.getMonth() + 1}-${d.getDate()}`;
}

interface Props {
  threads: ThreadResponse[];
  /** 客户头像（用于客户消息显示） */
  customerAvatar?: string | null;
  /** 客户名称（用于无头像时的首字母回退） */
  customerName?: string | null;
}

function ThreadBubble({ thread, onPreview, customerAvatar, customerName }: { thread: ThreadResponse; onPreview: (atts: AttachmentResponse[], idx: number) => void; customerAvatar?: string | null; customerName?: string | null }) {
  const { t } = useI18n();
  const isCustomer = thread.type === ThreadType.Customer;
  const isNote = thread.type === ThreadType.Note;
  const isMessage = thread.type === ThreadType.Message;
  // 系统回复：From 含 "@system" 标识（如 wechat@system、dingtalk@system），区别于客服手动回复
  const isSystemReply = isMessage && !!thread.from?.includes('@system');

  // 微信来源标识：sourceType === 7 表示 WeChat（提前到样式设置之前以便差异化配色）
  const isWeChat = thread.sourceType === 7;
  // WhatsApp 来源标识：sourceType === 8 表示 WhatsApp（使用 emerald 色调与微信 green 区分）
  const isWhatsApp = thread.sourceType === 8;
  // 新增 4 个国际 IM 渠道：Messenger=11 / Telegram=12 / LINE=13 / iMessage=14
  const isMessenger = thread.sourceType === 11;
  const isTelegram = thread.sourceType === 12;
  const isLine = thread.sourceType === 13;
  const isIMessage = thread.sourceType === 14;
  // 新渠道复用 WhatsApp 的「现代 IM」气泡/卡片样式（配色与附件内联展示语义一致）
  const isModernIm = isMessenger || isTelegram || isLine || isIMessage;

  // 检测是否为「聊天转工单」：后端 ChatService.TransferToTicketAsync 生成的 body 包含 .chat-transcript
  const isChatTranscript = useMemo(() => isChatTranscriptBody(thread.body), [thread.body]);

  // 长文本折叠/展开：对非聊天转工单的正文，超过 400px 时折叠并显示"展开全文"
  const bodyRef = useRef<HTMLDivElement | null>(null);
  const [overflowing, setOverflowing] = useState(false);
  const [expanded, setExpanded] = useState(false);
  useEffect(() => {
    const el = bodyRef.current;
    if (!el || isChatTranscript) return;
    // 使用 scrollHeight 判断实际内容是否超出阈值
    setOverflowing(el.scrollHeight > 400);
  }, [thread.body, isChatTranscript]);

  /** 位置消息点击：PC 打开高德地图网页版，手机尝试唤起地图 App。
   *  后端在 .wechat-location / .whatsapp-location 元素上注入 data-lat / data-lng / data-name。 */
  const handleBodyClick = (e: MouseEvent<HTMLDivElement>) => {
    const target = (e.target as HTMLElement).closest('.wechat-location, .whatsapp-location, .messenger-location, .telegram-location, .line-location, .imessage-location') as HTMLElement | null;
    if (!target) return;

    const lat = target.getAttribute('data-lat');
    const lng = target.getAttribute('data-lng');
    if (!lat || !lng) return;

    e.preventDefault();
    e.stopPropagation();

    const name = target.getAttribute('data-name') || t('serviceOps.threads.locationFallback');
    // 设备检测：移动端 callnative=1 尝试唤起高德 App（未安装则回退网页），PC 端 callnative=0 直接打开网页版
    const isMobile = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent)
      || ('ontouchstart' in window && window.innerWidth <= 1024);
    const callnative = isMobile ? 1 : 0;
    const url = `https://uri.amap.com/marker?position=${encodeURIComponent(lng)},${encodeURIComponent(lat)}&name=${encodeURIComponent(name)}&coordinate=gaode&callnative=${callnative}`;
    window.open(url, '_blank', 'noopener,noreferrer');
  };

  let bgClass = '';
  let borderClass = '';
  let label = '';
  let labelColor = '';

  if (isNote) {
    bgClass = 'bg-amber-50 dark:bg-amber-900/20';
    borderClass = 'border-l-4 border-amber-400 dark:border-amber-700';
    label = t('conversations.detail.threadTypeNote');
    labelColor = 'bg-amber-200 text-amber-800 dark:bg-amber-800/50 dark:text-amber-200';
  } else if (isCustomer) {
    bgClass = isWhatsApp || isModernIm
      ? 'bg-gradient-to-br from-white via-white to-emerald-50/40 dark:from-slate-800 dark:via-slate-800 dark:to-emerald-900/15'
      : isWeChat
      ? 'bg-gradient-to-br from-white via-white to-green-50/40 dark:from-slate-800 dark:via-slate-800 dark:to-green-900/15'
      : 'bg-white dark:bg-slate-800';
    // WhatsApp 客户消息：左侧 4px emerald 色实线 + 浅 emerald 边框 + 底部阴影；
    // 微信客户消息：左侧 4px 亮绿色实线 + 浅绿边框，强化"客户发起"的视觉权重；
    // 其他客户消息保持灰色边框
    borderClass = isWhatsApp || isModernIm
      ? 'border border-emerald-200/70 dark:border-emerald-800/50 border-l-4 border-l-emerald-500 shadow-sm shadow-emerald-500/5 hover:shadow-md hover:shadow-emerald-500/10 transition-shadow duration-200'
      : isWeChat
      ? 'border border-green-200/70 dark:border-green-800/50 border-l-4 border-l-green-500 shadow-sm shadow-green-500/5 hover:shadow-md hover:shadow-green-500/10 transition-shadow duration-200'
      : 'border border-gray-200 dark:border-slate-600';
    label = t('conversations.detail.threadTypeCustomer');
    // WhatsApp 客户消息标签使用 emerald 色系；微信客户消息标签使用亮绿色系，与"微信"来源徽章视觉呼应
    labelColor = isWhatsApp || isModernIm
      ? 'bg-gradient-to-r from-emerald-100 to-teal-100 text-emerald-700 dark:from-emerald-900/50 dark:to-teal-900/40 dark:text-emerald-200 ring-1 ring-emerald-200/60 dark:ring-emerald-800/40'
      : isWeChat
      ? 'bg-gradient-to-r from-green-100 to-emerald-100 text-green-700 dark:from-green-900/50 dark:to-emerald-900/40 dark:text-green-200 ring-1 ring-green-200/60 dark:ring-green-800/40'
      : 'bg-blue-100 text-blue-700 dark:bg-blue-900/50 dark:text-blue-200';
  } else if (isSystemReply) {
    // 系统回复：渠道自动发送的引导消息等，使用 violet 色系区别于客户消息和客服回复
    bgClass = 'bg-violet-50/60 dark:bg-violet-900/15';
    borderClass = 'border border-violet-200/70 dark:border-violet-800/50 border-l-4 border-l-violet-400 dark:border-l-violet-600';
    label = t('serviceOps.threads.threadType.systemReply');
    labelColor = 'bg-violet-100 text-violet-700 dark:bg-violet-800/50 dark:text-violet-200 ring-1 ring-violet-200/50 dark:ring-violet-800/40';
  } else if (isMessage) {
    bgClass = isWhatsApp || isModernIm
      ? 'bg-gradient-to-br from-teal-50/50 via-white to-cyan-50/30 dark:from-teal-900/20 dark:via-slate-800 dark:to-cyan-900/15'
      : isWeChat
      ? 'bg-gradient-to-br from-teal-50/50 via-white to-emerald-50/30 dark:from-teal-900/20 dark:via-slate-800 dark:to-emerald-900/15'
      : 'bg-primary-50/40 dark:bg-primary-900/20';
    // WhatsApp 客服回复：左侧 4px 深青色实线 + 浅青边框，与客户的 emerald 形成明暗对比；
    // 微信客服回复：左侧 4px 深青色实线 + 浅青边框，与客户的亮绿色形成明暗对比；
    // 其他客服回复保持 primary 边框
    borderClass = isWhatsApp || isModernIm
      ? 'border border-teal-200/70 dark:border-teal-800/50 border-l-4 border-l-teal-600 shadow-sm shadow-teal-500/5 hover:shadow-md hover:shadow-teal-500/10 transition-shadow duration-200'
      : isWeChat
      ? 'border border-teal-200/70 dark:border-teal-800/50 border-l-4 border-l-teal-600 shadow-sm shadow-teal-500/5 hover:shadow-md hover:shadow-teal-500/10 transition-shadow duration-200'
      : 'border border-primary-200 dark:border-primary-800/50';
    // 客服回复统一显示"客服回复"，不再按渠道区分
    label = t('conversations.detail.threadTypeReply');
    // WhatsApp/微信客服回复标签使用深青色系，与客户的 emerald/green 形成层次；其他客服回复保持 emerald
    labelColor = (isWhatsApp || isWeChat || isModernIm)
      ? 'bg-gradient-to-r from-teal-100 to-cyan-100 text-teal-700 dark:from-teal-900/50 dark:to-cyan-900/40 dark:text-teal-200 ring-1 ring-teal-200/60 dark:ring-teal-800/40'
      : 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/50 dark:text-emerald-200';
  } else {
    bgClass = 'bg-gray-50 dark:bg-slate-700/50';
    borderClass = 'border border-gray-200 dark:border-slate-600';
    label = t('conversations.detail.threadTypeSystem');
    labelColor = 'bg-gray-200 text-gray-700 dark:bg-slate-600 dark:text-slate-300';
  }

  // 聊天转工单 thread：覆盖为透明容器 + 更清晰的专属标签，样式交由内部 chatTranscriptStyles 接管
  if (isChatTranscript) {
    bgClass = 'bg-transparent';
    borderClass = 'border border-dashed border-slate-200/70 dark:border-slate-600/60';
    label = t('conversations.sidebar.chatToConversation');
    labelColor = 'inline-flex items-center gap-1 bg-sky-100 text-sky-700 dark:bg-sky-900/50 dark:text-sky-200';
  }

  let displayName: string;
  let avatarChar: string;
  if (isCustomer) {
    // 客户消息：优先显示客户在系统中登记的姓名（customerName），
    // 原始邮件 From 头仅作为姓名缺失时的备用。
    // 这样当 Customer 实体有姓名时，显示客户名而不是邮箱 From 头。
    // 防泄露：当最终显示名回退成"邮箱地址"时（Customer 无真名、GetFullName(true) 回退到主邮箱，
    // 或 thread.from 本身为邮箱），统一用友好的"客户"兜底，不在会话中暴露客户邮箱。
    const nameOrFrom = customerName || thread.from || t('format.unknown');
    displayName = isEmailAddress(nameOrFrom) ? t('serviceOps.threads.customerFallback') : nameOrFrom;
    avatarChar = displayName.charAt(0).toUpperCase();
  } else if (isSystemReply) {
    // 系统回复：显示"系统"，不暴露内部标识（如 wechat@system）
    displayName = t('serviceOps.threads.systemFallback');
    avatarChar = 'S';
  } else {
    // 客服回复 / 内部备注 / 系统消息：优先显示登录用户姓名
    displayName = thread.user?.fullName || thread.from || customerName || t('format.unknown');
    avatarChar = thread.user?.fullName?.charAt(0) || thread.from?.charAt(0)?.toUpperCase() || customerName?.charAt(0)?.toUpperCase() || '?';
  }

  // 聊天转工单：正文容器的 className（不套用 prose 通用样式，完全由 chatTranscriptStyles 接管语义 class）
  const bodyClassName = isChatTranscript
    ? `prose prose-sm max-w-none text-gray-800 dark:text-slate-200 ${chatTranscriptStyles} [&_*]:!prose-none`
    : 'text-sm text-gray-800 dark:text-slate-200 leading-relaxed prose prose-sm max-w-none break-words ' +
      '[&_a]:text-primary-600 [&_a]:underline dark:[&_a]:text-primary-400 ' +
      '[&_strong]:font-semibold ' +
      '[&_em]:italic ' +
      '[&_blockquote]:border-l-4 [&_blockquote]:border-gray-300 [&_blockquote]:pl-3 [&_blockquote]:text-gray-600 dark:[&_blockquote]:border-slate-600 dark:[&_blockquote]:text-slate-400 ' +
      '[&_code]:bg-gray-100 [&_code]:px-1 [&_code]:py-0.5 [&_code]:rounded [&_code]:text-pink-600 [&_code]:text-xs [&_code]:font-mono dark:[&_code]:bg-slate-700 dark:[&_code]:text-pink-400 ' +
      '[&_h3]:font-semibold [&_h3]:text-base [&_h3]:mt-2 [&_h3]:mb-1 dark:[&_h3]:text-slate-100 ' +
      '[&_ul]:list-disc [&_ul]:pl-5 [&_ul]:my-1 ' +
      '[&_ol]:list-decimal [&_ol]:pl-5 [&_ol]:my-1 ' +
      // 微信媒体消息卡片样式（后端 BuildThreadHtmlBody 生成的语义 class）
      '[&_.wechat-text]:whitespace-pre-wrap [&_.wechat-text]:break-words ' +
      '[&_.wechat-media]:p-3 [&_.wechat-media]:border [&_.wechat-media]:border-gray-200 [&_.wechat-media]:border-l-4 [&_.wechat-media]:border-l-green-400 [&_.wechat-media]:rounded-lg [&_.wechat-media]:bg-gray-50 dark:[&_.wechat-media]:bg-slate-700/40 dark:[&_.wechat-media]:border-slate-600 dark:[&_.wechat-media]:border-l-green-500 ' +
      '[&_.wechat-media-inner]:flex [&_.wechat-media-inner]:items-center [&_.wechat-media-inner]:gap-3 ' +
      '[&_.wechat-media-icon]:flex-shrink-0 [&_.wechat-media-icon]:w-9 [&_.wechat-media-icon]:h-9 [&_.wechat-media-icon]:rounded-lg [&_.wechat-media-icon]:bg-green-100 [&_.wechat-media-icon]:flex [&_.wechat-media-icon]:items-center [&_.wechat-media-icon]:justify-center [&_.wechat-media-icon]:text-lg dark:[&_.wechat-media-icon]:bg-green-900/40 ' +
      '[&_.wechat-media-info]:flex-1 [&_.wechat-media-info]:min-w-0 ' +
      '[&_.wechat-media-title]:font-semibold [&_.wechat-media-title]:text-gray-700 [&_.wechat-media-title]:text-sm [&_.wechat-media-title]:mb-0.5 dark:[&_.wechat-media-title]:text-slate-200 ' +
      '[&_.wechat-media-desc]:text-xs [&_.wechat-media-desc]:text-gray-400 [&_.wechat-media-desc]:leading-relaxed dark:[&_.wechat-media-desc]:text-slate-400 ' +
      '[&_.wechat-media-header]:flex [&_.wechat-media-header]:items-center [&_.wechat-media-header]:gap-1.5 [&_.wechat-media-header]:mb-1.5 ' +
      // 位置消息
      '[&_.wechat-location]:block [&_.wechat-location]:p-3 [&_.wechat-location]:border [&_.wechat-location]:border-gray-200 [&_.wechat-location]:border-l-4 [&_.wechat-location]:border-l-green-400 [&_.wechat-location]:rounded-lg [&_.wechat-location]:bg-gray-50 [&_.wechat-location]:no-underline [&_.wechat-location]:text-inherit [&_.wechat-location]:transition-all [&_.wechat-location]:duration-200 hover:[&_.wechat-location]:border-l-green-500 hover:[&_.wechat-location]:shadow-md hover:[&_.wechat-location]:bg-green-50/50 dark:[&_.wechat-location]:bg-slate-700/40 dark:[&_.wechat-location]:border-slate-600 dark:[&_.wechat-location]:border-l-green-500 dark:hover:[&_.wechat-location]:bg-slate-700/60 ' +
      '[&_.wechat-location-link]:cursor-pointer ' +
      '[&_.wechat-location-hint]:ml-auto [&_.wechat-location-hint]:text-xs [&_.wechat-location-hint]:text-green-500 [&_.wechat-location-hint]:font-medium [&_.wechat-location-hint]:flex-shrink-0 dark:[&_.wechat-location-hint]:text-green-400 ' +
      // 链接消息
      '[&_.wechat-link]:p-3 [&_.wechat-link]:border [&_.wechat-link]:border-gray-200 [&_.wechat-link]:border-l-4 [&_.wechat-link]:border-l-green-400 [&_.wechat-link]:rounded-lg [&_.wechat-link]:bg-gray-50 dark:[&_.wechat-link]:bg-slate-700/40 dark:[&_.wechat-link]:border-slate-600 dark:[&_.wechat-link]:border-l-green-500 ' +
      '[&_.wechat-link-inner]:flex [&_.wechat-link-inner]:items-start [&_.wechat-link-inner]:gap-2 ' +
      '[&_.wechat-link-content]:flex-1 [&_.wechat-link-content]:min-w-0 ' +
      '[&_.wechat-link-title]:font-semibold [&_.wechat-link-title]:text-gray-900 [&_.wechat-link-title]:text-sm [&_.wechat-link-title]:mb-1 dark:[&_.wechat-link-title]:text-slate-100 ' +
      '[&_.wechat-link-desc]:text-xs [&_.wechat-link-desc]:text-gray-500 [&_.wechat-link-desc]:mb-1.5 [&_.wechat-link-desc]:leading-relaxed dark:[&_.wechat-link-desc]:text-slate-400 ' +
      '[&_.wechat-link-url]:text-green-600 [&_.wechat-link-url]:text-xs [&_.wechat-link-url]:underline [&_.wechat-link-url]:break-all dark:[&_.wechat-link-url]:text-green-400 ' +
      // 未知类型消息
      '[&_.wechat-unknown]:px-3 [&_.wechat-unknown]:py-2 [&_.wechat-unknown]:border [&_.wechat-unknown]:border-dashed [&_.wechat-unknown]:border-gray-300 [&_.wechat-unknown]:rounded-lg [&_.wechat-unknown]:bg-gray-50 [&_.wechat-unknown]:text-gray-500 [&_.wechat-unknown]:text-xs dark:[&_.wechat-unknown]:border-slate-600 dark:[&_.wechat-unknown]:bg-slate-700/40 dark:[&_.wechat-unknown]:text-slate-400 ' +
      // 客服回复附件占位（微信客服发送的图片/语音/视频）
      '[&_.wechat-media-attachment]:inline-flex [&_.wechat-media-attachment]:items-center [&_.wechat-media-attachment]:mr-2 [&_.wechat-media-attachment]:mb-1 ' +
      '[&_.wechat-media-attachment-inner]:inline-flex [&_.wechat-media-attachment-inner]:items-center [&_.wechat-media-attachment-inner]:gap-1.5 [&_.wechat-media-attachment-inner]:px-2.5 [&_.wechat-media-attachment-inner]:py-1 [&_.wechat-media-attachment-inner]:bg-green-50 [&_.wechat-media-attachment-inner]:text-green-700 [&_.wechat-media-attachment-inner]:rounded-full [&_.wechat-media-attachment-inner]:text-xs [&_.wechat-media-attachment-inner]:border [&_.wechat-media-attachment-inner]:border-green-200 dark:[&_.wechat-media-attachment-inner]:bg-green-900/30 dark:[&_.wechat-media-attachment-inner]:text-green-300 dark:[&_.wechat-media-attachment-inner]:border-green-800/50 ' +
      '[&_.wechat-media-attachment-icon]:text-sm ' +
      '[&_.wechat-media-attachment-name]:font-medium ' +
      // WhatsApp 媒体消息卡片样式（后端生成 body 中的语义 class，使用 emerald 色调 + 渐变背景 + 阴影）
      '[&_.whatsapp-text]:whitespace-pre-wrap [&_.whatsapp-text]:break-words [&_.whatsapp-text]:leading-relaxed ' +
      '[&_.whatsapp-media]:p-4 [&_.whatsapp-media]:border [&_.whatsapp-media]:border-emerald-200/70 [&_.whatsapp-media]:border-l-4 [&_.whatsapp-media]:border-l-emerald-500 [&_.whatsapp-media]:rounded-xl [&_.whatsapp-media]:bg-gradient-to-br [&_.whatsapp-media]:from-emerald-50/50 [&_.whatsapp-media]:via-white [&_.whatsapp-media]:to-teal-50/30 [&_.whatsapp-media]:shadow-sm [&_.whatsapp-media]:shadow-emerald-500/5 [&_.whatsapp-media]:transition-all [&_.whatsapp-media]:duration-200 [&_.whatsapp-media]:hover:shadow-md [&_.whatsapp-media]:hover:shadow-emerald-500/10 dark:[&_.whatsapp-media]:from-emerald-900/20 dark:[&_.whatsapp-media]:via-slate-800 dark:[&_.whatsapp-media]:to-teal-900/15 dark:[&_.whatsapp-media]:border-slate-700 dark:[&_.whatsapp-media]:border-l-emerald-500 ' +
      '[&_.whatsapp-media-inner]:flex [&_.whatsapp-media-inner]:items-center [&_.whatsapp-media-inner]:gap-3.5 ' +
      '[&_.whatsapp-media-icon]:flex-shrink-0 [&_.whatsapp-media-icon]:w-11 [&_.whatsapp-media-icon]:h-11 [&_.whatsapp-media-icon]:rounded-xl [&_.whatsapp-media-icon]:bg-gradient-to-br [&_.whatsapp-media-icon]:from-emerald-100 [&_.whatsapp-media-icon]:to-teal-100 [&_.whatsapp-media-icon]:flex [&_.whatsapp-media-icon]:items-center [&_.whatsapp-media-icon]:justify-center [&_.whatsapp-media-icon]:text-2xl [&_.whatsapp-media-icon]:shadow-sm [&_.whatsapp-media-icon]:ring-1 [&_.whatsapp-media-icon]:ring-emerald-200/60 dark:[&_.whatsapp-media-icon]:from-emerald-900/50 dark:[&_.whatsapp-media-icon]:to-teal-900/40 dark:[&_.whatsapp-media-icon]:ring-emerald-800/40 ' +
      '[&_.whatsapp-media-info]:flex-1 [&_.whatsapp-media-info]:min-w-0 ' +
      '[&_.whatsapp-media-title]:font-bold [&_.whatsapp-media-title]:text-gray-800 [&_.whatsapp-media-title]:text-sm [&_.whatsapp-media-title]:mb-1 dark:[&_.whatsapp-media-title]:text-slate-100 ' +
      '[&_.whatsapp-media-desc]:text-xs [&_.whatsapp-media-desc]:text-gray-500 [&_.whatsapp-media-desc]:leading-relaxed dark:[&_.whatsapp-media-desc]:text-slate-400 ' +
      // WhatsApp 位置消息（地图卡片风格）
      '[&_.whatsapp-location]:p-0 [&_.whatsapp-location]:overflow-hidden [&_.whatsapp-location]:border [&_.whatsapp-location]:border-emerald-200/70 [&_.whatsapp-location]:rounded-xl [&_.whatsapp-location]:bg-gradient-to-br [&_.whatsapp-location]:from-emerald-50/60 [&_.whatsapp-location]:to-teal-50/40 [&_.whatsapp-location]:shadow-sm [&_.whatsapp-location]:shadow-emerald-500/5 [&_.whatsapp-location]:cursor-pointer [&_.whatsapp-location]:transition-all [&_.whatsapp-location]:duration-200 hover:[&_.whatsapp-location]:shadow-md dark:[&_.whatsapp-location]:from-emerald-900/20 dark:[&_.whatsapp-location]:to-teal-900/15 dark:[&_.whatsapp-location]:border-slate-700 ' +
      // WhatsApp 联系人消息
      '[&_.whatsapp-contact]:p-4 [&_.whatsapp-contact]:border [&_.whatsapp-contact]:border-emerald-200/70 [&_.whatsapp-contact]:border-l-4 [&_.whatsapp-contact]:border-l-emerald-500 [&_.whatsapp-contact]:rounded-xl [&_.whatsapp-contact]:bg-gradient-to-br [&_.whatsapp-contact]:from-emerald-50/50 [&_.whatsapp-contact]:via-white [&_.whatsapp-contact]:to-green-50/30 [&_.whatsapp-contact]:shadow-sm [&_.whatsapp-contact]:shadow-emerald-500/5 dark:[&_.whatsapp-contact]:from-emerald-900/20 dark:[&_.whatsapp-contact]:via-slate-800 dark:[&_.whatsapp-contact]:to-green-900/15 dark:[&_.whatsapp-contact]:border-slate-700 dark:[&_.whatsapp-contact]:border-l-emerald-500 ' +
      // Messenger(11)/Telegram(12)/LINE(13)/iMessage(14) 后端 BuildThreadHtmlBody 生成的语义 class：
      // 统一沿用 WhatsApp 的 emerald 卡片色系，渠道身份由上方来源徽章区分（避免为每个渠道再造一套配色）
      '[&_.messenger-text]:whitespace-pre-wrap]:break-words]:leading-relaxed [&_.telegram-text]:whitespace-pre-wrap]:break-words]:leading-relaxed [&_.line-text]:whitespace-pre-wrap]:break-words]:leading-relaxed [&_.imessage-text]:whitespace-pre-wrap]:break-words]:leading-relaxed ' +
      '[&_.messenger-media]:p-4]:border]:border-emerald-200/70]:dark:border-emerald-800/50]:border-l-4]:border-l-emerald-500]:rounded-xl]:bg-white/70]:dark:bg-slate-800/50]:shadow-sm [&_.telegram-media]:p-4]:border]:border-emerald-200/70]:dark:border-emerald-800/50]:border-l-4]:border-l-emerald-500]:rounded-xl]:bg-white/70]:dark:bg-slate-800/50]:shadow-sm [&_.line-media]:p-4]:border]:border-emerald-200/70]:dark:border-emerald-800/50]:border-l-4]:border-l-emerald-500]:rounded-xl]:bg-white/70]:dark:bg-slate-800/50]:shadow-sm [&_.imessage-media]:p-4]:border]:border-emerald-200/70]:dark:border-emerald-800/50]:border-l-4]:border-l-emerald-500]:rounded-xl]:bg-white/70]:dark:bg-slate-800/50]:shadow-sm ' +
      '[&_.messenger-media-inner]:flex]:items-center]:gap-3.5 [&_.telegram-media-inner]:flex]:items-center]:gap-3.5 [&_.line-media-inner]:flex]:items-center]:gap-3.5 [&_.imessage-media-inner]:flex]:items-center]:gap-3.5 ' +
      '[&_.messenger-media-icon]:flex-shrink-0]:w-11]:h-11]:rounded-xl]:bg-emerald-100]:text-emerald-600]:flex]:items-center]:justify-center]:text-xl]:dark:bg-emerald-900/40]:dark:text-emerald-300 [&_.telegram-media-icon]:flex-shrink-0]:w-11]:h-11]:rounded-xl]:bg-emerald-100]:text-emerald-600]:flex]:items-center]:justify-center]:text-xl]:dark:bg-emerald-900/40]:dark:text-emerald-300 [&_.line-media-icon]:flex-shrink-0]:w-11]:h-11]:rounded-xl]:bg-emerald-100]:text-emerald-600]:flex]:items-center]:justify-center]:text-xl]:dark:bg-emerald-900/40]:dark:text-emerald-300 [&_.imessage-media-icon]:flex-shrink-0]:w-11]:h-11]:rounded-xl]:bg-emerald-100]:text-emerald-600]:flex]:items-center]:justify-center]:text-xl]:dark:bg-emerald-900/40]:dark:text-emerald-300 ' +
      '[&_.messenger-media-info]:flex-1]:min-w-0 [&_.telegram-media-info]:flex-1]:min-w-0 [&_.line-media-info]:flex-1]:min-w-0 [&_.imessage-media-info]:flex-1]:min-w-0 ' +
      '[&_.messenger-media-title]:font-bold]:text-gray-800]:dark:text-slate-100]:text-sm]:mb-1 [&_.telegram-media-title]:font-bold]:text-gray-800]:dark:text-slate-100]:text-sm]:mb-1 [&_.line-media-title]:font-bold]:text-gray-800]:dark:text-slate-100]:text-sm]:mb-1 [&_.imessage-media-title]:font-bold]:text-gray-800]:dark:text-slate-100]:text-sm]:mb-1 ' +
      '[&_.messenger-media-desc]:text-xs]:text-gray-500]:dark:text-slate-400]:leading-relaxed [&_.telegram-media-desc]:text-xs]:text-gray-500]:dark:text-slate-400]:leading-relaxed [&_.line-media-desc]:text-xs]:text-gray-500]:dark:text-slate-400]:leading-relaxed [&_.imessage-media-desc]:text-xs]:text-gray-500]:dark:text-slate-400]:leading-relaxed ' +
      '[&_.messenger-location]:block]:p-3]:border]:border-emerald-200/70]:dark:border-emerald-800/50]:border-l-4]:border-l-emerald-500]:rounded-lg [&_.telegram-location]:block]:p-3]:border]:border-emerald-200/70]:dark:border-emerald-800/50]:border-l-4]:border-l-emerald-500]:rounded-lg [&_.line-location]:block]:p-3]:border]:border-emerald-200/70]:dark:border-emerald-800/50]:border-l-4]:border-l-emerald-500]:rounded-lg [&_.imessage-location]:block]:p-3]:border]:border-emerald-200/70]:dark:border-emerald-800/50]:border-l-4]:border-l-emerald-500]:rounded-lg ' +
      '[&_.messenger-location-link]:cursor-pointer [&_.telegram-location-link]:cursor-pointer [&_.line-location-link]:cursor-pointer [&_.imessage-location-link]:cursor-pointer ' +
      '[&_.messenger-unknown]:px-3]:py-2]:border]:border-dashed]:border-gray-300]:dark:border-slate-600]:rounded-lg]:text-xs]:text-gray-500]:italic [&_.telegram-unknown]:px-3]:py-2]:border]:border-dashed]:border-gray-300]:dark:border-slate-600]:rounded-lg]:text-xs]:text-gray-500]:italic [&_.line-unknown]:px-3]:py-2]:border]:border-dashed]:border-gray-300]:dark:border-slate-600]:rounded-lg]:text-xs]:text-gray-500]:italic [&_.imessage-unknown]:px-3]:py-2]:border]:border-dashed]:border-gray-300]:dark:border-slate-600]:rounded-lg]:text-xs]:text-gray-500]:italic ' +
      '[&_.messenger-event]:text-xs]:text-gray-400]:dark:text-slate-500]:italic [&_.telegram-event]:text-xs]:text-gray-400]:dark:text-slate-500]:italic [&_.line-event]:text-xs]:text-gray-400]:dark:text-slate-500]:italic [&_.imessage-event]:text-xs]:text-gray-400]:dark:text-slate-500]:italic ' +
      '[&_.messenger-postback]:text-xs]:text-gray-500]:dark:text-slate-400]:italic [&_.telegram-postback]:text-xs]:text-gray-500]:dark:text-slate-400]:italic [&_.line-postback]:text-xs]:text-gray-500]:dark:text-slate-400]:italic [&_.imessage-postback]:text-xs]:text-gray-500]:dark:text-slate-400]:italic ' +
      '[&_.messenger-quick-reply]:text-xs]:text-gray-500]:dark:text-slate-400]:italic [&_.telegram-quick-reply]:text-xs]:text-gray-500]:dark:text-slate-400]:italic [&_.line-quick-reply]:text-xs]:text-gray-500]:dark:text-slate-400]:italic [&_.imessage-quick-reply]:text-xs]:text-gray-500]:dark:text-slate-400]:italic ' +
      '[&_.messenger-optin]:text-xs]:text-gray-500]:dark:text-slate-400]:italic [&_.telegram-optin]:text-xs]:text-gray-500]:dark:text-slate-400]:italic [&_.line-optin]:text-xs]:text-gray-500]:dark:text-slate-400]:italic [&_.imessage-optin]:text-xs]:text-gray-500]:dark:text-slate-400]:italic ' +
      '[&_.messenger-tag]:text-[10px]]:uppercase]:tracking-wide]:text-emerald-600]:dark:text-emerald-400 [&_.telegram-tag]:text-[10px]]:uppercase]:tracking-wide]:text-emerald-600]:dark:text-emerald-400 [&_.line-tag]:text-[10px]]:uppercase]:tracking-wide]:text-emerald-600]:dark:text-emerald-400 [&_.imessage-tag]:text-[10px]]:uppercase]:tracking-wide]:text-emerald-600]:dark:text-emerald-400 ' +
      '[&_.messenger-caption]:whitespace-pre-wrap]:break-words]:text-sm [&_.telegram-caption]:whitespace-pre-wrap]:break-words]:text-sm [&_.line-caption]:whitespace-pre-wrap]:break-words]:text-sm [&_.imessage-caption]:whitespace-pre-wrap]:break-words]:text-sm ' +
      '[&_.messenger-reply-to]:text-xs]:text-gray-400]:border-l-2]:border-gray-300]:pl-2]:mb-1]:block [&_.telegram-reply-to]:text-xs]:text-gray-400]:border-l-2]:border-gray-300]:pl-2]:mb-1]:block [&_.line-reply-to]:text-xs]:text-gray-400]:border-l-2]:border-gray-300]:pl-2]:mb-1]:block [&_.imessage-reply-to]:text-xs]:text-gray-400]:border-l-2]:border-gray-300]:pl-2]:mb-1]:block ' +
      '[&_.messenger-topic]:text-xs]:text-gray-500]:dark:text-slate-400 [&_.telegram-topic]:text-xs]:text-gray-500]:dark:text-slate-400 [&_.line-topic]:text-xs]:text-gray-500]:dark:text-slate-400 [&_.imessage-topic]:text-xs]:text-gray-500]:dark:text-slate-400 ' +
      '[&_.messenger-start-param]:text-xs]:text-gray-400]:dark:text-slate-500 [&_.telegram-start-param]:text-xs]:text-gray-400]:dark:text-slate-500 [&_.line-start-param]:text-xs]:text-gray-400]:dark:text-slate-500 [&_.imessage-start-param]:text-xs]:text-gray-400]:dark:text-slate-500 ' +
      '[&_.messenger-group]:text-xs]:text-gray-500]:dark:text-slate-400 [&_.telegram-group]:text-xs]:text-gray-500]:dark:text-slate-400 [&_.line-group]:text-xs]:text-gray-500]:dark:text-slate-400 [&_.imessage-group]:text-xs]:text-gray-500]:dark:text-slate-400 ' +
      '[&_.messenger-empty]:text-xs]:text-gray-400]:dark:text-slate-500]:italic [&_.telegram-empty]:text-xs]:text-gray-400]:dark:text-slate-500]:italic [&_.line-empty]:text-xs]:text-gray-400]:dark:text-slate-500]:italic [&_.imessage-empty]:text-xs]:text-gray-400]:dark:text-slate-500]:italic ' +
      '[&_.messenger-attachments]:mt-2 [&_.telegram-attachments]:mt-2 [&_.line-attachments]:mt-2 [&_.imessage-attachments]:mt-2 ' +
      '[&_.messenger-unsend]:line-through]:text-gray-400]:dark:text-slate-500 [&_.telegram-unsend]:line-through]:text-gray-400]:dark:text-slate-500 [&_.line-unsend]:line-through]:text-gray-400]:dark:text-slate-500 [&_.imessage-unsend]:line-through]:text-gray-400]:dark:text-slate-500 ' +
      // 邮件正文表格样式（T000121：客户邮件正文包含表格时，会话记录应显示表格边框与样式）
      '[&_table]:w-full [&_table]:border-collapse [&_table]:my-2 [&_table]:text-sm ' +
      '[&_thead]:bg-gray-50 dark:[&_thead]:bg-slate-700/40 ' +
      '[&_th]:border [&_th]:border-gray-300 [&_th]:px-2 [&_th]:py-1 [&_th]:font-semibold [&_th]:text-left [&_th]:align-middle dark:[&_th]:border-slate-600 dark:[&_th]:text-slate-200 ' +
      '[&_td]:border [&_td]:border-gray-300 [&_td]:px-2 [&_td]:py-1 [&_td]:align-middle dark:[&_td]:border-slate-600 dark:[&_td]:text-slate-300 ' +
      '[&_tr]:border-b [&_tr]:border-gray-200 dark:[&_tr]:border-slate-700 ' +
      '[&_table]:overflow-x-auto ' +
      // 邮件正文图片（T000121：内嵌图片通过附件下载 URL 加载，约束尺寸避免溢出）
      '[&_img]:max-w-full [&_img]:h-auto [&_img]:rounded-md [&_img]:my-2';

  // 聊天转工单：body 内部 padding 增加一点，让气泡视觉更接近在线聊天页的留白
  const bodyWrapperClass = isChatTranscript
    ? `rounded-xl p-3 sm:p-4 md:p-5 ${bgClass} ${borderClass}`
    : `rounded-xl p-3 sm:p-4 ${bgClass} ${borderClass}`;

  return (
    <div className="flex gap-2 sm:gap-3">
      {/* 头像 */}
      <div className="flex-shrink-0">
        {thread.user ? (
          <UserAvatar userId={thread.user.id} name={thread.user.fullName} size="sm" avatarUrl={thread.user.avatar ?? null} />
        ) : isCustomer && customerAvatar ? (
          <img
            src={customerAvatar}
            alt={displayName}
            className={`w-8 h-8 sm:w-9 sm:h-9 rounded-full object-cover border ${
              isWhatsApp || isModernIm
                ? 'border-emerald-300 dark:border-emerald-700 ring-2 ring-emerald-300/50 dark:ring-emerald-700/40'
                : isWeChat
                ? 'border-green-300 dark:border-green-700 ring-2 ring-green-300/50 dark:ring-green-700/40'
                : 'border-gray-200 dark:border-slate-600'
            }`}
          />
        ) : (
          <div
            className={`w-8 h-8 sm:w-9 sm:h-9 rounded-full flex items-center justify-center text-sm font-semibold ${
              isNote ? 'bg-amber-200 text-amber-800 dark:bg-amber-700 dark:text-amber-100'
                : isCustomer ? (isWhatsApp || isModernIm
                  ? 'bg-gradient-to-br from-emerald-500 to-green-600 text-white shadow-sm ring-2 ring-emerald-300/60 dark:ring-emerald-700/40'
                  : isWeChat
                  ? 'bg-gradient-to-br from-green-500 to-emerald-600 text-white shadow-sm ring-2 ring-green-300/60 dark:ring-green-700/40'
                  : 'bg-gradient-to-br from-blue-500 to-indigo-600 text-white shadow-sm')
                : isSystemReply
                ? 'bg-gradient-to-br from-violet-500 to-purple-600 text-white shadow-sm ring-2 ring-violet-300/50 dark:ring-violet-700/40'
                : (isWhatsApp || isWeChat || isModernIm)
                ? 'bg-gradient-to-br from-teal-600 to-cyan-700 text-white shadow-sm ring-2 ring-teal-300/50 dark:ring-teal-700/40'
                : 'bg-gradient-to-br from-emerald-500 to-teal-600 text-white shadow-sm'
            }`}
          >
            {avatarChar}
          </div>
        )}
      </div>

      {/* 内容区 */}
      <div className="flex-1 min-w-0">
        <div className="flex items-center gap-2 mb-1.5 flex-wrap">
          <span className="text-sm font-medium text-gray-900 dark:text-slate-100">{displayName}</span>
          <span className={`text-[11px] px-2 py-0.5 rounded-full font-medium ${labelColor}`}>
            {isChatTranscript && <MessageCircleIcon className="w-3 h-3" />}
            {label}
          </span>
          {isWeChat && (
            <span className="inline-flex items-center gap-1 text-[11px] px-2 py-0.5 rounded-full font-medium bg-green-100 text-green-700 dark:bg-green-900/50 dark:text-green-200">
              <SourceTypeIcon sourceType={thread.sourceType} className="w-3 h-3" />
              {t('format.sourceType.wechat')}
            </span>
          )}
          {isWhatsApp && (
            <span className="relative inline-flex items-center gap-1.5 text-[11px] px-2.5 py-1 rounded-full font-semibold bg-gradient-to-r from-emerald-100 via-white to-teal-100 text-emerald-700 dark:from-emerald-900/50 dark:via-slate-800 dark:to-teal-900/40 dark:text-emerald-200 ring-1 ring-emerald-200/70 dark:ring-emerald-800/50 shadow-sm hover:shadow-md transition-all duration-200">
              <span className="absolute inset-0 rounded-full overflow-hidden opacity-60" aria-hidden="true">
                <span className="absolute -inset-1 bg-gradient-to-r from-emerald-400/30 via-teal-400/30 to-emerald-400/30 blur-sm" />
              </span>
              <SourceTypeIcon sourceType={thread.sourceType} className="w-3.5 h-3.5 relative" />
              <span className="relative tracking-wide">WhatsApp</span>
            </span>
          )}
          {(isMessenger || isTelegram || isLine || isIMessage) && (
            <span className="inline-flex items-center gap-1 text-[11px] px-2 py-0.5 rounded-full font-medium bg-sky-100 text-sky-700 dark:bg-sky-900/50 dark:text-sky-300">
              <SourceTypeIcon sourceType={thread.sourceType} className="w-3 h-3" />
              {t(isMessenger ? 'format.sourceType.messenger' : isTelegram ? 'format.sourceType.telegram' : isLine ? 'format.sourceType.line' : 'format.sourceType.imessage')}
            </span>
          )}
          <SmartTime iso={thread.createdAt} />
        </div>

        <div className={bodyWrapperClass}>
          {/* 正文 */}
          <div
            ref={bodyRef}
            className={`${bodyClassName} ${overflowing && !expanded ? 'max-h-[400px] overflow-hidden' : ''}`}
            dangerouslySetInnerHTML={{ __html: replaceEmojiShortcodes(sanitizeHtml(thread.body)) }}
            onClick={handleBodyClick}
          />
          {/* 长文本折叠遮罩 + 展开按钮 */}
          {overflowing && !expanded && (
            <div className="relative -mt-12 pointer-events-none">
              <div className="h-12 bg-gradient-to-t from-white dark:from-slate-800 to-transparent" />
            </div>
          )}
          {overflowing && (
            <button
              type="button"
              onClick={() => setExpanded((v) => !v)}
              className="mt-1.5 inline-flex items-center gap-1 text-xs font-medium text-primary-600 hover:text-primary-700 dark:text-primary-400 dark:hover:text-primary-300 transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary-400 rounded px-1"
              aria-expanded={expanded}
              aria-label={expanded ? t('serviceOps.threads.collapseBody') : t('serviceOps.threads.expandFull')}
            >
              {expanded ? (
                <>
                  <ChevronUp className="w-3.5 h-3.5" />
                  {t('serviceOps.threads.collapse')}
                </>
              ) : (
                <>
                  <ChevronDown className="w-3.5 h-3.5" />
                  {t('serviceOps.threads.expandFull')}
                </>
              )}
            </button>
          )}

          {/* 微信/WhatsApp 客户消息：图片附件内联展示为缩略图网格 */}
          {(isWeChat || isWhatsApp || isModernIm) && isCustomer && thread.attachments && thread.attachments.length > 0 && (() => {
            const imageAtts = thread.attachments.filter((a) => isImage(a.mimeType));
            if (imageAtts.length === 0) return null;
            // 单图大图展示，多图网格展示（响应式 max-width 避免小屏溢出）
            const gridClass = imageAtts.length === 1
              ? 'grid grid-cols-1 max-w-[16rem]'
              : imageAtts.length === 2
              ? 'grid grid-cols-2 gap-1 max-w-[20rem]'
              : 'grid grid-cols-3 gap-1 max-w-[24rem]';
            const hoverBorder = isWhatsApp || isModernIm
              ? 'hover:border-emerald-300 dark:hover:border-emerald-600'
              : 'hover:border-green-300 dark:hover:border-green-600';
            return (
              <div className={`mt-2 ${gridClass}`}>
                {imageAtts.map((att) => {
                  const attIdx = thread.attachments!.findIndex((a) => a.id === att.id);
                  return (
                    <button
                      key={att.id}
                      onClick={() => onPreview(thread.attachments!, attIdx)}
                      className={`relative group/img overflow-hidden rounded-lg border border-gray-200/60 dark:border-slate-600/50 ${hoverBorder} transition aspect-square`}
                      aria-label={t('serviceOps.threads.viewImageAria', { fileName: att.fileName })}
                    >
                      <img
                        src={attachmentApi.previewUrl(att.id, 240, 240)}
                        alt={att.fileName}
                        className="w-full h-full object-cover group-hover/img:scale-105 transition-transform duration-200"
                        loading="lazy"
                      />
                      <div className="absolute inset-0 bg-black/0 group-hover/img:bg-black/10 transition-colors" />
                    </button>
                  );
                })}
              </div>
            );
          })()}

          {/* 微信/WhatsApp 客户消息：语音/视频附件内联播放器 + 其他附件卡片 */}
          {(isWeChat || isWhatsApp || isModernIm) && isCustomer && thread.attachments && thread.attachments.length > 0 && (() => {
            const otherAtts = thread.attachments.filter((a) => !isImage(a.mimeType));
            if (otherAtts.length === 0) return null;
            return (
              <div className="mt-2 space-y-1.5">
                {otherAtts.map((att) => {
                  // 语音消息使用内联播放器（无需点击进入模态）
                  if (isAudio(att.mimeType)) {
                    return <InlineAudioPlayer key={att.id} attachmentId={att.id} fileName={att.fileName} size={att.size} mimeType={att.mimeType} />;
                  }
                  // 视频消息使用内联预览播放器
                  if (isVideo(att.mimeType)) {
                    return <InlineVideoPlayer key={att.id} attachmentId={att.id} fileName={att.fileName} size={att.size} />;
                  }
                  // 其他附件保持卡片样式
                  const attIdx = thread.attachments!.findIndex((a) => a.id === att.id);
                  return (
                    <button
                      key={att.id}
                      onClick={() => onPreview(thread.attachments!, attIdx)}
                      className="group flex items-center gap-2 px-3 py-2 bg-white/60 dark:bg-slate-700/40 rounded-lg border border-gray-200/60 dark:border-slate-600/50 hover:border-green-300 dark:hover:border-green-600 transition w-full text-left"
                      aria-label={t('serviceOps.threads.previewAttachmentAria', { fileName: att.fileName })}
                    >
                      <div className="w-8 h-8 bg-gray-100 dark:bg-slate-700 rounded-lg flex items-center justify-center flex-shrink-0">
                        <svg className="w-4 h-4 text-gray-500 dark:text-slate-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M7 21h10a2 2 0 002-2V9.414a1 1 0 00-.293-.707l-5.414-5.414A1 1 0 0012.586 3H7a2 2 0 00-2 2v14a2 2 0 002 2z" />
                        </svg>
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="text-xs font-medium text-gray-700 dark:text-slate-200 truncate">{att.fileName}</div>
                        <div className="text-[10px] text-gray-400 dark:text-slate-500">{formatFileSize(att.size)}</div>
                      </div>
                      <svg className="w-3.5 h-3.5 text-gray-400 group-hover:text-green-500 dark:text-slate-500 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                        <path strokeLinecap="round" strokeLinejoin="round" d="M14.752 11.168l-3.197-2.132A1 1 0 0010 9.87v4.263a1 1 0 001.555.832l3.197-2.132a1 1 0 000-1.664z" />
                        <path strokeLinecap="round" strokeLinejoin="round" d="M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                      </svg>
                    </button>
                  );
                })}
              </div>
            );
          })()}

          {/* 非微信/WhatsApp 消息或客服回复：保持原有附件列表样式 */}
          {((!isWeChat && !isWhatsApp && !isModernIm) || !isCustomer) && thread.attachments && thread.attachments.length > 0 && (
            <div className="mt-3 pt-3 border-t border-gray-200/50 dark:border-slate-600/50">
              <div className="text-xs text-gray-500 dark:text-slate-400 mb-2">{t('serviceOps.threads.attachmentsTitle', { count: thread.attachments.length })}</div>
              <div className="flex flex-wrap gap-2">
                {thread.attachments.map((att, idx) => (
                  <button
                    key={att.id}
                    onClick={() => onPreview(thread.attachments!, idx)}
                    className="group flex items-center gap-2 px-3 py-2 bg-white dark:bg-slate-800 rounded-lg border border-gray-200 dark:border-slate-600 hover:border-primary-300 dark:hover:border-primary-500 hover:shadow-sm transition max-w-xs text-left"
                  >
                    {isImage(att.mimeType) ? (
                      <img
                        src={attachmentApi.previewUrl(att.id, 48, 48)}
                        alt={att.fileName}
                        className="w-8 h-8 object-cover rounded"
                      />
                    ) : isAudio(att.mimeType) ? (
                      <div className="w-8 h-8 bg-purple-100 dark:bg-purple-900/40 rounded flex items-center justify-center">
                        <svg className="w-4 h-4 text-purple-500 dark:text-purple-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M19.114 5.636a9 9 0 010 12.728M16.463 8.288a5.25 5.25 0 010 7.424M6.75 8.25l4.72-4.72a.75.75 0 011.28.53v15.88a.75.75 0 01-1.28.53l-4.72-4.72H4.51c-.88 0-1.704-.507-1.938-1.354A9.01 9.01 0 012.25 12c0-.83.112-1.633.322-2.396C2.806 8.756 3.63 8.25 4.51 8.25H6.75z" />
                        </svg>
                      </div>
                    ) : isVideo(att.mimeType) ? (
                      <div className="w-8 h-8 bg-orange-100 dark:bg-orange-900/40 rounded flex items-center justify-center">
                        <svg className="w-4 h-4 text-orange-500 dark:text-orange-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M15 10l4.553-2.276A1 1 0 0121 8.618v6.764a1 1 0 01-1.447.894L15 14M5 18h8a2 2 0 002-2V8a2 2 0 00-2-2H5a2 2 0 00-2 2v8a2 2 0 002 2z" />
                        </svg>
                      </div>
                    ) : (
                      <div className="w-8 h-8 bg-gray-100 dark:bg-slate-700 rounded flex items-center justify-center">
                        <svg className="w-4 h-4 text-gray-500 dark:text-slate-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M7 21h10a2 2 0 002-2V9.414a1 1 0 00-.293-.707l-5.414-5.414A1 1 0 0012.586 3H7a2 2 0 00-2 2v14a2 2 0 002 2z" />
                        </svg>
                      </div>
                    )}
                    <div className="flex-1 min-w-0">
                      <div className="text-xs font-medium text-gray-700 dark:text-slate-200 truncate group-hover:text-primary-700 dark:group-hover:text-primary-400">{att.fileName}</div>
                      <div className="text-[10px] text-gray-400 dark:text-slate-500">{formatFileSize(att.size)}</div>
                    </div>
                    <svg className="w-3.5 h-3.5 text-gray-400 group-hover:text-primary-500 dark:text-slate-500 dark:group-hover:text-primary-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
                      <path strokeLinecap="round" strokeLinejoin="round" d="M2.458 12C3.732 7.943 7.523 5 12 5c4.478 0 8.268 2.943 9.542 7-1.274 4.057-5.064 7-9.542 7-4.477 0-8.268-2.943-9.542-7z" />
                    </svg>
                  </button>
                ))}
              </div>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

export default function ThreadList({ threads, customerAvatar, customerName }: Props) {
  const { t } = useI18n();
  const [preview, setPreview] = useState<{ attachments: AttachmentResponse[]; index: number } | null>(null);

  if (!threads || threads.length === 0) {
    return (
      <div className="text-center py-16 text-gray-400 dark:text-slate-500">
        <svg className="w-12 h-12 mx-auto mb-3 opacity-50" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
        </svg>
        <p className="text-sm">{t('serviceOps.threads.empty')}</p>
      </div>
    );
  }

  const sortedThreads = threads
    .slice()
    .filter((t) => t.state !== ThreadState.Draft)
    .sort((a, b) => {
      const timeDiff = new Date(a.createdAt).getTime() - new Date(b.createdAt).getTime();
      if (timeDiff !== 0) return timeDiff;
      return a.id - b.id;
    });

  return (
    <>
      <div className="space-y-5">
        {sortedThreads.map((t, idx) => {
          const curKey = dateGroupKey(t.createdAt);
          const prevKey = idx > 0 ? dateGroupKey(sortedThreads[idx - 1].createdAt) : null;
          const showSeparator = curKey !== prevKey;
          return (
            <div key={t.id}>
              {showSeparator && (
                <div className="flex items-center gap-3 mb-4" role="separator" aria-label={formatDateGroup(t.createdAt)}>
                  <div className="flex-1 h-px bg-gray-200 dark:bg-slate-700" />
                  <span className="text-[11px] font-medium text-gray-500 dark:text-slate-400 px-2 py-0.5 bg-gray-100 dark:bg-slate-800 rounded-full">
                    {formatDateGroup(t.createdAt)}
                  </span>
                  <div className="flex-1 h-px bg-gray-200 dark:bg-slate-700" />
                </div>
              )}
              <ThreadBubble
                thread={t}
                customerAvatar={customerAvatar}
                customerName={customerName}
                onPreview={(atts, i) => setPreview({ attachments: atts, index: i })}
              />
            </div>
          );
        })}
      </div>

      {preview && (
        <AttachmentPreview
          attachments={preview.attachments}
          initialIndex={preview.index}
          onClose={() => setPreview(null)}
        />
      )}
    </>
  );
}
