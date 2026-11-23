import client from '../client';
import type {
  TimeEntryDto,
  CreateTimeEntryRequest,
  UpdateTimeEntryRequest,
  StartTimerRequest,
  TimeEntryQuery,
  TimeTrackingOverviewResponse,
  TimeTrackingReportResponse,
  PagedTimeEntries,
} from '../types/timeTracking';

export const timeTrackingApi = {
  list: (params?: TimeEntryQuery) =>
    client.get<PagedTimeEntries>('/api/time-tracking', { params }).then((r) => r.data),
  getById: (id: number) =>
    client.get<TimeEntryDto>(`/api/time-tracking/${id}`).then((r) => r.data),
  create: (data: CreateTimeEntryRequest) =>
    client.post<TimeEntryDto>('/api/time-tracking', data).then((r) => r.data),
  update: (id: number, data: UpdateTimeEntryRequest) =>
    client.put<TimeEntryDto>(`/api/time-tracking/${id}`, data, {
      headers: data?.ifMatch ? { 'If-Match': data.ifMatch } : undefined,
    }).then((r) => r.data),
  remove: (id: number) =>
    client.delete<boolean>(`/api/time-tracking/${id}`).then((r) => r.data),
  startTimer: (data: StartTimerRequest) =>
    client.post<TimeEntryDto>('/api/time-tracking/timer/start', data).then((r) => r.data),
  stopTimer: (id: number, rowVersion?: string | null) =>
    client.post<TimeEntryDto>(`/api/time-tracking/timer/${id}/stop`, {}, {
      headers: rowVersion ? { 'If-Match': rowVersion } : undefined,
    }).then((r) => r.data),
  getActiveTimer: () =>
    client.get<TimeEntryDto | null>('/api/time-tracking/timer/active').then((r) => r.data),
  overview: () =>
    client.get<TimeTrackingOverviewResponse>('/api/time-tracking/overview').then((r) => r.data),
  report: (params?: { startDate?: string; endDate?: string; groupBy?: string }) =>
    client.get<TimeTrackingReportResponse>('/api/time-tracking/report', { params }).then((r) => r.data),
  export: (params?: TimeEntryQuery) =>
    client
      .get<Blob>('/api/time-tracking/export', { params, responseType: 'blob' })
      .then((r) => r.data),
};
