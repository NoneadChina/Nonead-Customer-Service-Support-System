using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInReplyToAndReferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InReplyTo",
                table: "SendLogs",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
            
            migrationBuilder.AddColumn<string>(
                name: "References",
                table: "SendLogs",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InReplyTo",
                table: "SendLogs");
            
            migrationBuilder.DropColumn(
                name: "References",
                table: "SendLogs");
        }
    }
}