using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// 为 CalendarEvents 表添加 7 个事件类型专属字段（2026-08-17）：
    ///   - HostUserIdsJson     (TEXT, 主持人用户 ID 数组 JSON, 会议类型)
    ///   - AgendaIdsJson       (TEXT, 议程 ID 数组 JSON, 会议类型)
    ///   - TaskOwnerId         (INTEGER, 任务负责人 userId, 任务类型)
    ///   - DueAt               (TEXT, 任务截止时间, 任务类型, 区别于 EndAt)
    ///   - ReminderType        (TEXT, 提醒方式 popup/email/sms, 提醒类型)
    ///   - WeekdaysSelectedJson(TEXT, 每周哪几天 JSON 数组, 重复面板)
    ///   - RecurrenceRule      (TEXT, RRULE 原始字符串, 重复面板)
    ///
    /// 历史背景：
    ///   前端 EditorPanel.tsx 已有这些 UI 字段，但 handleSave payload 未拼装、后端实体/DTO 也无对应列，
    ///   导致用户输入的主持人/议程/任务负责人/截止时间/提醒方式/重复规则保存即丢失。
    ///
    /// 部署须知（生产环境）：
    ///   生产环境已通过 FixMigrationHistoryAsync 的 schema probe 自动检测本迁移列；
    ///   若已手动 ALTER TABLE 添加过这些列，probe 会命中并跳过 DDL，避免 duplicate column 错误。
    /// </summary>
    [DbContext(typeof(NoneadCSS.Infrastructure.Persistence.NoneadCSSDbContext))]
    [Migration("20260817180000_AddCalendarEventExtendedFields")]
    public partial class AddCalendarEventExtendedFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 会议类型：主持人 + 议程
            migrationBuilder.AddColumn<string>(
                name: "HostUserIdsJson",
                table: "CalendarEvents",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgendaIdsJson",
                table: "CalendarEvents",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            // 任务类型：负责人 + 截止时间
            migrationBuilder.AddColumn<int>(
                name: "TaskOwnerId",
                table: "CalendarEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DueAt",
                table: "CalendarEvents",
                type: "TEXT",
                nullable: true);

            // 提醒类型：方式
            migrationBuilder.AddColumn<string>(
                name: "ReminderType",
                table: "CalendarEvents",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            // 重复面板：每周哪几天 + RRULE
            migrationBuilder.AddColumn<string>(
                name: "WeekdaysSelectedJson",
                table: "CalendarEvents",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecurrenceRule",
                table: "CalendarEvents",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SQLite 3.35+ 支持 DROP COLUMN，但旧版本需表重建。
            // 这里直接 DropColumn；若生产 SQLite 版本过低需手动表重建。
            migrationBuilder.DropColumn(
                name: "HostUserIdsJson",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "AgendaIdsJson",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "TaskOwnerId",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "DueAt",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "ReminderType",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "WeekdaysSelectedJson",
                table: "CalendarEvents");

            migrationBuilder.DropColumn(
                name: "RecurrenceRule",
                table: "CalendarEvents");
        }
    }
}
