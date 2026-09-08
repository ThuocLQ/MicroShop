[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Require-Path {
    param([string]$Path)

    if (-not (Test-Path $Path)) {
        throw "Required repository path is missing: $Path"
    }
}

function Get-RepositoryFiles {
    param(
        [string]$Filter,
        [switch]$ExcludeHistoricalMaterial
    )

    $excludedDirectories = '(?:\.git|bin|obj|node_modules)'
    if ($ExcludeHistoricalMaterial) {
        $excludedDirectories = '(?:\.git|bin|obj|node_modules|GiaoAn)'
    }

    Get-ChildItem -Path $root -File -Recurse -Filter $Filter |
        Where-Object { $_.FullName -notmatch "[\\/]$excludedDirectories[\\/]" }
}

function Find-RepositoryText {
    param(
        [string]$Pattern,
        [string]$Filter = '*',
        [switch]$ExcludeHistoricalMaterial
    )

    $files = @(Get-RepositoryFiles -Filter $Filter -ExcludeHistoricalMaterial:$ExcludeHistoricalMaterial)
    if ($files.Count -eq 0) {
        return @()
    }

    @(Select-String -Path $files.FullName -Pattern $Pattern | ForEach-Object {
        "$($_.Path):$($_.LineNumber):$($_.Line.Trim())"
    })
}

Require-Path 'Gateways/ApiGateway/ApiGateway.csproj'
Require-Path 'Workers/NotificationWorker/NotificationWorker.csproj'
Require-Path 'Workers/ProjectionWorker/ProjectionWorker.csproj'
Require-Path 'Directory.Build.props'
Require-Path 'Directory.Packages.props'

$legacyReferences = Find-RepositoryText `
    -Pattern 'Services[\\/]ApiGateway|Services[\\/]NotificationWorker' `
    -ExcludeHistoricalMaterial

if ($legacyReferences.Count -gt 0) {
    throw "Legacy deployable-unit paths are still referenced outside historical learning material:`n$legacyReferences"
}

$duplicatedProjectDefaults = Find-RepositoryText -Pattern '<Nullable>enable</Nullable>|<ImplicitUsings>enable</ImplicitUsings>' -Filter '*.csproj'
if ($duplicatedProjectDefaults.Count -gt 0) {
    throw "Compiler defaults belong in Directory.Build.props, not individual projects:`n$duplicatedProjectDefaults"
}

$trackedDatabaseArtifacts = git ls-files -- 'Services/**/*.db' 'Services/**/*.sqlite' 'Workers/**/*.db' 'Workers/**/*.sqlite'
if ($trackedDatabaseArtifacts) {
    throw "Local database artifacts must not be tracked:`n$trackedDatabaseArtifacts"
}

[xml]$centralPackages = Get-Content 'Directory.Packages.props' -Raw
$centralPackageIds = @($centralPackages.Project.ItemGroup.PackageVersion | ForEach-Object { $_.Include })

$missingCentralVersions = [System.Collections.Generic.List[string]]::new()
$inlineVersions = [System.Collections.Generic.List[string]]::new()

Get-RepositoryFiles -Filter '*.csproj' | ForEach-Object {
    [xml]$project = Get-Content $_.FullName -Raw
    @($project.Project.ItemGroup.PackageReference) | Where-Object { $_ } | ForEach-Object {
        if ($_.Version) {
            $inlineVersions.Add("$_ -> $($_.Include)")
        }

        if ($_.Include -and $_.Include -notin $centralPackageIds) {
            $missingCentralVersions.Add("$_ -> $($_.Include)")
        }
    }
}

if ($inlineVersions.Count -gt 0) {
    throw "Package versions must be centralized in Directory.Packages.props:`n$($inlineVersions -join "`n")"
}

if ($missingCentralVersions.Count -gt 0) {
    throw "Package references missing a central version:`n$($missingCentralVersions -join "`n")"
}

Write-Host 'Repository layout validation passed.' -ForegroundColor Green
