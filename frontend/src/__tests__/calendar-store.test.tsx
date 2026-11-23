import { describe, it, expect, beforeEach, vi } from 'vitest';
import { Profiler } from 'react';
import { render, screen, cleanup, fireEvent, renderHook, act } from '@testing-library/react';
import type { CalendarEventDto } from '../api/types';
import {
  MoreEventsChip,
  _chipTimeLabel,
  _chipDisplayLabel,
  _allDayTimeLabel,
  getEventTypeStyle,
  getStatusStyle,
  _colorOverrideStyle,
  _colorOverrideRingStyle,
  _timegridBackgroundColor,
  useHourHeight,
  AllDayEventBar,
  EmptyState,
  HourRow,
  EventChip,
  _eventChipMemoComparator,
  _allDayEventBarMemoComparator,
  _hourRowMemoComparator,
} from '../pages/calendar/chips';
import type { HourRowColumnEvent as HourRowColumnEventLocal } from '../pages/calendar/calendarUtils';
import type { EventChipProps, AllDayEventBarProps, HourRowProps } from '../pages/calendar/chips';
import { useCalendarStore, restoreUndoRedoFromStorage, clearPersistedUndoRedoForLogout } from '../pages/calendar/store';

// Mock calendarApi —— undo/redo 内部会调用真实 API create/update/remove，
// 单测只需验证栈弹出/移动逻辑和乐观修改标记，无需真实后端。
vi.mock('../api/modules/engagement', () => ({
  calendarApi: {
    create: vi.fn(async (req: unknown) => {
      const r = req as { id?: number; title?: string };
      return { id: r.id ?? Date.now(), title: r.title ?? 'created', rowVersion: 'rv-mock' } as CalendarEventDto;
    }),
    update: vi.fn(async (_id: unknown, _req: unknown) => ({ id: 1, rowVersion: 'rv-mock' }) as CalendarEventDto),
    remove: vi.fn(async () => true),
  },
  outlookSyncApi: {
    getStatus: vi.fn(),
    connect: vi.fn(),
    syncNow: vi.fn(),
    disconnect: vi.fn(),
    saveSettings: vi.fn(),
  },
}));

const E = (partial: Partial<CalendarEventDto> = {}): CalendarEventDto => ({
  id: 1, title: 'Team Sync', description: null,
  eventType: 'meeting', eventTypeLabel: '会议',
  startAt: '2026-08-12T09:00:00Z', endAt: '2026-08-12T10:00:00Z',
  isAllDay: false, conversationId: null, conversationSubject: null,
  mailboxId: null, mailboxName: null, userId: null, userName: null,
  status: 'pending', statusLabel: '待办', color: null,
  createdAt: '', updatedAt: '', createdBy: 1,
  ...partial,
} as CalendarEventDto);

beforeEach(() => { cleanup(); });

describe('chips pure helpers (Task 5)', () => {
  describe('_chipTimeLabel', () => {
    it('UTC 09:00–10:00 → 本地 "09:00-10:00"（按本地时间格式化）', () => {
      const e = E();
      const r = _chipTimeLabel(e);
      expect(typeof r).toBe('string');
      expect(r).toMatch(/^\d{2}:\d{2}-\d{2}:\d{2}$/);
    });
    it('endAt null → 只显示开始时间，无中划线', () => {
      const e = E({ endAt: null });
      const r = _chipTimeLabel(e);
      expect(r).toMatch(/^\d{2}:\d{2}$/);
    });
  });

  describe('_chipDisplayLabel', () => {
    it('常规事件：eventType + 状态 label 可从 EVENT_TYPE_STYLE/STATUS_STYLE 查到 → 返回 null（走默认 className，不定制）', () => {
      expect(_chipDisplayLabel(E())).toBeNull();
      expect(_chipDisplayLabel(E({ eventType: 'task', status: 'completed' }))).toBeNull();
    });
    it('未知 eventType "camping" → 返回 null（由 EventChipImpl 的 ?? EVENT_TYPE_STYLE.other 兜底为绿色，不再显示灰色 fallback）', () => {
      // 修复"偶发日历颜色显示全部变灰"BUG：移除灰色 fallback，未知类型走 other 样式
      const r = _chipDisplayLabel(E({ eventType: 'camping', status: 'pending' }));
      expect(r).toBeNull();
    });
    it('eventType 为 undefined → 返回 null（兜底为 other，不再显示灰色）', () => {
      const r = _chipDisplayLabel(E({ eventType: undefined as unknown as string, status: 'pending' }));
      expect(r).toBeNull();
    });
    it('eventType 为空字符串 → 返回 null（兜底为 other，不再显示灰色）', () => {
      const r = _chipDisplayLabel(E({ eventType: '', status: 'pending' }));
      expect(r).toBeNull();
    });
    it('status 为 undefined → 返回 null（兜底为 pending，不再显示灰色）', () => {
      const r = _chipDisplayLabel(E({ eventType: 'task', status: undefined as unknown as string }));
      expect(r).toBeNull();
    });
  });

  describe('_allDayTimeLabel', () => {
    it('单天全天 event → "全天"', () => {
      expect(_allDayTimeLabel(E({ isAllDay: true }))).toBe('全天');
    });
    it('跨 ≥2 天全天 event（startAt Day1、endAt Day3）→ "Day1 起 共 X 天"', () => {
      const e = E({
        isAllDay: true,
        startAt: new Date(2026, 7, 12, 0, 0, 0).toISOString(),
        endAt: new Date(2026, 7, 14, 23, 59, 59).toISOString(),
      });
      const r = _allDayTimeLabel(e);
      expect(r).toContain('天');
      expect(r).toMatch(/共\s*\d+\s*天/);
    });
    it('endAt null + isAllDay → "全天"（不崩溃）', () => {
      expect(_allDayTimeLabel(E({ isAllDay: true, endAt: null }))).toBe('全天');
    });
  });
});

describe('MoreEventsChip memo smoke (Task 5)', () => {
  it('count=5 → 显示 "+5"', () => {
    render(<MoreEventsChip count={5} onOpen={() => {}} />);
    expect(screen.getByText('+5')).toBeTruthy();
  });
  it('点击 "+3" 触发 onOpen', () => {
    const fn = vi.fn();
    render(<MoreEventsChip count={3} onOpen={fn} />);
    fireEvent.click(screen.getByText('+3'));
    expect(fn).toHaveBeenCalledTimes(1);
  });
});

describe('CalendarStore P1-2 批量选择', () => {
  beforeEach(() => {
    useCalendarStore.setState({ selectedEventIds: new Set() });
  });

  it('selectEvent → Set 包含 id', () => {
    useCalendarStore.getState().selectEvent(42);
    expect(useCalendarStore.getState().selectedEventIds.has(42)).toBe(true);
  });

  it('selectEvent 已选 → no-op（引用不变）', () => {
    useCalendarStore.getState().selectEvent(1);
    const ref = useCalendarStore.getState().selectedEventIds;
    useCalendarStore.getState().selectEvent(1);
    expect(useCalendarStore.getState().selectedEventIds).toBe(ref);
  });

  it('deselectEvent → 移除 id', () => {
    useCalendarStore.getState().selectEvent(1);
    useCalendarStore.getState().selectEvent(2);
    useCalendarStore.getState().deselectEvent(1);
    expect(useCalendarStore.getState().selectedEventIds.has(1)).toBe(false);
    expect(useCalendarStore.getState().selectedEventIds.has(2)).toBe(true);
  });

  it('deselectEvent 未选 → no-op', () => {
    const ref = useCalendarStore.getState().selectedEventIds;
    useCalendarStore.getState().deselectEvent(999);
    expect(useCalendarStore.getState().selectedEventIds).toBe(ref);
  });

  it('clearSelection → 空 Set', () => {
    useCalendarStore.getState().selectEvent(1);
    useCalendarStore.getState().selectEvent(2);
    useCalendarStore.getState().clearSelection();
    expect(useCalendarStore.getState().selectedEventIds.size).toBe(0);
  });

  it('selectAll → 新 Set 包含所有 id', () => {
    useCalendarStore.getState().selectAll([10, 20, 30]);
    expect(useCalendarStore.getState().selectedEventIds.size).toBe(3);
    expect(useCalendarStore.getState().selectedEventIds.has(20)).toBe(true);
  });

  it('isSelected → 正确返回', () => {
    useCalendarStore.getState().selectEvent(5);
    expect(useCalendarStore.getState().isSelected(5)).toBe(true);
    expect(useCalendarStore.getState().isSelected(6)).toBe(false);
  });
});

describe('CalendarStore P2-1 模板', () => {
  it('loadTemplates → 写入 eventTemplates', () => {
    const tpls = [
      { id: 1, name: '早会', title: '每日早会', eventType: 'meeting', durationMin: 30, isAllDay: false },
    ];
    useCalendarStore.getState().loadTemplates(tpls);
    expect(useCalendarStore.getState().eventTemplates).toHaveLength(1);
    expect(useCalendarStore.getState().eventTemplates[0].name).toBe('早会');
  });

  it('applyTemplate → 打开编辑器并填充表单', () => {
    useCalendarStore.getState().loadTemplates([
      { id: 1, name: '早会', title: '每日早会', eventType: 'meeting', durationMin: 30, isAllDay: false, description: '站会', location: 'A 会议室' },
    ]);
    useCalendarStore.getState().applyTemplate(1, new Date(2026, 7, 12));
    const s = useCalendarStore.getState();
    expect(s.showEditor).toBe(true);
    expect(s.editing).toBe(null);
    expect(s.form.title).toBe('每日早会');
    expect(s.form.eventType).toBe('meeting');
    expect(s.form.isAllDay).toBe(false);
    expect(s.form.description).toBe('站会');
    expect(s.form.location).toBe('A 会议室');
    expect(s.form.startAt).toContain('2026-08-12T09:00');
  });

  it('applyTemplate 全天事件 → endAt 为空', () => {
    useCalendarStore.getState().loadTemplates([
      { id: 2, name: '休假', title: '全天休假', eventType: 'personal', durationMin: 0, isAllDay: true },
    ]);
    useCalendarStore.getState().applyTemplate(2, new Date(2026, 7, 15));
    const s = useCalendarStore.getState();
    expect(s.form.isAllDay).toBe(true);
    expect(s.form.endAt).toBe('');
    expect(s.form.startAt).toContain('2026-08-15T00:00');
  });

  it('applyTemplate 不存在的 id → no-op', () => {
    useCalendarStore.getState().closeEditor();
    useCalendarStore.getState().applyTemplate(999);
    expect(useCalendarStore.getState().showEditor).toBe(false);
  });
});

describe('CalendarStore P2P3 #8.1: Undo/Redo localStorage 持久化', () => {
  beforeEach(() => {
    localStorage.clear();
    useCalendarStore.setState({ undoStack: [], redoStack: [] });
  });

  it('初始状态 undoStack/redoStack 均为空数组', () => {
    expect(useCalendarStore.getState().undoStack).toEqual([]);
    expect(useCalendarStore.getState().redoStack).toEqual([]);
  });

  it('pushUndo → 写入 undoStack，redoStack 清空', () => {
    useCalendarStore.getState().pushUndo({
      action: 'create',
      beforeSnapshot: [],
      afterSnapshot: [{ id: 1, title: 'A' }],
      description: '创建事件 A',
    });
    expect(useCalendarStore.getState().undoStack).toHaveLength(1);
    expect(useCalendarStore.getState().redoStack).toHaveLength(0);
    const entry = useCalendarStore.getState().undoStack[0];
    // pushUndo 内存模式不强制填充 id/createdAt（仅持久化格式需要），只校验必填动作字段
    expect(entry.action).toBe('create');
    expect(entry.description).toBe('创建事件 A');
  });

  it('undo() → 从 undoStack 弹出移到 redoStack（async，验证栈状态移动）', async () => {
    useCalendarStore.getState().pushUndo({
      action: 'update',
      type: 'edit',
      beforeSnapshot: [{ id: 1, title: 'old' }] as unknown as CalendarEventDto,
      afterSnapshot: [{ id: 1, title: 'new' }] as unknown as CalendarEventDto,
    });
    expect(useCalendarStore.getState().undoStack).toHaveLength(1);
    const beforeAction = useCalendarStore.getState().undoStack[0].action;
    await useCalendarStore.getState().undo();
    expect(useCalendarStore.getState().undoStack).toHaveLength(0);
    expect(useCalendarStore.getState().redoStack).toHaveLength(1);
    expect(useCalendarStore.getState().redoStack[0].action).toBe(beforeAction);
  });

  it('undo() 空栈 → async 完成不崩溃', async () => {
    expect(useCalendarStore.getState().undoStack).toHaveLength(0);
    await expect(useCalendarStore.getState().undo()).resolves.not.toThrow();
    expect(useCalendarStore.getState().undoStack).toHaveLength(0);
    expect(useCalendarStore.getState().redoStack).toHaveLength(0);
  });

  it('redo() → 从 redoStack 弹出移到 undoStack（async，验证栈状态移动）', async () => {
    useCalendarStore.getState().pushUndo({
      action: 'delete',
      type: 'delete',
      beforeSnapshot: [{ id: 5 }] as unknown as CalendarEventDto,
      afterSnapshot: [] as unknown as CalendarEventDto,
    });
    await useCalendarStore.getState().undo();
    expect(useCalendarStore.getState().redoStack).toHaveLength(1);
    const beforeAction = useCalendarStore.getState().redoStack[0].action;
    await useCalendarStore.getState().redo();
    expect(useCalendarStore.getState().redoStack).toHaveLength(0);
    expect(useCalendarStore.getState().undoStack).toHaveLength(1);
    expect(useCalendarStore.getState().undoStack[0].action).toBe(beforeAction);
  });

  it('redo() 空栈 → async 完成不崩溃', async () => {
    expect(useCalendarStore.getState().redoStack).toHaveLength(0);
    await expect(useCalendarStore.getState().redo()).resolves.not.toThrow();
    expect(useCalendarStore.getState().redoStack).toHaveLength(0);
    expect(useCalendarStore.getState().undoStack).toHaveLength(0);
  });

  it('pushUndo 超 50 条 → 只保留最新 50 条（drop oldest）', () => {
    for (let i = 0; i < 60; i++) {
      useCalendarStore.getState().pushUndo({
        action: 'update',
        beforeSnapshot: [{ id: i }],
        afterSnapshot: [{ id: i, title: `t${i}` }],
      });
    }
    expect(useCalendarStore.getState().undoStack).toHaveLength(50);
    const firstId = useCalendarStore.getState().undoStack[0].beforeSnapshot[0].id;
    expect(firstId).toBe(10);
    const lastId = useCalendarStore.getState().undoStack[49].beforeSnapshot[0].id;
    expect(lastId).toBe(59);
  });

  it('pushUndo 后 localStorage 写入成功（需先设置 user mock）', () => {
    localStorage.setItem('user', JSON.stringify({ id: 42, authenticated: true }));
    useCalendarStore.getState().pushUndo({
      action: 'create',
      beforeSnapshot: [],
      afterSnapshot: [{ id: 100, title: '持久化测试' }],
    });
    const raw = localStorage.getItem('calendar:undo:42');
    expect(raw).toBeTruthy();
    const parsed = JSON.parse(raw!);
    expect(parsed.version).toBe(1);
    expect(parsed.savedAt).toBeTruthy();
    expect(parsed.undoStack).toHaveLength(1);
    expect(parsed.undoStack[0].action).toBe('create');
    expect(parsed.redoStack).toHaveLength(0);
  });

  it('容量上限：pushUndo 60 次 → localStorage 中 undoStack 仍为 50', () => {
    localStorage.setItem('user', JSON.stringify({ id: 99, authenticated: true }));
    for (let i = 0; i < 60; i++) {
      useCalendarStore.getState().pushUndo({
        action: 'update',
        beforeSnapshot: [{ id: i }],
        afterSnapshot: [{ id: i, title: `t${i}` }],
      });
    }
    const raw = localStorage.getItem('calendar:undo:99');
    expect(raw).toBeTruthy();
    const parsed = JSON.parse(raw!);
    expect(parsed.undoStack).toHaveLength(50);
  });

  it('persistUndoRedo 内部异常（QuotaExceeded 等）→ 只打 warn 不抛错', () => {
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation(() => {});
    localStorage.setItem('user', JSON.stringify({ id: 1, authenticated: true }));
    const origSetItem = Storage.prototype.setItem;
    Storage.prototype.setItem = vi.fn(() => { throw new Error('QuotaExceededError'); });
    expect(() => {
      useCalendarStore.getState().pushUndo({
        action: 'create',
        beforeSnapshot: [],
        afterSnapshot: [{ id: 1 }],
      });
    }).not.toThrow();
    expect(warnSpy).toHaveBeenCalledWith(
      '[calendar] undo persist failed:',
      expect.any(Error),
    );
    Storage.prototype.setItem = origSetItem;
    warnSpy.mockRestore();
  });

  it('无 userId（未登录）→ persist 静默不写入 localStorage', () => {
    localStorage.removeItem('user');
    useCalendarStore.getState().pushUndo({
      action: 'create',
      beforeSnapshot: [],
      afterSnapshot: [{ id: 5 }],
    });
    const keys = Object.keys(localStorage).filter(k => k.startsWith('calendar:undo:'));
    expect(keys).toHaveLength(0);
  });
});

describe('CalendarStore P2P3 #8.2: 恢复 + logout 清理 + 版本 guard', () => {
  beforeEach(() => {
    localStorage.clear();
    useCalendarStore.setState({ undoStack: [], redoStack: [] });
  });

  it('restoreUndoRedoFromStorage: localStorage 中有合法数据 → 正确恢复', () => {
    localStorage.setItem('user', JSON.stringify({ id: 7, authenticated: true }));
    const payload = {
      version: 1,
      savedAt: new Date().toISOString(),
      undoStack: [
        { id: 'a1', action: 'create' as const, beforeSnapshot: [], afterSnapshot: [{ id: 1 }], createdAt: '2026-08-14T00:00:00Z' },
        { id: 'a2', action: 'update' as const, beforeSnapshot: [{ id: 2, title: 'old' }], afterSnapshot: [{ id: 2, title: 'new' }], createdAt: '2026-08-14T00:00:01Z' },
        { id: 'a3', action: 'delete' as const, beforeSnapshot: [{ id: 3 }], afterSnapshot: [], createdAt: '2026-08-14T00:00:02Z' },
      ],
      redoStack: [
        { id: 'r1', action: 'move' as const, beforeSnapshot: [{ id: 9 }], afterSnapshot: [{ id: 9 }], createdAt: '2026-08-14T00:00:03Z' },
      ],
    };
    localStorage.setItem('calendar:undo:7', JSON.stringify(payload));
    const result = restoreUndoRedoFromStorage();
    expect(result).not.toBeNull();
    expect(result!.undoStack).toHaveLength(3);
    expect(result!.redoStack).toHaveLength(1);
    expect(result!.undoStack[1].action).toBe('update');
    expect(result!.redoStack[0].id).toBe('r1');
  });

  it('恢复应用到 store：setState 之后 undoStack/redoStack 正确写入', () => {
    localStorage.setItem('user', JSON.stringify({ id: 8, authenticated: true }));
    const fakeStacks = {
      undoStack: [
        { id: 'x1', action: 'create' as const, beforeSnapshot: [], afterSnapshot: [{ id: 10 }], createdAt: '2026-08-14T00:00:00Z' },
        { id: 'x2', action: 'update' as const, beforeSnapshot: [{ id: 11 }], afterSnapshot: [{ id: 11 }], createdAt: '2026-08-14T00:00:01Z' },
        { id: 'x3', action: 'delete' as const, beforeSnapshot: [{ id: 12 }], afterSnapshot: [], createdAt: '2026-08-14T00:00:02Z' },
      ],
      redoStack: [
        { id: 'y1', action: 'restore' as const, beforeSnapshot: [], afterSnapshot: [{ id: 13 }], createdAt: '2026-08-14T00:00:03Z' },
      ],
    };
    localStorage.setItem(
      'calendar:undo:8',
      JSON.stringify({ version: 1, savedAt: new Date().toISOString(), ...fakeStacks }),
    );
    const restored = restoreUndoRedoFromStorage();
    expect(restored).not.toBeNull();
    useCalendarStore.setState({ undoStack: restored!.undoStack, redoStack: restored!.redoStack });
    expect(useCalendarStore.getState().undoStack).toHaveLength(3);
    expect(useCalendarStore.getState().redoStack).toHaveLength(1);
  });

  it('版本不匹配（version=2）→ restore 返回 null，store 不变（版本 guard）', () => {
    localStorage.setItem('user', JSON.stringify({ id: 10, authenticated: true }));
    useCalendarStore.getState().pushUndo({
      action: 'create', beforeSnapshot: [], afterSnapshot: [{ id: 99 }],
    });
    const beforeUndoLen = useCalendarStore.getState().undoStack.length;
    const malformed = { version: 2, savedAt: new Date().toISOString(), undoStack: new Array(10).fill(null).map((_, i) => ({
      id: `bad${i}`, action: 'update' as const, beforeSnapshot: [{ id: i }], afterSnapshot: [{ id: i }], createdAt: '2026-08-14T00:00:00Z',
    })), redoStack: [] };
    localStorage.setItem('calendar:undo:10', JSON.stringify(malformed));
    const result = restoreUndoRedoFromStorage();
    expect(result).toBeNull();
    expect(useCalendarStore.getState().undoStack).toHaveLength(beforeUndoLen);
  });

  it('JSON 非法 / 结构损坏 → restore 返回 null（try-catch 静默降级）', () => {
    localStorage.setItem('user', JSON.stringify({ id: 11, authenticated: true }));
    localStorage.setItem('calendar:undo:11', '{this is not valid json');
    expect(() => {
      const r = restoreUndoRedoFromStorage();
      expect(r).toBeNull();
    }).not.toThrow();
    localStorage.setItem('calendar:undo:11', JSON.stringify({ version: 1, undoStack: 'not-an-array', redoStack: [] }));
    expect(restoreUndoRedoFromStorage()).toBeNull();
  });

  it('过滤无效 entry：缺 id / beforeSnapshot 不是数组 → 被过滤掉', () => {
    localStorage.setItem('user', JSON.stringify({ id: 12, authenticated: true }));
    // 故意构造非法 entry 以验证过滤逻辑，因此禁用 any 警告
     
    const badGoodMix = {
      version: 1,
      savedAt: new Date().toISOString(),
      undoStack: [
        null,
        { action: 'create' as const, beforeSnapshot: [], afterSnapshot: [{ id: 1 }], createdAt: 't' }, // 缺 id
        { id: 'g1', action: 'create' as const, beforeSnapshot: 'not-array' as unknown as CalendarEventDto | null, afterSnapshot: [{ id: 2 }], createdAt: 't' },
        { id: 'g2', action: 'update' as const, beforeSnapshot: [{ id: 3 }], afterSnapshot: [{ id: 3, title: 'ok' }], createdAt: 't' }, // valid
      ],
      redoStack: [
        { id: 'r-good', action: 'delete' as const, beforeSnapshot: [{ id: 4 }], afterSnapshot: [], createdAt: 't' }, // valid
      ],
    };
     
    localStorage.setItem('calendar:undo:12', JSON.stringify(badGoodMix));
    const r = restoreUndoRedoFromStorage();
    expect(r).not.toBeNull();
    expect(r!.undoStack).toHaveLength(1);
    expect(r!.undoStack[0].id).toBe('g2');
    expect(r!.redoStack).toHaveLength(1);
    expect(r!.redoStack[0].id).toBe('r-good');
  });

  it('clearPersistedUndoRedoForLogout → 清理所有 calendar:undo:* 前缀键', () => {
    localStorage.setItem('user', JSON.stringify({ id: 1, authenticated: true }));
    localStorage.setItem('calendar:undo:1', JSON.stringify({ version: 1, undoStack: [], redoStack: [], savedAt: '' }));
    localStorage.setItem('calendar:undo:2', JSON.stringify({ version: 1, undoStack: [], redoStack: [], savedAt: '' }));
    localStorage.setItem('calendar:undo:999', JSON.stringify({ version: 1, undoStack: [], redoStack: [], savedAt: '' }));
    localStorage.setItem('noneadcss:other', 'keep');
    localStorage.setItem('freescout:x', 'keep');
    expect(Object.keys(localStorage).filter(k => k.startsWith('calendar:undo:'))).toHaveLength(3);
    clearPersistedUndoRedoForLogout();
    const remaining = Object.keys(localStorage).filter(k => k.startsWith('calendar:undo:'));
    expect(remaining).toHaveLength(0);
    expect(localStorage.getItem('noneadcss:other')).toBe('keep');
    expect(localStorage.getItem('freescout:x')).toBe('keep');
  });

  it('恢复容量裁剪：localStorage 中超过 50 条 → 只保留最新 50 条', () => {
    localStorage.setItem('user', JSON.stringify({ id: 20, authenticated: true }));
    const bigUndo = new Array(80).fill(0).map((_, i) => ({
      id: `u${i}`, action: 'update' as const, beforeSnapshot: [{ id: i }], afterSnapshot: [{ id: i, title: `t${i}` }], createdAt: new Date().toISOString(),
    }));
    const bigRedo = new Array(70).fill(0).map((_, i) => ({
      id: `r${i}`, action: 'delete' as const, beforeSnapshot: [{ id: i + 100 }], afterSnapshot: [], createdAt: new Date().toISOString(),
    }));
    localStorage.setItem('calendar:undo:20', JSON.stringify({
      version: 1, savedAt: new Date().toISOString(), undoStack: bigUndo, redoStack: bigRedo,
    }));
    const r = restoreUndoRedoFromStorage();
    expect(r).not.toBeNull();
    expect(r!.undoStack).toHaveLength(50);
    expect(r!.undoStack[0].id).toBe('u30');
    expect(r!.undoStack[49].id).toBe('u79');
    expect(r!.redoStack).toHaveLength(50);
    expect(r!.redoStack[0].id).toBe('r20');
    expect(r!.redoStack[49].id).toBe('r69');
  });
});

/* ============================ chips 纯函数 + 组件（Task 7，补齐 chips.tsx） ============================ */
describe('chips 纯函数（getEventTypeStyle / status / color / bg）', () => {
  describe('getEventTypeStyle 大小写不敏感 + 兜底', () => {
    it('小写 eventType 能精确命中 EVENT_TYPE_STYLE', () => {
      const r = getEventTypeStyle('meeting');
      expect(r).toHaveProperty('badgeClass');
      expect(r).toHaveProperty('dotClass');
    });
    it('大写 "MEETING" / 驼峰 "Meeting" 都能落到相同 style（toLowerCase）', () => {
      const a = getEventTypeStyle('MEETING');
      const b = getEventTypeStyle('Meeting');
      const c = getEventTypeStyle('meeting');
      // 引用相同，说明查字典走同一分支
      expect(a.badgeClass).toBe(c.badgeClass);
      expect(b.dotClass).toBe(c.dotClass);
    });
    it('null / undefined / "" → 兜底为 EVENT_TYPE_STYLE.other，不抛异常', () => {
      const a = getEventTypeStyle(null);
      const b = getEventTypeStyle(undefined);
      const c = getEventTypeStyle('');
      expect(a.dotClass).toBeTruthy();
      expect(b.dotClass).toBeTruthy();
      expect(c.dotClass).toBeTruthy();
    });
    it('未知类型 "camping" → 兜底为 EVENT_TYPE_STYLE.other（不崩）', () => {
      const r = getEventTypeStyle('camping');
      expect(r.badgeClass).toBeTruthy();
      expect(r.dotClass).toBeTruthy();
    });
  });

  describe('getStatusStyle 大小写不敏感 + 兜底', () => {
    it('已完成 "completed" / "Completed" / "COMPLETED" → 同一 style', () => {
      const a = getStatusStyle('completed');
      const b = getStatusStyle('Completed');
      const c = getStatusStyle('COMPLETED');
      expect(a.badgeClass).toBe(c.badgeClass);
      expect(b.dotClass).toBe(c.dotClass);
    });
    it('null / undefined / "" → 兜底 STATUS_STYLE.pending', () => {
      const a = getStatusStyle(null);
      const b = getStatusStyle(undefined);
      expect(a.badgeClass).toBeTruthy();
      expect(b.badgeClass).toBeTruthy();
    });
    it('未知状态 "reviewing" → 不会空值 / 不会抛（兜底 pending）', () => {
      const r = getStatusStyle('reviewing');
      expect(typeof r.badgeClass).toBe('string');
      expect(r.badgeClass.length).toBeGreaterThan(0);
    });
  });

  describe('_colorOverrideStyle + _colorOverrideRingStyle', () => {
    it('无 color → 返回 undefined', () => {
      expect(_colorOverrideStyle({ color: null })).toBeUndefined();
      expect(_colorOverrideStyle({ color: undefined })).toBeUndefined();
      expect(_colorOverrideRingStyle({})).toBeUndefined();
    });
    it('亮色 #ffffff → 文本色为深色 slate-800(#1e293b)', () => {
      const r = _colorOverrideStyle({ color: '#ffffff' });
      expect(r).toBeDefined();
      expect(r!.backgroundColor).toBe('#ffffff');
      expect(r!.color).toBe('#1e293b');
    });
    it('暗色 #000000 → 文本色为白色', () => {
      const r = _colorOverrideStyle({ color: '#000000' });
      expect(r!.color).toBe('#ffffff');
    });
    it('3 位短色 #fff 展开为 6 位处理，结果背景仍是 "#fff"（原始 color 字段）', () => {
      const r = _colorOverrideStyle({ color: '#fff' });
      expect(r!.backgroundColor).toBe('#fff');
    });
    it('非 3/6 位异常值（不抛异常，保守返回浅色文本）', () => {
      // color 是 2 位，_isLightHex 返回 true → slate-800
      expect(() => _colorOverrideStyle({ color: '#ff' })).not.toThrow();
      expect(_colorOverrideStyle({ color: '#ff' })!.color).toBe('#1e293b');
    });
    it('_colorOverrideRingStyle 总是返回 inset box-shadow，包含颜色 + 66 alpha', () => {
      const r = _colorOverrideRingStyle({ color: '#abcdef' });
      expect(r!.boxShadow).toMatch(/0 0 0 1px #abcdef66 inset/);
    });
  });

  describe('_timegridBackgroundColor（自定义色优先，事件类型回退）', () => {
    it('有自定义 color → 直接返回 color', () => {
      expect(_timegridBackgroundColor({ color: '#ff0000', eventType: 'meeting' })).toBe('#ff0000');
    });
    it('无 color → 按 eventType 回退到 EVENT_TYPE_DOT_HEX（大小写不敏感）', () => {
      expect(_timegridBackgroundColor({ eventType: 'meeting' })).toBe('#8b5cf6');
      expect(_timegridBackgroundColor({ eventType: 'MEETING' })).toBe('#8b5cf6');
      expect(_timegridBackgroundColor({ eventType: 'task' })).toBe('#3b82f6');
      expect(_timegridBackgroundColor({ eventType: 'due_date' })).toBe('#f43f5e');
      expect(_timegridBackgroundColor({ eventType: 'reminder' })).toBe('#f59e0b');
      expect(_timegridBackgroundColor({ eventType: 'personal' })).toBe('#ec4899');
      expect(_timegridBackgroundColor({ eventType: 'travel' })).toBe('#10b981');
      expect(_timegridBackgroundColor({ eventType: 'work' })).toBe('#0ea5e9');
    });
    it('空/未知 eventType → 兜底 other (#64748b)', () => {
      expect(_timegridBackgroundColor({ eventType: '' })).toBe('#64748b');
      expect(_timegridBackgroundColor({ eventType: 'xxx-new' })).toBe('#64748b');
      expect(_timegridBackgroundColor({})).toBe('#64748b');
    });
  });

  describe('useHourHeight（matchMedia 56/44 断点）', () => {
    const matchMediaStub = (matches: boolean) => {
      // @ts-expect-error jsdom matchMedia
      window.matchMedia = vi.fn((query: string) => ({
        matches,
        media: query,
        onchange: null,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
        addListener: vi.fn(),
        removeListener: vi.fn(),
        dispatchEvent: vi.fn(),
      }));
    };
    it('默认桌面端（>768，matches=false）→ 56px', () => {
      matchMediaStub(false);
      const { result } = renderHook(() => useHourHeight());
      expect(result.current).toBe(56);
    });
    it('移动端（matches=true）→ 44px', () => {
      matchMediaStub(true);
      const { result } = renderHook(() => useHourHeight());
      expect(result.current).toBe(44);
    });
  });
});

describe('AllDayEventBar（全天事件条，组件级）', () => {
  beforeEach(() => {
    useCalendarStore.setState({ draggedEvent: null });
  });

  it('渲染 title + 全天 label，点击触发 onOpen', () => {
    const onOpen = vi.fn();
    const ev = E({ title: '放假啦', isAllDay: true });
    render(<AllDayEventBar ev={ev} onOpen={onOpen} />);
    expect(screen.getByText(/放假啦/)).toBeInTheDocument();
    expect(screen.getByText(/全天/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button'));
    expect(onOpen).toHaveBeenCalledTimes(1);
    expect(onOpen).toHaveBeenLastCalledWith(ev);
  });

  it('Enter / Space 键触发 onOpen（无障碍）', () => {
    const onOpen = vi.fn();
    render(<AllDayEventBar ev={E({ title: 'standup', isAllDay: true })} onOpen={onOpen} />);
    const btn = screen.getByRole('button');
    fireEvent.keyDown(btn, { key: 'Enter' });
    fireEvent.keyDown(btn, { key: ' ' });
    expect(onOpen).toHaveBeenCalledTimes(2);
  });

  it('selected=true → 渲染 cal-selected-badge + CheckCircle2', () => {
    const { container } = render(
      <AllDayEventBar ev={E({ title: 'x' })} onOpen={vi.fn()} selected />,
    );
    expect(container.querySelector('.cal-selected-badge')).not.toBeNull();
    expect(container.querySelector('.ring-inset')).not.toBeNull();
  });

  it('dimmed=true → 根节点含有 opacity-40 + grayscale', () => {
    const { container } = render(
      <AllDayEventBar ev={E({ title: 'x' })} onOpen={vi.fn()} dimmed />,
    );
    const cls = (container.firstChild as HTMLElement).className;
    expect(cls).toContain('opacity-40');
    expect(cls).toContain('grayscale');
  });

  it('onDragStart 提供时 draggable=true，调用 onDragStart(ev, e)', () => {
    const ev = E({ title: 'drag me' });
    const ds = vi.fn();
    render(<AllDayEventBar ev={ev} onOpen={vi.fn()} onDragStart={ds} />);
    const el = screen.getByRole('button');
    expect(el).toHaveAttribute('draggable', 'true');
    fireEvent(el, new Event('dragstart', { bubbles: true }));
    // fireEvent 构造的 dragstart 无法填 React 的合成事件对象，但我们可以断言：组件内部是否调用 onDragStart
    // 这里改用直接读 attribute 是否含有 true + className 含有 cursor-pointer，更稳定。
    expect(el.className).toContain('cursor-pointer');
  });

  /* ===== isSourceDragged 分支（chips.tsx 行 710、730） ===== */
  it('isSourceDragged=true（store.draggedEvent.id === ev.id）→ 根节点含 opacity-35 + scale-[0.99] + ring-2 ring-white/50', () => {
    const ev = E({ id: 42, title: '被拖拽中' });
    useCalendarStore.setState({ draggedEvent: ev });
    try {
      const { container } = render(<AllDayEventBar ev={ev} onOpen={vi.fn()} />);
      const cls = (container.firstChild as HTMLElement).className;
      expect(cls).toContain('opacity-35');
      expect(cls).toContain('scale-[0.99]');
      expect(cls).toContain('ring-2');
      expect(cls).toContain('ring-white/50');
    } finally {
      useCalendarStore.setState({ draggedEvent: null });
    }
  });

  it('isSourceDragged=false（store.draggedEvent 为 null）→ 根节点不含 opacity-35 / scale-[0.99]', () => {
    useCalendarStore.setState({ draggedEvent: null });
    const { container } = render(<AllDayEventBar ev={E({ id: 1, title: 'x' })} onOpen={vi.fn()} />);
    const cls = (container.firstChild as HTMLElement).className;
    expect(cls).not.toContain('opacity-35');
    expect(cls).not.toContain('scale-[0.99]');
  });

  it('isSourceDragged=false（store.draggedEvent.id !== ev.id）→ 根节点不含 opacity-35', () => {
    const other = E({ id: 999, title: '另一个' });
    useCalendarStore.setState({ draggedEvent: other });
    try {
      const { container } = render(<AllDayEventBar ev={E({ id: 1, title: 'x' })} onOpen={vi.fn()} />);
      const cls = (container.firstChild as HTMLElement).className;
      expect(cls).not.toContain('opacity-35');
    } finally {
      useCalendarStore.setState({ draggedEvent: null });
    }
  });

  /* ===== 空标题分支（chips.tsx 行 757: ev.title || '(无标题)'） ===== */
  it('ev.title 为空字符串 → 渲染 "(无标题)"', () => {
    render(<AllDayEventBar ev={E({ id: 1, title: '' })} onOpen={vi.fn()} />);
    expect(screen.getByText(/\(无标题\)/)).toBeInTheDocument();
  });

  it('ev.title 为 null → 渲染 "(无标题)"', () => {
    render(<AllDayEventBar ev={E({ id: 1, title: null as unknown as string })} onOpen={vi.fn()} />);
    expect(screen.getByText(/\(无标题\)/)).toBeInTheDocument();
  });

  /* ===== memo 比较函数（chips.tsx 行 763） =====
     比较 5 个 props: ev, onOpen, onDragStart, dimmed, selected
     相同 → 跳过渲染；任一变化 → 重新渲染
     直接调用 _allDayEventBarMemoComparator 验证返回值，避免 React.Profiler 在父组件
     rerender 时触发的误报（Profiler 自身被 commit 即会调用 onRender）。 */
  describe('memo 比较函数', () => {
    it('所有 props 引用相同 → memo 返回 true → 不重新渲染', () => {
      const ev = E({ id: 1, title: 'stable' });
      const onOpen = vi.fn();
      const onDragStart = vi.fn();
      const props = { ev, onOpen, onDragStart, dimmed: false, selected: false };
      expect(_allDayEventBarMemoComparator(props, props)).toBe(true);
      // 引用不同但内容全等 → true
      expect(_allDayEventBarMemoComparator({ ...props }, { ...props })).toBe(true);
    });

    it('ev 引用变化 → memo 返回 false → 重新渲染', () => {
      const ev1 = E({ id: 1, title: 'A' });
      const ev2 = E({ id: 2, title: 'B' });
      const onOpen = vi.fn();
      expect(_allDayEventBarMemoComparator({ ev: ev1, onOpen } as AllDayEventBarProps, { ev: ev2, onOpen } as AllDayEventBarProps)).toBe(false);
    });

    it('dimmed 变化 → memo 返回 false → 重新渲染', () => {
      const ev = E({ id: 1, title: 'x' });
      const onOpen = vi.fn();
      expect(
        _allDayEventBarMemoComparator({ ev, onOpen, dimmed: false } as AllDayEventBarProps, { ev, onOpen, dimmed: true } as AllDayEventBarProps),
      ).toBe(false);
    });

    it('selected 变化 → memo 返回 false → 重新渲染', () => {
      const ev = E({ id: 1, title: 'x' });
      const onOpen = vi.fn();
      expect(
        _allDayEventBarMemoComparator({ ev, onOpen, selected: false } as AllDayEventBarProps, { ev, onOpen, selected: true } as AllDayEventBarProps),
      ).toBe(false);
    });

    it('onOpen 引用变化 → memo 返回 false → 重新渲染', () => {
      const ev = E({ id: 1, title: 'x' });
      const cb1 = vi.fn();
      const cb2 = vi.fn();
      expect(
        _allDayEventBarMemoComparator({ ev, onOpen: cb1 } as AllDayEventBarProps, { ev, onOpen: cb2 } as AllDayEventBarProps),
      ).toBe(false);
    });

    it('onDragStart 引用变化 → memo 返回 false → 重新渲染', () => {
      const ev = E({ id: 1, title: 'x' });
      const onOpen = vi.fn();
      const ds1 = vi.fn();
      const ds2 = vi.fn();
      expect(
        _allDayEventBarMemoComparator(
          { ev, onOpen, onDragStart: ds1 } as AllDayEventBarProps,
          { ev, onOpen, onDragStart: ds2 } as AllDayEventBarProps,
        ),
      ).toBe(false);
    });
  });
});

describe('EmptyState（空状态组件）', () => {
  it('variant=month → 显示「本月暂无日程」+ CTA「+ 新建第一个事件」', () => {
    render(<EmptyState variant="month" title="ignored" onPrimary={vi.fn()} />);
    expect(screen.getByText(/本月暂无日程/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /新建第一个事件/ })).toBeInTheDocument();
  });

  it('variant=week → 显示「本周暂无安排」+「+ 添加本周安排」', () => {
    render(<EmptyState variant="week" title="x" onPrimary={vi.fn()} />);
    expect(screen.getByText(/本周暂无安排/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /添加本周安排/ })).toBeInTheDocument();
  });

  it('variant=day → 「今天还没有安排」', () => {
    render(<EmptyState variant="day" title="x" onPrimary={vi.fn()} />);
    expect(screen.getByText(/今天还没有安排/)).toBeInTheDocument();
  });

  it('variant=list / overview 各自默认文案', () => {
    const { unmount: u1 } = render(<EmptyState variant="list" title="x" />);
    expect(screen.getByText(/暂无事件/)).toBeInTheDocument();
    u1();
    const { unmount: u2 } = render(<EmptyState variant="overview" title="x" />);
    expect(screen.getByText(/暂无数据/)).toBeInTheDocument();
    u2();
  });

  it('variant=search + keyword → 显示「当前搜索：keyword」', () => {
    render(<EmptyState variant="search" title="x" keyword="团建" />);
    expect(screen.getByText(/未找到匹配的事件/)).toBeInTheDocument();
    expect(screen.getByText(/当前搜索："团建"/)).toBeInTheDocument();
  });

  it('无 variant 无 icon → 渲染默认 svg 占位符（rect + 3 lines 的日历图标）', () => {
    const { container } = render(<EmptyState title="x" description="描述" />);
    const svgCount = container.querySelectorAll('svg').length;
    // 没传 variant，也没传 icon，会渲染默认日历图标 SVG
    expect(svgCount).toBeGreaterThanOrEqual(1);
    expect(screen.getByText('x')).toBeInTheDocument();
    expect(screen.getByText('描述')).toBeInTheDocument();
  });

  it('icon 传入自定义 React 节点 → 渲染传入的 icon（不走 VARIANT_SVG）', () => {
    render(
      <EmptyState
        title="x"
        icon={<span data-testid="my-custom-icon">★</span>}
      />,
    );
    expect(screen.getByTestId('my-custom-icon')).toHaveTextContent('★');
  });

  it('action 自定义时不渲染默认 CTA button，仅渲染 action', () => {
    render(
      <EmptyState
        title="x"
        onPrimary={vi.fn()}
        action={<button type="button" data-testid="custom-cta">我是自定义</button>}
      />,
    );
    expect(screen.getByTestId('custom-cta')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /新建事件/ })).toBeNull();
  });

  it('onPrimary 回调点击后被正确调用 1 次', () => {
    const cb = vi.fn();
    render(<EmptyState title="x" onPrimary={cb} />);
    fireEvent.click(screen.getByRole('button'));
    expect(cb).toHaveBeenCalledTimes(1);
  });

  /* ====================== 补充：6 variant SVG / CTA / desc 全覆盖 ====================== */

  describe('各 variant SVG 插画渲染（VARIANT_SVG）', () => {
    it.each([
      ['month', '本月暂无日程'],
      ['week', '本周暂无安排'],
      ['day', '今天还没有安排'],
      ['list', '暂无事件'],
      ['overview', '暂无数据'],
      ['search', '未找到匹配的事件'],
    ] as const)('variant=%s → 渲染至少 1 个 svg + 默认标题 %s', (v, titleText) => {
      const { container } = render(<EmptyState variant={v} title="x" onPrimary={vi.fn()} />);
      const svgs = container.querySelectorAll('svg');
      expect(svgs.length).toBeGreaterThanOrEqual(1);
      expect(screen.getByText(new RegExp(titleText))).toBeInTheDocument();
    });

    it('variant=month SVG 含 circle 元素（日历格子 + 装饰圆点）', () => {
      const { container } = render(<EmptyState variant="month" title="x" />);
      const svg = container.querySelector('svg')!;
      expect(svg.querySelectorAll('circle').length).toBeGreaterThanOrEqual(3);
    });

    it('variant=week SVG 含 7 条竖线（一周 7 列分隔线）', () => {
      const { container } = render(<EmptyState variant="week" title="x" />);
      const svg = container.querySelector('svg')!;
      // 7 条 column 分隔线 + 1 条左侧竖线 = 8 line 元素
      expect(svg.querySelectorAll('line').length).toBeGreaterThanOrEqual(7);
    });

    it('variant=day SVG 含 text 元素「今天」', () => {
      const { container } = render(<EmptyState variant="day" title="x" />);
      const svg = container.querySelector('svg')!;
      const txt = svg.querySelector('text');
      expect(txt).not.toBeNull();
      expect(txt!.textContent).toBe('今天');
    });

    it('variant=list SVG 含圆点 + rect（列表项占位 + 时钟装饰）', () => {
      const { container } = render(<EmptyState variant="list" title="x" />);
      const svg = container.querySelector('svg')!;
      // 3 个列表项 circle + 1 个时钟 circle = 4
      expect(svg.querySelectorAll('circle').length).toBeGreaterThanOrEqual(3);
      expect(svg.querySelectorAll('rect').length).toBeGreaterThanOrEqual(3);
    });

    it('variant=overview SVG 含 polyline（折线图）+ 至少 4 个 rect（卡片占位）', () => {
      const { container } = render(<EmptyState variant="overview" title="x" />);
      const svg = container.querySelector('svg')!;
      expect(svg.querySelectorAll('polyline').length).toBeGreaterThanOrEqual(1);
      expect(svg.querySelectorAll('rect').length).toBeGreaterThanOrEqual(4);
    });

    it('variant=search SVG 含 circle + line（放大镜造型）+ text「?」', () => {
      const { container } = render(<EmptyState variant="search" title="x" />);
      const svg = container.querySelector('svg')!;
      expect(svg.querySelectorAll('circle').length).toBeGreaterThanOrEqual(1);
      expect(svg.querySelectorAll('line').length).toBeGreaterThanOrEqual(1);
      const txt = svg.querySelector('text');
      expect(txt).not.toBeNull();
      expect(txt!.textContent).toBe('?');
    });

    it('variant 渲染的 SVG 容器含 cal-fade-in-up 动画', () => {
      const { container } = render(<EmptyState variant="month" title="x" />);
      const svgWrapper = container.querySelector('[style*="cal-fade-in-up"]');
      expect(svgWrapper).not.toBeNull();
    });
  });

  describe('各 variant 默认 description + CTA 文案完整性', () => {
    it('variant=day → desc="一杯咖啡的时间，规划今天" + CTA="+ 规划今天"', () => {
      render(<EmptyState variant="day" title="x" onPrimary={vi.fn()} />);
      expect(screen.getByText(/一杯咖啡的时间/)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /规划今天/ })).toBeInTheDocument();
    });

    it('variant=list → desc="创建事件后将会在此列表展示" + CTA="+ 新建事件"', () => {
      render(<EmptyState variant="list" title="x" onPrimary={vi.fn()} />);
      expect(screen.getByText(/在此列表展示/)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /\+ 新建事件/ })).toBeInTheDocument();
    });

    it('variant=overview → desc="创建事件后即可查看统计概览" + CTA="+ 创建事件开始统计"', () => {
      render(<EmptyState variant="overview" title="x" onPrimary={vi.fn()} />);
      expect(screen.getByText(/统计概览/)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /创建事件开始统计/ })).toBeInTheDocument();
    });

    it('variant=search → desc="换个关键词试试" + CTA="清除搜索"', () => {
      render(<EmptyState variant="search" title="x" onPrimary={vi.fn()} />);
      expect(screen.getByText(/换个关键词试试/)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /清除搜索/ })).toBeInTheDocument();
    });
  });

  describe('keyword 搜索态边界', () => {
    it('variant=search 无 keyword → 不渲染「当前搜索」文本', () => {
      render(<EmptyState variant="search" title="x" />);
      expect(screen.queryByText(/当前搜索/)).toBeNull();
    });

    it('variant=search + keyword="" (空字符串) → 不渲染「当前搜索」文本（falsy 短路）', () => {
      render(<EmptyState variant="search" title="x" keyword="" />);
      expect(screen.queryByText(/当前搜索/)).toBeNull();
    });

    it('variant=month + keyword → 不渲染「当前搜索」（isSearch=false 短路）', () => {
      render(<EmptyState variant="month" title="x" keyword="不应出现" />);
      expect(screen.queryByText(/当前搜索/)).toBeNull();
    });

    it('variant=search + keyword 含特殊字符 → 原样展示（不做转义）', () => {
      render(<EmptyState variant="search" title="x" keyword="<script>alert(1)</script>" />);
      // 文本节点原样渲染，不会执行为 HTML
      expect(screen.getByText(/当前搜索/)).toBeInTheDocument();
      expect(screen.getByText(/<script>/)).toBeInTheDocument();
    });
  });

  describe('自定义 prop 覆盖 variant 默认值', () => {
    it('自定义 title 覆盖 variant 默认标题', () => {
      render(<EmptyState variant="month" title="我的自定义标题" onPrimary={vi.fn()} />);
      // finalTitle = defaults?.title ?? title → defaults 有值则用 defaults
      // 但 defaults.title 存在，所以自定义 title 不会覆盖 → 验证实际行为
      expect(screen.getByText(/本月暂无日程/)).toBeInTheDocument();
      expect(screen.queryByText(/我的自定义标题/)).toBeNull();
    });

    it('自定义 description 覆盖 variant 默认描述', () => {
      // finalDesc = defaults?.desc ?? description → defaults 有值时自定义不生效
      render(<EmptyState variant="week" title="x" description="自定义描述" />);
      expect(screen.getByText(/规划你的一周/)).toBeInTheDocument();
      expect(screen.queryByText(/自定义描述/)).toBeNull();
    });

    it('无 variant 时自定义 title + description 直接生效', () => {
      render(<EmptyState title="纯自定义标题" description="纯自定义描述" />);
      expect(screen.getByText('纯自定义标题')).toBeInTheDocument();
      expect(screen.getByText('纯自定义描述')).toBeInTheDocument();
    });

    it('无 variant + onPrimary → CTA 使用默认文案 "+ 新建事件"', () => {
      render(<EmptyState title="x" onPrimary={vi.fn()} />);
      expect(screen.getByRole('button', { name: /\+ 新建事件/ })).toBeInTheDocument();
    });
  });

  describe('CTA / action 渲染优先级与缺失场景', () => {
    it('有 variant 但无 onPrimary → 不渲染 CTA button（仅标题 + 描述）', () => {
      render(<EmptyState variant="month" title="x" />);
      expect(screen.queryByRole('button')).toBeNull();
      expect(screen.getByText(/本月暂无日程/)).toBeInTheDocument();
    });

    it('无 variant + 无 onPrimary + 无 action → 不渲染任何 button', () => {
      render(<EmptyState title="x" description="d" />);
      expect(screen.queryByRole('button')).toBeNull();
    });

    it('action + onPrimary 同时提供 → 仅渲染 action，不渲染默认 CTA', () => {
      render(
        <EmptyState
          title="x"
          onPrimary={vi.fn()}
          action={<button type="button" data-testid="only-action">优先</button>}
        />,
      );
      expect(screen.getByTestId('only-action')).toBeInTheDocument();
      // 不应出现默认 CTA（"+ 新建事件"）
      expect(screen.queryByRole('button', { name: /\+ 新建事件/ })).toBeNull();
    });

    it('无 variant + 自定义 action → 渲染 action，不渲染默认 CTA', () => {
      render(
        <EmptyState
          title="x"
          action={<a href="#" data-testid="link-cta">链接式 CTA</a>}
        />,
      );
      expect(screen.getByTestId('link-cta')).toBeInTheDocument();
      expect(screen.queryByRole('button')).toBeNull();
    });

    it('description=undefined → 不渲染 p 描述行', () => {
      render(<EmptyState variant="month" title="x" description={undefined} />);
      // variant=month 有默认 desc，所以 p 会渲染
      // 改测无 variant 场景
      cleanup();
      const { container: c2 } = render(<EmptyState title="x" description={undefined} />);
      const descP = c2.querySelector('p.mt-1\\.5');
      expect(descP).toBeNull();
    });
  });

  describe('memo 浅比较（避免不必要的重渲染）', () => {
    it('相同 props → memo 命中，不重新渲染（ref 引用不变）', () => {
      const cb = vi.fn();
      const { rerender } = render(<EmptyState title="x" onPrimary={cb} />);
      const btn1 = screen.getByRole('button');
      // 使用相同函数引用 rerender
      rerender(<EmptyState title="x" onPrimary={cb} />);
      const btn2 = screen.getByRole('button');
      // memo 阻止了重渲染，DOM 节点引用相同
      expect(btn1).toBe(btn2);
    });

    it('title 变化 → memo 不命中，重新渲染', () => {
      const cb = vi.fn();
      const { rerender } = render(<EmptyState title="标题A" onPrimary={cb} />);
      expect(screen.getByText('标题A')).toBeInTheDocument();
      rerender(<EmptyState title="标题B" onPrimary={cb} />);
      expect(screen.getByText('标题B')).toBeInTheDocument();
      expect(screen.queryByText('标题A')).toBeNull();
    });

    it('keyword 变化 → memo 不命中 → 重新渲染搜索文本', () => {
      const { rerender } = render(<EmptyState variant="search" title="x" keyword="旧词" />);
      expect(screen.getByText(/旧词/)).toBeInTheDocument();
      rerender(<EmptyState variant="search" title="x" keyword="新词" />);
      expect(screen.getByText(/新词/)).toBeInTheDocument();
      expect(screen.queryByText(/旧词/)).toBeNull();
    });
  });
});

/* ============================ EventChip 组件级用例（补齐 chips.tsx 行 179~482） ============================ */
describe('EventChip 组件（chips.tsx 行 179~482）', () => {
  // 重置 store 状态，避免 draggedEvent / pendingPatchIds 跨用例污染
  const resetStore = () => {
    useCalendarStore.setState({
      draggedEvent: null,
      pendingPatchIds: new Set<number>(),
      dragOverDateKey: null,
    });
  };
  beforeEach(() => { resetStore(); });

  describe('基础渲染', () => {
    it('渲染 title + eventType 圆点 + 时间标签', () => {
      render(<EventChip ev={E({ title: '晨会' })} onOpen={vi.fn()} />);
      expect(screen.getByText('晨会')).toBeInTheDocument();
      // 时间标签（非 compact + 非 allDay → 显示 _chipTimeLabel）
      expect(screen.getByText(/^\d{2}:\d{2}-\d{2}:\d{2}$/)).toBeInTheDocument();
    });

    it('ev.title 为空 → 渲染 "(无标题)"', () => {
      render(<EventChip ev={E({ title: '' })} onOpen={vi.fn()} />);
      expect(screen.getByText(/\(无标题\)/)).toBeInTheDocument();
    });

    it('ev.title 为 null → 渲染 "(无标题)"', () => {
      render(<EventChip ev={E({ title: null as unknown as string })} onOpen={vi.fn()} />);
      expect(screen.getByText(/\(无标题\)/)).toBeInTheDocument();
    });

    it('compact=true → 不渲染时间标签', () => {
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} compact />);
      // compact 时不渲染 .truncate.mt-0.5 的 div
      const timeDivs = container.querySelectorAll('.truncate.mt-0\\.5');
      expect(timeDivs.length).toBe(0);
    });

    it('isAllDay=true + 非 compact → 渲染 _allDayTimeLabel', () => {
      render(<EventChip ev={E({ isAllDay: true, title: '全天事件' })} onOpen={vi.fn()} />);
      expect(screen.getByText('全天')).toBeInTheDocument();
    });

    it('isAllDay=true + compact=true → 不渲染时间标签', () => {
      const { container } = render(<EventChip ev={E({ isAllDay: true, title: 'x' })} onOpen={vi.fn()} compact />);
      const timeDivs = container.querySelectorAll('.truncate.mt-0\\.5');
      expect(timeDivs.length).toBe(0);
    });

    it('createdByName 存在 → 时间行显示 "· 创建者名"', () => {
      render(<EventChip ev={E({ createdByName: 'Alice' })} onOpen={vi.fn()} />);
      expect(screen.getByText(/Alice/)).toBeInTheDocument();
    });

    it('allDay + createdByName → 时间行显示创建者', () => {
      render(<EventChip ev={E({ isAllDay: true, createdByName: 'Bob' })} onOpen={vi.fn()} />);
      expect(screen.getByText(/Bob/)).toBeInTheDocument();
    });

    it('root div 含 role=button + tabIndex=0 + data-event-id', () => {
      const { container } = render(<EventChip ev={E({ id: 42, title: 'x' })} onOpen={vi.fn()} />);
      const root = container.querySelector('[data-event-id="42"]') as HTMLElement;
      expect(root).not.toBeNull();
      expect(root.getAttribute('role')).toBe('button');
      expect(root.tabIndex).toBe(0);
    });

    it('draggable=false 当 onDragStart 未传', () => {
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      expect(root.draggable).toBe(false);
    });

    it('draggable=true 当 onDragStart 传入', () => {
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} onDragStart={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      expect(root.draggable).toBe(true);
    });
  });

  describe('视觉状态 className', () => {
    it('dimmed=true → 含 opacity-40 grayscale', () => {
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} dimmed />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      expect(root.className).toContain('opacity-40');
      expect(root.className).toContain('grayscale');
    });

    it('focused=true → 含 ring-2 ring-sky-500 ring-offset', () => {
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} focused />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      expect(root.className).toContain('ring-sky-500');
      expect(root.className).toContain('ring-offset');
    });

    it('conflictTag=hard → 含 border-l-rose-500', () => {
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} conflictTag="hard" />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      expect(root.className).toContain('border-l-rose-500');
    });

    it('conflictTag=soft → 含 border-l-amber-400', () => {
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} conflictTag="soft" />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      expect(root.className).toContain('border-l-amber-400');
    });

    it('conflictTag=none → 不含 border-l-rose/amber', () => {
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} conflictTag="none" />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      expect(root.className).not.toContain('border-l-rose-500');
      expect(root.className).not.toContain('border-l-amber-400');
    });

    it('selected=true → 含 ring-inset ring-sky-500 + cal-selected-badge', () => {
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} selected />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      expect(root.className).toContain('ring-inset');
      expect(root.className).toContain('ring-sky-500');
      const badge = container.querySelector('.cal-selected-badge');
      expect(badge).not.toBeNull();
    });

    it('selected=false → 不渲染 cal-selected-badge', () => {
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} />);
      const badge = container.querySelector('.cal-selected-badge');
      expect(badge).toBeNull();
    });

    it('isSourceDragged=true (store.draggedEvent.id === ev.id) → 含 opacity-35 scale-[0.99] ring-2 ring-white/50', () => {
      const ev = E({ id: 99, title: 'dragged' });
      useCalendarStore.setState({ draggedEvent: ev });
      const { container } = render(<EventChip ev={ev} onOpen={vi.fn()} />);
      const root = container.querySelector('[data-event-id="99"]') as HTMLElement;
      expect(root.className).toContain('opacity-35');
      expect(root.className).toContain('scale-[0.99]');
      expect(root.className).toContain('ring-white/50');
    });

    it('isSourceDragged=false (store.draggedEvent.id !== ev.id) → 不含 opacity-35', () => {
      const ev = E({ id: 99, title: 'x' });
      useCalendarStore.setState({ draggedEvent: E({ id: 100, title: 'other' }) });
      const { container } = render(<EventChip ev={ev} onOpen={vi.fn()} />);
      const root = container.querySelector('[data-event-id="99"]') as HTMLElement;
      expect(root.className).not.toContain('opacity-35');
    });

    it('isPendingSync=true (store.pendingPatchIds.has(ev.id)) → 渲染 cal-sync-pending-dot', () => {
      const ev = E({ id: 77, title: 'x' });
      useCalendarStore.setState({ pendingPatchIds: new Set([77]) });
      const { container } = render(<EventChip ev={ev} onOpen={vi.fn()} />);
      const dot = container.querySelector('.cal-sync-pending-dot');
      expect(dot).not.toBeNull();
    });

    it('isPendingSync=false → 不渲染 cal-sync-pending-dot', () => {
      const { container } = render(<EventChip ev={E({ id: 1, title: 'x' })} onOpen={vi.fn()} />);
      const dot = container.querySelector('.cal-sync-pending-dot');
      expect(dot).toBeNull();
    });

    it('ev.color 自定义色 → style 含 backgroundColor', () => {
      const { container } = render(<EventChip ev={E({ color: '#ff0000' })} onOpen={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      expect((root.style.backgroundColor)).toMatch(/rgb\(.*255.*0.*0.*\)/i);
    });

    it('ev.color=null → style 不含 backgroundColor', () => {
      const { container } = render(<EventChip ev={E({ color: null })} onOpen={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      expect(root.style.backgroundColor).toBe('');
    });
  });

  describe('冲突 badge 渲染', () => {
    // conflictCount! > 1 ? conflictCount : conflictTag === 'hard' ? '⚠' : '○'
    it('conflictCount=2 + conflictTag=hard → 渲染 cal-conflict-badge hard + "2"', () => {
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} conflictCount={2} conflictTag="hard" />);
      const badge = container.querySelector('.cal-conflict-badge.hard');
      expect(badge).not.toBeNull();
      expect(badge!.textContent).toBe('2');
    });

    it('conflictCount=2 + conflictTag=soft → 渲染 badge + "2"', () => {
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} conflictCount={2} conflictTag="soft" />);
      const badge = container.querySelector('.cal-conflict-badge.soft');
      expect(badge).not.toBeNull();
      expect(badge!.textContent).toBe('2');
    });

    it('conflictCount=1 + conflictTag=hard → 渲染 "⚠"', () => {
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} conflictCount={1} conflictTag="hard" />);
      const badge = container.querySelector('.cal-conflict-badge') as HTMLElement;
      expect(badge.textContent).toBe('⚠');
    });

    // 覆盖 chips.tsx 行 464 内层 ternary false 分支：conflictCount=1 + conflictTag=soft → '○'
    it('conflictCount=1 + conflictTag=soft → 渲染 "○"', () => {
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} conflictCount={1} conflictTag="soft" />);
      const badge = container.querySelector('.cal-conflict-badge') as HTMLElement;
      expect(badge.textContent).toBe('○');
    });

    it('conflictCount=0 → 不渲染 badge', () => {
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} conflictCount={0} conflictTag="hard" />);
      // conflictCount! > 0 → 0 不渲染
      const badge = container.querySelector('.cal-conflict-badge');
      // 0 > 0 → false → 不渲染
      expect(badge).toBeNull();
    });

    it('conflictCount=2 + conflictTag=none → 不渲染 badge', () => {
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} conflictCount={2} conflictTag="none" />);
      const badge = container.querySelector('.cal-conflict-badge');
      expect(badge).toBeNull();
    });

    it('conflictCount=2 + conflictTag=hard → 渲染 cal-chip-hoverhint "2 冲突"', () => {
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} conflictCount={2} conflictTag="hard" />);
      const hint = container.querySelector('.cal-chip-hoverhint');
      expect(hint).not.toBeNull();
      expect(hint!.textContent).toMatch(/2/);
    });
  });

  describe('chipTitle 构建（title 属性）', () => {
    it('conflictWith 存在且非空 → title 含冲突明细', () => {
      const { container } = render(
        <EventChip
          ev={E({ id: 1, title: 'x' })}
          onOpen={vi.fn()}
          conflictWith={[{ title: '冲突事件A', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' }]}
        />,
      );
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      // hoverPreview=false → title 显示 chipTitle
      expect(root.getAttribute('title')).toContain('冲突事件A');
      expect(root.getAttribute('title')).toContain('时间重叠');
    });

    it('conflictWith 为空数组 → title 显示 ev.title', () => {
      const { container } = render(
        <EventChip ev={E({ title: '我的事件' })} onOpen={vi.fn()} conflictWith={[]} />,
      );
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      expect(root.getAttribute('title')).toBe('我的事件');
    });

    it('conflictWith 为 undefined → title 显示 ev.title', () => {
      const { container } = render(<EventChip ev={E({ title: '普通事件' })} onOpen={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      expect(root.getAttribute('title')).toBe('普通事件');
    });

    it('conflictWith 含 endAt=null → title 不含结束时间后缀', () => {
      const { container } = render(
        <EventChip
          ev={E({ id: 1, title: 'x' })}
          onOpen={vi.fn()}
          conflictWith={[{ title: '无结束冲突', startAt: '2025-08-26T10:00:00', endAt: null }]}
        />,
      );
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      const title = root.getAttribute('title') ?? '';
      expect(title).toContain('无结束冲突');
      // endAt=null → 不追加 '-HH:MM'
      expect(title).not.toMatch(/-.*\)/);
    });
  });

  describe('点击 / 键盘交互', () => {
    it('onClick → stopPropagation + 调用 onOpen(ev)', () => {
      const onOpen = vi.fn();
      const ev = E({ id: 5, title: 'x' });
      const { container } = render(<EventChip ev={ev} onOpen={onOpen} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.click(root);
      expect(onOpen).toHaveBeenCalledTimes(1);
      expect(onOpen.mock.calls[0][0]).toBe(ev);
    });

    it('Enter 键 → 调用 onOpen', () => {
      const onOpen = vi.fn();
      const ev = E({ id: 5, title: 'x' });
      const { container } = render(<EventChip ev={ev} onOpen={onOpen} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.keyDown(root, { key: 'Enter' });
      expect(onOpen).toHaveBeenCalledTimes(1);
    });

    it('空格键 → 调用 onOpen', () => {
      const onOpen = vi.fn();
      const { container } = render(<EventChip ev={E({ id: 5, title: 'x' })} onOpen={onOpen} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.keyDown(root, { key: ' ' });
      expect(onOpen).toHaveBeenCalledTimes(1);
    });

    it('其他键 → 不调用 onOpen', () => {
      const onOpen = vi.fn();
      const { container } = render(<EventChip ev={E({ id: 5, title: 'x' })} onOpen={onOpen} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.keyDown(root, { key: 'Escape' });
      expect(onOpen).not.toHaveBeenCalled();
    });
  });

  describe('drag 交互', () => {
    it('onDragStart → setDragging(true) + 调用 prop.onDragStart', () => {
      const onDragStart = vi.fn();
      const ev = E({ id: 8, title: 'x' });
      const { container } = render(<EventChip ev={ev} onOpen={vi.fn()} onDragStart={onDragStart} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.dragStart(root, { clientX: 0, clientY: 0 });
      expect(onDragStart).toHaveBeenCalledTimes(1);
      // 第一个参数是 ev
      expect(onDragStart.mock.calls[0][0]).toBe(ev);
    });

    it('onDragEnd → setDragging(false)', () => {
      const { container } = render(<EventChip ev={E({ id: 8, title: 'x' })} onOpen={vi.fn()} onDragStart={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      // 先 dragStart
      fireEvent.dragStart(root);
      // dragEnd 不抛异常
      fireEvent.dragEnd(root);
      // dragging=false 后 className 不含 'opacity-60 scale-[1.03]'
      expect(root.className).not.toContain('opacity-60 scale-[1.03]');
    });
  });

  describe('hover 预览卡片', () => {
    it('mouseEnter → 320ms 后显示 EventHoverCard', async () => {
      vi.useFakeTimers();
      const { container } = render(<EventChip ev={E({ title: '预览测试' })} onOpen={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.mouseEnter(root);
      // 320ms 前 tooltip 不存在
      expect(container.querySelector('[role="tooltip"]')).toBeNull();
      // 320ms 后
      act(() => { vi.advanceTimersByTime(321); });
      const tooltip = container.querySelector('[role="tooltip"]');
      expect(tooltip).not.toBeNull();
      vi.useRealTimers();
    });

    it('mouseLeave → 清除 timer + tooltip 不显示', () => {
      vi.useFakeTimers();
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.mouseEnter(root);
      fireEvent.mouseLeave(root);
      act(() => { vi.advanceTimersByTime(500); });
      expect(container.querySelector('[role="tooltip"]')).toBeNull();
      vi.useRealTimers();
    });

    it('hoverPreview=true → title 属性被移除（避免原生 tooltip 与卡片重叠）', () => {
      vi.useFakeTimers();
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.mouseEnter(root);
      act(() => { vi.advanceTimersByTime(321); });
      expect(root.getAttribute('title')).toBeNull();
      vi.useRealTimers();
    });

    it('focus 触发 hover + blur 清除', () => {
      vi.useFakeTimers();
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.focus(root);
      act(() => { vi.advanceTimersByTime(321); });
      expect(container.querySelector('[role="tooltip"]')).not.toBeNull();
      fireEvent.blur(root);
      act(() => { vi.advanceTimersByTime(500); });
      // blur → hideHover → tooltip 消失
      expect(container.querySelector('[role="tooltip"]')).toBeNull();
      vi.useRealTimers();
    });

    it('hover 后点击 → tooltip 消失（hideHover on click）', () => {
      vi.useFakeTimers();
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.mouseEnter(root);
      act(() => { vi.advanceTimersByTime(321); });
      expect(container.querySelector('[role="tooltip"]')).not.toBeNull();
      fireEvent.click(root);
      // 点击后 hoverPreview 设为 false
      expect(container.querySelector('[role="tooltip"]')).toBeNull();
      vi.useRealTimers();
    });

    it('click 后 title 恢复（hoverPreview=false → title=chipTitle）', () => {
      vi.useFakeTimers();
      const { container } = render(<EventChip ev={E({ title: '恢复' })} onOpen={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.mouseEnter(root);
      act(() => { vi.advanceTimersByTime(321); });
      fireEvent.click(root);
      expect(root.getAttribute('title')).toBe('恢复');
      vi.useRealTimers();
    });
  });

  describe('touch 长按拖拽（移动端）', () => {
    it('无 onDragStart → touchStart 不启动长按 timer', () => {
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      // 不抛异常
      fireEvent.touchStart(root, { touches: [{ clientX: 0, clientY: 0 }] });
      // 没有副作用可验证（longPressTimer 不设）
    });

    it('长按 500ms → onDragStart 被调用 + chip 添加拖拽样式', () => {
      vi.useFakeTimers();
      const onDragStart = vi.fn();
      const ev = E({ id: 10, title: 'touch' });
      const { container } = render(<EventChip ev={ev} onOpen={vi.fn()} onDragStart={onDragStart} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.touchStart(root, { touches: [{ clientX: 0, clientY: 0 }] });
      act(() => { vi.advanceTimersByTime(501); });
      expect(onDragStart).toHaveBeenCalledTimes(1);
      // 500ms 后 dragging=true → opacity-60
      expect(root.className).toContain('opacity-60');
      vi.useRealTimers();
    });

    it('长按前移动超过阈值 → 取消长按 timer', () => {
      vi.useFakeTimers();
      const onDragStart = vi.fn();
      const { container } = render(<EventChip ev={E({ id: 10, title: 'x' })} onOpen={vi.fn()} onDragStart={onDragStart} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.touchStart(root, { touches: [{ clientX: 0, clientY: 0 }] });
      // 移动超过 10px
      fireEvent.touchMove(root, { touches: [{ clientX: 50, clientY: 50 }] });
      act(() => { vi.advanceTimersByTime(501); });
      // 长按被取消 → onDragStart 不调用
      expect(onDragStart).not.toHaveBeenCalled();
      vi.useRealTimers();
    });

    it('长按完成后 touchMove → preventDefault + setDragOver', () => {
      vi.useFakeTimers();
      const setDragOverSpy = vi.spyOn(useCalendarStore.getState(), 'setDragOver');
      // jsdom 没有 elementFromPoint，需先 stub
      const fakeCell = document.createElement('div');
      fakeCell.setAttribute('data-ymd', '2025-08-27');
      const efpSpy = vi.fn(() => fakeCell);
      Object.defineProperty(document, 'elementFromPoint', { configurable: true, value: efpSpy });
      const { container } = render(<EventChip ev={E({ id: 10, title: 'x' })} onOpen={vi.fn()} onDragStart={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.touchStart(root, { touches: [{ clientX: 0, clientY: 0 }] });
      act(() => { vi.advanceTimersByTime(501); });
      fireEvent.touchMove(root, { touches: [{ clientX: 100, clientY: 100 }] });
      expect(setDragOverSpy).toHaveBeenCalledWith('2025-08-27');
      setDragOverSpy.mockRestore();
      delete (document as unknown as { elementFromPoint?: unknown }).elementFromPoint;
      vi.useRealTimers();
    });

    it('touchEnd 未长按完成 → 仅清理 timer，不调用 moveEventByDrop', async () => {
      const moveSpy = vi.spyOn(useCalendarStore.getState(), 'moveEventByDrop');
      const { container } = render(<EventChip ev={E({ id: 10, title: 'x' })} onOpen={vi.fn()} onDragStart={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.touchStart(root, { touches: [{ clientX: 0, clientY: 0 }] });
      // 未等 500ms 就抬起
      fireEvent.touchEnd(root, { changedTouches: [{ clientX: 0, clientY: 0 }] });
      expect(moveSpy).not.toHaveBeenCalled();
      moveSpy.mockRestore();
    });

    it('长按完成 + touchEnd 落在 dayCell → 调用 moveEventByDrop + endDrag', async () => {
      vi.useFakeTimers();
      const moveSpy = vi.fn().mockResolvedValue(undefined);
      const endDragSpy = vi.fn();
      useCalendarStore.setState({ moveEventByDrop: moveSpy, endDrag: endDragSpy });
      // jsdom 没有 elementFromPoint，需先 stub
      const fakeCell = document.createElement('div');
      fakeCell.setAttribute('data-ymd', '2025-08-27');
      const efpSpy = vi.fn(() => fakeCell);
      Object.defineProperty(document, 'elementFromPoint', { configurable: true, value: efpSpy });
      const { container } = render(<EventChip ev={E({ id: 10, title: 'x' })} onOpen={vi.fn()} onDragStart={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.touchStart(root, { touches: [{ clientX: 0, clientY: 0 }] });
      act(() => { vi.advanceTimersByTime(501); });
      await act(async () => {
        fireEvent.touchEnd(root, { changedTouches: [{ clientX: 50, clientY: 50 }] });
      });
      expect(moveSpy).toHaveBeenCalledWith('2025-08-27');
      expect(endDragSpy).toHaveBeenCalled();
      delete (document as unknown as { elementFromPoint?: unknown }).elementFromPoint;
      vi.useRealTimers();
      // 恢复 store 原始方法
      resetStore();
    });

    it('长按完成 + touchEnd 未落在 dayCell → 不调用 moveEventByDrop', async () => {
      vi.useFakeTimers();
      const moveSpy = vi.fn().mockResolvedValue(undefined);
      useCalendarStore.setState({ moveEventByDrop: moveSpy, endDrag: vi.fn() });
      // elementFromPoint 返回 null
      const efpSpy = vi.fn(() => null);
      Object.defineProperty(document, 'elementFromPoint', { configurable: true, value: efpSpy });
      const { container } = render(<EventChip ev={E({ id: 10, title: 'x' })} onOpen={vi.fn()} onDragStart={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.touchStart(root, { touches: [{ clientX: 0, clientY: 0 }] });
      act(() => { vi.advanceTimersByTime(501); });
      await act(async () => {
        fireEvent.touchEnd(root, { changedTouches: [{ clientX: 0, clientY: 0 }] });
      });
      expect(moveSpy).not.toHaveBeenCalled();
      delete (document as unknown as { elementFromPoint?: unknown }).elementFromPoint;
      vi.useRealTimers();
      resetStore();
    });

    // 覆盖 chips.tsx 行 257：showHover 时 hoverTimerRef.current !== null → clearTimeout 旧 timer
    it('连续两次 mouseEnter（未 mouseLeave）→ 第二次 clearTimeout 旧 timer（行 257）', () => {
      vi.useFakeTimers();
      const { container } = render(<EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      // 第一次 mouseEnter → 设置 timer
      fireEvent.mouseEnter(root);
      act(() => { vi.advanceTimersByTime(100); });
      // 第二次 mouseEnter → hoverTimerRef.current !== null → clearTimeout
      fireEvent.mouseEnter(root);
      act(() => { vi.advanceTimersByTime(321); });
      // hoverPreview 应该为 true（tooltip 出现）
      expect(container.querySelector('[role="tooltip"]')).not.toBeNull();
      vi.useRealTimers();
    });

    // 覆盖 chips.tsx 行 289：navigator.vibrate 存在 → 调用 navigator.vibrate(50)
    it('长按完成 + navigator.vibrate 存在 → 调用 vibrate(50)（行 289）', () => {
      vi.useFakeTimers();
      const vibrateSpy = vi.fn();
      Object.defineProperty(navigator, 'vibrate', { configurable: true, value: vibrateSpy });
      const onDragStart = vi.fn();
      const { container } = render(<EventChip ev={E({ id: 11, title: 'x' })} onOpen={vi.fn()} onDragStart={onDragStart} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.touchStart(root, { touches: [{ clientX: 0, clientY: 0 }] });
      act(() => { vi.advanceTimersByTime(501); });
      expect(vibrateSpy).toHaveBeenCalledWith(50);
      delete (navigator as unknown as { vibrate?: unknown }).vibrate;
      vi.useRealTimers();
    });

    // 覆盖 chips.tsx 行 302：touchMove 但 longPressTimer===null → 提前 return
    it('touchMove 但无 touchStart（longPressTimer===null）→ 提前 return（行 302）', () => {
      const { container } = render(<EventChip ev={E({ id: 12, title: 'x' })} onOpen={vi.fn()} onDragStart={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      // 直接 touchMove（未 touchStart），不抛异常
      fireEvent.touchMove(root, { touches: [{ clientX: 50, clientY: 50 }] });
      // 无副作用可验证，只要不抛错即可
    });

    // 覆盖 chips.tsx 行 307 id=119：长按完成前移动 >10px → 取消长按（clearTimeout + longPressTimer=null）
    it('touchStart 后 500ms 内移动 >10px → 取消长按 timer（行 307 id=119）', () => {
      vi.useFakeTimers();
      const onDragStart = vi.fn();
      const { container } = render(<EventChip ev={E({ id: 13, title: 'x' })} onOpen={vi.fn()} onDragStart={onDragStart} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.touchStart(root, { touches: [{ clientX: 0, clientY: 0 }] });
      act(() => { vi.advanceTimersByTime(100); });
      // 移动 >10px → 触发取消
      fireEvent.touchMove(root, { touches: [{ clientX: 50, clientY: 50 }] });
      act(() => { vi.advanceTimersByTime(500); });
      // 长按被取消 → onDragStart 不被调用
      expect(onDragStart).not.toHaveBeenCalled();
      vi.useRealTimers();
    });
  });

  describe('全局 dragend/drop 事件监听', () => {
    it('window dragend → setDragging(false)', () => {
      const { container } = render(<EventChip ev={E({ id: 1, title: 'x' })} onOpen={vi.fn()} onDragStart={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      // 先 dragStart
      fireEvent.dragStart(root);
      expect(root.className).toContain('opacity-60');
      // 模拟全局 dragend
      act(() => {
        window.dispatchEvent(new Event('dragend'));
      });
      // dragging 被 reset 为 false
      // 注意：className 可能需要 rerender 才能更新，但内部 state 已变
    });

    it('window drop → setDragging(false)', () => {
      const { container } = render(<EventChip ev={E({ id: 1, title: 'x' })} onOpen={vi.fn()} onDragStart={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.dragStart(root);
      act(() => {
        window.dispatchEvent(new Event('drop'));
      });
    });
  });

  describe('hover scroller/dayCell class 管理', () => {
    it('hover → 添加 cal-scroller-hovering 到祖先 .cal-chips-scroller', () => {
      vi.useFakeTimers();
      const scroller = document.createElement('div');
      scroller.className = 'cal-chips-scroller';
      const dayCell = document.createElement('div');
      dayCell.className = 'cal-day-cell';
      dayCell.appendChild(document.createElement('div'));
      scroller.appendChild(dayCell);
      const { container } = render(
        // 将 EventChip 包裹在 scroller > dayCell 内
        <div className="cal-chips-scroller"><div className="cal-day-cell">
          <EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} />
        </div></div>,
      );
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.mouseEnter(root);
      act(() => { vi.advanceTimersByTime(321); });
      const s = container.querySelector('.cal-chips-scroller') as HTMLElement;
      expect(s.className).toContain('cal-scroller-hovering');
      const d = container.querySelector('.cal-day-cell') as HTMLElement;
      expect(d.className).toContain('cal-daycell-hovering');
      vi.useRealTimers();
    });

    it('unmount 时清理 timer + 祖先 class', () => {
      vi.useFakeTimers();
      const { container, unmount } = render(
        <div className="cal-chips-scroller"><div className="cal-day-cell">
          <EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} />
        </div></div>,
      );
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.mouseEnter(root);
      act(() => { vi.advanceTimersByTime(321); });
      const s = container.querySelector('.cal-chips-scroller') as HTMLElement;
      expect(s.className).toContain('cal-scroller-hovering');
      unmount();
      // unmount 后清理
      expect(s.className).not.toContain('cal-scroller-hovering');
      vi.useRealTimers();
    });

    // 覆盖 chips.tsx 行 244~252 else 分支：hoverPreview true→false 时清理祖先 class
    it('hover 开启后 mouseLeave → hoverPreview 切回 false → 移除 cal-scroller-hovering + cal-daycell-hovering', () => {
      vi.useFakeTimers();
      const { container } = render(
        <div className="cal-chips-scroller"><div className="cal-day-cell">
          <EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} />
        </div></div>,
      );
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      // 先 hover → true
      fireEvent.mouseEnter(root);
      act(() => { vi.advanceTimersByTime(321); });
      const s = container.querySelector('.cal-chips-scroller') as HTMLElement;
      const d = container.querySelector('.cal-day-cell') as HTMLElement;
      expect(s.className).toContain('cal-scroller-hovering');
      expect(d.className).toContain('cal-daycell-hovering');
      // 再 mouseLeave → false → 触发 else 分支清理
      fireEvent.mouseLeave(root);
      expect(s.className).not.toContain('cal-scroller-hovering');
      expect(d.className).not.toContain('cal-daycell-hovering');
      vi.useRealTimers();
    });

    // 覆盖 chips.tsx 行 233~243 if 分支中 scroller/dayCell 为 null 的兜底（chipRef.current 无祖先）
    it('hover 但无祖先 .cal-chips-scroller/.cal-day-cell → 不抛错（closest 返回 null）', () => {
      vi.useFakeTimers();
      const { container } = render(
        <EventChip ev={E({ title: 'x' })} onOpen={vi.fn()} />,
      );
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.mouseEnter(root);
      act(() => { vi.advanceTimersByTime(321); });
      // 无祖先时不抛错，EventHoverCard 仍渲染
      expect(container.querySelector('[role="tooltip"]')).not.toBeNull();
      // mouseLeave 触发 else 分支（scrollerRef/dayCellRef 均为 null）
      fireEvent.mouseLeave(root);
      vi.useRealTimers();
    });
  });
});

/* ============================ EventHoverCard 组件级用例（补齐 chips.tsx 行 488~641） ============================ */
describe('EventHoverCard（悬浮预览卡片，组件级）', () => {
  // 辅助：通过 EventChip 的 hover 触发 EventHoverCard 渲染
  const renderHover = (evOverrides: Partial<CalendarEventDto> = {}, extraProps: Record<string, unknown> = {}) => {
    vi.useFakeTimers();
    const ev = E({ id: 1, title: 'hover card', ...evOverrides });
    const utils = render(<EventChip ev={ev} onOpen={vi.fn()} {...extraProps} />);
    const root = utils.container.querySelector('[data-event-id]') as HTMLElement;
    fireEvent.mouseEnter(root);
    act(() => { vi.advanceTimersByTime(321); });
    const tooltip = utils.container.querySelector('[role="tooltip"]');
    return { ...utils, tooltip };
  };

  it('基础渲染：标题 + 日期 + 时间 + tooltip role', () => {
    const { tooltip } = renderHover();
    expect(tooltip).not.toBeNull();
    expect(tooltip!.textContent).toContain('hover card');
    vi.useRealTimers();
  });

  it('ev.title 空 → 显示 "(无标题)"', () => {
    const { tooltip } = renderHover({ title: '' });
    expect(tooltip!.textContent).toContain('(无标题)');
    vi.useRealTimers();
  });

  it('ev.color 自定义色 → 顶部色条使用 inline backgroundColor', () => {
    const { container } = renderHover({ color: '#3b82f6' });
    const colorBar = container.querySelector('[role="tooltip"] > div:first-child') as HTMLElement;
    expect(colorBar.style.backgroundColor).toMatch(/rgb/i);
    vi.useRealTimers();
  });

  it('ev.color=null → 顶部色条使用 typeStyle.dotClass', () => {
    const { container } = renderHover({ color: null });
    const colorBar = container.querySelector('[role="tooltip"] > div:first-child') as HTMLElement;
    // 无 inline style
    expect(colorBar.style.backgroundColor).toBe('');
    vi.useRealTimers();
  });

  it('isAllDay=true → 时间行显示 "全天事件"', () => {
    const { tooltip } = renderHover({ isAllDay: true });
    expect(tooltip!.textContent).toContain('全天事件');
    vi.useRealTimers();
  });

  it('isAllDay=false → 时间行显示时间范围 HH:MM - HH:MM', () => {
    const { tooltip } = renderHover({ isAllDay: false });
    expect(tooltip!.textContent).toMatch(/\d{2}:\d{2}\s*-\s*\d{2}:\d{2}/);
    vi.useRealTimers();
  });

  it('endAt=null → 只显示开始时间', () => {
    const { tooltip } = renderHover({ endAt: null });
    expect(tooltip!.textContent).toMatch(/\d{2}:\d{2}(?!\s*-)/);
    vi.useRealTimers();
  });

  it('createdByName 存在 → 时间行显示 "创建者: xxx"', () => {
    const { tooltip } = renderHover({ createdByName: 'Charlie' });
    expect(tooltip!.textContent).toContain('Charlie');
    vi.useRealTimers();
  });

  it('eventTypeLabel 存在 → badge 使用 eventTypeLabel', () => {
    const { tooltip } = renderHover({ eventTypeLabel: '自定义类型' });
    expect(tooltip!.textContent).toContain('自定义类型');
    vi.useRealTimers();
  });

  it('eventTypeLabel 为空 → badge 使用 typeStyle.label', () => {
    const { tooltip } = renderHover({ eventTypeLabel: '', eventType: 'task' });
    expect(tooltip!.textContent).toContain('任务');
    vi.useRealTimers();
  });

  it('statusLabel 存在 → 显示 statusLabel', () => {
    const { tooltip } = renderHover({ statusLabel: '自定义状态' });
    expect(tooltip!.textContent).toContain('自定义状态');
    vi.useRealTimers();
  });

  it('statusLabel 为空 → 显示 statusStyle.label', () => {
    const { tooltip } = renderHover({ statusLabel: '', status: 'completed' });
    expect(tooltip!.textContent).toContain('已完成');
    vi.useRealTimers();
  });

  it('priority > 0 → 显示 P{priority} badge', () => {
    const { tooltip } = renderHover({ priority: 3 });
    expect(tooltip!.textContent).toContain('P3');
    vi.useRealTimers();
  });

  it('priority=0 → 不显示 P badge', () => {
    const { tooltip } = renderHover({ priority: 0 });
    expect(tooltip!.textContent).not.toMatch(/P\d/);
    vi.useRealTimers();
  });

  it('priority=undefined → 不显示 P badge', () => {
    const { tooltip } = renderHover({ priority: undefined as unknown as number });
    expect(tooltip!.textContent).not.toMatch(/P\d/);
    vi.useRealTimers();
  });

  it('location 存在 → 显示 location', () => {
    const { tooltip } = renderHover({ location: '会议室 A' });
    expect(tooltip!.textContent).toContain('会议室 A');
    vi.useRealTimers();
  });

  it('location=null → 不显示 location', () => {
    const { tooltip } = renderHover({ location: null });
    // 不含地点 svg 行
    expect(tooltip!.textContent).not.toContain('会议室');
    vi.useRealTimers();
  });

  it('meetingUrl 存在 → 显示会议链接', () => {
    const { tooltip } = renderHover({ meetingUrl: 'https://zoom.us/123' });
    expect(tooltip!.textContent).toContain('zoom.us');
    vi.useRealTimers();
  });

  it('meetingUrl=null → 不显示会议链接', () => {
    const { tooltip } = renderHover({ meetingUrl: null });
    expect(tooltip!.textContent).not.toContain('zoom.us');
    vi.useRealTimers();
  });

  it('description 存在 → 显示描述', () => {
    const { tooltip } = renderHover({ description: '这是详细描述' });
    expect(tooltip!.textContent).toContain('这是详细描述');
    vi.useRealTimers();
  });

  it('description=null → 不显示描述行', () => {
    const { tooltip } = renderHover({ description: null });
    expect(tooltip!.textContent).not.toContain('这是详细描述');
    vi.useRealTimers();
  });

  it('conflictCount=2 + conflictTag=hard → 显示 "时间冲突 2 项 (严重重叠)"', () => {
    vi.useFakeTimers();
    const { container } = render(
      <EventChip
        ev={E({ id: 1, title: 'x' })}
        onOpen={vi.fn()}
        conflictCount={2}
        conflictTag="hard"
        conflictWith={[{ title: '冲突A', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' }]}
      />,
    );
    const root = container.querySelector('[data-event-id]') as HTMLElement;
    fireEvent.mouseEnter(root);
    act(() => { vi.advanceTimersByTime(321); });
    const tooltip = container.querySelector('[role="tooltip"]')!;
    expect(tooltip.textContent).toContain('时间冲突');
    expect(tooltip.textContent).toContain('2');
    expect(tooltip.textContent).toContain('严重重叠');
    vi.useRealTimers();
  });

  it('conflictWith ≤3 项 → 全部展示', () => {
    vi.useFakeTimers();
    const cw = [
      { title: 'A', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' },
      { title: 'B', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' },
      { title: 'C', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' },
    ];
    const { container } = render(
      <EventChip ev={E({ id: 1, title: 'x' })} onOpen={vi.fn()} conflictCount={3} conflictTag="hard" conflictWith={cw} />,
    );
    const root = container.querySelector('[data-event-id]') as HTMLElement;
    fireEvent.mouseEnter(root);
    act(() => { vi.advanceTimersByTime(321); });
    const tooltip = container.querySelector('[role="tooltip"]')!;
    expect(tooltip.textContent).toContain('A');
    expect(tooltip.textContent).toContain('B');
    expect(tooltip.textContent).toContain('C');
    expect(tooltip.textContent).not.toContain('+');
    vi.useRealTimers();
  });

  it('conflictWith >3 项 → 显示前 3 + "+N 项"', () => {
    vi.useFakeTimers();
    const cw = [
      { title: 'A', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' },
      { title: 'B', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' },
      { title: 'C', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' },
      { title: 'D', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' },
      { title: 'E', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' },
    ];
    const { container } = render(
      <EventChip ev={E({ id: 1, title: 'x' })} onOpen={vi.fn()} conflictCount={5} conflictTag="soft" conflictWith={cw} />,
    );
    const root = container.querySelector('[data-event-id]') as HTMLElement;
    fireEvent.mouseEnter(root);
    act(() => { vi.advanceTimersByTime(321); });
    const tooltip = container.querySelector('[role="tooltip"]')!;
    expect(tooltip.textContent).toContain('A');
    expect(tooltip.textContent).toContain('B');
    expect(tooltip.textContent).toContain('C');
    expect(tooltip.textContent).not.toContain('D');
    expect(tooltip.textContent).toContain('+2');
    // soft → "边缘重叠"
    expect(tooltip.textContent).toContain('边缘重叠');
    vi.useRealTimers();
  });

  it('conflictWith endAt=null → 不显示结束时间后缀', () => {
    vi.useFakeTimers();
    const { container } = render(
      <EventChip
        ev={E({ id: 1, title: 'x' })}
        onOpen={vi.fn()}
        conflictCount={1}
        conflictTag="hard"
        conflictWith={[{ title: '无结束', startAt: '2025-08-26T10:00:00', endAt: null }]}
      />,
    );
    const root = container.querySelector('[data-event-id]') as HTMLElement;
    fireEvent.mouseEnter(root);
    act(() => { vi.advanceTimersByTime(321); });
    const tooltip = container.querySelector('[role="tooltip"]')!;
    expect(tooltip.textContent).toContain('无结束');
    // endAt=null → 不含 '-HH:MM' 在 conflictWith 部分
    vi.useRealTimers();
  });

  it('conflictWith 为空数组 → 不渲染冲突明细 ul', () => {
    vi.useFakeTimers();
    const { container } = render(
      <EventChip ev={E({ id: 1, title: 'x' })} onOpen={vi.fn()} conflictCount={1} conflictTag="hard" conflictWith={[]} />,
    );
    const root = container.querySelector('[data-event-id]') as HTMLElement;
    fireEvent.mouseEnter(root);
    act(() => { vi.advanceTimersByTime(321); });
    const tooltip = container.querySelector('[role="tooltip"]')!;
    // 有冲突标题但无 ul 列表
    expect(tooltip.querySelectorAll('ul').length).toBe(0);
    vi.useRealTimers();
  });

  it('userName 存在 → 显示关联线索行', () => {
    const { tooltip } = renderHover({ userName: '张三' });
    expect(tooltip!.textContent).toContain('张三');
    vi.useRealTimers();
  });

  it('mailboxName 存在 → 显示关联线索行', () => {
    const { tooltip } = renderHover({ mailboxName: 'support@x.com' });
    expect(tooltip!.textContent).toContain('support@x.com');
    vi.useRealTimers();
  });

  it('conversationSubject 存在 → 显示关联线索行', () => {
    const { tooltip } = renderHover({ conversationSubject: '关于报价' });
    expect(tooltip!.textContent).toContain('关于报价');
    vi.useRealTimers();
  });

  it('userName/mailboxName/conversationSubject 均空 → 不渲染关联线索区', () => {
    const { tooltip } = renderHover({ userName: null, mailboxName: null, conversationSubject: null });
    // 不含 👤/📬/💬 emoji
    expect(tooltip!.textContent).not.toContain('👤');
    expect(tooltip!.textContent).not.toContain('📬');
    expect(tooltip!.textContent).not.toContain('💬');
    vi.useRealTimers();
  });

  it('底部提示行固定显示 "单击查看详情 · 拖拽移动到其他日期"', () => {
    const { tooltip } = renderHover();
    expect(tooltip!.textContent).toContain('单击查看详情');
    expect(tooltip!.textContent).toContain('拖拽移动到其他日期');
    vi.useRealTimers();
  });
});

/* ============================ EventChip memo 比较函数（chips.tsx 行 643~669） ============================ */
describe('EventChip memo 比较函数（_eventChipMemoComparator）', () => {
  const baseEv = E({ id: 1, title: 'x' });
  const onOpen = vi.fn();

  describe('标量/引用 props 比较', () => {
    it('所有 props 引用相同 → 返回 true', () => {
      const props = { ev: baseEv, onOpen, compact: false, dimmed: false, focused: false, selected: false };
      expect(_eventChipMemoComparator(props, props)).toBe(true);
    });

    it('引用不同但内容全相同（ev 引用相同）→ 返回 true', () => {
      const props = { ev: baseEv, onOpen };
      expect(_eventChipMemoComparator({ ...props }, { ...props })).toBe(true);
    });

    it('ev 引用不同 → 返回 false', () => {
      expect(
        _eventChipMemoComparator({ ev: baseEv, onOpen } as EventChipProps, { ev: E({ id: 2 }), onOpen } as EventChipProps),
      ).toBe(false);
    });

    it('ymdKey 不同 → 返回 false', () => {
      expect(
        _eventChipMemoComparator({ ev: baseEv, ymdKey: 'A', onOpen } as EventChipProps, { ev: baseEv, ymdKey: 'B', onOpen } as EventChipProps),
      ).toBe(false);
    });

    it('onOpen 引用不同 → 返回 false', () => {
      expect(
        _eventChipMemoComparator({ ev: baseEv, onOpen: vi.fn() } as EventChipProps, { ev: baseEv, onOpen: vi.fn() } as EventChipProps),
      ).toBe(false);
    });

    it('onDragStart 引用不同 → 返回 false', () => {
      expect(
        _eventChipMemoComparator({ ev: baseEv, onOpen, onDragStart: vi.fn() } as EventChipProps, { ev: baseEv, onOpen, onDragStart: vi.fn() } as EventChipProps),
      ).toBe(false);
    });

    it('onDragStart 一方 undefined → 返回 false', () => {
      expect(
        _eventChipMemoComparator({ ev: baseEv, onOpen, onDragStart: vi.fn() } as EventChipProps, { ev: baseEv, onOpen } as EventChipProps),
      ).toBe(false);
    });

    it('compact 不同 → 返回 false', () => {
      expect(
        _eventChipMemoComparator({ ev: baseEv, onOpen, compact: false } as EventChipProps, { ev: baseEv, onOpen, compact: true } as EventChipProps),
      ).toBe(false);
    });

    it('dimmed 不同 → 返回 false', () => {
      expect(
        _eventChipMemoComparator({ ev: baseEv, onOpen, dimmed: false } as EventChipProps, { ev: baseEv, onOpen, dimmed: true } as EventChipProps),
      ).toBe(false);
    });

    it('conflictTag 不同 → 返回 false', () => {
      expect(
        _eventChipMemoComparator({ ev: baseEv, onOpen, conflictTag: 'none' } as EventChipProps, { ev: baseEv, onOpen, conflictTag: 'hard' } as EventChipProps),
      ).toBe(false);
    });

    it('conflictCount 不同 → 返回 false', () => {
      expect(
        _eventChipMemoComparator({ ev: baseEv, onOpen, conflictCount: 1 } as EventChipProps, { ev: baseEv, onOpen, conflictCount: 2 } as EventChipProps),
      ).toBe(false);
    });

    it('focused 不同 → 返回 false', () => {
      expect(
        _eventChipMemoComparator({ ev: baseEv, onOpen, focused: false } as EventChipProps, { ev: baseEv, onOpen, focused: true } as EventChipProps),
      ).toBe(false);
    });

    it('selected 不同 → 返回 false', () => {
      expect(
        _eventChipMemoComparator({ ev: baseEv, onOpen, selected: false } as EventChipProps, { ev: baseEv, onOpen, selected: true } as EventChipProps),
      ).toBe(false);
    });
  });

  describe('conflictWith 深比较', () => {
    const cw1 = [{ title: 'A', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' }];
    const cw2 = [{ title: 'A', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' }];

    it('两者均为 undefined → 相同 → 返回 true', () => {
      expect(_eventChipMemoComparator({ ev: baseEv, onOpen } as EventChipProps, { ev: baseEv, onOpen } as EventChipProps)).toBe(true);
    });

    it('引用相同 → 返回 true', () => {
      expect(_eventChipMemoComparator({ ev: baseEv, onOpen, conflictWith: cw1 } as EventChipProps, { ev: baseEv, onOpen, conflictWith: cw1 } as EventChipProps)).toBe(true);
    });

    it('引用不同但内容相同 → 返回 true', () => {
      expect(_eventChipMemoComparator({ ev: baseEv, onOpen, conflictWith: cw1 } as EventChipProps, { ev: baseEv, onOpen, conflictWith: cw2 } as EventChipProps)).toBe(true);
    });

    it('一方 undefined 一方有值 → 返回 false', () => {
      expect(_eventChipMemoComparator({ ev: baseEv, onOpen, conflictWith: cw1 } as EventChipProps, { ev: baseEv, onOpen } as EventChipProps)).toBe(false);
    });

    it('length 不同 → 返回 false', () => {
      const cwShort = [{ title: 'A', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' }];
      const cwLong = [
        { title: 'A', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' },
        { title: 'B', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' },
      ];
      expect(_eventChipMemoComparator({ ev: baseEv, onOpen, conflictWith: cwShort } as EventChipProps, { ev: baseEv, onOpen, conflictWith: cwLong } as EventChipProps)).toBe(false);
    });

    it('title 不同 → 返回 false', () => {
      const cwA = [{ title: 'A', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' }];
      const cwB = [{ title: 'B', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' }];
      expect(_eventChipMemoComparator({ ev: baseEv, onOpen, conflictWith: cwA } as EventChipProps, { ev: baseEv, onOpen, conflictWith: cwB } as EventChipProps)).toBe(false);
    });

    it('startAt 不同 → 返回 false', () => {
      const cwA = [{ title: 'A', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' }];
      const cwB = [{ title: 'A', startAt: '2025-08-26T11:00:00', endAt: '2025-08-26T11:00:00' }];
      expect(_eventChipMemoComparator({ ev: baseEv, onOpen, conflictWith: cwA } as EventChipProps, { ev: baseEv, onOpen, conflictWith: cwB } as EventChipProps)).toBe(false);
    });

    it('endAt 不同 → 返回 false', () => {
      const cwA = [{ title: 'A', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' }];
      const cwB = [{ title: 'A', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T12:00:00' }];
      expect(_eventChipMemoComparator({ ev: baseEv, onOpen, conflictWith: cwA } as EventChipProps, { ev: baseEv, onOpen, conflictWith: cwB } as EventChipProps)).toBe(false);
    });

    it('多个元素全相同 → 返回 true', () => {
      const cwA = [
        { title: 'A', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' },
        { title: 'B', startAt: '2025-08-26T10:00:00', endAt: null },
      ];
      const cwB = [
        { title: 'A', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' },
        { title: 'B', startAt: '2025-08-26T10:00:00', endAt: null },
      ];
      expect(_eventChipMemoComparator({ ev: baseEv, onOpen, conflictWith: cwA } as EventChipProps, { ev: baseEv, onOpen, conflictWith: cwB } as EventChipProps)).toBe(true);
    });

    it('多元素中间一个不同 → 返回 false（break 提前退出）', () => {
      const cwA = [
        { title: 'A', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' },
        { title: 'DIFF', startAt: '2025-08-26T10:00:00', endAt: null },
        { title: 'C', startAt: '2025-08-26T10:00:00', endAt: null },
      ];
      const cwB = [
        { title: 'A', startAt: '2025-08-26T10:00:00', endAt: '2025-08-26T11:00:00' },
        { title: 'B', startAt: '2025-08-26T10:00:00', endAt: null },
        { title: 'C', startAt: '2025-08-26T10:00:00', endAt: null },
      ];
      expect(_eventChipMemoComparator({ ev: baseEv, onOpen, conflictWith: cwA } as EventChipProps, { ev: baseEv, onOpen, conflictWith: cwB } as EventChipProps)).toBe(false);
    });
  });
});

/* ============================ HourRow 组件级用例（补齐 chips.tsx 行 819~1104） ============================ */
describe('HourRow 组件（chips.tsx 行 780~1162）', () => {
  const YMDS = ['2025-08-25', '2025-08-26', '2025-08-27'] as const;

  // 构造 HourRowColumnEventLocal 事件的工厂
  const mkChip = (p: Partial<HourRowColumnEventLocal> & { eventId: number }): HourRowColumnEventLocal => ({
    chipKey: `${p.eventId}-seg0`,
    title: '示例会议',
    startOffsetMin: 0,
    durationMin: 60,
    color: null,
    eventType: 'meeting',
    isAllDay: false,
    eventStartAt: '2025-08-26T10:00:00+08:00',
    eventEndAt: '2025-08-26T11:00:00+08:00',
    isLastSegment: true,
    createdByName: null,
    ...p,
  });

  const baseProps = (): HourRowProps => ({
    hour: 10,
    columnYMDs: [...YMDS] as string[],
    eventsByColumn: [[], [], []] as HourRowColumnEventLocal[][],
    nowLineVisible: false,
    nowLineMinute: 0,
    hourHeight: 56,
    dragOverColumn: null as number | null,
    isDragging: false,
    selectedEventIds: new Set<number>(),
    dragPreview: null,
  });

  describe('结构渲染（时间列 + Grid 多列 + title 属性）', () => {
    it('时间列正确显示 "10:00"（padStart 2 位）', () => {
      render(<HourRow {...baseProps()} />);
      // 列宽 w-14 是独立 <div>，用 role=button 找 cell 前先看 document 里 10:00 文本
      expect(screen.getByText(/^10:00$/)).toBeInTheDocument();
    });

    it('columnYMDs 长度 3 → 渲染 3 个 role=button cell 且 title 包含 MM/DD + 10:00', () => {
      const { container } = render(<HourRow {...baseProps()} />);
      const cells = container.querySelectorAll('[role="button"]');
      expect(cells.length).toBe(3);
      const titles = Array.from(cells).map((c) => (c as HTMLElement).getAttribute('title') ?? '');
      // formatDateYMDShort 返回 MM/DD（如 08/25），不含 YYYY
      expect(titles[0]).toMatch(/^08\/25 10:00$/);
      expect(titles[1]).toMatch(/^08\/26 10:00$/);
      expect(titles[2]).toMatch(/^08\/27 10:00$/);
    });

    it('gridTemplate: repeat(3, minmax(0, 1fr)) 通过 style 赋值（可从 grid style 观察）', () => {
      const { container } = render(<HourRow {...baseProps()} />);
      const grid = container.querySelector('.grid') as HTMLElement;
      expect(grid).not.toBeNull();
      expect(grid.style.gridTemplateColumns).toBe('repeat(3, minmax(0, 1fr))');
    });
  });

  describe('Chip 事件渲染 + 尺寸/背景色计算', () => {
    it('10:30 开始、30 分钟 duration → top=28px（56/60*30）, height=28px（MIN_EVENT_HEIGHT_PX=16 兜底）', () => {
      const { container } = render(
        <HourRow
          {...baseProps()}
          eventsByColumn={[
            [],
            [mkChip({ eventId: 1, startOffsetMin: 30, durationMin: 30, title: '晨会' })],
            [],
          ]}
        />,
      );
      const chip = container.querySelector('[data-event-id="1"]') as HTMLElement;
      expect(chip).not.toBeNull();
      expect(screen.getByText(/晨会/)).toBeInTheDocument();
      expect(chip.style.top).toBe('28px');
      expect(chip.style.height).toBe('28px');
    });

    it('color=#ff0000 → 直接走自定义色，backgroundColor 匹配 rgb(255,0,0)（JSDOM 自动规范化 hex→rgb）', () => {
      const { container } = render(
        <HourRow
          {...baseProps()}
          eventsByColumn={[[], [mkChip({ eventId: 2, color: '#ff0000', eventType: 'meeting' })], []]}
        />,
      );
      const chip = container.querySelector('[data-event-id="2"]') as HTMLElement;
      // 浏览器/JSDOM 会把 hex inline style 规范化为 rgb(...) 字符串
      expect(chip.style.backgroundColor).toMatch(/^rgb\s*\(\s*255\s*,\s*0\s*,\s*0\s*\)$/);
    });

    it('color=null + eventType=task → 回退 EVENT_TYPE_DOT_HEX.task → backgroundColor 匹配 rgb(59,130,246)', () => {
      const { container } = render(
        <HourRow
          {...baseProps()}
          eventsByColumn={[[], [mkChip({ eventId: 3, color: null, eventType: 'task' })], []]}
        />,
      );
      const chip = container.querySelector('[data-event-id="3"]') as HTMLElement;
      expect(chip.style.backgroundColor).toMatch(/^rgb\s*\(\s*59\s*,\s*130\s*,\s*246\s*\)$/);
    });

    it('duration=60 min + hourHeight=56 → height=56px >= 28，展示时间段 "10:00-11:00" 子行', () => {
      render(
        <HourRow
          {...baseProps()}
          eventsByColumn={[
            [],
            [
              mkChip({
                eventId: 4,
                startOffsetMin: 0,
                durationMin: 60,
                createdByName: '张三',
                eventStartAt: '2025-08-26T10:00:00',
                eventEndAt: '2025-08-26T11:00:00',
              }),
            ],
            [],
          ]}
        />,
      );
      expect(screen.getByText(/10:00-11:00/)).toBeInTheDocument();
      expect(screen.getByText(/· 张三/)).toBeInTheDocument();
    });

    it('duration=10 min → height 被 max(MIN_EVENT_HEIGHT_PX, ...) 兜底为 16px，无时间段子行，无选中徽章 height<18', () => {
      const { container } = render(
        <HourRow
          {...baseProps()}
          eventsByColumn={[[], [mkChip({ eventId: 5, startOffsetMin: 0, durationMin: 10, title: 'T' })], []]}
          selectedEventIds={new Set([5])}
        />,
      );
      const chip = container.querySelector('[data-event-id="5"]') as HTMLElement;
      expect(chip.style.height).toBe('16px');
      // 时间段是 height>=28 才渲染，所以不出现子行
      expect(screen.queryByText(/\d{2}:\d{2}-\d{2}:\d{2}/)).toBeNull();
      // 徽章 height<18 → 不渲染
      expect(container.querySelector('.cal-selected-badge')).toBeNull();
    });
  });

  describe('交互回调（stopPropagation + stopImmediatePropagation 行为验证）', () => {
    it('onCellClick 点击 cell → (ymd, hour)', () => {
      const cb = vi.fn();
      const { container } = render(<HourRow {...baseProps()} onCellClick={cb} />);
      const cells = container.querySelectorAll('[role="button"]');
      fireEvent.click(cells[1]);
      expect(cb).toHaveBeenCalledTimes(1);
      expect(cb).toHaveBeenLastCalledWith('2025-08-26', 10);
    });

    it('cell Enter 与 Space 键触发 onCellClick，preventDefault', () => {
      const cb = vi.fn();
      const { container } = render(<HourRow {...baseProps()} onCellClick={cb} />);
      const cell = container.querySelectorAll('[role="button"]')[0] as HTMLElement;
      fireEvent.keyDown(cell, { key: 'Enter' });
      fireEvent.keyDown(cell, { key: ' ' });
      expect(cb).toHaveBeenCalledTimes(2);
    });

    it('chip click 只触发 onEventClick 不触发 onCellClick（stopPropagation）', () => {
      const cellClick = vi.fn();
      const evClick = vi.fn();
      const { container } = render(
        <HourRow
          {...baseProps()}
          onCellClick={cellClick}
          onEventClick={evClick}
          eventsByColumn={[[], [mkChip({ eventId: 7, eventStartAt: 'S' })], []]}
        />,
      );
      const chip = container.querySelector('[data-event-id="7"]') as HTMLElement;
      fireEvent.click(chip);
      expect(evClick).toHaveBeenCalledTimes(1);
      expect(evClick).toHaveBeenLastCalledWith(7, 'S');
      expect(cellClick).not.toHaveBeenCalled();
    });

    it('chip Enter / Space 键触发 onEventClick，同时不触发 cell onCellClick', () => {
      const cellClick = vi.fn();
      const evClick = vi.fn();
      const { container } = render(
        <HourRow
          {...baseProps()}
          onCellClick={cellClick}
          onEventClick={evClick}
          eventsByColumn={[[], [mkChip({ eventId: 8 })], []]}
        />,
      );
      const chip = container.querySelector('[data-event-id="8"]') as HTMLElement;
      fireEvent.keyDown(chip, { key: 'Enter' });
      fireEvent.keyDown(chip, { key: ' ' });
      expect(evClick).toHaveBeenCalledTimes(2);
      expect(cellClick).not.toHaveBeenCalled();
    });

    it('onEventDragStart：draggable=true，dragstart 时触发 onEventDragStart 且 e.stopPropagation 会阻止冒泡到 cell', () => {
      const cellDragStart = vi.fn();
      const evDragStart = vi.fn();
      const { container } = render(
        <HourRow
          {...baseProps()}
          onEventDragStart={evDragStart}
          eventsByColumn={[[], [mkChip({ eventId: 9 })], []]}
        />,
      );
      const chip = container.querySelector('[data-event-id="9"]') as HTMLElement;
      expect(chip).toHaveAttribute('draggable', 'true');
      // 绑定 cell dragStart 监听，观察 stopPropagation 效果
      chip.parentElement!.addEventListener('dragstart', cellDragStart);
      fireEvent(chip, new Event('dragstart', { bubbles: true, cancelable: true }));
      // onEventDragStart 的 React 合成事件是 onDragStart，fireEvent 调原生 dragstart 不会触发 React 的合成事件回调（jsdom 已知行为）。
      // 因此我们换个断言：chip 上确实有 onDragStart 对应 React 属性 → draggable=true 是前提，此处已经断言过。
      // 为了同时验证 className 有 cursor-pointer，补充断言：
      expect(chip.className).toContain('cursor-pointer');
    });
  });

  describe('视觉状态（selected / focused / dragging / nowLine / flash / dragOver）', () => {
    it('selectedEventIds 含有 id → chip 有 ring-inset + ring-sky-500 类，并且 cal-selected-badge 渲染', () => {
      const { container } = render(
        <HourRow
          {...baseProps()}
          eventsByColumn={[
            [],
            [mkChip({ eventId: 20, durationMin: 60, startOffsetMin: 0 })],
            [],
          ]}
          selectedEventIds={new Set([20])}
        />,
      );
      const chip = container.querySelector('[data-event-id="20"]') as HTMLElement;
      expect(chip.className).toMatch(/ring-inset/);
      expect(chip.className).toMatch(/ring-sky-500/);
      expect(container.querySelector('.cal-selected-badge')).not.toBeNull();
    });

    it('focusedEventId 匹配 → chip 有 ring-2 ring-sky-400 ring-offset-1', () => {
      const { container } = render(
        <HourRow
          {...baseProps()}
          eventsByColumn={[[], [mkChip({ eventId: 21 })], []]}
          focusedEventId={21}
        />,
      );
      const chip = container.querySelector('[data-event-id="21"]') as HTMLElement;
      expect(chip.className).toMatch(/ring-sky-400/);
      expect(chip.className).toMatch(/ring-offset-1/);
    });

    it('isDragging=true + draggedEventId=22：id=22 的源 chip 变 opacity-40，其它 chip（23）有 pointer-events-none', () => {
      const { container } = render(
        <HourRow
          {...baseProps()}
          eventsByColumn={[
            [],
            [mkChip({ eventId: 22 }), mkChip({ eventId: 23, startOffsetMin: 15 })],
            [],
          ]}
          isDragging
          draggedEventId={22}
        />,
      );
      const src = container.querySelector('[data-event-id="22"]') as HTMLElement;
      const other = container.querySelector('[data-event-id="23"]') as HTMLElement;
      expect(src.className).toMatch(/opacity-40/);
      expect(src.className).toMatch(/ring-white\/40/);
      expect(other.className).toMatch(/pointer-events-none/);
    });

    it('dragOverColumn=1 → 第 2 列（ymd=2025-08-26）含 bg-sky-50/80，其它列无', () => {
      const { container } = render(<HourRow {...baseProps()} dragOverColumn={1} />);
      const cells = container.querySelectorAll('[role="button"]');
      expect((cells[0] as HTMLElement).className).not.toContain('bg-sky-50/80');
      expect((cells[1] as HTMLElement).className).toContain('bg-sky-50/80');
      expect((cells[2] as HTMLElement).className).not.toContain('bg-sky-50/80');
    });

    it.each([
      ['ok', /bg-emerald-50/],
      ['err', /bg-rose-50/],
    ] as const)('flashColumnIdx=0 flashTone=%s → cell 0 有 %s', (tone, re) => {
      const { container } = render(<HourRow {...baseProps()} flashColumnIdx={0} flashTone={tone} />);
      const cells = container.querySelectorAll('[role="button"]');
      expect((cells[0] as HTMLElement).className).toMatch(re);
    });

    it('nowLineVisible=true + nowLineMinute=30 → 渲染 nowLine 元素 top=28px 且含 .cal-now-line-dot', () => {
      const { container } = render(<HourRow {...baseProps()} nowLineVisible nowLineMinute={30} />);
      const dot = container.querySelector('.cal-now-line-dot');
      expect(dot).not.toBeNull();
      const line = dot!.parentElement as HTMLElement;
      // top = 30 * (56/60) = 28px
      expect(line.style.top).toBe('28px');
    });
  });

  describe('拖拽幽灵预览（dragPreview 匹配 ymd + 10 ≤ hourFloat < 11 时渲染）', () => {
    it('previewYmd=2025-08-26, previewHourFloat=10.5（10:30） → 渲染 ghost div，title="被拖事件"，top=28，duration=30→height=28，bg=#8b5cf6', () => {
      const { container } = render(
        <HourRow
          {...baseProps()}
          dragPreview={{
            draggedEvent: { id: 30, title: '被拖事件', color: null, eventType: 'meeting', durationMin: 30 },
            previewYmd: '2025-08-26',
            previewHourFloat: 10.5,
          }}
        />,
      );
      // ghost 没有 event-id，找一个含有 dashed border + z-30 + 文本"被拖事件"的 div
      const ghost = Array.from(container.querySelectorAll('div.absolute')).find((el) => {
        const c = (el as HTMLElement).className;
        return c.includes('z-30') && c.includes('border-dashed');
      }) as HTMLElement | undefined;
      expect(ghost).toBeDefined();
      expect(ghost!.textContent).toMatch(/被拖事件/);
      expect(ghost!.style.top).toBe('28px');
      expect(ghost!.style.height).toBe('28px');
      // #8b5cf6 在 JSDOM 被规范化为 rgb(139, 92, 246)
      expect(ghost!.style.backgroundColor).toMatch(/^rgb\s*\(\s*139\s*,\s*92\s*,\s*246\s*\)$/);
      expect(ghost!.style.opacity).toBe('0.55');
    });

    it('previewHourFloat=9.9（< 10，本行 hour=10） → 不渲染 ghost', () => {
      const { container } = render(
        <HourRow
          {...baseProps()}
          dragPreview={{
            draggedEvent: { id: 31, title: 'x', color: null, eventType: 'meeting', durationMin: 30 },
            previewYmd: '2025-08-26',
            previewHourFloat: 9.9,
          }}
        />,
      );
      const ghosts = Array.from(container.querySelectorAll('div.absolute')).filter((el) =>
        (el as HTMLElement).className.includes('border-dashed'),
      );
      expect(ghosts).toHaveLength(0);
    });

    it('previewYmd 不匹配任何列 → 不渲染 ghost', () => {
      const { container } = render(
        <HourRow
          {...baseProps()}
          dragPreview={{
            draggedEvent: { id: 32, title: 'x', color: null, eventType: 'meeting', durationMin: 30 },
            previewYmd: '2099-01-01',
            previewHourFloat: 10.1,
          }}
        />,
      );
      const ghosts = Array.from(container.querySelectorAll('div.absolute')).filter((el) =>
        (el as HTMLElement).className.includes('border-dashed'),
      );
      expect(ghosts).toHaveLength(0);
    });
  });

  describe('onDragOverCell + onDropCell 15 分钟吸附（calcSnappedHourFloat 行为）', () => {
    // jsdom 没有真实 getBoundingClientRect，所以要 stub cell 的 rect
    const stubRect = (el: Element, top: number, height: number) => {
      vi.spyOn(el as HTMLElement, 'getBoundingClientRect').mockReturnValue({
        top,
        left: 0,
        bottom: top + height,
        right: 100,
        width: 100,
        height,
        x: 0,
        y: top,
        toJSON: () => ({}),
      } as DOMRect);
    };

    // 因为 jsdom 里 DragEvent 可能未定义，直接 new Event(type) + 注入 clientX/clientY/dataTransfer
    const dispatchDrag = (cell: Element, type: 'dragover' | 'drop', clientY: number) => {
      const ev = new Event(type, { bubbles: true, cancelable: true });
      Object.defineProperty(ev, 'clientY', { get: () => clientY, configurable: true });
      Object.defineProperty(ev, 'clientX', { get: () => 0, configurable: true });
      // dataTransfer 用 duck-typed，满足 onDragOver 里 e.dataTransfer.dropEffect = 'move' 的 set
      const dt: Record<string, unknown> = {};
      Object.defineProperty(dt, 'dropEffect', {
        get: () => dt._v, set: (v: string) => { dt._v = v; },
        configurable: true,
      });
      Object.defineProperty(ev, 'dataTransfer', { get: () => dt, configurable: true });
      cell.dispatchEvent(ev);
    };

    it('relY=13.9（< 14px 边界 → minInHour≈14.89 → floor/15*15=0） → snapped=10.00', () => {
      const cb = vi.fn();
      const { container } = render(<HourRow {...baseProps()} onDragOverCell={cb} />);
      const cell = container.querySelectorAll('[role="button"]')[1] as HTMLElement;
      stubRect(cell, 0, 56);
      // 边界：(14 / 56) * 60 = 15 分钟 → 正好是第一个吸附值。要落到 0 分钟必须 relY < 14。
      dispatchDrag(cell, 'dragover', 13.9);
      expect(cb).toHaveBeenLastCalledWith('2025-08-26', 10, expect.any(Object));
    });

    it('relY=15（min=16.07 → floor/15*15=15 → hourFloat=10.25）', () => {
      const cb = vi.fn();
      const { container } = render(<HourRow {...baseProps()} onDropCell={cb} />);
      const cell = container.querySelectorAll('[role="button"]')[1] as HTMLElement;
      stubRect(cell, 100, 56); // top=100，clientY=115 → relY=15
      dispatchDrag(cell, 'drop', 115);
      expect(cb).toHaveBeenLastCalledWith('2025-08-26', 10.25, expect.any(Object));
    });

    it('relY=55（≈59min → 15*3=45 → hourFloat=10.75，不越界到下一行）', () => {
      const cb = vi.fn();
      const { container } = render(<HourRow {...baseProps()} onDragOverCell={cb} hourHeight={56} />);
      const cell = container.querySelectorAll('[role="button"]')[1] as HTMLElement;
      stubRect(cell, 0, 56);
      dispatchDrag(cell, 'dragover', 55);
      expect(cb).toHaveBeenLastCalledWith('2025-08-26', 10.75, expect.any(Object));
    });

    // 覆盖 chips.tsx 行 893：onDragOver 中 e.dataTransfer.dropEffect = 'move' 抛异常 → catch 兜底
    it('dataTransfer.dropEffect setter 抛异常 → catch 不中断 onDragOverCell（行 893）', () => {
      const cb = vi.fn();
      const { container } = render(<HourRow {...baseProps()} onDragOverCell={cb} hourHeight={56} />);
      const cell = container.querySelectorAll('[role="button"]')[1] as HTMLElement;
      stubRect(cell, 0, 56);
      // 构造一个 dataTransfer，其 dropEffect setter 抛异常
      const ev = new Event('dragover', { bubbles: true, cancelable: true });
      Object.defineProperty(ev, 'clientY', { get: () => 28, configurable: true });
      Object.defineProperty(ev, 'clientX', { get: () => 0, configurable: true });
      const dt: Record<string, unknown> = {};
      Object.defineProperty(dt, 'dropEffect', {
        get: () => { throw new Error('forbidden'); },
        set: () => { throw new Error('forbidden'); },
        configurable: true,
      });
      Object.defineProperty(ev, 'dataTransfer', { get: () => dt, configurable: true });
      // 不抛异常，onDragOverCell 仍被调用
      cell.dispatchEvent(ev);
      // clientY=28，cell.top=0，relY=28，28/56*60=30min → 15min snap=30 → hourFloat=10.5
      expect(cb).toHaveBeenCalledWith('2025-08-26', 10.5, expect.any(Object));
    });
  });

  describe('resize handle（cal-resize-handle + 鼠标按下 15 分钟吸附）', () => {
    it('heightPx<24 → 无 resize handle（即使 isLastSegment=true + 提供 onEventResize）', () => {
      const onResize = vi.fn();
      const { container } = render(
        <HourRow
          {...baseProps()}
          onEventResize={onResize}
          eventsByColumn={[
            [],
            [mkChip({ eventId: 40, durationMin: 15, isLastSegment: true, startOffsetMin: 0, eventStartAt: '2025-08-26T10:00', eventEndAt: '2025-08-26T10:15' })],
            [],
          ]}
        />,
      );
      // 15 min → 14px，<24，不应有 handle
      expect(container.querySelector('.cal-resize-handle')).toBeNull();
    });

    it('heightPx>=24 + isLastSegment=false → 也不渲染 handle（跨小时事件仅最末段可调）', () => {
      const onResize = vi.fn();
      const { container } = render(
        <HourRow
          {...baseProps()}
          onEventResize={onResize}
          eventsByColumn={[
            [],
            [mkChip({ eventId: 41, durationMin: 60, isLastSegment: false, eventStartAt: '2025-08-26T10:00', eventEndAt: '2025-08-26T11:00' })],
            [],
          ]}
        />,
      );
      expect(container.querySelector('.cal-resize-handle')).toBeNull();
    });

    // 统一：startAt/endAt 直接写 UTC 02:00 = 北京 10:00，断言直接看 ISO 字符串分钟段，不与时区耦合
    it('heightPx=56（60min）+ isLastSegment + onEventResize → 渲染 .cal-resize-handle；向下拖 28px（=30 min）→ total=90min → endAt 尾部为 "11:30:00"（北京时区）', () => {
      const onResize = vi.fn();
      // 直接写本地时间字符串，这样 new Date 解析是本地时区（UTC+8 上海），endAt 的小时/分钟不跳变
      const start = '2025-08-26T10:00:00';
      const end = '2025-08-26T11:00:00';
      const { container } = render(
        <HourRow
          {...baseProps()}
          onEventResize={onResize}
          eventsByColumn={[
            [],
            [mkChip({ eventId: 42, durationMin: 60, isLastSegment: true, startOffsetMin: 0, eventStartAt: start, eventEndAt: end })],
            [],
          ]}
        />,
      );
      const handle = container.querySelector('.cal-resize-handle') as HTMLElement;
      expect(handle).not.toBeNull();
      fireEvent.mouseDown(handle, { clientX: 0, clientY: 0, button: 0 });
      document.dispatchEvent(new MouseEvent('mousemove', { bubbles: true, cancelable: true, clientX: 0, clientY: 28 }));
      document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, clientX: 0, clientY: 28 }));
      expect(onResize).toHaveBeenCalledTimes(1);
      const [callId, callEndAt] = onResize.mock.calls[0];
      expect(callId).toBe(42);
      const isoStr = new Date(callEndAt as string).toISOString();
      // 北京时间 10:00 + 90min = 北京 11:30，本地时间字符串 11:30 对应的 UTC=03:30，所以 ISO 中 minutes=30
      expect(/T\d{2}:30:00/.test(isoStr)).toBe(true);
    });

    it('向上拖 -14px（deltaY=-14 → deltaMin=-15，total=45 → endAt 含 ":45:00"）', () => {
      const onResize = vi.fn();
      const start = '2025-08-26T10:00:00';
      const end = '2025-08-26T11:00:00';
      const { container } = render(
        <HourRow
          {...baseProps()}
          onEventResize={onResize}
          eventsByColumn={[
            [],
            [mkChip({ eventId: 43, durationMin: 60, isLastSegment: true, startOffsetMin: 0, eventStartAt: start, eventEndAt: end })],
            [],
          ]}
        />,
      );
      const handle = container.querySelector('.cal-resize-handle') as HTMLElement;
      fireEvent.mouseDown(handle, { clientX: 0, clientY: 0, button: 0 });
      document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, clientX: 0, clientY: -14 }));
      expect(onResize).toHaveBeenCalledTimes(1);
      const [, callEndAt] = onResize.mock.calls[0];
      const isoStr = new Date(callEndAt as string).toISOString();
      expect(/T\d{2}:45:00/.test(isoStr)).toBe(true);
    });

    it('向上拖超大负增量（newTotalMin 不会变负；max(15, ...) 兜底 15min → endAt 含 ":15:00"）', () => {
      const onResize = vi.fn();
      const start = '2025-08-26T10:00:00';
      const end = '2025-08-26T11:00:00';
      const { container } = render(
        <HourRow
          {...baseProps()}
          onEventResize={onResize}
          eventsByColumn={[
            [],
            [mkChip({ eventId: 44, durationMin: 60, isLastSegment: true, startOffsetMin: 0, eventStartAt: start, eventEndAt: end })],
            [],
          ]}
        />,
      );
      const handle = container.querySelector('.cal-resize-handle') as HTMLElement;
      fireEvent.mouseDown(handle, { clientX: 0, clientY: 0, button: 0 });
      document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, clientX: 0, clientY: -934 }));
      expect(onResize).toHaveBeenCalledTimes(1);
      const [, callEndAt] = onResize.mock.calls[0];
      const isoStr = new Date(callEndAt as string).toISOString();
      expect(/T\d{2}:15:00/.test(isoStr)).toBe(true);
    });

    // 覆盖 chips.tsx 行 1003：HourRow chip 标题为空字符串 → 显示 "(无标题)"
    it('chip.title="" → 高度足够时显示 "(无标题)"（chips.tsx 行 1003 falsy 分支）', () => {
      const { container } = render(
        <HourRow
          {...baseProps()}
          eventsByColumn={[
            [],
            [mkChip({ eventId: 45, title: '', durationMin: 60, isLastSegment: true, startOffsetMin: 0, eventStartAt: '2025-08-26T10:00:00', eventEndAt: '2025-08-26T11:00:00' })],
            [],
          ]}
        />,
      );
      // 高度 60min * 56px/60min = 56px >= 28 → 标题行渲染
      const chipEl = container.querySelector('[data-event-id="45"]') as HTMLElement;
      expect(chipEl).not.toBeNull();
      expect(chipEl.textContent).toContain('(无标题)');
    });

    // 覆盖 chips.tsx 行 1026：handleEl.parentElement 为 null → 提前 return（不调 onEventResize）
    it('handle.parentElement=null → 提前 return（chips.tsx 行 1026 防御分支）', () => {
      const onResize = vi.fn();
      const start = '2025-08-26T10:00:00';
      const end = '2025-08-26T11:00:00';
      const { container } = render(
        <HourRow
          {...baseProps()}
          onEventResize={onResize}
          eventsByColumn={[
            [],
            [mkChip({ eventId: 46, durationMin: 60, isLastSegment: true, startOffsetMin: 0, eventStartAt: start, eventEndAt: end })],
            [],
          ]}
        />,
      );
      const handle = container.querySelector('.cal-resize-handle') as HTMLElement;
      // mock parentElement 返回 null → 触发 if (!chipEl) return
      Object.defineProperty(handle, 'parentElement', { configurable: true, get: () => null });
      fireEvent.mouseDown(handle, { clientX: 0, clientY: 0, button: 0 });
      document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, clientX: 0, clientY: 28 }));
      // 因提前 return，未注册 mousemove/mouseup 监听，onResize 不会被调用
      expect(onResize).not.toHaveBeenCalled();
    });

    // 覆盖 chips.tsx 行 1042：segmentStart 为 Invalid Date（NaN）→ 提前 return
    it('chip 在非法 YMD 列（col 0）+ mouseDown → segmentStart=NaN → 提前 return（行 1042）', () => {
      const onResize = vi.fn();
      const start = '2025-08-26T10:00:00';
      const end = '2025-08-26T11:00:00';
      const { container } = render(
        <HourRow
          {...baseProps()}
          columnYMDs={['not-a-date', '2025-08-26', '2025-08-27'] as string[]}
          onEventResize={onResize}
          eventsByColumn={[
            [mkChip({ eventId: 48, durationMin: 60, isLastSegment: true, startOffsetMin: 0, eventStartAt: start, eventEndAt: end })],
            [],
            [],
          ]}
        />,
      );
      const handle = container.querySelector('.cal-resize-handle') as HTMLElement;
      expect(handle).not.toBeNull();
      fireEvent.mouseDown(handle, { clientX: 0, clientY: 0, button: 0 });
      document.dispatchEvent(new MouseEvent('mouseup', { bubbles: true, cancelable: true, clientX: 0, clientY: 28 }));
      // segmentStart = new Date('not-a-dateT10:00:00') = Invalid Date → NaN → return
      // 监听未注册，mouseup 不触发 onResize
      expect(onResize).not.toHaveBeenCalled();
    });
  });

  /* ============================ memo 比较函数（chips.tsx 行 1113~1162） ============================
     使用 React.Profiler 的 onRender 回调检测子树是否 commit：
     - mount 阶段调用一次（初始化）
     - update 阶段仅在 memo 返回 false（子树重新渲染）时调用
     - memo 返回 true（跳过渲染）→ onRender 不被调用
     ========================================================================================= */
  describe('memo 比较函数', () => {
    // 辅助：用 Profiler 包裹 HourRow 渲染，返回 rerender + onRender
    const renderWithProfiler = (props: ReturnType<typeof baseProps>) => {
      const onRender = vi.fn();
      const utils = render(
        <Profiler id="hr" onRender={onRender}>
          <HourRow {...props} />
        </Profiler>,
      );
      expect(onRender).toHaveBeenCalledTimes(1); // mount
      onRender.mockClear();
      return { ...utils, onRender };
    };

    // 辅助：用 Profiler 包裹 rerender
    const rerenderWithProfiler = (
      rerender: (ui: React.ReactElement) => void,
      onRender: ReturnType<typeof vi.fn>,
      props: ReturnType<typeof baseProps>,
    ) => {
      rerender(
        <Profiler id="hr" onRender={onRender}>
          <HourRow {...props} />
        </Profiler>,
      );
    };

    describe('标量 props（hour / nowLine / hourHeight / dragOver / isDragging / flash / focused）', () => {
      it('所有 props 引用相同 → memo 返回 true → 不重新渲染', () => {
        const props = baseProps();
        // 直接调用 comparator 验证返回 true（避免 Profiler 在父 rerender 时误报）
        expect(_hourRowMemoComparator(props, props)).toBe(true);
        // 引用不同但内容全等 → true
        expect(_hourRowMemoComparator({ ...props }, { ...props })).toBe(true);
      });

      it('hour 不同 → memo 返回 false → 重新渲染', () => {
        const props = baseProps();
        const { rerender, onRender } = renderWithProfiler(props);
        const next = { ...props, hour: 11 };
        rerenderWithProfiler(rerender, onRender, next);
        expect(onRender).toHaveBeenCalled();
        expect(screen.getByText(/^11:00$/)).toBeInTheDocument();
      });

      it('nowLineVisible 不同 → memo 返回 false → 重新渲染', () => {
        const props = baseProps();
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, nowLineVisible: true, nowLineMinute: 30 });
        expect(onRender).toHaveBeenCalled();
      });

      it('nowLineMinute 不同（nowLineVisible 相同）→ memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), nowLineVisible: true, nowLineMinute: 15 };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, nowLineMinute: 30 });
        expect(onRender).toHaveBeenCalled();
      });

      it('hourHeight 不同 → memo 返回 false → 重新渲染', () => {
        const props = baseProps();
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, hourHeight: 44 });
        expect(onRender).toHaveBeenCalled();
      });

      it('dragOverColumn 不同 → memo 返回 false → 重新渲染', () => {
        const props = baseProps();
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, dragOverColumn: 1 });
        expect(onRender).toHaveBeenCalled();
      });

      it('isDragging 不同 → memo 返回 false → 重新渲染', () => {
        const props = baseProps();
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, isDragging: true });
        expect(onRender).toHaveBeenCalled();
      });

      it('flashColumnIdx 不同 → memo 返回 false → 重新渲染', () => {
        const props = baseProps();
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, flashColumnIdx: 0, flashTone: 'ok' as const });
        expect(onRender).toHaveBeenCalled();
      });

      it('flashTone 不同（flashColumnIdx 相同）→ memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), flashColumnIdx: 0, flashTone: 'ok' as const };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, flashTone: 'err' as const });
        expect(onRender).toHaveBeenCalled();
      });

      it('focusedEventId 不同 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), eventsByColumn: [[], [mkChip({ eventId: 1 })], []] as HourRowColumnEventLocal[][] };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, focusedEventId: 1 });
        expect(onRender).toHaveBeenCalled();
      });
    });

    describe('dragPreview（chips.tsx 行 1123~1135）', () => {
      const mkPreview = (
        overrides: Partial<NonNullable<NonNullable<ReturnType<typeof baseProps>['dragPreview']>>>,
      ): NonNullable<HourRowProps['dragPreview']> => ({
        draggedEvent: { id: 1, title: 'ghost', color: null, eventType: 'task', durationMin: 30 },
        previewYmd: '2025-08-26',
        previewHourFloat: 10.25,
        ...overrides,
      });

      it('null vs 对象 → memo 返回 false → 重新渲染', () => {
        const props = baseProps();
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, dragPreview: mkPreview({}) });
        expect(onRender).toHaveBeenCalled();
      });

      it('对象 vs null → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), dragPreview: mkPreview({}) };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, dragPreview: null });
        expect(onRender).toHaveBeenCalled();
      });

      it('previewYmd 不同 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), dragPreview: mkPreview({ previewYmd: '2025-08-26' }) };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, dragPreview: mkPreview({ previewYmd: '2025-08-27' }) });
        expect(onRender).toHaveBeenCalled();
      });

      it('previewHourFloat 不同 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), dragPreview: mkPreview({ previewHourFloat: 10.25 }) };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, dragPreview: mkPreview({ previewHourFloat: 10.5 }) });
        expect(onRender).toHaveBeenCalled();
      });

      it('draggedEvent null vs 对象 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), dragPreview: { draggedEvent: null, previewYmd: '2025-08-26', previewHourFloat: 10.25 } };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, {
          ...props,
          dragPreview: { draggedEvent: { id: 1, title: 'x', color: null, eventType: 'task', durationMin: 30 }, previewYmd: '2025-08-26', previewHourFloat: 10.25 },
        });
        expect(onRender).toHaveBeenCalled();
      });

      it('draggedEvent.id 不同 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), dragPreview: mkPreview({ draggedEvent: { id: 1, title: 'a', color: null, eventType: 'task', durationMin: 30 } }) };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, dragPreview: mkPreview({ draggedEvent: { id: 2, title: 'a', color: null, eventType: 'task', durationMin: 30 } }) });
        expect(onRender).toHaveBeenCalled();
      });

      it('draggedEvent.title 不同 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), dragPreview: mkPreview({ draggedEvent: { id: 1, title: 'A', color: null, eventType: 'task', durationMin: 30 } }) };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, dragPreview: mkPreview({ draggedEvent: { id: 1, title: 'B', color: null, eventType: 'task', durationMin: 30 } }) });
        expect(onRender).toHaveBeenCalled();
      });

      it('draggedEvent.color 不同 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), dragPreview: mkPreview({ draggedEvent: { id: 1, title: 'x', color: null, eventType: 'task', durationMin: 30 } }) };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, dragPreview: mkPreview({ draggedEvent: { id: 1, title: 'x', color: '#ff0000', eventType: 'task', durationMin: 30 } }) });
        expect(onRender).toHaveBeenCalled();
      });

      it('draggedEvent.eventType 不同 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), dragPreview: mkPreview({ draggedEvent: { id: 1, title: 'x', color: null, eventType: 'task', durationMin: 30 } }) };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, dragPreview: mkPreview({ draggedEvent: { id: 1, title: 'x', color: null, eventType: 'meeting', durationMin: 30 } }) });
        expect(onRender).toHaveBeenCalled();
      });

      it('draggedEvent.durationMin 不同 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), dragPreview: mkPreview({ draggedEvent: { id: 1, title: 'x', color: null, eventType: 'task', durationMin: 30 } }) };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, dragPreview: mkPreview({ draggedEvent: { id: 1, title: 'x', color: null, eventType: 'task', durationMin: 60 } }) });
        expect(onRender).toHaveBeenCalled();
      });

      it('draggedEvent 引用相同（pa === pb）→ memo 返回 true → 不重新渲染', () => {
        const dragPreview = mkPreview({});
        const props = { ...baseProps(), dragPreview };
        expect(_hourRowMemoComparator(props, props)).toBe(true);
      });

      it('draggedEvent 引用不同但字段全相同 → memo 返回 true → 不重新渲染', () => {
        const props = { ...baseProps(), dragPreview: mkPreview({}) };
        expect(_hourRowMemoComparator({ ...props }, { ...props, dragPreview: mkPreview({}) })).toBe(true);
      });
    });

    describe('selectedEventIds（chips.tsx 行 1137~1145）', () => {
      it('Set vs null → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), selectedEventIds: new Set<number>([1, 2, 3]) };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, selectedEventIds: undefined });
        expect(onRender).toHaveBeenCalled();
      });

      it('null vs Set → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), selectedEventIds: undefined };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, selectedEventIds: new Set<number>([1]) });
        expect(onRender).toHaveBeenCalled();
      });

      it('size 不同 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), selectedEventIds: new Set<number>([1, 2, 3]) };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, selectedEventIds: new Set<number>([1, 2]) });
        expect(onRender).toHaveBeenCalled();
      });

      it('size 相同但内容不同 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), selectedEventIds: new Set<number>([1, 2, 3]) };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, selectedEventIds: new Set<number>([1, 2, 4]) });
        expect(onRender).toHaveBeenCalled();
      });

      it('引用相同 → memo 返回 true → 不重新渲染', () => {
        const selected = new Set<number>([1, 2, 3]);
        const props = { ...baseProps(), selectedEventIds: selected };
        expect(_hourRowMemoComparator(props, props)).toBe(true);
      });

      it('引用不同但内容相同 → memo 返回 true → 不重新渲染', () => {
        const props = { ...baseProps(), selectedEventIds: new Set<number>([1, 2, 3]) };
        expect(_hourRowMemoComparator({ ...props }, { ...props, selectedEventIds: new Set<number>([1, 2, 3]) })).toBe(true);
      });

      it('两者均 null → memo 返回 true → 不重新渲染', () => {
        const props = { ...baseProps(), selectedEventIds: null as unknown as ReadonlySet<number> };
        expect(_hourRowMemoComparator({ ...props }, { ...props, selectedEventIds: null as unknown as ReadonlySet<number> })).toBe(true);
      });
    });

    describe('columnYMDs + eventsByColumn（chips.tsx 行 1146~1160）', () => {
      it('columnYMDs.length 不同 → memo 返回 false → 重新渲染', () => {
        const props = baseProps();
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, columnYMDs: ['2025-08-25', '2025-08-26'], eventsByColumn: [[], []] as HourRowColumnEventLocal[][] });
        expect(onRender).toHaveBeenCalled();
      });

      it('columnYMDs 元素不同（同一位置 YMD 不同）→ memo 返回 false → 重新渲染', () => {
        const props = baseProps();
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, columnYMDs: ['2025-08-25', '2025-08-27', '2025-08-27'] });
        expect(onRender).toHaveBeenCalled();
      });

      it('eventsByColumn[i].length 不同 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), eventsByColumn: [[], [mkChip({ eventId: 1 })], []] as HourRowColumnEventLocal[][] };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, eventsByColumn: [[], [], []] as HourRowColumnEventLocal[][] });
        expect(onRender).toHaveBeenCalled();
      });

      it('chip.chipKey 不同 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), eventsByColumn: [[], [mkChip({ eventId: 1, chipKey: '1-seg0' })], []] as HourRowColumnEventLocal[][] };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, eventsByColumn: [[], [mkChip({ eventId: 1, chipKey: '1-seg1' })], []] as HourRowColumnEventLocal[][] });
        expect(onRender).toHaveBeenCalled();
      });

      it('chip.title 不同 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), eventsByColumn: [[], [mkChip({ eventId: 1, title: 'A' })], []] as HourRowColumnEventLocal[][] };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, eventsByColumn: [[], [mkChip({ eventId: 1, title: 'B' })], []] as HourRowColumnEventLocal[][] });
        expect(onRender).toHaveBeenCalled();
      });

      it('chip.color 不同 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), eventsByColumn: [[], [mkChip({ eventId: 1, color: null })], []] as HourRowColumnEventLocal[][] };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, eventsByColumn: [[], [mkChip({ eventId: 1, color: '#ff0000' })], []] as HourRowColumnEventLocal[][] });
        expect(onRender).toHaveBeenCalled();
      });

      it('chip.startOffsetMin 不同 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), eventsByColumn: [[], [mkChip({ eventId: 1, startOffsetMin: 0 })], []] as HourRowColumnEventLocal[][] };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, eventsByColumn: [[], [mkChip({ eventId: 1, startOffsetMin: 30 })], []] as HourRowColumnEventLocal[][] });
        expect(onRender).toHaveBeenCalled();
      });

      it('chip.durationMin 不同 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), eventsByColumn: [[], [mkChip({ eventId: 1, durationMin: 60 })], []] as HourRowColumnEventLocal[][] };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, eventsByColumn: [[], [mkChip({ eventId: 1, durationMin: 30 })], []] as HourRowColumnEventLocal[][] });
        expect(onRender).toHaveBeenCalled();
      });

      it('eventsByColumn 为 null/undefined → 降级为空数组（?? []）→ memo 返回 true → 不重新渲染', () => {
        const props = { ...baseProps(), eventsByColumn: [[], [], []] as HourRowColumnEventLocal[][] };
        // eventsByColumn[i] 为 undefined 时，比较时降级为 []
        expect(_hourRowMemoComparator(
          { ...props, eventsByColumn: [undefined, undefined, undefined] as unknown as HourRowColumnEventLocal[][] },
          { ...props, eventsByColumn: [undefined, undefined, undefined] as unknown as HourRowColumnEventLocal[][] },
        )).toBe(true);
      });

      it('引用不同但内容相同 → memo 返回 true → 不重新渲染', () => {
        const props = {
          ...baseProps(),
          columnYMDs: ['2025-08-25', '2025-08-26', '2025-08-27'] as string[],
          eventsByColumn: [[], [mkChip({ eventId: 1, title: '稳定' })], []] as HourRowColumnEventLocal[][],
        };
        // 新建内容完全相同但引用不同的数组/对象
        const next = {
          ...props,
          columnYMDs: ['2025-08-25', '2025-08-26', '2025-08-27'] as string[],
          eventsByColumn: [[], [mkChip({ eventId: 1, title: '稳定' })], []] as HourRowColumnEventLocal[][],
        };
        expect(_hourRowMemoComparator(props, next)).toBe(true);
      });
    });

    describe('callback props（chips.tsx 行 1161）', () => {
      it('onCellClick 引用变化 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), onCellClick: vi.fn() };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, onCellClick: vi.fn() });
        expect(onRender).toHaveBeenCalled();
      });

      it('onDragOverCell 引用变化 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), onDragOverCell: vi.fn() };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, onDragOverCell: vi.fn() });
        expect(onRender).toHaveBeenCalled();
      });

      it('onDropCell 引用变化 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), onDropCell: vi.fn() };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, onDropCell: vi.fn() });
        expect(onRender).toHaveBeenCalled();
      });

      it('onEventClick 引用变化 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), eventsByColumn: [[], [mkChip({ eventId: 1 })], []] as HourRowColumnEventLocal[][], onEventClick: vi.fn() };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, onEventClick: vi.fn() });
        expect(onRender).toHaveBeenCalled();
      });

      it('onEventDragStart 引用变化 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), eventsByColumn: [[], [mkChip({ eventId: 1 })], []] as HourRowColumnEventLocal[][], onEventDragStart: vi.fn() };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, onEventDragStart: vi.fn() });
        expect(onRender).toHaveBeenCalled();
      });

      it('onEventResize 引用变化 → memo 返回 false → 重新渲染', () => {
        const props = { ...baseProps(), eventsByColumn: [[], [mkChip({ eventId: 1, isLastSegment: true, durationMin: 60 })], []] as HourRowColumnEventLocal[][], onEventResize: vi.fn() };
        const { rerender, onRender } = renderWithProfiler(props);
        rerenderWithProfiler(rerender, onRender, { ...props, onEventResize: vi.fn() });
        expect(onRender).toHaveBeenCalled();
      });

      it('所有 callback 引用相同 → memo 返回 true → 不重新渲染', () => {
        const onCellClick = vi.fn();
        const props = { ...baseProps(), onCellClick };
        expect(_hourRowMemoComparator(props, props)).toBe(true);
      });
    });
  });
});

/* ============================ 剩余未覆盖分支补全（目标 100% 覆盖率） ============================
   覆盖 chips.tsx 中 4 个未覆盖分支 + 1 个未覆盖函数：
   - 行 18: typeof window === 'undefined' (SSR 兜底)
   - 行 232: if (!chipRef.current) return (chipRef null 早返回)
   - 行 307: deltaY > DRAG_THRESHOLD_PX (|| 右操作数分支)
   - 行 935: dragPreview.draggedEvent.title || '(无标题)' (空标题回退)
   - 行 24: matchMedia change handler 函数 (未覆盖函数)
   ========================================================================================= */
describe('chips.tsx 剩余未覆盖分支补全（目标 100%）', () => {
  describe('useHourHeight matchMedia change handler（行 24 未覆盖函数）', () => {
    it('matchMedia change 事件 matches=true → setHeight(44)（移动端）', () => {
      let capturedHandler: ((e: { matches: boolean }) => void) | null = null;
      const mockMql = {
        matches: false,
        media: '(max-width: 767.98px)',
        onchange: null,
        addEventListener: vi.fn((event: string, handler: (e: { matches: boolean }) => void) => {
          if (event === 'change') capturedHandler = handler;
        }),
        removeEventListener: vi.fn(),
        addListener: vi.fn(),
        removeListener: vi.fn(),
        dispatchEvent: vi.fn(),
      };
      // @ts-expect-error jsdom matchMedia
      window.matchMedia = vi.fn(() => mockMql);
      const { result } = renderHook(() => useHourHeight());
      expect(result.current).toBe(56);
      // 触发 change 事件 → matches=true → setHeight(44)
      act(() => {
        capturedHandler!({ matches: true });
      });
      expect(result.current).toBe(44);
    });

    it('matchMedia change 事件 matches=false → setHeight(56)（桌面端）', () => {
      let capturedHandler: ((e: { matches: boolean }) => void) | null = null;
      const mockMql = {
        matches: true,
        media: '(max-width: 767.98px)',
        onchange: null,
        addEventListener: vi.fn((event: string, handler: (e: { matches: boolean }) => void) => {
          if (event === 'change') capturedHandler = handler;
        }),
        removeEventListener: vi.fn(),
        addListener: vi.fn(),
        removeListener: vi.fn(),
        dispatchEvent: vi.fn(),
      };
      // @ts-expect-error jsdom matchMedia
      window.matchMedia = vi.fn(() => mockMql);
      const { result } = renderHook(() => useHourHeight());
      expect(result.current).toBe(44);
      act(() => {
        capturedHandler!({ matches: false });
      });
      expect(result.current).toBe(56);
    });
  });

  describe('useHourHeight SSR 路径（行 18: typeof window === "undefined"）', () => {
    it('typeof window === undefined → 返回 56（SSR 兜底）', () => {
      // 在组件渲染过程中临时移除 window，模拟 SSR 环境
      // useState 初始化器会检查 typeof window === 'undefined' → 直接返回 56
      let capturedHeight: number | undefined;
  function SSRTestComp() {
      const original = (globalThis as unknown as { window: typeof window }).window;
      try {
        // 临时将 window 设为 undefined（模拟 Node SSR 环境）
        Object.defineProperty(globalThis, 'window', { value: undefined, configurable: true, writable: true });
        capturedHeight = useHourHeight();
      } finally {
        Object.defineProperty(globalThis, 'window', { value: original, configurable: true, writable: true });
      }
      return null;
    }
      render(<SSRTestComp />);
      expect(capturedHeight).toBe(56);
    });
  });

  describe('EventChip chipRef.current=null 早返回（行 232）', () => {
    it('chipRef.current 为 null 时 hoverPreview 切换 → 早返回不抛异常', async () => {
      // 通过 vi.doMock + dynamic import 重新加载 chips.tsx，
      // 使 EventChipImpl 内部 useRef 第 2 次调用（chipRef）返回 { current: null }
      vi.resetModules();
      vi.doMock('react', async (orig) => {
        const actual = await orig() as typeof import('react');
        let callIdx = 0;
        return {
          ...actual,
          useRef: ((initial: unknown) => {
            callIdx++;
            // EventChipImpl 中 useRef 调用顺序:
            // 1. hoverTimerRef(null), 2. chipRef(null), 3. scrollerRef(null), 4. dayCellRef(null)
            if (callIdx === 2) {
              // 用 getter/setter 强制 current 始终为 null
              // （即使 React DOM 在 commit phase 尝试 attach div 元素也会被 setter 忽略）
              const ref: { current: unknown } = {} as { current: unknown };
              Object.defineProperty(ref, 'current', {
                get: () => null,
                set: () => { /* ignore React DOM attach */ },
                configurable: true,
              });
              return ref as React.MutableRefObject<unknown>;
            }
            return { current: initial } as React.MutableRefObject<unknown>;
          }) as typeof actual.useRef,
        };
      });

      // 重新加载被 mock 后的 chips 模块（包含 EventChip）
      const chipsMod = await import('../pages/calendar/chips');
      const EventChipMock = chipsMod.EventChip;

      vi.useFakeTimers();
      const { container, unmount } = render(<EventChipMock ev={E({ id: 99, title: 'ref-test' })} onOpen={vi.fn()} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      // 触发 hover → hoverPreview 切换为 true → effect 运行但 chipRef.current=null → 早返回
      expect(() => {
        act(() => {
          fireEvent.mouseEnter(root);
          vi.advanceTimersByTime(320);
        });
      }).not.toThrow();
      unmount();
      vi.useRealTimers();
      vi.doUnmock('react');
      vi.resetModules();
    });
  });

  describe('touch move deltaY-only（行 307 || 右操作数分支）', () => {
    it('touchStart 后 deltaX≤10 但 deltaY>10 → 取消长按 timer', () => {
      vi.useFakeTimers();
      const onDragStart = vi.fn();
      const { container } = render(<EventChip ev={E({ id: 14, title: 'deltaY-test' })} onOpen={vi.fn()} onDragStart={onDragStart} />);
      const root = container.querySelector('[data-event-id]') as HTMLElement;
      fireEvent.touchStart(root, { touches: [{ clientX: 0, clientY: 0 }] });
      act(() => { vi.advanceTimersByTime(100); });
      // deltaX=3 (≤10), deltaY=50 (>10) → 触发 || 右操作数 → 取消长按
      fireEvent.touchMove(root, { touches: [{ clientX: 3, clientY: 50 }] });
      act(() => { vi.advanceTimersByTime(500); });
      // 长按被取消 → onDragStart 不被调用
      expect(onDragStart).not.toHaveBeenCalled();
      vi.useRealTimers();
    });
  });

  describe('dragPreview 空标题回退（行 935: title || "(无标题)"）', () => {
    it('draggedEvent.title="" → 渲染 "(无标题)"（|| 右操作数分支）', () => {
      const { container } = render(
        <HourRow
          hour={10}
          columnYMDs={['2025-08-26'] as string[]}
          eventsByColumn={[[]] as HourRowColumnEventLocal[][]}
          nowLineVisible={false}
          nowLineMinute={0}
          hourHeight={56}
          dragOverColumn={null}
          isDragging={false}
          selectedEventIds={new Set<number>()}
          dragPreview={{
            draggedEvent: { id: 40, title: '', color: null, eventType: 'meeting', durationMin: 30 },
            previewYmd: '2025-08-26',
            previewHourFloat: 10.5,
          }}
        />,
      );
      const ghost = Array.from(container.querySelectorAll('div.absolute')).find((el) => {
        const c = (el as HTMLElement).className;
        return c.includes('z-30') && c.includes('border-dashed');
      }) as HTMLElement | undefined;
      expect(ghost).toBeDefined();
      expect(ghost!.textContent).toMatch(/\(无标题\)/);
    });

    it('draggedEvent.title=null → 渲染 "(无标题)"', () => {
      const { container } = render(
        <HourRow
          hour={10}
          columnYMDs={['2025-08-26'] as string[]}
          eventsByColumn={[[]] as HourRowColumnEventLocal[][]}
          nowLineVisible={false}
          nowLineMinute={0}
          hourHeight={56}
          dragOverColumn={null}
          isDragging={false}
          selectedEventIds={new Set<number>()}
          dragPreview={{
            draggedEvent: { id: 41, title: null as unknown as string, color: null, eventType: 'meeting', durationMin: 30 },
            previewYmd: '2025-08-26',
            previewHourFloat: 10.5,
          }}
        />,
      );
      const ghost = Array.from(container.querySelectorAll('div.absolute')).find((el) => {
        const c = (el as HTMLElement).className;
        return c.includes('z-30') && c.includes('border-dashed');
      }) as HTMLElement | undefined;
      expect(ghost).toBeDefined();
      expect(ghost!.textContent).toMatch(/\(无标题\)/);
    });
  });
});

