# ============================================================
# TDX SANDBOX - AUTHENTICATION AND RESPONSE DIAGNOSTICS
# Windows PowerShell 5.1
# Authentication only. No ticket operations.
# ============================================================

$ErrorActionPreference = "Stop"

# Replace with the official TDX Sandbox API base path.
$BaseUrl = "https://help.uillinois.edu/<TDX_SANDBOX_API_PATH>"

$Response = $null
$Credential = $null
$Token = $null
$Payload = $null
$RequestObject = $null
$ResponseText = $null
$Content = $null
$StatusCode = $null
$ContentType = $null

try {
    # --------------------------------------------------------
    # 1. Validate URL
    # --------------------------------------------------------

    $BaseUri = [System.Uri]$BaseUrl

    if (
        -not $BaseUri.IsAbsoluteUri -or
        $BaseUri.Scheme -ne "https" -or
        $BaseUri.Host -ine "help.uillinois.edu" -or
        $BaseUri.Port -ne 443 -or
        $BaseUri.UserInfo -ne "" -or
        $BaseUri.Query -ne "" -or
        $BaseUri.Fragment -ne ""
    ) {
        throw "Invalid Sandbox URL. HTTPS, exact host and port 443 are required."
    }

    if (
        $BaseUri.AbsolutePath -match "<TDX_SANDBOX_API_PATH>|%2f|%5c|%2e|\\"
    ) {
        throw "Invalid or unreplaced Sandbox API path."
    }

    if ($BaseUri.AbsolutePath -match "(^|/)\.\.?(/|$)") {
        throw "Dot segments are prohibited."
    }

    $BasePath = $BaseUri.AbsolutePath.TrimEnd("/")
    $AuthUrl = "https://help.uillinois.edu$BasePath/api/auth"

    Write-Host ""
    Write-Host "TDX SANDBOX AUTHENTICATION"
    Write-Host "Endpoint: $AuthUrl"
    Write-Host ""

    # --------------------------------------------------------
    # 2. Request credentials
    # --------------------------------------------------------

    $Credential = Get-Credential -Message "Enter TDX Sandbox credentials"

    if ($null -eq $Credential) {
        throw "Authentication cancelled."
    }

    $RequestObject = @{
        UserName = $Credential.UserName
        Password = $Credential.GetNetworkCredential().Password
    }

    $Payload = ConvertTo-Json -InputObject $RequestObject -Compress

    # --------------------------------------------------------
    # 3. Send POST request
    # --------------------------------------------------------

    Write-Host "Sending authentication request..."

    $Response = Invoke-WebRequest `
        -Uri $AuthUrl `
        -Method POST `
        -ContentType "application/json" `
        -Body $Payload `
        -UseBasicParsing `
        -MaximumRedirection 0 `
        -ErrorAction Stop

    $StatusCode = [int]$Response.StatusCode

    if ($null -ne $Response.Headers["Content-Type"]) {
        $ContentType = [string]$Response.Headers["Content-Type"]
    }

    # --------------------------------------------------------
    # 4. Inspect HTTP response metadata
    # --------------------------------------------------------

    Write-Host ""
    Write-Host "========== TDX RESPONSE DIAGNOSTICS =========="

    Write-Host "HTTP Status: $StatusCode"

    if ([string]::IsNullOrWhiteSpace($ContentType)) {
        Write-Host "Content-Type: [NOT PROVIDED]"
    }
    else {
        Write-Host "Content-Type: $ContentType"
    }

    Write-Host "Content object type: $($Response.Content.GetType().FullName)"

    if ($null -ne $Response.Content) {
        Write-Host "Content length: $($Response.Content.Length)"
    }
    else {
        Write-Host "Content length: 0"
    }

    Write-Host "Raw content length: $($Response.RawContentLength)"

    Write-Host ""
    Write-Host "Response headers:"

    foreach ($Header in $Response.Headers.GetEnumerator()) {
        if (
            $Header.Key -match "Authorization|Token|Cookie|Set-Cookie"
        ) {
            Write-Host "$($Header.Key): [REDACTED]"
        }
        else {
            Write-Host "$($Header.Key): $($Header.Value)"
        }
    }

    Write-Host ""
    Write-Host "Raw response available: $($null -ne $Response.RawContent)"
    Write-Host "=============================================="

    # --------------------------------------------------------
    # 5. Validate HTTP result
    # --------------------------------------------------------

    if ($StatusCode -ge 300 -and $StatusCode -lt 400) {
        throw "HTTP redirect rejected."
    }

    if ($StatusCode -lt 200 -or $StatusCode -ge 300) {
        throw "Authentication returned HTTP $StatusCode."
    }

    # --------------------------------------------------------
    # 6. Extract response text
    # --------------------------------------------------------

    $Content = $Response.Content

    if ($Content -is [byte[]]) {
        $ResponseText = [System.Text.Encoding]::UTF8.GetString($Content)
    }
    else {
        $ResponseText = [string]$Content
    }

    $ResponseText = $ResponseText.Trim()

    if ([string]::IsNullOrWhiteSpace($ResponseText)) {
        throw "HTTP request succeeded, but authentication response body is empty."
    }

    # --------------------------------------------------------
    # 7. Extract token (same rule as the validator: decided by the body)
    #    - starts with a quote -> JSON string
    #    - otherwise a single token without spaces -> plain token
    # --------------------------------------------------------

    if ($ResponseText.StartsWith('"')) {
        $ParsedResponse = ConvertFrom-Json -InputObject $ResponseText
        if ($ParsedResponse -isnot [string]) {
            throw "Response looked like a JSON string but could not be read."
        }
        $Token = $ParsedResponse.Trim()
        $TokenFormat = "JSON string"
    }
    elseif ($ResponseText -notmatch '\s' -and $ResponseText -notmatch '[<>{\[]') {
        $Token = $ResponseText
        $TokenFormat = "plain token"
    }
    else {
        throw "Response was neither a JSON string nor a plain token."
    }

    if ([string]::IsNullOrWhiteSpace($Token)) {
        throw "No authentication token was extracted."
    }

    # --------------------------------------------------------
    # 8. Report result
    # --------------------------------------------------------

    Write-Host ""
    Write-Host "========================================"
    Write-Host "AUTHENTICATION SUCCESSFUL"
    Write-Host "========================================"
    Write-Host "HTTP Status: $StatusCode"
    Write-Host "Token format: $TokenFormat"
    Write-Host "Token received: YES"
    Write-Host "Token value: [REDACTED]"

}
catch {
    Write-Host ""
    Write-Host "========================================"
    Write-Host "AUTHENTICATION FAILED"
    Write-Host "========================================"
    Write-Host "Error: $($_.Exception.Message)"

    $ErrResponse = $_.Exception.Response
    if ($null -ne $ErrResponse) {
        try {
            Write-Host "Exception HTTP Status: $([int]$ErrResponse.StatusCode) $($ErrResponse.StatusDescription)"
            $Stream = $ErrResponse.GetResponseStream()
            if ($null -ne $Stream) {
                $ErrText = (New-Object System.IO.StreamReader($Stream)).ReadToEnd()
                if ([string]::IsNullOrWhiteSpace($ErrText)) {
                    Write-Host "TDX error text: [empty]"
                }
                else {
                    Write-Host "TDX error text:"
                    Write-Host $ErrText.Substring(0, [Math]::Min(1500, $ErrText.Length))
                }
            }
        }
        catch {
            Write-Host "TDX error text: [could not be read]"
        }
    }
}
finally {
    # Clear sensitive variables.
    $Token = $null
    $Payload = $null
    $RequestObject = $null
    $ResponseText = $null
    $Content = $null
    $Credential = $null
    $ParsedResponse = $null
    $Response = $null
    $ErrText = $null
    $ErrResponse = $null
}

