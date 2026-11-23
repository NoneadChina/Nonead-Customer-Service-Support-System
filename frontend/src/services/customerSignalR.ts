import * as signalR from '@microsoft/signalr';
import { SignalREvents } from './signalR';
import { tBound } from '../i18n/locale';

// 开发环境标志：用于控制日志输出，避免生产环境打印调试信息
const isDev = import.meta.env.DEV;

/**
 * 客户侧 SignalR 连接管理类。
 *
 * 与客服侧 SignalRService 不同:
 * - 连接到 `/hubs/chat`(ChatHub),而非 `/hubs/notifications`(NotificationHub)
 * - 通过 URL 查询参数 `sessionToken` 完成无登录态鉴权(由后端 ChatHub.OnConnectedAsync 校验)
 * - 不依赖 JWT Cookie,使用 withCredentials=false 避免跨域 Cookie 干扰
 * - 仅监听与客户侧聊天相关的事件:RealtimeChat / ChatMessageRecalled / ChatMessageEdited / ChatMessageRead / ChatTyping
 */
class CustomerSignalRService {
  private connection: signalR.HubConnection | null = null;
  private maxReconnectAttempts = 5;
  private listeners: Map<string, Set<(data: unknown) => void>> = new Map();
  private connectionState: 'disconnected' | 'connecting' | 'connected' | 'reconnecting' =
    'disconnected';
  private currentSessionToken: string | null = null;

  /** 客户侧需要监听的事件子集(无需监听客服侧的全量事件) */
  private static readonly CUSTOMER_EVENTS: string[] = [
    SignalREvents.RealtimeChat,
    SignalREvents.ChatMessageRecalled,
    SignalREvents.ChatMessageEdited,
    SignalREvents.ChatMessageRead,
    SignalREvents.ChatTyping,
  ];

  getState() {
    return this.connectionState;
  }

  isConnected() {
    return this.connectionState === 'connected';
  }

  getCurrentSessionToken() {
    return this.currentSessionToken;
  }

  private setState(state: typeof this.connectionState) {
    if (this.connectionState !== state) {
      this.connectionState = state;
      this.notifyListeners('_stateChange', state);
    }
  }

  /**
   * 连接到客户侧 ChatHub。
   * M13: sessionToken 不再放进 URL（防日志/历史/Referer 泄露），
   * 连接成功后通过 JoinSession 消息体显式加入会话组。
   * @param sessionToken 客户聊天会话 Token(由 /api/chat/initiate 返回)
   */
  async connect(sessionToken: string): Promise<void> {
    if (!sessionToken) {
      // 非组件模块：`tBound` 在**抛出这一刻**取当前绑定语言（由 I18nProvider 绑定）
      throw new Error(tBound('customers.common.sessionTokenRequired'));
    }

    // 已存在连接且使用相同 token,直接复用
    if (this.connection && this.connectionState === 'connected' && this.currentSessionToken === sessionToken) {
      return;
    }

    // token 变化时先断开旧连接
    if (this.connection && this.currentSessionToken !== sessionToken) {
      await this.disconnect();
    }

    this.currentSessionToken = sessionToken;
    this.setState('connecting');

    this.connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/chat', {
        // 客户侧无登录态,不携带 Cookie,避免 CORS 凭据问题
        withCredentials: false,
      })
      .withAutomaticReconnect({
        nextRetryDelayInMilliseconds: (retryContext) => {
          if (retryContext.previousRetryCount >= this.maxReconnectAttempts) {
            if (isDev) console.warn('[CustomerSignalR] 达到最大重连次数,停止重连');
            return null;
          }
          // 重连间隔:0, 2, 4, 6, 8 秒
          return Math.min(retryContext.previousRetryCount * 2000, 10000);
        },
      })
      .configureLogging(import.meta.env.DEV ? signalR.LogLevel.Information : signalR.LogLevel.Warning)
      .build();

    this.setupEventHandlers();

    try {
      await this.connection.start();
      // M13: 通过消息体加入会话组（后端 JoinSession 校验 token 有效性）
      await this.connection.invoke('JoinSession', sessionToken);
      if (isDev) console.info('[CustomerSignalR] 已连接到客户聊天 Hub');
      this.setState('connected');
    } catch (error: unknown) {
      if (isDev) console.error('[CustomerSignalR] 连接失败:', error);
      this.setState('disconnected');
      throw error;
    }
  }

  async disconnect(): Promise<void> {
    if (this.connection) {
      try {
        await this.connection.stop();
        if (isDev) console.info('[CustomerSignalR] 已断开连接');
      } catch (error: unknown) {
        if (isDev) console.error('[CustomerSignalR] 断开连接时出错:', error);
      }
      this.connection = null;
      this.currentSessionToken = null;
      this.setState('disconnected');
      this.listeners.clear();
    }
  }

  private setupEventHandlers() {
    if (!this.connection) return;

    this.connection.onclose((error) => {
      if (error) {
        if (isDev) console.error('[CustomerSignalR] 连接关闭:', error);
      }
      this.setState('disconnected');
    });

    this.connection.onreconnecting((error) => {
      if (isDev) console.warn('[CustomerSignalR] 正在重连...', error);
      this.setState('reconnecting');
    });

    this.connection.onreconnected(async (connectionId) => {
      if (isDev) console.info('[CustomerSignalR] 重连成功,连接 ID:', connectionId);
      // M13: 重连后连接为新连接，需重新通过消息体加入会话组
      if (this.currentSessionToken) {
        try {
          await this.connection?.invoke('JoinSession', this.currentSessionToken);
        } catch (error: unknown) {
          if (isDev) console.error('[CustomerSignalR] 重连后加入会话组失败:', error);
          return;
        }
      }
      this.setState('connected');
    });

    // 仅注册客户侧关心的事件
    CustomerSignalRService.CUSTOMER_EVENTS.forEach((eventName) => {
      if (!this.connection) return;
      this.connection.on(eventName, (data: unknown) => {
        if (import.meta.env.DEV) {
          console.debug(`[CustomerSignalR] 收到事件 ${eventName}:`, data);
        }
        this.notifyListeners(eventName, data);
      });
    });
  }

  on(eventName: string, callback: (data: unknown) => void): void {
    if (!this.listeners.has(eventName)) {
      this.listeners.set(eventName, new Set());
    }
    this.listeners.get(eventName)?.add(callback);
  }

  off(eventName: string, callback: (data: unknown) => void): void {
    const eventListeners = this.listeners.get(eventName);
    if (eventListeners) {
      eventListeners.delete(callback);
    }
  }

  private notifyListeners(eventName: string, data: unknown): void {
    const eventListeners = this.listeners.get(eventName);
    if (eventListeners) {
      eventListeners.forEach((callback) => {
        try {
          callback(data);
        } catch (error: unknown) {
          if (isDev) console.error(`[CustomerSignalR] 监听器 ${eventName} 执行出错:`, error);
        }
      });
    }
  }
}

// 单例实例
export const customerSignalRService = new CustomerSignalRService();
export default customerSignalRService;
