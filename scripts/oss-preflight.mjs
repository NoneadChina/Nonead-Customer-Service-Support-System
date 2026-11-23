// 开源发布前的一键闸门：把三条只读检查串成一条命令，任何一条不过就退出码 1。
//
//   node scripts/oss-preflight.mjs            跑全部三条
//   node scripts/oss-preflight.mjs --quick    跳过耗时的密钥扫描（日常提交用）
//
// 三条检查本身都是独立可运行的工具，本脚本只负责「按顺序跑 + 汇总判定」，
// 不复制它们的规则，也不写任何文件：
//   1) scripts/oss-secret-scan.mjs            密钥 / 内网主机 / 本机路径 / 个人信息
//   2) scripts/oss-doc-links.mjs check        文档死链与 file:/// 机器路径链接
//   3) scripts/i18n/api-message-dictionary.mjs coverage  源码中文文案是否都有英文词典条目
// 挂 CI：把本脚本放在 build 之后即可，见 docs/development.md §9.7 与 README「开源发布检查」。
import { spawnSync } from 'node:child_process';
import path from 'node:path';
import process from 'node:process';

const ROOT = path.resolve(import.meta.dirname, '..');
const quick = process.argv.includes('--quick');

// 每条检查：命令、参数、以及「怎么从输出里判定」——扫描器类工具靠退出码，
// coverage 靠退出码 + 输出里的数字，这里额外把数字打出来方便留档。
const CHECKS = [
  { name: '密钥与机器路径扫描', skip: quick, cmd: 'node', args: ['scripts/oss-secret-scan.mjs'] },
  { name: '文档链接检查', cmd: 'node', args: ['scripts/oss-doc-links.mjs', 'check'] },
  { name: '后端文案英文覆盖率', cmd: 'node', args: ['scripts/i18n/api-message-dictionary.mjs', 'coverage'] },
];

let failed = 0;
for (const c of CHECKS) {
  if (c.skip) {
    console.log(`\n=== [skip] ${c.name}（--quick）===`);
    continue;
  }
  console.log(`\n=== ${c.name} ===`);
  const r = spawnSync(c.cmd, c.args, { cwd: ROOT, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  const out = (r.stdout || '') + (r.stderr || '');
  process.stdout.write(out);
  if (r.error) {
    console.log(`判定: 无法启动 —— ${r.error.message}`);
    failed++;
  } else if (r.status !== 0) {
    console.log(`判定: 失败（退出码 ${r.status}）`);
    failed++;
  } else {
    console.log('判定: 通过');
  }
}

console.log(`\n==== 预检结果：${failed === 0 ? '全部通过' : `${failed} 项失败`} ====`);
if (failed === 0) {
  console.log('提示：本脚本只覆盖可机械判定的三类问题。发布前仍需人工确认 docs/ 的发布范围与运行时目录（data/ logs/ uploads/）没有被打包。');
}
process.exit(failed === 0 ? 0 : 1);
