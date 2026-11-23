CREATE TABLE "Customers" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Customers" PRIMARY KEY AUTOINCREMENT,
    "FirstName" TEXT NOT NULL,
    "LastName" TEXT NOT NULL,
    "Address" TEXT NULL,
    "City" TEXT NULL,
    "State" TEXT NULL,
    "Zip" TEXT NULL,
    "Country" TEXT NULL,
    "Company" TEXT NULL,
    "JobTitle" TEXT NULL,
    "Notes" TEXT NULL,
    "Channel" TEXT NULL,
    "ChannelId" TEXT NULL,
    "Phones" TEXT NOT NULL,
    "SocialProfiles" TEXT NOT NULL,
    "Websites" TEXT NOT NULL,
    "Avatar" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL
);


CREATE TABLE "KnowledgeCategories" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_KnowledgeCategories" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Description" TEXT NULL,
    "ParentId" INTEGER NULL,
    "MailboxId" INTEGER NULL,
    "SortOrder" INTEGER NOT NULL,
    "Icon" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_KnowledgeCategories_KnowledgeCategories_ParentId" FOREIGN KEY ("ParentId") REFERENCES "KnowledgeCategories" ("Id")
);


CREATE TABLE "Mailboxes" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Mailboxes" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Email" TEXT NOT NULL,
    "InProtocol" INTEGER NOT NULL,
    "InHost" TEXT NOT NULL,
    "InPort" INTEGER NOT NULL,
    "InEncryption" INTEGER NOT NULL,
    "InValidateCert" INTEGER NOT NULL,
    "InUsername" TEXT NOT NULL,
    "InPassword" TEXT NOT NULL,
    "ImapSentFolder" TEXT NOT NULL,
    "OutMethod" INTEGER NOT NULL,
    "OutHost" TEXT NOT NULL,
    "OutPort" INTEGER NOT NULL,
    "OutEncryption" INTEGER NOT NULL,
    "OutUsername" TEXT NOT NULL,
    "OutPassword" TEXT NOT NULL,
    "Aliases" TEXT NOT NULL,
    "AliasesReply" TEXT NOT NULL,
    "AutoBcc" TEXT NOT NULL,
    "AutoReplyEnabled" INTEGER NOT NULL,
    "AutoReplySubject" TEXT NOT NULL,
    "AutoReplyMessage" TEXT NOT NULL,
    "BeforeReply" TEXT NOT NULL,
    "Signature" TEXT NOT NULL,
    "OauthEnabled" INTEGER NOT NULL,
    "OauthProvider" TEXT NOT NULL,
    "OauthAccessToken" TEXT NOT NULL,
    "OauthRefreshToken" TEXT NOT NULL,
    "OauthTokenExpiresAt" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    "LastUid" INTEGER NOT NULL,
    "FetchFrequency" INTEGER NOT NULL,
    "LastFetchAt" TEXT NULL
);


CREATE TABLE "Modules" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Modules" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Title" TEXT NOT NULL,
    "Description" TEXT NULL,
    "Version" TEXT NOT NULL,
    "Author" TEXT NULL,
    "Icon" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "Category" TEXT NOT NULL,
    "Settings" TEXT NULL,
    "SortOrder" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL
);


CREATE TABLE "NotificationTemplates" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_NotificationTemplates" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Subject" TEXT NOT NULL,
    "Body" TEXT NOT NULL,
    "EventType" TEXT NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL
);


CREATE TABLE "Roles" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Roles" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Slug" TEXT NOT NULL,
    "Description" TEXT NULL,
    "PermissionsJson" TEXT NOT NULL,
    "IsSystem" INTEGER NOT NULL,
    "SortOrder" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL
);


CREATE TABLE "SystemOptions" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SystemOptions" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Value" TEXT NOT NULL,
    "Category" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL
);


CREATE TABLE "Translations" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Translations" PRIMARY KEY AUTOINCREMENT,
    "Key" TEXT NOT NULL,
    "Value" TEXT NOT NULL,
    "Culture" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL
);


CREATE TABLE "Users" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT,
    "FirstName" TEXT NOT NULL,
    "LastName" TEXT NOT NULL,
    "Email" TEXT NOT NULL,
    "Password" TEXT NOT NULL,
    "Role" INTEGER NOT NULL,
    "Status" INTEGER NOT NULL,
    "Timezone" TEXT NOT NULL,
    "Locale" TEXT NOT NULL,
    "Permissions" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL
);


CREATE TABLE "Emails" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Emails" PRIMARY KEY AUTOINCREMENT,
    "CustomerId" INTEGER NOT NULL,
    "EmailAddress" TEXT NOT NULL,
    "Type" INTEGER NOT NULL,
    CONSTRAINT "FK_Emails_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE CASCADE
);


CREATE TABLE "KnowledgeArticles" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_KnowledgeArticles" PRIMARY KEY AUTOINCREMENT,
    "Title" TEXT NOT NULL,
    "Content" TEXT NOT NULL,
    "Summary" TEXT NULL,
    "CategoryId" INTEGER NULL,
    "AuthorId" INTEGER NULL,
    "Tags" TEXT NULL,
    "Status" TEXT NOT NULL,
    "ViewCount" INTEGER NOT NULL,
    "LikeCount" INTEGER NOT NULL,
    "SortOrder" INTEGER NOT NULL,
    "IsPinned" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "PublishedAt" TEXT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_KnowledgeArticles_KnowledgeCategories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "KnowledgeCategories" ("Id")
);


CREATE TABLE "CustomFields" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CustomFields" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Type" TEXT NOT NULL,
    "EntityType" TEXT NOT NULL,
    "MailboxId" INTEGER NULL,
    "IsRequired" INTEGER NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "SortOrder" INTEGER NOT NULL,
    "OptionsJson" TEXT NULL,
    "DefaultValue" TEXT NULL,
    "Placeholder" TEXT NULL,
    "HelpText" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_CustomFields_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE CASCADE
);


CREATE TABLE "Macros" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Macros" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Description" TEXT NULL,
    "MailboxId" INTEGER NULL,
    "IsActive" INTEGER NOT NULL,
    "SortOrder" INTEGER NOT NULL,
    "ActionsJson" TEXT NOT NULL DEFAULT '[]',
    "UsageCount" INTEGER NOT NULL,
    "LastUsedAt" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_Macros_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE SET NULL
);


CREATE TABLE "MailTemplates" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_MailTemplates" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Slug" TEXT NOT NULL,
    "Type" INTEGER NOT NULL,
    "Subject" TEXT NOT NULL,
    "BodyHtml" TEXT NOT NULL,
    "BodyText" TEXT NOT NULL,
    "VariablesJson" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "IsSystem" INTEGER NOT NULL,
    "MailboxId" INTEGER NULL,
    "Description" TEXT NULL,
    "SortOrder" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_MailTemplates_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE SET NULL
);


CREATE TABLE "SlaPolicies" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SlaPolicies" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Description" TEXT NULL,
    "MailboxId" INTEGER NULL,
    "FirstResponseHours" REAL NOT NULL DEFAULT 24.0,
    "ResolutionHours" REAL NOT NULL DEFAULT 72.0,
    "BusinessHoursOnly" INTEGER NOT NULL,
    "Priority" INTEGER NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "SortOrder" INTEGER NOT NULL,
    "AppliedCount" INTEGER NOT NULL,
    "ViolationCount" INTEGER NOT NULL,
    "LastAppliedAt" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_SlaPolicies_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE SET NULL
);


CREATE TABLE "Tags" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Tags" PRIMARY KEY AUTOINCREMENT,
    "MailboxId" INTEGER NULL,
    "Name" TEXT NOT NULL,
    "Color" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_Tags_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id")
);


CREATE TABLE "Teams" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Teams" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Description" TEXT NULL,
    "MailboxId" INTEGER NULL,
    "IsActive" INTEGER NOT NULL,
    "SortOrder" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_Teams_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE SET NULL
);


CREATE TABLE "Webhooks" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Webhooks" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Url" TEXT NOT NULL,
    "Method" TEXT NOT NULL,
    "Events" TEXT NOT NULL,
    "HeadersJson" TEXT NULL,
    "Secret" TEXT NULL,
    "IsActive" INTEGER NOT NULL,
    "MailboxId" INTEGER NULL,
    "TriggerCount" INTEGER NOT NULL,
    "SuccessCount" INTEGER NOT NULL,
    "FailCount" INTEGER NOT NULL,
    "LastTriggeredAt" TEXT NULL,
    "LastError" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_Webhooks_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE CASCADE
);


CREATE TABLE "Workflows" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Workflows" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Description" TEXT NULL,
    "MailboxId" INTEGER NULL,
    "IsActive" INTEGER NOT NULL,
    "SortOrder" INTEGER NOT NULL,
    "TriggerType" TEXT NOT NULL,
    "ConditionsJson" TEXT NULL,
    "ActionsJson" TEXT NULL,
    "MatchCount" INTEGER NOT NULL,
    "LastMatchedAt" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_Workflows_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE SET NULL
);


CREATE TABLE "ApiKeys" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ApiKeys" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Key" TEXT NOT NULL,
    "Description" TEXT NULL,
    "UserId" INTEGER NULL,
    "Scopes" TEXT NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "ExpiresAt" TEXT NULL,
    "LastUsedAt" TEXT NULL,
    "LastUsedIp" TEXT NULL,
    "UsageCount" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_ApiKeys_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);


CREATE TABLE "AuditLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AuditLogs" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NULL,
    "Action" TEXT NOT NULL,
    "EntityType" TEXT NOT NULL,
    "EntityId" INTEGER NULL,
    "OldValues" TEXT NOT NULL,
    "NewValues" TEXT NOT NULL,
    "IpAddress" TEXT NOT NULL,
    "UserAgent" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_AuditLogs_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
);


CREATE TABLE "CannedResponses" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CannedResponses" PRIMARY KEY AUTOINCREMENT,
    "MailboxId" INTEGER NULL,
    "Name" TEXT NOT NULL,
    "Body" TEXT NOT NULL,
    "Category" TEXT NULL,
    "SortOrder" INTEGER NOT NULL,
    "IsShared" INTEGER NOT NULL,
    "CreatedByUserId" INTEGER NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_CannedResponses_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_CannedResponses_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);


CREATE TABLE "EmailSignatures" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_EmailSignatures" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "MailboxId" INTEGER NULL,
    "Name" TEXT NOT NULL,
    "Body" TEXT NOT NULL,
    "IsDefault" INTEGER NOT NULL,
    "SortOrder" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_EmailSignatures_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_EmailSignatures_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);


CREATE TABLE "Folders" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Folders" PRIMARY KEY AUTOINCREMENT,
    "MailboxId" INTEGER NOT NULL,
    "UserId" INTEGER NULL,
    "Type" INTEGER NOT NULL,
    "Name" TEXT NOT NULL,
    "ActiveCount" INTEGER NOT NULL,
    "TotalCount" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_Folders_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Folders_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
);


CREATE TABLE "MailboxUsers" (
    "MailboxId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Access" TEXT NOT NULL,
    CONSTRAINT "PK_MailboxUsers" PRIMARY KEY ("MailboxId", "UserId"),
    CONSTRAINT "FK_MailboxUsers_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_MailboxUsers_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);


CREATE TABLE "NotificationRules" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_NotificationRules" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "EventType" TEXT NOT NULL,
    "MailboxId" INTEGER NULL,
    "UserId" INTEGER NULL,
    "TemplateId" INTEGER NULL,
    "IsActive" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_NotificationRules_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id"),
    CONSTRAINT "FK_NotificationRules_NotificationTemplates_TemplateId" FOREIGN KEY ("TemplateId") REFERENCES "NotificationTemplates" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_NotificationRules_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
);


CREATE TABLE "PasswordResetTokens" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_PasswordResetTokens" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Token" TEXT NOT NULL,
    "ExpiresAt" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UsedAt" TEXT NULL,
    CONSTRAINT "FK_PasswordResetTokens_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);


CREATE TABLE "RefreshTokens" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_RefreshTokens" PRIMARY KEY AUTOINCREMENT,
    "Token" TEXT NOT NULL,
    "ExpiresAt" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "CreatedByIp" TEXT NOT NULL,
    "RevokedAt" TEXT NULL,
    "ReplacedByToken" TEXT NULL,
    "ReasonRevoked" TEXT NULL,
    "UserId" INTEGER NOT NULL,
    CONSTRAINT "FK_RefreshTokens_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);


CREATE TABLE "UserRoles" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserRoles" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "RoleId" INTEGER NOT NULL,
    "MailboxId" INTEGER NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_UserRoles_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_UserRoles_Roles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "Roles" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_UserRoles_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);


CREATE TABLE "CustomFieldValues" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CustomFieldValues" PRIMARY KEY AUTOINCREMENT,
    "CustomFieldId" INTEGER NOT NULL,
    "EntityId" INTEGER NOT NULL,
    "EntityType" TEXT NOT NULL,
    "Value" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_CustomFieldValues_CustomFields_CustomFieldId" FOREIGN KEY ("CustomFieldId") REFERENCES "CustomFields" ("Id") ON DELETE CASCADE
);


CREATE TABLE "TeamMembers" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_TeamMembers" PRIMARY KEY AUTOINCREMENT,
    "TeamId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Role" INTEGER NOT NULL,
    "JoinedAt" TEXT NOT NULL,
    CONSTRAINT "FK_TeamMembers_Teams_TeamId" FOREIGN KEY ("TeamId") REFERENCES "Teams" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_TeamMembers_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);


CREATE TABLE "WebhookLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WebhookLogs" PRIMARY KEY AUTOINCREMENT,
    "WebhookId" INTEGER NOT NULL,
    "EventType" TEXT NOT NULL,
    "EntityType" TEXT NULL,
    "EntityId" INTEGER NULL,
    "Url" TEXT NOT NULL,
    "Method" TEXT NOT NULL,
    "RequestBody" TEXT NULL,
    "ResponseStatusCode" INTEGER NULL,
    "ResponseBody" TEXT NULL,
    "IsSuccess" INTEGER NOT NULL,
    "ErrorMessage" TEXT NULL,
    "DurationMs" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_WebhookLogs_Webhooks_WebhookId" FOREIGN KEY ("WebhookId") REFERENCES "Webhooks" ("Id") ON DELETE CASCADE
);


CREATE TABLE "ApiKeyLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ApiKeyLogs" PRIMARY KEY AUTOINCREMENT,
    "ApiKeyId" INTEGER NOT NULL,
    "Endpoint" TEXT NOT NULL,
    "Method" TEXT NOT NULL,
    "StatusCode" INTEGER NOT NULL,
    "IpAddress" TEXT NULL,
    "UserAgent" TEXT NULL,
    "DurationMs" INTEGER NOT NULL,
    "RequestBody" TEXT NULL,
    "ErrorMessage" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_ApiKeyLogs_ApiKeys_ApiKeyId" FOREIGN KEY ("ApiKeyId") REFERENCES "ApiKeys" ("Id") ON DELETE CASCADE
);


CREATE TABLE "Conversations" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Conversations" PRIMARY KEY AUTOINCREMENT,
    "Number" INTEGER NOT NULL,
    "ThreadsCount" INTEGER NOT NULL,
    "MailboxId" INTEGER NOT NULL,
    "CustomerId" INTEGER NULL,
    "UserId" INTEGER NULL,
    "FolderId" INTEGER NULL,
    "Type" INTEGER NOT NULL,
    "SourceVia" INTEGER NOT NULL,
    "SourceType" INTEGER NOT NULL,
    "Status" INTEGER NOT NULL,
    "State" INTEGER NOT NULL,
    "Priority" INTEGER NOT NULL,
    "Subject" TEXT NOT NULL,
    "CustomerEmail" TEXT NOT NULL,
    "Cc" TEXT NOT NULL,
    "Bcc" TEXT NOT NULL,
    "Preview" TEXT NOT NULL,
    "HasAttachments" INTEGER NOT NULL,
    "Imported" INTEGER NOT NULL,
    "CreatedByUserId" INTEGER NULL,
    "CreatedByCustomerId" INTEGER NULL,
    "ClosedByUserId" INTEGER NULL,
    "LastReplyAt" TEXT NULL,
    "LastReplyFrom" INTEGER NULL,
    "UserUpdatedAt" TEXT NULL,
    "ClosedAt" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_Conversations_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Conversations_Folders_FolderId" FOREIGN KEY ("FolderId") REFERENCES "Folders" ("Id"),
    CONSTRAINT "FK_Conversations_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Conversations_Users_ClosedByUserId" FOREIGN KEY ("ClosedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Conversations_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Conversations_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);


CREATE TABLE "CalendarEvents" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CalendarEvents" PRIMARY KEY AUTOINCREMENT,
    "Title" TEXT NOT NULL,
    "Description" TEXT NULL,
    "EventType" TEXT NOT NULL,
    "StartAt" TEXT NOT NULL,
    "EndAt" TEXT NULL,
    "IsAllDay" INTEGER NOT NULL,
    "ConversationId" INTEGER NULL,
    "MailboxId" INTEGER NULL,
    "UserId" INTEGER NULL,
    "Status" TEXT NOT NULL,
    "Color" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_CalendarEvents_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id"),
    CONSTRAINT "FK_CalendarEvents_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id"),
    CONSTRAINT "FK_CalendarEvents_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
);


CREATE TABLE "ChatSessions" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ChatSessions" PRIMARY KEY AUTOINCREMENT,
    "SessionToken" TEXT NOT NULL,
    "CustomerName" TEXT NOT NULL,
    "CustomerEmail" TEXT NOT NULL,
    "CustomerIp" TEXT NULL,
    "CustomerUserAgent" TEXT NULL,
    "CustomerId" INTEGER NULL,
    "MailboxId" INTEGER NULL,
    "UserId" INTEGER NULL,
    "ConversationId" INTEGER NULL,
    "Status" TEXT NOT NULL,
    "Subject" TEXT NULL,
    "CustomerLastSeenAt" TEXT NULL,
    "AgentLastSeenAt" TEXT NULL,
    "CustomerUnreadCount" INTEGER NOT NULL,
    "AgentUnreadCount" INTEGER NOT NULL,
    "Rating" INTEGER NULL,
    "RatingComment" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "ClosedAt" TEXT NULL,
    CONSTRAINT "FK_ChatSessions_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_ChatSessions_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_ChatSessions_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_ChatSessions_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);


CREATE TABLE "ConversationFolders" (
    "ConversationId" INTEGER NOT NULL,
    "FolderId" INTEGER NOT NULL,
    CONSTRAINT "PK_ConversationFolders" PRIMARY KEY ("ConversationId", "FolderId"),
    CONSTRAINT "FK_ConversationFolders_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ConversationFolders_Folders_FolderId" FOREIGN KEY ("FolderId") REFERENCES "Folders" ("Id") ON DELETE CASCADE
);


CREATE TABLE "ConversationTags" (
    "ConversationId" INTEGER NOT NULL,
    "TagId" INTEGER NOT NULL,
    "Id" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "PK_ConversationTags" PRIMARY KEY ("ConversationId", "TagId"),
    CONSTRAINT "FK_ConversationTags_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ConversationTags_Tags_TagId" FOREIGN KEY ("TagId") REFERENCES "Tags" ("Id") ON DELETE CASCADE
);


CREATE TABLE "Followers" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Followers" PRIMARY KEY AUTOINCREMENT,
    "ConversationId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_Followers_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Followers_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);


CREATE TABLE "MacroExecutionLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_MacroExecutionLogs" PRIMARY KEY AUTOINCREMENT,
    "MacroId" INTEGER NOT NULL,
    "ConversationId" INTEGER NOT NULL,
    "ExecutedByUserId" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "ActionsExecuted" INTEGER NOT NULL,
    "ErrorMessage" TEXT NULL,
    "ExecutedAt" TEXT NOT NULL,
    CONSTRAINT "FK_MacroExecutionLogs_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_MacroExecutionLogs_Macros_MacroId" FOREIGN KEY ("MacroId") REFERENCES "Macros" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_MacroExecutionLogs_Users_ExecutedByUserId" FOREIGN KEY ("ExecutedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);


CREATE TABLE "SlaViolations" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SlaViolations" PRIMARY KEY AUTOINCREMENT,
    "SlaPolicyId" INTEGER NOT NULL,
    "ConversationId" INTEGER NOT NULL,
    "ViolationType" TEXT NOT NULL,
    "DueAt" TEXT NOT NULL,
    "DetectedAt" TEXT NOT NULL,
    "ResolvedAt" TEXT NULL,
    "Status" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_SlaViolations_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SlaViolations_SlaPolicies_SlaPolicyId" FOREIGN KEY ("SlaPolicyId") REFERENCES "SlaPolicies" ("Id") ON DELETE CASCADE
);


CREATE TABLE "Stars" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Stars" PRIMARY KEY AUTOINCREMENT,
    "ConversationId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_Stars_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Stars_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);


CREATE TABLE "Threads" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Threads" PRIMARY KEY AUTOINCREMENT,
    "ConversationId" INTEGER NOT NULL,
    "UserId" INTEGER NULL,
    "CustomerId" INTEGER NULL,
    "Type" INTEGER NOT NULL,
    "State" INTEGER NOT NULL,
    "ActionType" INTEGER NULL,
    "ActionData" TEXT NULL,
    "Body" TEXT NOT NULL,
    "Subject" TEXT NOT NULL,
    "From" TEXT NOT NULL,
    "To" TEXT NOT NULL,
    "Cc" TEXT NOT NULL,
    "Bcc" TEXT NOT NULL,
    "MessageId" TEXT NOT NULL,
    "Headers" TEXT NULL,
    "IsInternal" INTEGER NOT NULL,
    "HasAttachments" INTEGER NOT NULL,
    "IsFirst" INTEGER NOT NULL,
    "SourceVia" INTEGER NOT NULL,
    "SourceType" INTEGER NOT NULL,
    "CreatedByUserId" INTEGER NULL,
    "CreatedByCustomerId" INTEGER NULL,
    "SendStatus" INTEGER NULL,
    "OpenedAt" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_Threads_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Threads_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Threads_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Threads_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);


CREATE TABLE "WorkflowExecutionLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WorkflowExecutionLogs" PRIMARY KEY AUTOINCREMENT,
    "WorkflowId" INTEGER NOT NULL,
    "ConversationId" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "ActionsExecuted" INTEGER NOT NULL,
    "ErrorMessage" TEXT NULL,
    "ExecutedAt" TEXT NOT NULL,
    CONSTRAINT "FK_WorkflowExecutionLogs_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_WorkflowExecutionLogs_Workflows_WorkflowId" FOREIGN KEY ("WorkflowId") REFERENCES "Workflows" ("Id") ON DELETE CASCADE
);


CREATE TABLE "ChatMessages" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ChatMessages" PRIMARY KEY AUTOINCREMENT,
    "SessionId" INTEGER NOT NULL,
    "SenderType" TEXT NOT NULL,
    "SenderId" INTEGER NULL,
    "SenderName" TEXT NOT NULL,
    "MessageType" TEXT NOT NULL,
    "Content" TEXT NOT NULL,
    "AttachmentUrl" TEXT NULL,
    "AttachmentName" TEXT NULL,
    "IsRead" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_ChatMessages_ChatSessions_SessionId" FOREIGN KEY ("SessionId") REFERENCES "ChatSessions" ("Id") ON DELETE CASCADE
);


CREATE TABLE "Attachments" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Attachments" PRIMARY KEY AUTOINCREMENT,
    "ThreadId" INTEGER NULL,
    "UserId" INTEGER NULL,
    "FileName" TEXT NOT NULL,
    "StoredFileName" TEXT NOT NULL,
    "MimeType" TEXT NOT NULL,
    "Type" INTEGER NOT NULL,
    "FileDir" TEXT NOT NULL,
    "Size" INTEGER NOT NULL,
    "Embedded" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_Attachments_Threads_ThreadId" FOREIGN KEY ("ThreadId") REFERENCES "Threads" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Attachments_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
);


CREATE TABLE "SatisfactionRatings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SatisfactionRatings" PRIMARY KEY AUTOINCREMENT,
    "ConversationId" INTEGER NOT NULL,
    "ThreadId" INTEGER NULL,
    "CustomerId" INTEGER NOT NULL,
    "UserId" INTEGER NULL,
    "Rating" INTEGER NOT NULL,
    "Comment" TEXT NULL,
    "CustomerName" TEXT NULL,
    "CustomerEmail" TEXT NULL,
    "IpAddress" TEXT NULL,
    "UserAgent" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_SatisfactionRatings_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SatisfactionRatings_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_SatisfactionRatings_Threads_ThreadId" FOREIGN KEY ("ThreadId") REFERENCES "Threads" ("Id"),
    CONSTRAINT "FK_SatisfactionRatings_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);


CREATE TABLE "SendLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SendLogs" PRIMARY KEY AUTOINCREMENT,
    "ThreadId" INTEGER NULL,
    "MessageId" TEXT NOT NULL,
    "InReplyTo" TEXT NULL,
    "References" TEXT NULL,
    "Email" TEXT NOT NULL,
    "MailType" INTEGER NOT NULL,
    "Status" INTEGER NOT NULL,
    "CustomerId" INTEGER NULL,
    "UserId" INTEGER NULL,
    "CreatedAt" TEXT NOT NULL,
    "OpenCount" INTEGER NOT NULL,
    "FirstOpenedAt" TEXT NULL,
    "LastOpenedAt" TEXT NULL,
    "ErrorMessage" TEXT NULL,
    "SentAt" TEXT NULL,
    "AttemptCount" INTEGER NOT NULL,
    "AttachmentIds" TEXT NULL,
    "MailboxId" INTEGER NULL,
    "Cc" TEXT NULL,
    "Bcc" TEXT NULL,
    "Subject" TEXT NULL,
    "Body" TEXT NULL,
    CONSTRAINT "FK_SendLogs_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id"),
    CONSTRAINT "FK_SendLogs_Threads_ThreadId" FOREIGN KEY ("ThreadId") REFERENCES "Threads" ("Id"),
    CONSTRAINT "FK_SendLogs_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
);


CREATE INDEX "IX_ApiKeyLogs_ApiKeyId" ON "ApiKeyLogs" ("ApiKeyId");


CREATE INDEX "IX_ApiKeyLogs_CreatedAt" ON "ApiKeyLogs" ("CreatedAt");


CREATE INDEX "IX_ApiKeyLogs_Endpoint" ON "ApiKeyLogs" ("Endpoint");


CREATE INDEX "IX_ApiKeyLogs_StatusCode" ON "ApiKeyLogs" ("StatusCode");


CREATE INDEX "IX_ApiKeys_IsActive" ON "ApiKeys" ("IsActive");


-- API Key 唯一索引（部分唯一索引，过滤已软删除记录）
-- 与 Migration 20260729093234_FixUniqueIndexSoftDelete 保持一致
-- 软删除后 Key 可被新记录复用，避免软删除导致 Key 永久占用
CREATE UNIQUE INDEX "IX_ApiKeys_Key" ON "ApiKeys" ("Key") WHERE "DeletedAt" IS NULL;


CREATE INDEX "IX_ApiKeys_UserId" ON "ApiKeys" ("UserId");


CREATE INDEX "IX_Attachments_ThreadId" ON "Attachments" ("ThreadId");


CREATE INDEX "IX_Attachments_UserId" ON "Attachments" ("UserId");


CREATE INDEX "IX_AuditLogs_CreatedAt" ON "AuditLogs" ("CreatedAt");


CREATE INDEX "IX_AuditLogs_EntityType_EntityId" ON "AuditLogs" ("EntityType", "EntityId");


CREATE INDEX "IX_AuditLogs_UserId" ON "AuditLogs" ("UserId");


CREATE INDEX "IX_CalendarEvents_ConversationId" ON "CalendarEvents" ("ConversationId");


CREATE INDEX "IX_CalendarEvents_MailboxId" ON "CalendarEvents" ("MailboxId");


CREATE INDEX "IX_CalendarEvents_StartAt" ON "CalendarEvents" ("StartAt");


CREATE INDEX "IX_CalendarEvents_Status" ON "CalendarEvents" ("Status");


CREATE INDEX "IX_CalendarEvents_UserId" ON "CalendarEvents" ("UserId");


CREATE INDEX "IX_CannedResponses_Category" ON "CannedResponses" ("Category");


CREATE INDEX "IX_CannedResponses_CreatedByUserId" ON "CannedResponses" ("CreatedByUserId");


CREATE INDEX "IX_CannedResponses_IsShared" ON "CannedResponses" ("IsShared");


CREATE INDEX "IX_CannedResponses_MailboxId_Name" ON "CannedResponses" ("MailboxId", "Name");


CREATE INDEX "IX_ChatMessages_CreatedAt" ON "ChatMessages" ("CreatedAt");


CREATE INDEX "IX_ChatMessages_IsRead" ON "ChatMessages" ("IsRead");


CREATE INDEX "IX_ChatMessages_SenderType" ON "ChatMessages" ("SenderType");


CREATE INDEX "IX_ChatMessages_SessionId" ON "ChatMessages" ("SessionId");


CREATE INDEX "IX_ChatSessions_ConversationId" ON "ChatSessions" ("ConversationId");


CREATE INDEX "IX_ChatSessions_CreatedAt" ON "ChatSessions" ("CreatedAt");


CREATE INDEX "IX_ChatSessions_CustomerId" ON "ChatSessions" ("CustomerId");


CREATE INDEX "IX_ChatSessions_MailboxId" ON "ChatSessions" ("MailboxId");


CREATE UNIQUE INDEX "IX_ChatSessions_SessionToken" ON "ChatSessions" ("SessionToken");


CREATE INDEX "IX_ChatSessions_Status" ON "ChatSessions" ("Status");


CREATE INDEX "IX_ChatSessions_UserId" ON "ChatSessions" ("UserId");


CREATE INDEX "IX_ConversationFolders_FolderId" ON "ConversationFolders" ("FolderId");


CREATE INDEX "IX_Conversations_ClosedByUserId" ON "Conversations" ("ClosedByUserId");


CREATE INDEX "IX_Conversations_CreatedAt" ON "Conversations" ("CreatedAt") WHERE "DeletedAt" IS NULL;


CREATE INDEX "IX_Conversations_CreatedByUserId" ON "Conversations" ("CreatedByUserId");


CREATE INDEX "IX_Conversations_CustomerId" ON "Conversations" ("CustomerId") WHERE "DeletedAt" IS NULL;


CREATE INDEX "IX_Conversations_FolderId" ON "Conversations" ("FolderId") WHERE "DeletedAt" IS NULL;


CREATE INDEX "IX_Conversations_LastReplyAt" ON "Conversations" ("LastReplyAt") WHERE "DeletedAt" IS NULL;


CREATE INDEX "IX_Conversations_MailboxId_Status" ON "Conversations" ("MailboxId", "Status") WHERE "DeletedAt" IS NULL;


CREATE INDEX "IX_Conversations_MailboxId_Status_LastReplyAt" ON "Conversations" ("MailboxId", "Status", "LastReplyAt") WHERE "DeletedAt" IS NULL;


CREATE INDEX "IX_Conversations_MailboxId_UserId" ON "Conversations" ("MailboxId", "UserId") WHERE "DeletedAt" IS NULL;


-- 会话编号唯一索引（部分唯一索引，过滤已软删除记录）
-- 与 Migration 20260729093234_FixUniqueIndexSoftDelete 保持一致
-- 用于保证 Conversation.Number 在未删除记录中唯一，配合 DbContext.SaveChangesAsync
-- 的指数退避重试机制（50/100/200ms，最多 3 次）解决 Max(Number)+1 并发竞态
CREATE UNIQUE INDEX "IX_Conversations_Number" ON "Conversations" ("Number") WHERE "DeletedAt" IS NULL;


CREATE INDEX "IX_Conversations_Priority" ON "Conversations" ("Priority") WHERE "DeletedAt" IS NULL;


CREATE INDEX "IX_Conversations_UserId" ON "Conversations" ("UserId") WHERE "DeletedAt" IS NULL;


CREATE INDEX "IX_ConversationTags_TagId" ON "ConversationTags" ("TagId");


CREATE INDEX "IX_CustomFields_EntityType" ON "CustomFields" ("EntityType");


CREATE INDEX "IX_CustomFields_IsActive" ON "CustomFields" ("IsActive");


CREATE INDEX "IX_CustomFields_MailboxId" ON "CustomFields" ("MailboxId");


CREATE INDEX "IX_CustomFieldValues_CustomFieldId" ON "CustomFieldValues" ("CustomFieldId");


CREATE INDEX "IX_CustomFieldValues_EntityType_EntityId" ON "CustomFieldValues" ("EntityType", "EntityId");


CREATE INDEX "IX_Emails_CustomerId" ON "Emails" ("CustomerId");


CREATE INDEX "IX_EmailSignatures_IsDefault" ON "EmailSignatures" ("IsDefault");


CREATE INDEX "IX_EmailSignatures_MailboxId" ON "EmailSignatures" ("MailboxId");


CREATE INDEX "IX_EmailSignatures_UserId" ON "EmailSignatures" ("UserId");


CREATE INDEX "IX_Folders_MailboxId" ON "Folders" ("MailboxId");


CREATE INDEX "IX_Folders_UserId" ON "Folders" ("UserId");


CREATE UNIQUE INDEX "IX_Followers_ConversationId_UserId" ON "Followers" ("ConversationId", "UserId");


CREATE INDEX "IX_Followers_UserId" ON "Followers" ("UserId");


CREATE INDEX "IX_KnowledgeArticles_AuthorId" ON "KnowledgeArticles" ("AuthorId");


CREATE INDEX "IX_KnowledgeArticles_CategoryId" ON "KnowledgeArticles" ("CategoryId");


CREATE INDEX "IX_KnowledgeArticles_Status" ON "KnowledgeArticles" ("Status");


CREATE INDEX "IX_KnowledgeCategories_MailboxId" ON "KnowledgeCategories" ("MailboxId");


CREATE INDEX "IX_KnowledgeCategories_ParentId" ON "KnowledgeCategories" ("ParentId");


CREATE INDEX "IX_MacroExecutionLogs_ConversationId" ON "MacroExecutionLogs" ("ConversationId");


CREATE INDEX "IX_MacroExecutionLogs_ExecutedAt" ON "MacroExecutionLogs" ("ExecutedAt");


CREATE INDEX "IX_MacroExecutionLogs_ExecutedByUserId" ON "MacroExecutionLogs" ("ExecutedByUserId");


CREATE INDEX "IX_MacroExecutionLogs_MacroId" ON "MacroExecutionLogs" ("MacroId");


CREATE INDEX "IX_Macros_IsActive_MailboxId" ON "Macros" ("IsActive", "MailboxId");


CREATE INDEX "IX_Macros_MailboxId" ON "Macros" ("MailboxId");


CREATE INDEX "IX_Macros_SortOrder" ON "Macros" ("SortOrder");


CREATE INDEX "IX_Macros_UsageCount" ON "Macros" ("UsageCount");


CREATE UNIQUE INDEX "IX_Mailboxes_Email" ON "Mailboxes" ("Email") WHERE "DeletedAt" IS NULL;


CREATE INDEX "IX_MailboxUsers_UserId" ON "MailboxUsers" ("UserId");


CREATE INDEX "IX_MailTemplates_IsActive" ON "MailTemplates" ("IsActive");


CREATE INDEX "IX_MailTemplates_MailboxId" ON "MailTemplates" ("MailboxId");


CREATE UNIQUE INDEX "IX_MailTemplates_Slug" ON "MailTemplates" ("Slug");


CREATE INDEX "IX_MailTemplates_Type" ON "MailTemplates" ("Type");


CREATE INDEX "IX_Modules_Category" ON "Modules" ("Category");


CREATE INDEX "IX_Modules_IsActive" ON "Modules" ("IsActive");


CREATE UNIQUE INDEX "IX_Modules_Name" ON "Modules" ("Name");


CREATE INDEX "IX_Modules_SortOrder" ON "Modules" ("SortOrder");


CREATE INDEX "IX_NotificationRules_EventType" ON "NotificationRules" ("EventType");


CREATE INDEX "IX_NotificationRules_MailboxId" ON "NotificationRules" ("MailboxId");


CREATE INDEX "IX_NotificationRules_TemplateId" ON "NotificationRules" ("TemplateId");


CREATE INDEX "IX_NotificationRules_UserId" ON "NotificationRules" ("UserId");


CREATE INDEX "IX_NotificationTemplates_EventType" ON "NotificationTemplates" ("EventType");


CREATE INDEX "IX_PasswordResetTokens_UserId" ON "PasswordResetTokens" ("UserId");


CREATE UNIQUE INDEX "IX_RefreshTokens_Token" ON "RefreshTokens" ("Token");


CREATE INDEX "IX_RefreshTokens_UserId" ON "RefreshTokens" ("UserId");


CREATE INDEX "IX_RefreshTokens_UserId_RevokedAt" ON "RefreshTokens" ("UserId", "RevokedAt");


CREATE INDEX "IX_Roles_IsSystem" ON "Roles" ("IsSystem");


CREATE UNIQUE INDEX "IX_Roles_Slug" ON "Roles" ("Slug") WHERE "DeletedAt" IS NULL;


CREATE UNIQUE INDEX "IX_SatisfactionRatings_ConversationId" ON "SatisfactionRatings" ("ConversationId");


CREATE INDEX "IX_SatisfactionRatings_CreatedAt" ON "SatisfactionRatings" ("CreatedAt");


CREATE INDEX "IX_SatisfactionRatings_CustomerId" ON "SatisfactionRatings" ("CustomerId");


CREATE INDEX "IX_SatisfactionRatings_Rating" ON "SatisfactionRatings" ("Rating");


CREATE INDEX "IX_SatisfactionRatings_ThreadId" ON "SatisfactionRatings" ("ThreadId");


CREATE INDEX "IX_SatisfactionRatings_UserId" ON "SatisfactionRatings" ("UserId");


CREATE INDEX "IX_SendLogs_CreatedAt" ON "SendLogs" ("CreatedAt");


CREATE INDEX "IX_SendLogs_CustomerId" ON "SendLogs" ("CustomerId");


CREATE INDEX "IX_SendLogs_Email" ON "SendLogs" ("Email");


CREATE INDEX "IX_SendLogs_MailboxId" ON "SendLogs" ("MailboxId");


CREATE INDEX "IX_SendLogs_MailType" ON "SendLogs" ("MailType");


CREATE INDEX "IX_SendLogs_MessageId" ON "SendLogs" ("MessageId");


CREATE INDEX "IX_SendLogs_Status" ON "SendLogs" ("Status");


CREATE INDEX "IX_SendLogs_ThreadId" ON "SendLogs" ("ThreadId");


CREATE INDEX "IX_SendLogs_UserId" ON "SendLogs" ("UserId");


CREATE INDEX "IX_SlaPolicies_IsActive_MailboxId" ON "SlaPolicies" ("IsActive", "MailboxId");


CREATE INDEX "IX_SlaPolicies_MailboxId" ON "SlaPolicies" ("MailboxId");


CREATE INDEX "IX_SlaPolicies_Priority" ON "SlaPolicies" ("Priority");


CREATE INDEX "IX_SlaPolicies_SortOrder" ON "SlaPolicies" ("SortOrder");


CREATE INDEX "IX_SlaViolations_ConversationId" ON "SlaViolations" ("ConversationId");


CREATE INDEX "IX_SlaViolations_ConversationId_ViolationType" ON "SlaViolations" ("ConversationId", "ViolationType");


CREATE INDEX "IX_SlaViolations_DueAt" ON "SlaViolations" ("DueAt");


CREATE INDEX "IX_SlaViolations_SlaPolicyId" ON "SlaViolations" ("SlaPolicyId");


CREATE INDEX "IX_SlaViolations_Status" ON "SlaViolations" ("Status");


-- Stars 联合唯一索引：防止同一用户对同一会话重复点赞
-- 与 Migration 20260729093234_FixUniqueIndexSoftDelete 保持一致
-- 联合索引最左前缀可覆盖按 ConversationId 的查询，无需再建单列索引
CREATE UNIQUE INDEX "IX_Stars_ConversationId_UserId" ON "Stars" ("ConversationId", "UserId");


CREATE INDEX "IX_Stars_UserId" ON "Stars" ("UserId");


CREATE UNIQUE INDEX "IX_SystemOptions_Name" ON "SystemOptions" ("Name");


CREATE UNIQUE INDEX "IX_Tags_MailboxId_Name" ON "Tags" ("MailboxId", "Name");


CREATE INDEX "IX_TeamMembers_TeamId" ON "TeamMembers" ("TeamId");


CREATE UNIQUE INDEX "IX_TeamMembers_TeamId_UserId" ON "TeamMembers" ("TeamId", "UserId");


CREATE INDEX "IX_TeamMembers_UserId" ON "TeamMembers" ("UserId");


CREATE INDEX "IX_Teams_IsActive" ON "Teams" ("IsActive");


CREATE INDEX "IX_Teams_MailboxId" ON "Teams" ("MailboxId");


CREATE INDEX "IX_Teams_Name" ON "Teams" ("Name");


CREATE INDEX "IX_Threads_ConversationId" ON "Threads" ("ConversationId");


CREATE INDEX "IX_Threads_CreatedAt" ON "Threads" ("CreatedAt");


CREATE INDEX "IX_Threads_CreatedByUserId" ON "Threads" ("CreatedByUserId");


CREATE INDEX "IX_Threads_CustomerId" ON "Threads" ("CustomerId");


CREATE INDEX "IX_Threads_Type" ON "Threads" ("Type");


CREATE INDEX "IX_Threads_UserId" ON "Threads" ("UserId");

-- 邮件去重和回复匹配的核心索引（同步自 2026_07_14 迁移）
CREATE INDEX "IX_Threads_MessageId" ON "Threads" ("MessageId");


CREATE INDEX "IX_Translations_Culture" ON "Translations" ("Culture");


CREATE UNIQUE INDEX "IX_Translations_Key_Culture" ON "Translations" ("Key", "Culture");


CREATE INDEX "IX_UserRoles_MailboxId" ON "UserRoles" ("MailboxId");


CREATE INDEX "IX_UserRoles_RoleId" ON "UserRoles" ("RoleId");


CREATE INDEX "IX_UserRoles_UserId" ON "UserRoles" ("UserId");


CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email") WHERE "DeletedAt" IS NULL;


CREATE INDEX "IX_WebhookLogs_CreatedAt" ON "WebhookLogs" ("CreatedAt");


CREATE INDEX "IX_WebhookLogs_EventType" ON "WebhookLogs" ("EventType");


CREATE INDEX "IX_WebhookLogs_WebhookId" ON "WebhookLogs" ("WebhookId");


CREATE INDEX "IX_Webhooks_IsActive" ON "Webhooks" ("IsActive");


CREATE INDEX "IX_Webhooks_MailboxId" ON "Webhooks" ("MailboxId");


CREATE INDEX "IX_WorkflowExecutionLogs_ConversationId" ON "WorkflowExecutionLogs" ("ConversationId");


CREATE INDEX "IX_WorkflowExecutionLogs_ExecutedAt" ON "WorkflowExecutionLogs" ("ExecutedAt");


CREATE INDEX "IX_WorkflowExecutionLogs_WorkflowId" ON "WorkflowExecutionLogs" ("WorkflowId");


CREATE INDEX "IX_Workflows_IsActive_TriggerType" ON "Workflows" ("IsActive", "TriggerType");


CREATE INDEX "IX_Workflows_MailboxId" ON "Workflows" ("MailboxId");


CREATE INDEX "IX_Workflows_SortOrder" ON "Workflows" ("SortOrder");


