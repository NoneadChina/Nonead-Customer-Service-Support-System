-- =============================================================
-- iMessage / Apple Messages 渠道模块注册（镜像 2026_08_03_add_whatsapp_module.sql）
-- 模块默认 IsActive=0：安装后由管理员在「模块管理」里启用（避免开源仓库默认开启外部渠道）。
-- 权限：imessage.view（查看配置/会话）、imessage.manage（回复与管理）。
-- 幂等：全部 IF NOT EXISTS 包裹，可重复执行。
-- =============================================================

-- 1) 模块行
IF NOT EXISTS (SELECT 1 FROM Modules WHERE Name = 'imessage')
INSERT INTO Modules (Name, Title, Description, Version, Author, Icon, IsActive, Category, SortOrder, CreatedAt, UpdatedAt)
VALUES (
    'imessage',
    'iMessage / Apple Messages 集成',
    '接入 Apple Messages：默认走自建 macOS 桥（BlueBubbles REST，需一台常开 Mac）；可选 Apple Messages for Business（AMB）路线，需 Apple 核准的 MSP 资格，默认关闭。',
    '1.0.0',
    'nCSS Team',
    'imessage',
    0,
    'integration',
    15,
    GETUTCDATE(),
    GETUTCDATE()
);
GO

-- 2) 角色权限：agent / viewer 只读
IF NOT EXISTS (
    SELECT 1 FROM RolePermissions rp
    INNER JOIN Roles r ON rp.RoleId = r.Id
    WHERE r.Name = 'agent' AND rp.Permission = 'imessage.view'
)
INSERT INTO RolePermissions (RoleId, Permission)
SELECT r.Id, 'imessage.view' FROM Roles r WHERE r.Name = 'agent' AND r.DeletedAt IS NULL;
GO

IF NOT EXISTS (
    SELECT 1 FROM RolePermissions rp
    INNER JOIN Roles r ON rp.RoleId = r.Id
    WHERE r.Name = 'viewer' AND rp.Permission = 'imessage.view'
)
INSERT INTO RolePermissions (RoleId, Permission)
SELECT r.Id, 'imessage.view' FROM Roles r WHERE r.Name = 'viewer' AND r.DeletedAt IS NULL;
GO

-- 3) 坐席可回复（manage）；viewer 不给 manage
IF NOT EXISTS (
    SELECT 1 FROM RolePermissions rp
    INNER JOIN Roles r ON rp.RoleId = r.Id
    WHERE r.Name = 'agent' AND rp.Permission = 'imessage.manage'
)
INSERT INTO RolePermissions (RoleId, Permission)
SELECT r.Id, 'imessage.manage' FROM Roles r WHERE r.Name = 'agent' AND r.DeletedAt IS NULL;
GO
