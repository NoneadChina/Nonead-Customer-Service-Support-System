import client from '../client';
import type {
  ReportResponse,
  SatisfactionRatingDto,
  SatisfactionOverviewResponse,
  SatisfactionRatingQuery,
  CreateSatisfactionRatingRequest,
} from '../types';

// ====== 报表 API ======
export const reportApi = {
  getReport: (startDate?: string, endDate?: string, mailboxId?: number | null) =>
    client
      .get<ReportResponse>('/api/reports', {
        params: {
          startDate: startDate || undefined,
          endDate: endDate || undefined,
          mailboxId: mailboxId ?? undefined,
        },
      })
      .then((r) => r.data),
};

// ====== 满意度评价 API ======
export const satisfactionApi = {
  list: (params?: SatisfactionRatingQuery) =>
    client
      .get<{ data: SatisfactionRatingDto[]; totalItems: number; totalPages: number; currentPage: number; pageSize: number }>('/api/satisfactionratings', {
        params: {
          page: params?.page ?? 1,
          pageSize: params?.pageSize ?? 20,
          rating: params?.rating ?? undefined,
          mailboxId: params?.mailboxId ?? undefined,
          userId: params?.userId ?? undefined,
          startDate: params?.startDate || undefined,
          endDate: params?.endDate || undefined,
          keyword: params?.keyword || undefined,
        },
      })
      .then((r) => r.data),
  get: (id: number) =>
    client.get<SatisfactionRatingDto>(`/api/satisfactionratings/${id}`).then((r) => r.data),
  getByConversation: (conversationId: number) =>
    client
      .get<SatisfactionRatingDto>(`/api/satisfactionratings/conversation/${conversationId}`)
      .then((r) => r.data)
      .catch(() => null),
  overview: (startDate?: string, endDate?: string, mailboxId?: number | null) =>
    client
      .get<SatisfactionOverviewResponse>('/api/satisfactionratings/overview', {
        params: {
          startDate: startDate || undefined,
          endDate: endDate || undefined,
          mailboxId: mailboxId ?? undefined,
        },
      })
      .then((r) => r.data),
  recent: (count = 10, rating?: number | null) =>
    client
      .get<SatisfactionRatingDto[]>('/api/satisfactionratings/recent', {
        params: { count, rating: rating ?? undefined },
      })
      .then((r) => r.data ?? []),
  create: (data: CreateSatisfactionRatingRequest) =>
    client
      .post<SatisfactionRatingDto>('/api/satisfactionratings', data)
      .then((r) => r.data),
  remove: (id: number) =>
    client.delete<boolean>(`/api/satisfactionratings/${id}`).then((r) => r.data),
  ratingOptions: () =>
    client
      .get<Record<number, string>>('/api/satisfactionratings/rating-options')
      .then((r) => r.data),
  validateToken: (token: string) =>
    client
      .get<{
        conversationId: number;
        customerEmail: string;
        subject: string;
        alreadyRated: boolean;
      }>(`/api/satisfactionratings/validate/${token}`)
      .then((r) => r.data)
      .catch(() => null),
};
