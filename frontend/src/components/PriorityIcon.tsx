/**
 * 优先级图标组件
 * 优先级：0=Low、1=Normal、2=High、3=Urgent
 * - Low: 向下箭头（↓）
 * - Normal: 不显示图标（默认值，无需强调）
 * - High: 向上箭头（↑）
 * - Urgent: 火焰（🔥）
 */
interface Props {
  priority: number;
  className?: string;
}

export default function PriorityIcon({ priority, className = 'w-3 h-3' }: Props) {
  // 普通优先级不显示图标（默认值，无需强调）
  if (priority === 1) {
    return null;
  }
  // 使用 currentColor 以便继承父元素的文字颜色
  if (priority === 3) {
    // 紧急：火焰图标
    return (
      <svg className={className} viewBox="0 0 24 24" fill="currentColor" xmlns="http://www.w3.org/2000/svg">
        <path d="M12 2c-.5 3-2 4.5-3.5 6C7 9.5 5.5 11 5.5 13.5c0 3.6 2.9 6.5 6.5 6.5s6.5-2.9 6.5-6.5c0-1.7-.7-3.2-1.8-4.3-.2 1-1 1.8-1.7 2.1.3-1.8-.3-4-1.5-5.5-.5-.6-1-1.3-1.5-2.3z" />
      </svg>
    );
  }
  if (priority === 2) {
    // 高：向上箭头
    return (
      <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" xmlns="http://www.w3.org/2000/svg">
        <path d="M12 19V5M5 12l7-7 7 7" />
      </svg>
    );
  }
  if (priority === 0) {
    // 低：向下箭头
    return (
      <svg className={className} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="3" strokeLinecap="round" strokeLinejoin="round" xmlns="http://www.w3.org/2000/svg">
        <path d="M12 5v14M5 12l7 7 7-7" />
      </svg>
    );
  }
  return null;
}

