import type { Entries } from '../../i18n/types';
import { common } from './common';
import { nav } from './nav';
import { auth } from './auth';
import { dashboard } from './dashboard';
import { format } from './format';
import { conversations } from './conversations';
import { notifications } from './notifications';
import { knowledge } from './knowledge';
import { chat } from './chat';
import { settings } from './settings';
import { teamPermissions } from './teamPermissions';
import { automation } from './automation';
import { mail } from './mail';
import { channels } from './channels';
import { data } from './data';
import { customers } from './customers';
import { calendar } from './calendar';
import { integration } from './integration';
import { serviceOps } from './serviceOps';
import { localization } from './localization';

/**
 * 简体中文真源。
 *
 * 新增模块指引（三步，缺一不可）：
 * ① 在 `zh-Hans/` 与 `en/` 各建一个**同名**模块文件；
 * ② 在本文件追加 import 与展开（`en/index.ts` 也要追加同样的 import 与展开）；
 * ③ 模块必须用 `satisfies Entries`，**不要用类型标注**——类型标注（`: Entries`）会让该模块的键
 *    从 `TranslationKey` 静默消失（`tsc` 不报错），详见 `guard.ts` 与本计划 Task 4 Step 1b。
 */
export const zhHans = {
  ...common,
  ...nav,
  ...auth,
  ...dashboard,
  ...format,
  ...conversations,
  ...notifications,
  ...knowledge,
  ...chat,
  ...settings,
  ...teamPermissions,
  ...automation,
  ...mail,
  ...channels,
  ...data,
  ...customers,
  ...calendar,
  ...integration,
  ...serviceOps,
  ...localization,
} satisfies Entries;

/** 全部合法 key 的联合类型——其它语言必须以它为键集合 */
export type TranslationKey = keyof typeof zhHans;
