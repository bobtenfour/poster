# POSTER database installation

These instructions are for the university SQL Server administrator. The accompanying file `Poster.Database.Install.md` contains the complete clean-install script. Copy only the SQL inside its fenced block and execute that whole script.

The clean-install script creates database `POSTER` on the instance to which the administrator is already connected. The administrator does not create the database first. The script does not create a SQL Server login, an application password, a server role, or the application's connection string.

The university SQL Server host name, port, instance name, application service identity, and certificate requirements have not been supplied. This package does not invent them. The script does not need those values.

## Responsibilities

- The SQL administrator runs the clean-install script and, after the university names the application service identity, grants that identity database access. The script does not perform the grant.
- The infrastructure team provides the network path and firewall rules from the IIS virtual machine to SQL Server, joins that machine to the institutional domain, and establishes certificate trust if the connection requires encryption.
- The application connects with its own Windows service identity. It does not connect as the signed-in student or staff member, and end users are not given a SQL Server login.

## Permission required to run the script

The account that executes the clean-install script needs `CREATE DATABASE` on the existing instance. Membership in `dbcreator` or `sysadmin` provides that permission. That is the only server-level permission the script uses.

After installation, the application service identity needs `CONNECT` on `POSTER`, membership in `db_datareader`, and membership in `db_datawriter`. It does not need `db_owner` or `db_ddladmin`. The application does not apply migrations at startup.

The service identity is not yet known. When the university names it, run the grant separately. Replace the placeholder with that exact Windows account. Do not substitute a guessed domain or account name.

```sql
CREATE USER [<university-application-service-identity>] FOR LOGIN [<university-application-service-identity>];
ALTER ROLE db_datareader ADD MEMBER [<university-application-service-identity>];
ALTER ROLE db_datawriter ADD MEMBER [<university-application-service-identity>];
```

The login itself must already exist as a Windows login created by the university's account process. These statements do not create a password and are not part of the clean-install script.

## How to execute the clean installation

1. Connect SQL Server Management Studio to the existing instance. The current database may be `master`. Do not create database `POSTER`.
2. Open `Poster.Database.Install.md`. Copy the contents of the fenced `sql` block, from the opening comment through the final `SET NOEXEC OFF`. Do not copy the Markdown fence lines or the text outside the block.
3. Paste the script into a new query window and execute the entire script. Do not highlight a portion of it.
4. Read the Messages pane.

Run the script in one session. `CREATE DATABASE` is in its own batch because SQL Server cannot include it in the schema transaction. The schema statements that follow run in one transaction with `XACT_ABORT ON`.

## Success

Messages include:

`Database POSTER installed. Schema transaction committed.`

SQL Server may also report index-length warnings for `PK_AspNetUserLogins`, `PK_AspNetUserRoles`, and `PK_AspNetUserTokens`, and caution messages from `sp_rename`. Those messages come from the historical migration chain inside the schema transaction. The same transaction drops the application-managed authentication tables and renames the account columns before it commits. A successful commit does not leave those tables or a password hash column in place. Confirm that with the verification queries below.

The finished database has eleven tables:

- `__EFMigrationsHistory`
- `Departments`, `Reasons`
- `PosterRequests`, `PosterFiles`, `ApprovalSheets`, `PosterProcessings`
- `PrintingConsumables`, `PrintingStockEntries`
- `PrinterModels`, `PrinterModelConsumables`

`__EFMigrationsHistory` has eight rows. Every `ProductVersion` is `10.0.13`. The latest `MigrationId` is `20261023184022_RemoveApplicationManagedAuthentication`.

No departments, reasons, consumables, printer models, or requests are inserted. File locations, growth, collation, and compatibility level stay at the instance defaults.

## If POSTER already exists

The first batch checks `DB_ID(N'POSTER')`. When that database exists, the script raises severity 16 with the message `Database POSTER already exists. No changes were made.` and turns `NOEXEC` on. `CREATE DATABASE`, `USE`, and the schema batches are parsed and then skipped. The final `SET NOEXEC OFF` still runs, so the query window remains usable. Nothing in the existing database is changed. No rollback is required.

`RAISERROR` is used instead of `THROW` so the same batch can turn `NOEXEC` on. `THROW` would stop the batch before that setting took effect.

## If installation fails after database creation

`CREATE DATABASE` commits immediately and cannot be rolled back with the schema transaction. If a later batch fails, database `POSTER` may exist and be empty or only partly built. `XACT_ABORT` rolls back the schema transaction. The script does not contain `DROP DATABASE`.

Do not run the clean-install script again while that database still exists. The second run will stop without repairing it. Inspect the error. If the database is unused, remove it through the university's normal change process, then run the clean-install script again.

## Schema verification

Run these statements while connected to `POSTER`:

```sql
SELECT DB_NAME() AS CurrentDatabase;

SELECT MigrationId, ProductVersion
FROM __EFMigrationsHistory
ORDER BY MigrationId;

SELECT name
FROM sys.tables
WHERE schema_id = SCHEMA_ID(N'dbo')
ORDER BY name;

SELECT COUNT(*) AS PasswordHashColumns
FROM sys.columns
WHERE name = N'PasswordHash';

SELECT COUNT(*) AS AspNetTables
FROM sys.tables
WHERE name LIKE N'AspNet%';

SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN (N'PosterRequests', N'PosterProcessings')
  AND COLUMN_NAME IN (
      N'SubmittedByAccountName', N'SubmittedBySecurityIdentifier',
      N'ProcessedByAccountName', N'ProcessedBySecurityIdentifier',
      N'SubmittedByUserName', N'ITPerson', N'Email', N'Phone', N'PickedUpBy')
ORDER BY TABLE_NAME, COLUMN_NAME;

SELECT name, delete_referential_action_desc
FROM sys.foreign_keys
ORDER BY name;

SELECT COUNT(*) AS DepartmentRows FROM Departments;
SELECT COUNT(*) AS ReasonRows FROM Reasons;
```

Expected results:

- `DB_NAME()` is `POSTER`.
- Eight history rows, each with `ProductVersion` `10.0.13`, ending with `20261023184022_RemoveApplicationManagedAuthentication`.
- The eleven tables listed above. `PrintingInventorySettings` is absent. No table name starts with `AspNet`.
- `PasswordHashColumns` is 0 and `AspNetTables` is 0.
- `PosterRequests.SubmittedByAccountName` is nullable `nvarchar(256)`. `PosterRequests.SubmittedBySecurityIdentifier` is nullable `nvarchar(184)`. `PosterRequests.Email` and `PosterRequests.Phone` remain.
- `PosterProcessings.ProcessedByAccountName` is nullable `nvarchar(256)`. `PosterProcessings.ProcessedBySecurityIdentifier` is nullable `nvarchar(184)`. `PosterProcessings.PickedUpBy` remains.
- `SubmittedByUserName` and `ITPerson` are absent.
- Poster file, approval sheet, and processing rows cascade when a poster request is deleted. Department, reason, stock, and printer-model relationships are `NO_ACTION`.
- Department and reason row counts are 0 immediately after a clean install.

The script itself throws, and rolls the schema transaction back, if the history, tables, password storage, or identity columns do not match this result. A committed installation has already passed those checks.

## Clean installation and upgrade

The clean-install script is for a new `POSTER` database. It replays the whole EF Core migration chain, including the historical statements that create application-managed authentication tables, and then removes those tables in the same transaction. It is the script a new university database uses.

The upgrade script below is a different script. Use it only when database `POSTER` already exists, migration `20261008032642_ProtectConfigurationHistory` is already applied, and migration `20261023184022_RemoveApplicationManagedAuthentication` is not. It does not create a database and it does not drop a database. It preserves poster, department, reason, file, inventory, and printer data. It renames `SubmittedByUserName` to `SubmittedByAccountName` and `ITPerson` to `ProcessedByAccountName`, so existing account text is kept, and it adds the security-identifier columns. It drops the seven `AspNet*` tables.

If `POSTER` is missing, if the previous migration is missing, or if the new migration is already present, the upgrade script raises an error and changes nothing. A failure inside the upgrade transaction rolls that transaction back and leaves the database in place.

```sql
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
```

## What this package does not configure

The clean-install script and the upgrade script do not create server logins, SQL passwords, application passwords, or the application's connection string. They do not open firewall ports, choose a certificate, or enable IIS authentication.

The application reads its connection string from the deployment setting `ConnectionStrings__PosterPrintRequest`. In the Production environment the process refuses to start unless that string uses Windows Integrated Authentication, contains no user id and no password, and names the database `POSTER`. The server name, port, and encryption settings are supplied by deployment configuration when the university publishes them. `TrustServerCertificate=True` is not an approved institutional setting; certificate trust belongs to the infrastructure team.

Commit `4745037` stores the local development connection string in `appsettings.json`. That string points at a development database named `PosterPrintRequest` on `localhost` and uses Windows Integrated Authentication. It is not the institutional connection. The uncommitted working tree removes it from `appsettings.json` and keeps the local development string only in `appsettings.Development.json`. This document does not record the string. The Linux demonstration host uses SQL authentication to its own database and is not the institutional deployment. Production startup rejects that form of connection string.

The IIS site uses the in-process ASP.NET Core module. The repository `web.config` does not enable or disable Windows Authentication, because that section is normally locked in `applicationHost.config`. The university IIS configuration still has to enable Windows Authentication, disable Anonymous Authentication, and run the application pool as the Windows identity that was granted access to `POSTER`. The site must not impersonate the end user for the SQL connection. The virtual machine must be domain-joined so the user's group SIDs are on the Windows token. These IIS and Active Directory settings have not been verified against the university environment. If the Windows token or the group membership cannot be verified, the application denies access. It does not fall back to a password form or to a claim that is not a Windows group.

Technician operations require the Windows group `GroupPCAdmins`. Requester operations require the Windows group `DEN-DentistryUsers`. A user in neither group is denied. Membership in one group does not grant the other group's operations. A user in both groups receives only the operations each policy allows.

Request submission records the Windows account name and the Windows security identifier. Technician processing records the signed-in technician's account name and security identifier. The account name is the logon name from Windows authentication, not a display name the technician can type. Email and phone remain requester contact fields. `PickedUpBy` remains the name of the person who collects the poster. The database does not keep a local user registry or a password hash.

## Local execution evidence

The clean-install script was executed against a disposable SQL Server instance on this workstation. That instance is not the university SQL Server, and the institutional host name was not used.

The first execution created `POSTER`, committed the schema, and reported `Database POSTER installed. Schema transaction committed.` Verification showed eight history rows at product version `10.0.13`, the eleven approved tables, no `AspNet` table, no `PasswordHash` column, the identity column types and lengths above, the foreign-key delete behaviors above, and zero department and reason rows.

A second execution against that database reported `Database POSTER already exists. No changes were made.` The history count stayed at eight.

The upgrade script, run against that already-current database, reported that migration `20261023184022_RemoveApplicationManagedAuthentication` was already applied and made no changes.

The disposable database was then removed. A second disposable `POSTER` was created at migration `20261008032642_ProtectConfigurationHistory`, where the `AspNet` tables were still present. The upgrade script committed, left eight history rows, left no `AspNet` table and no `PasswordHash` column, and left `SubmittedByAccountName` and `ProcessedByAccountName` in place of the old column names.

That disposable `POSTER` database was dropped after the checks. The local development database `PosterPrintRequest` was not dropped. No statement was executed against the university SQL Server, and the institutional connection has not been tested.
