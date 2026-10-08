using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosterPrintRequest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ProtectConfigurationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PrintingStockEntries_PrintingConsumables_PrintingConsumableId",
                table: "PrintingStockEntries");

            migrationBuilder.AddColumn<string>(
                name: "DepartmentName",
                table: "PosterRequests",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ReasonName",
                table: "PosterRequests",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE request
                SET DepartmentName = department.Name
                FROM PosterRequests AS request
                INNER JOIN Departments AS department ON department.DepartmentId = request.DepartmentId;

                UPDATE request
                SET ReasonName = reason.Name
                FROM PosterRequests AS request
                INNER JOIN Reasons AS reason ON reason.ReasonId = request.ReasonId;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "DepartmentName",
                table: "PosterRequests",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(128)",
                oldMaxLength: 128,
                oldDefaultValue: "");

            migrationBuilder.AddForeignKey(
                name: "FK_PrintingStockEntries_PrintingConsumables_PrintingConsumableId",
                table: "PrintingStockEntries",
                column: "PrintingConsumableId",
                principalTable: "PrintingConsumables",
                principalColumn: "PrintingConsumableId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PrintingStockEntries_PrintingConsumables_PrintingConsumableId",
                table: "PrintingStockEntries");

            migrationBuilder.DropColumn(
                name: "DepartmentName",
                table: "PosterRequests");

            migrationBuilder.DropColumn(
                name: "ReasonName",
                table: "PosterRequests");

            migrationBuilder.AddForeignKey(
                name: "FK_PrintingStockEntries_PrintingConsumables_PrintingConsumableId",
                table: "PrintingStockEntries",
                column: "PrintingConsumableId",
                principalTable: "PrintingConsumables",
                principalColumn: "PrintingConsumableId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
