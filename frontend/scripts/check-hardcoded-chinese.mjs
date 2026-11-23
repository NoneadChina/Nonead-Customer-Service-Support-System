/**
 * 门禁：源码中不得存在"用户可见的硬编码中文"。
 *
 * 依据：i18n 中英设计文档 §4.5 / §7（内部留存、不在发布包内）
 * 排除项（不算违规）：
 *   - 注释（AST 天然不含注释，无需特判）
 *   - console.* 调用的**整棵子树**（不只是直接实参）：判定沿祖先链进行，所以
 *     `console.log(arr.map((x) => '中文'))` 同样豁免——日志文案不翻译，**刻意不收紧**
 *   - 正则字面量（`/中文/`）：**刻意**不把 RegularExpressionLiteral 列为候选——正则源码不面向用户
 * 用 `\uXXXX` 转义写的中文**仍算违规**：字符串字面量除原始切片外还检查解码后的值
 * （`'\u4e2d\u6587'` 的原始切片里没有汉字，`node.text` 才有）。
 * 合法例外写在 EXEMPT_FILES / EXEMPT_PATTERNS 里并附原因。
 *
 * ## 显式豁免标记（批次 2 收官批新增，替代"每波各打一个补丁"）
 *
 * 有些中文常量**不是文案**：数据（邮箱名）、持久化 code（日期格式串）、语言自称（`简体中文`）、
 * 入站消息解析用的关键词表（`'[微笑]'`）、开发者诊断消息。它们**不随界面语言变化**，因此不该进字典；
 * 但**必须保持可读**——转义或字符码伪装是禁止的（见设计 §七 与修订记录第 50 行）。为了让这类常量
 * **显式**（而不是靠"基线里恰好有多少处"）留存，扫描器认行标记与块标记两种形态，
 * 每种都支持 `//` 与 `/* … *\/` 两种注释写法（JSX 里写 `{/* … *\/}`，整行形态）：
 *
 * ```
 *   const x = '简体中文'; // i18n-exempt: 语言自称，不随界面语言变化
 *   // i18n-exempt-block-start: 入站消息的表情码表，非用户可见文案
 *   const EMOJI = { '[微笑]': '😊' };
 *   // i18n-exempt-block-end
 *
 *   {/* i18n-exempt-block-start: 邮箱名（数据），不随界面语言变化 *\/}
 *   <option value="1">技术支持</option>
 *   {/* i18n-exempt-block-end *\/}
 * ```
 *
 * 规则（都有测试兜底，见 `scripts/__tests__/check-hardcoded-chinese.test.mjs`）：
 *   - **必须带非空理由**；缺理由的标记**一律不生效**（宁可门禁红，也不要静默豁免）；
 *   - 块标记**未闭合则整块不生效**（同样往"红"的方向失败）；
 *   - 标记必须是**注释形态**（`//` / `/*` 前是行首或空白）。写成字符串字面量
 *     `'// i18n-exempt: x'`、`'{/* i18n-exempt: x *\/}'` 都不算标记。
 *   - **只许用于"非文案中文"**；真文案用它就是在骗门禁。
 *
 * ⚠ **覆盖边界（刻意为之，不要据此扩大解释）**：本门禁只扫 `src/` 下的 `.ts`/`.tsx`。
 * `frontend/index.html`（外壳）、`.css`、`.json`、`.mjs` 均**不**在范围内——其中
 * `index.html` 的 `<title>`（双语静态标题）**不在**扫描范围内。收官批已把它变成"JS 就绪前的
 * 兜底"：I18nProvider 会按界面语言把 `document.title` 覆盖为 `common.shell.documentTitle`。
 * 本门禁仍不覆盖 `index.html`——那里只应保留双语兜底，不要往里写单语文案。
 *
 * 本模块同时被 gen-i18n-baseline.mjs 复用，因此 findHardcodedChinese / collectFiles /
 * isExempt / relativeToRoot / PROJECT_ROOT / diffAgainstBaseline / scanSrc 都必须导出
 * ——**不要**在别处复制一份。
 */
import { existsSync, lstatSync, readdirSync, readFileSync, realpathSync, statSync } from 'node:fs';
import { dirname, join, relative, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';
import ts from 'typescript';

/** 汉字（含扩展区） */
const HAN = /\p{Script=Han}/u;
/**
 * 中文标点与**全角形式**：只含标点/全角字符的串同样是"用户可见硬编码"。
 *
 * ⚠ **不要**把 `\u2014`（—）、`\u2026`（…）、`\u2018\u2019\u201C\u201D`（弯引号）加进来：
 * 它们在英文排版里完全合法（实测本仓库就有 `'Loading…'`、`'—'`（空值占位）、`OpenID: …`
 * 等 22 处），加进来会把合法英文误判成硬编码中文。
 * ⚠ 也**不要**拆成几个零散区间——那会漏掉全角字母数字（`Ｓｅａｒｃｈ`）。
 * 用整块 U+FF00–FFEF 最省事，实测在本仓库与窄区间结果完全相同（121 文件 / 9035 处）。
 */
const CJK_PUNCT = /[\u3000-\u303F\uFF00-\uFFEF]/;

/** 该文本是否含"用户可见中文"（汉字，或中文标点/全角形式） */
function hasChinese(text) {
  return HAN.test(text) || CJK_PUNCT.test(text);
}

/** 整文件豁免：只豁免中文真源目录。**不要写成 `/src[\\/]locales[\\/]/`**——
 *  那会把 `src/locales/en/` 一起豁免，于是"英文词条里残留中文"就没有任何机械门禁
 *  （Task 3 代码审查 F1 实测：改一条英文值为中文后 tsc/vitest/eslint 全绿）。 */
const EXEMPT_FILES = [/src[\\/]locales[\\/]zh-Hans[\\/]/];

/**
 * 测试文件豁免：断言样本里必然出现中文。
 * ⚠ `__tests__` 必须**锚定到路径段**：写成 `/__tests__[\\/]/` 时 `src/foo__tests__/x.ts`
 *   这种"只是名字里含 __tests__"的路径会被整文件豁免，业务代码由此可以绕过门禁。
 */
const EXEMPT_PATTERNS = [
  /\.test\.tsx?$/,
  /\.spec\.tsx?$/,
  /(^|[\\/])__tests__[\\/]/,
  /src[\\/]test[\\/]/,
];

/**
 * 项目根（frontend/）。**由本模块自身的路径推导，不用 `process.cwd()`**：
 * cwd 不同会得到不同的相对路径，CI 与本地就会对不上基线。
 *
 * ⚠ **不要写成 `fileURLToPath(new URL('..', import.meta.url))`**：
 * 本模块的自测要在 vitest（`environment: 'jsdom'`）里 import 它，而 jsdom 会把全局 `URL`
 * 换成"相对当前页面 origin 解析"的版本——`new URL('..', 'file:///…')` 实测得到
 * `http://localhost:3000/@fs/D:/…`，随后 `fileURLToPath` 抛
 * `TypeError: The URL must be of scheme file`，整个测试文件直接失败。
 * 用 `dirname` + `resolve` 这类纯路径运算没有这个问题。
 */
const MODULE_DIR = dirname(fileURLToPath(import.meta.url));
export const PROJECT_ROOT = resolve(MODULE_DIR, '..');

/** 统一成以 frontend/ 为根的 POSIX 风格相对路径（基线的键格式） */
export function relativeToRoot(absPath) {
  return relative(PROJECT_ROOT, absPath).split(sep).join('/');
}

export function isExempt(file) {
  return EXEMPT_FILES.some((re) => re.test(file)) || EXEMPT_PATTERNS.some((re) => re.test(file));
}

/**
 * 显式豁免标记的形态（行标记 / 块标记各支持 `//` 与 `/* … *\/` 两种注释写法）。
 *
 * - 行标记：`// i18n-exempt: 理由`、`/* i18n-exempt: 理由 *\/`、
 *   JSX 里写 `{/* i18n-exempt: 理由 *\/}`
 * - 块标记：`// i18n-exempt-block-start: 理由` … `// i18n-exempt-block-end`
 *   （JSX 里写 `{/* i18n-exempt-block-start: 理由 *\/}` … `{/* i18n-exempt-block-end *\/}`）
 *
 * ⚠ 顺序无关：`i18n-exempt:`（带冒号）**不会**匹配 `i18n-exempt-block-start:`，
 * 因为后者在 `i18n-exempt` 之后紧跟的是 `-` 而不是 `:`。
 * `(?:^|\s)` 保证标记必须是**注释形态**——`'// i18n-exempt: x'` 这类字符串字面量
 * （`//` 前是引号）不算标记，测试有覆盖。
 * `/*` 形态要求**本行闭合** `*\/`（半截块注释不生效），理由必须非空。
 */
const EXEMPT_LINE_RE = /(?:^|\s)\/\/\s*i18n-exempt:\s*\S/;
const EXEMPT_COMMENT_LINE_RE = /(?:^|\s)\/\*\s*i18n-exempt:\s*\S.*\*\/\s*$/;
const EXEMPT_BLOCK_START_RE =
  /(?:^|\s)(?:\/\/\s*i18n-exempt-block-start:\s*\S.*$|\/\*\s*i18n-exempt-block-start:\s*\S.*?\*\/\s*$)/;
const EXEMPT_BLOCK_END_RE =
  /(?:^|\s)(?:\/\/\s*i18n-exempt-block-end\s*$|\/\*\s*i18n-exempt-block-end\s*\*\/\s*$)/;

/**
 * JSX 注释行归一：把**行尾**的 `{/* … *\/}` 换成 `/* … *\/`（前面内容与缩进原样保留）。
 *
 * 目的是让**同一套**标记规则既可用在 TS 代码行尾，也可用在 JSX 里（JSX 的注释必须写成
 * `{/* … *\/}`）。三条边界：
 *   - `*\/}` 之后只允许空白 ⇒ 写成 `'{/* … *\/}'` 的字符串字面量不被归一（`{` 前是引号，
 *     也不在允许的前缀集里），仍不算标记；
 *   - `{` 之前允许 行首 / 空白 / `>` / `}`（JSX 子节点里紧跟在标签或表达式容器之后的写法）；
 *   - 归一后**强制**在 `/*` 前留一个空格，下游正则只需认 `(?:^|\s)` 一种前缀。
 */
function normalizeJsxCommentLine(line) {
  return line.replace(
    /(^|[\s>}])\{\/\*([\s\S]*?)\*\/\}(\s*)$/,
    (_, pre, body, trail) => `${pre} /*${body}*/${trail}`,
  );
}

/**
 * 计算本文件里被显式标记豁免的**行号集合**（1 基）。
 *
 * 三条 fail-safe 规则（都往"门禁红"的方向失败，绝不静默放行）：
 *   ① 标记缺非空理由 ⇒ 不生效；
 *   ② 块标记未闭合 ⇒ 该块整体不生效；
 *   ③ `-block-end` 没有配对的 start ⇒ 忽略。
 *
 * @param {string} content 文件内容
 * @returns {Set<number>} 1 基行号集合
 */
export function collectExemptLines(content) {
  const lines = content.split(/\r?\n/);
  /** @type {Set<number>} */
  const exempt = new Set();
  let blockStart = null;
  for (let i = 0; i < lines.length; i++) {
    const n = i + 1;
    const line = normalizeJsxCommentLine(lines[i]);
    if (EXEMPT_BLOCK_START_RE.test(line)) {
      blockStart = n;
      continue;
    }
    if (EXEMPT_BLOCK_END_RE.test(line)) {
      if (blockStart !== null) {
        for (let k = blockStart; k <= n; k++) exempt.add(k);
        blockStart = null;
      }
      continue;
    }
    if (EXEMPT_LINE_RE.test(line)) {
      exempt.add(n);
      continue;
    }
    // 单行写法：`/* i18n-exempt: 理由 *\/`（含 JSX 归一后的 `{/* … *\/}`）
    if (EXEMPT_COMMENT_LINE_RE.test(line)) exempt.add(n);
  }
  return exempt;
}

/** 该节点是否位于 console.* 调用内部（沿祖先链判断，即**整棵子树**都算） */
function insideConsoleCall(node) {
  for (let cur = node; cur; cur = cur.parent) {
    if (ts.isCallExpression(cur) && ts.isPropertyAccessExpression(cur.expression)) {
      const obj = cur.expression.expression;
      if (ts.isIdentifier(obj) && obj.text === 'console') return true;
    }
  }
  return false;
}

/** 命中文本单行化：`node.getText()` 会保留 CRLF，一处命中会打印成好几行 */
function oneLine(text) {
  return text.replace(/\s+/g, ' ').trim().slice(0, 40);
}

/**
 * 扫描单个文件内容。
 *
 * **返回结构化对象**（不要退化成拼好的字符串）：调用方要按 file 聚合做基线比对，
 * 从字符串里反解文件名会在"路径含空格"或"带行号"时出错，且无法可靠还原。
 * 记录**列号（1 基）**是因为设计 §七.1 的清单格式是 `文件:行:列`：存量命中里
 * 大量共享同一个 `文件:行`，缺列号就无法唯一指认。
 *
 * @param {string} content 文件内容
 * @param {string} fileName 用于诊断与选择解析模式（`*.tsx` → TSX，其余 → TS；建议传 relativeToRoot 的结果）
 * @returns {{ file: string, line: number, column: number, text: string }[]}
 */
export function findHardcodedChinese(content, fileName = 'inline.tsx') {
  // 解析模式随扩展名，**不能一律 TSX**：`.ts` 文件里的单参数泛型箭头
  // `const f = <T>(x: T) => x;` 会被当成 JSX，该文件后续节点的行列与取文全部错乱
  // （实测：第 2 行的中文被报成第 1 行，text 还是多语句拼接）。
  const kind = /\.tsx$/i.test(fileName) ? ts.ScriptKind.TSX : ts.ScriptKind.TS;
  const sf = ts.createSourceFile(fileName, content, ts.ScriptTarget.Latest, true, kind);
  /** @type {{ file: string, line: number, column: number, text: string }[]} */
  const hits = [];
  /** 被 `i18n-exempt` 标记豁免的行（1 基）；命中落在这些行上则不计 */
  const exemptLines = collectExemptLines(content);

  const visit = (node) => {
    const isCandidate =
      ts.isStringLiteral(node) ||
      ts.isNoSubstitutionTemplateLiteral(node) ||
      ts.isTemplateHead(node) ||
      ts.isTemplateMiddle(node) ||
      ts.isTemplateTail(node) ||
      node.kind === ts.SyntaxKind.JsxText;

    if (isCandidate) {
      const text = node.getText(sf);
      // 字符串字面量/无插值模板还要看**解码后**的值：`'\u4e2d\u6587'` 的原始切片里没有汉字
      const decoded =
        ts.isStringLiteral(node) || ts.isNoSubstitutionTemplateLiteral(node) ? node.text : '';
      if ((hasChinese(text) || hasChinese(decoded)) && !insideConsoleCall(node)) {
        const { line, character } = sf.getLineAndCharacterOfPosition(node.getStart(sf));
        if (!exemptLines.has(line + 1)) {
          hits.push({ file: fileName, line: line + 1, column: character + 1, text: oneLine(text) });
        }
      }
    }
    ts.forEachChild(node, visit);
  };

  visit(sf);
  return hits;
}

/**
 * 递归收集待扫描文件。
 *
 * **符号链接一律跳过**：junction/软链指回上级（或自身，Windows 上 `mklink /J` 就是这种条目）
 * 会让递归无限展开直至爆栈。
 */
export function collectFiles(dir) {
  const out = [];
  for (const name of readdirSync(dir)) {
    if (name === 'node_modules' || name === 'dist') continue;
    const p = join(dir, name);
    const st = lstatSync(p);
    if (st.isSymbolicLink()) continue;
    if (st.isDirectory()) out.push(...collectFiles(p));
    else if (/\.(ts|tsx)$/.test(name)) out.push(p);
  }
  return out;
}

/**
 * 基线比对：只允许"不比基线更差"（某文件当前命中数 > 基线值即为回归）。
 *
 * 基线是**棘轮**，读坏它等于关掉门禁，所以先校验再比对：必须是"路径 → 非负整数"的普通对象，
 * 且按 **own property** 取值（`baseline[file]` 会命中原型链，`constructor` 这类文件名的回归
 * 会被静默吞掉）。
 *
 * @param {{ file: string }[]} hits 本次扫描结果
 * @param {Record<string, number>} baseline 基线（键为 relativeToRoot 的路径）
 * @returns {{ file: string, allowed: number, current: number, added: number }[]}
 */
export function diffAgainstBaseline(hits, baseline) {
  if (baseline === null || typeof baseline !== 'object' || Array.isArray(baseline)) {
    throw new TypeError(
      `基线必须是"路径 → 命中数"的普通对象，实际收到 ${baseline === null ? 'null' : typeof baseline}`,
    );
  }
  for (const [file, allowed] of Object.entries(baseline)) {
    if (!Number.isInteger(allowed) || allowed < 0) {
      throw new TypeError(`基线条目 ${file} 的值必须是非负整数，实际收到 ${JSON.stringify(allowed)}`);
    }
  }

  const counts = new Map();
  for (const h of hits) counts.set(h.file, (counts.get(h.file) ?? 0) + 1);

  const regressions = [];
  for (const [file, current] of counts) {
    const allowed = Object.hasOwn(baseline, file) ? baseline[file] : 0;
    if (current > allowed) regressions.push({ file, allowed, current, added: current - allowed });
  }
  return regressions;
}

/** 全量扫描 src/ 下所有未豁免的 ts/tsx，返回结构化命中清单 */
export function scanSrc() {
  const srcDir = join(PROJECT_ROOT, 'src');
  if (!existsSync(srcDir) || !statSync(srcDir).isDirectory()) {
    const err = new Error(
      `前端源码目录不存在或不是目录：${srcDir}。` +
        'PROJECT_ROOT 由本模块自身路径推导（应为 frontend/），请确认在完整仓库中运行本脚本。',
    );
    err.code = 'SRC_MISSING';
    throw err;
  }
  return collectFiles(srcDir)
    .map((f) => relativeToRoot(f))
    .filter((rel) => !isExempt(rel))
    .flatMap((rel) => findHardcodedChinese(readFileSync(join(PROJECT_ROOT, rel), 'utf8'), rel));
}

/**
 * 本模块是否被**直接执行**（而非被 import）——用**真实路径**比较两侧。
 *
 * ⚠ **不要退回 `resolve(process.argv[1]) === fileURLToPath(import.meta.url)`**：
 * Node 会把 ESM 入口 realpath 化，而 `process.argv[1]` 保持调用者写的字面路径。
 * 于是经 junction/软链调用时（`mklink /J <link> frontend` 后 `node <link>/scripts/…`）
 * 判定为 false → **不扫描、零输出、exit 0**，门禁在最需要它的地方静默失效
 * （实测：junction 路径退出 0 且零行输出，真实路径退出 1 并列出全部违规）。
 * `argv[1]` 不存在（例如 `node -e`）时 realpathSync 抛错，按"非直接执行"处理。
 */
function isDirectRun() {
  if (!process.argv[1]) return false;
  try {
    return realpathSync(process.argv[1]) === realpathSync(fileURLToPath(import.meta.url));
  } catch {
    return false;
  }
}

// 直接执行时扫描 src/ 并决定退出码（被 import 时不执行）
if (isDirectRun()) {
  let violations;
  try {
    violations = scanSrc();
  } catch (err) {
    // src/ 缺失是预期错误，给一行可读提示；其余错误连栈一起打印，便于排查扫描器本身
    console.error(`✗ 扫描失败：${err instanceof Error ? err.message : String(err)}`);
    if (!(err instanceof Error && err.code === 'SRC_MISSING')) console.error(err);
    process.exit(1);
  }

  // 基线：只允许"不比基线更差"（某文件当前命中数 > 基线值即为回归）。
  // 键格式与基线 JSON 完全一致（都是 relativeToRoot 的结果，以 frontend/ 为根）。
  const baselinePath = join(PROJECT_ROOT, 'scripts', 'hardcoded-chinese-baseline.json');
  if (!existsSync(baselinePath)) {
    console.error('✗ 基线文件缺失：scripts/hardcoded-chinese-baseline.json');
    console.error('  生成：node scripts/gen-i18n-baseline.mjs（产物必须提交）');
    process.exit(1);
  }

  let baseline;
  try {
    baseline = JSON.parse(readFileSync(baselinePath, 'utf8'));
  } catch (err) {
    console.error(`✗ 基线文件无法解析：${err instanceof Error ? err.message : String(err)}`);
    process.exit(1);
  }

  let regressions;
  try {
    regressions = diffAgainstBaseline(violations, baseline);
  } catch (err) {
    // 基线形状不合法时**必须失败**：否则 current > allowed 永远为 false，门禁被静默关掉
    console.error(`✗ 基线校验失败：${err instanceof Error ? err.message : String(err)}`);
    process.exit(1);
  }

  if (regressions.length > 0) {
    console.error(`✗ 新增硬编码中文：${regressions.length} 个文件不得比基线更差`);
    for (const r of regressions) {
      console.error(`  ${r.file}: 新增 ${r.added} 处（基线 ${r.allowed}，当前 ${r.current}）`);
    }
    process.exit(1);
  }

  const baselineTotal = Object.values(baseline).reduce((a, b) => a + b, 0);
  console.log(`✓ 未超出基线（当前 ${violations.length} 处 / 基线 ${baselineTotal} 处）`);
}
