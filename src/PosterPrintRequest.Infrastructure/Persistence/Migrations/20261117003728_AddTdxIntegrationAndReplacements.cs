using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosterPrintRequest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTdxIntegrationAndReplacements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PosterTitle",
                table: "PosterRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TdxClaimedUntil",
                table: "PosterRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TdxTicketId",
                table: "PosterRequests",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TdxTicketState",
                table: "PosterRequests",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.CreateTable(
                name: "PosterReplacements",
                columns: table => new
                {
                    PosterReplacementId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PosterRequestId = table.Column<int>(type: "int", nullable: false),
                    ReplacedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ArchivedFilePath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ReplacedByObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosterReplacements", x => x.PosterReplacementId);
                    table.ForeignKey(
                        name: "FK_PosterReplacements_PosterRequests_PosterRequestId",
                        column: x => x.PosterRequestId,
                        principalTable: "PosterRequests",
                        principalColumn: "PosterRequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PosterRequests_TdxTicketId",
                table: "PosterRequests",
                column: "TdxTicketId",
                unique: true,
                filter: "[TdxTicketId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PosterReplacements_PosterRequestId",
                table: "PosterReplacements",
                column: "PosterRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PosterReplacements");

            migrationBuilder.DropIndex(
                name: "IX_PosterRequests_TdxTicketId",
                table: "PosterRequests");

            migrationBuilder.DropColumn(
                name: "PosterTitle",
                table: "PosterRequests");

            migrationBuilder.DropColumn(
                name: "TdxClaimedUntil",
                table: "PosterRequests");

            migrationBuilder.DropColumn(
                name: "TdxTicketId",
                table: "PosterRequests");

            migrationBuilder.DropColumn(
                name: "TdxTicketState",
                table: "PosterRequests");
        }
    }
}
