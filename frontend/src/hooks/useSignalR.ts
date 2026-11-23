import { useEffect, useRef, useState } from 'react';
import signalRService, { SignalREvents } from '../services/signalR';
import { useAuth } from '../context/AuthContext';

// 连接状态
export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

// SignalR Hook
export function useSignalR() {
  const { isAuthenticated } = useAuth();
  const [connectionState, setConnectionState] = useState<ConnectionState>(signalRService.getState());
  const stateRef = useRef(connectionState);

  // 更新状态的辅助函数
  const updateState = (newState: ConnectionState) => {
    if (stateRef.current !== newState) {
      stateRef.current = newState;
      setConnectionState(newState);
    }
  };

  // 连接状态变化监听
  useEffect(() => {
    const handler = (state: unknown) => {
      if (typeof state === 'string') {
        updateState(state as ConnectionState);
      }
    };
    signalRService.on('_stateChange', handler);
    return () => {
      signalRService.off('_stateChange', handler);
    };
  }, []);

  // 登录时自动连接
  useEffect(() => {
    if (isAuthenticated && signalRService.getState() === 'disconnected') {
      signalRService.connect().catch((err: unknown) => {
        console.error('[Signalr Hook] 连接失败:', err);
      });
    } else if (!isAuthenticated && signalRService.isConnected()) {
      signalRService.disconnect();
    }
  }, [isAuthenticated]);

  return {
    connectionState,
    isConnected: connectionState === 'connected',
    connect: signalRService.connect,
    disconnect: signalRService.disconnect,
  };
}

// 事件监听 Hook
export function useSignalREvent<T = unknown>(eventName: string, callback: (data: T) => void) {
  const callbackRef = useRef(callback);
  callbackRef.current = callback;

  useEffect(() => {
    const handler = (data: T) => callbackRef.current(data);
    signalRService.on(eventName, handler as (data: unknown) => void);

    return () => {
      signalRService.off(eventName, handler as (data: unknown) => void);
    };
  }, [eventName]);
}

// 新会话通知 Hook
export function useNewConversationNotification<T = unknown>(callback: (data: T) => void) {
  return useSignalREvent<T>(SignalREvents.NewConversation, callback);
}

// 新回复通知 Hook
export function useNewReplyNotification<T = unknown>(callback: (data: T) => void) {
  return useSignalREvent<T>(SignalREvents.NewReply, callback);
}

// 系统通知 Hook
export function useSystemNotification<T = unknown>(callback: (data: T) => void) {
  return useSignalREvent<T>(SignalREvents.SystemNotification, callback);
}

// 组管理 Hook
export function useSignalRGroup(groupName: string | null) {
  const { isConnected } = useSignalR();
  const previousGroupRef = useRef<string | null>(null);

  useEffect(() => {
    if (!isConnected) return;

    // 离开之前的组
    if (previousGroupRef.current && previousGroupRef.current !== groupName) {
      signalRService.leaveGroup(previousGroupRef.current);
    }

    // 加入新组
    if (groupName) {
      signalRService.joinGroup(groupName);
    }

    previousGroupRef.current = groupName;

    return () => {
      if (groupName) {
        signalRService.leaveGroup(groupName);
      }
    };
  }, [groupName, isConnected]);
}