// 后端 API 返回文案双语词典的生成/校验工具（方案：出口词典替换，见 docs/development.md §9.7）
//
//   node scripts/i18n/api-message-dictionary.mjs extract   [--work <dir>] [--batch <n>]
//       扫描源码里「会进响应体的中文文案」，归一化成词典键，写出 rows/batch 文件供翻译。
//   node scripts/i18n/api-message-dictionary.mjs coverage  [--work <dir>]
//       对照已发布词典，列出源码里存在、词典里没有的键（新增中文文案后必查此项）。
//   node scripts/i18n/api-message-dictionary.mjs merge     [--work <dir>]
//       把 <work>/msg-en-NN.json（id → English）合并进发布词典，并跑全部判据。
//
// 词典键的归一化必须与 NoneadCSS.Api/Localization/ApiMessageLocalizer.cs 里的运行期算法一致：
// 字面量里的 {hole}、两边的数字串与引号串都换成 U+E000 哨兵，英文值按出现顺序用 {0}、{1}… 回填。
// 改一边就必须改另一边，否则词典整体失配（失配的后果是保持中文原样输出，不会误翻）。
import fs from 'node:fs';
import path from 'node:path';
import process from 'node:process';

const ROOT = path.resolve(import.meta.dirname, '../..');
const DICT = path.join(ROOT, 'NoneadCSS.Api/Localization/api-message-en.json');
const CJK = /[\u3400-\u9fff\uf900-\ufaff]/;
const CJK_OR_FULLWIDTH = /[\u3400-\u9fff\u3000-\u303f\uff00-\uffef]/;
const GUIILLEMET = /[\u00ab\u00bb]/;
const SKIP = new Set(['bin', 'obj', 'node_modules', '.git', 'dist', 'data', '.dsh-memory', '.dsh-project-memory', '.vs', 'TestResults', 'artifacts']);
const SEP = '\uE000';
const MARK = (i) => '\u00ab' + i + '\u00bb';

function arg(name, fallback) {
  const i = process.argv.indexOf('--' + name);
  return i > -1 && process.argv[i + 1] ? process.argv[i + 1] : fallback;
}

const WORK = path.resolve(arg('work', path.join(ROOT, 'artifacts/i18n-msg')));
const BATCH = Number(arg('batch', 110));

// ---------- 源码扫描 ----------

function sourceFiles() {
  const out = [];
  (function walk(dir) {
    for (const entry of fs.readdirSync(dir, { withFileTypes: true })) {
      const p = path.join(dir, entry.name);
      if (entry.isDirectory()) { if (!SKIP.has(entry.name)) walk(p); }
      else if (entry.name.endsWith('.cs')) out.push(p);
    }
  })(ROOT);
  return out.filter((f) => {
    const rel = path.relative(ROOT, f).replace(/\\/g, '/');
    if (rel.startsWith('NoneadCSS.Tests/') || rel.includes('NoneadCSS.Api/Localization/')) return false;
    if (rel.includes('/Migrations/') || rel.includes('/Seeders/') || rel === 'NoneadCSS.Api/DatabaseSeeder.cs') return false;
    return true;
  });
}

// C# 字符串字面量（含转义）：" ... \" ... "
const STR = '"(?:\\\\.|[^"\\\\])*"';
const PATTERNS = [
  { tag: 'ErrorMessage', re: new RegExp(`ErrorMessage\\s*=\\s*(\\$?${STR})`, 'g') },
  { tag: 'Result.Fail', re: new RegExp(`Result\\.(?:Fail|Error)\\s*(?:<[^>]*>)?\\(\\s*(\\$?${STR})`, 'g') },
  { tag: 'ObjectMessage', re: new RegExp(`\\bmessage\\s*[=:]\\s*(\\$?${STR})`, 'g') },
  // 状态辅助方法直出文案：BadRequest/NotFound/Conflict/Unauthorized/Forbid/Problem/UnprocessableEntity
  // （漏掉 Unauthorized/Forbid/Problem 会让这类文案在英文界面保持中文——词典失配只会保持原文，不会误翻）
  { tag: 'BadRequestObj', re: new RegExp(`(?:BadRequest|NotFound|Conflict|Unauthorized|Forbid|Problem|UnprocessableEntity)\\s*\\(\\s*(?:[^,;)]*,\\s*)?(?:new\\s*\\{\\s*)?(\\$?${STR})`, 'g') },
  { tag: 'Throw', re: new RegExp(`throw new \\w*Exception\\(\\s*(\\$?${STR})`, 'g') },
  { tag: 'AddError', re: new RegExp(`AddError\\s*\\(\\s*[^,]+,\\s*(\\$?${STR})`, 'g') },
  // 校验错误集合：errors/messages/warnings.Add("中文")。这类文案会经 Result.Errors 进响应体，
  // 早先漏这一族导致 `coverage` 报「全部有词典条目」却仍有 50 条漏出（PasswordPolicyService.ValidateAsync 首当其冲）。
  { tag: 'ListAdd', re: new RegExp(`\\b\\w*(?:errors|messages|warnings)\\.Add\\(\\s*(\\$?${STR})`, 'gi') },
];

function unescapeCs(s) {
  return s.replace(/\\"/g, '"').replace(/\\n/g, ' ').replace(/\\r/g, '').replace(/\\t/g, ' ').replace(/\\\\/g, '\\').replace(/\{\{/g, '{').replace(/\}\}/g, '}');
}

function normalize(text, isLiteral) {
  let s = text;
  if (isLiteral) s = s.replace(/\{[^{}]*\}/g, SEP);
  s = s.replace(/\d+(?:[.,]\d+)*/g, SEP);
  s = s.replace(/"[^"]*"|'[^']*'/g, SEP);
  const count = (s.match(new RegExp(SEP, 'g')) || []).length;
  return { key: s, count };
}

function scan() {
  const rows = [];
  const seen = new Map();
  let braceSkipped = 0;
  for (const f of sourceFiles()) {
    const rel = path.relative(ROOT, f).replace(/\\/g, '/');
    fs.readFileSync(f, 'utf8').split(/\r?\n/).forEach((line, i) => {
      const t = line.trim();
      if (t.startsWith('//') || t.startsWith('///') || t.startsWith('*')) return;
      if (/Log(Information|Warning|Error|Debug|Trace)/.test(t)) return;
      for (const { tag, re } of PATTERNS) {
        re.lastIndex = 0;
        let m;
        while ((m = re.exec(line))) {
          const lit = m[1];
          const isLiteral = lit.startsWith('$');
          const raw = unescapeCs(lit.replace(/^\$/, '').slice(1, -1));
          if (!CJK.test(raw) || raw.length > 300) continue;
          const { key, count } = normalize(raw, isLiteral);
          // 键里残留花括号（示例载荷形如 {"a":{"b":"c"}}）时，字面量侧与运行时侧的引号/花括号配对不同，永远匹配不上
          if (key.includes('{') || key.includes('}')) { braceSkipped += 1; continue; }
          if (seen.has(key)) { seen.get(key).sites.push(`${rel}:${i + 1}`); continue; }
          const parts = key.split(SEP);
          const marked = parts.map((p, idx) => (idx < parts.length - 1 ? p + MARK(idx) : p)).join('');
          const row = { id: 'm' + String(rows.length + 1).padStart(4, '0'), key, zh: marked, dynCount: count, sample: raw, tag, sites: [`${rel}:${i + 1}`] };
          seen.set(key, row);
          rows.push(row);
        }
      }
    });
  }
  return { rows, braceSkipped };
}

// ---------- 子命令 ----------

function extract() {
  const { rows, braceSkipped } = scan();
  fs.mkdirSync(WORK, { recursive: true });
  fs.writeFileSync(path.join(WORK, 'msg-rows.json'), JSON.stringify(rows, null, 1), 'utf8');
  for (let i = 0; i < rows.length; i += BATCH) {
    const chunk = rows.slice(i, i + BATCH).map((r) => ({ id: r.id, zh: r.zh, sample: r.sample, tag: r.tag }));
    fs.writeFileSync(path.join(WORK, 'msg-batch-' + String(Math.floor(i / BATCH) + 1).padStart(2, '0') + '.json'), JSON.stringify(chunk, null, 1), 'utf8');
  }
  const dyn = rows.filter((r) => r.dynCount > 0).length;
  console.log(JSON.stringify({ out: WORK, files: sourceFiles().length, rows: rows.length, withDynamic: dyn, fullyStatic: rows.length - dyn, braceSkipped, batches: Math.ceil(rows.length / BATCH) }, null, 1));
  console.log('把每个 msg-batch-NN.json 译成同目录的 msg-en-NN.json（{"m0001":"English",…}，«N» 必须写成 {N}），再跑 merge。');
}

function readDictionary() {
  if (!fs.existsSync(DICT)) return {};
  try { return JSON.parse(fs.readFileSync(DICT, 'utf8')); } catch { return {}; }
}

function coverage() {
  const { rows } = scan();
  const dict = readDictionary();
  const missing = rows.filter((r) => !(r.key in dict));
  const unused = Object.keys(dict).filter((k) => !rows.some((r) => r.key === k));
  console.log(JSON.stringify({ scanned: rows.length, dictKeys: Object.keys(dict).length, missingFromDictionary: missing.length, notSeenInSource: unused.length }, null, 1));
  missing.slice(0, 40).forEach((r) => console.log('MISSING\t' + r.sites[0] + '\t' + r.zh.replace(new RegExp(SEP, 'g'), '«?»')));
  if (missing.length === 0) console.log('OK：源码里的中文文案全部有词典条目。');
  return missing.length === 0 ? 0 : 2;
}

function slotList(text, fromBraces) {
  if (fromBraces) return [...text.matchAll(/\{(\d+)\}/g)].map((m) => Number(m[1]));
  return [...text.matchAll(/\u00ab(\d+)\u00bb/g)].map((m) => Number(m[1]));
}

function merge() {
  const rowFiles = fs.readdirSync(WORK).filter((f) => /^msg-rows.*\.json$/.test(f)).sort();
  if (rowFiles.length === 0) { console.error('缺 ' + path.join(WORK, 'msg-rows.json') + '，先跑 extract'); process.exit(1); }
  const rows = rowFiles.flatMap((f) => JSON.parse(fs.readFileSync(path.join(WORK, f), 'utf8')));
  // rows 可能按 id 去重前重复出现同一条；这里按 id 收敛，避免后面的判据被重复行干扰
  const byId = new Map();
  for (const r of rows) {
    if (byId.has(r.id) && byId.get(r.id).zh !== r.zh) console.log('重复 id 但 zh 不一致: ' + r.id);
    byId.set(r.id, r);
  }
  const unique = [...byId.values()];

  const translations = new Map();
  const batchFiles = fs.readdirSync(WORK).filter((f) => /^msg-en-\d+\.json$/.test(f)).sort();
  for (const f of batchFiles) {
    const map = JSON.parse(fs.readFileSync(path.join(WORK, f), 'utf8'));
    for (const [id, en] of Object.entries(map)) {
      if (translations.has(id) && translations.get(id) !== en) console.log('CONFLICT ' + id);
      translations.set(id, en);
    }
  }

  const problems = [];
  const dict = {};
  let missing = 0;
  for (const r of unique) {
    const en = translations.get(r.id);
    // 词典键优先用 rows 里缓存的归一化键；旧格式的 rows 只有带 «N» 的 zh，
    // 这里把标记换回哨兵重建键（与 scan() 的产物等价）。
    const key = typeof r.key === 'string' && r.key ? r.key : String(r.zh || '').replace(/\u00ab\d+\u00bb/g, SEP);
    if (!key) { problems.push(`${r.id}: 无法确定词典键`); continue; }
    if (en === undefined) { missing++; continue; }
    if (!en.trim()) { problems.push(`${r.id}: 空译文`); continue; }
    if (CJK_OR_FULLWIDTH.test(en)) { problems.push(`${r.id}: 译文含中日韩字符或全角标点`); continue; }
    if (GUIILLEMET.test(en)) { problems.push(`${r.id}: 译文残留 «N» 标记，应写成 {N}`); continue; }
    const zhSlots = slotList(r.zh, false);
    const enSlots = slotList(en, true);
    if (zhSlots.length !== enSlots.length || zhSlots.some((v, i) => v !== enSlots[i])) {
      problems.push(`${r.id}: 槽位不匹配 zh=[${zhSlots}] en=[${enSlots}]`);
      continue;
    }
    if (en.replace(/\{\d+\}/g, '').match(/[{}]/)) {
      problems.push(`${r.id}: 去掉槽位后仍残留大括号`);
      continue;
    }
    if (Object.prototype.hasOwnProperty.call(dict, key) && dict[key] !== en) {
      problems.push(`重复键但译文不一致: ${key.slice(0, 60)}`);
      continue;
    }
    dict[key] = en;
  }

  if (problems.length) {
    problems.slice(0, 40).forEach((p) => console.log('PROBLEM\t' + p));
    console.error(`判据未通过：${problems.length} 条`);
    return 1;
  }

  // 词典只增不减：本轮 rows 没覆盖到的旧键必须原样保留。
  // 事故记录：出口点族（GlobalExceptionMiddleware 里 `return $"时间冲突：与 {n} 个已有事件重叠 {m} 分钟…"`
  // 这类不经过六个模式族的写法）是由 extras 行文件手工补的，extractor 看不见它们；
  // 一次 merge 因此把 13 条已验证过的英文条目**静默删掉**，条目数只从 722 变 723，
  // 90% 守卫不会拦，构建与覆盖率检查也都不会报——只有两条本地化测试变红才暴露。
  // 需要真正清理孤儿键时显式加 --prune。
  const previous = readDictionary();
  const previousCount = Object.keys(previous).length;
  let carried = 0;
  if (!process.argv.includes('--prune')) {
    for (const [k, v] of Object.entries(previous)) {
      if (!Object.prototype.hasOwnProperty.call(dict, k)) { dict[k] = v; carried++; }
    }
  }

  const sorted = {};
  for (const k of Object.keys(dict).sort()) sorted[k] = dict[k];

  // 写盘前的最后一道闸：新词典条目数明显少于现有发布词典时中止。
  // rows.json 与批次文件版本不匹配（例如 rows 换了 id 口径）会让上面的循环静默产出极少的键，
  // 直接把词典覆盖成几十个字节的空壳，而构建与测试都不会报错——只有线上文案退回中文才会暴露。
  if (previousCount && Object.keys(sorted).length < previousCount * 0.9 && !process.argv.includes('--force')) {
    console.error(`拒绝写入：新词典 ${Object.keys(sorted).length} 条 < 现有 ${previousCount} 条的 90%。`
      + '通常是 rows.json 与 msg-en-NN.json 不是同一批产物；确认无误再加 --force。');
    return 1;
  }

  fs.mkdirSync(path.dirname(DICT), { recursive: true });
  fs.writeFileSync(DICT, JSON.stringify(sorted, null, 1) + '\n', 'utf8');
  const dist = {};
  for (const k of Object.keys(sorted)) dist[(k.match(new RegExp(SEP, 'g')) || []).length] = (dist[(k.match(new RegExp(SEP, 'g')) || []).length] || 0) + 1;
  console.log(JSON.stringify({ rowFiles, rows: unique.length, batchFiles: batchFiles.length, batchEntries: translations.size, missing, dictKeys: Object.keys(sorted).length, problems: 0, bytes: Buffer.byteLength(JSON.stringify(sorted, null, 1)), slotDistribution: dist }, null, 1));
  return 0;
}

const cmd = process.argv[2];
if (cmd === 'extract') process.exit(extract());
if (cmd === 'coverage') process.exit(coverage());
if (cmd === 'merge') process.exit(merge());
console.log('用法：node scripts/i18n/api-message-dictionary.mjs extract|coverage|merge [--work <dir>] [--batch <n>]');
process.exit(1);
