import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from 'react';
import { localizationApi } from '../api';
import { useAuth } from './AuthContext';
import type { TranslationKey } from '../locales/zh-Hans';
import { translate } from '../i18n/translate';
import { BUILT_IN_ENTRIES, setBoundLocale } from '../i18n/locale';
import type { Entries, TranslateParams } from '../i18n/types';

const LANGUAGE_KEY = 'noneadcss:language';
const LEGACY_LANGUAGE_KEY = 'freescout:language';
const SUPPORTED_LANGUAGES = ['zh-Hans', 'en'] as const;
export type SupportedLanguage = (typeof SUPPORTED_LANGUAGES)[number];

/** 内置真源表在 `i18n/locale.ts`（非组件模块也要按当前语言取词条，两边共用同一份） */
const BUILT_IN = BUILT_IN_ENTRIES;
const FALLBACK_LANGUAGE: SupportedLanguage = 'zh-Hans';

/**
 * 开发期把"缺失词条"升级为可见错误。
 * **不要改成 `options?.isDev ?? true`**：漏写开关时，生产上一条缺翻译就会变成 500；
 * 缺省必须是失败安全（返回 key 本身 + 一次性告警）。
 */
const isDev = import.meta.env.DEV;

interface I18nContextValue {
  language: SupportedLanguage;
  /**
   * 切换语言。**契约只收受支持语言**：调用点拿到的原始字符串（下拉框的 `e.target.value`、
   * 翻译管理页的 `culture`）必须先经过 `useLanguageSwitch`／`normalizeLanguage` 归一化。
   * 不要把入参放宽成 `string | null | undefined`——那会把"调用点漏归一化"从编译错误
   * 降级成运行期静默忽略，契约层的保证就此丢失。
   */
  setLanguage: (lang: SupportedLanguage) => void;
  t: (key: TranslationKey, params?: TranslateParams) => string;
  loading: boolean;
  reload: () => void;
}

const I18nContext = createContext<I18nContextValue | undefined>(undefined);

/** localStorage 在隐私模式/被禁用时会抛错，读写一律兜住 */
function readStored(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function writeStored(key: string, value: string): void {
  try {
    localStorage.setItem(key, value);
  } catch {
    // 存不下只影响"下次打开时的语言"，不影响本次会话
  }
}

/**
 * 把任意值收敛到受支持语言。
 * 注意：`zh-Hant` / `zh-TW` 等一律收敛为 `zh-Hans`——本产品只提供简体与英语，
 * 没有繁体资源，收敛比"回落成另一种语言"更可预期。这是**有意为之**，不是遗漏。
 *
 * 导出给调用点复用（`useLanguageSwitch` 在写入前调用它），避免各调用点再写一份
 * 大小写敏感的重复实现。
 *
 * 这里的 eslint-disable 与 `useI18n` 上方那处同理、且**确实被触发**：
 * 本文件同时导出组件（`I18nProvider`）与非组件函数，react-refresh 建议拆文件。
 * 拆走会破坏"归一化规则只此一份"的初衷，故就地抑制。
 */
// eslint-disable-next-line react-refresh/only-export-components
export function normalizeLanguage(value: string | null | undefined): SupportedLanguage | null {
  if (!value) return null;
  const lower = value.toLowerCase();
  if (lower.startsWith('zh')) return 'zh-Hans';
  if (lower.startsWith('en')) return 'en';
  return null;
}

/**
 * 解析初始语言。回落链（依据设计文档 §5.1）：
 *   User.Locale → localStorage → navigator.language → 系统默认(zh-Hans)
 */
function resolveInitialLanguage(userLocale: string | null | undefined): SupportedLanguage {
  const fromUser = normalizeLanguage(userLocale);
  if (fromUser) return fromUser;

  const stored = normalizeLanguage(readStored(LANGUAGE_KEY));
  if (stored) return stored;

  const fromBrowser = normalizeLanguage(typeof navigator === 'undefined' ? null : navigator.language);
  if (fromBrowser) return fromBrowser;

  return FALLBACK_LANGUAGE;
}

function migrateLegacyLanguage(): void {
  const legacy = readStored(LEGACY_LANGUAGE_KEY);
  if (legacy && !readStored(LANGUAGE_KEY)) writeStored(LANGUAGE_KEY, legacy);
}

export function I18nProvider({ children }: { children: ReactNode }) {
  const { user, isAuthenticated } = useAuth();

  const [language, setLanguageState] = useState<SupportedLanguage>(() => {
    migrateLegacyLanguage();
    // `useAuth()` 的 user 在登录态恢复期间可能已带 profile 字段：
    // 此时按回落链首档（User.Locale）直接定语言，避免"先渲染错语言的首帧 +
    // 白拉一次上一语言的覆盖层"。user 未就绪时该参数为 null，落到下一档。
    return resolveInitialLanguage(user?.locale);
  });
  // 覆盖层必须记住它属于哪种语言：切换语言的首帧若仍套用上一语言的覆盖层，
  // 界面会短暂出现"英文里夹中文"的错乱。
  const [overrides, setOverrides] = useState<{ lang: SupportedLanguage; entries: Entries } | null>(null);
  const [loading, setLoading] = useState(true);
  const isMountedRef = useRef(true);

  /**
   * 把当前语言绑定给**非组件模块**（`utils/format.ts` 的 Intl 日期与相对时间、
   * `utils/permissions.ts` 的角色标签、`constants/timezones.ts` 的时区标签）。
   *
   * 有意写在渲染体内而不是 `useEffect`：那些模块的消费点（如仪表盘的
   * `relativeTime(...)`）在**首帧**就要按正确语言产出，effect 在首次绘制之后才跑，
   * 会先渲染出一帧旧语言的时间戳。绑定是幂等的"把当前语言镜像到宿主"，
   * 与 react-redux 的 store 镜像同类，不引入副作用顺序问题。
   */
  setBoundLocale(language);

  // 同步 `<html lang>`：文档语言标签必须跟着界面语言走，否则英文界面对屏幕阅读器
  // 仍声明为中文（zh-Hans），会被用中文语音朗读。同样写在渲染体内，理由同上（幂等镜像）。
  // 生产环境由后端注入静态 `lang`；这里覆盖它是为了让语言切换与首帧都正确。
  if (typeof document !== 'undefined' && document.documentElement.lang !== language) {
    document.documentElement.lang = language;
  }

  // 当前语言的镜像。覆盖层请求是异步的，同一个语言可能在请求在途时又被切走；
  // 迟到的响应必须能被识别并丢弃（见 loadOverrides）。
  const languageRef = useRef(language);

  // 已登录用户的 Locale 是权威来源；它到位后覆盖本地推断的语言
  useEffect(() => {
    const fromUser = normalizeLanguage(user?.locale);
    if (fromUser && fromUser !== language) {
      setLanguageState(fromUser);
      writeStored(LANGUAGE_KEY, fromUser);
    }
    // 仅在 user.locale 变化时纠正，避免与用户手动切换互相打架
  }, [user?.locale]); // eslint-disable-line react-hooks/exhaustive-deps

  const loadOverrides = useCallback(async (lang: SupportedLanguage) => {
    try {
      const data = await localizationApi.getTranslations(lang);
      if (!isMountedRef.current) return;
      // 乱序响应防护：语言已经切走时，这份 payload 属于**上一语言**，
      // 无条件写入会让当前语言的覆盖层被丢弃（overrides.lang 校验随后把它整个作废），
      // 界面就会显示内置文案而不是 DB 覆盖文案。识别不匹配即忽略。
      if (languageRef.current !== lang) return;
      setOverrides({ lang, entries: (data.translations || {}) as Entries });
    } catch {
      // 覆盖层失败不阻断：内置文案已完整
      if (isMountedRef.current && languageRef.current === lang) setOverrides(null);
    } finally {
      // loading 只反映"当前语言"的加载状态：上一语言的迟到响应不得提前结束它
      if (isMountedRef.current && languageRef.current === lang) setLoading(false);
    }
  }, []);

  useEffect(() => {
    isMountedRef.current = true;
    languageRef.current = language;
    if (!isAuthenticated) {
      // 未登录外壳（登录页、公开客服小部件 `/chat/widget`）**不请求覆盖层**：
      // `GET /api/localization/translations/{lang}` 自 2026-10-10 起需要登录（此前标了
      // AllowAnonymous，会把 DB 词条匿名吐出去）。匿名请求会 401 → 客户端拦截器尝试
      // 刷新令牌并跳转登录页，公开页面会被打断；内置真源本身是完整的，退回它即可。
      setOverrides(null);
      setLoading(false);
      return () => {
        isMountedRef.current = false;
      };
    }
    void loadOverrides(language);
    return () => {
      isMountedRef.current = false;
    };
  }, [language, loadOverrides, isAuthenticated]);

  const setLanguage = useCallback((lang: SupportedLanguage) => {
    // 契约已收窄为 `SupportedLanguage`：归一化只在导出的 `normalizeLanguage`
    // （与 `useLanguageSwitch`）一处完成，这里不再做"任意字符串"的兜底，
    // 也就没有"看似接受、实则静默忽略"的模糊语义。
    writeStored(LANGUAGE_KEY, lang);
    setLanguageState(lang);
  }, []);

  const reload = useCallback(() => {
    // 未登录时同样不发请求（同上面的守卫）：reload 的调用点都在登录态页面内，
    // 但公开页若误调也不该把用户弹去登录。
    if (!isAuthenticated) return;
    void loadOverrides(language);
  }, [language, loadOverrides, isAuthenticated]);

  // 覆盖层优先：DB 里配置过的 key 覆盖内置文案（设计文档 §4.3）
  const merged = useMemo<Entries>(() => {
    const builtin = BUILT_IN[language] ?? BUILT_IN[FALLBACK_LANGUAGE];
    const layer = overrides && overrides.lang === language ? overrides.entries : {};
    return { ...builtin, ...layer };
  }, [language, overrides]);

  const t = useCallback(
    (key: TranslationKey, params?: TranslateParams) =>
      translate(merged, language, key, params, {
        isDev,
        onMissing: (k) => console.error(`[i18n] 缺失词条：${k}（locale=${language}）`),
      }),
    [merged, language]
  );

  // 同步标签页标题：同样随界面语言变。`index.html` 里的双语静态标题只是 JS 就绪前的
  // 兜底（首帧之前没有任何 JS 能改它），此处按当前语言覆盖。
  // 与本文件其它镜像一致写在渲染体内（幂等比较后再赋值），语言切换与首帧都正确。
  if (typeof document !== 'undefined') {
    const title = t('common.shell.documentTitle');
    if (document.title !== title) document.title = title;
  }

  // 消费方（Layout/ProfileTab 等）遍布全树：value 每次渲染都新建对象会让所有消费方
  // 无条件重渲染，这里按内容 memo，只有实际字段变化时才通知它们
  const value = useMemo<I18nContextValue>(
    () => ({ language, setLanguage, t, loading, reload }),
    [language, setLanguage, t, loading, reload]
  );

  return <I18nContext.Provider value={value}>{children}</I18nContext.Provider>;
}

// eslint-disable-next-line react-refresh/only-export-components
export function useI18n() {
  const ctx = useContext(I18nContext);
  if (!ctx) throw new Error('useI18n 必须在 I18nProvider 内部使用'); // i18n-exempt: 开发者诊断消息，非用户可见文案
  return ctx;
}

// 注意：这里**不要**加 eslint-disable —— 常量导出不会触发 react-refresh 规则，
// 加了反而会产生一条 "Unused eslint-disable directive" 警告（实施时实测新增第 6 条警告）。
export { SUPPORTED_LANGUAGES };
