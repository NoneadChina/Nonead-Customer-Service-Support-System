IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [Customers] (
        [Id] INTEGER NOT NULL,
        [FirstName] TEXT NOT NULL,
        [LastName] TEXT NOT NULL,
        [Address] TEXT NULL,
        [City] TEXT NULL,
        [State] TEXT NULL,
        [Zip] TEXT NULL,
        [Country] TEXT NULL,
        [Company] TEXT NULL,
        [JobTitle] TEXT NULL,
        [Notes] TEXT NULL,
        [Channel] TEXT NULL,
        [ChannelId] TEXT NULL,
        [Phones] TEXT NOT NULL,
        [SocialProfiles] TEXT NOT NULL,
        [Websites] TEXT NOT NULL,
        [Avatar] TEXT NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_Customers] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [Mailboxes] (
        [Id] INTEGER NOT NULL,
        [Name] TEXT NOT NULL,
        [Email] TEXT NOT NULL,
        [InProtocol] INTEGER NOT NULL,
        [InHost] TEXT NOT NULL,
        [InPort] INTEGER NOT NULL,
        [InEncryption] INTEGER NOT NULL,
        [InValidateCert] INTEGER NOT NULL,
        [InUsername] TEXT NOT NULL,
        [InPassword] TEXT NOT NULL,
        [ImapSentFolder] TEXT NOT NULL,
        [OutMethod] INTEGER NOT NULL,
        [OutHost] TEXT NOT NULL,
        [OutPort] INTEGER NOT NULL,
        [OutEncryption] INTEGER NOT NULL,
        [OutUsername] TEXT NOT NULL,
        [OutPassword] TEXT NOT NULL,
        [Aliases] TEXT NOT NULL,
        [AliasesReply] TEXT NOT NULL,
        [AutoBcc] TEXT NOT NULL,
        [AutoReplyEnabled] INTEGER NOT NULL,
        [AutoReplySubject] TEXT NOT NULL,
        [AutoReplyMessage] TEXT NOT NULL,
        [BeforeReply] TEXT NOT NULL,
        [Signature] TEXT NOT NULL,
        [OauthEnabled] INTEGER NOT NULL,
        [OauthProvider] TEXT NOT NULL,
        [OauthAccessToken] TEXT NOT NULL,
        [OauthRefreshToken] TEXT NOT NULL,
        [OauthTokenExpiresAt] TEXT NULL,
        [IsActive] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_Mailboxes] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [Modules] (
        [Id] INTEGER NOT NULL,
        [Name] TEXT NOT NULL,
        [Title] TEXT NOT NULL,
        [Description] TEXT NULL,
        [Version] TEXT NOT NULL,
        [Author] TEXT NULL,
        [Icon] TEXT NULL,
        [IsActive] INTEGER NOT NULL,
        [Category] TEXT NOT NULL,
        [Settings] TEXT NULL,
        [SortOrder] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_Modules] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [NotificationTemplates] (
        [Id] INTEGER NOT NULL,
        [Name] TEXT NOT NULL,
        [Subject] TEXT NOT NULL,
        [Body] TEXT NOT NULL,
        [EventType] TEXT NOT NULL,
        [IsActive] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_NotificationTemplates] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [Roles] (
        [Id] INTEGER NOT NULL,
        [Name] TEXT NOT NULL,
        [Slug] TEXT NOT NULL,
        [Description] TEXT NULL,
        [PermissionsJson] TEXT NOT NULL,
        [IsSystem] INTEGER NOT NULL,
        [SortOrder] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_Roles] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [SystemOptions] (
        [Id] INTEGER NOT NULL,
        [Name] TEXT NOT NULL,
        [Value] TEXT NOT NULL,
        [Category] TEXT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_SystemOptions] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] INTEGER NOT NULL,
        [FirstName] TEXT NOT NULL,
        [LastName] TEXT NOT NULL,
        [Email] TEXT NOT NULL,
        [Password] TEXT NOT NULL,
        [Role] INTEGER NOT NULL,
        [Status] INTEGER NOT NULL,
        [Timezone] TEXT NOT NULL,
        [Locale] TEXT NOT NULL,
        [Permissions] TEXT NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [Emails] (
        [Id] INTEGER NOT NULL,
        [CustomerId] INTEGER NOT NULL,
        [EmailAddress] TEXT NOT NULL,
        [Type] INTEGER NOT NULL,
        CONSTRAINT [PK_Emails] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Emails_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [CustomFields] (
        [Id] INTEGER NOT NULL,
        [Name] TEXT NOT NULL,
        [Type] TEXT NOT NULL,
        [EntityType] TEXT NOT NULL,
        [MailboxId] INTEGER NULL,
        [IsRequired] INTEGER NOT NULL,
        [IsActive] INTEGER NOT NULL,
        [SortOrder] INTEGER NOT NULL,
        [OptionsJson] TEXT NULL,
        [DefaultValue] TEXT NULL,
        [Placeholder] TEXT NULL,
        [HelpText] TEXT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_CustomFields] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CustomFields_Mailboxes_MailboxId] FOREIGN KEY ([MailboxId]) REFERENCES [Mailboxes] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [Macros] (
        [Id] INTEGER NOT NULL,
        [Name] TEXT NOT NULL,
        [Description] TEXT NULL,
        [MailboxId] INTEGER NULL,
        [IsActive] INTEGER NOT NULL,
        [SortOrder] INTEGER NOT NULL,
        [ActionsJson] TEXT NOT NULL DEFAULT '[]',
        [UsageCount] INTEGER NOT NULL,
        [LastUsedAt] TEXT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_Macros] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Macros_Mailboxes_MailboxId] FOREIGN KEY ([MailboxId]) REFERENCES [Mailboxes] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [MailTemplates] (
        [Id] INTEGER NOT NULL,
        [Name] TEXT NOT NULL,
        [Slug] TEXT NOT NULL,
        [Type] INTEGER NOT NULL,
        [Subject] TEXT NOT NULL,
        [BodyHtml] TEXT NOT NULL,
        [BodyText] TEXT NOT NULL,
        [VariablesJson] TEXT NULL,
        [IsActive] INTEGER NOT NULL,
        [IsSystem] INTEGER NOT NULL,
        [MailboxId] INTEGER NULL,
        [Description] TEXT NULL,
        [SortOrder] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_MailTemplates] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MailTemplates_Mailboxes_MailboxId] FOREIGN KEY ([MailboxId]) REFERENCES [Mailboxes] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [SlaPolicies] (
        [Id] INTEGER NOT NULL,
        [Name] TEXT NOT NULL,
        [Description] TEXT NULL,
        [MailboxId] INTEGER NULL,
        [FirstResponseHours] REAL NOT NULL DEFAULT CAST(24 AS REAL),
        [ResolutionHours] REAL NOT NULL DEFAULT CAST(72 AS REAL),
        [BusinessHoursOnly] INTEGER NOT NULL,
        [Priority] INTEGER NOT NULL,
        [IsActive] INTEGER NOT NULL,
        [SortOrder] INTEGER NOT NULL,
        [AppliedCount] INTEGER NOT NULL,
        [ViolationCount] INTEGER NOT NULL,
        [LastAppliedAt] TEXT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_SlaPolicies] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SlaPolicies_Mailboxes_MailboxId] FOREIGN KEY ([MailboxId]) REFERENCES [Mailboxes] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [Tags] (
        [Id] INTEGER NOT NULL,
        [MailboxId] INTEGER NULL,
        [Name] TEXT NOT NULL,
        [Color] TEXT NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_Tags] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Tags_Mailboxes_MailboxId] FOREIGN KEY ([MailboxId]) REFERENCES [Mailboxes] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [Teams] (
        [Id] INTEGER NOT NULL,
        [Name] TEXT NOT NULL,
        [Description] TEXT NULL,
        [MailboxId] INTEGER NULL,
        [IsActive] INTEGER NOT NULL,
        [SortOrder] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_Teams] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Teams_Mailboxes_MailboxId] FOREIGN KEY ([MailboxId]) REFERENCES [Mailboxes] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [Webhooks] (
        [Id] INTEGER NOT NULL,
        [Name] TEXT NOT NULL,
        [Url] TEXT NOT NULL,
        [Method] TEXT NOT NULL,
        [Events] TEXT NOT NULL,
        [HeadersJson] TEXT NULL,
        [Secret] TEXT NULL,
        [IsActive] INTEGER NOT NULL,
        [MailboxId] INTEGER NULL,
        [TriggerCount] INTEGER NOT NULL,
        [SuccessCount] INTEGER NOT NULL,
        [FailCount] INTEGER NOT NULL,
        [LastTriggeredAt] TEXT NULL,
        [LastError] TEXT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_Webhooks] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Webhooks_Mailboxes_MailboxId] FOREIGN KEY ([MailboxId]) REFERENCES [Mailboxes] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [Workflows] (
        [Id] INTEGER NOT NULL,
        [Name] TEXT NOT NULL,
        [Description] TEXT NULL,
        [MailboxId] INTEGER NULL,
        [IsActive] INTEGER NOT NULL,
        [SortOrder] INTEGER NOT NULL,
        [TriggerType] TEXT NOT NULL,
        [ConditionsJson] TEXT NULL,
        [ActionsJson] TEXT NULL,
        [MatchCount] INTEGER NOT NULL,
        [LastMatchedAt] TEXT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_Workflows] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Workflows_Mailboxes_MailboxId] FOREIGN KEY ([MailboxId]) REFERENCES [Mailboxes] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [ApiKeys] (
        [Id] INTEGER NOT NULL,
        [Name] TEXT NOT NULL,
        [Key] TEXT NOT NULL,
        [Description] TEXT NULL,
        [UserId] INTEGER NULL,
        [Scopes] TEXT NOT NULL,
        [IsActive] INTEGER NOT NULL,
        [ExpiresAt] TEXT NULL,
        [LastUsedAt] TEXT NULL,
        [LastUsedIp] TEXT NULL,
        [UsageCount] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_ApiKeys] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ApiKeys_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] INTEGER NOT NULL,
        [UserId] INTEGER NULL,
        [Action] TEXT NOT NULL,
        [EntityType] TEXT NOT NULL,
        [EntityId] INTEGER NULL,
        [OldValues] TEXT NOT NULL,
        [NewValues] TEXT NOT NULL,
        [IpAddress] TEXT NOT NULL,
        [UserAgent] TEXT NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AuditLogs_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [CannedResponses] (
        [Id] INTEGER NOT NULL,
        [MailboxId] INTEGER NULL,
        [Name] TEXT NOT NULL,
        [Body] TEXT NOT NULL,
        [Category] TEXT NULL,
        [SortOrder] INTEGER NOT NULL,
        [IsShared] INTEGER NOT NULL,
        [CreatedByUserId] INTEGER NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_CannedResponses] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CannedResponses_Mailboxes_MailboxId] FOREIGN KEY ([MailboxId]) REFERENCES [Mailboxes] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_CannedResponses_Users_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [EmailSignatures] (
        [Id] INTEGER NOT NULL,
        [UserId] INTEGER NOT NULL,
        [MailboxId] INTEGER NULL,
        [Name] TEXT NOT NULL,
        [Body] TEXT NOT NULL,
        [IsDefault] INTEGER NOT NULL,
        [SortOrder] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_EmailSignatures] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_EmailSignatures_Mailboxes_MailboxId] FOREIGN KEY ([MailboxId]) REFERENCES [Mailboxes] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_EmailSignatures_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [Folders] (
        [Id] INTEGER NOT NULL,
        [MailboxId] INTEGER NOT NULL,
        [UserId] INTEGER NULL,
        [Type] INTEGER NOT NULL,
        [Name] TEXT NOT NULL,
        [ActiveCount] INTEGER NOT NULL,
        [TotalCount] INTEGER NOT NULL,
        CONSTRAINT [PK_Folders] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Folders_Mailboxes_MailboxId] FOREIGN KEY ([MailboxId]) REFERENCES [Mailboxes] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Folders_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [MailboxUsers] (
        [MailboxId] INTEGER NOT NULL,
        [UserId] INTEGER NOT NULL,
        [Access] TEXT NOT NULL,
        CONSTRAINT [PK_MailboxUsers] PRIMARY KEY ([MailboxId], [UserId]),
        CONSTRAINT [FK_MailboxUsers_Mailboxes_MailboxId] FOREIGN KEY ([MailboxId]) REFERENCES [Mailboxes] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_MailboxUsers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [NotificationRules] (
        [Id] INTEGER NOT NULL,
        [Name] TEXT NOT NULL,
        [EventType] TEXT NOT NULL,
        [MailboxId] INTEGER NULL,
        [UserId] INTEGER NULL,
        [TemplateId] INTEGER NULL,
        [IsActive] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_NotificationRules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_NotificationRules_Mailboxes_MailboxId] FOREIGN KEY ([MailboxId]) REFERENCES [Mailboxes] ([Id]),
        CONSTRAINT [FK_NotificationRules_NotificationTemplates_TemplateId] FOREIGN KEY ([TemplateId]) REFERENCES [NotificationTemplates] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_NotificationRules_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [UserRoles] (
        [Id] INTEGER NOT NULL,
        [UserId] INTEGER NOT NULL,
        [RoleId] INTEGER NOT NULL,
        [MailboxId] INTEGER NULL,
        [CreatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_UserRoles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserRoles_Mailboxes_MailboxId] FOREIGN KEY ([MailboxId]) REFERENCES [Mailboxes] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserRoles_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_UserRoles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [CustomFieldValues] (
        [Id] INTEGER NOT NULL,
        [CustomFieldId] INTEGER NOT NULL,
        [EntityId] INTEGER NOT NULL,
        [EntityType] TEXT NOT NULL,
        [Value] TEXT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_CustomFieldValues] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CustomFieldValues_CustomFields_CustomFieldId] FOREIGN KEY ([CustomFieldId]) REFERENCES [CustomFields] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [TeamMembers] (
        [Id] INTEGER NOT NULL,
        [TeamId] INTEGER NOT NULL,
        [UserId] INTEGER NOT NULL,
        [Role] INTEGER NOT NULL,
        [JoinedAt] TEXT NOT NULL,
        CONSTRAINT [PK_TeamMembers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TeamMembers_Teams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [Teams] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_TeamMembers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [WebhookLogs] (
        [Id] INTEGER NOT NULL,
        [WebhookId] INTEGER NOT NULL,
        [EventType] TEXT NOT NULL,
        [EntityType] TEXT NULL,
        [EntityId] INTEGER NULL,
        [Url] TEXT NOT NULL,
        [Method] TEXT NOT NULL,
        [RequestBody] TEXT NULL,
        [ResponseStatusCode] INTEGER NULL,
        [ResponseBody] TEXT NULL,
        [IsSuccess] INTEGER NOT NULL,
        [ErrorMessage] TEXT NULL,
        [DurationMs] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_WebhookLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WebhookLogs_Webhooks_WebhookId] FOREIGN KEY ([WebhookId]) REFERENCES [Webhooks] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [ApiKeyLogs] (
        [Id] INTEGER NOT NULL,
        [ApiKeyId] INTEGER NOT NULL,
        [Endpoint] TEXT NOT NULL,
        [Method] TEXT NOT NULL,
        [StatusCode] INTEGER NOT NULL,
        [IpAddress] TEXT NULL,
        [UserAgent] TEXT NULL,
        [DurationMs] INTEGER NOT NULL,
        [RequestBody] TEXT NULL,
        [ErrorMessage] TEXT NULL,
        [CreatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_ApiKeyLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ApiKeyLogs_ApiKeys_ApiKeyId] FOREIGN KEY ([ApiKeyId]) REFERENCES [ApiKeys] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [Conversations] (
        [Id] INTEGER NOT NULL,
        [Number] INTEGER NOT NULL,
        [ThreadsCount] INTEGER NOT NULL,
        [MailboxId] INTEGER NOT NULL,
        [CustomerId] INTEGER NULL,
        [UserId] INTEGER NULL,
        [FolderId] INTEGER NULL,
        [Type] INTEGER NOT NULL,
        [SourceVia] INTEGER NOT NULL,
        [SourceType] INTEGER NOT NULL,
        [Status] INTEGER NOT NULL,
        [State] INTEGER NOT NULL,
        [Subject] TEXT NOT NULL,
        [CustomerEmail] TEXT NOT NULL,
        [Cc] TEXT NOT NULL,
        [Bcc] TEXT NOT NULL,
        [Preview] TEXT NOT NULL,
        [HasAttachments] INTEGER NOT NULL,
        [Imported] INTEGER NOT NULL,
        [CreatedByUserId] INTEGER NULL,
        [CreatedByCustomerId] INTEGER NULL,
        [ClosedByUserId] INTEGER NULL,
        [LastReplyAt] TEXT NULL,
        [LastReplyFrom] INTEGER NULL,
        [UserUpdatedAt] TEXT NULL,
        [ClosedAt] TEXT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_Conversations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Conversations_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Conversations_Folders_FolderId] FOREIGN KEY ([FolderId]) REFERENCES [Folders] ([Id]),
        CONSTRAINT [FK_Conversations_Mailboxes_MailboxId] FOREIGN KEY ([MailboxId]) REFERENCES [Mailboxes] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Conversations_Users_ClosedByUserId] FOREIGN KEY ([ClosedByUserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Conversations_Users_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Conversations_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [ConversationFolders] (
        [ConversationId] INTEGER NOT NULL,
        [FolderId] INTEGER NOT NULL,
        CONSTRAINT [PK_ConversationFolders] PRIMARY KEY ([ConversationId], [FolderId]),
        CONSTRAINT [FK_ConversationFolders_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ConversationFolders_Folders_FolderId] FOREIGN KEY ([FolderId]) REFERENCES [Folders] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [ConversationTags] (
        [ConversationId] INTEGER NOT NULL,
        [TagId] INTEGER NOT NULL,
        [Id] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_ConversationTags] PRIMARY KEY ([ConversationId], [TagId]),
        CONSTRAINT [FK_ConversationTags_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ConversationTags_Tags_TagId] FOREIGN KEY ([TagId]) REFERENCES [Tags] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [Followers] (
        [Id] INTEGER NOT NULL,
        [ConversationId] INTEGER NOT NULL,
        [UserId] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_Followers] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Followers_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Followers_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [MacroExecutionLogs] (
        [Id] INTEGER NOT NULL,
        [MacroId] INTEGER NOT NULL,
        [ConversationId] INTEGER NOT NULL,
        [ExecutedByUserId] INTEGER NOT NULL,
        [Status] TEXT NOT NULL,
        [ActionsExecuted] INTEGER NOT NULL,
        [ErrorMessage] TEXT NULL,
        [ExecutedAt] TEXT NOT NULL,
        CONSTRAINT [PK_MacroExecutionLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MacroExecutionLogs_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_MacroExecutionLogs_Macros_MacroId] FOREIGN KEY ([MacroId]) REFERENCES [Macros] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_MacroExecutionLogs_Users_ExecutedByUserId] FOREIGN KEY ([ExecutedByUserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [SlaViolations] (
        [Id] INTEGER NOT NULL,
        [SlaPolicyId] INTEGER NOT NULL,
        [ConversationId] INTEGER NOT NULL,
        [ViolationType] TEXT NOT NULL,
        [DueAt] TEXT NOT NULL,
        [DetectedAt] TEXT NOT NULL,
        [ResolvedAt] TEXT NULL,
        [Status] TEXT NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_SlaViolations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SlaViolations_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SlaViolations_SlaPolicies_SlaPolicyId] FOREIGN KEY ([SlaPolicyId]) REFERENCES [SlaPolicies] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [Stars] (
        [Id] INTEGER NOT NULL,
        [ConversationId] INTEGER NOT NULL,
        [UserId] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_Stars] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Stars_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Stars_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [Threads] (
        [Id] INTEGER NOT NULL,
        [ConversationId] INTEGER NOT NULL,
        [UserId] INTEGER NULL,
        [CustomerId] INTEGER NULL,
        [Type] INTEGER NOT NULL,
        [State] INTEGER NOT NULL,
        [ActionType] INTEGER NULL,
        [ActionData] TEXT NULL,
        [Body] TEXT NOT NULL,
        [Subject] TEXT NOT NULL,
        [From] TEXT NOT NULL,
        [To] TEXT NOT NULL,
        [Cc] TEXT NOT NULL,
        [Bcc] TEXT NOT NULL,
        [MessageId] TEXT NOT NULL,
        [Headers] TEXT NULL,
        [IsInternal] INTEGER NOT NULL,
        [HasAttachments] INTEGER NOT NULL,
        [IsFirst] INTEGER NOT NULL,
        [SourceVia] INTEGER NOT NULL,
        [SourceType] INTEGER NOT NULL,
        [CreatedByUserId] INTEGER NULL,
        [CreatedByCustomerId] INTEGER NULL,
        [SendStatus] INTEGER NULL,
        [OpenedAt] TEXT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_Threads] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Threads_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Threads_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Threads_Users_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_Threads_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [WorkflowExecutionLogs] (
        [Id] INTEGER NOT NULL,
        [WorkflowId] INTEGER NOT NULL,
        [ConversationId] INTEGER NOT NULL,
        [Status] TEXT NOT NULL,
        [ActionsExecuted] INTEGER NOT NULL,
        [ErrorMessage] TEXT NULL,
        [ExecutedAt] TEXT NOT NULL,
        CONSTRAINT [PK_WorkflowExecutionLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_WorkflowExecutionLogs_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_WorkflowExecutionLogs_Workflows_WorkflowId] FOREIGN KEY ([WorkflowId]) REFERENCES [Workflows] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [Attachments] (
        [Id] INTEGER NOT NULL,
        [ThreadId] INTEGER NOT NULL,
        [UserId] INTEGER NULL,
        [FileName] TEXT NOT NULL,
        [MimeType] TEXT NOT NULL,
        [Type] INTEGER NOT NULL,
        [FileDir] TEXT NOT NULL,
        [Size] INTEGER NOT NULL,
        [Embedded] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_Attachments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Attachments_Threads_ThreadId] FOREIGN KEY ([ThreadId]) REFERENCES [Threads] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_Attachments_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [SatisfactionRatings] (
        [Id] INTEGER NOT NULL,
        [ConversationId] INTEGER NOT NULL,
        [ThreadId] INTEGER NULL,
        [CustomerId] INTEGER NOT NULL,
        [UserId] INTEGER NULL,
        [Rating] INTEGER NOT NULL,
        [Comment] TEXT NULL,
        [CustomerName] TEXT NULL,
        [CustomerEmail] TEXT NULL,
        [IpAddress] TEXT NULL,
        [UserAgent] TEXT NULL,
        [CreatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_SatisfactionRatings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SatisfactionRatings_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SatisfactionRatings_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SatisfactionRatings_Threads_ThreadId] FOREIGN KEY ([ThreadId]) REFERENCES [Threads] ([Id]),
        CONSTRAINT [FK_SatisfactionRatings_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE TABLE [SendLogs] (
        [Id] INTEGER NOT NULL,
        [ThreadId] INTEGER NULL,
        [MessageId] TEXT NOT NULL,
        [Email] TEXT NOT NULL,
        [MailType] INTEGER NOT NULL,
        [Status] INTEGER NOT NULL,
        [CustomerId] INTEGER NULL,
        [UserId] INTEGER NULL,
        [CreatedAt] TEXT NOT NULL,
        [OpenCount] INTEGER NOT NULL,
        [FirstOpenedAt] TEXT NULL,
        [LastOpenedAt] TEXT NULL,
        [ErrorMessage] TEXT NULL,
        [SentAt] TEXT NULL,
        [AttemptCount] INTEGER NOT NULL,
        [AttachmentIds] TEXT NULL,
        [MailboxId] INTEGER NOT NULL,
        [Cc] TEXT NULL,
        [Bcc] TEXT NULL,
        [Subject] TEXT NULL,
        [Body] TEXT NULL,
        CONSTRAINT [PK_SendLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SendLogs_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]),
        CONSTRAINT [FK_SendLogs_Threads_ThreadId] FOREIGN KEY ([ThreadId]) REFERENCES [Threads] ([Id]),
        CONSTRAINT [FK_SendLogs_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_ApiKeyLogs_ApiKeyId] ON [ApiKeyLogs] ([ApiKeyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_ApiKeyLogs_CreatedAt] ON [ApiKeyLogs] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_ApiKeyLogs_Endpoint] ON [ApiKeyLogs] ([Endpoint]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_ApiKeyLogs_StatusCode] ON [ApiKeyLogs] ([StatusCode]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_ApiKeys_IsActive] ON [ApiKeys] ([IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ApiKeys_Key] ON [ApiKeys] ([Key]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_ApiKeys_UserId] ON [ApiKeys] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Attachments_ThreadId] ON [Attachments] ([ThreadId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Attachments_UserId] ON [Attachments] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_CreatedAt] ON [AuditLogs] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_EntityType_EntityId] ON [AuditLogs] ([EntityType], [EntityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_UserId] ON [AuditLogs] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_CannedResponses_Category] ON [CannedResponses] ([Category]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_CannedResponses_CreatedByUserId] ON [CannedResponses] ([CreatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_CannedResponses_IsShared] ON [CannedResponses] ([IsShared]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_CannedResponses_MailboxId_Name] ON [CannedResponses] ([MailboxId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_ConversationFolders_FolderId] ON [ConversationFolders] ([FolderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Conversations_ClosedByUserId] ON [Conversations] ([ClosedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Conversations_CreatedByUserId] ON [Conversations] ([CreatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Conversations_CustomerId] ON [Conversations] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Conversations_FolderId] ON [Conversations] ([FolderId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Conversations_MailboxId] ON [Conversations] ([MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Conversations_UserId] ON [Conversations] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_ConversationTags_TagId] ON [ConversationTags] ([TagId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_CustomFields_EntityType] ON [CustomFields] ([EntityType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_CustomFields_IsActive] ON [CustomFields] ([IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_CustomFields_MailboxId] ON [CustomFields] ([MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_CustomFieldValues_CustomFieldId] ON [CustomFieldValues] ([CustomFieldId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_CustomFieldValues_EntityType_EntityId] ON [CustomFieldValues] ([EntityType], [EntityId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Emails_CustomerId] ON [Emails] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_EmailSignatures_IsDefault] ON [EmailSignatures] ([IsDefault]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_EmailSignatures_MailboxId] ON [EmailSignatures] ([MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_EmailSignatures_UserId] ON [EmailSignatures] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Folders_MailboxId] ON [Folders] ([MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Folders_UserId] ON [Folders] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Followers_ConversationId] ON [Followers] ([ConversationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Followers_UserId] ON [Followers] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_MacroExecutionLogs_ConversationId] ON [MacroExecutionLogs] ([ConversationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_MacroExecutionLogs_ExecutedAt] ON [MacroExecutionLogs] ([ExecutedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_MacroExecutionLogs_ExecutedByUserId] ON [MacroExecutionLogs] ([ExecutedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_MacroExecutionLogs_MacroId] ON [MacroExecutionLogs] ([MacroId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Macros_IsActive_MailboxId] ON [Macros] ([IsActive], [MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Macros_MailboxId] ON [Macros] ([MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Macros_SortOrder] ON [Macros] ([SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Macros_UsageCount] ON [Macros] ([UsageCount]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Mailboxes_Email] ON [Mailboxes] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_MailboxUsers_UserId] ON [MailboxUsers] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_MailTemplates_IsActive] ON [MailTemplates] ([IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_MailTemplates_MailboxId] ON [MailTemplates] ([MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE UNIQUE INDEX [IX_MailTemplates_Slug] ON [MailTemplates] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_MailTemplates_Type] ON [MailTemplates] ([Type]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Modules_Category] ON [Modules] ([Category]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Modules_IsActive] ON [Modules] ([IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Modules_Name] ON [Modules] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Modules_SortOrder] ON [Modules] ([SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_NotificationRules_EventType] ON [NotificationRules] ([EventType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_NotificationRules_MailboxId] ON [NotificationRules] ([MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_NotificationRules_TemplateId] ON [NotificationRules] ([TemplateId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_NotificationRules_UserId] ON [NotificationRules] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_NotificationTemplates_EventType] ON [NotificationTemplates] ([EventType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Roles_IsSystem] ON [Roles] ([IsSystem]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Roles_Slug] ON [Roles] ([Slug]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SatisfactionRatings_ConversationId] ON [SatisfactionRatings] ([ConversationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SatisfactionRatings_CreatedAt] ON [SatisfactionRatings] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SatisfactionRatings_CustomerId] ON [SatisfactionRatings] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SatisfactionRatings_Rating] ON [SatisfactionRatings] ([Rating]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SatisfactionRatings_ThreadId] ON [SatisfactionRatings] ([ThreadId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SatisfactionRatings_UserId] ON [SatisfactionRatings] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SendLogs_CreatedAt] ON [SendLogs] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SendLogs_CustomerId] ON [SendLogs] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SendLogs_Email] ON [SendLogs] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SendLogs_MailboxId] ON [SendLogs] ([MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SendLogs_MailType] ON [SendLogs] ([MailType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SendLogs_MessageId] ON [SendLogs] ([MessageId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SendLogs_Status] ON [SendLogs] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SendLogs_ThreadId] ON [SendLogs] ([ThreadId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SendLogs_UserId] ON [SendLogs] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SlaPolicies_IsActive_MailboxId] ON [SlaPolicies] ([IsActive], [MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SlaPolicies_MailboxId] ON [SlaPolicies] ([MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SlaPolicies_Priority] ON [SlaPolicies] ([Priority]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SlaPolicies_SortOrder] ON [SlaPolicies] ([SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SlaViolations_ConversationId] ON [SlaViolations] ([ConversationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SlaViolations_ConversationId_ViolationType] ON [SlaViolations] ([ConversationId], [ViolationType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SlaViolations_DueAt] ON [SlaViolations] ([DueAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SlaViolations_SlaPolicyId] ON [SlaViolations] ([SlaPolicyId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_SlaViolations_Status] ON [SlaViolations] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Stars_ConversationId] ON [Stars] ([ConversationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Stars_UserId] ON [Stars] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SystemOptions_Name] ON [SystemOptions] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tags_MailboxId_Name] ON [Tags] ([MailboxId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_TeamMembers_TeamId] ON [TeamMembers] ([TeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TeamMembers_TeamId_UserId] ON [TeamMembers] ([TeamId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_TeamMembers_UserId] ON [TeamMembers] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Teams_IsActive] ON [Teams] ([IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Teams_MailboxId] ON [Teams] ([MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Teams_Name] ON [Teams] ([Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Threads_ConversationId] ON [Threads] ([ConversationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Threads_CreatedByUserId] ON [Threads] ([CreatedByUserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Threads_CustomerId] ON [Threads] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Threads_UserId] ON [Threads] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_UserRoles_MailboxId] ON [UserRoles] ([MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_UserRoles_RoleId] ON [UserRoles] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_UserRoles_UserId] ON [UserRoles] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_WebhookLogs_CreatedAt] ON [WebhookLogs] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_WebhookLogs_EventType] ON [WebhookLogs] ([EventType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_WebhookLogs_WebhookId] ON [WebhookLogs] ([WebhookId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Webhooks_IsActive] ON [Webhooks] ([IsActive]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Webhooks_MailboxId] ON [Webhooks] ([MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_WorkflowExecutionLogs_ConversationId] ON [WorkflowExecutionLogs] ([ConversationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_WorkflowExecutionLogs_ExecutedAt] ON [WorkflowExecutionLogs] ([ExecutedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_WorkflowExecutionLogs_WorkflowId] ON [WorkflowExecutionLogs] ([WorkflowId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Workflows_IsActive_TriggerType] ON [Workflows] ([IsActive], [TriggerType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Workflows_MailboxId] ON [Workflows] ([MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    CREATE INDEX [IX_Workflows_SortOrder] ON [Workflows] ([SortOrder]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714064654_FixSendLogAttachmentIds'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260714064654_FixSendLogAttachmentIds', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714081440_AddMailboxLastUid'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SendLogs]') AND [c].[name] = N'MailboxId');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [SendLogs] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [SendLogs] ALTER COLUMN [MailboxId] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714081440_AddMailboxLastUid'
)
BEGIN
    ALTER TABLE [Mailboxes] ADD [LastUid] INTEGER NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714081440_AddMailboxLastUid'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260714081440_AddMailboxLastUid', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714090709_AddPasswordResetToken'
)
BEGIN
    CREATE TABLE [PasswordResetTokens] (
        [Id] INTEGER NOT NULL,
        [UserId] INTEGER NOT NULL,
        [Token] TEXT NOT NULL,
        [ExpiresAt] TEXT NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UsedAt] TEXT NULL,
        CONSTRAINT [PK_PasswordResetTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PasswordResetTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714090709_AddPasswordResetToken'
)
BEGIN
    CREATE INDEX [IX_PasswordResetTokens_UserId] ON [PasswordResetTokens] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714090709_AddPasswordResetToken'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260714090709_AddPasswordResetToken', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714092305_AddMailboxFetchFrequency'
)
BEGIN
    ALTER TABLE [Mailboxes] ADD [FetchFrequency] INTEGER NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714092305_AddMailboxFetchFrequency'
)
BEGIN
    ALTER TABLE [Mailboxes] ADD [LastFetchAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714092305_AddMailboxFetchFrequency'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260714092305_AddMailboxFetchFrequency', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714093747_AddTranslationsTable'
)
BEGIN
    CREATE TABLE [Translations] (
        [Id] INTEGER NOT NULL,
        [Key] TEXT NOT NULL,
        [Value] TEXT NOT NULL,
        [Culture] TEXT NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_Translations] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714093747_AddTranslationsTable'
)
BEGIN
    CREATE INDEX [IX_Translations_Culture] ON [Translations] ([Culture]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714093747_AddTranslationsTable'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Translations_Key_Culture] ON [Translations] ([Key], [Culture]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714093747_AddTranslationsTable'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260714093747_AddTranslationsTable', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715140528_AllowNullThreadIdForAttachments'
)
BEGIN
    DECLARE @var1 nvarchar(max);
    SELECT @var1 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Attachments]') AND [c].[name] = N'ThreadId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Attachments] DROP CONSTRAINT ' + @var1 + ';');
    ALTER TABLE [Attachments] ALTER COLUMN [ThreadId] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715140528_AllowNullThreadIdForAttachments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260715140528_AllowNullThreadIdForAttachments', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715141341_MakeCustomerAvatarNullable'
)
BEGIN
    DECLARE @var2 nvarchar(max);
    SELECT @var2 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Customers]') AND [c].[name] = N'Avatar');
    IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Customers] DROP CONSTRAINT ' + @var2 + ';');
    ALTER TABLE [Customers] ALTER COLUMN [Avatar] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260715141341_MakeCustomerAvatarNullable'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260715141341_MakeCustomerAvatarNullable', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717160725_AddStoredFileNameToAttachment'
)
BEGIN
    ALTER TABLE [Attachments] ADD [StoredFileName] TEXT NOT NULL DEFAULT '';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260717160725_AddStoredFileNameToAttachment'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260717160725_AddStoredFileNameToAttachment', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718001539_AddDeletedAtToMailbox'
)
BEGIN
    ALTER TABLE [Mailboxes] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718001539_AddDeletedAtToMailbox'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260718001539_AddDeletedAtToMailbox', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718003031_AddDeletedAtToUser'
)
BEGIN
    DROP INDEX [IX_Users_Email] ON [Users];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718003031_AddDeletedAtToUser'
)
BEGIN
    ALTER TABLE [Users] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718003031_AddDeletedAtToUser'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260718003031_AddDeletedAtToUser'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260718003031_AddDeletedAtToUser', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719033901_AddCustomerDeletedAt'
)
BEGIN
    ALTER TABLE [Customers] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719033901_AddCustomerDeletedAt'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260719033901_AddCustomerDeletedAt', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719040419_AddTagDeletedAt'
)
BEGIN
    ALTER TABLE [Tags] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719040419_AddTagDeletedAt'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260719040419_AddTagDeletedAt', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719042539_AddMailTemplateDeletedAt'
)
BEGIN
    ALTER TABLE [MailTemplates] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719042539_AddMailTemplateDeletedAt'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260719042539_AddMailTemplateDeletedAt', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719052233_AddConversationPriority'
)
BEGIN
    ALTER TABLE [Conversations] ADD [Priority] INTEGER NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719052233_AddConversationPriority'
)
BEGIN
    UPDATE Conversations SET Priority = 1 WHERE Priority = 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719052233_AddConversationPriority'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260719052233_AddConversationPriority', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719061100_AddDeletedAtToEmailSignature'
)
BEGIN
    ALTER TABLE [EmailSignatures] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719061100_AddDeletedAtToEmailSignature'
)
BEGIN
    ALTER TABLE [CannedResponses] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719061100_AddDeletedAtToEmailSignature'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260719061100_AddDeletedAtToEmailSignature', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719061124_AddDeletedAtToCannedResponse'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260719061124_AddDeletedAtToCannedResponse', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719071632_AddTeamAndRoleSoftDelete'
)
BEGIN
    DROP INDEX [IX_Roles_Slug] ON [Roles];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719071632_AddTeamAndRoleSoftDelete'
)
BEGIN
    ALTER TABLE [Teams] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719071632_AddTeamAndRoleSoftDelete'
)
BEGIN
    ALTER TABLE [Roles] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719071632_AddTeamAndRoleSoftDelete'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Roles_Slug] ON [Roles] ([Slug]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719071632_AddTeamAndRoleSoftDelete'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260719071632_AddTeamAndRoleSoftDelete', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719075321_AddSlaPolicySoftDelete'
)
BEGIN
    ALTER TABLE [SlaPolicies] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719075321_AddSlaPolicySoftDelete'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260719075321_AddSlaPolicySoftDelete', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719082440_AddMacroSoftDelete'
)
BEGIN
    ALTER TABLE [Macros] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719082440_AddMacroSoftDelete'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260719082440_AddMacroSoftDelete', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719083517_AddNotificationTrash'
)
BEGIN
    ALTER TABLE [NotificationTemplates] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719083517_AddNotificationTrash'
)
BEGIN
    ALTER TABLE [NotificationRules] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260719083517_AddNotificationTrash'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260719083517_AddNotificationTrash', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260720053614_AddWorkflowSoftDelete'
)
BEGIN
    ALTER TABLE [Workflows] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260720053614_AddWorkflowSoftDelete'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260720053614_AddWorkflowSoftDelete', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    DROP INDEX [IX_Mailboxes_Email] ON [Mailboxes];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE TABLE [CalendarEvents] (
        [Id] INTEGER NOT NULL,
        [Title] TEXT NOT NULL,
        [Description] TEXT NULL,
        [EventType] TEXT NOT NULL,
        [StartAt] TEXT NOT NULL,
        [EndAt] TEXT NULL,
        [IsAllDay] INTEGER NOT NULL,
        [ConversationId] INTEGER NULL,
        [MailboxId] INTEGER NULL,
        [UserId] INTEGER NULL,
        [Status] TEXT NOT NULL,
        [Color] TEXT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_CalendarEvents] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CalendarEvents_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]),
        CONSTRAINT [FK_CalendarEvents_Mailboxes_MailboxId] FOREIGN KEY ([MailboxId]) REFERENCES [Mailboxes] ([Id]),
        CONSTRAINT [FK_CalendarEvents_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE TABLE [ChatSessions] (
        [Id] INTEGER NOT NULL,
        [SessionToken] TEXT NOT NULL,
        [CustomerName] TEXT NOT NULL,
        [CustomerEmail] TEXT NOT NULL,
        [CustomerIp] TEXT NULL,
        [CustomerUserAgent] TEXT NULL,
        [CustomerId] INTEGER NULL,
        [MailboxId] INTEGER NULL,
        [UserId] INTEGER NULL,
        [ConversationId] INTEGER NULL,
        [Status] TEXT NOT NULL,
        [Subject] TEXT NULL,
        [CustomerLastSeenAt] TEXT NULL,
        [AgentLastSeenAt] TEXT NULL,
        [CustomerUnreadCount] INTEGER NOT NULL,
        [AgentUnreadCount] INTEGER NOT NULL,
        [Rating] INTEGER NULL,
        [RatingComment] TEXT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        [ClosedAt] TEXT NULL,
        CONSTRAINT [PK_ChatSessions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ChatSessions_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_ChatSessions_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_ChatSessions_Mailboxes_MailboxId] FOREIGN KEY ([MailboxId]) REFERENCES [Mailboxes] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_ChatSessions_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE TABLE [KnowledgeCategories] (
        [Id] INTEGER NOT NULL,
        [Name] TEXT NOT NULL,
        [Description] TEXT NULL,
        [ParentId] INTEGER NULL,
        [MailboxId] INTEGER NULL,
        [SortOrder] INTEGER NOT NULL,
        [Icon] TEXT NULL,
        [IsActive] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_KnowledgeCategories] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_KnowledgeCategories_KnowledgeCategories_ParentId] FOREIGN KEY ([ParentId]) REFERENCES [KnowledgeCategories] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE TABLE [RefreshTokens] (
        [Id] INTEGER NOT NULL,
        [Token] TEXT NOT NULL,
        [ExpiresAt] TEXT NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [CreatedByIp] TEXT NOT NULL,
        [RevokedAt] TEXT NULL,
        [ReplacedByToken] TEXT NULL,
        [ReasonRevoked] TEXT NULL,
        [UserId] INTEGER NOT NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_RefreshTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE TABLE [ChatMessages] (
        [Id] INTEGER NOT NULL,
        [SessionId] INTEGER NOT NULL,
        [SenderType] TEXT NOT NULL,
        [SenderId] INTEGER NULL,
        [SenderName] TEXT NOT NULL,
        [MessageType] TEXT NOT NULL,
        [Content] TEXT NOT NULL,
        [AttachmentUrl] TEXT NULL,
        [AttachmentName] TEXT NULL,
        [IsRead] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_ChatMessages] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ChatMessages_ChatSessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [ChatSessions] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE TABLE [KnowledgeArticles] (
        [Id] INTEGER NOT NULL,
        [Title] TEXT NOT NULL,
        [Content] TEXT NOT NULL,
        [Summary] TEXT NULL,
        [CategoryId] INTEGER NULL,
        [AuthorId] INTEGER NULL,
        [Tags] TEXT NULL,
        [Status] TEXT NOT NULL,
        [ViewCount] INTEGER NOT NULL,
        [LikeCount] INTEGER NOT NULL,
        [SortOrder] INTEGER NOT NULL,
        [IsPinned] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        [PublishedAt] TEXT NULL,
        CONSTRAINT [PK_KnowledgeArticles] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_KnowledgeArticles_KnowledgeCategories_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [KnowledgeCategories] ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Mailboxes_Email] ON [Mailboxes] ([Email]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_CalendarEvents_ConversationId] ON [CalendarEvents] ([ConversationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_CalendarEvents_MailboxId] ON [CalendarEvents] ([MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_CalendarEvents_StartAt] ON [CalendarEvents] ([StartAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_CalendarEvents_Status] ON [CalendarEvents] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_CalendarEvents_UserId] ON [CalendarEvents] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_ChatMessages_CreatedAt] ON [ChatMessages] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_ChatMessages_IsRead] ON [ChatMessages] ([IsRead]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_ChatMessages_SenderType] ON [ChatMessages] ([SenderType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_ChatMessages_SessionId] ON [ChatMessages] ([SessionId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_ChatSessions_ConversationId] ON [ChatSessions] ([ConversationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_ChatSessions_CreatedAt] ON [ChatSessions] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_ChatSessions_CustomerId] ON [ChatSessions] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_ChatSessions_MailboxId] ON [ChatSessions] ([MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ChatSessions_SessionToken] ON [ChatSessions] ([SessionToken]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_ChatSessions_Status] ON [ChatSessions] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_ChatSessions_UserId] ON [ChatSessions] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_KnowledgeArticles_AuthorId] ON [KnowledgeArticles] ([AuthorId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_KnowledgeArticles_CategoryId] ON [KnowledgeArticles] ([CategoryId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_KnowledgeArticles_Status] ON [KnowledgeArticles] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_KnowledgeCategories_MailboxId] ON [KnowledgeCategories] ([MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_KnowledgeCategories_ParentId] ON [KnowledgeCategories] ([ParentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE UNIQUE INDEX [IX_RefreshTokens_Token] ON [RefreshTokens] ([Token]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_UserId] ON [RefreshTokens] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_UserId_RevokedAt] ON [RefreshTokens] ([UserId], [RevokedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260721014420_AddRefreshTokens'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260721014420_AddRefreshTokens', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    DROP INDEX [IX_Followers_ConversationId] ON [Followers];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    DROP INDEX [IX_Conversations_CustomerId] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    DROP INDEX [IX_Conversations_FolderId] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    DROP INDEX [IX_Conversations_MailboxId] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    DROP INDEX [IX_Conversations_UserId] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    ALTER TABLE [Webhooks] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    ALTER TABLE [KnowledgeCategories] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    ALTER TABLE [KnowledgeArticles] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    ALTER TABLE [Folders] ADD [CreatedAt] TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    ALTER TABLE [Folders] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    ALTER TABLE [Folders] ADD [UpdatedAt] TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    ALTER TABLE [CustomFields] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    ALTER TABLE [Conversations] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    ALTER TABLE [ApiKeys] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    CREATE INDEX [IX_Threads_CreatedAt] ON [Threads] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    CREATE INDEX [IX_Threads_Type] ON [Threads] ([Type]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Followers_ConversationId_UserId] ON [Followers] ([ConversationId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_CreatedAt] ON [Conversations] ([CreatedAt]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_CustomerId] ON [Conversations] ([CustomerId]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_FolderId] ON [Conversations] ([FolderId]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_LastReplyAt] ON [Conversations] ([LastReplyAt]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_MailboxId_Status] ON [Conversations] ([MailboxId], [Status]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_MailboxId_Status_LastReplyAt] ON [Conversations] ([MailboxId], [Status], [LastReplyAt]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_MailboxId_UserId] ON [Conversations] ([MailboxId], [UserId]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_Priority] ON [Conversations] ([Priority]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_UserId] ON [Conversations] ([UserId]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260722034439_AddConversationAndThreadIndexes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260722034439_AddConversationAndThreadIndexes', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725020724_AddIsSystemToWorkflow'
)
BEGIN
    ALTER TABLE [Workflows] ADD [IsSystem] INTEGER NOT NULL DEFAULT CAST(0 AS INTEGER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725020724_AddIsSystemToWorkflow'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260725020724_AddIsSystemToWorkflow', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [AuditLogs] DROP CONSTRAINT [FK_AuditLogs_Users_UserId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [MacroExecutionLogs] DROP CONSTRAINT [FK_MacroExecutionLogs_Conversations_ConversationId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [SlaViolations] DROP CONSTRAINT [FK_SlaViolations_Conversations_ConversationId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [WorkflowExecutionLogs] DROP CONSTRAINT [FK_WorkflowExecutionLogs_Conversations_ConversationId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    DROP INDEX [IX_Conversations_CreatedAt] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    DROP INDEX [IX_Conversations_CustomerId] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    DROP INDEX [IX_Conversations_FolderId] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    DROP INDEX [IX_Conversations_LastReplyAt] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    DROP INDEX [IX_Conversations_MailboxId_Status] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    DROP INDEX [IX_Conversations_Priority] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    DROP INDEX [IX_Conversations_UserId] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [Workflows] ADD [IsSystem] INTEGER NOT NULL DEFAULT CAST(0 AS INTEGER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [Users] ADD [Avatar] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [Threads] ADD [Version] INTEGER NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [SatisfactionRatings] ADD [UpdatedAt] TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [NotificationRules] ADD [RecipientMailboxId] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [NotificationRules] ADD [RoleId] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [NotificationRules] ADD [TeamId] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [Mailboxes] ADD [Version] INTEGER NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [Conversations] ADD [SearchVector] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [Conversations] ADD [Version] INTEGER NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [IsRecurring] INTEGER NOT NULL DEFAULT CAST(0 AS INTEGER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [IsReminderSent] INTEGER NOT NULL DEFAULT CAST(0 AS INTEGER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [RecurrenceCount] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [RecurrenceEndDate] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [RecurrenceInterval] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [RecurrenceOriginalStart] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [RecurrenceParentId] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [RecurrenceType] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [ReminderMinutes] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    CREATE TABLE [UserNotifications] (
        [Id] INTEGER NOT NULL,
        [UserId] INTEGER NOT NULL,
        [Title] TEXT NOT NULL,
        [Message] TEXT NOT NULL,
        [Link] TEXT NULL,
        [Type] TEXT NOT NULL,
        [IsRead] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [ReadAt] TEXT NULL,
        CONSTRAINT [PK_UserNotifications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserNotifications_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    EXEC(N'ALTER TABLE [Users] ADD CONSTRAINT [CK_User_Role] CHECK ([Role] IN (1, 2, 3, 4))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    EXEC(N'ALTER TABLE [Threads] ADD CONSTRAINT [CK_ConversationThread_State] CHECK ([State] IN (1, 2, 3))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    EXEC(N'ALTER TABLE [SendLogs] ADD CONSTRAINT [CK_SendLog_Status] CHECK ([Status] IN (1, 2, 3, 4, 5))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    CREATE INDEX [IX_NotificationRules_RecipientMailboxId] ON [NotificationRules] ([RecipientMailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    CREATE INDEX [IX_NotificationRules_RoleId] ON [NotificationRules] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    CREATE INDEX [IX_NotificationRules_TeamId] ON [NotificationRules] ([TeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    CREATE INDEX [IX_Conversations_CustomerId] ON [Conversations] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_FolderId_Status] ON [Conversations] ([FolderId], [Status]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Conversations_Number] ON [Conversations] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_Status_Priority_LastReplyAt] ON [Conversations] ([Status], [Priority], [LastReplyAt]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    CREATE INDEX [IX_Conversations_UserId] ON [Conversations] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    EXEC(N'ALTER TABLE [Conversations] ADD CONSTRAINT [CK_Conversation_Priority] CHECK ([Priority] IN (0, 1, 2, 3))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    EXEC(N'ALTER TABLE [Conversations] ADD CONSTRAINT [CK_Conversation_State] CHECK ([State] IN (1, 2))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    EXEC(N'ALTER TABLE [Conversations] ADD CONSTRAINT [CK_Conversation_Status] CHECK ([Status] IN (1, 2, 3, 4))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    CREATE INDEX [IX_UserNotifications_UserId] ON [UserNotifications] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [AuditLogs] ADD CONSTRAINT [FK_AuditLogs_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [MacroExecutionLogs] ADD CONSTRAINT [FK_MacroExecutionLogs_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [NotificationRules] ADD CONSTRAINT [FK_NotificationRules_Mailboxes_RecipientMailboxId] FOREIGN KEY ([RecipientMailboxId]) REFERENCES [Mailboxes] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [NotificationRules] ADD CONSTRAINT [FK_NotificationRules_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [NotificationRules] ADD CONSTRAINT [FK_NotificationRules_Teams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [Teams] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [SlaViolations] ADD CONSTRAINT [FK_SlaViolations_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    ALTER TABLE [WorkflowExecutionLogs] ADD CONSTRAINT [FK_WorkflowExecutionLogs_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260726115230_AddPasswordResetTokenIndexes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260726115230_AddPasswordResetTokenIndexes', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [AuditLogs] DROP CONSTRAINT [FK_AuditLogs_Users_UserId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [MacroExecutionLogs] DROP CONSTRAINT [FK_MacroExecutionLogs_Conversations_ConversationId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [SlaViolations] DROP CONSTRAINT [FK_SlaViolations_Conversations_ConversationId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [WorkflowExecutionLogs] DROP CONSTRAINT [FK_WorkflowExecutionLogs_Conversations_ConversationId];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    DROP INDEX [IX_Tags_MailboxId_Name] ON [Tags];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    DROP INDEX [IX_MailTemplates_Slug] ON [MailTemplates];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    DROP INDEX [IX_Conversations_CreatedAt] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    DROP INDEX [IX_Conversations_CustomerId] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    DROP INDEX [IX_Conversations_FolderId] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    DROP INDEX [IX_Conversations_LastReplyAt] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    DROP INDEX [IX_Conversations_MailboxId_Status] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    DROP INDEX [IX_Conversations_Priority] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    DROP INDEX [IX_Conversations_UserId] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [Workflows] ADD [IsSystem] INTEGER NOT NULL DEFAULT CAST(0 AS INTEGER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [Avatar] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [BrowserOnAssignedToMe] INTEGER NOT NULL DEFAULT CAST(0 AS INTEGER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [BrowserOnMention] INTEGER NOT NULL DEFAULT CAST(0 AS INTEGER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [BrowserOnNewConversation] INTEGER NOT NULL DEFAULT CAST(0 AS INTEGER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [BrowserOnNewReply] INTEGER NOT NULL DEFAULT CAST(0 AS INTEGER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [DefaultSignature] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [EmailOnAssignedToMe] INTEGER NOT NULL DEFAULT CAST(0 AS INTEGER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [EmailOnMention] INTEGER NOT NULL DEFAULT CAST(0 AS INTEGER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [EmailOnNewConversation] INTEGER NOT NULL DEFAULT CAST(0 AS INTEGER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [EmailOnNewReply] INTEGER NOT NULL DEFAULT CAST(0 AS INTEGER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [FailedLoginAttempts] INTEGER NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [LockoutEnd] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [Users] ADD [UseGlobalSignature] INTEGER NOT NULL DEFAULT CAST(0 AS INTEGER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [SatisfactionRatings] ADD [UpdatedAt] TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [NotificationRules] ADD [RecipientMailboxId] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [NotificationRules] ADD [RoleId] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [NotificationRules] ADD [TeamId] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [DeletedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [IsRecurring] INTEGER NOT NULL DEFAULT CAST(0 AS INTEGER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [IsReminderSent] INTEGER NOT NULL DEFAULT CAST(0 AS INTEGER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [RecurrenceCount] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [RecurrenceEndDate] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [RecurrenceInterval] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [RecurrenceOriginalStart] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [RecurrenceParentId] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [RecurrenceType] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [ReminderMinutes] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    CREATE TABLE [UserNotifications] (
        [Id] INTEGER NOT NULL,
        [UserId] INTEGER NOT NULL,
        [Title] TEXT NOT NULL,
        [Message] TEXT NOT NULL,
        [Link] TEXT NULL,
        [Type] TEXT NOT NULL,
        [IsRead] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [ReadAt] TEXT NULL,
        CONSTRAINT [PK_UserNotifications] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_UserNotifications_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    EXEC(N'ALTER TABLE [Users] ADD CONSTRAINT [CK_User_Role] CHECK ([Role] IN (1, 2, 3, 4))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    CREATE INDEX [IX_Threads_ConversationId_CreatedAt] ON [Threads] ([ConversationId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    CREATE INDEX [IX_Threads_ConversationId_Type_CreatedAt] ON [Threads] ([ConversationId], [Type], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    CREATE INDEX [IX_Threads_Type_CreatedAt_UserId] ON [Threads] ([Type], [CreatedAt], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    EXEC(N'ALTER TABLE [Threads] ADD CONSTRAINT [CK_ConversationThread_State] CHECK ([State] IN (1, 2, 3))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Tags_MailboxId_Name] ON [Tags] ([MailboxId], [Name]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    CREATE INDEX [IX_SendLogs_MailboxId_CreatedAt] ON [SendLogs] ([MailboxId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    EXEC(N'ALTER TABLE [SendLogs] ADD CONSTRAINT [CK_SendLog_Status] CHECK ([Status] IN (1, 2, 3, 4, 5))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    CREATE INDEX [IX_PasswordResetTokens_ExpiresAt] ON [PasswordResetTokens] ([ExpiresAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PasswordResetTokens_Token] ON [PasswordResetTokens] ([Token]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    CREATE INDEX [IX_NotificationRules_RecipientMailboxId] ON [NotificationRules] ([RecipientMailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    CREATE INDEX [IX_NotificationRules_RoleId] ON [NotificationRules] ([RoleId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    CREATE INDEX [IX_NotificationRules_TeamId] ON [NotificationRules] ([TeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_MailTemplates_Slug] ON [MailTemplates] ([Slug]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_CustomerEmail] ON [Conversations] ([CustomerEmail]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    CREATE INDEX [IX_Conversations_CustomerId] ON [Conversations] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_FolderId_Status] ON [Conversations] ([FolderId], [Status]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_MailboxId_ClosedAt] ON [Conversations] ([MailboxId], [ClosedAt]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_MailboxId_CreatedAt] ON [Conversations] ([MailboxId], [CreatedAt]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Conversations_Number] ON [Conversations] ([Number]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_Status_Priority_LastReplyAt] ON [Conversations] ([Status], [Priority], [LastReplyAt]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_UserId_Status] ON [Conversations] ([UserId], [Status]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    EXEC(N'ALTER TABLE [Conversations] ADD CONSTRAINT [CK_Conversation_Priority] CHECK ([Priority] IN (0, 1, 2, 3))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    EXEC(N'ALTER TABLE [Conversations] ADD CONSTRAINT [CK_Conversation_State] CHECK ([State] IN (1, 2))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    EXEC(N'ALTER TABLE [Conversations] ADD CONSTRAINT [CK_Conversation_Status] CHECK ([Status] IN (1, 2, 3, 4))');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    CREATE INDEX [IX_UserNotifications_UserId] ON [UserNotifications] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [AuditLogs] ADD CONSTRAINT [FK_AuditLogs_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [MacroExecutionLogs] ADD CONSTRAINT [FK_MacroExecutionLogs_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [NotificationRules] ADD CONSTRAINT [FK_NotificationRules_Mailboxes_RecipientMailboxId] FOREIGN KEY ([RecipientMailboxId]) REFERENCES [Mailboxes] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [NotificationRules] ADD CONSTRAINT [FK_NotificationRules_Roles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [Roles] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [NotificationRules] ADD CONSTRAINT [FK_NotificationRules_Teams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [Teams] ([Id]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [SlaViolations] ADD CONSTRAINT [FK_SlaViolations_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    ALTER TABLE [WorkflowExecutionLogs] ADD CONSTRAINT [FK_WorkflowExecutionLogs_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728110945_AddUserNotificationSettings'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260728110945_AddUserNotificationSettings', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728134238_AddAuditLogHmacSignature'
)
BEGIN
    ALTER TABLE [AuditLogs] ADD [HmacSignature] TEXT NOT NULL DEFAULT '';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728134238_AddAuditLogHmacSignature'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Emails_EmailAddress] ON [Emails] ([EmailAddress]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260728134238_AddAuditLogHmacSignature'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260728134238_AddAuditLogHmacSignature', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260729093234_FixUniqueIndexSoftDelete'
)
BEGIN
    DROP INDEX IF EXISTS "IX_Stars_ConversationId"
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260729093234_FixUniqueIndexSoftDelete'
)
BEGIN
    DROP INDEX IF EXISTS "IX_Conversations_Number"
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260729093234_FixUniqueIndexSoftDelete'
)
BEGIN
    DROP INDEX IF EXISTS "IX_ApiKeys_Key"
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260729093234_FixUniqueIndexSoftDelete'
)
BEGIN
    CREATE INDEX IF NOT EXISTS "IX_Threads_MessageId" ON "Threads" ("MessageId")
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260729093234_FixUniqueIndexSoftDelete'
)
BEGIN
    CREATE UNIQUE INDEX IF NOT EXISTS "IX_Stars_ConversationId_UserId" ON "Stars" ("ConversationId", "UserId")
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260729093234_FixUniqueIndexSoftDelete'
)
BEGIN

    WITH duplicates AS (
        SELECT Id, ROW_NUMBER() OVER (ORDER BY Id) as rn,
               (SELECT COALESCE(MAX(Number), 0) FROM Conversations WHERE DeletedAt IS NULL) as max_num
        FROM Conversations
        WHERE DeletedAt IS NULL
        AND Id NOT IN (SELECT MIN(Id) FROM Conversations WHERE DeletedAt IS NULL GROUP BY Number HAVING COUNT(*) > 1)
        AND Number IN (SELECT Number FROM Conversations WHERE DeletedAt IS NULL GROUP BY Number HAVING COUNT(*) > 1)
    )
    UPDATE Conversations
    SET Number = (SELECT max_num + rn FROM duplicates WHERE duplicates.Id = Conversations.Id)
    WHERE Id IN (SELECT Id FROM duplicates)
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260729093234_FixUniqueIndexSoftDelete'
)
BEGIN
    CREATE UNIQUE INDEX IF NOT EXISTS "IX_Conversations_Number" ON "Conversations" ("Number") WHERE "DeletedAt" IS NULL
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260729093234_FixUniqueIndexSoftDelete'
)
BEGIN
    CREATE UNIQUE INDEX IF NOT EXISTS "IX_ApiKeys_Key" ON "ApiKeys" ("Key") WHERE "DeletedAt" IS NULL
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260729093234_FixUniqueIndexSoftDelete'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260729093234_FixUniqueIndexSoftDelete', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730194040_CalendarEventExtendedFields'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [AttendeesJson] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730194040_CalendarEventExtendedFields'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [CreatedBy] INTEGER NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730194040_CalendarEventExtendedFields'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [Location] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730194040_CalendarEventExtendedFields'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [MeetingUrl] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730194040_CalendarEventExtendedFields'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [Priority] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730194040_CalendarEventExtendedFields'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [Progress] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730194040_CalendarEventExtendedFields'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [RecurrenceExclusionsJson] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730194040_CalendarEventExtendedFields'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [RelatedEventId] INTEGER NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730194040_CalendarEventExtendedFields'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [SubTasksJson] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260730194040_CalendarEventExtendedFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260730194040_CalendarEventExtendedFields', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801020313_AddOutlookSyncEntities'
)
BEGIN
    CREATE TABLE [OutlookEventMappings] (
        [Id] INTEGER NOT NULL,
        [CalendarEventId] INTEGER NOT NULL,
        [OutlookEventId] TEXT NOT NULL,
        [UserId] INTEGER NOT NULL,
        [Source] TEXT NOT NULL DEFAULT 'local',
        [OutlookLastModified] TEXT NULL,
        [LocalLastModified] TEXT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_OutlookEventMappings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OutlookEventMappings_CalendarEvents_CalendarEventId] FOREIGN KEY ([CalendarEventId]) REFERENCES [CalendarEvents] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_OutlookEventMappings_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801020313_AddOutlookSyncEntities'
)
BEGIN
    CREATE TABLE [OutlookSyncSettings] (
        [Id] INTEGER NOT NULL,
        [UserId] INTEGER NOT NULL,
        [OutlookEmail] TEXT NULL,
        [OutlookUserId] TEXT NULL,
        [AccessToken] TEXT NULL,
        [RefreshToken] TEXT NULL,
        [TokenExpiresAt] TEXT NULL,
        [SyncEnabled] INTEGER NOT NULL,
        [SyncDirection] TEXT NOT NULL DEFAULT 'both',
        [LastSyncAt] TEXT NULL,
        [LastSyncStatus] TEXT NULL,
        [LastSyncError] TEXT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        [DeletedAt] TEXT NULL,
        CONSTRAINT [PK_OutlookSyncSettings] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_OutlookSyncSettings_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801020313_AddOutlookSyncEntities'
)
BEGIN
    CREATE UNIQUE INDEX [IX_OutlookEventMappings_CalendarEventId_UserId] ON [OutlookEventMappings] ([CalendarEventId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801020313_AddOutlookSyncEntities'
)
BEGIN
    CREATE INDEX [IX_OutlookEventMappings_OutlookEventId] ON [OutlookEventMappings] ([OutlookEventId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801020313_AddOutlookSyncEntities'
)
BEGIN
    CREATE INDEX [IX_OutlookEventMappings_Source] ON [OutlookEventMappings] ([Source]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801020313_AddOutlookSyncEntities'
)
BEGIN
    CREATE INDEX [IX_OutlookEventMappings_UserId] ON [OutlookEventMappings] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801020313_AddOutlookSyncEntities'
)
BEGIN
    CREATE INDEX [IX_OutlookSyncSettings_LastSyncAt] ON [OutlookSyncSettings] ([LastSyncAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801020313_AddOutlookSyncEntities'
)
BEGIN
    CREATE INDEX [IX_OutlookSyncSettings_SyncEnabled] ON [OutlookSyncSettings] ([SyncEnabled]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801020313_AddOutlookSyncEntities'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_OutlookSyncSettings_UserId] ON [OutlookSyncSettings] ([UserId]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801020313_AddOutlookSyncEntities'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260801020313_AddOutlookSyncEntities', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801055118_AddCalendarReminderEmails'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [LastReminderSentAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801055118_AddCalendarReminderEmails'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [ReminderEmails] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801055118_AddCalendarReminderEmails'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260801055118_AddCalendarReminderEmails', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801063734_ExtendChatMessageFields'
)
BEGIN
    ALTER TABLE [ChatMessages] ADD [EditedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801063734_ExtendChatMessageFields'
)
BEGIN
    ALTER TABLE [ChatMessages] ADD [IsRecalled] INTEGER NOT NULL DEFAULT CAST(0 AS INTEGER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801063734_ExtendChatMessageFields'
)
BEGIN
    ALTER TABLE [ChatMessages] ADD [ReadAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801063734_ExtendChatMessageFields'
)
BEGIN
    ALTER TABLE [ChatMessages] ADD [RecalledAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801063734_ExtendChatMessageFields'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260801063734_ExtendChatMessageFields', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801072221_AddCalendarEventRowVersion'
)
BEGIN
    ALTER TABLE [CalendarEvents] ADD [RowVersion] BLOB NOT NULL DEFAULT 0x;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801072221_AddCalendarEventRowVersion'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260801072221_AddCalendarEventRowVersion', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801152724_AddTimeEntry'
)
BEGIN
    DROP INDEX [IX_Conversations_CustomerId] ON [Conversations];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801152724_AddTimeEntry'
)
BEGIN
    CREATE TABLE [TimeEntries] (
        [Id] INTEGER NOT NULL,
        [EntryType] TEXT NOT NULL,
        [Description] TEXT NULL,
        [ConversationId] INTEGER NULL,
        [CustomerId] INTEGER NULL,
        [UserId] INTEGER NOT NULL,
        [MailboxId] INTEGER NULL,
        [StartTime] TEXT NOT NULL,
        [EndTime] TEXT NULL,
        [DurationMinutes] INTEGER NOT NULL,
        [Billable] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        [DeletedAt] TEXT NULL,
        [RowVersion] BLOB NULL,
        CONSTRAINT [PK_TimeEntries] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TimeEntries_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_TimeEntries_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_TimeEntries_Mailboxes_MailboxId] FOREIGN KEY ([MailboxId]) REFERENCES [Mailboxes] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_TimeEntries_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801152724_AddTimeEntry'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_CustomerId] ON [Conversations] ([CustomerId]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801152724_AddTimeEntry'
)
BEGIN
    CREATE INDEX [IX_TimeEntries_ConversationId] ON [TimeEntries] ([ConversationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801152724_AddTimeEntry'
)
BEGIN
    CREATE INDEX [IX_TimeEntries_CustomerId] ON [TimeEntries] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801152724_AddTimeEntry'
)
BEGIN
    CREATE INDEX [IX_TimeEntries_EndTime] ON [TimeEntries] ([EndTime]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801152724_AddTimeEntry'
)
BEGIN
    CREATE INDEX [IX_TimeEntries_MailboxId] ON [TimeEntries] ([MailboxId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801152724_AddTimeEntry'
)
BEGIN
    CREATE INDEX [IX_TimeEntries_StartTime] ON [TimeEntries] ([StartTime]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801152724_AddTimeEntry'
)
BEGIN
    CREATE INDEX [IX_TimeEntries_UserId] ON [TimeEntries] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801152724_AddTimeEntry'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260801152724_AddTimeEntry', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801161753_AddCustomReports'
)
BEGIN
    CREATE TABLE [CustomReports] (
        [Id] INTEGER NOT NULL,
        [Name] TEXT NOT NULL,
        [Description] TEXT NULL,
        [Category] TEXT NOT NULL,
        [Visibility] TEXT NOT NULL,
        [OwnerId] INTEGER NOT NULL,
        [TeamId] INTEGER NULL,
        [ReportConfig] TEXT NOT NULL,
        [IsDashboardPinned] INTEGER NOT NULL,
        [DashboardSortOrder] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        [DeletedAt] TEXT NULL,
        CONSTRAINT [PK_CustomReports] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CustomReports_Teams_TeamId] FOREIGN KEY ([TeamId]) REFERENCES [Teams] ([Id]) ON DELETE SET NULL,
        CONSTRAINT [FK_CustomReports_Users_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801161753_AddCustomReports'
)
BEGIN
    CREATE TABLE [CustomReportSchedules] (
        [Id] INTEGER NOT NULL,
        [CustomReportId] INTEGER NOT NULL,
        [Recurrence] TEXT NOT NULL,
        [WeekDay] INTEGER NULL,
        [SendTime] TEXT NOT NULL,
        [Recipients] TEXT NOT NULL,
        [Subject] TEXT NOT NULL,
        [AttachmentFormat] TEXT NOT NULL,
        [IsActive] INTEGER NOT NULL,
        [LastRunAt] TEXT NULL,
        [NextRunAt] TEXT NULL,
        [OwnerId] INTEGER NOT NULL,
        [CreatedAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        [DeletedAt] TEXT NULL,
        CONSTRAINT [PK_CustomReportSchedules] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_CustomReportSchedules_CustomReports_CustomReportId] FOREIGN KEY ([CustomReportId]) REFERENCES [CustomReports] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_CustomReportSchedules_Users_OwnerId] FOREIGN KEY ([OwnerId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801161753_AddCustomReports'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_CustomReports_IsDashboardPinned_DashboardSortOrder] ON [CustomReports] ([IsDashboardPinned], [DashboardSortOrder]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801161753_AddCustomReports'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_CustomReports_Name_OwnerId] ON [CustomReports] ([Name], [OwnerId]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801161753_AddCustomReports'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_CustomReports_OwnerId] ON [CustomReports] ([OwnerId]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801161753_AddCustomReports'
)
BEGIN
    CREATE INDEX [IX_CustomReports_TeamId] ON [CustomReports] ([TeamId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801161753_AddCustomReports'
)
BEGIN
    CREATE INDEX [IX_CustomReportSchedules_CustomReportId] ON [CustomReportSchedules] ([CustomReportId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801161753_AddCustomReports'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_CustomReportSchedules_IsActive_NextRunAt] ON [CustomReportSchedules] ([IsActive], [NextRunAt]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801161753_AddCustomReports'
)
BEGIN
    CREATE INDEX [IX_CustomReportSchedules_OwnerId] ON [CustomReportSchedules] ([OwnerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801161753_AddCustomReports'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260801161753_AddCustomReports', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801192456_AddWeChatAndModuleThreadModelChanges'
)
BEGIN
    ALTER TABLE [KnowledgeArticles] ADD CONSTRAINT [FK_KnowledgeArticles_Users_AuthorId] FOREIGN KEY ([AuthorId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260801192456_AddWeChatAndModuleThreadModelChanges'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260801192456_AddWeChatAndModuleThreadModelChanges', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806014707_AddConversationReadState'
)
BEGIN
    DROP INDEX [IX_NotificationRules_MailboxId] ON [NotificationRules];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806014707_AddConversationReadState'
)
BEGIN
    DROP INDEX [IX_NotificationRules_UserId] ON [NotificationRules];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806014707_AddConversationReadState'
)
BEGIN
    CREATE TABLE [ConversationReadStates] (
        [Id] INTEGER NOT NULL,
        [ConversationId] INTEGER NOT NULL,
        [UserId] INTEGER NOT NULL,
        [LastReadAt] TEXT NOT NULL,
        [UpdatedAt] TEXT NOT NULL,
        CONSTRAINT [PK_ConversationReadStates] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ConversationReadStates_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ConversationReadStates_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806014707_AddConversationReadState'
)
BEGIN
    CREATE INDEX [IX_NotificationRules_EventType_Active_Mailbox_User_Deleted] ON [NotificationRules] ([EventType], [IsActive], [MailboxId], [UserId], [DeletedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806014707_AddConversationReadState'
)
BEGIN
    CREATE INDEX [IX_NotificationRules_MailboxId_Active_Deleted] ON [NotificationRules] ([MailboxId], [IsActive], [DeletedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806014707_AddConversationReadState'
)
BEGIN
    CREATE INDEX [IX_NotificationRules_UserId_Active_Deleted] ON [NotificationRules] ([UserId], [IsActive], [DeletedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806014707_AddConversationReadState'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ConversationReadStates_ConversationId_UserId] ON [ConversationReadStates] ([ConversationId], [UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806014707_AddConversationReadState'
)
BEGIN
    CREATE INDEX [IX_ConversationReadStates_UserId] ON [ConversationReadStates] ([UserId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806014707_AddConversationReadState'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260806014707_AddConversationReadState', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806090243_AddCustomerContactExtractAttempts'
)
BEGIN
    ALTER TABLE [Customers] ADD [ContactExtractAttempts] INTEGER NOT NULL DEFAULT 0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260806090243_AddCustomerContactExtractAttempts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260806090243_AddCustomerContactExtractAttempts', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807074858_AddImMessageLog'
)
BEGIN
    CREATE TABLE [ImMessageLogs] (
        [Id] INTEGER NOT NULL,
        [ConversationId] INTEGER NOT NULL,
        [CustomerId] INTEGER NULL,
        [SourceType] INTEGER NOT NULL,
        [ChannelId] TEXT NOT NULL,
        [MessageText] TEXT NOT NULL,
        [Status] INTEGER NOT NULL DEFAULT 1,
        [AttemptCount] INTEGER NOT NULL DEFAULT 0,
        [MaxAttempts] INTEGER NOT NULL DEFAULT 4,
        [NextRetryAt] TEXT NULL,
        [ErrorMessage] TEXT NULL,
        [ExternalMessageId] TEXT NULL,
        [CreatedAt] TEXT NOT NULL,
        [SentAt] TEXT NULL,
        [UpdatedAt] TEXT NULL,
        CONSTRAINT [PK_ImMessageLogs] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_ImMessageLog_Status] CHECK ([Status] IN (1, 2, 3, 4)),
        CONSTRAINT [FK_ImMessageLogs_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_ImMessageLogs_Customers_CustomerId] FOREIGN KEY ([CustomerId]) REFERENCES [Customers] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807074858_AddImMessageLog'
)
BEGIN
    CREATE INDEX [IX_ImMessageLogs_ConversationId] ON [ImMessageLogs] ([ConversationId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807074858_AddImMessageLog'
)
BEGIN
    CREATE INDEX [IX_ImMessageLogs_CreatedAt] ON [ImMessageLogs] ([CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807074858_AddImMessageLog'
)
BEGIN
    CREATE INDEX [IX_ImMessageLogs_CustomerId] ON [ImMessageLogs] ([CustomerId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807074858_AddImMessageLog'
)
BEGIN
    CREATE INDEX [IX_ImMessageLogs_NextRetryAt] ON [ImMessageLogs] ([NextRetryAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807074858_AddImMessageLog'
)
BEGIN
    CREATE INDEX [IX_ImMessageLogs_SourceType] ON [ImMessageLogs] ([SourceType]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807074858_AddImMessageLog'
)
BEGIN
    CREATE INDEX [IX_ImMessageLogs_Status] ON [ImMessageLogs] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807074858_AddImMessageLog'
)
BEGIN
    CREATE INDEX [IX_ImMessageLogs_Status_NextRetryAt] ON [ImMessageLogs] ([Status], [NextRetryAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807074858_AddImMessageLog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260807074858_AddImMessageLog', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807145043_AddDistributedLockTable'
)
BEGIN
    EXEC(N'CREATE INDEX [IX_Conversations_SourceType_Status_LastReplyAt] ON [Conversations] ([SourceType], [Status], [LastReplyAt]) WHERE "DeletedAt" IS NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807145043_AddDistributedLockTable'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260807145043_AddDistributedLockTable', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807165854_AddCustomerPhoneToChatSession'
)
BEGIN
    ALTER TABLE [CustomFields] ADD [HideWhenChannelDisabled] INTEGER NOT NULL DEFAULT CAST(0 AS INTEGER);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807165854_AddCustomerPhoneToChatSession'
)
BEGIN
    ALTER TABLE [CustomFields] ADD [SourceTypesJson] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807165854_AddCustomerPhoneToChatSession'
)
BEGIN
    ALTER TABLE [ChatSessions] ADD [CustomerPhone] TEXT NOT NULL DEFAULT '';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807165854_AddCustomerPhoneToChatSession'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260807165854_AddCustomerPhoneToChatSession', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807183327_AddChatSessionRatingRequestedAtAndTimeoutAt'
)
BEGIN
    ALTER TABLE [ChatSessions] ADD [RatingRequestedAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807183327_AddChatSessionRatingRequestedAtAndTimeoutAt'
)
BEGIN
    ALTER TABLE [ChatSessions] ADD [RatingTimeoutAt] TEXT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260807183327_AddChatSessionRatingRequestedAtAndTimeoutAt'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260807183327_AddChatSessionRatingRequestedAtAndTimeoutAt', N'10.0.9');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260813162544_WorkflowLogConversationIdNullable'
)
BEGIN
    DECLARE @var3 nvarchar(max);
    SELECT @var3 = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[WorkflowExecutionLogs]') AND [c].[name] = N'ConversationId');
    IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [WorkflowExecutionLogs] DROP CONSTRAINT ' + @var3 + ';');
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

