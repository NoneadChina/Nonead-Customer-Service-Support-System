/* eslint-disable react-refresh/only-export-components -- Provider 与 useMailboxes hook 同属 context 模块，fast refresh 非关注点 */
import { createContext, useContext, useEffect, useState, ReactNode, useCallback, useRef } from 'react';
import { mailboxApi } from '../api';
import type { MailboxResponse } from '../api/types';
import { useAuth } from './AuthContext';
import { useI18n } from './I18nContext';

interface MailboxContextValue {
  mailboxes: MailboxResponse[];
  loading: boolean;
  refreshMailboxes: () => Promise<void>;
  getMailboxName: (id: number | null | undefined) => string;
}

const MailboxContext = createContext<MailboxContextValue | undefined>(undefined);

export function MailboxProvider({ children }: { children: ReactNode }) {
  const { isAuthenticated } = useAuth();
  const { t } = useI18n();
  const [mailboxes, setMailboxes] = useState<MailboxResponse[]>([]);
  const [loading, setLoading] = useState(true);

  const isMountedRef = useRef(true);

  const refreshMailboxes = useCallback(async () => {
    if (!isAuthenticated) {
      setMailboxes([]);
      setLoading(false);
      return;
    }
    try {
      const resp = await mailboxApi.listAll();
      if (!isMountedRef.current) return;
      setMailboxes(Array.isArray(resp) ? resp : []);
    } catch {
      if (!isMountedRef.current) return;
      setMailboxes([]);
    } finally {
      if (isMountedRef.current) setLoading(false);
    }
  }, [isAuthenticated]);

  useEffect(() => {
    isMountedRef.current = true;
    refreshMailboxes();
    return () => {
      isMountedRef.current = false;
    };
  }, [refreshMailboxes]);

  const getMailboxName = useCallback(
    (id: number | null | undefined) => {
      if (id === null || id === undefined) return t('mail.mailboxes.allMailboxes');
      return mailboxes.find((m) => m.id === id)?.name ?? `#${id}`;
    },
    [mailboxes, t]
  );

  return (
    <MailboxContext.Provider
      value={{ mailboxes, loading, refreshMailboxes, getMailboxName }}
    >
      {children}
    </MailboxContext.Provider>
  );
}

export function useMailboxes() {
  const ctx = useContext(MailboxContext);
  if (!ctx) {
    throw new Error('useMailboxes must be used within a MailboxProvider');
  }
  return ctx;
}