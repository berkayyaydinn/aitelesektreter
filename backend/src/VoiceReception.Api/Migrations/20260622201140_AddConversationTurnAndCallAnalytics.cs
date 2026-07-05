using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoiceReception.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationTurnAndCallAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DurationSeconds",
                table: "CallLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EndReason",
                table: "CallLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Outcome",
                table: "CallLogs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ToolCallCount",
                table: "CallLogs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ConversationTurns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CallLogId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "text", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationTurns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversationTurns_CallLogId_OccurredAt",
                table: "ConversationTurns",
                columns: new[] { "CallLogId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConversationTurns");

            migrationBuilder.DropColumn(
                name: "DurationSeconds",
                table: "CallLogs");

            migrationBuilder.DropColumn(
                name: "EndReason",
                table: "CallLogs");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "CallLogs");

            migrationBuilder.DropColumn(
                name: "ToolCallCount",
                table: "CallLogs");
        }
    }
}
