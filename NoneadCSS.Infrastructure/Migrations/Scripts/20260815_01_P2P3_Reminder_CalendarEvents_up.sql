-- P2P3 Phase 2: CalendarEvents 表新增提醒加固字段
-- SQLite dialect
-- 2026-08-15

BEGIN TRANSACTION;

-- ReminderSentAt: 实际发送提醒的时间（用于 fallback 判定窗口）
ALTER TABLE "CalendarEvents"
    ADD COLUMN "ReminderSentAt" DATETIMEOFFSET NULL;

-- ReminderConfirmation: 提醒确认状态
--   pending      = 已发送提醒，等待用户确认（初始/默认状态）
--   confirmed    = 用户通过点击或自动关闭确认了收到
--   fallback_sent = Fallback Job 已补发（邮件+IM）
--   failed       = Fallback 两个通道均失败
ALTER TABLE "CalendarEvents"
    ADD COLUMN "ReminderConfirmation" NVARCHAR(20) NOT NULL DEFAULT 'pending';

-- ReminderConfirmedAt: 提醒被确认的时间
ALTER TABLE "CalendarEvents"
    ADD COLUMN "ReminderConfirmedAt" DATETIMEOFFSET NULL;

-- Fallback Job 高频查询复合索引
--   查询条件：IsDeleted=0 AND ReminderSentAt IS NOT NULL AND ReminderConfirmation='pending'
--             AND ReminderSentAt < (now - window) AND ReminderSentAt >= (now - 48h)
CREATE INDEX IF NOT EXISTS "IX_CalendarEvents_ReminderFallback"
    ON "CalendarEvents" ("ReminderConfirmation", "ReminderSentAt" DESC)
    WHERE "DeletedAt" IS NULL;

COMMIT;
