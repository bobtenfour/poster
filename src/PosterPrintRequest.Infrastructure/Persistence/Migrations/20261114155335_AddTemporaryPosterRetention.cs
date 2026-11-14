using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosterPrintRequest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTemporaryPosterRetention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OriginalDirectory",
                table: "PosterRequests",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PermanentlyDeletedAt",
                table: "PosterRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TemporaryDeletionAt",
                table: "PosterRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PosterRetentionAudits",
                columns: table => new
                {
                    PosterRetentionAuditId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PosterRequestId = table.Column<int>(type: "int", nullable: false),
                    PosterId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OriginalDirectory = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosterRetentionAudits", x => x.PosterRetentionAuditId);
                    table.ForeignKey(
                        name: "FK_PosterRetentionAudits_PosterRequests_PosterRequestId",
                        column: x => x.PosterRequestId,
                        principalTable: "PosterRequests",
                        principalColumn: "PosterRequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PosterRetentionAudits_PosterRequestId",
                table: "PosterRetentionAudits",
                column: "PosterRequestId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PosterRetentionAudits");

            migrationBuilder.DropColumn(
                name: "OriginalDirectory",
                table: "PosterRequests");

            migrationBuilder.DropColumn(
                name: "PermanentlyDeletedAt",
                table: "PosterRequests");

            migrationBuilder.DropColumn(
                name: "TemporaryDeletionAt",
                table: "PosterRequests");
        }
    }
}
