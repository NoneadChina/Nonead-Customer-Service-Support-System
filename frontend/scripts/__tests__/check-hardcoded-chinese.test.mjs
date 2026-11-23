import { describe, expect, it } from 'vitest';
import {
  collectExemptLines,
  diffAgainstBaseline,
  findHardcodedChinese,
  isExempt,
} from '../check-hardcoded-chinese.mjs';

describe('i18n-exempt 显式豁免标记', () => {
  it('行标记带理由 ⇒ 豁免该行', () => {
    const src = "const x = '简体中文'; // i18n-exempt: 语言自称，不随界面语言变化\nconst y = '保存';\n";
    const hits = findHardcodedChinese(src, 'src/a.ts');
    expect(hits).toHaveLength(1);
    expect(hits[0]).toMatchObject({ line: 2, text: "'保存'" });
  });

  it('行标记**缺理由** ⇒ 不生效（宁可门禁红，也不要静默豁免）', () => {
    const src = "const x = '简体中文'; // i18n-exempt:\n";
    expect(findHardcodedChinese(src, 'src/a.ts')).toHaveLength(1);
    expect(findHardcodedChinese("const x = '简体中文'; // i18n-exempt\n", 'src/a.ts')).toHaveLength(1);
  });

  it('行标记只豁免**本行**，不波及相邻行', () => {
    const src = "const a = '甲'; // i18n-exempt: 理由\nconst b = '乙';\nconst c = '丙';\n";
    const hits = findHardcodedChinese(src, 'src/a.ts');
    expect(hits.map((h) => h.text)).toEqual(["'乙'", "'丙'"]);
  });

  it('块标记豁免首尾之间的所有行', () => {
    const src = [
      "const a = '甲';",
      '// i18n-exempt-block-start: 入站消息关键词表，非用户可见文案',
      "const b = '乙';",
      "const c = '丙';",
      '// i18n-exempt-block-end',
      "const d = '丁';",
    ].join('\n');
    const hits = findHardcodedChinese(src, 'src/a.ts');
    expect(hits.map((h) => h.text)).toEqual(["'甲'", "'丁'"]);
  });

  it('块标记**未闭合** ⇒ 整块不生效（往红的方向失败）', () => {
    const src = "const a = '甲';\n// i18n-exempt-block-start: 理由\nconst b = '乙';\n";
    expect(findHardcodedChinese(src, 'src/a.ts')).toHaveLength(2);
  });

  it('孤立的 block-end 不产生豁免', () => {
    const src = "const a = '甲';\n// i18n-exempt-block-end\nconst b = '乙';\n";
    expect(findHardcodedChinese(src, 'src/a.ts')).toHaveLength(2);
  });

  it('写在字符串字面量里的标记不算标记（`//` 前是引号）', () => {
    const src = "const a = '// i18n-exempt: 伪装'; const b = '乙';\n";
    expect(findHardcodedChinese(src, 'src/a.ts')).toHaveLength(2);
  });

  it('单行块注释形态 `/* i18n-exempt: 理由 */` 也可用', () => {
    const src = "const a = '甲'; /* i18n-exempt: 数据例外 */\n";
    expect(findHardcodedChinese(src, 'src/a.ts')).toEqual([]);
  });

  it('JSX 注释形态 `{/* i18n-exempt: 理由 */}`（写在行尾）也可用', () => {
    const src = 'const A = () => (\n  <div>甲</div>{/* i18n-exempt: 数据例外 */}\n);\n';
    expect(findHardcodedChinese(src, 'src/A.tsx')).toEqual([]);
  });

  it('JSX 块标记包裹 JSX 文本 ⇒ 块内命中豁免，块外不豁免', () => {
    const src = [
      "const A = () => (",
      '  <select>',
      '    {/* i18n-exempt-block-start: 邮箱名（数据），不随界面语言变化 */}',
      '    <option value="1">技术支持</option>',
      '    <option value="2">销售咨询</option>',
      '    {/* i18n-exempt-block-end */}',
      '    <option value="3">其它（真文案，必须进字典）</option>',
      '  </select>',
      ');',
    ].join('\n');
    const hits = findHardcodedChinese(src, 'src/A.tsx');
    expect(hits.map((h) => h.text)).toEqual(['其它（真文案，必须进字典）']);
  });

  it('JSX 块标记未闭合 ⇒ 整块不生效', () => {
    const src = ['const A = () => (', '  <div>', '    {/* i18n-exempt-block-start: 理由 */}', '    甲', '  </div>', ');'].join('\n');
    expect(findHardcodedChinese(src, 'src/A.tsx')).toHaveLength(1);
  });

  it('JSX 形态缺理由 ⇒ 不生效', () => {
    const src = "const A = () => (\n  <div>{/* i18n-exempt: */}甲</div>\n);\n";
    expect(findHardcodedChinese(src, 'src/A.tsx')).toHaveLength(1);
  });

  it('写字面量里的 `{/* i18n-exempt: x */}` 不算标记（`{` 前是引号）', () => {
    const src = "const a = '{/* i18n-exempt: 伪装 */}';\nconst b = '乙';\n";
    expect(findHardcodedChinese(src, 'src/a.ts')).toHaveLength(2);
  });

  it('`{/* … */}` 之后还有 JSX 内容 ⇒ 不是标记（只认行尾形态）', () => {
    const src = 'const A = () => (\n  <div>{/* i18n-exempt: 伪装 */}甲</div>\n);\n';
    expect(findHardcodedChinese(src, 'src/A.tsx')).toHaveLength(1);
  });

  it('半截块注释 `/* i18n-exempt: 理由`（本行未闭合）不生效', () => {
    const src = "const a = '甲'; /* i18n-exempt: 理由\n";
    expect(findHardcodedChinese(src, 'src/a.ts')).toHaveLength(1);
  });

  it('collectExemptLines 只返回带理由的标记行', () => {
    const src = ['// i18n-exempt: 理由一', '// i18n-exempt:', '// i18n-exempt-block-start: 理由二', 'x', '// i18n-exempt-block-end'].join('\n');
    expect([...collectExemptLines(src)].sort((a, b) => a - b)).toEqual([1, 3, 4, 5]);
  });
});

describe('findHardcodedChinese', () => {
  it('检出 JSX 文本里的中文，并给出文件名/行号/列号/原文', () => {
    const hits = findHardcodedChinese('const A = () => (\n  <div>保存</div>\n);', 'src/A.tsx');
    expect(hits).toHaveLength(1);
    expect(hits[0]).toEqual({ file: 'src/A.tsx', line: 2, column: 8, text: '保存' });
  });

  it('检出字符串字面量与 JSX 属性里的中文', () => {
    expect(findHardcodedChinese("const x = '保存失败';", 'src/a.ts')).toHaveLength(1);
    expect(findHardcodedChinese('const A = () => <input placeholder="请输入姓名" />;', 'src/A.tsx')).toHaveLength(1);
  });

  it('检出模板字面量（带插值时 TS 会切成 Head/Tail 两段，各命中一次）', () => {
    expect(findHardcodedChinese('const x = `保存`;', 'src/a.ts')).toHaveLength(1);
    expect(findHardcodedChinese('const x = `共 ${n} 条`;', 'src/a.ts')).toHaveLength(2);
  });

  it('检出只含中文标点的字符串（全角标点同样用户可见）', () => {
    expect(findHardcodedChinese("const x = '，。';", 'src/a.ts')).toHaveLength(1);
  });

  it('检出全角字母数字（中文输入法下的常见手滑）', () => {
    // R2 的同类问题：只写 \uFF01-\uFF0F 这类零散区间会漏掉全角字母数字
    expect(findHardcodedChinese("const x = 'Ｓｅａｒｃｈ';", 'src/a.ts')).toHaveLength(1);
  });

  it('检出写成 \\uXXXX 转义的中文（只看原始切片会漏）', () => {
    expect(findHardcodedChinese("const x = '\\u4e2d\\u6587';", 'src/a.ts')).toHaveLength(1);
    expect(findHardcodedChinese('const x = `\\u4e2d\\u6587`;', 'src/a.ts')).toHaveLength(1);
  });

  it('不检出英文排版里合法的 em dash / 省略号 / 弯引号', () => {
    // 这几个字符在中英里都合法，若算作"中文标点"会大面积误报（本仓库有 22 处）
    expect(findHardcodedChinese("const x = 'Loading…';", 'src/a.ts')).toEqual([]);
    expect(findHardcodedChinese("const x = '—';", 'src/a.ts')).toEqual([]);
    expect(findHardcodedChinese("const x = 'it’s a “quote”';", 'src/a.ts')).toEqual([]);
  });

  it('同一行多处命中各自计数', () => {
    expect(findHardcodedChinese("const x = ['保存', '删除'];", 'src/a.ts')).toHaveLength(2);
  });

  it('不检出注释里的中文（含 JSX 注释）', () => {
    expect(findHardcodedChinese('// 这是注释\nconst x = 1;', 'src/a.ts')).toEqual([]);
    expect(findHardcodedChinese('/* 块注释 */\nconst x = 1;', 'src/a.ts')).toEqual([]);
    expect(findHardcodedChinese('const A = () => <div>{/* 中文注释 */}</div>;', 'src/A.tsx')).toEqual([]);
  });

  it('不检出 console.* 参数里的中文（含模板）', () => {
    expect(findHardcodedChinese("console.error('加载失败');", 'src/a.ts')).toEqual([]);
    expect(findHardcodedChinese('console.warn(`第 ${i} 次`);', 'src/a.ts')).toEqual([]);
  });

  it('console.* 豁免的是**整棵子树**，不是只有直接实参', () => {
    expect(findHardcodedChinese("console.log(arr.map((x) => '中文'));", 'src/a.ts')).toEqual([]);
    expect(findHardcodedChinese("console.log(fn({ tip: '中文' }));", 'src/a.ts')).toEqual([]);
  });

  it('只是被 console 打印的变量仍算违规（豁免的是调用点，不是赋值）', () => {
    const hits = findHardcodedChinese("const msg = '中文';\nconsole.log(msg);", 'src/a.ts');
    expect(hits).toHaveLength(1);
    expect(hits[0]).toMatchObject({ line: 1, column: 13 });
  });

  it('按扩展名选解析模式：.ts 里的单参数泛型箭头不会污染后续行列', () => {
    // 一律按 TSX 解析时，`<T>(x: T) => x` 被当成 JSX，第 2 行的命中会被报成第 1 行
    const src = "const f = <T>(x: T) => x;\nconst msg = '保存失败';\n";
    const hits = findHardcodedChinese(src, 'src/a.ts');
    expect(hits).toHaveLength(1);
    expect(hits[0]).toMatchObject({ line: 2, column: 13, text: "'保存失败'" });
  });

  it('不检出纯 ASCII 代码', () => {
    expect(findHardcodedChinese("const x = 'save';", 'src/a.ts')).toEqual([]);
  });
});

describe('diffAgainstBaseline', () => {
  const hits = (file, n) => Array.from({ length: n }, (_, i) => ({ file, line: i + 1, text: '中' }));

  it('等于基线不算回归', () => {
    expect(diffAgainstBaseline(hits('src/a.ts', 2), { 'src/a.ts': 2 })).toEqual([]);
  });

  it('少于基线不算回归（允许分批清理）', () => {
    expect(diffAgainstBaseline(hits('src/a.ts', 1), { 'src/a.ts': 2 })).toEqual([]);
  });

  it('多于基线算回归并给出差值', () => {
    expect(diffAgainstBaseline(hits('src/a.ts', 3), { 'src/a.ts': 1 })).toEqual([
      { file: 'src/a.ts', allowed: 1, current: 3, added: 2 },
    ]);
  });

  it('基线里没有的文件出现中文算回归', () => {
    expect(diffAgainstBaseline(hits('src/new.tsx', 1), {})).toEqual([
      { file: 'src/new.tsx', allowed: 0, current: 1, added: 1 },
    ]);
  });

  it('基线里有、当前已清空的文件不算回归', () => {
    expect(diffAgainstBaseline([], { 'src/a.ts': 5 })).toEqual([]);
  });

  it('基线不是"路径 → 非负整数"的普通对象时大声失败（而不是静默关掉棘轮）', () => {
    expect(() => diffAgainstBaseline(hits('src/a.ts', 1), null)).toThrow(TypeError);
    expect(() => diffAgainstBaseline(hits('src/a.ts', 1), ['src/a.ts'])).toThrow(TypeError);
    expect(() => diffAgainstBaseline(hits('src/a.ts', 3), { 'src/a.ts': 'n/a' })).toThrow(/非负整数/);
    expect(() => diffAgainstBaseline(hits('src/a.ts', 1), { 'src/a.ts': -1 })).toThrow(/非负整数/);
  });

  it('基线走 own-property 查找：原型上的同名键不算既有豁免', () => {
    expect(diffAgainstBaseline(hits('toString', 1), {})).toEqual([
      { file: 'toString', allowed: 0, current: 1, added: 1 },
    ]);
  });
});

describe('isExempt', () => {
  it('只豁免中文真源目录与测试文件', () => {
    expect(isExempt('src/locales/zh-Hans/nav.ts')).toBe(true);
    expect(isExempt('src/pages/Foo.test.tsx')).toBe(true);
    expect(isExempt('src/components/__tests__/x.ts')).toBe(true);
  });

  it('`.spec` 与 `src/test/` 同样豁免', () => {
    expect(isExempt('src/pages/Foo.spec.tsx')).toBe(true);
    expect(isExempt('src/pages/Foo.spec.ts')).toBe(true);
    expect(isExempt('src/test/setup.ts')).toBe(true);
  });

  it('`__tests__` 必须整段匹配：路径里只是含这三个词的**不**豁免', () => {
    expect(isExempt('src/foo__tests__/x.ts')).toBe(false);
    expect(isExempt('src/components/__tests__x/y.ts')).toBe(false);
  });

  it('英文词条目录**不**豁免（英文里残留中文必须被扫描器抓到）', () => {
    expect(isExempt('src/locales/en/nav.ts')).toBe(false);
    expect(isExempt('src/locales/en/common.ts')).toBe(false);
  });

  it('业务代码不豁免', () => {
    expect(isExempt('src/components/Layout.tsx')).toBe(false);
  });
});
