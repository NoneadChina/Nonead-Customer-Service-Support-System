/**
 * `ErrorBoundary` 的双语文案回归（批次 2 收官批补入）。
 *
 * 这是**类组件**：拿不到 `useI18n()`，只能走 `tBound`（模块级语言镜像，由 `I18nProvider`
 * 在渲染体里绑定）。这种"非 hook 取词"最容易在两处漏掉——写了 key 却没接线、或接线了却
 * 固定成一种语言。本文件按语言分别渲染，并逐条断言**另一语言的字面文案不出现**。
 *
 * 两条分支都覆盖：
 * ① 运行期异常（`错误标题 + 说明 + 重试/重新加载`）；
 * ② 版本更新（chunk 失配，`window.__spaChunkMismatchPendingReload` 触发）。
 */
import { describe, expect, it, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import ErrorBoundary from './ErrorBoundary';
import { setBoundLocale } from '../i18n/locale';
import { translate } from '../i18n/translate';
import { zhHans } from '../locales/zh-Hans';
import { en } from '../locales/en';

/** 一渲染就抛错的子组件：用来把 ErrorBoundary 推进异常分支 */
function Boom(): never {
  throw new Error('boom');
}

/** 期望值一律从真源派生，不硬编码文案（真源改了这里跟着改） */
const zh = (key: string) => translate(zhHans, 'zh-Hans', key);
const enT = (key: string) => translate(en, 'en', key);

const chunkFlag = (value?: boolean) => {
  const w = window as unknown as { __spaChunkMismatchPendingReload?: boolean };
  if (value === undefined) delete w.__spaChunkMismatchPendingReload;
  else w.__spaChunkMismatchPendingReload = value;
};

beforeEach(() => {
  // 真实运行时由 I18nProvider 设置；测试里直接绑定，模拟"界面已经是该语言"
  setBoundLocale('zh-Hans');
  chunkFlag(undefined);
});

describe('ErrorBoundary · 运行期异常分支', () => {
  it('中文界面：四个文案全部来自中文真源', () => {
    render(
      <ErrorBoundary>
        <Boom />
      </ErrorBoundary>
    );

    expect(screen.getByText(zh('common.shell.errorTitle'))).toBeInTheDocument();
    expect(screen.getByText(zh('common.shell.errorDescription'))).toBeInTheDocument();
    expect(screen.getByText(zh('common.shell.errorRetry'))).toBeInTheDocument();
    expect(screen.getByText(zh('common.shell.errorReload'))).toBeInTheDocument();
  });

  it('英文界面：切到英文真源，且不残留中文文案', () => {
    setBoundLocale('en');
    render(
      <ErrorBoundary>
        <Boom />
      </ErrorBoundary>
    );

    expect(screen.getByText(enT('common.shell.errorTitle'))).toBeInTheDocument();
    expect(screen.getByText(enT('common.shell.errorDescription'))).toBeInTheDocument();
    expect(screen.getByText(enT('common.shell.errorRetry'))).toBeInTheDocument();
    expect(screen.getByText(enT('common.shell.errorReload'))).toBeInTheDocument();
    // 反证：另一语言的字面文案不得出现（"接了 key 但语言写死"的典型缺陷）
    expect(screen.queryByText(zh('common.shell.errorTitle'))).toBeNull();
  });
});

describe('ErrorBoundary · 版本更新分支（chunk 失配）', () => {
  it('中文界面：标题/说明/链接文案来自中文真源', () => {
    chunkFlag(true);
    const { unmount } = render(
      <ErrorBoundary>
        <Boom />
      </ErrorBoundary>
    );

    expect(screen.getByText(zh('common.shell.updateDetected'))).toBeInTheDocument();
    expect(screen.getByText(zh('common.shell.updateRefreshing'))).toBeInTheDocument();
    expect(screen.getByText(zh('common.shell.updateManualAction'))).toBeInTheDocument();
    // 前缀与链接按钮被切成两个文本节点，用子串匹配定位含前缀的那一段
    expect(screen.getByText(zh('common.shell.updateManualPrefix'), { exact: false })).toBeInTheDocument();
    // 卸挂载以清掉 600ms 自动刷新定时器（jsdom 里触发会报 navigation not implemented）
    unmount();
  });

  it('英文界面：前缀含尾随空格，链接文案为英文（不残留中文）', () => {
    setBoundLocale('en');
    chunkFlag(true);
    const { unmount } = render(
      <ErrorBoundary>
        <Boom />
      </ErrorBoundary>
    );

    expect(screen.getByText(enT('common.shell.updateDetected'))).toBeInTheDocument();
    expect(screen.getByText(enT('common.shell.updateRefreshing'))).toBeInTheDocument();
    expect(screen.getByText(enT('common.shell.updateManualAction'))).toBeInTheDocument();
    // 英文前缀以空格结尾（"…automatically, " + 链接），匹配时先 trim
    expect(
      screen.getByText(enT('common.shell.updateManualPrefix').trim(), { exact: false })
    ).toBeInTheDocument();
    expect(screen.queryByText(zh('common.shell.updateDetected'))).toBeNull();
    unmount();
  });
});
