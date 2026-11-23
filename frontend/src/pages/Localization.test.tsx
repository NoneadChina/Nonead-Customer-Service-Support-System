/**
 * `Localization`（翻译管理页）保存路径的回归测试（Task 6/7 第二轮审查 R6 补入）。
 *
 * 修前的缺陷：保存**一个词条**也会 `void switchLanguage(culture)`——它无条件写回整份
 * `PUT /api/profile`，失败被 `void` 吞掉，页面照报「保存成功」，随后的 `reload()`
 * 又把界面拉回**旧语言**的覆盖层。这里用真实的 `useLanguageSwitch` + `I18nProvider`
 * 渲染页面（只 mock HTTP），所以断言的正是"页面到底报了什么"。
 */
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen, act, fireEvent } from '@testing-library/react';
import Localization, { mergeExportEntries } from './Localization';
import { I18nProvider } from '../context/I18nContext';
import { getUiTranslationKeys } from '../i18nKeys';
import { en } from '../locales/en';
import { zhHans } from '../locales/zh-Hans';

const mocks = vi.hoisted(() => ({
  user: null as unknown,
  updateUser: vi.fn(),
  clientGet: vi.fn(),
  clientPut: vi.fn(),
  clientPost: vi.fn(),
  getTranslations: vi.fn(),
  updateProfile: vi.fn(),
  getPasswordPolicy: vi.fn(),
}));

vi.mock('../api/client', () => ({
  default: { get: mocks.clientGet, put: mocks.clientPut, post: mocks.clientPost },
}));

vi.mock('../context/AuthContext', () => ({
  useAuth: () => ({ user: mocks.user, isAuthenticated: true, updateUser: mocks.updateUser }),
}));

vi.mock('../api', () => ({
  localizationApi: { getTranslations: mocks.getTranslations },
  profileApi: { update: mocks.updateProfile },
  authApi: { getPasswordPolicy: mocks.getPasswordPolicy },
}));

const EDITED_KEY = [...getUiTranslationKeys()].sort()[0];

const fullUser = {
  id: 7,
  locale: 'en',
  firstName: 'Ada',
  lastName: 'Lovelace',
  timezone: 'UTC',
  avatar: null,
};

function renderPage() {
  return render(
    <I18nProvider>
      <Localization />
    </I18nProvider>
  );
}

/** 页面上唯一的 `<select>` 就是顶部语言选择 */
function cultureSelect(): HTMLSelectElement {
  return screen.getByRole('combobox') as HTMLSelectElement;
}

/** 每个词条一行：键名 span 的父级是键名行，再上一级是含输入框与保存按钮的整行 */
function saveButtonFor(key: string): HTMLButtonElement {
  const row = screen.getByText(key).closest('div')?.parentElement;
  const button = row?.querySelector('button');
  if (!button) throw new Error(`未找到 ${key} 的保存按钮`);
  return button;
}

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('noneadcss:language', 'en');
  mocks.user = { ...fullUser };
  mocks.updateUser.mockReset();

  mocks.clientGet.mockReset();
  mocks.clientGet.mockImplementation((url: string) => {
    if (url.includes('/stats')) {
      return Promise.resolve({ data: { languages: [] } });
    }
    return Promise.resolve({ data: { translations: {} } });
  });
  mocks.clientPut.mockReset();
  mocks.clientPut.mockResolvedValue({ data: undefined });
  mocks.clientPost.mockReset();
  mocks.clientPost.mockResolvedValue({ data: {} });

  mocks.getTranslations.mockReset();
  mocks.getTranslations.mockResolvedValue({ language: 'en', translations: {} });
  mocks.updateProfile.mockReset();
  mocks.updateProfile.mockResolvedValue({ ...fullUser, locale: 'zh-Hans' });
  mocks.getPasswordPolicy.mockReset();
  mocks.getPasswordPolicy.mockResolvedValue({
    minLength: 6,
    requireUppercase: false,
    requireLowercase: false,
    requireDigit: false,
    requireNonAlphanumeric: false,
    requirements: [],
  });
});

describe('Localization 保存单个词条（R6 回归）', () => {
  it('编辑的语言就是当前界面语言时：不写资料、照报成功，并刷新界面覆盖层', async () => {
    renderPage();
    await act(async () => {});
    expect(cultureSelect().value).toBe('en');
    const reloadCallsBefore = mocks.getTranslations.mock.calls.length;

    await act(async () => {
      fireEvent.click(saveButtonFor(EDITED_KEY));
    });

    // 保存一个词条不该顺带 PUT 整份资料
    expect(mocks.updateProfile).not.toHaveBeenCalled();
    expect(mocks.clientPut).toHaveBeenCalledTimes(1);
    // 文案取自真源（本用例的界面语言是 en），不再硬编码中文字面量
    expect(screen.getByText(en['common.saveSucceeded'])).toBeInTheDocument();
    // 语言没变 → 必须显式重拉覆盖层，否则刚存的词条在界面上看不到
    expect(mocks.getTranslations.mock.calls.length).toBeGreaterThan(reloadCallsBefore);
  });

  it('编辑的语言与当前语言不同且写回失败时：提示切换失败，不再谎报成功', async () => {
    mocks.updateProfile.mockRejectedValue(new Error('403 Forbidden'));

    renderPage();
    await act(async () => {});

    // 切到另一种语言再保存词条（切换只在语言确实不同时才发生）
    await act(async () => {
      fireEvent.change(cultureSelect(), { target: { value: 'zh-Hans' } });
    });

    await act(async () => {
      fireEvent.click(saveButtonFor(EDITED_KEY));
    });

    expect(mocks.updateProfile).toHaveBeenCalledTimes(1);
    expect(mocks.updateProfile).toHaveBeenCalledWith(
      expect.objectContaining({ locale: 'zh-Hans' })
    );
    // 修前这里是「保存成功」（void + 吞异常）；失败必须可见
    expect(screen.queryByText(en['common.saveSucceeded'])).toBeNull();
    expect(screen.getByText(en['common.savedButLanguageSwitchFailed'])).toBeInTheDocument();
  });
});

describe('Localization 导出/导入（P0/P1 回归）', () => {
  /** 页面上那个隐藏的 `<input type="file">` */
  function fileInput(container: HTMLElement): HTMLInputElement {
    const input = container.querySelector('input[type="file"]');
    if (!input) throw new Error('未找到导入文件输入框');
    return input as HTMLInputElement;
  }

  async function importFile(container: HTMLElement, content: string): Promise<void> {
    const input = fileInput(container);
    const file = new File([content], 'translations_en.json', { type: 'application/json' });
    Object.defineProperty(input, 'files', { value: [file], configurable: true });
    await act(async () => {
      fireEvent.change(input);
    });
    // `file.text()` 在 jsdom 下由 FileReader 兜底（见 src/test/setup.ts），
    // 它的 load 是**宏任务**：只 flush 微任务的话，断言会跑在处理函数完成之前。
    await act(async () => {
      await new Promise((resolve) => setTimeout(resolve, 0));
      await new Promise((resolve) => setTimeout(resolve, 0));
    });
  }

  it('导出内容 = 该语言前端真源 ∪ 后端 resx/DB（DB 优先），覆盖页面全部可编辑键', () => {
    // 后端那份：一个后端 resx 键 + 一个已被 DB 定制的**前端**键
    const fromServer = { LoginFailed: 'Sign-in failed', 'nav.localization': 'Localization (custom)' };

    const exported = mergeExportEntries('en', fromServer);
    const uiKeys = getUiTranslationKeys();

    // 页面确实是"数千条前端键"这一量级；只导后端那 62 条就是漏掉 99%
    expect(uiKeys.length).toBeGreaterThan(1000);
    // +1 = 后端专有的 LoginFailed（nav.localization 与前端的键重合）
    expect(Object.keys(exported)).toHaveLength(uiKeys.length + 1);
    // 未被 DB 定制的键取**该语言**的真源（en 导出拿英文，不是中文占位）
    expect(exported[uiKeys[0]]).toBe((en as Record<string, string>)[uiKeys[0]]);
    // DB 覆盖优先于真源；后端专有键原样带出
    expect(exported['nav.localization']).toBe('Localization (custom)');
    expect(exported.LoginFailed).toBe('Sign-in failed');

    // 同一条键在 zh-Hans 下取中文真源（"en 用 en 真源、zh-Hans 用 zh-Hans 真源"）
    expect(mergeExportEntries('zh-Hans', {})['nav.localization']).toBe(
      (zhHans as Record<string, string>)['nav.localization']
    );
  });

  it('导入值不是字符串时：整体拒绝、不发请求，提示真源文案而不是英文 .NET 原文', async () => {
    const { container } = renderPage();
    await act(async () => {});

    await importFile(container, '{"nav.localization": 42}');

    expect(mocks.clientPost).not.toHaveBeenCalled();
    expect(
      screen.getByText(en['localization.message.importInvalidEntries'].replace('{count}', '1'))
    ).toBeInTheDocument();
  });

  it('导入空对象时：提示"没有任何词条"，不再借道后端 400', async () => {
    const { container } = renderPage();
    await act(async () => {});

    await importFile(container, '{}');

    expect(mocks.clientPost).not.toHaveBeenCalled();
    expect(screen.getByText(en['localization.message.importEmpty'])).toBeInTheDocument();
  });

  it('合法文件照常提交，提示里带条数（不再拼接后端中文 message）', async () => {
    mocks.clientPost.mockResolvedValue({ data: { importedCount: 2, message: '成功导入 2 个翻译' } });
    const { container } = renderPage();
    await act(async () => {});

    await importFile(container, '{"nav.localization":"A","LoginFailed":"B"}');

    expect(mocks.clientPost).toHaveBeenCalledTimes(1);
    expect(mocks.clientPost.mock.calls[0][0]).toContain('/import');
    expect(
      screen.getByText(
        en['localization.message.importSucceeded'].replace('{count}', '2')
      )
    ).toBeInTheDocument();
  });
});
