-- Telegram Bot API 集成模块种子数据
-- 写法镜像 2026_08_03_add_whatsapp_module.sql（SQL Server 方言：IF NOT EXISTS ... BEGIN ... END + GETUTCDATE()）
-- 字段值与 DatabaseSeeder.cs:1930-1945 的 telegram 模块行完全一致（Title / Description / Version / Icon / Category / SortOrder），
-- 因此本脚本与种子流程重复执行都安全：已存在即跳过，不会覆盖管理员在「模块管理」里的启用状态。

-- 注册 telegram 模块（默认未上架 IsActive=0，需在模块管理里手动启用）
IF NOT EXISTS (SELECT 1 FROM Modules WHERE Name = 'telegram')
BEGIN
    INSERT INTO Modules (Name, Title, Description, Version, Author, Icon, IsActive, Category, SortOrder, CreatedAt, UpdatedAt)
    VALUES (
        'telegram',
        'Telegram 集成',
        '通过 Telegram 机器人接收和回复客户消息，实现多渠道统一管理。',
        '1.2.0',
        'nCSS Team',
        'telegram',
        0,
        'integration',
        2,
        GETUTCDATE(),
        GETUTCDATE()
    );
END

-- 为 agent 角色添加 telegram.view 权限（如不存在）
IF NOT EXISTS (
    SELECT 1 FROM RolePermissions rp
    INNER JOIN Roles r ON rp.RoleId = r.Id
    WHERE r.Name = 'agent' AND rp.Permission = 'telegram.view'
)
BEGIN
    INSERT INTO RolePermissions (RoleId, Permission)
    SELECT r.Id, 'telegram.view'
    FROM Roles r
    WHERE r.Name = 'agent' AND r.DeletedAt IS NULL;
END

-- 为 viewer 角色添加 telegram.view 权限（如不存在）
IF NOT EXISTS (
    SELECT 1 FROM RolePermissions rp
    INNER JOIN Roles r ON rp.RoleId = r.Id
    WHERE r.Name = 'viewer' AND rp.Permission = 'telegram.view'
)
BEGIN
    INSERT INTO RolePermissions (RoleId, Permission)
    SELECT r.Id, 'telegram.view'
    FROM Roles r
    WHERE r.Name = 'viewer' AND r.DeletedAt IS NULL;
END

-- 说明：
--   1) telegram.manage（回复客户、改配置、登记/删除 Webhook）默认不预置给任何角色，由超级管理员按需分配
--      （与 whatsapp/line 同一口径：配置写入接口另有 [RequireSuperAdmin] 双保险）。
--   2) 渠道配置全部落 SystemOptions（Category='telegram'，键名 telegram.*），凭据以 'ENC:' 前缀密文存储，
--      本脚本不写入任何 BotToken / WebhookSecretToken，也不硬编码任何域名。
--   3) 会话匹配窗口在 TelegramOptions.ConversationMatchWindowHours（同样可被 SystemOptions 覆盖），
--      无需新增表或列：客户身份走 Customers.Channel='telegram' + Customers.ChannelId=chat_id，
--      正文走 ConversationThreads，附件走 Attachments。
