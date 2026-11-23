import { useState, useEffect, useMemo, useRef, useCallback } from 'react';
import { useSearchParams } from 'react-router-dom';
import { knowledgeApi } from '../api';
import { useTimedToast } from '../hooks/useTimedToast';
import { useAuth } from '../context/AuthContext';
import { isAdmin } from '../utils/permissions';
import { useI18n } from '../context/I18nContext';
import { toIntlLocale } from '../i18n/locale';
import type { TranslationKey } from '../locales/zh-Hans';
import { formatDate, formatDateTime } from '../utils/format';
import type {
  KnowledgeCategoryDto,
  KnowledgeArticleDto,
  KnowledgeArticleAttachmentDto,
  KnowledgeOverviewResponse,
  KnowledgeArticleQuery,
  KnowledgeSearchResult,
  CreateKnowledgeCategoryRequest,
  UpdateKnowledgeCategoryRequest,
  CreateKnowledgeArticleRequest,
  UpdateKnowledgeArticleRequest,
} from '../api/types';
import Modal from '../components/Modal';
import ConfirmDialog from '../components/ConfirmDialog';
import { useMailboxes } from '../context/MailboxContext';
import {
  Plus,
  Search,
  Edit2,
  Trash2,
  BookOpen,
  Eye,
  ThumbsUp,
  Folder,
  ChevronRight,
  ChevronDown,
  Pin,
  FileText,
  TrendingUp,
  Clock,
  ArrowLeft,
  Save,
  Sparkles,
  RefreshCw,
  Recycle,
  RotateCcw,
  Eraser,
  Hash,
  Bold,
  Italic,
  Code,
  List,
  Link as LinkIcon,
  Heading1,
  Heading2,
  Heading3,
  Quote,
  Image,
  Eye as EyeIcon,
  Pencil,
  Layers,
  ChevronLeft,
  Tag,
  User,
  Calendar,
  CheckCircle2,
  Archive,
  FileEdit,
  FolderPlus,
  AlertTriangle,
  Mail,
  Paperclip,
  Upload,
  Download,
  File as FileIcon,
  FileType,
  X,
  Loader2,
  Cloud,
} from 'lucide-react';

// 文章状态颜色
const STATUS_COLORS: Record<string, string> = {
  draft:
    'bg-amber-50 dark:bg-amber-900/30 text-amber-700 dark:text-amber-300 ring-1 ring-amber-200/60 dark:ring-amber-700/40',
  published:
    'bg-green-50 dark:bg-green-900/30 text-green-700 dark:text-green-300 ring-1 ring-green-200/60 dark:ring-green-700/40',
  archived:
    'bg-gray-50 dark:bg-slate-700/40 text-gray-700 dark:text-slate-300 ring-1 ring-gray-200/60 dark:ring-slate-600/40',
};

const STATUS_OPTIONS: { value: string; labelKey: TranslationKey; icon: typeof FileEdit; color: string }[] = [
  { value: 'draft', labelKey: 'knowledge.status.draft', icon: FileEdit, color: 'text-amber-600' },
  { value: 'published', labelKey: 'knowledge.status.published', icon: CheckCircle2, color: 'text-green-600' },
  { value: 'archived', labelKey: 'knowledge.status.archived', icon: Archive, color: 'text-gray-600' },
];

/**
 * 文章状态码 → 展示标签 key。
 *
 * 后端 `StatusLabel` 是**中文**字面量（`MappingExtensions` 的 `KnowledgeArticleDto.ToDto`：
 * draft→草稿 / published→已发布 / archived→已归档），英文界面下直接渲染会漏出中文，
 * 因此徽章展示改为前端字典按**稳定 code** 取值。码集合对照后端白名单
 * `KnowledgeService.AllowedStatuses`（恰好这三个码）；字典未覆盖的码回落渲染原始 code，
 * 与后端 `statusLabels.TryGetValue` 失败时回落 `a.Status` 的行为一致。
 */
const STATUS_LABEL_KEYS: Record<string, TranslationKey | undefined> = {
  draft: 'knowledge.status.draft',
  published: 'knowledge.status.published',
  archived: 'knowledge.status.archived',
};

// 预设图标列表
const PRESET_ICONS = ['📚', '📘', '📖', '📄', '📝', '💡', '🔧', '⚙️', '🎯', '📋', '🏷️', '📌', '✅', '❓', '⚠️', '🔥'];

/**
 * 数字千分位：显式按**界面语言**走 Intl。
 * `toLocaleString()` 不带参数用的是浏览器默认语言，与界面语言可能不一致。
 */
function formatCount(n: number, locale: string): string {
  return new Intl.NumberFormat(toIntlLocale(locale)).format(n);
}

/**
 * `<select>` 多级选项缩进。
 *
 * 原实现用全角空格（U+3000）——它会被硬编码中文门禁判为"中文标点/全角字符"
 * （基线里 Knowledge.tsx:2318/2994/3416 三处）。改用 **EM SPACE（U+2003）**：
 * 它与全角空格同为固定宽度（1 em）、HTML 不折叠，因此缩进宽度与改造前**逐字级等价**，
 * 而 U+2003 不在门禁的两个被判区间（U+3000–U+303F / U+FF00–U+FFEF）内。
 * ⚠ 不要退化成 U+00A0×2：那是半角宽度，两层才约等于 1 em，会让层级缩进视觉变窄。
 */
const categoryIndent = (depth: number): string => '\u2003'.repeat(depth);

import DOMPurify from 'dompurify';
import { marked } from 'marked';
import hljs from 'highlight.js';
import 'highlight.js/styles/github.css';

// ====== Markdown 渲染：marked + highlight.js 代码高亮 ======
// 配置 marked 自定义渲染器：保持与原有 CSS class 风格一致（tailwind 类名），
// 标题加 data-anchor，图片 lazy loading，代码块走 hljs.highlightAuto。
// KB-013: 移除 as any / : any，使用具体接口类型保证类型安全
//
// KB-T000102 — marked v18 Renderer 注意事项：
//  marked v18 不允许重写 block-level Renderer 方法（paragraph / list / listitem / blockquote 等）
//  时直接返回原始 token.text，否则会禁用其内部的 inline walker，导致 ** / * / ` / []() / ![]() 等
//  inline 语法不被解析。
// 因此我们只覆盖不会影响 inline 递归的方法：heading + code，其他样式
// 统一在 marked.parse 之后使用轻量 post-process 注入 Tailwind class，
// 最后再经 DOMPurify.sanitize 放行。
const markedRenderer = new marked.Renderer();
{
  const slugCounters = new Map<string, number>();
  const makeSlug = (text: string): string => {
    let slug = text
      .trim()
      .toLowerCase()
      .replace(/[^\p{L}\p{N}\s-]/gu, '')
      .replace(/\s+/g, '-')
      .replace(/-+/g, '-')
      .replace(/^-|-$/g, '');
    if (!slug) slug = 'heading';
    const count = slugCounters.get(slug) ?? 0;
    slugCounters.set(slug, count + 1);
    return count === 0 ? slug : `${slug}-${count}`;
  };

  markedRenderer.heading = ({ text, depth }: { text: string; depth: number }) => {
    const slug = makeSlug(text);
    const cls =
      depth === 1
        ? 'text-2xl font-bold mt-5 mb-3 text-slate-900 dark:text-slate-100'
        : depth === 2
          ? 'text-xl font-semibold mt-5 mb-2 text-slate-800 dark:text-slate-200'
          : 'text-lg font-semibold mt-5 mb-2 text-slate-800 dark:text-slate-200';
    return `<h${depth} data-anchor="${slug}" class="${cls}">${text}</h${depth}>`;
  };

  markedRenderer.code = ({ text, lang }: { text: string; lang?: string }) => {
    let highlighted: string;
    try {
      if (lang && hljs.getLanguage(lang)) {
        highlighted = hljs.highlight(text, { language: lang, ignoreIllegals: true }).value;
      } else {
        highlighted = hljs.highlightAuto(text).value;
      }
    } catch {
      highlighted = DOMPurify.sanitize(text);
    }
    return `<pre class="hljs bg-slate-50 dark:bg-slate-900/60 p-4 rounded-lg my-3 overflow-x-auto text-sm border border-slate-200 dark:border-slate-700"><code class="hljs-body">${highlighted}</code></pre>`;
  };
}

marked.setOptions({
  renderer: markedRenderer,
  breaks: true,
  gfm: true,
});

/**
 * marked.parse 输出 → 追加 Tailwind class（后处理）。
 * 注意：所有替换都只修改"开始标签的属性部分"，绝不触碰节点内部文本，
 * 避免破坏用户输入的内容（例如内容里恰好出现 `<img` 字符串除外——它们是不可能出现在 HTML text 节点的）。
 * 支持嵌套列表（ul/ol 内再次出现 ul/ol 也会被正确加 class）。
 */
function postAddTailwindClasses(html: string): string {
  let out = html;
  // 列表
  out = out.replace(/<ul(?=\s|>)/g, '<ul class="list-disc pl-6 my-3 marker:text-slate-500 dark:marker:text-slate-400 space-y-1"');
  out = out.replace(/<ol(?=\s|>)( start="\d+")?/g, (_m, startAttr) =>
    `<ol class="list-decimal pl-6 my-3 marker:text-slate-500 dark:marker:text-slate-400 space-y-1"${startAttr ?? ''}>`
  );
  out = out.replace(/<li(?=\s|>)/g, '<li class="ml-0 pl-1 text-slate-700 dark:text-slate-300 leading-7 marker:text-slate-500 dark:marker:text-slate-400"');
  // 代码：避免覆盖 <pre> 里的 hljs-body（<code 开始标签仅在 codespan 默认 <code> 上生效，pre>code 已带 hljs-body class）
  out = out.replace(/<code(?=\s|>)( class="hljs-body")?/g, (_m, hl) =>
    hl
      ? `<code class="hljs-body"`
      : `<code class="px-1.5 py-0.5 bg-slate-100 dark:bg-slate-700 rounded text-sm text-rose-600 dark:text-rose-400"`
  );
  // 链接：外部链接自动加 _blank
  out = out.replace(/<a(?=\s[^>]*?href="(?!#)([^"]+)"[^>]*?>)/g,
    `<a target="_blank" rel="noopener noreferrer"`
  );
  out = out.replace(/<a(?=\s|>)/g,
    '<a class="text-blue-600 dark:text-blue-400 underline hover:text-blue-700 cursor-pointer"'
  );
  // 图片
  out = out.replace(/<img(?=\s|>)/g, '<img loading="lazy" class="max-w-full rounded-lg my-3 shadow-sm"');
  // 引用
  out = out.replace(/<blockquote(?=\s|>)/g,
    '<blockquote class="border-l-4 border-blue-400 dark:border-blue-500 pl-4 py-1 italic text-slate-600 dark:text-slate-400 my-3 bg-blue-50/50 dark:bg-blue-900/20 rounded-r"');
  // 分隔线
  out = out.replace(/<hr(?=\s|>)/g, '<hr class="my-5 border-slate-200 dark:border-slate-700"');
  // 段落
  out = out.replace(/<p(?=\s|>)/g, '<p class="my-2 leading-relaxed"');
  // 表格
  out = out.replace(/<table(?=\s|>)/g,
    '<table class="w-full text-sm border-collapse my-3 border border-slate-200 dark:border-slate-700"');
  out = out.replace(/<thead(?=\s|>)/g, '<thead class="bg-slate-50 dark:bg-slate-900/40"');
  out = out.replace(/<tr(?=\s|>)/g, '<tr class="border-b border-slate-200 dark:border-slate-700"');
  // th 与 td 的 align 样式由 DOMPurify 放行
  out = out.replace(/<th(?=\s|>)/g,
    '<th class="px-3 py-2 border border-slate-200 dark:border-slate-700 font-semibold text-slate-600 dark:text-slate-300 align-middle"');
  out = out.replace(/<td(?=\s|>)/g,
    '<td class="px-3 py-2 border border-slate-200 dark:border-slate-700 align-middle"');
  return out;
}

// 允许内联 SVG 图表的 DOMPurify 配置：放开 svg 系列标签和 style（用于 SVG 的 CSS 类），
// 同时保留事件属性的拦截，确保 XSS 安全。
const SVG_ALLOWED_TAGS = [
  'svg', 'g', 'path', 'rect', 'circle', 'ellipse', 'line', 'polyline', 'polygon',
  'text', 'tspan', 'title', 'desc', 'defs', 'symbol', 'use',
  'linearGradient', 'radialGradient', 'stop', 'clipPath', 'mask', 'pattern',
  'marker', 'animate', 'animateTransform', 'animateMotion',
  'view', 'switch', 'foreignObject', 'image',
];
const SVG_ALLOWED_ATTR = [
  'viewBox', 'width', 'height', 'x', 'y', 'cx', 'cy', 'r', 'rx', 'ry',
  'd', 'points', 'x1', 'y1', 'x2', 'y2', 'fill', 'stroke', 'stroke-width',
  'stroke-linecap', 'stroke-linejoin', 'stroke-dasharray', 'stroke-opacity',
  'fill-opacity', 'opacity', 'transform', 'gradientTransform', 'gradientUnits',
  'patternUnits', 'patternContentUnits', 'clipPathUnits', 'maskUnits',
  'markerUnits', 'markerWidth', 'markerHeight', 'markerUnits', 'refX', 'refY',
  'orient', 'preserveAspectRatio', 'xmlns', 'xmlns:xlink', 'xlink:href',
  'href', 'xlink:title', 'role', 'aria-label', 'aria-labelledby', 'aria-describedby',
  'id', 'class', 'style', 'tabindex', 'lang',
];

function renderMarkdown(md: string): string {
  if (!md) return '';
  try {
    // KB-T000102：统一"段落归一化"预处理（不破坏代码块围栏）：
    // 将单行结尾的 \n（代码块外部）合并为段落间空行，避免 breaks:true 把段落压成同一 <p> 不排版。
    // 参考 Experience 304047：解析前预处理 >> 仅靠 CSS margin。
    const normalized = (() => {
      const lines = md.split(/\r?\n/);
      const out: string[] = [];
      let inFence = false;
      for (const line of lines) {
        if (/^\s*```/.test(line)) inFence = !inFence;
        if (!inFence && line.trim() === '') {
          out.push('');
          continue;
        }
        out.push(line);
      }
      return out.join('\n');
    })();
    // marked v18：只覆盖 heading/code 两项 block 级 renderer，inline 解析和 list 嵌套都由默认实现处理，
    // 之后用 postAddTailwindClasses 统一为 ul/ol/li/code/a/img/blockquote/table 等元素加 Tailwind class。
    const rawHtml = marked.parse(normalized, { async: false }) as string;
    const styledHtml = postAddTailwindClasses(rawHtml);
    const wrapped = `<div class="knowledge-content">${styledHtml}</div>`;
    const purifyCfg = {
      // KB-T000102：显式放开 style 属性（白名单只允许 text-align/vertical-align 等安全 CSS）；
      // 配合 FORBID_ATTR 只禁 on* 事件，确保 table 对齐、代码块自定义内联样式不被清掉。
      ADD_ATTR: ['target', 'rel', 'class', 'src', 'alt', 'id', 'data-anchor', 'disabled', 'checked', 'style', 'start', 'loading', 'referrerpolicy', 'decoding', ...SVG_ALLOWED_ATTR],
      FORBID_TAGS: ['script', 'iframe', 'embed', 'object', 'form', 'textarea', 'select', 'button', 'base', 'link', 'meta', 'math'],
      FORBID_ATTR: [
        'onclick', 'onload', 'onerror', 'onmouseover', 'onfocus', 'onblur', 'onchange', 'onsubmit',
        'onkeydown', 'onkeypress', 'onkeyup', 'onmousedown', 'onmouseup', 'onmousemove', 'onmouseout', 'ondblclick',
        'onanimationstart', 'onanimationend', 'onanimationiteration', 'ontransitionend', 'onpointerdown', 'onpointerup',
      ],
      ALLOW_UNKNOWN_PROTOCOLS: false,
      ALLOW_DATA_ATTR: true,
      SANITIZE_NAMED_PROPS: true,
      // S5: 收紧 data: URI —— 仅允许栅格图片（data:image/png|jpeg|gif|webp|bmp），
      // 原正则放开任意 data: 协议，data:text/html 等可被 <a href> 点击后执行脚本。
      ALLOWED_URI_REGEXP: /^(?:(?:(?:f|ht)tps?|mailto:|tel:|callto:|cid:|xmpp):|data:image\/(?:png|jpe?g|gif|webp|bmp)|#|[^a-z]|[a-z+.-]+(?:[^a-z+.\-:]|$))/i,
      ADD_TAGS: ['input', 'wbr', 'details', 'summary', ...SVG_ALLOWED_TAGS],
      WHOLE_DOCUMENT: false,
      RETURN_DOM: false,
      RETURN_DOM_FRAGMENT: false,
      IN_PLACE: false,
      FORCE_BODY: false,
      USE_PROFILES: { html: true, svg: true, svgFilters: true },
      // DOMPurify 默认会清 style，但我们仅允许白名单 CSS 属性（防止 CSS 注入）：
      // S5: 移除 background / list-style 简写属性——两者允许 url()，可被用于 CSS 数据外带
      ALLOWED_CSS_PROPERTIES: [
        'text-align', 'vertical-align', 'white-space', 'word-break', 'overflow-wrap',
        'color', 'background-color', 'border-color', 'border-radius', 'border',
        'width', 'height', 'max-width', 'max-height', 'min-width', 'min-height',
        'padding', 'padding-left', 'padding-right', 'padding-top', 'padding-bottom',
        'margin', 'margin-left', 'margin-right', 'margin-top', 'margin-bottom',
        'font-weight', 'font-style', 'font-size', 'line-height', 'font-family',
        'list-style-type', 'list-style-position',
        'opacity', 'display', 'text-decoration', 'text-decoration-color', 'text-underline-offset',
        'box-shadow', 'transform', 'float', 'clear',
      ],
    } as const;
    // 注意：ALLOWED_CSS_PROPERTIES 在部分类型版本中未声明，但 DOMPurify 运行时支持，故用 unknown 断言避免 TS 报错。
    return DOMPurify.sanitize(wrapped, purifyCfg as unknown as Parameters<typeof DOMPurify.sanitize>[1]);
  } catch (err) {
    console.warn('[Knowledge] marked parse failed, fallback escaped:', err);
    return `<div class="knowledge-content"><p class="my-2 whitespace-pre-wrap break-words">${DOMPurify.sanitize(md)}</p></div>`;
  }
}

// 计算字数
function countWords(text: string): number {
  if (!text) return 0;
  // 中文按字计算，英文按词计算
  const chineseChars = (text.match(/[\u4e00-\u9fa5]/g) || []).length;
  const englishWords = (text.replace(/[\u4e00-\u9fa5]/g, ' ').match(/\b\w+\b/g) || []).length;
  return chineseChars + englishWords;
}

// 阅读时间估算（分钟）
function estimateReadTime(text: string): number {
  const words = countWords(text);
  return Math.max(1, Math.ceil(words / 300));
}

// 文章内容字符上限（与后端 DTO [MaxLength(200000)] 保持一致）
const MAX_ARTICLE_CONTENT = 200000;

// 统一提取后端错误信息：优先 response.data.message，其次 message，最后 fallback
function extractErrorMessage(e: unknown, fallback: string): string {
  if (!e) return fallback;
  const err = e as { message?: string; response?: { data?: { message?: string; errors?: unknown } } };
  const respMsg = err?.response?.data?.message;
  if (typeof respMsg === 'string' && respMsg.trim()) return respMsg.trim();
  if (typeof err?.message === 'string' && err.message.trim()) return err.message.trim();
  return fallback;
}

interface CategoryForm {
  name: string;
  description: string;
  parentId: number | null;
  mailboxId: number | null;
  sortOrder: number;
  icon: string;
  isActive: boolean;
}

const EMPTY_CATEGORY_FORM: CategoryForm = {
  name: '',
  description: '',
  parentId: null,
  mailboxId: null,
  sortOrder: 0,
  icon: '',
  isActive: true,
};

interface ArticleForm {
  title: string;
  content: string;
  /** 内容格式：markdown / html */
  contentType: string;
  summary: string;
  categoryId: number | null;
  tags: string;
  status: string;
  sortOrder: number;
  isPinned: boolean;
}

const EMPTY_ARTICLE_FORM: ArticleForm = {
  title: '',
  content: '',
  contentType: 'markdown',
  summary: '',
  categoryId: null,
  tags: '',
  status: 'draft',
  sortOrder: 0,
  isPinned: false,
};

// 内容格式选项
const CONTENT_TYPE_OPTIONS = [
  { value: 'markdown', label: 'Markdown', icon: FileText },
  { value: 'html', label: 'HTML', icon: FileType },
] as const;

// 附件上传：允许的文件类型白名单（与后端一致）
const ATTACHMENT_ALLOWED_EXTENSIONS = ['pdf', 'png', 'jpg', 'jpeg', 'gif', 'webp', 'svg', 'bmp', 'doc', 'docx', 'xls', 'xlsx', 'ppt', 'pptx', 'txt', 'csv', 'zip', 'rar', '7z'];
const ATTACHMENT_MAX_SIZE = 25 * 1024 * 1024; // 25MB

/**
 * 渲染 HTML 内容（安全过滤后返回 HTML 字符串）
 * HTML 模式下内容由用户直接编写 HTML，后端入库前已做 HtmlSanitizerHelper.Sanitize，
 * 前端展示前再次经过 DOMPurify 清洗（防御性二次过滤），防止存储型 XSS。
 * 支持内联 SVG 图表（流程/架构图）：放开 svg 系列标签与 style/CSS 属性。
 */
function renderHtml(html: string): string {
  if (!html) return '';
  return DOMPurify.sanitize(html, {
    ADD_ATTR: ['target', 'rel', 'class', 'src', 'alt', 'id', 'data-anchor', 'href', 'controls', 'width', 'height', ...SVG_ALLOWED_ATTR],
    FORBID_TAGS: ['script', 'iframe', 'embed', 'object', 'form', 'input', 'textarea', 'select', 'button', 'base', 'link', 'meta', 'math'],
    FORBID_ATTR: ['onclick', 'onload', 'onerror', 'onmouseover', 'onfocus', 'onblur', 'onchange', 'onsubmit', 'onkeydown', 'onkeypress', 'onkeyup', 'onmousedown', 'onmouseup', 'onmousemove', 'onmouseout', 'ondblclick'],
    ALLOW_UNKNOWN_PROTOCOLS: false,
    ALLOW_DATA_ATTR: true,
    SANITIZE_NAMED_PROPS: true,
    // T2: 与 renderMarkdown 对齐——仅放行栅格图片 data:image/(png|jpeg|gif|webp|bmp)，
    // 原正则放行任意 data: 协议，data:text/html 可被 <a href> 点击后执行脚本
    ALLOWED_URI_REGEXP: /^(?:(?:(?:f|ht)tps?|mailto|tel|callto|cid|xmpp):|data:image\/(?:png|jpe?g|gif|webp|bmp)|#|[^a-z]|[a-z+.-]+(?:[^a-z+.\-:]|$))/i,
    ADD_TAGS: [...SVG_ALLOWED_TAGS],
    WHOLE_DOCUMENT: false,
    RETURN_DOM: false,
    RETURN_DOM_FRAGMENT: false,
    IN_PLACE: false,
    FORCE_BODY: false,
    USE_PROFILES: { html: true, svg: true, svgFilters: true },
  });
}

/**
 * 根据内容格式统一渲染为 HTML 字符串（markdown -> renderMarkdown，html -> renderHtml）
 */
function renderContent(content: string, contentType: string): string {
  return contentType === 'html' ? renderHtml(content) : renderMarkdown(content);
}

/**
 * 格式化附件文件大小（人类可读）
 */
function formatAttachmentSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

/**
 * 获取文件扩展名图标映射：根据 mimeType/extension 返回对应的 lucide 图标名
 */
function getAttachmentIcon(att: KnowledgeArticleAttachmentDto): typeof FileIcon {
  if (att.isImage) return Image;
  if (att.isPdf) return FileType;
  return FileIcon;
}

type Tab = 'articles' | 'categories';

// 分页组件
function Pagination({ currentPage, totalPages, totalItems, onPageChange }: {
  currentPage: number;
  totalPages: number;
  totalItems: number;
  onPageChange: (page: number) => void;
}) {
  const { t } = useI18n();

  if (totalPages <= 1) return null;

  const getVisiblePages = (): number[] => {
    const pages: number[] = [];
    const maxVisible = 5;
    let start = Math.max(1, currentPage - Math.floor(maxVisible / 2));
    const end = Math.min(totalPages, start + maxVisible - 1);
    if (end - start + 1 < maxVisible) {
      start = Math.max(1, end - maxVisible + 1);
    }
    for (let i = start; i <= end; i++) pages.push(i);
    return pages;
  };

  return (
    <div className="flex items-center justify-between px-4 py-3 border-t border-slate-100 dark:border-slate-700/70 text-sm bg-slate-50/50 dark:bg-slate-900/30">
      <span className="text-slate-500 dark:text-slate-400">
        {t('knowledge.common.totalBefore', { count: totalItems })}<span className="font-semibold text-slate-700 dark:text-slate-200">{totalItems}</span>{t('knowledge.common.paginationTotalBetween', { count: totalItems })}<span className="font-semibold text-slate-700 dark:text-slate-200">{currentPage}</span>{t('knowledge.common.paginationTotalAfter', { totalPages })}
      </span>
      <div className="flex items-center gap-1.5">
        <button
          onClick={() => onPageChange(currentPage - 1)}
          disabled={currentPage <= 1}
          className="flex items-center justify-center w-8 h-8 rounded-lg border border-slate-200 dark:border-slate-600 disabled:opacity-40 disabled:cursor-not-allowed hover:bg-white dark:hover:bg-slate-700 hover:border-blue-400 dark:hover:border-blue-500 hover:text-blue-600 dark:hover:text-blue-400 transition-all"
          aria-label={t('knowledge.common.previousPage')}
        >
          <ChevronLeft size={16} />
        </button>
        {getVisiblePages().map((p) => (
          <button
            key={p}
            onClick={() => onPageChange(p)}
            className={`min-w-[32px] h-8 px-2 rounded-lg text-sm font-medium transition-all ${
              p === currentPage
                ? 'bg-gradient-to-br from-blue-600 to-indigo-600 text-white shadow-md shadow-blue-500/20'
                : 'border border-slate-200 dark:border-slate-600 text-slate-600 dark:text-slate-300 hover:bg-white dark:hover:bg-slate-700 hover:border-blue-400 dark:hover:border-blue-500 hover:text-blue-600 dark:hover:text-blue-400'
            }`}
          >
            {p}
          </button>
        ))}
        <button
          onClick={() => onPageChange(currentPage + 1)}
          disabled={currentPage >= totalPages}
          className="flex items-center justify-center w-8 h-8 rounded-lg border border-slate-200 dark:border-slate-600 disabled:opacity-40 disabled:cursor-not-allowed hover:bg-white dark:hover:bg-slate-700 hover:border-blue-400 dark:hover:border-blue-500 hover:text-blue-600 dark:hover:text-blue-400 transition-all"
          aria-label={t('knowledge.common.nextPage')}
        >
          <ChevronRight size={16} />
        </button>
      </div>
    </div>
  );
}

export default function Knowledge() {
  const { user } = useAuth();
  const { t, language } = useI18n();

  /**
   * 文章状态标签：后端 `statusLabel` 是中文，英文界面下直接渲染会漏出中文，
   * 故按稳定 code 取前端字典；字典未覆盖的码回落后端原值。
   */
  const articleStatusLabel = (status: string, apiLabel: string) => {
    const key = STATUS_LABEL_KEYS[status];
    return key ? t(key) : apiLabel;
  };

  const isSuperAdmin = user ? isAdmin(user.role) : false;
  // 全局搜索跳转支持：/knowledge?article=<id> 时自动打开对应文章
  const [searchParams, setSearchParams] = useSearchParams();

  const [tab, setTab] = useState<Tab>('articles');

  // 文章相关
  const [articleQuery, setArticleQuery] = useState<KnowledgeArticleQuery>({
    page: 1,
    pageSize: 20,
  });
  const [articles, setArticles] = useState<{
    data: KnowledgeArticleDto[];
    currentPage: number;
    totalPages: number;
    totalItems: number;
    pageSize: number;
  } | null>(null);
  const [overview, setOverview] = useState<KnowledgeOverviewResponse | null>(null);
  const [loadingArticles, setLoadingArticles] = useState(false);
  const [refreshLock, setRefreshLock] = useState(false);
  const isRefreshing = loadingArticles || refreshLock;

  // 分类相关
  const [categories, setCategories] = useState<KnowledgeCategoryDto[]>([]);
  const [loadingCategories, setLoadingCategories] = useState(false);
  const [expandedCategoryIds, setExpandedCategoryIds] = useState<Set<number>>(new Set());

  // 编辑器
  const [editingArticle, setEditingArticle] = useState<KnowledgeArticleDto | null>(null);
  const [showArticleEditor, setShowArticleEditor] = useState(false);
  const [articleForm, setArticleForm] = useState<ArticleForm>(EMPTY_ARTICLE_FORM);
  const [savingArticle, setSavingArticle] = useState(false);
  const [articleFormError, setArticleFormError] = useState<string | null>(null);
  const [editorMode, setEditorMode] = useState<'edit' | 'preview'>('edit');

  // 文章附件管理（编辑器内）
  const [editorAttachments, setEditorAttachments] = useState<KnowledgeArticleAttachmentDto[]>([]);
  const [uploadingAttachment, setUploadingAttachment] = useState(false);
  const [attachmentError, setAttachmentError] = useState<string | null>(null);
  const attachmentFileInputRef = useRef<HTMLInputElement>(null);
  // PDF 预览（文章详情）
  const [previewingAttachment, setPreviewingAttachment] = useState<KnowledgeArticleAttachmentDto | null>(null);
  const [attachmentBlobUrl, setAttachmentBlobUrl] = useState<string | null>(null);
  const [loadingAttachmentPreview, setLoadingAttachmentPreview] = useState(false);

  const [editingCategory, setEditingCategory] = useState<KnowledgeCategoryDto | null>(null);
  const [showCategoryEditor, setShowCategoryEditor] = useState(false);
  const [categoryForm, setCategoryForm] = useState<CategoryForm>(EMPTY_CATEGORY_FORM);
  const [savingCategory, setSavingCategory] = useState(false);
  const [categoryFormError, setCategoryFormError] = useState<string | null>(null);

  // 删除确认
  const [deleteArticleTarget, setDeleteArticleTarget] = useState<KnowledgeArticleDto | null>(null);
  const [deleteCategoryTarget, setDeleteCategoryTarget] = useState<KnowledgeCategoryDto | null>(null);
  const [deleting, setDeleting] = useState(false);

  // 文章查看
  const [viewArticle, setViewArticle] = useState<KnowledgeArticleDto | null>(null);

  // 邮箱列表
  const { mailboxes } = useMailboxes();

  const { message: toast, show: showToast } = useTimedToast();

  // 垃圾箱
  const [showTrash, setShowTrash] = useState(false);
  const [trashArticles, setTrashArticles] = useState<{
    data: KnowledgeArticleDto[];
    currentPage: number;
    totalPages: number;
    totalItems: number;
    pageSize: number;
  } | null>(null);
  const [trashCategories, setTrashCategories] = useState<{
    data: KnowledgeCategoryDto[];
    currentPage: number;
    totalPages: number;
    totalItems: number;
    pageSize: number;
  } | null>(null);
  const [loadingTrash, setLoadingTrash] = useState(false);
  const [trashKeyword, setTrashKeyword] = useState('');
  const [trashPage, setTrashPage] = useState(1);
  const [restoreArticleTarget, setRestoreArticleTarget] = useState<KnowledgeArticleDto | null>(null);
  const [restoreCategoryTarget, setRestoreCategoryTarget] = useState<KnowledgeCategoryDto | null>(null);
  const [hardDeleteArticleTarget, setHardDeleteArticleTarget] = useState<KnowledgeArticleDto | null>(null);
  const [hardDeleteCategoryTarget, setHardDeleteCategoryTarget] = useState<KnowledgeCategoryDto | null>(null);
  const [clearTrashTarget, setClearTrashTarget] = useState<'articles' | 'categories' | null>(null);

  // 搜索防抖
  const searchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  // 文章编辑器 textarea 引用，供 Markdown 工具栏插入操作使用
  const articleContentRef = useRef<HTMLTextAreaElement>(null);
  // 自动保存草稿定时器
  const autoSaveTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const [searchInput, setSearchInput] = useState('');

  // ====== 全局 FTS5 搜索（页头搜索框 + 下拉结果弹层）======
  const [globalSearchInput, setGlobalSearchInput] = useState('');
  const [globalSearchOpen, setGlobalSearchOpen] = useState(false);
  const [globalSearchLoading, setGlobalSearchLoading] = useState(false);
  const [globalSearchResult, setGlobalSearchResult] = useState<KnowledgeSearchResult | null>(null);
  const globalSearchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const globalSearchBoxRef = useRef<HTMLDivElement | null>(null);
  // 点击弹层外部关闭
  useEffect(() => {
    const onDocClick = (e: MouseEvent) => {
      if (globalSearchBoxRef.current && !globalSearchBoxRef.current.contains(e.target as Node)) {
        setGlobalSearchOpen(false);
      }
    };
    document.addEventListener('mousedown', onDocClick);
    return () => document.removeEventListener('mousedown', onDocClick);
  }, []);
  // KB-005: 请求序列号防止竞态——快速输入时旧请求可能晚于新请求返回，
  // 若不加以保护会导致旧结果覆盖新结果（表现为"打字快时结果闪烁回退"）
  const globalSearchSeqRef = useRef(0);
  const trashArticleSeqRef = useRef(0);
  const trashCategorySeqRef = useRef(0);

  // 搜索请求：使用 /api/knowledge/search (FTS5 snippet)
  const runGlobalSearch = useCallback(async (kw: string, page = 1) => {
    const keyword = kw.trim();
    if (!keyword) {
      setGlobalSearchResult(null);
      setGlobalSearchLoading(false);
      return;
    }
    // KB-005: 递增序列号，仅接受最新一次请求的响应
    const seq = ++globalSearchSeqRef.current;
    setGlobalSearchLoading(true);
    try {
      const res = await knowledgeApi.search(keyword, page, 8);
      if (seq !== globalSearchSeqRef.current) return; // 已被后续请求取代，丢弃
      setGlobalSearchResult(res);
    } catch (e) {
      if (seq !== globalSearchSeqRef.current) return;
      showToast({ type: 'error', text: extractErrorMessage(e, t('knowledge.search.searchFailed')) });
    } finally {
      if (seq === globalSearchSeqRef.current) setGlobalSearchLoading(false);
    }
  }, [showToast, t]);
  const handleGlobalSearchChange = (val: string) => {
    setGlobalSearchInput(val);
    setGlobalSearchOpen(true);
    if (globalSearchTimerRef.current) clearTimeout(globalSearchTimerRef.current);
    if (!val.trim()) {
      setGlobalSearchResult(null);
      setGlobalSearchLoading(false);
      return;
    }
    globalSearchTimerRef.current = setTimeout(() => {
      void runGlobalSearch(val, 1);
    }, 250);
  };
  // snippet 高亮：后端返回 [[H]]...[[/H]] 标记，前端渲染为 <mark>
  const renderSnippet = (snip?: string | null): string => {
    if (!snip) return '';
    const esc = (s: string) => s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
    const escaped = esc(snip);
    const highlight = escaped
      .replace(/\[\[H\]\]/g, '<mark class="bg-yellow-200 dark:bg-yellow-500/40 text-slate-900 dark:text-slate-100 px-0.5 rounded">')
      .replace(/\[\[\/H\]\]/g, '</mark>');
    return DOMPurify.sanitize(highlight, { ADD_TAGS: ['mark'] });
  };

  // 草稿冲突提示：编辑已有文章时检测本地草稿与服务器版本新旧
  const [draftConflict, setDraftConflict] = useState<null | {
    draftSavedAt: number;
    serverUpdatedAt: string;
    form: ArticleForm;
  }>(null);

  // 组件卸载时清理所有防抖定时器，避免内存泄漏
  useEffect(() => {
    return () => {
      if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
      if (trashSearchTimerRef.current) clearTimeout(trashSearchTimerRef.current);
      if (autoSaveTimerRef.current) clearTimeout(autoSaveTimerRef.current);
      if (globalSearchTimerRef.current) clearTimeout(globalSearchTimerRef.current);
    };
  }, []);

  // 加载文章列表
  useEffect(() => {
    let cancelled = false;
    setLoadingArticles(true);
    knowledgeApi
      .listArticles(articleQuery)
      .then((res) => { if (!cancelled) setArticles(res); })
      .catch((e: unknown) => { if (!cancelled) showToast({ type: 'error', text: extractErrorMessage(e, t('knowledge.list.loadArticlesFailed')) }); })
      .finally(() => { if (!cancelled) setLoadingArticles(false); });
    return () => { cancelled = true; };
  }, [articleQuery, showToast, t]);

  const refreshOverview = useCallback(() => {
    knowledgeApi.overview().then(setOverview).catch((e: unknown) => { console.error('Knowledge overview failed:', e); });
  }, []);

  const refreshCategories = useCallback(() => {
    setLoadingCategories(true);
    knowledgeApi
      .listCategories()
      .then((res) => {
        // listCategories 已在 API 层规范化为 KnowledgeCategoryDto[]，做一次防御性校验
        const data = Array.isArray(res) ? res : [];
        setCategories(data);
        setExpandedCategoryIds(new Set(data.map((c) => c.id)));
      })
      .catch((e: unknown) => showToast({ type: 'error', text: extractErrorMessage(e, t('knowledge.categories.loadCategoriesFailed')) }))
      .finally(() => setLoadingCategories(false));
  }, [showToast, t]);

  // 初始化
  useEffect(() => {
    refreshOverview();
    refreshCategories();
  }, [refreshOverview, refreshCategories]);

  const handleRefresh = useCallback(() => {
    if (isRefreshing) return;
    setRefreshLock(true);
    try {
      refreshOverview();
      refreshCategories();
      setArticleQuery((q) => ({ ...q, page: 1 }));
    } finally {
      setTimeout(() => setRefreshLock(false), 400);
    }
  }, [isRefreshing, refreshOverview, refreshCategories]);

  // 扁平化分类树（全量，含禁用——分类管理「父分类选择器」使用，支持将禁用分类作为父节点）
  const flatCategories = useMemo(() => {
    const result: { id: number; name: string; depth: number; isActive: boolean }[] = [];
    const walk = (list: KnowledgeCategoryDto[], depth = 0) => {
      for (const c of list) {
        result.push({ id: c.id, name: c.name, depth, isActive: c.isActive });
        if (c.children && c.children.length > 0) walk(c.children, depth + 1);
      }
    };
    walk(categories);
    return result;
  }, [categories]);

  // 扁平化分类树（仅启用——文章筛选器 & 文章创建/编辑的分类选择器使用）
  // 说明：① 父分类被禁用时，子分类一并隐藏，避免子分类悬空；② 作为前端兜底，后端调用时也传 isActive=true。
  const activeFlatCategories = useMemo(() => {
    const result: { id: number; name: string; depth: number }[] = [];
    const walk = (list: KnowledgeCategoryDto[], depth = 0) => {
      for (const c of list) {
        if (!c.isActive) continue;
        result.push({ id: c.id, name: c.name, depth });
        if (c.children && c.children.length > 0) walk(c.children, depth + 1);
      }
    };
    walk(categories);
    return result;
  }, [categories]);

  // 切换分类展开
  const toggleCategoryExpand = (id: number) => {
    setExpandedCategoryIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  };

  // 收集所有有子分类的节点 ID（用于全部展开/收起）
  const allCategoryIdsWithChildren = useMemo(() => {
    const ids: number[] = [];
    const walk = (list: KnowledgeCategoryDto[]) => {
      for (const c of list) {
        if (c.children && c.children.length > 0) {
          ids.push(c.id);
          walk(c.children);
        }
      }
    };
    walk(categories);
    return ids;
  }, [categories]);

  const allCategoriesExpanded =
    allCategoryIdsWithChildren.length > 0 &&
    allCategoryIdsWithChildren.every((id) => expandedCategoryIds.has(id));

  const toggleAllCategoriesExpand = () => {
    if (allCategoriesExpanded) {
      // 全部收起
      setExpandedCategoryIds(new Set());
    } else {
      // 全部展开（包含所有有子节点的分类）
      setExpandedCategoryIds(new Set(allCategoryIdsWithChildren));
    }
  };

  // 搜索防抖处理
  const handleSearchChange = (value: string) => {
    setSearchInput(value);
    if (searchTimerRef.current) clearTimeout(searchTimerRef.current);
    searchTimerRef.current = setTimeout(() => {
      setArticleQuery((q) => ({ ...q, keyword: value || null, page: 1 }));
    }, 350);
  };

  // 垃圾箱搜索防抖处理
  const trashSearchTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const handleTrashSearchChange = (value: string, type: 'articles' | 'categories') => {
    setTrashKeyword(value);
    setTrashPage(1);
    if (trashSearchTimerRef.current) clearTimeout(trashSearchTimerRef.current);
    trashSearchTimerRef.current = setTimeout(() => {
      if (type === 'articles') refreshTrashArticles(1, value);
      else refreshTrashCategories(1, value);
    }, 350);
  };

  const handleArticleFilterChange = (key: keyof KnowledgeArticleQuery, value: string | number | boolean | null) => {
    setArticleQuery((q) => ({ ...q, [key]: value || null, page: 1 }));
  };

  const handleArticlePageChange = (page: number) => {
    setArticleQuery((q) => ({ ...q, page }));
  };

  // ====== 文章草稿自动保存 ======
  const getDraftKey = useCallback((): string | null => {
    if (!user?.id) return null;
    // 编辑已有文章用文章 ID，新建文章用 'new'
    const suffix = editingArticle ? `edit_${editingArticle.id}` : 'new';
    return `noneadcss_kb_draft_u${user.id}_${suffix}`;
  }, [editingArticle, user]);

  // 自动保存草稿到 localStorage（防抖 2 秒）
  // T000103 修复：草稿同时携带 editorAttachments，避免取消后重新打开编辑页附件丢失
  useEffect(() => {
    if (!showArticleEditor) return;
    // 只要标题/内容或附件有值就保存草稿（新建文章无法上传附件时仍按原条件）
    if (!articleForm.title.trim() && !articleForm.content.trim() && editorAttachments.length === 0) return;
    const key = getDraftKey();
    if (!key) return;
    if (autoSaveTimerRef.current) clearTimeout(autoSaveTimerRef.current);
    autoSaveTimerRef.current = setTimeout(() => {
      try {
        localStorage.setItem(
          key,
          JSON.stringify({
            form: articleForm,
            attachments: editorAttachments,
            savedAt: Date.now(),
          })
        );
      } catch {
        // localStorage 满或不可用，静默失败
      }
    }, 2000);
  }, [articleForm, editorAttachments, showArticleEditor, getDraftKey]);

  // ====== 文章操作 ======
  const openCreateArticle = () => {
    setEditingArticle(null);
    setArticleForm(EMPTY_ARTICLE_FORM);
    setArticleFormError(null);
    setEditorMode('edit');
    setEditorAttachments([]);
    setAttachmentError(null);
    setDraftConflict(null);
    // 尝试恢复未保存的新建草稿
    const uid = user?.id;
    if (uid) {
      try {
        const raw = localStorage.getItem(`noneadcss_kb_draft_u${uid}_new`);
        if (raw) {
          const draft = JSON.parse(raw) as { form: ArticleForm; attachments?: KnowledgeArticleAttachmentDto[]; savedAt: number };
          let hasPayload = false;
          if (draft.form && (draft.form.title.trim() || draft.form.content.trim())) {
            setArticleForm(draft.form);
            hasPayload = true;
          }
          if (Array.isArray(draft.attachments) && draft.attachments.length > 0) {
            setEditorAttachments(draft.attachments);
            hasPayload = true;
          }
          if (hasPayload) {
            showToast({ type: 'success', text: t('knowledge.draft.restoredUnsaved', { date: formatDateTime(new Date(draft.savedAt).toISOString()) }) });
          }
        }
      } catch {
        // 草稿解析失败，静默忽略
      }
    }
    setShowArticleEditor(true);
  };

  // ====== 文章编辑入口（列表行编辑按钮 / 查看详情页编辑按钮）======
  // T000103 修复：不再复用列表页 stale DTO，改为拉取详情接口，保证 attachments 与 查看页一致。
  // 同时恢复本地草稿附件（对未保存的上传附件做"已存在"去重合并）。
  const openEditArticle = async (a: KnowledgeArticleDto) => {
    // 1) 优先拉取后端最新详情（含 attachments），失败则回退到传入对象
    let latest: KnowledgeArticleDto = a;
    try {
      latest = await knowledgeApi.getArticle(a.id, false);
    } catch {
      // 静默回退到传入对象（例如网络波动）
    }
    setEditingArticle(latest);
    const serverForm: ArticleForm = {
      title: latest.title,
      content: latest.content,
      contentType: latest.contentType || 'markdown',
      summary: latest.summary ?? '',
      categoryId: latest.categoryId,
      tags: latest.tags ?? '',
      status: latest.status,
      sortOrder: latest.sortOrder,
      isPinned: latest.isPinned,
    };
    setArticleForm(serverForm);
    setArticleFormError(null);
    setEditorMode('edit');
    setAttachmentError(null);
    setDraftConflict(null);

    const baseAttachments = latest.attachments ?? [];
    let finalAttachments: KnowledgeArticleAttachmentDto[] = baseAttachments;

    // 若本地存在该文章草稿，与服务器对比：
    // - 草稿更"新"：弹窗提示恢复 / 丢弃
    // - 否则用服务器版本，并清除该草稿（避免重复保留）
    // T000103 补充：草稿中若存在 server 尚未包含的附件（上传后未保存），合并进 editorAttachments
    const uid = user?.id;
    if (uid) {
      try {
        const key = `noneadcss_kb_draft_u${uid}_edit_${latest.id}`;
        const raw = localStorage.getItem(key);
        if (raw) {
          const draft = JSON.parse(raw) as {
            form: ArticleForm;
            attachments?: KnowledgeArticleAttachmentDto[];
            savedAt: number;
          };
          const hasValidForm = draft?.form && (draft.form.title.trim() || draft.form.content.trim());
          const serverMs = latest.updatedAt ? new Date(latest.updatedAt).getTime() : 0;
          const draftMs = Number.isFinite(draft.savedAt) ? draft.savedAt : 0;

          // 附件合并：按 id 去重（草稿优先），保证 editor 中不漏掉"上传后未保存"的附件
          const draftAttachments = Array.isArray(draft.attachments) ? draft.attachments : [];
          if (draftAttachments.length > 0) {
            const byId = new Map<number | string, KnowledgeArticleAttachmentDto>();
            for (const att of baseAttachments) {
              if (att.id != null) byId.set(att.id, att);
            }
            for (const att of draftAttachments) {
              const key_ = att.id ?? `${att.fileName}-${att.url}-${att.fileSize}`;
              byId.set(key_, att);
            }
            finalAttachments = Array.from(byId.values());
          }

          const hasFormDiff =
            draft.form &&
            (draft.form.title !== serverForm.title ||
              draft.form.content !== serverForm.content ||
              draft.form.summary !== serverForm.summary ||
              draft.form.tags !== serverForm.tags ||
              draft.form.status !== serverForm.status ||
              draft.form.categoryId !== serverForm.categoryId ||
              draft.form.contentType !== serverForm.contentType ||
              draft.form.sortOrder !== serverForm.sortOrder ||
              draft.form.isPinned !== serverForm.isPinned);

          if (hasValidForm && hasFormDiff && draftMs >= serverMs) {
            setDraftConflict({
              draftSavedAt: draftMs,
              serverUpdatedAt: latest.updatedAt,
              form: draft.form,
            });
          } else if (!hasFormDiff) {
            // 表单完全一致：只保留附件合并结果，清理旧草稿（下次以服务端为准）
            try { localStorage.removeItem(key); } catch { /* ignore */ }
          }
        }
      } catch {
        // ignore
      }
    }

    setEditorAttachments(finalAttachments);
    setShowArticleEditor(true);
  };

  // 在编辑器光标处插入文本（用于粘贴图片上传后的 Markdown 链接插入）
  const insertTextAtCursor = useCallback((text: string) => {
    const ta = articleContentRef.current;
    if (!ta) {
      setArticleForm((f) => ({ ...f, content: f.content + text }));
      return;
    }
    const start = ta.selectionStart ?? ta.value.length;
    const end = ta.selectionEnd ?? ta.value.length;
    const newValue = ta.value.slice(0, start) + text + ta.value.slice(end);
    setArticleForm((f) => ({ ...f, content: newValue }));
    // 在浏览器下次渲染完成后，把光标挪到插入位置之后
    requestAnimationFrame(() => {
      const cur = articleContentRef.current;
      if (!cur) return;
      const pos = start + text.length;
      try {
        cur.focus();
        cur.setSelectionRange(pos, pos);
      } catch { /* ignore */ }
    });
  }, []);

  // 编辑器粘贴图片：自动上传并插入 Markdown 链接
  const handleEditorPaste = useCallback(async (e: React.ClipboardEvent<HTMLTextAreaElement>) => {
    if (articleForm.contentType !== 'markdown') return; // HTML 模式下暂不接管
    const items = e.clipboardData?.items;
    if (!items) return;
    const images: File[] = [];
    for (let i = 0; i < items.length; i++) {
      const it = items[i];
      if (it.kind === 'file' && it.type?.startsWith('image/')) {
        const f = it.getAsFile();
        if (f) images.push(f);
      }
    }
    if (images.length === 0) return;

    e.preventDefault();
    for (const img of images) {
      // 大小/类型安全校验
      const ext = (img.name.split('.').pop() ?? '').toLowerCase();
      const allowedExt = ['png', 'jpg', 'jpeg', 'gif', 'webp', 'svg', 'bmp'];
      if (ext && !allowedExt.includes(ext)) {
        showToast({ type: 'error', text: t('knowledge.attachments.unsupportedImage', { ext }) });
        continue;
      }
      if (img.size > ATTACHMENT_MAX_SIZE) {
        showToast({ type: 'error', text: t('knowledge.attachments.imageTooLarge', { size: (img.size / 1024 / 1024).toFixed(2), limit: 25 }) });
        continue;
      }

      // 新建文章尚未保存：转为 data-url 内联图片嵌入草稿，避免依赖 articleId
      if (!editingArticle?.id) {
        const reader = new FileReader();
        reader.onload = () => {
          const dataUrl = typeof reader.result === 'string' ? reader.result : '';
          if (!dataUrl) return;
          const safeName = (img.name || 'image').replace(/"/g, '');
          insertTextAtCursor(`\n![${safeName}](${dataUrl})\n`);
          showToast({ type: 'info', text: t('knowledge.attachments.pastedInline') });
        };
        reader.onerror = () => showToast({ type: 'error', text: t('knowledge.attachments.clipboardReadFailed') });
        reader.readAsDataURL(img);
        continue;
      }

      // 已有文章：作为附件上传
      try {
        setUploadingAttachment(true);
        const att = await knowledgeApi.uploadAttachment(editingArticle.id, img);
        const url = `/api/knowledge/articles/${editingArticle.id}/attachments/${att.id}/download`;
        const safeName = (att.fileName || 'image').replace(/"/g, '');
        insertTextAtCursor(`\n![${safeName}](${url})\n`);
        setEditorAttachments((xs) => [att, ...xs]);
        showToast({ type: 'success', text: t('knowledge.attachments.imageUploaded') });
      } catch (err) {
        showToast({ type: 'error', text: extractErrorMessage(err, t('knowledge.attachments.imageUploadFailed')) });
      } finally {
        setUploadingAttachment(false);
      }
    }
  }, [articleForm.contentType, editingArticle, insertTextAtCursor, showToast, t]);

  // Ctrl+S / Cmd+S 快捷键保存文章
  const handleArticleEditorKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if ((e.ctrlKey || e.metaKey) && e.key === 's') {
      e.preventDefault();
      handleSaveArticle();
    }
  };

  const handleSaveArticle = async () => {
    setArticleFormError(null);
    if (!articleForm.title.trim()) {
      setArticleFormError(t('knowledge.editor.titleRequired'));
      return;
    }
    if (!articleForm.content.trim()) {
      setArticleFormError(t('knowledge.editor.contentRequired'));
      return;
    }
    if (articleForm.content.length > MAX_ARTICLE_CONTENT) {
      setArticleFormError(t('knowledge.editor.contentTooLong', { current: formatCount(articleForm.content.length, language), max: formatCount(MAX_ARTICLE_CONTENT, language) }));
      return;
    }
    setSavingArticle(true);
    try {
      if (editingArticle) {
        const data: UpdateKnowledgeArticleRequest = {
          id: editingArticle.id,
          title: articleForm.title.trim(),
          content: articleForm.content,
          contentType: articleForm.contentType,
          summary: articleForm.summary || null,
          categoryId: articleForm.categoryId,
          authorId: editingArticle.authorId,
          tags: articleForm.tags || null,
          status: articleForm.status,
          sortOrder: articleForm.sortOrder,
          isPinned: articleForm.isPinned,
          // KB-003 修复：携带乐观锁令牌，防止多客服同时编辑导致 lost update
          // 后端 ConcurrencyCheckHelper.Check 会比对 rowVersion，不一致时返回 409 + latest 快照
          ifMatch: editingArticle.rowVersion ?? null,
        };
        await knowledgeApi.updateArticle(editingArticle.id, data);
        showToast({ type: 'success', text: t('knowledge.editor.updated') });
      } else {
        const data: CreateKnowledgeArticleRequest = {
          title: articleForm.title.trim(),
          content: articleForm.content,
          contentType: articleForm.contentType,
          summary: articleForm.summary || null,
          categoryId: articleForm.categoryId,
          authorId: null,
          tags: articleForm.tags || null,
          status: articleForm.status,
          sortOrder: articleForm.sortOrder,
          isPinned: articleForm.isPinned,
        };
        await knowledgeApi.createArticle(data);
        showToast({ type: 'success', text: t('knowledge.editor.created') });
        // 新建文章保存成功后，若编辑器中有已上传的附件，需要迁移到新文章 ID 下
        // 但附件上传要求文章已存在（articleId），因此新建模式下附件需在文章保存后才能上传
        // 这里清空编辑器附件列表，因为新文章已创建
        if (editorAttachments.length > 0) {
          setEditorAttachments([]);
        }
      }
      // 保存成功后清除草稿
      const draftKey = getDraftKey();
      if (draftKey) localStorage.removeItem(draftKey);
      setShowArticleEditor(false);
      refreshOverview();
      setArticleQuery((q) => ({ ...q, page: 1 }));
    } catch (e: unknown) {
      setArticleFormError(extractErrorMessage(e, t('knowledge.common.saveFailed')));
    } finally {
      setSavingArticle(false);
    }
  };

  const handleViewArticle = async (a: KnowledgeArticleDto) => {
    try {
      const detail = await knowledgeApi.getArticle(a.id, true);
      setViewArticle(detail);
    } catch {
      setViewArticle(a);
    }
  };

  // 全局搜索跳转：/knowledge?article=<id> 在挂载时自动打开对应文章，
  // 打开后立即清理 URL 参数，避免刷新/后退时重复弹出
  const handleViewArticleRef = useRef(handleViewArticle);
  handleViewArticleRef.current = handleViewArticle;
  useEffect(() => {
    const raw = searchParams.get('article');
    const id = raw ? Number(raw) : NaN;
    if (!Number.isInteger(id) || id <= 0) return;
    setSearchParams({}, { replace: true });
    void handleViewArticleRef.current({ id } as KnowledgeArticleDto);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleLikeArticle = async (a: KnowledgeArticleDto) => {
    try {
      // T000101: 后端为 toggle 语义——已点赞则取消（liked=false），未点赞则点赞（liked=true）
      const res = await knowledgeApi.likeArticle(a.id);
      if (res && typeof res === 'object' && 'success' in res && !res.success) {
        showToast({ type: 'error', text: res.message || t('knowledge.common.actionFailed') });
        return;
      }
      const nowLiked = Boolean(res && 'liked' in res ? res.liked : true);
      const delta = nowLiked ? 1 : -1;
      // 刷新当前查看的文章（点赞状态 + 计数）
      if (viewArticle?.id === a.id) {
        setViewArticle({
          ...viewArticle,
          likeCount: Math.max(0, viewArticle.likeCount + delta),
          isLiked: nowLiked,
        });
      }
      // 同步更新文章列表中的点赞数与状态，避免列表与详情不一致
      setArticles((prev) => {
        if (!prev) return prev;
        return {
          ...prev,
          data: prev.data.map((item) =>
            item.id === a.id
              ? { ...item, likeCount: Math.max(0, item.likeCount + delta), isLiked: nowLiked }
              : item
          ),
        };
      });
      showToast({ type: 'success', text: nowLiked ? t('knowledge.view.likeSucceeded') : t('knowledge.view.unlikeSucceeded') });
    } catch (e: unknown) {
      showToast({ type: 'error', text: extractErrorMessage(e, t('knowledge.common.actionFailed')) });
    }
  };

  const handleDeleteArticle = async () => {
    if (!deleteArticleTarget) return;
    setDeleting(true);
    try {
      await knowledgeApi.removeArticle(deleteArticleTarget.id);
      showToast({ type: 'success', text: t('knowledge.view.articleDeleted') });
      setDeleteArticleTarget(null);
      refreshOverview();
      setArticleQuery((q) => ({ ...q }));
    } catch {
      showToast({ type: 'error', text: t('knowledge.view.deleteFailed') });
    } finally {
      setDeleting(false);
    }
  };

  // ====== 分类操作 ======
  const openCreateCategory = (parentId: number | null = null) => {
    setEditingCategory(null);
    setCategoryForm({ ...EMPTY_CATEGORY_FORM, parentId });
    setCategoryFormError(null);
    setShowCategoryEditor(true);
  };

  const openEditCategory = (c: KnowledgeCategoryDto) => {
    setEditingCategory(c);
    setCategoryForm({
      name: c.name,
      description: c.description ?? '',
      parentId: c.parentId,
      mailboxId: c.mailboxId,
      sortOrder: c.sortOrder,
      icon: c.icon ?? '',
      isActive: c.isActive,
    });
    setCategoryFormError(null);
    setShowCategoryEditor(true);
  };

  const handleSaveCategory = async () => {
    setCategoryFormError(null);
    if (!categoryForm.name.trim()) {
      setCategoryFormError(t('knowledge.categories.nameRequired'));
      return;
    }
    setSavingCategory(true);
    try {
      if (editingCategory) {
        const data: UpdateKnowledgeCategoryRequest = {
          id: editingCategory.id,
          name: categoryForm.name.trim(),
          description: categoryForm.description || null,
          parentId: categoryForm.parentId,
          mailboxId: categoryForm.mailboxId,
          sortOrder: categoryForm.sortOrder,
          icon: categoryForm.icon || null,
          isActive: categoryForm.isActive,
        };
        await knowledgeApi.updateCategory(editingCategory.id, data);
        showToast({ type: 'success', text: t('knowledge.categories.updated') });
      } else {
        const data: CreateKnowledgeCategoryRequest = {
          name: categoryForm.name.trim(),
          description: categoryForm.description || null,
          parentId: categoryForm.parentId,
          mailboxId: categoryForm.mailboxId,
          sortOrder: categoryForm.sortOrder,
          icon: categoryForm.icon || null,
          isActive: categoryForm.isActive,
        };
        await knowledgeApi.createCategory(data);
        showToast({ type: 'success', text: t('knowledge.categories.created') });
      }
      setShowCategoryEditor(false);
      refreshCategories();
      refreshOverview();
    } catch (e: unknown) {
      setCategoryFormError(extractErrorMessage(e, t('knowledge.common.saveFailed')));
    } finally {
      setSavingCategory(false);
    }
  };

  const handleDeleteCategory = async () => {
    if (!deleteCategoryTarget) return;
    setDeleting(true);
    try {
      await knowledgeApi.removeCategory(deleteCategoryTarget.id);
      showToast({ type: 'success', text: t('knowledge.categories.deleted') });
      setDeleteCategoryTarget(null);
      refreshCategories();
      refreshOverview();
    } catch {
      showToast({ type: 'error', text: t('knowledge.view.deleteFailed') });
    } finally {
      setDeleting(false);
    }
  };

  // ====== 垃圾箱操作 ======
  const refreshTrashArticles = async (page = 1, keyword = '') => {
    // KB-005: 请求序列号防止竞态
    const seq = ++trashArticleSeqRef.current;
    setLoadingTrash(true);
    try {
      const res = await knowledgeApi.getArticleTrash(page, 10, keyword || undefined);
      if (seq !== trashArticleSeqRef.current) return; // 已被后续请求取代
      setTrashArticles(res);
    } catch {
      if (seq !== trashArticleSeqRef.current) return;
      showToast({ type: 'error', text: t('knowledge.trash.loadFailed') });
    } finally {
      if (seq === trashArticleSeqRef.current) setLoadingTrash(false);
    }
  };

  const refreshTrashCategories = async (page = 1, keyword = '') => {
    // KB-005: 请求序列号防止竞态
    const seq = ++trashCategorySeqRef.current;
    setLoadingTrash(true);
    try {
      const res = await knowledgeApi.getCategoryTrash(page, 10, keyword || undefined);
      if (seq !== trashCategorySeqRef.current) return; // 已被后续请求取代
      setTrashCategories(res);
    } catch {
      if (seq !== trashCategorySeqRef.current) return;
      showToast({ type: 'error', text: t('knowledge.trash.loadFailed') });
    } finally {
      if (seq === trashCategorySeqRef.current) setLoadingTrash(false);
    }
  };

  const handleRestoreArticle = async () => {
    if (!restoreArticleTarget) return;
    try {
      await knowledgeApi.restoreArticle(restoreArticleTarget.id);
      showToast({ type: 'success', text: t('knowledge.view.articleRestored') });
      setRestoreArticleTarget(null);
      refreshTrashArticles(trashPage, trashKeyword);
      refreshOverview();
      setArticleQuery((q) => ({ ...q }));
    } catch {
      showToast({ type: 'error', text: t('knowledge.view.restoreFailed') });
    }
  };

  const handleRestoreCategory = async () => {
    if (!restoreCategoryTarget) return;
    try {
      await knowledgeApi.restoreCategory(restoreCategoryTarget.id);
      showToast({ type: 'success', text: t('knowledge.categories.restored') });
      setRestoreCategoryTarget(null);
      refreshTrashCategories(trashPage, trashKeyword);
      refreshCategories();
      refreshOverview();
    } catch {
      showToast({ type: 'error', text: t('knowledge.view.restoreFailed') });
    }
  };

  const handleHardDeleteArticle = async () => {
    if (!hardDeleteArticleTarget) return;
    try {
      await knowledgeApi.hardDeleteArticle(hardDeleteArticleTarget.id);
      showToast({ type: 'success', text: t('knowledge.view.articleHardDeleted') });
      setHardDeleteArticleTarget(null);
      refreshTrashArticles(trashPage, trashKeyword);
    } catch {
      showToast({ type: 'error', text: t('knowledge.view.hardDeleteFailed') });
    }
  };

  const handleHardDeleteCategory = async () => {
    if (!hardDeleteCategoryTarget) return;
    try {
      await knowledgeApi.hardDeleteCategory(hardDeleteCategoryTarget.id);
      showToast({ type: 'success', text: t('knowledge.categories.hardDeleted') });
      setHardDeleteCategoryTarget(null);
      refreshTrashCategories(trashPage, trashKeyword);
    } catch {
      showToast({ type: 'error', text: t('knowledge.view.hardDeleteFailed') });
    }
  };

  const handleClearTrash = async () => {
    if (!clearTrashTarget) return;
    try {
      if (clearTrashTarget === 'articles') {
        const res = await knowledgeApi.clearArticleTrash();
        showToast({ type: 'success', text: t('knowledge.trash.clearedArticles', { count: res.count }) });
        setTrashArticles(null);
      } else {
        const res = await knowledgeApi.clearCategoryTrash();
        showToast({ type: 'success', text: t('knowledge.trash.clearedCategories', { count: res.count }) });
        setTrashCategories(null);
      }
      setClearTrashTarget(null);
      refreshOverview();
    } catch {
      showToast({ type: 'error', text: t('knowledge.trash.clearFailed') });
    }
  };

  // Markdown 工具栏插入
  const insertMarkdown = (before: string, after = '', placeholder = '') => {
    const textarea = articleContentRef.current;
    if (!textarea) return;
    const start = textarea.selectionStart;
    const end = textarea.selectionEnd;
    const selected = articleForm.content.substring(start, end) || placeholder;
    const newText = articleForm.content.substring(0, start) + before + selected + after + articleForm.content.substring(end);
    setArticleForm({ ...articleForm, content: newText });
    // 恢复焦点和选区
    setTimeout(() => {
      textarea.focus();
      textarea.setSelectionRange(start + before.length, start + before.length + selected.length);
    }, 0);
  };

  // ====== 文章附件操作 ======
  // 触发文件选择
  const handleAttachmentSelect = () => {
    setAttachmentError(null);
    attachmentFileInputRef.current?.click();
  };

  // 上传附件
  const handleUploadAttachment = async (file: File) => {
    if (!editingArticle) {
      setAttachmentError(t('knowledge.attachments.saveArticleFirst'));
      return;
    }
    // 客户端校验：文件名安全、扩展名白名单、大小限制
    const ext = (file.name.split('.').pop() || '').toLowerCase();
    if (!ext || !ATTACHMENT_ALLOWED_EXTENSIONS.includes(ext)) {
      setAttachmentError(t('knowledge.attachments.unsupportedType', { ext: ext || t('format.unknown'), allowed: ATTACHMENT_ALLOWED_EXTENSIONS.join(', ') }));
      return;
    }
    if (file.size > ATTACHMENT_MAX_SIZE) {
      setAttachmentError(t('knowledge.attachments.tooLarge', { current: formatAttachmentSize(file.size), max: formatAttachmentSize(ATTACHMENT_MAX_SIZE) }));
      return;
    }
    setUploadingAttachment(true);
    setAttachmentError(null);
    try {
      const att = await knowledgeApi.uploadAttachment(editingArticle.id, file);
      setEditorAttachments((prev) => [...prev, att]);
      showToast({ type: 'success', text: t('knowledge.attachments.uploaded') });
    } catch (e: unknown) {
      setAttachmentError(extractErrorMessage(e, t('knowledge.attachments.uploadFailed')));
    } finally {
      setUploadingAttachment(false);
      // 重置 input，允许重复选择同一文件
      if (attachmentFileInputRef.current) attachmentFileInputRef.current.value = '';
    }
  };

  // 删除附件
  const handleDeleteAttachment = async (attachmentId: number) => {
    if (!editingArticle) return;
    try {
      await knowledgeApi.deleteAttachment(editingArticle.id, attachmentId);
      setEditorAttachments((prev) => prev.filter((a) => a.id !== attachmentId));
      showToast({ type: 'success', text: t('knowledge.attachments.deleted') });
    } catch (e: unknown) {
      showToast({ type: 'error', text: extractErrorMessage(e, t('knowledge.attachments.deleteFailed')) });
    }
  };

  // 获取附件预览/下载的 Blob URL（使用带认证的 axios 客户端获取文件流）
  const fetchAttachmentBlobUrl = useCallback(async (att: KnowledgeArticleAttachmentDto): Promise<string | null> => {
    try {
      const url = knowledgeApi.getAttachmentDownloadUrl(att.articleId, att.id);
      // 动态导入 client，避免循环依赖
      const { default: client } = await import('../api/client');
      const response = await client.get(url, { responseType: 'blob', _skipDedup: true } as Record<string, unknown>);
      const blob = response.data as Blob;
      return URL.createObjectURL(blob);
    } catch {
      return null;
    }
  }, []);

  // 打开附件预览（PDF/图片）
  const handlePreviewAttachment = async (att: KnowledgeArticleAttachmentDto) => {
    if (!att.isPdf && !att.isImage) {
      // 非预览类型直接下载
      handleDownloadAttachment(att);
      return;
    }
    setLoadingAttachmentPreview(true);
    setPreviewingAttachment(att);
    setAttachmentBlobUrl(null);
    const blobUrl = await fetchAttachmentBlobUrl(att);
    setAttachmentBlobUrl(blobUrl);
    setLoadingAttachmentPreview(false);
  };

  // 关闭附件预览，释放 Blob URL
  const handleCloseAttachmentPreview = () => {
    if (attachmentBlobUrl) {
      URL.revokeObjectURL(attachmentBlobUrl);
      setAttachmentBlobUrl(null);
    }
    setPreviewingAttachment(null);
  };

  // 组件卸载时释放 Blob URL
  useEffect(() => {
    return () => {
      if (attachmentBlobUrl) URL.revokeObjectURL(attachmentBlobUrl);
    };
  }, [attachmentBlobUrl]);

  // 下载附件（非预览类型）
  const handleDownloadAttachment = async (att: KnowledgeArticleAttachmentDto) => {
    const blobUrl = await fetchAttachmentBlobUrl(att);
    if (!blobUrl) {
      showToast({ type: 'error', text: t('knowledge.attachments.downloadFailed') });
      return;
    }
    const a = document.createElement('a');
    a.href = blobUrl;
    a.download = att.fileName;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    // 延迟释放，确保下载已触发
    setTimeout(() => URL.revokeObjectURL(blobUrl), 1000);
  };

  // 切换内容格式（md <-> html）
  const handleContentTypeChange = (newType: string) => {
    if (newType === articleForm.contentType) return;
    setArticleForm({ ...articleForm, contentType: newType });
    // 切换格式时提示用户内容不会被转换（仅切换解析器）
    if (articleForm.content.trim()) {
      showToast({ type: 'success', text: t('knowledge.editor.contentSwitched', { format: newType === 'html' ? 'HTML' : 'Markdown' }) });
    }
  };

  // 渲染分类树
  const renderCategoryTree = (
    list: KnowledgeCategoryDto[],
    depth = 0,
  ): React.ReactNode => {
    return list.map((c) => {
      const expanded = expandedCategoryIds.has(c.id);
      const hasChildren = c.children && c.children.length > 0;
      return (
        <div key={c.id}>
          <div
            className="group relative flex items-center gap-2 px-3 py-2.5 rounded-xl hover:bg-gradient-to-r hover:from-blue-50/60 hover:to-indigo-50/40 dark:hover:from-slate-700/40 dark:hover:to-slate-700/20 cursor-pointer transition-all duration-200 hover:shadow-sm"
            style={{ paddingLeft: `${16 + depth * 24}px` }}
          >
            {/* 悬浮左侧指示条 */}
            <div className="absolute left-0 top-2 bottom-2 w-1 rounded-full bg-gradient-to-b from-blue-500 to-indigo-500 opacity-0 group-hover:opacity-100 transition-opacity duration-200" />
            {/* 缩进指示线（仅深度>0时显示） */}
            {depth > 0 && (
              <span
                className="absolute left-0 top-0 bottom-0 w-px bg-slate-200/60 dark:bg-slate-700/40"
                style={{ left: `${16 + (depth - 1) * 24 + 12}px` }}
              />
            )}
            {hasChildren ? (
              <button
                onClick={() => toggleCategoryExpand(c.id)}
                className="p-1 rounded-md text-slate-400 hover:text-blue-600 dark:hover:text-blue-400 hover:bg-blue-100 dark:hover:bg-blue-900/40 transition-all flex-shrink-0 hover:scale-110 active:scale-95"
              >
                {expanded ? <ChevronDown size={16} className="transition-transform duration-200" /> : <ChevronRight size={16} className="transition-transform duration-200" />}
              </button>
            ) : (
              <span className="w-6 flex justify-center">
                <span className="w-1.5 h-1.5 rounded-full bg-slate-300 dark:bg-slate-600" />
              </span>
            )}
            <div className={`relative w-9 h-9 rounded-xl flex items-center justify-center flex-shrink-0 transition-all duration-200 group-hover:scale-105 ${
              c.isActive
                ? 'bg-gradient-to-br from-amber-100 to-orange-100 dark:from-amber-900/40 dark:to-orange-900/40 ring-1 ring-amber-200/60 dark:ring-amber-800/40'
                : 'bg-slate-100 dark:bg-slate-700/50 ring-1 ring-slate-200/60 dark:ring-slate-600/40'
            }`}>
              <span className="text-lg">{c.icon || '📁'}</span>
            </div>
            <div className="flex-1 min-w-0">
              <div className="flex items-center gap-2">
                <span className={`text-sm font-semibold truncate transition-colors ${c.isActive ? 'text-slate-800 dark:text-slate-100 group-hover:text-blue-700 dark:group-hover:text-blue-300' : 'text-slate-400 dark:text-slate-500'}`}>
                  {c.name}
                </span>
                {!c.isActive && (
                  <span className="text-[10px] px-1.5 py-0.5 rounded-full bg-slate-200 dark:bg-slate-700 text-slate-500 dark:text-slate-400 flex-shrink-0 uppercase tracking-wide font-medium">
                    {t('knowledge.common.disabled')}
                  </span>
                )}
              </div>
              {c.description && (
                <p className="text-xs text-slate-400 dark:text-slate-500 truncate mt-0.5">
                  {c.description}
                </p>
              )}
            </div>
            <span className="text-xs text-slate-500 dark:text-slate-400 flex-shrink-0 px-2 py-1 rounded-full bg-slate-100 dark:bg-slate-700/60 group-hover:bg-blue-100 dark:group-hover:bg-blue-900/40 group-hover:text-blue-600 dark:group-hover:text-blue-300 transition-all">
              {t('knowledge.categories.articleCount', { count: c.articleCount })}
            </span>
            {isSuperAdmin && (
              <div className="opacity-0 group-hover:opacity-100 flex items-center gap-1 transition-opacity duration-200">
                <button
                  onClick={() => openCreateCategory(c.id)}
                  className="p-1.5 rounded-lg hover:bg-blue-100 dark:hover:bg-blue-900/30 text-blue-600 dark:text-blue-400 hover:scale-110 active:scale-95 transition-all"
                  title={t('knowledge.categories.addSubcategory')}
                >
                  <FolderPlus size={15} />
                </button>
                <button
                  onClick={() => openEditCategory(c)}
                  className="p-1.5 rounded-lg hover:bg-slate-200 dark:hover:bg-slate-600 text-slate-600 dark:text-slate-300 hover:scale-110 active:scale-95 transition-all"
                  title={t('knowledge.common.edit')}
                >
                  <Edit2 size={15} />
                </button>
                <button
                  onClick={() => setDeleteCategoryTarget(c)}
                  className="p-1.5 rounded-lg hover:bg-rose-100 dark:hover:bg-rose-900/30 text-rose-600 dark:text-rose-400 hover:scale-110 active:scale-95 transition-all"
                  title={t('knowledge.common.delete')}
                >
                  <Trash2 size={15} />
                </button>
              </div>
            )}
          </div>
          {hasChildren && expanded && renderCategoryTree(c.children, depth + 1)}
        </div>
      );
    });
  };

  const articleWordCount = useMemo(() => countWords(articleForm.content), [articleForm.content]);
  const articleReadTime = useMemo(() => estimateReadTime(articleForm.content), [articleForm.content]);

  return (
    <div className="space-y-4 sm:space-y-6">
      {/* 页头 */}
      <div className="relative overflow-hidden rounded-2xl bg-gradient-to-br from-blue-600 via-indigo-600 to-violet-600 p-4 sm:p-6 shadow-xl shadow-indigo-500/20">
        {/* 装饰渐变光斑 */}
        <div className="absolute -top-12 -right-12 w-40 h-40 sm:w-48 sm:h-48 bg-white/10 rounded-full blur-3xl" />
        <div className="absolute -bottom-16 -left-8 w-32 h-32 sm:w-40 sm:h-40 bg-violet-400/20 rounded-full blur-3xl" />
        {/* 装饰网格图案 */}
        <div
          className="absolute inset-0 opacity-[0.08]"
          style={{
            backgroundImage:
              'linear-gradient(to right, white 1px, transparent 1px), linear-gradient(to bottom, white 1px, transparent 1px)',
            backgroundSize: '32px 32px',
          }}
        />
        <div className="relative flex items-center justify-between flex-wrap gap-3 sm:gap-4">
          <div className="flex items-center gap-3 sm:gap-4">
            <div className="w-11 h-11 sm:w-14 sm:h-14 rounded-2xl bg-white/20 backdrop-blur-md flex items-center justify-center ring-1 ring-white/30 shadow-lg flex-shrink-0">
              <BookOpen className="w-6 h-6 sm:w-8 sm:h-8 text-white" />
            </div>
            <div className="min-w-0">
              <h1 className="text-xl sm:text-2xl lg:text-3xl font-bold text-white tracking-tight flex items-center gap-2">
                {t('knowledge.title')}
                <span className="hidden sm:inline-flex items-center gap-1 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wider bg-white/20 backdrop-blur-sm rounded-full ring-1 ring-white/20">
                  <Sparkles size={10} /> Knowledge Base
                </span>
              </h1>
              <p className="text-xs sm:text-sm text-blue-50/90 mt-1 sm:mt-1.5 flex items-center gap-1.5">
                <Sparkles className="w-3.5 h-3.5 flex-shrink-0" />
                <span className="truncate">{t('knowledge.subtitle')}</span>
              </p>
            </div>
          </div>
          <div className="flex items-center gap-2 sm:gap-3 flex-wrap w-full sm:w-auto">
            {/* 全局 FTS5 搜索框 + 下拉弹层 */}
            <div ref={globalSearchBoxRef} className="relative w-full sm:min-w-[320px] sm:w-[380px]">
              <div className="flex items-center gap-2 px-3 py-2 bg-white/15 hover:bg-white/25 backdrop-blur-md rounded-xl border border-white/20 focus-within:ring-2 focus-within:ring-white/40 focus-within:bg-white/25 transition-all">
                <Search size={15} className="text-white/80 flex-shrink-0" />
                <input
                  type="text"
                  value={globalSearchInput}
                  onFocus={() => setGlobalSearchOpen(true)}
                  onChange={(e) => handleGlobalSearchChange(e.target.value)}
                  onKeyDown={(e) => {
                    if (e.key === 'Enter') {
                      setGlobalSearchOpen(true);
                      void runGlobalSearch(globalSearchInput, 1);
                    }
                    if (e.key === 'Escape') setGlobalSearchOpen(false);
                  }}
                  placeholder={t('knowledge.search.placeholder')}
                  className="flex-1 min-w-0 px-1 py-0.5 text-sm bg-transparent text-white placeholder:text-white/70 border-0 focus:outline-none"
                />
                {globalSearchLoading ? (
                  <RefreshCw size={14} className="text-white/80 animate-spin flex-shrink-0" />
                ) : globalSearchInput ? (
                  <button
                    onClick={() => {
                      setGlobalSearchInput('');
                      setGlobalSearchResult(null);
                    }}
                    className="text-white/80 hover:text-white transition-colors flex-shrink-0"
                    aria-label={t('knowledge.common.clearSearch')}
                  >
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                      <path d="M18 6L6 18M6 6l12 12" />
                    </svg>
                  </button>
                ) : null}
              </div>
              {globalSearchOpen && (globalSearchInput || globalSearchResult) && (
                <div className="absolute z-[60] mt-2 right-0 w-full sm:min-w-[440px] sm:w-[520px] rounded-2xl bg-white dark:bg-slate-800 shadow-2xl ring-1 ring-slate-200 dark:ring-slate-700 overflow-hidden max-h-[560px] overflow-y-auto">
                  {/* KB-T000104: 下拉弹层仅作为快速预览入口；用户按下 Enter / 点击“查看全部结果”时，
                      结果会同步显示在下方主区域（Global Search Results Section），避免被页头/其它 label 遮挡 */}
                  <div className="px-4 py-2 flex items-center justify-between bg-gradient-to-r from-blue-50/80 to-indigo-50/80 dark:from-blue-900/20 dark:to-indigo-900/20 border-b border-slate-100 dark:border-slate-700 text-xs text-slate-500">
                    <span className="inline-flex items-center gap-2">
                      <Search size={12} />
                      {t('knowledge.search.previewHint')}
                      {globalSearchResult && globalSearchResult.isFts && (
                        <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded bg-emerald-100 dark:bg-emerald-900/40 text-emerald-700 dark:text-emerald-300 font-medium">FTS5</span>
                      )}
                    </span>
                    {globalSearchInput && (
                      <button
                        onClick={() => {
                          // 切换到主区域显示模式：立即在主区块渲染完整结果
                          setGlobalSearchOpen(false);
                          void runGlobalSearch(globalSearchInput, 1);
                        }}
                        className="inline-flex items-center gap-1 px-2 py-1 rounded-lg bg-white dark:bg-slate-800 border border-slate-200 dark:border-slate-700 text-blue-600 dark:text-blue-400 hover:bg-blue-50 dark:hover:bg-slate-700 font-medium transition-colors"
                      >
                        {t('knowledge.search.viewAllResults')}
                      </button>
                    )}
                  </div>
                  {!globalSearchLoading && !globalSearchResult && (
                    <div className="p-6 sm:p-8 text-center text-sm text-slate-400">
                      {t('knowledge.search.emptyPrompt')}
                    </div>
                  )}
                  {globalSearchLoading && (
                    <div className="p-6 sm:p-8 text-center text-sm text-slate-400 flex items-center justify-center gap-2">
                      <RefreshCw size={14} className="animate-spin text-blue-500" />
                      {t('knowledge.search.searching')}
                    </div>
                  )}
                  {!globalSearchLoading && globalSearchResult && (
                    <div>
                      <div className="flex items-center justify-between px-4 py-2 bg-slate-50/80 dark:bg-slate-900/40 border-b border-slate-100 dark:border-slate-700 text-xs text-slate-500">
                        <span>
                          {t('knowledge.common.totalBefore', { count: globalSearchResult.total })}<b className="text-slate-700 dark:text-slate-200">{globalSearchResult.total}</b>{t('knowledge.search.totalResultsAfter', { count: globalSearchResult.total })}
                        </span>
                        <span>{t('knowledge.search.pageLabel', { page: globalSearchResult.page })}</span>
                      </div>
                      {globalSearchResult.data.length === 0 ? (
                        <div className="p-8 text-center text-sm text-slate-400">
                          {t('knowledge.search.noMatchHint')}
                        </div>
                      ) : (
                        <ul className="divide-y divide-slate-100 dark:divide-slate-700">
                          {globalSearchResult.data.map((a: KnowledgeArticleDto) => (
                            <li key={a.id}>
                              <button
                                onClick={() => {
                                  setGlobalSearchOpen(false);
                                  void handleViewArticle(a);
                                }}
                                className="w-full text-left px-4 py-3 hover:bg-blue-50/50 dark:hover:bg-slate-700/30 transition-colors group"
                              >
                                <div className="flex items-center justify-between gap-2">
                                  <div className="font-medium text-slate-800 dark:text-slate-100 truncate group-hover:text-blue-600 dark:group-hover:text-blue-400 transition-colors">
                                    {a.title}
                                  </div>
                                  <span className={`flex-shrink-0 px-2 py-0.5 text-[11px] rounded-full font-medium ${STATUS_COLORS[a.status] || STATUS_COLORS.draft}`}>
                                    {articleStatusLabel(a.status, a.statusLabel || a.status)}
                                  </span>
                                </div>
                                <div className="mt-1 flex items-center gap-2 text-[11px] text-slate-400">
                                  {a.categoryName && (
                                    <span className="inline-flex items-center gap-1 px-1.5 py-0.5 rounded bg-slate-100 dark:bg-slate-700 text-slate-500">
                                      <Folder size={10} /> {a.categoryName}
                                    </span>
                                  )}
                                  {a.authorName && <span>{t('knowledge.list.byAuthor', { name: a.authorName })}</span>}
                                  <span>{formatDateTime(a.updatedAt)}</span>
                                </div>
                                {a.snippet ? (
                                  <p
                                    className="mt-1.5 text-xs text-slate-600 dark:text-slate-300 leading-relaxed line-clamp-3"
                                    dangerouslySetInnerHTML={{ __html: renderSnippet(a.snippet) }}
                                  />
                                ) : a.summary ? (
                                  <p className="mt-1.5 text-xs text-slate-500 dark:text-slate-400 line-clamp-3">{a.summary}</p>
                                ) : null}
                              </button>
                            </li>
                          ))}
                        </ul>
                      )}
                      {globalSearchResult.total > globalSearchResult.pageSize && (
                        <div className="p-2 border-t border-slate-100 dark:border-slate-700 flex items-center justify-end gap-1">
                          <button
                            disabled={globalSearchResult.page <= 1}
                            onClick={() => void runGlobalSearch(globalSearchInput, globalSearchResult.page - 1)}
                            className="px-2.5 py-1 text-xs rounded-lg disabled:opacity-40 hover:bg-slate-100 dark:hover:bg-slate-700 text-slate-600 dark:text-slate-300"
                          >{t('knowledge.common.previousPage')}</button>
                          <button
                            disabled={globalSearchResult.page * globalSearchResult.pageSize >= globalSearchResult.total}
                            onClick={() => void runGlobalSearch(globalSearchInput, globalSearchResult.page + 1)}
                            className="px-2.5 py-1 text-xs rounded-lg disabled:opacity-40 hover:bg-slate-100 dark:hover:bg-slate-700 text-slate-600 dark:text-slate-300"
                          >{t('knowledge.common.nextPage')}</button>
                        </div>
                      )}
                    </div>
                  )}
                </div>
              )}
            </div>
            <button
              onClick={handleRefresh}
              disabled={isRefreshing}
              className="group flex items-center gap-2 px-4 py-2.5 text-sm font-medium text-white bg-white/15 hover:bg-white/25 backdrop-blur-md rounded-xl border border-white/20 disabled:opacity-60 disabled:cursor-not-allowed active:scale-[0.98] hover:shadow-lg hover:shadow-black/10 transition-all duration-200"
            >
              <RefreshCw size={15} className={`text-white ${isRefreshing ? 'animate-spin' : 'group-hover:rotate-180'} transition-transform duration-500`} />
              {t('knowledge.common.refresh')}
            </button>
          </div>
        </div>
      </div>

      {/* 概览卡片 */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3 sm:gap-4">
        <OverviewCard
          label={t('knowledge.list.overview.totalArticles')}
          value={overview?.stats?.total ?? 0}
          icon={<FileText size={20} />}
          color="from-blue-500 to-cyan-500"
          delay="0ms"
        />
        <OverviewCard
          label={t('knowledge.status.published')}
          value={overview?.stats?.published ?? 0}
          icon={<CheckCircle2 size={20} />}
          color="from-green-500 to-emerald-500"
          delay="50ms"
        />
        <OverviewCard
          label={t('knowledge.status.draft')}
          value={overview?.stats?.draft ?? 0}
          icon={<FileEdit size={20} />}
          color="from-amber-500 to-orange-500"
          delay="100ms"
        />
        <OverviewCard
          label={t('knowledge.list.overview.categories')}
          value={overview?.stats?.categories ?? 0}
          icon={<Layers size={20} />}
          color="from-violet-500 to-purple-500"
          delay="150ms"
        />
      </div>

      {/* 全局 FTS5 搜索结果（主区域直观展示，避免被页头/其它 label 遮挡 — T000104） */}
      {globalSearchResult && globalSearchInput.trim() && tab === 'articles' && !showTrash && (
        <section
          aria-label={t('knowledge.search.sectionAria')}
          className="relative overflow-hidden rounded-2xl border border-indigo-100 dark:border-indigo-900/40 bg-white dark:bg-slate-800 shadow-lg shadow-indigo-500/10"
        >
          <div className="absolute inset-x-0 top-0 h-1 bg-gradient-to-r from-blue-500 via-indigo-500 to-violet-500" />
          <div className="p-5 sm:p-6">
            <div className="flex flex-wrap items-start justify-between gap-3 mb-4">
              <div className="flex items-center gap-3">
                <div className="w-11 h-11 rounded-2xl bg-gradient-to-br from-blue-500 to-indigo-600 text-white flex items-center justify-center shadow-md shadow-indigo-500/30">
                  <Search size={20} />
                </div>
                <div>
                  <h2 className="text-lg font-semibold text-slate-800 dark:text-slate-100 flex items-center gap-2">
                    {t('knowledge.search.resultsTitle')}
                    {globalSearchResult.isFts && (
                      <span className="inline-flex items-center gap-1 px-2 py-0.5 text-[11px] rounded-full bg-emerald-100 dark:bg-emerald-900/40 text-emerald-700 dark:text-emerald-300 font-medium">
                        {t('knowledge.search.ftsBadge')}
                      </span>
                    )}
                  </h2>
                  <p className="text-xs sm:text-sm text-slate-500 dark:text-slate-400 mt-1">
                    {t('knowledge.search.keywordPrefix')}
                    <span className="mx-1 px-2 py-0.5 rounded-md bg-amber-50 dark:bg-amber-900/30 text-amber-700 dark:text-amber-300 font-medium break-all">
                      {globalSearchInput}
                    </span>
                    {t('knowledge.search.matchedBefore', { count: globalSearchResult.total })}<b className="text-slate-700 dark:text-slate-200">{globalSearchResult.total}</b>{t('knowledge.search.matchedAfter', { count: globalSearchResult.total })}
                    {globalSearchResult.total > 0 && (
                      <>{t('knowledge.search.matchedPagePrefix')}<b>{globalSearchResult.page}</b>{t('knowledge.search.matchedPageSuffix', { totalPages: Math.max(1, Math.ceil(globalSearchResult.total / globalSearchResult.pageSize)) })}</>
                    )}
                  </p>
                </div>
              </div>
              <div className="flex items-center gap-2">
                {globalSearchLoading && (
                  <span className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs rounded-xl bg-blue-50 dark:bg-blue-900/30 text-blue-600 dark:text-blue-300 border border-blue-100 dark:border-blue-800/50">
                    <RefreshCw size={12} className="animate-spin" />
                    {t('knowledge.search.refreshing')}
                  </span>
                )}
                <button
                  onClick={() => {
                    setGlobalSearchInput('');
                    setGlobalSearchResult(null);
                    setGlobalSearchOpen(false);
                  }}
                  className="inline-flex items-center gap-1.5 px-3.5 py-2 text-xs sm:text-sm font-medium rounded-xl border border-slate-200 dark:border-slate-700 text-slate-600 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700/60 transition-colors"
                >
                  <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                    <path d="M18 6L6 18M6 6l12 12" />
                  </svg>
                  {t('knowledge.common.clearSearch')}
                </button>
              </div>
            </div>

            {globalSearchLoading && !globalSearchResult.data.length ? (
              <div className="p-10 flex items-center justify-center gap-2 text-slate-400">
                <RefreshCw size={16} className="animate-spin text-blue-500" />
                <span className="text-sm">{t('knowledge.search.searchingPleaseWait')}</span>
              </div>
            ) : globalSearchResult.data.length === 0 ? (
              <div className="p-10 text-center">
                <div className="w-20 h-20 mx-auto mb-4 rounded-2xl bg-gradient-to-br from-slate-50 to-slate-100 dark:from-slate-700/50 dark:to-slate-900/50 flex items-center justify-center ring-1 ring-slate-100 dark:ring-slate-700/60">
                  <Search size={28} className="text-slate-400 dark:text-slate-500" />
                </div>
                <h3 className="text-base font-semibold text-slate-700 dark:text-slate-200 mb-1">{t('knowledge.search.noResultsTitle')}</h3>
                <p className="text-sm text-slate-400 dark:text-slate-500">
                  {t('knowledge.search.noResultsHint')}
                </p>
              </div>
            ) : (
              <ul className="grid grid-cols-1 gap-3 divide-y divide-slate-100 dark:divide-slate-700/70">
                {globalSearchResult.data.map((a: KnowledgeArticleDto) => (
                  <li key={a.id}>
                    <button
                      onClick={() => {
                        setGlobalSearchOpen(false);
                        void handleViewArticle(a);
                      }}
                      className="w-full text-left group rounded-xl p-4 hover:bg-indigo-50/50 dark:hover:bg-slate-700/40 transition-colors border border-transparent hover:border-indigo-100 dark:hover:border-slate-600/60"
                    >
                      <div className="flex flex-wrap items-center justify-between gap-2 mb-1.5">
                        <div className="font-semibold text-slate-800 dark:text-slate-100 group-hover:text-blue-600 dark:group-hover:text-blue-400 transition-colors truncate">
                          {a.title}
                        </div>
                        <span className={`flex-shrink-0 px-2.5 py-0.5 text-[11px] rounded-full font-medium ${STATUS_COLORS[a.status] || STATUS_COLORS.draft}`}>
                          {articleStatusLabel(a.status, a.statusLabel || a.status)}
                        </span>
                      </div>
                      <div className="flex flex-wrap items-center gap-2 text-[11px] text-slate-400 dark:text-slate-500 mb-2">
                        {a.categoryName && (
                          <span className="inline-flex items-center gap-1 px-2 py-0.5 rounded-full bg-slate-100 dark:bg-slate-700 text-slate-500 dark:text-slate-300">
                            <Folder size={10} />
                            {a.categoryName}
                          </span>
                        )}
                        {a.authorName && (
                          <span>{t('knowledge.list.byAuthor', { name: a.authorName })}</span>
                        )}
                        <span>{t('knowledge.list.updatedAt', { date: formatDateTime(a.updatedAt) })}</span>
                      </div>
                      {a.snippet ? (
                        <p
                          className="text-xs sm:text-sm text-slate-600 dark:text-slate-300 leading-relaxed"
                          dangerouslySetInnerHTML={{ __html: renderSnippet(a.snippet) }}
                        />
                      ) : a.summary ? (
                        <p className="text-xs sm:text-sm text-slate-500 dark:text-slate-400 leading-relaxed line-clamp-3">
                          {a.summary}
                        </p>
                      ) : null}
                    </button>
                  </li>
                ))}
              </ul>
            )}

            {globalSearchResult.total > globalSearchResult.pageSize && (
              <div className="mt-4 pt-3 border-t border-slate-100 dark:border-slate-700/70 flex flex-wrap items-center justify-between gap-2 text-xs text-slate-500 dark:text-slate-400">
                <div>
                  {t('knowledge.common.totalBefore', { count: globalSearchResult.total })}<b className="text-slate-700 dark:text-slate-200">{globalSearchResult.total}</b>{t('knowledge.search.perPageAfter', { pageSize: globalSearchResult.pageSize, count: globalSearchResult.total })}
                </div>
                <div className="flex items-center gap-1.5">
                  <button
                    disabled={globalSearchResult.page <= 1 || globalSearchLoading}
                    onClick={() => void runGlobalSearch(globalSearchInput, globalSearchResult.page - 1)}
                    className="px-3 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 disabled:opacity-40 hover:bg-slate-50 dark:hover:bg-slate-700 text-slate-600 dark:text-slate-300 transition-colors"
                  >
                    {t('knowledge.common.previousPage')}
                  </button>
                  <span className="px-2 font-medium text-slate-600 dark:text-slate-300">
                    {globalSearchResult.page} / {Math.max(1, Math.ceil(globalSearchResult.total / globalSearchResult.pageSize))}
                  </span>
                  <button
                    disabled={globalSearchResult.page * globalSearchResult.pageSize >= globalSearchResult.total || globalSearchLoading}
                    onClick={() => void runGlobalSearch(globalSearchInput, globalSearchResult.page + 1)}
                    className="px-3 py-1.5 rounded-lg border border-slate-200 dark:border-slate-700 disabled:opacity-40 hover:bg-slate-50 dark:hover:bg-slate-700 text-slate-600 dark:text-slate-300 transition-colors"
                  >
                    {t('knowledge.common.nextPage')}
                  </button>
                </div>
              </div>
            )}
          </div>
        </section>
      )}

      {/* Tab 切换 */}
      <div className="flex items-center justify-between gap-2 p-1.5 bg-slate-100/80 dark:bg-slate-800/60 rounded-2xl border border-slate-200/60 dark:border-slate-700/60 backdrop-blur-sm">
        <div className="flex items-center gap-1 flex-1">
          <button
            onClick={() => { setTab('articles'); setShowTrash(false); }}
            className={`relative flex-1 sm:flex-none flex items-center justify-center gap-2 px-4 py-2.5 text-sm font-medium rounded-xl transition-all duration-200 ${
              tab === 'articles' && !showTrash
                ? 'bg-white dark:bg-slate-700 text-blue-600 dark:text-blue-400 shadow-md shadow-blue-500/10'
                : 'text-slate-500 hover:text-slate-700 dark:hover:text-slate-300 hover:bg-white/50 dark:hover:bg-slate-700/50'
            }`}
          >
            <FileText size={16} />
            {t('knowledge.list.title')}
          </button>
          <button
            onClick={() => { setTab('categories'); setShowTrash(false); }}
            className={`relative flex-1 sm:flex-none flex items-center justify-center gap-2 px-4 py-2.5 text-sm font-medium rounded-xl transition-all duration-200 ${
              tab === 'categories' && !showTrash
                ? 'bg-white dark:bg-slate-700 text-blue-600 dark:text-blue-400 shadow-md shadow-blue-500/10'
                : 'text-slate-500 hover:text-slate-700 dark:hover:text-slate-300 hover:bg-white/50 dark:hover:bg-slate-700/50'
            }`}
          >
            <Folder size={16} />
            {t('knowledge.categories.title')}
          </button>
        </div>
        {isSuperAdmin && (
          <button
            onClick={() => {
              setShowTrash(!showTrash);
              if (!showTrash) {
                if (tab === 'articles') refreshTrashArticles();
                else refreshTrashCategories();
              }
            }}
            className={`flex items-center gap-2 px-4 py-2.5 text-sm font-medium rounded-xl transition-all duration-200 ${
              showTrash
                ? 'bg-white dark:bg-slate-700 text-amber-600 dark:text-amber-400 shadow-md shadow-amber-500/10'
                : 'text-slate-500 hover:text-slate-700 dark:hover:text-slate-300 hover:bg-white/50 dark:hover:bg-slate-700/50'
            }`}
          >
            <Recycle size={16} />
            <span className="hidden sm:inline">{t('knowledge.trash.tab')}</span>
          </button>
        )}
      </div>

      {/* 文章管理 Tab */}
      {tab === 'articles' && (
        <div className="space-y-4">
          {showTrash ? (
            <>
              {/* 文章垃圾箱工具栏 */}
              <div className="flex flex-wrap items-center justify-between gap-3 p-4 bg-gradient-to-r from-rose-50/50 to-orange-50/50 dark:from-rose-900/10 dark:to-orange-900/10 rounded-2xl shadow-sm border border-rose-100 dark:border-rose-900/30">
                <div className="flex items-center gap-2 flex-1 min-w-[240px] px-3 py-2 bg-white dark:bg-slate-900/40 rounded-xl border border-slate-200 dark:border-slate-700 focus-within:ring-2 focus-within:ring-rose-500/20 transition-all">
                  <Search size={16} className="text-slate-400" />
                  <input
                    type="text"
                    placeholder={t('knowledge.trash.searchArticlesPlaceholder')}
                    value={trashKeyword}
                    onChange={(e) => handleTrashSearchChange(e.target.value, 'articles')}
                    className="flex-1 px-1 py-0.5 text-sm bg-transparent border-0 focus:outline-none placeholder:text-slate-400"
                  />
                </div>
                <button
                  onClick={() => setClearTrashTarget('articles')}
                  className="group flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-white bg-gradient-to-r from-rose-600 to-red-600 hover:from-rose-700 hover:to-red-700 rounded-xl transition-all shadow-md shadow-rose-500/20 hover:shadow-lg hover:shadow-rose-500/30 active:scale-[0.98]"
                >
                  <Eraser size={14} className="group-hover:rotate-12 transition-transform" />
                  {t('knowledge.common.emptyTrash')}
                </button>
              </div>

              {/* 文章垃圾箱列表 */}
              <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-sm overflow-hidden border border-slate-100 dark:border-slate-700">
                {loadingTrash ? (
                  <div className="p-12 text-center text-slate-500 dark:text-slate-400">
                    <div className="flex items-center justify-center gap-2">
                      <RefreshCw className="w-5 h-5 animate-spin text-blue-500" />
                      <span>{t('common.shell.loading')}</span>
                    </div>
                  </div>
                ) : !trashArticles || trashArticles.data.length === 0 ? (
                  <div className="p-16 text-center text-slate-500 dark:text-slate-400">
                    <div className="w-24 h-24 rounded-2xl bg-gradient-to-br from-amber-50 to-orange-50 dark:from-amber-900/20 dark:to-orange-900/20 flex items-center justify-center mx-auto mb-4 ring-1 ring-amber-100 dark:ring-amber-900/30">
                      <Recycle size={40} className="text-amber-400" />
                    </div>
                    <h3 className="text-lg font-semibold text-slate-700 dark:text-slate-300 mb-1">{t('knowledge.trash.emptyArticles')}</h3>
                    <p className="text-sm text-slate-400 dark:text-slate-500">{t('knowledge.trash.retentionArticles')}</p>
                  </div>
                ) : (
                  <>
                  <div className="hidden md:block overflow-x-auto">
                    <table className="w-full text-sm">
                      <thead className="bg-slate-50/80 dark:bg-slate-900/40 text-slate-500 dark:text-slate-400 uppercase text-xs border-b border-slate-200 dark:border-slate-700">
                        <tr>
                          <th className="px-6 py-3.5 text-left font-semibold tracking-wider">{t('knowledge.common.col.title')}</th>
                          <th className="px-6 py-3.5 text-left font-semibold tracking-wider">{t('knowledge.common.col.category')}</th>
                          <th className="px-6 py-3.5 text-left font-semibold tracking-wider">{t('knowledge.common.col.status')}</th>
                          <th className="px-6 py-3.5 text-left font-semibold tracking-wider">{t('knowledge.common.col.author')}</th>
                          <th className="px-6 py-3.5 text-left font-semibold tracking-wider">{t('knowledge.common.col.deletedAt')}</th>
                          <th className="px-6 py-3.5 text-right font-semibold tracking-wider">{t('knowledge.common.col.actions')}</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-slate-100 dark:divide-slate-700/70">
                        {trashArticles.data.map((a) => (
                          <tr key={a.id} className="hover:bg-rose-50/30 dark:hover:bg-slate-700/30 transition-colors duration-150">
                            <td className="px-6 py-4">
                              <span className="font-medium text-slate-800 dark:text-slate-200 truncate max-w-xs">
                                {a.title}
                              </span>
                              {a.summary && (
                                <div className="text-xs text-slate-400 truncate max-w-xs mt-1">
                                  {a.summary}
                                </div>
                              )}
                            </td>
                            <td className="px-6 py-4 text-slate-600 dark:text-slate-300">
                              {a.categoryName || '-'}
                            </td>
                            <td className="px-6 py-4">
                              <span className={`px-2.5 py-1 text-xs rounded-full font-medium ${STATUS_COLORS[a.status] || STATUS_COLORS.draft}`}>
                                {articleStatusLabel(a.status, a.statusLabel || a.status)}
                              </span>
                            </td>
                            <td className="px-6 py-4 text-slate-600 dark:text-slate-300">
                              {a.authorName || '-'}
                            </td>
                            <td className="px-6 py-4 text-xs text-slate-500 dark:text-slate-400">
                              {formatDateTime(a.updatedAt)}
                            </td>
                            <td className="px-6 py-4 text-right">
                              <div className="flex items-center justify-end gap-1">
                                <button
                                  onClick={() => setRestoreArticleTarget(a)}
                                  className="p-2 rounded-lg hover:bg-green-50 dark:hover:bg-green-900/30 text-green-600 dark:text-green-400 transition-colors"
                                  title={t('knowledge.common.restore')}
                                >
                                  <RotateCcw size={15} />
                                </button>
                                <button
                                  onClick={() => setHardDeleteArticleTarget(a)}
                                  className="p-2 rounded-lg hover:bg-rose-50 dark:hover:bg-rose-900/30 text-rose-600 dark:text-rose-400 transition-colors"
                                  title={t('knowledge.common.hardDelete')}
                                >
                                  <Eraser size={15} />
                                </button>
                              </div>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>

                  {/* 移动端卡片视图 */}
                  <div className="md:hidden divide-y divide-slate-100 dark:divide-slate-700/70">
                    {trashArticles.data.map((a) => (
                      <div key={a.id} className="p-3.5 hover:bg-rose-50/30 dark:hover:bg-slate-700/30 transition-colors duration-150">
                        <div className="flex items-start justify-between gap-2 mb-1.5">
                          <div className="font-medium text-slate-800 dark:text-slate-200 flex-1 min-w-0 truncate">
                            {a.title}
                          </div>
                          <div className="text-xs text-slate-400 flex-shrink-0 whitespace-nowrap">
                            {formatDateTime(a.updatedAt)}
                          </div>
                        </div>
                        {a.summary && (
                          <div className="text-xs text-slate-400 dark:text-slate-500 truncate mb-2">
                            {a.summary}
                          </div>
                        )}
                        <div className="flex items-center flex-wrap gap-2 mb-2.5">
                          <span className="inline-flex items-center gap-1 px-2 py-0.5 text-xs rounded-md bg-slate-100 dark:bg-slate-700/70 text-slate-600 dark:text-slate-300">
                            <Folder size={11} className="opacity-60" />
                            {a.categoryName || '-'}
                          </span>
                          <span className={`px-2.5 py-0.5 text-xs rounded-full font-medium ${STATUS_COLORS[a.status] || STATUS_COLORS.draft}`}>
                            {articleStatusLabel(a.status, a.statusLabel || a.status)}
                          </span>
                          <span className="inline-flex items-center gap-1 text-xs text-slate-500 dark:text-slate-400">
                            <User size={11} className="opacity-60" />
                            {a.authorName || '-'}
                          </span>
                        </div>
                        <div className="flex items-center gap-2">
                          <button
                            onClick={() => setRestoreArticleTarget(a)}
                            className="flex-1 flex items-center justify-center gap-1.5 px-3 py-2 text-sm font-medium text-green-700 dark:text-green-400 bg-green-50 dark:bg-green-900/30 hover:bg-green-100 dark:hover:bg-green-900/50 rounded-lg transition-colors"
                            title={t('knowledge.common.restore')}
                          >
                            <RotateCcw size={14} />
                            {t('knowledge.common.restore')}
                          </button>
                          <button
                            onClick={() => setHardDeleteArticleTarget(a)}
                            className="flex-1 flex items-center justify-center gap-1.5 px-3 py-2 text-sm font-medium text-rose-700 dark:text-rose-400 bg-rose-50 dark:bg-rose-900/30 hover:bg-rose-100 dark:hover:bg-rose-900/50 rounded-lg transition-colors"
                            title={t('knowledge.common.hardDelete')}
                          >
                            <Eraser size={14} />
                            {t('knowledge.common.hardDelete')}
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>
                  <Pagination
                    currentPage={trashArticles.currentPage}
                    totalPages={trashArticles.totalPages}
                    totalItems={trashArticles.totalItems}
                    onPageChange={(p) => { setTrashPage(p); refreshTrashArticles(p, trashKeyword); }}
                  />
                  </>
                )}
              </div>
            </>
          ) : (
            <>
              {/* 工具栏 */}
              <div className="flex flex-wrap items-center gap-3 p-4 bg-white dark:bg-slate-800 rounded-2xl shadow-sm border border-slate-100 dark:border-slate-700">
                <div className="flex items-center gap-2 flex-1 min-w-[240px] px-3 py-2 bg-slate-50 dark:bg-slate-900/40 rounded-xl focus-within:ring-2 focus-within:ring-blue-500/20 focus-within:bg-white dark:focus-within:bg-slate-900 focus-within:border-blue-500/30 border border-transparent transition-all">
                  <Search size={16} className="text-slate-400" />
                  <input
                    type="text"
                    placeholder={t('knowledge.list.searchPlaceholder')}
                    value={searchInput}
                    onChange={(e) => handleSearchChange(e.target.value)}
                    className="flex-1 px-1 py-0.5 text-sm bg-transparent border-0 focus:outline-none placeholder:text-slate-400"
                  />
                  {searchInput && (
                    <button
                      onClick={() => handleSearchChange('')}
                      className="text-slate-400 hover:text-slate-600 dark:hover:text-slate-200 transition-colors"
                      aria-label={t('knowledge.common.clearSearch')}
                    >
                      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                        <path d="M18 6L6 18M6 6l12 12" />
                      </svg>
                    </button>
                  )}
                </div>
                <select
                  value={articleQuery.status ?? ''}
                  onChange={(e) => handleArticleFilterChange('status', e.target.value)}
                  className="px-3 py-2 text-sm border border-slate-200 dark:border-slate-600 rounded-xl bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 focus:outline-none focus:ring-2 focus:ring-blue-500/20 hover:border-blue-400 dark:hover:border-blue-500 transition-all cursor-pointer"
                >
                  <option value="">{t('knowledge.list.allStatuses')}</option>
                  {STATUS_OPTIONS.map((s) => (
                    <option key={s.value} value={s.value}>
                      {t(s.labelKey)}
                    </option>
                  ))}
                </select>
                <select
                  value={articleQuery.categoryId ?? ''}
                  onChange={(e) => handleArticleFilterChange('categoryId', e.target.value ? Number(e.target.value) : null)}
                  className="px-3 py-2 text-sm border border-slate-200 dark:border-slate-600 rounded-xl bg-white dark:bg-slate-800 text-slate-700 dark:text-slate-200 max-w-[200px] focus:outline-none focus:ring-2 focus:ring-blue-500/20 hover:border-blue-400 dark:hover:border-blue-500 transition-all cursor-pointer"
                >
                  <option value="">{t('knowledge.list.allCategories')}</option>
                  {activeFlatCategories.map((c) => (
                    <option key={c.id} value={c.id}>
                      {categoryIndent(c.depth)}{c.name}
                    </option>
                  ))}
                </select>
                <label className={`flex items-center gap-2 text-sm cursor-pointer px-3 py-2 rounded-xl border transition-all ${
                  articleQuery.isPinned === true
                    ? 'border-amber-300 dark:border-amber-700 bg-amber-50 dark:bg-amber-900/20 text-amber-700 dark:text-amber-400'
                    : 'border-slate-200 dark:border-slate-600 text-slate-600 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700/30'
                }`}>
                  <input
                    type="checkbox"
                    checked={articleQuery.isPinned === true}
                    onChange={(e) => handleArticleFilterChange('isPinned', e.target.checked ? true : null)}
                    className="rounded text-amber-500 focus:ring-amber-500/20"
                  />
                  <Pin size={14} className={articleQuery.isPinned === true ? 'text-amber-500' : ''} />
                  {t('knowledge.list.pinnedOnly')}
                </label>
                {isSuperAdmin && (
                  <button
                    onClick={openCreateArticle}
                    className="group flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-white bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-700 hover:to-indigo-700 rounded-xl transition-all shadow-md shadow-blue-500/20 hover:shadow-lg hover:shadow-blue-500/30 active:scale-[0.98]"
                  >
                    <Plus size={16} className="group-hover:rotate-90 transition-transform duration-300" />
                    {t('knowledge.common.newArticle')}
                  </button>
                )}
              </div>

              {/* 文章列表 */}
              <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-sm overflow-hidden border border-slate-100 dark:border-slate-700">
                {loadingArticles ? (
                  <div className="divide-y divide-slate-100 dark:divide-slate-700">
                    {[0, 1, 2, 3, 4].map((i) => (
                      <div key={i} className="flex items-center gap-4 px-6 py-4 animate-pulse">
                        <div className="flex-1 space-y-2">
                          <div className="h-4 bg-slate-200 dark:bg-slate-700 rounded w-3/5" />
                          <div className="h-3 bg-slate-100 dark:bg-slate-800 rounded w-4/5" />
                        </div>
                        <div className="h-5 w-16 bg-slate-100 dark:bg-slate-800 rounded-full" />
                        <div className="h-3 w-20 bg-slate-100 dark:bg-slate-800 rounded" />
                        <div className="h-3 w-24 bg-slate-100 dark:bg-slate-800 rounded" />
                      </div>
                    ))}
                  </div>
                ) : !articles || articles.data.length === 0 ? (
                  <div className="p-16 text-center text-slate-500 dark:text-slate-400">
                    <div className="w-24 h-24 rounded-2xl bg-gradient-to-br from-blue-50 to-indigo-50 dark:from-blue-900/20 dark:to-indigo-900/20 flex items-center justify-center mx-auto mb-4 ring-1 ring-blue-100 dark:ring-blue-900/30">
                      <FileText size={40} className="text-blue-400" />
                    </div>
                    <h3 className="text-lg font-semibold text-slate-700 dark:text-slate-300 mb-1">
                      {searchInput || articleQuery.status || articleQuery.categoryId ? t('knowledge.list.emptyFiltered') : t('knowledge.list.empty')}
                    </h3>
                    <p className="text-sm mb-5 text-slate-400 dark:text-slate-500">
                      {searchInput || articleQuery.status || articleQuery.categoryId ? t('knowledge.list.emptyFilteredHint') : t('knowledge.list.emptyHint')}
                    </p>
                    {isSuperAdmin && !searchInput && !articleQuery.status && !articleQuery.categoryId && (
                      <button
                        onClick={openCreateArticle}
                        className="inline-flex items-center gap-2 px-5 py-2.5 text-sm font-medium text-white bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-700 hover:to-indigo-700 rounded-xl transition-all shadow-md shadow-blue-500/20 hover:shadow-lg hover:shadow-blue-500/30"
                      >
                        <Plus size={16} />
                        {t('knowledge.common.newArticle')}
                      </button>
                    )}
                  </div>
                ) : (
                  <>
                  <div className="hidden md:block overflow-x-auto">
                    <table className="w-full text-sm">
                      <thead className="bg-slate-50/80 dark:bg-slate-900/40 text-slate-500 dark:text-slate-400 uppercase text-xs border-b border-slate-200 dark:border-slate-700">
                        <tr>
                          <th className="px-6 py-3.5 text-left font-semibold tracking-wider">{t('knowledge.common.col.title')}</th>
                          <th className="px-6 py-3.5 text-left font-semibold tracking-wider">{t('knowledge.common.col.category')}</th>
                          <th className="px-6 py-3.5 text-left font-semibold tracking-wider">{t('knowledge.common.col.status')}</th>
                          <th className="px-6 py-3.5 text-left font-semibold tracking-wider">{t('knowledge.common.col.author')}</th>
                          <th className="px-6 py-3.5 text-left font-semibold tracking-wider">{t('knowledge.list.col.viewsLikes')}</th>
                          <th className="px-6 py-3.5 text-left font-semibold tracking-wider">{t('knowledge.list.col.updatedAt')}</th>
                          <th className="px-6 py-3.5 text-right font-semibold tracking-wider">{t('knowledge.common.col.actions')}</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-slate-100 dark:divide-slate-700/70">
                        {articles.data.map((a) => (
                          <tr
                            key={a.id}
                            className="hover:bg-blue-50/40 dark:hover:bg-slate-700/40 transition-colors duration-150 cursor-pointer group"
                            onClick={() => handleViewArticle(a)}
                          >
                            <td className="px-6 py-4" onClick={(e) => e.stopPropagation()}>
                              <div className="flex items-center gap-3">
                                {a.isPinned && (
                                  <Pin size={14} className="text-amber-500 flex-shrink-0 fill-amber-500" />
                                )}
                                <button
                                  onClick={() => handleViewArticle(a)}
                                  className="font-medium text-slate-800 dark:text-slate-200 truncate max-w-xs text-left hover:text-blue-600 dark:hover:text-blue-400 transition-colors"
                                >
                                  {a.title}
                                </button>
                              </div>
                              {a.summary && (
                                <div className="text-xs text-slate-400 dark:text-slate-500 truncate max-w-xs mt-1 ml-5">
                                  {a.summary}
                                </div>
                              )}
                              {a.tagList && a.tagList.length > 0 && (
                                <div className="flex flex-wrap gap-1.5 mt-2 ml-5">
                                  {a.tagList.slice(0, 3).map((t, i) => (
                                    <span
                                      key={i}
                                      className="px-2 py-0.5 text-xs rounded-md bg-blue-50 dark:bg-blue-900/20 text-blue-600 dark:text-blue-400 border border-blue-100 dark:border-blue-800/40"
                                    >
                                      #{t}
                                    </span>
                                  ))}
                                  {a.tagList.length > 3 && (
                                    <span className="px-2 py-0.5 text-xs rounded-md bg-slate-100 dark:bg-slate-700 text-slate-500 dark:text-slate-400">
                                      +{a.tagList.length - 3}
                                    </span>
                                  )}
                                </div>
                              )}
                            </td>
                            <td className="px-6 py-4 text-slate-600 dark:text-slate-300" onClick={(e) => e.stopPropagation()}>
                              {a.categoryName ? (
                                <span className="inline-flex items-center gap-1 px-2 py-1 text-xs rounded-md bg-slate-100 dark:bg-slate-700/70 text-slate-600 dark:text-slate-300">
                                  <Folder size={11} className="opacity-60" />
                                  {a.categoryName}
                                </span>
                              ) : (
                                <span className="text-slate-400">-</span>
                              )}
                            </td>
                            <td className="px-6 py-4" onClick={(e) => e.stopPropagation()}>
                              <span className={`px-2.5 py-1 text-xs rounded-full font-medium ${STATUS_COLORS[a.status] || STATUS_COLORS.draft}`}>
                                {articleStatusLabel(a.status, a.statusLabel || a.status)}
                              </span>
                            </td>
                            <td className="px-6 py-4 text-slate-600 dark:text-slate-300" onClick={(e) => e.stopPropagation()}>
                              <div className="flex items-center gap-2">
                                <div className="w-6 h-6 rounded-full bg-gradient-to-br from-slate-200 to-slate-300 dark:from-slate-600 dark:to-slate-700 flex items-center justify-center text-xs font-medium text-slate-600 dark:text-slate-300 flex-shrink-0">
                                  {(a.authorName || '?').charAt(0).toUpperCase()}
                                </div>
                                <span className="truncate">{a.authorName || '-'}</span>
                              </div>
                            </td>
                            <td className="px-6 py-4 text-slate-600 dark:text-slate-300" onClick={(e) => e.stopPropagation()}>
                              <div className="flex items-center gap-3 text-xs">
                                <span className="flex items-center gap-1 text-slate-500 dark:text-slate-400 hover:text-blue-600 dark:hover:text-blue-400 transition-colors">
                                  <Eye size={12} /> {a.viewCount}
                                </span>
                                <span className="flex items-center gap-1 text-slate-500 dark:text-slate-400 hover:text-rose-600 dark:hover:text-rose-400 transition-colors">
                                  <ThumbsUp size={12} /> {a.likeCount}
                                </span>
                              </div>
                            </td>
                            <td className="px-6 py-4 text-xs text-slate-500 dark:text-slate-400 whitespace-nowrap" onClick={(e) => e.stopPropagation()}>
                              <div className="flex items-center gap-1.5">
                                <Clock size={12} className="opacity-50" />
                                {formatDateTime(a.updatedAt)}
                              </div>
                            </td>
                            <td className="px-6 py-4 text-right" onClick={(e) => e.stopPropagation()}>
                              <div className="flex items-center justify-end gap-1">
                                <button
                                  onClick={() => handleViewArticle(a)}
                                  className="p-2 rounded-lg hover:bg-blue-50 dark:hover:bg-blue-900/30 text-blue-600 dark:text-blue-400 transition-colors"
                                  title={t('knowledge.common.view')}
                                >
                                  <Eye size={15} />
                                </button>
                                {isSuperAdmin && (
                                  <>
                                    <button
                                      onClick={() => openEditArticle(a)}
                                      className="p-2 rounded-lg hover:bg-slate-100 dark:hover:bg-slate-700 text-slate-600 dark:text-slate-300 transition-colors"
                                      title={t('knowledge.common.edit')}
                                    >
                                      <Edit2 size={15} />
                                    </button>
                                    <button
                                      onClick={() => setDeleteArticleTarget(a)}
                                      className="p-2 rounded-lg hover:bg-rose-50 dark:hover:bg-rose-900/30 text-rose-600 dark:text-rose-400 transition-colors"
                                      title={t('knowledge.common.delete')}
                                    >
                                      <Trash2 size={15} />
                                    </button>
                                  </>
                                )}
                              </div>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>

                  {/* 移动端卡片视图 */}
                  <div className="md:hidden divide-y divide-slate-100 dark:divide-slate-700/70">
                    {articles.data.map((a) => (
                      <div key={a.id} className="p-3.5 hover:bg-blue-50/40 dark:hover:bg-slate-700/40 transition-colors duration-150">
                        <div className="flex items-start justify-between gap-2 mb-1.5">
                          <div className="font-medium text-slate-800 dark:text-slate-200 flex-1 min-w-0 flex items-center gap-1.5">
                            {a.isPinned && <Pin size={14} className="text-amber-500 flex-shrink-0 fill-amber-500" />}
                            <button
                              onClick={() => handleViewArticle(a)}
                              className="text-left truncate hover:text-blue-600 dark:hover:text-blue-400 transition-colors"
                            >
                              {a.title}
                            </button>
                          </div>
                          <div className="text-xs text-slate-400 flex-shrink-0 whitespace-nowrap">
                            {formatDateTime(a.updatedAt)}
                          </div>
                        </div>
                        {a.summary && (
                          <div className="text-xs text-slate-400 dark:text-slate-500 truncate mb-2">
                            {a.summary}
                          </div>
                        )}
                        <div className="flex items-center flex-wrap gap-2 mb-2">
                          {a.categoryName ? (
                            <span className="inline-flex items-center gap-1 px-2 py-0.5 text-xs rounded-md bg-slate-100 dark:bg-slate-700/70 text-slate-600 dark:text-slate-300">
                              <Folder size={11} className="opacity-60" />
                              {a.categoryName}
                            </span>
                          ) : (
                            <span className="text-xs text-slate-400">-</span>
                          )}
                          <span className={`px-2.5 py-0.5 text-xs rounded-full font-medium ${STATUS_COLORS[a.status] || STATUS_COLORS.draft}`}>
                            {articleStatusLabel(a.status, a.statusLabel || a.status)}
                          </span>
                          <span className="inline-flex items-center gap-1.5 text-xs text-slate-500 dark:text-slate-400">
                            <span className="w-5 h-5 rounded-full bg-gradient-to-br from-slate-200 to-slate-300 dark:from-slate-600 dark:to-slate-700 flex items-center justify-center text-[10px] font-medium text-slate-600 dark:text-slate-300">
                              {(a.authorName || '?').charAt(0).toUpperCase()}
                            </span>
                            {a.authorName || '-'}
                          </span>
                        </div>
                        <div className="flex items-center flex-wrap gap-3 mb-2.5">
                          <span className="inline-flex items-center gap-1 text-xs text-slate-500 dark:text-slate-400">
                            <Eye size={12} /> {a.viewCount}
                          </span>
                          <span className="inline-flex items-center gap-1 text-xs text-slate-500 dark:text-slate-400">
                            <ThumbsUp size={12} /> {a.likeCount}
                          </span>
                          {a.tagList && a.tagList.length > 0 && (
                            <div className="flex flex-wrap gap-1.5">
                              {a.tagList.slice(0, 3).map((t, i) => (
                                <span
                                  key={i}
                                  className="px-1.5 py-0.5 text-xs rounded-md bg-blue-50 dark:bg-blue-900/20 text-blue-600 dark:text-blue-400 border border-blue-100 dark:border-blue-800/40"
                                >
                                  #{t}
                                </span>
                              ))}
                              {a.tagList.length > 3 && (
                                <span className="px-1.5 py-0.5 text-xs rounded-md bg-slate-100 dark:bg-slate-700 text-slate-500 dark:text-slate-400">
                                  +{a.tagList.length - 3}
                                </span>
                              )}
                            </div>
                          )}
                        </div>
                        <div className="flex items-center gap-2" onClick={(e) => e.stopPropagation()}>
                          <button
                            onClick={() => handleViewArticle(a)}
                            className="flex-1 flex items-center justify-center gap-1.5 px-3 py-2 text-sm font-medium text-blue-700 dark:text-blue-400 bg-blue-50 dark:bg-blue-900/30 hover:bg-blue-100 dark:hover:bg-blue-900/50 rounded-lg transition-colors"
                            title={t('knowledge.common.view')}
                          >
                            <Eye size={14} />
                            {t('knowledge.common.view')}
                          </button>
                          {isSuperAdmin && (
                            <>
                              <button
                                onClick={() => openEditArticle(a)}
                                className="flex-1 flex items-center justify-center gap-1.5 px-3 py-2 text-sm font-medium text-slate-700 dark:text-slate-300 bg-slate-100 dark:bg-slate-700/70 hover:bg-slate-200 dark:hover:bg-slate-700 rounded-lg transition-colors"
                                title={t('knowledge.common.edit')}
                              >
                                <Edit2 size={14} />
                                {t('knowledge.common.edit')}
                              </button>
                              <button
                                onClick={() => setDeleteArticleTarget(a)}
                                className="flex-1 flex items-center justify-center gap-1.5 px-3 py-2 text-sm font-medium text-rose-700 dark:text-rose-400 bg-rose-50 dark:bg-rose-900/30 hover:bg-rose-100 dark:hover:bg-rose-900/50 rounded-lg transition-colors"
                                title={t('knowledge.common.delete')}
                              >
                                <Trash2 size={14} />
                                {t('knowledge.common.delete')}
                              </button>
                            </>
                          )}
                        </div>
                      </div>
                    ))}
                  </div>
                  <Pagination
                    currentPage={articles.currentPage}
                    totalPages={articles.totalPages}
                    totalItems={articles.totalItems}
                    onPageChange={handleArticlePageChange}
                  />
                  </>
                )}
              </div>
            </>
          )}
        </div>
      )}

      {/* 分类管理 Tab */}
      {tab === 'categories' && (
        <div className="space-y-4">
          {showTrash ? (
            <>
              {/* 分类垃圾箱工具栏 */}
              <div className="flex flex-wrap items-center justify-between gap-3 p-4 bg-gradient-to-r from-rose-50/50 to-orange-50/50 dark:from-rose-900/10 dark:to-orange-900/10 rounded-2xl shadow-sm border border-rose-100 dark:border-rose-900/30">
                <div className="flex items-center gap-2 flex-1 min-w-[240px] px-3 py-2 bg-white dark:bg-slate-900/40 rounded-xl border border-slate-200 dark:border-slate-700 focus-within:ring-2 focus-within:ring-rose-500/20 transition-all">
                  <Search size={16} className="text-slate-400" />
                  <input
                    type="text"
                    placeholder={t('knowledge.trash.searchCategoriesPlaceholder')}
                    value={trashKeyword}
                    onChange={(e) => handleTrashSearchChange(e.target.value, 'categories')}
                    className="flex-1 px-1 py-0.5 text-sm bg-transparent border-0 focus:outline-none placeholder:text-slate-400"
                  />
                </div>
                <button
                  onClick={() => setClearTrashTarget('categories')}
                  className="group flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-white bg-gradient-to-r from-rose-600 to-red-600 hover:from-rose-700 hover:to-red-700 rounded-xl transition-all shadow-md shadow-rose-500/20 hover:shadow-lg hover:shadow-rose-500/30 active:scale-[0.98]"
                >
                  <Eraser size={14} className="group-hover:rotate-12 transition-transform" />
                  {t('knowledge.common.emptyTrash')}
                </button>
              </div>

              {/* 分类垃圾箱列表 */}
              <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-sm overflow-hidden border border-slate-100 dark:border-slate-700">
                {loadingTrash ? (
                  <div className="p-12 text-center text-slate-500 dark:text-slate-400">
                    <div className="flex items-center justify-center gap-2">
                      <RefreshCw className="w-5 h-5 animate-spin text-blue-500" />
                      <span>{t('common.shell.loading')}</span>
                    </div>
                  </div>
                ) : !trashCategories || trashCategories.data.length === 0 ? (
                  <div className="p-16 text-center text-slate-500 dark:text-slate-400">
                    <div className="w-24 h-24 rounded-2xl bg-gradient-to-br from-amber-50 to-orange-50 dark:from-amber-900/20 dark:to-orange-900/20 flex items-center justify-center mx-auto mb-4 ring-1 ring-amber-100 dark:ring-amber-900/30">
                      <Recycle size={40} className="text-amber-400" />
                    </div>
                    <h3 className="text-lg font-semibold text-slate-700 dark:text-slate-300 mb-1">{t('knowledge.trash.emptyCategories')}</h3>
                    <p className="text-sm text-slate-400 dark:text-slate-500">{t('knowledge.trash.retentionCategories')}</p>
                  </div>
                ) : (
                  <>
                  <div className="hidden md:block overflow-x-auto">
                    <table className="w-full text-sm">
                      <thead className="bg-slate-50/80 dark:bg-slate-900/40 text-slate-500 dark:text-slate-400 uppercase text-xs border-b border-slate-200 dark:border-slate-700">
                        <tr>
                          <th className="px-6 py-3.5 text-left font-semibold tracking-wider">{t('knowledge.common.col.name')}</th>
                          <th className="px-6 py-3.5 text-left font-semibold tracking-wider">{t('knowledge.common.col.description')}</th>
                          <th className="px-6 py-3.5 text-left font-semibold tracking-wider">{t('knowledge.common.col.deletedAt')}</th>
                          <th className="px-6 py-3.5 text-right font-semibold tracking-wider">{t('knowledge.common.col.actions')}</th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-slate-100 dark:divide-slate-700/70">
                        {trashCategories.data.map((c) => (
                          <tr key={c.id} className="hover:bg-rose-50/30 dark:hover:bg-slate-700/30 transition-colors duration-150">
                            <td className="px-6 py-4">
                              <div className="flex items-center gap-2">
                                <span className="text-lg">{c.icon || '📁'}</span>
                                <span className="font-medium text-slate-800 dark:text-slate-200">
                                  {c.name}
                                </span>
                              </div>
                            </td>
                            <td className="px-6 py-4 text-slate-600 dark:text-slate-300">
                              {c.description || <span className="text-slate-400">-</span>}
                            </td>
                            <td className="px-6 py-4 text-xs text-slate-500 dark:text-slate-400 whitespace-nowrap">
                              <div className="flex items-center gap-1.5">
                                <Clock size={12} className="opacity-50" />
                                {formatDateTime(c.updatedAt)}
                              </div>
                            </td>
                            <td className="px-6 py-4 text-right">
                              <div className="flex items-center justify-end gap-1">
                                <button
                                  onClick={() => setRestoreCategoryTarget(c)}
                                  className="p-2 rounded-lg hover:bg-green-50 dark:hover:bg-green-900/30 text-green-600 dark:text-green-400 transition-colors"
                                  title={t('knowledge.common.restore')}
                                >
                                  <RotateCcw size={15} />
                                </button>
                                <button
                                  onClick={() => setHardDeleteCategoryTarget(c)}
                                  className="p-2 rounded-lg hover:bg-rose-50 dark:hover:bg-rose-900/30 text-rose-600 dark:text-rose-400 transition-colors"
                                  title={t('knowledge.common.hardDelete')}
                                >
                                  <Eraser size={15} />
                                </button>
                              </div>
                            </td>
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>

                  {/* 移动端卡片视图 */}
                  <div className="md:hidden divide-y divide-slate-100 dark:divide-slate-700/70">
                    {trashCategories.data.map((c) => (
                      <div key={c.id} className="p-3.5 hover:bg-rose-50/30 dark:hover:bg-slate-700/30 transition-colors duration-150">
                        <div className="flex items-start justify-between gap-2 mb-1.5">
                          <div className="font-medium text-slate-800 dark:text-slate-200 flex-1 min-w-0 flex items-center gap-2">
                            <span className="text-lg">{c.icon || '📁'}</span>
                            <span className="truncate">{c.name}</span>
                          </div>
                          <div className="text-xs text-slate-400 flex-shrink-0 whitespace-nowrap">
                            {formatDateTime(c.updatedAt)}
                          </div>
                        </div>
                        {c.description ? (
                          <div className="text-xs text-slate-400 dark:text-slate-500 truncate mb-2">
                            {c.description}
                          </div>
                        ) : (
                          <div className="text-xs text-slate-400 mb-2">-</div>
                        )}
                        <div className="flex items-center gap-2 mb-2.5">
                          <span className="inline-flex items-center gap-1 px-2 py-0.5 text-xs rounded-md bg-slate-100 dark:bg-slate-700/70 text-slate-600 dark:text-slate-300">
                            <FileText size={11} className="opacity-60" />
                            {t('knowledge.categories.articleCountLong', { count: c.articleCount })}
                          </span>
                        </div>
                        <div className="flex items-center gap-2">
                          <button
                            onClick={() => setRestoreCategoryTarget(c)}
                            className="flex-1 flex items-center justify-center gap-1.5 px-3 py-2 text-sm font-medium text-green-700 dark:text-green-400 bg-green-50 dark:bg-green-900/30 hover:bg-green-100 dark:hover:bg-green-900/50 rounded-lg transition-colors"
                            title={t('knowledge.common.restore')}
                          >
                            <RotateCcw size={14} />
                            {t('knowledge.common.restore')}
                          </button>
                          <button
                            onClick={() => setHardDeleteCategoryTarget(c)}
                            className="flex-1 flex items-center justify-center gap-1.5 px-3 py-2 text-sm font-medium text-rose-700 dark:text-rose-400 bg-rose-50 dark:bg-rose-900/30 hover:bg-rose-100 dark:hover:bg-rose-900/50 rounded-lg transition-colors"
                            title={t('knowledge.common.hardDelete')}
                          >
                            <Eraser size={14} />
                            {t('knowledge.common.hardDelete')}
                          </button>
                        </div>
                      </div>
                    ))}
                  </div>
                  <Pagination
                    currentPage={trashCategories.currentPage}
                    totalPages={trashCategories.totalPages}
                    totalItems={trashCategories.totalItems}
                    onPageChange={(p) => { setTrashPage(p); refreshTrashCategories(p, trashKeyword); }}
                  />
                  </>
                )}
              </div>
            </>
          ) : (
            <>
              <div className="flex items-center justify-between p-4 bg-white dark:bg-slate-800 rounded-2xl shadow-sm border border-slate-100 dark:border-slate-700 gap-4 flex-wrap">
                <div className="flex items-center gap-3 text-sm text-slate-600 dark:text-slate-300 flex-1 min-w-0">
                  <div className="w-10 h-10 rounded-xl bg-gradient-to-br from-blue-50 to-indigo-50 dark:from-blue-900/20 dark:to-indigo-900/20 flex items-center justify-center flex-shrink-0 ring-1 ring-blue-100 dark:ring-blue-900/30">
                    <Layers size={18} className="text-blue-500" />
                  </div>
                  <span className="text-xs sm:text-sm">{t('knowledge.categories.description')}</span>
                </div>
                <div className="flex items-center gap-2">
                  {allCategoryIdsWithChildren.length > 0 && (
                    <button
                      onClick={toggleAllCategoriesExpand}
                      className="flex items-center gap-1.5 px-3 py-2 text-sm font-medium text-slate-600 dark:text-slate-300 hover:text-blue-600 dark:hover:text-blue-400 hover:bg-blue-50 dark:hover:bg-blue-900/20 rounded-xl transition-all border border-slate-200 dark:border-slate-600 hover:border-blue-300 dark:hover:border-blue-700"
                      title={allCategoriesExpanded ? t('knowledge.categories.collapseAll') : t('knowledge.categories.expandAll')}
                    >
                      {allCategoriesExpanded ? (
                        <>
                          <ChevronDown size={16} />
                          <span className="hidden sm:inline">{t('knowledge.categories.collapseAll')}</span>
                        </>
                      ) : (
                        <>
                          <ChevronRight size={16} />
                          <span className="hidden sm:inline">{t('knowledge.categories.expandAll')}</span>
                        </>
                      )}
                    </button>
                  )}
                  {isSuperAdmin && (
                    <button
                      onClick={() => openCreateCategory(null)}
                      className="group flex items-center gap-1.5 px-4 py-2 text-sm font-medium text-white bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-700 hover:to-indigo-700 rounded-xl transition-all shadow-md shadow-blue-500/20 hover:shadow-lg hover:shadow-blue-500/30 active:scale-[0.98]"
                    >
                      <Plus size={16} className="group-hover:rotate-90 transition-transform duration-300" />
                      {t('knowledge.categories.newCategory')}
                    </button>
                  )}
                </div>
              </div>

              <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-sm p-2 border border-slate-100 dark:border-slate-700">
                {loadingCategories ? (
                  <div className="p-2 space-y-1">
                    {[0, 1, 2, 3, 4].map((i) => (
                      <div key={i} className="flex items-center gap-2 px-3 py-3 rounded-xl animate-pulse">
                        <div className="w-5 h-5 bg-slate-200 dark:bg-slate-700 rounded" />
                        <div className="w-9 h-9 bg-slate-200 dark:bg-slate-700 rounded-lg" />
                        <div className="flex-1 space-y-1.5">
                          <div className="h-3.5 bg-slate-200 dark:bg-slate-700 rounded w-1/3" />
                          <div className="h-2.5 bg-slate-100 dark:bg-slate-800 rounded w-1/2" />
                        </div>
                        <div className="h-5 w-10 bg-slate-100 dark:bg-slate-800 rounded-full" />
                      </div>
                    ))}
                  </div>
                ) : categories.length === 0 ? (
                  <div className="p-12 text-center text-slate-500 dark:text-slate-400">
                    <div className="w-24 h-24 rounded-2xl bg-gradient-to-br from-amber-50 to-orange-50 dark:from-amber-900/20 dark:to-orange-900/20 flex items-center justify-center mx-auto mb-4 ring-1 ring-amber-100 dark:ring-amber-900/30">
                      <Folder size={40} className="text-amber-400" />
                    </div>
                    <h3 className="text-lg font-semibold text-slate-700 dark:text-slate-300 mb-1">{t('knowledge.categories.empty')}</h3>
                    <p className="text-sm mb-5 text-slate-400 dark:text-slate-500">{t('knowledge.categories.emptyHint')}</p>
                    {isSuperAdmin && (
                      <button
                        onClick={() => openCreateCategory(null)}
                        className="inline-flex items-center gap-2 px-5 py-2.5 text-sm font-medium text-white bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-700 hover:to-indigo-700 rounded-xl transition-all shadow-md shadow-blue-500/20 hover:shadow-lg hover:shadow-blue-500/30"
                      >
                        <Plus size={16} />
                        {t('knowledge.categories.newCategory')}
                      </button>
                    )}
                  </div>
                ) : (
                  <div>{renderCategoryTree(categories)}</div>
                )}
              </div>
            </>
          )}
        </div>
      )}

      {/* 热门文章与最近文章 */}
      {tab === 'articles' && !showTrash && overview && (overview.popularArticles.length > 0 || overview.recentArticles.length > 0) && (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
          {overview.recentArticles.length > 0 && (
            <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-sm p-5 border border-slate-100 dark:border-slate-700 hover:shadow-md transition-shadow">
              <div className="flex items-center gap-2 mb-4 text-slate-700 dark:text-slate-200 font-semibold">
                <div className="w-9 h-9 rounded-xl bg-gradient-to-br from-blue-50 to-indigo-50 dark:from-blue-900/20 dark:to-indigo-900/20 flex items-center justify-center ring-1 ring-blue-100 dark:ring-blue-900/30">
                  <Clock size={16} className="text-blue-500" />
                </div>
                <span>{t('knowledge.list.recentTitle')}</span>
                <span className="ml-auto text-xs text-slate-400 font-normal">{t('knowledge.list.recentSubtitle')}</span>
              </div>
              <ul className="space-y-1">
                {overview.recentArticles.slice(0, 5).map((a, i) => (
                  <li
                    key={a.id}
                    className="text-sm cursor-pointer hover:bg-blue-50/50 dark:hover:bg-slate-700/30 rounded-lg p-2 -mx-1 transition-colors flex items-center gap-3 group"
                    onClick={() => handleViewArticle(a)}
                  >
                    <span className="w-6 h-6 rounded-full bg-slate-100 dark:bg-slate-700 text-slate-500 dark:text-slate-400 flex items-center justify-center text-xs font-medium flex-shrink-0 group-hover:bg-blue-100 dark:group-hover:bg-blue-900/40 group-hover:text-blue-600 dark:group-hover:text-blue-400 transition-colors">
                      {i + 1}
                    </span>
                    <span className="truncate flex-1 group-hover:text-blue-600 dark:group-hover:text-blue-400 transition-colors">
                      {a.title}
                    </span>
                    <span className="text-xs text-slate-400 flex-shrink-0 flex items-center gap-1">
                      <Calendar size={11} />
                      {formatDate(a.createdAt)}
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          )}
          {overview.popularArticles.length > 0 && (
            <div className="bg-white dark:bg-slate-800 rounded-2xl shadow-sm p-5 border border-slate-100 dark:border-slate-700 hover:shadow-md transition-shadow">
              <div className="flex items-center gap-2 mb-4 text-slate-700 dark:text-slate-200 font-semibold">
                <div className="w-9 h-9 rounded-xl bg-gradient-to-br from-orange-50 to-amber-50 dark:from-orange-900/20 dark:to-amber-900/20 flex items-center justify-center ring-1 ring-orange-100 dark:ring-orange-900/30">
                  <TrendingUp size={16} className="text-orange-500" />
                </div>
                <span>{t('knowledge.list.popularTitle')}</span>
                <span className="ml-auto text-xs text-slate-400 font-normal">{t('knowledge.list.popularSubtitle')}</span>
              </div>
              <ul className="space-y-1">
                {overview.popularArticles.slice(0, 5).map((a, i) => (
                  <li
                    key={a.id}
                    className="text-sm cursor-pointer hover:bg-orange-50/50 dark:hover:bg-slate-700/30 rounded-lg p-2 -mx-1 transition-colors flex items-center gap-3 group"
                    onClick={() => handleViewArticle(a)}
                  >
                    <span className={`w-6 h-6 rounded-full flex items-center justify-center text-xs font-bold flex-shrink-0 transition-colors ${
                      i === 0
                        ? 'bg-gradient-to-br from-amber-400 to-orange-500 text-white shadow-md shadow-amber-500/30'
                        : i === 1
                        ? 'bg-gradient-to-br from-slate-300 to-slate-400 text-white'
                        : i === 2
                        ? 'bg-gradient-to-br from-orange-300 to-amber-400 text-white'
                        : 'bg-slate-100 dark:bg-slate-700 text-slate-500 dark:text-slate-400 group-hover:bg-orange-100 dark:group-hover:bg-orange-900/40 group-hover:text-orange-600 dark:group-hover:text-orange-400'
                    }`}>
                      {i + 1}
                    </span>
                    <span className="truncate flex-1 group-hover:text-orange-600 dark:group-hover:text-orange-400 transition-colors">
                      {a.title}
                    </span>
                    <span className="text-xs text-slate-400 flex items-center gap-1 flex-shrink-0">
                      <Eye size={11} /> {a.viewCount}
                    </span>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </div>
      )}

      {/* 文章编辑器 */}
      {showArticleEditor && (
        <Modal
          open={showArticleEditor}
          onClose={() => setShowArticleEditor(false)}
          title={
            <div className="flex items-center gap-2">
              <div className="w-8 h-8 rounded-lg bg-gradient-to-br from-blue-500 to-indigo-600 flex items-center justify-center">
                <FileText size={16} className="text-white" />
              </div>
              {editingArticle ? t('knowledge.common.editArticle') : t('knowledge.common.newArticle')}
            </div>
          }
          size="xl"
        >
          <div className="space-y-4">
            {articleFormError && (
              <div className="px-4 py-3 text-sm text-rose-700 bg-rose-50 dark:bg-rose-900/30 dark:text-rose-300 rounded-xl flex items-center gap-2 border border-rose-200 dark:border-rose-800/40">
                <AlertTriangle size={16} className="flex-shrink-0" />
                {articleFormError}
              </div>
            )}
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">
                {t('knowledge.editor.titleLabel')} <span className="text-rose-500">*</span>
              </label>
              <input
                type="text"
                value={articleForm.title}
                onChange={(e) => setArticleForm({ ...articleForm, title: e.target.value })}
                placeholder={t('knowledge.editor.titlePlaceholder')}
                className="w-full px-4 py-3 text-base border border-slate-200 dark:border-slate-600 rounded-xl bg-white dark:bg-slate-800 text-slate-800 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500/30 focus:border-blue-500 transition-all placeholder:text-slate-400"
              />
            </div>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">
                  {t('knowledge.editor.categoryLabel')}
                </label>
                <select
                  value={articleForm.categoryId ?? ''}
                  onChange={(e) =>
                    setArticleForm({
                      ...articleForm,
                      categoryId: e.target.value ? Number(e.target.value) : null,
                    })
                  }
                  className="w-full px-3 py-2.5 text-sm border border-slate-200 dark:border-slate-600 rounded-xl bg-white dark:bg-slate-800 text-slate-800 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500/30 focus:border-blue-500 transition-all cursor-pointer"
                >
                  <option value="">{t('knowledge.common.noCategory')}</option>
                  {activeFlatCategories.map((c) => (
                    <option key={c.id} value={c.id}>
                      {categoryIndent(c.depth)}{c.name}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">
                  {t('knowledge.editor.statusLabel')}
                </label>
                <div className="flex gap-2">
                  {STATUS_OPTIONS.map((s) => {
                    const Icon = s.icon;
                    return (
                      <button
                        key={s.value}
                        onClick={() => setArticleForm({ ...articleForm, status: s.value })}
                        className={`flex-1 flex items-center justify-center gap-1.5 px-3 py-2 text-sm rounded-xl border transition-all ${
                          articleForm.status === s.value
                            ? 'border-blue-500 bg-blue-50 dark:bg-blue-900/20 text-blue-600 dark:text-blue-400 shadow-sm shadow-blue-500/20'
                            : 'border-slate-200 dark:border-slate-600 text-slate-600 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700'
                        }`}
                      >
                        <Icon size={14} className={s.color} />
                        {t(s.labelKey)}
                      </button>
                    );
                  })}
                </div>
              </div>
            </div>
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">
                {t('knowledge.editor.summaryLabel')}
              </label>
              <input
                type="text"
                value={articleForm.summary}
                onChange={(e) => setArticleForm({ ...articleForm, summary: e.target.value })}
                placeholder={t('knowledge.editor.summaryPlaceholder')}
                className="w-full px-3 py-2.5 text-sm border border-slate-200 dark:border-slate-600 rounded-xl bg-white dark:bg-slate-800 text-slate-800 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500/30 focus:border-blue-500 transition-all placeholder:text-slate-400"
              />
            </div>
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">
                <Tag size={14} className="inline mr-1" />
                {t('knowledge.editor.tagsLabel')}
              </label>
              <input
                type="text"
                value={articleForm.tags}
                onChange={(e) => setArticleForm({ ...articleForm, tags: e.target.value })}
                placeholder={t('knowledge.editor.tagsPlaceholder')}
                className="w-full px-3 py-2.5 text-sm border border-slate-200 dark:border-slate-600 rounded-xl bg-white dark:bg-slate-800 text-slate-800 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500/30 focus:border-blue-500 transition-all placeholder:text-slate-400"
              />
            </div>

            {/* 内容编辑器区域（支持 Markdown / HTML 双模式） */}
            <div>
              <div className="flex items-center justify-between mb-2 flex-wrap gap-2">
                <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300">
                  {t('knowledge.editor.contentLabelBefore')}<span className="text-blue-600 dark:text-blue-400">{articleForm.contentType === 'html' ? 'HTML' : 'Markdown'}</span>{t('knowledge.editor.contentLabelAfter')}<span className="text-rose-500">*</span>
                </label>
                <div className="flex items-center gap-2">
                  {/* 内容格式切换 */}
                  <div className="flex items-center rounded-lg bg-slate-100 dark:bg-slate-700/60 p-0.5 border border-slate-200 dark:border-slate-600">
                    {CONTENT_TYPE_OPTIONS.map((opt) => {
                      const Icon = opt.icon;
                      return (
                        <button
                          key={opt.value}
                          onClick={() => handleContentTypeChange(opt.value)}
                          className={`flex items-center gap-1 px-2.5 py-1 text-xs rounded-md transition-all ${
                            articleForm.contentType === opt.value
                              ? 'bg-white dark:bg-slate-800 text-blue-600 dark:text-blue-400 shadow-sm font-medium'
                              : 'text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200'
                          }`}
                          title={t('knowledge.editor.contentSwitchTitle', { format: opt.label })}
                        >
                          <Icon size={12} /> {opt.label}
                        </button>
                      );
                    })}
                  </div>
                  <span className={`text-xs ${articleForm.content.length > MAX_ARTICLE_CONTENT ? 'text-rose-500 dark:text-rose-400 font-semibold' : 'text-slate-400'}`}>
                    {t('knowledge.editor.stats', { words: formatCount(articleWordCount, language), max: formatCount(MAX_ARTICLE_CONTENT, language), minutes: articleReadTime })}
                  </span>
                  {/* 编辑/预览切换 - iOS 风格 */}
                  <div className="flex items-center rounded-lg bg-slate-100 dark:bg-slate-700/60 p-0.5 border border-slate-200 dark:border-slate-600">
                    <button
                      onClick={() => setEditorMode('edit')}
                      className={`flex items-center gap-1 px-3 py-1 text-xs rounded-md transition-all ${
                        editorMode === 'edit'
                          ? 'bg-white dark:bg-slate-800 text-blue-600 dark:text-blue-400 shadow-sm font-medium'
                          : 'text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200'
                      }`}
                    >
                      <Pencil size={12} /> {t('knowledge.common.edit')}
                    </button>
                    <button
                      onClick={() => setEditorMode('preview')}
                      className={`flex items-center gap-1 px-3 py-1 text-xs rounded-md transition-all ${
                        editorMode === 'preview'
                          ? 'bg-white dark:bg-slate-800 text-blue-600 dark:text-blue-400 shadow-sm font-medium'
                          : 'text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200'
                      }`}
                    >
                      <EyeIcon size={12} /> {t('knowledge.editor.previewMode')}
                    </button>
                  </div>
                </div>
              </div>

              {/* 字数进度条 */}
              {articleForm.content.length > 0 && (
                <div className="h-1 bg-slate-100 dark:bg-slate-700 rounded-full overflow-hidden mb-2">
                  <div
                    className={`h-full transition-all duration-300 ${
                      articleForm.content.length > MAX_ARTICLE_CONTENT * 0.9
                        ? 'bg-gradient-to-r from-rose-500 to-red-500'
                        : articleForm.content.length > MAX_ARTICLE_CONTENT * 0.7
                        ? 'bg-gradient-to-r from-amber-400 to-orange-500'
                        : 'bg-gradient-to-r from-blue-500 to-indigo-500'
                    }`}
                    style={{ width: `${Math.min(100, (articleForm.content.length / MAX_ARTICLE_CONTENT) * 100)}%` }}
                  />
                </div>
              )}

              {/* Markdown 工具栏（仅 Markdown 模式显示） */}
              {editorMode === 'edit' && articleForm.contentType === 'markdown' && (
                <div className="flex items-center gap-0.5 px-2 py-1.5 border border-b-0 border-slate-200 dark:border-slate-600 rounded-t-xl bg-slate-50 dark:bg-slate-900/40">
                  <button onClick={() => insertMarkdown('# ', '', t('knowledge.editor.md.heading1'))} className="p-1.5 rounded hover:bg-white dark:hover:bg-slate-700 hover:text-blue-600 dark:hover:text-blue-400 text-slate-600 dark:text-slate-300 transition-all" title={t('knowledge.editor.md.heading1')}>
                    <Heading1 size={15} />
                  </button>
                  <button onClick={() => insertMarkdown('## ', '', t('knowledge.editor.md.heading2'))} className="p-1.5 rounded hover:bg-white dark:hover:bg-slate-700 hover:text-blue-600 dark:hover:text-blue-400 text-slate-600 dark:text-slate-300 transition-all" title={t('knowledge.editor.md.heading2')}>
                    <Heading2 size={15} />
                  </button>
                  <button onClick={() => insertMarkdown('### ', '', t('knowledge.editor.md.heading3'))} className="p-1.5 rounded hover:bg-white dark:hover:bg-slate-700 hover:text-blue-600 dark:hover:text-blue-400 text-slate-600 dark:text-slate-300 transition-all" title={t('knowledge.editor.md.heading3')}>
                    <Heading3 size={15} />
                  </button>
                  <div className="w-px h-5 bg-slate-200 dark:bg-slate-600 mx-1" />
                  <button onClick={() => insertMarkdown('**', '**', t('knowledge.editor.md.boldPlaceholder'))} className="p-1.5 rounded hover:bg-white dark:hover:bg-slate-700 hover:text-blue-600 dark:hover:text-blue-400 text-slate-600 dark:text-slate-300 transition-all" title={t('knowledge.editor.md.boldTitle')}>
                    <Bold size={15} />
                  </button>
                  <button onClick={() => insertMarkdown('*', '*', t('knowledge.editor.md.italicPlaceholder'))} className="p-1.5 rounded hover:bg-white dark:hover:bg-slate-700 hover:text-blue-600 dark:hover:text-blue-400 text-slate-600 dark:text-slate-300 transition-all" title={t('knowledge.editor.md.italicTitle')}>
                    <Italic size={15} />
                  </button>
                  <div className="w-px h-5 bg-slate-200 dark:bg-slate-600 mx-1" />
                  <button onClick={() => insertMarkdown('`', '`', t('knowledge.editor.md.inlineCodePlaceholder'))} className="p-1.5 rounded hover:bg-white dark:hover:bg-slate-700 hover:text-blue-600 dark:hover:text-blue-400 text-slate-600 dark:text-slate-300 transition-all" title={t('knowledge.editor.md.inlineCodeTitle')}>
                    <Code size={15} />
                  </button>
                  <button onClick={() => insertMarkdown('```\n', '\n```', t('knowledge.editor.md.codeBlockPlaceholder'))} className="p-1.5 rounded hover:bg-white dark:hover:bg-slate-700 hover:text-blue-600 dark:hover:text-blue-400 text-slate-600 dark:text-slate-300 transition-all" title={t('knowledge.editor.md.codeBlockTitle')}>
                    <Hash size={15} />
                  </button>
                  <div className="w-px h-5 bg-slate-200 dark:bg-slate-600 mx-1" />
                  <button onClick={() => insertMarkdown('- ', '', t('knowledge.editor.md.listItemPlaceholder'))} className="p-1.5 rounded hover:bg-white dark:hover:bg-slate-700 hover:text-blue-600 dark:hover:text-blue-400 text-slate-600 dark:text-slate-300 transition-all" title={t('knowledge.editor.md.unorderedListTitle')}>
                    <List size={15} />
                  </button>
                  <button onClick={() => insertMarkdown('> ', '', t('knowledge.editor.md.quotePlaceholder'))} className="p-1.5 rounded hover:bg-white dark:hover:bg-slate-700 hover:text-blue-600 dark:hover:text-blue-400 text-slate-600 dark:text-slate-300 transition-all" title={t('knowledge.editor.md.quoteTitle')}>
                    <Quote size={15} />
                  </button>
                  <div className="w-px h-5 bg-slate-200 dark:bg-slate-600 mx-1" />
                  <button onClick={() => insertMarkdown('[', '](https://)', t('knowledge.editor.md.linkPlaceholder'))} className="p-1.5 rounded hover:bg-white dark:hover:bg-slate-700 hover:text-blue-600 dark:hover:text-blue-400 text-slate-600 dark:text-slate-300 transition-all" title={t('knowledge.editor.md.linkTitle')}>
                    <LinkIcon size={15} />
                  </button>
                  <button onClick={() => insertMarkdown('![', '](https://)', t('knowledge.editor.md.imagePlaceholder'))} className="p-1.5 rounded hover:bg-white dark:hover:bg-slate-700 hover:text-blue-600 dark:hover:text-blue-400 text-slate-600 dark:text-slate-300 transition-all" title={t('knowledge.editor.md.imageTitle')}>
                    <Image size={15} />
                  </button>
                </div>
              )}

              {/* HTML 模式提示条 */}
              {editorMode === 'edit' && articleForm.contentType === 'html' && (
                <div className="flex items-center gap-2 px-3 py-1.5 border border-b-0 border-slate-200 dark:border-slate-600 rounded-t-xl bg-violet-50 dark:bg-violet-900/20 text-xs text-violet-700 dark:text-violet-300">
                  <FileType size={13} />
                  <span>{t('knowledge.editor.htmlHint')}</span>
                </div>
              )}

              {editorMode === 'edit' ? (
                <textarea
                  ref={articleContentRef}
                  value={articleForm.content}
                  onChange={(e) => setArticleForm({ ...articleForm, content: e.target.value })}
                  onPaste={handleEditorPaste}
                  onKeyDown={handleArticleEditorKeyDown}
                  placeholder={articleForm.contentType === 'html'
                    ? t('knowledge.editor.htmlPlaceholder')
                    : t('knowledge.editor.markdownPlaceholder')}
                  rows={14}
                  maxLength={MAX_ARTICLE_CONTENT}
                  className={`w-full px-3 py-2.5 text-sm font-mono border border-slate-200 dark:border-slate-600 ${articleForm.contentType === 'html' ? 'rounded-b-xl' : editorMode === 'edit' && articleForm.contentType === 'markdown' ? 'rounded-b-xl' : 'rounded-xl'} bg-white dark:bg-slate-800 text-slate-800 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500/30 focus:border-blue-500 transition-all resize-y placeholder:text-slate-400`}
                />
              ) : (
                <div className="border border-slate-200 dark:border-slate-600 rounded-xl bg-white dark:bg-slate-800 min-h-[300px] max-h-[500px] overflow-y-auto p-6">
                  {articleForm.content ? (
                    <div
                      className="prose prose-slate dark:prose-invert max-w-none text-slate-700 dark:text-slate-300 text-sm knowledge-content"
                      dangerouslySetInnerHTML={{ __html: renderContent(articleForm.content, articleForm.contentType) }}
                    />
                  ) : (
                    <div className="text-center text-slate-400 dark:text-slate-500 py-12">
                      <FileText size={32} className="mx-auto mb-2 opacity-40" />
                      <p className="text-sm">{t('knowledge.editor.previewEmpty')}</p>
                    </div>
                  )}
                </div>
              )}
            </div>

            {/* 附件管理面板（PDF/图片/文档） */}
            <div>
              <div className="flex items-center justify-between mb-2">
                <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300">
                  <Paperclip size={14} className="inline mr-1" />
                  {t('knowledge.attachments.sectionLabel')}
                </label>
                <span className="text-xs text-slate-400">
                  {t('knowledge.attachments.formatsHint', { limit: formatAttachmentSize(ATTACHMENT_MAX_SIZE) })}
                </span>
              </div>
              {attachmentError && (
                <div className="px-3 py-2 mb-2 text-xs text-rose-700 bg-rose-50 dark:bg-rose-900/30 dark:text-rose-300 rounded-lg border border-rose-200 dark:border-rose-800/40">
                  {attachmentError}
                </div>
              )}
              <input
                ref={attachmentFileInputRef}
                type="file"
                className="hidden"
                onChange={(e) => {
                  const f = e.target.files?.[0];
                  if (f) handleUploadAttachment(f);
                }}
                accept=".pdf,.png,.jpg,.jpeg,.gif,.webp,.svg,.bmp,.doc,.docx,.xls,.xlsx,.ppt,.pptx,.txt,.csv,.zip,.rar,.7z"
              />
              {editingArticle ? (
                <div className="space-y-2">
                  {/* 上传按钮 */}
                  <button
                    onClick={handleAttachmentSelect}
                    disabled={uploadingAttachment}
                    className="w-full flex items-center justify-center gap-2 px-4 py-3 text-sm font-medium text-slate-600 dark:text-slate-300 border-2 border-dashed border-slate-300 dark:border-slate-600 rounded-xl hover:border-blue-400 dark:hover:border-blue-500 hover:bg-blue-50/50 dark:hover:bg-blue-900/20 hover:text-blue-600 dark:hover:text-blue-400 transition-all disabled:opacity-50 disabled:cursor-not-allowed"
                  >
                    {uploadingAttachment ? (
                      <>
                        <Loader2 size={16} className="animate-spin" /> {t('knowledge.attachments.uploading')}
                      </>
                    ) : (
                      <>
                        <Upload size={16} /> {t('knowledge.attachments.uploadButton')}
                      </>
                    )}
                  </button>
                  {/* 已上传附件列表 */}
                  {editorAttachments.length > 0 && (
                    <div className="space-y-2">
                      <div className="flex items-center gap-2 pt-1">
                        <div className="w-1.5 h-1.5 rounded-full bg-slate-400" />
                        <span className="text-xs font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wide">
                          {t('knowledge.attachments.uploadedList', { count: editorAttachments.length })}
                        </span>
                      </div>
                      {editorAttachments.map((att) => {
                        const AttIcon = getAttachmentIcon(att);
                        return (
                          <div
                            key={att.id}
                            className="group flex items-center gap-3 px-3.5 py-2.5 rounded-xl bg-slate-50 dark:bg-slate-700/40 border border-slate-200 dark:border-slate-600 hover:border-blue-300 dark:hover:border-blue-700 hover:shadow-sm transition-all"
                          >
                            <div className={`w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0 ${att.isPdf ? 'bg-red-50 dark:bg-red-900/30 text-red-600 dark:text-red-400' : att.isImage ? 'bg-blue-50 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400' : 'bg-slate-100 dark:bg-slate-600 text-slate-500 dark:text-slate-400'}`}>
                              <AttIcon size={16} />
                            </div>
                            <div className="flex-1 min-w-0">
                              <div className="text-sm text-slate-700 dark:text-slate-200 truncate font-medium">
                                {att.fileName}
                              </div>
                              <div className="text-xs text-slate-400 mt-0.5">
                                {att.fileSizeDisplay || formatAttachmentSize(att.fileSize)} · {att.extension.toUpperCase()}
                                {att.isPdf && <span className="ml-1 text-red-500 font-medium">{t('knowledge.attachments.pdfPreviewable')}</span>}
                                {att.isImage && <span className="ml-1 text-blue-500 font-medium">{t('knowledge.attachments.imagePreviewable')}</span>}
                              </div>
                            </div>
                            <div className="flex items-center gap-1.5 flex-shrink-0">
                              <button
                                onClick={() => handlePreviewAttachment(att)}
                                className="p-2 rounded-lg text-slate-400 hover:text-blue-600 hover:bg-blue-50 dark:hover:bg-blue-900/30 transition-all"
                                title={t('knowledge.attachments.previewOrDownload')}
                              >
                                <EyeIcon size={15} />
                              </button>
                              <button
                                onClick={() => handleDeleteAttachment(att.id)}
                                className="p-2 rounded-lg text-rose-400 hover:text-rose-600 hover:bg-rose-50 dark:hover:bg-rose-900/30 transition-all"
                                title={t('knowledge.attachments.deleteAttachment')}
                              >
                                <Trash2 size={15} />
                              </button>
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  )}
                </div>
              ) : (
                <div className="px-4 py-3 text-sm text-slate-400 dark:text-slate-500 bg-slate-50 dark:bg-slate-700/40 rounded-xl border border-dashed border-slate-200 dark:border-slate-600 text-center">
                  <AlertTriangle size={14} className="inline mr-1" />
                  {t('knowledge.attachments.newModeHint')}
                </div>
              )}
            </div>

            <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
              <div>
                <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2">
                  {t('knowledge.common.sortLabel')}
                </label>
                <input
                  type="number"
                  value={articleForm.sortOrder}
                  onChange={(e) =>
                    setArticleForm({ ...articleForm, sortOrder: Number(e.target.value) || 0 })
                  }
                  className="w-full px-3 py-2.5 text-sm border border-slate-200 dark:border-slate-600 rounded-xl bg-white dark:bg-slate-800 text-slate-800 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500/30"
                />
              </div>
              <div className="md:col-span-2 flex items-end">
                <label className={`flex items-center gap-2 text-sm cursor-pointer p-3 rounded-xl border transition-all flex-1 ${
                  articleForm.isPinned
                    ? 'border-amber-300 dark:border-amber-700 bg-amber-50 dark:bg-amber-900/20 text-amber-700 dark:text-amber-400'
                    : 'border-slate-200 dark:border-slate-600 text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700/30'
                }`}>
                  <input
                    type="checkbox"
                    checked={articleForm.isPinned}
                    onChange={(e) =>
                      setArticleForm({ ...articleForm, isPinned: e.target.checked })
                    }
                    className="rounded text-amber-500 focus:ring-amber-500/20"
                  />
                  <Pin size={14} className={articleForm.isPinned ? 'text-amber-500 fill-amber-500' : ''} />
                  <span className="font-medium">{t('knowledge.editor.pinLabel')}</span>
                  <span className="text-xs text-slate-400 ml-auto">{t('knowledge.editor.pinHint')}</span>
                </label>
              </div>
            </div>
            <div className="flex justify-end gap-2 pt-4 border-t border-slate-100 dark:border-slate-700">
              <button
                onClick={() => setShowArticleEditor(false)}
                className="px-5 py-2.5 text-sm font-medium border border-slate-200 dark:border-slate-600 rounded-xl hover:bg-slate-50 dark:hover:bg-slate-700 transition-all"
              >
                {t('knowledge.common.cancel')}
              </button>
              <button
                onClick={handleSaveArticle}
                disabled={savingArticle}
                className="group flex items-center gap-1.5 px-5 py-2.5 text-sm font-medium text-white bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-700 hover:to-indigo-700 rounded-xl disabled:opacity-50 transition-all shadow-md shadow-blue-500/20 hover:shadow-lg hover:shadow-blue-500/30 active:scale-[0.98]"
              >
                <Save size={14} className={savingArticle ? 'animate-spin' : 'group-hover:scale-110'} transition-all />
                {savingArticle ? t('knowledge.common.saving') : t('knowledge.common.save')}
              </button>
            </div>
          </div>
        </Modal>
      )}

      {/* 分类编辑器 */}
      {showCategoryEditor && (
        <Modal
          open={showCategoryEditor}
          onClose={() => setShowCategoryEditor(false)}
          title={
            <div className="flex items-center gap-2">
              <div className={`w-8 h-8 rounded-lg flex items-center justify-center text-white shadow-md ${editingCategory ? 'bg-gradient-to-br from-amber-500 to-orange-600' : 'bg-gradient-to-br from-blue-500 to-indigo-600'}`}>
                {editingCategory ? <Edit2 size={16} /> : <FolderPlus size={16} />}
              </div>
              {editingCategory ? t('knowledge.categories.editCategory') : t('knowledge.categories.newCategory')}
            </div>
          }
          size="md"
        >
          <div className="space-y-4">
            {categoryFormError && (
              <div className="px-4 py-3 text-sm text-rose-700 bg-rose-50 dark:bg-rose-900/30 dark:text-rose-300 rounded-xl flex items-center gap-2 border border-rose-200 dark:border-rose-800/40 animate-in fade-in duration-200">
                <AlertTriangle size={16} className="flex-shrink-0" />
                {categoryFormError}
              </div>
            )}
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2 flex items-center gap-1.5">
                <Folder size={14} className="text-blue-500" />
                {t('knowledge.categories.nameLabel')} <span className="text-rose-500">*</span>
              </label>
              <input
                type="text"
                value={categoryForm.name}
                onChange={(e) => setCategoryForm({ ...categoryForm, name: e.target.value })}
                placeholder={t('knowledge.categories.namePlaceholder')}
                className="w-full px-4 py-2.5 text-sm border border-slate-200 dark:border-slate-600 rounded-xl bg-white dark:bg-slate-800 text-slate-800 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500/30 focus:border-blue-500 transition-all placeholder:text-slate-400"
              />
            </div>
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2 flex items-center gap-1.5">
                <Layers size={14} className="text-indigo-500" />
                {t('knowledge.categories.parentLabel')}
              </label>
              <select
                value={categoryForm.parentId ?? ''}
                onChange={(e) =>
                  setCategoryForm({
                    ...categoryForm,
                    parentId: e.target.value ? Number(e.target.value) : null,
                  })
                }
                className="w-full px-3 py-2.5 text-sm border border-slate-200 dark:border-slate-600 rounded-xl bg-white dark:bg-slate-800 text-slate-800 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500/30 focus:border-blue-500 transition-all cursor-pointer"
              >
                <option value="">{t('knowledge.categories.parentNone')}</option>
                {flatCategories
                  .filter((c) => c.id !== editingCategory?.id)
                  .map((c) => (
                    <option key={c.id} value={c.id}>
                      {categoryIndent(c.depth)}{c.name}
                    </option>
                  ))}
              </select>
            </div>
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2 flex items-center gap-1.5">
                <FileText size={14} className="text-slate-500" />
                {t('knowledge.categories.descriptionLabel')}
              </label>
              <textarea
                value={categoryForm.description}
                onChange={(e) =>
                  setCategoryForm({ ...categoryForm, description: e.target.value })
                }
                rows={2}
                placeholder={t('knowledge.categories.descriptionPlaceholder')}
                className="w-full px-3 py-2.5 text-sm border border-slate-200 dark:border-slate-600 rounded-xl bg-white dark:bg-slate-800 text-slate-800 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500/30 focus:border-blue-500 transition-all resize-y placeholder:text-slate-400"
              />
            </div>

            {/* 图标选择器 */}
            <div>
              <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2 flex items-center gap-1.5">
                <Sparkles size={14} className="text-amber-500" />
                {t('knowledge.categories.iconLabel')}
              </label>
              <div className="flex items-center gap-2 mb-2">
                <input
                  type="text"
                  value={categoryForm.icon}
                  onChange={(e) => setCategoryForm({ ...categoryForm, icon: e.target.value })}
                  placeholder={t('knowledge.categories.iconPlaceholder')}
                  className="w-32 px-3 py-2 text-sm border border-slate-200 dark:border-slate-600 rounded-xl bg-white dark:bg-slate-800 text-slate-800 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500/30 text-center text-lg"
                />
                <span className="text-xs text-slate-400">{t('knowledge.categories.iconPresetHint')}</span>
              </div>
              <div className="flex flex-wrap gap-1.5 p-2 bg-slate-50 dark:bg-slate-900/30 rounded-xl border border-slate-100 dark:border-slate-700/60">
                {PRESET_ICONS.map((icon) => (
                  <button
                    key={icon}
                    onClick={() => setCategoryForm({ ...categoryForm, icon })}
                    className={`w-9 h-9 rounded-lg text-lg flex items-center justify-center transition-all hover:scale-110 active:scale-95 ${
                      categoryForm.icon === icon
                        ? 'bg-gradient-to-br from-blue-100 to-indigo-100 dark:from-blue-900/40 dark:to-indigo-900/40 ring-2 ring-blue-500 shadow-md'
                        : 'bg-white dark:bg-slate-700/50 hover:bg-blue-50 dark:hover:bg-slate-600 ring-1 ring-slate-200/60 dark:ring-slate-600/40'
                    }`}
                  >
                    {icon}
                  </button>
                ))}
              </div>
            </div>

            <div className="grid grid-cols-2 gap-4">
              <div>
                <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2 flex items-center gap-1.5">
                  <Mail size={14} className="text-cyan-500" />
                  {t('knowledge.categories.mailboxLabel')}
                </label>
                <select
                  value={categoryForm.mailboxId ?? ''}
                  onChange={(e) =>
                    setCategoryForm({
                      ...categoryForm,
                      mailboxId: e.target.value ? Number(e.target.value) : null,
                    })
                  }
                  className="w-full px-3 py-2.5 text-sm border border-slate-200 dark:border-slate-600 rounded-xl bg-white dark:bg-slate-800 text-slate-800 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500/30 focus:border-blue-500 transition-all cursor-pointer"
                >
                  <option value="">{t('knowledge.categories.mailboxAll')}</option>
                  {mailboxes.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.name}
                    </option>
                  ))}
                </select>
              </div>
              <div>
                <label className="block text-sm font-semibold text-slate-700 dark:text-slate-300 mb-2 flex items-center gap-1.5">
                  <Hash size={14} className="text-violet-500" />
                  {t('knowledge.common.sortLabel')}
                </label>
                <input
                  type="number"
                  value={categoryForm.sortOrder}
                  onChange={(e) =>
                    setCategoryForm({
                      ...categoryForm,
                      sortOrder: Number(e.target.value) || 0,
                    })
                  }
                  className="w-full px-3 py-2.5 text-sm border border-slate-200 dark:border-slate-600 rounded-xl bg-white dark:bg-slate-800 text-slate-800 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-blue-500/30 focus:border-blue-500 transition-all"
                />
              </div>
            </div>
            <label className={`flex items-center gap-2 text-sm cursor-pointer p-3 rounded-xl border transition-all ${
              categoryForm.isActive
                ? 'border-emerald-200 dark:border-emerald-800/40 bg-emerald-50 dark:bg-emerald-900/20 text-emerald-700 dark:text-emerald-300'
                : 'border-slate-200 dark:border-slate-600 text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-slate-700/30'
            }`}>
              <input
                type="checkbox"
                checked={categoryForm.isActive}
                onChange={(e) =>
                  setCategoryForm({ ...categoryForm, isActive: e.target.checked })
                }
                className="rounded text-emerald-600 focus:ring-emerald-500/20"
              />
              <CheckCircle2 size={14} className={categoryForm.isActive ? 'text-emerald-500' : 'text-slate-400'} />
              <span className="font-medium">{t('knowledge.categories.enableLabel')}</span>
              <span className="text-xs text-slate-400 ml-auto">{t('knowledge.categories.enableHint')}</span>
            </label>
            <div className="flex justify-end gap-2 pt-4 border-t border-slate-100 dark:border-slate-700">
              <button
                onClick={() => setShowCategoryEditor(false)}
                className="px-5 py-2.5 text-sm font-medium border border-slate-200 dark:border-slate-600 rounded-xl hover:bg-slate-50 dark:hover:bg-slate-700 transition-all"
              >
                {t('knowledge.common.cancel')}
              </button>
              <button
                onClick={handleSaveCategory}
                disabled={savingCategory}
                className="group flex items-center gap-1.5 px-5 py-2.5 text-sm font-medium text-white bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-700 hover:to-indigo-700 rounded-xl disabled:opacity-50 transition-all shadow-md shadow-blue-500/20 hover:shadow-lg hover:shadow-blue-500/30 active:scale-[0.98]"
              >
                <Save size={14} className={savingCategory ? 'animate-spin' : 'group-hover:scale-110 transition-all'} />
                {savingCategory ? t('knowledge.common.saving') : t('knowledge.common.save')}
              </button>
            </div>
          </div>
        </Modal>
      )}

      {/* 文章查看模态 */}
      {viewArticle && (
        <Modal
          open={!!viewArticle}
          onClose={() => setViewArticle(null)}
          title={
            <div className="flex items-center gap-2">
              <div className="w-8 h-8 rounded-lg bg-gradient-to-br from-violet-500 to-purple-600 flex items-center justify-center shadow-md">
                <BookOpen size={16} className="text-white" />
              </div>
              {t('knowledge.view.title')}
            </div>
          }
          size="xl"
        >
          <div className="flex flex-col">
            {/* 顶部工具栏：返回 / 编辑 / 点赞（始终可见，不随正文滚动） */}
            <div className="flex items-center justify-between gap-2 pb-3 mb-2 border-b border-slate-100 dark:border-slate-700">
              <button
                onClick={() => setViewArticle(null)}
                className="group flex items-center gap-1.5 px-3 py-1.5 text-sm text-slate-500 dark:text-slate-400 hover:text-slate-700 dark:hover:text-slate-200 hover:bg-slate-100 dark:hover:bg-slate-700/60 rounded-lg transition-all"
              >
                <ArrowLeft size={15} className="group-hover:-translate-x-0.5 transition-transform" /> {t('knowledge.view.back')}
              </button>
              <div className="flex items-center gap-2">
                <button
                  onClick={() => handleLikeArticle(viewArticle)}
                  className={`group flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-full transition-all active:scale-95 ${
                    viewArticle.isLiked
                      ? 'text-rose-600 dark:text-rose-400 bg-rose-50 dark:bg-rose-900/30 border border-rose-300 dark:border-rose-700/60'
                      : 'text-slate-600 dark:text-slate-300 border border-slate-200 dark:border-slate-600 hover:bg-rose-50 hover:text-rose-600 hover:border-rose-200 dark:hover:bg-rose-900/20 dark:hover:text-rose-400 dark:hover:border-rose-800/40 hover:shadow-md hover:shadow-rose-500/10'
                  }`}
                  title={viewArticle.isLiked ? t('knowledge.view.unlike') : t('knowledge.view.like')}
                >
                  <ThumbsUp size={13} className={`group-hover:scale-110 transition-transform ${viewArticle.isLiked ? 'fill-current' : ''}`} /> {viewArticle.isLiked ? t('knowledge.view.liked') : t('knowledge.view.like')} <span className="text-rose-500 dark:text-rose-400 font-semibold">{viewArticle.likeCount}</span>
                </button>
                {isSuperAdmin && (
                  <button
                    onClick={() => {
                      openEditArticle(viewArticle);
                      setViewArticle(null);
                    }}
                    className="group flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-white bg-gradient-to-r from-blue-600 to-indigo-600 hover:from-blue-700 hover:to-indigo-700 rounded-full transition-all shadow-md shadow-blue-500/20 hover:shadow-lg hover:shadow-blue-500/30 active:scale-95"
                  >
                    <Edit2 size={13} className="group-hover:scale-110 transition-transform" /> {t('knowledge.common.edit')}
                  </button>
                )}
              </div>
            </div>

            {/* 文章头部区：分类 / 标题 / 元信息 / 标签 / 摘要 */}
            <header className="px-1 sm:px-2">
              {viewArticle.categoryName && (
                <div className="mb-3">
                  <span className="inline-flex items-center gap-1 px-2.5 py-1 text-xs font-medium text-blue-700 dark:text-blue-300 bg-blue-50 dark:bg-blue-900/20 rounded-full ring-1 ring-blue-200/60 dark:ring-blue-800/40">
                    <Folder size={12} /> {viewArticle.categoryName}
                  </span>
                </div>
              )}

              <h1 className="text-2xl sm:text-3xl font-bold tracking-tight text-slate-900 dark:text-slate-50 flex items-start gap-2">
                {viewArticle.isPinned && (
                  <span className="inline-flex items-center justify-center w-7 h-7 rounded-lg bg-gradient-to-br from-amber-100 to-orange-100 dark:from-amber-900/40 dark:to-orange-900/40 ring-1 ring-amber-200/60 dark:ring-amber-800/40 flex-shrink-0 mt-1">
                    <Pin size={16} className="text-amber-500 fill-amber-500" />
                  </span>
                )}
                <span className="bg-gradient-to-r from-slate-900 to-slate-700 dark:from-white dark:to-slate-200 bg-clip-text text-transparent">{viewArticle.title}</span>
              </h1>

              {/* 元信息行 */}
              <div className="flex items-center gap-2 mt-4 text-xs text-slate-500 dark:text-slate-400 flex-wrap">
                <span className="flex items-center gap-1 px-2 py-1 rounded-full bg-slate-50 dark:bg-slate-700/40">
                  <User size={12} className="text-slate-400" /> {viewArticle.authorName || t('format.unknown')}
                </span>
                <span className="text-slate-300 dark:text-slate-600">·</span>
                <span className="flex items-center gap-1 px-2 py-1 rounded-full bg-slate-50 dark:bg-slate-700/40">
                  <Calendar size={12} className="text-slate-400" /> {formatDate(viewArticle.publishedAt || viewArticle.createdAt)}
                </span>
                <span className="text-slate-300 dark:text-slate-600">·</span>
                <span className="flex items-center gap-1 px-2 py-1 rounded-full bg-slate-50 dark:bg-slate-700/40">
                  <Clock size={12} className="text-slate-400" /> {t('knowledge.view.readTime', { minutes: estimateReadTime(viewArticle.content) })}
                </span>
                <span className="text-slate-300 dark:text-slate-600">·</span>
                <span className="flex items-center gap-1 px-2 py-1 rounded-full bg-slate-50 dark:bg-slate-700/40">
                  <Eye size={12} className="text-slate-400" /> {t('knowledge.view.views', { count: viewArticle.viewCount })}
                </span>
              </div>

              {/* 状态徽章 + 标签 */}
              {(viewArticle.status !== 'published' || (viewArticle.tagList && viewArticle.tagList.length > 0)) && (
                <div className="flex items-center gap-2 flex-wrap mt-3">
                  {viewArticle.status !== 'published' && (
                    <span className={`px-2.5 py-1 text-xs rounded-full font-medium ${STATUS_COLORS[viewArticle.status] || STATUS_COLORS.draft}`}>
                      {articleStatusLabel(viewArticle.status, viewArticle.statusLabel || viewArticle.status)}
                    </span>
                  )}
                  {viewArticle.tagList && viewArticle.tagList.map((t, i) => (
                    <span
                      key={i}
                      className="px-2 py-0.5 text-xs rounded-full bg-slate-100 dark:bg-slate-700/50 text-slate-600 dark:text-slate-300 hover:bg-blue-100 dark:hover:bg-blue-900/30 hover:text-blue-600 dark:hover:text-blue-300 transition-colors cursor-default"
                    >
                      #{t}
                    </span>
                  ))}
                </div>
              )}

              {/* 摘要 */}
              {viewArticle.summary && (
                <div className="mt-4 px-4 py-3 text-sm text-slate-600 dark:text-slate-300 italic bg-gradient-to-r from-slate-50 to-blue-50/30 dark:from-slate-700/40 dark:to-blue-900/10 border-l-4 border-blue-400 dark:border-blue-500 rounded-r-xl leading-relaxed">
                  {viewArticle.summary}
                </div>
              )}
            </header>

            {/* 分隔线 */}
            <div className="my-6 border-t border-slate-100 dark:border-slate-700" />

            {/* 正文阅读区：限定最大宽度，居中，提升可读性 */}
            <article
              className="knowledge-content mx-auto w-full max-w-3xl px-1 sm:px-2 text-[15px] leading-[1.8] text-slate-700 dark:text-slate-300"
              dangerouslySetInnerHTML={{ __html: renderContent(viewArticle.content, viewArticle.contentType || 'markdown') }}
              onClick={(e) => {
                // 拦截文章内锚点链接(#xxx)点击，平滑滚动到对应标题
                const target = e.target as HTMLElement;
                const anchor = target.closest('a');
                if (!anchor) return;
                const href = anchor.getAttribute('href') || '';
                if (!href.startsWith('#')) return;
                e.preventDefault();
                const hash = decodeURIComponent(href.slice(1));
                if (!hash) return;
                const articleEl = e.currentTarget;
                // 用 attribute selector 匹配 data-anchor（M-FE4：从 id 改为 data-anchor，配合 SANITIZE_NAMED_PROPS=true）
                const targetEl = articleEl.querySelector(`[data-anchor="${CSS.escape(hash)}"]`) as HTMLElement | null;
                if (targetEl) {
                  targetEl.scrollIntoView({ behavior: 'smooth', block: 'start' });
                  // 临时高亮目标标题，提升定位感知
                  targetEl.classList.add('knowledge-anchor-target');
                  setTimeout(() => targetEl.classList.remove('knowledge-anchor-target'), 1500);
                }
              }}
            />

            {/* 文章附件区（PDF预览 / 图片画廊 / 下载链接） */}
            {viewArticle.attachments && viewArticle.attachments.length > 0 && (
              <section className="mt-8 mx-auto w-full max-w-3xl px-1 sm:px-2">
                <div className="my-6 border-t border-slate-100 dark:border-slate-700" />
                <div className="flex items-center gap-2 mb-4">
                  <div className="w-7 h-7 rounded-lg bg-gradient-to-br from-emerald-500 to-teal-600 flex items-center justify-center shadow-sm">
                    <Paperclip size={14} className="text-white" />
                  </div>
                  <h3 className="text-sm font-semibold text-slate-700 dark:text-slate-300">
                    {t('knowledge.attachments.sectionTitle', { count: viewArticle.attachments.length })}
                  </h3>
                </div>
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                  {viewArticle.attachments.map((att) => {
                    const AttIcon = getAttachmentIcon(att);
                    return (
                      <div
                        key={att.id}
                        className="group flex items-center gap-3 px-3 py-2.5 rounded-xl bg-slate-50 dark:bg-slate-700/40 border border-slate-200 dark:border-slate-600 hover:border-blue-300 dark:hover:border-blue-700 hover:shadow-sm transition-all"
                      >
                        <div className={`w-9 h-9 rounded-lg flex items-center justify-center flex-shrink-0 ${att.isPdf ? 'bg-red-50 dark:bg-red-900/30 text-red-600 dark:text-red-400' : att.isImage ? 'bg-blue-50 dark:bg-blue-900/30 text-blue-600 dark:text-blue-400' : 'bg-slate-100 dark:bg-slate-600 text-slate-500 dark:text-slate-400'}`}>
                          <AttIcon size={18} />
                        </div>
                        <div className="flex-1 min-w-0">
                          <div className="text-sm text-slate-700 dark:text-slate-200 truncate font-medium">
                            {att.fileName}
                          </div>
                          <div className="text-xs text-slate-400">
                            {att.fileSizeDisplay || formatAttachmentSize(att.fileSize)} · {att.extension.toUpperCase()}
                          </div>
                        </div>
                        <div className="flex items-center gap-1 flex-shrink-0">
                          {(att.isPdf || att.isImage) && (
                            <button
                              onClick={() => handlePreviewAttachment(att)}
                              className="p-1.5 rounded text-slate-400 hover:text-blue-600 dark:hover:text-blue-400 hover:bg-blue-50 dark:hover:bg-blue-900/30 transition-all"
                              title={t('knowledge.attachments.preview')}
                            >
                              <EyeIcon size={15} />
                            </button>
                          )}
                          <button
                            onClick={() => handleDownloadAttachment(att)}
                            className="p-1.5 rounded text-slate-400 hover:text-green-600 dark:hover:text-green-400 hover:bg-green-50 dark:hover:bg-green-900/30 transition-all"
                            title={t('knowledge.attachments.download')}
                          >
                            <Download size={15} />
                          </button>
                        </div>
                      </div>
                    );
                  })}
                </div>
              </section>
            )}

            {/* 底部交互区 */}
            <footer className="mt-8 pt-5 border-t border-slate-100 dark:border-slate-700">
              <div className="flex items-center justify-between flex-wrap gap-3">
                <div className="flex items-center gap-3 text-xs text-slate-400 dark:text-slate-500">
                  <span className="flex items-center gap-1 px-2.5 py-1 rounded-full bg-slate-50 dark:bg-slate-700/40">
                    <Eye size={12} /> {t('knowledge.view.footerViews', { count: viewArticle.viewCount })}
                  </span>
                  <span className="flex items-center gap-1 px-2.5 py-1 rounded-full bg-slate-50 dark:bg-slate-700/40">
                    <ThumbsUp size={12} /> {t('knowledge.view.footerLikes', { count: viewArticle.likeCount })}
                  </span>
                </div>
                <button
                  onClick={() => handleLikeArticle(viewArticle)}
                  className={`group flex items-center gap-1.5 px-4 py-2 text-sm font-medium rounded-full transition-all active:scale-95 ${
                    viewArticle.isLiked
                      ? 'text-rose-600 dark:text-rose-400 bg-rose-50 dark:bg-rose-900/30 border border-rose-300 dark:border-rose-700/60 shadow-md shadow-rose-500/10'
                      : 'text-rose-600 dark:text-rose-400 border border-rose-200 dark:border-rose-800/40 hover:bg-rose-50 dark:hover:bg-rose-900/20 hover:shadow-md hover:shadow-rose-500/20'
                  }`}
                  title={viewArticle.isLiked ? t('knowledge.view.unlike') : t('knowledge.view.like')}
                >
                  <ThumbsUp size={14} className={`group-hover:scale-110 transition-transform ${viewArticle.isLiked ? 'fill-current' : ''}`} /> {viewArticle.isLiked ? t('knowledge.view.likedHint') : t('knowledge.view.likeHint')}
                </button>
              </div>
            </footer>
          </div>
        </Modal>
      )}

      {/* 附件预览模态窗（PDF iframe / 图片预览） */}
      {previewingAttachment && (
        <Modal
          open={!!previewingAttachment}
          onClose={handleCloseAttachmentPreview}
          title={
            <div className="flex items-center gap-2">
              <div className={`w-8 h-8 rounded-lg flex items-center justify-center ${previewingAttachment.isPdf ? 'bg-gradient-to-br from-red-500 to-rose-600' : 'bg-gradient-to-br from-blue-500 to-cyan-600'}`}>
                {previewingAttachment.isPdf ? <FileType size={16} className="text-white" /> : <Image size={16} className="text-white" />}
              </div>
              <span className="truncate max-w-[400px]">{previewingAttachment.fileName}</span>
            </div>
          }
          size="xl"
        >
          <div className="flex flex-col gap-3">
            {loadingAttachmentPreview ? (
              <div className="flex flex-col items-center justify-center py-20 text-slate-400">
                <Loader2 size={32} className="animate-spin mb-3" />
                <p className="text-sm">{t('knowledge.attachments.loading')}</p>
              </div>
            ) : attachmentBlobUrl ? (
              <div className="flex flex-col gap-3">
                {previewingAttachment.isPdf ? (
                  <iframe
                    src={attachmentBlobUrl}
                    title={previewingAttachment.fileName}
                    // S5: PDF 可能包含恶意脚本/表单，sandbox 隔离；allow-same-origin 保留 blob 同源加载
                    sandbox="allow-same-origin"
                    className="w-full rounded-xl border border-slate-200 dark:border-slate-600"
                    style={{ height: '70vh', minHeight: '400px' }}
                  />
                ) : (
                  <div className="flex items-center justify-center bg-slate-50 dark:bg-slate-800 rounded-xl border border-slate-200 dark:border-slate-600 p-4" style={{ minHeight: '300px' }}>
                    <img
                      src={attachmentBlobUrl}
                      alt={previewingAttachment.fileName}
                      className="max-w-full max-h-[70vh] rounded-lg shadow-md object-contain"
                    />
                  </div>
                )}
                <div className="flex items-center justify-between flex-wrap gap-2 px-2">
                  <div className="text-xs text-slate-400">
                    {previewingAttachment.fileSizeDisplay || formatAttachmentSize(previewingAttachment.fileSize)} · {previewingAttachment.extension.toUpperCase()}
                  </div>
                  <div className="flex items-center gap-2">
                    <button
                      onClick={() => handleDownloadAttachment(previewingAttachment)}
                      className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-green-600 dark:text-green-400 border border-green-200 dark:border-green-800/40 rounded-lg hover:bg-green-50 dark:hover:bg-green-900/20 transition-all"
                    >
                      <Download size={13} /> {t('knowledge.attachments.download')}
                    </button>
                    <button
                      onClick={handleCloseAttachmentPreview}
                      className="flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium text-slate-600 dark:text-slate-300 border border-slate-200 dark:border-slate-600 rounded-lg hover:bg-slate-50 dark:hover:bg-slate-700 transition-all"
                    >
                      <X size={13} /> {t('knowledge.attachments.close')}
                    </button>
                  </div>
                </div>
              </div>
            ) : (
              <div className="flex flex-col items-center justify-center py-20 text-rose-400">
                <AlertTriangle size={32} className="mb-3" />
                <p className="text-sm">{t('knowledge.attachments.loadFailed')}</p>
              </div>
            )}
          </div>
        </Modal>
      )}

      {/* 删除文章确认 */}
      <ConfirmDialog
        open={!!deleteArticleTarget}
        title={t('knowledge.view.deleteConfirmTitle')}
        message={t('knowledge.view.deleteConfirmMessage', { title: deleteArticleTarget?.title ?? '' })}
        variant="danger"
        confirmText={t('knowledge.common.delete')}
        loading={deleting}
        onConfirm={handleDeleteArticle}
        onCancel={() => setDeleteArticleTarget(null)}
      />

      {/* 删除分类确认 */}
      <ConfirmDialog
        open={!!deleteCategoryTarget}
        title={t('knowledge.categories.deleteConfirmTitle')}
        message={t('knowledge.categories.deleteConfirmMessage', { name: deleteCategoryTarget?.name ?? '' })}
        variant="warning"
        confirmText={t('knowledge.common.delete')}
        loading={deleting}
        onConfirm={handleDeleteCategory}
        onCancel={() => setDeleteCategoryTarget(null)}
      />

      {/* 恢复文章确认 */}
      <ConfirmDialog
        open={!!restoreArticleTarget}
        title={t('knowledge.view.restoreConfirmTitle')}
        message={t('knowledge.view.restoreConfirmMessage', { title: restoreArticleTarget?.title ?? '' })}
        variant="info"
        confirmText={t('knowledge.common.restore')}
        onConfirm={handleRestoreArticle}
        onCancel={() => setRestoreArticleTarget(null)}
      />

      {/* 恢复分类确认 */}
      <ConfirmDialog
        open={!!restoreCategoryTarget}
        title={t('knowledge.categories.restoreConfirmTitle')}
        message={t('knowledge.categories.restoreConfirmMessage', { name: restoreCategoryTarget?.name ?? '' })}
        variant="info"
        confirmText={t('knowledge.common.restore')}
        onConfirm={handleRestoreCategory}
        onCancel={() => setRestoreCategoryTarget(null)}
      />

      {/* 彻底删除文章确认 */}
      <ConfirmDialog
        open={!!hardDeleteArticleTarget}
        title={t('knowledge.view.hardDeleteConfirmTitle')}
        message={t('knowledge.view.hardDeleteConfirmMessage', { title: hardDeleteArticleTarget?.title ?? '' })}
        variant="danger"
        confirmText={t('knowledge.common.hardDelete')}
        onConfirm={handleHardDeleteArticle}
        onCancel={() => setHardDeleteArticleTarget(null)}
      />

      {/* 彻底删除分类确认 */}
      <ConfirmDialog
        open={!!hardDeleteCategoryTarget}
        title={t('knowledge.categories.hardDeleteConfirmTitle')}
        message={t('knowledge.categories.hardDeleteConfirmMessage', { name: hardDeleteCategoryTarget?.name ?? '' })}
        variant="danger"
        confirmText={t('knowledge.common.hardDelete')}
        onConfirm={handleHardDeleteCategory}
        onCancel={() => setHardDeleteCategoryTarget(null)}
      />

      {/* 清空垃圾箱确认 */}
      <ConfirmDialog
        open={!!clearTrashTarget}
        title={t('knowledge.common.emptyTrash')}
        message={t('knowledge.trash.clearConfirmMessage', { target: clearTrashTarget === 'articles' ? t('knowledge.trash.targetArticles') : t('knowledge.trash.targetCategories') })}
        variant="danger"
        confirmText={t('knowledge.common.clear')}
        onConfirm={handleClearTrash}
        onCancel={() => setClearTrashTarget(null)}
      />

      {/* 草稿冲突提示：本地草稿与服务器版本不一致 */}
      {draftConflict && (
        <Modal open={!!draftConflict} onClose={() => setDraftConflict(null)} title={t('knowledge.draft.conflictTitle')} size="sm" footer={null}>
          <div className="space-y-4">
            <div className="flex items-start gap-3">
              <div className="w-10 h-10 rounded-xl bg-amber-100 dark:bg-amber-900/40 text-amber-600 dark:text-amber-300 flex items-center justify-center flex-shrink-0">
                <AlertTriangle size={20} />
              </div>
              <div className="flex-1">
                <p className="text-sm text-slate-600 dark:text-slate-300 leading-relaxed">
                  {t('knowledge.draft.conflictMessage')}
                </p>
                <div className="mt-3 grid grid-cols-2 gap-2 text-xs">
                  <div className="p-3 rounded-xl bg-slate-50 dark:bg-slate-700/40 border border-slate-200 dark:border-slate-600">
                    <div className="flex items-center gap-1 text-slate-400 mb-1">
                      <RefreshCw size={11} /> {t('knowledge.draft.localLabel')}
                    </div>
                    <div className="font-medium text-slate-700 dark:text-slate-100">
                      {formatDateTime(new Date(draftConflict.draftSavedAt).toISOString())}
                    </div>
                  </div>
                  <div className="p-3 rounded-xl bg-slate-50 dark:bg-slate-700/40 border border-slate-200 dark:border-slate-600">
                    <div className="flex items-center gap-1 text-slate-400 mb-1">
                      <Cloud size={11} /> {t('knowledge.draft.serverLabel')}
                    </div>
                    <div className="font-medium text-slate-700 dark:text-slate-100">
                      {draftConflict.serverUpdatedAt ? formatDateTime(draftConflict.serverUpdatedAt) : '—'}
                    </div>
                  </div>
                </div>
              </div>
            </div>
            <div className="flex items-center justify-end gap-2 pt-2">
              <button
                onClick={() => {
                  const uid = user?.id;
                  if (uid && editingArticle) {
                    try { localStorage.removeItem(`noneadcss_kb_draft_u${uid}_edit_${editingArticle.id}`); } catch { /* ignore */ }
                  }
                  // 丢弃草稿附件：以已拉取的 server 版本为准
                  setEditorAttachments((editingArticle?.attachments) ?? []);
                  setDraftConflict(null);
                  showToast({ type: 'info', text: t('knowledge.draft.discarded') });
                }}
                className="px-3.5 py-2 text-sm rounded-xl border border-slate-200 dark:border-slate-600 bg-white dark:bg-slate-700 text-slate-600 dark:text-slate-200 hover:bg-slate-50 dark:hover:bg-slate-600 transition-colors"
              >
                {t('knowledge.draft.discard')}
              </button>
              <button
                onClick={() => {
                  setArticleForm(draftConflict.form);
                  // T000103：恢复草稿时，同步把草稿中包含的附件一并合并（按 id 去重，草稿优先）
                  const uid = user?.id;
                  if (uid && editingArticle) {
                    try {
                      const raw = localStorage.getItem(`noneadcss_kb_draft_u${uid}_edit_${editingArticle.id}`);
                      if (raw) {
                        const draft = JSON.parse(raw) as {
                          form: ArticleForm;
                          attachments?: KnowledgeArticleAttachmentDto[];
                          savedAt: number;
                        };
                        const draftAttachments = Array.isArray(draft.attachments) ? draft.attachments : [];
                        const base = editingArticle.attachments ?? [];
                        if (draftAttachments.length > 0) {
                          const byId = new Map<number | string, KnowledgeArticleAttachmentDto>();
                          for (const att of base) {
                            if (att.id != null) byId.set(att.id, att);
                          }
                          for (const att of draftAttachments) {
                            const k = att.id ?? `${att.fileName}-${att.url}-${att.fileSize}`;
                            byId.set(k, att);
                          }
                          setEditorAttachments(Array.from(byId.values()));
                        }
                      }
                    } catch { /* ignore */ }
                  }
                  setDraftConflict(null);
                  showToast({ type: 'success', text: t('knowledge.draft.restored') });
                }}
                className="px-3.5 py-2 text-sm rounded-xl bg-gradient-to-br from-amber-500 to-orange-500 text-white shadow-sm hover:shadow-md hover:shadow-amber-500/30 active:scale-[0.98] transition-all"
              >
                {t('knowledge.draft.restore')}
              </button>
            </div>
          </div>
        </Modal>
      )}

      {/* Toast */}
      {toast && (
        <div
          className={`fixed bottom-4 right-4 px-4 py-2 rounded-lg shadow-lg text-white text-sm z-50 ${
            toast.type === 'success'
              ? 'bg-green-600'
              : toast.type === 'error'
                ? 'bg-rose-600'
                : toast.type === 'warning'
                  ? 'bg-amber-600'
                  : 'bg-slate-700'
          }`}
        >
          {toast.text}
        </div>
      )}
    </div>
  );
}

// 概览卡片组件
function OverviewCard({
  label,
  value,
  icon,
  color,
  delay = '0ms',
}: {
  label: string;
  value: number;
  icon: React.ReactNode;
  color: string;
  delay?: string;
}) {
  const { language } = useI18n();
  return (
    <div
      className="group relative bg-white dark:bg-slate-800 rounded-2xl shadow-sm hover:shadow-xl hover:shadow-indigo-500/10 p-4 sm:p-5 flex items-center gap-3 sm:gap-4 border border-slate-100 dark:border-slate-700 transform transition-all duration-300 hover:-translate-y-1 overflow-hidden"
      style={{ animationDelay: delay }}
    >
      {/* 悬浮渐变背景 */}
      <div className={`absolute inset-0 bg-gradient-to-br ${color} opacity-0 group-hover:opacity-[0.04] transition-opacity duration-300`} />
      {/* 左侧装饰条 */}
      <div className={`absolute left-0 top-0 bottom-0 w-1 bg-gradient-to-b ${color} opacity-80 group-hover:opacity-100 transition-opacity`} />
      <div className={`relative w-12 h-12 sm:w-14 sm:h-14 rounded-xl bg-gradient-to-br ${color} flex items-center justify-center text-white shadow-lg group-hover:scale-110 group-hover:rotate-3 transition-transform duration-300`}>
        {icon}
      </div>
      <div className="relative flex-1 min-w-0">
        <div className="text-[10px] sm:text-xs font-medium text-slate-500 dark:text-slate-400 uppercase tracking-wider truncate">{label}</div>
        <div className="text-2xl sm:text-3xl font-bold text-slate-800 dark:text-slate-100 mt-0.5 tracking-tight">{formatCount(value, language)}</div>
      </div>
    </div>
  );
}
