import type { Entries } from '../../i18n/types';

/**
 * 在线客服（坐席工作台）与客户侧聊天挂件的文案真源。
 *
 * 分区（每个分区由一个文件承担，便于定位与复用）：
 * `status`（聊天会话状态，后端 code → 前端字典）· `page`（`pages/Chat.tsx` 概览 / 垃圾箱 / 确认弹窗）·
 * `list`（`ChatSessionList.tsx`）· `conversation`（`ChatConversation.tsx`）·
 * `bubble`（`ChatMessageBubble.tsx`）· `composer`（`ChatComposer.tsx`）·
 * `info`（`ChatInfoPanel.tsx`）· `transfer`（`ChatTransferModal.tsx`）·
 * `widget`（`pages/CustomerChatWidget.tsx`——客户侧公开页，读者是访客，单列一区以便区分口径）。
 *
 * **语言归属（本波次的设计裁定）**：客服侧一律跟随界面当前语言（`useI18n()`），与全站一致；
 * `CustomerChatWidget` 虽由 `App.tsx` 的 `<I18nProvider>` 包住，但挂在**公开路由** `/chat/widget`
 * （`routes/index.tsx` 的 `publicRoutes`，无需登录），Provider 因未登录而按
 * `localStorage → navigator.language → zh-Hans` 回落 ⇒ 客户看到的是**他自己浏览器的语言**。
 * 因此直接复用 `useI18n()`/`t`，**不另造语言开关、也不硬编码坐席语言**。
 *
 * **会话状态标签改走前端字典**：后端 `MappingExtensions.ToDto(ChatSession)`
 * （`NoneadCSS.Application/Mappings/MappingExtensions.cs:600-607`）下发中文 `StatusLabel`，
 * 取值恰为 `waiting` / `active` / `awaiting_rating` / `closed` / `transferred` 五码，
 * 未知码回落渲染原始 code。前端字典与之逐一对应，无缺无余。
 *
 * **拆键保样式**：原 JSX 把动态值包在带样式元素里（`font-semibold` / `font-mono` /
 * `tabular-nums` / `truncate` / `uppercase`）的位置，**不要**把值塞进译文字符串内部
 * （那样值会失去自己的元素）。本轮做法二选一：① 拆"前缀 / 后缀"两条键，值保留自己的元素
 * （如 `chat.page.trashCountBefore` / `After`）；② 保留带样式元素、只把其中的纯文本换成
 * `t()`。本模块只有垃圾箱数量行用了 ①，其余为 ②。
 */
export const chat = {
  // ── 聊天会话状态（后端 code → 前端标签；5 码，与 MappingExtensions.cs:600-607 逐一对应） ──
  'chat.status.waiting': '等待接待',
  'chat.status.active': '进行中',
  'chat.status.awaitingRating': '等待评分',
  'chat.status.closed': '已关闭',
  'chat.status.transferred': '已转工单',

  // ── chat.page：在线聊天页（pages/Chat.tsx）——概览 / 垃圾箱 / 删除确认 ──
  // ── 页头 / 概览 ──
  'chat.page.title': '在线聊天',
  'chat.page.subtitle': '管理实时聊天会话,与客户实时沟通',
  'chat.page.refresh': '刷新',
  'chat.page.statTotal': '总会话数',
  'chat.page.statUnread': '未读消息',
  'chat.page.accept': '接待',
  'chat.page.noOverview': '暂无概览数据',
  'chat.page.noOverviewHint': '点击右上角刷新按钮重试',
  'chat.page.noSessions': '暂无聊天会话',
  'chat.page.noSessionsHint': '新的客户聊天请求将显示在左侧列表',

  // ── toast 提示 ──
  'chat.page.toastAccepted': '已接待会话',
  'chat.page.acceptFailed': '接待失败',
  'chat.page.ratingRequested': '已发起满意度评价,请等待客户评分',
  'chat.page.sessionClosed': '会话已关闭',
  'chat.page.closeFailed': '关闭会话失败',
  'chat.page.convertedToConversation': '已转为工单',
  'chat.page.movedToTrash': '已移入垃圾箱',
  'chat.page.deleteFailed': '删除失败',
  'chat.page.bulkDeleted': '已删除 {count} 个会话',
  'chat.page.bulkDeleteFailed': '批量删除失败',
  'chat.page.restored': '已恢复会话',
  'chat.page.restoreFailed': '恢复失败',
  'chat.page.permanentlyDeleted': '已彻底删除',
  'chat.page.trashCleared': '已清空 {count} 个会话',
  'chat.page.clearTrashFailed': '清空失败',

  // ── 确认弹窗 ──
  // 同一动作的弹窗标题 / 确认按钮在中文里是同一串字面量，故共用一个动作标签键；
  // 消息体各自独立成键。
  'chat.page.confirmClose': '确认关闭',
  'chat.page.confirmCloseMessage': '确定要关闭此聊天会话吗?关闭后客户将无法继续发送消息。',
  'chat.page.moveToTrash': '移入垃圾箱',
  'chat.page.moveToTrashMessage': '确定要将此会话移入垃圾箱吗？可在垃圾箱中恢复或彻底删除。',
  'chat.page.bulkMoveToTrash': '批量移入垃圾箱',
  'chat.page.bulkMoveToTrashMessage': '确定要将选中的 {count} 个会话移入垃圾箱吗？可在垃圾箱中恢复或彻底删除。',
  'chat.page.permanentlyDelete': '彻底删除',
  'chat.page.permanentlyDeleteMessage': '确定要彻底删除此会话吗？此操作不可恢复。',
  'chat.page.clearTrash': '清空垃圾箱',
  'chat.page.clearTrashMessage': '确定要清空垃圾箱吗？所有会话将被永久删除，此操作不可恢复。',

  // ── 垃圾箱 Modal ──
  'chat.page.trashTitle': '垃圾箱 - 在线聊天会话',
  'chat.page.trashSearchPlaceholder': '搜索已删除的会话（客户名/邮箱）...',
  // 数量行原本是「共 <span class="font-semibold">{n}</span> 个已删除的会话」，
  // 拆前后缀两条键以保住那个 font-semibold 的 span（前后缀各带原有空格）。
  'chat.page.trashCountBefore': '共 ',
  'chat.page.trashCountAfter': ' 个已删除的会话',
  'chat.page.trashEmpty': '垃圾箱为空',
  'chat.page.trashNoMatch': '未找到匹配的已删除会话',
  'chat.page.trashEmptyHint': '暂无已删除的聊天会话',
  'chat.page.colCustomer': '客户',
  'chat.page.colEmail': '邮箱',
  'chat.page.colStatus': '状态',
  'chat.page.colDeletedAt': '删除时间',
  'chat.page.colActions': '操作',
  'chat.page.unknownCustomer': '未知客户',
  'chat.page.unknownCustomerParen': '(未知客户)',
  'chat.page.restore': '恢复',
  'chat.page.restoreAria': '恢复会话',
  'chat.page.permanentlyDeleteAria': '彻底删除会话',
  'chat.page.mobileEmail': '邮箱：',
  'chat.page.mobileDeletedAt': '删除时间：',
  'chat.page.trashPageOf': '第 {page} / {totalPages} 页',
  'chat.page.prevPage': '上一页',
  'chat.page.nextPage': '下一页',

  // ── chat.list：会话列表（components/chat/ChatSessionList.tsx） ──
  // ── 状态筛选 Tab（短标签，与 chat.status.* 的完整状态文案不同） ──
  'chat.list.filter.all': '全部',
  'chat.list.filter.waiting': '等待',
  'chat.list.filter.active': '进行中',
  'chat.list.filter.awaitingRating': '待评分',
  'chat.list.filter.closed': '已关闭',
  'chat.list.filter.transferred': '已转工单',

  // ── 面板标题 ──
  'chat.list.title': '会话列表',
  'chat.list.subtitle': '实时接待 & 历史会话',

  // ── 工具栏 / 搜索 / 多选 ──
  'chat.list.selectMode': '多选模式',
  'chat.list.exitSelectMode': '退出多选',
  'chat.list.trash': '垃圾箱',
  'chat.list.searchPlaceholder': '搜索客户名称/邮箱',
  'chat.list.clearSearch': '清空搜索',
  'chat.list.allMailboxes': '全部邮箱',
  'chat.list.selectAll': '全选',
  'chat.list.deselectAll': '取消全选',
  'chat.list.selectedCount': '已选 {selected} / {total}',
  'chat.list.bulkDelete': '批量删除',

  // ── 空状态 ──
  'chat.list.emptyTitle': '暂无会话',
  'chat.list.emptyFiltered': '没有匹配的会话，尝试调整筛选条件',
  'chat.list.emptyHint': '暂无聊天会话，新客户咨询将在此处实时显示',

  // ── 会话卡片 ──
  'chat.list.moveToTrash': '移入垃圾箱',
  'chat.list.awaitingRatingHint': '等待客户评分',
  'chat.list.unknownCustomer': '未知客户',
  'chat.list.accept': '接待',

  // ── 分页 ──
  'chat.list.prevPage': '上一页',
  'chat.list.nextPage': '下一页',

  // ── chat.conversation：会话详情（components/chat/ChatConversation.tsx） ──
  // ── 日期分组 ──
  'chat.conversation.dateToday': '今天',
  'chat.conversation.dateYesterday': '昨天',
  'chat.conversation.dateTomorrow': '明天',

  // ── 会话头 ──
  'chat.conversation.typing': '{name}正在输入...',
  'chat.conversation.back': '返回',
  'chat.conversation.unknownCustomer': '未知客户',
  'chat.conversation.awaitingRating': '等待客户评分',
  'chat.conversation.searchMessages': '搜索消息',
  'chat.conversation.transferDisabledAwaitingRating': '等待客户评分中，无法转工单',
  'chat.conversation.transfer': '转工单',
  'chat.conversation.awaitingRatingTimeout': '等待评分中，超时自动关闭',

  // ── 搜索条 ──
  'chat.conversation.searchInConversation': '搜索本会话消息...',
  'chat.conversation.noMatches': '无匹配',
  'chat.conversation.prevMatch': '上一个匹配',
  'chat.conversation.nextMatch': '下一个匹配',
  'chat.conversation.closeSearch': '关闭搜索',

  // ── 消息流 ──
  'chat.conversation.loadingMessages': '加载消息中...',
  'chat.conversation.startNewTitle': '开始新的对话',
  'chat.conversation.startNewHint': '在下方输入框中给客户发送第一条消息吧',

  // ── chat.bubble：消息气泡（components/chat/ChatMessageBubble.tsx） ──
  // ── 系统消息横幅 ──
  'chat.bubble.satisfactionSurvey': '满意度调查',
  'chat.bubble.systemNotice': '系统通知',

  // ── 已读回执 / 编辑标记 ──
  'chat.bubble.readAt': '已读 {time}',
  'chat.bubble.unread': '未读',
  'chat.bubble.editedAt': '已编辑 {time}',
  'chat.bubble.edited': '已编辑',

  // ── 发送者回退名 / 撤回 ──
  'chat.bubble.agent': '客服',
  'chat.bubble.recalledBy': '{name}撤回了一条消息',

  // ── 编辑态 ──
  'chat.bubble.editHint': 'Enter 保存 · Esc 取消',
  'chat.bubble.saving': '保存中',

  // ── 附件 ──
  'chat.bubble.viewImage': '查看图片',
  'chat.bubble.image': '图片',
  'chat.bubble.attachment': '附件',
  'chat.bubble.clickToDownload': '点击下载',

  // ── 操作 ──
  'chat.bubble.copyMessage': '复制消息',
  'chat.bubble.moreActions': '更多操作',
  'chat.bubble.recall': '撤回',

  // ── chat.composer：输入区（components/chat/ChatComposer.tsx） ──
  // ── @提及 自动完成（ChatComposer.tsx，仅客服侧） ──
  'chat.composer.mentionTitle': '@提及',
  'chat.composer.mentionNoMatch': '未找到匹配的用户',
  'chat.composer.mentionEmpty': '暂无可提及的用户',
  'chat.composer.mentionHint': '↑↓ 选择 · Enter 确认 · Esc 取消',
  'chat.composer.mentionCount': '{count} 人',

  // ── 快捷回复弹出层 ──
  'chat.composer.cannedTitle': '快捷回复',
  'chat.composer.cannedSubtitle': '快速插入常用话术',
  'chat.composer.cannedSearchPlaceholder': '搜索快捷回复...',
  'chat.composer.cannedNoMatch': '未找到匹配的快捷回复',
  'chat.composer.cannedEmpty': '暂无快捷回复模板',

  // ── 表情面板 ──
  'chat.composer.emojiTitle': '表情',
  'chat.composer.emojiSubtitle': '常用表情',
  'chat.composer.insertEmoji': '插入表情 {emoji}',

  // ── 附件预览与上传 ──
  'chat.composer.removeAttachment': '移除附件',
  'chat.composer.toolbar': '工具栏',
  'chat.composer.uploadAttachmentHint': '上传附件 (最大 25MB)',
  'chat.composer.uploadAttachment': '上传附件',

  // ── 输入框与发送按钮 ──
  'chat.composer.sessionClosed': '会话已关闭',
  'chat.composer.placeholder': '输入消息,Enter 发送 · Shift+Enter 换行',
  'chat.composer.sendMessage': '发送消息',
  'chat.composer.enterContent': '请输入内容',
  'chat.composer.send': '发送',

  // ── 校验与失败提示（toast） ──
  'chat.composer.fileTooLarge': '文件大小不能超过 25MB(当前 {size})',
  'chat.composer.unsupportedFileType': '不支持的文件类型,请上传图片、文档或压缩包',
  'chat.composer.uploadFailed': '附件上传失败',
  'chat.composer.sendFailed': '发送失败',

  // ── chat.info：信息面板（components/chat/ChatInfoPanel.tsx） ──
  'chat.info.unknownCustomer': '未知客户',

  // ── 评分区 ──
  'chat.info.customerRating': '客户评分',

  // ── 客户信息 ──
  'chat.info.customerSection': '客户信息',
  'chat.info.email': '邮箱',
  'chat.info.notProvided': '未提供',
  'chat.info.phone': '手机',
  'chat.info.ipAddress': 'IP 地址',
  'chat.info.customerId': '客户 ID',
  'chat.info.notLinked': '未关联',

  // ── 会话信息 ──
  'chat.info.conversationSection': '会话信息',
  'chat.info.subject': '主题',
  'chat.info.noSubject': '无主题',
  'chat.info.mailbox': '所属邮箱',
  'chat.info.agent': '接待客服',
  'chat.info.messageCount': '消息总数',
  'chat.info.unreadCount': '{count} 未读',
  'chat.info.createdAt': '创建时间',
  'chat.info.updatedAt': '最后更新',
  'chat.info.closedAt': '关闭时间',

  // ── 关联工单（工单在英文里统一叫 conversation） ──
  'chat.info.linkedConversation': '关联工单',
  'chat.info.viewConversation': '查看工单',

  // ── chat.transfer：转工单弹窗（components/chat/ChatTransferModal.tsx） ──
  // ── 转工单弹窗（ChatTransferModal.tsx） ──
  'chat.transfer.title': '转工单',
  'chat.transfer.bannerTitle': '将聊天会话转为工单',
  'chat.transfer.descForCustomer': '客户 {name} 的聊天会话将被转换为工单继续跟进,聊天记录会作为附件保留。',
  'chat.transfer.descCurrent': '当前聊天会话将被转换为工单继续跟进,聊天记录会作为附件保留。',
  'chat.transfer.mailboxLabel': '目标邮箱',
  'chat.transfer.optional': '(可选)',
  'chat.transfer.useOriginalMailbox': '使用会话原邮箱',
  'chat.transfer.agentLabel': '分配客服',
  'chat.transfer.unassignedOption': '不指定(自动分配)',
  'chat.transfer.subjectLabel': '工单主题',
  'chat.transfer.subjectPlaceholder': '留空将自动生成',
  'chat.transfer.next': '下一步',

  // ── 二次确认 ──
  'chat.transfer.confirmTitle': '确认转工单',
  'chat.transfer.confirmMessage': '确定要将此聊天会话转换为工单吗?转换后聊天会话将被标记为已转工单。',
  'chat.transfer.converting': '转换中...',
  'chat.transfer.confirmAction': '确认转换',

  // ── chat.widget：客户侧聊天挂件（pages/CustomerChatWidget.tsx，公开页 /chat/widget） ──
  // ── 发起咨询表单：校验错误（键值逐字照抄改造前，数字沿用 maxLength 常量） ──
  'chat.widget.form.error.nameRequired': '请输入您的姓名',
  'chat.widget.form.error.nameTooLong': '姓名长度不能超过 50 个字符',
  'chat.widget.form.error.emailRequired': '请输入您的邮箱',
  'chat.widget.form.error.emailInvalid': '邮箱格式不正确,请检查(例如 example@domain.com)',
  'chat.widget.form.error.emailTooLong': '邮箱长度不能超过 100 个字符',
  'chat.widget.form.error.phoneRequired': '请输入您的手机号',
  'chat.widget.form.error.phoneInvalid': '手机号格式不正确(例如 13812345678 或 +86 138 1234 5678)',
  'chat.widget.form.error.subjectTooLong': '主题长度不能超过 200 个字符',
  'chat.widget.form.error.messageTooLong': '消息长度不能超过 2000 个字符',

  // ── 欢迎卡 ──
  'chat.widget.welcome.title': '您好,很高兴为您服务',
  'chat.widget.welcome.subtitle': '我们的专业客服团队随时为您提供帮助。请填写信息开始咨询,首次回复通常在 2 分钟内',
  'chat.widget.welcome.online': '客服在线',
  'chat.widget.welcome.secure': '安全加密',
  'chat.widget.welcome.private': '隐私保护',

  // ── 表单标签与占位符 ──
  'chat.widget.form.name': '您的姓名',
  'chat.widget.form.namePlaceholder': '请输入您的姓名或昵称',
  'chat.widget.form.email': '联系邮箱',
  'chat.widget.form.phone': '联系手机',
  'chat.widget.form.phonePlaceholder': '13812345678 或 +86 138 1234 5678',
  'chat.widget.form.subject': '咨询主题',
  'chat.widget.form.optional': '(可选)',
  'chat.widget.form.subjectPlaceholder': '请简要描述您的问题',
  'chat.widget.form.message': '问题描述',
  'chat.widget.form.messagePlaceholder': '请描述您遇到的问题(Enter 发送,Shift+Enter 换行)',
  'chat.widget.form.connecting': '正在连接客服...',
  'chat.widget.form.start': '立即咨询',
  'chat.widget.form.terms': '点击开始咨询,即代表您同意我们的服务条款',

  // ── 消息气泡与内容渲染 ──
  'chat.widget.message.imageAlt': '图片',
  'chat.widget.message.fileFallback': '文件',
  'chat.widget.message.satisfactionSurvey': '满意度调查',
  'chat.widget.message.systemNotice': '系统通知',
  'chat.widget.message.agentInitial': '客',
  'chat.widget.message.you': '我',
  'chat.widget.message.agent': '客服',
  'chat.widget.message.edited': '已编辑',
  'chat.widget.message.recalled': '该消息已被撤回',
  'chat.widget.message.viewAttachment': '查看附件',
  'chat.widget.message.read': '已读',
  'chat.widget.message.sent': '已发送',

  // ── 输入指示器（{name} 为对方名称） ──
  'chat.widget.typing': '{name}正在输入...',

  // ── 评分表单 ──
  'chat.widget.rating.veryDissatisfied': '非常不满意',
  'chat.widget.rating.dissatisfied': '不满意',
  'chat.widget.rating.neutral': '一般',
  'chat.widget.rating.satisfied': '满意',
  'chat.widget.rating.verySatisfied': '非常满意',
  'chat.widget.rating.required': '请选择评分(1-5 星)',
  'chat.widget.rating.title': '会话已结束',
  'chat.widget.rating.subtitle': '感谢您的宝贵咨询!请为本次服务打个分,帮助我们持续改进',
  'chat.widget.rating.countdownBefore': '请在',
  'chat.widget.rating.countdownAfter': '内完成评分,超时将自动关闭',
  'chat.widget.rating.starAria': '{star} 星 - {label}',
  'chat.widget.rating.starTitle': '{star} 星:{label}',
  'chat.widget.rating.hint': '点击一颗星开始评分',
  'chat.widget.rating.comment': '详细评价',
  'chat.widget.rating.commentPlaceholder': '请告诉我们您对本次服务的感受和建议...',
  'chat.widget.rating.skip': '稍后再说',
  'chat.widget.rating.submitting': '提交中...',
  'chat.widget.rating.submit': '提交评分',

  // ── 请求失败提示 ──
  'chat.widget.error.initiateFailed': '发起咨询失败,请稍后重试',
  'chat.widget.error.sendFailed': '发送失败',
  'chat.widget.error.closeFailed': '关闭会话失败',
  'chat.widget.error.rateFailed': '提交评分失败',

  // ── SignalR 连接状态 ──
  'chat.widget.connection.connected': '已连接',
  'chat.widget.connection.connecting': '连接中',
  'chat.widget.connection.reconnecting': '重连中',
  'chat.widget.connection.disconnected': '已断开',

  // ── 无障碍标签与悬停提示 ──
  'chat.widget.a11y.openChat': '打开聊天',
  'chat.widget.a11y.startChat': '点击开始在线咨询',
  'chat.widget.a11y.minimize': '最小化',
  'chat.widget.a11y.minimizeToBubble': '最小化到悬浮按钮',
  'chat.widget.a11y.endChat': '结束会话',
  'chat.widget.a11y.endChatHint': '结束本次会话并为服务评分',
  'chat.widget.a11y.newChat': '新会话',
  'chat.widget.a11y.newChatHint': '开启新的咨询会话',
  'chat.widget.a11y.dismissError': '关闭错误',
  'chat.widget.a11y.emoji': '表情',
  'chat.widget.a11y.insertEmoji': '插入表情',
  'chat.widget.a11y.send': '发送',
  'chat.widget.a11y.sendMessage': '发送消息',
  'chat.widget.a11y.sendDisabled': '请输入内容后发送',

  // ── 头部与空态 ──
  'chat.widget.header.title': '在线客服',
  'chat.widget.loading.messages': '正在加载消息...',
  'chat.widget.loading.pleaseWait': '请稍候',
  'chat.widget.empty.title': '欢迎来到在线客服!',
  'chat.widget.empty.subtitle': '在下方输入问题,我们的客服将尽快为您解答',
  'chat.widget.composer.placeholder': '输入消息,Enter 发送,Shift+Enter 换行...',

  // ── 已完成页 ──
  'chat.widget.completed.ratedTitle': '感谢您的反馈!',
  'chat.widget.completed.timeoutTitle': '会话已自动结束',
  'chat.widget.completed.ratedSubtitle': '您的评价对我们非常重要,我们将持续改进服务质量。',
  'chat.widget.completed.timeoutSubtitle': '由于未在规定时间内完成评分,会话已自动关闭。',
  'chat.widget.completed.retryHint': '如需再次咨询,可点击下方按钮开启新的会话',
  'chat.widget.completed.newChat': '开启新咨询',
} satisfies Entries;
