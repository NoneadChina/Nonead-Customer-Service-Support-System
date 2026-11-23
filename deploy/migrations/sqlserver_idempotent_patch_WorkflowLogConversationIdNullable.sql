BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260813162544_WorkflowLogConversationIdNullable'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[WorkflowExecutionLogs]') AND [c].[name] = N'ConversationId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [WorkflowExecutionLogs] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [WorkflowExecutionLogs] ALTER COLUMN [ConversationId] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260813162544_WorkflowLogConversationIdNullable'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260813162544_WorkflowLogConversationIdNullable', N'10.0.9');
END;

COMMIT;
GO

