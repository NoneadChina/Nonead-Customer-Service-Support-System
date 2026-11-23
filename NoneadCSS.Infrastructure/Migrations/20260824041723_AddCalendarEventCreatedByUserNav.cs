using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// 空迁移：仅更新模型快照以包含 CreatedByUser 导航属性关系配置。
    /// 实际数据库列（CreatedBy）已存在于建表迁移中，无需变更 schema。
    /// 之前未纳入快照的字段（DueAt/TaskOwnerId/AgendaIdsJson 等）也已通过
    /// 20260817180000_AddCalendarEventExtendedFields 迁移或手动应用至生产库。
    /// </summary>
    public partial class AddCalendarEventCreatedByUserNav : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 故意留空：所有列已存在于数据库中，此迁移仅用于同步模型快照
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 故意留空
        }
    }
}
