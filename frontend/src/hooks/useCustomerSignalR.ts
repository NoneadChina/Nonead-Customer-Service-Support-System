import { useEffect, useRef, useState, useCallback } from 'react';
import customerSignalRService from '../services/customerSignalR';
import { SignalREvents } from '../services/signalR';

export type CustomerConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

/**
 * 客户侧 SignalR Hook。
 *
 * 行为:
 * - 当 `sessionToken` 非空且变化时,自动建立到 ChatHub 的连接
 * - 当 `sessionToken` 清空或组件卸载时,自动断开连接
 * - 暴露当前连接状态供 UI 显示连接指示器
 *
 * @param sessionToken 客户聊天会话 Token(为空时不连接)
 */
export function useCustomerSignalR(sessionToken: string | null) {
  const [connectionState, setConnectionState] = useState<CustomerConnectionState>(
    customerSignalRService.getState(),
  );
  const stateRef = useRef(connectionState);

  const updateState = useCallback((newState: CustomerConnectionState) => {
    if (stateRef.current !== newState) {
      stateRef.current = newState;
      setConnectionState(newState);
    }
  }, []);

  // 监听连接状态变化
  useEffect(() => {
    const handler = (state: unknown) => {
      if (typeof state === 'string') {
        updateState(state as CustomerConnectionState);
      }
    };
    customerSignalRService.on('_stateChange', handler);
    return () => {
      customerSignalRService.off('_stateChange', handler);
    };
  }, [updateState]);

  // 根据 sessionToken 自动连接/断开
  useEffect(() => {
    if (sessionToken) {
      customerSignalRService.connect(sessionToken).catch((err: unknown) => {
        console.error('[useCustomerSignalR] 连接失败:', err);
      });
    } else if (customerSignalRService.isConnected()) {
      customerSignalRService.disconnect();
    }
  }, [sessionToken]);

  // 组件卸载时断开连接,避免泄漏
  useEffect(() => {
    return () => {
      customerSignalRService.disconnect();
    };
  }, []);

  return {
    connectionState,
    isConnected: connectionState === 'connected',
    connect: customerSignalRService.connect,
    disconnect: customerSignalRService.disconnect,
  };
}

/**
 * 客户侧事件监听 Hook。
 * 与客服侧 useSignalREvent 类似,但订阅 customerSignalRService。
 */
export function useCustomerSignalREvent<T = unknown>(
  eventName: string,
  callback: (data: T) => void,
) {
  const callbackRef = useRef(callback);
  callbackRef.current = callback;

  useEffect(() => {
    const handler = (data: T) => callbackRef.current(data);
    customerSignalRService.on(eventName, handler as (data: unknown) => void);

    return () => {
      customerSignalRService.off(eventName, handler as (data: unknown) => void);
    };
  }, [eventName]);
}

// 便捷的事件监听 Hook
export function useCustomerRealtimeChat<T = unknown>(callback: (data: T) => void) {
  return useCustomerSignalREvent<T>(SignalREvents.RealtimeChat, callback);
}

export function useCustomerChatMessageRecalled<T = unknown>(callback: (data: T) => void) {
  return useCustomerSignalREvent<T>(SignalREvents.ChatMessageRecalled, callback);
}

export function useCustomerChatMessageEdited<T = unknown>(callback: (data: T) => void) {
  return useCustomerSignalREvent<T>(SignalREvents.ChatMessageEdited, callback);
}

export function useCustomerChatMessageRead<T = unknown>(callback: (data: T) => void) {
  return useCustomerSignalREvent<T>(SignalREvents.ChatMessageRead, callback);
}

export function useCustomerChatTyping<T = unknown>(callback: (data: T) => void) {
  return useCustomerSignalREvent<T>(SignalREvents.ChatTyping, callback);
}
