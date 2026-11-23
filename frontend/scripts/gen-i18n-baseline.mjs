/**
 * 生成 / 更新硬编码中文基线快照。
 *
 * 用法：cd frontend && node scripts/gen-i18n-baseline.mjs
 *
 * 只在"刻意接受存量"或"本批次已清理掉一部分"时运行，产物必须提交。
 * 基线按**文件命中数**记录，门禁只允许"不比基线更差"（见 check-hardcoded-chinese.mjs）。
 *
 * ⚠ 复用扫描器的导出，**不要**在这里复制豁免清单或目录遍历逻辑——
 * 两处各写一份必然漂移，而漂移的门禁比没有门禁更危险。
 */
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import {
  PROJECT_ROOT,
  collectFiles,
  findHardcodedChinese,
  isExempt,
  relativeToRoot,
} from './check-hardcoded-chinese.mjs';

const srcDir = join(PROJECT_ROOT, 'src');
const counts = {};
let total = 0;

for (const abs of collectFiles(srcDir)) {
  const rel = relativeToRoot(abs);
  if (isExempt(rel)) continue;
  const hits = findHardcodedChinese(readFileSync(abs, 'utf8'), rel);
  if (hits.length > 0) {
    counts[rel] = hits.length;
    total += hits.length;
  }
}

const target = join(PROJECT_ROOT, 'scripts', 'hardcoded-chinese-baseline.json');
writeFileSync(target, `${JSON.stringify(counts, null, 2)}\n`);
console.log(`已写入基线：${Object.keys(counts).length} 个文件 / ${total} 处`);
console.log(`  路径：${target}`);
