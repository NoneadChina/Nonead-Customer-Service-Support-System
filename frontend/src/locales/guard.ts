import type { TranslationKey } from './zh-Hans';
import { common as zhCommon } from './zh-Hans/common';
import { nav as zhNav } from './zh-Hans/nav';
import { auth as zhAuth } from './zh-Hans/auth';
import { dashboard as zhDashboard } from './zh-Hans/dashboard';
import { format as zhFormat } from './zh-Hans/format';
import { conversations as zhConversations } from './zh-Hans/conversations';
import { notifications as zhNotifications } from './zh-Hans/notifications';
import { knowledge as zhKnowledge } from './zh-Hans/knowledge';
import { chat as zhChat } from './zh-Hans/chat';
import { settings as zhSettings } from './zh-Hans/settings';
import { teamPermissions as zhTeamPermissions } from './zh-Hans/teamPermissions';
import { automation as zhAutomation } from './zh-Hans/automation';
import { mail as zhMail } from './zh-Hans/mail';
import { channels as zhChannels } from './zh-Hans/channels';
import { data as zhData } from './zh-Hans/data';
import { customers as zhCustomers } from './zh-Hans/customers';
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
import { channels as enChannels } from './en/channels';
import { data as enData } from './en/data';
import { customers as enCustomers } from './en/customers';
import { calendar as zhCalendar } from './zh-Hans/calendar';
import { calendar as enCalendar } from './en/calendar';
import { integration as zhIntegration } from './zh-Hans/integration';
import { integration as enIntegration } from './en/integration';
import { serviceOps as zhServiceOps } from './zh-Hans/serviceOps';
import { serviceOps as enServiceOps } from './en/serviceOps';
import { localization as zhLocalization } from './zh-Hans/localization';
import { localization as enLocalization } from './en/localization';

/**
 * 编译期护栏（第 1 道）：聚合体的 TranslationKey 必须是**字面量联合**，不能被抹宽成 `string`。
 *
 * 背景（2026-09-23 实测，三种变体）：
 * 1. **聚合体**被标注（`const zhHans: Entries = {…}`）→ 索引签名传播，键联合被抹宽；
 * 2. 单展开时**叶子**被标注 → 同样抹宽（Task 1 当时只有 common 一个叶子，故当时有效）；
 * 3. ≥2 个展开（当前形态）时**叶子**被标注 → **不抹宽，而是该模块的键从 TranslationKey 静默消失**
 *    （实测：标注 `zh-Hans/nav.ts` 后 TranslationKey 只剩 13 个 `common.*` 键），
 *    `tsc` 退出码 0、本护栏亦不报错。
 *
 * 因此本护栏只覆盖情形 1、2；情形 3 由下面的**逐模块护栏**封堵。
 * 抹宽时 `IsKeyUnionNarrow` 为 `false`，下面的赋值即报 TS2322。
 * 这个护栏不依赖任何 key 清单，因此不需要随词条增删维护。
 * （"聚合体被清空成 `never`"这一情形不在这里管，由下面的逐模块护栏覆盖：
 *  任一叶子模块为空即报错，聚合体不可能悄悄变成空联合。）
 */
type IsKeyUnionNarrow = string extends TranslationKey ? false : true;
export const i18nKeyUnionIsNarrow: IsKeyUnionNarrow = true;

/**
 * 逐模块护栏（第 2 道）：每个叶子模块自身的键必须是**非空的字面量联合**。
 *
 * 用 `satisfies Entries` 时 `keyof typeof nav` 是字面量联合 → `true`，编译通过；
 * 一旦改成类型标注 `const nav: Entries = {…}`，`keyof` 变成 `string` → 类型为 `false`，
 * 下面的赋值立即报 TS2322。这就是"该模块的键从 TranslationKey 静默消失"的入口。
 * `keyof T === never`（模块被清空、只剩占位）同样返回 `false`——
 * 空模块会让它的全部键消失，且运行期门禁看不见（中英两侧一起缩小）。
 *
 * **新增 locales 模块时，必须把该模块补进下面这组断言。**
 * 漏注册会被 `locales.test.ts` 的"逐模块护栏注册数必须等于模块总数"当场抓住。
 */
type HasLiteralKeys<T> = [keyof T] extends [never] ? false : string extends keyof T ? false : true;

export const zhCommonKeysAreLiteral: HasLiteralKeys<typeof zhCommon> = true;
export const zhNavKeysAreLiteral: HasLiteralKeys<typeof zhNav> = true;
export const zhAuthKeysAreLiteral: HasLiteralKeys<typeof zhAuth> = true;
export const zhDashboardKeysAreLiteral: HasLiteralKeys<typeof zhDashboard> = true;
export const zhFormatKeysAreLiteral: HasLiteralKeys<typeof zhFormat> = true;
export const zhConversationsKeysAreLiteral: HasLiteralKeys<typeof zhConversations> = true;
export const zhNotificationsKeysAreLiteral: HasLiteralKeys<typeof zhNotifications> = true;
export const zhKnowledgeKeysAreLiteral: HasLiteralKeys<typeof zhKnowledge> = true;
export const zhChatKeysAreLiteral: HasLiteralKeys<typeof zhChat> = true;
export const zhSettingsKeysAreLiteral: HasLiteralKeys<typeof zhSettings> = true;
export const zhTeamPermissionsKeysAreLiteral: HasLiteralKeys<typeof zhTeamPermissions> = true;
export const zhAutomationKeysAreLiteral: HasLiteralKeys<typeof zhAutomation> = true;
export const zhMailKeysAreLiteral: HasLiteralKeys<typeof zhMail> = true;
export const zhChannelsKeysAreLiteral: HasLiteralKeys<typeof zhChannels> = true;
export const enCommonKeysAreLiteral: HasLiteralKeys<typeof enCommon> = true;
export const enNavKeysAreLiteral: HasLiteralKeys<typeof enNav> = true;
export const enAuthKeysAreLiteral: HasLiteralKeys<typeof enAuth> = true;
export const enDashboardKeysAreLiteral: HasLiteralKeys<typeof enDashboard> = true;
export const enFormatKeysAreLiteral: HasLiteralKeys<typeof enFormat> = true;
export const enConversationsKeysAreLiteral: HasLiteralKeys<typeof enConversations> = true;
export const enNotificationsKeysAreLiteral: HasLiteralKeys<typeof enNotifications> = true;
export const enKnowledgeKeysAreLiteral: HasLiteralKeys<typeof enKnowledge> = true;
export const enChatKeysAreLiteral: HasLiteralKeys<typeof enChat> = true;
export const enSettingsKeysAreLiteral: HasLiteralKeys<typeof enSettings> = true;
export const enTeamPermissionsKeysAreLiteral: HasLiteralKeys<typeof enTeamPermissions> = true;
export const enAutomationKeysAreLiteral: HasLiteralKeys<typeof enAutomation> = true;
export const enMailKeysAreLiteral: HasLiteralKeys<typeof enMail> = true;
export const enChannelsKeysAreLiteral: HasLiteralKeys<typeof enChannels> = true;
export const zhDataKeysAreLiteral: HasLiteralKeys<typeof zhData> = true;
export const enDataKeysAreLiteral: HasLiteralKeys<typeof enData> = true;
export const zhCustomersKeysAreLiteral: HasLiteralKeys<typeof zhCustomers> = true;
export const enCustomersKeysAreLiteral: HasLiteralKeys<typeof enCustomers> = true;
export const zhCalendarKeysAreLiteral: HasLiteralKeys<typeof zhCalendar> = true;
export const enCalendarKeysAreLiteral: HasLiteralKeys<typeof enCalendar> = true;
export const zhIntegrationKeysAreLiteral: HasLiteralKeys<typeof zhIntegration> = true;
export const enIntegrationKeysAreLiteral: HasLiteralKeys<typeof enIntegration> = true;
export const zhServiceOpsKeysAreLiteral: HasLiteralKeys<typeof zhServiceOps> = true;
export const enServiceOpsKeysAreLiteral: HasLiteralKeys<typeof enServiceOps> = true;
export const zhLocalizationKeysAreLiteral: HasLiteralKeys<typeof zhLocalization> = true;
export const enLocalizationKeysAreLiteral: HasLiteralKeys<typeof enLocalization> = true;
