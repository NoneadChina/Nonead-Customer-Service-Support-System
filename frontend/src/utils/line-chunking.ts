/**
 * LINE 出站文本的分段口径（审计 #13）。
 *
 * 这里是后端 `NoneadCSS.Infrastructure/Services/Line/LineClient.cs` 里
 * `internal static List<string> SplitText(string text, int maxChars)`（LineClient.cs:822）的逐行等价移植：
 * 后端按 **UTF-16 长度**（即 C# 的 `string.Length`）切段，只在「切点前一个单元是高位代理项」时回退一个单元，
 * 并优先在段落**后半部**的换行处断开。
 *
 * 为什么必须照抄而不是用 `[...value].length`：
 * 一个 emoji（如 😀 / 👨‍👩‍👧）是 2 个甚至更多 UTF-16 单元，但只算 1 个码点。
 * 旧前端用码点数计数、又用 UTF-16 `slice` 截断，两套口径混用 ⇒ 含 emoji 的长文本前端显示 1 段、
 * 后端实际发 2 段（分段提示与「单次请求最多消息数」告警都跟着错）。
 * 计数与截断统一按后端的 UTF-16 口径，前端显示才与真实投递一致。
 */

/** C# `char.IsHighSurrogate` 的等价判断（UTF-16 高位代理项 D800–DBFF） */
function isHighSurrogate(codeUnit: number): boolean {
  return codeUnit >= 0xd800 && codeUnit <= 0xdbff;
}

/**
 * 与 `LineClient.SplitText` 同口径的分片：maxChars 为 0/负数时取 5000
 * （对应后端 SendTextAsync 里 `settings.TextChunkChars > 0 ? settings.TextChunkChars : 5000` 的兜底）。
 * 空字符串返回空数组 —— 与后端一致（后端对空文本不会发出任何 text 消息）。
 */
export function splitLineText(text: string, maxChars: number): string[] {
  const pieces: string[] = [];
  if (!text) {
    return pieces;
  }

  const limit = maxChars > 0 ? maxChars : 5000;
  let index = 0;
  while (index < text.length) {
    const remaining = text.length - index;
    if (remaining <= limit) {
      pieces.push(text.slice(index));
      break;
    }

    let take = limit;
    // 不切断代理对：切点落在高代理项之后 ⇒ 退一个单元（同 LineClient.cs:844）
    if (isHighSurrogate(text.charCodeAt(index + take - 1))) {
      take -= 1;
    }

    // 优先在最近的换行处断开（同 LineClient.cs:850 的 LastIndexOf('\n', index + take - 1, take)，
    // 其搜索窗口就是 [index, index + take - 1]，所以 JS 侧还要排除 index 之前的命中）
    const found = text.lastIndexOf('\n', index + take - 1);
    const lastNewLine = found >= index ? found : -1;
    if (lastNewLine > index + take / 2) {
      take = lastNewLine - index;
    }

    if (take <= 0) {
      take = 1;
    }

    pieces.push(text.slice(index, index + take));
    index += take;
  }

  return pieces;
}

/** 预期分段数（用于界面提示）；空文本按 1 段显示，避免输入框里出现「0 段」 */
export function expectedLineChunkCount(text: string, maxChars: number): number {
  return Math.max(1, splitLineText(text, maxChars).length);
}

/**
 * 输入框的截断口径（审计 #13）：按 UTF-16 长度截断，且不把代理对切一半。
 * 与 `splitLineText(text, maxChars)[0]` 的区别是这里**不**应用换行优先断行 ——
 * 换行只影响后端如何分段，不影响「用户最多能输入多少单元」。
 */
export function clampLineText(text: string, maxChars: number): string {
  const limit = maxChars > 0 ? maxChars : 5000;
  if (text.length <= limit) {
    return text;
  }

  let cut = limit;
  // 与后端同一处理：切点前一个单元是高代理项 ⇒ 退一个单元，避免留下半个 emoji
  if (isHighSurrogate(text.charCodeAt(cut - 1))) {
    cut -= 1;
  }

  return text.slice(0, cut);
}
