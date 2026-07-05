using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoiceReception.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DueDate",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PaymentStatus",
                table: "Invoices",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSentAt",
                table: "Invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSentAt",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PaymentStatus_DueDate",
                table: "Invoices",
                columns: new[] { "PaymentStatus", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_Status_StartUtc",
                table: "Appointments",
                columns: new[] { "Status", "StartUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_PaymentStatus_DueDate",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_Status_StartUtc",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "DueDate",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ReminderSentAt",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ReminderSentAt",
                table: "Appointments");
        }
    }
}
