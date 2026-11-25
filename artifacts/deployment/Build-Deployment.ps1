<#
.SYNOPSIS
    Builds the PosterPrintRequest web release directory (web application only).

.DESCRIPTION
    Run from any folder on the build machine:
        powershell -ExecutionPolicy Bypass -File C:\projects\posters\artifacts\deployment\Build-Deployment.ps1

    Steps:
      1. Checks that the build inputs (src, tests, solution, global.json) are committed.
      2. Runs the test suite (Release).
      3. Publishes the web application (Release, framework-dependent) into a new directory.
      4. Removes web.config from the release, so the server's web.config and its IIS environment
         variables are never overwritten.
      5. Checks that the release directory contains no web.config, database script, secret, connection string,
         Entra identifier, TDX setting or credential file.

    The database is not part of this release: no SQL script is produced, read or checked.
    The script changes no server, IIS site, database, Entra registration or source file.

.PARAMETER SkipTests
    Skips step 2. The output is then not release evidence.

.PARAMETER AllowUncommittedChanges
    Builds from a working tree with uncommitted build inputs. The release directory name gets the suffix -dirty.
#>
[CmdletBinding()]
param(
    [switch] $SkipTests,
    [switch] $AllowUncommittedChanges
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$DeploymentDir = $PSScriptRoot
$RepoRoot      = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$WebProject    = Join-Path $RepoRoot 'src\PosterPrintRequest.Web\PosterPrintRequest.Web.csproj'
$Solution      = Join-Path $RepoRoot 'PosterPrintRequest.sln'

function Step([string] $Text) { Write-Host ''; Write-Host "== $Text" -ForegroundColor Cyan }
function Invoke-Checked([string] $File, [string[]] $Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

Push-Location $RepoRoot
try {
    Step 'Tools'
    Invoke-Checked 'dotnet' @('--version')
    Invoke-Checked 'git' @('--version')

    Step 'Source state'
    $commit = (& git rev-parse --short HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Not a Git repository.' }
    $dirty = & git status --porcelain -- src tests PosterPrintRequest.sln global.json
    $suffix = ''
    if ($dirty) {
        if (-not $AllowUncommittedChanges) {
            $dirty | ForEach-Object { Write-Host "  $_" }
            throw 'Build inputs have uncommitted changes. Commit them, or run with -AllowUncommittedChanges.'
        }
        Write-Warning 'Building from uncommitted build inputs. The release directory is marked -dirty.'
        $suffix = '-dirty'
    }
    $releaseName = "PosterPrintRequest-Release-$commit$suffix"
    Write-Host "  Release directory: $releaseName"

    if ($SkipTests) {
        Write-Warning 'Tests skipped. This output is not release evidence.'
    } else {
        Step 'Tests'
        Invoke-Checked 'dotnet' @('test', $Solution, '-c', 'Release')
    }

    Step 'Publish'
    $publishDir = Join-Path $DeploymentDir $releaseName
    if (Test-Path -LiteralPath $publishDir) { throw "$publishDir already exists. Remove it or build another commit." }
    Invoke-Checked 'dotnet' @('publish', $WebProject, '-c', 'Release', '--self-contained', 'false', '-o', $publishDir)

    $devSettings = Join-Path $publishDir 'appsettings.Development.json'
    if (Test-Path -LiteralPath $devSettings) { Remove-Item -LiteralPath $devSettings }

    # The server's web.config holds the IIS environment variables (connection string, storage, Entra, TDX).
    # The release never carries a web.config, so copying it can never overwrite the server's.
    $webConfig = Join-Path $publishDir 'web.config'
    if (-not (Test-Path -LiteralPath $webConfig)) { throw 'web.config is missing from the publish output.' }
    Remove-Item -LiteralPath $webConfig

    Step 'Release directory content checks'
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir 'PosterPrintRequest.Web.dll'))) { throw 'PosterPrintRequest.Web.dll is missing from the publish output.' }
    if (Test-Path -LiteralPath $webConfig) { throw 'The release directory contains web.config.' }
    $forbiddenFiles = Get-ChildItem -LiteralPath $publishDir -Recurse -File |
        Where-Object { ($_.Extension -in @('.pfx', '.key', '.pem', '.dpapi', '.bin', '.sql')) -or (($_.Name -like '*secret*') -and ($_.Extension -ne '.dll')) }
    if ($forbiddenFiles) { $forbiddenFiles | ForEach-Object { Write-Host "  $($_.FullName)" }; throw 'The release directory contains a database script or a credential file.' }
    foreach ($settings in Get-ChildItem -LiteralPath $publishDir -Filter 'appsettings*.json') {
        $json = Get-Content -LiteralPath $settings.FullName -Raw | ConvertFrom-Json
        $names = $json.PSObject.Properties.Name
        if ($names -contains 'ConnectionStrings') { throw "$($settings.Name) contains ConnectionStrings." }
        if ($names -contains 'Authentication') { throw "$($settings.Name) contains an Authentication section." }
        if ($names -contains 'Tdx') { throw "$($settings.Name) contains a Tdx section (TDX settings belong in IIS)." }
        if (($names -contains 'SharedStorage') -and
            ($json.SharedStorage.PSObject.Properties.Name -contains 'CredentialFile')) { throw "$($settings.Name) contains SharedStorage:CredentialFile." }
        $raw = Get-Content -LiteralPath $settings.FullName -Raw
        if ($raw -match '(?i)password|secret|tenantid|clientid|credentialfile') { throw "$($settings.Name) contains a credential-like key." }
    }
    Write-Host '  No database script, secret, connection string, Entra identifier, TDX setting or credential file.'

    Step 'Done'
    Write-Host "  Release directory: $publishDir"
    Write-Host '  Web application only. The database is not changed by this release.'
    Write-Host '  No web.config: the server keeps its own web.config and its environment variables.'
}
finally {
    Pop-Location
}
