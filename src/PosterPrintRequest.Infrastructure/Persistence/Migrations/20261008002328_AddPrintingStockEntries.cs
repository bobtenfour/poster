using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosterPrintRequest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPrintingStockEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentQuantity",
                table: "PrintingConsumables");

            migrationBuilder.DropColumn(
                name: "ExpirationDate",
                table: "PrintingConsumables");

            migrationBuilder.CreateTable(
                name: "PrintingStockEntries",
                columns: table => new
                {
                    PrintingStockEntryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PrintingConsumableId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintingStockEntries", x => x.PrintingStockEntryId);
                    table.ForeignKey(
                        name: "FK_PrintingStockEntries_PrintingConsumables_PrintingConsumableId",
                        column: x => x.PrintingConsumableId,
                        principalTable: "PrintingConsumables",
                        principalColumn: "PrintingConsumableId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrintingStockEntries_PrintingConsumableId",
                table: "PrintingStockEntries",
                column: "PrintingConsumableId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrintingStockEntries");

            migrationBuilder.AddColumn<int>(
                name: "CurrentQuantity",
                table: "PrintingConsumables",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExpirationDate",
                table: "PrintingConsumables",
                type: "date",
                nullable: true);
        }
    }
}
