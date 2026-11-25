/*
    PosterPrintRequest - PHASE 16 database update: configurable consumable types, Printer heads, stock-movement register.
    Authority: docs/Consumables_Inventory_Contract_v1.docx (sections 4, 5 and 7).

    Run in SSMS against the application's database (select it first, or uncomment USE). Run the whole script.
    - Keeps every consumable, lot, threshold, status and printer link. Adds the tables ConsumableTypes and
      PrintingStockMovements, and replaces PrintingConsumables.Category with ConsumableTypeId.
    - Stops without changing anything if a Category value is not exactly Cartridge, Paper or Laminating (contract 5.1, 5.2);
      the error lists each value with its byte length and bytes.
    - One transaction. Every statement runs in its own batch; after each one a check stops the rest of the script if
      the transaction was rolled back.
    - Requires PHASE 15 (migration AddTdxIntegrationAndReplacements) and runs only once.
*/
-- USE [<database-name>];
-- GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
   OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261117003728_AddTdxIntegrationAndReplacements')
BEGIN
    RAISERROR(N'This database is not at PHASE 15 (AddTdxIntegrationAndReplacements). No changes were made.', 16, 1);
    SET NOEXEC ON;
END
ELSE IF EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261119215655_AddConfigurableConsumableTypes')
BEGIN
    RAISERROR(N'PHASE 16 is already applied. No changes were made.', 10, 1);
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
DECLARE @unrecognized nvarchar(max);
SELECT @unrecognized = STRING_AGG(CONVERT(nvarchar(max), CONCAT(
        N'[', ISNULL([Value], N'<NULL>'), N'] bytes=', ISNULL(CONVERT(nvarchar(20), DATALENGTH([Value])), N'NULL'),
        N' hex=', ISNULL(CONVERT(nvarchar(300), [Bytes], 1), N'NULL'), N' rows=', [Rows])), N'; ')
FROM (
    SELECT MIN([Category]) AS [Value], CONVERT(varbinary(128), [Category]) AS [Bytes], COUNT(*) AS [Rows]
    FROM [PrintingConsumables]
    WHERE NOT ([Category] IS NOT NULL AND ((DATALENGTH([Category]) = DATALENGTH(N'Cartridge') AND [Category] COLLATE Latin1_General_100_BIN2 = N'Cartridge') OR (DATALENGTH([Category]) = DATALENGTH(N'Paper') AND [Category] COLLATE Latin1_General_100_BIN2 = N'Paper') OR (DATALENGTH([Category]) = DATALENGTH(N'Laminating') AND [Category] COLLATE Latin1_General_100_BIN2 = N'Laminating')))
    GROUP BY CONVERT(varbinary(128), [Category])
) AS [Unrecognized];
IF @unrecognized IS NOT NULL
BEGIN
    DECLARE @message nvarchar(2048) = LEFT(N'PrintingConsumables.Category has values that are not exactly Cartridge, Paper or Laminating. Nothing was changed. Correct them and run the migration again: ' + @unrecognized, 2048);
    THROW 51601, @message, 1;
END;
GO

IF @@TRANCOUNT = 0
BEGIN
    RAISERROR(N'The update stopped and was rolled back. Nothing was changed. Read the error above.', 16, 1);
    SET NOEXEC ON;
END
GO

CREATE TABLE [ConsumableTypes] (
    [ConsumableTypeId] int NOT NULL IDENTITY,
    [Name] nvarchar(64) NOT NULL,
    [NameKey] AS UPPER([Name]) COLLATE Latin1_General_100_BIN2 PERSISTED,
    [StockMode] nvarchar(16) NOT NULL,
    [AcknowledgeEarliestExpiration] bit NOT NULL DEFAULT CAST(0 AS bit),
    [UsesPrinterCompatibility] bit NOT NULL DEFAULT CAST(1 AS bit),
    [DisplayOrder] int NOT NULL,
    [Active] bit NOT NULL DEFAULT CAST(1 AS bit),
    CONSTRAINT [PK_ConsumableTypes] PRIMARY KEY ([ConsumableTypeId]),
    CONSTRAINT [CK_ConsumableTypes_Acknowledge] CHECK ([AcknowledgeEarliestExpiration] = 0 OR [StockMode] <> N'Undated'),
    CONSTRAINT [CK_ConsumableTypes_DisplayOrder] CHECK ([DisplayOrder] >= 0),
    CONSTRAINT [CK_ConsumableTypes_Name] CHECK (DATALENGTH([Name]) > 0 AND DATALENGTH([Name]) = DATALENGTH(LTRIM(RTRIM([Name])))),
    CONSTRAINT [CK_ConsumableTypes_StockMode] CHECK ([StockMode] IN (N'Undated', N'DateRequired', N'DateOptional'))
);
GO

IF @@TRANCOUNT = 0
BEGIN
    RAISERROR(N'The update stopped and was rolled back. Nothing was changed. Read the error above.', 16, 1);
    SET NOEXEC ON;
END
GO

INSERT INTO [ConsumableTypes] ([Name], [StockMode], [AcknowledgeEarliestExpiration], [UsesPrinterCompatibility], [DisplayOrder], [Active])
VALUES (N'Cartridges', N'DateRequired', 1, 1, 1, 1),
       (N'Paper', N'Undated', 0, 1, 2, 1),
       (N'Laminating materials', N'DateOptional', 0, 1, 3, 1),
       (N'Printer heads', N'DateRequired', 1, 1, 4, 1);
GO

IF @@TRANCOUNT = 0
BEGIN
    RAISERROR(N'The update stopped and was rolled back. Nothing was changed. Read the error above.', 16, 1);
    SET NOEXEC ON;
END
GO

ALTER TABLE [PrintingConsumables] ADD [ConsumableTypeId] int NULL;
GO

IF @@TRANCOUNT = 0
BEGIN
    RAISERROR(N'The update stopped and was rolled back. Nothing was changed. Read the error above.', 16, 1);
    SET NOEXEC ON;
END
GO

UPDATE [PrintingConsumables]
SET [ConsumableTypeId] = (SELECT [ConsumableTypeId] FROM [ConsumableTypes] WHERE [Name] = N'Cartridges')
WHERE DATALENGTH([Category]) = DATALENGTH(N'Cartridge') AND [Category] COLLATE Latin1_General_100_BIN2 = N'Cartridge';
UPDATE [PrintingConsumables]
SET [ConsumableTypeId] = (SELECT [ConsumableTypeId] FROM [ConsumableTypes] WHERE [Name] = N'Paper')
WHERE DATALENGTH([Category]) = DATALENGTH(N'Paper') AND [Category] COLLATE Latin1_General_100_BIN2 = N'Paper';
UPDATE [PrintingConsumables]
SET [ConsumableTypeId] = (SELECT [ConsumableTypeId] FROM [ConsumableTypes] WHERE [Name] = N'Laminating materials')
WHERE DATALENGTH([Category]) = DATALENGTH(N'Laminating') AND [Category] COLLATE Latin1_General_100_BIN2 = N'Laminating';
GO

IF @@TRANCOUNT = 0
BEGIN
    RAISERROR(N'The update stopped and was rolled back. Nothing was changed. Read the error above.', 16, 1);
    SET NOEXEC ON;
END
GO

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
GO

IF @@TRANCOUNT = 0
BEGIN
    RAISERROR(N'The update stopped and was rolled back. Nothing was changed. Read the error above.', 16, 1);
    SET NOEXEC ON;
END
GO

DECLARE @var nvarchar(max);
SELECT @var = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PrintingConsumables]') AND [c].[name] = N'ConsumableTypeId');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [PrintingConsumables] DROP CONSTRAINT ' + @var + ';');
ALTER TABLE [PrintingConsumables] ALTER COLUMN [ConsumableTypeId] int NOT NULL;
GO

IF @@TRANCOUNT = 0
BEGIN
    RAISERROR(N'The update stopped and was rolled back. Nothing was changed. Read the error above.', 16, 1);
    SET NOEXEC ON;
END
GO

CREATE INDEX [IX_PrintingConsumables_ConsumableTypeId] ON [PrintingConsumables] ([ConsumableTypeId]);
GO

IF @@TRANCOUNT = 0
BEGIN
    RAISERROR(N'The update stopped and was rolled back. Nothing was changed. Read the error above.', 16, 1);
    SET NOEXEC ON;
END
GO

CREATE UNIQUE INDEX [IX_ConsumableTypes_NameKey] ON [ConsumableTypes] ([NameKey]);
GO

IF @@TRANCOUNT = 0
BEGIN
    RAISERROR(N'The update stopped and was rolled back. Nothing was changed. Read the error above.', 16, 1);
    SET NOEXEC ON;
END
GO

ALTER TABLE [PrintingConsumables] ADD CONSTRAINT [FK_PrintingConsumables_ConsumableTypes_ConsumableTypeId] FOREIGN KEY ([ConsumableTypeId]) REFERENCES [ConsumableTypes] ([ConsumableTypeId]) ON DELETE NO ACTION;
GO

IF @@TRANCOUNT = 0
BEGIN
    RAISERROR(N'The update stopped and was rolled back. Nothing was changed. Read the error above.', 16, 1);
    SET NOEXEC ON;
END
GO

DECLARE @var1 nvarchar(max);
SELECT @var1 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PrintingConsumables]') AND [c].[name] = N'Category');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [PrintingConsumables] DROP CONSTRAINT ' + @var1 + ';');
ALTER TABLE [PrintingConsumables] DROP COLUMN [Category];
GO

IF @@TRANCOUNT = 0
BEGIN
    RAISERROR(N'The update stopped and was rolled back. Nothing was changed. Read the error above.', 16, 1);
    SET NOEXEC ON;
END
GO

CREATE TABLE [PrintingStockMovements] (
    [PrintingStockMovementId] bigint NOT NULL IDENTITY,
    [OperationId] uniqueidentifier NOT NULL,
    [PrintingConsumableId] int NOT NULL,
    [ExpirationDate] date NULL,
    [MovementType] nvarchar(16) NOT NULL,
    [QuantityChange] int NOT NULL,
    [LotQuantityAfter] int NOT NULL,
    [OutOfOrder] bit NOT NULL DEFAULT CAST(0 AS bit),
    [Reason] nvarchar(500) NULL,
    [OccurredAt] datetime2 NOT NULL,
    [PerformedByObjectId] uniqueidentifier NOT NULL,
    [PerformedByAccountName] nvarchar(256) NULL,
    CONSTRAINT [PK_PrintingStockMovements] PRIMARY KEY ([PrintingStockMovementId]),
    CONSTRAINT [CK_PrintingStockMovements_LotQuantityAfter] CHECK ([LotQuantityAfter] >= 0),
    CONSTRAINT [CK_PrintingStockMovements_MovementType] CHECK ([MovementType] IN (N'Received', N'Used', N'Adjustment')),
    CONSTRAINT [CK_PrintingStockMovements_OutOfOrder] CHECK ([OutOfOrder] = 0 OR [MovementType] = N'Used'),
    CONSTRAINT [CK_PrintingStockMovements_QuantityChange] CHECK ([QuantityChange] <> 0 AND ([MovementType] <> N'Received' OR [QuantityChange] > 0) AND ([MovementType] <> N'Used' OR [QuantityChange] < 0)),
    CONSTRAINT [CK_PrintingStockMovements_Reason] CHECK (([MovementType] = N'Adjustment' AND [Reason] IS NOT NULL AND LEN(LTRIM(RTRIM([Reason]))) > 0) OR ([MovementType] <> N'Adjustment' AND [Reason] IS NULL)),
    CONSTRAINT [FK_PrintingStockMovements_PrintingConsumables_PrintingConsumableId] FOREIGN KEY ([PrintingConsumableId]) REFERENCES [PrintingConsumables] ([PrintingConsumableId]) ON DELETE NO ACTION
);
GO

IF @@TRANCOUNT = 0
BEGIN
    RAISERROR(N'The update stopped and was rolled back. Nothing was changed. Read the error above.', 16, 1);
    SET NOEXEC ON;
END
GO

CREATE INDEX [IX_PrintingStockMovements_OperationId] ON [PrintingStockMovements] ([OperationId]);
GO

IF @@TRANCOUNT = 0
BEGIN
    RAISERROR(N'The update stopped and was rolled back. Nothing was changed. Read the error above.', 16, 1);
    SET NOEXEC ON;
END
GO

CREATE INDEX [IX_PrintingStockMovements_PrintingConsumableId_OccurredAt] ON [PrintingStockMovements] ([PrintingConsumableId], [OccurredAt]);
GO

IF @@TRANCOUNT = 0
BEGIN
    RAISERROR(N'The update stopped and was rolled back. Nothing was changed. Read the error above.', 16, 1);
    SET NOEXEC ON;
END
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261119215655_AddConfigurableConsumableTypes', N'10.0.13');
GO

IF @@TRANCOUNT = 0
BEGIN
    RAISERROR(N'The update stopped and was rolled back. Nothing was changed. Read the error above.', 16, 1);
    SET NOEXEC ON;
END
GO

COMMIT;
GO

SET NOEXEC OFF;
GO

-- Check
SELECT TOP (3) [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId] DESC;
SELECT [ConsumableTypeId], [Name], [StockMode], [AcknowledgeEarliestExpiration], [UsesPrinterCompatibility], [DisplayOrder], [Active] FROM [ConsumableTypes] ORDER BY [DisplayOrder];
SELECT [t].[Name] AS [Type], COUNT([c].[PrintingConsumableId]) AS [Consumables]
FROM [ConsumableTypes] AS [t] LEFT JOIN [PrintingConsumables] AS [c] ON [c].[ConsumableTypeId] = [t].[ConsumableTypeId]
GROUP BY [t].[Name], [t].[DisplayOrder] ORDER BY [t].[DisplayOrder];
