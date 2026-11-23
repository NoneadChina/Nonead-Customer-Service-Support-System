// 临时审计脚本（只读；用完删除）
import ts from 'typescript';
import { readFileSync, readdirSync, statSync, lstatSync } from 'node:fs';
import { join, resolve, dirname, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const ROOT = resolve(dirname(fileURLToPath(import.meta.url)), '.');
console.error('[debug] ROOT =', ROOT);
console.error('[debug] zhDir listing =', readdirSync(join(ROOT, 'src', 'locales', 'zh-Hans')).join(','));

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
            const nameNode = p.name;
            let key = null;
            if (ts.isStringLiteral(nameNode)) key = nameNode.text;
            else if (ts.isIdentifier(nameNode)) key = nameNode.text;
            else if (ts.isNumericLiteral(nameNode)) key = nameNode.text;
            if (key == null) continue;
            const init = p.initializer;
            let val;
            if (ts.isStringLiteral(init) || ts.isNoSubstitutionTemplateLiteral(init)) val = init.text;
            else if (ts.isObjectLiteralExpression(init)) {
              const o = {};
              for (const q of init.properties) {
                if (ts.isPropertyAssignment(q) && ts.isIdentifier(q.name)) {
                  o[q.name.text] = ts.isStringLiteral(q.initializer) || ts.isNoSubstitutionTemplateLiteral(q.initializer)
                    ? q.initializer.text
                    : q.initializer.getText(sf);
                }
              }
              val = o;
            } else val = init.getText(sf);
            const { line } = sf.getLineAndCharacterOfPosition(p.getStart(sf));
            out.set(key, { val, line: line + 1, file: relative(process.cwd(), file).split(sep).join('/') });
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
  const map = new Map();
  for (const f of readdirSync(dir)) {
    if (!f.endsWith('.ts') || f === 'index.ts') continue;
    for (const [k, v] of parseModule(join(dir, f))) {
      if (map.has(k)) map.set(k, { collision: true });
      else map.set(k, v);
    }
  }
  return map;
}

const zhDir = join(ROOT, 'src', 'locales', 'zh-Hans');
const enDir = join(ROOT, 'src', 'locales', 'en');
const zh = loadDir(zhDir);
const en = loadDir(enDir);

const ph = (s) => {
  const set = new Set();
  for (const m of String(s).matchAll(/\{(\w+)\}/g)) set.add(m[1]);
  return set;
};
const parts = (v) => (typeof v === 'string' ? [v] : [v.one ?? '', v.other ?? '']);

console.log(`zh keys=${zh.size}  en keys=${en.size}`);
const collZh = [...zh].filter(([, v]) => v.collision).map(([k]) => k);
const collEn = [...en].filter(([, v]) => v.collision).map(([k]) => k);
console.log(`collisions zh=${collZh.length} en=${collEn.length}`);

const onlyZh = [...zh.keys()].filter((k) => !en.has(k));
const onlyEn = [...en.keys()].filter((k) => !zh.has(k));
console.log(`\n[1] missing in en: ${onlyZh.length}`); onlyZh.slice(0, 50).forEach((k) => console.log('   ', k));
console.log(`[2] extra in en: ${onlyEn.length}`); onlyEn.slice(0, 50).forEach((k) => console.log('   ', k));

// 占位符不一致
const phBad = [];
for (const [k, zv] of zh) {
  const ev = en.get(k);
  if (!ev || ev.collision) continue;
  const zp = new Set(); const ep = new Set();
  parts(zv.val).forEach((s) => ph(s).forEach((x) => zp.add(x)));
  parts(ev.val).forEach((s) => ph(s).forEach((x) => ep.add(x)));
  const missEn = [...zp].filter((x) => !ep.has(x));
  const extraEn = [...ep].filter((x) => !zp.has(x));
  if (missEn.length || extraEn.length) phBad.push({ k, missEn, extraEn, z: zv.val, e: ev.val });
}
console.log(`\n[3] placeholder mismatch: ${phBad.length}`);
phBad.slice(0, 60).forEach((x) => console.log(`    ${x.k}  zh-missing-in-en=[${x.missEn}] en-extra=[${x.extraEn}]`));

// en 与 zh 完全相同（疑似未翻译）
const IDENT_OK = /^(NoneadCSS|SLA|API|ID|URL|JSON|CSV|PDF|Excel|Email|Email\b|OK|—|–|-|\/|\.|\d+|[A-Z0-9_]+|NoneadCSS[\s\S]*)$/;
const same = [];
for (const [k, zv] of zh) {
  const ev = en.get(k);
  if (!ev || ev.collision) continue;
  const zp = parts(zv.val); const ep = parts(ev.val);
  if (zp.length !== ep.length) { same.push({ k, kind: 'shape', z: zv.val, e: ev.val }); continue; }
  if (zp.every((s, i) => s === ep[i])) {
    const hasLetters = /[A-Za-z]/.test(zp.join(''));
    const hasHan = /\p{Script=Han}/u.test(zp.join(''));
    if (hasHan) same.push({ k, kind: 'zh-in-both', z: zv.val, e: ev.val });
    else if (hasLetters && !IDENT_OK.test(zp[0])) same.push({ k, kind: 'identical', z: zv.val, e: ev.val });
  }
}
console.log(`\n[4] identical/odd zh-en values: ${same.length}`);
const byKind = {};
same.forEach((s) => { (byKind[s.kind] ??= []).push(s); });
for (const [kind, list] of Object.entries(byKind)) {
  console.log(`  -- ${kind}: ${list.length}`);
  list.slice(0, 40).forEach((s) => console.log(`     ${s.k}  zh=${JSON.stringify(s.z).slice(0, 70)} en=${JSON.stringify(s.e).slice(0, 70)}`));
}

// 复数形态检查
const pluralOdd = [];
for (const [k, ev] of en) {
  if (typeof ev.val !== 'object' || ev.collision) continue;
  const zv = zh.get(k);
  if (typeof ev.val.one === 'string' && ev.val.one === ev.val.other) pluralOdd.push({ k, why: 'en one==other', one: ev.val.one });
  if (zv && typeof zv.val === 'object' && zv.val.one !== zv.val.other) pluralOdd.push({ k, why: 'zh one!=other', one: zv.val.one, other: zv.val.other });
}
console.log(`\n[5] plural anomalies: ${pluralOdd.length}`);
pluralOdd.slice(0, 40).forEach((x) => console.log(`    ${x.k} (${x.why}) ${JSON.stringify(x.one).slice(0, 60)}`));

// 首尾空白 / 换行不一致
const ws = [];
for (const [k, zv] of zh) {
  const ev = en.get(k);
  if (!ev || ev.collision) continue;
  const zp = parts(zv.val); const ep = parts(ev.val);
  if (zp.length !== ep.length) continue;
  for (let i = 0; i < zp.length; i++) {
    const a = zp[i].trim() !== zp[i], b = ep[i].trim() !== ep[i];
    if (a !== b) ws.push({ k, i, z: JSON.stringify(zp[i]).slice(0, 50), e: JSON.stringify(ep[i]).slice(0, 50) });
  }
}
console.log(`\n[6] whitespace-edge mismatch: ${ws.length}`);
ws.slice(0, 30).forEach((x) => console.log(`    ${x.k}[${x.i}] z=${x.z} e=${x.e}`));

// en 值中残留中文（复核测试）
const HAN = /\p{Script=Han}/u;
const CJKP = /[\u3000-\u303F\uFF00-\uFFEF]/;
const taint = [];
for (const [k, ev] of en) {
  if (ev.collision) continue;
  if (parts(ev.val).some((s) => HAN.test(s) || CJKP.test(s))) taint.push(k);
}
console.log(`\n[7] en values containing Han/fullwidth: ${taint.length}`);
taint.slice(0, 40).forEach((k) => console.log('    ', k, JSON.stringify(en.get(k).val).slice(0, 80)));
