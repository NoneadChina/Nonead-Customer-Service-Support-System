import type { Entries } from '../../i18n/types';

/**
 * 会话簇文案（真源）。抽取自：
 * `pages/Conversations.tsx`、`pages/ConversationDetail.tsx`、
 * `components/ReplyBox.tsx`、`components/ConversationSidebar.tsx`、
 * `components/{WeChat,WhatsApp,Feishu,DingDing}ReplyBox.tsx`。
 *
 * 分区约定：
 * - `conversations.list.*`     会话列表页
 * - `conversations.detail.*`   会话详情页
 * - `conversations.reply.*`    回复框公共外壳（ReplyBox 与 4 个渠道回复框共用）
 * - `conversations.sidebar.*`  会话详情右侧边栏
 * - `conversations.channel.*`  渠道特有文案（微信 / WhatsApp / 飞书 / 钉钉）
 * - `conversations.shared.*`   跨文件复用的小词条（无主题、微信客户）
 *
 * **状态 / 优先级 / 来源类型 / 角色 / 时区标签不在这里**：它们已经由 `format` 模块
 * 统一持有（`format.status.*`、`format.priority.*`、`format.sourceType.*`、
 * `format.role.*`、`format.timezone.*`），本模块只引用、不重复造键。
 * `format.unassigned`（未分配）在本次一并补进 `format`——它同属被列表/详情/侧栏
 * 共用的「分配状态」标签。
 *
 * **前缀 / 后缀拆键约定（值必须保留自己的 `<span>`）**：译文里带插值的句子若在改造前把**值**
 * 包在独立元素里（`font-mono` 时间戳、`font-semibold` 计数），就不能把整句写成一条带 `{x}` 的词条
 * ——那样值会失去自己的样式（修订记录第 39 行，批次 2 收尾打磨统一处理）。做法是拆成
 * `…Prefix` / `…Suffix`（一句里多个值时按位置依次拆），由 JSX 把值放回自己的 `<span>`。
 * 拆开后整句不再含 `{count}`，因此 **en 侧的后缀用复数条目 `{ one, other }` 承载单复数**，
 * 调用方仍要传 `count`。
 * ⚠ `locales.test.ts` 拒绝空词条，所以两侧的每个键都必须有真实内容：中英句式不对称时
 * （中文有尾随词、英文天然以值结尾）en 用一个句末句点承载尾随键——既不发明信息，也不动中文文案。
 */
export const conversations = {
  // ── 跨文件复用 ──
  'conversations.shared.noSubject': '(无主题)',
  'conversations.shared.wechatCustomer': '微信客户',

  // =====================================================================
  // 会话列表（pages/Conversations.tsx）
  // =====================================================================
  'conversations.list.title': '会话管理',
  'conversations.list.subtitle': '管理所有客户支持会话',
  'conversations.list.refresh': '刷新',
  'conversations.list.trash': '垃圾箱',
  'conversations.list.loading': '加载中...',
  'conversations.list.loadFailed': '加载失败：{message}',

  // ── 快捷来源标签与筛选栏 ──
  'conversations.list.all': '全部',
  'conversations.list.search': '搜索',
  'conversations.list.searchPlaceholder': '搜索主题、客户邮箱...',
  'conversations.list.clearSearch': '清除搜索',
  'conversations.list.mailbox': '邮箱',
  'conversations.list.allMailboxes': '全部邮箱',
  'conversations.list.status': '状态',
  'conversations.list.allStatuses': '全部状态',
  'conversations.list.priority': '优先级',
  'conversations.list.allPriorities': '全部优先级',
  'conversations.list.source': '来源',
  'conversations.list.allSources': '全部来源',
  'conversations.list.follow': '关注',
  'conversations.list.allConversations': '全部会话',
  'conversations.list.onlyFollowed': '仅我关注的',
  'conversations.list.filters': '筛选',
  'conversations.list.resultCountPrefix': '找到 ',
  'conversations.list.resultCountSuffix': ' 条匹配结果',

  // ── 空状态 ──
  'conversations.list.emptySourceTitle': '暂无{source}会话',
  'conversations.list.emptySourceHint': '客户通过{source}发送的消息将显示在这里',
  'conversations.list.emptySearch': '未找到匹配的会话',
  'conversations.list.emptySearchHint': '尝试调整搜索关键词或筛选条件',
  'conversations.list.empty': '暂无会话数据',
  'conversations.list.emptyHint': '当有新的客户会话时，将显示在这里',

  // ── 列表项徽标 ──
  'conversations.list.new': '新',
  'conversations.list.newMessage': '新消息',
  'conversations.list.hasNewMessage': '有新消息',

  // ── 表头 ──
  'conversations.list.columnType': '类型',
  'conversations.list.columnSubject': '主题',
  'conversations.list.columnCustomer': '客户',
  'conversations.list.columnAssignee': '负责人',
  'conversations.list.columnAttachments': '附件',
  'conversations.list.columnLastReply': '最后回复',
  'conversations.list.columnCreatedAt': '创建时间',

  // ── 分页 ──
  // 分页摘要：改造前「共 N 条，」在 `hidden sm:inline` 里、「第 P / Q 页」在它外面
  // （移动端本来就能看到页码），拆键时按原样还原这层包裹关系。
  'conversations.list.pageSummaryPrefix': '共 ',
  'conversations.list.pageSummaryAfterTotal': ' 条，',
  'conversations.list.pageSummaryBeforePage': '第 ',
  'conversations.list.pageSummaryBetween': ' / ',
  'conversations.list.pageSummarySuffix': ' 页',

  // ── 批量操作工具栏 ──
  'conversations.list.selectedCount': '已选中 {count} 个会话',
  'conversations.list.assignAssignee': '分配负责人',
  'conversations.list.updateStatus': '更新状态',
  'conversations.list.setPriority': '设置优先级',
  'conversations.list.addTags': '添加标签',
  'conversations.list.clearSelection': '取消选择',

  // ── 批量操作结果与错误 ──
  'conversations.list.errorSelectAssignee': '请选择负责人',
  'conversations.list.assignSuccess': '成功分配 {count} 个会话',
  'conversations.list.assignFailed': '分配失败',
  'conversations.list.statusUpdateSuccess': '成功更新 {count} 个会话状态',
  'conversations.list.statusUpdateFailed': '状态更新失败',
  'conversations.list.priorityUpdateSuccess': '成功更新 {count} 个会话优先级',
  'conversations.list.priorityUpdateFailed': '优先级更新失败',
  'conversations.list.deleteSuccess': '成功删除 {count} 个会话，可在垃圾箱中恢复',
  'conversations.list.deleteFailed': '删除失败',
  'conversations.list.deleteFailedAll': '删除失败，共 {count} 个',
  'conversations.list.deletePartialSuccess': '部分删除成功：成功 {success} 个，失败 {failed} 个，共 {total} 个',
  'conversations.list.withFailureDetails': '{message}（失败原因：{details}）',
  'conversations.list.restoreSuccess': '会话 #{id} 已恢复',
  'conversations.list.restoreFailed': '恢复失败',
  'conversations.list.hardDeleteSuccess': '会话 #{id} 已彻底删除',
  'conversations.list.hardDeleteFailed': '彻底删除失败',
  'conversations.list.clearTrashSuccess': '已清空垃圾箱，共彻底删除 {count} 个会话',
  'conversations.list.clearTrashFailed': '清空垃圾箱失败',
  'conversations.list.errorSelectTag': '请选择至少一个标签',
  'conversations.list.tagUpdateSuccess': '标签更新成功',
  'conversations.list.tagAddSuccess': '成功为 {count} 个会话添加标签',
  'conversations.list.tagUpdateFailed': '标签更新失败',

  // ── 批量弹窗 ──
  'conversations.list.assigning': '分配中...',
  'conversations.list.confirmAssign': '确认分配',
  'conversations.list.assignModalTitle': '分配负责人（{count} 个会话）',
  'conversations.list.assignModalHintPrefix': '将选中的 ',
  'conversations.list.assignModalHintSuffix': ' 个会话分配给：',
  'conversations.list.selectUser': '请选择用户',
  'conversations.list.updating': '更新中...',
  'conversations.list.confirmUpdate': '确认更新',
  'conversations.list.statusModalTitle': '更新状态（{count} 个会话）',
  'conversations.list.statusModalHintPrefix': '将选中的 ',
  'conversations.list.statusModalHintSuffix': ' 个会话状态更新为：',
  'conversations.list.priorityModalTitle': '设置优先级（{count} 个会话）',
  'conversations.list.priorityModalHintPrefix': '将选中的 ',
  'conversations.list.priorityModalHintSuffix': ' 个会话优先级设置为：',
  'conversations.list.tagModalTitleSingle': '管理标签（1 个会话）',
  'conversations.list.tagModalTitle': '添加标签（{count} 个会话）',
  'conversations.list.tagModalHintSingle': '当前会话已选中的标签会高亮显示，点击可添加或移除：',
  'conversations.list.tagModalHintPrefix': '为选中的 ',
  'conversations.list.tagModalHintSuffix': ' 个会话选择标签：',
  'conversations.list.noTags': '暂无标签，请先创建',
  'conversations.list.saving': '保存中...',
  'conversations.list.confirmAddTags': '确认添加',

  // ── 删除 / 垃圾箱确认 ──
  'conversations.list.bulkDeleteTitle': '批量删除会话',
  'conversations.list.bulkDeleteMessage':
    '确认删除选中的 {count} 个会话吗？删除后将移入垃圾箱，可在垃圾箱中恢复或彻底删除。',
  'conversations.list.confirmDelete': '确认删除',
  'conversations.list.trashTitle': '垃圾箱 - {mailbox}',
  'conversations.list.clearTrash': '清空垃圾箱',
  'conversations.list.trashEmpty': '垃圾箱为空',
  'conversations.list.trashItemMeta': '客户：{customer} · 负责人：{assignee}',
  'conversations.list.restore': '恢复',
  'conversations.list.hardDelete': '彻底删除',
  'conversations.list.trashHint':
    '提示：垃圾箱中的会话为已软删除的会话。可"恢复"还原至列表，或"彻底删除"永久移除（不可恢复）。',
  'conversations.list.clearTrashMessage':
    '确认彻底删除该邮箱下所有已软删除的会话吗？共 {count} 条，此操作不可恢复。',
  'conversations.list.confirmClearTrash': '确认清空',
  'conversations.list.hardDeleteTitle': '彻底删除会话',
  'conversations.list.hardDeleteMessage': '确认彻底删除会话 #{id} 吗？此操作不可恢复，删除后无法还原。',
  'conversations.list.confirmHardDelete': '确认彻底删除',

  // =====================================================================
  // 会话详情（pages/ConversationDetail.tsx）
  // =====================================================================
  'conversations.detail.loading': '正在加载会话详情...',
  'conversations.detail.notFound': '会话不存在',
  'conversations.detail.backToList': '返回会话列表',
  'conversations.detail.breadcrumbList': '会话列表',
  'conversations.detail.wechatServiceAccount': '微信服务号会话',
  'conversations.detail.customerAvatar': '客户头像',
  'conversations.detail.customerLabel': '客户：{name}',
  'conversations.detail.assigneeLabel': '负责人：{name}',

  // ── 头部操作按钮 ──
  'conversations.detail.actions': '操作',
  'conversations.detail.addNote': '添加备注',
  'conversations.detail.forward': '转发',
  'conversations.detail.clone': '克隆',
  'conversations.detail.print': '打印',
  'conversations.detail.forwardConversation': '转发会话',
  'conversations.detail.cloneConversation': '克隆会话',
  'conversations.detail.printConversation': '打印会话',

  // ── 会话记录区 ──
  'conversations.detail.threads': '会话记录',
  'conversations.detail.refresh': '刷新',
  'conversations.detail.jumpToLatest': '跳到最新消息',
  'conversations.detail.latestMessage': '最新消息',

  // ── 操作反馈 ──
  'conversations.detail.followed': '已关注该会话',
  'conversations.detail.unfollowed': '已取消关注',
  'conversations.detail.operationFailed': '操作失败',
  'conversations.detail.statusUpdated': '状态已更新为：{status}',
  'conversations.detail.statusUpdateFailed': '更新状态失败',
  'conversations.detail.priorityUpdated': '优先级已更新为：{priority}',
  'conversations.detail.priorityUpdateFailed': '更新优先级失败',
  'conversations.detail.unassigned': '已取消分配',
  'conversations.detail.assignedTo': '已分配给 {name}',
  'conversations.detail.assignFailed': '分配失败',
  'conversations.detail.replyFailed': '回复失败',
  'conversations.detail.emptyReply': '请输入回复内容或添加附件',
  'conversations.detail.draftSaved': '草稿已保存',
  'conversations.detail.draftSaveFailed': '保存草稿失败',
  'conversations.detail.addNoteFailed': '添加备注失败',
  'conversations.detail.cloneConfirm': '确定要克隆此会话吗？将创建一个相同主题的新会话。',
  'conversations.detail.cloneSuccess': '会话克隆成功',
  'conversations.detail.cloneFailed': '克隆失败',

  // ── 渠道回复发送结果 ──
  'conversations.detail.threadCreateFailedWithReason': '消息已送达客户，但会话线程记录创建失败：{message}',
  'conversations.detail.threadCreateFailed':
    '消息已送达客户，但会话线程记录创建失败（数据可能不一致），请稍后刷新页面确认',
  'conversations.detail.partialSend': '部分消息已发送：{message}',
  'conversations.detail.wechatSendFailed': '微信回复发送失败',
  'conversations.detail.wechatSent': '微信回复已发送',
  'conversations.detail.wechatFailed': '微信回复失败',
  'conversations.detail.whatsAppSendFailed': 'WhatsApp 回复发送失败',
  'conversations.detail.whatsAppSent': 'WhatsApp 回复已发送',
  'conversations.detail.whatsAppFailed': 'WhatsApp 回复失败',
  'conversations.detail.feishuSendFailed': '飞书回复发送失败',
  'conversations.detail.feishuSent': '飞书回复已发送',
  'conversations.detail.feishuFailed': '飞书回复失败',
  'conversations.detail.dingtalkSendFailed': '钉钉回复发送失败',
  'conversations.detail.dingtalkSent': '钉钉回复已发送',
  'conversations.detail.dingtalkFailed': '钉钉回复失败',

  // ── 客服消息窗口剩余时间（formatRemaining / dingDingWindowRemainingText） ──
  'conversations.detail.expired': '已过期',
  'conversations.detail.remainingHoursMinutes': '{hours} 小时 {minutes} 分钟',
  'conversations.detail.remainingHours': '{hours} 小时',
  'conversations.detail.remainingMinutes': '{minutes} 分钟',
  'conversations.detail.remainingSeconds': '{seconds} 秒',

  // ── 窗口状态通用片段 ──
  'conversations.detail.windowStatusOk': '正常',
  'conversations.detail.windowStatusWarning': '即将过期',
  'conversations.detail.remainingLabel': '剩余 {remaining}',
  'conversations.detail.lastInteraction': '最后互动：',
  'conversations.detail.lastInteractionShort': '互动：',
  'conversations.detail.lastInteractionTitle': '最后互动：{time}',
  'conversations.detail.goToTemplate': '前往模板消息',
  'conversations.detail.windowExpiresAtTitle': '窗口将于 {time} 过期',
  'conversations.detail.windowExpiredAtPrefix': '已于 ',
  'conversations.detail.windowExpiredAtSuffix': ' 过期',
  'conversations.detail.windowWillExpireAtPrefix': '将于 ',
  'conversations.detail.windowWillExpireAtSuffix': ' 过期',

  // ── 微信 48 小时窗口 ──
  'conversations.detail.wechatWindowOk': '客服消息窗口有效，剩余 {remaining}',
  'conversations.detail.wechatWindowWarning': '客服消息窗口即将过期，剩余 {remaining}',
  'conversations.detail.wechatWindowExpired': '客服消息窗口已过期，客户消息可能无法送达，建议使用模板消息',
  'conversations.detail.wechatWindowExpiredAdvice': '客服消息可能无法送达，建议发送模板消息',
  'conversations.detail.wechatWindowUnknown': '暂无客户互动记录，等待客户发信后即可回复',
  'conversations.detail.wechatWindowSrText': '客服消息 48 小时窗口状态：{status}，已使用 {percent}%，{remaining}',
  'conversations.detail.wechatWindowProgressAria': '48 小时客服消息窗口,剩余 {remaining}',
  'conversations.detail.wechatUsedRemaining': '已用 {used}h · 剩 {remaining}',
  'conversations.detail.wechatTemplateLinkAria': '前往微信设置页发送模板消息',
  'conversations.detail.wechatTemplateLinkTitle': '在新页面打开微信设置 → 模板消息',

  // ── WhatsApp 24 小时窗口 ──
  'conversations.detail.whatsAppWindowOk': '✅ 24 小时客户服务窗口',
  'conversations.detail.whatsAppActive': '正常进行中',
  'conversations.detail.whatsAppWindowWarning': '⚠️ 24 小时窗口即将过期',
  'conversations.detail.whatsAppReplySoon': '请尽快回复',
  'conversations.detail.whatsAppWindowExpired': '🔴 24 小时窗口已过期',
  'conversations.detail.whatsAppTemplateOnly': '仅支持模板消息',
  'conversations.detail.whatsAppAwaitingCustomer': '📭 等待客户首次互动',
  'conversations.detail.whatsAppOkDesc': '窗口有效，可自由发送文本与媒体消息。剩余时间：',
  'conversations.detail.whatsAppWarningDesc': '窗口将到期，建议尽快回复客户。剩余时间：',
  'conversations.detail.whatsAppExpiredDesc':
    '客户超过 24 小时未互动，自由消息无法送达。请发送经审批的模板消息重新触达。',
  'conversations.detail.whatsAppUnknownDesc': '暂无客户互动记录，等待客户发信后将自动开启 24 小时服务窗口。',
  'conversations.detail.whatsAppWindowSrText': '24 小时客户服务窗口状态：{status}，已使用 {percent}%，{remaining}',
  'conversations.detail.whatsAppFreeMessageBlocked': '自由消息无法送达，建议发送模板消息',
  'conversations.detail.whatsAppWindowProgressAria': '24 小时客户服务窗口,剩余 {remaining}',
  'conversations.detail.whatsAppRulerStart': '0h · 开始',
  'conversations.detail.whatsAppRulerEnd': '24h · 到期',
  'conversations.detail.whatsAppStopped': '⏹ 已过期',
  'conversations.detail.whatsAppConsumed': '已消耗 {percent}% · 剩 {remaining}',
  'conversations.detail.whatsAppWillExpireAtPrefix': '将于 ',
  'conversations.detail.whatsAppWillExpireAtSuffix': ' 到期',
  'conversations.detail.whatsAppTemplateLinkAria': '前往 WhatsApp 设置页发送模板消息',
  'conversations.detail.whatsAppTemplateLinkTitle': '打开 WhatsApp 设置 → 模板消息',

  // ── 转发会话弹窗 ──
  'conversations.detail.forwarding': '转发中...',
  'conversations.detail.confirmForward': '确认转发',
  'conversations.detail.forwardHintPrefix': '将会话 ',
  'conversations.detail.forwardHintSuffix': ' 的全部内容转发到指定邮箱。',
  'conversations.detail.forwardRecipient': '收件人邮箱 *',
  'conversations.detail.forwardNoteLabel': '附加说明（可选）',
  'conversations.detail.forwardNotePlaceholder': '添加一些说明文字...',
  'conversations.detail.forwardRecipientRequired': '请填写收件人邮箱',
  'conversations.detail.forwardRecipientInvalid': '收件人邮箱格式不正确',
  'conversations.detail.forwardSuccess': '已成功转发到 {email}',
  'conversations.detail.forwardFailed': '转发失败',
  'conversations.detail.forwardIncludes': '转发内容包含：',
  'conversations.detail.forwardIncludesMeta': '· 会话主题、客户邮箱、负责人、创建时间',
  'conversations.detail.forwardIncludesMessages': '· 全部 {count} 条消息记录（含客户消息和客服回复，不含内部备注）',
  'conversations.detail.forwardIncludesSender': '· 发件邮箱：{email}',
  'conversations.detail.defaultMailbox': '默认邮箱',

  // ── 转发邮件正文（外发邮件，当前按界面语言产出） ──
  'conversations.detail.forwardBodyGreeting': '您好，',
  'conversations.detail.forwardBodyHeading': '— 转发的会话 —',
  'conversations.detail.forwardBodySubject': '主题：',
  'conversations.detail.forwardBodyCustomer': '客户：',
  'conversations.detail.forwardBodyAssignee': '负责人：',
  'conversations.detail.forwardBodyCreatedAt': '创建时间：',
  'conversations.detail.forwardSubjectPrefix': '【转发】{subject}',
  'conversations.detail.threadTypeCustomer': '客户消息',
  'conversations.detail.threadTypeReply': '客服回复',
  'conversations.detail.threadTypeNote': '内部备注',
  'conversations.detail.threadTypeSystem': '系统消息',

  // =====================================================================
  // 回复框公共外壳（components/ReplyBox.tsx 与 4 个渠道回复框共用）
  // =====================================================================
  'conversations.reply.noteTitle': '内部备注',
  'conversations.reply.replyTitle': '回复客户',
  'conversations.reply.addNote': '添加内部备注',
  'conversations.reply.readOnlyHint': '您当前为只读权限，无法发送回复',
  'conversations.reply.from': '发件人: {email}',
  'conversations.reply.quickSend': '快速发送',

  // ── 富文本工具栏 ──
  'conversations.reply.bold': '粗体',
  'conversations.reply.boldPlaceholder': '粗体文本',
  'conversations.reply.italic': '斜体',
  'conversations.reply.italicPlaceholder': '斜体文本',
  'conversations.reply.underline': '下划线',
  'conversations.reply.underlinePlaceholder': '下划线文本',
  'conversations.reply.bulletList': '无序列表',
  'conversations.reply.numberList': '有序列表',
  'conversations.reply.link': '链接',
  'conversations.reply.linkPlaceholder': '链接文本',
  'conversations.reply.heading': '标题',
  'conversations.reply.headingPlaceholder': '标题',
  'conversations.reply.quote': '引用',
  'conversations.reply.quotePlaceholder': '引用文本',
  'conversations.reply.code': '代码',
  'conversations.reply.codePlaceholder': '代码',
  'conversations.reply.templates': '快捷回复模板',
  'conversations.reply.htmlSupported': '支持 HTML 格式',

  // ── 抄送 / 密送 ──
  'conversations.reply.cc': '抄送 (CC)',
  'conversations.reply.bcc': '密送 (BCC)',
  'conversations.reply.multipleEmails': '多个邮箱用逗号分隔',

  // ── 编辑区 ──
  'conversations.reply.notePlaceholder': '输入内部备注内容...（仅客服可见）',
  'conversations.reply.replyPlaceholder': '输入回复内容...（支持 HTML 富文本）',
  'conversations.reply.noteAria': '内部备注内容',
  'conversations.reply.replyAria': '回复内容',
  'conversations.reply.remove': '移除',
  'conversations.reply.removeAttachmentAria': '移除附件 {name}',
  'conversations.reply.removeImageAria': '移除图片附件 {name}',
  'conversations.reply.addAttachment': '添加附件',
  'conversations.reply.attachmentHint': '支持图片、PDF、文档等',

  // ── 附件校验 ──
  'conversations.reply.fileTooLarge25': '{name} 超过 25MB 大小限制',
  'conversations.reply.fileTypeNotAllowed': '{name} 的文件类型不允许',
  'conversations.reply.uploadFailed': '上传失败',

  // ── 底部操作 ──
  'conversations.reply.savingDraft': '保存中...',
  'conversations.reply.saveDraft': '保存草稿',
  'conversations.reply.draftSaveFailed': '保存草稿失败',
  'conversations.reply.sending': '发送中...',
  'conversations.reply.saveNote': '保存备注',
  'conversations.reply.sendReply': '发送回复',
  'conversations.reply.undoCountdownPrefix': '邮件将在 ',
  'conversations.reply.undoCountdownSuffix': ' 秒后发送',
  'conversations.reply.undo': '撤销发送',

  // ── 快捷回复模板下拉 ──
  'conversations.reply.templateSearch': '搜索模板...',
  'conversations.reply.templatesLoading': '加载中...',
  'conversations.reply.noTemplates': '暂无模板',
  'conversations.reply.uncategorized': '未分类',

  // ── 渠道回复框共用外壳 ──
  'conversations.reply.dropToUpload': '松开鼠标以上传附件',
  'conversations.reply.quickReplies': '常用快捷回复',
  'conversations.reply.quickRepliesHint': '按数字键 1-{count} 快速插入',
  'conversations.reply.commonEmojis': '常用表情',
  'conversations.reply.insertEmoji': '插入表情',
  'conversations.reply.insertEmojiAria': '插入表情 {emoji}',
  'conversations.reply.quick': '快捷',
  'conversations.reply.attachments': '附件',
  'conversations.reply.uploading': '上传中',
  'conversations.reply.emoji': '表情',
  'conversations.reply.shortcuts': '键盘快捷键',
  'conversations.reply.shortcutsAria': '查看键盘快捷键',
  'conversations.reply.shortcutSend': '发送消息',
  'conversations.reply.shortcutQuickReplies': '快捷回复（1-{count}）',
  'conversations.reply.shortcutQuickRepliesPlain': '快捷回复',
  'conversations.reply.shortcutClosePanel': '关闭面板',
  'conversations.reply.shortcutPasteImage': '粘贴图片上传',
  'conversations.reply.sendingEllipsis': '发送中…',
  'conversations.reply.send': '发送',
  'conversations.reply.charCountTitle': '字符数 / 字符上限',

  // ── 常用快捷回复短语（4 个渠道回复框共用同一份） ──
  'conversations.reply.quickReply1': '您好，请问有什么可以帮您？',
  'conversations.reply.quickReply2': '稍等，正在为您查询…',
  'conversations.reply.quickReply3': '已收到您的消息，我们会尽快处理。',
  'conversations.reply.quickReply4': '感谢您的耐心等待。',
  'conversations.reply.quickReply5': '问题已解决，还有其他需要帮助的吗？',
  'conversations.reply.quickReply6': '已为您转接相关专业人员，请稍候。',

  // =====================================================================
  // 右侧边栏（components/ConversationSidebar.tsx）
  // =====================================================================
  'conversations.sidebar.unknownMailbox': '未知邮箱',
  'conversations.sidebar.actions': '操作',
  'conversations.sidebar.follow': '关注会话',
  'conversations.sidebar.unfollow': '取消关注',
  'conversations.sidebar.statusAria': '会话状态',
  'conversations.sidebar.statusOption': '状态：{status}',
  'conversations.sidebar.priorityAria': '会话优先级',
  'conversations.sidebar.priorityOption': '优先级：{priority}',
  'conversations.sidebar.assignedTo': '分配给：{name}',
  'conversations.sidebar.assignPlaceholder': '分配给...',
  'conversations.sidebar.customerInfo': '客户信息',
  'conversations.sidebar.wechatChannel': '微信渠道',
  'conversations.sidebar.copied': '已复制到剪贴板',
  'conversations.sidebar.copyFailed': '复制失败，请手动复制',
  'conversations.sidebar.copyOpenIdTitle': '点击复制完整 OpenID\n{id}',
  'conversations.sidebar.copyOpenIdAriaSuccess': 'OpenID 已成功复制到剪贴板',
  'conversations.sidebar.copyOpenIdAriaFailed': '复制失败，请手动复制 OpenID',
  'conversations.sidebar.copyOpenIdAria': '复制 OpenID，完整值为 {id}',
  'conversations.sidebar.joinedAt': '加入于 {date}',
  'conversations.sidebar.conversationCount': '历史会话 {count} 条',
  'conversations.sidebar.unknownCustomer': '未知客户',
  'conversations.sidebar.unregisteredCustomer': '未注册客户',
  'conversations.sidebar.conversationInfo': '会话信息',
  'conversations.sidebar.conversationId': '会话 ID',
  'conversations.sidebar.status': '状态',
  'conversations.sidebar.priority': '优先级',
  'conversations.sidebar.source': '来源',
  'conversations.sidebar.mailbox': '邮箱',
  'conversations.sidebar.assignee': '负责人',
  'conversations.sidebar.createdAt': '创建时间',
  'conversations.sidebar.lastReply': '最后回复',
  'conversations.sidebar.tags': '标签',
  'conversations.sidebar.emailSatisfaction': '邮件满意度评价',
  'conversations.sidebar.ratedAt': '评价于 {date}',
  'conversations.sidebar.chatRating': '在线聊天客户评分',
  'conversations.sidebar.chatToConversation': '聊天转工单',
  'conversations.sidebar.sessionUpdatedAt': '会话更新 {date}',
  'conversations.sidebar.ratingStars': '{rating} 星',
  'conversations.sidebar.ratingVerySatisfied': '非常满意',
  'conversations.sidebar.ratingSatisfied': '满意',
  'conversations.sidebar.ratingNeutral': '一般',
  'conversations.sidebar.ratingDissatisfied': '不满意',
  'conversations.sidebar.ratingVeryDissatisfied': '非常不满意',

  // =====================================================================
  // 渠道回复框（components/{WeChat,WhatsApp,Feishu,DingDing}ReplyBox.tsx）
  // =====================================================================
  'conversations.channel.goToTemplate': '前往模板消息',
  'conversations.channel.textAndAttachments': '支持发送文本和附件',
  'conversations.channel.fileTooLarge': '{name} 超过 {size} 大小限制',
  'conversations.channel.remainingSuffix': '（剩余 {remaining}）',

  // ── 微信 ──
  'conversations.channel.wechat.title': '微信客服回复',
  'conversations.channel.wechat.windowExpired': '客服消息窗口已过期',
  'conversations.channel.wechat.windowExpiredDesc':
    '客户超过 48 小时未互动，客服消息无法送达。请通过模板消息主动触达客户。',
  'conversations.channel.wechat.windowWarning': '客服消息窗口即将过期，请尽快回复客户。窗口过期后需使用模板消息。',
  'conversations.channel.wechat.templateLinkAria': '前往微信设置页发送模板消息',
  'conversations.channel.wechat.templateLinkTitle': '打开微信设置 → 模板消息',
  'conversations.channel.wechat.uploadTitle': '上传图片/语音/视频（图片≤2MB、语音≤2MB、视频≤10MB）',
  'conversations.channel.wechat.uploadAria': '上传图片、语音或视频附件',
  'conversations.channel.wechat.unsupportedType': '{name} 类型不支持，微信仅支持图片/语音/视频',
  'conversations.channel.wechat.placeholderExpired': '客服消息窗口已过期，无法发送客服消息',
  'conversations.channel.wechat.placeholder': '输入微信回复内容…（Ctrl+Enter 发送）',
  'conversations.channel.wechat.textareaAria': '微信客服回复内容',
  'conversations.channel.wechat.byteCountTitle': 'UTF-8 字节占用 / 微信 600 字节上限',
  'conversations.channel.wechat.footerHint':
    '客服消息仅在客户最近互动 48 小时内可发送；附件将作为独立消息逐条发送。',
  'conversations.channel.wechat.footerHintShort': '48 小时窗口内可发送',
  'conversations.channel.wechat.sendAria': '发送微信回复',

  // ── WhatsApp ──
  'conversations.channel.whatsApp.title': 'WhatsApp 回复',
  'conversations.channel.whatsApp.subtitle': 'Cloud API · 24h 服务窗口',
  'conversations.channel.whatsApp.dropHint': '支持图片、音频、视频、文档、WebP 贴纸',
  'conversations.channel.whatsApp.windowExpired': '24 小时客户服务窗口已过期',
  'conversations.channel.whatsApp.cannotSendFree': '无法发自由消息',
  'conversations.channel.whatsApp.windowExpiredDesc':
    '客户超过 24 小时未互动，WhatsApp 平台将拒绝自由消息送达。请通过模板消息主动触达客户。',
  'conversations.channel.whatsApp.templateLinkAria': '前往 WhatsApp 设置页发送模板消息',
  'conversations.channel.whatsApp.templateLinkTitle': '打开 WhatsApp 设置 → 模板消息',
  'conversations.channel.whatsApp.windowWarning': '⚠ 24 小时窗口即将过期',
  'conversations.channel.whatsApp.windowWarningDesc': '请尽快回复客户，窗口过期后仅能通过模板消息触达。',
  'conversations.channel.whatsApp.quickRepliesTitle': '常用快捷回复（可按数字键快速插入）',
  'conversations.channel.whatsApp.quickRepliesHint': '数字键 1-{count}',
  'conversations.channel.whatsApp.uploadTitle':
    '上传图片/音频/视频/文档（图片≤5MB、音频≤16MB、视频≤16MB、文档≤100MB、贴纸≤500KB）',
  'conversations.channel.whatsApp.uploadAria': '上传图片、音频、视频或文档附件',
  'conversations.channel.whatsApp.unsupportedType':
    '{name} 类型不支持，WhatsApp 仅支持图片/音频/视频/文档/贴纸',
  'conversations.channel.whatsApp.pendingAttachments': '待发送附件 · {count} 项',
  'conversations.channel.whatsApp.removeNamed': '移除 {name}',
  'conversations.channel.whatsApp.placeholderExpired': '🔒 24 小时窗口已过期，无法发送自由消息。请前往模板消息。',
  'conversations.channel.whatsApp.placeholder': '💬 输入 WhatsApp 回复内容…（Ctrl + Enter 发送）',
  'conversations.channel.whatsApp.textareaAria': 'WhatsApp 回复内容',
  'conversations.channel.whatsApp.charCountTitle': '字符数 / 字符上限（4096）',
  'conversations.channel.whatsApp.footerHint':
    '24 小时客户服务窗口内可自由回复；附件将按类型独立发送，可能产生多条消息。',
  'conversations.channel.whatsApp.footerHintShort': '24h 窗口内可回复；附件将独立发送',
  'conversations.channel.whatsApp.sendAria': '发送 WhatsApp 回复',
  'conversations.channel.whatsApp.sendNow': '立即发送',
  'conversations.channel.whatsApp.sendReady': '准备发送',

  // ── 飞书 ──
  'conversations.channel.feishu.title': '飞书客服回复',
  'conversations.channel.feishu.uploadTitle': '上传图片/语音/视频（≤25MB）',
  'conversations.channel.feishu.uploadAria': '上传图片、语音或视频附件',
  'conversations.channel.feishu.unsupportedType': '{name} 类型不支持，飞书仅支持图片/语音/视频',
  'conversations.channel.feishu.fileTooLarge': '{name} 超过 25MB 大小限制',
  'conversations.channel.feishu.footerHint': '飞书机器人可在用户与会话建立后发送消息；附件将作为独立消息发送。',
  'conversations.channel.feishu.placeholder': '输入飞书回复内容…（Ctrl+Enter 发送）',
  'conversations.channel.feishu.textareaAria': '飞书客服回复内容',
  'conversations.channel.feishu.sendAria': '发送飞书回复',

  // ── 钉钉 ──
  'conversations.channel.dingtalk.title': '钉钉客服回复',
  'conversations.channel.dingtalk.windowOk': 'sessionWebhook 可用',
  'conversations.channel.dingtalk.windowWarning': '即将过期',
  'conversations.channel.dingtalk.windowExpired': 'sessionWebhook 已过期，将通过批量发送接口回复',
  'conversations.channel.dingtalk.windowUnknown': 'sessionWebhook 状态未知',
  'conversations.channel.dingtalk.uploadTitle': '上传图片/语音/视频（≤25MB）',
  'conversations.channel.dingtalk.uploadAria': '上传图片、语音或视频附件',
  'conversations.channel.dingtalk.unsupportedType': '{name} 类型不支持，钉钉仅支持图片/语音/视频',
  'conversations.channel.dingtalk.fileTooLarge': '{name} 超过 25MB 大小限制',
  'conversations.channel.dingtalk.footerHint':
    '钉钉机器人通过 sessionWebhook 即时回复（72h 窗口）；窗口过期后通过批量发送接口回复。',
  'conversations.channel.dingtalk.placeholder': '输入钉钉回复内容…（Ctrl+Enter 发送）',
  'conversations.channel.dingtalk.textareaAria': '钉钉客服回复内容',
  'conversations.channel.dingtalk.sendAria': '发送钉钉回复',

  'conversations.channel.line.title': 'LINE 回复（reply 优先，过期自动转 push）',
  'conversations.channel.line.windowOk': '回复窗口内',
  'conversations.channel.line.windowExpiring': '窗口即将过期',
  'conversations.channel.line.windowExpired': '窗口已过期',
  'conversations.channel.line.windowUnknown': '无回复窗口',
  'conversations.channel.line.windowTitle': '客户最后一条消息在 {ttl} 秒内可直接回复：走 replyToken 免费且必达；过期后改用 push，会消耗月度配额',
  'conversations.channel.line.takeoverActive': '人工接管中：机器人不会自动回复',
  'conversations.channel.line.takeoverActiveWithRemaining': '人工接管中（剩余 {remaining}）：机器人不会自动回复',
  'conversations.channel.line.takeoverStart': '开始接管',
  'conversations.channel.line.takeoverRelease': '结束接管',
  'conversations.channel.line.takeoverFailed': '接管状态更新失败',
  'conversations.channel.line.unreachableHint': '该客户未添加机器人为好友（或已屏蔽），消息无法送达',
  'conversations.channel.line.fileTooLarge': '附件「{name}」超过 LINE 直链上限（{limit}）',
  'conversations.channel.line.uploadTitle': 'LINE 附件会转成本系统公网 HTTPS 签名直链发送（需配置 AttachmentPublicBaseUrl）',
  'conversations.channel.line.uploadAria': '上传 LINE 附件',
  'conversations.channel.line.splitInto': '将分 {count} 条发送',
  'conversations.channel.line.placeholder': '输入要发送给该 LINE 客户的回复…',
  'conversations.channel.line.textareaAria': 'LINE 回复内容',
  'conversations.channel.line.attachmentLinkHint': '{count} 个附件走签名直链（{nonNative} 个非原生图片格式会按文件发送）',
  'conversations.channel.line.tooManyMessages': '单次最多 5 条消息：当前预计 {count} 条，请减少内容或附件',
  'conversations.channel.line.footerHint': '5000 字符/条 · 图片直链 · 过期后转 push 耗配额',
  'conversations.channel.line.sendAria': '发送 LINE 回复',
  'conversations.channel.messenger.subtitle': '窗口内直接发送；窗口外会自动带 message_tag',
  'conversations.channel.messenger.placeholder': '输入要发送给客户的 Messenger 消息',
  'conversations.channel.messenger.placeholderExpired': '回复窗口已过期，发送时将带 message_tag',
  'conversations.channel.messenger.textareaAria': 'Messenger 回复内容',
  'conversations.channel.messenger.charCountTitle': '字符数（超过分段长度会自动拆分为多条）',
  'conversations.channel.messenger.chunkHint': '将分为 {count} 条发送（每条 ≤ {chars} 字符）',
  'conversations.channel.messenger.dropHint': '拖拽图片、音频、视频或文件到此处上传',
  'conversations.channel.messenger.unsupportedType': '不支持的文件类型：{name}',
  'conversations.channel.messenger.uploadTitle': '上传附件',
  'conversations.channel.messenger.uploadAria': '上传 Messenger 附件',
  'conversations.channel.messenger.pendingAttachments': '{count} 个待发送附件',
  'conversations.channel.messenger.removeNamed': '移除 {name}',
  'conversations.channel.messenger.windowExpired': '24 小时回复窗口已过期',
  'conversations.channel.messenger.windowExpiredDesc': 'Meta 要求窗口外的客服消息带 message_tag，发送时会自动附加',
  'conversations.channel.messenger.tagApplied': '将带 HUMAN_AGENT 标签',
  'conversations.channel.messenger.tagNone': '策略为不带标签，可能被拒绝',
  'conversations.channel.messenger.windowWarning': '回复窗口即将到期',
  'conversations.channel.messenger.windowWarningDesc': '到期后出站会带 message_tag，投递率会下降',
  'conversations.channel.messenger.quickRepliesTitle': '快捷回复',
  'conversations.channel.messenger.quickRepliesHint': '按 {count} 号键插入',
  'conversations.channel.messenger.footerHint': 'Ctrl+Enter 发送；附件会先经 Attachment Upload API 转成 attachment_id',
  'conversations.channel.messenger.footerHintShort': 'Ctrl+Enter 发送',
  'conversations.channel.messenger.sendAria': '发送 Messenger 消息',
  'conversations.channel.messenger.sendNow': '发送',
  'conversations.channel.messenger.sendReady': '准备发送',
  'conversations.channel.messenger.title': 'Messenger',

  'conversations.channel.telegram.title': 'Telegram',
  'conversations.channel.telegram.unreachableHint': '该用户已拉黑机器人，或已关闭与机器人的会话，消息无法送达。',
  'conversations.channel.telegram.clearUnreachable': '重新检测可达状态',
  'conversations.channel.telegram.parseModeAria': '消息格式化模式',
  'conversations.channel.telegram.parseMode.none': '纯文本',
  'conversations.channel.telegram.parseMode.HTML': 'HTML',
  'conversations.channel.telegram.parseMode.Markdown': 'Markdown',
  'conversations.channel.telegram.quoteLast': '引用上一条消息',
  'conversations.channel.telegram.uploadTitle': '添加图片、视频、文档或音频文件',
  'conversations.channel.telegram.uploadAria': '上传附件',
  'conversations.channel.telegram.splitInto': '将拆分为 {count} 条消息',
  'conversations.channel.telegram.placeholder': '输入回复内容…',
  'conversations.channel.telegram.placeholderCaption': '为附件补充说明文字…',
  'conversations.channel.telegram.textareaAria': 'Telegram 回复输入框',
  'conversations.channel.telegram.largeFileHint': '机器人自身最多只能下载 20MB 的文件',
  'conversations.channel.telegram.footerHint': 'Telegram 上传上限 50MB；超长文本会自动拆分为多条发送。',
  'conversations.channel.telegram.sendAria': '发送回复',
  'conversations.channel.telegram.fileTooLarge': '{name} 超过 {limit}',
  'conversations.channel.telegram.unsupportedType': '{name} 不是 Telegram 支持的文件类型',
  'conversations.channel.iMessage.title': 'Apple 信息（iMessage）',
  'conversations.channel.iMessage.subtitle': '回复经本机 iMessage 桥接服务发出',
  'conversations.channel.iMessage.ambSessionNotice': '商务信息遵循 Apple 的回复窗口，只能在活跃会话内发送。',
  'conversations.channel.iMessage.sessionIdle': '会话已闲置，客户可能看不到新的回复。',
  'conversations.channel.iMessage.placeholder': '输入回复内容…',
  'conversations.channel.iMessage.textareaAria': 'iMessage 回复输入框',
  'conversations.channel.iMessage.removeAttachment': '移除附件',
  'conversations.channel.iMessage.uploadAria': '上传附件',
  'conversations.channel.iMessage.uploadTitle': '添加图片、视频或文档附件（单个最大 100MB）',
  'conversations.channel.iMessage.quickRepliesTitle': '插入快捷回复',
  'conversations.channel.iMessage.sendNow': '发送',
  'conversations.channel.iMessage.dropHint': '松开鼠标即可添加附件',
  'conversations.channel.iMessage.footerHint': '能否送达取决于桥接服务在线，以及会话是否仍处于活跃状态。',
  'conversations.channel.iMessage.unsupportedType': '该文件类型无法通过 iMessage 发送',
  'conversations.channel.iMessage.tooManyAttachments': '单条消息的附件数量超出上限',
  'conversations.channel.iMessage.uploadFailed': '附件上传失败',


  'conversations.detail.messengerSendFailed': 'Messenger 发送失败',
  'conversations.detail.messengerSent': 'Messenger 消息已发送',
  'conversations.detail.messengerSentNoThread': '消息已发出，但会话记录保存失败，请核对后再重发',
  'conversations.detail.messengerFailed': 'Messenger 消息发送失败',
  'conversations.detail.telegramSendFailed': 'Telegram 发送失败',
  'conversations.detail.telegramSent': 'Telegram 消息已发送',
  'conversations.detail.telegramFailed': 'Telegram 消息发送失败',
  'conversations.detail.telegramUnreachableCleared': '已清除不可达标记，可继续向该客户发送',
  'conversations.detail.lineSendFailed': 'LINE 发送失败',
  'conversations.detail.lineSent': 'LINE 消息已发送',
  'conversations.detail.lineFailed': 'LINE 消息发送失败',
  'conversations.detail.lineTakeoverOn': '已开启人工接管：接管窗口内 LINE 机器人不再自动回复',
  'conversations.detail.lineTakeoverOff': '已结束人工接管：LINE 机器人恢复自动回复',
  'conversations.detail.iMessageSendFailed': 'iMessage 发送失败',
  'conversations.detail.iMessageSent': 'iMessage 消息已发送',
  'conversations.detail.iMessageFailed': 'iMessage 消息发送失败',
} satisfies Entries;
