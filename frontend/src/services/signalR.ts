import * as signalR from '@microsoft/signalr';

// 开发环境标志：用于控制日志输出，避免生产环境打印调试信息
const isDev = import.meta.env.DEV;

// 读取浏览器 Cookie 的工具（用于 XSRF-TOKEN 等）
function readCookie(name: string): string | null {
  if (typeof document === 'undefined' || !document.cookie) return null;
  const prefix = `${encodeURIComponent(name)}=`;
  for (const part of document.cookie.split(';')) {
    const s = part.trim();
    if (s.indexOf(prefix) === 0) {
      return decodeURIComponent(s.substring(prefix.length));
    }
  }
  return null;
}

/**
 * 为 SignalR 的每个 HTTP 请求附加 X-XSRF-TOKEN 头。
 * 后端在 [ValidateAntiForgeryToken] 下要求 POST/PUT/PATCH/DELETE 等写入操作
 * 必须同时携带：
 *   1) Cookie 中的 XSRF-TOKEN
 *   2) Header 中的 X-XSRF-TOKEN（同值）
 * 原生 SignalR 的 negotiate 只带 Cookie（带 withCredentials），不带 header，
 * 因此所有带 Cookie 的 Hub 请求都会被后端以 "CSRF 验证失败" 拒绝 (403)。
 * 这里实现 IHttpClient 封装 DefaultHttpClient，在 send() 之前补 X-XSRF-TOKEN header。
 */
// @microsoft/signalr v10 DefaultHttpClient 构造函数要求传入 ILogger，
// 但对外导出中既没有 ConsoleLogger 也没有 NullLogger 单例，所以自行实现一个。
const silentLogger: signalR.ILogger = {
  log(logLevel: signalR.LogLevel, message: string) {
    if (!isDev) return;
    if (logLevel >= signalR.LogLevel.Warning) {
       
      console.debug(`[SignalR.HttpClient][${signalR.LogLevel[logLevel]}] ${message}`);
    }
  },
};

class XsrfAwareHttpClient extends signalR.HttpClient {
  private inner: signalR.DefaultHttpClient;
  constructor() {
    super();
    this.inner = new signalR.DefaultHttpClient(silentLogger);
  }
  send(request: signalR.HttpRequest): Promise<signalR.HttpResponse> {
    const token = readCookie('XSRF-TOKEN');
    if (token) {
      request.headers = {
        ...(request.headers || {}),
        'X-XSRF-TOKEN': token,
      };
    }
    return this.inner.send(request);
  }
  getCookieString(url: string): string {
    return this.inner.getCookieString(url);
  }
}

// SignalR 事件类型
export const SignalREvents = {
  NewConversation: 'NewConversation',
  NewReply: 'NewReply',
  ConversationStatusChanged: 'ConversationStatusChanged',
  ConversationAssigned: 'ConversationAssigned',
  ConversationTransferred: 'ConversationTransferred',
  SystemNotification: 'SystemNotification',
  NewNote: 'NewNote',
  RealtimeChat: 'RealtimeChat',
  RefreshConversations: 'RefreshConversations',
  RealtimeConvView: 'RealtimeConvView',
  CalendarEventChanged: 'CalendarEventChanged',
  CalendarRefresh: 'CalendarRefresh',
  // P2-A#5 §3.2 #7.1：前端收到 SignalR 实时提醒弹层 → 用户点击 ACK 免兜底
  CalendarReminder: 'CalendarReminder',
  // 聊天增强事件
  ChatMessageRecalled: 'ChatMessageRecalled',
  ChatMessageEdited: 'ChatMessageEdited',
  ChatMessageRead: 'ChatMessageRead',
  ChatTyping: 'ChatTyping',
  ChatMention: 'ChatMention',
  // 满意度评价事件
  SatisfactionUpdated: 'SatisfactionUpdated',
};

// 日历事件变更通知数据类型
export interface CalendarEventChangedNotification {
  changeType: string;
  eventId: number;
  data: unknown;
  timestamp: string;
}

// 日历刷新通知数据类型
export interface CalendarRefreshNotification {
  timestamp: string;
}

// P2-A#5 §3.2 #7.1：实时日程提醒推送载荷
export interface CalendarReminderNotification {
  eventId: number;
  title: string;
  /** 事件开始时间（ISO string，后端以 effectiveStartAt 序列化） */
  startAt: string;
  endAt?: string | null;
  isAllDay?: boolean;
  /** 提前多少分钟提醒 */
  reminderMinutes?: number | null;
  /** 推送时间（ISO string） */
  sentAt?: string;
}

// 通知数据类型
export interface NewConversationNotification {
  conversationId: number;
  subject: string;
  customerName: string;
  mailboxId: number;
  mailboxName: string;
  createdAt: string;
}

export interface NewReplyNotification {
  conversationId: number;
  replyId: number;
  senderName: string;
  senderType: 'customer' | 'agent';
  preview: string;
  createdAt: string;
}

export interface SystemNotificationData {
  id: number;
  title: string;
  message: string;
  type: 'info' | 'warning' | 'error' | 'success';
  createdAt: string;
}

// SignalR 连接管理类
class SignalRService {
  private connection: signalR.HubConnection | null = null;
  private maxReconnectAttempts = 5;
  private listeners: Map<string, Set<(data: unknown) => void>> = new Map();
  private connectionState: 'disconnected' | 'connecting' | 'connected' | 'reconnecting' =
    'disconnected';
  private activeGroups: Set<string> = new Set();

  // 获取连接状态
  getState() {
    return this.connectionState;
  }

  // 是否已连接
  isConnected() {
    return this.connectionState === 'connected';
  }

  // 更新连接状态并通知所有监听者
  private setState(state: typeof this.connectionState) {
    if (this.connectionState !== state) {
      this.connectionState = state;
      this.notifyListeners('_stateChange', state);
    }
  }

  // 连接 SignalR Hub
  async connect(): Promise<void> {
    if (this.connection && this.connectionState === 'connected') {
      return;
    }

    this.setState('connecting');

    // JWT Token 已迁移至 HttpOnly Cookie，通过 withCredentials 让浏览器自动携带 Cookie
    // 不再需要 accessTokenFactory，也不再因为没有 token 就跳过连接
    //
    // httpClient: 使用自定义的 XsrfAwareHttpClient，在 negotiate/每个 HTTP 请求上
    // 附加 X-XSRF-TOKEN header，让后端 [ValidateAntiForgeryToken] 校验通过。
    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/notifications', {
        withCredentials: true,
        httpClient: new XsrfAwareHttpClient(),
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          if (retryContext.previousRetryCount >= this.maxReconnectAttempts) {
            if (isDev) console.warn('[SignalR] 达到最大重连次数，停止重连');
            return null;
          }
          // 重连间隔：0, 2, 4, 6, 8 秒
          return Math.min(retryContext.previousRetryCount * 2000, 10000);
        },
      })
      .configureLogging(import.meta.env.DEV ? signalR.LogLevel.Information : signalR.LogLevel.Warning)
      .build();

    // 设置事件监听
    this.setupEventHandlers();

    try {
      await this.connection.start();
      if (isDev) console.info('[SignalR] 已连接到通知中心');
      this.setState('connected');
    } catch (error: unknown) {
      if (isDev) console.error('[SignalR] 连接失败:', error);
      this.setState('disconnected');
      throw error;
    }
  }

  // 断开连接
  async disconnect(): Promise<void> {
    if (this.connection) {
      try {
        await this.connection.stop();
        if (isDev) console.info('[SignalR] 已断开连接');
      } catch (error: unknown) {
        if (isDev) console.error('[SignalR] 断开连接时出错:', error);
      }
      this.connection = null;
      this.setState('disconnected');
      this.listeners.clear();
      this.activeGroups.clear();
    }
  }

  // 设置事件处理器
  private setupEventHandlers() {
    if (!this.connection) return;

    // 连接状态变化
    this.connection.onclose((error) => {
      if (error) {
        if (isDev) console.error('[SignalR] 连接关闭:', error);
      }
      this.setState('disconnected');
    });

    this.connection.onreconnecting((error) => {
      if (isDev) console.warn('[SignalR] 正在重连...', error);
      this.setState('reconnecting');
    });

    this.connection.onreconnected(async (connectionId) => {
      if (isDev) console.info('[SignalR] 重连成功，连接 ID:', connectionId);
      await this.rejoinGroups();
      this.setState('connected');
      // P1-2: 通知监听器重连成功，触发全量刷新（重连期间可能错过推送消息）
      this.notifyListeners('_reconnected', null);
    });

    // 注册所有事件监听
    Object.values(SignalREvents).forEach((eventName) => {
      if (!this.connection) return;
      this.connection.on(eventName, (data: unknown) => {
        if (import.meta.env.DEV) {
          console.debug(`[SignalR] 收到事件 ${eventName}:`, data);
        }
        this.notifyListeners(eventName, data);
      });
    });
  }

  // 添加事件监听器
  on(eventName: string, callback: (data: unknown) => void): void {
    if (!this.listeners.has(eventName)) {
      this.listeners.set(eventName, new Set());
    }
    this.listeners.get(eventName)?.add(callback);
  }

  // 移除事件监听器
  off(eventName: string, callback: (data: unknown) => void): void {
    const eventListeners = this.listeners.get(eventName);
    if (eventListeners) {
      eventListeners.delete(callback);
    }
  }

  // 通知所有监听器
  private notifyListeners(eventName: string, data: unknown): void {
    const eventListeners = this.listeners.get(eventName);
    if (eventListeners) {
      eventListeners.forEach((callback) => {
        try {
          callback(data);
        } catch (error: unknown) {
          if (isDev) console.error(`[SignalR] 监听器 ${eventName} 执行出错:`, error);
        }
      });
    }
  }

  // 加入组
  async joinGroup(groupName: string): Promise<void> {
    if (this.connection && this.connectionState === 'connected') {
      try {
        await this.connection.invoke('JoinGroup', groupName);
        this.activeGroups.add(groupName);
        if (import.meta.env.DEV) {
          console.debug(`[SignalR] 已加入组 ${groupName}`);
        }
      } catch (error: unknown) {
        if (isDev) console.error(`[SignalR] 加入组 ${groupName} 失败:`, error);
      }
    }
  }

  // 离开组
  async leaveGroup(groupName: string): Promise<void> {
    if (this.connection && this.connectionState === 'connected') {
      try {
        await this.connection.invoke('LeaveGroup', groupName);
        this.activeGroups.delete(groupName);
        if (import.meta.env.DEV) {
          console.debug(`[SignalR] 已离开组 ${groupName}`);
        }
      } catch (error: unknown) {
        if (isDev) console.error(`[SignalR] 离开组 ${groupName} 失败:`, error);
      }
    }
  }

  // 重连后重新加入所有组
  private async rejoinGroups(): Promise<void> {
    if (this.activeGroups.size === 0) return;

    if (isDev) console.info(`[SignalR] 重新加入 ${this.activeGroups.size} 个组`);
    const groups = Array.from(this.activeGroups);
    for (const groupName of groups) {
      try {
        await this.connection?.invoke('JoinGroup', groupName);
        if (import.meta.env.DEV) {
          console.debug(`[SignalR] 重连后已重新加入组 ${groupName}`);
        }
      } catch (error: unknown) {
        if (isDev) console.error(`[SignalR] 重连后重新加入组 ${groupName} 失败:`, error);
        this.activeGroups.delete(groupName);
      }
    }
  }
}

// 单例实例
export const signalRService = new SignalRService();

export default signalRService;