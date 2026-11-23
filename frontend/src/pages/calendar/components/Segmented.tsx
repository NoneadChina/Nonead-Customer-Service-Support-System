import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';

export interface SegmentedOption<T extends string> {
  value: T;
  label: React.ReactNode;
  icon?: React.ReactNode;
  disabled?: boolean;
  /** T000108: 移动端降级为原生 select 时使用的纯文本（label 为 ReactNode 时无法直接渲染，需显式提供） */
  labelText?: string;
}

export interface SegmentedProps<T extends string> {
  value: T;
  onChange: (next: T) => void;
  options: SegmentedOption<T>[];
  size?: 'sm' | 'md';
  className?: string;
  /** <md 断点且选项数超过该阈值时，自动降级为原生 select */
  selectFallbackThreshold?: number;
}

const SIZE_CLS: Record<NonNullable<SegmentedProps<string>['size']>, string> = {
  sm: 'h-7 text-xs',
  md: 'h-9 text-sm',
};

/**
 * iOS 风格 Segmented 控件。
 * - 选中态由 indicator 滑块（绝对定位）平滑过渡
 * - <md 且 options.length > selectFallbackThreshold 时降级为原生 select
 * - 支持 ArrowLeft/Right/Home/End 键盘切换
 */
export function Segmented<T extends string>({
  value,
  onChange,
  options,
  size = 'md',
  className = '',
  selectFallbackThreshold = 3,
}: SegmentedProps<T>) {
  const containerRef = useRef<HTMLDivElement>(null);
  const [mounted, setMounted] = useState(false);
  const [indicatorStyle, setIndicatorStyle] = useState<{ x: number; w: number }>({ x: -9999, w: 0 });
  const [isMobile, setIsMobile] = useState(false);

  // 响应式：检测 <md(768px)
  useEffect(() => {
    const mq = window.matchMedia('(max-width: 767.98px)');
    const handler = (e: MediaQueryListEvent) => setIsMobile(e.matches);
    setIsMobile(mq.matches);
    mq.addEventListener('change', handler);
    return () => mq.removeEventListener('change', handler);
  }, []);

  // 更新 indicator 位置
  useEffect(() => {
    const container = containerRef.current;
    if (!container) return;
    const idx = options.findIndex((o) => o.value === value);
    if (idx < 0) return;
    const btn = container.querySelectorAll<HTMLButtonElement>('[role="tab"]')[idx];
    if (!btn) return;
    setIndicatorStyle({ x: btn.offsetLeft, w: btn.offsetWidth });
  }, [value, options, isMobile]);

  useEffect(() => {
    const t = window.requestAnimationFrame(() => setMounted(true));
    return () => window.cancelAnimationFrame(t);
  }, []);

  const mergedIndicatorStyle: React.CSSProperties = useMemo(() => ({
    transform: `translateX(${indicatorStyle.x}px)`,
    width: `${indicatorStyle.w}px`,
    height: `calc(100% - 4px)`,
    top: '2px',
    transition: mounted
      ? 'transform var(--cal-dur-normal, 180ms) var(--cal-ease, cubic-bezier(0.2,0,0,1)), width var(--cal-dur-normal, 180ms) var(--cal-ease, cubic-bezier(0.2,0,0,1))'
      : 'none',
    visibility: mounted ? 'visible' : 'hidden',
  }), [indicatorStyle, mounted]);

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent<HTMLDivElement>) => {
      const idx = options.findIndex((o) => o.value === value);
      if (idx < 0) return;
      let nextIdx = idx;
      if (e.key === 'ArrowRight') nextIdx = Math.min(options.length - 1, idx + 1);
      else if (e.key === 'ArrowLeft') nextIdx = Math.max(0, idx - 1);
      else if (e.key === 'Home') nextIdx = 0;
      else if (e.key === 'End') nextIdx = options.length - 1;
      else return;
      e.preventDefault();
      const nextOpt = options[nextIdx];
      if (nextOpt && !nextOpt.disabled) onChange(nextOpt.value);
    },
    [options, value, onChange],
  );

  // 移动端降级：渲染原生 select
  if (isMobile && options.length > selectFallbackThreshold) {
    return (
      <select
        value={value}
        onChange={(e) => onChange(e.target.value as T)}
        className={[
          'px-3 rounded-lg border border-slate-200 dark:border-slate-700',
          'bg-white dark:bg-slate-900 text-slate-700 dark:text-slate-200',
          'focus:ring-2 focus:ring-sky-500/30 focus:border-sky-400 transition',
          SIZE_CLS[size],
          className,
        ].join(' ')}
      >
        {options.map((opt) => (
          <option key={opt.value} value={opt.value} disabled={opt.disabled}>
            {opt.labelText ?? (typeof opt.label === 'string' ? opt.label : opt.value)}
          </option>
        ))}
      </select>
    );
  }

  return (
    <div
      ref={containerRef}
      role="tablist"
      onKeyDown={handleKeyDown}
      className={[
        'inline-flex items-center gap-0.5 rounded-lg border border-slate-200 dark:border-slate-700',
        'bg-white dark:bg-slate-800 p-0.5 shadow-sm ring-1 ring-slate-200/50 dark:ring-slate-700/50',
        'relative',
        SIZE_CLS[size],
        className,
      ].join(' ')}
    >
      {/* indicator 滑块 */}
      <div
        className="absolute rounded-[6px] bg-sky-500/10 dark:bg-sky-400/15 ring-1 ring-sky-500/20 dark:ring-sky-400/30 pointer-events-none"
        style={mergedIndicatorStyle}
      />
      {options.map((opt) => {
        const selected = opt.value === value;
        return (
          <button
            key={opt.value}
            role="tab"
            aria-selected={selected}
            aria-disabled={opt.disabled}
            disabled={opt.disabled}
            onClick={() => !opt.disabled && onChange(opt.value)}
            className={[
              'relative z-10 flex items-center gap-1.5 px-2 sm:px-3 font-medium rounded-[6px] transition-colors',
              selected
                ? 'text-sky-700 dark:text-sky-300'
                : 'text-slate-600 dark:text-slate-300 hover:text-slate-800 dark:hover:text-slate-100',
              opt.disabled ? 'opacity-40 cursor-not-allowed' : 'cursor-pointer',
            ].join(' ')}
            style={{ height: '100%' }}
          >
            {opt.icon}
            <span>{opt.label}</span>
          </button>
        );
      })}
    </div>
  );
}

export default Segmented;
