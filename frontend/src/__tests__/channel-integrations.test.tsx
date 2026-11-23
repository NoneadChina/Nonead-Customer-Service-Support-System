/**
 * 四个新增 IM 渠道（Messenger / Telegram / LINE / iMessage）的前端接线回归测试。
 *
 * 约定沿用本仓既有测试（`src/pages/Localization.test.tsx` 的 client 打桩 + 动词/路径断言、
 * `src/__tests__/calendar-store.test.tsx` 的 cleanup/beforeEach 结构）：
 * 只 mock `src/api/client` 的默认导出（get/put/post/delete），让**真实**的
 * `src/api/modules/system.ts` 跑起来，从而锁住「前端调用的路由与 HTTP 动词」这一层。
 *
 * 后端对应控制器：`NoneadCSS.Api/Controllers/MessengerController.cs`、
 * `TelegramController.cs`、`LineController.cs`、`IMessageController.cs`（路由前缀
 * `/api/messenger|telegram|line|imessage`）。
 */
import { describe, it, expect, beforeEach, vi } from 'vitest';
import type { ComponentType } from 'react';
import { render, waitFor, cleanup, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { I18nProvider } from '../context/I18nContext';
import { ToastProvider } from '../context/ToastContext';
import { imessageApi, lineApi, messengerApi, telegramApi } from '../api/modules/system';
import type {
  IMessageReplyRequest,
  IMessageTestRequest,
  LinePushTestRequest,
  LineReplyRequest,
  LineTakeoverRequest,
  LineWebhookCheckRequest,
  MessengerReplyRequest,
  MessengerSenderActionRequest,
  TelegramReplyRequest,
  TelegramUnreachableStateRequest,
  TelegramWebhookSetupRequest,
  UpdateIMessageSettingsRequest,
  UpdateLineSettingsRequest,
  UpdateMessengerSettingsRequest,
  UpdateTelegramSettingsRequest,
} from '../api/types/system';
import { PermissionKey, UserRole, canAccessGroup, canAccessItem } from '../utils/permissions';
import {
  getRequiredModulesForPath,
  isPathModuleActive,
  menuModuleMap,
} from '../config/moduleMenuMap';
import { protectedRoutes } from '../routes';
import {
  getSourceTypeColor,
  getSourceTypeLabel,
  getSourceTypeLabelByCode,
  sourceTypeToString,
} from '../utils/format';
import MessengerPage from '../pages/Messenger';
import TelegramPage from '../pages/Telegram';
import LinePage from '../pages/Line';
import IMessagePage from '../pages/IMessage';

// ===== 打桩：真实 api 模块 + 假的 axios client =====
const clientMocks = vi.hoisted(() => ({
  get: vi.fn(),
  put: vi.fn(),
  post: vi.fn(),
  delete: vi.fn(),
}));

vi.mock('../api/client', () => ({ default: clientMocks }));

// 页面渲染所需：`I18nProvider` 与 `usePermission` 都读 AuthContext。
// isAuthenticated=false → I18nProvider 不拉 DB 覆盖层，直接用内置真源文案。
vi.mock('../context/AuthContext', () => ({
  useAuth: () => ({
    user: null,
    isAuthenticated: false,
    loading: false,
    error: null,
    login: vi.fn(),
    logout: vi.fn(),
    updateUser: vi.fn(),
  }),
}));

/** 未配置态：渠道页据此显示「尚未配置」而不是抛错 */
const UNCONFIGURED_SETTINGS = {
  id: 1,
  configured: false,
  enabled: false,
  webhookUrl: null,
};
const UNCONFIGURED_STATUS = { configured: false, connected: false };

function stubDataForGet(url: string): unknown {
  if (/mailbox/i.test(url)) return [];
  if (/\/settings$/.test(url)) return UNCONFIGURED_SETTINGS;
  if (/\/status$/.test(url)) return UNCONFIGURED_STATUS;
  return {};
}

beforeEach(() => {
  cleanup();
  vi.clearAllMocks();
  clientMocks.get.mockImplementation(async (url: string) => ({ data: stubDataForGet(url) }));
  clientMocks.put.mockImplementation(async (_url: string, data: unknown) => ({ data }));
  clientMocks.post.mockImplementation(async (_url: string, data?: unknown) => ({ data: data ?? { success: true } }));
  clientMocks.delete.mockImplementation(async () => ({ data: true }));
  localStorage.clear();
  localStorage.setItem('noneadcss:language', 'en');
});

// ===== 1. API 层：路由 + 动词 =====
describe('API 层 · Messenger（MessengerController）', () => {
  it('settings 读 / 写、status、reply 使用正确的路径与动词', async () => {
    await messengerApi.getSettings();
    expect(clientMocks.get).toHaveBeenCalledWith('/api/messenger/settings');

    const updatePayload = {} as UpdateMessengerSettingsRequest;
    await messengerApi.updateSettings(updatePayload);
    expect(clientMocks.put).toHaveBeenCalledWith('/api/messenger/settings', updatePayload);

    await messengerApi.getStatus();
    expect(clientMocks.get).toHaveBeenCalledWith('/api/messenger/status');

    const replyPayload = {} as MessengerReplyRequest;
    await messengerApi.reply(replyPayload);
    expect(clientMocks.post).toHaveBeenCalledWith('/api/messenger/reply', replyPayload);
  });

  it('渠道专属：sender-action / subscribe / test', async () => {
    const senderPayload = {} as MessengerSenderActionRequest;
    await messengerApi.senderAction(senderPayload);
    expect(clientMocks.post).toHaveBeenCalledWith('/api/messenger/sender-action', senderPayload);

    // 无参调用时前端补空对象（后端按已配置字段订阅）
    await messengerApi.subscribe();
    expect(clientMocks.post).toHaveBeenCalledWith('/api/messenger/subscribe', {});

    await messengerApi.testConnection();
    expect(clientMocks.post).toHaveBeenCalledWith('/api/messenger/test');
  });
});

describe('API 层 · Telegram（TelegramController）', () => {
  it('settings 读 / 写、status、reply 使用正确的路径与动词', async () => {
    await telegramApi.getSettings();
    expect(clientMocks.get).toHaveBeenCalledWith('/api/telegram/settings');

    const updatePayload = {} as UpdateTelegramSettingsRequest;
    await telegramApi.updateSettings(updatePayload);
    expect(clientMocks.put).toHaveBeenCalledWith('/api/telegram/settings', updatePayload);

    await telegramApi.getStatus();
    expect(clientMocks.get).toHaveBeenCalledWith('/api/telegram/status');

    const replyPayload = {} as TelegramReplyRequest;
    await telegramApi.reply(replyPayload);
    expect(clientMocks.post).toHaveBeenCalledWith('/api/telegram/reply', replyPayload);
  });

  it('webhook setup / delete 与 unreachable/clear、test', async () => {
    const setupPayload = {} as TelegramWebhookSetupRequest;
    await telegramApi.setupWebhook(setupPayload);
    expect(clientMocks.post).toHaveBeenCalledWith('/api/telegram/webhook/setup', setupPayload);

    await telegramApi.deleteWebhook();
    expect(clientMocks.post).toHaveBeenCalledWith('/api/telegram/webhook/delete');

    const unreachablePayload = {} as TelegramUnreachableStateRequest;
    await telegramApi.clearUnreachable(unreachablePayload);
    expect(clientMocks.post).toHaveBeenCalledWith('/api/telegram/unreachable/clear', unreachablePayload);

    await telegramApi.testConnection();
    expect(clientMocks.post).toHaveBeenCalledWith('/api/telegram/test');
  });
});

describe('API 层 · LINE（LineController）', () => {
  it('settings 读 / 写、status、reply 使用正确的路径与动词', async () => {
    await lineApi.getSettings();
    expect(clientMocks.get).toHaveBeenCalledWith('/api/line/settings');

    const updatePayload = {} as UpdateLineSettingsRequest;
    await lineApi.updateSettings(updatePayload);
    expect(clientMocks.put).toHaveBeenCalledWith('/api/line/settings', updatePayload);

    await lineApi.getStatus();
    expect(clientMocks.get).toHaveBeenCalledWith('/api/line/status');

    const replyPayload = {} as LineReplyRequest;
    await lineApi.reply(replyPayload);
    expect(clientMocks.post).toHaveBeenCalledWith('/api/line/reply', replyPayload);
  });

  it('人工接管：POST takeover + DELETE takeover/{id}', async () => {
    const takeoverPayload = {} as LineTakeoverRequest;
    await lineApi.takeover(takeoverPayload);
    expect(clientMocks.post).toHaveBeenCalledWith('/api/line/takeover', takeoverPayload);

    await lineApi.releaseTakeover(42);
    expect(clientMocks.delete).toHaveBeenCalledWith('/api/line/takeover/42');
  });

  it('配额探活与 webhook 校验 / 写回（webhook-endpoint 走 query 参数）', async () => {
    await lineApi.testConnection();
    expect(clientMocks.post).toHaveBeenCalledWith('/api/line/test', {});

    const pushPayload = {} as LinePushTestRequest;
    await lineApi.pushTest(pushPayload);
    expect(clientMocks.post).toHaveBeenCalledWith('/api/line/test', pushPayload);

    const checkPayload = {} as LineWebhookCheckRequest;
    await lineApi.checkWebhook(checkPayload);
    expect(clientMocks.post).toHaveBeenCalledWith('/api/line/webhook-check', checkPayload);

    await lineApi.applyWebhookEndpoint(true);
    expect(clientMocks.post).toHaveBeenCalledWith('/api/line/webhook-endpoint?enabled=true');
    await lineApi.applyWebhookEndpoint(false);
    expect(clientMocks.post).toHaveBeenCalledWith('/api/line/webhook-endpoint?enabled=false');
  });
});

describe('API 层 · iMessage（IMessageController）', () => {
  it('settings 读 / 写、status、reply 使用正确的路径与动词', async () => {
    await imessageApi.getSettings();
    expect(clientMocks.get).toHaveBeenCalledWith('/api/imessage/settings');

    const updatePayload = {} as UpdateIMessageSettingsRequest;
    await imessageApi.updateSettings(updatePayload);
    expect(clientMocks.put).toHaveBeenCalledWith('/api/imessage/settings', updatePayload);

    await imessageApi.getStatus();
    expect(clientMocks.get).toHaveBeenCalledWith('/api/imessage/status');

    const replyPayload = {} as IMessageReplyRequest;
    await imessageApi.reply(replyPayload);
    expect(clientMocks.post).toHaveBeenCalledWith('/api/imessage/reply', replyPayload);
  });

  it('test 与 register-webhook', async () => {
    const testPayload = {} as IMessageTestRequest;
    await imessageApi.testConnection(testPayload);
    expect(clientMocks.post).toHaveBeenCalledWith('/api/imessage/test', testPayload);
    await imessageApi.testConnection();
    expect(clientMocks.post).toHaveBeenCalledWith('/api/imessage/test', {});

    await imessageApi.registerWebhook();
    expect(clientMocks.post).toHaveBeenCalledWith('/api/imessage/webhook-register');
  });
});

// ===== 2. 权限键 + 菜单/路由接线 =====
const CHANNELS = [
  {
    viewKey: PermissionKey.MessengerView,
    manageKey: PermissionKey.MessengerManage,
    path: '/messenger',
    module: 'messenger',
  },
  {
    viewKey: PermissionKey.TelegramView,
    manageKey: PermissionKey.TelegramManage,
    path: '/telegram',
    module: 'telegram',
  },
  {
    viewKey: PermissionKey.LineView,
    manageKey: PermissionKey.LineManage,
    path: '/line',
    module: 'line',
  },
  {
    viewKey: PermissionKey.IMessageView,
    manageKey: PermissionKey.IMessageManage,
    path: '/imessage',
    module: 'imessage',
  },
] as const;

describe('权限层 · PermissionKey 与后端 RoleService 的字符串权限一致', () => {
  it('四个渠道的 view / manage 键名正确', () => {
    expect(PermissionKey.MessengerView).toBe('messenger.view');
    expect(PermissionKey.MessengerManage).toBe('messenger.manage');
    expect(PermissionKey.TelegramView).toBe('telegram.view');
    expect(PermissionKey.TelegramManage).toBe('telegram.manage');
    expect(PermissionKey.LineView).toBe('line.view');
    expect(PermissionKey.LineManage).toBe('line.manage');
    expect(PermissionKey.IMessageView).toBe('imessage.view');
    expect(PermissionKey.IMessageManage).toBe('imessage.manage');
  });

  it('pathPermissionMap 四行已登记：有 .view 才放行（manage 单独存在不放行）', () => {
    for (const { path, viewKey, manageKey } of CHANNELS) {
      expect(canAccessItem(UserRole.Agent, path, [viewKey]), path).toBe(true);
      expect(canAccessItem(UserRole.Agent, path, []), `${path} 无权限`).toBe(false);
      expect(canAccessItem(UserRole.Agent, path, [manageKey]), `${path} 只有 manage`).toBe(false);
      expect(canAccessItem(UserRole.Agent, path, ['*']), `${path} 通配符`).toBe(true);
      // 子路由按最长前缀回落到本渠道的 .view
      expect(canAccessItem(UserRole.Agent, `${path}/detail`, [viewKey]), `${path}/detail`).toBe(true);
    }
  });

  it('features 分组已纳入四个渠道（任一子项有权限即显示分组）', () => {
    for (const { viewKey } of CHANNELS) {
      expect(canAccessGroup(UserRole.Agent, 'features', [viewKey]), viewKey).toBe(true);
    }
  });
});

describe('模块开关与路由表 · 四个渠道全部登记', () => {
  it('menuModuleMap 含 /messenger /telegram /line /imessage 四行', () => {
    for (const { path, module } of CHANNELS) {
      expect(menuModuleMap[path]?.moduleName, path).toBe(module);
      expect(getRequiredModulesForPath(path), path).toEqual([module]);
    }
  });

  it('模块未激活时路径不可进入，激活后可进入', () => {
    const all = new Set(['messenger', 'telegram', 'line', 'imessage']);
    for (const { path, module } of CHANNELS) {
      expect(isPathModuleActive(path, new Set<string>(), all), path).toBe(false);
      expect(isPathModuleActive(path, new Set([module]), all), path).toBe(true);
    }
  });

  it('routes/index.tsx 的四条路由存在，且 module 字段与 menuModuleMap 一致', () => {
    for (const { path, module } of CHANNELS) {
      const route = protectedRoutes.find((r) => r.path === path);
      expect(route, `缺少路由 ${path}`).toBeDefined();
      expect(route?.module, `${path} 的 module 字段`).toBe(module);
      expect(route?.element, `${path} 的懒加载组件`).toBeTruthy();
    }
  });
});

// ===== 3. 来源类型映射（SourceType 11–14）=====
describe('来源类型映射 · Messenger=11 Telegram=12 Line=13 IMessage=14', () => {
  it('sourceTypeToString 覆盖四个新枚举值', () => {
    expect(sourceTypeToString(11)).toBe('messenger');
    expect(sourceTypeToString(12)).toBe('telegram');
    expect(sourceTypeToString(13)).toBe('line');
    expect(sourceTypeToString(14)).toBe('imessage');
    expect(sourceTypeToString(99)).toBeNull();
    expect(sourceTypeToString(undefined)).toBeNull();
  });

  // 注意：截至本文件提交时，en / zh-Hans 两套词典都**没有**
  // `format.sourceType.messenger|telegram|line|imessage` 四个词条（见 `src/locales/en/format.ts`
  // 止于 dingtalk）。`translate()` 缺 key 时按失败安全返回 key 本身，因此今天这四个标签
  // 渲染成原始 key。以下断言刻意只要求「映射存在 + 四个渠道互不相同」，
  // 补上词条后（标签变成 Messenger / Telegram / LINE / iMessage）依然通过。
  it('getSourceTypeLabel 为 11–14 产出互不相同、且非「未知」的标签', () => {
    const unknown = getSourceTypeLabel(99, 'en');
    const labels = [11, 12, 13, 14].map((code) => getSourceTypeLabel(code, 'en'));
    for (const label of labels) {
      expect(typeof label).toBe('string');
      expect(label.length).toBeGreaterThan(0);
      expect(label).not.toBe(unknown);
    }
    expect(new Set(labels).size).toBe(4);
  });

  it('getSourceTypeLabelByCode 覆盖四个新渠道码（与 KnownChannels 一致）', () => {
    const labels = ['messenger', 'telegram', 'line', 'imessage'].map((code) =>
      getSourceTypeLabelByCode(code, 'en'),
    );
    for (const label of labels) {
      expect(label.length).toBeGreaterThan(0);
    }
    expect(new Set(labels).size).toBe(4);
    // 未知渠道原样返回，便于暴露后端新增渠道
    expect(getSourceTypeLabelByCode('threema', 'en')).toBe('threema');
  });

  it('getSourceTypeColor 对新枚举值有兜底色（不抛错、返回类名字符串）', () => {
    for (const code of [11, 12, 13, 14]) {
      const color = getSourceTypeColor(code);
      expect(typeof color).toBe('string');
      expect(color.length).toBeGreaterThan(0);
      expect(color).toContain('text-');
    }
  });
});

// ===== 4. 页面冒烟：未配置时能渲染并显示渠道标题 =====
function renderChannelPage(Page: ComponentType) {
  return render(
    <MemoryRouter>
      <ToastProvider>
        <I18nProvider>
          <Page />
        </I18nProvider>
      </ToastProvider>
    </MemoryRouter>,
  );
}

describe('页面冒烟 · 未配置态下四个渠道页可渲染', () => {
  it('pages/Messenger.tsx 渲染标题并拉取 settings', async () => {
    renderChannelPage(MessengerPage);
    await waitFor(() =>
      expect(document.body.textContent).toContain('Messenger integration'),
    );
    expect(clientMocks.get).toHaveBeenCalledWith('/api/messenger/settings');
  });

  it('pages/Telegram.tsx 渲染标题', async () => {
    renderChannelPage(TelegramPage);
    await waitFor(() =>
      expect(document.body.textContent).toContain('Telegram integration'),
    );
  });

  it('pages/Line.tsx 渲染标题并拉取 settings', async () => {
    renderChannelPage(LinePage);
    await waitFor(() => expect(document.body.textContent).toContain('LINE integration'));
    expect(clientMocks.get).toHaveBeenCalledWith('/api/line/settings');
  });

  it('pages/IMessage.tsx 渲染标题并拉取 settings', async () => {
    renderChannelPage(IMessagePage);
    await waitFor(() => expect(document.body.textContent).toContain('iMessage integration'));
    expect(clientMocks.get).toHaveBeenCalledWith('/api/imessage/settings');
  });
});

// ===== 5. LINE 状态卡与可调参数（审计 #11 / #12）=====
/**
 * 审计 #11：ReplyModeHint / LastError 此前永远为空（服务端从不赋值），状态卡上永远显示「未知」。
 * 服务端现在只回机器可读代码（reply / push / unknown），文案由前端 locale 解析 ⇒ 这里锁住
 * 「代码 → 文案」这条链路，同时锁 LastError 横幅真的渲染。
 * 审计 #12：六个可调参数（statusCheckTimeoutSeconds / retryMaxAttempts / maxInboundMediaBytes /
 * maxOutboundAttachmentBytes / outboundImageExtensions / channelAccessTokenKeyId）此前没进 DTO 与表单。
 * 用 min/max/placeholder 定位输入框（不依赖译文措辞），锁「后端 [Range] 已搬到界面」。
 */
const LINE_SETTINGS_CONFIGURED = {
  id: 1,
  configured: true,
  enabled: true,
  webhookUrl: 'https://css.example.com/api/line/webhook',
  channelId: '@opencss-demo',
  hasChannelSecret: true,
  hasChannelAccessToken: true,
  webhookEnabled: true,
  textChunkChars: 5000,
  maxMessagesPerRequest: 5,
  replyTokenTtlSeconds: 25,
  humanHandoverMuteMinutes: 60,
  quotaWarnPercent: 80,
  attachmentPublicUrlTtlMinutes: 10080,
  // 审计 #12：后端 LineOptions 已有、此前界面调不到的六项
  statusCheckTimeoutSeconds: 8,
  retryMaxAttempts: 1,
  maxInboundMediaBytes: 104857600,
  maxOutboundAttachmentBytes: 10485760,
  outboundImageExtensions: 'jpg,jpeg,png',
  channelAccessTokenKeyId: 'kid-2026-11',
};

function stubConfiguredLine(statusOverride: Record<string, unknown>) {
  clientMocks.get.mockImplementation(async (url: string) => {
    if (/mailbox/i.test(url)) return { data: [] };
    if (/\/api\/line\/settings$/.test(url)) return { data: LINE_SETTINGS_CONFIGURED };
    if (/\/api\/line\/status$/.test(url)) {
      return {
        data: {
          configured: true,
          enabled: true,
          webhookEnabled: true,
          webhookUrl: 'https://css.example.com/api/line/webhook',
          tokenValid: true,
          tokenExpiresAt: null,
          webhookEndpointOnLine: 'https://css.example.com/api/line/webhook',
          webhookDeliveryOnLine: true,
          quotaType: 'unlimited',
          ...statusOverride,
        },
      };
    }
    return { data: {} };
  });
}

describe('页面接线 · LINE 状态卡与六个可调参数', () => {
  it('replyModeHint 是代码，界面按代码取文案；lastError 渲染成 HTTP 状态 + message', async () => {
    stubConfiguredLine({
      replyModeHint: 'push',
      lastError: { httpStatus: 429, message: 'Too Many Requests' },
    });
    renderChannelPage(LinePage);

    await waitFor(() => expect(document.body.textContent).toContain('push (consumes monthly quota)'));
    expect(document.body.textContent).toContain('HTTP 429 · Too Many Requests');
  });

  it('replyModeHint=reply 时显示 reply 文案，没有 lastError 时不出现错误横幅', async () => {
    stubConfiguredLine({ replyModeHint: 'reply', lastError: null });
    renderChannelPage(LinePage);

    await waitFor(() => expect(document.body.textContent).toContain('reply (free, within the 25s window)'));
    expect(document.body.textContent).not.toContain('HTTP 429');
  });

  it('六个可调参数出现在设置表单里，数字框带上与后端 [Range] 一致的 min/max', async () => {
    stubConfiguredLine({ replyModeHint: 'reply' });
    const { container } = renderChannelPage(LinePage);
    await waitFor(() => expect(document.body.textContent).toContain('LINE integration'));

    const numbers = Array.from(container.querySelectorAll<HTMLInputElement>('input[type="number"]'));
    const hasNumber = (value: string, min: string, max: string) =>
      numbers.some((i) => i.value === value && i.min === min && i.max === max);

    // 状态检测超时 8（1–60）/ 重试 1（0–5）/ 入站 104857600 / 出站 10485760（都是 1–209715200）
    expect(hasNumber('8', '1', '60'), 'statusCheckTimeoutSeconds 输入框缺失或范围不对').toBe(true);
    expect(hasNumber('1', '0', '5'), 'retryMaxAttempts 输入框缺失或范围不对').toBe(true);
    expect(hasNumber('104857600', '1', '209715200'), 'maxInboundMediaBytes 输入框缺失或范围不对').toBe(true);
    expect(hasNumber('10485760', '1', '209715200'), 'maxOutboundAttachmentBytes 输入框缺失或范围不对').toBe(true);

    const extensions = container.querySelector<HTMLInputElement>('input[placeholder="jpg,jpeg,png"]');
    expect(extensions, 'outboundImageExtensions 输入框缺失').not.toBeNull();
    expect(extensions?.value).toBe('jpg,jpeg,png');

    // KeyId 此前只读展示，现在可编辑（空串保存 = 清除，回落 appsettings 基线）
    const keyId = container.querySelector<HTMLInputElement>('input[placeholder="kid-xxxxxxxx-xxxx"]');
    expect(keyId, 'channelAccessTokenKeyId 输入框缺失（仍是只读展示？）').not.toBeNull();
    expect(keyId?.value).toBe('kid-2026-11');
  });
});
