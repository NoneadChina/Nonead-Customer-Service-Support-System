-- ============================================
-- NoneadCSS 数据库迁移脚本
-- 版本：2026-07-14
-- 说明：添加新字段以支持：
--   1. 邮件线程匹配（Message-ID, In-Reply-To, References）
--   2. 文件夹自动更新
--   3. 附件收取
-- ============================================

-- 1. Conversations 表新增字段
ALTER TABLE "Conversations" ADD COLUMN "Number" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "Conversations" ADD COLUMN "ThreadsCount" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "Conversations" ADD COLUMN "Cc" TEXT NOT NULL DEFAULT '';
ALTER TABLE "Conversations" ADD COLUMN "Bcc" TEXT NOT NULL DEFAULT '';
ALTER TABLE "Conversations" ADD COLUMN "Preview" TEXT NOT NULL DEFAULT '';
ALTER TABLE "Conversations" ADD COLUMN "Imported" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "Conversations" ADD COLUMN "CreatedByUserId" INTEGER NULL;
ALTER TABLE "Conversations" ADD COLUMN "CreatedByCustomerId" INTEGER NULL;
ALTER TABLE "Conversations" ADD COLUMN "ClosedByUserId" INTEGER NULL;
ALTER TABLE "Conversations" ADD COLUMN "LastReplyFrom" INTEGER NULL;
ALTER TABLE "Conversations" ADD COLUMN "UserUpdatedAt" TEXT NULL;
ALTER TABLE "Conversations" ADD COLUMN "SourceType" INTEGER NOT NULL DEFAULT 1;

-- 2. Conversations 表新增索引
CREATE INDEX "IX_Conversations_Number" ON "Conversations" ("Number");
CREATE INDEX "IX_Conversations_CreatedByUserId" ON "Conversations" ("CreatedByUserId");
CREATE INDEX "IX_Conversations_ClosedByUserId" ON "Conversations" ("ClosedByUserId");

-- 3. Threads 表新增字段（Subject 已存在，跳过）
ALTER TABLE "Threads" ADD COLUMN "Cc" TEXT NOT NULL DEFAULT '';
ALTER TABLE "Threads" ADD COLUMN "Bcc" TEXT NOT NULL DEFAULT '';
ALTER TABLE "Threads" ADD COLUMN "MessageId" TEXT NOT NULL DEFAULT '';
ALTER TABLE "Threads" ADD COLUMN "Headers" TEXT NULL;
ALTER TABLE "Threads" ADD COLUMN "State" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "Threads" ADD COLUMN "ActionType" INTEGER NULL;
ALTER TABLE "Threads" ADD COLUMN "ActionData" TEXT NULL;
ALTER TABLE "Threads" ADD COLUMN "IsFirst" INTEGER NOT NULL DEFAULT 0;
ALTER TABLE "Threads" ADD COLUMN "SourceType" INTEGER NOT NULL DEFAULT 1;
ALTER TABLE "Threads" ADD COLUMN "CreatedByUserId" INTEGER NULL;
ALTER TABLE "Threads" ADD COLUMN "CreatedByCustomerId" INTEGER NULL;
ALTER TABLE "Threads" ADD COLUMN "SendStatus" INTEGER NULL;
ALTER TABLE "Threads" ADD COLUMN "OpenedAt" TEXT NULL;
ALTER TABLE "Threads" ADD COLUMN "UpdatedAt" TEXT NOT NULL DEFAULT '';

-- 4. Threads 表新增索引
CREATE INDEX "IX_Threads_MessageId" ON "Threads" ("MessageId");
CREATE INDEX "IX_Threads_CreatedByUserId" ON "Threads" ("CreatedByUserId");

-- 5. 更新已存在会话的 Number 字段（从 1 开始递增）
UPDATE "Conversations" SET "Number" = ROWID WHERE "Number" = 0;

-- 6. 统计已存在会话的线程数
UPDATE "Conversations" 
SET "ThreadsCount" = (
    SELECT COUNT(*) 
    FROM "Threads" 
    WHERE "Threads"."ConversationId" = "Conversations"."Id" 
    AND "Threads"."Type" != 4
);

-- 7. 更新 UpdatedAt 默认值
UPDATE "Threads" SET "UpdatedAt" = "CreatedAt" WHERE "UpdatedAt" = '' OR "UpdatedAt" IS NULL;
