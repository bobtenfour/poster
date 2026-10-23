/*
    Poster upgrade for an existing database POSTER.

    This is not the clean-install script. It does not create or drop a database.
    It applies only migration 20261023184022_RemoveApplicationManagedAuthentication
    when POSTER exists and its latest applied migration is
    20261008032642_ProtectConfigurationHistory.
    If that is not the database state, this script stops and changes nothing.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF DB_ID(N'POSTER') IS NULL
BEGIN
    RAISERROR(N'Database POSTER does not exist. Use the clean-install script. No changes were made.', 16, 1);
    SET NOEXEC ON;
END
GO

USE [POSTER];
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;

IF EXISTS (
    SELECT 1
    FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023184022_RemoveApplicationManagedAuthentication')
BEGIN
    RAISERROR(N'Migration 20261023184022_RemoveApplicationManagedAuthentication is already applied to POSTER. No changes were made.', 16, 1);
    SET NOEXEC ON;
END
ELSE IF NOT EXISTS (
    SELECT 1
    FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261008032642_ProtectConfigurationHistory'
      AND [ProductVersion] = N'10.0.13')
BEGIN
    RAISERROR(N'POSTER is not at migration 20261008032642_ProtectConfigurationHistory. No changes were made. Use the clean-install script for a new database.', 16, 1);
    SET NOEXEC ON;
END
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
BEGIN TRANSACTION;
DROP TABLE [AspNetRoleClaims];

DROP TABLE [AspNetUserClaims];

DROP TABLE [AspNetUserLogins];

DROP TABLE [AspNetUserRoles];

DROP TABLE [AspNetUserTokens];

DROP TABLE [AspNetRoles];

DROP TABLE [AspNetUsers];

EXEC sp_rename N'[PosterProcessings].[ITPerson]', N'ProcessedByAccountName', 'COLUMN';

DECLARE @var nvarchar(max);
SELECT @var = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PosterProcessings]') AND [c].[name] = N'ProcessedByAccountName');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [PosterProcessings] DROP CONSTRAINT ' + @var + ';');
ALTER TABLE [PosterProcessings] ALTER COLUMN [ProcessedByAccountName] nvarchar(256) NULL;

EXEC sp_rename N'[PosterRequests].[SubmittedByUserName]', N'SubmittedByAccountName', 'COLUMN';

ALTER TABLE [PosterRequests] ADD [SubmittedBySecurityIdentifier] nvarchar(184) NULL;

ALTER TABLE [PosterProcessings] ADD [ProcessedBySecurityIdentifier] nvarchar(184) NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261023184022_RemoveApplicationManagedAuthentication', N'10.0.13');

COMMIT;

IF EXISTS (SELECT 1 FROM sys.tables WHERE schema_id = SCHEMA_ID(N'dbo') AND [name] LIKE N'AspNet%')
    THROW 51021, N'Application-managed authentication tables remain after the upgrade. The upgrade transaction was not committed.', 1;

IF EXISTS (SELECT 1 FROM sys.columns WHERE [name] = N'PasswordHash')
    THROW 51022, N'A password hash column remains after the upgrade. The upgrade transaction was not committed.', 1;

IF NOT EXISTS (
    SELECT 1
    FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023184022_RemoveApplicationManagedAuthentication'
      AND [ProductVersion] = N'10.0.13')
    THROW 51023, N'The upgrade migration was not recorded. The upgrade transaction was not committed.', 1;

COMMIT TRANSACTION;
PRINT N'POSTER upgrade committed. Application-managed authentication storage was removed.';
GO

SET NOEXEC OFF;
GO