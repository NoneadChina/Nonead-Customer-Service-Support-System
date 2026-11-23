/**
 * `I18nProvider` 行为回归测试。
 *
 * 覆盖 Task 6/7 联合审查指出的缺口：此前两个提交都改了运行期行为却没有一条测试。
 * 这里断言的都是**可观察的运行期行为**（渲染出来的语言、文案、调用次数与参数），
 * 不是对实现细节的复述。
 *
 * 覆盖范围：
 * ① 回落链四档优先级：`User.Locale` > localStorage > navigator.language > `zh-Hans`
 * ② `normalizeLanguage` 全表（`zh-Hant` / `zh-TW` / `en-US` / `EN` / `fr` / `''` / `null` / 垃圾值）
 * ③ `user.locale` 迟到纠正：纠正一次、不与手动切换打架、不循环
 * ④ DB 覆盖层：上一语言的迟到响应不得顶掉当前语言的覆盖层（乱序响应竞态），
 *    **失败**的迟到响应不得清掉当前语言的覆盖层，上一语言的迟到响应也不得提前结束 `loading`
 * ⑤ 覆盖层失败的可观察语义：当前语言的 `reload()` 失败会丢弃已生效的覆盖层并退回内置文案
 * ⑥ `reload()` 按当前语言重新拉取
 * ⑦ `useLanguageSwitch`：乐观切换 / 完整 payload / 失败回滚 / 资料不全时不写后端 /
 *    跨实例在途互斥 / 返回值如实上报成败
 */
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { useState } from 'react';
import { render, screen, act, fireEvent } from '@testing-library/react';
import { I18nProvider, normalizeLanguage, useI18n, type SupportedLanguage } from './I18nContext';
import { useLanguageSwitch } from '../hooks/useLanguageSwitch';

/** provider 只读 `user.locale`；`useLanguageSwitch` 还要读资料字段与 `updateUser` */
interface MockUser {
  locale?: string;
  firstName?: string;
  lastName?: string;
  timezone?: string;
  avatar?: string | null;
}

const mocks = vi.hoisted(() => ({
  user: null as MockUser | null,
  /** 覆盖层是登录态接口：未登录时 provider 不得发请求（公开页/登录页不得被 401 弹走） */
  isAuthenticated: true,
  updateUser: vi.fn(),
  getTranslations: vi.fn(),
  updateProfile: vi.fn(),
}));

vi.mock('./AuthContext', () => ({
  useAuth: () => ({ user: mocks.user, isAuthenticated: mocks.isAuthenticated, updateUser: mocks.updateUser }),
}));

vi.mock('../api', () => ({
  localizationApi: { getTranslations: mocks.getTranslations },
  profileApi: { update: mocks.updateProfile },
}));

const LANGUAGE_KEY = 'noneadcss:language';

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  return { promise, resolve, reject };
}

/** jsdom 的 `navigator.language` 默认是 `en-US`，测试需要按档位控制它 */
function setNavigatorLanguage(value: string): void {
  Object.defineProperty(window.navigator, 'language', { value, configurable: true });
}

function response(lang: string, translations: Record<string, string> = {}) {
  return { language: lang, translations };
}

function Probe({ onRender }: { onRender?: (lang: SupportedLanguage) => void }) {
  const { language, t, loading, setLanguage, reload } = useI18n();
  onRender?.(language);
  return (
    <div>
      <span data-testid="language">{language}</span>
      <span data-testid="save">{t('common.save')}</span>
      <span data-testid="switch-language">{t('common.switchLanguage')}</span>
      <span data-testid="loading">{String(loading)}</span>
      <button data-testid="to-en" onClick={() => setLanguage('en')}>
        to-en
      </button>
      <button data-testid="to-zh" onClick={() => setLanguage('zh-Hans')}>
        to-zh
      </button>
      {/* setLanguage 的契约只收 `SupportedLanguage`（字符串归一化在 useLanguageSwitch 内完成），
          所以这里**没有**传裸字符串（'zh-Hant' / 'pt-BR'）的按钮——那现在是编译错误。
          写路径的归一化由下面 useLanguageSwitch 的用例覆盖。 */}
      <button data-testid="reload" onClick={reload}>
        reload
      </button>
    </div>
  );
}

function renderProvider(onRender?: (lang: SupportedLanguage) => void) {
  return render(
    <I18nProvider>
      <Probe onRender={onRender} />
    </I18nProvider>
  );
}

const shownLanguage = (): string => screen.getByTestId('language').textContent ?? '';
const shownSave = (): string => screen.getByTestId('save').textContent ?? '';

/** 触发并等待一次点击（内部会 flush 微任务与 effect） */
async function click(testId: string): Promise<void> {
  await act(async () => {
    fireEvent.click(screen.getByTestId(testId));
  });
}

beforeEach(() => {
  localStorage.clear();
  mocks.user = null;
  mocks.isAuthenticated = true;
  mocks.updateUser.mockReset();
  mocks.getTranslations.mockReset();
  mocks.getTranslations.mockResolvedValue(response('zh-Hans'));
  mocks.updateProfile.mockReset();
  // 默认让 navigator 档位不干扰：它是"本地推断"的最后一档，除专门测试外都设成英文
  setNavigatorLanguage('en-US');
});

afterEach(() => {
  // 移除 own property，恢复 jsdom 原型上的默认 getter
  Reflect.deleteProperty(window.navigator, 'language');
  localStorage.clear();
});

describe('回落链：User.Locale > localStorage > navigator.language > zh-Hans', () => {
  it('User.Locale 压过 localStorage 与 navigator.language', async () => {
    localStorage.setItem(LANGUAGE_KEY, 'zh-Hans');
    setNavigatorLanguage('zh-Hans');
    mocks.user = { locale: 'en-US' };

    renderProvider();
    await act(async () => {});

    expect(shownLanguage()).toBe('en');
  });

  it('无 User.Locale 时 localStorage 压过 navigator.language', async () => {
    localStorage.setItem(LANGUAGE_KEY, 'en-GB');
    setNavigatorLanguage('zh-Hans');

    renderProvider();
    await act(async () => {});

    expect(shownLanguage()).toBe('en');
  });

  it('无 User.Locale、无 localStorage 时取 navigator.language', async () => {
    setNavigatorLanguage('zh-TW');

    renderProvider();
    await act(async () => {});

    expect(shownLanguage()).toBe('zh-Hans');
  });

  it('四档全空时落到系统默认 zh-Hans', async () => {
    setNavigatorLanguage('fr-CA');

    renderProvider();
    await act(async () => {});

    expect(shownLanguage()).toBe('zh-Hans');
  });

  it('User.Locale 缺省（undefined）时不误判、继续走下一档', async () => {
    localStorage.setItem(LANGUAGE_KEY, 'en');
    mocks.user = {};

    renderProvider();
    await act(async () => {});

    expect(shownLanguage()).toBe('en');
  });
});

describe('normalizeLanguage 收敛表（经回落链观察）', () => {
  it.each([
    ['zh-Hant', 'zh-Hans'],
    ['zh-TW', 'zh-Hans'],
    ['zh-Hans', 'zh-Hans'],
    ['ZH-hant', 'zh-Hans'],
    ['en-US', 'en'],
    ['EN', 'en'],
    ['en', 'en'],
    // 前缀收敛：只要以 zh / en 开头即认，'zh' 与 'english' 都算命中（有意为之的宽松规则）
    ['zh', 'zh-Hans'],
    ['english', 'en'],
  ])('localStorage=%s → %s', async (stored, expected) => {
    localStorage.setItem(LANGUAGE_KEY, stored);
    setNavigatorLanguage('fr-CA'); // 归一化为 null，若 localStorage 档失效就会落到 zh-Hans

    renderProvider();
    await act(async () => {});

    expect(shownLanguage()).toBe(expected);
  });

  it.each([['fr'], [''], ['null'], ['garbage-!!'], ['de-DE'], ['jp']])(
    'localStorage=%s 无法归一化 → 跳过该档，落到 navigator 档',
    async (stored) => {
      localStorage.setItem(LANGUAGE_KEY, stored);
      setNavigatorLanguage('en');

      renderProvider();
      await act(async () => {});

      expect(shownLanguage()).toBe('en');
    }
  );

  it('localStorage 项不存在（null）时跳过该档', async () => {
    setNavigatorLanguage('en');

    renderProvider();
    await act(async () => {});

    expect(shownLanguage()).toBe('en');
  });

  it.each([
    ['zh-Hant', 'zh-Hans'],
    ['en-US', 'en'],
    ['fr', 'zh-Hans'],
    ['', 'zh-Hans'],
  ])('navigator.language=%s → %s（无更高优先级来源时）', async (navLang, expected) => {
    setNavigatorLanguage(navLang);

    renderProvider();
    await act(async () => {});

    expect(shownLanguage()).toBe(expected);
  });
});

describe('user.locale 迟到纠正', () => {
  it('资料到位后按 User.Locale 纠正一次', async () => {
    localStorage.setItem(LANGUAGE_KEY, 'zh-Hans');
    mocks.user = null;

    const { rerender } = renderProvider();
    await act(async () => {});
    expect(shownLanguage()).toBe('zh-Hans');

    mocks.user = { locale: 'en' };
    await act(async () => {
      rerender(
        <I18nProvider>
          <Probe />
        </I18nProvider>
      );
    });

    expect(shownLanguage()).toBe('en');
    // 纠正后的语言要落盘，刷新后本地推断才不会与后端打架
    expect(localStorage.getItem(LANGUAGE_KEY)).toBe('en');
  });

  it('纠正不会陷入循环（user.locale 不变则不再触发渲染）', async () => {
    const renders: string[] = [];
    localStorage.setItem(LANGUAGE_KEY, 'zh-Hans');
    setNavigatorLanguage('zh-Hans');
    mocks.user = null; // 首帧资料未就绪

    const { rerender } = renderProvider((lang) => renders.push(lang));
    await act(async () => {});
    expect(renders[0]).toBe('zh-Hans');
    expect(shownLanguage()).toBe('zh-Hans');

    // 资料迟到到位：按 User.Locale 纠正为 en
    mocks.user = { locale: 'en' };
    await act(async () => {
      rerender(
        <I18nProvider>
          <Probe onRender={(lang) => renders.push(lang)} />
        </I18nProvider>
      );
    });
    expect(shownLanguage()).toBe('en');

    // 再 flush 一轮，确保覆盖层与 loading 都落地，然后才测量"是否还在增长"
    await act(async () => {});
    const settled = renders.length;
    await act(async () => {});
    await act(async () => {});
    expect(renders.length).toBe(settled);
    expect(renders[renders.length - 1]).toBe('en');

    // 同一 locale 再次渲染最多带来一次普通重渲染
    await act(async () => {
      rerender(
        <I18nProvider>
          <Probe onRender={(lang) => renders.push(lang)} />
        </I18nProvider>
      );
    });
    expect(renders.length).toBeLessThanOrEqual(settled + 1);
    expect(renders[renders.length - 1]).toBe('en');
  });

  it('不覆盖用户的手动切换（user.locale 未变时不把语言纠正回去）', async () => {
    mocks.user = { locale: 'zh-Hans' };

    const { rerender } = renderProvider();
    await act(async () => {});
    expect(shownLanguage()).toBe('zh-Hans');

    await click('to-en');
    expect(shownLanguage()).toBe('en');

    await act(async () => {
      rerender(
        <I18nProvider>
          <Probe />
        </I18nProvider>
      );
    });

    expect(shownLanguage()).toBe('en');
  });
});

describe('DB 覆盖层', () => {
  it('切换语言时，上一语言的覆盖响应迟到也不得顶掉当前语言的覆盖层', async () => {
    localStorage.setItem(LANGUAGE_KEY, 'zh-Hans');
    const zhRequest = deferred<ReturnType<typeof response>>();
    const enRequest = deferred<ReturnType<typeof response>>();
    mocks.getTranslations.mockImplementation((lang: string) =>
      lang === 'en' ? enRequest.promise : zhRequest.promise
    );

    renderProvider();
    await act(async () => {});
    expect(mocks.getTranslations).toHaveBeenCalledWith('zh-Hans');

    await click('to-en');
    expect(mocks.getTranslations).toHaveBeenCalledWith('en');

    // 当前语言（en）的覆盖层先到，界面用的是 DB 覆盖文案
    await act(async () => {
      enRequest.resolve(response('en', { 'common.save': 'EN-OVERRIDE' }));
    });
    expect(shownSave()).toBe('EN-OVERRIDE');

    // 上一语言（zh-Hans）的响应迟到：必须被忽略，否则当前语言的覆盖层被丢弃、
    // 界面退回英文内置文案（'Save'），而 zh-Hans 的覆盖文案又不该出现在 en 下
    await act(async () => {
      zhRequest.resolve(response('zh-Hans', { 'common.save': 'ZH-OVERRIDE' }));
    });
    expect(shownSave()).toBe('EN-OVERRIDE');
  });

  it('切换语言后、新语言覆盖层到达前，不渲染上一语言的覆盖文案', async () => {
    localStorage.setItem(LANGUAGE_KEY, 'zh-Hans');
    const enRequest = deferred<ReturnType<typeof response>>();
    mocks.getTranslations.mockImplementation((lang: string) =>
      lang === 'en'
        ? enRequest.promise
        : Promise.resolve(response('zh-Hans', { 'common.save': 'ZH-OVERRIDE' }))
    );

    renderProvider();
    await act(async () => {});
    // zh-Hans 覆盖层先落地并生效
    expect(shownLanguage()).toBe('zh-Hans');
    expect(shownSave()).toBe('ZH-OVERRIDE');

    await click('to-en');

    // en 的覆盖层还没到：应显示英文内置文案，绝不能残留中文覆盖文案
    expect(shownSave()).toBe('Save');
    expect(shownSave()).not.toBe('ZH-OVERRIDE');
  });

  it('失败清理只作用于当前语言：上一语言的请求**失败**迟到时不得清掉当前语言的覆盖层', async () => {
    // 这条锁的是 catch 里的 `languageRef.current === lang` 守卫：
    // 去掉守卫（catch 里无条件 setOverrides(null)）时，上一语言的失败会把当前语言
    // 已生效的覆盖层整个清空，界面退回内置文案。
    localStorage.setItem(LANGUAGE_KEY, 'zh-Hans');
    const staleZh = deferred<ReturnType<typeof response>>();
    let zhCalls = 0;
    mocks.getTranslations.mockImplementation((lang: string) => {
      if (lang === 'en') return Promise.resolve(response('en', { 'common.save': 'EN-OVERRIDE' }));
      zhCalls += 1;
      // 第一次 zh-Hans（挂载时）立即成功；第二次（reload 触发）一直挂着，稍后让它失败
      return zhCalls === 1
        ? Promise.resolve(response('zh-Hans', { 'common.save': 'ZH-OVERRIDE' }))
        : staleZh.promise;
    });

    renderProvider();
    await act(async () => {});
    expect(shownSave()).toBe('ZH-OVERRIDE');

    // 仍是 zh-Hans 时再拉一次 → 制造一个"属于上一语言、结果迟到"的在途请求
    await click('reload');
    expect(zhCalls).toBe(2);

    await click('to-en');
    expect(shownSave()).toBe('EN-OVERRIDE');

    // 那个 zh-Hans 的在途请求现在失败了
    await act(async () => {
      staleZh.reject(new Error('network'));
    });

    // 当前语言是 en：en 的覆盖层必须原样保留
    expect(shownSave()).toBe('EN-OVERRIDE');
  });

  it('上一语言的迟到响应不得提前结束 loading：要等**当前语言**的响应落地', async () => {
    // 这条锁的是 finally 里的 `languageRef.current === lang` 守卫：
    // 去掉守卫（无条件 setLoading(false)）时，上一语言的迟到响应会过早把 loading 置假，
    // 消费方据此认为"当前语言的覆盖层已就绪"，于是先渲染内置文案再被覆盖层顶掉。
    localStorage.setItem(LANGUAGE_KEY, 'zh-Hans');
    const zhRequest = deferred<ReturnType<typeof response>>();
    const enRequest = deferred<ReturnType<typeof response>>();
    mocks.getTranslations.mockImplementation((lang: string) =>
      lang === 'en' ? enRequest.promise : zhRequest.promise
    );

    renderProvider();
    await act(async () => {});
    expect(screen.getByTestId('loading').textContent).toBe('true');

    await click('to-en');
    expect(screen.getByTestId('loading').textContent).toBe('true');

    // zh-Hans（上一语言）的响应先回来：它无权结束当前语言的 loading
    await act(async () => {
      zhRequest.resolve(response('zh-Hans', { 'common.save': 'ZH-OVERRIDE' }));
    });
    expect(screen.getByTestId('loading').textContent).toBe('true');
    // 上一语言的 payload 也不得写进覆盖层
    expect(shownSave()).not.toBe('ZH-OVERRIDE');

    // 当前语言（en）的响应落地后才结束
    await act(async () => {
      enRequest.resolve(response('en', { 'common.save': 'EN-OVERRIDE' }));
    });
    expect(screen.getByTestId('loading').textContent).toBe('false');
    expect(shownSave()).toBe('EN-OVERRIDE');
  });

  it('首帧拉取失败时保留内置文案，不阻断渲染', async () => {
    // 注意：这条**无法区分 catch 的具体实现**（首帧失败时覆盖层本就是 null，
    // 空 catch 与 setOverrides(null) 表现一致）。失败路径的可观察语义由下一条
    // 「已生效的覆盖层在 reload 失败后被丢弃」负责钉住。
    localStorage.setItem(LANGUAGE_KEY, 'zh-Hans');
    mocks.getTranslations.mockRejectedValue(new Error('403'));

    renderProvider();
    await act(async () => {});

    expect(shownSave()).toBe('保存');
    expect(screen.getByTestId('loading').textContent).toBe('false');
  });

  it('当前语言的 reload() 失败会丢弃已生效的覆盖层、退回内置文案（catch 的可观察语义）', async () => {
    localStorage.setItem(LANGUAGE_KEY, 'zh-Hans');
    mocks.getTranslations.mockResolvedValueOnce(response('zh-Hans', { 'common.save': 'ZH-OVERRIDE' }));

    renderProvider();
    await act(async () => {});
    expect(shownSave()).toBe('ZH-OVERRIDE');

    const failed = deferred<ReturnType<typeof response>>();
    mocks.getTranslations.mockImplementation(() => failed.promise);
    await click('reload');

    await act(async () => {
      failed.reject(new Error('network'));
    });

    // 生产语义：catch 里 setOverrides(null) —— 覆盖层被丢弃、退回内置文案。
    // 把 catch 体清空（空 catch）时这里会仍是 'ZH-OVERRIDE'，本用例即失败。
    //
    // 「失败时保留上一份好数据」同样是一种说得通的设计（不会因为一次网络抖动丢掉
    // 已加载的覆盖文案）；本产品选择失败即回落内置文案。若将来改成语义，请连同本用例
    // 一起改——关键是这条路径必须**可被断言**，不能是空 catch。
    expect(shownSave()).toBe('保存');
  });

  it('失败后切换语言会重新拉取当前语言的覆盖层', async () => {
    localStorage.setItem(LANGUAGE_KEY, 'zh-Hans');
    mocks.getTranslations.mockRejectedValue(new Error('network'));

    renderProvider();
    await act(async () => {});
    expect(shownSave()).toBe('保存');

    mocks.getTranslations.mockReset();
    mocks.getTranslations.mockResolvedValue(response('en', { 'common.save': 'EN-OVERRIDE' }));

    await click('to-en');
    expect(mocks.getTranslations).toHaveBeenCalledWith('en');
    expect(shownSave()).toBe('EN-OVERRIDE');
  });

  it('reload() 按当前语言重新拉取', async () => {
    localStorage.setItem(LANGUAGE_KEY, 'en');
    renderProvider();
    await act(async () => {});
    expect(mocks.getTranslations).toHaveBeenCalledTimes(1);
    expect(mocks.getTranslations).toHaveBeenLastCalledWith('en');

    await click('reload');
    expect(mocks.getTranslations).toHaveBeenCalledTimes(2);
    expect(mocks.getTranslations).toHaveBeenLastCalledWith('en');
  });

  it('reload() 在切换语言后拉取的是新语言', async () => {
    localStorage.setItem(LANGUAGE_KEY, 'zh-Hans');
    renderProvider();
    await act(async () => {});

    await click('to-en');
    await click('reload');

    expect(mocks.getTranslations).toHaveBeenLastCalledWith('en');
  });
});

describe('normalizeLanguage（导出后直接断言全表）', () => {
  it.each([
    ['zh-Hans', 'zh-Hans'],
    ['zh-Hant', 'zh-Hans'],
    ['zh-TW', 'zh-Hans'],
    ['zh-CN', 'zh-Hans'],
    ['ZH-HANT', 'zh-Hans'],
    ['en', 'en'],
    ['en-US', 'en'],
    ['EN', 'en'],
    ['En-gb', 'en'],
  ])('%s → %s', (input, expected) => {
    expect(normalizeLanguage(input)).toBe(expected);
  });

  it.each([['fr'], ['de-DE'], [''], ['   '], ['null'], ['undefined'], ['gar'], ['123']])(
    '%s 不受支持 → null',
    (input) => {
      expect(normalizeLanguage(input)).toBeNull();
    }
  );

  it('null / undefined → null', () => {
    expect(normalizeLanguage(null)).toBeNull();
    expect(normalizeLanguage(undefined)).toBeNull();
  });
});

// 说明（Task 6/7 第二轮审查 R4）：原先这里有一个「setLanguage 写路径归一化」的 describe，
// 靠给 `setLanguage` 传裸字符串（'zh-Hant' / 'pt-BR'）来观察收敛。契约收窄为
// `SupportedLanguage` 后，那两条用例本身就是编译错误；写路径的归一化只在
// `useLanguageSwitch` 的入参上存在，故迁移到下面 hook 的 describe 里覆盖。

describe('首帧语言', () => {
  it('首帧即采用 User.Locale，不先渲染 localStorage 语言、也不白拉一次', async () => {
    localStorage.setItem(LANGUAGE_KEY, 'zh-Hans');
    setNavigatorLanguage('zh-Hans');
    mocks.user = { locale: 'en' };
    const renders: string[] = [];

    renderProvider((lang) => renders.push(lang));

    // 首帧
    expect(renders[0]).toBe('en');
    expect(renders).not.toContain('zh-Hans');
    // 只请求当前语言的覆盖层，没有为被纠正掉的语言白拉一次
    expect(mocks.getTranslations).toHaveBeenCalledTimes(1);
    expect(mocks.getTranslations).toHaveBeenCalledWith('en');

    await act(async () => {});
    expect(shownLanguage()).toBe('en');
    expect(mocks.getTranslations).toHaveBeenCalledTimes(1);
  });

  it('user 未就绪（null）时仍按 localStorage 首帧渲染', async () => {
    localStorage.setItem(LANGUAGE_KEY, 'en');
    const renders: string[] = [];

    renderProvider((lang) => renders.push(lang));

    expect(renders[0]).toBe('en');
    // flush 掉在途的覆盖层请求，避免它的 setState 落到后续用例里
    await act(async () => {});
  });
});

describe('context value 引用稳定性', () => {
  it('provider 重渲染且依赖未变时，context value 是同一个对象（消费方不被无谓重渲染）', async () => {
    localStorage.setItem(LANGUAGE_KEY, 'en');
    const values: unknown[] = [];

    function Consumer() {
      const value = useI18n();
      values.push(value);
      return <span data-testid="consumer-language">{value.language}</span>;
    }

    const { rerender } = render(
      <I18nProvider>
        <Consumer />
      </I18nProvider>
    );
    await act(async () => {}); // 等覆盖层与 loading 落地，value 进入稳定态

    const settled = values.length;
    const settledValue = values[settled - 1];
    expect(settledValue).toBeDefined();

    // 一次纯粹的 provider 重渲染：没有任何依赖发生变化
    rerender(
      <I18nProvider>
        <Consumer />
      </I18nProvider>
    );

    expect(values.length).toBeGreaterThan(settled); // 消费方确实重渲染了
    expect(values[values.length - 1]).toBe(settledValue); // 但拿到的是同一个 value
  });
});

describe('未登录守卫：覆盖层只对登录态请求（P2）', () => {
  it('未登录时不请求覆盖层、loading 立即结束，文案回退内置真源', async () => {
    localStorage.setItem(LANGUAGE_KEY, 'en');
    mocks.isAuthenticated = false;
    // 故意准备一份覆盖层：用它证明"确实没发请求"，而不是"请求失败后回退"
    mocks.getTranslations.mockResolvedValue(response('en', { 'common.save': 'OVERRIDDEN' }));

    renderProvider();
    await act(async () => {});

    // `GET /api/localization/translations/{lang}` 是登录态接口；匿名请求会 401，
    // 客户端拦截器随后会刷新令牌并跳登录页——公开页（/chat/widget）会被打断
    expect(mocks.getTranslations).not.toHaveBeenCalled();
    expect(screen.getByTestId('loading').textContent).toBe('false');
    expect(shownSave()).not.toBe('OVERRIDDEN');
  });
});

describe('useLanguageSwitch（三处切换入口的统一实现）', () => {
  function SwitchProbe() {
    const { language, saving, switchLanguage } = useLanguageSwitch();
    // 切换的返回值是调用点（翻译管理页）唯一的成败依据，必须可观察
    const [result, setResult] = useState('');
    const run = (value: string) => {
      void switchLanguage(value).then((ok) => setResult(String(ok)));
    };
    return (
      <div>
        <span data-testid="hook-language">{language}</span>
        <span data-testid="hook-saving">{String(saving)}</span>
        <span data-testid="hook-result">{result}</span>
        <button data-testid="hook-to-en" onClick={() => run('en')}>
          en
        </button>
        <button data-testid="hook-to-zhant" onClick={() => run('zh-Hant')}>
          zh-Hant
        </button>
        <button data-testid="hook-to-ptbr" onClick={() => run('pt-BR')}>
          pt-BR
        </button>
      </div>
    );
  }

  /**
   * 两个实例挂在同一棵树上（`Layout` 的顶栏切换器 + `ProfileTab` 的下拉），
   * 并且**同一个 tick 内**触发两次切换：只有模块级的在途标记能挡住第二个 PUT
   * ——每实例一份 `saving` 时，第二个实例看到的是自己那份还没变的状态。
   */
  function DualSwitchProbe() {
    const first = useLanguageSwitch();
    const second = useLanguageSwitch();
    const [results, setResults] = useState('');
    return (
      <div>
        <span data-testid="dual-language">{first.language}</span>
        <span data-testid="dual-saving">{`${first.saving}/${second.saving}`}</span>
        <span data-testid="dual-results">{results}</span>
        <button
          data-testid="dual-switch"
          onClick={() => {
            void Promise.all([first.switchLanguage('en'), second.switchLanguage('en')]).then((r) =>
              setResults(r.map(String).join(','))
            );
          }}
        >
          dual
        </button>
      </div>
    );
  }

  function renderSwitchProbe() {
    return render(
      <I18nProvider>
        <SwitchProbe />
      </I18nProvider>
    );
  }

  function renderDualSwitchProbe() {
    return render(
      <I18nProvider>
        <DualSwitchProbe />
      </I18nProvider>
    );
  }

  const shownHookLanguage = (): string => screen.getByTestId('hook-language').textContent ?? '';
  const shownHookSaving = (): string => screen.getByTestId('hook-saving').textContent ?? '';
  const shownHookResult = (): string => screen.getByTestId('hook-result').textContent ?? '';

  const fullProfile: MockUser = {
    locale: 'zh-Hans',
    firstName: 'Ada',
    lastName: 'Lovelace',
    timezone: 'UTC',
    avatar: 'avatar.png',
  };

  beforeEach(() => {
    localStorage.setItem(LANGUAGE_KEY, 'zh-Hans');
  });

  it('乐观切换 + 完整 payload 写回 + 用响应同步全局 user', async () => {
    mocks.user = { ...fullProfile };
    const request = deferred<MockUser>();
    mocks.updateProfile.mockReturnValue(request.promise);

    renderSwitchProbe();
    await act(async () => {});
    expect(shownHookLanguage()).toBe('zh-Hans');

    await click('hook-to-en');

    // 乐观：不等后端就已经切过去
    expect(shownHookLanguage()).toBe('en');
    expect(shownHookSaving()).toBe('true');
    // PUT /api/profile 是整份替换：五个字段一个都不能少
    expect(mocks.updateProfile).toHaveBeenCalledWith({
      firstName: 'Ada',
      lastName: 'Lovelace',
      timezone: 'UTC',
      locale: 'en',
      avatar: 'avatar.png',
    });

    const updated: MockUser = { ...fullProfile, locale: 'en' };
    await act(async () => {
      request.resolve(updated);
    });

    expect(mocks.updateUser).toHaveBeenCalledWith(updated);
    expect(shownHookLanguage()).toBe('en');
    expect(shownHookSaving()).toBe('false');
    expect(shownHookResult()).toBe('true');
  });

  it('无头像时 avatar 传 undefined（不把 null 写进整份替换的 payload）', async () => {
    mocks.user = { ...fullProfile, avatar: null };
    mocks.updateProfile.mockResolvedValue({ ...fullProfile, locale: 'en' });

    renderSwitchProbe();
    await act(async () => {});
    await click('hook-to-en');

    expect(mocks.updateProfile).toHaveBeenCalledWith(
      expect.objectContaining({ avatar: undefined })
    );
  });

  it('写回失败（非超管的 403）回滚到原语言且不抛错', async () => {
    mocks.user = { ...fullProfile };
    mocks.updateProfile.mockRejectedValue(new Error('403 Forbidden'));

    renderSwitchProbe();
    await act(async () => {});
    await click('hook-to-en');

    // 回滚：界面语言与后端必须一致
    expect(shownHookLanguage()).toBe('zh-Hans');
    expect(localStorage.getItem(LANGUAGE_KEY)).toBe('zh-Hans');
    expect(shownHookSaving()).toBe('false');
    expect(mocks.updateUser).not.toHaveBeenCalled();
    // 失败必须**如实上报**：翻译管理页据此提示"切换失败"而不是照报「保存成功」
    expect(shownHookResult()).toBe('false');
    // 组件仍然可用（失败没有冒泡成未捕获异常）
    expect(screen.getByTestId('hook-language')).toBeInTheDocument();
  });

  it('资料未完整加载时只切界面、不写后端（避免整份替换清空资料）', async () => {
    mocks.user = { locale: 'zh-Hans' }; // 无 firstName/lastName/timezone

    renderSwitchProbe();
    await act(async () => {});
    await click('hook-to-en');

    expect(shownHookLanguage()).toBe('en');
    expect(mocks.updateProfile).not.toHaveBeenCalled();
    expect(shownHookSaving()).toBe('false');
    // 语言确实切了，只是没持久化——对调用点而言不是失败
    expect(shownHookResult()).toBe('true');
  });

  it('传入 zh-Hant 时按归一化结果写回（不把原值发给后端）', async () => {
    mocks.user = { ...fullProfile };
    mocks.updateProfile.mockResolvedValue({ ...fullProfile, locale: 'zh-Hans' });

    renderSwitchProbe();
    await act(async () => {});
    await click('hook-to-zhant');

    // zh-Hant 归一化为 zh-Hans，与当前语言相同 → 不产生无谓写回
    expect(shownHookLanguage()).toBe('zh-Hans');
    expect(mocks.updateProfile).not.toHaveBeenCalled();
    expect(shownHookResult()).toBe('true');
  });

  it('系统语言不是 zh-Hans 时，zh-Hant 归一化后确实发生切换并写回', async () => {
    mocks.user = { ...fullProfile, locale: 'en' };
    localStorage.setItem(LANGUAGE_KEY, 'en');
    mocks.updateProfile.mockResolvedValue({ ...fullProfile, locale: 'zh-Hans' });

    renderSwitchProbe();
    await act(async () => {});
    expect(shownHookLanguage()).toBe('en');

    await click('hook-to-zhant');

    expect(shownHookLanguage()).toBe('zh-Hans');
    // 归一化后的值落盘（不是把 'zh-Hant' 原样存进去）
    expect(localStorage.getItem(LANGUAGE_KEY)).toBe('zh-Hans');
    expect(mocks.updateProfile).toHaveBeenCalledWith(
      expect.objectContaining({ locale: 'zh-Hans' })
    );
    expect(shownHookResult()).toBe('true');
  });

  it('传入不受支持的语言时不改状态、不写 localStorage、不写后端，并回报 false', async () => {
    mocks.user = { ...fullProfile };

    renderSwitchProbe();
    await act(async () => {});
    expect(shownHookLanguage()).toBe('zh-Hans');

    await click('hook-to-ptbr');

    expect(shownHookLanguage()).toBe('zh-Hans');
    expect(localStorage.getItem(LANGUAGE_KEY)).toBe('zh-Hans');
    expect(mocks.updateProfile).not.toHaveBeenCalled();
    expect(shownHookResult()).toBe('false');
  });

  it('两个实例（顶栏切换器 + 个人中心下拉）不能同时在途：只发一次 PUT', async () => {
    mocks.user = { ...fullProfile };
    const request = deferred<MockUser>();
    mocks.updateProfile.mockReturnValue(request.promise);

    renderDualSwitchProbe();
    await act(async () => {});
    expect(screen.getByTestId('dual-language').textContent).toBe('zh-Hans');

    // 同一个 tick 内两个实例各切一次（用户连点两处）
    await click('dual-switch');

    // 每实例一份 saving 时这里会是 2 次：第二个实例看到的是自己那份还没变的状态，
    // 于是两个 PUT /api/profile 并发，谁后到谁生效、最后一次点击被改回去
    expect(mocks.updateProfile).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('dual-language').textContent).toBe('en');
    expect(screen.getByTestId('dual-saving').textContent).toBe('true/false');

    await act(async () => {
      request.resolve({ ...fullProfile, locale: 'en' });
    });

    // 第一个实例成功、第二个实例如实回报失败（调用点据此提示，而不是静默丢弃）
    expect(screen.getByTestId('dual-results').textContent).toBe('true,false');
    expect(screen.getByTestId('dual-saving').textContent).toBe('false/false');
  });
});

