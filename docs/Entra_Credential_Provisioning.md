# Entra Credential Provisioning

Authority: `docs/Authentication_Contract_v1.docx`, sections 5.4, 6, 10 and 13.

This procedure is for the server administrator of the IIS host. It creates, protects, places and rotates the file that holds the Microsoft Entra client secret the application uses in Production. Angle-bracket values are placeholders that you replace on the host. Do not write real values into this repository, a ticket that is copied into the repository, a script that is committed, or the deployment package.

## What the file is

- The protected bytes of the UTF-8 **Client Secret Value** and nothing else. It is the Secret Value, never the Secret ID. There is no JSON, no header and no trailing line break.
- Protected with DPAPI, scope `LocalMachine`, additional entropy equal to the UTF-8 bytes of `PosterPrintRequest.Authentication.v1`. This entropy differs from the one that protects the Qumulo storage credential.
- Read once, at application start. The application does not reload it while running.
- Kept apart from the Qumulo credential: a separate file, in a separate directory (`EntraID`, under the logical `Secrets` structure of the host), with separate access controls.
- Never in source control, never in the deployment package, never in configuration files, never in an environment variable, never in logs.

The secret is rotated every 12 months.

## Prerequisites

- The written confirmations of contract section 13, items A1, A5, A6 and A7: the tenant and client identifiers, the creation and expiry of the client secret, the application-pool name, and the location of the `EntraID` directory.
- The Secret Value, handed to you by IT through a controlled channel. You type it into the secure prompt of step 3 and nowhere else. If the channel delivered it in a ticket, a chat or an e-mail, ask IT to remove it from there after you have used it.
- A folder for the file that is on the IIS host, outside the site's physical directory, outside any directory IIS serves, outside the repository and outside the deployment package.
- An elevated PowerShell session on the IIS host, run by an administrator.

## 1. Create the protected directory and file

The access control of this section is applied to the directory, and then to the file, before any secret content exists. The file is created empty, protected, and only then filled, so no other principal can read it at any moment.

Required result, and nothing else:

| Principal | Access |
|---|---|
| `BUILTIN\Administrators` | Full control |
| `IIS AppPool\<application-pool-name>` | Read |

Inheritance is disabled on the file and on its directory. No other principal has access.

LocalMachine scope means that any process on the host that can read the file can decrypt it. This access control is the control that limits who can use the secret.

Step 1. Define the target and the access-control function, and protect the directory.

```powershell
$target = '<entra-secret-file-path>'      # absolute path, in the EntraID directory, outside every served directory
$folder = Split-Path -Parent $target

function Set-EntraAcl([string] $Path) {
    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)                  # disable inheritance, remove inherited rules
    foreach ($rule in @($acl.Access)) { [void]$acl.RemoveAccessRule($rule) }

    $inherit = if ((Get-Item -LiteralPath $Path).PSIsContainer) { 'ContainerInherit,ObjectInherit' } else { 'None' }
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule('BUILTIN\Administrators', 'FullControl', $inherit, 'None', 'Allow')))
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule('IIS AppPool\<application-pool-name>', 'Read', $inherit, 'None', 'Allow')))
    Set-Acl -LiteralPath $Path -AclObject $acl
}

if (-not (Test-Path -LiteralPath $folder)) { [void](New-Item -ItemType Directory -Path $folder) }
Set-EntraAcl $folder
```

Step 2. Create the file empty and protect it. Nothing sensitive exists in it yet.

```powershell
if (-not (Test-Path -LiteralPath $target)) { [void](New-Item -ItemType File -Path $target) }
Set-EntraAcl $target
```

Step 3. Only now accept the secret and protect it immediately. The Secret Value is typed into a secure prompt. It is never written to disk in plaintext, never placed in a command line, never stored in an environment variable, and `Read-Host` input is not kept in the PowerShell history.

```powershell
Add-Type -AssemblyName System.Security

$secure = Read-Host 'Entra client Secret Value' -AsSecureString

$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    $entropy = [Text.Encoding]::UTF8.GetBytes('PosterPrintRequest.Authentication.v1')
    $protected = [Security.Cryptography.ProtectedData]::Protect(
        [Text.Encoding]::UTF8.GetBytes($plain),
        $entropy,
        [Security.Cryptography.DataProtectionScope]::LocalMachine)
    [IO.File]::WriteAllBytes($target, $protected)     # overwrites content, keeps the access control
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    Remove-Variable plain, secure -ErrorAction SilentlyContinue
}
```

Use the same scope (`LocalMachine`) and the same entropy exactly. A file protected with another scope or entropy cannot be decrypted by the application and the application will not start. Do not use the Qumulo entropy.

## 2. Check the access control

Check the result with `Get-Acl -LiteralPath <path> | Format-List` for both the file and the directory: exactly two entries, no inherited entries.

## 3. Configure the IIS application

Set these three values as application configuration of the PosterPrintRequest IIS application, in the same way as `ConnectionStrings__PosterPrintRequest`:

| Name | Value |
|---|---|
| `Authentication__TenantId` | the tenant identifier confirmed under item A1 |
| `Authentication__ClientId` | the application (client) identifier confirmed under item A1 |
| `Authentication__ClientSecretFile` | the absolute path of the file; a path only, never the secret |

Windows Authentication stays disabled and Anonymous Authentication stays enabled for the application. Recycle the application pool, then sign in once.

## 4. Startup refusals

In Production the application does not start, and reports the condition without a secret, a path or an account name, when:

- `Authentication__TenantId` or `Authentication__ClientId` is not configured or is not valid;
- `Authentication__ClientSecretFile` is not configured;
- the file is missing;
- the file cannot be read;
- the file cannot be decrypted (wrong scope, wrong entropy, or damaged content);
- the file or the secret in it is empty.

## 5. Rotation

Rotate every 12 months, and immediately when exposure is suspected. Do it in a low-use window.

1. IT creates the new client secret in Entra and hands you its Secret Value through a controlled channel.
2. Create the new file beside the current one, with a different file name in the same directory, following section 1 in its order: the directory already has the access control; set `$target` to the new name, create the file empty, apply the access control to it (step 2), and only then protect the new Secret Value (step 3).
3. Check the access control of the new file (section 2 of this document) before it is renamed over the current one.
4. Replace the current file with the new one in a single rename (`Move-Item -LiteralPath <new-file> -Destination <entra-secret-file-path> -Force`), then confirm that the access control of the replaced file still matches section 1.
5. Recycle the application pool. The application does not reload the secret while running.
6. Sign in and confirm that the application accepts the new secret.
7. Tell IT to delete the old client secret in Entra, and delete any copy of the previous file.
8. Record the rotation in the institutional change record. Do not record it in the repository.

Between the recycle and a successful sign-in, a failed sign-in is the sign of a wrong secret. Do not delete the old secret in Entra until the new one has worked.

## 6. What never to do

- Do not put the secret, the file or its real path in source control, the deployment package, `appsettings` files, logs, tickets that are mirrored into the repository, or an environment variable.
- Do not use the Secret ID in place of the Secret Value.
- Do not grant the file to any principal other than those in section 1.
- Do not change the scope or the entropy.
- Do not share the file, its directory or its access control with the Qumulo credential.
- Do not expect the application to pick up a changed file without an application-pool recycle.
- Do not use this procedure or any secret file in Development. Development is a public client and uses no secret.
