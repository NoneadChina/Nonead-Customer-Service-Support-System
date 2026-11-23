/**
 * 来源渠道图标组件
 * 来源类型：1=邮件、2=网页、3=API、4=电话、5=聊天、6=社交、7=微信、8=WhatsApp、9=飞书、10=钉钉
 * - 邮件(1)：信封图标
 * - 网页(2)：地球图标
 * - API(3)：代码图标
 * - 电话(4)：电话图标
 * - 聊天(5)：气泡图标
 * - 社交(6)：分享图标
 * - 微信(7)：微信官方 logo
 * - WhatsApp(8)：WhatsApp 官方 logo
 * - 飞书(9)：飞书风格图标
 * - 钉钉(10)：钉钉风格图标
   * - Messenger(11)/Telegram(12)/LINE(13)/iMessage(14)：与侧边栏导航图标同一套 SVG
 * 使用 currentColor 以便继承父元素的文字颜色。
 */
interface Props {
  sourceType: number | undefined;
  className?: string;
  title?: string;
}

export default function SourceTypeIcon({ sourceType, className = 'w-3 h-3', title }: Props) {
  const role = title ? 'img' : undefined;
  const ariaLabel = title;

  if (sourceType == null) return null;

  if (sourceType === 7) {
    // 微信官方 logo
    return (
      <svg
        className={className}
        viewBox="0 0 24 24"
        fill="currentColor"
        role={role}
        aria-label={ariaLabel}
        xmlns="http://www.w3.org/2000/svg"
      >
        <path d="M8.691 2.188C3.891 2.188 0 5.476 0 9.53c0 2.212 1.17 4.203 3.002 5.55a.59.59 0 01.213.665l-.39 1.48c-.019.07-.048.141-.048.213 0 .163.13.295.29.295a.326.326 0 00.167-.054l1.903-1.114a.864.864 0 01.717-.098 10.16 10.16 0 002.837.403c.276 0 .543-.027.81-.05-.857-2.578.157-4.972 1.616-6.546 1.24-1.24 3.04-2.084 5.082-2.084.295 0 .584.018.87.05C17.061 4.744 13.258 2.188 8.691 2.188zM5.785 5.991c.642 0 1.162.529 1.162 1.18a1.17 1.17 0 01-1.162 1.178A1.17 1.17 0 014.623 7.17c0-.651.52-1.18 1.162-1.18zm5.813 0c.642 0 1.162.529 1.162 1.18a1.17 1.17 0 01-1.162 1.178 1.17 1.17 0 01-1.162-1.178c0-.651.52-1.18 1.162-1.18z" />
        <path d="M24 14.382c0-3.376-3.297-6.118-7.366-6.118-4.069 0-7.366 2.742-7.366 6.118 0 3.376 3.297 6.118 7.366 6.118.853 0 1.671-.118 2.43-.337a.72.72 0 01.597.082l1.586.926a.272.272 0 00.14.045c.133 0 .241-.108.241-.245 0-.06-.024-.119-.04-.178l-.325-1.232a.493.493 0 01.178-.554C22.961 17.882 24 16.224 24 14.382zm-9.747-1.382c.535 0 .969.44.969.982a.976.976 0 01-.969.983.976.976 0 01-.969-.983c0-.542.434-.982.969-.982zm4.844 0c.535 0 .969.44.969.982a.976.976 0 01-.969.983.976.976 0 01-.969-.983c0-.542.434-.982.969-.982z" />
      </svg>
    );
  }

  if (sourceType === 8) {
    // WhatsApp 官方 logo
    return (
      <svg
        className={className}
        viewBox="0 0 24 24"
        fill="currentColor"
        role={role}
        aria-label={ariaLabel}
        xmlns="http://www.w3.org/2000/svg"
      >
        <path d="M17.472 14.382c-.297-.149-1.758-.867-2.03-.967-.273-.099-.471-.148-.67.15-.197.297-.767.966-.94 1.164-.173.199-.347.223-.644.075-.297-.15-1.255-.463-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.298-.347.446-.52.149-.174.198-.298.298-.497.099-.198.05-.371-.025-.52-.075-.149-.669-1.612-.916-2.207-.242-.579-.487-.5-.669-.51-.173-.008-.371-.01-.57-.01-.198 0-.52.074-.792.372-.272.297-1.04 1.016-1.04 2.479 0 1.462 1.065 2.875 1.213 3.074.149.198 2.096 3.2 5.077 4.487.709.306 1.262.489 1.694.625.712.227 1.36.195 1.871.118.571-.085 1.758-.719 2.006-1.413.248-.694.248-1.289.173-1.413-.074-.124-.272-.198-.57-.347m-5.421 7.403h-.004a9.87 9.87 0 01-5.031-1.378l-.361-.214-3.741.982.998-3.648-.235-.374a9.86 9.86 0 01-1.51-5.26c.001-5.45 4.436-9.884 9.888-9.884 2.64 0 5.122 1.03 6.988 2.898a9.825 9.825 0 012.893 6.994c-.003 5.45-4.437 9.884-9.885 9.884m8.413-18.297A11.815 11.815 0 0012.05 0C5.495 0 .16 5.335.157 11.892c0 2.096.547 4.142 1.588 5.945L.057 24l6.305-1.654a11.882 11.882 0 005.683 1.448h.005c6.554 0 11.89-5.335 11.893-11.893a11.821 11.821 0 00-3.48-8.413Z" />
      </svg>
    );
  }

  if (sourceType === 9) {
    // 飞书：纸飞机/消息流风格图标（蓝色主题）
    return (
      <svg
        className={className}
        viewBox="0 0 24 24"
        fill="currentColor"
        role={role}
        aria-label={ariaLabel}
        xmlns="http://www.w3.org/2000/svg"
      >
        <path d="M3 9.5c0-.4.24-.76.61-.92l11-4.5a1 1 0 011.39.92v3.5c0 .4-.24.76-.61.92l-3.39 1.39 3.39 1.39c.37.16.61.52.61.92v3.5a1 1 0 01-1.39.92l-11-4.5A1 1 0 013 9.5z" />
        <path d="M21 14.5a1 1 0 01-.61.92l-5 2.05A1 1 0 0114 16.55V13a1 1 0 011.39-.92l5 2.05c.37.16.61.52.61.92z" />
      </svg>
    );
  }

  if (sourceType === 10) {
    // 钉钉官方 logo（蓝色圆形 + 白色闪电蜂鸟）
    return (
      <svg
        className={className}
        viewBox="0 0 24 24"
        fill="currentColor"
        role={role}
        aria-label={ariaLabel}
        xmlns="http://www.w3.org/2000/svg"
      >
        <path d="M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm4.18 3.06l-4.2 6.3h3.06c.32 0 .52.36.34.64l-5.34 8.06c-.2.3-.66.12-.58-.22l1.16-4.92H7.6c-.32 0-.52-.34-.36-.62l5.3-8.9c.1-.16.28-.28.48-.28h2.76c.34 0 .54.38.4.66l-.78 1.28h2.78z" />
      </svg>
    );
  }


  if (sourceType === 11) {
    // Messenger 渠道图标（与侧边栏导航图标同源，避免两套 SVG）
    return (
          <svg
              role={role}
              aria-label={ariaLabel}
            className={className} viewBox="0 0 24 24" fill="currentColor" xmlns="http://www.w3.org/2000/svg">
            <path d="M12 2C6.49 2 2 6.15 2 11.24c0 2.93 1.4 5.55 3.6 7.29.19.15.3.38.3.62l.09 2.38c.03.7.77 1.15 1.4.84l2.65-1.32c.2-.1.43-.12.65-.06.94.26 1.93.4 2.91.4 5.51 0 10-4.15 10-9.24S17.51 2 12 2zm5.76 7.95l-2.75 4.27c-.39.61-1.26.71-1.78.21l-2.14-2.14c-.29-.29-.75-.33-1.08-.1l-3.77 2.63c-.36.25-.83-.17-.58-.53l2.75-4.27c.39-.61 1.26-.71 1.78-.21l2.14 2.14c.29.29.75.33 1.08.1l3.77-2.63c.36-.25.83.17.58.53z" />
          </svg>
    );
  }

  if (sourceType === 12) {
    // Telegram 渠道图标（与侧边栏导航图标同源，避免两套 SVG）
    return (
          <svg
              role={role}
              aria-label={ariaLabel}
            className={className} viewBox="0 0 24 24" fill="currentColor" xmlns="http://www.w3.org/2000/svg">
            <path d="M9.78 18.65l.28-4.23 7.68-6.92c.34-.31-.07-.46-.52-.19L7.74 13.17 3.64 11.75c-.85-.27-.86-.86.2-1.3l15.97-6.16c.71-.26 1.38.19 1.12 1.3l-2.63 12.28c-.17.81-.67 1-1.32.65l-3.67-2.71-1.8 1.76c-.2.2-.37.36-.73.36z" />
          </svg>
    );
  }

  if (sourceType === 13) {
    // LINE 渠道图标（与侧边栏导航图标同源，避免两套 SVG）
    return (
          <svg
              role={role}
              aria-label={ariaLabel}
            className={className} viewBox="0 0 24 24" fill="currentColor" xmlns="http://www.w3.org/2000/svg">
            <path d="M12 2.5c-5.25 0-9.5 3.36-9.5 7.5 0 3.69 3.05 6.8 7.13 7.39.33.07.77.22 1.05.5.14.13.27.29.27.46 0 .16-.1.31-.23.43-.13.12-.35.2-.53.26-.28.09-.51.16-.51.45 0 .24.21.41.5.41 1.06 0 2.49-.21 3.51-.48.98-.26 1.86-.73 1.86-1.86 0-2.79-3.86-5.06-8.62-5.06-.44 0-.86.02-1.27.06C5.7 11.13 7.2 9.9 9.06 9.34c1.34-.4 2.78-.4 4.12 0 1.86.56 3.36 1.79 3.94 3.28.41-.04.83-.06 1.27-.06h.11c.53 0 1.02.05 1.5.14-.6-3.66-4.32-6.34-9.5-6.34z" />
          </svg>
    );
  }

  if (sourceType === 14) {
    // iMessage 渠道图标（与侧边栏导航图标同源，避免两套 SVG）
    return (
          <svg
              role={role}
              aria-label={ariaLabel}
            className={className} fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={1.8} xmlns="http://www.w3.org/2000/svg">
            <path strokeLinecap="round" strokeLinejoin="round" d="M8.63 19.5A10.3 10.3 0 0112 20c5.523 0 10-3.806 10-8.5S17.523 3 12 3 2 6.806 2 11.5c0 1.827.77 3.5 2.099 4.84-.167.97-.573 1.96-1.299 2.66 1.31.04 2.53-.31 3.44-.84.79.65 1.72 1.17 2.39 1.34z" />
            <circle cx="8.75" cy="11.5" r=".75" fill="currentColor" stroke="none" />
            <circle cx="12" cy="11.5" r=".75" fill="currentColor" stroke="none" />
            <circle cx="15.25" cy="11.5" r=".75" fill="currentColor" stroke="none" />
          </svg>
    );
  }

  if (sourceType === 1) {
    // 邮件：信封
    return (
      <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" role={role} aria-label={ariaLabel} xmlns="http://www.w3.org/2000/svg">
        <path d="M4 4h16c1.1 0 2 .9 2 2v12c0 1.1-.9 2-2 2H4c-1.1 0-2-.9-2-2V6c0-1.1.9-2 2-2z" />
        <polyline points="22,6 12,13 2,6" />
      </svg>
    );
  }

  if (sourceType === 2) {
    // 网页：地球
    return (
      <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" role={role} aria-label={ariaLabel} xmlns="http://www.w3.org/2000/svg">
        <circle cx="12" cy="12" r="10" />
        <line x1="2" y1="12" x2="22" y2="12" />
        <path d="M12 2a15.3 15.3 0 014 10 15.3 15.3 0 01-4 10 15.3 15.3 0 01-4-10 15.3 15.3 0 014-10z" />
      </svg>
    );
  }

  if (sourceType === 3) {
    // API：代码括号
    return (
      <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" role={role} aria-label={ariaLabel} xmlns="http://www.w3.org/2000/svg">
        <polyline points="16 18 22 12 16 6" />
        <polyline points="8 6 2 12 8 18" />
      </svg>
    );
  }

  if (sourceType === 4) {
    // 电话
    return (
      <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" role={role} aria-label={ariaLabel} xmlns="http://www.w3.org/2000/svg">
        <path d="M22 16.92v3a2 2 0 01-2.18 2 19.79 19.79 0 01-8.63-3.07 19.5 19.5 0 01-6-6 19.79 19.79 0 01-3.07-8.67A2 2 0 014.11 2h3a2 2 0 012 1.72 12.84 12.84 0 00.7 2.81 2 2 0 01-.45 2.11L8.09 9.91a16 16 0 006 6l1.27-1.27a2 2 0 012.11-.45 12.84 12.84 0 002.81.7A2 2 0 0122 16.92z" />
      </svg>
    );
  }

  if (sourceType === 5) {
    // 聊天：气泡
    return (
      <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" role={role} aria-label={ariaLabel} xmlns="http://www.w3.org/2000/svg">
        <path d="M21 15a2 2 0 01-2 2H7l-4 4V5a2 2 0 012-2h14a2 2 0 012 2z" />
      </svg>
    );
  }

  if (sourceType === 6) {
    // 社交：分享节点
    return (
      <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.8} strokeLinecap="round" strokeLinejoin="round" role={role} aria-label={ariaLabel} xmlns="http://www.w3.org/2000/svg">
        <circle cx="18" cy="5" r="3" />
        <circle cx="6" cy="12" r="3" />
        <circle cx="18" cy="19" r="3" />
        <line x1="8.59" y1="13.51" x2="15.42" y2="17.49" />
        <line x1="15.41" y1="6.51" x2="8.59" y2="10.49" />
      </svg>
    );
  }

  return null;
}
