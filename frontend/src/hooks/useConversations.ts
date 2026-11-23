import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { conversationApi, mailboxApi, userApi, tagApi } from '../api';
import type { BulkOperationResponse } from '../api/types';

export const queryKeys = {
  conversations: {
    all: ['conversations'] as const,
    lists: (params: Record<string, unknown>) => [...queryKeys.conversations.all, 'list', params] as const,
    detail: (id: number) => [...queryKeys.conversations.all, 'detail', id] as const,
    threads: (id: number) => [...queryKeys.conversations.all, 'threads', id] as const,
    followers: (id: number) => [...queryKeys.conversations.all, 'followers', id] as const,
    trash: (mailboxId?: number) => [...queryKeys.conversations.all, 'trash', mailboxId] as const,
    starredIds: ['conversations', 'starredIds'] as const,
  },
  mailboxes: {
    all: ['mailboxes'] as const,
    list: ['mailboxes', 'list'] as const,
    listAll: ['mailboxes', 'listAll'] as const,
  },
  users: {
    all: ['users'] as const,
    list: ['users', 'list'] as const,
    assignable: ['users', 'assignable'] as const,
  },
  tags: {
    all: ['tags'] as const,
    list: (mailboxId?: number) => [...queryKeys.tags.all, 'list', mailboxId] as const,
  },
};

export function useConversations(params?: {
  mailboxId?: number;
  folderId?: number;
  page?: number;
  pageSize?: number;
  search?: string;
  status?: number;
  priority?: number;
  starred?: boolean;
  sourceType?: number;
}) {
  return useQuery({
    queryKey: queryKeys.conversations.lists(params ?? {}),
    queryFn: () => conversationApi.list(params),
    enabled: true,
    refetchOnMount: 'always',
  });
}

export function useConversation(id: number | null | undefined) {
  return useQuery({
    queryKey: queryKeys.conversations.detail(id ?? 0),
    queryFn: () => {
      if (!id) throw new Error('id is required');
      return conversationApi.get(id);
    },
    enabled: !!id,
  });
}

/**
 * 标记会话为已读。进入会话详情时调用，使列表页未读徽章消失。
 * 后端 GET /api/conversations/{id} 已会顺带标记已读，此 mutation 用于显式触发列表缓存失效。
 */
export function useMarkConversationAsRead() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => conversationApi.markRead(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
    },
  });
}

export function useConversationThreads(id: number | null | undefined) {
  return useQuery({
    queryKey: queryKeys.conversations.threads(id ?? 0),
    queryFn: () => {
      if (!id) throw new Error('id is required');
      return conversationApi.getThreads(id);
    },
    enabled: !!id,
  });
}

export function useMailboxes() {
  return useQuery({
    queryKey: queryKeys.mailboxes.list,
    queryFn: () => mailboxApi.list(),
  });
}

export function useAllMailboxes() {
  return useQuery({
    queryKey: queryKeys.mailboxes.listAll,
    queryFn: () => mailboxApi.listAll(),
  });
}

export function useUsers() {
  return useQuery({
    queryKey: queryKeys.users.list,
    queryFn: async () => {
      const result = await userApi.list();
      // userApi.list() 返回分页对象 {data: UserResponse[], ...}，需要提取数组
      return Array.isArray(result?.data) ? result.data : (Array.isArray(result) ? result : []);
    },
  });
}

/**
 * 获取可被分配会话的活跃用户列表。
 * 调用 GET /api/users/assignable（受 conversations.assign 权限保护），
 * 客服主管/客服无需 SuperAdmin 即可获取，用于"分配负责人"下拉框（修复 T000061）。
 */
export function useAssignableUsers() {
  return useQuery({
    queryKey: queryKeys.users.assignable,
    queryFn: () => userApi.assignable(),
  });
}

export function useTags(mailboxId?: number) {
  return useQuery({
    queryKey: queryKeys.tags.list(mailboxId),
    queryFn: () => tagApi.list(mailboxId),
  });
}

export function useUpdateConversationStatus() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, status }: { id: number; status: number }) =>
      conversationApi.updateStatus(id, status),
    onSuccess: (_, { id }) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.detail(id) });
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
    },
  });
}

export function useUpdateConversationPriority() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, priority }: { id: number; priority: number }) =>
      conversationApi.updatePriority(id, priority),
    onSuccess: (_, { id }) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.detail(id) });
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
    },
  });
}

export function useAssignConversation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, userId }: { id: number; userId: number }) =>
      conversationApi.assign(id, userId),
    onSuccess: (_, { id }) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.detail(id) });
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
    },
  });
}

export function useToggleFollow() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => conversationApi.toggleFollow(id),
    onSuccess: (_, id) => {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.detail(id) });
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.followers(id) });
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
    },
  });
}

export function useDeleteConversation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => conversationApi.remove(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
    },
  });
}

export function useFollowers(id: number | null | undefined) {
  return useQuery({
    queryKey: queryKeys.conversations.followers(id ?? 0),
    queryFn: () => {
      if (!id) throw new Error('id is required');
      return conversationApi.getFollowers(id);
    },
    enabled: !!id,
  });
}

export function useBulkUpdateStatus() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ ids, status }: { ids: number[]; status: number }) =>
      conversationApi.bulkUpdateStatus(ids, status),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
    },
  });
}

export function useBulkAssign() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ ids, userId }: { ids: number[]; userId: number }) =>
      conversationApi.bulkAssign(ids, userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
    },
  });
}

export function useBulkDelete() {
  const queryClient = useQueryClient();
  return useMutation<BulkOperationResponse, Error, number[]>({
    mutationFn: (ids: number[]) =>
      conversationApi.bulkRemove(ids),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
    },
  });
}

export function useBulkUpdatePriority() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ ids, priority }: { ids: number[]; priority: number }) =>
      conversationApi.bulkUpdatePriority(ids, priority),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
    },
  });
}

export function useTrashConversations(mailboxId?: number) {
  return useQuery({
    queryKey: queryKeys.conversations.trash(mailboxId),
    queryFn: () => conversationApi.getTrash(mailboxId ?? 0, 1, 200),
    enabled: mailboxId !== undefined,
  });
}

export function useStarredIds() {
  return useQuery({
    queryKey: queryKeys.conversations.starredIds,
    queryFn: () => conversationApi.getStarredIds(),
  });
}

export function useRestoreConversation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => conversationApi.restore(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
    },
  });
}

export function useHardDeleteConversation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: number) => conversationApi.hardDelete(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
    },
  });
}

export function useClearTrash() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (mailboxId?: number) => conversationApi.clearTrash(mailboxId ?? 0),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
    },
  });
}

export function useBulkAddTags() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ ids, tagIds }: { ids: number[]; tagIds: number[] }) =>
      tagApi.bulkUpdateConversationTags(ids, tagIds),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: queryKeys.conversations.all });
    },
  });
}
