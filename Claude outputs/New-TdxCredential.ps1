<#
    PosterPrintRequest - creates the protected directory and the DPAPI file of the TDX credential (sandbox).
    Authority: TDX Integration Contract section 9.2; docs/Tdx_Credential_Provisioning.md.

    Run in an elevated Windows PowerShell (5.1) on the IIS host:
        .\New-TdxCredential.ps1 -Target '<absolute path>\Secrets\Tdx\tdx-credential.bin' -AppPool '<application-pool-name>'

    - The user name and the password are typed into prompts. The password prompt is masked.
      Nothing is written to disk in plain text, nothing goes on the command line, nothing is echoed.
    - DPAPI scope LocalMachine, entropy PosterPrintRequest.Tdx.v1 (exactly what the application expects).
    - Access: BUILTIN\Administrators full control, IIS AppPool\<AppPool> read. No inheritance, nobody else.
    - If the file already exists it is replaced in a single rename, keeping the access control.
    - The application reads the file once, at start (like Qumulo): recycle the application pool after creating or changing it.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Target,
    [Parameter(Mandatory = $true)] [string] $AppPool
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Security

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script in an elevated PowerShell (Run as administrator).'
}

if (-not [IO.Path]::IsPathRooted($Target)) { throw 'Target must be an absolute path.' }
$folder = Split-Path -Parent $Target
$poolAccount = "IIS AppPool\$AppPool"

# The application pool identity must exist, otherwise the ACL would name nobody.
try { [void](New-Object Security.Principal.NTAccount($poolAccount)).Translate([Security.Principal.SecurityIdentifier]) }
catch { throw "The application pool '$AppPool' was not found on this host." }

function Set-TdxAcl([string] $Path) {
    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($rule in @($acl.Access)) { [void]$acl.RemoveAccessRule($rule) }
    $inherit = if ((Get-Item -LiteralPath $Path).PSIsContainer) { 'ContainerInherit,ObjectInherit' } else { 'None' }
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule('BUILTIN\Administrators', 'FullControl', $inherit, 'None', 'Allow')))
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($poolAccount, 'Read', $inherit, 'None', 'Allow')))
    Set-Acl -LiteralPath $Path -AclObject $acl
}

# 1. Directory, protected before anything sensitive exists.
if (-not (Test-Path -LiteralPath $folder)) { [void](New-Item -ItemType Directory -Path $folder) }
Set-TdxAcl $folder

# 2. A new empty file next to the target, protected before it is filled.
$temp = Join-Path $folder ('tdx-' + [guid]::NewGuid().ToString('N') + '.new')
[void](New-Item -ItemType File -Path $temp)
Set-TdxAcl $temp

# 3. Credential typed now, protected immediately.
$user = Read-Host 'TDX service-account user name'
if ([string]::IsNullOrWhiteSpace($user)) { Remove-Item -LiteralPath $temp -Force; throw 'The user name is empty.' }
$secure = Read-Host 'TDX service-account password' -AsSecureString

$bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
$plainBytes = $null
try {
    $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    if ([string]::IsNullOrEmpty($plain)) { throw 'The password is empty.' }
    $json = [pscustomobject][ordered]@{ UserName = $user.Trim(); Password = $plain } | ConvertTo-Json -Compress
    $plainBytes = [Text.Encoding]::UTF8.GetBytes($json)
    $protected = [Security.Cryptography.ProtectedData]::Protect(
        $plainBytes,
        [Text.Encoding]::UTF8.GetBytes('PosterPrintRequest.Tdx.v1'),
        [Security.Cryptography.DataProtectionScope]::LocalMachine)
    [IO.File]::WriteAllBytes($temp, $protected)
}
catch {
    if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force }
    throw
}
finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    if ($plainBytes) { [Array]::Clear($plainBytes, 0, $plainBytes.Length) }
    Remove-Variable plain, json, secure -ErrorAction SilentlyContinue
}

# 4. Check that it decrypts and has both properties, without showing anything.
$check = [Security.Cryptography.ProtectedData]::Unprotect(
    [IO.File]::ReadAllBytes($temp),
    [Text.Encoding]::UTF8.GetBytes('PosterPrintRequest.Tdx.v1'),
    [Security.Cryptography.DataProtectionScope]::LocalMachine)
try {
    $parsed = [Text.Encoding]::UTF8.GetString($check) | ConvertFrom-Json
    if (-not $parsed.UserName -or -not $parsed.Password) { throw 'The protected file does not contain UserName and Password.' }
}
finally {
    [Array]::Clear($check, 0, $check.Length)
    Remove-Variable parsed -ErrorAction SilentlyContinue
}

# 5. Put it in place in a single rename, then re-apply and show the access control.
Move-Item -LiteralPath $temp -Destination $Target -Force
Set-TdxAcl $Target

Write-Host ''
Write-Host 'TDX credential file created and verified.'
foreach ($path in @($folder, $Target)) {
    Write-Host "Access control of: $path"
    (Get-Acl -LiteralPath $path).Access |
        Select-Object IdentityReference, FileSystemRights, IsInherited |
        Format-Table -AutoSize | Out-String | Write-Host
}
Write-Host 'Next: set Tdx__CredentialFile to this path in the IIS application, then recycle the application pool. The application reads the file only at start.'
