# TDX Credential Provisioning (sandbox)

Authority: `docs/TDX_Integration_Contract_v1.docx`, sections 9 and 10.

This procedure is for the server administrator of the IIS host. It creates, protects and places the file that holds the TDX service-account credential the application uses to create tickets in the **TDX sandbox**. Production is a later phase (contract section 3.3). Angle-bracket values are placeholders that you replace on the host. Do not write real values into this repository, a ticket that is copied into the repository, a script that is committed, or the deployment package.

## What the file is

- The protected bytes of a UTF-8 JSON object with exactly two properties, `UserName` and `Password`, of the TDX service account. Example of the shape only: `{"UserName":"<service-account>","Password":"<password>"}`.
- Protected with DPAPI, scope `LocalMachine`, additional entropy equal to the UTF-8 bytes of `PosterPrintRequest.Tdx.v1`. This entropy differs from the Entra and Qumulo entropies.
- Read by the application before each ticket attempt. A replaced file is used from the next attempt; no recycle is needed.
- Kept apart from the other credentials: a separate file, in a separate directory (`Tdx`, under the logical `Secrets` structure of the host), with its own access control.
- Never in source control, never in the deployment package, never in configuration files, never in an environment variable, never in logs.

## Prerequisites

- The service-account user name and password, handed to you through a controlled channel. You type them into the secure prompts of step 3 and nowhere else.
- A folder for the file on the IIS host, outside the site's physical directory, outside any directory IIS serves, outside the repository and outside the deployment package.
- An elevated PowerShell session on the IIS host, run by an administrator.

## 1. Create the protected directory and file

Required result, and nothing else:

| Principal | Access |
|---|---|
| `BUILTIN\Administrators` | Full control |
| `IIS AppPool\<application-pool-name>` | Read |

Inheritance is disabled on the file and on its directory. LocalMachine scope means that any process on the host that can read the file can decrypt it; this access control is what limits who can use the credential.

Step 1. Define the target and the access-control function, and protect the directory.

```powershell
$target = '<tdx-credential-file-path>'      # absolute path, in the Tdx directory, outside every served directory
$folder = Split-Path -Parent $target

function Set-TdxAcl([string] $Path) {
    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($rule in @($acl.Access)) { [void]$acl.RemoveAccessRule($rule) }

    $inherit = if ((Get-Item -LiteralPath $Path).PSIsContainer) { 'ContainerInherit,ObjectInherit' } else { 'None' }
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule('BUILTIN\Administrators', 'FullControl', $inherit, 'None', 'Allow')))
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule('IIS AppPool\<application-pool-name>', 'Read', $inherit, 'None', 'Allow')))
    Set-Acl -LiteralPath $Path -AclObject $acl
}

if (-not (Test-Path -LiteralPath $folder)) { [void](New-Item -ItemType Directory -Path $folder) }
Set-TdxAcl $folder
```

Step 2. Create the file empty and protect it.

```powershell
if (-not (Test-Path -LiteralPath $target)) { [void](New-Item -ItemType File -Path $target) }
Set-TdxAcl $target
```

Step 3. Only now type the credential and protect it immediately. It is never written to disk in plaintext, never placed in a command line and never stored in an environment variable.

```powershell
Add-Type -AssemblyName System.Security

$user   = Read-Host 'TDX service-account user name'
$secure = Read-Host 'TDX service-account password' -AsSecureString

$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    $json  = [pscustomobject][ordered]@{ UserName = $user; Password = $plain } | ConvertTo-Json -Compress
    $protected = [Security.Cryptography.ProtectedData]::Protect(
        [Text.Encoding]::UTF8.GetBytes($json),
        [Text.Encoding]::UTF8.GetBytes('PosterPrintRequest.Tdx.v1'),
        [Security.Cryptography.DataProtectionScope]::LocalMachine)
    [IO.File]::WriteAllBytes($target, $protected)
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    Remove-Variable plain, secure, json -ErrorAction SilentlyContinue
}
```

## 2. Check the access control

`Get-Acl -LiteralPath <path> | Format-List` for the file and the directory: exactly two entries, no inherited entries.

## 3. Configure the IIS application

Set these values as environment variables of the PosterPrintRequest IIS application at the ApplicationHost level, like the other application settings. The identifiers come from the sandbox; none of them is written in the repository.

| Name | Value |
|---|---|
| `Tdx__BaseUrl` | `https://<sandbox-host>/<sandbox-web-api-path>` (without `/api`) |
| `Tdx__ApplicationId` | `<ticketing-application-id>` |
| `Tdx__FormId` | `<form-id>` |
| `Tdx__TypeId` | `<type-id>` |
| `Tdx__StatusId` | `<status-id>` |
| `Tdx__PriorityId` | `<priority-id>` |
| `Tdx__AccountId` | `<fallback-account-id>` |
| `Tdx__ResponsibleGroupId` | `<responsible-group-id>` |
| `Tdx__SourceId` | optional, `<source-id>` |
| `Tdx__LookupAttempts` | optional, 1 to 10, default 3 |
| `Tdx__LookupIntervalSeconds` | optional, 1 to 120, default 10 |
| `Tdx__CredentialFile` | the absolute path of the file; a path only, never the credential |

Recycle the application pool once after the configuration is set.

## 4. When something is missing

The application always starts and accepts posters. If the configuration is incomplete, ticket creation is disabled and the log names the missing keys (no values). If the credential file is missing, unreadable, cannot be decrypted or lacks `UserName` and `Password`, tickets stay pending and the log names the condition without the path or the content. Tickets are created once the problem is fixed.

## 5. What never to do

- Do not put the credential, the file or its real path in source control, the deployment package, `appsettings` files, logs or an environment variable.
- Do not change the scope or the entropy.
- Do not share the file, its directory or its access control with the Entra or Qumulo credential.
- Do not point `Tdx__BaseUrl` at production in this phase.
