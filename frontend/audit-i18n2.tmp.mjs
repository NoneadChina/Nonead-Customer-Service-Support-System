// 临时审计脚本 2（只读；用完删除）：t() 调用参数完整性 + 反向英文硬编码
import ts from 'typescript';
import { readFileSync, readdirSync, lstatSync } from 'node:fs';
import { join, resolve, dirname, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '.');
const SRC = join(ROOT, 'src');

function unwrap(node) {
  let n = node;
  while (n && (ts.isSatisfiesExpression?.(n) || ts.isAsExpression?.(n) || ts.isParenthesizedExpression?.(n))) n = n.expression;
  return n;
}

function parseModule(file) {
  const src = readFileSync(file, 'utf8');
  const sf = ts.createSourceFile(file, src, ts.ScriptTarget.Latest, true, ts.ScriptKind.TS);
  const out = new Map();
  const visit = (node) => {
    if (ts.isVariableStatement(node)) {
      for (const d of node.declarationList.declarations) {
        const init0 = d.initializer ? unwrap(d.initializer) : null;
        if (ts.isIdentifier(d.name) && init0 && ts.isObjectLiteralExpression(init0)) {
          for (const p of init0.properties) {
            if (!ts.isPropertyAssignment(p)) continue;
            const nn = p.name;
            let key = ts.isStringLiteral(nn) || ts.isIdentifier(nn) ? nn.text : null;
            if (key == null) continue;
            const init = p.initializer;
            let val;
            if (ts.isStringLiteral(init) || ts.isNoSubstitutionTemplateLiteral(init)) val = init.text;
            else if (ts.isObjectLiteralExpression(init)) {
              val = {};
              for (const q of init.properties) {
                if (ts.isPropertyAssignment(q) && ts.isIdentifier(q.name)) {
                  val[q.name.text] = ts.isStringLiteral(q.initializer) || ts.isNoSubstitutionTemplateLiteral(q.initializer) ? q.initializer.text : q.initializer.getText(sf);
                }
              }
            } else val = init.getText(sf);
            if (!out.has(key)) out.set(key, val);
          }
        }
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(sf);
  return out;
}

function loadDir(dir) {
  const m = new Map();
  for (const f of readdirSync(dir)) {
    if (!f.endsWith('.ts') || f === 'index.ts') continue;
    for (const [k, v] of parseModule(join(dir, f))) if (!m.has(k)) m.set(k, v);
  }
  return m;
}

const zh = loadDir(join(SRC, 'locales', 'zh-Hans'));
const en = loadDir(join(SRC, 'locales', 'en'));
const partsOf = (v) => (typeof v === 'string' ? [v] : [v.one ?? '', v.other ?? '']);
const phOf = (v) => {
  const s = new Set();
  for (const p of partsOf(v)) for (const m of String(p).matchAll(/\{(\w+)\}/g)) s.add(m[1]);
  return s;
};

function collectFiles(dir, out = []) {
  for (const name of readdirSync(dir)) {
    if (name === 'node_modules' || name === 'dist') continue;
    const p = join(dir, name);
    const st = lstatSync(p);
    if (st.isSymbolicLink()) continue;
    if (st.isDirectory()) collectFiles(p, out);
    else if (/\.(ts|tsx)$/.test(name)) out.push(p);
  }
  return out;
}

const files = collectFiles(SRC).filter((f) => !/\.test\.tsx?$/.test(f) && !/[\\/]__tests__[\\/]/.test(f) && !/[\\/]test[\\/]/.test(f));
console.log(`scanned source files: ${files.length}`);

// ---- A. t() 调用点参数完整性 ----
const missingParams = [];
const unknownKeys = [];
const usedKeys = new Set();
let tCalls = 0;
let dynamicKeys = 0;

for (const f of files) {
  const text = readFileSync(f, 'utf8');
  const sf = ts.createSourceFile(f, text, ts.ScriptTarget.Latest, true, f.endsWith('.tsx') ? ts.ScriptKind.TSX : ts.ScriptKind.TS);
  const rel = relative(ROOT, f).split(sep).join('/');
  const visit = (node) => {
    if (ts.isCallExpression(node) && ts.isIdentifier(node.expression) && node.expression.text === 't') {
      tCalls++;
      const arg0 = node.arguments[0];
      const { line } = sf.getLineAndCharacterOfPosition(node.getStart(sf));
      if (arg0 && (ts.isStringLiteral(arg0) || ts.isNoSubstitutionTemplateLiteral(arg0))) {
        const key = arg0.text;
        usedKeys.add(key);
        const entry = zh.get(key) ?? en.get(key);
        if (!entry) { unknownKeys.push({ rel, line: line + 1, key }); }
        else {
          const need = phOf(entry);
          if (need.size) {
            const arg1 = node.arguments[1];
            let provided = new Set();
            if (arg1 && ts.isObjectLiteralExpression(arg1)) {
              for (const q of arg1.properties) {
                if (ts.isPropertyAssignment(q) && ts.isIdentifier(q.name)) provided.add(q.name.text);
                else if (ts.isShorthandPropertyAssignment(q)) provided.add(q.name.text);
                else provided.add('<?spread/dynamic>');
              }
            }
            const miss = [...need].filter((x) => !provided.has(x));
            if (miss.length) missingParams.push({ rel, line: line + 1, key, miss, provided: [...provided] });
          }
        }
      } else dynamicKeys++;
    }
    ts.forEachChild(node, visit);
  };
  visit(sf);
}
console.log(`\n[A] t() 调用点 ${tCalls} 处；动态 key ${dynamicKeys} 处；未定义 key ${unknownKeys.length}`);
unknownKeys.slice(0, 30).forEach((x) => console.log(`    未定义 ${x.rel}:${x.line} -> ${x.key}`));
console.log(`[A] 缺少必填插值参数：${missingParams.length}`);
missingParams.slice(0, 60).forEach((x) => console.log(`    ${x.rel}:${x.line} ${x.key} 缺 [${x.miss}] 实际 [${x.provided}]`));

// ---- B. 未被引用的词条（可能未使用 key） ----
const unused = [...zh.keys()].filter((k) => !usedKeys.has(k));
console.log(`\n[B] zh 词条 ${zh.size} 条，未被静态 t('literal') 引用：${unused.length}`);
const byPrefix = {};
for (const k of unused) byPrefix[k.split('.')[0]] = (byPrefix[k.split('.')[0]] ?? 0) + 1;
console.log('    按模块前缀：', JSON.stringify(byPrefix));
unused.slice(0, 25).forEach((k) => console.log('     ', k));

// ---- C. 反向检查：组件中硬编码英文界面文案 ----
const UI_ATTRS = new Set(['placeholder', 'title', 'aria-label', 'alt', 'label', 'aria-description', 'data-tooltip']);
const engHits = [];
const WORDY = /[A-Za-z]{3,}/;
const escapeOk = /^[\x20-\x7E]*$/;
for (const f of files) {
  if (!f.endsWith('.tsx')) continue;
  const text = readFileSync(f, 'utf8');
  const sf = ts.createSourceFile(f, text, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const rel = relative(ROOT, f).split(sep).join('/');
  const push = (node, why, s) => {
    // 祖先里有 console.* 则豁免
    for (let c = node; c; c = c.parent) {
      if (ts.isCallExpression(c) && ts.isPropertyAccessExpression(c.expression) && ts.isIdentifier(c.expression.expression) && c.expression.expression.text === 'console') return;
    }
    const { line } = sf.getLineAndCharacterOfPosition(node.getStart(sf));
    engHits.push({ rel, line: line + 1, why, text: s.replace(/\s+/g, ' ').trim().slice(0, 60) });
  };
  const visit = (node) => {
    if (node.kind === ts.SyntaxKind.JsxText) {
      const s = node.getText(sf);
      if (WORDY.test(s) && escapeOk.test(s) && !/https?:|www\.|@/.test(s)) push(node, 'JsxText', s);
    }
    if (ts.isJsxAttribute(node) && node.name && ts.isIdentifier(node.name) && UI_ATTRS.has(node.name.text) && node.initializer && ts.isStringLiteral(node.initializer)) {
      const s = node.initializer.text;
      if (WORDY.test(s) && !/\{/.test(s)) push(node, `attr:${node.name.text}`, s);
    }
    if (ts.isStringLiteral(node) && node.parent && ts.isJsxExpression(node.parent)) {
      const s = node.text;
      const inJsxChild = ts.isJsxExpression(node.parent) && node.parent.parent && (ts.isJsxElement(node.parent.parent) || ts.isJsxFragment(node.parent.parent));
      if (inJsxChild && WORDY.test(s) && escapeOk.test(s) && !/^[a-z-]+$/.test(s) && !/^(GET|POST|PUT|DELETE|PATCH|json|application|text|button|submit|email|password|text|number|date|checkbox|radio|file|tel|url|search|none|true|false|null)$/i.test(s)) {
        push(node, 'jsx-expr-string', s);
      }
    }
    ts.forEachChild(node, visit);
  };
  visit(sf);
}
console.log(`\n[C] 疑似硬编码英文界面文案：${engHits.length}`);
const byFile = {};
for (const h of engHits) (byFile[h.rel] ??= []).push(h);
for (const [rel, list] of Object.entries(byFile).sort((a, b) => b[1].length - a[1].length)) {
  console.log(`  ${rel} (${list.length})`);
  list.slice(0, 30).forEach((h) => console.log(`     ${h.line} [${h.why}] ${h.text}`));
}
