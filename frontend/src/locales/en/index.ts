import type { Entry } from '../../i18n/types';
import type { TranslationKey } from '../zh-Hans';
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
 * 英语译文。
 *
 * 显式声明为 Record<TranslationKey, Entry>：
 * **少一条 → 编译错误**（TS2741，漏翻在编译期暴露）。
 *
 * 注意：**"多一条"不会被这个标注挡住**——聚合体只有展开（`{ ...common, ...nav }`），
 * 展开属性不触发 TS 的多余属性检查（excess property check），
 * 且各叶子模块用 `satisfies Entries`（`Record<string, Entry>`）本就允许任意 key。
 * 因此"英文多出中文没有的 key"由 Task 4 的运行期对齐测试兜底锁定；
 * 叶子模块被误写成类型标注（丢键）则由 Task 4 的逐模块字面量键护栏在编译期拦住。
 *
 * 新增模块指引（三步，缺一不可）：
 * ① 在 `zh-Hans/` 与 `en/` 各建一个**同名**模块文件；
 * ② 在本文件追加 import 与展开（`zh-Hans/index.ts` 也要追加同样的 import 与展开）；
 * ③ 模块必须用 `satisfies Entries`，**不要用类型标注**——类型标注（`: Entries`）会让该模块的键
 *    从 `TranslationKey` 静默消失（`tsc` 不报错），详见 `guard.ts` 与本计划 Task 4 Step 1b。
 */
export const en: Record<TranslationKey, Entry> = {
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
};
