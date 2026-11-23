export interface PagedResponse<T> {
  data: T[];
  currentPage: number;
  pageSize: number;
  totalItems: number;
  totalPages: number;
}

export interface BulkOperationResponse {
  totalCount: number;
  successCount: number;
  failedCount: number;
  errors: string[];
}

export interface ApiError {
  message: string;
}
