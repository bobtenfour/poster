/*
    PosterPrintRequest - schema update for PHASE 14 (temporary retention) and PHASE 15 (TDX + poster replacement).

    Run it in SSMS against the application's database (select the database first, or uncomment USE).
    Idempotent: each migration runs only if it is not yet in __EFMigrationsHistory, so it is safe
    whether the database is at PHASE 13 (AddEntraObjectIdentifiers) or already at PHASE 14.
    It adds columns and tables only. It changes no existing value and deletes nothing.
    Existing requests get TdxTicketState = 'None' (no ticket, no title), as the contract says (4.5).
    Generated with dotnet ef migrations script --idempotent.
*/
-- USE [<database-name>];
-- GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
GO

IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
    OR NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261108191516_AddEntraObjectIdentifiers')
BEGIN
    RAISERROR(N'This database is not at PHASE 13 (AddEntraObjectIdentifiers). No changes were made.', 16, 1);
    SET NOEXEC ON;
END
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114155335_AddTemporaryPosterRetention'
)
BEGIN
    ALTER TABLE [PosterRequests] ADD [OriginalDirectory] nvarchar(512) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114155335_AddTemporaryPosterRetention'
)
BEGIN
    ALTER TABLE [PosterRequests] ADD [PermanentlyDeletedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114155335_AddTemporaryPosterRetention'
)
BEGIN
    ALTER TABLE [PosterRequests] ADD [TemporaryDeletionAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114155335_AddTemporaryPosterRetention'
)
BEGIN
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
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114155335_AddTemporaryPosterRetention'
)
BEGIN
    CREATE INDEX [IX_PosterRetentionAudits_PosterRequestId] ON [PosterRetentionAudits] ([PosterRequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261114155335_AddTemporaryPosterRetention'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261114155335_AddTemporaryPosterRetention', N'10.0.13');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117003728_AddTdxIntegrationAndReplacements'
)
BEGIN
    ALTER TABLE [PosterRequests] ADD [PosterTitle] nvarchar(200) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117003728_AddTdxIntegrationAndReplacements'
)
BEGIN
    ALTER TABLE [PosterRequests] ADD [TdxClaimedUntil] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117003728_AddTdxIntegrationAndReplacements'
)
BEGIN
    ALTER TABLE [PosterRequests] ADD [TdxTicketId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117003728_AddTdxIntegrationAndReplacements'
)
BEGIN
    ALTER TABLE [PosterRequests] ADD [TdxTicketState] nvarchar(16) NOT NULL DEFAULT N'None';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117003728_AddTdxIntegrationAndReplacements'
)
BEGIN
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
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117003728_AddTdxIntegrationAndReplacements'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_PosterRequests_TdxTicketId] ON [PosterRequests] ([TdxTicketId]) WHERE [TdxTicketId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117003728_AddTdxIntegrationAndReplacements'
)
BEGIN
    CREATE INDEX [IX_PosterReplacements_PosterRequestId] ON [PosterReplacements] ([PosterRequestId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261117003728_AddTdxIntegrationAndReplacements'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261117003728_AddTdxIntegrationAndReplacements', N'10.0.13');
END;

COMMIT;
GO


SET NOEXEC OFF;
GO

-- Check
SELECT [MigrationId] FROM [__EFMigrationsHistory] ORDER BY [MigrationId];
SELECT [TdxTicketState], COUNT(*) AS [Requests] FROM [PosterRequests] GROUP BY [TdxTicketState];
