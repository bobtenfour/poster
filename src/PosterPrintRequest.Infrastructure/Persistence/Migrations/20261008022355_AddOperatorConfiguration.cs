using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosterPrintRequest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperatorConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Reasons",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "Reasons",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Departments",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AddColumn<bool>(
                name: "Active",
                table: "Departments",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateTable(
                name: "PrinterModels",
                columns: table => new
                {
                    PrinterModelId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrinterModels", x => x.PrinterModelId);
                });

            migrationBuilder.CreateTable(
                name: "PrinterModelConsumables",
                columns: table => new
                {
                    PrinterModelConsumableId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PrinterModelId = table.Column<int>(type: "int", nullable: false),
                    PrintingConsumableId = table.Column<int>(type: "int", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrinterModelConsumables", x => x.PrinterModelConsumableId);
                    table.ForeignKey(
                        name: "FK_PrinterModelConsumables_PrinterModels_PrinterModelId",
                        column: x => x.PrinterModelId,
                        principalTable: "PrinterModels",
                        principalColumn: "PrinterModelId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrinterModelConsumables_PrintingConsumables_PrintingConsumableId",
                        column: x => x.PrintingConsumableId,
                        principalTable: "PrintingConsumables",
                        principalColumn: "PrintingConsumableId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Reasons_Name",
                table: "Reasons",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrinterModelConsumables_PrinterModelId_PrintingConsumableId",
                table: "PrinterModelConsumables",
                columns: new[] { "PrinterModelId", "PrintingConsumableId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrinterModelConsumables_PrintingConsumableId",
                table: "PrinterModelConsumables",
                column: "PrintingConsumableId");

            migrationBuilder.CreateIndex(
                name: "IX_PrinterModels_Name",
                table: "PrinterModels",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PrinterModelConsumables");

            migrationBuilder.DropTable(
                name: "PrinterModels");

            migrationBuilder.DropIndex(
                name: "IX_Reasons_Name",
                table: "Reasons");

            migrationBuilder.DropIndex(
                name: "IX_Departments_Name",
                table: "Departments");

            migrationBuilder.DropColumn(
                name: "Active",
                table: "Reasons");

            migrationBuilder.DropColumn(
                name: "Active",
                table: "Departments");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Reasons",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Departments",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128);
        }
    }
}
