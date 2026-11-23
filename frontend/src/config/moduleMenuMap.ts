export interface MenuModuleMapping {
  moduleName: string;
  requiredModules?: string[];
}

export const menuModuleMap: Record<string, MenuModuleMapping> = {
  '/conversations': { moduleName: 'core' },
  '/customers': { moduleName: 'core' },
  '/tags': { moduleName: 'core' },
  '/satisfaction': { moduleName: 'core' },

  '/mailboxes': { moduleName: 'core' },
  '/mail-templates': { moduleName: 'core' },
  '/email-signatures': { moduleName: 'core' },
  '/canned-responses': { moduleName: 'core' },

  '/users': { moduleName: 'core' },
  '/teams': { moduleName: 'core' },
  '/roles': { moduleName: 'core' },

  '/workflows': { moduleName: 'core' },
  '/sla': { moduleName: 'core' },
  '/macros': { moduleName: 'core' },

  '/notifications': { moduleName: 'core' },
  '/notification-templates': { moduleName: 'core' },
  '/notification-rules': { moduleName: 'core' },

  '/webhooks': { moduleName: 'core' },
  '/api-keys': { moduleName: 'core' },
  '/custom-fields': { moduleName: 'core' },
  '/modules': { moduleName: 'core' },

  '/reports': { moduleName: 'core' },
  '/import-export': { moduleName: 'core' },

  '/audit-logs': { moduleName: 'core' },
  '/send-logs': { moduleName: 'core' },
  '/system-tools': { moduleName: 'core' },
  '/localization': { moduleName: 'core' },
  '/settings': { moduleName: 'core' },
  '/calendar': { moduleName: 'calendar' },
  '/knowledge': { moduleName: 'knowledgebase' },
  '/chat': { moduleName: 'chat' },
  '/wechat': { moduleName: 'wechat' },
  '/whatsapp': { moduleName: 'whatsapp' },
  '/feishu': { moduleName: 'feishu' },
  '/dingding': { moduleName: 'dingding' },
  '/messenger': { moduleName: 'messenger' },
  '/telegram': { moduleName: 'telegram' },
  '/line': { moduleName: 'line' },
  '/imessage': { moduleName: 'imessage' },
  '/time-tracking': { moduleName: 'time-tracking' },
  '/modules/custom-reports': { moduleName: 'custom-reports' },
};

export function getRequiredModulesForPath(path: string): string[] {
  let bestMatch: string | null = null;
  for (const key of Object.keys(menuModuleMap)) {
    if (path.startsWith(key) && (!bestMatch || key.length > bestMatch.length)) {
      bestMatch = key;
    }
  }
  if (!bestMatch) return [];
  const mapping = menuModuleMap[bestMatch];
  const modules = [mapping.moduleName];
  if (mapping.requiredModules) {
    modules.push(...mapping.requiredModules);
  }
  return modules;
}

export function isPathModuleActive(
  path: string,
  activeModules: Set<string>,
  allModules: Set<string>
): boolean {
  const required = getRequiredModulesForPath(path);
  if (required.length === 0) return true;
  return required.every((m) => {
    if (!allModules.has(m)) return true;
    return activeModules.has(m);
  });
}