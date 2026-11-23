import client from '../client';
import type {
  CustomReportDto,
  CustomReportExecutionResultDto,
  CustomReportScheduleDto,
  CreateCustomReportRequest,
  UpdateCustomReportRequest,
  CreateScheduleRequest,
  UpdateScheduleRequest,
  PinRequest,
  PagedCustomReportsResponse,
} from '../types';

export const customReportApi = {
  getPaged: (params?: { search?: string; category?: string; visibility?: string; page?: number; pageSize?: number }) =>
    client
      .get<PagedCustomReportsResponse>('/api/customreports', {
        params: {
          search: params?.search || undefined,
          category: params?.category || undefined,
          visibility: params?.visibility || undefined,
          page: params?.page ?? 1,
          pageSize: params?.pageSize ?? 12,
        },
      })
      .then((r) => r.data),

  getPinned: (limit = 6) =>
    client
      .get<CustomReportDto[]>('/api/customreports/pinned', { params: { limit } })
      .then((r) => r.data ?? []),

  getById: (id: number) =>
    client.get<CustomReportDto>(`/api/customreports/${id}`).then((r) => r.data),

  create: (data: CreateCustomReportRequest) =>
    client.post<CustomReportDto>('/api/customreports', data).then((r) => r.data),

  update: (id: number, data: UpdateCustomReportRequest) =>
    client.put<CustomReportDto>(`/api/customreports/${id}`, data).then((r) => r.data),

  remove: (id: number) =>
    client.delete<boolean>(`/api/customreports/${id}`).then((r) => r.data),

  getTrashPaged: (params?: { search?: string; page?: number; pageSize?: number }) =>
    client
      .get<PagedCustomReportsResponse>('/api/customreports/trash', {
        params: {
          search: params?.search || undefined,
          page: params?.page ?? 1,
          pageSize: params?.pageSize ?? 12,
        },
      })
      .then((r) => r.data),

  restore: (id: number) =>
    client.post<CustomReportDto>(`/api/customreports/${id}/restore`).then((r) => r.data),

  hardDelete: (id: number) =>
    client.delete<boolean>(`/api/customreports/${id}/hard`).then((r) => r.data),

  clearTrash: () =>
    client.post<{ cleared: number }>('/api/customreports/trash/clear').then((r) => r.data),

  togglePin: (id: number, data: PinRequest) =>
    client.patch<CustomReportDto>(`/api/customreports/${id}/pin`, data).then((r) => r.data),

  execute: (id: number) =>
    client
      .get<CustomReportExecutionResultDto>(`/api/customreports/${id}/execute`)
      .then((r) => r.data),

  executePreview: (data: CreateCustomReportRequest) =>
    client
      .post<CustomReportExecutionResultDto>('/api/customreports/execute-preview', data)
      .then((r) => r.data),

  exportFile: (id: number, format: 'csv' | 'xlsx' | 'pdf') =>
    client
      .get<Blob>(`/api/customreports/${id}/export`, {
        params: { format },
        responseType: 'blob',
      })
      .then((r) => r.data),

  listSchedules: (reportId: number) =>
    client
      .get<CustomReportScheduleDto[]>(`/api/customreports/${reportId}/schedules`)
      .then((r) => r.data ?? []),

  createSchedule: (reportId: number, data: CreateScheduleRequest) =>
    client
      .post<CustomReportScheduleDto>(`/api/customreports/${reportId}/schedules`, data)
      .then((r) => r.data),

  updateSchedule: (scheduleId: number, data: UpdateScheduleRequest) =>
    client
      .put<CustomReportScheduleDto>(`/api/customreports/schedules/${scheduleId}`, data)
      .then((r) => r.data),

  deleteSchedule: (scheduleId: number) =>
    client.delete<boolean>(`/api/customreports/schedules/${scheduleId}`).then((r) => r.data),
};
