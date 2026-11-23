import { describe, expect, it } from 'vitest';

import { clampLineText, expectedLineChunkCount, splitLineText } from './line-chunking';

/**
 * 审计 #13：前端分段计数/截断必须与后端 LineClient.SplitText（LineClient.cs:822）同口径 ——
 * 按 UTF-16 长度（C# string.Length），不是 Unicode 码点数。
 * 期望值都是照后端算法逐行手算出来的（不切断代理对 + 换行只在段落"后半部"才生效）。
 */
describe('utils/line-chunking · 与 LineClient.SplitText 同口径（审计 #13）', () => {
  it('码点数会低估：含 emoji 的文本前端显示 1 段、后端实际发 2 段', () => {
    // '😀😀' = 4 个 UTF-16 单元 / 2 个码点；maxChars=3
    const text = '😀😀';
    expect([...text].length).toBe(2); // 旧的码点口径：以为不超过 3 ⇒ 1 段
    expect(text.length).toBe(4); // 后端口径：4 > 3 ⇒ 要分两段

    expect(expectedLineChunkCount(text, 3)).toBe(2);
    // 切点落在第二个 emoji 的高代理项上 ⇒ 退一个单元，代理对不被拆
    expect(splitLineText(text, 3)).toEqual(['😀', '😀']);
  });

  it('ZWJ 家庭 emoji：一个码点占 8 个 UTF-16 单元，分段数按单元数算', () => {
    const family = '👨‍👩‍👧'; // 👨 + ZWJ + 👩 + ZWJ + 👧
    expect([...family].length).toBe(5);
    expect(family.length).toBe(8);

    expect(expectedLineChunkCount(family, 3)).toBe(3);
    const pieces = splitLineText(family, 3);
    expect(pieces).toHaveLength(3);
    expect(pieces.join('')).toBe(family);
    pieces.forEach((piece) => expect(piece.length).toBeLessThanOrEqual(3));
  });

  it('换行只在段落后半部才生效，与后端 LastIndexOf 的搜索窗口一致', () => {
    // 换行在下半部（索引 6 > 0 + 10/2）⇒ 第一段切到换行之前
    const earlyBreak = 'abcdef\nghijklmnop';
    expect(splitLineText(earlyBreak, 10).map((p) => p.length)).toEqual([6, 10, 1]);
    expect(splitLineText(earlyBreak, 10).join('')).toBe(earlyBreak);

    // 换行正好在切点前半（索引 5 不 > 10/2）⇒ 硬切，不在换行处断；剩下的 7 个单元直接成第二段
    const hardCut = 'abcde\nfghijklmnop'; // 17 个 UTF-16 单元
    expect(splitLineText(hardCut, 10).map((p) => p.length)).toEqual([10, 7]);
    expect(splitLineText(hardCut, 10)[0]).toBe('abcde\nfghi');
  });

  it('不变量：分片重组等于原文，每片不超过 maxChars（纯 ASCII 与混合文本都成立）', () => {
    const samples = ['x'.repeat(23), '你好世界😀😀'.repeat(4), 'a\nb\n\nccc😀'.repeat(3)];
    for (const text of samples) {
      for (const maxChars of [2, 5, 9, 5000]) {
        const pieces = splitLineText(text, maxChars);
        expect(pieces.join('')).toBe(text);
        pieces.forEach((piece) => expect(piece.length).toBeLessThanOrEqual(Math.max(1, maxChars)));
        expect(pieces.length).toBe(expectedLineChunkCount(text, maxChars));
      }
    }
  });

  it('空文本与边界：空文本不分片，界面按 1 段显示；maxChars 非正数按 5000 兜底', () => {
    expect(splitLineText('', 5000)).toEqual([]);
    expect(expectedLineChunkCount('', 5000)).toBe(1);
    expect(splitLineText('y'.repeat(5001), 0)).toHaveLength(2); // 0 ⇒ 兜底 5000，同 SendTextAsync
  });

  it('输入截断不拆代理项（UTF-16 口径）', () => {
    // 切点 2 落在 '😀' 的高代理项上 ⇒ 退一个单元
    expect(clampLineText('a😀b', 2)).toBe('a');
    expect(clampLineText('a😀b', 3)).toBe('a😀');
    expect(clampLineText('a😀b', 4)).toBe('a😀b');
    expect(clampLineText('abc', 5)).toBe('abc');
    // 结果始终是合法 UTF-16（半个代理项会被 JSON/后端拒收）
    const clamped = clampLineText('😀😀😀', 5);
    expect(clamped.length).toBe(4);
    expect(clamped).toBe('😀😀');
  });
});
