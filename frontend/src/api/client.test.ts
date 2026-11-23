import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import type { AxiosAdapter, AxiosRequestConfig, AxiosResponse, InternalAxiosRequestConfig } from 'axios';
import client from './client';
import { getBoundLocale, setBoundLocale } from '../i18n/locale';

/**
 * 语言头守卫（i18n 审计 2026-11-20）。
 *
 * 后端 `RequestLocalization` 的判定链是 `?lang=` → Cookie → `Accept-Language` → 默认 zh-Hans
 * （`NoneadCSS.Api/Program.cs:79-92`）。浏览器自动附带的 `Accept-Language` 是**操作系统语言**，
 * 与用户在本系统里切换的界面语言无关；因此前端必须显式把界面语言写进该头，
 * 否则英文界面的用户会收到中文的后端文案（信封 message、枚举标签、已本地化端点）。
 *
 * 这里不连真实后端：用桩 adapter 截获即将发出的请求配置，断言头值就是 `getBoundLocale()`。
 * 后端确实按该头切换 culture，已有 HTTP 实测：`GET /api/auth/captcha` 的信封 `message`
 * 在 `Accept-Language: en` 下返回 "Success"，不带该头或带 `zh-Hans` 时返回「操作成功」。
 */
const captured: Array<{ url: string; language: string }> = [];

const stubAdapter: AxiosAdapter = (config: InternalAxiosRequestConfig) => {
  captured.push({
    url: config.url ?? '',
    language: String(config.headers?.['Accept-Language'] ?? ''),
  });
  return Promise.resolve({
    data: { probed: true },
    status: 200,
    statusText: 'OK',
    headers: {},
    config,
  } as AxiosResponse);
};

const originalAdapter = client.defaults.adapter;

beforeEach(() => {
  captured.length = 0;
  client.defaults.adapter = stubAdapter;
});

afterEach(() => {
  client.defaults.adapter = originalAdapter;
  setBoundLocale(undefined); // 回落 FALLBACK_LOCALE，避免污染其它用例
});

/** 每次换一条 URL：GET 去重缓存（INFLIGHT_WINDOW_MS）会复用同 URL 的既有 Promise 而跳过 adapter */
describe('api client propagates the UI language to the backend', () => {
  it('defaults to zh-Hans, matching the backend default culture', async () => {
    setBoundLocale('zh-Hans');
    expect(getBoundLocale()).toBe('zh-Hans');
    await client.get('/api/locale-probe/1');
    expect(captured).toHaveLength(1);
    expect(captured[0].language).toBe('zh-Hans');
  });

  it('sends the switched UI language, not the operating-system language', async () => {
    setBoundLocale('en');
    await client.get('/api/locale-probe/2');
    expect(captured).toHaveLength(1);
    expect(captured[0].language).toBe('en');
  });

  it('covers POST as well as GET', async () => {
    setBoundLocale('en');
    await client.post('/api/locale-probe/3', { a: 1 });
    expect(captured).toHaveLength(1);
    expect(captured[0].language).toBe('en');
  });

  it('sets the header before the _skipDedup early return, so opted-out GETs carry it too', async () => {
    setBoundLocale('en');
    await client.get('/api/locale-probe/4', { _skipDedup: true } as AxiosRequestConfig);
    expect(captured).toHaveLength(1);
    expect(captured[0].language).toBe('en');
  });
});
