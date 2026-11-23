import { useEffect, useState, useRef, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { auditLogApi } from '../api';
import type { AuditLogResponse } from '../api/types';
import { useAuth } from '../context/AuthContext';
import { useI18n } from '../context/I18nContext';
import type { TranslationKey } from '../locales/zh-Hans';
import { relativeTime, formatDateTime } from '../utils/format';
import signalRService, { SignalREvents } from '../services/signalR';
import { useAutoRefresh } from '../hooks/useAutoRefresh';

/**
 * 操作类型配置：图标 + 颜色 + **真源 key**。
 *
 * 持有的是 `TranslationKey` 而不是译好的字符串——映射表在模块级常量里，
 * 拿不到 React 上下文；若在这里存中文/英文，就只有一种语言且会在切换语言时冻结。
 * 使用点必须经 `t()` 解析（见 `cfg.label` 的渲染处）。
 *
 * `label: null` 表示"未知操作码"：渲染时直接显示原始 action 码
 * （与改造前 `label: action` 的行为一致）。
 */
interface ActionConfig {
  label: TranslationKey | null;
  icon: React.ReactNode;
  color: string;
}

const actionConfig: Record<string, ActionConfig> = {
  login: {
    label: 'notifications.bell.login',
    color: 'bg-gray-100 text-gray-700',
    icon: (
      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
        <path strokeLinecap="round" strokeLinejoin="round" d="M11 16l-4-4m0 0l4-4m-4 4h14m-5 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1" />
      </svg>
    ),
  },
  logout: {
    label: 'notifications.bell.logout',
    color: 'bg-gray-100 text-gray-700',
    icon: (
      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
        <path strokeLinecap="round" strokeLinejoin="round" d="M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1" />
      </svg>
    ),
  },
  create: {
    label: 'notifications.bell.create',
    color: 'bg-green-50 text-green-700',
    icon: (
      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
        <path strokeLinecap="round" strokeLinejoin="round" d="M12 4v16m8-8H4" />
      </svg>
    ),
  },
  update: {
    label: 'notifications.bell.update',
    color: 'bg-blue-50 text-blue-700',
    icon: (
      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
        <path strokeLinecap="round" strokeLinejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
      </svg>
    ),
  },
  delete: {
    // 与其它页面共用「删除」——不另造键
    label: 'common.delete',
    color: 'bg-red-50 text-red-700',
    icon: (
      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
        <path strokeLinecap="round" strokeLinejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
      </svg>
    ),
  },
  assign: {
    label: 'notifications.bell.assign',
    color: 'bg-purple-50 text-purple-700',
    icon: (
      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
        <path strokeLinecap="round" strokeLinejoin="round" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
      </svg>
    ),
  },
  'status_change': {
    label: 'notifications.bell.statusChange',
    color: 'bg-amber-50 text-amber-700',
    icon: (
      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
        <path strokeLinecap="round" strokeLinejoin="round" d="M9 12l2 2 4-4m6 2a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
    ),
  },
  reply: {
    label: 'notifications.bell.reply',
    color: 'bg-cyan-50 text-cyan-700',
    icon: (
      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
        <path strokeLinecap="round" strokeLinejoin="round" d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
      </svg>
    ),
  },
  note: {
    label: 'notifications.bell.note',
    color: 'bg-yellow-50 text-yellow-700',
    icon: (
      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
        <path strokeLinecap="round" strokeLinejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
      </svg>
    ),
  },
};

const getActionConfig = (action: string): ActionConfig => {
  const key = action.toLowerCase();
  return actionConfig[key] ?? {
    label: null,
    color: 'bg-gray-100 text-gray-700',
    icon: (
      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
        <path strokeLinecap="round" strokeLinejoin="round" d="M13 16h-1v-4h-1m1-4h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
    ),
  };
};

// 实体类型 → 跳转链接生成器
const getEntityLink = (entityType: string, entityId: number): string | null => {
  const t = entityType.toLowerCase();
  if (t === 'conversation') return `/conversations/${entityId}`;
  if (t === 'customer') return `/customers`;
  if (t === 'user') return `/users`;
  if (t === 'mailbox') return `/mailboxes`;
  if (t === 'tag') return `/tags`;
  return null;
};

export default function NotificationBell() {
  const navigate = useNavigate();
  const { t } = useI18n();
  const { user } = useAuth();
  const [open, setOpen] = useState(false);
  const [logs, setLogs] = useState<AuditLogResponse[]>([]);
  const [loading, setLoading] = useState(false);
  const [unreadCount, setUnreadCount] = useState(0);
  const [lastReadAt, setLastReadAt] = useState(0);
  const containerRef = useRef<HTMLDivElement>(null);

  // 用 localStorage 记录上次已读时间戳
  const storageKey = `noneadcss:lastReadLogAt:${user?.id ?? 'anon'}`;
  const legacyStorageKey = `freescout:lastReadLogAt:${user?.id ?? 'anon'}`;

  useEffect(() => {
    try {
      const legacy = localStorage.getItem(legacyStorageKey);
      if (legacy && !localStorage.getItem(storageKey)) {
        localStorage.setItem(storageKey, legacy);
      }
    } catch {
      // ignore
    }
  }, [user?.id, storageKey, legacyStorageKey]);

  const fetchLogsCancelled = useRef(false);
  const fetchLogs = useCallback(async () => {
    if (!user?.id) return;
    fetchLogsCancelled.current = false;
    setLoading(true);
    try {
      const resp = await auditLogApi.getByUser(user.id, 1, 20);
      if (fetchLogsCancelled.current) return;
      const arr = Array.isArray(resp) ? resp : (resp?.data ?? []);
      setLogs(arr);
      // 计算未读数
      const lastReadStr = localStorage.getItem(storageKey);
      const lastRead = lastReadStr ? new Date(lastReadStr).getTime() : 0;
      setLastReadAt(lastRead);
      const unread = arr.filter((l) => new Date(l.createdAt).getTime() > lastRead).length;
      setUnreadCount(unread);
    } catch {
      if (!fetchLogsCancelled.current) setLogs([]);
    } finally {
      if (!fetchLogsCancelled.current) setLoading(false);
    }
  }, [user?.id, storageKey]);

  // 初次加载
  useEffect(() => {
    fetchLogs();
    return () => { fetchLogsCancelled.current = true; };
  }, [fetchLogs]);

  // 根据用户偏好自动刷新
  useAutoRefresh(fetchLogs, [fetchLogs]);

  // SignalR 实时通知监听
  useEffect(() => {
    // 新会话通知：刷新列表
    const handleNewConversation = () => {
      fetchLogs();
    };

    // 新回复通知：刷新列表
    const handleNewReply = () => {
      fetchLogs();
    };

    // 会话状态变更
    const handleStatusChange = () => {
      fetchLogs();
    };

    signalRService.on(SignalREvents.NewConversation, handleNewConversation);
    signalRService.on(SignalREvents.NewReply, handleNewReply);
    signalRService.on(SignalREvents.ConversationStatusChanged, handleStatusChange);

    return () => {
      signalRService.off(SignalREvents.NewConversation, handleNewConversation);
      signalRService.off(SignalREvents.NewReply, handleNewReply);
      signalRService.off(SignalREvents.ConversationStatusChanged, handleStatusChange);
    };
  }, [fetchLogs]);

  // 点击外部关闭
  useEffect(() => {
    const handler = (e: MouseEvent) => {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    if (open) {
      document.addEventListener('mousedown', handler);
    }
    return () => document.removeEventListener('mousedown', handler);
  }, [open]);

  const handleToggle = () => {
    const next = !open;
    setOpen(next);
    if (next && unreadCount > 0) {
      // 打开时标记为已读
      const latest = logs[0]?.createdAt;
      if (latest) {
        const ts = new Date(latest).getTime();
        localStorage.setItem(storageKey, latest);
        setLastReadAt(ts);
      }
      setUnreadCount(0);
    }
  };

  const handleClickItem = (log: AuditLogResponse) => {
    const link = getEntityLink(log.entityType, log.entityId ?? 0);
    if (link) {
      navigate(link);
      setOpen(false);
    }
  };

  const handleMarkAllRead = () => {
    const latest = logs[0]?.createdAt;
    if (latest) {
      const ts = new Date(latest).getTime();
      localStorage.setItem(storageKey, latest);
      setLastReadAt(ts);
    }
    setUnreadCount(0);
  };

  return (
    <div ref={containerRef} className="relative">
      <button
        onClick={handleToggle}
        className="relative p-2 text-gray-500 dark:text-slate-400 hover:text-gray-900 dark:hover:text-slate-100 hover:bg-gray-100 dark:hover:bg-slate-700 rounded-lg transition"
        title={t('notifications.bell.title')}
        aria-label={unreadCount > 0 ? t('notifications.bell.unreadCount', { count: unreadCount }) : t('notifications.bell.title')}
        aria-expanded={open}
        aria-haspopup="menu"
      >
        <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9" />
        </svg>
        {unreadCount > 0 && (
          <span className="absolute top-1 right-1 min-w-[16px] h-4 px-1 bg-red-500 text-white text-[10px] font-semibold rounded-full flex items-center justify-center">
            {unreadCount > 99 ? '99+' : unreadCount}
          </span>
        )}
      </button>

      {/* 下拉通知面板
          T000107: 移动端 fixed 定位相对视口（right-2/top 对齐 sticky header 下方），
          避免 absolute 相对 header 内铃铛容器定位时面板左侧溢出视口被裁剪；
          桌面端（lg:）恢复 absolute 对齐铃铛。 */}
      {open && (
        <div className="fixed right-2 top-[4.5rem] z-50 w-96 max-w-[calc(100vw-2rem)] bg-white dark:bg-slate-800 rounded-xl shadow-xl border border-gray-200 dark:border-slate-700 overflow-hidden lg:absolute lg:right-0 lg:top-auto lg:mt-2">
          {/* 头部 */}
          <div className="px-4 py-3 border-b border-gray-100 dark:border-slate-700/50 flex items-center justify-between">
            <div>
              <h3 className="text-sm font-semibold text-gray-900 dark:text-slate-100">{t('notifications.bell.title')}</h3>
              <p className="text-xs text-gray-500 dark:text-slate-400 mt-0.5">
                {unreadCount > 0 ? t('notifications.bell.unreadOnly', { count: unreadCount }) : t('notifications.bell.allRead')}
              </p>
            </div>
            {logs.length > 0 && unreadCount > 0 && (
              <button
                onClick={handleMarkAllRead}
                className="text-xs text-primary-600 hover:text-primary-700"
              >
                {t('notifications.bell.markAllRead')}
              </button>
            )}
          </div>

          {/* 通知列表 */}
          <div className="max-h-96 overflow-y-auto">
            {loading ? (
              <div className="p-8 text-center text-sm text-gray-500 dark:text-slate-400">{t('common.shell.loading')}</div>
            ) : logs.length === 0 ? (
              <div className="p-8 text-center">
                <svg className="w-12 h-12 mx-auto mb-2 text-gray-300 dark:text-slate-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9" />
                </svg>
                <p className="text-sm text-gray-500 dark:text-slate-400">{t('notifications.bell.empty')}</p>
                <p className="text-xs text-gray-400 dark:text-slate-500 mt-1">{t('notifications.bell.emptyHint')}</p>
              </div>
            ) : (
              <div className="py-1">
                {logs.map((log) => {
                  const cfg = getActionConfig(log.action);
                  const link = getEntityLink(log.entityType, log.entityId ?? 0);
                  const isUnread = new Date(log.createdAt).getTime() > lastReadAt;
                  // 解析 oldValues/newValues 简要描述
                  let detail = log.action;
                  try {
                    if (log.newValues) {
                      const parsed = JSON.parse(log.newValues);
                      const keys = Object.keys(parsed);
                      if (keys.length > 0) {
                        detail = keys.slice(0, 3).map((k) => `${k}: ${parsed[k]}`).join(', ');
                      }
                    }
                  } catch {
                    // ignore
                  }
                  return (
                    <button
                      key={log.id}
                      onClick={() => handleClickItem(log)}
                      disabled={!link}
                      className={`w-full flex items-start gap-3 px-4 py-2.5 text-left transition ${
                        link ? 'hover:bg-gray-50 dark:hover:bg-slate-700 cursor-pointer' : 'cursor-default'
                      } ${isUnread ? 'bg-primary-50/40 dark:bg-primary-900/20' : ''}`}
                    >
                      <div className={`w-8 h-8 rounded-lg flex items-center justify-center flex-shrink-0 ${cfg.color}`}>
                        {cfg.icon}
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2">
                          <span className="text-sm font-medium text-gray-900 dark:text-slate-100">
                            {cfg.label ? t(cfg.label) : log.action}
                          </span>
                          <span className="text-xs text-gray-500 dark:text-slate-400">
                            {log.entityType}
                            {log.entityId ? ` #${log.entityId}` : ''}
                          </span>
                          {isUnread && (
                            <span className="w-1.5 h-1.5 bg-primary-500 rounded-full flex-shrink-0" />
                          )}
                        </div>
                        <div className="text-xs text-gray-600 dark:text-slate-300 mt-0.5 line-clamp-2">
                          {detail}
                        </div>
                        <div className="flex items-center gap-2 mt-1">
                          <span className="text-xs text-gray-400 dark:text-slate-500" title={formatDateTime(log.createdAt)}>
                            {relativeTime(log.createdAt)}
                          </span>
                          {log.ipAddress && (
                            <span className="text-xs text-gray-400 dark:text-slate-500 font-mono">
                              · {log.ipAddress}
                            </span>
                          )}
                        </div>
                      </div>
                      {link && (
                        <svg className="w-4 h-4 text-gray-300 dark:text-slate-600 flex-shrink-0 mt-1" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                          <path strokeLinecap="round" strokeLinejoin="round" d="M9 5l7 7-7 7" />
                        </svg>
                      )}
                    </button>
                  );
                })}
              </div>
            )}
          </div>

          {/* 底部 */}
          {logs.length > 0 && (
            <div className="px-4 py-2 border-t border-gray-100 dark:border-slate-700/50 bg-gray-50 dark:bg-slate-900">
              <button
                onClick={() => {
                  navigate('/audit-logs');
                  setOpen(false);
                }}
                className="w-full text-center text-xs text-primary-600 hover:text-primary-700 py-1"
              >
                {t('notifications.bell.viewAllLogs')}
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
