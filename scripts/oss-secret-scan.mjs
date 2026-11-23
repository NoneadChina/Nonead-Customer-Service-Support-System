// OSS readiness scan: identity-bearing paths, internal hosts, IPs, credential-shaped literals.
// Read-only. Usage: node scripts/oss-secret-scan.mjs [--json out.json]
import fs from 'fs';
import path from 'path';

const ROOT = path.resolve(process.argv[2] && !process.argv[2].startsWith('--') ? process.argv[2] : '.');
const jsonOutIdx = process.argv.indexOf('--json');
const JSON_OUT = jsonOutIdx >= 0 ? process.argv[jsonOutIdx + 1] : null;

const SKIP_DIRS = new Set(['bin', 'obj', 'bin-docs', 'node_modules', 'dist', 'wwwroot', '.git', '.dsh', '.dsh-memory', '.dsh-project-memory', 'playwright-report', 'test-results', 'coverage', 'data', 'backups', '.vs', 'TestResults', 'uploads', 'logs']);
// Text files that actually ship in a source release.
const TEXT_EXT = /\.(cs|cshtml|razor|md|html|mhtml|txt|json|jsonc|yml|yaml|xml|config|cmd|ps1|bat|sh|sql|ts|tsx|js|jsx|mjs|cjs|css|scss|env|example|slnx|sln|csproj|props|targets|editorconfig|resx|proto|py|ini|conf|toml)$/i;
const NAME_ONLY = /(^|\.)(Dockerfile|\.gitignore|\.gitattributes|\.env\.example|Makefile|NOTICE|LICENSE)$/i;

const RULES = [
  ['local-user-profile', /C:\\+Users\\+(?!<[yY]ou>)[A-Za-z0-9._-]+/g],
  ['local-user-profile-fwd', /C:\/+Users\/+(?!<[yY]ou>)[A-Za-z0-9._-]+/g],
  ['repo-absolute-path', /[A-H]:\\+MyProgram\\+GitLab\\+\w+/g],
  ['repo-absolute-path-fwd', /[A-H]:\/+MyProgram\/+GitLab\/+\w+/g],
  ['windows-drive-src', /\b[a-h]:\\\\+(?!Users)[A-Za-z0-9._ -]+/g],
  ['internal-host', /\b[a-z0-9-]+\.corp\.[a-z0-9.-]+/gi],
  ['internal-host2', /\b(ai-server|git-server|jenkins|nexus|harbor|gitlab-internal)\b/gi],
  ['lan-ip', /\b(?:10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})\b/g],
  ['public-ipv4', /\b(?!127\.)(?!0\.)(?:\d{1,3}\.){3}\d{1,3}\b/g],
  ['credential-assignment', /\b(?:password|passwd|pwd|secret|token|apikey|api_key|access_key|private_key|hmac|encryptionkey)\b\s*[:=]\s*["'`]?[A-Za-z0-9+\/=_-]{8,}/gi],
  ['pem-block', /-----BEGIN [A-Z ]*PRIVATE KEY-----/g],
  ['connection-string-pwd', /(?:Pwd|Password|Integrated Security)=\s*[^;"'\s][^;"']*/gi],
  ['email-personal', /\b[a-z0-9._%+-]+@(?!example\.com|nonead\.com|gmail\.com|outlook\.com|qq\.com|163\.com|microsoft\.com|nuget\.org|scalar\.com|gmail)[a-z0-9.-]+\.[a-z]{2,}\b/gi],
];

const PLACEHOLDER_OK = /(<REDACTED|<you>|<YOUR_|example\.com|CHANGE_ME|your-|-placeholder|REDACTED__ROTATE|Data Source=|Set-LocalSecret|<repo>)/i;

function* walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (SKIP_DIRS.has(e.name)) continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) yield* walk(p);
    else if (TEXT_EXT.test(e.name) || NAME_ONLY.test(e.name)) yield p;
  }
}

const findings = [];
let files = 0, skippedBinary = 0;
for (const p of walk(ROOT)) {
  const rel = path.relative(ROOT, p).replace(/\\/g, '/');
  const buf = fs.readFileSync(p);
  if (buf.includes(0)) { skippedBinary++; continue; }
  files++;
  const text = buf.toString('utf8').replace(/^\uFEFF/, '');
  const lines = text.split(/\r?\n/);
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    for (const [rule, re] of RULES) {
      re.lastIndex = 0;
      const m = line.match(re);
      if (!m) continue;
      for (const hit of m) {
        if (PLACEHOLDER_OK.test(hit)) continue;
        // 扫描器自身的规则文本会命中自己的主机名/关键词规则
        if (rel === 'scripts/oss-secret-scan.mjs' && (rule === 'internal-host' || rule === 'internal-host2')) continue;
        if (rule === 'public-ipv4') {
          // 文档/示例与容器网段（RFC 5737 / Docker bridge）不算泄露；组播/广播/回环同样排除
          if (/^(255\.|0\.|1\.1\.1\.1|8\.8\.8\.8|127\.|22[4-9]\.|23[0-9]\.|24[0-9]\.|25[0-5]\.)/.test(hit)) continue;
          if (/^(192\.0\.2\.|198\.51\.100\.|203\.0\.113\.|172\.17\.)/.test(hit)) continue;
          // 四段版本号（如 10.0.9.1 / 1.0.0.0）不是 IP：出现在依赖版本或程序集版本上下文里
          if (/\b(?:Version|version|AssemblyVersion|FileVersion|productVersion|v)\s*[=:"]*\s*$/.test(line.slice(0, line.indexOf(hit)))) continue;
          // 千分位写法（3.074.149.198 一类）不是 IP：每段都超过 255 或段数异常
          if (hit.split('.').some((seg) => seg.length > 3 || Number(seg) > 255)) continue;
        }
        if (rule === 'credential-assignment') {
          // 已保存网页的 quoted-printable 正文（=E5=88=B7…）与 JSX 属性传参不是凭据赋值
          if (/(=[0-9A-F]{2}){2,}/.test(hit)) continue;
          if (/[=:]\s*\{/.test(hit)) continue;
          if (/=\{|\bpassword=\{/.test(line)) continue;
          // 赋值右侧是变量/占位符（editingPassword、your_xxx_api_key、undefined）而不是字面密钥
          const rhs = hit.split(/[=:]/).slice(1).join('=');
          if (/^\s*(?:this\.)?[a-z_$][A-Za-z0-9_$]*$/.test(rhs) || /your[_-]|<[A-Z_]+>|\.\.\./.test(rhs)) continue;
        }
        if (rule === 'connection-string-pwd' && (/=\s*\{/.test(hit) || /=\s*(?:xxx|XXX|<[^>]+>|\*+)$/.test(hit))) continue;
        if (rule === 'email-personal' && /@(mhtml\.blink|test\.org|test\.com|x\.com|schemas\.[a-z0-9.-]+|w3\.org|purl\.org|x-ms[a-z0-9.-]*|example\.org|toolkit\.)$/.test(hit)) continue;
        findings.push({ rule, file: rel, line: i + 1, hit: hit.slice(0, 90) });
      }
    }
  }
}

const byRule = {};
for (const f of findings) (byRule[f.rule] ||= []).push(f);

console.log(`scanned files=${files} skipped-binary=${skippedBinary} findings=${findings.length}`);
for (const [rule] of RULES) {
  const list = byRule[rule] || [];
  const filesHit = new Set(list.map((x) => x.file));
  console.log(`  ${rule.padEnd(24)} findings=${String(list.length).padStart(4)} files=${filesHit.size}`);
}
console.log('--- detail (max 6 per rule) ---');
for (const [rule, list] of Object.entries(byRule)) {
  console.log(`\n[${rule}] ${list.length}`);
  for (const f of list.slice(0, 6)) console.log(`  ${f.file}:${f.line}  ${f.hit}`);
}
if (JSON_OUT) fs.writeFileSync(JSON_OUT, JSON.stringify({ files, skippedBinary, findings }, null, 1));
