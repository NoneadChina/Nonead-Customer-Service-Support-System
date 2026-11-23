// ESLint 9+ Flat Config
// 依赖（package.json devDependencies 已安装）：eslint、@eslint/js、typescript-eslint、
// eslint-plugin-react-hooks、eslint-plugin-react-refresh
// TODO(L-FE4): 考虑引入 eslint-plugin-jsx-a11y 以静态检查可访问性（a11y）问题，
//   如 img 缺少 alt、button 缺少 aria-label、交互元素键盘可达性等。
//   引入方式：npm i -D eslint-plugin-jsx-a11y，并在下方 plugins 中注册、rules 中启用推荐规则集。
import js from '@eslint/js';
import reactHooks from 'eslint-plugin-react-hooks';
import reactRefresh from 'eslint-plugin-react-refresh';
import typescriptEslint from 'typescript-eslint';

export default typescriptEslint.config(
  { ignores: ['dist', 'coverage'] },
  {
    extends: [js.configs.recommended, ...typescriptEslint.configs.recommended],
    files: ['**/*.{ts,tsx}'],
    languageOptions: {
      ecmaVersion: 2020,
      globals: {
        window: 'readonly',
        document: 'readonly',
        console: 'readonly',
        localStorage: 'readonly',
        navigator: 'readonly',
        location: 'readonly',
        history: 'readonly',
        setTimeout: 'readonly',
        clearTimeout: 'readonly',
        setInterval: 'readonly',
        clearInterval: 'readonly',
        fetch: 'readonly',
        requestAnimationFrame: 'readonly',
        cancelAnimationFrame: 'readonly',
      },
    },
    plugins: {
      'react-hooks': reactHooks,
      'react-refresh': reactRefresh,
    },
    rules: {
      ...reactHooks.configs.recommended.rules,
      'react-refresh/only-export-components': ['warn', { allowConstantExport: true }],
      '@typescript-eslint/no-unused-vars': ['warn', { argsIgnorePattern: '^_', varsIgnorePattern: '^_', caughtErrorsIgnorePattern: '^_' }],
      '@typescript-eslint/no-explicit-any': 'warn',
    },
  },
  {
    files: ['**/__tests__/**', '**/*.test.{ts,tsx}', '**/*.spec.{ts,tsx}'],
    rules: {
      '@typescript-eslint/no-explicit-any': 'off',
    },
  },
  {
    // scripts/ 下的 Node 工具脚本（.mjs）：走 JS 推荐规则 + 必需的 Node 全局量。
    // 上面的块只匹配 ts/tsx，不覆盖的话 eslint 对这些脚本是**零检查**——
    // "没有报错"不等于"检查过"，而 i18n 扫描器本身就是门禁，不能被漏检。
    files: ['scripts/**/*.mjs'],
    extends: [js.configs.recommended],
    languageOptions: {
      ecmaVersion: 2020,
      sourceType: 'module',
      globals: { process: 'readonly', console: 'readonly' },
    },
  },
);
