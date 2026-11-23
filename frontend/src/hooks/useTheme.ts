import { useEffect, useState, useCallback } from 'react';

export type Theme = 'light' | 'dark' | 'system';

const storageKey = 'noneadcss:theme';
const legacyStorageKey = 'freescout:theme';

function migrateLegacyTheme(): void {
  try {
    const legacy = localStorage.getItem(legacyStorageKey);
    if (legacy && !localStorage.getItem(storageKey)) {
      localStorage.setItem(storageKey, legacy);
    }
  } catch {
    // ignore
  }
}

function getSystemTheme(): 'light' | 'dark' {
  if (typeof window !== 'undefined' && window.matchMedia) {
    return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
  }
  return 'light';
}

function getEffectiveTheme(theme: Theme): 'light' | 'dark' {
  if (theme === 'system') {
    return getSystemTheme();
  }
  return theme;
}

export function useTheme() {
  const [theme, setTheme] = useState<Theme>(() => {
    if (typeof window !== 'undefined') {
      migrateLegacyTheme();
      const saved = localStorage.getItem(storageKey);
      if (saved === 'light' || saved === 'dark' || saved === 'system') {
        return saved;
      }
    }
    return 'light';
  });

  const [effectiveTheme, setEffectiveTheme] = useState<'light' | 'dark'>(() => {
    return getEffectiveTheme(theme);
  });

  const applyTheme = useCallback((themeValue: 'light' | 'dark') => {
    const root = document.documentElement;
    if (themeValue === 'dark') {
      root.classList.add('dark');
      root.classList.remove('light');
    } else {
      root.classList.remove('dark');
      root.classList.add('light');
    }
  }, []);

  useEffect(() => {
    applyTheme(effectiveTheme);
  }, [effectiveTheme, applyTheme]);

  useEffect(() => {
    const newEffectiveTheme = getEffectiveTheme(theme);
    setEffectiveTheme(newEffectiveTheme);
    localStorage.setItem(storageKey, theme);
  }, [theme]);

  useEffect(() => {
    if (theme !== 'system') return;

    const mediaQuery = window.matchMedia('(prefers-color-scheme: dark)');
    const handler = (e: MediaQueryListEvent) => {
      setEffectiveTheme(e.matches ? 'dark' : 'light');
    };

    mediaQuery.addEventListener('change', handler);
    return () => mediaQuery.removeEventListener('change', handler);
  }, [theme]);

  const setThemeValue = useCallback((newTheme: Theme) => {
    setTheme(newTheme);
  }, []);

  return {
    theme,
    effectiveTheme,
    setTheme: setThemeValue,
  };
}
