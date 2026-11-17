<#
  Test-TdxAuth.ps1
  Read-only diagnostic of TDX SANDBOX Web API authentication.

  - One authentication attempt per run (optionally a second one against /api/auth/login, only if you confirm).
  - Credentials are requested with Get-Credential and kept in memory only.
  - The token is NEVER printed: only its length and shape are shown.
  - On error, the HTTP status and TDX's own error text are shown.
  - It creates or changes nothing in TDX.
#>

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function Get-BodyText($content) {
    if ($null -eq $content) { return '' }
    if ($content -is [byte[]]) { return [Text.Encoding]::UTF8.GetString($content) }
    return [string]$content
}

function Test-Endpoint([string] $Uri, [string] $JsonBody) {
    Write-Host ''
    Write-Host "== POST $Uri" -ForegroundColor Cyan
    try {
        $r = Invoke-WebRequest -UseBasicParsing -Method Post -Uri $Uri -Body $JsonBody `
                               -ContentType 'application/json' -MaximumRedirection 0
        $text = (Get-BodyText $r.Content).Trim()
        $ct = $r.Headers['Content-Type']; if (-not $ct) { $ct = '[not provided]' }

        Write-Host "HTTP status:        $($r.StatusCode)"
        Write-Host "Content-Type:       $ct"
        Write-Host "Body length:        $($text.Length)"
        if ($text.Length -eq 0) {
            Write-Host 'Result: EMPTY body. This is not a token. Check that the path is the Web API.' -ForegroundColor Yellow
        } elseif ($text.StartsWith('"')) {
            Write-Host 'Result: token received as a JSON string (not shown).' -ForegroundColor Green
        } elseif ($text -notmatch '\s' -and $text.Split('.').Count -eq 3) {
            Write-Host 'Result: token received as plain text, JWT shape (not shown).' -ForegroundColor Green
        } else {
            Write-Host 'Result: unexpected body (not shown). Starts with: ' + $text.Substring(0, [Math]::Min(1, $text.Length)) -ForegroundColor Yellow
        }
        $text = $null; $r = $null
    }
    catch {
        $resp = $_.Exception.Response
        if ($resp) {
            Write-Host "HTTP status:        $([int]$resp.StatusCode) $($resp.StatusDescription)" -ForegroundColor Red
            try {
                $stream = $resp.GetResponseStream()
                if ($stream) {
                    $err = (New-Object IO.StreamReader($stream)).ReadToEnd()
                    if ($err) {
                        Write-Host 'TDX error text:' -ForegroundColor Red
                        Write-Host ($err.Substring(0, [Math]::Min(1500, $err.Length)))
                    } else {
                        Write-Host 'TDX error text: [empty]'
                    }
                }
            } catch { Write-Host 'TDX error text: [could not be read]' }
        } else {
            Write-Host ('Error (no HTTP response): ' + $_.Exception.Message) -ForegroundColor Red
        }
    }
}

# ---- Input ------------------------------------------------------------------------------------
$hostName = 'help.uillinois.edu'
$path = (Read-Host 'Sandbox Web API base path (example: /SBTDWebApi, without /api)').Trim().TrimEnd('/')
if (-not $path.StartsWith('/') -or $path -match '[<>?#]') {
    Write-Host 'Invalid path. It must start with / and contain no host, < > ? or #.' -ForegroundColor Red
    return
}
$api = "https://$hostName$path/api"

$cred = Get-Credential -Message 'TDX SANDBOX account (Web API)'
if (-not $cred) { Write-Host 'Cancelled.'; return }

$body = @{ UserName = $cred.UserName; Password = $cred.GetNetworkCredential().Password } | ConvertTo-Json

try {
    # ---- Attempt 1: /api/auth ------------------------------------------------------------------
    Test-Endpoint "$api/auth" $body

    # ---- Optional attempt 2: /api/auth/login (same documented request) -----------------------
    Write-Host ''
    $again = Read-Host 'Also try /api/auth/login? Repeated failed attempts may lock the account. (y/N)'
    if ($again -match '^[yYsS]') {
        Test-Endpoint "$api/auth/login" $body
    }
}
finally {
    $body = $null; $cred = $null
    Write-Host ''
    Write-Host 'Done. Nothing was created or changed. The token was not shown or saved.'
}
