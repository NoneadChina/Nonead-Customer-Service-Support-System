import DOMPurify from 'dompurify';
import type { TranslationKey } from '../locales/zh-Hans';
import { getBoundLocale, tBound, toIntlLocale } from '../i18n/locale';

/** 显式 locale 优先，否则用 `I18nProvider` 绑定的当前语言（见 `i18n/locale.ts`） */
function resolveLocale(locale?: string): string {
  return locale ?? getBoundLocale();
}

/**
 * 净化文件名：将 Windows/Unix 文件系统非法字符替换为下划线（M-FE6）。
 * 统一供附件上传（core.ts upload/uploadBatch）与 ReplyBox 等组件复用，
 * 避免各处文件名净化规则不一致导致的安全/兼容问题。
 * @param name 原始文件名
 * @returns 净化后的文件名
 */
export function sanitizeFileName(name: string): string {
  return name.replace(/[\\/:*?"<>|]/g, '_');
}

/**
 * 校验 URL 是否使用安全协议（http/https/mailto/tel/data:image）。
 * 拒绝 javascript: / vbscript: / data:（非图片）等危险协议，防止 XSS。
 * @param url 待校验的 URL 字符串
 * @returns 安全的 URL 字符串；不安全时返回 '#'
 */
export function safeUrl(url: string | null | undefined): string {
  if (!url) return '#';
  const trimmed = String(url).trim();
  if (!trimmed) return '#';
  // 允许 http/https/mailto/tel
  if (/^(https?:\/\/|mailto:|tel:)/i.test(trimmed)) {
    return trimmed;
  }
  // 允许 data:image/ 开头的 base64 图片（用于头像等客户端压缩的图片）
  if (/^data:image\/(jpeg|jpg|png|gif|webp|bmp|svg\+xml);base64,/i.test(trimmed)) {
    return trimmed;
  }
  // 拒绝任何其他协议（包括 javascript:、data:（非图片）、vbscript:、file: 等）
  if (/^[a-z][a-z0-9+.-]*:/i.test(trimmed)) {
    return '#';
  }
  // 相对路径或裸字符串：当作 http 处理
  return trimmed;
}

/**
 * T6: 聊天附件 URL 安全校验——仅允许 http/https 绝对地址或相对路径，
 * 拒绝 javascript:/data:/vbscript:/file: 等危险协议（与后端 NormalizeAttachmentUrl 对齐，前端纵深防御）。
 * @param url 附件 URL
 * @returns 安全 URL；不安全返回 '#'
 */
export function safeAttachmentUrl(url: string | null | undefined): string {
  if (!url) return '#';
  const trimmed = String(url).trim();
  if (!trimmed) return '#';
  if (/^https?:\/\//i.test(trimmed) || trimmed.startsWith('/') || trimmed.startsWith('./') || trimmed.startsWith('../')) {
    return trimmed;
  }
  return '#';
}

/**
 * KB-008: 简化 DOMPurify 配置——依赖默认安全配置而非显式白名单。
 *
 * 原配置同时使用 ADD_TAGS（白名单）+ FORBID_TAGS（黑名单），存在两个问题：
 *   1. ADD_TAGS 白名单遗漏了 caption/col/colgroup/tfoot/dt/dd/dl/figure/figcaption/abbr/address/cite/q/time 等安全标签
 *   2. FORBID_TAGS 中的 script/style/iframe 等已被 DOMPurify 默认禁止，显式列出纯属冗余
 *
 * DOMPurify 默认配置已足够安全：
 *   - 默认禁止 script, style, iframe, embed, object, form, input 等危险标签
 *   - 默认禁止所有 on* 事件处理器属性
 *   - 默认禁止 javascript:/vbscript: 协议
 *   - 默认允许所有常见 HTML 内容标签（p, div, span, table 系列, 列表, 标题等）
 *
 * 因此移除 ADD_TAGS/FORBID_TAGS/FORBID_ATTR，仅保留必要的 ADD_ATTR（自定义数据属性）
 * 和其他安全增强配置（URL 协议白名单、SANITIZE_NAMED_PROPS 等）。
 */
export function sanitizeHtml(html: string): string {
  if (!html) return '';
  return DOMPurify.sanitize(html, {
    // 仅显式添加自定义数据属性（DOMPurify 默认不允许 data-* 属性）
    ADD_ATTR: ['target', 'rel', 'class', 'title', 'data-lat', 'data-lng', 'data-name'],
    // 显式禁止 style 属性（DOMPurify 默认允许 style 属性，需手动禁止以防止 CSS 注入）
    FORBID_ATTR: ['style'],
    ALLOW_UNKNOWN_PROTOCOLS: false,
    ALLOW_DATA_ATTR: false,
    SANITIZE_NAMED_PROPS: true,
    USE_PROFILES: { html: true },
    ALLOWED_URI_REGEXP: /^(?:(?:(?:f|ht)tps?|mailto|tel|callto|cid|xmpp):|[^a-z]|[a-z+.-]+(?:[^a-z+.\-:]|$))/i,
    WHOLE_DOCUMENT: false,
    RETURN_DOM: false,
    RETURN_DOM_FRAGMENT: false,
    FORCE_BODY: false,
  });
}

/**
 * 中文表情 shortcode → Unicode emoji 映射表。
 * 覆盖飞书/钉钉/微信等渠道在纯文本消息中使用的 [表情名] 格式。
 * 飞书表情参考：https://open.feishu.cn/document/uAjLw4CM/ukTMukTMukTM/im-v1/message/emoji
 *
 * ⚠ 这张表是**入站消息解析用的关键词**（左列是渠道文本里出现的字面量，右列是替换结果），
 * **不是用户可见文案**：它不随界面语言变化，翻译它会让解析失效。故整块用 `i18n-exempt-block`
 * 标记豁免（见 `scripts/check-hardcoded-chinese.mjs` 的标记规则），**不要**为了消掉门禁命中
 * 把左列的键改成转义或变量。（原先是靠硬编码中文基线里的 110 处"默默"豁免，现改为显式标记。）
 */
// i18n-exempt-block-start: 入站消息解析关键词表（渠道文本 → emoji），非用户可见文案
const EMOJI_SHORTCODE_MAP: Record<string, string> = {
  // 表情类
  '[微笑]': '😊', '[撇嘴]': '😒', '[色]': '😍', '[发呆]': '😳', '[得意]': '😎',
  '[流泪]': '😢', '[害羞]': '☺️', '[闭嘴]': '😐', '[睡]': '😴', '[大哭]': '😭',
  '[尴尬]': '😅', '[发怒]': '😠', '[调皮]': '😜', '[呲牙]': '😁', '[惊讶]': '😮',
  '[难过]': '😞', '[酷]': '😎', '[冷汗]': '😓', '[抓狂]': '😫', '[吐]': '🤮',
  '[偷笑]': '🤭', '[可爱]': '🥰', '[白眼]': '🙄', '[傲慢]': '😏', '[饥饿]': '🤤',
  '[困]': '🥱', '[惊恐]': '😨', '[流汗]': '😅', '[憨笑]': '😃', '[大兵]': '🫡',
  '[奋斗]': '💪', '[咒骂]': '🤬', '[疑问]': '❓', '[嘘]': '🤫', '[晕]': '😵',
  '[折磨]': '😣', '[衰]': '😞', '[骷髅]': '💀', '[敲打]': '🔨', '[再见]': '👋',
  '[擦汗]': '😅', '[抠鼻]': '🤭', '[鼓掌]': '👏', '[糗大了]': '😳', '[坏笑]': '😈',
  '[左哼哼]': '😤', '[右哼哼]': '😤', '[哈欠]': '🥱', '[鄙视]': '😒', '[委屈]': '🥺',
  '[快哭了]': '😢', '[阴险]': '😏', '[亲亲]': '😘', '[吓]': '😱', '[可怜]': '🥺',
  '[捂脸]': '🤦', '[加油]': '💪', '[敬礼]': '🫡', '[专注]': '🧐', '[思考]': '🤔',
  '[激动]': '🤩', '[祈祷]': '🙏', '[庆祝]': '🎉', '[炸弹]': '💣',
  // 手势/动作
  '[强]': '👍', '[弱]': '👎', '[握手]': '🤝', '[胜利]': '✌️', '[抱拳]': '🙏',
  '[勾引]': '👉', '[拳头]': '👊', '[差劲]': '👎', '[爱你]': '🫶', '[OK]': '🙆',
  '[NO]': '🙅', '[点赞]': '👍', '[拥抱]': '🤗', '[献吻]': '💋', '[跳跳]': '🦘',
  '[发抖]': '🫨', '[转圈]': '🌀', '[磕头]': '🙇', '[跳绳]': '🤸', '[挥手]': '👋',
  // 物品/符号
  '[菜刀]': '🔪', '[西瓜]': '🍉', '[啤酒]': '🍺', '[篮球]': '🏀', '[乒乓]': '🏓',
  '[咖啡]': '☕', '[饭]': '🍚', '[猪头]': '🐷', '[玫瑰]': '🌹', '[凋谢]': '🥀',
  '[爱心]': '❤️', '[心碎]': '💔', '[示爱]': '❤️', '[蛋糕]': '🎂', '[闪电]': '⚡',
  '[刀]': '🔪', '[足球]': '⚽', '[便便]': '💩', '[月亮]': '🌙', '[太阳]': '☀️',
  '[礼物]': '🎁', '[爱情]': '💕', '[飞吻]': '😘', '[666]': '🔥', '[红牌]': '🟥',
  '[绿牌]': '🟩', '[清单]': '📝', '[勾]': '✔️', '[叉]': '❌',
};
// i18n-exempt-block-end

/** 预编译正则：匹配所有已知 shortcode，按长度降序避免短码遮蔽长码 */
const EMOJI_SHORTCODE_REGEX = new RegExp(
  Object.keys(EMOJI_SHORTCODE_MAP)
    .sort((a, b) => b.length - a.length)
    .map((k) => k.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
    .join('|'),
  'g'
);

/**
 * 将文本中的 [表情名] shortcode 替换为 Unicode emoji。
 * 仅替换已知映射表中的 shortcode，未知方括号文本（如 [文件]）保持原样。
 * 应在 sanitizeHtml 之后调用，对已清理的 HTML 字符串做纯文本替换。
 */
export function replaceEmojiShortcodes(html: string): string {
  if (!html) return '';
  return html.replace(EMOJI_SHORTCODE_REGEX, (match) => EMOJI_SHORTCODE_MAP[match] ?? match);
}

/**
 * 日期 + 时间。走 `Intl.DateTimeFormat`。
 *
 * `en` 用其默认的 12 小时制（不传 `hour12`）；`zh-Hans` 保持现状的 24 小时制。
 *
 * @param iso ISO 时间串
 * @param timezone IANA 时区（可选）
 * @param locale 显式语言（可选，缺省用当前界面语言）
 */
export function formatDateTime(iso: string | null | undefined, timezone?: string, locale?: string): string {
  if (!iso) return '-';
  const d = new Date(iso);
  if (isNaN(d.getTime())) return '-';
  const effective = resolveLocale(locale);
  const options: Intl.DateTimeFormatOptions = {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
  };
  if (!effective.toLowerCase().startsWith('en')) {
    options.hour12 = false;
  }
  if (timezone) {
    options.timeZone = timezone;
  }
  return new Intl.DateTimeFormat(toIntlLocale(effective), options).format(d);
}

/**
 * 短日期。走 `Intl.DateTimeFormat`（`zh` → `2026/08/15`，`en` → `08/15/2026`）。
 *
 * @param iso ISO 时间串
 * @param timezone IANA 时区（可选）
 * @param locale 显式语言（可选，缺省用当前界面语言）
 */
export function formatDate(iso: string | null | undefined, timezone?: string, locale?: string): string {
  if (!iso) return '-';
  const d = new Date(iso);
  if (isNaN(d.getTime())) return '-';
  const options: Intl.DateTimeFormatOptions = {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
  };
  if (timezone) {
    options.timeZone = timezone;
  }
  return new Intl.DateTimeFormat(toIntlLocale(resolveLocale(locale)), options).format(d);
}

/**
 * 文件大小。单位（B/KB/MB/GB）是国际通用符号，不进字典；
 * 数字部分走 `Intl.NumberFormat`（小数位与千分位随语言，例如阿拉伯语数字、小数逗号）。
 */
export function formatFileSize(bytes: number, locale?: string): string {
  if (bytes < 1024) return `${bytes} B`;
  const nf = (digits: number) =>
    new Intl.NumberFormat(toIntlLocale(resolveLocale(locale)), {
      minimumFractionDigits: digits,
      maximumFractionDigits: digits,
    });
  if (bytes < 1024 * 1024) return `${nf(1).format(bytes / 1024)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${nf(1).format(bytes / 1024 / 1024)} MB`;
  return `${nf(2).format(bytes / 1024 / 1024 / 1024)} GB`;
}

/**
 * 相对时间。
 *
 * 数值型（n 分钟 / n 小时 / n 天）走 `Intl.RelativeTimeFormat(locale, { numeric: 'auto' })`：
 * `zh` → `5分钟前` / `昨天`，`en` → `5 minutes ago` / `yesterday`。
 * 唯一例外是"刚刚"——它不是数值语义而是固定文案（`numeric: 'auto'` 下 Intl 会输出
 * `现在` / `now`，会改掉中文界面既有的措辞），故留作字典条目 `format.relativeJustNow`。
 *
 * 超过 7 天回落到 `formatDate`。
 *
 * @param iso ISO 时间串
 * @param locale 显式语言（可选，缺省用当前界面语言）
 */
export function relativeTime(iso: string | null | undefined, locale?: string): string {
  if (!iso) return '-';
  const d = new Date(iso).getTime();
  const now = Date.now();
  const diff = now - d;
  const min = 60 * 1000;
  const hour = 60 * min;
  const day = 24 * hour;
  if (diff < min) return tBound('format.relativeJustNow', locale);
  const effective = resolveLocale(locale);
  const rtf = new Intl.RelativeTimeFormat(toIntlLocale(effective), { numeric: 'auto' });
  if (diff < hour) return rtf.format(-Math.floor(diff / min), 'minute');
  if (diff < day) return rtf.format(-Math.floor(diff / hour), 'hour');
  if (diff < 7 * day) return rtf.format(-Math.floor(diff / day), 'day');
  return formatDate(iso, undefined, effective);
}

/**
 * 相对时间格式化别名（兼容调用端传入 Date 或 ISO 字符串）。
 * 命名更贴近其他项目的惯例，与 relativeTime 语义一致。
 */
export function formatRelative(input: string | Date | null | undefined, locale?: string): string {
  if (input == null) return '-';
  if (input instanceof Date) {
    if (isNaN(input.getTime())) return '-';
    return relativeTime(input.toISOString(), locale);
  }
  return relativeTime(String(input), locale);
}

export function isImage(mimeType: string): boolean {
  return mimeType?.startsWith('image/');
}

/** 判断 MIME 类型是否为音频（含微信 amr 格式） */
export function isAudio(mimeType: string): boolean {
  return mimeType?.startsWith('audio/');
}

/** 判断 MIME 类型是否为视频 */
export function isVideo(mimeType: string): boolean {
  return mimeType?.startsWith('video/');
}

/** 会话状态 code（后端 ConversationStatus）→ 文案 key。code 稳定、不随语言变化 */
const STATUS_LABEL_KEYS: Record<number, TranslationKey> = {
  1: 'format.status.active',
  2: 'format.status.pending',
  3: 'format.status.closed',
  4: 'format.status.spam',
};

export function getStatusLabel(status: number, locale?: string): string {
  const key = STATUS_LABEL_KEYS[status];
  return key ? tBound(key, locale) : tBound('format.unknown', locale);
}

export function getStatusColor(status: number): string {
  const map: Record<number, string> = {
    1: 'bg-green-100 text-green-700 border-green-200',
    2: 'bg-amber-100 text-amber-700 border-amber-200',
    3: 'bg-gray-100 text-gray-600 border-gray-200',
    4: 'bg-red-100 text-red-700 border-red-200',
  };
  return map[status] || 'bg-gray-100 text-gray-600 border-gray-200';
}

// ====== 优先级相关 ======
// 优先级：0=Low、1=Normal、2=High、3=Urgent

/** 优先级 code（0=Low、1=Normal、2=High、3=Urgent）→ 文案 key */
const PRIORITY_LABEL_KEYS: Record<number, TranslationKey> = {
  0: 'format.priority.low',
  1: 'format.priority.normal',
  2: 'format.priority.high',
  3: 'format.priority.urgent',
};

/** 优先级标签（文案见真源 `locales/<lang>/format.ts`） */
export function getPriorityLabel(priority: number, locale?: string): string {
  const key = PRIORITY_LABEL_KEYS[priority];
  return tBound(key ?? 'format.priority.normal', locale);
}

/** 优先级 badge 颜色（含背景、文字、边框，含 dark 模式） */
export function getPriorityColor(priority: number): string {
  const map: Record<number, string> = {
    0: 'bg-sky-100 text-sky-700 border-sky-200 dark:bg-sky-900/40 dark:text-sky-400 dark:border-sky-800',
    1: 'bg-emerald-100 text-emerald-700 border-emerald-200 dark:bg-emerald-900/40 dark:text-emerald-400 dark:border-emerald-800',
    2: 'bg-orange-100 text-orange-700 border-orange-200 dark:bg-orange-900/40 dark:text-orange-400 dark:border-orange-800',
    3: 'bg-red-100 text-red-700 border-red-200 dark:bg-red-900/40 dark:text-red-400 dark:border-red-800',
  };
  return map[priority] ?? map[1];
}

/** 优先级文字颜色（不含背景，用于下拉选择器的文字颜色随值变化） */
export function getPriorityTextColor(priority: number): string {
  const map: Record<number, string> = {
    0: 'text-sky-700 dark:text-sky-400',
    1: 'text-emerald-700 dark:text-emerald-400',
    2: 'text-orange-700 dark:text-orange-400',
    3: 'text-red-700 dark:text-red-400',
  };
  return map[priority] ?? map[1];
}

/** 来源类型 code（后端 SourceType 枚举）→ 文案 key */
const SOURCE_TYPE_LABEL_KEYS: Record<number, TranslationKey> = {
  1: 'format.sourceType.email',
  2: 'format.sourceType.web',
  3: 'format.sourceType.api',
  4: 'format.sourceType.phone',
  5: 'format.sourceType.chat',
  6: 'format.sourceType.social',
  7: 'format.sourceType.wechat',
  8: 'format.sourceType.whatsapp',
  9: 'format.sourceType.feishu',
  10: 'format.sourceType.dingtalk',
  11: 'format.sourceType.messenger',
  12: 'format.sourceType.telegram',
  13: 'format.sourceType.line',
  14: 'format.sourceType.imessage',
};

/** 来源类型标签（对应后端 SourceType 枚举：Email=1, Web=2, Api=3, Phone=4, Chat=5, Social=6, WeChat=7, WhatsApp=8, Feishu=9, DingTalk=10, Messenger=11, Telegram=12, Line=13, IMessage=14） */
export function getSourceTypeLabel(sourceType: number | undefined, locale?: string): string {
  const key = sourceType == null ? undefined : SOURCE_TYPE_LABEL_KEYS[sourceType];
  return key ? tBound(key, locale) : tBound('format.unknown', locale);
}

/** 来源类型徽章颜色（含背景+文字色，用于侧边栏/列表展示） */
export function getSourceTypeColor(sourceType: number | undefined): string {
  const map: Record<number, string> = {
    1: 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-400',
    2: 'bg-cyan-100 text-cyan-700 dark:bg-cyan-900/40 dark:text-cyan-400',
    3: 'bg-gray-100 text-gray-700 dark:bg-slate-700 dark:text-slate-300',
    4: 'bg-purple-100 text-purple-700 dark:bg-purple-900/40 dark:text-purple-400',
    5: 'bg-teal-100 text-teal-700 dark:bg-teal-900/40 dark:text-teal-400',
    6: 'bg-pink-100 text-pink-700 dark:bg-pink-900/40 dark:text-pink-400',
    7: 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-400',
    8: 'bg-emerald-100 text-emerald-700 dark:bg-emerald-900/40 dark:text-emerald-400',
    9: 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-400',
    10: 'bg-indigo-100 text-indigo-700 dark:bg-indigo-900/40 dark:text-indigo-400',
  };
  return (sourceType != null && map[sourceType]) || 'bg-gray-100 text-gray-700 dark:bg-slate-700 dark:text-slate-300';
}

/**
 * 把数字 SourceType（与后端 SourceType 枚举值对应）转换为字符串标识符（小写枚举名）。
 * 用于自定义字段 API 的 sourceType 参数（如 conversation.sourceType=7 → "wechat"）。
 * 未知值返回 null，由调用方按 undefined 处理（不传 sourceType，走全局字段逻辑）。
 */
export function sourceTypeToString(sourceType: number | undefined | null): string | null {
  if (sourceType == null) return null;
  const map: Record<number, string> = {
    1: 'email',
    2: 'web',
    3: 'api',
    4: 'phone',
    5: 'chat',
    6: 'social',
    7: 'wechat',
    8: 'whatsapp',
    9: 'feishu',
    10: 'dingtalk',
    11: 'messenger',
    12: 'telegram',
    13: 'line',
    14: 'imessage',
  };
  return map[sourceType] ?? null;
}

/** 渠道字符串（与后端 KnownChannels 一致）→ 文案 key，用于列表警告标签等 */
const SOURCE_TYPE_CODE_LABEL_KEYS: Record<string, TranslationKey> = {
  email: 'format.sourceType.email',
  web: 'format.sourceType.web',
  api: 'format.sourceType.api',
  phone: 'format.sourceType.phone',
  chat: 'format.sourceType.liveChat',
  social: 'format.sourceType.social',
  wechat: 'format.sourceType.wechat',
  whatsapp: 'format.sourceType.whatsapp',
  feishu: 'format.sourceType.feishu',
  dingtalk: 'format.sourceType.dingtalk',
  messenger: 'format.sourceType.messenger',
  telegram: 'format.sourceType.telegram',
  line: 'format.sourceType.line',
  imessage: 'format.sourceType.imessage',
};

/** 渠道字符串 → 显示名（未知 code 原样返回，便于暴露后端新增渠道） */
export function getSourceTypeLabelByCode(code: string | null | undefined, locale?: string): string {
  if (!code) return '';
  const key = SOURCE_TYPE_CODE_LABEL_KEYS[code];
  return key ? tBound(key, locale) : code;
}

/** 判定字符串是否为邮箱地址（用于识别"客户真名缺失时回退成邮箱"的显示名，避免在会话中泄露客户邮箱） */
export function isEmailAddress(value: string | null | undefined): boolean {
  return typeof value === 'string' && /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim());
}

/**
 * 获取客户展示名：优先返回客户登记的真实姓名，当实名为空或回退成邮箱地址时用友好"客户"兜底。
 * 用于会话头部"客户："等场景，避免把客户邮箱（如 qq 邮箱）当作名称暴露给坐席。
 */
export function displayCustomerName(fullName: string | null | undefined, locale?: string): string {
  if (typeof fullName === 'string' && fullName.trim() && !isEmailAddress(fullName)) {
    return fullName.trim();
  }
  return tBound('format.customerFallback', locale);
}
