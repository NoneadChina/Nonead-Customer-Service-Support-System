-- P2P3 Phase 2: OutlookSyncLog 同步审计日志表
-- SQLite dialect
-- 2026-08-14

BEGIN TRANSACTION;

CREATE TABLE "OutlookSyncLogs" (
    "Id"                INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL,
    "UserId"            INTEGER NOT NULL,
    "Direction"         NVARCHAR(20)   NOT NULL,   -- pull / push / sync-all
    "SyncAction"        NVARCHAR(20)   NOT NULL,   -- create / update / delete / skip / conflict / error
    "ConflictStrategy"  NVARCHAR(20)   NULL,       -- outlook_wins / local_wins / last_write_wins
    "CalendarEventId"   INTEGER        NULL,
    "OutlookEventId"    NVARCHAR(256)  NULL,
    "Status"            NVARCHAR(20)   NOT NULL,   -- success / failed / skipped / retry
    "ErrorDetail"       TEXT           NULL,       -- 错误详情（失败时记录，无长度限制）
    "StartedAt"         DATETIMEOFFSET NOT NULL,
    "FinishedAt"        DATETIMEOFFSET NOT NULL,
    "DurationMs"        INTEGER        NOT NULL,
    "CreatedAt"         DATETIMEOFFSET NOT NULL DEFAULT (CURRENT_TIMESTAMP),
    "DeletedAt"         DATETIME       NULL,

    CONSTRAINT "FK_OutlookSyncLogs_Users_UserId"
        FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);

-- 按用户查询同步历史（用户级审计查询高频路径）
CREATE INDEX IF NOT EXISTS "IX_OutlookSyncLog_UserId_CreatedAt"
    ON "OutlookSyncLogs" ("UserId", "CreatedAt" DESC);

-- 按状态过滤 + 排序（失败日志巡检 / Purge Job 清理查询）
CREATE INDEX IF NOT EXISTS "IX_OutlookSyncLog_Status_CreatedAt"
    ON "OutlookSyncLogs" ("Status", "CreatedAt" DESC);

-- 辅助索引：按本地事件 / Outlook 事件反查同步历史
CREATE INDEX IF NOT EXISTS "IX_OutlookSyncLogs_CalendarEventId"
    ON "OutlookSyncLogs" ("CalendarEventId");

CREATE INDEX IF NOT EXISTS "IX_OutlookSyncLogs_OutlookEventId"
    ON "OutlookSyncLogs" ("OutlookEventId");

COMMIT;
