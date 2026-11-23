import { useEffect, useState, useRef, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { searchApi } from '../api';
import type { GlobalSearchResponse } from '../api/types';
import { usePermission } from '../hooks/usePermission';
import { useI18n } from '../context/I18nContext';
import { tBound } from '../i18n/locale';

// 搜索结果统一格式
interface SearchResult {
  id: number;
  type: 'conversation' | 'customer' | 'knowledge';
  title: string;
  subtitle: string;
  snippet: string;
  link: string;
  icon: React.ReactNode;
}

interface GlobalSearchProps {
  open: boolean;
  onClose: () => void;
}

/**
 * 结果分组元数据。`labelKey` 走真源（`as const` 保住字面量类型，
 * 否则属性被抹宽成 `string`，`t()` 会报 TS2345）；模块级常量不能调 hook，故只存 key。
 */
const typeConfig = {
  conversation: {
    labelKey: 'common.globalSearch.type.conversation',
    color: 'bg-blue-50 text-blue-700',
    icon: (
      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
        <path strokeLinecap="round" strokeLinejoin="round" d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
      </svg>
    ),
  },
  customer: {
    labelKey: 'common.globalSearch.type.customer',
    color: 'bg-pink-50 text-pink-700',
    icon: (
      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
        <path strokeLinecap="round" strokeLinejoin="round" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
      </svg>
    ),
  },
  knowledge: {
    labelKey: 'common.globalSearch.type.knowledge',
    color: 'bg-green-50 text-green-700',
    icon: (
      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
        <path strokeLinecap="round" strokeLinejoin="round" d="M12 6.042A8.967 8.967 0 006 3.75c-1.052 0-2.062.18-3 .512v14.25A8.987 8.987 0 016 18c2.305 0 4.408.867 6 2.292m0-14.25a8.966 8.966 0 016-2.292c1.052 0 2.062.18 3 .512v14.25A8.987 8.987 0 0018 18a8.967 8.967 0 00-6 2.292m0-14.25v14.25" />
      </svg>
    ),
  },
} as const;

export default function GlobalSearch({ open, onClose }: GlobalSearchProps) {
  const navigate = useNavigate();
  const { canAccessItem } = usePermission();
  const { t } = useI18n();
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<SearchResult[]>([]);
  const [loading, setLoading] = useState(false);
  const [activeIndex, setActiveIndex] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const listRef = useRef<HTMLDivElement>(null);
  // 搜索请求序号：搜索在途时切换关键词，丢弃过期响应，避免旧结果覆盖新结果
  const searchSeqRef = useRef(0);

  // 按权限过滤搜索结果分组
  const canViewConversations = canAccessItem('/conversations');
  const canViewCustomers = canAccessItem('/customers');
  const canViewKnowledge = canAccessItem('/knowledge');

  // 执行搜索（防抖）
  useEffect(() => {
    if (!open) {
      setQuery('');
      setResults([]);
      setActiveIndex(0);
      return;
    }

    // 自动聚焦
    const focusTimer = setTimeout(() => inputRef.current?.focus(), 50);

    if (!query.trim() || query.trim().length < 2) {
      setResults([]);
      setLoading(false);
      return () => clearTimeout(focusTimer);
    }

    setLoading(true);
    const timer = setTimeout(async () => {
      const seq = ++searchSeqRef.current;
      try {
        const keyword = query.trim();
        const data: GlobalSearchResponse | undefined = await searchApi.global(keyword, 'all', 1, 10);

        // 已切换关键词/组件已重新搜索：丢弃过期响应
        if (seq !== searchSeqRef.current) return;

        if (!data) {
          setResults([]);
          return;
        }

        const next: SearchResult[] = [];

        // 会话结果
        if (canViewConversations && data.conversations) {
          data.conversations.forEach((c) => {
            next.push({
              id: c.id,
              type: 'conversation',
              title: c.subject || tBound('common.globalSearch.noSubject'),
              subtitle: `#${c.id} · ${c.customerEmail || '-'}`,
              snippet: c.snippet || '',
              link: `/conversations/${c.id}`,
              icon: typeConfig.conversation.icon,
            });
          });
        }

        // 客户结果
        if (canViewCustomers && data.customers) {
          data.customers.forEach((c) => {
            next.push({
              id: c.id,
              type: 'customer',
              title: c.fullName || tBound('common.globalSearch.unnamedCustomer'),
              subtitle: c.email || c.phone || '-',
              snippet: c.snippet || '',
              link: `/customers`,
              icon: typeConfig.customer.icon,
            });
          });
        }

        // 知识库结果
        if (canViewKnowledge && data.knowledge) {
          data.knowledge.forEach((a) => {
            next.push({
              id: a.id,
              type: 'knowledge',
              title: a.title,
              subtitle: a.excerpt || '',
              snippet: a.snippet || '',
              link: `/knowledge?article=${a.id}`,
              icon: typeConfig.knowledge.icon,
            });
          });
        }

        setResults(next);
        setActiveIndex(0);
      } catch {
        if (seq === searchSeqRef.current) setResults([]);
      } finally {
        if (seq === searchSeqRef.current) setLoading(false);
      }
    }, 300);

    return () => {
      searchSeqRef.current++; // 使在途搜索请求过期，避免旧响应覆盖新结果
      clearTimeout(focusTimer);
      clearTimeout(timer);
    };
  }, [query, open, canViewConversations, canViewCustomers, canViewKnowledge]);

  // 键盘导航
  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.preventDefault();
        onClose();
        return;
      }
      if (results.length === 0) return;

      if (e.key === 'ArrowDown') {
        e.preventDefault();
        setActiveIndex((i) => (i + 1) % results.length);
      } else if (e.key === 'ArrowUp') {
        e.preventDefault();
        setActiveIndex((i) => (i - 1 + results.length) % results.length);
      } else if (e.key === 'Enter') {
        e.preventDefault();
        const selected = results[activeIndex];
        if (selected) {
          navigate(selected.link);
          onClose();
        }
      }
    },
    [results, activeIndex, navigate, onClose]
  );

  // 滚动激活项到可视区
  useEffect(() => {
    const el = listRef.current?.querySelector(`[data-index="${activeIndex}"]`);
    el?.scrollIntoView({ block: 'nearest' });
  }, [activeIndex]);

  const handleSelect = (r: SearchResult) => {
    navigate(r.link);
    onClose();
  };

  // 按类型分组
  const grouped: Record<string, SearchResult[]> = {};
  results.forEach((r) => {
    if (!grouped[r.type]) grouped[r.type] = [];
    grouped[r.type].push(r);
  });
  // 按权限过滤类型
  const groupOrder: SearchResult['type'][] = (['conversation', 'customer', 'knowledge'] as const).filter(
    (type) => {
      if (type === 'conversation') return canViewConversations;
      if (type === 'customer') return canViewCustomers;
      if (type === 'knowledge') return canViewKnowledge;
      return true;
    }
  );
  let runningIndex = 0;

  return (
    <div
      className={`fixed inset-0 z-50 ${open ? '' : 'pointer-events-none'}`}
      aria-hidden={!open}
    >
      {/* 遮罩 */}
      <div
        className={`absolute inset-0 bg-black/40 transition-opacity ${
          open ? 'opacity-100' : 'opacity-0'
        }`}
        onClick={onClose}
      />

      {/* 搜索面板 */}
      <div
        className={`absolute left-1/2 top-[10vh] -translate-x-1/2 w-[92%] max-w-2xl transition-all ${
          open ? 'opacity-100 translate-y-0' : 'opacity-0 -translate-y-2'
        }`}
        role="dialog"
        aria-modal="true"
        aria-label={t('common.globalSearch.dialogLabel')}
      >
        <div className="bg-white dark:bg-slate-800 rounded-xl shadow-2xl border border-gray-200 dark:border-slate-700 overflow-hidden">
          {/* 搜索输入 */}
          <div className="flex items-center gap-3 px-4 border-b border-gray-100 dark:border-slate-700/50">
            <svg className="w-5 h-5 text-gray-400 dark:text-slate-400 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
            </svg>
            <input
              ref={inputRef}
              type="text"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder={t('common.globalSearch.placeholder')}
              aria-label={t('common.globalSearch.inputLabel')}
              className="flex-1 py-4 text-base text-gray-900 dark:text-slate-100 placeholder-gray-400 dark:placeholder-slate-500 focus:outline-none bg-transparent"
            />
            {loading && (
              <svg className="w-5 h-5 text-gray-400 dark:text-slate-400 animate-spin" fill="none" viewBox="0 0 24 24">
                <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.373 0 0 5.373 0 12h4zm2 5.291A7.962 7.962 0 014 12H0c0 3.042 1.135 5.824 3 7.938l3-2.647z" />
              </svg>
            )}
            <kbd className="hidden sm:inline-flex items-center px-1.5 py-0.5 text-xs text-gray-500 dark:text-slate-400 bg-gray-100 dark:bg-slate-700 border border-gray-200 dark:border-slate-600 rounded">
              ESC
            </kbd>
          </div>

          {/* 搜索结果 */}
          <div ref={listRef} className="max-h-[60vh] overflow-y-auto">
            {!query.trim() ? (
              <div className="p-8 text-center text-sm text-gray-500 dark:text-slate-400">
                <svg className="w-12 h-12 mx-auto mb-3 text-gray-300 dark:text-slate-600" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.5}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                </svg>
                <p>{t('common.globalSearch.hint')}</p>
                <p className="text-xs text-gray-400 dark:text-slate-500 mt-1">{t('common.globalSearch.hintSub')}</p>
              </div>
            ) : query.trim().length < 2 ? (
              <div className="p-8 text-center text-sm text-gray-500 dark:text-slate-400">
                {t('common.globalSearch.minChars')}
              </div>
            ) : loading ? (
              <div className="p-8 text-center text-sm text-gray-500 dark:text-slate-400">{t('common.globalSearch.searching')}</div>
            ) : results.length === 0 ? (
              <div className="p-8 text-center text-sm text-gray-500 dark:text-slate-400">
                {t('common.globalSearch.noResults', { query })}
              </div>
            ) : (
              <div className="py-2">
                {groupOrder.map((type) => {
                  const group = grouped[type];
                  if (!group || group.length === 0) return null;
                  return (
                    <div key={type} className="mb-2">
                      <div className="px-4 py-1 text-xs font-semibold text-gray-500 dark:text-slate-400 uppercase tracking-wider">
                        {t(typeConfig[type].labelKey)} ({group.length})
                      </div>
                      {group.map((r) => {
                        const idx = runningIndex++;
                        const isActive = idx === activeIndex;
                        return (
                          <button
                            key={`${r.type}-${r.id}`}
                            data-index={idx}
                            onMouseEnter={() => setActiveIndex(idx)}
                            onClick={() => handleSelect(r)}
                            className={`w-full flex items-center gap-3 px-4 py-2.5 text-left transition ${
                              isActive ? 'bg-primary-50 dark:bg-primary-900/30' : 'hover:bg-gray-50 dark:hover:bg-slate-700'
                            }`}
                          >
                            <div className={`w-8 h-8 rounded-lg flex items-center justify-center flex-shrink-0 ${typeConfig[r.type].color}`}>
                              {r.icon}
                            </div>
                            <div className="flex-1 min-w-0">
                              <div className={`text-sm font-medium truncate ${isActive ? 'text-primary-900 dark:text-primary-300' : 'text-gray-900 dark:text-slate-100'}`}>
                                {r.title}
                              </div>
                              <div className="text-xs text-gray-500 dark:text-slate-400 truncate">
                                {r.snippet || r.subtitle}
                              </div>
                            </div>
                            <span className={`text-xs px-1.5 py-0.5 rounded ${typeConfig[r.type].color}`}>
                              {t(typeConfig[r.type].labelKey)}
                            </span>
                            {isActive && (
                              <svg className="w-4 h-4 text-primary-600 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                                <path strokeLinecap="round" strokeLinejoin="round" d="M13 7l5 5m0 0l-5 5m5-5H6" />
                              </svg>
                            )}
                          </button>
                        );
                      })}
                    </div>
                  );
                })}
              </div>
            )}
          </div>

          {/* 底部提示 */}
          {results.length > 0 && (
            <div className="px-4 py-2 border-t border-gray-100 dark:border-slate-700/50 bg-gray-50 dark:bg-slate-900 flex items-center justify-between text-xs text-gray-500 dark:text-slate-400">
              <div className="flex items-center gap-3">
                <span className="flex items-center gap-1">
                  <kbd className="px-1.5 py-0.5 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded">↑</kbd>
                  <kbd className="px-1.5 py-0.5 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded">↓</kbd>
                  {t('common.globalSearch.navigateHint')}
                </span>
                <span className="flex items-center gap-1">
                  <kbd className="px-1.5 py-0.5 bg-white dark:bg-slate-800 border border-gray-200 dark:border-slate-600 rounded">↵</kbd>
                  {t('common.globalSearch.selectHint')}
                </span>
              </div>
              <span>{t('common.globalSearch.resultCount', { count: results.length })}</span>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
