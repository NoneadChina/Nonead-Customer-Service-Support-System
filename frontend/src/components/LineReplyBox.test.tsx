/**
 * 审计 #13：LineReplyBox 的分段提示与字符计数必须与后端 LineClient.SplitText 同一口径
 * —— 按 UTF-16 长度计数（C# string.Length），不是 Unicode 码点数；截断也不能切半个代理项。
 *
 * 旧实现用 [...value].length 计数：一个 emoji 只算 1，但它占 2 个及以上 UTF-16 单元，
 * 于是含 emoji 的长文本前端显示 1 段（分片提示压根不出现）、后端实际发 2 段。
 * 这里的断言就是那条差别的直接反证。
 */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { fireEvent, render, screen } from '@testing-library/react';

import { I18nProvider } from '../context/I18nContext';
import { ToastProvider } from '../context/ToastContext';
import LineReplyBox from './LineReplyBox';

// I18nProvider 会读 AuthContext 决定是否拉 DB 覆盖层；未登录 ⇒ 直接用内置真源文案
// （与 src/__tests__/channel-integrations.test.tsx 同一套桩）
vi.mock('../context/AuthContext', () => ({
  useAuth: () => ({
    user: null,
    isAuthenticated: false,
    loading: false,
    error: null,
    login: vi.fn(),
    logout: vi.fn(),
    updateUser: vi.fn(),
  }),
}));

beforeEach(() => {
  localStorage.clear();
  localStorage.setItem('noneadcss:language', 'en');
});

afterEach(() => {
  vi.clearAllMocks();
});

function renderBox(props: { value: string; maxLength: number; onChange?: (v: string) => void }) {
  const onChange = props.onChange ?? vi.fn();
  render(
    <ToastProvider>
      <I18nProvider>
        <LineReplyBox value={props.value} maxLength={props.maxLength} onChange={onChange} onSubmit={vi.fn()} />
      </I18nProvider>
    </ToastProvider>,
  );
  return onChange;
}

describe('components/LineReplyBox · 分段计数与截断按 UTF-16（审计 #13）', () => {
  it('含 emoji 的文本按 UTF-16 计数：显示 4/3 并提示发 2 段（旧口径会显示 2/3 且不提示）', () => {
    renderBox({ value: '😀😀', maxLength: 3 });

    const counter = document.querySelector('span[title="Characters used / limit"]');
    expect(counter, '字符计数条未渲染').not.toBeNull();
    // '😀😀' = 4 个 UTF-16 单元 / 2 个码点 ⇒ 上限 3 时后端必然分成 2 段
    expect(counter?.textContent).toBe('4/3(Will be sent as 2 messages)');
  });

  it('纯 BMP 文本不受影响：未超上限时不出现分片提示', () => {
    renderBox({ value: 'abc', maxLength: 5 });
    const counter = document.querySelector('span[title="Characters used / limit"]');
    expect(counter?.textContent).toBe('3/5');
    expect(screen.queryByText(/Will be sent as/)).toBeNull();
  });

  it('输入超上限时不切半个代理项（onChange 收到的是合法 UTF-16）', () => {
    const onChange = renderBox({ value: '', maxLength: 2 });

    const textarea = screen.getByRole('textbox');
    fireEvent.change(textarea, { target: { value: 'a😀b' } });

    const emitted = onChange.mock.calls.at(-1)?.[0] as string;
    expect(emitted).toBe('a'); // 切点 2 落在高代理项上 ⇒ 退一格，不产出 'a\ud83d'
    expect(emitted).not.toContain('\ud83d');
  });
});
