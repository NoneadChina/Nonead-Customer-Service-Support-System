CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
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
    "Avatar" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL
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
    "UpdatedAt" TEXT NOT NULL
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
    "UpdatedAt" TEXT NOT NULL
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
    "UpdatedAt" TEXT NOT NULL
);

CREATE TABLE "SystemOptions" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SystemOptions" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Value" TEXT NOT NULL,
    "Category" TEXT NULL,
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
    "UpdatedAt" TEXT NOT NULL
);

CREATE TABLE "Emails" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Emails" PRIMARY KEY AUTOINCREMENT,
    "CustomerId" INTEGER NOT NULL,
    "EmailAddress" TEXT NOT NULL,
    "Type" INTEGER NOT NULL,
    CONSTRAINT "FK_Emails_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE CASCADE
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
    CONSTRAINT "FK_SlaPolicies_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE SET NULL
);

CREATE TABLE "Tags" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Tags" PRIMARY KEY AUTOINCREMENT,
    "MailboxId" INTEGER NULL,
    "Name" TEXT NOT NULL,
    "Color" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
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
    CONSTRAINT "FK_NotificationRules_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id"),
    CONSTRAINT "FK_NotificationRules_NotificationTemplates_TemplateId" FOREIGN KEY ("TemplateId") REFERENCES "NotificationTemplates" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_NotificationRules_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
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
    CONSTRAINT "FK_Conversations_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Conversations_Folders_FolderId" FOREIGN KEY ("FolderId") REFERENCES "Folders" ("Id"),
    CONSTRAINT "FK_Conversations_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Conversations_Users_ClosedByUserId" FOREIGN KEY ("ClosedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Conversations_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Conversations_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
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

CREATE TABLE "Attachments" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Attachments" PRIMARY KEY AUTOINCREMENT,
    "ThreadId" INTEGER NOT NULL,
    "UserId" INTEGER NULL,
    "FileName" TEXT NOT NULL,
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
    "MailboxId" INTEGER NOT NULL,
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

CREATE UNIQUE INDEX "IX_ApiKeys_Key" ON "ApiKeys" ("Key");

CREATE INDEX "IX_ApiKeys_UserId" ON "ApiKeys" ("UserId");

CREATE INDEX "IX_Attachments_ThreadId" ON "Attachments" ("ThreadId");

CREATE INDEX "IX_Attachments_UserId" ON "Attachments" ("UserId");

CREATE INDEX "IX_AuditLogs_CreatedAt" ON "AuditLogs" ("CreatedAt");

CREATE INDEX "IX_AuditLogs_EntityType_EntityId" ON "AuditLogs" ("EntityType", "EntityId");

CREATE INDEX "IX_AuditLogs_UserId" ON "AuditLogs" ("UserId");

CREATE INDEX "IX_CannedResponses_Category" ON "CannedResponses" ("Category");

CREATE INDEX "IX_CannedResponses_CreatedByUserId" ON "CannedResponses" ("CreatedByUserId");

CREATE INDEX "IX_CannedResponses_IsShared" ON "CannedResponses" ("IsShared");

CREATE INDEX "IX_CannedResponses_MailboxId_Name" ON "CannedResponses" ("MailboxId", "Name");

CREATE INDEX "IX_ConversationFolders_FolderId" ON "ConversationFolders" ("FolderId");

CREATE INDEX "IX_Conversations_ClosedByUserId" ON "Conversations" ("ClosedByUserId");

CREATE INDEX "IX_Conversations_CreatedByUserId" ON "Conversations" ("CreatedByUserId");

CREATE INDEX "IX_Conversations_CustomerId" ON "Conversations" ("CustomerId");

CREATE INDEX "IX_Conversations_FolderId" ON "Conversations" ("FolderId");

CREATE INDEX "IX_Conversations_MailboxId" ON "Conversations" ("MailboxId");

CREATE INDEX "IX_Conversations_UserId" ON "Conversations" ("UserId");

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

CREATE INDEX "IX_Followers_ConversationId" ON "Followers" ("ConversationId");

CREATE INDEX "IX_Followers_UserId" ON "Followers" ("UserId");

CREATE INDEX "IX_MacroExecutionLogs_ConversationId" ON "MacroExecutionLogs" ("ConversationId");

CREATE INDEX "IX_MacroExecutionLogs_ExecutedAt" ON "MacroExecutionLogs" ("ExecutedAt");

CREATE INDEX "IX_MacroExecutionLogs_ExecutedByUserId" ON "MacroExecutionLogs" ("ExecutedByUserId");

CREATE INDEX "IX_MacroExecutionLogs_MacroId" ON "MacroExecutionLogs" ("MacroId");

CREATE INDEX "IX_Macros_IsActive_MailboxId" ON "Macros" ("IsActive", "MailboxId");

CREATE INDEX "IX_Macros_MailboxId" ON "Macros" ("MailboxId");

CREATE INDEX "IX_Macros_SortOrder" ON "Macros" ("SortOrder");

CREATE INDEX "IX_Macros_UsageCount" ON "Macros" ("UsageCount");

CREATE UNIQUE INDEX "IX_Mailboxes_Email" ON "Mailboxes" ("Email");

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

CREATE INDEX "IX_Roles_IsSystem" ON "Roles" ("IsSystem");

CREATE UNIQUE INDEX "IX_Roles_Slug" ON "Roles" ("Slug");

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

CREATE INDEX "IX_Stars_ConversationId" ON "Stars" ("ConversationId");

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

CREATE INDEX "IX_Threads_CreatedByUserId" ON "Threads" ("CreatedByUserId");

CREATE INDEX "IX_Threads_CustomerId" ON "Threads" ("CustomerId");

CREATE INDEX "IX_Threads_UserId" ON "Threads" ("UserId");

CREATE INDEX "IX_UserRoles_MailboxId" ON "UserRoles" ("MailboxId");

CREATE INDEX "IX_UserRoles_RoleId" ON "UserRoles" ("RoleId");

CREATE INDEX "IX_UserRoles_UserId" ON "UserRoles" ("UserId");

CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");

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

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260714064654_FixSendLogAttachmentIds', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "Mailboxes" ADD "LastUid" INTEGER NOT NULL DEFAULT 0;

CREATE TABLE "ef_temp_SendLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SendLogs" PRIMARY KEY AUTOINCREMENT,
    "AttachmentIds" TEXT NULL,
    "AttemptCount" INTEGER NOT NULL,
    "Bcc" TEXT NULL,
    "Body" TEXT NULL,
    "Cc" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "CustomerId" INTEGER NULL,
    "Email" TEXT NOT NULL,
    "ErrorMessage" TEXT NULL,
    "FirstOpenedAt" TEXT NULL,
    "LastOpenedAt" TEXT NULL,
    "MailType" INTEGER NOT NULL,
    "MailboxId" INTEGER NULL,
    "MessageId" TEXT NOT NULL,
    "OpenCount" INTEGER NOT NULL,
    "SentAt" TEXT NULL,
    "Status" INTEGER NOT NULL,
    "Subject" TEXT NULL,
    "ThreadId" INTEGER NULL,
    "UserId" INTEGER NULL,
    CONSTRAINT "FK_SendLogs_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id"),
    CONSTRAINT "FK_SendLogs_Threads_ThreadId" FOREIGN KEY ("ThreadId") REFERENCES "Threads" ("Id"),
    CONSTRAINT "FK_SendLogs_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
);

INSERT INTO "ef_temp_SendLogs" ("Id", "AttachmentIds", "AttemptCount", "Bcc", "Body", "Cc", "CreatedAt", "CustomerId", "Email", "ErrorMessage", "FirstOpenedAt", "LastOpenedAt", "MailType", "MailboxId", "MessageId", "OpenCount", "SentAt", "Status", "Subject", "ThreadId", "UserId")
SELECT "Id", "AttachmentIds", "AttemptCount", "Bcc", "Body", "Cc", "CreatedAt", "CustomerId", "Email", "ErrorMessage", "FirstOpenedAt", "LastOpenedAt", "MailType", "MailboxId", "MessageId", "OpenCount", "SentAt", "Status", "Subject", "ThreadId", "UserId"
FROM "SendLogs";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "SendLogs";

ALTER TABLE "ef_temp_SendLogs" RENAME TO "SendLogs";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE INDEX "IX_SendLogs_CreatedAt" ON "SendLogs" ("CreatedAt");

CREATE INDEX "IX_SendLogs_CustomerId" ON "SendLogs" ("CustomerId");

CREATE INDEX "IX_SendLogs_Email" ON "SendLogs" ("Email");

CREATE INDEX "IX_SendLogs_MailboxId" ON "SendLogs" ("MailboxId");

CREATE INDEX "IX_SendLogs_MailType" ON "SendLogs" ("MailType");

CREATE INDEX "IX_SendLogs_MessageId" ON "SendLogs" ("MessageId");

CREATE INDEX "IX_SendLogs_Status" ON "SendLogs" ("Status");

CREATE INDEX "IX_SendLogs_ThreadId" ON "SendLogs" ("ThreadId");

CREATE INDEX "IX_SendLogs_UserId" ON "SendLogs" ("UserId");

COMMIT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260714081440_AddMailboxLastUid', '10.0.9');

BEGIN TRANSACTION;
CREATE TABLE "PasswordResetTokens" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_PasswordResetTokens" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Token" TEXT NOT NULL,
    "ExpiresAt" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UsedAt" TEXT NULL,
    CONSTRAINT "FK_PasswordResetTokens_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_PasswordResetTokens_UserId" ON "PasswordResetTokens" ("UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260714090709_AddPasswordResetToken', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "Mailboxes" ADD "FetchFrequency" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "Mailboxes" ADD "LastFetchAt" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260714092305_AddMailboxFetchFrequency', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "Translations" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Translations" PRIMARY KEY AUTOINCREMENT,
    "Key" TEXT NOT NULL,
    "Value" TEXT NOT NULL,
    "Culture" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL
);

CREATE INDEX "IX_Translations_Culture" ON "Translations" ("Culture");

CREATE UNIQUE INDEX "IX_Translations_Key_Culture" ON "Translations" ("Key", "Culture");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260714093747_AddTranslationsTable', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "ef_temp_Attachments" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Attachments" PRIMARY KEY AUTOINCREMENT,
    "CreatedAt" TEXT NOT NULL,
    "Embedded" INTEGER NOT NULL,
    "FileDir" TEXT NOT NULL,
    "FileName" TEXT NOT NULL,
    "MimeType" TEXT NOT NULL,
    "Size" INTEGER NOT NULL,
    "ThreadId" INTEGER NULL,
    "Type" INTEGER NOT NULL,
    "UserId" INTEGER NULL,
    CONSTRAINT "FK_Attachments_Threads_ThreadId" FOREIGN KEY ("ThreadId") REFERENCES "Threads" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Attachments_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
);

INSERT INTO "ef_temp_Attachments" ("Id", "CreatedAt", "Embedded", "FileDir", "FileName", "MimeType", "Size", "ThreadId", "Type", "UserId")
SELECT "Id", "CreatedAt", "Embedded", "FileDir", "FileName", "MimeType", "Size", "ThreadId", "Type", "UserId"
FROM "Attachments";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "Attachments";

ALTER TABLE "ef_temp_Attachments" RENAME TO "Attachments";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE INDEX "IX_Attachments_ThreadId" ON "Attachments" ("ThreadId");

CREATE INDEX "IX_Attachments_UserId" ON "Attachments" ("UserId");

COMMIT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260715140528_AllowNullThreadIdForAttachments', '10.0.9');

BEGIN TRANSACTION;
CREATE TABLE "ef_temp_Customers" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Customers" PRIMARY KEY AUTOINCREMENT,
    "Address" TEXT NULL,
    "Avatar" TEXT NULL,
    "Channel" TEXT NULL,
    "ChannelId" TEXT NULL,
    "City" TEXT NULL,
    "Company" TEXT NULL,
    "Country" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "FirstName" TEXT NOT NULL,
    "JobTitle" TEXT NULL,
    "LastName" TEXT NOT NULL,
    "Notes" TEXT NULL,
    "Phones" TEXT NOT NULL,
    "SocialProfiles" TEXT NOT NULL,
    "State" TEXT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "Websites" TEXT NOT NULL,
    "Zip" TEXT NULL
);

INSERT INTO "ef_temp_Customers" ("Id", "Address", "Avatar", "Channel", "ChannelId", "City", "Company", "Country", "CreatedAt", "FirstName", "JobTitle", "LastName", "Notes", "Phones", "SocialProfiles", "State", "UpdatedAt", "Websites", "Zip")
SELECT "Id", "Address", "Avatar", "Channel", "ChannelId", "City", "Company", "Country", "CreatedAt", "FirstName", "JobTitle", "LastName", "Notes", "Phones", "SocialProfiles", "State", "UpdatedAt", "Websites", "Zip"
FROM "Customers";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "Customers";

ALTER TABLE "ef_temp_Customers" RENAME TO "Customers";

COMMIT;

PRAGMA foreign_keys = 1;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260715141341_MakeCustomerAvatarNullable', '10.0.9');

BEGIN TRANSACTION;
ALTER TABLE "Attachments" ADD "StoredFileName" TEXT NOT NULL DEFAULT '';

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260717160725_AddStoredFileNameToAttachment', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "Mailboxes" ADD "DeletedAt" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260718001539_AddDeletedAtToMailbox', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
DROP INDEX "IX_Users_Email";

ALTER TABLE "Users" ADD "DeletedAt" TEXT NULL;

CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email") WHERE "DeletedAt" IS NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260718003031_AddDeletedAtToUser', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "Customers" ADD "DeletedAt" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260719033901_AddCustomerDeletedAt', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "Tags" ADD "DeletedAt" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260719040419_AddTagDeletedAt', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "MailTemplates" ADD "DeletedAt" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260719042539_AddMailTemplateDeletedAt', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "Conversations" ADD "Priority" INTEGER NOT NULL DEFAULT 1;

UPDATE Conversations SET Priority = 1 WHERE Priority = 0;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260719052233_AddConversationPriority', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "EmailSignatures" ADD "DeletedAt" TEXT NULL;

ALTER TABLE "CannedResponses" ADD "DeletedAt" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260719061100_AddDeletedAtToEmailSignature', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260719061124_AddDeletedAtToCannedResponse', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
DROP INDEX "IX_Roles_Slug";

ALTER TABLE "Teams" ADD "DeletedAt" TEXT NULL;

ALTER TABLE "Roles" ADD "DeletedAt" TEXT NULL;

CREATE UNIQUE INDEX "IX_Roles_Slug" ON "Roles" ("Slug") WHERE "DeletedAt" IS NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260719071632_AddTeamAndRoleSoftDelete', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "SlaPolicies" ADD "DeletedAt" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260719075321_AddSlaPolicySoftDelete', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "Macros" ADD "DeletedAt" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260719082440_AddMacroSoftDelete', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "NotificationTemplates" ADD "DeletedAt" TEXT NULL;

ALTER TABLE "NotificationRules" ADD "DeletedAt" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260719083517_AddNotificationTrash', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "Workflows" ADD "DeletedAt" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260720053614_AddWorkflowSoftDelete', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
DROP INDEX "IX_Mailboxes_Email";

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
    CONSTRAINT "FK_KnowledgeCategories_KnowledgeCategories_ParentId" FOREIGN KEY ("ParentId") REFERENCES "KnowledgeCategories" ("Id")
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
    CONSTRAINT "FK_KnowledgeArticles_KnowledgeCategories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "KnowledgeCategories" ("Id")
);

CREATE UNIQUE INDEX "IX_Mailboxes_Email" ON "Mailboxes" ("Email") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_CalendarEvents_ConversationId" ON "CalendarEvents" ("ConversationId");

CREATE INDEX "IX_CalendarEvents_MailboxId" ON "CalendarEvents" ("MailboxId");

CREATE INDEX "IX_CalendarEvents_StartAt" ON "CalendarEvents" ("StartAt");

CREATE INDEX "IX_CalendarEvents_Status" ON "CalendarEvents" ("Status");

CREATE INDEX "IX_CalendarEvents_UserId" ON "CalendarEvents" ("UserId");

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

CREATE INDEX "IX_KnowledgeArticles_AuthorId" ON "KnowledgeArticles" ("AuthorId");

CREATE INDEX "IX_KnowledgeArticles_CategoryId" ON "KnowledgeArticles" ("CategoryId");

CREATE INDEX "IX_KnowledgeArticles_Status" ON "KnowledgeArticles" ("Status");

CREATE INDEX "IX_KnowledgeCategories_MailboxId" ON "KnowledgeCategories" ("MailboxId");

CREATE INDEX "IX_KnowledgeCategories_ParentId" ON "KnowledgeCategories" ("ParentId");

CREATE UNIQUE INDEX "IX_RefreshTokens_Token" ON "RefreshTokens" ("Token");

CREATE INDEX "IX_RefreshTokens_UserId" ON "RefreshTokens" ("UserId");

CREATE INDEX "IX_RefreshTokens_UserId_RevokedAt" ON "RefreshTokens" ("UserId", "RevokedAt");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260721014420_AddRefreshTokens', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
DROP INDEX "IX_Followers_ConversationId";

DROP INDEX "IX_Conversations_CustomerId";

DROP INDEX "IX_Conversations_FolderId";

DROP INDEX "IX_Conversations_MailboxId";

DROP INDEX "IX_Conversations_UserId";

ALTER TABLE "Webhooks" ADD "DeletedAt" TEXT NULL;

ALTER TABLE "KnowledgeCategories" ADD "DeletedAt" TEXT NULL;

ALTER TABLE "KnowledgeArticles" ADD "DeletedAt" TEXT NULL;

ALTER TABLE "Folders" ADD "CreatedAt" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';

ALTER TABLE "Folders" ADD "DeletedAt" TEXT NULL;

ALTER TABLE "Folders" ADD "UpdatedAt" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';

ALTER TABLE "CustomFields" ADD "DeletedAt" TEXT NULL;

ALTER TABLE "Conversations" ADD "DeletedAt" TEXT NULL;

ALTER TABLE "ApiKeys" ADD "DeletedAt" TEXT NULL;

CREATE INDEX "IX_Threads_CreatedAt" ON "Threads" ("CreatedAt");

CREATE INDEX "IX_Threads_Type" ON "Threads" ("Type");

CREATE UNIQUE INDEX "IX_Followers_ConversationId_UserId" ON "Followers" ("ConversationId", "UserId");

CREATE INDEX "IX_Conversations_CreatedAt" ON "Conversations" ("CreatedAt") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_CustomerId" ON "Conversations" ("CustomerId") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_FolderId" ON "Conversations" ("FolderId") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_LastReplyAt" ON "Conversations" ("LastReplyAt") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_MailboxId_Status" ON "Conversations" ("MailboxId", "Status") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_MailboxId_Status_LastReplyAt" ON "Conversations" ("MailboxId", "Status", "LastReplyAt") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_MailboxId_UserId" ON "Conversations" ("MailboxId", "UserId") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_Priority" ON "Conversations" ("Priority") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_UserId" ON "Conversations" ("UserId") WHERE "DeletedAt" IS NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260722034439_AddConversationAndThreadIndexes', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "Workflows" ADD "IsSystem" INTEGER NOT NULL DEFAULT 0;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260725020724_AddIsSystemToWorkflow', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
DROP INDEX "IX_Conversations_CreatedAt";

DROP INDEX "IX_Conversations_CustomerId";

DROP INDEX "IX_Conversations_FolderId";

DROP INDEX "IX_Conversations_LastReplyAt";

DROP INDEX "IX_Conversations_MailboxId_Status";

DROP INDEX "IX_Conversations_Priority";

DROP INDEX "IX_Conversations_UserId";

ALTER TABLE "Workflows" ADD "IsSystem" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "Users" ADD "Avatar" TEXT NULL;

ALTER TABLE "Threads" ADD "Version" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "SatisfactionRatings" ADD "UpdatedAt" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';

ALTER TABLE "NotificationRules" ADD "RecipientMailboxId" INTEGER NULL;

ALTER TABLE "NotificationRules" ADD "RoleId" INTEGER NULL;

ALTER TABLE "NotificationRules" ADD "TeamId" INTEGER NULL;

ALTER TABLE "Mailboxes" ADD "Version" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "Conversations" ADD "SearchVector" TEXT NULL;

ALTER TABLE "Conversations" ADD "Version" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "CalendarEvents" ADD "DeletedAt" TEXT NULL;

ALTER TABLE "CalendarEvents" ADD "IsRecurring" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "CalendarEvents" ADD "IsReminderSent" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "CalendarEvents" ADD "RecurrenceCount" INTEGER NULL;

ALTER TABLE "CalendarEvents" ADD "RecurrenceEndDate" TEXT NULL;

ALTER TABLE "CalendarEvents" ADD "RecurrenceInterval" INTEGER NULL;

ALTER TABLE "CalendarEvents" ADD "RecurrenceOriginalStart" TEXT NULL;

ALTER TABLE "CalendarEvents" ADD "RecurrenceParentId" INTEGER NULL;

ALTER TABLE "CalendarEvents" ADD "RecurrenceType" INTEGER NULL;

ALTER TABLE "CalendarEvents" ADD "ReminderMinutes" INTEGER NULL;

CREATE TABLE "UserNotifications" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserNotifications" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Title" TEXT NOT NULL,
    "Message" TEXT NOT NULL,
    "Link" TEXT NULL,
    "Type" TEXT NOT NULL,
    "IsRead" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "ReadAt" TEXT NULL,
    CONSTRAINT "FK_UserNotifications_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_NotificationRules_RecipientMailboxId" ON "NotificationRules" ("RecipientMailboxId");

CREATE INDEX "IX_NotificationRules_RoleId" ON "NotificationRules" ("RoleId");

CREATE INDEX "IX_NotificationRules_TeamId" ON "NotificationRules" ("TeamId");

CREATE INDEX "IX_Conversations_CustomerId" ON "Conversations" ("CustomerId");

CREATE INDEX "IX_Conversations_FolderId_Status" ON "Conversations" ("FolderId", "Status") WHERE "DeletedAt" IS NULL;

CREATE UNIQUE INDEX "IX_Conversations_Number" ON "Conversations" ("Number");

CREATE INDEX "IX_Conversations_Status_Priority_LastReplyAt" ON "Conversations" ("Status", "Priority", "LastReplyAt") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_UserId" ON "Conversations" ("UserId");

CREATE INDEX "IX_UserNotifications_UserId" ON "UserNotifications" ("UserId");

CREATE TABLE "ef_temp_AuditLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AuditLogs" PRIMARY KEY AUTOINCREMENT,
    "Action" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "EntityId" INTEGER NULL,
    "EntityType" TEXT NOT NULL,
    "IpAddress" TEXT NOT NULL,
    "NewValues" TEXT NOT NULL,
    "OldValues" TEXT NOT NULL,
    "UserAgent" TEXT NOT NULL,
    "UserId" INTEGER NULL,
    CONSTRAINT "FK_AuditLogs_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);

INSERT INTO "ef_temp_AuditLogs" ("Id", "Action", "CreatedAt", "EntityId", "EntityType", "IpAddress", "NewValues", "OldValues", "UserAgent", "UserId")
SELECT "Id", "Action", "CreatedAt", "EntityId", "EntityType", "IpAddress", "NewValues", "OldValues", "UserAgent", "UserId"
FROM "AuditLogs";

CREATE TABLE "ef_temp_MacroExecutionLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_MacroExecutionLogs" PRIMARY KEY AUTOINCREMENT,
    "ActionsExecuted" INTEGER NOT NULL,
    "ConversationId" INTEGER NOT NULL,
    "ErrorMessage" TEXT NULL,
    "ExecutedAt" TEXT NOT NULL,
    "ExecutedByUserId" INTEGER NOT NULL,
    "MacroId" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    CONSTRAINT "FK_MacroExecutionLogs_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_MacroExecutionLogs_Macros_MacroId" FOREIGN KEY ("MacroId") REFERENCES "Macros" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_MacroExecutionLogs_Users_ExecutedByUserId" FOREIGN KEY ("ExecutedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);

INSERT INTO "ef_temp_MacroExecutionLogs" ("Id", "ActionsExecuted", "ConversationId", "ErrorMessage", "ExecutedAt", "ExecutedByUserId", "MacroId", "Status")
SELECT "Id", "ActionsExecuted", "ConversationId", "ErrorMessage", "ExecutedAt", "ExecutedByUserId", "MacroId", "Status"
FROM "MacroExecutionLogs";

CREATE TABLE "ef_temp_SlaViolations" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SlaViolations" PRIMARY KEY AUTOINCREMENT,
    "ConversationId" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "DetectedAt" TEXT NOT NULL,
    "DueAt" TEXT NOT NULL,
    "ResolvedAt" TEXT NULL,
    "SlaPolicyId" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "ViolationType" TEXT NOT NULL,
    CONSTRAINT "FK_SlaViolations_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_SlaViolations_SlaPolicies_SlaPolicyId" FOREIGN KEY ("SlaPolicyId") REFERENCES "SlaPolicies" ("Id") ON DELETE CASCADE
);

INSERT INTO "ef_temp_SlaViolations" ("Id", "ConversationId", "CreatedAt", "DetectedAt", "DueAt", "ResolvedAt", "SlaPolicyId", "Status", "ViolationType")
SELECT "Id", "ConversationId", "CreatedAt", "DetectedAt", "DueAt", "ResolvedAt", "SlaPolicyId", "Status", "ViolationType"
FROM "SlaViolations";

CREATE TABLE "ef_temp_WorkflowExecutionLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WorkflowExecutionLogs" PRIMARY KEY AUTOINCREMENT,
    "ActionsExecuted" INTEGER NOT NULL,
    "ConversationId" INTEGER NOT NULL,
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

CREATE TABLE "ef_temp_Users" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT,
    "Avatar" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    "Email" TEXT NOT NULL,
    "FirstName" TEXT NOT NULL,
    "LastName" TEXT NOT NULL,
    "Locale" TEXT NOT NULL,
    "Password" TEXT NOT NULL,
    "Permissions" TEXT NOT NULL,
    "Role" INTEGER NOT NULL,
    "Status" INTEGER NOT NULL,
    "Timezone" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    CONSTRAINT "CK_User_Role" CHECK ([Role] IN (1, 2, 3, 4))
);

INSERT INTO "ef_temp_Users" ("Id", "Avatar", "CreatedAt", "DeletedAt", "Email", "FirstName", "LastName", "Locale", "Password", "Permissions", "Role", "Status", "Timezone", "UpdatedAt")
SELECT "Id", "Avatar", "CreatedAt", "DeletedAt", "Email", "FirstName", "LastName", "Locale", "Password", "Permissions", "Role", "Status", "Timezone", "UpdatedAt"
FROM "Users";

CREATE TABLE "ef_temp_Threads" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Threads" PRIMARY KEY AUTOINCREMENT,
    "ActionData" TEXT NULL,
    "ActionType" INTEGER NULL,
    "Bcc" TEXT NOT NULL,
    "Body" TEXT NOT NULL,
    "Cc" TEXT NOT NULL,
    "ConversationId" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "CreatedByCustomerId" INTEGER NULL,
    "CreatedByUserId" INTEGER NULL,
    "CustomerId" INTEGER NULL,
    "From" TEXT NOT NULL,
    "HasAttachments" INTEGER NOT NULL,
    "Headers" TEXT NULL,
    "IsFirst" INTEGER NOT NULL,
    "IsInternal" INTEGER NOT NULL,
    "MessageId" TEXT NOT NULL,
    "OpenedAt" TEXT NULL,
    "SendStatus" INTEGER NULL,
    "SourceType" INTEGER NOT NULL,
    "SourceVia" INTEGER NOT NULL,
    "State" INTEGER NOT NULL,
    "Subject" TEXT NOT NULL,
    "To" TEXT NOT NULL,
    "Type" INTEGER NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "UserId" INTEGER NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "CK_ConversationThread_State" CHECK ([State] IN (1, 2, 3)),
    CONSTRAINT "FK_Threads_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Threads_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Threads_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Threads_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);

INSERT INTO "ef_temp_Threads" ("Id", "ActionData", "ActionType", "Bcc", "Body", "Cc", "ConversationId", "CreatedAt", "CreatedByCustomerId", "CreatedByUserId", "CustomerId", "From", "HasAttachments", "Headers", "IsFirst", "IsInternal", "MessageId", "OpenedAt", "SendStatus", "SourceType", "SourceVia", "State", "Subject", "To", "Type", "UpdatedAt", "UserId", "Version")
SELECT "Id", "ActionData", "ActionType", "Bcc", "Body", "Cc", "ConversationId", "CreatedAt", "CreatedByCustomerId", "CreatedByUserId", "CustomerId", "From", "HasAttachments", "Headers", "IsFirst", "IsInternal", "MessageId", "OpenedAt", "SendStatus", "SourceType", "SourceVia", "State", "Subject", "To", "Type", "UpdatedAt", "UserId", "Version"
FROM "Threads";

CREATE TABLE "ef_temp_SendLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SendLogs" PRIMARY KEY AUTOINCREMENT,
    "AttachmentIds" TEXT NULL,
    "AttemptCount" INTEGER NOT NULL,
    "Bcc" TEXT NULL,
    "Body" TEXT NULL,
    "Cc" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "CustomerId" INTEGER NULL,
    "Email" TEXT NOT NULL,
    "ErrorMessage" TEXT NULL,
    "FirstOpenedAt" TEXT NULL,
    "InReplyTo" TEXT NULL,
    "LastOpenedAt" TEXT NULL,
    "MailType" INTEGER NOT NULL,
    "MailboxId" INTEGER NULL,
    "MessageId" TEXT NOT NULL,
    "OpenCount" INTEGER NOT NULL,
    "References" TEXT NULL,
    "SentAt" TEXT NULL,
    "Status" INTEGER NOT NULL,
    "Subject" TEXT NULL,
    "ThreadId" INTEGER NULL,
    "UserId" INTEGER NULL,
    CONSTRAINT "CK_SendLog_Status" CHECK ([Status] IN (1, 2, 3, 4, 5)),
    CONSTRAINT "FK_SendLogs_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id"),
    CONSTRAINT "FK_SendLogs_Threads_ThreadId" FOREIGN KEY ("ThreadId") REFERENCES "Threads" ("Id"),
    CONSTRAINT "FK_SendLogs_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
);

INSERT INTO "ef_temp_SendLogs" ("Id", "AttachmentIds", "AttemptCount", "Bcc", "Body", "Cc", "CreatedAt", "CustomerId", "Email", "ErrorMessage", "FirstOpenedAt", "InReplyTo", "LastOpenedAt", "MailType", "MailboxId", "MessageId", "OpenCount", "References", "SentAt", "Status", "Subject", "ThreadId", "UserId")
SELECT "Id", "AttachmentIds", "AttemptCount", "Bcc", "Body", "Cc", "CreatedAt", "CustomerId", "Email", "ErrorMessage", "FirstOpenedAt", "InReplyTo", "LastOpenedAt", "MailType", "MailboxId", "MessageId", "OpenCount", "References", "SentAt", "Status", "Subject", "ThreadId", "UserId"
FROM "SendLogs";

CREATE TABLE "ef_temp_Conversations" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Conversations" PRIMARY KEY AUTOINCREMENT,
    "Bcc" TEXT NOT NULL,
    "Cc" TEXT NOT NULL,
    "ClosedAt" TEXT NULL,
    "ClosedByUserId" INTEGER NULL,
    "CreatedAt" TEXT NOT NULL,
    "CreatedByCustomerId" INTEGER NULL,
    "CreatedByUserId" INTEGER NULL,
    "CustomerEmail" TEXT NOT NULL,
    "CustomerId" INTEGER NULL,
    "DeletedAt" TEXT NULL,
    "FolderId" INTEGER NULL,
    "HasAttachments" INTEGER NOT NULL,
    "Imported" INTEGER NOT NULL,
    "LastReplyAt" TEXT NULL,
    "LastReplyFrom" INTEGER NULL,
    "MailboxId" INTEGER NOT NULL,
    "Number" INTEGER NOT NULL,
    "Preview" TEXT NOT NULL,
    "Priority" INTEGER NOT NULL,
    "SearchVector" TEXT NULL,
    "SourceType" INTEGER NOT NULL,
    "SourceVia" INTEGER NOT NULL,
    "State" INTEGER NOT NULL,
    "Status" INTEGER NOT NULL,
    "Subject" TEXT NOT NULL,
    "ThreadsCount" INTEGER NOT NULL,
    "Type" INTEGER NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "UserId" INTEGER NULL,
    "UserUpdatedAt" TEXT NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "CK_Conversation_Priority" CHECK ([Priority] IN (0, 1, 2, 3)),
    CONSTRAINT "CK_Conversation_State" CHECK ([State] IN (1, 2)),
    CONSTRAINT "CK_Conversation_Status" CHECK ([Status] IN (1, 2, 3, 4)),
    CONSTRAINT "FK_Conversations_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Conversations_Folders_FolderId" FOREIGN KEY ("FolderId") REFERENCES "Folders" ("Id"),
    CONSTRAINT "FK_Conversations_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Conversations_Users_ClosedByUserId" FOREIGN KEY ("ClosedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Conversations_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Conversations_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);

INSERT INTO "ef_temp_Conversations" ("Id", "Bcc", "Cc", "ClosedAt", "ClosedByUserId", "CreatedAt", "CreatedByCustomerId", "CreatedByUserId", "CustomerEmail", "CustomerId", "DeletedAt", "FolderId", "HasAttachments", "Imported", "LastReplyAt", "LastReplyFrom", "MailboxId", "Number", "Preview", "Priority", "SearchVector", "SourceType", "SourceVia", "State", "Status", "Subject", "ThreadsCount", "Type", "UpdatedAt", "UserId", "UserUpdatedAt", "Version")
SELECT "Id", "Bcc", "Cc", "ClosedAt", "ClosedByUserId", "CreatedAt", "CreatedByCustomerId", "CreatedByUserId", "CustomerEmail", "CustomerId", "DeletedAt", "FolderId", "HasAttachments", "Imported", "LastReplyAt", "LastReplyFrom", "MailboxId", "Number", "Preview", "Priority", "SearchVector", "SourceType", "SourceVia", "State", "Status", "Subject", "ThreadsCount", "Type", "UpdatedAt", "UserId", "UserUpdatedAt", "Version"
FROM "Conversations";

CREATE TABLE "ef_temp_NotificationRules" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_NotificationRules" PRIMARY KEY AUTOINCREMENT,
    "CreatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    "EventType" TEXT NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "MailboxId" INTEGER NULL,
    "Name" TEXT NOT NULL,
    "RecipientMailboxId" INTEGER NULL,
    "RoleId" INTEGER NULL,
    "TeamId" INTEGER NULL,
    "TemplateId" INTEGER NULL,
    "UpdatedAt" TEXT NOT NULL,
    "UserId" INTEGER NULL,
    CONSTRAINT "FK_NotificationRules_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id"),
    CONSTRAINT "FK_NotificationRules_Mailboxes_RecipientMailboxId" FOREIGN KEY ("RecipientMailboxId") REFERENCES "Mailboxes" ("Id"),
    CONSTRAINT "FK_NotificationRules_NotificationTemplates_TemplateId" FOREIGN KEY ("TemplateId") REFERENCES "NotificationTemplates" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_NotificationRules_Roles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "Roles" ("Id"),
    CONSTRAINT "FK_NotificationRules_Teams_TeamId" FOREIGN KEY ("TeamId") REFERENCES "Teams" ("Id"),
    CONSTRAINT "FK_NotificationRules_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
);

INSERT INTO "ef_temp_NotificationRules" ("Id", "CreatedAt", "DeletedAt", "EventType", "IsActive", "MailboxId", "Name", "RecipientMailboxId", "RoleId", "TeamId", "TemplateId", "UpdatedAt", "UserId")
SELECT "Id", "CreatedAt", "DeletedAt", "EventType", "IsActive", "MailboxId", "Name", "RecipientMailboxId", "RoleId", "TeamId", "TemplateId", "UpdatedAt", "UserId"
FROM "NotificationRules";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "AuditLogs";

ALTER TABLE "ef_temp_AuditLogs" RENAME TO "AuditLogs";

DROP TABLE "MacroExecutionLogs";

ALTER TABLE "ef_temp_MacroExecutionLogs" RENAME TO "MacroExecutionLogs";

DROP TABLE "SlaViolations";

ALTER TABLE "ef_temp_SlaViolations" RENAME TO "SlaViolations";

DROP TABLE "WorkflowExecutionLogs";

ALTER TABLE "ef_temp_WorkflowExecutionLogs" RENAME TO "WorkflowExecutionLogs";

DROP TABLE "Users";

ALTER TABLE "ef_temp_Users" RENAME TO "Users";

DROP TABLE "Threads";

ALTER TABLE "ef_temp_Threads" RENAME TO "Threads";

DROP TABLE "SendLogs";

ALTER TABLE "ef_temp_SendLogs" RENAME TO "SendLogs";

DROP TABLE "Conversations";

ALTER TABLE "ef_temp_Conversations" RENAME TO "Conversations";

DROP TABLE "NotificationRules";

ALTER TABLE "ef_temp_NotificationRules" RENAME TO "NotificationRules";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE INDEX "IX_AuditLogs_CreatedAt" ON "AuditLogs" ("CreatedAt");

CREATE INDEX "IX_AuditLogs_EntityType_EntityId" ON "AuditLogs" ("EntityType", "EntityId");

CREATE INDEX "IX_AuditLogs_UserId" ON "AuditLogs" ("UserId");

CREATE INDEX "IX_MacroExecutionLogs_ConversationId" ON "MacroExecutionLogs" ("ConversationId");

CREATE INDEX "IX_MacroExecutionLogs_ExecutedAt" ON "MacroExecutionLogs" ("ExecutedAt");

CREATE INDEX "IX_MacroExecutionLogs_ExecutedByUserId" ON "MacroExecutionLogs" ("ExecutedByUserId");

CREATE INDEX "IX_MacroExecutionLogs_MacroId" ON "MacroExecutionLogs" ("MacroId");

CREATE INDEX "IX_SlaViolations_ConversationId" ON "SlaViolations" ("ConversationId");

CREATE INDEX "IX_SlaViolations_ConversationId_ViolationType" ON "SlaViolations" ("ConversationId", "ViolationType");

CREATE INDEX "IX_SlaViolations_DueAt" ON "SlaViolations" ("DueAt");

CREATE INDEX "IX_SlaViolations_SlaPolicyId" ON "SlaViolations" ("SlaPolicyId");

CREATE INDEX "IX_SlaViolations_Status" ON "SlaViolations" ("Status");

CREATE INDEX "IX_WorkflowExecutionLogs_ConversationId" ON "WorkflowExecutionLogs" ("ConversationId");

CREATE INDEX "IX_WorkflowExecutionLogs_ExecutedAt" ON "WorkflowExecutionLogs" ("ExecutedAt");

CREATE INDEX "IX_WorkflowExecutionLogs_WorkflowId" ON "WorkflowExecutionLogs" ("WorkflowId");

CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Threads_ConversationId" ON "Threads" ("ConversationId");

CREATE INDEX "IX_Threads_CreatedAt" ON "Threads" ("CreatedAt");

CREATE INDEX "IX_Threads_CreatedByUserId" ON "Threads" ("CreatedByUserId");

CREATE INDEX "IX_Threads_CustomerId" ON "Threads" ("CustomerId");

CREATE INDEX "IX_Threads_Type" ON "Threads" ("Type");

CREATE INDEX "IX_Threads_UserId" ON "Threads" ("UserId");

CREATE INDEX "IX_SendLogs_CreatedAt" ON "SendLogs" ("CreatedAt");

CREATE INDEX "IX_SendLogs_CustomerId" ON "SendLogs" ("CustomerId");

CREATE INDEX "IX_SendLogs_Email" ON "SendLogs" ("Email");

CREATE INDEX "IX_SendLogs_MailboxId" ON "SendLogs" ("MailboxId");

CREATE INDEX "IX_SendLogs_MailType" ON "SendLogs" ("MailType");

CREATE INDEX "IX_SendLogs_MessageId" ON "SendLogs" ("MessageId");

CREATE INDEX "IX_SendLogs_Status" ON "SendLogs" ("Status");

CREATE INDEX "IX_SendLogs_ThreadId" ON "SendLogs" ("ThreadId");

CREATE INDEX "IX_SendLogs_UserId" ON "SendLogs" ("UserId");

CREATE INDEX "IX_Conversations_ClosedByUserId" ON "Conversations" ("ClosedByUserId");

CREATE INDEX "IX_Conversations_CreatedByUserId" ON "Conversations" ("CreatedByUserId");

CREATE INDEX "IX_Conversations_CustomerId" ON "Conversations" ("CustomerId");

CREATE INDEX "IX_Conversations_FolderId_Status" ON "Conversations" ("FolderId", "Status") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_MailboxId_Status_LastReplyAt" ON "Conversations" ("MailboxId", "Status", "LastReplyAt") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_MailboxId_UserId" ON "Conversations" ("MailboxId", "UserId") WHERE "DeletedAt" IS NULL;

CREATE UNIQUE INDEX "IX_Conversations_Number" ON "Conversations" ("Number");

CREATE INDEX "IX_Conversations_Status_Priority_LastReplyAt" ON "Conversations" ("Status", "Priority", "LastReplyAt") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_UserId" ON "Conversations" ("UserId");

CREATE INDEX "IX_NotificationRules_EventType" ON "NotificationRules" ("EventType");

CREATE INDEX "IX_NotificationRules_MailboxId" ON "NotificationRules" ("MailboxId");

CREATE INDEX "IX_NotificationRules_RecipientMailboxId" ON "NotificationRules" ("RecipientMailboxId");

CREATE INDEX "IX_NotificationRules_RoleId" ON "NotificationRules" ("RoleId");

CREATE INDEX "IX_NotificationRules_TeamId" ON "NotificationRules" ("TeamId");

CREATE INDEX "IX_NotificationRules_TemplateId" ON "NotificationRules" ("TemplateId");

CREATE INDEX "IX_NotificationRules_UserId" ON "NotificationRules" ("UserId");

COMMIT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260726115230_AddPasswordResetTokenIndexes', '10.0.9');

BEGIN TRANSACTION;
DROP INDEX "IX_Tags_MailboxId_Name";

DROP INDEX "IX_MailTemplates_Slug";

DROP INDEX "IX_Conversations_CreatedAt";

DROP INDEX "IX_Conversations_CustomerId";

DROP INDEX "IX_Conversations_FolderId";

DROP INDEX "IX_Conversations_LastReplyAt";

DROP INDEX "IX_Conversations_MailboxId_Status";

DROP INDEX "IX_Conversations_Priority";

DROP INDEX "IX_Conversations_UserId";

ALTER TABLE "Workflows" ADD "IsSystem" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "Users" ADD "Avatar" TEXT NULL;

ALTER TABLE "Users" ADD "BrowserOnAssignedToMe" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "Users" ADD "BrowserOnMention" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "Users" ADD "BrowserOnNewConversation" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "Users" ADD "BrowserOnNewReply" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "Users" ADD "DefaultSignature" TEXT NULL;

ALTER TABLE "Users" ADD "EmailOnAssignedToMe" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "Users" ADD "EmailOnMention" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "Users" ADD "EmailOnNewConversation" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "Users" ADD "EmailOnNewReply" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "Users" ADD "FailedLoginAttempts" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "Users" ADD "LockoutEnd" TEXT NULL;

ALTER TABLE "Users" ADD "UseGlobalSignature" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "SatisfactionRatings" ADD "UpdatedAt" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';

ALTER TABLE "NotificationRules" ADD "RecipientMailboxId" INTEGER NULL;

ALTER TABLE "NotificationRules" ADD "RoleId" INTEGER NULL;

ALTER TABLE "NotificationRules" ADD "TeamId" INTEGER NULL;

ALTER TABLE "CalendarEvents" ADD "DeletedAt" TEXT NULL;

ALTER TABLE "CalendarEvents" ADD "IsRecurring" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "CalendarEvents" ADD "IsReminderSent" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "CalendarEvents" ADD "RecurrenceCount" INTEGER NULL;

ALTER TABLE "CalendarEvents" ADD "RecurrenceEndDate" TEXT NULL;

ALTER TABLE "CalendarEvents" ADD "RecurrenceInterval" INTEGER NULL;

ALTER TABLE "CalendarEvents" ADD "RecurrenceOriginalStart" TEXT NULL;

ALTER TABLE "CalendarEvents" ADD "RecurrenceParentId" INTEGER NULL;

ALTER TABLE "CalendarEvents" ADD "RecurrenceType" INTEGER NULL;

ALTER TABLE "CalendarEvents" ADD "ReminderMinutes" INTEGER NULL;

CREATE TABLE "UserNotifications" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_UserNotifications" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "Title" TEXT NOT NULL,
    "Message" TEXT NOT NULL,
    "Link" TEXT NULL,
    "Type" TEXT NOT NULL,
    "IsRead" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "ReadAt" TEXT NULL,
    CONSTRAINT "FK_UserNotifications_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_Threads_ConversationId_CreatedAt" ON "Threads" ("ConversationId", "CreatedAt");

CREATE INDEX "IX_Threads_ConversationId_Type_CreatedAt" ON "Threads" ("ConversationId", "Type", "CreatedAt");

CREATE INDEX "IX_Threads_Type_CreatedAt_UserId" ON "Threads" ("Type", "CreatedAt", "UserId");

CREATE UNIQUE INDEX "IX_Tags_MailboxId_Name" ON "Tags" ("MailboxId", "Name") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_SendLogs_MailboxId_CreatedAt" ON "SendLogs" ("MailboxId", "CreatedAt");

CREATE INDEX "IX_PasswordResetTokens_ExpiresAt" ON "PasswordResetTokens" ("ExpiresAt");

CREATE UNIQUE INDEX "IX_PasswordResetTokens_Token" ON "PasswordResetTokens" ("Token");

CREATE INDEX "IX_NotificationRules_RecipientMailboxId" ON "NotificationRules" ("RecipientMailboxId");

CREATE INDEX "IX_NotificationRules_RoleId" ON "NotificationRules" ("RoleId");

CREATE INDEX "IX_NotificationRules_TeamId" ON "NotificationRules" ("TeamId");

CREATE UNIQUE INDEX "IX_MailTemplates_Slug" ON "MailTemplates" ("Slug") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_CustomerEmail" ON "Conversations" ("CustomerEmail") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_CustomerId" ON "Conversations" ("CustomerId");

CREATE INDEX "IX_Conversations_FolderId_Status" ON "Conversations" ("FolderId", "Status") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_MailboxId_ClosedAt" ON "Conversations" ("MailboxId", "ClosedAt") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_MailboxId_CreatedAt" ON "Conversations" ("MailboxId", "CreatedAt") WHERE "DeletedAt" IS NULL;

CREATE UNIQUE INDEX "IX_Conversations_Number" ON "Conversations" ("Number");

CREATE INDEX "IX_Conversations_Status_Priority_LastReplyAt" ON "Conversations" ("Status", "Priority", "LastReplyAt") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_UserId_Status" ON "Conversations" ("UserId", "Status") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_UserNotifications_UserId" ON "UserNotifications" ("UserId");

CREATE TABLE "ef_temp_AuditLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_AuditLogs" PRIMARY KEY AUTOINCREMENT,
    "Action" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "EntityId" INTEGER NULL,
    "EntityType" TEXT NOT NULL,
    "IpAddress" TEXT NOT NULL,
    "NewValues" TEXT NOT NULL,
    "OldValues" TEXT NOT NULL,
    "UserAgent" TEXT NOT NULL,
    "UserId" INTEGER NULL,
    CONSTRAINT "FK_AuditLogs_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);

INSERT INTO "ef_temp_AuditLogs" ("Id", "Action", "CreatedAt", "EntityId", "EntityType", "IpAddress", "NewValues", "OldValues", "UserAgent", "UserId")
SELECT "Id", "Action", "CreatedAt", "EntityId", "EntityType", "IpAddress", "NewValues", "OldValues", "UserAgent", "UserId"
FROM "AuditLogs";

CREATE TABLE "ef_temp_MacroExecutionLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_MacroExecutionLogs" PRIMARY KEY AUTOINCREMENT,
    "ActionsExecuted" INTEGER NOT NULL,
    "ConversationId" INTEGER NOT NULL,
    "ErrorMessage" TEXT NULL,
    "ExecutedAt" TEXT NOT NULL,
    "ExecutedByUserId" INTEGER NOT NULL,
    "MacroId" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    CONSTRAINT "FK_MacroExecutionLogs_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_MacroExecutionLogs_Macros_MacroId" FOREIGN KEY ("MacroId") REFERENCES "Macros" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_MacroExecutionLogs_Users_ExecutedByUserId" FOREIGN KEY ("ExecutedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);

INSERT INTO "ef_temp_MacroExecutionLogs" ("Id", "ActionsExecuted", "ConversationId", "ErrorMessage", "ExecutedAt", "ExecutedByUserId", "MacroId", "Status")
SELECT "Id", "ActionsExecuted", "ConversationId", "ErrorMessage", "ExecutedAt", "ExecutedByUserId", "MacroId", "Status"
FROM "MacroExecutionLogs";

CREATE TABLE "ef_temp_SlaViolations" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SlaViolations" PRIMARY KEY AUTOINCREMENT,
    "ConversationId" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "DetectedAt" TEXT NOT NULL,
    "DueAt" TEXT NOT NULL,
    "ResolvedAt" TEXT NULL,
    "SlaPolicyId" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "ViolationType" TEXT NOT NULL,
    CONSTRAINT "FK_SlaViolations_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE RESTRICT,
    CONSTRAINT "FK_SlaViolations_SlaPolicies_SlaPolicyId" FOREIGN KEY ("SlaPolicyId") REFERENCES "SlaPolicies" ("Id") ON DELETE CASCADE
);

INSERT INTO "ef_temp_SlaViolations" ("Id", "ConversationId", "CreatedAt", "DetectedAt", "DueAt", "ResolvedAt", "SlaPolicyId", "Status", "ViolationType")
SELECT "Id", "ConversationId", "CreatedAt", "DetectedAt", "DueAt", "ResolvedAt", "SlaPolicyId", "Status", "ViolationType"
FROM "SlaViolations";

CREATE TABLE "ef_temp_WorkflowExecutionLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_WorkflowExecutionLogs" PRIMARY KEY AUTOINCREMENT,
    "ActionsExecuted" INTEGER NOT NULL,
    "ConversationId" INTEGER NOT NULL,
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

CREATE TABLE "ef_temp_Users" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY AUTOINCREMENT,
    "Avatar" TEXT NULL,
    "BrowserOnAssignedToMe" INTEGER NOT NULL,
    "BrowserOnMention" INTEGER NOT NULL,
    "BrowserOnNewConversation" INTEGER NOT NULL,
    "BrowserOnNewReply" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "DefaultSignature" TEXT NULL,
    "DeletedAt" TEXT NULL,
    "Email" TEXT NOT NULL,
    "EmailOnAssignedToMe" INTEGER NOT NULL,
    "EmailOnMention" INTEGER NOT NULL,
    "EmailOnNewConversation" INTEGER NOT NULL,
    "EmailOnNewReply" INTEGER NOT NULL,
    "FailedLoginAttempts" INTEGER NOT NULL,
    "FirstName" TEXT NOT NULL,
    "LastName" TEXT NOT NULL,
    "Locale" TEXT NOT NULL,
    "LockoutEnd" TEXT NULL,
    "Password" TEXT NOT NULL,
    "Permissions" TEXT NOT NULL,
    "Role" INTEGER NOT NULL,
    "Status" INTEGER NOT NULL,
    "Timezone" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "UseGlobalSignature" INTEGER NOT NULL,
    CONSTRAINT "CK_User_Role" CHECK ([Role] IN (1, 2, 3, 4))
);

INSERT INTO "ef_temp_Users" ("Id", "Avatar", "BrowserOnAssignedToMe", "BrowserOnMention", "BrowserOnNewConversation", "BrowserOnNewReply", "CreatedAt", "DefaultSignature", "DeletedAt", "Email", "EmailOnAssignedToMe", "EmailOnMention", "EmailOnNewConversation", "EmailOnNewReply", "FailedLoginAttempts", "FirstName", "LastName", "Locale", "LockoutEnd", "Password", "Permissions", "Role", "Status", "Timezone", "UpdatedAt", "UseGlobalSignature")
SELECT "Id", "Avatar", "BrowserOnAssignedToMe", "BrowserOnMention", "BrowserOnNewConversation", "BrowserOnNewReply", "CreatedAt", "DefaultSignature", "DeletedAt", "Email", "EmailOnAssignedToMe", "EmailOnMention", "EmailOnNewConversation", "EmailOnNewReply", "FailedLoginAttempts", "FirstName", "LastName", "Locale", "LockoutEnd", "Password", "Permissions", "Role", "Status", "Timezone", "UpdatedAt", "UseGlobalSignature"
FROM "Users";

CREATE TABLE "ef_temp_Threads" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Threads" PRIMARY KEY AUTOINCREMENT,
    "ActionData" TEXT NULL,
    "ActionType" INTEGER NULL,
    "Bcc" TEXT NOT NULL,
    "Body" TEXT NOT NULL,
    "Cc" TEXT NOT NULL,
    "ConversationId" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "CreatedByCustomerId" INTEGER NULL,
    "CreatedByUserId" INTEGER NULL,
    "CustomerId" INTEGER NULL,
    "From" TEXT NOT NULL,
    "HasAttachments" INTEGER NOT NULL,
    "Headers" TEXT NULL,
    "IsFirst" INTEGER NOT NULL,
    "IsInternal" INTEGER NOT NULL,
    "MessageId" TEXT NOT NULL,
    "OpenedAt" TEXT NULL,
    "SendStatus" INTEGER NULL,
    "SourceType" INTEGER NOT NULL,
    "SourceVia" INTEGER NOT NULL,
    "State" INTEGER NOT NULL,
    "Subject" TEXT NOT NULL,
    "To" TEXT NOT NULL,
    "Type" INTEGER NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "UserId" INTEGER NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "CK_ConversationThread_State" CHECK ([State] IN (1, 2, 3)),
    CONSTRAINT "FK_Threads_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Threads_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Threads_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Threads_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);

INSERT INTO "ef_temp_Threads" ("Id", "ActionData", "ActionType", "Bcc", "Body", "Cc", "ConversationId", "CreatedAt", "CreatedByCustomerId", "CreatedByUserId", "CustomerId", "From", "HasAttachments", "Headers", "IsFirst", "IsInternal", "MessageId", "OpenedAt", "SendStatus", "SourceType", "SourceVia", "State", "Subject", "To", "Type", "UpdatedAt", "UserId", "Version")
SELECT "Id", "ActionData", "ActionType", "Bcc", "Body", "Cc", "ConversationId", "CreatedAt", "CreatedByCustomerId", "CreatedByUserId", "CustomerId", "From", "HasAttachments", "Headers", "IsFirst", "IsInternal", "MessageId", "OpenedAt", "SendStatus", "SourceType", "SourceVia", "State", "Subject", "To", "Type", "UpdatedAt", "UserId", "Version"
FROM "Threads";

CREATE TABLE "ef_temp_SendLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_SendLogs" PRIMARY KEY AUTOINCREMENT,
    "AttachmentIds" TEXT NULL,
    "AttemptCount" INTEGER NOT NULL,
    "Bcc" TEXT NULL,
    "Body" TEXT NULL,
    "Cc" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "CustomerId" INTEGER NULL,
    "Email" TEXT NOT NULL,
    "ErrorMessage" TEXT NULL,
    "FirstOpenedAt" TEXT NULL,
    "InReplyTo" TEXT NULL,
    "LastOpenedAt" TEXT NULL,
    "MailType" INTEGER NOT NULL,
    "MailboxId" INTEGER NULL,
    "MessageId" TEXT NOT NULL,
    "OpenCount" INTEGER NOT NULL,
    "References" TEXT NULL,
    "SentAt" TEXT NULL,
    "Status" INTEGER NOT NULL,
    "Subject" TEXT NULL,
    "ThreadId" INTEGER NULL,
    "UserId" INTEGER NULL,
    CONSTRAINT "CK_SendLog_Status" CHECK ([Status] IN (1, 2, 3, 4, 5)),
    CONSTRAINT "FK_SendLogs_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id"),
    CONSTRAINT "FK_SendLogs_Threads_ThreadId" FOREIGN KEY ("ThreadId") REFERENCES "Threads" ("Id"),
    CONSTRAINT "FK_SendLogs_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
);

INSERT INTO "ef_temp_SendLogs" ("Id", "AttachmentIds", "AttemptCount", "Bcc", "Body", "Cc", "CreatedAt", "CustomerId", "Email", "ErrorMessage", "FirstOpenedAt", "InReplyTo", "LastOpenedAt", "MailType", "MailboxId", "MessageId", "OpenCount", "References", "SentAt", "Status", "Subject", "ThreadId", "UserId")
SELECT "Id", "AttachmentIds", "AttemptCount", "Bcc", "Body", "Cc", "CreatedAt", "CustomerId", "Email", "ErrorMessage", "FirstOpenedAt", "InReplyTo", "LastOpenedAt", "MailType", "MailboxId", "MessageId", "OpenCount", "References", "SentAt", "Status", "Subject", "ThreadId", "UserId"
FROM "SendLogs";

CREATE TABLE "ef_temp_Conversations" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_Conversations" PRIMARY KEY AUTOINCREMENT,
    "Bcc" TEXT NOT NULL,
    "Cc" TEXT NOT NULL,
    "ClosedAt" TEXT NULL,
    "ClosedByUserId" INTEGER NULL,
    "CreatedAt" TEXT NOT NULL,
    "CreatedByCustomerId" INTEGER NULL,
    "CreatedByUserId" INTEGER NULL,
    "CustomerEmail" TEXT NOT NULL,
    "CustomerId" INTEGER NULL,
    "DeletedAt" TEXT NULL,
    "FolderId" INTEGER NULL,
    "HasAttachments" INTEGER NOT NULL,
    "Imported" INTEGER NOT NULL,
    "LastReplyAt" TEXT NULL,
    "LastReplyFrom" INTEGER NULL,
    "MailboxId" INTEGER NOT NULL,
    "Number" INTEGER NOT NULL,
    "Preview" TEXT NOT NULL,
    "Priority" INTEGER NOT NULL,
    "SearchVector" TEXT NULL,
    "SourceType" INTEGER NOT NULL,
    "SourceVia" INTEGER NOT NULL,
    "State" INTEGER NOT NULL,
    "Status" INTEGER NOT NULL,
    "Subject" TEXT NOT NULL,
    "ThreadsCount" INTEGER NOT NULL,
    "Type" INTEGER NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "UserId" INTEGER NULL,
    "UserUpdatedAt" TEXT NULL,
    "Version" INTEGER NOT NULL,
    CONSTRAINT "CK_Conversation_Priority" CHECK ([Priority] IN (0, 1, 2, 3)),
    CONSTRAINT "CK_Conversation_State" CHECK ([State] IN (1, 2)),
    CONSTRAINT "CK_Conversation_Status" CHECK ([Status] IN (1, 2, 3, 4)),
    CONSTRAINT "FK_Conversations_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Conversations_Folders_FolderId" FOREIGN KEY ("FolderId") REFERENCES "Folders" ("Id"),
    CONSTRAINT "FK_Conversations_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Conversations_Users_ClosedByUserId" FOREIGN KEY ("ClosedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Conversations_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_Conversations_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);

INSERT INTO "ef_temp_Conversations" ("Id", "Bcc", "Cc", "ClosedAt", "ClosedByUserId", "CreatedAt", "CreatedByCustomerId", "CreatedByUserId", "CustomerEmail", "CustomerId", "DeletedAt", "FolderId", "HasAttachments", "Imported", "LastReplyAt", "LastReplyFrom", "MailboxId", "Number", "Preview", "Priority", "SearchVector", "SourceType", "SourceVia", "State", "Status", "Subject", "ThreadsCount", "Type", "UpdatedAt", "UserId", "UserUpdatedAt", "Version")
SELECT "Id", "Bcc", "Cc", "ClosedAt", "ClosedByUserId", "CreatedAt", "CreatedByCustomerId", "CreatedByUserId", "CustomerEmail", "CustomerId", "DeletedAt", "FolderId", "HasAttachments", "Imported", "LastReplyAt", "LastReplyFrom", "MailboxId", "Number", "Preview", "Priority", "SearchVector", "SourceType", "SourceVia", "State", "Status", "Subject", "ThreadsCount", "Type", "UpdatedAt", "UserId", "UserUpdatedAt", "Version"
FROM "Conversations";

CREATE TABLE "ef_temp_NotificationRules" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_NotificationRules" PRIMARY KEY AUTOINCREMENT,
    "CreatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    "EventType" TEXT NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "MailboxId" INTEGER NULL,
    "Name" TEXT NOT NULL,
    "RecipientMailboxId" INTEGER NULL,
    "RoleId" INTEGER NULL,
    "TeamId" INTEGER NULL,
    "TemplateId" INTEGER NULL,
    "UpdatedAt" TEXT NOT NULL,
    "UserId" INTEGER NULL,
    CONSTRAINT "FK_NotificationRules_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id"),
    CONSTRAINT "FK_NotificationRules_Mailboxes_RecipientMailboxId" FOREIGN KEY ("RecipientMailboxId") REFERENCES "Mailboxes" ("Id"),
    CONSTRAINT "FK_NotificationRules_NotificationTemplates_TemplateId" FOREIGN KEY ("TemplateId") REFERENCES "NotificationTemplates" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_NotificationRules_Roles_RoleId" FOREIGN KEY ("RoleId") REFERENCES "Roles" ("Id"),
    CONSTRAINT "FK_NotificationRules_Teams_TeamId" FOREIGN KEY ("TeamId") REFERENCES "Teams" ("Id"),
    CONSTRAINT "FK_NotificationRules_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id")
);

INSERT INTO "ef_temp_NotificationRules" ("Id", "CreatedAt", "DeletedAt", "EventType", "IsActive", "MailboxId", "Name", "RecipientMailboxId", "RoleId", "TeamId", "TemplateId", "UpdatedAt", "UserId")
SELECT "Id", "CreatedAt", "DeletedAt", "EventType", "IsActive", "MailboxId", "Name", "RecipientMailboxId", "RoleId", "TeamId", "TemplateId", "UpdatedAt", "UserId"
FROM "NotificationRules";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "AuditLogs";

ALTER TABLE "ef_temp_AuditLogs" RENAME TO "AuditLogs";

DROP TABLE "MacroExecutionLogs";

ALTER TABLE "ef_temp_MacroExecutionLogs" RENAME TO "MacroExecutionLogs";

DROP TABLE "SlaViolations";

ALTER TABLE "ef_temp_SlaViolations" RENAME TO "SlaViolations";

DROP TABLE "WorkflowExecutionLogs";

ALTER TABLE "ef_temp_WorkflowExecutionLogs" RENAME TO "WorkflowExecutionLogs";

DROP TABLE "Users";

ALTER TABLE "ef_temp_Users" RENAME TO "Users";

DROP TABLE "Threads";

ALTER TABLE "ef_temp_Threads" RENAME TO "Threads";

DROP TABLE "SendLogs";

ALTER TABLE "ef_temp_SendLogs" RENAME TO "SendLogs";

DROP TABLE "Conversations";

ALTER TABLE "ef_temp_Conversations" RENAME TO "Conversations";

DROP TABLE "NotificationRules";

ALTER TABLE "ef_temp_NotificationRules" RENAME TO "NotificationRules";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE INDEX "IX_AuditLogs_CreatedAt" ON "AuditLogs" ("CreatedAt");

CREATE INDEX "IX_AuditLogs_EntityType_EntityId" ON "AuditLogs" ("EntityType", "EntityId");

CREATE INDEX "IX_AuditLogs_UserId" ON "AuditLogs" ("UserId");

CREATE INDEX "IX_MacroExecutionLogs_ConversationId" ON "MacroExecutionLogs" ("ConversationId");

CREATE INDEX "IX_MacroExecutionLogs_ExecutedAt" ON "MacroExecutionLogs" ("ExecutedAt");

CREATE INDEX "IX_MacroExecutionLogs_ExecutedByUserId" ON "MacroExecutionLogs" ("ExecutedByUserId");

CREATE INDEX "IX_MacroExecutionLogs_MacroId" ON "MacroExecutionLogs" ("MacroId");

CREATE INDEX "IX_SlaViolations_ConversationId" ON "SlaViolations" ("ConversationId");

CREATE INDEX "IX_SlaViolations_ConversationId_ViolationType" ON "SlaViolations" ("ConversationId", "ViolationType");

CREATE INDEX "IX_SlaViolations_DueAt" ON "SlaViolations" ("DueAt");

CREATE INDEX "IX_SlaViolations_SlaPolicyId" ON "SlaViolations" ("SlaPolicyId");

CREATE INDEX "IX_SlaViolations_Status" ON "SlaViolations" ("Status");

CREATE INDEX "IX_WorkflowExecutionLogs_ConversationId" ON "WorkflowExecutionLogs" ("ConversationId");

CREATE INDEX "IX_WorkflowExecutionLogs_ExecutedAt" ON "WorkflowExecutionLogs" ("ExecutedAt");

CREATE INDEX "IX_WorkflowExecutionLogs_WorkflowId" ON "WorkflowExecutionLogs" ("WorkflowId");

CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Threads_ConversationId" ON "Threads" ("ConversationId");

CREATE INDEX "IX_Threads_ConversationId_CreatedAt" ON "Threads" ("ConversationId", "CreatedAt");

CREATE INDEX "IX_Threads_ConversationId_Type_CreatedAt" ON "Threads" ("ConversationId", "Type", "CreatedAt");

CREATE INDEX "IX_Threads_CreatedAt" ON "Threads" ("CreatedAt");

CREATE INDEX "IX_Threads_CreatedByUserId" ON "Threads" ("CreatedByUserId");

CREATE INDEX "IX_Threads_CustomerId" ON "Threads" ("CustomerId");

CREATE INDEX "IX_Threads_Type" ON "Threads" ("Type");

CREATE INDEX "IX_Threads_Type_CreatedAt_UserId" ON "Threads" ("Type", "CreatedAt", "UserId");

CREATE INDEX "IX_Threads_UserId" ON "Threads" ("UserId");

CREATE INDEX "IX_SendLogs_CreatedAt" ON "SendLogs" ("CreatedAt");

CREATE INDEX "IX_SendLogs_CustomerId" ON "SendLogs" ("CustomerId");

CREATE INDEX "IX_SendLogs_Email" ON "SendLogs" ("Email");

CREATE INDEX "IX_SendLogs_MailboxId" ON "SendLogs" ("MailboxId");

CREATE INDEX "IX_SendLogs_MailboxId_CreatedAt" ON "SendLogs" ("MailboxId", "CreatedAt");

CREATE INDEX "IX_SendLogs_MailType" ON "SendLogs" ("MailType");

CREATE INDEX "IX_SendLogs_MessageId" ON "SendLogs" ("MessageId");

CREATE INDEX "IX_SendLogs_Status" ON "SendLogs" ("Status");

CREATE INDEX "IX_SendLogs_ThreadId" ON "SendLogs" ("ThreadId");

CREATE INDEX "IX_SendLogs_UserId" ON "SendLogs" ("UserId");

CREATE INDEX "IX_Conversations_ClosedByUserId" ON "Conversations" ("ClosedByUserId");

CREATE INDEX "IX_Conversations_CreatedByUserId" ON "Conversations" ("CreatedByUserId");

CREATE INDEX "IX_Conversations_CustomerEmail" ON "Conversations" ("CustomerEmail") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_CustomerId" ON "Conversations" ("CustomerId");

CREATE INDEX "IX_Conversations_FolderId_Status" ON "Conversations" ("FolderId", "Status") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_MailboxId_ClosedAt" ON "Conversations" ("MailboxId", "ClosedAt") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_MailboxId_CreatedAt" ON "Conversations" ("MailboxId", "CreatedAt") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_MailboxId_Status_LastReplyAt" ON "Conversations" ("MailboxId", "Status", "LastReplyAt") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_MailboxId_UserId" ON "Conversations" ("MailboxId", "UserId") WHERE "DeletedAt" IS NULL;

CREATE UNIQUE INDEX "IX_Conversations_Number" ON "Conversations" ("Number");

CREATE INDEX "IX_Conversations_Status_Priority_LastReplyAt" ON "Conversations" ("Status", "Priority", "LastReplyAt") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_Conversations_UserId_Status" ON "Conversations" ("UserId", "Status") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_NotificationRules_EventType" ON "NotificationRules" ("EventType");

CREATE INDEX "IX_NotificationRules_MailboxId" ON "NotificationRules" ("MailboxId");

CREATE INDEX "IX_NotificationRules_RecipientMailboxId" ON "NotificationRules" ("RecipientMailboxId");

CREATE INDEX "IX_NotificationRules_RoleId" ON "NotificationRules" ("RoleId");

CREATE INDEX "IX_NotificationRules_TeamId" ON "NotificationRules" ("TeamId");

CREATE INDEX "IX_NotificationRules_TemplateId" ON "NotificationRules" ("TemplateId");

CREATE INDEX "IX_NotificationRules_UserId" ON "NotificationRules" ("UserId");

COMMIT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260728110945_AddUserNotificationSettings', '10.0.9');

BEGIN TRANSACTION;
ALTER TABLE "AuditLogs" ADD "HmacSignature" TEXT NOT NULL DEFAULT '';

CREATE UNIQUE INDEX "IX_Emails_EmailAddress" ON "Emails" ("EmailAddress");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260728134238_AddAuditLogHmacSignature', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
DROP INDEX IF EXISTS "IX_Stars_ConversationId"

DROP INDEX IF EXISTS "IX_Conversations_Number"

DROP INDEX IF EXISTS "IX_ApiKeys_Key"

CREATE INDEX IF NOT EXISTS "IX_Threads_MessageId" ON "Threads" ("MessageId")

CREATE UNIQUE INDEX IF NOT EXISTS "IX_Stars_ConversationId_UserId" ON "Stars" ("ConversationId", "UserId")


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

CREATE UNIQUE INDEX IF NOT EXISTS "IX_Conversations_Number" ON "Conversations" ("Number") WHERE "DeletedAt" IS NULL

CREATE UNIQUE INDEX IF NOT EXISTS "IX_ApiKeys_Key" ON "ApiKeys" ("Key") WHERE "DeletedAt" IS NULL

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260729093234_FixUniqueIndexSoftDelete', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "CalendarEvents" ADD "AttendeesJson" TEXT NULL;

ALTER TABLE "CalendarEvents" ADD "CreatedBy" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "CalendarEvents" ADD "Location" TEXT NULL;

ALTER TABLE "CalendarEvents" ADD "MeetingUrl" TEXT NULL;

ALTER TABLE "CalendarEvents" ADD "Priority" INTEGER NULL;

ALTER TABLE "CalendarEvents" ADD "Progress" INTEGER NULL;

ALTER TABLE "CalendarEvents" ADD "RecurrenceExclusionsJson" TEXT NULL;

ALTER TABLE "CalendarEvents" ADD "RelatedEventId" INTEGER NULL;

ALTER TABLE "CalendarEvents" ADD "SubTasksJson" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260730194040_CalendarEventExtendedFields', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "OutlookEventMappings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_OutlookEventMappings" PRIMARY KEY AUTOINCREMENT,
    "CalendarEventId" INTEGER NOT NULL,
    "OutlookEventId" TEXT NOT NULL,
    "UserId" INTEGER NOT NULL,
    "Source" TEXT NOT NULL DEFAULT 'local',
    "OutlookLastModified" TEXT NULL,
    "LocalLastModified" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_OutlookEventMappings_CalendarEvents_CalendarEventId" FOREIGN KEY ("CalendarEventId") REFERENCES "CalendarEvents" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_OutlookEventMappings_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);

CREATE TABLE "OutlookSyncSettings" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_OutlookSyncSettings" PRIMARY KEY AUTOINCREMENT,
    "UserId" INTEGER NOT NULL,
    "OutlookEmail" TEXT NULL,
    "OutlookUserId" TEXT NULL,
    "AccessToken" TEXT NULL,
    "RefreshToken" TEXT NULL,
    "TokenExpiresAt" TEXT NULL,
    "SyncEnabled" INTEGER NOT NULL,
    "SyncDirection" TEXT NOT NULL DEFAULT 'both',
    "LastSyncAt" TEXT NULL,
    "LastSyncStatus" TEXT NULL,
    "LastSyncError" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_OutlookSyncSettings_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX "IX_OutlookEventMappings_CalendarEventId_UserId" ON "OutlookEventMappings" ("CalendarEventId", "UserId");

CREATE INDEX "IX_OutlookEventMappings_OutlookEventId" ON "OutlookEventMappings" ("OutlookEventId");

CREATE INDEX "IX_OutlookEventMappings_Source" ON "OutlookEventMappings" ("Source");

CREATE INDEX "IX_OutlookEventMappings_UserId" ON "OutlookEventMappings" ("UserId");

CREATE INDEX "IX_OutlookSyncSettings_LastSyncAt" ON "OutlookSyncSettings" ("LastSyncAt");

CREATE INDEX "IX_OutlookSyncSettings_SyncEnabled" ON "OutlookSyncSettings" ("SyncEnabled");

CREATE UNIQUE INDEX "IX_OutlookSyncSettings_UserId" ON "OutlookSyncSettings" ("UserId") WHERE "DeletedAt" IS NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260801020313_AddOutlookSyncEntities', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "CalendarEvents" ADD "LastReminderSentAt" TEXT NULL;

ALTER TABLE "CalendarEvents" ADD "ReminderEmails" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260801055118_AddCalendarReminderEmails', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "ChatMessages" ADD "EditedAt" TEXT NULL;

ALTER TABLE "ChatMessages" ADD "IsRecalled" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "ChatMessages" ADD "ReadAt" TEXT NULL;

ALTER TABLE "ChatMessages" ADD "RecalledAt" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260801063734_ExtendChatMessageFields', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "CalendarEvents" ADD "RowVersion" BLOB NOT NULL DEFAULT X'';

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260801072221_AddCalendarEventRowVersion', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
DROP INDEX "IX_Conversations_CustomerId";

CREATE TABLE "TimeEntries" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_TimeEntries" PRIMARY KEY AUTOINCREMENT,
    "EntryType" TEXT NOT NULL,
    "Description" TEXT NULL,
    "ConversationId" INTEGER NULL,
    "CustomerId" INTEGER NULL,
    "UserId" INTEGER NOT NULL,
    "MailboxId" INTEGER NULL,
    "StartTime" TEXT NOT NULL,
    "EndTime" TEXT NULL,
    "DurationMinutes" INTEGER NOT NULL,
    "Billable" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    "RowVersion" BLOB NULL,
    CONSTRAINT "FK_TimeEntries_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_TimeEntries_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_TimeEntries_Mailboxes_MailboxId" FOREIGN KEY ("MailboxId") REFERENCES "Mailboxes" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_TimeEntries_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_Conversations_CustomerId" ON "Conversations" ("CustomerId") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_TimeEntries_ConversationId" ON "TimeEntries" ("ConversationId");

CREATE INDEX "IX_TimeEntries_CustomerId" ON "TimeEntries" ("CustomerId");

CREATE INDEX "IX_TimeEntries_EndTime" ON "TimeEntries" ("EndTime");

CREATE INDEX "IX_TimeEntries_MailboxId" ON "TimeEntries" ("MailboxId");

CREATE INDEX "IX_TimeEntries_StartTime" ON "TimeEntries" ("StartTime");

CREATE INDEX "IX_TimeEntries_UserId" ON "TimeEntries" ("UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260801152724_AddTimeEntry', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "CustomReports" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CustomReports" PRIMARY KEY AUTOINCREMENT,
    "Name" TEXT NOT NULL,
    "Description" TEXT NULL,
    "Category" TEXT NOT NULL,
    "Visibility" TEXT NOT NULL,
    "OwnerId" INTEGER NOT NULL,
    "TeamId" INTEGER NULL,
    "ReportConfig" TEXT NOT NULL,
    "IsDashboardPinned" INTEGER NOT NULL,
    "DashboardSortOrder" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_CustomReports_Teams_TeamId" FOREIGN KEY ("TeamId") REFERENCES "Teams" ("Id") ON DELETE SET NULL,
    CONSTRAINT "FK_CustomReports_Users_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE TABLE "CustomReportSchedules" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_CustomReportSchedules" PRIMARY KEY AUTOINCREMENT,
    "CustomReportId" INTEGER NOT NULL,
    "Recurrence" TEXT NOT NULL,
    "WeekDay" INTEGER NULL,
    "SendTime" TEXT NOT NULL,
    "Recipients" TEXT NOT NULL,
    "Subject" TEXT NOT NULL,
    "AttachmentFormat" TEXT NOT NULL,
    "IsActive" INTEGER NOT NULL,
    "LastRunAt" TEXT NULL,
    "NextRunAt" TEXT NULL,
    "OwnerId" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    CONSTRAINT "FK_CustomReportSchedules_CustomReports_CustomReportId" FOREIGN KEY ("CustomReportId") REFERENCES "CustomReports" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_CustomReportSchedules_Users_OwnerId" FOREIGN KEY ("OwnerId") REFERENCES "Users" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_CustomReports_IsDashboardPinned_DashboardSortOrder" ON "CustomReports" ("IsDashboardPinned", "DashboardSortOrder") WHERE "DeletedAt" IS NULL;

CREATE UNIQUE INDEX "IX_CustomReports_Name_OwnerId" ON "CustomReports" ("Name", "OwnerId") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_CustomReports_OwnerId" ON "CustomReports" ("OwnerId") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_CustomReports_TeamId" ON "CustomReports" ("TeamId");

CREATE INDEX "IX_CustomReportSchedules_CustomReportId" ON "CustomReportSchedules" ("CustomReportId");

CREATE INDEX "IX_CustomReportSchedules_IsActive_NextRunAt" ON "CustomReportSchedules" ("IsActive", "NextRunAt") WHERE "DeletedAt" IS NULL;

CREATE INDEX "IX_CustomReportSchedules_OwnerId" ON "CustomReportSchedules" ("OwnerId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260801161753_AddCustomReports', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "ef_temp_KnowledgeArticles" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_KnowledgeArticles" PRIMARY KEY AUTOINCREMENT,
    "AuthorId" INTEGER NULL,
    "CategoryId" INTEGER NULL,
    "Content" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    "DeletedAt" TEXT NULL,
    "IsPinned" INTEGER NOT NULL,
    "LikeCount" INTEGER NOT NULL,
    "PublishedAt" TEXT NULL,
    "SortOrder" INTEGER NOT NULL,
    "Status" TEXT NOT NULL,
    "Summary" TEXT NULL,
    "Tags" TEXT NULL,
    "Title" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    "ViewCount" INTEGER NOT NULL,
    CONSTRAINT "FK_KnowledgeArticles_KnowledgeCategories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "KnowledgeCategories" ("Id"),
    CONSTRAINT "FK_KnowledgeArticles_Users_AuthorId" FOREIGN KEY ("AuthorId") REFERENCES "Users" ("Id") ON DELETE SET NULL
);

INSERT INTO "ef_temp_KnowledgeArticles" ("Id", "AuthorId", "CategoryId", "Content", "CreatedAt", "DeletedAt", "IsPinned", "LikeCount", "PublishedAt", "SortOrder", "Status", "Summary", "Tags", "Title", "UpdatedAt", "ViewCount")
SELECT "Id", "AuthorId", "CategoryId", "Content", "CreatedAt", "DeletedAt", "IsPinned", "LikeCount", "PublishedAt", "SortOrder", "Status", "Summary", "Tags", "Title", "UpdatedAt", "ViewCount"
FROM "KnowledgeArticles";

COMMIT;

PRAGMA foreign_keys = 0;

BEGIN TRANSACTION;
DROP TABLE "KnowledgeArticles";

ALTER TABLE "ef_temp_KnowledgeArticles" RENAME TO "KnowledgeArticles";

COMMIT;

PRAGMA foreign_keys = 1;

BEGIN TRANSACTION;
CREATE INDEX "IX_KnowledgeArticles_AuthorId" ON "KnowledgeArticles" ("AuthorId");

CREATE INDEX "IX_KnowledgeArticles_CategoryId" ON "KnowledgeArticles" ("CategoryId");

CREATE INDEX "IX_KnowledgeArticles_Status" ON "KnowledgeArticles" ("Status");

COMMIT;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260801192456_AddWeChatAndModuleThreadModelChanges', '10.0.9');

BEGIN TRANSACTION;
DROP INDEX "IX_NotificationRules_MailboxId";

DROP INDEX "IX_NotificationRules_UserId";

CREATE TABLE "ConversationReadStates" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ConversationReadStates" PRIMARY KEY AUTOINCREMENT,
    "ConversationId" INTEGER NOT NULL,
    "UserId" INTEGER NOT NULL,
    "LastReadAt" TEXT NOT NULL,
    "UpdatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_ConversationReadStates_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ConversationReadStates_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
);

CREATE INDEX "IX_NotificationRules_EventType_Active_Mailbox_User_Deleted" ON "NotificationRules" ("EventType", "IsActive", "MailboxId", "UserId", "DeletedAt");

CREATE INDEX "IX_NotificationRules_MailboxId_Active_Deleted" ON "NotificationRules" ("MailboxId", "IsActive", "DeletedAt");

CREATE INDEX "IX_NotificationRules_UserId_Active_Deleted" ON "NotificationRules" ("UserId", "IsActive", "DeletedAt");

CREATE UNIQUE INDEX "IX_ConversationReadStates_ConversationId_UserId" ON "ConversationReadStates" ("ConversationId", "UserId");

CREATE INDEX "IX_ConversationReadStates_UserId" ON "ConversationReadStates" ("UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260806014707_AddConversationReadState', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "Customers" ADD "ContactExtractAttempts" INTEGER NOT NULL DEFAULT 0;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260806090243_AddCustomerContactExtractAttempts', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE "ImMessageLogs" (
    "Id" INTEGER NOT NULL CONSTRAINT "PK_ImMessageLogs" PRIMARY KEY AUTOINCREMENT,
    "ConversationId" INTEGER NOT NULL,
    "CustomerId" INTEGER NULL,
    "SourceType" INTEGER NOT NULL,
    "ChannelId" TEXT NOT NULL,
    "MessageText" TEXT NOT NULL,
    "Status" INTEGER NOT NULL DEFAULT 1,
    "AttemptCount" INTEGER NOT NULL DEFAULT 0,
    "MaxAttempts" INTEGER NOT NULL DEFAULT 4,
    "NextRetryAt" TEXT NULL,
    "ErrorMessage" TEXT NULL,
    "ExternalMessageId" TEXT NULL,
    "CreatedAt" TEXT NOT NULL,
    "SentAt" TEXT NULL,
    "UpdatedAt" TEXT NULL,
    CONSTRAINT "CK_ImMessageLog_Status" CHECK ([Status] IN (1, 2, 3, 4)),
    CONSTRAINT "FK_ImMessageLogs_Conversations_ConversationId" FOREIGN KEY ("ConversationId") REFERENCES "Conversations" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_ImMessageLogs_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE RESTRICT
);

CREATE INDEX "IX_ImMessageLogs_ConversationId" ON "ImMessageLogs" ("ConversationId");

CREATE INDEX "IX_ImMessageLogs_CreatedAt" ON "ImMessageLogs" ("CreatedAt");

CREATE INDEX "IX_ImMessageLogs_CustomerId" ON "ImMessageLogs" ("CustomerId");

CREATE INDEX "IX_ImMessageLogs_NextRetryAt" ON "ImMessageLogs" ("NextRetryAt");

CREATE INDEX "IX_ImMessageLogs_SourceType" ON "ImMessageLogs" ("SourceType");

CREATE INDEX "IX_ImMessageLogs_Status" ON "ImMessageLogs" ("Status");

CREATE INDEX "IX_ImMessageLogs_Status_NextRetryAt" ON "ImMessageLogs" ("Status", "NextRetryAt");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260807074858_AddImMessageLog', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
CREATE INDEX "IX_Conversations_SourceType_Status_LastReplyAt" ON "Conversations" ("SourceType", "Status", "LastReplyAt") WHERE "DeletedAt" IS NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260807145043_AddDistributedLockTable', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "CustomFields" ADD "HideWhenChannelDisabled" INTEGER NOT NULL DEFAULT 0;

ALTER TABLE "CustomFields" ADD "SourceTypesJson" TEXT NULL;

ALTER TABLE "ChatSessions" ADD "CustomerPhone" TEXT NOT NULL DEFAULT '';

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260807165854_AddCustomerPhoneToChatSession', '10.0.9');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE "ChatSessions" ADD "RatingRequestedAt" TEXT NULL;

ALTER TABLE "ChatSessions" ADD "RatingTimeoutAt" TEXT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260807183327_AddChatSessionRatingRequestedAtAndTimeoutAt', '10.0.9');

COMMIT;

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

