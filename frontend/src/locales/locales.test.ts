import { describe, expect, it } from 'vitest';
import type { Entry } from '../i18n/types';
import { zhHans } from './zh-Hans';
import { en } from './en';
import { common } from './zh-Hans/common';
import { nav } from './zh-Hans/nav';
import { auth } from './zh-Hans/auth';
import { dashboard } from './zh-Hans/dashboard';
import { format } from './zh-Hans/format';
import { conversations } from './zh-Hans/conversations';
import { notifications } from './zh-Hans/notifications';
import { knowledge } from './zh-Hans/knowledge';
import { chat } from './zh-Hans/chat';
import { settings } from './zh-Hans/settings';
import { teamPermissions } from './zh-Hans/teamPermissions';
import { automation } from './zh-Hans/automation';
import { mail } from './zh-Hans/mail';
import { common as enCommon } from './en/common';
import { nav as enNav } from './en/nav';
import { auth as enAuth } from './en/auth';
import { dashboard as enDashboard } from './en/dashboard';
import { format as enFormat } from './en/format';
import { conversations as enConversations } from './en/conversations';
import { notifications as enNotifications } from './en/notifications';
import { knowledge as enKnowledge } from './en/knowledge';
import { chat as enChat } from './en/chat';
import { settings as enSettings } from './en/settings';
import { teamPermissions as enTeamPermissions } from './en/teamPermissions';
import { automation as enAutomation } from './en/automation';
import { mail as enMail } from './en/mail';
import { channels } from './zh-Hans/channels';
import { data } from './zh-Hans/data';
import { channels as enChannels } from './en/channels';
import { data as enData } from './en/data';
import { customers } from './zh-Hans/customers';
import { customers as enCustomers } from './en/customers';
import { calendar } from './zh-Hans/calendar';
import { calendar as enCalendar } from './en/calendar';
import { integration } from './zh-Hans/integration';
import { integration as enIntegration } from './en/integration';
import { serviceOps } from './zh-Hans/serviceOps';
import { serviceOps as enServiceOps } from './en/serviceOps';
import { localization } from './zh-Hans/localization';
import { localization as enLocalization } from './en/localization';
import * as guardNs from './guard';

/**
 * 模块清单。**新增 locales 模块时必须同时加进这两个数组**——
 * 否则"各模块键数之和 == 折叠后键数"这条重名门禁就管不到新模块；
 * 下面那条"注册数 == 模块总数"的用例会立刻把它变成红灯。
 */
const zhModules = [common, nav, auth, dashboard, format, conversations, notifications, knowledge, chat, settings, teamPermissions, automation, mail, channels, data, customers, calendar, integration, serviceOps, localization];
const enModules = [
  enCommon,
  enNav,
  enAuth,
  enDashboard,
  enFormat,
  enConversations,
  enNotifications,
  enKnowledge,
  enChat,
  enSettings,
  enTeamPermissions,
  enAutomation,
  enMail,
  enChannels,
  enData,
  enCustomers,
  enCalendar,
  enIntegration,
  enServiceOps,
  enLocalization,
];

/** 汉字（含扩展区） */
const HAN = /\p{Script=Han}/u;
/**
 * 中文字形特有的标点与**全角形式**（含全角字母/数字/标点，即 U+FF00–FFEF 整块）。
 *
 * ⚠ **不要**把 `\u2014`（—）、`\u2026`（…）、弯引号加进来：它们在中英排版里都合法
 * （本仓库就有 `'Loading…'`、`'—'` 空值占位等 22 处），加进来会把合法英文误判成中文。
 * ⚠ 也别只写 `\uFF01-\uFF0F…` 这些零散区间——那会漏掉全角字母数字（`Ｓｅａｒｃｈ`），
 * 而全角字母数字正是中文输入法下最常见的手滑残留。
 */
const CJK_PUNCT = /[\u3000-\u303F\uFF00-\uFFEF]/;

/** 只认自有属性：`k in obj` 会沿原型链，把 valueOf / toString 之类算成"存在" */
const hasOwn = (o: object, k: string) => Object.prototype.hasOwnProperty.call(o, k);

/**
 * 门禁：中英 key 集合必须完全一致。
 *
 * `Record<TranslationKey, Entry>` 已能挡住"少 key"（TS2741），但挡不住：
 * - "多 key"（聚合体只有展开，展开属性不触发多余属性检查）；
 * - 值侧错译（英文里残留中文）与空值；
 * - 运行期动态塞入的 key。
 * 也无法在 CI 里给出可读的失败原因——这个测试同时充当第二道防线与诊断信息。
 */
describe('中英词条对齐', () => {
  it('英文不得缺少任何中文 key', () => {
    const missing = Object.keys(zhHans).filter((k) => !hasOwn(en, k));
    expect(missing, `英文缺失 ${missing.length} 条：${missing.slice(0, 10).join(', ')}`).toEqual([]);
  });

  it('英文不得多出中文没有的 key', () => {
    const extra = Object.keys(en).filter((k) => !hasOwn(zhHans, k));
    expect(extra, `英文多出 ${extra.length} 条：${extra.slice(0, 10).join(', ')}`).toEqual([]);
  });

  it('词条内容不得为空（中英两侧都要查）', () => {
    const isEmpty = (v: Entry) => (typeof v === 'string' ? v.trim() === '' : !v.one.trim() || !v.other.trim());
    const empty = [
      ...Object.entries(zhHans).filter(([, v]) => isEmpty(v)).map(([k]) => `zh:${k}`),
      ...Object.entries(en).filter(([, v]) => isEmpty(v)).map(([k]) => `en:${k}`),
    ];
    expect(empty, `空词条 ${empty.length} 条：${empty.slice(0, 10).join(', ')}`).toEqual([]);
  });

  /**
   * 英文值不得含中文。
   *
   * 这是本批次要防的两种静默失败之一的**值侧**：key 对齐门禁只比较键集合，
   * 英文里若残留一段中文（复制粘贴漏改）或空字符串，`tsc` 与键对齐测试都照样通过，
   * 只有人工看界面才会发现。批次 2-11 要新增数千条词条，必须有机械门禁兜住。
   */
  it('英文词条不得包含汉字或中文标点/全角字符', () => {
    const tainted = Object.entries(en)
      .filter(([, v]) => {
        const parts = typeof v === 'string' ? [v] : [v.one, v.other];
        return parts.some((s) => HAN.test(s) || CJK_PUNCT.test(s));
      })
      .map(([k]) => k);
    expect(tainted, `英文词条含中文：${tainted.slice(0, 10).join(', ')}`).toEqual([]);
  });

  /**
   * 防误伤：em dash / 省略号 / 弯引号在英文排版里合法，字符类绝不能把它们算成中文。
   *
   * 这条用例**直接跑正则**。不要写成"断言某个词条的字面值等于它自己"
   * （例如 `expect(en['common.loading']).toBe('Loading…')`）——那是恒真的，
   * 正则被人加宽时它照样绿，等于没保护。
   */
  it('英文排版合法的 — … 弯引号不会被判成中文', () => {
    for (const s of ['Loading…', '—', 'it’s a “quote”']) {
      expect(HAN.test(s) || CJK_PUNCT.test(s), `不应判为中文：${s}`).toBe(false);
    }
    expect(en['common.loading']).toBe('Loading…');
  });

  /**
   * 跨模块重名检测。
   *
   * `zh-Hans/index.ts` 用对象展开聚合（`{ ...common, ...nav }`），若两个模块定义了同一个 key，
   * 后者会**静默 last-wins**——没有任何报错。而上面的"中英 key 对齐"比较的是**折叠后**的键集合，
   * 中英两侧都相等，因此同样看不见重名。
   *
   * 断言"各模块键数之和 == 折叠后键数"即可在聚合层捕获重名。**中英两侧都要断言**：
   * en 侧的重名会让英文值被静默 shadow。
   */
  it('各模块键数之和必须等于折叠后的总键数（中英两侧都无跨模块重名）', () => {
    const zhSummed = zhModules.reduce((n, m) => n + Object.keys(m).length, 0);
    const enSummed = enModules.reduce((n, m) => n + Object.keys(m).length, 0);

    expect(
      zhSummed,
      `zh 各模块键数之和 ${zhSummed} ≠ 折叠后 ${Object.keys(zhHans).length}（跨模块重名 key 会静默覆盖）`
    ).toBe(Object.keys(zhHans).length);
    expect(
      enSummed,
      `en 各模块键数之和 ${enSummed} ≠ 折叠后 ${Object.keys(en).length}（跨模块重名 key 会静默覆盖）`
    ).toBe(Object.keys(en).length);
  });

  /**
   * guard.ts 里的逐模块护栏是**手工注册**的：新增模块时若只加了上面的数组、
   * 忘了在 guard.ts 里补一行断言，新模块的键被标注后仍会静默消失，而整套门禁全绿。
   * 这条断言把"注册"变成机械要求：护栏常量个数必须等于模块总数。
   */
  it('逐模块护栏的注册数必须等于模块总数（防新增模块漏注册）', () => {
    const registered = Object.keys(guardNs).filter((k) => k.endsWith('KeysAreLiteral'));
    expect(
      registered.length,
      `guard.ts 里只有 ${registered.length} 条逐模块护栏，但模块有 ${zhModules.length + enModules.length} 个：${registered.join(', ')}`
    ).toBe(zhModules.length + enModules.length);
  });

  /**
   * 上面的护栏常量是**编译期**断言（关键在它们的类型上，见 guard.ts）。
   * 这条运行期用例只保证它们没被删掉或改成别的值——真正的把关由 `npx tsc -b` 完成。
   */
  it('护栏常量必须全部为 true', () => {
    expect([
      guardNs.i18nKeyUnionIsNarrow,
      guardNs.zhCommonKeysAreLiteral,
      guardNs.zhNavKeysAreLiteral,
      guardNs.zhAuthKeysAreLiteral,
      guardNs.zhDashboardKeysAreLiteral,
      guardNs.zhFormatKeysAreLiteral,
      guardNs.zhConversationsKeysAreLiteral,
      guardNs.zhNotificationsKeysAreLiteral,
      guardNs.zhKnowledgeKeysAreLiteral,
      guardNs.zhChatKeysAreLiteral,
      guardNs.zhSettingsKeysAreLiteral,
      guardNs.zhTeamPermissionsKeysAreLiteral,
      guardNs.zhAutomationKeysAreLiteral,
      guardNs.zhMailKeysAreLiteral,
      guardNs.zhChannelsKeysAreLiteral,
      guardNs.enCommonKeysAreLiteral,
      guardNs.enNavKeysAreLiteral,
      guardNs.enAuthKeysAreLiteral,
      guardNs.enDashboardKeysAreLiteral,
      guardNs.enFormatKeysAreLiteral,
      guardNs.enConversationsKeysAreLiteral,
      guardNs.enNotificationsKeysAreLiteral,
      guardNs.enKnowledgeKeysAreLiteral,
      guardNs.enChatKeysAreLiteral,
      guardNs.enSettingsKeysAreLiteral,
      guardNs.enTeamPermissionsKeysAreLiteral,
      guardNs.enAutomationKeysAreLiteral,
      guardNs.enMailKeysAreLiteral,
      guardNs.enChannelsKeysAreLiteral,
      guardNs.zhDataKeysAreLiteral,
      guardNs.enDataKeysAreLiteral,
      guardNs.zhCustomersKeysAreLiteral,
      guardNs.enCustomersKeysAreLiteral,
      guardNs.zhCalendarKeysAreLiteral,
      guardNs.enCalendarKeysAreLiteral,
      guardNs.zhIntegrationKeysAreLiteral,
      guardNs.enIntegrationKeysAreLiteral,
      guardNs.zhServiceOpsKeysAreLiteral,
      guardNs.enServiceOpsKeysAreLiteral,
      guardNs.zhLocalizationKeysAreLiteral,
      guardNs.enLocalizationKeysAreLiteral,
    ]).toEqual([
      // 1 条聚合体护栏 + 20 条 zh 逐模块护栏 + 20 条 en 逐模块护栏 = 41 条
      true, true, true, true, true, true, true, true, true, true, true, true,
      true, true, true, true, true, true, true, true, true, true, true, true,
      true, true, true, true, true, true, true, true, true, true, true, true,
      true, true, true, true, true,
    ]);
  });
});
