import { useState, useEffect } from 'react';
import { useParams, useSearchParams } from 'react-router-dom';
import { satisfactionApi } from '../api';
import { ApiError } from '../api/client';
import { getApiErrorMessage } from '../utils/error';
import { useI18n } from '../context/I18nContext';
import { tBound } from '../i18n/locale';
import type { TranslationKey } from '../locales/zh-Hans';
import {
  Star,
  AlertTriangle,
  CheckCircle2,
  Info,
  Loader2,
  Send,
  Heart,
} from 'lucide-react';

/**
 * 评分码 → 词条 key。码值与后端 `SatisfactionRatingConst` 对齐
 * （1=Bad / 2=Poor / 3=Average / 4=Good / 5=Excellent），中文值与该常量 `GetLabel` 的 5 条**逐字相同**。
 * 刻意**不渲染**后端下发的 `RatingLabel`（那是后端中文标签直出）。
 */
const RATING_LABEL_KEYS: Record<number, TranslationKey | undefined> = {
  1: 'serviceOps.satisfaction.rating.bad',
  2: 'serviceOps.satisfaction.rating.poor',
  3: 'serviceOps.satisfaction.rating.average',
  4: 'serviceOps.satisfaction.rating.good',
  5: 'serviceOps.satisfaction.rating.excellent',
};

const RATING_DESC_KEYS: Record<number, TranslationKey | undefined> = {
  1: 'serviceOps.satisfaction.ratingDesc.bad',
  2: 'serviceOps.satisfaction.ratingDesc.poor',
  3: 'serviceOps.satisfaction.ratingDesc.average',
  4: 'serviceOps.satisfaction.ratingDesc.good',
  5: 'serviceOps.satisfaction.ratingDesc.excellent',
};

/** key 表项 → 文案（未命中返回空串，与改造前 `ratingLabels[code] || ` 的兜底同义） */
function entryOf(t: (key: TranslationKey) => string, key: TranslationKey | undefined): string {
  return key ? t(key) : '';
}

const ratingBadgeColors: Record<number, string> = {
  1: 'from-red-500 to-rose-600',
  2: 'from-orange-500 to-red-500',
  3: 'from-amber-400 to-orange-500',
  4: 'from-emerald-400 to-teal-500',
  5: 'from-emerald-500 via-teal-500 to-blue-500',
};

function RatingStarsInteractive({
  value,
  onChange,
  size = 'lg',
}: {
  value: number;
  onChange: (v: number) => void;
  size?: 'sm' | 'md' | 'lg';
}) {
  const { t } = useI18n();
  const [hover, setHover] = useState(0);
  const display = hover || value;
  const sizeClass = size === 'sm' ? 'w-6 h-6' : size === 'md' ? 'w-9 h-9' : 'w-11 h-11 sm:w-12 sm:h-12';
  const gapClass = size === 'sm' ? 'gap-1' : size === 'md' ? 'gap-1.5' : 'gap-2 sm:gap-3';

  return (
    <div className={`flex items-center justify-center ${gapClass}`}>
      {[1, 2, 3, 4, 5].map((star) => {
        const active = display >= star;
        return (
          <button
            key={star}
            type="button"
            onClick={() => onChange(star)}
            onMouseEnter={() => setHover(star)}
            onMouseLeave={() => setHover(0)}
            className="group relative transition-transform hover:scale-110 active:scale-95"
            aria-label={t('chat.widget.rating.starAria', { star, label: entryOf(t, RATING_LABEL_KEYS[star]) })}
            title={t('serviceOps.satisfaction.starTitle', { star, label: entryOf(t, RATING_LABEL_KEYS[star]) })}
          >
            <Star
              className={`${sizeClass} transition-all duration-300 ${
                active
                  ? 'text-amber-400 drop-shadow-[0_4px_10px_rgba(251,191,36,0.45)]'
                  : 'text-slate-200 dark:text-slate-600'
              } group-hover:rotate-[8deg]`}
              fill={active ? 'currentColor' : 'none'}
              strokeWidth={active ? 0 : 1.8}
            />
            {active && (
              <Star
                className={`absolute inset-0 ${sizeClass} text-amber-300 animate-pulse opacity-40 pointer-events-none`}
                fill="currentColor"
                strokeWidth={0}
              />
            )}
          </button>
        );
      })}
    </div>
  );
}

function StaticStars({ value, size = 'lg' }: { value: number; size?: 'sm' | 'md' | 'lg' }) {
  const sizeClass = size === 'sm' ? 'w-6 h-6' : size === 'md' ? 'w-9 h-9' : 'w-11 h-11';
  const gapClass = size === 'sm' ? 'gap-1' : size === 'md' ? 'gap-1.5' : 'gap-2';
  return (
    <div className={`flex items-center justify-center ${gapClass}`}>
      {[1, 2, 3, 4, 5].map((star) => {
        const active = value >= star;
        return (
          <Star
            key={star}
            className={`${sizeClass} ${
              active
                ? 'text-amber-400 drop-shadow-[0_4px_10px_rgba(251,191,36,0.45)]'
                : 'text-slate-200 dark:text-slate-600'
            }`}
            fill={active ? 'currentColor' : 'none'}
            strokeWidth={active ? 0 : 1.8}
          />
        );
      })}
    </div>
  );
}

export default function SatisfactionForm() {
  const { t } = useI18n();
  const { token } = useParams<{ token: string }>();
  const [searchParams] = useSearchParams();
  const [loading, setLoading] = useState(true);
  const [valid, setValid] = useState(false);
  const [alreadyRated, setAlreadyRated] = useState(false);
  const [conversationId, setConversationId] = useState<number>(0);
  const [customerEmail, setCustomerEmail] = useState('');
  const [subject, setSubject] = useState('');
  const [selectedRating, setSelectedRating] = useState<number>(
    searchParams.get('rating') ? Number(searchParams.get('rating')) : 0
  );
  const [comment, setComment] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [submitted, setSubmitted] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    let destroyed = false;
    if (!token) {
      setValid(false);
      setLoading(false);
      return;
    }

    satisfactionApi
      .validateToken(token)
      .then((result) => {
        if (!destroyed) {
          if (result) {
            setValid(true);
            setConversationId(result.conversationId);
            // 防御性 trim：IM 渠道 enrich 写入 Conversations.CustomerEmail 的值
            // 可能含不可见字符，规范化在后端完成，这里至少去除前后空白再回填
            setCustomerEmail((result.customerEmail ?? '').trim());
            setSubject(result.subject);
            setAlreadyRated(result.alreadyRated);
          } else {
            setValid(false);
          }
        }
      })
      .catch((err: unknown) => {
        console.error('Failed to validate satisfaction token:', err);
        if (!destroyed) {
          setValid(false);
          setError(getApiErrorMessage(err, tBound('serviceOps.satisfaction.form.validateFailed')));
        }
      })
      .finally(() => {
        if (!destroyed) setLoading(false);
      });
    return () => { destroyed = true; };
  }, [token]);

  const handleSubmit = async () => {
    if (!selectedRating || selectedRating < 1 || selectedRating > 5) {
      setError(t('serviceOps.satisfaction.form.selectRating'));
      return;
    }

    setSubmitting(true);
    setError('');

    try {
      await satisfactionApi.create({
        conversationId,
        rating: selectedRating,
        comment: comment.trim() || undefined,
        customerEmail: customerEmail.trim() || undefined,
        token: token || undefined,
      });
      setSubmitted(true);
    } catch (e: unknown) {
      const status = e instanceof ApiError ? e.status : undefined;
      const message = e instanceof Error ? e.message : t('serviceOps.satisfaction.form.submitFailed');
      if (status === 409) {
        setAlreadyRated(true);
      } else {
        setError(message);
      }
    } finally {
      setSubmitting(false);
    }
  };

  if (loading) {
    return (
      <div className="min-h-screen bg-gradient-to-br from-slate-50 via-blue-50/40 to-indigo-50/40 dark:from-slate-900 dark:via-slate-900 dark:to-indigo-950/30 flex items-center justify-center p-4">
        <div className="flex flex-col items-center gap-3 text-slate-500 dark:text-slate-400">
          <Loader2 className="w-8 h-8 animate-spin text-blue-500" />
          <span className="text-sm font-medium">{t('common.shell.loading')}</span>
        </div>
      </div>
    );
  }

  if (!valid) {
    return (
      <div className="min-h-screen bg-gradient-to-br from-slate-50 via-blue-50/40 to-indigo-50/40 dark:from-slate-900 dark:via-slate-900 dark:to-indigo-950/30 flex items-center justify-center p-4 sm:p-6">
        <div className="max-w-md w-full">
          <div className="relative bg-white dark:bg-slate-800 rounded-3xl shadow-[0_30px_80px_-20px_rgba(15,23,42,0.35)] p-8 sm:p-10 text-center overflow-hidden border border-slate-200/60 dark:border-slate-700/60">
            <div className="absolute -top-24 -right-24 w-56 h-56 rounded-full bg-gradient-to-br from-red-200/40 to-orange-200/40 dark:from-red-900/20 dark:to-orange-900/10 blur-3xl pointer-events-none" />
            <div className="absolute -bottom-24 -left-24 w-56 h-56 rounded-full bg-gradient-to-br from-indigo-200/40 to-purple-200/40 dark:from-indigo-900/20 dark:to-purple-900/10 blur-3xl pointer-events-none" />

            <div className="relative">
              <div className="w-20 h-20 mx-auto mb-5 rounded-2xl bg-gradient-to-br from-red-100 to-orange-100 dark:from-red-900/30 dark:to-orange-900/20 flex items-center justify-center shadow-inner">
                <AlertTriangle className="w-10 h-10 text-red-500" strokeWidth={1.8} />
              </div>
              <h1 className="text-2xl font-bold text-slate-900 dark:text-slate-100 mb-2">
                {error ? t('serviceOps.common.loadFailed') : t('serviceOps.satisfaction.form.invalidTitle')}
              </h1>
              <p className="text-slate-500 dark:text-slate-400 text-sm leading-relaxed">
                {error || t('serviceOps.satisfaction.form.invalidMessage')}
              </p>
            </div>
          </div>
        </div>
      </div>
    );
  }

  if (submitted) {
    const label = entryOf(t, RATING_LABEL_KEYS[selectedRating]);
    const badge = ratingBadgeColors[selectedRating] || ratingBadgeColors[3];
    return (
      <div className="min-h-screen bg-gradient-to-br from-slate-50 via-blue-50/40 to-indigo-50/40 dark:from-slate-900 dark:via-slate-900 dark:to-indigo-950/30 flex items-center justify-center p-4 sm:p-6">
        <div className="max-w-md w-full">
          <div className="relative bg-white dark:bg-slate-800 rounded-3xl shadow-[0_30px_80px_-20px_rgba(15,23,42,0.35)] p-8 sm:p-10 text-center overflow-hidden border border-slate-200/60 dark:border-slate-700/60">
            <div className="absolute -top-24 -right-24 w-56 h-56 rounded-full bg-gradient-to-br from-emerald-200/50 to-teal-200/50 dark:from-emerald-900/20 dark:to-teal-900/20 blur-3xl pointer-events-none" />
            <div className="absolute -bottom-24 -left-24 w-56 h-56 rounded-full bg-gradient-to-br from-indigo-200/40 to-purple-200/40 dark:from-indigo-900/20 dark:to-purple-900/10 blur-3xl pointer-events-none" />

            <div className="relative">
              <div className="w-20 h-20 mx-auto mb-5 rounded-2xl bg-gradient-to-br from-emerald-400 via-teal-500 to-blue-500 flex items-center justify-center shadow-lg shadow-emerald-500/30">
                <CheckCircle2 className="w-11 h-11 text-white" strokeWidth={2} />
              </div>
              <h1 className="text-2xl sm:text-3xl font-extrabold text-slate-900 dark:text-slate-100 mb-3">
                {t('serviceOps.satisfaction.form.successTitle')}
              </h1>

              {selectedRating > 0 && (
                <div className="my-6 py-5 rounded-2xl bg-gradient-to-b from-slate-50/80 to-white dark:from-slate-700/40 dark:to-slate-800 border border-slate-200/70 dark:border-slate-700/60">
                  <StaticStars value={selectedRating} />
                  <div className="mt-3 flex items-center justify-center gap-2">
                    <span className={`px-3 py-1 rounded-full text-xs font-bold text-white shadow-sm bg-gradient-to-r ${badge}`}>
                      {t('serviceOps.satisfaction.form.successStars', { rating: selectedRating })}
                    </span>
                    {label && (
                      <span className="text-sm font-semibold text-slate-700 dark:text-slate-300">
                        {label}
                      </span>
                    )}
                  </div>
                </div>
              )}

              <p className="text-slate-500 dark:text-slate-400 text-sm leading-relaxed">
                {t('serviceOps.satisfaction.form.successLine1')}
                <br />{t('serviceOps.satisfaction.form.successLine2')}
              </p>
            </div>
          </div>
        </div>
      </div>
    );
  }

  if (alreadyRated) {
    return (
      <div className="min-h-screen bg-gradient-to-br from-slate-50 via-blue-50/40 to-indigo-50/40 dark:from-slate-900 dark:via-slate-900 dark:to-indigo-950/30 flex items-center justify-center p-4 sm:p-6">
        <div className="max-w-md w-full">
          <div className="relative bg-white dark:bg-slate-800 rounded-3xl shadow-[0_30px_80px_-20px_rgba(15,23,42,0.35)] p-8 sm:p-10 text-center overflow-hidden border border-slate-200/60 dark:border-slate-700/60">
            <div className="absolute -top-24 -right-24 w-56 h-56 rounded-full bg-gradient-to-br from-blue-200/40 to-indigo-200/40 dark:from-blue-900/20 dark:to-indigo-900/10 blur-3xl pointer-events-none" />
            <div className="absolute -bottom-24 -left-24 w-56 h-56 rounded-full bg-gradient-to-br from-emerald-200/40 to-teal-200/40 dark:from-emerald-900/20 dark:to-teal-900/10 blur-3xl pointer-events-none" />

            <div className="relative">
              <div className="w-20 h-20 mx-auto mb-5 rounded-2xl bg-gradient-to-br from-blue-100 to-indigo-100 dark:from-blue-900/30 dark:to-indigo-900/20 flex items-center justify-center shadow-inner">
                <Info className="w-10 h-10 text-blue-500" strokeWidth={1.8} />
              </div>
              <h1 className="text-2xl font-bold text-slate-900 dark:text-slate-100 mb-2">
                {t('serviceOps.satisfaction.form.alreadyTitle')}
              </h1>
              <p className="text-slate-500 dark:text-slate-400 text-sm leading-relaxed">
                {t('serviceOps.satisfaction.form.alreadyMessage')}
              </p>
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-50 via-blue-50/50 to-indigo-50/50 dark:from-slate-900 dark:via-slate-900 dark:to-indigo-950/30 flex items-center justify-center p-4 sm:p-6">
      <div className="max-w-lg w-full">
        {/* 页头 */}
        <div className="text-center mb-7">
          <div className="relative inline-flex items-center justify-center mb-4">
            <div className="absolute inset-0 w-16 h-16 rounded-2xl bg-gradient-to-br from-amber-400 to-orange-500 blur-2xl opacity-40 animate-pulse" />
            <div className="relative w-16 h-16 rounded-2xl bg-gradient-to-br from-amber-400 via-orange-400 to-rose-400 flex items-center justify-center shadow-lg shadow-amber-500/30">
              <Heart className="w-8 h-8 text-white" strokeWidth={2} />
            </div>
          </div>
          <h1 className="text-3xl sm:text-4xl font-extrabold bg-gradient-to-r from-slate-900 via-slate-800 to-slate-900 dark:from-white dark:via-slate-100 dark:to-white bg-clip-text text-transparent">
            {t('serviceOps.satisfaction.form.pageTitle')}
          </h1>
          <p className="text-slate-500 dark:text-slate-400 text-sm mt-2 leading-relaxed">
            {subject || t('serviceOps.satisfaction.form.fallbackSubject')}
          </p>
        </div>

        {/* 主卡片 */}
        <div className="relative bg-white dark:bg-slate-800 rounded-3xl shadow-[0_30px_80px_-20px_rgba(15,23,42,0.35)] overflow-hidden border border-slate-200/60 dark:border-slate-700/60">
          {/* 顶部装饰条 */}
          <div className="h-1.5 w-full bg-gradient-to-r from-amber-400 via-orange-400 to-rose-500" />

          <div className="p-6 sm:p-8">
            {/* 评分区 */}
            <div className="mb-8">
              <label className="block text-center mb-4">
                <span className="text-sm sm:text-base font-bold text-slate-700 dark:text-slate-200">
                  {t('serviceOps.satisfaction.form.ratingLabel')}
                </span>
              </label>

              <div className="py-5 rounded-3xl bg-gradient-to-b from-slate-50/80 to-white dark:from-slate-700/30 dark:to-slate-800 border border-slate-200/70 dark:border-slate-700/60">
                <RatingStarsInteractive
                  value={selectedRating}
                  onChange={(v) => {
                    setSelectedRating(v);
                    setError('');
                  }}
                />

                <div className="mt-4 h-12 flex items-center justify-center">
                  {selectedRating > 0 ? (
                    <div className="flex flex-col items-center gap-1.5 animate-[fadeIn_0.3s_ease]">
                      <span
                        className={`px-4 py-1.5 rounded-full text-xs sm:text-sm font-extrabold text-white shadow-md bg-gradient-to-r ${
                          ratingBadgeColors[selectedRating]
                        }`}
                      >
                        {t('serviceOps.satisfaction.form.selectedSummary', { rating: selectedRating, label: entryOf(t, RATING_LABEL_KEYS[selectedRating]) })}
                      </span>
                      <span className="text-xs text-slate-500 dark:text-slate-400 font-medium">
                        {entryOf(t, RATING_DESC_KEYS[selectedRating])}
                      </span>
                    </div>
                  ) : (
                    <span className="text-sm text-slate-400 dark:text-slate-500 animate-pulse">
                      {t('serviceOps.satisfaction.form.ratingHint')}
                    </span>
                  )}
                </div>
              </div>
            </div>

            {/* 建议 */}
            <div className="mb-6">
              <label className="flex items-center justify-between mb-2">
                <span className="text-sm font-bold text-slate-700 dark:text-slate-200">
                  {t('serviceOps.satisfaction.form.commentLabel')}
                  <span className="ml-1.5 text-slate-400 dark:text-slate-500 text-[11px] font-normal">
                    {t('serviceOps.satisfaction.form.optional')}
                  </span>
                </span>
                <span
                  className={`text-[11px] font-semibold px-2 py-0.5 rounded-md ${
                    comment.length > 450
                      ? 'bg-red-50 text-red-500 dark:bg-red-900/20'
                      : comment.length > 350
                      ? 'bg-amber-50 text-amber-600 dark:bg-amber-900/20'
                      : 'bg-slate-100 text-slate-500 dark:bg-slate-700/50 dark:text-slate-400'
                  }`}
                >
                  {comment.length} / 500
                </span>
              </label>
              <div className="relative group">
                <textarea
                  value={comment}
                  onChange={(e) => setComment(e.target.value.slice(0, 500))}
                  placeholder={t('serviceOps.satisfaction.form.commentPlaceholder')}
                  rows={5}
                  className="w-full px-4 py-3.5 text-sm border-2 border-slate-200 dark:border-slate-700 rounded-2xl bg-white dark:bg-slate-900/50 text-slate-900 dark:text-slate-100 placeholder:text-slate-400 resize-none focus:outline-none focus:ring-4 focus:ring-amber-400/20 focus:border-amber-400 transition-all shadow-sm group-focus-within:shadow-md leading-relaxed"
                />
              </div>
            </div>

            {/* 错误提示 */}
            {error && (
              <div className="mb-5 flex items-start gap-2.5 p-3.5 rounded-2xl bg-red-50/90 dark:bg-red-900/20 border border-red-200 dark:border-red-800/50 text-red-600 dark:text-red-400 text-sm shadow-sm animate-[shake_0.3s_ease]">
                <AlertTriangle className="w-5 h-5 flex-shrink-0 mt-0.5" strokeWidth={1.8} />
                <span className="font-medium leading-relaxed">{error}</span>
              </div>
            )}

            {/* 提交按钮 */}
            <button
              onClick={handleSubmit}
              disabled={submitting || !selectedRating}
              className={`w-full relative overflow-hidden py-3.5 px-6 rounded-2xl font-bold text-white transition-all duration-300 flex items-center justify-center gap-2.5 shadow-md ${
                !selectedRating || submitting
                  ? 'bg-slate-200 dark:bg-slate-700 text-slate-400 dark:text-slate-500 cursor-not-allowed shadow-none'
                  : 'bg-gradient-to-r from-amber-500 via-orange-500 to-rose-500 hover:from-amber-600 hover:via-orange-600 hover:to-rose-600 hover:shadow-lg hover:shadow-orange-500/30 hover:-translate-y-0.5 active:translate-y-0 active:scale-[0.99]'
              }`}
            >
              {submitting ? (
                <>
                  <Loader2 className="w-5 h-5 animate-spin" />
                  {t('serviceOps.satisfaction.form.submitting')}
                </>
              ) : (
                <>
                  <Send className="w-5 h-5" strokeWidth={2} />
                  {t('serviceOps.satisfaction.form.submit')}
                </>
              )}
              {!submitting && selectedRating > 0 && (
                <span className="absolute inset-0 -translate-x-full bg-gradient-to-r from-transparent via-white/20 to-transparent group-hover:translate-x-full transition-transform duration-1000" />
              )}
            </button>
          </div>
        </div>

        <p className="text-center text-xs text-slate-400 dark:text-slate-500 mt-5 leading-relaxed">
          {t('serviceOps.satisfaction.form.footer')}
        </p>
      </div>
    </div>
  );
}

