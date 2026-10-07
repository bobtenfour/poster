using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosterPrintRequest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrintingConsumables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PrintingConsumables",
                columns: table => new
                {
                    PrintingConsumableId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Category = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    Capacity = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    HasExpirationDate = table.Column<bool>(type: "bit", nullable: false),
                    CurrentQuantity = table.Column<int>(type: "int", nullable: false),
                    LowStockThreshold = table.Column<int>(type: "int", nullable: false),
                    CriticalStockThreshold = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintingConsumables", x => x.PrintingConsumableId);
                });

            migrationBuilder.CreateTable(
                name: "PrintingInventorySettings",
                columns: table => new
                {
                    PrintingInventorySettingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ExpirationWarningDays = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintingInventorySettings", x => x.PrintingInventorySettingId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrintingConsumables_Code",
                table: "PrintingConsumables",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrintingConsumables");

            migrationBuilder.DropTable(
                name: "PrintingInventorySettings");
        }
    }
}
