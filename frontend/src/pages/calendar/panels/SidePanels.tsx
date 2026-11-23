import { TrashPanel } from './TrashPanel';
import { SubscriptionPanel } from './SubscriptionPanel';
import { OutlookSyncPanel } from './OutlookSyncPanel';

/* =========================================================
   SidePanels —— 侧边面板聚合入口
   历史实现已按职责拆分到独立文件：
     - TrashPanel.tsx         垃圾箱（搜索/恢复/永久删除/清空/按期清理）
     - SubscriptionPanel.tsx  日历订阅（iCal 链接复制/重新生成）
     - OutlookSyncPanel.tsx   Outlook 同步（OAuth 连接/方向/开关/立即同步）
   共享组件（Overlay/ModalHeader/ConfirmDialog/Icon/工具函数）抽离到 shared.tsx
   本文件仅作聚合 re-export，保持 `import SidePanels from './panels/SidePanels'`
   的向后兼容入口。
   ========================================================= */

export { TrashPanel } from './TrashPanel';
export { SubscriptionPanel } from './SubscriptionPanel';
export { OutlookSyncPanel } from './OutlookSyncPanel';

export default function SidePanels() {
  return (
    <>
      <TrashPanel />
      <SubscriptionPanel />
      <OutlookSyncPanel />
    </>
  );
}
