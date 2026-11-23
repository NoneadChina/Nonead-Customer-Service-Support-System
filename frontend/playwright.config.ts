import { defineConfig, devices } from '@playwright/test';

/**
 * 端到端（E2E）测试配置。
 *
 * 前置条件（仓库不内置浏览器二进制，也不内置任何账号）：
 *   1. 安装浏览器：npx playwright install chromium
 *   2. 启动后端：deploy/_start-backend.cmd（API 监听 5008）
 *   3. 启动前端：pnpm dev（默认 5171，/api 由 vite 代理到 5008）
 *   4. 需要登录态时提供账号：E2E_EMAIL / E2E_PASSWORD 环境变量
 *
 * 用法：
 *   pnpm test:e2e              # 跑全部
 *   pnpm test:e2e -- --ui      # 交互模式
 *   E2E_BASE_URL=http://127.0.0.1:4173 pnpm test:e2e   # 打 preview 端口
 */
const baseURL = process.env.E2E_BASE_URL ?? 'http://127.0.0.1:5171';

export default defineConfig({
  testDir: './e2e',
  // 不并发跑同一个开发实例：登录态与验证码都受同一后端限流约束。
  workers: 1,
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  reporter: process.env.CI ? [['list'], ['html', { open: 'never' }]] : [['list']],
  timeout: 60_000,
  expect: { timeout: 10_000 },
  use: {
    baseURL,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    locale: 'zh-CN',
  },
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
  ],
});
