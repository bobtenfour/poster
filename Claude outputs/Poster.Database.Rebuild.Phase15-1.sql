/*
    PosterPrintRequest - rebuilds the application tables up to PHASE 15, inside the existing database.

    The database itself is NOT dropped: its users, roles, permissions granted at database or schema level,
    and every other setting stay as they are. Only the application's own tables (listed below) are dropped,
    with their data, then created again from all migrations.
    Permissions granted on individual tables (GRANT ... ON dbo.<table>) are lost with the table;
    the script lists them first so they can be granted again.

    Select the application's database in SSMS (or uncomment USE) and run the whole script.
*/
-- USE [<database-name>];
-- GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF DB_NAME() IN (N'master', N'model', N'msdb', N'tempdb')
BEGIN
    RAISERROR(N'Select the application database first. No changes were made.', 16, 1);
    SET NOEXEC ON;
END
GO

-- 0. Table-level permissions that will be lost (grant them again afterwards if any are listed).
SELECT pr.name AS [Principal], pe.permission_name AS [Permission], OBJECT_NAME(pe.major_id) AS [Table], pe.state_desc AS [State]
FROM sys.database_permissions pe
JOIN sys.database_principals pr ON pr.principal_id = pe.grantee_principal_id
WHERE pe.class = 1 AND OBJECT_SCHEMA_NAME(pe.major_id) = N'dbo'
  AND OBJECT_NAME(pe.major_id) IN (SELECT t FROM (VALUES
        (N'PosterReplacements'),
        (N'PosterRetentionAudits'),
        (N'PrinterModelConsumables'),
        (N'PrinterModels'),
        (N'PrintingStockEntries'),
        (N'PrintingInventorySettings'),
        (N'PrintingConsumables'),
        (N'AspNetUserTokens'),
        (N'AspNetUserRoles'),
        (N'AspNetUserLogins'),
        (N'AspNetUserClaims'),
        (N'AspNetRoleClaims'),
        (N'AspNetUsers'),
        (N'AspNetRoles'),
        (N'PosterProcessings'),
        (N'PosterFiles'),
        (N'ApprovalSheets'),
        (N'PosterRequests'),
        (N'Reasons'),
        (N'Departments'),
        (N'__EFMigrationsHistory')) AS v(t));
GO

-- 1. Drop the application tables only: first every foreign key that points to or from them, then the tables.
BEGIN TRANSACTION;
DECLARE @tables TABLE (t sysname PRIMARY KEY);
INSERT INTO @tables (t) VALUES
        (N'PosterReplacements'),
        (N'PosterRetentionAudits'),
        (N'PrinterModelConsumables'),
        (N'PrinterModels'),
        (N'PrintingStockEntries'),
        (N'PrintingInventorySettings'),
        (N'PrintingConsumables'),
        (N'AspNetUserTokens'),
        (N'AspNetUserRoles'),
        (N'AspNetUserLogins'),
        (N'AspNetUserClaims'),
        (N'AspNetRoleClaims'),
        (N'AspNetUsers'),
        (N'AspNetRoles'),
        (N'PosterProcessings'),
        (N'PosterFiles'),
        (N'ApprovalSheets'),
        (N'PosterRequests'),
        (N'Reasons'),
        (N'Departments'),
        (N'__EFMigrationsHistory');

DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(fk.parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(fk.parent_object_id))
             + N' DROP CONSTRAINT ' + QUOTENAME(fk.name) + N';' + CHAR(10)
FROM sys.foreign_keys fk
WHERE (OBJECT_SCHEMA_NAME(fk.parent_object_id) = N'dbo' AND OBJECT_NAME(fk.parent_object_id) IN (SELECT t FROM @tables))
   OR (OBJECT_SCHEMA_NAME(fk.referenced_object_id) = N'dbo' AND OBJECT_NAME(fk.referenced_object_id) IN (SELECT t FROM @tables));
EXEC sys.sp_executesql @sql;

SET @sql = N'';
SELECT @sql += N'DROP TABLE [dbo].' + QUOTENAME(t) + N';' + CHAR(10)
FROM @tables
WHERE OBJECT_ID(N'[dbo].' + QUOTENAME(t), N'U') IS NOT NULL;
EXEC sys.sp_executesql @sql;
COMMIT;
GO

-- 2. Create every table from the migrations (InitialSchema ... AddTdxIntegrationAndReplacements).
--    Every statement runs in its own batch (GO): SQL Server compiles a whole batch before running it,
--    so a column added by ALTER TABLE cannot be used by a later statement of the same batch.
IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [Departments] (
    [DepartmentId] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_Departments] PRIMARY KEY ([DepartmentId])
);
GO

CREATE TABLE [Reasons] (
    [ReasonId] int NOT NULL IDENTITY,
    [Name] nvarchar(max) NOT NULL,
    [RequiresMentor] bit NOT NULL,
    [RequiresApprovalSheet] bit NOT NULL,
    CONSTRAINT [PK_Reasons] PRIMARY KEY ([ReasonId])
);
GO

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
GO

CREATE TABLE [ApprovalSheets] (
    [ApprovalSheetId] int NOT NULL IDENTITY,
    [PosterRequestId] int NOT NULL,
    [FileName] nvarchar(max) NOT NULL,
    [StoragePath] nvarchar(max) NOT NULL,
    CONSTRAINT [PK_ApprovalSheets] PRIMARY KEY ([ApprovalSheetId]),
    CONSTRAINT [FK_ApprovalSheets_PosterRequests_PosterRequestId] FOREIGN KEY ([PosterRequestId]) REFERENCES [PosterRequests] ([PosterRequestId]) ON DELETE CASCADE
);
GO

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
GO

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
GO

CREATE UNIQUE INDEX [IX_ApprovalSheets_PosterRequestId] ON [ApprovalSheets] ([PosterRequestId]);
GO

CREATE UNIQUE INDEX [IX_PosterFiles_PosterRequestId] ON [PosterFiles] ([PosterRequestId]);
GO

CREATE UNIQUE INDEX [IX_PosterProcessings_PosterRequestId] ON [PosterProcessings] ([PosterRequestId]);
GO

CREATE INDEX [IX_PosterRequests_DepartmentId] ON [PosterRequests] ([DepartmentId]);
GO

CREATE UNIQUE INDEX [IX_PosterRequests_PosterId] ON [PosterRequests] ([PosterId]);
GO

CREATE INDEX [IX_PosterRequests_ReasonId] ON [PosterRequests] ([ReasonId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261006214544_InitialSchema', N'10.0.13');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [PosterRequests] ADD [SubmittedByUserName] nvarchar(256) NULL;
GO

CREATE TABLE [AspNetRoles] (
    [Id] nvarchar(450) NOT NULL,
    [Name] nvarchar(256) NULL,
    [NormalizedName] nvarchar(256) NULL,
    [ConcurrencyStamp] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
);
GO

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
GO

CREATE TABLE [AspNetRoleClaims] (
    [Id] int NOT NULL IDENTITY,
    [RoleId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [AspNetUserClaims] (
    [Id] int NOT NULL IDENTITY,
    [UserId] nvarchar(450) NOT NULL,
    [ClaimType] nvarchar(max) NULL,
    [ClaimValue] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [AspNetUserLogins] (
    [LoginProvider] nvarchar(450) NOT NULL,
    [ProviderKey] nvarchar(450) NOT NULL,
    [ProviderDisplayName] nvarchar(max) NULL,
    [UserId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
    CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [AspNetUserRoles] (
    [UserId] nvarchar(450) NOT NULL,
    [RoleId] nvarchar(450) NOT NULL,
    CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
    CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [AspNetRoles] ([Id]) ON DELETE CASCADE,
    CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [AspNetUserTokens] (
    [UserId] nvarchar(450) NOT NULL,
    [LoginProvider] nvarchar(450) NOT NULL,
    [Name] nvarchar(450) NOT NULL,
    [Value] nvarchar(max) NULL,
    CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
    CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId] FOREIGN KEY ([UserId]) REFERENCES [AspNetUsers] ([Id]) ON DELETE CASCADE
);
GO

CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [AspNetRoleClaims] ([RoleId]);
GO

CREATE UNIQUE INDEX [RoleNameIndex] ON [AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;
GO

CREATE INDEX [IX_AspNetUserClaims_UserId] ON [AspNetUserClaims] ([UserId]);
GO

CREATE INDEX [IX_AspNetUserLogins_UserId] ON [AspNetUserLogins] ([UserId]);
GO

CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [AspNetUserRoles] ([RoleId]);
GO

CREATE INDEX [EmailIndex] ON [AspNetUsers] ([NormalizedEmail]);
GO

CREATE UNIQUE INDEX [UserNameIndex] ON [AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261007010716_AddSignInAccounts', N'10.0.13');

COMMIT;
GO

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
GO

CREATE TABLE [PrintingInventorySettings] (
    [PrintingInventorySettingId] int NOT NULL IDENTITY,
    [ExpirationWarningDays] int NULL,
    CONSTRAINT [PK_PrintingInventorySettings] PRIMARY KEY ([PrintingInventorySettingId])
);
GO

CREATE UNIQUE INDEX [IX_PrintingConsumables_Code] ON [PrintingConsumables] ([Code]) WHERE [Code] IS NOT NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261007213212_AddPrintingConsumables', N'10.0.13');

COMMIT;
GO

BEGIN TRANSACTION;
DROP TABLE [PrintingInventorySettings];
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261008000705_RemovePrintingInventorySetting', N'10.0.13');

COMMIT;
GO

BEGIN TRANSACTION;
DECLARE @var nvarchar(max);
SELECT @var = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PrintingConsumables]') AND [c].[name] = N'CurrentQuantity');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [PrintingConsumables] DROP CONSTRAINT ' + @var + ';');
ALTER TABLE [PrintingConsumables] DROP COLUMN [CurrentQuantity];
GO

DECLARE @var1 nvarchar(max);
SELECT @var1 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PrintingConsumables]') AND [c].[name] = N'ExpirationDate');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [PrintingConsumables] DROP CONSTRAINT ' + @var1 + ';');
ALTER TABLE [PrintingConsumables] DROP COLUMN [ExpirationDate];
GO

CREATE TABLE [PrintingStockEntries] (
    [PrintingStockEntryId] int NOT NULL IDENTITY,
    [PrintingConsumableId] int NOT NULL,
    [Quantity] int NOT NULL,
    [ExpirationDate] date NULL,
    CONSTRAINT [PK_PrintingStockEntries] PRIMARY KEY ([PrintingStockEntryId]),
    CONSTRAINT [FK_PrintingStockEntries_PrintingConsumables_PrintingConsumableId] FOREIGN KEY ([PrintingConsumableId]) REFERENCES [PrintingConsumables] ([PrintingConsumableId]) ON DELETE CASCADE
);
GO

CREATE INDEX [IX_PrintingStockEntries_PrintingConsumableId] ON [PrintingStockEntries] ([PrintingConsumableId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261008002328_AddPrintingStockEntries', N'10.0.13');

COMMIT;
GO

BEGIN TRANSACTION;
DECLARE @var2 nvarchar(max);
SELECT @var2 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Reasons]') AND [c].[name] = N'Name');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Reasons] DROP CONSTRAINT ' + @var2 + ';');
ALTER TABLE [Reasons] ALTER COLUMN [Name] nvarchar(128) NOT NULL;
GO

ALTER TABLE [Reasons] ADD [Active] bit NOT NULL DEFAULT CAST(1 AS bit);
GO

DECLARE @var3 nvarchar(max);
SELECT @var3 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Departments]') AND [c].[name] = N'Name');
IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [Departments] DROP CONSTRAINT ' + @var3 + ';');
ALTER TABLE [Departments] ALTER COLUMN [Name] nvarchar(128) NOT NULL;
GO

ALTER TABLE [Departments] ADD [Active] bit NOT NULL DEFAULT CAST(1 AS bit);
GO

CREATE TABLE [PrinterModels] (
    [PrinterModelId] int NOT NULL IDENTITY,
    [Name] nvarchar(128) NOT NULL,
    [Active] bit NOT NULL DEFAULT CAST(1 AS bit),
    CONSTRAINT [PK_PrinterModels] PRIMARY KEY ([PrinterModelId])
);
GO

CREATE TABLE [PrinterModelConsumables] (
    [PrinterModelConsumableId] int NOT NULL IDENTITY,
    [PrinterModelId] int NOT NULL,
    [PrintingConsumableId] int NOT NULL,
    [Active] bit NOT NULL DEFAULT CAST(1 AS bit),
    CONSTRAINT [PK_PrinterModelConsumables] PRIMARY KEY ([PrinterModelConsumableId]),
    CONSTRAINT [FK_PrinterModelConsumables_PrinterModels_PrinterModelId] FOREIGN KEY ([PrinterModelId]) REFERENCES [PrinterModels] ([PrinterModelId]) ON DELETE NO ACTION,
    CONSTRAINT [FK_PrinterModelConsumables_PrintingConsumables_PrintingConsumableId] FOREIGN KEY ([PrintingConsumableId]) REFERENCES [PrintingConsumables] ([PrintingConsumableId]) ON DELETE NO ACTION
);
GO

CREATE UNIQUE INDEX [IX_Reasons_Name] ON [Reasons] ([Name]);
GO

CREATE UNIQUE INDEX [IX_Departments_Name] ON [Departments] ([Name]);
GO

CREATE UNIQUE INDEX [IX_PrinterModelConsumables_PrinterModelId_PrintingConsumableId] ON [PrinterModelConsumables] ([PrinterModelId], [PrintingConsumableId]);
GO

CREATE INDEX [IX_PrinterModelConsumables_PrintingConsumableId] ON [PrinterModelConsumables] ([PrintingConsumableId]);
GO

CREATE UNIQUE INDEX [IX_PrinterModels_Name] ON [PrinterModels] ([Name]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261008022355_AddOperatorConfiguration', N'10.0.13');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [PrintingStockEntries] DROP CONSTRAINT [FK_PrintingStockEntries_PrintingConsumables_PrintingConsumableId];
GO

ALTER TABLE [PosterRequests] ADD [DepartmentName] nvarchar(128) NOT NULL DEFAULT N'';
GO

ALTER TABLE [PosterRequests] ADD [ReasonName] nvarchar(128) NULL;
GO

UPDATE request
SET DepartmentName = department.Name
FROM PosterRequests AS request
INNER JOIN Departments AS department ON department.DepartmentId = request.DepartmentId;
GO

UPDATE request
SET ReasonName = reason.Name
FROM PosterRequests AS request
INNER JOIN Reasons AS reason ON reason.ReasonId = request.ReasonId;
GO

DECLARE @var4 nvarchar(max);
SELECT @var4 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PosterRequests]') AND [c].[name] = N'DepartmentName');
IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [PosterRequests] DROP CONSTRAINT ' + @var4 + ';');
GO

ALTER TABLE [PrintingStockEntries] ADD CONSTRAINT [FK_PrintingStockEntries_PrintingConsumables_PrintingConsumableId] FOREIGN KEY ([PrintingConsumableId]) REFERENCES [PrintingConsumables] ([PrintingConsumableId]) ON DELETE NO ACTION;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261008032642_ProtectConfigurationHistory', N'10.0.13');

COMMIT;
GO

BEGIN TRANSACTION;
DROP TABLE [AspNetRoleClaims];
GO

DROP TABLE [AspNetUserClaims];
GO

DROP TABLE [AspNetUserLogins];
GO

DROP TABLE [AspNetUserRoles];
GO

DROP TABLE [AspNetUserTokens];
GO

DROP TABLE [AspNetRoles];
GO

DROP TABLE [AspNetUsers];
GO

EXEC sp_rename N'[PosterProcessings].[ITPerson]', N'ProcessedByAccountName', 'COLUMN';
GO

DECLARE @var5 nvarchar(max);
SELECT @var5 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PosterProcessings]') AND [c].[name] = N'ProcessedByAccountName');
IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [PosterProcessings] DROP CONSTRAINT ' + @var5 + ';');
ALTER TABLE [PosterProcessings] ALTER COLUMN [ProcessedByAccountName] nvarchar(256) NULL;
GO

EXEC sp_rename N'[PosterRequests].[SubmittedByUserName]', N'SubmittedByAccountName', 'COLUMN';
GO

ALTER TABLE [PosterRequests] ADD [SubmittedBySecurityIdentifier] nvarchar(184) NULL;
GO

ALTER TABLE [PosterProcessings] ADD [ProcessedBySecurityIdentifier] nvarchar(184) NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261023184022_RemoveApplicationManagedAuthentication', N'10.0.13');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [PosterRequests] ADD [SubmittedByObjectId] uniqueidentifier NULL;
GO

ALTER TABLE [PosterProcessings] ADD [ProcessedByObjectId] uniqueidentifier NULL;
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261108191516_AddEntraObjectIdentifiers', N'10.0.13');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [PosterRequests] ADD [OriginalDirectory] nvarchar(512) NULL;
GO

ALTER TABLE [PosterRequests] ADD [PermanentlyDeletedAt] datetime2 NULL;
GO

ALTER TABLE [PosterRequests] ADD [TemporaryDeletionAt] datetime2 NULL;
GO

CREATE TABLE [PosterRetentionAudits] (
    [PosterRetentionAuditId] int NOT NULL IDENTITY,
    [PosterRequestId] int NOT NULL,
    [PosterId] nvarchar(64) NOT NULL,
    [Action] nvarchar(32) NOT NULL,
    [OccurredAt] datetime2 NOT NULL,
    [ActorObjectId] uniqueidentifier NULL,
    [OriginalDirectory] nvarchar(512) NOT NULL,
    CONSTRAINT [PK_PosterRetentionAudits] PRIMARY KEY ([PosterRetentionAuditId]),
    CONSTRAINT [FK_PosterRetentionAudits_PosterRequests_PosterRequestId] FOREIGN KEY ([PosterRequestId]) REFERENCES [PosterRequests] ([PosterRequestId]) ON DELETE CASCADE
);
GO

CREATE INDEX [IX_PosterRetentionAudits_PosterRequestId] ON [PosterRetentionAudits] ([PosterRequestId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261114155335_AddTemporaryPosterRetention', N'10.0.13');

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [PosterRequests] ADD [PosterTitle] nvarchar(200) NULL;
GO

ALTER TABLE [PosterRequests] ADD [TdxClaimedUntil] datetime2 NULL;
GO

ALTER TABLE [PosterRequests] ADD [TdxTicketId] int NULL;
GO

ALTER TABLE [PosterRequests] ADD [TdxTicketState] nvarchar(16) NOT NULL DEFAULT N'None';
GO

CREATE TABLE [PosterReplacements] (
    [PosterReplacementId] int NOT NULL IDENTITY,
    [PosterRequestId] int NOT NULL,
    [ReplacedAt] datetime2 NOT NULL,
    [Note] nvarchar(500) NOT NULL,
    [ArchivedFilePath] nvarchar(400) NOT NULL,
    [ReplacedByObjectId] uniqueidentifier NOT NULL,
    CONSTRAINT [PK_PosterReplacements] PRIMARY KEY ([PosterReplacementId]),
    CONSTRAINT [FK_PosterReplacements_PosterRequests_PosterRequestId] FOREIGN KEY ([PosterRequestId]) REFERENCES [PosterRequests] ([PosterRequestId]) ON DELETE CASCADE
);
GO

CREATE UNIQUE INDEX [IX_PosterRequests_TdxTicketId] ON [PosterRequests] ([TdxTicketId]) WHERE [TdxTicketId] IS NOT NULL;
GO

CREATE INDEX [IX_PosterReplacements_PosterRequestId] ON [PosterReplacements] ([PosterRequestId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261117003728_AddTdxIntegrationAndReplacements', N'10.0.13');

COMMIT;
GO


SET NOEXEC OFF;
GO

-- 3. Check
SELECT [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId];
SELECT name AS [Table] FROM sys.tables WHERE SCHEMA_NAME(schema_id) = N'dbo' ORDER BY name;
