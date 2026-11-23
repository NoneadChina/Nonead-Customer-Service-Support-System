import { describe, it, expect } from 'vitest';
import { getApiErrorMessage } from '../utils/error';

describe('getApiErrorMessage (utils/error)', () => {
  it('优先从 e.response.data.message 提取', () => {
    const err = { response: { data: { message: '业务错误：邮箱重复' } } };
    expect(getApiErrorMessage(err, '默认')).toBe('业务错误：邮箱重复');
  });

  it('response 存在但 data.message 缺失 → 走 Error fallback', () => {
    const err = { response: { status: 500 } };
    const jsErr = Object.assign(new Error('原生 JS 错误'), err);
    expect(getApiErrorMessage(jsErr, '默认')).toBe('原生 JS 错误');
  });

  it('普通 Error 实例 → 返回 e.message', () => {
    expect(getApiErrorMessage(new Error('网络断开'), 'fallback')).toBe('网络断开');
  });

  it('e 为 null / undefined / 字符串 → 返回 fallback', () => {
    expect(getApiErrorMessage(null, '默认信息')).toBe('默认信息');
    expect(getApiErrorMessage(undefined, '默认信息')).toBe('默认信息');
    expect(getApiErrorMessage('string error', '默认信息')).toBe('默认信息');
  });

  it('e 为对象但无 response 和 message → fallback', () => {
    expect(getApiErrorMessage({ code: 'ECONNRESET' }, '连接失败')).toBe('连接失败');
  });

  it('response.data.message 为空字符串 → 回退到 instanceof Error', () => {
    const jsErr = Object.assign(new Error('底层'), { response: { data: { message: '' } } });
    expect(getApiErrorMessage(jsErr, 'fallback')).toBe('底层');
  });
});
