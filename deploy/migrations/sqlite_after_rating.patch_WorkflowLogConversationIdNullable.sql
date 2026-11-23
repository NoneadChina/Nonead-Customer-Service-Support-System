BEGIN TRANSACTION;
CREATE TABLE "ef_temp_WorkflowExecutionLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WorkflowExecutionLogs" PRIMARY KEY AUTOINCREMENT,
    "ActionsExecuted" INTEGER NOT NULL,
    "ConversationId" INTEGER NULL,
    "ErrorMessage" TEXT NULL,
    "ExecutedAt" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "WorkflowId" INTEGER NOT NULL,
    CONSTRAINT "FK_WorkflowExecutionLogs_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_WorkflowExecutionLogs_Workflows_WorkflowId" FOREIGN KEY ("WorkflowId") REFERENCES "Workflows" ("Id") ON DELETE CASCADE
);

INSERT INTO "ef_temp_WorkflowExecutionLogs" ("Id", "ActionsExecuted", "ConversationId", "ErrorMessage", "ExecutedAt", "Status", "WorkflowId")
SELECT "Id", "ActionsExecuted", "ConversationId", "ErrorMessage", "ExecutedAt", "Status", "WorkflowId"
FROM "WorkflowExecutionLogs";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "WorkflowExecutionLogs";

ALTER TABLE "ef_temp_WorkflowExecutionLogs" RENAME TO "WorkflowExecutionLogs";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE INDEX "IX_WorkflowExecutionLogs_ConversationId" ON "WorkflowExecutionLogs" ("ConversationId");

CREATE INDEX "IX_WorkflowExecutionLogs_ExecutedAt" ON "WorkflowExecutionLogs" ("ExecutedAt");

CREATE INDEX "IX_WorkflowExecutionLogs_WorkflowId" ON "WorkflowExecutionLogs" ("WorkflowId");

COMMIT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260813162544_WorkflowLogConversationIdNullable', '10.0.9');

