# PosterPrintRequest --- IIS Installation Guide and Interactive Installer Specification

**Status:** Operational draft and specification pending closure\
**Date:** November 7, 2026\
**Application:** PosterPrintRequest\
**Platform:** ASP.NET Core / .NET 10, Blazor Server, SQL Server, IIS,
and Qumulo SMB shared storage\
**Scope:** From-zero installation on a Windows/IIS server, application
configuration, DPAPI credential-file provisioning, and requirements for
an administrator-facing interactive installer.

> **Important --- authority boundaries**
>
> This document organizes the steps and transcribes requirements from
> the governing contracts. It does not replace or amend the roadmap or
> contracts. Values not established by those documents are explicitly
> marked **pending confirmation**. They must not become installer
> defaults through inference.
>
> PHASE 12 implementation is authorized by the roadmap. Production
> acceptance remains conditional on the external confirmations E1--E5
> and the production tests defined in `Storage_Access_Contract_v1.docx`,
> §§13--14.

------------------------------------------------------------------------

## 1. Governing documents and precedence

### 1.1 Project-document precedence

Apply this hierarchy without mixing responsibilities:

1.  **`docs/Poster_Print_Request_System_Roadmap_v4_Inventory.docx`**\
    Sole authority for phase order, status, promotion, and the next
    phase. The roadmap authorizes PHASE 12 --- Qumulo SMB Storage
    Access. This guide does not change its status or order.

2.  **`docs/Storage_Access_Contract_v1.docx`**\
    Exclusive authority for the storage-access boundary, SMB identity,
    access modes, credential protection and rotation, runtime-context
    behavior, permissions, and failures.

3.  **`docs/Shared_Storage_Contract_v1.docx`**\
    Exclusive authority for folder structure, paths, placement,
    migration, movement when marking Printed, and reversal on failure.
    Storage Access cannot redefine these rules.

4.  **Printing Consumables Inventory Contract**\
    Authority for its own scope only. It does not change SMB identity or
    shared-storage structure.

5.  **This document**\
    Installation guide and proposed installer requirements. It cannot
    expand, amend, or contradict the documents above.

### 1.2 .NET application-configuration precedence

For an ASP.NET Core application using the default
`WebApplication.CreateBuilder` configuration setup, the usual
application-configuration provider priority, highest to lowest, is:

1.  Command-line arguments.
2.  Environment variables.
3.  User Secrets, Development environment only.
4.  `appsettings.{Environment}.json`, for example
    `appsettings.Production.json`.
5.  `appsettings.json`.

A provider added later can override earlier values. The actual
configuration must be checked in `Program.cs`; the installer must not
assume a different order without code evidence.

**Practical consequence:** values supplied as IIS application-level
environment variables normally override equivalent JSON settings. .NET
hierarchical configuration keys use double underscore (`__`) in
environment-variable names.

Examples authorized by the storage contract:

-   `SharedStorage__RootPath`
-   `SharedStorage__CredentialFile`
-   `ConnectionStrings__PosterPrintRequest`

The Qumulo password is **not** configured as an environment variable or
as text in `appsettings.json`. It is stored only inside the
DPAPI-protected credential file.

------------------------------------------------------------------------

## 2. Functional scope and decisions that must not change

-   The application runs on Windows Server under IIS.
-   Existing Windows Negotiate sign-in is preserved.
-   The SQL Server connection continues to use Windows Integrated
    Authentication.
-   The designated shared storage is Qumulo over SMB.
-   The identity used against Qumulo is a dedicated local Qumulo service
    account. It is not the Application Pool identity, SQL Server service
    account, or a person's account.
-   Qumulo REST API is excluded.
-   No fallback to the process identity or another credential is allowed
    when an SMB operation fails.
-   Shared-storage structure and the Shared Storage Contract's
    migration/Printed rules are unchanged.
-   IIS must run a single worker process.
-   The credential is read at startup. No hot reload is allowed.

------------------------------------------------------------------------

## 3. Step 0 --- inventory and preparation before installation

Before changing the server, the administrator and infrastructure owners
must have the following.

### 3.1 Application information

-   Approved PosterPrintRequest deployment package.
-   .NET target version confirmed from the published artifact (`net10.0`
    in the current project state).
-   IIS site name and Application Pool name: **to be selected and
    recorded by the administrator**.
-   Application physical installation path: **to be selected by the
    administrator**.
-   DNS/host name, port, binding, and HTTPS certificate: **pending
    institutional standards**.
-   Runtime environment: `Production`.
-   SQL Server configuration: correct server/instance and database.
    Authentication remains integrated Windows authentication.

### 3.2 Qumulo and infrastructure information

IT must provide and confirm:

-   Full UNC path of the Qumulo share to be used as
    `SharedStorage:RootPath`.
-   Exact domain/authority value to use in the credential's `Domain`
    field.
-   Local Qumulo service-account name.
-   Initial password, delivered securely to the administrator
    provisioning the credential.
-   Confirmation of SMB NTLMv2 authentication and the exact account-name
    form.
-   Confirmation of outbound NTLM policy on the IIS host.
-   Confirmation of TCP 445 connectivity between IIS and Qumulo.
-   SMB dialect and institutional signing/encryption requirements.
-   Required share and filesystem permissions under §8.
-   Credential-file location outside the application, IIS-served
    directory, repository, and deployment package.

### 3.3 Administrator access requirements

The administrator performing installation needs sufficient local
privileges to:

-   Install Windows roles/features and IIS, if not already installed.
-   Install the .NET Hosting Bundle.
-   Create/configure the IIS site and Application Pool.
-   Create application-installation and credential directories.
-   Configure IIS application-level environment variables.
-   Apply and verify NTFS ACLs.
-   Run DPAPI provisioning on the IIS host itself.
-   Recycle the Application Pool and perform verification.

The IIS administrator does not thereby receive Qumulo administrative
privileges. The Qumulo service account is managed by the authorized
Qumulo/IT team.

------------------------------------------------------------------------

## 4. Step 1 --- install and enable Windows components

### 4.1 Operating system and IIS role

On the approved Windows server:

1.  Confirm the operating-system version is supported by institutional
    policy and the selected .NET version.
2.  Open Server Manager.
3.  Select **Add Roles and Features**.
4.  Install the **Web Server (IIS)** role.
5.  Include IIS Management Console.
6.  Include the role services required to host ASP.NET Core.
7.  Include **Windows Authentication**, because the application
    preserves Windows Negotiate.
8.  Confirm with the security administrator whether any additional IIS
    modules/services are required. Do not install optional modules
    without a documented need.

### 4.2 .NET 10 Hosting Bundle

After IIS is installed:

1.  Download the **.NET 10 Hosting Bundle** from Microsoft.
2.  Run the installer on the IIS server.
3.  Confirm it installs the required runtime and ASP.NET Core Module
    (ANCM).
4.  If the Hosting Bundle was installed before IIS, rerun/repair the
    Hosting Bundle after IIS installation.
5.  Restart IIS or the server according to Microsoft guidance and the
    approved change window.

The Hosting Bundle provides the components needed to host ASP.NET Core
applications behind IIS. Do not install the .NET SDK in production
unless a separate, authorized operational need exists. Build/publish
normally happens in the build environment.

### 4.3 Components not authorized by default

-   Do not install Qumulo REST API tools: REST API is excluded.
-   Do not install direct-printing tools.
-   Do not install additional runtimes or modules without a verified
    dependency in the published package.
-   Do not install SQL Server on the IIS server merely because it hosts
    the application; SQL Server is a separate service whose location is
    determined by the existing architecture.

------------------------------------------------------------------------

## 5. Step 2 --- prepare SQL Server and existing authentication

1.  Confirm the PosterPrintRequest database exists and that the IIS
    server can reach SQL Server.
2.  Preserve **Windows Integrated Authentication** in the connection
    string.
3.  The effective identity used for SQL connections must be the one
    resulting from the approved hosting and SQL configuration. Do not
    replace it with the Qumulo account.
4.  Confirm with the DBA which database permissions are required for the
    identity that actually opens the connection.
5.  Configure the connection value through the approved IIS
    configuration mechanism, for example
    `ConnectionStrings__PosterPrintRequest`, if the application consumes
    the standard environment-variable provider.
6.  Do not store SQL passwords in JSON files or in the installer.

**Pending code verification:** confirm the exact connection-string key
and effective SQL identity in the final `Program.cs`/persistence
registration. The installer must reflect the actual code, not invent a
SQL principal or key.

------------------------------------------------------------------------

## 6. Step 3 --- create the IIS site and Application Pool

The following values must be entered or confirmed by the administrator
during installation:

-   IIS site name.
-   Application Pool name.
-   Physical path of the published application.
-   Binding (protocol, IP if applicable, port, and host name).
-   HTTPS certificate and TLS binding, according to institutional
    policy.
-   Application Pool identity.
-   Runtime environment: `Production`.

Contractual rules:

-   Record the actual Application Pool name; it is needed to construct
    the credential-file ACL.
-   Configure a single worker process (`Maximum Worker Processes = 1`).
-   Do not enable alternative identities or SMB fallback.
-   Preserve the existing Windows Negotiate mechanism.
-   Do not replace the dedicated SMB identity with the Application Pool
    identity.

**Pending confirmation:** the storage contract does not specify site
name, pool name, physical path, binding, port, certificate, idle
timeout, recycling schedule, or preload policy. Those values belong to
IIS/institutional administration and must not be hard-coded as defaults
without authorization.

------------------------------------------------------------------------

## 7. Step 4 --- deploy the application publish output

The project's deployment flow is:

1.  Build/publish in Release in the build environment.
2.  Use `dotnet publish` to generate the publish directory.
3.  Copy the publish-directory contents to the physical directory
    configured for the IIS application.
4.  Verify that `web.config` and the published files belong to the same
    build.
5.  Do not copy development files or secrets into the published
    directory.
6.  Do not include the Qumulo credential file in the application
    directory or ZIP package.
7.  Do not manually edit publish output to insert the UNC path, account,
    or password.

Example publish command (in the build environment, not necessarily on
production):

``` powershell
dotnet publish src\PosterPrintRequest.Web\PosterPrintRequest.Web.csproj -c Release
```

The final publish-directory path must be taken from the actual build
output. This guide does not prescribe a machine-specific local path.

------------------------------------------------------------------------

## 8. Step 5 --- configure the Qumulo share and permissions

### 8.1 Service account

IT/Qumulo creates or provides a dedicated local service account. The
application installer must not create this account in Qumulo or change
its password unless an officially authorized administrative interface
exists.

The account:

-   Is used only for PosterPrintRequest SMB access.
-   Is not the administrator's personal account.
-   Is not the Application Pool identity.
-   Is not the SQL Server account.
-   Must authenticate over SMB using NTLMv2.
-   Must not use NTLMv1.

### 8.2 Required Qumulo permissions

The contract requires both permission levels:

  -----------------------------------------------------------------------
  Level                   Required permission     Scope
  ----------------------- ----------------------- -----------------------
  Share                   Read and Change         Share root and
                                                  applicable contents

  Filesystem/ACL          Modify, including       Shared root and
                          Delete and Delete       everything beneath it
                          subfolders and files    
  -----------------------------------------------------------------------

Not required:

-   Change permissions.
-   Take ownership.
-   Cluster administration.
-   Global Qumulo administrative privileges.

**Important contractual warning:** Read + Write without Delete is **not
sufficient** for full application operation. Delete is required because
the application replaces directories/files, moves complete directories
during migration and the Printed transition, and may need to reverse a
failed move.

A limited read/write test can be used as an initial connectivity check,
but it is not production acceptance and does not prove the application
can perform all required operations. Full contractual validation
requires the move and delete operations defined in the contracts.

### 8.3 Network and protocol confirmations

Before production testing, IT confirms:

-   TCP 445 from the IIS host to the Qumulo cluster.
-   SMB 3.x.
-   Signing/encryption requirements.
-   Outbound NTLM policy on the host permits the required
    authentication.
-   The local account supports NTLMv2.

------------------------------------------------------------------------

## 9. Step 6 --- create the protected directory for the encrypted secret

The credential file is stored on the IIS host:

-   Outside the application's physical directory.
-   Outside any directory served by IIS.
-   Outside the repository.
-   Outside the deployment package.
-   At an absolute path selected by the administrator and confirmed in
    E5.

No literal path is prescribed in this guide. The administrator must
select a location according to institutional policy and record it in the
change record, not in the repository.

### 9.1 Required ACL

Disable inheritance on the folder and file, and set exactly the
contractual principals:

  Principal                                      Permission
  ---------------------------------------------- --------------
  `SYSTEM`                                       Full control
  `BUILTIN\Administrators`                       Full control
  `IIS AppPool\<actual Application Pool name>`   Read
  Any other principal                            No access

Confirm the exact Application Pool identity before creating the ACL.

**Why this is critical:** with DPAPI `LocalMachine`, any process on the
same host that can read the file can request its decryption. The ACL
limits who can read the file and therefore who can use the credential.

Do not grant the Application Pool Modify/Write access to the credential
file. The contract requires Read for that identity.

------------------------------------------------------------------------

## 10. Step 7 --- provision the DPAPI file

### 10.1 Logical format before encryption

The logical UTF-8 JSON content has these properties:

``` json
{
  "Domain": "<domain or authority confirmed by IT>",
  "UserName": "<dedicated Qumulo local service account>",
  "Password": "<password received through a secure channel>"
}
```

This JSON is the content protected by the provisioning procedure. **It
must not be stored on disk in plaintext.**

### 10.2 Required protection

The provisioning procedure must:

1.  Run on the destination IIS host.

2.  Construct UTF-8 JSON containing `Domain`, `UserName`, and
    `Password`.

3.  Protect the bytes using Windows DPAPI `ProtectedData.Protect`.

4.  Use `DataProtectionScope.LocalMachine`.

5.  Use the exact UTF-8 bytes of the following additional entropy:

    `PosterPrintRequest.SharedStorage.v1`

6.  Write only the encrypted result to the configured file.

7.  Apply the ACL specified in §9.

8.  Clear plaintext material from memory as soon as practical.

9.  Never print the JSON, password, account, or sensitive path to
    console, logs, or transcript.

Encryption must take place on the IIS server that will decrypt the file.
A copy of the DPAPI file on another machine must not be usable by
itself.

### 10.3 Application configuration

At the **IIS application level**, configure:

  -----------------------------------------------------------------------------
  Variable                                  Value
  ----------------------------------------- -----------------------------------
  `SharedStorage__RootPath`                 Full UNC path of the Qumulo share

  `SharedStorage__CredentialFile`           Absolute path of the
                                            DPAPI-protected file

  `ConnectionStrings__PosterPrintRequest`   SQL connection string using Windows
                                            Integrated Authentication, if
                                            confirmed by the actual
                                            configuration
  -----------------------------------------------------------------------------

Do not put the password in any environment variable. The encrypted
file's path is not the secret, but it is operationally sensitive and
must not be exposed in public logs.

Qumulo-account mode is selected at startup when
`SharedStorage:CredentialFile` is configured. If it is not configured,
the application uses process-identity mode. In Production, if
`SharedStorage:RootPath` is a UNC path and the credential file is not
configured, the application must refuse to start.

------------------------------------------------------------------------

## 11. Step 8 --- startup and runtime behavior

At startup:

1.  The application reads configuration.
2.  It selects the access mode once.
3.  In Qumulo-account mode, it checks Windows and reads the credential
    file.
4.  It decrypts using DPAPI LocalMachine and the contractual entropy.
5.  It validates that Domain, UserName, and Password are present.
6.  It creates the token once using `LogonUser` with:
    -   `LOGON32_LOGON_NEW_CREDENTIALS`
    -   `LOGON32_PROVIDER_WINNT50`
7.  It holds the token in the singleton service for the process
    lifetime.
8.  Each storage operation uses `WindowsIdentity.RunImpersonated` or
    `RunImpersonatedAsync`.
9.  It releases the token when the process shuts down.
10. It clears the plaintext password after creating the token.

`LOGON32_LOGON_NEW_CREDENTIALS` does not validate the password against
the server when the token is created. Therefore, the service attempts to
check the root under that token.

-   If the file is missing, unreadable, cannot be decrypted, or lacks
    required fields, startup fails.
-   If the share is unavailable at startup, the application does not
    stop for that reason alone; health and storage operations report the
    failure.
-   A failed operation is never retried using the process identity or
    another credential.

------------------------------------------------------------------------

## 12. Step 9 --- post-IIS configuration verification

Perform during an approved change window:

1.  Confirm environment variables are associated with the correct IIS
    application.
2.  Confirm the Application Pool identity can read the encrypted file
    and cannot modify it.
3.  Confirm SYSTEM and Administrators retain Full control.
4.  Confirm no other principals have access to the directory/file.
5.  Recycle the Application Pool so it reads configuration and
    credentials at startup.
6.  Check `/health/ready`.
7.  Confirm logs contain no password, file contents, account/domain,
    credential path, UNC root, share/server name, or physical path.
8.  Run authorized SMB tests using an approved test poster and data.
9.  Record the date, operator, and evidence of each test in the
    institutional record, never in the repository if it contains
    sensitive information.

### 12.1 Limited connectivity test

An initial read/write test may verify that the server reaches the share
and basic authentication works. This test is **only a smoke test**.

### 12.2 Full contractual tests

Production acceptance requires, at minimum, verification of:

-   Draft upload.
-   Event poster with an Approval Sheet.
-   Inline download.
-   Attachment download.
-   Technician library.
-   Printed transition using a test poster.
-   Startup migration does not move an already-settled poster again.
-   Credential-rotation drill.
-   No sensitive information in logs.
-   `/health/ready` Healthy.

Full testing requires the §8 permissions, including Delete and Delete
subfolders and files. Do not declare production acceptance based only on
read/write testing.

------------------------------------------------------------------------

## 13. Credential rotation

The contractual rotation interval is every 12 months and immediately
upon suspected exposure.

Procedure during a low-activity window:

1.  IT changes the Qumulo account password.
2.  The administrator generates a new DPAPI file beside the current
    file, using the same scope, entropy, and ACL.
3.  The administrator replaces the current file with the new file using
    a single rename.
4.  Recycle the Application Pool.
5.  Verify `/health/ready`.
6.  Download a test poster.
7.  Record the rotation in the institutional change record, not in the
    repository.

The application reads the credential only at startup. It does not reload
the file while running.

------------------------------------------------------------------------

## 14. Responsibility and permission matrix

  ---------------------------------------------------------------------------------
  Actor                      Permissions/responsibilities   Must not receive
  -------------------------- ------------------------------ -----------------------
  IIS server local           Install IIS/Hosting Bundle,    Qumulo global
  administrator              configure site/pool, create    administration by
                             directories, set ACLs, run     default
                             DPAPI, configure variables,    
                             recycle pool                   

  `SYSTEM` on IIS host       Full control over DPAPI        Interactive use of
                             folder/file as specified by    Qumulo account
                             ACL                            

  `BUILTIN\Administrators`   Full control over DPAPI        Unrequired Qumulo
  on IIS host                folder/file as specified by    permissions
                             ACL                            

  `IIS AppPool\<pool>`       Read DPAPI file; local process Write/Modify on the
                             identity                       secret

  Qumulo service account     Share Read + Change;           Change permissions,
                             filesystem Modify including    Take ownership, cluster
                             Delete and Delete              administration
                             subfolders/files               

  DBA/SQL administrator      Authorize the Windows identity Reuse of the Qumulo
                             that actually connects to SQL  account

  IT/network/Qumulo          Confirm E1--E5, NTLMv2, TCP    Password stored in
                             445, SMB 3.x, ACL and network  repository or logs
                             policy                         
  ---------------------------------------------------------------------------------

------------------------------------------------------------------------

## 15. Interactive installer requirements for administrators

This section defines the desired behavior of the future installer. **It
is a proposed specification; by itself it is not contractual
authorization to implement an installer.** Before developing the
installer, its responsibilities and boundaries must be authorized by the
applicable governing document.

### 15.1 Mandatory principles

The installer must:

-   Run elevated when the task requires it.
-   Be interactive, guided, and validated by stages.
-   Detect existing prerequisites and report what is missing.
-   Never assume site names, pool names, paths, hostnames, ports, or
    certificates.
-   Ask the administrator for values and show a summary before applying
    changes.
-   Separate local IIS configuration from external Qumulo/SQL/network
    configuration.
-   Not create or modify Qumulo accounts.
-   Not grant Qumulo permissions.
-   Not request personal credentials.
-   Request the Qumulo password through secure, non-echoing input,
    without process arguments, transcript, or logs.
-   Create the DPAPI file only on the destination IIS host.
-   Apply and verify the exact ACL.
-   Never write the password to appsettings, environment variables,
    persistent plaintext temporary files, or packages.
-   Never include the encrypted credential file in the site directory,
    installer package, repository, or logs.
-   Not change Windows Negotiate or SQL Integrated Authentication.
-   Not deploy database changes or alter contracts.
-   Stop on failed preconditions, contradictory values, or insufficient
    permissions.
-   Be idempotent: detect existing configuration and never silently
    overwrite it.
-   Clearly show what it will change and what requires the IT team.

### 15.2 Proposed interactive flow

**Screen 0 --- Welcome and scope** - Identify application and package
version. - Show target environment. - Explain actions and boundaries. -
Show detected prerequisites.

**Screen 1 --- Server prerequisites** - Detect Windows Server. - Detect
IIS role and management console. - Detect Windows Authentication. -
Detect ASP.NET Core Module and .NET 10 Hosting Bundle. - Report missing
components. - Do not install automatically without confirmation and
elevation.

**Screen 2 --- Site and Application Pool** - Request site name. -
Request Application Pool name. - Request physical installation path. -
Request HTTP/HTTPS binding, host, and certificate according to
institutional policy. - Select/confirm pool identity. - Enforce/validate
one worker process. - Confirm before creating or modifying anything.

**Screen 3 --- Application package** - Select the published-package
directory. - Validate expected artifacts. - Copy/publish to the selected
physical path. - Do not copy secrets or development configuration. -
Validate `web.config` and artifact target framework.

**Screen 4 --- SQL Server** - Request server/instance and database only
if final code confirms these are configurable values. - Show that
authentication is Windows Integrated Authentication. - Do not request a
SQL password. - Confirm the effective Windows identity and that the DBA
granted required permissions. - Configure the actual connection key
confirmed in code.

**Screen 5 --- Qumulo SMB** - Request UNC share root. - Request absolute
DPAPI credential-file path. - Request exact Domain and UserName supplied
by IT. - Request Password through secure input. - Show E1--E4
external-confirmation checklist. - Do not allow production to be marked
ready while required confirmations are missing.

**Screen 6 --- Secret directory and protection** - Validate that the
file path is outside the IIS site, served directory, repository, and
package. - Create the directory only after administrator confirmation. -
Disable inheritance. - Apply contractual ACL. - Verify effective ACL. -
Create DPAPI LocalMachine file with contractual entropy. - Clear
plaintext buffers as soon as practical. - Verify encrypted file exists
and the pool can read it.

**Screen 7 --- IIS environment variables** - Configure
`ASPNETCORE_ENVIRONMENT=Production`. - Configure
`SharedStorage__RootPath`. - Configure
`SharedStorage__CredentialFile`. - Configure
`ConnectionStrings__PosterPrintRequest` only according to the actual
code/contract. - Never configure the Qumulo password as an environment
variable. - Display non-secret values and conceal sensitive data.

**Screen 8 --- Review and apply** - Show a summary of site, pool, paths,
bindings, variables, and ACL. - Identify which values came from the
administrator and which confirmations came from IT. - Require explicit
confirmation. - Apply changes in order. - If a step fails, report the
failed operation without revealing sensitive values and stop. - Never
present a partially configured state as successful.

**Screen 9 --- Recycle and validate** - Recycle the selected Application
Pool. - Check startup. - Check `/health/ready`. - Run authorized
tests. - Generate a report without secrets, sensitive paths, or account
names. - Clearly distinguish `Installed`, `Configured`,
`Connectivity verified`, and `Production acceptance pending/complete`.

### 15.3 Data the installer must collect

  ---------------------------------------------------------------------------------------
  Data                            Source              Sensitivity       Status
  ------------------------------- ------------------- ----------------- -----------------
  IIS site name                   Administrator       Internal          Pending

  Application Pool name           Administrator       Internal          Required for ACL

  Application Pool identity       Administrator/IIS   Internal          Pending
                                  standard                              

  Application physical path       Administrator       Internal          Pending

  Binding/host/port/certificate   Administrator/PKI   Internal          Pending

  Qumulo UNC root                 IT                  Operationally     Required
                                                      sensitive         

  DPAPI file path                 Administrator       Operationally     Required
                                                      sensitive         

  Domain                          IT/Qumulo           Sensitive         Required

  UserName                        IT/Qumulo           Sensitive         Required

  Password                        IT/Qumulo           Secret            Required, secure
                                                                        input

  SQL server/database             DBA                 Internal          Confirm against
                                                                        code

  SQL connection key              Code/contract       Internal          Confirm against
                                                                        code

  Environment                     Administrator       Internal          Production

  E1--E5 permissions/evidence     IT/administrator    Control evidence  Required for
                                                                        acceptance
  ---------------------------------------------------------------------------------------

------------------------------------------------------------------------

## 16. External confirmations E1--E5 and acceptance criteria

Implementation authorization does not depend on already having these
permissions; production acceptance does.

-   **E1:** IT confirms Share Read + Change and filesystem Modify,
    including Delete and Delete subfolders and files, on the root and
    everything beneath it.
-   **E2:** IT/Qumulo confirms the local account authenticates over SMB
    with NTLMv2 and provides the exact account-name form.
-   **E3:** IT confirms the IIS host's outbound NTLM policy allows
    authentication to the cluster.
-   **E4:** IT confirms TCP 445, SMB dialect, and signing/encryption
    requirements.
-   **E5:** the server administrator confirms the Application Pool name
    used in the §6 ACL and the credential-file location.

Production acceptance also requires the tests in Storage Access Contract
§14: application started in Qumulo-account mode, `/health/ready`
Healthy, application operations against a test poster, idempotent
migration, rotation drill, and logs free of prohibited information.

**Build PASS and Tests PASS are not acceptance.** Tests using mocks or a
local filesystem do not establish real SMB connectivity.

------------------------------------------------------------------------

## 17. Pending values that must not be invented

1.  Exact Windows Server version and institutional baseline.
2.  Final IIS site name.
3.  Final Application Pool name.
4.  Exact Application Pool identity.
5.  Application physical installation path.
6.  Binding, hostname, port, and HTTPS certificate.
7.  Actual Qumulo UNC share path.
8.  Approved DPAPI credential-file location.
9.  Domain and Qumulo local-account format.
10. E1--E5 confirmations.
11. SQL Server/instance and effective Windows principal.
12. Exact SQL configuration key and format in final code.
13. Installer distribution, signing, elevation, logging, and update
    model.
14. Which operations the installer may execute automatically and which
    it may only verify, according to IT policy.

Do not turn examples into defaults. These values must be provided by the
administrator/IT and validated against the code and contracts.

------------------------------------------------------------------------

## 18. References

### Project documents

-   `docs/Poster_Print_Request_System_Roadmap_v4_Inventory.docx` ---
    phase order and status authority.
-   `docs/Storage_Access_Contract_v1.docx` --- especially §§2--15.
-   `docs/Shared_Storage_Contract_v1.docx` --- especially §§3--10 and
    §12.
-   Printing Consumables Inventory Contract --- its own scope only.

### Official Microsoft documentation

-   [Host ASP.NET Core on Windows with IIS --- .NET
    10](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/iis/?view=aspnetcore-10.0)
-   [Publish an ASP.NET Core app to
    IIS](https://learn.microsoft.com/en-us/aspnet/core/tutorials/publish-to-iis)
-   [Configuration in ASP.NET Core --- .NET
    10](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/?view=aspnetcore-10.0)

------------------------------------------------------------------------

## 19. Installation execution log

Complete this section during the actual installation. Do not include
passwords, tokens, DPAPI file contents, or sensitive paths in the
distributed document.

  ---------------------------------------------------------------------------------------
  Date           Step              Responsible    Result         Evidence/institutional
                                   party                         record
  -------------- ----------------- -------------- -------------- ------------------------
  ---            IIS installed     ---            Pending        ---

  ---            Hosting Bundle    ---            Pending        ---
                 installed                                       

  ---            Site and          ---            Pending        ---
                 Application Pool                                
                 configured                                      

  ---            Published package ---            Pending        ---
                 deployed                                        

  ---            SQL Integrated    ---            Pending        ---
                 Authentication                                  
                 validated                                       

  ---            E1--E5 confirmed  ---            Pending        ---

  ---            DPAPI directory   ---            Pending        ---
                 and ACL created                                 

  ---            DPAPI file        ---            Pending        ---
                 provisioned                                     

  ---            IIS environment   ---            Pending        ---
                 variables                                       
                 configured                                      

  ---            Application Pool  ---            Pending        ---
                 recycled                                        

  ---            `/health/ready`   ---            Pending        ---
                 validated                                       

  ---            SMB and           ---            Pending        ---
                 functional tests                                
                 executed                                        

  ---            Credential        ---            Pending        ---
                 rotation drill                                  

  ---            Production        ---            Pending        ---
                 acceptance                                      
  ---------------------------------------------------------------------------------------

**End of document.**
