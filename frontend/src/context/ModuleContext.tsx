/* eslint-disable react-refresh/only-export-components -- Provider 与 useModules hook 同属 context 模块，fast refresh 非关注点 */
import { createContext, useContext, useEffect, useState, ReactNode, useCallback, useMemo } from 'react';
import { moduleApi } from '../api';
import type { ModuleDto } from '../api/types';
import { useAuth } from './AuthContext';

interface ModuleContextValue {
  modules: ModuleDto[];
  activeModuleNames: Set<string>;
  loading: boolean;
  refreshModules: () => Promise<void>;
  isModuleActive: (name: string) => boolean;
}

const ModuleContext = createContext<ModuleContextValue | undefined>(undefined);

export function ModuleProvider({ children }: { children: ReactNode }) {
  const { isAuthenticated } = useAuth();
  const [modules, setModules] = useState<ModuleDto[]>([]);
  const [loading, setLoading] = useState(true);

  const refreshModules = useCallback(async () => {
    if (!isAuthenticated) {
      setModules([]);
      setLoading(false);
      return;
    }
    try {
      const resp = await moduleApi.all();
      setModules(Array.isArray(resp) ? resp : []);
    } catch {
      setModules([]);
    } finally {
      setLoading(false);
    }
  }, [isAuthenticated]);

  useEffect(() => {
    refreshModules();
  }, [refreshModules]);

  // 使用 useMemo 缓存 Set，避免每次渲染都创建新实例导致不必要的重渲染
  const activeModuleNames = useMemo(
    () => new Set(modules.filter((m) => m.isActive).map((m) => m.name)),
    [modules]
  );

  const isModuleActive = useCallback(
    (name: string) => activeModuleNames.has(name),
    [activeModuleNames]
  );

  return (
    <ModuleContext.Provider
      value={{ modules, activeModuleNames, loading, refreshModules, isModuleActive }}
    >
      {children}
    </ModuleContext.Provider>
  );
}

export function useModules() {
  const ctx = useContext(ModuleContext);
  if (!ctx) {
    throw new Error('useModules must be used within a ModuleProvider');
  }
  return ctx;
}