<#
  Find-TdxAccountId.ps1
  Read-only lookup of TDX accounts/departments in the SANDBOX, to find the AccountID
  for tdx-validator.settings.json.

  - It only authenticates and searches accounts. It creates or changes nothing.
  - Credentials are requested with Get-Credential and kept in memory only.
  - The token is never printed or written to disk.
  - The sandbox base path is typed by you at run time (same value as in SandboxHost.BasePath,
    without host and without /api). Nothing real is stored in this script.
#>

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$hostName = 'help.uillinois.edu'
$basePath = (Read-Host 'Sandbox base path (example: /EJEMPLO/TDWebApi)').Trim().TrimEnd('/')
if (-not $basePath.StartsWith('/') -or $basePath -match '[<>?#]' -or $basePath -match '(^|/)\.\.?(/|$)') {
    Write-Host 'Invalid base path. It must start with / and contain no host, query, < > or dot segments.' -ForegroundColor Red
    return
}
$api = "https://$hostName$basePath/api"

$cred = Get-Credential -Message 'TDX SANDBOX account'
if (-not $cred) { Write-Host 'Cancelled.'; return }

$token = $null
try {
    # Authenticate (documented LoginParameters form)
    $loginBody = @{
        UserName = $cred.UserName
        Password = $cred.GetNetworkCredential().Password
    } | ConvertTo-Json
    $raw = Invoke-RestMethod -Method Post -Uri "$api/auth" -ContentType 'application/json' `
                             -Body $loginBody -MaximumRedirection 0
    $loginBody = $null
    $token = ([string]$raw).Trim().Trim('"')
    if (-not $token) { throw 'Authentication returned no token.' }
    Write-Host 'Authenticated.' -ForegroundColor Green

    $headers = @{ Authorization = "Bearer $token" }

    while ($true) {
        $text = Read-Host 'Text to search in account name/code (empty = exit)'
        if ([string]::IsNullOrWhiteSpace($text)) { break }

        $searchBody = @{ SearchText = $text; MaxResults = 50 } | ConvertTo-Json
        $accounts = Invoke-RestMethod -Method Post -Uri "$api/accounts/search" -Headers $headers `
                                      -ContentType 'application/json' -Body $searchBody -MaximumRedirection 0

        if (-not $accounts) {
            Write-Host 'No accounts found.' -ForegroundColor Yellow
        } else {
            $accounts | Select-Object ID, Name, ParentName, IsActive | Format-Table -AutoSize
        }
    }
}
catch {
    $status = $null
    if ($_.Exception.Response) { $status = [int]$_.Exception.Response.StatusCode }
    if ($status -eq 401 -or $status -eq 403) {
        Write-Host "HTTP $status : the account cannot authenticate or has no permission to read accounts. Ask the TDX team for the AccountID." -ForegroundColor Red
    } elseif ($status) {
        Write-Host "HTTP $status returned by TDX." -ForegroundColor Red
    } else {
        Write-Host ('Error: ' + $_.Exception.Message) -ForegroundColor Red
    }
}
finally {
    $token = $null; $headers = $null; $cred = $null; $loginBody = $null
}
