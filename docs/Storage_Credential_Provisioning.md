# Storage Credential Provisioning

Authority: `docs/Storage_Access_Contract_v1.docx`, sections 5, 6, 8 and 12.

This procedure is for the server administrator of the IIS host. It creates, protects, places and rotates the credential file that the application uses to reach the shared-storage root as the dedicated Qumulo account. Angle-bracket values are placeholders that you replace on the host. Do not write real values into this repository, a ticket that is copied into the repository, a script that is committed, or the deployment package.

## What the file is

- A UTF-8 JSON document with exactly the properties `Domain`, `UserName` and `Password`.
- Protected with DPAPI, scope `LocalMachine`, additional entropy equal to the UTF-8 bytes of `PosterPrintRequest.SharedStorage.v1`.
- Read once, at application start. The application does not reload it while running.
- Never in source control, never in the deployment package, never in configuration files, never in a plaintext environment variable.

The values for `Domain` and `UserName` (including the domain qualifier for a local Qumulo account) are the exact form IT confirms under contract section 13, item E2.

## Prerequisites

- Written confirmations E1 to E5 of contract section 13.
- The application-pool name of the PosterPrintRequest IIS application (E5).
- A folder for the file that is on the IIS host, outside the site's physical directory, outside any directory IIS serves, outside the repository and outside the deployment package.
- An elevated PowerShell session on the IIS host, run by an administrator.

## 1. Create the protected folder and file

The access control of this section is applied to the folder, and then to the file, before any credential content is written. The file is created empty, protected, and only then filled, so no other principal can read it at any moment.

Required result, and nothing else:

| Principal | Access |
|---|---|
| `NT AUTHORITY\SYSTEM` | Full control |
| `BUILTIN\Administrators` | Full control |
| `IIS AppPool\<application-pool-name>` | Read |

Inheritance is disabled on the file and on its folder. No other principal has access.

LocalMachine scope means that any process on the host that can read the file can decrypt it. This access control is the control that limits who can use the credential.

Step 1. Define the target, the access-control function, and protect the folder.

```powershell
$target = '<credential-file-path>'        # absolute path, outside every served directory
$folder = Split-Path -Parent $target

function Set-CredentialAcl([string] $Path) {
    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)                  # disable inheritance, remove inherited rules
    foreach ($rule in @($acl.Access)) { [void]$acl.RemoveAccessRule($rule) }

    $inherit = if ((Get-Item -LiteralPath $Path).PSIsContainer) { 'ContainerInherit,ObjectInherit' } else { 'None' }
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule('NT AUTHORITY\SYSTEM',    'FullControl', $inherit, 'None', 'Allow')))
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule('BUILTIN\Administrators', 'FullControl', $inherit, 'None', 'Allow')))
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule('IIS AppPool\<application-pool-name>', 'Read', $inherit, 'None', 'Allow')))
    Set-Acl -LiteralPath $Path -AclObject $acl
}

if (-not (Test-Path -LiteralPath $folder)) { [void](New-Item -ItemType Directory -Path $folder) }
Set-CredentialAcl $folder
```

Step 2. Create the file empty and protect it. Nothing sensitive exists in it yet.

```powershell
if (-not (Test-Path -LiteralPath $target)) { [void](New-Item -ItemType File -Path $target) }
Set-CredentialAcl $target
```

Step 3. Only now write the protected content. The password is typed into a secure prompt and is never written to disk in plaintext or placed in the command line.

```powershell
Add-Type -AssemblyName System.Security

$domain   = Read-Host 'Domain (exact form confirmed by IT)'
$user     = Read-Host 'User name'
$secure   = Read-Host 'Password' -AsSecureString

$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    $json     = @{ Domain = $domain; UserName = $user; Password = $password } | ConvertTo-Json -Compress
    $entropy  = [Text.Encoding]::UTF8.GetBytes('PosterPrintRequest.SharedStorage.v1')
    $protected = [Security.Cryptography.ProtectedData]::Protect(
        [Text.Encoding]::UTF8.GetBytes($json),
        $entropy,
        [Security.Cryptography.DataProtectionScope]::LocalMachine)
    [IO.File]::WriteAllBytes($target, $protected)     # overwrites content, keeps the access control
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    Remove-Variable password, json, secure, user, domain -ErrorAction SilentlyContinue
}
```

Use the same scope (`LocalMachine`) and the same entropy exactly. A file protected with another scope or entropy cannot be decrypted by the application and the application will not start.

## 2. Check the access control

Check the result with `Get-Acl -LiteralPath <path> | Format-List` for both the file and the folder: exactly three entries, no inherited entries.

## 3. Place the file

Set the absolute path of the file as the environment variable `SharedStorage__CredentialFile` on the PosterPrintRequest IIS application, in the same way as `ConnectionStrings__PosterPrintRequest`. The variable holds the path only, never the credential.

`SharedStorage__RootPath` is supplied the same way. When `SharedStorage__CredentialFile` is not set, the application uses the identity of the process; in Production with a UNC root that configuration is refused at start.

Recycle the application pool, then check `/health/ready`.

## 4. Startup refusals

The application does not start, and reports the condition without a credential, path or account name, when:

- `SharedStorage__CredentialFile` is set on a host that is not Windows;
- the file is missing, unreadable, cannot be decrypted, or lacks a domain, user name or password;
- the environment is Production, the root is a UNC path, and no credential file is configured.

An unreachable share at start does not stop the application. It is reported by `/health/ready` and by the failing operations.

## 5. Permissions required of the account

Share permission to read and change, and file-system Modify, including Delete and Delete subfolders and files, on the shared-storage root and everything beneath it. Change permissions and Take ownership are not required. Read and Write without Delete are not sufficient: draft replacement, acceptance, the Printed transition, its reversal and the startup migration fail.

## 6. Rotation

Rotate every 12 months, and immediately when exposure is suspected. Do it in a low-use window.

1. IT sets the new password on the Qumulo account.
2. Create the new credential file beside the current one, with a different file name in the same folder, following section 1 in its order: the folder already has the access control; set `$target` to the new name, create the file empty, apply the access control to it (step 2), and only then write the protected content (step 3).
3. Check the access control of the new file (section 2 of this document) before it is renamed over the current one.
4. Replace the current file with the new one in a single rename (`Move-Item -LiteralPath <new-file> -Destination <credential-file-path> -Force`), then confirm that the access control of the replaced file still matches section 1.
5. Recycle the application pool.
6. Check `/health/ready` and download one poster.
7. Delete any copy of the previous file, and record the rotation in the institutional change record. Do not record it in the repository.

Between the password change and the recycle, storage operations fail as storage failures. Every poster stays in an authorized resting pair, so no partial state remains.

## 7. What never to do

- Do not put the credential, the file, or its real path in source control, the deployment package, `appsettings` files, logs, tickets that are mirrored into the repository, or a plaintext environment variable.
- Do not grant the file to any principal other than those in section 1.
- Do not change the scope or the entropy.
- Do not expect the application to pick up a changed file without an application-pool recycle.
