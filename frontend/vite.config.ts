import { defineConfig, type Plugin } from 'vite'
import react from '@vitejs/plugin-react'
import { visualizer } from 'rollup-plugin-visualizer'
import path from 'path'

/**
 * 开发环境专用：放宽 index.html 里 CSP meta 的 `style-src`。
 *
 * 背景：`index.html` 的 CSP meta 用 `'nonce-__CSP_NONCE__'` 作占位符，只有后端
 * `SecurityHeadersMiddleware` 会把它替换成真实 nonce。**Vite 开发服务器不替换**，
 * 而 Vite 是以 `<style>` 注入 Tailwind 的（不带 nonce）→ 开发页面所有样式被 CSP 拦掉
 * （实测 `<nav>` 高度 57892px、页面完全无排版）。
 *
 * 这里只放宽 **style-src**（脚本仍受 `'self'` 约束），且仅 `apply: 'serve'` 生效——
 * 生产构建产物里的占位符原样保留，仍由后端替换，安全语义不变。
 */
function devCspStylePatch(): Plugin {
  return {
    name: 'noneadcss:dev-csp-style-patch',
    apply: 'serve',
    transformIndexHtml(html) {
      return html.replace(
        "style-src 'self' 'nonce-__CSP_NONCE__'",
        "style-src 'self' 'unsafe-inline'",
      )
    },
  }
}

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [
    devCspStylePatch(),
    react(),
    // Bundle 分析工具：仅在 ANALYZE=true 时启用，避免 stats.html 被误部署到生产环境
    // 用法: ANALYZE=true npm run build
    ...(process.env.ANALYZE === 'true'
      ? [visualizer({ open: true, filename: 'dist/stats.html' })]
      : []),
  ],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, 'src'),
    },
  },
  build: {
    rollupOptions: {
      output: {
        // 手动分包（manualChunks 函数形式，兼容 Vite v8 + Rolldown 类型；对象字面量在新版类型更严格的 Rolldown 下会被误判）：
        // 原理：返回 chunk 名决定哪些模块归入同一个独立 chunk，独立缓存命中，减少重复加载
        manualChunks: (id: string) => {
          if (id.includes('node_modules')) {
            if (id.includes('react') || id.includes('react-dom') || id.includes('react-router')) return 'vendor';
            if (id.includes('@tanstack')) return 'query';
            if (id.includes('@microsoft/signalr')) return 'signalr';
            if (id.includes('lucide-react')) return 'lucide';
            if (id.includes('axios') || id.includes('dompurify') || id.includes('lunar-javascript')) return 'utils';
          }
          return undefined;
        },
      },
    },
    // 启用生产构建压缩（Vite 默认已开启，此处显式声明便于后续调整）
    minify: 'esbuild',
    // 大于 2KB 的 chunk 触发分包（Vite 默认值，显式声明）
    chunkSizeWarningLimit: 2000,
  },
  server: {
    port: 5171,
    strictPort: true,
    // 忽略"原子写"产生的临时文件/目录。
    // 背景：工具与编辑器保存文件时常见做法是先写 `.<name>.<pid>.<rand>.tmpdir/<name>.tmp`
    // 再重命名替换；chokidar 会在这些临时项消失/被占用时抛
    // `EBUSY: resource busy or locked, watch '….tmpdir/….tmp'`，而 Vite 对该 error 事件
    // 没有兜底 → **整个 dev server 直接退出**（实测 2026-09-24 两次复现，均由代理写文件触发）。
    // 这些路径永远不会被应用 import，忽略它们即可。
    watch: {
      ignored: ['**/.*.tmpdir/**', '**/*.tmp', '**/.*.tmp'],
    },
    // 开发环境绑定所有网卡（允许局域网/远程主机/反向代理访问）
    host: process.env.VITE_HOST || '0.0.0.0',
    // Vite 5+ 启用 host 后默认严格校验 Host 请求头，防止 DNS 重绑定攻击；
    // 开发环境显式添加允许的业务域名/公网 IP，否则浏览器访问 css.example.com:5171 时会被 Vite 返回 403。
    // DNS 重绑定保护：显式 Host 白名单，可通过 VITE_ALLOWED_HOSTS 环境变量覆盖（逗号分隔）
    allowedHosts: process.env.VITE_ALLOWED_HOSTS
      ? process.env.VITE_ALLOWED_HOSTS.split(',')
      : ['localhost', 'css.example.com', 'css.example.com'],
    proxy: {
      '/api/': {
        target: 'http://127.0.0.1:5008',
        changeOrigin: true,
        timeout: 600_000,
        proxyTimeout: 600_000,
      },
      '/attachments': {
        target: 'http://127.0.0.1:5008',
        changeOrigin: true,
        timeout: 600_000,
        proxyTimeout: 600_000,
      },
      // API 文档由后端提供（Scalar 页面 + OpenAPI 描述），前端路由里没有 /docs，
      // 不做代理就会落到 SPA 回退（返回 index.html），浏览器看到的是 404 页面
      '/docs': {
        target: 'http://127.0.0.1:5008',
        changeOrigin: true,
        timeout: 600_000,
        proxyTimeout: 600_000,
      },
      '/openapi': {
        target: 'http://127.0.0.1:5008',
        changeOrigin: true,
        timeout: 600_000,
        proxyTimeout: 600_000,
      },

      '/hubs': {
        target: 'http://127.0.0.1:5008',
        changeOrigin: true,
        ws: true,
        // WebSocket 长连接：禁用 http-proxy 层面的读写超时，让 SignalR 自己的 KeepAlive/ClientTimeout 机制生效
        timeout: 0,
        proxyTimeout: 0,
        configure: (proxy, _options) => {
          // 静默预期的 ECONNRESET：浏览器标签页关闭/网络切换/后端空闲超时都会触发 RST，这是正常的生命周期事件
          proxy.on('error', (err: NodeJS.ErrnoException, _req, _res) => {
            if (err.code === 'ECONNRESET' || err.code === 'EPIPE') {
              // 不打印：这些是对端主动关闭连接，属于正常场景
              return;
            }
            console.error('[vite] api proxy error:', err);
          });
          proxy.on('proxyReqWs', (proxyReq, req, socket) => {
            // 对代理发起的 WebSocket 上游连接启用 TCP KeepAlive，防止中间设备因空闲丢弃连接
            socket.setKeepAlive(true, 30_000);
            socket.on('error', (err: NodeJS.ErrnoException) => {
              if (err.code === 'ECONNRESET' || err.code === 'ETIMEDOUT') return;
              console.error('[vite] ws proxy socket error:', err);
            });
          });
        },
      },
    }
  },
  preview: {
    port: 5172,
    strictPort: true,
    // T15: 默认仅绑定回环地址，避免 vite preview 暴露到所有网卡绕过 nginx 安全头；
    // 需要对外直连时可用 VITE_HOST=0.0.0.0 显式覆盖
    host: process.env.VITE_HOST || '127.0.0.1',
    allowedHosts: process.env.VITE_ALLOWED_HOSTS
      ? process.env.VITE_ALLOWED_HOSTS.split(',')
      : ['localhost', 'css.example.com', 'css.example.com']
  }
})
