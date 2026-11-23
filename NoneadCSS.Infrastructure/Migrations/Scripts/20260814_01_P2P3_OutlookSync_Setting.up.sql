-- P2P3 Phase 2: OutlookSyncSetting 增量同步 + 重试队列字段
-- SQLite dialect
-- 2026-08-14

BEGIN TRANSACTION;

-- DeltaToken: Graph API 增量同步 delta token
ALTER TABLE "OutlookSyncSettings" ADD COLUMN "DeltaToken" NVARCHAR(1024) NULL;

-- ConsecutiveFailures: 连续失败计数（成功时归零）
ALTER TABLE "OutlookSyncSettings" ADD COLUMN "ConsecutiveFailures" INTEGER NOT NULL DEFAULT 0;

-- NextRetryAt: 下次重试时间（连续失败后按指数退避计算）
-- SQLite 无原生 DATETIMEOFFSET，此处按 DATETIMEOFFSET 语义以 ISO8601 UTC 字符串存储（含偏移量）
ALTER TABLE "OutlookSyncSettings" ADD COLUMN "NextRetryAt" DATETIMEOFFSET NULL;

-- LastFullSyncAt: 最近一次全量同步时间（delta token 首次从 null 变为非 null 时记录）
-- SQLite 无原生 DATETIMEOFFSET，此处按 DATETIMEOFFSET 语义以 ISO8601 UTC 字符串存储（含偏移量）
ALTER TABLE "OutlookSyncSettings" ADD COLUMN "LastFullSyncAt" DATETIMEOFFSET NULL;

-- NextRetryAt 索引：批量同步过滤查询高频使用
CREATE INDEX IF NOT EXISTS "IX_OutlookSyncSettings_NextRetryAt"
    ON "OutlookSyncSettings" ("NextRetryAt");

COMMIT;
