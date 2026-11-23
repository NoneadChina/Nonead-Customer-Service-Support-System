-- TT-001: 活跃计时器唯一索引（数据库层兜底防御并发竞态）
-- 每个用户最多只能有一个运行中的计时器（EndTime IS NULL）
-- 适用数据库：SQLite
-- 执行前如有重复活跃计时器需先清理：清除所有 EndTime IS NULL 的多余记录，每个 UserId 仅保留最新一条

-- 清理重复活跃计时器：保留每个用户最新的一条活跃计时器，其余软删除
UPDATE TimeEntries
SET "DeletedAt" = datetime('now'),
    "UpdatedAt" = datetime('now')
WHERE "EndTime" IS NULL
  AND "DeletedAt" IS NULL
  AND "Id" NOT IN (
      SELECT MAX(t2."Id")
      FROM TimeEntries t2
      WHERE t2."EndTime" IS NULL
        AND t2."DeletedAt" IS NULL
      GROUP BY t2."UserId"
  );

-- 创建唯一部分索引：仅对活跃计时器（EndTime IS NULL 且未软删除）强制每用户唯一
CREATE UNIQUE INDEX IF NOT EXISTS IX_TimeEntries_UserId_EndTime_ActiveTimer
ON TimeEntries ("UserId", "EndTime")
WHERE "EndTime" IS NULL AND "DeletedAt" IS NULL;
