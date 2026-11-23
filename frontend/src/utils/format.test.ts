import { describe, it, expect, afterEach } from 'vitest';
import {
  sanitizeFileName,
  safeUrl,
  formatFileSize,
  isImage,
  isAudio,
  isVideo,
  getStatusLabel,
  getPriorityLabel,
  getSourceTypeLabel,
  getSourceTypeLabelByCode,
  displayCustomerName,
  formatDate,
  formatDateTime,
  relativeTime,
  formatRelative,
} from '../utils/format';
import { getRoleLabel } from '../utils/permissions';
import { getTimezoneLabel, timezoneOptions } from '../constants/timezones';
import { setBoundLocale } from '../i18n/locale';

/** 断言辅助：locale-aware 用例一律**显式传语言**，不依赖模块默认值 */
const ZH = 'zh-Hans';
const EN = 'en';

/** n 毫秒前的 ISO 串 */
const agoMs = (ms: number) => new Date(Date.now() - ms).toISOString();

/** 改过绑定语言后必须复位，避免影响同文件后续用例 */
afterEach(() => setBoundLocale(ZH));

describe('sanitizeFileName (utils/format)', () => {
  it('移除路径分隔符和 Windows 非法字符，替换为 _', () => {
    expect(sanitizeFileName('../etc/passwd')).toBe('.._etc_passwd');
    expect(sanitizeFileName('a\\b:c*d?e"f<g>h|i')).toBe('a_b_c_d_e_f_g_h_i');
  });

  it('中文 / 空格 / 扩展保留', () => {
    expect(sanitizeFileName('测试 文件 v1.2.pdf')).toBe('测试 文件 v1.2.pdf');
  });

  it('空字符串 → 空字符串，不崩溃', () => {
    expect(sanitizeFileName('')).toBe('');
  });

  it('长文件名保持原样（实现未做显式截断）', () => {
    const long = 'a'.repeat(300) + '.txt';
    const r = sanitizeFileName(long);
    expect(r.endsWith('.txt')).toBe(true);
  });
});

describe('safeUrl (utils/format)', () => {
  it('http/https 原样返回', () => {
    expect(safeUrl('https://example.com/a?x=1')).toBe('https://example.com/a?x=1');
    expect(safeUrl('http://a.b')).toBe('http://a.b');
  });

  it('mailto / tel 也允许（实现白名单包含）', () => {
    expect(safeUrl('mailto:foo@bar.com')).toBe('mailto:foo@bar.com');
    expect(safeUrl('tel:13800138000')).toBe('tel:13800138000');
  });

  it('data:image/...;base64, 开头的白名单 MIME → 通过', () => {
    const dataUrl = 'data:image/png;base64,abc';
    expect(safeUrl(dataUrl)).toBe(dataUrl);
  });

  it('javascript: / vbscript: / data:text/html → 返回 "#"', () => {
    expect(safeUrl('javascript:alert(1)')).toBe('#');
    expect(safeUrl('vbscript:msgbox(1)')).toBe('#');
    expect(safeUrl('data:text/html,<h1>xss')).toBe('#');
  });

  it('null / undefined / 空字符串 → "#"', () => {
    expect(safeUrl(null)).toBe('#');
    expect(safeUrl(undefined)).toBe('#');
    expect(safeUrl('')).toBe('#');
    expect(safeUrl('   ')).toBe('#');
  });

  it('相对路径 / 裸字符串（无协议前缀）→ 按实现原样返回', () => {
    expect(safeUrl('/uploads/a.png')).toBe('/uploads/a.png');
    expect(safeUrl('no-protocol.example')).toBe('no-protocol.example');
  });
});

describe('formatFileSize (utils/format)', () => {
  it('0 字节 → "0 B"', () => {
    expect(formatFileSize(0)).toBe('0 B');
  });

  it('B / KB / MB / GB 单位正确进位（注意小数精度）', () => {
    expect(formatFileSize(512)).toBe('512 B');
    expect(formatFileSize(1024)).toBe('1.0 KB');
    expect(formatFileSize(1536)).toBe('1.5 KB');
    expect(formatFileSize(1024 * 1024)).toBe('1.0 MB');
    expect(formatFileSize(1024 * 1024 * 1024)).toBe('1.00 GB');
  });

  it('负数 → 显示 "-0.0 B" 之类（未特殊处理），仍为字符串', () => {
    const r = formatFileSize(-100);
    expect(typeof r).toBe('string');
  });
});

describe('isImage / isAudio / isVideo (utils/format)', () => {
  it('isImage 常见图片 mime', () => {
    expect(isImage('image/png')).toBe(true);
    expect(isImage('image/jpeg')).toBe(true);
    expect(isImage('image/svg+xml')).toBe(true);
    expect(isImage('application/pdf')).toBe(false);
    expect(isImage('')).toBe(false);
  });

  it('isAudio 常见音频 mime', () => {
    expect(isAudio('audio/mpeg')).toBe(true);
    expect(isAudio('audio/mp4')).toBe(true);
    expect(isAudio('audio/wav')).toBe(true);
    expect(isAudio('image/png')).toBe(false);
  });

  it('isVideo 常见视频 mime', () => {
    expect(isVideo('video/mp4')).toBe(true);
    expect(isVideo('video/webm')).toBe(true);
    expect(isVideo('video/x-matroska')).toBe(true);
    expect(isVideo('audio/mp4')).toBe(false);
  });
});

describe('getStatusLabel & getSourceTypeLabel', () => {
  it('getStatusLabel 已知 status（1 活跃 / 2 待处理 / 3 已关闭 / 4 垃圾邮件）', () => {
    expect(getStatusLabel(1, ZH)).toBe('活跃');
    expect(getStatusLabel(2, ZH)).toBe('待处理');
    expect(getStatusLabel(3, ZH)).toBe('已关闭');
    expect(getStatusLabel(4, ZH)).toBe('垃圾邮件');
  });

  it('getStatusLabel 未知 status 或 0 → "未知"', () => {
    expect(getStatusLabel(0, ZH)).toBe('未知');
    expect(getStatusLabel(99, ZH)).toBe('未知');
  });

  it('getSourceTypeLabel 已知来源类型（1=邮件 / 7=微信）', () => {
    expect(getSourceTypeLabel(1, ZH)).toBe('邮件');
    expect(getSourceTypeLabel(7, ZH)).toBe('微信');
    expect(getSourceTypeLabel(10, ZH)).toBe('钉钉');
  });

  it('getSourceTypeLabel undefined → "未知"', () => {
    expect(getSourceTypeLabel(undefined, ZH)).toBe('未知');
  });
});

describe('formatDate / formatDateTime / relativeTime 空值兜底', () => {
  it('null / undefined → 返回 "-"', () => {
    expect(formatDate(null)).toBe('-');
    expect(formatDate(undefined)).toBe('-');
    expect(formatDateTime(null)).toBe('-');
    expect(formatDateTime(undefined)).toBe('-');
    expect(relativeTime(null)).toBe('-');
    expect(relativeTime(undefined)).toBe('-');
    expect(formatRelative(null)).toBe('-');
  });

  it('合法 ISO → formatDate 返回短日期', () => {
    const r = formatDate('2026-08-15T09:30:00Z');
    expect(r.length).toBeGreaterThanOrEqual(8); // 至少包含 YYYY/M/D
  });
});

/* ============================================================
 * 语言感知（波次 2）：日期/数字走 Intl，标签走真源 locales 的 format 模块。
 * 这些用例都**显式传语言**，因此与 I18nProvider 是否挂载、模块默认值无关。
 * ============================================================ */

describe('relativeTime 语言感知（Intl.RelativeTimeFormat）', () => {
  it('中文：刚刚 / n分钟前 / n小时前 / n天前', () => {
    expect(relativeTime(agoMs(30 * 1000), ZH)).toBe('刚刚');
    expect(relativeTime(agoMs(5 * 60 * 1000), ZH)).toBe('5分钟前');
    expect(relativeTime(agoMs(3 * 3600 * 1000), ZH)).toBe('3小时前');
    expect(relativeTime(agoMs(5 * 86400 * 1000), ZH)).toMatch(/天前/);
  });

  it('英文：Just now / n minutes ago / n hours ago / n days ago', () => {
    expect(relativeTime(agoMs(30 * 1000), EN)).toBe('Just now');
    expect(relativeTime(agoMs(5 * 60 * 1000), EN)).toBe('5 minutes ago');
    expect(relativeTime(agoMs(3 * 3600 * 1000), EN)).toBe('3 hours ago');
    expect(relativeTime(agoMs(5 * 86400 * 1000), EN)).toMatch(/days ago/);
  });

  it('numeric: auto —— 恰好 1 天输出「昨天」/「yesterday」', () => {
    expect(relativeTime(agoMs(86400 * 1000), ZH)).toBe('昨天');
    expect(relativeTime(agoMs(86400 * 1000), EN)).toBe('yesterday');
  });

  it('超过 7 天回落日期，且顺序随语言：zh → YYYY/MM/DD，en → MM/DD/YYYY', () => {
    const old = '2020-01-02T12:00:00Z';
    expect(relativeTime(old, ZH)).toMatch(/^\d{4}\/\d{2}\/\d{2}$/);
    expect(relativeTime(old, EN)).toMatch(/^\d{2}\/\d{2}\/\d{4}$/);
  });
});

describe('formatDate / formatDateTime 语言感知（Intl.DateTimeFormat）', () => {
  const iso = '2026-08-15T09:30:00Z';

  it('短日期顺序随语言（zh 年在前，en 年在后）', () => {
    expect(formatDate(iso, undefined, ZH)).toMatch(/^\d{4}\/\d{2}\/\d{2}$/);
    expect(formatDate(iso, undefined, EN)).toMatch(/^\d{2}\/\d{2}\/\d{4}$/);
  });

  it('日期时间：zh 24 小时制（无 AM/PM），en 12 小时制（有 AM/PM）', () => {
    const zh = formatDateTime(iso, undefined, ZH);
    const en = formatDateTime(iso, undefined, EN);
    expect(zh).toMatch(/^\d{4}\/\d{2}\/\d{2} \d{2}:\d{2}$/);
    expect(zh).not.toMatch(/AM|PM/);
    expect(en).toMatch(/^\d{2}\/\d{2}\/\d{4}, \d{2}:\d{2}/);
    expect(en).toMatch(/AM|PM/);
  });

  it('时区参数仍然生效（同一时刻按 UTC 与按本地不同）', () => {
    expect(formatDateTime(iso, 'UTC', EN)).toMatch(/09:30/);
  });
});

describe('formatFileSize 数字走 Intl.NumberFormat', () => {
  it('两种语言下小数位一致（B/KB/MB/GB）', () => {
    expect(formatFileSize(1536, EN)).toBe('1.5 KB');
    expect(formatFileSize(1024, EN)).toBe('1.0 KB');
    expect(formatFileSize(1024, ZH)).toBe('1.0 KB');
    expect(formatFileSize(1024 * 1024 * 1024, EN)).toBe('1.00 GB');
  });
});

describe('标签映射语言感知（真源 locales/*/format.ts）', () => {
  it('状态 / 优先级 / 来源类型 / 角色 / 客户兜底名 —— 英文', () => {
    expect(getStatusLabel(1, EN)).toBe('Active');
    expect(getStatusLabel(4, EN)).toBe('Spam');
    expect(getStatusLabel(99, EN)).toBe('Unknown');
    expect(getPriorityLabel(0, EN)).toBe('Low');
    expect(getPriorityLabel(3, EN)).toBe('Urgent');
    expect(getPriorityLabel(99, EN)).toBe('Normal');
    expect(getSourceTypeLabel(1, EN)).toBe('Email');
    expect(getSourceTypeLabel(7, EN)).toBe('WeChat');
    expect(getSourceTypeLabelByCode('chat', EN)).toBe('Live chat');
    expect(getSourceTypeLabelByCode('brand-new-channel', EN)).toBe('brand-new-channel');
    expect(getRoleLabel(1, EN)).toBe('Read-only');
    expect(getRoleLabel(2, EN)).toBe('Super admin');
    expect(displayCustomerName('someone@example.com', EN)).toBe('Customer');
  });

  it('同一 code 的中文标签保持既有措辞', () => {
    expect(getPriorityLabel(1, ZH)).toBe('普通');
    expect(getSourceTypeLabelByCode('chat', ZH)).toBe('在线聊天');
    expect(getRoleLabel(3, ZH)).toBe('客服主管');
    expect(displayCustomerName('   ', ZH)).toBe('客户');
  });

  /**
   * 防"界面渲染出 key 本身"这一类缺陷（波次 1 实测：tsc / eslint / 中文扫描器全绿，
   * 但英文界面显示 `dashboard.status.active`）。这里对**两种语言**逐条断言：
   * 任何标签都不得等于以模块名开头的原始 key，也不得为空。
   */
  it('两种语言下任何标签都不是原始 key，也不为空', () => {
    const KEY_LIKE = /^(common|nav|auth|dashboard|format)\./;
    for (const lang of [ZH, EN]) {
      const labels = [
        getStatusLabel(1, lang),
        getStatusLabel(99, lang),
        getPriorityLabel(2, lang),
        getSourceTypeLabel(3, lang),
        getSourceTypeLabelByCode('email', lang),
        getRoleLabel(4, lang),
        displayCustomerName(null, lang),
        formatDateTime('2026-08-15T09:30:00Z', undefined, lang),
      ];
      for (const label of labels) {
        expect(label, `${lang} 标签不得是原始 key`).not.toMatch(KEY_LIKE);
        expect(label.length).toBeGreaterThan(0);
      }
    }
  });
});

describe('时区标签语言感知（constants/timezones.ts）', () => {
  it('中文标签与迁移前逐字一致（抽样），数量未缩水', () => {
    expect(timezoneOptions).toHaveLength(111);
    expect(getTimezoneLabel('UTC', ZH)).toBe('协调世界时 (UTC)');
    expect(getTimezoneLabel('Europe/London', ZH)).toBe('伦敦时间 (UTC±0)');
    expect(getTimezoneLabel('Asia/Shanghai')).toBe('中国标准时间 (UTC+8)');
  });

  it('英文标签为英文地名（含 IANA 末段不是通行地名的例外）', () => {
    expect(getTimezoneLabel('UTC', EN)).toBe('Coordinated Universal Time (UTC)');
    expect(getTimezoneLabel('Europe/London', EN)).toBe('London time (UTC±0)');
    expect(getTimezoneLabel('Asia/Shanghai', EN)).toBe('China Standard Time (UTC+8)');
    expect(getTimezoneLabel('Asia/Oman', EN)).toBe('Muscat time (UTC+4)');
  });

  it('未知时区值原样返回（不吞掉、不变 key）', () => {
    expect(getTimezoneLabel('Mars/Olympus', ZH)).toBe('Mars/Olympus');
  });

  it('两种语言下 111 条标签都不是原始 key，也不为空', () => {
    const KEY_LIKE = /^(common|nav|auth|dashboard|format)\./;
    for (const lang of [ZH, EN]) {
      for (const option of timezoneOptions) {
        expect(option.label, `${lang} ${option.value}`).not.toMatch(KEY_LIKE);
        expect(option.label.length).toBeGreaterThan(0);
      }
    }
  });
});

describe('未显式传语言时跟随绑定语言（I18nProvider → setBoundLocale）', () => {
  it('绑定 en 后默认输出英文；绑定 zh-Hans 后回到中文', () => {
    setBoundLocale(EN);
    expect(getStatusLabel(1)).toBe('Active');
    expect(getRoleLabel(1)).toBe('Read-only');
    expect(relativeTime(agoMs(5 * 60 * 1000))).toBe('5 minutes ago');
    expect(getTimezoneLabel('Europe/London')).toBe('London time (UTC±0)');
    expect(formatDate('2026-08-15T09:30:00Z')).toMatch(/^\d{2}\/\d{2}\/\d{4}$/);

    setBoundLocale(ZH);
    expect(getStatusLabel(1)).toBe('活跃');
    expect(getRoleLabel(1)).toBe('只读用户');
    expect(relativeTime(agoMs(5 * 60 * 1000))).toBe('5分钟前');
    expect(getTimezoneLabel('Europe/London')).toBe('伦敦时间 (UTC±0)');
  });

  it('非法语言值一律回落中文，不产生半绑定状态', () => {
    setBoundLocale('fr-FR');
    expect(getStatusLabel(1)).toBe('活跃');
    setBoundLocale(undefined);
    expect(getStatusLabel(1)).toBe('活跃');
  });
});
