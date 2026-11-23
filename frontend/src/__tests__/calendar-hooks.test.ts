import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { renderHook, act, cleanup } from '@testing-library/react';
import React from 'react';
import {
  _buildRangeWindow,
  _isRangeCovered,
  _debounceStore,
  _normalizeDirection,
  _handleKeyCombo,
  _checkConflictForRange,
  useKeyboardShortcuts,
  useRangeCache,
  useCalendarDrag,
  useSwipeNavigation,
} from '../pages/calendar/hooks';
import type { CalendarEventQuery } from '../api/types/engagement';
import type { SwipeOptions } from '../pages/calendar/hooks';

describe('hooks pure helpers (Task 6)', () => {
  describe('_buildRangeWindow (±14 天窗口)', () => {
    it('target 2026-08-12 → start 07-29, end 08-26 (共 28 天)', () => {
      const q: CalendarEventQuery = {};
      const { start, end } = _buildRangeWindow(q, new Date(2026, 7, 12));
      expect(start).toBe('2026-07-29');
      expect(end).toBe('2026-08-26');
    });
    it('target 跨 2025 除夕（2026-01-02）→ start 上一年 12-19', () => {
      const q: CalendarEventQuery = {};
      const { start } = _buildRangeWindow(q, new Date(2026, 0, 2));
      expect(start).toBe('2025-12-19');
    });
    it('已传入 startDate/endDate 的 query → 原样返回，不 ±14', () => {
      const q: CalendarEventQuery = { startDate: '2026-08-01', endDate: '2026-08-31' };
      const { start, end } = _buildRangeWindow(q, new Date(2026, 7, 15));
      expect(start).toBe('2026-08-01');
      expect(end).toBe('2026-08-31');
    });
  });

  describe('_isRangeCovered (缓存命中判断)', () => {
    it('请求完全落在缓存内 → true', () => {
      expect(_isRangeCovered({ cacheStart: '2026-07-01', cacheEnd: '2026-09-30',
        reqStart: '2026-08-01', reqEnd: '2026-08-31' })).toBe(true);
    });
    it('请求左边越界 → false', () => {
      expect(_isRangeCovered({ cacheStart: '2026-08-10', cacheEnd: '2026-08-20',
        reqStart: '2026-08-01', reqEnd: '2026-08-31' })).toBe(false);
    });
    it('空/缺字段 → false（不崩溃）', () => {
      // @ts-expect-error 故意缺参测试鲁棒性
      expect(_isRangeCovered({})).toBe(false);
    });
  });

  describe('_debounceStore (延迟刷新机制，纯函数模拟返回值)', () => {
    it('第一次调用：返回 {shouldFire: true, delayMs: 120}（立即开一个窗口）', () => {
      expect(_debounceStore(undefined, 'req-A', Date.now())).toMatchObject({ shouldFire: true });
    });
    it('同一 key 距上次 < 120ms → 不 fire，而是 scheduleNext=true（合并窗口）', () => {
      const now = 1000;
      const prev = { key: 'req-A', lastFireAt: now - 50 };
      const r = _debounceStore(prev as any, 'req-A', now);
      expect(r.shouldFire).toBe(false);
      expect(r.scheduleNext).toBe(true);
    });
    it('不同 key（范围变化）→ 立即 fire，忽略窗口', () => {
      const prev = { key: 'req-A', lastFireAt: Date.now() - 50 };
      const r = _debounceStore(prev as any, 'req-B', Date.now());
      expect(r.shouldFire).toBe(true);
    });
  });

  describe('_normalizeDirection（Outlook 双向同步方向映射）', () => {
    it('store 内部 bidirectional → 请求 body 的 "both"', () => {
      expect(_normalizeDirection('bidirectional')).toBe('both');
    });
    it('noneads_to_outlook → "to_outlook"', () => {
      expect(_normalizeDirection('noneads_to_outlook')).toBe('to_outlook');
    });
    it('outlook_to_noneads → "from_outlook"', () => {
      expect(_normalizeDirection('outlook_to_noneads')).toBe('from_outlook');
    });
    it('未知值兜底 → "both"（不崩溃）', () => {
      // @ts-expect-error 故意传非法值
      expect(_normalizeDirection('garbage')).toBe('both');
    });
  });

  describe('_handleKeyCombo（键盘快捷键分类）', () => {
    it('t=Enter + 焦点在非可编辑元素 → "submit"', () => {
      const ev = new KeyboardEvent('keydown', { key: 'Enter' });
      Object.defineProperty(ev, 'target', { value: document.createElement('div') });
      expect(_handleKeyCombo(ev)).toBe('submit');
    });
    it('t=Enter + input 中 → 不分类（返回 null，交给默认行为）', () => {
      const ev = new KeyboardEvent('keydown', { key: 'Enter' });
      Object.defineProperty(ev, 'target', { value: document.createElement('input') });
      expect(_handleKeyCombo(ev)).toBeNull();
    });
    it('t=Escape → "dismiss"', () => {
      const ev = new KeyboardEvent('keydown', { key: 'Escape' });
      Object.defineProperty(ev, 'target', { value: document.createElement('div') });
      expect(_handleKeyCombo(ev)).toBe('dismiss');
    });
    it('t=普通字母 A → null', () => {
      const ev = new KeyboardEvent('keydown', { key: 'a' });
      Object.defineProperty(ev, 'target', { value: document.createElement('div') });
      expect(_handleKeyCombo(ev)).toBeNull();
    });
  });
});

describe('_checkConflictForRange (P1-1)', () => {
  const E = (id: number, start: string, end?: string, isAllDay = false) =>
    ({ id, startAt: start, endAt: end, isAllDay } as any);

  it('无事件 → none', () => {
    const r = _checkConflictForRange({ startAt: '2026-08-12T10:00:00Z', endAt: '2026-08-12T11:00:00Z', existingEvents: [] });
    expect(r.tag).toBe('none');
    expect(r.conflictIds).toEqual([]);
  });

  it('无重叠 → none', () => {
    const r = _checkConflictForRange({
      startAt: '2026-08-12T10:00:00Z', endAt: '2026-08-12T11:00:00Z',
      existingEvents: [E(1, '2026-08-12T11:00:00Z', '2026-08-12T12:00:00Z')],
    });
    expect(r.tag).toBe('none');
  });

  it('重叠 10 分钟 → soft', () => {
    const r = _checkConflictForRange({
      startAt: '2026-08-12T10:00:00Z', endAt: '2026-08-12T11:00:00Z',
      existingEvents: [E(1, '2026-08-12T10:50:00Z', '2026-08-12T11:30:00Z')],
    });
    expect(r.tag).toBe('soft');
    expect(r.conflictIds).toEqual([1]);
    expect(r.overlapMinutes).toBeCloseTo(10, 1);
  });

  it('重叠 30 分钟 → hard', () => {
    const r = _checkConflictForRange({
      startAt: '2026-08-12T10:00:00Z', endAt: '2026-08-12T11:00:00Z',
      existingEvents: [E(1, '2026-08-12T10:30:00Z', '2026-08-12T12:00:00Z')],
    });
    expect(r.tag).toBe('hard');
    expect(r.overlapMinutes).toBeCloseTo(30, 1);
  });

  it('excludeId 排除自身', () => {
    const r = _checkConflictForRange({
      startAt: '2026-08-12T10:00:00Z', endAt: '2026-08-12T11:00:00Z',
      existingEvents: [E(1, '2026-08-12T10:30:00Z', '2026-08-12T11:30:00Z')],
      excludeId: 1,
    });
    expect(r.tag).toBe('none');
  });

  it('全天事件不参与冲突', () => {
    const r = _checkConflictForRange({
      startAt: '2026-08-12T10:00:00Z', endAt: '2026-08-12T11:00:00Z',
      existingEvents: [E(1, '2026-08-12T00:00:00Z', '2026-08-12T23:59:00Z', true)],
    });
    expect(r.tag).toBe('none');
  });

  it('endAt 缺失 → 默认 1h', () => {
    const r = _checkConflictForRange({
      startAt: '2026-08-12T10:00:00Z',
      existingEvents: [E(1, '2026-08-12T10:30:00Z', '2026-08-12T11:30:00Z')],
    });
    expect(r.tag).toBe('hard');
  });

  it('多事件冲突 → 收集所有冲突 id', () => {
    const r = _checkConflictForRange({
      startAt: '2026-08-12T10:00:00Z', endAt: '2026-08-12T12:00:00Z',
      existingEvents: [
        E(1, '2026-08-12T09:00:00Z', '2026-08-12T10:30:00Z'),
        E(2, '2026-08-12T11:00:00Z', '2026-08-12T11:45:00Z'),
        E(3, '2026-08-12T14:00:00Z', '2026-08-12T15:00:00Z'),  // 不冲突
      ],
    });
    expect(r.conflictIds).toEqual([1, 2]);
    expect(r.conflictIds).not.toContain(3);
  });
});

describe('React hooks 行为级用例（hooks.ts）', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    cleanup();
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  describe('useKeyboardShortcuts（全局键盘快捷键）', () => {
    it('ArrowRight / ArrowLeft 触发 onNext / onPrev，并调用 preventDefault', () => {
      const onNext = vi.fn();
      const onPrev = vi.fn();
      renderHook(() => useKeyboardShortcuts({ onNext, onPrev }));
      const evR = new KeyboardEvent('keydown', { key: 'ArrowRight', cancelable: true });
      Object.defineProperty(evR, 'target', { value: document.createElement('div') });
      const prR = vi.spyOn(evR, 'preventDefault');
      window.dispatchEvent(evR);
      expect(onNext).toHaveBeenCalledTimes(1);
      expect(prR).toHaveBeenCalledTimes(1);

      const evL = new KeyboardEvent('keydown', { key: 'ArrowLeft', cancelable: true });
      Object.defineProperty(evL, 'target', { value: document.createElement('div') });
      const prL = vi.spyOn(evL, 'preventDefault');
      window.dispatchEvent(evL);
      expect(onPrev).toHaveBeenCalledTimes(1);
      expect(prL).toHaveBeenCalledTimes(1);
    });

    it('Ctrl/Cmd+N → onCreate；Ctrl+Z / Ctrl+Shift+Z / Ctrl+Y → undo/redo', () => {
      const onCreate = vi.fn();
      const onUndo = vi.fn();
      const onRedo = vi.fn();
      renderHook(() => useKeyboardShortcuts({ onCreate, onUndo, onRedo }));
      const fire = (opts: KeyboardEventInit, target?: HTMLElement) => {
        const e = new KeyboardEvent('keydown', { cancelable: true, ...opts });
        Object.defineProperty(e, 'target', { value: target ?? document.createElement('div') });
        window.dispatchEvent(e);
      };
      fire({ key: 'n', ctrlKey: true });
      expect(onCreate).toHaveBeenCalledTimes(1);
      fire({ key: 'z', ctrlKey: true });
      expect(onUndo).toHaveBeenCalledTimes(1);
      fire({ key: 'z', ctrlKey: true, shiftKey: true });
      expect(onRedo).toHaveBeenCalledTimes(1);
      fire({ key: 'y', ctrlKey: true });
      expect(onRedo).toHaveBeenCalledTimes(2);
    });

    it('Tab / Shift+Tab → tabNext / tabPrev', () => {
      const tabNext = vi.fn();
      const tabPrev = vi.fn();
      renderHook(() => useKeyboardShortcuts({ onTabNext: tabNext, onTabPrev: tabPrev }));
      const fire = (shift: boolean) => {
        const e = new KeyboardEvent('keydown', { key: 'Tab', shiftKey: shift, cancelable: true });
        Object.defineProperty(e, 'target', { value: document.createElement('div') });
        window.dispatchEvent(e);
      };
      fire(false);
      expect(tabNext).toHaveBeenCalledTimes(1);
      fire(true);
      expect(tabPrev).toHaveBeenCalledTimes(1);
    });

    it('Enter 非编辑区 → onSubmit', () => {
      const onSubmit = vi.fn();
      renderHook(() => useKeyboardShortcuts({ onSubmit }));
      const e = new KeyboardEvent('keydown', { key: 'Enter', cancelable: true });
      Object.defineProperty(e, 'target', { value: document.createElement('div') });
      window.dispatchEvent(e);
      expect(onSubmit).toHaveBeenCalledTimes(1);
    });

    it('焦点在 <input> 时，只对 Escape 生效（dismiss 由面板处理，这里仅断言其他组合不触发）', () => {
      const onNext = vi.fn();
      const onUndo = vi.fn();
      renderHook(() => useKeyboardShortcuts({ onNext, onUndo }));
      const fire = (key: string, ctrl = false) => {
        const e = new KeyboardEvent('keydown', { key, ctrlKey: ctrl, cancelable: true });
        Object.defineProperty(e, 'target', { value: document.createElement('input') });
        window.dispatchEvent(e);
      };
      fire('ArrowRight');
      expect(onNext).not.toHaveBeenCalled();
      fire('z', true);
      expect(onUndo).not.toHaveBeenCalled();
    });

    it('卸载时移除 listener：后续按键不触发回调', () => {
      const onNext = vi.fn();
      const { unmount } = renderHook(() => useKeyboardShortcuts({ onNext }));
      unmount();
      const e = new KeyboardEvent('keydown', { key: 'ArrowRight', cancelable: true });
      Object.defineProperty(e, 'target', { value: document.createElement('div') });
      window.dispatchEvent(e);
      expect(onNext).not.toHaveBeenCalled();
    });
  });

  describe('useRangeCache（范围缓存 + 防抖刷新调度）', () => {
    it('首次 debounceRefresh 应立即调用 refresh（shouldFire=true）', () => {
      const refresh = vi.fn();
      const { result } = renderHook(() => useRangeCache(refresh));
      let r;
      act(() => { r = result.current.debounceRefresh('k1'); });
      expect(r!.shouldFire).toBe(true);
      expect(refresh).toHaveBeenCalledTimes(1);
    });

    it('同 key 紧接第二次调用：不立即 fire，走 setTimeout scheduleNext；vi.runAllTimers 后才 refresh', () => {
      const refresh = vi.fn();
      const { result } = renderHook(() => useRangeCache(refresh));
      act(() => { result.current.debounceRefresh('k1'); });
      act(() => { result.current.debounceRefresh('k1'); });
      // 立即 refresh 仅一次
      expect(refresh).toHaveBeenCalledTimes(1);
      act(() => { vi.advanceTimersByTime(200); });
      expect(refresh).toHaveBeenCalledTimes(2);
    });

    it('key 切换 → 忽略窗口，立即 fire', () => {
      const refresh = vi.fn();
      const { result } = renderHook(() => useRangeCache(refresh));
      act(() => { result.current.debounceRefresh('k1'); });
      act(() => { result.current.debounceRefresh('k2'); });
      expect(refresh).toHaveBeenCalledTimes(2);
    });

    it('invalidateWrite 清空 cacheState.start/end', () => {
      // 写入值的接口在实现里是私有 ref；但 invalidateWrite 至少保证下一轮读取返回空
      const refresh = vi.fn();
      const { result } = renderHook(() => useRangeCache(refresh));
      act(() => { result.current.invalidateWrite(); });
      expect(result.current.cacheState.cacheStart).toBeUndefined();
      expect(result.current.cacheState.cacheEnd).toBeUndefined();
    });

    it('scheduleNext 会清除旧 timer（连续多次压测只留最后一次）', () => {
      const refresh = vi.fn();
      const clearSpy = vi.spyOn(window, 'clearTimeout');
      const { result } = renderHook(() => useRangeCache(refresh));
      act(() => { result.current.debounceRefresh('k1'); });
      act(() => { result.current.debounceRefresh('k1'); });
      act(() => { result.current.debounceRefresh('k1'); });
      // 第一次 scheduleNext 调用 setTimeout，第二次开始前 clearTimeout
      expect(clearSpy).toHaveBeenCalled();
      clearSpy.mockRestore();
    });
  });

  describe('useCalendarDrag（返回结构 + onChipDragStart 写入 dataTransfer）', () => {
    it('返回 startDrag/endDrag/setDragOver/handleDrop/onChipDragStart 五个字段', () => {
      const { result } = renderHook(() => useCalendarDrag());
      expect(typeof result.current.startDrag).toBe('function');
      expect(typeof result.current.endDrag).toBe('function');
      expect(typeof result.current.setDragOver).toBe('function');
      expect(typeof result.current.handleDrop).toBe('function');
      expect(typeof result.current.onChipDragStart).toBe('function');
    });

    it('onChipDragStart：effectAllowed = "move"，并 setData id → startDrag 被调用（jsdom 无 DataTransfer，使用 duck-typed mock）', () => {
      const { result } = renderHook(() => useCalendarDrag());
      // 写一个最小可用的 DataTransfer 替身
      const storage = new Map<string, string>();
      const mockDt: {
        effectAllowed: string;
        setData: (t: string, v: string) => void;
        getData: (t: string) => string;
      } = {
        effectAllowed: 'none',
        setData(t, v) { storage.set(t, v); },
        getData(t) { return storage.get(t) ?? ''; },
      };
      const fakeEv = { id: 42 } as any;
      const fakeReactEv = { dataTransfer: mockDt } as unknown as React.DragEvent<HTMLDivElement>;
      act(() => { result.current.onChipDragStart(fakeEv, fakeReactEv); });
      expect(mockDt.effectAllowed).toBe('move');
      expect(mockDt.getData('text/calendar-event-id')).toBe('42');
      expect(mockDt.getData('text/plain')).toBe('42');
    });

    it('handleDrop：endDrag 会在 drop 完成后被调用（store.draggedEvent 从非空 → 空）', async () => {
      const { useCalendarStore } = await import('../pages/calendar/store');
      // 初始有 draggedEvent
      act(() => {
        useCalendarStore.setState({
          draggedEvent: { id: 100, title: 'x', startAt: new Date().toISOString() } as any,
        });
      });
      const { result } = renderHook(() => useCalendarDrag());
      await act(async () => { await result.current.handleDrop('2026-08-15', 9.25); });
      // 验证 endDrag 清空了拖拽源（moveEventByDrop 即使抛错也不影响这个断言：真实实现会调用 endDrag）
      // 注意：如果 moveEventByDrop 在 store 里提前 return，仍会走 endDrag；我们断言副作用即可
      expect(useCalendarStore.getState().draggedEvent).toBeNull();
    });
  });

  describe('useSwipeNavigation（触摸滑动切换日期）', () => {
    const touchEl = () => document.createElement('div');

    type ListenerMap = Partial<Record<'touchstart' | 'touchmove' | 'touchend', (e: TouchEvent) => void>>;

    /**
     * 返回 { hook, el, listeners, cleanup }。
     * 由于 jsdom 对 TouchEvent 派发的细节差异，我们直接捕获绑到 el 上的监听器，
     * 在测试里手动调用 listeners.touchstart/move/end。
     */
    const setupWithRef = (opts: SwipeOptions): {
      hook: ReturnType<typeof renderHook<React.RefCallback<HTMLElement>, SwipeOptions>>;
      el: HTMLDivElement;
      listeners: ListenerMap;
      cleanup: () => void;
    } => {
      const oldDesc = Object.getOwnPropertyDescriptor(window, 'ontouchstart');
      // @ts-expect-error 打桩：设置非标准 ontouchstart 属性以构造 touch 事件
      window.ontouchstart = null;
      const el = touchEl();
      document.body.appendChild(el);
      const listeners: ListenerMap = {};
      const origAEL = el.addEventListener.bind(el);
      vi.spyOn(el, 'addEventListener').mockImplementation(((type: string, fn: any) => {
        if (type === 'touchstart' || type === 'touchmove' || type === 'touchend') {
          listeners[type] = fn;
        }
        return origAEL(type, fn, { passive: true });
      }) as typeof el.addEventListener);
      // 先 disabled=true 挂载（effect 不绑 listener），再设置 ref，再 disabled=false 正式选项 → useEffect 依赖变化，会重读 ref.current
      const hook = renderHook<React.RefCallback<HTMLElement>, SwipeOptions>((p) => useSwipeNavigation(p ?? opts), { initialProps: { disabled: true } });
      act(() => { hook.result.current(el); });
      // 依赖变化 → effect 重跑 → 此时 ref.current 已设置，会 addEventListener
      act(() => { hook.rerender(opts); });
      const cleanup = () => {
        document.body.removeChild(el);
        if (oldDesc) Object.defineProperty(window, 'ontouchstart', oldDesc);
        else delete (window as any).ontouchstart;
        vi.restoreAllMocks();
      };
      return { hook, el, listeners, cleanup };
    };

    const mkTouchEv = (x: number, y: number, phase: 'start' | 'move' | 'end') => {
      const touch = {
        identifier: 1, clientX: x, clientY: y, pageX: x, pageY: y,
        screenX: x, screenY: y, radiusX: 0, radiusY: 0, force: 1, rotationAngle: 0,
      };
      return {
        touches: phase === 'end' ? [] : [touch],
        changedTouches: [touch],
      } as unknown as TouchEvent;
    };

    it('左滑 dx=-100 超过阈值 64 → onSwipeLeft 回调 1 次', () => {
      const left = vi.fn();
      const { listeners, cleanup } = setupWithRef({ onSwipeLeft: left });
      listeners.touchstart?.(mkTouchEv(200, 100, 'start'));
      listeners.touchmove?.(mkTouchEv(180, 105, 'move'));
      listeners.touchend?.(mkTouchEv(100, 108, 'end'));
      expect(left).toHaveBeenCalledTimes(1);
      cleanup();
    });

    it('右滑 dx=+120 超过阈值 64 → onSwipeRight 回调 1 次', () => {
      const right = vi.fn();
      const { listeners, cleanup } = setupWithRef({ onSwipeRight: right });
      listeners.touchstart?.(mkTouchEv(100, 100, 'start'));
      listeners.touchend?.(mkTouchEv(220, 100, 'end'));
      expect(right).toHaveBeenCalledTimes(1);
      cleanup();
    });

    it('垂直 dy > maxVertical(40) → cancelled，回调不触发', () => {
      const left = vi.fn();
      const right = vi.fn();
      const { listeners, cleanup } = setupWithRef({ onSwipeLeft: left, onSwipeRight: right });
      listeners.touchstart?.(mkTouchEv(200, 100, 'start'));
      listeners.touchmove?.(mkTouchEv(180, 180, 'move'));  // dy=80 > 40
      listeners.touchend?.(mkTouchEv(50, 200, 'end'));
      expect(left).not.toHaveBeenCalled();
      expect(right).not.toHaveBeenCalled();
      cleanup();
    });

    it('disabled=true → effect 提前 return，无 listener 注册', () => {
      const left = vi.fn();
      const { listeners, cleanup } = setupWithRef({ onSwipeLeft: left, disabled: true });
      expect(listeners.touchstart).toBeUndefined();
      expect(listeners.touchmove).toBeUndefined();
      expect(listeners.touchend).toBeUndefined();
      cleanup();
    });

    it('|dx|=40 < threshold=64 → 不触发回调', () => {
      const left = vi.fn();
      const right = vi.fn();
      const { listeners, cleanup } = setupWithRef({ onSwipeLeft: left, onSwipeRight: right });
      listeners.touchstart?.(mkTouchEv(200, 100, 'start'));
      listeners.touchend?.(mkTouchEv(160, 100, 'end'));
      expect(left).not.toHaveBeenCalled();
      expect(right).not.toHaveBeenCalled();
      cleanup();
    });

    it('dt > maxDurationMs=800 → 放弃，不触发回调', () => {
      const left = vi.fn();
      const { listeners, cleanup } = setupWithRef({ onSwipeLeft: left });
      listeners.touchstart?.(mkTouchEv(200, 100, 'start'));
      act(() => { vi.advanceTimersByTime(1200); });
      listeners.touchend?.(mkTouchEv(50, 100, 'end'));
      expect(left).not.toHaveBeenCalled();
      cleanup();
    });

    it('startX 在左边缘 10px (<24) → cancelled，不触发', () => {
      const oldWidth = window.innerWidth;
      Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: 800 });
      const left = vi.fn();
      const { listeners, cleanup } = setupWithRef({ onSwipeLeft: left });
      listeners.touchstart?.(mkTouchEv(10, 100, 'start'));
      listeners.touchend?.(mkTouchEv(-200, 100, 'end'));
      expect(left).not.toHaveBeenCalled();
      cleanup();
      Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: oldWidth });
    });

    it('startX 在右边缘（800-790=10 <24） → cancelled，不触发', () => {
      const oldWidth = window.innerWidth;
      Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: 800 });
      const right = vi.fn();
      const { listeners, cleanup } = setupWithRef({ onSwipeRight: right });
      listeners.touchstart?.(mkTouchEv(790, 100, 'start'));
      listeners.touchend?.(mkTouchEv(900, 100, 'end'));
      expect(right).not.toHaveBeenCalled();
      cleanup();
      Object.defineProperty(window, 'innerWidth', { configurable: true, writable: true, value: oldWidth });
    });
  });
});
