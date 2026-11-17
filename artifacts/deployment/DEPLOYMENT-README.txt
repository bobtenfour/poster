PosterPrintRequest production deployment — PHASE 12 (Qumulo storage access) and PHASE 13 (Entra ID / OIDC)

Governing documents: docs/Storage_Access_Contract_v1.docx and docs/Authentication_Contract_v1.docx.
This release replaces Windows Negotiate with Microsoft Entra ID. Do not deploy it until every item in
sections B and C is done; the application refuses to start when its required configuration is missing.

Files in this folder
- Build-Deployment.ps1          Builds the PosterPrintRequest-Release-<commit> directory only. No ZIP or checksum file is created.
- Poster.Database.Upgrade.sql   Existing POSTER database: adds migration 20261108191516_AddEntraObjectIdentifiers.
- Poster.Database.Install.sql   New POSTER database only: creates POSTER with all nine migrations.
- DEPLOYMENT-README.txt         This file.
The release directory contains the Release publish output directly. It contains no source code, database
script, connection string, Entra identifier, client secret, Qumulo credential or credential file.

A. Build (build machine)
1. Commit the source changes. The build refuses uncommitted build inputs unless -AllowUncommittedChanges.
2. Run: powershell -ExecutionPolicy Bypass -File C:\projects\posters\artifacts\deployment\Build-Deployment.ps1
3. The verified release directory is created under artifacts\deployment\PosterPrintRequest-Release-<commit>.

B. External prerequisites (IT and server administrator)
Entra (Authentication Contract sections 4, 5.4, 5.13, 13):
- App registration, Web platform, redirect URI https://posters.den.uic.edu/PosterPrintRequest/signin-oidc.
- The implicit-flow option "ID tokens" stays disabled. No sign-out redirect URI.
- App Roles with values Requester and Technician; assignments managed by IT.
- Optional claim onprem_sid in the ID token.
- Client secret created; Secret Value handed to the server administrator through a controlled channel.
IIS (Authentication Contract sections 5.5, 7, 10):
- Windows Authentication disabled and Anonymous Authentication enabled for the application.
- Application Pool LoadUserProfile = false (verify; set if necessary).
- Run Microsoft's Provision-AutoGenKeys.ps1 for the PosterPrintRequest Application Pool.
Credentials (provisioned on the IIS host, never in the release directory):
- Entra client secret file: docs/Entra_Credential_Provisioning.md.
- Qumulo credential file:   docs/Storage_Credential_Provisioning.md.
- Storage Access Contract section 13 (E1-E5) and Authentication Contract section 13 (A1-A8) confirmations on record.

C. IIS application configuration (environment variables of the PosterPrintRequest application only)
- ConnectionStrings__PosterPrintRequest   Windows Integrated Authentication, database POSTER.
- PrinterMonitoring__BaseUrl              Printer address.
- SharedStorage__RootPath                 UNC path of the Qumulo share.
- SharedStorage__CredentialFile           Absolute path of the Qumulo DPAPI credential file.
- Authentication__TenantId                Entra tenant identifier.
- Authentication__ClientId                Entra application (client) identifier.
- Authentication__ClientSecretFile        Absolute path of the Entra DPAPI client secret file.
Never put a password, secret or connection string in appsettings.json or in the release directory.

D. Database (SQL Server administrator; back up POSTER first)
- Existing POSTER at migration 20261023184022_RemoveApplicationManagedAuthentication:
  run Poster.Database.Upgrade.sql. It adds two nullable columns and changes no existing data.
- Existing POSTER still at 20261008032642_ProtectConfigurationHistory:
  run docs/database/Poster.Database.Upgrade.sql first, then Poster.Database.Upgrade.sql from this folder.
- No POSTER database yet: run Poster.Database.Install.sql.
Each script checks the database state and stops without changes when the state is not the expected one.
Before deploying, count historical requests that cannot be linked (read-only):
  SELECT COUNT(*) AS RequestsWithoutSubmitterSid FROM dbo.PosterRequests WHERE SubmittedBySecurityIdentifier IS NULL;

E. Deploy (IIS host)
1. Use the release directory produced on the build machine; no ZIP extraction or checksum-file step is required.
2. Use the existing PosterPrintRequest application's physical directory. Do not change that path.
3. Stop the Application Pool. Copy the contents of the release directory into the physical directory, replacing the published files.
   Persistent files on the Qumulo share and the database are not in the release directory and are not replaced.
4. Start the Application Pool.
5. Check https://posters.den.uic.edu/PosterPrintRequest/health/ready and the startup log: no storage or
   credential refusal and no "ephemeral key repository" warning.
6. Sign in with a Requester, a Technician, a user with both roles and a user with neither (AccessDenied).

F. Rollback
- Restore the previous published files into the same directory and restart the Application Pool.
- The previous release used Windows Negotiate: re-enable Windows Authentication for the application.
- The database upgrade is additive (two nullable columns); the previous release ignores them, so the
  database does not need to be rolled back.
