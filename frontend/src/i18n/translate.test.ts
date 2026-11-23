import { describe, expect, it, vi } from 'vitest';
import { translate } from './translate';
import type { Entries } from './types';

const entries: Entries = {
  'a.plain': '你好',
  'a.greet': '你好，{name}！',
  'a.count': { one: '一个会话', other: '{count} 个会话' },
  'a.en.count': { one: '{count} conversation', other: '{count} conversations' },
};

describe('translate', () => {
  it('返回普通词条', () => {
    expect(translate(entries, 'zh-Hans', 'a.plain')).toBe('你好');
  });

  it('替换插值参数', () => {
    expect(translate(entries, 'zh-Hans', 'a.greet', { name: '张三' })).toBe('你好，张三！');
  });

  it('同一占位符出现多次时全部替换', () => {
    const e: Entries = { 'x': '{a}-{a}' };
    expect(translate(e, 'zh-Hans', 'x', { a: '1' })).toBe('1-1');
  });

  it('未提供的占位符保持原样（不抛错）', () => {
    expect(translate(entries, 'zh-Hans', 'a.greet', {})).toBe('你好，{name}！');
  });

  it('英文 count=1 取 one', () => {
    expect(translate(entries, 'en', 'a.en.count', { count: 1 })).toBe('1 conversation');
  });

  it('英文 count=2 取 other', () => {
    expect(translate(entries, 'en', 'a.en.count', { count: 2 })).toBe('2 conversations');
  });

  it('中文即使 count=1 也取 other（复数规则仅英文生效）', () => {
    expect(translate(entries, 'zh-Hans', 'a.count', { count: 1 })).toBe('1 个会话');
    expect(translate(entries, 'zh-Hans', 'a.count', { count: 5 })).toBe('5 个会话');
  });

  it('英文 count=0 取 other', () => {
    expect(translate(entries, 'en', 'a.en.count', { count: 0 })).toBe('0 conversations');
  });

  it('英文地区码（en-US）同样适用复数规则', () => {
    expect(translate(entries, 'en-US', 'a.en.count', { count: 1 })).toBe('1 conversation');
  });

  it('count 传字符串 "1" 也按单数处理', () => {
    expect(translate(entries, 'en', 'a.en.count', { count: '1' })).toBe('1 conversation');
  });

  it('占位符名撞上原型成员时保持原样（不读原型链）', () => {
    const e: Entries = { 'x': '{constructor}' };
    expect(translate(e, 'zh-Hans', 'x', {})).toBe('{constructor}');
  });

  it('复数对象但未传 count 时取 other（避免崩溃）', () => {
    expect(translate(entries, 'en', 'a.en.count')).toBe('{count} conversations');
  });

  it('缺失 key 时显式开启 isDev 才抛错', () => {
    // 注意：必须显式传 { isDev: true }。
    // 缺省**不抛错**（失败安全）：isDev 未显式要求时，缺失 key 只回落为 key 字符串并上报，
    // 这样即使调用方忘了传 isDev，生产也只是显示 key 而不会 500。
    expect(() => translate(entries, 'zh-Hans', 'missing.key', undefined, { isDev: true }))
      .toThrowError(/missing\.key/);
  });

  it('不传 options 时不抛错（失败安全默认）', () => {
    expect(translate(entries, 'zh-Hans', 'missing.key')).toBe('missing.key');
  });
});

describe('translate 生产环境的缺失 key 行为', () => {
  it('不抛错，返回 key 本身，并只上报一次', () => {
    const report = vi.fn();
    const opts = { isDev: false, onMissing: report };

    expect(translate(entries, 'zh-Hans', 'missing.one', undefined, opts)).toBe('missing.one');
    expect(translate(entries, 'zh-Hans', 'missing.one', undefined, opts)).toBe('missing.one');
    expect(report).toHaveBeenCalledTimes(1);
  });
});

/**
 * ⚠ `reported` 是 `translate.ts` 里的**模块级 Set，永不清理**，会跨用例累积。
 * 因此本节所有 key 都用 `missing.alpha` / `missing.beta` / `missing.legacy.*` 前缀，
 * 且**不与文件内其它用例重名**——新增用例时不要复用这些 key：复用会被去重，
 * "应该上报一次"的断言就会静默失去意义（且不报错）。
 */
describe('缺失 key 的上报：按 key 去重，且不读原型链', () => {
  it('两个不同的缺失 key 各自上报一次，并按调用顺序上报 key 本身', () => {
    const report = vi.fn();
    const opts = { isDev: false, onMissing: report };

    expect(translate(entries, 'zh-Hans', 'missing.alpha', undefined, opts)).toBe('missing.alpha');
    expect(translate(entries, 'zh-Hans', 'missing.beta', undefined, opts)).toBe('missing.beta');

    // 断言**参数**与**顺序**：把 `!reported.has(key)` 改成 `reported.size === 0` 只会剩下第一条，
    // 把 `options.onMissing(key)` 改成 `options.onMissing('')` 会得到 [[''], ['']]——两种突变都在此失败。
    expect(report.mock.calls).toEqual([['missing.alpha'], ['missing.beta']]);
  });

  it('已上报过的 key 再次请求不再上报（按 key 去重，而非"只报第一次"）', () => {
    const report = vi.fn();
    const opts = { isDev: false, onMissing: report };

    // missing.alpha 已由上一条用例上报（reported 永不清理）→ 本次不得再调用；
    // missing.legacy.delta 是全新 key → 必须照常上报。两者一起证明去重是"按 key"而非"只报一次"。
    // 注意本用例依赖上一条用例先执行（vitest 默认按声明顺序）。
    expect(translate(entries, 'zh-Hans', 'missing.alpha', undefined, opts)).toBe('missing.alpha');
    expect(translate(entries, 'zh-Hans', 'missing.legacy.delta', undefined, opts)).toBe('missing.legacy.delta');

    expect(report.mock.calls).toEqual([['missing.legacy.delta']]);
  });

  it('原型链上的属性名不算词条：返回 key 字符串而非 undefined，并照常上报', () => {
    const report = vi.fn();
    const opts = { isDev: false, onMissing: report };
    const sparse: Entries = { a: 'A' };

    expect(translate(sparse, 'zh-Hans', 'constructor', undefined, opts)).toBe('constructor');
    expect(translate(sparse, 'zh-Hans', 'toString', undefined, opts)).toBe('toString');
    expect(report.mock.calls).toEqual([['constructor'], ['toString']]);
  });
});
