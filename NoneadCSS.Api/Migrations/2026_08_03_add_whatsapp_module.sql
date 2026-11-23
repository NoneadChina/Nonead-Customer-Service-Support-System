-- WhatsApp 集成模块种子数据
-- 执行前确认 Modules 表已存在

-- 注册 WhatsApp 模块（如不存在）
IF NOT EXISTS (SELECT 1 FROM Modules WHERE Name = 'whatsapp')
BEGIN
    INSERT INTO Modules (Name, Title, Description, Version, Author, Icon, IsActive, Category, SortOrder, CreatedAt, UpdatedAt)
    VALUES (
        'whatsapp',
        'WhatsApp 集成',
        '通过 WhatsApp Business API 收发客户消息，支持图片、文档等附件。',
        '1.1.0',
        'nCSS Team',
        'whatsapp',
        0,
        'integration',
        3,
        GETUTCDATE(),
        GETUTCDATE()
    );
END

-- 为 agent 角色添加 whatsapp.view 权限（如不存在）
IF NOT EXISTS (
    SELECT 1 FROM RolePermissions rp
    INNER JOIN Roles r ON rp.RoleId = r.Id
    WHERE r.Name = 'agent' AND rp.Permission = 'whatsapp.view'
)
BEGIN
    INSERT INTO RolePermissions (RoleId, Permission)
    SELECT r.Id, 'whatsapp.view'
    FROM Roles r
    WHERE r.Name = 'agent' AND r.DeletedAt IS NULL;
END

-- 为 viewer 角色添加 whatsapp.view 权限（如不存在）
IF NOT EXISTS (
    SELECT 1 FROM RolePermissions rp
    INNER JOIN Roles r ON rp.RoleId = r.Id
    WHERE r.Name = 'viewer' AND rp.Permission = 'whatsapp.view'
)
BEGIN
    INSERT INTO RolePermissions (RoleId, Permission)
    SELECT r.Id, 'whatsapp.view'
    FROM Roles r
    WHERE r.Name = 'viewer' AND r.DeletedAt IS NULL;
END
