# LINE — 前端共享登记点（请由 lead 应用；line-builder 不改共享文件）

> **状态（2026-11-23）**：下列登记点已全部应用到工作树（`Line.tsx`、`LineReplyBox.tsx`、路由 `/line`、`lineApi` 与 locale 键均已存在），本文改为交接记录存档，无需再执行。

`frontend/src/pages/Line.tsx` 与 `frontend/src/components/LineReplyBox.tsx` 按「只写新文件」约束完成，
以下登记点是它们生效所必需的共享改动。行号取自本轮工作树（2026-11-16），落地时以锚点文本为准。

`lineApi` 与 LINE 类型别名**已经**由 fe-wiring 加好（无需再改）：
`frontend/src/api/modules/system.ts:331-357`、`frontend/src/api/types/system.ts:744-894`。

---

## 1. 会话页接入 LINE 回复框（关键缺口）

`frontend/src/pages/ConversationDetail.tsx` 的回复框三元链目前止于钉钉（`sourceType === 10`），
在 `) : (`（约 :1594）之前没有 LINE 分支 ⇒ **LINEReplyBox 永远不会被渲染**，
LINE 会话只能用通用 `ReplyBox`，拿不到 5000 上限 / 分片计数 / replyToken 窗口 / 接管提示。

### 1.1 锚点 A — 第 4 行 import 补 `lineApi`
```tsx
import { conversationApi, profileApi, mailApi, satisfactionApi, weChatApi, whatsAppApi, feishuApi, dingdingApi, lineApi, chatApi } from '../api';
```

### 1.2 锚点 B — 组件 import（第 20 行 `import DingDingReplyBox ...` 之后）
```tsx
import LineReplyBox from '../components/LineReplyBox';
```

### 1.3 锚点 C — 状态与 replyToken 窗口（第 244 行 `const [dingDingReplyKey, setDingDingReplyKey] = useState(0);` 之后）
LINE 的 replyToken 不暴露在任何 DTO 上，只能从客户线程的创建时间推算（后端把它写在
`ConversationThreads.Headers` 的 `replyToken` / `lineReplyTokenCreatedAtUtc` 里，
`LineService.TryGetFreshReplyTokenAsync` 用 `Created_at + LineOptions.ReplyTokenTtlSeconds` 判定）。
这里复用 `now`（:162）与 `threads`（:77），不再新增 interval；TTL 取后端默认 25 秒。
```tsx
  const isLineConversation = convSourceReady && conversation?.sourceType === 13; // SourceType.Line
  const [lineReplyContent, setLineReplyContent] = useState('');
  const [lineReplyKey, setLineReplyKey] = useState(0);
  const [lineUnreachable, setLineUnreachable] = useState(false);
  const [lineHandoverActive, setLineHandoverActive] = useState(false);

  // LINE replyToken 是一次性凭据（默认 25s）：过期后后端自动转 push 并消耗配额
  const lineReplyWindow = useMemo(() => {
    if (!isLineConversation) return { status: 'unknown' as const, remainingText: undefined as string | undefined };
    const customerThreads = threads
      .filter((th) => th.type === ThreadType.Customer)
      .sort((a, b) => new Date(b.createdAt).getTime() - new Date(a.createdAt).getTime());
    const latest = customerThreads[0];
    if (!latest) return { status: 'unknown' as const, remainingText: undefined };
    const ttlSeconds = 25;
    const remaining = Math.floor(new Date(latest.createdAt).getTime() + ttlSeconds * 1000 - now);
    if (remaining <= 0) return { status: 'expired' as const, remainingText: undefined };
    const seconds = Math.floor(remaining / 1000);
    return {
      status: seconds <= 5 ? ('expiring' as const) : ('ok' as const),
      remainingText: `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`,
    };
  }, [isLineConversation, threads, now]);
```

### 1.4 锚点 D — 提交与接管 handler（第 665 行 `handleDingDingReply` 结束 `};` 之后）
```tsx
  const handleLineReply = async (attachmentIds?: number[]) => {
    if (!conversation) return;
    const content = lineReplyContent.trim();
    if (!content && (!attachmentIds || attachmentIds.length === 0)) {
      toast({ type: 'error', message: t('conversations.reply.textAndAttachments') });
      return;
    }
    try {
      const result = await lineApi.reply({
        conversationId: conversation.id,
        content: content || undefined,
        attachmentIds: attachmentIds && attachmentIds.length > 0 ? attachmentIds : undefined,
        preferReplyToken: lineReplyWindow.status !== 'expired',
      });
      if (!result.sent) {
        toast({ type: 'error', message: result.errorMessage || t('conversations.channel.line.takeoverFailed') });
        if (result.unreachable) setLineUnreachable(true);
        return;
      }
      if (result.mediaErrors?.length) {
        toast({ type: 'warning', message: result.mediaErrors.join('；') });
      }
      setLineReplyContent('');
      setLineReplyKey((key) => key + 1);
      await conversationApi.get(conversation.id);
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.threads(conversation.id) });
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.detail(conversation.id) });
    } catch (error) {
      console.error('Failed to send LINE reply:', error);
      toast({ type: 'error', message: t('common.errors.networkError') });
    }
  };

  const handleLineTakeover = async (minutes?: number) => {
    if (!conversation) return false;
    try {
      const ok = await lineApi.takeover({ conversationId: conversation.id, minutes });
      if (ok) {
        setLineHandoverActive(true);
        await conversationApi.get(conversation.id);
      }
      return ok;
    } catch (error) {
      console.error('Failed to start LINE human takeover:', error);
      return false;
    }
  };

  const handleLineReleaseTakeover = async () => {
    if (!conversation) return false;
    try {
      const ok = await lineApi.releaseTakeover(conversation.id);
      if (ok) {
        setLineHandoverActive(false);
        await conversationApi.get(conversation.id);
      }
      return ok;
    } catch (error) {
      console.error('Failed to release LINE human takeover:', error);
      return false;
    }
  };
```

### 1.5 锚点 E — 回复框分支（第 1594 行钉钉分支结束的 `)` 之后、`) : (`（第 1595 行）之前）
```tsx
              ) : isLineConversation ? (
                <LineReplyBox
                  key={`line-reply-${conversation.id}-${lineReplyKey}`}
                  value={lineReplyContent}
                  onChange={setLineReplyContent}
                  onSubmit={handleLineReply}
                  submitting={sendReplyMutation.isPending}
                  readOnly={!canSendReply || isArchived || isClosed}
                  maxLength={5000}
                  replyWindowStatus={lineReplyWindow.status}
                  replyWindowRemainingText={lineReplyWindow.remainingText}
                  humanHandoverActive={lineHandoverActive}
                  onTakeover={handleLineTakeover}
                  onReleaseTakeover={handleLineReleaseTakeover}
                  unreachable={lineUnreachable}
                />
```
> `maxLength={5000}` = `LineOptions.TextChunkChars` 上限（`UpdateLineSettingsRequest.TextChunkChars` 的 Range 上界）。
> 若要跟随自定义配置，从 `lineApi.getSettings()` 读 `textChunkChars` 传入。

---

## 2. i18n 键（另见 `docs/integration/line-locale-keys.md`）

`frontend/src/locales/zh-Hans/channels.ts` 与 `frontend/src/locales/en/channels.ts`：
缺 **78 个** `channels.line.*` 键（页面用到但未定义；另有 11 个已预置可复用）。
⚠️ 其中预置的 `channels.line.guide.step2..step5`（zh :663-666 / en :663-666）是**微信公众号**文案
（AppID/AppSecret、自定义 Token、EncodingAESKey、服务器配置→URL），LINE 无这些字段，需替换。
`frontend/src/locales/*/conversations.ts`：缺 **22 个** `conversations.channel.line.*` 键。
可粘贴的 zh-Hans + en 值见 `docs/integration/line-locale-keys.md`。

---

## 3. 其余登记点现状核对（已落地的无需重复）

| 文件 | 状态 | 证据 |
|------|------|------|
| `frontend/src/api/modules/system.ts` | ✅ 已有 `lineApi`（10 个方法） | :331-357 |
| `frontend/src/api/types/system.ts` | ✅ 已有 LINE 全套类型 | :749-894 |
| `frontend/src/utils/format.ts` | ✅ 已映射 `13 → 'format.sourceType.line'` | :359, :406, :426 |
| `frontend/src/components/SourceTypeIcon.tsx` | ✅ 已有 `sourceType === 13` 分支 | :120 |
| `frontend/src/components/ThreadList.tsx` | ✅ 已有 `isLine` | :499 |
| `frontend/src/utils/permissions.ts` | ✅ 已有 `line::view` / `line::manage` 与 path 映射 | :143-144, :211 |
| `frontend/src/config/moduleMenuMap.ts` | ✅ `'line': 'menu.channels.line'` | :51 |
| `frontend/src/routes/index.tsx` | ✅ lazy Line + `{ path: '/line', element: Line, module: 'line' }` | :49, :116 |
| `frontend/src/pages/ConversationDetail.tsx` | ❌ **缺 LINE 分支**（本文件第 1 节） | :4, :20, :244, :665, :1594 |
| `frontend/src/pages/Roles.tsx` | ❌ 缺 `line::view/manage` 权限行（兄弟 agent 报告） | :146-147, :241-242 |
| `frontend/src/pages/Conversations.tsx` | ❌ 渠道筛选枚举止于 `value: 8`，缺 9-14（兄弟 agent 报告） | :155-165, :800 |
| `frontend/src/locales/{en,zh-Hans}/format.ts` | ❌ 缺 `format.sourceType.{messenger,telegram,line,imessage}`（兄弟 agent 报告：utils/format.ts 已引用 ⇒ tsc 会红） | — |

`menu.channels.line` 若也缺，Telegram/Messenger 页同样在等；请一并核对
`frontend/src/locales/*/menu.ts`（本 agent 未核对该文件）。

---

## 4. 权限 / 后端契约一致性（已核对通过）

| 前端调用 | 后端端点 | 守卫 |
|---|---|---|
| `lineApi.getSettings()` | GET `api/line/settings` | `RequireModule("line")` + `line.view` |
| `lineApi.updateSettings()` | PUT `api/line/settings` | `RequireSuperAdmin` |
| `lineApi.getStatus()` | GET `api/line/status` | `line.view` |
| `lineApi.checkWebhook()` | POST `api/line/webhook-check` | `RequireSuperAdmin` |
| `lineApi.applyWebhookEndpoint(enabled)` | POST `api/line/webhook-endpoint?enabled=` | `RequireSuperAdmin` |
| `lineApi.pushTest()` | POST `api/line/test` | `RequireSuperAdmin` |
| `lineApi.reply()` | POST `api/line/reply` | `line.manage`（400 = 未送达，207 = 已送达但线程写入失败） |
| `lineApi.takeover()` / `releaseTakeover()` | POST `api/line/takeover` / DELETE `api/line/takeover/{id}` | `line.manage` |
| webhook 入站 | POST `api/line/webhook` | `AllowAnonymous` + `X-Line-Signature` HMAC 校验 |
| 附件直链 | GET `api/line/media/{id}` 与 `/{id}/preview` | `exp` + `sig` HMAC 校验（`LineMediaLinkSigner.Validate`） |

页面已遵守：`PUT settings` / `POST test` / `webhook-endpoint` 只在 `canManageSettings`（super_admin）下出现，
与 `utils/permissions.ts` 的 `superRoles=['super_admin']` 一致；`isReadOnly` 时显示 `channels.line.readOnly` 并隐藏保存。
「校验回调」按钮 = 后端 `POST api/line/webhook-check`（LINE 无 `GET hub.challenge` 握手，
故 WhatsApp 那条旁路注释不适用于 LINE）。
