/*
    Poster upgrade for an existing database POSTER (PHASE 13).

    This is not the clean-install script. It does not create or drop a database.
    It applies only migration 20261108191516_AddEntraObjectIdentifiers
    when POSTER exists and its latest applied migration is
    20261023184022_RemoveApplicationManagedAuthentication.
    If that is not the database state, this script stops and changes nothing.
    The migration adds two nullable columns. It changes no existing value,
    deletes nothing, and keeps the historical SID and account-name columns.
    Generated from dotnet ef migrations script, dotnet-ef 10.0.13.
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
    WHERE [MigrationId] = N'20261108191516_AddEntraObjectIdentifiers')
BEGIN
    RAISERROR(N'Migration 20261108191516_AddEntraObjectIdentifiers is already applied to POSTER. No changes were made.', 16, 1);
    SET NOEXEC ON;
END
ELSE IF NOT EXISTS (
    SELECT 1
    FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023184022_RemoveApplicationManagedAuthentication'
      AND [ProductVersion] = N'10.0.13')
    OR (SELECT COUNT(*) FROM [__EFMigrationsHistory]) <> 8
BEGIN
    RAISERROR(N'POSTER is not at migration 20261023184022_RemoveApplicationManagedAuthentication. No changes were made. Apply docs/database/Poster.Database.Upgrade.sql first if POSTER is at 20261008032642_ProtectConfigurationHistory.', 16, 1);
    SET NOEXEC ON;
END
GO

SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
BEGIN TRANSACTION;
BEGIN TRANSACTION;
ALTER TABLE [PosterRequests] ADD [SubmittedByObjectId] uniqueidentifier NULL;

ALTER TABLE [PosterProcessings] ADD [ProcessedByObjectId] uniqueidentifier NULL;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261108191516_AddEntraObjectIdentifiers', N'10.0.13');

COMMIT;

IF NOT EXISTS (
    SELECT 1
    FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261108191516_AddEntraObjectIdentifiers'
      AND [ProductVersion] = N'10.0.13')
    THROW 51031, N'The upgrade migration was not recorded. The upgrade transaction was not committed.', 1;

IF (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = N'dbo'
      AND DATA_TYPE = N'uniqueidentifier'
      AND IS_NULLABLE = N'YES'
      AND (
        (TABLE_NAME = N'PosterRequests' AND COLUMN_NAME = N'SubmittedByObjectId')
        OR (TABLE_NAME = N'PosterProcessings' AND COLUMN_NAME = N'ProcessedByObjectId'))) <> 2
    THROW 51032, N'An Entra object identifier column is missing or has the wrong type. The upgrade transaction was not committed.', 1;

IF (SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_SCHEMA = N'dbo'
      AND (
        (TABLE_NAME = N'PosterRequests' AND COLUMN_NAME IN (N'SubmittedByAccountName', N'SubmittedBySecurityIdentifier'))
        OR (TABLE_NAME = N'PosterProcessings' AND COLUMN_NAME IN (N'ProcessedByAccountName', N'ProcessedBySecurityIdentifier')))) <> 4
    THROW 51033, N'A historical identity column is missing after the upgrade. The upgrade transaction was not committed.', 1;

COMMIT TRANSACTION;
PRINT N'POSTER upgrade committed. Entra object identifier columns were added.';
GO

SET NOEXEC OFF;
GO
