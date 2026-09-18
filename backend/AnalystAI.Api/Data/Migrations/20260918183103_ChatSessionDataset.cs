using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnalystAI.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ChatSessionDataset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ActiveDatasetId",
                table: "UserSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DatasetId",
                table: "ChatSessions",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_DatasetId",
                table: "ChatSessions",
                column: "DatasetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatSessions_DatasetId",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "ActiveDatasetId",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "DatasetId",
                table: "ChatSessions");
        }
    }
}
