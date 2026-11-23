-- LINE Messaging API 集成模块种子数据
-- 执行前确认 Modules 表已存在（镜像 2026_08_03_add_whatsapp_module.sql 的写法）

-- 注册 line 模块（如不存在；默认未上架，需在模块管理中手动启用）
IF NOT EXISTS (SELECT 1 FROM Modules WHERE Name = 'line')
BEGIN
    INSERT INTO Modules (Name, Title, Description, Version, Author, Icon, IsActive, Category, SortOrder, CreatedAt, UpdatedAt)
    VALUES (
        'line',
        'LINE 集成',
        '通过 LINE Messaging API 收发客户消息，支持图片与文件附件（附件经本部署公网 HTTPS 直链下发）。',
        '1.0.0',
        'nCSS Team',
        'line',
        0,
        'integration',
        6,
        GETUTCDATE(),
        GETUTCDATE()
    );
END

-- 为 agent 角色添加 line.view 权限（如不存在）
IF NOT EXISTS (
    SELECT 1 FROM RolePermissions rp
    INNER JOIN Roles r ON rp.RoleId = r.Id
    WHERE r.Name = 'agent' AND rp.Permission = 'line.view'
)
BEGIN
    INSERT INTO RolePermissions (RoleId, Permission)
    SELECT r.Id, 'line.view'
    FROM Roles r
    WHERE r.Name = 'agent' AND r.DeletedAt IS NULL;
END

-- 为 viewer 角色添加 line.view 权限（如不存在）
IF NOT EXISTS (
    SELECT 1 FROM RolePermissions rp
    INNER JOIN Roles r ON rp.RoleId = r.Id
    WHERE r.Name = 'viewer' AND rp.Permission = 'line.view'
)
BEGIN
    INSERT INTO RolePermissions (RoleId, Permission)
    SELECT r.Id, 'line.view'
    FROM Roles r
    WHERE r.Name = 'viewer' AND r.DeletedAt IS NULL;
END

-- 说明：line.manage（回复/推送/改配置）默认不预置给任何角色，由超级管理员按需分配；
--       渠道配置项全部走 SystemOptions（category='line'），本脚本不写入任何凭据。
