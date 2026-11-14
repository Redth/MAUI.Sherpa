[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9-]+/[A-Za-z0-9_.-]+$')]
    [string] $ReleaseRepository,

    [Parameter(Mandatory)]
    [ValidatePattern('^v\d+\.\d+\.\d+(?:\.\d+)?$')]
    [string] $ReleaseTag,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string] $ReleaseCommit,

    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string] $Publisher,

    [Parameter(Mandatory)]
    [string] $ArtifactDirectory,

    [switch] $ValidateOnly
)

$ErrorActionPreference = 'Stop'
$artifactRoot = [IO.Path]::GetFullPath($ArtifactDirectory)
if (-not (Test-Path $artifactRoot -PathType Container)) {
    throw "Artifact directory does not exist: $artifactRoot"
}

$parsedVersion = [version]$ReleaseTag.Substring(1)
$revision = [Math]::Max(0, $parsedVersion.Revision)
$packageVersion = [version]::new(
    $parsedVersion.Major,
    $parsedVersion.Minor,
    $parsedVersion.Build,
    $revision)

function Invoke-GhJson {
    param([Parameter(Mandatory)][string[]] $Arguments)

    $output = & gh @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "gh $($Arguments -join ' ') failed."
    }

    return $output | ConvertFrom-Json
}

function Get-Release {
    param([Parameter(Mandatory)][string] $Tag)

    $output = & gh api "repos/$ReleaseRepository/releases/tags/$Tag" 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    return $output | ConvertFrom-Json
}

function Get-ExpectedArtifact {
    param([Parameter(Mandatory)][string] $Name)

    $matches = @(Get-ChildItem $artifactRoot -Recurse -File -Filter $Name)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one $Name artifact; found $($matches.Count)."
    }

    return $matches[0]
}

function Assert-Package {
    param(
        [Parameter(Mandatory)][IO.FileInfo] $Package,
        [Parameter(Mandatory)][string] $Architecture
    )

    $archive = [IO.Compression.ZipFile]::OpenRead($Package.FullName)
    try {
        $manifestEntry = $archive.GetEntry('AppxManifest.xml')
        if (-not $manifestEntry) {
            throw "$($Package.Name) does not contain AppxManifest.xml."
        }

        $reader = [IO.StreamReader]::new($manifestEntry.Open())
        try {
            [xml]$manifest = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        if ($manifest.Package.Identity.Name -cne 'codes.redth.mauisherpa' -or
            $manifest.Package.Identity.Publisher -cne $Publisher -or
            $manifest.Package.Identity.Version -cne $packageVersion.ToString() -or
            $manifest.Package.Identity.ProcessorArchitecture -cne $Architecture) {
            throw "$($Package.Name) has an unexpected package identity."
        }

        if (-not $archive.GetEntry('AppxSignature.p7x')) {
            throw "$($Package.Name) is not signed."
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-AppInstaller {
    param(
        [Parameter(Mandatory)][IO.FileInfo] $AppInstaller,
        [Parameter(Mandatory)][string] $Architecture
    )

    [xml]$document = Get-Content $AppInstaller.FullName -Raw
    $root = $document.DocumentElement
    $mainPackage = $root.SelectSingleNode("*[local-name()='MainPackage']")

    if ($root.LocalName -ne 'AppInstaller' -or
        $root.Version -cne $packageVersion.ToString() -or
        $root.Uri -cne "https://github.com/$ReleaseRepository/releases/download/windows-latest/$($AppInstaller.Name)" -or
        -not $mainPackage -or
        $mainPackage.Name -cne 'codes.redth.mauisherpa' -or
        $mainPackage.Publisher -cne $Publisher -or
        $mainPackage.Version -cne $packageVersion.ToString() -or
        $mainPackage.ProcessorArchitecture -cne $Architecture -or
        $mainPackage.Uri -cne "https://github.com/$ReleaseRepository/releases/download/$ReleaseTag/MAUI-Sherpa.win-$Architecture.msix") {
        throw "$($AppInstaller.Name) contains unexpected update metadata."
    }
}

function Test-ImmutableAsset {
    param(
        [Parameter(Mandatory)][pscustomobject] $Release,
        [Parameter(Mandatory)][IO.FileInfo] $Asset
    )

    $existing = @($Release.assets | Where-Object { $_.name -ceq $Asset.Name })
    if ($existing.Count -gt 1) {
        throw "Release $ReleaseTag contains duplicate $($Asset.Name) assets."
    }

    if ($existing.Count -eq 1) {
        $downloadDirectory = Join-Path ([IO.Path]::GetTempPath()) "maui-sherpa-release-$([guid]::NewGuid().ToString('N'))"
        New-Item -ItemType Directory -Path $downloadDirectory | Out-Null
        try {
            & gh release download $ReleaseTag --repo $ReleaseRepository `
                --pattern $Asset.Name --dir $downloadDirectory
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to download existing $($Asset.Name) for verification."
            }

            $existingPath = Join-Path $downloadDirectory $Asset.Name
            $existingHash = (Get-FileHash $existingPath -Algorithm SHA256).Hash
            $newHash = (Get-FileHash $Asset.FullName -Algorithm SHA256).Hash
            if ($existingHash -cne $newHash) {
                throw "Release $ReleaseTag already contains a different $($Asset.Name)."
            }

            Write-Host "$($Asset.Name) is already published with the expected SHA256."
            return $false
        }
        finally {
            Remove-Item $downloadDirectory -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    return $true
}

$packages = @{}
$appInstallers = @{}
foreach ($architecture in @('x64', 'arm64')) {
    $package = Get-ExpectedArtifact "MAUI-Sherpa.win-$architecture.msix"
    $appInstaller = Get-ExpectedArtifact "MAUI-Sherpa.win-$architecture.appinstaller"
    Assert-Package $package $architecture
    Assert-AppInstaller $appInstaller $architecture
    $packages[$architecture] = $package
    $appInstallers[$architecture] = $appInstaller
}

if ($ValidateOnly) {
    Write-Host "Validated signed $packageVersion MSIX and App Installer artifacts."
    return
}

if ([string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
    throw 'GH_TOKEN is required to publish release assets.'
}

$release = Get-Release $ReleaseTag
if (-not $release -or $release.draft -or $release.prerelease -or $release.tag_name -cne $ReleaseTag) {
    throw "$ReleaseTag must be an existing published stable release."
}

$assetsToUpload = [Collections.Generic.List[IO.FileInfo]]::new()
foreach ($architecture in @('x64', 'arm64')) {
    foreach ($asset in @($packages[$architecture], $appInstallers[$architecture])) {
        if (Test-ImmutableAsset $release $asset) {
            $assetsToUpload.Add($asset)
        }
    }
}

if ($assetsToUpload.Count -gt 0) {
    $assetPaths = @($assetsToUpload | ForEach-Object { $_.FullName })
    & gh release upload $ReleaseTag @assetPaths --repo $ReleaseRepository
    if ($LASTEXITCODE -ne 0) {
        $currentRelease = Get-Release $ReleaseTag
        foreach ($asset in $assetsToUpload) {
            $uploaded = @($currentRelease.assets | Where-Object { $_.name -ceq $asset.Name })
            foreach ($item in $uploaded) {
                & gh api --method DELETE "repos/$ReleaseRepository/releases/assets/$($item.id)" --silent
            }
        }
        throw "Failed to publish all immutable MSIX assets to $ReleaseTag; partial uploads were removed."
    }
}

$rollingTag = 'windows-latest'
$existingRef = & gh api "repos/$ReleaseRepository/git/ref/tags/$rollingTag" 2>$null
if ($LASTEXITCODE -eq 0) {
    & gh api --method PATCH "repos/$ReleaseRepository/git/refs/tags/$rollingTag" `
        -f "sha=$ReleaseCommit" -F 'force=true' --silent
}
else {
    & gh api --method POST "repos/$ReleaseRepository/git/refs" `
        -f "ref=refs/tags/$rollingTag" -f "sha=$ReleaseCommit" --silent
}
if ($LASTEXITCODE -ne 0) {
    throw "Failed to move $rollingTag to $ReleaseCommit."
}

$rollingRelease = Get-Release $rollingTag
$releaseName = 'MAUI Sherpa Windows stable channel'
$releaseNotes = "App Installer update channel for $ReleaseTag. Use the versioned release for immutable MSIX packages and release notes."
if ($rollingRelease) {
    $rollingRelease = Invoke-GhJson -Arguments @(
        'api', '--method', 'PATCH',
        "repos/$ReleaseRepository/releases/$($rollingRelease.id)",
        '-f', "target_commitish=$ReleaseCommit",
        '-f', "name=$releaseName",
        '-f', "body=$releaseNotes",
        '-F', 'draft=false',
        '-F', 'prerelease=false',
        '-f', 'make_latest=false'
    )
}
else {
    $rollingRelease = Invoke-GhJson -Arguments @(
        'api', '--method', 'POST',
        "repos/$ReleaseRepository/releases",
        '-f', "tag_name=$rollingTag",
        '-f', "target_commitish=$ReleaseCommit",
        '-f', "name=$releaseName",
        '-f', "body=$releaseNotes",
        '-F', 'draft=false',
        '-F', 'prerelease=false',
        '-f', 'make_latest=false'
    )
}

& gh release upload $rollingTag `
    $appInstallers['x64'].FullName $appInstallers['arm64'].FullName `
    --repo $ReleaseRepository --clobber
if ($LASTEXITCODE -ne 0) {
    throw "Failed to promote App Installer metadata to $rollingTag."
}

Write-Host "Published $ReleaseTag MSIX assets and promoted the $rollingTag channel."
