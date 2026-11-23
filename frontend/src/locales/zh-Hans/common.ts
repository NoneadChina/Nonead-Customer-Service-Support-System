import type { Entries } from '../../i18n/types';

/** 跨模块复用的通用文案 */
export const common = {
  'common.save': '保存',
  'common.cancel': '取消',
  'common.delete': '删除',
  'common.edit': '编辑',
  // 翻译管理页的「导入」：全仓此前**没有**通用导入键（`mail.templates.variableCategory.import`
  // 是变量分类标签，语义不同）；同页的「导出」复用既有 `data.common.export`，不新造同义键
  'common.import': '导入',
  'common.confirm': '确认',
  'common.close': '关闭',
  // 外壳共享件自持文案（波次 12 队长扩围：`ConfirmDialog` 的 loading 文案在删除类确认框
  // 提交期间必然渲染、`Modal` 的关闭按钮 aria-label 每页都在，故随本波一并入字典）
  'common.confirmAction': '确认操作',
  'common.processing': '处理中...',
  'common.confirmHint': '请确认以下操作',
  'common.closeDialog': '关闭对话框',
  'common.search': '搜索',
  'common.reset': '重置',
  'common.retry': '重试',
  'common.loading': '加载中…',
  'common.empty': '暂无数据',
  // `useCrudPage` 的失败 toast（列表页与垃圾箱共用；该 hook 用 `tBound` 取词）
  'common.loadListFailed': '加载列表失败',
  'common.loadTrashFailed': '加载垃圾箱失败',
  'common.language': '语言',
  'common.switchLanguage': '切换语言',
  'common.saveSucceeded': '保存成功',
  'common.savedButLanguageSwitchFailed': '保存成功，但界面语言切换失败（已保留当前语言）',
  'common.languageSwitchFailedSuffix': '；但界面语言切换失败，已保留当前语言',

  // ── 应用外壳：路由占位与 404（App.tsx） ──
  'common.shell.loading': '加载中...',
  'common.shell.notFoundTitle': '页面未找到',
  'common.shell.notFoundDescription': '抱歉，您访问的页面不存在或已被移动。',
  'common.shell.backToHome': '返回首页',

  // ── 应用外壳：侧边栏 / 顶栏 / 用户菜单（Layout.tsx） ──
  'common.layout.adminConsole': '管理后台',
  'common.layout.apiDocs': 'API 文档',
  'common.layout.openMenu': '打开菜单',
  'common.layout.searchPlaceholder': '搜索...',
  'common.layout.userMenu': '用户菜单',
  'common.layout.unknownUser': '未知用户',
  'common.layout.profile': '个人中心',
  'common.layout.notificationCenter': '通知中心',
  'common.layout.emailSignatures': '邮件签名',
  'common.layout.logout': '退出登录',
  'common.layout.colleagueFallback': '同事',
  'common.layout.mentionWithPreview': '{name}在聊天中@了你: {preview}',
  'common.layout.mentionInChat': '{name}在聊天中@了你',
  'common.layout.mentionedYou': '{name}@了你',

  // ── 应用外壳：标签页标题 / 403 / 版本更新 / 运行期异常 ──
  //    Forbidden.tsx · ErrorBoundary.tsx（类组件，用 `tBound` 取词）·
  //    ModuleProtectedRoute.tsx（复用 `common.shell.loading`）
  //    `documentTitle` 由 I18nProvider 在渲染体里镜像到 `document.title`；
  //    `index.html` 的静态标题只作 JS 就绪前的兜底。
  'common.shell.documentTitle': '拓德客户服务与支持系统',
  'common.shell.forbiddenTitle': '无访问权限',
  'common.shell.forbiddenDescription': '您没有权限访问此页面，请联系管理员获取相应权限。',
  'common.shell.updateDetected': '检测到应用版本更新',
  'common.shell.updateRefreshing': '正在为您刷新页面以加载最新版本…',
  // 版本更新提示里「请 + 链接」被 `<button>` 切开，中英语序一致（前缀 + 链接），故拆两个键
  'common.shell.updateManualPrefix': '如果页面没有自动刷新，请',
  'common.shell.updateManualAction': '点击这里手动刷新',
  'common.shell.errorTitle': '页面发生错误',
  'common.shell.errorDescription': '应用发生意外错误，请先点击「重试」。如问题持续，再尝试重新加载页面或联系管理员。',
  'common.shell.errorRetry': '重试（无需刷新）',
  'common.shell.errorReload': '重新加载',

  // ── 应用外壳：全局搜索（GlobalSearch.tsx） ──
  'common.globalSearch.dialogLabel': '全局搜索',
  'common.globalSearch.placeholder': '搜索会话、客户、知识库...',
  'common.globalSearch.inputLabel': '搜索会话、客户、知识库',
  'common.globalSearch.type.conversation': '会话',
  'common.globalSearch.type.customer': '客户',
  'common.globalSearch.type.knowledge': '知识库',
  'common.globalSearch.noSubject': '(无主题)',
  'common.globalSearch.unnamedCustomer': '(未命名)',
  'common.globalSearch.hint': '输入关键词开始搜索',
  'common.globalSearch.hintSub': '支持会话主题、客户姓名邮箱、知识库文章',
  'common.globalSearch.minChars': '请输入至少 2 个字符',
  'common.globalSearch.searching': '搜索中...',
  'common.globalSearch.noResults': '未找到匹配「{query}」的结果',
  'common.globalSearch.navigateHint': '切换',
  'common.globalSearch.selectHint': '选择',
  'common.globalSearch.resultCount': '共 {count} 条结果',

  // ── 通用：无障碍标签与请求失败提示 ──
  //    `api/client.ts` 不是组件模块，用 `tBound` 取词；文案面向用户（toast / error.message）
  'common.closeNotification': '关闭通知',
  'common.requestFailed': '请求失败',
  'common.sessionExpiredRequestCancelled': '会话已失效，请求已取消',
  'common.forbidden403': '权限不足，请联系管理员 (403)',
  'common.readOnlyTooltip': '只读模式：当前角色无权修改此配置',
  'common.superAdminOnlyTooltip': '仅超级管理员可操作：测试、保存与订阅需要超级管理员权限',
} satisfies Entries;
