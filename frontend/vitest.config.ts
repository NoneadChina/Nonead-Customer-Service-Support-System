/// <reference types="vitest" />
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'
import path from 'path'

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, 'src'),
    },
  },
  test: {
    globals: true,
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    // src/ 是应用代码；scripts/ 是仓库自带工具（i18n 扫描器 / 基线生成器），
    // 它们的自测同样必须进 CI，否则"扫描器写错却静默放行"没人管
    include: ['src/**/*.{test,spec}.{ts,tsx}', 'scripts/**/*.test.mjs'],
    css: false,
    // 单例预算：`src/pages/Localization.test.tsx` 的用例要 jsdom 渲染**整张真源字典**
    // （翻译管理页逐条列出所有词条），耗时就随真源规模线性增长：真源 2627 键时 ~3.7s、
    // 4960 键时 ~4.9s、5883 键时 ~5.5s —— 默认 5000ms 已被波次 13 撞穿（波次 13 验收实测，
    // 只加 `...calendar` 展开即可复现）。这是真实渲染耗时而非挂起，故提高预算；
    // 断言一条未改。底层产品事实（该页一次性渲染全部词条）登记在收尾打磨里另评。
    testTimeout: 20000,
  },
})
