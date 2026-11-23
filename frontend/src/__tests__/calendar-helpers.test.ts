import { describe, it, expect } from 'vitest';
import type { CalendarEventDto } from '../api/types';
import {
  toYMDKey, iterateEventDates, buildEventsByDate, rebuildEventsByDateIncremental,
  calcVisibleHourRange, isHourInViewport, SLOT_HEIGHT_PX_DEFAULT, ROW_HEIGHT_PX, ROWS_PER_HOUR, VIEW_BUFFER_HOURS,
} from '../pages/calendar/calendarUtils';

const DESKTOP_HOUR_HEIGHT = 56;

describe('calendar-helpers (P0 Task 3)', () => {
  describe('toYMDKey', () => {
    it('Date(2026,7,12,12,34) → "2026-08-12"（本地时区）', () => {
      expect(toYMDKey(new Date(2026, 7, 12, 12, 34, 56))).toBe('2026-08-12');
    });
    it('午夜 00:00 当天不偏移', () => {
      expect(toYMDKey(new Date(2026, 0, 1, 0, 0, 0))).toBe('2026-01-01');
    });
  });

  describe('iterateEventDates', () => {
    it('单天事件 → 1 条 YMD', () => {
      const ev = { startAt: '2026-08-12T01:00:00Z', endAt: '2026-08-12T02:00:00Z' } as CalendarEventDto;
      expect(iterateEventDates(ev)).toEqual(['2026-08-12']);
    });
    it('跨 3 天全天事件 → 3 条 YMD', () => {
      const ev = {
        startAt: new Date(2026, 7, 8).toISOString(),
        endAt: new Date(2026, 7, 10, 23, 59, 59).toISOString(),
      } as CalendarEventDto;
      expect(iterateEventDates(ev)).toEqual(['2026-08-08', '2026-08-09', '2026-08-10']);
    });
    it('安全钳：跨度 > 366 天最多展开 367 条（防死循环）', () => {
      const ev = { startAt: new Date(2026, 0, 1).toISOString(), endAt: new Date(2032, 0, 1).toISOString() } as CalendarEventDto;
      expect(iterateEventDates(ev).length).toBeLessThanOrEqual(367);
    });
    it('endAt null → 只返回 startAt 当天', () => {
      const ev = { startAt: '2026-08-12T10:00:00Z' } as CalendarEventDto;
      expect(iterateEventDates(ev)).toEqual(['2026-08-12']);
    });
  });

  describe('buildEventsByDate', () => {
    const E1 = { id: 1, title: 'e1', startAt: '2026-08-12T09:00:00Z', endAt: '2026-08-12T10:00:00Z' } as CalendarEventDto;
    const E2 = { id: 2, title: 'e2', startAt: '2026-08-12T15:00:00Z', endAt: '2026-08-13T01:00:00Z' } as CalendarEventDto;
    const E3 = { id: 3, title: 'e3', startAt: '2026-08-12T07:00:00Z', endAt: '2026-08-12T08:00:00Z' } as CalendarEventDto;
    it('空数组 → 空 Map', () => { expect(buildEventsByDate([]).size).toBe(0); });
    it('桶内按 startAt 升序：E3(07:00) → E1(09:00)', () => {
      expect(buildEventsByDate([E1, E3]).get('2026-08-12')?.map(e => e.id)).toEqual([3, 1]);
    });
    it('跨日事件 E2 → 两天各一条引用', () => {
      const m = buildEventsByDate([E2]);
      expect(m.get('2026-08-12')?.[0].id).toBe(2);
      expect(m.get('2026-08-13')?.[0].id).toBe(2);
    });
  });
});

describe('P2P3 §3.4.1: rebuildEventsByDateIncremental (增量重建)', () => {
  const D12 = { id: 1, title: 'd12', startAt: '2026-08-12T09:00:00Z', endAt: '2026-08-12T10:00:00Z' } as CalendarEventDto;
  const D13 = { id: 2, title: 'd13', startAt: '2026-08-13T09:00:00Z', endAt: '2026-08-13T10:00:00Z' } as CalendarEventDto;
  const D14 = { id: 3, title: 'd14', startAt: '2026-08-14T09:00:00Z', endAt: '2026-08-14T10:00:00Z' } as CalendarEventDto;
  const D12_UPDATED = { ...D12, title: 'd12-updated' };

  it('未变化 YMD 的 bucket 保持 SAME REFERENCE (===)', () => {
    const prevEvents = [D12, D13, D14];
    const prevMap = buildEventsByDate(prevEvents);
    const refD12Before = prevMap.get('2026-08-12');
    const refD13Before = prevMap.get('2026-08-13');
    const refD14Before = prevMap.get('2026-08-14');

    const nextEvents = [D12_UPDATED, D13, D14];
    const nextMap = rebuildEventsByDateIncremental(nextEvents, prevMap);

    expect(refD12Before).not.toBe(nextMap.get('2026-08-12'));
    expect(refD13Before).toBe(nextMap.get('2026-08-13'));
    expect(refD14Before).toBe(nextMap.get('2026-08-14'));
  });

  it('删除事件后原 bucket 条目被清理，其他保持引用', () => {
    const prevEvents = [D12, D13, D14];
    const prevMap = buildEventsByDate(prevEvents);
    const refD13Before = prevMap.get('2026-08-13');
    const refD14Before = prevMap.get('2026-08-14');

    const nextEvents = [D13, D14];
    const nextMap = rebuildEventsByDateIncremental(nextEvents, prevMap);

    expect(nextMap.get('2026-08-12')).toEqual([]);
    expect(nextMap.has('2026-08-12')).toBe(true);
    expect(refD13Before).toBe(nextMap.get('2026-08-13'));
    expect(refD14Before).toBe(nextMap.get('2026-08-14'));
  });

  it('新增事件只影响对应 YMD bucket', () => {
    const prevEvents = [D12, D13];
    const prevMap = buildEventsByDate(prevEvents);
    const refD12Before = prevMap.get('2026-08-12');
    const refD13Before = prevMap.get('2026-08-13');

    const nextEvents = [D12, D13, D14];
    const nextMap = rebuildEventsByDateIncremental(nextEvents, prevMap);

    expect(refD12Before).toBe(nextMap.get('2026-08-12'));
    expect(refD13Before).toBe(nextMap.get('2026-08-13'));
    expect(nextMap.get('2026-08-14')?.map(e => e.id)).toEqual([3]);
  });

  it('跨日事件变动：影响两端 YMD', () => {
    const evCrossStart = new Date(2026, 7, 12, 23, 0, 0);
    const evCrossEnd = new Date(2026, 7, 13, 1, 0, 0);
    const evCross = { id: 10, title: 'cross', startAt: evCrossStart.toISOString(), endAt: evCrossEnd.toISOString() } as CalendarEventDto;
    const D14Start = new Date(2026, 7, 14, 9, 0, 0);
    const D14End = new Date(2026, 7, 14, 10, 0, 0);
    const D14Local = { id: 3, title: 'd14', startAt: D14Start.toISOString(), endAt: D14End.toISOString() } as CalendarEventDto;
    const prevEvents = [D14Local];
    const prevMap = buildEventsByDate(prevEvents);
    const refD14Before = prevMap.get('2026-08-14');

    const nextEvents = [evCross, D14Local];
    const nextMap = rebuildEventsByDateIncremental(nextEvents, prevMap);

    expect(nextMap.get('2026-08-12')?.map(e => e.id)).toEqual([10]);
    expect(nextMap.get('2026-08-13')?.map(e => e.id)).toEqual([10]);
    expect(refD14Before).toBe(nextMap.get('2026-08-14'));
  });

  it('输出与 buildEventsByDate 等价（内容一致）', () => {
    const events1 = [D12, D13, D14];
    const prevMap = new Map<string, CalendarEventDto[]>();
    const incResult = rebuildEventsByDateIncremental(events1, prevMap);
    const fullResult = buildEventsByDate(events1);
    for (const k of fullResult.keys()) {
      expect(incResult.get(k)?.map(e => e.id)).toEqual(fullResult.get(k)?.map(e => e.id));
    }
    const evCross = { id: 10, title: 'cross', startAt: '2026-08-12T23:00:00Z', endAt: '2026-08-15T01:00:00Z' } as CalendarEventDto;
    const events2 = [D12_UPDATED, D14, evCross];
    const inc2 = rebuildEventsByDateIncremental(events2, fullResult);
    const full2 = buildEventsByDate(events2);
    const allKeys = new Set([...inc2.keys(), ...full2.keys()]);
    for (const k of allKeys) {
      expect(inc2.get(k)?.map(e => e.id)).toEqual(full2.get(k)?.map(e => e.id));
    }
  });
});

describe('P2P3 §3.4.1: shallowEqual (浅相等比较)', () => {
  it('从 hooks.ts 导出 shallowEqual 行为正确', async () => {
    const mod = await import('../pages/calendar/hooks');
    const { shallowEqual } = mod;
    expect(shallowEqual(1, 1)).toBe(true);
    expect(shallowEqual({ a: 1 }, { a: 1 })).toBe(true);
    expect(shallowEqual({ a: 1 }, { a: 2 })).toBe(false);
    expect(shallowEqual({ a: 1, b: 2 }, { a: 1 })).toBe(false);
    const ref = { x: 1 };
    expect(shallowEqual(ref, ref)).toBe(true);
    expect(shallowEqual(null, null)).toBe(true);
    expect(shallowEqual(null, undefined)).toBe(false);
  });
});

describe('P2P3 §3.4.2: calcVisibleHourRange / isHourInViewport（周视图部分虚拟化纯函数）', () => {
  it('常量自检：SLOT_HEIGHT_PX_DEFAULT=36 (SSR/测试兜底), ROWS_PER_HOUR=4, VIEW_BUFFER_HOURS=1', () => {
    expect(SLOT_HEIGHT_PX_DEFAULT).toBe(36);
    expect(ROW_HEIGHT_PX).toBe(36);
    expect(ROWS_PER_HOUR).toBe(4);
    expect(VIEW_BUFFER_HOURS).toBe(1);
  });

  it('scrollTop=0, H=720, hourHeight=56 (桌面) → slot=14px, viewport ≈ 51 slots ≈ 12.8h, ±1h缓冲 → start=0, end≈12.8+1≈13.5+对齐=14', () => {
    const r = calcVisibleHourRange(0, 720, DESKTOP_HOUR_HEIGHT);
    // slotHeight = 56/4 = 14px
    // H=720 → viewport covers ceil(720/14) = 52 slots ≈ 13 hours
    // bottomRow = ceil(720/14) + 1*4 = 52 + 4 = 56 rows → 56/4 = 14 hours
    expect(r.visibleStartHour).toBe(0);
    expect(r.visibleEndHour).toBe(14);
  });

  it('scrollTop=360, H=720, hourHeight=56 (桌面) → topRow=25-4=21 rows=5.25h, bottom=78+4=82 rows=20.5h', () => {
    // scrollTop=360 → first row = floor(360/14) = 25 → minus buffer 4 rows → 21 rows → start=5.25h
    // bottom: scrollTop+H=1080 → ceil(1080/14)=78 rows → +4 buffer = 82 → 82/4=20.5 hours
    const r = calcVisibleHourRange(360, 720, DESKTOP_HOUR_HEIGHT);
    expect(r.visibleStartHour).toBeCloseTo(5.25, 5);
    expect(r.visibleEndHour).toBeCloseTo(20.5, 5);
  });

  it('下边界钳制：scrollTop极大值时 endHour 不超过 24', () => {
    const r = calcVisibleHourRange(10000, 1000, DESKTOP_HOUR_HEIGHT);
    expect(r.visibleEndHour).toBeLessThanOrEqual(24);
    expect(r.visibleStartHour).toBeGreaterThanOrEqual(0);
  });

  it('上边界钳制：scrollTop=0 + 很小 viewport → startHour ≥ 0', () => {
    const r = calcVisibleHourRange(0, 10, DESKTOP_HOUR_HEIGHT);
    expect(r.visibleStartHour).toBeGreaterThanOrEqual(0);
  });

  it('移动高度 hourHeight=44: slot=11px, scrollTop=0/H=440 → viewport 40 slots=10h + 1h buffer → end=11h', () => {
    // slotHeight = 44/4 = 11px
    // H=440 → ceil(440/11) = 40 slots = 10 hours
    // bottomRow = 40 + 4 buffer = 44 → 44/4 = 11 hours
    const r = calcVisibleHourRange(0, 440, 44);
    expect(r.visibleStartHour).toBe(0);
    expect(r.visibleEndHour).toBe(11);
  });

  it('totalRows 参数可自定义：缩小 TOTAL_ROWS 时 endHour 正确被钳制', () => {
    const halfRows = 48; // only 12 hours total
    const r = calcVisibleHourRange(0, 1000, DESKTOP_HOUR_HEIGHT, halfRows);
    expect(r.visibleEndHour).toBeLessThanOrEqual(12);
  });

  it('isHourInViewport：事件 hour 2, 1h 时长，viewport [8, 10] → 被过滤（false）', () => {
    expect(isHourInViewport(2, 1, 8, 10)).toBe(false);
  });
  it('isHourInViewport：事件 hour 12, 1h 时长，viewport [8, 10] → 被过滤（false）', () => {
    expect(isHourInViewport(12, 1, 8, 10)).toBe(false);
  });
  it('isHourInViewport：事件 hour 23, 1h 时长，viewport [8, 10] → 被过滤（false）', () => {
    expect(isHourInViewport(23, 1, 8, 10)).toBe(false);
  });
  it('isHourInViewport：事件 hour 9, 1h 时长，刚好在 viewport [8,10] → true', () => {
    expect(isHourInViewport(9, 1, 8, 10)).toBe(true);
  });
  it('isHourInViewport：事件跨边界（hour=7, dur=2），viewport [8,10] → 重叠 true', () => {
    // event range [7, 9] with buffer: start=6, end=10
    // viewport [8, 10]: overlap
    expect(isHourInViewport(7, 2, 8, 10)).toBe(true);
  });
  it('isHourInViewport：缓冲机制，紧邻视口外1h也返回 true（避免快速滚动空白）', () => {
    // 事件在 hour=7，视口 [8,10]：缓冲是 ±1h
    // event: [7-1, 7+1+1] = [6, 9] intersects [8,10]? Yes (8-9)
    expect(isHourInViewport(7, 1, 8, 10)).toBe(true);
  });
});

describe('calcVisibleHourRange defensive inputs', () => {
  it('hourHeightPx=0 → returns {0,24} no throw', () => {
    expect(() => calcVisibleHourRange(0, 720, 0)).not.toThrow();
    expect(calcVisibleHourRange(0, 720, 0)).toEqual({ visibleStartHour: 0, visibleEndHour: 24 });
  });

  it('hourHeightPx=-5 → returns {0,24} no throw', () => {
    expect(() => calcVisibleHourRange(0, 720, -5)).not.toThrow();
    expect(calcVisibleHourRange(0, 720, -5)).toEqual({ visibleStartHour: 0, visibleEndHour: 24 });
  });

  it('viewportHeightPx=0 → returns safe (valid) range', () => {
    const r = calcVisibleHourRange(0, 0, DESKTOP_HOUR_HEIGHT);
    expect(r.visibleStartHour).toBeGreaterThanOrEqual(0);
    expect(r.visibleEndHour).toBeLessThanOrEqual(24);
    expect(r.visibleStartHour).toBeLessThanOrEqual(r.visibleEndHour);
  });

  it('scrollTopPx=-100 negative scroll → clamps to 0 (start hour is 0 if clamped correctly)', () => {
    const r = calcVisibleHourRange(-100, 720, DESKTOP_HOUR_HEIGHT);
    expect(r.visibleStartHour).toBe(0);
  });

  it('scrollTopPx=undefined as arg → returns {0,24}', () => {
    const r = (calcVisibleHourRange as any)(undefined, 720, DESKTOP_HOUR_HEIGHT);
    expect(r).toEqual({ visibleStartHour: 0, visibleEndHour: 24 });
  });
});
