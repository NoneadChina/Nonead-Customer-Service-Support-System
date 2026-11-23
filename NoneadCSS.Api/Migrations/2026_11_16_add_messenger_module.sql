-- Messenger 集成模块种子数据
-- 执行前确认 Modules 表已存在

-- 注册 Messenger 模块（如不存在）
IF NOT EXISTS (SELECT 1 FROM Modules WHERE Name = 'messenger')
BEGIN
    INSERT INTO Modules (Name, Title, Description, Version, Author, Icon, IsActive, Category, SortOrder, CreatedAt, UpdatedAt)
    VALUES (
        'messenger',
        'Messenger 集成',
        '通过 Facebook Messenger Platform（Graph API）收发客户消息，支持图片、音频、视频、文件与位置，含 24 小时会话窗口与 message_tag 管控。',
        '1.0.0',
        'nCSS Team',
        'messenger',
        0,
        'integration',
        15,
        GETUTCDATE(),
        GETUTCDATE()
    );
END

-- 为 agent 角色添加 messenger.view 权限（如不存在）
IF NOT EXISTS (
    SELECT 1 FROM RolePermissions rp
    INNER JOIN Roles r ON rp.RoleId = r.Id
    WHERE r.Name = 'agent' AND rp.Permission = 'messenger.view'
)
BEGIN
    INSERT INTO RolePermissions (RoleId, Permission)
    SELECT r.Id, 'messenger.view'
    FROM Roles r
    WHERE r.Name = 'agent' AND r.DeletedAt IS NULL;
END

-- 为 agent 角色添加 messenger.manage 权限（如不存在）
IF NOT EXISTS (
    SELECT 1 FROM RolePermissions rp
    INNER JOIN Roles r ON rp.RoleId = r.Id
    WHERE r.Name = 'agent' AND rp.Permission = 'messenger.manage'
)
BEGIN
    INSERT INTO RolePermissions (RoleId, Permission)
    SELECT r.Id, 'messenger.manage'
    FROM Roles r
    WHERE r.Name = 'agent' AND r.DeletedAt IS NULL;
END

-- 为 viewer 角色添加 messenger.view 权限（如不存在）
IF NOT EXISTS (
    SELECT 1 FROM RolePermissions rp
    INNER JOIN Roles r ON rp.RoleId = r.Id
    WHERE r.Name = 'viewer' AND rp.Permission = 'messenger.view'
)
BEGIN
    INSERT INTO RolePermissions (RoleId, Permission)
    SELECT r.Id, 'messenger.view'
    FROM Roles r
    WHERE r.Name = 'viewer' AND r.DeletedAt IS NULL;
END
