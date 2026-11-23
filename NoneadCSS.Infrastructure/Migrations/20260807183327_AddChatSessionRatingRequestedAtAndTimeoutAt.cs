using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NoneadCSS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddChatSessionRatingRequestedAtAndTimeoutAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RatingRequestedAt",
                table: "ChatSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RatingTimeoutAt",
                table: "ChatSessions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RatingRequestedAt",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "RatingTimeoutAt",
                table: "ChatSessions");
        }
    }
}
