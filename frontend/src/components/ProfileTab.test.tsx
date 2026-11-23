/**
 * `ProfileTab` 的回归测试（Task 6/7 第二轮审查 R1 补入）。
 *
 * 此前这个组件**零测试**——而「保存资料」的 timezone 传参回归恰好吃在这个盲区里：
 * ① 时区是**保存才生效**的字段，「保存资料」是它唯一的持久化入口，payload 必须带
 *    下拉框当前值；② 下拉初值必须来自服务端资料，不能是 `usePreferences` 里写死的
 *    `Asia/Shanghai`。两条都只有渲染级断言才锁得住。
 *
 * 只 mock 网络与鉴权（`../api`、`../context/AuthContext`）；`I18nProvider` 用真实实现
 * ——测试 (c) 要验证的正是"语言下拉的取值来自 `useI18n()`"，mock 掉它就没有意义了。
 */
import { describe, it, expect, beforeEach, vi } from 'vitest';
import { render, screen, act, fireEvent } from '@testing-library/react';
import ProfileTab from './ProfileTab';
import { I18nProvider } from '../context/I18nContext';
import { translate } from '../i18n/translate';
import { zhHans } from '../locales/zh-Hans';
import { en } from '../locales/en';

/** provider 只读 `user.locale`；ProfileTab / useLanguageSwitch 还要读资料字段与 `updateUser` */
interface MockUser {
  id: number;
  firstName?: string;
  lastName?: string;
  email?: string;
  role?: number;
  timezone?: string;
  locale?: string;
  avatar?: string | null;
  createdAt?: string;
}

const mocks = vi.hoisted(() => ({
  user: null as unknown,
  logout: vi.fn(),
  updateUser: vi.fn(),
  getProfile: vi.fn(),
  updateProfile: vi.fn(),
  getStats: vi.fn(),
  changePassword: vi.fn(),
  uploadAvatar: vi.fn(),
  deleteAvatar: vi.fn(),
  listMailboxes: vi.fn(),
  getAuditLogs: vi.fn(),
  getTranslations: vi.fn(),
  getPasswordPolicy: vi.fn(),
}));

vi.mock('../context/AuthContext', () => ({
  useAuth: () => ({ user: mocks.user, isAuthenticated: true, logout: mocks.logout, updateUser: mocks.updateUser }),
}));

vi.mock('../api', () => ({
  profileApi: {
    get: mocks.getProfile,
    update: mocks.updateProfile,
    getStats: mocks.getStats,
    changePassword: mocks.changePassword,
    uploadAvatar: mocks.uploadAvatar,
    deleteAvatar: mocks.deleteAvatar,
  },
  mailboxApi: { listAll: mocks.listMailboxes },
  auditLogApi: { getByUser: mocks.getAuditLogs },
  localizationApi: { getTranslations: mocks.getTranslations },
  authApi: { getPasswordPolicy: mocks.getPasswordPolicy },
}));

function serverProfile(overrides: Partial<MockUser> = {}) {
  return {
    id: 7,
    firstName: 'Ada',
    lastName: 'Lovelace',
    email: 'ada@example.com',
    role: 1,
    status: 1,
    fullName: 'Ada Lovelace',
    mailboxIds: [],
    permissions: [],
    createdAt: '2026-01-01T00:00:00Z',
    timezone: 'Asia/Shanghai',
    locale: 'zh-Hans',
    avatar: null,
    ...overrides,
  };
}

function renderTab() {
  return render(
    <I18nProvider>
      <ProfileTab />
    </I18nProvider>
  );
}

/** 页面上没有 `htmlFor`/`id` 关联，按标签文本就近取同一个容器里的 `<select>` */
function selectByLabel(labelText: string): HTMLSelectElement {
  const select = screen.getByText(labelText).closest('div')?.querySelector('select');
  if (!select) throw new Error(`未找到「${labelText}」下拉框`);
  return select;
}

/**
 * 语言下拉：按**选项值**定位（同时含 `zh-Hans` 与 `en`），不依赖标签文案。
 *
 * 用例 (c) 刻意把界面切到英文（`user.locale = 'en'`），此时标签渲染为字典的英文值，
 * 用中文标签去找必然失配——但该用例要验证的是"选中了哪个值"，与标签语言无关。
 */
function languageSelect(): HTMLSelectElement {
  const select = Array.from(document.querySelectorAll('select')).find(
    (s) =>
      Array.from(s.options).some((o) => o.value === 'zh-Hans') &&
      Array.from(s.options).some((o) => o.value === 'en')
  );
  if (!select) throw new Error('未找到语言下拉框');
  return select;
}

/**
 * 「保存资料」按钮文案**从真源字典推导**（`settings.profile.basic.save`），中英两态都接受：
 * 键被改名或删除时，`translate()` 缺 key 会回落到 key 本身，这里就找不到按钮而失败。
 * 写死 `保存资料` 会在英文界面失配（用例 (c) 就是英文界面）。
 */
const SAVE_PROFILE_LABELS = [
  translate(zhHans, 'zh-Hans', 'settings.profile.basic.save'),
  translate(en, 'en', 'settings.profile.basic.save'),
];

async function clickSaveProfile(): Promise<void> {
  await act(async () => {
    const button = screen
      .getAllByRole('button')
      .find((b) => SAVE_PROFILE_LABELS.includes((b.textContent ?? '').trim()));
    if (!button) throw new Error(`未找到「保存资料」按钮（期望文案：${SAVE_PROFILE_LABELS.join(' / ')}）`);
    fireEvent.click(button);
  });
}

beforeEach(() => {
  localStorage.clear();
  mocks.user = {
    id: 7,
    firstName: 'Ada',
    lastName: 'Lovelace',
    timezone: 'Asia/Shanghai',
    locale: 'zh-Hans',
  } satisfies MockUser;

  mocks.getProfile.mockReset();
  mocks.getProfile.mockResolvedValue(serverProfile());
  mocks.updateProfile.mockReset();
  mocks.updateProfile.mockResolvedValue(serverProfile());
  mocks.getStats.mockReset();
  mocks.getStats.mockResolvedValue({ conversationCount: 0, replyCount: 0, onlineMinutes: 0 });
  mocks.listMailboxes.mockReset();
  mocks.listMailboxes.mockResolvedValue([]);
  mocks.getAuditLogs.mockReset();
  mocks.getAuditLogs.mockResolvedValue({ data: [] });
  mocks.getTranslations.mockReset();
  mocks.getTranslations.mockResolvedValue({ language: 'zh-Hans', translations: {} });
  mocks.getPasswordPolicy.mockReset();
  mocks.getPasswordPolicy.mockResolvedValue({
    minLength: 8,
    requireUppercase: false,
    requireLowercase: false,
    requireDigit: false,
    requireNonAlphanumeric: false,
    requirements: [],
  });
  mocks.updateUser.mockReset();
});

describe('ProfileTab「保存资料」的时区语义（R1 回归）', () => {
  it('(a) payload 带的是时区下拉的**当前值**，不是服务端旧值', async () => {
    // 服务端存的是 Asia/Shanghai，用户在唯一的时区控件里改成 Europe/London
    mocks.getProfile.mockResolvedValue(serverProfile({ timezone: 'Asia/Shanghai' }));
    mocks.updateProfile.mockResolvedValue(serverProfile({ timezone: 'Europe/London' }));

    renderTab();
    await act(async () => {});

    fireEvent.change(selectByLabel('时区'), { target: { value: 'Europe/London' } });
    await clickSaveProfile();

    expect(mocks.updateProfile).toHaveBeenCalledTimes(1);
    // 这一条就是回归的位置：改成 `profile?.timezone ?? …` 后这里会拿到 Asia/Shanghai，
    // 用户的选择被静默丢弃，页面却照报「资料已保存」
    expect(mocks.updateProfile).toHaveBeenCalledWith(
      expect.objectContaining({ timezone: 'Europe/London' })
    );
    // 保存后控件仍显示用户选的值（不被服务端回包冲掉）
    expect(selectByLabel('时区').value).toBe('Europe/London');
  });

  it('(b) 资料到位后用服务端时区给下拉做初值（不是写死的 Asia/Shanghai）', async () => {
    mocks.getProfile.mockResolvedValue(serverProfile({ timezone: 'America/New_York' }));
    mocks.updateProfile.mockResolvedValue(serverProfile({ timezone: 'America/New_York' }));

    renderTab();
    await act(async () => {});

    // 修前：控件读的是纯 localStorage 的 preferences.timezone（默认 Asia/Shanghai）
    expect(selectByLabel('时区').value).toBe('America/New_York');

    // 且此时直接保存不会把服务端时区改回本地默认值
    await clickSaveProfile();
    expect(mocks.updateProfile).toHaveBeenCalledWith(
      expect.objectContaining({ timezone: 'America/New_York' })
    );
  });

  it('(c) 语言下拉取值来自 useI18n()，保存时以它为 locale 写回', async () => {
    // user.locale = 'en' 是 provider 的回落链首档；localStorage 偏好仍停在默认 zh-Hans
    mocks.user = {
      id: 7,
      firstName: 'Ada',
      lastName: 'Lovelace',
      timezone: 'UTC',
      locale: 'en',
    } satisfies MockUser;
    mocks.getProfile.mockResolvedValue(serverProfile({ locale: 'en', timezone: 'UTC' }));
    mocks.getTranslations.mockResolvedValue({ language: 'en', translations: {} });

    renderTab();
    await act(async () => {});

    expect(languageSelect().value).toBe('en');

    await clickSaveProfile();

    expect(mocks.updateProfile).toHaveBeenCalledWith(expect.objectContaining({ locale: 'en' }));
  });

  it('(c2) 语言下拉的选项来自统一入口（每种受支持语言一项，不再含 zh-Hant/ja）', async () => {
    renderTab();
    await act(async () => {});

    const options = Array.from(selectByLabel('语言').options).map((o) => o.value);
    expect(options).toEqual(['zh-Hans', 'en']);
  });
});
