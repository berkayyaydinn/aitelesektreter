using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoiceReception.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RetentionRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RanAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CallLogsDeleted = table.Column<int>(type: "integer", nullable: false),
                    TurnsDeleted = table.Column<int>(type: "integer", nullable: false),
                    RecordingsCleared = table.Column<int>(type: "integer", nullable: false),
                    MessageLogsDeleted = table.Column<int>(type: "integer", nullable: false),
                    AppointmentsAnonymized = table.Column<int>(type: "integer", nullable: false),
                    OrdersAnonymized = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RetentionRuns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MessageLogs_CreatedAt",
                table: "MessageLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CallLogs_StartedAt",
                table: "CallLogs",
                column: "StartedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RetentionRuns");

            migrationBuilder.DropIndex(
                name: "IX_MessageLogs_CreatedAt",
                table: "MessageLogs");

            migrationBuilder.DropIndex(
                name: "IX_CallLogs_StartedAt",
                table: "CallLogs");
        }
    }
}
