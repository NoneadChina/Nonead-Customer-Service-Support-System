-- =============================================================================
-- 使用前必改：把下面的占位符换成你自己的值（本文件是模板，不含任何真实域名/主机名）
--   <INTERNAL_HOST>          内部渠道/网关主机名，如 css.corp.example.com
--   <INTERNAL_DOMAIN>        内部域名根，如 corp.example.com
--   <PUBLIC_HOST>            对外站点主机名，如 css.example.com
--   <INTERNAL_MAIL_DOMAIN>   内部邮箱域，如 example.com
--   <KNOWN_OLD_PASSWORD>     需要一并判定为“弱口令遗留”的历史默认口令（没有就删掉该行校验）
-- 替换完成后再在数据库副本上执行；执行末尾的校验查询必须全部返回 0。
-- =============================================================================
-- =============================================================================
-- oss-db-scrub.sql — 开源发布前对「演示数据库副本」做脱敏
-- Sanitize a COPY of the database before publishing it with the open-source repo.
-- =============================================================================
-- ⚠️ 只在副本上执行，绝不在生产库执行。 / Run this on a COPY, never on production.
--
--   SQLite (仓库默认演示库):  sqlite3 data/noneadcss.db < scripts/oss-db-scrub.sql
--   SQL Server:  列名相同即可用，但把 `randomblob(...)` 换成 `NEWID()`，
--                并把 `DELETE FROM <t>;` 的分节顺序按外键关系调整。
--
-- 本脚本做四件事 / It does four things:
--   1) 清空所有渠道与邮件账号的凭据（含 "ENC:" 密文）  — Section 1–2
--   2) 删除一次性令牌、API Key、会话令牌                — Section 3
--   3) 把内网域名/内部邮箱泛化                          — Section 4
--   4) 个人数据与自由文本（会话正文、审计、日志）处理   — Section 5–6
-- 第 7 节是验收查询；跑完必须全部为 0 才可以发布。
--
-- 建议：更干净的做法是**不要发布任何数据库转储**，让使用者首次启动时由
-- NoneadCSS.Api/DatabaseSeeder.cs 自行播种演示数据。本脚本只服务于确实需要
-- 附带演示数据的情形。
-- =============================================================================

PRAGMA foreign_keys = OFF;
BEGIN TRANSACTION;

-- -----------------------------------------------------------------------------
-- 1) 渠道凭据：SystemOptions 里的所有渠道密钥（DB 覆盖层）
--    渠道配置的 key 命名规范见 NoneadCSS.Application/Services/I*SettingsProvider.cs
--    （wechat.* / whatsapp.* / feishu.* / dingtalk.* / messenger.* / telegram.* / line.* / imessage.*）
--    密文值带 "ENC:" 前缀（见 NoneadCSS.Infrastructure/Services/WhatsApp/WhatsAppSettingsProvider.cs）。
-- -----------------------------------------------------------------------------
UPDATE SystemOptions
   SET Value = ''
 WHERE Name LIKE 'wechat.%'
    OR Name LIKE 'whatsapp.%'
    OR Name LIKE 'feishu.%'
    OR Name LIKE 'dingding.%'
    OR Name LIKE 'dingtalk.%'
    OR Name LIKE 'messenger.%'
    OR Name LIKE 'telegram.%'
    OR Name LIKE 'line.%'
    OR Name LIKE 'imessage.%'
    OR Name LIKE 'dingtalk_stream.%'
    OR Name LIKE '%access_token%'
    OR Name LIKE '%app_secret%'
    OR Name LIKE '%encoding_aes_key%'
    OR Name LIKE '%verify_token%'
    OR Name LIKE '%proxy%'
    OR Name LIKE '%password%'
    OR Name LIKE '%secret%'
    OR Value LIKE 'ENC:%';

-- 回调地址里可能残留内网/自建中转域名
UPDATE SystemOptions
   SET Value = REPLACE(REPLACE(REPLACE(REPLACE(Value,
        '<INTERNAL_HOST>', 'css.example.com'),
        '<INTERNAL_DOMAIN>', 'example.com'),
        '<PUBLIC_HOST>', 'css.example.com'),
        '@<INTERNAL_MAIL_DOMAIN>', '@example.com')
 WHERE Value LIKE '%<INTERNAL_DOMAIN>%';

-- 旧的微信中转配置整行作废（直连模式已不需要 ProxyBaseUrl / ProxyApiKey）
DELETE FROM SystemOptions
 WHERE LOWER(Name) LIKE '%proxy%';

-- -----------------------------------------------------------------------------
-- 2) 邮箱账号凭据（IMAP/SMTP 口令、OAuth 令牌）
--    列定义见 NoneadCSS.Domain/Entities/Mailbox.cs、OutlookSyncSetting.cs
-- -----------------------------------------------------------------------------
UPDATE Mailboxes
   SET InPassword        = '',
       OutPassword       = '',
       OauthAccessToken  = '',
       OauthRefreshToken = '',
       OutlookDeltaToken = NULL
 WHERE Id > 0;

UPDATE OutlookSyncSettings
   SET AccessToken   = NULL,
       RefreshToken  = NULL,
       DeltaToken    = NULL,
       ClientSecret  = NULL
 WHERE Id > 0;

-- 展示用的邮箱地址本身不是密钥，但会暴露真实域名：统一换成 example.com
UPDATE Mailboxes          SET Email          = REPLACE(Email, '@<INTERNAL_MAIL_DOMAIN>', '@example.com')            WHERE Email LIKE '%@<INTERNAL_MAIL_DOMAIN>';
UPDATE OutlookSyncSettings SET OutlookEmail = REPLACE(OutlookEmail, '@<INTERNAL_MAIL_DOMAIN>', '@example.com')      WHERE OutlookEmail LIKE '%@<INTERNAL_MAIL_DOMAIN>';

-- -----------------------------------------------------------------------------
-- 3) 一次性令牌 / API Key / 会话令牌：直接删，不做替换
-- -----------------------------------------------------------------------------
DELETE FROM ApiKeys;
DELETE FROM ApiKeyLogs;
DELETE FROM RefreshTokens;
DELETE FROM PasswordResetTokens;
DELETE FROM WebhookLogs;
DELETE FROM WorkflowExecutionLogs;
DELETE FROM MacroExecutionLogs;
DELETE FROM SatisfactionRatings;   -- 含评价令牌与客户邮箱，公开库里没有保留价值
DELETE FROM ChatMessages;
DELETE FROM ChatSessions;          -- SessionToken 是免登录入口，必须随会话一起清掉

-- 对外 Webhook：目标地址与签名密钥都属敏感配置
UPDATE Webhooks SET Secret = NULL, Url = 'https://example.com/your-inbound-hook' WHERE Id > 0;

-- -----------------------------------------------------------------------------
-- 4) 内网域名 / 内部邮箱 泛化（自由文本列）
-- -----------------------------------------------------------------------------
UPDATE Users            SET Email = REPLACE(Email, '@<INTERNAL_MAIL_DOMAIN>', '@example.com')            WHERE Email LIKE '%@<INTERNAL_MAIL_DOMAIN>';
UPDATE Emails           SET EmailAddress = REPLACE(EmailAddress, '@<INTERNAL_MAIL_DOMAIN>', '@example.com') WHERE EmailAddress LIKE '%@<INTERNAL_MAIL_DOMAIN>';
UPDATE Conversations    SET CustomerEmail = REPLACE(CustomerEmail, '@<INTERNAL_MAIL_DOMAIN>', '@example.com') WHERE CustomerEmail LIKE '%@<INTERNAL_MAIL_DOMAIN>';
UPDATE SendLogs         SET Email = REPLACE(Email, '@<INTERNAL_MAIL_DOMAIN>', '@example.com')            WHERE Email LIKE '%@<INTERNAL_MAIL_DOMAIN>';
UPDATE CalendarEvents   SET ReminderEmails = REPLACE(ReminderEmails, '@<INTERNAL_MAIL_DOMAIN>', '@example.com') WHERE ReminderEmails LIKE '%@<INTERNAL_MAIL_DOMAIN>';

-- 任何仍写着内网域名的自由文本（工单正文、模板、快捷回复、审计值、自动化参数…）
UPDATE ConversationThreads SET Content  = REPLACE(REPLACE(REPLACE(Content,  '<INTERNAL_HOST>','css.example.com'),'<INTERNAL_DOMAIN>','example.com'),'@<INTERNAL_MAIL_DOMAIN>','@example.com') WHERE Content  LIKE '%<INTERNAL_DOMAIN>%';
UPDATE MailTemplates        SET Body   = REPLACE(REPLACE(REPLACE(Body,      '<INTERNAL_HOST>','css.example.com'),'<INTERNAL_DOMAIN>','example.com'),'@<INTERNAL_MAIL_DOMAIN>','@example.com') WHERE Body      LIKE '%<INTERNAL_DOMAIN>%';
UPDATE MailTemplates        SET Subject= REPLACE(REPLACE(REPLACE(Subject,   '<INTERNAL_HOST>','css.example.com'),'<INTERNAL_DOMAIN>','example.com'),'@<INTERNAL_MAIL_DOMAIN>','@example.com') WHERE Subject   LIKE '%<INTERNAL_DOMAIN>%';
UPDATE CannedResponses      SET Content  = REPLACE(REPLACE(REPLACE(Content, '<INTERNAL_HOST>','css.example.com'),'<INTERNAL_DOMAIN>','example.com'),'@<INTERNAL_MAIL_DOMAIN>','@example.com') WHERE Content   LIKE '%<INTERNAL_DOMAIN>%';
UPDATE NotificationTemplates SET Body   = REPLACE(REPLACE(REPLACE(Body,     '<INTERNAL_HOST>','css.example.com'),'<INTERNAL_DOMAIN>','example.com'),'@<INTERNAL_MAIL_DOMAIN>','@example.com') WHERE Body      LIKE '%<INTERNAL_DOMAIN>%';
UPDATE Workflows            SET ActionsJson = REPLACE(REPLACE(REPLACE(ActionsJson,'<INTERNAL_HOST>','css.example.com'),'<INTERNAL_DOMAIN>','example.com'),'@<INTERNAL_MAIL_DOMAIN>','@example.com') WHERE ActionsJson LIKE '%<INTERNAL_DOMAIN>%';
UPDATE ImMessageLogs        SET MessageText = REPLACE(REPLACE(REPLACE(MessageText,'<INTERNAL_HOST>','css.example.com'),'<INTERNAL_DOMAIN>','example.com'),'@<INTERNAL_MAIL_DOMAIN>','@example.com') WHERE MessageText LIKE '%<INTERNAL_DOMAIN>%';
UPDATE AuditLogs            SET OldValues = REPLACE(REPLACE(REPLACE(OldValues,'<INTERNAL_HOST>','css.example.com'),'<INTERNAL_DOMAIN>','example.com'),'@<INTERNAL_MAIL_DOMAIN>','@example.com') WHERE OldValues LIKE '%<INTERNAL_DOMAIN>%';
UPDATE AuditLogs            SET NewValues = REPLACE(REPLACE(REPLACE(NewValues,'<INTERNAL_HOST>','css.example.com'),'<INTERNAL_DOMAIN>','example.com'),'@<INTERNAL_MAIL_DOMAIN>','@example.com') WHERE NewValues LIKE '%<INTERNAL_DOMAIN>%';

-- -----------------------------------------------------------------------------
-- 5) 个人信息（PII）
--    渠道内外部 ID（PSID / telegram user id / LINE userId / 微信 openid / iMessage handle）
--    都是可关联到真人的标识：公开演示库里替换成假 ID。
-- -----------------------------------------------------------------------------
UPDATE ImMessageLogs   SET ChannelId = 'scrubbed-' || lower(hex(randomblob(8))),
                           ExternalMessageId = NULL,
                           ErrorMessage = NULL
                         WHERE ChannelId IS NOT NULL AND ChannelId <> '';
UPDATE Customers       SET Phones = ''                          WHERE Phones IS NOT NULL AND Phones <> '';
UPDATE SatisfactionRatings SET CustomerEmail = NULL             WHERE CustomerEmail IS NOT NULL;
UPDATE CalendarEvents  SET MeetingUrl = NULL                    WHERE MeetingUrl IS NOT NULL;
UPDATE AuditLogs       SET IpAddress = '', UserAgent = '', HmacSignature = '' WHERE Id > 0;

-- 附件：库内元数据删掉，磁盘文件也要一起删
--   对应目录：Data/Attachments/ 与 wwwroot/attachments/（见 .gitignore）
DELETE FROM Attachments;

-- 审计与工单正文（可选，最彻底的做法是整表清空）：
-- DELETE FROM AuditLogs;
-- DELETE FROM ConversationThreads;
-- DELETE FROM Conversations;
-- DELETE FROM Emails;
-- DELETE FROM Customers;

-- -----------------------------------------------------------------------------
-- 6) 口令：把所有人的密码改成「不可登录」占位值
--    历史库里可能存有弱口令的 BCrypt 哈希，且该明文口令曾出现在旧文档里。
--    保留占位串 → 任何人都无法用旧口令登录；演示账号请按下列方式之一重建：
--      a) 删库后首次启动，让 DatabaseSeeder 用 ADMIN_EMAIL / ADMIN_PASSWORD 播种；或
--      b) 用应用内的「重置密码」流程生成新哈希后回填。
-- -----------------------------------------------------------------------------
UPDATE Users SET Password = 'INVALIDATED-PUBLISHED-BUILD-ROTATE-ME' WHERE Id > 0;

COMMIT;

-- -----------------------------------------------------------------------------
-- 7) 验收：以下每一条都必须返回 0，否则不要发布
-- -----------------------------------------------------------------------------
SELECT 'systemoptions_creds_left'   AS check_name, COUNT(*) AS n FROM SystemOptions  WHERE Value LIKE 'ENC:%' OR Value LIKE '%<INTERNAL_DOMAIN>%'
UNION ALL SELECT 'mailboxes_creds_left',    COUNT(*) FROM Mailboxes           WHERE InPassword <> '' OR OutPassword <> '' OR OauthAccessToken <> '' OR OauthRefreshToken <> ''
UNION ALL SELECT 'internal_domain_any',     (SELECT COUNT(*) FROM SystemOptions WHERE Value LIKE '%<INTERNAL_DOMAIN>%')
                                            + (SELECT COUNT(*) FROM Conversations WHERE CustomerEmail LIKE '%@<INTERNAL_MAIL_DOMAIN>')
                                            + (SELECT COUNT(*) FROM Users WHERE Email LIKE '%@<INTERNAL_MAIL_DOMAIN>' OR Password = '<KNOWN_OLD_PASSWORD>')
UNION ALL SELECT 'apikies_left',            COUNT(*) FROM ApiKeys
UNION ALL SELECT 'refreshtokens_left',      COUNT(*) FROM RefreshTokens
UNION ALL SELECT 'chatsessions_left',       COUNT(*) FROM ChatSessions
UNION ALL SELECT 'attachments_left',        COUNT(*) FROM Attachments
UNION ALL SELECT 'weak_pw_left',            COUNT(*) FROM Users WHERE Password NOT LIKE 'INVALIDATED%' AND Password <> '';

PRAGMA foreign_keys = ON;
-- EOF
