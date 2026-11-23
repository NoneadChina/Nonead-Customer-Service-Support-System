// Markdown link hygiene for the published tree (read-only check, optional --apply fix).
//
//   node scripts/oss-doc-links.mjs check          // report stale file:/// links and broken relative links
//   node scripts/oss-doc-links.mjs fix            // dry-run the rewrite
//   node scripts/oss-doc-links.mjs fix --apply    // rewrite in place
//
// Why: historical docs were written with `file:///c:/noneadcss/...` absolute machine links, which
// are dead on GitHub. `fix` rewrites them to links relative to the .md file, and demotes links whose
// target no longer exists in the tree to plain inline code (so the path stays readable but is not a
// broken link). Placeholder links inside samples, e.g. `[文本](url)`, are never touched.
import { readdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { join, dirname, relative, resolve } from 'node:path';

const ROOT = resolve(process.cwd());
const MODE = process.argv[2] === 'fix' ? 'fix' : 'check';
const APPLY = process.argv.includes('--apply');

const SKIP = new Set(['bin', 'obj', 'bin-docs', 'node_modules', 'dist', 'wwwroot', '.git', 'data', 'logs', 'uploads', 'backups', '.dsh-memory', '.dsh-project-memory', '.vs', 'TestResults', 'playwright-report', 'test-results']);
const LINK = /\[([^\]]*)\]\(([^)\s]+)(?:\s+"[^"]*")?\)/g;
const FILEURI = /\[([^\]]+)\]\((file:\/\/\/[cC]:\/(?:noneadcss|noneadcss_prod)\/([^)#]*)(#[^)]*)?)\)/g;
const DEBT_FILE = 'NoneadCSS_TechnicalDebt_2026Q4.md';
const BARE_DEBT = new RegExp(`\\[([^\\]]+)\\]\\((?:\\.\\./)?${DEBT_FILE.replace(/\./g, '\\.')}\\)`, 'g');

function walk(dir, out = []) {
  for (const e of readdirSync(dir, { withFileTypes: true })) {
    if (e.isDirectory()) { if (!SKIP.has(e.name)) walk(join(dir, e.name), out); }
    else if (e.name.endsWith('.md')) out.push(join(dir, e.name));
  }
  return out;
}

function relLink(mdFile, repoPath) {
  const target = join(ROOT, repoPath.replace(/\//g, '\\'));
  if (!target.startsWith(ROOT) || !existsSync(target)) return null;
  let r = relative(dirname(mdFile), target).replace(/\\/g, '/');
  if (!r.startsWith('.')) r = './' + r;
  return r;
}

const files = walk(ROOT);
const missing = [];
let fileUri = 0, placeholder = 0, touched = 0, relativized = 0, demoted = 0, debtFixed = 0;

for (const mdFile of files) {
  const rel = mdFile.replace(ROOT + '\\', '');
  const src = readFileSync(mdFile, 'utf8');

  // check pass: classify every link
  LINK.lastIndex = 0;
  let m;
  while ((m = LINK.exec(src))) {
    const target = m[2].trim();
    if (/^(https?:|mailto:|#|data:)/i.test(target) || target.startsWith('/')) continue;
    const line = src.slice(0, m.index).split('\n').length;
    if (/^file:\/\//i.test(target)) { fileUri++; continue; }
    const clean = decodeURIComponent(target.split('#')[0].split('?')[0]);
    if (!clean) continue;
    if (!existsSync(resolve(dirname(mdFile), clean))) {
      // `[text](url)` style placeholders inside documentation samples are expected, not broken links
      if (/^(url|attachmentUrl|\$\{[^}]+\})$/i.test(clean)) { placeholder++; continue; }
      missing.push(`${rel}:${line}  [${m[1]}] -> ${target}`);
    }
  }

  if (MODE !== 'fix') continue;

  let out = src;
  out = out.replace(FILEURI, (whole, label, uri, repoPath, anchor) => {
    const r = relLink(mdFile, repoPath);
    if (r) { relativized++; return `[${label}](${r}${anchor ?? ''})`; }
    demoted++; return '`' + repoPath.replace(/\/$/, '') + (anchor ?? '') + '`';
  });
  out = out.replace(BARE_DEBT, (whole, label) => {
    const r = relLink(mdFile, 'docs/' + DEBT_FILE);
    if (!r) return whole;
    debtFixed++; return `[${label}](${r})`;
  });
  if (out !== src) { touched++; if (APPLY) writeFileSync(mdFile, out, 'utf8'); }
}

console.log(`md files=${files.length} fileUriLinks=${fileUri} brokenRelativeLinks=${missing.length} samplePlaceholders=${placeholder}`);
for (const x of missing.slice(0, 40)) console.log(`  BROKEN  ${x}`);
if (missing.length > 40) console.log(`  BROKEN  … and ${missing.length - 40} more`);
if (MODE === 'fix') console.log(`${APPLY ? 'APPLIED' : 'DRY-RUN'} files=${touched} relativized=${relativized} demotedToText=${demoted} debtLinksFixed=${debtFixed}`);
process.exit(missing.length > 0 || fileUri > 0 ? 1 : 0);
