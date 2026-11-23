using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// 为自定义字段新增"适用渠道"维度：
    /// - SourceTypesJson: 渠道 JSON 数组（如 ["wechat","feishu"]），null=全局
    /// - HideWhenChannelDisabled: 渠道关闭后是否自动隐藏
    /// 原 MailboxId 字段保留以保持向后兼容，但新逻辑以 SourceTypesJson 为准。
    /// </summary>
    public partial class AddCustomFieldSourceTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 适用渠道 JSON 数组：null 表示全局可用
            migrationBuilder.AddColumn<string>(
                name: "SourceTypesJson",
                table: "CustomFields",
                type: "TEXT",
                nullable: true);

            // 渠道关闭后是否自动隐藏该字段，默认 false 保持原行为
            migrationBuilder.AddColumn<bool>(
                name: "HideWhenChannelDisabled",
                table: "CustomFields",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // 按渠道过滤时索引加速（部分匹配）
            migrationBuilder.CreateIndex(
                name: "IX_CustomFields_SourceTypesJson",
                table: "CustomFields",
                column: "SourceTypesJson");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CustomFields_SourceTypesJson",
                table: "CustomFields");

            migrationBuilder.DropColumn(
                name: "SourceTypesJson",
                table: "CustomFields");

            migrationBuilder.DropColumn(
                name: "HideWhenChannelDisabled",
                table: "CustomFields");
        }
    }
}
