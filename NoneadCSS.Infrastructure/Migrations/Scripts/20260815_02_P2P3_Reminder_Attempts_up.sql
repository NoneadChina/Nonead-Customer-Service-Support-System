-- P2P3 Phase 2: CalendarReminderAttempts 提醒投递审计日志表
-- SQLite dialect
-- 2026-08-15

BEGIN TRANSACTION;

CREATE TABLE "CalendarReminderAttempts" (
    "Id"              INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    "EventId"         INTEGER        NOT NULL,              -- FK to CalendarEvents
    "AttemptChannel"  NVARCHAR(20)   NOT NULL,              -- email / im / signalr
    "AttemptNo"       INTEGER        NOT NULL DEFAULT 1,    -- 第几次尝试（同一 channel）
    "Status"          NVARCHAR(20)   NOT NULL,              -- sent / failed / skipped
    "Payload"         TEXT           NULL,                  -- 请求载荷（JSON 或原始内容）
    "ErrorDetail"     TEXT           NULL,                  -- 失败时记录错误详情
    "CreatedAt"       DATETIMEOFFSET NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    "DeletedAt"       DATETIME       NULL,                  -- 软删除（审计表默认保留）

    CONSTRAINT "FK_CalendarReminderAttempts_CalendarEvents_EventId"
        FOREIGN KEY ("EventId") REFERENCES "CalendarEvents" ("Id") ON DELETE CASCADE
);

-- 规范要求复合索引：按事件 + 通道查询，用于 Fallback Job 计算 attemptNo
--   也支撑 "某事件所有投递历史" 的审计查询
CREATE INDEX IF NOT EXISTS "IX_CalRem_EventId_Channel"
    ON "CalendarReminderAttempts" ("EventId", "AttemptChannel");

-- 辅助索引：按时间倒序批量巡检（"最近 N 分钟有多少失败投递"）
CREATE INDEX IF NOT EXISTS "IX_CalendarReminderAttempts_CreatedAt"
    ON "CalendarReminderAttempts" ("CreatedAt" DESC);

-- 辅助索引：按状态过滤 + 时间排序（失败告警场景）
CREATE INDEX IF NOT EXISTS "IX_CalendarReminderAttempts_Status_CreatedAt"
    ON "CalendarReminderAttempts" ("Status", "CreatedAt" DESC);

COMMIT;
