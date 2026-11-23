import { useState, useEffect, useRef, useCallback, useMemo, KeyboardEvent, ChangeEvent } from 'react';
import { chatApi } from '../api';
import { safeAttachmentUrl } from '../utils/format';
import { useI18n } from '../context/I18nContext';
import { toIntlLocale } from '../i18n/locale';
import type { TranslationKey } from '../locales/zh-Hans';
import type { TranslateParams } from '../i18n/types';
import type {
  ChatSessionDto,
  ChatMessageDto,
  InitiateChatRequest,
  RateChatRequest,
} from '../api/types';
import {
  useCustomerSignalR,
  useCustomerRealtimeChat,
  useCustomerChatMessageRecalled,
  useCustomerChatMessageEdited,
  useCustomerChatMessageRead,
  useCustomerChatTyping,
} from '../hooks/useCustomerSignalR';
import {
  Send,
  MessageCircle,
  X,
  Star,
  Minimize2,
  Maximize2,
  Loader2,
  CheckCircle2,
  AlertCircle,
  WifiOff,
  Wifi,
  Headphones,
  Paperclip,
  Smile,
  Sparkles,
  ThumbsUp,
  Meh,
  Frown,
  AlertTriangle,
  FileText,
  Link,
  Globe,
  Hash,
  Clock,
} from 'lucide-react';

/** 客户聊天窗口的状态 */
type WidgetStage = 'initiate' | 'chatting' | 'rating' | 'completed';

/**
 * 词条解析函数（`useI18n().t` 的类型）。
 * 模块级纯函数（`renderCustomerMessageContent`）取不到 Hook，按既有约定由调用方传入。
 */
type TranslateFn = (key: TranslationKey, params?: TranslateParams) => string;

/** 剩余 ms → MM:SS 文本 */
function formatCountdown(ms: number): string {
  if (!isFinite(ms) || ms <= 0) return '00:00';
  const totalSec = Math.floor(ms / 1000);
  const m = Math.floor(totalSec / 60).toString().padStart(2, '0');
  const s = (totalSec % 60).toString().padStart(2, '0');
  return `${m}:${s}`;
}

/** 表单状态 */
interface InitiateForm {
  customerName: string;
  customerEmail: string;
  customerPhone: string;
  subject: string;
  initialMessage: string;
}

const INITIATE_FORM_DEFAULT: InitiateForm = {
  customerName: '',
  customerEmail: '',
  customerPhone: '',
  subject: '',
  initialMessage: '',
};

/** 常用表情(轻量级,无需第三方库) */
const COMMON_EMOJIS = [
  '😀', '😃', '😄', '😁', '🙂', '😊', '😉', '😍',
  '🤔', '😎', '😅', '😂', '😭', '😱', '😡', '👍',
  '👎', '👌', '👏', '🙏', '💪', '🎉', '🎁', '❤️',
];

/**
 * sessionStorage key:在当前浏览器标签页会话内持久化客户信息以便回访。
 * 安全说明（L-FE4）：客户姓名/邮箱/手机号属于 PII，使用 sessionStorage 而非 localStorage，
 * 关闭标签页后自动清除，降低本地存储泄漏个人身份信息的风险。
 */
const CUSTOMER_INFO_KEY = 'nonead_customer_chat_info';

interface PersistedCustomerInfo {
  customerName: string;
  customerEmail: string;
  customerPhone: string;
}

function loadPersistedCustomerInfo(): PersistedCustomerInfo | null {
  try {
    const raw = sessionStorage.getItem(CUSTOMER_INFO_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw);
    if (parsed && typeof parsed.customerName === 'string' && typeof parsed.customerEmail === 'string') {
      // 旧数据兼容：没有 customerPhone 字段时返回空字符串
      return {
        customerName: parsed.customerName,
        customerEmail: parsed.customerEmail,
        customerPhone: typeof parsed.customerPhone === 'string' ? parsed.customerPhone : '',
      };
    }
  } catch {
    // sessionStorage 不可用或数据损坏,忽略
  }
  return null;
}

function savePersistedCustomerInfo(info: PersistedCustomerInfo) {
  try {
    sessionStorage.setItem(CUSTOMER_INFO_KEY, JSON.stringify(info));
  } catch {
    // 静默失败
  }
}

/**
 * 严格邮箱合规性校验：
 * - 禁止空白字符
 * - 必须是 本地部分@域名.TLD 结构
 * - 本地部分：允许字母数字和 !#$%&'*+/=?^_`{|}~.- ，不能以 . 开头/结尾，不能两个 . 连续
 * - 域名部分：允许字母数字和中划线，不能以中划线开头/结尾，至少两级域名（至少含一个 .），顶级域不能全数字
 * - 最大长度不超过 100
 */
function isValidEmail(email: string): boolean {
  if (!email || email.length > 100) return false;
  // 禁止控制字符和空白
  if (/[\s\p{Cc}]/u.test(email)) return false;
  const at = email.lastIndexOf('@');
  if (at <= 0 || at === email.length - 1) return false;
  const local = email.substring(0, at);
  const domain = email.substring(at + 1);

  // 本地部分：不能超出 RFC 5322 允许字符 + 不能以点开头/结尾 + 不能两个点连续
  if (!/^[A-Za-z0-9!#$%&'*+/=?^_`{|}~.-]+$/.test(local)) return false;
  if (local.startsWith('.') || local.endsWith('.') || /\.\./.test(local)) return false;

  // 域名部分：至少有一个点
  const dotIdx = domain.lastIndexOf('.');
  if (dotIdx <= 0 || dotIdx === domain.length - 1) return false;
  const tld = domain.substring(dotIdx + 1);
  // TLD 不能全数字（避免 foo@123.456 这种假地址）
  if (/^\d+$/.test(tld)) return false;
  // 每个标签必须是字母数字 + 中划线，不能以中划线开头或结尾
  const labels = domain.split('.');
  for (const label of labels) {
    if (!label || /^-|-$/.test(label)) return false;
    if (!/^[A-Za-z0-9-]+$/.test(label)) return false;
  }
  return true;
}

/**
 * 手机号合规性校验（前端强校验，后端还会二次清洗）：
 * 接受格式（匹配后端 DTO 的正则）：
 *   1) 纯 11 位大陆手机号：1[3-9]xxxxxxxxx
 *   2) +86 前缀 + 11 位大陆手机号：+861[3-9]xxxxxxxxx
 *   3) 可选空格/横线分段（138-1234-5678、138 1234 5678、+86 138-1234-5678）
 */
function isValidPhone(phone: string): boolean {
  if (!phone || phone.length > 50) return false;
  const stripped = phone.replace(/[\s\-（）()【】[\]]/g, '');
  // 大陆手机号（11位）
  if (/^1[3-9]\d{9}$/.test(stripped)) return true;
  // +86 前缀大陆手机号
  if (/^\+861[3-9]\d{9}$/.test(stripped)) return true;
  // 86 前缀大陆手机号
  if (/^861[3-9]\d{9}$/.test(stripped)) return true;
  // 国际号码宽松（含单前导 +，7~20 位数字）
  const plusCount = stripped.split('+').length - 1;
  if (plusCount > 1) return false;
  const digitCount = (stripped.match(/\d/g) || []).length;
  if (digitCount < 7 || digitCount > 20) return false;
  if (plusCount === 1 && !stripped.startsWith('+')) return false;
  return /^\+?\d+$/.test(stripped);
}

/** 格式化时间为 HH:MM */
function formatTime(dateStr: string, language: string): string {
  try {
    const d = new Date(dateStr);
    if (isNaN(d.getTime())) return '';
    return d.toLocaleTimeString(toIntlLocale(language), { hour: '2-digit', minute: '2-digit' });
  } catch {
    return '';
  }
}

/** 滚动到底部 */
function useScrollToBottom<T>(deps: T[]) {
  const endRef = useRef<HTMLDivElement>(null);
  useEffect(() => {
    endRef.current?.scrollIntoView({ behavior: 'smooth' });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);
  return endRef;
}

// =====================================================
// 发起咨询表单(欢迎卡)
// =====================================================
interface InitiateFormProps {
  onInitiate: (req: InitiateChatRequest) => Promise<void>;
  initiating: boolean;
}

function InitiateForm({ onInitiate, initiating }: InitiateFormProps) {
  const { t } = useI18n();
  const [form, setForm] = useState<InitiateForm>(INITIATE_FORM_DEFAULT);
  const [errors, setErrors] = useState<Partial<Record<keyof InitiateForm, string>>>({});

  // 回访客户自动填充
  useEffect(() => {
    const persisted = loadPersistedCustomerInfo();
    if (persisted) {
      setForm((prev) => ({
        ...prev,
        customerName: persisted.customerName,
        customerEmail: persisted.customerEmail,
        customerPhone: persisted.customerPhone || '',
      }));
    }
  }, []);

  const handleChange = useCallback(
    (field: keyof InitiateForm) => (e: ChangeEvent<HTMLInputElement | HTMLTextAreaElement>) => {
      setForm((prev) => ({ ...prev, [field]: e.target.value }));
      if (errors[field]) {
        setErrors((prev) => ({ ...prev, [field]: undefined }));
      }
    },
    [errors],
  );

  const validate = useCallback((): boolean => {
    const nextErrors: Partial<Record<keyof InitiateForm, string>> = {};
    if (!form.customerName.trim()) {
      nextErrors.customerName = t('chat.widget.form.error.nameRequired');
    } else if (form.customerName.trim().length > 50) {
      nextErrors.customerName = t('chat.widget.form.error.nameTooLong');
    }
    if (!form.customerEmail.trim()) {
      nextErrors.customerEmail = t('chat.widget.form.error.emailRequired');
    } else if (!isValidEmail(form.customerEmail.trim())) {
      nextErrors.customerEmail = t('chat.widget.form.error.emailInvalid');
    } else if (form.customerEmail.trim().length > 100) {
      nextErrors.customerEmail = t('chat.widget.form.error.emailTooLong');
    }
    if (!form.customerPhone.trim()) {
      nextErrors.customerPhone = t('chat.widget.form.error.phoneRequired');
    } else if (!isValidPhone(form.customerPhone.trim())) {
      nextErrors.customerPhone = t('chat.widget.form.error.phoneInvalid');
    }
    if (form.subject && form.subject.length > 200) {
      nextErrors.subject = t('chat.widget.form.error.subjectTooLong');
    }
    if (form.initialMessage && form.initialMessage.length > 2000) {
      nextErrors.initialMessage = t('chat.widget.form.error.messageTooLong');
    }
    setErrors(nextErrors);
    return Object.keys(nextErrors).length === 0;
  }, [form, t]);

  const handleSubmit = useCallback(async () => {
    if (!validate()) return;
    await onInitiate({
      customerName: form.customerName.trim(),
      customerEmail: form.customerEmail.trim(),
      customerPhone: form.customerPhone.trim(),
      subject: form.subject.trim() || null,
      initialMessage: form.initialMessage.trim() || null,
    });
  }, [form, validate, onInitiate]);

  const handleKeyDown = useCallback(
    (e: KeyboardEvent<HTMLTextAreaElement>) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        handleSubmit();
      }
    },
    [handleSubmit],
  );

  return (
    <div className="flex flex-col h-full overflow-y-auto bg-gradient-to-b from-white to-slate-50/60 dark:from-slate-800 dark:to-slate-900/40">
      {/* 欢迎卡 */}
      <div className="relative px-5 pt-6 pb-4 overflow-hidden">
        <div className="absolute -top-16 -right-16 w-40 h-40 rounded-full bg-gradient-to-br from-blue-400/30 via-indigo-400/30 to-purple-400/30 blur-3xl pointer-events-none" />
        <div className="absolute -bottom-20 -left-16 w-40 h-40 rounded-full bg-gradient-to-br from-emerald-300/30 via-teal-300/30 to-blue-300/30 blur-3xl pointer-events-none" />

        <div className="relative flex flex-col items-center text-center">
          <div className="relative">
            <div className="absolute inset-0 w-16 h-16 rounded-2xl bg-gradient-to-br from-blue-400 to-indigo-500 blur-xl opacity-50 animate-pulse" />
            <div className="relative w-16 h-16 rounded-2xl bg-gradient-to-br from-blue-500 via-indigo-500 to-purple-600 flex items-center justify-center text-white shadow-xl shadow-indigo-500/30">
              <Headphones className="w-8 h-8" strokeWidth={2} />
            </div>
          </div>
          <h2 className="text-xl font-extrabold mt-4 bg-gradient-to-r from-slate-900 via-indigo-800 to-slate-900 dark:from-white dark:via-indigo-100 dark:to-white bg-clip-text text-transparent">
            {t('chat.widget.welcome.title')}
          </h2>
          <p className="text-xs text-slate-500 dark:text-slate-400 mt-2 leading-relaxed max-w-xs">
            {t('chat.widget.welcome.subtitle')}
          </p>
          <div className="flex items-center gap-4 mt-4 text-[11px]">
            <div className="flex items-center gap-1 text-emerald-600 dark:text-emerald-400 font-semibold">
              <span className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse" />
              {t('chat.widget.welcome.online')}
            </div>
            <span className="text-slate-300 dark:text-slate-700">·</span>
            <span className="text-slate-500 dark:text-slate-400 font-medium">{t('chat.widget.welcome.secure')}</span>
            <span className="text-slate-300 dark:text-slate-700">·</span>
            <span className="text-slate-500 dark:text-slate-400 font-medium">{t('chat.widget.welcome.private')}</span>
          </div>
        </div>
      </div>

      {/* 表单 */}
      <div className="px-5 pb-6 space-y-4">
        <div className="group">
          <label className="block text-xs font-bold text-slate-700 dark:text-slate-200 mb-1.5 flex items-center gap-1.5">
            {t('chat.widget.form.name')} <span className="text-red-500">*</span>
          </label>
          <input
            type="text"
            value={form.customerName}
            onChange={handleChange('customerName')}
            maxLength={50}
            disabled={initiating}
            placeholder={t('chat.widget.form.namePlaceholder')}
            className={`w-full px-4 py-2.5 text-sm border-2 rounded-2xl bg-white dark:bg-slate-800 text-slate-900 dark:text-slate-100 placeholder:text-slate-400 focus:outline-none focus:ring-4 transition-all shadow-sm ${
              errors.customerName
                ? 'border-red-400 focus:ring-red-500/20 focus:border-red-500'
                : 'border-slate-200 dark:border-slate-700 focus:ring-blue-500/20 focus:border-blue-500'
            } disabled:opacity-60`}
          />
          {errors.customerName && (
            <p className="mt-1.5 text-[11px] text-red-500 flex items-center gap-1 font-medium">
              <AlertCircle className="w-3 h-3" />
              {errors.customerName}
            </p>
          )}
        </div>

        <div className="group">
          <label className="block text-xs font-bold text-slate-700 dark:text-slate-200 mb-1.5 flex items-center gap-1.5">
            {t('chat.widget.form.email')} <span className="text-red-500">*</span>
          </label>
          <input
            type="email"
            value={form.customerEmail}
            onChange={handleChange('customerEmail')}
            maxLength={100}
            disabled={initiating}
            placeholder="example@domain.com"
            className={`w-full px-4 py-2.5 text-sm border-2 rounded-2xl bg-white dark:bg-slate-800 text-slate-900 dark:text-slate-100 placeholder:text-slate-400 focus:outline-none focus:ring-4 transition-all shadow-sm ${
              errors.customerEmail
                ? 'border-red-400 focus:ring-red-500/20 focus:border-red-500'
                : 'border-slate-200 dark:border-slate-700 focus:ring-blue-500/20 focus:border-blue-500'
            } disabled:opacity-60`}
          />
          {errors.customerEmail && (
            <p className="mt-1.5 text-[11px] text-red-500 flex items-center gap-1 font-medium">
              <AlertCircle className="w-3 h-3" />
              {errors.customerEmail}
            </p>
          )}
        </div>

        {/* 联系手机(必填)：位于邮箱之后、主题之前 */}
        <div className="group">
          <label className="block text-xs font-bold text-slate-700 dark:text-slate-200 mb-1.5 flex items-center gap-1.5">
            {t('chat.widget.form.phone')} <span className="text-red-500">*</span>
          </label>
          <input
            type="tel"
            value={form.customerPhone}
            onChange={handleChange('customerPhone')}
            maxLength={30}
            disabled={initiating}
            inputMode="tel"
            autoComplete="tel-national"
            placeholder={t('chat.widget.form.phonePlaceholder')}
            className={`w-full px-4 py-2.5 text-sm border-2 rounded-2xl bg-white dark:bg-slate-800 text-slate-900 dark:text-slate-100 placeholder:text-slate-400 focus:outline-none focus:ring-4 transition-all shadow-sm ${
              errors.customerPhone
                ? 'border-red-400 focus:ring-red-500/20 focus:border-red-500'
                : 'border-slate-200 dark:border-slate-700 focus:ring-blue-500/20 focus:border-blue-500'
            } disabled:opacity-60`}
          />
          {errors.customerPhone && (
            <p className="mt-1.5 text-[11px] text-red-500 flex items-center gap-1 font-medium">
              <AlertCircle className="w-3 h-3" />
              {errors.customerPhone}
            </p>
          )}
        </div>

        <div className="group">
          <label className="block text-xs font-bold text-slate-700 dark:text-slate-200 mb-1.5 flex items-center justify-between">
            <span>{t('chat.widget.form.subject')} <span className="text-slate-400 text-[10px] font-normal">{t('chat.widget.form.optional')}</span></span>
            {form.subject.length > 0 && (
              <span className={`text-[10px] font-semibold px-1.5 py-0.5 rounded-md ${
                form.subject.length > 160 ? 'bg-red-50 text-red-500 dark:bg-red-900/20' : 'bg-slate-100 text-slate-500 dark:bg-slate-700/50 dark:text-slate-400'
              }`}>
                {form.subject.length}/200
              </span>
            )}
          </label>
          <input
            type="text"
            value={form.subject}
            onChange={handleChange('subject')}
            maxLength={200}
            disabled={initiating}
            placeholder={t('chat.widget.form.subjectPlaceholder')}
            className="w-full px-4 py-2.5 text-sm border-2 border-slate-200 dark:border-slate-700 rounded-2xl bg-white dark:bg-slate-800 text-slate-900 dark:text-slate-100 placeholder:text-slate-400 focus:outline-none focus:ring-4 focus:ring-blue-500/20 focus:border-blue-500 transition-all shadow-sm disabled:opacity-60"
          />
        </div>

        <div className="group">
          <label className="block text-xs font-bold text-slate-700 dark:text-slate-200 mb-1.5 flex items-center justify-between">
            <span>{t('chat.widget.form.message')} <span className="text-slate-400 text-[10px] font-normal">{t('chat.widget.form.optional')}</span></span>
            {form.initialMessage.length > 0 && (
              <span className={`text-[10px] font-semibold px-1.5 py-0.5 rounded-md ${
                form.initialMessage.length > 1600 ? 'bg-red-50 text-red-500 dark:bg-red-900/20' : 'bg-slate-100 text-slate-500 dark:bg-slate-700/50 dark:text-slate-400'
              }`}>
                {form.initialMessage.length}/2000
              </span>
            )}
          </label>
          <textarea
            value={form.initialMessage}
            onChange={handleChange('initialMessage')}
            onKeyDown={handleKeyDown}
            maxLength={2000}
            rows={4}
            disabled={initiating}
            placeholder={t('chat.widget.form.messagePlaceholder')}
            className="w-full px-4 py-2.5 text-sm border-2 border-slate-200 dark:border-slate-700 rounded-2xl bg-white dark:bg-slate-800 text-slate-900 dark:text-slate-100 placeholder:text-slate-400 resize-none focus:outline-none focus:ring-4 focus:ring-blue-500/20 focus:border-blue-500 transition-all shadow-sm disabled:opacity-60 leading-relaxed"
          />
        </div>

        <button
          onClick={handleSubmit}
          disabled={initiating}
          className="w-full py-3.5 bg-gradient-to-r from-blue-500 via-indigo-500 to-purple-600 hover:from-blue-600 hover:via-indigo-600 hover:to-purple-700 text-white text-sm font-bold rounded-2xl shadow-lg shadow-indigo-500/30 hover:shadow-xl hover:shadow-indigo-500/40 hover:-translate-y-0.5 transition-all disabled:from-slate-300 disabled:via-slate-300 disabled:to-slate-400 disabled:shadow-none disabled:cursor-not-allowed disabled:translate-y-0 flex items-center justify-center gap-2 active:scale-[0.99] relative overflow-hidden"
        >
          {initiating ? (
            <>
              <Loader2 className="w-4.5 h-4.5 animate-spin" />
              {t('chat.widget.form.connecting')}
            </>
          ) : (
            <>
              <MessageCircle className="w-4.5 h-4.5" strokeWidth={2} />
              {t('chat.widget.form.start')}
            </>
          )}
          <span className="absolute inset-0 -translate-x-full bg-gradient-to-r from-transparent via-white/20 to-transparent group-hover:translate-x-full transition-transform duration-1000 pointer-events-none" />
        </button>

        <p className="text-center text-[10px] text-slate-400 dark:text-slate-500 leading-relaxed">
          {t('chat.widget.form.terms')}
        </p>
        <p className="text-center text-[10px] text-slate-400 dark:text-slate-500 leading-relaxed mt-1">
          © 2009-2026 Nonead Corporation. All rights reserved.
        </p>
      </div>
    </div>
  );
}

// =====================================================
// 客户侧消息气泡(简化版,客户只看消息,无需撤回/编辑)
// =====================================================

/**
 * 客户侧消息内容渲染(和坐席端保持一致的内容识别能力):
 * - 图片 / 附件 URL(行内)
 * - 超链接
 * - @提及
 */
const CUSTOMER_MENTION_REGEX = /@\[([^\]]+)\]/g;
// 图片: http(s) 开头 + 图片扩展名结尾(可选 ?query)
const IMAGE_URL_REGEX = /https?:\/\/[^\s<>"`]+(?:\.png|\.jpg|\.jpeg|\.gif|\.webp|\.bmp|\.svg)(?:\?[^\s<>"`]*)?/gi;
// 附件: 常见文档/压缩/媒体扩展名
const ATTACHMENT_URL_REGEX = /https?:\/\/[^\s<>"`]+\.(?:pdf|doc|docx|xls|xlsx|ppt|pptx|txt|md|csv|zip|rar|7z|tar|gz|mp3|mp4|avi|mov|wav|flac|m4a)(?:\?[^\s<>"`]*)?/gi;
// 超链接(剩余的 http 链接)
const LINK_URL_REGEX = /https?:\/\/[^\s<>"`]+/gi;

function renderCustomerMessageContent(text: string, isCustomer: boolean, t: TranslateFn) {
  // 0. 快速路径:纯文本
  if (!text) return text;
  const hasMention = /@\[/.test(text);
  const hasUrl = /https?:\/\//i.test(text);
  if (!hasMention && !hasUrl) return text;

  type Segment =
    | { kind: 'plain'; value: string }
    | { kind: 'mention'; value: string }
    | { kind: 'image'; value: string }
    | { kind: 'attachment'; value: string }
    | { kind: 'link'; value: string };

  const segments: Segment[] = [{ kind: 'plain', value: text }];

  const splitSegments = <T extends Segment['kind']>(
    kind: T,
    regex: RegExp,
    extractValue: (match: RegExpExecArray) => string,
  ) => {
    const next: Segment[] = [];
    for (const seg of segments) {
      if (seg.kind !== 'plain') { next.push(seg); continue; }
      let lastIndex = 0;
      let match: RegExpExecArray | null;
      regex.lastIndex = 0;
      while ((match = regex.exec(seg.value)) !== null) {
        if (match.index > lastIndex) {
          next.push({ kind: 'plain', value: seg.value.substring(lastIndex, match.index) });
        }
        next.push({ kind, value: extractValue(match) } as Segment);
        lastIndex = match.index + match[0].length;
      }
      if (lastIndex < seg.value.length) {
        next.push({ kind: 'plain', value: seg.value.substring(lastIndex) });
      }
    }
    segments.length = 0;
    segments.push(...next);
  };

  // 解析顺序:提及 -> 图片 -> 附件 -> 剩余链接(图片/附件已被前面识别的不会再被 LINK 匹配)
  splitSegments('mention', CUSTOMER_MENTION_REGEX, (m) => m[1]);
  splitSegments('image', IMAGE_URL_REGEX, (m) => m[0]);
  splitSegments('attachment', ATTACHMENT_URL_REGEX, (m) => m[0]);
  splitSegments('link', LINK_URL_REGEX, (m) => m[0]);

  const mentionCls = isCustomer
    ? 'inline-flex items-center px-1 mx-0.5 rounded bg-white/25 text-white font-medium'
    : 'inline-flex items-center px-1 mx-0.5 rounded bg-blue-100 text-blue-700 font-medium dark:bg-blue-900/40 dark:text-blue-300';

  const linkBodyCls = isCustomer
    ? 'inline-flex items-center gap-1 px-2 py-0.5 my-0.5 rounded-md bg-white/20 text-white hover:bg-white/25 underline-offset-2 decoration-white/50 underline transition-colors'
    : 'inline-flex items-center gap-1 px-2 py-0.5 my-0.5 rounded-md bg-blue-50 dark:bg-blue-900/30 text-blue-700 dark:text-blue-300 hover:bg-blue-100 dark:hover:bg-blue-900/50 underline-offset-2 decoration-blue-500/60 underline transition-colors';

  const imageWrapperCls = isCustomer
    ? 'my-1.5 -mx-1.5'
    : 'my-1.5 -mx-1.5';

  return segments.map((seg, i) => {
    switch (seg.kind) {
      case 'plain':
        return <span key={`p-${i}`}>{seg.value}</span>;
      case 'mention':
        return (
          <span key={`m-${i}`} className={mentionCls}>@{seg.value}</span>
        );
      case 'image':
        return (
          <span key={`img-${i}`} className={`block ${imageWrapperCls}`}>
            <img
              src={seg.value}
              alt={t('chat.widget.message.imageAlt')}
              loading="lazy"
              className="max-w-full max-h-52 rounded-lg border border-black/5 shadow-sm cursor-pointer hover:opacity-95 transition-opacity bg-white/40"
              onClick={(e) => {
                e.preventDefault();
                e.stopPropagation();
                window.open(seg.value, '_blank', 'noopener,noreferrer');
              }}
            />
          </span>
        );
      case 'attachment': {
        const name = seg.value.split('/').pop()?.split('?')[0] || t('chat.widget.message.fileFallback');
        return (
          <a
            key={`att-${i}`}
            href={seg.value}
            target="_blank"
            rel="noopener noreferrer"
            className={`inline-flex items-center gap-1.5 px-2.5 py-1 my-1 rounded-lg text-xs font-medium transition-all ${
              isCustomer
                ? 'bg-white/20 hover:bg-white/25 text-white'
                : 'bg-emerald-50 dark:bg-emerald-900/30 text-emerald-700 dark:text-emerald-300 hover:bg-emerald-100 dark:hover:bg-emerald-900/50'
            }`}
            onClick={(e) => e.stopPropagation()}
          >
            <FileText className="w-3.5 h-3.5 flex-shrink-0" />
            <span className="max-w-[140px] truncate">{name}</span>
            <Globe className="w-3 h-3 opacity-70 flex-shrink-0" />
          </a>
        );
      }
      case 'link': {
        const display = seg.value.replace(/^https?:\/\//i, '').slice(0, 48);
        return (
          <a
            key={`a-${i}`}
            href={seg.value}
            target="_blank"
            rel="noopener noreferrer"
            className={linkBodyCls}
            onClick={(e) => e.stopPropagation()}
          >
            <Link className="w-3.5 h-3.5 flex-shrink-0 opacity-75" />
            <span className="max-w-[160px] truncate">{display}</span>
            <Hash className="w-3 h-3 opacity-50 flex-shrink-0" />
          </a>
        );
      }
    }
  });
}

interface CustomerMessageBubbleProps {
  message: ChatMessageDto;
}

function CustomerMessageBubble({ message }: CustomerMessageBubbleProps) {
  const { t, language } = useI18n();
  // 系统消息(横幅式,适配多行满意度调查/感谢通知含链接场景)
  if (message.senderType === 'system') {
    const satHint =
      /满意度|评价|评分|打分|调查|感谢您|感谢您的|会话已结束|survey|satisfaction|rate/i.test(
        message.content,
      );
    return (
      <div className="flex justify-center my-3 px-2">
        <div
          className={`max-w-[94%] w-full rounded-2xl px-3.5 py-3 border shadow-sm relative overflow-hidden ${
            satHint
              ? 'bg-gradient-to-br from-violet-50/85 via-fuchsia-50/70 to-blue-50/80 dark:from-violet-900/25 dark:via-fuchsia-900/20 dark:to-blue-900/20 text-slate-700 dark:text-slate-200 border-violet-200/70 dark:border-violet-800/30'
              : 'bg-gradient-to-br from-slate-50/95 to-slate-100/80 dark:from-slate-800/70 dark:to-slate-900/50 text-slate-600 dark:text-slate-300 border-slate-200/70 dark:border-slate-700/50'
          }`}
        >
          {satHint && (
            <>
              <div className="pointer-events-none absolute -top-8 -right-8 w-20 h-20 rounded-full bg-violet-400/10 blur-2xl" />
              <div className="pointer-events-none absolute -bottom-8 -left-8 w-20 h-20 rounded-full bg-fuchsia-400/10 blur-2xl" />
            </>
          )}
          <div className="relative flex flex-col">
            <div className="flex items-center gap-2 mb-1.5">
              <div
                className={`w-6.5 h-6.5 rounded-xl flex items-center justify-center shadow-sm flex-shrink-0 ${
                  satHint
                    ? 'bg-gradient-to-br from-violet-500 to-fuchsia-500 text-white'
                    : 'bg-slate-200/80 dark:bg-slate-700/70 text-slate-500 dark:text-slate-300'
                }`}
                style={{ width: '26px', height: '26px' }}
              >
                {satHint ? (
                  <Sparkles className="w-3.5 h-3.5" strokeWidth={2.2} />
                ) : (
                  <AlertCircle className="w-3.5 h-3.5" strokeWidth={2} />
                )}
              </div>
              <div className="flex-1 min-w-0 flex items-center gap-2">
                <span className={`text-[11px] font-bold ${satHint ? 'text-violet-700 dark:text-violet-300' : 'text-slate-600 dark:text-slate-300'}`}>
                  {satHint ? t('chat.widget.message.satisfactionSurvey') : t('chat.widget.message.systemNotice')}
                </span>
                <span className="text-[10px] text-slate-400 dark:text-slate-500 tabular-nums flex-shrink-0">
                  {formatTime(message.createdAt, language)}
                </span>
              </div>
            </div>
            <p
              className={`text-sm whitespace-pre-wrap break-words leading-relaxed ${
                satHint ? 'text-slate-700 dark:text-slate-200' : 'text-slate-600 dark:text-slate-300'
              }`}
            >
              {renderCustomerMessageContent(message.content, false, t)}
            </p>
          </div>
        </div>
      </div>
    );
  }

  const isCustomer = message.senderType === 'customer';

  return (
    <div className={`flex ${isCustomer ? 'justify-end' : 'justify-start'} gap-2 mb-3.5 last:mb-0 group`}>
      {!isCustomer && (
        <div className="w-8 h-8 rounded-xl flex items-center justify-center text-white text-xs font-bold shadow-md flex-shrink-0 bg-gradient-to-br from-emerald-500 to-teal-600 group-hover:scale-110 transition-transform">
          {message.senderName?.charAt(0) || t('chat.widget.message.agentInitial')}
        </div>
      )}

      <div className={`max-w-[80%] flex flex-col ${isCustomer ? 'items-end' : 'items-start'}`}>
        <div className="flex items-center gap-2 mb-1 px-0.5">
          <span className="text-[11px] font-semibold text-slate-600 dark:text-slate-400">
            {message.senderName || (isCustomer ? t('chat.widget.message.you') : t('chat.widget.message.agent'))}
          </span>
          <span className="text-[10px] text-slate-400 dark:text-slate-500">
            {formatTime(message.createdAt, language)}
          </span>
          {message.editedAt && (
            <span className="text-[10px] text-slate-400 dark:text-slate-500 px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-700/50">{t('chat.widget.message.edited')}</span>
          )}
        </div>

        <div
          className={`px-4 py-2.5 shadow-sm ${
            message.isRecalled
              ? 'bg-gradient-to-br from-slate-100 to-slate-50 dark:from-slate-700/40 dark:to-slate-800/30 text-slate-400 dark:text-slate-500 italic rounded-2xl text-xs border border-dashed border-slate-200 dark:border-slate-600'
              : isCustomer
                ? 'bg-gradient-to-br from-blue-500 via-indigo-500 to-purple-600 text-white rounded-2xl rounded-br-md shadow-lg shadow-indigo-500/20'
                : 'bg-white dark:bg-slate-800 text-slate-900 dark:text-slate-100 rounded-2xl rounded-bl-md border border-slate-200/70 dark:border-slate-700/60'
          }`}
        >
          {message.isRecalled ? (
            t('chat.widget.message.recalled')
          ) : (
            <>
              {message.content && (
                <p className="text-sm whitespace-pre-wrap break-words leading-relaxed">
                  {renderCustomerMessageContent(message.content, isCustomer, t)}
                </p>
              )}
              {message.attachmentUrl && (
                <a
                  href={safeAttachmentUrl(message.attachmentUrl)}
                  target="_blank"
                  rel="noopener noreferrer"
                  className={`mt-2 inline-flex items-center gap-2 text-xs font-medium px-2.5 py-1.5 rounded-lg transition-all hover:scale-[1.02] ${
                    isCustomer
                      ? 'bg-white/20 hover:bg-white/25 text-white'
                      : 'bg-blue-50 hover:bg-blue-100 dark:bg-blue-900/30 dark:hover:bg-blue-900/40 text-blue-700 dark:text-blue-300'
                  }`}
                >
                  <Paperclip className="w-3.5 h-3.5" />
                  {message.attachmentName || t('chat.widget.message.viewAttachment')}
                </a>
              )}
            </>
          )}
        </div>

        {/* 已读状态(仅客户自己发送的消息) */}
        {isCustomer && !message.isRecalled && (
          <div className="flex items-center gap-1 mt-1 px-0.5">
            {message.isRead ? (
              <>
                <CheckCircle2 className="w-3 h-3 text-emerald-500" />
                <span className="text-[10px] font-semibold text-emerald-600 dark:text-emerald-400">
                  {t('chat.widget.message.read')}
                </span>
              </>
            ) : (
              <>
                <svg className="w-3 h-3 text-slate-400" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M5 13l4 4L19 7" />
                </svg>
                <span className="text-[10px] text-slate-400 dark:text-slate-500">
                  {t('chat.widget.message.sent')}
                </span>
              </>
            )}
          </div>
        )}
      </div>

      {isCustomer && (
        <div className="w-8 h-8 rounded-xl flex items-center justify-center text-white text-xs font-bold shadow-md flex-shrink-0 bg-gradient-to-br from-blue-500 to-indigo-600 group-hover:scale-110 transition-transform">
          {message.senderName?.charAt(0) || t('chat.widget.message.you')}
        </div>
      )}
    </div>
  );
}

// =====================================================
// 输入指示器(显示"对方正在输入")
// =====================================================
interface TypingIndicatorProps {
  show: boolean;
  senderName?: string;
}

function TypingIndicator({ show, senderName }: TypingIndicatorProps) {
  const { t } = useI18n();
  if (!show) return null;
  return (
    <div className="flex items-center gap-2 px-1 mb-2 text-xs text-slate-500 dark:text-slate-400">
      <div className="flex gap-1">
        <span className="w-1.5 h-1.5 bg-slate-400 dark:bg-slate-500 rounded-full animate-bounce" style={{ animationDelay: '0ms' }} />
        <span className="w-1.5 h-1.5 bg-slate-400 dark:bg-slate-500 rounded-full animate-bounce" style={{ animationDelay: '150ms' }} />
        <span className="w-1.5 h-1.5 bg-slate-400 dark:bg-slate-500 rounded-full animate-bounce" style={{ animationDelay: '300ms' }} />
      </div>
      <span>{t('chat.widget.typing', { name: senderName || t('chat.widget.message.agent') })}</span>
    </div>
  );
}

// =====================================================
// 评分表单
// =====================================================
interface RatingFormProps {
  session: ChatSessionDto;
  onSubmit: (req: RateChatRequest) => Promise<void>;
  submitting: boolean;
  onSkip: () => void;
  /** 评分等待剩余毫秒（null = 无倒计时） */
  countdownRemainMs?: number | null;
}

function RatingForm({ onSubmit, submitting, onSkip, countdownRemainMs = null }: RatingFormProps) {
  const { t } = useI18n();
  const [rating, setRating] = useState<number>(0);
  const [hoverRating, setHoverRating] = useState<number>(0);
  const [comment, setComment] = useState('');
  const [error, setError] = useState<string>('');

  /** 星级 → 词条 key（使用点必须经 `t()` 解析） */
  const ratingLabelKeys: Record<number, TranslationKey> = {
    1: 'chat.widget.rating.veryDissatisfied',
    2: 'chat.widget.rating.dissatisfied',
    3: 'chat.widget.rating.neutral',
    4: 'chat.widget.rating.satisfied',
    5: 'chat.widget.rating.verySatisfied',
  };

  const handleSubmit = useCallback(async () => {
    if (rating < 1 || rating > 5) {
      setError(t('chat.widget.rating.required'));
      return;
    }
    await onSubmit({ rating, comment: comment.trim() || null });
  }, [rating, comment, onSubmit, t]);

  const displayRating = hoverRating || rating;

  return (
    <div className="flex flex-col h-full overflow-y-auto bg-gradient-to-b from-white to-slate-50/60 dark:from-slate-800 dark:to-slate-900/40">
      {/* 顶部装饰 */}
      <div className="relative px-5 pt-7 pb-4 overflow-hidden">
        <div className="absolute -top-20 -right-20 w-48 h-48 rounded-full bg-gradient-to-br from-emerald-300/40 via-teal-300/40 to-blue-300/40 blur-3xl pointer-events-none" />
        <div className="absolute -bottom-20 -left-20 w-48 h-48 rounded-full bg-gradient-to-br from-amber-300/40 via-orange-300/40 to-rose-300/40 blur-3xl pointer-events-none" />

        <div className="relative flex flex-col items-center text-center">
          <div className="relative">
            <div className="absolute inset-0 w-20 h-20 rounded-2xl bg-gradient-to-br from-emerald-400 to-teal-500 blur-xl opacity-50 animate-pulse" />
            <div className="relative w-20 h-20 rounded-2xl bg-gradient-to-br from-emerald-500 via-teal-500 to-cyan-600 flex items-center justify-center text-white shadow-xl shadow-emerald-500/30">
              <CheckCircle2 className="w-10 h-10" strokeWidth={2} />
            </div>
          </div>
          <h2 className="text-xl font-extrabold mt-5 bg-gradient-to-r from-slate-900 via-emerald-700 to-slate-900 dark:from-white dark:via-emerald-100 dark:to-white bg-clip-text text-transparent">
            {t('chat.widget.rating.title')}
          </h2>
          <p className="text-xs text-slate-500 dark:text-slate-400 mt-2 leading-relaxed max-w-xs">
            {t('chat.widget.rating.subtitle')}
          </p>
          {countdownRemainMs !== null && countdownRemainMs > 0 && (
            <div className="inline-flex items-center gap-1.5 mt-3 px-3 py-1.5 rounded-full text-[11px] font-bold bg-gradient-to-r from-violet-500/10 to-fuchsia-500/10 dark:from-violet-500/20 dark:to-fuchsia-500/20 text-violet-700 dark:text-violet-300 border border-violet-200/60 dark:border-violet-800/30 shadow-sm">
              <Clock className="w-3.5 h-3.5" />
              <span>{t('chat.widget.rating.countdownBefore')}</span>
              <span className="tabular-nums font-mono">{formatCountdown(countdownRemainMs)}</span>
              <span>{t('chat.widget.rating.countdownAfter')}</span>
            </div>
          )}
        </div>
      </div>

      <div className="px-5 pb-6 space-y-5">
        {/* 星级评分 */}
        <div className="relative bg-white dark:bg-slate-800/60 rounded-2xl border border-slate-200/70 dark:border-slate-700/60 p-5 shadow-sm">
          <div className="flex flex-col items-center">
            <div className="flex gap-2 sm:gap-3 mb-3">
              {[1, 2, 3, 4, 5].map((star) => {
                const filled = displayRating >= star;
                return (
                  <button
                    key={star}
                    type="button"
                    onClick={() => {
                      setRating(star);
                      setError('');
                    }}
                    onMouseEnter={() => setHoverRating(star)}
                    onMouseLeave={() => setHoverRating(0)}
                    disabled={submitting}
                    className="group relative transition-transform hover:scale-115 active:scale-95 disabled:opacity-60 disabled:cursor-not-allowed"
                    aria-label={t('chat.widget.rating.starAria', { star, label: t(ratingLabelKeys[star]) })}
                    title={t('chat.widget.rating.starTitle', { star, label: t(ratingLabelKeys[star]) })}
                  >
                    <Star
                      className={`w-11 h-11 sm:w-12 sm:h-12 transition-all duration-300 ${
                        filled
                          ? 'text-amber-400 drop-shadow-[0_4px_10px_rgba(251,191,36,0.45)]'
                          : 'text-slate-200 dark:text-slate-600'
                      } group-hover:rotate-[8deg]`}
                      fill={filled ? 'currentColor' : 'none'}
                      strokeWidth={filled ? 0 : 1.8}
                    />
                    {filled && (
                      <Star
                        className="absolute inset-0 w-11 h-11 sm:w-12 sm:h-12 text-amber-300 animate-pulse opacity-40 pointer-events-none"
                        fill="currentColor"
                        strokeWidth={0}
                      />
                    )}
                  </button>
                );
              })}
            </div>

            {/* 星级标签 + 描述徽章 */}
            {rating > 0 && (
              <div className="flex flex-col items-center gap-2 animate-in fade-in slide-in-from-bottom-2 duration-300">
                <div className={`px-4 py-1.5 rounded-full text-xs font-bold shadow-sm inline-flex items-center gap-1.5 ${
                  rating >= 5 ? 'bg-gradient-to-r from-amber-400 to-orange-500 text-white shadow-amber-500/30' :
                  rating === 4 ? 'bg-gradient-to-r from-emerald-400 to-teal-500 text-white shadow-emerald-500/30' :
                  rating === 3 ? 'bg-gradient-to-r from-sky-400 to-blue-500 text-white shadow-sky-500/30' :
                  rating === 2 ? 'bg-gradient-to-r from-orange-400 to-amber-500 text-white shadow-orange-500/30' :
                                 'bg-gradient-to-r from-rose-400 to-red-500 text-white shadow-rose-500/30'
                }`}>
                  {rating >= 5 && <Sparkles className="w-3 h-3" />}
                  {rating === 4 && <ThumbsUp className="w-3 h-3" />}
                  {rating === 3 && <Meh className="w-3 h-3" />}
                  {rating === 2 && <Frown className="w-3 h-3" />}
                  {rating === 1 && <AlertTriangle className="w-3 h-3" />}
                  {t(ratingLabelKeys[rating])}
                </div>
              </div>
            )}

            {!rating && !hoverRating && (
              <p className="text-xs text-slate-400 dark:text-slate-500 font-medium">
                {t('chat.widget.rating.hint')}
              </p>
            )}
          </div>
        </div>

        {/* 评价内容 */}
        <div className="group">
          <label className="block text-xs font-bold text-slate-700 dark:text-slate-200 mb-1.5 flex items-center justify-between">
            <span>{t('chat.widget.rating.comment')} <span className="text-slate-400 text-[10px] font-normal">{t('chat.widget.form.optional')}</span></span>
            {comment.length > 0 && (
              <span className={`text-[10px] font-semibold px-1.5 py-0.5 rounded-md ${
                comment.length > 400 ? 'bg-red-50 text-red-500 dark:bg-red-900/20' : 'bg-slate-100 text-slate-500 dark:bg-slate-700/50 dark:text-slate-400'
              }`}>
                {comment.length}/500
              </span>
            )}
          </label>
          <textarea
            value={comment}
            onChange={(e) => setComment(e.target.value)}
            maxLength={500}
            rows={3}
            disabled={submitting}
            placeholder={t('chat.widget.rating.commentPlaceholder')}
            className="w-full px-4 py-2.5 text-sm border-2 border-slate-200 dark:border-slate-700 rounded-2xl bg-white dark:bg-slate-800 text-slate-900 dark:text-slate-100 placeholder:text-slate-400 resize-none focus:outline-none focus:ring-4 focus:ring-amber-500/20 focus:border-amber-500 transition-all shadow-sm disabled:opacity-60 leading-relaxed"
          />
        </div>

        {error && (
          <div className="flex items-center gap-2 px-3.5 py-2.5 rounded-xl bg-red-50 dark:bg-red-900/20 border border-red-200/70 dark:border-red-800/50">
            <AlertCircle className="w-4 h-4 text-red-500 flex-shrink-0" />
            <span className="text-xs font-medium text-red-600 dark:text-red-400">{error}</span>
          </div>
        )}

        <div className="flex gap-3 pt-1">
          <button
            onClick={onSkip}
            disabled={submitting}
            className="flex-1 py-3 text-sm font-bold text-slate-600 dark:text-slate-300 bg-slate-100 dark:bg-slate-700/70 hover:bg-slate-200 dark:hover:bg-slate-600 rounded-2xl transition-all disabled:opacity-60 disabled:cursor-not-allowed shadow-sm hover:shadow active:scale-[0.98] border border-slate-200/70 dark:border-slate-600/60"
          >
            {t('chat.widget.rating.skip')}
          </button>
          <button
            onClick={handleSubmit}
            disabled={submitting || rating === 0}
            className="flex-1 py-3 bg-gradient-to-r from-blue-500 via-indigo-500 to-purple-600 hover:from-blue-600 hover:via-indigo-600 hover:to-purple-700 text-white text-sm font-bold rounded-2xl shadow-lg shadow-indigo-500/30 hover:shadow-xl hover:shadow-indigo-500/40 hover:-translate-y-0.5 transition-all disabled:from-slate-300 disabled:via-slate-300 disabled:to-slate-400 disabled:shadow-none disabled:cursor-not-allowed disabled:translate-y-0 flex items-center justify-center gap-2 active:scale-[0.98] relative overflow-hidden"
          >
            {submitting ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                {t('chat.widget.rating.submitting')}
              </>
            ) : (
              <>
                <Star className="w-4 h-4" fill="currentColor" />
                {t('chat.widget.rating.submit')}
              </>
            )}
          </button>
        </div>
      </div>
    </div>
  );
}

// =====================================================
// 主组件:客户聊天 Widget
// =====================================================
interface CustomerChatWidgetProps {
  /** 是否在嵌入式 iframe 模式下运行(影响关闭行为) */
  embedded?: boolean;
}

export default function CustomerChatWidget({ embedded = false }: CustomerChatWidgetProps) {
  const { t } = useI18n();
  const [stage, setStage] = useState<WidgetStage>('initiate');
  const [session, setSession] = useState<ChatSessionDto | null>(null);
  const [messages, setMessages] = useState<ChatMessageDto[]>([]);
  const [initiating, setInitiating] = useState(false);
  const [messagesLoading, setMessagesLoading] = useState(false);
  const [sending, setSending] = useState(false);
  const [closing, setClosing] = useState(false);
  const [ratingSubmitting, setRatingSubmitting] = useState(false);
  const [error, setError] = useState<string>('');
  const [collapsed, setCollapsed] = useState(false);
  /** completed 阶段进入原因：'rated' = 已提交评分；'timeout' = 评分超时自动结束；null = 未进入 completed */
  const [completedReason, setCompletedReason] = useState<'rated' | 'timeout' | null>(null);

  // 输入区状态
  const [input, setInput] = useState('');
  const [showEmoji, setShowEmoji] = useState(false);

  // 客服输入指示器(由 SignalR ChatTyping 事件触发)
  const [agentTyping, setAgentTyping] = useState(false);
  const typingTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const isMountedRef = useRef(true);
  const messagesEndRef = useScrollToBottom([messages.length]);

  // sessionToken 派生:仅当处于 chatting 阶段且有 session 时连接 SignalR
  const sessionToken = stage === 'chatting' && session ? session.sessionToken : null;
  const { connectionState } = useCustomerSignalR(sessionToken);

  useEffect(() => {
    isMountedRef.current = true;
    return () => {
      isMountedRef.current = false;
      if (typingTimeoutRef.current) {
        clearTimeout(typingTimeoutRef.current);
      }
    };
  }, []);

  // ============ SignalR 事件处理 ============

  /** 收到新消息(客服回复或自己发送的消息回显) */
  useCustomerRealtimeChat<unknown>((data) => {
    if (!session) return;
    // 仅处理当前会话的消息
    if (data && typeof data === 'object' && 'sessionId' in data) {
      const payload = data as { sessionId: number; message?: ChatMessageDto };
      if (payload.sessionId === session.id) {
        // 优先使用推送中的完整消息对象,否则刷新整个列表
        if (payload.message && typeof payload.message === 'object') {
          setMessages((prev) => {
            // 去重(防止 SignalR 与 HTTP 响应重复)
            if (prev.some((m) => m.id === payload.message!.id)) return prev;
            return [...prev, payload.message!];
          });
        } else {
          // 回退:整列表刷新
          void refreshMessages();
        }
      }
    } else {
      void refreshMessages();
    }
  });

  /** 消息被撤回 */
  useCustomerChatMessageRecalled<unknown>((data) => {
    if (!data || typeof data !== 'object') return;
    const payload = data as { sessionId?: number; messageId?: number; message?: ChatMessageDto };
    if (payload.sessionId !== session?.id) return;
    if (payload.message && typeof payload.message === 'object') {
      // 用推送的最新消息对象替换本地
      setMessages((prev) =>
        prev.map((m) => (m.id === payload.message!.id ? payload.message! : m)),
      );
    } else if (payload.messageId) {
      setMessages((prev) =>
        prev.map((m) =>
          m.id === payload.messageId
            ? { ...m, isRecalled: true, recalledAt: new Date().toISOString() }
            : m,
        ),
      );
    }
  });

  /** 消息被编辑 */
  useCustomerChatMessageEdited<unknown>((data) => {
    if (!data || typeof data !== 'object') return;
    const payload = data as { sessionId?: number; messageId?: number; message?: ChatMessageDto };
    if (payload.sessionId !== session?.id) return;
    if (payload.message && typeof payload.message === 'object') {
      setMessages((prev) =>
        prev.map((m) => (m.id === payload.message!.id ? payload.message! : m)),
      );
    } else if (payload.messageId) {
      void refreshMessages();
    }
  });

  /** 已读回执(客服读了我发的消息) */
  useCustomerChatMessageRead<unknown>((data) => {
    if (!data || typeof data !== 'object') return;
    const payload = data as { sessionId?: number; readerType?: string };
    if (payload.sessionId !== session?.id) return;
    // 仅当 readerType 是 agent 时更新客户消息的已读状态
    if (payload.readerType === 'agent') {
      setMessages((prev) =>
        prev.map((m) =>
          m.senderType === 'customer' && !m.isRead
            ? { ...m, isRead: true, readAt: new Date().toISOString() }
            : m,
        ),
      );
    }
  });

  /** 客服正在输入 */
  useCustomerChatTyping<unknown>((data) => {
    if (!data || typeof data !== 'object') return;
    const payload = data as { sessionId?: number; senderType?: string; senderName?: string };
    if (payload.sessionId !== session?.id) return;
    if (payload.senderType === 'agent') {
      setAgentTyping(true);
      if (typingTimeoutRef.current) clearTimeout(typingTimeoutRef.current);
      // 3 秒无新输入事件则隐藏
      typingTimeoutRef.current = setTimeout(() => {
        if (isMountedRef.current) setAgentTyping(false);
      }, 3000);
    }
  });

  // ============ 业务操作 ============

  const refreshMessages = useCallback(async () => {
    if (!session) return;
    try {
      setMessagesLoading(true);
      const data = await chatApi.getCustomerMessages(session.sessionToken);
      if (!isMountedRef.current) return;
      setMessages(data);
    } catch (e: unknown) {
      console.error('[CustomerChat] refreshMessages failed:', e);
    } finally {
      if (isMountedRef.current) setMessagesLoading(false);
    }
  }, [session]);

  const handleInitiate = useCallback(async (req: InitiateChatRequest) => {
    setError('');
    try {
      setInitiating(true);
      const result = await chatApi.initiateChat(req);
      if (!isMountedRef.current) return;
      setSession(result.session);
      setMessages(result.welcomeMessage ? [result.welcomeMessage] : []);
      setStage('chatting');
      // 持久化客户信息到 sessionStorage 以便当前标签页会话内回访（关闭标签页后清除）
      savePersistedCustomerInfo({
        customerName: req.customerName,
        customerEmail: req.customerEmail,
        customerPhone: req.customerPhone,
      });
    } catch (e: unknown) {
      if (!isMountedRef.current) return;
      const msg = e instanceof Error ? e.message : t('chat.widget.error.initiateFailed');
      setError(msg);
    } finally {
      if (isMountedRef.current) setInitiating(false);
    }
  }, [t]);

  const handleSend = useCallback(async () => {
    if (!session || !input.trim() || sending) return;
    const content = input.trim();
    setInput('');
    setShowEmoji(false);
    try {
      setSending(true);
      // 乐观插入:本地立即显示,失败时回退
      const optimistic: ChatMessageDto = {
        id: -Date.now(),
        sessionId: session.id,
        senderType: 'customer',
        senderId: null,
        senderName: session.customerName,
        messageType: 'text',
        content,
        attachmentUrl: null,
        attachmentName: null,
        isRead: false,
        readAt: null,
        isRecalled: false,
        recalledAt: null,
        editedAt: null,
        createdAt: new Date().toISOString(),
      };
      setMessages((prev) => [...prev, optimistic]);

      await chatApi.sendCustomerMessage(session.sessionToken, {
        content,
        messageType: 'text',
      });
      // 不主动刷新:由 SignalR 回显保证一致性(去重靠 id)
      // 若 SignalR 未连接,降级为整列表刷新
      if (connectionState !== 'connected') {
        await refreshMessages();
      }
    } catch (e: unknown) {
      if (!isMountedRef.current) return;
      // 移除乐观消息
      setMessages((prev) => prev.filter((m) => m.id > 0));
      const msg = e instanceof Error ? e.message : t('chat.widget.error.sendFailed');
      setError(msg);
      // 恢复输入内容,便于重试
      setInput(content);
    } finally {
      if (isMountedRef.current) setSending(false);
    }
  }, [session, input, sending, connectionState, refreshMessages, t]);

  const handleClose = useCallback(async () => {
    if (!session) return;
    setError('');
    try {
      setClosing(true);
      await chatApi.closeSessionByToken(session.sessionToken);
      if (!isMountedRef.current) return;
      setStage('rating');
    } catch (e: unknown) {
      if (!isMountedRef.current) return;
      const msg = e instanceof Error ? e.message : t('chat.widget.error.closeFailed');
      setError(msg);
    } finally {
      if (isMountedRef.current) setClosing(false);
    }
  }, [session, t]);

  const handleRate = useCallback(
    async (req: RateChatRequest) => {
      if (!session) return;
      setError('');
      try {
        setRatingSubmitting(true);
        await chatApi.rateChat(session.sessionToken, req);
        if (!isMountedRef.current) return;
        setCompletedReason('rated');
        setStage('completed');
      } catch (e: unknown) {
        if (!isMountedRef.current) return;
        const msg = e instanceof Error ? e.message : t('chat.widget.error.rateFailed');
        setError(msg);
      } finally {
        if (isMountedRef.current) setRatingSubmitting(false);
      }
    },
    [session, t],
  );

  const handleSkipRating = useCallback(() => {
    setCompletedReason('timeout');
    setStage('completed');
  }, []);

  const handleReset = useCallback(() => {
    setSession(null);
    setMessages([]);
    setInput('');
    setError('');
    setShowEmoji(false);
    setAgentTyping(false);
    setCompletedReason(null);
    setStage('initiate');
  }, []);

  const handleKeyDown = useCallback(
    (e: KeyboardEvent<HTMLTextAreaElement>) => {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        void handleSend();
      }
    },
    [handleSend],
  );

  const handleEmojiPick = useCallback((emoji: string) => {
    setInput((prev) => prev + emoji);
  }, []);

  const connectionInfo = useMemo<{ icon: React.ReactNode; textKey: TranslationKey; color: string } | null>(() => {
    if (stage !== 'chatting') return null;
    if (connectionState === 'connected') return { icon: <Wifi className="w-3 h-3" />, textKey: 'chat.widget.connection.connected', color: 'text-emerald-500' };
    if (connectionState === 'connecting') return { icon: <Loader2 className="w-3 h-3 animate-spin" />, textKey: 'chat.widget.connection.connecting', color: 'text-amber-500' };
    if (connectionState === 'reconnecting') return { icon: <Loader2 className="w-3 h-3 animate-spin" />, textKey: 'chat.widget.connection.reconnecting', color: 'text-amber-500' };
    return { icon: <WifiOff className="w-3 h-3" />, textKey: 'chat.widget.connection.disconnected', color: 'text-red-500' };
  }, [stage, connectionState]);

  // ====== 客户侧会话轮询：检测客服主动关闭(active → awaiting_rating)等状态变更 ======
  const refreshCustomerSession = useCallback(async () => {
    if (!session) return null;
    try {
      const fresh = await chatApi.getCustomerSession(session.sessionToken);
      if (!isMountedRef.current) return null;
      // 仅在对象层面有差异时更新,避免无意义的重渲染
      setSession((prev) => {
        if (!prev) return fresh;
        if (
          prev.status === fresh.status &&
          prev.ratingRequestedAt === fresh.ratingRequestedAt &&
          prev.ratingTimeoutAt === fresh.ratingTimeoutAt &&
          prev.ratingSubmittedAt === fresh.ratingSubmittedAt &&
          prev.updatedAt === fresh.updatedAt
        ) {
          return prev;
        }
        return fresh;
      });
      return fresh;
    } catch (e: unknown) {
      console.error('[CustomerChat] refreshCustomerSession failed:', e);
      return null;
    }
  }, [session]);

  // 1) chatting / rating 阶段,每 5 秒拉一次最新会话状态
  useEffect(() => {
    if (stage !== 'chatting' && stage !== 'rating') return;
    if (!session) return;
    // 立刻拉一次(例如从 sessionStorage 恢复后,拿到最新 status/ratingTimeoutAt)
    void refreshCustomerSession();
    const id = window.setInterval(() => {
      void refreshCustomerSession();
    }, 5000);
    return () => window.clearInterval(id);
  }, [stage, session, refreshCustomerSession]);

  // 2) 根据 session.status 自动切换 stage(例如客服点关闭 → awaiting_rating 自动弹评分)
  useEffect(() => {
    if (!session) return;
    // 使用临时变量,避免 TS 基于之前 return 的类型收窄导致后续比较报错
    const s: WidgetStage = stage;
    if (s === 'initiate' || s === 'completed') return;

    if (session.status === 'awaiting_rating') {
      // 未进入评分则进入;已在评分则保持(不打断客户正在打分)
      if (s !== 'rating') setStage('rating');
      return;
    }
    // 后端已真正关闭(如后台 Job 超时关闭 / 客服手动 ReallyClose / 已转工单)
    if (session.status === 'closed' || session.status === 'transferred') {
      if (session.ratingSubmittedAt) {
        // 已经评分过 → 直接进入完成页
        setStage('completed');
      } else if (s !== 'rating') {
        // 尚未评分 → 仍提供补打评分入口(客服主动关客户未评分期间 Job 已超时也允许再评)
        setStage('rating');
      }
    }
  }, [session, stage]);

  // 3) rating 阶段:显示倒计时;倒计时归零 → 后台 Job 会关,这里自动切 completed 作为兜底
  const [countdownRemainMs, setCountdownRemainMs] = useState<number | null>(null);
  useEffect(() => {
    if (stage !== 'rating' || !session?.ratingTimeoutAt) {
      setCountdownRemainMs(null);
      return;
    }
    const target = new Date(session.ratingTimeoutAt).getTime();
    const compute = () => {
      const remain = Math.max(0, target - Date.now());
      setCountdownRemainMs(remain);
      if (remain <= 0) {
        // 超时:客户未评分,进入"已完成"状态(后台 Job 会真正关闭会话)
        setCompletedReason('timeout');
        setStage('completed');
      }
    };
    compute();
    const t = window.setInterval(compute, 1000);
    return () => window.clearInterval(t);
  }, [stage, session?.ratingTimeoutAt]);

  // 嵌入式模式下若 collapsed=true,只显示触发按钮(带浮动徽章+波纹动画)
  if (embedded && collapsed) {
    return (
      <div className="fixed bottom-5 right-5 z-50">
        {/* 外部呼吸光晕 */}
        <div className="absolute inset-0 -m-2 rounded-full bg-gradient-to-br from-blue-400 to-indigo-500 blur-2xl opacity-40 animate-pulse pointer-events-none" />
        <button
          onClick={() => setCollapsed(false)}
          className="relative w-15 h-15 rounded-[22px] bg-gradient-to-br from-blue-500 via-indigo-500 to-purple-600 hover:from-blue-600 hover:via-indigo-600 hover:to-purple-700 text-white shadow-[0_12px_30px_-6px_rgba(79,70,229,0.55)] hover:shadow-[0_16px_36px_-6px_rgba(79,70,229,0.65)] hover:-translate-y-1 transition-all duration-300 flex items-center justify-center active:scale-95 group overflow-hidden"
          style={{ width: '60px', height: '60px' }}
          aria-label={t('chat.widget.a11y.openChat')}
          title={t('chat.widget.a11y.startChat')}
        >
          {/* 内部闪光 */}
          <span className="absolute inset-0 -translate-x-full bg-gradient-to-r from-transparent via-white/30 to-transparent group-hover:translate-x-full transition-transform duration-1000 pointer-events-none" />
          <MessageCircle className="w-7 h-7 relative z-10" strokeWidth={2.2} />
          {/* 在线状态点 */}
          <span className="absolute top-2 right-2 flex h-3 w-3 z-10">
            <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-80" />
            <span className="relative inline-flex rounded-full h-3 w-3 bg-emerald-500 border-2 border-white" />
          </span>
        </button>
      </div>
    );
  }

  return (
    <div
      className={`${
        embedded
          ? 'fixed bottom-5 right-5 z-50 w-[370px] h-[590px] max-w-[calc(100vw-2.5rem)] max-h-[calc(100vh-2.5rem)] shadow-[0_20px_60px_-15px_rgba(15,23,42,0.45)] rounded-3xl overflow-hidden ring-1 ring-white/20 dark:ring-black/20'
          : 'w-full h-screen max-w-md mx-auto'
      } flex flex-col bg-gradient-to-b from-white to-slate-50/40 dark:from-slate-900 dark:to-slate-950/60 border border-slate-200/60 dark:border-slate-700/60 backdrop-blur`}
    >
      {/* 头部(毛玻璃质感 + 渐变 + 分隔) */}
      <header className="flex items-center justify-between px-5 py-3.5 bg-gradient-to-r from-blue-600 via-indigo-600 to-purple-600 text-white shadow-md shadow-indigo-500/20 relative overflow-hidden">
        {/* 装饰光斑 */}
        <div className="absolute -top-10 -left-10 w-32 h-32 rounded-full bg-white/10 blur-2xl pointer-events-none" />
        <div className="absolute -bottom-10 -right-10 w-32 h-32 rounded-full bg-purple-300/20 blur-2xl pointer-events-none" />

        <div className="flex items-center gap-3 min-w-0 relative z-10">
          <div className="relative flex-shrink-0">
            <div className="w-10 h-10 rounded-2xl bg-white/20 flex items-center justify-center backdrop-blur-sm shadow-inner border border-white/30">
              <Headphones className="w-5 h-5" strokeWidth={2.2} />
            </div>
            {/* 在线状态 */}
            <span className="absolute -bottom-0.5 -right-0.5 flex h-3.5 w-3.5">
              <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75" />
              <span className="relative inline-flex rounded-full h-3.5 w-3.5 bg-emerald-400 border-2 border-indigo-600" />
            </span>
          </div>
          <div className="min-w-0">
            <h1 className="text-sm font-bold truncate tracking-wide">{t('chat.widget.header.title')}</h1>
            {connectionInfo && (
              <div className={`flex items-center gap-1.5 text-[10.5px] ${connectionInfo.color} mt-0.5`}>
                {connectionInfo.icon}
                <span className="font-semibold">{t(connectionInfo.textKey)}</span>
              </div>
            )}
          </div>
        </div>
        <div className="flex items-center gap-1 relative z-10">
          {embedded && (
            <button
              onClick={() => setCollapsed(true)}
              className="p-2 hover:bg-white/20 rounded-xl transition-all hover:scale-105 active:scale-95"
              aria-label={t('chat.widget.a11y.minimize')}
              title={t('chat.widget.a11y.minimizeToBubble')}
            >
              <Minimize2 className="w-4.5 h-4.5" strokeWidth={2} />
            </button>
          )}
          {stage === 'chatting' && (
            <button
              onClick={handleClose}
              disabled={closing}
              className="p-2 hover:bg-white/20 rounded-xl transition-all hover:scale-105 active:scale-95 disabled:opacity-60 disabled:cursor-not-allowed disabled:hover:scale-100"
              aria-label={t('chat.widget.a11y.endChat')}
              title={t('chat.widget.a11y.endChatHint')}
            >
              {closing ? <Loader2 className="w-4.5 h-4.5 animate-spin" strokeWidth={2} /> : <X className="w-4.5 h-4.5" strokeWidth={2.2} />}
            </button>
          )}
          {(stage === 'completed' || stage === 'rating') && (
            <button
              onClick={handleReset}
              className="p-2 hover:bg-white/20 rounded-xl transition-all hover:scale-105 active:scale-95"
              aria-label={t('chat.widget.a11y.newChat')}
              title={t('chat.widget.a11y.newChatHint')}
            >
              <Maximize2 className="w-4.5 h-4.5" strokeWidth={2} />
            </button>
          )}
        </div>
      </header>

      {/* 主体 */}
      <div className="flex-1 flex flex-col min-h-0">
        {/* 全局错误 Toast */}
        {error && (
          <div className="px-4 py-2.5 bg-gradient-to-r from-red-50 to-rose-50 dark:from-red-900/30 dark:to-rose-900/20 border-b border-red-200/70 dark:border-red-800/50 text-xs text-red-600 dark:text-red-300 flex items-center gap-2.5">
            <div className="w-7 h-7 rounded-xl bg-red-100 dark:bg-red-800/40 flex items-center justify-center flex-shrink-0">
              <AlertCircle className="w-3.5 h-3.5 text-red-500 dark:text-red-300" />
            </div>
            <span className="flex-1 break-words font-medium">{error}</span>
            <button
              onClick={() => setError('')}
              className="p-1 hover:bg-red-100 dark:hover:bg-red-800/50 rounded-lg transition-colors"
              aria-label={t('chat.widget.a11y.dismissError')}
            >
              <X className="w-3.5 h-3.5" />
            </button>
          </div>
        )}

        {stage === 'initiate' && (
          <InitiateForm onInitiate={handleInitiate} initiating={initiating} />
        )}

        {stage === 'chatting' && session && (
          <>
            {/* 消息列表（含 content-visibility 长列表优化样式） */}
            {/*
              客户聊天窗口消息列表优化：
              - 客户侧消息量通常不会特别大，但为了与坐席端保持一致性，
                同时为"历史长会话回访"场景做准备，同样启用 content-visibility。
              - 客户侧气泡平均高度略小于坐席端（无撤回/编辑工具栏），占位估 90px。
              - contain 做降级兼容（Firefox / 旧 Safari）。
            */}
            <style>{`
              .customer-chat-msg-wrap {
                content-visibility: auto;
                contain-intrinsic-size: auto 90px;
              }
              @supports not (content-visibility: auto) {
                .customer-chat-msg-wrap {
                  content-visibility: visible;
                  contain: layout style;
                }
              }
            `}</style>
            <div className="flex-1 overflow-y-auto px-4 py-4 bg-gradient-to-b from-slate-50 to-slate-100/40 dark:from-slate-900/60 dark:to-slate-950/40 scroll-smooth">
              {messagesLoading && messages.length === 0 ? (
                <div className="flex flex-col items-center justify-center h-full text-slate-400 gap-3">
                  <div className="relative">
                    <div className="absolute inset-0 rounded-2xl bg-indigo-400/30 blur-lg animate-pulse" />
                    <Loader2 className="w-8 h-8 animate-spin text-indigo-500 relative" strokeWidth={2} />
                  </div>
                  <div className="flex flex-col items-center gap-1">
                    <p className="text-xs font-semibold text-slate-500 dark:text-slate-400">{t('chat.widget.loading.messages')}</p>
                    <p className="text-[10px] text-slate-400 dark:text-slate-500">{t('chat.widget.loading.pleaseWait')}</p>
                  </div>
                </div>
              ) : messages.length === 0 ? (
                <div className="flex flex-col items-center justify-center h-full text-slate-400 gap-4 py-10">
                  <div className="relative">
                    <div className="absolute inset-0 -m-2 rounded-3xl bg-indigo-200/40 dark:bg-indigo-800/30 blur-2xl" />
                    <div className="relative w-20 h-20 rounded-3xl bg-gradient-to-br from-white to-slate-50 dark:from-slate-800 dark:to-slate-900 flex items-center justify-center shadow-xl shadow-indigo-100/50 dark:shadow-black/30 border border-slate-100 dark:border-slate-700">
                      <MessageCircle className="w-10 h-10 text-indigo-400 dark:text-indigo-300" strokeWidth={1.6} />
                    </div>
                  </div>
                  <div className="flex flex-col items-center gap-1.5">
                    <p className="text-sm font-bold text-slate-600 dark:text-slate-300">{t('chat.widget.empty.title')}</p>
                    <p className="text-xs text-slate-500 dark:text-slate-400">{t('chat.widget.empty.subtitle')}</p>
                  </div>
                </div>
              ) : (
                <>
                  {messages.map((msg) => (
                    <div key={msg.id} className="customer-chat-msg-wrap">
                      <CustomerMessageBubble message={msg} />
                    </div>
                  ))}
                  <TypingIndicator show={agentTyping} senderName={session.userName || t('chat.widget.message.agent')} />
                  <div ref={messagesEndRef} />
                </>
              )}
            </div>

            {/* 输入区(卡片式 + 工具条 + 渐变发送) */}
            <div className="relative border-t border-slate-200/70 dark:border-slate-700/60 bg-white dark:bg-slate-800 shadow-[0_-6px_24px_-12px_rgba(15,23,42,0.08)]">
              {showEmoji && (
                <div className="absolute bottom-full left-3 right-3 mb-2.5 p-2.5 bg-white dark:bg-slate-800 rounded-2xl shadow-[0_12px_32px_-10px_rgba(15,23,42,0.25)] border border-slate-200 dark:border-slate-700 grid grid-cols-8 gap-1.5 backdrop-blur-sm animate-in fade-in slide-in-from-bottom-2 duration-200">
                  {COMMON_EMOJIS.map((emoji) => (
                    <button
                      key={emoji}
                      onClick={() => handleEmojiPick(emoji)}
                      className="w-9 h-9 flex items-center justify-center text-lg hover:bg-gradient-to-br hover:from-slate-100 hover:to-slate-200 dark:hover:from-slate-700 dark:hover:to-slate-600 rounded-xl transition-all hover:scale-110 active:scale-95"
                      type="button"
                    >
                      {emoji}
                    </button>
                  ))}
                </div>
              )}
              <div className="flex items-end gap-2.5 p-3">
                <button
                  onClick={() => setShowEmoji((v) => !v)}
                  disabled={sending}
                  className={`p-2.5 rounded-xl transition-all hover:scale-105 active:scale-95 disabled:opacity-40 disabled:cursor-not-allowed disabled:hover:scale-100 ${
                    showEmoji
                      ? 'text-amber-500 bg-gradient-to-br from-amber-50 to-orange-50 dark:from-amber-900/40 dark:to-orange-900/30 shadow-sm ring-1 ring-amber-200 dark:ring-amber-800/50'
                      : 'text-slate-500 dark:text-slate-400 hover:bg-slate-100 dark:hover:bg-slate-700/60'
                  }`}
                  aria-label={t('chat.widget.a11y.emoji')}
                  title={t('chat.widget.a11y.insertEmoji')}
                >
                  <Smile className="w-5 h-5" strokeWidth={1.9} />
                </button>
                <div className="flex-1 relative group">
                  <div className="absolute inset-0 rounded-2xl bg-gradient-to-br from-blue-500/5 to-indigo-500/5 dark:from-blue-900/20 dark:to-indigo-900/20 opacity-0 group-focus-within:opacity-100 transition-opacity pointer-events-none" />
                  <textarea
                    value={input}
                    onChange={(e) => setInput(e.target.value)}
                    onKeyDown={handleKeyDown}
                    disabled={sending}
                    rows={1}
                    placeholder={t('chat.widget.composer.placeholder')}
                    className="relative flex-1 w-full px-4 py-2.5 text-sm border-2 border-slate-200 dark:border-slate-700 rounded-2xl bg-slate-50 dark:bg-slate-900/60 text-slate-900 dark:text-slate-100 placeholder:text-slate-400 resize-none focus:outline-none focus:ring-4 focus:ring-indigo-500/15 focus:border-indigo-500 disabled:opacity-60 max-h-28 transition-all shadow-inner"
                    style={{ minHeight: '42px' }}
                  />
                </div>
                <button
                  onClick={handleSend}
                  disabled={!input.trim() || sending}
                  className="relative p-2.5 rounded-2xl transition-all duration-200 disabled:opacity-60 disabled:cursor-not-allowed flex items-center justify-center overflow-hidden group/btn"
                  aria-label={t('chat.widget.a11y.send')}
                  title={input.trim() ? t('chat.widget.a11y.sendMessage') : t('chat.widget.a11y.sendDisabled')}
                >
                  {input.trim() && !sending ? (
                    <>
                      <span className="absolute inset-0 bg-gradient-to-br from-blue-500 via-indigo-500 to-purple-600 transition-all group-hover/btn:from-blue-600 group-hover/btn:via-indigo-600 group-hover/btn:to-purple-700" />
                      <span className="absolute inset-0 bg-gradient-to-br from-blue-500 via-indigo-500 to-purple-600 blur-lg opacity-50 group-hover/btn:opacity-70 transition-opacity" />
                      <span className="absolute inset-0 -translate-x-full bg-gradient-to-r from-transparent via-white/30 to-transparent group-hover/btn:translate-x-full transition-transform duration-1000 pointer-events-none" />
                      <Send className="w-5 h-5 relative text-white group-hover/btn:-translate-y-0.5 group-hover/btn:translate-x-0.5 transition-transform" strokeWidth={2.1} />
                    </>
                  ) : sending ? (
                    <span className="w-full h-full flex items-center justify-center p-2.5 rounded-2xl bg-slate-200 dark:bg-slate-700">
                      <Loader2 className="w-5 h-5 animate-spin text-slate-500 dark:text-slate-300" strokeWidth={2} />
                    </span>
                  ) : (
                    <span className="w-full h-full flex items-center justify-center p-2.5 rounded-2xl bg-slate-200 dark:bg-slate-700 cursor-not-allowed">
                      <Send className="w-5 h-5 text-slate-400 dark:text-slate-500" strokeWidth={2} />
                    </span>
                  )}
                </button>
              </div>
            </div>
          </>
        )}

        {stage === 'rating' && session && (
          <RatingForm
            session={session}
            onSubmit={handleRate}
            submitting={ratingSubmitting}
            onSkip={handleSkipRating}
            countdownRemainMs={countdownRemainMs}
          />
        )}

        {stage === 'completed' && (() => {
          const isRated = completedReason === 'rated';
          // 图标 + 配色：rated = 绿青渐变 (感谢反馈)；timeout = 琥珀橙渐变 (自动结束)
          const iconBg = isRated
            ? 'from-emerald-500 via-teal-500 to-cyan-600'
            : 'from-amber-500 via-orange-500 to-rose-500';
          const iconShadow = isRated ? 'shadow-emerald-500/30' : 'shadow-amber-500/30';
          const iconBlur = isRated ? 'from-emerald-400 to-teal-500' : 'from-amber-400 to-orange-500';
          const titleGrad = isRated
            ? 'from-slate-900 via-emerald-700 to-slate-900 dark:from-white dark:via-emerald-100 dark:to-white'
            : 'from-slate-900 via-amber-700 to-slate-900 dark:from-white dark:via-amber-100 dark:to-white';
          const titleText = isRated ? t('chat.widget.completed.ratedTitle') : t('chat.widget.completed.timeoutTitle');
          const subtitleTop = isRated
            ? t('chat.widget.completed.ratedSubtitle')
            : t('chat.widget.completed.timeoutSubtitle');
          const subtitleBottom = t('chat.widget.completed.retryHint');
          const IconEl = isRated ? CheckCircle2 : Clock;
          const deco1 = isRated
            ? 'from-emerald-300/30 via-teal-300/30 to-blue-300/30'
            : 'from-amber-300/30 via-orange-300/30 to-rose-300/30';
          const deco2 = isRated
            ? 'from-indigo-300/30 via-purple-300/30 to-pink-300/30'
            : 'from-yellow-300/25 via-orange-300/25 to-red-300/25';

          return (
            <div className="flex-1 flex flex-col items-center justify-center text-center px-6 py-8 relative overflow-hidden">
              {/* 装饰背景 */}
              <div className={`absolute -top-20 -right-20 w-52 h-52 rounded-full bg-gradient-to-br ${deco1} blur-3xl pointer-events-none`} />
              <div className={`absolute -bottom-20 -left-20 w-52 h-52 rounded-full bg-gradient-to-br ${deco2} blur-3xl pointer-events-none`} />

              <div className="relative flex flex-col items-center">
                <div className="relative mb-5">
                  <div className={`absolute inset-0 -m-3 rounded-3xl bg-gradient-to-br ${iconBlur} blur-xl opacity-50 animate-pulse`} />
                  <div className={`relative w-20 h-20 rounded-3xl bg-gradient-to-br ${iconBg} flex items-center justify-center text-white shadow-xl ${iconShadow}`}>
                    <IconEl className="w-10 h-10" strokeWidth={2} />
                  </div>
                </div>
                <h2 className={`text-xl font-extrabold bg-gradient-to-r ${titleGrad} bg-clip-text text-transparent`}>
                  {titleText}
                </h2>
                <p className="text-xs text-slate-500 dark:text-slate-400 mt-2 mb-7 max-w-xs leading-relaxed">
                  {subtitleTop}
                  <br />{subtitleBottom}
                </p>
                <button
                  onClick={handleReset}
                  className="px-7 py-3 bg-gradient-to-r from-blue-500 via-indigo-500 to-purple-600 hover:from-blue-600 hover:via-indigo-600 hover:to-purple-700 text-white text-sm font-bold rounded-2xl shadow-lg shadow-indigo-500/30 hover:shadow-xl hover:shadow-indigo-500/40 hover:-translate-y-0.5 transition-all active:scale-[0.98] flex items-center gap-2 relative overflow-hidden"
                >
                  <MessageCircle className="w-4.5 h-4.5" strokeWidth={2} />
                  {t('chat.widget.completed.newChat')}
                  <span className="absolute inset-0 -translate-x-full bg-gradient-to-r from-transparent via-white/20 to-transparent hover:translate-x-full transition-transform duration-1000 pointer-events-none" />
                </button>
              </div>
            </div>
          );
        })()}
      </div>
    </div>
  );
}
