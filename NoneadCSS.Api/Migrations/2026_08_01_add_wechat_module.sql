-- ============================================
-- NoneadCSS 数据库迁移脚本
-- 版本：2026-08-01
-- 说明：新增 WeChat 集成模块（集成分类，v1.0.0）
--   该脚本幂等可重复执行，已存在时跳过。
-- ============================================

INSERT INTO "Modules" (
    "Name", "Title", "Description", "Version", "Author", "Icon",
    "IsActive", "Category", "Settings", "SortOrder",
    "CreatedAt", "UpdatedAt"
)
SELECT
    'wechat',
    'WeChat 集成',
    '通过微信公众号/小程序接收和回复客户消息，实现多渠道统一管理。',
    '1.0.0',
    'nCSS Team',
    'wechat',
    0,
    'integration',
    NULL,
    12,
    strftime('%Y-%m-%dT%H:%M:%S.0000000Z', 'now'),
    strftime('%Y-%m-%dT%H:%M:%S.0000000Z', 'now')
WHERE NOT EXISTS (
    SELECT 1 FROM "Modules" WHERE "Name" = 'wechat'
);
