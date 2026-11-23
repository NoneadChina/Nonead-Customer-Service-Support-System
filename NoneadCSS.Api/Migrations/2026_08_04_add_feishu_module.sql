-- 飞书集成模块种子数据
-- 执行前确认 Modules 表已存在

-- 注册飞书模块（如不存在）
IF NOT EXISTS (SELECT 1 FROM Modules WHERE Name = 'feishu')
BEGIN
    INSERT INTO Modules (Name, Title, Description, Version, Author, Icon, IsActive, Category, SortOrder, CreatedAt, UpdatedAt)
    VALUES (
        'feishu',
        'Feishu 集成',
        '通过飞书机器人应用接收和回复客户消息，支持文本、图片、文件等消息类型。',
        '1.0.0',
        'nCSS Team',
        'feishu',
        0,
        'integration',
        4,
        GETUTCDATE(),
        GETUTCDATE()
    );
END

-- 为 agent 角色添加 feishu.view 权限（如不存在）
IF NOT EXISTS (
    SELECT 1 FROM RolePermissions rp
    INNER JOIN Roles r ON rp.RoleId = r.Id
    WHERE r.Name = 'agent' AND rp.Permission = 'feishu.view'
)
BEGIN
    INSERT INTO RolePermissions (RoleId, Permission)
    SELECT r.Id, 'feishu.view'
    FROM Roles r
    WHERE r.Name = 'agent' AND r.DeletedAt IS NULL;
END

-- 为 viewer 角色添加 feishu.view 权限（如不存在）
IF NOT EXISTS (
    SELECT 1 FROM RolePermissions rp
    INNER JOIN Roles r ON rp.RoleId = r.Id
    WHERE r.Name = 'viewer' AND rp.Permission = 'feishu.view'
)
BEGIN
    INSERT INTO RolePermissions (RoleId, Permission)
    SELECT r.Id, 'feishu.view'
    FROM Roles r
    WHERE r.Name = 'viewer' AND r.DeletedAt IS NULL;
END
