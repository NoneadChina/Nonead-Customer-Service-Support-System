import type { Entries } from '../../i18n/types';

/** 侧边栏导航与分组标题（真源，来自 Layout.tsx 原先的 'key|中文' 字面量） */
export const nav = {
  // 分组标题
  'nav.group.helpdesk': '服务台',
  'nav.group.mail': '邮件中心',
  'nav.group.team': '团队与权限',
  'nav.group.automation': '自动化',
  'nav.group.notification': '通知中心',
  'nav.group.integration': '集成与扩展',
  'nav.group.data': '数据中心',
  'nav.group.features': '功能模块',
  'nav.group.system': '系统管理',

  // 服务台
  'nav.dashboard': '仪表盘',
  'nav.conversations': '会话管理',
  'nav.customers': '客户管理',
  'nav.tags': '标签管理',
  'nav.satisfaction': '满意度评价',

  // 邮件中心
  'nav.mailboxes': '邮箱管理',
  'nav.folders': '文件夹管理',
  'nav.mailTemplates': '邮件模板',
  'nav.emailSignatures': '邮件签名',
  'nav.cannedResponses': '快捷回复',

  // 团队与权限
  'nav.users': '用户管理',
  'nav.teams': '团队管理',
  'nav.roles': '角色权限',

  // 自动化
  'nav.workflows': '工作流自动化',
  'nav.sla': 'SLA 策略',
  'nav.macros': '宏管理',

  // 通知中心
  'nav.notifications': '通知管理',
  'nav.notificationTemplates': '通知模板',
  'nav.notificationRules': '通知规则',

  // 集成与扩展
  'nav.webhooks': 'Webhook',
  'nav.apiKeys': 'API 密钥',
  'nav.customFields': '自定义字段',
  'nav.modules': '模块管理',

  // 数据中心
  'nav.reports': '报表统计',
  'nav.customReports': '自定义报表',
  'nav.importExport': '数据导入导出',

  // 功能模块
  'nav.calendar': '日历管理',
  'nav.knowledge': '知识库',
  'nav.timeTracking': '时间追踪',
  'nav.chat': '在线聊天',
  'nav.wechat': '微信聊天',
  'nav.dingding': '钉钉聊天',
  'nav.feishu': '飞书聊天',
  'nav.whatsapp': 'WhatsApp',
  'nav.messenger': 'Messenger',
  'nav.telegram': 'Telegram',
  'nav.line': 'LINE',
  'nav.imessage': 'iMessage',

  // 系统管理
  'nav.auditLogs': '审计日志',
  'nav.sendLogs': '发送日志',
  'nav.systemTools': '系统工具',
  'nav.localization': '翻译管理',
  'nav.settings': '系统设置',
} satisfies Entries;
