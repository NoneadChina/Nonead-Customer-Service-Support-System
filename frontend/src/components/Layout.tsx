import { ReactNode, useState, useEffect, useRef, useMemo } from 'react';
import { NavLink, useNavigate, useLocation } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { useI18n } from '../context/I18nContext';
import { LANGUAGE_OPTIONS, useLanguageSwitch } from '../hooks/useLanguageSwitch';
import type { TranslationKey } from '../locales/zh-Hans';
import { useModules } from '../context/ModuleContext';
import { usePermission } from '../hooks/usePermission';
import { isPathModuleActive } from '../config/moduleMenuMap';
import { useToast } from '../context/ToastContext';
import { useSignalREvent } from '../hooks/useSignalR';
import { SignalREvents } from '../services/signalR';
import GlobalSearch from './GlobalSearch';
import NotificationBell from './NotificationBell';
import UserAvatar from './UserAvatar';

interface NavItem {
  to: string;
  label: TranslationKey;
  icon: ReactNode;
}

interface NavGroup {
  key: string;
  label: TranslationKey;
  icon: ReactNode;
  children: NavItem[];
}

type NavEntry =
  | { type: 'item'; item: NavItem }
  | { type: 'group'; group: NavGroup };

// TODO(L-FE1): 当前侧边栏导航图标使用内联 SVG，后续应统一迁移至 lucide-react 图标库，
// 以减少重复代码、统一图标风格、并利用 lucide-react 的 tree-shaking 能力减小包体积。
const Icon = {
  dashboard: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-6 0a1 1 0 001-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 001 1m-6 0h6" />
    </svg>
  ),
  helpdesk: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
    </svg>
  ),
  mail: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
    </svg>
  ),
  folder: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M3.75 9.776c.112-.017.227-.026.344-.026h15.812c.117 0 .232.009.344.026m-16.5 0a2.25 2.25 0 00-1.883 2.542l.857 6a2.25 2.25 0 002.227 1.932H19.05a2.25 2.25 0 002.227-1.932l.857-6a2.25 2.25 0 00-1.883-2.542m-16.5 0V6A2.25 2.25 0 016 3.75H10.5a2.25 2.25 0 012.25 2.25v2.776" />
    </svg>
  ),
  users: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0zm6 3a2 2 0 11-4 0 2 2 0 014 0zM7 10a2 2 0 11-4 0 2 2 0 014 0z" />
    </svg>
  ),
  customer: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
    </svg>
  ),
  tag: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M7 7h.01M7 3h5c.512 0 1.024.195 1.414.586l7 7a2 2 0 010 2.828l-7 7a2 2 0 01-2.828 0l-7-7A1.994 1.994 0 013 12V7a4 4 0 014-4z" />
    </svg>
  ),
  fileText: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
    </svg>
  ),
  bell: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9" />
    </svg>
  ),
  fileBar: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M9 17v-2m3 2v-4m3 4v-6m2 10H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
    </svg>
  ),
  send: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M3 8l7.89 5.26a2 2 0 002.22 0L21 8M5 19h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
      <path strokeLinecap="round" strokeLinejoin="round" d="M12 11v4m0-4l-1.5 1.5M12 11l1.5 1.5" />
    </svg>
  ),
  smile: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M14.828 14.828a4 4 0 01-5.656 0M9 10h.01M15 10h.01M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
    </svg>
  ),
  zap: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M13 10V3L4 14h7v7l9-11h-7z" />
    </svg>
  ),
  clock: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M12 8v4l3 3m6-3a9 9 0 11-18 0 9 9 0 0118 0z" />
    </svg>
  ),
  settings2: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
      <path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
    </svg>
  ),
  edit: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
    </svg>
  ),
  link: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M13.828 10.172a4 4 0 00-5.656 0l-4 4a4 4 0 105.656 5.656l1.102-1.101m-.758-4.899a4 4 0 005.656 0l4-4a4 4 0 00-5.656-5.656l-1.1 1.1" />
    </svg>
  ),
  shield: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
    </svg>
  ),
  usersOutline: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0zm6 3a2 2 0 11-4 0 2 2 0 014 0zM7 10a2 2 0 11-4 0 2 2 0 014 0z" />
    </svg>
  ),
  signature: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
      <path strokeLinecap="round" strokeLinejoin="round" d="M9 12h6" />
    </svg>
  ),
  boxes: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M19 11H5m14 0a2 2 0 012 2v6a2 2 0 01-2 2H5a2 2 0 01-2-2v-6a2 2 0 012-2m14 0V9a2 2 0 00-2-2M5 11V9a2 2 0 012-2m0 0V5a2 2 0 012-2h6a2 2 0 012 2v2M7 7h10" />
    </svg>
  ),
  key: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M15 7a2 2 0 012 2m4 0a6 6 0 01-7.743 5.743L11 17H9v2H7v2H4a1 1 0 01-1-1v-2.586a1 1 0 01.293-.707l5.964-5.964A6 6 0 1121 9z" />
    </svg>
  ),
  chart: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z" />
    </svg>
  ),
  arrowUpDown: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
    </svg>
  ),
  settings: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
      <path strokeLinecap="round" strokeLinejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
    </svg>
  ),
  tools: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
    </svg>
  ),
  globe: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M3.055 11H5a2 2 0 012 2v1a2 2 0 002 2 2 2 0 012 2v2.945M8 3.935V5.5A2.5 2.5 0 0010.5 8h.5a2 2 0 012 2 2 2 0 104 0 2 2 0 012-2h1.064M15 20.488V18a2 2 0 012-2h3.064M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
    </svg>
  ),
  chevronDown: (
    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M19 9l-7 7-7-7" />
    </svg>
  ),
  calendar: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M8 7V3m8 4V3m-9 8h10M5 21h14a2 2 0 002-2V7a2 2 0 00-2-2H5a2 2 0 00-2 2v12a2 2 0 002 2z" />
    </svg>
  ),
  knowledge: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M12 6.253v13m0-13C10.832 5.477 9.246 5 7.5 5S4.168 5.477 3 6.253v13C4.168 18.477 5.754 18 7.5 18s3.332.477 4.5 1.253m0-13C13.168 5.477 14.754 5 16.5 5c1.747 0 3.332.477 4.5 1.253v13C19.832 18.477 18.247 18 16.5 18c-1.746 0-3.332.477-4.5 1.253" />
    </svg>
  ),
  chat: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M8 12h.01M12 12h.01M16 12h.01M21 12c0 4.418-4.03 8-9 8a9.863 9.863 0 01-4.255-.949L3 20l1.395-3.72C3.512 15.042 3 13.574 3 12c0-4.418 4.03-8 9-8s9 3.582 9 8z" />
    </svg>
  ),
  wechat: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M7.5 8.25h.008v.008H7.5V8.25zm0 0H7.5M6 12h.008v.008H6V12zm0 0H6m1.5 7.5c4.97 0 9-3.358 9-7.5 0-.123-.004-.246-.012-.367A4.5 4.5 0 0018 9.5c0 3.866-4.03 7-9 7-.46 0-.912-.028-1.354-.082A5.345 5.345 0 007.5 19.5z" />
      <path strokeLinecap="round" strokeLinejoin="round" d="M15 8.25c0-2.485-2.91-4.5-6.5-4.5S2 5.765 2 8.25c0 1.32.675 2.51 1.74 3.375L3 14l2.5-1.25c.84.234 1.74.375 2.5.375" />
    </svg>
  ),
  whatsapp: (
    <svg className="w-5 h-5" viewBox="0 0 24 24" fill="currentColor" xmlns="http://www.w3.org/2000/svg">
      <path d="M17.472 14.382c-.297-.149-1.758-.867-2.03-.967-.273-.099-.471-.148-.67.15-.197.297-.767.966-.94 1.164-.173.199-.347.223-.644.075-.297-.15-1.255-.463-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.298-.347.446-.52.149-.174.198-.298.298-.497.099-.198.05-.371-.025-.52-.075-.149-.669-1.612-.916-2.207-.242-.579-.487-.5-.669-.51-.173-.008-.371-.01-.57-.01-.198 0-.52.074-.792.372-.272.297-1.04 1.016-1.04 2.479 0 1.462 1.065 2.875 1.213 3.074.149.198 2.096 3.2 5.077 4.487.709.306 1.262.489 1.694.625.712.227 1.36.195 1.871.118.571-.085 1.758-.719 2.006-1.413.248-.694.248-1.289.173-1.413-.074-.124-.272-.198-.57-.347m-5.421 7.403h-.004a9.87 9.87 0 01-5.031-1.378l-.361-.214-3.741.982.998-3.648-.235-.374a9.86 9.86 0 01-1.51-5.26c.001-5.45 4.436-9.884 9.888-9.884 2.64 0 5.122 1.03 6.988 2.898a9.825 9.825 0 012.893 6.994c-.003 5.45-4.437 9.884-9.885 9.884m8.413-18.297A11.815 11.815 0 0012.05 0C5.495 0 .16 5.335.157 11.892c0 2.096.547 4.142 1.588 5.945L.057 24l6.305-1.654a11.882 11.882 0 005.683 1.448h.005c6.554 0 11.89-5.335 11.893-11.893a11.821 11.821 0 00-3.48-8.413Z" />
    </svg>
  ),
  dingding: (
    <svg className="w-5 h-5" viewBox="0 0 24 24" fill="currentColor" xmlns="http://www.w3.org/2000/svg">
      <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm4.18 3.06l-4.2 6.3h3.06c.32 0 .52.36.34.64l-5.34 8.06c-.2.3-.66.12-.58-.22l1.16-4.92H7.6c-.32 0-.52-.34-.36-.62l5.3-8.9c.1-.16.28-.28.48-.28h2.76c.34 0 .54.38.4.66l-.78 1.28h2.78z" />
    </svg>
  ),
  feishu: (
    <svg className="w-5 h-5" viewBox="0 0 24 24" fill="currentColor" xmlns="http://www.w3.org/2000/svg">
      <path d="M3 9.5c0-.4.24-.76.61-.92l11-4.5a1 1 0 011.39.92v3.5c0 .4-.24.76-.61.92l-3.39 1.39 3.39 1.39c.37.16.61.52.61.92v3.5a1 1 0 01-1.39.92l-11-4.5A1 1 0 013 9.5z" />
      <path d="M21 14.5a1 1 0 01-.61.92l-5 2.05A1 1 0 0114 16.55V13a1 1 0 011.39-.92l5 2.05c.37.16.61.52.61.92z" />
    </svg>
  ),
  messenger: (
    <svg className="w-5 h-5" viewBox="0 0 24 24" fill="currentColor" xmlns="http://www.w3.org/2000/svg">
      <path d="M12 2C6.49 2 2 6.15 2 11.24c0 2.93 1.4 5.55 3.6 7.29.19.15.3.38.3.62l.09 2.38c.03.7.77 1.15 1.4.84l2.65-1.32c.2-.1.43-.12.65-.06.94.26 1.93.4 2.91.4 5.51 0 10-4.15 10-9.24S17.51 2 12 2zm5.76 7.95l-2.75 4.27c-.39.61-1.26.71-1.78.21l-2.14-2.14c-.29-.29-.75-.33-1.08-.1l-3.77 2.63c-.36.25-.83-.17-.58-.53l2.75-4.27c.39-.61 1.26-.71 1.78-.21l2.14 2.14c.29.29.75.33 1.08.1l3.77-2.63c.36-.25.83.17.58.53z" />
    </svg>
  ),
  telegram: (
    <svg className="w-5 h-5" viewBox="0 0 24 24" fill="currentColor" xmlns="http://www.w3.org/2000/svg">
      <path d="M9.78 18.65l.28-4.23 7.68-6.92c.34-.31-.07-.46-.52-.19L7.74 13.17 3.64 11.75c-.85-.27-.86-.86.2-1.3l15.97-6.16c.71-.26 1.38.19 1.12 1.3l-2.63 12.28c-.17.81-.67 1-1.32.65l-3.67-2.71-1.8 1.76c-.2.2-.37.36-.73.36z" />
    </svg>
  ),
  line: (
    <svg className="w-5 h-5" viewBox="0 0 24 24" fill="currentColor" xmlns="http://www.w3.org/2000/svg">
      <path d="M12 2.5c-5.25 0-9.5 3.36-9.5 7.5 0 3.69 3.05 6.8 7.13 7.39.33.07.77.22 1.05.5.14.13.27.29.27.46 0 .16-.1.31-.23.43-.13.12-.35.2-.53.26-.28.09-.51.16-.51.45 0 .24.21.41.5.41 1.06 0 2.49-.21 3.51-.48.98-.26 1.86-.73 1.86-1.86 0-2.79-3.86-5.06-8.62-5.06-.44 0-.86.02-1.27.06C5.7 11.13 7.2 9.9 9.06 9.34c1.34-.4 2.78-.4 4.12 0 1.86.56 3.36 1.79 3.94 3.28.41-.04.83-.06 1.27-.06h.11c.53 0 1.02.05 1.5.14-.6-3.66-4.32-6.34-9.5-6.34z" />
    </svg>
  ),
  imessage: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8} xmlns="http://www.w3.org/2000/svg">
      <path strokeLinecap="round" strokeLinejoin="round" d="M8.63 19.5A10.3 10.3 0 0112 20c5.523 0 10-3.806 10-8.5S17.523 3 12 3 2 6.806 2 11.5c0 1.827.77 3.5 2.099 4.84-.167.97-.573 1.96-1.299 2.66 1.31.04 2.53-.31 3.44-.84.79.65 1.72 1.17 2.39 1.34z" />
      <circle cx="8.75" cy="11.5" r=".75" fill="currentColor" stroke="none" />
      <circle cx="12" cy="11.5" r=".75" fill="currentColor" stroke="none" />
      <circle cx="15.25" cy="11.5" r=".75" fill="currentColor" stroke="none" />
    </svg>
  ),
  customReports: (
    <svg className="w-5 h-5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
      <path strokeLinecap="round" strokeLinejoin="round" d="M3 13.125C3 12.504 3.504 12 4.125 12h2.25c.621 0 1.125.504 1.125 1.125v6.75C7.5 20.496 6.996 21 6.375 21h-2.25A1.125 1.125 0 013 19.875v-6.75zM9.75 8.625c0-.621.504-1.125 1.125-1.125h2.25c.621 0 1.125.504 1.125 1.125v11.25c0 .621-.504 1.125-1.125 1.125h-2.25a1.125 1.125 0 01-1.125-1.125V8.625zM16.5 4.125c0-.621.504-1.125 1.125-1.125h2.25C20.496 3 21 3.504 21 4.125v15.75c0 .621-.504 1.125-1.125 1.125h-2.25a1.125 1.125 0 01-1.125-1.125V4.125z" />
    </svg>
  ),
};

const navEntries: NavEntry[] = [
  {
    type: 'item',
    item: {
      to: '/',
      label: 'nav.dashboard',
      icon: Icon.dashboard,
    },
  },
  {
    type: 'group',
    group: {
      key: 'helpdesk',
      label: 'nav.group.helpdesk',
      icon: Icon.helpdesk,
      children: [
        { to: '/conversations', label: 'nav.conversations', icon: Icon.helpdesk },
        { to: '/customers', label: 'nav.customers', icon: Icon.customer },
        { to: '/tags', label: 'nav.tags', icon: Icon.tag },
        { to: '/satisfaction', label: 'nav.satisfaction', icon: Icon.smile },
      ],
    },
  },
  {
    type: 'group',
    group: {
      key: 'mail',
      label: 'nav.group.mail',
      icon: Icon.mail,
      children: [
        { to: '/mailboxes', label: 'nav.mailboxes', icon: Icon.mail },
        { to: '/folders', label: 'nav.folders', icon: Icon.folder },
        { to: '/mail-templates', label: 'nav.mailTemplates', icon: Icon.fileText },
        { to: '/email-signatures', label: 'nav.emailSignatures', icon: Icon.signature },
        { to: '/canned-responses', label: 'nav.cannedResponses', icon: Icon.fileText },
      ],
    },
  },
  {
    type: 'group',
    group: {
      key: 'team',
      label: 'nav.group.team',
      icon: Icon.users,
      children: [
        { to: '/users', label: 'nav.users', icon: Icon.users },
        { to: '/teams', label: 'nav.teams', icon: Icon.usersOutline },
        { to: '/roles', label: 'nav.roles', icon: Icon.shield },
      ],
    },
  },
  {
    type: 'group',
    group: {
      key: 'automation',
      label: 'nav.group.automation',
      icon: Icon.zap,
      children: [
        { to: '/workflows', label: 'nav.workflows', icon: Icon.zap },
        { to: '/sla', label: 'nav.sla', icon: Icon.clock },
        { to: '/macros', label: 'nav.macros', icon: Icon.settings2 },
      ],
    },
  },
  {
    type: 'group',
    group: {
      key: 'notification',
      label: 'nav.group.notification',
      icon: Icon.bell,
      children: [
        { to: '/notifications', label: 'nav.notifications', icon: Icon.bell },
        { to: '/notification-templates', label: 'nav.notificationTemplates', icon: Icon.fileText },
        { to: '/notification-rules', label: 'nav.notificationRules', icon: Icon.edit },
      ],
    },
  },
  {
    type: 'group',
    group: {
      key: 'integration',
      label: 'nav.group.integration',
      icon: Icon.link,
      children: [
        { to: '/webhooks', label: 'nav.webhooks', icon: Icon.link },
        { to: '/api-keys', label: 'nav.apiKeys', icon: Icon.key },
        { to: '/custom-fields', label: 'nav.customFields', icon: Icon.edit },
        { to: '/modules', label: 'nav.modules', icon: Icon.boxes },
      ],
    },
  },
  {
    type: 'group',
    group: {
      key: 'data',
      label: 'nav.group.data',
      icon: Icon.chart,
      children: [
        { to: '/reports', label: 'nav.reports', icon: Icon.chart },
        { to: '/modules/custom-reports', label: 'nav.customReports', icon: Icon.customReports },
        { to: '/import-export', label: 'nav.importExport', icon: Icon.arrowUpDown },
      ],
    },
  },
  {
    type: 'group',
    group: {
      key: 'features',
      label: 'nav.group.features',
      icon: Icon.boxes,
      children: [
        { to: '/calendar', label: 'nav.calendar', icon: Icon.calendar },
        { to: '/knowledge', label: 'nav.knowledge', icon: Icon.knowledge },
        { to: '/time-tracking', label: 'nav.timeTracking', icon: Icon.clock },
        { to: '/chat', label: 'nav.chat', icon: Icon.chat },
        { to: '/wechat', label: 'nav.wechat', icon: Icon.wechat },
        { to: '/dingding', label: 'nav.dingding', icon: Icon.dingding },
        { to: '/feishu', label: 'nav.feishu', icon: Icon.feishu },
        { to: '/whatsapp', label: 'nav.whatsapp', icon: Icon.whatsapp },
        { to: '/messenger', label: 'nav.messenger', icon: Icon.messenger },
        { to: '/telegram', label: 'nav.telegram', icon: Icon.telegram },
        { to: '/line', label: 'nav.line', icon: Icon.line },
        { to: '/imessage', label: 'nav.imessage', icon: Icon.imessage },
      ],
    },
  },
  {
    type: 'group',
    group: {
      key: 'system',
      label: 'nav.group.system',
      icon: Icon.settings,
      children: [
        { to: '/audit-logs', label: 'nav.auditLogs', icon: Icon.fileBar },
        { to: '/send-logs', label: 'nav.sendLogs', icon: Icon.send },
        { to: '/system-tools', label: 'nav.systemTools', icon: Icon.tools },
        { to: '/localization', label: 'nav.localization', icon: Icon.globe },
        { to: '/settings', label: 'nav.settings', icon: Icon.settings },
      ],
    },
  },
];

// 语言切换器
function LanguageSwitcher() {
  const { t } = useI18n();
  // 切换语义统一收敛在 hook 里：乐观切换 → 完整 payload 写回资料 → 失败回滚
  const { language, saving, switchLanguage } = useLanguageSwitch();

  return (
    <div className="relative">
      <select
        value={language}
        onChange={(e) => void switchLanguage(e.target.value)}
        // 写回在途时禁用：否则第二次点击会被静默丢弃（用户会以为"点了没反应"）
        disabled={saving}
        className="appearance-none pl-7 pr-7 py-1.5 text-sm border border-gray-300 dark:border-slate-600 bg-white dark:bg-slate-800 dark:text-slate-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-primary-500 focus:border-primary-500 transition cursor-pointer disabled:opacity-60 disabled:cursor-not-allowed"
        title={t('common.switchLanguage')}
      >
        {LANGUAGE_OPTIONS.map((option) => (
          <option key={option.value} value={option.value}>
            {option.label}
          </option>
        ))}
      </select>
      <svg
        className="w-4 h-4 absolute left-1.5 top-1/2 -translate-y-1/2 text-gray-400 dark:text-slate-500 pointer-events-none"
        fill="none"
        viewBox="0 0 24 24"
        stroke="currentColor"
        strokeWidth={1.8}
      >
        <path strokeLinecap="round" strokeLinejoin="round" d="M3.055 11H5a2 2 0 012 2v1a2 2 0 002 2 2 2 0 012 2v2.945M8 3.935V5.5A2.5 2.5 0 0010.5 8h.5a2 2 0 012 2 2 2 0 104 0 2 2 0 012-2h1.064M15 20.488V18a2 2 0 012-2h3.064M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
      </svg>
      <svg
        className="w-3 h-3 absolute right-2 top-1/2 -translate-y-1/2 text-gray-400 dark:text-slate-500 pointer-events-none"
        fill="none"
        viewBox="0 0 24 24"
        stroke="currentColor"
        strokeWidth={2}
      >
        <path strokeLinecap="round" strokeLinejoin="round" d="M19 9l-7 7-7-7" />
      </svg>
    </div>
  );
}

export default function Layout({ children }: { children: ReactNode }) {
  const { user, logout } = useAuth();
  const { modules, activeModuleNames } = useModules();
  // 头像直接来自后端 UserResponse（登录 / profile 接口返回），无需 localStorage 缓存
  const avatar = user?.avatar ?? null;
  const { canAccessGroup, canAccessItem } = usePermission();
  // 用户菜单中各入口的权限判断
  const canViewNotifications = canAccessItem('/notifications');
  const canViewEmailSignatures = canAccessItem('/email-signatures');
  const { t } = useI18n();
  const navigate = useNavigate();
  const location = useLocation();
  const toast = useToast();
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [searchOpen, setSearchOpen] = useState(false);
  const [userMenuOpen, setUserMenuOpen] = useState(false);
  const userMenuRef = useRef<HTMLDivElement>(null);

  // 全局监听 @提及 事件:显示 Toast 并允许点击跳转到对应会话
  useSignalREvent<unknown>(SignalREvents.ChatMention, (data) => {
    if (!data || typeof data !== 'object') return;
    const payload = data as {
      sessionId?: number;
      mentionedUserName?: string;
      senderName?: string;
      preview?: string;
    };
    const by = payload.senderName || t('common.layout.colleagueFallback');
    const preview = payload.preview ? (payload.preview.length > 40 ? payload.preview.substring(0, 40) + '...' : payload.preview) : '';
    const msg = payload.sessionId
      ? preview
        ? t('common.layout.mentionWithPreview', { name: by, preview })
        : t('common.layout.mentionInChat', { name: by })
      : t('common.layout.mentionedYou', { name: by });
    // 点击 Toast 跳转聊天页(会话页会自动选中最新的那个)
    const onClick = payload.sessionId
      ? () => {
          navigate(`/chat?highlightSession=${payload.sessionId}`);
        }
      : undefined;
    toast.info(msg, { onClick });
  });

  const allModuleNames = useMemo(
    () => new Set(modules.map((m) => m.name)),
    [modules]
  );

  const visibleNavEntries = useMemo(() => {
    return navEntries
      .map((entry) => {
        if (entry.type === 'item') {
          if (entry.item.to === '/') return entry;
          if (!isPathModuleActive(entry.item.to, activeModuleNames, allModuleNames)) return null;
          if (!canAccessItem(entry.item.to)) return null;
          return entry;
        }
        if (!canAccessGroup(entry.group.key)) return null;
        const visibleChildren = entry.group.children.filter((c) =>
          isPathModuleActive(c.to, activeModuleNames, allModuleNames) && canAccessItem(c.to)
        );
        if (visibleChildren.length === 0) return null;
        return {
          type: 'group' as const,
          group: { ...entry.group, children: visibleChildren },
        };
      })
      .filter((e): e is NavEntry => e !== null);
  }, [activeModuleNames, allModuleNames, canAccessGroup, canAccessItem]);

  // 计算当前路径所属的活动分组（最长匹配优先，避免 /modules 误匹配 /modules/custom-reports）
  const activeGroupKey = useMemo(() => {
    let longestKey: string | null = null;
    let longestLen = 0;
    for (const entry of visibleNavEntries) {
      if (entry.type === 'group') {
        for (const c of entry.group.children) {
          const matches = location.pathname === c.to || (c.to !== '/' && location.pathname.startsWith(c.to));
          if (matches && c.to.length > longestLen) {
            longestKey = entry.group.key;
            longestLen = c.to.length;
          }
        }
      }
    }
    return longestKey;
  }, [location.pathname, visibleNavEntries]);

  const [expandedGroups, setExpandedGroups] = useState<Record<string, boolean>>(() => {
    return activeGroupKey ? { [activeGroupKey]: true } : {};
  });

  useEffect(() => {
    if (activeGroupKey) {
      // 手风琴行为：路由变化时只展开当前活动组，收起其他组
      setExpandedGroups({ [activeGroupKey]: true });
    }
  }, [activeGroupKey]);

  const toggleGroup = (key: string) => {
    setExpandedGroups((prev) => {
      // 手风琴行为：点击已展开的组则收起；点击收起的组则先收起其他组再展开此组
      if (prev[key]) return { [key]: false };
      return { [key]: true };
    });
  };

  const handleLogout = async () => {
    // 必须 await 后端登出完成后再跳转，避免在请求未完成时已跳转导致：
    // 1. 服务端 Cookie 未失效（XSRF-TOKEN 仍可用）
    // 2. React Query 缓存中残留旧数据被新会话用户访问
    // 3. 用户感知的"立即登出"与服务端实际状态不一致
    try {
      await logout();
    } catch {
      // 登出失败时仍然跳转，由 AuthContext 内部已清理本地状态
    } finally {
      navigate('/login');
    }
  };

  // Ctrl+K / Cmd+K 唤起全局搜索
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        setSearchOpen((v) => !v);
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, []);

  // 点击外部关闭用户菜单
  useEffect(() => {
    const handleClickOutside = (e: MouseEvent) => {
      if (userMenuRef.current && !userMenuRef.current.contains(e.target as Node)) {
        setUserMenuOpen(false);
      }
    };
    if (userMenuOpen) {
      document.addEventListener('mousedown', handleClickOutside);
    }
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, [userMenuOpen]);

  return (
    <div className="min-h-screen bg-gray-50 dark:bg-slate-900 flex">
      {/* 侧边栏 */}
      <aside
        className={`fixed lg:static inset-y-0 left-0 z-30 w-64 bg-white dark:bg-slate-800 border-r border-gray-200 dark:border-slate-700 text-gray-900 dark:text-slate-100 transform ${
          sidebarOpen ? 'translate-x-0' : '-translate-x-full'
        } lg:translate-x-0 transition-transform duration-200 ease-in-out flex flex-col`}
      >
        <div className="h-16 flex items-center gap-3 px-5 border-b border-gray-200 dark:border-slate-700">
          <img src="/favicon.svg" alt="NoneadCSS Logo" className="w-9 h-9" />
          <div>
            <div className="font-semibold text-base">Nonead CSS</div>
            <div className="text-xs text-gray-500 dark:text-slate-400">{t('common.layout.adminConsole')}</div>
          </div>
        </div>

        <nav className="flex-1 py-4 px-3 space-y-1 overflow-y-auto">
          {visibleNavEntries.map((entry) => {
            if (entry.type === 'item') {
              return (
                <NavLink
                  key={entry.item.to}
                  to={entry.item.to}
                  end={entry.item.to === '/'}
                  onClick={() => setSidebarOpen(false)}
                  className={({ isActive }) =>
                    `flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition ${
                      isActive
                        ? 'bg-primary-600 text-white shadow-sm'
                        : 'text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700 hover:text-gray-900 dark:hover:text-white'
                    }`
                  }
                >
                  {entry.item.icon}
                  <span>{t(entry.item.label)}</span>
                </NavLink>
              );
            }

            const { group } = entry;
            const expanded = expandedGroups[group.key] ?? false;
            const hasActive = activeGroupKey === group.key;

            return (
              <div key={group.key}>
                <button
                  onClick={() => toggleGroup(group.key)}
                  className={`w-full flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm font-medium transition ${
                    hasActive
                      ? 'text-gray-900 dark:text-white bg-gray-100 dark:bg-slate-700/60'
                      : 'text-gray-600 dark:text-slate-300 hover:bg-gray-100 dark:hover:bg-slate-700 hover:text-gray-900 dark:hover:text-white'
                  }`}
                >
                  {group.icon}
                  <span className="flex-1 text-left">{t(group.label)}</span>
                  <span
                    className={`transform transition-transform duration-200 ${expanded ? 'rotate-180' : ''}`}
                  >
                    {Icon.chevronDown}
                  </span>
                </button>
                <div
                  // 折叠动画的 max-height 必须按子项数量算：固定 max-h-96（384px）在子项超过 9 个时
                  // 会把末尾的菜单项裁掉且无法滚动（新增 Messenger/Telegram/LINE/iMessage 后
                  // 「功能模块」分组已到 12 项，末两项就一直看不见）。这里改为按条数推算，
                  // 每条按 2.75rem（44px）留足 py-2 + text-sm 的高度，另加 1rem 余量。
                  className={`overflow-hidden transition-all duration-200 ease-in-out ${
                    expanded ? 'mt-1' : ''
                  }`}
                  style={{ maxHeight: expanded ? `${group.children.length * 2.75 + 1}rem` : 0 }}
                >
                  <div className="pl-3 space-y-1">
                    {group.children.map((child) => (
                      <NavLink
                        key={child.to}
                        to={child.to}
                        end={child.to === '/'}
                        onClick={() => setSidebarOpen(false)}
                        className={({ isActive }) =>
                          `flex items-center gap-3 px-3 py-2 rounded-lg text-sm font-medium transition border-l-2 ${
                            isActive
                              ? 'bg-primary-50 dark:bg-primary-600/20 text-primary-700 dark:text-primary-300 border-primary-500 dark:border-primary-400'
                              : 'text-gray-500 dark:text-slate-400 hover:bg-gray-50 dark:hover:bg-slate-700/60 hover:text-gray-700 dark:hover:text-slate-200 border-transparent'
                          }`
                        }
                      >
                        {child.icon}
                        <span>{t(child.label)}</span>
                      </NavLink>
                    ))}
                  </div>
                </div>
              </div>
            );
          })}
        </nav>

        <div className="p-3 border-t border-gray-200 dark:border-slate-700">
          { /* H-07：移除 5009 端口硬编码。生产/容器化部署时前后端同域，
                 使用相对路径 /docs 即可访问 Scalar API 文档，
                 避免开发端口、发布端口、反向代理端口与代码不一致导致链接 404。 */ }
          <a
            href="/docs"
            target="_blank"
            rel="noopener noreferrer"
            className="flex items-center gap-2 px-3 py-2 text-xs text-gray-500 dark:text-slate-400 hover:text-gray-700 dark:hover:text-slate-200 transition"
          >
            <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
              <path strokeLinecap="round" strokeLinejoin="round" d="M10 6H6a2 2 0 00-2 2v10a2 2 0 002 2h10a2 2 0 002-2v-4M14 4h6m0 0v6m0-6L10 14" />
            </svg>
            {t('common.layout.apiDocs')}
          </a>
        </div>
      </aside>

      {/* 移动端遮罩 */}
      {sidebarOpen && (
        <div
          className="fixed inset-0 z-20 bg-black/50 lg:hidden"
          onClick={() => setSidebarOpen(false)}
        />
      )}

      {/* 主区域 */}
      <div className="flex-1 flex flex-col min-w-0">
        {/* 顶部栏 */}
        <header className="h-16 bg-white dark:bg-slate-800 border-b border-gray-200 dark:border-slate-700 flex items-center justify-between px-4 lg:px-6 sticky top-0 z-40">
          <button
            onClick={() => setSidebarOpen(!sidebarOpen)}
            aria-label={t('common.layout.openMenu')}
            className="lg:hidden p-2 -ml-2 text-gray-600 dark:text-slate-300 hover:text-gray-900 dark:hover:text-white"
          >
            <svg className="w-6 h-6" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2} aria-hidden="true">
              <path strokeLinecap="round" strokeLinejoin="round" d="M4 6h16M4 12h16M4 18h16" />
            </svg>
          </button>

          <NavLink to="/" className="flex items-center mr-3">
            <img src="/logo.svg" alt="Logo" className="h-10 w-auto" />
          </NavLink>

          <div className="flex-1 lg:flex-none">
            <button
              onClick={() => setSearchOpen(true)}
              aria-label={t('common.search')}
              className="flex items-center gap-2.5 px-3 py-2 text-sm text-gray-500 dark:text-slate-400 bg-gray-100 dark:bg-slate-700 hover:bg-gray-200 dark:hover:bg-slate-600 rounded-lg transition w-full sm:w-64"
            >
              <svg className="w-4 h-4 flex-shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2} aria-hidden="true">
                <path strokeLinecap="round" strokeLinejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
              </svg>
              <span className="flex-1 text-left">{t('common.layout.searchPlaceholder')}</span>
              <kbd className="hidden sm:inline-flex items-center px-1.5 py-0.5 text-xs text-gray-500 dark:text-slate-400 bg-white dark:bg-slate-800 border border-gray-300 dark:border-slate-600 rounded">
                Ctrl K
              </kbd>
            </button>
          </div>

          <NotificationBell />

          {/* 语言切换器 */}
          <LanguageSwitcher />

          <div className="flex items-center gap-3">
            <div className="relative" ref={userMenuRef}>
              <button
                onClick={() => setUserMenuOpen((v) => !v)}
                aria-label={t('common.layout.userMenu')}
                className="flex items-center gap-2.5 p-1.5 -m-1.5 rounded-lg hover:bg-gray-100 dark:hover:bg-slate-700 transition"
              >
                <div className="w-8 h-8">
                  <UserAvatar
                    userId={user?.id}
                    name={user?.fullName}
                    size="sm"
                    avatarUrl={avatar}
                  />
                </div>
                <div className="hidden sm:block text-left">
                  <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{user?.fullName || t('common.layout.unknownUser')}</div>
                  <div className="text-xs text-gray-500 dark:text-slate-400">{user?.email}</div>
                </div>
                <svg className="w-4 h-4 text-gray-400 dark:text-slate-500 hidden sm:block" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                  <path strokeLinecap="round" strokeLinejoin="round" d="M19 9l-7 7-7-7" />
                </svg>
              </button>

              {userMenuOpen && (
                <div className="absolute right-0 top-full mt-2 w-56 bg-white dark:bg-slate-800 rounded-xl shadow-lg border border-gray-100 dark:border-slate-700 py-1.5 z-50">
                  <div className="px-3 py-2.5 border-b border-gray-100 dark:border-slate-700">
                    <div className="text-sm font-medium text-gray-900 dark:text-slate-100">{user?.fullName || t('common.layout.unknownUser')}</div>
                    <div className="text-xs text-gray-500 dark:text-slate-400 truncate">{user?.email}</div>
                  </div>
                  <button
                    onClick={() => {
                      setUserMenuOpen(false);
                      navigate('/profile');
                    }}
                    className="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-gray-700 dark:text-slate-300 hover:bg-gray-50 dark:hover:bg-slate-700 transition text-left"
                  >
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M16 7a4 4 0 11-8 0 4 4 0 018 0zM12 14a7 7 0 00-7 7h14a7 7 0 00-7-7z" />
                    </svg>
                    {t('common.layout.profile')}
                  </button>
                  {canViewNotifications && (
                    <button
                      onClick={() => {
                        setUserMenuOpen(false);
                        navigate('/notifications');
                      }}
                      className="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-gray-700 dark:text-slate-300 hover:bg-gray-50 dark:hover:bg-slate-700 transition text-left"
                    >
                      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                        <path strokeLinecap="round" strokeLinejoin="round" d="M15 17h5l-1.405-1.405A2.032 2.032 0 0118 14.158V11a6.002 6.002 0 00-4-5.659V5a2 2 0 10-4 0v.341C7.67 6.165 6 8.388 6 11v3.159c0 .538-.214 1.055-.595 1.436L4 17h5m6 0v1a3 3 0 11-6 0v-1m6 0H9" />
                      </svg>
                      {t('common.layout.notificationCenter')}
                    </button>
                  )}
                  {canViewEmailSignatures && (
                    <button
                      onClick={() => {
                        setUserMenuOpen(false);
                        navigate('/email-signatures');
                      }}
                      className="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-gray-700 dark:text-slate-300 hover:bg-gray-50 dark:hover:bg-slate-700 transition text-left"
                    >
                      <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                        <path strokeLinecap="round" strokeLinejoin="round" d="M16 12a4 4 0 10-8 0 4 4 0 008 0zm0 0v1.5a2.5 2.5 0 005 0V12a9 9 0 10-9 9m4.5-1.206a8.959 8.959 0 01-4.5 1.207" />
                      </svg>
                      {t('common.layout.emailSignatures')}
                    </button>
                  )}
                  <div className="border-t border-gray-100 dark:border-slate-700 my-1"></div>
                  <button
                    onClick={() => {
                      setUserMenuOpen(false);
                      handleLogout();
                    }}
                    className="w-full flex items-center gap-2.5 px-3 py-2 text-sm text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/20 transition text-left"
                  >
                    <svg className="w-4 h-4" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8}>
                      <path strokeLinecap="round" strokeLinejoin="round" d="M17 16l4-4m0 0l-4-4m4 4H7m6 4v1a3 3 0 01-3 3H6a3 3 0 01-3-3V7a3 3 0 013-3h4a3 3 0 013 3v1" />
                    </svg>
                    {t('common.layout.logout')}
                  </button>
                </div>
              )}
            </div>
          </div>
        </header>

        <main className="flex-1 p-4 lg:p-6 overflow-x-auto">
          {children}
        </main>
      </div>

      {/* 全局搜索 */}
      <GlobalSearch open={searchOpen} onClose={() => setSearchOpen(false)} />
    </div>
  );
}
