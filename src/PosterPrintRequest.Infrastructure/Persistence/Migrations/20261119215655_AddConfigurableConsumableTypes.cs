using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PosterPrintRequest.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConfigurableConsumableTypes : Migration
    {
        /// <inheritdoc />
        /// <summary>
        /// Consumables Inventory Contract section 5.1: a row is recognized only when its Category has the same byte length as
        /// one of the three literals and is equal to it under a binary collation. Equal byte length rules out the trailing-space
        /// padding of SQL Server comparisons, so both conditions together mean the exact same string.
        /// </summary>
        public const string Recognized =
            "[Category] IS NOT NULL AND ("
            + "(DATALENGTH([Category]) = DATALENGTH(N'Cartridge') AND [Category] COLLATE Latin1_General_100_BIN2 = N'Cartridge') OR "
            + "(DATALENGTH([Category]) = DATALENGTH(N'Paper') AND [Category] COLLATE Latin1_General_100_BIN2 = N'Paper') OR "
            + "(DATALENGTH([Category]) = DATALENGTH(N'Laminating') AND [Category] COLLATE Latin1_General_100_BIN2 = N'Laminating'))";

        /// <summary>Section 5.2: stop, listing every unrecognized value with its byte length, its bytes and its rows.</summary>
        public const string ValidateCategories = """
            DECLARE @unrecognized nvarchar(max);
            SELECT @unrecognized = STRING_AGG(CONVERT(nvarchar(max), CONCAT(
                    N'[', ISNULL([Value], N'<NULL>'), N'] bytes=', ISNULL(CONVERT(nvarchar(20), DATALENGTH([Value])), N'NULL'),
                    N' hex=', ISNULL(CONVERT(nvarchar(300), [Bytes], 1), N'NULL'), N' rows=', [Rows])), N'; ')
            FROM (
                SELECT MIN([Category]) AS [Value], CONVERT(varbinary(128), [Category]) AS [Bytes], COUNT(*) AS [Rows]
                FROM [PrintingConsumables]
                WHERE NOT (
            """ + Recognized + """
            )
                GROUP BY CONVERT(varbinary(128), [Category])
            ) AS [Unrecognized];
            IF @unrecognized IS NOT NULL
            BEGIN
                DECLARE @message nvarchar(2048) = LEFT(N'PrintingConsumables.Category has values that are not exactly Cartridge, Paper or Laminating. Nothing was changed. Correct them and run the migration again: ' + @unrecognized, 2048);
                THROW 51601, @message, 1;
            END;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(ValidateCategories);

            migrationBuilder.CreateTable(
                name: "ConsumableTypes",
                columns: table => new
                {
                    ConsumableTypeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    NameKey = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, computedColumnSql: "UPPER([Name]) COLLATE Latin1_General_100_BIN2", stored: true),
                    StockMode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    AcknowledgeEarliestExpiration = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    UsesPrinterCompatibility = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsumableTypes", x => x.ConsumableTypeId);
                    table.CheckConstraint("CK_ConsumableTypes_Acknowledge", "[AcknowledgeEarliestExpiration] = 0 OR [StockMode] <> N'Undated'");
                    table.CheckConstraint("CK_ConsumableTypes_DisplayOrder", "[DisplayOrder] >= 0");
                    table.CheckConstraint("CK_ConsumableTypes_Name", "DATALENGTH([Name]) > 0 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name])))");
                    table.CheckConstraint("CK_ConsumableTypes_StockMode", "[StockMode] IN (N'Undated', N'DateRequired', N'DateOptional')");
                });

            // Section 5.3: the four initial types (owner decisions D2, D4 and D5).
            migrationBuilder.Sql("""
                INSERT INTO [ConsumableTypes] ([Name], [StockMode], [AcknowledgeEarliestExpiration], [UsesPrinterCompatibility], [DisplayOrder], [Active])
                VALUES (N'Cartridges', N'DateRequired', 1, 1, 1, 1),
                       (N'Paper', N'Undated', 0, 1, 2, 1),
                       (N'Laminating materials', N'DateOptional', 0, 1, 3, 1),
                       (N'Printer heads', N'DateRequired', 1, 1, 4, 1);
                """);

            migrationBuilder.AddColumn<int>(
                name: "ConsumableTypeId",
                table: "PrintingConsumables",
                type: "int",
                nullable: true);

            // Section 5.2a: each row is assigned only to the type whose literal it matches exactly.
            migrationBuilder.Sql("""
                UPDATE [PrintingConsumables]
                SET [ConsumableTypeId] = (SELECT [ConsumableTypeId] FROM [ConsumableTypes] WHERE [Name] = N'Cartridges')
                WHERE DATALENGTH([Category]) = DATALENGTH(N'Cartridge') AND [Category] COLLATE Latin1_General_100_BIN2 = N'Cartridge';
                UPDATE [PrintingConsumables]
                SET [ConsumableTypeId] = (SELECT [ConsumableTypeId] FROM [ConsumableTypes] WHERE [Name] = N'Paper')
                WHERE DATALENGTH([Category]) = DATALENGTH(N'Paper') AND [Category] COLLATE Latin1_General_100_BIN2 = N'Paper';
                UPDATE [PrintingConsumables]
                SET [ConsumableTypeId] = (SELECT [ConsumableTypeId] FROM [ConsumableTypes] WHERE [Name] = N'Laminating materials')
                WHERE DATALENGTH([Category]) = DATALENGTH(N'Laminating') AND [Category] COLLATE Latin1_General_100_BIN2 = N'Laminating';
                """);

            // Section 5.4: every consumable has a type, and the count per type equals the count per old value.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [PrintingConsumables] WHERE [ConsumableTypeId] IS NULL)
                   OR EXISTS (
                        SELECT 1
                        FROM (VALUES (N'Cartridge', N'Cartridges'), (N'Paper', N'Paper'), (N'Laminating', N'Laminating materials')) AS [Map]([Old], [New])
                        WHERE (SELECT COUNT(*) FROM [PrintingConsumables] AS [c]
                               WHERE DATALENGTH([c].[Category]) = DATALENGTH([Map].[Old]) AND [c].[Category] COLLATE Latin1_General_100_BIN2 = [Map].[Old])
                           <> (SELECT COUNT(*) FROM [PrintingConsumables] AS [c]
                               INNER JOIN [ConsumableTypes] AS [t] ON [t].[ConsumableTypeId] = [c].[ConsumableTypeId]
                               WHERE [t].[Name] = [Map].[New]))
                    THROW 51602, N'The consumable types were not assigned one for one. Nothing was changed.', 1;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "ConsumableTypeId",
                table: "PrintingConsumables",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrintingConsumables_ConsumableTypeId",
                table: "PrintingConsumables",
                column: "ConsumableTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsumableTypes_NameKey",
                table: "ConsumableTypes",
                column: "NameKey",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PrintingConsumables_ConsumableTypes_ConsumableTypeId",
                table: "PrintingConsumables",
                column: "ConsumableTypeId",
                principalTable: "ConsumableTypes",
                principalColumn: "ConsumableTypeId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.DropColumn(
                name: "Category",
                table: "PrintingConsumables");

            migrationBuilder.CreateTable(
                name: "PrintingStockMovements",
                columns: table => new
                {
                    PrintingStockMovementId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PrintingConsumableId = table.Column<int>(type: "int", nullable: false),
                    ExpirationDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MovementType = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    QuantityChange = table.Column<int>(type: "int", nullable: false),
                    LotQuantityAfter = table.Column<int>(type: "int", nullable: false),
                    OutOfOrder = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PerformedByObjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PerformedByAccountName = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrintingStockMovements", x => x.PrintingStockMovementId);
                    table.CheckConstraint("CK_PrintingStockMovements_LotQuantityAfter", "[LotQuantityAfter] >= 0");
                    table.CheckConstraint("CK_PrintingStockMovements_MovementType", "[MovementType] IN (N'Received', N'Used', N'Adjustment')");
                    table.CheckConstraint("CK_PrintingStockMovements_OutOfOrder", "[OutOfOrder] = 0 OR [MovementType] = N'Used'");
                    table.CheckConstraint("CK_PrintingStockMovements_QuantityChange", "[QuantityChange] <> 0 AND ([MovementType] <> N'Received' OR [QuantityChange] > 0) AND ([MovementType] <> N'Used' OR [QuantityChange] < 0)");
                    table.CheckConstraint("CK_PrintingStockMovements_Reason", "([MovementType] = N'Adjustment' AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0) OR ([MovementType] <> N'Adjustment' AND [Reason] IS NULL)");
                    table.ForeignKey(
                        name: "FK_PrintingStockMovements_PrintingConsumables_PrintingConsumableId",
                        column: x => x.PrintingConsumableId,
                        principalTable: "PrintingConsumables",
                        principalColumn: "PrintingConsumableId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PrintingStockMovements_OperationId",
                table: "PrintingStockMovements",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_PrintingStockMovements_PrintingConsumableId_OccurredAt",
                table: "PrintingStockMovements",
                columns: new[] { "PrintingConsumableId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback only: the register is dropped and each consumable gets back a fixed category from its stock mode
            // (DateRequired: Cartridge; Undated: Paper; DateOptional: Laminating). Types added later lose their name.
            migrationBuilder.DropTable(
                name: "PrintingStockMovements");

            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "PrintingConsumables",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE [c]
                SET [Category] = CASE [t].[StockMode] WHEN N'DateRequired' THEN N'Cartridge' WHEN N'Undated' THEN N'Paper' ELSE N'Laminating' END
                FROM [PrintingConsumables] AS [c]
                INNER JOIN [ConsumableTypes] AS [t] ON [t].[ConsumableTypeId] = [c].[ConsumableTypeId];
                """);

            migrationBuilder.AlterColumn<string>(
                name: "Category",
                table: "PrintingConsumables",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.DropForeignKey(
                name: "FK_PrintingConsumables_ConsumableTypes_ConsumableTypeId",
                table: "PrintingConsumables");

            migrationBuilder.DropIndex(
                name: "IX_PrintingConsumables_ConsumableTypeId",
                table: "PrintingConsumables");

            migrationBuilder.DropColumn(
                name: "ConsumableTypeId",
                table: "PrintingConsumables");

            migrationBuilder.DropTable(
                name: "ConsumableTypes");
        }
    }
}
