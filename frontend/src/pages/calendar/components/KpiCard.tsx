import React, { memo } from 'react';
import { getBoundLocale, toIntlLocale } from '../../../i18n/locale';

export interface KpiCardProps {
  label: string;
  value: number;
  loading?: boolean;
  delta?: {
    pct: number;
    better: 'up' | 'down';
  };
  sparkline: number[];
  tone: 'sky' | 'violet' | 'emerald' | 'rose' | 'amber';
  icon: React.ReactNode;
  onClick?: () => void;
  /** 装饰条位置：'top' 顶部渐变横条（默认），'left' 左侧渐变竖条（和其他日历模态窗统一视觉语言） */
  accent?: 'top' | 'left';
}

const TONE_GRADIENT: Record<KpiCardProps['tone'], string> = {
  sky: 'from-sky-400 to-blue-500',
  violet: 'from-violet-400 to-purple-500',
  emerald: 'from-emerald-400 to-green-500',
  rose: 'from-rose-400 to-red-500',
  amber: 'from-amber-400 to-orange-500',
};

const TONE_ICON_BG: Record<KpiCardProps['tone'], string> = {
  sky: 'bg-gradient-to-br from-sky-50 to-sky-100 dark:from-sky-900/30 dark:to-sky-800/20 text-sky-600 dark:text-sky-400',
  violet: 'bg-gradient-to-br from-violet-50 to-violet-100 dark:from-violet-900/30 dark:to-violet-800/20 text-violet-600 dark:text-violet-400',
  emerald: 'bg-gradient-to-br from-emerald-50 to-emerald-100 dark:from-emerald-900/30 dark:to-emerald-800/20 text-emerald-600 dark:text-emerald-400',
  rose: 'bg-gradient-to-br from-rose-50 to-rose-100 dark:from-rose-900/30 dark:to-rose-800/20 text-rose-600 dark:text-rose-400',
  amber: 'bg-gradient-to-br from-amber-50 to-amber-100 dark:from-amber-900/30 dark:to-amber-800/20 text-amber-600 dark:text-amber-400',
};

const TONE_STROKE: Record<KpiCardProps['tone'], { from: string; to: string }> = {
  sky: { from: 'text-sky-400', to: 'text-sky-600' },
  violet: { from: 'text-violet-400', to: 'text-violet-600' },
  emerald: { from: 'text-emerald-400', to: 'text-emerald-600' },
  rose: { from: 'text-rose-400', to: 'text-rose-600' },
  amber: { from: 'text-amber-400', to: 'text-amber-600' },
};

function ShimmerBar({ className = '' }: { className?: string }) {
  return (
    <div className={`bg-gradient-to-r from-slate-100 via-slate-200 to-slate-100 dark:from-slate-800 dark:via-slate-700 dark:to-slate-800 animate-pulse rounded ${className}`} />
  );
}

function KpiCardImpl({ label, value, loading, delta, sparkline, tone, icon, onClick, accent = 'top' }: KpiCardProps) {
  // 构建 sparkline polyline points
  const points = sparkline
    .map((v, i) => `${(i * 100) / Math.max(1, sparkline.length - 1)},${24 - v * 0.24}`)
    .join(' ');
  const gradId = `kpiGrad-${tone}`;

  // delta 判断
  const deltaPositive = delta && delta.pct > 0;
  const deltaGood = delta && ((delta.better === 'up' && delta.pct > 0) || (delta.better === 'down' && delta.pct < 0));

  const content = loading ? (
    <div className="space-y-2">
      <ShimmerBar className="h-9 w-16" />
      <ShimmerBar className="h-4 w-20" />
      <ShimmerBar className="h-3 w-24 mt-3" />
    </div>
  ) : (
    <>
      <div className="flex items-start justify-between mb-3">
        <div className={`p-2 rounded-lg ${TONE_ICON_BG[tone]}`}>{icon}</div>
        {delta && (
          <span
            className={[
              'inline-flex items-center gap-0.5 px-1.5 py-0.5 rounded-full text-xs font-medium',
              deltaGood ? 'text-emerald-600 dark:text-emerald-400' : 'text-rose-600 dark:text-rose-400',
            ].join(' ')}
          >
            {deltaPositive ? '↑' : delta.pct < 0 ? '↓' : '→'} {Math.abs(delta.pct)}%
          </span>
        )}
      </div>
      <div className="text-3xl font-bold text-slate-900 dark:text-slate-100 tabular-nums tracking-tight">
        {new Intl.NumberFormat(toIntlLocale(getBoundLocale())).format(value)}
      </div>
      <div className="text-sm text-slate-500 dark:text-slate-400 mt-1">{label}</div>
      {/* sparkline */}
      <div className="mt-3 -mx-1 h-6">
        <svg viewBox="0 0 100 24" preserveAspectRatio="none" className="w-full h-full">
          <defs>
            <linearGradient id={gradId} x1="0%" y1="0%" x2="100%" y2="0%">
              <stop offset="0%" stopColor="currentColor" className={TONE_STROKE[tone].from} stopOpacity="0.6" />
              <stop offset="100%" stopColor="currentColor" className={TONE_STROKE[tone].to} stopOpacity="1" />
            </linearGradient>
          </defs>
          <polyline
            fill="none"
            stroke={`url(#${gradId})`}
            strokeWidth="1.5"
            strokeLinecap="round"
            strokeLinejoin="round"
            points={points}
          />
        </svg>
      </div>
    </>
  );

  const cls = [
    'group text-left bg-gradient-to-br from-white via-white to-slate-50/60 dark:from-slate-900 dark:via-slate-900 dark:to-slate-800/60',
    'border border-slate-200/70 dark:border-slate-800/80 rounded-2xl p-4 relative overflow-hidden',
    'hover:shadow-[0_10px_30px_-12px_rgba(14,165,233,0.28),0_0_0_1px_rgba(14,165,233,0.14)] hover:border-sky-200/80 dark:hover:border-sky-800/60 hover:-translate-y-0.5 active:scale-[0.985]',
    'transition-all duration-200 shadow-[0_1px_2px_rgba(15,23,42,0.05),0_0_0_1px_rgba(15,23,42,0.02)] backdrop-blur',
    accent === 'left' ? 'pl-4.5' : '',
  ].join(' ');

  // 光斑 + 装饰条
  const inner = (
    <>
      {accent === 'top' ? (
        <div className={`absolute top-0 left-0 right-0 h-[3px] bg-gradient-to-r ${TONE_GRADIENT[tone]} shadow-[0_1px_4px_-1px_rgba(0,0,0,0.2)]`} />
      ) : (
        <div className={`absolute left-0 top-0 bottom-0 w-[3px] bg-gradient-to-b ${TONE_GRADIENT[tone]} shadow-[2px_0_8px_-2px_rgba(0,0,0,0.18)]`} />
      )}
      {/* 光斑：右上 + 左下双光斑 + hover 增强 */}
      <div
        className={`absolute -top-14 -left-12 w-[200px] h-[200px] rounded-full bg-gradient-to-br ${TONE_GRADIENT[tone]} opacity-[0.05] group-hover:opacity-[0.10] transition-opacity duration-300 pointer-events-none blur-2xl`}
      />
      <div
        className={`absolute -bottom-16 -right-10 w-[220px] h-[220px] rounded-full bg-gradient-to-tr ${TONE_GRADIENT[tone]} opacity-[0.04] group-hover:opacity-[0.08] transition-opacity duration-300 pointer-events-none blur-2xl`}
      />
      <div className="relative">{content}</div>
    </>
  );

  if (onClick) {
    return (
      <button type="button" onClick={onClick} className={cls}>
        {inner}
      </button>
    );
  }
  return <div className={cls}>{inner}</div>;
}

export const KpiCard = memo(KpiCardImpl);
KpiCard.displayName = 'KpiCard';
export default KpiCard;
