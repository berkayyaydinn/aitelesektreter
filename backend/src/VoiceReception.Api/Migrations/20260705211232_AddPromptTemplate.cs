using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VoiceReception.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPromptTemplate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PromptTemplate",
                table: "Tenants",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PromptTemplate",
                table: "Tenants");
        }
    }
}
