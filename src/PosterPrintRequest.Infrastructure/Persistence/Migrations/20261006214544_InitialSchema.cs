using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosterPrintRequest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    DepartmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.DepartmentId);
                });

            migrationBuilder.CreateTable(
                name: "Reasons",
                columns: table => new
                {
                    ReasonId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequiresMentor = table.Column<bool>(type: "bit", nullable: false),
                    RequiresApprovalSheet = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reasons", x => x.ReasonId);
                });

            migrationBuilder.CreateTable(
                name: "PosterRequests",
                columns: table => new
                {
                    PosterRequestId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PosterId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Mentor = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DepartmentId = table.Column<int>(type: "int", nullable: false),
                    Room = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ReasonId = table.Column<int>(type: "int", nullable: true),
                    LaminationRequested = table.Column<bool>(type: "bit", nullable: false),
                    ApprovalSheetUploaded = table.Column<bool>(type: "bit", nullable: false),
                    DateIn = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosterRequests", x => x.PosterRequestId);
                    table.ForeignKey(
                        name: "FK_PosterRequests_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "DepartmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PosterRequests_Reasons_ReasonId",
                        column: x => x.ReasonId,
                        principalTable: "Reasons",
                        principalColumn: "ReasonId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ApprovalSheets",
                columns: table => new
                {
                    ApprovalSheetId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PosterRequestId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovalSheets", x => x.ApprovalSheetId);
                    table.ForeignKey(
                        name: "FK_ApprovalSheets_PosterRequests_PosterRequestId",
                        column: x => x.PosterRequestId,
                        principalTable: "PosterRequests",
                        principalColumn: "PosterRequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PosterFiles",
                columns: table => new
                {
                    PosterFileId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PosterRequestId = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DetectedFormat = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PageCount = table.Column<int>(type: "int", nullable: false),
                    Width = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    Length = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosterFiles", x => x.PosterFileId);
                    table.ForeignKey(
                        name: "FK_PosterFiles_PosterRequests_PosterRequestId",
                        column: x => x.PosterRequestId,
                        principalTable: "PosterRequests",
                        principalColumn: "PosterRequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PosterProcessings",
                columns: table => new
                {
                    PosterProcessingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PosterRequestId = table.Column<int>(type: "int", nullable: false),
                    ITPerson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Received = table.Column<DateOnly>(type: "date", nullable: true),
                    Printed = table.Column<bool>(type: "bit", nullable: false),
                    Laminated = table.Column<bool>(type: "bit", nullable: false),
                    Notified = table.Column<bool>(type: "bit", nullable: false),
                    DateOut = table.Column<DateOnly>(type: "date", nullable: true),
                    PickedUpBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Comments = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PosterProcessings", x => x.PosterProcessingId);
                    table.ForeignKey(
                        name: "FK_PosterProcessings_PosterRequests_PosterRequestId",
                        column: x => x.PosterRequestId,
                        principalTable: "PosterRequests",
                        principalColumn: "PosterRequestId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovalSheets_PosterRequestId",
                table: "ApprovalSheets",
                column: "PosterRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosterFiles_PosterRequestId",
                table: "PosterFiles",
                column: "PosterRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosterProcessings_PosterRequestId",
                table: "PosterProcessings",
                column: "PosterRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosterRequests_DepartmentId",
                table: "PosterRequests",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_PosterRequests_PosterId",
                table: "PosterRequests",
                column: "PosterId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PosterRequests_ReasonId",
                table: "PosterRequests",
                column: "ReasonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovalSheets");

            migrationBuilder.DropTable(
                name: "PosterFiles");

            migrationBuilder.DropTable(
                name: "PosterProcessings");

            migrationBuilder.DropTable(
                name: "PosterRequests");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropTable(
                name: "Reasons");
        }
    }
}
