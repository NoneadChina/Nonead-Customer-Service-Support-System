import { useState, useEffect, useCallback, useRef } from 'react';
import { useTimedToast } from './useTimedToast';
import { tBound } from '../i18n/locale';

export interface PagedList<T> {
  data: T[];
  totalItems: number;
  totalPages: number;
  currentPage: number;
}

export interface TrashQuery {
  page: number;
  pageSize: number;
  keyword: string;
}

export interface UseCrudPageOptions<T, Q extends object> {
  defaultQuery: Q;
  defaultTrashQuery?: TrashQuery;
  fetchList: (query: Q) => Promise<PagedList<T> | null | undefined>;
  fetchTrashList?: (query: TrashQuery) => Promise<PagedList<T> | null | undefined>;
  fetchTrashCount?: () => Promise<{ count?: number } | null | undefined>;
  deps?: unknown[];
}

export interface UseCrudPageResult<T, Q extends object> {
  query: Q;
  setQuery: React.Dispatch<React.SetStateAction<Q>>;
  list: PagedList<T> | null;
  loading: boolean;
  refresh: () => void;
  handleFilterChange: <K extends keyof Q>(key: K, value: Q[K]) => void;
  handlePageChange: (page: number) => void;

  editing: T | null;
  showEditor: boolean;
  openEditor: (item?: T) => void;
  closeEditor: () => void;

  deleteTarget: T | null;
  setDeleteTarget: React.Dispatch<React.SetStateAction<T | null>>;

  trash: {
    open: boolean;
    setOpen: React.Dispatch<React.SetStateAction<boolean>>;
    query: TrashQuery;
    setQuery: React.Dispatch<React.SetStateAction<TrashQuery>>;
    list: PagedList<T> | null;
    loading: boolean;
    count: number;
    actionLoading: boolean;
    setActionLoading: React.Dispatch<React.SetStateAction<boolean>>;
    refresh: () => void;
    refreshCount: () => void;
  };

  toast: { message: { type: 'success' | 'error' | 'info' | 'warning'; text: string } | null; show: (msg: { type: 'success' | 'error' | 'info' | 'warning'; text: string }) => void };
}

export function useCrudPage<T, Q extends object>(
  options: UseCrudPageOptions<T, Q>,
): UseCrudPageResult<T, Q> {
  const {
    defaultQuery,
    defaultTrashQuery = { page: 1, pageSize: 10, keyword: '' },
    fetchList,
    fetchTrashList,
    fetchTrashCount,
    deps = [],
  } = options;

  const [query, setQuery] = useState<Q>(defaultQuery);
  const [list, setList] = useState<PagedList<T> | null>(null);
  const [loading, setLoading] = useState(false);

  const [editing, setEditing] = useState<T | null>(null);
  const [showEditor, setShowEditor] = useState(false);

  const [deleteTarget, setDeleteTarget] = useState<T | null>(null);

  const [trashOpen, setTrashOpen] = useState(false);
  const [trashQuery, setTrashQuery] = useState<TrashQuery>(defaultTrashQuery);
  const [trashList, setTrashList] = useState<PagedList<T> | null>(null);
  const [trashLoading, setTrashLoading] = useState(false);
  const [trashCount, setTrashCount] = useState(0);
  const [trashActionLoading, setTrashActionLoading] = useState(false);

  const { message: toast, show: showToast } = useTimedToast();

  // 用 ref 保存最新的 fetch 函数，避免调用方传入的内联函数每次渲染创建新引用
  // 导致 useCallback 依赖失效 → effect 无限触发 → loading 状态闪烁
  const fetchListRef = useRef(fetchList);
  fetchListRef.current = fetchList;
  const fetchTrashListRef = useRef(fetchTrashList);
  fetchTrashListRef.current = fetchTrashList;
  const fetchTrashCountRef = useRef(fetchTrashCount);
  fetchTrashCountRef.current = fetchTrashCount;

  // S8: 追踪所有在途请求的取消函数，组件卸载时统一执行，
  // 防止手动 refresh() 的请求在卸载后仍 setState（卸载后 setState 残留/异步未清理）
  const pendingCancelsRef = useRef<Array<() => void>>([]);
  const trackCancellation = useCallback((cancel: () => void) => {
    pendingCancelsRef.current.push(cancel);
  }, []);

  useEffect(() => {
    return () => {
      pendingCancelsRef.current.forEach((cancel) => cancel());
      pendingCancelsRef.current = [];
    };
  }, []);

  const refreshList = useCallback(() => {
    setLoading(true);
    let cancelled = false;
    const cancel = () => { cancelled = true; };
    trackCancellation(cancel);
    fetchListRef
      .current(query)
      .then((data) => { if (!cancelled) setList(data ?? null); })
      .catch((err: unknown) => {
        console.error('加载列表失败:', err);
        // 文案用 `tBound` 在**失败发生的那一刻**取当前语言：本 hook 是纯逻辑模块，
        // 不引 `useI18n()` 就不会把 `t` 的身引进 useCallback 依赖（那会让语言切换
        // 重触发一次列表请求），也不影响既有测试（无需 Provider）。
        if (!cancelled) showToast({ type: 'error', text: tBound('common.loadListFailed') });
      })
      .finally(() => { if (!cancelled) setLoading(false); });
    // 返回的 cancel token 主要给 effect cleanup 使用；实际外部手动调用无需取消
    return cancel;
  }, [query, showToast, trackCancellation]);

  const refreshTrashList = useCallback(() => {
    const fn = fetchTrashListRef.current;
    if (!fn) return;
    setTrashLoading(true);
    let cancelled = false;
    const cancel = () => { cancelled = true; };
    trackCancellation(cancel);
    fn(trashQuery)
      .then((data) => { if (!cancelled) setTrashList(data ?? null); })
      .catch(() => { if (!cancelled) showToast({ type: 'error', text: tBound('common.loadTrashFailed') }); })
      .finally(() => { if (!cancelled) setTrashLoading(false); });
    return cancel;
  }, [trashQuery, showToast, trackCancellation]);

  const refreshTrashCount = useCallback(() => {
    const fn = fetchTrashCountRef.current;
    if (!fn) return;
    let cancelled = false;
    const cancel = () => { cancelled = true; };
    trackCancellation(cancel);
    fn()
      .then((res) => {
        if (cancelled) return;
        const count = res?.count ?? 0;
        setTrashCount(typeof count === 'number' ? count : 0);
      })
      .catch((err: unknown) => {
        // S8: 垃圾箱计数错误不再静默吞掉，记录日志便于排查
        if (!cancelled) console.error('加载垃圾箱计数失败:', err);
      });
    return cancel;
  }, [trackCancellation]);

  // 列表自动刷新（query / deps 变化时）
  useEffect(() => {
    let destroyed = false;
    setLoading(true);
    fetchListRef
      .current(query)
      .then((data) => { if (!destroyed) setList(data ?? null); })
      .catch((err: unknown) => {
        console.error('加载列表失败:', err);
        if (!destroyed) showToast({ type: 'error', text: tBound('common.loadListFailed') });
      })
      .finally(() => { if (!destroyed) setLoading(false); });
    return () => { destroyed = true; };
     
    // 原因：deps 是调用方传入的动态依赖数组，无法静态推断其元素，
    // 因此无法满足 react-hooks/exhaustive-deps 的静态检查要求；
    // 这里显式展开 deps 以保证 effect 在依赖变化时正确触发。
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query, ...deps, showToast]);

  // 垃圾箱自动刷新
  useEffect(() => {
    if (!trashOpen) return;
    const fn = fetchTrashListRef.current;
    if (!fn) return;
    let destroyed = false;
    setTrashLoading(true);
    fn(trashQuery)
      .then((data) => { if (!destroyed) setTrashList(data ?? null); })
      .catch(() => { if (!destroyed) showToast({ type: 'error', text: tBound('common.loadTrashFailed') }); })
      .finally(() => { if (!destroyed) setTrashLoading(false); });
    return () => { destroyed = true; };
  }, [trashOpen, trashQuery, showToast]);

  // 初始化垃圾箱计数（只执行一次，mount 后调用，后续通过 refreshCount 手动刷新）
  useEffect(() => {
    let destroyed = false;
    const fn = fetchTrashCountRef.current;
    if (fn) {
      fn()
        .then((res) => {
          if (destroyed) return;
          const count = res?.count ?? 0;
          setTrashCount(typeof count === 'number' ? count : 0);
        })
        .catch((err: unknown) => {
          // S8: 垃圾箱计数错误不再静默吞掉，记录日志便于排查
          if (!destroyed) console.error('加载垃圾箱计数失败:', err);
        });
    }
    return () => { destroyed = true; };
     
    // 原因：初始化垃圾箱计数仅需在 mount 时执行一次，
    // 后续刷新通过 refreshCount 手动触发，不应依赖任何外部值。
  }, []);

  const handleFilterChange = useCallback(<K extends keyof Q>(key: K, value: Q[K]) => {
    setQuery((q) => ({ ...q, [key]: value, page: 1 } as Q));
  }, []);

  const handlePageChange = useCallback((page: number) => {
    setQuery((q) => ({ ...q, page } as Q));
  }, []);

  const openEditor = useCallback((item?: T) => {
    setEditing(item ?? null);
    setShowEditor(true);
  }, []);

  const closeEditor = useCallback(() => {
    setShowEditor(false);
    setEditing(null);
  }, []);

  return {
    query,
    setQuery,
    list,
    loading,
    refresh: refreshList,
    handleFilterChange,
    handlePageChange,

    editing,
    showEditor,
    openEditor,
    closeEditor,

    deleteTarget,
    setDeleteTarget,

    trash: {
      open: trashOpen,
      setOpen: setTrashOpen,
      query: trashQuery,
      setQuery: setTrashQuery,
      list: trashList,
      loading: trashLoading,
      count: trashCount,
      actionLoading: trashActionLoading,
      setActionLoading: setTrashActionLoading,
      refresh: refreshTrashList,
      refreshCount: refreshTrashCount,
    },

    toast: { message: toast, show: showToast },
  };
}
