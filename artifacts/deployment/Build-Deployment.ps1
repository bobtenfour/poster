<#
.SYNOPSIS
    Builds the PosterPrintRequest production deployment package (PHASE 12 + PHASE 13).

.DESCRIPTION
    Run from any folder on the build machine:
        powershell -ExecutionPolicy Bypass -File C:\projects\posters\artifacts\deployment\Build-Deployment.ps1

    Steps:
      1. Checks that the build inputs (src, tests, solution, global.json) are committed.
      2. Checks that the latest EF Core migration is the one the database scripts in this folder cover,
         and that dotnet ef generates the same statements as Poster.Database.Upgrade.sql.
      3. Runs the test suite (Release).
      4. Publishes the web application (Release, framework-dependent).
      5. Checks that the package contains no secret, connection string, Entra identifier or credential file.
      6. Writes PosterPrintRequest-Release-<commit>.zip and its SHA-256 file in this folder.

    The script changes no server, IIS site, database, Entra registration or source file.

.PARAMETER SkipTests
    Skips step 3. The package is then not release evidence.

.PARAMETER AllowUncommittedChanges
    Builds from a working tree with uncommitted build inputs. The package name gets the suffix -dirty.

.PARAMETER SkipDatabaseScriptCheck
    Skips the dotnet ef comparison of step 2 (for example when dotnet-ef is not installed).
#>
[CmdletBinding()]
param(
    [switch] $SkipTests,
    [switch] $AllowUncommittedChanges,
    [switch] $SkipDatabaseScriptCheck
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$ExpectedLatestMigration = '20261108191516_AddEntraObjectIdentifiers'
$PreviousMigration       = '20261023184022_RemoveApplicationManagedAuthentication'

$DeploymentDir = $PSScriptRoot
$RepoRoot      = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$WebProject    = Join-Path $RepoRoot 'src\PosterPrintRequest.Web\PosterPrintRequest.Web.csproj'
$InfraProject  = Join-Path $RepoRoot 'src\PosterPrintRequest.Infrastructure\PosterPrintRequest.Infrastructure.csproj'
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
        Write-Warning 'Building from uncommitted build inputs. The package is marked -dirty.'
        $suffix = '-dirty'
    }
    $packageName = "PosterPrintRequest-Release-$commit$suffix"
    Write-Host "  Package: $packageName"

    Step 'Database scripts'
    foreach ($name in 'Poster.Database.Install.sql', 'Poster.Database.Upgrade.sql') {
        $path = Join-Path $DeploymentDir $name
        if (-not (Test-Path -LiteralPath $path)) { throw "$name is missing from $DeploymentDir." }
        if (-not (Select-String -LiteralPath $path -SimpleMatch $ExpectedLatestMigration -Quiet)) {
            throw "$name does not contain migration $ExpectedLatestMigration."
        }
    }
    $migrationFiles = Get-ChildItem (Join-Path $RepoRoot 'src\PosterPrintRequest.Infrastructure\Persistence\Migrations') -Filter '2*.cs' |
        Where-Object { $_.Name -notlike '*.Designer.cs' } | Sort-Object Name
    $latest = [IO.Path]::GetFileNameWithoutExtension($migrationFiles[-1].Name)
    if ($latest -ne $ExpectedLatestMigration) {
        throw "Latest migration in source is $latest, but the database scripts cover $ExpectedLatestMigration. Regenerate the database scripts first."
    }
    Write-Host "  Latest migration: $latest"

    if ($SkipDatabaseScriptCheck) {
        Write-Warning 'dotnet ef comparison skipped.'
    } else {
        & dotnet ef --version *> $null
        if ($LASTEXITCODE -ne 0) { throw 'dotnet-ef is not available. Install it (dotnet tool install --global dotnet-ef --version 10.0.13) or run with -SkipDatabaseScriptCheck.' }
        $generated = Join-Path ([IO.Path]::GetTempPath()) "poster-ef-$([guid]::NewGuid().ToString('N')).sql"
        try {
            Invoke-Checked 'dotnet' @('ef', 'migrations', 'script', $PreviousMigration, $ExpectedLatestMigration,
                '--project', $InfraProject, '--startup-project', $WebProject, '--output', $generated)
            $normalize = { param($t) (($t -replace "`r", '') -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' }) }
            $genLines = & $normalize (Get-Content -LiteralPath $generated -Raw)
            $required = @(
                'ALTER TABLE [PosterRequests] ADD [SubmittedByObjectId] uniqueidentifier NULL;',
                'ALTER TABLE [PosterProcessings] ADD [ProcessedByObjectId] uniqueidentifier NULL;',
                "VALUES (N'$ExpectedLatestMigration', N'10.0.13');")
            $upgradeLines = & $normalize (Get-Content -LiteralPath (Join-Path $DeploymentDir 'Poster.Database.Upgrade.sql') -Raw)
            foreach ($line in $required) {
                if ($genLines -notcontains $line) { throw "dotnet ef output does not contain: $line" }
                if ($upgradeLines -notcontains $line) { throw "Poster.Database.Upgrade.sql does not contain: $line" }
            }
            $genStatements = $genLines | Where-Object { $_ -match '^(ALTER|CREATE|DROP|EXEC|INSERT)' }
            if (@($genStatements).Count -ne 3) {
                throw "dotnet ef generated $(@($genStatements).Count) schema statements for $ExpectedLatestMigration; the database scripts contain 3. Regenerate the database scripts."
            }
            Write-Host '  dotnet ef output matches the database scripts.'
        } finally {
            Remove-Item -LiteralPath $generated -ErrorAction SilentlyContinue
        }
    }

    if ($SkipTests) {
        Write-Warning 'Tests skipped. This package is not release evidence.'
    } else {
        Step 'Tests'
        Invoke-Checked 'dotnet' @('test', $Solution, '-c', 'Release')
    }

    Step 'Publish'
    $publishDir = Join-Path $DeploymentDir $packageName
    if (Test-Path -LiteralPath $publishDir) { throw "$publishDir already exists. Remove it or build another commit." }
    Invoke-Checked 'dotnet' @('publish', $WebProject, '-c', 'Release', '--self-contained', 'false', '-o', $publishDir)

    $devSettings = Join-Path $publishDir 'appsettings.Development.json'
    if (Test-Path -LiteralPath $devSettings) { Remove-Item -LiteralPath $devSettings }

    Step 'Package content checks'
    if (-not (Test-Path -LiteralPath (Join-Path $publishDir 'web.config'))) { throw 'web.config is missing from the publish output.' }
    $forbiddenFiles = Get-ChildItem -LiteralPath $publishDir -Recurse -File |
        Where-Object { ($_.Extension -in @('.pfx', '.key', '.pem', '.dpapi', '.sql')) -or (($_.Name -like '*secret*') -and ($_.Extension -ne '.dll')) }
    if ($forbiddenFiles) { $forbiddenFiles | ForEach-Object { Write-Host "  $($_.FullName)" }; throw 'The package contains a credential or database file.' }
    foreach ($settings in Get-ChildItem -LiteralPath $publishDir -Filter 'appsettings*.json') {
        $json = Get-Content -LiteralPath $settings.FullName -Raw | ConvertFrom-Json
        if ($json.PSObject.Properties.Name -contains 'ConnectionStrings') { throw "$($settings.Name) contains ConnectionStrings." }
        if ($json.PSObject.Properties.Name -contains 'Authentication') { throw "$($settings.Name) contains an Authentication section." }
        if (($json.PSObject.Properties.Name -contains 'SharedStorage') -and
            ($json.SharedStorage.PSObject.Properties.Name -contains 'CredentialFile')) { throw "$($settings.Name) contains SharedStorage:CredentialFile." }
        $raw = Get-Content -LiteralPath $settings.FullName -Raw
        if ($raw -match '(?i)password|secret|tenantid|clientid') { throw "$($settings.Name) contains a credential-like key." }
    }
    Write-Host '  No secret, connection string, Entra identifier or credential file in the package.'

    Step 'Archive'
    $zip = Join-Path $DeploymentDir "$packageName.zip"
    if (Test-Path -LiteralPath $zip) { throw "$zip already exists." }
    Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zip
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash
    Set-Content -LiteralPath "$zip.sha256" -Value "$hash  $packageName.zip" -Encoding ASCII

    Step 'Done'
    Write-Host "  Package : $zip"
    Write-Host "  SHA-256 : $hash"
    Write-Host "  Database: Poster.Database.Upgrade.sql (existing POSTER) or Poster.Database.Install.sql (new POSTER)"
    Write-Host "  Read DEPLOYMENT-README.txt before deploying."
}
finally {
    Pop-Location
}
