/*
    Poster clean installation for database POSTER.

    Connect to the existing SQL Server instance first.
    This script creates database POSTER, then installs the approved schema.
    It does not create a login, a password, a server role, or a connection string.
    If database POSTER already exists, it stops and does not change that database.
    Generated from dotnet ef migrations script, dotnet-ef 10.0.13.
    Includes the nine approved migrations, through PHASE 13 migration
    20261108191516_AddEntraObjectIdentifiers (Authentication Contract v1, section 3).
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF DB_ID(N'POSTER') IS NOT NULL
BEGIN
    RAISERROR(N'Database POSTER already exists. No changes were made.', 16, 1);
    SET NOEXEC ON;
END
GO

CREATE DATABASE [POSTER];
GO

USE [POSTER];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;

BEGIN TRANSACTION;
CREATE TABLE [Departments] (
    [DepartmentId] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_Departments] PRIMARY KEY ([DepartmentId])
);

CREATE TABLE [Reasons] (
    [ReasonId] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    [RequiresMentor] bit NOT NULL,
    [RequiresApprovalSheet] bit NOT NULL,
    CONSTRAINT [PK_Reasons] PRIMARY KEY ([ReasonId])
);

CREATE TABLE [PosterRequests] (
    [PosterRequestId] int NOT NULL IDENTITY,
    [PosterId] nvarchar(64) NOT NULL,
    [Name] nvarchar(max) NOT NULL,
    [Mentor] nvarchar(max) NULL,
    [DepartmentId] int NOT NULL,
    [Room] nvarchar(max) NOT NULL,
    [Phone] nvarchar(max) NOT NULL,
    [Email] nvarchar(max) NOT NULL,
    [ReasonId] int NULL,
    [LaminationRequested] bit NOT NULL,
    [ApprovalSheetUploaded] bit NOT NULL,
    [DateIn] datetime2 NOT NULL,
    CONSTRAINT [PK_PosterRequests] PRIMARY KEY ([PosterRequestId]),
    CONSTRAINT [FK_PosterRequests_Departments_DepartmentId] FOREIGN KEY ([DepartmentId]) REFERENCES [Departments] ([DepartmentId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PosterRequests_Reasons_ReasonId] FOREIGN KEY ([ReasonId]) REFERENCES [Reasons] ([ReasonId]) ON DELETE NO ACTION
);

CREATE TABLE [ApprovalSheets] (
    [ApprovalSheetId] int NOT NULL IDENTITY,
    [PosterRequestId] int NOT NULL,
    [FileName] nvarchar(max) NOT NULL,
    [StoragePath] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_ApprovalSheets] PRIMARY KEY ([ApprovalSheetId]),
    CONSTRAINT [FK_ApprovalSheets_PosterRequests_PosterRequestId] FOREIGN KEY ([PosterRequestId]) REFERENCES [PosterRequests] ([PosterRequestId]) ON DELETE CASCADE
);

CREATE TABLE [PosterFiles] (
    [PosterFileId] int NOT NULL IDENTITY,
    [PosterRequestId] int NOT NULL,
    [OriginalFileName] nvarchar(max) NOT NULL,
    [DetectedFormat] nvarchar(max) NOT NULL,
    [PageCount] int NOT NULL,
    [Width] decimal(18,4) NOT NULL,
    [Length] decimal(18,4) NOT NULL,
    [StoragePath] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_PosterFiles] PRIMARY KEY ([PosterFileId]),
    CONSTRAINT [FK_PosterFiles_PosterRequests_PosterRequestId] FOREIGN KEY ([PosterRequestId]) REFERENCES [PosterRequests] ([PosterRequestId]) ON DELETE CASCADE
);

CREATE TABLE [PosterProcessings] (
    [PosterProcessingId] int NOT NULL IDENTITY,
    [PosterRequestId] int NOT NULL,
    [ITPerson] nvarchar(max) NULL,
    [Received] date NULL,
    [Printed] bit NOT NULL,
    [Laminated] bit NOT NULL,
    [Notified] bit NOT NULL,
    [DateOut] date NULL,
    [PickedUpBy] nvarchar(max) NULL,
    [Comments] nvarchar(max) NULL,
    CONSTRAINT [PK_PosterProcessings] PRIMARY KEY ([PosterProcessingId]),
    CONSTRAINT [FK_PosterProcessings_PosterRequests_PosterRequestId] FOREIGN KEY ([PosterRequestId]) REFERENCES [PosterRequests] ([PosterRequestId]) ON DELETE CASCADE
);

CREATE UNIQUE INDEX [IX_ApprovalSheets_PosterRequestId] ON [ApprovalSheets] ([PosterRequestId]);

CREATE UNIQUE INDEX [IX_PosterFiles_PosterRequestId] ON [PosterFiles] ([PosterRequestId]);

CREATE UNIQUE INDEX [IX_PosterProcessings_PosterRequestId] ON [PosterProcessings] ([PosterRequestId]);

CREATE INDEX [IX_PosterRequests_DepartmentId] ON [PosterRequests] ([DepartmentId]);

CREATE UNIQUE INDEX [IX_PosterRequests_PosterId] ON [PosterRequests] ([PosterId]);

CREATE INDEX [IX_PosterRequests_ReasonId] ON [PosterRequests] ([ReasonId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261006214544_InitialSchema', N'10.0.13');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE [PosterRequests] ADD [SubmittedByUserName] nvarchar(256) NULL;

CREATE TABLE [AspNetRoles] (
    [Id] nvarchar(450) NOT NULL,
    [Name] nvarchar(256) NULL,
    [NormalizedName] nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetUsers] (
    [Id] nvarchar(450) NOT NULL,
    [UserName] nvarchar(256) NULL,
    [NormalizedUserName] nvarchar(256) NULL,
    [Email] nvarchar(256) NULL,
    [NormalizedEmail] nvarchar(256) NULL,
    [EmailConfirmed] bit NOT NULL,
    [PasswordHash] nvarchar(max) NULL,
    [SecurityStamp] nvarchar(max) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    [PhoneNumber] nvarchar(max) NULL,
    [PhoneNumberConfirmed] bit NOT NULL,
    [TwoFactorEnabled] bit NOT NULL,
    [LockoutEnd] datetimeoffset NULL,
    [LockoutEnabled] bit NOT NULL,
    [AccessFailedCount] int NOT NULL,
    CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
);

CREATE TABLE [AspNetRoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserLogins] (
    [LoginProvider] nvarchar(450) NOT NULL,
    [ProviderKey] nvarchar(450) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserRoles] (
    [UserId] nvarchar(450) NOT NULL,
    [RoleId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [AspNetUserTokens] (
    [UserId] nvarchar(450) NOT NULL,
    [LoginProvider] nvarchar(450) NOT NULL,
    [Name] nvarchar(450) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);

CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);

CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;

CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);

CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);

CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);

CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);

CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261007010716_AddSignInAccounts', N'10.0.13');

COMMIT;

BEGIN TRANSACTION;
CREATE TABLE [PrintingConsumables] (
    [PrintingConsumableId] int NOT NULL IDENTITY,
    [Category] nvarchar(32) NOT NULL,
    [Name] nvarchar(128) NOT NULL,
    [Code] nvarchar(32) NULL,
    [Capacity] nvarchar(80) NULL,
    [HasExpirationDate] bit NOT NULL,
    [CurrentQuantity] int NOT NULL,
    [LowStockThreshold] int NOT NULL,
    [CriticalStockThreshold] int NOT NULL,
    [Status] nvarchar(32) NOT NULL,
    [ExpirationDate] date NULL,
    [Active] bit NOT NULL,
    CONSTRAINT [PK_PrintingConsumables] PRIMARY KEY ([PrintingConsumableId])
);

CREATE TABLE [PrintingInventorySettings] (
    [PrintingInventorySettingId] int NOT NULL IDENTITY,
    [ExpirationWarningDays] int NULL,
    CONSTRAINT [PK_PrintingInventorySettings] PRIMARY KEY ([PrintingInventorySettingId])
);

CREATE UNIQUE INDEX [IX_PrintingConsumables_Code] ON [PrintingConsumables] ([Code]) WHERE [Code] IS NOT NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261007213212_AddPrintingConsumables', N'10.0.13');

COMMIT;

BEGIN TRANSACTION;
DROP TABLE [PrintingInventorySettings];

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261008000705_RemovePrintingInventorySetting', N'10.0.13');

COMMIT;

BEGIN TRANSACTION;
DECLARE @var nvarchar(max);
SELECT @var = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PrintingConsumables]') AND [c].[name] = N'CurrentQuantity');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [PrintingConsumables] DROP CONSTRAINT ' + @var + ';');
ALTER TABLE [PrintingConsumables] DROP COLUMN [CurrentQuantity];

DECLARE @var1 nvarchar(max);
SELECT @var1 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PrintingConsumables]') AND [c].[name] = N'ExpirationDate');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [PrintingConsumables] DROP CONSTRAINT ' + @var1 + ';');
ALTER TABLE [PrintingConsumables] DROP COLUMN [ExpirationDate];

CREATE TABLE [PrintingStockEntries] (
    [PrintingStockEntryId] int NOT NULL IDENTITY,
    [PrintingConsumableId] int NOT NULL,
    [Quantity] int NOT NULL,
    [ExpirationDate] date NULL,
    CONSTRAINT [PK_PrintingStockEntries] PRIMARY KEY ([PrintingStockEntryId]),
    CONSTRAINT [FK_PrintingStockEntries_PrintingConsumables_PrintingConsumableId] FOREIGN KEY ([PrintingConsumableId]) REFERENCES [PrintingConsumables] ([PrintingConsumableId]) ON DELETE CASCADE
);

CREATE INDEX [IX_PrintingStockEntries_PrintingConsumableId] ON [PrintingStockEntries] ([PrintingConsumableId]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261008002328_AddPrintingStockEntries', N'10.0.13');

COMMIT;

BEGIN TRANSACTION;
DECLARE @var2 nvarchar(max);
SELECT @var2 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Reasons]') AND [c].[name] = N'Name');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Reasons] DROP CONSTRAINT ' + @var2 + ';');
ALTER TABLE [Reasons] ALTER COLUMN [Name] nvarchar(128) NOT NULL;

ALTER TABLE [Reasons] ADD [Active] bit NOT NULL DEFAULT CAST(1 AS bit);

DECLARE @var3 nvarchar(max);
SELECT @var3 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Departments]') AND [c].[name] = N'Name');
IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [Departments] DROP CONSTRAINT ' + @var3 + ';');
ALTER TABLE [Departments] ALTER COLUMN [Name] nvarchar(128) NOT NULL;

ALTER TABLE [Departments] ADD [Active] bit NOT NULL DEFAULT CAST(1 AS bit);

CREATE TABLE [PrinterModels] (
    [PrinterModelId] int NOT NULL IDENTITY,
    [Name] nvarchar(128) NOT NULL,
    [Active] bit NOT NULL DEFAULT CAST(1 AS bit),
    CONSTRAINT [PK_PrinterModels] PRIMARY KEY ([PrinterModelId])
);

CREATE TABLE [PrinterModelConsumables] (
    [PrinterModelConsumableId] int NOT NULL IDENTITY,
    [PrinterModelId] int NOT NULL,
    [PrintingConsumableId] int NOT NULL,
    [Active] bit NOT NULL DEFAULT CAST(1 AS bit),
    CONSTRAINT [PK_PrinterModelConsumables] PRIMARY KEY ([PrinterModelConsumableId]),
    CONSTRAINT [FK_PrinterModelConsumables_PrinterModels_PrinterModelId] FOREIGN KEY ([PrinterModelId]) REFERENCES [PrinterModels] ([PrinterModelId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PrinterModelConsumables_PrintingConsumables_PrintingConsumableId] FOREIGN KEY ([PrintingConsumableId]) REFERENCES [PrintingConsumables] ([PrintingConsumableId]) ON DELETE NO ACTION
);

CREATE UNIQUE INDEX [IX_Reasons_Name] ON [Reasons] ([Name]);

CREATE UNIQUE INDEX [IX_Departments_Name] ON [Departments] ([Name]);

CREATE UNIQUE INDEX [IX_PrinterModelConsumables_PrinterModelId_PrintingConsumableId] ON [PrinterModelConsumables] ([PrinterModelId], [PrintingConsumableId]);

CREATE INDEX [IX_PrinterModelConsumables_PrintingConsumableId] ON [PrinterModelConsumables] ([PrintingConsumableId]);

CREATE UNIQUE INDEX [IX_PrinterModels_Name] ON [PrinterModels] ([Name]);

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261008022355_AddOperatorConfiguration', N'10.0.13');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE [PrintingStockEntries] DROP CONSTRAINT [FK_PrintingStockEntries_PrintingConsumables_PrintingConsumableId];

ALTER TABLE [PosterRequests] ADD [DepartmentName] nvarchar(128) NOT NULL DEFAULT N'';

ALTER TABLE [PosterRequests] ADD [ReasonName] nvarchar(128) NULL;

UPDATE request
SET DepartmentName = department.Name
FROM PosterRequests AS request
INNER JOIN Departments AS department ON department.DepartmentId = request.DepartmentId;

UPDATE request
SET ReasonName = reason.Name
FROM PosterRequests AS request
INNER JOIN Reasons AS reason ON reason.ReasonId = request.ReasonId;

DECLARE @var4 nvarchar(max);
SELECT @var4 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PosterRequests]') AND [c].[name] = N'DepartmentName');
IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [PosterRequests] DROP CONSTRAINT ' + @var4 + ';');

ALTER TABLE [PrintingStockEntries] ADD CONSTRAINT [FK_PrintingStockEntries_PrintingConsumables_PrintingConsumableId] FOREIGN KEY ([PrintingConsumableId]) REFERENCES [PrintingConsumables] ([PrintingConsumableId]) ON DELETE NO ACTION;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261008032642_ProtectConfigurationHistory', N'10.0.13');

COMMIT;

BEGIN TRANSACTION;
DROP TABLE [AspNetRoleClaims];

DROP TABLE [AspNetUserClaims];

DROP TABLE [AspNetUserLogins];

DROP TABLE [AspNetUserRoles];

DROP TABLE [AspNetUserTokens];

DROP TABLE [AspNetRoles];

DROP TABLE [AspNetUsers];

EXEC sp_rename N'[PosterProcessings].[ITPerson]', N'ProcessedByAccountName', 'COLUMN';

DECLARE @var5 nvarchar(max);
SELECT @var5 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PosterProcessings]') AND [c].[name] = N'ProcessedByAccountName');
IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [PosterProcessings] DROP CONSTRAINT ' + @var5 + ';');
ALTER TABLE [PosterProcessings] ALTER COLUMN [ProcessedByAccountName] nvarchar(256) NULL;

EXEC sp_rename N'[PosterRequests].[SubmittedByUserName]', N'SubmittedByAccountName', 'COLUMN';

ALTER TABLE [PosterRequests] ADD [SubmittedBySecurityIdentifier] nvarchar(184) NULL;

ALTER TABLE [PosterProcessings] ADD [ProcessedBySecurityIdentifier] nvarchar(184) NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261023184022_RemoveApplicationManagedAuthentication', N'10.0.13');

COMMIT;

BEGIN TRANSACTION;
ALTER TABLE [PosterRequests] ADD [SubmittedByObjectId] uniqueidentifier NULL;

ALTER TABLE [PosterProcessings] ADD [ProcessedByObjectId] uniqueidentifier NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261108191516_AddEntraObjectIdentifiers', N'10.0.13');

COMMIT;

IF DB_NAME() <> N'POSTER'
    THROW 51001, N'Installation is not connected to database POSTER. The schema transaction was not committed.', 1;

IF (SELECT COUNT(*) FROM [__EFMigrationsHistory]) <> 9
    THROW 51002, N'POSTER migration history does not contain the nine approved migrations. The schema transaction was not committed.', 1;

IF EXISTS (
    SELECT 1
    FROM (VALUES
        (N'20261006214544_InitialSchema'),
        (N'20261007010716_AddSignInAccounts'),
        (N'20261007213212_AddPrintingConsumables'),
        (N'20261008000705_RemovePrintingInventorySetting'),
        (N'20261008002328_AddPrintingStockEntries'),
        (N'20261008022355_AddOperatorConfiguration'),
        (N'20261008032642_ProtectConfigurationHistory'),
        (N'20261023184022_RemoveApplicationManagedAuthentication'),
        (N'20261108191516_AddEntraObjectIdentifiers')) AS required([MigrationId])
    WHERE NOT EXISTS (
        SELECT 1
        FROM [__EFMigrationsHistory] AS history
        WHERE history.[MigrationId] = required.[MigrationId]
          AND history.[ProductVersion] = N'10.0.13'))
    THROW 51003, N'A required POSTER migration is missing or has an unexpected product version. The schema transaction was not committed.', 1;

IF EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [ProductVersion] <> N'10.0.13')
    THROW 51004, N'A POSTER migration history row has an unexpected product version. The schema transaction was not committed.', 1;

IF (SELECT COUNT(*) FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo')) <> 11
    THROW 51005, N'The finished POSTER schema does not contain the approved set of tables. The schema transaction was not committed.', 1;

IF EXISTS (
    SELECT 1
    FROM sys.tables
    WHERE schema_id = SCHEMA_ID(N'dbo')
      AND [name] LIKE N'AspNet%')
    THROW 51006, N'Application-managed authentication tables remain in POSTER. The schema transaction was not committed.', 1;

IF EXISTS (SELECT 1 FROM sys.columns WHERE [name] = N'PasswordHash')
    THROW 51007, N'A password hash column remains in POSTER. The schema transaction was not committed.', 1;

IF OBJECT_ID(N'dbo.Departments', N'U') IS NULL
    OR OBJECT_ID(N'dbo.Reasons', N'U') IS NULL
    OR OBJECT_ID(N'dbo.PosterRequests', N'U') IS NULL
    OR OBJECT_ID(N'dbo.PosterFiles', N'U') IS NULL
    OR OBJECT_ID(N'dbo.ApprovalSheets', N'U') IS NULL
    OR OBJECT_ID(N'dbo.PosterProcessings', N'U') IS NULL
    OR OBJECT_ID(N'dbo.PrintingConsumables', N'U') IS NULL
    OR OBJECT_ID(N'dbo.PrintingStockEntries', N'U') IS NULL
    OR OBJECT_ID(N'dbo.PrinterModels', N'U') IS NULL
    OR OBJECT_ID(N'dbo.PrinterModelConsumables', N'U') IS NULL
    OR OBJECT_ID(N'dbo.__EFMigrationsHistory', N'U') IS NULL
    OR OBJECT_ID(N'dbo.PrintingInventorySettings', N'U') IS NOT NULL
    THROW 51008, N'The finished POSTER tables do not match the approved model. The schema transaction was not committed.', 1;

IF EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE ([object_id] = OBJECT_ID(N'dbo.PosterRequests') AND [name] = N'SubmittedByUserName')
       OR ([object_id] = OBJECT_ID(N'dbo.PosterProcessings') AND [name] = N'ITPerson'))
    THROW 51009, N'An obsolete account column remains in POSTER. The schema transaction was not committed.', 1;

IF EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE NOT (
        (TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'PosterRequests' AND COLUMN_NAME = N'SubmittedByAccountName' AND DATA_TYPE = N'nvarchar' AND CHARACTER_MAXIMUM_LENGTH = 256 AND IS_NULLABLE = N'YES')
        OR (TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'PosterRequests' AND COLUMN_NAME = N'SubmittedBySecurityIdentifier' AND DATA_TYPE = N'nvarchar' AND CHARACTER_MAXIMUM_LENGTH = 184 AND IS_NULLABLE = N'YES')
        OR (TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'PosterProcessings' AND COLUMN_NAME = N'ProcessedByAccountName' AND DATA_TYPE = N'nvarchar' AND CHARACTER_MAXIMUM_LENGTH = 256 AND IS_NULLABLE = N'YES')
        OR (TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'PosterProcessings' AND COLUMN_NAME = N'ProcessedBySecurityIdentifier' AND DATA_TYPE = N'nvarchar' AND CHARACTER_MAXIMUM_LENGTH = 184 AND IS_NULLABLE = N'YES')
        OR (TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'PosterRequests' AND COLUMN_NAME = N'SubmittedByObjectId' AND DATA_TYPE = N'uniqueidentifier' AND IS_NULLABLE = N'YES')
        OR (TABLE_SCHEMA = N'dbo' AND TABLE_NAME = N'PosterProcessings' AND COLUMN_NAME = N'ProcessedByObjectId' AND DATA_TYPE = N'uniqueidentifier' AND IS_NULLABLE = N'YES'))
      AND (
        (TABLE_NAME = N'PosterRequests' AND COLUMN_NAME IN (N'SubmittedByAccountName', N'SubmittedBySecurityIdentifier', N'SubmittedByObjectId'))
        OR (TABLE_NAME = N'PosterProcessings' AND COLUMN_NAME IN (N'ProcessedByAccountName', N'ProcessedBySecurityIdentifier', N'ProcessedByObjectId'))))
    THROW 51010, N'An institutional identity column has the wrong type or length. The schema transaction was not committed.', 1;

IF (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = N'dbo'
      AND (
        (TABLE_NAME = N'PosterRequests' AND COLUMN_NAME IN (N'SubmittedByAccountName', N'SubmittedBySecurityIdentifier', N'SubmittedByObjectId'))
        OR (TABLE_NAME = N'PosterProcessings' AND COLUMN_NAME IN (N'ProcessedByAccountName', N'ProcessedBySecurityIdentifier', N'ProcessedByObjectId')))) <> 6
    THROW 51011, N'An institutional identity column is missing from POSTER. The schema transaction was not committed.', 1;

COMMIT TRANSACTION;
PRINT N'Database POSTER installed. Schema transaction committed.';
GO

SET NOEXEC OFF;
GO